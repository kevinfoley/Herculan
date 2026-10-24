# DBSIM.EXE — the HERC: construction, movement, damage and its systems

**ES1's HERC is ES2's mech object: the same functions build it, walk it, aim its turret, take its hits and run its systems each tick, in the same order, on ES1's fields.** What a player would see differently:

- it moves on flat ground, by ES1's own rules for its speed, its turning and its legs' pace ([movement](mech-locomotion.md));
- a blast, a shot's splash and a collision with another HERC reach its components without passing its shields, and its own code never resizes its shield array as the generator is shot;
- its reactor recharges the energy pool at a rate of 15 where ES2's has 20, and reactor damage cuts that rate from the next tick on; the pool holds 1500 back from the weapons where ES2's holds 500;
- only the two cockpits or the pilot kill it, not life support; the leg grades are coarser; and of its own damage this file's code posts only the message for a shot getting through the player's shields, where ES2's file also posts the leg, weapon and reactor messages (whether other ES1 code posts them is [Open](#open));
- the turret has no snap-to-target, and the servo sound its helpers test for plays;
- when the player's HERC is disabled, its body turns to face where the turret pointed and the turret centers;
- every hit that lands throws debris, where ES2's throws it only when the hit changes the component's damage band.

Addresses are `ES1DBSIM` image addresses ([the X-32 executable](../formats/x32-executable.md#the-image)); ES2's are DBSIM's. Every function here removes its own arguments, so argument lists are in stack order, the machine first and the other arguments in the reverse of ES2's order ([calling conventions](../formats/compiler.md#calling-conventions)).

## Where the object file starts and ends

The code is `00010d60`–`00015e3f`. It opens with the run of `ConfigPart`'s copies that starts each object file carrying one ([module copies](../formats/compiler.md#vtables)), after the one zero byte that ends [the squad condition's file](damage-readouts.md#where-the-object-file-starts-and-ends): `HeapPtr_DtorMechCopy` (`00010d60`), to which `es2_xref.py` finds no branch or stored pointer; `ConfigPart_DtorMechCopy` (`00010d88`), slot `+0x1c` of `0009192a`; `ConfigPart_GetClassTagMechCopy` (`00010dc7`); and `TSPartBase_DtorMechCopy` (`00010dd0`), slot `+0x1c` of `0009195e`. Two frameless functions with no Ghidra function follow, `Mech_FlushDeletes` (`00010dfc`) and `Mech_ResetMissionGlobals` (`00010e10`), the two the static initializer registers.

The static initializer and the exit routine close it ([where object files meet](../formats/compiler.md#where-object-files-meet-in-the-code)), both frameless:

- **`Mech_RegisterSubsystem` (`00015dfc`)**, the static constructor table's entry `000adbfc`: `RegisterSubsystemLoader` (`00000ac4`)(2, `Mech_ResetMissionGlobals`) and (5, `Mech_FlushDeletes`), then zeroes the word `000af6f4` and `g_MechPool` (`000af68c`). ES2's (`0041bc5c`) registers the same pair at the same phases ([only the short-lived classes are recycled](../../retail/simulation/sim-object-layout.md#only-the-short-lived-classes-are-recycled)) and builds the player's line-of-fire array where ES1's zeroes `000af6f4`.
- **`Mech_StaticDtor` (`00015e28`)**, the exit table's entry `000adca4`: `operator_delete` (`000015f8`) of `g_MechPool` when it is set.

Its `RET` at `00015e3c` is followed by three zero bytes and the next run at `00015e40`. No assert in the file names a source path. Its functions are ES2's from `Mech_FlushDeletes` (`0041544c`) to `Mech_SquadOrderLineIndex` (`0041bac8`), in ES2's order, less the functions [the comparison](#what-es2s-file-has-that-this-one-lacks) names, with the class copies after `Mech_Constructor` and `Mech_IsLocallyPiloted` (`000157dc`) added.

## The classes

`Mech_Constructor` stores the table `00091a6a`, the object base's, then the HERC's `MechVtable` (`00091ae6`); `Mech_Dtor` stores `00091ae6`, `00091a6a` and `00091b86` in turn before `AnimObj_Dtor` (`00001f98`)(machine, 0), where ES2's `Mech_Dtor` (`00427127`) counts three class levels and calls `AnimObj_Dtor`. So `00091a6a` is `ACTOR`'s place and `00091b86` `DET_ANIM_OBJ`'s ([two hierarchies, one root](../../retail/simulation/sim-object-layout.md#two-hierarchies-one-root), [the 3Space object classes](object-classes.md)). The file also holds its own copies of `TS_OBJ`'s (`00091ba6`), `MAT_OBJ`'s (`00091bc6`) and `DRAWABLE`'s (`00091be6`) tables for its shadow class, `00091b66`, [below](#the-shadows).

### The HERC's table

Each slot is ES2's at the slot 4 bytes lower ([vtables](../formats/compiler.md#vtables)), up to `+0x6c`:

| Slot | ES1 | ES2 |
|---|---|---|
| `+0x04` | `Mech_Draw` (`00012ac4`) | `+0x00` `Mech_Draw` |
| `+0x08` | `TSObj_GetPositionMechCopy` (`0001185c`), returns the machine + `0x20` | `+0x04` `SimObject_GetPosition` |
| `+0x0c` | `Mech_Dtor` (`00011920`) | `+0x08` `Mech_Dtor` |
| `+0x10` | `TSObj_ReturnZeroMechCopy` (`00011892`) | `+0x0c` `Stub_ReturnZero` |
| `+0x14` | `TSObj_GetRadiusMechCopy` (`0001189c`) | `+0x10` `SimObject_GetShapeRadius` |
| `+0x18`, `+0x1c`, `+0x20` | `Behaviour_DispatchMove`, `Behaviour_DispatchThink`, `Behaviour_DispatchReassess` (`000112b8`, `000112e0`, `00011308`) | `+0x14`–`+0x1c` |
| `+0x24` | `Mech_DirectFireHitTest` (`00013b44`) | `+0x20` |
| `+0x28` | `Mech_GetAimNodeTransform` (`00013204`) | `+0x24` |
| `+0x2c` | `Mech_ReceiveSquadOrder` (`0001a1cc`), outside the file ([receiving a squad order](mech-ai.md#receiving-a-squad-order--mech_receivesquadorder-0001a1cc)) | `+0x28` |
| `+0x30` | `Mech_HandleCommand` (`00011050`) | `+0x2c` |
| `+0x34`–`+0x44` | `Mech_GetAimPoint`, `Mech_GetShieldByHeading`, `Mech_GetSpeed`, `Mech_GetTorsoTwistAngle`, `Mech_GetOverallDamage` | `+0x30`–`+0x40` |
| `+0x48`, `+0x4c` | `SimObject_BearingInSensorArc` (`0000d738`), `Mech_AiEnemySighted` (`0000e760`) | `+0x44`, `+0x48` |
| `+0x50`, `+0x54` | `Mech_CompareCombatRating` (`00016968`), `Mech_AiOnTakingFire` (`00019338`), [the AI's](mech-ai.md) | `+0x4c`, `+0x50` |
| `+0x58`, `+0x5c` | `Mech_ComponentNearestAim` (`00015884`), `Mech_ComponentPosition` (`00015990`) | `+0x54`, `+0x58` |
| `+0x60`, `+0x64` | `Mech_GetBodyRadius` (`00010ec8`), `Mech_CreditNeutralisedTarget` (`00010f2c`) | `+0x5c`, `+0x60` |
| `+0x68` | `Mech_AiOnLineOfFireBlocked` (`00017a80`), [the AI's](mech-ai.md#skirting) | `+0x64` |
| `+0x6c` | `SimObject_SetRunIntoMechCopy` (`000118e4`), sets `+0xab` | `+0x68` `SimObject_SetRunInto` |
| `+0x70` | `SimObject_MissileLockState_AlwaysMechCopy` (`000118fa`), `MOV AX,1`, `RET 8` | `+0x6c` `Mech_MissileLockState` |
| `+0x74` | `Mech_MissileLockState` (`00011960`) | — |

The table ends at `+0x74`, followed by two zero dwords and `00091b66`. ES2's slots `+0x70` to `+0x84` (the blast, the damage write, the formation offset, the collision radius and the targeting pod's two component queries) have no slot: ES1 calls its blast, damage write and formation offset directly. The one call through slot `+0x70` a grep of the disassembly finds is at `000077ea`, in `00007748`, which passes the firing machine and a missile subtype where ES2's `Rocket_Fire` asks `Mech_MissileLockState` ([the lock state](../../retail/simulation/missile-lock.md#manager0x0a-is-the-lock-state-not-an-ammunition-count)); on a HERC that slot answers 1, and the lock reader sits at `+0x74` ([Open](#open)).

### The other tables

| Table | `+0x04` | `+0x08` | `+0x0c` | `+0x10` | `+0x14` |
|---|---|---|---|---|---|
| `00091a6a`, `ACTOR`'s place | `Shape_DrawAtDetailLevel` (`00002b30`) | `TSObj_GetPositionMechCopy` | `SimObject_DtorMechCopy` (`00011870`) | `TSObj_ReturnZeroMechCopy` | `TSObj_GetRadiusMechCopy`; then to `+0x70` [the object base's slots](sim-object-base.md#the-base-classs-own-functions), 0 at `+0x1c`–`+0x24` and `+0x44`, and this file's `SimObject_SetRunIntoMechCopy` and `SimObject_MissileLockState_AlwaysMechCopy` at `+0x6c` and `+0x70` |
| `00091b86`, `DET_ANIM_OBJ`'s place | `Shape_DrawAtDetailLevel` | the same | `DetAnimObj_DtorMechCopy` (`00011a0c`) | the same | the same |
| `00091b66`, the shadows | `FlatObj_DrawMechCopy` (`00011988`) | the same | `FlatObj_DtorMechCopy` (`000119b8`) | the same | the same |
| `00091ba6`, `TS_OBJ`'s | `TSObj_RenderMechCopy` (`00011a38`) | the same | `TSObj_DtorMechCopy` (`00011a58`) | the same, at `00011894` | the same |
| `00091bc6`, `MAT_OBJ`'s | `SimObject_InstallModelTransform` (`00001854`) | `MatObj_GetPositionMechCopy` (`00011aa0`) | `MatObj_DtorMechCopy` (`00011ab4`) | at `00011894` | `Drawable_GetRadiusMechCopy` (`00011ade`) |
| `00091be6`, `DRAWABLE`'s | 0 | 0 | `Drawable_DtorMechCopy` (`00011ae8`) | at `00011894` | `Drawable_GetRadiusMechCopy` |

The destructors, each (object, flags) and acting only on a non-null object: `SimObject_DtorMechCopy` stores `00091a6a` and `00091b86` and calls `AnimObj_Dtor`(object, 0), and frees nothing whatever the flags; `DetAnimObj_DtorMechCopy` stores `00091b86`, calls `AnimObj_Dtor`(object, 0) and `operator_delete`s the object on flags bit 0; `TSObj_DtorMechCopy` stores `00091ba6`, destroys the shape instance at `+0x2e` through its slot `+0x1c` with 3, stores `00091bc6` and `00091be6`, and deletes on bit 0; `MatObj_DtorMechCopy` stores `00091bc6` and `00091be6` and deletes on bit 0; `Drawable_DtorMechCopy` stores `00091be6` and frees nothing. `Mech_Dtor` queues the machine on `g_MechDeleteQueue` (`000af690`) with `ObjectPool_QueueForDelete` on bit 0, where ES2's takes it off the live object list. The getters and `TSObj_RenderMechCopy` are the code of `TSObj_GetPosition`, `TSObj_GetRadius`, `TSObj_ReturnZero`, `TSObj_Render`, `MatObj_GetPosition` and `Drawable_GetRadiusCopy`.

## The fields

The fields the object base and the mission groups share are in [the object fields](mission-groups.md#the-object-fields). The HERC's own, as this file uses them:

| ES1 | ES2 | Field |
|---|---|---|
| `+0x8d` | `+0x93` | the throttle changed, so the cockpit's gauge follows |
| `+0x8e` | `+0x94` | skip the combat-rating update; a hit that reaches the shields clears it |
| `+0x90` | `+0x96` | the radar, on or off |
| `+0x95`, `+0x96`, `+0x97` | `+0x9b`, `+0x9c`, `+0x9d` | the lock lamp, the ECM spoof, the target just changed |
| `+0x9b` | `+0xa1` | jamming |
| `+0x9d` | `+0xa3` | the player's machine |
| `+0x9f` | `+0xa5` | disarmed |
| `+0xa2`, `+0xa3` | `+0xa8`, `+0xa9` | the two leg grades, which cut speed ([the speed](mech-locomotion.md#the-speed)) |
| `+0xa4`, `+0xa5` | `+0xaa`, `+0xab` | the two reactor grades |
| `+0xa8` | `+0xae` | the unstick direction |
| `+0xaa` | `+0xb0` | the shields message has been posted |
| `+0xab` | `+0xb1` | something ran into it |
| `+0xae` | `+0xb4` | collapsed |
| `+0x15c`, `+0x15e` | `+0x1a2`, `+0x1a4` | how many objects hold it as their target; its own target |
| `+0x162` | `+0x1a8` | the target class, 0 for a HERC |
| `+0x16c`, `+0x170` | `+0x1b2`, `+0x1b6` | the two actions; the second fires when it goes out of the fight |
| `+0x18c`, `+0x190` | `+0x1f6`, `+0x1fa` | the `.COL` model and the `.DMG` table |
| `+0x1b8` | `+0x222` | the shields ([the shields](shields-and-behaviour-states.md#the-shields)) |
| `+0x1c0`, `+0x1c4`, `+0x1c8` | `+0x22c`, `+0x230`, `+0x234` | the locomotion, twist and pitch threads |
| `+0x1cc`, `+0x1d0` | `+0x238`, `+0x23c` | the shadows and their count |
| `+0x1d2` | `+0x23e` | the standing squad order |
| `+0x1e8` | `+0x254` | the unstick side, ±5000 |
| `+0x1ee`, `+0x1f1`, `+0x1f4`, `+0x1fa` | `+0x258`, `+0x25b`, `+0x25e`, `+0x264` | the lock countdowns of missile subtypes 0, 1, 2 and 4, five 3-byte records from `+0x1ee` |
| `+0x1fd`, `+0x200` | `+0x267`, `+0x26a` | the ECM roll and radar silence countdowns |
| `+0x203`, `+0x208`, `+0x20d`, `+0x212` | `+0x26d`, `+0x272`, `+0x277`, `+0x27c` | the four long countdowns: unstick, retarget, friendly-fire complaint, under fire |
| `+0x217`, `+0x21b`, `+0x21e` | `+0x281`, `+0x285`, `+0x288` | the damage from the player's side, the damage window, the damage taken |
| `+0x222`, `+0x224`, `+0x226`, `+0x228` | `+0x28c`, `+0x28e`, `+0x290`, `+0x292` | turn rate, speed, throttle, energy pool |
| `+0x22a`, `+0x22c`, `+0x22e`, `+0x230` | `+0x294`, `+0x296`, `+0x298`, `+0x29a` | twist rate, pitch rate, twist, pitch |
| `+0x236` | `+0x2a0` | the playback rate |
| `+0x24a`–`+0x24e` | `+0x2b4`–`+0x2b8` | the footfall latches, `+0x24e` the death sequence's ([footfalls](mech-locomotion.md#shadows-and-footfalls--mech_placelegsonground-000144dc)) |

## Construction

**`Mech_Constructor` (`00011330`)**, (machine, type), ES2's (`00415bb0`, [where the base ends](../../retail/simulation/sim-object-layout.md#where-the-base-ends--0x1f2)):

1. takes the machine from `g_MechPool` when it is null, returning 0 when that gives none, and runs `SimObjectBase_ConstructWithDetailTable` (`00002ac8`, [the constructors](object-classes.md#the-constructors))(machine, the type record's `+0xc6`, its LOD table);
2. stores `00091a6a`, the target class `0xffff`, `+0x92` and `+0xa1` set, `ObjectList_Add` (`0000db10`), both actions 0 and `+0x174` `0xffff`;
3. `Component_AllocDamageArrays` (`00009b8c`)(`+0x19c`, `0x15`, `0x1c`), the damage header with 21 internals and 28 components where ES2's has 22 and 29 ([the damage header](../formats/dmg-damage-file.md#the-damage-header));
4. the five lock countdowns at `+0x1ee` through `__vec_ctor` with `MechLockTimer_ElementCtor` (`00011904`), which allocates 3 bytes when given none;
5. stores `MechVtable`, the word 7 at `+0`, the type record (`0x1c4` bytes) at `+0x188`; zeroes the turn rate, speed, throttle, twist and pitch rates and angles; sets the energy pool to 10000; the target class 0;
6. `Shield_Init` (`0000fabc`)(`+0x1b8`, the record's `+0xc0`);
7. the locomotion thread on the record's `+0x12` at `+0x1c0` through `ShapeInst_AddThread` (`00001f48`, [threads](object-classes.md#animation-threads-and-root-motion)), where ES2's skips a negative sequence; the machine marked worth no salvage (`+0xad`) when the record's `+0x54` is 0; the word `+6` zeroed in the two entries the record's `+0x12` and `+0x14` index in the pointer array at `+6` of the shape's `+0x2a`, as ES2's does; and the twist and pitch threads at `+0x1c4` and `+0x1c8` for a sequence that is not negative ([three threads per machine](../../retail/simulation/torso-aim.md#three-threads-per-machine));
8. one shadow per byte of the record's list at `+0x72` up to the first negative one ([the shadows](#the-shadows));
9. the behavior state `deciding`, or `player` when `+0x9d` is set ([the HERC's state table](shields-and-behaviour-states.md#the-hercs-state-table)); the second pass sets the player's `+0x9d` only after this constructor returns ([the second pass](../formats/script-dat.md#the-second-pass)), so the player's machine, whose slot holds 0 there (below), is built in `deciding` and takes `player` from `deciding`'s reassess, `Mech_AiSelectBehaviour`, on its first tick;
10. loads `dmg\<name>` into `+0x190` with `Damage_LoadMechDmgFile` (`00009a2c`) and `col\<name>` into `+0x18c` with `Collision_RegisterObject` (`00009580`), the name being `g_MechTypeNames` (`000bc440`) at the record's `+0x6c`, and binds them with `Component_LinkMaxRefData` (`00009c28`)(`+0x19c`, model, table, machine);
11. clears the footfall latches `+0x24a`–`+0x24d` and sets `+0x24e`.

**A new machine's other fields are 0, its radar off among them.** The first pass makes `g_MechPool` each mission with `Pool_Init` ([reading](../formats/script-dat.md#reading), step 6), which takes the slots' block from `operator_new` and does not clear it, where ES2's `Pool_Init` (`004719cc`) does; through a mission's load the heap is on, so `operator_new` allocates with `Heap_Alloc`, which zero-fills the payload ([the heap](memory.md#the-heap)). `Pool_LinkFreeList` keeps its links in the 8 bytes before each slot and `Pool_Alloc` leaves the rest as it was, so a slot the second pass takes fresh from the block holds zeros, and a field the construction does not write is 0 when `Mech_Constructor` returns: `+0x9d` when step 9 tests it, and the radar `+0x90`. The writes of 1 to an object's `+0x90` that `es2_fieldscan.py --binary ES1DBSIM 90 --writes-only` finds are `Mech_ToggleRadarMode`'s flip, the AI's (`Ai_UpdateWeaponsFree`, `Mech_AiCombatReassess`, `Mech_BehaviourTravelThink`, `Mech_BehaviourGuardThink`, `Ai_ChooseWeapon`), `Mech_DirectFireHitTest`'s for an AI machine, the structure builder `00003cbc`'s and `Mech_ReceiveSquadOrder`'s for verb 4, whose two senders found by a grep of the disassembly both skip the cockpit's machine ([receiving a squad order](mech-ai.md#receiving-a-squad-order--mech_receivesquadorder-0001a1cc)) ([Open](#open)).

`es2_xref.py` finds one call, from `DBSim_SpawnMissionObjects` (`0001e638`, [the second pass](../formats/script-dat.md#the-second-pass)).

**`Mech_ConfigureLoadout` (`00012c00`)**, (machine, ammunition types, weapon fit), from a block-6 record's `0x46` and `0x2a` or a `data\player.mec` entry's `+0x20` and `+8`: ES2's ([the join](../../retail/simulation/weapon-mounts.md), [a fitted weapon replaces its mount's piece](../../retail/formats/dmg-damage-file.md#a-fitted-weapon-replaces-its-mounts-piece)) up to the occupancy flags. It builds the manager at `+0x198` ([the manager](weapon-mounts.md#the-manager)), runs `SimObject_ApplyRootMotionIfEnabled` (`0000215c`)(machine, 0), and for each fitted mount stamps the hardpoint's node onto the model through the shape's slot `+0x24` and `Shape_StampTransformId`, then puts the template's spheres, piece and internal maximum into the machine's model and table with `Collision_ReplaceClusters` (`000096e8`), `HercPiece_ReplacePieces` (`00009ad4`) and `HercPiece_SetInternalMaxima` (`000099e4`), and binds the anchors with `Collision_CollectComponentAnchors` (`000092e4`) and `HercPiece_BindComponentAnchors` (`00009b28`), each ES2's ([the loadout's join](../formats/dmg-damage-file.md#the-loadouts-join)). It clears the occupancy flag of each of the 28 components whose piece lists no internal. ES2's then files the pods and sizes the shields and the reactor; ES1's stops there. `es2_xref.py` finds one call, from `DBSim_SpawnMissionObjects`, and three dword occurrences of the address in data, at `00096bcb`, `00096be3` and `000ad900` ([Open](#open)).

**`Shape_StampTransformId` (`00012b58`)**, (node id, part): ES2's (`00417530`, [the hardpoint's node](../../retail/simulation/weapon-mounts.md)), the class tag read through slot `+4` where ES2's reads slot 0, and dispatched by `REPNE SCASD` over the ten dwords at `00091c04` with their targets after them. Its tags are ES2's and `0x1e0003` and `0x1e0005`, which return, as a tag it does not list does.

**`Mech_ApplyStartingCondition` (`00012f20`)**, (machine, condition), from a block-6 record's `0x60`: ES2's ([starting condition](../../retail/simulation/component-damage.md#starting-condition--mech_applystartingcondition-004178e8)), writing through `Mech_ComponentDamageWrite` directly. **`Mech_SpreadImpactDamage` (`00013078`)**, (machine, odds, damage): ES2's ([spread impact damage](../../retail/simulation/component-damage.md#spread-impact-damage--mech_spreadimpactdamage-00417a04)) over 28 components, the armor from `0000a24c` where ES2's calls `Component_TotalArmor`. `es2_xref.py` finds three calls: the landing in `Mech_LocomotionTick`, `Mech_ApplyStartingCondition` and `Mech_ApplyOutnumberedDamage`.

**`Mech_AttachToGroup` (`00013124`)**, (machine, member slot, group), from `DBSim_BuildGroupRecord` ([the group record](mission-groups.md#the-group-record)), with the slot the member's ref's place among the group's eight: stores the group at `+0x3f` and the slot at `+0x43`; when the rotation holds the sentinel (`+6` is −32768) it becomes (0, 0, the group's heading `+0xc`) with `+0x2c` cleared; and when the position's Z is negative, which the sentinel's −1 is, the position becomes the group's point `+0x20`, or with none the waypoint `Route_WaypointAt` gives for the group's route cursor, its Z raised by the record's `+0x16`, moved by `Mech_ApplyFormationOffset` (`00012ed0`, [formation offsets](../formats/formations-dat.md)). ES2's (`00417aa8`) tests Z for −32768, takes the ground height and lifts a flyer.

### The subsystem

- **`Mech_ResetMissionGlobals` (`00010e10`)**, phase 2: `Mech_DrawEnabled` (`000bc428`) = 1, and the developer keys' steps `DevKeys_MoveStep` (`000af6f6`) and `DevKeys_TurnStep` (`000af6f8`) from the words `00091996` and `000919aa`, both 2000. ES2's (`00415464`).
- **`Mech_FlushDeletes` (`00010dfc`)**, phase 5: `ObjectPool_FlushDeleteQueue` (`00063784`)(`g_MechDeleteQueue`, `g_MechPool`). ES2's (`0041544c`).

## The shadows

**A HERC's shadows are drawn flat at height 0, the ground's level in ES1, where ES2's lie on the terrain.** The constructor builds each from the pool `FlatObj_LoadResources` (`00006cf8`) makes, `000af044`, with `TSObj_Ctor` (`00001b04`) on the root of the flat shape set (`000bc1d8`) the list's byte names, the table `00091b66`, and Z −100000; it tests neither the allocation nor the pool. `Mech_PlaceLegsOnGround` moves them ([shadows and footfalls](mech-locomotion.md#shadows-and-footfalls--mech_placelegsonground-000144dc)), as ES2's does ([a HERC's shadows](../../retail/simulation/ground-shapes.md#a-hercs-shadows)).

- **`FlatObj_DrawMechCopy` (`00011988`)**, (shadow): sets the shadow's Z (`+0x28`) to 0, `SimObject_InstallModelTransform`, and draws the shape instance at `+0x2e` through its slot `+0x20`. ES2's `FlatObj_Draw` (`0040991c`) conforms the shadow to the terrain first.
- **`FlatObj_DtorMechCopy` (`000119b8`)**, (shadow, flags): stores `00091b66` and `00091ba6`, destroys the shape instance through its slot `+0x1c` with 3, stores `00091bc6` and `00091be6`, and on flags bit 0 queues the shadow on `000af048` with `ObjectPool_QueueForDelete`.

## Movement

The throttle, the control law and gait machine, obstacle avoidance, the move and its root motion, collision and the footfalls, this file's `Mech_ApplyThrottleInput`, `Mech_AiObstacleAvoidance`, `Mech_LocomotionTick`, `Mech_IntegrateMotion`, `Mech_CollisionTest`, `Mech_PlaceLegsOnGround` and `Mech_MovementTick`, are [the HERC's movement](mech-locomotion.md).

## The turret

**`Mech_TorsoTwistTick` (`000149ac`)**, (machine, axis), and **`Mech_TorsoPitchTick` (`00014ba0`)**, (machine, range, axis), are ES2's ticks ([the tick](../../retail/simulation/torso-aim.md#the-tick)) on `+0x22a`/`+0x22e` with the thread `+0x1c4`, and `+0x22c`/`+0x230` with `+0x1c8`, without the snap-to-target: neither takes a target or the flag. The pitch tick does ES2's gun convergence itself ([gun convergence](../../retail/simulation/weapon-firing.md#gun-convergence--mech_convergegunsonrange-0041a74c)) with no gates: for each mount, the range 0 clears `+0x24`–`+0x29`, otherwise `Math_EulerToward` from the mount's `WeaponMount_MuzzleOffset` less the record's `+0x66` in Z to (0, range, 0) fills them. `es2_xref.py` finds three calls of each, from `Mech_CenterTorsoTick` (`000186f8`), `Cockpit_TargetAnglesFromCameraBone` (`00018b78`) ([the AI's weapons](mech-ai.md#weapons)) and `0005a0c0`.

**`Mech_TorsoTwistServoSound` (`00014afc`)** and **`Mech_TorsoPitchServoSound` (`00014dc4`)**, (machine, axis), run at the end of each tick for the player's machine, test what ES2's helpers test ([the servo-sound helpers](../../retail/simulation/torso-aim.md#the-servo-sound-helpers)) and act on it: with the latch clear, the axis past `0xc0` and the angle moved since the last call, they play sound `0x21` when `Sound_IsPlaying`(`0x21`) (`0004e05c`, [the frame's sounds](main-loop.md#the-frames-sounds--sim_framesoundtick-00058574)) answers 0 and set the latch; with the latch set and the axis 0 or the angle still, they stop `0x21` and clear it. Their latches and last angles are `00091c54`/`00091c56` and `00091c58`/`00091c5a`.

## Hits and damage

### The hit test — `Mech_DirectFireHitTest` (`00013b44`)

Slot `+0x24`, (machine, shot): ES2's hit test and `Mech_ApplyDirectFireDamage` in one function ([direct-fire damage](../../retail/simulation/damage-system.md#direct-fire-damage-armor-then-part-deterministic-shield-gated)), on ES1's fields.

1. The coarse reject as ES2's; then the copies of the shot's `+6` (the shields' figure) and `+4` (the armor's), both scaled by [`Damage_ScaleByDifficulty`](world.md#the-difficulty-scale--damage_scalebydifficulty-0001f82c) when the owner's group's side byte is 0.
2. A machine other than the player's turns its radar on for a shot of type 0 or 1 unless the radar silence counter `+0x201` runs, and off with the counter at 6000 for type 2, as ES2's.
3. `Mech_ShieldAbsorb_DirectFire` (`0000fca8`, [the shields](shields-and-behaviour-states.md#the-shields)) on the shot in the machine's frame. With no entry it returns 0.
4. With one: `+0x8e` cleared and `+0x21e` raised by what the shields took. A shot the shields stopped makes its impact at the entry point. Otherwise the player's machine posts computer message 8 once, latched on `+0xaa`, where ES2's posts `SHIELDS CRITICAL` while front and rear together hold under 500; then `00009064`(model, flags, frame, shot, machine), where ES2's calls `Mech_SelectStruckComponent`, picks the component, and a miss returns 0.
5. On a struck component, ES2's damage application ([the chance path](../../retail/simulation/weapon-mounts.md#the-chance-path--the-destruction-roll)): `+0x21e` raised by the armor figure, the splash Q10(armor, shot `+8`), the reading before from `Component_ReadDamagePercent` (`0000a16c`), `Mech_ComponentDamageWrite`(machine, owner, armor − splash, component), and with a splash `Mech_ApplyExplosiveDamage`(machine, owner, 500, point, splash). When the band (`>> 5`) changed, a hit on component 0 or 1 of the player's machine starts `Cockpit_StartHitShake` (`0002e17c`), where ES2's also wants the reading under 100 and posts message `0x12`, and the mount knock-out roll runs as ES2's, its salvage at Q10(500, reading) for side 1 and its cascade through `00009f7c`(header, 10000, component).
6. Unless the component now reads `0x100`, the impact effect, `Explosion_Construct` (`00005af0`) with the shot's effect table `+0xa` at 4 × *n* + random & 3, *n* 0 for a stopped shot, 1 for an unchanged band and 2 for a changed one; and for a struck component `Debris_ThrowGroup` (`00005fb8`, [the debris](../formats/debris-dat.md)) with group *n* of `000bc1c0`, the point and the roots `000bc1c6`. ES2's throws debris only for band 2, and makes the impact even when the component is gone.

It returns the entry distance, the component's after a strike.

### A blast — `Mech_ApplyExplosiveDamage` (`00013a7c`)

(machine, attacker, radius, point, damage), called by `Damage_ExplosiveBlastSweep` (`0001f75c`, [blasts](world.md#blasts--damage_explosiveblastsweep-0001f75c)), the hit test's splash and the collision: ES2's per-component roll and falloff ([a mech](../../retail/simulation/damage-system.md#a-mech--mech_applyexplosivedamage-004187d0)) with no facing and no shields. Each of the 28 components whose occupancy flag is set and whose roll `Math_RandomNext` & `0xfff` falls under `0x802` is placed with slot `+0x5c` and, inside the radius, takes damage × (radius − distance) / radius through `Mech_ComponentDamageWrite`.

### The damage write — `Mech_ComponentDamageWrite` (`00013250`)

(machine, attacker, damage, component), the endpoint ([component damage](../../retail/simulation/component-damage.md#the-component-damage-system)), called directly by every writer here.

1. **Refusals.** On the player's machine, nothing while `MissionModeFlag` is 0 and script.dat header word 6 (`000bc4a8`) is 1, where ES2's asks `Sim_DamageToPlayerDisabled`; and nothing for a component whose occupancy flag is clear.
2. **The cascade**, `00009f7c`(header, damage, component), where ES2's calls `Component_ApplyDamageAndCascade`, with no mount snapshot before it.
3. **Mounts.** Each mount not yet destroyed (`+0x49`) whose component's occupancy flag the write cleared is destroyed with `WeaponMount_Destroy` (`0000b968`)(mount, 0, machine). ES2's calls each mount's condition slot with the readings before and after, and posts `WEAPON DESTROYED` ([losing a mount](weapon-mount-classes.md#losing-a-mount)).
4. **No shield recompute.** ES2's runs `Mech_ComputeShieldCapacity` here.
5. **Legs**, for any chassis, on internals 0 and 1, and 10 and 11 as well when the record's `+0x4a` is above 2: a side reading `0x100` destroys its shadow through the shadow's slot `+0xc` with 3, where ES2's queues it. When the machine is not immobilized: half or more of the legs gone immobilizes it, with the attacker's slot `+0x64`(attacker, 0, machine), `Mech_ReportOutOfAction` (`0000d77c`), the action `+0x170` and `disabled` while it is not destroyed, then `+0x9e`; otherwise the averaged sides grade at `0xc1` (`+0xa3`) and past `0x80` (`+0xa2`), where ES2's grades at `0x8d` and `0x50`. ES2's releases the target and posts the leg messages.
6. **Disabled by the player.** With `+0x9e` set by this write's leg branch, the attacker `LocalPlayerMech` (`000b5b86`) and that machine's target `+0x15e` the machine, computer message 7, ES2's `0x2f`.
7. **The player's machine disabled.** When the machine is `LocalPlayerMech`, the same branch turns the body to the node the record's `+0xc` names: the node's transform composed with the machine's by `00064fe4`, `Transform_RotationToEuler`, and the yaw to the heading `+0xa`; it clears `+0x2c` and the twist `+0x22e`, seeks the twist thread to position 0, and runs `SimObject_ApplyRootMotionIfEnabled` (`0000215c`)(machine, 1). ES2's has no such step.
8. **Death**, while `+0x93` is clear: when `00009f30` reports component 0 or 1 fully destroyed, or internal 9, the pilot, reads `0x100`. ES2's also kills on internal 8, life support. It sets `+0x93`, tells the attacker through its slot `+0x64` with the sampled `+0x9e`, runs the report and the action `+0x170` when that was clear, puts the radar off, posts message 3 (ES2's `0x2e`) on the same test as step 6, and installs `dead`, or for a record whose `+0x4c` is set sets `+0x32`, sinks the machine to Z −100000, installs `in limbo` and destroys every shadow with its count zeroed. ES2's also writes 30000 to component 0 first, releases the target and skips the state for a flyer.
9. **The reactor**, while `+0xa4` and `+0xa5` are clear: internal 5's reading latches `+0xa5` past `0xc0` and `+0xa4` past `0x80`, as ES2's, with no message.

ES2's ends by flushing the shadows' delete queue.

**`Mech_CreditNeutralisedTarget` (`00010f2c`)**, slot `+0x64`, (attacker, already immobilized, victim): ES2's ([what the attacker is told](../../retail/simulation/component-damage.md#what-the-attacker-is-told--mech_creditneutralisedtarget-00415710)) with two differences in the kill half: the squadmate's kill call-out is `Ai_PostSquadMessage` (`0001a188`)(attacker, 0, 2) for an attacker in the player's group other than the player, and for the player itself the nearest live squadmate within 30000 (`Group_NearestLiveMember`) calls out 5; and it sets no latch. The victim's cry, 4 or `0x25`, is ES2's; ES2's also counts the player's kills.

## The systems tick — `Mech_PerTickSystemsUpdate` (`00014ee0`)

(machine), once a tick from `Sim_MainTick` (`00059700`, [the tick](main-loop.md#the-tick--sim_maintick-00059700)) for each machine whose group has no action and that is not destroyed, unless the simulation is frozen: ES2's ([the per-tick cycle](../../retail/simulation/reactor-energy-pool.md#the-per-tick-cycle--mech_perticksystemsupdate-0041aa5c), [missile lock](../../retail/simulation/missile-lock.md)) with these differences.

- **The energy.** The pool `+0x228` gains `Math_IntegrateRateOverTick`(15), scaled Q10 by 200 while `+0xa5` is set or 600 while `+0xa4` is. ES2's adds the rate `Mech_ComputeReactorRate` fixed at spawn, 20 or with an Energy Pod 40. The weapons get the pool less 1500 through the manager's energy slot, (manager, budget), the shields the rest through `Shield_RechargeTick` (`0000fb2c`), and the pool becomes what is left plus 1500, clamped to [0, 10000]. ES2's holds back 500.
- **The shields message** re-arms on the player's machine when the front and the rear each hold more than 300, where ES2's wants more than 1500 together.
- **The countdowns** are ES2's on ES1's fields ([the fields](#the-fields)): `Timer_CountDown` (`00064178`) on the three long ones and the radar silence's with `Math_CountdownTimerTick`; the under-fire window's expiry zeroes `+0x217`, and the damage window's resets it to 4000 and zeroes `+0x21e`. With `+0x8e` clear, `Mech_ComputeCombatRating` (`000189f0`, [the combat reassess](mech-ai.md#the-combat-reassess--mech_aicombatreassess-00016e04)).
- **ECM.** As ES2's ([ECM](../../retail/simulation/missile-lock.md#ecm)): `Mech_GetEcmPodSwitch` sets the target's `+0x9c`, `Mech_IsEcmSwitchedOn` sets `+0x9b`. The roll against a jamming HERC target is always out of 20, ES1 having no Targeting Pod.
- **Lock.** The five flags at the manager's `+6` and `+0x95` are cleared, the rounds counted with `WeaponMounts_RoundsByMissileType` (`0000be6c`); with a target, the reload is the distance >> 2 for any chassis and the bearing the twist-adjusted one, and the block holds on `+0x97` or a bearing outside ±`0x3000` alone: there is no line-of-sight test. The subtypes' countdowns and holds are ES2's. For the player's machine the lamp `+0x95` follows the armed mount's subtype (its slot `+0x64`) as ES2's, and `Mech_LockTonePlay` runs; `+0x97` is cleared at the end when the block held.

**`Mech_LockTonePlay` (`0001552c`)**, (machine): ES2's ([the lock lamp](../../retail/simulation/missile-lock.md#the-lock-lamp)) on `+0x95` and `+0x97`, its two latches `00091c5d` and `00091c5c`, and the phase from bit `0x40` of now, `000981c4`.

## The cockpit

**`Player_PerFrameCockpitUpdate` (`000155a8`)**, (machine), from `Sim_RenderFrame` (`00059c08`) and `Sim_StartMission` (`0005a9dc`), ES2's (`0041b130`) with the target's selection coming from the gunsight:

1. the energy meter at the cockpit's `+0x1e1` through `EnergyMeter_SetValue` (`00039b78`, [the energy meter](cockpit-energy-shields.md)) with (pool << 10) / 10000 in `000af6fc`; `Shield_BalanceInputRead` (`0000fbb4`); `WeaponMounts_PerFrameUpdate` (`0000cf78`, [the per-frame pass](weapon-mounts.md#the-per-frame-pass--weaponmounts_perframeupdate-0000cf78)) with the distance to the target, always from the machine, where ES2's measures from the viewed object in the external view;
2. the throttle exchange with the gauge at `+0x1ed` through `ThrottleGauge_GetValues` (`0003b250`) and `ThrottleGauge_SetValues` (`0003b200`) ([the exchange](cockpit-throttle-gauge.md#the-exchange-with-the-throttle)), ES2's less the flyer's throttle;
3. the gunsight's state block (`Gunsight_GetStateBlock` at `+0x1f1`, [the gunsight](cockpit-gunsight-hud.md)): the pitch at offset 4, and unless the machine is immobilized the heading and the twist at 0 and 2. When the block's target, the dword at offset 6, is not the machine's `+0x15e`, it clears `+0x95`, takes 1 from the old target's holders' count `+0x15c` when there is an old target, makes the block's the machine's target, adds 1 to its count when it is not null, and sets `+0x97`, and when the new target is none and TRACK's latch (manager `+0x14`) is set, sets `g_CenterTurretMode` (`000b5ba4`) at once. ES2's takes the selection from the cockpit, resets the Targeting Pod's lock and waits `0x1194` before centering;
4. the lamp `+0x95` at offset `0x25`, and `Gunsight_SetValues`. ES2's then resolves the target's aim point for the cockpit's target block; ES1's does not.

**`Mech_HandleCommand` (`00011050`)**, slot `+0x30`, (machine, code): offers the code to `WeaponMounts_HandleCommand` (`0000d4b4`) with the manager of `PlayerMech` (`000bc42a`) first, returning 1 when it takes it; otherwise it finds the code among the 19 dwords at `000919d0` by `REPNE SCASD`, returns 0 for any other, and returns 1 after:

| Code | Action |
|---|---|
| `0x4c` | `PlayerMech`'s throttle zeroed and `+0x8d` set |
| `0x619` | `Mech_ApplyOutnumberedDamage`(`PlayerMech`) |
| `0x602`–`0x60a` | the move and turn steps from the `int16` tables at `00091990` and `000919a4`, read as `00090d8c` and `00090da0` plus twice the code |
| `0x24d`, `0x24b`, `0x248`, `0x250` | the machine moved by the move step along +X, −X, +Y and −Y of its own frame |
| `0x44b`, `0x44d` | the heading turned by ± the turn step, `+0x2c` cleared |
| `0x1a`, `0x1b` | `Shield_BalanceAdjust` (`0000fae4`)(`PlayerMech` `+0x1b8`, the code is `0x1b`) |

ES2's ([the developer keys](../../retail/command-line.md#-sprunknown-the-developer-keys)) gates the movement and step codes on the developer keys' switch, presses the shield gauge's child for `0x1a`/`0x1b` ([the shield-balance rocker](../../retail/simulation/cockpit-input.md#8-worked-example-the-shield-balance-rocker)), and has no `0x619`.

**`Mech_ToggleRadarMode` (`000157f4`)**, (machine): flips the radar `+0x90` when the machine's `+0x9d` is set, the locally piloted machine, and then, flipped or not, plays `0x1b` with the radar off or `0x1a` with it on. ES2's (`0041b468`) also posts the radar message. `es2_xref.py` finds two calls, `CockpitWidgets_HandleCommand`'s `R` ([the panel's keys](cockpit-panel.md)) and `MfdButton_OnClick` (`0003a46c`), the MFD's `MODE` button on the SCANNER screen ([the display's clicks and keys](mfd-display.md#clicks-and-keys)).

## Small functions

- **`Mech_GetTorsoTwistAngle` (`00010e34`)** returns `+0x22e`; **`Mech_GetSpeed` (`00010e48`)** Q10(2000, `+0x224`), with no flyer branch; **`Mech_GetShieldByHeading` (`00010e6c`)**, (machine, bearing), the front `+0x1b8` within ±`0x4000` and the rear `+0x1ba` otherwise; **`Mech_GetOverallDamage` (`00010eb0`)** `0000a07c` of `+0x19c`; **`Mech_GetBodyRadius` (`00010ec8`)** the record's `+0x70`; **`Mech_GetAimPoint` (`00010ee0`)**, (machine, orbit, eye), ES2's two triples from the record's `+0x64`–`+0x6a`. Each is ES2's.
- **`Behaviour_DispatchMove`, `Behaviour_DispatchThink` and `Behaviour_DispatchReassess` (`000112b8`, `000112e0`, `00011308`)** call the move `+0x1c`, think `+0x18` and reassess `+0x20` pointer of the descriptor at `+0x47` with the machine, or answer 0 for a null one, where ES2's call member-function triples.
- **`Mech_MissileLockState` (`00011960`)**, (machine, subtype): the manager's `int16` at `+6` + 2 × subtype, ES2's at `+0xa`.
- **`Mech_GetAimNodeTransform` (`00013204`)**: the 32-byte entry of the shape instance's node array (`+0x16`) at the node index (`+4`) of the record's `+0xc` part, found through the shape's slot `+0x24`, or 0 when the shape has no such part, where ES2's (`00417b98`) answers its fallback transform, as it does for a negative index.
- **`Mech_Draw` (`00012ac4`)**: while `Mech_DrawEnabled` is set, `Mech_SpliceHardpointShapes` (`00002654`)(the root's `CONNECT_INFO` entry, the record's `+0xd4` parts, the manager) for each of the record's roots (`+0xce`, `+0xd2`, [the type record in memory](../formats/chassis-dat.md#the-type-record-in-memory)), then `Shape_DrawAtDetailLevel` (`00002b30`)(machine), ES2's ([drawing](object-classes.md#drawing)).
- **`Mech_ComponentNearestAim` (`00015884`)** is ES2's (`0041b534`) over 28 components, placing each with slot `+0x5c`. **`Mech_ComponentPosition` (`00015990`)**, (machine, out, component), is ES2's ([where a component stands](../../retail/simulation/damage-system.md#where-a-component-stands--the-0x58-slot)) without the null-point test: it always transforms the piece's point, through the part's node when the part id is above 0 and the shape has the part, and through the machine's frame alone otherwise.
- **`Mech_ApplyOutnumberedDamage` (`00015a5c`)**, (machine), is ES2's ([the poll](../../retail/simulation/mission-objectives.md#the-poll--mission_pollstatus-004131ac)) on ES1's fields: `Mission_IsClearOfThreats` (`0000f744`) first; mechs, flyers and bases neither disarmed, immobilized nor destroyed and whose group holds no action, within 45000, 50000 and 35000, the HERC's record `+0x54` or 500, a base only when its record's `+0x34` is not 0; split by the group's side byte `+0x14`; then ES2's 600 and 400 with `Mech_SpreadImpactDamage`(machine, `0x40`, `0x40`). `es2_xref.py` finds two calls, from `Group_ApplyOutnumberedDamage` (`0001d4d0`, [going out of the fight](mission-groups.md#going-out-of-the-fight)) and from `Mech_HandleCommand` through the padding entry `00015a5a`.
- **`Mech_SquadOrderLineIndex` (`00015d70`)**, (machine): ES2's ([descriptor `+0x3c`](../../retail/simulation/ai-dispatch.md#descriptor-layout--0x3e-bytes)) on the descriptor's `int16` `+0x24` and the order `+0x1d2`. `es2_xref.py` finds one call, from `0003df60`.
- **`Mech_IsLocallyPiloted` (`000157dc`)** returns the byte `+0x9d`, signed; `es2_xref.py` finds no branch or stored pointer to it ([Open](#open)).
- **`MechLockTimer_ElementCtor` (`00011904`)** returns its argument, allocating 3 bytes when it is null.

## What ES2's file has that this one lacks

ES2's `Mech_ComponentPresent`, `Mech_NextTargetableComponent`, `Mech_GetCollisionRadius`, `Mech_PlayerFireTick`, `Mech_ApplyTerrainSlopeToSpeed`, `Mech_ComputeShieldCapacity`, `Mech_ComputeReactorRate`, `Razor_MovementTick`, `Mech_ConvergeGunsOnRange` (inlined in the pitch tick), `Player_ResolveTargetAimPoint`, `Mech_GetDisplaySpeedKph` and `Razor_ApplyFlightInput` have no counterpart in this file. Where ES1 fires the player's weapons from is [Open](#open).

## Functions

| ES1 | Address | ES2 |
|---|---|---|
| `HeapPtr_DtorMechCopy`, `ConfigPart_DtorMechCopy`, `ConfigPart_GetClassTagMechCopy`, `TSPartBase_DtorMechCopy` | `00010d60`, `00010d88`, `00010dc7`, `00010dd0` | the run ([where the file starts](#where-the-object-file-starts-and-ends)) |
| `Mech_FlushDeletes`, `Mech_ResetMissionGlobals` | `00010dfc`, `00010e10` | `0041544c`, `00415464` |
| `Mech_GetTorsoTwistAngle`, `Mech_GetSpeed`, `Mech_GetShieldByHeading`, `Mech_GetOverallDamage`, `Mech_GetBodyRadius`, `Mech_GetAimPoint` | `00010e34`, `00010e48`, `00010e6c`, `00010eb0`, `00010ec8`, `00010ee0` | `00415488`, `00415498`, `004154d0`, `00415504`, `00415518`, `004155c4` |
| `Mech_CreditNeutralisedTarget`, `Mech_HandleCommand` | `00010f2c`, `00011050` | `00415710`, `004157c8` |
| `Behaviour_DispatchMove`, `Behaviour_DispatchThink`, `Behaviour_DispatchReassess` | `000112b8`, `000112e0`, `00011308` | `00415afc`, `00415b38`, `00415b74` |
| `Mech_Constructor` | `00011330` | `00415bb0` |
| `TSObj_GetPositionMechCopy`, `SimObject_DtorMechCopy`, `TSObj_ReturnZeroMechCopy`, `TSObj_GetRadiusMechCopy` | `0001185c`, `00011870`, `00011892`, `0001189c` | `SimObject_GetPosition`, —, `Stub_ReturnZero`, `SimObject_GetShapeRadius` |
| `SimObject_SetRunIntoMechCopy`, `SimObject_MissileLockState_AlwaysMechCopy`, `MechLockTimer_ElementCtor`, `Mech_Dtor`, `Mech_MissileLockState` | `000118e4`, `000118fa`, `00011904`, `00011920`, `00011960` | `0042200c`, `00411b04`, —, `00427127`, `004155ac` |
| `FlatObj_DrawMechCopy`, `FlatObj_DtorMechCopy`, `DetAnimObj_DtorMechCopy` | `00011988`, `000119b8`, `00011a0c` | `0040991c`, `0041bd46`, — |
| `TSObj_RenderMechCopy`, `TSObj_DtorMechCopy`, `MatObj_GetPositionMechCopy`, `MatObj_DtorMechCopy`, `Drawable_GetRadiusMechCopy`, `Drawable_DtorMechCopy` | `00011a38`, `00011a58`, `00011aa0`, `00011ab4`, `00011ade`, `00011ae8` | — |
| `Mech_ApplyThrottleInput`, `Mech_AiObstacleAvoidance`, `Mech_LocomotionTick` | `00011afc`, `00011c80`, `00012038` | `004160dc`, `00416274`, `00416a04` |
| `Mech_Draw`, `Shape_StampTransformId`, `Mech_ConfigureLoadout`, `Mech_ApplyFormationOffset` | `00012ac4`, `00012b58`, `00012c00`, `00012ed0` | `004174c8`, `00417530`, `004175dc`, `00417898` |
| `Mech_ApplyStartingCondition`, `Mech_SpreadImpactDamage`, `Mech_AttachToGroup`, `Mech_GetAimNodeTransform` | `00012f20`, `00013078`, `00013124`, `00013204` | `004178e8`, `00417a04`, `00417aa8`, `00417b98` |
| `Mech_ComponentDamageWrite`, `Mech_ApplyExplosiveDamage`, `Mech_DirectFireHitTest`, `Mech_SalvageValue` | `00013250`, `00013a7c`, `00013b44`, `00014034` | `00417de4`, `004187d0`, `00418ba8` with `004188c8`, `00418e60` |
| `Mech_IntegrateMotion`, `Mech_CollisionTest`, `Mech_PlaceLegsOnGround`, `Mech_MovementTick` | `00014148`, `00014174`, `000144dc`, `00014844` | `00418f40`, `00418f74`, `004195c8`, `0041a360` |
| `Mech_TorsoTwistTick`, `Mech_TorsoTwistServoSound`, `Mech_TorsoPitchTick`, `Mech_TorsoPitchServoSound` | `000149ac`, `00014afc`, `00014ba0`, `00014dc4` | `0041a550`, `0041a6d0`, `0041a808` with `0041a74c`, `0041a994` |
| `Mech_IsEcmSwitchedOn`, `Mech_GetEcmPodSwitch`, `Mech_PerTickSystemsUpdate`, `Mech_LockTonePlay` | `00014e68`, `00014eb4`, `00014ee0`, `0001552c` | `0041aa10`, `0041aa44`, `0041aa5c`, `0041b0bc` |
| `Player_PerFrameCockpitUpdate`, `Mech_IsLocallyPiloted`, `Mech_ToggleRadarMode`, `Mech_ReadDamageReadouts`, `Mech_ReadEntryDamage` | `000155a8`, `000157dc`, `000157f4`, `0001583c`, `00015868` | `0041b130`, —, `0041b468`, `0041b4ec`, `0041b514` |
| `Mech_ComponentNearestAim`, `Mech_ComponentPosition`, `Mech_ApplyOutnumberedDamage`, `Mech_SquadOrderLineIndex` | `00015884`, `00015990`, `00015a5c`, `00015d70` | `0041b534`, `0041b60c`, `0041b804`, `0041bac8` |
| `Mech_RegisterSubsystem`, `Mech_StaticDtor` | `00015dfc`, `00015e28` | `0041bc5c`, — |

The movement functions ([movement](mech-locomotion.md)), `Mech_ApplyFormationOffset`, `Mech_SalvageValue` ([what the results read](mission-groups.md#what-the-results-read)), `Mech_IsEcmSwitchedOn`, `Mech_GetEcmPodSwitch` ([the ECM](weapon-mount-classes.md#the-ecm)), `Mech_ReadDamageReadouts` and `Mech_ReadEntryDamage` ([the readouts](damage-readouts.md)) are described where they are linked.

## Globals

| | Address |
|---|---|
| `MechVtable` | `00091ae6` |
| `FlatObjVtableMechCopy`, the shadows' table | `00091b66` |
| `Mech_HandleCommandCodes`, 19 codes and their targets | `000919d0` |
| `Shape_StampTransformIdTags`, ten tags and their targets | `00091c04` |
| `ThrottleLeverMode`, `int16`, 0 in the image, set by preferences option 10 and `Ctrl+1` ([the throttle](mech-locomotion.md#the-throttle--mech_applythrottleinput-00011afc)) | `000919ce` |
| `Mech_DrawEnabled`, `int16` | `000bc428` |
| `DevKeys_MoveStep`, `DevKeys_TurnStep`, `int16` | `000af6f6`, `000af6f8` |
| `g_MechDeleteQueue` | `000af690` |
| `EnergyMeter_Value`, the meter's reading | `000af6fc` |
| `g_CenterTurretMode` | `000b5ba4` |
| `TwistServo_Latch`, `TwistServo_LastAngle`, `PitchServo_Latch`, `PitchServo_LastAngle` | `00091c54`, `00091c56`, `00091c58`, `00091c5a` |
| `LockTone_PhaseSounded`, `LockTone_WasLocked` | `00091c5c`, `00091c5d` |

## Open

- **Open:** what reads slot `+0x74`, `Mech_MissileLockState`: a grep of the disassembly for calls through `+0x74` finds none, and the one call through `+0x70`, in `00007748` at `000077ea`, reaches the stub that answers 1 on a HERC. If nothing else asks, a HERC's missile takes its target whether or not the launcher has lock. A retail check: fire a missile at a HERC the moment it is selected and see whether the round homes.
- **Open:** where ES1 fires the player's weapons and keeps the AI out of the player's line of fire, which ES2's `Mech_PlayerFireTick` does in this file.
- **Open:** whether anything sets the shield capacity after `Shield_Init`: `es2_fieldscan.py` over `00001000`–`0005b000` finds no write of a machine's `+0x1be` (positive control: `+0x228`, written by `Mech_Constructor` and `Mech_PerTickSystemsUpdate`), and the shields' file writes the block's `+6` only in `Shield_Init`; a write through another copy of the block's address is outside that search. A retail check: the shield rings' full extent after the shield generator is shot compared with before.
- **Open:** the callees read for none of this file's functions, at the places ES2's calls the named functions: `00009064` (`Mech_SelectStruckComponent`), `00009f30` (`Component_IsFullyDestroyed`), `00009f7c` (`Component_ApplyDamageAndCascade`), `0000a07c` and `0000a24c` (the readings and the total armor), `0004e05c` (whether a sound is playing) and `00064fe4` (the transform product).
- **Open:** the caller `es2_xref.py` finds and this batch did not read, `0003df60` (the squad line index); the player's turret ticks and throttle are [`Sim_PollPlayerInput`'s](main-loop.md#the-players-controls--sim_pollplayerinput-0005a0c0). Also the dword occurrences of `Mech_ConfigureLoadout`'s address at `00096bcb`, `00096be3` and `000ad900`, of `FlatObj_DrawMechCopy`'s padding at `0006cec9`, of `Drawable_DtorMechCopy`'s at `0003680f` and of `Mech_ComponentNearestAim`'s at `000035f4` and `0003fa4d`, which may be instruction bytes rather than pointers; the one at `0005a308` is the displacement of a `JE` in `Sim_PollPlayerInput`.
- **Open:** what reaches `HeapPtr_DtorMechCopy` and `Mech_IsLocallyPiloted`, to which `es2_xref.py` finds no branch or stored pointer.
- **Open:** whether code outside this file posts a message for a leg, a weapon or the reactor damaged, which ES2's damage write posts and ES1's does not. A retail check: lose a weapon to fire and watch the message line.
- **Open:** what computer messages 3, 7 and 8 say in ES1's `str\system.str` ([the string files](cockpit-messages.md#the-string-files)), and whether a key or the shell reaches `Mech_HandleCommand`'s codes `0x619` and `0x602`–`0x60a` and the four movement codes.
- **Open:** whether anything but `R` and the SCANNER's `MODE` button turns the player's radar on: whether any of the AI's writers of `+0x90`, `Mech_DirectFireHitTest`'s or a call through slot `+0x2c` that the grep for `CALL [reg+0x2c]` misses runs on the player's machine ([a new machine's fields](#construction)). A retail check: at a mission's start, the SCANNER's legend reads a red `PASS`.
- **Open:** what the word `000af6f4` the static initializer zeroes is, and what header word 6 (`000bc4a8`), which with the mode `MissionModeFlag` (`000bc4a0`) at 0 refuses the player's damage, means ([the header](../formats/script-dat.md#the-header)).
- **Open:** a positive control in the running game for the done checklist ([when a module is done](../../herculan/plan-es1-support.md#when-a-module-is-done)). Proposed: disable the player's HERC by its legs with the turret twisted well off center; the view swings to where the turret pointed as the machine stops. And with the turret held against its stop under the stick, a servo sound plays, which ES2 never plays.
