#!/bin/bash
# Prepares a WSL Ubuntu to build, test and run HERCULAN. Run as root, from Windows:
#
#   wsl -d Ubuntu -u root -- bash /mnt/e/ES2Stuff/tools/scripts/wsl/setup.sh
#
# Installs, from Ubuntu's archive: the .NET 10 SDK (the one the Windows builds use), the OpenGL and X11/Wayland
# libraries GLFW loads, the PulseAudio client OpenAL Soft needs to reach WSLg's sound server, and mesa-utils for
# glxinfo. Ubuntu packages no .NET 8 runtime, which the engine targets, so that comes from Microsoft's own
# dotnet-install.sh, into the SDK's root so one dotnet host finds both. Safe to run again.
set -euo pipefail

if [ "$(id -u)" -ne 0 ]; then
	echo "Run as root: wsl -d <distro> -u root -- bash $0" >&2
	exit 1
fi

export DEBIAN_FRONTEND=noninteractive
apt-get update -qq
apt-get install -y -qq dotnet-sdk-10.0 \
	libgl1 libegl1 libx11-6 libxrandr2 libxinerama1 libxcursor1 libxi6 \
	libwayland-client0 libwayland-cursor0 libwayland-egl1 libxkbcommon0 \
	libpulse0 mesa-utils rsync python3

root=$(dirname "$(readlink -f "$(command -v dotnet)")")
if ! dotnet --list-runtimes | grep -q '^Microsoft.NETCore.App 8\.'; then
	work=$(mktemp -d /tmp/dotnet-install.XXXXXX)
	curl -sSfL https://dot.net/v1/dotnet-install.sh -o "$work/dotnet-install.sh"
	bash "$work/dotnet-install.sh" --channel 8.0 --runtime dotnet --install-dir "$root" --no-path
	rm -rf "$work"
fi

dotnet --list-sdks
dotnet --list-runtimes | grep NETCore
