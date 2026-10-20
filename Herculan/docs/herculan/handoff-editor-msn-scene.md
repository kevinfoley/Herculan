# Handoff — the editor shows the `.MSN`

The plan for making the editor show and select a mission straight from its `.MSN`, every record included, instead of from the `script.dat` the game's load generates from it after every edit. Part of the [editor handoff](handoff-editor.md). Paths are under `src/` unless named otherwise.

## Why

The document is already the `.MSN` ([`MissionDocument`](handoff-editor-document.md#holding-the-msn-as-the-document)): every tool writes `.MSN` records, and Save, Undo and Redo work on them. What the editor *shows* is not: the scene, the outliner, picking, the overlays, the selection and most Properties views are built from the `script.dat` that `MissionGenerator` writes after each edit. That generator is the game's load. It drops every record whose condition fails, merges a GUID's records into one, applies one random option per variant table and renumbers everything. So:

- a conditional record cannot be seen or selected unless the generation happens to keep it, and a refusal says it is "not shown in the editor";
- a selection, a link and an undo step hold `script.dat` block indices, which an add, a delete or a condition change renumbers;
- a whole layer exists only to map between the two: `MissionRebuild`, `MissionProvenance` lookups per edit, `ExportedIndex`/`ExportedSlot`/`GuidOf`, `PointFollowers`' index bookkeeping.

## Decided by the user

- The display works on the `.MSN`, not on a generated `script.dat`.
- Every record is always visible, conditional ones included. A conditional record's view says clearly that it is conditional and when it is present.
- No preview: the editor shows one version of the mission, the open's, with nothing to change which flags or options it shows ([What the editor shows](handoff-editor-conditions.md#what-the-editor-shows)).
- UI words: "campaign variable", never "flag"; squad, squadmate; no editor-only names; no cross-mission lookups. Latin-1 only; notes through `EditorText`.

## Kept

- `MissionDocument` and every record-writing tool in `HercWorks.Core/Data/File/Msn` (the edits, `MsnEdit` undo and redo, `SavedMissions`). What changes is how an edit picks the record it writes ([step 4](#steps)): today that choice reads the generation's provenance (`MissionDocument.SlotOf`), which knows only exported records and throws for one the generation dropped.
- Everything game-facing still goes through `MissionGenerator`: Save's `ScriptDatLint` check, File > Export script.dat and the handoff the game plays. The game's own loads do not change.

## What reads the generated `script.dat` today

| Piece | Reads | Replaced by |
|---|---|---|
| `EditorMission.Open` | writes the generated handoff, `MissionScene.Load` over it, `MissionIndex` over its `Mission` | the world loaded with no objects, objects built from the layout ([The new model](#the-new-model)) |
| `MissionScene` objects | `Mission.Placements` from `MissionLoader` (block-7/8/9 slots, block-11 groups, `player.mec` for the squad) | one object per layout roster record, placed by the shared placement rules |
| `MissionIndex` (424) | `Mission` (groups, orders, actions, timers, objectives, setups, pads), block 3 and 4 of the `ScriptDat` | rewritten over the layout, keyed by GUID |
| `MissionRebuild` (329) | regenerates, reloads `Mission`, matches objects to `.MSN` records through provenance, rewrites `player.mec` and `mission.str`, reselects by GUID ↔ index | `SceneSync`: rebuild the layout, re-place objects by identity, add and remove objects; nothing to reselect |
| `EditorSelection` | block indices for group (11), area (4), action (5), route (3), timer (6), objective (12); objects through `Placement.SlotIndex` | GUIDs; an objective by its row-17 index; an object by its row and GUID |
| `MissionOutliner` (344) | `Mission` lists, `MissionIndex`, condition outcomes for dimming | the layout's lists, every record, conditional ones marked |
| `ScenePicker` (229) | `MissionIndex.IsWaiting`, scene objects | unchanged in shape, over the new index |
| `MissionOverlay` (462) | `MissionIndex` routes, areas, pads, `Mission.Coordinates` for the mission box | the new index; the box from the layout's points |
| `GroupOverlay` (169) | `MissionIndex` setups, orders, members | the new index |
| `PointFollowers` (291) | the generated `ScriptDat`'s blocks 1, 3, 4, 7 to 9 and 11 | placement recomputed from the layout plus a drag's override, diffed against what stands |
| `PointMover` (900) | writes a drag's preview into the generated `ScriptDat`; block indices in every target; `Provenance.Slot` for variant points | targets by GUID; preview through an override; `.MSN` written on commit only |
| `MissionLint` (50) | `ScriptDatLint` over the generation, findings by block index | the same lint over the generation, each finding mapped back to its `.MSN` record through provenance (the one display use left) |
| `PropertiesPanel` and views | `MissionIndex`, `Mission`, `ScriptDat` record counts, `Placement` fields, `GuidOf`/`ExportedIndex` at every link | the layout and the new index, GUID links ([survey per view](#views)) |
| Editors and placers | take block indices and call `GuidOf`; select results through `ExportedIndex` | take GUIDs; select the GUID they wrote |
| `ConditionEditing.Outcome`, `VariantEditing.DrawingNow`, `VariantView` | `Generator.Conditions`, `Generator.VariantPicks`, `Provenance.ExportedSlot` | the layout's presence and the option it shows |
| `MissionSquad.PlayerMec` in a rebuild | rebuilds `player.mec` per edit | not needed for display; still built for Export and the game's handoff |
| plain `script.dat` (read-only path) | `MissionLoader.ReadScript`, `ScriptPointRefs`, `WhyReadOnly` | [question 3](#3-a-plain-scriptdat) |

### Views

What each view reads that must change (line counts in parentheses):

- `PropertiesPanel` (610): `GroupMembers`, `SideOf`, `SetupOf`, `ObjectivesOf*`, `IsWaiting`, `DeploymentActionOf`, `KindOf`, `BasePads`; `Mission.Actions` for the action pickers and arrival text; `Placement` fields including `EngagementActionRef`/`DefeatActionRef` (block-5 indices).
- `ActionView` (570): `Mission.Actions`/`ActionTimers`, `LinksOf`, `Areas`, `ObjectAt`; `ScriptDat` roster lengths for the target picker and `Actions[].TextRefs`.
- `TimerView` (162), `TriggerAreaView` (202), `RouteView` (207), `ObjectiveView` (328), `OrderSlotsView` (246): block-index entry points over `Mission`/`MissionIndex`, `ScriptDat` lengths for pickers.
- `ConditionView` (323), `VariantView` (424), `PresentWhenView` (167): already `.MSN`-based, but read the generation's outcomes and picks.
- `RosterFitView` (122), `MachineFitView` (130), `SquadMachinePanel` (196): block-7 slots through `MachineFitter`.
- `PanelLinks` (105), `MissionText` (215), `ActionChoice` (34): build links and labels from block indices.
- `MissionTextView` (100), `MissionView` (83), `PointFields` (78): `.MSN` already, or nothing index-based.

## The new model

### Core: `MissionLayout`

A read-only view of a `MissionFile` with every record present, built in one pass after each edit (the files are a few thousand records). It is what everything that displays a mission reads, and what an edit consults to choose the record it writes.

- **One entry per thing a designer sees**, keyed as the `.MSN` keys it: points, headings, routes, trigger areas, actions, timers, hercs, flyers, structures and ground vehicles, orders and groups by `(row, GUID)`; objectives by row-17 index; conditions by row-1 GUID; text lines by `.ENG` id. Refs stay GUIDs, resolved to entries, never to indices.
- **Each entry's fields** are its records merged by the rule [question 1](#1-a-record-whose-details-differ-by-condition) settles, with the same overlay-or-replace rule per row and the same unset values as the game's merge. Those spans, unset values and variant copies are one table, `MergeRules`, that both use, so the two cannot drift. `TrainingLaunchTests.ReproducesTheRetailTrainingHandoff` checks the generator's output for `TRAIN5` byte for byte; `LoadsEveryRetailMission` checks only that each mission's generated `script.dat` round-trips through `ScriptDatTransformer`, not that it is unchanged.
- **Each field's supplier**: the record whose value is shown, which is the record an edit writes ([step 4](#steps)).
- **Presence**: always, or the conditions it needs, composed from the record's own condition and, for a group member, its group's; a roster record drawing from a variant table names the table and the option shown ([question 2](#2-a-record-that-draws-from-a-random-choice)). Each view states it in plain words.
- **No generation**: no condition is tested, nothing is drawn, nothing is renumbered.

### Engine: shared placement and a world with no objects

- **Placement** moves out of `MissionLoader` into a public, input-agnostic helper in `Herculan.Engine/World`: a group's point (its own, else its slot-0 order's route's first waypoint, else the origin), its heading (its own, else the route's bearing), the paints-ground snap, each member's place by formation slot and the first-claim rule (the first group in file order to name a roster record places it), a roster record's own point and heading, and the squad's spread from group 0. `MissionLoader` calls it with what it resolves from `script.dat`; the editor with what the layout resolves. The game's behavior does not change; the existing loader tests guard it ([question 4](#4-positions)).
- **`MissionScene`** gains a load of the world alone (zone, theater, time of day, models, terrain), which `Load` itself uses; the editor then builds each object with `CreateObject` and settles it as `Load` does (footprints, pads, flattening).

### Editor

- **Identity.** Each scene object carries the `(row, GUID)` of the roster record it stands for (row 12 for a squad herc too). Selections become `GroupSelection(short Guid)`, `AreaSelection(Guid)`, `ActionSelection(Guid)`, `TimerSelection(Guid)`, `RouteSelection(Guid, Waypoint)`, `ObjectiveSelection(int Record)` (row-17 index, kept as today across an undo while the row keeps its length), `ObjectSelection(SceneObject)` and `ConditionSelection(Guid)`. A selection survives every edit that keeps its GUID, with no mapping.
- **`MissionIndex`** is rewritten over the layout and the scene objects, keyed by GUID: group members, routes with the groups walking or naming them, areas, each action's firing and fired links, objectives by subject, setups and pads.
- **`SceneSync`** replaces `MissionRebuild`: after every edit, undo and redo, the layout is rebuilt, every object is placed again by identity, objects for new records are created, objects for removed ones are kept off the scene for an undo, and the overlays and index are refreshed. A move needs no special path: it is an edit like any other.
- **Drag preview.** A drag places followers from the layout with the dragged point (or group point, or radius) overridden; only the commit writes the `.MSN`.
- **The generation** stays inside `MissionDocument`, generated with the open's inputs (the launch's flags, every draw at its lowest), for the Problems list, Save's check and Export only. Nothing displayed reads it.
- **Conditional records.** Every view opens, after the name, with one emphasized line when the record is conditional ("Present only when campaign variable 30 is more than 0", "Present in about half of all loads", "Its type is picked at random each load"), then the Present when control as today. The outliner marks a conditional row "(conditional)"; the scene draws it as any other (the editor's choice, offered for the user's call).

## Decisions on the rewrite's questions

Questions 1 to 4 are the user's choices; 5 stands unless the user says otherwise.

### 1. A record whose details differ by condition

One GUID, several records: a base and overlays (rows 12 to 16) or replacements (rows 6 to 9) under other conditions. In retail, rows 6, 7, 9, 10 and 11 carry no condition on any record, and row 8's 47 conditioned records are all random-choice options (GUID `-1`), so this concerns rows 12 to 16: 726 of 1,683 herc records, 30 of 124 flyer, 574 of 1,949 structure, 5 of 637 order and 31 of 1,247 group records carry a condition (`HercWorks.Query values --field ConditionRef`), most of the roster ones being options. Of the GUIDs two or more records carry, 7 of 93 herc, none of 3 flyer, 562 of 564 structure, 4 of 4 order and 11 of 124 group GUIDs have records of different conditions (`HercWorks.Query repeats`).

**Decided:** the scene and the view show the thing's **base**: the records carrying the same condition as its first record (the one that creates it), merged in file order. Each record under any other condition is listed in the view as a variation ("When campaign variable 30 is more than 0: type APOCA, weapons ..."), its fields editable there. An edit to a base field writes the last base record that sets it, else the first record; an edit inside a variation writes that record. What the user sees is what the mission is when none of the variations apply, and each variation is stated once.

### 2. A record that draws from a random choice

A herc, flyer or structure with a variant key takes its type, fit, point and heading from one option of a table, picked at each load (48% of herc, 30% of flyer and 0.4% of structure records).

**Decided:** the scene shows the **first option** in row-1 order, where the record then stands, as a mission opens today, the same every time. The view says it is picked at random from N options and lists them with what tells them apart (UX plan phase 2). No control changes which option the scene shows.

### 3. A plain `script.dat`

`DATA\script.dat` and a save slot's `script%d.dat` have no `.MSN`. Today the editor starts on `DATA\script.dat`, read-only.

**Decided: dropped.** Keeping it means keeping the whole `script.dat`-based scene, index and read-only path beside the new one, for files nobody can edit. The editor then starts with no mission and the VOL list up, and `Herculan.Engine.Host.Editor`'s command line names a mission, as the list does, instead of a file. `Herculan.Engine.Host` still plays any `script.dat`, an exported one included.

### 4. Positions

Where the game puts things: a roster record's own point, else its group's point spread by formation slot; a group's own point, else its first order's route start; the paints-ground snap; a keyed record taking the option's point and heading; the first group in file order claiming a record; group 0 spreading the squad.

**Decided: the code is shared with the game, not with the converter.** The placement arithmetic moves out of `MissionLoader` into the helper above, called by the game's load with `script.dat` data and by the editor with the layout's. The layout reproduces the converter only where display needs it: the per-row merge (through the shared span table) and the option a keyed record copies from. Condition gating, compaction and renumbering, the rest of `MissionGenerator`, are not copied. With conditions untested, two groups of different conditions can name one record; the first in file order places it, as the game does whenever both are present, and the view lists the other.

### 5. The squad in the scene

Today the squad's objects come from the handoff's `player.mec`: group 0's hercs in practice and `INSTANT ACTION`, the career's squad in a campaign.

**Proposed:** show group 0's member hercs from the `.MSN` in every mission, spread as the squad spawns, with the existing note in a campaign mission that the career's squad replaces them. `player.mec` is still built for Export and the game's handoff.

## Steps

Each step builds at 0 warnings, passes `dotnet test`, leaves the editor usable, and is one commit after the user's in-app check.

1. **Remove the preview** (done). Reasons that refuse a record the generation dropped say it is "not shown in the editor", until step 5 makes them unreachable.
2. **Core layout and shared rules.** `MissionLayout` with presence and suppliers by the answers to questions 1 and 2; the merge span table shared with `MissionGenerator`; the placement helper out of `MissionLoader`; the world-only scene load; `HercWorks.Query repeats` extended to rows 10 to 16 with conditions. No editor change. Tests only where a named edit would break them (a layout that merges a variation into the base; a keyed herc placed at its own point instead of its option's).
3. **Selections and links by GUID.** Every selection, link, picker and editor entry point takes GUIDs; the views still read the generated `Mission`, converting at their edge, and say plainly when the generation has no such record. `MissionRebuild`'s reselection goes.
4. **Edits choose their record from the layout.** `MissionDocument`'s supplier lookups (`SlotOf`, `ListRecord`, `AreaRecord`, `SuppliedByVariant`, `AlternativeOf`, `SetHeaderWord`'s provenance) read the layout, so an edit works on any record; the `.ENG` line supplier likewise. `handoff-editor-document.md`'s [Which record an edit writes](handoff-editor-document.md#which-record-an-edit-writes) is rewritten to match.
5. **The scene from the `.MSN`.** World-only load, objects from the layout, the new `MissionIndex`, `SceneSync` in place of `MissionRebuild`, picker, both overlays, followers, mover and leveller on the new index, the outliner listing every record with conditional rows marked, the Problems list mapped through provenance. Plain `script.dat` per question 3. The largest step; its brief may split it into objects and groups first, then routes and areas.
6. **Views on the layout.** Every Properties view reads the layout and the new index; the conditional line in every view; `Placement` no longer read for editor data; `ExportedIndex`, `ExportedSlot`, `GuidOf` and `Generator.Conditions`/`VariantPicks` gone from the editor.
7. **Docs.** Throughout, each step updates the handoffs it changes; this last pass rewrites [handoff-editor-document.md](handoff-editor-document.md)'s provenance, regeneration and rebuild sections and drops A plain `script.dat`, updates [handoff-editor-viewing.md](handoff-editor-viewing.md) and [handoff-editor.md](handoff-editor.md), and drains this handoff into them.

Class sizes stay under about 1,500 lines; `PointMover` and `EditorHost` shrink (no `script.dat` writes, no read-only path).

## Open

- **Open:** whether every retail variant table has an option holding a draw of 0 ([Not checked](handoff-editor-document.md#not-checked)); with question 2's first option it no longer matters for display, only for the generation behind Save and Export.
