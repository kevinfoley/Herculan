"""Extract the 32-bit program image from an ES1 X-32 executable, and import it into Ghidra.

`DBSIM.EXE` and `GO.EXE` are a real-mode stub, a 16-bit DOS-extender kernel and a flat 32-bit
image, in the layout of Herculan/docs/es1/formats/x32-executable.md. The image runs at offset 0 of
its own segment, so its file bytes are its memory from address 0 up, and every address the code
uses is an offset into it. This writes those bytes and a layout JSON:

    <out>/<ID>               the image, file part only
    <out>/<ID>_layout.json   file offsets, memory size, entry point, the kernel's address table

The layout was derived from one stub and two kernels. The script checks the stub's hash and the
kernel instructions it relies on, and refuses a file that differs, rather than extract from a
layout it was never checked against.

    py tools/scripts/es1_x32_extract.py E:/ES2Stuff/ES1/DBSIM.EXE ES1DBSIM --out E:/ES2Stuff/tools/analysis_out
    py tools/scripts/es1_x32_extract.py E:/ES2Stuff/ES1/GO.EXE ES1GO --out E:/ES2Stuff/tools/analysis_out --import E:/ES2Stuff

`--import ROOT` then imports the image into the `ES1Recon` project under ROOT/tools/ghidra_project
with Ghidra's raw-binary loader at base 0, `ES1SetupX32Image` before auto-analysis and
`ES1SeedFunctionStarts` after it. The compiler spec is `gcc` until the compiler is identified (plan
step 0.4). An existing program of the same name is kept unless `--overwrite` is given, which
replaces it and everything applied to it since.
"""
import argparse
import hashlib
import json
import os
import struct
import subprocess
import sys

REPO = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

# The stub both retail executables open with; the loader reading below is derived from its code.
STUB_SHA1 = "e869c4dbf647a1297107ac20b701ccd269595928"

# Offsets into the kernel's load module (its first paragraph is the kernel's data segment).
KERNEL_PARAS = 0x000      # word: kernel length in paragraphs; the image follows it
ADDRESS_TABLE = 0x118     # seven dwords written by the linker, see the format doc
MEM_SIZE = 0x12C          # table dword 5: bytes of address space the image needs
ENTRY = 0x130             # table dword 6: entry point offset

# Instructions the extraction trusts, matched in the kernel code rather than assumed.
MOV_EDX_MEMSIZE = bytes.fromhex("668b162c01")   # mov edx,[012c]: the read is capped at MEM_SIZE


def mz_size(b, at):
    """Bytes an MZ header at `at` declares: whole pages plus the used part of the last one."""
    last, pages = struct.unpack_from("<HH", b, at + 2)
    return (pages - 1) * 512 + (last or 512)


def parse(path):
    b = open(path, "rb").read()
    if b[:2] != b"MZ":
        sys.exit(f"{path}: no MZ header")
    stub_len = mz_size(b, 0)
    if hashlib.sha1(b[:stub_len]).hexdigest() != STUB_SHA1:
        sys.exit(f"{path}: the {stub_len}-byte stub is not the one this layout was derived from")
    k = stub_len
    if b[k:k + 2] != b"MZ":
        sys.exit(f"{path}: no second MZ header at 0x{k:x}")
    if k + mz_size(b, k) != len(b):
        sys.exit(f"{path}: second MZ declares 0x{mz_size(b, k):x} bytes, file has 0x{len(b) - k:x} after the stub")
    hdr_paras, = struct.unpack_from("<H", b, k + 8)
    reloc_count, = struct.unpack_from("<H", b, k + 6)
    reloc_at, = struct.unpack_from("<H", b, k + 0x18)
    ip, cs = struct.unpack_from("<HH", b, k + 0x14)
    load = k + hdr_paras * 16
    kernel_paras, = struct.unpack_from("<H", b, load + KERNEL_PARAS)
    image_at = load + kernel_paras * 16
    if image_at >= len(b):
        sys.exit(f"{path}: kernel length 0x{kernel_paras:x} paragraphs runs past the end of the file")
    table = struct.unpack_from("<7I", b, load + ADDRESS_TABLE)
    mem_size, = struct.unpack_from("<I", b, load + MEM_SIZE)
    entry, = struct.unpack_from("<I", b, load + ENTRY)
    kernel = b[load:image_at]
    if MOV_EDX_MEMSIZE not in kernel:
        sys.exit(f"{path}: kernel does not read the memory size at +0x{MEM_SIZE:x}; layout unverified")
    handoff = b"\x66\x68" + struct.pack("<I", entry) + b"\x66\xcb"
    if handoff not in kernel:
        sys.exit(f"{path}: kernel has no 'push 0x{entry:x} / retf' hand-off matching the table entry")
    image_len = len(b) - image_at
    if mem_size < image_len or not entry < image_len:
        sys.exit(f"{path}: memory size 0x{mem_size:x} / entry 0x{entry:x} inconsistent with 0x{image_len:x} image bytes")
    relocs = [struct.unpack_from("<HH", b, k + reloc_at + 4 * i) for i in range(reloc_count)]
    layout = {
        "source": os.path.basename(path),
        "file_size": len(b),
        "stub_size": stub_len,
        "kernel_header_offset": k,
        "kernel_header_paragraphs": hdr_paras,
        "kernel_entry": f"{cs:04x}:{ip:04x}",
        "kernel_relocations": len(relocs),
        "kernel_offset": load,
        "kernel_size": kernel_paras * 16,
        "image_offset": image_at,
        "image_file_size": image_len,
        "image_memory_size": mem_size,
        "image_base": 0,
        "entry": entry,
        "address_table": [f"{v:08x}" for v in table],
    }
    return b[image_at:], layout


def ghidra_import(root, image_path, layout, overwrite):
    ghidra = os.path.join(root, "tools", "ghidra_12.1.2_PUBLIC", "support", "analyzeHeadless.bat")
    project = os.path.join(root, "tools", "ghidra_project")
    scripts = os.path.join(REPO, "tools", "ghidra_scripts")
    cmd = [ghidra, project, "ES1Recon", "-import", image_path,
           "-loader", "BinaryLoader", "-loader-baseAddr", "0x0", "-loader-blockName", "image",
           "-processor", "x86:LE:32:default", "-cspec", "gcc",
           "-scriptPath", scripts,
           "-preScript", "ES1SetupX32Image", f"{layout['image_memory_size']:x}", f"{layout['entry']:x}",
           "-postScript", "ES1SeedFunctionStarts"]
    if overwrite:
        cmd.append("-overwrite")
    print(" ".join(cmd))
    return subprocess.call(cmd)


def main():
    ap = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    ap.add_argument("exe", help="DBSIM.EXE or GO.EXE from the ES1 install")
    ap.add_argument("id", help="binary identifier: ES1DBSIM or ES1GO; names the output files and the Ghidra program")
    ap.add_argument("--out", default=os.path.join(REPO, "tools", "analysis_out"), help="output directory")
    ap.add_argument("--import", dest="import_root", metavar="ROOT",
                    help="checkout holding tools/ghidra_12.1.2_PUBLIC and tools/ghidra_project; imports into ES1Recon")
    ap.add_argument("--overwrite", action="store_true", help="with --import: replace an existing program of the same name")
    args = ap.parse_args()

    image, layout = parse(args.exe)
    os.makedirs(args.out, exist_ok=True)
    image_path = os.path.join(args.out, args.id)
    with open(image_path, "wb") as f:
        f.write(image)
    with open(image_path + "_layout.json", "w", newline="\n") as f:
        json.dump(layout, f, indent=2)
        f.write("\n")
    print(f"{args.id}: image 0x{layout['image_offset']:x}..0x{layout['file_size']:x} "
          f"(0x{layout['image_file_size']:x} bytes), memory 0x{layout['image_memory_size']:x}, "
          f"entry 0x{layout['entry']:x} -> {image_path}")
    if args.import_root:
        sys.exit(ghidra_import(args.import_root, image_path, layout, args.overwrite))


if __name__ == "__main__":
    main()
