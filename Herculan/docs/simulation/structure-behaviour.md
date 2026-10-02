# Structure behaviour

What a `BASES.DAT` ([`bases-dat.md`](../formats/bases-dat.md)) structure does per tick, and how it takes damage. Hit detection is [`hit-detection.md`](hit-detection.md) and the collapse a lost part runs is [`destruction-effects.md`](destruction-effects.md#a-structure-coming-down). This doc owns the `+0x18` tick slot and the five classes that fill it.

## Timing constants

Every countdown in this doc is in the simulation's timer unit, which is not a millisecond: one count is about 0.49 ms ([Timer units](dbsim-physics-notes.md#timer-units)). The structure constants in real time:

| Constant | Units | Real time |
|---|---|---|
| Retarget `+0x21d` | 10000 | 4.9 s |
| Firing window `004973e0` | 10000 | 4.9 s each way |
| Refire `+0x211` | 1500 | 0.73 s |
| Ground vehicle back-off `+0x223` | 3000 | 1.5 s |

## Five classes, one switch

`Base_Construct` (`00405314`) switches on the type index and installs one of five vtables. The case labels below are the switch's own.

| Class | Vtable | `+0x18` tick | `BASES.DAT` type indices |
|---|---|---|---|
| Plain | `00497940` | `Base_ThinkTick` `00403ca8` | 0-4, 7, 9, `0x0c`-`0x1c`, `0x1f`, `0x21`, `0x24`-`0x2c` |
| Radar mast | `004979d4` | the same | 5, 6, `0x1d`, `0x1e` |
| Armed | `004978ac` | `00404100` | 8, `0x0b`, `0x20`, `0x23` |
| Triple turret | `00497784` | `004045c8` | `0x22` |
| GroundVehicle | `00497818` | `0046a5d0` | `0x2d`-`0x34`, `0x37`-`0x3d` |

Six indices — `0x0a`, `0x35`, `0x36`, `0x3e`-`0x40` — match no case, so nothing is constructed. Only three slots differ across the five tables: the destructor, this one, and `GetTorsoTwistAngle` (`+0x3c`).

**That last one is the armed/unarmed line.** The Armed and GroundVehicle tables install `Base_GetTurretAngle` (`00403594`), which returns a real aim angle from `structure+0x20f`; Plain and Radar mast keep the shared zero stub `SimObject_GetTorsoTwistAngleZero` (`00411a5c`).

**An ordinary building is born disarmed.** `Base_Construct` sets `+0xa5` ([the disarmed byte](component-damage.md#the-three-out-of-the-fight-bytes--0x99-0xa4-0xa5)) at spawn for exactly the two classes that keep the stub, Plain and Radar mast, and for none of the armed families. That the structure branch and the mech branch arrive at the same meaning from opposite directions is what settles the reading.

The library the shape comes from is picked per case too — the four Radar and four Armed cases construct from `dts\BASES_AN.DTS` and every other case from `dgs\BASES.DGS`. The switch alone decides it, not `+0x06`, though the two agree on every retail type.

The two constructors differ in what they leave at `+0x34`. The `BASES_AN.DTS` cases (`SimObjectBase_ConstructAnimated`, `0040332c`) build a full shape instance and set the root-motion-enabled byte at `+0x39`, so threads can attach and `SimObject_ApplyRootMotionIfEnabled` steps them. The `BASES.DGS` cases (`SimObjectBase_ConstructStatic`, `00403368`) leave a two-field cell-frame holder and that byte clear, so no thread can exist on them and the same call does nothing.

## The animation threads

Whatever class it ended up, every structure then runs the same tail of `Base_Construct`:

```
for (i = 0; i < 2; i++) {
    if (i >= typeRec+0x06) continue
    structure+0x1f9[i] = ShapeInst_AddThread(this, i)     // sequence i
    AnimThread_SetPlaybackRate(thread, typeRec+0x20[i])
    if (i == 1) AnimThread_SeekToPosition(thread, 1, 6000)
}
```

So **`BASES.DAT +0x06` is a thread count**, 0, 1 or 2, and `+0x20` is a `short` pair giving each thread its playback rate. Thread `i` plays sequence `i`, and thread 1 is seeded parked a little over a third of the way through its own sequence rather than at its start.

The eight types that state a non-zero count are the eight the constructor draws from `BASES_AN.DTS`, and each of those roots carries an animation list with exactly as many sequences as its type asks for:

| Type | Class | Root | Threads | Rates | Sequence frames |
|---|---|---|---|---|---|
| 5, `0x1d` | Radar mast | 0, 4 | 1 | 100 | 12 |
| 6, `0x1e` | Radar mast | 1, 5 | 2 | 30, 100 | 5, 12 |
| 8, `0x20` | Armed (gun) | 2, 6 | 2 | 0, 0 | 9, 12 |
| `0x0b`, `0x23` | Armed (launcher) | 3, 7 | 2 | 0, 0 | 9, 12 |

A rate of zero leaves the thread parked for something else to position it, which is exactly what an armed structure's turret seek does with both of its. The radar masts state real rates instead and spin freely — **that, and not the cell flipbook, is what turns a radar dish**: all four radar types state `-1` for their flipbook sequence and have no flipbook at all.

A thread only advances when something calls `SimObject_ApplyRootMotionIfEnabled`, and the only structure code that does is the plain tick and the turret seek.

### The root motion is inert on retail data

`SimObject_ApplyRootMotionIfEnabled` (`00402604`) forwards to `SimObject_ApplyRootMotion` (`0040250c`), the call a HERC's locomotion makes and the whole source of a HERC's translation ([`mech-locomotion.md`](mech-locomotion.md#root-motion)).

For a structure it moves nothing: **no sequence in `BASES_AN.DTS` sets the ground-movement flag**, so the transform read back is always identity. What the call does for a structure is the stepping — playing a dish's sweep, and re-posing the turret nodes a seek has moved.

## The plain tick — `Base_ThinkTick` (`00403ca8`)

Death sequence, then, while the structure still stands:

```
if (typeRec+0x24 >= 0 && Math_CountdownTimerTick(&structure+0x1f6) == 0) {
    structure+0x1f7 = typeRec+0x26                 // reload the interval
    cells[typeRec+0x24] = (cells[typeRec+0x24] + 1) % shape.sequenceCells[typeRec+0x24]
}
if (typeRec+0x06 != 0) SimObject_ApplyRootMotionIfEnabled(this, 100)
```

`BASES.DAT +0x24` is a **cell sequence index** and `+0x26` its **frame interval**, in the simulation's timer unit ([Timer units](dbsim-physics-notes.md#timer-units) — 256 of them is a frame every 125 ms). The array it steps is the same per-sequence cell array damage moves, so an idle animation and a collapsed part are one mechanism pointed at different sequences.

Eight retail types state a sequence, all of them sequence 0 on a 256-count interval — a frame every 125 ms: 8, 9, `0x0a`, `0x0b`, `0x1a`, `0x20`, `0x22`, `0x23`. **Only two of the eight reach this function** — 9 and `0x1a`, the two that are Plain. Types 8, `0x0b`, `0x20` and `0x23` are Armed and `0x22` is the triple turret; each of those ticks steps the same cell array from its own firing path instead, as a muzzle flash rather than a loop. `0x0a` matches no case and is never built. So the free-running flipbook belongs to exactly two structures in the game.

The last line is the animation step, for any type that states threads at all.

## The armed tick — `00404100`

A structure that has already fallen hands the whole tick to `Base_ThinkTick`, so a wrecked tower is an ordinary building again. While it stands: death sequence, then

- Retarget on a 10000-unit countdown at `+0x21d` — about five seconds, see [Timing constants](#timing-constants) — through `Ai_SelectTarget(this, 0x30, 0)`, reject own class, ignore bearing. Drops a target past 60000.
- Step the muzzle-flash cell on the `+0x1f6` countdown, gated on that countdown's own value at `+0x1f7` still being non-zero, and reload it **only while the cell has not wrapped back to 0**. So the flash plays the sequence through once and stops. Firing kicks it by writing 1.
- Lead the target: aim point from its vtable `+0x30`, then `Math_OffsetPointByBearing` along the target's heading by `range * targetSpeed / projectileSpeed`, the speed out of `PROJ.DAT` record 2. **Only a gun tower leads** — the launcher form never looks up a projectile speed, leaving the term zero, because its rounds track. Skipped past 40000.
- Aim through `Base_AimTurret` (`00403eec`), which also drives the turret animation.
- Fire from `(±300, 400, 0)` in the turret node's frame, both barrels, on a 1500-unit refire countdown at `+0x211` ([Timing constants](#timing-constants)). A type whose `+0x2e` is 2 fires `Rocket_Fire(0, …)` and everything else `Bullet_Fire(2, …)`.
- Gated on the aim error being inside ±1000 in both axes, on the range being inside 40000, and on the firing window being open.

`BASES.DAT +0x2e` is the type's **armament class**, read here as a value — 0 unarmed, 1 gun, 2 launcher — where the AI reads it as a flag ([`ai-combat-states.md`](ai-combat-states.md#basesdat-0x2e)). Retail states it on 8 of the 65 types:

| Types | Name | `+0x2e` |
|---|---|---|
| `0x0b`, `0x23` | MISSILE TOWER | 2 |
| 8, `0x20` | GUN TOWER | 1 |
| `0x2f` | MOBILE MISSILE | 1 |
| 3 | GENERATOR | 1 |
| `0x0a`, `0x22` | TRANSPORT | 1 |
| the other 57 | | 0 |

The stated armament and the tick part company on three of the eight. The generator is Plain and type `0x0a` is never constructed, so neither reaches a tick that would fire what it states, and the triple turret has a tick of its own that does not read the field at all. Type `0x2f` does fire what it states, through the ground vehicle tick's branch into this one ([below](#the-ground-vehicle-tick--0046a5d0)). See [Open](#open).

**The countdown at `+0x218` is a firing window, not a barrel selector.** Each expiry flips the flag at `+0x21b` and reloads the counter at `+0x219` from the pair at `004973e0`, both of whose entries are 10000 — **about five seconds** ([Timing constants](#timing-constants)) — so a tower fires for five seconds, holds for five, and repeats. Fire is gated on the flag being set.

**It is the launcher that rolls, not the gun.** A gun tower fires both barrels every time it is allowed to. A launcher rolls `rand & 0x1f == 0` for the first barrel and, only if that failed, again for the second — so it puts at most one round up per opportunity and usually none.

### What a structure is aimed at

`BASES.DAT +0x2c` is how far up the structure anything aiming at it aims. **All 65 retail types state one**, 1000 to 2000 world units, so a building is never shot at the ground point its model origin sits on. Two vtable slots carry it, read by different callers:

- **`+0x24`, `Base_GetAimNodeTransform` (`00403548`)** returns a node transform whose translation is `(0, 0, +0x2c)`. Homing rockets and bullets, the HUD target indicator and the line-of-sight ray read this slot — [`target-selection.md`](target-selection.md#aim-point--vtable-0x24).
- **`+0x30`, `Base_GetAimPoint` (`0040351c`)** fills two out triples. The shared base form (`SimObject_GetAimPointZero`, `00411a74`) zeroes both and the structure's writes `+0x2c` into the Z of the second. Its callers are the two tower ticks, which add the second triple to the target's position unrotated for their lead point; the camera attach, where the first triple is the eye and the second the orbit centre ([`external-views.md`](external-views.md#the-camera-object--cam)); and `Ai_AimAndFire`'s fallback for a target that is neither a machine nor a structure.

### What the turret's AI is, and is not

`Ai_SelectTarget(this, 0x30, 0)` on a five-second timer is the whole of it. Specifically, a tower does **not**:

- **shoot back at whoever hit it.** The only structure-side writers of `+0x1a4` that `es2_fieldscan.py` finds are this tick and the triple turret's; `Base_ApplyDamage` is not one, so there is no structure counterpart to `Mech_AiOnTakingFire`.
- **hold a behaviour state.** A structure has no behaviour block, which is also why `Ai_SelectTarget`'s "not engaged" weight null-checks past it.
- **take squad orders**, or feed the flee check: `Ai_SumAttackerRatings` walks `GlobalMechList`, so a tower holding a machine as its target adds nothing to what that machine thinks is shooting at it.

What a tower's target *does* reach is the two readers that take `+0x1a4` off the shared base rather than off a HERC: `Ai_SelectTarget`'s scoring — where an armed structure is the only thing that can trip the "re-index a class-1 candidate as a HERC" branch — and `Mission_IsClearOfThreats`, which widens its threat radius from 80000 to 100000 for an object that holds the subject. See [`ai-targeting.md`](ai-targeting.md).

The turret also scores its own candidates against **column 1** of the four weight tables, not column 0: vtable `+0x4c` is dispatched on the asker and the structure table installs a bare `return 1`.

## The turret seek — `Base_AimTurret` (`00403eec`) and `00403d5c`

`Base_AimTurret` transforms the aim point into the turret's own frame — the object's world transform inverted, then shape part 2's, which on all four armed roots is transform 2, the barrel node hanging off the traversing base at transform 1 — and takes the aim error from `Math_EulerToward`. `00403d5c` then runs both axes:

```
in   = clamp(angle >> 3, +/-0x100)
step = Q8(Q8(|in|, in), gain[axis])
RateLimitedMoveToward(&turretRate[axis], step, rateLimit[axis])
turretAngle[axis] = clamp(turretAngle[axis] + turretRate[axis], min[axis], max[axis])
AnimThread_SeekToPosition(thread[axis], axis, (unsigned)turretAngle[axis] >> 2)
```

and finishes with `SimObject_ApplyRootMotionIfEnabled(this, 100)`, which is what re-poses the nodes it just moved.

| Axis | Error term | Sequence | Gain `004973e4` | Rate limit `004973e8` | Stops `004973ec`/`004973f0` |
|---|---|---|---|---|---|
| 0 — elevation | `EulerToward.X` | 0, 9 frames | 2000 | 200 | ±4000 |
| 1 — traverse | `-EulerToward.Z` | 1, 12 frames | 2500 | 800 | full `short` |

**The traverse is the negated axis**, and the one with no stops: a full `short` range is no limit at all in binary angle, so a base turret traverses freely and only its elevation is held, to a little over 20°. The position is seeked as an *unsigned* Q14 fraction, so a negative angle lands in the far end of the sequence rather than off its front. **A structure's turret aims the way a HERC's does**: the angle seeks a position in a full-sweep animation rather than rotating a node — see [`torso-aim.md`](torso-aim.md).

## The triple turret — `004045c8`

Type `0x22` alone, and the only object in the game that **aims three turrets from one object**: it rewrites its own heading field to `heading + 0x1555`, evaluates three turrets `0x5554` (120°) apart, and restores the original heading at the end. Each turret runs three weapon slots against `DAT_004a9640`, an 11-`short` descriptor table: slot 0 fires `Rocket_Fire(3, …)` and slots 1 and 2 `Bullet_FireBurst(3, …)` through `WeaponMountTemplate_GetByWeaponId(8)`. Its acquisition is `Ai_SelectTarget(this, 0x10, 0x3000)` — **the `0x3000` bearing cone** [`ai-targeting.md`](ai-targeting.md) names as the base turret's.

Once every component but the first is at full damage, the tick sets `+0xa5` ([disarmed](component-damage.md#the-three-out-of-the-fight-bytes--0x99-0xa4-0xa5)), so a triple turret shot down to its base leaves the AI's fight without being destroyed.

A slot only fires while its own component is undamaged, and the missile slot installs the target on `this+0x1a4` across the `Rocket_Fire` call and clears it again straight after, purely so the round picks up a lock — the object holds no target otherwise.

Type `0x22` states no animation threads, so whatever it aims, it does not aim it by seeking one.

## The ground vehicle tick — `0046a5d0`

The mobile ground units, and the only structure class that moves. Gated on the group's first member answering `targetClass == 3`, so a ground-vehicle type dropped into a group led by anything else is an ordinary building.

```
if (typeRec+0x2e == 0) Base_ThinkTick(this)
else { 00404100(this); if (no target) TurretSeek(this, -turretAz, turretEl) }
if (!destroyed) {
    save position, pitch, heading
    GroundVehicle_Advance(this)              // 0046a70c
    SimObject_ConformToTerrain(this)       // 004029d8
    if (GroundVehicle_CollisionTest(this)) { // 0046a510
        restore the save; speed = 0
        back-off timer +0x223 = 3000, reverse flag +0x227 = (speed > 0)
    }
}
```

So a ground vehicle fights with the armed tick and moves with its own, and its block handling is `Mech_MovementTick`'s: restore the step and arm a back-off rather than detonate. **The turret seek's second caller is here** — with nothing acquired the two turret angles are fed straight back in negated, which walks the turret to centre.

- **`GroundVehicle_CollisionTest` (`0046a510`)** is `Mech_CollisionTest`'s two object sweeps standing alone — the same group `+0x14` action gate, the same asymmetric vtable `+0x5c` against `+0x7c` radius pair, and the same `Structure_GatherWalkCandidates` volume sweep behind them, whose result is the return value. It drops the machine's other two arms: there is no terrain test, so a ground vehicle drives up anything, and a block does no damage to what was hit. It does still call the blocker's vtable `+0x68`, which makes it the **second writer** of the `obj+0xb1` latch a ramming machine detonates on — see [`ai-combat-states.md`](ai-combat-states.md#the-charge--mech_behaviourramtick-0041e488).
- **`GroundVehicle_Advance` (`0046a70c`)** picks the group leader (`0046a4b8`: the first of up to four group members that is neither immobilised nor destroyed — so unlike a HERC group, a convoy promotes when the vehicle in front goes down), steers as leader or follower, then steps the position forward by `+0x220` along model Y, through the vehicle's whole frame rather than its heading alone.
- **Leader (`0046a8e4`)** drives the group's route: no waypoint after the cursor means steer 0 and speed 0; otherwise drive at it on the bearing between the two waypoints, and advance the cursor on arrival.
- **Follower (`0046a95c`)** keeps formation on the leader through its own vtable `+0x78` slot. Inside 90° of the leader's heading it matches speed — `leaderSpeed - alongTrackError >> 5`, clamped to `+0x100`/`-0x96` — and drives at a point 20000 ahead of its own post along the leader's heading, with no lateral steering term at all; outside it, it abandons the leader's heading and turns at the post itself at `distance >> 5`. The leader's speed it matches is that object's own `+0x220`, not its vtable `+0x38`, which answers zero for every structure.
- **The post is anchored on one object and rotated by another.** `Base_ApplyFormationOffset` (`00405c04`) is handed the *able* leader's position, but `Formation_RotateAndAddOffset` (`00411d64`) reaches past its caller for the group's member array slot 0 and rotates the `BFORMS.DAT` offset by that object's heading. The array is never compacted, so once the vehicle in the lead slot is destroyed a convoy is anchored on its new leader while still dressed on the wreck's last heading.
- **`SimObject_ConformToTerrain` (`004029d8`)** sits the vehicle on the ground — [below](#terrain-conform--simobject_conformtoterrain-004029d8).

### Terrain conform — `SimObject_ConformToTerrain` (`004029d8`)

It samples the ground at ±r forward and ±r right (`r` from vtable `+0x10`, the shape's own radius), takes pitch from `Math_Atan2Bam(2r, forward - back)` and roll from the left/right pair, and sets Z to the mean of the four samples. This is how a vehicle sits on a slope, and it is the only thing in the simulation that writes a structure's pitch and roll. It is not the vehicle's alone: `FlatObj_Draw` calls it for every ground shape ([`ground-shapes.md`](ground-shapes.md#the-draw-pass)).

### The control law — `0046a798` and `0046a854`

Everything above decides a steer and a speed and hands the pair to `0046a798`, the ground vehicle's `Mech_LocomotionTick`:

```
if (Math_CountdownTimerTick(&this+0x222) != 0)          // a back-off is running,
    speed = this+0x227 ? -200 : 200                     // and overrides the speed, not the steer
steer = clamp(steer, +/-0x100)
Math_RateLimitedMoveToward(&this+0x220, speed, 0x1e)    // the speed slews
heading += Q8(200, steer)                               // the steer *is* the heading change
```

**There is no turn rate and no inertia in the heading**: the clamped steer becomes heading within the same tick, at a little over 200 binary-angle units at full lock. Only the speed is rate-limited. Nothing here is scaled by the tick length — the steer gain, the speed slew and the forward step are all per-call constants, as the turret seek's are.

`0046a854` is the drive-to-point both steering halves call, and the counterpart of `Ai_DriveToPoint`. Same 10000-unit arrival range, and two differences:

- **It does not steer at the point it is given.** It steers at a point offset from that one along the caller's stated bearing by `9000 - range`, so while the vehicle is further out than 9000 the aim point sits *short* of the destination, back down the incoming line, and inside 9000 it swings past. Against the leader's route bearing that pulls a convoy onto the leg between two waypoints instead of letting each vehicle cut its own corner.
- **Its steering gain is four times a HERC's** — the bearing error over 16, not over 64.

A speed of zero means "none stated" and takes `0xaa`, the same default `Ai_DriveToPoint` uses.

**The retail mission handoff exercises none of the move half.** Its one ground vehicle (type `0x38`) rides in a group whose first member is a plain building, so the class gate keeps it parked — and it stands overlapping an armed tower, which would block every step it tried to take even if the gate let it move.

## Taking damage — `Base_ApplyDamage` (`00404d70`)

Vtable `+0x74`, the endpoint a direct-fire hit ([`hit-detection.md`](hit-detection.md#base_directfirehittest--00405038)) and a blast ([`damage-system.md`](damage-system.md)) both write into. Structures have a per-component health model, much simpler than a machine's ([`component-damage.md`](component-damage.md#the-component-damage-system)). The per-component state is the alive-flag array at `obj+0x201` and an 11-byte record per component at `obj+0x205`: `+0` damage, `+3` stage timer, `+5` stages of the death sequence left, `+7` attacker.

```
if (typeRec[+0x1e] != 0) return                       // invulnerable
if (componentIndex == -1) componentIndex = 0
if (!alive[componentIndex]) return
taken = damage[i] + incoming
destroyed = component.maxDamage <= taken
if (!destroyed && component.maxDamage / 2 < taken) {
    tenth = Q16Divide(10, maxDamage)
    for (a = Q16Multiply(damage[i], tenth); Q16Multiply(taken, tenth) > a; a++)
        if ((rand & 0xfff) <= 0x199) { destroyed = true; break }    // ~10% per step
}
if (!destroyed) { damage[i] = taken; return }
damage[i] = maxDamage; alive[i] = false; attacker recorded at state+7
if (vtable+0x40 == 0x100) {                            // Base_DamageFraction (004052b4), the Q8 damage fraction
    if (attacker is the local player's machine and this is its selected target) post computer message 0x2e
    if (attacker) attacker->vtable+0x60 credits the kill
    obj[+0x99] = 1; obj[+0x96] = 0
    Mech_ReportOutOfAction(obj)                        // 00411bc8
    fire the object's mission action (obj+0x1b6), if it has one
}
if (component[+4] != -1) { state[+5] = stageCount[component[+4]]; state[+3] = 300 }   // start the collapse
```

**A component can die early, at random.** Past half its maximum, one ~10% roll fires per tenth of the component's health the shot moved it through, so a heavy hit on a half-wrecked section usually finishes it before its stated hit points run out, and the same hit twice does not do the same thing.

`Base_DamageFraction` (`004052b4`, vtable `+0x40`) is a **ratio of sums**, not a count of destroyed components: `(Σ damage << 8) / Σ maxDamage`. A type with one 30000-point core and six 2000–8000-point parts is effectively destroyed by killing the core alone, which is how both seven-component retail types are authored.

The message is [`component-damage.md`](component-damage.md#what-the-endpoint-announces)'s `0x2e`, and the counters are [the out-of-action report](mission-deployment.md#the-out-of-action-report).

Spawn-time health comes from the block-9 record's starting condition (`+0x32`, [`script-dat.md`](../formats/script-dat.md)), a per-cent value that `Base_Construct` applies to every component. Negative or 100 leaves them undamaged. 0 spawns every component at full damage with its cell sequence stepped to the collapsed cell, flags the structure destroyed (`+0x99`) and installs its wreck shape (`BASES.DAT +0x04`). Anything else starts each component at `(100 - pct) * maxDamage / 100`.

## Open

- **Unported:** the triple turret (`004045c8`, [Type `0x22`](#the-triple-turret--004045c8)).
- **Unported:** the kill credit a destroyed structure hands its attacker (vtable `+0x60`).
- **Unported:** spawn-time component health from the mission record.
- **Open:** why the generator (type 3) and the transports (`0x0a`, `0x22`) state an armament of 1 when none of them reaches a tick that fires it. The AI's danger flag ([`ai-combat-states.md`](ai-combat-states.md#basesdat-0x2e)) reads all three as armed.
- **Open:** a tower's ranges against retail play. At `Hud_WorldUnitsToMetres` (`00434228`)'s confirmed scale of `(units / 1000) * 6`, the armed tick's 40000-unit fire gate is 240 m and its 60000-unit target drop 360 m, but retail towers are seen aiming from about 320 m and firing from about 200 m, short of both by a margin the scale does not account for.
- **Open:** the ground vehicle follower arm: no `script.dat` handoff examined places a second mobile vehicle for it to hold station on. The campaign's `.MSN` files are where to look for one.
