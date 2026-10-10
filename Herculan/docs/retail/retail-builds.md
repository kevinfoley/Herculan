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

All four of v1.10's `VSHELL.EXE` and `DBSIM.EXE` carry v1.0's language strings — the `-f`/`-g` usage lines, the `eng\`/`fre\`/`ger\` folders, the `.eng`/`.fre`/`.ger` extensions, `simvoice`, `data\language.cfg` and the manual folders — and both `VSHELL.EXE`s add two readers of the language, for `campaign.str` and the intro movies ([v1.10's shell](#v110s-shell-reads-the-language-twice-more)). Both `DBSIM.EXE`s choose and play the CD music as v1.0's does ([`simulation/audio.md`](simulation/audio.md#which-track-and-whether-there-is-one)). `VER95\`'s two programs also read their startup messages from a file, show accented text, and fix a few bugs ([How v1.10's programs differ](#how-v110s-programs-differ)); `VER31\`'s have not been compared ([Open](#open)).

The HiRez Studios image, `Earthsiege2_Freeware_HiRezStudios_1r0.iso`, is a v1.0 disc: volume `ES2`, mastered 1997-12-29, a single data track of 2,048-byte sectors with no audio and no Joliet tree. Its executables, archives, `SIERRA.INF`, `BATCH.EXE`, movies and instructor clips are byte for byte the install's, as are the 1997 `ES2TS.TXT` and the 1998 `README.WRI`, so the install came from the same pressing. It adds a `DEMOS\` folder of other products' demos. Its `FRENCH\` and `GERMAN\` folders hold English copies of the readme and `ES2TS.TXT` and translate only `ES2GUIDE.HLP` and `LANGUAGE.INF`.

## The installer

Both builds install through Sierra's `SETUP.EXE`, which runs the script in `SIERRA.INF`.

**The language is the setup program's own.** The script sets flags 1, 2 and 3 for English, French and German from three `LANGUAGE_EQ` tests. The `InstallLanguage` dialog that would ask the player is defined but its line in the script is commented out, in both builds.

**The size choice decides what is copied.** Minimum, Medium and Maximum copy more of the archives; whatever is left is read from the disc, which both programs find through `data\drive.cfg` ([`formats/vol-archive.md`](formats/vol-archive.md#which-archives-are-mounted)).

- Every size copies `SIMALERT.VOL`, `SIMSOUND.VOL` and `SIMPATCH.VOL`. Medium adds `SIMVOL0.VOL`; Maximum adds `SIMVOL0.VOL`, `SHLSOUND.VOL`, `SHELL0.VOL`, `ZONES.VOL` and `LANG0.VOL`. Both scripts also mark `PATCH1.VOL`, which neither `[Files]` list has.
- v1.0 copies `SIMVOICE.VOL` at every size. The lines that would pick the French or German archive are commented out, and its `[Files]` list gives `SIMVOICF.VOL` and `SIMVOICG.VOL` as 4 bytes each. It marks `README.WRI` three times, against three `[Files]` entries in `ENGLISH\`, `FRENCH\` and `GERMAN\` ([Open](#open)).
- v1.10 copies the chosen language's voice archive and `README.WRI`, its `ERROR.*` as `ERROR.STR`, its `MISSION.*` as `DATA\MISSION.STR`, and under Windows 3.1 its `JCONFIG.*` as `JCONFIG.STR`. It adds `SIMLANG.VOL` at every size and `SHELL1.VOL` to Maximum. Its Windows 3.1 branch takes the executables and the voice archives from `VER31\`, whose `SIMVOIC?.VOL` are smaller than `VOL\`'s, and its Windows 95 branch the executables from `VER95\`.

**Some files are always read from the disc**, whatever the size: the movies, the on-line manual and the training instructor's clips, each under the directory `data\drive.cfg` names (`Path_UnderDriveCfg`, VSHELL `0040d429`; `DriveCfg_PrefixPath`, DBSIM `0045ee44`). Neither installer copies them. At startup VSHELL opens `avi\pt1.avi` there, and when it cannot, shows `Please insert ESII CD and restart` (in v1.10, [`ERROR.STR`'s message 5](#how-v110s-programs-differ)) and quits (`Shell_Main`, `00401525`).

**`BATCH.EXE` writes the configuration.** The script runs it as `BATCH.EXE <source> <install> <E|F|G> <0|1>`. It writes:

1. `data\drive.cfg`: the source directory and the install directory, one per line.
2. `data\language.cfg`: the letter, one byte.
3. A copy of `<install>\<LANGUAGE>\README.WRI`, under a name the two builds differ on ([`formats/winhelp.md`](formats/winhelp.md#macros)). v1.10's `BATCH.EXE` also carries `\SPANISH` and `\ITALIAN` beside the three shipped folder names; the script passes only `E`, `F` and `G`.
4. `data\prefs.cfg`, when `Sierra.ini`'s `[Config] VideoSpeed` is at most 1000 (v1.0) or 700 (v1.10): byte 4, the low-resolution option, set to 1, and in v1.10 also byte 47, which makes v1.10's shell show its `Performance Note` ([How v1.10's programs differ](#how-v110s-programs-differ)).
5. With the last argument `1`, the Indeo codecs' registry entries. v1.0's returns before this step when `VideoSpeed` is above 1000; v1.10's does not. Both scripts also write the codecs' `SYSTEM.INI` entries themselves.

## How a language is chosen

The shell and the simulator each keep a language value, set only from their command lines ([`command-line.md`](command-line.md)):

| Program | Switches | Selects |
|---|---|---|
| VSHELL | `-f`, `-g`, either case | the `LANG0.VOL` folder of every `.BIN` table, the extension of a mission's text (`.eng`, `.fre`, `.ger`), and in v1.10 the folders of `campaign.str` and the intro movies |
| DBSIM | `-F`, `-G` (`-E` for Spanish) | the voice archive and its folder label, `SIMVOICE`/`SIMVOICF`/`SIMVOICG` ([`simulation/audio.md`](simulation/audio.md#speech-and-the-comm-portraits)), and the `st<letter>\` folder of its `.STR` tables |

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
| `SIMVOICF.VOL`, `SIMVOICG.VOL` | in the install, byte copies of `SIMVOICE.VOL` with its `SIMVOICE\` label | under `SIMVOICF\` and `SIMVOICG\`: the cockpit computer in French and German, the squadmates in English ([`formats/sound-samples.md`](formats/sound-samples.md#voice-clips)) |
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

**`SIMPATCH.VOL`** adds the two fonts above, a near-silent `battle1.wav` for the music catalog entries, and the low-memory bank's missing `explo5.wav` ([`simulation/audio.md`](simulation/audio.md#ids-0-9-are-music)).

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
| `C5_05.MSN` | eight of the lunar outpost's structures start collapsed when campaign flags 821 to 828 are set ([`campaign-continuity.md`](campaign-continuity.md#persistent-bases)); v1.10 removes those records, and their conditions, for the four on 821, 823, 825 and 827 — the conduit, hangar, listening post and missile tower — which therefore always start whole |
| `DEMO_01.MSN`, `DEMO_02.MSN` | two orders each take the next route: 46 to 47, and 29 to 30 |
| `TRAIN1.MSN` | two row-3 values, GUID 68 = 6 and GUID 71 = 37, which no record in the file names |

## How v1.10's programs differ

`VER95\`'s `VSHELL.EXE` and `DBSIM.EXE` are v1.0's code rebuilt with a handful of changes. Compared function by function, with relocated addresses and branch offsets masked and each match's calls and data references checked against the other matches, 2,474 of the shell's 2,503 functions and 3,494 of the simulator's 3,502 are the same code at new addresses. The rest are below. Addresses are v1.0's.

**Both programs read their startup messages from `ERROR.STR`.** The installer copies the chosen language's `ERROR.*` under that name ([The installer](#the-installer)): two groups, twelve messages and three captions (`Error`, `Warning`, `Notice` in English). Each program loads it once it has mounted the archives, which the shell now does before parsing its command line and the simulator before its missing-launcher check; a missing file is a severity-4 assert in the shell (`Missing string resource: error.str.`) and severity 5 in the simulator. These boxes take their text from it:

| Box | v1.0 text, caption | v1.10 message, caption |
|---|---|---|
| Shell: the desktop is not in 256 colours ([`shell/startup.md`](shell/startup.md#the-startup--shell_main-00401525)) | literal, `You are not in 256 color mode` | 3, `Notice` |
| Shell: started without `-eggplant` | literal, `Error` | 4, `Error` |
| Shell: no disc, at startup and in `Movie_PlayQueue` | literal, `Error` | 5, `Error` |
| Shell: `Performance Note` | literal, `Performance Note`, *320x200 mode* | 6, `Notice`, *320x240* in English |
| Simulator: run directly | `dbsim.exe is not meant to be run directly`, `alert` | 4, `Error` |

The shell's `Could not create Sound Manager.` and the simulator's memory assert stay literal.

**The installer decides whether the Performance Note shows.** v1.10's shell has no `Sierra.ini` read. Its main menu shows the note when `prefs.cfg` option 47 is 1, which `BATCH.EXE` writes when `VideoSpeed` is at most 700 ([The installer](#the-installer)), then sets the option to 2 and saves, so the note shows once. v1.0's shell reads `VideoSpeed` itself while option 47 is 0 and then sets it to 1 ([`shell/startup.md`](shell/startup.md#sierraini)).

**The shell shows accented letters.** v1.0's shell cannot show a byte above 127. `TextBox_Wrap` (`0040d0d6`) compares each character as a signed byte and turns every one below a space into a space, which takes in 128 to 255, and the font routines (`Font_GlyphWidth`, `Font_DrawGlyph`, `Font_DrawString`) take the character as signed, indexing the font's width and glyph tables before their start. v1.10's wrap turns only control characters into spaces. Its font code is another build of the graphics library's `src32\GL_FONT.CPP`, linked first in the code segment, which reads characters unsigned, asserts on a character the font lacks and draws it as `?`, checks its fonts with asserts v1.0's build lacks, and calls the blit, line and palette routines directly rather than through the library's wrappers.

**The shell's other changes:**

- `SfxTimer_Kill` (`004062b0`), which every exit runs, first puts the memory DC's previous bitmap back and deletes the DIB section `Display_Init` made for the shell's drawing.
- `Sos_OpenDrivers` (`0042df68`) reports a failed driver open with a severity-2 assert, `Sound driver initialization failed.`, before returning as v1.0 does.
- The two language readers of [v1.10's shell reads the language twice more](#v110s-shell-reads-the-language-twice-more).

**The simulator's changes:**

- **MUSIC off stays off.** `Sim_InitMissionSession` (`004614fc`) no longer ends with `Sound_SetMusicEnabled(1)`, so the mission no longer overrides the preference ([`simulation/audio.md`](simulation/audio.md#the-mission-session-overrides-the-music-preference)).
- **A ray meets the ground only where it finds a point on it.** In the thin-ray mode of `Terrain_RayWalk` (`0046e87c`), the weapon-fire and line-of-sight ray, each of the three places that find the ground at or above the ray now runs `Terrain_CellSurfaceIntersect` whether or not the caller asked for the point, and reports a hit only when it finds one. When it finds none, a crossing in mid-walk walks on and the segment's last step reports no hit. v1.0 reports the hit either way, leaving the caller's point unwritten, and skips the solve for a caller that passes no point, which neither retail caller does. So where the solve misses, v1.0 cuts a shot to a range measured to stale stack memory and keeps a line of sight blocked, and v1.10 lets both through that part of the ground. The solve itself is the same code in both, including the skew in the second plane of a cell split along its `00`–`11` diagonal, which can put that plane metres off the ground ([`simulation/terrain-heightmap.md`](simulation/terrain-heightmap.md#when-the-solve-finds-no-point)).
- **The MFD status screen's range is in metres.** `MfdStatusScreen_Paint` (`0043a5a0`) passes `DIST:`'s range through `Hud_WorldUnitsToMetres` (`00434228`) before printing it; the range itself is still `Math_DistanceBetweenPoints` between the two origins ([`simulation/mfd.md`](simulation/mfd.md#mfdstatus--modes-0-and-4)). The same function also adds up the damage of every paper-doll region's components and divides by their count, into a local it never reads again.
- **A cockpit drag or release with no widget held is ignored.** `Widget_PressedIndex` is -1 when no widget is held. Under a mouse capture, v1.0's `CockpitMouse_ProcessQueue` (`00452d18`) uses it without checking, looking up the dword before the root's widget list and calling through it; v1.10 checks for -1 first and skips the drag and the release, the release leaving the capture set ([`simulation/cockpit-input.md`](simulation/cockpit-input.md#4-once-per-frame-the-real-clickpressdrag-logic)).
- `AlertPanel_CtorBase` (`00454174`) no longer asserts when the panel's backing-store allocation fails, and `Sos_UnbindLibrary` (`00495913`) decrements the bind count even when it is 0.

**What the rebuild alone changed.** Both programs' data moved and the shell's assert messages name their sources in upper case (`src\VSHELL.CPP`). Nine shell functions differ only in the source line numbers their asserts report: `Display_RealizePalette`, `Vshell_LoadItemFromPath`, `DDraw_SetModeAndCreatePrimary`, `Game_ExportMissionHandoff`, `CareerDat_ReadStage`, `LoadCareerDat`, `Career_BuildBriefingText`, `Career_BuildDebriefText` and `OnlineManual_Open`. The shell's process entry no longer stores the thread's stack base (`fs:[4]`) at `0046c06e`.

## The v1.10 disc image

The GoldGames image, `EarthSiege2_Freeware_GoldGames_1r11_withAudio.iso`, is one file of raw 2,352-byte sectors with no cue sheet, so no table of contents survives. Its data track is Mode 1: the ISO 9660 volume (`EARTHSIEGE2`, primary names only, no Joliet tree) spans 207,041 sectors, followed by 152 more that carry a sync pattern, 207,193 in all. Audio fills the remaining 72,795 sectors, to the end of the file at 279,988.

The audio falls into six pieces between runs of exact digital silence: 150 sectors before the first, 453 to 458 between pieces, 151 after the last. From the start of one piece's music to the next, they run 12,390, 10,864, 12,387, 13,061, 13,001 and 10,941 sectors, the last to the end of the file. The v1.0 disc's tracks 2 to 7 run 10,865, 12,395, 12,393, 13,065, 13,005 and 11,019 ([`simulation/audio.md`](simulation/audio.md#the-disc)).

The second piece is v1.0's track 2: a rip of that track from a v1.0 disc is the same recording as the image's audio there, 300 samples out of step. The other pieces match by length alone, which makes the fourth to sixth v1.0's tracks 5 to 7, and the first and third its tracks 3 and 4 in an order lengths cannot settle ([Open](#open)). Played from this image, v1.10's first mission would therefore not open with v1.0's track 2.

## Open

- **Open:** whether `VER31\`'s `VSHELL.EXE` and `DBSIM.EXE` carry `VER95\`'s changes ([How v1.10's programs differ](#how-v110s-programs-differ)); only `VER95\`'s were compared function by function.
- **Open:** what shows `ERROR.STR`'s messages 0 to 2 (memory) and 7 (a movie that failed to play). The shell's message helper is called with 3 to 6 and the simulator reads only 4; `VER95\ES.EXE`, whose code has not been read, is a candidate.
- **Open:** why the buffer whose address a raster-driver setup stores before calling `RasterDriver_InstallRoutines` (VSHELL `0047d0b1`, stored at `0045979d`; DBSIM `004a4335`) is zero in v1.0's files and holds non-zero bytes in v1.10's, and whether anything reads it before writing it.
- **Open:** whether the v1.10 disc's audio tracks are in the image's order, with v1.0's track 2 second, or the image was assembled out of order; and which of the image's first and third pieces is v1.0's track 3. Ripping v1.0's tracks 3 and 4 would settle the second.
- **Deferred:** why the v1.0 `SIERRA.INF` lists `VSHELL.EXE` at 563,232 bytes. The disc it ships on carries a 564,768-byte one, the analysed `ES2\VSHELL.EXE`.
- **Deferred:** what `VER31\ES.EXE` does with `data\language.cfg`, and how it numbers `-R`. It names the file; its code has not been read.
- **Deferred:** what Sierra's `SETUP.EXE` tests `LANGUAGE_EQ` against.
- **Deferred:** whether v1.0's three `TOGGLEON(README.WRI)` mark all three of its `README.WRI` entries. `BATCH.EXE` copies the chosen language's, which suggests each is installed.
