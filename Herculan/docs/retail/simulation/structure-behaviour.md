# Structure behaviour

What a `BASES.DAT` ([`bases-dat.md`](../formats/bases-dat.md)) structure does per tick, and how it takes damage. Hit detection is [`hit-detection.md`](hit-detection.md) and the collapse a lost part runs is [`destruction-effects.md`](destruction-effects.md#a-structure-coming-down). This doc owns the `+0x18` tick slot and the five classes that fill it, except the GroundVehicle class's tick and movement, which are [`ground-vehicles.md`](ground-vehicles.md).

## Timing constants

Every countdown in this doc is in the simulation's timer unit, which is not a millisecond: one count is about 0.49 ms ([Timer units](dbsim-physics-notes.md#timer-units)). The structure constants in real time:

| Constant | Units | Real time |
|---|---|---|
| Retarget `+0x21d` | 10000 | 4.9 s |
| Firing window `004973e0` | 10000 | 4.9 s each way |
| Refire `+0x211` | 1500 | 0.73 s |
| Transport firing window `004973f4` + `004973f8` | 5000 + rand(5000) | 2.4–4.9 s each way |

## Five classes, one switch

`Base_Construct` (`00405314`) switches on the type index and installs one of five vtables. The case labels below are the switch's own.

| Class | Vtable | `+0x18` tick | `BASES.DAT` type indices |
|---|---|---|---|
| Plain | `00497940` | `Base_ThinkTick` `00403ca8` | 0-4, 7, 9, `0x0c`-`0x1c`, `0x1f`, `0x21`, `0x24`-`0x2c` |
| Radar mast | `004979d4` | the same | 5, 6, `0x1d`, `0x1e` |
| Armed | `004978ac` | `00404100` | 8, `0x0b`, `0x20`, `0x23` |
| Transport | `00497784` | `004045c8` | `0x22` |
| GroundVehicle | `00497818` | `0046a5d0`, [`ground-vehicles.md`](ground-vehicles.md) | `0x2d`-`0x34`, `0x37`-`0x3d` |

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

Eight retail types state a sequence, all of them sequence 0 on a 256-count interval — a frame every 125 ms: 8, 9, `0x0a`, `0x0b`, `0x1a`, `0x20`, `0x22`, `0x23`. **Only two of the eight reach this function** — 9 and `0x1a`, the two that are Plain. Types 8, `0x0b`, `0x20` and `0x23` are Armed, whose tick steps the same cell array from its firing path instead, as a muzzle flash rather than a loop. `0x22` is the transport, whose tick never steps it, so the sequence it states is never played. `0x0a` matches no case and is never built. So the free-running flipbook belongs to exactly two structures in the game.

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

The stated armament and the tick part company on three of the eight. The generator is Plain and type `0x0a` is never constructed, so neither reaches a tick that would fire what it states, and the transport has a tick of its own that does not read the field at all. Type `0x2f` does fire what it states, through the ground vehicle tick's branch into this one ([`ground-vehicles.md`](ground-vehicles.md#the-ground-vehicle-tick--0046a5d0)). See [Open](#open).

**The countdown at `+0x218` is a firing window, not a barrel selector.** Each expiry flips the flag at `+0x21b` and reloads the counter at `+0x219` from the pair at `004973e0`, both of whose entries are 10000 — **about five seconds** ([Timing constants](#timing-constants)) — so a tower fires for five seconds, holds for five, and repeats. Fire is gated on the flag being set.

**It is the launcher that rolls, not the gun.** A gun tower fires both barrels every time it is allowed to. A launcher rolls `rand & 0x1f == 0` for the first barrel and, only if that failed, again for the second — so it puts at most one round up per opportunity and usually none.

### Where a tower starts aiming and firing

The 60000 and 40000 ranges are gates the tick tests, not the distances at which an approaching machine sees a tower react: a timer samples each of them, and the cockpit's metre readout measures differently.

**What the tower measures.** Both ranges are `Math_DistanceBetweenPoints` from the tower's origin to the target's: `Math_FastMagnitude3D` (`0047dd66`) of the offset, `L + 0.34375 M + 0.25 S` over the sorted absolute components, so the height difference counts. Two readouts show a range to the selected target, and only one is the tower's:

- The MFD status screen's `DIST:` ([`mfd.md`](mfd.md#mfdstatus--modes-0-and-4)) prints the same `Math_DistanceBetweenPoints` between the two origins, in **raw world units**: `_itoa` of the distance, with no metre conversion. A tower's gates are 60000 and 40000 on it. v1.10 prints it through `Hud_WorldUnitsToMetres`, which reads them as 360 and 240 ([`../retail-builds.md`](../retail-builds.md#how-v110s-programs-differ)).
- The scanner's `TRG:` ([`mfd-scanner.md`](mfd-scanner.md#readouts)) is in metres, through `Hud_WorldUnitsToMetres` (`00434228`, `(units / 1000) * 6`), of `Math_FastMagnitude2D` (`0047dd40`, `L + M / 2`) over the ground-plane offset from the viewing machine to the target (`MfdRadarScreen_Update`, call at `0043ed74`). On level ground it reads the tower's gates as 360 and 240 approaching along a world axis and up to 11.6% more at 45° to one. A height difference pulls it the other way, since the tower counts the height and the scanner does not: with the target a height `h` above or below the tower, `h` well short of the ground range, the scanner reads about a third of `h` less at each gate: `0.34375 h` along an axis, `0.28 h` at 45° to one.

- **Aiming starts at a retarget.** A target past 60000 is dropped on any tick, but one is picked up only when the `+0x21d` countdown expires, every 10000 units (4.9 s). `Ai_SelectTarget` reaches out to 100000 for anything not designated ([`ai-targeting.md`](ai-targeting.md#acquisition--ai_selecttarget-00411fa0)), so a machine further out than 360 m is picked and dropped in the same tick. A machine closing at `v` is first held at the first retarget after it crosses 360 m: anywhere from 360 m down to `360 m − 4.9 s × v`.
- **Firing starts with the window.** Inside 40000 the tower also needs the `+0x218` window open, and that window runs from the tower's spawn whoever is near: 4.9 s open, 4.9 s shut. A machine that crosses 240 m while it is shut is not fired on until it opens: anywhere from 240 m down to `240 m − 4.9 s × v`. With the window up, the first burst waits at most one 0.73 s refire.
- **A launcher's first round comes later still.** Each opportunity puts a round up with odds of about 1 in 16 (two 1-in-32 rolls), so its first launch takes about 16 refires, 12 s of open window, on average.

At a run both shortfalls are large — a HERC at 90 km/h covers about 120 m in 4.9 s — and at a crawl they vanish.

### What a structure is aimed at

`BASES.DAT +0x2c` is how far up the structure anything aiming at it aims. **All 65 retail types state one**, 1000 to 2000 world units, so a building is never shot at the ground point its model origin sits on. Two vtable slots carry it, read by different callers:

- **`+0x24`, `Base_GetAimNodeTransform` (`00403548`)** returns a node transform whose translation is `(0, 0, +0x2c)`. Homing rockets and bullets, the HUD target indicator and the line-of-sight ray read this slot — [`target-selection.md`](target-selection.md#aim-point--vtable-0x24).
- **`+0x30`, `Base_GetAimPoint` (`0040351c`)** fills two out triples. The shared base form (`SimObject_GetAimPointZero`, `00411a74`) zeroes both and the structure's writes `+0x2c` into the Z of the second. Its callers are the two tower ticks, which add the second triple to the target's position unrotated for their lead point; the camera attach, where the first triple is the eye and the second the orbit centre ([`external-views.md`](external-views.md#the-camera-object--cam)); and `Ai_AimAndFire`'s fallback for a target that is neither a machine nor a structure.

### What the turret's AI is, and is not

`Ai_SelectTarget(this, 0x30, 0)` on a five-second timer is the whole of it. Specifically, a tower does **not**:

- **shoot back at whoever hit it.** The only structure-side writers of `+0x1a4` that `es2_fieldscan.py` finds are this tick and the transport's; `Base_ApplyDamage` is not one, so there is no structure counterpart to `Mech_AiOnTakingFire`.
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

and finishes with `SimObject_ApplyRootMotionIfEnabled(this, 100)`, which is what re-poses the nodes it just moved. Its other caller is the ground vehicle tick, which uses it to walk an idle turret back to centre ([`ground-vehicles.md`](ground-vehicles.md#the-ground-vehicle-tick--0046a5d0)).

| Axis | Error term | Sequence | Gain `004973e4` | Rate limit `004973e8` | Stops `004973ec`/`004973f0` |
|---|---|---|---|---|---|
| 0 — elevation | `EulerToward.X` | 0, 9 frames | 2000 | 200 | ±4000 |
| 1 — traverse | `-EulerToward.Z` | 1, 12 frames | 2500 | 800 | full `short` |

**The traverse is the negated axis**, and the one with no stops: a full `short` range is no limit at all in binary angle, so a base turret traverses freely and only its elevation is held, to a little over 20°. The position is seeked as an *unsigned* Q14 fraction, so a negative angle lands in the far end of the sequence rather than off its front. **A structure's turret aims the way a HERC's does**: the angle seeks a position in a full-sweep animation rather than rotating a node — see [`torso-aim.md`](torso-aim.md).

## The transport — `004045c8`

Type `0x22` alone, class `LC_BASE`, named `TRANSPORT` by its MFD readout (`STRINGS0.STR` group 23 entry 24, through `BASES.DAT +0x28`). The model is a landed drop pod with three weapon stations round it, and **nothing on it moves**: the tick fires three stations from one object by rewriting its own heading. A fallen one hands the tick to `Base_ThinkTick`. A standing one runs its death sequence, adds `0x1555` (30°) to its own heading field, runs one station, adds `0x5554` (120°), and so on three times, then restores the heading. Each station therefore acquires, aims and fires in its own frame, and the object is never seen facing any of them.

Before the stations, unless `+0xa5` ([disarmed](component-damage.md#the-three-out-of-the-fight-bytes--0x99-0xa4-0xa5)) is already set, the tick sets it once every component but the first is at full damage, so a transport shot down to its core leaves the AI's fight without being destroyed. The stations still run on the tick that sets it; from the next tick they are skipped.

**Per station.** Each of the three `0x1f`-byte records at `+0x209` holds three 8-byte weapon slots, a retarget countdown at `+0x18` and a target pointer at `+0x1b`:

```
if (Math_CountdownTimerTick(&rec+0x18) == 0) {
    rec.target = Ai_SelectTarget(this, 0x10, 0x3000)        // reject own class, 0x3000 cone off this station's heading
    rec+0x19 = 10000
}
if (!rec.target) next station
range = Math_DistanceBetweenPoints(position, target.position)
aim   = target.position + target->vtable+0x30's second triple
error = Math_EulerToward(aim, position) - (pitch, roll, heading)
for slot in 0..2:
    component = 2 * station + (slot != 0) + 1
    if (damage[component] == maxDamage[component]) continue
    if (Math_CountdownTimerTick(&slot.window) == 0) {
        slot.open = !slot.open
        slot.window = 004973f4[slot.open] + Math_RandomBelow(004973f8[slot.open])
    }
    if (Math_CountdownTimerTick(&slot.refire) == 0 && slot.open
        && error.yaw in [-arc.yaw, arc.yaw) && error.pitch in [-arc.pitch, arc.pitch)
        && range < slot.range) {
        muzzle = position + Rotate2D(heading, offset.xy) + (0, 0, offset.z)
        fire the slot; slot.refire = slot.refireDelay
    }
```

The slot records' arcs, ranges, offsets and refire delays are [`LC_WPNS.DAT`](../formats/lc-wpns-dat.md). The aim error is taken from the structure's origin, not the muzzle, and the range to the target's position, not its aim point. A slot whose component is dead steps neither countdown.

**The components are the stations.** Type `0x22`'s seven components are a 30000-point core and three pairs: an 8000-point launcher pod (1, 3, 5) and a 2000-point beam housing hanging off it (2, 4, 6). Station `s`'s launcher slot fires while component `1 + 2s` stands and both its beam slots while `2 + 2s` does, and the pods' stated positions sit within a few hundred units of where the stations' launcher offsets put them, 30°, 150° and 270° round from the heading.

**The firing window** is the armed tick's duty cycle with a random length: each phase lasts `5000 + rand(5000)` timer units, about 2.4 to 4.9 s ([Timing constants](#timing-constants)), and each slot keeps its own. A slot's window starts shut with its countdown at zero, since a structure's pool is cleared once and its slots are never reissued ([`sim-object-layout.md`](sim-object-layout.md#only-the-short-lived-classes-are-recycled)), so the first tick the slot is live opens it.

**Slot 0 is an `EO` launcher, slots 1 and 2 are beams.**

- The launcher calls `Rocket_Fire(3, muzzle, euler, this, 0)` with the object's own euler triple, so the round leaves along the station's facing and not toward the target. It is the lock that brings it round: the tick installs the station's target on `+0x1a4` across the call and clears it straight after, and `Rocket_Fire` attaches `+0x1a4` to the round, without consulting the lock state, for a subtype-3 round whose owner is not locally piloted. The object holds no target otherwise.
- A beam calls `Bullet_FireBurst(3, frame, range, this, power)` with a frame pointed from the muzzle at the aim point by `Math_EulerToward`, so a beam is aimed. Its range and power are `LAS100`'s, weapon template 8's `+0x30` and `+0x38` ([`weapons-dat-sim.md`](../formats/weapons-dat-sim.md#decoded-tail-fields)), through `WeaponMountTemplate_GetByWeaponId(8)`.

Type `0x22` states no animation threads and its tick never steps a cell sequence, so nothing on the model moves when it aims or fires.

## Taking damage — `Base_ApplyDamage` (`00404d70`)

Vtable `+0x74`, the endpoint a direct-fire hit ([`hit-detection.md`](hit-detection.md#base_directfirehittest--00405038)) and a blast ([`damage-system.md`](damage-system.md)) both write into. Structures have a per-component health model, much simpler than a machine's ([`component-damage.md`](component-damage.md#the-component-damage-system)). The per-component state is the alive-flag array at `obj+0x201` and an 11-byte record per component at `obj+0x205`: `+0` damage, `+2` stage countdown (a 3-byte countdown record whose counter is the short at `+3`), `+5` stages of the death sequence left, `+7` attacker.

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

### Starting condition

Spawn-time health comes from the block-9 record's starting condition (`+0x32`, [`script-dat.md`](../formats/script-dat.md)), a per-cent value. It is the last thing `Base_Construct` does, after the class switch and the animation threads and before it stores the flipbook interval (`+0x1f7`) and the type record pointer (`+0x1f2`). Having set every alive flag at `+0x201`, it walks the components:

```
for each component i:
    state[i]+5 = 0                                                       // stages left
    if (pct < 0)       damage[i] = 0
    else if (pct == 0) {
        damage[i] = component.maxDamage
        if (component[+2] >= 0) shapeInstance[+8][component[+2]] = 1     // the collapsed cell
    }
    else               damage[i] = (short)((100 - pct) * component.maxDamage / 100)
if (pct == 0) {
    obj[+0x99] = 1; obj[+0x96] = 0
    if (typeRec[+0x04] >= 0) shapeInstance[+4] = hulkShapes[typeRec[+0x04]]   // the hulk swap
}
```

So 100, like any negative value, leaves the components undamaged, and 0 places the structure already fallen: destroyed, scanner off, parts on their rubble cells and the wreck installed. That is all it does. No mission action fires, no out-of-action report is made, and no death sequence, fire or debris starts.

**The alive flags stay set.** `Base_DirectFireHitTest` tests `+0x99` before it writes damage, so direct fire leaves such a structure alone, but `Base_ApplyExplosiveDamage` tests only for a wreck. A starting-condition-0 structure whose type has no wreck and a `BASECOL.DAT` model therefore still takes blasts. Every part a blast reaches is already at its maximum, so it dies at once: it starts its death sequence, and `Base_ApplyDamage`'s fallen branch runs a second time: the computer message if the player had it targeted, the kill credit, the out-of-action report and the mission action. Retail states one such record, the type 0 at row #14 record 26 of `C4_06.MSN`. The other eight retail records that state 0 for a real type are wreck-leaving types 8 and `0x22`, which the wreck test protects.

## Open

- **Deferred:** why the generator (type 3) and the transports (`0x0a`, `0x22`) state an armament of 1 when no tick any of them reaches reads it. The AI's danger flag ([`ai-combat-states.md`](ai-combat-states.md#basesdat-0x2e)) reads all three as armed.
