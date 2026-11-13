# Plan — Metaltech: Earthsiege support

Reverse-engineering *Metaltech: Earthsiege* (ES1) and running it on this engine alongside Earthsiege 2, from the retail files in `ES1/` (git-ignored, like `ES2/`).

The plan has three phases: **phase 0** built the tooling and measured how much of ES2's work carries over, **phase 1** is the reverse-engineering, and **phase 2** is the engine work. Phase 0 is done; phases 1 and 2 run together, a module at a time, under [`plan-es1-first-mission.md`](plan-es1-first-mission.md). Retail behaviour is written up in `docs/es1/`; each module's state is its doc's `## Open`.

## What the survey established

A header-and-strings survey of the install, with step 0.2's disassembly of the loader added. Each fact below was read from the files.

### The binaries

| File | Size | Dated | What it is |
|---|---|---|---|
| `DBSIM.EXE` | 730,581 | 1994-10-24 | the simulator |
| `GO.EXE` | 650,157 | 1994-10-24 | the shell: campaign, career, armory |
| `ESCD.EXE`, `DETECT.EXE` | 44,461 / 189,781 | 1994-10-21 | launcher and hardware detection |
| `PLAYFLIC.EXE` | 168,170 | 1994-10-17 | plays `FLIC\*.DFL` |

Both game executables are 32-bit protected-mode programs on a FlashTek X-32 DOS extender ([`x32-executable.md`](../es1/formats/x32-executable.md)), built with Symantec C++ where ES2's are Borland builds ([`compiler.md`](../es1/formats/compiler.md)).

**The executables embed their source paths**, which gives a module map before any function is read:

- `DBSIM.EXE`, under `C:\P\M\s\c\`: `alert`, `appinput`, `base`, `bullet`, `cal_alrt`, `collide`, `damage`, `debris`, `explo`, `flyersys`, `gndtex`, `gunlist`, `init`, `main`, `mechsys`, `mechview`, `memory`, `panel`, `phdd`, `rocket`, `terrain`, `vcr`, `world`; under `C:\P\M\s\util\`: `libio`, `ovl`.
- `GO.EXE`, under `src\`: `alert`, `cal_alrt`, `career`, `dialog`, `inventor`, `main`, `mechman`, `memory`, `mg`, `mission`, `shell`, `shellmap`, `system`; under `C:\P\M\s\util\`: `mods`, `my_math`, `ovl`.

How the modules both simulators name compare is in [`shared-modules.md`](../es1/simulation/shared-modules.md).

### The data

All seven ES1 archives parse with the layout in [`../retail/formats/vol-archive.md`](../retail/formats/vol-archive.md), and every entry is compression type 2 (stored). They hold 2,484 entries: `SIMVOL0` 1,062, `SHELL0` 780, `ZONES` 273, `SPEECH` 161, `SIMVOICE` 147, `SIMSOUND` 58 and `DETECT` 3.

Against ES2's `SIMVOL0`, ES1's has no `.FM`, `.RMP` or high-resolution set (`.HFN`, `.HBA`, `.HB0`–`.HB2`, `.HD0`–`.HD3`), and adds `.64K` and `.TTM`. Its sound effects and voice are `.SFX` where ES2's are RIFF WAVs, and its music is `.HMP`. `.TTM` is Dynamix's TTM animation script, the format of their adventure games, which ScummVM's `dgds` engine reads ([`ttm-scripts.md`](../es1/formats/ttm-scripts.md)).

Every persistent-object file opens with the class tag and version ES2's files of its type open with, except `SHELL0`'s `WPNDESC.DPL`, which opens `02 00 28 00`. Where an ES2 reader fails or stops short on ES1's files, the layout under the same header differs.

### What HercWorks reads

`HercWorks.Query census --install ES1 --against ES2` runs every entry of the seven archives through the HercWorks reader bound to it, writes back what parses and compares the bytes. An entry's reader is the one the VOL browser's registry binds, else the name-list reader for a `.NAM`, the chassis stats reader for a `dat\` file its archive's `MECHS.NAM` names, and the structure shape library reader for every `.DGS`; ES1's own readers are registered in `TransformerRegistry`'s `Es1Additions`. Each row of the census names the reader and the headers it met, and each ES1 format doc says what the reader reads.

*Round-trip*: the reader returns a model and writing it back gives the entry's bytes. *Parses*: it returns a model and writing it back does not. *Fails*: it throws. *Identical*: the entry's bytes equal an ES2 entry of the same folder and name.

Of ES1's 2,484 entries, 2,468 round-trip and 16 parse without round-tripping: the 13 `.DGS` and 2 `.NAM`, whose readers have no writer, and `WPNDESC.DPL`, whose written copy differs from byte 0. None fail and every entry has a reader. 601 are byte-identical to an ES2 entry. ES2's eleven archives hold 3,004 entries: 2,174 round-trip, 733 parse (the read-only `.WAV`, `.DGS` and `.NAM` readers), 0 fail and 97 have no reader.

### Not ES1

The install also carries `ES2DEMO\` (an ES2 demo; its 723,488-byte `ES.EXE` is the simulator, linked 1996-04-12, a month after the retail `DBSIM.EXE`'s 1996-03-09), `HUNTER\` (Hunter Hunted demo), `CSTORM\` (CyberStorm demo), `DEMO\` (a Sierra demo), `INN\` (ImagiNation Network), `DIRECTX\`, `ACROREAD.EXE` and `SETUP.EXE`. ES1's own executables and archives are dated September and October 1994; these additions are dated 1996. The ES2 demo simulator is an ES2 resource — a second build to diff against retail — and outside this plan.

## Working method

The rules in `CLAUDE.md`, the hooks wired into `.claude/settings.json` and the RE tools in `tools/` apply to ES1 from its first session. Work is chosen from the source-path module map above, not from whichever function happens to be on screen, and every module has the same definition of done.

### When a module is done

A source module (`collide.cpp`, `career.cpp`, …) is done when every item below holds. Done means every gap is listed, not that nothing is left to learn.

- Every function in the module has a `known_symbols_<binary>.json` entry: a name at `high` or `medium` confidence, or a `low` entry whose description says what is missing.
- Every callee read to understand one of them is named, as `CLAUDE.md` requires for ES2.
- Every global and struct field the module reads or writes is in `known_symbols_<binary>.json` or `known_structs.json`, or is listed under its doc's `## Open`.
- The module's behaviour is written up in `docs/es1/`, lint-clean: what matches ES2 by a link to the ES2 section, what differs in full, and everything else as items in that doc's `## Open`.
- At least one of its claims has been confirmed in the running game, by a debugger breakpoint or a capture, as a positive control on the reading.

## Decisions

1. **Same repository.** `HercWorks.Core` and `HercWorks.Vol` are the largest reusable body of work, and the rules, hooks and tools here are the workflow ES1 needs ([the alternatives](planning.md#earthsiege-1)).
2. **Binary identifiers.** Both games have a `DBSIM.EXE`, so ES1's are `ES1DBSIM` and `ES1GO`: the `binary` value in symbol entries, in tool arguments and in `-process`, with files `known_symbols_es1dbsim.json` and `known_symbols_es1go.json`. ES2's identifiers stay as they are. `CLAUDE.md`'s "name the binary when it is not DBSIM" covers ES1 citations, as `ES1DBSIM` followed by the address.
3. **A separate Ghidra project**, `ES1Recon`, so an ES1 apply can never touch the ES2 databases, and dumps in `tools/analysis_out/` prefixed `ES1DBSIM_` and `ES1GO_`.
4. **A separate retail doc tree**, `Herculan/docs/es1/` with `formats/`, `simulation/`, `rendering/` and `shell/` beneath it. Where ES1 matches ES2, the ES1 doc links to the ES2 section and states only what differs, once the match has been checked against the ES1 binary; a shared fact keeps one home. Documentation rule 9 applies to it unchanged.
5. **No engine refactor before phase 2.** As with the rendering backend in [`planning.md`](planning.md#rendering), an abstraction designed before its second implementation exists bakes in the wrong assumptions. The seam is designed when ES1 code is ready to be ported.

## Phase 0 — tooling and measurement

No naming and no porting. Each step had an exit test, and a step was done when its test passed.

**0.1 Ground truth.** ES1 runs in DOSBox-X, launched by `E:\DOS\Play-ES1.cmd` (outside the repository, like ES2's launcher), which mounts the install and the CD. The CD's own installer makes the full install, and `SOUNDSET` sets a Sound Blaster 16 at port `220`, IRQ 7, DMA 1 with FM music at `388`. DOSBox-X's debugger (Alt+Pause) breaks on protected-mode addresses and reads memory: an `ES1DBSIM` address is that offset at the game's code selector. The manual is `ESSR10-1.PDF` in the install's root, 80 pages.

**0.2 The loader.** `tools/scripts/es1_x32_extract.py` checks a file against the layout in [`x32-executable.md`](../es1/formats/x32-executable.md), writes the image to `tools/analysis_out/ES1DBSIM` or `ES1GO` with a `_layout.json` beside it, and with `--import` loads it raw at base 0 into `ES1Recon` ([the import](../../../tools/ghidra_scripts/README.md#es1-import)).

**0.3 Tooling by binary, not by name.** Every script that names `DBSIM` or `VSHELL` takes its binaries from one registry, `tools/scripts/retail_binaries.py`, which also records each binary's container format and compiler and locates the gitignored files in the main checkout from a worktree. A tool that cannot apply to a binary refuses with an error, because a tool that silently returns nothing on a binary it does not understand produces a false negative that reads exactly like a real one. The compiler-specific tools needed more than a new entry:

| Tool | Built around | For ES1 |
|---|---|---|
| `es2_xref.py` | the PE section table | reads the 0.2 image instead, and searches a function's padding entries with its prologue |
| `es2_classes.py`, `borland-rtti.md` | Borland's class records and the typeinfo at `vt-0xc` | refuses: ES1's vtables open with a zero dword and no class record has been found ([vtables](../es1/formats/compiler.md#vtables)); class names come from the persistent-class table and the asserts |
| `es2_fieldscan.py` | Borland's `LEA`/`ADD` rebase and stack-spill idioms | adds the inlined `MOVS`/`STOS` copies, the `OR reg,reg` null test and a constant added to a pointer |
| `es2_late_entries.py` | Borland's `55 8B EC` prologue shapes | the same prologue, which calls and vtable slots also enter through the 1–3 `0x90` bytes before it, so an entry on that padding is a real one; lists it as PADDING or SPLIT |
| `ghidra_full_decomp.py` | `ES2Recon`'s vtable dump | refuses the vtable dump ([Open](#open)) |
| `es2_naming.py` | `rtti`, `vtables`, `classes` and `stats` read Borland's class records | those refuse; `match` pairs `ES1DBSIM` with `ES1GO`, masking addresses, and refuses to pair an ES1 image with a PE |

**0.4 Compiler and runtime library.** [`compiler.md`](../es1/formats/compiler.md) holds the identification, the conventions, the library matches and the split between game objects and the library region; `es1_libmatch.py` reruns the match and writes the symbols. ES1's runtime is a later revision than the Symantec C++ 6.1 whose libraries are matched, so Function ID signatures built from 6.1 would name only those and the rest is identified by reading. The CD image is in `Reference/` and is not to be committed. `es1_x32_extract.py` imports with Visual Studio's (`windows`) compiler spec: Ghidra has none for this compiler, `gcc` has no callee-cleaned convention, and `windows`' default `__stdcall` takes each function's cleanup from its `ret N` while its `__cdecl` covers C functions. Pascal's left-to-right order only reverses the parameter numbering.

**0.5 Full dumps.** `ghidra_full_decomp.py` writes the `analysis_out/` disassembly, decompile and function list dumps for both ES1 binaries from `ES1Recon`, and `es2_naming.py body ES1DBSIM <address>` prints a function from them.

**0.6 Data census.** [What HercWorks reads](#what-hercworks-reads).

**0.7 Cross-game function matching.** `tools/scripts/es1_crossmatch.py` maps ES1 functions to ES2's named ones, from anchors (the persistent-class tables, strings, constants and data blocks that exactly one function references in each binary) and propagation along calls, callers and vtable stores; its docstring states each rule. It pairs each ES1 binary with both `DBSIM` and `VSHELL`, which both carry the `TS*`, `GL*` and `AN*` libraries, and writes `analysis_out/<ID>_es2_candidates.json`: per ES1 function its module and each candidate's ES2 address and symbol, evidence, hops from an anchor, and the two functions' sizes, call counts and `ret` operands, with the conflicting proposals beside them. A candidate is a hypothesis about the ES1 function, never a name for it: it becomes one only through 1.2. A caller pairing can join two functions that play the same part without being one function (it pairs `ES1DBSIM`'s startup at `0000000d` with `DBSIM`'s `WinMain`), and a candidate can sit one call away from its counterpart (`000734a0`'s is `Math_Atan2Bam_Thunk`, the one-line forwarder to `Math_Atan2Bam`, whose body `000734a0` is). In the simulator an ES2 function is a lead for about one game function in twelve, and the shell pairs with almost nothing.

The tool also places each function in a source module: the game objects' assert path strings place each module in the initialised data, which the linker lays out in the code's object order, and a monotone fit over the code gives each function the modules it can belong to. The library region that `__cinit` opens is one row: the C runtime, the `util\` modules and the class libraries share it ([compiler.md](../es1/formats/compiler.md#libraries-against-game-code)).

**0.8 Go or no-go.** The report from 0.6 and 0.7 put the data formats first, since they depend on neither the simulator's structure nor the ES2 simulation, and every later step reads the mission and zone layouts. Phase 1 started there.

## Phase 1 — reverse-engineering

**1.1 Order of work.** Data formats first, then the simulator and shell module by module from the module map, functions in each worked by caller count, each module taken to [done](#when-a-module-is-done) before the next starts. ScummVM's `dgds` engine is a reference for TTM, read against the binary rather than instead of it.

**1.2 Working rules on top of the existing ones.**

- An ES2 match is a hypothesis about an ES1 function. It becomes an ES1 symbol only when the ES1 body has been read and agrees, and the symbol's `source` records which ES2 symbol it was matched from.
- Siblings differ, and two games are the widest sibling gap this project has. Constants, field offsets, record sizes and branch structure are each checked in ES1, never carried over.
- An ES1 doc is written only from checked claims; a candidate that has not been checked goes in that doc's `## Open` section.
- The negative-claim gate holds for ES1 as for ES2, and only with ES1 tools whose positive controls fire.
- An ES1 function's `known_symbols` entry is at its prologue (`55 8B EC`), never on the `0x90` padding before it that calls and vtable slots also enter ([vtables](../es1/formats/compiler.md#vtables)). The padding is fall-through, and the prologue is where stamps, the late-entry checks and 0.7's body matching expect a function to start. In `ES1Recon` a padding entry is a Ghidra thunk of the function at the prologue, with no entry of its own, so a call through it decompiles under the function's name with a `thunk_` prefix: `ES1DBSIM 00000d4c`'s call through the 1-byte thunk at `000015a3` reads `thunk_FUN_000015a4(0x14)`, and `0000cf78`'s through the 2-byte one at `0000d41a` reads `thunk_FUN_0000d41c(param_1)`. `ES1PaddingThunks` keeps both programs in this form; it made 232 such thunks in `ES1DBSIM` and 43 in `ES1GO`.

**1.3 Back to ES2.** ES1 holds an older version of much of ES2's code, built by a different compiler. When an ES1 function settles a question an ES2 doc lists under `## Open`, the ES2 doc gets the fact, with the ES1 function cited as evidence.

## Phase 2 — the engine

The two games share a core and each has its own assembly ([`planning.md`](planning.md#how-the-two-games-share-code)). What crosses the core's seams is designed against the ES1 code that needs it, starting from the first slice and the modules after it in [`plan-es1-first-mission.md`](plan-es1-first-mission.md).

## Open

- **Open:** what `util\ovl.cpp` does beyond its asserting function.
- **Open:** two of `ES1SeedFunctionStarts`' seeds fall inside a function Ghidra made, and not on the `0x90` padding before that function's prologue: `ES1DBSIM 00034ee3` in `00034ec4`, and `00035038` in `00034f3c`. Ghidra also leaves 9 and 11 bad-instruction errors, and functions over zero-filled data at `ES1DBSIM 000a21b8` and `000a2594` and `ES1GO 0008e720`, `0008eafc` and `000a09c8`.
- **Open:** code Ghidra labels as data inside ES1 functions, and how many functions have such gaps. The two runs `ConsoleButtons_HandleCommand` (`ES1DBSIM 00038034`) leaves undisassembled are targets of its `REPNE SCASD` case table, which auto-analysis did not follow ([the console buttons](../es1/simulation/cockpit-weapon-gauges.md#the-console-buttons)); `ES2RepairFunctionBody`'s `switch:` item recovers them.
- **Open:** a vtable listing for ES1. `ES2DumpAllVtables` sweeps non-executable blocks for 4-aligned runs of function entries; an ES1 image is one executable block, and its vtables open with a zero dword, sit at any byte address and point into padding.
- **Open:** the module of each game function the assert strings leave open (`es1_crossmatch.py`'s margins) and of each library-region function. In `ES1DBSIM`'s game objects the copies of one class's vtables and destructors open object files ([vtables](../es1/formats/compiler.md#vtables)), 44 of them, so they place module starts, including modules no assert names: `0001c824`–`0001d5fb`, between `flyersys.cpp` and `world.cpp`, is one ([`world.md`](../es1/simulation/world.md#where-the-module-starts)). Uninitialised data and the call graph are the evidence left for the rest. Letting the functions placed in one module vote for the addresses they reference lets a large module absorb its neighbours through their globals (`ES1GO`'s `career.cpp`, with nine path strings, loses every function), so such a vote needs a guard against it.
- **Open:** a candidate for the functions no anchor or propagation reaches, most of both binaries. Ghidra's BSim, which compares decompiled data flow rather than bytes, is the means `es1_crossmatch.py` does not use.
- **Open:** the conflicting proposals the candidate files list, each a function two pieces of evidence pair differently.
- **Open:** a positive control for `es2_fieldscan.py`'s `OR reg,reg` rule in ES1's code. Over `ES1DBSIM`'s offsets below `0x400` it changes no hit; in `0005b754` the tested register is spilled and not read back.
- **Open:** whether `DBSIM.EXE` and `GO.EXE` are the October 1994 release build. Their dates match the archives they read; comparing hashes against another ES1 copy settles it.
- **Open:** whether ES2's clip-region reader (`ClipRegionFileTransformer`) and ES1's (`Es1ClipRegionsTransformer`, bound to `.EDG` and `.ED0`–`.ED3`) should share one model type.
