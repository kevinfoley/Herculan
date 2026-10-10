# Plan — smooth rendering above the 25 Hz tick

Two stages to make motion smooth at any display refresh rate without changing what the simulation does: **interpolation** (stage A), then **prediction** for the machines where latency is felt (stage B). Both are opt-in modernizations under the [vanilla-by-default](planning.md#vanilla-by-default) rule.

This is a plan, not a record of something built. Nothing here is implemented.

**Build it after porting is complete.** Stage B's predictors mirror sim code (locomotion, flight, rocket homing) that is still being reverse-engineered, so building them earlier means maintaining a second copy of code that is still changing. And while RE is ongoing, rendering at 25 Hz is useful because it matches retail frame for frame when comparing against retail footage or stepping ticks with the developer keys.

## Where rendering stands

- The simulator runs a fixed-step accumulator (`Cockpit/SimulationStepper.cs`, called from `SimulatorFrame.Update`): `SimWorld.Tick` at `SimWorld.TicksPerSecond` = 25, with `SimMath.TickDelta` pinned to 81. Retail ticks and renders in the same 40 ms loop (`Time_BeginSimTick`, `004677bc`); see [`../retail/rendering/dts-node-posing.md`](../retail/rendering/dts-node-posing.md#evaluation-cadence--per-tick-not-per-rendered-frame).
- After the tick loop, render state is copied straight off the sim, so a frame shows the latest whole tick. Every path goes through a small number of places, and this plan depends on keeping it that way:
  - `WorldDrawItems`' movers → `MissionScene.TransformOf` → `SimObject.WorldFrame`
  - `WorldDrawItems`' posed parts → `MissionScene.PosedTransformOf` → `WorldFrame` × `NodeTransform`
  - the pools: `WorldDrawItems.RefreshWreckItems`, and `TransientDrawItems`' `RefreshProjectileItems`, `RefreshDebrisItems`, `RefreshDropPodItems`, `RefreshWeaponItems`, `RefreshSpriteBatches`
  - the cockpit eye (`pilotMech.EyeTransform`), the view chain's `ViewCamera` (placed once a tick), `ExternalCamera.Place`, and the fly camera
- Player input is written to `pilotMech.Controls` every frame in `window.Update` and read by the sim once per tick.
- Some presentation already runs per frame and is not affected: cockpit pan, the view kick and the hit shake (`Render/`), and the gauges driven by coarse ticks.

## Constraints

- **The simulation is not touched.** Per-tick state, RNG draws, tape replay and the [differential harness](plan-differential-harness.md) must be identical with smoothing on or off.
- **Off means retail exactly:** a frame shows the latest whole tick, as it does now.
- **Discrete state stays discrete.** Flipbook effects (`ImpactEffect.Frame`), shape cell swaps (`CellFrames`), the HUD, radar and gauges keep stepping at 25 Hz as in retail. Only continuous transforms are smoothed.

## Rejected approaches

| Approach | Why not |
|---|---|
| Raise the sim tick rate | Changes behaviour. Retail never ran a `SimTickDelta` below 64 (`0x40` clamp floor, 81 in practice). Rolls not gated by a timer scale with tick count: a missile tower whose launch roll fails does not reset `_refireTimer` (`BaseObject.Armed.cs`) and rolls again next tick. Fixed-point truncation grows as the delta shrinks: `IntegrateRateOverTick`'s `>> 8`, `AnimationThread.Advance`'s `step == 0` early-out, and `ScalePerTickStep`'s integer divide. Timers that reload instead of carrying their remainder (`ImpactEffect`) change period. Only deltas of the form 2048/d Hz are exact. |
| Make movement authoritative every frame and keep logic on ticks | Movement cannot be separated from logic. A Herc moves by root motion, which is tied to the gait state machine, `Mech_CollisionTest` with its push/pop restore, and footfalls. A projectile's movement is its hit test, and a hit draws RNG. Results would depend on frame rate, which is the retail defect this engine removed, and tape replay would break. Splitting integer steps also does not add up: the sum of the truncated parts differs from the truncated whole. |

## Stage A — interpolation

The sim stays as it is. At the end of each tick the host saves a render snapshot. Each frame it draws the blend between the last two snapshots at `alpha = tickAccumulator / SecondsPerTick`.

1. **Snapshots per render item.** After each tick, compute each item's model matrix from the paths above, move the old "current" to "previous", and store the new one. Snapshot the computed render matrix rather than sim state, so node poses (`NodeTransform`) come along for free and the sim needs no new API.
2. **Blend.** Decompose each matrix into translation and rotation, lerp the translation, slerp the rotation, and recompose. The transforms are rigid; check this once at snapshot time and snap any item that is not.
3. **Stable identity for pools.** The `Refresh*Items` functions rebuild items from sim pools. Key each item by its pool object (an object reference, or an id added to the pool type) so a projectile or piece of debris keeps its previous snapshot across ticks. An item with no previous snapshot is drawn at its current one.
4. **Snap instead of blend** when:
   - an object spawns or is deployed (`AwaitingDeployment` clears);
   - an object is removed;
   - the fly camera or developer keys teleport it;
   - on the first frame after a modal panel closes, a single step (`developerKeys.StepPending`), the tick-loop cap (`MaxAccumulatedSeconds`), or a tape seek;
   - the view switches between cockpit, external and fly camera.
5. **Cameras.** Snapshot the eye transform and the external camera's placement the same way. Leave cockpit pan, view kick and hit shake as they are; they already apply per frame on top of the camera.
6. **Beams and sprites.** Beam chains are rebuilt each tick (`SimWorld.Beams`, `BeamTracer`) and are short-lived. Interpolate their endpoints when the same tracer exists in both snapshots, and snap otherwise.
7. **Setting.** One preference (Tweaks menu), off by default, following the pattern set by `TweakSettingDefinitions.SmootherTurretMovement`.

**Cost:** up to one tick (40 ms) of added visual latency, averaging half a tick. Other objects' motion does not feel this delay. The player's own view does, which is what stage B addresses.

## Stage B — prediction

Stage B replaces "blend previous → current" with "blend current → predicted next" for the objects where latency matters. The predicted state is computed and discarded; it is never written back to the sim.

1. **Predict once per tick, with full integer ticks.** After each real tick, compute each predicted object's state one tick ahead with the sim's own arithmetic at the normal `TickDelta`. This keeps the truncation problems from the rejected approaches out: nothing is ever evaluated at a fractional delta. Each frame, draw the blend from current to predicted at `alpha`.
2. **Re-predict the player's machine when input changes.** `pilotMech.Controls` is written every frame. When it differs from the value the last prediction used, re-run that machine's prediction, so a turn or twist shows in the same frame. Other objects are predicted once per tick.
3. **Predictors, in order of value:**

   | Object | Prediction | Building blocks |
   |---|---|---|
   | Player's Herc and cockpit eye | Throttle and turn control law, root motion, torso twist and pitch | `AnimationThread.Capture`/`Restore` and `MechObject`'s `Snapshot`, which already save and replay a step for a blocked move |
   | AI Hercs | The same step with the controls last written by their think | as above |
   | Flyers, RAZOR | Flight model step | `FlyerObject.Flight`, `FlightPhysics` |
   | Ground vehicles | Their movement step | `BaseObject.GroundVehicle` |
   | Rockets | Homing step | `Rocket` |
   | Projectiles, debris, drop pods | Straight-line or ballistic step | trivial |

   An object with no predictor falls back to stage A.
4. **No side effects.** A predictor must not draw RNG, play sounds, add to or remove from pools, raise mission events, write contacts, or leave `SimMath.TickDelta` (a global static) changed. The simplest structure is capture → step the motion code alone → read the transforms → restore, reusing the blocked-move save/restore.
5. **Collision.** Run `Mech_CollisionTest` in the prediction if it has no side effects; otherwise leave it out and accept that the next tick will correct a step that collision refuses.
6. **Correction.** A prediction goes wrong on a collision refusal, a gait or sequence change, death, knockback, an AI changing its controls, or a projectile hit. When the next real tick lands, carry the difference between the predicted and the real state as an offset and decay it over a few frames instead of snapping. The decay is chosen per class (see [Open](#open)).
7. **Projectile impacts.** A predicted round can pass through its target for part of a frame before the tick that registers the hit. Clamp the predicted segment at the first surface a raycast finds, or hide the round once the prediction crosses its target's bounds.

## Verification

- **Sim-state equality.** Run the same mission and input with smoothing off, with A, and with B, and assert that the sim state is identical every tick. This is the per-tick hash from [`plan-differential-harness.md`](plan-differential-harness.md#shape) run engine against engine, and it is what catches a predictor with a side effect.
- **Tape replay.** Every tape in the test set replays identically with smoothing on.
- **Visual.** A capture at 144 Hz of a Herc walking, running, turning in place, and a projectile in flight shows no stepping and no visible correction pops in steady motion. Rendering with smoothing off matches the current build frame for frame.

## Docs to change when this lands

- The `WorldDrawItems.Refresh` comment on the pose cadence, which says the original's per-tick pose evaluation is kept: point it at the setting.
- `potential-modernization-features.md`: the higher-tickrate entry is superseded by this plan.

## Open

- **Open:** whether `Mech_CollisionTest` and the ground-contact code can be called in a prediction without writing state. Audit before building stage B.
- **Open:** whether every matrix the snapshot paths produce is rigid (step 2 of stage A). A shape with scaled nodes would not be.
- **Open:** the correction decay per class, which has to be tuned by eye.
- **Open:** the default for the setting. Off until porting and retail comparison are finished; revisit then.
