# DBSIM.EXE — `debris.cpp` and the smoke file, the wreckage and smoke

**When a HERC's component, a structure or a flyer is hit or destroyed, ES1 throws wreckage that tumbles, falls, bounces and vanishes once it comes to rest, and a lost component or a collapsed structure can leave a smoke emitter puffing smoke balls.** ES2's counterparts are [destruction effects](../../retail/simulation/destruction-effects.md). What a player would see differently, as far as these files' code decides it ([Open](#open)):

- every piece leaves at the same speed and tumbles about one axis at a fixed rate, where ES2's speed falls with the piece's mass and its spin is drawn per piece;
- a piece never bursts: it vanishes on the bounce that brings it to rest, with no second generation and no effect of its own;
- a component whose `.DMG` flags ask for fire smokes instead ([losing a component](component-damage.md#losing-a-component--component_destroyandcascade-00009cd4)), and a collapsed structure smokes, where ES2 constructs no smoke.

Addresses are `ES1DBSIM` image addresses ([the X-32 executable](../formats/x32-executable.md#the-image)); ES2's are DBSIM's. Every function here removes its own arguments, so argument lists are in stack order ([calling conventions](../formats/compiler.md#calling-conventions)). The groups and the shapes are [`DEBRIS.DAT`](../formats/debris-dat.md).

## Where the object files start and end

**`debris.cpp`** is `00005ebc`–`00006635`. It opens with `DebrisPool_FlushDeletes` (`00005ebc`) after [`explo.cpp`](impact-effects.md#where-the-object-file-starts-and-ends)'s three zero bytes, with no run of `ConfigPart`'s copies; `Debris_LoadResources` pushes the source path `C:\P\M\s\c\debris.cpp` (`000908ce`) at `000060ef`. The last code is the static initializer `Debris_RegisterSubsystem` (`000065f4`), entry `000adbcc` of the static constructor table, and its exit routine `Debris_StaticDtor` (`00006620`), entry `000adc94`, whose `RET` at `00006634` is followed by three zero bytes ([where object files meet](../formats/compiler.md#where-object-files-meet-in-the-code)). Its tables: `DebrisVtable` (`000908e6`) and this file's copies of `TS_OBJ`'s (`00090906`), `MAT_OBJ`'s (`00090926`) and `DRAWABLE`'s (`00090946`).

**The smoke file** is `00006638`–`00006cf5`, with no source path. It opens with `SmokeBall_FlushDeletes` (`00006638`), a frameless function with no Ghidra function, after `debris.cpp`'s zero bytes. The last code is the static initializer **`Smoke_RegisterSubsystem` (`00006c94`)**, entry `000adbd0`, which registers `Smoke_LoadResources` at phase 2 and `Smoke_FlushDeletes` at phase 5 and zeroes the words `000aefd0` and `000af03c` and both pools, and its exit routine **`Smoke_StaticDtor` (`00006ccc`)**, entry `000adc98`, which frees the emitter pool and then the ball pool when each is set. Its `RET` at `00006cf4` is followed by three zero bytes and the ground-shape file ([the ground-shape set](../formats/wld-world.md#the-ground-shape-set)). Its tables: `SmokeBallVtable` (`0009097a`), this file's copies of `TS_OBJ`'s (`0009099a`), `MAT_OBJ`'s (`000909ba`) and `DRAWABLE`'s (`000909da`), and `SmokeVtable` (`000909fa`). Its functions are ES2's from `SmokeBall_FlushDeletes` (`00408f84`) to `Smoke_RegisterSubsystem` (`0040948c`), with each class's destructor and table copies after its constructor where ES2's follow the static initializer.

## Debris

`Debris_ThrowGroup` (`00005fb8`) draws the pieces of a group ([throwing a group](../formats/debris-dat.md#throwing-a-group)) and throws each with `Debris_Construct` and `Debris_ThrowRandomBearing`.

### The piece — `Debris_Construct` (`00006168`)

(piece, point, root): with a null piece it takes one from `g_DebrisPool` (`000aeef8`, 150 of `0x4e` bytes) and returns 0 when that gives none. It runs `TSObj_Ctor` (`00001b04`) with the root, stores `DebrisVtable` at `+2` and the type tag 1 at `+0`, copies the point to the position `+0x20`, and zeroes the detail bias `+0x4a`. It writes no velocity. ES2's `Debris_Construct` (`00408940`) also stores a child group, a death effect, a burst countdown and the database ([the piece](../../retail/simulation/destruction-effects.md#the-piece--debris_tickupdate-00408bd8)); ES1's piece has none of them.

| Offset | Meaning |
|---|---|
| `+0x06`, `+0x08`, `+0x0a` | the `TS_OBJ`'s angles; the tick turns `+0x06` |
| `+0x20` | position, three `int32` |
| `+0x2c` | the orientation's cached-rotation word, zeroed when the angles change |
| `+0x44` | velocity, three `int16`, per tick |
| `+0x4a` | the detail bias `Debris_Draw` (`000065c4`) sets around the draw |

Neither caller tests for a null piece: `Debris_ThrowGroup` passes the answer to `Debris_ThrowRandomBearing` and `WeaponMount_Destroy` stores the bias and launches it, so with all 150 pieces in flight a throw writes through a null pointer.

### The launch — `Debris_Launch` (`00006378`)

(piece, heading, pitch): sets the angles to (0, 0, heading), zeroes `+0x2c`, and gives the piece a speed of 400:

```
horizontal = Math_Q14Multiply(400, Math_CosBam(pitch))
vx = Math_Q14Multiply(horizontal, Math_CosBam(heading + 0x4000))
vy = Math_Q14Multiply(horizontal, Math_CosBam(heading))
vz = Math_Q14Multiply(400, Math_CosBam(pitch - 0x4000))
```

ES2's `Debris_Launch` (`004089e4`) takes the speed from a scale and the piece's mass, adds a carrier velocity and draws a spin.

**`Debris_ThrowRandomBearing` (`00006480`)**, (piece), launches it with a pitch of `Math_RandomBelow`(`000b5bd4`, 24000) − 10000, as an `int16`, drawn first, and then a heading of `Math_RandomNext`(`000b5bd4`): a pitch from about 55° down to 77° up, so a piece can leave downward. ES2 draws the pitch in `Debris_ThrowAtBearing` (`00408b74`) between two globals.

### The tick — `Debris_TickUpdate` (`000064b8`)

(piece), which `Sim_MainTick` calls on each piece ([the tick](main-loop.md#the-tick--sim_maintick-00059700)), in fixed steps whatever the tick's length:

1. the angle `+0x06` turned by 1500;
2. gravity: `vz` −= 50;
3. drag: `vx` −= `Math_Q10Multiply`(50, `vx`), and the same for `vy`;
4. the move: the position plus the velocity as it now stands;
5. the ground, flat at height 0 ([the ground](external-views.md)): with the new Z below 0, Z becomes 0 and `vz` becomes −`Math_Q10Multiply`(450, `vz`), and when that rebound is under 100 the tick answers 1;
6. the position and angles stored back, `+0x2c` zeroed, and the answer 0 otherwise.

A tick that answers 1 ends the piece: `Sim_MainTick` destroys it through slot `+0xc` with 3. So a piece bounces until a rebound comes out under 100 and vanishes on that bounce. ES2's tick ([the piece](../../retail/simulation/destruction-effects.md#the-piece--debris_tickupdate-00408bd8)) integrates its rates over the tick, moves by the average of the old and new speed, bounces off the terrain plus a share of the shape's radius, and bursts a piece with a child group.

### Drawing and deleting

`ObjList_RebuildFrame` adds every live piece to the frame's object list ([the object list](../rendering/polygon-shading.md#the-object-list)), which draws it through `Debris_Draw` ([the detail setting](object-classes.md#the-detail-setting--shapedetail_applydetailsetting-00057c08)). **`Debris_Destruct` (`00006210`)**, (piece, flags), slot `+0xc` of `DebrisVtable`, with the piece non-null stores `DebrisVtable` and `00090906`, destroys the shape instance through its slot `+0x1c` with 3, stores `00090926` and `00090946`, and on flags bit 0 queues the piece on `g_DebrisDeleteQueue` (`000aeefc`), which `DebrisPool_FlushDeletes` empties at phase 5 ([the load](../formats/debris-dat.md#the-load)).

### Where pieces are thrown

`es2_xref.py` finds five calls of `Debris_ThrowGroup`, each with the shapes `g_DebrisShapes` (`000bc1c6`), and two calls of `Debris_Construct`:

| Call | In | Group | From |
|---|---|---|---|
| `00003a28` | `Base_ApplyDamage` ([the structures' tests](terrain.md#the-structures-tests)), the structure destroyed | 3 | its position |
| `00003c71` | the structure's hit test, `00003a58`, after a hit on a component | 1 | the hit point |
| `00009e7d` | `Component_DestroyAndCascade` ([losing a component](component-damage.md#losing-a-component--component_destroyandcascade-00009cd4)) | 3 | the anchor |
| `00014021` | `Mech_DirectFireHitTest` ([the hit test](mech.md#the-hit-test--mech_directfirehittest-00013b44)) | 1 or 2 | the hit point |
| `0001b4fb` | the flyer's hit test, `Flyer_DirectFireHitTest` (`0001b370`), after a hit on a component | 1, or 3 when the hit destroys the flyer (`Flyer_ComponentDamageWrite` (`0001b290`) answers 1, [hits and death](flyers.md#hits-and-death)) | the hit point |
| `00006058` | `Debris_ThrowGroup` | | |
| `0000ba90` | `WeaponMount_Destroy` ([losing a mount](weapon-mount-classes.md#losing-a-mount)), the thrown gun | | the mount point |

**The thrown gun leaves the mount turned as it was on the machine, heading away from the machine's aim point.** `WeaponMount_Destroy` (`0000b968`) first works out the mount point: the hardpoint bone's node transform (its part found through slot `+0x24` of the machine's shape, a null transform for a part the shape lacks) concatenated with the machine's frame (`Context_GetTransform` of the machine's `+6`) by `Transform_Concat`, and `WeaponMount_MuzzleOffset` (`0000b918`) put through that with `Transform_ApplyToShortPoint`. To throw the gun it:

1. takes the translation `+0x14` of the transform the machine's slot `+0x28` answers (`Mech_GetAimNodeTransform`, `00013204`, on a HERC, read without a test for 0) through the machine's frame, and `Math_EulerToward` (`00063d2c`) from that point to the mount point;
2. builds the piece with `Debris_Construct`(null, the mount point, the gun's root of `WeaponModels_Mechwpn2Roots`) and stores `g_TSDetailBiasForMounts` (`000bc2b8`) at its `+0x4a`;
3. launches it with `Debris_Launch`(piece, the bearing's third angle, the hardpoint's dword `+0x18`);
4. overwrites the angles `Debris_Launch` set, `+6` to `+0xb`, with `Transform_RotationToEuler` (`0005e988`) of the bone-and-frame rotation, and zeroes `+0x2c`.

So the piece's speed and direction are the launch's, its starting orientation the mount's. It then turns, moves and ends as any piece does.

## Smoke

**A smoke emitter is ES2's ([smoke](../../retail/simulation/destruction-effects.md#smoke)), at ES2's offsets less 6, and ES1 lights one.** Its pool holds 20 emitters of `0x24` bytes and the ball pool 200 balls of `0x46` bytes.

### Resources — `Smoke_LoadResources` (`000066ec`)

No arguments, a phase-2 loader with no Ghidra function: `g_SmokePool` (`000aefd4`) = `Pool_Init`(0, `0x24`, 20), then `SmokeBall_LoadResources` (`00006664`): `g_SmokeBallPool` (`000aef68`) = `Pool_Init`(0, `0x46`, 200); `Shape_LoadAllRoots`(0, `0x140008`, `dts\smoke`, `g_SmokeShapes` (`000bc1d0`)); `g_SmokeTextures` (`000bc1d4`) = `ClassItem_LoadFromFile` of `dba\smoke`; and that bank in `+0x26` of each root. ES1's pools have static delete queues where ES2's builds them. `Smoke_FlushDeletes` (`0000664c`), at phase 5, runs `ObjectPool_FlushDeleteQueue` on `g_SmokeDeleteQueue` (`000aefd8`) and then `SmokeBall_FlushDeletes`, the same on `g_SmokeBallDeleteQueue` (`000aef6c`).

### The emitter — `Smoke_Construct` (`000069ec`)

(smoke, kind, point, part id, owner): with a null emitter it takes one from `g_SmokePool` and returns 0 when that gives none. It stores `SmokeVtable`, the kind at `+0x22` and the count `Smoke_PuffCountByKind` (`00090964`)[kind] at `+0x1e`, `{200, 500, 1000}` as ES2's, zeroes `+0x06`–`+0x0d` and the countdown's `int16` `+0x1c`, stores the owner at `+0x0f`, the part id at `+0x13` and the first shape kind × 4 + (`Math_RandomNext`(`000b5c48`) & 3) at `+0x20`, and copies the point's three `int16` to `+0x15`, or with a null point sets `+0x06`.

### The tick — `Smoke_Tick` (`00006b28`)

(smoke), which `Sim_MainTick` calls on each emitter: ES2's (`004092dc`), its countdown the record `+0x1b` and its count `+0x1e`, with these differences. The part's node is found through slot `+0x24` of the owner's shape, `TSShape`'s part search; for a part the shape lacks the node transform is null and is read through, where ES2's substitutes a fixed transform, and a part with a negative node indexes before the instance's array. Each puff's point, by the part id `+0x13` and the no-point byte `+6`:

| Part id | A point given (`+6` clear) | No point (`+6` set) |
|---|---|---|
| 0 or more | the stored point `+0x15` through the node's transform (`Transform_ApplyToShortPoint`), then the owner's frame | the node's translation `+0x14`, then the owner's frame |
| negative | the stored point, then the owner's frame | the owner's position `+0x20` |

The owner's frame is `Context_GetTransform` of the owner's `+6`, applied with `Transform_ApplyToPoint`. The ball is built by `SmokeBall_Construct`(null, the shape `+0x20`, the point), which takes it from the pool itself, and `+0x20` then steps to kind × 4 + ((`+0x20` + 1) & 3). The answer is 1 once no puffs are left, and `Sim_MainTick` then destroys the emitter through slot `+0xc` with 3: **`Smoke_Dtor` (`00006af8`)**, which stores `SmokeVtable` and `000909da` and on flags bit 0 queues the emitter on `g_SmokeDeleteQueue`. Its slot `+4`, `Smoke_DrawNoOp` (`00006ae9`), draws nothing, and `ObjList_RebuildFrame` does not add the emitters to the frame.

### The ball — `SmokeBall_Construct` (`00006704`)

(ball, shape, point): with a null ball it takes one from `g_SmokeBallPool` and returns 0 when that gives none. It runs `TSObj_Ctor` with root *shape* of `g_SmokeShapes`, stores `SmokeBallVtable` and the type tag 4, copies the point to the position `+0x20`, and sets the countdown's `int32` `+0x42` to 200. It writes no velocity at `+0x3b`. The pool's block is zero-filled by the heap when `Pool_Init` (`00063878`) allocates it ([object pools](memory.md#object-pools)), and the pool holds only balls, so a ball's velocity is 0 unless other code stores one ([Open](#open)).

Its tick, **`Effect_DriftAndAnimate` (`0000693c`)**, from `Sim_MainTick`, is ES2's (`004091cc`): the three `int16` at `+0x3b` added to the position, and on each expiry of `Timer_CountDown` (`00064178`) of `+0x41` the countdown reloaded with 200 and the instance's sequence 0 stepped to its next cell; it answers 1 when the cell wraps to 0. **`SmokeBall_Dtor` (`000067f4`)**, slot `+0xc` of `SmokeBallVtable`, destroys the shape instance and on flags bit 0 queues the ball on `g_SmokeBallDeleteQueue`. `ObjList_RebuildFrame` adds every live ball to the frame, drawn through `TSObj_RenderSmokeCopy` (`000067c0`).

### Where smoke is lit

`es2_xref.py` finds two calls of `Smoke_Construct`:

- `Component_DestroyAndCascade`, for a piece whose sequence byte `+3` is not negative, with the cascade's latch clear and the flags' bit 1 set, before the cascade's cell write, explosion and debris: kind 0, the cluster's bound center, the node's part id and the machine ([losing a component](component-damage.md#losing-a-component--component_destroyandcascade-00009cd4)). The emitter puffs 200 balls from that point on the part as the machine moves.
- the structure's collapse, `00002df8`, when its count `+0x1a0` reaches 0, after it puts the type's hulk in the shape instance: kind 1, the type record's point `+0x0c`, part id −1 and the structure. The emitter puffs 500 balls from that point through the structure's frame.

## Functions

| ES1 | Address | ES2 |
|---|---|---|
| `DebrisPool_FlushDeletes`, `Debris_LoadPieceList`, `Debris_ThrowGroup`, `Debris_LoadResources` | `00005ebc`, `00005ed0`, `00005fb8`, `0000607c` | [`DEBRIS.DAT`](../formats/debris-dat.md) |
| `Debris_Construct`, `Debris_Destruct`, `Debris_Launch`, `Debris_ThrowRandomBearing`, `Debris_TickUpdate`, `Debris_Draw` | `00006168`, `00006210`, `00006378`, `00006480`, `000064b8`, `000065c4` | `00408940`; —; `004089e4`; `00408bb4` with `00408b74`; `00408bd8`; `00408e6c` |
| `TSObj_GetPositionDebrisCopy`, `TSObj_ReturnZeroDebrisCopy`, `TSObj_GetRadiusDebrisCopy`, `TSObj_RenderDebrisCopy`, `TSObj_DtorDebrisCopy` | `000061fc`, `00006264`, `0000626c`, `000062b4`, `000062d4` | the object classes' copies |
| `MatObj_GetPositionDebrisCopy`, `MatObj_DtorDebrisCopy`, `Drawable_GetRadiusDebrisCopy`, `Drawable_DtorDebrisCopy` | `0000631c`, `00006330`, `0000635a`, `00006364` | the object classes' copies |
| `Debris_RegisterSubsystem`, `Debris_StaticDtor` | `000065f4`, `00006620` | |
| `SmokeBall_FlushDeletes`, `Smoke_FlushDeletes`, `SmokeBall_LoadResources`, `Smoke_LoadResources` | `00006638`, `0000664c`, `00006664`, `000066ec` | `00408f84`, `00408f9c`, `00408fb8`, `004090cc` |
| `SmokeBall_Construct`, `SmokeBall_Dtor`, `Effect_DriftAndAnimate`, `Smoke_Construct`, `Smoke_Dtor`, `Smoke_Tick` | `00006704`, `000067f4`, `0000693c`, `000069ec`, `00006af8`, `00006b28` | `00409178`, `0040970d`, `004091cc`, `0040923c`, `004096e3`, `004092dc` |
| `TSObj_RenderSmokeCopy`, `TSObj_GetPositionSmokeCopy`, `TSObj_ReturnZeroSmokeCopy`, `TSObj_GetRadiusSmokeCopy`, `TSObj_DtorSmokeCopy` | `000067c0`, `000067e0`, `00006848`, `00006850`, `00006898` | the object classes' copies |
| `MatObj_GetPositionSmokeCopy`, `MatObj_DtorSmokeCopy`, `Drawable_GetRadiusSmokeCopy`, `Drawable_DtorSmokeCopy` | `000068e0`, `000068f4`, `0000691e`, `00006928` | the object classes' copies |
| `Smoke_DrawNoOp`, `Smoke_GetPositionNull` | `00006ae9`, `00006aef` | `004096d7`, `004096dc` |
| `Smoke_RegisterSubsystem`, `Smoke_StaticDtor` | `00006c94`, `00006ccc` | |

## Globals

| | Address |
|---|---|
| `g_DebrisPool`, `g_DebrisDeleteQueue` | `000aeef8`, `000aeefc` |
| `g_DebrisGroups`, the group count, `g_DebrisShapes`, the shape count | `000bc1c0`, `000bc1c4`, `000bc1c6`, `000bc1ca` |
| `g_SmokeBallPool`, `g_SmokeBallDeleteQueue`, `g_SmokePool`, `g_SmokeDeleteQueue` | `000aef68`, `000aef6c`, `000aefd4`, `000aefd8` |
| `g_SmokeShapes`, `g_SmokeTextures`, `Smoke_PuffCountByKind` | `000bc1d0`, `000bc1d4`, `00090964` |
| `DebrisVtable`, `SmokeBallVtable`, `SmokeVtable` | `000908e6`, `0009097a`, `000909fa` |

## Open

- **Open:** whether a smoke ball moves. `SmokeBall_Construct` writes no velocity and the pool starts zeroed, so `Effect_DriftAndAnimate` adds 0 unless other code stores to `+0x3b`–`+0x40`. `es2_fieldscan.py` over `00005734`–`00006d9b` finds no store to `+0x3b`, `+0x3d` or `+0x3f` but `Explosion_Construct`'s byte, an effect's field, and `es2_xref.py --decode` finds the ball pool read by `SmokeBall_Construct`, `Sim_MainTick` and `ObjList_RebuildFrame` alone. A retail check: destroy an enemy HERC's arm and watch whether its smoke rises, drifts or stands in place.
- **Open:** the rest of the structure's collapse `00002df8` after its effect calls (from `00002f98`).
- **Open:** what the words `000aefd0` and `000af03c` the smoke file's static initializer zeroes are; they lie `0x64` past each delete queue's start, beyond the count `+0x50` `ObjectPool_QueueForDelete` keeps.
- **Open:** a positive control in the running game for the done checklist ([when a module is done](../../herculan/plan-es1-support.md#when-a-module-is-done)). Proposed: shoot an enemy HERC until a component goes; the pieces bounce and vanish on the flat ground, none bursts; `BP <CS>:00006b28` fires once a tick while smoke is lit, `BP <CS>:00006704` once per ball.
