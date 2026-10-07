#!/usr/bin/env python3
"""List functions Ghidra starts past their frame prologue, or early, on the fill bytes before their code.

A late start puts a function's name and plate comment on an address nothing calls, so
`es2_xref.py` reports the named function UNREFERENCED, and the decompile reads the frame through
`unaff_EBP`. Ghidra often keeps a separate few-byte function at the true entry as well.

For every function entry in `analysis_out/<BIN>_disasm_full.txt` and every known_symbols
function address, this reports the entry when its first byte is not PUSH EBP (0x55) and the bytes
immediately before it are a complete frame prologue (`55 8B EC` optionally followed by
ADD/SUB ESP, imm8 or imm32). A function cannot end in a prologue, so such an entry is the body of
the function at the prologue. Each hit shows the known_symbols name and the dump's name at both
addresses and the direct references (rel32 branches and stored dwords) to each.

Shapes it sorts the hits into:
  NAMED-LATE  the known_symbols name is on the late address: re-address the entry, then repair.
  INVERSE     the name is already on the true entry; Ghidra's function there is only the prologue
              and the body is a stray FUN_ at the late address.
  UNNAMED     neither address is named.
  EARLY       the opposite case: the function starts on fill bytes (00 or 90) before its code, and
              the first byte after the fill is what a branch or stored pointer reaches. The "true"
              address is the one after the fill; re-address a known_symbols entry to it.

`es2_naming.py fixentry BIN addr+addr...` repairs the true entries. The dumps lag the database,
so a hit already repaired shows here until the dumps are regenerated; the fixentry dry run's
`removing` lines show the live state.

Usage:
    python tools/scripts/es2_late_entries.py [--binary DBSIM|VSHELL] [--no-xref]
"""

from __future__ import annotations

import argparse
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import es2_symbols  # noqa: E402
import es2_xref as X  # noqa: E402

# (label, length, byte predicate on the bytes before the entry)
PROLOGUES = [
    ("55 8B EC 83 C4 ib", 6, lambda b: b[:5] == b"\x55\x8b\xec\x83\xc4"),
    ("55 8B EC 83 EC ib", 6, lambda b: b[:5] == b"\x55\x8b\xec\x83\xec"),
    ("55 8B EC 81 C4 id", 9, lambda b: b[:5] == b"\x55\x8b\xec\x81\xc4"),
    ("55 8B EC 81 EC id", 9, lambda b: b[:5] == b"\x55\x8b\xec\x81\xec"),
    ("55 8B EC", 3, lambda b: b == b"\x55\x8b\xec"),
]


def read(img: X.Image, va: int, n: int):
    for _name, sva, vsize, raw_off, raw_size in img.sections:
        if sva <= va and va + n <= sva + min(vsize, raw_size):
            o = raw_off + (va - sva)
            return img.data[o:o + n]
    return None


def refs(img: X.Image, va: int) -> str:
    return f"{len(X.rel32_sites(img, va))} branch/{len(X.dword_sites(img, va))} dword"


def early(img: X.Image, binary: str, va: int, first: bytes, known, dump, xref: bool) -> int:
    """An entry on a run of 00/90 fill whose first byte past the fill is referenced and the entry
    itself is not: Ghidra began the function early. Returns 1 when it reports one."""
    if first[0] not in (0x00, 0x90):
        return 0
    run = read(img, va, 16) or b""
    k = len(run) - len(run.lstrip(bytes((0x00, 0x90))))
    if not 0 < k < 16:
        return 0
    true = va + k
    if X.rel32_sites(img, va) or X.dword_sites(img, va):
        return 0
    if not (X.rel32_sites(img, true) or X.dword_sites(img, true)):
        return 0
    line = (f"{binary} {'EARLY':10} true {true:08x} known={known.get(true)} dump={dump.get(true)}"
            f" | early {va:08x} known={known.get(va)} dump={dump.get(va)} | {k} fill bytes")
    if xref:
        line += f" | refs true {refs(img, true)}, early {refs(img, va)}"
    print(line)
    return 1


def scan(binary: str, entries, xref: bool) -> int:
    img = X.Image(X.BINARIES[binary])
    dump = {}
    with open(os.path.join(X.ANALYSIS, f"{binary}_disasm_full.txt"), encoding="utf-8", errors="replace") as f:
        for line in f:
            m = X.FUNC_LINE.match(line)
            if m:
                dump[int(m.group(2), 16)] = m.group(1)
    known = {int(e["address"], 16): e.get("name") for e in entries
             if e.get("binary") == binary and e.get("type") == "function"}
    cands = sorted(set(dump) | set(known))
    hits = 0
    for va in cands:
        first = read(img, va, 1)
        if first is None or first[0] == 0x55:
            continue
        for label, k, test in PROLOGUES:
            pre = read(img, va - k, k)
            if pre is None or not test(pre):
                continue
            true = va - k
            shape = "NAMED-LATE" if known.get(va) else "INVERSE" if known.get(true) else "UNNAMED"
            line = (f"{binary} {shape:10} true {true:08x} known={known.get(true)} dump={dump.get(true)}"
                    f" | late {va:08x} known={known.get(va)} dump={dump.get(va)} | {label}")
            if xref:
                line += f" | refs true {refs(img, true)}, late {refs(img, va)}"
            print(line)
            hits += 1
            break
        else:
            hits += early(img, binary, va, first, known, dump, xref)
    print(f"{binary}: {len(cands)} entries checked, {hits} late- or early-start candidates")
    return hits


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--binary", choices=sorted(X.BINARIES))
    ap.add_argument("--no-xref", action="store_true", help="skip the per-hit reference counts")
    a = ap.parse_args()
    entries = es2_symbols.entries()
    for binary in [a.binary] if a.binary else sorted(X.BINARIES):
        scan(binary, entries, not a.no_xref)
    return 0


if __name__ == "__main__":
    sys.exit(main())
