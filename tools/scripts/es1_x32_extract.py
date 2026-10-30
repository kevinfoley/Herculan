"""Extract the 32-bit program image from an ES1 X-32 executable, and import it into Ghidra.

`DBSIM.EXE`, `GO.EXE` and `ESCD.EXE` are a real-mode stub, a 16-bit DOS-extender kernel and a flat
32-bit image, in the layout of Herculan/docs/es1/formats/x32-executable.md. The image runs at
offset 0 of its own segment, so its file bytes are its memory from address 0 up, and every address
the code uses is an offset into it. This writes those bytes and a layout JSON:

    <out>/<ID>               the image, file part only
    <out>/<ID>_layout.json   file offsets, memory size, entry point, the kernel's address table

The layouts were derived from two stub and kernel versions (`LAYOUTS`). The script picks a layout
by the stub's hash, checks the kernel instructions that layout relies on, and refuses a file that
differs, rather than extract from a layout it was never checked against.

    py tools/scripts/es1_x32_extract.py E:/ES2Stuff/ES1/DBSIM.EXE ES1DBSIM --out E:/ES2Stuff/tools/analysis_out
    py tools/scripts/es1_x32_extract.py E:/ES2Stuff/ES1/GO.EXE ES1GO --out E:/ES2Stuff/tools/analysis_out --import E:/ES2Stuff
    py tools/scripts/es1_x32_extract.py E:/ES2Stuff/ES1/ESCD.EXE ES1ESCD --out E:/ES2Stuff/tools/analysis_out --import E:/ES2Stuff

`--import ROOT` then imports the image into the `ES1Recon` project under ROOT/tools/ghidra_project
with Ghidra's raw-binary loader at base 0, `ES1SetupX32Image` before auto-analysis and
`ES1SeedFunctionStarts` after it. The compiler spec is Visual Studio's (`windows`), whose default
`__stdcall` takes each function's stack cleanup from its `ret N`, as Symantec C++'s C++ functions
need (Herculan/docs/es1/formats/compiler.md#calling-conventions). An existing program of the same name is kept unless `--overwrite` is given, which
replaces it and everything applied to it since.
"""
import argparse
import hashlib
import json
import os
import struct
import subprocess
import sys
from dataclasses import dataclass

REPO = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

@dataclass(frozen=True)
class Layout:
    """One stub and kernel version (Herculan/docs/es1/formats/x32-executable.md#escdexes-kernel).
    Offsets count from the kernel's load module, whose first paragraph is its data segment."""
    name: str
    stub_sha1: str            # the stub the loader reading below was derived from
    banner: bytes             # the kernel's copyright string, naming its version
    address_table: int        # seven dwords written by the linker: dword 5 the memory size, dword 6 the entry
    # The kernel also stores the memory size into the image as an immediate operand, `mov ebx,imm32`
    # (`66 BB`), which the extraction matches against the table's dword 5.
    stores_mem_size: bool

    @property
    def mem_size(self):
        return self.address_table + 0x14

    @property
    def entry(self):
        return self.address_table + 0x18

    def reads_mem_size(self):
        """`mov edx,[mem_size]`: the image read is capped at the memory size."""
        return bytes.fromhex("668b16") + struct.pack("<H", self.mem_size)


LAYOUTS = (
    Layout("1991-1994", "e869c4dbf647a1297107ac20b701ccd269595928",
           b"DOS extender Copyright 1991-1994 by Doug Huffman", 0x118, False),   # DBSIM.EXE, GO.EXE
    Layout("1991 1993", "ca8b125edaf95b1077a646ec1c0692e3cd2a1bcc",
           b"DOS extender Copyright 1991 1993 by Doug Huffman", 0xD8, True),     # ESCD.EXE
)

KERNEL_PARAS = 0x000      # word: kernel length in paragraphs; the image follows it


def mz_size(b, at):
    """Bytes an MZ header at `at` declares: whole pages plus the used part of the last one."""
    last, pages = struct.unpack_from("<HH", b, at + 2)
    return (pages - 1) * 512 + (last or 512)


def parse(path):
    b = open(path, "rb").read()
    if b[:2] != b"MZ":
        sys.exit(f"{path}: no MZ header")
    stub_len = mz_size(b, 0)
    stub_sha1 = hashlib.sha1(b[:stub_len]).hexdigest()
    lay = next((x for x in LAYOUTS if x.stub_sha1 == stub_sha1), None)
    if lay is None:
        sys.exit(f"{path}: the {stub_len}-byte stub is not one a layout was derived from")
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
    table = struct.unpack_from("<7I", b, load + lay.address_table)
    mem_size, = struct.unpack_from("<I", b, load + lay.mem_size)
    entry, = struct.unpack_from("<I", b, load + lay.entry)
    kernel = b[load:image_at]
    if lay.banner not in kernel:
        sys.exit(f"{path}: kernel lacks the {lay.name} banner its stub pairs with; layout unverified")
    if lay.reads_mem_size() not in kernel:
        sys.exit(f"{path}: kernel does not read the memory size at +0x{lay.mem_size:x}; layout unverified")
    if lay.stores_mem_size and b"\x66\xbb" + struct.pack("<I", mem_size) not in kernel:
        sys.exit(f"{path}: kernel does not store the memory size 0x{mem_size:x} into the image; layout unverified")
    handoff = b"\x66\x68" + struct.pack("<I", entry) + b"\x66\xcb"
    if handoff not in kernel:
        sys.exit(f"{path}: kernel has no 'push 0x{entry:x} / retf' hand-off matching the table entry")
    image_len = len(b) - image_at
    if mem_size < image_len or not entry < image_len:
        sys.exit(f"{path}: memory size 0x{mem_size:x} / entry 0x{entry:x} inconsistent with 0x{image_len:x} image bytes")
    relocs = [struct.unpack_from("<HH", b, k + reloc_at + 4 * i) for i in range(reloc_count)]
    layout = {
        "source": os.path.basename(path),
        "kernel_version": lay.name,
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
        "address_table_offset": lay.address_table,
        "address_table": [f"{v:08x}" for v in table],
    }
    return b[image_at:], layout


def ghidra_import(root, image_path, layout, overwrite):
    ghidra = os.path.join(root, "tools", "ghidra_12.1.2_PUBLIC", "support", "analyzeHeadless.bat")
    project = os.path.join(root, "tools", "ghidra_project")
    scripts = os.path.join(REPO, "tools", "ghidra_scripts")
    cmd = [ghidra, project, "ES1Recon", "-import", image_path,
           "-loader", "BinaryLoader", "-loader-baseAddr", "0x0", "-loader-blockName", "image",
           "-processor", "x86:LE:32:default", "-cspec", "windows",
           "-scriptPath", scripts,
           "-preScript", "ES1SetupX32Image", f"{layout['image_memory_size']:x}", f"{layout['entry']:x}",
           "-postScript", "ES1SeedFunctionStarts"]
    if overwrite:
        cmd.append("-overwrite")
    print(" ".join(cmd))
    return subprocess.call(cmd)


def main():
    ap = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    ap.add_argument("exe", help="DBSIM.EXE, GO.EXE or ESCD.EXE from the ES1 install")
    ap.add_argument("id", help="binary identifier: ES1DBSIM, ES1GO or ES1ESCD; names the output files and the Ghidra program")
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
