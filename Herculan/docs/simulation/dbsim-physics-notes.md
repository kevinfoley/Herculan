# DBSIM.EXE fixed-point math and simulation timing

Reverse-engineered from `DBSIM.EXE` (Ghidra project `ES2Recon`); addresses are DBSIM virtual addresses. The fast-magnitude coefficients and the fixed-point shift amounts were checked against raw disassembly, not just decompiler output.

Scope is the shared math-library primitives and the simulation's timestep, which every other simulation doc builds on. What uses them lives with the subsystem: projectile flight in [`projectiles.md`](projectiles.md) and [`rockets.md`](rockets.md), hit geometry in [`hit-detection.md`](hit-detection.md), damage in [`damage-system.md`](damage-system.md) and [`component-damage.md`](component-damage.md), the terrain heightmap in [`../formats/terrain-heightmap.md`](../formats/terrain-heightmap.md). The pseudo-random generator is a math-library utility of the same kind but not a fixed-point primitive, and has its own page: [`random-generator.md`](random-generator.md).

## Fixed-point math toolkit

Shared helper functions used throughout the sim, not tied to any one subsystem.

**`DAT_004d3be8` — the global simulation timestep (`SimTickDelta`)**, read by `Math_IntegrateRateOverTick`, `Math_CountdownTimerTick` and `Timer_CountDown` below and computed once per tick by `Time_BeginSimTick` (`004677bc`):

```
spin until GetTickCount() >= last + 40             // 25 Hz frame cap
SimTickDelta = clamp((elapsedMs << 8) / 125, 0x40, 0x1c2)
```

Q8, where `1.0` (`0x100`) = 125 ms — helper "rates" below are per-125ms quantities, not per-second or per-tick-count, and one countdown unit is 125/256 ms ([Timer units](#timer-units)). Everything scaled by it is a "per this tick" quantity — DBSIM runs a discrete fixed/semi-fixed timestep sim, not a continuous-time integrator. At the vanilla 40 ms/25 Hz tick this evaluates to **81** (`40×256/125`, floored); a tick measured at 41 ms gives 83.

Not every per-tick quantity is scaled by this timestep: locomotion's accel-step fields (mech type record `+0x08`/`+0x0a`) are raw per-tick steps with no `Math_IntegrateRateOverTick` (`00467820`) integration, making the original's control law frame-rate dependent — see [`mech-locomotion.md`](mech-locomotion.md#timing) for the consequence.

**The multiply family.** Each is `(int64)a * b >> n` (an `IMUL` then `SHRD EAX,EDX,n`, taking the low word), and the scale is the fixed-point unit of the operands:

| Function | Address | Shift | Used for |
|---|---|---|---|
| `Math_Q8Multiply` | `0047df94` | 8 | rates against the timestep (`SimTickDelta` is Q8); stick and rate products |
| `Math_Q10Multiply` | `0047dfa4` | 10 | normalised scalars: throttle and speed, damage and shield fractions, capacitor charge |
| `Math_Q14Multiply` | `0047dfb4` | 14 | the sine/cosine table at `004a25dc`, whose entries are Q14; the second operand is a signed 16-bit value |
| `Math_Q16Multiply` | `0047df81` (twin at `0047df71`) | 16 | ratios built by `Math_Q16Divide` (`0047df5c`), such as the load-time speed rescale in `MechType_InitOne` |

**`Math_IntegrateRateOverTick(rate)` (`00467820`) — "integrate this rate over one tick."** `Q8mul(DAT_004d3be8, rate)`, clamped to signed 16-bit range (`[-0x7fff, 0x7fff]`). The core "apply a per-unit-time rate as this tick's delta" primitive, called on velocity- and acceleration-like fields to get a position or speed delta: a round's step and age ([`projectiles.md`](projectiles.md), [`rockets.md`](rockets.md)), the reactor's recharge ([`reactor-energy-pool.md`](reactor-energy-pool.md)), the torso's accelerations ([`torso-aim.md`](torso-aim.md)), a flyer's motion ([`razor-flight.md`](razor-flight.md)).

**`Math_CountdownTimerTick(timerRecord)` (`00467944`) — countdown timer tick.** The argument points at a 3-byte packed record, **not** at the counter: the counter is the `short` at `+1`. `rec[+1] -= DAT_004d3be8`, clamped to 0, returning the new value. **`Timer_CountDown` (`004679a4`)** is the same step over a 5-byte record whose counter is an `int` at `+1`, for windows a `short` cannot hold. Where the records sit in an object, and why a cited offset is the counter rather than the record, is in [`sim-object-layout.md`](sim-object-layout.md#countdowns-keep-their-counter-one-byte-past-the-record). Used for cooldowns and frame intervals (a rocket's exhaust animation, a mount's refire, a lock timer). The byte at `+0` is never read or written anywhere in DBSIM ([Open](#open)).

**The rest of the time module** (`00467724`–`00467a24`). `Time_InitSimClock` (`0046773c`) seeds `SimTickDelta` and a second Q8 delta, `004d3bea`, at `0x100`; `Time_BeginSimTick` computes the second with its own unit (`004d3bee`), and `Math_IntegrateRateOverTick2` (`00467858`) integrates against it. Beside the countdowns sit their count-up mirrors, `Math_CountupTimerTick` (`0046792c`, `short`) and `Timer_CountUp` (`0046798c`, `int`), plus `Math_RateFromTickDelta` (`00467890`, `(delta << 8) / SimTickDelta`, the inverse of the integrate), `Math_IntegrateRateWithRemainder` (`004678a8`), `Math_MulTickDelta64` (`00467918`), `Timer_SetShort` (`0046796c`) and `Time_AdvanceCoarseTick` (`00467734`, which moves `Time_GetCoarseTicks`' start back 16 ms). VSHELL links the same module (`00465a1c`–`00465d22`) with other constants: its unit is 1000/10 = 100 ms where DBSIM's is 125 ms, and its `Time_BeginSimTick` (`00465ab8`) waits 2 ms where DBSIM's waits 40.

**`Math_RateLimitedMoveToward(current*, target, step)` (`004679d8`) — rate-limited "move toward."** If `current < target`, adds `step` (clamped so it doesn't overshoot `target`); symmetric for `current > target`. Returns the remaining error (0 once `current == target`). A generic per-tick slew-rate limiter with `int` twin `Math_RateLimitedMoveTowardInt` (`00467a24`, a flyer's airspeed): rocket and plasma-round steering, locomotion's speed and turn ramps, structure turrets, the shield recharge and the external camera's rates all go through it. The function does no timestep scaling itself; whether the step it is handed was scaled is up to the caller.

**`Math_EulerToward(out, from, to)` (`00492884`) — the euler triple that aims at a point.** With `d = from - to`: `euler[2] = Math_Atan2Guarded(dx, dy) - 0x4000`, `euler[1] = 0`, `euler[0] = Math_Atan2Guarded(Math_FastMagnitude2D(dx, dy), dz)`. The quarter-turn subtraction is because the simulation's forward axis is model Y. `Math_Atan2Guarded` (`00492800`) takes **`(x, y)`** and sets the *x* to 1 when both are zero, so `euler[2]` is the ground bearing `atan2(dy, dx)` and `euler[0]` is `atan2(dz, groundDistance)` — an **elevation above the horizon**, not a polar angle from +Z. Reading the argument order backwards mirrors the bearing about the 45° line and turns a level target into a quarter turn of pitch. Call sites pass the destination first. `Math_HeadingToward` is the same helper and order for the ground bearing alone. The pitch's ground distance is the sqrt-free `Math_FastMagnitude2D`, so it carries an approximation error. Callers: the plasma round's and launcher rounds' steers ([`projectiles.md`](projectiles.md#the-plasma-branch), [`rockets.md`](rockets.md#guidance--rocket_homingsteer-0040a254)), the structure turret's aim ([`structure-behaviour.md`](structure-behaviour.md)), the AI's fire decision ([`ai-weapons.md`](ai-weapons.md)).

**`Math_FastMagnitude3D(dx, dy, dz)` (`0047dd66`) — fast (sqrt-free) 3D magnitude approximation.** Takes `|dx|,|dy|,|dz|`, sorts into `L ≥ M ≥ S`, returns:

```
L + M×0.34375 + S×0.25          (M×(1/4 + 1/16 + 1/32), S×(1/4))
```

An alpha-max-plus-beta-min-style approximation that avoids a real `sqrt`. It is exact along an axis and its error depends on direction: up to about **8% low** on the body diagonal (three equal components) and up to about **8.7% high** where the two smaller components are roughly a third and a quarter of the largest, averaging about 4% high over all directions. Ranges and radii measured with it inherit that bias, so a real `sqrt` is not a substitute. In the disassembly the sort is three `CMP`/`XCHG` pairs and the coefficients are `SAR`+`ADD` chains. A general math-library utility, used for the collision bounding-sphere radius ([`../formats/collision-spheres.md`](../formats/collision-spheres.md)), sound placement ([`../formats/audio.md`](../formats/audio.md)), the LOD size estimate ([`../formats/mech-shape-drawing.md`](../formats/mech-shape-drawing.md)), the flyer's engine-hum pitch ([`razor-flight.md`](razor-flight.md)) and the rocket's proximity beep. Not every range uses it: a fire's sound placement takes an exact integer square root ([`destruction-effects.md`](destruction-effects.md#where-the-shared-sound-is-heard)).

**`Math_FastMagnitude2D(dx, dy)` (`0047dd40`) — the 2D counterpart.** `max(|dx|,|dy|) + min(|dx|,|dy|)/2`, the octagonal estimate: exact on an axis and up to about 11.8% high on a diagonal. It measures ground-plane distances (the detection sweep's decay range, a locomotion slide, the scanner's range test) and is the distance both HUD range readouts display, so the original's own on-screen ranges carry the error.

## Timer units

Every countdown in the simulation is in `SimTickDelta` counts, and **a count is not a millisecond**. `Math_CountdownTimerTick` and `Timer_CountDown` subtract `SimTickDelta`, which is Q8 with 1.0 = 125 ms and 81 on hardware that keeps up with the 40 ms frame cap. So one count is 125/256 ms, about 0.49 ms, and a stated reload of 10000 expires in about 4.9 seconds. Reading a stated constant as milliseconds overstates the interval by a factor of about two.

A mission action timer's delay is the one stated in seconds: it is shifted left 11 on load, and 2048 counts are exactly one second ([`../formats/script-dat.md`](../formats/script-dat.md#block-6-in-memory--49-bytes-0x31)).

## Rejected readings

| Reading | Why it is wrong |
|---|---|
| The distance test in `Rocket_TickUpdate` (`0040a538`), `Math_FastMagnitude3D(round - camera) < 40000`, is a proximity fuze or a target-proximity check | It measures the round's distance to the camera (`ViewObjectPtr`) and plays a warning beep, for rounds from a machine that is not locally piloted. Nothing detonates on it; a round ends on its lifetime or on the raycast alone. The beep's conditions are [`rockets.md`](rockets.md#flight--rocket_tickupdate-0040a538)'s |
| A countdown's value is in milliseconds | The unit is one `SimTickDelta` count, 125/256 ms. A reload of 10000 lasts about 4.9 s |

## Open

- **Open:** the byte at `+0` of a `CountdownTimer` record. `Math_CountdownTimerTick` never touches it, and for the four global instances (`004a9be8`, `004a9bec`, `004a9ee6`, `004a9ee9`) nothing reads or writes it anywhere in DBSIM, only takes its address. That leaves its meaning open rather than establishing it as padding.
