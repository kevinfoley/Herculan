# DBSIM.EXE — the gunsight HUD

**ES1's front-window HUD is ES2's gunsight complex ([the gunsight complex](../../retail/simulation/cockpit-gunsight-hud.md)) with eight children instead of nine: a clickable surface, a compass tape, two sliding bars that track an angle, a reticle, a target box, an unused child and one waypoint indicator.** Differences a player could see:

- both sliding bars are drawn, the horizontal rotation indicator and a vertical one, each from a bitmap bank of its own, where ES2's vertical bar paints nothing;
- the compass reads the machine's heading from the first change of heading on, with no wind-up when the machine powers up;
- the reticle is a small cross of four line segments that never changes, where ES2's is a picture that marks the target under it and missile lock;
- the target box is four corner brackets drawn as lines, which become four filled corner triangles with missile lock, in one of two colours by the target's side; it has no centre pip, no edge ticks and no off-screen arrow;
- the next waypoint is a small triangle on the compass, or at its left or right end, captioned `WP n: d M.`; there is no nav-marker indicator;
- the `ATT` legend is the word on a plain filled box;
- a click on the gunsight surface arms the pixel pick, and the object it finds becomes the target, where ES2's click leaves the pick unarmed.

Addresses are `ES1DBSIM` image addresses ([the X-32 executable](../formats/x32-executable.md#the-image)); ES2's are DBSIM's. Every function here removes its own arguments, so argument lists are in stack order ([calling conventions](../formats/compiler.md#calling-conventions)).

## Where the object file starts and ends

The code runs from `00033740` to `0003526f`, after [the status screen's](mfd-status-screen.md#where-the-object-file-starts-and-ends) zero byte, and the initialized data from `00093662`. Both open with the copy of `ConfigPart`'s destructors and tables that starts each object file which carries one ([module copies](../formats/compiler.md#vtables)): `00033740`, `00033768`, `000337a7` (frameless) and `000337b0`, each the code of `panel.cpp`'s ([where `panel.cpp` starts](cockpit-panel.md#where-the-module-starts-and-ends)), and the tables `00093662` and `00093696`.

The last function, `Gunsight_ShiftChildRects`, returns with a `RET 8` that ends at `0003526f` on a 4-byte boundary, and the file has no static initializer. `00035270` is a second copy of `HeapPtr_DtorGunsightCopy`'s code, which this file already holds at `00033740`, so it belongs to the next object file, [the scanner screen's](mfd-scanner.md#where-the-object-file-starts-and-ends); `es2_xref.py` finds no reference to it ([where object files meet](../formats/compiler.md#where-object-files-meet-in-the-code)).

## Building the gunsight

**`Gau_RovingGunsightWidget` (`00034aec`)**, (gunsight, cockpit, block), the constructor `Gau_RovingGunsightWrapper` runs on the `.GAU` block at 1104 ([the block](../formats/gau-cockpit-layout.md#the-gunsight-block-at-1104)), is ES2's builder (`0043c7d8`, [the gunsight complex](../../retail/simulation/cockpit-gunsight-hud.md#front-window-hud--the-gunsight-complex)) on ES1's layout. It allocates `0xd0` bytes when given none (returning 0 when that fails), runs `PanelGauge_Ctor`(gunsight, cockpit, block) ([the gauge base](cockpit-widgets.md#the-gauge-base)) and stores `HUDVtable` (`00093842`) at `+0x76`. Its `0x26`-byte state block is at `+0x7a`, its child array at `+0xa0` (ten slots, the count at `+0xc8`, ES2's `+0xd7` and `+0xff`), and **`Gunsight_AddChild` (`00034e94`)**, (gunsight, child), appends a non-null child, as ES2's (`0043d5a4`) does.

With *o* the block's origin (`int32` 0 and 1) and *p* the reticle point (`int32` 12 and 13), it builds and appends, in this order:

| # | Constructor | Rect | Then |
|---|---|---|---|
| 0 | `Gunsight_ClickSurface_Ctor` (`000345bc`) | `int32` 15–18 + *o* | — |
| 1 | `HudHeadingTape_Ctor` (`00033c20`) | `int32` 4–7 + *o*, the tape's | limits ±`0xe38` |
| 2 | `HudRotationIndicator_Ctor` (`00033968`) | the tape's, its x0 + 15, y0 + 10, x1 − 15, y1 + 10 | limits ±`0x38e3` |
| 3 | `HudSlideBarV_Ctor` (`00033ac4`) | `int32` 8–11 + *o* | limits ±`0x1f40` |
| 4 | `Gunsight_Reticle_Ctor` (`00033f1c`) | *p* ± `int32` 14, + *o* | the centre `+0x1c`/`+0x20` = *p* |
| 5 | `Gunsight_TargetIndicator_Ctor` (`0003403c`) | the reticle's | `+0x24` = the state block |
| 6 | `Gunsight_UnusedChild_Ctor` (`00034710`) | the reticle's | `+0x24` = the state block |
| 7 | `HudWaypointIndicator_Ctor` (`0003475c`) | the tape's | limits ±`0xe38` |

The limits go through each child's set-limits slot, `+0x14`, with (child, max, min). Between child 0 and child 1 it moves the pointer to *p*: `Cursor_SyncPosition` (`000669dc`)(*p*), then, when `Input_GetMouseDevice_Thunk` (`0004c634`) returns non-zero, `Mouse_SetPosition`(*p*.x, *p*.y), and then `00055bc2`({2*p*.x, *p*.y, `int16` 0}, 1), which calls `00055c6f` when the mouse flag `000962e0` is set ([Open](#open)). ES2's moves the pointer to *p*.x and a row of its own, and calls `Stub_ReturnZeroCopy` where ES1's calls `00055bc2`.

Last it builds the `ATT` label at `+0xcc`: `Label_Ctor` with `ColorSchemePanels`' `cpblack` (`00092d60`), `Label_SetRect`(label, 2, {x0 − 3, y0 + 10, x0 + 12, y0 + 15}, {0, 0}) about the heading tape's top-left, centred, and the fill colour `+0x1c` 0x38. It zeroes the dword `+0x64`, the state block and the condition `+0x68`, and returns the gunsight.

ES2's builds a second waypoint indicator, its children 2 and 3 over a rect that the tape's derives with literals and video-mode shifts, its target indicator over the gunsight area, and its three gunsight children (4–6) with a copy of the state block each, where ES1's children 5 and 6 keep a pointer to the gunsight's.

### The state block

| Offset | Holds | Written by |
|---|---|---|
| 0, 2, 4 | the machine's `int16` `+0xa` (its heading), `+0x22e` and `+0x230` | `000155a8`, through `Gunsight_SetValues` |
| 6 | the selected object, or 0 | `Gunsight_UpdateAndPaint`, `TargetSelect_SetObject`, `TargetSelect_Cycle` |
| `0xa`, `0xe`, `0x12` | the object's position, its `+0x20`–`+0x2b` | `Gunsight_UpdateAndPaint` |
| `0x16` | its heading, `+0xa` | the same |
| `0x1c` | its shape's radius: the `int16` `+8` of the dword `+4` of its `+0x2e`, ES2's chain 6 lower | the same |
| `0x20` | 2000 | the same |
| `0x24` | 1 once a selection reaches the gunsight | `TargetSelect_SetObject`, `TargetSelect_Cycle` ([the target selection](cockpit-panel.md#the-target-selection)) |
| `0x25` | the machine's byte `+0x95`, at ES2's lock byte `+0x9b` less 6 | `000155a8` |

`000155a8`, read only around its call, copies the block with `Gunsight_GetStateBlock`, stores `+0x230` at offset 4 and, unless the machine's byte `+0x9e` is set, `+0xa` and `+0x22e` at 0 and 2, stores `+0x95` at `0x25`, and hands the copy to `Gunsight_SetValues` ([Open](#open)). ES2's block ([the state block](../../retail/simulation/hud-target-indicator.md#where-the-selection-reaches-the-hud)) takes the target's aim point and the pod flag from the cockpit's target block.

**`Gunsight_SetValues` (`00035090`)** hands each changed value's delta to slot `+0x10` of a child, ES2's `+0xc` ([live values](../../retail/simulation/cockpit-gunsight-hud.md#live-values)): offset 0's (new − old) to the heading tape (child 1) and the waypoint indicator (child 7), offset 2's (old − new) to the rotation indicator (child 2) and offset 4's to the vertical bar (child 3), then copies the block over the gunsight's. So the vertical bar tracks the machine's `+0x230` as ES2's hidden bar tracks the pitch.

## The slide bars

**ES1's sliding bars are ES2's rotation gauges ([rotation indicator](../../retail/simulation/cockpit-gunsight-hud.md#rotation-indicator)) with the child's rect at `+0` and the vptr at `+0x10`.** The fields from `+0x24` are ES2's: the limits at `+0x24` and `+0x26`, the travel (the rect's width or height) at `+0x28`, the range at `+0x2c`, the track's point at `+0x2e` and `+0x32`, and the value at `+0x3a`.

- **`GunsightChild_CtorBase` (`000337fc`)**, (child, rect), copies the rect to `+0`; the allocation at its head is the rect's own constructor inlined, as in [`PanelGauge_Ctor`](cockpit-widgets.md#the-gauge-base). ES2's also stores a vtable, with only pure-virtual slots.
- **`HudSlideBar_CtorBase` (`0003385c`)**, (bar, rect), stores `RotationGaugeVtable` (`000936de`) and sets ES2's limits ±`0x4800`, range and middle value. **`HudSlideBar_SetLimits` (`000338cc`)**, (bar, max, min), and **`HudSlideBar_AddDelta` (`00033910`)**, (bar, delta), are ES2's; the set-limits ends with the bar's recompute, slot `+0xc`. The table's three slots before them are 0, where ES2's hold `Rtl_PureVirtualCalled`.

Each subclass loads its bank once from the folder `000980f8` names and draws from it:

| | Horizontal: the rotation indicator | Vertical |
|---|---|---|
| Constructor, `0x3c` bytes | `HudRotationIndicator_Ctor` (`00033968`) | `HudSlideBarV_Ctor` (`00033ac4`) |
| Bank | `hudhslid`, `HudHSlidBank` (`000936c8`) | `hudvslid`, `HudVSlidBank` (`000936cc`) |
| Recompute: travel; track point | `HudRotationIndicator_Recompute` (`000339dc`): *x1* − *x0*; (*x0*, *y0* − 1) | `HudSlideBarV_Recompute` (`00033b38`): *y1* − *y0*; (*x0*, *y0*) |
| Paint | `HudRotationIndicator_Paint` (`00033a04`) | `HudSlideBarV_Paint` (`00033b60`) |
| Bar position *p* | (*p*, *y0*) | (*x0*, *p*) |
| *p* | *x0* + *t* − 15 | *y0* + *t* − 7 |
| Frame 2 while the value lies strictly within | ±300 | ±400 |

With *t* = ((value − min) & `0xffff`) × travel / range, unsigned, plus 1 when the value is negative, each paint blits frame 0, the track, at the track point and then the bar, frame 2 near the middle and frame 1 elsewhere, with `Bitmap_Blit` inside `Cockpit_PushCanvasContext` and `Cockpit_PopRenderContext`. ES2's rotation indicator draws `HUD` frames 11, 13 and 12, lifts its track `2 << YCoordShift` and takes `15 << XCoordShift`; ES2's vertical class, `HudSlideBar_CtorHidden`'s, has empty paint and recompute slots. Each class's repaint, **`HudRotationIndicator_Repaint` (`000339c4`)** and **`HudSlideBarV_Repaint` (`00033b20`)**, calls its paint, slot `+4`. The tables are `HFixRotationGaugeVtable` (`000936fe`) and `VFixRotationGaugeVtable` (`00093726`), ES2's in order: paint, repaint, recompute, add delta, set limits.

## The heading tape

**`HudHeadingTape_Ctor` (`00033c20`)**, (tape, rect), allocates `0x54` bytes when given none, runs `HudRotationIndicator_Ctor`, stores `HRevRotationGaugeVtable` (`0009374e`) at `+0x10`, loads the `hudhtick` bank once into `HudHTickBank` (`000936d0`) from the folder `000980f8` names, and calls its recompute directly (by its padding entry `00033c79`). ES2's (`0043b57c`, [heading tape](../../retail/simulation/cockpit-gunsight-hud.md#heading-tape)) picks `hba` or `dba` and calls the recompute through its table. The table holds paint, repaint, recompute, set heading and set limits.

- **`HudHeadingTape_Recompute` (`00033c7c`)**, (tape), is ES2's (`0043b5dc`) but for `+0x40`: *P* = frames × width × `0x400` / `0x10000`, frames × width / 64 truncated, where ES2's holds frames × width.
- **`HudHeadingTape_SetLimits` (`00033eac`)**, (tape, max, min), stores (max − min) / width at `+0x40` and runs `HudSlideBar_SetLimits`, whose recompute writes *P* back, as ES2's (`0043b7b4`).
- **`HudHeadingTape_Paint` (`00033dd4`)**, (tape), is ES2's (`0043b6dc`): inside the canvas push it narrows the context's x extent (`+0x210`, `+0x218`) to the tape's and sets the origin's x (`+0x220`) to the tape's x0 less the old, blits `+0x48` at (`+0x44`, *y0*) and `+0x4c` a width further right with `Bitmap_Blit`, and restores the four fields. **`HudHeadingTape_Repaint` (`00033e94`)** calls it.
- **`HudHeadingTape_SetHeading` (`00033d08`)**, (tape, delta), the slot `Gunsight_SetValues` hands the heading's delta, reads no argument: it takes the angle from the cockpit's machine (`+0x1ff`), *h* = −`+0xa` as an unsigned 16-bit value, and with *W* the width `+0x28` and *F* the bank's frame count:

```
q      = (h * P) >> 10
frame  = q / W,  offset = q % W
offset += ((h >> 8) * ((P * 0xe39) >> 10)) >> 8
if offset >= W:  offset -= W, frame += 1, wrapped to 0 at F
+0x44  = x0 - offset
+0x48  = bank[frame],  +0x4c = bank[frame + 1 wrapped at F]
```

ES2's maps the heading it is given with `Math_Q16Multiply(heading, frames × width)` and no added term. The added term is (*h* / 65536) × (*F* × *W* / 18) to rounding, so over a turn the strip moves about 19/18 of its length, and at north, where *h* wraps from 65535 to 0, it jumps back by about *F* × *W* / 18 pixels.

**The compass moves when the heading changes.** `Gunsight_SetValues` runs the set-heading slot when offset 0 of the block changes, and `es2_xref.py` finds `HudHeadingTape_SetHeading` in its table alone ([Open](#open)). So the tape holds still while the machine's byte `+0x9e` keeps that offset from being refreshed, and until the offset first differs from the 0 the builder leaves there it shows what the recompute left: the bank's last frame at *x0* and its first beside it. ES2's update slot drives the tape every frame with the negated heading, and through the power-up wind-up ([power-up wind-up](../../retail/simulation/cockpit-gunsight-hud.md#power-up-wind-up)), which ES1 does not have.

## The reticle

**`Gunsight_Reticle_Ctor` (`00033f1c`)**, (child, rect), allocates `0x24` bytes when given none and runs **`GunsightChild_CtorWithFields` (`00033ed8`)**, (child, rect), which adds to `GunsightChild_CtorBase` the dwords 3, 6, `0xa0` and `0x3a` at `+0x14`–`+0x20`; it stores `HUDCrosshairGunsightVtable` (`00093776`) at `+0x10`. The four fields are the reticle's: `+0x14` and `+0x18` the inner and outer ends of its arms, `+0x1c` and `+0x20` its centre, which the builder sets to the reticle point. The target box reads `+0x18` as its corners' length, and the click surface keeps the cursor in `+0x1c`/`+0x20`.

**`Gunsight_ReticlePaint` (`00033f64`)**, (child), inside the canvas push installs the pen {0, 0x38} at the context's `+0x228` and draws four lines with `Raster_DrawLine`, with (*cx*, *cy*) the centre, *a* `+0x14` and *b* `+0x18`: (*cx* + *a*, *cy*)–(*cx* + *b*, *cy*), (*cx* − *b*, *cy*)–(*cx* − *a*, *cy*), (*cx*, *cy* − *b*)–(*cx*, *cy* − *a*) and (*cx*, *cy* + *a*)–(*cx*, *cy* + *b*): a cross with a gap at its middle. **`Gunsight_ReticleRepaint` (`00033f4c`)** calls it. ES2's (`0043b7e0`, [child 4](../../retail/simulation/hud-target-indicator.md#child-4--the-reticle)) blits one of three `HUD` frames by where the target projects and by lock.

## The target box

**`Gunsight_TargetIndicator_Ctor` (`0003403c`)**, (child, rect), allocates `0x38` bytes when given none, runs `GunsightChild_CtorWithFields` and the inlined constructor of the box rect at `+0x28`–`+0x37`, and stores `HUDLockingGunsightVtable` (`0009378a`) at `+0x10`. The builder points `+0x24` at the state block. **`Gunsight_TargetIndicatorRepaint` (`000340a8`)** calls the paint.

**`Gunsight_TargetIndicatorPaint` (`000340c0`)**, (child), draws only while the block has a selection (offset 6) and its byte `0x24` is set. With *r* the radius (offset `0x1c`) and (*x*, *y*, *z*) the position, it installs the identity model transform (`Raster_SetModelTransform`(0), saving the 32 bytes at `g_ModelTransformPtr` to put back at the end), takes the points (*x*, *y*, *z* − *r*/2) and (*x*, *y*, *z* − *r*/2 + 2*r*) to view space with `Raster_ModelToView`, and gives up when either's depth is below `0x100`. It projects both (`Raster_PerspectiveDivide`, `Raster_ProjectToScreen`); with *h* the distance between their screen rows, it moves the two rows apart or together by half the difference, `int16` arithmetic, to make *h* 25 when it is below 25 or 75 when it is above 75. The box (`+0x28`–`+0x34`) is the upper point's x ± *h*/2 across and its row to the lower point's row down, square.

The corners' length across is `+0x18` (6) when the box is wider than that and half its width otherwise, and likewise down with its height; after the clamp both are 6. The colour is 1 when the side byte `+0x14` of the target's group (`+0x3f`, [the group](mission-groups.md)) is 0 and 0x35 otherwise. In the render context installed when it runs (`000bdbda`), with no canvas push, saving and restoring its pen `+0x228` and brush `+0x22c`, it draws:

- **without lock** (block byte `0x25` clear), with the pen {0, colour}, an L at each corner of two `Raster_DrawLine`s along the box's edges, each the corner length;
- **with lock**, with the brush {0, colour}, four filled triangles through `Raster_DrawPolygonDispatch`, each the inner half of a corner's 6 × 6 square: its right angle at the square's inner corner and its other corners on the box's edges, 6 from the box's corner.

ES2's box and its off-screen arrow are [child 5](../../retail/simulation/hud-target-indicator.md#child-5--the-box-and-the-arrow).

**`Gunsight_UnusedChild_Ctor` (`00034710`)**, (child, rect), builds the same `0x38`-byte object with `Gunsight_TargetIndicator_Ctor` and stores `HUDPipperVtable` (`00093802`), whose paint, **`Gunsight_UnusedChild_Paint` (`00034755`)**, is three `NOP`s and `RET 4`; **`Gunsight_UnusedChild_Repaint` (`00034740`)** calls it. ES2's child 6 is the same ([the gunsight complex](../../retail/simulation/cockpit-gunsight-hud.md#front-window-hud--the-gunsight-complex)).

## The waypoint indicator

**`HudWaypointIndicator_Ctor` (`0003475c`)**, (indicator, rect), allocates `0x4f` bytes when given none, runs `HudRotationIndicator_Ctor` and the inlined constructor of the point at `+0x3c`, stores `HNavGaugeVtable` (`00093816`) at `+0x10`, clears the bytes `+0x49` and `+0x4a`, and builds the caption at `+0x4b`: `Label_Ctor` with `ColorSchemePanels`' `cpgreen` (`00092d44`) and `Label_SetRect`(label, 2, rect, {0, 6}), centred in the tape's rect and 6 lower. The table's recompute and set limits are `HudRotationIndicator_Recompute` and `HudSlideBar_SetLimits`; its add-delta slot, **`HudWaypointIndicator_ShiftLimits` (`000347e8`)**, (indicator, delta), adds the delta to both limits, as ES2's (`0043c3d0`) does. **`HudWaypointIndicator_Repaint` (`000347d0`)** calls the paint.

**`Hud_UpdateWaypointIndicator` (`00034800`)**, (indicator), its paint, is ES2's (`0043c3e4`, [waypoint indicators](../../retail/simulation/cockpit-gunsight-hud.md#waypoint-indicators)) for the route alone. It draws nothing unless the cockpit's machine has a group (`+0x3f`) whose route (`+6`, the waypoint list at `+8`) has a waypoint after the cursor: `Route_WaypointAt`(route, cursor + 1) non-null, and the cursor below the list's count, *n* being cursor + 1. Then:

1. It copies the waypoint's point to `+0x3c`, its distance from the machine's point `+0x20` (`Vec2_DistanceBetween`) to `+0x45`, and the bearing `Math_Atan2Guarded`(*dx*, *dy*) − `0x4000` to `+0x3a`.
2. With the error *e* = heading (`+0xa`) − bearing, unsigned 16-bit: `+0x4a` = 1 for *e* from `0xe39` to `0x7fff`, the subject off to the right, `+0x49` = 1 for `0x8000` to `0xf1c7`, off to the left; both clear otherwise.
3. Inside the canvas push, with the brush {0, 1}, it fills a triangle with `Raster_DrawPolygonDispatch`. On the tape, at *x* = *x1* − ((bearing − `+0x24`) & `0xffff`) × `+0x28` / `+0x2c`, unsigned: (*x*, *y0* + 8), (*x* − 2, *y0* + 5), (*x* + 2, *y0* + 5), pointing down. Off to the left: (*x0*, *y0* + 6), (*x0*, *y0* + 10), (*x0* − 2, *y0* + 8); off to the right: (*x1*, *y0* + 6), (*x1* + 2, *y0* + 8), (*x1*, *y0* + 10).
4. It replaces `+0x45` with `Hud_WorldUnitsToMetres` of it and sets the caption to `WP ` (`HudWaypoint_CaptionPrefix`, `00093832`), *n* (`ltoa`), `: ` (`00093836`), the metres and ` M.` (`00093839`) with `Label_SetText`.

The marker's place comes from the lower limit `+0x24`, which the builder sets to −`0xe38` and `Gunsight_SetValues` moves by every change of heading, so (bearing − `+0x24`) is `0xe38` − *e* and the marker reaches *x1* at an error of `0xe38` and *x0* at −`0xe38`. ES2's places its diamond from the error and reads neither limit, so its shift of the limits has no effect. ES2's also draws the nav marker's indicator, its shapes `<< CoordShift` and above the rect, in `COLORS.DAT` colours, and takes its caption's first word from `STRINGS0.STR`.

## The click surface

**`Gunsight_ClickSurface_Ctor` (`000345bc`)**, (child, rect), is ES2's ([the gunsight complex](../../retail/simulation/cockpit-gunsight-hud.md#front-window-hud--the-gunsight-complex), child 0) on ES1's layout: `0x28` bytes, built on `GunsightChild_CtorWithFields`, with `HUDRovingGunsightVtable` (`0009379e`) at `+0x10`. Its `0x20`-byte gadget, kept at `+0x24` and registered with `Widget_RegisterClickable` on the cockpit's clickable list, takes `Widget_CtorRect`'s rect and then `CTLButtonControlVtableGunsightCopy` (`000937b2`) and `HUDRovingGunsightGadgetVtable` (`000937da`) at its `+0x1c`.

**`Gunsight_ClickSurface_Paint` (`000346a4`)**, (child), reads the cursor into `+0x1c`/`+0x20` with `Cursor_GetPosition` (`00066a2e`) and calls `Cockpit_SetForwardCursor` with 1 while it lies in the rect, bounds inclusive, and 0 otherwise, as ES2's (`0043c1dc`). **`Gunsight_ClickSurface_Repaint` (`00034660`)** calls it.

**`Gunsight_ClickGadget_OnClick` (`000337dc`)**, (gadget, flags), is the gadget's click slot (`+0xc`): with bit 0 of flags set, it sets the byte `Gunsight_PickArmed` (`000936d8`) and arms [the pixel pick](terrain.md#the-pixel-pick-and-the-occlusion-probe) with `ObjPick_Arm` at the point `000b16d4`, which the widget input code stores (`00044564`, `0004466d`, `00044755`). ES2's slot, `Gunsight_ClickGadget_OnClickNoOp` (`0043b338`), tests the same bit and returns, and `es2_xref.py` finds no reference to ES2's `ObjPick_Arm` ([ES2's pick](../../retail/rendering/terrain-drawing.md#the-pixel-pick-and-the-occlusion-probe)). The update hands the picked object to the target selection ([below](#painting-and-the-per-frame-update)).

The gadget's other slots are frameless stubs. `CTLButtonControlVtableGunsightCopy` is this file's copy of the button base's table ([`panel.cpp`'s copy](cockpit-panel.md#the-screen-edges-and-the-cursors)), and `HUDRovingGunsightGadgetVtable` shares its last four slots:

| Slot | `000937b2` | `000937da` | ES2's `0049c188`, a slot earlier |
|---|---|---|---|
| `+4` | `CTLControl_PaintNoOpGunsightCopy` (`00034675`), three `NOP`s and `RET 4` | `Gunsight_ClickGadget_PaintNoOp` (`0003469b`), `NOP` and `RET 4` | `0043e611` |
| `+8` | `CTLControl_InvalidateNoOpGunsightCopy` (`0003467b`), `NOP` and `RET 4` | `Gunsight_ClickGadget_InvalidateNoOp` (`0003469f`), `NOP` and `RET 4` | `0043e616` |
| `+0xc` | `CTLControl_OnClickNoOpGunsightCopy` (`0003467f`), `NOP` and `RET 8` | `Gunsight_ClickGadget_OnClick` | `0043b338` |
| `+0x10` | `CTLControl_FlushDeferredPaintNoOpGunsightCopy` (`00034683`), `NOP` and `RET 4` | the same | `0045554a` |
| `+0x14` | `Widget_GetButtonValueGunsightCopy` (`00034687`), `NOP`, the `int16` at `00094b84` in `AX`, `RET 4` | the same | `00455530` |
| `+0x18` | `CTLControl_SetValueNoOpGunsightCopy` (`00034691`), three `NOP`s and `RET 8` | the same | `0045553b` |
| `+0x1c` | `CTLControl_DragMoveNoOpGunsightCopy` (`00034697`), `NOP` and `RET 8` | the same | `00455540` |

## Painting and the per-frame update

`HUDVtable` (`00093842`) is `PanelGaugeVtable`'s shape ([the gauge base](cockpit-widgets.md#the-gauge-base)): `PanelGauge_OnChildClickNoOpGunsightCopy` (`00034e88`, `RET 0xc`) at `+4`, the paint at `+8`, the update at `+0xc` and `PanelGauge_KeyDispatchNoneGunsightCopy` (`00034e8b`, `NOP`, `XOR EAX,EAX`, `RET 8`) at `+0x10`.

**`Gunsight_Paint` (`00034ec4`)**, (gunsight): unless the display is dark (`+0x74`), calls each child's slot `+4`, its paint, and then, while the byte `+0x7c` of the weapon buttons (`Gau_WeaponButtonsWidget`'s instrument, the cockpit's `+0x1dd`) is set, sets the `ATT` label's text to `STRINGS0.STR` group 33's `ATT` (`000bd4a8`, [the string table](../formats/str-strings.md)) with `Label_SetText`, which fills the label's box in its colour 0x38 and writes the word. ES2's (`0043d5c8`, [painting](../../retail/simulation/cockpit-gunsight-hud.md#painting)) also tests that child 0's rect is on screen, pushes the canvas context, blits a plate for `ATT`, sets four readout labels' text and calls `HudClock_Tick`, `Hud_UpdateSpeedReadout`, `Gunsight_PaintHudScanner` and `Gunsight_PaintAltitudeScale`, none of which ES1's calls.

**`Gunsight_UpdateAndPaint` (`00034f3c`)**, (gunsight), returns at once while the view manager (`CockpitViewManager_Published`) is in view 4 (`+0x10`). Otherwise it stores `Player_DependentCondition`(4) at `+0x68` and runs `PanelGauge_TickDropout` when that is non-zero ([sensor dropout](cockpit-widgets.md#sensor-dropout)); then, unless the display is dark or `CockpitView_RectCornerVisible` answers 0 for child 0's rect:

1. While `Gunsight_PickArmed` is set and `ObjPick_IsIdle` answers non-zero, it passes the cockpit (`+0x10`) and a non-zero `ObjPick_GetObject` to `TargetSelect_SetObject`, and clears `Gunsight_PickArmed`.
2. It stores the cockpit's selection (`+0x20c`) at block offset 6 and, when there is one, its position, radius, 2000 and heading ([the state block](#the-state-block)).
3. It calls each child's slot `+8`, its repaint, and sets the `ATT` text as the paint does.

ES2's (`0043d6dc`) also runs in view 4 while the view manager's byte `+0x1c` is set, sets its dropout ranges from tables of its own, pushes the canvas context, takes the target's aim point from the cockpit's target block, drives the heading tape, and draws what its paint draws.

**`Gunsight_ShiftChildRects` (`0003518c`)**, (gunsight, delta), subtracts the delta's x from *x0* and *x1* and its y from *y0* and *y1* of the rects of children 1, 2, 3, 4 and 7, as ES2's (`0043db98`). `es2_xref.py` finds no reference to it or its padding ([Open](#open)).

## Functions

| ES1 | Address | ES2 |
|---|---|---|
| `HeapPtr_DtorGunsightCopy`, `ConfigPart_DtorGunsightCopy`, `ConfigPart_GetClassTagGunsightCopy`, `TSPartBase_DtorGunsightCopy` | `00033740`, `00033768`, `000337a7`, `000337b0` | the run; slots `+0x1c` and `+4` of `00093662` and `+0x1c` of `00093696` |
| `Gunsight_ClickGadget_OnClick`, `Gunsight_ClickSurface_Ctor` | `000337dc`, `000345bc` | `0043b338`, `0043c120` |
| `GunsightChild_CtorBase`, `GunsightChild_CtorWithFields` | `000337fc`, `00033ed8` | `0043b344`; `00033ed8` none |
| `HudSlideBar_CtorBase`, `HudSlideBar_SetLimits`, `HudSlideBar_AddDelta` | `0003385c`, `000338cc`, `00033910` | `0043b378`, `0043b3c0`, `0043b3f8` |
| `HudRotationIndicator_Ctor`, `HudRotationIndicator_Repaint`, `HudRotationIndicator_Recompute`, `HudRotationIndicator_Paint` | `00033968`, `000339c4`, `000339dc`, `00033a04` | `0043b438`, `0043e685`, `0043b460`, `0043b4a4` |
| `HudSlideBarV_Ctor`, `HudSlideBarV_Repaint`, `HudSlideBarV_Recompute`, `HudSlideBarV_Paint` | `00033ac4`, `00033b20`, `00033b38`, `00033b60` | `0043b54c`, `0043e677`, `0043b56c`, `0043b574` |
| `HudHeadingTape_Ctor`, `HudHeadingTape_Recompute`, `HudHeadingTape_SetHeading`, `HudHeadingTape_Paint`, `HudHeadingTape_Repaint`, `HudHeadingTape_SetLimits` | `00033c20`, `00033c7c`, `00033d08`, `00033dd4`, `00033e94`, `00033eac` | `0043b57c`, `0043b5dc`, `0043b654`, `0043b6dc`, `0043b7a4`, `0043b7b4` |
| `Gunsight_Reticle_Ctor`, `Gunsight_ReticleRepaint`, `Gunsight_ReticlePaint` | `00033f1c`, `00033f4c`, `00033f64` | built inline; `0043e5e7`, `0043b7e0` |
| `Gunsight_TargetIndicator_Ctor`, `Gunsight_TargetIndicatorRepaint`, `Gunsight_TargetIndicatorPaint` | `0003403c`, `000340a8`, `000340c0` | `0043b928`, `0043e629`, `0043b950` |
| `Point_DefaultCtorGunsightCopy`, the element constructor of the target box's and the waypoint indicator's point arrays | `000345a0` | the code of `Point_DefaultCtor` (`0007ca28`) |
| `Gunsight_ClickSurface_Repaint`, `Gunsight_ClickSurface_Paint` | `00034660`, `000346a4` | `0043e61b`, `0043c1dc` |
| the click gadget's stubs | `00034675`–`0003469f` | [the table](#the-click-surface) |
| `Gunsight_UnusedChild_Ctor`, `Gunsight_UnusedChild_Repaint`, `Gunsight_UnusedChild_Paint` | `00034710`, `00034740`, `00034755` | `0043c240`, `0043e603`, `0043c260` |
| `HudWaypointIndicator_Ctor`, `HudWaypointIndicator_Repaint`, `HudWaypointIndicator_ShiftLimits`, `Hud_UpdateWaypointIndicator` | `0003475c`, `000347d0`, `000347e8`, `00034800` | `0043c268`, `0043e5f5`, `0043c3d0`, `0043c3e4` |
| `Gau_RovingGunsightWidget`, `PanelGauge_OnChildClickNoOpGunsightCopy`, `PanelGauge_KeyDispatchNoneGunsightCopy` | `00034aec`, `00034e88`, `00034e8b` | `0043c7d8`; the code of `00031395` and `0003139b` |
| `Gunsight_AddChild`, `Gunsight_Paint`, `Gunsight_UpdateAndPaint` | `00034e94`, `00034ec4`, `00034f3c` | `0043d5a4`, `0043d5c8`, `0043d6dc` |
| `Gunsight_SetValues`, `Gunsight_GetStateBlock`, `Gunsight_ShiftChildRects` | `00035090`, `00035180`, `0003518c` | `0043d98c`, `0043db88`, `0043db98` |

The callees read for the builder, the paint and the waypoint indicator, outside this file: `Input_GetMouseDevice` (`0004b340`) and `Input_GetMouseDevice_Thunk` (`0004c634`), ES2's `0045bf4c` and `0045bd70`; `Vec2_DistanceBetween` (`000645c8`), (a, b): `Math_FastMagnitude2D` of b less a, ES2's `0047d588`; `Cursor_SyncPosition` (`000669dc`) and `Cursor_GetPosition` (`00066a2e`), ES2's `00486d70` and `00486dc2`, which `GO.EXE` has too, at `ES1GO 00050ed4` and `00050f26`, the same code with its own globals.

## Globals

| | Address |
|---|---|
| `HudHSlidBank`, `HudVSlidBank`, `HudHTickBank` | `000936c8`, `000936cc`, `000936d0` |
| `Gunsight_PickArmed`, a byte | `000936d8` |
| `RotationGaugeVtable`, `HFixRotationGaugeVtable`, `VFixRotationGaugeVtable`, `HRevRotationGaugeVtable` | `000936de`, `000936fe`, `00093726`, `0009374e` |
| `HUDCrosshairGunsightVtable`, `HUDLockingGunsightVtable`, `HUDPipperVtable`, `HNavGaugeVtable` | `00093776`, `0009378a`, `00093802`, `00093816` |
| `HUDRovingGunsightVtable`, `CTLButtonControlVtableGunsightCopy`, `HUDRovingGunsightGadgetVtable` | `0009379e`, `000937b2`, `000937da` |
| `HudWaypoint_CaptionPrefix`, `WP `, then `: ` and ` M.` | `00093832`, `00093836`, `00093839` |
| `HUDVtable` | `00093842` |

## Open

- **Open:** `000155a8`, the caller of `Gunsight_SetValues` that fills the block's first three values, read only around the call; what the machine's `+0x22e` and `+0x230` are (ES2's turret twist and pitch are its `+0x298` and `+0x29a`), and what its bytes `+0x95` and `+0x9e` mean (ES2's missile lock `+0x9b` and immobilised byte `+0xa4` sit 6 higher).
- **Open:** `00055bc2`, which the builder passes {2*p*.x, *p*.y, 0} and 1, and `00055c6f`, which it calls when the mouse flag is set and which on its way in stores the point's x and y at `000962e8` and `000962ec` and the third value at `000962f0`: read in part, where ES2's builder calls a stub.
- **Open:** the art of `hudhslid`, `hudvslid` and `hudhtick`: what frames 1 and 2 of the slide bars show, and whether the heading tape's strip is drawn so that its 19/18 travel and the jump at north do not show. What the point `000b16d4` holds is in [ES1's pick](terrain.md#open).
- **Open:** which render context is installed when `Gunsight_UpdateAndPaint` runs, which the target box draws through; ES2's box draws through the canopy's ([why the box goes behind the cockpit frame](../../retail/simulation/hud-target-indicator.md#why-the-box-goes-behind-the-cockpit-frame)).
- **Open:** what the target's side colours 1 and 0x35 look like in each theater's palette, and which side is 0.
- **Open:** what the weapon buttons' byte `+0x7c` is, which shows the `ATT` legend; ES2's legend follows the console buttons' auto-track latch ([the `ATT` legend](../../retail/simulation/cockpit-gunsight-hud.md#the-att-legend)).
- **Open:** an `ES1GO` copy of this file's functions: `es2_naming.py match ES1DBSIM ES1GO` pairs none exactly but `Point_DefaultCtorGunsightCopy`, with ten point constructors, held as a module copy.
- **Open:** a call of the heading tape's set-heading slot other than `Gunsight_SetValues`'s: `es2_xref.py` finds `HudHeadingTape_SetHeading` in `HRevRotationGaugeVtable` alone, and a call through slot `+0x10` of another child array would not show in it.
- **Open:** a caller of `Gunsight_ShiftChildRects`: `es2_xref.py` finds no branch or stored pointer to it or its padding, as for ES2's.
- **Open:** a positive control in the running game for the done checklist ([when a module is done](../../herculan/plan-es1-support.md#when-a-module-is-done)). Proposed: in DOSBox-X's debugger, `BP <CS>:000337dc` fires on a left click over the gunsight surface with bit 0 of the dword at `[ESP+8]` set, and `BP <CS>:00033b60` fires as the cockpit draws, showing that the vertical bar is painted; and in play, a turn of the HERC moves the compass, the waypoint caption reads `WP`, and a locked target's box shows filled corners.
