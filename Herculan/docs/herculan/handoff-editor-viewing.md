# Handoff — opening and viewing a mission in the editor

Opening a mission, New mission, the theater and time of day, and how the editor shows and selects a mission's features. Part of the [editor handoff](handoff-editor.md).

## Opening a mission

The editor opens a mission only from its `.MSN`, never a `script.dat`; `Herculan.Engine.Host` plays one, an exported one included ([What a saved `script.dat` reaches](handoff-editor-document.md#what-a-saved-scriptdat-reaches)). It starts with no mission and File > Load from VOL's list up, from the game's Editor menu item too. `Herculan.Engine.Host.Editor`'s second argument, after the install, names a mission as the list does (`C1_05` or `C1_05.MSN`, in any case, a saved or new one included); the list then says it is loading it and opens it as a pick from the list does, and a name no mission has leaves the list up with "No mission is named <name>." under it (`VolMissionBrowser.Find`).

File > Load from VOL lists every `.MSN` in the archives and every loose one in the install's `MSN\` folder, marking a loose one "(saved)" when it overrides an archive mission and "(new)" when no archive holds it. It opens one through `MissionOpener.Open` and `MissionFileLaunch.Open` (the simulator host's `.MSN` path), which writes the handoff into `%TEMP%\herculan-editor\`, so the install's `DATA\` is never written, and holds the mission as a `MissionDocument` whose generation draws what the handoff's load drew ([Conditions and variants](handoff-editor-document.md#conditions-and-variants)). The document's generated `script.dat` and `mission.str` are written over the handoff's and the scene is loaded from them; `Mission.SourcePath` is that scratch file.

## New mission

File > New mission (`NewMissionDialog`) asks for:

- a name, taken as Save As takes one, but never an archive mission's (the game would play it as that mission in its career position), asking first when the install has a saved mission of that name. A refused name shows its reason in strong red and tints the field;
- a zone, from every `dat\ZONE*.DAT` with its `.DBA` in DBSIM's mount (`MissionZones`);
- a theater, named by the terrain bank its day descriptor loads (`TheaterNames`: `urban`, `bsnow`, `volcan`, `ice`, `moon`, [`../retail/formats/wld-world.md`](../retail/formats/wld-world.md)), since no retail text names one;
- day or night.

`NewMission` (Core, `Data/File/Msn/NewMission.cs`) builds the `.MSN` and `.ENG`: the player's machine alone on a route of two points, with one mandatory objective met when the player reaches the second, every record copied from `TRAIN1.MSN` but the text package, which takes `TRAIN5.MSN`'s shape; its comments cite each record. `MissionOpener.OpenNew` and `MissionFileLaunch.OpenNew` open it from memory as `INSTANT ACTION`, into the same scratch handoff. The title carries `*` until File > Save writes it (`EditHistory.MarkNeverSaved`); the browser then lists it "(new)". The mission check finds nothing in it.

## Theater and time of day

The outliner's first entry, Mission, shows `MissionView`: the name and zone, a Theater choice named as the New mission dialog names them, Day or Night, then the text ([The mission's text](handoff-editor-mission-logic.md#the-missions-text)).

What retail plays ([`../retail/formats/script-dat.md`](../retail/formats/script-dat.md#header-format)): the theater (header offset 0) picks `wld\world<theater * 2 + variant>`, the terrain texture bank, palette, sky and fog; the variant (offset 18) is day or night. Every retail load writes over the theater, a campaign's from its stage and a practice or `INSTANT ACTION` one as 1, and a practice or `INSTANT ACTION` load writes the practice screen's time of day over the variant; only a campaign mission plays its `.MSN`'s own day or night.

The editor shows a campaign mission as the game plays it, on its stage's theater (shown disabled, with a note) by its own day or night, and every other mission on its own theater and time of day, which the generation leaves to row 2. The planned menu for playing user-made missions is to honour them: a departure from `INSTANT ACTION` the user chose. The game's own launches still write theater 1 and the practice screen's time of day.

A change goes through `TheaterChanger` ([The tools](handoff-editor-document.md#the-tools)), and then dresses the scene in the new world (`MissionScene.ChangeTheater`): a model library in the new palette and shade ramp, every object's model and LOD chain built again (those of objects an undo can put back included), the terrain mesh lit again in the new bank, and the sky, fog, ground-shape set and beam colours. Heights, materials, objects, the selection, the camera and the view settings stay; `TerrainLeveller` keeps building rows against the scene's models as they stand. The host then frees every upload of the old world and uploads the scene again (`EditorHost.ReloadWorld`). A change costs about what an open costs, without loading the zone. A HERC's shadow already on the ground keeps its radius; every retail descriptor names the same ground-shape set (`FLAT2`). `Mission.Header` keeps the theater the mission was opened or last rebuilt with; the view and the scene read the generated header.

## Selection and panels

`EditorSelection` is an object, group, trigger area, action, timer, route (with an optional waypoint), objective or condition, held in one `SelectionState` set by the viewport click, the `MissionOutliner` (left: groups with members, areas, actions, timers, routes, objectives under the block-13 objective lines, conditions, and the Problems list's findings) and the `PropertiesPanel` (right, one view per kind, every mentioned record a link).

Every kind but an object names its `.MSN` record: a group, trigger area, action, timer, route or condition by the GUID of its row (16, 9, 10, 11, 8, 1), an objective by its index in row 17; an object is its `SceneObject`. The editors take the same identities, and each selects the GUID or row-17 index it has just written. A selection is kept across every edit, undo and redo while the `.MSN` has its record ([Rebuilding after an edit](handoff-editor-document.md#rebuilding-after-an-edit)).

The lists, views, overlays and picks still read the generated `script.dat`, so `GeneratedRecords` gives the block index a selection's record was exported at, and the selection a list, link, finding or pick makes from a block index (the GUID of the record that created it). A record the generation has none for, one whose condition fails in the version the editor shows, is selected only through a condition's Used by or by keeping a selection across an edit; its view shows its kind and "Not shown in the editor.", and its editor refuses an edit or a delete ("It is not shown in the editor.").

## Camera

`EditorCamera` flies free (right mouse to look, WASD/arrows, Q/E, Shift). A double-click in the viewport or the outliner selects and frames the selection (`SelectionBounds`, `EditorCamera.Frame`), keeping the yaw and looking down on it. A double-click on a link in the Properties panel selects without framing, and a timer is never framed.

V, or View > Top-down, looks down from above, north up, high enough over the middle of the zone that its whole grid fits the window; looking turns about the vertical alone, W is up the screen, fog is off and the clip planes are pushed out with the height. V again returns to the pose it left; opening another mission leaves it. It is the perspective camera at its pitch limit, 88 degrees down, not an orthographic map: things stand up toward the edges and a point's place on screen shifts with its height. It frames the grid as it opens, not after a resize.

How a structure's ground plane (shadow, floor plates) draws from that height is not looked at: it is ranked against the terrain by the cell `HeightGrid.PickDrawCell` files it under in the visible region, which `TerrainVisibleRegion.Update` keeps from the last view whenever the frustum, cut off at the zone's visibility range, reaches no ground.

## Picking

A press on a move handle of the selection, a selected trigger area's corners and radius included, is taken in screen space first (`MoveGizmo`). Otherwise `ScenePicker` tries a waypoint in screen space, then an object (skipping waiting groups when hidden): the bounding spheres the ray meets give the candidates, and the nearest triangle wins, a structure's ground plane counting only when the ray meets nothing solid, so a Herc on a base's pad is picked rather than the base; then `GroundPick` (a ray march against `HeightAtWorld`) and the smallest trigger area containing that point.

## Overlays

`MissionOverlay` draws through `DrapedLines`, each toggled from the View menu, a line behind terrain dimmed:

- every trigger area of block 4, coloured by who trips it, grey when no action tests it;
- every route of block 3 in three colours (the player's, walked, never walked), loops closed, waypoints numbered when selected;
- every base pad's painted tile (`MissionBasePad.TileOrigin`), with the selected group's authored point and its snap;
- the mission box with its HDD-map and abort margins.

A selected area's ground is tinted yellow over exactly what its test catches, a circle's octagon included, through `SceneRenderer.Highlight`, which only the editor's program compiles. Only a selected area is tinted; a selected action's areas are outlined, since `Highlight` holds one region. The tint covers the terrain, not a structure's ground plane inside the area.

## Cross-references

`MissionIndex` works out once which actions test each area, which groups walk or merely name each route, and for each action the timers, engaged and defeated objects that fire it and the waiting groups, ended orders, started timers, counters and message (from `COMMAND<n>.STR`) that follow. The action view shows all of it, and an area's view shows it read-only for every action testing the area (`ActionView.DrawSummary`).

## Groups

`Mission.GroupSetups` carries each block-11 record's point (with its pre-snap authored point and where it came from), heading (and whether it is the route bearing), formation id and paints-ground flag. Selecting a group boxes its members and highlights the route it walks; `GroupOverlay` draws a post at its point, a heading arrow, and a line to each order's subject labelled with slot and verb. Its view shows its members, Present when, deployment action, orders, setup, out-of-action counter writes and related objectives; an object's view its Present when, counter writes and objectives.

Not drawn:

- ghosts of the formation slots a group leaves empty, which need the formation tables only `MissionLoader` holds;
- an order's own point (`+0x04`, set in 47 orders), which only the briefing map reads, off the squad's first order ([`../retail/simulation/ai-goals.md`](../retail/simulation/ai-goals.md#the-order-record)): a briefing-only marker, or nothing.

## Waiting groups

View > Waiting Groups draws, outlines (meshes hidden, members boxed, still selectable) or hides the members of a group gated on an action. Unless hidden, each is labelled at its point with the action and the arrival (drop pod, on foot, in place), labels sharing a point stacked. Translucent ghosts in place of outlines need a tint or alpha path in `SceneRenderer`.

## Mission check

`ScriptDatLint` (Core) holds `MissionScriptForm`'s ref-range checks plus: an action area ref behind its first `-1`; an action of type 7 to 10 with no target; an action with a message that writes no counter, which DBSIM never posts; an objective condition or subject kind with no case; a group with neither a heading nor a slot-0 order; a herc group with no slot-0 order; rosters past DBSIM's slot caps; and, given the line count, a `mission.str` past the 50 lines DBSIM reads. Both apps run it. The editor lists the findings under Problems, each selecting its record; it runs after every rebuild, not after a move or a side change. `HercWorks.Query lint` runs it over every retail mission and finds one thing: `C1_02`'s action 1, `MAYDAY!`, which carries a message and writes no counter ([`../retail/simulation/cockpit-messages.md`](../retail/simulation/cockpit-messages.md#its-speakerless-set)).

## Not shown

- **Drop-pod and on-foot arrival points**, picked at run time from the player's position and heading when the action fires.
- **Detection and engagement ranges** (50,000 and 100,000 units), simulation constants rather than mission data.
- An **action graph**: the action view walks the wiring one link at a time, where a node graph of actions, timers and the objects and groups between them would show it at once.
