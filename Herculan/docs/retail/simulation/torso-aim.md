# Torso aim — turret twist and pitch (DBSIM.EXE)

The manual calls it the **turret**: the part of a HERC carrying the pilot and the weapons, aimed independently of the legs. DBSIM's own field and symbol names say "torso"; they are the same thing.

Reverse-engineered from `DBSIM.EXE`. Movement of the machine itself is in [`mech-locomotion.md`](mech-locomotion.md); how a posed node reaches the screen is in [`dts-node-posing.md`](../formats/dts-node-posing.md).

**Core fact: the turret has no rotation of its own.** The type record names a sequence per axis, each one a single full sweep of one node, and the twist/pitch angle selects a *position* within that sequence. Nothing rotates the torso; an animation is seeked to match the angle.

## Call graph

| Address | Name | Role |
|---|---|---|
| `0041a550` | `Mech_TorsoTwistTick` | One tick of the twist axis |
| `0041a808` | `Mech_TorsoPitchTick` | The same on the pitch axis |
| `0041e8d4` | `Mech_CenterTorsoTick` | The [Backspace] centring command |
| `00479238` | `AnimThread_SeekToPosition` | Angle → frame + intra-frame offset |
| `0041a6d0` / `0041a994` | — | Servo-sound helpers, gated on `mech+0xa3` — [below](#the-servo-sound-helpers) |
| `0041a74c` | `Mech_ConvergeGunsOnRange` | Gun convergence, run at the tail of the pitch tick — [`weapon-firing.md`](weapon-firing.md#gun-convergence--mech_convergegunsonrange-0041a74c) |
| `0041ef14` | `Cockpit_TargetAnglesFromCameraBone` | Bring a world point into the eye's frame and drive both ticks at it — [below](#aiming-at-a-point) |
| `00415488` | `Mech_GetTorsoTwistAngle` | Twist-angle accessor, mech vtable `+0x3c` |

Each tick has four call sites, none of them more than once per tick: `Sim_PollPlayerInput` (`00460764`) for the pilot's axes, and again for the [Center Body](mech-locomotion.md#center-body) block; `Cockpit_TargetAnglesFromCameraBone`, which `Sim_PollPlayerInput` calls for the tracker and `Ai_FireAtPoint` calls for every AI shot; and `Mech_CenterTorsoTick`. `Mech_MovementTick` does **not** call them — the turret is driven from the input path, between the throttle and the move.

## Fields

The turret's state — the two animation threads at `mech+0x230` / `+0x234` and each axis' rate and angle at `+0x294`..`+0x29a` — is in the [instance-field table](mech-locomotion.md#mech-instance-fields), and its tuning, nine words of the type record at `typeRec+0x1c`..`+0x2c` (sequence id, rate at full stick, acceleration and limits, per axis), in the [type-record table](mech-locomotion.md#mech-type-record). Unlike the locomotion accel pair, both accel fields go through `Math_IntegrateRateOverTick`, so they are time-based.

## The tick

Twist and pitch are the same code, differing only in which fields and which limits they use:

```
target = Q8(axis, maxRate)                  // axis is ±0x100, the same stick units as steering
if (|rate| < |target|)  rateLimitedMoveToward(rate, target, integrateOverTick(accel))
else                    rate = target       // and the angle integrates the new rate over the whole tick
angle = clamp(angle + integrateOverTick((rateBefore + rate) / 2), limitMin, limitMax)
```

`|x|` saturates rather than wraps: `-0x8000` yields `0x7fff`.

**Only acceleration is rate-limited.** The moment the stick asks for less than the turret is already doing, the rate snaps to it — so releasing the stick stops the turret dead, and so does reversing it. The angle integrates the mean of the rate before and after, a trapezoid rule while ramping and a plain step while not.

### Snap-to-target

Both ticks take a target angle and an enable flag. When enabled, the turret stops dead on the tick its angle moves onto or across the target:

```
if ((angle - target >= 0 && before - target < 0) || (angle - target <= 0 && before - target > 0)) {
    angle = target;  rate = 0;
}
```

The pilot's own axes pass it **disabled**. Two callers enable it: the centring command, with target 0, and [the aim primitive](#aiming-at-a-point), with the angle the turret would hold if it were already on the point.

### Angle to pose

Each tick ends by seeking the axis's thread:

```
AnimThread_SeekToPosition(thread, sequenceId, (unsigned)angle >> 2)
```

`AnimThread_SeekToPosition` (`00479238`) sums the sequence's frame durations, scales the position by `Q14 x (total - 1)`, and walks the frames subtracting durations to land on a frame plus an intra-frame offset, which it installs with `AnimThread_SetSequence` (`004791a0`). The shift is on the **unsigned** angle, so a whole turn spans the sequence exactly once and a negative angle lands in its far end rather than off the front.

The threads themselves never play: `Mech_Constructor` gives every thread a rate of zero and only the locomotion tick ever raises one, so `AnimThread_Advance` returns immediately for these two.

The intra-frame offset lands on a whole animation tick, because the scale-down truncates. A twist sequence is 8 frames of 100 ticks, so a full turn has 799 drawable positions and **the turret steps about 0.45° at a time**; the pitch sequences are the same size over a much smaller travel, which leaves ~67 positions across OUTLAW's 30° of pitch. Below roughly 10°/s the steps are far enough apart in time to read as a stutter rather than as motion — at 0.55°/s, one step per second. OUTLAW's 76.9° of twist travel, centre to limit, draws 170 distinct poses. Only an analogue stick can hold a rate that low: a held key is worth `0x80` on the axis, which is 22°/s.

### Three threads per machine

`Mech_Constructor` (`00415bb0`) builds them in this order, skipping any whose sequence id is negative: locomotion on `typeRec+0x12` at `mech+0x22c`, twist on `+0x1c` at `+0x230`, pitch on `+0x24` at `+0x234`.

The shape re-ranks them by their sequences' priority on every step, and registration order only breaks ties; the three cover disjoint nodes on every retail chassis, so the ranking decides which thread carries root motion rather than any pose. Both are in [`dts-node-posing.md`](../formats/dts-node-posing.md#several-threads-on-one-shape).

### The angle is not the drawn direction

The two drift apart by up to ~7%, because the sequences' keyframes are not evenly spaced. OUTLAW's twist sequence (node 4, rotation about Z only) steps `0, −7280, −15470, −23660, −31850, −40238, −48428, −56618` — summing to exactly −65536, one full turn, but in uneven strides. At the 14000 limit the eye ends up 13004 round. The pitch keyframes are near-uniform, so pitch barely drifts: OUTLAW's eye pitches 3443 at its 3500 limit up and −2133 at −2000 down.

Nothing is inconsistent as a result: `Cockpit_TargetAnglesFromCameraBone` (`0041ef14`) reads the camera node's own composed transform, so the HUD, the aim and the view all agree with the drawn pose. The angle field is control state, not a direction.

## Automatic Turret Tracking — [T]

ATT flies the turret at the selected target on its own. Its latch is the weapon manager's `manager+0x14`, which the console's TRACK button and the [T] command both toggle; it is read by the input path's turret block and by the per-frame cockpit update whose timeout closes this section.

The block's three cases, in its own order of tests:

1. **Either turret axis non-zero** — the pilot has the turret. Tracking is skipped for the tick and the centring latch is cleared.
2. **ATT latched, a target selected, and that target's `+0x99` clear** — `Player_ResolveTargetAimPoint` (`0041b728`) takes the target's aim point and hands it to `Cockpit_TargetAnglesFromCameraBone`, which runs both axis ticks itself, and the centring latch is cleared. Note the liveness test is `+0x99` **alone**: unlike every AI test, a crippled (`+0xa4`) target is still tracked.
3. **Otherwise** the centring mode or the plain axis ticks, as before.

The block is the walker's; a RAZOR takes its flight input instead. Center Body replaces it while it holds the legs. It also runs while the external-view camera has the controls, with the twist axis zero and the pitch axis whatever a throttle lever bound to the turret pair reads ([`../formats/joystick-input.md`](../formats/joystick-input.md#while-the-camera-has-the-controls)), so a non-zero reading there takes the turret from the tracker exactly as case 1 says.

**Both centring commands turn ATT off.** `Sim_DispatchCommand`'s scancode `0x0e` ([Backspace]) and `0x2b` (`\`) each write `manager+0x14 = 0` alongside their own latch, so a pilot who asks for the turret back keeps it.

**[T] turning ATT off also centres the turret.** Scancode `0x14` toggles the TRACK widget (`ConsoleButtons_ToggleAutoTrack` (`00441f7c`), which is also the console button's whole click action) and then, *only if that turned it off*, runs the same three writes the [Backspace] case does. Clicking TRACK off with the mouse therefore leaves the turret where the tracker had it; pressing [T] brings it home. The asymmetry is the dispatch case's, not the button's.

Toggling it either way announces the new state on the computer's channel — `0x26` `AUTO TRACKING ENGAGED` and `0x27` `AUTO TRACKING DISABLED`, both withdrawn before the new one is posted, the same shape as the radar toggle's pair ([`../formats/cockpit-messages.md`](../formats/cockpit-messages.md#posters)).

**ATT with nothing selected gives up after a delay.** `Player_PerFrameCockpitUpdate` (`0041b130`) arms `mech+0x31c` with `0x1194` on the selection change that leaves the latch holding nothing, counts it down every frame the pair still holds, and latches the centring mode when it reaches zero. It does not clear the latch, so selecting again puts the turret straight back on a target.

## Centring — [Backspace]

`Mech_CenterTorsoTick` (`0041e8d4`) drives both axes from the angles themselves and enables the snap, so the turret runs home fast, eases off as it arrives, and stops exactly on centre:

```
twistAxis = -clamp(Q10(0xfa, twistAngle), ±0x100)   // pitch likewise
```

Its second argument is the gun convergence range, passed straight to the pitch tick, so the guns keep toeing in on the selected target while the turret comes home. The input path is the only caller that gives it one; every AI caller passes zero.

It is a **mode**, not a keypress: scancode `0x0e` latches `DAT_004d2588`, and the input path clears it again the moment either turret axis is non-zero. Nothing clears it on arrival — with the turret centred the axes are zero and nothing moves, so it simply idles until the pilot takes the turret back.

Scancode `0x2b` (`\`, "Center Body") sets the opposite flag `g_CenterBodyMode` (`004d2af4`), which turns the legs under the turret rather than the turret back to the legs. It substitutes the steering and the twist axis both, and is documented with the rest of the steering in [`mech-locomotion.md`](mech-locomotion.md#center-body).

## Aiming at a point

`Cockpit_TargetAnglesFromCameraBone` (`0041ef14`) is **the "point the turret at that" primitive**. The tracker calls it with the selected target's aim point and `Ai_FireAtPoint` with every AI shot, whether or not anything fires ([`ai-weapons.md`](ai-weapons.md#the-fire-decision--ai_fireatpoint-0041f5a0)). It works in the pilot's frame: the camera node's world transform composed with the machine's. **That frame's orientation is what "the direction the pilot is looking" means in DBSIM** — the camera node hangs below both turret nodes (see [`mech-locomotion.md`](mech-locomotion.md#cockpit-eye-and-bob)'s chain table), so twist and pitch turn the view with nothing having to add them to it, and the walk cycle does not.

```
p        = point in the eye's frame, less typeRec+0x66 off its Z      // the eye's lift
(e, y)   = Math_EulerToward(p)                                        // residual pitch and yaw
a        = Q10(0xfa, residual)                                        // per axis
if (p.y > 50000)                                                      // beyond ~300 m
    if (|yaw|   < 1000) a_yaw   = |yaw|   < 0x32 ? 0 : Q10(700, a_yaw)
    if (|pitch| < 1000) a_pitch = |pitch| < 0x55 ? 0 : Q10(700, a_pitch)
axis     = Q8(|c|, c),  c = clamp(a, ±0x100)                          // yaw negated first
Mech_TorsoTwistTick(mech, twistAxis, angle - yaw,   snap on)
Mech_TorsoPitchTick(mech, pitchAxis, |p|, angle + pitch, snap on)     // |p| is the guns' convergence range
return (e, y)
```

The demand is a quarter of the residual, so a residual of about 5.8° is full stick. Squaring it after the clamp is what lets the turret run hard while it is far off and ease as it arrives; the snap target, the angle the axis would hold if it were already on the point, is what stops it exactly there. Beyond 50000 units a residual under 1000 (5.5°) is damped and one under `0x32` yaw or `0x55` pitch is discarded, so the turret does not hunt on a distant target. The residual it returns is the static pair at `004a9d80` — pitch, then yaw — which `Ai_FireAtPoint` tests against 1000 before it lets a gun fire.

## The servo-sound helpers

Each tick ends, for the locally piloted machine only (`mech+0xa3`), with a helper: `0041a6d0` after the twist and `0041a994` after the pitch. They look like a servo loop and are not one. When the axis exceeds `0xc0` in magnitude and the angle has moved since the last call, the helper tests whether sound `0x21` is playing and, if not, sets a byte; it clears the byte when the axis returns to zero or the angle stops. **It never starts or stops a sound.** The byte and the last-angle word are function-local statics that nothing else references (`es2_xref.py` on `0049a1c9` and `0049a1cc` finds only each helper's own instructions), so no servo sound plays in retail. Sound `0x21` is `explo4.wav`, the rumble under the drop-in lift ([`mission-deployment.md`](mission-deployment.md#the-ride--liftstart_rise-0045d840)).
