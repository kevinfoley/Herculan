"""Extract a driver image from one of ES1's HMI driver containers, and import it into Ghidra.

`SOS\\HMIMDRV.386` (the MIDI drivers), `SOS\\HMIDRV.386` (the digital drivers) and `SOS\\HMIDET.386`
(the detection drivers) each hold a run of `.com` driver images, in the layout of
Herculan/docs/es1/formats/hmi-driver-container.md. An image is flat 32-bit code and data that runs
at offset 0 of its own segment, so its file bytes are its memory from address 0 up, and every
address the code uses is an offset into it. This writes those bytes and a layout JSON in the form
`retail_binaries.Image` reads:

    <out>/<ID>               the image
    <out>/<ID>_layout.json   where it came from, its device id, its size and entry

    py tools/scripts/es1_hmi_extract.py E:/ES2Stuff/ES1/SOS/HMIMDRV.386 --list
    py tools/scripts/es1_hmi_extract.py E:/ES2Stuff/ES1/SOS/HMIMDRV.386 fmmidi.com ES1FMMIDI --import E:/ES2Stuff --seed 504+699+69f+6a5+54a+5b0+610

The walk checks what the layout rests on and refuses a file that differs: the entries end exactly
at the file's end, each entry's length field is its image size plus 0x5c, and each image opens with
a 32-bit jump followed by its entry's device id.

`--import ROOT` imports the image into the `ES1Recon` project under ROOT/tools/ghidra_project with
Ghidra's raw-binary loader at base 0, as `es1_x32_extract.py` imports a program image. A driver is
hand-written assembly entered through pointers the image holds, which auto-analysis does not
follow, so `--seed` names further entry points (hex, `+`-separated) for it to start from; address 0,
the image's opening jump, is always one. An existing program of the same name is kept unless
`--overwrite` is given, which replaces it and everything applied to it since.
"""
import argparse
import json
import os
import struct
import subprocess
import sys

REPO = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

HEADER = 0x2C          # +0x20 the image count, +0x24 the first entry's offset
ENTRY = 0x30           # a 32-byte name, then four dwords: length, image size, device id, flags
LENGTH_OVER_SIZE = 0x5C


def walk(path):
    """Every entry of a container: (index, entry offset, name, image offset, size, device id, flags)."""
    b = open(path, "rb").read()
    if len(b) < HEADER:
        sys.exit(f"{path}: shorter than a container's header")
    count, first = struct.unpack_from("<II", b, 0x20)
    if first != HEADER:
        sys.exit(f"{path}: first entry at 0x{first:x}, not 0x{HEADER:x}; layout unverified")
    rows, at = [], first
    for i in range(count):
        if at + ENTRY > len(b):
            sys.exit(f"{path}: entry {i} at 0x{at:x} runs past the end of the file")
        name = b[at:at + 0x20].split(b"\0")[0].decode("latin1")
        length, size, device, flags = struct.unpack_from("<IIII", b, at + 0x20)
        image = at + ENTRY
        if length != size + LENGTH_OVER_SIZE:
            sys.exit(f"{path}: entry {i} ({name}) length 0x{length:x} is not its size 0x{size:x} + 0x5c; layout unverified")
        if image + size > len(b) or b[image] != 0xE9 or struct.unpack_from("<H", b, image + 5)[0] != device:
            sys.exit(f"{path}: entry {i} ({name}) image does not open with a jump and device id 0x{device:x}; layout unverified")
        rows.append((i, at, name, image, size, device, flags))
        at = image + size
    if at != len(b):
        sys.exit(f"{path}: the {count} entries end at 0x{at:x}, the file at 0x{len(b):x}; layout unverified")
    return b, rows


def ghidra_import(root, image_path, layout, seeds, overwrite):
    ghidra = os.path.join(root, "tools", "ghidra_12.1.2_PUBLIC", "support", "analyzeHeadless.bat")
    project = os.path.join(root, "tools", "ghidra_project")
    scripts = os.path.join(REPO, "tools", "ghidra_scripts")
    cmd = [ghidra, project, "ES1Recon", "-import", image_path,
           "-loader", "BinaryLoader", "-loader-baseAddr", "0x0", "-loader-blockName", "image",
           "-processor", "x86:LE:32:default", "-cspec", "windows",
           "-scriptPath", scripts,
           "-preScript", "ES1SetupX32Image", f"{layout['image_memory_size']:x}", f"{layout['entry']:x}",
           *[f"{s:x}" for s in seeds]]
    if overwrite:
        cmd.append("-overwrite")
    print(" ".join(cmd))
    return subprocess.call(cmd)


def main():
    ap = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    ap.add_argument("container", help="HMIMDRV.386, HMIDRV.386 or HMIDET.386 from the ES1 install's SOS folder")
    ap.add_argument("name", nargs="?", help="the image's name in the container, e.g. fmmidi.com")
    ap.add_argument("id", nargs="?", help="binary identifier, e.g. ES1FMMIDI; names the output files and the Ghidra program")
    ap.add_argument("--list", action="store_true", help="print the container's entries and stop")
    ap.add_argument("--out", default=os.path.join(REPO, "tools", "analysis_out"), help="output directory")
    ap.add_argument("--import", dest="import_root", metavar="ROOT",
                    help="checkout holding tools/ghidra_12.1.2_PUBLIC and tools/ghidra_project; imports into ES1Recon")
    ap.add_argument("--seed", default="", help="with --import: further entry points, hex, +-separated")
    ap.add_argument("--overwrite", action="store_true", help="with --import: replace an existing program of the same name")
    args = ap.parse_args()

    b, rows = walk(args.container)
    if args.list:
        for i, at, name, image, size, device, flags in rows:
            print(f"{i:3d}  entry {at:06x}  image {image:06x}  size {size:05x}  device {device:04x}  flags {flags:04x}  {name}")
        return
    if not args.name or not args.id:
        ap.error("give the image's name and an identifier, or --list")
    hits = [r for r in rows if r[2].lower() == args.name.lower()]
    if len(hits) != 1:
        sys.exit(f"{args.container}: {len(hits)} entries named {args.name}")
    i, at, name, image, size, device, flags = hits[0]
    seeds = [int(s, 16) for s in args.seed.split("+") if s]
    bad = [s for s in seeds if not s < size]
    if bad:
        sys.exit("seed outside the image: " + ", ".join(f"{s:x}" for s in bad))
    layout = {
        "source": os.path.basename(args.container),
        "entry_index": i,
        "entry_name": name,
        "entry_offset": at,
        "device_id": f"{device:04x}",
        "flags": f"{flags:04x}",
        "image_offset": image,
        "image_file_size": size,
        "image_memory_size": size,
        "image_base": 0,
        "entry": 0,
    }
    os.makedirs(args.out, exist_ok=True)
    image_path = os.path.join(args.out, args.id)
    with open(image_path, "wb") as f:
        f.write(b[image:image + size])
    with open(image_path + "_layout.json", "w", newline="\n") as f:
        json.dump(layout, f, indent=2)
        f.write("\n")
    print(f"{args.id}: {name}, entry {i} of {os.path.basename(args.container)}, image 0x{image:x}..0x{image + size:x} "
          f"(0x{size:x} bytes), device {device:04x} -> {image_path}")
    if args.import_root:
        sys.exit(ghidra_import(args.import_root, image_path, layout, seeds, args.overwrite))


if __name__ == "__main__":
    main()
