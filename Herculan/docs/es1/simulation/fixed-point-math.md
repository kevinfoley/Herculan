# DBSIM.EXE — fixed-point math

ES1's simulator against ES2's [fixed-point math toolkit](../../retail/simulation/dbsim-physics-notes.md#fixed-point-math-toolkit). Addresses are in `ES1DBSIM`, the program image of ES1's `DBSIM.EXE` ([the X-32 executable](../formats/x32-executable.md#the-image)); ES2 names and addresses are DBSIM's. The hand-written assembly library and its tables are ES2's; the C primitives beneath the simulation round where ES2's truncate.

## The assembly library

**The hand-written assembly library is identical.** With image-address operands masked, each of these ES2 functions occurs whole in `ES1DBSIM`:

| | |
|---|---|
| byte for byte | `Math_FastMagnitude2D`, `Math_FastMagnitude3D`, `Math_ChebyshevMagnitude2D`, `Math_ChebyshevMagnitude3D`, `Math_ISqrt`, `Math_MapRange`, `DotProductVec3Short`, `DotProductVec3Int`, `Vec3I_DotSign`, `Vec3I_Cross`, `Math_TransposeRotation2D`, `Transform_TransposeRotation`, `Math_Rotate2DPoint`, `Transform2D_ApplyToPoint`, `Rotation_Concat`, `Transform_ConcatRotation` (`000647f4`), `Transform_Concat`, `RotateVectorByMatrixQ14`, `Transform_ApplyToShortPoint` |
| but for image addresses | `Math_CosBam` (`0006477c`), `Math_AsinBam` (`0005f54c`), `Math_BuildRotation2D`, `BuildEulerRotationMatrixQ14`, `Transform_RotationToEuler` (`0005e988`) |

`GO.EXE`'s `BuildEulerRotationMatrixQ14` (`ES1GO 000589c4`) and `Math_AsinBam` (`ES1GO 0006a378`) are not: they truncate each trigonometry-table index (`SAR 4` in the 15 of the first, `SAR 2` then `SAR 3` in the second) where `ES1DBSIM`'s (`0005f0ac`, `0005f54c`) and ES2's round them (each shift followed by `ADC 0`). `GO.EXE`'s `Transform_RotationToEuler` (`ES1GO 00056710`) is `ES1DBSIM`'s, calling the truncating `Math_AsinBam`.

The tables the trigonometry reads are identical: the cosine quarter-turn table (`000981cc`, ES2 `004a25dc`, 0x401 words), the arctangent table (`0009e648`, ES2 `004a1c4c`, 513 bytes) and the used ranges of both arcsine tables (`00097634` and `000979b4`, ES2 `004a1e54` and `004a21d4`, 0x201 words each). The arctangent, `Math_Atan2Bam` (`000734a0`), is ES2's without its `PUSH EBX`/`POP EBX`: it overwrites `EBX`, which both compilers' conventions require a callee to keep. `Math_FastMagnitude3D` is `0006325a`.

## The primitives

**The fixed-point primitives round to nearest, where ES2's truncate.**

| | `ES1DBSIM` | ES2 |
|---|---|---|
| Q8, Q10 multiply | `Math_Q8Multiply` (`0005e7f8`), `Math_Q10Multiply` (`0005e818`), (*a*, *b*) in the C convention, its arguments removed by the caller: 32-bit *a* × 16-bit *b* (`MOVSX` of the second), rounded | `Math_Q8Multiply`, `Math_Q10Multiply`: 32-bit × 32-bit, truncated |
| Q14 multiply | `Math_Q14Multiply` (`0005e838`): rounded | `Math_Q14Multiply`: 32-bit × 16-bit, truncated |
| Q16 multiply | `Math_Q16Multiply` (`00073574`) and its copy `Math_Q16Multiply2` (`0007358c`): `0x8000` added before the shift | `Math_Q16Multiply` and its copy `Math_Q16Multiply2`: truncated |
| Q16 divide | `Math_Q16Divide` (`00073534`): divides the magnitudes, adds 1 when the remainder is at least half the divisor rounded up, then restores the sign | `Math_Q16Divide`: signed `IDIV`, truncated toward zero |

The Q8, Q10 and Q14 multiplies add half a unit (`0x80`, `0x200`, `0x2000`) when the product fits in 32 bits; when it does not, `SBB ECX,ECX` / `XOR ECX,half` adds `0xffffff7f`, `0xfffffdff` or `0xffffdfff` instead. The add is `ADD EAX,ECX` / `ADC EDX,0`, so the value goes into the 64-bit product unsigned: an overflowing product gains almost 2³² before the shift, where a rounding of it would add half a unit. The rounding is general in ES1: of `ES1DBSIM`'s 27 `SHRD EAX,EDX` instructions, 25 follow a rounding add and the other 2 (`0007e0ae`, `0007e41c`) normalise a mantissa. Of DBSIM's 26, the 20 in the matrix routines above follow a rounding add and the 6 in the primitives do not.

## The tick length — `Time_BeginSimTick` (`00063f40`)

**ES1's simulation tick is counted in tenths of a second, where ES2's is in eighths ([timer units](../../retail/simulation/dbsim-physics-notes.md#timer-units)): `SimTickDelta` (`000bdbd0`) is Q8 with 1.0 = 12 interrupts of the PC timer, which the simulator runs at 120 a second ([the rate](timer.md#the-rate)), so 1.0 is 100 ms. One countdown count is then 100/256 ms, about 0.39 ms, and a reload of 10000 lasts 3.9 seconds.** It is measured with the PC timer rather than ES2's `GetTickCount`:

- **`Time_InitSimClock` (`00063e64`)**, which `Sim_StartMission` calls ([the mission start](main-loop.md#the-mission-start--sim_startmission-0005a9dc)), sets `SimTickDelta` and `SimTickDelta2` (`000bdbd2`) to `0x100`, and on its first call registers `Time_SimClockTimerTick` (`00063e3c`) with `Timer_AddCallback` at count 1, keeps the timer structure `000bdd0c` points to in `000981c0`, and sets `SimClock_TicksPerSecond` (`000bba44`) and `000bba40` to `1000000 / Math_Q14Multiply(w, 0x35a1)`, *w* that structure's word `+4`, the timer's divisor, which makes it 120 ([the rate](timer.md#the-rate)), `SimClock_TicksPer100ms` (`000bba42`), the two divisors `000bdbd4` and `000bdbd6` and the last frame's elapsed count `000bdbd8` to a tenth of that, 12, and the countdown `SimClock_Countdown` (`000bba46`) to `SimClock_TicksPerSecond`.
- **`Time_SimClockTimerTick` (`00063e3c`)**, the timer callback, which runs on every interrupt ([callbacks](timer.md#callbacks)), takes 1 from `SimClock_Countdown` while it is not 0, adds 1 to the dword `000981c8`, and adds 1 to the clock `000981c4` on every second call, 60 times a second.
- **`Time_BeginSimTick` (`00063f40`)**, once a frame from `Sim_Main` unless a tape plays ([the frame](main-loop.md#the-frame)), waits until `SimClock_Countdown` (read through `Time_GetSimClockCountdown`, `00063e34`) is at least 2 below `SimClock_TicksPerSecond`, then takes the elapsed ticks *e* = `SimClock_TicksPerSecond` − `SimClock_Countdown`, keeps it in `000bdbd8`, sets `SimTickDelta` = `Time_ElapsedToQ8Clamped` (`00063f04`)(*e*, `000bdbd4`) and `SimTickDelta2` the same with `000bdbd6`, and reloads the countdown.
- **`Time_ElapsedToQ8Clamped` (`00063f04`)**, (elapsed, unit): (elapsed << 8) / unit, clamped to `0x40`–`0x1c2`, ES2's (`00467790`).

So a frame waits for two interrupts, 8.3 to 16.7 ms after the last frame's reload, and at most 60 frames run in a second. With *e* interrupts elapsed the tick is (*e* × 256) / 12 held to `0x40`–`0x1c2`:

| *e* | Real time | `SimTickDelta` | Tick |
|---|---|---|---|
| 2 | 16.7 ms | `0x40` (42 held up) | 25 ms |
| 3 to 21 | 25 to 175 ms | 64 to 448 | the real time, rounded down to a 256th of 100 ms |
| 22 and more | 183 ms and more | `0x1c2` (held down) | 175.8 ms |

A frame drawn within two interrupts therefore advances the simulation 25 ms in 16.7 ms: at 60 frames a second the game runs half again as fast as real time ([Open](#open)). The clamp's range is 25 to 176 ms (ES2's: 31 to 220 ms), and the countdown stops at 0 after a second without a frame. The two divisors start equal, so the two deltas are equal while they stay so ([Open](#open)). A replay takes each frame's delta from the tape instead ([the tick](main-loop.md#the-tick--sim_maintick-00059700)).

## Rate over tick

The rate-over-tick helpers differ as well. ES1's `Math_IntegrateRateOverTick` (`00063fa4`), which the bullet tick calls where ES2's `Bullet_TickUpdate` calls ES2's, reads its rate argument as an `int16` (`MOVSX` at `00063fae`), as ES2's does (`0046782c`): a caller that pushes a dword, as the missile's tick does with its type record's `+4`, has only the low word read. It multiplies the rate by `SimTickDelta` through the rounding Q8 multiply, then sign-extends the product from 16 bits before its ±0x7fff clamp, so an out-of-range product wraps where ES2's saturates. `Math_IntegrateRateOverTick2` (`00063fe0`) does the same with `SimTickDelta2`, where ES2's reads `004d3bea`.

## Helpers that match

These C helpers do the same as ES2's of the same name, read side by side:

- `Math_Atan2Guarded` (`00063ca0`) and `Math_HeadingToward` (`00063cc8`), which take their arguments in ES2's order;
- `Math_EulerToward` (`00063d2c`), (from, to, out) in stack order, the reverse of ES2's (out, to, from): with *d* = *to* − *from*, out[2] = `Math_Atan2Guarded`(*d*.x, *d*.y) − `0x4000`, out[1] = 0 and out[0] = `Math_Atan2Guarded`(`Math_FastMagnitude2D`(*d*.x, *d*.y), *d*.z), the angles that turn model Y toward *to*; ES2's reaches the magnitude through a thunk;
- `Math_CountdownTimerTick` (`0006410c`), whose tick delta is `SimTickDelta`;
- `Math_DistanceBetweenPoints` (`00063c20`), which subtracts in line where ES2's calls `Vec3_Subtract`;
- `Transform_ApplyToPoint` (`0005f58c`) and `Transform_RotatePoint` (`0005ef74`), the `int32` point through a 32-byte transform with and without its translation, identical to ES2's after normalisation;
- `Math_RateLimitedMoveToward` (`0005e598`) and its `int32` form `Math_RateLimitedMoveTowardInt` (`0005e608`), which take their arguments in the reverse of ES2's order ([calling conventions](../formats/compiler.md#calling-conventions));
- the generator ([ES2's](../../retail/simulation/random-generator.md)): `Math_RandomNext` (`00063af4`) and `Math_RandomBelow` (`00063b3c`) over the same 56-short state with its cursors at `+0x70` and `+0x71`, and `Math_RandomSeed` (`00063aa4`), whose 112-byte seed table (`00098150`) is ES2's at `004a6958`. ES1's seeding calls nothing after the copy, where ES2's calls an empty function.

## Rescaling a vector

**`Math_NormalizeVec3ToLength` (`00063b5c`)**, (length, vector) in stack order, is ES2's (DBSIM `004926e4`, (vector, length)) on ES1's primitives: it rescales an `int32` vector in place to the `int16` length and returns the vector. With *m* = `Math_FastMagnitude3D` of the vector, a vector of magnitude 0 is zeroed (`memset` of 12 bytes); otherwise:

```
s = (int16)((length << 16) / m)          // IDIV, truncated toward zero
v[i] = (int16)Math_Q16Multiply2(s, v[i]) // i = 0, 1, 2
```

The scale wraps once *m* is no more than twice the length, as ES2's does. Each product goes through the rounding `Math_Q16Multiply2` ([the primitives](#the-primitives)) and is truncated to 16 bits and sign-extended, where ES2's divides through `Math_DivInt` and keeps each product of the truncating `Math_Q16Multiply` whole. `es2_xref.py` finds two calls, from `GridShape_Raycast` ([the collision volume](world.md#the-collision-volume--gridshape)) and from `BeamTracer_Ctor` (`00008268`), which rescales an `ELF` beam's step to `0x400` ([the tracer](projectiles.md#the-tracer--beamtracer_ctor-00008268)). The function opens an object file: `Math_RandomBelow`'s `RET 8` at `00063b57` is followed by two zero bytes ([where object files meet](../formats/compiler.md#where-object-files-meet-in-the-code)).

## The slew

ES1's simulator also has a 0x12-byte object that moves an `int16` position toward a target with a limited speed, an acceleration that ramps by a fixed step, and an exponential settle near the target. Among the callers of ES2's `Math_RateLimitedMoveToward` found by `es2_xref.py`, none is a counterpart.

| Offset | Type | Field |
|---|---|---|
| `+0` | `uint16` | acceleration limit, `topSpeed >> shift`, at least 1 |
| `+2` | `uint16` | jerk, the acceleration's step, `limit >> shift`, at least 1 |
| `+4` | `uint16` | settle band, `topSpeed << shift` |
| `+6` | `int16` | position |
| `+8` | `int16` | target |
| `+0xa` | `int16` | velocity |
| `+0xc` | `int16` | acceleration |
| `+0xe` | `uint16` | top speed |
| `+0x10` | `int8` | shift |

`Slew_Ctor` (`0005e77c`) takes the object, the top speed and the shift, derives the first three fields and zeroes the velocity and acceleration, and leaves the position and target as they were. Each `Slew_Step` (`0005e658`) takes `error = target − position` and:

- when the error and the velocity do not point opposite ways and the error is inside the settle band, sets the velocity to `error >> shift` (arithmetic) and the acceleration to 0;
- when they do not point opposite ways and the error is outside the band, moves the acceleration toward its limit by the jerk, then the velocity toward the top speed in the error's direction by the acceleration (both with `Math_RateLimitedMoveToward`);
- when they point opposite ways, moves the acceleration toward its limit the same way and the velocity toward the target by the acceleration, stopping at the error;

then adds the velocity to the position and returns it. An error and a velocity that are both 0 count as pointing the same way.

## Open

- **Open:** where the object file `Math_NormalizeVec3ToLength` opens ends: `es1_boilerplate.py bounds` finds no zero fill or module copy from it to `00063cf7`.
- **Open:** no caller of `Slew_Ctor`, `Slew_Step` or `Math_RateLimitedMoveTowardInt` found by `es2_xref.py`.

- **Open:** a retail check of the two-interrupt frame: under DOSBox-X with enough cycles for 60 frames a second, a 10000-count countdown (the mission status poll, [`mission-status.md`](mission-status.md#open)) runs out in 2.6 seconds of wall time rather than 3.9, and a HERC covers a measured distance in two thirds of the time it takes at 40 frames a second or fewer.
- **Open:** whether anything changes the divisors `000bdbd4` and `000bdbd6` after `Time_InitSimClock`: `es2_xref.py` finds no other store to either.
