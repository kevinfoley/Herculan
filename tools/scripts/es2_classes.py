#!/usr/bin/env python3
"""Dump the Borland class records (RTTI type descriptors) a DBSIM/VSHELL binary carries.

Each record names its class, so this recovers the authentic class names, with
the object size, every base and its subobject offset, and the vtable pointer's
offset in the object. The record layout and the vtable block that points back
at it are documented in Herculan/docs/retail/formats/borland-rtti.md.

`--vtables` resolves each record to its primary vtable through the record
pointer stored at vtable-0xc.

Usage:
    python tools/scripts/es2_classes.py
    python tools/scripts/es2_classes.py --binary VSHELL
    python tools/scripts/es2_classes.py --vtables --filter Gadget
    python tools/scripts/es2_classes.py --tree

This reads the shipped executable, not a Ghidra export, so it needs nothing to
have been analysed first. It reads Borland binaries only. ES1's Symantec C++
vtables open with a zero dword, not a pointer to a class record, and no record
has been found in ES1's images (Herculan/docs/es1/formats/compiler.md#vtables),
so it refuses them.
"""

from __future__ import annotations

import argparse
import re
import struct
import sys

import retail_binaries
from retail_binaries import Image

NAME = re.compile(rb"[A-Za-z_][A-Za-z0-9_ *<>,&]{2,60}\x00")
# Type mask -> the name offsets a record of that kind uses: 3 is a class, 1 a plain struct.
NAME_OFFSETS = {3: (0x20, 0x30), 1: (0x10,)}


def image(binary: str) -> Image:
    """The binary's image, refusing any binary not built by Borland C++."""
    return Image(retail_binaries.require(
        binary, "es2_classes.py", "it reads Borland class records, and ES1's vtables point to none",
        compiler=retail_binaries.BORLAND))


def scan(img: Image):
    """Every descriptor record in the image, keyed by virtual address."""
    out = {}
    d = img.data
    for m in re.finditer(b"[\x01\x03]\x00", d):
        rec = m.start() - 4
        if rec < 0 or rec + 0x60 > len(d):
            continue
        mask, name_off = struct.unpack_from("<HH", d, rec + 4)
        if name_off not in NAME_OFFSETS.get(mask, ()):
            continue
        size = struct.unpack_from("<I", d, rec)[0]
        # Objects run past 0x400 (ESGrid 0x6e8, HDDGauge 0x78a, Sheet 0x808).
        if not 1 <= size <= 0x4000:
            continue
        nm = NAME.match(d, rec + name_off, rec + name_off + 62)
        if not nm:
            continue
        va = img.va_of(rec)
        if va is None:
            continue
        name = nm.group(0)[:-1].decode("latin1")
        vptr = struct.unpack_from("<i", d, rec + 8)[0]
        # The base list: 12-byte {record, subobject offset, flags} entries ending in a zero dword.
        # A struct record has no base list: its name is where the list's offset would be.
        bases = []
        p = rec + struct.unpack_from("<H", d, rec + 0x10)[0] if mask == 3 else len(d)
        while p + 12 <= len(d) and len(bases) < 8:
            b, sub, _flags = struct.unpack_from("<IIi", d, p)
            if b == 0:
                break
            bases.append((b, sub))
            p += 12
        dtor = struct.unpack_from("<I", d, rec + 0x28)[0] if name_off == 0x30 else 0
        opdel = struct.unpack_from("<I", d, rec + 0x14)[0] if mask == 3 else 0
        out[va] = {"name": name, "size": size, "vptr": vptr, "bases": bases,
                   "base": bases[0][0] if bases else 0, "dtor": dtor, "opdel": opdel}
    return out


def find_vtables(img: Image, recs):
    """record VA -> the primary vtable whose preceding dword points at it. A record whose +0x08 says
    the class has no vtable pointer is skipped: a hit for it is another record's field."""
    out = {}
    for va in recs:
        if recs[va]["vptr"] < 0:
            continue
        for m in re.finditer(re.escape(struct.pack("<I", va)), img.data):
            hit = img.va_of(m.start())
            if hit is None:
                continue
            # A vtable block stores the pointer 12 bytes ahead of the table, with
            # two zero dwords between. That pair is what tells a block apart from
            # another record's base-class field.
            if img.dword(hit + 4) == 0 and img.dword(hit + 8) == 0:
                out.setdefault(va, []).append(hit + 0xC)
    return out


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--binary", default="DBSIM", type=str.upper, choices=retail_binaries.IDS)
    ap.add_argument("--filter", help="only classes whose name contains this (case-insensitive)")
    ap.add_argument("--vtables", action="store_true", help="also resolve each class's primary vtable")
    ap.add_argument("--tree", action="store_true", help="print as an inheritance tree instead of a list")
    args = ap.parse_args()

    img = image(args.binary)
    recs = scan(img)
    vts = find_vtables(img, recs) if args.vtables else {}
    keep = [va for va, r in recs.items() if not args.filter or args.filter.lower() in r["name"].lower()]

    if args.tree:
        kids = {}
        for va in recs:
            kids.setdefault(recs[va]["base"] if recs[va]["base"] in recs else 0, []).append(va)
        shown = set()

        def walk(parent, depth):
            for va in sorted(kids.get(parent, []), key=lambda v: recs[v]["name"]):
                if va in shown:
                    continue
                shown.add(va)
                print("%s%-28s %08x size=0x%x" % ("  " * depth, recs[va]["name"], va, recs[va]["size"]))
                walk(va, depth + 1)

        walk(0, 0)
        return 0

    print("%d class record(s) in %s" % (len(keep), args.binary))
    for va in sorted(keep, key=lambda v: recs[v]["name"]):
        r = recs[va]
        base = ",".join(
            (recs[b]["name"] if b in recs else "%08x" % b) + ("@+0x%x" % sub if sub else "")
            for b, sub in r["bases"]) or "-"
        line = "%-28s %08x size=0x%-5x vptr=%-6s base=%s" % (
            r["name"], va, r["size"], ("+0x%x" % r["vptr"]) if r["vptr"] >= 0 else "none", base)
        if args.vtables:
            line += "  vtable=" + ",".join("%08x" % v for v in vts.get(va, [])) if va in vts else "  vtable=-"
        print(line)
    return 0


if __name__ == "__main__":
    sys.exit(main())
