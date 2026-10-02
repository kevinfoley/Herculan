# Joystick input (DBSIM.EXE)

How a stick reaches the simulation: the device layer that reads the hardware, the twelve bytes of `data\prefs.cfg` that say what each control does, and the per-frame build that turns one into the other. The two panels that *edit* those twelve bytes are [`../simulation/preferences.md`](../simulation/preferences.md)'s; this document owns everything below them. Mouse routing and keyboard command codes are [`cockpit-input.md`](cockpit-input.md)'s.

```
joyGetDevCapsA / joyGetPosEx     Joystick_Enumerate (00477568) / Joystick_Poll (00477614)
  -> raw X, Y, Z, R + POV + buttons, two devices merged
  -> Joystick_NormaliseAxes (00477750)       normalise each axis to +/-0x80
  -> Joystick_ReadWithResponse (0045c314)    deadzone and the squared response curve, to +/-0x100
  -> Input_PollDeviceBlock (0045ba8c)        the device block at DAT_004d247a
  -> Input_BuildPlayerDevice                 apply the bindings; write the four game axes and eight buttons
  -> Sim_PollPlayerInput                     the control laws, and the button action switch
```

**The bindings name no hardware.** Four bytes say which *pair of game axes* each control feeds and eight say which *action* each button fires. Nothing in the file identifies a device, an axis number or a HID usage — which is why the file survives a change of input backend intact. HERCULAN's side file for that step is [`../engine/joystick-config.md`](../engine/joystick-config.md). <!-- doc-lint: ok -->

## Reading the hardware

`Joystick_Enumerate` (`00477568`) enumerates with `joyGetDevCapsA` over joystick ids **0 and 1 only**, filling a `JOYCAPS` per device at `DAT_006bb3fc` (stride `0x194`) and stamping each one's `MMRESULT` at `DAT_006bb724`. `Joystick_Poll` (`00477614`) then polls both with `joyGetPosEx` into `JOYINFOEX` blocks at `DAT_006bb394` (stride 52), `dwFlags` fixed at `0xccf`:

| Bit | Flag | |
|---|---|---|
| `0x001`-`0x008` | `JOY_RETURNX/Y/Z/R` | the four axes, and the only four asked for — **`U` and `V` are not requested**, so a fifth and sixth analogue control is invisible to the original |
| `0x040` | `JOY_RETURNPOV` | the hat |
| `0x080` | `JOY_RETURNBUTTONS` | the button mask |
| `0x400` | `JOY_RETURNCENTERED` | |
| `0x800` | `JOY_USEDEADZONE` | Windows' own band, on top of the game's |

There is no calibration of the game's own: `JOYCAPS`' `wXmin`/`wXmax` are never read, and the axis maths below assumes the full 16-bit range. Whatever the Windows control panel's calibration produces is what the game gets.

**Two devices, merged into one.** A second stick is not a second controller — it fills in whatever the first lacks:

| Field | From stick 0 | Or, when stick 0 lacks it |
|---|---|---|
| stick X, Y | `dwXpos`, `dwYpos` | — |
| throttle | `dwZpos`, if `JOYCAPS_HASZ` | stick 1's `dwXpos` |
| rudder | `dwRpos`, if `JOYCAPS_HASR` | stick 1's `dwYpos` |
| buttons | `dwButtons` | OR'd with stick 1's, shifted left by stick 0's `wMaxButtons` |
| hat | `dwPOV`, if `JOYCAPS_HASPOV` | — |

**The hat has four positions and no more.** `dwPOV` is tested against exactly `0`, `18000`, `27000` and `9000`, setting bits 0-3 of one byte — north, south, west, east. Anything between them, which is every diagonal a modern eight-way hat reports, matches nothing and the byte stays clear.

## Normalising an axis

Two steps, and reading only the first of them is the trap that costs a factor of two.

`Joystick_NormaliseAxes` (`00477750`) maps a raw reading with literal constants: `(raw << 8) / 0xffff - 0x80`, so an axis arrives spanning `-0x80..+0x80`. The response step below takes that width as `1 << res` from the device object's resolution field (`+0x16`), which `JoystickDevice_CtorBase` (`004774d0`) sets to 7.

`Joystick_ReadWithResponse` (`0045c314`) then applies the deadzone and a **response curve** selected by the object's `+0x28`. The joystick is constructed as `JoystickDevice_Ctor(obj, 3, 0x201, 0xf, 1)` (`0045c27c`) by `Joystick_InitAndSeedBindings` (`00459dd4`) — that last argument is `+0x28`, so a joystick is always **mode 1, the squared curve**:

```
span = (1 << 7) - 0x19                  = 103        // deadzone 0x19 subtracted, not rescaled away
k    = Math_Q16Divide(1, span*span >> 8) = 1/41 in Q16
out  = sign(x) * Math_Q16Multiply(|x|², k)
```

Full deflection is therefore `103² / 41 = 258` — a shade over `0x100`, which the control laws clamp to. **`0x100` is the scale every input source shares:** a held keyboard direction is worth `0x80`, half of it ([`../simulation/mech-locomotion.md`](../simulation/mech-locomotion.md)), and a hat direction `0xc0`.

Modes 0 and 2 take the deadzone's other arm (`local_18`, a rescale to `0x80`), and mode 0 is then linear. Mode 2 reads the axes through `Joystick_ReadAxisSigns` (`0047779c`) instead of `Joystick_NormaliseAxes`, collapsing each to -1, 0 or +1, which that deadzone then zeroes, so a mode-2 device reports no axis movement. Nothing constructs a joystick with either — see [Rejected readings](#rejected-readings).

## The capability block — `Input_QueryCapabilities` (`004777f8`)

Eight bytes rebuilt on every call, and the whole of what any caller is told about the hardware. It is straight `JOYCAPS`:

| Field | Is | From |
|---|---|---|
| `+0` | 2 when stick 1 enumerated, otherwise 1. **Never 0** | `DAT_006bb728`, stick 1's `MMRESULT` |
| `+2` | button count, clamped to 8 | `wNumButtons[0] + wNumButtons[1]` |
| `+4` | has a throttle | `wCaps & JOYCAPS_HASZ`, **or** stick 1 being present |
| `+5` | has a rudder | `wCaps & JOYCAPS_HASR`, or the same |
| `+6` | has a hat | `wCaps & JOYCAPS_HASPOV` |

Field `+0` never being 0 is why the presence test is `Input_GetDevice(3)` (`0045c508`) — a lookup into the device table `Input_DeviceTable` (`004d24ec`) — rather than a field of this block. The CONTROLS panel's use of the block is [`../simulation/preferences.md`](../simulation/preferences.md#the-capability-block); the throttle row also reaches `+4` through `Input_SetThrottleLeverMode`.

## Sources and destinations — `Input_BuildSourceTable` (`0045a5c0`)

The four game axes the control laws read are the device struct's `+0x0e`, `+0x10`, `+0x12` and `+0x14` (`DAT_004d2358` onwards): steering, throttle, torso twist, torso pitch. The trigger is the byte at `+0x0d` and the eight button states are `+0x16`..`+0x1d`.

`Input_BuildSourceTable` fills a table of pointers at `DAT_004d2394` with the address of every value that can *feed* one of those — the two keyboard axis pairs `Input_BuildKeyboardAxes` (`0045a4b0`) writes, the four joystick axes, and the buttons — so that a binding is a choice of pointer rather than a hardcoded path. That indirection is the mechanism the whole scheme rests on: the control laws never learn which device moved an axis, and so never need a joystick case.

### The keyboard

`Input_BuildKeyboardAxes` (`0045a4b0`) produces **two signed axis pairs, not four independent axes**. It reads fourteen held-key flags from the key-state block at `004d2418`, which `Input_KeyjoyAxisKey` fills by the position of each key in its wanted-codes list ([`cockpit-input.md`](cockpit-input.md#how-a-keystroke-becomes-one-of-those-codes)): keys 0-7 (keypad 7, 8, 9, 4, 6, 1, 2, 3) feed the first pair and keys 8-13 (`M`, `J`, `K`, `I`, keypad `-` and `+`) the second. A key contributes its `(dx, dy)` entry from the table at `0049eb6d` shifted left 7, so a key is worth ±0x80, half a stick's travel; `dx` goes to the pair's first axis (steering, or twist) and `dy` to its second (throttle, or pitch). The two groups combine differently:

- **Keys 0-7 add up, except the diagonals.** The byte flags at `0049eb65` (`01 00 01 00 00 01 00 01`) mark keypad 7, 9, 1 and 3. The first of those held, in list order, zeroes the pair, adds its own entry and ends the group, so a diagonal overrides every other key of the group.
- **Keys 8-13 take the first held key only.** Its entry is added and the loop ends, so [J] and [I] held together twist without pitching.

Keypad `-` and `+` reach the second pair only while flying the RAZOR; `Input_KeyjoyAxisKey` drops them otherwise. The pairs are the *sources* the table above registers, so a binding chooses which pair each game axis reads.

The `(dx, dy)` table is zero in the image. A static initialiser at `0045b888`, registered in Borland's `_INIT_` table at `004a7b64` (priority `0x20`) and lying in bytes Ghidra has not made a function, fills it before `main`:

| Key | `(dx, dy)` | Key | `(dx, dy)` |
|---|---|---|---|
| keypad 7 | (−1, −1) | `M` | (0, −1) |
| keypad 8 | (0, −1) | `J` | (−1, 0) |
| keypad 9 | (+1, −1) | `K` | (+1, 0) |
| keypad 4 | (−1, 0) | `I` | (0, +1) |
| keypad 6 | (+1, 0) | keypad `-` | (0, −1) |
| keypad 1 | (−1, +1) | keypad `+` | (0, +1) |
| keypad 2 | (0, +1) | | |
| keypad 3 | (+1, +1) | | |

No panel edits it: the CONTROLS panel binds the stick, throttle, rudder, hat and buttons only.

## Applying the bindings — `Input_BuildPlayerDevice` (`0045a7f4`)

The per-frame input build, and where the twelve bytes are read. `ControlsOptionBase` (`DAT_004d25fb`) selects the walking block or the RAZOR's.

### The four axis rows

Each row's byte is 0, 1 or 2, and **the three values mean the same thing on the three analogue rows**: 0 is unassigned, 1 points the control at the *movement* pair of game axes (steering, throttle) and 2 at the *turret* pair (twist, pitch). The words `CTL_ALRT.STR` shows differ only because a given control reaches a different half of the pair:

| Row | Feeds under 1 | Feeds under 2 |
|---|---|---|
| JOYSTICK — stick X, Y | axes 0 and 1 | axes 2 and 3 |
| THROTTLE — the Z axis | axis 1 | axis 3 |
| RUDDER — the R axis | axis 0 | axis 2 |

A control whose destination is already spoken for lands in a **second rank** behind it, and the combine prefers the second when both have moved:

```
axis = secondRank  if it is non-zero
     : firstRank   if it is non-zero
     : the keyboard pair
```

So a rudder bound to DIRECTION overrides the stick's own steering while it is being pushed, and hands back when it centres. The same test is what lets the keyboard reach an axis the stick is bound to but not currently moving.

Two sign flips ride along: a RAZOR negates the stick's Y when the JOYSTICK row is 2, and negates the throttle axis unconditionally whenever a throttle exists.

**With the stick on the movement pair the keyboard is moved off it.** When the JOYSTICK row reads 1, the keyboard's first axis pair is copied onto the second and zeroed — the arrow keys aim the turret instead of duplicating the stick — with the pitch half negated on the way.

### The hat

Only under HAT = 2 does the hat drive axes, and then it writes straight over the turret pair at `0xc0`, testing north, south, east, west in that order and stopping at the first:

| Direction | Writes |
|---|---|
| north / south | axis 3 = `+0xc0` / `-0xc0` |
| east / west | axis 2 = `+0xc0` / `-0xc0` |

Under HAT = 1 the four bytes are left alone and reach `CockpitView_PollViewDevice` (`00432b14`), which queues view commands 1, 0, 5 and 4 off them — up, down, and the two outside-view steps. Under HAT = 0, and after the axis write under HAT = 2, they are zeroed, so the view path sees nothing.

### While the camera has the controls

The two camera axes, the device struct's `+0x22` and `+0x26`, are pointers that `Input_BuildPlayerDevice` sets at its tail, and `Sim_PollPlayerInput` reads through them to steer the external-view camera ([`../simulation/external-views.md`](../simulation/external-views.md#steering-the-camera--cam_steer)). While `InputDrivesCamera` is clear they follow the JOYSTICK row (the turret pair under 2, the movement pair otherwise). While it is set they point at the steering and throttle axes, and the input build changes in step:

- The keyboard's first pair is no longer moved onto the second, so the arrow keys steer the camera instead of the turret.
- The stick's X and Y replace that keyboard pair wherever they have moved and are zeroed as sources, so the stick reaches the camera whatever the JOYSTICK row says.
- A throttle lever and a rudder keep their bindings on a stick that has a lever, and are zeroed on one without.
- Backturn is applied afterwards, to the camera's axes as it is to the machine's.

`Sim_PollPlayerInput` (`00460764`) chooses its branch on `InputDrivesCamera` alone (`004607cd`). Under it the machine's steering, throttle and twist inputs are zero and `Mech_PlayerFireTick` is skipped, so its trigger is not read; the two centring commands, being dispatcher cases, still reach it. Its pitch axis is the exception. `Sim_PollPlayerInput` still reads the device struct's `+0x14` into the turret block, but only when a stick is present (`Input_GetDevice(3)`) and the capability block's `+4` says it has a throttle, a second stick counting. Of the device's sources only the lever can reach it: the stick's X and Y and the keyboard's second pair are all zeroed as sources, and a rudder feeds the twist axis, which this branch does not read. The [gunsight drag](#the-gunsight-drag) is added after either branch, and stays zero in play. A lever bound to the turret pair (THROTTLE = 2) therefore pitches the machine's turret while the camera flies, and under any other binding the axis reads zero. What the turret does with it is [`../simulation/torso-aim.md`](../simulation/torso-aim.md#automatic-turret-tracking--t). The input build takes the same branch while an electro-optical round is being flown (`DAT_004d25aa`, [`../simulation/rockets.md`](../simulation/rockets.md#the-missile-camera)), with one more step: before the stick replaces the keyboard pair, the pair's throttle half — the round's pitch — is negated, unless `keyjoy.cfg`'s `Missile` says `Reverse`. `Sim_PollPlayerInput` does not follow it: with `InputDrivesCamera` clear it takes its ordinary branch and runs `Mech_PlayerFireTick` ([Open](#open)).

### The gunsight drag

After either branch `Sim_PollPlayerInput` adds a mouse term to the twist and pitch it hands the machine. It arms when the device struct's `+0x0b` comes up with the pointer (`+0x02`, `+0x06`) inside the rectangle at `+4`..`+0x10` of the roving gunsight (`CockpitViewInstance+0x1f5`) and records that point (`DAT_004d2afa`, `DAT_004d2afe`). While `+0x0b` holds it adds `dx²/64` to twist and `dy²/32` to pitch, `dx` and `dy` being the pointer's distance from that point, positive to the right and up. `+0x0b` clearing zeroes both and disarms it.

The term stays zero in play. `+0x0b` is bit 1 of the mouse record `Input_PollDeviceBlock` copies, and only `WM_RBUTTONDOWN` sets that bit: `Mouse_WndProcHook` (`004808ec`) passes a right button held during any other message as `4`, which `Mouse_DispatchEvent` (`0048083c`) drops. The next mouse event therefore clears the bit before the pointer can have moved, and the term is non-zero only when a right release and a press at a different point land within one tick.

### The buttons

Each of the eight destination bytes takes its device button OR'd with whatever key is bound alongside it. Two things then happen to them.

**The trigger is pulled out first.** The block is scanned for the first row bound to action code 1, FIRE; that button's state, after the latch mask, is copied to the device struct's `+0x0d` and **its own byte is zeroed**, so it never reaches the dispatch below. The trigger is a held state the fire path reads every tick, not a command: `Mech_PlayerFireTick` hands the device struct to `WeaponMounts_FireTrigger` — see [`../simulation/weapon-mounts.md`](../simulation/weapon-mounts.md).

That scan reads `SimOptions[0x11 + i]` — a **literal** `0x11` at `0045b22b`, the walking block's first button row, where the dispatch one step later correctly uses `ControlsOptionBase + 4`. In a RAZOR the trigger is therefore found through the walker's bindings, and the byte zeroed is the button on the walker's FIRE row, whatever the RAZOR binds that button to. A RAZOR whose FIRE row is a different button sends that button on to the dispatch. None of this shows with bindings the CONTROLS panel can set: both blocks offer FIRE on BUTTON 1 alone and nothing else there ([`../simulation/preferences.md`](../simulation/preferences.md#the-bindings-are-twelve-bytes-of-the-same-file)), so both FIRE rows are button 0.

**Everything else is press-once.** `Sim_PollPlayerInput` (`00460764`) walks the eight bytes and switches on `SimOptions[ControlsOptionBase + 4 + i]`; acting on one calls `Input_LatchButton` (`0045b718`), which latches it, and the next `Input_BuildPlayerDevice` masks that button to zero until the player lets go. Holding a button repeats nothing.

The switch's nineteen cases, codes 2-20 (jump table at `00460c77`), against `CTL_ALRT.STR` group 2's names:

| Code | Name | Does |
|---|---|---|
| 1 | `FIRE` | no case. The trigger scan's row never reaches the switch (see above); a second FIRE row, or a RAZOR's own FIRE row on another button, reaches it and takes the default, which does nothing |
| 2 | `TARGET` | `TargetSelect_Cycle`, what [Enter] does |
| 3 | `CENTER LEGS` | latches Center Body and caches the turret's heading |
| 4 | `CENTER TURRET` | latches the centring mode, [Backspace] |
| 5 | `CHANGE DIRECTION` | flips `ThrottleLeverMode` between ±1, gated on the capability block's `+4` (a throttle) and on the walker's THROTTLE row reading 1 — a literal `SimOptions[0x0e]` at `00460d42`, so a RAZOR tests the walker's row too |
| 6 | `ATT TOGGLE` | dispatches command `0x14` |
| 7 | `ALL STOP` | throttle to zero, and the gauge dirty flag |
| 8 | `TARGET NEAREST` | `TargetSelect_Nearest`, ['] |
| 9, 10 | `SHIELDS FRONT`, `SHIELDS REAR` | mech commands `0x1a` / `0x1b`, the bracket keys |
| 11 | `HDD VIEW` | scancode `0x41` (F7) or `1` ([Esc]) to the widget tree — but see [below](#hdd-view-can-only-leave) |
| 12, 15 | `OUTSIDE VIEW`, `CHASE VIEW` | step the chain of views `ViewChain_View`, 12 as [V] does; 15 waits for the frame counter `Sim_FrameCount` to pass `0x31` — see [`../simulation/external-views.md`](../simulation/external-views.md#the-chain-of-views) |
| 13 | `LINK WEAPON` | presses the console LINK button, scancode `0x26` |
| 14 | `MFD DISPLAYS` | `MfdDisplay_CycleMode` (`00446e14`) — step the MFD's mode, wrapping at six |
| 16 | `WEAPON TOGGLE` | weapon command `0x202`, which is `ToggleChainMember(0)` — **row 1's chain membership, not a general toggle** |
| 17 | `COCKPIT VIEW` | scancode 1 to the widget tree |
| 18 | `NEXT CHAIN` | presses the console chain button, scancode `0x29` |
| 19, 20 | `NEXT WEAPON`, `PREV WEAPON` | weapon commands `0x11` / `0x211`, [W] and [Alt]+[W] |

Code 0 is `OFF`, which a row displays when its byte is zero and which the switch has no case for.

#### A latched first row holds the axes

Outside the camera branch, while button 0's latch holds with state 1 the input build zeroes the pair of axes the camera-axis pointers address — the turret pair under JOYSTICK = 2, the movement pair otherwise — after the tape has recorded them. Button 0 is the trigger and `[Space]`, the only binding the panel offers it, so the dispatch never latches it; two `Input_LatchButton(1, 1)` calls do. `Rocket_TickUpdate` makes one as a flown round ends ([`../simulation/rockets.md`](../simulation/rockets.md#flight--rocket_tickupdate-0040a538)), so the deflection that was steering the round does not walk the machine off until the trigger is let go. `AlertPanel_Enter` (`00454630`) latches buttons 0-3 as any modal panel opens ([`../simulation/alert-panels.md`](../simulation/alert-panels.md)), so a trigger still held when the panel closes neither fires nor lets the axes move until it is let go. `DAT_0049ebe5` does the same for the keyboard. Any build with `DAT_004d25aa` up sets it to 1, which the same build drops to 0 unless row 0 is latched, when it becomes 2; it stays 2 until the latch goes, and while it is not 0 the keyboard's first pair is zeroed.

#### `HDD VIEW` can only leave

The case picks between F7 and [Esc] on `CockpitViewManager_Published` (`00429820`), and it tests **the pointer, not a field of it** — so it asks whether the cockpit view manager exists, not which view is up. `CockpitViewManager_LoadViews` publishes that pointer (`004cfa20`) while the cockpit is being built, through `CockpitViewManager_Publish` (`00429810`) with its own `this`; of the 23 references to the address in DBSIM that is the one store, so by the time `Sim_PollPlayerInput` runs the pointer is non-null and the button sends [Esc]. A button bound to `HDD VIEW` can therefore leave the heads-down display and never enter it; see [`../../KNOWN_ISSUES.md`](../../KNOWN_ISSUES.md). The two branches read as the toggle the action's name promises.

#### One action per tick

**At most one action fires per tick.** The loop keeps a 21-entry array to stop two buttons bound to the same action acting twice, but indexes it with the *button byte* — always 0 or 1 — rather than with the action code (`MOV AL, byte ptr [EDI]` at `00460c0f`). The first pressed button claims the only usable slot, and is latched, before the switch looks at its code, so a button the switch has no case for — `OFF`, or a `FIRE` row the trigger scan did not take — claims it too. It costs little, because that button is latched immediately and the next tick lets the one behind it through: two buttons pressed together act one tick apart.

## `data\keyjoy.cfg`

The only part of the input configuration outside `prefs.cfg`. `Keyjoy_LoadConfig` (`0045b78c`) reads it once with four `GetPrivateProfileStringA` calls against section `[Keyjoy]`, each a case-insensitive compare against the word `Reverse` — anything else, the shipped `Default` included, leaves the flag clear. `GetPrivateProfileStringA` is DBSIM's only profile import, so it has no API to write the file with, and none of VSHELL, ES, SETUP or BATCH contains its name; retail ships it with its own explanatory comments for the player to edit. A tape carries a copy, unpacked to `tapes\keyjoy.cfg` and never read ([`tap-input-tape.md`](tap-input-tape.md)).

| Key | Global | Inverts |
|---|---|---|
| `Tilt` | `0049eab8` | the **keyboard's** turret-pitch axis, unconditionally |
| `Backturn` | `0049eabc` | the steering axis while the throttle axis is positive — while backing up. Applied last, to the combined axes |
| `Missile` | `0049eac0` | the keyboard's pitch while a round is flown (`DAT_004d25aa`), which the input build otherwise negates; the stick's is untouched |
| `Rudder` | `0049eac4` | the joystick's rudder axis, and only when the device reports one |

## Rejected readings

| Reading | Why it is wrong |
|---|---|
| The deadzone is a linear rescale, so a joystick axis spans `±0x80` | That is response mode 0/2's arm, and `Joystick_ReadWithResponse` (`0045c314`) skips it for mode 1. `Joystick_InitAndSeedBindings` (`00459dd4`) builds the joystick with mode 1, the squared curve, whose full deflection is `103²/41 = 258`. At `±0x80` a stick would turn at the keyboard's rate rather than twice it, and a throttle lever could neither idle nor open fully, `Mech_ApplyThrottleInput`'s absolute read being `\|axis − 0x100\| × 2` |
| A binding byte names an axis or a button on the device | It names a *destination* — which pair of game axes, or which action code. The device's own layout is fixed in the reading code and stored nowhere |
| The analogue axis rows each have their own meaning for 0, 1 and 2 | The numbers are the same three destinations on JOYSTICK, THROTTLE and RUDDER; only the words differ, because a one-axis control reaches half of a pair and the stick reaches both. The HAT row is the one that differs: its 1 is VIEWS ([The hat](#the-hat)) |
| The hat's VIEWS setting is dead because nothing reads `DAT_004d2368`-`236b` by name | `CockpitView_PollViewDevice` reads them off the device-struct pointer `Sim_PollPlayerInput` hands it, at `+0x1e`..`+0x21`, which produces no direct address reference |
| A second joystick is a second controller | It is a donor. Its X and Y stand in for a throttle and rudder the first stick lacks, and its buttons are OR'd into the first's mask |
| A `winmm` backend would identify the throttle, `dwZpos` being semantic where an ordered array is not | It is not: for a device whose `JOYCAPS.wCaps` reports only X, Y, Z and R — `0x33` on a T.Flight — GLFW enumerates those same four in that same order, so winmm's `Z` *is* GLFW's axis 2 and the mapping is identical. A platform-specific dependency for no behavioural difference |
| `Input_QueryCapabilities`' `+0` says whether a stick is present | It is 1 or 2 and never 0. Presence is `Input_GetDevice(3)` (`0045c508`), a lookup in the device table |

## Open

- **Unported:** a throttle lever bound to the turret pair pitching the machine's turret while the camera has the controls ([above](#while-the-camera-has-the-controls)).
- **Open:** whether a replay holds the axes still for good once the first row is latched. Playback jumps past the loop that drops a released button's latch, and the tail that zeroes the axes for a latched first row runs after the jump ([above](#a-latched-first-row-holds-the-axes)).
- **Open:** what the machine's four axes carry while an electro-optical round is flown with `InputDrivesCamera` clear. `Sim_PollPlayerInput` then reads the device struct's `+0x0e`..`+0x14` through its ordinary branch, which the input build's camera arm has filled ([above](#while-the-camera-has-the-controls)), and runs the fire path while the weapon chain holds still ([`../simulation/rockets.md`](../simulation/rockets.md#the-missile-camera)).
