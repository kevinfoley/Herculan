# DBSIM.EXE — the projectiles: the pool, gun rounds, the grenade and beams

**ES1's guns fire rounds that cross the ground over several ticks and its beams strike at once and leave a flash for one frame, as ES2's do ([ES2's travelling rounds](../../retail/simulation/projectiles.md), [ES2's beams](../../retail/simulation/beam-visuals.md)); its missiles are [`rocket.cpp`](rockets.md).** What a player would see differently, as far as these files' code decides it ([Open](#open)):

- a gun round flies straight from the muzzle at its weapon's speed, with no scatter and nothing added for the firing HERC's own speed;
- how far a gun round flies depends on how long the simulation's ticks are, since its life counts ticks while each step scales with the tick;
- a laser or particle beam is two flat-colored strips fixed in the world, not turned to the camera: one upright from the muzzle to where the beam stops, and one that twists from a level span across the world's X axis at the muzzle to the first strip's upright span at the far end, so the beam is a cross at the muzzle and a single upright span at its end; their width is the weapon's in world units, so a beam thins with distance, where ES2's is a textured ribbon turned to face the camera and never drawn thinner than four pixels; the color is the weapon's, 88 for the lasers and 10 for the particle beams, where every straight ES2 beam draws the same orange and white;
- a beam is drawn as one piece from the muzzle to where it stops, where ES2's is cut into 5000-unit pieces;
- the grenade launcher, `GREN`, spends a round on each throw and launches nothing ([the grenade](#the-grenade)); no retail HERC carries it and the armory never offers it ([cut content](../cut-content.md#weapons)).

Addresses are `ES1DBSIM` image addresses ([the X-32 executable](../formats/x32-executable.md#the-image)); ES2's are DBSIM's. Every function here removes its own arguments, so argument lists are in stack order ([calling conventions](../formats/compiler.md#calling-conventions)). The mounts' fire dispatches that call these files' shot functions are in [the mount classes](weapon-mount-classes.md#firing).

## Where the object files start and end

Four object files and a fifth, `rocket.cpp`, between them ([where object files meet](../formats/compiler.md#where-object-files-meet-in-the-code)):

| File | Code | Opens with | Ends with |
|---|---|---|---|
| the projectile pool | `00006d9c`–`00006dfb` | `Projectile_LoadResources`, after the three zero bytes that follow the `RET` at `00006d98` of the exit routine `00006d84` of the file holding `FlatObj_LoadResources` (`00006cf8`) | `Projectile_StaticDtor`'s `RET` at `00006dfb`, on a 4-byte boundary |
| [`rocket.cpp`](rockets.md#where-the-object-file-starts-and-ends) | `00006dfc`–`00007879` | a run of `ConfigPart`'s copies | `Rocket_RegisterSubsystem` |
| the grenade | `0000787c`–`00007b69` | a lone 40-byte destructor, `HeapPtr_DtorGrenadeCopy` | `Grenade_RegisterSubsystem`, its `RET` at `00007b68` and three zero bytes |
| `bullet.cpp` | `00007b6c`–`00008205` | a lone 40-byte destructor, `HeapPtr_DtorBulletCopy` | `Bullet_RegisterSubsystem`, its `RET` at `00008204` and three zero bytes |
| the beams | `00008208`–`000089d1` | a lone 40-byte destructor, `HeapPtr_DtorBeamCopy` | `Beam_RegisterSubsystem`, its `RET` at `000089d0` and three zero bytes |

The pool's file has no copy of the 40-byte destructor and no `ConfigPart` run; the zero fill before it and the run at `00006dfc` after it bound it, and its static initializer and exit routine are its last code. Each file's static initializer is its last code but for the pool's, whose exit routine follows it; their entries in the static constructor table are `000adbd8` to `000adbe8`, one to each file in address order, and `collide.cpp`'s run follows the beams' file at `000089d4`. `bullet.cpp`'s source path (`00090c08`) is pushed at `00007be6`, in `Bullet_LoadResources`; `es1_boilerplate.py bounds` finds a source path pushed in no other of the four. Each file but the pool's has its own copies of `TS_OBJ`'s, `MAT_OBJ`'s and `DRAWABLE`'s functions and tables, and its own copy of the projectile base's destructor. The files' tables follow one another from `00090a26`: `rocket.cpp`'s to `00090b56`, the grenade's from `00090b5a`, `bullet.cpp`'s with its names and source path from `00090c00`, and the beams' from `00090cd8`, up to `collide.cpp`'s source path at `00090da4`.

## The projectile pool

ES2's projectile module ([the pool](../../retail/simulation/projectiles.md)), without `Bullet_Draw`:

- **`Projectile_LoadResources` (`00006d9c`)**, phase 2: `Pool_Init`(0, `0x60`, `0xa0`), 160 elements of `0x60` bytes, into `g_ProjectilePool` (`000af0b4`). ES2's elements are `0x66` bytes, and its loader also builds the delete queue.
- **`Projectile_FlushDeletes` (`00006db0`)**, phase 5, at the start of each `Sim_RenderFrame` ([the frame](main-loop.md)): `ObjectPool_FlushDeleteQueue`(`g_ProjectileDeleteQueue` (`000af0b8`), `g_ProjectilePool`), which destroys each queued projectile through its slot `+0xc` with flags 2 and returns it to the pool.
- **`Projectile_RegisterSubsystem` (`00006dc4`)**, the static initializer: `RegisterSubsystemLoader`(2, `Projectile_LoadResources`) and (5, `Projectile_FlushDeletes`), then the word `000af11c` and `g_ProjectilePool` zeroed. **`Projectile_StaticDtor` (`00006df0`)**, its exit routine: `operator_delete`(`g_ProjectilePool`).

`Sim_MainTick` walks the pool from its end each tick that is not frozen, after the explosions, debris and the other effect pools and before the groups and the player's controls, through each projectile's slot `+0x18`; a projectile whose tick answers non-zero it destroys through slot `+0xc` with 3, which queues it ([the tick](main-loop.md#the-tick--sim_maintick-00059700)). So a projectile fired on a tick first moves on the next, and an ended one goes back to the pool at the next frame's phase 5.

## The projectile classes

Four classes share the projectile base, as ES2's `ROCKET`, `GRENADE`, `BULLET` and `BEAM` share `PROJECTILE`. Each constructor takes the element from `g_ProjectilePool` when it is given none and returns 0 when the pool is empty, builds a `TS_OBJ` with `TSObj_Ctor` ([the 3Space object classes](object-classes.md)), stores its file's copy of the base's table, the type tag 3 at `+0`, the subtype byte `+0x3b` and 0 at `+0x48`, then its own table:

| Class | Constructor | Table | The file's base table |
|---|---|---|---|
| missile | `Rocket_Construct` (`000074e4`, [launching](rockets.md#launching--rocket_fire-00007748)) | `RocketVtable` (`00090ad6`) | `ProjectileBaseVtableRocketCopy` (`00090ab2`) |
| grenade | `Grenade_Construct` (`000078c4`) | `GrenadeVtable` (`00090b7e`) | `ProjectileBaseVtableGrenadeCopy` (`00090b5a`) |
| gun round | `Bullet_Construct` (`00007d08`, [the load](../formats/bullets-skimmer-dat.md#the-load)) | `BulletVtable` (`00090c56`) | `ProjectileBaseVtableBulletCopy` (`00090c32`) |
| beam tracer | `BeamTracer_Ctor` (`00008268`) | `BeamTracerVtable` (`00090d1e`) | `ProjectileBaseVtableBeamCopy` (`00090cfa`) |

The tables' slots are ES2's 4 bytes higher ([vtables](../formats/compiler.md#vtables)): `+4` the draw, `+8` the position (the object + `0x20`), `+0xc` the destructor, `+0x10` a stub returning 0, `+0x14` the shape's radius and `+0x18` the tick. The missile's, grenade's and gun round's draw is their file's copy of `TS_OBJ`'s render, which installs the object's transform and renders the shape instance `+0x2e`; ES2's `Bullet_Draw` also zeroes `DAT_004a5b1c` around it, drawing a round's textured polys fullbright. A class's destructor queues the object on `g_ProjectileDeleteQueue` with `ObjectPool_QueueForDelete` on flags bit 0, where ES2's frees it with `Mem_Delete`. Each file's copy of the base's destructor, `ProjectileBase_DtorRocketCopy` (`000075b4`), `ProjectileBase_DtorGrenadeCopy` (`000079b0`), `ProjectileBase_DtorBulletCopy` (`00007df0`) and `ProjectileBase_DtorBeamCopy` (`00008604`), the slot `+0xc` of the base table, stores that table and the `TS_OBJ` copy's, destroys the shape instance through its slot `+0x1c` with 3 and stores the `MAT_OBJ` and `DRAWABLE` copies', with no null test and no freeing; ES2's `ProjectileBase_Dtor` (`0040c324`) tests for null and frees on flags bit 0.

The fields are ES2's 6 lower, the `TS_OBJ` being `0x3b` bytes to ES2's `0x41`:

| ES1 | ES2 | Holds |
|---|---|---|
| `+0x00` | `+0x04` | the type tag, 3 |
| `+0x02` | `+0x00` | the table |
| `+0x06`, `+0x08`, `+0x0a` | `+0x0c`–`+0x10` | pitch, roll and heading |
| `+0x0c`–`+0x2b` | `+0x12`–`+0x31` | the transform, rebuilt from the angles when the word `+0x2c` is 0; its translation `+0x20`–`+0x28` is the position |
| `+0x2c` | `+0x32` | the frame-valid word |
| `+0x2e` | `+0x34` | the shape instance |
| `+0x3b` | `+0x41` | the subtype |
| `+0x3c`–`+0x43` | `+0x42`–`+0x49` | 8 bytes the fire functions zero |
| `+0x44` | `+0x4a` | the owner |
| `+0x48` | `+0x4e` | the projectile record ([the projectile records](../formats/weapons-dat-sim.md#the-layout)) |

The classes' own fields follow in their sections.

## Gun rounds — `bullet.cpp`

`Bullet_LoadResources` (`00007bb0`), `Bullet_GetTypeRecord` (`00007b94`) and `Bullet_Construct` (`00007d08`) are in [`BULLETS.DAT`](../formats/bullets-skimmer-dat.md#datbulletsdat). The round's own fields:

| ES1 | ES2 | Holds |
|---|---|---|
| `+0x4c` | `+0x52` | the speed |
| `+0x4e` | `+0x54` | the age, in ticks |
| `+0x50` | `+0x56` | the power, a Q10 scale of both damage figures, 0 for none |
| `+0x52` | `+0x58` | the frame countdown, its `int16` at `+0x53` |

### Firing — `Bullet_Fire` (`00008134`)

(machine, angles, muzzle point, subtype): `Bullet_Construct`(0, subtype), then, through what it returns, a full pool's null included, the position `+0x20` from the muzzle point, the six bytes of angles to `+6`, the frame word `+0x2c` = 0, the owner `+0x44`, the 8 bytes at `+0x3c`, the power `+0x50` and the age `+0x4e` zeroed, and **the speed `+0x4c` = the projectile record's speed `+0xa`**; then `Sound_PlayAt`(muzzle point, the type record's `+8` + 10). It returns the round. ES2's ([spawning](../../retail/simulation/projectiles.md#spawning--bullet_fire-0040b43c)) scatters the angles by the record's `+0x0a` and adds the machine's travel speed; ES1's record has no scatter, and the round flies at its weapon's speed alone: 5000 for the autocannons and 2000 for the `EMP`s ([the projectile records](../formats/weapons-dat-sim.md#retail-values)).

**`Bullet_FirePowered` (`000081d4`)**, (power, machine, angles, muzzle point, subtype), is `Bullet_Fire` with the power `+0x50` stored after it. ES2's also gives subtype 9 the machine's selected target to home on; ES1 has no subtype 9 and no homing.

`es2_xref.py` finds seven calls of `Bullet_Fire`: from `Bullet_FirePowered`, from `WeaponMount_FireDispatch_GunBeam` and `WeaponMount_FireDispatch_Missile` ([firing](weapon-mount-classes.md#firing)), and two each from `00003364` and `0001bba4`, which pass subtype 2 ([Open](#open)); and three of `Bullet_FirePowered`, all from `WeaponMount_FireDispatch_GunBeam`.

### Flight — `Bullet_TickUpdate` (`00007f84`)

Slot `+0x18` of `BulletVtable`, (round), returning 1 when the round ends; ES2's ([flight](../../retail/simulation/projectiles.md#flight--bullet_tickupdate-0040b124)) without the homing and the plasma branch:

1. With the type record's interval `+6` non-zero, the countdown `+0x52` runs down by `Math_CountdownTimerTick`; `Bullet_Construct` loaded it with the interval ([the load](../formats/bullets-skimmer-dat.md#the-load)). At 0 it reloads with the interval and the shape instance (`+0x2e`) steps its cell frame for sequence 0: the first word of the frame array the instance's `+8` points at becomes (frame + 1) mod the first word of the frame-count table at its shape's (instance `+4`) `+0x20`, both read as unsigned 16-bit words (`MOVZX`, then a 32-bit `IDIV`), so a count of 0 would divide by zero. When the result is 0 the code stores 0 into the same word a second time. In `dts\bullets`, roots 2 and 3, the two `EMP` rounds' with interval 256, have four frames ([the records](../formats/bullets-skimmer-dat.md#datbulletsdat)).
2. **Age.** `+0x4e` + 1; past the record's lifetime `+2` the round ends. ES2's ages by `Math_IntegrateRateOverTick`(`0x200`) against lifetime × `0x200`, a measure of time; ES1's counts ticks, so a round moves *lifetime* times, 17 for the autocannons and 30 for the `EMP`s ([the records](../formats/bullets-skimmer-dat.md#datbulletsdat)), and how far it gets depends on how long the ticks are.
3. **The sweep.** `Sim_RaycastObjectList` ([shots against objects](world.md#shots-against-objects--sim_raycastobjectlist-0001f1d0)) over the step `Math_IntegrateRateOverTick`(speed) along the frame's Y axis, with the type record's `+4` as the ray's slack and a shot record of the projectile record's armor `+6` and shield `+4`, each `Math_Q10Multiply`'d by the power when it is not 0, its splash factor `+8`, its impact effects `+0xc`, the owner, and 5 at `+0x12`. A strike ends the round; otherwise it moves to the step's end. The difficulty scale is the struck object's hit test's ([the difficulty scale](world.md#the-difficulty-scale--damage_scalebydifficulty-0001f82c)), and so are the damage and the impact effect ([the hit test](mech.md#the-hit-test--mech_directfirehittest-00013b44)); a strike that reaches no object makes its effect in the sweep.

**`Bullet_Dtor` (`00007e80`)**, slot `+0xc` of `BulletVtable` by its padding `00007e7f`, is [the class destructor](#the-projectile-classes) on `bullet.cpp`'s tables. **`Bullet_RegisterSubsystem` (`000081f8`)** registers `Bullet_LoadResources` at phase 2, as ES2's does.

## The grenade

**The grenade launcher spends a round and launches nothing.** `GrenadeMount_FireDispatch` takes a round off the magazine, unless the shot is free, and calls **`Grenade_FireNoOp` (`00007b44`)** with (throw distance, machine, angles, muzzle point, subtype) ([firing](weapon-mount-classes.md#firing)); it is a lone `RET 0x14`, which removes the five arguments and reads none, so the subtype it is passed, read from the record `GREN`'s projectile index 22 points past the table's end at ([the projectile index](../formats/weapons-dat-sim.md#the-projectile-index)), goes nowhere. ES2's `Grenade_FireNoOp` (`0040acac`) is a bare `RET` and its grenade class serves `LAEW`.

The rest of the file is the class, as ES2's grenade module is:

- **`Grenade_Construct` (`000078c4`)**, (round, subtype), builds the round on the root `g_GrenadeShapes` (`000bc1f0`)[record `+0`] of the 8-byte record `g_GrenadeTypeTable` (`000bc1ea`)[subtype], with [the base's fields](#the-projectile-classes), and stores `Proj_LookupRecord`(subtype, 3) at `+0x48`, the one lookup of a type-3 record. `es2_xref.py` finds no reference to it, nor to **`Grenade_GetTypeRecord` (`000078a4`)**, the table + subtype × 8.
- **`Grenade_LoadResourcesNoOp` (`000078c0`)**, the phase-2 loader **`Grenade_RegisterSubsystem` (`00007b5c`)** registers, is a lone `RET` after three `NOP`s, so nothing in this file fills the two tables the constructor reads.
- **`Grenade_TickNoOp` (`00007b47`)**, the tick slot `+0x18` of `GrenadeVtable`, returns 0, so a grenade would never end; **`Grenade_Dtor` (`00007a40`)**, slot `+0xc`, is [the class destructor](#the-projectile-classes) on this file's tables.
- **`Grenade_NoOp` (`00007b50`)**, a `RET 4`, and **`Grenade_ReturnZero` (`00007b54`)**, `XOR EAX,EAX; RET 4`, follow the fire stub, each after `NOP` padding, as ES2's two stubs follow its fire stub; `es2_xref.py` finds no reference to either or to their padding.

## Beams

### Firing — `Bullet_FireBurst` (`000088cc`)

(power, machine, range, shot transform, subtype), from `WeaponMount_FireDispatch_GunBeam` and `ElfMount_FireDispatch` ([firing](weapon-mount-classes.md#firing)), is ES2's ([the chain](../../retail/simulation/beam-visuals.md#chain)) up to the tracer:

1. The ray record: the shot transform, the range as its length, 200 as its slack. The shot record from `Proj_LookupRecord`(subtype, 4), a beam record: armor `+6` and shield `+4`, each `Math_Q10Multiply`'d by the power, the splash factor `+8`, the impact effects `+0xc`, the machine as owner, and 5 at `+0x12`.
2. `Sim_RaycastObjectList` ([shots against objects](world.md#shots-against-objects--sim_raycastobjectlist-0001f1d0)); the distance travelled is its answer, the length + 1 after a strike, or the range when it answers 0.
3. The far end, `Transform_ApplyToPoint` of (0, travelled, 0) by the shot transform, and **one tracer**, `BeamTracer_Ctor`(0, machine, far end, muzzle point, subtype), the muzzle point being the transform's translation `+0x14`. ES2's spawns one tracer per 5000 units for every subtype but its two `ELF`s.
4. `Sound_PlayAt`(muzzle point, `0xb`), which ES2's plays first.

### The tracer — `BeamTracer_Ctor` (`00008268`)

(tracer, owner, far end, muzzle point, subtype). The tracer's own fields:

| ES1 | ES2 | Holds |
|---|---|---|
| `+0x4c` | `+0x52` | the subtype, an `int16` |
| `+0x4e` | `+0x54` | the quad count, an `int8` |
| `+0x4f` | `+0x55` | the jagged flag: 1 for the `ELF`, 0 otherwise |
| `+0x50` | `+0x56` | the point count, an `int16` |
| `+0x52` | `+0x58` | the point array, 12 bytes a point, from `__vec_new` with `maybe_Point3I_CtorBeamCopy` (`00008694`) entered at its padding `00008693` |
| `+0x56` | `+0x5c` | the life countdown, its `int16` at `+0x57` |

It builds the object with no shape, stores [the base's fields](#the-projectile-classes), then `BeamTracerVtable`, the subtype `+0x4c`, the owner `+0x44` and the position `+0x20` from the muzzle point. The byte `+0x3b` gets the element's `int16` `+0x4c` as it was before the constructor stores the subtype there, where ES2's stores the subtype in both. The life countdown is 0, where ES2's arms it at `0x38`: **`BeamTracer_LifeTick` (`000089a8`)**, the tick slot `+0x18`, answers 1 when `Math_CountdownTimerTick` of it reads 0, so in either game the tracer ends at the first tick after it is fired and is drawn in the frame between.

The half-width and colour come from two tables of `int16` in the image, indexed by subtype, where ES2's come from `dat\beam` ([`BEAM.DAT`](../../retail/formats/beam-dat.md)):

| Subtype | Weapon | `Beam_HalfWidths` (`00090cd8`) | `Beam_Colors` (`00090ce4`) |
|---|---|---|---|
| 0 | `PBEAM` | 60 | 10 |
| 1 | `ELF` | 30 | 102 |
| 2 | the big `PBEAM` | 120 | 10 |
| 3 | `LAS100` | 20 | 88 |
| 4 | `LAS200` | 25 | 88 |
| 5 | `LAS300` | 30 | 88 |

These are ES2's `BEAM.DAT` records 0 to 5 but for `ELF`'s color, 104 in ES2. The weapons are the beam records' ([the projectile records](../formats/weapons-dat-sim.md#retail-values)).

- **The `ELF`, subtype 1**, is ES2's jagged chain ([ELF and ELF2](../../retail/simulation/beam-visuals.md#elf-and-elf2--the-jagged-branch)) from the muzzle to the far end. The count *n* = (`Math_DistanceBetweenPoints`(far end, muzzle) >> 10) + 1 is stored as a byte at `+0x4e` and read back sign-extended; the point count 2*n* + 2 is a word. The step is the far end less the muzzle, rescaled to `0x400` by `Math_NormalizeVec3ToLength`. Nodes *k* = 0 to *n*, *n* + 1 of them, start from the muzzle and advance by the step, but node *n* is the far end itself. Every node after the first, the far end included, gets `Math_RandomNext`(`000b5bd4`) & `0x7f`, 0 to 127, added to its X, then a second draw to its Y, then a third to its Z. Node *k* is written to points 2*k* + 1 and 2*k*, and the half-width is added to point 2*k*'s Z.
- **Every other subtype** has two quads and eight points. Points 0, 1, 4 and 5 are the muzzle, and 2, 3, 6 and 7 the far end; the half-width is added to the Z of points 0, 2 and 6, taken from the Z of points 1, 3 and 7, added to the X of point 4 and taken from the X of point 5 (`ADD [ESI+0x50]` at `00008594` and `SUB [EBX+0x5c]` at `00008599` are points 6's and 7's Z). The first quad, points 1, 0, 2 and 3 ([the draw](#drawing--beamtracer_draw-000087e0)), is an upright strip from the muzzle to the far end. The second, points 5, 4, 6 and 7, runs from a level span along the world's X axis at the muzzle to the first quad's own upright span at the far end, points 6 and 7 being points 2 and 3, so its four corners do not lie in one plane and it twists along the beam. Both are fixed to the world's axes, not to the beam's direction. ES2's straight beam is two points drawn as a ribbon facing the camera.

### Drawing — `BeamTracer_Draw` (`000087e0`)

Slot `+4` of `BeamTracerVtable`, by its padding `000087dd`. Every tracer draws as ES2's `ELF` chain does ([the paint](../../retail/simulation/beam-visuals.md#the-paint-uses-the-polygon-renderers-project-clip-and-fill-chain)): it installs the brush {0, `Beam_Colors`[subtype]} at the render context's (`000bdbda`) `+0x22c`, the identity model transform (`Raster_SetModelTransform`(0)), the point count at `000b672c`, the points at `Poly_IntPoints`, `Poly_PointState` cleared over the count and `Beam_QuadIndexTable` (`000bc202`) at `Poly_IndexList`; then for each quad *k*, 4 vertices from the offset *k* << (3 − jagged flag), `Poly_ProjectIndexedVertices`, `Poly_ClipRingToNearPlane` when a vertex lies behind the near plane, and `PolyFill_Fill`; and it puts the context's brush back. The index table, which **`Beam_BuildIndexTable` (`00008230`)**, the file's phase-2 loader, fills as (*i* >> 1) + `Beam_QuadIndexOffsets` (`00090cf0`)[*i* & 3] with the offsets 1, 0, 1, 2, over 80 entries where ES2's has 120, makes quad *k* of a chain the points 2*k* + 1, 2*k*, 2*k* + 2, 2*k* + 3, and the two quads of a straight beam points 1, 0, 2, 3 and 5, 4, 6, 7. So a straight beam is a flat fill in its color, and there is no texture, no screen-space width and no muzzle stub, which ES2's straight path and its fall-through draw. 80 entries are 20 chain quads; the `ELF`'s range of 15000 makes 15.

**`BeamTracer_Dtor` (`00008768`)**, slot `+0xc` by its padding `00008767`, frees the point array with `__vec_delete`(0, `0xc`, 7, points) and goes on as [the class destructor](#the-projectile-classes) does. **`Beam_RegisterSubsystem` (`000089c4`)** registers `Beam_BuildIndexTable` at phase 2.

## Functions

| ES1 | Address | ES2 |
|---|---|---|
| `Projectile_LoadResources`, `Projectile_FlushDeletes`, `Projectile_RegisterSubsystem`, `Projectile_StaticDtor` | `00006d9c`, `00006db0`, `00006dc4`, `00006df0` | `0040a060`, `0040a108`, `0040a14c`, `0040a1a0` |
| `HeapPtr_DtorGrenadeCopy`, `Grenade_GetTypeRecord`, `Grenade_LoadResourcesNoOp`, `Grenade_Construct` | `0000787c`, `000078a4`, `000078c0`, `000078c4` | —, `0040ac20`, `0040ac38`, `0040ac3c` |
| `TSObj_RenderGrenadeCopy`, `TSObj_GetPositionGrenadeCopy`, `ProjectileBase_DtorGrenadeCopy`, `TSObj_ReturnZeroGrenadeCopy`, `TSObj_GetRadiusGrenadeCopy` | `0000797c`, `0000799c`, `000079b0`, `000079ef`, `000079f8` | the slots `+4` to `+0x14` of the two projectile tables |
| `Grenade_Dtor`, `TSObj_DtorGrenadeCopy`, `MatObj_GetPositionGrenadeCopy`, `MatObj_DtorGrenadeCopy`, `Drawable_GetRadiusGrenadeCopy`, `Drawable_DtorGrenadeCopy` | `00007a40`, `00007aa0`, `00007ae8`, `00007afc`, `00007b26`, `00007b30` | `0040ad2c`; the object classes' copies |
| `Grenade_FireNoOp`, `Grenade_TickNoOp`, `Grenade_NoOp`, `Grenade_ReturnZero`, `Grenade_RegisterSubsystem` | `00007b44`, `00007b47`, `00007b50`, `00007b54`, `00007b5c` | `0040acac`, `0040acb4`, `0040acbc`, `0040acc4`, `0040accc` |
| `HeapPtr_DtorBulletCopy`, `TSObj_RenderBulletCopy`, `TSObj_GetPositionBulletCopy`, `ProjectileBase_DtorBulletCopy`, `TSObj_ReturnZeroBulletCopy`, `TSObj_GetRadiusBulletCopy` | `00007b6c`, `00007dbc`, `00007ddc`, `00007df0`, `00007e2f`, `00007e38` | — |
| `Bullet_Dtor`, `TSObj_DtorBulletCopy`, `MatObj_GetPositionBulletCopy`, `MatObj_DtorBulletCopy`, `Drawable_GetRadiusBulletCopy`, `Drawable_DtorBulletCopy` | `00007e80`, `00007ee0`, `00007f28`, `00007f3c`, `00007f66`, `00007f70` | `0040b64d`; the object classes' copies |
| `Bullet_TickUpdate`, `Bullet_Fire`, `Bullet_FirePowered`, `Bullet_RegisterSubsystem` | `00007f84`, `00008134`, `000081d4`, `000081f8` | `0040b124`, `0040b43c`, `0040b5a0`, `0040b5d8` |
| `HeapPtr_DtorBeamCopy`, `Beam_BuildIndexTable`, `BeamTracer_Ctor` | `00008208`, `00008230`, `00008268` | —, `Beam_LoadResourceTables` (`0040b6e0`), `0040b804` |
| `TSObj_RenderBeamCopy`, `TSObj_GetPositionBeamCopy`, `ProjectileBase_DtorBeamCopy`, `TSObj_ReturnZeroBeamCopy`, `TSObj_GetRadiusBeamCopy`, `maybe_Point3I_CtorBeamCopy` | `000085d0`, `000085f0`, `00008604`, `00008643`, `0000864c`, `00008694` | — |
| `TSObj_DtorBeamCopy`, `MatObj_GetPositionBeamCopy`, `MatObj_DtorBeamCopy`, `Drawable_GetRadiusBeamCopy`, `Drawable_DtorBeamCopy` | `000086c4`, `0000870c`, `00008720`, `0000874a`, `00008754` | the object classes' copies |
| `BeamTracer_Dtor`, `BeamTracer_Draw`, `Bullet_FireBurst`, `BeamTracer_LifeTick`, `Beam_RegisterSubsystem` | `00008768`, `000087e0`, `000088cc`, `000089a8`, `000089c4` | `0040ba94`, `0040bc14`, `0040bf74`, `0040c2a0`, `0040c2c0` |

## Globals

| | Address |
|---|---|
| `g_ProjectilePool`, `g_ProjectileDeleteQueue` | `000af0b4`, `000af0b8` |
| `g_GrenadeTypeTable`, `g_GrenadeShapes` | `000bc1ea`, `000bc1f0` |
| `Beam_QuadIndexTable`, 80 `int16` | `000bc202` |
| `Beam_HalfWidths`, `Beam_Colors`, `Beam_QuadIndexOffsets`, six, six and four `int16` | `00090cd8`, `00090ce4`, `00090cf0` |
| `ProjectileBaseVtableGrenadeCopy`, `GrenadeVtable`, `ProjectileBaseVtableBulletCopy`, `BulletVtable`, `ProjectileBaseVtableBeamCopy`, `BeamTracerVtable` | `00090b5a`, `00090b7e`, `00090c32`, `00090c56`, `00090cfa`, `00090d1e` |

## Open

- **Open:** `00003364` and `0001bba4`, which fire gun rounds of subtype 2 (and `0001bba4` a missile), found by `es2_xref.py` and read only at their calls: the structures' and the flyers' guns, if their files say so.
- **Open:** what the word `000af11c`, which the pool's static initializer zeroes, is: `es2_xref.py` finds that one reference.
- **Open:** what reads a tracer's `+0x3b`, which holds a stale word.
- **Open:** the retail look of a beam against these readings. A retail check from the outside view: a laser fired across the view should show a cross at the muzzle, an upright and a level span, narrowing to one upright span where the beam stops; one fired along the world's X axis shows the level span edge-on, so its second strip is a wedge from a line at the muzzle to the upright span; and a far beam is thinner on screen than a near one, with no four-pixel floor.
