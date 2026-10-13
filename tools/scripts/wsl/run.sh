#!/bin/bash
# Runs the engine host under WSLg from a linux-x64 publish of the WSL copy, publishing it first when asked or when
# there is none. Arguments after --publish go to the host.
#
#   wsl -d Ubuntu -- bash /mnt/e/ES2Stuff/tools/scripts/wsl/run.sh [--publish] [host arguments]
#
# A publish, not a plain build: a plain build leaves GLFW under runtimes/linux-x64/native, where Silk.NET does not
# look for it on Linux, and fails with "GlfwPlatform - not applicable". A publish puts the native libraries beside the
# program, as a release does.
#
# WSLg's default OpenGL is llvmpipe, in software. GALLIUM_DRIVER=d3d12 renders on the GPU through Direct3D 12, which
# takes the first adapter, often an integrated one; with NVIDIA's WSL driver present the NVIDIA card is asked for.
# Set MESA_D3D12_DEFAULT_ADAPTER_NAME to choose another (a part of the name glxinfo -B prints).
set -uo pipefail

root=${HERCULAN_WSL_ROOT:-$HOME/ES2Stuff}
out=$root/publish-linux-x64

publish=0
if [ "${1:-}" = "--publish" ]; then
	publish=1
	shift
fi

if [ $publish -eq 1 ] || [ ! -x "$out/Herculan" ]; then
	cd "$root/Herculan" || { echo "No $root/Herculan: run sync.sh first." >&2; exit 1; }
	DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1 dotnet publish src/Herculan.Engine.Host/Herculan.Engine.Host.csproj \
		-c Release -r linux-x64 --self-contained -o "$out" > "$HOME/herculan-wsl-publish.log" 2>&1 \
		|| { tail -20 "$HOME/herculan-wsl-publish.log"; exit 1; }
fi

export GALLIUM_DRIVER=${GALLIUM_DRIVER:-d3d12}
if [ -z "${MESA_D3D12_DEFAULT_ADAPTER_NAME:-}" ] && [ -e /usr/lib/wsl/lib/libcuda.so.1 ]; then
	export MESA_D3D12_DEFAULT_ADAPTER_NAME=NVIDIA
fi

cd "$out"
./Herculan "$@"
status=$?
echo "exit=$status"
exit $status
