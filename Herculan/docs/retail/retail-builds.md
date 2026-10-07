# Retail builds and languages

Two retail builds of Earthsiege 2 are in hand. They share the data formats every other doc describes, but differ in their launcher, their installer, their translations and a handful of missions. The rest of these docs describe v1.0 unless they say otherwise.

| | v1.0 | v1.10 |
|---|---|---|
| Source | the install under `ES2\`, and the HiRez Studios freeware disc image | the GoldGames freeware disc, `VER95\` and `VER31\` |
| `SIERRA.INF` `[Ident]` | `Version=100 Maverick JC` | `Version=110` |
| `VERSION.TXT` | `EarthSiege II version 1.0` | `EarthSiege II version 1.11` |
| Windows | 95 | 95 (`VER95\`) or 3.1 with Win32s (`VER31\`), chosen by the installer; it refuses Windows NT |
| `ES.EXE` | 8,736 bytes | 11,808 (`VER95\`), 40,480 (`VER31\`) |
| `VSHELL.EXE` | 564,768 bytes ([Open](#open)) | 566,304 in both folders, different bytes |
| `DBSIM.EXE` | 724,512 bytes | 724,512 (`VER95\`, different bytes), 727,584 (`VER31\`) |
| Languages | English | English, French, German |

All four of v1.10's `VSHELL.EXE` and `DBSIM.EXE` carry v1.0's language strings — the `-f`/`-g` usage lines, the `eng\`/`fre\`/`ger\` folders, the `.eng`/`.fre`/`.ger` extensions, `simvoice`, `data\language.cfg` and the manual folders — and both `VSHELL.EXE`s add two readers of the language, for `campaign.str` and the intro movies ([v1.10's shell](#v110s-shell-reads-the-language-twice-more)). Both `DBSIM.EXE`s choose and play the CD music as v1.0's does ([`formats/audio.md`](formats/audio.md#which-track-and-whether-there-is-one)). How else their code differs from v1.0's is [Open](#open).

The HiRez Studios image, `Earthsiege2_Freeware_HiRezStudios_1r0.iso`, is a v1.0 disc: volume `ES2`, mastered 1997-12-29, a single data track of 2,048-byte sectors with no audio and no Joliet tree. Its executables, archives, `SIERRA.INF`, `BATCH.EXE`, movies and instructor clips are byte for byte the install's, as are the 1997 `ES2TS.TXT` and the 1998 `README.WRI`, so the install came from the same pressing. It adds a `DEMOS\` folder of other products' demos. Its `FRENCH\` and `GERMAN\` folders hold English copies of the readme and `ES2TS.TXT` and translate only `ES2GUIDE.HLP` and `LANGUAGE.INF`.

## The installer

Both builds install through Sierra's `SETUP.EXE`, which runs the script in `SIERRA.INF`.

**The language is the setup program's own.** The script sets flags 1, 2 and 3 for English, French and German from three `LANGUAGE_EQ` tests. The `InstallLanguage` dialog that would ask the player is defined but its line in the script is commented out, in both builds.

**The size choice decides what is copied.** Minimum, Medium and Maximum copy more of the archives; whatever is left is read from the disc, which both programs find through `data\drive.cfg` ([`formats/vol-archive.md`](formats/vol-archive.md#which-archives-are-mounted)).

- Every size copies `SIMALERT.VOL`, `SIMSOUND.VOL` and `SIMPATCH.VOL`. Medium adds `SIMVOL0.VOL`; Maximum adds `SIMVOL0.VOL`, `SHLSOUND.VOL`, `SHELL0.VOL`, `ZONES.VOL` and `LANG0.VOL`. Both scripts also mark `PATCH1.VOL`, which neither `[Files]` list has.
- v1.0 copies `SIMVOICE.VOL` at every size. The lines that would pick the French or German archive are commented out, and its `[Files]` list gives `SIMVOICF.VOL` and `SIMVOICG.VOL` as 4 bytes each. It marks `README.WRI` three times, against three `[Files]` entries in `ENGLISH\`, `FRENCH\` and `GERMAN\` ([Open](#open)).
- v1.10 copies the chosen language's voice archive and `README.WRI`, its `ERROR.*` as `ERROR.STR`, its `MISSION.*` as `DATA\MISSION.STR`, and under Windows 3.1 its `JCONFIG.*` as `JCONFIG.STR`. It adds `SIMLANG.VOL` at every size and `SHELL1.VOL` to Maximum. Its Windows 3.1 branch takes the executables and the voice archives from `VER31\`, whose `SIMVOIC?.VOL` are smaller than `VOL\`'s, and its Windows 95 branch the executables from `VER95\`.

**Some files are always read from the disc**, whatever the size: the movies, the on-line manual and the training instructor's clips, each under the directory `data\drive.cfg` names (`Path_UnderDriveCfg`, VSHELL `0040d429`; `DriveCfg_PrefixPath`, DBSIM `0045ee44`). Neither installer copies them. At startup VSHELL opens `avi\pt1.avi` there, and when it cannot, shows `Please insert ESII CD and restart` and quits (`Shell_Main`, `00401525`).

**`BATCH.EXE` writes the configuration.** The script runs it as `BATCH.EXE <source> <install> <E|F|G> <0|1>`. It writes:

1. `data\drive.cfg`: the source directory and the install directory, one per line.
2. `data\language.cfg`: the letter, one byte.
3. A copy of `<install>\<LANGUAGE>\README.WRI`, under a name the two builds differ on ([`formats/winhelp.md`](formats/winhelp.md#macros)). v1.10's `BATCH.EXE` also carries `\SPANISH` and `\ITALIAN` beside the three shipped folder names; the script passes only `E`, `F` and `G`.
4. `data\prefs.cfg`, when `Sierra.ini`'s `[Config] VideoSpeed` is at most 1000 (v1.0) or 700 (v1.10): byte 4, the low-resolution option, set to 1, and in v1.10 also byte 47, which stops the shell's own `VideoSpeed` check from showing its `Performance Note` ([`shell/screen-layout.md`](shell/screen-layout.md#the-main-menu)).
5. With the last argument `1`, the Indeo codecs' registry entries. v1.0's returns before this step when `VideoSpeed` is above 1000; v1.10's does not. Both scripts also write the codecs' `SYSTEM.INI` entries themselves.

## How a language is chosen

The shell and the simulator each keep a language value, set only from their command lines ([`command-line.md`](command-line.md)):

| Program | Switches | Selects |
|---|---|---|
| VSHELL | `-f`, `-g`, either case | the `LANG0.VOL` folder of every `.BIN` table, the extension of a mission's text (`.eng`, `.fre`, `.ger`), and in v1.10 the folders of `campaign.str` and the intro movies |
| DBSIM | `-F`, `-G` (`-E` for Spanish) | the voice archive and its folder label, `SIMVOICE`/`SIMVOICF`/`SIMVOICG` ([`formats/audio.md`](formats/audio.md#speech-and-the-comm-portraits)), and the `st<letter>\` folder of its `.STR` tables |

The on-line manual is the exception: both programs open `<LANGUAGE>\es2guide.hlp` by `data\language.cfg` directly ([`formats/winhelp.md`](formats/winhelp.md)).

**v1.0's launcher passes neither switch**, so a v1.0 game always runs in English, whatever `language.cfg` says, and that file chooses only the manual and its readme.

**v1.10's launcher passes the installed language.** `VER95\ES.EXE` turns `language.cfg`'s letter into a switch on both command lines, so a French or German install runs both programs in its language ([`command-line.md`](command-line.md#v110s-language-switch)).

### v1.10's shell reads the language twice more

Both v1.10 `VSHELL.EXE`s read the shell's language, `0048235e` in each, at two places v1.0's does not. The addresses are `VER95\VSHELL.EXE`'s; `VER31\VSHELL.EXE` has the same code at `0040fd6d` and `0041ea1c`.

**The campaign map's stage text** (`0040fd2d`, v1.0's `Campaign_LoadStageText`) appends `eng\`, `fre\` or `ger\` for language 0, 1 or 2, and nothing for any other value, then `campaign.str`, and opens the result. v1.0 opens the literal `eng\campaign.str`. Each append is a `_strcat` onto a stack buffer the function never writes first, so the path is whatever string the buffer already holds followed by the folder and the name. When that string is not empty the open fails, and the severity-4 assert that follows exits the shell, in v1.10 as in v1.0 (`Assert_Report`, v1.0 `0044ded0`).

**The intro** (`0041e98c`, v1.0's `Movie_PlayQueue`): the first time the queue is played with movies on, the third letter of the intro's two paths, `avi\intr_pt1.avi` and `avi\intr_pt2.avi`, is overwritten with `f` for language 1 or `g` for 2, so a French or German shell plays its intro from `avf\` or `avg\` on the disc. A flag at `00471128` keeps it to once. Every other movie stays in `avi\`.

## What each build carries per language

| Resource | v1.0 | v1.10 |
|---|---|---|
| `SIMVOICF.VOL`, `SIMVOICG.VOL` | in the install, byte copies of `SIMVOICE.VOL` with its `SIMVOICE\` label | under `SIMVOICF\` and `SIMVOICG\`: the cockpit computer in French and German, the squadmates in English ([`formats/audio.md`](formats/audio.md#file-naming)) |
| Training instructor's loose clips | `SIMVOICE\` only, in the install | `SIMVOICE\`, `SIMVOICF\`, `SIMVOICG\`, 65 each, all three the English recordings |
| `LANG0.VOL` `FRE\`, `GER\` | copies of `ENG\` | translated, but for the pilot names and mission paths ([`formats/weapons-dat.md`](formats/weapons-dat.md#the-bin-string-tables)) |
| `SIMALERT.VOL` `STF\`, `STG\` | French and German text for an earlier design of the panels ([`simulation/alert-panels.md`](simulation/alert-panels.md#what-the-family-shares)) | translations of the shipped panels |
| `SIMLANG.VOL` | absent | `STF\` and `STG\` twins of every `str\` table in `SIMVOL0.VOL` but `PILOT0`, and of `TAPES\DEMOLIST.STR` |
| Mission text | `.ENG` only | `.FRE` and `.GER` beside every `.ENG` but `DEMO2`'s ([`formats/msn-mission-file.md`](formats/msn-mission-file.md#the-eng-string-table)) |
| Fonts | | six replaced, with more accented letters ([`formats/dfn-hfn-dci.md`](formats/dfn-hfn-dci.md#dfn--hfn--bitmap-font)) |
| On-line manual | the same three `ES2GUIDE.HLP` in both builds ([`formats/winhelp.md`](formats/winhelp.md)) | |
| Intro movie | `AVI\INTR_PT1.AVI`, `INTR_PT2.AVI` | also `AVF\` and `AVG\`, each with its own two files, different from `AVI\`'s, which a French or German shell plays ([v1.10's shell](#v110s-shell-reads-the-language-twice-more)) |

## Other content changes in v1.10

Every archive v1.0 has is in v1.10 with the same entries, except for the translations above and these:

**`SIMPATCH.VOL`** adds the two fonts above, a near-silent `battle1.wav` for the music catalog entries, and the low-memory bank's missing `explo5.wav` ([`formats/audio.md`](formats/audio.md#ids-0-9-are-music)).

**`ZONES.VOL`** changes twelve mission files:

| File | Change |
|---|---|
| `C2_01.ENG` | "You patrol zone" to "Your patrol zone" |
| `C2_04.ENG` | "an pair" to "a pair" |
| `C3_03.ENG` | the briefing loses its last sentence, that the listening post may be destroyed once its data is downloaded |
| `C4_09.ENG` | the briefing loses its last sentence |
| `C2_03.ENG`, `C2_03.MSN` | a second copy of the debrief's loss line, on twelve new row-1 conditions that repeat the first copy's tests, so the debrief reads the same ([`formats/msn-mission-file.md`](formats/msn-mission-file.md#the-debriefs-loss-line)) |
| `C2_05.MSN` | group 188's row-16 `0x08` from 25, the one non-zero value in any mission, to 0 ([`formats/msn-mission-file.md`](formats/msn-mission-file.md#row-16-field-decode--the-group-record-dat_0047065a-164-bytesrecord)) |
| `C2_06.MSN` | point 34 moves by (+19656, +5732) |
| `C5_05.MSN` | of the eight structures conditioned on campaign flags 821 to 828, the four on 821, 823, 825 and 827 are removed, with their conditions |
| `DEMO_01.MSN`, `DEMO_02.MSN` | two orders each take the next route: 46 to 47, and 29 to 30 |
| `TRAIN1.MSN` | two row-3 values, GUID 68 = 6 and GUID 71 = 37, which no record in the file names |

## The v1.10 disc image

The GoldGames image, `EarthSiege2_Freeware_GoldGames_1r11_withAudio.iso`, is one file of raw 2,352-byte sectors with no cue sheet, so no table of contents survives. Its data track is Mode 1: the ISO 9660 volume (`EARTHSIEGE2`, primary names only, no Joliet tree) spans 207,041 sectors, followed by 152 more that carry a sync pattern, 207,193 in all. Audio fills the remaining 72,795 sectors, to the end of the file at 279,988.

The audio falls into six pieces between runs of exact digital silence: 150 sectors before the first, 453 to 458 between pieces, 151 after the last. From the start of one piece's music to the next, they run 12,390, 10,864, 12,387, 13,061, 13,001 and 10,941 sectors, the last to the end of the file. The v1.0 disc's tracks 2 to 7 run 10,865, 12,395, 12,393, 13,065, 13,005 and 11,019 ([`formats/audio.md`](formats/audio.md#the-disc)).

The second piece is v1.0's track 2: a rip of that track from a v1.0 disc is the same recording as the image's audio there, 300 samples out of step. The other pieces match by length alone, which makes the fourth to sixth v1.0's tracks 5 to 7, and the first and third its tracks 3 and 4 in an order lengths cannot settle ([Open](#open)). Played from this image, v1.10's first mission would therefore not open with v1.0's track 2.

## Open

- **Open:** how v1.10's `VSHELL.EXE` and `DBSIM.EXE` differ from v1.0's beyond the language readers and the music. `DBSIM.EXE`'s code section is `0x200` bytes longer, so a byte comparison says nothing, and neither v1.10 executable is in the Ghidra project; the music path was compared as instruction sequences with absolute addresses masked.
- **Open:** whether the v1.10 disc's audio tracks are in the image's order, with v1.0's track 2 second, or the image was assembled out of order; and which of the image's first and third pieces is v1.0's track 3. Ripping v1.0's tracks 3 and 4 would settle the second.
- **Open:** why the v1.0 `SIERRA.INF` lists `VSHELL.EXE` at 563,232 bytes. The disc it ships on carries a 564,768-byte one, the analysed `ES2\VSHELL.EXE`.
- **Open:** what `VER31\ES.EXE` does with `data\language.cfg`, and how it numbers `-R`. It names the file; its code has not been read.
- **Open:** what Sierra's `SETUP.EXE` tests `LANGUAGE_EQ` against.
- **Open:** whether v1.0's three `TOGGLEON(README.WRI)` mark all three of its `README.WRI` entries. `BATCH.EXE` copies the chosen language's, which suggests each is installed.
