#!/usr/bin/env python3
"""Answer "what touches this struct field?" over the Ghidra text disassembly.

Grepping the disassembly for `+ 0x9e]` finds only the accesses that happen to
name the offset directly. Borland reaches a field three other ways, and all
three are common in DBSIM:

    LEA EAX,[EBX + 0x92]        ...  MOV byte ptr [EAX + 0xc],1     ; obj+0x9e
    ADD EBX,0x92                ...  MOV byte ptr [EBX + 0x19],1    ; obj+0xab
    LEA ECX,[EBX + 0x222]  MOV [EBP-4],ECX  MOV EAX,[EBP-4]  ...    ; the spill

This resolves all of them, so "no reader" means something. It is the tool for
checking a claim that a field is write-only or dead -- and for finding the
setter of a flag whose writer "could not be located".

**It runs two passes and unions them, and that is not optional.** Tracking a
register alias only within a basic block misses accesses that straddle one;
carrying aliases across blocks recovers those but goes wrong when a base
register picked up a stale alias on another path, which silently *skews the
computed offset* and drops real hits rather than failing loudly. That exact
false negative hid `Sim_RaycastObjectList`'s read of `obj+0xa2`. Neither pass
alone is sound; the union is what should be quoted.

Offsets are struct-relative, so a hit is only interesting once you have checked
that the base register really holds the object you mean -- the same `+0x38` is
a SimObject flag, a structure type record's field and a weapon-mount vtable
slot. Use --range to cut the search to the code that handles your class.

**Two kinds of access it cannot see, and neither shows as a warning:**
- Code Ghidra left undisassembled (`; ... N undefined bytes` in the dump; e.g.
  `Mech_BehaviourDriveOffThink`, `Flyer_Constructor`). Grep the range a claim
  depends on for that marker, and cross-check DBSIM_decomp_full.c, which covers
  those functions and folds the rebases back (`*(char *)((int)this + 0xb1)`).
- Bulk writes. Only `[reg + disp]` forms match, so a memset/memcpy over the
  field is invisible; sweep their call sites for destinations on your class.
A field holding a meaningful value is therefore never proven unread by this.

Usage:
    python tools/scripts/es2_fieldscan.py 9e
    python tools/scripts/es2_fieldscan.py 222 224 226 228 22a
    python tools/scripts/es2_fieldscan.py --range 00415000-00422000 a8 a9
    python tools/scripts/es2_fieldscan.py --writes-only --binary VSHELL 1c

Exit status is 1 when no access is found for any offset given.
"""

from __future__ import annotations

import argparse
import os
import re
import sys

REPO = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
ANALYSIS = os.path.join(REPO, "tools", "analysis_out")

LINE = re.compile(r"^([0-9a-f]{8})\s{3}([0-9a-f.]+)\s+(.*)$")
FUNC = re.compile(r"^; FUNCTION (\S+) @ ([0-9a-f]{8})")
MEM = re.compile(r"\[(E[A-Z][A-Z])(?:\s*\+\s*(-?0x[0-9a-f]+|\d+))?\]")
LEA = re.compile(r"^LEA\s+(E[A-Z][A-Z]),\[(E[A-Z][A-Z])\s*\+\s*(-?0x[0-9a-f]+)\]$")
REGMOV = re.compile(r"^MOV\s+(E[A-Z][A-Z]),(E[A-Z][A-Z])$")
ADDIMM = re.compile(r"^(ADD|SUB)\s+(E[A-Z][A-Z]),(-?0x[0-9a-f]+)$")
SPILL = re.compile(r"^MOV\s+dword ptr \[(E[BS]P \+ -?0x[0-9a-f]+)\],(E[A-Z][A-Z])$")
RELOAD = re.compile(r"^MOV\s+(E[A-Z][A-Z]),dword ptr \[(E[BS]P \+ -?0x[0-9a-f]+)\]$")

# Mnemonics whose FIRST operand is the destination.
WRITERS = {
    "MOV", "ADD", "SUB", "AND", "OR", "XOR", "INC", "DEC", "NEG", "NOT", "SHL", "SHR", "SAR",
    "MOVSX", "MOVZX", "ADC", "SBB", "IMUL", "LEA", "XCHG", "POP",
    "SETZ", "SETNZ", "SETC", "SETNC", "SETL", "SETG", "SETLE", "SETGE", "SETA", "SETB",
}
UNARY = ("INC", "DEC", "NEG", "NOT", "POP")


def scan(path: str, targets: set, keep_alias: bool):
    """One pass. `keep_alias` carries register aliases across basic blocks."""
    hits = {}
    func = "?"
    alias: dict = {}          # reg -> (base reg, offset from it)
    spill: dict = {}          # stack slot -> (base reg, offset)

    with open(path, encoding="utf-8", errors="replace") as fh:
        for raw in fh:
            raw = raw.rstrip("\n")

            m = FUNC.match(raw)
            if m:
                func = "%s @ %s" % (m.group(1), m.group(2))
                alias, spill = {}, {}
                continue

            if raw.startswith("LAB_"):
                if not keep_alias:
                    alias, spill = {}, {}
                continue
            if raw.startswith("; ") or not raw.strip():
                continue

            m = LINE.match(raw)
            if not m:
                continue
            addr, _bytes, text = m.group(1), m.group(2), m.group(3)
            text = text.split(";")[0].strip()
            if not text:
                continue
            mn = text.split()[0]

            # Resolve every memory operand against what we know of its base.
            comma = text.find(",")
            for hit in MEM.finditer(text):
                reg, disp = hit.group(1), hit.group(2)
                value = 0
                if disp:
                    value = int(disp, 16) if "x" in disp.lower() else int(disp)
                base, extra = alias.get(reg, (reg, 0))
                effective = value + extra
                if effective not in targets:
                    continue
                if mn == "LEA":
                    continue          # taking an address is not touching the field
                written = (mn in WRITERS and comma != -1 and hit.start() < comma) \
                    or (mn in UNARY and comma == -1)
                via = "" if base == reg else " (via %s=%s+0x%x)" % (reg, base, extra)
                hits[addr] = (effective, "W" if written else "R", text, base, via, func)

            # Then update what we know, in the order the idioms shadow each other.
            lea = LEA.match(text)
            if lea:
                dst, src, off = lea.group(1), lea.group(2), int(lea.group(3), 16)
                base, extra = alias.get(src, (src, 0))
                alias[dst] = (base, extra + off)
                continue

            imm = ADDIMM.match(text)
            if imm:
                op, reg, off = imm.group(1), imm.group(2), int(imm.group(3), 16)
                base, extra = alias.get(reg, (reg, 0))
                alias[reg] = (base, extra + (-off if op == "SUB" else off))
                continue

            sp = SPILL.match(text)
            if sp:
                slot, reg = sp.group(1), sp.group(2)
                if reg in alias:
                    spill[slot] = alias[reg]
                else:
                    spill.pop(slot, None)
                continue

            rl = RELOAD.match(text)
            if rl:
                reg, slot = rl.group(1), rl.group(2)
                if slot in spill:
                    alias[reg] = spill[slot]
                else:
                    alias.pop(reg, None)
                continue

            mv = REGMOV.match(text)
            if mv:
                dst, src = mv.group(1), mv.group(2)
                if src in alias:
                    alias[dst] = alias[src]
                else:
                    alias.pop(dst, None)
                continue

            # Anything else that writes a register invalidates its alias.
            if comma != -1 and mn in WRITERS:
                first = text.split(None, 1)[1].split(",")[0].strip()
                alias.pop(first, None)
            elif mn in UNARY and " " in text:
                alias.pop(text.split(None, 1)[1].strip(), None)
            if mn == "CALL":
                for volatile in ("EAX", "ECX", "EDX"):
                    alias.pop(volatile, None)

    return hits


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("offsets", nargs="+", help="struct offsets in hex, e.g. 9e 1a4")
    ap.add_argument("--binary", default="DBSIM", help="DBSIM (default) or VSHELL")
    ap.add_argument("--range", dest="span", help="limit to LO-HI virtual addresses")
    ap.add_argument("--writes-only", action="store_true")
    ap.add_argument("--reads-only", action="store_true")
    args = ap.parse_args()

    path = os.path.join(ANALYSIS, "%s_disasm_full.txt" % args.binary.upper())
    if not os.path.exists(path):
        raise SystemExit("no disassembly dump at %s" % path)

    targets = {int(o, 16) for o in args.offsets}
    lo, hi = 0, 0xFFFFFFFF
    if args.span:
        parts = args.span.split("-")
        lo, hi = int(parts[0], 16), int(parts[1], 16)

    # Both passes, unioned -- see the module docstring for why neither is sound.
    merged = scan(path, targets, keep_alias=False)
    merged.update(scan(path, targets, keep_alias=True))

    found = 0
    for offset in sorted(targets):
        rows = [(a, v) for a, v in sorted(merged.items())
                if v[0] == offset and lo <= int(a, 16) <= hi
                and not (args.writes_only and v[1] == "R")
                and not (args.reads_only and v[1] == "W")]
        reads = sum(1 for _, v in rows if v[1] == "R")
        print("==== +0x%x : %d access(es), %d read, %d write ===="
              % (offset, len(rows), reads, len(rows) - reads))
        for addr, (_off, rw, text, base, via, func) in rows:
            print("  %s %s  %-44s base=%s%s   [%s]" % (rw, addr, text[:44], base, via, func))
        if not rows:
            print("  NO ACCESS FOUND -- a null result, not proof; see the module docstring")
        found += len(rows)

    return 0 if found else 1


if __name__ == "__main__":
    sys.exit(main())
