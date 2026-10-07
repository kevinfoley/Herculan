# Herc locomotion — throttle, steering, and animation root motion

Reverse-engineered from `DBSIM.EXE` (`mechsys.cpp`) in the `ES2Recon` Ghidra project. Covers ground Hercs only. The Razor (`typeRec+0x50 != 0`) takes different paths throughout — a different control law, a different move and a real velocity vector; see [`razor-flight.md`](razor-flight.md).

**Core fact: Hercs have no velocity vector.** All translation and all turn-in-place rotation come from the walk/run/turn animations' root-node motion. The control law only sets a speed scalar, a turn rate, and an animation playback rate.

## Call graph

| Address | Name | Role |
|---|---|---|
| `00460764` | `Sim_PollPlayerInput` | Reads device axes, dispatches player control |
| `0045fdac` | `Sim_DispatchCommand` | Keyboard command dispatch, by scancode |
| `00415498` | `Mech_GetSpeed` | Mech vtable `+0x38`: `Q10(2000, mech+0x28e)`, or `mech+0x2bd` for a flyer |
| `004160dc` | `Mech_ApplyThrottleInput` | Stick/key throttle → `mech+0x290`, computes desired speed |
| `00416a04` | `Mech_LocomotionTick` | Control law: speed, turn rate, animation rate, gait state machine |
| `0041693c` | `Mech_ApplyTerrainSlopeToSpeed` | Uphill/downhill speed modifier |
| `00416274` | `Mech_AiObstacleAvoidance` | AI only — skipped when `mech == DAT_004a9c08` (player). [`ai-navigation.md`](ai-navigation.md#obstacle-avoidance--mech_aiobstacleavoidance-00416274) |
| `0041a360` | `Mech_MovementTick` | Per-tick physics: integrate, terrain-clamp Z, collide |
| `00418f40` | `Mech_IntegrateMotion` | Steps animation, applies root motion |
| `0040250c` | `SimObject_ApplyRootMotion` | Root-motion → world position/heading |
| `00402628` / `004027fc` | `SimObject_PushTransform` / `PopTransform` | Save/restore full transform incl. node hierarchy |
| `00418f74` | `Mech_CollisionTest` | Returns nonzero on blocked move |
| `004195c8` | `Mech_PlaceLegsOnGround` | Moves each of the machine's shadows under its part ([`ground-shapes.md`](ground-shapes.md#a-hercs-shadows)); footfall detection |
| `0041a550` / `0041a808` | `Mech_TorsoTwistTick` / `Mech_TorsoPitchTick` | Turret aim, not locomotion — [`torso-aim.md`](torso-aim.md) |

`Mech_MovementTick` is the **move** slot of the AI behaviour state a machine currently holds, shared by 18 of the 22 states; it decides nothing, the think function calls `Mech_LocomotionTick` with the steering. How the slot is reached, and which states differ, is in [`ai-dispatch.md`](ai-dispatch.md).

## Mech instance fields

| Offset | Type | Meaning |
|---|---|---|
| `+0x0c/0x0e/0x10` | short×3 | Euler angles; `+0x10` is heading (yaw) |
| `+0x12` | 32 B | World transform record (Q14 matrix, translation at `+0x26`) — [`sim-object-layout.md`](sim-object-layout.md#the-objects-frame-is-a-transform-and-its-position-is-that-transforms-translation) |
| `+0x26/0x2a/0x2e` | int×3 | World position X/Y/Z |
| `+0x32` | short | Rotation-matrix-dirty flag |
| `+0x34` | ptr | `TSShapeInstance` |
| `+0x1f2` | ptr | Mech type record (`MECH_TYPE_DATA[i]`) |
| `+0x22c` | ptr | Animation thread (`mech[0x8b]`) |
| `+0x28c` | short | Current turn rate (per tick) |
| `+0x28e` | short | Current speed scalar |
| `+0x290` | short | Throttle setting, Q10, clamped ±0x400 |
| `+0x230/0x234` | ptr | Turret twist / pitch animation threads — [`torso-aim.md`](torso-aim.md#three-threads-per-machine) |
| `+0x294/0x298` | short | Turret twist rate / angle. Both angles are binary angle measure relative to the machine's own heading |
| `+0x296/0x29a` | short | Turret pitch rate / angle |
| `+0x31c` | short | Countdown after which ATT with nothing selected latches the centring mode — [`torso-aim.md`](torso-aim.md#automatic-turret-tracking--t) |
| `+0x2a0` | short | Animation playback rate (Q8 multiplier) |
| `+0x93` | byte | Throttle-dirty flag (input changed it this frame) |
| `+0x317` | ptr | Turbo Pod mount (id 31) — [Damage effects on movement](#damage-effects-on-movement) |

Angles are 16-bit binary angle measure: **65536 = 360°**. Confirmed by a full-sweep animation (`OUTLAW` seq 5) stepping `0, 8190, 16380, 24570, 32760, -24570, -16380, -8190` = 8 × 8190 ≈ 65536, and by turn-in-place keyframes of 1820 = 10.00°.

World scale is 166.667 units/metre (see `docs/herculan/planning.md`).

## Mech type record

Loaded by `MechType_InitOne` (`004201a8`) as a 216-byte little-endian record into `MECH_TYPE_DATA[i]+2`. **Record offset N = `typeRec+N+2`.** Record offsets count from the start of the entry's content, past the per-entry prefix ([`vol-archive.md`](../formats/vol-archive.md#the-per-entry-prefix--fixed-9-bytes)).

| rec | typeRec | Meaning |
|---|---|---|
| 0 | `+0x02` | Max turn rate (not rescaled at load) |
| 2 | `+0x04` | Max reverse speed (negative) |
| 4 | `+0x06` | Max forward speed |
| 6 | `+0x08` | Linear accel step, per tick, **not** dt-scaled |
| 8 | `+0x0a` | Turn-rate accel step, per tick, **not** dt-scaled |
| 10 | `+0x0c` | Node the cockpit eye rides |
| 12 | `+0x0e` | Walk sequence id |
| 14 | `+0x10` | Run sequence id |
| 16 | `+0x12` | Stop/step-off sequence, forward |
| 18 | `+0x14` | Stop/step-off sequence, reverse |
| 20 | `+0x16` | Ride height added to terrain height |
| 22 | `+0x18` | Height of the direct-fire hit cylinder's centre above the machine's origin: 1000 heavy and medium, 750 light, 0 RAZOR. Read by `Mech_ShieldAbsorb_DirectFire` — [`damage-system.md`](damage-system.md#direct-fire-damage-armor-then-part-deterministic-shield-gated) |
| 24 | `+0x1a` | Radius of that cylinder, and of the coarse reject in front of it: 2500 heavy, 1500 medium, 1000 SPIDER. Deliberately generous — it only has to be wide enough that nothing which could hit is rejected, since the sphere model behind it decides. Its two consumers are both direct-fire hit tests |
| 26 | `+0x1c` | The twist sequence: a single full sweep of the twist node, which the twist angle selects a position within — [`torso-aim.md`](torso-aim.md#angle-to-pose). Negative for a chassis with none (RAZOR, SPIDER) |
| 28 | `+0x1e` | Twist rate at full stick: 1000 on the 18 bipeds, PITBULL and RAZOR, 1500 on SPIDER |
| 30 | `+0x20` | How fast the twist rate may build: 1000, except RAPTOR2's 250 and SPIDER's 300 |
| 32 | `+0x22` | Twist limit, applied symmetrically: 14000 (76.9°) everywhere but PITBULL's 32767, which is no limit. `Ai_AimAndFireAtMech` also reads it as the arc it will shoot in — [`ai-weapons.md`](ai-weapons.md#aiming-at-a-machine--ai_aimandfireatmech-0041e984) |
| 34 | `+0x24` | The pitch sequence, the same way. Negative for RAZOR and SPIDER |
| 36 | `+0x26` | Pitch rate at full stick: 800, except RAPTOR2's 700 and SPIDER's 1000 |
| 38 | `+0x28` | How fast the pitch rate may build: 800, except RAPTOR2's 175 and SPIDER's 200 |
| 40 | `+0x2a` | Pitch limit looking up: 3500 on OUTLAW, MAVERICK, STINGRAY, MONGOOSE and RAZOR, 6000 on the rest |
| 42 | `+0x2c` | Pitch limit looking down, negative: −2000 on those five and PITBULL, −4000 on the rest |
| 44 | `+0x2e` | Walk↔run threshold speed |
| 68 | `+0x46` | The sequence an immobilised machine goes down in — see [Going down](#going-down). The chassis' one non-cyclic sequence |
| 72 | `+0x4a` | Leg count: 2, except PITBULL's 4. Selects whether the front leg servos or all four are averaged in `Mech_ComponentDamageWrite` — [`component-damage.md`](component-damage.md#slots-the-write-path-reads-by-index) |
| 76 | `+0x4e` | Chassis mass, the Q10 weight each party's speed carries in a collision. 5000 light … 20000 PITBULL, **0 SPIDER** |
| 78 | `+0x50` | 1 = Razor. Selects the flight paths ([`razor-flight.md`](razor-flight.md)) and the `fm\<NAME>.FM` load ([`../formats/flight-model-fm.md`](../formats/flight-model-fm.md)) |
| 84 | `+0x56` | Whether a hit can knock this chassis' weapon mounts out — 1 on every biped, **0 on the PITBULL**. `Mech_ApplyDirectFireDamage` tests it before rolling; see [`weapon-mounts.md`](weapon-mounts.md#the-chance-path--the-destruction-roll) |
| 98 | `+0x64` | Fore/aft half of the pilot's eye, from the camera node, in that node's frame: 200 on ten chassis and 0 on eight, 300 on RAPTOR2, 800 on APOCA, 1200 on RAZOR. Half of `Mech_GetAimPoint`'s (`004155c4`) eye triple `(0, +0x64, +0x66)` — [`external-views.md`](external-views.md#the-camera-object--cam) |
| 100 | `+0x66` | The eye's lift above the node, the other half of that triple: 0 to 820 across the walkers, 2000 on PITBULL, 0 on RAZOR. It is also the height the sight line is measured from — [`ai-weapons.md`](ai-weapons.md) |
| 102 | `+0x68` | Fore/aft half of the outside view's orbit centre in the machine's own frame, `Mech_GetAimPoint`'s second triple: 0 on every retail chassis |
| 104 | `+0x6a` | The orbit centre's height: 1600 on most chassis, 1400 on MAVERICK, MONGOOSE, OUTLAW and STINGRAY, 800 on SPIDER, 2600 on PITBULL, 0 on RAZOR |
| 108 | `+0x6e` | Reverse-side walk↔run threshold |
| 110 | `+0x70` | Body radius, **750 on every HERC** — both radius vtable slots return it, see [`hit-detection.md`](hit-detection.md#the-three-radius-slots) |
| 122 | `+0x7c` | Turn-in-place sequence id |
| 190 | `+0xc0` | Shield array capacity before any Shield Pod: 3500 on every HERC, 0 on SPIDER — [`damage-system.md`](damage-system.md#the-shield-system) |
| 192 | `+0xc2` | HUD scale: the km/h the readout shows at top speed. A walker's is overwritten at load with `Q10(315 × rawSpeedForward)`; the RAZOR's 265 is used as stated — [`razor-flight.md`](razor-flight.md#hud-speed) |
| 194 | `+0xc4` | Stride calibration divisor |
| 196 | `+0xc6` | Stride calibration numerator |
| 204 | `+0xce` | Base name of the chassis' own debris file, 12 bytes NUL-padded — [`debris-dat.md`](../formats/debris-dat.md) |

### Load-time speed rescale

`MechType_InitOne` rescales four speed fields for non-flyers:

```
scale  = Q16Divide(rec196 × 400, rec194)
typeRec+0x04, +0x06, +0x2e, +0x6e  ×= scale     (Q16)
typeRec+0x02 (turn rate) is NOT rescaled
typeRec+0xc2 = Q10(315 × rawSpeedForward)       computed BEFORE the rescale
```

`scale` normalises the designer's speed points to the model's stride length: `simMax × stridePerTick` tracks `rawSpeedForward` across every Herc (see verification below). It is not friction.

The HUD reads `speed × typeRec[0xc2] / typeRec[0x06]` (`Mech_GetDisplaySpeedKph`, `0041bb3c`, walker branch; the flyer branch is in [`razor-flight.md`](razor-flight.md)), so `simMax` cancels and top speed always displays `315 × rawSpeedForward / 1024` regardless of scale.

## Control law (`Mech_LocomotionTick`)

Speed:

```
throttle += Q8(0x91, -stickAxis)                  // 0.566/tick, clamp ±0x400
desired   = Q10(throttle < 0 ? maxRev : maxFwd, throttle)
desired  += slopeTerm                             // dot(terrainNormal, forward) / 2400
desired   = clamp(desired, maxRev, maxFwd)
RateLimitedMoveToward(speed, desired, typeRec[0x08])
```

`DAT_0049a06e` is **not** a gear selector. `Input_SetThrottleLeverMode` (`00459d20`) sets it to 1 only when the input configuration reports a throttle control *and* the preferences page has that control assigned to THROTTLE rather than TURRET, and to 0 otherwise; the key command and the cockpit slider that "toggle" it only ever flip between +1 and −1, gated on that same pair. It selects the **joystick throttle-lever mode**: 0 = none, ±1 = lever present, sign inverting its sense.

It matters because it is what gates the throttle clamp. At 0 — keyboard and plain stick — the range is the full ±0x400, so holding the axis against its stop runs the setting from full forward through a one-tick pause at zero and on into full reverse. That one-tick pause is the sign-crossing guard, and it is the manual's "Centered is stopped". Non-zero also switches the handler's first block on, which reads the axis as an absolute lever position (`|axis − 0x100| × 2`, deadbanded below 100) instead of as a rate.

The throttle is two-way bound to the cockpit throttle gauge, arbitrated by the `mech+0x93` dirty flag — see [`cockpit-hud-widgets.md`](../formats/cockpit-hud-widgets.md).

Turn rate — a symmetric tent over speed, `T` the max turn rate (`typeRec+0x02`):

```
if (inStopAnim || speed == 0) turnBase = 0
else {
    s = clamp(|speed| bumped to min 45, 45, maxFwd);  H = (maxFwd - 45) / 2
    turnBase = (s <= 45+H) ? T·(s-45+H)/(2H)
                           : T - T·(s-45-H)/(2H)
}
turnTarget = Q8(Q10(1600, turnBase), stickAxis)    // stick clamped ±0x100
RateLimitedMoveToward(turnRate, turnTarget, typeRec[0x0a])
heading += turnRate
```

Half turn rate at crawl, peak at half top speed, half again at top speed. `Q16Divide(0x32, 0x32)` in that branch is a dead constant (always 1.0).

**Turning in place is not produced here** — at zero speed `turnBase` is 0. The turn-in-place branch only sets the animation rate to `Q10(350, stickAxis)`; the rotation comes from the turn-in-place sequence's root rotation.

The remainder of `Mech_LocomotionTick` (~60% of its body) is the gait state machine, switching between the walk / run / stop-forward / stop-reverse / turn-in-place / death sequences and maintaining `mech+0x2a0`. In steady state the playback rate is copied from the speed scalar, `animRate = speed`, so the two can be compared directly even though they are different quantities: the speed scalar (`+0x28e`) is on the chassis' own scale up to its top speed (`typeRec+0x06`, 276 on OUTLAW), and the playback rate (`+0x2a0`) is Q8, 256 playing the animation at full speed.

**The crawl.** The exception is a crawl: after the gait machine, a nonzero `|speed|` under `0x2d` (45) lifts a nonzero `|animRate|` under `0x3c` (60) to 60, keeping its sign. Every speed from 1 to 44 therefore walks at the same pace, and at 45 the rate drops back to the speed itself, slower than the 60 before it. The pace also rises in steps: at the 25 Hz cap a tick advances `31 × animRate >> 8` whole animation ticks ([Resulting speed](#resulting-speed)), so runs of neighbouring rates cover the same ground. On OUTLAW, whose walk covers 2.092 units per animation tick:

| Speed scalar | Playback rate | HUD readout, km/h | Actual pace, km/h |
|---|---|---|---|
| 1-44 | 60 | 0-15 | 7.9 |
| 45 | 45 | 16 | 5.6 |
| 50 | 50 | 17 | 6.8 |
| 58 | 58 | 20 | 7.9 |
| 67 | 67 | 24 | 9.0 |

The HUD readout follows the speed scalar and rises steadily through all of it.

## Center Body

The manual's other half of [Backspace]: instead of bringing the turret back to the legs, it walks the legs round under the turret. Scancode `0x2b` (`Sim_DispatchCommand`, `0045fdac`, and the identical case in `Sim_PollPlayerInput`) latches `g_CenterBodyMode` (`004d2af4`), clears `g_CenterTurretMode` and the ATT flag, and caches

```
g_CenterBodyTargetHeading = heading - Mech_GetTorsoTwistAngle()    // 004d2af8, short
```

— the world direction the turret is pointing in. While latched, the player's input block substitutes its own steering and twist axis; the throttle and the pitch axis still come from the pilot.

```
bodyError   = heading - target                       // legs still to turn
turretError = (heading - twistAngle) - target        // turret drifted off the direction
steer = sign(a) x (a² >> 8),  a = Q10(100, bodyError)
twist = sign(b) x (b² >> 8),  b = Q10(0x46, turretError)
if (a² >> 8 < 0x1e && b² >> 8 < 10)  g_CenterBodyMode = 0
if (Mech_GetSpeed() < 0)  steer = -steer
Mech_ApplyThrottleInput(mech, steer, throttleAxis)
Mech_TorsoTwistTick(mech, twist);  Mech_TorsoPitchTick(mech, pitchAxis, range)
```

All 16-bit arithmetic, so both errors wrap. Squaring the gained error makes the command soft near the target and hard away from it, which is what stops the legs hunting; the sign is put back afterwards. Both terms reach their thresholds together, since heading meeting the target forces the twist to zero. The extra inversion on `Mech_GetSpeed` (mech vtable `+0x38`, `00415498` — `Q10(2000, mech+0x28e)`) sits on top of the one `Mech_ApplyThrottleInput` already does from the stick, so reversing steers the right way.

The mode is not cancelled by steering or by the turret axes — only by its own convergence test or by [Backspace]. It leaves a few degrees of residual twist, by design: it is not a centring command.

## Timing

Tick rate, the `SimTickDelta`/`DAT_004d3be8` formula (`Time_BeginSimTick`, `004677bc`), and its Q8/125ms scale are documented in [`dbsim-physics-notes.md`](dbsim-physics-notes.md#fixed-point-math-toolkit) — not repeated here.

The locomotion accel steps (`typeRec+0x08`, `+0x0a`) are raw per-tick steps with no `Math_IntegrateRateOverTick`, so **the control law is frame-rate dependent**. The animation advance and the torso rates *are* dt-scaled.

## Root motion

`SimObject_ApplyRootMotion` (`0040250c`), called once per tick from `Mech_IntegrateMotion`:

```c
setRootTransform(shape, IDENTITY);   // ShapeInst_SeedRootTransform (00478a70)
advanceAnimation(shape, dt);         // ShapeInstance_StepAnimation (00478c2c), dt = Q8(SimTickDelta, 100)
delta = shape->nodeWorldTransforms[0];
pos   = objRotationMatrix × delta.translation + pos;   // Transform_ApplyToPoint (00480330)
euler += eulerOf(delta.rotation);                      // Transform_RotationToEuler (0047f894)
setRootTransform(shape, IDENTITY);
```

Per-frame ground movement is loaded on every frame advance by `AnimThread_LoadFrameGroundMovement` (`00478de8`):

```c
seq = animList->Sequences[seqId];
if (seq->groundMovementFlag == 0) thread.groundMoveFlag = 0;
else {
    G = animList->Transforms[ seq->transformIndices[frame * numParts] ];   // part index 0 = root
    thread.groundTrans = G.translation;   // thread+0x22/0x24/0x26
    thread.groundRot   = G.rotation;      // thread+0x28/0x2a/0x2c
    thread.groundMoveFlag = 1;            // thread+0x20
}
```

The sequence's ground-movement flag is the enable flag. A transition's transform index is a *different* field — a gait-change hook used only when switching sequences, not the steady gait.

Application is a matched set around the fraction `thread+0x1c / thread+0x1e` (intra-frame accumulator ÷ frame duration):

| Function | Effect |
|---|---|
| `00478fa8` | read: returns `scale(G, frac) ∘ stored` |
| `00479088` | write: `stored = scale(G, frac)⁻¹ ∘ incoming` |
| `00478e60` | frame exit: commits full `G` into `stored` |
| `00478ee8` | inverse of `00478e60`, for backward playback |

Seeding the root to identity then reading back yields `scale(G, frac_after) ∘ scale(G, frac_before)⁻¹` — the exact delta for that tick. Over one full frame the Herc advances by exactly `G`, ramped linearly. The node poses are blended by the same fraction — [Keyframe interpolation](../formats/dts-node-posing.md#keyframe-interpolation).

**Axes:** +Y is forward in model space (matches `Mech_ApplyTerrainSlopeToSpeed`, which builds the forward vector as `(0, speed, 0)`); root rotation Z is yaw.

### Resulting speed

```
animTicksPerSec = 3.125 × animRate
        because  dt        = Q8(SimTickDelta, 100) = 0.8 × elapsedMs
                 advance   = dt × animRate / 256           (AnimThread_Advance, 00479614)
                 per sec   = 1000 × 0.8 × animRate / 256

worldSpeed = 3.125 × speed × (ΣG_cycle / Σticks_cycle)     world units/sec
```

Frame-rate independent — `elapsedMs` cancels.

Because `G` varies frame to frame (OUTLAW walk: 150, 240, 170, 240, 80, 380 …), world speed **pulses with each footfall**, up to 4.75× between the slowest and fastest frame of a stride. Averaging `G` over the cycle loses that.

### Verification

Predicted run-gait top speed vs. the HUD reading, all Hercs, no fitted parameters:

| Herc | rawMax | scale | simMax | walk u/tick | run u/tick | pred km/h | HUD km/h | pred/HUD |
|---|---|---|---|---|---|---|---|---|
| OUTLAW | 325 | 0.851 | 276 | 2.092 | 5.080 | 94.6 | 100.0 | 0.947 |
| RAPTOR2 | 215 | 0.976 | 209 | 2.862 | 5.025 | 70.9 | 66.1 | 1.072 |
| TOMAHAWK | 240 | 1.180 | 283 | 2.025 | 4.140 | 79.1 | 73.8 | 1.071 |
| SAMSON | 190 | 1.078 | 204 | 2.200 | 4.460 | 61.4 | 58.4 | 1.051 |
| COLOSSUS | 180 | 0.911 | 164 | 2.325 | 5.140 | 56.9 | 55.4 | 1.028 |
| APOCA | 200 | 0.497 | 99 | 3.800 | 8.472 | 56.6 | 61.5 | 0.920 |
| OGRE | 190 | 0.874 | 166 | 2.775 | 5.375 | 60.2 | 58.4 | 1.030 |
| MAVERICK | 285 | 0.976 | 278 | 2.862 | 5.025 | 94.3 | 87.7 | 1.076 |
| SCARAB | 180 | 1.070 | 192 | 1.833 | 3.840 | 49.8 | 55.4 | 0.899 |

Full 18-Herc run: all within 0.899–1.076, mean ≈ 1.00. Four independent quantities must be correct for this to hold — root-motion model, the 3.125 tick constant, the load-time rescale, and the 166.667 units/m world scale. APOCA is the tightest constraint: stride 8.472 u/tick (largest) against scale 0.497 (smallest); without the rescale it is 2× wrong.

### Turn-in-place

Uniform across every Herc: 1820 units (10.00°) per frame, 7 frames, 100 ticks/frame = **70° per 700-tick cycle**, zero translation. At full stick `animRate = Q10(350, 256) = 87.5`, giving 27.3°/s (180° in 6.6 s). Negative stick plays the sequence backward.

## Walk/run gait discontinuity

Real and universal; confirmed against the retail build.

A run stride is ~2× a walk stride but takes 5/6 the time, and `animRate = speed` in both gaits. Crossing `typeRec+0x2e` therefore roughly doubles actual ground speed while the HUD number moves continuously. Per-Herc run/walk u/tick ratio: 1.76–2.43.

The HUD's 315/1024 constant is calibrated for the run gait only. Below the threshold — 50–60% of the throttle range — a Herc physically moves about half what the readout claims. See [`KNOWN_ISSUES.md`](../../../KNOWN_ISSUES.md).

## Damage effects on movement

Three terms, applied to the speed the machine is *asking* for rather than to the speed it has, so a damaged machine still accelerates at its own rate. They sit after the obstacle-avoidance step, which writes a speed of its own.

- **The flat penalties**, one pair of thresholds over two independent conditions:

  | | 39% (`Q10 × 400`) | 73% (`Q10 × 750`) |
  |---|---|---|
  | Legs | `mech+0xa9` — a side at `0x8d` damage or worse | `mech+0xa8` — a side past `0x50` |
  | Reactor | `mech+0xab` critical | `mech+0xaa` degraded |

The severe pair wins outright where both apply. Both leg flags are written by the leg grading in [`component-damage.md`](component-damage.md); the reactor pair by the reactor grading there — see [reactor-energy-pool.md](reactor-energy-pool.md#reactor-damage-flags), and for why this is the only thing it cuts.

- `mech+0x317` is the **Turbo Pod** (`TURB`, catalog id 31). While engaged it adds a term to desired speed (`00416b64`) *in the current direction of travel*: `Q10(Q10(scale, 1000), top)`, where `scale` is the pod's [damage curve](equipment-pods.md#the-damage-curve-both-bonuses-share) and `top` is the type's reverse speed at a current speed under 1 and its forward speed otherwise. Both gates are the original's: the pod must be engaged and the machine must already be moving (`speed != 0`), so the pod accelerates a walk rather than starting one. A pristine pod is worth about 98% of top speed, not a round 100%: the curve is taken against a literal 1000 rather than the 1024 that would double it. What engages the pod and what it costs the pool are in [`equipment-pods.md`](equipment-pods.md#what-the-two-ticks-do-with-the-button).

## Going down

`Mech_LocomotionTick`'s own branch for an **immobilised** machine, and the whole of how a HERC that has lost its legs ends up face down. It is an animation, not a physics result: there is no rigid body, no angular velocity and no ground-contact solve anywhere in the mech path, and the pitch you see is the last keyframe of a sequence.

Two things happen before the gait machine is even reached:

1. **The inputs are taken away.** Throttle and steer are zeroed, the unstick countdown does not run, the slope term and the clamp are skipped, and obstacle avoidance does not run. Everything below still runs, so the machine decelerates through the same rate limiter and walks its remaining momentum off over the next few ticks rather than stopping dead.
2. **The fall**, taken instead of the gait machine once the thread is settled — unless the machine is turning in place, which wins, so one immobilised mid-pirouette keeps turning.

| Thread state | What happens |
|---|---|
| Neither running nor targeting the death sequence (`typeRec+0x46`) | Aim playback at it with `AnimThread_SetTarget` (`00479570`) so the list's own transition is used; a machine in the reverse step-off is first snapped to the forward one, which is the only one with a transition to take. Sound `0x1e`. Rate 100 |
| Running it | Rate `0x78` |
| Running it, and `frame == nextFrame` | It has played out: latch `mech+0xb4` **collapsed**, take the landing damage, sound `0x29` |

The end-of-sequence test works only because the death sequence is the chassis' one **non-cyclic** sequence — see [`../formats/dts-node-posing.md`](../formats/dts-node-posing.md#cyclic-and-one-shot-sequences).

`mech+0xb4` is a third condition distinct from destroyed and immobilised, and the one that takes a machine off the AI's books completely: [`ai-targeting.md`](ai-targeting.md)'s targetability test and the mission group's condition test both reject a collapsed candidate, while one still falling is still a target.

The landing calls `Mech_SpreadImpactDamage` (`00417a04`) with `(150, 120)` — see [`component-damage.md`](component-damage.md#spread-impact-damage--mech_spreadimpactdamage-00417a04), which owns that primitive. A bad enough landing can therefore finish a machine off through the death gate.

`Mech_PlaceLegsOnGround` has a death-sequence arm of its own, but it leaves the sound id unset and so can never reach the footfall it guards.

## Cockpit eye and bob

No dedicated bob code, and none is needed. `typeRec+0x0c`, the camera node, is a shape **part** id. `Cockpit_TargetAnglesFromCameraBone` (`0041ef14`) resolves it through the shape's find-by-id, takes that part's `TSBasePart.Transform` as a transform id, and indexes the shape instance's per-node transform array at `shapeInst+0x16` (`0x20` bytes per entry) — the same array `SimObject_PushTransform` (`00402628`) memcpy's `count << 5` bytes of when saving state for a blocked step. The eye rides a node the walk cycle animates, so the bob falls out of correct root motion.

Resolution is uniform across the fleet. Every ground HERC lands on the same chain shape, and the parent links come from the `ANAnimList` relation pairs, the same table that places geometry ([`dts-node-posing.md`](../formats/dts-node-posing.md)):

| | camera part | transform | chain to root |
|---|---|---|---|
| 16 of 18 HERCs | 5 | 11 | 11 <- 4 <- 1 |
| MONGOOSE | 10 | 12 | 12 <- 11 <- 4 <- 1 |
| HEADHUNT | 5 | 12 | 12 <- 5 <- 1 |
| RAZOR | 12 | 1 | 1 (flyer, no animation) |

Node **1** is the one the walk, run, stop and turn sequences animate; 4 and 11 are the turret nodes sequences 0 and 5 drive (see [`torso-aim.md`](torso-aim.md)). The bob therefore comes entirely from node 1: with the turret held still, 4 and 11 contribute a fixed offset and no motion at all.

The chain also **rotates** only at 4 and 11. Measured over a full stride on OUTLAW, OGRE, MONGOOSE and HEADHUNT, the camera node's world orientation does not move — zero yaw, pitch and roll swing — so a walking machine's view bobs without tilting, and everything the pilot's frame is turned by comes from the turret. MONGOOSE's camera node carries a −570 (−3.1°) rest pitch of its own, so its view looks slightly down even with the turret centred.

On flat ground, standing eye height 3.2 m (STINGRAY) to 11.2 m (SAMSON), running 4.7 m to 11.8 m (OGRE), against the 6.1-10.4 m statures the manual's HERC specs quote. A stride swings the eye 0.24-0.42 m. Nothing here is fitted.

## Collision

`Mech_CollisionTest` (`00418f74`) answers "is the position I just integrated into refused", and is run after every move. Three things refuse it, in order:

1. **An object in the way.** The gap is asymmetric: **this** machine contributes its own vtable `+0x5c` and the **other** object its `+0x7c`, and an object whose `+0x7c` is zero never blocks — see [`hit-detection.md`](hit-detection.md#the-three-radius-slots) for which classes those are. Its distance is still taken, for [the structure record](#the-structure-a-machine-stands-in), before the zero sends the loop on. An object still waiting on its mission action is passed over before anything is read from it.
2. **A structure's collision volume.** `Structure_GatherWalkCandidates` (`00404ae4`) walks the structure list at `g_StructurePool` (`004a9624`) and hands `Structure_WalkCollisionTest` (`00427c68`) everything step 1 does not already cover: every static type, plus every animated type that has fallen to a wreck (the test in [`hit-detection.md`](hit-detection.md#base_directfirehittest--00405038)). It skips a structure that is *gone* — destroyed, no hulk, one component. `Structure_WalkCollisionTest` then tests the point against each one's `.DGS` height field (see [`hit-detection.md`](hit-detection.md#the-collision-volume--the-dgs-records-height-field)). Step 1 and step 2 are exact complements, so no structure is walked through and none is tested twice.
3. **Ground too steep**, `|normal.z| < 0x5aa` against normals scaled to the height grid's own one — about 45°. Off the grid (`Terrain_FaceNormalAt` returns null) counts as steep, which keeps a computer-piloted machine inside the zone. The player is refused only when climbing onto it: `Mech_MovementTick` saves the player's pre-move position at `004a9d5c`/`60`/`64`, and steep ground refuses while no slide is running and the new Z is above that saved one. Any other steep step starts or continues a **slide** instead and is accepted: the flag at `004a9d68` latches and Q10 10 of the normal's X and Y is added to a displacement at `004a9d54`/`58`, which `Mech_MovementTick` adds to the player's position every tick the flag is up. So the displacement is a slide velocity that grows each steep tick, and the landing is [below](#the-landing). The slide branch reads the normal through its pointer, which off the grid is null — see [Open](#open).

A block against another **machine** also hurts both of them, through the explosive-damage slot — see [`damage-system.md`](damage-system.md#a-collision--mech_collisiontest-00418f74). It additionally latches "something ran into me" on the struck object (vtable `+0x68`, `obj+0xb1`), which only the ram behaviour reads.

### The structure a machine stands in

Separately from the block test, `Mech_CollisionTest` clears `mech+0x2b0` on entry and, for each candidate whose target class (`obj+0x1a8`) is 1 (a structure) and whose body radius (vtable `+0x5c`) contains the machine's position, stores that structure there (`00418fb2`, `00419016`). The test sits in the object loop ahead of the gap test, so it sees a static structure with no collision radius as well, skips one still waiting on its mission action, and ends with the loop at the first object that blocks; the field holds the last match. It is a render-side hand-off, not an aim or lock-on aid. `Scene_SubmitFrameObjects` reads it every frame (`00428519`) and, when it is set, files the machine with `Scene_SubmitObjectAtCell` (`004283b4`) under the structure's cached draw cell (`structure+0x1e8`, stored by the structure walk earlier in the same pass) instead of with `Scene_SubmitObjectWithRadius` (`0042837c`) under the cell its own position and body radius pick. A machine inside a building's radius is therefore painted with the building's cell, in the same farthest-first sort as the building, and goes undrawn when the walk skips that cell ([`terrain-drawing.md`](../formats/terrain-drawing.md#objects-in-the-walk)).

### The landing

The first walkable step clears the slide flag. A slide still moving faster than `0xfa` (250) world units a tick, measured as `Math_FastMagnitude2D` over the displacement, hurts on arrival; either way the displacement is zeroed:

```
base   = Q10Multiply(slideDamageScale[difficulty], slideSpeed)   // 0049a058, entries in difficulty.md
spread = base * 3
for component in 7..12:                                          // upper legs, lower legs, feet
    vtable+0x74(component, RandomBelow(spread) + base, no attacker)
```

Every component is rolled separately over a window three times the base wide, so the six readings scatter rather than moving together. The write is the ordinary damage endpoint, so the landing cascades, can cripple or immobilise, and is stopped by the invulnerability setting like anything else ([`difficulty.md`](difficulty.md#the-two-sibling-cheats)). It carries no attacker, so nothing is credited if it kills.

It then calls `Cockpit_StartHitShake` (`00434010`), the same view shake and palette flash a hit on the cockpit raises ([`../formats/cockpit-canopy-palette.md`](../formats/cockpit-canopy-palette.md#the-damage-shake)), and `Sound_Play(0x29)`, the collision thump. This is the shake's ungated trigger: the direct-fire one tests who is flying and how far gone the cockpit is, and this one fires on any landing that got past the distance threshold.

## Open

- **Open:** whether the player can reach the slide branch off the grid. There `Terrain_FaceNormalAt` returns null, and unless the step climbs (which refuses it first), `Mech_CollisionTest` reads the normal's X at `00419451` through that null pointer.
- **Open:** the gait state machine, about 60% of `Mech_LocomotionTick`'s body, is named here but its transitions — which sequence each speed change, stop and turn input selects, and the playback rate each one sets — are not written up.
