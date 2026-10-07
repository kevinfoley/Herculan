# External views (DBSIM.EXE)

The views from outside the cockpit: the outside view [V] opens, the joystick's OUTSIDE VIEW and CHASE VIEW, the free camera only the developer keys reach, the camera that takes over when the player's machine is destroyed, and the camera object every view — the cockpit's included — is drawn from. The keys are listed in [`../key-bindings.md`](../key-bindings.md#displays-and-views); the joystick actions' dispatch is [`../formats/joystick-input.md`](../formats/joystick-input.md#the-buttons); the cockpit view manager whose view 4 these all live in is [`../formats/cockpit-views.md`](../formats/cockpit-views.md#views).

The manual's own description ("COCKPIT CONTROLS: External Views") agrees with all of it: [V] out and [V] or [Esc] back, pan with the stick or arrows, zoom with the fire button or [Space] held, [Enter] to swap between viewpoint and HERC control, [N] for the other HERCs in the squad, which can be rotated and zoomed but not controlled.

## The chain of views

`ViewChain_View` (`004d2572`) holds which view the game is in and `ViewChain_PendingView` (`004d259e`) which it is going to. Both start at 2 (`Main_StaticInit`, `0045cad8`). `ViewChain_Chosen` (`004d25a0`) is the object the outside view is looking at, the player at mission start, and `ViewChain_CameraControlPreferred` (`004d259c`) is whether the outside view starts with the controls on the camera.

| `ViewChain_View` | View | Camera mode (`CAM+0x36`) | Viewing | Controls |
|---|---|---|---|---|
| 1 | Free camera | 0 | where the outside view left off | as `ViewChain_CameraControlPreferred` says |
| 2 | Cockpit | 2, attached | the player, or the chosen object after [Ctrl+F] | the machine |
| 3 | Outside view | 1, orbit | `ViewChain_Chosen` | the camera, by default |
| 4 | Chase view | 3, chase | the player | the machine |

Every command that changes view sets `ViewChain_PendingView` and queues a cockpit view command — 2 to enter the manager's view 4, 3 to leave it — from `Sim_DispatchCommand` (`0045fdac`) and `Sim_PollPlayerInput` (`00460764`):

| Command | From 1 | From 2 | From 3 | From 4 |
|---|---|---|---|---|
| [V] (`0x2f`), OUTSIDE VIEW (action 12) | leave, to 2 | enter, to 3 | leave, to 2 | leave, to 2 |
| CHASE VIEW (action 15) | leave, to 2 | enter, to 4 | to 4, nothing queued | leave, to 2 |
| [Esc] (scancode 1) | leave, to 2 | — | leave, to 2 | leave, to 2 |
| [Ctrl+F] (`0x421`) | leave, to 2 | enter, to 3 | to 1 at once, camera to mode 0 | — |

[V] and [Esc] refuse while the camera is locked (`CAM+0x4a`); OUTSIDE VIEW and CHASE VIEW do not test it. CHASE VIEW is refused until the frame counter `Sim_FrameCount` (`004d25ff`) is past `0x31` — the main loop (`Sim_Run`, `0045f144`) adds one to it after every frame, so this is the chase view's 50-frame history filling, not mission time. Both joystick actions and [V] clear `ViewChain_FollowChosen` (`004d25b8`), which says whether the cockpit rides the chosen object; [Ctrl+F] sets it.

[Esc] reaches the dispatcher's case 1 only because the cockpit's widgets are off in view 4: anywhere else, `CockpitWidgets_HandleCommand` (`00432bc8`) takes scancode 1 first for the heads-down and glance returns.

**CHASE VIEW does nothing from the outside view.** It sets `ViewChain_PendingView` to 4 and queues nothing, and the pending view is only ever applied while the view manager is mid-step (below). Nothing steps the manager from view 4 but a command 3, and every command that queues one also sets `ViewChain_PendingView` back to 2 first. The chase view is reached from the cockpit only.

### Moving between views — `ViewChain_Apply`

`ViewChain_Apply` (`0045de14`) is called once a tick from `Sim_MainTick`, after `Sim_PollPlayerInput`. It acts only while the cockpit view manager's step flag `+0x1c` is up, which `CockpitView_ProcessViewCommand` raises when it takes a queued command and `CockpitView_StepViewTransition` lowers when it finishes it one frame later ([`../formats/cockpit-views.md`](../formats/cockpit-views.md#heads-down-pan--cockpitview_stepviewtransition-0042a9c0)). So a view changes on the frame after its key, and the camera and the manager change on the same frame. If `ViewChain_PendingView` differs from `ViewChain_View` it copies it over and:

| To | Views | Camera mode | Controls to the camera (`InputDrivesCamera`, `004d2574`) |
|---|---|---|---|
| 1 | — | 0 | `ViewChain_CameraControlPreferred` |
| 2 | the player, or `ViewChain_Chosen` while `ViewChain_FollowChosen` is set | 2 | cleared |
| 3 | `ViewChain_Chosen` | 1 | as `ViewChain_ViewObject` leaves it |
| 4 | the player | 3 | cleared |

The last two also draw the caption (below).

While the flag is down it does one thing: in view 3, looking at the player, it copies `ViewChain_CameraControlPreferred` into `InputDrivesCamera`. That is what makes [Enter] take effect, and why [Enter] does nothing while another HERC is being viewed.

[Enter] (`0x1c`) and [Tab] (`0x0f`), with the view manager in view 4, call `ViewChain_ToggleCameraControl` (`0045fd2c`): `ViewChain_CameraControlPreferred` flips, and the caption is redrawn if the camera is on the player. It starts at 1, so the outside view opens with the controls on the camera. With the widgets off, [Tab] is no longer the Targeting Pod's and swaps the controls exactly as [Enter] does.

[N] (`0x31`), in view 3 with the view manager in view 4, walks the live-object list on from the viewed object `ViewChain_Viewed` (`004d2708`) to the next one at or above height 0 whose group (`+0x45`) is the player's, the player included. A new one is viewed through `ViewChain_ViewObject` and becomes `ViewChain_Chosen`.

## The camera object — `CAM`

`ViewObjectPtr` (`004d256e`) points at a `CAM` (`0x4e` bytes, vtable `0049f900`, a `TSCamera` by its class record). The main loop (`Sim_Run`, `0045f144`) places it once a frame with `Cam_Update` (`004011a0`), between `Sim_MainTick` and the render, whatever else is frozen: `Alt+S` stops neither.

| Offset | |
|---|---|
| `+0x04` | position, three ints |
| `+0x10` | the view's euler triple: pitch, roll, heading |
| `+0x22` | orbit distance |
| `+0x24` | zoom rate in the orbit; speed in the free camera |
| `+0x26`, `+0x28`, `+0x2a` | orbit pitch, roll and heading, the heading relative to the viewed object's |
| `+0x2c`, `+0x2e`, `+0x30` | their rates |
| `+0x32` | the viewed object |
| `+0x36` | mode |
| `+0x38` | orbit centre, three shorts in the object's frame |
| `+0x3e` | attached eye, three shorts in the object's frame |
| `+0x44` | a rotation added to the attached view's |
| `+0x4a` | lock: while set, nothing changes the mode, the object or the rates. [The player-death camera](#the-player-death-camera) sets it |

`Cam_AttachTo` (`0045ed94`) attaches a camera to an object. It asks the object's vtable `+0x30` for two points and passes the first to `Cam_AttachEye` (`0040111c`: eye to `+0x3e`, object to `+0x32`, mode 2) and the second to `Cam_AttachOrbit` (`0040109c`: centre to `+0x38`, object, and distance, all three orbit angles and all three rates zeroed, mode 1). Both do nothing while the camera is locked. An attach therefore always ends in mode 1, and a new outside view starts directly behind the object at the least distance, level and not turning.

| Class | vtable `+0x30` | Eye | Orbit centre |
|---|---|---|---|
| Machine | `Mech_GetAimPoint` (`004155c4`) | `(0, type+0x64, type+0x66)` | `(0, type+0x68, type+0x6a)` |
| Structure | `Base_GetAimPoint` (`0040351c`) | zero | `(0, 0, BASES.DAT +0x2c)` |
| Flyer, base object | `SimObject_GetAimPointZero` (`00411a74`) | zero | zero |

The machine's four type-record fields, and their values across the fleet, are in [`mech-locomotion.md`](mech-locomotion.md#mech-type-record). The structure's height is the one it is aimed at ([`structure-behaviour.md`](structure-behaviour.md#what-a-structure-is-aimed-at)).

### Mode 1: the orbit

```
distance = clamp(distance + zoomRate, 0x9c4, 30000)
heading += headingRate;  pitch += pitchRate;  roll += rollRate
if |pitch| > 0x3000:  pitch = ±0x3000, pitchRate = 0
view   = (pitch, roll, heading + object.heading)
eye    = objectFrame · orbitCentre + rotate(view, (0, -distance, 0))
floor  = Terrain_HeightQuery(eye) + 0x226
if eye.z < floor:
    eye.z = floor
    if pitchRate > 0:                      -- pitching up drove the eye into the ground
        pitch -= pitchRate;  pitchRate = 0
        view.pitch = last frame's view pitch
        recompute eye, and raise it to the floor again if it is still under
```

The camera looks along its own forward axis from `distance` behind the centre, so it always faces the object. Only the object's heading turns the orbit; its pitch and roll do not.

### Mode 3: the chase

Every frame, whatever the mode, `Cam_Update` first records the player's position (`+0x26`), euler triple (`+0x0c`) and `Math_IntegrateRateOverTick(0x400)` into a 50-entry ring (`ChaseTrail_Positions` `004a7c80`, `ChaseTrail_Rotations` `004a7ed8`, `ChaseTrail_Lengths` `004a8004`, cursor `ChaseTrail_Cursor` `004a80cc`). The chase camera walks back from the newest entry subtracting each frame's length from 8000 — about one second at a 40 ms frame — going round the ring again if it has to, until an entry's length covers what is left. It then interpolates, by that remainder over that entry's length, from the entry after it towards it:

```
fraction = Math_Q16Divide(remaining, length[i])
position = pos[i+1] + (pos[i] - pos[i+1]) · fraction      -- the same for the euler triple
```

The interval between `pos[i+1]` and `pos[i]` is `length[i+1]`, not `length[i]`, so the camera sits one frame later along the path than the delay it measured. It sits on the recorded origin itself, looking the way the machine was facing then, with no height added and no ground test.

### Mode 0: the free camera

```
heading += headingRate;  pitch += pitchRate (clamped to ±0x3000 as above);  roll += rollRate
position = view · (0, speed, 0) + position
floor    = height under the old position + Q10(500, |ground normal's XY|) + 0x226
```

### Mode 2: attached

The eye `+0x3e` through the object's vtable `+0x24` node composed with its frame (`Transform_Concat`), and the view from that matrix through `Transform_RotationToEuler` (`0047f894`) plus `+0x44`. This is the cockpit: for a machine the node is the camera bone and the eye the pilot's offset from it ([type record](mech-locomotion.md#mech-type-record)). What the node is for the other classes:

| Class | vtable `+0x24` | Attached view |
|---|---|---|
| Machine | `Mech_GetAimNodeTransform` (`00417b98`) | the camera bone's transform |
| Structure | `Base_GetAimNodeTransform` (`00403548`) | a node that is the identity with the translation `(0, 0, BASES.DAT +0x2c)`, so the eye sits at the structure's aim-point height looking along its own heading. Only [Ctrl+F] reaches it ([target-selection.md](target-selection.md#aim-point--vtable-0x24)) |
| Flyer, base object | `SimObject_GetAimNodeTransform_None` (`00411a9c`) | no node: ridden by the object's own frame and euler triple, without `+0x44` |

## Steering the camera — `Cam_Steer`

`Cam_Steer` (`00401c74`) is called by `Sim_PollPlayerInput` once a tick with the steering axis, the throttle axis and the trigger byte while `InputDrivesCamera` is set, and with zeros otherwise. It does nothing while the camera is locked, and works only in modes 0 and 1; in the others the rates hold until the camera is next in one of these.

| | Heading rate | With the trigger held | Otherwise |
|---|---|---|---|
| Mode 1 | → `Q8(0x800, steer)`, step `0xc0` | zoom rate → `Q8(0x400, throttle)`, step `0x50`; pitch rate → 0 | pitch rate → `Q8(0x800, throttle)`, step `0xc0`; zoom rate → 0 |
| Mode 0 | → `Q8(0x800, -steer)` | speed → `Q8(0x400, -throttle)` | pitch rate → `Q8(0x800, throttle)`; speed → 0 |

Each "→" is `Math_RateLimitedMoveToward`: the rate moves towards the target by at most the step each frame. Nothing is scaled by the frame's length, so the camera turns at a rate per frame.

The steering and throttle axes are the stick's and keyboard's, re-pointed at the camera by the input build, which leaves the machine none of them: [While the camera has the controls](../formats/joystick-input.md#while-the-camera-has-the-controls).

## One camera per squadmate — `ViewChain_ViewObject`

`Sim_InitMissionSession` builds one `CAM` for each member of the player's group (`ViewChain_SquadCameras`, `004d270c`, the count from the group's `+0x10`) besides the one it attaches to the player's eye for the cockpit. `ViewChain_ViewObject` (`0045df18`) views an object by taking the first of those whose `+0x32` is that object, setting it to mode 1, or else the first free one, which is copied field by field from the current camera and attached afresh. With none of either it uses the spare `ViewChain_SpareCamera` (`004d27d8`) the same way. So each HERC in the squad keeps its own outside view — distance, angles and rates — from one visit to the next, and the player's cockpit and outside view share a camera after the first [V].

It then stores the object in `ViewChain_Viewed` (`004d2708`) and sets `InputDrivesCamera` to 1 for any object but the player, and to `ViewChain_CameraControlPreferred` for the player.

## The spectator flag — `DAT_0049ef5c`

A dword whose image value is 0. `es2_xref.py` finds seven accesses to it over the whole PE, every one a `CMP dword ptr [..],0`. Where it is set the code treats the player as watching another machine: the watched object `ViewChain_Viewed` (`004d2708`) stands in for the player's machine as the origin of what the player senses and fires, while a command still reaches the player's own machine.

| Site | With the flag set |
|---|---|
| `TargetSelect_CanTarget` (`00433174`) | Range to a candidate is measured from the watched object instead of `view+0x203` |
| `TargetSelect_InForwardCone` (`00433250`) | The bearing is taken from the watched object, less its heading, with no turret twist added |
| `Player_PerFrameCockpitUpdate` (`0041b130`) | The range to the selected target, which the weapon manager's readiness gate reads ([`weapon-mounts.md`](weapon-mounts.md)), is measured from the watched object |
| `WeaponMount_PrepareShot` (`0040e788`), two compares | For the local player, the hardpoint's bone record is copied with its last translation component lowered by 1500 (`0x5dc`) and composed with the watched object in place of the machine ([`weapon-firing.md`](weapon-firing.md)) |
| `Flyer_DirectFireHitTest` (`00421c8c`) | The watched flyer skips the distance-against-radius prefilter that otherwise ends the test early |
| `Sim_DispatchCommand` (`0045fdac`), the default case | The command goes to `LocalPlayerMech` through vtable `+0x2c`; with the flag clear it goes to `ViewChain_Viewed`, which is the player's machine until the camera moves |

## What the external view shows

Entering view 4 (`CockpitView_ApplyViewState`, `00429e60`) installs the default 3D rect `CockpitViewManager_Ctor` (`00429660`) made — columns 0-319 and rows 0-194 at 320x240, doubled in the 640-wide modes, with the projection centre 160 across and 90 down — and turns the cockpit's widgets off (`+0x20f`). `View_FillOutside3dRect` (`0042da08`) floods everything outside the rect with colour 19. No cockpit, gunsight, MFD or heads-down display is drawn ([`../formats/cockpit-hud-widgets.md`](../formats/cockpit-hud-widgets.md#when-each-display-ticks)).

With the widgets off, `CockpitWidgets_HandleCommand` answers nothing, so every key it owns goes dead: the weapon-row numbers, [F1]-[F11], [Enter] and [;], [Tab], [R], the `Alt` order letters and [Alt+D]. [Enter], [Tab] and [Esc] fall through to the dispatcher's own cases above. The dispatcher's own cases — ['], [T], [Q], [P], [F12] — work as in the cockpit, and so do the joystick actions that call their handlers directly; HDD VIEW and COCKPIT VIEW, which go through the widgets, do nothing.

**The caption.** `ViewChain_DrawCaption` (`0045e1ec`) fills one row of the band below the 3D rect and writes two runs on it in the green 6x8 face (`DAT_004d1eb0`): `STRINGS0.STR` group 36's `VIEW: ` and a name at x = 50, and its `CONTROL: ` and `CAMERA` (while `InputDrivesCamera` is set) or `HERC` at x = 215, both shifted in the 640-wide modes. The name is group 17's `YOU` for the player, the pilot in the squadmate's comm box (`Squad_IndexOf`, `Squad_PilotName`), or nothing for an object not in the squad. The row is centred between the rect's last row and the screen's, kept two rows off the bottom; each run's y is the row's bottom, which `HudFont_DrawGlyph` hangs the ink above. It is drawn only on the two frames `ViewChain_CaptionFrames` (`004d25a4`) counts down after a change that sets it — entering views 3 and 4, [N], [Enter] while viewing the player, [Ctrl+N]/[Ctrl+P] outside the cockpit — and stays on screen until it is drawn again. [Ctrl+F]'s step to the free camera and [Ctrl+T] leave it as it was.

## The player-death camera

`ViewChain_PlayerDeathCamera` (`0045f978`) is what the player watches once their machine is destroyed. `Sim_MainTick` calls it once a tick in place of the objective poll, while the player's machine is destroyed (`+0x99`) and the simulation is not frozen (`DAT_004d2576`); the status alert it leads to is [`mission-objectives.md`](mission-objectives.md#the-status-alert--gnl_alrt-00455934)'s.

Its first call, latched by `DAT_004d2aec`:

1. arms the countdown `DAT_004d2adc`, a `LongCountdownTimer` ([`sim-object-layout.md`](sim-object-layout.md)), from the class table below;
2. makes the player `ViewChain_Chosen`;
3. from the cockpit (`ViewChain_View` 2) queues view command 2 and sets `ViewChain_PendingView` to 3, as [V] does; from any other view, views the player at once through `ViewChain_ViewObject` and sets `ViewChain_CaptionFrames` to 2;
4. sets `InputDrivesCamera` and calls `CockpitView_ProcessViewCommand` and `ViewChain_Apply` itself, so the outside view comes up on the frame of the death rather than the next. A view change in the two frames before leaves the view manager cooling down, so that pass arms nothing and step 5 poses the camera still in use;
5. writes the orbit's pitch and heading from the table, its roll as 0, and its distance, each only while the camera is unlocked. The rates are left as they were.

Every call then clears the camera's lock (`+0x4a`), steers it with `Cam_Steer` from the table's three constant controls, sets the lock again, and steps the countdown with `Timer_CountDown`, answering whether it has reached zero. **The lock is what makes it a cutscene.** `Sim_PollPlayerInput`'s own `Cam_Steer` with the player's stick, earlier in the tick, finds the camera locked and does nothing, and [V], [Esc] and [N] refuse; OUTSIDE VIEW and CHASE VIEW do not test the lock ([above](#the-chain-of-views)).

The seven tables are indexed by the type record's `+0x50`, which is 1 in `RAZOR.DAT` and 0 in the other twenty chassis files:

| `+0x50` | countdown `0049f50c` | steer `0049f514` | throttle `0049f518` | trigger `0049f51c` | pitch `0049f520` | heading `0049f524` | distance `0049f528` |
|---|---|---|---|---|---|---|---|
| 0, walker | 20000 | 50 | -30 | 0 | `0x1f40` | 0 | 2500 |
| 1, RAZOR | 6000 | 50 | 30 | 1 | `-0x1f40` | 0 | 7000 |

Through the [orbit](#mode-1-the-orbit) and [`Cam_Steer`](#steering-the-camera--cam_steer), that is:

- **A walker**, for about 9.8 seconds ([timer units](dbsim-physics-notes.md#timer-units)): directly behind the wreck at the least orbit distance, pitched 44° up, so the eye starts low and looks up at the machine. The heading rate builds to 400 a frame and the pitch rate to -240, so the view circles while swinging down through level after about 33 frames to the 67° downward limit after about 85, where it stays.
- **The RAZOR**, for about 2.9 seconds: 7000 units behind, pitched 44° down. The heading rate builds to 400 a frame as a walker's does; the trigger turns the throttle constant into zoom, so the distance grows by up to 120 a frame and the pitch holds.

## Rejected readings

| Reading | Why it is wrong |
|---|---|
| [Esc] cannot leave the external view, since `CockpitWidgets_HandleCommand` claims scancode 1 from view 4 without queueing anything | It claims it only while the widgets are on. View 4 turns them off (`+0x20f`), and the handler's first test returns 0 on that byte, so scancode 1 reaches the dispatcher's case 1 |
| CHASE VIEW switches from the outside view to the chase view | It sets the pending view and nothing applies it — see [The chain of views](#the-chain-of-views) |
| `Sim_FrameCount`, CHASE VIEW's gate, is mission time | It is a frame count, incremented once a frame by the main loop, and the gate is the chase ring's length |
| The chase view is an orbit or trailing camera placed behind the machine | It replays the machine's own recorded position and facing from a second earlier; there is no offset at all |

## Open

- **Open:** what sets the spectator flag `DAT_0049ef5c` ([above](#the-spectator-flag--dat_0049ef5c)). The whole-PE sweep finds no store to it, so retail appears to run with it clear and every branch in the table unreachable; the sweep is a null result, not proof.
