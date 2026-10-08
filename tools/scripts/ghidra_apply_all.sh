#!/bin/sh
# Applies all three Ghidra knowledge files to each binary's Ghidra project, in the one order that
# works on a database that has never had them applied before.
#
#   ES2ApplyVtables      builds the vtable types
#   ES2ApplyStructures   builds the object structs (which reference the vtable types) and types the
#                        function parameters listed in known_structs.json
#   ES2ApplySymbolNames  renames functions, writes plate comments, applies signatures -- and a
#                        signature may name a struct type, which is why it goes last. It reads
#                        the binary's own known_symbols_<binary>.json.
#
# The binaries, their projects and their program names come from tools/scripts/retail_binaries.py:
# DBSIM.EXE and VSHELL.EXE in ES2Recon, then ES1DBSIM and ES1GO in ES1Recon. Each apply reads only
# the entries whose "binary" names the program it runs on.
#
# Only a database with no /ES2 types yet actually cares about the order: once the types exist they
# persist in the project, and from then on any of the three can be run on its own, in any order, as
# often as you like. All three are idempotent. Run this after a fresh import and auto-analysis;
# after that, run whichever one you need directly.
#
# Usage: sh tools/scripts/ghidra_apply_all.sh [--read-only] [BINARY...]     (default: every binary)
#   --read-only opens each program -readOnly: a dry run that reports what it would apply.
set -e

READONLY=
if [ "$1" = "--read-only" ]; then
    READONLY=-readOnly
    shift
fi

HERE=$(cd "$(dirname "$0")" && pwd)
# One line per binary: id, project, program, assets checkout, ghidra_scripts directory.
REGISTRY=$(py -c "
import sys; sys.path.insert(0, sys.argv[1]); import retail_binaries as R
for b in R.REGISTRY.values():
    print(b.id, b.project, b.program, *(p.replace(chr(92), '/') for p in (R.ASSETS, R.GHIDRA_SCRIPTS)))" "$HERE")

if [ $# -eq 0 ]; then
    set -- $(echo "$REGISTRY" | cut -d' ' -f1)
fi

for binary in "$@"; do
    line=$(echo "$REGISTRY" | grep -i "^$binary " || true)
    if [ -z "$line" ]; then
        echo "unknown binary $binary; the registry holds: $(echo "$REGISTRY" | cut -d' ' -f1 | tr '\n' ' ')" >&2
        exit 1
    fi
    set -- $line
    id=$1 project=$2 program=$3 assets=$4 scripts=$5
    lower=$(echo "$id" | tr 'A-Z' 'a-z')
    for step in "ES2ApplyVtables known_vtables.json" \
                "ES2ApplyStructures known_structs.json" \
                "ES2ApplySymbolNames known_symbols_$lower.json"; do
        script=${step% *}
        json=${step#* }
        echo "=== $id $script"
        "$assets/tools/ghidra_12.1.2_PUBLIC/support/analyzeHeadless.bat" "$assets/tools/ghidra_project" \
            "$project" -process "$program" -noanalysis $READONLY \
            -scriptPath "$scripts" -postScript "$script" "$scripts/$json" 2>&1 \
            | grep -E "ERROR|WARN|$script\.java>" || true
    done
done

echo
echo "Each line above ending 'errors=0' is a clean apply. A signature that fails to resolve a"
echo "struct type means the structures step did not run -- re-run this script."
