# DBSIM.EXE — labels, the gauge and gadget bases, and bar graphs

**ES1's cockpit writes its captions with ES2's label object, builds its instruments and buttons on ES2's gauge and gadget base classes, and fills its meters with ES2's bar graphs.** The differences a player could see: a label sits a pixel or two from where ES2's would; a display whose feed is damaged flickers on and off in bursts, with a sound as it goes dark, where ES2's goes dark and comes back in long spells; a bar graph repaints only the strip between its old and new value; and the throttle's two bars are drawn, as slices of a full and an empty picture ([the throttle gauge](cockpit-throttle-gauge.md#the-bars)). Addresses are `ES1DBSIM` image addresses ([the X-32 executable](../formats/x32-executable.md#the-image)); ES2's are DBSIM's. Every function here removes its own arguments, so argument lists are in stack order ([calling conventions](../formats/compiler.md#calling-conventions)).

## Where the object files start and end

Four object files lie between [the paper doll's](paper-doll.md#where-the-object-file-starts-and-ends) and [the status screen's](mfd-status-screen.md#where-the-object-file-starts-and-ends), which the run of `ConfigPart`'s copies at `00032ed8` opens ([module copies](../formats/compiler.md#vtables)). Each start below is a function on a 4-byte boundary after zero bytes that follow the previous file's last return ([where object files meet](../formats/compiler.md#where-object-files-meet-in-the-code)):

| Code | Holds | Its end |
|---|---|---|
| `00030f44`–`00031317` | [the labels](#labels) | `Label_StaticInitNoOp` (`00031314`), entry `000adc3c` of the static constructor table: a bare `RET`, then three zero bytes |
| `00031318`–`0003149f` | [the gauge base](#the-gauge-base) | `PanelGauge_TickDropout`'s `RET 4` at `0003149b`, then two zero bytes |
| `000314a0`–`000316d3` | [the gadget bases](#the-gadget-bases) | `VSliderGadget_OnClick`'s `RET 8` at `000316ce`, then three zero bytes |
| `000316d4`–`00032ed7` | [the bar graphs](#bar-graphs) | `LedBmBarGraphV_DrawBitmapSpan`'s `RET 8` at `00032ed5`, which ends on a 4-byte boundary, so no zero fill; the run at `00032ed8` follows |

Only the label file has a static initializer, and none has an entry in the exit table. The vtables the last three store lie in order from `0009338e`, right after the paper doll's data, to `00093585`; the next file's copy of `ConfigPart`'s table is at `0009358e`.

## Labels

**`Label_Ctor` (`00030f44`)**, (label, font), makes ES2's [label object](../../retail/simulation/mfd.md#label-placement) without its panel-relative byte, so `0x20` bytes: the font at `+0`, the rect at `+4`–`+0x13`, the anchor's `int16` x and y at `+0x14` and `+0x16`, the flags at `+0x18` and the fill color at `+0x1c`. **`Label_CtorWithAltFont` (`00031250`)** allocates `0x24` bytes when given none, runs `Label_Ctor` and stores the font again at `+0x20`, the hotkey font.

Placement differs from ES2's in two places. `Label_SetRect` (`00031080`), (label, flags, rect, margin), copies the rect and computes the anchor; the margin is a point of two `int32`, (*mx*, *my*), of which it reads the low 16 bits. The anchor's x is (*x1* − *x0*) >> 1 + *x0* + *mx* for flags 2, else *x1* − *mx* + 1 for flags 4, else *x0* + *mx*; its y is (*y1* − *y0* + *c*) >> 1 + *y0* + *my*, *c* the cell height from `HudFont_CellHeight`(font, `'0'`); each is kept as an `int16` and the shifts are arithmetic. ES2's right-aligns at *x1* − *mx* and centers half the ink height and adds 1. `Label_SetText` (`0003114c`) measures the text only for flags 2 and 4, and puts the pen's x at the anchor's x less (*w* − 1) >> 1 for flags 2 or *w* − 1 for flags 4, else at the anchor's, where ES2's takes 1 ≪ `VideoMode_XCoordShift` from *w*, and its y at the anchor's; it sets the pen at `000b11bc`, `000b11c0`, fills the rect with the brush {0, color} at the render context's `+0x22c` when the fill color is non-zero, and draws with `HudFont_DrawString`. The glyph's top row is the pen's y less the font's ink height (`+0x16`), as in ES2.

The other entry points are ES2's, with these differences:

- **`Label_SetRectAroundPoint` (`00030fc4`)**, (label, flags, text, point): *y0* is *y* less the whole cell height (`HudFont_CellHeight2`), not half of it, and the anchor's y is *y1* + *y*, with no ink height; the flags go to `+0x18`.
- **`Label_SetNumber` (`00031224`)**, (label, n): `ltoa` in base 10.
- **`Label_SetText_Thunk` (`00031280`)**, (label, text), and **`Label_SetTextWithHotkey` (`00031294`)**, (label, index, text): the hotkey moves the pen x (`000b11bc`) right by the width of the text's first index characters and draws `text[index]` in the font at `+0x20` with `HudFont_DrawGlyph`. The pen is where `Label_SetText` left it, the start of the text, since `HudFont_DrawString` advances a copy of the pen. When the index is at most the text's length (`REPNE SCASB`, compared as `int16`s), it puts a NUL at `text[index]`, takes `HudFont_MeasureString` of what is left into the local `[EBP−2]`, keeps the character in `[EBP−4]` and puts it back; so an index equal to the length measures the whole text and draws the terminating NUL's glyph. ES1's adds the width and draws the character whatever the index; with the index past the text's length, both come from those two locals, which the function then never wrote, where ES2's does neither.

`es2_xref.py` finds `Label_SetTextWithHotkey` called from `MfdFlashCommScreen_Paint` (`00035d20`), twice from `MfdFlashCommScreen_Update` (`00035e14`), twice from `HddCommandScreen_Update` (`0003f578`) and from `HddCommandScreen_RefreshOrders` (`00040948`) ([the order list](hdd-command-screen.md#the-order-list)), beside the reads of the squad orders' strings and attribute bytes ([`STRINGS0.STR` group 0](../formats/str-strings.md#the-groups)), and `Label_CtorWithAltFont` from `MfdFlashCommScreen_Ctor` (`00035c04`) and `HddCommandScreen_Ctor` (`0003ee58`). The FLASH COMM screen and the Heads-Down Display's command screen pass each order's attribute byte as the index, and in the retail `STRINGS0.STR` each one lies inside its text; both set the hotkey font to `cpred`, which the command screen's update and its order list keep for an available order and replace with `ColorSchemePanels`[0] for an unavailable one ([the FLASH COMM screen](mfd-flash-comm.md#the-screen-object), [the command screen](hdd-command-screen.md#drawing-it)).

The font functions the labels reach, in the library region:

- **`HudFont_GlyphWidth` (`00069ce0`)**, (font, c): the fixed width at `+0xc` unless it is −1, else the width byte at `+0x2a`[c − the first code at `+8`], ES2's (`0048238c`).
- **`HudFont_CellHeight2` (`00069d50`)**, (font, text): the `int16` at `+0xa`; it does not read the text. ES2's (`0048241c`) takes the font alone.
- **`HudFont_DrawGlyph` (`00069d64`)**, (font, c, pen): ES2's (`00482428`) without the message ES2's formats first: blits the glyph through `Raster_BlitDispatch` with its top row at the pen's y less `+0x16`, then, when the render context (`000bdbda`) has `+0x230` set, underlines it with `Raster_DrawLine` along the pen's row.

## The gauge base

**`PanelGauge_Ctor` (`00031318`)**, (gauge, cockpit, rect), is the base of each instrument: it copies the rect to `+0`, stores the cockpit at `+0x10` and `PanelGaugeVtable` (`0009338e`) at `+0x76`, and clears `+0x68`, `+0x74` and `+0x75`. ES2's (`00438b20`) keeps its vtable at `+0`, the rect at `+4` and the cockpit at `+0x14`, and also clears the sequencer and the power-up fields, which ES1's base lacks. The allocation at its head is the rect's own constructor inlined ([constructors](../formats/compiler.md#constructors-and-destructors)): with the gauge null it allocates `0x10` bytes and then writes through the null pointer.

`PanelGaugeVtable` is ES2's (`0049bc6c`) one slot further on: `PanelGauge_OnChildClickNoOp` (`00031395`, three `NOP`s and `RET 0xc`) at `+4`, 0 at `+8` and `+0xc` where ES2's holds `Rtl_PureVirtualCalled`, and `PanelGauge_KeyDispatchNone` (`0003139b`, `NOP`, `XOR EAX,EAX`, `RET 8`) at `+0x10`. A child button calls its `+4` with (gauge, flags, child) ([the gadget bases](#the-gadget-bases)).

### Sensor dropout

**While a display's feed is damaged, it flickers in bursts: for a random stretch it goes dark and comes back every few moments, with a sound each time it goes dark, then holds steady until the next burst.** A burst lasts 1 to 240 counts of the clock, up to 4 seconds, and flips the display every 1 to 30 counts, and the next begins 120 to 420 counts, 2 to 7 seconds, after it ends. ES2's alternates long dark and lit spells instead ([ES2's sensor dropout](../../retail/simulation/cockpit-hud-widgets.md#sensor-dropout)). The clock is the dword `000981c4`, 60 counts a second ([the tick length](fixed-point-math.md#the-tick-length--time_beginsimtick-00063f40)), and the random numbers are `Math_RandomNext`(`000b5c48`), low word.

| Offset | Holds |
|---|---|
| `+0x68` | the condition, 0 intact to 4 destroyed (`int16`) |
| `+0x6a` | the burst's length in counts (`int16`) |
| `+0x6c` | the clock count the burst starts after |
| `+0x70` | the clock count the next flip comes after |
| `+0x74` | 1 dark, 0 shown |
| `+0x75` | set to 2 at each change |

The display's update stores `Player_DependentCondition`(4) at `+0x68` and runs **`PanelGauge_TickDropout` (`00031400`)**, (gauge), when it is non-zero; `Gunsight_UpdateAndPaint` (`00034f3c`) does so, outside view 4 ([the gunsight's update](cockpit-gunsight-hud.md#painting-and-the-per-frame-update)), as does the MFD's `MfdDisplay_Update` (`0003a2a8`, [the MFD's dropout](mfd-display.md#the-dropout)), and `es2_xref.py` finds the third call in `HddDisplay_Update` (`0003cd28`), the Heads-Down Display's, outside view 4 ([drawing it](heads-down-display.md#drawing-it)). The tick:

1. Condition 3 sets dark and returns, leaving `+0x75` as it is.
2. Otherwise, while the clock is past `+0x6c` and short of `+0x6c` + `+0x6a`, the window open at both ends, each time it is past `+0x70` the state flips, `+0x75` becomes 2, `+0x70` becomes the clock + 1 + (*r* mod 30), and a flip to dark plays sound `0x1c` (`Sound_Play`).
3. Once the clock, past `+0x6c`, reaches `+0x6c` + `+0x6a`, it runs `PanelGauge_ScheduleDropout`, sets the state dark for condition 2 or more and shown below 2, and `+0x75` to 2.

The three compares of the clock are unsigned (`JBE`); the burst's end is `+0x6c` plus the `int16` `+0x6a` sign-extended. The condition is an `int16` the tick tests against 3 and 2 alone.

**`PanelGauge_ScheduleDropout` (`000313a4`)**, (gauge): `+0x6c` and `+0x70` = the clock + (*r* mod 6 + 2) × 60, and `+0x6a` = *r'* mod 240 + 1. A gauge from the zero-filled heap ([the heap](memory.md#the-heap)) has these at 0, so its first tick schedules a burst. A condition of 4 flickers as 2 does, dark between bursts, rather than staying dark as 3 does.

## The gadget bases

**ES1's buttons and sliders are ES2's [gadget family](../../retail/simulation/cockpit-input.md#the-cockpits-own-gadget-classes), with each subobject's vptr after its data:** a gadget's primary vptr at `+0x1c`, the `PanelGadget` mixin's data, the owning gauge, at `+0x20` and its vptr at `+0x24`, for a slider the mixin at `+0x3e` and `+0x42`. A primary table has ES2's [leaf-widget slots](../../retail/simulation/cockpit-input.md#the-leaf-widget-vtable) one slot further on; the mixin's table holds the click sound at `+0xc`.

- **`Widget_ClickSound` (`000314a0`)**, frameless: `Sound_Play`(`0x11`) and `RET 4`. `es1_naming.py vtables` lists it at `+0xc` of nine three-slot mixin tables (`00093ba6`, `00093be6`, …), and `es2_xref.py` finds 16 stored pointers to it.
- **`Widget_ForwardClickToOwner` (`000314ac`)**, (gadget, flags): with bit 0 of flags set, calls slot `+4` of the owner's `+0x76` table with (owner, flags, gadget), then the gadget's primary `+8` (invalidate) and the mixin's `+0xc` (the sound) on the mixin. ES2's (`00438e3c`). It is the click slot `+0xc` of eight tables, `PanelSelectGadgetVtable` (`000933aa`) and seven in later files.
- **`PanelStateGadget_OnClick` (`00031550`)**, (gadget, flags): the same, after flipping the lit byte `+0x3c` (ES2's `+0x40`). The click slot of `00093d76` and `00094736`.
- **`HSliderGadget_OnClick` (`0003166c`)** and **`VSliderGadget_OnClick` (`000316a0`)**, (slider, value): the same three calls with the owner at `+0x3e` and the mixin's vptr at `+0x42`, with no test of the value, as ES2's (`0043901c`, `0043904c`). The first is the click slot of five tables whose value slot reads a horizontal track (`SliderWidget_GetValueH`, `00043ac8`), the second of two whose value slot reads a vertical one (`SliderWidget_GetValueV`, `00043be8`) ([the slider base](cockpit-mouse.md#the-slider-base)).
- **`Widget_NoClickSound` (`00031666`)**, frameless, two `NOP`s and `RET 4`: slot `+0xc` of `PanelSliderGadgetVtable` (`000933d2`), whose `+4` and `+8` hold 0. ES2's (`00439014`).

`PanelSelectGadgetVtable` (`000933aa`) is ES2's `PanelSelectGadget` table (`0049dee4`) one slot further on, its slots this file's copies of the button base's ([`panel.cpp`'s copies](cockpit-panel.md#the-screen-edges-and-the-cursors)):

| Slot | ES1 | Address | ES2's |
|---|---|---|---|
| `+4` | `CTLButtonControl_PaintNoOpGadgetsCopy`, `NOP` and `RET 4` | `00031523` | `CTLButtonControl_PaintNoOp` (`00455545`) |
| `+8` | `Widget_InvalidateDeferredGadgetsCopy`, (gadget): `+0x1b` = 2, ES2's `+0x1f` ([deferred painting](../../retail/simulation/cockpit-input.md#painting-is-deferred-two-frames)) | `00031528` | `Widget_InvalidateDeferred` (`00455524`) |
| `+0xc` | `Widget_ForwardClickToOwner` | `000314ac` | `Widget_ForwardClickToOwner` (`00438e3c`) |
| `+0x10` | `CTLControl_FlushDeferredPaintNoOpGadgetsCopy`, `RET 4` | `00031538` | `CTLControl_FlushDeferredPaintNoOp` (`0045554a`) |
| `+0x14` | `Widget_GetButtonValueGadgetsCopy`, `NOP`, `Widget_ClickButtons` (`00094b84`, [the frame's mouse events](cockpit-mouse.md#the-frames-mouse-events--cockpitmouse_processqueue-000444d0)) in `AX`, `RET 4` | `0003153b` | `Widget_GetButtonValue` (`00455530`) |
| `+0x18` | `CTLControl_SetValueNoOpGadgetsCopy`, three `NOP`s and `RET 8` | `00031545` | `CTLControl_SetValueNoOp` (`0045553b`) |
| `+0x1c` | `CTLControl_DragMoveNoOpGadgetsCopy`, `NOP` and `RET 8` | `0003154b` | `CTLControl_DragMoveNoOp` (`00455540`) |

The two destructors, (gadget, flags), destroy the members as ES2's do but test neither the gadget nor the flags, and free nothing of the gadget itself:

- **`PanelSelectGadget_Dtor` (`000314e4`)**: stores `PanelSelectGadgetVtable` at `+0x1c`, frees `+0x2c` with `Mem_Delete` and destroys each of the three objects at `+0x30`, `+0x34`, `+0x38` that is set through its slot `+0x1c` with flags 3. ES2's (`00438e70`).
- **`PanelSliderGadget_Dtor` (`0003158c`)**, on the mixin: stores `PanelSliderGadgetVtable` at its `+4`, frees `+8`, destroys the object at `+0x10` through its slot `+0x1c` with flags 3, frees `+0x14` when set, runs `GLBitmapArray_Dtor`(`+0x38`, 3) and destroys the `GLRectangleRegion` at `+0x18` in place, storing its three vtables there in turn. ES2's (`00438f4c`).

`es2_xref.py` finds no reference to either ([Open](#open)). The region's three vtables and their destructors are the file's copies of `GLRectangleRegion`'s, class tag `0x28000c` at the first slot of `000933ea` ([`mechview.cpp`'s copies](cockpit-views.md#functions)): `GLRectangleRegion_DtorGadgetsCopy` (`000315ec`, `000933ea`), `GLRegion_DtorGadgetsCopy` (`0003161c`, `00093412`) and `GLBase_DtorGadgetsCopy` (`00031644`, `0009343a`), each (object, flags) storing its table and those of its bases at `+0` and freeing the object when bit 0 of flags is set.

## Bar graphs

**ES1's bar graphs are ES2's [base classes](../../retail/simulation/cockpit-hud-widgets.md#the-bar-graph-base) with the rect at `+0` and the vptr at `+0x28`, one color, and a paint that repaints only what changed.**

| Offset | Holds | ES2's |
|---|---|---|
| `+0x00` | the rect `{x0, y0, x1, y1}` | `+0x04` |
| `+0x10` | the span | `+0x14` |
| `+0x14`, `+0x18` | start and end | `+0x18`, `+0x1c` |
| `+0x1c` | the fill position | `+0x20` |
| `+0x20` | the unlit color | `+0x24` |
| `+0x28` | the vptr: `BarGraphHVtable` (`00093462`) or `BarGraphVVtable` (`0009347a`) | `+0x00` |

`BarGraph_Ctor` (`000316d4`), (bar, rect), copies the rect, the allocation at its head the rect's own as in `PanelGauge_Ctor`. **`BarGraphH_Ctor` (`00031740`)** and **`BarGraphV_Ctor` (`00031aac`)**, (bar, range, rect), allocate `0x2c` bytes when given none, run `BarGraph_Ctor`, store the vtable and set the span, start, end and position as ES2's `LedBarGraph_CtorBase` (`004390c4`) and `LedBarGraph_CtorV` (`00439344`) do. `es2_xref.py` finds `BarGraphH_Ctor` called from `LedBarGraph_Ctor` and `LedBmBarGraphH_Ctor` and `BarGraphV_Ctor` from `LEDBarGraphV_Ctor` and `LedBmBarGraphV_Ctor` ([LED](#led-bar-graphs) and [bitmap bar graphs](#bitmap-bar-graphs)). `BarGraph_GetRect` (`00031738`), (bar), returns the bar, whose rect is at `+0`; `es2_xref.py` finds it called once each from `EnergyPoolGauge_Paint` (`00039a28`) and `EnergyPoolGauge_Update` (`00039abc`), which test whether the energy meter's bar is on screen ([drawing the energy meter](cockpit-energy-shields.md#drawing-the-meter)), and four times each from `ThrottleSlider_PaintV` (`0003b804`) and `ThrottleSlider_FlushPaintH` (`0003bf18`), which union the throttle's bars' rects into the rect they copy ([drawing the throttle](cockpit-throttle-gauge.md#drawing-it)). `BarGraph_NoOp` (`00031733`), `NOP` and `RET 8`, sits between them, as ES2's does.

Both tables hold three slots from `+4`, ES2's in order: paint, repaint and reset. The reset, `BarGraphH_ResetToEmpty` (`000317a4`) and `BarGraphV_ResetToEmpty` (`00031b10`), is ES2's: it puts `+0x1c` at the start `+0x14` when the span `+0x10` is above 0, otherwise at the end `+0x18`. The fill edge is ES2's, except that a `BarGraphV` fills from start when the span is below 1 in its paint as in its repaint and reset, where ES2's paint tests below 0.

**The paint repaints only the strip between the old position and the new.** `BarGraphH_PaintToValue` (`000317c4`) and `BarGraphV_PaintToValue` (`00031b30`), (bar, value), compute the new position p as ES2's do and return when it equals the stored one, q. Otherwise they fill the rect from q to p on the bar's axis with `Raster_FillRect`: in the unlit color when the bar shrinks, through the brush the render context holds (the display's `000bdd28` `+0x44`, its `+0x228`) for that fill alone, and with that brush as it stands when the bar grows, so the lit color is whatever brush the caller installed. They store p. `BarGraphV_PaintToValue` writes the rect's *y0* and *y1*, where ES2's writes *x0* and *x1*.

`BarGraphH_RepaintSpan` (`000318e0`) and `BarGraphV_RepaintSpan` (`00031c54`), (bar, a, b), are ES2's, the unlit color from `+0x20`, the lit part again the installed brush. As in ES2's, a run the position splits is lit on the side the paint lights in `BarGraphV`'s and on the other side in `BarGraphH`'s.

### LED bar graphs

**ES1's LED bar graphs are ES2's [pinstriped bars](../../retail/simulation/cockpit-hud-widgets.md#led-bar-graphs) on the base above, and their paint, like the base's, repaints only the strip that changed.** The two shades are at `+0x2c` and `+0x30`, as in ES2.

- **`LedBarGraph_Ctor` (`00031e28`)** and **`LEDBarGraphV_Ctor` (`0003216c`)**, (bar, range, rect), allocate `0x34` and `0x3c` bytes when given none, the sizes of ES2's class records, run `BarGraphH_Ctor` or `BarGraphV_Ctor` and store `LEDBarGraphHVtable` (`00093492`) or `LEDBarGraphVVtable` (`000934aa`) at `+0x28`; neither sets a color. `es2_xref.py` finds `LedBarGraph_Ctor` called from `WeaponSliderGadget_Ctor` (`000386a0`, [the charge bar](cockpit-weapon-gauges.md#the-charge-bar--weaponslidergadget)), `EnergyPoolGauge_Ctor` (`00039818`), `AlertSliderGadget_Ctor` (`000469ec`, [the panels' slider](alert-panels.md#the-slider)) and `PreferencesPanel_Ctor` (`00047578`, [the preferences panel](preferences.md#the-preferences-panel--preferencespanel_ctor-00047578)), as many call sites as ES2's has, and `LEDBarGraphV_Ctor` from `EnergyPoolGauge_Ctor` alone, which builds one or the other by its `.GAU` block ([the energy meter](cockpit-energy-shields.md#building-it--energypoolgauge_ctor-00039818)).
- **`LedBarGraph_FillPinstripe` (`00032078`)** and **`LedBarGraphV_FillPinstripe` (`000323f0`)**, (bar, rect), are ES2's statement for statement. `LedBarGraphV_FillPinstripe` draws lines from (*x0*, *y*) to (*x1*, *y*), the even rows first in `+0x2c`, then the odd in `+0x30`.
- **`LedBarGraph_PaintToValue` (`00031e5c`)** and **`LedBarGraphV_PaintToValue` (`000321a0`)**, (bar, value), compute p as `BarGraphH_PaintToValue` and `BarGraphV_PaintToValue` do and return when it equals the stored position q. The strip that becomes lit, q to p, is drawn by the pinstripe; the strip that becomes unlit through a temporary brush {0, `+0x20`} at the render context's `+0x22c` (the display's `000bdd28` `+0x44` less 4), from p + 1 to q for an `LEDBarGraphH` filling from start and from q to p, p included, in the other three cases. They store p. `LedBarGraph_PaintToValue` writes each strip's ends into the rect's *x0* and *x1* and `LedBarGraphV_PaintToValue` into its *y0* and *y1*, where ES2's writes *x0* and *x1*, and they leave them there, so `BarGraph_GetRect` then answers the last strip drawn on the bar's axis and the rect as built across it.
- **`LedBarGraph_RepaintToValue` (`00031fac`)**, (bar, a, value), is ES2's (bar, value) with an argument it does not read; **`LedBarGraphV_Repaint` (`000322f4`)**, (bar, a, b), uses neither argument and is otherwise ES2's. They leave the rect's ends as the paints do. `LedBarGraphV_Repaint` with the span 1 or more draws start to *q* unlit and *q* + 1 to end through the pinstripe, and below 1 the reverse. `EnergyPoolGauge_Paint` calls them at a view change, so the energy meter shows them ([drawing the energy meter](cockpit-energy-shields.md#drawing-the-meter)).

### Bitmap bar graphs

**ES1's bitmap bar graphs are ES2's [bitmap bar graphs](../../retail/simulation/cockpit-hud-widgets.md#bitmap-bar-graphs), slices of a full and an empty picture, repainting only the strip that changed; the throttle's forward and reverse bars are the ones built ([the throttle gauge](cockpit-throttle-gauge.md#the-bars)).**

- **`LedBmBarGraphH_Ctor` (`000324e4`)**, (bar, unlit, lit, range, rect), and **`LedBmBarGraphV_Ctor` (`00032a18`)**, (bar, lit, unlit, range, rect), allocate `0x68` bytes when given none, run `BarGraphH_Ctor` or `BarGraphV_Ctor`, build the two regions at `+0x2c` and `+0x44` with this file's copies of the region vtables (`000934c2`, `000934e6`, `0009350e` and `00093536` stored in turn), store `LEDBMBarGraphHVtable` (`0009355e`) or `LEDBMBarGraphVVtable` (`00093576`) at `+0x28`, the second argument at `+0x64` and the third at `+0x60`, and allocate the buffer at `+0x5c` with `Mem_New`, the `+0x64` bitmap's width × height + `0x20` bytes, where ES2's takes a fixed 5000. So the lit bitmap is at `+0x60` in an `LEDBMBarGraphH` and at `+0x64`, as in ES2, in an `LEDBMBarGraphV`.
- **`LedBmBarGraphH_PaintToValue` (`00032764`)** and **`LedBmBarGraphV_PaintToValue` (`00032c1c`)**, (bar, value), compute p as the base's do, clamp it to at most end and then to at least start, and when it differs from the stored q draw the strip between them, both ends included, from the lit bitmap when it becomes lit and from the unlit one when it becomes unlit, through `LedBmBarGraphH_DrawBitmapSpan` or `LedBmBarGraphV_DrawBitmapSpan`; then they store p. A growing `LEDBMBarGraphH` draws from `+0x60` and a growing `LEDBMBarGraphV` from `+0x64`, each from the other bitmap when it shrinks. Both signs of the span draw from the bitmaps, where ES2's `LedBmBarGraphH_PaintToValue` fills with `Raster_FillRect` below 1. Before drawing, each writes the strip's ends, the lesser of p and q first, into the bar's own rect, *x0* and *x1* (`LEDBMBarGraphH`) or *y0* and *y1* (`LEDBMBarGraphV`), and leaves them there, so `BarGraph_GetRect` then answers the last strip drawn on the bar's axis and the rect as built across it.
- **`LedBmBarGraphH_RepaintNoOp` (`00032833`)**, a `NOP` and `RET 0xc`, and **`LedBmBarGraphV_RepaintNoOp` (`00032ced`)**, three `NOP`s and `RET 0xc`, (bar, a, b), are frameless with no Ghidra function.
- **`LedBmBarGraphH_DrawBitmapSpan` (`00032838`)** and **`LedBmBarGraphV_DrawBitmapSpan` (`00032cf4`)**, (bar, bitmap), are ES2's with the bitmap's size unshifted and one blit, `Bitmap_Blit`, in both. The canvas context is `000bdbda`, the display `000bdd28`, and a current display's source page goes through `00098dd6` and the raster slot at `0009df14`. With (*ox*, *oy*) the canvas context's `+0x220` and `+0x224`, the strip {*x0*, *y0*, *x1*, *y1*} in the bar's rect, *start* the bar's `+0x14`, and *w* and *h* the `int16` `+6` and `+4`, columns and rows, of the bitmap at `+0x64` (the unlit one in an `LEDBMBarGraphH`, the lit one in an `LEDBMBarGraphV`), whichever bitmap is drawn:

  1. region `+0x2c`'s rect (`+0x34`–`+0x40`) is {2*ox*, 2*oy*, 2*ox* + *w*, 2*oy* + *h*};
  2. region `+0x44`'s rect (`+0x4c`–`+0x58`) is {*x0* − *start* + *ox*, *oy*, *x1* − *start* + *ox*, *y1* − *y0* + *oy*} with the copy's offset (*start* − *ox*, *y0* − *oy*) across, and {*ox*, *y0* − *start* + *oy*, *x1* − *x0* + *ox*, *y1* − *start* + *oy*} with (*x0* − *ox*, *start* − *oy*) down;
  3. the display's draw page `+0x3c` becomes its source page `+0x38`;
  4. `Region_Save`(region `+0x2c`, buffer `+0x5c`); `Bitmap_Blit`(bitmap, 0, (*ox*, *oy*)); `Region_CopyBetweenPages`(region `+0x44`, the offset); `Region_Restore`(region `+0x2c`, (*ox*, *oy*), buffer `+0x5c`).

  `Region_Save` moves its rect by the translation twice and the blit and the restore once each ([saving and restoring a rect region](../rendering/screen-pages.md#lines-and-clipped-bitmaps)).
- The region destructors, (object, flags), are this file's copies ([the gadget file's](#the-gadget-bases)): **`GLRectangleRegion_DtorBarGraphsCopy` (`00032734`)**, slot `+0x1c` of `00093536`, whose `+4` returns the class tag `0x28000c`; **`GLRegion_DtorBarGraphsCopy` (`0003270c`)**, of `0009350e`; and **`GLBase_DtorBarGraphsCopy` (`000326e8`)**, of `000934e6`. Each stores its table and its bases' at `+0` in turn and frees the object when bit 0 of flags is set.

## In GO.EXE

**The shell compiles the label file and the font functions too.** `ES1GO` `0000fd20`, `0000fdc6`, `0000fe8e`, `0000ffc5`, `000100d8`, `00010103`, `00010140` and `00010158` are `Label_Ctor` to `Label_SetTextWithHotkey` in the order of the table below, the same statements laid out differently; its `Label_SetText` finds the render context at the display's `+0x40` less 4 where the simulator's reads `+0x44`. `00051f80`, `00051ff0` and `00052004` are `HudFont_GlyphWidth`, `HudFont_CellHeight2` and `HudFont_DrawGlyph`, the last testing the render context `000ab612`. Its code from `00008920` holds the gadget click handlers' statements ([Open](#open)).

**The shell compiles the bar graph file too**, as its code from `00003d7c` to the `RET 8` at `0000601e`, in the simulator's order:

| Functions | `ES1GO` |
|---|---|
| `BarGraph_Ctor`, `BarGraph_NoOp`, `BarGraph_GetRect` | `00003d7c`, `00003de0`, `00003dee` |
| `BarGraphH_Ctor`, `BarGraphH_ResetToEmpty`, `BarGraphH_PaintToValue`, `BarGraphH_RepaintSpan` | `00003dfa`, `00003e80`, `00003e9f`, `00004008` |
| `BarGraphV_Ctor`, `BarGraphV_ResetToEmpty`, `BarGraphV_PaintToValue`, `BarGraphV_RepaintSpan` | `00004272`, `000042f4`, `00004313`, `00004480` |
| `LedBarGraph_Ctor`, `LedBarGraph_PaintToValue`, `LedBarGraph_RepaintToValue`, `LedBarGraph_FillPinstripe` | `0000472a`, `0000476d`, `00004903`, `00004b87` |
| `LEDBarGraphV_Ctor`, `LedBarGraphV_PaintToValue`, `LedBarGraphV_Repaint`, `LedBarGraphV_FillPinstripe` | `00004d04`, `00004d47`, `00004ee0`, `000051b4` |
| `LedBmBarGraphH_Ctor`, the three region destructors, `LedBmBarGraphH_PaintToValue`, `LedBmBarGraphH_RepaintNoOp`, `LedBmBarGraphH_DrawBitmapSpan` | `00005331`, `00005560`, `0000558c`, `000055d5`, `00005640`, `00005767`, `00005777` |
| `LedBmBarGraphV_Ctor`, `LedBmBarGraphV_PaintToValue`, `LedBmBarGraphV_RepaintNoOp`, `LedBmBarGraphV_DrawBitmapSpan` | `00005a19`, `00005c48`, `00005d70`, `00005d80` |

They are the same statements laid out differently, the no-ops framed functions, with these differences: the render context is the display's (`000ab616`) `+0x40` less 4, the display's source and draw pages are its `+0x34` and `+0x38`, and the canvas context is `000ab612`, a current display's source page going through `0008448a` and the raster slot at `000865f0`. Two repaints differ in their statements:

- **`LedBarGraph_RepaintToValue` (`00004903`)** lights start to p and leaves the rest unlit, as the simulator's does, only while p lies strictly between start and end. With the span at 1 or more it fills the whole bar unlit when p is at or before start and lit through the pinstripe when p is at or after end; with the span below 1, unlit when p is at or after end and with `Raster_FillRect` in the brush as installed when p is at or before start.
- **`LedBarGraphV_Repaint` (`00004ee0`)**, (bar, a, b), repaints the run from a to b: `BarGraphV_RepaintSpan`'s statements with the lit part drawn by `LedBarGraphV_FillPinstripe` and the unlit part through the temporary brush, where the simulator's redraws the whole bar at the stored position.

`es2_xref.py --binary ES1GO` finds `LedBarGraph_Ctor` called once, from `00003684`, and `BarGraphH_Ctor` and `BarGraphV_Ctor` from the constructors above ([Open](#open)). The vtables are `0007dc30` (`BarGraphH`), `0007dc50` (`BarGraphV`), `0007dc74` and `0007dc98` (the LED bar graphs), `0007dd82` and `0007ddaa` (the bitmap bar graphs), and the region copies' `0007dce2`, `0007dd14` and `0007dd4c`.

## Functions

| ES1 | Address | ES2 |
|---|---|---|
| `Label_Ctor`, `Label_SetRectAroundPoint`, `Label_SetRect`, `Label_SetText` | `00030f44`, `00030fc4`, `00031080`, `0003114c` | `004387ac`, `004387d0`, `00438884`, `00438920` |
| `Label_SetNumber`, `Label_CtorWithAltFont`, `Label_SetText_Thunk`, `Label_SetTextWithHotkey`, `Label_StaticInitNoOp` | `00031224`, `00031250`, `00031280`, `00031294`, `00031314` | `00438a40`, `00438a70`, `00438a94`, `00438aac`, `00438b1c` |
| `PanelGauge_Ctor`, `PanelGauge_OnChildClickNoOp`, `PanelGauge_KeyDispatchNone` | `00031318`, `00031395`, `0003139b` | `00438b20`, `0045233f`, `00452344` |
| `PanelGauge_ScheduleDropout`, `PanelGauge_TickDropout` | `000313a4`, `00031400` | `PanelGauge_RollDuration` (`00438d6c`) has the role, `00438bc0` |
| `Widget_ClickSound`, `Widget_ForwardClickToOwner`, `PanelSelectGadget_Dtor` | `000314a0`, `000314ac`, `000314e4` | `00438e2c`, `00438e3c`, `00438e70` |
| `PanelSelectGadgetVtable`'s other slots | `00031523`–`0003154b` | [the table](#the-gadget-bases) |
| `PanelStateGadget_OnClick`, `PanelSliderGadget_Dtor` | `00031550`, `0003158c` | `00438f14`, `00438f4c` |
| the region destructors | `000315ec`, `0003161c`, `00031644` | the code of [`mechview.cpp`'s copies](cockpit-views.md#functions) |
| `Widget_NoClickSound`, `HSliderGadget_OnClick`, `VSliderGadget_OnClick` | `00031666`, `0003166c`, `000316a0` | `00439014`, `0043901c`, `0043904c` |
| `BarGraph_Ctor`, `BarGraph_NoOp`, `BarGraph_GetRect` | `000316d4`, `00031733`, `00031738` | `0043907c`, `004390b0`, `004390b8` |
| `BarGraphH_Ctor`, `BarGraphH_ResetToEmpty`, `BarGraphH_PaintToValue`, `BarGraphH_RepaintSpan` | `00031740`, `000317a4`, `000317c4`, `000318e0` | `004390c4`, `0043a211`, `00439118`, `004391e0` |
| `BarGraphV_Ctor`, `BarGraphV_ResetToEmpty`, `BarGraphV_PaintToValue`, `BarGraphV_RepaintSpan` | `00031aac`, `00031b10`, `00031b30`, `00031c54` | `00439344`, `0043a190`, `00439398`, `00439460` |
| `LedBarGraph_Ctor`, `LedBarGraph_PaintToValue`, `LedBarGraph_RepaintToValue`, `LedBarGraph_FillPinstripe` | `00031e28`, `00031e5c`, `00031fac`, `00032078` | `004395c4`, `004395e8`, `004396a8`, `00439758` |
| `LEDBarGraphV_Ctor`, `LedBarGraphV_PaintToValue`, `LedBarGraphV_Repaint`, `LedBarGraphV_FillPinstripe` | `0003216c`, `000321a0`, `000322f4`, `000323f0` | `00439834`, `00439858`, `00439918`, `00439a0c` |
| `LedBmBarGraphH_Ctor`, `LedBmBarGraphH_PaintToValue`, `LedBmBarGraphH_RepaintNoOp`, `LedBmBarGraphH_DrawBitmapSpan` | `000324e4`, `00032764`, `00032833`, `00032838` | `00439ae8`, `00439b8c`, `00439c40`, `00439c48` |
| `LedBmBarGraphV_Ctor`, `LedBmBarGraphV_PaintToValue`, `LedBmBarGraphV_RepaintNoOp`, `LedBmBarGraphV_DrawBitmapSpan` | `00032a18`, `00032c1c`, `00032ced`, `00032cf4` | `00439e10`, `00439eb4`, `00439f68`, `00439f70` |
| `GLRectangleRegion_DtorBarGraphsCopy`, `GLRegion_DtorBarGraphsCopy`, `GLBase_DtorBarGraphsCopy` | `00032734`, `0003270c`, `000326e8` | the code of the gadget file's copies |
| `HudFont_GlyphWidth`, `HudFont_CellHeight2`, `HudFont_DrawGlyph` | `00069ce0`, `00069d50`, `00069d64` | `0048238c`, `0048241c`, `00482428` |

## Globals

| | Address |
|---|---|
| `PanelGaugeVtable` | `0009338e` |
| `PanelSelectGadgetVtable`, `PanelSliderGadgetVtable` | `000933aa`, `000933d2` |
| the region vtables' copies | `000933ea`, `00093412`, `0009343a` |
| `BarGraphHVtable`, `BarGraphVVtable` | `00093462`, `0009347a` |
| `LEDBarGraphHVtable`, `LEDBarGraphVVtable` | `00093492`, `000934aa` |
| the bar graph file's region vtable copies, stored in this order | `000934c2`, `000934e6`, `0009350e`, `00093536` |
| `LEDBMBarGraphHVtable`, `LEDBMBarGraphVVtable` | `0009355e`, `00093576` |
| the label pen, {x, y} | `000b11bc` |

## Open

- **Open:** a caller of `Label_SetRectAroundPoint`, `Label_SetNumber`, `PanelSelectGadget_Dtor`, `PanelSliderGadget_Dtor` or `BarGraph_NoOp`: `es2_xref.py` finds no branch to any of them and no stored pointer. It finds `BarGraph_Ctor`'s address also as a dword at `000ac6cc`, among other code addresses.
- **Open:** which part of the machine `Player_DependentCondition`'s dependent 4, the dropout's condition, is.
- **Open:** `GO.EXE`'s gadget code, `ES1GO 00008920`–`00008cc0`, unread beyond the decompile lines that match the simulator's `Widget_ForwardClickToOwner`, `PanelStateGadget_OnClick` and the two slider handlers: `0000897b`, `00008a63`, `00008c40`, `00008c77`. In its bar graph code, `es2_xref.py --binary ES1GO` finds no reference to `LEDBarGraphV_Ctor` (`00004d04`), `LedBmBarGraphH_Ctor` (`00005331`) or `LedBmBarGraphV_Ctor` (`00005a19`); `00003684`, which builds the shell's one LED bar graph, is unread.
- **Open:** a positive control in the running game for the done checklist ([when a module is done](../../herculan/plan-es1-support.md#when-a-module-is-done)). Proposed: in DOSBox-X's debugger, `BP <CS>:00031294` fires as the squad order list is drawn, and the word at `[ESP+8]` is the hotkey's index into the string at `[ESP+0xc]`. For the bar graphs: `BP <CS>:00032c1c` (a vertical throttle) or `00032764` (a horizontal one) fires twice each time the value the throttle's bars show changes, once for each bar, with the value in the word at `[ESP+8]`.
