# Handoff — hercs, flyers, ground vehicles, structures and the squad in the editor

Adding roster objects from the Toolbox, changing their type and fit, a group's side, deleting, and the player's squad. Part of the [editor handoff](handoff-editor.md). Which records each tool writes, and the retail counts behind each new record's fields, are in [The tools](handoff-editor-document.md#the-tools).

## The Toolbox

The Toolbox window (`EditorToolbox`) lists, in order, Hercs (the 21 mech types of `nam\MECHS.NAM`), Flyers, Ground vehicles and Structures, each under the name the MFD status screen prints, with a Team choice (Human or Cybrid). An entry is added by dragging it out and letting go over the view (on the ground under the pointer), by double-clicking it, or with Add (on the ground at the middle of the view, or under the camera when that is sky, which the gamepad's pointer reaches too). While dragged over the view, the object it would add stands on the ground under the pointer (`DropPreview`, drawn but not picked, listed or levelled under). Letting go over a panel adds nothing. After an add the editor's state is rebuilt, which builds the object through `MissionScene.CreateObject`, the scene load's own path (its model uploaded then when nothing else draws with it), levels the ground under it, and selects it. Undo takes the records out; Redo puts the same ones back under the same GUIDs, with the same object standing for them.

### Joining and caps

With a group or one of its members selected, **Join the selected group** adds to that group instead. While the group cannot take the entry chosen or dragged, the checkbox is disabled and unticked with the reason under it, by the check the add itself refuses by (each placer's `WhyNotJoin`, through `HercGroups`, `FlyerGroups` and `VehicleGroups`), and the add gets a group of its own; an entry double-clicked in the frame it is chosen is checked again and refused with the reason when the box was ticked. A joined member takes the group's team.

An add past DBSIM's slots for its block is refused and that Toolbox heading disabled with the reason under it, counted as the mission check counts them: block 7's 100 for hercs (shared with the squad's `player.mec` machines), block 8's 50 for flyers, block 9's 140 for structures and ground vehicles together.

## Structures

The 45 structure types of `BASES.DAT`. `StructurePlacer` adds one in a group of its own, facing heading 0; the mission check passes it, since a group naming a heading needs no slot-0 order. Joined to a structure group, it names a point of its own where it was put and faces the group's way.

## Hercs

`HercPlacer` adds a herc in a new group hunting the player's squad (search/destroy on group 0) along a one-waypoint route, standing on that waypoint facing heading 0; the drag preview stands at its ride height, a RAZOR 5,000 units higher, where it starts. Its fit: a Human chassis (types 0 to 8) takes its stock fit from `gam\ini_*.dat`; a Cybrid (types 9 to 20), which no shell file fits and every mission fits in its own records, often through a random variant, takes the fit most retail records of its type carry, the first in mission and record order on a tie (the editor's choice). A join takes the first free member slot up to slot 7, the last `dat\MFORMS.DAT` spreads (the editor's limit), and stands in its formation place. Group 0 is refused; its view adds squadmates. Deleting the herc deletes its group and order; the route stays and can then be deleted.

## Flyers

The types of `nam\FLYERS.NAM` the install has a `dat\<type>.DAT` for (`SKIMMER` as LANDSKIMMER); `HOVTANK` and `DROPSHIP`, which have no data to fly or be shot with ([`../retail/simulation/ai-flyers.md`](../retail/simulation/ai-flyers.md)), are named in a note and not offered. `FlyerPlacer` adds one as a herc is added. Its waypoint has Z 0, so DBSIM starts it at 5,000 units ([`../retail/formats/script-dat.md`](../retail/formats/script-dat.md#placement--the-actual-rule), rule 5), as every retail flyer group starts, and the drag preview stands there; the group's Z handle changes it after. A join takes the first free slot up to slot 3, the last `dat\FFORMS.DAT` places beside the leader (the editor's limit; no retail flyer group has more than two members). Deleting the flyer deletes its group and order.

## Ground vehicles

The 15 types of `BASES.DAT` that DBSIM builds as ground vehicles ([`../retail/simulation/structure-behaviour.md`](../retail/simulation/structure-behaviour.md#five-classes-one-switch)); the other five vehicle types, for which it builds no object, are named in a note and not offered. `VehiclePlacer` adds one as a convoy, a structure group whose first order is travel. With one waypoint the leader has nowhere to drive ([`../retail/simulation/ground-vehicles.md`](../retail/simulation/ground-vehicles.md#the-ground-vehicle-tick--0046a5d0)), so it stands until waypoints are added to its route, and the Toolbox says so.

A structure group whose first member is a ground vehicle is a convoy. A vehicle joins one in the first free slot up to slot 3, since the leader pick, `Group_FirstAbleMember` (`0046a4b8`), reaches only the first four members (the editor's limit), naming no point, so it takes its formation post, as 29 of the 30 retail convoy members past slot 0 do. Joined to any other structure group it is placed as a structure is and stands there as a building, as the vehicles of the 39 retail base groups led by a building do; the 13 retail base groups with no orders that a vehicle leads are convoys to the editor. A structure joins a convoy as any structure group. Deleting the vehicle deletes its convoy and order.

## Type and fit

Outside the squad:

- **A structure's or ground vehicle's Type** offers the Toolbox's structures, or ground vehicles, so a building never becomes a vehicle or the reverse, and a convoy stays a convoy (`RosterTypeEditor`, the editor's choice). The ground is levelled under the new object.
- **A flyer's Type** offers the Toolbox's flyers.
- **A herc's Chassis** offers the 21 types; the new type takes the fit an add gives it, all ten slots written. A Human chassis then shows one row per mount, a weapon and for a launcher a guidance, offered and written as the squad's ([The player's squad](#the-players-squad), `MachineFitter`, drawn by `MachineFitView`); a Cybrid's fit is listed read-only, with a note, since nothing establishes which weapons its mounts take.

A field a variant table supplies is the alternative's to edit ([Variants](handoff-editor-conditions.md#variants)). Each change builds a new object and keeps it selected; the mission check tests no roster record's type, so it finds nothing new.

## Sides

A group's view offers its Team, the group's side (block 11 `0x6e`), for every group but the squad; all its members change, and the view says how many when there are others. An object's view shows its group's team fixed. `SideChanger` writes it and generates again, then sets the index's copy and each member's simulation object from the generated field, without a rebuild.

## Deleting

An object's view has Delete, and the Delete key deletes the selected object. `ObjectDeleter` removes every record of the object's GUID and its group's member slots naming it; a group left with no member goes too, with its orders that no other group runs, and the view names the group that goes with the object. The roster shrinks, so the slot caps count only what can spawn. The variant alternatives a deleted record drew from stay, and deleting a variant-keyed record moves every later draw ([Conditions and variants](handoff-editor-document.md#conditions-and-variants)).

A GUID with no record resolves to `-1` (`MissionGenerator.cs:473-496`), so the delete is refused while an objective, an action or an order names the object, or the group the delete would remove, and while the object fires an action of its own when engaged or defeated, a variant alternative it can draw included. The player's squad is refused (group 0's view removes squadmates). The ground is levelled again as after a move.

## The player's squad

Group 0's view shows the player's machine, then each squadmate: group 0's members in the unbroken run from its second slot, which the squad build reads (`Squad_SetPositionsInPlay`, [`../retail/shell/main-menu.md`](../retail/shell/main-menu.md#starting-a-practice-mission)), in that order. The player's own object's view shows the player's machine, and a squadmate's object that squadmate.

First they say which Hercs the squad pilots in this mission, by the launch's rule (`MissionSquad`, handed out by `MissionFileLaunch.Open` and `OpenNew` from `ShellTrainingLaunch.PilotsMissionMachine`):

- On practice rows 0 to 3 and in every mission loaded as `INSTANT ACTION` (one `gam\career.dat` does not list, and a new mission), the player pilots group 0's own machine; on rows 4 to 7 a stock fit of the practice screen's Herc Type, option `0x28` in `DATA\prefs.cfg`. On all of these the squadmates pilot group 0's further machines, and the view says that the last squadmate listed stays behind and how many go: the retail bug in that doc's squad build, which this engine keeps.
- In a campaign mission the squad pilots the career's machines from `DATA\player.mec`, and group 0's squadmates set only how many squad positions are in play ([`../retail/shell/campaign-loop.md`](../retail/shell/campaign-loop.md#loading-the-careers-mission)); the view says in bold that the HERCs and loadouts set here are not used there.

Then each machine is edited in every `.MSN` mission: a chassis among the nine Herc Type steps through, and one row per mount the chassis has (`Herc_CapacityForType`), each offering the weapons of the weapons screen's list whose chassis layout (`gam\arm_*.dat`, `ShellArmingLayouts`) has a record for that socket, and for a missile rack or the Razor's launcher the four guidance kinds ([`../retail/shell/weapons-and-repair.md`](../retail/shell/weapons-and-repair.md#fitting-a-weapon)). The career's unlock flags and the armory's stock are not applied, as a mission's machine is fitted from no stock. A weapon chosen takes the guidance the weapons screen fits it with: ARH on a missile rack, none otherwise. A chassis chosen comes with its stock fit from `gam\ini_*.dat` ([`../retail/formats/herc-catalogs.md`](../retail/formats/herc-catalogs.md#gamini_dat--the-stock-fit-per-chassis)), an empty mount `0` and each slot past the capacity `-1` with filler 5, as the retail squad records have them. The player's machines of `TRAIN1` to `TRAIN4` and `TRAIN5`'s squad records are variant-keyed, so their edits take overlay records.

Group 0's view adds a squadmate after the last and removes any (`SquadEditor`). A new one is of the player's machine's chassis as it stands, with that chassis's stock fit. A remove moves each later squadmate up a slot, so order matters and the view says so. An add is refused past three squadmates, the three squad members the player structure points at and the squad build fits ([`../retail/shell/squad-and-crew.md`](../retail/shell/squad-and-crew.md#the-rows)), and past block 7's slots; a remove is refused as a delete is. Each edit first builds the handoff's `player.mec` again as the launch would ([Rebuilding after an edit](handoff-editor-document.md#rebuilding-after-an-edit)): where the squad pilots the mission's machines, the scene's squad takes the new chassis, fits and squadmates, a squadmate left behind having no object, and Export copies that squad.

The squad has no wiring (none of the 138 retail squad records sets a waited-on action, and none of the 278 records the squads reference sets an engaged or defeated action; DBSIM builds the machines from `player.mec`), no Present when and no Drawn from (the editor's choices).

Gaps:

- An overlay cannot carry a field's unset value, so a field a variant supplies keeps the variant's value where an edit would unset it: after a chassis change on `TRAIN1` to `TRAIN4`, a slot past the new capacity keeps the variant's weapon, and a mount that takes no guidance its ammunition type, neither of which the squad build or DBSIM reads.
- A launcher mount whose stock fit carries the filler 5 is written SARH (0), which DBSIM reads a 5 as ([`../retail/simulation/weapon-mounts.md`](../retail/simulation/weapon-mounts.md#the-join--mechloadout_constructweaponmounts-0040fff8)): the editor's choice, which leaves 0 in the generated record and `player.mec` where an unedited stock fit's launch holds 5. A herc outside the squad edited with "Only this herc" follows the same two rules.
- Removing a squadmate whose record draws a variant, as `TRAIN5`'s do, moves every later draw.

## Gaps

- Once added, an object changes only its point, type (and a herc's fit, but not a Cybrid's weapons), the actions it fires, the states it is present in and the table it draws from; its group changes only its team, orders and, for a flyer group, height. The rest is in [What is left, by row](handoff-editor.md#what-is-left-by-row).
- Nothing deletes a group directly, only with its last member. A refused delete cannot be forced, and the view offers no way to clear the reference that refuses it.
