# DBSIM.EXE travelling projectiles (`PROJ.DAT` type `Bullet`)

Addresses are DBSIM virtual addresses.

The other half of the fire dispatch. A `Beam` record carries `Speed == 0` and is over inside the call that fired it ([`beam-visuals.md`](beam-visuals.md)); a `Bullet` record becomes a real object that crosses the ground over several ticks. Every autocannon, every EMP cannon and the plasma cannon fire one. Launcher rounds are a third family with their own table and their own tick — see [`rockets.md`](rockets.md).

Like a tracer, a bullet lives in the effect pool (`g_ProjectilePool`, `004a9746`) that `Sim_MainTick` walks **before** the machine list, not in the object list the raycast sweeps. It cannot be shot at, and a round that leaves the barrel this tick does not move until the next.

## `dat\BULLETS.DAT`

`Bullet_LoadResources` (`0040ade0`) reads it as `int16 count` then that many 14-byte records, and loads `dts\BULLETS.DTS` and `dba\BULLETS.DBA` alongside it. **Indexed by the firing `PROJ.DAT` record's subtype id**, as `BEAM.DAT` is — `Bullet_GetTypeRecord` (`0040adc0`) is `table + id * 14`.

| Offset | Meaning |
|---|---|
| `+0x00` | model: root of `BULLETS.DTS` |
| `+0x02` | lifetime, in 125 ms units; the shot is dropped when its age passes `lifetime * 0x200` |
| `+0x04` | clip radius: the shot record's `+0x08` slack, in place of a beam's literal 200 |
| `+0x06` | animation frame interval; 0 = static shape |
| `+0x08` | fire sound id, played as `id + 10` |
| `+0x0a` | **firing scatter**, in binary-angle units |
| `+0x0c` | nonzero arms a per-lifetime rate at `obj+0x61` ([Open](#open)) |

Retail (12 records; the five not listed are unreachable — no `Bullet` record carries their id). Two of the five, 10 and 11, are `ATC75`'s and `ATC100`'s own records, which their `PROJ.DAT` records do not name: [`../formats/proj-dat.md`](../formats/proj-dat.md#lookup).

| id | Weapons | Shape | Life | Radius | Anim | Scatter |
|---|---|---|---|---|---|---|
| 0 | ATC20 | 0 | 20 | 100 | 0 | 63 |
| 1 | ATC35, ATC75 | 4 | 18 | 100 | 0 | 63 |
| 2 | ATC50, ATC100 | 5 | 16 | 100 | 0 | 63 |
| 6 | EMPC | 2 | 30 | 100 | 256 | 0 |
| 7 | BEMP | 3 | 30 | 200 | 256 | 0 |
| 8 | EMP2 | 2 | 30 | 100 | 256 | 0 |
| 9 | PLAS, MAGN | 8 | 40 | 100 | 0 | 0 |

Weapon names above are the simulator's own. Subtype 9 is reached by two weapon ids, `PLAS` (25) and `MAGN` (28, the shell catalog's `MFAC` — [`../formats/weapons-dat.md`](../formats/weapons-dat.md)), whose templates both carry `PROJ.DAT` index 22.

## Spawning — `Bullet_Fire` (`0040b43c`)

`Bullet_Fire(projIndex, muzzleWorldPoint, aimEulerTriple, ownerMech)`. The powered form `Bullet_FirePowered` (`0040b5a0`) is the same call with two fields written after it.

- **Geometry is one transform.** The object holds a euler triple at `+0x0c` and a transform at `+0x12` whose translation *is* the position (`+0x26`); the rotation is rebuilt from the triple whenever the dirty flag at `+0x32` says the angles moved.
- **Scatter** displaces euler components 0 and 2 by `(scatter * 2 & random) - scatter`. The mask is literally `scatter * 2`, not a power of two minus one, so the retail 63 draws odd values only. Component 1 is roll about the shot's own axis and is left alone.
- **Speed** is `ownerMech->vtable+0x38` (travel speed) **plus** the record's `Speed`, so a round fired from a machine running forward flies faster.
- `Bullet_FirePowered` adds `+0x56`, the capacitor charge the shot was fired at, and — for subtype 9 alone — `+0x5b`, the firing machine's selected target at `mech+0x1a4`.

## Flight — `Bullet_TickUpdate` (`0040b124`)

1. Advance the shape's animation frame when the record's `+0x06` is nonzero.
2. `age += IntegrateRateOverTick(0x200)`; expire at `record[+0x02] * 0x200` (the lifetime) with no impact of any kind.
3. Home, if a target was attached — `Bullet_HomingSteer` (`0040aff0`). Only the plasma round is ever given one.
4. `step = IntegrateRateOverTick(obj+0x52)`, taken along the frame's Y axis.
5. Build a shot record (same layout as a beam's, see [`weapon-firing.md`](weapon-firing.md#the-shot-record)) with **the frame as the ray and the step as its length**, and run `Sim_RaycastObjectList`. A bullet therefore sweeps the segment it is about to cross rather than testing a point, which is what stops a fast round tunnelling through a machine between ticks.
6. Struck anything and the shot ends; struck nothing and it moves.

**Both damage figures are scaled Q10 by `+0x56` only when that is nonzero** — an autocannon round, spent out of a magazine rather than a charge, does the record's face value. The beam dispatch spells the same line unconditionally but can never pass zero.

### The plasma branch

Subtype 9 is singled out by literal value. Before the raycast it stashes both damage figures in globals and then **empties the shot record** — armour, shield and splash factor alike — so the raycast reports contact and nothing more. Everything the round does it does through a `Damage_ExplosiveBlastSweep` at 4000 units, which **excludes nothing**: the object it touched stands in the blast like any other. A proximity fuze detonates it within 2000 units of the homing target once the bearing error exceeds a quarter turn.

The blast figure is the record's **armour** damage, power-scaled — the two are equal on the one record this reaches — and then difficulty-scaled by a direct `Damage_ScaleByDifficulty` (`00426b04`) call, because the emptied record gives the raycast's own scaling nothing to scale ([`difficulty.md`](difficulty.md#the-damage-scale-reaches-all-direct-fire-not-just-plasma)). The stash is written before that scale, so it holds the power-scaled figures without the difficulty scale.

The one reader of the stash is a structure struck on its collision-volume path, which puts the armour figure back — see [`hit-detection.md`](hit-detection.md#base_directfirehittest--00405038).

Homing is a steer of the **euler angles**, not of a velocity: the bearing to the target (`Math_EulerToward`, [`dbsim-physics-notes.md`](dbsim-physics-notes.md#fixed-point-math-toolkit)) drives euler 0 and 2 through `Math_RateLimitedMoveToward` at `0x280` per 125 ms, and the yaw error it leaves is what the proximity fuze tests. The target point is the vtable `+0x24` aim node, not the origin — see [`target-selection.md`](target-selection.md#aim-point--vtable-0x24).

## How a round is drawn

`Bullet_Draw` (`0040a120`) is the vtable slot 0 of both projectile classes: it zeroes `DAT_004a5b1c` for the duration, installs the object's frame as the model transform, renders the shape instance at `+0x34`, and restores. The zeroing is what makes a textured poly fullbright — [`../formats/dts-texture-binding.md`](../formats/dts-texture-binding.md#tstexture4poly--frame-index-ramp-row-by-light-fullbright-on-demand). A round is faded from its own range like any other depth-sorted object — [`../formats/distance-fog-and-sky.md`](../formats/distance-fog-and-sky.md#a-projectile-is-faded-like-anything-else).

The one textured shape is the plasma round's (`BULLETS.DTS` root 8). Its fullbright fill is a plain texture copy that never computes a shade row, and the depth bias is spent only while a mode 1 or mode 2 fill computes one, so **the plasma round does not fade with distance**. Every other round's shape is `TSSolidPoly` and fades normally.

## Open

- **Open:** identify what reads the per-lifetime rate `+0x0c` arms at `obj+0x61` in `BULLETS.DAT`.
