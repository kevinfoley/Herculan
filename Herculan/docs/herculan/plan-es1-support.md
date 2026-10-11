# Plan — Metaltech: Earthsiege support

Reverse-engineering *Metaltech: Earthsiege* (ES1) and running it on this engine alongside Earthsiege 2, from the retail files in `ES1/` (git-ignored, like `ES2/`).

This is a plan. Of it, steps 0.1 to 0.8 are done; nothing else is. It is in three phases: **phase 0** builds the tooling and measures how much of ES2's work carries over, **phase 1** is the reverse-engineering, and **phase 2** is the engine work. Phases 1 and 2 wait on the user's decision from step 0.8's report and on the ES2 simulation settling.

## What the survey established

A header-and-strings survey of the install, with step 0.2's disassembly of the loader and of `util\ovl.cpp`'s asserting function added. Each fact below was read from the files.

### The binaries

| File | Size | Dated | What it is |
|---|---|---|---|
| `DBSIM.EXE` | 730,581 | 1994-10-24 | the simulator |
| `GO.EXE` | 650,157 | 1994-10-24 | the shell: campaign, career, armory |
| `ESCD.EXE`, `DETECT.EXE` | 44,461 / 189,781 | 1994-10-21 | launcher and hardware detection |
| `PLAYFLIC.EXE` | 168,170 | 1994-10-17 | plays `FLIC\*.DFL` |

**Both game executables are 32-bit protected-mode programs**, not 16-bit real-mode ones. Each is the same 1,341-byte real-mode stub, a 16-bit DOS-extender kernel of the FlashTek X-32 family (`DOS extender Copyright 1991-1994 by Doug Huffman`) and a flat 32-bit program image that runs at address 0 of its own segment, as [`../es1/formats/x32-executable.md`](../es1/formats/x32-executable.md) lays out.

**Neither is a Borland build.** ES2's `DBSIM.EXE` and `VSHELL.EXE` both carry `Borland C++ - Copyright 1995 Borland Intl.`; ES1's were built with Symantec C++ (the Zortech line, continued by Digital Mars) and carry no compiler banner. [`../es1/formats/compiler.md`](../es1/formats/compiler.md) gives the evidence and the conventions that follow from it — calling conventions, `this` passing, vtable and class layout — which are the things the ES2 tooling is built around.

**The executables embed their source paths**, which gives a module map before any function is read:

- `DBSIM.EXE`, under `C:\P\M\s\c\`: `alert`, `appinput`, `base`, `bullet`, `cal_alrt`, `collide`, `damage`, `debris`, `explo`, `flyersys`, `gndtex`, `gunlist`, `init`, `main`, `mechsys`, `mechview`, `memory`, `panel`, `phdd`, `rocket`, `terrain`, `vcr`, `world`; under `C:\P\M\s\util\`: `libio`, `ovl`.
- `GO.EXE`, under `src\`: `alert`, `cal_alrt`, `career`, `dialog`, `inventor`, `main`, `mechman`, `memory`, `mg`, `mission`, `shell`, `shellmap`, `system`; under `C:\P\M\s\util\`: `mods`, `my_math`, `ovl`.

ES2's `DBSIM.EXE` names `bullet`, `collide`, `damage`, `debris`, `flyersys`, `libio` and `mechsys` too. The two simulators share 292 identical strings of six characters or more, among them the `TS*` shape classes (`TSShape`, `TSBSPPart`, `TSCellAnimPart`, `TSTexture4Poly`, …), the `GL*` bitmap, font and palette classes, the `AN*` animation classes, the `Assert::` messages, the HERC part names (`LEFT_SHIN`, `RIGHT_SHOULDER`, …), the chassis names and the `WEAPON 1`–`WEAPON 10` labels. ES1's `DBSIM.EXE` also names `data\script.dat`, `data\results.dat` and `data\restore.dat`, files of the same names as ES2 keeps in `data\`, along with `data\prefs.cfg`, `keyjoy.cfg` and `exit.cfg`.

`util\ovl.cpp`'s one asserting function (`ES1DBSIM 000654bc`, `ES1GO 00051918`) opens a named file and reads rows × columns bytes of it into the caller's buffer, asserting at lines 21 and 23; `DBSIM.EXE` calls it from two places, each for 1 × `0xc0` bytes, and `GO.EXE` from eight. Both images also name the HMI driver files `hmidrv.386`, `hmidet.386` and `hmimdrv.386`, which the install keeps in `SOS\`.

### The data

All seven ES1 archives parse with the layout in [`../retail/formats/vol-archive.md`](../retail/formats/vol-archive.md), and every entry is compression type 2 (stored). They hold 2,484 entries: `SIMVOL0` 1,062, `SHELL0` 780, `ZONES` 273, `SPEECH` 161, `SIMVOICE` 147, `SIMSOUND` 58 and `DETECT` 3. [What HercWorks reads](#what-hercworks-reads) lists them by type.

Against ES2's `SIMVOL0`, ES1's has no `.FM`, `.RMP` or high-resolution set (`.HFN`, `.HBA`, `.HB0`–`.HB2`, `.HD0`–`.HD3`), and adds `.64K` and `.TTM`. Its sound effects and voice are `.SFX` where ES2's are RIFF WAVs, and its music is `.HMP`. `.TTM` opens `VER:` … `4.14` `PAG:`: it is Dynamix's TTM animation script, the format of their adventure games, which ScummVM's `dgds` engine reads.

**825 entry names appear in both games' `SIMVOL0`, and 595 of those entries are byte-identical**: 548 of the 556 `.SNC`, all 12 `.OFS`, 7 of 7 `.DCI`, 7 of 8 `.COL`, 5 of 7 `.GL`, 5 of 16 `.DTS`, 5 of 20 `.DAT`, 3 of 63 `.BND`, 2 of 56 `.DBA` and one `.ED1`. Every persistent-object file opens with the class tag and version ES2's files of its type open with — `.DTS` `08 00 14 00` or `03 00 1e 00`, `.DBA` and `.DB0`–`.DB2` `01 00 28 00`, `.DBM` `0e 00 28 00`, `.DPL` `0f 00 28 00`, `.DFN` `05 00 28 00`, `.DCI` `0b 00 28 00`, `.DGS` `01 00 bc 02` — except `SHELL0`'s `WPNDESC.DPL`, which opens `02 00 28 00`.

### What HercWorks reads

Step 0.6's census: `HercWorks.Query census --install ES1 --against ES2` runs every entry of the seven archives through the HercWorks reader bound to it, writes back what parses and compares the bytes. The same pass over ES2's eleven archives is the baseline the notes cite. An entry's reader is the one the VOL browser's registry binds, else the name-list reader for a `.NAM`, the chassis stats reader for a `dat\` file its archive's `MECHS.NAM` names, and the structure shape library reader for every `.DGS`. ES2's registry finds its chassis files by their source timestamps, which ES1's do not share; the 21 files it binds are exactly the 21 names ES2's `MECHS.NAM` lists.

*Same* counts the entries byte-identical to an ES2 entry of the same folder and name in any archive. *Round-trip*: the reader returns a model and writing it back gives the entry's bytes. *Parses*: it returns a model and writing it back does not. *Fails*: it throws, at the reader's cursor offset given. Offsets are into the entry's content.

| Archive | Type | Entries | Same | Round-trip | Parses | Fails | No reader | Notes |
|---|---|---|---|---|---|---|---|---|
| `DETECT` | `.DPL` | 1 | 0 | 1 | 0 | 0 | 0 | |
| `DETECT` | `.DTS` | 1 | 0 | 1 | 0 | 0 | 0 | |
| `DETECT` | `.ED0` | 1 | 0 | 0 | 0 | 0 | 1 | |
| `SHELL0` | `.ANM` | 2 | 0 | 0 | 0 | 0 | 2 | |
| `SHELL0` | `.BIN` | 54 | 0 | 0 | 44 | 10 | 0 | 44 read to the end, and the writer changes them from byte 4, as it does all 18 of ES2's. `C0`–`C3`, `D0`–`D3`, `G0` and `T1_TEXT.BIN` fail with the cursor at `0x10000c`–`0x6e000c`, past their end |
| `SHELL0` | `.BND` | 8 | 0 | 0 | 0 | 0 | 8 | Of the `.BND` files, only `CAM.BND` has a reader |
| `SHELL0` | `.BNK` | 3 | 0 | 0 | 0 | 0 | 3 | |
| `SHELL0` | `.DAT` | 39 | 0 | 0 | 0 | 1 | 38 | The registry binds only `CFG\DAMAGE.DAT`, by name; the component value table reader runs past its end, `0x48` |
| `SHELL0` | `.DBA` | 224 | 1 | 221 | 0 | 3 | 0 | `CONTROLS.DBA` fails at `0x1f24`, `ICONS.DBA` at `0x2e2`, `WPNDESC.DBA` at `0x19ec5` |
| `SHELL0` | `.DBM` | 40 | 0 | 0 | 40 | 0 | 0 | Each is 64,022 bytes and reads to its last byte but one; ES2's are 307,222 and read the same way |
| `SHELL0` | `.DCI` | 3 | 1 | 0 | 0 | 0 | 3 | |
| `SHELL0` | `.DFN` | 14 | 0 | 0 | 0 | 0 | 14 | |
| `SHELL0` | `.DGS` | 6 | 0 | 0 | 6 | 0 | 0 | Read to the end; the reader has no writer |
| `SHELL0` | `.DIG` | 2 | 0 | 0 | 0 | 0 | 2 | |
| `SHELL0` | `.DLG` | 39 | 0 | 0 | 0 | 0 | 39 | |
| `SHELL0` | `.DPL` | 36 | 0 | 1 | 35 | 0 | 0 | 35 read 1,036 of 1,040 bytes, as 28 of ES2's 65 do |
| `SHELL0` | `.HMP` | 13 | 0 | 0 | 0 | 0 | 13 | |
| `SHELL0` | `.LOG` | 1 | 0 | 0 | 0 | 0 | 1 | |
| `SHELL0` | `.MEC` | 1 | 0 | 0 | 0 | 0 | 1 | |
| `SHELL0` | `.RAW` | 14 | 0 | 0 | 0 | 0 | 14 | |
| `SHELL0` | `.SFX` | 3 | 0 | 0 | 0 | 0 | 3 | |
| `SHELL0` | `.SNC` | 162 | 0 | 0 | 0 | 0 | 162 | |
| `SHELL0` | `.STR` | 3 | 0 | 3 | 0 | 0 | 0 | |
| `SHELL0` | `.TTM` | 113 | 0 | 0 | 0 | 0 | 113 | |
| `SIMSOUND` | `.HMP` | 6 | 0 | 0 | 0 | 0 | 6 | |
| `SIMSOUND` | `.SFX` | 52 | 0 | 0 | 0 | 0 | 52 | |
| `SIMVOICE` | `.SFX` | 147 | 0 | 0 | 0 | 0 | 147 | |
| `SIMVOL0` | `.64K` | 2 | 0 | 0 | 0 | 0 | 2 | |
| `SIMVOL0` | `.BND` | 63 | 3 | 0 | 0 | 1 | 62 | `CAM.BND` fails at its end, `0x14` |
| `SIMVOL0` | `.COL` | 18 | 7 | 18 | 0 | 0 | 0 | |
| `SIMVOL0` | `.DAT` | 51 | 5 | 1 | 16 | 20 | 14 | `ROCKETS.DAT` round-trips. The 16 chassis files are 192 bytes against ES2's 216 and fail at their end, as `BULLETS.DAT` (`0x52`) and `BASES.DAT` (`0x1e8`) do; `WEAPONS.DAT` fails at `0x9d6` of `0xad4` and `SKIMMER.DAT` at `0x12` of `0x22`. `EXPLOS.DAT` stops at byte 76 of 794. The 15 `ZONE*.DAT` read as ES2's zone header, below. No reader for `STYLES`, `FFORMS`, `MFORMS`, `BFORMS`, `BASECOL`, `DEBRIS`, `DESERT`, `TEMPER`, `ICE`, `URBAN`, `SHADES`, `TEX`, `TEX_LO` and `TEX_LO2` |
| `SIMVOL0` | `.DB0`–`.DB2` | 24, 8 each | 0 | 24 | 0 | 0 | 0 | |
| `SIMVOL0` | `.DBA` | 104 | 2 | 103 | 0 | 1 | 0 | `CORNERS.DBA` fails at `0x6bc`; it is byte-identical to ES2's, which fails there too |
| `SIMVOL0` | `.DCI` | 8 | 8 | 0 | 0 | 0 | 8 | |
| `SIMVOL0` | `.DFN` | 16 | 0 | 0 | 0 | 0 | 16 | |
| `SIMVOL0` | `.DGS` | 7 | 0 | 0 | 7 | 0 | 0 | Read to the end; the reader has no writer |
| `SIMVOL0` | `.DMG` | 18 | 0 | 0 | 1 | 17 | 0 | 16 fail at their end: `HOVTANK.DMG` and every file that opens with 21 internals, where ES2's HERC files hold 22. `SPIDER.DMG` overflows a count at `0x30`. `SKIMMER.DMG` reads to the end and the writer throws on it, as it does on all 22 of ES2's |
| `SIMVOL0` | `.DPL` | 18 | 0 | 16 | 2 | 0 | 0 | 2 read 1,036 of 1,040 bytes, as ES2's do |
| `SIMVOL0` | `.DTS` | 30 | 5 | 30 | 0 | 0 | 0 | |
| `SIMVOL0` | `.ED0`–`.ED3` | 32, 8 each | 1 | 0 | 0 | 0 | 32 | |
| `SIMVOL0` | `.EDG` | 4 | 0 | 4 | 0 | 0 | 0 | |
| `SIMVOL0` | `.GAU` | 8 | 0 | 0 | 8 | 0 | 0 | 1,700 bytes, ES2's size, read to the end; the written copy differs from `0xbc`, inside the run `0xb4`–`0x1d3` the reader skips as zero in all nine of ES2's |
| `SIMVOL0` | `.GL` | 16 | 5 | 16 | 0 | 0 | 0 | |
| `SIMVOL0` | `.NAM` | 2 | 0 | 0 | 2 | 0 | 0 | The reader has no writer |
| `SIMVOL0` | `.OFS` | 12 | 12 | 12 | 0 | 0 | 0 | |
| `SIMVOL0` | `.PDG` | 17 | 0 | 17 | 0 | 0 | 0 | |
| `SIMVOL0` | `.SNC` | 556 | 548 | 0 | 0 | 0 | 556 | |
| `SIMVOL0` | `.STR` | 14 | 1 | 14 | 0 | 0 | 0 | |
| `SIMVOL0` | `.TTM` | 10 | 0 | 0 | 0 | 0 | 10 | |
| `SIMVOL0` | `.VUE` | 8 | 0 | 8 | 0 | 0 | 0 | |
| `SIMVOL0` | `.WLD` | 24 | 0 | 0 | 24 | 0 | 0 | Stop at byte 71 of 281–287, and the copy differs from `0x1c`; ES2's are 310–313 bytes and round-trip |
| `SPEECH` | `.RAW` | 161 | 0 | 0 | 0 | 0 | 161 | |
| `ZONES` | `.BIN` | 1 | 0 | 0 | 1 | 0 | 0 | Reads to the end, and the writer changes it from byte 4, as it does ES2's |
| `ZONES` | `.DAT` | 116 | 2 | 0 | 91 | 0 | 25 | The 91 `ZONE*.DAT` read as ES2's zone header, below. No reader for `WORLD0`–`WORLD3`, `MFORMS`, `STYLES`, `SING_MSN`, the eleven `ANY_*`, `BTLSET_0`–`BTLSET_2`, `TECH_0`, `TECH_SNG` and `TEST_BTL` |
| `ZONES` | `.ENG` | 79 | 0 | 0 | 79 | 0 | 0 | Stop after 10 bytes; every one opens `01 00 42 52`. ES2's read to the end |
| `ZONES` | `.MSN` | 77 | 0 | 0 | 8 | 69 | 0 | Every one opens with revision 5, as ES2's do. 50 fail allocating an array of negative length, 14 run past their end and 5 read a negative segment length, at offsets from `0xfe` to `0xb212`; the 8 that parse stop at 2,910–10,446 of 9,170–56,392 bytes |

Of the 2,484 entries, 490 round-trip, 364 parse without round-tripping, 122 fail and 1,508 have no HercWorks reader. ES2's eleven archives hold 3,004: 746 round-trip, 912 parse, 2 fail (`CORNERS.DBA` and `SHELL0`'s `ARM_HERC.DBA`) and 1,344 have no reader.

No type parses as a different version: with the one `.DPL` above, the headers that carry a class tag and version match ES2's, and every `.MSN` carries ES2's revision. Where an ES2 reader fails or stops short on ES1's files, the layout under the same header differs. ES1's `ZONE*.DAT`, in both archives, are 4 + 20 × *n* bytes, *n* the word at `+2`; ES2's are 16 bytes, which is all the zone header reader reads.

`TAPES\` holds four demo recordings and a `DEMOLIST.STR`. `DEMO1.TAP`'s first dword is 4558, which reads as the size of a bundled file in the scheme of [`../retail/formats/tap-input-tape.md`](../retail/formats/tap-input-tape.md#the-bundle); whether the rest of that scheme holds is [Open](#open).

### Not ES1

The install also carries `ES2DEMO\` (an ES2 demo; its 723,488-byte `ES.EXE` is the simulator, linked 1996-04-12, a month after the retail `DBSIM.EXE`'s 1996-03-09), `HUNTER\` (Hunter Hunted demo), `CSTORM\` (CyberStorm demo), `DEMO\` (a Sierra demo), `INN\` (ImagiNation Network), `DIRECTX\`, `ACROREAD.EXE` and `SETUP.EXE`. ES1's own executables and archives are dated September and October 1994; these additions are dated 1996. The ES2 demo simulator is an ES2 resource — a second build to diff against retail — and outside this plan.

## Working method

The rules in `CLAUDE.md`, the hooks wired into `.claude/settings.json` and the RE tools in `tools/` apply to ES1 from its first session, and the work is ordered so that each is in place before the work that depends on it. Four principles shape the phases below; the specific rules sit in the phase they govern.

**Foundations before names.** The identifiers, symbol files, Ghidra project and doc tree ([Decisions](#decisions-to-make-before-phase-0)) and tools that work on ES1's binaries (0.3) exist before the first function is named. Phase 0 names nothing.

**Ground truth before reading.** The running game (0.1), the demo tapes and the manual are set up before the disassembly is interpreted, so a reading can be checked against retail as soon as it is made. A claim about something the player sees is checked in the running game before it is written up.

**A map before the territory.** The source-path module map and the match counts (0.7) decide the order of work, and every module has the same definition of done, below. Work is chosen from that map, not from whichever function happens to be on screen.

**ES2 as evidence, not as answers.** ES2's names, docs and code are where to look first and never what to conclude (1.2). Agreement is checked in the ES1 binary; a difference is recorded as the fact the ES1 doc exists to hold.

### When a module is done

A source module (`collide.cpp`, `career.cpp`, …) is done when every item below holds. Done means every gap is listed, not that nothing is left to learn.

- Every function in the module has a `known_symbols_<binary>.json` entry: a name at `high` or `medium` confidence, or a `low` entry whose description says what is missing.
- Every callee read to understand one of them is named, as `CLAUDE.md` requires for ES2.
- Every global and struct field the module reads or writes is in `known_symbols_<binary>.json` or `known_structs.json`, or is listed under its doc's `## Open`.
- The module's behaviour is written up in `docs/es1/`, lint-clean: what matches ES2 by a link to the ES2 section, what differs in full, and everything else as items in that doc's `## Open`.
- At least one of its claims has been confirmed in the running game, by a debugger breakpoint or a capture, as a positive control on the reading.

## Decisions to make before phase 0

The user has accepted 2 and 3, and the step 0.2 tooling uses them. The others are recommendations for the user to accept or change.

1. **Same repository.** `HercWorks.Core` and `HercWorks.Vol` are the largest reusable body of work, and the formats overlap heavily; two repositories would mean fixing a reader twice. The rules, hooks and tools in this repository are also the workflow ES1 needs from its first session.
2. **Binary identifiers.** Both games have a `DBSIM.EXE`, so `DBSIM` stops being unambiguous. Proposed: `ES1DBSIM` and `ES1GO` as the `binary` value in symbol entries, in tool arguments and in `-process`, with files `known_symbols_es1dbsim.json` and `known_symbols_es1go.json`. ES2's identifiers stay as they are. `CLAUDE.md`'s "name the binary when it is not DBSIM" then covers ES1 citations, as `ES1DBSIM` followed by the address.
3. **A separate Ghidra project**, `ES1Recon`, so an ES1 apply can never touch the ES2 databases, and dumps in `tools/analysis_out/` prefixed `ES1DBSIM_` and `ES1GO_`.
4. **A separate retail doc tree**, `Herculan/docs/es1/` with `formats/`, `simulation/` and `shell/` beneath it. Where ES1 matches ES2, the ES1 doc links to the ES2 section and states only what differs, once the match has been checked against the ES1 binary; a shared fact keeps one home. Documentation rule 9 applies to it unchanged.
5. **No engine refactor before phase 2.** As with the rendering backend in [`planning.md`](planning.md#rendering), an abstraction designed before its second implementation exists bakes in the wrong assumptions. The seam is designed when ES1 code is ready to be ported.

## Phase 0 — tooling and measurement

No naming and no porting. Each step has an exit test, and a step is done when its test passes, not when the code is written.

**0.1 Ground truth.** Get ES1 running in DOSBox-X, with a launch recipe like ES2's. Retail ES2 runs under Windows through cnc-ddraw; DOSBox-X has a built-in debugger that breaks on protected-mode addresses and reads memory, so retail ES1 can be inspected directly rather than inferred. Find the manual. *Exit:* a mission starts from the shell, a demo tape plays, and a breakpoint on an address from 0.2 fires in the debugger. All three hold. The CD's own installer, run from the CD copy mounted as a CD drive, makes the full install, and `SOUNDSET` sets a Sound Blaster 16 at port `220`, IRQ 7, DMA 1 with FM music at `388`. DOSBox-X runs it with its default settings and DOS 6.22 reported; the install's `ES.BAT` sets `ALT_DRIVE` to the CD drive and starts `ESCD.EXE`, and `RESOURCE.CFG` plays the movies from the CD's `FLIC\`. `E:\DOS\Play-ES1.cmd`, outside the repository like ES2's launcher, mounts both drives and starts the game. In the debugger (Alt+Pause), memory at the game's code selector and offset `000015a4` holds the image's bytes there, and a breakpoint on it fires: an `ES1DBSIM` address is that offset in the running simulator. The manual is `ESSR10-1.PDF` in the install's root, 80 pages.

**0.2 The loader.** `tools/scripts/es1_x32_extract.py` checks a file against the layout in [`x32-executable.md`](../es1/formats/x32-executable.md), refusing any other stub or kernel, and writes the image to `tools/analysis_out/ES1DBSIM` or `ES1GO` with a `_layout.json` beside it. The image is flat at address 0 and carries no relocations. With `--import` the script loads it raw at base 0 into the `ES1Recon` Ghidra project, running `ES1SetupX32Image` before auto-analysis (the uninitialised tail up to the memory size, the entry point, and the reference analyzers' subroutine option off, since at base 0 every constant below the end of the code reads as a code address) and `ES1SeedFunctionStarts` after it (a function at every prologue that follows a `ret` or `jmp`, which finds the code reached only through pointers). *Exit:* every embedded source-path string has a code reference landing inside a function, and the entry point disassembles as the extender hands control to it. Both hold: the references land inside functions for all 41 strings in `ES1DBSIM` and all 42 in `ES1GO`, and the code at `0xd` reads the command tail through `FS`, the PSP selector the kernel hands over, and zero-fills from the end of the file bytes to the memory size.

**0.3 Tooling by binary, not by name.** Every script that names `DBSIM` or `VSHELL` — `es2_symbols.py`, `es2_xref.py`, `es2_classes.py`, `es2_fieldscan.py`, `es2_late_entries.py`, `es2_naming.py`, `es2_unnamed_callees.py`, `ghidra_full_decomp.py`, `ghidra_apply_all.sh`, `doc_lint.py`, `truncated_enumeration_guard.py` and the three `ES2Apply*` scripts — takes its binaries from one registry, which also records each binary's container format and compiler. The compiler-specific tools need more than a new entry:

| Tool | Built around | For ES1 |
|---|---|---|
| `es2_xref.py`, `es2_classes.py` | the PE section table | read the 0.2 image instead |
| `es2_classes.py`, `borland-rtti.md` | Borland's class records and the typeinfo at `vt-0xc` | ES1's vtables open with a zero dword and no class record has been found ([vtables](../es1/formats/compiler.md#vtables)); class names come from the persistent-class table and the asserts |
| `es2_fieldscan.py` | Borland's `LEA`/`ADD` rebase and stack-spill idioms | re-derive the idioms from ES1 code |
| `es2_late_entries.py` | Borland's `55 8B EC` prologue shapes | the same prologue, which calls and vtable slots also enter through the 1–3 `0x90` bytes before it ([vtables](../es1/formats/compiler.md#vtables)), so an entry on that padding is a real one |

*Exit:* each tool runs on an ES1 binary with a positive control that fires, and a tool that cannot apply to a binary refuses with an error. A tool that silently returns nothing on a binary it does not understand produces a false negative that reads exactly like a real one.

The registry is `tools/scripts/retail_binaries.py`; it also locates the gitignored files in the main checkout from a worktree. What each tool does with ES1's binaries:

| Tool | On `ES1DBSIM` and `ES1GO` | Positive control |
|---|---|---|
| `es2_xref.py` | scans the whole image as code and data, and searches a function's padding entries with its prologue | `operator new` (`ES1DBSIM 000015a4`): the call from `00000d59` through `000015a3` and the one from `0000187e`; vtable `000973fe`'s slot at `00097402` through `0005b875`; `ES1GO`'s startup calling `main` (`00012ec7`) |
| `es2_fieldscan.py` | adds the inlined `MOVS`/`STOS` copies, the `OR reg,reg` null test and a constant added to a pointer, read from `ES1DBSIM`'s code | `0005b7d0` writes `+0x8`, `+0xc`, `+0x10` and `+0x11` through `EDI` with no displacement, and `+0x12` directly; `0004b70f` writes `+0x87` |
| `es2_late_entries.py` | lists entries on padding as PADDING or SPLIT instead of early starts, and counts a padding entry that is a thunk of the function at the prologue as the [1.2](#phase-1--reverse-engineering) form | `ES1DBSIM`'s 232 padding thunks and `ES1GO`'s 43; the late and early shapes fire on symbols placed at `0005b757` and `000003ab` |
| `es2_unnamed_callees.py` | counts a call into padding as a call to the function | callers of `000015a3` and `000015a4` give one callee, `000015a4` |
| `ghidra_full_decomp.py` | dumps from `ES1Recon`; refuses the vtable dump | the function lists hold 3,367 and 2,579 functions: the counts in [`compiler.md`](../es1/formats/compiler.md#libraries-against-game-code), with the 74 and 13 thunks `ES1PaddingThunks` adds and the 21 and 23 library functions step 0.4 defines |
| `ES2Apply*`, `ghidra_apply_all.sh`, `es2_naming.py apply` | match an entry's `binary` to the program name exactly, where a substring match let `DBSIM` entries apply to `ES1DBSIM` | a read-only apply of an `ES1GO` entry renames it, and skips one marked `GO` |
| `es2_naming.py match` | pairs `ES1DBSIM` with `ES1GO`, masking as an address what Ghidra prints as one (8 hex digits, or bare in a jump table), an immediate on a function's entry or inside its own extent, and an immediate in the image's data that is not a power of two or one less; refuses to pair an ES1 image with a PE | the 74 runtime names `es1_libmatch.py` gives both binaries: with `--min 3`, 69 pair with their own twin and none with another name. Of the other five, `__DOSIGN`, `__FLTDBL` and `__memintset` have different instruction counts in the two dumps, `__DMUL` indexes a jump table 2 bytes past the end of the body Ghidra gives it, and `__cinit` is 2 instructions |
| `es2_classes.py`; `es2_naming.py` `rtti`, `vtables`, `classes`, `stats` | refuse | — |

`es2_symbols.py`, `es2_stamp.py`, `doc_lint.py`, `truncated_enumeration_guard.py` and `Check-Symbol.ps1` take the binaries and symbol files from the registry. `es2_naming.py`'s `body`, `callers`, `diff` and `triage` read the decompile dump, which step 0.5 makes.

**0.4 Compiler and runtime library.** Identify the compiler from its runtime library and code generation, then the conventions it implies: calling conventions, `this` register, vtable placement, name mangling, RTTI. If the library can be obtained, build Ghidra Function ID signatures from it so library functions are named before the backlog is counted. Set the `ES1Recon` programs' compiler spec to match. *Exit:* a `docs/es1/formats/` page on the compiler's conventions, playing the part `borland-rtti.md` plays for ES2, and a count of library functions against game functions. [`compiler.md`](../es1/formats/compiler.md) holds the identification, the conventions and the split between game objects and the library region. Both hold. Symantec C++ 6.1's DOS-extender libraries match 87 functions whole in `ES1DBSIM` and 101 in `ES1GO`, and the known-symbols files name the 78 and 92 whose match picks out one function at one address, 21 and 23 of them code Ghidra had not disassembled; `es1_libmatch.py` reruns the match and writes the symbols. ES1's runtime is a later revision, so Function ID signatures built from 6.1 would name only those; the 6.1 runtime source identifies the rest by reading, in phase 1. The CD image is in `Reference/` and is not to be committed. `es1_x32_extract.py` imports with Visual Studio's (`windows`) compiler spec: Ghidra has none for this compiler, `gcc` has no callee-cleaned convention, and `windows`' default `__stdcall` takes each function's cleanup from its `ret N` while its `__cdecl` covers C functions. Pascal's left-to-right order only reverses the parameter numbering.

**0.5 Full dumps.** The `analysis_out/` disassembly, decompile and function list dumps for both ES1 binaries. *Exit:* the dump files exist and `ghidra_full_decomp.py` regenerates them. Both hold: the decompile covers all 3,367 functions of `ES1DBSIM` and all 2,579 of `ES1GO` with none failing, and `es2_naming.py body ES1DBSIM 0005b754` prints that function from it. The script refuses the vtable dump for ES1, whose vtables the sweep cannot find ([Open](#open)).

**0.6 Data census.** Run every HercWorks reader over every ES1 VOL entry and record, per type, whether it parses, parses as a different version, or fails, and at which offset. Round-trip what parses. The 595 entries byte-identical to ES2's need no reader work; the census is about the rest. *Exit:* one table, every type in the ES1 archives in it, nothing sampled. It is [What HercWorks reads](#what-hercworks-reads), and `HercWorks.Query census` reruns it.

**0.7 Cross-game function matching.** Map ES1 functions to ES2's named ones. Byte similarity is no guide across two compilers, so anchors carry it, then propagation from the anchored functions along the call graph and the vtables. The output is a candidate map from ES1 address to ES2 symbol with the evidence for each pairing. A candidate is a hypothesis about the ES1 function, never a name for it: it becomes one only through 1.2. *Exit:* a match count per source module. It holds. `tools/scripts/es1_crossmatch.py` pairs each ES1 binary with both `DBSIM` and `VSHELL`, which both carry the `TS*`, `GL*` and `AN*` libraries, from the dumps and the images, and its docstring states each rule. It writes `analysis_out/<ID>_es2_candidates.json`: per ES1 function its module and each candidate's ES2 address and symbol, evidence, hops from an anchor, and the two functions' sizes, call counts and `ret` operands, with the conflicting proposals beside them.

- **Anchors.** The persistent-class tables give the strongest. Both games register a class with the same 12-byte record of tag, version, factory and name, under the same tags (`TSShape` is tag 8, version `0x14`, in both), so a record found in both pairs the two factories: 30 of `ES1DBSIM`'s records and 25 of `ES1GO`'s are in `DBSIM`, and 29 and 24 in `VSHELL`. The other anchors are a string of six or more characters, an immediate that is not small, a power of two or a mask, and an initialised 8- or 16-byte block that is not text, each when exactly one function in each binary references it. Between `ES1DBSIM` and `DBSIM`, 52 pairs carry a string, 29 a constant and 8 a data block, some more than one.
- **Propagation.** From each pair: its calls, aligned on the callees already paired, where one gap between aligned calls holds one unpaired callee on each side; its callers, where each function has one unpaired caller; and its vtable stores, the tables pairing in order when both functions store the same number and their slots pairing by index when two tables are the same length. An ES1 table opens with a zero dword and takes its slots from `+4`; a zero slot stands where ES2 has its pure-virtual stub, and a slot can enter code Ghidra made no function of (`ES1DBSIM 00063214`, `xor eax,eax; ret 4`). A Borland table ends before the next block's record pointer ([vtable block](../retail/formats/borland-rtti.md#vtable-block)), and 93 of the 98 table pairs that `ES1DBSIM`–`DBSIM` propagation meets are then the same length. Each round takes the proposals of the pairs the round before made; a function proposed with two partners, or against an existing pair, pairs with neither and is kept as a conflict. The Symantec runtime functions `es1_libmatch.py` names take no part: ES2 links Borland's runtime, and ES1's software floating point has no ES2 call to pair with.
- **Result.** `ES1DBSIM`: 293 functions pair with `DBSIM` (118 anchors, the rest 1–6 hops out) and 137 with `VSHELL`, 296 with either, against 17 conflicting proposals. `ES1GO`: 139 with `DBSIM`, 113 with `VSHELL`, 147 with either, against 8. Vtable slots carry 115 of the 175 propagated `ES1DBSIM`–`DBSIM` pairs. The library region that `__cinit` opens ([compiler.md](../es1/formats/compiler.md#libraries-against-game-code)) holds 132 of `ES1DBSIM`'s 296 in 1,169 functions and 108 of `ES1GO`'s 147 in 1,085; the game objects hold 164 in 1,960 and 39 in 1,447. The counts are of the functions Ghidra starts below the data, padding thunks excluded.
- **Checks.** Each class anchor pairs, with no conflict, the factories one record names in both games. Ten candidates read side by side agree in what the two functions do, most at the same field offsets: `000045a4` with `BaseType_LoadShape` (a string), `00006eb4` with `Rocket_HomingSteer` and `0000b4d4` with `WeaponMount_ChargeCapacitor` (constants), `00010c38` with `Mech_WeightedArmorRemaining` (a data block), `0001ff48` with `GridShape_ReadFromStream` and `0003a2a8` with `MfdDisplay_Update` (vtable slots, 1 and 2 hops), `0000b408` with `WeaponMount_RefireTick` and `0001c770` with `FlyerTypes_LoadAll` (callees, 1 and 2 hops), `00007198` with `Rocket_TickUpdate` and `000174cc` with `Mech_AiEnterCombat` (callers, 1 and 2 hops). A caller pairing can join two functions that play the same part without being one function: it pairs `ES1DBSIM`'s startup at `0000000d` with `DBSIM`'s `WinMain`. A candidate can also sit one call away from its counterpart: `000734a0`'s is `Math_Atan2Bam_Thunk`, the one-line forwarder to `Math_Atan2Bam`, whose body `000734a0` is.
- **Modules.** The game objects' assert path strings place each module in the initialised data, which the linker lays out in the code's object order. A function's initialised-data references give the modules it can belong to, and a fit along the code that never steps back a module, missing the fewest functions' references, gives each function every module some best fit assigns it. A row naming one module holds the functions placed in it: 500 of `ES1DBSIM`'s 1,960 game functions and 514 of `ES1GO`'s 1,447. A row "*x* to *y*" holds the functions the fit can place in any module from *x* to *y*: they reference no initialised data between those modules' path strings, and uninitialised data has no path string to read a module by. An earlier or later module is one with no assert. The library region is one row: the C runtime, the `util\` modules and the class libraries share it without the one-object-per-path layout ([compiler.md](../es1/formats/compiler.md#libraries-against-game-code)), and a fit over it puts 73 runtime functions in `ES1GO`'s `my_math.cpp`. *Runtime* counts the functions `es1_libmatch.py` names; *anchored* counts the candidates that are anchors.

`ES1DBSIM`:

| Module | Functions | Runtime | `DBSIM` | anchored | `VSHELL` | anchored | Either |
|---|---|---|---|---|---|---|---|
| an earlier module to `vcr.cpp` | 22 | 1 | 3 | 1 | 0 | 0 | 3 |
| `vcr.cpp` | 1 | 0 | 0 | 0 | 0 | 0 | 0 |
| `vcr.cpp` to `memory.cpp` | 13 | 0 | 0 | 0 | 0 | 0 | 0 |
| `memory.cpp` | 9 | 0 | 2 | 0 | 2 | 0 | 2 |
| `memory.cpp` to `base.cpp` | 80 | 0 | 1 | 1 | 0 | 0 | 1 |
| `memory.cpp` to `explo.cpp` | 5 | 0 | 1 | 1 | 0 | 0 | 1 |
| `base.cpp` to `explo.cpp` | 29 | 0 | 0 | 0 | 0 | 0 | 0 |
| `base.cpp` to `debris.cpp` | 4 | 0 | 1 | 1 | 0 | 0 | 1 |
| `explo.cpp` to `debris.cpp` | 11 | 0 | 0 | 0 | 0 | 0 | 0 |
| `explo.cpp` to `rocket.cpp` | 6 | 0 | 0 | 0 | 0 | 0 | 0 |
| `debris.cpp` to `rocket.cpp` | 32 | 0 | 0 | 0 | 0 | 0 | 0 |
| `debris.cpp` to `bullet.cpp` | 5 | 0 | 3 | 2 | 0 | 0 | 3 |
| `rocket.cpp` to `bullet.cpp` | 23 | 0 | 0 | 0 | 0 | 0 | 0 |
| `rocket.cpp` to `collide.cpp` | 4 | 0 | 1 | 1 | 0 | 0 | 1 |
| `bullet.cpp` to `collide.cpp` | 53 | 0 | 0 | 0 | 0 | 0 | 0 |
| `collide.cpp` | 2 | 0 | 2 | 1 | 0 | 0 | 2 |
| `collide.cpp` to `damage.cpp` | 9 | 0 | 0 | 0 | 0 | 0 | 0 |
| `damage.cpp` | 1 | 0 | 0 | 0 | 0 | 0 | 0 |
| `damage.cpp` to `gunlist.cpp` | 74 | 0 | 4 | 2 | 0 | 0 | 4 |
| `gunlist.cpp` | 26 | 0 | 1 | 1 | 0 | 0 | 1 |
| `gunlist.cpp` to `mechsys.cpp` | 225 | 0 | 12 | 7 | 0 | 0 | 12 |
| `mechsys.cpp` | 7 | 0 | 2 | 1 | 0 | 0 | 2 |
| `mechsys.cpp` to `flyersys.cpp` | 49 | 0 | 1 | 1 | 0 | 0 | 1 |
| `mechsys.cpp` to `world.cpp` | 6 | 0 | 1 | 1 | 0 | 0 | 1 |
| `flyersys.cpp` to `world.cpp` | 30 | 0 | 2 | 0 | 0 | 0 | 2 |
| `world.cpp` | 1 | 0 | 1 | 1 | 1 | 1 | 1 |
| `world.cpp` to `terrain.cpp` | 26 | 0 | 8 | 3 | 0 | 0 | 8 |
| `terrain.cpp` | 1 | 0 | 0 | 0 | 0 | 0 | 0 |
| `terrain.cpp` to `mechview.cpp` | 49 | 0 | 2 | 2 | 0 | 0 | 2 |
| `mechview.cpp` | 13 | 0 | 1 | 0 | 0 | 0 | 1 |
| `mechview.cpp` to `gndtex.cpp` | 89 | 0 | 4 | 2 | 0 | 0 | 4 |
| `gndtex.cpp` to `panel.cpp` | 30 | 0 | 5 | 1 | 0 | 0 | 5 |
| `panel.cpp` | 345 | 0 | 29 | 8 | 1 | 1 | 30 |
| `panel.cpp` to `phdd.cpp` | 34 | 0 | 6 | 2 | 0 | 0 | 6 |
| `phdd.cpp` | 11 | 0 | 0 | 0 | 0 | 0 | 0 |
| `phdd.cpp` to `alert.cpp` | 153 | 0 | 4 | 1 | 0 | 0 | 4 |
| `alert.cpp` | 21 | 0 | 3 | 1 | 0 | 0 | 3 |
| `alert.cpp` to `cal_alrt.cpp` | 19 | 0 | 2 | 2 | 0 | 0 | 2 |
| `cal_alrt.cpp` | 33 | 0 | 9 | 2 | 2 | 2 | 9 |
| `cal_alrt.cpp` to `appinput.cpp` | 97 | 0 | 6 | 2 | 0 | 0 | 6 |
| `appinput.cpp` | 7 | 0 | 1 | 1 | 0 | 0 | 1 |
| `appinput.cpp` to `main.cpp` | 276 | 0 | 41 | 14 | 20 | 6 | 41 |
| `main.cpp` | 1 | 0 | 1 | 0 | 0 | 0 | 1 |
| `main.cpp` to `init.cpp` | 7 | 0 | 0 | 0 | 0 | 0 | 0 |
| `init.cpp` | 21 | 0 | 3 | 1 | 2 | 0 | 3 |
| library region | 1169 | 77 | 130 | 54 | 109 | 52 | 132 |
| **Total** | 3129 | 78 | 293 | 118 | 137 | 62 | 296 |

`ES1GO`:

| Module | Functions | Runtime | `DBSIM` | anchored | `VSHELL` | anchored | Either |
|---|---|---|---|---|---|---|---|
| an earlier module to `memory.cpp` | 42 | 1 | 8 | 5 | 11 | 4 | 11 |
| `memory.cpp` | 9 | 0 | 2 | 0 | 2 | 0 | 2 |
| `memory.cpp` to `alert.cpp` | 14 | 0 | 2 | 1 | 0 | 0 | 2 |
| `alert.cpp` | 2 | 0 | 0 | 0 | 0 | 0 | 0 |
| `alert.cpp` to `cal_alrt.cpp` | 82 | 0 | 2 | 2 | 0 | 0 | 2 |
| `alert.cpp` to `shellmap.cpp` | 6 | 0 | 0 | 0 | 0 | 0 | 0 |
| `cal_alrt.cpp` to `shellmap.cpp` | 37 | 0 | 0 | 0 | 0 | 0 | 0 |
| `shellmap.cpp` | 58 | 0 | 2 | 1 | 0 | 0 | 2 |
| `shellmap.cpp` to `main.cpp` | 177 | 0 | 10 | 5 | 2 | 1 | 11 |
| `main.cpp` | 5 | 0 | 0 | 0 | 0 | 0 | 0 |
| `main.cpp` to `career.cpp` | 47 | 0 | 1 | 0 | 1 | 0 | 1 |
| `career.cpp` | 66 | 0 | 0 | 0 | 0 | 0 | 0 |
| `career.cpp` to `dialog.cpp` | 12 | 0 | 0 | 0 | 0 | 0 | 0 |
| `dialog.cpp` | 1 | 0 | 0 | 0 | 0 | 0 | 0 |
| `dialog.cpp` to `inventor.cpp` | 263 | 0 | 0 | 0 | 1 | 1 | 1 |
| `inventor.cpp` | 179 | 0 | 0 | 0 | 0 | 0 | 0 |
| `inventor.cpp` to `mechman.cpp` | 2 | 0 | 0 | 0 | 0 | 0 | 0 |
| `mechman.cpp` | 71 | 0 | 1 | 0 | 0 | 0 | 1 |
| `mechman.cpp` to `mg.cpp` | 36 | 0 | 0 | 0 | 1 | 1 | 1 |
| `mechman.cpp` to `mission.cpp` | 1 | 0 | 0 | 0 | 0 | 0 | 0 |
| `mg.cpp` to `mission.cpp` | 3 | 0 | 0 | 0 | 0 | 0 | 0 |
| `mission.cpp` | 5 | 0 | 0 | 0 | 0 | 0 | 0 |
| `mission.cpp` to `shell.cpp` | 48 | 0 | 1 | 1 | 2 | 2 | 2 |
| `shell.cpp` | 19 | 0 | 1 | 1 | 0 | 0 | 1 |
| `shell.cpp` to `system.cpp` | 48 | 0 | 0 | 0 | 0 | 0 | 0 |
| `system.cpp` | 99 | 0 | 1 | 1 | 1 | 0 | 1 |
| `system.cpp` to a later module | 115 | 0 | 1 | 1 | 0 | 0 | 1 |
| library region | 1085 | 91 | 107 | 46 | 92 | 43 | 108 |
| **Total** | 2532 | 92 | 139 | 64 | 113 | 52 | 147 |

**0.8 Go or no-go.** A short report from 0.6 and 0.7: per module, how much matched; per file type, how much the readers already handle; what that implies for phases 1 and 2. The user decides from it whether, and when, phase 1 starts. The report:

- **Data.** Of the 2,484 entries, 490 round-trip, and 146 more read whole as their ES2 counterparts do, ES2's own failing to write back the same way (`.BIN`, `.DBM`, `.DGS`, `.DPL`, `.GAU`, `.NAM`, `SKIMMER.DMG`): the bitmaps, palettes, shapes, colliders, gun and cockpit layouts. 339 carry an ES2 header over a different layout: every `.MSN` (77), the `ZONE*.DAT` (106), `.ENG` (79), `.WLD` (24) and `.DMG` (17), 21 `dat\` tables (the 16 chassis files, `WEAPONS`, `BULLETS`, `BASES`, `SKIMMER` and `EXPLOS`), 10 `SHELL0` `.BIN`, 3 `SHELL0` `.DBA`, `CAM.BND` and `DAMAGE.DAT` — the missions and the world they run in. The 1,508 without a reader have none in the VOL browser's registry, which is all the census runs. Three of their types have readers in the engine instead, for ES2's files: 718 `.SNC` portrait scripts (548 byte-identical to ES2's), 30 `.DFN` fonts and 33 `.ED*` clip-region files. The others are the sound effects, music and speech (`.SFX` 202, `.HMP` 19, `.RAW` 175), the 123 `.TTM` scripts the shell runs, and 208 more (`.BND` tuning, dialogs, cursors, 77 `.DAT` tables).
- **Code.** Step 0.7 pairs 296 of `ES1DBSIM`'s 3,129 functions and 147 of `ES1GO`'s 2,532 with an ES2 function. In the library regions it pairs 132 of 1,169 and 108 of 1,085, most of them `TS*`, `GL*` and `AN*` class methods through their vtables. In the game objects it pairs 164 of the simulator's 1,960 and 39 of the shell's 1,447. The simulator's largest groups lie between `appinput.cpp` and `main.cpp` (41), in `panel.cpp` (29), between `gunlist.cpp` and `mechsys.cpp` (12), in `cal_alrt.cpp` (9) and between `world.cpp` and `terrain.cpp` (8). How the modules both simulators name compare is in [`shared-modules.md`](../es1/simulation/shared-modules.md), and the math in [`fixed-point-math.md`](../es1/simulation/fixed-point-math.md): the assembly math library and its tables carry over, the fixed-point primitives round where ES2's truncate.
- **For phase 1.** The data-format step is the census's list above: the mission, zone, world, damage and `dat\` layouts for the simulator; `.TTM`, `.SFX`, `.HMP` and the speech for the shell and the sound. The bitmaps and shapes need no reader work. In the simulator, an ES2 function is a lead for about one game function in twelve; the rest is read fresh, with ES2's docs as where to look by subsystem rather than by function. The shell pairs with almost nothing and is read fresh, `.TTM` first, as 1.1 orders. The [done](#when-a-module-is-done) test needs each module's function list, which step 0.7's margins leave open for most game functions, so reading a module also settles its boundaries. A demo replay reaches code but measures nothing until ES1's replay is shown to repeat ([Open](#open)).
- **For phase 2.** The data layer the engine shares already reads ES1's bitmaps, palettes and shapes. The engine's own `.SNC`, `.DFN` and `.ED*` readers have not been run on ES1's files, 548 of whose `.SNC` are ES2's bytes. An ES1 mission needs ES1 readers for the mission, zone, world and `dat\` files before the engine can load one, and ES1 sound needs readers for `.SFX` and `.HMP`.
- **Recommendation.** Start phase 1's data formats now. The work is the census's list, it depends on neither the simulator's structure nor ES2's simulation settling, and the mission and zone layouts are what every later step reads. Hold the simulator and shell modules until the ES2 simulation has settled, as the opening of this plan sets out.

## Phase 1 — reverse-engineering

Starts after 0.8, and once the ES2 simulation's structure has stopped moving, so the port target is stable.

**1.1 Order of work.** Data formats first: they are the cheapest, the census has already found the differences, and they let HercWorks open ES1 data. Then the simulator module by module from the module map, functions in each worked by caller count, each module taken to [done](#when-a-module-is-done) before the next starts. The shell last: `GO.EXE` and its TTM-driven screens have the least in common with `VSHELL.EXE`. ScummVM's `dgds` engine is a reference for TTM, read against the binary rather than instead of it.

**1.2 Working rules on top of the existing ones.**

- An ES2 match is a hypothesis about an ES1 function. It becomes an ES1 symbol only when the ES1 body has been read and agrees, and the symbol's `source` records which ES2 symbol it was matched from.
- Siblings differ, and two games are the widest sibling gap this project has. Constants, field offsets, record sizes and branch structure are each checked in ES1, never carried over.
- An ES1 doc is written only from checked claims; a candidate that has not been checked goes in that doc's `## Open` section.
- The negative-claim gate holds for ES1 as for ES2, and only with ES1 tools whose positive controls fired in 0.3.
- An ES1 function's `known_symbols` entry is at its prologue (`55 8B EC`), never on the `0x90` padding before it that calls and vtable slots also enter ([vtables](../es1/formats/compiler.md#vtables)). The padding is fall-through, and the prologue is where stamps, the late-entry checks and 0.7's body matching expect a function to start. In `ES1Recon` a padding entry is a Ghidra thunk of the function at the prologue, with no entry of its own, so a call through it decompiles under the function's name with a `thunk_` prefix: `ES1DBSIM 00000d4c`'s call through the 1-byte thunk at `000015a3` reads `thunk_FUN_000015a4(0x14)`, and `0000cf78`'s through the 2-byte one at `0000d41a` reads `thunk_FUN_0000d41c(param_1)`. `ES1PaddingThunks` keeps both programs in this form; it made 232 such thunks in `ES1DBSIM` and 43 in `ES1GO`.

**1.3 Back to ES2.** ES1 holds an older version of much of ES2's code, built by a different compiler. When an ES1 function settles a question an ES2 doc lists under `## Open`, the ES2 doc gets the fact, with the ES1 function cited as evidence.

## Phase 2 — the engine

What the game seam has to carry, from what is already known to differ:

- the archive set and data paths;
- format versions, dispatched on the class tag and version each persistent file already opens with;
- simulation constants and record layouts;
- terrain, which ES1 draws flat;
- the player's machines, since ES1 has no flyable aircraft;
- the display modes;
- the shell, which shares little beyond the data layer.

The two games share a core and each has its own assembly ([`planning.md`](planning.md#how-the-two-games-share-code)). What crosses the core's seams for the items above is designed at the start of phase 2, against the ES1 code that needs it.

## Open

- **Open:** what `util\ovl.cpp` does beyond its asserting function.
- **Open:** whether the program loads and runs code from the HMI driver files in `SOS\`. If it does, that code is outside the images 0.2 extracts.
- **Open:** two of `ES1SeedFunctionStarts`' seeds fall inside a function Ghidra made, and not on the `0x90` padding before that function's prologue: `ES1DBSIM 00034ee3` in `00034ec4`, and `00035038` in `00034f3c`. Ghidra also leaves 9 and 11 bad-instruction errors, and functions over zero-filled data at `ES1DBSIM 000a21b8` and `000a2594` and `ES1GO 0008e720`, `0008eafc` and `000a09c8`.
- **Open:** code Ghidra labels as data inside ES1 functions. In `ES1DBSIM 00038034` it leaves two runs undisassembled (`00038098`, 44 bytes, and `000380c4`, 33 bytes), each ending in a jump to `000380f0`; the second holds the call to `0000d41c` at `000380d7`, which the decompile therefore leaves out. What reaches the two runs, how many functions have such gaps, and whether they are cases of a jump table Ghidra did not follow, is open.
- **Open:** a vtable listing for ES1. `ES2DumpAllVtables` sweeps non-executable blocks for 4-aligned runs of function entries; an ES1 image is one executable block, and its vtables open with a zero dword, sit at any byte address and point into padding.
- **Open:** the module of the functions in step 0.7's "*x* to *y*" rows, most of both images' game objects, and of each library-region function. Uninitialised data and the call graph are the evidence left. Letting the functions placed in one module vote for the addresses they reference lets a large module absorb its neighbours through their globals (`ES1GO`'s `career.cpp`, with nine path strings, loses every function), so such a vote needs a guard against it.
- **Open:** a candidate for the functions no anchor or propagation reaches: 2,833 of `ES1DBSIM`'s 3,129 and 2,385 of `ES1GO`'s 2,532. Ghidra's BSim, which compares decompiled data flow rather than bytes, is the means step 0.7 did not use.
- **Open:** the 17 and 8 conflicting proposals the candidate files list, each a function two pieces of evidence pair differently.
- **Open:** a positive control for `es2_fieldscan.py`'s `OR reg,reg` rule in ES1's code. Over `ES1DBSIM`'s offsets below `0x400` it changes no hit; in `0005b754` the tested register is spilled and not read back.
- **Open:** whether `DBSIM.EXE` and `GO.EXE` are the October 1994 release build. Their dates match the archives they read; comparing hashes against another ES1 copy settles it.
- **Open:** whether ES1's `.TAP` files follow ES2's bundle-then-stream layout beyond the first dword.
- **Open:** whether an ES1 replay reproduces its recording, and whether two plays of one tape match. ES2's replays drift from their recordings, and further when paced to real time ([timing](../retail/formats/tap-input-tape.md#timing)). ES1's View Demo plays faster than real time in places. `ES1DBSIM`'s data holds `dmp` and `Playback error.` / `Frame #%ld` beside `vcr.cpp`'s path string; what prints them, and whether it can report the frame a replay diverges at, is open. Until this is settled a replay reaches code but measures nothing.
- **Open:** where each ES1 layout the [census](#what-hercworks-reads) finds failing or stopping short departs from ES2's, the `.MSN`'s aside ([`../es1/formats/msn-mission-file.md`](../es1/formats/msn-mission-file.md)). The census gives where the ES2 reader stops, which can lie well past the first field that differs.
- **Open:** ES1's display modes. The retail game is described as 320×200; no file read so far confirms it.
- **Unported:** ES1 as a whole — the archive set, the format versions, the simulator and the shell.
