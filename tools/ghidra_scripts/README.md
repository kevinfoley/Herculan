# Ghidra scripts

Headless scripts driving the `ES2Recon` Ghidra project over `DBSIM.EXE` and `VSHELL.EXE`, and the `ES1Recon` project over ES1's `ES1DBSIM` and `ES1GO` images.

## Running one

```sh
tools/ghidra_12.1.2_PUBLIC/support/analyzeHeadless.bat \
    'E:\ES2Stuff\tools\ghidra_project' ES2Recon -process DBSIM.EXE -noanalysis \
    -scriptPath 'E:\ES2Stuff\tools\ghidra_scripts' -postScript ES2DumpAsmBatch 0041a2f0+0041b100 400 out.txt
```

`-noanalysis` reuses the analysed database; drop it only on a fresh import. `-process` picks the binary, and the `Apply*` scripts additionally filter by each JSON entry's own `binary` field, so running them against the wrong `-process` is a no-op rather than a corruption.

Every script that produces bulk output writes to a **file path given as an argument** rather than to stdout, because headless stdout is interleaved with Ghidra's own analysis logging. Per the project's `analysis_out/` rule, that path belongs in the scratchpad unless the dump is long-lived.

Two argument conventions recur:

- **`+`-separated lists** (`ES2DumpAsmBatch`, `ES2DumpCallSites`) — `cmd.exe` silently splits on commas before Ghidra sees the argument. The splitters accept `[+,]`, so `+` is the safe one.
- **Spec files** — the argument is a path to a text file whose *first line is the output path* and whose remaining lines are the work items. Used where the item list is too long or too quoted for a command line.

A single malformed `.java` in this directory breaks **every** script in it, with an OSGi compile error naming the wrong file. That is a compile failure, not a broken environment.

## Knowledge files and the apply pipeline

| File | Consumed by | Holds |
| --- | --- | --- |
| `known_symbols_dbsim.json`, `known_symbols_vshell.json` | `ES2ApplySymbolNames` | address → name, description, optional C signature, one file per binary |
| `known_structs.json` | `ES2ApplyStructures` | object layouts, and the parameters to type with them |
| `known_vtables.json` | `ES2ApplyVtables` | vtable slot names and instance addresses |

`known_structs.json` and `known_vtables.json` each carry their schema in an `_readme` entry; the symbols schema is [Known symbols](#known-symbols), below.

`tools/scripts/ghidra_apply_all.sh` runs all three in the only order that works on a database that has never had them applied: vtables → structs → symbols, for DBSIM then VSHELL. All three are idempotent; once the `/ES2` types exist, any one can be run alone in any order.

- **ES2ApplyVtables** — builds one `FunctionDefinitionDataType` per slot under `/ES2/<Name>`, assembles them into a struct of 4-byte function pointers, then applies and labels it at each instance address.
- **ES2ApplyStructures** — builds each object struct at its declared size and places fields with `replaceAtOffset`; checks every declared field width against the resolved type's own length, and refuses overlaps and past-the-end fields. Then types the listed function parameters with a pointer to it.
- **ES2ApplySymbolNames** — renames, writes plate comments, applies signatures. An entry with no `name` gets only a comment, so a guess cannot masquerade as a confirmed symbol. Preserves `/ES2` pointer parameter types across a signature apply, which is what stops it undoing `ES2ApplyStructures`.

### Known symbols

Confirmed address → meaning mappings, one file per binary: `known_symbols_dbsim.json` and `known_symbols_vshell.json`. `ES2ApplySymbolNames` applies one file to its binary (`es2_naming.py apply BIN` passes the right one), and it is idempotent: re-run it after adding or changing entries. Python tools read and write the files through `tools/scripts/es2_symbols.py`.

One address, one entry: when a later finding revises an existing one, edit that entry in place rather than adding a second. Entries are in ascending address order, so two branches that add symbols insert at different places. `es2_naming.py insert` and `edit` keep the order and refuse a file that breaks it, a duplicate address or name, or an entry of the other binary. The apply script aborts on a duplicate.

| Field | Holds |
| --- | --- |
| `address` | hex address, no `0x` prefix (`00411fc4`) |
| `binary` | `DBSIM` or `VSHELL`: the file's own binary, matched against the `-process` file name at apply time |
| `type` | `function` or `data` |
| `confidence` | `high`, `medium` or `low` |
| `name` | the symbol name. Omitted for `low` entries, which get a plate comment only, never a rename or label, so a guess can never look like a confirmed name in the database. `medium` names carry a `maybe_` prefix, written out here rather than added by the apply script. |
| `description` | evidence and meaning, also used as the plate comment body |
| `source` | the repo doc section the entry was taken from |
| `signature` | optional, functions only: a full C prototype. Record it only when the argument list was derived from the disassembly by hand, never copied from what Ghidra displays: every prototype in the database is unverified `ANALYSIS`-tier inference, and it labels these functions `__stdcall` where the call sites clean their own stack (`ADD ESP,n` / `POP ECX`), i.e. `__cdecl`. A remaining `param_N` name means the derivation is unfinished; omit the field rather than record a partial one. |

| Confidence | Meaning |
| --- | --- |
| `high` | verified by more than one cross-check: raw disassembly, a byte-exact match against real files, or a doc section that settles it |
| `medium` | a reasoned finding that was not independently cross-checked; named with `maybe_`, never bare |
| `low` | open or unconfirmed; comment only, no rename |

**Who owns a parameter's type.** A function that carries a `signature` owns its whole parameter list, struct types included: write `SimObject *this`, not `int *this`, and do not also list that parameter in `known_structs.json`'s `applications`. A function without one gets its parameter types from `known_structs.json`. The two must not describe the same parameter, because applying a signature replaces the parameter list, so a disagreement would undo the struct typing on every run. `ES2ApplySymbolNames` guards against it — a parameter already typed with a pointer into category `/ES2` is captured before the signature apply and restored after, with a `WARN` naming the two files — but the guard is a safety net, not a licence to record the same fact twice.

Struct-instance offsets (`mech+0x222`) belong in `known_structs.json`, which owns field layout; they are not fixed addresses, so they have no entry here. Vtable slot meanings belong in `known_vtables.json`.

## Catalog

### Disassembly and decompilation

| Script | Args | Does |
| --- | --- | --- |
| `ES2DumpAsmBatch` | `addrs(+)` `maxInsn` `out` | Whole-function disassembly for many functions per run; stops at each body end rather than running into the next function. |
| `ES2DisasmRange` | `start` `len` `out` | Already-disassembled instructions in an address range, plus the containing function. |
| `ES2DumpFullAsm` | `out` `[-keepundef]` | Whole-program disassembly; collapses runs of untyped bytes into one line unless `-keepundef`. |
| `ES2DecompileContaining` | `addr` `out` | Decompiles the function containing an address. |
| `ES2DecompileContainingBatch` | `spec` | Same for many addresses, deduped by function. |
| `ES2DecompileRange` | `lo` `hi` `out` | Decompiles every function whose entry point falls in a range. |
| `ES2DecompileNamed` | `spec` | Decompiles functions by name. |
| `ES2DumpFullDecomp` | `out` `[timeout]` | Whole-program decompilation. `tools/scripts/ghidra_full_decomp.py` runs it for both binaries into `analysis_out/` and reports how many DBSIM functions carry `known_symbols_dbsim.json` names. |
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

These three mutate the program. Ghidra routinely places a function entry past the real prologue, or never promotes code to a function at all when its only reference is a vtable slot. `tools/scripts/es2_late_entries.py` lists the late entries from the binary and the dumps, and `es2_naming.py fixentry` repairs a batch of them in one headless session: `ES2MergeFunctionAt <entry> auto` for each, then the structure and symbol applies, then `ES2CheckFunctionEntries`.

| Script | Args | Does |
| --- | --- | --- |
| `ES2DefineFunctionAt` | `addrs...` | Creates a function at each address. Destroys nothing: an existing function is reported and left alone, and no code units are cleared. Run before `ES2ApplySymbolNames`, which skips an address that has no function. |
| `ES2MergeFunctionAt` | `trueEntry` `len\|auto` `out` | Removes every function entered within the range, clears it, and creates one function at `trueEntry`. For an entry split by a stray prologue function. `auto` sweeps through the last body of any function entered in the first 16 bytes, and refuses when that would reach a later function. A removed function's non-default name would survive as a label, so the script also deletes that label and its plate comment at every removed entry other than `trueEntry`. The new function's prototype is committed from the decompiler at `ANALYSIS`, so `ES2ApplyStructures` still finds the parameters it types. |
| `ES2RedefineFunction` | `clearStart` `trueEntry` `out` | Clears from `clearStart`, recreates at `trueEntry`, and decompiles the result. |
| `ES2CheckFunctionEntries` | `out` `addrs...` | Read-only: whether a function starts exactly at each address, and what contains it. Run after any of the above. |
| `ES2RemoveVtableType` | `names...` | Removes the `/ES2/<Name>` struct and slot category `ES2ApplyVtables` built for a vtable shape `known_vtables.json` no longer defines (renamed or dropped); refuses a struct still applied anywhere, so run it after `ES2ApplyVtables`. |

### ES1 import

`tools/scripts/es1_x32_extract.py --import` runs both, around Ghidra's auto-analysis, when it imports an ES1 image raw at base 0 (`Herculan/docs/es1/formats/x32-executable.md`).

| Script | Args | Does |
| --- | --- | --- |
| `ES1SetupX32Image` | `memSize` `entry` | Pre-analysis: makes the image block read/write/execute, adds the uninitialised block up to the memory size, marks the entry point, and turns off the reference analyzers' subroutine option, which at base 0 disassembles at any constant that falls in the code. |
| `ES1SeedFunctionStarts` | — | Post-analysis: creates a function at every `push ebp / mov ebp,esp` or `enter` prologue that follows a `ret` or `jmp`, clearing a pointer Ghidra typed over one, and re-runs analysis. Reports seeds that fall inside an existing function rather than creating them. |

### Signatures and analysis state

| Script | Args | Does |
| --- | --- | --- |
| `ES2ListFunctions` | `out` | Every function as `address<TAB>name<TAB>size`. |
| `ES2DumpSignatures` | `known_symbols_<binary>.json` `out` | Read-only companion to `ES2ApplySymbolNames`: pulls committed prototypes back out for every tracked address, tagged `verified` / `analysis` / `default`, so mass-committed guesses can be told from human decisions. |
| `ES2SignatureSourceCensus` | — | Positive control for the above: histograms `SourceType` program-wide. Zero human-sourced signatures anywhere means the detection itself is suspect; DLL thunks should report `IMPORTED`. |
| `ES2CommitAllParams` | `[passes]` | Commits decompiler-inferred prototypes program-wide as `ANALYSIS`. Improves cross-function decompilation; also fills the database with plausible signatures nobody checked. |
| `ES2EnableParamID` | — | Turns on Decompiler Parameter ID, with the default prototype evaluation, and fails if the option did not take. Changes nothing until the next auto-analysis, which then commits inferred prototypes program-wide as `ANALYSIS`. |
