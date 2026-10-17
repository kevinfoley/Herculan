# Ghidra scripts

Headless scripts driving the `ES2Recon` Ghidra project over `DBSIM.EXE` and `VSHELL.EXE`, and the `ES1Recon` project over ES1's `ES1DBSIM` and `ES1GO` images.

`tools/scripts/retail_binaries.py` is the registry of those four binaries: for each, its identifier, the file the tools read, its container format (PE, or the flat X-32 image `es1_x32_extract.py` writes), its compiler (Borland C++ or Symantec C++), its Ghidra project and program name, its known-symbols file and its `analysis_out/` dumps. Every Python tool that takes a binary takes it from there, and a tool that depends on one container or compiler refuses a binary of another with an error rather than returning nothing for it. The gitignored files (the retail binaries, the dumps, the Ghidra install and projects) are read from the main checkout, which the registry finds from a worktree's `.git` file, so the tools run the same from a worktree.

## Running one

```sh
tools/ghidra_12.1.2_PUBLIC/support/analyzeHeadless.bat \
    'E:\ES2Stuff\tools\ghidra_project' ES2Recon -process DBSIM.EXE -noanalysis \
    -scriptPath 'E:\ES2Stuff\tools\ghidra_scripts' -postScript ES2DumpAsmBatch 0041a2f0+0041b100 400 out.txt
```

`-noanalysis` reuses the analysed database; drop it only on a fresh import. `-process` picks the binary, and the `Apply*` scripts additionally filter by each JSON entry's own `binary` field, which must name the program exactly (`DBSIM` for `DBSIM.EXE`, `ES1DBSIM` for `ES1DBSIM`), so running them against the wrong `-process` is a no-op rather than a corruption.

Every script that produces bulk output writes to a **file path given as an argument** rather than to stdout, because headless stdout is interleaved with Ghidra's own analysis logging. Per the project's `analysis_out/` rule, that path belongs in the scratchpad unless the dump is long-lived.

Two argument conventions recur:

- **`+`-separated lists** (`ES2DumpAsmBatch`, `ES2DumpCallSites`) — `cmd.exe` silently splits on commas before Ghidra sees the argument. The splitters accept `[+,]`, so `+` is the safe one.
- **Spec files** — the argument is a path to a text file whose *first line is the output path* and whose remaining lines are the work items. Used where the item list is too long or too quoted for a command line.

A single malformed `.java` in this directory breaks **every** script in it, with an OSGi compile error naming the wrong file. That is a compile failure, not a broken environment.

## Knowledge files and the apply pipeline

| File | Consumed by | Holds |
| --- | --- | --- |
| `known_symbols_<binary>.json`: `dbsim`, `vshell`, `es1dbsim`, `es1go` | `ES2ApplySymbolNames` | address → name, description, optional C signature, one file per binary |
| `known_structs.json` | `ES2ApplyStructures` | object layouts, and the parameters to type with them |
| `known_vtables.json` | `ES2ApplyVtables` | vtable slot names and instance addresses |

`known_structs.json` and `known_vtables.json` each carry their schema in an `_readme` entry; the symbols schema is [Known symbols](#known-symbols), below.

`tools/scripts/ghidra_apply_all.sh [--read-only] [BINARY...]` runs all three in the only order that works on a database that has never had them applied: vtables → structs → symbols, for each binary in the registry (or those named), in its own project. `--read-only` is a dry run. All three are idempotent; once the `/ES2` types exist, any one can be run alone in any order.

- **ES2ApplyVtables** — builds one `FunctionDefinitionDataType` per slot under `/ES2/<Name>`, assembles them into a struct of 4-byte function pointers, then applies and labels it at each instance address.
- **ES2ApplyStructures** — builds each object struct at its declared size and places fields with `replaceAtOffset`; checks every declared field width against the resolved type's own length, and refuses overlaps and past-the-end fields. An `inline` base is copied in component by component around the explicit fields, so a derived class can retype its base's vtable pointer. Then types the listed function parameters with a pointer to it, first committing the decompiler's parameters at `ANALYSIS` for a function whose stored prototype is too short.

`tools/scripts/es2_skeletons.py BIN [--write]` keeps the last two files covering every Borland class record: a skeleton struct (bases and vtable pointer) for each class without a layout, a vtable shape for each primary vtable without one, and `this` typing for every vtable slot function and record destructor. It never changes an existing entry.
- **ES2ApplySymbolNames** — renames, writes plate comments, applies signatures. An entry with no `name` gets only a comment, so a guess cannot masquerade as a confirmed symbol. Preserves `/ES2` pointer parameter types across a signature apply, which is what stops it undoing `ES2ApplyStructures`.

### Known symbols

Confirmed address → meaning mappings, one file per binary: `known_symbols_dbsim.json`, `known_symbols_vshell.json`, `known_symbols_es1dbsim.json` and `known_symbols_es1go.json`. `ES2ApplySymbolNames` applies one file to its binary (`es2_naming.py apply BIN` passes the right one), and it is idempotent: re-run it after adding or changing entries. Python tools read and write the files through `tools/scripts/es2_symbols.py`.

One address, one entry: when a later finding revises an existing one, edit that entry in place rather than adding a second. Entries are in ascending address order, so two branches that add symbols insert at different places. `es2_naming.py insert` and `edit` keep the order and refuse a file that breaks it, a duplicate address or name, or an entry of another binary. The apply script aborts on a duplicate.

| Field | Holds |
| --- | --- |
| `address` | hex address, no `0x` prefix (`00411fc4`). An ES1 function's is its prologue, never the `0x90` padding before it, which `ES1Recon` holds as a thunk of the function (`Herculan/docs/herculan/plan-es1-support.md`, 1.2) |
| `binary` | `DBSIM`, `VSHELL`, `ES1DBSIM` or `ES1GO`: the file's own binary, matched against the `-process` program name, less `.EXE`, at apply time |
| `type` | `function` or `data` |
| `confidence` | `high`, `medium` or `low` |
| `name` | the symbol name. Omitted for `low` entries, which get a plate comment only, never a rename or label, so a guess can never look like a confirmed name in the database. `medium` names carry a `maybe_` prefix, written out here rather than added by the apply script. |
| `description` | evidence and meaning, also used as the plate comment body |
| `source` | the repo doc section the entry was taken from |
| `verified` | optional, functions only: a [verification stamp](#verification-stamps), written by `tools/scripts/es2_stamp.py` |
| `signature` | optional, functions only: a full C prototype. Record it only when the argument list was derived from the disassembly by hand, never copied from what Ghidra displays: every prototype in the database is unverified `ANALYSIS`-tier inference, and it labels these functions `__stdcall` where the call sites clean their own stack (`ADD ESP,n` / `POP ECX`), i.e. `__cdecl`. A remaining `param_N` name means the derivation is unfinished; omit the field rather than record a partial one. |

| Confidence | Meaning |
| --- | --- |
| `high` | verified by more than one cross-check: raw disassembly, a byte-exact match against real files, or a doc section that settles it |
| `medium` | a reasoned finding that was not independently cross-checked; named with `maybe_`, never bare |
| `low` | open or unconfirmed; comment only, no rename |

**Who owns a parameter's type.** A function that carries a `signature` owns its whole parameter list, struct types included: write `SimObject *this`, not `int *this`, and do not also list that parameter in `known_structs.json`'s `applications`. A function without one gets its parameter types from `known_structs.json`. The two must not describe the same parameter, because applying a signature replaces the parameter list, so a disagreement would undo the struct typing on every run. `ES2ApplySymbolNames` guards against it — a parameter already typed with a pointer into category `/ES2` is captured before the signature apply and restored after, with a `WARN` naming the two files — but the guard is a safety net, not a licence to record the same fact twice.

### Verification stamps

`confidence` says how sure the name and role are. A stamp says something narrower and stronger: the function's whole body was read, and its `description` checked against it and corrected where it was wrong. A stamped description is trusted without re-reading the body; an unstamped one is a lead.

```json
"verified": {
  "bytes": "0041b468+130",
  "sha1": "dc6133fcf008",
  "scope": "body",
  "via": "disasm",
  "negatives": [
    { "claim": "The player's HERC starts each mission PASSIVE", "evidence": "retail",
      "how": "the user's play of the retail game" }
  ]
}
```

| Field | Holds |
| --- | --- |
| `bytes` | the function's extent in the Ghidra function list (`analysis_out/<BIN>_functions.txt`) when it was stamped: its entry, and the length from there to the end of its body's last address range, so an inline jump table or other bytes between the body's ranges are covered |
| `sha1` | the first 12 hex digits of the SHA-1 of those bytes in the binary's retail file, as `retail_binaries.py` locates it |
| `scope` | `body`: what the description says this function's own code does. `callees`: also every callee the description relies on, read the same way |
| `via` | how the body was read: `decompile`, `disasm`, `both` or `raw`. The decompile is enough for control flow and field offsets; a claim that rests on an argument's value, a register-passed parameter, or where one function ends and the next begins wants the disassembly. `raw` is the capstone disassembly of a straight-line stub up to its `RET`, read from the image, for a stub that has no Ghidra function yet (a frameless vtable slot target): its `bytes` are that decode's extent, `check` lists it as pending and verifies it against the decode until `es2_naming.py apply --define` has made the function and the dumps are regenerated, and from then on against the function list like any other stamp. Absent on stamps made before the field existed |
| `negatives` | optional: a negative claim the description makes ("only", "never", "no other") that is settled, with `evidence` `data` (an exhaustive read of the data settles it, e.g. a field that is -1 in every retail file) or `retail` (observed in the retail game), and `how` |

What a stamp covers is the function's own behaviour, plus its listed negatives. Anything else the description says about other code — who calls it, what another function does — is covered only by that function's own stamp or the doc it cites.

**Who calls a function is a search result or a read, and the description says which.** "Callers found by `es2_xref`: A, B and C" is a search; "`A` passes 3", with `A` read this session, is a read. A bare "called by A, B and C" reads as complete when it is neither, and is how a search result slips into a stamp.

**A search that found nothing is never a stamped fact.** `es2_xref.py`, `es2_fieldscan.py` and every other sweep can only report "not found by this search", whatever its positive control showed: a wider store over the field, a bulk copy, the allocator's zeroing, an alias the scan cannot resolve or a dispatch table filled at run time all escape them. Before stamping, a description that states such a result as fact is reworded to say what was searched and found nothing ("no caller found by `es2_xref`"), and the doc carries it as an `**Open:**` item. It is not listed under `negatives`.

`es2_stamp.py stamp NAME --via decompile|disasm|both|raw [--scope callees] [--negative "claim :: data|retail :: how"]` writes one, after checking the description sentence by sentence: it refuses a sentence that names callers without saying which search found them, and one saying nothing, never or only something calls, reads, writes, references or reaches something (or that code is unused, unreachable or dead) unless it is a listed negative or names its search. A "calls it" after the sentence has named a callee the function calls ("calls `Name (addr)`(block, 1) … calls it with 0") is that callee, not a caller claim. `--read-callers` and `--own-negatives` accept such sentences when they are reads or describe the function's own code ("it makes no other call"); the sentences are printed either way, so the choice is made looking at them. Several targets joined with `+` are stamped in one write; a refused one, whether for its wording or because it cannot be read as asked (no entry, not a function start, not a straight-line stub for `--via raw`), is reported and the rest are still stamped, exit 1. `es2_stamp.py lint` runs the same checks over every stamp already written. The doc-lint hook names any unstamped function in the lines an edit writes to a doc. `es2_stamp.py check` re-reads every stamp and voids it, exit 1, when the function's extent in the function list has changed (a late-entry fix, a re-analysis) or its bytes no longer hash the same (another build in the retail file), because the read it records was of other code. `es2_naming.py edit` drops the stamp of an entry whose description it changes. `Check-Symbol.ps1` shows a stamp and prints a stamped description whole.

Struct-instance offsets (`mech+0x222`) belong in `known_structs.json`, which owns field layout; they are not fixed addresses, so they have no entry here. Vtable slot meanings belong in `known_vtables.json`.

## Catalog

### Disassembly and decompilation

| Script | Args | Does |
| --- | --- | --- |
| `ES2DumpAsmBatch` | `addrs(+)` `maxInsn` `out` | Whole-function disassembly for many functions per run; stops at each body end rather than running into the next function. |
| `ES2DisasmRange` | `start` `len` `out` | Already-disassembled instructions in an address range, plus the containing function. |
| `ES2DisassembleFrom` | `out` `lo` `hi` `starts...` | Disassembles undefined bytes by following flow from each start address, then lists the instructions in `[lo, hi)`. For hand-written assembly reached only through jump tables, such as `IR41_32.DLL`'s decoder. Writes code units unless run `-readOnly`. |
| `ES2DumpFullAsm` | `out` `[-keepundef]` | Whole-program disassembly; collapses runs of untyped bytes into one line unless `-keepundef`. |
| `ES2DecompileContaining` | `addr` `out` | Decompiles the function containing an address. |
| `ES2DecompileContainingBatch` | `spec` | Same for many addresses, deduped by function. |
| `ES2DecompileRange` | `lo` `hi` `out` | Decompiles every function whose entry point falls in a range. |
| `ES2DecompileNamed` | `spec` | Decompiles functions by name. |
| `ES2DumpFullDecomp` | `out` `[timeout]` | Whole-program decompilation. `tools/scripts/ghidra_full_decomp.py` runs it for both binaries into `analysis_out/`. |
| `ES2DumpCallSites` | `addrs(+)` `ctx` `maxSites` `out` | Call sites with surrounding instructions — settles argument setup and `__cdecl` vs `__stdcall`, which the ANALYSIS-tier prototypes get wrong throughout this database. |

### Searching

Reference-manager searches see only what Ghidra has already disassembled and typed; the raw-memory scanners see the bytes. When a negative result matters, the raw scanner is the one that counts.

| Script | Args | Does |
| --- | --- | --- |
| `ES2FindAddressRefs` | `addr` `out` | `Reference`s to an address, each with its containing function. |
| `ES2FindAddressRefsBatch` | `spec` | Same for many addresses in one program load. |
| `ES2FindAddressRangeRefs` | `lo` `hi` `out` | Every stored LE dword falling inside a *range*. Catches base-plus-offset access, where an exact-address search reports zero for data that is written every frame. Pass `lo == hi` for an exact-value search. |
| `ES2FindCallsTo` | `addr` `out` | Raw `E8 rel32` scan — finds callers at sites Ghidra never disassembled, so no `Reference` exists. |
| `ES2FindFieldRefs` | `spec` | Instructions carrying a given scalar operand — locates the readers and writers of a struct field by its displacement. |
| `ES2FindImmediateRefs` | `spec` | Greps decompiled C text for literal substrings. The other half of the field hunt: catches what the operand scan misses, and vice versa. |
| `ES2FindStringRefs` | `spec` | Defined strings matching keywords, with their referencing functions. |
| `ES2RawByteSearch` | `needle` `out` | Case-insensitive ASCII literal scan over initialized blocks. |
| `ES2HexByteSearch` | `hexNeedle` `out` | Exact byte-pattern scan, for tables and structs rather than text. |
| `ES2ScanStringsRange` | `lo` `hi` `minLen` `out` | Every NUL-terminated printable run in a range. |
| `FindCDXrefs` | — | Frozen one-off: xrefs plus instruction context for the five CD-check / `-eggplant` string addresses. Hardcoded; kept as the record of that investigation. |

### Reading memory and data

| Script | Args | Does |
| --- | --- | --- |
| `ES2DumpRange` | `addr` `len` `out` | Raw hex + ASCII, 16/line — the bytes underneath, whatever the typing says. |
| `ES2DumpDataAt` | `out` `addrs...` | How a range is *typed*: label, data type, and each component's field name and value. Use to confirm a structure apply took. |
| `ES2ReadDataValue` | `addr` `len` `out` | A 1/2/4-byte value plus whether its block is initialized — answers "is this global a compile-time constant" instead of guessing. |
| `ES2DumpString` | `addr` `maxLen` `out` | One NUL-terminated string at a raw address, whether or not Ghidra defined it as a string. |
| `ES2DumpStrings` | `spec` | Batch form; a `P` prefix on an address dereferences a pointer there first. |
| `ES2DumpStringPtrArray` | `addr` `count` `out` | An array of pointers-to-strings. |
| `tools/scripts/es2_peek.py` | `BIN` `addr` `[count]` `[--strings\|--words\|--bytes]` | Python, no Ghidra session: a table of dwords from the image file, each resolved to its known-symbols name, the function it enters or lies inside, the function a `0x90` padding run falls through to, or the printable string it points at (without a count, up to the first 0 dword); or consecutive strings, words or bytes. For vtables, label tables and `REPNE SCASD` case tables. |
| `ES2FileOffsetRange` | `out` `startOff` `[endOff]` | Maps a raw PE **file offset** range onto addresses and reports what is there, with both the mapped bytes and the `FileBytes` bytes, so the file-offset reading can be checked against the mapped-address reading. |

### Vtables and class structure

| Script | Args | Does |
| --- | --- | --- |
| `ES2DumpVtable` | `base` `slots` `out` | N consecutive dwords, each resolved to a function name. |
| `ES2DumpVtablesBatch` | `spec` | Same for many tables; spec lines are `label addr slotCount`. |
| `ES2DumpAllVtables` | `out` `[minSlots]` | Every vtable-shaped structure: the typed and labelled ones, then a heuristic sweep for runs of dwords that are all function entry points. `tools/scripts/ghidra_full_decomp.py` runs it for both binaries into `analysis_out/<BINARY>_vtables_full.txt`. |
| `ES2DumpStructs` | `out` | The object structures under the `/ES2` category, from `known_structs.json`, one line per defined field; vtable structs are left to `ES2DumpAllVtables`. `tools/scripts/ghidra_full_decomp.py` runs it for both binaries into `analysis_out/<BINARY>_structs_full.txt`. |
| `ES2DumpClassRegistry` | `countAddr` `arrayBase` `out` | Walks a `ClassItem` registry — buckets of `{id, loadFn, extra}` — rendering each id both as hex and as a byte-swapped FourCC. |

### Repairing the database

These mutate the program. Ghidra routinely places a function entry past the real prologue, occasionally on the fill bytes before it, or never promotes code to a function at all when its only reference is a vtable slot or a table entry. `tools/scripts/es2_late_entries.py` lists the late and early entries from the binary and the dumps, and for ES1's binaries, where code is also entered through the `0x90` padding before a prologue, the functions Ghidra starts on that padding apart from them; and `es2_naming.py fixentry` repairs a batch of them in one headless session: `ES2MergeFunctionAt <entry> auto` for each, then the structure and symbol applies, then `ES2CheckFunctionEntries`.

Code Ghidra never made a function of is listed by `es2_naming.py stats BIN --list`: each `55 8B EC` prologue outside a function, and each code-section gap that is not all fill bytes. Most gap bytes are Borland class records, which sit in the code section. The code among them is reached only through tables: the six-byte `{kind, priority, function}` entries of Borland's `_INIT_` and `_EXIT_` tables, which run on from DBSIM `004a7a5c` and VSHELL `00480fb0`: frameless static constructors and destructors; the `this`-adjusting thunks (`ADD dword ptr [ESP+4],imm8; JMP`) in DBSIM's secondary vtables; and the raster driver's routine tables, hand-written assembly that opens with `ENTER` or none. `es2_naming.py apply BIN --define a+b+...` creates them in the session that applies the names.

| Script | Args | Does |
| --- | --- | --- |
| `ES2DefineFunctionAt` | `addrs...` | Creates a function at each address. Destroys nothing: an existing function is reported and left alone, and no code units are cleared. Run before `ES2ApplySymbolNames`, which skips an address that has no function. |
| `ES2MergeFunctionAt` | `trueEntry` `len\|auto` `out` | Removes every function entered within the range, clears it, and creates one function at `trueEntry`. For an entry split by a stray prologue function. `auto` sweeps through the last body of any function entered in the first 16 bytes, and refuses when that would reach a later function. It also repairs an early start: a function entered up to 16 bytes before `trueEntry` whose body contains it is removed, and the sweep starts at its entry. A removed function's non-default name would survive as a label, so the script also deletes that label and its plate comment at every removed entry other than `trueEntry`. The new function's prototype is committed from the decompiler at `ANALYSIS`, so `ES2ApplyStructures` still finds the parameters it types. |
| `ES2RepairFunctionBody` | `out` `items...` | Extends a function over code its body leaves out. `switch:JMP:T1+T2+...` adds a `COMPUTED_JUMP` reference from an indirect `JMP` to each target, disassembles them, writes a decompiler jump-table override (as Ghidra's `SwitchOverride` does) and re-runs `fixupFunctionBody`, for a jump table auto-analysis did not recover. `tail:ENTRY:LO:HI` disassembles from `LO` and adds every instruction in `[LO, HI)` to `ENTRY`'s body, for an unrolled run reached only by a computed `JMP reg`; a fixup would drop it again. `bytes:ADDR:N` and `ptrs:ADDR:N` type a switch's index and target tables. `clear:ADDR:N`, given before a `switch:` item, clears a pointer auto-analysis typed over an instruction's operand, where it stops the targets disassembling. Reports each touched function's body ranges and decompiles it into `out`, so a `-readOnly` run previews the result. |
| `ES2RedefineFunction` | `clearStart` `trueEntry` `out` | Clears from `clearStart`, recreates at `trueEntry`, and decompiles the result. |
| `ES1PaddingThunks` | `out` `[addrs(+)]` | For an ES1 image: every function Ghidra starts on the `0x90` padding before a `55 8B EC` prologue is started at the prologue, and the padding becomes a thunk of it (`Herculan/docs/herculan/plan-es1-support.md`, 1.2). Handles both of `es2_late_entries.py`'s shapes, PADDING and SPLIT; leaves a named, commented or unexpectedly shaped padding function alone and reports it, and reports a re-created function whose body differs from the old one's past the padding. Decompiles the functions listed in `addrs` afterwards, to show calls through the padding. Run `-readOnly` first. |
| `ES2CheckFunctionEntries` | `out` `addrs...` | Read-only: whether a function starts exactly at each address, and what contains it. Run after any of the above. |
| `ES2RemoveVtableType` | `names...` | Removes the `/ES2/<Name>` struct and slot category `ES2ApplyVtables` built for a vtable shape `known_vtables.json` no longer defines (renamed or dropped); refuses a struct still applied anywhere, so run it after `ES2ApplyVtables`. |

### ES1 import

`tools/scripts/es1_x32_extract.py --import` runs both, around Ghidra's auto-analysis, when it imports an ES1 image raw at base 0 (`Herculan/docs/es1/formats/x32-executable.md`).

| Script | Args | Does |
| --- | --- | --- |
| `ES1SetupX32Image` | `memSize` `entry` | Pre-analysis: makes the image block read/write/execute, adds the uninitialised block up to the memory size, marks the entry point, and turns off the reference analyzers' subroutine option, which at base 0 disassembles at any constant that falls in the code. |
| `ES1SeedFunctionStarts` | — | Post-analysis: creates a function at every `push ebp / mov ebp,esp` or `enter` prologue that follows a `ret` or `jmp`, clearing a pointer Ghidra typed over one, and re-runs analysis. Reports seeds that fall inside an existing function rather than creating them. |

### ES1 naming

Python, not headless. `es1_boilerplate.py` reads the image, the dumps and the known-symbols files and writes only the batch files it is given; `es1_batch.py` writes a batch's brief and runs its writes, applies and regeneration.

| Script | Args | Does |
| --- | --- | --- |
| `tools/scripts/es1_boilerplate.py scan` | `BIN` `lo` `hi` `--module TAG[@addr]...` `[--out batch.json]` `[--source doc#section]` `[--fresh]` | Names a range's per-object-file boilerplate (`Herculan/docs/es1/formats/compiler.md#vtables`): a function or frameless code whose bytes, vtable operands and padding `NOP`s aside, equal a named function's becomes `<Original><TAG>Copy` with a "byte for byte the code of X" description and its vtable slots; a no-op or constant stub is named through a sibling vtable whose same slot holds a named copy, else listed unnamed with its description. Covers slot targets and code with no Ghidra function. `--out` writes an insert-ready batch and `<out>.unnamed.json`; it prints ties, this-adjusting thunks and other uncovered code, the `apply --define` list and the `es2_stamp.py` commands (`--via raw` for the stubs with no function). `--fresh` reruns a named range as a positive control, comparing each proposal with the existing name. |
| `tools/scripts/es1_boilerplate.py bounds` | `BIN` `lo` `hi` | Lists the evidence `compiler.md#where-object-files-meet-in-the-code` places object files by, in address order: a `RET` followed by 1–3 zero bytes outside every function extent up to a 4-byte boundary, each `HeapPtr_Dtor` copy (lone, or opening a run with the `ConfigPart_Dtor` and `TSPartBase_Dtor` copies after it, looked for past `hi` too), the static constructor and exit table entries pointing into the range (bounds read from `Runtime_RunStaticCtors` and `__dodtors`), and the assert source paths the range's code pushes. Leads, not boundaries. |
| `tools/scripts/es1_batch.py brief` | `N` `lo` `hi` `--scratch DIR` `[--notes FILE]` `[--out FILE]` | Writes a batch subagent's brief: `tools/scripts/es1_batch_brief.md`, the rules every batch shares, with the batch number, range and scratch folder filled in and the batch's own section from the notes file. |
| `tools/scripts/es1_batch.py run` | `COMMANDS.txt` `[--from N]` | Runs a batch subagent's command file: each `es2_naming.py insert`/`relink`/`edit` line as a dry run and then with `--write`, each `es1_naming.py twin` and `es2_stamp.py stamp` line as given, each `es2_naming.py apply BIN` read-only and then with `--write` when it reported no errors, `ghidra_full_decomp.py` for the applied binaries (stopping on a name that differs from the database or a failed decompile), the lines after a `# after regen` comment, and `es2_stamp.py check`; then prints the paths to stage without the handoff docs. Any other line is skipped with a note. A failure or a stamp refusal stops the run with the step to resume from; `list` prints the numbered steps. |

### Signatures and analysis state

| Script | Args | Does |
| --- | --- | --- |
| `ES2ListFunctions` | `out` | Every function as `address<TAB>name<TAB>size<TAB>ranges`: the size is the body's byte count, and the ranges are the body's address ranges, half-open hex `start-end` pairs separated by commas. A body Ghidra split around an inline jump table, or around bytes no flow reaches, has several, and its extent (entry to the end of the last range, as `es2_naming.py` and `es2_stamp.py` take it) is longer than its byte count. `tools/scripts/ghidra_full_decomp.py` runs it for both binaries into `analysis_out/<BINARY>_functions.txt` and reports from it. |
| `ES2DumpSignatures` | `known_symbols_<binary>.json` `out` | Read-only companion to `ES2ApplySymbolNames`: pulls committed prototypes back out for every tracked address, tagged `verified` / `analysis` / `default`, so mass-committed guesses can be told from human decisions. |
| `ES2SignatureSourceCensus` | — | Positive control for the above: histograms `SourceType` program-wide. Zero human-sourced signatures anywhere means the detection itself is suspect; DLL thunks should report `IMPORTED`. |
| `ES2CommitAllParams` | `[passes]` | Commits decompiler-inferred prototypes program-wide as `ANALYSIS`. Improves cross-function decompilation; also fills the database with plausible signatures nobody checked. |
| `ES2EnableParamID` | — | Turns on Decompiler Parameter ID, with the default prototype evaluation, and fails if the option did not take. Changes nothing until the next auto-analysis, which then commits inferred prototypes program-wide as `ANALYSIS`. |
