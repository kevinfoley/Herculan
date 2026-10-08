#!/usr/bin/env python3
"""Answer "is this address referenced anywhere?" from the binary itself.

A Ghidra cross-reference list only shows what Ghidra managed to resolve, and a
text search of the decompilation only shows call sites it chose to render. Both
miss an address reached through a data table, and both are why "nothing calls
this" claims in the docs have been wrong before. This walks the whole PE image
instead:

  * every `E8`/`E9` rel32 whose computed target is the address, in any code
    section, whether or not Ghidra made a function there;
  * every naturally-aligned-or-not occurrence of the address as a little-endian
    dword, in any section, which is what a vtable, a jump table or a factory
    table would look like;
  * every vtable slot holding it, named, from the vtable dump.

A clean sweep across all three is the strongest available evidence that code is
unreachable. It is still a null result -- see docs rule 6 and
docs/retail/simulation/weapon-damage-types.md, "Type -- a firing-mechanism selector", for a
case where this is what settled the question.

The binaries come from tools/scripts/retail_binaries.py. An ES1 image has no
section table, so its whole image is scanned as code and as data. Symantec C++
code is also entered through the `0x90` padding before a prologue, so for an
ES1 binary a function's padding addresses are searched with its prologue, and
each hit names the one it reaches when that is not the address asked for.
An ES1 image sits at address 0, so its addresses are also small integers: a
stored-pointer hit there may be a constant that happens to equal the address
(ES1DBSIM holds the dword 0x3ab 14 times, and nothing branches to 0x3ab).

Usage:
    python tools/scripts/es2_xref.py 0040ac3c
    python tools/scripts/es2_xref.py Grenade_Construct Rocket_TickUpdate
    python tools/scripts/es2_xref.py --binary VSHELL 004a1b2c
    python tools/scripts/es2_xref.py --binary ES1DBSIM 000015a4
    python tools/scripts/es2_xref.py --quiet 0040ac3c   # summary lines only

Exit status is 1 if every address given is unreferenced, so it can gate a claim.
"""

from __future__ import annotations

import argparse
import os
import re
import struct
import sys

import es2_symbols
import retail_binaries
from retail_binaries import ANALYSIS, Image

FUNC_LINE = re.compile(r"^; FUNCTION (\S+) @ ([0-9a-f]{8})")
ADDR_LINE = re.compile(r"^([0-9a-f]{8})\s{3}")
VTABLE_HEAD = re.compile(r"^=== (\S+).*@ ([0-9a-f]{8})")
VTABLE_SLOT = re.compile(r"^\s+\+0x([0-9a-f]+) \(([0-9a-f]{8})\): (\S+) = .*@ ([0-9a-f]{8})")


def rel32_sites(image: Image, target: int):
    """Every E8/E9 whose target is `target`. Opcode-aligned scanning would miss
    the ones Ghidra did not disassemble, so this brute-forces every offset."""
    return [(site, kind) for site, kind, _hit in rel32_sites_any(image, {target})]


def rel32_sites_any(image: Image, targets):
    """Every E8/E9 whose target is in `targets`, as (site, CALL|JMP, target), in one pass."""
    out = []
    for _name, va, _vsize, raw_off, raw_size in image.executable():
        blob = image.data[raw_off:raw_off + raw_size]
        for i in range(len(blob) - 5):
            op = blob[i]
            if op not in (0xE8, 0xE9):
                continue
            rel = struct.unpack_from("<i", blob, i + 1)[0]
            site = va + i
            if site + 5 + rel in targets:
                out.append((site, "CALL" if op == 0xE8 else "JMP", site + 5 + rel))
    return out


def dword_sites(image: Image, target: int):
    """Every occurrence of the address as a stored pointer, in any section."""
    out = []
    needle = struct.pack("<I", target)
    i = image.data.find(needle)
    while i != -1:
        name, va = image.section_of_offset(i)
        out.append((i, va, name))
        i = image.data.find(needle, i + 1)
    return out


def load_functions(binary: str):
    """address -> function name, from the disassembly dump's FUNCTION headers."""
    path = retail_binaries.get(binary).dump("disasm_full.txt")
    if not os.path.exists(path):
        return {}, []
    names, bounds = {}, []
    with open(path, encoding="utf-8", errors="replace") as fh:
        for line in fh:
            m = FUNC_LINE.match(line)
            if m:
                addr = int(m.group(2), 16)
                names[addr] = m.group(1)
                bounds.append(addr)
                continue
    bounds.sort()
    return names, bounds


def enclosing(names, bounds, addr: int):
    """The function an address falls inside, by nearest preceding entry point."""
    lo, hi = 0, len(bounds)
    while lo < hi:
        mid = (lo + hi) // 2
        if bounds[mid] <= addr:
            lo = mid + 1
        else:
            hi = mid
    if lo == 0:
        return None
    start = bounds[lo - 1]
    return "%s+0x%x" % (names[start], addr - start) if addr != start else names[start]


def vtable_slots(binary: str, target: int):
    """Vtable slots holding the address, as (vtable, slot offset, slot name)."""
    path = retail_binaries.get(binary).dump("vtables_full.txt")
    if not os.path.exists(path):
        return []
    out, current = [], "?"
    want = "%08x" % target
    with open(path, encoding="utf-8", errors="replace") as fh:
        for line in fh:
            head = VTABLE_HEAD.match(line)
            if head:
                current = head.group(1)
                continue
            slot = VTABLE_SLOT.match(line)
            if slot and slot.group(4) == want:
                out.append((current, int(slot.group(1), 16), slot.group(3)))
    return out


def resolve(token: str, binary: str):
    """A bare hex address, or a name from the binary's known_symbols file."""
    if re.fullmatch(r"(0x)?[0-9a-fA-F]{6,8}", token):
        return int(token, 16)
    path = es2_symbols.path(binary)
    if not os.path.exists(path):
        raise SystemExit("cannot resolve %r: %s is missing" % (token, path))
    for row in es2_symbols.entries(binary):
        if row.get("name") == token:
            return int(row["address"], 16)
    raise SystemExit("no symbol named %r in %s" % (token, binary))


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("targets", nargs="+", help="hex addresses or known_symbols names")
    ap.add_argument("--binary", default="DBSIM", type=str.upper, choices=retail_binaries.IDS,
                    help="default DBSIM")
    ap.add_argument("--quiet", action="store_true", help="one summary line per target")
    args = ap.parse_args()

    image = Image(args.binary)
    names, bounds = load_functions(args.binary)
    if not names:
        print("(no %s: hits are not attributed to functions)"
              % os.path.relpath(retail_binaries.get(args.binary).dump("disasm_full.txt"), retail_binaries.ASSETS))
    if not os.path.exists(retail_binaries.get(args.binary).dump("vtables_full.txt")):
        print("(no vtable dump: vtable slots are among the stored pointers, unnamed)")

    any_referenced = False
    for token in args.targets:
        target = resolve(token, args.binary)
        label = enclosing(names, bounds, target) or token
        entries = image.entries_of(target)

        def via(hit: int) -> str:
            return "" if hit == target else "  (to %08x)" % hit

        calls = rel32_sites_any(image, set(entries))
        dwords = [(o, va, sec, hit) for hit in entries for o, va, sec in dword_sites(image, hit)]
        slots = [(vt, slot, name, hit) for hit in entries for vt, slot, name in vtable_slots(args.binary, hit)]
        total = len(calls) + len(dwords)
        any_referenced |= total > 0

        print("%08x  %s" % (target, label))
        if len(entries) > 1:
            print("    entered also at %s (padding falls through to the prologue)"
                  % ", ".join("%08x" % a for a in entries[1:]))
        if not args.quiet:
            for site, kind, hit in calls:
                print("    %-4s from %08x  %s%s" % (kind, site, enclosing(names, bounds, site) or "", via(hit)))
            for offset, va, section, hit in dwords:
                where = "%08x" % va if va is not None else "(headers)"
                print("    dword at %s  section %s  raw %08x%s" % (where, section, offset, via(hit)))
            for vtable, slot, slot_name, hit in slots:
                print("    vtable %s +0x%03x (%s)%s" % (vtable, slot, slot_name, via(hit)))

        if total == 0:
            print("    UNREFERENCED: no rel32 branch and no stored pointer anywhere in the image")
        else:
            print("    %d rel32 branch(es), %d stored pointer(s), %d vtable slot(s)"
                  % (len(calls), len(dwords), len(slots)))

    return 0 if any_referenced else 1


if __name__ == "__main__":
    sys.exit(main())
