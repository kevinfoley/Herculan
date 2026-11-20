# DBSIM.EXE — the Heads-Down Display

**ES1's cockpit has ES2's Heads-Down Display ([ES2's](../../retail/simulation/heads-down-display.md)): the console below the dashboard, seen by looking down, with two pages, the map (`F7`) and the damage detail (`F8`), a row of buttons, a title, and three squad comm boxes, each showing a squadmate's name, condition and orders, or the squadmate's face talking while its line is up.** What a player would see differently:

- the buttons are frames of the console buttons' bank, and the two page buttons light only while held, where ES2's stay lit on the page shown; `XMIT` and `CANCEL` are captions on a filled box, lit while held, rather than plates;
- the comm boxes stay clickable on both pages;
- `Ctrl`+`Alt` with keypad 4, keypad 6 and `\` make the first squadmate say the message lines one id at a time, a developer's key the display's key slot holds ([keys](#keys); whether the keys reach it is [Open](#open)).

The button paint draws a hidden button in its second frame ([painting a button](#painting-a-button)); what that looks like on screen is [Open](#open).

Addresses are `ES1DBSIM` image addresses ([the X-32 executable](../formats/x32-executable.md#the-image)); ES2's are DBSIM's. Every function here removes its own arguments, so argument lists are in stack order ([calling conventions](../formats/compiler.md#calling-conventions)). The display is the cockpit's instrument `+0x1f5`, built from the `.GAU` block at 1212 ([the block](../formats/gau-cockpit-layout.md#the-heads-down-display-block-at-1212)); its two pages are [the command screen](hdd-command-screen.md), the next object file's, and [the damage screen](hdd-damage-screen.md), the one after it, and its comm boxes are [their own doc](hdd-comm-boxes.md).

## Where the object file starts and ends

The file is `phdd.cpp`, the source path `HddGauge_LoadPilotFrames` pushes at `0003dbda` and `0003dcb0`. Its code is `0003c18c`–`0003edbb` and its tables and data `00094426`–`00094801`. It opens with the run of `ConfigPart`'s copies ([module copies](../formats/compiler.md#vtables)) after the throttle gauge's file's `RET 4` at `0003c186` and three zero bytes ([where the throttle's file ends](cockpit-throttle-gauge.md#where-the-object-file-starts-and-ends)): `HeapPtr_DtorHddCopy` (`0003c18c`), `ConfigPart_DtorHddCopy` (`0003c1b4`), `ConfigPart_GetClassTagHddCopy` (`0003c1f3`) and `TSPartBase_DtorHddCopy` (`0003c1fc`), with the tables `00094426` and `0009445a`. It ends with its static initializer, **`HddDisplay_StaticInit` (`0003edac`)**, the static constructor table's entry `000adc48`: frameless and with no Ghidra function, it zeroes the two dwords of `HddStaticFramePos` (`000b15fc`), and its `RET` at `0003edb8` is followed by three zero bytes and the run at `0003edbc` that opens [the command screen's file](hdd-command-screen.md#where-the-object-file-starts-and-ends) ([where object files meet](../formats/compiler.md#where-object-files-meet-in-the-code)). The file has no entry in the exit table. The next file's copy of `ConfigPart`'s table is at `00094802`.

The functions follow ES2's in order, from `HDDisplay_Ctor` (ES2's `00448cac`) to `HddDisplay_StaticInit` (ES2's `0044c004`); ES1's has the two page refreshes inlined in the update, and nothing in the place of ES2's `HddGau_ApplyCoordShift` (`0044bed0`).

## The display object — `HddDisplay_Ctor` (`0003c258`)

**The object is ES2's ([object model](../../retail/simulation/heads-down-display.md#object-model)) with the vptr after its base and every other field `0x37` to `0x43` bytes lower, and no clip regions or published block; it is `0x51f` bytes.**

| ES1 | ES2 | Holds |
|---|---|---|
| `+0x00`–`+0x75` | its base's | the [gauge base](cockpit-widgets.md#the-gauge-base): the block's `int32` 0–3 as its rect, the cockpit `+0x10`, the fifteen buttons from `+0x14`, their count `+0x64`, the dropout from `+0x68`, `+0x74` dark and `+0x75` the paint stage |
| `+0x76` | `+0x00` | the vptr, `HDDGaugeVtable` (`0009451e`) |
| `+0x7a` | `+0xb1` | 16 bytes the constructor zeroes |
| `+0x8a`, `+0x9a`, `+0xaa`, `+0xba` | `+0xc1`, `+0xd1`, `+0xe1`, `+0xf1` | the screen rect, the damage column, the order column and the title indicator |
| `+0xca`, `+0xce` | `+0x101`, `+0x105` | the two pages, the command screen and the damage screen |
| `+0xd2` | `+0x109` | the current page, −1 before the first switch |
| `+0xd6` + page | `+0x10d` + page | a page's full-repaint flag (byte) |
| `+0xd8` + page | `+0x10f` + page | a page's update flag (byte) |
| `+0xf6` | `+0x12d` | the three comm boxes' gauges, `0x14a` bytes each ([the gauge](hdd-comm-boxes.md#the-gauge)) |
| `+0x4d4`, `+0x4e8` | `+0x517`, `+0x52b` | the selected pilot's slot and the previous one, −1 for none |
| `+0x4d8` | `+0x51b` | the title label |
| `+0x4dc` | `+0x51f` | the title indicator flag (byte) |
| `+0x4e1` | `+0x524` | the damage page's subject machine |
| `+0x4e5` | `+0x528` | the pressed button's index (byte), `0xff` for none |
| `+0x4e6` | `+0x529` | the comm boxes' highlight mode (`int16`), the block's `int32` `0x5e` |
| `+0x4ec` | `+0x52f` | the dark byte `+0x74` as the last update saw it |
| `+0x4ed` | `+0x530` | the clock count after which the damage page next refreshes |
| `+0x4f1` | `+0x534` | five subject machines: the player, three squadmates, the target |
| `+0x505` | `+0x548` | their five names |
| `+0x519` | `+0x55c` | the subject selector (`int16`) |
| `+0x51b` | `+0x55e` | the no-subject text |

**`HddDisplay_Ctor` (`0003c258`)**, (display, cockpit, block), from `Gau_PilotRosterWidget` (`0002ce60`) with the block at 1212 ([building the cockpit](cockpit-panel.md#building-it)):

1. allocates `0x51f` bytes when given none, returning 0 when that fails, and runs `PanelGauge_Ctor`(display, cockpit, block); builds the three gauges with `__vec_ctor` and `CommBox_ElementCtor` (`0003cab8`, by its padding `0003cab7`), and stores `HDDGaugeVtable` at `+0x76`;
2. makes the comm boxes' clip context, a `GLViewport_Ctor`(0) at `CommBox_ClipContext` (`000b15f8`), and the first time loads `static` from the folder `000980f8` names, `dba`, into `HddStaticBank` (`000944d0`);
3. with the origin the block's `int32` 0 and 1, sets the screen rect `+0x8a` to `int32` 4–7, the damage column `+0x9a` to 12–15 and the order column `+0xaa` to 8–11, each plus the origin, and `+0xd2` to −1;
4. builds the command screen `HddCommandScreen_Ctor` (`0003ee58`)(0, display, order column, screen rect) at `+0xca` ([building it](hdd-command-screen.md#building-it--hddcommandscreen_ctor-0003ee58)) and the damage screen `HddDamageScreen_Ctor` (`00042164`)(0, display, damage column, screen rect) at `+0xce` ([building it](hdd-damage-screen.md#building-it--hdddamagescreen_ctor-00042164)), so the pages' own click regions are registered before the buttons and take a contested pixel, as in ES2 ([widgets](../../retail/simulation/heads-down-display.md#widgets));
5. builds the fifteen buttons ([the buttons](#the-buttons)), each `HddButton_Ctor`(0, *i*, display, `int32` `0x14` + 4*i* to `0x17` + 4*i* plus the origin), stored at `+0x14` + 4*i* and registered with `Widget_RegisterClickable` in the cockpit's root widget's list, and sets `+0x64` to 15;
6. gives `XMIT` (button 13) and `CANCEL` (14) a label each at the button's `+0x2c`, and the display its title label `+0x4d8` on button 9's rect: `Label_Ctor` in `cpred` (`ColorSchemePanels`[2], `00092d48`), `Label_SetRect` with flags 2, centered, and margin (0, 0), and the fill color `0x3f`;
7. sets the title indicator `+0xba` to `int32` 16–19 plus the origin; for each gauge, the marker rect (gauge `+0x2d`) to `int32` `0x50` + 4*i* to `0x53` + 4*i* plus the origin, and the name (gauge `+0x137`) to 0;
8. `+0x4e8` and `+0x4d4` to −1, zeroes `+0x7a`–`+0x89`, and `HddDisplay_SetPage`(display, 0);
9. on the pilot channel, `CockpitView_GetSquadMessagePort`'s port ([the pilot channel](cockpit-messages.md#the-pilot-channel)), `MessagePort_AddBeginCallback`(port, `CommBox_OnMessageBegin` by its padding `0003e485`) and `MessagePort_AddEndCallback`(port, `CommBox_OnMessageEnd` by its padding `0003e53a`) ([the lifecycle](cockpit-messages.md#the-lifecycle));
10. stores the display at `HddDisplayInstance` (`000944cc`); `+0x4e5` = `0xff`; the clip context's origin `+0x220`/`+0x224` = 0, its translation flag `+0x20c` and clip mode `+0x208` = 1, the clip block's `+0x21c`/`+0x220`, `+0x208` and `+0x204`, the block at the context's `+4` ([pixels](../rendering/2d-drawing.md#pixels)), so its draws are cut to the rect and moved by its corner; `+0x4e6` = the block's `int32` `0x5e`; `+0x4ec` = `+0x74`; `+0x4ed` = the clock `000981c4` + `0x1e`;
11. the first time, loads `str\pilots` into `HddPilotNameTable` (`000944d4`) with `StringTable_Ctor` and `StringTable_LoadFromFile`;
12. the subjects: slot 0 the cockpit's machine (`CockpitViewInstance`'s `+0x1ff`) named `YOU` (`000bd3e0`, [`STRINGS0` group 14](../formats/str-strings.md#the-groups)), slots 1–3 none named with the empty string `00094548`, slot 4 none named `TARGET` (`000bd3e4`, group 15); the selector `+0x519` = 0, the subject `+0x4e1` = slot 0's machine, `+0x4dc` = 1 and `+0x51b` = `NO TARGET SELECTED` (`000bd3e8`, group 16).

ES2's (`00448cc8`) also loads the `hdd` bank, which its buttons' frames come from, and the `hddclip` regions, and zeroes the MFD's published block at `+0x766`.

**`HDDisplay_Ctor` (`0003c228`)**, (screen, *a*, *b*), the pages' base, which `es2_xref.py` finds called from `HddCommandScreen_Ctor` and `HddDamageScreen_Ctor` (`00042164`): stores *b* at `+0`, *a* at `+4` and `HDDisplayVtable` (`000944fe`) at `+0xc`. Its slots, which the display calls through a page's `+0xc` ([drawing it](#drawing-it)): `+4` repaint and `+8` update, both 0; `+0xc` hide, `HDDisplay_HideNoOp` (`0003c248`, `RET 4`); `+0x10` show, `HDDisplay_ShowNoOp` (`0003c24b`, `NOP` and `RET 4`); `+0x14` key, `HDDisplay_KeyDispatchNone` (`0003c24f`, `NOP`, `XOR EAX,EAX` and `RET 8`). The three have no Ghidra function. The command screen and the damage screen pass the display as *a* and the screen rect as *b* ([the command screen's object](hdd-command-screen.md#the-screen-object), [the damage screen's](hdd-damage-screen.md#the-screen-object)). ES2's (`00448cac`) keeps the vptr at `+0` and the two arguments at `+4` and `+8`.

`HDDGaugeVtable` (`0009451e`) holds the gauge base's four slots ([the gauge base](cockpit-widgets.md#the-gauge-base)): `+4` `HddDisplay_OnChildClick` (`0003d430`), `+8` the paint `HddDisplay_Repaint` (`0003cb90`), `+0xc` the per-frame `HddDisplay_Update` (`0003cd28`) and `+0x10` the key slot `HddDisplay_KeyDispatch` (`0003d12c`), the last three by their padding.

## The buttons

**The fifteen buttons are ES2's ([widgets](../../retail/simulation/heads-down-display.md#widgets)) in ES2's index order, built from the `.GAU` rects as read, their pictures frames of `dba\pbuttons`.** `HddButton_Ctor` (`0003ea5c`), (button, index, display, rect), allocates `0x3d` bytes when given none, returning 0 when that fails, and runs the gadget bases' chain ([the gadget bases](cockpit-widgets.md#the-gadget-bases)): `Widget_CtorRect` with the button base's copy `000946ce` and the drag flag 0; the mixin's copy `000946f6` at `+0x24`, the display at `+0x20`, `0009470e` at `+0x1c` (the select gadget's copy), the index at `+0x28` and the state `+0x17` 0; `00094736` (the state gadget's copy) and the lamp `+0x3c` 0; and last `HddButton_MixinVtable` (`0009475e`) at `+0x24` and `HDDSelectGadgetVtable` (`00094776`) at `+0x1c`. The unlit and lit frames go to `+0x30` and `+0x34`.

| Button | Role | Frames of `dba\pbuttons` |
|---|---|---|
| 0, 1 | `F7`, `F8` page buttons | 22 and 23, 24 and 25 |
| 2–5 | the up, down, left and right arrows | *b* and *b* + 1 to *b* + 6 and *b* + 7, *b* the block's `int32` `0x5c` |
| 6, 7 | zoom out, zoom in | 51 and 52, 53 and 54 |
| 8 | none | none; no paint case and no press case |
| 9 | the title box | none; its rect is the title label's |
| 10–12 | the comm boxes | none |
| 13, 14 | `XMIT`, `CANCEL` | none; a label |

With *b* 35 (`0x23`) the constructor gives each arrow the diamond hit shape, `Widget_SetHitShape`(button, 1) ([hit testing](cockpit-mouse.md#hit-testing)); *b* is 35 in `ROADRUNN`, `RAPTOR`, `PANTHER`, `HERCULES` and `COLOSSUS` and 43 in `GUNSLING`, `TOMAHAWK` and `PATRIOT` ([the block](../formats/gau-cockpit-layout.md#the-heads-down-display-block-at-1212)).

`HDDSelectGadgetVtable` (`00094776`), slots from `+4`:

| Slot | ES1 | Does |
|---|---|---|
| `+4` paint | `HddButton_Paint` (`0003eb3c`, by its padding `0003eb3a`) | [below](#painting-a-button) |
| `+8` invalidate | `Widget_InvalidateDeferredHddCopy` (`0003eb14`, by its padding `0003eb13`) | the paint counter `+0x1b` = 2 |
| `+0xc` click | `HddButton_OnClick` (`0003ed2c`) | (button, value): slot `+4` of the display's `+0x76` table with (display, value, button), which is `HddDisplay_OnChildClick`; its own invalidate; the mixin's click sound, `Widget_ClickSound`; the value untested |
| `+0x10` per frame | `HddButton_FlushDeferredPaint` (`0003ecc8`, by its padding `0003ecc5`) | [below](#painting-a-button) |
| `+0x14`–`+0x1c` | `Widget_GetButtonValueHddCopy` (`0003eaff`), `CTLControl_SetValueNoOpHddCopy` (`0003eb09`), `CTLControl_DragMoveNoOpHddCopy` (`0003eb0f`) | the button base's |

`HddButton_MixinVtable` (`0009475e`) holds `HddButton_PaintThunk` (`0003eb24`) and `HddButton_FlushDeferredPaintThunk` (`0003eb30`), each adding −`0x20` to the object and jumping to the paint or the per-frame slot by its padding, and `Widget_ClickSound` at `+0xc`. The copies of the base tables hold this file's stubs: `CTLButtonControl_PaintNoOpHddCopy` (`0003eaef`), `CTLControl_InvalidateNoOpHddCopy` (`0003eaf3`), `CTLControl_OnClickNoOpHddCopy` (`0003eaf7`), `CTLControl_FlushDeferredPaintNoOpHddCopy` (`0003eafb`) and the three above, the code of `panel.cpp`'s ([`panel.cpp`'s copies](cockpit-panel.md#the-screen-edges-and-the-cursors)).

**A click changes nothing on the button itself, and either mouse button presses it.** ES2's `HddButton_OnClick` (`0044be50`) flips the button's selection flag first and refuses a lit page button, so ES2's page buttons latch on and a click on the selected comm box puts its flag out of step ([selecting a pilot](../../retail/simulation/heads-down-display.md#selecting-a-pilot)); ES1's lamp `+0x3c` changes in `HddDisplay_SelectPilot` alone. ES1's passes the value on without testing it, and `HddDisplay_OnChildClick` keeps only the button's index. The value is `Widget_ClickButtons` (`00094b84`), which `Widget_GetButtonValueHddCopy` returns, and `Widget_OnMouseUp` calls the click slot of the widget under a right release, bit 1 set, as well as under the release of a left press on it ([the frame's mouse events](cockpit-mouse.md#the-frames-mouse-events--cockpitmouse_processqueue-000444d0)); so a right click presses a Heads-Down Display button, where `Widget_ForwardClickToOwner`, the panel gadgets' click, acts only with bit 0, the left button's, set ([the gadget bases](cockpit-widgets.md#the-gadget-bases)).

**`HDDisplayGadget_Ctor` (`0003ed60`)**, (gadget, owner, rect), the base of the pages' click regions, which `es2_xref.py` finds called from `HddMarker_Ctor` (`00041c50`): given no object it allocates the base's `0x20` bytes and writes the rest through the null pointer, as `PanelGauge_Ctor` does ([the gauge base](cockpit-widgets.md#the-gauge-base)); `Widget_CtorRect` with the button base's copy and the drag flag 0, then `HDDisplayGadgetVtable` (`000947da`) at `+0x1c`, the owner at `+0x20` and the state 0. That table's paint, invalidate and click slots are 0. ES2's (`0044be94`).

### Painting a button

**`HddButton_Paint` (`0003eb3c`)**, (button), switched on the index `+0x28`, with no test of the state:

- **0–7**: `Bitmap_Blit` of the frame `+0x34` when the state `+0x17` is not 0, else `+0x30`, at the button's top-left. So a page button, an arrow or a magnifier shows its second frame while it is held (state 1) and while it is hidden (state 2).
- **10–12**: by the display's highlight mode `+0x4e6`. Mode 0 frames the button's rect with the brush {4, color}, its outline alone ([brush mode 4](../rendering/2d-drawing.md#ellipses-outlines-and-scaled-bitmaps)), `0x38` with its lamp set and `0x3f` with it clear. Mode 1, every retail file's, fills a gauge's marker rect (gauge `+0x2d`) with the brush {0, color}: with the lamp set, the selected slot's (`+0x4d4`) in `0x3b`; with it clear, the previous slot's (`+0x4e8`) in `0x39`, nothing when that is −1.
- **13**: the label's fill `0x3b` while the state is 1 and the command screen's (`+0xca`) byte `+0x104` is set, else `0x3f`, and the text `XMIT` (`000bd330`, group 8).
- **14**: the fill `0x3b` while the state is 1, else `0x3f`, and `CANCEL` (`000bd334`).
- **8, 9**: nothing.

ES2's (`0044bb38`) paints nothing at state 2 or above, takes its page buttons' frame from the selection flag and captions them `F7` and `F8`, blits a plate under `XMIT` and `CANCEL` with the font chosen by the press, and has `XMIT` redraw the armed order's highlight ([widgets](../../retail/simulation/heads-down-display.md#widgets)).

**`HddButton_FlushDeferredPaint` (`0003ecc8`)**, (button), once a frame through the display's update while `CockpitView_RectCornerVisible` passes the button's rect: at paint counter 2 the paint, at 1 the paint again for a comm box and `Widget_DrawToCockpit`(0, rect, 0) for any other ([drawing it](cockpit-panel.md#drawing-it)), and either way the counter less 1. ES2's (`0044bdf0`).

## Pages and visibility

**`HddDisplay_SetPage` (`0003d8b4`)**, (display, page), when the page differs from `+0xd2`: the old page's hide (slot `+0xc` of its `+0xc` table) unless `+0xd2` is −1, `+0xd2` = page, the new page's show (`+0x10`), and the visibility row `HddPageWidgetVisibility` (`000944dc`) + 15 × page applied to the fifteen buttons: a 1 puts a button at state 2 back to 0 and a 0 puts one at state 0 to 2, each invalidated (slot `+8`). Then, whether or not the page changed, the page's full-repaint flag `+0xd6` + page and the paint stage `+0x75` = 2. ES2's (`0044a5e4`) also unlights the old page's button and lights the new one's.

| Page | Key | Buttons shown | ES2's |
|---|---|---|---|
| 0, the command screen | `F7` | 0–7, 10–14 | 0–7, 13, 14 |
| 1, the damage screen | `F8` | 0–5, 10–12 | 0–5 |

So the comm boxes are shown and clickable on both pages, where ES2's rows hide them ([visibility](../../retail/simulation/heads-down-display.md#visibility)); button 8 is hidden on both.

**`HddDisplay_SetTitle` (`0003d9b8`)**, (display): sets the title label's text to `MAP` (`000bd350`, group 10) on page 0 and to the entry the damage screen's (`+0xce`) view `+0x74` ([views](hdd-damage-screen.md#views)) picks of `000bd358` (group 11, `" STRUCT DAMAGE"`, `" INTERN DAMAGE"`, `" WEAPON DAMAGE"`) on page 1, and to nothing on another page. ES2's (`0044a6dc`).

## Drawing it

**`HddDisplay_Repaint` (`0003cb90`)**, the paint slot, from `CockpitView_InvalidateWidgets` on a view change ([drawing it](cockpit-panel.md#drawing-it)), while `CockpitView_RectCornerVisible` passes the screen rect `+0x8a`: each button's paint (slot `+4`) in order, with no state test; the current page's show (`+0x10`) and repaint (`+4`); the title indicator `+0xba` filled `0x3b` while `+0x4dc` is set and `0x39` while it is clear; `HddDisplay_SetTitle`; and for each comm box, the box's rect (button 10 + *i*'s) copied into the gauge's video rect `+0x1d` and inset there by one pixel on every side, then filled `0x3f` when the gauge has no name `+0x137` and painted by `HddGauge_PaintIdle` when it has. ES2's (`00449a50`) fills an inset copy and keeps the box's own rect.

**`HddDisplay_Update` (`0003cd28`)**, the per-frame slot, from `Cockpit_DrawFrame` between the canvas push and pop, and with the widgets off as the one instrument still run ([drawing it](cockpit-panel.md#drawing-it)):

1. **The target.** With the cockpit's selection `+0x20c` null, `+0x51b` = `NO TARGET SELECTED`; with one that is not a HERC (its class `int16` `+0x162` not 0), `+0x51b` = `NO INFO AVAILABLE` (`000bd3ec`) and the selection taken as null. The result is subject slot 4, `+0x501`. When it differs from `HddDisplay_LastTarget` (`0009456c`), the subject `+0x4e1` follows it while the selector is 4, `HddDisplay_LastTarget` takes it and page 1's update flag `+0xd9` is set.
2. `HddDisplay_HandleWidgetPress` ([below](#presses)).
3. **Comms out.** On a frame where the view manager's `+0x18` is set and its view `+0x10` is 4, each squadmate whose machine's byte `+0x93` is set gets its gauge's comms-out latch `+0x147`; every frame, each squadmate with `+0x93` and the latch set has its box put in state 1.
4. `HddDisplay_ServiceCommBoxes` ([the comm boxes](hdd-comm-boxes.md#the-state-machine--hdddisplay_servicecommboxes-0003e574)).
5. Outside view 4: the [sensor dropout](cockpit-widgets.md#sensor-dropout) on `Player_DependentCondition`(4), as the gunsight's and the MFD's take it; when `+0x74` differs from `+0x4ec`, the current page's full-repaint flag, and `+0x4ec` = `+0x74`. Then, while `CockpitView_RectCornerVisible` passes the screen rect: each button's per-frame slot `+0x10`; on page 1, once the clock is past `+0x4ed`, the stage `+0x75` = 2, the page's update flag and `+0x4ed` = the clock + `0x1e`, so the damage page refreshes every half second; and the paint stages.

**The paint stages** are ES2's two-frame refresh ([painting is deferred two frames](../../retail/simulation/cockpit-input.md#painting-is-deferred-two-frames)) on the stage `+0x75`, with the page *p* `+0xd2`:

- **Stage 2.** With *p*'s full-repaint flag clear and its update flag set: the stage into the page's `+8` and the page's update (slot `+8`), the stage less 1. With the full-repaint flag set: the page's repaint (`+4`), `HddDisplay_SetTitle`, the stage less 1.
- **Stage 1.** With the full-repaint flag clear and the update flag set: the same update with stage 1, the stage less 1. With it set: `Widget_DrawToCockpit`(0, the screen rect, 0), which copies the screen rect from the display's `+0x88` page, the one on the screen, onto `+0x8c` ([drawing it](cockpit-panel.md#drawing-it)); then `HddDisplay_SetTitle`, the stage less 1 and the full-repaint flag cleared. While the pilot channel has a line up (its `+0x436`), the copy is lifted (`0003cff2`–`0003d051`): the display's draw page `+0x3c` is set to its `+0x88`, the channel's slot `+0x14` (`PilotMessagePort_RestoreBackground`) takes the line off that page, the copy runs, and the channel's slot `+0x18` (`PilotMessagePort_RedrawFromSaveUnder`) puts it back ([the pilot channel](cockpit-messages.md#the-pilot-channel)), so the copied screen has no line in it. The draw page is not put back: it stays `+0x88`, the page on the screen, for the rest of the frame, until `Sim_EndFrame` makes `+0x88` the draw page again before it swaps the pair ([the frame's end](cockpit-views.md#cockpitview_stepviewtransition-00022904)). Then for page 0 or 1 the update flag is set and the stage put back to 2, and for another page the update flag is cleared.

The stages run only while `CockpitView_RectCornerVisible` passes the screen rect, which bounds the frames drawn on the screen page. The rect's rows start at 202 or further down the canvas in all eight layouts (the block's `int32` 5 plus `int32` 1), below the 201 rows the test takes from the canvas origin's *y* in views 0, 2 and 3, whose origins are (0, 0) and (±320, 0) ([the pages a view change moves](cockpit-views.md#the-pages-a-view-change-moves)), and the test fails with the widgets off in view 4; so the stages run while the canvas origin is the heads-down display's, (0, 197), and there `Sim_RenderFrame` draws no 3D pass while the current view is 1 and no view change is staged ([the frame](main-loop.md#the-frame)). In the one frame a pan down is staged, `CockpitView_ApplyViewState` has already moved the canvas origin and the current view is still 0, so the 3D pass runs after the stages ([Open](#open)).

So once a page is shown its update runs every frame, its stage alternating 2 and 1. ES2's (`00449bd0`) runs `HddDisplay_RepaintPage` and `HddDisplay_PaintCurrentScreen` (`00449ebc`, `00449f3c`) in these places, takes the dropout on dependent 2, pans the map left and right on a view change, and copies with neither the channel's lift nor its own title.

## Presses

**`HddDisplay_OnChildClick` (`0003d430`)**, (display, value, button): the index of the button among the `+0x64` from `+0x14` into the byte `+0x4e5`. ES2's (`0044a14c`).

**`HddDisplay_HandleWidgetPress` (`0003d48c`)**, (display), from the update, is ES2's ([selecting a pilot](../../retail/simulation/heads-down-display.md#selecting-a-pilot), the cases in [the rest of the keyboard](../../retail/simulation/heads-down-display.md#the-order-list-and-its-state-machine)) on ES1's offsets: it returns at once with `+0x4e5` `0xff`, and while the display is dark (`+0x74`) it returns for a button above 1 with `+0x4e5` left as it is, so the press waits; otherwise it switches on the index and puts `0xff` back:

| Button | Page 0 | Page 1 |
|---|---|---|
| 0, 1 | `HddDisplay_SetPage` 0 or 1 | the same |
| 2, 3 | `HddMap_PanUp` (`0003fd2c`), `HddMap_PanDown` (`0003fd58`) on the command screen ([the map](hdd-command-screen.md#the-map)); page 0's update flag `+0xd8` and the stage 2 | `HddDamageScreen_NextView` (`0004246c`), `HddDamageScreen_PrevView` (`00042494`) on the damage screen ([views](hdd-damage-screen.md#views)); page 1's full-repaint flag `+0xd7` and the stage 2 |
| 4, 5 | `HddMap_PanLeft` (`0003fcd4`), `HddMap_PanRight` (`0003fd00`), the same flags | `HddDisplay_PrevSubject` or `HddDisplay_NextSubject` into `+0x4e1`, then [the title indicator](#subject-stepping) |
| 6, 7 | `HddMap_ZoomOut` (`0003fc70`), `HddMap_ZoomIn` (`0003fca4`) | nothing |
| 9 | nothing | `HddDamageScreen_NextView` (`0004246c`), as button 2 |
| 10–12 | for the slot *s* = index − 10, when *s* is not `+0x4d4`, `g_SquadmateMachines`[*s*] (`000bcc16`) is set and its `+0x93` clear: `HddDisplay_SelectPilot`(display, *s*), and then, with the command screen's `+0x103` set (the armed order wants a pick), `Cockpit_SetOffForwardCursor`(2), `MCURSOR` | `HddDisplay_SetPage`(display, 0) first, then the same |
| 13, `XMIT` | with the command screen's ready byte `+0x104` clear, `HddCommandScreen_SetMessageRow` (`00040834`)(screen, 1, 0); otherwise `HddCommandScreen_CommitPick` (`0004075c`)(screen) and, with a pilot selected, the screen's armed order `+0x5d` as a word into the order record `HddOrderRecord` (`000b0ddc`), the record FLASH COMM's transmit also fills ([the order helpers](mfd-flash-comm.md#the-order-helpers)), `HddCommandScreen_FillOrderRecord` (`0004070c`)(screen, record), `Squad_SendOrderToSlot`(the cockpit `+0x10`, the slot, record) ([the squadmates](cockpit-panel.md#the-squadmates)), `Cockpit_SetOffForwardCursor`(0), `HddCommandScreen_CancelTransmission` (`000407d4`)(screen) and `HddDisplay_SelectPilot`(display, −1) ([orders and picks](hdd-command-screen.md#orders-and-picks)) | nothing |
| 14, `CANCEL` | `HddCommandScreen_CancelTransmission`(command screen), `Cockpit_SetOffForwardCursor`(0), `HddDisplay_SelectPilot`(display, −1) | the same |

On a page other than 0 or 1 the comm boxes, 10–12, do nothing, as buttons 2–7, 9 and `XMIT` do; the page buttons and `CANCEL` act on any page. The page is 0 or 1 from the constructor's `HddDisplay_SetPage`(display, 0) on: each of the seven calls of `HddDisplay_SetPage` `es2_xref.py` finds, in the constructor, the key slot and this function, passes 0 or 1.

The page 0 callees are [the command screen's](hdd-command-screen.md). These are three of the seven calls of `Cockpit_SetOffForwardCursor` `es2_xref.py` finds ([the screen edges](cockpit-panel.md#the-screen-edges-and-the-cursors)); the other four are the command screen's, in `HddCommandScreen_Show` (`0003fbb8`), `HddCommandScreen_Hide` (`0003fc14`) and `HddCommandScreen_SelectOrder` (`000405ac`).

**`HddDisplay_SelectPilot` (`0003da08`)**, (display, slot), makes a squadmate the one an order goes to, or none for −1. With a slot selected (`+0x4d4` not −1) whose comm box (button 10 + slot) has its lamp `+0x3c` lit, it puts the lamp out and invalidates the box (slot `+8`). Then `+0x4e8` = `+0x4d4` and `+0x4d4` = slot. For a slot other than −1 it lights the new box's lamp and invalidates the box when the lamp was out, and posts {`0x22`, `g_SquadmateMachines`[slot]} to the pilot channel with `MessagePort_Post`, so the squadmate answers with its line `0x22` ([posting](cockpit-messages.md#posting)); `Squad_SendOrderToSlot` withdraws that line when the order goes ([the squadmates](cockpit-panel.md#the-squadmates)). Last, for any slot, `HddCommandScreen_SelectPilot` (`00040664`)(the command screen `+0xca`, `+0x4d4`) ([orders and picks](hdd-command-screen.md#orders-and-picks)). ES2's (`0044a720`) posts through the channel's slot 0.

### Subject stepping

**`HddDisplay_NextSubject` (`0003e8d4`)** and **`HddDisplay_PrevSubject` (`0003e950`)**, (display), are ES2's (`0044b988`, `0044b9e0`, [subject](../../retail/simulation/heads-down-display.md#subject)) on `+0x519` and `+0x4f1`: the selector one place on, 5 wrapping to 0 and −1 to 4, skipping an empty slot by recursing except the target slot 4, which `NextSubject` stops at and `PrevSubject` reaches by its wrap; each returns the slot's machine.

After a step, `HddDisplay_HandleWidgetPress` sets the title indicator from the subject's byte `+0x9d`, the player's machine's ([the fields](mech.md#the-fields)), a null subject counting as clear: when the flag `+0x4dc` changes, it fills the indicator `+0xba` with the brush {0, `0x3b`} (set) or {0, `0x39`} (clear) and copies it with `Widget_DrawToCockpit`(0, the rect one column wider, 1); either way page 1's update flag `+0xd9` and the stage 2.

**`HddDamageScreen_SetSubjectCaption` (`0003e9b8`)**, (display, label), which `es2_xref.py` finds called from `HddDamageScreen_Update` (`000424f8`) ([drawing it](hdd-damage-screen.md#drawing-it--hdddamagescreen_update-000424f8)): the label's font and fill by the selector, `cpylw` (`ColorSchemePanels`[3]) on `0x32` for the player, `cpred` on `0x3b` for the target, and `cpred` on `HudColorTable_Get`(slot − 1) for a squadmate ([squadmate and pilot helpers](cockpit-panel.md#squadmate-and-pilot-helpers)), then the name `+0x505` + 4 × selector. ES2's (`0044ba2c`) on color ids 6 and 15.

## Keys

**`HddDisplay_KeyDispatch` (`0003d12c`)**, (display, code), the key slot, looks the code up among the 17 dwords of `HddDisplay_KeyCodes` (`00094574`) with `REPNE SCASD` and jumps through the 17 after them; any other code goes to the current page's key slot (`+0x14` of its `+0xc` table), whose answer it returns. It returns 1 for a listed code. In view 1 the cockpit passes it every key it does not take itself, `Tab` included, and `Tab` for the view device's button; `F7` and `F8` the cockpit takes itself, pressing buttons 0 and 1 and then queuing view command 0 ([keys and the view device](cockpit-panel.md#keys-and-the-view-device)). A press is the cockpit's: unless the button's state is 2 or 3, its click slot with (button, 1).

| Code | Key | Does |
|---|---|---|
| `0x48`, `0x50`, `0x4b`, `0x4d` | the arrows (keypad 8, 2, 4, 6) | press buttons 2, 3, 4, 5 |
| `0x02`–`0x04` | `1`–`3` | press buttons 10–12 |
| `0x0c`, `0x4a` | `-`, keypad `-` | press button 6 |
| `0x0d`, `0x4e` | `=`, keypad `+` | press button 7 |
| `0x1f`, `0x17`, `0x11` | `S`, `I`, `W` | `HddDisplay_SetPage`(display, 1), then `HddDamageScreen_SetView` (`000423d8`)(the damage screen, 0, 1 or 2) ([views](hdd-damage-screen.md#views)) |
| `0x64b` | `Ctrl`+`Alt`+keypad 4 | `HddDebugMessageId` (`00094570`, −1 in the image) made 1 when negative, then less 1 when above 0, and posted |
| `0x64d` | `Ctrl`+`Alt`+keypad 6 | the same, made 1 when negative, then plus 1 when below `0x29` |
| `0x62b` | `Ctrl`+`Alt`+`\` | the same, made 1 when negative |

Each of the last three posts the record {the id as a word, `g_SquadmateMachines`[0]} to the pilot channel with `MessagePort_Post` ([posting](cockpit-messages.md#posting)), so the first squadmate says the line of that id, 0 to `0x29`, from its pilot file. ES2's (`00449fcc`) presses through `Widget_PressChild` and has none of the three.

`HddDisplay_NoOp` (`0003d120`, `RET 8`) and `HddDisplay_ReturnZero` (`0003d124`, `XOR EAX,EAX` and `RET 4`), frameless and with no Ghidra function, stand between the update and the key slot where ES2's `HddDisplay_NoOp` (`00449fbc`) and `HddDisplay_ReturnZero` (`00449fc4`) do; `es2_xref.py` finds no reference to either ([Open](#open)).

## The comm boxes

The three squad comm boxes, buttons 10–12, are [their own doc](hdd-comm-boxes.md): the gauges the constructor builds at `+0xf6`, who is in each, how a line starts, the state machine the update runs and the paints.

The callers of the squad and pilot helpers in this file are `CommBox_OnMessageBegin` and `CommBox_OnMessageEnd` (`Squad_IndexOf`), `HddGauge_LoadPilotFrames` (`Pilot_PortraitOf`, `Pilot_VoiceBankOf`, `HudColorTable_Get`), `CommBox_BeginMessage` (`Pilot_VoiceBankOf`), `HddDamageScreen_SetSubjectCaption` (`HudColorTable_Get`) and `HddDisplay_Update` (`Player_DependentCondition`)..

## Callees of other modules

Read for this file and named with it:

- **`Widget_SetHitShape` (`00043784`)**, in the root widget's file ([hit testing](cockpit-mouse.md#hit-testing)).

## Functions

| ES1 | Address | ES2 |
|---|---|---|
| `HeapPtr_DtorHddCopy`, `ConfigPart_DtorHddCopy`, `ConfigPart_GetClassTagHddCopy`, `TSPartBase_DtorHddCopy` | `0003c18c`, `0003c1b4`, `0003c1f3`, `0003c1fc` | the run ([module copies](../formats/compiler.md#vtables)) |
| `HDDisplay_Ctor`, `HDDisplay_HideNoOp`, `HDDisplay_ShowNoOp`, `HDDisplay_KeyDispatchNone` | `0003c228`, `0003c248`, `0003c24b`, `0003c24f` | `00448cac`, `00451e86`, `0044c1eb`, `HddDamageScreen_KeyDispatch` (`00451e8b`) |
| `HddDisplay_Ctor` | `0003c258` | `00448cc8` |
| `HddDisplay_Repaint`, `HddDisplay_Update`, `HddDisplay_NoOp`, `HddDisplay_ReturnZero` | `0003cb90`, `0003cd28`, `0003d120`, `0003d124` | `00449a50`, `00449bd0`, `00449fbc`, `00449fc4` |
| `HddDisplay_KeyDispatch`, `HddDisplay_OnChildClick`, `HddDisplay_HandleWidgetPress` | `0003d12c`, `0003d430`, `0003d48c` | `00449fcc`, `0044a14c`, `0044a178` |
| `HddDisplay_SetPage`, `HddDisplay_SetTitle`, `HddDisplay_SelectPilot` | `0003d8b4`, `0003d9b8`, `0003da08` | `0044a5e4`, `0044a6dc`, `0044a720` |
| `HddDisplay_NextSubject`, `HddDisplay_PrevSubject`, `HddDamageScreen_SetSubjectCaption` | `0003e8d4`, `0003e950`, `0003e9b8` | `0044b988`, `0044b9e0`, `0044ba2c` |
| `HddButton_Ctor` | `0003ea5c` | `0044baac` |
| `CTLButtonControl_PaintNoOpHddCopy`, `CTLControl_InvalidateNoOpHddCopy`, `CTLControl_OnClickNoOpHddCopy`, `CTLControl_FlushDeferredPaintNoOpHddCopy`, `Widget_GetButtonValueHddCopy`, `CTLControl_SetValueNoOpHddCopy`, `CTLControl_DragMoveNoOpHddCopy`, `Widget_InvalidateDeferredHddCopy` | `0003eaef`, `0003eaf3`, `0003eaf7`, `0003eafb`, `0003eaff`, `0003eb09`, `0003eb0f`, `0003eb14` | slots `+4`–`+0x1c` of `000946ce`, the code of `panel.cpp`'s copies and of `Widget_InvalidateDeferredGadgetsCopy` (`00031528`) |
| `HddButton_PaintThunk`, `HddButton_FlushDeferredPaintThunk` | `0003eb24`, `0003eb30` | `HDDSelectGadget_PaintThunk` (`0044c1e1`), `HDDSelectGadget_FlushDeferredPaintThunk` (`0044c1d7`) |
| `HddButton_Paint`, `HddButton_FlushDeferredPaint`, `HddButton_OnClick`, `HDDisplayGadget_Ctor`, `HddDisplay_StaticInit` | `0003eb3c`, `0003ecc8`, `0003ed2c`, `0003ed60`, `0003edac` | `0044bb38`, `0044bdf0`, `0044be50`, `0044be94`, `0044c004` |

The stubs from `0003c1f3`, `0003c248`–`0003c24f`, `0003d120`, `0003d124`, `0003eaef`–`0003eb0f`, the two thunks and `HddDisplay_StaticInit` have no Ghidra function.

## Globals

| | Address |
|---|---|
| `HddDisplayInstance`, the display | `000944cc` |
| `HddPageWidgetVisibility`, 2 × 15 bytes | `000944dc` |
| `HDDisplayVtable`, `HDDGaugeVtable` | `000944fe`, `0009451e` |
| `HddDisplay_LastTarget` | `0009456c` |
| `HddDebugMessageId`, −1 in the image | `00094570` |
| `HddDisplay_KeyCodes`, 17 codes and then their 17 handlers | `00094574` |
| the copies of the button base's, the mixin's, the select gadget's and the state gadget's tables | `000946ce`, `000946f6`, `0009470e`, `00094736` |
| `HddButton_MixinVtable`, `HDDSelectGadgetVtable`, `HDDisplayGadgetVtable` | `0009475e`, `00094776`, `000947da` |
| `HddOrderRecord`, the order `XMIT` sends and FLASH COMM's transmit broadcasts | `000b0ddc` |

The constructor's and `HddDisplay_HandleWidgetPress`'s jump tables are at `0009454c` and `000945fc`.

## Open

- **Open:** what the second frame of a hidden button looks like on screen: `HddButton_Paint` draws buttons 0–7 with no state test and `HddDisplay_Repaint` paints every button, so on the damage page the two magnifiers (hidden, state 2) are painted in their second frame. The damage screen's floods fill the screen rect ([drawing it](hdd-damage-screen.md#drawing-it--hdddamagescreen_update-000424f8)), and in all eight layouts the magnifiers' rects lie outside it ([the block](../formats/gau-cockpit-layout.md#the-heads-down-display-block-at-1212)). Proposed retail check, by eye: on the HERC's look-down view, press `F8` and compare the two magnifier buttons with their look on `F7` and while held.
- **Open:** whether `Ctrl`+`Alt`+keypad 4, keypad 6 and `\` reach the display. The key slot tests no flag before them, where ES2's developer keys wait for a command-line switch; of ES1's switches, `-P` sets a byte, `000b5b69`, for which `es2_xref.py` finds no reader ([the command line](main-loop.md#the-command-line)). On the way in, keypad 4 and 6 are among the axis keys' codes, which claim them while `SimInput_Enabled` is set ([the axis keys](input.md#the-axis-keys--input_keyjoyaxiskey-0004c6ec)), and `\` (`0x2b`) is in `KeyQueueWantedCodes` and not among them; what the cockpit's own key handling does with the three codes before the display's slot is unread. Proposed retail check: on the look-down view with a squadmate, `Ctrl`+`Alt`+keypad 6 twice and the squadmate's box should talk a line in the pilot channel each time.
- **Open:** what the rest of a frame draws on the screen page after stage 1's lifted copy leaves the draw page at `+0x88` ([the paint stages](#drawing-it)): in view 1 the later steps of `Cockpit_DrawFrame`, `Player_PerFrameCockpitUpdate`, the gunsight's slot and `PilotMessagePort_Update` draw on the page being shown rather than the one `Sim_EndFrame` shows next; and whether the lift runs in the frame a pan down is staged, when `Widget_DrawToCockpit` copies nothing (the view manager's `+0x18` is set) and the 3D pass runs after it. Proposed retail check, by eye: with a squadmate's line up in the pilot channel on the look-down view, press `F8` and `F7` in turn and watch whether the line or the ticker flickers for a frame.
- **Open:** the sizes of `dba\pbuttons` frames 22–25 and 35–54 against the buttons' rects, through HercWorks.Core.
- **Open:** a reference to `HddDisplay_NoOp` (`0003d120`) or `HddDisplay_ReturnZero` (`0003d124`): `es2_xref.py` finds none; a positive control, `HDDisplay_HideNoOp`, finds its slot in `HDDisplayVtable`. It finds no branch to the two thunks, whose one stored pointer each is in `HddButton_MixinVtable`.
- **Open:** a positive control in the running game for the done checklist ([when a module is done](../../herculan/plan-es1-support.md#when-a-module-is-done)). Proposed, by eye: a right click on the `F8` button turns the page to the damage screen ([the buttons](#the-buttons)).
