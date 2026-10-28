# DBSIM.EXE — world.cpp, the mission's handoff and its results

**At the end of a mission ES1's simulator writes what happened for the shell in two files, as ES2's does: `data\results.dat`, with the outcome, the salvage and the state of every machine in the player's squad, and `data\mission.var`, the campaign flags.** `world.cpp` holds that writer, the two passes that read the shell's files and build the mission's objects when it starts ([`script-dat.md`](../formats/script-dat.md#reading)), and what the mission's objects ask of the world while it runs: whether a shot strikes something, who a blast reaches, which actions their trigger areas set off, how difficulty scales damage, and the collision volume of the zone's shapes. Addresses are `ES1DBSIM` image addresses ([the X-32 executable](../formats/x32-executable.md#the-image)); ES2's are DBSIM's, whose `world.cpp` asserts in the same two functions, `Mission_WriteResults` and `DBSim_LoadScriptDat`.

## Where the module starts

`world.cpp`'s code opens at `0001d5fc` and its initialised data at `0009225e`. Both open with the copy of `ConfigPart`'s destructors and vtables that starts each object file which carries one ([module copies](../formats/compiler.md#vtables)): the four functions `0001d5fc`–`0001d697` and the tables `0009225e` and `00092292`. `Mission_WriteResults` (`0001d698`), the first function to assert `world.cpp`, follows the run; in the data the four `int16` 2800, 2000, 1400 and 1000 at `000922c4` follow the tables, and then the strings from `000922cc` (`data\results.dat`, the source path, `results.dat`, …).

The object file before it, `0001c824`–`0001d5fb` with its data at `00092142`–`0009225d`, opens with its own run and tables, and its data holds its switches' jump tables and no source path, so the asserts give it no name. It holds the mission groups, actions and trigger areas ([`mission-groups.md`](mission-groups.md)): none of its functions belongs to `flyersys.cpp` or to `world.cpp`. `flyersys.cpp` ends at its static initialiser, `Flyer_RegisterSubsystem` (`0001c814`), and its data with the string `flyers` at `00092139`; its functions in this stretch, `FlyerTypes_LoadAll` (`0001c770`) and `FlyerFormation_GetSlotOffset` (`0001c7f0`), are in [`nam-name-list.md`](../formats/nam-name-list.md) and [`formations-dat.md`](../formats/formations-dat.md).

The next run, at `0002019c`, opens `terrain.cpp` ([`terrain.md`](terrain.md#where-the-module-starts-and-ends)): `Terrain_LoadZoneShapes` (`00020238`) follows it ([the simulator's load](../formats/zone-terrain.md#the-simulators-load)). So `world.cpp` is `0001d5fc`–`0002019b`.

ES2's group functions stand in the same place, `Route_AdvanceCursor` (`0042313c`) to `Group_ReportIfAllOutOfAction` (`00423f30`) between `FlyerSys_RegisterSubsystem` (`00423068`) and `Mission_WriteResults` (`0042412c`); which of ES2's modules holds them is [Open](#open).

The module's functions run in ES2's order, from `Mission_WriteResults` to the `GridShape` class ([functions](#functions)), ES2's from `0042412c` to `0042786c`. ES2's has `Mission_ResolveRefByKind` where ES1's has the points' element constructor, and adds the action timers' tick before the trigger test and, between the trigger test and `GridShape`, its static initialiser and destructor, four class destructors, an operator delete and the action timers' element constructor. `Sim_RaycastObjectList`, the shot sweep, is one of ES1's `world.cpp` functions, within its `ConfigPart` runs.

## Writing the results — `Mission_WriteResults` (`0001d698`)

ES2's writer ([what the mission leaves the shell](../../retail/simulation/mission-objectives.md#what-the-mission-leaves-the-shell--mission_writeresults-0042412c)), given the player's machine. `es2_xref.py` finds one call, from `Sim_Shutdown` (`0005b568`, [the end](main-loop.md#the-end--sim_shutdown-0005b568)), which also calls `GndTex_Free` ([the ground texture](../rendering/ground-and-sky.md#loading-the-ground-texture)). With *g* the player's group (`+0x3f`) and *w* header word 3 of `data\script.dat` (the dword at `000bc4a2`):

1. It asks `Group_StatusLineIndex` (`0000f0f0`)(*g*, *w*) for the outcome, the squad's report line for verb *w* ([the group report](mission-status.md#the-group-report--group_statuslineindex-0000f0f0)).
2. It opens `data\results.dat` through `Path_ApplyAltDrive` and `FileWStream_Ctor`, asserting at severity 5 (`world.cpp` line `0x58`) when it cannot, and writes:

| Bytes | Field |
|---|---|
| 2 | the outcome, as `Group_StatusLineIndex` returned it |
| 4 | `Math_Q10Multiply`(`Mission_TotalSalvage`(*g*, *w*), 2500) + flag 7 × 25000 |
| 2 | the number of salvage pairs, the `int16` at `000bc4b2` |
| 4 × that | each pair at `000bc4b4`, two `int16` |
| 72 per machine | `Group_WriteStatusBlocks` (*g*): for each member of the player's group in order, 33 conditions and 6 bytes of kill tallies ([the status blocks](mission-groups.md#what-the-results-read)) |

3. It closes the file, opens `data\mission.var` (assert at line `0x6d`) and writes the 100 flags at `000bc4b8`, 200 bytes, back over it.

A results file is therefore 8 + 4 × pairs + 72 × squad machines bytes.

Where ES2's differs:

| | ES1 | ES2 |
|---|---|---|
| the outcome | `Group_StatusLineIndex`'s answer for the player's group and header word 3, written as it is | 1 when its objectives' evaluation for the player answers 9, otherwise 0 |
| the salvage bonus | 25,000 per unit of flag 7 (`000bc4c6`), which `DBSim_LoadScriptDat` zeroes at the load | per unit of counter 20 |
| `mission.var` | the 100 flags, 200 bytes | 1,000 counters, 2,000 bytes |

`Mission_TotalSalvage` (`0001d434`) ignores *w*, as ES2's ignores its second argument ([what the results read](mission-groups.md#what-the-results-read)).

## Building the objects

`DBSim_LoadScriptDat` (`0001d850`) is the load's first pass over `data\script.dat` and `DBSim_SpawnMissionObjects` (`0001e638`) its second, which builds every mech, flyer, base and group the first pass marked live; both are in [`script-dat.md`](../formats/script-dat.md#reading).

## Shots against objects — `Sim_RaycastObjectList` (`0001f1d0`)

**A shot in ES1 stops at the flat ground, at a zone shape or at the nearest object it strikes, as one in ES2 stops at the terrain or an object ([the sweep](../../retail/simulation/hit-detection.md#the-sweep--sim_raycastobjectlist-00426528)); a shot that strikes an enemy sets off that enemy's engaged action whether or not it was the shooter's target.** It takes the shot record, ES2's: at `+0` the ray record (its frame pointer at `+0`, its length at `+4`, the world-to-ray transform it caches at `+0xa`), the two damage figures at `+4` and `+6`, at `+0xa` a pointer to the impact effects and at `+0xe` the owner. `es2_xref.py` finds three calls, from `Rocket_TickUpdate` (`00007198`), `Bullet_TickUpdate` (`00007f84`) and `Bullet_FireBurst` (`000088cc`), the missile's, the gun round's and the beam's ([the missiles](rockets.md#flight--rocket_tickupdate-00007198), [the projectiles](projectiles.md)). In order:

1. It copies the frame's 32 bytes to the ray record's `+0xa`, transposes the rotation and stores the negated translation rotated by it, as ES2's does.
2. **The ground.** The ray's end is `Transform_ApplyToPoint` of (0, length, 0) by the frame, taken with the length the shot came with, before anything cuts the ray. When its Z is negative the ray is cut where it crosses Z 0. With *z* the start's Z (frame `+0x1c`) and *d* the negated `Math_ElevationAngle` (`00063cf0`) from the start (frame `+0x14`) to the end, the length becomes 0 when *z* is negative; otherwise, when *d* is not 0, it becomes (*z* << 14) / `Math_CosBam`(*d* − `0x4000`), *z* / sin *d*, a 32-bit shift and a signed 32-bit divide (`CDQ`, `IDIV` at `0001f324`); when *d* is 0 the length stays as it was. `Math_CosBam` rounds its angle to a step of 16 and answers 0 at the quarter turn, so a *d* of 1 to 7, a ray that dips below the ground at under 0.04°, divides by zero. Any of the three counts as a strike but not as an object struck. ES2 asks its heightmap instead.
3. **The zone's shapes.** `RaycastShapeMinDistance_Clear` (`00020880`), then `Zone_RaycastShapeObjects` (`00020b50`)(ray, result): a hit cuts the length to the result's distance, counts as a strike and makes the struck shape the blocker for step 6. `RaycastShapeMinDistance_Get` (`0002088c`) goes to `000afa1c`, where it is 0 unless something reaches it through a pointer ([a ray against the shapes](terrain.md#a-ray--zone_raycastshapeobjects-00020b50)), and the distance is cleared again. ES2's calls `Sim_RaycastTerrain` between the same two.
4. **The objects.** It walks the live object list (`LiveObjectList`, `000bc416`, count `000bc41c`), skipping the owner and any object whose group's `+0x16` is set, the group's action ([the group record](mission-groups.md#the-group-record)), as ES2's skips a group that has not deployed. There is no second excluded object. The object's slot `+0x24`, its hit test, takes (object, shot) and answers the hit distance *r*, 0 for a miss. On a hit:
   - When the object's group's side byte (`+0x14`) differs from the owner's group's, the owner's `+0x98` is set to 1, the object's `+0x16c` action goes to `Action_Activate` (`0001cc5c`) unless the object's `+0x9c` is set, and the blocker is cleared. Otherwise the object becomes the blocker. ES2's does the first only when the object is the owner's own target, and makes any other object the blocker.
   - Unless the object is destroyed (`+0x93`), or on the other side and immobilized (`+0x9e`) or disarmed (`+0x9f`), it calls the object's slot `+0x54`, (object, the shot's dword `+4`, owner), ES2's `+0x50`. Before that, when the owner is the locally piloted machine (`+0x9d`) and the object is on its side but in another group, `Group_NearestLiveMember` (`0001cea4`)(owner's group, owner) that is within 30000 of the owner is passed to `Mech_AiFriendlyFireComplaint` (`00019300`, [taking fire](mech-ai.md#taking-fire--mech_aiontakingfire-00019338); [radio callouts](../../retail/simulation/ai-targeting.md#radio-callouts)).
   - On every hit, those three included, the length becomes *r* and the hit counts as a strike with an object struck: a destroyed object, or an immobilized or disarmed enemy, still stops the shot there and takes no impact effect from step 5; only the slot `+0x54` call is skipped. The side test and the blocker above apply to it too. The walk goes on while *r* is 500 or more.

   The owner's group is read with no test for a null owner. After the walk `RaycastShapeMinDistance_Get` goes to `000afa18`.
5. **The impact.** After a strike that struck no object, an effect at the ray's end, its Z raised to 0 when negative: `Explosion_Construct` (`00005af0`) with a null effect, so one from the pool, that point and the `int16` at `+8` + 2 × (`Math_RandomNext`(`000b5bd4`) & 3) of the effects `+0xa` names. ES2's raises no Z.
6. **The blocked line of fire.** With a blocker and the owner's `+0x15e` set, where ES2's reads its target at `+0x1a4`: when `Math_DistanceBetweenPoints2D` from the ray's end to the owner is less than `Math_DistanceBetweenPoints` from that object to the owner, and the bearing from the owner to the ray's end less the owner's heading (`+0xa`), minus the bearing to that object less the heading, is below `0x2000` as a signed `int16`, it calls the owner's slot `+0x68` with the blocker's `+6`. The difference has no absolute value, so every negative one passes.

It returns the length + 1 after a strike, otherwise 0.

## The difficulty scale — `Damage_ScaleByDifficulty` (`0001f82c`)

**In ES1 difficulty scales the damage the human side's shots do, by one table, where in ES2 it scales both sides' in opposite directions ([what the difficulty changes](../../retail/simulation/difficulty.md#what-the-difficulty-changes)).** (figure), callee-cleaned: the `int16` figure becomes `Math_Q10Multiply`(figure, `DamageScaleByDifficulty`[*i*]), the four `int16` at `000922c4`, 2800, 2000, 1400 and 1000 (2.73× down to 0.98×). *i* is header word 7 (`000bc4aa`), ES2's difficulty word, or 3 when the word at header offset 4 (`000bc4a0`) is not 0, where the shell writes the load's mode ([the header](../formats/script-dat.md#the-header)). There is one table and no side.

`es2_xref.py` finds six calls, two in each of `00003a58`, `00013b44` and `0001b370`: slot `+0x24`, the hit test, of the three base vtables (`00090522`, `0009059e`, `0009061a`), of the mech's (`00091ae6`) and of the flyer's (`00091fea`). The mech's, `Mech_DirectFireHitTest` ([the hit test](mech.md#the-hit-test--mech_directfirehittest-00013b44)), after its coarse reject, copies the shot's `+6` and `+4` into locals and scales both copies when the owner's group's side byte (`+0x14`) is 0. The base's (`00003a8b`) and the flyer's (`0001b3df`) make their two calls behind a test that a byte `+0x14` is 0 ([Open](#open)). ES2's sweep scales the shot record itself, for either side ([the damage scale](../../retail/simulation/difficulty.md#the-damage-scale-reaches-all-direct-fire-not-just-plasma)).

## Blasts — `Damage_ExplosiveBlastSweep` (`0001f75c`)

**An ES1 blast reaches mechs alone, measured centre to centre, and spares none of them that is in range, the one at its centre included, unless the mech's group still holds its action.** (damage, radius, point) in stack order, callee-cleaned: ES2's ([the sweep](../../retail/simulation/damage-system.md#the-sweep--damage_explosiveblastsweep-00426a20)) without the attacker and the excluded object. It walks the mech pool (`000af68c`) oldest first with `Pool_Prev` ([object pools](memory.md#object-pools)), skips a mech whose group's `+0x16` is set, and for each whose `Math_DistanceBetweenPoints` from the point is less than the radius, taken as an `int16`, calls `Mech_ApplyExplosiveDamage` (`00013a7c`, [a blast](mech.md#a-blast--mech_applyexplosivedamage-00013a7c))(mech, 0, radius, point, damage) in stack order, which no shield takes, where ES2's calls the object's slot `+0x70` and subtracts its body radius first. It returns 1 when any mech was in range. `es2_xref.py` finds one call, from `Mech_BehaviourRamTick` (`00018278`, [ramming](mech-ai.md#ramming)), with 2000, 3000 and that mech's own `+0x20`, the figures of ES2's ramming detonation.

## Trigger areas — `Actions_EvaluateTriggers` (`0001f87c`)

ES2's ([trigger areas](../../retail/simulation/mission-deployment.md#trigger-areas--actions_evaluatetriggers-00426b70)) for action types 0 to 5, over the 16-byte actions (`MissionActions`, `000bc496`, count `000bc494`) and the `0x28`-byte groups (`MissionGroups`, `000bc476`, count `000bc47a`): type 0 offers the argument's position (`+0x20`), type 1 each member of its group (`+0x3f`, read with no null test) through `Action_TestTriggerForGroup` (`0001cd30`), type 2 each group whose side byte `+0x14` is 0 and type 3 each whose side byte is 1, type 4 each group whose kind `+0` is 0 (mechs) and type 5 each whose kind is 1 (flyers), those four skipping a group whose `+0x16` is set; the sweep stops at the first group that answers, and a non-zero answer goes to `Action_Activate`. An action already activated answers 1 through `Action_TestTrigger`, so `Action_Activate` is called for it on every tick and does nothing. Types 6 and up have no case, so this function offers them nothing. `es2_xref.py` finds one call, from `Sim_MainTick` (`00059700`), with `LocalPlayerMech`, as ES2's, but whether or not the simulation is frozen ([the tick](main-loop.md#the-tick--sim_maintick-00059700)).

## Small functions

- **`Salvage_QueueWeapon` (`0001f7e8`)**, (pair), callee-cleaned: stores the dword the argument points at, two `int16`, in `Salvage_Pairs` (`000bc4b4`) at index `Salvage_PairCount` (`000bc4b2`) and adds 1 to the count, with no bound, as ES2's ([the salvage pairs](../../retail/simulation/mission-objectives.md#what-the-mission-leaves-the-shell--mission_writeresults-0042412c)). `DBSim_LoadScriptDat` zeroes the count and allocates the 200 bytes, 50 pairs (`0001e5d4`). `es2_xref.py` finds two calls, from `Mech_SalvageValue` (`00014034`) and from `Mech_DirectFireHitTest` (`00013b44`, [the hit test](mech.md#the-hit-test--mech_directfirehittest-00013b44)).
- **`Detection_ContactScanRange` (`0001f814`)**: 30000 when header word 8 (`000bc4ac`) is 1, otherwise 60000, as ES2's from its header word 8 ([can this be targeted](../../retail/simulation/target-selection.md#can-this-be-targeted--targetselect_cantarget-00433174)). ES1's word 8 also picks the world file ([which file](../formats/wld-world.md#which-file)). `es2_xref.py` finds two calls, from `Detection_Sweep` (`0000e85c`, [the sweep](sensor-model.md#looking-around--detection_sweep-0000e85c)) and `0002d624`.
- **`Sim_ShutdownNoOp1` (`0001f1bc`) and `Sim_ShutdownNoOp2` (`0001e634`)**: a lone `RET` each. `Sim_Shutdown` (`0005b568`) calls the first at `0005b67a` and the second after it, its last two calls, as ES2's `Sim_Shutdown` does.
- **`maybe_Point3I_CtorWorldCopy` (`0001e604`)**: the module's copy of `maybe_Point3I_Ctor` (`000633fc`), identical after normalisation; `DBSim_LoadScriptDat` passes its padding entry `0001e601` to `__vec_new` for the 12-byte points (`0001dc3c`).

## The collision volume — `GridShape`

**The theater shape libraries and `dgs\bhulks` load as ES2's collision-volume shapes, each with a coarse height field ([the collision volume](../../retail/formats/dgs-hd0-notes.md#the-collision-volume), [the queries](../../retail/simulation/hit-detection.md#the-collision-volume--the-dgs-records-height-field)), whose march the shot sweep's walk over the zone's shapes calls.** The class `Shape_LoadAllRoots` builds for tag `0x2bc0001` ([the zone's shapes](../formats/zone-terrain.md#the-simulators-load)) is `GridShape`: the string at `0009240a` stands beside `GridShape_Construct`'s pointer at `00092418`, and the object is `0x438` bytes with ES2's fields from `+0x2a` to `+0x434`. Its vtable is `000923da`, each slot ES2's plus 4:

| Slot | ES1 | Address | Differs from ES2's |
|---|---|---|---|
| `+0x04` | `GridShape_GetClassTag` | `0001ff0a` | `nop; nop; mov eax, 0x2bc0001; ret 4` |
| `+0x08` | `GridShape_GetStreamSize` | `0001ff14` | |
| `+0x0c` | `GridShape_GetMemorySize` | `0001ff40` | `xor eax, eax; ret 4` |
| `+0x10` | `GridShape_ReadFromStream` | `0001ff48` | the row pointers and rows from the arena at `000bd954` |
| `+0x14` | `GridShape_WriteToStream` | `00020078` | |
| `+0x18`, `+0x20` | `TSShape_DebugPrint`, `TSShape_Render` | `00065f1c`, `00062fcc` | |
| `+0x1c` | `GridShape_Dtor` | `0001fee0` | stores the vtable again before `TSShape_Dtor`(shape, 0), and keeps no destructor count |
| `+0x24`, `+0x28` | `00052e88`, `00052f0c` | | shared by seven tables, where ES2's are `TSPartList_FindPart` and `TSPartList_FindPartSlot` |

`GO.EXE` carries the class too: vtable `0007e580`, the load function's pointer at `0007e5c0`, and the load function and the class's own slots at `0000f917`–`0000fc74`, the same statements as the simulator's, its two stubs with a stack frame.

`GridShape_Construct` (`0001fe6c`) takes two arguments, allocates the `0x438` bytes from the arena at `000bd954`, runs `TSShape_Ctor` and stores the vtable, and calls slot `+0x10` with the two arguments, even when the allocation failed, as ES2's does.

The three queries are ES2's, the arguments after the shape in reverse order:

- `GridShape_HeightAround` (`0001fa9c`), (shape, radius, point) in stack order: ES2's. `es2_xref.py` finds calls from the other two.
- `GridShape_HeightAt` (`0001fc5c`), (shape, radius, point): ES2's. `es2_xref.py` finds one call, from `Structure_WalkCollisionTest` (`0002075c`, [a point against the shapes](terrain.md#a-point--zone_walkcollisiontest-00020860)).
- `GridShape_Raycast` (`0001fccc`), (shape, clearance, segment, out), the segment two `int32` triples: ES2's march but for three things. It also rejects a segment whose two ends both have a negative Z; the step count is the length divided by the step, without ES2's 1 added; and a step hits when the sampled height is greater than its Z, where ES2's also wants the height non-zero. With *s* the cell shift `+0x32`:

  1. It returns 0 for a shape with no columns (`+0x2a`), when both ends' Z are at or above the ceiling `+0x430`, or when both are negative.
  2. The origin shift (*ox*, *oy*) = (`int16`)(`+0x2e` << *s*), (`int16`)(`+0x30` << *s*) is added to both ends' X and Y. The step is the `int16` (1 << *s*) + the clearance, the clearance read as a word, as `GridShape_HeightAround` reads its radius (`0001faba`).
  3. The direction, end − start, is rescaled to the step with `Math_NormalizeVec3ToLength` (`00063b5c`, [rescaling a vector](fixed-point-math.md#rescaling-a-vector)), and the step count is `Math_FastMagnitude3D`(start − end) / step, truncated to 16 bits; with a count of 0 or less it returns 0.
  4. From the start, before each advance by the direction, while the `int16` step index is below the count: when `GridShape_HeightAround`(shape, clearance, point) is greater than the point's Z, it writes the point to out `+4`, `+8` and `+0xc`, less *ox* and *oy* in X and Y, and returns 1 in `AX`. After the last step it returns 0.

  `es2_xref.py` finds one call, from `Sim_RaycastShapeVolume` (`00020894`, [a ray against the shapes](terrain.md#a-ray--zone_raycastshapeobjects-00020b50)).

## Functions

| ES1 | Address | Notes |
|---|---|---|
| `HeapPtr_DtorWorldCopy`, `ConfigPart_DtorWorldCopy`, `ConfigPart_GetClassTagWorldCopy`, `TSPartBase_DtorWorldCopy` | `0001d5fc`, `0001d624`, `0001d663`, `0001d66c` | the run that opens the module ([module copies](../formats/compiler.md#vtables)); `es2_xref.py` finds no branch or stored pointer to `0001d5fc`, and the tables' slots `+0x1c` and `+4` hold the other three |
| `Mission_WriteResults` | `0001d698` | ES2's `Mission_WriteResults` (`0042412c`) |
| `DBSim_LoadScriptDat` | `0001d850` | [reading `data\script.dat`](../formats/script-dat.md#reading) |
| `maybe_Point3I_CtorWorldCopy` | `0001e604` | the module's copy of `maybe_Point3I_Ctor` (`000633fc`) |
| `Sim_ShutdownNoOp2`, `Sim_ShutdownNoOp1` | `0001e634`, `0001f1bc` | ES2's `00425344`, `00426514` |
| `DBSim_SpawnMissionObjects` | `0001e638` | ES2's `004253d8`; [the second pass](../formats/script-dat.md#the-second-pass) |
| `Sim_RaycastObjectList` | `0001f1d0` | ES2's `00426528` |
| `Damage_ExplosiveBlastSweep`, `Salvage_QueueWeapon` | `0001f75c`, `0001f7e8` | ES2's `00426a20`, `00426ac8` |
| `Detection_ContactScanRange`, `Damage_ScaleByDifficulty` | `0001f814`, `0001f82c` | ES2's `00426aec`, `00426b04` |
| `Actions_EvaluateTriggers` | `0001f87c` | ES2's `00426b70` |
| `GridShape_HeightAround`, `GridShape_HeightAt`, `GridShape_Raycast` | `0001fa9c`, `0001fc5c`, `0001fccc` | ES2's `00427238`, `00427360`, `004273c8` |
| `GridShape_Construct`, `GridShape_Dtor`, `GridShape_GetClassTag`, `GridShape_GetStreamSize` | `0001fe6c`, `0001fee0`, `0001ff0a`, `0001ff14` | ES2's `00427568`, `0042786c`, `004275f0`, `004275fc` |
| `GridShape_GetMemorySize`, `GridShape_ReadFromStream`, `GridShape_WriteToStream` | `0001ff40`, `0001ff48`, `00020078` | ES2's `00427624`, `0042762c`, `00427714` |

Callees of other modules read for them: `RaycastShapeMinDistance_Clear` (`00020880`), `RaycastShapeMinDistance_Get` (`0002088c`) and `Zone_RaycastShapeObjects` (`00020b50`), in `terrain.cpp` ([the tests against the shapes](terrain.md#the-tests-against-the-shapes)); and `Math_ElevationAngle` (`00063cf0`), (*a*, *b*): `Math_Atan2Guarded`(`Math_FastMagnitude2D`(*b*.x − *a*.x, *b*.y − *a*.y), *b*.z − *a*.z), the elevation of *b* seen from *a*, where ES2's (`00492850`) answers that of *a* seen from *b*.

## Globals

| | Address |
|---|---|
| `LiveObjectList`, the pointer to the live object list, and its `int16` count | `000bc416`, `000bc41c` |
| the mech `DBSim_SpawnMissionObjects` built for the player's `data\player.mec` entry | `000bc42a` |
| `MissionGroups`, the group array's pointer, and its `int16` count | `000bc476`, `000bc47a` |
| `MissionActions_Count` and `MissionActions`, the 16-byte actions | `000bc494`, `000bc496` |
| `data\script.dat`'s 20-byte header, `ScriptDatHeader`; word 3 at `000bc4a2` | `000bc49c` |
| the salvage pairs' count and the pointer to them | `000bc4b2`, `000bc4b4` |
| `data\mission.var`'s 100 flags, `MissionVars`; flag 7 at `000bc4c6` | `000bc4b8` |
| the position and rotation sentinels of the second pass, each with its first-use flag 4 bytes before it | `000afa00`, `000afa10` |
| `DamageScaleByDifficulty`, four `int16` | `000922c4` |

## Open

- **Open:** the callees read for none of this module's functions: `0001ac58` and `00003cbc` from the second pass ([its Open](../formats/script-dat.md#open)); and the five readers of the header mode at offset 4 besides `Damage_ScaleByDifficulty`.
- **Open:** the caller of the scan range `es2_xref.py` finds, `0002d624`, unread.
- **Open:** whether `GO.EXE`'s `0000f39c`, `0000f515` and `0000f5c4` are its copies of the three queries: they stand before its `GridShape` functions in the queries' order, but compile differently and `0000f5c4` is 851 bytes to `GridShape_Raycast`'s 413. `es2_xref.py --binary ES1GO` finds two calls to `0000f39c` and none to the other two.
- **Open:** the module's two static constructors, the dwords at `000adc10` and `000adc14`, have no Ghidra function and no entry: `0001fa98`, a lone `RET`, and `0002018c`, which passes 1 and `00092414`, the entry beside `GridShape`'s name and load function, to `ClassItem_RegisterBucket` in stack order.
- **Open:** whether the base's and the flyer's hit tests test the owner's group's side byte before scaling, as the mech's does: their `CMP byte [reg+0x14], 0` at `00003a8b` and `0001b3df` is all that was read.
- **Open:** whether anything writes the header word at offset 4 after the read: a grep of the disassembly finds six reads of `000bc4a0` by address and no store, and the block base `000bc49c` passed to `FileRStream_Read` and `World_LoadTheater`. ES2's `DBSim_LoadScriptDat` zeroes its word there. If nothing does, a career mission, mode 1, always takes the difficulty table's last entry.
- **Open:** what the owner's `+0x98` set by a strike on an enemy is, what the slots `+0x54` and `+0x68` are on each class but the HERC's, whose are `Mech_AiOnTakingFire` and `Mech_AiOnLineOfFireBlocked` ([the HERC's AI](mech-ai.md)), and what reads `000afa1c` and `000afa18`, where the shot sweep stores `RaycastShapeMinDistance`.
- **Open:** how `GO.EXE` reads `data\results.dat`, and so what each outcome, a line index of the squad's report, means to the career.
- **Open:** which of ES2's modules holds its group functions (`0042313c`–`00423f30`): its `WORLD.CPP` asserts sit in `Mission_WriteResults` and `DBSim_LoadScriptDat`, after them.
- **Open:** the player object's field `+0x3f`, its group, has no `known_structs.json` layout: no ES1 object layout is recorded there ([the object fields](mission-groups.md#the-object-fields)).
- **Open:** a positive control in the running game for the done checklist ([when a module is done](../../herculan/plan-es1-support.md#when-a-module-is-done)). Proposed: in DOSBox-X's debugger, `BP <CS>:0001d698` set during a mission fires once as the mission ends, and after `F5` the install's `DATA\RESULTS.DAT` has just been rewritten with 8 + 4 × pairs + 72 × squad machines bytes: 80 for a one-HERC squad that salvaged nothing.
