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

Usage:
    python tools/scripts/es2_xref.py 0040ac3c
    python tools/scripts/es2_xref.py Grenade_Construct Rocket_TickUpdate
    python tools/scripts/es2_xref.py --binary VSHELL 004a1b2c
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

REPO = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
ANALYSIS = os.path.join(REPO, "tools", "analysis_out")

BINARIES = {
    "DBSIM": os.path.join(REPO, "ES2", "DBSIM.EXE"),
    "VSHELL": os.path.join(REPO, "ES2", "VSHELL.EXE"),
}

FUNC_LINE = re.compile(r"^; FUNCTION (\S+) @ ([0-9a-f]{8})")
ADDR_LINE = re.compile(r"^([0-9a-f]{8})\s{3}")
VTABLE_HEAD = re.compile(r"^=== (\S+).*@ ([0-9a-f]{8})")
VTABLE_SLOT = re.compile(r"^\s+\+0x([0-9a-f]+) \(([0-9a-f]{8})\): (\S+) = .*@ ([0-9a-f]{8})")


class Image:
    """A parsed PE, enough of one to map file offsets to virtual addresses."""

    def __init__(self, path: str):
        self.path = path
        self.data = open(path, "rb").read()
        lfanew = struct.unpack_from("<I", self.data, 0x3C)[0]
        if self.data[lfanew:lfanew + 4] != b"PE\0\0":
            raise SystemExit("%s is not a PE image" % path)
        sections = struct.unpack_from("<H", self.data, lfanew + 6)[0]
        opt_size = struct.unpack_from("<H", self.data, lfanew + 20)[0]
        self.base = struct.unpack_from("<I", self.data, lfanew + 24 + 28)[0]
        table = lfanew + 24 + opt_size
        self.sections = []
        for i in range(sections):
            off = table + i * 40
            name = self.data[off:off + 8].rstrip(b"\0").decode("latin1")
            vsize, va, raw_size, raw_off = struct.unpack_from("<IIII", self.data, off + 8)
            self.sections.append((name, self.base + va, vsize, raw_off, raw_size))

    def section_of_offset(self, offset: int):
        for name, va, vsize, raw_off, raw_size in self.sections:
            if raw_off <= offset < raw_off + raw_size:
                return name, va + (offset - raw_off)
        return None, None

    def executable(self):
        """The code sections. Named CODE by Borland, .text by everything else."""
        return [s for s in self.sections if s[0] in ("CODE", ".text")]


def rel32_sites(image: Image, target: int):
    """Every E8/E9 whose target is `target`. Opcode-aligned scanning would miss
    the ones Ghidra did not disassemble, so this brute-forces every offset."""
    out = []
    for _name, va, _vsize, raw_off, raw_size in image.executable():
        blob = image.data[raw_off:raw_off + raw_size]
        for i in range(len(blob) - 5):
            op = blob[i]
            if op not in (0xE8, 0xE9):
                continue
            rel = struct.unpack_from("<i", blob, i + 1)[0]
            site = va + i
            if site + 5 + rel == target:
                out.append((site, "CALL" if op == 0xE8 else "JMP"))
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
    path = os.path.join(ANALYSIS, "%s_disasm_full.txt" % binary)
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
    path = os.path.join(ANALYSIS, "%s_vtables_full.txt" % binary)
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
    ap.add_argument("--binary", default="DBSIM", choices=sorted(BINARIES), help="default DBSIM")
    ap.add_argument("--quiet", action="store_true", help="one summary line per target")
    args = ap.parse_args()

    path = BINARIES[args.binary]
    if not os.path.exists(path):
        raise SystemExit("no %s at %s" % (args.binary, path))

    image = Image(path)
    names, bounds = load_functions(args.binary)

    any_referenced = False
    for token in args.targets:
        target = resolve(token, args.binary)
        label = enclosing(names, bounds, target) or token

        calls = rel32_sites(image, target)
        dwords = dword_sites(image, target)
        slots = vtable_slots(args.binary, target)
        total = len(calls) + len(dwords)
        any_referenced |= total > 0

        print("%08x  %s" % (target, label))
        if not args.quiet:
            for site, kind in calls:
                print("    %-4s from %08x  %s" % (kind, site, enclosing(names, bounds, site) or ""))
            for offset, va, section in dwords:
                where = "%08x" % va if va else "(headers)"
                print("    dword at %s  section %s  raw %08x" % (where, section, offset))
            for vtable, slot, slot_name in slots:
                print("    vtable %s +0x%03x (%s)" % (vtable, slot, slot_name))

        if total == 0:
            print("    UNREFERENCED: no rel32 branch and no stored pointer anywhere in the image")
        else:
            print("    %d rel32 branch(es), %d stored pointer(s), %d vtable slot(s)"
                  % (len(calls), len(dwords), len(slots)))

    return 0 if any_referenced else 1


if __name__ == "__main__":
    sys.exit(main())
