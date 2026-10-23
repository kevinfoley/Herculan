# Handoff — positions, routes and trigger areas in the editor

Moving objects, waypoints, groups and area corners; creating and editing routes and trigger areas; and what a move does and does not change once the mission runs. Part of the [editor handoff](handoff-editor.md). Which records each tool writes is in [The tools](handoff-editor-document.md#the-tools).

## How positions are held

An object holds no position; it holds a `PositionRef` index into block 1. Most retail roster records carry `-1`, so the object takes its group's point plus a formation offset ([`../retail/formats/script-dat.md`](../retail/formats/script-dat.md#placement--the-actual-rule), rule 2, gives the ones that name their own). A group with no point of its own stands on its route's first waypoint (rules 2 and 3).

Points are shared on purpose. In 45 of the 62 retail missions a route waypoint is the same block-1 point as a trigger area's centre or corner (94 of the 886 distinct waypoint points, `HercWorks.Query routes` and `actions`): that is how a mission fires a trigger when a waypoint is reached. `C1_01`'s player route runs through points 119, 121, 122, 123 and 124, its drop-pod triggers are circles about 121 to 124, and the groups those triggers bring in deploy on 119.

So every drag offers two choices: a plain drag moves the point, and whatever shares it moves too; Alt+drag gives the dragged thing a point of its own (`MissionDocument.PlacePoint`), leaving the rest where it was.

## Moving

- **Fields.** An object's view outside the player's squad (which spreads from group 0 whatever point its records name) shows typed X and Y, and Z for a flyer, the one height the spawn pass reads from the point (rule 5); the route view shows them for the selected waypoint, the group view for the group's authored point (Z for a flyer group), the area view for each corner or centre (`PointFields`). A field moves its point as a plain drag does.
- **Handles.** `MoveGizmo` draws X and Y arrows, a square in the ground plane between them and, for a flyer or flyer group, a Z arrow, on the selected object, waypoint or group, and on each corner of a selected trigger area. A drag previews the move and commits it on release (`PointMover`).
- **An object** always gets a point of its own; the sim object stands where the simulator would stand it.
- **A waypoint** moves the point it names; Alt+drag detaches it, its route's list entry naming the new point. A list a variant supplies is the alternative's, which every route drawing it shares. Either way the point is written with Z 0, and a closed route's first and last waypoints move together.
- **A group** moves the point it stands on, its row-16 record's own or else its slot-0 route's first waypoint, as the layout has them, so the route's start moves with it; Alt+drag gives the record a point of its own, and it then spawns there with its route unchanged. Its handles sit on its authored point, which a paints-ground structure group's snap puts in its tile, so its pad jumps a tile at a time. Its members with no point of their own follow, the player's squad with group 0. Only a flyer group's Z is written; any other keeps its point's Z. The view says what else names the point.
- **Warnings.** The object, route, group and area views say when the point is outside the mission box as opened, testing X and Y alone, since the box is of block 1's points (a circle's edge past it grows nothing). The object view also says when the object is a group follower holding formation on its leader once the mission runs; the route view says how many other `.MSN` records name the selected waypoint's point.

A drag writes nothing until it lets go. While it previews, everything standing on what it moves is placed again from the layout by the game's rules with the drag's overrides read in place of the layout's values: a point's coordinates, a group alone on a point of its own, an object alone, a waypoint or an area corner alone on a point of its own, or a circle's radius as it would be stored (`PlacementOverrides`, `SceneSync.Preview`). Each group's point and heading (facing along the first leg when it names none) with its members spread from it, a paints-ground group's pad outline, the objects whose roster record names the point, and the route and trigger-area lines on it follow; `MissionDrawables.Move` and `ScenePicker.Move` follow each object that moves, and `MissionOverlay.Replan` lays again only the lines that moved. A waypoint dragged alone moves its route's line and nothing else until the commit. The commit writes the `.MSN` and syncs the scene as any edit does ([Rebuilding after an edit](handoff-editor-document.md#rebuilding-after-an-edit)), and the mission box overlay is rebuilt from the layout's points.

After every move, a drag's preview included, `TerrainLeveller` puts the heightmap and cell materials back as the zone loaded them and runs the spawn pass's pad painting and footprint flattening again over every structure and base pad where they now stand; structures and walking mechs are settled on the new ground, the changed rows of the terrain mesh are rebuilt and uploaded, and the overlay lines are laid again.

## What appending a point changes

- **Block 1's extent frames the heads-down map and the mission box** ([`../retail/simulation/heads-down-display.md`](../retail/simulation/heads-down-display.md#the-maps-frame-of-reference), `MissionBox`). A point outside the current box rescales the player's in-game map and moves the boundary warning and abort lines, which is why the views warn. A removed waypoint, a deleted route or area, and a shape change remove the points nothing names any more, so the box shrinks back.
- **File length is no constraint.** `DBSim_LoadScriptDat` (`00424308`) reads through a `FileRStream` block by block and allocates blocks 1 to 6 and the group array from their counts, so a file longer than the retail 13,520 bytes reads correctly. Its fixed arrays are the per-roster slot flags and slot maps ([`../retail/formats/script-dat.md`](../retail/formats/script-dat.md#the-two-pass-read--and-what-it-means-for-dbsim-keeps)), past which it writes over neighbouring globals; hence the roster caps the adds enforce ([Joining and caps](handoff-editor-roster.md#joining-and-caps)). VSHELL's reader, `ShellMap_LoadScriptDat` (`004243d7`), which reads a save slot's file for the briefing map, has not been checked for caps.
- **A mech's or flyer's own point has never been seen live.** No retail mech or flyer record names one (rule 2), so `Mech_AttachToGroup` and `Flyer_AttachToGroup` honouring it is RE-derived and engine-implemented, and moving either relies on it; structures' own points are exercised by retail data. Confirming it means writing such a file and running retail DBSIM.

## What a move does not change

- A non-leader group member keeps its `FormationOffset`, and AI followers hold formation on their leader every tick, so the move sets a spawn position the AI may walk out of.
- A paints-ground base group snaps to a fixed spot in its tile, so a small move does nothing and a larger one jumps a whole tile ([`../retail/formats/script-dat.md`](../retail/formats/script-dat.md#the-anchor)).
- A group waiting to arrive by drop pod or on foot is placed at a placeholder, so its position changes nothing once the mission runs ([`../retail/simulation/mission-deployment.md`](../retail/simulation/mission-deployment.md#arrival--group_deploymentcheck-004236c4)); it is not in the scene, so it has no move handles, and its view says how it arrives ([Waiting groups](handoff-editor-viewing.md#waiting-groups)). One arriving in place, and a held-back structure, stand where placed, the structure solid and undrawn from the first frame ([`../retail/simulation/mission-deployment.md`](../retail/simulation/mission-deployment.md#held-back-structures-stand-from-the-start)).

## Routes

**New route**, in the Toolbox and at the top of the outliner's Routes list, starts placing (`RoutePlacing`): each left click on the ground places the next waypoint, drawn with its legs and a leg to the pointer, with a hint across the top of the view. A click on the first waypoint closes the loop and finishes (with two waypoints or more; otherwise it does nothing); Enter or Finish finishes open; Esc or Cancel adds nothing, and Esc then reaches neither Back to game nor the unsaved-changes prompt. While placing, a click over a panel does nothing, the camera flies, handles and picking are off, and the Delete key, Undo, Redo and the Toolbox entries do nothing. No waypoint adds nothing; one waypoint is a route, as in retail, though the editor never makes a loop of one, which retail has (`C5_05`'s route 44, [`../retail/formats/msn-mission-file.md`](../retail/formats/msn-mission-file.md#row-8-field-decode--the-waypoint-group-dat_00470656-10-fixed-bytes--nested-count2-bytesrecord)) and which opens as it is. The route is selected.

The route view (`RouteView`, through `RouteEditor`):

- **Add waypoints** places more as New route does, after the last and before a closed route's closing entry, the whole session one step.
- **Insert after** puts a waypoint halfway along the leg after the selected one.
- **Remove** (or the Delete key on a waypoint) takes the selected waypoint's entry out; a closed route's first takes its closing entry with it and the loop closes on the next; a loop left with one waypoint is left open; the last waypoint is refused.
- **Close the loop** appends the first waypoint's point (refused below two waypoints); **Open the loop** removes it.
- **Delete** (or the Delete key on the route) is refused while an order, an objective or a group's own route field names it, one line per record, a group's orders and own route on one line.
- A list a variant supplies is the alternative's, as for a waypoint move, and the view says so.

The outliner lists every route of row 8, those no order names included, and one with no waypoint standing anywhere as "no waypoints". No waypoint count is capped (no doc records a fixed-size waypoint array in DBSIM); the heads-down map marks at most waypoints 1 to 9 of the player's route ([`../retail/simulation/heads-down-display.md`](../retail/simulation/heads-down-display.md#markers)), and the route view says so for a longer one.

Gaps:

- Nothing sets a group's own route field; a new route is walked once a group's first order names it ([Orders](handoff-editor-mission-logic.md#orders)).
- A waypoint has no Z field or handle and is always written Z 0, as 1487 of the 1488 retail waypoints have it. DBSIM reads a waypoint's Z as the starting altitude of a flight standing on its route's first waypoint, and the heads-down map projects the player's route icons with it ([`../retail/simulation/ai-goals.md`](../retail/simulation/ai-goals.md#the-route-cursor-is-loaded-once)); only a flyer group's Z handle sets it. This engine's heads-down map places route icons by X and Y alone.
- A list entry naming a point the generation leaves out is not listed; the view edits the entries around it, and Insert after refuses a leg ending on one.
- A list a variant supplies cannot be given to the route alone; a route's table is shown read-only.

## Trigger areas

**New box** and **New circle**, in the Toolbox and at the top of the outliner's Trigger areas list, start placing (`AreaPlacing`): a box is a press at one corner and a drag to the opposite one, a circle a press at its centre and a drag to its radius, drawn as it grows with its size by the pointer, and added on release. Esc or Cancel adds nothing (and reaches neither Back to game nor the prompt); a box with no width or depth, or a circle with no radius, adds nothing; a press over a panel starts none. While placing, handles and picking are off, and the Delete key, Undo, Redo, the Toolbox entries and New route do nothing. A box's corners need no order ([`../retail/simulation/mission-deployment.md`](../retail/simulation/mission-deployment.md#the-areas--block-4-resolved-by-triggerarea_resolve-00423358)). The generation exports every row-9 record whether or not a trigger references it, so a new area reaches block 4; the view of one no trigger tests says it does nothing. The area is selected.

The area view (`TriggerAreaView`, through `AreaEditor`):

- **Box or Circle.** A box becomes the circle about its middle with a radius of half its longer side; a circle the square of its centre plus and minus its radius (the editor's choice).
- **Corner or centre fields** and a circle's **radius** in world units, stored in tens, rounded; with how many other records reference each point.
- **Handles** on each box corner, or on a circle's centre with a diamond on its edge for the radius. A corner moves as a waypoint does, Z 0 always (the area test ignores Z). A corner or radius move goes through `PointMover`'s preview and commit; the outline, the tint, whatever shares the point and the mission box follow.
- **The triggers testing it**, each read-only as the trigger view summarizes it, and **New trigger testing this area** ([Triggers](handoff-editor-mission-logic.md#triggers)).
- **Delete** (or the Delete key) is refused while a trigger references the area in any of its eight slots, since the load stops reading a trigger's areas at its first `-1`.

Retail: 58 of 62 missions use trigger areas, 257 distinct areas named 143 times as boxes and 132 as circles, 16 shared by more than one action; actions test the player in 142 of 338 and the player's group in 170. `C2_01`'s boundary strips and `TRAIN8.MSN`'s three defeat-chained reinforcement waves ([`../retail/simulation/mission-deployment.md`](../retail/simulation/mission-deployment.md#train8msn-end-to-end)) are good test cases.

Gaps:

- A shape change and a radius edit write the area's supplying record alone: another record of its GUID, conditioned, keeps its own shape and points in the states it survives in.
- All of a selected area's handles show at once; two corners closer on screen than the handles' reach are taken in the order the box names them.
