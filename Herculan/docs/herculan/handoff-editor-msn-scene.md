# Handoff — the editor shows the `.MSN`

The plan for making the editor show and select a mission straight from its `.MSN`, every record included, instead of from the `script.dat` the game's load generates from it after every edit. Part of the [editor handoff](handoff-editor.md). Paths are under `src/` unless named otherwise.

## Why

The document is already the `.MSN` ([`MissionDocument`](handoff-editor-document.md#holding-the-msn-as-the-document)): every tool writes `.MSN` records, and Save, Undo and Redo work on them. What the editor *shows* is only partly the `.MSN`: the scene's objects, groups, routes and trigger areas are placed from the layout and the outliner lists it ([steps 5a to 5c](#steps)), but the Properties views are built from the `script.dat` that `MissionGenerator` writes after each edit, and the mission check runs over it. That generator is the game's load. It drops every record whose condition fails, merges a GUID's records into one, applies one random option per variant table and renumbers everything. So:

- a conditional record cannot be viewed unless the generation happens to keep it: an object, group, route or area it drops stands in the scene, but its view says it is "not shown in the editor" or absent from the mission as opened, and a refusal says the same;
- the views hold `script.dat` block indices, which an add, a delete or a condition change renumbers;
- a whole layer exists only to map between the two: `GeneratedIndex`, `ExportedIndex`/`ExportedSlot`/`GuidOf`, and the generation's indices each object's `Placement` carries.

## Decided by the user

- The display works on the `.MSN`, not on a generated `script.dat`.
- Every record is always visible, conditional ones included. A conditional record's view says clearly that it is conditional and when it is present.
- No preview: the editor shows one version of the mission, the open's, with nothing to change which flags or options it shows ([What the editor shows](handoff-editor-conditions.md#what-the-editor-shows)).
- UI words: "campaign variable", never "flag"; squad, squadmate; no editor-only names; no cross-mission lookups. Latin-1 only; notes through `EditorText`.
- A group waiting on an action that brings it in by drop pod or on foot (verbs 2 to 5) is not in the scene, neither drawn nor picked, as retail neither draws nor touches it before it lands; its own point is never used, since it lands relative to wherever the player is when the action fires ([`../retail/simulation/mission-deployment.md`](../retail/simulation/mission-deployment.md#arrival--group_deploymentcheck-004236c4)). It is selected from the outliner, and its view says how it arrives. A group arriving in place (any other verb) stands on its point, where it goes live.

## Kept

- `MissionDocument` and every record-writing tool in `HercWorks.Core/Data/File/Msn` (the edits, `MsnEdit` undo and redo, `SavedMissions`). An edit picks the record it writes from the layout ([step 4](#steps)), so it works on any record, one the generation drops included.
- Everything game-facing still goes through `MissionGenerator`: Save's `ScriptDatLint` check, File > Export script.dat and the handoff the game plays. The game's own loads do not change.

## What reads the generated `script.dat` today

| Piece | Reads | Replaced by |
|---|---|---|
| `GeneratedIndex` (405) | `Mission` (groups, orders, actions, timers, objectives, setups, pads), block 3 and 4 of the `ScriptDat`, for the views | the new index and the layout in each view (step 6) |
| `MissionLint` (71) | `ScriptDatLint` over the generation, each finding mapped back to the `.MSN` record that created its record through provenance (`MissionDocument.GuidOf`, `ObjectiveEditing.RecordOf`); the squad count from the generated `Mission`'s placements | stays: the one display use of the generation |
| `PropertiesPanel` and views | `MissionIndex`, `Mission`, `ScriptDat` record counts, `Placement` fields, `GeneratedRecords` at every link and choice | the layout and the new index ([survey per view](#views)) |
| Editors and placers | refuse a record the generation has none for (`GeneratedRecords`); `OrderEditor.Slots` reads the generated blocks; a placer's join and `SideChanger` take a group by block index | edit any record; take GUIDs |
| `ConditionEditing.Outcome`, `VariantEditing.DrawingNow`, `VariantView` | `Generator.Conditions`, `Generator.VariantPicks`, `Provenance.ExportedSlot` | the layout's presence and the option it shows |

### Views

What each view reads that must change (line counts in parentheses):

- `PropertiesPanel` (610): `GroupMembers`, `SideOf`, `SetupOf`, `ObjectivesOf*`, `IsWaiting`, `DeploymentActionOf`, `KindOf`, `BasePads`; `Mission.Actions` for the action pickers and arrival text; `Placement` fields including `EngagementActionRef`/`DefeatActionRef` (block-5 indices).
- `ActionView` (570): `Mission.Actions`/`ActionTimers`, `LinksOf`, `Areas`, `ObjectAt`; `ScriptDat` roster lengths for the target picker and `Actions[].TextRefs`.
- `TimerView` (162), `TriggerAreaView` (206), `RouteView` (211), `ObjectiveView` (328), `OrderSlotsView` (246): block-index entry points over `Mission`/`GeneratedIndex`, `ScriptDat` lengths for pickers; the area's corners and the route's waypoints are already the new index's.
- `ConditionView` (323), `VariantView` (424): already `.MSN`-based, but read the generation's outcomes and picks. `PresentWhenView` reads the `.MSN` and the layout, but resolves what loses its reference to an absent record to block indices.
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

- **Placement** is `MissionPlacementRules` in `Herculan.Engine/World`, over plain values: a group's point (its own, else its slot-0 order's route's first waypoint, else the origin), its heading (its own, else the route's bearing) and the paints-ground snap (`GroupSetup`); the first-claim rule (the first group in file order to name a roster record places it, at its raw slot index; group 0 claims nothing), generic over the record's key (`ClaimMembers`); each member's place by formation slot, a roster record's own point and heading winning (`PlaceHerc`, `PlaceFlyer`, `PlaceStructure`); the squad's spread from group 0 (`PlaceSquadMember`); and a pad's layout and lead member (`PadLayout`, `PadLead`). `MissionLoader` calls it with what it resolves from `script.dat`, keyed by roster slot; the editor calls it with what the layout resolves, keyed by GUID ([question 4](#4-positions)).
- **`MissionScene`** has a load of the world alone (zone, theater, time of day, models, terrain; `LoadWorld`), whose parts `Load` itself loads; the editor builds each object with `CreateObject` and stands them on the ground as `Load` does, footprints, pads and one flattening pass (`Settle`).

### Editor

- **Identity.** Each scene object stands for the `(row, GUID)` of one roster record (row 12 for a squad herc too), which `MissionIndex` maps both ways. Selections are `GroupSelection(short Guid)`, `AreaSelection(Guid)`, `ActionSelection(Guid)`, `TimerSelection(Guid)`, `RouteSelection(Guid, Waypoint)`, `ObjectiveSelection(int Record)` (row-17 index, kept while row 17 has a record there), `ObjectSelection(Row, Guid)`, the roster record the object stands for, and `ConditionSelection(Guid)`. A selection survives every edit that keeps its GUID, with no mapping.
- **`MissionIndex`** is built over the layout's placement (`LayoutPlacement`) and the scene objects, keyed by GUID: the objects and their records, each group's members, setup, pad, roster, side, the action it waits on and its display number (5a); each route's waypoints with the groups walking or naming it, each area's shape with the actions testing it, each action's areas, and the mission box from row 6 (5b). What else fires an action, what it fires and the objectives by subject join it with the views that read them (step 6).
- **`SceneSync`**: after every edit, undo and redo, every record is placed again from the layout, each object stands for its record by identity, objects for new records (or a changed type or fit) are created, objects for removed ones are kept off the scene for an undo, and the overlays and indices are refreshed. A move and a side change need no special path: each is an edit like any other.
- **Drag preview.** A drag places followers from the layout with the dragged point (or group point, or radius) overridden; only the commit writes the `.MSN`.
- **The generation** stays inside `MissionDocument`, generated with the open's inputs (the launch's flags, every draw at its lowest), for the Problems list, Save's check and Export only. Nothing displayed reads it.
- **Conditional records.** Every view opens, after the name, with its Present when, an emphasized sentence and its controls ([Present when](handoff-editor-conditions.md#present-when)), and a record drawn at random with a line saying so ("Its type is picked at random each load"). The outliner marks a conditional row "(not always present)"; the scene draws it as any other (the editor's choice, offered for the user's call).

## Decisions on the rewrite's questions

All five are the user's choices.

### 1. A record whose details differ by condition

One GUID, several records: a base and overlays (rows 12 to 16) or replacements (rows 6 to 9) under other conditions. In retail, rows 6, 7, 9, 10 and 11 carry no condition on any record, and row 8's 47 conditioned records are all random-choice options (GUID `-1`), so this concerns rows 12 to 16: 726 of 1,683 herc records, 30 of 124 flyer, 574 of 1,949 structure, 5 of 637 order and 31 of 1,247 group records carry a condition (`HercWorks.Query values --field ConditionRef`), most of the roster ones being options. Of the GUIDs two or more records carry, 7 of 93 herc, none of 3 flyer, 562 of 564 structure, 4 of 4 order and 11 of 124 group GUIDs have records of different conditions (`HercWorks.Query repeats`).

**Decided:** the scene and the view show the thing's **base**: the records carrying the same condition as its first record (the one that creates it), merged in file order. Each record under any other condition is listed in the view as a variation ("When campaign variable 30 is more than 0: type APOCA, weapons ..."), its fields editable there. An edit to a base field writes the last base record that sets it, else the first record; an edit inside a variation writes that record. What the user sees is what the mission is when none of the variations apply, and each variation is stated once.

### 2. A record that draws from a random choice

A herc, flyer or structure with a variant key takes its type, fit, point and heading from one option of a table, picked at each load (48% of herc, 30% of flyer and 0.4% of structure records).

**Decided:** the scene shows the **first option** in row-1 order, where the record then stands, as a mission opens today, the same every time. The view says it is picked at random from N options and lists them with what tells them apart (UX plan phase 2). No control changes which option the scene shows.

### 3. A plain `script.dat`

`DATA\script.dat` and a save slot's `script%d.dat` have no `.MSN`.

**Decided: dropped** ([step 3a](#steps)). Keeping it would mean keeping the whole `script.dat`-based scene, index and read-only path beside the new one, for files nobody can edit. The editor starts with no mission and the VOL list up, and `Herculan.Engine.Host.Editor`'s command line names a mission, as the list does ([Opening a mission](handoff-editor-viewing.md#opening-a-mission)). `Herculan.Engine.Host` still plays any `script.dat`, an exported one included.

### 4. Positions

Where the game puts things: a roster record's own point, else its group's point spread by formation slot; a group's own point, else its first order's route start; the paints-ground snap; a keyed record taking the option's point and heading; the first group in file order claiming a record; group 0 spreading the squad.

**Decided: the code is shared with the game, not with the converter.** The placement arithmetic is the helper above, called by the game's load with `script.dat` data and by the editor with the layout's. The layout reproduces the converter only where display needs it: the per-row merge (through the shared span table) and the option a keyed record copies from. Condition gating, compaction and renumbering, the rest of `MissionGenerator`, are not copied. With conditions untested, two groups of different conditions can name one record; the first in file order places it, as the game does whenever both are present, and the view lists the other.

### 5. The squad in the scene

Today the squad's objects come from the handoff's `player.mec`: group 0's hercs in practice and `INSTANT ACTION`, the career's squad in a campaign.

**Decided:** show group 0's member hercs from the `.MSN` in every mission, one object per member, spread as the squad spawns, the squadmate a practice load leaves behind and a herc the Herc Type option replaces included, with the squad view's notes for those and the existing note in a campaign mission that the career's squad replaces them. `player.mec` is still built for Export and the game's handoff.

## Steps

Each step builds at 0 warnings, passes `dotnet test`, leaves the editor usable, and is one commit after the user's in-app check.

1. **Remove the preview** (done). Reasons that refuse a record the generation dropped say it is "not shown in the editor", until step 5 makes them unreachable.
2. **Core layout and shared rules** (done). `MissionLayout` with presence and suppliers by the answers to questions 1 and 2; the merge span table shared with `MissionGenerator`; the placement helper out of `MissionLoader`; `HercWorks.Query repeats` extended to rows 10 to 16 with conditions. No editor change. Tests only where a named edit would break them (a layout that merges a variation into the base; a keyed herc placed at its own point instead of its option's).

3a. **Drop the plain `script.dat`** (done), per [question 3](#3-a-plain-scriptdat): `EditorMission.Source` is never null, and the editor has no read-only mode.

3. **Selections and links by GUID** (done). Every selection, link, picker and editor entry point takes GUIDs; the views still read the generated `Mission`, converting at their edge, and say plainly when the generation has no such record. `MissionRebuild`'s reselection goes.
4. **Edits choose their record from the layout** (done). `MissionDocument`'s supplier lookups (`SlotOf`, `ListRecord`, `AreaRecord`, `SuppliedByVariant`, `AlternativeOf`, `SetHeaderWord`'s provenance) read the layout, so an edit works on any record; the `.ENG` line supplier likewise. `handoff-editor-document.md`'s [Which record an edit writes](handoff-editor-document.md#which-record-an-edit-writes) is rewritten to match.
5. **The scene from the `.MSN`.** World-only load, objects from the layout placed through `MissionPlacementRules` (`LayoutPresence.Group` counts group 0 as a claimant where `ClaimMembers` skips it; the two agree once the squad is placed apart, per question 5), the new `MissionIndex`, `SceneSync` in place of `MissionRebuild`, picker, both overlays, followers, mover and leveller on the new index, the outliner listing every record with conditional rows marked, the Problems list mapped through provenance. Three commits:
   - **5a. Objects and groups** (done). The world-only load; objects from the layout, the squad by question 5; object identity and `ObjectSelection` by `(row, GUID)`; the new `MissionIndex` for objects and groups; `SceneSync`; the picker, `GroupOverlay`, the leveller, and the mover's object and group targets with a drag previewed through an override. The Properties views keep reading the generation through the old index, renamed `GeneratedIndex`, each object's `Placement` carrying the generation's indices for its GUID.
   - **5b. Routes and areas** (done). `LayoutFeatures` places every route, area and action from the layout into the plan, a drag's overrides included (a point, a waypoint or an area corner alone, a circle's radius); `MissionIndex` serves them by GUID with the mission box; `MissionOverlay`, the waypoint and area picks, the framing and the move handles read them, so a route or area the mission as opened leaves out is drawn, picked and moved, while its view still says it is not shown. The waypoint and area targets are by GUID (`FeatureMoves`), and `PointMover` writes no `script.dat`. The route and area views take their waypoints and corners from the new index, so they follow a drag's preview.
   - **5c. Outliner and Problems** (done). The outliner lists the layout and the new index, every record of rows 8 to 11, 16 and 17 included, numbered by its place in its row, conditional rows marked, each row selecting by GUID or row-17 index; the objective lines are the text package's from the `.ENG`. The Problems list maps each finding to its `.MSN` record through provenance ([Mission check](handoff-editor-viewing.md#mission-check)).
6. **Views on the layout, in their new design.** Each Properties view is rewritten once, reading the layout and the new index and laid out as [handoff-editor-ux-plan.md](handoff-editor-ux-plan.md)'s phases 2 to 6 describe, one view (or one phase's views) per commit, each judged against the [designer tasks](handoff-editor-ux-plan.md#designer-tasks). With the conditional line in every view; `Placement` no longer read for editor data; `ExportedIndex`, `ExportedSlot`, `GuidOf` and `Generator.Conditions`/`VariantPicks` gone from the editor once the last view moves.
7. **Docs.** Throughout, each step updates the handoffs it changes; this last pass rewrites [handoff-editor-document.md](handoff-editor-document.md)'s provenance, regeneration and rebuild sections, updates [handoff-editor-viewing.md](handoff-editor-viewing.md) and [handoff-editor.md](handoff-editor.md), and drains this handoff into them.

Class sizes stay under about 1,500 lines.

## Open

- **Unported:** a waiting group that arrives by drop pod or on foot is not in the scene, as the user decided ([Decided by the user](#decided-by-the-user)); it still stands on its point, often the squad's start (`C1_05`'s groups 42 to 44).
- **Open:** whether a Cybrid's weapon fit becomes editable, and with what offered per mount (no shell file covers a Cybrid chassis). Until then its view shows the fit twice: `MachineFitView` draws a dropdown row per mount (the weapon dropdowns empty, the guidance ones working) and then the read-only list. One of the two goes once this is decided.
- **Open:** whether every retail variant table has an option holding a draw of 0 ([Not checked](handoff-editor-document.md#not-checked)); with question 2's first option it no longer matters for display, only for the generation behind Save and Export.
