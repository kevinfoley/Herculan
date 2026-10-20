# DBSIM.EXE — the camera

ES1's simulator places its view through ES2's camera object with fewer modes: a free camera, an orbit around an object, and a camera riding an object's eye, with no chase view and no lock ([ES2's external views](../../retail/simulation/external-views.md)). Addresses are `ES1DBSIM` image addresses ([the X-32 executable](../formats/x32-executable.md#the-image)); ES2's are DBSIM's.

## The camera

The camera module is `000003ac`–`00000ac0`, ES2's `Cam_*` family function for function:

| ES1 | ES2 | Difference |
|---|---|---|
| `Camera_IsMountedOn` (`000003ac`) | `Cam_IsAttachedTo` (`00401078`) | |
| `Cam_AttachOrbit` (`000003d0`) | `Cam_AttachOrbit` (`0040109c`) | no lock test |
| `Cam_AttachEye` (`00000454`) | `Cam_AttachEye` (`0040111c`) | no lock test |
| `Cam_SetMode` (`00000480`) | `Cam_SetMode` (`00401148`) | modes 0–2; any other value is ignored |
| `Cam_Update` (`000004b8`) | `Cam_Update` (`004011a0`) | below |
| `Cam_ApproachRate` (`000009b8`), `Cam_ApproachZoom` (`000009e0`) | `Cam_ApproachRate`, `Cam_ApproachZoom` | the same constants: `Q8(0x800, axis)` at step `0xc0`, `Q8(0x400, axis)` at step `0x50` |
| `Cam_Steer` (`00000a04`) | `Cam_Steer` (`00401c74`) | no lock test; [ES2's table](../../retail/simulation/external-views.md#steering-the-camera--cam_steer) holds for modes 0 and 1 |

The functions are callee-cleaned, their arguments in stack order the reverse of ES2's after the camera: `Cam_AttachOrbit(camera, centre, object)`, `Cam_AttachEye(camera, eye, object)`, `Cam_Steer(camera, trigger, throttle, steer)`, `Cam_ApproachRate(axis, rate)`. [The player's controls](main-loop.md#the-players-controls--sim_pollplayerinput-0005a0c0) say which input axis is which.

The camera's fields from `+0x22` on are ES2's: distance `+0x22`, zoom rate or speed `+0x24`, orbit pitch, roll and heading `+0x26`–`+0x2a`, their rates `+0x2c`–`+0x30`, the object `+0x32`, the mode `+0x36`, the orbit centre `+0x38`, the eye `+0x3e`, and the rotation added to the attached view `+0x44`–`+0x48`. The position is at `+0x00` and the view's euler triple (pitch, roll, heading) at `+0x0c`, where ES2's are at `+0x04` and `+0x10`. The object is `0x4a` bytes, so it has no lock at `+0x4a`.

`Sim_Main` (`00059350`) calls `Cam_Update` once a frame on the main camera `000b5b8a`.

### `Cam_Update`

The ground is flat at height 0, so where ES2 tests the terrain height plus `0x226`, ES1 tests 100.

**Mode 0, free.** As ES2's: the pitch, roll and heading take their rates, the pitch clamped to ±`0x3000` with its rate zeroed, and the position moves by the view's rotation of `(0, speed, 0)`. Its height is then raised to at least 100.

**Mode 1, orbit.**

```
distance = clamp(distance + zoomRate, 2500, 30000)
heading += headingRate;  pitch += pitchRate;  roll += rollRate
if |pitch| > 0x3000:  pitch = ±0x3000, pitchRate = 0
view = (pitch, roll, heading + object's int16 +0x0a)
eye  = objectFrame · centre + rotate(view, (0, -distance, 0))
if eye.z < 100:
    if zoomRate < 1:  restore the position, view and all three orbit angles the call began with; return
                      -- the clamped distance and a zeroed pitch rate stay
    pitch = Math_AsinBam((centre.z - 100) · 0x4000 / distance)    -- in the view and in +0x26
    recompute eye
```

The object's frame is `Context_GetTransform` of its `+6` block, and `centre.z` is the centre's height after that frame. So zooming in or holding still against the ground leaves the camera where it was, and zooming out tilts the orbit so the eye sits at height 100.

**Mode 2, attached.** As ES2's, through the object's vtable (at its `+2`) slot `+0x28`, where ES2's is `+0x24`; with no node, the object's frame and its own euler triple at `+6`.

## Open

- **Open:** what the main camera's `+0x12`–`+0x21` hold. In a camera built on `TSCamera_Ctor` (`0005b99c`), `+0x12` is `TSContext`'s vptr ([object layout](../formats/compiler.md#object-layout)) and `+0x16`, `+0x1a` and `+0x1e` hold three of the constructor's arguments, which for the missile camera are its viewport, 7 and `0x80` in that order ([the missile camera](mfd-missile-camera.md#the-viewport-and-the-camera)); `Sim_StartMission` builds `000b5b8a` with `TSCamera_Ctor`(camera, (0, 0, 0), (0, −6000, 0), `0x100`, 8, the render context `000b5b6e`) ([the mission start](main-loop.md#the-mission-start--sim_startmission-0005a9dc), step 12), and which of those land at `+0x16`–`+0x1e` is not read.
- **Open:** what `0005771c`, one of the two calls of `Cam_Steer` found by `es2_xref`, steers it with (the other, `Sim_PollPlayerInput`'s, is [the player's controls](main-loop.md#the-players-controls--sim_pollplayerinput-0005a0c0)), and what `0005771c`, `00057820` and `00057ffc` place with `Cam_Update`.
