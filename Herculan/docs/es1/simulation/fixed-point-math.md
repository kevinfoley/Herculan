# DBSIM.EXE — fixed-point math

ES1's simulator against ES2's [fixed-point math toolkit](../../retail/simulation/dbsim-physics-notes.md#fixed-point-math-toolkit). Addresses are in `ES1DBSIM`, the program image of ES1's `DBSIM.EXE` ([the X-32 executable](../formats/x32-executable.md#the-image)); ES2 names and addresses are DBSIM's. The hand-written assembly library and its tables are ES2's; the C primitives beneath the simulation round where ES2's truncate.

## The assembly library

**The hand-written assembly library is identical.** With image-address operands masked, each of these ES2 functions occurs whole in `ES1DBSIM`:

| | |
|---|---|
| byte for byte | `Math_FastMagnitude2D`, `Math_FastMagnitude3D`, `Math_ChebyshevMagnitude2D`, `Math_ChebyshevMagnitude3D`, `Math_ISqrt`, `Math_MapRange`, `DotProductVec3Short`, `DotProductVec3Int`, `Vec3I_DotSign`, `Vec3I_Cross`, `Math_TransposeRotation2D`, `Transform_TransposeRotation`, `Math_Rotate2DPoint`, `Transform2D_ApplyToPoint`, `Rotation_Concat`, `Transform_ConcatRotation`, `Transform_Concat`, `RotateVectorByMatrixQ14`, `Transform_ApplyToShortPoint` |
| but for image addresses | `Math_CosBam` (`0006477c`), `Math_AsinBam` (`0005f54c`), `Math_BuildRotation2D`, `BuildEulerRotationMatrixQ14`, `Transform_RotationToEuler` |

`GO.EXE`'s `BuildEulerRotationMatrixQ14` (`ES1GO 000589c4`) is not: it truncates each of its 15 trigonometry-table indexes (`SAR 4`) where `ES1DBSIM`'s (`0005f0ac`) and ES2's round them (`SAR 4`, then `ADC 0`).

The tables the trigonometry reads are identical: the cosine quarter-turn table (`000981cc`, ES2 `004a25dc`, 0x401 words), the arctangent table (`0009e648`, ES2 `004a1c4c`, 513 bytes) and the used ranges of both arcsine tables (`00097634` and `000979b4`, ES2 `004a1e54` and `004a21d4`, 0x201 words each). The arctangent, `Math_Atan2Bam` (`000734a0`), is ES2's without its `PUSH EBX`/`POP EBX`: it overwrites `EBX`, which both compilers' conventions require a callee to keep. `Math_FastMagnitude3D` is `0006325a`.

## The primitives

**The fixed-point primitives round to nearest, where ES2's truncate.**

| | `ES1DBSIM` | ES2 |
|---|---|---|
| Q8, Q10 multiply | `Math_Q8Multiply` (`0005e7f8`), `Math_Q10Multiply` (`0005e818`): 32-bit × 16-bit, rounded | `Math_Q8Multiply`, `Math_Q10Multiply`: 32-bit × 32-bit, truncated |
| Q14 multiply | `Math_Q14Multiply` (`0005e838`): rounded | `Math_Q14Multiply`: 32-bit × 16-bit, truncated |
| Q16 multiply | `Math_Q16Multiply` (`00073574`) and its copy `Math_Q16Multiply2` (`0007358c`): `0x8000` added before the shift | `Math_Q16Multiply` and its copy `Math_Q16Multiply2`: truncated |
| Q16 divide | `Math_Q16Divide` (`00073534`): divides the magnitudes, adds 1 when the remainder is at least half the divisor rounded up, then restores the sign | `Math_Q16Divide`: signed `IDIV`, truncated toward zero |

The Q8, Q10 and Q14 multiplies add half a unit (`0x80`, `0x200`, `0x2000`) when the product fits in 32 bits; when it does not, `SBB ECX,ECX` / `XOR ECX,half` adds `0xffffff7f`, `0xfffffdff` or `0xffffdfff` instead. The rounding is general in ES1: of `ES1DBSIM`'s 27 `SHRD EAX,EDX` instructions, 25 follow a rounding add and the other 2 (`0007e0ae`, `0007e41c`) normalise a mantissa. Of DBSIM's 26, the 20 in the matrix routines above follow a rounding add and the 6 in the primitives do not.

## Rate over tick

The rate-over-tick helpers differ as well. ES1's `Math_IntegrateRateOverTick` (`00063fa4`), which the bullet tick calls where ES2's `Bullet_TickUpdate` calls ES2's, multiplies the rate by the short at `000bdbd0` through the rounding Q8 multiply, then sign-extends the product from 16 bits before its ±0x7fff clamp, so an out-of-range product wraps where ES2's saturates. `Math_IntegrateRateOverTick2` (`00063fe0`) does the same with the short at `000bdbd2`, where ES2's reads `004d3bea`.

## Helpers that match

These C helpers do the same as ES2's of the same name, read side by side:

- `Math_Atan2Guarded` (`00063ca0`) and `Math_HeadingToward` (`00063cc8`), which take their arguments in ES2's order;
- `Math_CountdownTimerTick` (`0006410c`), whose tick delta is the short at `000bdbd0`;
- `Math_DistanceBetweenPoints` (`00063c20`), which subtracts in line where ES2's calls `Vec3_Subtract`;
- `Math_RateLimitedMoveToward` (`0005e598`);
- the generator ([ES2's](../../retail/simulation/random-generator.md)): `Math_RandomNext` (`00063af4`) and `Math_RandomBelow` (`00063b3c`) over the same 56-short state with its cursors at `+0x70` and `+0x71`, and `Math_RandomSeed` (`00063aa4`), whose 112-byte seed table (`00098150`) is ES2's at `004a6958`. ES1's seeding calls nothing after the copy, where ES2's calls an empty function.

## Open

- **Open:** what the shorts at `000bdbd0` and `000bdbd2` hold, and what sets them. `0006410c` subtracts the first where `Math_CountdownTimerTick` subtracts `SimTickDelta`; `Math_IntegrateRateOverTick` (`00063fa4`) multiplies by the first and `Math_IntegrateRateOverTick2` (`00063fe0`) by the second, as ES2's `Math_IntegrateRateOverTick2` does by `004d3bea`.
