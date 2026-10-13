#!/bin/bash
# Builds and tests the engine solution in the WSL copy that sync.sh makes, and prints the warnings, errors and
# results; the whole log is ~/herculan-wsl-test.log. Arguments go to dotnet test, as in --filter <expression>.
#
#   wsl -d Ubuntu -- bash /mnt/e/ES2Stuff/tools/scripts/wsl/test.sh
set -uo pipefail

root=${HERCULAN_WSL_ROOT:-$HOME/ES2Stuff}
log=$HOME/herculan-wsl-test.log
cd "$root/Herculan" || { echo "No $root/Herculan: run sync.sh first." >&2; exit 1; }
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1

{
	dotnet build HerculanEngine.sln && dotnet test HerculanEngine.sln --no-build "$@"
} > "$log" 2>&1
status=$?

grep -E "warning [A-Z]+[0-9]+|error [A-Z]+[0-9]+" "$log" | sed -E 's/ \[[^]]*\]$//' | sort -u
grep -E "Build succeeded|Build FAILED|Warning\(s\)|Error\(s\)|Passed!|Failed!" "$log"
grep -E "^\s+Failed [A-Za-z]" "$log" | sort -u
exit $status
