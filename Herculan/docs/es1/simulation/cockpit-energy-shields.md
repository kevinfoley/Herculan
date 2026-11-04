# DBSIM.EXE — the energy meter and the shield display

**ES1's energy meter is ES2's Master Energy Pool meter, a striped bar that fills with the reactor's energy pool, and its shield display is ES2's: two buttons that move the shield balance toward the front or the rear, two numbers that show the balance, and rings in the canopy art lit by rewriting their palette entries as each facing's charge changes ([ES2's energy meter](../../retail/simulation/cockpit-hud-widgets.md#led-bar-graphs), [ES2's `ShieldsGauge`](../../retail/simulation/cockpit-hud-widgets.md#shieldsgauge)).** What a player would see differently:

- the energy bar runs up the panel in five chassis and across it in three (`TOMAHAWK`, `HERCULES`, `COLOSSUS`), and in five its fill is cut to a shape by an `edg\ENG_CLP` file, where ES2's always runs across, unshaped;
- its two stripe shades are the layout's own, different between chassis;
- the two shield buttons are pictures from the console buttons' bank, and show their press, where ES2's draw nothing of their own;
- the balance numbers are green;
- the rings dim on a ramp of their own, from entries 80 to 85, with no overcharged colors, and one ring of each facing goes black as the facing's charge falls below a third of the shields' capacity and glows dim green again below an eighth ([the ring colors](#the-ring-colors--shieldsgauge_updateringpalette-00039340));
- neither instrument's code runs a power-up: both show their values from the mission's first frame.

Addresses are `ES1DBSIM` image addresses ([the X-32 executable](../formats/x32-executable.md#the-image)); ES2's are DBSIM's. Every function here removes its own arguments, so argument lists are in stack order ([calling conventions](../formats/compiler.md#calling-conventions)). The two instruments are the cockpit's `+0x1e1` and `+0x1e5` ([building the cockpit](cockpit-panel.md#building-it)), built from the `.GAU` blocks at 548 and 616 ([the energy meter's block](../formats/gau-cockpit-layout.md#the-energy-meter-block-at-548), [the shield display's](../formats/gau-cockpit-layout.md#the-shield-display-block-at-616)).

## Where the object files start and end

Two object files follow [the weapon rows'](cockpit-weapon-gauges.md#where-the-object-file-starts-and-ends), each opening with a lone copy of the 40-byte destructor that opens a run of `ConfigPart`'s copies ([module copies](../formats/compiler.md#vtables)) and ending in zero fill after its last return ([where object files meet](../formats/compiler.md#where-object-files-meet-in-the-code)):

| Code | Opens with | Holds | Ends |
|---|---|---|---|
| `00038f0c`–`000397ef` | `HeapPtr_DtorShieldsGaugeCopy` (`00038f0c`) | [the shield display](#the-shield-display) | `ShieldFacing_FlushDeferredPaint`'s `RET 4` at `000397ec`, then one zero byte |
| `000397f0`–`00039bb3` | `HeapPtr_DtorEnergyGaugeCopy` (`000397f0`) | [the energy meter](#the-energy-meter) | `PanelGauge_OnChildClickNoOpEnergyGaugeCopy`'s `RET 0xc` at `00039bb0`, then one zero byte |

The run at `00039bb4` follows, opening [the MFD display's file](mfd-display.md#where-the-object-file-starts-and-ends). Neither file has a static initializer. `es2_xref.py` finds no reference to either destructor copy ([Open](#open)). The shield display's tables lie from `00093e12` to `00093ed5`, after its ring color at `00093e04`, and the energy meter's from `00093ed6`, followed by its string `edg` (`00093eee`); the next file's copy of `ConfigPart`'s table is at `00093ef6`.

## The shield display

### The display object

**`ShieldsGauge` is `0x95` bytes, ES2's (`0xd4`) without the power-up fields, its vptr after the gauge base's data.**

| Offset | Holds | ES2's |
|---|---|---|
| `+0x00` | the rect, the block's first 16 bytes as `PanelGauge_Ctor` copies them | `+0x04` |
| `+0x10` | the cockpit | `+0x14` |
| `+0x14`, `+0x18` | the front and the rear facing | `+0x18`, `+0x1c` |
| `+0x64` | the facing count, 2 | `+0x68` |
| `+0x75` | the paint count, set to 2 at each change | `+0xb0` |
| `+0x76` | the vptr, `ShieldsGaugeVtable` (`00093e12`) | `+0x00` |
| `+0x7e`–`+0x8c` | the state block, 15 bytes: the front and the rear reading `+0x7e` and `+0x82`, each the charge as (charge << 10) / capacity; the balance `+0x86`, `0x200` even; a byte `+0x8a`; and the front and the rear press flags `+0x8b` and `+0x8c` | `+0xb5`–`+0xc3` |
| `+0x8d`, `+0x91` | the front and the rear readout's label | `+0xc4`, `+0xc8` |

`PanelGauge_Ctor` clears the gauge base's dropout fields (`+0x68`–`+0x74`, [sensor dropout](cockpit-widgets.md#sensor-dropout)), and no function of this file ticks them.

`ShieldsGaugeVtable` holds, from `+4`: `ShieldsGauge_OnClick` (by its padding `00039252`), `ShieldsGauge_Paint` (`00039175`), `ShieldsGauge_Update` (`000391aa`) and `PanelGauge_KeyDispatchNoneShieldsGaugeCopy` (`0003916f`), the code of `PanelGauge_KeyDispatchNone`: no key reaches the display. `Cockpit_DrawFrame` calls the update once a frame and `CockpitView_InvalidateWidgets` the paint at a view change ([drawing the cockpit](cockpit-panel.md#drawing-it)).

### Building it — `ShieldsGauge_Ctor` (`00038f34`)

**`ShieldsGauge_Ctor`**, (gauge, cockpit, block), from `Gau_ShieldDisplayWidget` (`0002cd74`) with the `.GAU` block at 616, allocates `0x95` bytes when given none, runs `PanelGauge_Ctor`(gauge, cockpit, block) and stores `ShieldsGaugeVtable` at `+0x76`. With (*x*, *y*) the block's first two `int32`, for each facing *i*, 0 the front and 1 the rear:

1. builds the facing with `ShieldsGauge_FacingCtor`(0, gauge, the block's rect at `+0x10` + 16*i* moved by (*x*, *y*)), stores it at `+0x14` + 4*i* and registers it with `Widget_RegisterClickable`(facing, the root widget at `CockpitViewInstance`'s `+0`);
2. gives it frames 58 + 3*i*, 59 + 3*i* and 60 + 3*i* of `dba\pbuttons` (`CockpitPButtonsBank`) at its `+0x30`, `+0x34` and `+0x38`;
3. builds its readout with `Label_Ctor`(0, `ColorSchemePanels`[1], the font `cpgreen`), places it with `Label_SetRect`(label, 2, the block's rect at `+0x30` + 16*i* moved by (*x*, *y*), margin (0, 0)), centered, sets its fill color to `0x3f` and stores it at `+0x8d` + 4*i*.

It then stores 2 at `+0x64`; stores at `000b13ec` the palette entry {25, 59, 23} with 1 in its fourth byte, from `ShieldRingColors` (`00093e04`); zeroes the state block (`memset`(`+0x7e`, 0, 15)) and runs `ShieldsGauge_UpdateRingPalette`, which installs all six rings at {0, 9, 0}, an empty facing's colors. ES2's (`004434fc`) builds the same two facings and labels, its labels in another font on another color, and gives the facings no pictures.

**When the cockpit first appears the display already shows the machine's shields: 100 and 100 and, for an even capacity, every ring at {0, 34, 0}.** The constructor leaves both readings and the balance 0, which the readouts would show as 0 and 200. `Sim_StartMission` runs `Player_PerFrameCockpitUpdate` once after building the cockpit and before its first `CockpitView_SetView` ([the mission start](main-loop.md#the-mission-start--sim_startmission-0005a9dc), step 13), and its `Shield_BalanceInputRead` hands the display the shields as `Shield_Init` left them ([the shields](shields-and-behaviour-states.md#the-shields)): each reading ((capacity >> 1) << 10) / capacity, `0x200` for an even capacity, the balance `0x200`, and the paint count 2. The view change's repaint, `ShieldsGauge_Paint`, is the display's first paint, so no paint shows the zeroed block.

### The facings — `ShieldsGauge_FacingCtor` (`000396ac`)

**Each facing is a button that shows one of three pictures by its state.** The object is `0x3c` bytes: the rect `+0`, the state `+0x17` (0 up, 1 pressed), the drag flag `+0x19`, 0, the paint count `+0x1b`, the vptr `+0x1c`, the gauge at `+0x20` and the mixin's vptr at `+0x24` ([the gadget bases](cockpit-widgets.md#the-gadget-bases)), 0 at `+0x28`, and the three pictures at `+0x30`, `+0x34` and `+0x38`.

`ShieldsGauge_FacingCtor`, (facing, gauge, rect), allocates `0x3c` bytes when given none, returning 0 when that fails, runs `Widget_CtorRect` on the rect and stores the file's copy of the button base table (`CTLButtonControlVtableShieldsGaugeCopy`, `00093e2e`) and 0 at `+0x19`; then the mixin's table `00093e56`, the gauge at `+0x20`, `PanelSelectGadget`'s table `00093e6e`, 0 at `+0x28` and the state 0; then `ShieldsSelectGadgetVtable` (`00093eae`) and its mixin table `ShieldsSelectGadget_PanelGadgetVtable` (`00093e96`). ES2's (`00444aec`) runs the same chain.

| Slot | `ShieldsSelectGadgetVtable` | The mixin's table |
|---|---|---|
| `+4` | `ShieldFacing_Paint` (by its padding `00039782`) | `ShieldsSelectGadget_PaintThunk` (`0003976c`) |
| `+8` | `Widget_InvalidateDeferredShieldsGaugeCopy` (`0003975c`, by its padding `0003975b`): `+0x1b` = 2 | `ShieldsSelectGadget_FlushDeferredPaintThunk` (`00039778`) |
| `+0xc` | `Widget_ForwardClickToOwner` (`000314ac`) | `Widget_ClickSound` (`000314a0`) |
| `+0x10` | `ShieldFacing_FlushDeferredPaint` (by its padding `000397b6`) | |
| `+0x14`–`+0x1c` | the file's copies of the button base's value, set-value and drag slots (`00039747`, `00039751`, `00039757`) | |

The two thunks, frameless with no Ghidra function, add −`0x20` to the object and jump to the paint and the flush. The button base table's copy holds the file's copies of `panel.cpp`'s seven no-op slots ([`panel.cpp`'s copies](cockpit-panel.md#functions)), `00039735` to `00039757`.

- **`ShieldFacing_Paint` (`00039784`)**, (facing): when `CockpitView_RectCornerVisible` passes its rect, blits the picture *state* (`+0x30` + 4 × the state) at its top-left with `Bitmap_Blit`. So the front button shows frame 58 up and 59 pressed, the rear 61 and 62. ES2's (`00444b5c`) only makes the visibility test.
- **`ShieldFacing_FlushDeferredPaint` (`000397b8`)**, (facing): with the paint count at 2, calls its paint (slot `+4`) and takes 1 off; at 1, copies its rect to the screen with `Widget_DrawToCockpit`(0, facing, 0) and takes 1 off. ES2's (`00444b70`) paints nothing at 2.

A click with the left button ([press, release and drag](cockpit-mouse.md#press-release-and-drag)) reaches `Widget_ForwardClickToOwner`, which calls the gauge's `+4` with (gauge, flags, facing), then the facing's invalidate and the click sound (`0x11`).

### The click — `ShieldsGauge_OnClick` (`00039254`)

**`ShieldsGauge_OnClick`**, (gauge, flags, facing), finds the facing among the gauge's `+0x64` facings and sets the front press flag `+0x8b` and clears `+0x8c` for the front, the reverse for the rear; it does not test the flags. ES2's (`0044380c`). The next `Player_PerFrameCockpitUpdate`, which `Sim_RenderFrame` runs after the frame's `Cockpit_DrawFrame` ([the frame](main-loop.md#the-frame), step 4), runs `Shield_BalanceInputRead`, which takes the flag, moves the machine's balance with `Shield_BalanceAdjust` and hands the block back through `ShieldsGauge_SetStateBlock` (`000392d0`) ([the shields](shields-and-behaviour-states.md#the-shields)). The setter sets the paint count to 2 when the readings or the balance changed, so the next frame's draw shows the move. A press moves the balance `0x66`, 102 of its `0x400`, toward the facing pressed, and the readouts 19 or 20: one front press from an even balance reads 119 and 81, and the readouts follow ES2's ladder ([ES2's readouts](../../retail/simulation/cockpit-hud-widgets.md#readouts--shieldsgauge_updatereadouts-00444a68)). The `[` and `]` keys move the balance without the gauge.

### Drawing the display

- **`ShieldsGauge_Paint` (`00039178`)**, (gauge), slot `+8`: `ShieldsGauge_UpdateRingPalette`, `ShieldsGauge_UpdateReadouts`, and the paint (slot `+4`) of both facings. ES2's (`00443730`) paints no facing.
- **`ShieldsGauge_Update` (`000391ac`)**, (gauge), slot `+0xc`, when `CockpitView_RectCornerVisible` passes the gauge's rect: with the paint count at 2, the flush (slot `+0x10`) of both facings, `ShieldsGauge_UpdateRingPalette` and `ShieldsGauge_UpdateReadouts`; at 1, the flush of both facings and `Widget_DrawToCockpit`(0, the label's rect `+4`, 0) for each readout; each takes 1 off the count. So a facing's press or release shows only in a frame in which the gauge's own count is 1 or 2, after a change of the readings or the balance. ES2's (`00443748`) runs only once the gauge is armed, runs the power-up fill until it completes, and at 1 only takes 1 off.

The gauge's rect is the block's first 16 bytes, (0, 0, 0, 0) in five of the eight retail files ([the block](../formats/gau-cockpit-layout.md#the-shield-display-block-at-616)), so in those the test is of the point (0, 0).

### The ring colors — `ShieldsGauge_UpdateRingPalette` (`00039340`)

**Each facing's three rings brighten as its charge rises, all three reaching the base color when the facing holds the whole capacity.** For each facing, with *v* its reading (`+0x7e` front, `+0x82` rear) and *f* = (*v* × 100) >> 9, from 0 to 200 for a reading from 0 to `0x400`, each ring's color is the base less a dimming *d*, a palette dword with red in its low byte, then green and blue, and 1 in its top byte: {25 − *d*, 59 − *d*, 23 − *d*}, a component below 1 stored as 0.

| Entry, front and rear | *d* |
|---|---|
| 82, 85 | (200 − *f*) >> 2 |
| 81, 84 | 0 above 150; (150 − *f*) >> 1 from 50 to 150; 50 below 50 |
| 80, 83 | 0 above 125; 125 − *f* from 25 to 125; 50 below 25 |

The shifts are arithmetic. It installs the six with `Palette_InstallRange`(colors, 6, `0x50`), entries 80 to 85, front first, each facing's in the order 80, 81, 82; they reach the display at the frame's end, in `Sim_EndFrame`'s flush of the palette's changed entries ([installing colors](../rendering/palette.md#installing-colors)). With full shields at an even balance, *f* is 100 on each facing and all six are {0, 34, 0}; an empty facing's are {0, 9, 0}. Entries 80 and 83 are {0, 0, 0} for *f* from 25 to 66, darker than below 25. The base is ES2's base color; ES2's ramp installs from entry 66 and has overcharged colors ([ES2's ring ramp](../../retail/simulation/cockpit-hud-widgets.md#ring-ramp--shieldsgauge_updateringpalette-004438f0)).

**The six rings are painted into the front canopy art as a column of horizontal bands, the front facing's above and the rear's below.** Each of the eight `db0\<chassis>.DB0` pictures holds pixels of entries 80 to 85, 126 to 348 of them, and a census of every bitmap bank's raw frames in `SIMVOL0.VOL` and `ZONES.VOL` found them in no other. From the top the rings run 82, 81, 80, then a gap, then 83, 84, 85: the front facing's three with entry 80 innermost, the rear's mirrored with 83 innermost. `ROADRUNN`'s column is an hourglass at rows 162–179 whose bands narrow toward the gap, 27 pixels wide at each end; `RAPTOR`'s is 7 pixels wide at columns 267–273, three rows a ring; `COLOSSUS`'s sits at the top of the picture, from row 4. So the ring a facing goes black on is the one beside the gap, while its reading is between an eighth and a third of the whole capacity (*f* 25 to 66).

### The readouts — `ShieldsGauge_UpdateReadouts` (`00039628`)

ES2's ([the readouts](../../retail/simulation/cockpit-hud-widgets.md#readouts--shieldsgauge_updatereadouts-00444a68)): when the front label's rect passes `CockpitView_RectCornerVisible`, *n* = the balance `+0x86` × 200 >> 10 goes to the front label and 200 − *n* to the rear, written with `ltoa` in base 10 and `Label_SetText`, which fills each label's rect with `0x3f` first ([labels](cockpit-widgets.md#labels)).

## The energy meter

### The meter object

**`EnergyPoolGauge` is `0x8c` bytes.**

| Offset | Holds | ES2's |
|---|---|---|
| `+0x00` | the bar's rect, the block's rect at `+0x10` moved by the block's first pair | `+0x04` |
| `+0x10` | the cockpit | `+0x14` |
| `+0x75` | the paint count | `+0xb0` |
| `+0x76` | the vptr, `EnergyPoolGaugeVtable` (`00093ed6`) | `+0x00` |
| `+0x7a` | the value, the pool as a Q10 fraction, 0 to `0x400` | `+0xb1` |
| `+0x7e` | the LED bar graph | `+0xb5` |
| `+0x88` | the bar's clip context, or 0 | `+0xbf` |

`EnergyPoolGaugeVtable` holds, from `+4`: `PanelGauge_OnChildClickNoOpEnergyGaugeCopy` (`00039bad`), `EnergyPoolGauge_Paint` (by its padding `00039a25`), `EnergyPoolGauge_Update` (`00039aba`) and `PanelGauge_KeyDispatchNoneEnergyGaugeCopy` (`00039a1d`), the code of `PanelGauge_OnChildClickNoOp` and `PanelGauge_KeyDispatchNone`.

### Building it — `EnergyPoolGauge_Ctor` (`00039818`)

**`EnergyPoolGauge_Ctor`**, (gauge, cockpit, block), from `Gau_EnergyMeterWidget` (`0002cd3c`) with the `.GAU` block at 548 ([the block](../formats/gau-cockpit-layout.md#the-energy-meter-block-at-548)), allocates `0x8c` bytes when given none, runs `PanelGauge_Ctor`(gauge, cockpit, block) and stores `EnergyPoolGaugeVtable` at `+0x76`. Then, with *b* the block's `int32` and (*x*, *y*) = (*b*[0], *b*[1]):

1. builds a `0x239`-byte render context with `GLViewport_Ctor`(0) and stores it at `EnergyMeter_ClipContext` (`000b13f0`) ([`Cockpit_CreateCanvasContext`](cockpit-panel.md#building-it) describes what it copies);
2. sets the rect `+0` to (*b*[4] + *x*, *b*[5] + *y*, *b*[6] + *x*, *b*[7] + *y*) and the value `+0x7a` to 0;
3. with *b*[9] 0, stores 0 at `+0x88`; otherwise sets the context's clip mode `+0x208` to 2 and its translation `+0x20c` to 1, loads `edg\` + `EnergyMeter_ClipFileNames` (`000932c8`)[*b*[9]], `ENG_CLP1` to `ENG_CLP4`, into `EnergyMeter_ClipRegions` (`000b13f4`) with `CockpitClipRegions_Load` ([the `edg\` files](../formats/hd-ed-clip-regions.md#which-files)), adds the rect's *y0* to each region's first row and its *x0* to each span's two columns, reading each region's data as a span block's (each retail `ENG_CLP` file holds span blocks alone), copies the `0x204`-byte region list over the context's `+4`, its clip block, and stores the context at `+0x88`;
4. with *b*[8] 3, builds the bar with `LedBarGraph_Ctor`(0, `0x400`, the rect `+0`), an `LEDBarGraphH`; with 4, with `LEDBarGraphV_Ctor` ([LED bar graphs](cockpit-widgets.md#led-bar-graphs)); gives it the shades *b*[10] and *b*[11] at its `+0x2c` and `+0x30` and the unlit color `0x3f` at its `+0x20`, and stores it at `+0x7e`. Every retail file holds 3 or 4.

The bar's rect is the gauge's own. Each retail rect's far corner lies right of and below its near one, so the span is positive and an `LEDBarGraphH` fills from the left and an `LEDBarGraphV` from the bottom ([the bar graph base](../../retail/simulation/cockpit-hud-widgets.md#the-bar-graph-base), [ES1's fill edge](cockpit-widgets.md#bar-graphs)). ES2's (`00444d5c`) always builds an `LEDBarGraphH`, over a rect its wrapper has shifted into device pixels, takes its shades from `HudColorTable`, and stores 0 at `+0xbf`.

### Drawing the meter

- **`EnergyMeter_SetValue` (`00039b78`)**, (gauge, &value): stores the value at `+0x7a` and sets the paint count `+0x75` to 2 when it differs from the one there. `Player_PerFrameCockpitUpdate` passes (pool << 10) / 10000 through `EnergyMeter_Value` (`000af6fc`) each frame ([the HERC's cockpit](mech.md#the-cockpit)). ES2's (`004450d0`).
- **`EnergyPoolGauge_Update` (`00039abc`)**, (gauge), slot `+0xc`, when `CockpitView_RectCornerVisible` passes the bar's rect (`BarGraph_GetRect`) and the paint count is 2: with a clip context, copies the installed render context's rect `+0x210`–`+0x21c` and origin `+0x220`, `+0x224` into it and installs it with `Raster_InstallRenderContext`, so the bar is drawn through the clip shape at the canvas context's position; calls the bar's paint (slot `+4`) with the value, which draws the strip between the old position and the new; copies the gauge's rect to the screen with `Widget_DrawToCockpit`(0, gauge, 1); puts the context back; and sets the count to 0. The bar's rect is the last strip its paint or repaint drew, since both write the strip's ends into the rect on the bar's axis and leave them there ([LED bar graphs](cockpit-widgets.md#led-bar-graphs)), so the test passes when a corner of that strip is on screen; while it fails the count stays 2, and the bar is painted to the value in the first frame it passes.
- **`EnergyPoolGauge_Paint` (`00039a28`)**, (gauge), slot `+8`, at a view change, under the same visibility test and context: calls the bar's repaint (slot `+8`) with (bar, 0, the value), which draws the whole bar, and copies nothing to the screen. So a view change redraws an `LEDBarGraphH` at the value through `LedBarGraph_RepaintToValue` and an `LEDBarGraphV` at its last painted position through `LedBarGraphV_Repaint`.

**Through a clip context the bar is cut to the `ENG_CLP` shape.** The context is in clip mode 2 with its translation on, and holds the installed context's rect and origin, so its translation is the rect's corner less the origin. The lit strip's pinstripe lines go through `Raster_DrawLine`, each moved by the translation, cut to the rect and then to each span region of the list ([lines](../rendering/screen-pages.md#lines-and-clipped-bitmaps)); the unlit strip goes through `Raster_FillRect`, its rect moved the same way and intersected with each region ([rectangles](../../retail/rendering/polygon-fill.md#rectangles)). The regions themselves are not moved: the constructor placed them on the bar's rect when it loaded them.

ES2's paint and update (`00444f20`, `00444fe0`) install a temporary brush {0} at the render context's `+0x22c` first and call the bar's paint in both. `EnergyPoolGauge_ReturnZero` (`00039ba8`), frameless, `XOR EAX,EAX` and `RET 4`, stands between the setter and the vtable's first slot as ES2's (`00445100`) does; `es2_xref.py` finds no reference to it ([Open](#open)).

## Functions

| ES1 | Address | ES2 |
|---|---|---|
| `HeapPtr_DtorShieldsGaugeCopy`, `HeapPtr_DtorEnergyGaugeCopy` | `00038f0c`, `000397f0` | the run's first destructor ([module copies](../formats/compiler.md#vtables)) |
| `ShieldsGauge_Ctor`, `PanelGauge_KeyDispatchNoneShieldsGaugeCopy`, `ShieldsGauge_Paint`, `ShieldsGauge_Update`, `ShieldsGauge_OnClick` | `00038f34`, `0003916f`, `00039178`, `000391ac`, `00039254` | `004434fc`, —, `00443730`, `00443748`, `0044380c` |
| `ShieldsGauge_SetStateBlock`, `ShieldsGauge_GetStateBlock` | `000392d0`, `00039334` | `00443858`, `004438e0` ([the shields](shields-and-behaviour-states.md#the-shields)) |
| `ShieldsGauge_UpdateRingPalette`, `ShieldsGauge_UpdateReadouts`, `ShieldsGauge_FacingCtor` | `00039340`, `00039628`, `000396ac` | `004438f0`, `00444a68`, `00444aec` |
| `CTLButtonControl_PaintNoOpShieldsGaugeCopy`, `CTLControl_InvalidateNoOpShieldsGaugeCopy`, `CTLControl_OnClickNoOpShieldsGaugeCopy`, `CTLControl_FlushDeferredPaintNoOpShieldsGaugeCopy`, `Widget_GetButtonValueShieldsGaugeCopy`, `CTLControl_SetValueNoOpShieldsGaugeCopy`, `CTLControl_DragMoveNoOpShieldsGaugeCopy` | `00039735`, `0003973b`, `0003973f`, `00039743`, `00039747`, `00039751`, `00039757` | the code of `panel.cpp`'s copies ([its functions](cockpit-panel.md#functions)) |
| `Widget_InvalidateDeferredShieldsGaugeCopy`, `ShieldsSelectGadget_PaintThunk`, `ShieldsSelectGadget_FlushDeferredPaintThunk` | `0003975c`, `0003976c`, `00039778` | `Widget_InvalidateDeferred` (`00455524`), — |
| `ShieldFacing_Paint`, `ShieldFacing_FlushDeferredPaint` | `00039784`, `000397b8` | `00444b5c`, `00444b70` |
| `EnergyPoolGauge_Ctor`, `PanelGauge_KeyDispatchNoneEnergyGaugeCopy`, `EnergyPoolGauge_Paint`, `EnergyPoolGauge_Update` | `00039818`, `00039a1d`, `00039a28`, `00039abc` | `00444d5c`, —, `00444f20`, `00444fe0` |
| `EnergyMeter_SetValue`, `EnergyPoolGauge_ReturnZero`, `PanelGauge_OnChildClickNoOpEnergyGaugeCopy` | `00039b78`, `00039ba8`, `00039bad` | `004450d0`, `00445100`, `EnergyPoolGauge_OnChildClickNoOp` (`00445108`) |

## Globals

| | Address |
|---|---|
| `ShieldRingColors`, the base color's three `int32`, 25, 59 and 23 | `00093e04` |
| `ShieldsGaugeVtable` | `00093e12` |
| `CTLButtonControlVtableShieldsGaugeCopy`, then the tables `ShieldsGauge_FacingCtor` stores in turn, `00093e56` and `00093e6e` | `00093e2e` |
| `ShieldsSelectGadget_PanelGadgetVtable`, `ShieldsSelectGadgetVtable` | `00093e96`, `00093eae` |
| `EnergyPoolGaugeVtable` | `00093ed6` |
| `EnergyMeter_ClipFileNames`, five name pointers, the first an empty string | `000932c8` |
| `ShieldRingBaseEntry`, the palette entry `ShieldsGauge_Ctor` stores | `000b13ec` |
| `EnergyMeter_ClipContext`, `EnergyMeter_ClipRegions` (an `int32` count and 128 region pointers) | `000b13f0`, `000b13f4` |
| `EnergyMeter_Value` | `000af6fc` |

## Open

- **Open:** a retail check of the rings: with the balance even, let a facing's shields fall below a third and watch its ring beside the gap go black, and below an eighth come back dim green.
- **Open:** what reads `ShieldRingBaseEntry` (`000b13ec`): `es2_xref.py --decode` finds the constructor's store and no read (its positive control, `EnergyMeter_ClipContext` beside it, finds its four reads).
- **Open:** what the state block's byte `+0x8a` (block byte `0xc`) holds; `ShieldsGauge_SetStateBlock` copies it and this file reads it nowhere else.
- **Open:** what calls `HeapPtr_DtorShieldsGaugeCopy`, `HeapPtr_DtorEnergyGaugeCopy` and `EnergyPoolGauge_ReturnZero`, to none of which `es2_xref.py` finds a branch or stored pointer.
- **Open:** a positive control in the running game for the done checklist ([when a module is done](../../herculan/plan-es1-support.md#when-a-module-is-done)). Proposed: in DOSBox-X's debugger during a mission, `BP <CS>:00039254` fires on a click of either shield button, with the facing's address at `[ESP+0xc]`; and `BP <CS>:00039b78` fires every frame, with the value at the dword `[ESP+8]` points at falling as weapons fire.
