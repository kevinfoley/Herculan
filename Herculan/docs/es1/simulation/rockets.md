# DBSIM.EXE — `rocket.cpp`, the missiles

**ES1's missile launchers fire missiles that burn, home on their target and can be flown from the missile camera as ES2's do ([ES2's missiles](../../retail/simulation/rockets.md)).** What a player would see differently, as far as this file's code decides it ([Open](#open)):

- a missile leaves the launcher at a speed of 500 whatever the firing HERC's own speed, where ES2's adds the machine's travel speed;
- a HERC's missile takes the target its HERC has selected whether or not the launcher has lock: the lock question goes to a slot that answers yes on every HERC ([launching](#launching--rocket_fire-00007748));
- an enemy's missile makes no warning sound as it nears the camera, where ES2's plays one;
- any machine's electro-optical missile, not only the player's, makes the player let go of the trigger when it ends: a trigger held at that moment fires nothing until it is released ([flight](#flight--rocket_tickupdate-00007198)).

Addresses are `ES1DBSIM` image addresses ([the X-32 executable](../formats/x32-executable.md#the-image)); ES2's are DBSIM's. Every function here removes its own arguments, so argument lists are in stack order ([calling conventions](../formats/compiler.md#calling-conventions)). A missile is one of [the projectile classes](projectiles.md#the-projectile-classes): it lives in the projectile pool, its fields are ES2's 6 lower, and `Sim_MainTick` walks it through its tick slot `+0x18` ([the tick](main-loop.md#the-tick--sim_maintick-00059700)).

## Where the object file starts and ends

The file's code is `00006dfc`–`00007879`. It opens with a run of `ConfigPart`'s copies, `HeapPtr_DtorRocketCopy` (`00006dfc`), `ConfigPart_DtorRocketCopy` (`00006e24`), the class tag `ConfigPart_GetClassTagRocketCopy` (`00006e63`) and `TSPartBase_DtorRocketCopy` (`00006e6c`), right after the exit routine of [the projectile pool's file](projectiles.md#where-the-object-files-start-and-end), whose `RET` at `00006dfb` ends on a 4-byte boundary ([where object files meet](../formats/compiler.md#where-object-files-meet-in-the-code)). `Rocket_LoadResources` pushes the source path `C:\P\M\s\c\rocket.cpp` (`00090a92`) at `00007406`. The last code is the static initializer **`Rocket_RegisterSubsystem` (`0000786c`)**, entry `000adbdc` of the static constructor table, which registers `Rocket_LoadResources` at subsystem phase 2; its `RET` at `00007878` is followed by three zero bytes and the lone 40-byte destructor at `0000787c` that opens [the grenade's file](projectiles.md#the-grenade). Its functions are ES2's from `Rocket_GetTypeRecord` (`0040a234`) to `Rocket_RegisterSubsystem` (`0040ab18`), with the destructors after the constructor as ES2's are after the static initializer, and with the file's copies of the object classes' functions (`00007580`–`00007734`).

Its tables run from `00090a26` to `00090b56`: the run's two tables, the names `rockets` and the source path, then `ProjectileBaseVtableRocketCopy` (`00090ab2`), `RocketVtable` (`00090ad6`) and this file's copies of `TS_OBJ`'s (`00090afa`), `MAT_OBJ`'s (`00090b1a`) and `DRAWABLE`'s (`00090b3a`) tables.

## The type table — `Rocket_LoadResources` (`000073d0`)

The phase-2 loader is ES2's `Rocket_LoadTypeTable_Unguided` (`0040a818`, [`ROCKETS.DAT`](../../retail/formats/rockets-dat.md)): it opens `dat\rockets`, asserting at severity 5 (line `0xd1`) when the stream status is set, reads an `int16` count into `g_RocketTypeCount` (`000bc1e2`) and that many 14-byte records into `g_RocketTypeTable` (`000bc1de`), keeps the greatest root plus one at `000bc1e8`, and loads `dts\rockets` with `Shape_LoadAllRoots` (tag `0x140008`) into `g_RocketShapes` (`000bc1e4`), binding no bank. The records are ES2's layout, and **`Rocket_GetTypeRecord` (`00006e98`)** reaches one by the missile's subtype byte `+0x3b`, as ES2's does by `+0x41`.

The retail `dat\rockets` in `SIMVOL0.VOL` is 72 bytes, a count of 5 and five records, as read through `HercWorks.Core`'s reader of ES2's file; they are ES2's retail records ([`ROCKETS.DAT`](../../retail/formats/rockets-dat.md#datrocketsdat)) value for value:

| Subtype | Fired by | Root | Lifetime | Acceleration | Slack | Interval | Sequence | Sound |
|---|---|---|---|---|---|---|---|---|
| 0–3 | the pods `6`, `8` and `10` | 0 | 80 | 250 | 200 | 256 | 0 | 5 |
| 4 | `MISSL` | 1 | 80 | 250 | 300 | 0 | 0 | 5 |

`WeaponMount_FireDispatch_Missile` passes the projectile record's subtype id `+2` as the subtype. `MISSL`'s projectile index 18 names the record of type 0 and subtype id 4 ([the projectile records](../formats/weapons-dat-sim.md#retail-values)). A pod's mount holds the type-0 record whose subtype id is its hardpoint's ammunition type, 5 read as 0 ([the projectile index](../formats/weapons-dat-sim.md#the-projectile-index)): records 10 to 13 for types 0 to 3. `dts\rockets` holds two roots of two cell frames each. With interval 0, the `MISSL`'s flame never steps.

## Launching — `Rocket_Fire` (`00007748`)

(speed, machine, angles, muzzle point, subtype), from `WeaponMount_FireDispatch_Missile` ([firing](weapon-mount-classes.md#firing)) with speed 0, is ES2's ([spawning](../../retail/simulation/rockets.md#spawning--rocket_fire-0040a9c4)) with these differences:

- **`Rocket_Construct` (`000074e4`)**, (missile, subtype), takes the missile from `g_ProjectilePool` itself when given none and returns 0 when the pool is empty; `Rocket_Fire` then writes its fields through what it returns, a full pool's null included, as ES2's does. The constructor builds the object on the root `g_RocketShapes`[record `+0`] with `TSObj_Ctor`, stores `ProjectileBaseVtableRocketCopy`, the type tag 3 at `+0`, the subtype byte `+0x3b` and `+0x48` = 0, then `RocketVtable`, the target `+0x50` = 0 and the projectile record `+0x48` = `Proj_LookupRecord`(subtype, 0) ([the projectile lookups](../formats/weapons-dat-sim.md#the-projectile-lookups)).
- `Rocket_Fire` sets the position `+0x20`, the six bytes of angles at `+6`, the frame word `+0x2c` = 0, the owner `+0x44`, the speed `+0x4c` = speed + 500, and zeroes the 8 bytes at `+0x3c` and the age `+0x4e`. **The launch speed is 500**: the dispatch passes 0 where ES2's passes the machine's travel speed.
- **The target.** A machine whose `+0x9d` (ES2's piloted byte `+0xa3`) is clear firing subtype 3 takes it outright, as in ES2; otherwise the machine's slot `+0x70`(machine, subtype) decides, where ES2's asks slot `+0x6c`. On a HERC that slot is `SimObject_MissileLockState_AlwaysMechCopy`, which answers 1 ([the HERC's table](mech.md#the-hercs-table)), so a HERC's missile always takes the machine's selected target `+0x15e` (ES2's `+0x1a4`) into `+0x50`, lock or no lock. With a target, the component to steer at goes to `+0x54` from the target's slot `+0x58`(target, the missile's angles `+6`), `Mech_ComponentNearestAim` (`00015884`) on a HERC.
- With the machine's `+0x9d` set, `MfdMissileView_TrackedRound` (`00093914`) becomes the missile, for [the missile camera](mfd-missile-camera.md#painting).
- `Sound_PlayAt`(muzzle point, the type record's `+0xc` + 10).
- **The flame countdown.** Neither function writes `+0x56` or its `int16` at `+0x57`, so the missile keeps what the pool element last held there: what the previous projectile in that element left, or, for an element never used, 0, the heap having zero-filled the pool's block ([object pools](memory.md#object-pools), [the pool](projectiles.md#the-projectile-pool)). `Bullet_Construct` does load its round's countdown `+0x53` from the type record's `+6` ([the load](../formats/bullets-skimmer-dat.md#the-load)).

The missile's own fields, ES2's 6 lower:

| ES1 | ES2 | Holds |
|---|---|---|
| `+0x4c` | `+0x52` | the speed, an `int16` |
| `+0x4e` | `+0x54` | the age, in ticks |
| `+0x50` | `+0x56` | the target |
| `+0x54` | `+0x5a` | the target's component to steer at |
| `+0x56` | `+0x5c` | the flame countdown, its `int16` at `+0x57` |

`es2_xref.py` finds one other call, from `0001bba4`, which passes its `+0x1c9` as the speed and subtype 0 ([Open](#open)).

## Flight — `Rocket_TickUpdate` (`00007198`)

Slot `+0x18` of `RocketVtable`, (missile), returning 1 when the missile ends; ES2's ([flight](../../retail/simulation/rockets.md#flight--rocket_tickupdate-0040a538)) on ES1's fields, in order:

1. **The flame.** With the type record's interval `+8` non-zero, the countdown at `+0x56` (its `int16` at `+0x57`) runs down by `Math_CountdownTimerTick`. At 0 it reloads with the interval and the shape instance (`+0x2e`) steps its cell frame for the record's sequence *s* `+0xa`: word *s* of the frame array the instance's `+8` points at becomes (frame + 1) mod word *s* of the frame-count table at its shape's (instance `+4`) `+0x20`. *s*, the frame and the count are read as unsigned 16-bit words (`MOVZX`, then a 32-bit `IDIV` at `00007220`), so a count of 0 would divide by zero; when the result is 0 the code stores 0 into the same word a second time. The first step comes when the count the pool element held runs out ([the flame countdown](#launching--rocket_fire-00007748)).
2. **Age.** `+0x4e` + 1; past the record's lifetime `+2` the missile ends.
3. **Burn.** The speed `+0x4c` gains `Math_IntegrateRateOverTick`(record `+4`), is averaged with the speed the tick opened at, and is capped at the projectile record's (`+0x48`) speed `+0xa`.
4. **Guidance.** With the owner's `+0x9d` set and the subtype 3, `Rocket_PlayerSteer`(missile, `PlayerInputBlock`) and `maybe_WeaponChain_EoHold` (`000b5bc6`) = 1, which `Sim_MainTick` clears before it walks the pool ([the chain hold](weapon-mounts.md#firing--weaponmounts_firetrigger-0000d2bc)); otherwise `Rocket_HomingSteer`.
5. **The sweep.** `Sim_RaycastObjectList` ([shots against objects](world.md#shots-against-objects--sim_raycastobjectlist-0001f1d0)) over the step `Math_IntegrateRateOverTick`(speed) along the frame's Y axis, with the record's `+6` as the ray's slack and a shot record from the projectile record: armor `+6` at `+4`, shield `+4` at `+6`, the splash factor `+8`, the impact effects `+0xc`, the owner and the subtype `+2` at `+0x12`, none of them scaled. A strike ends the missile; otherwise it moves to the step's end. What a strike does to what it strikes, the damage and the impact effect, is the sweep's and the struck object's hit test's ([the hit test](mech.md#the-hit-test--mech_directfirehittest-00013b44)).

**When the missile ends**, by its lifetime or a strike, with subtype 3, it calls `Input_LatchButton`(1, 1) and clears the trigger byte `000b17c1` (`PlayerInputBlock` `+0x15`), whoever fired it: a latched button reads released while it is held ([the input build](input.md#the-input-build--input_buildplayerdevice-0004cb14)). ES2's does this for the locally piloted machine's missile alone. The tick raises no strike flag for the missile camera and plays no proximity sound; ES1's missile camera finds its strike flash by looking for an explosion near where the missile was last seen ([painting](mfd-missile-camera.md#painting)).

## Homing — `Rocket_HomingSteer` (`00006eb4`)

(missile): ES2's ([guidance](../../retail/simulation/rockets.md#guidance--rocket_homingsteer-0040a254)) on ES1's fields ([the HERC's fields](mech.md)):

- With no target `+0x50`, or for a locally piloted owner (`+0x9d`) a target that is no longer the owner's selected target `+0x15e`, it returns.
- Subtype 2 steers only while the target's radar `+0x90` or jamming byte `+0x9b` is set (ES2's `+0x96` and `+0xa1`).
- The aim point is the component `+0x54` placed through the target's slot `+0x5c` (`Mech_ComponentPosition`, `00015990`, on a HERC) when there is one; otherwise the point `+0x14` of the transform the target's slot `+0x28` returns (`Mech_GetAimNodeTransform`, `00013204`), carried by the target's own transform, or the target's position `+0x20` when that slot returns 0.
- `Math_EulerToward` from the missile's position gives the bearing; the errors are taken from the pitch `+6` and heading `+0xa`. Subtype 3 sets the owner's `+0xaf` (ES2's `+0xb5`); subtypes other than 2 and 3, with the owner's ECM spoof `+0x96` (ES2's `+0x9c`) set, move each error in [−`0xc00`, `0xbff`] by `0xc00` toward the opposite sign.
- Each error, clamped to ±`0x500` by `Math_RateLimitedMoveToward`, is added to its angle through `Math_IntegrateRateOverTick`, and the frame word `+0x2c` is cleared.

## The flown missile — `Rocket_PlayerSteer` (`000070dc`)

(missile, input), the input being `PlayerInputBlock` (`000b17ac`): ES2's ([the player flying the missile](../../retail/simulation/rockets.md#rocket_playersteer-0040a488--the-player-flying-the-missile)) on the block's ES1 offsets. With the trigger byte `+0x15` (ES2's `+0x0d`) clear it drops the target `+0x50` and rewrites the subtype `+0x3b` to 0, and the missile flies on unguided. Held, it subtracts `Math_IntegrateRateOverTick`(`Math_Q8Multiply`(`0x500`, *axis*)) of the axis the pointer `+0x1d` addresses from the heading `+0xa`, adds the same of the axis `+0x21` addresses to the pitch `+6`, clears the frame word `+0x2c`, and zeroes the trigger byte and both axes. The block's `+0x1d` and `+0x21` are its camera-axis pointers (ES2's `+0x22` and `+0x26`), which the input build points at the round's axes while one is flown ([the input build](input.md#the-input-build--input_buildplayerdevice-0004cb14)).

## The destructors

**`Rocket_Dtor` (`00007644`)**, (missile, flags), slot `+0xc` of `RocketVtable` by its padding `00007643`, with the missile non-null stores `RocketVtable`, `00090ab2` and `00090afa`, destroys the shape instance `+0x2e` through its slot `+0x1c` with 3, stores `00090b1a` and `00090b3a`, and on flags bit 0 queues the missile on `g_ProjectileDeleteQueue` (`000af0b8`) for `Projectile_FlushDeletes` to return to the pool ([the projectile pool](projectiles.md#the-projectile-pool)). ES2's (`0040ab8d`) frees it with `Mem_Delete`. **`ProjectileBase_DtorRocketCopy` (`000075b4`)**, slot `+0xc` of `ProjectileBaseVtableRocketCopy`, is the same without the null test, the first store and the queueing ([the projectile classes](projectiles.md#the-projectile-classes)).

## Functions

| ES1 | Address | ES2 |
|---|---|---|
| `HeapPtr_DtorRocketCopy`, `ConfigPart_DtorRocketCopy`, `ConfigPart_GetClassTagRocketCopy`, `TSPartBase_DtorRocketCopy` | `00006dfc`, `00006e24`, `00006e63`, `00006e6c` | the run ([module copies](../formats/compiler.md#vtables)) |
| `Rocket_GetTypeRecord`, `Rocket_HomingSteer`, `Rocket_PlayerSteer`, `Rocket_TickUpdate` | `00006e98`, `00006eb4`, `000070dc`, `00007198` | `0040a234`, `0040a254`, `0040a488`, `0040a538` |
| `Rocket_LoadResources`, `Rocket_Construct` | `000073d0`, `000074e4` | `Rocket_LoadTypeTable_Unguided` (`0040a818`), `0040a948` |
| `TSObj_RenderRocketCopy`, `TSObj_GetPositionRocketCopy`, `ProjectileBase_DtorRocketCopy`, `TSObj_ReturnZeroRocketCopy`, `TSObj_GetRadiusRocketCopy` | `00007580`, `000075a0`, `000075b4`, `000075f3`, `000075fc` | the slots `+4` to `+0x14` of the two projectile tables |
| `Rocket_Dtor`, `TSObj_DtorRocketCopy`, `MatObj_GetPositionRocketCopy`, `MatObj_DtorRocketCopy`, `Drawable_GetRadiusRocketCopy`, `Drawable_DtorRocketCopy` | `00007644`, `000076a4`, `000076ec`, `00007700`, `0000772a`, `00007734` | `0040ab8d`; the object classes' copies |
| `Rocket_Fire`, `Rocket_RegisterSubsystem` | `00007748`, `0000786c` | `0040a9c4`, `0040ab18` |

## Globals

| | Address |
|---|---|
| `g_RocketTypeTable`, the pointer to the 14-byte records, and `g_RocketTypeCount` | `000bc1de`, `000bc1e2` |
| `g_RocketShapes`, the roots of `dts\rockets`, and the greatest root plus one | `000bc1e4`, `000bc1e8` |
| `ProjectileBaseVtableRocketCopy`, `RocketVtable` | `00090ab2`, `00090ad6` |

## Open

- **Open:** `0001bba4`, the other caller of `Rocket_Fire` (`es2_xref.py`), and of `Bullet_Fire` with subtype 2, read only at its calls.
- **Open:** what reads the owner's `+0xaf`, which homing sets for subtype 3: ES2's AI reads its `+0xb5` ([the fire decision](../../retail/simulation/ai-weapons.md#the-fire-decision--ai_fireatpoint-0041f5a0)), and ES1's AI's selection skip is `+0xaf` ([the AI](mech-ai.md)).
- **Open:** whether any machine but the player's fires an electro-optical missile in retail, and so whether the trigger latch on its end is ever seen. A retail check: hold the trigger on an autocannon while an enemy's electro-optical missile ends nearby; the gun stops until the trigger is released if the latch is hit.
- **Open:** a positive control in the running game for the done checklist ([when a module is done](../../herculan/plan-es1-support.md#when-a-module-is-done)). Proposed: fire a missile at a selected HERC the moment it is selected, before any lock lamp; the missile homes. `BP <CS>:00007748` fires once per missile with the second dword on the stack the firing HERC.
