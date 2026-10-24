# DBSIM.EXE — the throttle gauge

**ES1's throttle is ES2's throttle gauge ([ES2's](../../retail/simulation/cockpit-hud-widgets.md#throttle-gauge)): a knob on a track that shows the throttle, follows the keys and the stick, and sets the throttle when the player drags it.** What a player would see differently:

- two bars beside the track fill with the speed the HERC has reached, the upper one as it walks forward and the lower one as it backs up, where ES2's builds its bars and never draws them;
- the Raptor's throttle runs across the panel, its forward bar right of the middle and its reverse bar left of it; the other seven chassis' run up, as all of ES2's do;
- the knob is a frame of the console buttons' bank, and no centre tick is drawn;
- dragging the knob leaves the throttle lever mode as it is, where ES2's sets it from the knob's side of the middle when a joystick throttle is fitted;
- on the Raptor, a full redraw of the panel, as after a view change, leaves the lit part of the reverse bar undrawn ([the bars](#the-bars)).

Addresses are `ES1DBSIM` image addresses ([the X-32 executable](../formats/x32-executable.md#the-image)); ES2's are DBSIM's. Every function here removes its own arguments, so argument lists are in stack order ([calling conventions](../formats/compiler.md#calling-conventions)). The gauge is the cockpit's instrument `+0x1ed`, built from the `.GAU` block at 1000 ([the throttle block](../formats/gau-cockpit-layout.md#the-throttle-block-at-1000)).

## Where the object file starts and ends

The file's code is `0003b0ac`–`0003c18b` and its tables `00094206`–`0009441d`. It opens with a lone copy of the 40-byte destructor, `HeapPtr_DtorThrottleGaugeCopy` (`0003b0ac`), after the previous file's `RET 8` at `0003b0a7` and two zero bytes; it ends with `ThrottleSlider_FlushPaintH`'s `RET 4` at `0003c186` and three zero bytes, before the run of `ConfigPart`'s copies at `0003c18c` that opens the next file ([where object files meet](../formats/compiler.md#where-object-files-meet-in-the-code)). It has no static initializer. The `C2` bytes at `0003b67c` and `0003bd94`, which read as a `RET 0xce9` followed by a zero byte, are the −`0x3e` operands of the this-adjusting thunks at `0003b678` and `0003bd90`.

The tables before `00094206`, from `000941e2`, are the slots of `MFDListGadgetVtable` (`000941de`), the previous file's ([the MFD's click surface](mfd-display.md#the-click-surface)).

## The gauge — `ThrottleGauge_Ctor` (`0003b0d4`)

**The gauge is ES2's with its fields lower, no centre tick and no bank of its own; it is `0x8b` bytes.**

| ES1 | ES2 | Holds |
|---|---|---|
| `+0x00`–`+0x75` | its base's | the [gauge base](cockpit-widgets.md#the-gauge-base) |
| `+0x14` | `+0x18` | the slider, its one child |
| `+0x64` | `+0x68` | the child count, 1 |
| `+0x76` | `+0x00` | the vptr, `ThrottleGaugeVtable` (`00094206`) |
| `+0x7a` | `+0xb1` | the speed fraction, `int32` |
| `+0x7e` | `+0xb5` | the throttle, `int32` |
| `+0x8a` | `+0xc1` | 0 for the vertical slider, 1 for the horizontal |
| none | `+0xc2` | the centre tick's position |

`ThrottleGauge_Ctor` (`0003b0d4`), (gauge, cockpit, block), from `Gau_ThrottleWidget` (`0002cdf0`) with the block at 1000:

1. allocates `0x8b` bytes when given none, returning 0 when that fails, and runs `PanelGauge_Ctor`(gauge, cockpit, block), storing `ThrottleGaugeVtable` at `+0x76`;
2. the track is the block's `int32` 4–7 plus the origin, `int32` 0 and 1;
3. with `int32` `0x10` equal to 1, `ThrottleSlider_CtorV`(0, block, gauge, track) at `+0x14` and `+0x8a` = 0; otherwise `ThrottleSlider_CtorFixed`, the same arguments, and `+0x8a` = 1 ([the slider](#the-slider));
4. `Widget_RegisterClickable`(the slider, the cockpit's root widget, `CockpitViewInstance`'s `+0`), so the root's hit test reaches the slider ([hit testing](cockpit-mouse.md#hit-testing));
5. the count `+0x64` = 1, and `+0x7a` and `+0x7e` = 0.

ES2's (`00447b84`) also loads the bank `throttle` the first time, builds its slider with `Mem_New` in an exception frame, and places the centre tick from the knob's rect and the block's `int32` `0x12`.

`ThrottleGaugeVtable` (`00094206`) is ES2's (`0049d1e0`) one slot further on ([vtables](../formats/compiler.md#vtables)):

| Slot | ES1 | Does | ES2's |
|---|---|---|---|
| `+4` | `ThrottleGauge_OnChildValue` (`0003b25c`, by its padding `0003b25a`) | (gauge, value, child): when the child is the first of the `+0x64` at `+0x14`, `+0x7e` = the value, negated when `+0x8a` is 0; for another child or none, nothing | `00447de0`, (gauge, child, value), returning the child's index |
| `+8` | `ThrottleGauge_Paint` (`0003b1d0`, by its padding `0003b1cd`) | (gauge): the slider's paint, slot `+4` of its table at `+0x1c` | `00447d38`, which also blits the centre tick, frame 0 of `throttle` |
| `+0xc` | `ThrottleGauge_Update` (`0003b1e8`) | (gauge): the slider's per-frame slot `+0x10` | `00447d6c` |
| `+0x10` | `PanelGauge_KeyDispatchNoneThrottleGaugeCopy` (`0003b1c5`) | three `NOP`s, `XOR EAX,EAX` and `RET 8`: takes no key | `PanelGauge_KeyDispatchNone` (`00452344`) |

The cockpit calls them as it does every instrument's ([drawing it](cockpit-panel.md#drawing-it), [keys](cockpit-panel.md#keys-and-the-view-device)): the paint from `CockpitView_InvalidateWidgets` on a view change, between the canvas push and pop; the per-frame slot from `Cockpit_DrawFrame` step 5, between the push and the pop, unless the widgets are off; and the key slot in the chain of `CockpitWidgets_HandleCommand`, which goes on past it to `+0x1f1` and `+0x1e9`. The gauge has no slot for a view change: `CockpitWidgets_TranslateForView` moves the root widget's rect, which the slider is hit-tested from, and the canvas context's translation `+0x220`/`+0x224`, which the slider's paints add to the knob's screen rect.

## The exchange with the throttle

**Each frame the throttle the HERC has and the throttle the knob shows are made one, whichever changed last winning, and the gauge is handed the speed reached as a fraction of top speed.** It is ES2's exchange ([live values and the two-way binding](../../retail/simulation/cockpit-hud-widgets.md#live-values-and-the-two-way-binding)), in step 2 of `Player_PerFrameCockpitUpdate` (`000155a8`, [the HERC's cockpit](mech.md#the-cockpit)) on the machine's fields ([the fields](mech.md#the-fields)):

1. `ThrottleGauge_GetValues` (`0003b250`), (gauge): frameless, returns the gauge plus `0x7a`, the pair {speed fraction, throttle}; ES2's (`00447dd0`) `+0xb1`. It is the same bytes as `Gunsight_GetStateBlock` (`00035180`).
2. With the machine's byte `+0x8d` clear, the throttle `+0x226` = the low 16 bits of the pair's second dword, the gauge's `+0x7e`. With it set, the pair's throttle becomes `+0x226` and `+0x8d` is cleared. `+0x8d` is set on a tick a throttle control steps the throttle ([the throttle](mech-locomotion.md#the-throttle--mech_applythrottleinput-00011afc)), and by the all-stop ([the HERC's commands](mech.md#the-cockpit), [the player's controls](main-loop.md#the-players-controls--sim_pollplayerinput-0005a0c0)).
3. The speed fraction is (`+0x224` ≪ 10) / *top*, *top* being the type record's (`+0x188`) forward top speed `+0x06` with the speed `+0x224` at 0 or more and the negated reverse top speed `+0x04`, which the chassis file holds negative ([the chassis file](../formats/chassis-dat.md#per-chassis-values)), with it below 0: a Q10 fraction, `0x400` at forward top speed and −`0x400` at reverse top speed, truncated toward zero and not clamped.
4. `ThrottleGauge_SetValues` (`0003b200`), (gauge, pair): when the pair's throttle differs from `+0x7e`, the slider's set-value slot `+0x18` with it, which places the knob and calls the slider's click slot with it, so `ThrottleGauge_OnChildValue` runs ([dragging the knob](#dragging-the-knob)); when the speed fraction differs from `+0x7a`, the slider's paint counter `+0x1b` = 2; then `+0x7a` and `+0x7e` = the pair. ES2's (`00447d80`) passes the throttle as an `int16` and sets ES2's `+0x1f`.

The gauge starts at 0, so on the frame `Sim_StartMission` runs the update the machine's throttle becomes 0 unless `+0x8d` is set.

## The slider

**The slider is ES2's vertical or horizontal throttle slider ([slider geometry](../../retail/simulation/cockpit-hud-widgets.md#slider-geometry)) on ES1's [slider base](cockpit-mouse.md#the-slider-base), with the two bars as bitmap bar graphs and its knob from the console buttons' bank.** It is `0x86` bytes, as ES2's:

| Offset | Holds |
|---|---|
| `+0x00`–`+0x3d` | the slider base: the track's rect `+0`, the paint counter `+0x1b`, the vptr `+0x1c`, the scale `+0x20`, the `int16` minimum −`0x400` `+0x24`, maximum `0x400` `+0x26`, position `+0x28`, previous position `+0x2a` and knob length `+0x2c`, the knob's rect `+0x2e`–`+0x3d` |
| `+0x3e`, `+0x42` | the owner, the gauge, and the mixin's vptr ([the gadget bases](cockpit-widgets.md#the-gadget-bases)) |
| `+0x4a` | the speed fraction the bars were last painted to |
| `+0x4e` | the knob's bitmap |
| `+0x52` | the buffer for the screen under the knob, the knob's rect's `Region_PixelCount` bytes |
| `+0x56` | a `GLRectangleRegion`: its vptr, the type 0 at `+0x5a` and the rect `+0x5e`–`+0x6d`, the knob's rect on screen |
| `+0x6e`, `+0x72` | the knob's top-left as last drawn, where the saved screen goes back |
| `+0x76` | the bars' bitmap array |
| `+0x7a` | the speed fraction, copied from the gauge |
| `+0x7e`, `+0x82` | the forward and the reverse bar |

### Building it

**`ThrottleSlider_CtorV` (`0003b2c8`)** and **`ThrottleSlider_CtorFixed` (`0003ba74`)**, (slider, block, gauge, track), from `ThrottleGauge_Ctor`:

1. allocate `0x86` bytes when given none, returning 0 when that fails; `SliderWidget_CtorBase`(slider, 1, 0, `0x100`, 0, track) with this file's copy of the base table, `00094222` (vertical) or `0009438a` (horizontal), and `SliderWidget_RecomputeScaleV` or `H`; the owner at `+0x3e`; the region at `+0x56` with this file's copies of its four tables in turn, `00094266`, `0009428a`, `000942b2` and `000942da`, and 0 at `+0x5a`; the mixin's tables `0009424e` and `00094302` and the primary `0009431a` or `000943b6` in turn, the `int16` state `+0x17` = 0, and last the mixin's `00094346` or `000943e2` and the primary `ThrottleVSliderGadgetVtable` (`0009435e`) or `ThrottleHSliderGadgetVtable` (`000943fa`);
2. `+0x76` = `ClassItem_LoadFromFile`(`ResourcePath_BuildFolderName`(`dba`, the light name the block's `int32` `0x10` indexes in the table at `0009328c`)): `dba\THR_LED1` for the vertical sliders and `dba\THR_LED2` for the `RAPTOR`'s, the only values the retail files hold ([the throttle block](../formats/gau-cockpit-layout.md#the-throttle-block-at-1000));
3. the knob's bitmap `+0x4e` is the frame the block's `int32` `0x11` names in `CockpitPButtonsBank` (`dba\pbuttons`, [building the cockpit](cockpit-panel.md#building-it)), 27 in every retail file;
4. the knob, from the track {*x0*, *y0*, *x1*, *y1*}:
   - vertical: the length `+0x2c` is the knob bitmap's rows, its `int16` `+4`; the knob spans the track's width, its bottom row ((*y1* − *y0*) ≫ 1 below *y1*) + (length ≫ 1) and its top row that less the length;
   - horizontal: the length is 12 whatever the bitmap; the knob spans the track's rows, its left column *x0* + ((*x1* − *x0*) ≫ 1) − 6 and its right that plus 12;
   - the region's rect and `+0x6e`/`+0x72` take the knob's rect and top-left, and `+0x52` is allocated with `operator_new`;
5. the bars: the forward bar `+0x7e` over the block's `int32` 8–11 plus the origin, range `0x400`, and the reverse bar `+0x82` over `int32` 12–15, range −`0x400`, each `LedBmBarGraphV_Ctor` or `LedBmBarGraphH_Ctor` ([bitmap bar graphs](cockpit-widgets.md#bitmap-bar-graphs)) with frame 1 of `+0x76` as the lit picture and frame 0 (forward) or 2 (reverse) as the unlit one;
6. `+0x4a` and `+0x7a` = 0; the position `+0x28` and `+0x2a` = the knob's bottom row (vertical) or left column (horizontal); the minimum −`0x400`, the maximum `0x400`; and `SliderWidget_RecomputeScaleV` or `H` again, which makes the scale ((*y1* − length − *y0*) ≪ 16) / `0x800`, or the same across.

ES2's `ThrottleSlider_CtorV` (`00447e24`) takes its knob from frame 1 of the bank `throttle` and builds its bars as plain `BarGraphV`s; its `ThrottleSlider_CtorFixed` (`004483c0`) places the horizontal knob as ES1's does, with that bank's bitmap.

| ES1 table | Address | Slots from `+4` |
|---|---|---|
| `ThrottleVSliderGadgetVtable` | `0009435e` | paint `ThrottleSlider_DrawKnobV` (`0003b690`, by its padding `0003b68e`), invalidate `VSliderGadget_InvalidateDeferred` (`0003b668`, by its padding `0003b666`), click `VSliderGadget_OnClick` (`000316a0`), per-frame `ThrottleSlider_PaintV` (`0003b804`, by its padding `0003b803`), then `SliderWidget_GetValueV`, `SliderWidget_SetValueV`, `SliderWidget_DragToPointV` and `SliderWidget_RecomputeScaleV` |
| `ThrottleVSliderGadget_PanelSliderGadgetVtable` | `00094346` | `ThrottleVSliderGadget_DrawKnobVThunk` (`0003b678`) and `ThrottleVSliderGadget_PaintVThunk` (`0003b684`), each adding −`0x3e` to the object and jumping to the paint or the per-frame slot by its padding; `Widget_NoClickSound`'s `RET 4` (`00031668`) |
| `ThrottleHSliderGadgetVtable` | `000943fa` | paint `ThrottleSlider_PaintH` (`0003bda8`, by its padding `0003bda6`), invalidate `HSliderGadget_InvalidateDeferredThrottleGaugeCopy` (`0003bd80`, by its padding `0003bd7d`), click `HSliderGadget_OnClick` (`0003166c`), per-frame `ThrottleSlider_FlushPaintH` (`0003bf18`), then the four `SliderWidget_*H` slots |
| `ThrottleHSliderGadget_PanelSliderGadgetVtable` | `000943e2` | `ThrottleHSliderGadget_PaintHThunk` (`0003bd90`) and `ThrottleHSliderGadget_FlushPaintHThunk` (`0003bd9c`), likewise; `Widget_NoClickSound`'s `RET 4` |

The click slot is the gadget base's, so a click reaches the gauge through `ThrottleGauge_OnChildValue`, invalidates the slider and plays no sound ([the gadget bases](cockpit-widgets.md#the-gadget-bases)). ES2's vertical slider's click slot is `ThrottleSlider_OnValue` (`00448378`), which also sets `ThrottleLeverMode`.

### Drawing it

**The knob is drawn over a saved copy of the screen under it, which goes back when it moves; the bars are repainted when the speed fraction changes.** Each paint works only when `CockpitView_RectCornerVisible` passes the track's rect ([drawing it](cockpit-panel.md#drawing-it)).

**`ThrottleSlider_DrawKnobV` (`0003b690`)**, (slider), slot `+4`, the full paint:

1. each bar: its reset (slot `+0xc` of its table at `+0x28`), its paint to its full value (`0x400` or −`0x400`) and its paint to the slider's `+0x7a`, so the whole bar is drawn, lit from its filling edge to the value and unlit beyond;
2. the knob's rect from the position: rows (position − length) to position;
3. the display's (`000bdd28`) source page `+0x38` = its draw page `+0x3c`, through `00098dd6` and the raster slot `0009df14` when it is the current display;
4. the region's rect = the knob's rect plus the canvas translation, the render context's (`000bdbda`) `+0x220`/`+0x224`; `Region_Save`(region `+0x5a`, buffer `+0x52`);
5. `Bitmap_Blit`(knob `+0x4e`, 0, the knob's top-left `+0x2e`), and `+0x6e`/`+0x72` = that top-left.

ES2's `ThrottleSlider_DrawKnobV` (`004480a0`) is steps 2–5, its blit with flags 2 when the knob's *x0* is past column 160 ≪ `VideoMode_XCoordShift`.

**`ThrottleSlider_PaintV` (`0003b804`)**, (slider), slot `+0x10`, once a frame through `ThrottleGauge_Update`; it acts only while the paint counter `+0x1b` is 2, and stays at 2 while the track is not visible:

1. `Region_Restore`(region `+0x5a`, `+0x6e`, buffer `+0x52`) puts back the screen under the knob as last drawn;
2. `+0x7a` = the gauge's `+0x7a`; when that differs from `+0x4a`, both bars' paint to it ([bitmap bar graphs](cockpit-widgets.md#bitmap-bar-graphs): each draws only the strip between its old and new fill), and `+0x4a` = it;
3. the rect to copy: columns from the lesser of the forward bar's *x0* and the knob's *x0* to the greater of the knob's *x1* and the forward bar's *x1*; rows from the lesser of the old knob's top and the forward bar's start to the greater of the old knob's bottom and the reverse bar's end, widened to the new knob's rows. The bar's columns come from its rect through `BarGraph_GetRect`, whose *x0* and *x1* a vertical bar's paints never write, and its rows from the bars' start `+0x14` and end `+0x18`, not from their rects, which hold only the last strip painted ([bitmap bar graphs](cockpit-widgets.md#bitmap-bar-graphs)). The rect is the knob's own coordinates, without the canvas translation the save adds;
4. steps 2–5 of the full paint;
5. `Widget_DrawToCockpit`(0, the rect, 1) ([drawing it](cockpit-panel.md#drawing-it)), and `+0x1b` = 0.

ES2's `ThrottleSlider_PaintV` (`0044819c`) stores the speed fraction at `+0x7a` and `+0x4a` and paints no bar.

**`ThrottleSlider_PaintH` (`0003bda8`)** and **`ThrottleSlider_FlushPaintH` (`0003bf18`)** are the two above on the x axis: the knob's columns are position to position + 12; the copied rect's columns run from the lesser of the reverse bar's start and the old knob's *x0* to the greater of the old knob's *x1* and the forward bar's end, widened to the new knob's, and its rows from the lesser of the forward bar's *y0* and the knob's *y0* to the greater of the knob's *y1* and the forward bar's *y1*, the bar's rows from its rect, which a horizontal bar's paints never write across. `ThrottleSlider_PaintH`'s first step resets the forward bar twice, once before each bar's pair of paints, and the reverse bar not at all ([the bars](#the-bars)). ES2's (`00448638`, `00448710`) paint no bar.

`VSliderGadget_InvalidateDeferred` (`0003b668`) and `HSliderGadget_InvalidateDeferredThrottleGaugeCopy` (`0003bd80`), (slider), set the paint counter `+0x1b` to 2, as ES2's `VSliderGadget_InvalidateDeferred` (`00448b30`) sets its `+0x1f`.

### Dragging the knob

**A press on the track jumps the knob to the pointer, the knob follows the pointer while the button is held, and the throttle takes the knob's value when the button is let go.** The press, the drag and the release are ES2's ([press, release, click vs. drag](../../retail/simulation/cockpit-input.md#7-press-release-click-vs-drag)) through ES1's [press, release and drag](cockpit-mouse.md#press-release-and-drag) and [the frame's mouse events](cockpit-mouse.md#the-frames-mouse-events--cockpitmouse_processqueue-000444d0); the slider's drag flag `+0x19` is 1 from `SliderWidget_CtorBase`.

1. The press and each move with the drag captured call the drag slot, `SliderWidget_DragToPointV` or `H`, with the point less the root's origin: the position becomes the point's *y*, held to *y0* + length … *y1*, the knob's bottom row, or its *x*, held to *x0* … *x1* − length, the knob's left column; the slider is repainted (`Widget_Repaint`, its invalidate slot), so the next frame's per-frame slot draws the knob there.
2. The release, with the drag captured, calls the click slot with the value slot's `int16` answer, sign-extended ([the frame's mouse events](cockpit-mouse.md#the-frames-mouse-events--cockpitmouse_processqueue-000444d0), step 5), `SliderWidget_GetValueV` or `H`, read from the knob's rect as last drawn: ((*y0*_knob − *y0*) ≪ 16) / scale − `0x400` vertically, which is −`0x400` with the knob at the top, and ((*x0*_knob − *x0*) ≪ 16) / scale − `0x400` across, `0x400` with the knob at the right.
3. The click slot, `VSliderGadget_OnClick` or `HSliderGadget_OnClick`, calls `ThrottleGauge_OnChildValue`(gauge, value, slider), which stores the value at `+0x7e`, negated for the vertical slider, so up and right are forward; invalidates the slider; and calls the mixin's click sound, `Widget_NoClickSound`.
4. The next `Player_PerFrameCockpitUpdate` with `+0x8d` clear makes it the machine's throttle ([the exchange](#the-exchange-with-the-throttle)).

The set-value slot runs the same click when the exchange hands the gauge the machine's throttle, held to ±`0x400` first: `SliderWidget_SetValueV` puts the knob's bottom at *y1* − ((throttle + `0x400`) × scale ≫ 16), `SliderWidget_SetValueH` its left at *x0* + the same ([the slider base](cockpit-mouse.md#the-slider-base)), and `ThrottleGauge_OnChildValue` stores the held throttle (negated for the vertical slider) before `ThrottleGauge_SetValues` stores the pair's throttle, unnegated and unheld, over it.

The value reaches the throttle with any sign: this file stores nothing to `ThrottleLeverMode` (`000919ce`), and the clamp to its range is `Mech_ApplyThrottleInput`'s, on a tick a throttle control steps the throttle ([the throttle](mech-locomotion.md#the-throttle--mech_applythrottleinput-00011afc)).

## The bars

**The forward bar fills from the track's middle toward the forward end as the HERC's speed nears its forward top speed, and the reverse bar from the middle toward the other end as it nears its reverse top speed; at most one of them is lit.** Each bar is a bitmap bar graph ([bitmap bar graphs](cockpit-widgets.md#bitmap-bar-graphs)) drawn from the pictures in `dba\THR_LED1` (vertical) or `dba\THR_LED2` (the `RAPTOR`): frame 1 is both bars' lit picture, frame 0 the forward bar's unlit one and frame 2 the reverse bar's. The value both are painted to is the speed fraction ([the exchange](#the-exchange-with-the-throttle)), read as an `int16` and clamped to the bar's ends:

- vertical: the forward bar, range `0x400`, fills from its bottom row upward; the reverse bar, range −`0x400`, from its top row downward; a positive fraction leaves the reverse bar empty and a negative one the forward bar;
- horizontal: the forward bar fills from its left column rightward and the reverse bar from its right column leftward.

`ThrottleSlider_PaintH` leaves the reverse bar's stored fill position as it was before painting it full and back to the value, so with the HERC backing up it draws that bar's unlit part alone; its lit part is drawn again only strip by strip as the speed changes. The vertical paint resets both bars.

## What the player sees

The panel's art under the track is the cockpit's; the gauge draws the knob, and the bars beside or along it. The knob is drawn in full and the bars rebuilt when the view changes (the paint), and redrawn each frame its paint counter is 2 (the per-frame slot): after the speed fraction changes, after the exchange moves the knob to a throttle the keys or the stick set, and after each move of a drag. A frame with none of these draws nothing.

## Functions

| ES1 | Address | ES2 |
|---|---|---|
| `HeapPtr_DtorThrottleGaugeCopy` | `0003b0ac` | the lone copy of the 40-byte destructor ([module copies](../formats/compiler.md#vtables)) |
| `ThrottleGauge_Ctor`, `PanelGauge_KeyDispatchNoneThrottleGaugeCopy`, `ThrottleGauge_Paint`, `ThrottleGauge_Update` | `0003b0d4`, `0003b1c5`, `0003b1d0`, `0003b1e8` | `00447b84`, `00452344`, `00447d38`, `00447d6c` |
| `ThrottleGauge_SetValues`, `ThrottleGauge_GetValues`, `ThrottleGauge_OnChildValue` | `0003b200`, `0003b250`, `0003b25c` | `00447d80`, `00447dd0`, `00447de0` |
| `ThrottleSlider_CtorV` | `0003b2c8` | `00447e24` |
| `CTLSliderControl_PaintNoOpThrottleGaugeCopy`, `CTLSliderControl_InvalidateNoOpThrottleGaugeCopy`, `CTLControl_OnClickNoOpThrottleGaugeCopy`, `CTLControl_FlushDeferredPaintNoOpThrottleGaugeCopy` | `0003b5d9`, `0003b5df`, `0003b5e3`, `0003b5e7` | slots `+4`–`+0x10` of `00094222` and `0009438a`; `0045571c`, `00455721`, `0045555e`, `0045554a` |
| `GLBase_DtorThrottleGaugeCopy`, `GLRegion_DtorThrottleGaugeCopy`, `GLRectangleRegion_DtorThrottleGaugeCopy` | `0003b5ec`, `0003b610`, `0003b638` | slot `+0x1c` of `0009428a`, `000942b2` and `000942da`; the code of [the bar graph file's copies](cockpit-widgets.md#bitmap-bar-graphs) |
| `VSliderGadget_InvalidateDeferred`, `ThrottleVSliderGadget_DrawKnobVThunk`, `ThrottleVSliderGadget_PaintVThunk` | `0003b668`, `0003b678`, `0003b684` | `00448b30`, `00448b50`, `00448b46` |
| `ThrottleSlider_DrawKnobV`, `ThrottleSlider_PaintV` | `0003b690`, `0003b804` | `004480a0`, `0044819c` |
| `ThrottleSlider_CtorFixed` | `0003ba74` | `004483c0` |
| `HSliderGadget_InvalidateDeferredThrottleGaugeCopy`, `ThrottleHSliderGadget_PaintHThunk`, `ThrottleHSliderGadget_FlushPaintHThunk` | `0003bd80`, `0003bd90`, `0003bd9c` | `004556d0`, `00448b26`, `00448b1c` |
| `ThrottleSlider_PaintH`, `ThrottleSlider_FlushPaintH` | `0003bda8`, `0003bf18` | `00448638`, `00448710` |

`PanelGauge_KeyDispatchNoneThrottleGaugeCopy`, the four stubs from `0003b5d9` and the four thunks have no Ghidra function.

## Globals

| | Address |
|---|---|
| `ThrottleGaugeVtable` | `00094206` |
| this file's copies of the vertical and horizontal slider base tables | `00094222`, `0009438a` |
| the copies of the mixin's tables, stored in this order | `0009424e`, `00094302` |
| the copies of the region's four tables, stored in this order | `00094266`, `0009428a`, `000942b2`, `000942da` |
| the copies of the vertical and horizontal gadget tables | `0009431a`, `000943b6` |
| `ThrottleVSliderGadget_PanelSliderGadgetVtable`, `ThrottleVSliderGadgetVtable` | `00094346`, `0009435e` |
| `ThrottleHSliderGadget_PanelSliderGadgetVtable`, `ThrottleHSliderGadgetVtable` | `000943e2`, `000943fa` |
| the light names, five pointers ([the string table's file](../formats/str-strings.md#where-the-object-file-starts-and-ends)) | `0009328c` |

## Open

- **Open:** what calls the slots `+4` and `+8` of the two mixin tables, the thunks: `es2_xref.py` finds `0003b678`, `0003b684`, `0003bd90` and `0003bd9c` once each, as a stored pointer in those tables, and no branch to them. It finds no reference to `HeapPtr_DtorThrottleGaugeCopy`.
- **Open:** whether a widget registered in the root's list before the slider covers its track in a retail `.GAU`, which would take a press first ([hit testing](cockpit-mouse.md#hit-testing)): the constructors of the instruments `Cockpit_LoadPanel` builds before the throttle at `+0x1e1` to `+0x1e9` are unread. The console's five buttons (`+0x1dd`, [the console buttons](cockpit-weapon-gauges.md#the-console-buttons)) and the in-use weapon rows' rects cover no throttle track in the eight files, the origins of the blocks added.
- **Open:** the size of `dba\pbuttons` frame 27, the vertical knob's length, and of `dba\THR_LED1` and `THR_LED2` frames 0–2 against the bars' rects.
- **Open:** a positive control in the running game ([when a module is done](../../herculan/plan-es1-support.md#when-a-module-is-done)). Proposed, by eye: in a Roadrunner, push the throttle full forward and the knob rises to the top of its track while the strip beside the track's upper half fills upward from the middle as the HERC gathers speed; back up and the lower half fills downward. In a Raptor, the same along the bottom of the across track, and a view change while backing up leaves the reverse bar's lit part undrawn until the speed changes. Dragging the knob in either sets the throttle on release. In DOSBox-X's debugger, `BP <CS>:0003b25c` fires on each release of a drag, with the value at `[ESP+8]`.
