#!/bin/bash
# Copies the repo and the retail installs onto WSL's own file system, which is case-sensitive. The Windows drives
# under /mnt match names ignoring case as Windows does, so a test run from there proves nothing about case.
# Run as the WSL user, from Windows:
#
#   wsl -d Ubuntu -- bash /mnt/e/ES2Stuff/tools/scripts/wsl/sync.sh
#
# The copy goes to $HERCULAN_WSL_ROOT, ~/ES2Stuff unless set. Each run brings Herculan/ and Branding/ (which the
# host's project links its icon from) in step with Windows. ES2/ and ES2v110/ are copied the first time only, since
# the games write into them; delete one from the copy to take it again. Disc images are linked, not copied: they are
# opened by name, and what is inside them is matched ignoring case anyway.
set -euo pipefail

src=$(cd "$(dirname "$(readlink -f "$0")")/../../.." && pwd)
dst=${HERCULAN_WSL_ROOT:-$HOME/ES2Stuff}
mkdir -p "$dst"

rsync -a --delete --exclude=bin/ --exclude=obj/ --exclude=.vs/ --exclude=TestResults/ "$src/Herculan/" "$dst/Herculan/"
rsync -a --delete "$src/Branding/" "$dst/Branding/"

for install in ES2 ES2v110; do
	if [ -d "$src/$install" ] && [ ! -d "$dst/$install" ]; then
		rsync -a "$src/$install/" "$dst/$install/"
		# Their drive.cfg files name Windows paths into the repo (E:\ES2Stuff\ES2v110\CD), which mean nothing here.
		python3 -I - "$src" "$dst" "$dst/$install" <<'PY'
import os, re, sys

src, dst, tree = sys.argv[1:4]
# /mnt/e/ES2Stuff is E:\ES2Stuff to Windows.
m = re.fullmatch(r"/mnt/([a-z])(/.*)?", src)
if not m:
    sys.exit(0)
windows = m.group(1) + ":" + (m.group(2) or "").replace("/", "\\")
prefix = re.compile(re.escape(windows) + r"(\\[^\r\n]*)?", re.IGNORECASE)
for folder, _, files in os.walk(tree):
    for name in files:
        if name.lower() != "drive.cfg":
            continue
        path = os.path.join(folder, name)
        text = open(path, "rb").read().decode("latin-1")
        new = prefix.sub(lambda h: dst + (h.group(1) or "").replace("\\", "/"), text)
        if new != text:
            open(path, "wb").write(new.encode("latin-1"))
            print("pointed at the copy:", path)
PY
	fi
done

for iso in "$src"/*.iso; do
	[ -e "$iso" ] && ln -sfn "$iso" "$dst/$(basename "$iso")"
done

echo "Synced into $dst"
