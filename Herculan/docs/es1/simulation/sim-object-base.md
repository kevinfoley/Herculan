# DBSIM.EXE — the object base, the live object list and AI targets

**What every simulated object shares in ES1 is ES2's: the default answers a HERC, an aircraft or a structure gives when its class has nothing of its own, the one list of live objects the sensors and the AI walk, and the tests that decide what an AI machine knows of and may shoot at.** What a player would see differs in what an AI machine shoots at: the test of what it may target leaves out ES2's exclusions of an invulnerable structure and of an aircraft that is out of the fight, and the score that picks among targets weighs bearing and nearness with ES1's own numbers, with no cone or range limit, and while it favors the target its group's order names it can still settle on a disarmed machine the order does not name. Addresses are `ES1DBSIM` image addresses ([the X-32 executable](../formats/x32-executable.md#the-image)); ES2's are DBSIM's. Every function here removes its own arguments, so argument lists are in stack order ([calling conventions](../formats/compiler.md#calling-conventions)). The objects' fields stand 6 bytes below ES2's up to `+0xad` and a slot 4 bytes above ES2's ([the object fields](mission-groups.md#the-object-fields)).

## Where the object file starts and ends

The code is `0000d68c`–`0000e217`. It opens with `HeapPtr_DtorSimObjectCopy` (`0000d68c`), a lone copy of the 40-byte destructor that opens each run of `ConfigPart`'s copies ([module copies](../formats/compiler.md#vtables)), with no run after it, right after the zero fill that ends [`gunlist.cpp`](weapon-mounts.md#where-the-code-sits); `es2_xref.py` finds no reference to it ([Open](#open)). `Ai_SelectTarget`'s `RET 8` at `0000e212` is followed by three zero bytes and `HeapPtr_DtorDetectionCopy` (`0000e218`), which opens [the sensor model's file](sensor-model.md#where-the-object-file-starts-and-ends) ([where object files meet](../formats/compiler.md#where-object-files-meet-in-the-code)). The file has no static initializer, and no assert in it names a source path. Its functions are those ES2 has from `00411a3c` to `00411fa0`, in the same order, with ES1's pair cache among the object list's.

## The base class's own functions

These stand at the file's start and fill the slots the structure, HERC and aircraft classes leave to the base: the seven tables `000904a6`, `StructureVtable` (`00090522`), `StructureRadarVtable` (`0009059e`), `StructureArmedVtable` (`0009061a`), `00091a6a` (which `Mech_Constructor` stores), `00091f6e` and `00091fea` hold them, each table not every one. Each is ES2's at the slot 4 bytes lower ([ES2's table](../../retail/simulation/sim-object-layout.md#two-hierarchies-one-root)). `SimObject_GetAimPointZero`, `SimObject_BearingInSensorArc` and `Mech_ShareContact` are framed functions; the rest are frameless, with no Ghidra function.

| Slot | ES1 | Code | ES2 |
|---|---|---|---|
| `+0x18` | `SimObject_TickNoOp` (`0000d728`) | `MOV AX,1`, `RET 4` | `00411ab4` |
| `+0x28` | `SimObject_GetAimNodeTransform_None` (`0000d718`) | returns 0, `RET 4` | `00411a9c` ([aim point](../../retail/simulation/aim-point.md)) |
| `+0x2c` | `SimObject_ReceiveSquadOrder_Refuse` (`0000d6d8`) | returns 0, `RET 0xc` | `00411a64` |
| `+0x30` | `SimObject_HandleCommandNoOp` (`0000d6e0`) | returns 0, `RET 8` | `00411a6c` |
| `+0x34` | `SimObject_GetAimPointZero` (`0000d6e8`) | (object, a, b): clears the two 6-byte triples b and a with `memset` | `00411a74` |
| `+0x38` | `SimObject_GetShieldByHeadingZero` (`0000d6c0`) | returns 0, `RET 8` | `00411a4c` |
| `+0x3c` | `SimObject_GetSpeedZero` (`0000d6c8`) | returns 0, `RET 4` | `00411a54` |
| `+0x40` | `SimObject_GetTorsoTwistAngleZero` (`0000d6d0`) | returns 0, `RET 4` | `00411a5c` |
| `+0x48` | `SimObject_BearingInSensorArc` (`0000d738`) | [below](#the-sensor-arc) | `00411acc` |
| `+0x4c` | `SimObject_AiEnemySightedNoOp` (`0000e75c`, in the next file) | `RET 8` | `004127f8` |
| `+0x50` | `SimObject_CompareCombatRatingStub` (`0000d730`) | `MOV AX,1`, `RET 8` | `00411ac0` ([relative combat rating](../../retail/simulation/ai-targeting.md#relative-combat-rating)) |
| `+0x54` | `Mech_ShareContact` (`0000d760`) | (object, unread, contact): `Detection_ShareContact`(object, contact) | `00411aec` |
| `+0x58` | `SimObject_ComponentNearestAim_None` (`0000d6b8`) | `MOV AX,0xffff`, `RET 8` | `00411a44` |
| `+0x5c` | `SimObject_ComponentPositionNoOp` (`0000d6b4`) | `RET 0xc` | `00411a3c` |
| `+0x60` | `SimObject_GetBodyRadiusZero` (`0000d720`) | returns 0, `RET 4` | `00411aa4` |
| `+0x64` | `SimObject_CreditNeutralisedTargetNoOp` (`0000d774`) | `RET 0xc`; in the aircraft's table too, so an aircraft is credited with nothing for what it destroys | `00411b2c` |
| `+0x68` | `SimObject_AiOnLineOfFireBlockedNoOp` (`0000d778`) | `RET 8` | `00411b34` |

ES1's tables end at slot `+0x70`, where ES2's base table runs to `+0x84`; the base slots this file does not fill are [Open](#open).

### The sensor arc

**`SimObject_BearingInSensorArc` (`0000d738`)**, (object, bearing), answers whether a bearing lies within ±`0x3800` of the object's heading (`int16` `+0xa`), ±78.75°. It sign-extends both to 32 bits and tests (bearing − heading + `0x3800`) < `0x7000` unsigned. ES2's (`00411acc`) does the same sum in 16 bits, where it wraps; ES1's does not, so a bearing and a heading within `0x3800` of each other but on the two sides of ±`0x8000`, the heading opposite 0, fail the test.

## The out-of-action report — `Mech_ReportOutOfAction` (`0000d77c`)

(object): ES2's ([the out-of-action report](../../retail/simulation/mission-deployment.md#the-out-of-action-report)) with one counter pair where ES2's has ten. With a group at `+0x3f` it runs `Group_ReportIfAllOutOfAction` (`0001d524`)(group, object) ([going out of the fight](mission-groups.md#going-out-of-the-fight)); then, when the flag number `int16` `+0x174` is not negative, it finds the operation `int16` `+0x176` by `REPNE SCASD` among the six dwords at `00091640` (2, 1, `0xd`, `0xe`, `0xf`, `0x10`) and jumps through the six targets after them: 2 adds 1 to that flag of `MissionVars` (`000bc4b8`), 1 sets it to 0, `0xd` to `0x10` store the operation less `0xc`; any other operation writes nothing. ES2's writes the mission counters. `es2_xref.py` finds four calls, from `Base_ApplyDamage` (`00003978`, [a structure's damage](structures.md#damage--base_applydamage-00003978)), `Mech_ComponentDamageWrite` (`00013250`, twice: [the damage write](mech.md#the-damage-write--mech_componentdamagewrite-00013250)) and `Flyer_ComponentDamageWrite` (`0001b290`).

## The live object list

`LiveObjectList` (`000bc416`) is a `0x10`-byte list: the array of object pointers at `+0`, the capacity `int16` `+4`, the count `int16` `+6` (`LiveObjectList_Count`, `000bc41c`) and at `+8` the [pair cache](#the-pair-cache). An object's slot in the array is its `int16` `+0x45`, ES2's `+0x4b`, and indexes its two per-object byte tables, the contacts at `+0xbc` and the line-of-sight cache at `+0x10c` ([the sensor model](sensor-model.md)). They are `0x50` apart and `+0x15c` follows the second by `0x50`, so each has 80 rows, where ES2's have 112 ([ES2's tables](../../retail/simulation/sim-object-layout.md#the-two-per-object-tables-are-112-rows-each)).

- **`ObjectList_Init` (`0000db40`)**, (list, capacity): `Mem_New`(capacity × 4) at `+0`, the capacity at `+4`, then `ObjectPairCache_Init`(list + 8, capacity). ES2's (`00411df8`) has no pair cache. `DBSim_LoadScriptDat` calls it with `LiveObjectList` and the sum of three of its counts.
- **`ObjectList_Add` (`0000db10`)**, (list, object): stores the object at the count's place, gives the object that slot at `+0x45` and raises the count, as ES2's (`00411dd4`). `es2_xref.py` finds five calls: three from `Base_Construct` (`00003cbc`) ([construction](structures.md#construction--base_construct-00003cbc)), one from `Mech_Constructor` (`00011330`) and one from `Flyer_Constructor` (`0001ac58`).
- **`ObjList_SwapRemove` (`0000db70`)**, (list, object), and **`ObjectList_Prev` (`0000dbd0`)**, (list, object), are ES2's (`00411e1c`, `00411e60`): the last entry moved into the object's slot and the count lowered, without rewriting the moved object's `+0x45`; and the entry before the object, wrapping. `es2_xref.py` finds no reference to either ([Open](#open)). `ObjectList_Next` (`0000dba4`) is ES2's too ([cockpit target selection](cockpit-panel.md#the-target-selection)).

**Every HERC, flyer and structure is built marked as an object the Heads-Down Display's command screen may step to.** The mark is the byte `+0x92`: `Mech_Constructor`, `Flyer_Constructor` and the structure builder `Base_Construct` (`00003cbc`) store 1 there ([construction](mech.md#construction), [the flyer's](flyers.md#construction--flyer_constructor-0001ac58)), as ES2's constructors set `+0x98`; `es2_fieldscan.py --binary ES1DBSIM 92` finds those five stores of 1 (three in `00003cbc`), writes into other structures and block copies, and one read through an object, in **`HddCommandScreen_IsEligibleUnit` (`00041b1c`)**, (screen, side, object), ES2's (`0044f014`, [Tab's step](../../retail/simulation/heads-down-display.md#the-order-list-and-its-state-machine)) on ES1's fields. It answers 1 when the object's group's (`+0x3f`) side byte `+0x14` is the side asked for, its destroyed byte `+0x93` is clear, `HddCommandScreen_IsOnMap` (`00041bb8`) ([Tab's step](hdd-command-screen.md#tabs-step)) answers non-zero for the screen and the object's position `+0x20`, `+0x92` is set, and, for a side other than 0, `Ai_KnowsObject` answers non-zero for the cockpit's machine (`CockpitViewInstance` `+0x1ff`) and the object; otherwise 0. `es2_xref.py` finds its one caller, `HddCommandScreen_CycleUnit` (`000419f4`), which walks this list from the screen's `+0x15c` ([Tab's step](hdd-command-screen.md#tabs-step)). The constructors' `+0xa1` is the scanner's ([the contacts](mfd-scanner.md#the-contacts)).

## The pair cache

ES1's list carries a table of one `0x18`-byte record per unordered pair of slots, ES2's list has none:

- **`ObjectPairCache_Init` (`0000dbfc`)**, (table, count): stores the count at `+0`; takes the `int16` already at `+2` as *n*, stores *n*(*n* − 1) >> 1 there, and allocates that many records at `+4`. It reads *n* before it writes it, so the count it was passed sizes nothing. For `LiveObjectList` the word is `000bc420`, the list's `+0xa`, in the zero-filled memory past the image's file bytes (`0xadcd8`): unless something writes it before the first call, that call allocates no records and leaves 0 there ([Open](#open)).
- **`ObjectPairCache_Lookup` (`0000dc3c`)**, (table, a, b): with *lo* the smaller of a and b and *hi* the larger, the record at index ((*lo* × (2 × count − *lo* − 3) − 2) >> 1) + *hi*, the upper triangle in row order.
- **`ObjectPairCache_Invalidate` (`0000dc8c`)**, (table): clears the 16 bytes from `+8` of each of the `+2` records.
- **`ObjectPairCache_GetHeading` (`0000da68`)**, (a, record, b): when the record's byte `+8` is clear, stores `Math_HeadingToward` from a's `+0x20` to b's at `int16` `+0` and sets `+8`; returns `+0`.
- **`ObjectPairCache_GetDistance` (`0000dabc`)**, (a, record, b): when `+0xa` is clear, stores `Math_DistanceBetweenPoints`(a `+0x20`, b `+0x20`) at `+4` and sets `+0xa`; returns its low 16 bits.
- **`ObjectPairCache_GetFlag` (`0000da04`)**, (a, record, b): when `+0xb` is clear, stores at `+0xc` whether `ObjectPairCache_TestStub` (`0000d9fc`)(a, b `+0x20`) answers non-zero and sets `+0xb`; returns `+0xc`. The test is `MOV AX,1`, `RET 8`, so the flag is always 1.

`es2_xref.py` finds no reference to the lookup, the invalidation or the three getters ([Open](#open)).

## What an AI machine knows and may target

- **`Ai_KnowsObject` (`0000d808`)**, (machine, object): ES2's ([is it a target at all](../../retail/simulation/ai-targeting.md#is-it-a-target-at-all--ai_istargetable-00411e80)): 1 when the object's byte `+0x8f`, radar-visible, is set and `Math_DistanceBetweenPoints`(object `+0x20`, machine `+0x20`) is below 1000000, or when the machine's contact byte at `+0xbc` + the object's `int16` slot `+0x45` is set ([the fields](sensor-model.md#the-fields)); otherwise 0. `es2_xref.py` finds three calls, from `Ai_IsTargetable`, `Mission_IsClearOfThreats` (`0000f744`, [clear of threats](mission-status.md#clear-of-threats)) and `HddCommandScreen_IsEligibleUnit` (`00041b1c`, [below](#the-live-object-list)).
- **`Ai_TargetStateTier` (`0000d880`)**, (object): ES2's (`00411cb4`) on the behaviour descriptor at `+0x47`: 2 when its byte `+0xd` is set, 1 when `+0xc` is, otherwise 0, and 0 with no descriptor. `es2_xref.py` finds four calls, from `Ai_ShouldAbandonTarget` (`000162b4`, [targets and orders](mech-ai.md#targets-and-orders)) and three times from `Mech_ReceiveSquadOrder` (`0001a1cc`, [receiving a squad order](ai-squadmates.md#receiving-a-squad-order--mech_receivesquadorder-0001a1cc)).
- **`Ai_IsTargetable` (`0000dce8`)**, (machine, mask, candidate): 1 when the candidate's group's side byte `+0x14` differs from the machine's group's, the candidate is neither destroyed (`+0x93`) nor collapsed (`+0xae`), `Ai_KnowsObject` answers 1, and, with mask bit `0x10`, its class `int16` `+0x162` differs from the machine's; and then 0 in two cases: the leader of the machine's group (the first of the member array `+0xe`) is the player's (`+0x9d`), script.dat header word 3 (`000bc4a2`) is 3, `Group_OrderTargetObject` of the group is the candidate and the leader's byte `+0x9a` is clear; or the machine's `+0x94` is set and the candidate is the player's selection, `CockpitViewInstance` `+0x20c`. `+0x9a` is the latch the player's think sets when the data link's last line is posted, and `+0x94` the latch squad verb 1 sets so that a squadmate leaves the player's selection alone ([the fields](mech-ai.md#the-fields)): a squad the player leads under header word 3 does not take the order's object until the data link is done. ES2's (`00411e80`) also rejects an invulnerable candidate and an aircraft that is out of the fight, and its order-target case has no `+0x9a` term. `es2_xref.py` finds two calls, from `Ai_SelectTarget` and `Mech_BehaviourGuardThink` (`00017ed4`, [navigation](mech-ai.md#navigation)).

`Mech_AiTick` (`0000d8e8`) and `Formation_RotateAndAddOffset` (`0000d9a0`) stand between the AI's tests and the object list, as in ES2 ([the tick](mission-groups.md#the-tick), [formation offsets](../formats/formations-dat.md)).

### Acquisition — `Ai_SelectTarget` (`0000ddf4`)

(machine, mask): ES2's acquisition ([acquisition](../../retail/simulation/ai-targeting.md#acquisition--ai_selecttarget-00411fa0)), the mask bits `1`, `2`, `4`, `0x10` and `0x20` meaning what ES2's do, with no cone limit argument. It walks `LiveObjectList`'s array to its count or to a null entry and returns the best candidate, or 0. The tier bar starts at 0, and for each candidate:

1. `Ai_IsTargetable`(machine, mask, candidate) must answer non-zero.
2. It is *designated* when `Group_IsOrderTarget` (`0001ce3c`)(group, candidate) answers non-zero and the group's word `+4` is 1, `search destroy` ([choosing a state](mech-ai.md#choosing-a-state--mech_aiselectbehaviour-0001892c)). *d* is 1 when it is designated or mask bit 0 is clear, otherwise 0.
3. Its tier is 2*d* + 1, less 1 when it is disarmed (`+0x9f`), immobilized (`+0x9e`) or destroyed (`+0x93`).
4. It is passed over when it is immobilized, unless it is designated and the machine's group's side byte `+0x14` is not 0, the Cybrid side, as ES2's; and when its tier is below the bar.
5. It is scored and compared:

```
e     = |Math_HeadingToward(machine+0x20, candidate+0x20) - machine+0xa|   // int16, |-0x8000| taken as 0x7fff; 0 with mask 0x20
b     = 0x1000 - (e >> 3)
score = Q10(100, b)
r     = Math_DistanceBetweenPoints(candidate+0x20, machine+0x20)
if (r < 25000) score = max(score, b / max(r >> 11, 1))
k     = machine's slot +0x50 (machine, candidate)
if (!(mask & 2) && candidate+0x15e == machine)                score = Q10(score, W_shooting[k])
if (candidate+0x47 != 0 && candidate's descriptor +9 == 0)    score = Q10(score, W_idle[k])
if (candidate+0x15e != 0 && candidate+0x15e != machine)       score = Q10(score, W_taken[k])
score = Q10(score, W_class[candidate+0x162])
h     = candidate+0x15c - (machine+0x15e == candidate)
if (!(mask & 4) && h != 0) score = score / (h + 1)
if (score > bestScore || tier > bar) { best = candidate; bestScore = score; bar = tier }
```

`bestScore` starts at 0, so a candidate scoring 0 is taken only on a higher tier. Every Q10 is the rounding `Math_Q10Multiply` ([the primitives](fixed-point-math.md#the-primitives)), its result cut to an `int16`; the two divisions are signed and truncate. The slot `+0x50` call runs for every candidate that reaches step 5, before any weight it indexes is tested: a HERC's is `Mech_CompareCombatRating` (`00016968`, [the combat reassess](mech-ai.md#the-combat-reassess--mech_aicombatreassess-00016e04)), which draws two numbers from the generator for a candidate of class 0, so a HERC's walk advances the generator by two for each HERC it scores. An aircraft's is the base's `SimObject_CompareCombatRatingStub` ([the base's slots](#the-base-classs-own-functions)), so a flyer's sweep ([the flyers' thinks](flyers.md#the-thinks)) takes every weight at *k* = 1 and draws nothing.

| Table | Values (Q10) | Indexed by |
|---|---|---|
| `W_shooting` `00091620` | 1500, 2000, 2500 | *k* |
| `W_idle` `00091628` | 600, 400, 200 | *k* |
| `W_taken` `00091630` | 700, 850, 1000 | *k* |
| `W_class` `00091638` | 2000, 500, 200, 0 | the class `+0x162` |

ES2's (`00411fa0`) differs in seven places:

- its tier bar starts at 1, or 0 under a group order verb of 3, so with mask bit 0 a disarmed candidate the group does not designate, tier 0, is passed over, where ES1's can take it while it holds nothing better;
- it rejects a candidate past a cone limit, and one farther than 1000000 when designated or 100000 otherwise;
- its designated test is a group order verb of 0;
- its bearing score is Q10(70, *b*), raised inside 60000 to *b* / max(*r* >> 12, 1);
- it applies `W_idle` only to a candidate that is not locally piloted;
- a class-1 candidate shooting at the machine takes `W_class`'s class-0 weight, and `W_class` holds 1500, 700, 500 and 500;
- its Q10 multiplies truncate.

## Functions

| ES1 | Address | ES2 |
|---|---|---|
| `HeapPtr_DtorSimObjectCopy` | `0000d68c` | the code of `HeapPtr_DtorGroupsCopy` (`0001c824`) |
| the base class's functions | `0000d6b4`–`0000d778` | [above](#the-base-classs-own-functions) |
| `Mech_ReportOutOfAction`, `Ai_KnowsObject`, `Ai_TargetStateTier`, `Mech_AiTick`, `Formation_RotateAndAddOffset` | `0000d77c`, `0000d808`, `0000d880`, `0000d8e8`, `0000d9a0` | `00411bc8`, `00411c58`, `00411cb4`, `00411cec`, `00411d64` |
| `ObjectPairCache_TestStub`, `ObjectPairCache_GetFlag`, `ObjectPairCache_GetHeading`, `ObjectPairCache_GetDistance` | `0000d9fc`, `0000da04`, `0000da68`, `0000dabc` | — |
| `ObjectList_Add`, `ObjectList_Init`, `ObjList_SwapRemove`, `ObjectList_Next`, `ObjectList_Prev` | `0000db10`, `0000db40`, `0000db70`, `0000dba4`, `0000dbd0` | `00411dd4`, `00411df8`, `00411e1c`, `00411e40`, `00411e60` |
| `ObjectPairCache_Init`, `ObjectPairCache_Lookup`, `ObjectPairCache_Invalidate` | `0000dbfc`, `0000dc3c`, `0000dc8c` | — |
| `Ai_IsTargetable`, `Ai_SelectTarget` | `0000dce8`, `0000ddf4` | `00411e80`, `00411fa0` |

## Globals

| | Address |
|---|---|
| `LiveObjectList`, the list's array pointer at its `+0`, and `LiveObjectList_Count` | `000bc416`, `000bc41c` |
| `Ai_SelectTarget`'s four weight tables, three `int16` and a pad each, the last four `int16` | `00091620`–`0009163f` |
| `Mech_ReportOutOfAction`'s operations and targets | `00091640` |
| script.dat header word 3 | `000bc4a2` |

## Open

- **Open:** what calls `HeapPtr_DtorSimObjectCopy`, to which `es2_xref.py` finds no branch or stored pointer.
- **Open:** the base class's slots this file does not fill, `+4`–`+0x14`, `+0x1c`–`+0x24`, `+0x44` and `+0x6c`–`+0x70`. In `00091a6a`, the HERC's table and the aircraft's (`00091fea`, whose `SimObject_SetRunIntoFlyerCopy` (`0001af1c`) and `SimObject_MissileLockState_AlwaysFlyerCopy` (`0001af32`) are the same code), `+0x6c` is ES2's `+0x68`, run into, and `+0x70` ES2's `+0x6c`, the missile lock, filled by each file's own copies ([the HERC's table](mech.md#the-hercs-table)).
- **Open:** what reaches `ObjList_SwapRemove`, `ObjectList_Prev`, `ObjectPairCache_Lookup`, `ObjectPairCache_Invalidate` and the three getters, to which `es2_xref.py` finds no branch or stored pointer; and whether anything writes `000bc420` before `ObjectList_Init` runs: a search of the disassembly for that address as an operand finds no such write, and a write through the list's or the cache's base was not searched for.
- **Open:** a positive control in the running game for the done checklist ([when a module is done](../../herculan/plan-es1-support.md#when-a-module-is-done)). Proposed: `BP <CS>:0000db10` fires once for each HERC, aircraft and structure while a mission loads, before the first frame is drawn.
