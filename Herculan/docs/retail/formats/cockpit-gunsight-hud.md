# Front-window HUD: the gunsight complex

Reverse-engineered from `DBSIM.EXE` in the `ES2Recon` Ghidra project. All addresses are DBSIM unless noted. Symbols are in `tools/ghidra_scripts/known_symbols_dbsim.json`; apply with `ES2ApplySymbolNames.java`.

Verified against retail data in `ES2/VOL/simvol0/{hba,gau,dat}/`.

Everything drawn over the live 3D view, rather than on the console, belongs to one widget: `Gau_RovingGunsightWidget` (`0043c7d8`), built from `.GAU` offset **1088**. The console and console-mounted gauges are a separate widget tree: [`cockpit-hud-widgets.md`](cockpit-hud-widgets.md). The view manager and per-view geometry: [`cockpit-views.md`](cockpit-views.md). How a mouse click on the gunsight surface reaches its handler: [`cockpit-input.md`](cockpit-input.md).

## Front-window HUD — the gunsight complex

Its own ints:

| int | file offset | Role |
|---|---|---|
| `[0]`,`[1]` | 1088, 1092 | Origin added to every child rect. Zero in all 9 retail files |
| `[2]`,`[3]` | 1096, 1100 | The complex's own bottom-right — `320, 117` or `320, 157` |
| `[4..7]` | 1104-1116 | **Heading tape** rect. `100,y - 220,y+17` in every file, so 120x17 centred on the 320-wide HUD. The rotation indicator is derived from it, below |
| `[8..0xb]` | 1120-1132 | Speed and time readout anchors — see below |
| `[0xc]`,`[0xd]` | 1136, 1140 | Reticle point |
| `[0xe]` | 1144 | Half-extent of child 4's rect about the reticle point. Zero in all 9 retail files, and unread by that child's paint |
| `[0xf..0x12]` | 1148-1163 | Rect shared by children 0, 5 and 6 — the gunsight area, the target arrow's safe area |
| `[0x13..0x16]` | 1164-1179 | The **`ATT` legend's** rect — see below |
| `[0x17..0x1a]` | 1180-1195 | A second label of the same kind, at the widget's `+0x107`. Neither gunsight paint reaches it |
| `[0x1b]`,`[0x1c]` | 1196, 1200 | Top-left of the floating scanner repeater, a bare point with no size. Per herc; see [`mfd-scanner.md`](mfd-scanner.md) |

The complex also builds two `ColorSchemePanels[12]` (`dark`) labels of its own, at `+0x103` and `+0x107`. The first is the manual's **`ATT` legend** — see [below](#the-att-legend).

`Gunsight_AddChild` (`0043d5a4`) appends to a pointer array at the widget's `+0xd7`, so construction order *is* child index. `Gunsight_Paint` (`0043d5c8`) walks that array calling each child's slot 0, then draws what sits outside it: the [`ATT` legend](#the-att-legend), the [speed and time readouts](#speed-and-time-readouts), the **floating scanner repeater** (`Gunsight_PaintHudScanner` (`0043e0ec`) into `HudScanner_Paint` (`0043f2b0`)) and the [RAZOR's altitude scale](#the-razors-altitude-scale), which works from a second derived point at the widget's `+0x113` — the reticle plus `(0x46 << XCoordShift, -0x12 << YCoordShift)`.

All nine children derive from `GunsightChild_CtorBase` (`0043b344`), a bare rect holder. Children 4, 5 and 6 additionally receive the 38-byte state block described in [`hud-target-indicator.md`](hud-target-indicator.md), at `+0x14`.

| # | Ctor | What it is |
|---|---|---|
| 0 | `Gunsight_ClickSurface_Ctor` (`0043c120`) | The clickable gunsight surface. Registers a child gadget with the cockpit's click list — the "gunsight click" entry into `TargetSelect_SetObject`. Its paint (`Gunsight_ClickSurface_Paint`, `0043c1dc`) only tracks the cursor against its rect |
| 1 | `HudHeadingTape_Ctor` (`0043b57c`) | Heading tick tape, bank `hudhtick`, limits ±`0xe38` |
| 2 | `HudRotationIndicator_Ctor` (`0043b438`) | The rotation indicator, limits ±`0x38e3` |
| 3 | `HudSlideBar_CtorHidden` (`0043b54c`) | The pitch axis's slide bar — **paint slot is a no-op** (`HudSlideBarHidden_PaintNoOp`, `0043b574`), so it is never drawn |
| 4 | `GunsightChild_CtorBase`, vtable `0049c124` inline | The reticle |
| 5 | `Gunsight_TargetIndicator_Ctor` (`0043b928`, vtable `0049c1c4`) | The target box and its off-screen arrow |
| 6 | `Gunsight_UnusedChild_Ctor` (`0043c240`) | Constructed and fed the state block, but its **paint slot is `ret`** (`Gunsight_UnusedChild_Paint`, `0043c260`) |
| 7, 8 | `HudWaypointIndicator_Ctor` (`0043c268`) | The two waypoint indicators, below. 7 takes the `+0x45` flag that makes it the nav marker's |

Children 4 and 5: [`hud-target-indicator.md`](hud-target-indicator.md).

### Live values

`Player_PerFrameCockpitUpdate` (`0041b130`) calls `Gunsight_SetValues` (`0043d98c`) once a frame with three shorts — `mech+0x10` (heading), `mech+0x298` (twist angle), `mech+0x29a` (pitch angle). Heading and twist are refreshed only while the machine's immobilised byte (`+0xa4`) is clear; once it is set they keep the values last cached. The widget caches them at `+0xb1`/`+0xb3`/`+0xb5` and forwards each one's **delta** to a child's `AddDelta` slot (`+0xc`):

| value | child | delta sent |
|---|---|---|
| heading | 7 | `new - old` |
| twist | 2 | `old - new` |
| pitch | 3 | `old - new` |

Children 2 and 3 are slide bars, and `HudSlideBar_AddDelta` (`0043b3f8`) does `value -= delta` clamped to the bar's limits, so they *track* their angle. Both start at zero, which is where the machine's angles start.

**The heading's delta reaches a waypoint indicator, and does nothing.** Child 7 overrides slot `+0xc` with `HudWaypointIndicator_ShiftLimits` (`0043c3d0`), which adds the delta to `+0x24` and `+0x26` rather than to a value — and `Hud_UpdateWaypointIndicator` reads neither, only the range `+0x2c` that an equal shift of both leaves alone. The heading tape, child 1, is not driven from here at all.

The same call copies the whole 38-byte state block into children 4 and 5. The target's fields in it — the object, its aim point, heading, pod flag and radius — are filled by the gunsight's own update slot, `Gunsight_UpdateAndPaint` (`0043d6dc`), from the target block at `CockpitView+0x26c`; the indicator-armed and missile-lock bytes have writers of their own — see [`hud-target-indicator.md`](hud-target-indicator.md). That slot also drives child 1 and runs each child's slot `+4`.

### Rotation indicator

The manual's sliding green bar. It has **no `.GAU` rect of its own**: `Gau_RovingGunsightWidget` derives it from the heading tape's rect with literals — `+15, -10` from its top-left, 90 wide and 4 tall, all in `.GAU` units and shifted by the video mode.

`HudRotationIndicator_Paint` (`0043b4a4`) draws two `HUD`-bank frames:

```
track:  frame 11 (182x10) at (rect.x0, rect.y0 - (2 << YCoordShift))
bar:    frame 13 while |value| <= 299, frame 12 otherwise
        x = rect.x0 + (value - min) * rectWidth / range  (+1 if value < 0)  - (15 << XCoordShift)
        y = rect.y0
```

Frame 13 is green and 12 yellow; the 299 threshold is about 1.6°, so any deliberate movement trips it. The trailing `-15` undoes the `+15` the rect carries, which centres the 31-unit-wide bar on the mapped point. The ±`0x38e3` limit is about 80°, wider than the 14000 twist limit every player chassis states, so the bar never reaches the ends of its track.

### Heading tape

Child 1, the manual's Heading Indicator. `HudHeadingTape_Recompute` (`0043b5dc`) caches the rect's width at `+0x28` and the `hudhtick` bank's last and first frames at `+0x48`/`+0x4c`; `HudHeadingTape_SetHeading` (`0043b654`) converts the angle it is given into a frame pair and a sub-frame offset:

```
total   = Math_Q16Multiply(heading, framePixels)     // +0x40, bankFrames * rectWidth in Q16
frame   = total / rectWidth,  offset = total % rectWidth   // both wrapped at bankFrames
+0x44   = rect.x0 - offset
+0x48   = bank[frame],  +0x4c = bank[frame + 1]
```

The paint (`0043b6dc`) narrows the canvas clip to the rect and blits those two frames at `+0x44` and `+0x44 + rectWidth`, so the tape is a strip of full-width frames sliding through a window: the whole compass, degree labels included, is art, and the angle picks which slice of it shows. Retail's bank is nine 256x16 frames against a 240-device-pixel window (the low-resolution `.DBA`, nine 128x8 against 120), so the pair always covers it with no seam.

**The angle is the heading negated.** `Gunsight_UpdateAndPaint` reads the viewing object's `mech+0x10` and calls child 1's `+0xc` with `-heading`. Without that sign the strip would run opposite to the simulation's own bearings, and a tick would slide one way while the waypoint diamond naming the same bearing slid the other. Because the art's degrees rise left to right, the negation is also what makes the readout count *up* as the machine turns right, the ordinary compass convention, out of headings that run counter-clockwise.

#### Power-up wind-up

On taking a machine the tape starts at north and winds round to the real heading — the gunsight's part in the cockpit's [power-up sequence](cockpit-hud-widgets.md#power-up-sequence), which owns the armed/done fields and the arming. While armed and not done, `Gunsight_UpdateAndPaint` substitutes a ramp for the heading:

```
ramp = (ushort)((coarseTicks - +0x90) * 0x32)
heading <= 0x8000:  angle = ramp,   done when heading <= ramp
heading >  0x8000:  angle = -ramp,  done when -ramp <= heading
```

`0x32` a coarse tick is about 17°/s, so the longest wind-up is some ten seconds. It always takes the short way round: below half a turn the angle climbs from north, above it the angle descends. `Cockpit_PowerUpTick` (`00432924`) arms the gunsight on the first tick after the sequence's start.

**Two things stop it, which is why it is not seen every mission.**

- **A flyer never winds up.** A RAZOR's cockpit skips the [whole sequence](cockpit-hud-widgets.md#power-up-sequence), so its compass reads true from the first frame.
- **A heading past half a turn finishes on its first frame.** The descending branch is done as soon as `-ramp <= heading`, and a zero `ramp` is at or below every heading in that half. `Sim_RenderFrame` (`0045fb9c`) arms the widget in `CockpitView_PerFrameUpdate` (`004327ac`) and runs `Gunsight_UpdateAndPaint` later in the same pass, after the world draw, reading the clock again; unless a 16 ms coarse tick falls between the two reads (or the gunsight's update is skipped that frame), `ramp` is zero and a machine facing anywhere past `0x8000` is done before it has moved. The climbing branch survives that frame, since a climbing zero is below every heading but zero itself. Listed in [`../../../KNOWN_ISSUES.md`](../../../KNOWN_ISSUES.md).

Only the tape is ramped. The waypoint indicators over it go on reading the true heading throughout, so they and the compass visibly disagree for as long as the wind-up lasts.

### Waypoint indicators

Children 7 and 8, the manual's Waypoint Indicator. Both are `HudWaypointIndicator_Ctor` (`0043c268`) — `HudRotationIndicator_Ctor`'s object with vtable `0049c154`, a label child, and the `±0xe38` limits the gunsight hands them. **Neither has a `.GAU` rect of its own**: the complex passes both of them the heading tape's rect (offset 1104), so a mark rides the same span of bearing the compass under it does. The `+0x45` flag separates them:

| Child | `+0x45` | Subject | Colour id | Caption |
|---|---|---|---|---|
| 7 | 1 | `NavMarker_Position` (`0043495c`) | `DAT_004d3c1e`, id 15 → palette 13 yellow | none |
| 8 | 0 | The waypoint after the player group's route cursor, or `mech+0x1a4` while `DAT_004d2af0` is set | table entry 0 → palette 14 green | `WAYPOINT n: d M.` |

What each points at, and how the player's route and the nav marker behave, is [`../simulation/player-waypoints.md`](../simulation/player-waypoints.md).

**Child 8 follows the route.** Its branch to the player's selected target is gated on `DAT_004d2af0`, a `.bss` dword with no writer found — see [Open](#open).

`Hud_UpdateWaypointIndicator` (`0043c3e4`) is the shared paint. It takes the ground range with `Vec2_DistanceBetween` and the bearing with the `Math_Atan2Guarded(dx, dy) - 0x4000` that `Math_HeadingToward` is, then works the error `mech.heading - bearing` as an unsigned short:

| Error | Shape |
|---|---|
| ≤ `0xe38` or ≥ `0xf1c8` | Diamond, on the tape |
| `0xe39`-`0x7fff` | Arrow past the rect's **right** end, pointing right |
| `0x8000`-`0xf1c7` | Arrow past its **left** end, pointing left |

Simulation headings run counter-clockwise, so a positive error is a subject off to the player's right — which is the end its arrow parks at, and the side of centre its diamond sits on.

Both shapes are filled polygons through `Raster_DrawPolygonDispatch`, not sprites, and both hang off the rect's **top** edge lifted `4 << YCoordShift`. The diamond's centre is `rect.x0 + rectWidth/2 + error * rectWidth / 0x1c70` — the widget's own `+0x28` width over its `+0x2c` range — so it reaches the rect's ends exactly at the limits; it is `5 << XCoordShift` by `5 << YCoordShift` about that point. An arrow's base sits `2 << XCoordShift` past the rect's end with its tip `4 << XCoordShift` further out and its base `5 << YCoordShift` tall.

The caption is the label child, given the tape's rect dropped `3 << YCoordShift` and centred in it — which puts the line below the compass while the marks sit above. Font is `ColorSchemePanels[17]` (`HUD3`, the same face the speed and time *values* use). The text is `STRINGS0.STR` group 37 entry 1 (`"WAYPOINT "`, trailing space included) then the waypoint number, `": "`, the range in metres and `" M."`. The number is the route cursor plus one and the range is `Hud_WorldUnitsToMetres`, so it is always a multiple of six — see [`../../herculan/planning.md`](../../herculan/planning.md#world-scale).

### Speed and time readouts

`Gau_RovingGunsightWidget` places these from two anchor points in the same block, both already device-shifted:

- **1128/1132** is the *left* edge of the `SPEED:` caption. The value follows at `captionEnd + (2 << XCoordShift)`.
- **1120/1124** is the *right* edge of the time field. Its left edge is that minus the measured width of `"00000"` — a five-digit reservation — and the `TIME:` caption is right-aligned `(2 << XCoordShift)` before it.

The speed value's rect is as wide as `"000 K/H"` measured in the value font. Its text is rebuilt on every paint — `Gunsight_Paint` and `Gunsight_UpdateAndPaint` both call `Hud_UpdateSpeedReadout` (`0043dc78`) — as the decimal of `Mech_GetDisplaySpeedKph(LocalPlayerMech)` (`0041bb3c`) followed by `" K/H"`. What that figure means, and why it overstates a walking Herc's speed, is in [`../simulation/mech-locomotion.md`](../simulation/mech-locomotion.md#walkrun-gait-discontinuity).

Captions use `ColorSchemePanels[16]` (`HUD2`, ink 73) and values `[17]` (`HUD3`, ink 74). Those are theater palette indices, not colours the widget picks — which is where retail's pale yellow-green captions and cyan values come from.

### The ATT legend

The manual's upper-left indicator that Automatic Turret Tracking is on. Its label is built over the rect at 1164 — 24x7 in every retail file, `68,0 - 92,7` on most hercs, `60,0` on OGRE, `30,0` on SAMSON and `60,67` on RAZOR — centred (`Label_SetRect` flag 2) with no margin.

Both `Gunsight_Paint` and `Gunsight_UpdateAndPaint` test the console button panel's auto-track latch (`CockpitView+0x1e1`, byte `+0xb3` — the flag `ConsoleButtons_GetStateBlock` copies into the mount manager's `+0x14`) after the child loop. While it is set they blit `HUD` frame 14, a 50x16 plate, at the rect's top-left, then set the label's text to `STRINGS0.STR` group 37 entry 0, `ATT`. While it is clear nothing is drawn. The tracker itself: [`../simulation/torso-aim.md`](../simulation/torso-aim.md#automatic-turret-tracking--t).

### The RAZOR's altitude scale

`Gunsight_PaintAltitudeScale` (`0043dd70`), which `Gunsight_Paint` and `Gunsight_UpdateAndPaint` call last, after the scanner repeater, draws only when the piloted machine is a flyer (type record `+0x50`). It is a vertical scale `0x24 << YCoordShift` tall hanging from the point at `+0x113`, out of the `HUD` bank's frames 15-18, drawn in this order:

1. Frame 15, a bracket, is blitted at the point, and frame 16, a second bracket, with its bottom edge at the scale's foot. The scale proper runs from frame 15's bottom row (the *top*) to frame 16's top row (the *foot*).
2. A column over columns `x + w - (1 << XCoordShift)` to `x + w` inclusive, with `x` the point's and `w` frame 15's width: three device pixels in the 640-wide modes, its right side one column past frame 15. Its two sides are `Raster_DrawLine`s in palette index `0x49` from the top down to the terrain height under the machine (`Terrain_HeightQuery` at its x, y); then a `Raster_FillRect` in `0x4b` fills it from that row to the foot, both ends inclusive.
3. Frame 17, a marker, is centred on the column at the machine's own height (`mech+0x2e`).
4. Frame 18, a strip of ticks, is blitted twice at `x + (2 << XCoordShift)`: once with its top at `height / 50 % (0x24 << YCoordShift)` below the top, and once a frame height above that. **Neither blit reaches the screen** — see [The tick tape is never drawn](#the-tick-tape-is-never-drawn).

Both heights map linearly from the zone's height range, `grid+0x110` at the foot to `grid+0x114` + 5000 at the top, as `foot - (h - floor) * (foot - top) / (ceiling - floor)` with a 64-bit product and a truncating divide, and are clamped to the scale. The pen and brush are plain palette indices the paint states as immediates, not `COLORS.DAT` ids.

#### The tick tape is never drawn

Retail shows the brackets, the column and the marker, and no tape. The paint runs inside `Cockpit_PushCanvasContext`, so the context it narrows for the tape is the cockpit canvas's, which is built with its [blit translation](hud-target-indicator.md#why-the-box-goes-behind-the-cockpit-frame) on. Around the two tape blits (`0043dfac`-`0043e0dc`) it saves the context's rect, origin and clip mode, sets the rect to the strip — `x + (2 << XCoordShift)` to that plus frame 18's width, top to foot inclusive — sets the mode to 1 and the origin `+0x220`/`+0x224` to zero, and restores all of them after. It leaves the translation flag `+0x20c` at 1, so `Bitmap_BlitClipDispatch` (`004886cc`) moves each tape blit by the strip's own top-left less a zero origin: the tape lands at twice the strip's left edge, and since that edge is at least `0x48 << XCoordShift` (the reticle's x plus `0x46`, plus 2) and the strip is one frame wide, that is right of the strip's right edge on every herc in every video mode, and the clip rejects the blit whole. `HudHeadingTape_Paint` (`0043b6dc`), which narrows the same context the same way, narrows only x and sets the origin's x to the rect's new left edge, which cancels the translation in x, so its strip shows.

The brackets, column and marker are drawn before the rect changes, through the canvas's own rect and origin, and are unaffected.

What frame 18 would show is real art: in `hba\HUD.HBA` it is 6x60 in palette index `0x4a` on a colour-0 ground, a full-width tick every 30 rows and a short one at the right edge every 6. The offset wraps at 72 rows, not at the art's 30, so a drawn tape would jump its long ticks by 12 rows at each wrap and leave the scale's top rows bare while the offset is past 60.

## Open

- **Open:** no writer of `DAT_004d2af0`, which would point [waypoint indicator](#waypoint-indicators) child 8 at `mech+0x1a4` instead of the route, found by `es2_xref.py` (one reference in the image, the `CMP` in `Hud_UpdateWaypointIndicator` at `0043c448`) or by a scan of `all_asm.txt` for block bases below it: its neighbours `DAT_004d2aec` and `DAT_004d2af4` are written by name, the nearest pushed base `0x4d2adc` is a `Timer_CountDown` block that touches only `+1`..`+4`, and `0x4d29dc` is a 256-byte string buffer.
