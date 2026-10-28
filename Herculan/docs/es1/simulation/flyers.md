# DBSIM.EXE — the flyers

**ES1's Cybrid flyers fly the way ES2's do in outline: one aircraft of a group flies the route and the others keep station on it, a flight engages as one, and an attack is a circuit, away, round and back in, firing a missile first and then pairs of gun rounds. What carries them differs: an ES1 flyer has no flight model. It moves along its nose at its type's fixed speed each tick, turns only by banking, and steps its pitch toward what its AI asks.** ES2's is [the flyer AI](../../retail/simulation/ai-flyers.md); this page holds ES1's addresses and differences. What a player would see differently, as far as these files' code decides it:

- a flyer cruises at 10000 units (60 m) where ES2's holds 30000, and holds the same 10000 while it extends away from its target, where ES2's holds the target's height plus 10000;
- it turns in from 80000 (480 m), runs in from 45000 (270 m), and breaks off when its bearing to the target leaves ±45° or it drops below 5500 (33 m), where ES2's turns at 90000, runs in from 65000 and breaks off below 3000 over the target or inside 10000;
- a look-ahead 15000 (90 m) along its nose sets the height it pitches toward from how near the ground ahead is, or a zone shape or structure short of it, where ES2's four terrain probes roll and pitch it off ground it has already reached;
- its sound plays all the while it is alive, from the position of one flyer, where ES2's plays a flyby once a pass;
- a flyer ordered to guard sleeps, where ES2's has no state for that order.

Addresses are `ES1DBSIM` image addresses and argument lists are in stack order ([the X-32 executable](../formats/x32-executable.md#the-image)); ES2's are DBSIM's. The type file is [`SKIMMER.DAT`](../formats/bullets-skimmer-dat.md#datskimmerdat), the formations [`FFORMS.DAT`](../formats/formations-dat.md), the spawn [block 7 of `script.dat`](../formats/script-dat.md#reading) and the state table [the aircraft's](shields-and-behaviour-states.md#the-aircrafts-state-table).

## Where the object files start and end

Three object files, none of whose functions asserts, hold the flyers: the flyer class, its flying and AI, and `flyersys.cpp`'s loads. They lie between [the squad orders' file](ai-squadmates.md#where-the-object-file-starts-and-ends), which ends at `0001ab53`, and [the mission groups' file](world.md#where-the-module-starts), which opens at `0001c824`. ES2's counterparts stand in the same order, `00421584`–`00423068`.

| Code | Data | Opens with | Ends with |
|---|---|---|---|
| `0001ab54`–`0001b697`, the flyer class | `00091f06`–`00092083` | the run of `ConfigPart`'s copies `0001ab54`–`0001abef` and its tables `00091f06` and `00091f3a` ([module copies](../formats/compiler.md#vtables)) | its static initializer `Flyer_RegisterSubsystem` (`0001b660`, static constructor table entry `000adc08`) and exit routine `Flyer_StaticDtor` (`0001b680`, exit table entry `000adca8`), whose `RET` at `0001b694` is followed by three zero bytes |
| `0001b698`–`0001c4bb`, the flying and the AI | `00092084`–`000920b5` | `HeapPtr_DtorFlyerAiCopy` (`0001b698`), a lone copy of the 40-byte destructor, to which `es2_xref.py` finds no branch or stored pointer | `Flyer_AiSelectBehaviour`'s `RET 4` at `0001c4b8` and one zero byte; its data is that function's jump table and two zero bytes |
| `0001c4bc`–`0001c823`, `flyersys.cpp` | `000920b6`–`00092141` | the run `0001c4bc`–`0001c557` and its tables `000920b6` and `000920ea` | `FlyerSys_RegisterSubsystem` (`0001c814`, entry `000adc0c`), its `RET` at `0001c820` and three zero bytes; the data holds the source path at `00092121` and the string `flyers` at `00092139` |

After the run's two tables the flyer class's data holds three vtables: `00091f6e`, the object base's copy ([the base class's own functions](sim-object-base.md#the-base-classs-own-functions)), which the constructor stores before the flyer's; `00091fea`, the flyer's; and `00092066`, `DET_ANIM_OBJ`'s copy ([the classes](object-classes.md#the-classes)), which the destructor stores last. The slots they share with other files point at this file's own copies ([module copies](../formats/compiler.md#vtables)).

## The flyer's table

`00091fea` is ES2's `FlyerVtable` (`0049a5e0`) with each slot 4 bytes higher, as the base's are ([the base](sim-object-base.md#the-base-classs-own-functions)). It ends after the missile lock at `+0x70`, where ES2's goes on to the explosive damage, the damage write, the formation offset and three component slots; ES1's flyer code calls its damage write and formation offset directly.

| Slot | ES1 | ES2's slot |
|---|---|---|
| `+0x04` | `Flyer_Draw` (`0001ac34`), [the draw](object-classes.md#the-detail-setting--shapedetail_applydetailsetting-00057c08) | `+0x00` |
| `+0x08`, `+0x10`, `+0x14` | `TSObj_GetPositionFlyerCopy`, `TSObj_ReturnZeroFlyerCopy`, `TSObj_GetRadiusFlyerCopy` | `+0x04`, `+0x0c`, `+0x10` |
| `+0x0c` | `Flyer_Dtor` (`0001af3c`) | `+0x08` |
| `+0x18`, `+0x1c`, `+0x20` | `Flyer_DispatchMove` (`0001afa8`), `Behaviour_DispatchThinkFlyerCopy`, `Behaviour_DispatchReassessFlyerCopy` | `+0x14`–`+0x1c` |
| `+0x24` | `Flyer_DirectFireHitTest` (`0001b370`) | `+0x20` |
| `+0x30` | `Flyer_HandleCommandNoOp` (`0001ac28`), (flyer, command): reads the command's word and returns 0 | `+0x2c` |
| `+0x3c` | `Flyer_GetSpeed` (`0001abf0`), (flyer): `Math_Q10Multiply`(1000, `+0x1c9`) | `+0x38` |
| `+0x44` | `Flyer_GetOverallDamage` (`0001b50c`), (flyer): `Component_ReadOverallDamage` of `+0x196` | `+0x40` |
| `+0x6c`, `+0x70` | `SimObject_SetRunIntoFlyerCopy`, `SimObject_MissileLockState_AlwaysFlyerCopy` | `+0x68`, `+0x6c` |

The rest are the base's own ([the base's slots](sim-object-base.md#the-base-classs-own-functions)): a squad order is refused and taking fire changes nothing, as in ES2.

## The fields

A flyer is `0x1d7` bytes (`g_FlyerPool`, [the first pass](../formats/script-dat.md#reading)), ES2's `0x291`. The flyer's own fields, from `+0x18a`, sit `0x6a` below ES2's.

| ES1 | ES2 | Field |
|---|---|---|
| `+0x00` | `+0x04` | the type tag, 6 |
| `+0x47` | `+0x4d` | the behaviour block ([the behaviour block](shields-and-behaviour-states.md#the-behaviour-block)); in it the word `+0x54` is the attack run's missile flag and the countdown record `+0x54` (its counter `+0x55`) the target sweep's, overlapping as ES2's `+0x5a` and `+0x5b` do |
| `+0x90` | `+0x96` | the radar byte, cleared every tick the flyer follows its route or keeps station, and on its death |
| `+0x93` | `+0x99` | destroyed |
| `+0xa8` | `+0xae` | the ground warning ([the move](#the-move--flyer_movementtick-0001b084)) |
| `+0xa9` | `+0xaf` | skip the next think ([the tick](mission-groups.md#the-tick)) |
| `+0x15e` | `+0x1a4` | the target, counted in the target's `+0x15c` |
| `+0x162` | `+0x1a8` | the class, 2 |
| `+0x18a`, `+0x18c`, `+0x18e` | `+0x1f4`, `+0x1f6`, `+0x1f8` | the pitch, roll and heading rates, `int16`, added to the angles `+6`, `+8` and `+0xa` each tick |
| `+0x190` | `+0x1fa` | the type record, `0x2e` bytes |
| `+0x194` | `+0x1fe` | the attack run's phase |
| `+0x196` | `+0x200` | the damage arrays, one component and one internal |
| `+0x1b2` | `+0x21c` | the formation throttle |
| `+0x1b4`, `+0x1b5` | `+0x21e`, `+0x21f` | the refire countdown record and its counter |
| `+0x1b7` | — | the run-in latch |
| `+0x1c7`, `+0x1c9`, `+0x1cb` | `+0x231`, `+0x233`, `+0x235` | the velocity in the flyer's own frame, three `int16`: 0, the type's speed, 0 |
| `+0x1cd` | — | a timer record, its `int32` counter at `+0x1ce`, for the attack run's third phase |
| `+0x1d2` | `+0x23c` | the ground warning's height figure |
| `+0x1d4`, `+0x1d5` | — | the ground warning's countdown record and its counter |

ES2's flyer code reads `+0x1f4`, `+0x1f8`, `+0xae` and `+0x23c` with no writer found ([ES2's Open](../../retail/simulation/ai-flyers.md#open)). In ES1 they are the pitch rate, the heading rate and the ground warning's flag and figure; ES1's move writes the warning's two and adds the rates.

## Construction — `Flyer_Constructor` (`0001ac58`)

(flyer, type), from `DBSim_SpawnMissionObjects`'s block 7 with a null flyer ([the second pass](../formats/script-dat.md#reading)); ES2's (`004215f4`). In order:

1. A null flyer is taken from `g_FlyerPool` (`000af72c`) with `Pool_Alloc`; it returns 0 when the pool is empty.
2. `SimObjectBase_ConstructStatic`(flyer, the type record's shape `+0x2a`) ([the constructors](object-classes.md#the-constructors)); the record is `000bc46c` + `FlyerType_RecordIndex`(type) × `0x2e`.
3. The base's fields: the vtable `00091f6e`, the class `+0x162` = −1, `+0x92` = 1, `+0xa1` = 1, `ObjectList_Add`(`LiveObjectList`, flyer) ([the live object list](sim-object-base.md#the-live-object-list)), the action pointers `+0x16c` and `+0x170` = 0 and the flag number `+0x174` = −1.
4. `Component_AllocDamageArrays`(`+0x196`, 1, 1).
5. The vtable `00091fea`, the type tag 6, the record at `+0x190`, the class 2, the velocity (0, the record's `+4`, 0), `Component_LinkMaxRefData`(`+0x196`, the record's collision model `+0x26`, its damage table `+0x22`, flyer), and `Behaviour_SetState`(`+0x47`, `deciding`, `000af58c`).

It returns the flyer. ES2's also builds the flight model's block and starts it at 1000 with the throttle at `0x200`; ES1's has none.

`Flyer_Dtor` (`0001af3c`), (flyer, flags), stores the three vtables in turn, runs `AnimObj_Dtor`(flyer, 0) and, with flag bit 0, queues the flyer on `g_FlyerDeleteQueue` (`000af730`) with `ObjectPool_QueueForDelete` ([object pools](memory.md#object-pools)); ES2's takes it out of the live object list instead. `Flyer_FlushDeletes` (`0001ac14`), which `Flyer_RegisterSubsystem` registers at phase 5, empties the queue into the pool with `ObjectPool_FlushDeleteQueue`. The static initializer also zeroes the pool pointer and the word `000af794` ([Open](#open)); `Flyer_StaticDtor` frees the pool.

### Joining its group — `Flyer_AttachToGroup` (`0001b574`)

(flyer, member slot, group), from `DBSim_BuildGroupRecord` ([the group record](mission-groups.md#the-group-record)), is `Mech_AttachToGroup` ([construction](mech.md#construction)) for a flyer: the group to `+0x3f`, the slot to `+0x43`; the rotation sentinel becomes (0, 0, the group's heading `+0xc`) with `+0x2c` cleared; a negative Z takes the group's point `+0x20`, or the waypoint `Route_WaypointAt` gives for the group's route cursor, moved by `Flyer_ApplyFormationOffset` ([where a member stands](../formats/formations-dat.md#where-a-member-stands)). Then a Z of 0 becomes 5000 (30 m). ES2's (`00421ee8`) tests Z for −32768 and calls the formation offset through the vtable. The slot is not checked against the formation's three, so a group's fifth member takes the next formation's first slot: `B0_C7_3A`'s group 148, five sleeping flyers, is the one group of more than four in the 77 missions (`HercWorks.Query orders`).

## Flying

**A flyer has no flight model: each tick it steps along its nose by its type's speed and adds three turn rates to its attitude, and its AI sets the rates.** ES2's flies through `FlightModel_Step` ([the control law](../../retail/simulation/ai-flyers.md#the-control-law)); ES1's functions in the same places act on the attitude directly. The type record's fields are in [`SKIMMER.DAT`](../formats/bullets-skimmer-dat.md#datskimmerdat).

- **`Flyer_PitchCommand` (`0001b6c0`)**, (flyer, pitch): `Math_RateLimitedMoveToward` moves the pitch `+6` toward the pitch by at most the record's `+0`, and `+0x2c` is cleared so the frame is rebuilt. ES2's (`00422098`) returns a command for the flight model.
- **`Flyer_PitchToAltitude` (`0001b708`)**, (flyer, altitude): with the ground warning `+0xa8` set the altitude is replaced by `Math_Q8Multiply`(15000, `+0x1d2`); then `Flyer_PitchCommand`(flyer, `Math_Atan2Guarded` of the height error against a fixed 6000-unit run). ES2's (`00422108`) runs it against 10000, and its warning arm pitches by `+0x23c` from the current pitch.
- **`Flyer_ApplyFlightCommand` (`0001b794`)**, (flyer, pitch command, roll command): the roll command is clamped to ±`0x100`; the roll rate `+0x18c` moves toward `Math_Q8Multiply`(command, the record's `+2`) by at most the record's `+8`; the heading rate `+0x18e` becomes −roll (`+8`) shifted right by the record's `+0xa`, so the bank alone turns the flyer; and `Flyer_PitchCommand`(flyer, pitch + 16 × the pitch command). ES2's (`004221a8`) squares the stick and runs the flight model.
- **`Flyer_CommandRoll` (`0001b76c`)**, (flyer, roll): `Flyer_ApplyFlightCommand`(flyer, 0, (roll − `+8`) >> 4). ES2's (`0042215c`) adds a roll-rate damping term.
- **`Flyer_SteerAndFly` (`0001b8c4`)**, (flyer, leader, steering): the command is −steering >> 4 plus `Math_Q10Multiply`(1100, the heading rate `+0x18e`), the heading rate first less, with a leader, `Math_Q10Multiply`(200, `Math_Q14Multiply`(the leader's `+0x18e`, cos(heading − the leader's heading))). With the roll within 500 of the record's bank limit `+0xe` and a command that would take it further over, `Flyer_CommandRoll` holds it at the limit, on either side; otherwise `Flyer_ApplyFlightCommand`(flyer, 0, command). ES2's (`004222fc`) has a 1500 band, a roll-rate term and a rudder arm for small commands, and its leader term reads `+0x1f8`, in ES1 the leader's heading rate.

### The move — `Flyer_MovementTick` (`0001b084`)

(flyer), the move of the four states that have one ([the state table](shields-and-behaviour-states.md#the-aircrafts-state-table)), in order:

1. The forward speed `+0x1c9` is reloaded from the record's `+4`, and the position becomes the flyer's own frame applied to the velocity (`Transform_ApplyToShortPoint`): a step of the speed along the nose, not scaled by `SimTickDelta`.
2. The three rates `+0x18a`, `+0x18c` and `+0x18e` are added to the pitch, roll and heading, and `+0x2c` is cleared.
3. **The look-ahead.** A ray runs from the position to the point 15000 ahead along the nose, through the frame rebuilt with the new attitude. Its distance *d* starts at 0. When the ray's end is below 500 (3 m), *d* is 1 when the position is below 500 too, and otherwise the distance along the ray to the height of 500, (height − 500) × `0x4000` / cos(−elevation − `0x4000`), the elevation from `Math_ElevationAngle`. A hit by `Sim_RaycastShapes` (clearance 0, [the zone's shapes](terrain.md)) or by `Structure_RaycastShapes` (side `0xffff`, clearance 0) replaces *d* when it is nearer than *d*; while *d* is 0, on a ray whose end stays above 500, a shape on the ray does not count.
4. **The warning.** `Math_CountdownTimerTick` steps the record `+0x1d4`. When it answers 0 and *d* is 0, `+0xa8` is cleared; otherwise it is set, and with *d* not 0 the figure `+0x1d2` becomes (15000 − *d*) × 256 / 15000 and the countdown's counter `+0x1d5` 1500, about 0.6 seconds ([the tick length](fixed-point-math.md#the-tick-length--time_beginsimtick-00063f40)). So the warning holds that long after the ray last reached the ground.

It returns 1. It does not clamp the flyer to the ground, probe the terrain or play a sound; ES2's (`004218c4`) adds a velocity from the flight model and does all three.

**Its sound.** `Flyer_DispatchMove` (`0001afa8`), the vtable's move slot, plays catalog row 16 at the flyer's position before the move: while the flyer is not destroyed (`+0x93`), `Sound_PlayAt`(position, 16) when the row is not playing and otherwise `Sound_OfferPosition` (`0004dee4`)(position, 16), and once it is destroyed `Sound_Stop`(16). It then calls the state's move with the flyer and returns its answer, 0 for a state without one. `Sound_OfferPosition` keeps the first point offered for the row each frame ([the frame's sounds](main-loop.md#the-frames-sounds--sim_framesoundtick-00058574)), so every flyer shares one sound placed at one of them.

### The player at a flyer's controls

When the view's object is a flyer and the camera is not driven, `Sim_PollPlayerInput` passes it the stick: `Flyer_ApplyFlightCommand`(flyer, throttle axis, steering axis), so the throttle axis pitches it and the steering axis rolls it, and sets `+0xa9`, which makes `Mech_AiTick` skip that tick's think and clear the byte ([the player's controls](main-loop.md#the-players-controls--sim_pollplayerinput-0005a0c0), [the tick](mission-groups.md#the-tick)). The move and the reassess still run. Whether a retail view can make a flyer the view's object is [Open](#open).

## Orders — `Flyer_AiSelectBehaviour` (`0001c46c`)

(flyer), the reassess of the five states that have one: the state from the group's order `+4` through the jump table `00092084` over 0–11, then `Behaviour_SetState` and 1. ES2's maps its verbs the same way ([orders](../../retail/simulation/ai-flyers.md#orders--flyer_aiselectbehaviour-00422d00)); ES1's words are [the HERC's](mech-ai.md#choosing-a-state--mech_aiselectbehaviour-0001892c):

| Word | State | Descriptor |
|---|---|---|
| 1 | `search and destroy` (3) | `000af5f8` |
| 6, 10 | `sleeping` (4) | `000af61c` |
| 7, 11 | `scouting` (5) | `000af640` |
| 8 | `patrolling` (2) | `000af5d4` |

It writes no out-of-the-fight byte, where ES2's latches `+0xa5` for sleep and travel. A word with no case, 0, 2 to 5 and 9 through the table and any outside 0–11 through the `JA`, installs what `ESI` holds: `Behaviour_DispatchReassessFlyerCopy` called the function through `ESI`, so the "descriptor" is the function's own code, ES2's defect in another register. No retail flyer group reaches it: `HercWorks.Query orders` lists 325 flyer group records in the 77 missions, each with order 1, 6, 8, 10 or 11.

## The thinks

The leader is the first of the group's member array (`+0xe`), as in ES2 ([the thinks](../../retail/simulation/ai-flyers.md#the-thinks), [station keeping](../../retail/simulation/ai-flyers.md#station-keeping)). Each think answers 0 but `attacking`'s.

- **`Flyer_FollowStep` (`0001c114`)**, (flyer): `Flyer_LeadRouteStep` for the leader, `Flyer_FormationStep` for the others; then the radar `+0x90` = 0. `scouting`'s think, `Flyer_BehaviourScoutThink` (`0001c2d8`), is this alone.
- **`Flyer_LeadRouteStep` (`0001b9c4`)**, (flyer): ES2's (`004224c4`): the waypoint one past the group's route cursor (`+6` of the group), the cursor stepped with `Route_AdvanceCursor` once that waypoint is within 15000 (`Math_DistanceBetweenPoints2D`), and the cursor's own waypoint when there is none past it. The steering is `Math_Q10Multiply`(800, `Math_HeadingToward` − heading), into `Flyer_SteerAndFly`(flyer, 0, steering); the formation throttle `+0x1b2` = `0xb4`; and `Flyer_PitchToAltitude`(flyer, 10000). ES2's steers by `Q16`(error, 28000) and holds 30000.
- **`Flyer_FormationStep` (`0001ba88`)**, (flyer): ES2's (`00422598`): the station `0x2000` ahead of the leader along its heading (`Math_OffsetPointByBearing`) plus the flyer's slot (`Flyer_ApplyFormationOffset`), the steering to it mirrored (`−0x8000` − steering) while the flyer is within ±`0x4000` of the leader's heading, the station is outside ±`0x4000` of its nose and nearer than 10000; `Flyer_SteerAndFly`(flyer, leader, steering); `Flyer_PitchToAltitude`(flyer, 10000), so the slot's Z does not lift it; then `Flyer_FormationThrottle` within 15000 of the station and `+0x1b2` = `0x100` beyond.
- **`Flyer_FormationThrottle` (`0001b824`)**, (flyer, `0x2000`, steering, distance, leader): ES2's (`00422260`), its arguments reversed: `+0x1b2` plus eight times the leader's speed `+0x1c9` less the flyer's plus half of `Math_Q14Multiply`(distance, cos(steering + heading − the leader's heading)) − `0x2000`, clamped to `[0x8c, 0x100]`, into `+0x1b2`. The move reloads the speed from the type record every tick ([A flyer cannot change speed](../../retail/simulation/ai-flyers.md#a-flyer-cannot-change-speed)); `Flyer_ClearFormationThrottle` (`0001c0fc`) zeroes `+0x1b2` ([Open](#open)).
- **`Flyer_BehaviourPatrolThink` (`0001c21c`)** and **`Flyer_BehaviourSearchDestroyThink` (`0001c14c`)**, (flyer): ES2's (`00422b34`, `00422a80`): `Flyer_FollowStep`, then for the leader, when `Math_CountdownTimerTick` of `+0x54` answers 0, the counter `+0x55` = 10000 and a sweep: patrolling `Ai_SelectTarget`(flyer, `0x10`), search and destroy `Ai_SelectTarget`(flyer, `0x11`) and then `Group_IsOrderTarget` against the group's one target ([the group's target](mission-groups.md#the-groups-target)). Patrolling takes whatever the sweep returns as the target, releasing the old (`+0x15c` down by one, the new one's up, `+0x97` = 1), and engages when the target is set; search and destroy takes and engages only a target that passed.
- **`Flyer_EngageWithFlight` (`0001c2ec`)**, (flyer): ES2's (`00422bf0`): `attacking` (`000af5b0`) for the flyer; then, for the leader, each other member that is not destroyed, up to the count `+0x12` or the array's first null, takes the leader's target and engages in turn.
- **`Flyer_BehaviourAttackThink` (`0001c3f8`)**, (flyer): ES2's (`00422ca8`): 1, finished, when the target's `+0x9e` or `+0x93` is set; otherwise `Flyer_AttackRun` for the leader and `Flyer_FormationStep` for the others, and 0.

## The attack run — `Flyer_AttackRun` (`0001bba4`)

(flyer), ES2's ([the attack run](../../retail/simulation/ai-flyers.md#the-attack-run--flyer_attackrun-004226a0)) with ES1's figures. It takes the target's position, the bearing error to it, the ground range (`Math_DistanceBetweenPoints2D`), the flyer's own speed and the target's through slot `+0x3c` of each, and the range capped at 200000.

1. **The lead.** In phase 1 inside 45000 the shot speed becomes `PROJ.DAT` record 2's `+0xa` (`Proj_LookupRecordByIndex`). With a positive shot speed and a moving target, the target point moves along the target's heading by (capped range >> 3) × (target speed × 8) / shot speed, taken as an `int16`, and the bearing error is taken again.
2. **Phase 0, extending.** The bearing error is turned half round, the missile flag `+0x54` cleared, phase 1 set beyond 80000, and `Flyer_PitchToAltitude`(flyer, 10000).
3. **Phase 1, running in.** Inside 45000: `Flyer_PitchToAltitude`(flyer, 0) with the ground warning set, otherwise `Flyer_PitchCommand`(flyer, `Math_ElevationAngle` from the flyer to the target point); on the first such tick the latch `+0x1b7` is set and the third phase is tested ([below](#the-third-phase)); then phase 0, with the latch cleared, when the bearing error is outside ±`0x2000` or the flyer's Z is below `0x157c`. Beyond 45000 phase 1 commands no pitch.
4. **The shot**, in any phase but 0 inside 45000: the bearing error and the pitch error to the target (`Math_ElevationAngle` less the pitch `+6`) both between −1000 and 999, the pitch rate `+0x18a` between −10 and 9, and `Math_CountdownTimerTick` of `+0x1b4` at 0. The muzzle (500, 200, −100) goes through the flyer's frame. With the missile flag clear inside 30000 (180 m), `Rocket_Fire`(the speed `+0x1c9`, flyer, angles, muzzle, 0) ([firing](rockets.md)) and the flag set; otherwise `Bullet_Fire`(flyer, angles, muzzle, 2) from the muzzle and from its mirror at X −500 ([gun rounds](projectiles.md#gun-rounds--bulletcpp)). The counter `+0x1b5` = 1500, about 0.6 seconds.
5. Below phase 2, `+0x1b2` = `0xb4` and `Flyer_SteerAndFly`(flyer, 0, bearing error).

ES2's holds the target's height plus 10000 while extending, turns at 90000, runs in from 65000 and pitches toward the target's height plus 10000 beyond it, breaks off under 3000 over the target or inside 10000, and steps its refire record through the gate the same way. `es2_fieldscan.py` finds no flyer function writing the pitch rate the gate reads ([Open](#open)).

### The third phase

**The run has two more phases, a slow strafe and a stop, that no flyer reaches.** The test that would enter it is `(Math_RandomNext & 0xfff) >> 31`, which is always 0. Taken, it would load the timer `+0x1cd` with 30000 + `Math_RandomBelow`(20000) and set phase 2. Phase 2 sets `+0x1b2` to −100 inside 8000, 0 to 20000 and 100 beyond; levels the wings through `Flyer_ApplyFlightCommand`(flyer, 0, −roll >> 4); pitches toward `0xec78`; while the roll is within ±1000, sets the heading rate to `Math_Q8Multiply`(bearing error >> 4, the record's `+0x10`) and moves the velocity's X `+0x1c7` toward 500 by 40; and when `Timer_CountDown` of `+0x1cd` answers 0, sets phase 3 and clears `+0x8c`. Phase 3 levels the wings, pitches toward 0, brings the heading rate to 0 by the record's `+8` and the velocity's X to 0 by 40, and returns to phase 0 once that X is 0. Neither phase steers through `Flyer_SteerAndFly`. ES2's run has phases 0 and 1 only.

## Hits and death

**`Flyer_DirectFireHitTest` (`0001b370`)**, (flyer, shot): ES2's ([the flyer's hit test](../../retail/simulation/hit-detection.md#flyer_directfirehittest--00421c8c)) without its test of the view's object. It returns 0 when the flyer is farther from the ray's start than its radius (slot `+0x14`) plus the ray's length and clearance; otherwise it copies the shot's two damage figures `+6` and `+4` and scales both with `Damage_ScaleByDifficulty` when the shooter's (`+0xe`) group's side byte `+0x14` is 0, as the HERC's does ([the difficulty scale](world.md#the-difficulty-scale--damage_scalebydifficulty-0001f82c)). It passes the flyer's frame joined to the ray's (`Transform_Concat`) to `Mech_SelectStruckComponent` with the record's collision model `+0x26` and the occupancy flags `+0x19e`. On a hit: `Flyer_ComponentDamageWrite`(flyer, shooter, the scaled `+4`, the struck component), then the effect and the debris group at the hit point, the effect type 10 and debris group 3 when the write answers 1 ([impact effects](impact-effects.md), [debris](destruction-effects.md)). It returns the hit distance plus 1, or 0. The scaled `+6` is not read again.

**`Flyer_ComponentDamageWrite` (`0001b290`)**, (flyer, attacker, damage, component): ES2's ([death](../../retail/simulation/ai-flyers.md#death)). `Component_ApplyDamageAndCascade`(`+0x196`, damage, component) ([component damage](component-damage.md)); when it answers non-zero for component 0:

1. computer message 3, when the attacker is `LocalPlayerMech` and that machine's target `+0x15e` is the flyer (ES2's `0x2e`);
2. the attacker's slot `+0x64`(attacker, 0, flyer), the kill credit, with no test for a null attacker;
3. `+0x93` = 1, the radar `+0x90` = 0, `+0x32` = 1 and the position's Z `+0x28` = −100000;
4. `Mech_ReportOutOfAction`(flyer) ([the out-of-action report](sim-object-base.md#the-out-of-action-report--mech_reportoutofaction-0000d77c)), and `Action_Activate` of the action `+0x170` when there is one;
5. `Behaviour_SetState`(`+0x47`, `dead`, `000af664`);

and it answers 1; otherwise 0. ES2's sets the debris carrier global around the call and stops the flyby sound; ES1's `Flyer_DispatchMove` stops the flyer's sound on the next tick.

## What a HERC does with a flyer

- `Ai_IsTargetable` with mask bit `0x10` refuses a candidate of the machine's own class, so a patrolling flyer's sweep (`0x10`) takes HERCs and structures and no flyer ([what an AI machine knows](sim-object-base.md#what-an-ai-machine-knows-and-may-target)).
- `Mech_AiCombatReassess` sends a HERC whose target is of class 2 to `attacking flyer` ([the combat reassess](mech-ai.md#the-combat-reassess--mech_aicombatreassess-00016e04)), and so does `Mech_AiEngageOrderedTarget` for a squad order ([targets and orders](mech-ai.md#targets-and-orders)).
- `Mech_BehaviourAttackFlyerThink` ([the combat states](mech-ai.md#the-combat-states)) never advances on the flyer and is done when the flyer or the machine is out of action.

## `flyersys.cpp`

The loads are in the formats' pages: `FlyerSys_RegisterSubsystem` registers `Flyer_LoadResources` (`0001c558`) at phase 2, which reads `FFORMS.DAT` ([the loads](../formats/formations-dat.md#the-loads)); `FlyerTypes_LoadAll` (`0001c770`) reads the type names and each type's files ([`SKIMMER.DAT`'s load](../formats/bullets-skimmer-dat.md#the-load-1)). `FlyerTypes_Free` (`0001c7c4`), a frameless function with no Ghidra function, sets the two-byte type-slot table `000bc466` to `0xff`, the type count `000bc464` to 0 and frees the records `000bc46c` with `operator_delete`; ES2's code at `00423018` is the same with its `0x22`-byte table ([Open](#open)).

## Functions

| ES1 | Address | ES2 |
|---|---|---|
| `HeapPtr_DtorFlyerCopy`, `ConfigPart_DtorFlyerCopy`, `ConfigPart_GetClassTagFlyerCopy`, `TSPartBase_DtorFlyerCopy` | `0001ab54`, `0001ab7c`, `0001abbb`, `0001abc4` | the run |
| `Flyer_GetSpeed`, `Flyer_FlushDeletes`, `Flyer_HandleCommandNoOp`, `Flyer_Draw`, `Flyer_Constructor` | `0001abf0`, `0001ac14`, `0001ac28`, `0001ac34`, `0001ac58` | `00421584`, `004215a8`, `004215c0`, `004215cc`, `004215f4` |
| `TSObj_GetPositionFlyerCopy`, `SimObject_DtorFlyerCopy`, `TSObj_ReturnZeroFlyerCopy`, `TSObj_GetRadiusFlyerCopy`, `SimObject_SetRunIntoFlyerCopy`, `SimObject_MissileLockState_AlwaysFlyerCopy`, `DetAnimObj_DtorFlyerCopy` | `0001ae94`, `0001aea8`, `0001aeca`, `0001aed4`, `0001af1c`, `0001af32`, `0001af7c` | the base's and `DET_ANIM_OBJ`'s |
| `Flyer_Dtor`, `Flyer_DispatchMove`, `Behaviour_DispatchThinkFlyerCopy`, `Behaviour_DispatchReassessFlyerCopy` | `0001af3c`, `0001afa8`, `0001b034`, `0001b05c` | `004271bf`, `004217fc`, `0042184c`, `00421888` |
| `Flyer_MovementTick`, `Flyer_ComponentDamageWrite`, `Flyer_DirectFireHitTest`, `Flyer_GetOverallDamage`, `Flyer_ApplyFormationOffset`, `Flyer_AttachToGroup` | `0001b084`, `0001b290`, `0001b370`, `0001b50c`, `0001b524`, `0001b574` | `004218c4`, `00421bb4`, `00421c8c`, `00421e84`, `00421e98`, `00421ee8` |
| `Flyer_RegisterSubsystem`, `Flyer_StaticDtor` | `0001b660`, `0001b680` | `00421fb0`, `00421ff4` |
| `HeapPtr_DtorFlyerAiCopy` | `0001b698` | the lone copy |
| `Flyer_PitchCommand`, `Flyer_PitchToAltitude`, `Flyer_CommandRoll`, `Flyer_ApplyFlightCommand` | `0001b6c0`, `0001b708`, `0001b76c`, `0001b794` | `00422098`, `00422108`, `0042215c`, `004221a8` |
| `Flyer_FormationThrottle`, `Flyer_SteerAndFly`, `Flyer_LeadRouteStep`, `Flyer_FormationStep`, `Flyer_AttackRun`, `Flyer_ClearFormationThrottle` | `0001b824`, `0001b8c4`, `0001b9c4`, `0001ba88`, `0001bba4`, `0001c0fc` | `00422260`, `004222fc`, `004224c4`, `00422598`, `004226a0`, `00422a3c` |
| `Flyer_FollowStep`, `Flyer_BehaviourSearchDestroyThink`, `Flyer_BehaviourPatrolThink`, `Flyer_BehaviourScoutThink`, `Flyer_EngageWithFlight`, `Flyer_BehaviourAttackThink`, `Flyer_AiSelectBehaviour` | `0001c114`, `0001c14c`, `0001c21c`, `0001c2d8`, `0001c2ec`, `0001c3f8`, `0001c46c` | `00422a50`, `00422a80`, `00422b34`, `00422bdc`, `00422bf0`, `00422ca8`, `00422d00` |
| `HeapPtr_DtorFlyerSysCopy`, `ConfigPart_DtorFlyerSysCopy`, `ConfigPart_GetClassTagFlyerSysCopy`, `TSPartBase_DtorFlyerSysCopy` | `0001c4bc`, `0001c4e4`, `0001c523`, `0001c52c` | the run |
| `Flyer_LoadResources`, `FlyerFormation_ElementCtor`, `TexPoly_AngleElementCtorFlyerSysCopy`, `FlyerType_RecordIndex`, `FlyerType_Register`, `FlyerType_LoadResources` | `0001c558`, `0001c618`, `0001c648`, `0001c678`, `0001c688`, `0001c6c8` | `00422d90`, `00423078`, —, `00422e80`, `00422e90`, `00422ed0` |
| `FlyerTypes_LoadAll`, `FlyerTypes_Free`, `FlyerFormation_GetSlotOffset`, `FlyerSys_RegisterSubsystem` | `0001c770`, `0001c7c4`, `0001c7f0`, `0001c814` | `00422fb4`, `00423018`, `00423044`, `00423068` |

`TexPoly_AngleElementCtorFlyerSysCopy` is the slots' element constructor `FlyerFormation_ElementCtor` passes, entered at its padding `0001c646`. A callee of another file read for these: `Sound_OfferPosition` (`0004dee4`), in [the frame's sounds](main-loop.md#the-frames-sounds--sim_framesoundtick-00058574).

## Globals

| | Address | ES2 |
|---|---|---|
| `g_FlyerPool` | `000af72c` | `004a9e3d` |
| `g_FlyerDeleteQueue` | `000af730` | `004a9e41` |
| `FlyerBehaviourStateTable`, seven `0x24`-byte descriptors | `000af58c` | `00499cf8` |
| `Flyer_AiSelectBehaviour`'s jump table, twelve dwords | `00092084` | — |
| a word the static initializer zeroes | `000af794` | — |

The type and formation tables are in the formats' pages: [`SKIMMER.DAT`](../formats/bullets-skimmer-dat.md#the-load-1) and [`FFORMS.DAT`](../formats/formations-dat.md#the-loads).

## Open

- **Open:** what writes the pitch rate `+0x18a`, which the move adds to the pitch and the attack run's fire gate reads. `es2_fieldscan.py 18a` finds those two reads in the flyer code and its writes elsewhere in other classes (`TtmScript_*`) and block copies; the same scan finds every write of the phase `+0x194` (positive control). Unwritten, the pitch moves only through `Flyer_PitchCommand` and the gate always passes.
- **Open:** what reads the formation throttle `+0x1b2` besides `Flyer_FormationThrottle`: `es2_fieldscan.py 1b2` finds no other read in the flyer code.
- **Open:** what calls `Flyer_ClearFormationThrottle` (`0001c0fc`) and `FlyerTypes_Free` (`0001c7c4`). `es2_xref.py` finds no branch or stored pointer for either, its one hit for `FlyerTypes_Free`'s padding `0001c7c3` being the bytes of an `ADD EBX,0x1c7` in `Flyer_AttackRun`; the same sweep finds `Flyer_LoadResources` through `FlyerSys_RegisterSubsystem`'s `PUSH`. ES2's two are unreferenced as well.
- **Open:** what the word `000af794` is, which `Flyer_RegisterSubsystem` zeroes: `es2_xref.py` finds that store and no other reference by its address.
- **Open:** whether a retail view can make a flyer the view's object, which hands the player its controls ([the player at a flyer's controls](#the-player-at-a-flyers-controls)).
- **Open:** catalog row 16's sound, the flyer's.
- **Open:** a positive control in the running game. Proposed: on `SM_PTL01`, groups 115 and 116 patrol from the start, one `SKIMMER` each; a skimmer that finds the player flies away to about 480 m, turns, runs in, fires a missile first inside about 180 m and then pairs of gun rounds, and breaks off when it turns more than 45° off or comes down near 33 m; its sound follows one flyer.
