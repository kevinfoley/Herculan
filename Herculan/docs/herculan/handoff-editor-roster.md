# Handoff — hercs, flyers, ground vehicles, structures and the squad in the editor

Adding roster objects from the Toolbox, changing their type and fit, a group's side, deleting, and the player's squad. Part of the [editor handoff](handoff-editor.md). Which records each tool writes, and the retail counts behind each new record's fields, are in [The tools](handoff-editor-document.md#the-tools).

## The Toolbox

The Toolbox window (`EditorToolbox`) lists, in order, Hercs (the 21 mech types of `nam\MECHS.NAM`), Flyers, Ground vehicles and Structures, each under the name the MFD status screen prints, with a Team choice (Human or Cybrid). An entry is added by dragging it out and letting go over the view (on the ground under the pointer), by double-clicking it, or with Add (on the ground at the middle of the view, or under the camera when that is sky, which the gamepad's pointer reaches too). While dragged over the view, the object it would add stands on the ground under the pointer (`DropPreview`, drawn but not picked, listed or levelled under). Letting go over a panel adds nothing. After an add the scene is synced with the document, which builds the object through `MissionScene.CreateObject`, the path every object of the editor's is built by (its model uploaded then when nothing else draws with it), levels the ground under it, and selects it by its record. Undo takes the records out; Redo puts the same ones back under the same GUIDs, with the same object standing for them.

### Joining and caps

With a group or one of its members selected, **Join the selected group** adds to that group instead. While the group cannot take the entry chosen or dragged, the checkbox is disabled and unticked with the reason under it, by the check the add itself refuses by (each placer's `WhyNotJoin`, through `HercGroups`, `FlyerGroups` and `VehicleGroups`), and the add gets a group of its own; an entry double-clicked in the frame it is chosen is checked again and refused with the reason when the box was ticked. A joined member takes the group's team.

An add past DBSIM's slots for its block is refused and that Toolbox heading disabled with the reason under it ("At maximum HERC limit (100)."), counted as the mission check counts them, over every version of the mission at once (`RosterLimits`): every GUID of the roster's row whatever its condition, and for block 7 one machine per squad position group 0 gives, which `player.mec` brings. Block 7 holds 100 hercs, block 8 50 flyers and block 9 140 structures and ground vehicles together.

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
- **A herc's Chassis** offers the 21 types; the new type takes the fit an add gives it, all ten slots written. Then one row per hardpoint, a weapon and for a launcher a guidance (`MachineFitter`, drawn by `MachineFitView`): a Human chassis's offered and written as the squad's ([The player's squad](#the-players-squad)), a Cybrid's as [A Cybrid's weapons](#a-cybrids-weapons) says.

A field a variant table supplies is the alternative's to edit ([Variants](handoff-editor-conditions.md#variants)). Each change builds a new object and keeps it selected; the mission check tests no roster record's type, so it finds nothing new. A flyer's, structure's or ground vehicle's Type, and a herc's Chassis and hardpoints, are changed for any record the layout has (`RosterTypeEditor`, `MachineFitter`), one the mission as opened leaves out included.

### A Cybrid's weapons

By the user's choice, a Cybrid's weapons are chosen from what retail missions fit, since no shell file says which weapons a Cybrid's mounts take (`CybridWeapons`, over Core's `RetailMountFits`):

- Its hardpoints are the fit slots its `gl\<NAME>.GL` hardpoints address ([`../retail/formats/gun-layout-gl.md`](../retail/formats/gun-layout-gl.md#record)), each numbered from 1 by its slot, as the squad's are (the editor's choice: no retail screen shows a Cybrid's).
- Each offers, in a dropdown, the weapons the install's retail missions fit in that slot of that chassis: every row-12 record of the type in the missions as the archives hold them, not a mission saved over one, variant options (GUID `-1`) included and no condition tested; "(empty)" only where such a record leaves the slot empty. A record that draws a variant carries type `-1` and no fit of its own in every retail mission, and so does every later record of a GUID, so each fit counts once. The list is read once per editor run, the first time a Cybrid's hardpoints are drawn; the weapons screen's order, a weapon it does not list after by id, and "(empty)" last (the editor's choice).
- Every slot a retail record fits on a Cybrid is one its hardpoints address. MIRIMAC's sixth hardpoint is fitted by no retail record, and SPIDER has no hardpoint; such a hardpoint's weapon is read-only, with the reason.
- A weapon chosen takes the ammunition type its records carry with it in that slot most often, the lowest on a tie (the editor's choice). In retail that is the filler 5 for every weapon that takes no guidance but BMSL, which both PITBULL records carrying it give 0; a missile rack takes what its records give it there (DIABLO's MSL10 carries 5 in 125 records and ARH in 39, so it takes 5, which DBSIM reads as SARH). A launcher's guidance then stays editable, as a Human's is.
- A record holding a weapon no retail record fits there shows it as the current choice, marked "(not from a retail mission)".
- With no mission in the install's archives, every hardpoint's weapon is read-only, with a note; the guidance stays editable.

A new Cybrid's fit, which `HercPlacer` and a chassis change give, is still [the most common retail fit of its type](#hercs).

## Sides

A group's view offers its Team, the group's side (block 11 `0x6e`), for every group but the squad; all its members change, and the view says how many when there are others. An object's view shows its group's team fixed. `SideChanger` takes the group by its row-16 GUID, any group the layout has, and writes it; the sync then sets each member's simulation object from the layout.

## Deleting

An object's view has Delete, and the Delete key deletes the selected object. `ObjectDeleter` removes every record of the object's GUID and its group's member slots naming it; a group left with no member goes too, with its orders that no other group runs, and the view names the group that goes with the object. The roster shrinks, so the slot caps count only what can spawn. The variant alternatives a deleted record drew from stay, and deleting a variant-keyed record moves every later draw ([Conditions and variants](handoff-editor-document.md#conditions-and-variants)).

A GUID with no record resolves to `-1` (`MissionGenerator.cs:473-496`), so the delete is refused while an objective, an action or an order names the object, or the group the delete would remove, and while the object fires an action of its own when engaged or defeated, a variant alternative it can draw included. The view lists each by its identity in the layout, one the mission as opened leaves out included (`ObjectDeleter.Resolve`): an objective or trigger by its link, a trigger it fires with ": fired by it", an order as its group and slot ("Cybrid hercs (group 4): 1st order"), and the options of random choices and the orders no group runs as counts. The player's squad is refused (group 0's view removes squadmates); Core's own refusal finds the squad as the layout's first group (`RosterDeletion.Check`, `SquadRoster.Squad`). The ground is levelled again as after a move.

## The player's squad

The squad is group 0, the layout's first group, read by GUID (`SquadRoster`, `SquadEditor`). Its view shows the player's HERC, its first member, then each squadmate, "Squadmate 1" to "Squadmate 3" by place in the list: group 0's members in the unbroken run from its second slot, which the squad build reads (`Squad_SetPositionsInPlay`, [`../retail/shell/main-menu.md`](../retail/shell/main-menu.md#starting-a-practice-mission)), in that order. The player's own object's view shows the player's HERC, and a squadmate's object that squadmate; a herc after a gap in the member slots, which no squad build reads, says it is not in the squad in play. No text in these views says "group 0" or "member slot".

First they say which Hercs the squad pilots in this mission, by the launch's rule (`MissionSquad`, handed out by `MissionFileLaunch.Open` and `OpenNew` from `ShellTrainingLaunch.PilotsMissionMachine`):

- On practice rows 0 to 3 and in every mission loaded as `INSTANT ACTION` (one `gam\career.dat` does not list, and a new mission), the player pilots group 0's own machine; on rows 4 to 7 a stock fit of the practice screen's Herc Type, option `0x28` in `DATA\prefs.cfg`. On all of these the squadmates pilot group 0's further machines, and the view says that the last squadmate listed stays behind and how many go: the retail bug in that doc's squad build, which this engine keeps.
- In a campaign mission the squad pilots the career's machines from `DATA\player.mec`, and group 0's squadmates set only how many squad positions are in play ([`../retail/shell/campaign-loop.md`](../retail/shell/campaign-loop.md#loading-the-careers-mission)); the view says in bold that the HERCs and weapons set here are not used and that the number of squadmates listed sets how many of the career's squadmates can go, and puts each HERC's chassis and hardpoints under a collapsed "HERC and weapons (not used in the campaign)" header. Add and Remove stay outside it, since the count is used.

Then each machine is edited in every `.MSN` mission: a chassis among the nine Herc Type steps through, and one row per mount the chassis has (`Herc_CapacityForType`), "Hardpoint 1" up (the user's spelling; the weapons screen's stepper reads "Hard Points", [`../retail/shell/weapons-and-repair.md`](../retail/shell/weapons-and-repair.md#the-weapons-screen)), numbered as the repair screen numbers them, from 1 by slot ([A row is four text columns](../retail/shell/weapons-and-repair.md#a-row-is-four-text-columns)), an empty one "(empty)", each offering the weapons of the weapons screen's list whose chassis layout (`gam\arm_*.dat`, `ShellArmingLayouts`) has a record for that socket, and for a missile rack or the Razor's launcher the four guidance kinds ([`../retail/shell/weapons-and-repair.md`](../retail/shell/weapons-and-repair.md#fitting-a-weapon)). The career's unlock flags and the armory's stock are not applied, as a mission's machine is fitted from no stock. A weapon chosen takes the guidance the weapons screen fits it with: ARH on a missile rack, none otherwise. A chassis chosen comes with its stock fit from `gam\ini_*.dat` ([`../retail/formats/herc-catalogs.md`](../retail/formats/herc-catalogs.md#gamini_dat--the-stock-fit-per-chassis)), an empty mount `0` and each slot past the capacity `-1` with filler 5, as the retail squad records have them. The player's machines of `TRAIN1` to `TRAIN4` and `TRAIN5`'s squad records are variant-keyed, so their edits take overlay records.

Group 0's view adds a squadmate after the last and removes any (`SquadEditor`). A new one is of the player's HERC's chassis as it stands, with that chassis's stock fit. A remove moves each later squadmate up a slot, so order matters and the view says so. An add is refused past three squadmates, the three squad members the player structure points at and the squad build fits ([`../retail/shell/squad-and-crew.md`](../retail/shell/squad-and-crew.md#the-rows)), and past block 7's slots, the add taking a row-12 GUID and a squad position; a remove is refused as a delete is. Export builds `player.mec` from the edited mission as the launch would ([The generation](handoff-editor-document.md#the-generation)). The scene shows group 0's hercs from the `.MSN` in every mission, one object per herc its member slots reference, spread from group 0 as the squad spawns (the user's choice): the squadmate a practice load leaves behind and a herc the Herc Type option replaces are drawn too, and the views' notes explain them ([Rebuilding after an edit](handoff-editor-document.md#rebuilding-after-an-edit)). Core finds the squad as the layout's first group (`SquadRoster.Squad`).

The squad has no wiring (none of the 138 retail squad records sets a waited-on action, and none of the 278 records the squads reference sets an engaged or defeated action; DBSIM builds the machines from `player.mec`), no Present when, and no choice of random choice: a squad herc that picks from one shows it, read-only (the editor's choices).

Gaps:

- An overlay cannot carry a field's unset value, so a field a variant supplies keeps the variant's value where an edit would unset it: after a chassis change on `TRAIN1` to `TRAIN4`, a slot past the new capacity keeps the variant's weapon, and a mount that takes no guidance its ammunition type, neither of which the squad build or DBSIM reads.
- A launcher mount whose stock fit carries the filler 5 is written SARH (0), which DBSIM reads a 5 as ([`../retail/simulation/weapon-mounts.md`](../retail/simulation/weapon-mounts.md#the-join--mechloadout_constructweaponmounts-0040fff8)): the editor's choice, which leaves 0 in the generated record and `player.mec` where an unedited stock fit's launch holds 5. A herc outside the squad edited with "Changes apply to:" "only this herc" follows the same two rules.
- Removing a squadmate whose record draws a variant, as `TRAIN5`'s do, moves every later draw.

## Gaps

- Once added, an object changes only its point, type (and a herc's fit), the actions it fires, the states it is present in and the table it draws from; its group changes only its team, orders and, for a flyer group, height. The rest is in [What is left, by row](handoff-editor.md#what-is-left-by-row).
- Nothing deletes a group directly, only with its last member. A refused delete cannot be forced, and the view offers no way to clear the reference that refuses it.
