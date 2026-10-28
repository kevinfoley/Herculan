# DBSIM.EXE — group conditions and the mission status

**ES1 decides how a mission stands by asking one question, picked per mission by header word 3 of `data\script.dat`, of the player's machine and squad, where ES2 walks a list of authored objectives ([ES2's status](../../retail/simulation/mission-objectives.md#the-status--mission_status-004135e8)); and what ES1 writes as the mission's outcome for the shell is the squad's report line, a number in three bands, where ES2 writes success or failure.** What a player would see differs in four places: the boundary warning comes 60000 units past the mission box and the abort 170000 past it, where ES2's come at the box and 110000 past it, and ES1's box always takes in the map's origin ([the mission box](#the-mission-box)); the status is read once every 10000 timer counts ([the poll](#the-poll--mission_pollstatus-0000f454)), outside the box as inside, where ES2 reads it every tick outside the box; when the squad's group order `+4` is 6, `guarding` ([choosing a state](mech-ai.md#choosing-a-state--mech_aiselectbehaviour-0001892c)), the mission ends with ES2's "mission failed" status as soon as the squad's target is badly damaged; and this module posts no computer message for a status, where ES2's posts `MISSION FAILED`, `MISSION SUCCESSFUL` and the two boundary lines ([Open](#open)). The tests of a group's condition and of being clear of threats are ES2's, asked of the group's one target where ES2's ask of the current order's subject ([the group's target](mission-groups.md#the-groups-target)). Addresses are `ES1DBSIM` image addresses ([the X-32 executable](../formats/x32-executable.md#the-image)); ES2's are DBSIM's. Every function here removes its own arguments, so argument lists are in stack order ([calling conventions](../formats/compiler.md#calling-conventions)).

## Where the object file starts and ends

The code is `0000eb68`–`0000fa1f`. It opens with a run of `ConfigPart`'s copies ([module copies](../formats/compiler.md#vtables)) right after the two zero bytes that end [the sensor model's file](sensor-model.md#where-the-object-file-starts-and-ends): `HeapPtr_DtorMissionStatusCopy` (`0000eb68`), `ConfigPart_DtorMissionStatusCopy` (`0000eb90`, slot `+0x1c` of `00091672`), `ConfigPart_GetClassTagMissionStatusCopy` (`0000ebcf`) and `TSPartBase_DtorMissionStatusCopy` (`0000ebd8`, slot `+0x1c` of `000916a6`). `Group_OrderSubjectArrivedAndClear`'s `RET 4` at `0000fa1a` is followed by three zero bytes and the next run at `0000fa20`, which opens [the shields' file](shields-and-behaviour-states.md#where-the-object-files-start-and-end) ([where object files meet](../formats/compiler.md#where-object-files-meet-in-the-code)). The file has no static initializer, and no assert in it names a source path. Its functions are ES2's from `Group_OrderSubjectObject` (`00412bbc`) to `Group_OrderSubjectArrivedAndClear` (`00413a08`), in the same order, less five ([functions](#functions)).

## The fields

The machines' fields stand 6 bytes below ES2's ([the object fields](mission-groups.md#the-object-fields)), and a slot 4 bytes above.

| ES1 | ES2 | Field |
|---|---|---|
| `+0x20` | `+0x26` | the position |
| `+0x3f` | `+0x45` | the group: its route cursor `+6` (index) and `+8` (route), member array `+0xe` and count `+0x12`, side byte `+0x14`, action `+0x16`, target kind `+0x1a` and target `+0x1c` ([the group record](mission-groups.md#the-group-record)) |
| `+0x93` | `+0x99` | destroyed |
| `+0x98` | `+0x9e` | engaged |
| `+0x99` | `+0x9f` | the byte the selector-0 status tests, ES2's "objective sighted", which `Mech_BehaviourPlayerThink` sets ([the player's think](mech-ai.md#the-players-think--mech_behaviourplayerthink-00015f80)) |
| `+0x9a` | `+0xa0` | the byte the selector-3 status tests, ES2's data link, which `Mech_BehaviourPlayerThink` sets at the link's fourth step |
| `+0x9e` | `+0xa4` | immobilised |
| `+0x9f` | `+0xa5` | disarmed |
| `+0xa0` | `+0xa6` | ES2's "scored a kill", which `Group_AnyMemberScoredAKill` reads ([Open](#open)) |
| `+0xae` | `+0xb4` | collapsed |
| `+0x15e` | `+0x1a4` | the selected target |
| slot `+0x44` | slot `+0x40` | the overall damage, Q8 |

## The group's target and its condition

`Group_OrderSubjectObject` (`0000ec04`) and `Group_OrderSubjectGroup` (`0000ec14`), (group), return the group's target `+0x1c`, as an object and as a group record; both are the code of `Group_OrderSubjectPtr` (`0001ce94`). The tests below take the group's target kind `+0x1a` where ES2's take the current order's `+0x0c`: 0 a group, anything else an object.

- **`Group_ConditionTier` (`0000ecc0`)**, (group): ES2's 0–4 scale ([the subject's condition](../../retail/simulation/ai-goals.md#the-subjects-condition--group_ordersubjectcondition-00412dc4)) with tier 3 reached at 500/1024 of the members lost where ES2's needs 650/1024: 4 when every member is immobilised or destroyed; 3 at 500/1024 lost or a mean overall damage of `0xc1`; 2 at 250/1024 or `0x81`; 1 at a mean of `0x33`; otherwise 0. The mean counts every member, the lost ones included. It divides by the member count `+0x12` with no test (`IDIV` at `0000ed6c` and `0000ed81`), so a group with no members faults ([what the retail missions give](#what-the-retail-missions-give-the-tests)).
- **`Group_OrderSubjectCondition` (`0000ee84`)**, (group): `Group_ConditionTier` of the target group, or for an object 4 when it is destroyed or collapsed and otherwise its overall damage banded at `0x32`, `0x80` and `0xc0`, as ES2's.
- **`Group_IsWipedOut` (`0000ec24`)**, (group): 1 when every member is disarmed, immobilised or destroyed, as ES2's (`00412be4`). `es2_xref.py` finds one call, from `Group_NoRivalOrderOnSubject`.
- **`Group_NoRivalOrderOnSubject` (`0000ef2c`)**, (group): 0 when another of the `0x28`-byte groups at `MissionGroups` (`000bc476`, count `MissionGroups_Count`) has a different side byte, the same target pointer and is not wiped out; otherwise 1. ES2's (`00412e74`) compares the two groups' current orders' subjects.
- **`Group_AnyMemberEngaged` (`0000ee18`)**, **`Group_AnyMemberObjectiveSighted` (`0000efac`)**, **`Group_AnyMemberDataLinked` (`0000f018`)** and **`Group_AnyMemberScoredAKill` (`0000f084`)**, (group): 1 when any member has `+0x98`, `+0x99`, `+0x9a` or `+0xa0` set, ES2's four.
- **`Group_OrderSubjectEngaged` (`0000edd8`)**, (group): `Group_AnyMemberEngaged` of the target group, or the target object's `+0x98`, as ES2's (`00412d4c`).

`es2_xref.py` finds no branch or stored pointer to `Group_NoRivalOrderOnSubject` or `Group_OrderSubjectEngaged` ([Open](#open)); in ES2 the first is a guard order's completion test, and ES1's groups have no orders.

## Clear of threats

- **`Mission_IsClearOfThreats` (`0000f744`)**, (subject): ES2's ([clear of threats](../../retail/simulation/mission-objectives.md#clear-of-threats--mission_isclearofthreats-004137b4)) on ES1's fields. It reads the subject's group `+0x3f` and the group of the player's machine `LocalPlayerMech` (`000b5b86`), which it does not test for null, and walks `LiveObjectList` (`000bc416`) up to its count (`000bc41c`) or a null entry, answering 1 at the end. It skips an object whose group's action `+0x16` is set, whose group's side byte `+0x14` is the subject's group's, or that is disarmed (`+0x9f`), immobilised (`+0x9e`) or destroyed (`+0x93`). For each other object, with *k* = `Ai_KnowsObject` (`0000d808`)(the object, the subject), it answers 0:
  1. when the subject's group is not the player's, `Group_IsOrderTarget` (`0001ce3c`)(the object's group, the subject) answers non-zero, and either `Route_WaypointAt` (`0001cfb8`)(the group's route cursor `+6`, the cursor's index + 1) finds a waypoint or *k* is set;
  2. when *k* is set and `Math_DistanceBetweenPoints` of the object's and the subject's `+0x20` is below 100000 when the object's selected target `+0x15e` is the subject, otherwise below 80000.
- **`Group_AllMembersClearOfThreats` (`0000f8b4`)**, (group): 1 when every member that is not destroyed is clear of threats. ES2's (`00413950`) first answers 0 for a group that is written off.
- **`Group_OrderSubjectRouteExhausted` (`0000f930`)**, (group): 1 when the target group's route, or the target object's group's route, has no waypoint after its cursor, as ES2's (`004139a0`).
- **`Group_OrderSubjectArrivedAndClear` (`0000f99c`)**, (group): 1 when the target group's action `+0x16` is clear and `Group_AllMembersClearOfThreats` answers 1, or the target object's group's action is clear and `Mission_IsClearOfThreats` answers 1, as ES2's (`00413a08`) with the action where ES2's tests its deployment gate.

`es2_xref.py` finds `Mission_IsClearOfThreats` called by `Mission_Status` (three times), `Group_AllMembersClearOfThreats`, `Group_OrderSubjectArrivedAndClear` and `Mech_ApplyOutnumberedDamage` (`00015a5c`, [small functions](mech.md#small-functions)), the callee of [`Group_ApplyOutnumberedDamage`](mission-groups.md#going-out-of-the-fight).

## The group report — `Group_StatusLineIndex` (`0000f0f0`)

(group, verb): ES2's group report ([the group report](../../retail/simulation/mission-objectives.md#the-group-report-and-why-nothing-shows-it)), the same for verbs 0 to 6 with the group's own route cursor at `+6`, and three more. With *tier* the group's `Group_ConditionTier` held to at most 3, *c* `Group_OrderSubjectCondition` and *a* `Group_OrderSubjectArrivedAndClear` of the group:

```
7  !a || c == 4                 ? tier+5
   : c == 0 && tier < 2         ? 1
   : c < 2 && tier <= 2         ? 2
   : c < 3 && tier <= 2         ? 3
   :                              4
8  c == 4                       ? tier+1
   : c >= 1 && tier == 0        ? 5
   :                              tier+5
9  c == 4 && tier == 0          ? 1
   : c >= 2                     ? max(tier, 1) + 1
   : c == 1 && tier < 2         ? tier+5
   :                              max(tier, 2) + 5
return result - 1
```

A verb above 9 or below 0 matches no case (an unsigned bound against 9 before the jump table at `000916d8`) and answers −2.

**It is the mission's outcome.** `es2_xref.py` finds one call, from [`Mission_WriteResults`](world.md#writing-the-results--mission_writeresults-0001d698), which passes the player's group and header word 3 and writes the answer as `data\results.dat`'s first word. What the shell makes of it is in [world.md's Open](world.md#open).

## The status — `Mission_Status` (`0000f4e8`)

(machine, quiet, selector), the player's machine, a flag that skips the box tests, and header word 3:

```
if (machine destroyed)                                    2
else if (machine immobilised)                             3
else if (group+4 == 6 && Group_OrderSubjectCondition(group) >= 3)
                                                          clear ? 6 : 4
else if (!quiet && outside box + 170000)                  8
else if (!quiet && outside box + 60000)                   7
else if (the selector's test holds)                       clear ? 9 : 10
else                                                      clear ? table[selector] : 4
```

*group* is the machine's group and *clear* `Mission_IsClearOfThreats`(machine). `+4` is the group's word from `.MSN` `0x32` ([the group record](mission-groups.md#the-group-record)). The selector's tests, by a jump table at `0009173c` behind an unsigned bound against 9:

| Selector | Holds when | `00091728`[selector] |
|---|---|---|
| 0 | the machine's `+0x99` is set | 12 |
| 1, 2 | the group's route has no waypoint after its cursor | 11 |
| 3 | the machine's `+0x9a` is set | 15 |
| 4, 5 | `Group_OrderSubjectArrivedAndClear`(group) | 13, 14 |
| 6, 8, 9 | `Group_OrderSubjectCondition`(group) is 4 | 16 |
| 7 | `Group_OrderSubjectRouteExhausted`(group) and `Group_OrderSubjectArrivedAndClear`(group) | 14 |

A selector outside 0–9 holds nothing and reads its status past the table's ten words. The retail missions' selectors all lie in 0–9 (`HercWorks.Query patches`): each of the 75 missions in the header-patch layout takes its word 3 from one unconditioned header patch, or keeps the 0 the load starts it at ([header patches](../formats/msn-mission-file.md#header-patches)), and the two in the other layout carry 3 (`B0_C1_20`) and 2 (`B0_C1_30`). Of the 75, 6 have selector 0, 9 have 1, 5 have 2, 12 have 3, 14 have 4, 3 have 5, 10 have 6, 5 have 7 and 11 have 9; none has 8.

Where ES2's differs: ES2's evaluates its objective records and ignores the selector it is passed; it has the 500 ms hold and the `SYSTEM.STR` post when not quiet; its box margins are 110000 and 0; and it has no test of the group's `+4`. ES1's gives ES2's numbers 2, 3, 7, 8, 9 and 10 for the same cases and never 5; 4 and 6 it also gives from the `+4` arm, and 11 to 16 are its own, each with a line of its own in the status panel ([what each status shows](alert-panels.md#what-each-status-shows)).

**`Mission_StatusForAlert` (`0000f43c`)**, (quiet, machine): `Mission_Status`(machine, quiet, `ScriptDatHeader` word 3, `000bc4a2`). ES2's (`00413180`) also records the answer as the status already raised when it publishes. `es2_xref.py` finds two calls: `Mission_PollStatus`, quiet 0, and `Q`'s case in `Sim_DispatchCommand` (`00059df8`), at `00059f87`, with quiet 1 and `LocalPlayerMech`, which shows the answer in the status panel ([`StatusAlertPanel_Ctor`](alert-panels.md#the-status-panel--statusalertpanel_ctor-00046efc), `00046efc`) and clears `MissionAlertArmed` ([commands](main-loop.md#commands--sim_dispatchcommand-00059df8)).

### What the retail missions give the tests

**Every retail mission gives the status and the report a squad with members, and a target wherever they read one.** Both read the player's group and its target with no null test: `Group_ConditionTier` divides by the member count, `Group_StatusLineIndex`'s verb 3 reads the first member's `+0x93`, `Group_OrderSubjectCondition` reads the target, and `Group_OrderSubjectArrivedAndClear` and `Group_OrderSubjectRouteExhausted` read a target object's group. The status reads the target for selectors 4 to 9 and for an order `+4` of 6, the report for verbs 2 and 4 to 9, and a target object's group is read for selectors and verbs 4, 5 and 7. `HercWorks.Query orders` over the 77 missions, each later record of the squad's GUID laid over the first ([repeated GUIDs](../formats/msn-mission-file.md#filtering-variants-and-repeats)), gives:

- every squad at least one member;
- every squad whose selector is 2 or 4 to 9, or whose order is 6, a target: a group of at least one member, a mech (`B0_C0_4A`, `B0_C3_1A`, `B0_C6_2A`, `B0_C6_3A`, `B0_C6_4A`, `SM_SAR01`) or a structure (`B0_C0_4B`, `B0_C0_6A`, `B0_C7_3A`, `B0_C7_6A`); in `B0_C6_6A` it comes from the two halves of a draw, one of which always holds; whether each such object is in a group is [Open](#open);
- no target to seven squads, each with selector 1 and order 8 or 11, which no test of the target reaches: `B0_C0_2A`, `B0_C1_00`, `B0_C2_60`, `B0_C4_20`, `SM_ESC50`, `SM_SCT01` and `SM_SCT50`.

`SM_SCR01`'s squad, selector 4, takes order 6 and its target, group 62's eight structures, from its second record ([groups](../formats/msn-mission-file.md#groups)). So selector 4's `Group_OrderSubjectArrivedAndClear` asks whether that group's action is clear, as it is from the load (the record names none), and whether every structure in it not destroyed is clear of threats; and the `+4` arm answers 6 or 4 once their condition reaches 3.

## The poll — `Mission_PollStatus` (`0000f454`)

(machine): ES2's ([the poll](../../retail/simulation/mission-objectives.md#the-poll--mission_pollstatus-004131ac)) on ES1's globals. It ticks `MissionAlertTimer` (`000af7a0`) and `MissionPollTimer` (`000af79c`) with `Math_CountdownTimerTick`, and only when the poll interval has run out asks `Mission_StatusForAlert`(0, machine). A status whose entry in `Mission_StatusWorthRaising` (`00091700`) is set (2, 3, 6, 7, 8, 9 and 18, ES2's) and that differs from `Mission_StatusRaised` (`000bc49a`) arms the alert delay when `MissionAlertArmed` (`000bc4b0`, an `int16`) is clear, setting it and the delay's counter `000af7a1` to 1 and 10000; the flag stays set until `Q`'s case clears it, so only the first status raised, and the first after each `Q`, waits out the delay, and is returned, and recorded as raised, when that counter is 0 or the status is 2. Otherwise the poll interval's counter `000af79d` is set to 10000 and it returns −1; a status returned leaves that counter at 0, so the next tick asks again, as ES2's does ([ES2's](../../retail/simulation/mission-objectives.md#the-poll--mission_pollstatus-004131ac)). `Ai_ChooseWeapon` (`00018e50`) also raises the counter to 1000 when it is below, as a machine runs dry ([weapons](mech-ai.md#weapons)). ES2's also evaluates every tick while the machine is outside the mission box. `es2_xref.py` finds one call, from `Sim_MainTick` (`00059700`), with `LocalPlayerMech`, whether the machine is destroyed or not ([the tick](main-loop.md#the-tick--sim_maintick-00059700)). [`DBSim_SpawnMissionObjects`](../formats/script-dat.md#the-second-pass) sets `Mission_StatusRaised` to `0xffff`.

## The mission box

**`Mission_IsOutsideBox` (`0000f6b8`)**, (margin, point): 0 when the point's X and Y lie strictly inside `Mission_Box` (`000af7a4`; least X, least Y, greatest X, greatest Y), widened by the margin on every side, otherwise 1, as ES2's (`0041373c`). **`SimObject_IsFarOutsideMissionBox` (`0000f704`)** asks it for the object's `+0x20` with margin 170000 and **`SimObject_IsOutsideMissionBox` (`0000f724`)** with 60000, where ES2's use 110000 and 0.

**The mission area always takes in the map's origin: in a mission whose points all lie on one side of X = 0 or of Y = 0, the area reaches back to that line, so toward it the player can drive past the mission's points to 60000 beyond the line before the boundary warning.** [`DBSim_LoadScriptDat`](../formats/script-dat.md#reading) widens the box to take in each of block 1's points, a signed compare and store per edge (`0001dc99`–`0001dcda`), and never sets it first, where ES2's starts it at `0x7fffffff` for the least X and Y and `0x80000000` for the greatest (`00424709`–`00424729`). The box lies past the image's file bytes, so it is {0, 0, 0, 0} at load. `es2_xref.py --decode` finds the load's four stores, `Mission_IsOutsideBox`'s four reads, and the Heads-Down Display's command screen constructor `0003ee58` ([the display object](heads-down-display.md#the-display-object--hdddisplay_ctor-0003c258)) copying the 16 bytes into its own `+0x160` with four `MOVSD` (`0003f0cc`) and then widening its copy by 60000 ([Open](#open)).

## Functions

| ES1 | Address | ES2 |
|---|---|---|
| `HeapPtr_DtorMissionStatusCopy`, `ConfigPart_DtorMissionStatusCopy`, `ConfigPart_GetClassTagMissionStatusCopy`, `TSPartBase_DtorMissionStatusCopy` | `0000eb68`, `0000eb90`, `0000ebcf`, `0000ebd8` | |
| `Group_OrderSubjectObject`, `Group_OrderSubjectGroup`, `Group_IsWipedOut`, `Group_ConditionTier` | `0000ec04`, `0000ec14`, `0000ec24`, `0000ecc0` | `00412bbc`, `00412bd0`, `00412be4`, `00412c8c` |
| `Group_OrderSubjectEngaged`, `Group_AnyMemberEngaged`, `Group_OrderSubjectCondition`, `Group_NoRivalOrderOnSubject` | `0000edd8`, `0000ee18`, `0000ee84`, `0000ef2c` | `00412d4c`, `00412d90`, `00412dc4`, `00412e74` |
| `Group_AnyMemberObjectiveSighted`, `Group_AnyMemberDataLinked`, `Group_AnyMemberScoredAKill`, `Group_StatusLineIndex` | `0000efac`, `0000f018`, `0000f084`, `0000f0f0` | `00412ef4`, `00412f28`, `00412f5c`, `00412f90` |
| `Mission_StatusForAlert`, `Mission_PollStatus`, `Mission_Status`, `Mission_IsOutsideBox` | `0000f43c`, `0000f454`, `0000f4e8`, `0000f6b8` | `00413180`, `004131ac`, `004135e8`, `0041373c` |
| `SimObject_IsFarOutsideMissionBox`, `SimObject_IsOutsideMissionBox`, `Mission_IsClearOfThreats` | `0000f704`, `0000f724`, `0000f744` | `00413780`, `0041379c`, `004137b4` |
| `Group_AllMembersClearOfThreats`, `Group_OrderSubjectRouteExhausted`, `Group_OrderSubjectArrivedAndClear` | `0000f8b4`, `0000f930`, `0000f99c` | `00413950`, `004139a0`, `00413a08` |

ES2's functions in that stretch with no counterpart in the file: `Group_AllMembersDisarmed`, `Group_OrderSubjectDestroyed`, `Mission_GroupOrderCompleteOnRoute`, `Mission_EvaluateObjectives` and `Group_IsWrittenOff`. ES1's `data\script.dat` carries no objective records ([`script-dat.md`](../formats/script-dat.md)).

## Globals

| | Address |
|---|---|
| `MissionPollTimer` and `MissionAlertTimer`, countdowns, their counters a byte later | `000af79c`, `000af7a0` |
| `Mission_Box`, four `int32` | `000af7a4` |
| `Mission_StatusRaised`, `int16` | `000bc49a` |
| `MissionAlertArmed`, `int16` | `000bc4b0` |
| `LocalPlayerMech`, the player's machine, which `Sim_MainTick` passes to `Mission_PollStatus` | `000b5b86` |
| `Mission_StatusWorthRaising`, 20 `int16` | `00091700` |
| `Mission_StatusBySelector`, 10 `int16` | `00091728` |

## Open

- **Open:** whether anything in ES1 posts `MISSION FAILED`, `MISSION SUCCESSFUL` or the boundary lines; this file posts nothing. A retail check: walk the HERC out past the mission area and note whether a computer line comes up with the boundary panel.
- **Open:** a writer of `MissionAlertArmed` (`000bc4b0`) besides the poll's store of 1 and `Q`'s store of 0. `es2_xref.py --decode` finds those two word stores and the poll's word compare, and no other instruction holding its address. It lies past the image's file bytes, so it is 0 at load, and after the 20 bytes of `ScriptDatHeader` (`000bc49c`), which the load reads with a length of 20 and passes to `World_LoadTheater`.
- **Open:** a writer of a machine's `+0xa0`. `es2_fieldscan.py --binary ES1DBSIM a0` finds one access through a machine, `Group_AnyMemberScoredAKill`'s read (`0000f0c0`, through `+0x8c`), and its writes in gauge, gunsight and block-copy code of other classes; the same scan of `9f` finds `Ai_ChooseWeapon`'s write through `+0x8c`. A new machine's fields the constructor does not write are 0 ([construction](mech.md#construction)), so with no writer `Group_StatusLineIndex`'s verb 1 never answers 0, its answer for a group at tier 0 whose route has run out and that has scored a kill, and a selector-1 mission never writes that outcome.
- **Open:** a writer of `Mission_Box` besides the load. Over `000af770`–`000af7c4`, `es2_xref.py --decode` finds no instruction holding an address but those in [the mission box](#the-mission-box), the poll's pushes of its two countdowns (3 bytes each, ending at `000af7a2`), `Flyer_RegisterSubsystem`'s word store at `000af794` and the load's push of `000af7b4`, the block after the box; a store through a base held in a register is not searched. And whether any retail mission's block-1 points all lie on one side of X = 0 or Y = 0, so that the box's reach back to the origin shows: a read of each mission's points through `HercWorks.Core`'s `.MSN` load.
- **Open:** whether each object a squad targets with selector 4, 5 or 7 is in a group, whose `+0x3f` `Group_OrderSubjectArrivedAndClear` and `Group_OrderSubjectRouteExhausted` read with no null test: the mechs of `B0_C0_4A`, `B0_C3_1A`, `B0_C6_2A` and `SM_SAR01`, and the structure of `B0_C0_4B`. `HercWorks.Query orders` counts each group's members without naming them.
- **Open:** what reaches `Group_NoRivalOrderOnSubject` and `Group_OrderSubjectEngaged`, to which `es2_xref.py` finds no branch or stored pointer. Its stored-pointer hits for `Group_OrderSubjectCondition` (`00035c20`, `0005447a`, `00054e02`), `Group_AnyMemberScoredAKill` (`00062502`) and `Group_OrderSubjectArrivedAndClear` (`000874e7`) were not read.
- **Open:** a positive control in the running game for the done checklist ([when a module is done](../../herculan/plan-es1-support.md#when-a-module-is-done)). Proposed: in a mission, `BP <CS>:0000f4e8` fires at a steady interval, 10000 counts of a countdown, about 3.9 seconds ([the tick length](fixed-point-math.md#the-tick-length--time_beginsimtick-00063f40)), and walking out of the mission area makes the boundary panel come up within one such interval of crossing a line 60000 units past the outermost mission point.
