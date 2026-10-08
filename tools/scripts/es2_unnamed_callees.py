#!/usr/bin/env python3
"""List unnamed functions that named functions call -- the naming backlog.

Understanding a named function usually means decoding its FUN_ callees. If those
callees never get a known_symbols entry, the next session decodes them
again. This lists every function that

  * has no name in tools/ghidra_scripts/known_symbols_<binary>.json (a low-confidence,
    comment-only entry still counts as unnamed), and
  * is the target of a direct CALL, or a JMP tail call, from a function that
    known_symbols files name at the chosen confidence (default: high).

It is ranked by how many named callers reach it, then by its call sites across
the whole binary, so library code that every decompile passes through comes
first.

Source of structure: the linear disassembly dumps in tools/analysis_out
(`; FUNCTION name @ addr` headers, `CALL 0x... ; -> label` lines). Names come
from the known_symbols files, never from the dump's labels, which may be stale.

What it does not see: calls through a vtable, a function pointer or a jump table
(CALL [reg+n], CALL reg). A callee reached only that way is not listed -- this is
a backlog, not a proof that nothing else is unnamed.

For an ES1 binary, a call into the 0x90 padding before a function's prologue is
counted as a call to that function (retail_binaries.Image.entries_of).

A target the dump labels with a non-FUN_ name that the known_symbols files do not
carry (a CRT routine, an import thunk, a Ghidra FID match) is left out unless
--include-library is given; those names came from Ghidra, not from this project.

Usage:
    python tools/scripts/es2_unnamed_callees.py                  # both binaries
    python tools/scripts/es2_unnamed_callees.py --binary VSHELL
    python tools/scripts/es2_unnamed_callees.py --binary ES1DBSIM
    python tools/scripts/es2_unnamed_callees.py --confidence medium   # high+medium callers
    python tools/scripts/es2_unnamed_callees.py --caller MissionVar_Write
    python tools/scripts/es2_unnamed_callees.py --min-callers 3 --limit 40
    python tools/scripts/es2_unnamed_callees.py --json out.json
"""

from __future__ import annotations

import argparse
import json
import os
import re
import sys
from collections import defaultdict

import es2_symbols
import retail_binaries

REPO = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
ANALYSIS = retail_binaries.ANALYSIS

FUNC_LINE = re.compile(r"^; FUNCTION (\S+) @ ([0-9a-f]{8})")
BRANCH_LINE = re.compile(r"^([0-9a-f]{8})\s{3}\S+\s+(CALL|JMP) 0x([0-9a-f]{1,8})\b(?:.*; -> (\S+))?")

CONFIDENCE_RANK = {"high": 2, "medium": 1, "low": 0}
UNNAMED_PREFIXES = ("FUN_", "LAB_", "thunk_FUN_", "SUB_")


def load_symbols(binary: str, path: str | None = None) -> dict[str, dict]:
    if path is None:
        entries = es2_symbols.entries(binary)
    else:
        with open(path, encoding="utf-8-sig") as f:
            entries = json.load(f)["entries"]
    return {e["address"].lower(): e for e in entries
            if e.get("binary") == binary and e.get("type") == "function"}


def parse_disasm(binary: str):
    """Returns (function starts -> dump label, caller start -> [(site, kind, target)])."""
    path = retail_binaries.get(binary).dump("disasm_full.txt")
    labels: dict[str, str] = {}
    branches: dict[str, list[tuple[str, str, str]]] = defaultdict(list)
    current = None
    with open(path, encoding="utf-8", errors="replace") as f:
        for line in f:
            m = FUNC_LINE.match(line)
            if m:
                current = m.group(2)
                labels[current] = m.group(1)
                continue
            if current is None:
                continue
            m = BRANCH_LINE.match(line)
            if m:
                site, kind, target, label = m.groups()
                target = target.zfill(8)
                if label and target not in labels:
                    labels.setdefault("~" + target, label)
                branches[current].append((site, kind, target))
    return labels, branches


def canonical_targets(binary: str, branches, starts):
    """Symantec C++ code is entered through the 0x90 padding before a prologue as well as at it
    (retail_binaries.Image.entries_of), so a call to either is a call to one function. Each target
    becomes the prologue when Ghidra made a function there or made none at any of the entries, and
    otherwise the entry it did make one at. Ghidra also makes 1-3 byte functions of the padding alone
    (FUN_000015a3 in ES1DBSIM), which are not the callee."""
    image = retail_binaries.Image(binary)
    memo: dict[str, str] = {}

    def canon(target: str) -> str:
        if target not in memo:
            entries = image.entries_of(int(target, 16))
            prologue = max(entries)
            known = [a for a in entries if "%08x" % a in starts]
            memo[target] = "%08x" % (prologue if prologue in known or not known else known[0])
        return memo[target]

    return {caller: [(site, kind, canon(t)) for site, kind, t in sites] for caller, sites in branches.items()}


def analyse(binary: str, min_conf: int, include_library: bool, caller_filter: set[str] | None,
            symbols_path: str | None = None):
    symbols = load_symbols(binary, symbols_path)
    labels, branches = parse_disasm(binary)
    starts = {a for a in labels if not a.startswith("~")}
    if retail_binaries.get(binary).compiler == retail_binaries.SYMANTEC:
        branches = canonical_targets(binary, branches, starts)

    def named(addr: str) -> dict | None:
        e = symbols.get(addr)
        return e if e and e.get("name") else None

    def dump_label(addr: str) -> str:
        return labels.get(addr) or labels.get("~" + addr) or f"FUN_{addr}"

    # Every direct call site in the binary, for the global ranking.
    total_sites: dict[str, int] = defaultdict(int)
    for caller, sites in branches.items():
        for _, kind, target in sites:
            if kind == "CALL" or target in starts:
                total_sites[target] += 1

    found: dict[str, dict] = {}
    for caller, sites in branches.items():
        ce = named(caller)
        if not ce or CONFIDENCE_RANK.get(ce.get("confidence"), 0) < min_conf:
            continue
        if caller_filter and caller not in caller_filter and ce["name"] not in caller_filter:
            continue
        for site, kind, target in sites:
            # A JMP counts only as a tail call into another function, not a branch inside this one.
            if kind == "JMP" and (target not in starts or target == caller):
                continue
            if named(target):
                continue
            label = dump_label(target)
            is_library = not label.startswith(UNNAMED_PREFIXES) and not label.startswith("maybe_")
            if is_library and not include_library:
                continue
            rec = found.setdefault(target, {
                "address": target,
                "dump_label": label,
                "library": is_library,
                "function_start": target in starts,
                "low_confidence_entry": target in symbols,
                "named_callers": {},
            })
            rec["named_callers"].setdefault(ce["name"], []).append(site)

    rows = []
    for rec in found.values():
        rec["named_caller_count"] = len(rec["named_callers"])
        rec["total_call_sites"] = total_sites.get(rec["address"], 0)
        rows.append(rec)
    rows.sort(key=lambda r: (-r["named_caller_count"], -r["total_call_sites"], r["address"]))
    return rows


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    ap.add_argument("--binary", type=str.upper, choices=retail_binaries.IDS, action="append",
                    help="binary to scan (repeatable; default VSHELL and DBSIM)")
    ap.add_argument("--confidence", choices=["high", "medium"], default="high",
                    help="minimum confidence of the calling function's entry (default high)")
    ap.add_argument("--caller", action="append",
                    help="only callees of this named function (name or address; repeatable)")
    ap.add_argument("--min-callers", type=int, default=1,
                    help="only callees reached from at least this many named functions")
    ap.add_argument("--limit", type=int, default=0, help="rows to print per binary (0 = all)")
    ap.add_argument("--callers-shown", type=int, default=4,
                    help="named callers listed per row (default 4)")
    ap.add_argument("--include-library", action="store_true",
                    help="also list targets the dump labels with a Ghidra/CRT name")
    ap.add_argument("--json", metavar="PATH", help="write the full result as JSON")
    ap.add_argument("--symbols", metavar="PATH",
                    help="a known_symbols file to read (default: the repo's file for each binary)")
    args = ap.parse_args()

    binaries = args.binary or ["VSHELL", "DBSIM"]
    caller_filter = {c.lower() if re.fullmatch(r"[0-9a-fA-F]{8}", c) else c
                     for c in args.caller} if args.caller else None
    out = {}
    for binary in binaries:
        rows = [r for r in analyse(binary, CONFIDENCE_RANK[args.confidence],
                                   args.include_library, caller_filter, args.symbols)
                if r["named_caller_count"] >= args.min_callers]
        out[binary] = rows
        shown = rows[: args.limit] if args.limit else rows
        print(f"== {binary}: {len(rows)} unnamed callees of {args.confidence}+ named functions"
              + (f" (showing {len(shown)})" if len(shown) < len(rows) else ""))
        print(f"{'address':8}  {'named':>5}  {'sites':>5}  callers")
        for r in shown:
            callers = sorted(r["named_callers"])
            more = len(callers) - args.callers_shown
            text = ", ".join(callers[: args.callers_shown]) + (f", +{more} more" if more > 0 else "")
            flags = []
            if not r["function_start"]:
                flags.append("no Ghidra function here")
            if r["low_confidence_entry"]:
                flags.append("low-confidence entry")
            if r["library"]:
                flags.append(f"dump: {r['dump_label']}")
            suffix = f"  [{'; '.join(flags)}]" if flags else ""
            print(f"{r['address']}  {r['named_caller_count']:>5}  {r['total_call_sites']:>5}  {text}{suffix}")
        print()

    if args.json:
        with open(args.json, "w", encoding="utf-8") as f:
            json.dump(out, f, indent=2)
        print(f"wrote {args.json}")
    print("Direct CALL/JMP only: callees reached through a vtable or function pointer are not listed.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
