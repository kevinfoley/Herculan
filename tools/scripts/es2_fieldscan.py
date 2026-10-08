#!/usr/bin/env python3
"""Answer "what touches this struct field?" over the Ghidra text disassembly.

Grepping the disassembly for `+ 0x9e]` finds only the accesses that happen to
name the offset directly. Both compilers reach a field in other ways, and this
resolves each of them, so "no reader" means something. It is the tool for
checking a claim that a field is write-only or dead -- and for finding the
setter of a flag whose writer "could not be located".

Borland (DBSIM, VSHELL) uses three, all common in DBSIM:

    LEA EAX,[EBX + 0x92]        ...  MOV byte ptr [EAX + 0xc],1     ; obj+0x9e
    ADD EBX,0x92                ...  MOV byte ptr [EBX + 0x19],1    ; obj+0xab
    LEA ECX,[EBX + 0x222]  MOV [EBP-4],ECX  MOV EAX,[EBP-4]  ...    ; the spill

Symantec C++ (ES1DBSIM, ES1GO) uses the same three, and ES1DBSIM's code has
three more shapes that bear on them (Herculan/docs/es1/formats/compiler.md):

    MOV EDI,EBX  ADD EDI,0xc  MOVSD  MOVSB  MOVSB                   ; obj+0xc..+0x11
    MOV EDI,ESI  ADD EDI,0xc  OR EDI,EDI  JNZ ...  MOV [EBP-4],EDI  ; the null test
    MOV EDI,0x19  ADD EDI,EBP  ...  MOV CX,word ptr [EDI]           ; base+0x19

The first is an inlined copy or fill: `MOVS`/`STOS` write through `EDI` (and
`MOVS` reads through `ESI`) with no displacement, a unit at a time, and step
the register. The disassembly dumps hold 2,092 unrepeated `MOVS` and 234
unrepeated `STOS` in ES1DBSIM, 44 and 206 in DBSIM. A `REP` form is resolved
over its whole extent when `ECX` holds a constant, and labelled with its unit
count; otherwise only its first unit is reported, labelled "count unknown".
The second is how Symantec C++ tests a pointer for null (`TEST` in Borland's
code); `OR` or `AND` of a register with itself leaves its alias in place. The
third adds a constant below 0x1000 held in one register to a pointer in another;
ES1DBSIM has it 9 times.

A field access names its offset exactly, as `[reg + disp]` does; a string
move reports every offset its units cover.

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

**Kinds of access it cannot see, and none shows as a warning:**
- Code Ghidra left undisassembled (`; ... N undefined bytes` in the dump; e.g.
  `Mech_BehaviourDriveOffThink`, `Flyer_Constructor`). Grep the range a claim
  depends on for that marker, and cross-check the decompile dump, which covers
  those functions and folds the rebases back (`*(char *)((int)this + 0xb1)`).
- Bulk writes through a call: a memset/memcpy over the field is invisible;
  sweep their call sites for destinations on your class. So is the rest of a
  `REP` move whose count is not a constant.
- Indexed operands (`[EBX + EAX*0x4 + 0x12]`): only `[reg]` and `[reg + disp]`
  are matched.
A field holding a meaningful value is therefore never proven unread by this.

Usage:
    python tools/scripts/es2_fieldscan.py 9e
    python tools/scripts/es2_fieldscan.py 222 224 226 228 22a
    python tools/scripts/es2_fieldscan.py --range 00415000-00422000 a8 a9
    python tools/scripts/es2_fieldscan.py --writes-only --binary VSHELL 1c
    python tools/scripts/es2_fieldscan.py --binary ES1DBSIM --range 0005b7d0-0005b875 c 12

Exit status is 1 when no access is found for any offset given.
"""

from __future__ import annotations

import argparse
import os
import re
import sys

import retail_binaries

LINE = re.compile(r"^([0-9a-f]{8})\s{3}([0-9a-f.]+)\s+(.*)$")
FUNC = re.compile(r"^; FUNCTION (\S+) @ ([0-9a-f]{8})")
MEM = re.compile(r"\[(E[A-Z][A-Z])(?:\s*\+\s*(-?0x[0-9a-f]+|\d+))?\]")
LEA = re.compile(r"^LEA\s+(E[A-Z][A-Z]),\[(E[A-Z][A-Z])\s*\+\s*(-?0x[0-9a-f]+)\]$")
REGMOV = re.compile(r"^MOV\s+(E[A-Z][A-Z]),(E[A-Z][A-Z])$")
IMMMOV = re.compile(r"^MOV\s+(E[A-Z][A-Z]),(-?0x[0-9a-f]+|\d+)$")
ADDIMM = re.compile(r"^(ADD|SUB)\s+(E[A-Z][A-Z]),(-?0x[0-9a-f]+)$")
ADDREG = re.compile(r"^ADD\s+(E[A-Z][A-Z]),(E[A-Z][A-Z])$")
SELFTEST = re.compile(r"^(OR|AND)\s+(E[A-Z][A-Z]),\2$")
SPILL = re.compile(r"^MOV\s+dword ptr \[(E[BS]P \+ -?0x[0-9a-f]+)\],(E[A-Z][A-Z])$")
RELOAD = re.compile(r"^MOV\s+(E[A-Z][A-Z]),dword ptr \[(E[BS]P \+ -?0x[0-9a-f]+)\]$")
STRING = re.compile(r"^(MOVS|STOS|LODS|CMPS|SCAS)([BWD])(\.REP\w*)?(?:\s|$)")

# Mnemonics whose FIRST operand is the destination.
WRITERS = {
    "MOV", "ADD", "SUB", "AND", "OR", "XOR", "INC", "DEC", "NEG", "NOT", "SHL", "SHR", "SAR",
    "MOVSX", "MOVZX", "ADC", "SBB", "IMUL", "LEA", "XCHG", "POP",
    "SETZ", "SETNZ", "SETC", "SETNC", "SETL", "SETG", "SETLE", "SETGE", "SETA", "SETB",
}
UNARY = ("INC", "DEC", "NEG", "NOT", "POP")
FIELD_CONST = 0x1000
WIDTH = {"B": 1, "W": 2, "D": 4}
# String instructions: (register, written?) for each pointer they use.
STRING_POINTERS = {
    "MOVS": (("EDI", True), ("ESI", False)),
    "STOS": (("EDI", True),),
    "LODS": (("ESI", False),),
    "CMPS": (("EDI", False), ("ESI", False)),
    "SCAS": (("EDI", False),),
}


def number(text: str) -> int:
    return int(text, 16) if "x" in text.lower() else int(text)


def scan(path: str, targets: set, keep_alias: bool):
    """One pass. `keep_alias` carries register aliases across basic blocks. Returns
    {(address, offset, operand): (offset, R|W, text, base, via, function)}."""
    hits = {}
    func = "?"
    alias: dict = {}          # reg -> (base reg, offset from it)
    spill: dict = {}          # stack slot -> (base reg, offset)
    const: dict = {}          # reg -> constant it holds

    def forget(reg: str) -> None:
        alias.pop(reg, None)
        const.pop(reg, None)

    with open(path, encoding="utf-8", errors="replace") as fh:
        for raw in fh:
            raw = raw.rstrip("\n")

            m = FUNC.match(raw)
            if m:
                func = "%s @ %s" % (m.group(1), m.group(2))
                alias, spill, const = {}, {}, {}
                continue

            if raw.startswith("LAB_"):
                if not keep_alias:
                    alias, spill, const = {}, {}, {}
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

            # A string instruction: each pointer register covers count units of its width.
            st = STRING.match(text)
            if st:
                op, width, rep = st.group(1), WIDTH[st.group(2)], st.group(3)
                count = 1 if not rep else const.get("ECX")
                for reg, written in STRING_POINTERS[op]:
                    base, extra = alias.get(reg, (reg, 0))
                    span = width * (count if count is not None else 1)
                    for t in targets:
                        if extra <= t < extra + span:
                            via = "" if (base, extra) == (reg, 0) else " (via %s=%s+0x%x)" % (reg, base, extra)
                            if rep:
                                via += " (REP, %s)" % ("count unknown" if count is None else "%d units" % count)
                            hits[(addr, t, reg)] = (t, "W" if written else "R", text, base, via, func)
                    if count is None:
                        forget(reg)
                    else:
                        alias[reg] = (base, extra + span)
                        const.pop(reg, None)
                if rep:
                    const["ECX"] = 0
                    alias.pop("ECX", None)
                continue

            # Resolve every memory operand against what we know of its base.
            comma = text.find(",")
            for hit in MEM.finditer(text):
                reg, disp = hit.group(1), hit.group(2)
                value = number(disp) if disp else 0
                base, extra = alias.get(reg, (reg, 0))
                effective = value + extra
                if effective not in targets:
                    continue
                if mn == "LEA":
                    continue          # taking an address is not touching the field
                written = (mn in WRITERS and comma != -1 and hit.start() < comma) \
                    or (mn in UNARY and comma == -1)
                via = "" if (base, extra) == (reg, 0) else " (via %s=%s+0x%x)" % (reg, base, extra)
                hits[(addr, effective, hit.start())] = (effective, "W" if written else "R", text, base, via, func)

            # Then update what we know, in the order the idioms shadow each other.
            if SELFTEST.match(text):
                continue              # OR/AND of a register with itself leaves it unchanged

            lea = LEA.match(text)
            if lea:
                dst, src, off = lea.group(1), lea.group(2), int(lea.group(3), 16)
                base, extra = alias.get(src, (src, 0))
                forget(dst)
                alias[dst] = (base, extra + off)
                continue

            imm = ADDIMM.match(text)
            if imm:
                op, reg, off = imm.group(1), imm.group(2), int(imm.group(3), 16)
                base, extra = alias.get(reg, (reg, 0))
                const.pop(reg, None)
                alias[reg] = (base, extra + (-off if op == "SUB" else off))
                continue

            # A constant below FIELD_CONST added to a pointer is a field offset; a larger one is
            # an address with an index added to it, and is left alone.
            addreg = ADDREG.match(text)
            if addreg:
                dst, src = addreg.group(1), addreg.group(2)
                if 0 <= const.get(src, -1) < FIELD_CONST and dst not in const:
                    base, extra = alias.get(dst, (dst, 0))
                    alias[dst] = (base, extra + const[src])
                elif 0 <= const.get(dst, -1) < FIELD_CONST and src not in const:
                    base, extra = alias.get(src, (src, 0))
                    k = const.pop(dst)
                    alias[dst] = (base, extra + k)
                else:
                    forget(dst)
                continue

            iv = IMMMOV.match(text)
            if iv:
                forget(iv.group(1))
                const[iv.group(1)] = number(iv.group(2))
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
                forget(reg)
                if slot in spill:
                    alias[reg] = spill[slot]
                continue

            mv = REGMOV.match(text)
            if mv:
                dst, src = mv.group(1), mv.group(2)
                forget(dst)
                if src in alias:
                    alias[dst] = alias[src]
                elif not {src, dst} & {"ESP", "EBP"}:
                    alias[dst] = (src, 0)     # names the base a later rebase is relative to
                if src in const:
                    const[dst] = const[src]
                continue

            # Anything else that writes a register invalidates its alias.
            if comma != -1 and mn in WRITERS:
                forget(text.split(None, 1)[1].split(",")[0].strip())
            elif mn in UNARY and " " in text:
                forget(text.split(None, 1)[1].strip())
            if mn == "CALL":
                for volatile in ("EAX", "ECX", "EDX"):
                    forget(volatile)

    return hits


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("offsets", nargs="+", help="struct offsets in hex, e.g. 9e 1a4")
    ap.add_argument("--binary", default="DBSIM", type=str.upper, choices=retail_binaries.IDS,
                    help="default DBSIM")
    ap.add_argument("--range", dest="span", help="limit to LO-HI virtual addresses")
    ap.add_argument("--writes-only", action="store_true")
    ap.add_argument("--reads-only", action="store_true")
    args = ap.parse_args()

    path = retail_binaries.get(args.binary).dump("disasm_full.txt")
    if not os.path.exists(path):
        raise SystemExit("no disassembly dump at %s (tools/scripts/ghidra_full_decomp.py --dump disasm)" % path)

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
        rows = [(a, v) for (a, _t, _o), v in sorted(merged.items(), key=lambda kv: (kv[0][0], str(kv[0][2])))
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
