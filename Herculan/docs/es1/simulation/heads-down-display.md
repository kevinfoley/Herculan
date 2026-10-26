# DBSIM.EXE — the Heads-Down Display

**ES1's cockpit has ES2's Heads-Down Display ([ES2's](../../retail/simulation/heads-down-display.md)): the console below the dashboard, seen by looking down, with two pages, the map (`F7`) and the damage detail (`F8`), a row of buttons, a title, and three squad comm boxes, each showing a squadmate's name, condition and orders, or the squadmate's face talking while its line is up.** What a player would see differently:

- the buttons are frames of the console buttons' bank, and the two page buttons light only while held, where ES2's stay lit on the page shown; `XMIT` and `CANCEL` are captions on a filled box, lit while held, rather than plates;
- the comm boxes stay clickable on both pages;
- a talking squadmate is drawn as a head, one of three picked at random on each paint, with the mouth frame over it;
- the name in a comm box is the `str\pilots` entry at the pilot's portrait number, where ES2's is the entry at the pilot's own number;
- `Ctrl`+`Alt` with keypad 4, keypad 6 and `\` make the first squadmate say the message lines one id at a time, a developer's key the display's key slot holds ([keys](#keys); whether the keys reach it is [Open](#open)).

The button paint draws a hidden button in its second frame ([painting a button](#painting-a-button)); what that looks like on screen is [Open](#open).

Addresses are `ES1DBSIM` image addresses ([the X-32 executable](../formats/x32-executable.md#the-image)); ES2's are DBSIM's. Every function here removes its own arguments, so argument lists are in stack order ([calling conventions](../formats/compiler.md#calling-conventions)). The display is the cockpit's instrument `+0x1f5`, built from the `.GAU` block at 1212 ([the block](../formats/gau-cockpit-layout.md#the-heads-down-display-block-at-1212)); its two pages, the command screen and the damage screen, are the next object files' ([Open](#open)).

## Where the object file starts and ends

The file is `phdd.cpp`, the source path `HddGauge_LoadPilotFrames` pushes at `0003dbda` and `0003dcb0`. Its code is `0003c18c`–`0003edbb` and its tables and data `00094426`–`00094801`. It opens with the run of `ConfigPart`'s copies ([module copies](../formats/compiler.md#vtables)) after the throttle gauge's file's `RET 4` at `0003c186` and three zero bytes ([where the throttle's file ends](cockpit-throttle-gauge.md#where-the-object-file-starts-and-ends)): `HeapPtr_DtorHddCopy` (`0003c18c`), `ConfigPart_DtorHddCopy` (`0003c1b4`), `ConfigPart_GetClassTagHddCopy` (`0003c1f3`) and `TSPartBase_DtorHddCopy` (`0003c1fc`), with the tables `00094426` and `0009445a`. It ends with its static initializer, **`HddDisplay_StaticInit` (`0003edac`)**, the static constructor table's entry `000adc48`: frameless and with no Ghidra function, it zeroes the two dwords of `HddStaticFramePos` (`000b15fc`), and its `RET` at `0003edb8` is followed by three zero bytes and the next file's run at `0003edbc` ([where object files meet](../formats/compiler.md#where-object-files-meet-in-the-code)). The file has no entry in the exit table. The next file's copy of `ConfigPart`'s table is at `00094802`.

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
| `+0xf6` | `+0x12d` | the three comm boxes' gauges, `0x14a` bytes each ([the gauge](#the-comm-boxes)) |
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
4. builds the command screen `0003ee58`(0, display, order column, screen rect) at `+0xca` and the damage screen `00042164`(0, display, damage column, screen rect) at `+0xce`, so the pages' own click regions are registered before the buttons and take a contested pixel, as in ES2 ([widgets](../../retail/simulation/heads-down-display.md#widgets));
5. builds the fifteen buttons ([the buttons](#the-buttons)), each `HddButton_Ctor`(0, *i*, display, `int32` `0x14` + 4*i* to `0x17` + 4*i* plus the origin), stored at `+0x14` + 4*i* and registered with `Widget_RegisterClickable` in the cockpit's root widget's list, and sets `+0x64` to 15;
6. gives `XMIT` (button 13) and `CANCEL` (14) a label each at the button's `+0x2c`, and the display its title label `+0x4d8` on button 9's rect: `Label_Ctor` in `cpred` (`ColorSchemePanels`[2], `00092d48`), `Label_SetRect` with flags 2, centered, and margin (0, 0), and the fill color `0x3f`;
7. sets the title indicator `+0xba` to `int32` 16–19 plus the origin; for each gauge, the marker rect (gauge `+0x2d`) to `int32` `0x50` + 4*i* to `0x53` + 4*i* plus the origin, and the name (gauge `+0x137`) to 0;
8. `+0x4e8` and `+0x4d4` to −1, zeroes `+0x7a`–`+0x89`, and `HddDisplay_SetPage`(display, 0);
9. on the pilot channel, `CockpitView_GetSquadMessagePort`'s port ([the pilot channel](cockpit-messages.md#the-pilot-channel)), `MessagePort_AddBeginCallback`(port, `CommBox_OnMessageBegin` by its padding `0003e485`) and `MessagePort_AddEndCallback`(port, `CommBox_OnMessageEnd` by its padding `0003e53a`) ([the lifecycle](cockpit-messages.md#the-lifecycle));
10. stores the display at `HddDisplayInstance` (`000944cc`); `+0x4e5` = `0xff`; the clip context's origin `+0x220`/`+0x224` = 0, its translation `+0x20c` and clip mode `+0x208` = 1; `+0x4e6` = the block's `int32` `0x5e`; `+0x4ec` = `+0x74`; `+0x4ed` = the clock `000981c4` + `0x1e`;
11. the first time, loads `str\pilots` into `HddPilotNameTable` (`000944d4`) with `StringTable_Ctor` and `StringTable_LoadFromFile`;
12. the subjects: slot 0 the cockpit's machine (`CockpitViewInstance`'s `+0x1ff`) named `YOU` (`000bd3e0`, [`STRINGS0` group 14](../formats/str-strings.md#the-groups)), slots 1–3 none named with the empty string `00094548`, slot 4 none named `TARGET` (`000bd3e4`, group 15); the selector `+0x519` = 0, the subject `+0x4e1` = slot 0's machine, `+0x4dc` = 1 and `+0x51b` = `NO TARGET SELECTED` (`000bd3e8`, group 16).

ES2's (`00448cc8`) also loads the `hdd` bank, which its buttons' frames come from, and the `hddclip` regions, and zeroes the MFD's published block at `+0x766`.

**`HDDisplay_Ctor` (`0003c228`)**, (screen, *a*, *b*), the pages' base, which `es2_xref.py` finds called from `0003ee58` and `00042164`: stores *b* at `+0`, *a* at `+4` and `HDDisplayVtable` (`000944fe`) at `+0xc`. Its slots, which the display calls through a page's `+0xc` ([drawing it](#drawing-it)): `+4` repaint and `+8` update, both 0; `+0xc` hide, `HDDisplay_HideNoOp` (`0003c248`, `RET 4`); `+0x10` show, `HDDisplay_ShowNoOp` (`0003c24b`, `NOP` and `RET 4`); `+0x14` key, `HDDisplay_KeyDispatchNone` (`0003c24f`, `NOP`, `XOR EAX,EAX` and `RET 8`). The three have no Ghidra function. ES2's (`00448cac`) keeps the vptr at `+0` and the two arguments at `+4` and `+8`.

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
| `+0xc` click | `HddButton_OnClick` (`0003ed2c`) | (button, value): slot `+4` of the display's `+0x76` table with (display, value, button), which is `HddDisplay_OnChildClick`; its own invalidate; the mixin's click sound, `Widget_ClickSound` |
| `+0x10` per frame | `HddButton_FlushDeferredPaint` (`0003ecc8`, by its padding `0003ecc5`) | [below](#painting-a-button) |
| `+0x14`–`+0x1c` | `Widget_GetButtonValueHddCopy` (`0003eaff`), `CTLControl_SetValueNoOpHddCopy` (`0003eb09`), `CTLControl_DragMoveNoOpHddCopy` (`0003eb0f`) | the button base's |

`HddButton_MixinVtable` (`0009475e`) holds `HddButton_PaintThunk` (`0003eb24`) and `HddButton_FlushDeferredPaintThunk` (`0003eb30`), each adding −`0x20` to the object and jumping to the paint or the per-frame slot by its padding, and `Widget_ClickSound` at `+0xc`. The copies of the base tables hold this file's stubs: `CTLButtonControl_PaintNoOpHddCopy` (`0003eaef`), `CTLControl_InvalidateNoOpHddCopy` (`0003eaf3`), `CTLControl_OnClickNoOpHddCopy` (`0003eaf7`), `CTLControl_FlushDeferredPaintNoOpHddCopy` (`0003eafb`) and the three above, the code of `panel.cpp`'s ([`panel.cpp`'s copies](cockpit-panel.md#the-screen-edges-and-the-cursors)).

**A click changes nothing on the button itself.** ES2's `HddButton_OnClick` (`0044be50`) flips the button's selection flag first and refuses a lit page button, so ES2's page buttons latch on and a click on the selected comm box puts its flag out of step ([selecting a pilot](../../retail/simulation/heads-down-display.md#selecting-a-pilot)); ES1's lamp `+0x3c` changes in `HddDisplay_SelectPilot` alone.

**`HDDisplayGadget_Ctor` (`0003ed60`)**, (gadget, owner, rect), the base of the pages' click regions, which `es2_xref.py` finds called from `00041c50`: given no object it allocates the base's `0x20` bytes and writes the rest through the null pointer, as `PanelGauge_Ctor` does ([the gauge base](cockpit-widgets.md#the-gauge-base)); `Widget_CtorRect` with the button base's copy and the drag flag 0, then `HDDisplayGadgetVtable` (`000947da`) at `+0x1c`, the owner at `+0x20` and the state 0. That table's paint, invalidate and click slots are 0. ES2's (`0044be94`).

### Painting a button

**`HddButton_Paint` (`0003eb3c`)**, (button), switched on the index `+0x28`, with no test of the state:

- **0–7**: `Bitmap_Blit` of the frame `+0x34` when the state `+0x17` is not 0, else `+0x30`, at the button's top-left. So a page button, an arrow or a magnifier shows its second frame while it is held (state 1) and while it is hidden (state 2).
- **10–12**: by the display's highlight mode `+0x4e6`. Mode 0 fills the button's rect, `0x38` with its lamp set and `0x3f` with it clear. Mode 1, every retail file's, fills a gauge's marker rect (gauge `+0x2d`): with the lamp set, the selected slot's (`+0x4d4`) in `0x3b`; with it clear, the previous slot's (`+0x4e8`) in `0x39`, nothing when that is −1.
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

**`HddDisplay_SetTitle` (`0003d9b8`)**, (display): sets the title label's text to `MAP` (`000bd350`, group 10) on page 0 and to entry `+0x74` of the damage screen (`+0xce`) of `000bd358` (group 11, `" STRUCT DAMAGE"`, `" INTERN DAMAGE"`, `" WEAPON DAMAGE"`) on page 1, and to nothing on another page. ES2's (`0044a6dc`).

## Drawing it

**`HddDisplay_Repaint` (`0003cb90`)**, the paint slot, from `CockpitView_InvalidateWidgets` on a view change ([drawing it](cockpit-panel.md#drawing-it)), while `CockpitView_RectCornerVisible` passes the screen rect `+0x8a`: each button's paint (slot `+4`) in order, with no state test; the current page's show (`+0x10`) and repaint (`+4`); the title indicator `+0xba` filled `0x3b` while `+0x4dc` is set and `0x39` while it is clear; `HddDisplay_SetTitle`; and for each comm box, the box's rect (button 10 + *i*'s) copied into the gauge's video rect `+0x1d` and inset there by one pixel on every side, then filled `0x3f` when the gauge has no name `+0x137` and painted by `HddGauge_PaintIdle` when it has. ES2's (`00449a50`) fills an inset copy and keeps the box's own rect.

**`HddDisplay_Update` (`0003cd28`)**, the per-frame slot, from `Cockpit_DrawFrame` between the canvas push and pop, and with the widgets off as the one instrument still run ([drawing it](cockpit-panel.md#drawing-it)):

1. **The target.** With the cockpit's selection `+0x20c` null, `+0x51b` = `NO TARGET SELECTED`; with one that is not a HERC (its class `int16` `+0x162` not 0), `+0x51b` = `NO INFO AVAILABLE` (`000bd3ec`) and the selection taken as null. The result is subject slot 4, `+0x501`. When it differs from `HddDisplay_LastTarget` (`0009456c`), the subject `+0x4e1` follows it while the selector is 4, `HddDisplay_LastTarget` takes it and page 1's update flag `+0xd9` is set.
2. `HddDisplay_HandleWidgetPress` ([below](#presses)).
3. **Comms out.** On a frame where the view manager's `+0x18` is set and its view `+0x10` is 4, each squadmate whose machine's byte `+0x93` is set gets its gauge's comms-out latch `+0x147`; every frame, each squadmate with `+0x93` and the latch set has its box put in state 1.
4. `HddDisplay_ServiceCommBoxes` ([the comm boxes](#the-state-machine--hdddisplay_servicecommboxes-0003e574)).
5. Outside view 4: the [sensor dropout](cockpit-widgets.md#sensor-dropout) on `Player_DependentCondition`(4), as the gunsight's and the MFD's take it; when `+0x74` differs from `+0x4ec`, the current page's full-repaint flag, and `+0x4ec` = `+0x74`. Then, while `CockpitView_RectCornerVisible` passes the screen rect: each button's per-frame slot `+0x10`; on page 1, once the clock is past `+0x4ed`, the stage `+0x75` = 2, the page's update flag and `+0x4ed` = the clock + `0x1e`, so the damage page refreshes every half second; and the paint stages.

**The paint stages** are ES2's two-frame refresh ([painting is deferred two frames](../../retail/simulation/cockpit-input.md#painting-is-deferred-two-frames)) on the stage `+0x75`, with the page *p* `+0xd2`:

- **Stage 2.** With *p*'s full-repaint flag clear and its update flag set: the stage into the page's `+8` and the page's update (slot `+8`), the stage less 1. With the full-repaint flag set: the page's repaint (`+4`), `HddDisplay_SetTitle`, the stage less 1.
- **Stage 1.** With the full-repaint flag clear and the update flag set: the same update with stage 1, the stage less 1. With it set: `Widget_DrawToCockpit`(0, the screen rect, 0); while the pilot channel has a line up (its `+0x436`) the display's `+0x88` page is made the draw page first and the channel's slots `+0x14` and `+0x18` run before and after the copy, so the line is lifted off the copied screen and put back ([the pilot channel](cockpit-messages.md#the-pilot-channel)); `HddDisplay_SetTitle`, the stage less 1 and the full-repaint flag cleared. Then for page 0 or 1 the update flag is set and the stage put back to 2, and for another page the update flag is cleared.

So once a page is shown its update runs every frame, its stage alternating 2 and 1. ES2's (`00449bd0`) runs `HddDisplay_RepaintPage` and `HddDisplay_PaintCurrentScreen` (`00449ebc`, `00449f3c`) in these places, takes the dropout on dependent 2, pans the map left and right on a view change, and copies with neither the channel's lift nor its own title.

## Presses

**`HddDisplay_OnChildClick` (`0003d430`)**, (display, value, button): the index of the button among the `+0x64` from `+0x14` into the byte `+0x4e5`. ES2's (`0044a14c`).

**`HddDisplay_HandleWidgetPress` (`0003d48c`)**, (display), from the update, is ES2's ([selecting a pilot](../../retail/simulation/heads-down-display.md#selecting-a-pilot), the cases in [the rest of the keyboard](../../retail/simulation/heads-down-display.md#the-order-list-and-its-state-machine)) on ES1's offsets: it returns at once with `+0x4e5` `0xff`, and while the display is dark (`+0x74`) it returns for a button above 1 with `+0x4e5` left as it is, so the press waits; otherwise it switches on the index and puts `0xff` back:

| Button | Page 0 | Page 1 |
|---|---|---|
| 0, 1 | `HddDisplay_SetPage` 0 or 1 | the same |
| 2, 3 | `0003fd2c`, `0003fd58` on the command screen, ES2's `HddMap_PanUp`'s and `_PanDown`'s places; page 0's update flag `+0xd8` and the stage 2 | `0004246c`, `00042494` on the damage screen, ES2's `HddDamageScreen_NextView`'s and `_PrevView`'s places; page 1's full-repaint flag `+0xd7` and the stage 2 |
| 4, 5 | `0003fcd4`, `0003fd00`, ES2's `HddMap_PanLeft`'s and `_PanRight`'s places, the same flags | `HddDisplay_PrevSubject` or `HddDisplay_NextSubject` into `+0x4e1`, then [the title indicator](#subject-stepping) |
| 6, 7 | `0003fc70`, `0003fca4`, ES2's `HddMap_ZoomOut`'s and `_ZoomIn`'s places | nothing |
| 9 | nothing | `0004246c`, as button 2 |
| 10–12 | for the slot *s* = index − 10, when *s* is not `+0x4d4`, `g_SquadmateMachines`[*s*] (`000bcc16`) is set and its `+0x93` clear: `HddDisplay_SelectPilot`(display, *s*), and then, with the command screen's `+0x103` set (the armed order wants a pick), `Cockpit_SetOffForwardCursor`(2), `MCURSOR` | `HddDisplay_SetPage`(display, 0) first, then the same |
| 13, `XMIT` | with the command screen's `+0x104` clear, `00040834`(screen, 1, 0); otherwise `0004075c`(screen) and, with a pilot selected, the screen's word `+0x5d` into the order record `HddOrderRecord` (`000b0ddc`), `0004070c`(screen, record), `Squad_SendOrderToSlot`(the cockpit `+0x10`, the slot, record) ([the squadmates](cockpit-panel.md#the-squadmates)), `Cockpit_SetOffForwardCursor`(0), `000407d4`(screen) and `HddDisplay_SelectPilot`(display, −1); ES2's `HddCommandScreen_SetMessageRow`, `_CommitPick`, `_FillOrderRecord` and `_CancelTransmission` in those places | nothing |
| 14, `CANCEL` | `000407d4`(command screen), `Cockpit_SetOffForwardCursor`(0), `HddDisplay_SelectPilot`(display, −1) | the same |

The page 0 callees are the command screen's file's ([Open](#open)). These are three of the seven calls of `Cockpit_SetOffForwardCursor` `es2_xref.py` finds ([the screen edges](cockpit-panel.md#the-screen-edges-and-the-cursors)); the other four are in `0003fbb8`, `0003fc14` and `000405ac`, in that file.

### Subject stepping

**`HddDisplay_NextSubject` (`0003e8d4`)** and **`HddDisplay_PrevSubject` (`0003e950`)**, (display), are ES2's (`0044b988`, `0044b9e0`, [subject](../../retail/simulation/heads-down-display.md#subject)) on `+0x519` and `+0x4f1`: the selector one place on, 5 wrapping to 0 and −1 to 4, skipping an empty slot by recursing except the target slot 4, which `NextSubject` stops at and `PrevSubject` reaches by its wrap; each returns the slot's machine.

After a step, `HddDisplay_HandleWidgetPress` sets the title indicator from the subject's byte `+0x9d`, the player's machine's ([the fields](mech.md#the-fields)): when the flag `+0x4dc` changes, it fills the indicator `+0xba` with `0x3b` (set) or `0x39` (clear) and copies it with `Widget_DrawToCockpit`(0, the rect one column wider, 1); either way page 1's update flag `+0xd9` and the stage 2.

**`HddDamageScreen_SetSubjectCaption` (`0003e9b8`)**, (display, label), which `es2_xref.py` finds called from `000424f8`: the label's font and fill by the selector, `cpylw` (`ColorSchemePanels`[3]) on `0x32` for the player, `cpred` on `0x3b` for the target, and `cpred` on `HudColorTable_Get`(slot − 1) for a squadmate ([squadmate and pilot helpers](cockpit-panel.md#squadmate-and-pilot-helpers)), then the name `+0x505` + 4 × selector. ES2's (`0044ba2c`) on color ids 6 and 15.

## Keys

**`HddDisplay_KeyDispatch` (`0003d12c`)**, (display, code), the key slot, looks the code up among the 17 dwords of `HddDisplay_KeyCodes` (`00094574`) with `REPNE SCASD` and jumps through the 17 after them; any other code goes to the current page's key slot (`+0x14` of its `+0xc` table), whose answer it returns. It returns 1 for a listed code. In view 1 the cockpit passes it every key it does not take itself, `Tab` included, and `Tab` for the view device's button; `F7` and `F8` the cockpit takes itself, pressing buttons 0 and 1 and then queuing view command 0 ([keys and the view device](cockpit-panel.md#keys-and-the-view-device)). A press is the cockpit's: unless the button's state is 2 or 3, its click slot with (button, 1).

| Code | Key | Does |
|---|---|---|
| `0x48`, `0x50`, `0x4b`, `0x4d` | the arrows (keypad 8, 2, 4, 6) | press buttons 2, 3, 4, 5 |
| `0x02`–`0x04` | `1`–`3` | press buttons 10–12 |
| `0x0c`, `0x4a` | `-`, keypad `-` | press button 6 |
| `0x0d`, `0x4e` | `=`, keypad `+` | press button 7 |
| `0x1f`, `0x17`, `0x11` | `S`, `I`, `W` | `HddDisplay_SetPage`(display, 1), then `000423d8`(the damage screen, 0, 1 or 2), ES2's `HddDamageScreen_SetView`'s place |
| `0x64b` | `Ctrl`+`Alt`+keypad 4 | `HddDebugMessageId` (`00094570`, −1 in the image) made 1 when negative, then less 1 when above 0, and posted |
| `0x64d` | `Ctrl`+`Alt`+keypad 6 | the same, made 1 when negative, then plus 1 when below `0x29` |
| `0x62b` | `Ctrl`+`Alt`+`\` | the same, made 1 when negative |

Each of the last three posts the record {the id as a word, `g_SquadmateMachines`[0]} to the pilot channel with `MessagePort_Post` ([posting](cockpit-messages.md#posting)), so the first squadmate says the line of that id, 0 to `0x29`, from its pilot file. ES2's (`00449fcc`) presses through `Widget_PressChild` and has none of the three.

`HddDisplay_NoOp` (`0003d120`, `RET 8`) and `HddDisplay_ReturnZero` (`0003d124`, `XOR EAX,EAX` and `RET 4`), frameless and with no Ghidra function, stand between the update and the key slot where ES2's `HddDisplay_NoOp` (`00449fbc`) and `HddDisplay_ReturnZero` (`00449fc4`) do; `es2_xref.py` finds no reference to either ([Open](#open)).

## The comm boxes

**Each comm box is ES2's ([squad comm boxes](../../retail/simulation/heads-down-display.md#squad-comm-boxes)): idle, it shows the squadmate's name, condition and orders; while the squadmate speaks it goes to static, then the face talks, then static again.** A gauge is `0x14a` bytes, ES2's `0x14e` less ES2's published-message field, with ES2's offsets up to `+0x149`:

| Gauge | Holds |
|---|---|
| `+0x00`–`+0x14` | the six labels |
| `+0x18` | the pilot's bank |
| `+0x1c` | a byte `CommBox_OnMessageEnd` clears ([Open](#open)) |
| `+0x1d`, `+0x2d` | the video rect, the marker rect |
| `+0x3d` | 30 offset pairs, 8 bytes each, from `ofs\pilot<n>` |
| `+0x12d`, `+0x12e`, `+0x12f` | the message id (byte), the static frame 0–4, the speech slot |
| `+0x133` | 1 for the full bank, 0 for the low-memory one |
| `+0x135`, `+0x137` | the portrait number (`int16`), the name |
| `+0x13b`, `+0x13f`, `+0x143` | the state, the previous state, the deadline |
| `+0x147`, `+0x148`, `+0x149` | the comms-out latch, the scream's phase and its previous value |

**`CommBox_ElementCtor` (`0003cab8`)**, (gauge), allocates `0x14a` bytes when given none, returning 0 when that fails, and runs `__vec_ctor` over the 30 pairs with `Point_DefaultCtorHddCopy` (`0003cb74`, by its padding `0003cb71`), which allocates 8 bytes when given none and returns its argument. ES2's (`0044c0e7`).

### Who is in it — `HddGauge_LoadPilotFrames` (`0003dab4`)

(display, pilot, slot), from `Cockpit_LoadSquadmatePilots` with each squadmate's pilot index, its `int16` `+0x232` ([the squadmates](cockpit-panel.md#the-squadmates)):

1. for slot 0, the pilot is `CommBox0PilotOverride` (`000944d8`) when that is not negative; `Sim_SetCommBox0PilotOverride` (`0003ea50`), (*n*), frameless and with no Ghidra function, stores *n* there, and `Sim_ParseCommandLine` calls it with `atol` of a `-t<n>` argument ([the command line](main-loop.md#the-command-line));
2. the portrait *p* = `Pilot_PortraitOf`(pilot), pilot / 3, at `+0x135`, and the name at `+0x137`: the string of `HddPilotNameTable` reached by *p* steps of `StrTable_NextString` from `StrTable_FirstString`, entry *p* of `str\pilots`; ES2's walks to the pilot's own number;
3. `SystemMessages_Index`(the pilot channel, `Pilot_VoiceBankOf`(*p*), slot), which loads `str\pilot<v>`, *v* = *p* / 4 + 1 with 3 made 4, for the slot's lines ([the string files](cockpit-messages.md#the-string-files)); ES2's passes mode 2 and the voice bank of the same portrait;
4. reads `ofs\pilot<p>` through a `VolRStream`, asserting at severity 5 (`phdd.cpp` line `0x3c1`, `HDDGauge::Couldn't load anim offset`) when it does not open: 27 entries, each an `int32` index *k* and 8 bytes into pair *k* at `+0x3d` + 8*k*;
5. loads the bank `pilot<p>` from the folder `000980f8` names, `dba`, at `+0x18` and sets `+0x133`; with `g_LowMemory` (`000bda98`, [low memory](../formats/chassis-dat.md#low-memory)) set, `pilot<p>s` instead and `+0x133` cleared; asserting at line `0x3d9` (`HDDGauge::Couldn't load bitmap array`) when the bank is null;
6. builds the six labels on button 10 + slot's rect {*x0*, *y0*, *x1*, *y1*}, each `Label_SetRect` with flags 2 and margin (0, 0), *h* the cell height of `cpred` (its `int16` `+10`):

| Label | Rect | Font | Fill |
|---|---|---|---|
| `+0x00` | *x0* + 2 to *x1* − 2, *y0* + 4 to *y0* + 4 + *h* | `cpred` | `HudColorTable_Get`(slot), the slot's color |
| `+0x04` | *x0* to *x1*, *y0* + 16 to *y0* + 16 + *h* | `cpblue` | none |
| `+0x08` | the same, from *y0* + 24 | `cpred` | none |
| `+0x0c` | the same, from *y0* + 32 | `cpblue` | none |
| `+0x10` | the same, from *y0* + 40 | `cpred` | none |
| `+0x14` | *x0* to *x0* + 10, *y1* − 10 to *y1* | `cpred` | `0x3b` |

7. clears the previous state, the state, the static frame, the comms-out latch and the scream's two phase bytes; puts the squadmate's machine in subject slot 1 + slot and the name in name slot 1 + slot; and closes the stream.

The labels are ES2's ([the gauge](../../retail/simulation/heads-down-display.md#the-gauge)) at half ES2's offsets with no coordinate shift. ES2's also puts the box's button back to state 0, where ES1's visibility rows show the boxes on both pages. The sixth label's text is set by none of this file's code, as in ES2 ([the unfilled sixth label](../../retail/simulation/heads-down-display.md#the-unfilled-sixth-label)).

### Starting a line

**`CommBox_OnMessageBegin` (`0003e488`)**, (record), the pilot channel's begin callback, ES2's (`0044b4ec`): for a record whose subject `+2` is in a squadmate slot (`Squad_IndexOf`), `CommBox_BeginMessage`(the display, that gauge, record); when that fails, the channel's ready and cancelled latches `+0x462` and `+0x463`, so the line goes by unshown ([the lifecycle](cockpit-messages.md#the-lifecycle)); when it succeeds, the deadline `+0x143` = the clock + `0x14`, state 1, `Sound_Play`(`0x1c`) unless `Sound_IsPlaying`(`0x1c`), and `HddDisplay_ServiceCommBoxes` while the screen rect is visible. ES2's also hands the MFD the published block and keeps the record.

**`CommBox_BeginMessage` (`0003e060`)**, (display, gauge, record), ES2's (`0044afc8`): the id `+0` into the gauge's `+0x12d`; the suffix `_` + the id in two digits + the record's attribute byte 1 (`+0x16`, [the queue](cockpit-messages.md#the-queue)) in three, zero-padded; and `SpeechLine_Load`(script name, voice name) ([voice lines](../formats/sound-samples.md#voice-lines)) with the script name `P` + the letter `A` + portrait + suffix and the voice name `P` + `Pilot_VoiceBankOf`(portrait) + suffix, so `P1_01000` for id 1, byte 0, in voice bank 1, the shape of the `simvoice\` files. The slot goes to `+0x12f`; it returns whether the slot is set. ES2's names its voice through its language folders.

**`CommBox_OnMessageEnd` (`0003e53c`)**, (record), the end callback: clears the byte `+0x1c` of the subject's gauge. ES2's (`0044b5c0`).

### The state machine — `HddDisplay_ServiceCommBoxes` (`0003e574`)

(display), from the update each frame and from the begin callback; ES2's ([the state machine](../../retail/simulation/heads-down-display.md#the-state-machine--hdddisplay_servicecommboxes-0044b5f8)) on the clock `000981c4`, for each gauge with a name:

| State | Does |
|---|---|
| 0 | while the previous state was not 0 or `CommBox_IdleRepaintPending` (`000946bb`) is set, `HddGauge_PaintIdle` and `Widget_DrawToCockpit`(0, the video rect, 1), `CommBox_IdleRepaintPending` set when that copied nothing |
| 1 | static (`HddGauge_PaintStatic`) until the clock reaches the deadline with the latch clear, then state 2 at once |
| 2 | on entry the channel's ready latch `+0x462` and `Snc_Start`(slot), the frame −1 when that returns 0 and otherwise the slot's `int16` `+8`, with the scream's first deadline the clock + 5; after entry the slot's `+8`. At −1, the channel's cancelled latch `+0x463`; then for the scream (id `0x25`, `%`) the latch and state 1, and otherwise state 3, the deadline the clock + `0x14`, sound `0x1c` unless playing, and the latch when the squadmate's machine has `+0x93`; and static. Otherwise `HddGauge_PaintScream` for the scream and `HddGauge_PaintPilotFrame`(display, frame, gauge) for any other |
| 3 | static until the clock reaches the deadline, then state 0 at once |

and the previous state `+0x13f` = the state. ES2's drains the `.SNC` event queue first, marks and cancels the line through `MessagePort_MarkReady` and `MessagePort_Cancel`, and drops the MFD's published block.

**`Snc_Start` (`0004e518`)**, (slot): copies the script pointer `+0x27` to the cursor `+0xa`, stores `0004e59c`'s answer as the `int16` frame `+8`, `0004e5c4`'s at `+4` and calls `0004e624`(slot, that); then, when the slot's object `+0` is set, `Sound_SpeechEnabled` (`000959e4`) is set and the frame is not 0, returns slot `+0x10` of the table at the object's `+0x3b`, the sound's play, and otherwise 1. So the voice plays while preferences option 2 has speech on ([the option array](preferences.md#the-option-array)), and the face talks either way. ES2's `Snc_Start` (`004632fc`).

### The three paints

**`HddGauge_PaintIdle` (`0003df60`)**, (display, slot), while the screen rect is visible: with the squadmate's machine's `+0x93` clear, inside the canvas push and pop, fills the video rect `0x3f` and sets the five labels' text: the name, `STATUS:` (`000bd4a0`, group 29), the condition `000bd46c`[`HddGauge_ConditionIndex`(display, machine)] (group 24), `OBJECTIVE:` (`000bd4a4`) and `000bd4d4`[`Mech_SquadOrderLineIndex`(machine)] (group 34) ([the order line](mech.md#small-functions)); with it set, `HddGauge_PaintStatic` instead. ES2's (`0044ae78`).

**`HddGauge_ConditionIndex` (`0003ded8`)**, (display, machine), is ES2's (`0044adf4`): `Mech_ReadDamageReadouts`, the mean of entries 1–19 as (`0x100` − mean) × 100 ≫ 8, bucketed at 90, 74, 51 and 1 into 0–4.

**`HddGauge_PaintPilotFrame` (`0003e1cc`)**, (display, frame, gauge), while the screen rect is visible: fills the video rect `0x3f` inside the canvas push and pop; installs the box's clip (`CommBox_PushVideoClip`); then

- with `+0x133` set, the bank's frame 24 + (`Math_RandomNext` (`000b5c48`) mod 3) at its own pair, then the frame at its own pair;
- with it clear, the low-memory bank's frame 6 at pair 24, then frame `HddLowMemFrameMap` (`00094494`)[frame] at the frame's pair;

each with `Bitmap_Blit`; then `CommBox_PopVideoClip` and the name label's text again inside a push and pop. The map holds 28 `int16`, frames 0–23 to 0–5, 24–26 to 6 and 27 to 7, the same values as ES2's `DAT_0049d1f6`, which ES2's paint reads only behind a flag its loader always sets. ES2's (`0044b120`) blits the frame alone, rolling the head's number and drawing nothing with it ([the gauge](../../retail/simulation/heads-down-display.md#the-gauge)), and publishes it for the MFD.

**`HddGauge_PaintScream` (`0003e348`)**, (display, gauge), while the screen rect is visible, is ES2's (`0044b31c`, [the death scream](../../retail/simulation/heads-down-display.md#the-death-scream)): frame `0x1b` through `HddGauge_PaintPilotFrame` until the deadline, then static until it again, each flip setting the deadline the clock + max(5, `Math_RandomBelow`(`0x14`)).

**`HddGauge_PaintStatic` (`0003e400`)**, (display, gauge), while the screen rect is visible: inside the box's clip, `Bitmap_Blit` of frame `+0x12e` of `HddStaticBank` at `HddStaticFramePos`, the frame stepped on and wrapped at 5; then the name label again. ES2's (`0044b3b4`) blits through `VideoMode_BlitRotated` with the coordinate offsets added and publishes the frame for the MFD.

**`CommBox_PushVideoClip` (`0003e790`)**, (gauge): `Cockpit_PushCanvasContext`; keeps the installed render context (`000bdbda`) in `CommBox_SavedContext` (`000bd500`); sets the clip context's rect to the gauge's video rect less that context's origin `+0x220`/`+0x224`; and installs it with `Raster_InstallRenderContext`. **`CommBox_PopVideoClip` (`0003e828`)**: `Cockpit_PopRenderContext`. ES2's (`0044b83c`, `0044b8f8`), whose push also takes off the coordinate offsets.

The callers of the squad and pilot helpers in this file are `CommBox_OnMessageBegin` and `CommBox_OnMessageEnd` (`Squad_IndexOf`), `HddGauge_LoadPilotFrames` (`Pilot_PortraitOf`, `Pilot_VoiceBankOf`, `HudColorTable_Get`), `CommBox_BeginMessage` (`Pilot_VoiceBankOf`), `HddDamageScreen_SetSubjectCaption` (`HudColorTable_Get`) and `HddDisplay_Update` (`Player_DependentCondition`). `HddGauge_Name` (`0003e830`) and `HddGauge_FindByName` (`0003e854`) read the names through `HddDisplayInstance` ([callees of other modules](cockpit-panel.md#callees-of-other-modules)); `Squad_PilotName`, which reads `HddGauge_Name`, gives the outside view's caption its squadmate's name ([the views](main-loop.md#the-views--viewchain_apply-0005894c)), so the caption shows the same `str\pilots` entry the box does.

## Callees of other modules

Read for this file and named with it:

- **`Widget_SetHitShape` (`00043784`)**, in the root widget's file ([hit testing](cockpit-mouse.md#hit-testing)).
- **`Snc_Start` (`0004e518`)**, after `Snc_Load` (`0004e4a8`), [above](#the-state-machine--hdddisplay_servicecommboxes-0003e574).

## Functions

| ES1 | Address | ES2 |
|---|---|---|
| `HeapPtr_DtorHddCopy`, `ConfigPart_DtorHddCopy`, `ConfigPart_GetClassTagHddCopy`, `TSPartBase_DtorHddCopy` | `0003c18c`, `0003c1b4`, `0003c1f3`, `0003c1fc` | the run ([module copies](../formats/compiler.md#vtables)) |
| `HDDisplay_Ctor`, `HDDisplay_HideNoOp`, `HDDisplay_ShowNoOp`, `HDDisplay_KeyDispatchNone` | `0003c228`, `0003c248`, `0003c24b`, `0003c24f` | `00448cac`, `00451e86`, `0044c1eb`, `HddDamageScreen_KeyDispatch` (`00451e8b`) |
| `HddDisplay_Ctor`, `CommBox_ElementCtor`, `Point_DefaultCtorHddCopy` | `0003c258`, `0003cab8`, `0003cb74` | `00448cc8`, `0044c0e7`, `Rtl_ReturnArg` |
| `HddDisplay_Repaint`, `HddDisplay_Update`, `HddDisplay_NoOp`, `HddDisplay_ReturnZero` | `0003cb90`, `0003cd28`, `0003d120`, `0003d124` | `00449a50`, `00449bd0`, `00449fbc`, `00449fc4` |
| `HddDisplay_KeyDispatch`, `HddDisplay_OnChildClick`, `HddDisplay_HandleWidgetPress` | `0003d12c`, `0003d430`, `0003d48c` | `00449fcc`, `0044a14c`, `0044a178` |
| `HddDisplay_SetPage`, `HddDisplay_SetTitle`, `HddDisplay_SelectPilot` | `0003d8b4`, `0003d9b8`, `0003da08` | `0044a5e4`, `0044a6dc`, `0044a720` |
| `HddGauge_LoadPilotFrames`, `HddGauge_ConditionIndex`, `HddGauge_PaintIdle`, `CommBox_BeginMessage` | `0003dab4`, `0003ded8`, `0003df60`, `0003e060` | `0044a7c0`, `0044adf4`, `0044ae78`, `0044afc8` |
| `HddGauge_PaintPilotFrame`, `HddGauge_PaintScream`, `HddGauge_PaintStatic` | `0003e1cc`, `0003e348`, `0003e400` | `0044b120`, `0044b31c`, `0044b3b4` |
| `CommBox_OnMessageBegin`, `CommBox_OnMessageEnd`, `HddDisplay_ServiceCommBoxes`, `CommBox_PushVideoClip`, `CommBox_PopVideoClip` | `0003e488`, `0003e53c`, `0003e574`, `0003e790`, `0003e828` | `0044b4ec`, `0044b5c0`, `0044b5f8`, `0044b83c`, `0044b8f8` |
| `HddGauge_Name`, `HddGauge_FindByName`, `HddDisplay_NextSubject`, `HddDisplay_PrevSubject`, `HddDamageScreen_SetSubjectCaption` | `0003e830`, `0003e854`, `0003e8d4`, `0003e950`, `0003e9b8` | `0044b900`, `0044b928`, `0044b988`, `0044b9e0`, `0044ba2c` |
| `Sim_SetCommBox0PilotOverride`, `HddButton_Ctor` | `0003ea50`, `0003ea5c` | `0044ba9c`, `0044baac` |
| `CTLButtonControl_PaintNoOpHddCopy`, `CTLControl_InvalidateNoOpHddCopy`, `CTLControl_OnClickNoOpHddCopy`, `CTLControl_FlushDeferredPaintNoOpHddCopy`, `Widget_GetButtonValueHddCopy`, `CTLControl_SetValueNoOpHddCopy`, `CTLControl_DragMoveNoOpHddCopy`, `Widget_InvalidateDeferredHddCopy` | `0003eaef`, `0003eaf3`, `0003eaf7`, `0003eafb`, `0003eaff`, `0003eb09`, `0003eb0f`, `0003eb14` | slots `+4`–`+0x1c` of `000946ce`, the code of `panel.cpp`'s copies and of `Widget_InvalidateDeferredGadgetsCopy` (`00031528`) |
| `HddButton_PaintThunk`, `HddButton_FlushDeferredPaintThunk` | `0003eb24`, `0003eb30` | `HDDSelectGadget_PaintThunk` (`0044c1e1`), `HDDSelectGadget_FlushDeferredPaintThunk` (`0044c1d7`) |
| `HddButton_Paint`, `HddButton_FlushDeferredPaint`, `HddButton_OnClick`, `HDDisplayGadget_Ctor`, `HddDisplay_StaticInit` | `0003eb3c`, `0003ecc8`, `0003ed2c`, `0003ed60`, `0003edac` | `0044bb38`, `0044bdf0`, `0044be50`, `0044be94`, `0044c004` |

The stubs from `0003c1f3`, `0003c248`–`0003c24f`, `0003d120`, `0003d124`, `0003ea50`, `0003eaef`–`0003eb0f`, the two thunks and `HddDisplay_StaticInit` have no Ghidra function.

## Globals

| | Address |
|---|---|
| `HddLowMemFrameMap`, 28 `int16` | `00094494` |
| `HddDisplayInstance`, the display | `000944cc` |
| `HddStaticBank`, `dba\static` | `000944d0` |
| `HddPilotNameTable`, `str\pilots` | `000944d4` |
| `CommBox0PilotOverride`, −1 in the image | `000944d8` |
| `HddPageWidgetVisibility`, 2 × 15 bytes | `000944dc` |
| `HDDisplayVtable`, `HDDGaugeVtable` | `000944fe`, `0009451e` |
| `HddDisplay_LastTarget` | `0009456c` |
| `HddDebugMessageId`, −1 in the image | `00094570` |
| `HddDisplay_KeyCodes`, 17 codes and then their 17 handlers | `00094574` |
| `CommBox_IdleRepaintPending` (byte) | `000946bb` |
| the copies of the button base's, the mixin's, the select gadget's and the state gadget's tables | `000946ce`, `000946f6`, `0009470e`, `00094736` |
| `HddButton_MixinVtable`, `HDDSelectGadgetVtable`, `HDDisplayGadgetVtable` | `0009475e`, `00094776`, `000947da` |
| `HddOrderRecord`, the order `XMIT` sends | `000b0ddc` |
| `CommBox_ClipContext`, `HddStaticFramePos` | `000b15f8`, `000b15fc` |
| `CommBox_SavedContext` | `000bd500` |

The constructor's, `HddDisplay_HandleWidgetPress`'s and `HddDisplay_ServiceCommBoxes`'s jump tables are at `0009454c`, `000945fc` and `000946bc`.

## Open

- **Open:** the two pages, the command screen (`0003ee58`) and the damage screen (`00042164`), in the next object files from `0003edbc`, with the page 0 and page 1 callees the presses and keys name (`0003fc70`–`0003fd58`, `0004070c`–`00040834`, `000423d8`–`00042494`) and the other callers of `Cockpit_SetOffForwardCursor` (`0003fbb8`, `0003fc14`, `000405ac`), `Squad_IndexOf` (`000403a8`, `00040a40`) and `HudColorTable_Get` (`00040d4c`); and `HDDisplay_Ctor`'s two arguments, which those constructors pass.
- **Open:** what the second frame of a hidden button looks like on screen: `HddButton_Paint` draws buttons 0–7 with no state test and `HddDisplay_Repaint` paints every button, so on the damage page the two magnifiers (hidden, state 2) are painted in their second frame unless a later paint covers them. Proposed retail check, by eye: on the HERC's look-down view, press `F8` and compare the two magnifier buttons with their look on `F7` and while held.
- **Open:** whether `Ctrl`+`Alt`+keypad 4, keypad 6 and `\` reach the display, through the input build and the axis keys ([the axis keys](input.md#the-axis-keys--input_keyjoyaxiskey-0004c6ec)). Proposed retail check: on the look-down view with a squadmate, `Ctrl`+`Alt`+keypad 6 twice and the squadmate's box should talk a line in the pilot channel each time.
- **Open:** what reads the gauge's byte `+0x1c` that `CommBox_OnMessageEnd` clears: `es2_fieldscan.py --binary ES1DBSIM` over this file finds that write and no access through a gauge's base.
- **Open:** `Snc_Start`'s callees `0004e59c`, `0004e5c4` and `0004e624`, and what advances the slot's frame `+8` while a line plays, since `HddDisplay_ServiceCommBoxes` drains no event queue.
- **Open:** what `str\pilots` and the `ofs\pilot<n>` files hold, through HercWorks.Core: how many names the first holds, whose the portrait number reaches, and which of the 27 entries places frame 27, the scream's; and the sizes of `dba\pbuttons` frames 22–25 and 35–54 against the buttons' rects.
- **Open:** a reference to `HddDisplay_NoOp` (`0003d120`) or `HddDisplay_ReturnZero` (`0003d124`): `es2_xref.py` finds none; a positive control, `HDDisplay_HideNoOp`, finds its slot in `HDDisplayVtable`. It finds no branch to the two thunks, whose one stored pointer each is in `HddButton_MixinVtable`.
- **Open:** a positive control in the running game for the done checklist ([when a module is done](../../herculan/plan-es1-support.md#when-a-module-is-done)). Proposed: in DOSBox-X's debugger, `BP <CS>:0003dab4` fires once for each squadmate as the mission starts, with the pilot index at `[ESP+8]`; and by eye, a squadmate's message shows static in its comm box, then its face talking, then static again.
