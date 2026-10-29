# DBSIM.EXE — the Heads-Down Display's command screen

**ES1's command screen, the Heads-Down Display's page 0 (`F7`), is ES2's ([command display](../../retail/simulation/heads-down-display.md#command-display--page-0)): a map of the mission area beside a column of eight squad orders under a message row, where the player picks a squadmate, an order and, for four of the orders, a unit or a gridpoint on the map, then sends it with `XMIT`.** What a player would see differently:

- fully zoomed out, the map shows the whole mission area and 60000 units (360 m) beyond it on every side, the looser axis filling the map, where ES2's fills it with the tighter axis and crops the other;
- the magnifiers step the zoom by a fixed amount, between two fixed limits, and the arrows pan the map's center up to 5,000,000 units (30,000 m) from the origin each way, where ES2's pans stop at the mission area's edge;
- the armed order's row is marked by a filled bar at the column's left edge alone, with no plate under the row;
- `ATTACK ENEMY` and `DEFEND POSITION` take a unit only inside the MFD scanner's range ([modes](mfd-display.md#modes)), and once `DEFEND POSITION` holds a pick a click on open ground does not move it ([picks](#picks));
- a unit picked on the map does not become the chosen unit that `Enter` picks again ([picks](#picks)).

Addresses are `ES1DBSIM` image addresses ([the X-32 executable](../formats/x32-executable.md#the-image)); ES2's are DBSIM's. Every function here removes its own arguments, so argument lists are in stack order ([calling conventions](../formats/compiler.md#calling-conventions)). The screen is the display's `+0xca` ([the display object](heads-down-display.md#the-display-object--hdddisplay_ctor-0003c258)), and the display's presses and keys reach it as [the display's doc](heads-down-display.md#presses) lists. The paint of the map, the markers and the order list, and the rest of the file from `000408dc`, are [Open](#open).

## Where the object file starts and ends

The code is `0003edbc`–`000420c7`, between [the Heads-Down Display's](heads-down-display.md#where-the-object-file-starts-and-ends) and the damage screen's, and the tables and data start at `00094802`. It opens with the run of `ConfigPart`'s copies ([module copies](../formats/compiler.md#vtables)) after `HddDisplay_StaticInit`'s `RET` at `0003edb8` and three zero bytes: `HeapPtr_DtorHddCommandCopy` (`0003edbc`), `ConfigPart_DtorHddCommandCopy` (`0003ede4`), `ConfigPart_GetClassTagHddCommandCopy` (`0003ee23`) and `TSPartBase_DtorHddCommandCopy` (`0003ee2c`), with the tables `00094802` and `00094836`. It ends with its static initializer, `000420b0`, the static constructor table's entry `000adc4c`: frameless, it stores the `int16` triple (`0xc000`, 0, 0) at `HddMapView_Angles` (`000b1604`), as ES2's `HddCommandScreen_StaticInit` (`00450430`) does, and its `RET` at `000420c7` stands right before the damage screen's file at `000420c8` ([where object files meet](../formats/compiler.md#where-object-files-meet-in-the-code)).

The functions follow ES2's in order, from `HddCommandScreen_Ctor` (ES2's `0044c264`).

## The screen object

**The object is ES2's ([command display](../../retail/simulation/heads-down-display.md#command-display--page-0)) on ES2's offsets from `+0x10`, with the page base's fields reordered, 75 map markers where ES2's has 140, and the map cursor and two flags packed at the end; it is `0x17e` bytes.**

| ES1 | ES2 | Holds |
|---|---|---|
| `+0x00`, `+0x04` | `+0x04`, `+0x08` | the page base's ([the pages' base](heads-down-display.md#the-display-object--hdddisplay_ctor-0003c258)): the screen rect (a pointer) and the display |
| `+0x08` | `+0x0c` | the paint stage the display hands its pages ([the paint stages](heads-down-display.md#drawing-it)) |
| `+0x0c` | `+0x00` | the vptr, `HDDMapVtable` (`00094886`) |
| `+0x10` | `+0x10` | the map's render context, a `GLViewport` |
| `+0x14` | `+0x14` | the map's camera |
| `+0x18`, `+0x1c`, `+0x20` | the same | the camera's position: the map center's *x* and *y*, and the scale |
| `+0x30` | `+0x30` | a byte the constructor and the zooms set ([Open](#open)) |
| `+0x31` | `+0x31` | the markers, an array of 75 pointers |
| `+0x35`, `+0x39` | the same | the two click regions: the order column, the map |
| `+0x3d` | `+0x3d` | the eight order rows' labels, orders 10–17 |
| `+0x5d`, `+0x81` | the same | the armed order, −1 for none, and the one armed before it |
| `+0x61` | `+0x61` | the armed row's bar rect: *x* from the column's *x0* + 1 to *x0* + 3 |
| `+0x71` | `+0x71` | the map viewport rect |
| `+0x85` | `+0x85` | three `0x2a`-byte pick records, one per squadmate ([the link line](../../retail/simulation/heads-down-display.md#the-link-line)) |
| `+0x103`, `+0x104` | the same | the armed order wants a pick; the order is ready to send (bytes) |
| `+0x105` | `+0x105` | the selected squadmate's slot, −1 for none |
| `+0x109`, `+0x10a` | the same | the markers are built (byte); the marker count (`int16`) |
| `+0x114`, `+0x118` | the same | the pan offsets, *x* and *y* |
| `+0x11c`, `+0x11d` | the same | a map click is waiting (byte); its point |
| `+0x131` | `+0x131` | eight availability bytes, one per order |
| `+0x139` | `+0x139` | a byte that makes the update redraw the whole order list |
| `+0x13a`, `+0x13e`, `+0x140` | the same | the message row's label, its countdown in blink ticks (`int16`), its text |
| `+0x144` | `+0x144` | clear the message row (byte) |
| `+0x145` | `+0x145` | the pan step |
| `+0x149` | `+0x149` | the clock count of the next blink tick |
| `+0x14d`–`+0x15b` | the same | a queued list click: waiting `+0x14d`, the click region `+0x14e`, the point `+0x152`/`+0x156`, from a key `+0x15a`, the double-click `+0x15b` |
| `+0x15c` | `+0x15c` | the chosen unit, the one `Tab` sets and `Enter` picks |
| `+0x160` | `+0x160` | the mission box grown by 60000 on every edge; ES2's holds it ungrown |
| `+0x170` | `+0x374` | the map cursor: the last map click as a world point (*x*, *y*, *z*) |
| `+0x17c` | `+0x380` | the pick cursor is up (byte) |
| `+0x17d` | `+0x381` | a squadmate is selected (byte) |

The pick record is ES2's ([the link line](../../retail/simulation/heads-down-display.md#the-link-line)): the saved point `+0x85`, the current point `+0x91`, the saved unit `+0x9d`, the current unit `+0xa1`, the held bytes `+0xa5` and `+0xa6`, and the kinds `+0xa7` and `+0xab`. **`HddPilotPick_ElementCtor` (`0003f418`)**, (record), allocates `0x2a` bytes when given none, returning 0 when that fails, runs the inlined constructors of the two points at `+0` and `+0xc`, and returns the record. ES2's (`004505ef`) returns its argument.

### Building it — `HddCommandScreen_Ctor` (`0003ee58`)

(screen, display, order column, screen rect), from `HddDisplay_Ctor` with 0 for the screen:

1. allocates `0x17e` bytes when given none and runs `HDDisplay_Ctor`(screen, display, screen rect), so `+0` holds the screen rect and `+4` the display; the pick records with `__vec_ctor` and `HddPilotPick_ElementCtor` (by its padding `0003f415`); `HDDMapVtable` at `+0xc`; `+0x149` = the clock `000981c4` + `0x1e`;
2. **the map viewport** `+0x71`: the screen rect, with its *x1* the order column's *x0* − 2; ES2's insets it by 4 ≪ `VideoMode_XCoordShift` and 2 ≪ `VideoMode_YCoordShift` and takes its *x1* 4 ≪ `VideoMode_XCoordShift` left of the column;
3. inside `Cockpit_PushCanvasContext` and `Cockpit_PopRenderContext`: **the render context** `+0x10`, `GLViewport_Ctor`(0), its origin `+0x220`/`+0x224` at −(width ≫ 1) and −(height ≫ 1) of the viewport, so a point at (0, 0) lands on its center, and its clip mode `+0x208` and translation flag `+0x20c` 1; the center `+0x18`/`+0x1c` = 0;
4. **the grown box** `+0x160`: `Mission_Box` (`000af7a4`) copied and widened by 60000 on every edge ([the mission box](mission-status.md#the-mission-box)); **the full zoom-out scale** `+0x20` = 2 × max((((*x1* − *x0*) ≫ 1) ≪ 7) / *w*, (((*y1* − *y0*) ≫ 1) ≪ 7) / *h*), on the grown box, *w* and *h* the viewport's width and height as the origin's halves doubled; with the camera's shift of 7 below, that is the scale at which the whole grown box fits. ES2's takes the smaller of the two, ≪ 8, on the ungrown box ([the map's frame of reference](../../retail/simulation/heads-down-display.md#the-maps-frame-of-reference)), and keeps a zoom step ES1's has no counterpart of;
5. **the camera** `+0x14`: `TSCamera_Ctor`(0, `HddMapView_Angles`, `+0x18`, `0x80`, 7, the render context), a focal length of 2⁷ ([the camera](external-views.md#the-camera)); then `00041994`(screen), in the place of ES2's `HddMap_RecomputePanStep` and `HddMap_ZoomIn` pair;
6. **the click regions**: `00041fcc`(0, screen, order column) at `+0x35` and `00041fcc`(0, screen, the map viewport) at `+0x39`, ES2's `HDDListGadget_Ctor`'s place, each registered with `Widget_RegisterClickable` in the cockpit's root widget's list, in that order ([the two click regions](../../retail/simulation/heads-down-display.md#the-two-click-regions));
7. **the order column**, *r* the column's height / 9 and *c* `HudFont_CellHeight`(`cpgreen`, `'0'`): the message row `+0x13a`, `Label_Ctor`(0, `cpred`) placed by `Label_SetRect` with flags 2 and margin (0, 0) over the column's width from *y0* to *y0* + *c*, its fill `+0x1c` = `0x3a`; then the eight order rows at `+0x3d`, row *i* from *y0* + (*i* + 1) *r* to that plus *c*, each `Label_CtorWithAltFont`(0, `cpgreen`) placed with flags 1 and margin (5, 0), its hotkey font `+0x20` = `cpred` ([`ColorSchemePanels`](cockpit-widgets.md#labels)); ES2's message row is 7 ≪ `VideoMode_YCoordShift` tall;
8. `000418d8`(screen), ES2's `HddCommandScreen_DisableOrders`'s place; `+0x81` and `+0x5d` = −1; the bar rect's *x0* `+0x61` = the column's *x0* + 1 and *x1* `+0x69` = *x0* + 3;
9. **the markers**: a 300-byte array at `+0x31`, filled with 75 markers `00041c50`(0, screen, the rect (0, 0, 0, 0)), ES2's `HddMarker_Ctor`'s place; ES2's builds 140;
10. `+0x109` and `+0x10a` = 0, `HddCommandScreen_MapDirty` (`00094868`) = 0, `+0x30` = 1, `HddCommandScreen_BlinkPhase` (`00094867`) = 1 and `HddCommandScreenInstance` (`0009486c`) = the screen;
11. each pick record's held bytes `+0xa5` and `+0xa6` cleared; `+0x105` = −1; `+0x17c`, `+0x17d`, the pan offsets, `+0x11c`, the 12 bytes `+0x125`–`+0x130`, `+0x13e`, `+0x144` and `+0x15a` cleared. It writes neither `+0x103` nor `+0x104` ([Open](#open)).

`HDDMapVtable` (`00094886`) holds, from `+4`: the repaint `HddCommandScreen_Repaint`, the update `HddCommandScreen_Update`, the hide `HddCommandScreen_Hide`, the show `HddCommandScreen_Show` and the key slot `HddCommandScreen_KeyDispatch`, each by its padding, the slots the display calls through a page's `+0xc` ([the pages' base](heads-down-display.md#the-display-object--hdddisplay_ctor-0003c258)).

## The map

**The map is a camera looking straight down from the scale's height over the map center, as ES2's ([the map's frame of reference](../../retail/simulation/heads-down-display.md#the-maps-frame-of-reference)), with a focal length of 128 where ES2's is 256: a scale *s* draws *s* / 128 world units to a pixel.**

- **`HddMap_ZoomOut` (`0003fc70`)** and **`HddMap_ZoomIn` (`0003fca4`)**, (screen), the magnifiers: the scale `+0x20` plus 50000 while that is at most 1,000,000, or less 50000 while that is at least 50000; then `00041994`(screen), `+0x30` = 1 and `HddCommandScreen_MapDirty`. So the map runs from 390 units (2.3 m) to 7812 units (47 m) a pixel in steps of 390 units, and the full zoom-out from the constructor is outside those steps whenever it is above 1,000,000. ES2's step is the fit less its floor of 60000, divided by 25, and at least 5000, and runs between that floor and the fit (`0044cf68`, `0044cf9c`).
- **`HddMap_PanLeft` (`0003fcd4`)**, **`HddMap_PanRight` (`0003fd00`)**, **`HddMap_PanUp` (`0003fd2c`)** and **`HddMap_PanDown` (`0003fd58`)**, (screen), the arrows: the pan offset `+0x114` (*x*) or `+0x118` (*y*) less or plus the pan step `+0x145`, toward −*x*, +*x*, +*y* and −*y*, and `HddCommandScreen_MapDirty`, only while the center `+0x18` or `+0x1c` moved by one step stays within ±5,000,000; otherwise nothing. ES2's (`0044cfd0`–`0044d0fc`) move by the step or by the room left between the viewport's edge and the grown box, whichever is less.
- **`HddMap_ToViewport` (`0003fd84`)** and **`HddMap_FromViewport` (`0003fdc4`)**, (screen, out, in), are ES2's (`0044d224`, `0044d260`): the point plus or less the viewport's corner `+0x71`/`+0x75` and its half-extent.
- **`HddCommandScreen_InstallMapContext` (`0003fe04`)**, (screen): the render context's rect `+0x210`–`+0x21c` = the map viewport less the installed context's (`000bdbda`) origin `+0x220`/`+0x224`; installs it with `Raster_InstallRenderContext` and returns it. ES2's (`0044d29c`) also takes off the view offsets.

**`HddCommandScreen_HitTestMarker` (`00040410`)**, (screen, point), turns a map click into a world point and finds the object under it. It installs the map's context, sets the camera's position to (`+0x18`, `+0x1c`, `+0x20`), installs it with `Raster_InstallViewProjection` and `Raster_SetModelTransform`(0), and takes the point back from the viewport (`HddMap_FromViewport`) with *y* negated; (*x* × scale ≫ *k*, *y* × scale ≫ *k*, scale), *k* the camera's shift `+0x1a`, goes through `Transform_ApplyToPoint` with `g_ViewToModel` into the map cursor `+0x170`. It installs the context it found again, does the same for (*x* − 5, *y* − 5), and takes *h* = the cursor's *x* less that point's, five pixels in world units. It answers the first object of `LiveObjectList` whose *x* `+0x20` and *y* `+0x24` both lie strictly within *h* of the cursor's, or 0. ES2's (`0044d860`) scales the point itself, keeps the scale as the cursor's *z*, and tests half a side of (5 ≪ `VideoMode_XCoordShift`) ≫ 1 pixels ([which markers are drawn](../../retail/simulation/heads-down-display.md#which-markers-are-drawn)), so ES1's box is twice ES2's on the same screen in 640-wide mode.

## Drawing it

**`HddCommandScreen_Repaint` (`0003f490`)**, (screen), slot `+4`, the page's full repaint: fills the screen rect with the brush {0, `0x3f`} at the render context's `+0x22c`; `00040948`(screen), ES2's `HddCommandScreen_RefreshOrders`'s place; with an order armed, the bar rect `+0x61` from the armed row's label's *y0* + 3 to its *y1*, filled `0x3b`; the paints (slot `+4` of the table at `+0x1c`) of the display's buttons at `+0x48` and `+0x4c`, `XMIT` and `CANCEL`; while `+0x109` is clear `00040a40`(screen), ES2's `HddCommandScreen_BuildMapMarkers`'s place; `00040d4c`(screen), ES2's `HddCommandScreen_DrawMap`'s place; and the message row's text `+0x140` again while the countdown `+0x13e` runs and `+0x144` is clear. ES2's (`0044c894`) draws the bar in color id 15.

**`HddCommandScreen_Update` (`0003f578`)**, (screen), slot `+8`, once a frame on page 0 through the display's paint stages ([the paint stages](heads-down-display.md#drawing-it)):

1. a queued list click (`+0x14d`) to `HddCommandScreen_HandleListClick`;
2. once the clock passes `+0x149`, `+0x149` = the clock + `0x1e` and `HddCommandScreen_BlinkTick`, so every half second as the display's damage page refresh counts it ([drawing it](heads-down-display.md#drawing-it));
3. a waiting map click (`+0x11c`) to `HddCommandScreen_PickPilot` when no pick is wanted (`+0x103`) or no squadmate is selected, else to `HddCommandScreen_PickTarget`;
4. with a squadmate selected whose saved kind `+0xa7` is 1, either held byte set and the saved unit `+0x9d`'s byte `+0x93` set, both held bytes cleared; ES2's (`0044c960`) tests the saved unit with `+0xa5` and the current unit with `+0xa6` ([the link line](../../retail/simulation/heads-down-display.md#the-link-line)), where ES1's tests the saved unit for either;
5. the rest only at stage 1 or 2 (`+8`):
   - with `HddCommandScreen_MarkerRepaintPending` (`00094866`) set: for a selected squadmate, `00041f90`(the marker at `+0x31`[slot]) and the marker's byte `+0x1b` cleared; the flag cleared;
   - with `+0x139` clear and the armed order changed (`+0x81` other than `+0x5d`): the old row rewritten, `Label_SetTextWithHotkey`(label, the order's attribute byte, the order's text) from `SquadOrderStrings` and `SquadOrderAttributes` ([`STRINGS0.STR` group 0](../formats/str-strings.md#the-groups)), in `cpgreen` with its hotkey in `cpred` when available (`+0x131` + row) and wholly in `ColorSchemePanels`[0] when not, the bar rect filled `0x3f` and the label copied with `Widget_DrawToCockpit`(0, its rect, 1); the new row the same in `cpylw` with its hotkey in `cpred`, the bar rect moved to it and filled `0x3b`; `+0x81` = `+0x5d`. ES2's draws and clears the plate with `HddCommandScreen_DrawOrderHighlight` instead of the bar;
   - with `+0x139` set: `00040948`(screen) and the column from the first row's top-left to the last row's bottom-right copied with `Widget_DrawToCockpit`(0, rect, 1);
   - with `+0x144` set, `000408dc`(screen), ES2's `HddCommandScreen_ClearMessageRow`'s place;
   - with `HddCommandScreen_MapDirty` set: the map viewport filled `0x3f`, `00040d4c`(screen), then the display's (`000bdd28`) source page `+0x38` and draw page `+0x3c` both made its `+0x8c` page, through `00098dd6` and the raster slot `0009df14` and through `00098dda` and `0009df18` when it is the current display; the pilot channel's ([the pilot channel](cockpit-messages.md#the-pilot-channel)) slot `+0x18` (`PilotMessagePort_RedrawFromSaveUnder`), `Widget_DrawToCockpit`(0, the map viewport, 1) and the channel's slot `+0x14` (`PilotMessagePort_RestoreBackground`); then the stage `+8` = 0 and the flag cleared. The pages are not put back ([Open](#open)). ES2's draws the map and clears the two with no page change or copy.

ES2's also steps a counter through 0–3 on each rise of the coarse ticks' bit `0x10`, which ES1's has no counterpart of.

**`HddCommandScreen_BlinkTick` (`0003fe83`)**, (screen), entered at a `NOP`, with no frame, and leaving its argument unread: works on `HddCommandScreenInstance`. Sets `HddCommandScreen_MarkerRepaintPending` and `HddCommandScreen_MapDirty`, flips `HddCommandScreen_BlinkPhase`, sets the display's page 0 update flag `+0xd8` and its stage `+0x75` to 2, and counts the message countdown `+0x13e` down when it runs, setting `+0x144` when it reaches 0. ES2's (`0044d348`, [the selected pilot's marker](../../retail/simulation/heads-down-display.md#the-selected-pilots-marker)).

**`HddCommandScreen_Show` (`0003fbb8`)** and **`HddCommandScreen_Hide` (`0003fc14`)**, (screen), the show and hide slots, are ES2's (`0044cee8`, `0044cf28`): the `int16` state `+0x17` of the order column's region and of the display's `XMIT` and `CANCEL` buttons (`+0x48`, `+0x4c`) to 0 or 2, the map's region untouched ([the two click regions](../../retail/simulation/heads-down-display.md#the-two-click-regions)), and with the pick cursor up (`+0x17c`), `Cockpit_SetOffForwardCursor`(2) or (0) ([the screen edges](cockpit-panel.md#the-screen-edges-and-the-cursors)): `MCURSOR` comes back with page 0 and goes with it.

## Clicks and keys

**Both click regions queue the click, and the update decides it, as ES2's ([the two click regions](../../retail/simulation/heads-down-display.md#the-two-click-regions)).**

- **`HddCommandScreen_QueueListClick` (`0003fed8`)**, (screen, point, region): `+0x14d` = 1, `+0x15b` = `CockpitMouse_DoubleClick` ([the frame's mouse events](cockpit-mouse.md#the-frames-mouse-events--cockpitmouse_processqueue-000444d0)), the region at `+0x14e` and the point at `+0x152`/`+0x156`, and the display's page 0 update flag `+0xd8` and stage `+0x75` = 2. `es2_xref.py` finds it called from `HddCommandScreen_SynthesizeListClick` and from `0004208c`, ES2's `HDDListGadget_OnClick`'s place. ES2's (`0044d3a4`) adds the view offsets to the point.
- **`HddCommandScreen_HandleListClick` (`0003ff34`)**, (screen), is ES2's (`0044d428`). For the order column it tests the point against the eight row labels' rects in order, strictly on all four edges, and acts on the first hit: with no squadmate selected, `HddCommandScreen_SetMessageRow`(screen, 1, `SELECT PILOT`); on the armed row, with the double-click `+0x15b` and the ready byte `+0x104` both set, it presses `XMIT`, the display's `+0x48`, by its click slot `+0xc` with (button, 1) unless its state `+0x17` is 2 or 3, where ES2's calls `Widget_PressChild`; on another row, `HddCommandScreen_SelectOrder`(screen, row + 10); then, for a click a key made (`+0x15a`), `Cursor_SyncPosition` to the map viewport's center in screen coordinates (the canvas context's `+0x210`/`+0x214` added and its origin `+0x220`/`+0x224` taken off), and `+0x15a` cleared. For the map it stores the point at `+0x11d`/`+0x121`, sets `+0x11c` and the display's page 0 update flag and stage. Then `+0x14d` = 0.
- **`HddCommandScreen_SynthesizeListClick` (`00040114`)**, (screen, pick, region), the keys' way in, is ES2's (`0044d598`): `+0x15a` = 1; for the order column, the row label *pick* − 10's top-left plus (1, 1); for the map, pick 0 the pointer's position (`Cursor_GetPosition`, less the canvas context's `+0x210`/`+0x214`, plus its origin) and pick 1 the chosen unit `+0x15c`'s position `+0x20` through the map's camera and `HddMap_ToViewport`; each to `HddCommandScreen_QueueListClick`.

**`HddCommandScreen_KeyDispatch` (`0003f938`)**, (screen, code), the key slot, looks the code up among the 15 dwords of `HddCommandScreen_KeyCodes` (`000948a4`) with `REPNE SCASD` and jumps through the 15 after them, returning 1 in `AL` for a listed code and 0 for any other. Its cases are ES2's ([the rest of the keyboard](../../retail/simulation/heads-down-display.md#the-order-list-and-its-state-machine)):

| Code | Key | Does |
|---|---|---|
| `0x20`, `0x1e`, `0x21`, `0x14`, `0x22`, `0x18`, `0x2e`, `0x12` | `D`, `A`, `F`, `T`, `G`, `O`, `C`, `E` | `HddCommandScreen_SynthesizeListClick`(screen, 10–17, the order column) |
| `0x2d`, `0x0e` | `X`, Backspace | press `XMIT` (`+0x48`) or `CANCEL` (`+0x4c`) as the armed row's double-click does |
| `0x34`, `0x33` | `.`, `,` | `00041904`(screen) or `0004194c`(screen), by their padding, ES2's `HddCommandScreen_NextOrder`'s and `_PreviousOrder`'s places |
| `0x1c` | Enter | with a pick wanted (`+0x103`), through the table `0009491c` on the armed order: for 11 and 12, pick 1 on the map when the chosen unit `+0x15c` is set; else nothing for 11, and pick 0 for 12, 13 and 14 |
| `0x0f` | Tab | with a pick wanted: the chosen unit `+0x15c` = `000419cc`(screen) for 11 or `000419e0`(screen, by its padding) for 12, ES2's `HddCommandScreen_CycleHostileUnit`'s and `_CycleFriendlyUnit`'s places; with one, it becomes the current pick (`+0xa1`, kind 1, `+0xa6`), `+0x104` = 1, `000408dc`(screen) and `Sound_Play`(`0x1a`); with none, `Sound_Play`(`0x1b`) |
| `0x4c` | keypad 5 | the pan offsets `+0x118` and `+0x114` = 0 |

The cases from `0003f982` are undefined bytes in `ES1Recon`, read here as the image's bytes ([Open](#open)).

## Orders and picks

**Selecting a squadmate, arming an order, picking on the map and sending are ES2's state machine ([the order list](../../retail/simulation/heads-down-display.md#the-order-list-and-its-state-machine)) on the offsets above.** The message row's texts are `STRINGS0.STR` group 28 at `000bd490`, `SELECT PILOT`, `SELECT COMMAND`, `DESIGNATE LOCATION` and `DESIGNATE TARGET` ([the groups](../formats/str-strings.md#the-groups)); the orders are group 0's entries 10–17, each one's hotkey index inside its text (`HercWorks.Query strings --group 0`).

- **`HddCommandScreen_SelectOrder` (`000405ac`)**, (screen, order), is ES2's (`0044d9cc`): for 11–14, `Cockpit_SetOffForwardCursor`(2), `HddCommandScreen_SetMessageRow`(screen, 0, `DESIGNATE TARGET` for 11, `DESIGNATE LOCATION` for the rest), `+0x103` and `+0x17c` = 1 and `+0x104` = 0; for any other order, −1 included, `Cockpit_SetOffForwardCursor`(0), `+0x103` = 0, `+0x104` = 1, `+0x17c` = 0 and `000408dc`(screen). When the order differs from `+0x5d`, `+0x81` = `+0x5d`, `+0x5d` = order and the display's page 0 update flag and stage.
- **`HddCommandScreen_SelectPilot` (`00040664`)**, (screen, slot), from `HddDisplay_SelectPilot` ([presses](heads-down-display.md#presses)) and `HddCommandScreen_CancelTransmission`, is ES2's (`0044da70`): with a squadmate selected, `00041884`(screen, its slot), ES2's `HddCommandScreen_TogglePilotButton`'s place, and with an order armed `HddCommandScreen_SelectOrder`(screen, −1), that squadmate's `+0xa6`, `+0x104`, `+0x17d`, `+0x15c` and `+0x103` cleared and `+0x144` set; then `+0x105` = slot; for −1 `000418d8`(screen), and otherwise `00041884`(screen, slot), `000418ac`(screen), ES2's `HddCommandScreen_EnableOrders`'s place, and `+0x17d` = 1; the display's page 0 update flag and stage.
- **`HddCommandScreen_SetMessageRow` (`00040834`)**, (screen, flag, text), is ES2's (`0044dc44`): given no text, `SELECT PILOT` or `SELECT COMMAND` while no pick is wanted, by whether a squadmate is selected, and `DESIGNATE TARGET` for 11 or `DESIGNATE LOCATION` for 12–14 while one is; `000408dc`(screen); `+0x140` = the text; inside the canvas push and pop, `Label_SetText` on the row and `Widget_DrawToCockpit`(0, its rect, 1); and a flag of 1 sets the countdown `+0x13e` to 6, three seconds of blink ticks.
- **`HddCommandScreen_CommitPick` (`0004075c`)**, **`HddCommandScreen_FillOrderRecord` (`0004070c`)** and **`HddCommandScreen_CancelTransmission` (`000407d4`)**, (screen) and (screen, record), are ES2's (`0044db74`, `0044db24`, `0044dbe8`) on ES1's `+0x17d`: `XMIT` runs the first two, the second writing the saved unit to the record's `+0x12` for kind 1 or the saved point to `+6`–`+0xe` for kind 0, and `CANCEL` the third, which arms nothing, drops the selected squadmate's current pick and the selection, and clears `+0x104`, `+0x17d`, `+0x15c` and `+0x103` and sets `+0x144` ([presses](heads-down-display.md#presses)).

### Picks

**`HddCommandScreen_PickPilot` (`000403a8`)**, (screen), is ES2's (`0044d804`): `HddCommandScreen_HitTestMarker` on the map click `+0x11d`; an object whose byte `+0x93` is clear and that `Squad_IndexOf` places in a squad slot is selected with `HddDisplay_SelectPilot`(the display, slot), whether or not it already is; then `+0x11c` = 0 and `HddCommandScreen_MapDirty`.

**`HddCommandScreen_PickTarget` (`00040254`)**, (screen), takes the pick of an armed order that wants one, switching on `+0x5d` through the table at `0009492c`:

- **11 `ATTACK ENEMY` and 12 `DEFEND POSITION`**: the object `HddCommandScreen_HitTestMarker` finds on the map click is taken when `MfdDisplay_InScannerRange`(the cockpit's `+0x1e9`, object) answers 1 ([modes](mfd-display.md#modes)) and its group's (`+0x3f`) side byte `+0x14` is 1 for 11 or 0 for 12: the current pick's unit `+0xa1`, kind `+0xab` 1 and `+0xa6` in the selected squadmate's record, `+0x104` = 1, `Sound_Play`(`0x14`) and `000408dc`(screen). Then for 11, or with `+0x104` set, it is done;
- **12 with `+0x104` clear, 13 `PATROL GRIDPOINT` and 14 `GOTO GRIDPOINT`**: the map cursor `+0x170`–`+0x178` into the current pick's point `+0x91`–`+0x99`, kind 0, `+0xa6`, `+0x104` = 1, `Sound_Play`(`0x14`) and `000408dc`(screen);
- then, for any order, `+0x11c` = 0 and `HddCommandScreen_MapDirty`.

ES2's (`0044d6b8`) takes a unit on its side alone, stores it as the chosen unit `+0x15c` too, and takes the gridpoint for 12 whenever no unit was taken. So in ES1 `+0x104`, which any earlier pick of the armed order has set, keeps `DEFEND POSITION` from moving its gridpoint until the order is sent, re-armed or dropped; and of the code above only `Tab`'s case sets the chosen unit, so `Enter` with `ATTACK ENEMY` after a mouse pick and no `Tab` queues no click ([Open](#open)).

## Functions

| ES1 | Address | ES2 |
|---|---|---|
| `HeapPtr_DtorHddCommandCopy`, `ConfigPart_DtorHddCommandCopy`, `ConfigPart_GetClassTagHddCommandCopy`, `TSPartBase_DtorHddCommandCopy` | `0003edbc`, `0003ede4`, `0003ee23`, `0003ee2c` | the run ([module copies](../formats/compiler.md#vtables)) |
| `HddCommandScreen_Ctor`, `HddPilotPick_ElementCtor`, `HddCommandScreen_Repaint`, `HddCommandScreen_Update` | `0003ee58`, `0003f418`, `0003f490`, `0003f578` | `0044c264`, `004505ef`, `0044c894`, `0044c960` |
| `HddCommandScreen_KeyDispatch`, `HddCommandScreen_Show`, `HddCommandScreen_Hide` | `0003f938`, `0003fbb8`, `0003fc14` | `0044cc40`, `0044cee8`, `0044cf28` |
| `HddMap_ZoomOut`, `HddMap_ZoomIn`, `HddMap_PanLeft`, `HddMap_PanRight`, `HddMap_PanUp`, `HddMap_PanDown` | `0003fc70`, `0003fca4`, `0003fcd4`, `0003fd00`, `0003fd2c`, `0003fd58` | `0044cf68`, `0044cf9c`, `0044cfd0`, `0044d034`, `0044d098`, `0044d0fc` |
| `HddMap_ToViewport`, `HddMap_FromViewport`, `HddCommandScreen_InstallMapContext`, `HddCommandScreen_BlinkTick` | `0003fd84`, `0003fdc4`, `0003fe04`, `0003fe83` | `0044d224`, `0044d260`, `0044d29c`, `0044d348` |
| `HddCommandScreen_QueueListClick`, `HddCommandScreen_HandleListClick`, `HddCommandScreen_SynthesizeListClick` | `0003fed8`, `0003ff34`, `00040114` | `0044d3a4`, `0044d428`, `0044d598` |
| `HddCommandScreen_PickTarget`, `HddCommandScreen_PickPilot`, `HddCommandScreen_HitTestMarker` | `00040254`, `000403a8`, `00040410` | `0044d6b8`, `0044d804`, `0044d860` |
| `HddCommandScreen_SelectOrder`, `HddCommandScreen_SelectPilot`, `HddCommandScreen_FillOrderRecord` | `000405ac`, `00040664`, `0004070c` | `0044d9cc`, `0044da70`, `0044db24` |
| `HddCommandScreen_CommitPick`, `HddCommandScreen_CancelTransmission`, `HddCommandScreen_SetMessageRow` | `0004075c`, `000407d4`, `00040834` | `0044db74`, `0044dbe8`, `0044dc44` |

`ConfigPart_GetClassTagHddCommandCopy` has no Ghidra function.

## Globals

| | Address |
|---|---|
| `HddCommandScreen_MarkerRepaintPending`, `HddCommandScreen_BlinkPhase`, `HddCommandScreen_MapDirty` (bytes) | `00094866`, `00094867`, `00094868` |
| `HddCommandScreenInstance`, the screen | `0009486c` |
| `HDDMapVtable` | `00094886` |
| `HddCommandScreen_KeyCodes`, 15 codes and then their 15 handlers | `000948a4` |
| `HddMapView_Angles`, three `int16` | `000b1604` |

The key slot's `Enter` table and `HddCommandScreen_PickTarget`'s and `HddCommandScreen_SelectOrder`'s jump tables are at `0009491c`, `0009492c` and `0009493c`.

## Open

- **Open:** the rest of the file, `000408dc`–`000420c7`, and `000438a4`: the paint of the order list, the markers and the map (`00040948`, `00040a40`, `00040d4c`), the message row's clear (`000408dc`), the button toggle and the availability (`00041884`, `000418ac`, `000418d8`), the order steps (`00041904`, `0004194c`), the unit cycles (`000419cc`, `000419e0`), `00041994`, which the zooms and the constructor call, the marker (`00041c50`, `00041f90`), the click regions (`00041fcc`, `0004208c`) and the static initializer `000420b0`; what reads the byte `+0x30`, `HddCommandScreen_BlinkPhase`, the pan offsets and the pan step, what sets `+0x139`, and whether any of it sets the chosen unit `+0x15c`; the end of the file's data from `00094802`; and the source path, which no assert in `0003edbc`–`000408db` pushes.
- **Open:** whether `+0x103` and `+0x104` start at 0: the constructor writes neither, and the screen comes from `operator_new` (`000015a4`) with 0 for the screen.
- **Open:** what the map's redraw in `HddCommandScreen_Update` shows with a line up in the pilot channel: it makes the display's `+0x8c` page both source and draw page, runs the channel's slot `+0x18` before the copy and `+0x14` after it, the reverse of the display's stage 1 lift ([the paint stages](heads-down-display.md#drawing-it)), and leaves the pages so. Proposed retail check, by eye: with a squadmate's line up on the look-down view, press a magnifier and watch whether the line or the ticker flickers or is left on the map.
- **Open:** `DEFEND POSITION`'s gridpoint after a first pick. Proposed retail check: select a squadmate, press `F`, click open ground on the map (a blip, the message row clears), then click other open ground: in ES1 no second blip and the squadmate's line stays on the first point.
- **Open:** the undefined bytes in `ES1Recon` of `HddCommandScreen_KeyDispatch`'s cases from `0003f982`, and its shared return at `0003fbaa`–`0003fbb4`, outside Ghidra's extent of the function; a `REPNE SCASD` repair would give them Ghidra code.
- **Open:** a positive control in the running game for the done checklist ([when a module is done](../../herculan/plan-es1-support.md#when-a-module-is-done)). Proposed: on the look-down view in `F7`, `BP <CS>:0003fca4` fires on a click of the zoom-in magnifier, and `BP <CS>:000405ac` on a click on an order row with a squadmate selected, with the order, 10–17, at `[ESP+8]`.
