# DBSIM.EXE — mission groups, actions and trigger areas

**Each of ES1's mission groups holds one target for its machines, where an ES2 group works through a list of up to ten orders; and a mission action, when it fires, changes one campaign flag, where ES2's changes up to ten and puts a message on screen.** Trigger areas, routes and the squad order broadcast are ES2's. The functions are one object file, `0001c824`–`0001d5fb`, between `flyersys.cpp` and `world.cpp`, whose data holds no source path, so it has no name ([where the module starts](world.md#where-the-module-starts)). Addresses are `ES1DBSIM` image addresses ([the X-32 executable](../formats/x32-executable.md#the-image)); ES2's are DBSIM's.

## The object fields

The machines' fields these functions read stand 6 bytes below ES2's up to `+0xad`, and `0x6a` below from the type record on. The vptr is at `+2`, and a slot is ES2's slot plus 4, since an ES1 vtable opens with a zero dword ([vtables](../formats/compiler.md#vtables)).

| ES1 | ES2 | Field |
|---|---|---|
| `+0x20` | `+0x26` | the position |
| `+0x3f` | `+0x45` | the group |
| `+0x47` | `+0x4d` | the behaviour descriptor, its countdown object at `+0x4b` and count at `+0x4c` |
| `+0x90` | | the byte the broadcast scores verbs 4 and 7 by, where ES2's reads the radar's state |
| `+0x93` | `+0x99` | destroyed |
| `+0x9e` | `+0xa4` | immobilised |
| `+0xa9` | `+0xaf` | skip the next think |
| `+0xad` | `+0xb3` | worth no salvage |
| `+0x188` | `+0x1f2` | the type record |
| `+0x198` | | the hardpoint list: its array of pointers at `+0`, their `int16` count at `+4` |
| `+0x19c` | `+0x206` | the component damage |
| `+0x23a` | `+0x2a4` | the kill tallies |

## Trigger areas

`TriggerArea_Resolve` (`0001cb48`) and `TriggerArea_ContainsPoint` (`0001cb94`) are ES2's ([the areas](../../retail/simulation/mission-deployment.md#the-areas--block-4-resolved-by-triggerarea_resolve-00423358)). A block-4 record of `data\script.dat` ([reading](../formats/script-dat.md#reading)) resolves to 10 bytes: its first word, the type, at `+0`; at `+2` a pointer to the point its second word indexes; at `+6` a pointer to the point its third word indexes for type 0, or the third word × 10 for any other type. Type 0 contains a position strictly between the two points in X and in Y; any other type contains one whose `Math_DistanceBetweenPoints2D` (`00063c64`) from the first point is less than `+6`.

`TriggerArea_Resolve` takes (area, *n*, points, record) in stack order: the block-1 points as the two dwords `000bc47c` and `000bc480`, of which it reads only the second, the array. ES2's takes the array alone. `DBSim_LoadScriptDat` resolves each block-4 record with it.

## Actions

An action is the 16-byte record `DBSim_LoadScriptDat` builds from block 5 ([reading](../formats/script-dat.md#reading)). These functions read its trigger areas' count at `+4` and their pointer array at `+6`, the activated flag at `+0xa`, and the (flag, operation) pair at `+0xc` and `+0xe`, from `.MSN` `0x1a` and `0x1c`.

- **`Action_Activate` (`0001cc5c`)** acts once: when `+0xa` is 0 it sets it to 1 and, when the flag number `+0xc` is not negative, adds 1 to that flag of `000bc4b8` for operation 6 or sets it to 0 for operation 5 ([`mission.var`'s flags](world.md#globals)). ES2's walks ten such pairs and queues the action's message inside that loop ([ES2's](../../retail/simulation/mission-deployment.md#the-mission-counters--dat_004a9ef4)); ES1's has one pair and queues nothing. `es2_xref.py` finds 11 calls.
- **`Action_TestTrigger` (`0001ccb4`)**, (action, position): ES2's ([trigger areas](../../retail/simulation/mission-deployment.md#trigger-areas--actions_evaluatetriggers-00426b70)). 1 at once for an action already activated; otherwise it offers the position to each of the action's areas in turn and activates the action on the first that contains it.
- **`Action_TestTriggerForGroup` (`0001cd30`)**, (group, action): `Action_TestTrigger` with each member's position in turn, returning the first non-zero answer.

## The group record

ES1's group record is `0x28` bytes, where ES2's is `0x7a` ([ES2's](../../retail/simulation/ai-goals.md#the-group-records-order-fields)). `DBSim_BuildGroupRecord` (`0001cff4`) fills it from a block-9 record of `data\script.dat` ([the groups](../formats/msn-mission-file.md#groups); a block offset is the `.MSN` offset less 6) and from arguments its caller, `DBSim_SpawnMissionObjects` (`0001e638`), resolves from the record's refs ([the second pass](../formats/script-dat.md#the-second-pass)); it takes (group, point, groups, heading, action, route, record) in stack order.

| Offset | Type | Field | From |
|---|---|---|---|
| `+0x00` | `int16` | the members' kind: 0 mechs, 1 flyers, 2 bases | `.MSN` `0x2e` |
| `+0x02`, `+0x04` | `int16` | copied, no reader read ([Open](#open)) | `.MSN` `0x30`, `0x32` |
| `+0x06` | `int16` | the route cursor's index, 0 from the element constructor | |
| `+0x08` | ptr | the route, a waypoint group | the argument *route*: `.MSN` `0x38` |
| `+0x0c` | `int16` | the heading | the argument *heading*: `.MSN` `0x36`, or the route's first leg |
| `+0x0e`, `+0x12` | ptr, `int16` | the member array and its count | |
| `+0x14` | byte | the side: `Mission_TotalSalvage` counts a machine whose group's byte differs from the player's group's | `.MSN` `0x4a`'s low byte |
| `+0x16` | ptr | the action; the shot sweep, the blast and the trigger test skip the group's members while it is set ([`world.md`](world.md#shots-against-objects--sim_raycastobjectlist-0001f1d0)) | the argument *action*: `.MSN` `0x50` |
| `+0x1a` | `int16` | the target's kind: 0 a group, 1 a mech, 2 a flyer, 3 a base | `.MSN` `0x4c` |
| `+0x1c` | ptr | the target | `.MSN` `0x4e` |
| `+0x20` | ptr | the group's point | the argument *point*: `.MSN` `0x34` |
| `+0x24`, `+0x26` | `int16` | the group's out-of-action (flag, operation) pair, [below](#going-out-of-the-fight) | `.MSN` `0x54`, `0x56` |

The members are the non-negative ones of the eight refs at `.MSN` `0x3a`–`0x49`, in order, in an array of that many pointers from `operator_new`. Each is `Pool_Nth` (`00063a78`) of the pool its kind names, the dword at `000af68c`, `000af72c` or `000aed70`, and is passed with its ref's position among the eight (0–7) and the group to `00013124`, `0001b574` or `000044a4` ([Open](#open)). The target resolves by its kind: a group as the groups array's base plus the index × `0x28`, a mech, flyer or base as `Pool_Nth` of the dword at `000af68c`, `000af72c` or `000aed70`. `Pool_Nth`, (pool, *n*), walks the pool with `Pool_Prev` from 0 and returns the element *n* + 1 steps in, or 0 when the walk ends first, as ES2's does; it keeps *n* in `DX` across the calls.

ES2's group has twenty member refs, ten counter pairs, and the target as the subject of each of its ten orders, which it works through ([the tick](../../retail/simulation/ai-goals.md#the-tick--group_ordertick-00423a74)).

`MissionGroups_Allocate` (`0001d260`), (array, count): the count at the array's `+4` as an `int16` and at `+0` a `__vec_new` array of count `0x28`-byte records, each built by `MissionGroup_ElementCtor` (`0001d288`), which allocates the record when given none and zeroes the route cursor's index at `+6`. `DBSim_LoadScriptDat` allocates the groups with it (`0001e25b`); `es2_xref.py` finds `__vec_new`'s argument at `0001d276` holding the constructor's padding entry `0001d286`, and two more hits, `000179aa` and `00017195`, that are bytes of instructions addressing `+0x1d2`.

## The group's target

ES2's four accessors read the current one of the group's ten orders; ES1's read the group's one target, with no null test and no test for kind −1:

| ES1 | Kind 0 (a group) | Kinds 1–3 | Other kinds |
|---|---|---|---|
| `Group_OrderTargetObject` (`0001cda0`) | the target group's first member | the target | 0 |
| `Group_OrderTargetPosition` (`0001cde0`) | that member's position | the target's position | the first waypoint of the group's route |
| `Group_IsOrderTarget` (`0001ce3c`), (group, object) | whether the object's group is the target | whether the object is the target | 0 |

`Group_OrderSubjectPtr` (`0001ce94`) returns `+0x1c`; `es2_xref.py` finds no branch or stored pointer to it.

`Group_NearestLiveMember` (`0001cea4`), (group, object): ES2's ([radio callouts](../../retail/simulation/ai-targeting.md#radio-callouts)), the member other than the object, neither destroyed nor immobilised, nearest it by `Math_DistanceBetweenPoints` if nearer than 100000, otherwise 0.

The route functions `Route_AdvanceCursor` (`0001c8c0`), `Route_NextWaypointClosesRoute` (`0001c908`) and `Route_WaypointAt` (`0001cfb8`) are ES2's ([the route cursor](../../retail/simulation/ai-goals.md#the-route-cursor-is-loaded-once)) on the cursor at `+6`: the index at `+6`, the route at `+8`, where ES2's are at `+4` and `+6`.

## The tick

`Group_TickMembers` (`0001cf60`) runs `Mech_AiTick` (`0000d8e8`) on each member; there is no order to advance. `es2_xref.py` finds one call, from `00059700`.

`Mech_AiTick` is ES2's ([the AI tick](../../retail/simulation/ai-dispatch.md#the-ai-tick--mech_aitick-00411cec)) on ES1's fields: with a descriptor at `+0x47`, it calls slot `+0x20` (reassess) when the count `+0x4c` is 0; `00064178` on the countdown object `+0x4b` unless the descriptor's byte `+8` is set; slot `+0x18` (move); then slot `+0x1c` (think), zeroing the count when it answers non-zero, unless `+0xa9` is set, which it clears instead; and last `0000fe84` on the block at `+0x47`.

## The squad order broadcast

`Squad_BroadcastOrder` (`0001c950`), (group, order): ES2's ([the two ways an order leaves the cockpit](../../retail/simulation/ai-squadmates.md#the-two-ways-an-order-leaves-the-cockpit)), with the issuer at the order's `+2`, the committed byte at the descriptor's `+9`, the byte `+0x90` where ES2's reads the radar's state ([the object fields](#the-object-fields)), the destroyed flag at `+0x93`, and the order sent through the member's slot `+0x2c` with the reply so far and the order. Its switch over verbs 0 to `0xf` is ES2's, and so is the score left unassigned for a verb with no case. `es2_xref.py` finds one call, from `0002b448`.

## Going out of the fight

`Group_ReportIfAllOutOfAction` (`0001d524`), (group, object): ES2's ([the out-of-action report](../../retail/simulation/mission-deployment.md#the-out-of-action-report)) with one pair where ES2's has ten. It returns at the first member other than the object that is neither destroyed nor immobilised; otherwise, when the flag number `+0x24` is not negative, it writes that flag of `000bc4b8` by the operation `+0x26`: 2 adds 1, 1 sets 0, `0xd` to `0x10` store the operation less `0xc`, any other writes nothing. The operation is looked up by `REPNE SCASD` in the six dwords at `0009222c` (2, 1, `0xd`, `0xe`, `0xf`, `0x10`) with their targets after them. `es2_xref.py` finds one call, from `Mech_ReportOutOfAction` (`0000d77c`, [the out-of-action report](sim-object-base.md#the-out-of-action-report--mech_reportoutofaction-0000d77c)).

`maybe_Group_ApplyOutnumberedDamage` (`0001d4d0`) calls `00015a5c` on each member; `es2_xref.py` finds one call, from `00059700`. ES2's `Group_ApplyOutnumberedDamage` (`00423f08`) is the same loop ([Open](#open)).

## What the results read

[`Mission_WriteResults`](world.md#writing-the-results--mission_writeresults-0001d698) calls two of the module's functions.

**`Mission_TotalSalvage` (`0001d434`)**, (group, *w*): ES2's ([what the mission leaves the shell](../../retail/simulation/mission-objectives.md#what-the-mission-leaves-the-shell--mission_writeresults-0042412c)). It walks the mech pool, the dword at `000af68c`, from its end with `Pool_Prev`, and sums `Mech_SalvageValue` (`00014034`) over every machine whose group's side byte differs from the group's and that is destroyed or immobilised. It ignores *w*.

`Mech_SalvageValue` is ES2's ([what a wreck is worth](../../retail/simulation/component-damage.md#what-a-wreck-is-worth--mech_salvagevalue-00418e60)): 0 when `+0xad` is set; otherwise, for each hardpoint whose component reading (`0000a16c` on `+0x19c`, component `0x13` plus the byte `+0x17` of the hardpoint's `+0x18`) is below `0x80`, it passes the pair {the `int16` at `+0x56` of the hardpoint's `+0x1c`, ((`0x100` − reading) × 100) >> 8} to `Salvage_QueueWeapon` (`0001f7e8`, [small functions](world.md#small-functions)), and it returns `Math_Q10Multiply`(`00010c38` on `+0x19c`, the type record's `+0x54`), the second halved when component 0 reads `0x100`.

**`Group_WriteStatusBlocks` (`0001d2c8`)**, (group, stream): ES2's ([the status blocks](../../retail/simulation/mission-objectives.md#what-the-mission-leaves-the-shell--mission_writeresults-0042412c)). For each member in order, `Mech_ReadDamageReadouts` (`0001583c`) fills a buffer with the type record's `+0x6c` and then `000109bc`'s readouts of `+0x19c`; entries 1–13, 20–29 and 32–41 are each written as ((`0x100` − *r*) × 100) >> 8 through the stream's slot `+0x18`, and then the 6 bytes at `+0x23a`: 72 bytes a member.

## The module's start

The four functions at `0001c824`–`0001c8bf` are the copy of `ConfigPart`'s destructors that opens the object file ([vtables](../formats/compiler.md#vtables)): `HeapPtr_DtorGroupsCopy` (`0001c824`), which frees the block its argument points at and then the argument when bit 0 of the flags is set, and which `es2_xref.py` finds no branch or stored pointer to; `ConfigPart_DtorGroupsCopy` (`0001c84c`), slot `+0x1c` of `00092142`, which stores `00092142`, runs `HeapBlockOwner_Dtor` on `+0x10`, stores `00092176` and runs `TSBase_Dtor`; `ConfigPart_GetClassTagGroupsCopy` (`0001c88b`), slot `+4`, `nop; xor eax,eax; ret 4`; and `TSPartBase_DtorGroupsCopy` (`0001c894`), slot `+0x1c` of `00092176`.

## Functions

| ES1 | Address | ES2 |
|---|---|---|
| `HeapPtr_DtorGroupsCopy`, `ConfigPart_DtorGroupsCopy`, `ConfigPart_GetClassTagGroupsCopy`, `TSPartBase_DtorGroupsCopy` | `0001c824`, `0001c84c`, `0001c88b`, `0001c894` | |
| `Route_AdvanceCursor`, `Route_NextWaypointClosesRoute` | `0001c8c0`, `0001c908` | `0042313c`, `00423170` |
| `Squad_BroadcastOrder` | `0001c950` | `004231a4` |
| `TriggerArea_Resolve`, `TriggerArea_ContainsPoint` | `0001cb48`, `0001cb94` | `00423358`, `004233a4` |
| `Action_Activate`, `Action_TestTrigger`, `Action_TestTriggerForGroup` | `0001cc5c`, `0001ccb4`, `0001cd30` | `00423430`, `004234b8`, `00423508` |
| `Group_OrderTargetObject`, `Group_OrderTargetPosition`, `Group_IsOrderTarget`, `Group_OrderSubjectPtr` | `0001cda0`, `0001cde0`, `0001ce3c`, `0001ce94` | `004238a0`, `004238d4`, `00423918`, `00423960` |
| `Group_NearestLiveMember`, `Group_TickMembers` | `0001cea4`, `0001cf60` | `00423974`, `Group_OrderTick` (`00423a74`) without the orders |
| `Route_WaypointAt`, `DBSim_BuildGroupRecord` | `0001cfb8`, `0001cff4` | `00423b0c`, `00423b34` |
| `MissionGroups_Allocate`, `MissionGroup_ElementCtor` | `0001d260`, `0001d288` | `00423d38`, `004240e4` |
| `Group_WriteStatusBlocks`, `Mission_TotalSalvage` | `0001d2c8`, `0001d434` | `00423d68`, `00423e88` |
| `maybe_Group_ApplyOutnumberedDamage`, `Group_ReportIfAllOutOfAction` | `0001d4d0`, `0001d524` | `00423f08`, `00423f30` |

Callees of other modules read for them: `Mech_AiTick` (`0000d8e8`), `Mech_SalvageValue` (`00014034`), `Mech_ReadDamageReadouts` (`0001583c`) and `Pool_Nth` (`00063a78`), ES2's `00411cec`, `00418e60`, `0041b4ec` and `00471ba4`.

## Open

- **Open:** what reads the group's point `+0x20`, heading `+0x0c` and `+0x02`, `+0x04`, and whether anything clears the action `+0x16`, which stands where ES2's [deployment gate](../../retail/simulation/mission-deployment.md#the-deployment-gate--group0x14) does.
- **Open:** the callees not read: `00013124`, `0001b574` and `000044a4`, which take a mech, flyer or base member with its ref's position and the group; `00015a5c`, which decides `0001d4d0`'s name; `0000a16c`, `00010c38` and `000109bc`, the component readings; `00064178` and `0000fe84`, the AI tick's countdown and last call.
- **Open:** five of the eleven calls `es2_xref.py` finds to `Action_Activate`, from `00003978`, `00013250` (two), `00018e50` and `0001b290`. Three others are the sensor model's engagement, from `Detection_ShareContact` (`0000e618`) and twice from `Detection_Sweep` (`0000e85c`) ([the sensor model](sensor-model.md#looking-around--detection_sweep-0000e85c)), and the last three are `Action_TestTrigger`, `Actions_EvaluateTriggers` (`0001f87c`, [trigger areas](world.md#trigger-areas--actions_evaluatetriggers-0001f87c)) and `Sim_RaycastObjectList` (`0001f1d0`), which fires a struck enemy's `+0x16c` action.
- **Open:** whether anything changes a group's target after the load; ES1's target is set once, by `DBSim_BuildGroupRecord`, in the functions read.
- **Open:** the machine fields above are not in `known_structs.json`, which holds no ES1 layout; nor are the group record, the action and the trigger area.
- **Open:** a positive control in the running game for this module, as for [`world.cpp`'s](world.md#open).
