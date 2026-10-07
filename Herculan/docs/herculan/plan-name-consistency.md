# Plan — one home per name

Make a field's name and description live in one place, and have the copies that must exist checked by the build or the doc linter instead of by a reader noticing drift.

All five stages are built.

## Why

One decoded datum appears in up to ten places. Take the mech type in a mission's mech roster:

| place | spelling |
|---|---|
| [`../retail/formats/msn-mission-file.md`](../retail/formats/msn-mission-file.md), row #12 table | "mech type", `0x30` |
| [`../retail/formats/script-dat.md`](../retail/formats/script-dat.md), block 7 rows | "mech type", `0x30` / `0x28` |
| `HercWorks.Core` `.msn` model | `MechRosterEntry144.TypeIndex` |
| `HercWorks.Core` `script.dat` model | `ScriptMechRecord.TypeIndex` |
| `MissionFileTransformer`, `ScriptDatTransformer` | the parse and write order |
| `MissionGenerator` | a word index into a `short[]`, `MechRosterEntry144.TypeIndexWord` |
| `Herculan.Engine` | `MissionPlacement.TypeIndex` |
| `HercWorks.UI` row wrapper | `ScriptMechRow.TypeIndex` |
| `MissionScriptForm.Designer.cs` | `nameof(ScriptMechRow.TypeIndex)` |
| `tools/ghidra_scripts/known_symbols_*.json` | prose in a function description |

The cleanup that prompted this plan found drift in almost every one of those, and three kinds caused most of it:

- **Retail docs naming C# members.** `BinaryFlag`, `SmallDiscrete`, `MiscEntityInfo` and `EntitySpawn164` sat in the format docs after the code had moved on. Rule 9 of `CLAUDE.md` forbids this, but `doc_lint.py` only catches explicit markers, not a bare type or member name.
- **String bindings in the UI.** A grid column bound by a string `DataPropertyName` compiles cleanly after a rename and breaks at run time, and `HercWorks.UI` cannot be built on Linux, so a cloud session verifies UI edits by grep alone.
- **Descriptions restated per layer.** When the `.msn` model, the `script.dat` export, the UI row and the engine record each carry their own comment for the same datum, they disagree.

Renames themselves were the other cost: `RefRow6` meant a spawn point on one type and an unread point on two others, so a text-level rename had to be scoped by hand.

## Constraints

- **The docs keep the evidence.** Value distributions, reader addresses, which offsets an export drops — that is reverse-engineering evidence and stays in the doc tables. Nothing here generates doc tables from code.
- **No new copy.** A central glossary file would be one more place to keep in step. Every step below either removes a copy or puts a check on one.
- **`MissionGenerator` stays a raw-word walk.** It filters, merges and renumbers as it reads, as VSHELL does; it is not to be rebuilt on `MissionFile`'s models.

## Stage 1 — retail docs never name C# members

`tools/scripts/doc_lint.py` rule `csharp-name`, over the retail docs and the `description` strings of `known_symbols_dbsim.json`, `known_symbols_vshell.json`, `known_structs.json` and `known_vtables.json`:

- **C# names** are every type declared under `Herculan/src/HercWorks.Core` and `Herculan/src/Herculan.Engine`, plus every `public` property, field and method there, read by regex.
- **Retail names** are exempt: every `name` in the three `known_*.json` files, the class prefix of each (`Text` from `ESMessage_Ctor`), and every identifier in `ES2/DBSIM.EXE` and `ES2/VSHELL.EXE`. The binaries supply the 3Space class names (`TSPoly`, `ANAnimList`) the DTS model reuses; they are gitignored, so a checkout without `ES2/` reports those few as well.
- **Single words pass.** Only a compound — two words run together, or a word and a digit (`SplashFactor`, `Unknown3`) — is flagged; `Height` or `Data` is as likely prose or a retail keyword. A `Type.Member` token is flagged on its type alone. File names (`MECHS.NAM`) and fixed-point notation (`Q10`) pass.
- In a doc the check reads backticked tokens; in a JSON description, which has no backticks, every identifier outside a quoted span — a quoted span there is literal retail text, such as a menu string (`'AutoRepair All Hercs'`) the EXEs do not carry. The JSON pass also runs `engine-mention` without "the engine", which in a plate comment means DBSIM's own 3D engine.
- Findings sit in the `--engine` listing, so a full run counts them per file and the edit hook reports only the lines an edit wrote. The hook also fires on edits to the `known_*.json` files.

With this in place a doc describes a field ("the paints-ground flag, `0x06`") and only code carries its name, so a C# rename never touches a doc.

## Stage 2 — UI bindings that fail to compile

Every grid column's `DataPropertyName` and every combo column's `DisplayMember`/`ValueMember` in the `HercWorks.UI` `*.Designer.cs` files is `nameof(RowType.X)`, assigned in `InitializeComponent`. A renamed row property is a compile error at the designer line. Whether the designer keeps `nameof` on regeneration is [Open](#open); if it strips it, the assignments move into the form's constructor after `InitializeComponent()`.

## Stage 3 — one description per datum

The `.msn` model owns the description of a field, including the RE facts (reader addresses, value ranges, retail observations). Every other layer that carries the same datum inherits it, and `CLAUDE.md` rule 5 holds new code to this:

- `script.dat` export properties (`ScriptDat.cs`) use `<inheritdoc cref="MechRosterEntry144.TypeIndex"/>`. "Refs as block indices" is said once per record class; the tail-view records add the exported offset as a `<remarks>`. A field the export changes keeps its own summary stating only the difference and linking the model member: the counter arrays (the model holds interleaved pairs, the export splits them into `CounterRefs`/`CounterOps`) and the text refs (renumbered from `.ENG` ids into `mission.str` lines).
- UI row wrappers (`MissionScriptRows.cs`) inherit from the export property they wrap; UI-only notes are `<remarks>`.
- Engine records that hold a datum straight from a Core field inherit the Core member, as a property `inheritdoc` or a nested `<param><inheritdoc/></param>`. A value the loader resolves, converts or assembles (`Position`, `Heading`, `Delay`, `MessageId`, `Point`, `Route`) keeps its own summary.

A layer that names the same datum differently takes the Core name (`ScriptMechRow.TypeIndex`, `ScriptHeading.Degrees`, `Mission.ObjectiveTextRefs`), and Core follows its own `…Ref` convention (`MissionAction82.TargetRef`), unless the difference is the point. Two differences are: `MissionHerc` keeps the shell's wording (`Chassis`, `Weapons`, `AmmoTypes`), and `ScriptDat.ObjectiveTextRefs` is not `MissionText144.ObjectiveLines`, because the export keeps only the populated lines and renumbers them.

## Stage 4 — named word offsets for `MissionGenerator`

Each `.msn` model class declares a `public const int <Property>Word` for every field `MissionGenerator` reads, on the line after the property, written as its byte offset over two (`MechRosterEntry144.TypeIndexWord = 0x30 / 2`, `OutOfActionReportWord = 0x4C / 2`) so it can be checked against the property's summary. The generator indexes records through these instead of literals; a copy or overlay spanning several fields starts at its first field's constant. The offset lives next to the field it describes, and a model restructure shows up as a compile error in the generator rather than a silent misread.

Offsets stay literal where the model has no property to name: the row #2 settings patch and the row #5 debrief (both kept as raw arrays), the row #8 waypoint count (the model holds it as the waypoint array's length), and `VariantSource`'s word 1, which is the condition ref of every row with a variant key. Spans, record sizes and slot bounds are counts and stay literal.

`MissionWordOffsetTests.EveryNamedWordOffsetReadsItsProperty` writes a synthetic `.msn` with one record per row and a distinct value in every word, parses it through `MissionFileTransformer`, and asserts that each named offset in the record's word view reads the same value as its property.

## Stage 5 — a symbol-aware rename tool

`tools/scripts/rename_symbol` is a console project on Roslyn's `MSBuildWorkspace` that loads a solution and calls `Renamer.RenameSymbolAsync` for one type or member, `cref`s included. It is in neither solution.

```
dotnet restore Herculan/HerculanEngine.sln
dotnet run --project tools/scripts/rename_symbol -- Herculan/HerculanEngine.sln <Type[.Member]> <NewName> [--dry-run]
```

- The type part may be a simple, namespace-qualified or metadata name; zero or several matches is an error listing the candidates.
- Comment text, strings, overloads and file names are renamed only with `--comments`, `--strings`, `--overloads`, `--rename-file`.
- It refuses to run while any project has compile errors (`--allow-errors` overrides), because an unrestored solution loads without a workspace diagnostic and Roslyn silently skips references it cannot bind.
- It targets net8.0 and rolls forward to the newest installed runtime, since `MSBuildLocator` offers only SDKs the running runtime can host.

`HercWorks.UI` is outside `HerculanEngine.sln`; Stage 2's `nameof` bindings make the remaining UI references compile errors that the Windows build reports. Stage 3's renames go through this tool.

## Verification

- Stage 1: a planted `` `MechRosterEntry144.TypeIndex` `` or `` `MissionFileTransformer` `` in a format doc is reported by the edit hook; a planted `` `ESMessage_Ctor` ``, `` `Button` ``, `` `MECHS.NAM` `` or `` `Q10` `` is not. A full `--engine` run lists no findings; a quoted `'AutoRepair All Hercs'` in a JSON description passes and an unquoted `ShellHangar.AutoRepair` does not.
- Stage 2: renaming a row property without touching the designer fails the Windows build.
- Stage 4: the offset test passes, and moving a property in a model without updating its constant fails it.
- Stage 5: renaming `MissionGroup164.MemberKind` and back leaves `git diff` empty.

## Open

- **Open:** whether the Windows Forms designer preserves `nameof` in `InitializeComponent` on regeneration, which decides where Stage 2's assignments go.
- **Open:** whether `MSBuildWorkspace` loads `HerculanEngine.sln` on Linux without the WindowsDesktop SDK, given the solution does not include `HercWorks.UI`.
