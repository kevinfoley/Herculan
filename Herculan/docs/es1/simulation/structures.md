# DBSIM.EXE — `base.cpp`, the structures

**ES1's buildings stand, take hits and come down as ES2's do in outline: a radar dish turns, a gun tower picks a target every few seconds, turns its turret and fires both barrels, and a destroyed building throws wreckage, shakes with small explosions, goes up in a last blast and leaves a smoking wreck. What carries them is simpler: nine types in three classes, one damage total per structure where ES2's has destructible parts, and the shapes picked by the structure's side.** ES2's are [structure behaviour](../../retail/simulation/structure-behaviour.md); this page holds ES1's addresses and differences. What a player would see differently, as far as this file's code decides it:

- a structure of side 0 is drawn from `dgs\bases` or `dts\bases_an`, any other side's from `dgs\bases_e` or `dts\bases_ae`;
- the only armed structure is the GUN TOWER; it holds a target at any range, changes it only every 4.9 s, and fires only inside 40,000 units (240 m), where ES2's drops a target past 60,000;
- a shot that lands within a type's weak-spot sphere does three times its damage, and a shot from the human side is scaled by the difficulty here, in the hit test;
- every hit on a structure throws wreckage (debris group 1), where ES2's throws it on about one hit in four;
- a destroyed structure comes down the same way whatever its type: six small explosions about 0.15 s apart, a large one, and about a second later the wreck and a smoke emitter, with no EFFECTS DETAIL setting to thin it out;
- a blast does not reach a structure ([blasts](world.md#blasts--damage_explosiveblastsweep-0001f75c)).

Addresses are `ES1DBSIM` image addresses and argument lists are in stack order ([the X-32 executable](../formats/x32-executable.md#the-image)); ES2's are DBSIM's. The type file is [`BASES.DAT`](../formats/bases-dat.md), the formations [`BFORMS.DAT`](../formats/formations-dat.md), the spawn [block 8 of `script.dat`](../formats/script-dat.md#the-second-pass), and the tests against a structure's shape [the structures' tests](terrain.md#the-structures-tests).

## Where the object file starts and ends

`base.cpp`'s code is `00002c7c`–`00004bb7` and its data `00090426`–`00090765`. `Shape_DrawAtDetailLevel`'s `RET 4` at `00002c76` is followed by three zero bytes and the run of `ConfigPart`'s copies that opens the file, `00002c7c`–`00002d17`, with its tables `00090426` and `0009045a` ([module copies](../formats/compiler.md#vtables)); `es2_xref.py` finds no branch or stored pointer to the run's first function, `HeapPtr_DtorBaseCopy` (`00002c7c`). `Base_LoadResources` (`000046ac`) pushes the source path `C:\P\M\s\c\base.cpp` at `000046ee`, `00004793` and `0000486c`. The file's last code is its static initializer `Base_StaticInit` (`00004b80`, static constructor table entry `000adbc0`, no Ghidra function) and its exit routine `Base_StaticDtor` (`00004bac`, exit table entry `000adc88`), whose `RET` at `00004bb7` stands right before `LightManager_Construct` (`00004bb8`), on a 4-byte boundary, which opens the light manager's file ([effect lights](../rendering/polygon-shading.md#effect-lights)). Its functions are ES2's from `Structure_DrawWithDetailBias` (`004034f4`) to `Base_StaticDtor` (`004069b0`) for the three classes ES1 has, in ES2's order, but with the destructors after the constructor and the element constructors before the static initializer, where ES2's follow the exit routine.

The data holds, in order: the run's two tables; the firing window's pair, the turret's four tables and the hit test's occupancy flag (`0009048c`–`000904a0`); `000904a6`, the object base's table copy ([the base class's own functions](sim-object-base.md#the-base-classs-own-functions)); the three structure tables; the constructor's jump table `00090694`; `000906ba`, `DET_ANIM_OBJ`'s table copy ([the classes](object-classes.md#the-classes)); and the shape set names, the three source paths and the file names the load opens.

## The classes

**Three classes, one per table, chosen by the type: plain buildings, radar masts and the gun tower.** `Base_Construct` installs the table ([construction](#construction--base_construct-00003cbc)):

| Class | Table | Tick, slot `+0x1c` | Destructor, `+0xc` | Types |
|---|---|---|---|---|
| Plain | `StructureVtable` (`00090522`) | `Base_ThinkTick` (`00002df8`) | `Base_Dtor` (`00004318`) | 0 SUPPLY DEPOT, 1 HERC FACTORY, 2 MINING FACILITY, 3 GENERATOR, 4 HERC HANGAR, 7 REFINERY |
| Radar mast | `StructureRadarVtable` (`0009059e`) | the same | `Base_RadarDtor` (`00004360`) | 5 LISTENING POST, 6 RADAR TOWER |
| Armed | `StructureArmedVtable` (`0009061a`) | `Base_ArmedThinkTick` (`00003364`) | `Base_ArmedDtor` (`000043a8`) | 8 GUN TOWER |

ES2's switch installs five tables over 65 types ([five classes, one switch](../../retail/simulation/structure-behaviour.md#five-classes-one-switch)); ES1 has no transport, ground vehicle or missile tower. The tables share every other slot. Each slot is ES2's 4 bytes lower:

| Slot | ES1 | ES2's slot |
|---|---|---|
| `+0x04` | `Structure_DrawWithDetailBias` (`00002d18`), [drawing](#drawing) | `+0x00` |
| `+0x08`, `+0x10`, `+0x14` | `TSObj_GetPositionBaseCopy`, `TSObj_ReturnZeroBaseCopy`, `TSObj_GetRadiusBaseCopy` | `+0x04`, `+0x0c`, `+0x10` |
| `+0x20` | `Base_ReassessTickNoOp` (`00004356`): `XOR EAX,EAX`, `RET 4` | `+0x1c` |
| `+0x24` | `Base_DirectFireHitTest` (`00003a58`), [hits](#hits--base_directfirehittest-00003a58) | `+0x20` |
| `+0x28` | `Base_GetAimNodeTransform` (`00002d68`), [what a structure is aimed at](#what-a-structure-is-aimed-at) | `+0x24` |
| `+0x34` | `Base_GetAimPoint` (`00002d3c`) | `+0x30` |
| `+0x40` | the gun tower's `Base_GetTurretAngle` (`00002dc0`); the base's zero stub in the other two | `+0x3c` |
| `+0x44` | `Base_DamageFraction` (`00003c84`) | `+0x40` |
| `+0x48` | `Base_BearingInSensorArc_Always` (`00003cb4`): `MOV AX,1`, `RET 8` | `+0x44` |
| `+0x60` | `Base_GetBodyRadius` (`00002dd4`) | `+0x5c` |
| `+0x6c`, `+0x70` | `SimObject_SetRunIntoBaseCopy`, `SimObject_MissileLockState_AlwaysBaseCopy` | `+0x68`, `+0x6c` |

The rest are the base's own ([the base's slots](sim-object-base.md#the-base-classs-own-functions)). A structure's table ends at `+0x70`: ES2's explosive damage, damage write, formation offset, collision radius and component slots have no place, and ES1's structure code calls its damage write and formation offset directly.

Each destructor, (structure, flags), stores its class's table, the plain table where it is not its own, `000904a6` and `000906ba`, runs `AnimObj_Dtor` (`00001f98`)(structure, 0), and on flags bit 0 queues the structure on `g_BaseDeleteQueue` (`000aed74`) with `ObjectPool_QueueForDelete`. ES2's (`Base_Dtor` `00406dab`, `Base_RadarDtor` `00406df3`, `Base_ArmedDtor` `00406d5c`) also counts destructors and takes the structure out of the live object list.

## The fields

A structure is `0x1ba` bytes (`g_BasePool`, [loading](#loading-the-shapes--basetype_loadshape-000045a4)), ES2's `0x26d`. The fields up to `+0xad` are [the object fields](mission-groups.md#the-object-fields), 6 bytes below ES2's.

| ES1 | ES2 | Field |
|---|---|---|
| `+0x00` | `+0x04` | the type tag, 5 |
| `+0x3f`, `+0x43` | `+0x45`, `+0x49` | the group and the member slot ([joining its group](#joining-its-group--base_attachtogroup-000044a4)) |
| `+0x90` | `+0x96` | the radar: the structure emits while it is set ([the sensor model](sensor-model.md#the-fields)); set at construction for a radar mast, cleared by `Base_ApplyDamage` |
| `+0x93` | `+0x99` | destroyed |
| `+0x162` | `+0x1a8` | the class, 1 |
| `+0x16c`, `+0x170` | | the actions a strike from an enemy and the structure's destruction fire ([the second pass](../formats/script-dat.md#the-second-pass)) |
| `+0x174`, `+0x176` | | the out-of-action flag and operation ([the out-of-action report](sim-object-base.md#the-out-of-action-report--mech_reportoutofaction-0000d77c)) |
| `+0x188` | `+0x1f2` | the type record, `0x3e` bytes ([the type record](../formats/bases-dat.md#the-type-record)) |
| `+0x18c` | | the damage, `int16`, one total |
| `+0x18e`, `+0x190` | | the record's `0x06` and `0x08`, the out-of-action pair again ([Open](#open)) |
| `+0x192` | `+0x1f6` | the cell sequence's countdown record, its counter `+0x193` |
| `+0x195` | `+0x1f9` | the two animation threads |
| `+0x19d` | | the collapse's countdown record, its counter `+0x19e` |
| `+0x1a0` | | the collapse count |
| `+0x1a2` | `+0x209` | the turret's two rates, `int16` |
| `+0x1a6` | `+0x20d` | the turret's two angles, `int16`: elevation, then traverse at `+0x1a8` |
| `+0x1aa` | `+0x211` | the refire countdown record |
| `+0x1ad` | | the gun tower's target |
| `+0x1b1` | `+0x218` | the firing window's countdown record |
| `+0x1b4` | `+0x21b` | the firing window's flag, `int16` |
| `+0x1b6` | `+0x21d` | the retarget countdown record |

A countdown record is ES2's 3-byte timer, its `int16` counter at `+1`, stepped by `Math_CountdownTimerTick` (`0006410c`), which takes `SimTickDelta` off the counter, stops it at 0 and answers it, so a counter at 0 answers 0 on every tick ([helpers that match](fixed-point-math.md#helpers-that-match)). One count is about 0.49 ms: 300 is 0.15 s, 1500 0.73 s, 2000 0.98 s and 10000 4.9 s ([timer units](../../retail/simulation/structure-behaviour.md#timing-constants)).

## Loading the shapes — `BaseType_LoadShape` (`000045a4`)

`Base_LoadResources` (`000046ac`) reads the types, the formations, the collision models and the wrecks at subsystem phase 2 ([the load](../formats/bases-dat.md#the-load)); `__vec_new` builds the type records with `BaseType_ElementCtor` (`00004ac0`) and the formations with `BaseFormation_ElementCtor` (`00004a90`), whose seven 8-byte slots `BaseFormationSlot_ElementCtor` (`00004b64`) builds. Each allocates its record when given none, inlines its members' empty constructors and returns the record.

**Each type a mission places loads its shape once, from one of four shape sets, chosen by the type's `+0x06` and the structure's side.** `BaseType_LoadShape`, (side, type), from `DBSim_LoadScriptDat` for each live base slot, with the byte the first pass kept for the slot, the side byte of the group that lists the structure (`.MSN` `0x4a`, [reading](../formats/script-dat.md#reading)), sign-extended:

| `+0x06` | Side | Set | Table | Tag |
|---|---|---|---|---|
| 0 | 0 | `dgs\bases` | `BaseShapes_Static` (`000bc176`) | `0x2bc0001` |
| 0 | other | `dgs\bases_e` | `BaseShapes_StaticEnemy` (`000bc17a`) | `0x2bc0001` |
| not 0 | 0 | `dts\bases_an` | `BaseShapes_Animated` (`000bc16c`) | `0x1e0003` |
| not 0 | other | `dts\bases_ae` | `BaseShapes_AnimatedEnemy` (`000bc170`) | `0x1e0003` |

When the table's entry `+0x02` is 0 it builds the path with `ResourcePath_BuildFolderName` (`00060698`)(folder, set), loads chunk `+0x02` of it with `BaseType_ResolveShape` (`000636a4`), stores the shape there and puts `BaseTexture` (`000bc184`), the `dba\basetex` bank, in the shape's `+0x26`; a filled entry is left as it is. ES2's (`00405ebc`) reads no side, has two sets and chooses the bank by the type.

`Structure_InitPool` (`00004574`), (count), from the same pass with the live base count: `g_BasePool` (`000aed70`) = `Pool_Init`(0, `0x1ba`, count). ES2's (`00405df4`) also builds its delete queue; ES1's is the static `g_BaseDeleteQueue`. `Structure_FlushDeletes` (`00004590`, frameless, no Ghidra function) runs `ObjectPool_FlushDeleteQueue` on that queue into the pool. `Base_StaticInit` registers `Base_LoadResources` at phase 2 and `Structure_FlushDeletes` at phase 5 with `RegisterSubsystemLoader` (`00000ac4`) and zeroes the word `000aedd8` and `g_BasePool`; `Base_StaticDtor` frees the pool with `operator_delete`.

## Construction — `Base_Construct` (`00003cbc`)

(side, record), from `DBSim_SpawnMissionObjects` for each live base slot with the side above and the 16-byte block-8 record ([the second pass](../formats/script-dat.md#the-second-pass)); ES2's (`00405314`, [five classes](../../retail/simulation/structure-behaviour.md#five-classes-one-switch)). The type record is `g_BaseTypeTable` (`000bc18a`) + type × `0x3e`. The side picks the shape tables as `BaseType_LoadShape` filled them, and the switch on the type (`00090694`):

- **0–4 and 7, plain**: a structure from `g_BasePool` with `Pool_Alloc`, built by `SimObjectBase_ConstructStatic` ([the constructors](object-classes.md#the-constructors)) on the static table's entry `+0x02`, then the base's fields and `StructureVtable`;
- **5 and 6, radar mast**: the same with `SimObjectBase_ConstructAnimated` on the animated table's entry, then `StructureRadarVtable`, and the radar byte `+0x90` set to 1 after the pool test, so an empty pool writes it at address `0x90`;
- **8, armed**: as the radar mast, then `StructureArmedVtable`.

The base's fields, inlined in each arm: the object base's table `000904a6`, the class `+0x162` −1, `+0x92` = 1 ([the command screen's mark](sim-object-base.md#the-live-object-list)), `+0xa1` = 1 ([the scanner's](mfd-scanner.md#the-contacts)), `ObjectList_Add`(`LiveObjectList`, structure), the action pointers `+0x16c` and `+0x170` = 0, the flag number `+0x174` = −1; then the plain table, the type tag 5, the class 1 and the collapse count `+0x1a0` = 0. The behaviour block at `+0x47` is not built, so `Mech_AiTick`, which acts only with a descriptor there, does nothing for a structure its group ticks ([the tick](mission-groups.md#the-tick)). A type above 8 jumps to the tail with the structure pointer never set, a stack word read as it stands ([Open](#open)).

Then, for every class:

1. **The threads**, as ES2's ([the animation threads](../../retail/simulation/structure-behaviour.md#the-animation-threads)): for each *i* of 0 and 1 below the type's `+0x06`, `ShapeInst_AddThread`(structure, *i*) at `+0x195` + 4*i*, `AnimThread_SetPlaybackRate`(thread, the type's `int16` `+0x26` + 2*i*), and for *i* = 1 `AnimThread_SeekToPosition`(thread, 6000, 1). Types 5, 6 and 8 state 1, 2 and 2 threads, at rates 100; 30 and 100; and 0 and 0.
2. **The starting condition**, the record's `0x0e` (`.MSN` `0x16`): negative, damage `+0x18c` = 0; 0, the damage is the type's `+0x08`, the shape instance's (`+0x2e`) shape `+4` becomes the type's wreck, entry `+0x04` of `BaseHulkShapes` (`000bc180`), and the destroyed byte `+0x93` is set; otherwise (100 − condition) × `+0x08` / 100. ES2's ([starting condition](../../retail/simulation/structure-behaviour.md#starting-condition)) also puts each part's collapsed cell up and clears the radar; ES1's leaves a radar mast placed at 0 emitting.
3. `+0x193` = the type's `+0x2c`, `+0x188` = the type record, and `+0x18e` and `+0x190` = the record's `0x06` and `0x08`.

It returns the structure. ES2's reads no byte argument, sets the disarmed byte `+0xa5` for its plain and radar classes, and keeps a component array; ES1's sets no disarmed byte (`+0x9f`) on any structure. The second pass then sets the position, rotation, actions and out-of-action pair from the record ([the second pass](../formats/script-dat.md#the-second-pass)), and the group's build joins the structure to its group.

### Joining its group — `Base_AttachToGroup` (`000044a4`)

(structure, member slot, group), from `DBSim_BuildGroupRecord` ([the group record](mission-groups.md#the-group-record)), is `Mech_AttachToGroup` ([construction](mech.md#construction)) without its height: the group to `+0x3f`, the slot to `+0x43`; the rotation sentinel (−32768 at `+6`) becomes (0, 0, the group's heading `+0xc`) with `+0x2c` cleared; a negative Z takes the group's point `+0x20`, or with none the waypoint `Route_WaypointAt` gives for the group's route cursor, moved by `Base_ApplyFormationOffset` (`00004474`, [where a member stands](../formats/formations-dat.md#where-a-member-stands)), as it stands. ES2's (`00405c3c`) adds the formation's heading nudge, the ground patch and the ground height.

## Drawing

`Structure_DrawWithDetailBias` draws through `Shape_DrawAtDetailLevel` with `g_TSDetailBiasForStructures` ([the detail setting](object-classes.md#the-detail-setting--shapedetail_applydetailsetting-00057c08)). A structure has no LOD table, so it is drawn from its instance's shape whole ([the LOD root](object-classes.md#the-lod-root--shape_drawatdetaillevel-00002b30)): the standing shape, or the wreck once the collapse or the starting condition has put it in. Its textured faces take the `dba\basetex` bank, which `Base_LoadResources` loads only when `g_LowMemory` is 0 ([the load](../formats/bases-dat.md#the-load)).

## The plain tick — `Base_ThinkTick` (`00002df8`)

(structure), slot `+0x1c` of the plain and radar tables, which `Sim_MainTick` calls on every structure each tick that is not frozen, whatever its group ([the tick](main-loop.md#the-tick--sim_maintick-00059700)), and the gun tower's tick once the tower is destroyed. It answers 0. ES2's `Base_ThinkTick` (`00403ca8`) with ES2's `Base_DeathSequenceTick` (`00403914`) inlined for one part. In order:

1. **The collapse**, while the count `+0x1a0` is not 0, [below](#the-collapse).
2. **The cell sequence**, while the structure stands (`+0x93` clear) and the type's `+0x2a` is not negative: on each expiry of `+0x192` the counter reloads from the type's `+0x2c` and the cell of sequence `+0x2a` in the instance's cell array (`+8`) steps to (cell + 1) modulo that sequence's frame count (the shape's `+0x20`). ES2's ([the plain tick](../../retail/simulation/structure-behaviour.md#the-plain-tick--base_thinktick-00403ca8)) reads the sequence at its `+0x24` and the interval at `+0x26`. Types 2 and 8 state sequence 0 on a 256 interval, a frame every 125 ms; of them only the MINING FACILITY reaches this step standing.
3. **The threads**, while it stands and the type's `+0x06` is not 0: `SimObject_ApplyRootMotionIfEnabled`(structure, 100) ([animation threads](object-classes.md#animation-threads-and-root-motion)), which turns a radar mast's dish.

So a destroyed structure's dish stops and its cells stay where they were.

### The collapse

**A destroyed structure comes down in eight steps: six small explosions about 0.15 s apart, a large one, and about a second later the wreck and its smoke.** `Base_ApplyDamage` sets the count `+0x1a0` to 8 ([damage](#damage--base_applydamage-00003978)) and leaves the countdown `+0x19d`, never loaded before, at 0, so the first step runs on the next tick. On each expiry of `+0x19d` the count goes down by 1, and then:

| Count | What happens | Reload |
|---|---|---|
| 7 to 2 | an explosion of `EXPLOS.DAT` type `0xf` (`Explosion_Construct` (`00005af0`) with no effect, [construction](impact-effects.md#construction--explosion_construct-00005af0)) at a random point: on each axis `Math_RandomBelow`(`000b5bd4`, 2*e*) − *e* + *c*, *e* the type's `+0x18`, `+0x1a`, `+0x1c` and *c* its `+0x12`, `+0x14`, `+0x16`, through the structure's frame (`Context_GetTransform`, `Transform_ApplyToPoint`) | 300 |
| 1 | an explosion of type `0x10` at the type's point `+0x0c` through the frame (`Transform_ApplyToShortPoint`) | 2000 |
| 0 | the wreck: the instance's shape `+4` becomes `BaseHulkShapes`[the type's `+0x04`]; then `Smoke_Construct` (`000069ec`)(0, kind 1, the type's point `+0x0c`, part −1, structure), 500 balls from that point through the frame ([smoke](destruction-effects.md#where-smoke-is-lit)) | |

From the kill to the wreck is 6 × 300 + 2000 counts, about 1.9 s. The explosions 16 and 15, the hold 2000 and the 8 steps are sequence 0 of ES2's tables (`004973fc`–`00497423`), fixed in ES1's code, but ES2's sequence 0 sets the large explosion off at the structure's origin. ES2's `Base_DeathSequenceTick` steps each part on its own sequence, scatters the small explosions only as EFFECTS DETAIL allows and throws its debris at the large one ([a structure coming down](../../retail/simulation/destruction-effects.md#a-structure-coming-down)); ES1 throws the wreckage at the kill.

## The gun tower — `Base_ArmedThinkTick` (`00003364`)

(structure), slot `+0x1c` of the armed table; it answers 0. ES2's (`00404100`, [the armed tick](../../retail/simulation/structure-behaviour.md#the-armed-tick--00404100)). A destroyed tower hands the whole tick to `Base_ThinkTick`. A standing one:

1. **Retargets** when `+0x1b6` expires: `Ai_SelectTarget` (`0000ddf4`)(tower, `0x30`) ([acquisition](sim-object-base.md#acquisition--ai_selecttarget-0000ddf4)) to `+0x1ad`, and the counter `+0x1b7` = 10000. A target is kept at any range until the next retarget: there is no 60000 drop, and `Ai_SelectTarget` has no range limit. The target goes in `+0x1ad`, not in the shared target field `+0x15e`, and no holder count `+0x15c` is kept.
2. Takes the range *r*, `Math_DistanceBetweenPoints` from the target's position to the tower's, 0 with no target.
3. **Steps the muzzle flash** while the counter `+0x193` is not 0: on each expiry of `+0x192` the cell of sequence `+0x2a` steps as in the plain tick, and the counter reloads from `+0x2c` only while the cell has not wrapped to 0, so the flash plays the sequence through once.
4. With a target:
   - **The aim point**: the target's position plus the triple its slot `+0x34` gives as the aim offset ([what a structure is aimed at](#what-a-structure-is-aimed-at) for a structure's own). With *r* below 40000, the projectile record 2's speed *v* (`Proj_LookupRecordByIndex` (`0000c2bc`), its `int16` `+0xa`) and the target's speed *s* (its slot `+0x3c`) both not 0, the point moves along the target's heading (`+0xa`) by `Math_OffsetPointByBearing` (`00063d8c`) the lead (`int16`)(((`int16`)(*r* >> 3) × 8*s*) / *v*), about *r* × *s* / *v*.
   - **The turret**: `Base_AimTurret`(tower, aim point), [below](#the-turret--base_aimturret-00003058), which answers the aim error.
   - **The firing window**: on each expiry of `+0x1b1` the flag `+0x1b4` flips and the counter reloads from `BaseFiringWindow` (`0009048c`)[flag], 10000 open and 10000 shut. Like the refire countdown it runs only on ticks with a target, so it opens on the first tick a target is held.
   - **The shot**, when `+0x1aa` expires, the window's flag is set, both the error's X (elevation) and Z (traverse) lie in −1000 to 999, and *r* is below 40000: the turret's frame is the node transform of the shape's part 2 (found through the shape's slot `+0x24`, a null transform for a shape without one, read through) concatenated with the tower's frame (`Transform_Concat`); `Bullet_Fire` (`00008134`)(tower, that frame's Euler angles, (300, 400, 0) through it, 2) and again from (−300, 400, 0) ([firing](projectiles.md#firing--bullet_fire-00008134)); then the refire counter `+0x1ab` = 1500 and the flash counter `+0x193` = 1, which starts the flash.

ES2's also runs a launcher form, rolls the launcher's barrels, and keeps its target in the shared field with the holder count. **What a tower fires at** is therefore the best of `Ai_SelectTarget`'s scoring with mask `0x30`, every 4.9 s, at any range; it turns toward a target out of range but holds its fire until the target is inside 240 m, and then fires for 4.9 s, holds for 4.9 s, and so on, two rounds every 0.73 s while its aim is inside ±1000 on both axes.

### The turret — `Base_AimTurret` (`00003058`)

(structure, aim point): ES2's `Base_AimTurret` (`00403eec`) and `Base_TurretSeek` (`00403d5c`) in one ([the turret seek](../../retail/simulation/structure-behaviour.md#the-turret-seek--base_aimturret-00403eec-and-00403d5c)). It turns the point into the turret's frame, the structure's transform (`Context_GetTransform`) inverted and then part 2's node transform inverted, each by `Transform_TransposeRotation` and the negated translation through `Transform_RotatePoint`, and takes the error `Math_EulerToward`((0, 0, 0), the point, `BaseTurret_AimError` (`000aee00`)) ([helpers that match](fixed-point-math.md#helpers-that-match)). For a shape with no part 2 it copies the transform from address 0, where ES2's uses a fixed one. Then, for axis 0 the error's X and axis 1 its Z negated, each first shifted right 3 and held to ±`0x100`:

```
step = Math_Q8Multiply(in, |in|)                       // |-0x8000| taken as 0x7fff
Math_RateLimitedMoveToward(rateLimit[axis], Math_Q8Multiply(gain[axis], step), &rate[axis])   // +0x1a2
angle[axis] = clamp(angle[axis] + rate[axis], min[axis], max[axis])                           // +0x1a6
AnimThread_SeekToPosition(thread[axis], (unsigned)angle[axis] >> 2, axis)
```

with ES2's tables: gain `BaseTurret_Gain` (`00090490`) {2000, 2500}, rate limit `BaseTurret_RateLimit` (`00090494`) {200, 800}, stops `BaseTurret_MinAngle` (`00090498`) {−4000, −32768} and `BaseTurret_MaxAngle` (`0009049c`) {4000, 32767}. Last, `SimObject_ApplyRootMotionIfEnabled`(structure, 100) re-poses the nodes. It answers `BaseTurret_AimError`. `es2_xref.py` finds one call, from `Base_ArmedThinkTick`; ES2's seek has a second caller, the ground vehicle's tick.

## What a structure is aimed at

- **`Base_GetAimPoint` (`00002d3c`)**, slot `+0x34`, (structure, offset, eye): `SimObject_GetAimPointZero` clears both, then the offset's Z = the type's `+0x32`, 1500 in every type. ES2's (`0040351c`) reads its `+0x2c`. `Ai_AimAndFire` shoots a structure at its position plus that offset ([weapons](mech-ai.md#weapons)), and the gun tower adds its target's offset to the target's position in the same way.
- **`Base_GetAimNodeTransform` (`00002d68`)**, slot `+0x28`, (structure): fills `BaseAimNodeTransform` (`000aeddc`, its first-use byte `000aedda`) with a zero transform, the three diagonal entries `0x4000` and the translation's Z the type's `+0x32`, and returns it. ES2's (`00403548`).
- **`Base_GetBodyRadius` (`00002dd4`)**, slot `+0x60`, (structure): the type's `+0x30` when it has a collision model (`+0x3a` not 0), otherwise 0: 4000 for type 5, 2000 for 6 and 8. ES2's (`004035a4`) returns its `+0x2a` for every type.
- **`Base_GetTurretAngle` (`00002dc0`)**, slot `+0x40` of the armed table, (structure): the traverse angle `+0x1a8`. ES2's (`00403594`) returns its `+0x20f`.
- **`Base_DamageFraction` (`00003c84`)**, slot `+0x44`, (structure): (`+0x18c` << 8) / the type's `+0x08`, signed, the overall damage in 256ths. ES2's (`004052b4`) sums its parts.

## Hits — `Base_DirectFireHitTest` (`00003a58`)

(structure, shot), slot `+0x24`, from the shot sweep ([shots against objects](world.md#shots-against-objects--sim_raycastobjectlist-0001f1d0)); it answers the hit distance + 1, or 0 for a miss. ES2's (`00405038`, [the hit test](../../retail/simulation/hit-detection.md#base_directfirehittest--00405038)). In order:

1. **The difficulty.** It copies the shot's two damage figures, `+6` and `+4`, and when the side byte `+0x14` of the shot owner's (`+0xe`) group (`+0x3f`) is 0 scales both copies with `Damage_ScaleByDifficulty` ([the difficulty scale](world.md#the-difficulty-scale--damage_scalebydifficulty-0001f82c)). The owner and its group are read with no null test.
2. **The test.** A wreck, destroyed (`+0x93`) with its collapse count `+0x1a0` at 0, or a type with no collision model (`+0x3a` 0, every type but 5, 6 and 8), goes to `Sim_RaycastShapeVolume` (`00020894`)(1, a one-entry array of the structure, the shot's ray, result) ([a ray](terrain.md#a-ray--zone_raycastshapeobjects-00020b50)), against the shape the instance holds; a hit gives the result's distance + 1. Otherwise it rejects the shot when `Math_DistanceBetweenPoints` from the structure's position to the ray's start (frame `+0x14`) is more than the ray's length `+4` + its clearance `+8` + the structure's radius (slot `+0x14`), answering 0; then `Mech_SelectStruckComponent` (`00009064`)(the type's collision model, `BaseHit_Occupancy` (`000904a0`, one byte 1), the structure's transform concatenated with the ray's world-to-ray transform `+0xa`, ray, structure) ([the struck component](component-damage.md#the-struck-component--mech_selectstruckcomponent-00009064)), and a hit gives its distance + 1.
3. **On a hit**, the hit point is (0, distance + 1, 0) through the ray's frame. While the structure is not destroyed, **the weak spot**: when `Math_DistanceBetweenPoints` from the hit point to the type's point `+0x1e`, `+0x20`, `+0x22` through the structure's frame is below the type's `+0x24`, the damage figure `+4` triples (`int16`); then `Base_ApplyDamage`(structure, shot owner, that figure) ([damage](#damage--base_applydamage-00003978)). The `+6` figure goes nowhere. Then, destroyed or not, an effect of the shot's effect table (`+0xa`) entry 8 + (`Math_RandomNext`(`000b5bd4`) & 3) at the hit point ([where effects are spawned](impact-effects.md#where-effects-are-spawned)), and `Debris_ThrowGroup`(debris group 1, the hit point, `g_DebrisShapes`) ([where pieces are thrown](destruction-effects.md#where-pieces-are-thrown)).

ES2's takes no difficulty here, names the struck component, has no weak spot, and throws its debris when `rand & 0xfff` is below `0x401`. The weak spots (`+0x1e`–`+0x24`, [the type record](../formats/bases-dat.md#the-type-record)) are spheres of radius 250 to 1200 at a point of each type's model: 900 at 2440 above the origin on the SUPPLY DEPOT, 700 at 3640 on the GUN TOWER.

## Damage — `Base_ApplyDamage` (`00003978`)

**A structure has one damage total, and once a hit takes it past the type's hit points the structure is destroyed: the player is told if it was their target, the shooter is credited, wreckage is thrown, the mission's flag and action fire and the collapse starts.** (structure, attacker, damage): ES2's ([taking damage](../../retail/simulation/structure-behaviour.md#taking-damage--base_applydamage-00404d70)) on one damage total where ES2's has one per component, with no invulnerable test and no early roll. It adds the damage to the `int16` `+0x18c`, and when that passes the type's `+0x08` (15000; 2000 for the MINING FACILITY; 5000 for the LISTENING POST, RADAR TOWER and GUN TOWER) it:

1. posts computer message 3 (`Computer_PostMessage`, `0001a154`) when the attacker is the player's machine (`LocalPlayerMech`, `000b5b86`) and its target `+0x15e` is the structure;
2. sets `+0x18c` to the type's `+0x08` and calls the attacker's slot `+0x64`(attacker, 0, structure);
3. sets the destroyed byte `+0x93` and clears the radar `+0x90`;
4. throws the debris group at the dword `g_DebrisGroups` (`000bc1c0`) + `0x1e`, group 3, at the structure's position `+0x20`, with the shapes at the dword `g_DebrisShapes` (`000bc1c6`) (`Debris_ThrowGroup`, [throwing a group](../formats/debris-dat.md#throwing-a-group));
5. runs `Mech_ReportOutOfAction` (`0000d77c`, [the out-of-action report](sim-object-base.md#the-out-of-action-report--mech_reportoutofaction-0000d77c)) and, when the action `+0x170` is set, `Action_Activate` (`0001cc5c`) of it;
6. sets the collapse count `+0x1a0` to 8 ([the collapse](#the-collapse)).

`es2_xref.py` finds one call, from `Base_DirectFireHitTest`. A structure placed with starting condition 0 is destroyed at construction and never reaches this function: its hits test the wreck and set off their effect and debris alone.

## What the HERCs' AI does with a structure

A structure is class 1 (`+0x162`), and the AI's tests read its type's `+0x34`, 1 on the GUN TOWER and 0 on the others, where ES2's read `+0x2e`:

- a class-1 target installs `attacking base`, whose think holds its aspect at 32000 for a target whose `+0x34` is 0 ([the combat states](mech-ai.md#the-combat-states), [targets and orders](mech-ai.md#targets-and-orders));
- the flee check sends a machine out of action to choose a state again, rather than flee, when its target is a structure whose `+0x34` is 0 ([the combat reassess](mech-ai.md#the-combat-reassess--mech_aicombatreassess-00016e04));
- a group whose target is kind 3 names a structure ([the group's target](mission-groups.md#the-groups-target)): its machines' goal position is the structure's (`Mech_AiGoalPosition`, [navigation](mech-ai.md#navigation)), and under `search destroy` `Ai_SelectTarget` designates it ([acquisition](sim-object-base.md#acquisition--ai_selecttarget-0000ddf4));
- the line of sight a skirting machine waits for is tested against the structures of its own side alone ([skirting](mech-ai.md#skirting), [the structures' tests](terrain.md#the-structures-tests)).

## Functions

| ES1 | Address | ES2 |
|---|---|---|
| `HeapPtr_DtorBaseCopy`, `ConfigPart_DtorBaseCopy`, `ConfigPart_GetClassTagBaseCopy`, `TSPartBase_DtorBaseCopy` | `00002c7c`, `00002ca4`, `00002ce3`, `00002cec` | [module copies](../formats/compiler.md#vtables) |
| `Structure_DrawWithDetailBias`, `Base_GetAimPoint`, `Base_GetAimNodeTransform`, `Base_GetTurretAngle`, `Base_GetBodyRadius` | `00002d18`, `00002d3c`, `00002d68`, `00002dc0`, `00002dd4` | `004034f4`, `0040351c`, `00403548`, `00403594`, `004035a4` |
| `Base_ThinkTick`, `Base_AimTurret`, `Base_ArmedThinkTick` | `00002df8`, `00003058`, `00003364` | `00403ca8` with `00403914`; `00403eec` with `00403d5c`; `00404100` |
| `Structure_GatherWalkCandidates`, `Sim_RaycastShapeList`, `Structure_RaycastShapes` | `0000375c`, `000037e0`, `000038b8` | [the structures' tests](terrain.md#the-structures-tests) |
| `Base_ApplyDamage`, `Base_DirectFireHitTest`, `Base_DamageFraction`, `Base_BearingInSensorArc_Always`, `Base_Construct` | `00003978`, `00003a58`, `00003c84`, `00003cb4`, `00003cbc` | `00404d70`, `00405038`, `004052b4`, `00405308`, `00405314` |
| `TSObj_GetPositionBaseCopy`, `SimObject_DtorBaseCopy`, `TSObj_ReturnZeroBaseCopy`, `TSObj_GetRadiusBaseCopy`, `SimObject_SetRunIntoBaseCopy`, `SimObject_MissileLockState_AlwaysBaseCopy` | `00004270`, `00004284`, `000042a6`, `000042b0`, `000042f8`, `0000430e` | the object base's and the object classes' copies |
| `Base_Dtor`, `Base_ReassessTickNoOp`, `Base_RadarDtor`, `Base_ArmedDtor`, `DetAnimObj_DtorBaseCopy` | `00004318`, `00004356`, `00004360`, `000043a8`, `000043f0` | `00406dab`, `00406cfe`, `00406df3`, `00406d5c`, `00426f98` |
| `BaseFormation_GetSlotOffset`, `Base_ApplyFormationOffset`, `Base_AttachToGroup` | `0000441c`, `00004474`, `000044a4` | `00405b9c`, `00405c04`, `00405c3c` |
| `Structure_InitPool`, `Structure_FlushDeletes`, `BaseType_LoadShape`, `Base_LoadResources` | `00004574`, `00004590`, `000045a4`, `000046ac` | `00405df4`, `00405ea4`, `00405ebc`, `00405fac` |
| `BaseFormation_ElementCtor`, `BaseType_ElementCtor`, `BaseFormationSlot_ElementCtor` | `00004a90`, `00004ac0`, `00004b64` | —, `00406a77`, — |
| `Base_StaticInit`, `Base_StaticDtor` | `00004b80`, `00004bac` | `0040695c`, `004069b0` |

## Globals

| | Address |
|---|---|
| `g_BasePool`, `g_BaseDeleteQueue`, and the word the static initializer zeroes | `000aed70`, `000aed74`, `000aedd8` |
| `BaseAimNodeTransform` and its first-use byte; `BaseTurret_AimError` and its first-use byte | `000aeddc`, `000aedda`; `000aee00`, `000aedfc` |
| `BaseFiringWindow`, `BaseTurret_Gain`, `BaseTurret_RateLimit`, `BaseTurret_MinAngle`, `BaseTurret_MaxAngle`, two `int16` each; `BaseHit_Occupancy` | `0009048c`, `00090490`, `00090494`, `00090498`, `0009049c`; `000904a0` |
| `StructureVtable`, `StructureRadarVtable`, `StructureArmedVtable` | `00090522`, `0009059e`, `0009061a` |
| `BaseShapes_Animated`, `BaseShapes_AnimatedEnemy`, `BaseShapes_Static`, `BaseShapes_StaticEnemy` | `000bc16c`, `000bc170`, `000bc176`, `000bc17a` |
| `BaseHulkShapes`, `BaseTexture` | `000bc180`, `000bc184` |
| the type table's count, `g_BaseTypeTable`, the formations' count and array, the collision model slots | `000bc188`, `000bc18a`, `000bc18e`, `000bc190`, `000bc194` ([the load](../formats/bases-dat.md#the-load)) |

## Open

- **Open:** what reads a structure's `+0x18e` and `+0x190`, the constructor's copies of the out-of-action pair the second pass also stores at `+0x174` and `+0x176`, and the gun tower's target `+0x1ad`: none of this page's functions does but the armed tick, and no field scan has been run.
- **Open:** what reads the type record's `+0x0a` (5000; 2000 for the GUN TOWER) and `+0x2e` (0 in every type): none of this page's functions does.
- **Open:** what calls a structure's slot `+0x60`, `Base_GetBodyRadius`.
- **Open:** whether a retail block-8 record names a type above 8, which `Base_Construct` builds through an unset pointer: `HercWorks.Query structures --type all` counts 731 row-10 records under types 0 to 8, and `missions` 926 row-10 records with repeats and variants.
- **Open:** what the word `000aedd8` the static initializer zeroes is: it lies `0x64` past `g_BaseDeleteQueue`'s start, as the smoke file's words do ([its Open](destruction-effects.md#open)).
- **Open:** a positive control in the running game ([when a module is done](../../herculan/plan-es1-support.md#when-a-module-is-done)). Proposed: on `SM_SCR01`, destroy a building and watch for six small explosions, a large one and, about a second later, the wreck with smoke rising from it; in DOSBox-X's debugger, `BP <CS>:00003cbc` set before the mission loads fires once for each live block-8 record of the install's `DATA\SCRIPT.DAT`. A GUN TOWER turns toward the player at any range and fires only inside 240 m, in bursts of about five seconds.
