# Handoff — mission editing in the HERCULAN Editor

Feasibility and scope for three features in the editor, `Herculan.Engine.Editor` (run on its own by `Herculan.Engine.Host.Editor`, and from the game's Editor menu item):

1. Showing and selecting the mission's non-object features — trigger areas, routes, groups, objectives and the action wiring between them.
2. Moving a herc to a new starting position.
3. Saving the edited mission back to `script.dat`.

## Headline

None of this is a from-scratch build. `MissionLoader` resolves almost every mission feature the editor could show into `Mission`, and the editor already shows and selects trigger areas, routes, the mission box, groups, objectives, timers and the action wiring ([In place](#in-place)); what is left of the first feature is the same kind of drawing and UI work. The `script.dat` writer exists and is complete: `ScriptDatTransformer.Write` covers all 13 blocks, and `HercWorks.UI.MissionScriptForm` already drives it through an open/edit/Save As UI. The editor keeps the parsed document (`EditorMission.Document`) beside the scene loaded from it, moves roster objects, route waypoints and groups in it, and adds structures to it ([In place](#in-place), "Moving objects, waypoints and groups" and "Adding structures"); what is left is moving area corners through the same machinery, adding the other kinds of object, and a Save.

Whether DBSIM can read a grown file is settled (it can — [gap 2](#2-three-consequences-of-appending)).

## What already exists

- `Mission` carries `Actions` (each `MissionAction` with its resolved `MissionTriggerArea` list), `ActionTimers`, `GroupOrders` (each order's `Route` resolved to points), `PlayerRoute`, `Objectives`, `GroupDeploymentActions`, `GroupKinds`, `GroupSides`, `GroupOutOfActionReports`, `Coordinates` and `Text`. Each `MissionPlacement` carries its `GroupIndex`, `SlotIndex`, `EngagementActionRef`, `DefeatActionRef`, `StartingCondition` and `OutOfActionReport`.
- `SceneItem.Visible`, for hiding waiting groups; the editor's own `GroundPick`.
- `MissionLoader.AddRoster` — `Coordinate(script, record.PositionRef) ?? OffsetFromGroup(...)`. The engine honours a roster record's own position override, matching `Mech_AttachToGroup` (`00417aa8`), and the editor's object moves write it.
- `MissionPlacement.Kind` + `SlotIndex` address a block-7/8/9 record exactly, and `GroupIndex` addresses block 11, so the editor's selection already identifies the record to edit.
- `HercWorks.UI.MissionScriptForm.OnSaveAs` — a working save path, with VOL entry-prefix preservation and an advisory cross-block ref-range validator.
- `MissionGenerator` (Core) builds a `script.dat` from an `.MSN` as VSHELL does, and `MissionFileTransformer` round-trips `.MSN` files.
- `HercWorks.Query actions|orders|objectives` — the corpus cross-check for anything the editor shows.
- `MissionLoader.Load(content, script, path)` loads from a `ScriptDat` already parsed, which `EditorMission.Open` reads once and keeps as `Document`; the mission check runs over that document.
- Round-trip coverage: `TrainingLaunchTests.LoadsEveryRetailMission` writes a `script.dat` from each of the 62 `.MSN` files and checks `ScriptDatTransformer` rewrites it byte-exact, and `ScriptDatRoundTripTests` rewrites the install's `DATA\script.dat` and every `sav\script%d.dat` as a prefix of the file read (they keep stale tails past block 13). Both skip silently without an install.

## What a saved `script.dat` reaches

VSHELL writes `data\script.dat` afresh from the `.MSN` every time it loads a campaign or practice mission, and so does this engine's own campaign launch (`ShellCampaignLaunch`, through `MissionGenerator`). An edited `script.dat` is therefore played by: the editor and `Herculan.Engine.Host` opening that file directly; DBSIM run directly over `data\`; and a save slot, since `Career_LoadSlot` (`00412bbf`) copies `sav\script%d.dat` into `data\` and the replay flies it as-is ([`../retail/formats/save-games.md`](../retail/formats/save-games.md#the-slot-handoff)). It does not change a campaign mission. That needs `.MSN` editing — GUID-keyed rows, conditions and variants, the `.ENG` text — a separate and larger project this plan does not cover.

## Mission features to show and select

Counts below are over the 62 retail `.MSN` files with campaign conditions not applied (`HercWorks.Query`).

### In place

- **Opening a mission.** The editor starts on `DATA\script.dat` or the file named on its command line, and with no mission and the VOL list up when there is none. File > Load from VOL lists every `.MSN` in the archives and opens one through `MissionFileLaunch` (the simulator host's `.MSN` path), generating the handoff into `%TEMP%\herculan-editor\` so the install's `DATA\` is never written; `EditorMission` and `MissionDrawables` are rebuilt for it. Such a mission's `Mission.SourcePath` is that scratch file, so a future Save must offer Save As for it rather than writing in place.
- **Selection and panels.** `EditorSelection` is an object, group, trigger area, action, timer, route (with an optional waypoint) or objective, held in one `SelectionState` that the viewport click, the `MissionOutliner` (left: groups with members, areas, actions, timers, routes, objectives under the block-13 briefing list) and the `PropertiesPanel` (right, one view per kind, every mentioned record a link) all set.
- **Camera.** `EditorCamera` flies free (right mouse to look, WASD/arrows, Q/E, Shift). A double-click in the viewport or the outliner selects and frames the selection (`SelectionBounds`, `EditorCamera.Frame`), keeping the yaw and looking down on it. V, or View > Top-down, switches to a view looking down from above, north up, high enough over the middle of the zone that its whole grid fits the window; there looking turns about the vertical alone, W is up the screen, fog is off and the clip planes are pushed out with the camera's height. V again returns to the pose it left; opening another mission leaves it.
- **Picking.** `ScenePicker` tries a waypoint in screen space, then an object's bounding sphere (skipping waiting groups when they are hidden), then `GroundPick` (a ray march against `HeightAtWorld`) and the smallest trigger area containing that ground point.
- **Overlays.** `MissionOverlay` draws, through `DrapedLines`, every trigger area (coloured by who trips it), every route (the player's, the walked ones and the never-walked ones in three colours, loops closed, waypoints numbered when selected), every base pad's painted tile (`MissionBasePad.TileOrigin`, with the selected group's authored point and its snap drawn too) and the mission box with its HDD-map and abort margins, each toggled from the View menu. A line hidden behind terrain shows dimmed. A selected area's ground is tinted yellow over exactly what its test catches — a circle area's octagon included — through `SceneRenderer.Highlight`, compiled only into the editor's program as the grid is.
- **Wiring.** `MissionIndex` cross-references, once, which actions test each area, which groups walk or merely name each route, and for each action the timers, engaged and defeated objects that fire it and the waiting groups, ended orders, started timers, counters and message (from `COMMAND<n>.STR`) that follow. The action view shows all of it, and an area's view shows it for every action testing that area.
- **Groups.** `Mission.GroupSetups` carries each block-11 record's point (with its pre-snap authored point and where it came from), heading (and whether it is the route bearing), formation id and paints-ground flag. Selecting a group boxes its members, highlights the route it walks, and `GroupOverlay` draws a post at its point, a heading arrow, and a line to each order's subject labelled with the slot and verb. Its view adds the setup, the out-of-action counter writes and the objectives asked of it to the members, deployment action and ten orders; an object's view adds its own counter writes and objectives.
- **Waiting groups.** View > Waiting Groups draws, outlines (meshes hidden, members boxed, still selectable) or hides the members of a group gated on an action. Unless hidden, each is labelled at its point with the action and the arrival (drop pod, on foot, in place), labels sharing a point stacked.
- **Mission check.** `ScriptDatLint` (Core) holds `MissionScriptForm`'s ref-range checks plus an action area ref behind its first `-1`, an objective condition or subject kind with no case, a group with neither a heading nor a slot-0 order, a herc group with no slot-0 order, and rosters past DBSIM's slot caps. Both apps run it; the editor lists the findings under Problems, each selecting its record, and `HercWorks.Query lint` runs it over every retail mission's generated `script.dat`, finding nothing.
- **Moving objects, waypoints and groups.** Selecting a roster object (not one of the player's squad, which has no roster record) shows typed X and Y fields in its view, and Z for a flyer, the one height the spawn pass reads from the point ([`../retail/formats/script-dat.md`](../retail/formats/script-dat.md#placement--the-actual-rule), rule 5); a field applies when it loses the focus. In the viewport `MoveGizmo` draws X and Y arrows, a square in the ground plane between them and, for a flyer or a flyer group, a Z arrow, on the selected object, waypoint or group; dragging one previews the move and commits it on release. `PointMover` makes each edit. An object is given a point of its own: `ScriptPointRefs.Place` (Core) rewrites its point in place when nothing else names it (`ScriptPointRefs.Count` covers every block-1 ref) and appends one otherwise, and the sim object stands where the simulator would stand it. A waypoint moves the point it names, so whatever shares that point moves with it ([gap 1](#1-positions-are-indirect)); Alt+drag detaches it instead, through `ScriptPointRefs.Place` as for an object. Either way its point is written with Z 0, and a closed route's first and last waypoints move together. A group moves the same way through the point it stands on, its block-11 record's own or else its route's first waypoint (rules 2 and 3), so the route's start and whatever shares the point move with it; Alt+drag gives the record a point of its own. Its handles sit on its authored point, which a paints-ground structure group's snap then puts in its tile, so its pad jumps a tile at a time; its members with no point of their own follow, the player's squad with group 0. Only a flyer group's Z is written from the drag, and any other keeps its point's Z; the group view says what else names the point. After every change to block 1, `PointFollowers` places again from the document whatever stands on a moved point ([gap 4](#4-live-scene-refresh)), `MissionDrawables.Move` and `ScenePicker.Move` following each object it moves, and the route view says how many other records name the selected waypoint's point. The mission box overlay is rebuilt from block 1. After every move, a drag's preview included, `TerrainLeveller` puts the heightmap and the cell materials back as the zone loaded them and runs the spawn pass's pad painting and footprint flattening again over every structure and base pad where they now stand, so the ground and a pad's paint follow as they move and return to the zone as loaded where they leave; structures and walking mechs are settled on the new ground, the changed rows of the terrain mesh are rebuilt and uploaded, and the overlay lines are laid again. Edit > Undo and Redo (Ctrl+Z, Ctrl+Y) step through the edits. The view says when the object is outside the mission box as opened, and when it is a group follower that holds formation on its leader once the mission runs.
- **Adding structures.** The Toolbox window (`StructureToolbox`) lists the 45 structure types of `BASES.DAT` under the names the MFD status screen prints for them, with a Human or Cybrid choice for the side. An entry dragged out and let go over the view adds that structure on the ground under the pointer; a double-click, or Add with an entry chosen, adds it on the ground at the middle of the view, or under the camera when that is sky, which the gamepad's pointer reaches as well. Letting go over a panel adds nothing. `StructurePlacer` appends a block-9 record naming no point, heading or action of its own, pristine and writing no counter, and a block-11 group of its own with it in slot 0, standing on a block-1 point appended at the ground's height and facing a block-2 heading of 0 degrees (one already in the block is shared), in formation 0, not painting its ground, with no orders, no deployment action and no counter writes. That is the shape of 148 of the retail missions' 537 structure groups (`HercWorks.Query orders`), and the mission check passes it, since a group naming a heading needs no slot-0 order. An add that would take block 9 past DBSIM's 140 structure slots is refused, and the Toolbox says why. The object is built by `MissionScene.CreateObject`, the scene load's own path, its model built and uploaded then when nothing else in the mission draws with it; it joins `MissionIndex` with its group (`Mission` stays as loaded), the picker and the drawing, the ground is levelled under it as after a move, and it is selected. The add is one step of the undo history: Undo takes the records, the group, the object and its drawing out again, and Redo puts the same ones back. An added structure and its group move as any other.
- **Changing sides.** An object's view and a group's view offer Human or Cybrid for the group's side (block 11 `0x6e`), every group but the player's squad; the side is the group's, so all its members change, and the view says how many when there are others. `SideChanger` writes the field, the index's copy and each member's simulation object, as one step of the undo history.
- **Objectives and timers.** An objective's view gives mandatory or failure, the subject (boxed in the scene), the condition, for condition 0 the route (highlighted) and the subject group's first order slot on it, the `mission.str` failure text and the counter writes. A timer's view gives the delay, the action that starts it and the actions it fires, and the action view links to it.

### Gaps in what is in place

- **Waypoint height.** A moved waypoint's point is written with Z 0, as 1487 of the 1488 retail waypoints have it. DBSIM reads a waypoint's Z as the starting altitude of a flight standing on its route's first waypoint, and the heads-down map projects the player's route icons with it ([`../retail/simulation/ai-goals.md`](../retail/simulation/ai-goals.md#the-route-cursor-is-loaded-once)); everything else works in the ground plane. A waypoint has no field or handle for it; only a flyer group standing on its route's first waypoint sets it, through the group's Z handle. This engine's heads-down map places route icons by X and Y alone.
- **Waypoint and group views.** The route view lists its waypoints' coordinates and the group view its point read-only, with no typed fields as an object's view has, and neither warns when a waypoint or a group is moved outside the mission box as opened, though that grows the box and the heads-down map's frame as an object does (the box overlay shows it).
- **Area tint.** Only a selected area is tinted. A selected action's areas are outlined, not tinted: `SceneRenderer.Highlight` holds one region. The tint is painted on the terrain alone, not on a structure's ground plane (its shadow and floor plates) inside the area.
- **Adding.** Only structures can be added. A herc, a flyer or a ground vehicle (which drives, and whose group needs orders the Toolbox has no way to set) cannot, and nothing is added to an existing group. Nothing but a tooltip follows the pointer while an entry is dragged. An added structure's type is fixed once it is added, and nothing is deleted except by Undo.
- **Framing.** A double-click on a link in the Properties panel selects without framing. A timer stands nowhere and is not framed.
- **Top-down view.** It is the perspective camera at its pitch limit, 88 degrees down rather than 90, not an orthographic map: things stand up from the ground toward the edges of the view, and a point's place on screen shifts with its height. It frames the zone's grid as it opens, not whatever window size it is later resized to.
- **Ground planes from high up.** A structure's ground plane (shadow, floor plates) is ranked against the terrain by the cell `HeightGrid.PickDrawCell` files it under in the visible region, which `TerrainVisibleRegion.Update` keeps from the last view whenever the view's frustum, cut off at the zone's visibility range, reaches no ground, as it may from the top-down view's height. How the planes draw there has not been looked at.

Counts for the retail missions: 58 of 62 use trigger areas, 257 distinct areas named 143 times as boxes and 132 as circles, 16 shared by more than one action. Subjects are the player in 142 of 338 actions and the player's group in 170. `C2_01`'s boundary strips and `TRAIN8.MSN`'s three defeat-chained reinforcement waves ([`../retail/simulation/mission-deployment.md`](../retail/simulation/mission-deployment.md#train8msn-end-to-end)) are good test cases.

### Groups in the scene (blocks 10, 11)

Two things the group display still lacks. Ghosts of the formation slots a group leaves empty need the formation tables, which only `MissionLoader` holds. An order's own point (`+0x04`, set in 47 orders) is read only by the briefing map, and only off the squad's first order ([`../retail/simulation/ai-goals.md`](../retail/simulation/ai-goals.md#the-order-record)); mark it as a distinct briefing-only marker or leave it out.

Size: small each.

### Waiting groups as true ghosts

Outlining stands in for ghosting. Drawing the meshes translucent instead needs a tint or alpha path in `SceneRenderer`.

Size: small–medium.

### Action graph

The action view walks the wiring one link at a time; a node graph of the mission's actions, timers and the objects and groups between them would show it at once.

Size: medium–large.

### Left out

- **Drop-pod and on-foot arrival points.** Picked at run time relative to the player's position and heading when the action fires; nothing static to draw.
- **Detection and engagement ranges** (50,000 and 100,000 units). Simulation constants, not mission data; a per-object debug ring at most.
- **Variants and conditions.** `script.dat` is already resolved; these exist only in the `.MSN`.

## Gaps on the write path

### 1. Positions are indirect

An object does not hold a position; it holds a `PositionRef` index into block 1. Most retail roster records carry `-1` there, so the object inherits its group's point plus a formation offset ([`../retail/formats/script-dat.md`](../retail/formats/script-dat.md#placement--the-actual-rule), rule 2, gives the ones that name their own), and moving one gives it a point of its own. `ScriptPointRefs.Place` appends one, which shifts nothing since every other block names block 1 by array position, or rewrites it in place once the record is its only user. It does not look for an identical existing point to reuse: file length is not a constraint.

Points are shared on purpose. In 45 of the 62 retail missions a route waypoint is the same block-1 point as a trigger area's center or corner (94 of the 886 distinct waypoint points, from `HercWorks.Query routes` and `actions`): that is how a mission fires an action when a waypoint is reached. `C1_01`'s player route runs through points 119, 121, 122, 123 and 124, its drop-pod triggers are circles about 121 to 124, and the groups those triggers bring in deploy on 119. So a waypoint drag moves the shared point, and the trigger and the groups move with it; giving the waypoint a point of its own, which Alt+drag does, leaves them where they were. A group drag (block 11) makes the same choice the same way, and moving an area corner (block 4) directly will meet it too.

### 2. Three consequences of appending

- **Block 1's extent frames the heads-down map** (see [`../retail/simulation/heads-down-display.md`](../retail/simulation/heads-down-display.md#the-maps-frame-of-reference), and `MissionBox`), and the mission box. A point outside the current box silently rescales the player's in-game map and moves the boundary warning and abort lines. The object view warns on it, and the mission box overlay is rebuilt after each edit.
- **File length is not a constraint.** `DBSim_LoadScriptDat` (`00424308`) reads through a `FileRStream` block by block and allocates each of blocks 1-6 and the group array from its own count, so a file longer than the retail 13,520 bytes reads correctly. Its fixed arrays are the per-roster slot flags and slot maps ([`../retail/formats/script-dat.md`](../retail/formats/script-dat.md#the-two-pass-read--and-what-it-means-for-dbsim-keeps)); past them it writes over neighbouring globals. Appending block-1 points is unaffected; adding roster records is not: the mission check reports a roster past its cap, and the editor refuses to add a structure past block 9's. Appending does give meaning to a ref past block 1's old end, which then names the appended point; the mission check reports such refs, and no retail mission has one, but the editor does not re-run the check after an edit ([gap 4](#4-live-scene-refresh)). VSHELL's own reader, `ShellMap_LoadScriptDat` (`004243d7`), has not been checked for caps, and it reads a save slot's file for the briefing map.
- **A mech's or flyer's own point has never been observed live.** No retail mech or flyer record names one ([`../retail/formats/script-dat.md`](../retail/formats/script-dat.md#placement--the-actual-rule), rule 2), so `Mech_AttachToGroup` and `Flyer_AttachToGroup` honouring it is RE-derived and engine-implemented, and the editor's moves of either rely on it. Structures' is exercised by retail data. Confirming it means writing such a file and running retail DBSIM.

### 3. Moved is not the same as stays there

- `MissionPlacement` carries `FormationOffset` even when the record supplies its own position, and AI followers hold formation on their leader every tick. For a non-leader group member the edit sets a spawn position the AI may walk out of; the object view says so.
- A group with no point of its own stands on its route's first waypoint, so moving that waypoint, as a plain group drag does, moves the start of the route too. Given a point of its own (Alt+drag), it spawns there instead, its route unchanged.
- A paints-ground base group snaps to a fixed spot in its tile, so a small move does nothing and a larger one jumps a whole tile ([`../retail/formats/script-dat.md`](../retail/formats/script-dat.md#the-anchor)); the base pad overlay shows the tile.
- A group waiting to deploy by drop pod or on foot is placed at a placeholder, so moving it changes nothing once the mission runs ([`../retail/simulation/mission-deployment.md`](../retail/simulation/mission-deployment.md#arrival--group_deploymentcheck-004236c4)); its view says so. One arriving in place, and a held-back structure, stand where they are placed: the structure, solid and undrawn, from the first frame ([`../retail/simulation/mission-deployment.md`](../retail/simulation/mission-deployment.md#held-back-structures-stand-from-the-start)).

### 4. Live scene refresh

An object move re-places that object's items and pick sphere. After every change to block 1, `PointFollowers` places again, by the loader's rules, whatever stands on a point that moved: route points, trigger areas, each group's point and heading (its own point, else its route's first waypoint, facing along the first leg when it names no heading) with the members spread from it, a paints-ground group's pad outline, and objects whose roster record names the point. The positions it moves are editor copies in `MissionIndex`; `Mission` and the mission check stay as loaded (a `MissionPlacement.Position`, the orders' routes and the actions' areas go stale, and nothing in the editor reads a position from them). The ground is levelled and the pads painted again where the structures and pads now stand (`TerrainLeveller`).

## Scope

| Slice | Size |
|---|---|
| Empty formation slots, briefing-only order point | small each |
| Waiting groups as translucent ghosts | small–medium |
| Action graph | medium–large |
| Save (in place, with a `.bak`) and Save As, the mission check run first | small |
| Write the edited mission as an `.MSN` — wanted; feasibility not yet looked at ([What a saved `script.dat` reaches](#what-a-saved-scriptdat-reaches)) | not sized |
| Area-corner moves, with their overlay lines rebuilt | small |
| Waypoint and group typed fields and mission-box warning | small |
| Mission check re-run after each edit | small |
| Tint every area of a selected action; tint ground planes too | small |
| Orthographic top-down view | small–medium |
| Heading entry and a turn handle | small |
| Add hercs, flyers and ground vehicles, each group with an order | small–medium |
| Add a structure to the selected structure group; delete an object | small each |
| A preview of the dragged structure under the pointer | small |

## Suggested sequencing

1. Area-corner moves through the same `ScriptPointRefs.Place`.
2. Save, which turns the edits into a file DBSIM can be run over.