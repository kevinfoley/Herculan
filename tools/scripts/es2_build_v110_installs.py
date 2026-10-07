"""Builds v1.10 test installs from the GoldGames ISO.

  <out>/CD/                 the disc's game files (the source directory drive.cfg names), less the
                            DEMOS, DIRECTX, DRIVERS, INDEO, WIN32S and WING folders
  <out>/Install-<LANG>/     what SIERRA.INF's Win95 Maximum install leaves, for ENG, FRE and GER
  <out>/Install-ENG-Min/    what its Minimum install leaves, in English

Follows the v1.10 SIERRA.INF script: the FlagFiles and MaximumInstall toggles, group 95 (the VER95
executables), group 96/97/98 (that language's voice archive and README.WRI), the ERROR/MISSION renames,
then BATCH.EXE's drive.cfg, language.cfg and README copy. The Indeo DLLs (system directory) and the
VideoSpeed prefs.cfg tweak are not reproduced. See Herculan/docs/retail/retail-builds.md, "The installer".
Herculan.Engine.Tests' GameContentMountTests reads the result when it is present.

Usage: es2_build_v110_installs.py [ISO] [OUT]
  defaults: EarthSiege2_Freeware_GoldGames_1r11_withAudio.iso and ES2v110, both at the repo root.
  Rewrites OUT/CD and each install folder.
"""
import os
import shutil
import sys

sys.path.insert(0, os.path.dirname(__file__))
from iso9660 import Iso  # noqa: E402

REPO_ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
DEFAULT_ISO = os.path.join(REPO_ROOT, "EarthSiege2_Freeware_GoldGames_1r11_withAudio.iso")
DEFAULT_OUT = os.path.join(REPO_ROOT, "ES2v110")

SKIP_ON_CD = {"DEMOS", "DIRECTX", "DRIVERS", "INDEO", "WIN32S", "WING"}

LANGS = {"E": ("ENGLISH", "ENG"), "F": ("FRENCH", "FRE"), "G": ("GERMAN", "GER")}
VOICE = {"E": "SIMVOICE.VOL", "F": "SIMVOICF.VOL", "G": "SIMVOICG.VOL"}

# (source path on the disc, destination relative to the install)
COMMON = [
    ("ESII.ICO", "ESII.ICO"), ("BWCC32.DLL", "BWCC32.DLL"), ("SOSLIBS3.DLL", "SOSLIBS3.DLL"),
    ("HMIDRV.WIN", "HMIDRV.WIN"), ("HMIMDRV.WIN", "HMIMDRV.WIN"), ("SOSLIB.INI", "SOSLIB.INI"),
    ("CW3220.DLL", "CW3220.DLL"),
    ("DATA/EXIT.CFG", "DATA/EXIT.CFG"), ("DATA/KEYJOY.CFG", "DATA/KEYJOY.CFG"),
    ("DATA/PREFS.CFG", "DATA/PREFS.CFG"), ("DATA/SOUND.CFG", "DATA/SOUND.CFG"),
    ("DATA/MAT0.DAT", "DATA/MAT0.DAT"), ("DATA/MFORMS.DAT", "DATA/MFORMS.DAT"),
    ("DATA/MAPLABEL.STR", "DATA/MAPLABEL.STR"),
    ("TAPES/DEMOLIST.STR", "TAPES/DEMOLIST.STR"), ("TAPES/DEMO1.TAP", "TAPES/DEMO1.TAP"),
    ("TAPES/DEMO2.TAP", "TAPES/DEMO2.TAP"), ("TAPES/DEMO3.TAP", "TAPES/DEMO3.TAP"),
    ("VOL/SIMALERT.VOL", "VOL/SIMALERT.VOL"), ("VOL/SIMSOUND.VOL", "VOL/SIMSOUND.VOL"),
    ("VOL/SIMPATCH.VOL", "VOL/SIMPATCH.VOL"), ("VOL/SIMLANG.VOL", "VOL/SIMLANG.VOL"),
    ("SAV/GAMEFILE.STR", "SAV/GAMEFILE.STR"),
    # MaximumInstall (dropped for a Minimum install: see MAXIMUM_ONLY)
    ("VOL/SHLSOUND.VOL", "VOL/SHLSOUND.VOL"), ("VOL/SIMVOL0.VOL", "VOL/SIMVOL0.VOL"),
    ("VOL/SHELL0.VOL", "VOL/SHELL0.VOL"), ("VOL/SHELL1.VOL", "VOL/SHELL1.VOL"),
    ("VOL/ZONES.VOL", "VOL/ZONES.VOL"), ("VOL/LANG0.VOL", "VOL/LANG0.VOL"),
    # group 95
    ("VER95/ES.EXE", "ES.EXE"), ("VER95/VSHELL.EXE", "VSHELL.EXE"), ("VER95/DBSIM.EXE", "DBSIM.EXE"),
    ("VER95/SOS9503.DLL", "SOS9503.DLL"),
]


MAXIMUM_ONLY = {"VOL/SHLSOUND.VOL", "VOL/SIMVOL0.VOL", "VOL/SHELL0.VOL", "VOL/SHELL1.VOL", "VOL/ZONES.VOL",
                "VOL/LANG0.VOL"}


def per_language(letter):
    folder, ext = LANGS[letter]
    return [
        (f"{folder}/ERROR.{ext}", "ERROR.STR"),
        (f"{folder}/MISSION.{ext}", "DATA/MISSION.STR"),
        (f"VOL/{VOICE[letter]}", f"VOL/{VOICE[letter]}"),
        (f"{folder}/README.WRI", f"{folder}/README.WRI"),
    ]


def write(path, data):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, "wb") as out:
        out.write(data)


def main():
    iso_path = sys.argv[1] if len(sys.argv) > 1 else DEFAULT_ISO
    out_root = os.path.abspath(sys.argv[2] if len(sys.argv) > 2 else DEFAULT_OUT)
    iso = Iso(iso_path)

    cd = os.path.join(out_root, "CD")
    count = 0
    for path, entry in iso.walk():
        if entry["dir"] or path.split("/")[0] in SKIP_ON_CD:
            continue
        write(os.path.join(cd, *path.split("/")), iso.read(entry))
        count += 1
    print(f"CD: {count} files")

    builds = [(letter, f"Install-{ext}", False) for letter, (_, ext) in LANGS.items()] + [("E", "Install-ENG-Min", True)]
    for letter, name, minimum in builds:
        folder = LANGS[letter][0]
        dest = os.path.join(out_root, name)
        if os.path.exists(dest):
            shutil.rmtree(dest)
        for src, rel in COMMON + per_language(letter):
            if minimum and src in MAXIMUM_ONLY:
                continue
            target = os.path.join(dest, *rel.split("/"))
            os.makedirs(os.path.dirname(target), exist_ok=True)
            shutil.copyfile(os.path.join(cd, *src.split("/")), target)

        # BATCH.EXE <source> <install> <letter> 0: drive.cfg, language.cfg, the README copy.
        cd_win = cd.replace("/", "\\")
        dest_win = dest.replace("/", "\\")
        write(os.path.join(dest, "DATA", "DRIVE.CFG"), f"{cd_win}\r\n{dest_win}".encode("latin-1"))
        write(os.path.join(dest, "DATA", "LANGUAGE.CFG"), letter.encode("ascii"))
        shutil.copyfile(os.path.join(dest, folder, "README.WRI"), os.path.join(dest, "README.TXT"))
        print(f"{dest}: done")


if __name__ == "__main__":
    main()
