# Plan — Metaltech: Earthsiege support

Reverse-engineering *Metaltech: Earthsiege* (ES1) and running it on this engine alongside Earthsiege 2, from the retail files in `ES1/` (git-ignored, like `ES2/`).

This is a plan. Of it, step 0.2 is built and step 0.4 is under way; nothing else is. It is in three phases: **phase 0** builds the tooling and measures how much of ES2's work carries over, **phase 1** is the reverse-engineering, and **phase 2** is the engine work. Phase 0 is meant to start now. Phases 1 and 2 wait on phase 0's numbers and on the ES2 simulation settling.

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

**Neither is a Borland build.** ES2's `DBSIM.EXE` and `VSHELL.EXE` both carry `Borland C++ - Copyright 1995 Borland Intl.`; ES1's were built by a compiler of the Zortech C++ line (later Symantec C++, then Digital Mars C++) and carry no compiler banner. [`../es1/formats/compiler.md`](../es1/formats/compiler.md) gives the evidence and the conventions that follow from it — calling conventions, `this` passing, vtable and class layout — which are the things the ES2 tooling is built around.

**The executables embed their source paths**, which gives a module map before any function is read:

- `DBSIM.EXE`, under `C:\P\M\s\c\`: `alert`, `appinput`, `base`, `bullet`, `cal_alrt`, `collide`, `damage`, `debris`, `explo`, `flyersys`, `gndtex`, `gunlist`, `init`, `main`, `mechsys`, `mechview`, `memory`, `panel`, `phdd`, `rocket`, `terrain`, `vcr`, `world`; under `C:\P\M\s\util\`: `libio`, `ovl`.
- `GO.EXE`, under `src\`: `alert`, `cal_alrt`, `career`, `dialog`, `inventor`, `main`, `mechman`, `memory`, `mg`, `mission`, `shell`, `shellmap`, `system`; under `C:\P\M\s\util\`: `mods`, `my_math`, `ovl`.

ES2's `DBSIM.EXE` names `bullet`, `collide`, `damage`, `debris`, `flyersys`, `libio` and `mechsys` too. The two simulators share 292 identical strings of six characters or more, among them the `TS*` shape classes (`TSShape`, `TSBSPPart`, `TSCellAnimPart`, `TSTexture4Poly`, …), the `GL*` bitmap, font and palette classes, the `AN*` animation classes, the `Assert::` messages, the HERC part names (`LEFT_SHIN`, `RIGHT_SHOULDER`, …), the chassis names and the `WEAPON 1`–`WEAPON 10` labels. ES1's `DBSIM.EXE` also names `data\script.dat`, `data\results.dat` and `data\restore.dat`, files of the same names as ES2 keeps in `data\`, along with `data\prefs.cfg`, `keyjoy.cfg` and `exit.cfg`.

`util\ovl.cpp`'s one asserting function (`ES1DBSIM 000654bc`, `ES1GO 00051918`) opens a named file and reads rows × columns bytes of it into the caller's buffer, asserting at lines 21 and 23; `DBSIM.EXE` calls it from two places, each for 1 × `0xc0` bytes, and `GO.EXE` from eight. Both images also name the HMI driver files `hmidrv.386`, `hmidet.386` and `hmimdrv.386`, which the install keeps in `SOS\`.

### The data

All seven ES1 archives parse with the layout in [`../retail/formats/vol-archive.md`](../retail/formats/vol-archive.md), and every entry is compression type 2 (stored).

| Archive | Entries | Types |
|---|---|---|
| `SIMVOL0` | 1,062 | `.64K` 2, `.BND` 63, `.COL` 18, `.DAT` 51, `.DB0`–`.DB2` 8 each, `.DBA` 104, `.DCI` 8, `.DFN` 16, `.DGS` 7, `.DMG` 18, `.DPL` 18, `.DTS` 30, `.ED0`–`.ED3` 8 each, `.EDG` 4, `.GAU` 8, `.GL` 16, `.NAM` 2, `.OFS` 12, `.PDG` 17, `.SNC` 556, `.STR` 14, `.TTM` 10, `.VUE` 8, `.WLD` 24 |
| `ZONES` | 273 | `.BIN` 1, `.DAT` 116, `.ENG` 79, `.MSN` 77 |
| `SHELL0` | 780 | `.ANM` 2, `.BIN` 54, `.BND` 8, `.BNK` 3, `.DAT` 39, `.DBA` 224, `.DBM` 40, `.DCI` 3, `.DFN` 14, `.DGS` 6, `.DIG` 2, `.DLG` 39, `.DPL` 36, `.HMP` 13, `.LOG` 1, `.MEC` 1, `.RAW` 14, `.SFX` 3, `.SNC` 162, `.STR` 3, `.TTM` 113 |
| `SIMSOUND` | 58 | `.HMP` 6, `.SFX` 52 |
| `SIMVOICE` | 147 | `.SFX` 147 |
| `SPEECH` | 161 | `.RAW` 161 |
| `DETECT` | 3 | `.DPL`, `.DTS`, `.ED0` |

Against ES2's `SIMVOL0`, ES1's has no `.FM`, `.RMP` or high-resolution set (`.HFN`, `.HBA`, `.HB0`–`.HB2`, `.HD0`–`.HD3`), and adds `.64K` and `.TTM`. Its sound effects and voice are `.SFX` where ES2's are RIFF WAVs, and its music is `.HMP`. `.TTM` opens `VER:` … `4.14` `PAG:`: it is Dynamix's TTM animation script, the format of their adventure games, which ScummVM's `dgds` engine reads.

**825 entry names appear in both games' `SIMVOL0`, and 595 of those entries are byte-identical**: 548 of the 556 `.SNC`, all 12 `.OFS`, 7 of 7 `.DCI`, 7 of 8 `.COL`, 5 of 7 `.GL`, 5 of 16 `.DTS`, 5 of 20 `.DAT`, 3 of 63 `.BND`, 2 of 56 `.DBA` and one `.ED1`. Where the content differs, the persistent-object headers sampled still match: `.DTS` opens `08 00 14 00`, `.DBA` `01 00 28 00`, `.DPL` `0f 00 28 00`, `.DFN` `05 00 28 00`. A matching header settles the class tag and version, not the body.

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

**0.1 Ground truth.** Get ES1 running in DOSBox-X, with a launch recipe like ES2's. Retail ES2 runs under Windows through cnc-ddraw; DOSBox-X has a built-in debugger that breaks on protected-mode addresses and reads memory, so retail ES1 can be inspected directly rather than inferred. Find the manual. *Exit:* a mission starts from the shell, a demo tape plays, and a breakpoint on an address from 0.2 fires in the debugger.

**0.2 The loader.** `tools/scripts/es1_x32_extract.py` checks a file against the layout in [`x32-executable.md`](../es1/formats/x32-executable.md), refusing any other stub or kernel, and writes the image to `tools/analysis_out/ES1DBSIM` or `ES1GO` with a `_layout.json` beside it. The image is flat at address 0 and carries no relocations. With `--import` the script loads it raw at base 0 into the `ES1Recon` Ghidra project, running `ES1SetupX32Image` before auto-analysis (the uninitialised tail up to the memory size, the entry point, and the reference analyzers' subroutine option off, since at base 0 every constant below the end of the code reads as a code address) and `ES1SeedFunctionStarts` after it (a function at every prologue that follows a `ret` or `jmp`, which finds the code reached only through pointers). *Exit:* every embedded source-path string has a code reference landing inside a function, and the entry point disassembles as the extender hands control to it. Both hold: the references land inside functions for all 41 strings in `ES1DBSIM` and all 42 in `ES1GO`, and the code at `0xd` reads the command tail through `FS`, the PSP selector the kernel hands over, and zero-fills from the end of the file bytes to the memory size.

**0.3 Tooling by binary, not by name.** Every script that names `DBSIM` or `VSHELL` — `es2_symbols.py`, `es2_xref.py`, `es2_classes.py`, `es2_fieldscan.py`, `es2_late_entries.py`, `es2_naming.py`, `es2_unnamed_callees.py`, `ghidra_full_decomp.py`, `ghidra_apply_all.sh`, `doc_lint.py`, `truncated_enumeration_guard.py` and the three `ES2Apply*` scripts — takes its binaries from one registry, which also records each binary's container format and compiler. The compiler-specific tools need more than a new entry:

| Tool | Built around | For ES1 |
|---|---|---|
| `es2_xref.py`, `es2_classes.py` | the PE section table | read the 0.2 image instead |
| `es2_classes.py`, `borland-rtti.md` | Borland's class records and the typeinfo at `vt-0xc` | ES1's vtables open with a zero dword and no class record has been found ([vtables](../es1/formats/compiler.md#vtables)); class names come from the persistent-class table and the asserts |
| `es2_fieldscan.py` | Borland's `LEA`/`ADD` rebase and stack-spill idioms | re-derive the idioms from ES1 code |
| `es2_late_entries.py` | Borland's `55 8B EC` prologue shapes | the same prologue, which calls and vtable slots also enter through the 1–3 `0x90` bytes before it ([vtables](../es1/formats/compiler.md#vtables)), so an entry on that padding is a real one |

*Exit:* each tool runs on an ES1 binary with a positive control that fires, and a tool that cannot apply to a binary refuses with an error. A tool that silently returns nothing on a binary it does not understand produces a false negative that reads exactly like a real one.

**0.4 Compiler and runtime library.** Identify the compiler from its runtime library and code generation, then the conventions it implies: calling conventions, `this` register, vtable placement, name mangling, RTTI. If the library can be obtained, build Ghidra Function ID signatures from it so library functions are named before the backlog is counted. Set the `ES1Recon` programs' compiler spec to match; 0.2 imported them as `gcc`. *Exit:* a `docs/es1/formats/` page on the compiler's conventions, playing the part `borland-rtti.md` plays for ES2, and a count of library functions against game functions. [`compiler.md`](../es1/formats/compiler.md) holds the identification, the conventions and the split between game objects and the library region. Digital Mars's free 8.31 DOS-extender library matches 46 functions in `ES1DBSIM`, too few for Function ID; signatures need a runtime library of 1993–94, and the library region's split between the C runtime and Dynamix's libraries waits on one. The programs keep the `gcc` compiler spec: Ghidra has none for this compiler, and `gcc`'s `__cdecl` and `__stdcall` cover both conventions the code uses, where Visual Studio's `__thiscall` would put `this` in `ECX`.

**0.5 Full dumps.** The `analysis_out/` disassembly, decompile, function list and vtable dumps for both ES1 binaries. *Exit:* the dump files exist and `ghidra_full_decomp.py` regenerates them.

**0.6 Data census.** Run every HercWorks reader over every ES1 VOL entry and record, per type, whether it parses, parses as a different version, or fails, and at which offset. Round-trip what parses. The 595 entries byte-identical to ES2's need no reader work; the census is about the rest. *Exit:* one table, every type in the ES1 archives in it, nothing sampled.

**0.7 Cross-game function matching.** Map ES1 functions to ES2's named ones. Byte similarity is no guide across two compilers, so anchors carry it: the embedded source paths and `Assert::` strings, the 292 shared strings, distinctive constants and tables (fixed-point constants, the generator's 56-entry seed table, trigonometric tables), then propagation along the call graph from anchored functions. Ghidra Version Tracking or BinDiff over BinExport can do the propagation. The output is a candidate map from ES1 address to ES2 symbol with the evidence for each pairing. A candidate is a hypothesis about the ES1 function, never a name for it: it becomes one only through 1.2. *Exit:* a match count per source module.

**0.8 Go or no-go.** A short report from 0.6 and 0.7: per module, how much matched; per file type, how much the readers already handle; what that implies for phases 1 and 2. The user decides from it whether, and when, phase 1 starts.

## Phase 1 — reverse-engineering

Starts after 0.8, and once the ES2 simulation's structure has stopped moving, so the port target is stable.

**1.1 Order of work.** Data formats first: they are the cheapest, the census has already found the differences, and they let HercWorks open ES1 data. Then the simulator module by module from the module map, functions in each worked by caller count, each module taken to [done](#when-a-module-is-done) before the next starts. The shell last: `GO.EXE` and its TTM-driven screens have the least in common with `VSHELL.EXE`. ScummVM's `dgds` engine is a reference for TTM, read against the binary rather than instead of it.

**1.2 Working rules on top of the existing ones.**

- An ES2 match is a hypothesis about an ES1 function. It becomes an ES1 symbol only when the ES1 body has been read and agrees, and the symbol's `source` records which ES2 symbol it was matched from.
- Siblings differ, and two games are the widest sibling gap this project has. Constants, field offsets, record sizes and branch structure are each checked in ES1, never carried over.
- An ES1 doc is written only from checked claims; a candidate that has not been checked goes in that doc's `## Open` section.
- The negative-claim gate holds for ES1 as for ES2, and only with ES1 tools whose positive controls fired in 0.3.

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

How the seam is built — a game profile object, per-game subclasses, separate assemblies — is decided at the start of phase 2, against the ES1 code that needs it.

## Open

- **Open:** what `util\ovl.cpp` does beyond its asserting function.
- **Open:** whether the program loads and runs code from the HMI driver files in `SOS\`. If it does, that code is outside the images 0.2 extracts.
- **Open:** 82 of `ES1SeedFunctionStarts`' seeds in `ES1DBSIM` and 14 in `ES1GO` fall inside a function Ghidra had already made. All but two (`ES1DBSIM 00034ee3` in `00034ec4`, `00035038` in `00034f3c`) are 1–3 bytes past that function's entry, which Ghidra placed on the `0x90` padding before the prologue, where calls and vtable slots also enter ([vtables](../es1/formats/compiler.md#vtables)); how 0.3's tooling records a function with both entries is open. Ghidra also leaves 25 and 18 disassembly conflicts, on data it typed inside code, and functions over zero-filled data at `ES1DBSIM 000a21b8` and `000a2594` and `ES1GO 0008e720`, `0008eafc` and `000a09c8`.
- **Open:** whether `DBSIM.EXE` and `GO.EXE` are the October 1994 release build. Their dates match the archives they read; comparing hashes against another ES1 copy settles it.
- **Open:** whether ES1's `.TAP` files follow ES2's bundle-then-stream layout beyond the first dword.
- **Open:** what `ESSR10-1.PDF` is. It has six pages and its title metadata reads `ES sr`.
- **Open:** ES1's display modes. The retail game is described as 320×200; no file read so far confirms it.
- **Unported:** ES1 as a whole — the archive set, the format versions, the simulator and the shell.
