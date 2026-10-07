#!/usr/bin/env python3
"""Regenerate the whole-program function list, decompilation, vtable and struct dumps and report how much of each binary is named.

Runs `ES2ListFunctions` headless against the ES2Recon project, once per binary, writing
`tools/analysis_out/<BINARY>_functions.txt` (address, name, body size), then `ES2DumpFullDecomp`
the same way, writing `tools/analysis_out/<BINARY>_decomp_full.c`, then `ES2DumpAllVtables`,
writing `tools/analysis_out/<BINARY>_vtables_full.txt` (read by `es2_xref.py`), then
`ES2DumpStructs`, writing `tools/analysis_out/<BINARY>_structs_full.txt`, then `ES2DumpFullAsm`, writing
`tools/analysis_out/<BINARY>_disasm_full.txt` (read by `es2_naming.py`, `es2_xref.py`,
`es2_fieldscan.py` and `es2_late_entries.py`). The runs are sequential
because headless Ghidra locks the project. Each dump is written to a temporary file and moved into
place only when the script reports `SCRIPT-OK`, so a failed or cancelled run leaves the previous
dump intact.

A dump is skipped when it is already current. Ghidra keeps each program as `db.<N>.gbf` in the
project's `idata` folder and raises N on every save, so `<BINARY>_dumps.json` beside the dumps
records, per dump, the N it was taken from and a hash of the dumping script and its arguments; a dump
whose N and hash both still match is not run again. The dumps open the program `-readOnly`, since a
headless run without it saves the program on exit and raises N even when nothing changed. `--force`
dumps regardless.

It then reports, from the function list, the functions whose name is the one
`known_symbols_<binary>.json` records for that address -- the names this project assigned, as
opposed to `FUN_` placeholders and the names Ghidra supplies itself (Borland runtime functions,
Win32 import thunks, `entry`) -- both as a count and weighted by body size in bytes, since a few
large functions hold much of the code. Each figure is given as a share of every function and as a
share excluding the library/import names. Code bytes Ghidra has not placed in any function are
outside both denominators.

Usage:
    python tools/scripts/ghidra_full_decomp.py              # dump both binaries (all five dumps, skipping current ones), then report
    python tools/scripts/ghidra_full_decomp.py --force      # dump everything, current or not
    python tools/scripts/ghidra_full_decomp.py --no-dump    # report from the existing function lists
    python tools/scripts/ghidra_full_decomp.py --binary DBSIM
    python tools/scripts/ghidra_full_decomp.py --binary DBSIM --dump functions --dump vtables
"""

from __future__ import annotations

import argparse
import glob
import hashlib
import json
import os
import re
import subprocess
import sys

import es2_symbols

REPO_ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
GHIDRA = os.path.join(REPO_ROOT, "tools", "ghidra_12.1.2_PUBLIC", "support", "analyzeHeadless.bat")
PROJECT = os.path.join(REPO_ROOT, "tools", "ghidra_project")
SCRIPTS = os.path.join(REPO_ROOT, "tools", "ghidra_scripts")
OUT_DIR = os.path.join(REPO_ROOT, "tools", "analysis_out")

BINARIES = ["DBSIM", "VSHELL"]
DUMPS = ["functions", "decomp", "vtables", "structs", "disasm"]
TIMEOUT_SECONDS = 60  # per function, passed through to ES2DumpFullDecomp


def functions_path(binary: str) -> str:
    return os.path.join(OUT_DIR, f"{binary}_functions.txt")


def dump_path(binary: str) -> str:
    return os.path.join(OUT_DIR, f"{binary}_decomp_full.c")


def vtables_path(binary: str) -> str:
    return os.path.join(OUT_DIR, f"{binary}_vtables_full.txt")


def structs_path(binary: str) -> str:
    return os.path.join(OUT_DIR, f"{binary}_structs_full.txt")


def disasm_path(binary: str) -> str:
    return os.path.join(OUT_DIR, f"{binary}_disasm_full.txt")


def program_version(binary: str) -> int | None:
    """The program's saved-database version: the highest N of the db.<N>.gbf files Ghidra keeps
    for it under ES2Recon.rep/idata, located through that folder's ~index.dat. None when it cannot
    be read, which makes every dump of the binary run."""
    idata = os.path.join(PROJECT, "ES2Recon.rep", "idata")
    try:
        with open(os.path.join(idata, "~index.dat"), encoding="utf-8", errors="replace") as f:
            ids = re.findall(r"^\s*([0-9a-f]{8}):" + re.escape(binary) + r"\.EXE:", f.read(), re.M | re.I)
    except OSError:
        return None
    if len(ids) != 1:
        return None
    files = glob.glob(os.path.join(idata, "*", f"~{ids[0]}.db", "db.*.gbf"))
    nums = [int(m.group(1)) for p in files if (m := re.search(r"db\.(\d+)\.gbf$", p))]
    return max(nums) if nums else None


def stamps_path(binary: str) -> str:
    return os.path.join(OUT_DIR, f"{binary}_dumps.json")


def read_stamps(binary: str) -> dict:
    try:
        with open(stamps_path(binary), encoding="utf-8") as f:
            return json.load(f)
    except (OSError, ValueError):
        return {}


def script_hash(script: str, script_args: list[str]) -> str:
    """The dumping script's source and arguments, so a change to either re-dumps."""
    with open(os.path.join(SCRIPTS, f"{script}.java"), "rb") as f:
        h = hashlib.sha1(f.read())
    h.update(json.dumps(script_args).encode())
    return h.hexdigest()[:12]


def run_dump(binary: str, script: str, final: str, script_args: list[str], what: str,
             force: bool = False) -> bool:
    version = program_version(binary)
    stamp = {"db": version, "script": script_hash(script, script_args)}
    stamps = read_stamps(binary)
    name = os.path.basename(final)
    if not force and version is not None and os.path.exists(final) and stamps.get(name) == stamp:
        print(f"=== {binary}: {what}: current (database version {version}), skipped", flush=True)
        return True
    tmp = final + ".tmp"
    cmd = [GHIDRA, PROJECT, "ES2Recon", "-process", f"{binary}.EXE", "-noanalysis", "-readOnly",
           "-scriptPath", SCRIPTS, "-postScript", script, tmp, *script_args]
    print(f"=== {binary}: {what}", flush=True)
    ok = False
    with subprocess.Popen(cmd, stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
                          text=True, errors="replace") as proc:
        for line in proc.stdout:
            if "SCRIPT-" in line or "progress:" in line or "ERROR" in line:
                print("   " + line.strip(), flush=True)
            if "SCRIPT-OK" in line:
                ok = True
    if ok and proc.returncode == 0 and os.path.exists(tmp):
        os.replace(tmp, final)
        print(f"   wrote {os.path.relpath(final, REPO_ROOT)}")
        # Re-read: another dump of this binary may have written its own entry meanwhile.
        stamps = read_stamps(binary)
        if version is not None and program_version(binary) == version:
            stamps[name] = stamp
        else:
            # The database was saved while this dump ran, so it may already be behind.
            stamps.pop(name, None)
        with open(stamps_path(binary), "w", encoding="utf-8") as f:
            json.dump(stamps, f, indent=1, sort_keys=True)
        return True
    if os.path.exists(tmp):
        os.remove(tmp)
    print(f"   FAILED (exit {proc.returncode}); {os.path.relpath(final, REPO_ROOT)} left unchanged")
    return False


def read_functions(path: str) -> dict[str, tuple[str, int]]:
    """Address -> (name, body size in bytes) for every line of an `ES2ListFunctions` list."""
    functions = {}
    with open(path, encoding="utf-8", errors="replace") as f:
        for line in f:
            address, name, size = line.rstrip("\r\n").split("\t")
            functions[address.lower()] = (name, int(size))
    return functions


def report_names(binary: str) -> None:
    path = functions_path(binary)
    if not os.path.exists(path):
        sys.exit(f"{os.path.relpath(path, REPO_ROOT)} does not exist; run without --no-dump first")

    known = {e["address"].lower(): e["name"] for e in es2_symbols.entries(binary)
             if e.get("type") == "function" and "name" in e}

    functions = read_functions(path)
    manual = [a for a, (n, _) in functions.items() if known.get(a) == n]
    placeholder = [a for a, (n, _) in functions.items() if n.startswith("FUN_")]
    classified = set(manual) | set(placeholder)
    other = [a for a in functions if a not in classified]
    renamed_since = [a for a, (n, _) in functions.items() if a in known and known[a] != n]
    no_function = sorted(a for a in known if a not in functions)

    def size(addresses: list[str]) -> int:
        return sum(functions[a][1] for a in addresses)

    total, total_bytes = len(functions), size(list(functions))
    # Excludes Ghidra's own library and import-thunk names.
    game, game_bytes = total - len(other), total_bytes - size(other)

    def row(label: str, addresses: list[str], shares: bool = False) -> str:
        n, b = len(addresses), size(addresses)
        line = f"   {label:<28}{n:6}"
        if shares:
            line += f"  {100 * n / total:5.1f}%  {100 * n / game:5.1f}%"
        line += f"  {b:9}"
        if shares:
            line += f"  {100 * b / total_bytes:5.1f}%  {100 * b / game_bytes:5.1f}%"
        return line

    print(f"\n=== {binary} function names ({os.path.relpath(path, REPO_ROOT)})")
    print(f"   {'':<28}{'count':>6}  {'all':>6}  {'game':>6}  {'bytes':>9}  {'all':>6}  {'game':>6}")
    print(row("functions in the database", list(functions)))
    print(row("manually named", manual, shares=True))
    print(row("FUN_ placeholders", placeholder, shares=True))
    print(row("library/import/entry names", other))
    print("   'game' excludes the library/import/entry names from the denominator")
    if renamed_since:
        print(f"   {len(renamed_since)} known_symbols names differ from the database"
              " (run ES2ApplySymbolNames): " + " ".join(sorted(renamed_since)))
    if no_function:
        print(f"   {len(no_function)} known_symbols names have no function at their address"
              " in the database: " + " ".join(f"{a} ({known[a]})" for a in no_function))


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    parser.add_argument("--no-dump", action="store_true",
                        help="skip Ghidra; report from the existing function lists")
    parser.add_argument("--binary", choices=BINARIES, action="append",
                        help="dump only this binary (repeatable; default both)")
    parser.add_argument("--dump", choices=DUMPS, action="append",
                        help="regenerate only this dump (repeatable; default all five)")
    parser.add_argument("--force", action="store_true",
                        help="dump even when the database is unchanged since the last dump")
    args = parser.parse_args()

    ok = True
    if not args.no_dump:
        dumps = args.dump or DUMPS
        for binary in args.binary or BINARIES:
            if "functions" in dumps:
                ok = run_dump(binary, "ES2ListFunctions", functions_path(binary), [],
                              "listing functions", args.force) and ok
            if "decomp" in dumps:
                ok = run_dump(binary, "ES2DumpFullDecomp", dump_path(binary), [str(TIMEOUT_SECONDS)],
                              "decompiling (several minutes)", args.force) and ok
            if "vtables" in dumps:
                ok = run_dump(binary, "ES2DumpAllVtables", vtables_path(binary), [],
                              "dumping vtables", args.force) and ok
            if "structs" in dumps:
                ok = run_dump(binary, "ES2DumpStructs", structs_path(binary), [],
                              "dumping structs", args.force) and ok
            if "disasm" in dumps:
                ok = run_dump(binary, "ES2DumpFullAsm", disasm_path(binary), [],
                              "disassembling", args.force) and ok
    report_names("DBSIM")
    report_names("VSHELL")
    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main())
