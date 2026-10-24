# Cockpit console widgets: HUD sprite banks and the gauges

Reverse-engineered from `DBSIM.EXE` in the `ES2Recon` Ghidra project. All addresses are DBSIM unless noted. Symbols are in `tools/ghidra_scripts/known_symbols_dbsim.json`; apply with `ES2ApplySymbolNames.java`.

Verified against retail data in `ES2/VOL/simvol0/{hba,gau,dat}/`.

The view manager and canvas these widgets are drawn onto: [`cockpit-views.md`](cockpit-views.md). Canopy art and the palette a flash swaps: [`../rendering/cockpit-canopy-palette.md`](../rendering/cockpit-canopy-palette.md). The front-window gunsight complex (a separate widget tree): [`cockpit-gunsight-hud.md`](cockpit-gunsight-hud.md). How a mouse click on any of these widgets reaches its own click handler: [`cockpit-input.md`](cockpit-input.md).

The files behind them: the per-herc widget geometry is [`gau\<HERC>.GAU`](../formats/gau-cockpit-layout.md), the colour ids the gauges carry resolve through [`dat\COLORS.DAT`](../formats/colors-dat.md), and the damage diagram is [`.PDG`](../formats/pdg-paper-doll.md).

## The cockpit object

**One object holds the whole cockpit: every console instrument, the two message boxes, the cursors and the canvas they are drawn on. It is built once a mission, as the cockpit's views are loaded, and repaints its instruments every frame.** `PANEL.CPP` owns it; the view manager builds it in step 4 of its [bring-up](cockpit-views.md#cockpitviewmanager_loadviews-sequence), and the first one built is `CockpitViewInstance` (`0049b088`).

### Building it

1. `CockpitView_Ctor` (`00431008`), (cockpit, machine): stores the machine at `+0x203` and the `int16` `+0x6c` of its type record (`+0x1f2`) at `+0x201`, sets `+0x214` to `maybe_GlobalLiveObjectList` and the `int16` `+0x218` to 0, and runs `ColorSchemePanels_LoadAll`. It then allocates a 16-byte array at `004d0490` and four 12-byte records at `004d0494`, each {0, a 16-byte array, a 16-byte array}, and returns the cockpit.
2. `ColorSchemePanels_LoadAll` (`00431098`), (cockpit): hands `SimStrings_LoadAll` the path `Language_StringFilePath` makes of `strings0` and `str`; the first time it runs, it loads the [fonts](#hud-fonts) and the seven cursors from `dci\` into `Cockpit_CursorImages` through `Cursor_LoadWrapper` (`00430fb0`), putting `PCURSOR` in the forward view's slot `+0x236` and `CURSOR` in the other views' slot `+0x23a` ([cursor rendering](cockpit-input.md#9-cursor-rendering)). It zeroes the three `g_SquadmateMachines`, calls `_memset`(cockpit + `0xa20`, `0x284`, 0), stores the cockpit in `CockpitViewInstance` when that is 0, clears the widgets-off byte `+0x20f`, the active cursor slot `+0x226`, `+0x241`, `+0x245` and `+0x26a`, and sets the `int16` `+0x23f` to −1.
3. `Gau_BuildCockpitWidgets` (`00431bf8`), (cockpit, herc name): loads `gau\<herc>` into `CockpitGauRecord` with `Gau_Load` ([the layout](../formats/gau-cockpit-layout.md)); makes the canvas context over {0, 0, `CockpitCanvasWidth` − 1, `004d25d6` − 1} at `+4`; builds the root widget, a `0x398`-byte `CTLWindow` over the record, at `+0`; then `SystemButtons_Ctor`, the pilot channel at `+0x207` and the ticker at `+0x20b` ([the message boxes](cockpit-messages.md)), the seven instruments below, the coordinate shifts of the record's remaining rects, the weapon rows (`WeaponMounts_BuildGauges`) and the [flyer branch](#power-up-sequence). The order is the cockpit's [click precedence](cockpit-input.md#registration-order-is-precedence).

`Cockpit_CreateCanvasContext` (`00430ea0`), (rect), makes the canvas context: a `0x239`-byte `GLViewport` with clip mode `+0x208` = 1, translation `+0x20c` = 1, the rect at `+0x210`–`+0x21c`, the byte `+0x230` and the dword `+0x231` cleared and `+0x235` = 200. It stores the context at `004d042c` and returns it.

Each instrument's wrapper, (cockpit, `.GAU` record), shifts its block into device pixels, allocates and constructs the instrument with (block, cockpit), appends it to the cockpit's widget list (`+8`, its count at `+0x6c`) and returns it:

| Field | Wrapper | Block | Size | Constructor |
|---|---|---|---|---|
| `+0x1e1` | `Gau_WeaponButtonsWidget` (`00432340`) | 468 | `0xb5` | `ConsoleButtons_Ctor`, then `ConsoleButtons_SetChainNumber`(2); the wrapper also sets the byte `+0x1e0` to 1, `+0x1d4` to 0 and `+0x1d8` to the dword at `+0x16c` |
| `+0x1e5` | `Gau_EnergyMeterWidget` (`004323e0`) | 548 | `0xc3` | `EnergyPoolGauge_Ctor` |
| `+0x1e9` | `Gau_ShieldDisplayWidget` (`00432454`) | 616 | `0xd4` | `ShieldsGauge_Ctor` |
| `+0x1ed` | `Gau_MfdPanelWidget` (`004324c8`) | 728 | `0x349` | `MfdDisplay_Ctor`, then `MfdDisplay_SetMode`(3) |
| `+0x1f1` | `Gau_ThrottleWidget` (`0043254c`) | 1000 | `0xca` | `ThrottleGauge_Ctor` |
| `+0x1f5` | `Gau_RovingGunsightWrapper` (`004325c0`) | 1088 | `0x11b` | `Gau_RovingGunsightWidget` |
| `+0x1f9` | `Gau_PilotRosterWidget` (`00432634`) | 1212 | `0x78a` | `HddDisplay_Ctor` |

**The weapon rows.** Every row's factory ([weapon-row rects](../formats/gau-cockpit-layout.md#weapon-row-rects)) ends in `CockpitView_RegisterWeaponGauge` (`00432018`), (cockpit, gauge, row): a row of −1 takes the first empty one of the ten slots at `+0x70`; the gauge goes to `+0x1d8` when that is 0, onto the widget list and into its row's slot, and the list count and the row count `+0x98` each go up by 1.

**The squadmates.** `Cockpit_LoadSquadmatePilots` (`00431530`), (cockpit), finds the player's machine (byte `+0xa3` set) in `GlobalMechList` with `Pool_Prev`, keeps its group (`+0x45`) at `0049b0f8`, and takes up to three of the group's members (the array `+0xc`, `int16` count `+0x10`, stopping at a null) other than the cockpit's own machine into `g_SquadmateMachines`. For each it calls `HddGauge_LoadPilotFrames`(`+0x1f9`, slot, the member's `int16` `+0x29c`), then `MfdDisplay_SetStatusRoster`(`+0x1ed`, count). `Squad_SendOrderToSlot` (`00431610`), `Squad_BroadcastOrderFromCockpit` (`0043166c`) and `Squad_CallsignOf` (`00431690`) read that list.

| Offset | Holds |
|---|---|
| `+0x00`, `+0x04` | the root widget, the canvas context |
| `+0x08` | the widget list, its count at `+0x6c` |
| `+0x70` | the ten weapon-row gauges, their count at `+0x98` |
| `+0x1e1`–`+0x1f9` | the seven instruments above |
| `+0x201`, `+0x203` | the chassis' `int16` `+0x6c`, the machine |
| `+0x207`, `+0x20b` | the pilot channel and the ticker |
| `+0x20f` | 1 while the widgets are off (the external view) |
| `+0x210` | the target selection |
| `+0x21a`, `+0x21e`, `+0x222` | the [screen-edge strips](cockpit-input.md#10-the-screen-edges-are-three-widgets) |
| `+0x226`, `+0x232`–`+0x23a` | the active cursor slot and the three slots |
| `+0x23e` | set once the screen-edge strips are built |
| `+0x241`, `+0x245` | the [power-up](#power-up-sequence)'s |

### Drawing it

`Cockpit_PushCanvasContext` (`004311e0`) adds 1 to the index byte `0049b106`, saves the installed render context (`006c5ff4`) at that index of the stack `004d0470`, installs the cockpit's canvas context `+4` with `Raster_InstallRenderContext` and returns it; `Cockpit_PopRenderContext` (`00431210`) reinstalls the saved one and takes 1 from the index.

`CockpitView_PerFrameUpdate` (`004327ac`), (cockpit), from `Sim_RenderFrame` ([per-frame ordering](#per-frame-ordering)), does nothing while the byte `0049b104` is set. Otherwise:

1. the first time, it calls `CockpitView_BuildScrollTriggers` ([the screen-edge strips](cockpit-input.md#10-the-screen-edges-are-three-widgets)) and sets `+0x23e`;
2. until `+0x245` is set, `Cockpit_PowerUpTick`; then `Cockpit_HitShakeTick` and `Cockpit_StepKickTick` ([the damage shake](../rendering/cockpit-canopy-palette.md#the-damage-shake));
3. with the widgets on: slot `+0x14` of the pilot channel `+0x207`; between `Cockpit_PushCanvasContext` and `Cockpit_PopRenderContext`, slot `+8` of each of the ten weapon rows that is set and of `+0x1e1`, `+0x1e5`, `+0x1e9`, `+0x1f1`, `+0x1ed` and `+0x1f9`; `Cockpit_UpdateEdgeCursor`; with the target selection `+0x210` set, clears it when `TargetSelect_CanTarget`(cockpit, selection) returns 0 ([losing the selection](target-selection.md#losing-the-selection--cockpitview_perframeupdate)); `WidgetRoot_ServicePressFlashes` on the root; `NavMarker_Tick`;
4. with the widgets off, slot `+8` of `+0x1f9` alone.

The gunsight complex `+0x1f5` is not among them; it is one of the dispatches `Sim_RenderFrame` makes after `Player_PerFrameCockpitUpdate` ([per-frame ordering](#per-frame-ordering)).

## HUD sprite art — `.HBA`/`.DBA`

A bank ships twice under the same name: `dba\NAME.DBA` for the 320-wide mode and `hba\NAME.HBA` for the 640-wide one, exactly 2x on both axes, frame for frame, with identical frame counts. The two folder-name literals sit adjacent to each bank name in `.rdata` (`"NAME\0hba\0dba\0"`). The exceptions: `corners` is hardcoded to `dba` and `hba\CORNERS.HBA` does not exist; `static` and `pilot<n>` have no `hba\` copy, below; and `hdd`'s two copies hold the same frames at sizes that are not 2x ([`heads-down-display.md`](heads-down-display.md#frame-to-widget-confirmation)).

Load path: `ResourcePath_BuildFolderName(name, folder)` → `Resource_Load` (`0045cdd8`) → `ClassItem_LoadResource`. The bank container: [`../formats/dfn-hfn-dci.md`](../formats/dfn-hfn-dci.md).

| Bank | Owning function | Role |
|---|---|---|
| `hud` | `Gau_RovingGunsightWidget` (`0043c7d8`) | gunsight / reticle — see [`cockpit-gunsight-hud.md`](cockpit-gunsight-hud.md) |
| `hudhtick` | `HudHeadingTape_Ctor` (`0043b57c`) | heading tick tape — see [`cockpit-gunsight-hud.md`](cockpit-gunsight-hud.md#heading-tape) |
| `mfd`, `mfd_dmg`, `radar` | `MfdDisplay_Ctor` (`00445218`) | multi-function display — see [`mfd.md`](mfd.md) |
| `hdd`, `static`, `pilotN` | `HddDisplay_Ctor` (`00448cc8`), `HddGauge_LoadPilotFrames` (`0044a7c0`) | heads-down display — see [`heads-down-display.md`](heads-down-display.md) |
| `pweapons`, `wpn_dmg` | `WeaponGauge_Ctor` (`0044080c`) | weapon hardpoint plates |
| `throttle` | `ThrottleGauge_Ctor` (`00447b84`) | throttle slider knob |
| `sysbuttn`, `icons`, `corners` | `SystemButtons_Ctor` (`00434368`), `HddMarker_Ctor` (`0044f130`), `CockpitFontsAndCorners_Init` (`004544a4`) | |

The class names in these symbols (`ThrottleGauge`, `WeaponGauge`, …) are the classes' own, read from their Borland class records ([`../formats/borland-rtti.md`](../formats/borland-rtti.md)). [`cockpit-input.md`](cockpit-input.md#the-cockpits-own-gadget-classes) has the clickable-widget hierarchy.

Frame-to-state mapping: `PWEAPONS` 0/1 are the selected/unselected row plate, 2/3 the unlit/lit console-button plate, 4/5/6 the hardpoint state box (green / red / amber), 7 a 640x80 strip ([Open](#open)); `WPN_DMG`'s frame 0 is the row underlay, a flat 112x14 plate in the row background `0x2e`, and frames 1-9 a weapon row's [sensor-dropout wipe](#the-wipes); `MFD_DMG`'s seven 192x118 frames are the MFD's; `THROTTLE` 0 is a 2x12 tick and 1 the 28x12 knob; `RADAR`'s 10 110x110 frames are the sweep animation; `MFD` 0-2 are 196x122 screen chrome, 3-10 five button plates in unlit/lit pairs (see [`mfd.md`](mfd.md)); `HUD` 0 is the 45x45 reticle, 11 the 182x10 rotation-indicator track and 12/13 its 62x4 yellow and green bars (sizes in the 640-wide `hba\` banks; `dba\` is exactly half).

**`static` and `pilot<n>` ship in `dba\` only**, so the 640-wide mode has no matching art for them; see [`heads-down-display.md`](heads-down-display.md).

## LED gauges

**A bar graph is a meter that fills a rect from one edge in proportion to a value, lit up to the value and unlit beyond it.** `LEDBarGraphH`/`LEDBarGraphV` are the two the cockpit draws, built on a plain bar graph base.

### The bar graph base

`BarGraph_Ctor` (`0043907c`) copies the rect `{x0, y0, x1, y1}` to `+4`, and `BarGraph_GetRect` (`004390b8`) returns its address. `LedBarGraph_CtorBase` (`004390c4`), (bar, rect, range), makes the bar a `BarGraphH` (vtable `0049bd60`), and `LedBarGraph_CtorV` (`00439344`) a `BarGraphV` (`0049bd48`), `0x2c` bytes each:

| Offset | Holds |
|---|---|
| `+0x14` | the span, `(end − start) × 0x10000 / range`: 16.16 pixels per unit of value, *range* the constructor's `int16` |
| `+0x18`, `+0x1c` | start and end: the rect's `x0` and `x1` (`BarGraphH`) or `y0` and `y1` (`BarGraphV`) |
| `+0x20` | the fill position |
| `+0x24`, `+0x28` | the unlit and lit colors |

The span's sign picks the edge the bar fills from. A `BarGraphH` fills from start when the span is 1 or more and from end otherwise; a `BarGraphV` fills from end, its bottom, when the span is 0 or more and from start otherwise (its repaint and reset test 1 or more, as `BarGraphH`'s do). Each class has three slots:

- **`+0`, paint to value v** (`BarGraphH_PaintToValue`, `00439118`; `BarGraphV_PaintToValue`, `00439398`): the position p is `start + (v × span >> 16)` filling from start, `end − (v × span >> 16)` filling from end. It fills start to p in the lit color and p + 1 to end in the unlit, or p to end lit and start to p − 1 unlit, with `Raster_FillRect` on the rect at `+4`, and stores p at `+0x20`. `BarGraphV_PaintToValue` writes those positions into the rect's `x0` and `x1` rather than `y0` and `y1`, so it would fill a band across x over the rect's whole height; the throttle's two `BarGraphV`s are the ones built, and they are never drawn ([below](#throttle-gauge)).
- **`+4`, repaint the run from a to b** (`BarGraphH_RepaintSpan`, `004391e0`; `BarGraphV_RepaintSpan`, `00439460`): orders a and b and clamps them to start..end as lo..hi, then fills lo..hi against the stored position p, the lit part with the brush the render context already holds (the display's `+0x44`, its `+0x228`) and the unlit part in `+0x24`. Wholly on the unlit side of p, the run is unlit; wholly on the lit side, lit. A run that p splits is filled lo..p and p..hi: `BarGraphV`'s lights the side its paint lights, and `BarGraphH`'s the other side, in both directions.
- **`+8`, reset** (`BarGraphH_ResetToEmpty`, `0043a211`; `BarGraphV_ResetToEmpty`, `0043a190`): the position becomes the edge the bar fills from, as each constructor also sets it.

### LED bar graphs

**An LED bar graph lights its filled part in two shades interleaved a pixel apart, which read as one shaded fill.** `LEDBarGraphH` (`0x34` bytes by its class record) and `LEDBarGraphV` (`0x3c`) are a `BarGraphH` and a `BarGraphV` with the two shades at `+0x2c` and `+0x30`. `LedBarGraph_Ctor` (`004395c4`), (bar, rect, range), runs `LedBarGraph_CtorBase` and installs `LEDBarGraphH`'s vtable (`0049bd30`); `LEDBarGraphV_Ctor` (`00439834`) runs `LedBarGraph_CtorV` and installs `0049bd18`. Neither sets a color: the builder writes `+0x24`, `+0x2c` and `+0x30` afterwards.

The lit run is the pinstripe. `LedBarGraph_FillPinstripe` (`00439758`), (bar, rect), walks the rect's x range twice, once over even columns and once over odd, drawing a full-height line (`Raster_DrawLine` from *y0* to *y1*) at each, both ends of the range included: `+0x2c` paints even columns, `+0x30` odd, each through a temporary pen {0, shade} in the current render context, the old pen put back after. `LedBarGraphV_FillPinstripe` (`00439a0c`) draws full-width lines on alternate rows instead, `+0x2c` on even rows and `+0x30` on odd. The columns are device pixels, since the energy meter's rect is shifted before its bar is built (below), so each stripe is one device pixel wide in every video mode.

The slots replace the base's paint and repaint; the reset is the base's:

- **Paint to value** (`LedBarGraph_PaintToValue`, `004395e8`): `BarGraphH_PaintToValue`'s runs, the lit one through the pinstripe and the remainder with `Raster_FillRect` after the installed brush's color is swapped for `+0x24` (put back after). With the span at 1 or more that is start to `start + (value * span >> 16)` (16.16 fixed point) lit and the next column to end unlit. `LedBarGraphV_PaintToValue` (`00439858`) is `BarGraphV_PaintToValue`'s, the lit run through `LedBarGraphV_FillPinstripe`, and like it writes the positions into the rect's *x0* and *x1* (`+4`, `+0xc`).
- **Repaint** (`LedBarGraph_RepaintToValue`, `004396a8`), (bar, value): draws the whole bar for the value without storing it: the position p by the span's sign as the paint does, then start to p through the pinstripe and p + 1 to end through a temporary brush {0, `+0x24`} at the render context's `+0x22c`, the old brush put back. For a bar filling from end that lights the side the paint leaves unlit. `LedBarGraphV_Repaint` (`00439918`) redraws at the stored position `+0x20` on the rect's *y0* and *y1* (`+8`, `+0x10`): with the span below 1, start to the position lit and the rest unlit; otherwise start to the position unlit and the rest lit. Its argument is loaded and not used.

`es2_xref.py` finds `LedBarGraph_Ctor` called from `TurboPodGauge_Ctor`, `WeaponSliderGadget_Ctor`, `EnergyPoolGauge_Ctor` and `AlertSliderGadget_Ctor`, and `LEDBarGraphV_Ctor` from `EnergyPoolGauge_Ctor` alone, in an arm it never takes (below).

`EnergyPoolGauge_Ctor` (`00444d5c`) constructs an `LEDBarGraphH` over the `.GAU` widget rect at 564 with range `0x400`. The rect is its two corners, offset by the panel rect at 548 (zero in every retail `.GAU`), and `Gau_EnergyMeterWidget` (`004323e0`) has shifted both into device pixels first (`GauEnergyMeter_ApplyCoordShift`, `00445110`), so the bar spans `x0 << XCoordShift` to `x1 << XCoordShift` inclusive. It builds the bar in a switch on its own `+0x20`, which it sets to 0 just before (`00444e41`-`00444e49`), so it always takes the `LedBarGraph_Ctor` case and skips the `LEDBarGraphV` one (`00444edc`). It writes colour ids 6 and 5 into `0x2c`/`0x30` and id 19 into `0x24`. Those resolve to palette indices 98/97/16 = `(0,116,204)`, `(0,40,160)`, `(0,0,0)` — the blue pinstripe bar retail draws directly under the TRACK button, i.e. the **Master Energy Pool meter**. `Player_PerFrameCockpitUpdate` feeds it the pool scaled to that range — see [../simulation/reactor-energy-pool.md](reactor-energy-pool.md#cockpit-readouts). Its only caller is `Gau_EnergyMeterWidget`, and the binary's own class-name table pairs `EnergyPoolGauge` with `LEDBarGraphV` (file offset 280429) and `ShieldsGauge` with `ShieldsSelectGadget` (279148) — the LED bar is the energy meter, and `ShieldsGauge` is a different class entirely.

A second `LEDBarGraph` per weapon row carries the energy-weapon charge field (`WeaponSliderGadget_Ctor` (`00442950`), range `0x400`) — but with raw palette indices `0x20`/`0x22` and remainder `0x2e`, not `COLORS.DAT` ids. See [Weapon hardpoint rows](#weapon-hardpoint-rows).

### Bitmap bar graphs

**A bitmap bar graph draws its lit and unlit parts as slices of two pictures of the whole bar, one full and one empty, rather than in flat colors.** `LEDBMBarGraphH` and `LEDBMBarGraphV`, `0x68` bytes each by their class records, are a `BarGraphH` and a `BarGraphV` with:

| Offset | Holds |
|---|---|
| `+0x2c`, `+0x44` | two `0x18`-byte `GLRectangleRegion`s, each its vtable at `+0`, a dword at `+4` set to 0 and a rect at `+8`: the drawn bitmap's rect and the run's rect moved onto it |
| `+0x5c` | a 5000-byte buffer (`Mem_NewArray`) for the screen under the bitmap |
| `+0x60`, `+0x64` | the unlit and the lit bitmap |

`LedBmBarGraphH_Ctor` (`00439ae8`) and `LedBmBarGraphV_Ctor` (`00439e10`), (bar, rect, range, unlit, lit), run `LedBarGraph_CtorBase` or `LedBarGraph_CtorV`, install `0049bd00` or `0049bce8`, build the two regions in place, store the bitmaps and allocate the buffer. `es2_xref.py` finds no reference to either constructor ([Open](#open)).

- **Paint to value** (`LedBmBarGraphH_PaintToValue`, `00439b8c`): with the span at 1 or more, start to p from the lit bitmap and p + 1 to end from the unlit one, through `LedBmBarGraphH_DrawBitmapSpan`; with the span below 1, p to end and start to p − 1 with `Raster_FillRect` in the brush as installed. It stores p at `+0x20`. `LedBmBarGraphV_PaintToValue` (`00439eb4`) is `LedBarGraphV_PaintToValue`'s runs, both from the bitmaps (lit `+0x64`, unlit `+0x60`) through `LedBmBarGraphV_DrawBitmapSpan`, the positions written into the rect's *x0* and *x1* as there.
- **Repaint**: `LedBmBarGraphH_RepaintNoOp` (`00439c40`) and `LedBmBarGraphV_RepaintNoOp` (`00439f68`) return at once.
- **Reset**: the base's.

**A run shows the same columns of the picture as it covers of the bar.** `LedBmBarGraphH_DrawBitmapSpan` (`00439c48`), (bar, bitmap), with the run in the bar's rect:

1. Region `+0x2c`'s rect becomes the lit bitmap's size, its `int16` `+6` columns and `+4` rows shifted by `VideoMode_XCoordShift` and `VideoMode_YCoordShift`, placed at twice the translation of the installed render context (`006c5ff4`), its `+0x220` and `+0x224`.
2. Region `+0x44`'s rect becomes the run's rect moved left by start and up to row 0, then by the translation; the copy's offset is (start − the translation's *x*, the run's *y0* − its *y*).
3. The display's draw page (`+0x3c`) becomes its source page (`+0x38`), through the raster routines' source-page slot when that display is current.
4. `Region_Save` keeps the screen under region `+0x2c` in the buffer; `Bitmap_Blit`(bitmap, the translation) draws the bitmap, and `VideoMode_BlitBitmap`(`004d2540`, bitmap, the translation) follows ([Open](#open)); `Region_CopyBetweenPages` copies region `+0x44` by the offset, which puts the bitmap's columns *x0* − start to *x1* − start onto the run; `Region_Restore` puts the saved screen back.

`LedBmBarGraphV_DrawBitmapSpan` (`00439f70`) does the same on the y axis, the run's rect moved up by start onto the bitmap, calling `VideoMode_BlitBitmap` alone of the two blits.

`LedBmBarGraphH_Dtor` (`0043a22a`), (bar, flags), takes seven from the runtime's destructor count and frees the bar with `Mem_Delete` when bit 0 of flags is set; it frees neither the buffer nor the regions. `es2_xref.py` finds its address as a dword at `0043a1d1`, in the class record at `0043a1a9`.

## Throttle gauge

`ThrottleGauge_Ctor` (`00447b84`), called only by `Gau_ThrottleWidget` (`0043254c`).

Its geometry is the `.GAU` block at 1000 — the slider track and a forward and a reverse fill bar, already in device pixels when the constructor sees them ([layout](../formats/gau-cockpit-layout.md#gau-block-at-1000)).

**Neither bar is ever drawn.** `ThrottleSlider_CtorV` keeps them as private fields (`+0x7e`, `+0x82`) and never registers them with the widget tree, so nothing dispatches their paint; their class's draw slots, `BarGraphV_PaintToValue` and `BarGraphV_RepaintSpan` (`00439398`, `00439460`), have no direct caller — `es2_xref.py` finds each only in `BarGraphV`'s vtable (`0049bd48`). The slider's paint (`ThrottleSlider_PaintV`, `0044819c`) reads them only through `BarGraph_GetRect` (`004390b8`), which returns the object's rect, and unions those rects into the region it invalidates. The bars are a cut feature whose construction was left in — see the speed fraction below, which is what would have filled them.

### Slider geometry

`ThrottleSlider_CtorV` builds the knob over the track: full track width, height taken from bank frame 1 (28x12 in every retail bank), limits `+/-0x400`, and an initial position centred on the track — `knobBottom = trackBottom - (trackHeight - knobHeight)/2`. That is the manual's "Centered is stopped". `SliderWidget_RecomputeScaleV` (`00452694`) then precomputes

```
scale     = (trackHeight - knobHeight) * 0x10000 / 0x800     // Q16 device px per throttle unit
knobBottom(v) = trackBottom - ((v + 0x400) * scale >> 16)     // 00452644
```

so `+0x400` puts the knob at the top and `-0x400` at the bottom. **Up is forward** — corroborated by `Reference/Simulator1.jpg`, where the knob sits at the track's top while the HUD reads 61 K/H.

The original reaches that convention through two sign flips that cancel: `SliderWidget_GetValueV` (`00452628`) reads the knob's *top* against the track's top, so it returns the negation of what `_SetValueV` was given, and `ThrottleGauge_OnChildValue` (`00447de0`) negates again for the vertical variant (gated on the gauge's `+0xc1 == 0`). Net effect: positive is forward, linear in knob position.

### Sprites

Bank `throttle` (`.HBA`/`.DBA`), 2 frames: **0** is a 2x12 tick, **1** the 28x12 knob.

The gauge captures the tick's blit position **once**, in the constructor, at the knob's neutral height plus the block's `[0x12]` nudge ([layout](../formats/gau-cockpit-layout.md#gau-block-at-1000)), and nothing writes it again — so it is a static centre-detent marker beside the track. Matches `Simulator1.jpg`.

### Live values and the two-way binding

`ThrottleGauge_GetValues` (`00447dd0`) returns gauge `+0xb1`, a pair of ints:

| offset | Value |
|---|---|
| `+0xb1` | Speed as a Q10 fraction of max, `(mech+0x28e << 10) / (speed < 0 ? -maxRev : maxFwd)` |
| `+0xb5` | Throttle setting, Q10 `+/-0x400` — the same number as `mech+0x290` |

`Player_PerFrameCockpitUpdate` (`0041b130`) writes both once a frame via `ThrottleGauge_SetValues` (`00447d80`), and arbitrates the throttle against the `mech+0x93` dirty flag: flag clear, the gauge drives `mech+0x290`; flag set, the machine's throttle is handed back for the gauge to follow and the flag is cleared. Whichever moved last wins, which is what makes the slider track the keyboard and the keyboard pick up where a drag left off.

**`+0xb1` drives nothing.** `ThrottleGauge_SetValues` marks the slider child dirty when it changes, and `ThrottleSlider_PaintV` copies it into `+0x7a` and `+0x4a` and does nothing further with it — so its only observable effect is to force a repaint whenever the machine's speed changes. It is the other half of the cut feature the two fill bars are: the knob shows the throttle asked for, the bars would have shown the speed actually reached.

The slider is the **only widget a press can drag in a retail cockpit**: the energy rows' charge bars carry the same drag flag, but each lies under its row's select gadget — see [../simulation/cockpit-input.md §7](cockpit-input.md#7-press-release-click-vs-drag). `ThrottleSlider_OnValue` (`00448378`) also sets `ThrottleLeverMode` (`0049a06e`) from the committed value's sign, but gated on a joystick throttle control being configured. See mech-locomotion.md for what that global actually is.

## `ShieldsGauge`

`ShieldsGauge_Ctor` (`004434fc`), called only by `Gau_ShieldDisplayWidget` (`00432454`) with `.GAU` offset 616 ([layout](../formats/gau-cockpit-layout.md#gau-block-at-616)). It loads no sprite bank and **draws no geometry**: it builds a `0x40`-byte child per facing (`ShieldsGauge_FacingCtor`, `00444aec`) whose paint slot (`ShieldFacing_Paint`, `00444b5c`) only tests visibility, plus two text labels.

**The meter is lit, not drawn.** The nested concentric rings are painted into the herc's own canopy art in palette indices 66-71 — verified on `OUTLAW.HB0`, where those six indices appear only inside the meter bezel, three per facing, the innermost ring using the fewest pixels. The gauge's paint (`00443730`/`00443748`) does two things per frame: rewrite those six palette slots (`ShieldsGauge_UpdateRingPalette`) and refill the two readouts (`ShieldsGauge_UpdateReadouts`).

### Ring ramp — `ShieldsGauge_UpdateRingPalette` (`004438f0`)

Per facing (charge at object `+0xb5` and `+0xb9`, each `(charge << 10) / baseMax` as `Shield_BalanceInputRead` writes it — [`damage-system.md`](damage-system.md#the-shield-system)), three rings light in turn as charge rises. The divisor is the type's base capacity, not the current maximum, so a Shield Pod's extra charge carries a ring past `0x400` into the overcharged colours instead of being renormalised:

```
ring 1: t = v
ring 2: t = v < 0x100 ? 0 : min((v - 0x100) * 2, 0x400)
ring 3: t = v < 0x180 ? 0 : min((v - 0x180) * 4, 0x400)
colour = base * t >> 10                                  -- Q10
```

Above `0x400` (an overcharged shield) the same three tracks run again over `base + (bright - base) * t`, with `v` taken as `charge - 0x400`.

Colour immediates at `ShieldRingColors` (`0049c9cb`): base RGB6 `(25,59,23)`, bright `(59,59,23)`. `Palette_InstallRange(0x42, 6, ...)` reads the six entries up the stack, so **66-68 are the first facing outermost-first and 69-71 the second**.

A facing runs 0..`0x800` with `0x400` the whole pool on one side, so an even 100/100 split parks both at `0x200`, where all six rings resolve to RGB `(48,116,44)`. The retail screenshot's meter is `(48,117,44)` — the one-channel difference is the palette scalar's own rounding.

### Readouts — `ShieldsGauge_UpdateReadouts` (`00444a68`)

`itoa(balance * 200 >> 10)` into the first label and `200 -` that into the second, from the fore/aft balance at `+0xbd`. An even split reads 100 and 100 out of a 200-point pool, which is what retail shows. **The pair always sums to 200 whatever the charge**, and an empty array still reads 100/100, so it is not a charge percentage — the natural way to misread it. Font is `ColorSchemePanels[10]` (`WHITE`); background is `COLORS.DAT` id 19 (palette 16, black).

**A forward press from centre reads 119/81, not 120/80.** A press moves the balance by `0x66` (102, [`damage-system.md`](damage-system.md#balance-adjustment-input--players-own-mech-only)), which is 19.92 readout points rather than 20, and the `>> 10` truncates. Stepping from the power-up centre of 512, every forward stop falls just short of its multiple of 20 and every rear stop just past it, so only the front label rounds down:

| Presses | Balance | Readout |
|---|---|---|
| 5 rear | 2 | 0 / 200 |
| 4 rear | 104 | 20 / 180 |
| 3 rear | 206 | 40 / 160 |
| 2 rear | 308 | 60 / 140 |
| 1 rear | 410 | 80 / 120 |
| — | 512 | 100 / 100 |
| 1 forward | 614 | 119 / 81 |
| 2 forward | 716 | 139 / 61 |
| 3 forward | 818 | 159 / 41 |
| 4 forward | 920 | 179 / 21 |
| 5 forward | 1022 | 199 / 1 |

The readings depend on the path, because the clamp at either end moves the balance off this ladder. After a sixth forward press (1024, 200/0) the steps back are 922, 820, 718, 616 and 514, which read 180/20, 160/40, 140/60, 120/80 and 100/100; after a sixth rear press (0) the steps forward are 102, 204, 306, 408 and 510, which read 19/181, 39/161, 59/141, 79/121 and 99/101.

## Weapon hardpoint rows

**Which mount owns which row is the mount's business, not the panel's**: the gauge factory is called with the `gl\<HERC>.GL` record's fire-chain byte as its `.GAU` weapon-slot index, so row order and mount order are different orderings. See [`weapon-mounts.md`](weapon-mounts.md).

Three gauge classes, one per mount class, all built on `WeaponGauge_Ctor` (`0044080c`), which lazily loads `pweapons` and `wpn_dmg` and builds a two-sequence frame table for the latter:

| Class | Factory → ctor | Value field |
|---|---|---|
| energy | `CockpitView_CreateEnergyWeaponGauge` (`00432074`) → `EnergyWeaponGauge_Ctor` (`00440a68`) | charge bar: a `WeaponSliderGadget` slider over an `LEDBarGraph` (`WeaponSliderGadget_Ctor`, `00442950`), filled to the mount's capacitor level `+0x7d` |
| ammunition | `CockpitView_CreateAmmoWeaponGauge` (`00432124`) → `AmmoWeaponGauge_Ctor` (`00440f78`) | round count, `itoa` (`AmmoWeaponGauge_Paint`, `004411b4`) of the mount's `+0x7d >> 8` |
| pod | `CockpitView_CreatePodGauge` → one of three `PodGauge` classes | none — the name label widens over both fields — except the Turbo Pod's |

**The energy and ammunition rows.** `WeaponGauge_Ctor`, (gauge, block, cockpit, mount), runs `PanelGauge_Ctor` and stores the mount at `+0xbd` and 0 in the bytes `+0xb1` and `+0xc1`; [the wipes](#the-wipes) give its banks and sequencer. `WeaponGauge_MountProjectileType` (`00440a14`) and `WeaponGauge_MountProjectileSubtype` (`00440a3c`), (gauge), return the `int16` `+0` or `+2` of the `PROJ.DAT` record at the mount's `+0x20`, or −1 with no mount or no record ([the missile cam switch](mfd.md#modes)). `EnergyWeaponGauge_Ctor` and `AmmoWeaponGauge_Ctor`, (gauge, block, cockpit, row, mount), each store `ChainedWeaponGaugeVtable` (`0049c93c`), the `int16` row at `+0xd9` and then their own table; build child 0, the `ChainedWeaponSelectGadget` at `+0x18`, handing it bit *row* of the block's byte `+0x40` (energy) or `+0x30` (ammunition) as the mirror flag; register it in the root widget's list with `Widget_RegisterClickable`; and copy the mount's name (below). The energy row then builds child 1, the charge bar (`WeaponSliderGadget_Ctor` with the block's dword `+0x3c`), at `+0x1c` and registers it, and stores 2 in the child count `+0x68` and `0x400` at `+0xc6`; the ammunition row builds its round-count label at `+0xdb` (`Label_Ctor` in `ColorSchemePanels[11]` `gray`, panel-relative, left-aligned or right-aligned when mirrored, fill `0x2e`) and stores 1 in the count. Both end by clearing the 0x17-byte state block at `+0xc2`, which also clears the energy row's `0x400`. The objects are `0xdf` and `0xef` bytes.

| Slot | Energy (`EnergyWeaponGaugeVtable`, `0049c91c`) | Ammunition (`ProjectileWeaponGaugeVtable`, `0049c8fc`) |
|---|---|---|
| `+0` | `EnergyWeaponGauge_OnChildClick` (`00440ef0`), (gauge, child, value): child 0 chains the row (`WeaponMounts_ToggleChainMember`, the cockpit machine's manager `+0x202` and the row) when bit 1 of the value is set and arms it (`CockpitView_ArmWeaponByGauge`) otherwise; child 1 stores the value, clamped to 0..`0x400`, at `+0xc6` | `AmmoWeaponGauge_OnChildClick` (`004414b4`): child 0 as the energy row's, then 2 in the repaint counter `+0xb0` |
| `+4` | `EnergyWeaponGauge_Paint` (`00440c68`): while armed (`+0x8c`), each child's slot `+0` | `AmmoWeaponGauge_Paint` (`004411b4`): while armed, child 0's slot `+0`, then the round count unless the row is dark or mid-wipe, the label off screen, child 0's state `+0x1b` 2 or the destroyed byte `+0xd3` set: `itoa` of `+0xc2` into the label in `ColorSchemePanels[10]` `white` when `+0xd6` is set and `[11]` `gray` otherwise |
| `+8` | `EnergyWeaponGauge_Update` (`00440c90`), below | `AmmoWeaponGauge_Update` (`00441268`), below |
| `+0xc` | `PanelGauge_KeyDispatchNone` | the same |
| `+0x10` | `EnergyWeaponGauge_SetState` (`00440d4c`), below | `AmmoWeaponGauge_SetState` (`004413f4`), below |

`EnergyWeaponGauge_GetState` (`00440e74`) and `AmmoWeaponGauge_GetState` (`004414a4`) return the gauge plus `0xc2`. Each update returns at once unless the row's rect has a corner on screen (`CockpitView_RectCornerVisible`) or the view manager's view-change flag `+0x1c` is set, and the row is armed. The energy row then runs `EnergyWeaponGauge_PowerUpFill` until the power-up is done; the ammunition row marks it done (`+0x8d`) and sets `+0xb0` to 2. With the power-up done, the first update that finds the rect on screen runs the row's paint, sets `+0x8e` and returns. Otherwise the update runs the [sensor dropout](#sensor-dropout) on condition 2, repaints the children (slot `+4`; the ammunition row also sets `+0xb0` to 2) on the frame the row comes back from dark, and calls each child's slot `+0xc`. The ammunition row's then, unless the row is dark or mid-wipe, sets `+0xb0` to 2 when child 0's byte `+0x1f` is set, and steps `+0xb0` down: at 2, once the label has a corner on screen, the paint's round count, unless destroyed; at 1, `Widget_DrawToCockpit`(0, the label's rect, 0) unless destroyed.

`EnergyWeaponGauge_SetState`, (gauge, block): a new block `+4` goes to `+0xc6` and to the charge bar's slot `+0x14`; a new block `+0` to `+0xc2` and the bar's fill `+0x7a`, and while the power-up is not done to `+0xdb`, the value the fill ramps toward; a new destroyed byte `+0x11` goes to `+0xd3` and puts both children's state `+0x1b` to 2 or 0, held back through `+0xc1` while the shake offset's `+4` is nonzero; the bytes `+0x14`–`+0x16` go to `+0xd6`–`+0xd8`. It repaints (slot `+4`) child 0 when a byte changed and the bar when a value did. `AmmoWeaponGauge_SetState` is the same over one child, with no shake test: a new round count goes to `+0xc2` and sets `+0xb0` to 2, and a byte change repaints child 0 and sets `+0xb0` to 2.

**The mount pushes its row's values.** Its vtable `+0x50` — `WeaponMount_PushEnergyGaugeState` (`0040f288`) on the energy and ELF classes, `WeaponMount_PushAmmoGaugeState` (`0040f330`) on the ammunition class — copies the gauge's state block, fills it and hands it back through the gauge's vtable `+0x10`:

| Block | Energy | Ammunition |
|---|---|---|
| `+0x00` | capacitor level, `(+0x7d << 10) / 1200` | rounds, `+0x7d >> 8` |
| `+0x04` | the charge bar's slider position, exchanged with the power level `+0x7b` — [`weapon-firing.md`](weapon-firing.md#the-charge-bar) | — |
| `+0x11` | destroyed, `+0x49` | destroyed, `+0x49` |
| `+0x12` | mid-charge, `+0x43` | — |
| `+0x14`-`+0x16` | the three state flags its caller passes | as energy |

`WeaponSliderGadget_Paint` (`00442b38`) fills the `LEDBarGraph` to the bar's `+0x7a`, which `EnergyWeaponGauge_SetState` sets from block `+0x00` (above), so the bar shows the capacitor rather than the power level, against the fixed 1200: a capacitor at its spawn 960 fills four fifths of it. The ammunition row's `+0x7d` lags the magazine on purpose, which is what rolls the counter ([`weapon-mounts.md`](weapon-mounts.md#ammunition)).

All three `strncpy` 12 bytes of the mount's name (`WeaponMount_GetDisplayName`, `0040e18c`) into the gauge at `+0xb1`. The pod class instead seeds an 11-char buffer with a space, appends the name, then appends `STRINGS0.STR` group 3 (`" POD"`) into the room left — `" SHIELD POD"`. A destroyed mount's row prints group 2 (`"OFFLINE"`) in place of the name.

**The Turbo Pod's row is the exception.** `TurboPodGauge_Ctor` (`00441a34`) overwrites that buffer with a plain 11-char `strncpy` of the name — so the row reads `TURBO`, not `" TURBO POD"` — rebuilds the name label at `x0+6 .. x0+34`, `y0+1 .. y0+5` and gives the freed right-hand end an `LedBarGraph` over `pod+0x7d`, its charge. The bar's range is 2500 where `TurboPod_ChargeTick` caps the charge at 2000, so a fully charged Turbo Pod shows four fifths of a bar. Which pod gets which gauge class, and why only two of the five have a button at all, is in [`equipment-pods.md`](equipment-pods.md#only-two-pods-have-a-button).

Sub-rects, all relative to the `.GAU` hardpoint rect and mirrored from its right edge when the constructor's slot-mask byte is set (that byte lands in the `.GAU`'s confirmed-zero padding in every retail file, so retail never mirrors):

| Sub-rect | Offsets from the rect, GAU | Built by |
|---|---|---|
| hardpoint state box | `x0+6 .. x0+9`, `y0 .. y0+7` | `ChainedWeaponSelectGadget_Ctor` (`00442488`) |
| weapon-name label | `x0+11 .. x0+35`, `y0 .. y0+5` | `ChainedWeaponSelectGadget_Ctor` |
| value field | `x0+36 .. x0+53`, `y0 .. y0+5` | `EnergyWeaponGauge_Ctor` / `AmmoWeaponGauge_Ctor` |
| pod name label | `x0+11 .. x0+53`, `y0 .. y0+5` | `PodGauge_Ctor` (`00441524`) |
| Turbo Pod name label | `x0+6 .. x0+34`, `y0+1 .. y0+5` | `TurboPodGauge_Ctor` (`00441a34`) |

The two pod labels are the only sub-rects that are ever painted rather than merely written in, and the paint is the only feedback a pod row gives, since it has no state box. `PodGauge_Paint` (`0044171c`) picks the label's font and background from the gauge's two state bytes: the destroyed byte at `+0xc3` wins outright and prints the offline text across the widened label, and failing that the button at `+0xc2` selects the `gray` font over background `0x2e` when it is off and the `dark` font over `COLORS.DAT` id 12 — green — when it is on. Both go into the label itself, the font at `label+0` and the background at `label+0x1d`, so an engaged pod reads as dark lettering on a green plate filling the label rect. What sets the button is in [`equipment-pods.md`](equipment-pods.md#only-two-pods-have-a-button). Both edges are inclusive, so the Turbo Pod's plate is 57x9 device pixels against a plain pod's 85x11. The label's *text* does not follow its rect — every row on the panel prints its name at the same `x0+11`, the Turbo Pod's included, which is why that plate has green to the left of the `T`.

`WeaponSliderGadget_Ctor` then drops the bar's own top edge one GAU unit below the value field's, and builds it over `0x400` with colour **palette indices** `0x20`/`0x22` and remainder `0x2e` written straight into the bar object — not `COLORS.DAT` ids.

`WeaponSelectGadget_Paint` (`004426c0`) draws:

- `WPN_DMG` frame 0 as the row underlay, then the slot number (`WeaponSelectGadget_PaintUnderlay`, `00442394`), in `[10]` `WHITE` while the gadget's state `+0x1b` is nonzero and `[11]` `GRAY` while it is 0. It is one digit, `(slot + 1) % 10`, which `WeaponSelectGadget_Ctor` (`004421dc`) writes once, so a tenth row prints `0`, matching its `[0]` key. Its label is the constructor's `x0+2 .. x0+5`, `y0 .. y0+5` GAU, **centred** (`Label_SetRect` flag 2, [label placement](mfd.md#label-placement)), so the digit's own width places it: at 640 wide a 6-pixel digit starts 5 device pixels into the rect;
- the name, in `ColorSchemePanels[10]` `WHITE` when selected and `[11]` `GRAY` otherwise;
- the slot number again, recoloured `[13]` `GREEN` when selected / `[11]` `GRAY`;
- the state box, `PWEAPONS` 6x14 frames — **only when the mount is armed or in the current fire group**, otherwise the box area is filled with the row background. Frame 4 (green, index 14) when the mount is ready, frame 5 (red) when it is not — including when the selected target is outside the weapon's range, which is also what makes the firing chain skip it ([`weapon-mounts.md`](weapon-mounts.md#readiness--weaponmounts_mountisready-00410970)). A pod is in no fire group, so a pod row never has one;
- last, the row plate: `PWEAPONS` frame 0 selected / frame 1 not, at the rect **minus two device pixels on both axes**. The 116x18 art is not a plate but a frame — a 112x14 hole of palette index 0 is punched out of it, so all the sprite contributes is a two-pixel bezel. That offset lands the hole's top-left corner exactly on the rect, where the underlay fills it, which is what puts the 14-pixel state box inside it.

**A pod row has no plate.** `PodGauge_Ctor` builds the plain `WeaponSelectGadget`, whose paint slot (`WeaponSelectGadgetVtable`, `0049c81c`) is `WeaponSelectGadget_PaintUnderlay` itself, and `PodGauge_Paint` runs that slot and then its own name label. `PWEAPONS` frames 0 and 1 are blitted only from `WeaponSelectGadget_Paint`, the chained class's paint, so a pod row is the underlay, the number and the label, with no bezel. Its number is drawn once, in the underlay's colour, and is never recoloured green. `PodGauge_Paint`'s destroyed branch sets the gadget's state to 2 after it has painted the gadget, so a destroyed pod's number turns white only at the row's next repaint ([Open](#open)).

**When a row repaints.** A row is painted into the cockpit's page and stays there until something repaints it. `EnergyWeaponGauge_SetState` and `AmmoWeaponGauge_SetState` mark the select gadget (`Widget_InvalidateDeferred`, its vtable `+4`) when the destroyed byte or one of the three state flags — selected, in the fire group, ready — changes, and the gauge's own update flushes the mark with the paint (`ChainedWeaponSelectGadget_FlushDeferredPaint`, `004428d4`), so the rows marked in one frame repaint in gauge update order, which is the order `WeaponMounts_BuildGauges` built them: mount order. A row also repaints at its first update on screen once its power-up is done, and on the frame it comes back from the [sensor dropout](#sensor-dropout)'s dark. Every in-use rect is 56x7 GAU and the bezel overhangs it by one GAU, so two rects closer than two GAU share bezel columns, and **the row repainted last owns them**. Of the nine retail cockpits only RAZOR has such a pair: row 6 at `133,209` ends at x 188 and row 3 starts at x 190 ([`../formats/gau-cockpit-layout.md`](../formats/gau-cockpit-layout.md#weapon-row-rects)), so the two plates share two device columns. Selecting row 3 repaints it and shows its white bezel whole, and row 6's next repaint — a readiness change, say — cuts its left edge back to row 6's grey.

The three state flags come from `WeaponMounts_PerFrameUpdate` (`00410b40`), the mount manager's per-frame pass.

## Console buttons

`ConsoleButton_Paint` (`00442c88`) blits `PWEAPONS` frame `2 + state` at the widget's own rect ([`.GAU` 484-532](../formats/gau-cockpit-layout.md#block-map)) — frame 2 unlit, solid palette index 34 (the retail blue, RGB `(77,77,182)`); frame 3 lit, index 14 green — then the caption in `[10]` `WHITE` unlit / `[12]` `DARK` lit. The plates are **not** canopy art. Which field is each button's `state`: [`weapon-mounts.md`](weapon-mounts.md#console-buttons).

The chain button's caption is its count in Roman numerals from `ChainCountCaptions` (`0049c71c`): `"I"`, `"II"`, `"III"` — a literal table in `.rdata`. LINK and TRACK are not fixed the same way: `ConsoleButton_Paint` reads them from `DAT_004d13d0`, the `.bss` array `SimStrings_LoadAll` fills from `STRINGS0.STR` group 4 (see [`../formats/str-strings.md`](../formats/str-strings.md)), indexed by the widget's own kind field — entry 1 for LINK, entry 2 for TRACK.

## Paper doll

The damage diagram drawn from a chassis' [`.PDG`](../formats/pdg-paper-doll.md): its regions take a damage tint, and its hardpoint list places the weapon icons over it.

### Tinting

A region is not filled. `PaperDoll_RecolorRect` (`00437e94`), (rect, keyIndex, tintIndex, mode), rewrites only the pixels of the region's rect that still hold `colorId` (the key), which is why the outlines and detail drawn over a limb survive its recolor. It makes the display's draw page (`+0x3c`) its source page (`+0x38`, through `g_RasterRoutines` slot 16 when that display is current), offsets the rect by the canvas context's `+0x210`, `+0x214` less its `+0x220`, `+0x224`, does nothing when the top row is then above row 0, and walks the rect a row at a time, left to right, reading each pixel back through `g_RasterRoutines_GetPixel` and writing through `g_RasterRoutines_PutPixel`. The mode is the region's [`recolorMode`](../formats/pdg-paper-doll.md#views):

| Mode | A pixel is rewritten when it is | It becomes | The walk is skipped when tint = key |
|---|---|---|---|
| 0 | the key | the tint | yes |
| 1 | the key | the tint | yes |
| 2 | anything but `COLORS.DAT` id 19's index, the background | itself + the tint | no |
| 3 | the key | the tint | no |

Mode 0 is written as a range test from the key to the key and a shift of the pixel by tint − key, which comes to the same. When a 320-wide bank is drawn doubled (`VideoMode_PanelMode` 3 without `VideoMode_UseHiResBanks`), every mode tests one pixel per block of 2^`VideoMode_XCoordShift` × 2^`VideoMode_YCoordShift` and writes the whole block, stepping a block at a time; otherwise a block is one pixel.

`PaperDoll_RecolorRectFromArt` (`00438230`), (rect, art, artOrigin, keyIndex, tintIndex, mode), is the same walk with mode 0 testing the source bitmap instead of the screen, for a screen that repaints a region without having repainted what is under it first: the pixel at the rect's offset from `artOrigin` (offset the same way) in `art`'s rows, its width the `int16` `+6` and its pixels from `+0xe`, the offset divided by the block size. Its mode 0 does not skip when tint = key, and its modes 1 to 3 step one pixel, doubled bank or not. `es2_xref.py` finds `PaperDoll_RecolorRect` called once from `MfdStatusScreen_Paint` and five times from `HddDamageScreen_Update`, and `PaperDoll_RecolorRectFromArt` once from `HddDamageScreen_Update`.

`Damage_PickRegionTint` (`00438624`) chooses the colour from one Q8 damage reading, on `Damage_ToConditionState`'s own bands:

| Intact | State | Tint (`COLORS.DAT` id → palette) |
|---|---|---|
| ≥ 90% | 0 | 12 → 14 green |
| ≥ 74% | 1 | 15 → 13 yellow |
| ≥ 51% | 2 | 20 → 12 orange |
| ≥ 1% | 3 | 9 → 10 red |
| 0 | 4 | 18 → 20 grey |

Which reading a region takes is the caller's business, and the two callers disagree: see [`mfd.md`](mfd.md#viewport-and-condition-per-class) for the status screen's compact view and [`heads-down-display.md`](heads-down-display.md#damage-detail--page-1) for the damage detail's.

### Weapon icons

`PaperDoll_BuildWeaponIcons` (`00437c8c`) turns the `.PDG`'s [hardpoint list](../formats/pdg-paper-doll.md#hardpoint-list) into each machine's icon list, stored at `mech+0x1fe` by `Sim_InitMissionSession`. **Entry `n` is the hardpoint whose `.GL` slot byte (`+0x17`) is `n`**, not the `n`th `.GL` record: the builder searches the gun layout for the slot byte and takes the mount at that record's position. Every retail `.PDG` has at most as many entries as its `.GL` has records, and each entry's slot is present, so the search always lands. An empty hardpoint, or a weapon whose template `+0x50` is -1, gets no bitmap.

The frame is template `+0x50` ([`../formats/weapons-dat-sim.md`](../formats/weapons-dat-sim.md#decoded-tail-fields)) plus `frameOffset`, out of the `weapons` bank — `hba\WEAPONS.HBA` or `dba\WEAPONS.DBA`, loaded once by `PaperDoll_InitTables` (`004378d8`) together with [`pdg\WEAPONS.PDG`](../formats/pdg-paper-doll.md#pdgweaponspdg), which gives each frame's size. The alignment backs the anchor off by that size, or half of it, after the shift; the rect is anchor to anchor plus size, inclusive.

`blitFlags` 2 moves the icon further left by the bitmap's own width less the `WEAPONS.PDG` width.

## HUD fonts

`ColorSchemePanels_LoadAll` (`00431098`) loads 18 fonts into `ColorSchemePanels` (`0049b0ac`) the first time it runs ([building the cockpit](#building-it)), each through `Panel_LoadWrapper` (`00430f58`), which opens a `VolRStream` on the path and returns `ClassItem_LoadResource`'s item, from `hfn\` in panel mode 3 and `dfn\` otherwise. Load order is the array index:

| 0-5 | 6-11 | 12-17 |
|---|---|---|
| `cpblue`, `cpgreen`, `cpred`, `cpylw`, `cpon`, `cppress` | `cpoff`, `cpgrey`, `cpblack`, `cporange`, `white`, `gray` | `dark`, `green`, `red`, `hud1`, `hud2`, `hud3` |

Each file is the same typeface stencilled in one palette index, so **a widget picks its text colour by picking a font** — no colour is ever passed to a label. Consumers reach entries by absolute address: `0049b0d4` = 10 `white`, `0049b0d8` = 11 `gray`, `0049b0dc` = 12 `dark`, `0049b0ec` = 16 `hud2`, `0049b0f0` = 17 `hud3`.

Format, glyph layout and per-file ink indices: [`../formats/dfn-hfn-dci.md`](../formats/dfn-hfn-dci.md).

## Power-up sequence

On taking a walking machine the cockpit comes up piece by piece rather than reading true from its first frame. Three fields of the shared widget base carry it — `+0x8c` armed, `+0x8d` done, `+0x90` the coarse tick it was armed on — cleared by the base constructor (`00438b20`) and set by `Widget_BeginPowerUpAnimation` (`00438ddc`); each class reads them in its own paint and update. A coarse tick is `Time_GetCoarseTicks` (`00467724`), `GetTickCount() >> 4`: 16 ms of wall time.

`Cockpit_PowerUpSound` (`004328cc`) stamps the sequence's start at `cockpit+0x241`, and `Cockpit_PowerUpTick` (`00432924`) arms, once a frame, every widget whose moment has come:

| Widget | Armed once ticks since the start | While armed and not done |
|---|---|---|
| Weapon rows, `cockpit+0x70` | `> 20 * (row + 1)` | [the row winks on](#weapon-rows-wink-on) |
| `ShieldsGauge`, `cockpit+0x1e9` | `!= 0` | [the rings fill](#shield-rings-fill) |
| Roving gunsight, `cockpit+0x1f5` | `!= 0` | the compass winds up — [`cockpit-gunsight-hud.md`](cockpit-gunsight-hud.md#power-up-wind-up) |
| MFD, `cockpit+0x1ed` | `!= 0` | [the scanner dish grows](#scanner-dish-grows) |

**A flyer skips all of it.** `Gau_BuildCockpitWidgets` (`00431bf8`) ends with a branch taken when the piloted machine's type record has `FlyerFlag` set — `mech+0x1f2 -> +0x50`, the RAZOR alone (see [`mech-locomotion.md`](mech-locomotion.md)'s type-record table). It arms *and* marks done every widget in the table, and sets `cockpit+0x245`, which stops `Cockpit_PowerUpSound` ever stamping the start. The same flag gates the engine hum, [`audio.md`](audio.md#the-cockpit-power-up). <!-- doc-lint: ok -->

Retail runs every one of these animations on the coarse clock from a stamped tick, so a widget that is off screen while its update is skipped shows on its return exactly what it would have shown.

### Weapon rows wink on

`cockpit+0x70` is ten widget slots indexed by `.GAU` weapon row. `CockpitView_RegisterWeaponGauge` (`00432018`), the registration every weapon-row gauge factory ends in, stores the gauge at its row; the number-key handler in `CockpitWidgets_HandleCommand` indexes the same array to arm a row. The delays are `DAT_0049b05a`, ten shorts reading 20, 40, … 200, so the rows arm top to bottom 320 ms apart and the last at 3.2 s.

Every weapon and pod gauge's paint (`EnergyWeaponGauge_Paint` (`00440c68`), `AmmoWeaponGauge_Paint` (`004411b4`), `PodGauge_Paint`, `TurboPodGauge_Paint` (`00441c14`)) and every child's (`WeaponSelectGadget_Paint`, `WeaponSelectGadget_PaintUnderlay` (`00442394`), both through the owner at child `+0x24`) opens on the owning gauge's armed byte. **A row that is not armed draws nothing**, so the console art shows where it will be.

Once armed, an energy row's charge bar fills rather than appearing full. `EnergyWeaponGauge_PowerUpFill` (`00440e84`) shows `min(elapsed * 0x19, value)` against the ticks since the row was armed, then the live value outright from `elapsed >= 0x33`. That reaches `0x400`, the bar's whole range, before the ramp ends. The Turbo Pod's bar runs the same ramp in `TurboPodGauge_PowerUpFill` (`00441d88`), but its value is the pod's raw charge on a 2500-unit bar, so it climbs to 1250 and then jumps to the tank's real level. An ammunition row (`AmmoWeaponGauge_Update`, `00441268`) marks itself done on its first update and has nothing to ramp.

### Shield rings fill

Until the meter is done, `ShieldsGauge_SetStateBlock` (`00443858`) copies each frame's live facings to `+0xcc`/`+0xd0` and zeroes the displayed pair at `+0xb5`/`+0xb9`, so before it is armed [the ring ramp](#ring-ramp--shieldsgauge_updateringpalette-004438f0) paints all six rings dark. Once armed, `ShieldsGauge_Update` calls `ShieldsGauge_PowerUpFill` (`004437a4`) each frame until done:

```
shown = min(ticks since armed, live)      -- per facing, on the rings' 0..0x800 scale
done when both facings show their live value
```

One ring unit a tick, so an even split's `0x200` fills in 512 ticks, about eight seconds. The readouts are not ramped: `+0xbd`, the balance they print, passes straight through.

### Scanner dish grows

The `radar` bank exists for this animation alone. `MfdDisplay_Ctor` (`00445218`) loads it, builds a one-sequence frame table over it — frames 0 to 9, each held 7 ticks — and hands both to a sprite sequencer at `+0x331` (`SpriteSequence_Init`, `00471ca0`); `MfdDisplay_Update` frees all of it the frame the display is done. The frames are 110x110, the dish's own size, and blit at the scanner screen's dish position (screen `+0x18`, [`mfd-scanner.md`](mfd-scanner.md)):

| Frames | Content |
|---|---|
| 0-4 | A small ring on the screen's palette 17 background, widening and brightening each frame |
| 5-8 | The full-size dish ring, its colour settling |
| 9 | The finished dish, with the transparent interior the scanner's own dish frame (`MFD` 14) has |

Frames 0-8 are fully opaque, so each one covers the last.

The display boots on the scanner (`Gau_MfdPanelWidget` sets mode 3). What it does there, until done:

- **Not yet armed:** `MfdDisplay_Repaint` blits frame 0 over the dish in place of the screen's paint.
- **Armed:** `MfdDisplay_Update` starts the sequence on its first pass (`SpriteSequence_Start`, `00471d04`, which stamps the coarse tick and blits frame 0) and steps it on every later one (`SpriteSequence_Step`, `00471d7c`). A step advances past every frame whose hold has run out, so frame `k` shows once more than `7k` ticks have passed. Reaching frame 9 blits it and stops the sequence, and the next pass finds it stopped and sets done: about one second in all. Each of these passes returns before the screen's update slot and the transmission branch, so neither the plot nor a squadmate's transmission is drawn while the dish grows.

On any other screen, the update sets done once the coarse tick passes `+0x345`, which the start stamps at 70 ticks (`0x46`) past its own tick — and at once if the sequence never started, since `+0x345` is then zero.

The sequencer is a 0x2a-byte object, the same kind the [sensor dropout](#sensor-dropout) plays over widget `+0x6c`:

| Offset | Contents |
|---|---|
| `+0x00` | State: 0 idle, 1 playing, 2 just ended — `SpriteSequence_Step` reports an end once and drops back to 0 |
| `+0x02`, `+0x04` | Sequence index, frame index |
| `+0x06` | Coarse tick it started on |
| `+0x0a` | Sum of the holds of the frames reached so far |
| `+0x0e` | Coarse tick the current frame's hold ends |
| `+0x12`, `+0x16` | Current sequence `{count, frames}`, sequence set `{count, sequences}`; a frame entry is `{frame, hold}` |
| `+0x1e` | Sprite bank |
| `+0x22` | Blit position |

## Sensor dropout

While the player's sensor array is damaged, the cockpit's displays drop out at random. The mechanism is on the `PanelGauge` base, whose constructor (`PanelGauge_Ctor`, `00438b20`) sets it up, and `es2_xref.py` finds six calls to its toggle, one in each display's update: the HUD gunsight complex (`Gunsight_UpdateAndPaint`), the MFD (`MfdDisplay_Update`), the Heads-Down Display (`HddDisplay_Update`, `00449bd0`), and the weapon rows' `EnergyWeaponGauge_Update` (`00440c90`), `AmmoWeaponGauge_Update` (`00441268`) and `PodGauge_Update` (`00441850`).

Each frame the caller stores the sensor array's condition at `+0x74` — `Player_DependentCondition(2)` (`004342e0`), which reads dependent 2 of the player's machine (`STRINGS0` group 15 entry 2, `SENSOR ARRAY`) as `damage << 8 / max` and buckets it through `Damage_ToConditionState`, 0 intact to 4 destroyed — and runs `PanelGauge_TickDropout` (`00438bc0`) only when it is nonzero.

| Offset | Contents |
|---|---|
| `+0x6c` | Sprite sequencer played at each change, or 0 for none |
| `+0x70`, `+0x72` | Its sequence for going dark, and for coming back |
| `+0x74` | Sensor condition, 0-4 |
| `+0x76` | 1 dark, 0 shown; `+0x77` holds the previous frame's |
| `+0x78` | Coarse tick the current state ends, 0 while none is drawn |
| `+0x7c`, `+0x80` | Range a dark spell is drawn from, in coarse ticks |
| `+0x84`, `+0x88` | Range a shown spell is drawn from |

Entering either state draws its length with `PanelGauge_RollDuration` (`00438d6c`) — `(next & 0xffff) % (hi - lo) + lo` on the [presentation generator](random-generator.md#the-presentation-generator), no draw when the two are equal — and the state flips once it has passed. With a sequencer, the expiry instead starts sequence `+0x70` (going dark) or `+0x72` (coming back) at the display's own rect (`+4`), steps it once a frame, and flips the state on the step after the sequence ends. A condition of 0 forces the dark state, but no caller runs the toggle with one.

| Display | Dark, by condition 1 / 2 / 3 / 4 | Shown | Sequencer |
|---|---|---|---|
| Heads-Down Display (`PanelGauge_Ctor`'s defaults) | 180-360 | 180-360 | none |
| Weapon rows (`WeaponGauge_Ctor`, `0044080c`) | 120-360 | 180-1800 | one per row |
| Gunsight (tables `0049be48`-`0049be66`) | 30-120 / 60-120 / 90-160 / 120-200 | 180-900 / 180-360 / 180-360 / 120-300 | none |
| MFD (`MfdDisplay_SetDropoutRanges`, `00446db4`) | 30-120 / 60-120 / 60-120 / 60-120 | 180-900 / 180-360 / 180-360 / 180-360 | one of three at `004d1d0c`, by condition |

The Heads-Down Display keeps the defaults: every write `es2_fieldscan.py` finds to `+0x7c`-`+0x88` on a `PanelGauge` is in the base constructor, the gunsight's update, `WeaponGauge_Ctor` or the MFD's setter.

### The wipes

A weapon row and the MFD play a short animation at each change; the gunsight and the Heads-Down Display flip at once.

**Weapon rows.** `WeaponGauge_Ctor` gives every row its own sequencer over `WPN_DMG`, on a two-sequence table it builds once from the start frames at `0049c4c8` and the end frames at `0049c4cc`: frames 1 to 9 going dark and 9 to 1 coming back, one coarse tick each. The frames are 112x14, the plate's hole exactly. Frame 1 fills the row with a bright bar, which collapses to a line and then to a dot, and frame 9 is the plain plate in index 42 — a screen switching off, and on again backwards.

**MFD.** `MfdDisplay_Ctor` builds three sequencers over `MFD_DMG`, whose 192x118 frames blit at the screen inset (`+0xeb`, which the constructor also copies into the base rect). `MfdDisplay_SetDropoutRanges` picks one through `[0, 1, 2, 2, 2]` at `0049cb34` and asks it for sequences `set*2` going dark and `set*2+1` coming back. The counts at `0049cb40` and the frame lists at `0049cb4c`-`0049cb88` describe six sequences, but the constructor's loop builds three, sequence `i` for sequencer `i`, each frame held 4 ticks, and hands each sequencer a sequence set of count 1. `SpriteSequence_Select` accepts an index up to the count, so sets 1 and 2 refuse both their numbers and every wipe plays the sequence its sequencer was built with, both ways:

| Condition | Set | Frames |
|---|---|---|
| 1 | 1 | 4, 0 |
| 2-4 | 2 | 4, 5, 6 |

Frame 4 floods the screen in index 30, 5 is a thin line across it, 6 the blank screen in index 17 and 0 a band across the middle. Set 0 is the one whose numbers `SpriteSequence_Select` accepts (frames 0, 4, 0 going dark and 4, 0 coming back), and it belongs to condition 0, which never ticks. Frames 1-3 are in none of the three built sequences, and the bank has no other reader: `es2_xref.py` finds its three references, all in `MfdDisplay_Ctor`.

### While dark

A display holds back while `+0x76` is set, and one with a sequencer also while the sequencer is anything but idle — the pair every paint below tests. Nothing covers what the wipe last blitted, so its final frame stays up for the whole dark spell.

- **Gunsight.** `Gunsight_UpdateAndPaint` and `Gunsight_Paint` skip the whole paint: every child, the readouts, and the floating scanner repeater.
- **MFD.** `MfdDisplay_Update` returns before the buttons' updates, a squadmate's transmission and the screen's own update. `MfdDisplay_Repaint` paints the chrome, the buttons and the title and skips the screen.
- **Weapon rows.** `WeaponSelectGadget_Paint` draws the row plate and nothing else, and a pod row, having no plate, draws nothing. The underlay and slot number (`WeaponSelectGadget_PaintUnderlay`), the name, the state box, the round count (`AmmoWeaponGauge_Paint`, `004411b4`), the charge bar (`WeaponSliderGadget_Paint`, `00442b38`) and a pod's label all hold back, so the wipe's frame is what fills the plate's hole.
- **Heads-Down Display.** `HddCommandScreen_DrawMap` floods the map viewport in id 19 and draws nothing in it; `HddCommandScreen_RefreshOrders` fonts every order `CPBLUE` with no highlight; `HddDamageScreen_Update` floods the screen rect in id 3 and draws nothing else, the subject caption included. `HddDisplay_Update` flags the current page for a full repaint whenever `+0x76` changes.

**The Heads-Down Display holds its buttons too.** `HddDisplay_HandleWidgetPress` acts on the two page buttons while dark, and for any other press returns before it clears the pending index at `+0x528`. The press stays latched, a later one replaces it, and it runs on the first update that finds the display back — which handles the press before it ticks the dropout, so one frame after the flip. The arrow keys, the magnifiers' keys and `[1]`-`[3]` reach it the same way, since `HddDisplay_KeyDispatch` (`00449fcc`) presses the widget for each; `[S]`/`[I]`/`[W]` and the command display's own keys do not.

Coming back, a weapon row repaints its children and the MFD and the ammunition and pod rows their whole widget, over the wipe's last frame.

### When each display ticks

Each display runs the toggle from its own update, so a display whose update does not run draws no spell. The deadline is an absolute coarse tick, so one that expired meanwhile flips on the first update back.

| Display | Updates |
|---|---|
| Gunsight | Outside the external view (view 4), or during a view transition |
| MFD | Once its power-up has armed it, outside view 4, with its screen on screen; not while the dish is still growing on the scanner |
| Weapon row | Once its power-up has armed it, with its rect on screen or a view transition running |
| Heads-Down Display | Outside view 4 |

## Per-frame ordering

`Sim_RenderFrame` (`0045fb9c`): `Terrain_SetupVisibleRegion`, then `CockpitView_PerFrameUpdate` (`004327ac`; `CockpitViewInstance` widget paint dispatch), then `Scene_SubmitFrameObjects` (the 3D world), then the terrain and the objects on it when the view shows any world ([`../rendering/terrain-drawing.md`](../rendering/terrain-drawing.md#the-frame)), then `Player_PerFrameCockpitUpdate`, then three more paint dispatches on `CockpitViewInstance` sub-objects (`+0x1f5`, `CockpitView_GetSquadMessagePort` (`00433158`)'s result, `+0x20b`).

## Rejected readings

| Reading | Why it is wrong |
|---|---|
| `WPN_DMG` and `MFD_DMG` are damage art — a weapon row's damage fill levels and the MFD's damaged screen. | The names say damage and `WPN_DMG`'s frames do step from full to empty. But `es2_xref.py` finds `WPN_DMG`'s bank pointer only in `WeaponGauge_Ctor`, which builds the sensor-dropout sequencer over frames 1-9, and in `WeaponSelectGadget_PaintUnderlay`'s blit of frame 0; `MFD_DMG`'s only in `MfdDisplay_Ctor`, which builds the MFD's three sequencers over it. Both are the [sensor dropout's wipes](#the-wipes), and `WPN_DMG` frame 0 is every row's plain underlay. |

## Open

- **Open:** a builder of a [bitmap bar graph](#bitmap-bar-graphs): `es2_xref.py` finds no reference to `LedBmBarGraphH_Ctor` or `LedBmBarGraphV_Ctor`, nor to `LedBmBarGraphH_Dtor` beyond its class record. ES1's throttle builds its two bars as bitmap bar graphs.
- **Open:** what `VideoMode_BlitBitmap` does with the block at `004d2540`, which the [bitmap bar graphs'](#bitmap-bar-graphs) run calls after `Bitmap_Blit`, unread.
- **Open:** which repaints catch a pod row's gadget state, whose nonzero values turn its number white: 2 from `PodGauge_Paint`'s destroyed branch, and the held state 1 ([`cockpit-input.md`](cockpit-input.md)). The repaints that follow a press and a destruction are the open part.
- **Deferred:** what consumes `PWEAPONS` frame 7, a 640x80 strip.
- **Deferred:** whether any cockpit shows the `PanelAmbience` clock: two two-digit labels and a colon label that `PanelAmbience_TickClock` (`00452144`) blinks, advancing the clock on every second call, the second field wrapping at 59 and the whole at 99:59. Its one construction is in `Gau_PanelAmbienceWidget` (`004326a8`), from the `.GAU` block at 1604, for which `es2_xref.py` finds no caller; that block is zero in all nine retail `.GAU` files.
