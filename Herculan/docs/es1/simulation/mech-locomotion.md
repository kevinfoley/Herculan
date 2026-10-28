# DBSIM.EXE — the HERC's movement: throttle, gait, root motion, collision and footfalls

**ES1's HERC moves as ES2's does: the throttle sets a speed, a gait machine picks the walk, run, stop and turn sequences and their pace from it, and all the ground the HERC covers comes from those sequences' root motion ([ES2's locomotion](../../retail/simulation/mech-locomotion.md)).** What a player would see differently:

- it walks on flat ground: no slope slows it, speeds it or slides it, and no slide landing hurts it;
- its control law cuts its speed for leg damage only, where ES2's also cuts it for reactor damage;
- below speed 45 it cannot turn while moving, and its turn rate rises from nothing at 45 to its peak at half its top speed, where ES2's starts at half its peak; the peak is the chassis file's turn rate, where ES2's turns 1600/1024 times as fast;
- its legs take a new pace only once its speed stops changing, except while it speeds up in a walk, where ES2's follow every change of speed: a HERC slowing from a run keeps its running pace until its speed falls under the walk-to-run speed;
- its footfall code tests the player's own HERC alone, with no bob of the view, and sounds once more as the player's HERC falls and its body comes down.

Addresses are `ES1DBSIM` image addresses ([the X-32 executable](../formats/x32-executable.md#the-image)); ES2's are DBSIM's. Every function here removes its own arguments, so argument lists are in stack order, the machine first and the other arguments in the reverse of ES2's order ([calling conventions](../formats/compiler.md#calling-conventions)). The machine's fields are [the HERC's](mech.md#the-fields); the type record is at the machine's `+0x188`, its offset *N* being the chassis file's offset *N* − 2 ([the layout](../formats/chassis-dat.md#the-layout)).

## The throttle — `Mech_ApplyThrottleInput` (`00011afc`)

(machine, throttle axis, steer), from `Sim_PollPlayerInput` (`0005a0c0`, [the player's controls](main-loop.md#the-players-controls--sim_pollplayerinput-0005a0c0)), the one call `es2_xref.py` finds. ES2's ([the control law](../../retail/simulation/mech-locomotion.md#control-law-mech_locomotiontick)) without its first block, which reads the axis as an absolute lever position: ES1's always reads it as a rate.

1. The axis is negated when `ThrottleLeverMode` (`000919ce`) is negative.
2. The steer is negated when |axis| is at most `0x3c` and the throttle `+0x226` is 0 or more, or when |axis| is above `0x3c` and the axis is 0 or less, as ES2's.
3. The step is `Math_Q8Multiply`(−axis, `0x91`). When it is not 0, `+0x8d` is set and the throttle moves by it, clamped to the range below; when the throttle is not 0 and the new value has the other sign, the throttle becomes 0 instead, so a sign change waits a tick at zero. (The zero test is of the throttle's low 12 bits, which inside the clamp is a test for 0.)
4. `Mech_LocomotionTick`(machine, 0, `Math_Q10Multiply`(throttle, the negated reverse top speed `+0x04` when the throttle is negative, else the forward top speed `+0x06`), steer).

| `ThrottleLeverMode` | Throttle range | Axis |
|---|---|---|
| 0, the image's value | −`0x400` to `0x400` | as read |
| 1 | 0 to `0x400` | as read |
| −1 | −`0x400` to 0 | negated |

A scan of the image's bytes for `ThrottleLeverMode`'s address finds the two reads here and three stores, in two pieces of code:

- **Preferences option 10** (`000bd56c`), the option that has `Ctrl+I` and `Ctrl+M` step the throttle ([the input build](input.md#the-input-build--input_buildplayerdevice-0004cb14)). Its handler, `Input_SetThrottleLeverMode` (`0004ad94`), sets 1 when the option is on and 0 when it is off. `Prefs_Load` (`0004a744`) calls every option's handler with its value once it has read the 17 option bytes of `data\prefs.cfg` (all 0 when the file does not open), and `Prefs_SetOption` (`0004a914`) calls it when it applies option 10; `Prefs_Init` (`0004afe8`) registers the handler with `Prefs_SetHandler` (`0004ac4c`) before its call of `Prefs_Load`. ES2's handler, `Input_SetThrottleLeverMode` (`00459d20`), sets 1 only for a value of 1 with a throttle control fitted.
- **`Ctrl+1`** ([commands](main-loop.md#commands--sim_dispatchcommand-00059df8)) sets −1 when it is above 0 and 1 otherwise: from 0 the first press makes the throttle forward only, and each press after switches it between forward only and reverse only. Reverse only also negates the axis, so the control that opens the throttle forward (keypad 8, the stick pushed forward) drives the HERC backward, and the one that closes it (keypad 2, the stick pulled back) only brings it back to a stop. The clamp runs only on a tick whose step is not 0, so a throttle the press leaves outside the new range holds until a throttle control moves it: a HERC walking forward when reverse only comes on keeps walking, and the first step of either control stops it.

The multiplies round ([the primitives](fixed-point-math.md#the-primitives)).

## The control law — `Mech_LocomotionTick` (`00012038`)

(machine, avoidance argument, desired speed, steer): ES2's control law and gait machine on these fields:

| Type record | File offset | Field |
|---|---|---|
| `+0x02` | 0 | the top turn rate |
| `+0x04`, `+0x06` | 2, 4 | the reverse and forward top speeds |
| `+0x08`, `+0x0a` | 6, 8 | the speed and turn-rate steps, per tick |
| `+0x0e`, `+0x10` | 12, 14 | the walk and run sequences |
| `+0x12`, `+0x14` | 16, 18 | the forward and reverse stop sequences |
| `+0x2e` | 44 | the walk-to-run speed `thr` |
| `+0x46` | 68 | the death sequence |
| `+0x6e` | 108 | the reverse walk-to-run speed `thrRev` |
| `+0x7c` | 122 | the turn-in-place sequence |

The machine's speed is `+0x224`, its turn rate `+0x222`, its heading `+0x0a`, the locomotion thread `+0x1c0` and the playback rate `+0x236`.

### The speed

1. **Immobilized** (`+0x9e`): the desired speed and the steer are 0.
2. **Otherwise**, while `Timer_CountDown` (`00064178`) of the unstick countdown `+0x203` runs, the desired speed is the reverse top speed when `+0xa8` is set and the forward one when not; once it has run out, the desired speed is clamped to the two. ES2's adds the slope term before its clamp and clears its unstick direction (ES2's `+0xae`) when the countdown has run out; ES1's leaves `+0xa8` as it is, for `Mech_AiObstacleAvoidance` to read ([obstacle avoidance](#obstacle-avoidance--mech_aiobstacleavoidance-00011c80)).
3. **A machine other than the player's** (`PlayerMech`, `000bc42a`) that is not immobilized runs `Mech_AiObstacleAvoidance`(machine, avoidance argument, &desired, &steer).
4. **The legs**: `+0xa3` scales the desired speed by `Math_Q10Multiply` 400, otherwise `+0xa2` by 750. ES2's also scales it for its two reactor grades, and adds the Turbo Pod's term.
5. The steer is clamped to ±`0x100`, and the speed moves toward the desired speed by `+0x08` with `Math_RateLimitedMoveToward` (`0005e598`).

### The gait machine

ES2's ([the gait state machine](../../retail/simulation/mech-locomotion.md#the-gait-state-machine)) on the sequences above, row for row, including the turn-in-place exclusion, the speed frozen through a transition frame, the fall of an immobilized machine (rate 100 and sound `0x1e` as it is aimed at the death sequence, then `0x78`, and once the sequence has played out `+0xae` latched, `Mech_SpreadImpactDamage` (`00013078`)(machine, `0x78`, `0x96`) and sound `0x29`, both sounds at the machine's position and only while `+0xae` is clear) and the thresholds `thr` and `thrRev`, except in three rows, where ES1's leaves the playback rate as it was:

| Speed magnitude against last tick's | Playing | ES1 | ES2 |
|---|---|---|---|
| rising | anything but the walk and the two stop sequences | drop any request | drop any request; rate `S` |
| falling | the run, `S` at or above `thr` | drop any request | the same; rate `S` |
| falling | the walk, `S` not 0 | drop a pending run request | the same; rate `S` |

So the rate follows the speed only while the speed holds steady and while it rises in the walk; the stop and downshift rows set it as ES2's do. After the machine there is no crawl lift: a speed of 1 to 44 plays at its own rate, where ES2's lifts a rate under 60 to 60.

### Turning

The turn target is `Math_Q8Multiply`(steer, tent), and the turn rate moves toward it by `+0x0a`; the heading takes the turn rate and `+0x2c`, the rotation word, is cleared. The tent is 0 when a gait row held the turn this tick or |`S`| is under 45; otherwise, with *h* = (forward top speed − 45) >> 1 and *s* = |`S`| − 45, it is *T* × *s* / *h* up to *s* = *h* and *T* − *T* × (*s* − *h*) / (2 × (forward top speed − 45 − *h*)) above, *T* the top turn rate: 0 at speed 45, *T* at half way to the top speed, *T* / 2 at the top speed. ES2's takes a speed of 1 to 44 as 45, rises from *T* / 2 as *T* × (*s* + *h*) / (2*h*), and scales the tent by `Math_Q10Multiply` 1600 before the steer.

## Obstacle avoidance — `Mech_AiObstacleAvoidance` (`00011c80`)

(machine, flags, desired speed, steer), ES2's ([obstacle avoidance](../../retail/simulation/ai-navigation.md#obstacle-avoidance--mech_aiobstacleavoidance-00416274)), `es2_xref.py` finding its one call in `Mech_LocomotionTick` for a machine other than the player's. It returns at once when the speed `+0x224` and the desired speed are both 0, or when the desired speed is negative and the unstick countdown's `int32` counter `+0x204` is 0, as ES2's does. Otherwise the left and right figures start at 22000, and:

1. **The probes**, with flags bit 0 clear: the two rays of ES2's geometry, from (∓1500, 0, 0) to (∓10000, 20000, 0) in the machine's frame (`Context_GetTransform` of `+6`, `Transform_ApplyToPoint`), the left from −1500 and the right from 1500, at no ground height. Each goes first through `Sim_RaycastShapes` (`00020b70`, [the zone shapes](terrain.md#the-tests-against-the-shapes))(0, to, from, result), whose hit sets the side's figure to its range, and then through `Structure_RaycastShapes` (`000038b8`, [the structures](terrain.md#the-structures-tests))(−1, 0, to, from, result), every side's structures, whose hit sets it only when nearer. ES2's walks the terrain as its second test.
2. **Other objects**, with flags bit 0 clear: each live object but itself whose group holds no action (the dword `+0x16` of its group `+0x3f` is 0) and whose slot `+0x60` answers non-zero is taken at *d* = `Math_Q10Multiply`(`Math_DistanceBetweenPoints` from it to the machine, 3D, 2000). When *d* is under either figure, its bearing *b* is `Math_HeadingToward` from the machine's `+0x20` to its `+0x20`, less the heading, as an `int16`; with |*b*| (|−`0x8000`| taken as `0x7fff`) under `0x2000`, a negative *b* sets the right figure to *d* and a *b* of 0 or more the left, each only when *d* is under that figure. An object only ever lowers a figure. ES2's reads slot `+0x7c`.
3. **Resolving it**, when either figure is under 22000: with `+0xa8` set and both under 22000, the unstick side `+0x1e8` (±5000, [the move](#the-move)) is added to the left figure. With *n* the nearer of the two after that, the steer gains (22000 − *n*) × 2000 / 22000, an `IDIV` that truncates toward 0, cut to an `int16` and negated when the left figure is at most the right, so the machine turns away from the nearer side. The desired speed becomes 0 when *n* is negative, as when the unstick side's −5000 is added to a left figure under 5000. The code has a second store after that test, half the forward top speed, behind the same test's opposite, and it never runs.

ES2's also keeps out of the player's line of fire, takes a quarter off the unstick side's figure when the machine is backing out and both sides are clear, gains 700, or 1000 for a firing-line reading, and sets the desired speed to the reverse top speed for a firing-line reading under 3000.

## The move

**`Mech_MovementTick` (`00014844`)**, (machine), the move of the HERC's behavior states (`es2_xref.py` finds 17 stored pointers in `Behaviour_BuildStateTable`), is ES2's ([what a refused move does](../../retail/simulation/mech-locomotion.md#what-a-refused-move-does)) with no ground height and no slide: `SimObject_PushTransform` (`00002184`) and `SimObject_PopTransform` (`00002248`) save and restore the transform ([saving and restoring the transform](object-classes.md#saving-and-restoring-the-transform)); a refused move, the collision answer's low 16 bits not 0, unsticks a machine other than the player's with `+0x204` = 20000 (ES2's 10000), the side in `+0x1e8` 5000 when bit 0 of `Math_RandomNext`(`000b5bd4`) is clear and −5000 when it is set, and `+0xa8` set for a speed of 1 or more, and plays `0x29` for the player's; it zeroes the throttle, sets `+0x8d`, and bounces or cuts to the stop sequence as ES2's, the bounce's speed the negated speed held to the record's `+0x06` above and `+0x6e` (file offset 108, the reverse walk-to-run speed) below; then `Mech_PlaceLegsOnGround`. It returns 1.

**`Mech_IntegrateMotion` (`00014148`)**, (machine): ES2's (`00418f40`): `AnimThread_SetPlaybackRate` (`000626c8`)(thread `+0x1c0`, rate `+0x236`), then `SimObject_ApplyRootMotionIfEnabled` (`0000215c`, [root motion](object-classes.md#animation-threads-and-root-motion))(machine, `Math_IntegrateRateOverTick` (`00063fa4`)(100)).

### How fast it walks

The root-motion step's time is `Math_IntegrateRateOverTick`(100), 100 × `SimTickDelta` / 256 rounded. `SimTickDelta` is Q8 with 1.0 a tenth of a second by the clock's timer rate ([the tick length](fixed-point-math.md#the-tick-length--time_beginsimtick-00063f40)), so the step is the tick's length in milliseconds, 25 to 176 by its clamp; ES2's same call gives 0.8 of the tick's milliseconds, its unit being 125 ms. The thread advances step × rate / 256 animation ticks (`AnimThread_Advance`, `00062718`, ES2's), so at a steady playback rate *r* the animation plays 1000 × *r* / 256 ≈ 3.906 × *r* ticks a second, against ES2's 3.125 × *r* ([resulting speed](../../retail/simulation/mech-locomotion.md#resulting-speed)).

In a steady walk or run the rate is the speed `S`, so the HERC covers

  3.906 × `S` × *G* world units a second,

*G* being the sequence's root translation per animation tick over a cycle. `S` is the chassis file's speed scale as written: ES1's chassis load does no rescale ([the load](../formats/chassis-dat.md#the-load)). A stop or step-off sequence plays at ±100 and a turn in place at Q10(350, steer), as ES2's.

## Collision — `Mech_CollisionTest` (`00014174`)

(machine), ES2's ([collision](../../retail/simulation/mech-locomotion.md#collision)) without the steep-ground test and the slide, and with no structure record:

1. **An object in the way**: each live object but itself whose group holds no action and whose slot `+0x60` is not 0, closer than its own and the machine's slot `+0x60` together. It calls the object's slot `+0x6c` (`+0xab`, run into) and, when the object's target class `+0x162` is 0, a HERC, does ES2's collision damage ([a collision](../../retail/simulation/damage-system.md#a-collision--mech_collisiontest-00418f74)) with ES1's rounding (`Math_Q10Multiply`, `0005e818`, rounds to nearest) and one narrowing:
   - the mover's momentum, `Math_Q10Multiply`(its speed `+0x224`, its type record's mass `+0x4e`), is cut to an `int16`, where ES2's keeps 32 bits;
   - the struck machine's speed `+0x224` as the vector (0, speed, 0) is turned by its rotation (`Context_GetTransform` of its `+6`) and then by the transpose of the mover's, and the result's Y, the struck machine's travel along the mover's forward axis, goes with the struck machine's mass to `Math_Q10Multiply` as an `int32`;
   - the damage is `Math_Q10Multiply`(the difference, 300), an `int32`, and only a damage above 200 does anything;
   - the point's X and Y are the sums of the two positions' (`+0x20`) X and Y, each halved by an arithmetic shift, and its Z is the lower of the two eye nodes' Z: each machine's node array entry (the shape instance `+0x2e`'s `+0x16`, 32 bytes an entry) at the node index of the part the record's `+0xc` names, found through the shape's slot `+0x24`, its `+0x1c`, a height in the machine's own frame, with no fallback for a missing part, whose null entry is read at address `0x1c`;
   - `Mech_ApplyExplosiveDamage`(machine, other, 1200, point, damage) and then (other, machine, 1200, point, damage), which read the radius and the damage as `int16` ([a blast](mech.md#a-blast--mech_applyexplosivedamage-00013a7c)), so a damage past 32767 wraps.

   It returns 1.
2. **The structures**: 1 when `Structure_GatherWalkCandidates` (`0000375c`, [the structures](terrain.md#the-structures-tests))(radius, position) answers non-zero: the position inside the collision volume of a structure whose type has no collision model, `BASES.DAT` types 0–4 and 7. With no structure spawned it answers 0.
3. **The zone's shapes**: otherwise `Zone_WalkCollisionTest` (`00020860`, [a point](terrain.md#a-point--zone_walkcollisiontest-00020860))(radius, position)'s answer.

## Shadows and footfalls — `Mech_PlaceLegsOnGround` (`000144dc`)

(machine): ES2's (`004195c8`) on the shadows at `+0x1cc` (count `+0x1d0`, [the shadows](mech.md#the-shadows)), with no test for a missing locomotion thread or part: a part the node lookup answers 0 for is read at address `0x14`. Each shadow takes the position of its part's node through the machine's frame, the machine's heading and a cleared rotation word, as ES2's ([a HERC's shadows](../../retail/simulation/ground-shapes.md#a-hercs-shadows)). Entry *i* of the record's lists at `+0x77` (the part) and `+0x72` (the kind) goes with shadow slot *i*. A null slot, a leg's shadow [the damage write](mech.md#the-damage-write--mech_componentdamagewrite-00013250) destroyed, is passed over, and the footfall latch moves on only for a slot that is present: after a null slot each later shadow tests the latch of the slot before it, `+0x24a` + *i* − 1 where it would have tested `+0x24a` + *i*, while its part and kind entries are still its own slot's.

The footfalls are ES2's ([footfalls](../../retail/simulation/mech-locomotion.md#footfalls)) on the latches `+0x24a` + *i* and `+0x24e`, with these differences:

- **Only the player's machine** (`+0x9d`) is tested, and its sound plays at its position (`Sound_PlayAt`, `0004deb8`) at any distance. ES2's tests any machine within 15000 of the view as well, and plays only within 15000.
- **No step kick.**
- **The death arm sounds**: while the death sequence plays, the body's entry (kind 2) plays sound `0x1f` once when its part's world Z falls below the record's `+0x9e`, file offset 156, 400 on every retail chassis but `COLOSSUS`, whose is 800 ([the layout](../formats/chassis-dat.md#the-layout)), the latch `+0x24e` being the one `Mech_Constructor` sets ([construction](mech.md#construction)). ES2's sets no sound there.

The thresholds are the file's offsets 150–156, where a foot plants, and 170–174, where it arms ([the layout](../formats/chassis-dat.md#the-layout)): for walking (and turning and stopping) forward, walking backward and running in turn, the foot's fore-and-aft position in the machine's frame against each, and for the fall the body's height against 156. `es2_xref.py` finds two calls: `Mech_MovementTick` and `Mech_BehaviourRamTick` (`00018278`).

## Open

- **Open:** a retail check of `Ctrl+1` with option 10 off: from standstill the first press keeps keypad 2 from backing the HERC up; after a second press keypad 8 backs it up and keypad 2 stops it.
- **Open:** which sample sound `0x1f` is, and whether a retail fall plays it. A retail check: have the player's HERC's legs shot out; between the sound as it starts to fall and the thump as it lands, a third sound plays as its body passes 400 units above the ground.
- **Open:** a retail check of the pace: from a full-speed run, pull the throttle to zero at once. ES1's code keeps the full running pace until the HERC drops to a walk and then the walk-to-run pace until it stops, where ES2's slows its legs every tick.
- **Open:** what stops a walker at a structure of types 5, 6 or 8, which `Structure_GatherWalkCandidates` leaves out: whether step 1's slot `+0x60` answers non-zero for one, as ES2's object sweep does for the types its volume gather leaves out.
