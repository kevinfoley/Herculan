# DBSIM.EXE — `explo.cpp`, the impact effects

**ES1's impact effects are ES2's ([ES2's impact effects](../../retail/simulation/impact-effects.md)): a flipbook that plays once where a shot lands or a part comes down, with a light and a ground mark for some types.** What a player would see differently, as far as this file's code decides it ([Open](#open)):

- an effect's light goes out the tick the effect ends, where ES2's stays lit one more frame ([the end](#the-end--explosion_destruct-00005940));
- every effect plays its sound, the terrain hit's included, where ES2's terrain hit is silent ([construction](#construction--explosion_construct-00005af0));
- the effects on the HERC the camera rides in are drawn like any other, since an effect has no owner ([drawing](#drawing)).

Addresses are `ES1DBSIM` image addresses ([the X-32 executable](../formats/x32-executable.md#the-image)); ES2's are DBSIM's. Every function here removes its own arguments, so argument lists are in stack order ([calling conventions](../formats/compiler.md#calling-conventions)). The type table, the banks and the shapes are [`EXPLOS.DAT`](../formats/explos-dat.md).

## Where the object file starts and ends

The file's code is `00005734`–`00005eb9`. It opens with `Explosion_GetTypeRecord` (`00005734`), after the light manager's exit routine `LightManager_StaticDtor`, whose `RET` at `00005730` is followed by three zero bytes ([where object files meet](../formats/compiler.md#where-object-files-meet-in-the-code)); it has no run of `ConfigPart`'s copies. `Explosion_LoadResources` pushes the source path `C:\P\M\s\c\explo.cpp` (`00090804`) at `0000580f`. The last code is the static initializer **`Explosion_RegisterSubsystem` (`00005e78`)**, entry `000adbc8` of the static constructor table, which registers `Explosion_LoadResources` at subsystem phase 2 and `ExplosionPool_FlushDeletes` at phase 5 with `RegisterSubsystemLoader` (`00000ac4`) and zeroes the word `000aeef0` and `g_ExplosionPool`, and its exit routine **`Explosion_StaticDtor` (`00005ea4`)**, entry `000adc90`, which frees the pool when it is set. Its `RET` at `00005eb8` is followed by three zero bytes and [`debris.cpp`](destruction-effects.md#where-the-object-files-start-and-end).

Its tables follow the name template and the source path: `ExplosionVtable` (`00090822`), this file's copies of `TS_OBJ`'s (`00090842`), `MAT_OBJ`'s (`00090862`) and `DRAWABLE`'s (`00090882`) tables, and `FlatObjVtableExploCopy` (`000908a2`), the ground shape's.

## The effect

An effect is a `0x51`-byte `TS_OBJ` from `g_ExplosionPool` (`000aee88`, 40 effects):

| Offset | Meaning |
|---|---|
| `+0x00` | type tag: 2, or 8 when the row's `+0x2a` is not 0 |
| `+0x02` | `ExplosionVtable` |
| `+0x20` | position, three `int32`, the `TS_OBJ`'s |
| `+0x2e` | the shape instance |
| `+0x3b` | the type id, a byte |
| `+0x44` | the frame countdown, its `int16` at `+0x45` |
| `+0x49` | the ground shape, or 0 |
| `+0x4d` | the light handle, or 0 |

## Construction — `Explosion_Construct` (`00005af0`)

(effect, point, type id): [ES2's](../../retail/simulation/impact-effects.md#construction--explosion_construct-00407f1c) with these differences. When the effect is null it allocates one from the pool and returns 0 when the pool is full. It takes the root at row `+0x00`, stores the type id at `+0x3b`, sets the type tag to 2 when `+0x2a` is 0 and 8 when not, loads the frame countdown at `+0x45` from `+0x06`, and when `+0x02` is not negative zeroes that sequence's frame counter. When `+0x08` is non-zero it builds a ground shape from the pool `g_FlatObjPool` (`000af044`) on root 1 of `g_FlatShapes` (`000bc1d8`) with `FlatObjVtableExploCopy`, at the point, into `+0x49`. When `+0x0a` is non-zero it calls `EffectLight_Construct` (`000054b0`) with 0, the dword at `+0x0c` and the point, and keeps the handle it returns at `+0x4d`: a light at the point whose intensity is the dword's low 16 bits, the ramp's first step ([effect lights](../rendering/polygon-shading.md#effect-lights)). It always plays sound `+0x28` + 10 at the point, where ES2's plays it only when its caller asks and the id is not negative. It records no owner.

## The tick — `Explosion_TickUpdate` (`00005d74`)

(effect), which `Sim_MainTick` calls on each effect ([the tick](main-loop.md#the-tick--sim_maintick-00059700)): [ES2's](../../retail/simulation/impact-effects.md#tick--explosion_tickupdate-0040813c), reading its own row's offsets. When `Math_CountdownTimerTick` (`0006410c`) of `+0x44` has not reached 0 it answers 0. Otherwise:

1. with the row's sequence `+0x02` negative it answers 1, so an effect with no flipbook ends at its first step (row 6);
2. it steps that sequence's frame counter in the instance's array `+8` on by one, modulo the shape's frame count for the sequence (`+0x20` of the instance's shape); a frame of 0 answers 1, so the flipbook plays once;
3. with a light, `EffectLight_SetIntensity` (`00005578`) with the low byte of the row's `int16` at `+0x0c` + 2 × frame, the ramp's step for the frame; a flipbook of more than twelve frames reads on into the radius `+0x24`, the sound `+0x28` and the class `+0x2a`;
4. with a ground shape, its instance's sequence 0 stepped on modulo its frame count;
5. the countdown's `int16` `+0x45` reloaded from the row's `+0x06`, and the answer 0.

## The end — `Explosion_Destruct` (`00005940`)

(effect, flags), slot `+0xc` of `ExplosionVtable`, which `Sim_MainTick` calls with 3 on an effect whose tick answers 1. With the effect non-null it stores `ExplosionVtable`, destroys the ground shape and then the light through slot `+0xc` of each with 3, stores `00090842`, destroys the shape instance through its slot `+0x1c` with 3, stores `00090862` and `00090882`, and on flags bit 0 queues the effect on `g_ExplosionDeleteQueue` (`000aee8c`). ES2's (`00407e48`) queues the ground shape and the light on their pools' queues instead of destroying them. The light's destructor, `EffectLight_Destruct` (`00005534`), releases its manager slot there and then, so the light is out from the next draw; the ground shape's, `FlatObj_DtorExploCopy` (`00005cd8`), the same code as `FlatObj_DtorMechCopy` (`000119b8`), destroys its instance and queues it on `g_FlatObjDeleteQueue` (`000af048`).

`ExplosionPool_FlushDeletes` (`00005750`), at phase 5, runs `ObjectPool_FlushDeleteQueue` (`00063784`) on the queue, which calls each queued effect's slot `+0xc` with 2, by then `Drawable_DtorExploCopy` (`00005adc`), and returns it to the pool. The ground shape goes back through `FlatObjPool_FlushDeletes` (`00006d50`), the ground-shape file's phase-5 function ([the ground-shape set](../formats/wld-world.md#the-ground-shape-set)). `Sim_StartMission` empties the pool before the first frame ([the mission start](main-loop.md#the-mission-start--sim_startmission-0005a9dc)).

## Drawing

`ObjList_RebuildFrame` adds every live effect to the frame's object list ([the object list](../rendering/polygon-shading.md#the-object-list)), and the list draws each through slot `+4`, `TSObj_RenderExploCopy` (`000059c8`), `TS_OBJ`'s draw, after picking its lights by its position and radius. ES2's skips an effect on the HERC the camera rides in; ES1's effect has no owner, and the list leaves out only the object the camera rides in itself. The ground shape is drawn with the other ground shapes within 30000 of the camera ([the frame's draw](../rendering/polygon-shading.md#the-frames-draw)), through `FlatObj_DrawExploCopy` (`00005ca8`), the same code as `FlatObj_DrawMechCopy` (`00011988`): its Z set to 0, then its shape.

The missile camera's strike flash is the first effect it finds within reach of where the missile was last seen, and lasts while that effect is in the pool ([painting](mfd-missile-camera.md#painting)).

## The proximity test — `Explosion_ProximityTest` (`00005d2c`)

(effect, point): 1 when the row's light mode `+0x0a` is 2 and `Math_DistanceBetweenPoints` (`00063c20`) from the point to the effect's position is under the row's `int32` `+0x24`, otherwise 0. ES2's (`00408100`). `es2_xref.py` finds no reference to it ([Open](#open)).

## Where effects are spawned

`es2_xref.py` finds seven calls of `Explosion_Construct`, each with a null effect. An entry *n* of a shot's effect table is the `int16` at the shot's `+0xa` + 2*n*, as in ES2 ([which effect a shot spawns](../../retail/simulation/impact-effects.md#which-effect-a-shot-spawns)):

| Call | In | Type | Where |
|---|---|---|---|
| `00002eb1` | the structure's collapse, `00002df8`, its count `+0x1a0` reaching 1 | `0x10` | the type record's point `+0x0c` through the structure's frame |
| `00002f80` | the same, the count reaching 2 to 7 | `0xf` | a random point: on each axis `Math_RandomBelow`(2*e*) − *e* + *c*, *e* the type record's `+0x18`, `+0x1a`, `+0x1c` and *c* its `+0x12`, `+0x14`, `+0x16`, through the structure's frame |
| `00003c59` | the structure's hit test, `00003a58`, after a hit on a component, whether the structure is destroyed or not | entry 8 + (`Math_RandomNext` & 3) | the hit point |
| `00009e65` | `Component_DestroyAndCascade` ([losing a component](component-damage.md#losing-a-component--component_destroyandcascade-00009cd4)) | 10, or `0x11` | the anchor |
| `00013ffb` | `Mech_DirectFireHitTest` ([the hit test](mech.md#the-hit-test--mech_directfirehittest-00013b44)) | entry 4*n* + (random & 3) | the hit point |
| `0001b4dc` | the flyer's hit test, `0001b370` | entry 8 + (`Math_RandomNext` & 3), or 10 when `0001b290` answers non-zero | the hit point |
| `0001f62d` | `Sim_RaycastObjectList` ([shots against objects](world.md#shots-against-objects--sim_raycastobjectlist-0001f1d0)) | entry 4 + (`Math_RandomNext` & 3) | the ray's end, its Z raised to 0 |

The structure's collapse reloads its timer `+0x19d` with 2000 after type `0x10` and 300 after type `0xf`; with the count at 0 it lights [smoke](destruction-effects.md#smoke) instead.

## Functions

| ES1 | Address | ES2 |
|---|---|---|
| `Explosion_GetTypeRecord`, `ExplosionPool_FlushDeletes`, `Explosion_LoadResources` | `00005734`, `00005750`, `00005764` | `00407b54` for the loader ([`EXPLOS.DAT`](../formats/explos-dat.md)) |
| `Explosion_Destruct`, `Explosion_Construct`, `Explosion_ProximityTest`, `Explosion_TickUpdate` | `00005940`, `00005af0`, `00005d2c`, `00005d74` | `00407e48`, `00407f1c`, `00408100`, `0040813c` |
| `TSObj_RenderExploCopy`, `TSObj_GetPositionExploCopy`, `TSObj_ReturnZeroExploCopy`, `TSObj_GetRadiusExploCopy`, `TSObj_DtorExploCopy` | `000059c8`, `000059e8`, `000059fc`, `00005a04`, `00005a4c` | the object classes' copies |
| `MatObj_GetPositionExploCopy`, `MatObj_DtorExploCopy`, `Drawable_GetRadiusExploCopy`, `Drawable_DtorExploCopy` | `00005a94`, `00005aa8`, `00005ad2`, `00005adc` | the object classes' copies |
| `FlatObj_DrawExploCopy`, `FlatObj_DtorExploCopy` | `00005ca8`, `00005cd8` | `FlatObj_Draw` (`0040991c`), `FlatObj_Destruct` (`0041bd46`) |
| `Explosion_RegisterSubsystem`, `Explosion_StaticDtor` | `00005e78`, `00005ea4` | |

## Globals

| | Address |
|---|---|
| `g_ExplosionPool`, `g_ExplosionDeleteQueue` | `000aee88`, `000aee8c` |
| `ExplosionVtable`, `FlatObjVtableExploCopy` | `00090822`, `000908a2` |

## Open

- **Open:** whether anything calls `Explosion_ProximityTest`: `es2_xref.py` finds no branch, stored pointer or vtable slot reaching it or its padding.
- **Open:** what `0001b290` answers for the flyer's hit test, under which a hit spawns type 10 and debris group 3, and what the word `000aeef0` the static initializer zeroes is.
- **Open:** a positive control in the running game for the done checklist ([when a module is done](../../herculan/plan-es1-support.md#when-a-module-is-done)). Proposed: shoot the ground beside a HERC and listen for the impact's sound, which ES2 leaves silent; `BP <CS>:00005d74` fires once per tick per live effect.
