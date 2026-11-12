# DBSIM.EXE — the in-mission panels

**ES1's in-mission panels, among them the status panel (`P`, `Q`, `Ctrl+Q` and the mission's alerts) and the objectives (`F11`), are built on ES2's panel base ([ES2's](../../retail/simulation/alert-panels.md#what-the-family-shares)): a panel saves the screen under itself and the input block, takes the mouse with a widget root of its own, changes the pointer to the `dci\alert` cursor, runs a loop that reads the controls and repaints its buttons while the mission stands still, and puts everything back when it closes.** What a player could see differently: a key that presses a panel's button ([Return], [Esc], the trigger) runs it without showing it pressed; pressing a button, by a click or a key, also moves the keyboard focus to it, which puts the pointer on the button's center; [Alt+Tab] walks the focus backwards, and keypad 0 forwards only with preferences option 10 on; a button is drawn as a bevelled frame in colors from `STYLES.DAT` with small corner pieces, where ES2's blits a plate picture; the pause and quit prompts are the mission alert's panel at its size, the quit prompt titled `QUIT TO DOS?`; a mission alert's text is always its own string table's, where ES2's shows the mission's objective text for status 5; and the objectives panel lists the lines the shell wrote from the mission's `.ENG` file, left-aligned. Addresses are `ES1DBSIM` image addresses ([the X-32 executable](../formats/x32-executable.md#the-image)); ES2's are DBSIM's. The functions here remove their own arguments, so argument lists are in stack order ([calling conventions](../formats/compiler.md#calling-conventions)): a member function's object first, the rest in reverse of ES2's.

## Where the object files start and end

Five object files lie between [the cockpit's mouse](cockpit-mouse.md#where-the-object-files-start-and-end), whose `CockpitMouse_StaticInitNoOp` ends with a `RET` at `0004487c` and three zero bytes, and the status panel's code at `00046efc`, each start a function on a 4-byte boundary after zero bytes that follow the previous file's last return ([where object files meet](../formats/compiler.md#where-object-files-meet-in-the-code)):

| Code | Holds | Its end |
|---|---|---|
| `00044880`–`00044d4f` | the style table's load ([`STYLES.DAT`](../formats/styles-dat.md#the-loads)) and [the frame drawn from a style](#drawing-a-frame-from-a-style) | `CornerFrame_DrawCorners`' `RET 0x14` at `00044d4c`, then one zero byte |
| `00044d50`–`00044f13` | [the resource loaders and the color helpers](#the-resource-loaders-and-the-color-helpers) | `Raster_DrawLineColour`'s `RET 0x14` at `00044f0e`, then three zero bytes |
| `00044f14`–`000451df` | the string table's functions ([the message boxes' callees](cockpit-messages.md#callees-of-other-modules)) | `StrTable_GetAttributes`' `RET 8` at `000451db`, then two zero bytes |
| `000451e0`–`00045987` | not read here ([Open](#open)); static constructor table entry `000adc54` holds `0004596c` and exit table entry `000adcc0` holds `00045978` | a `RET` at `00045984`, then three zero bytes |
| `00045988`–`00046efb` | `alert.cpp`, whose source path `AlertPanel_CtorBase` pushes at `00045c1c`: the panel base, [`Widget_LoadStylesAndFonts`](../formats/styles-dat.md#the-loads) (`00045e10`), the panel's button and slider | `Alert_StaticInit` (`00046e6c`), entry `000adc58` of the static constructor table, whose `RET` at `00046efa` is followed by one zero byte |

`alert.cpp` opens with no run of `ConfigPart`'s copies ([module copies](../formats/compiler.md#vtables)). `es1_boilerplate.py bounds` reports a `RET 0x36e9` at `00046c8c` followed by a zero byte; those bytes are inside the this-adjusting thunks at `00046c88` and `00046c94` ([the slider](#the-slider)), not a file's end. ES2 keeps the style frame, the loaders and the color helpers in one run (`CornerFrame_Draw` `00453240` to `Raster_DrawLineColour` `00453798`) before its `StringTable_Ctor`.

**The two members read here have files of their own.** The status panel's code, `00046efc`–`00047577`, starts on the 4-byte boundary after `alert.cpp`'s zero byte, with `StatusAlertPanel_Ctor` and no destructor copy; `StatusAlertPanel_GetButtonCaption`'s `RET 8` ends at `00047577`, on the boundary before the preferences panel's code at `00047578`, so no zero fill tells whether `00047578` opens another file ([the preferences panel's files](preferences.md#where-the-object-files-start-and-end)). The objectives panel's file, `0004a18c`–`0004a723`, follows a `RET 4` at `0004a187` and two zero bytes: it opens with `HeapPtr_DtorObjectivesPanelCopy` (`0004a18c`), a lone copy of the 40-byte destructor that opens a run of `ConfigPart`'s copies ([module copies](../formats/compiler.md#vtables)), to which `es2_xref.py` finds no reference, and ends with `ObjectivesPanel_GetButtonCaption`'s `RET 8` at `0004a71f` and two zero bytes. No function of either file asserts, so neither names its source path, and neither has an entry in the static constructor table, whose entry after `Alert_StaticInit`'s (`000adc58`) holds `0004c64c` (`000adc5c`).

## The panel object — `AlertPanel_CtorBase` (`00045988`)

**`AlertPanel_CtorBase`**, (panel, rect, *keepsScreen*), is ES2's ([the family's base](../../retail/simulation/alert-panels.md#what-the-family-shares)) with the vptr after the data. It allocates `0x10` bytes when the panel is null, which is the rect's own constructor inlined ([constructors](../formats/compiler.md#constructors-and-destructors)); `es2_xref.py` finds it called by the five panel constructors `00046efc`, `00047578`, `00048408`, `00049154` and `0004a1b4`, each passing its own object. In order: the rect region at `+0x10` (`GLRectangleRegion`'s chain of this file's vtable copies `00094d8a`, `00094dae`, `00094dd6`, `00094dfe`; type 0), the string table at `+0x30` (`StringTable_Ctor`), the render context at `+0x44` (`GLViewport_Ctor`), the title bar's rect at `+0x2ba`, the cursor record at `+0x2ca` (`GLCursor_Ctor`); `AlertVtable` (`00094e26`) at `+0x314`; the widget count and the close byte cleared; the rect copied to `+0` and to the render context's rect; the region's rect set to (0, 0, *x1* − *x0*, *y1* − *y0*); with *keepsScreen* set, a buffer of `Rect_PixelCount`(region) bytes from `Mem_New` at `+0x28`, asserting `Alert: can't allocate buffer.` (line `0x3b`) when that fails, and 0 otherwise; `+0x2c` cleared; the root widget the mouse works on now (`Widget_ActiveRoot`) kept at `+0x285`; a root of its own, `CTLWindow_Ctor`(0, panel), at `+0x281`, its rect the panel's ([the root widget](cockpit-mouse.md#the-root-widget--ctlwindow_ctor-00043cac)); the display's draw page (`+0x3c` of `000bdd28`) at `+0x289`; −1 at `+0x2e3`; and 1 at `+0x313`.

| ES1 | ES2 | Holds |
|---|---|---|
| `+0`–`+0xc` | `+4`–`+0x10` | the rect *x0*, *y0*, *x1*, *y1*, `int32`, on the screen |
| `+0x10` | `+0x14` | the rect region: its vptr, its type `+0x14` and its rect `+0x18`–`+0x24`, panel-local |
| `+0x28` | `+0x2c` | the screen under the panel, or 0 |
| `+0x2c` | `+0x30` | the plate, frame 0 of a bitmap array the member loads, which its paint blits and `Alert_Dtor` destroys through its slot `+0x1c` with 3; the base leaves it 0 |
| `+0x30` | `+0x34` | the panel's string table, `0x10` bytes |
| `+0x40` | `+0x44` | the title label |
| `+0x44` | `+0x48` | the render context, a `GLViewport`; its clip mode `+0x24c` and translation flag `+0x250` are 1, its rect `+0x254`–`+0x260` the panel's, `+0x264` and `+0x268` 0, and its brush `+0x270` is `Alert_FillBrush` (`000b1718`) |
| `+0x27d` | `+0x281` | the render context `AlertPanel_Enter` found |
| `+0x281` | `+0x285` | the panel's root widget |
| `+0x285` | `+0x289` | the root widget the mouse worked on before |
| `+0x289` | `+0x28d` | the draw page at construction |
| `+0x28d` | `+0x291` | the widgets, 10 pointers (ES2's 15) |
| `+0x2b5` | `+0x2cd` | their count |
| `+0x2b9` | `+0x2d1` | the close byte, which a member's loop runs until it is set |
| `+0x2ba`–`+0x2c6` | none | the title bar's rect ([the title bar](#the-title-bar--alertpanel_settitletext-000461ec)) |
| `+0x2ca` | `+0x2e2` | the cursor record |
| `+0x2ce`, `+0x2d2`, `+0x2da` | `+0x2e6`, `+0x2ea`, `+0x2f2` | the cursor that was up (its bitmap less 4), its hotspot, and the panel's cursor ([putting it up](#putting-a-panel-up-and-taking-it-down)) |
| `+0x2de` | `+0x2f6` | `Cursor_FollowsMouse` as it was |
| `+0x2df` | `+0x2f7` | the focused widget, −1 for none |
| `+0x2e3` | `+0x2fb` | the cancel widget, which [Esc] presses; −1 from the base |
| `+0x2e8`–`+0x312` | `+0x300`–`+0x32f` | the input block as it was, all `0x2b` bytes of `PlayerInputBlock` ([the input block](input.md#the-input-block)); the pointer is `+0x2ea` and `+0x2ee` |
| `+0x313` | `+0x330` | 1: put the cursor back on leaving |
| none | `+0x331` | ES2's close hold |
| `+0x314` | `+0` | the vptr |

`AlertVtable` is ES2's ([`AlertVtable`](../../retail/simulation/alert-panels.md#what-the-family-shares)) one slot further on, with 0 where ES2's holds `Rtl_PureVirtualCalled`: the paint at `+4`, 0; `AlertPanel_SetTitleText` (`000461ec`) at `+8`; the run at `+0xc`, 0; `AlertPanel_OnChildClickNoOp` (`00045d06`, two `NOP`s and `RET 0xc`) at `+0x10`; `AlertPanel_HandleEvent` (`00046670`) at `+0x14`; and the button caption at `+0x18`, 0.

**Drawing on a panel.** The render context's translation, its rect `+0x254` less its `+0x264`, is the panel's corner, and its clip mode 1 cuts every draw to the panel's rect, edges included: a line by `Line_ClipToRect`, a bitmap a column and a row at a time ([lines and clipped bitmaps](../rendering/screen-pages.md#lines-and-clipped-bitmaps)). So a member draws in panel coordinates, and nothing it draws reaches past the panel's rect, which covers *width* + 1 columns and *height* + 1 rows.

**Placing a panel.** `AlertPanel_SetRect` (`0004675c`), (*height*, *width*, *y*, *x*), is ES2's (`00454ef8`): the rect (*x*, *y*, *x* + *width*, *y* + *height*) in `AlertPanel_RectScratch` (`000b1724`), whose address it returns. `AlertPanel_CenterRect` (`0004679c`), (*height*, *width*), is ES2's (`00454f34`) on the screen rect `AlertPanel_ScreenRect` (`000b1708`), {0, 0, 319, 199}, which `Alert_StaticInit` sets: *x* = (319 − *width*) >> 1 and *y* = (199 − *height*) >> 1, where ES2's centers on 640 × 480. Neither scales a coordinate: a member's numbers are the 320 × 200 screen's own pixels as the base sees them. `Alert_StaticInit` also clears `Alert_FillBrush`.

## Putting a panel up and taking it down

**`AlertPanel_Enter` (`00045f00`)**, (panel), ES2's (`00454630`) without the message ports and the present timer:

1. Copies the 43 bytes of `PlayerInputBlock` to `+0x2e8`.
2. Latches buttons 0 to 3, `Input_LatchButton`(1, 1) to (1, 4) ([small functions](input.md#small-functions)).
3. `AlertPanel_SetFocus`(panel, −1) ([focus](#focus)), then `Widget_ClearPressed`(the panel's root), which hands the mouse to the panel's widgets.
4. Keeps the render context (`000bdbda`) at `+0x27d` and installs the panel's (`Raster_InstallRenderContext`); sets the display's source and draw pages to its `+0x88`, the page on the screen ([the frame's end](cockpit-views.md#cockpitview_stepviewtransition-00022904)), so the panel draws where it is seen.
5. Keeps `Cursor_FollowsMouse` (`00094c5b`) at `+0x2de` and sets it, so the pointer moves the drawn cursor ([the cockpit's mouse](cockpit-mouse.md#the-callback-and-the-queue)).
6. Calls `AlertPanel_EnterHook` (`00094d80`) when it is set: `SimInput_PushDisable`, which `Sim_StartMission` stores there ([small functions](main-loop.md#small-functions)), so every key reaches the command queue ([the simulator's two subscribers](input.md#the-simulators-two-subscribers)).
7. The first time, loads `dci\alert` into `AlertPanel_CursorImage` (`00094d7c`) with `Cursor_LoadWrapper`.
8. Hides the cursor (slot 31, [the cursor](../rendering/screen-pages.md#the-cursor)); with a buffer at `+0x28`, saves the screen under the region into it (`Region_Save`), the region's rect moved by minus the panel's corner for the call, as ES2's: `Region_SaveRect` (`00066470`) adds the render context's translation, the corner, and `Bitmap_GrabClipDispatch` (`0006aa70`) adds it again ([lines and clipped bitmaps](../rendering/screen-pages.md#lines-and-clipped-bitmaps)), so the grab reads the screen under the panel, cut to its rect; calls the panel's paint, slot `+4`; `AlertPanel_LoadCursor`(panel, the alert cursor), which makes it the drawn cursor ([the cursor](../rendering/screen-pages.md#the-cursor)); shows the cursor (slot 30).
9. Adds 1 to `AlertPanel_OpenCount` (`00094d78`, `int16`), which `es2_xref.py` finds also read by `MessagePort_CopyBoxBetweenPages`.

ES2's also cancels and pauses the squad message port, pauses the cockpit view's, clears two flags when no panel is up, pumps the window's messages and resets the present timer.

**`AlertPanel_Leave` (`000460b8`)**, (panel), ES2's (`004548ac`) without the present, the two flags and the message ports:

1. Clears `Cursor_FollowsMouse` and hides the cursor; with a buffer, puts the saved screen back (`Region_Restore`).
2. `AlertPanel_RestoreCursorImage`(panel); `Cursor_FollowsMouse` back from `+0x2de`, and the cursor shown again when that is set.
3. Calls `AlertPanel_LeaveHook` (`00094d84`) when it is set: `SimInput_PopEnable`.
4. Installs the render context from `+0x27d` and makes the draw page `+0x289` again; the source page stays the `+0x88` page `AlertPanel_Enter` set.
5. Deletes the panel's root (`WidgetRoot_Delete`) and, when `+0x285` is set, hands the mouse back to that root (`Widget_ClearPressed`).
6. Copies the 43 bytes at `+0x2e8` back to `PlayerInputBlock`; with a mouse device (`Input_GetMouseDevice`), moves the mouse to the saved pointer (`Mouse_SetPosition`) and the drawn cursor with it (`Cursor_SyncPosition`).
7. Takes 1 from `AlertPanel_OpenCount`.

**`AlertPanel_LoadCursor` (`000463c0`)**, (panel, cursor), is ES2's (`00454b70`) without the scroll-window path: keeps `Cursor_Bitmap` less 4 at `+0x2ce`, `Cursor_Hotspot` at `+0x2d2` and `+0x2d6`, and the cursor at `+0x2da`, then `Cursor_SetImage`(the cursor's `+4` plus 4, or 0 when that is 0; the cursor's `+8`). **`AlertPanel_RestoreCursorImage` (`00046428`)**, (panel), ES2's (`00454c10`): while `+0x313` is set, `Cursor_SetImage` with the kept bitmap (plus 4, or 0) and hotspot.

## The loop

The loop is each member's, as in ES2. The two read here, `StatusAlertPanel_RunModal` (`0004730c`) and `ObjectivesPanel_RunModal` (`0004a4ec`), each slot `+0xc` of its member's table, are the same code: `AlertPanel_Enter`; `AlertPanel_SetFocus`(panel, 0), which puts the pointer on widget 0; then, until the close byte `+0x2b9` is set, `Input_ReadPlayerInput` ([the input read](input.md#reading-the-players-input--input_readplayerinput-0004cb14)), whose mouse events now go to the panel's root, its block handed to slot `+0x14`, the key handler, and slot `+0x10`, the per-frame paint, of each widget in turn; then `AlertPanel_Leave`; and it returns bit 0 of the close byte. So the loop calls neither `Sim_MainTick` nor `Sim_EndFrame`: the mission stands still, nothing presents a frame or flips a page, the panel's widgets draw straight onto the page on the screen, and the loop goes round as fast as the input read returns, with no wait. ES2's loops also service the press flashes and present ([ES2's](../../retail/simulation/alert-panels.md#keys-and-the-press-flash)). The preferences and controls panels' loops are [`preferences.md`'s](preferences.md#the-loop--preferencespanel_run-00047a88); the preferences panel's draws a frame of the outside view and flips the pages each pass.

## Keys — `AlertPanel_HandleEvent` (`00046670`)

(panel, block): ES2's ([keys and the press flash](../../retail/simulation/alert-panels.md#keys-and-the-press-flash)) on ES1's block ([the input block](input.md#the-input-block)), in this order, each press through `AlertPanel_PressWidget` with flags 1; it returns 1 for either button and for each of the five codes, even when the case does nothing ([Esc] with no cancel widget, keypad 0 with option 10 off), and 0 for any other code:

| | |
|---|---|
| button 0, the trigger (`+0x15`) | presses the focused widget, or focuses widget 0 (`AlertPanel_SetFocus`(panel, 0)) when there is none; `Input_LatchButton`(1, 1) |
| button 1 (`+0x16`) | focuses the next widget; `Input_LatchButton`(1, 2) |
| `0x01`, [Esc] | presses the cancel widget `+0x2e3` when it is not −1 |
| `0x0f`, [Tab] | focuses the next widget |
| `0x20f`, [Alt+Tab] | focuses the previous widget |
| `0x1c`, [Return] | as button 0, without the latch |
| `0x52`, keypad 0 | focuses the next widget while preferences option 10 (`000bd56c`, [the axis keys](input.md#the-axis-keys--input_keyjoyaxiskey-0004c6ec)) is on |

The command word keeps the `0x200` of [Alt] under the input read's mask ([the command word](input.md#the-command-word)), so [Alt+Tab] reaches the handler, where ES2's previous-widget code, [Shift+Tab], loses its `0x800` to that mask. The codes are found by `REPNE SCASD` in the 5 dwords at `00094eec`, their targets 5 dwords after.

**`AlertPanel_PressWidget` (`0004662c`)**, (panel, flags, index): unless the widget at that index is in state 2 or 3, calls its click slot `+0xc` with the flags. ES2's (`00454dcc`) refuses state 2 alone and then flashes the widget on the panel's root ([the press flash](../../retail/simulation/cockpit-input.md#the-press-flash)); ES1's root has no flash list ([the root widget](cockpit-mouse.md#the-root-widget--ctlwindow_ctor-00043cac)) and `AlertPanel_PressWidget` makes no other call, so a key that presses a button runs it without putting it in state 1. Whether anything else shows it pressed is [Open](#open).

## Focus

**Focus is the pointer, as in ES2** ([keys and the press flash](../../retail/simulation/alert-panels.md#keys-and-the-press-flash)):

- **`AlertPanel_SetFocus` (`00046470`)**, (panel, index), ES2's (`00454c7c`): for an index of −1, with a mouse device, runs `Input_ReadPlayerInput` and takes the first widget whose rect holds the block's pointer (`+2`, `+6`), edges included, the pointer in screen pixels tested against the widgets' panel-local rects without the panel's corner, staying −1 when none does or there is no mouse device; for any other index, puts the pointer on the widget's center plus the panel's origin, ((*x0* + (*x1* − *x0*) >> 1) + the panel's *x0*, the same in *y*), with `Mouse_SetPosition` when there is a mouse device, and `Cursor_SyncPosition` either way. Stores the index at `+0x2df`.
- **`AlertPanel_FocusChild` (`00046584`)**, (panel, widget), ES2's (`00454d48`): focuses the widget's index in the list, and does nothing for a widget not in it.
- **`AlertPanel_FocusNext` (`000465d8`)** and **`AlertPanel_FocusPrevious` (`00046600`)**, (panel), ES2's (`00454d7c`, `00454da0`): the next, focus + 1, or 0 when that equals the count; and the previous, focus − 1 unless the focus is −1, the last when that leaves it below 0.

## The panel's buttons

**A panel button is ES2's `AlertSelectGadget` ([the gadget classes](../../retail/simulation/cockpit-input.md#the-cockpits-own-gadget-classes)) on ES1's gadget layout ([the gadget bases](cockpit-widgets.md#the-gadget-bases)), drawn as a style's frame rather than a picture.** `PanelButton_Ctor` (`000467c8`), (button, index, owner, rect), allocates `0x54` bytes when given none, returning 0 when that fails; builds the widget on the rect (`Widget_CtorRect`) through this file's copies of the button tables (`00094f16`, then `00094f56`, whose click slot is `Widget_ForwardClickToOwner`), the mixin's tables `00094f3e` and `00094f7e` at `+0x24`, the owner at `+0x20`, the index at `+0x28`, state 0 and drag flag 0; stores `AlertSelectGadgetVtable` (`00094f96`) at `+0x1c`; and fills two three-entry tables indexed by the state: style records 0, 1 and 2 of the [style table](../formats/styles-dat.md#the-layout) at `+0x3c`, `+0x40` and `+0x44`, and the fonts `active`, `pushed` and `inactive` (`000bd54a`, `000bd54e`, `000bd552`) at `+0x48`, `+0x4c` and `+0x50`. ES2's (`00454f64`, (button, rect, owner, index)) fills a four-entry font table and no styles. The constructor leaves the caption label `+0x2c` unset.

| Slot of `00094f96` | ES1 | ES2's |
|---|---|---|
| `+4` paint | `PanelButton_Paint` (`000468e0`) | `PanelButton_Paint` (`00454ff8`) |
| `+8` invalidate | `Widget_InvalidateDeferredAlertCopy` (`000468b8`): `+0x1b` = 2 | `Widget_InvalidateDeferred` (`00455524`) |
| `+0xc` click | `PanelButton_OnClick` (`000469ac`) | `PanelButton_OnClick` (`00455080`) |
| `+0x10` per frame | `PanelButton_Tick` (`0004698c`) | `PanelButton_Tick` (`00455064`) |
| `+0x14`, `+0x18`, `+0x1c` | this file's copies of the button base's value, set-value and drag slots | the same |

The mixin's table `00094f7e` holds `AlertSelectGadget_PaintThunk` (`000468c8`) and `AlertSelectGadget_TickThunk` (`000468d4`), each `ADD dword ptr [ESP+4], -0x20` and a jump to the paint or the tick, and `Widget_ClickSound` ([the gadget bases](cockpit-widgets.md#the-gadget-bases)).

- **`PanelButton_Paint` (`000468e0`)**, (button): hides the cursor; draws [the frame](#drawing-a-frame-from-a-style) of the style for its state (`+0x3c` + 4 × state) on its rect; asks the owner for the caption, slot `+0x18` with (owner, index); sets the caption label's font to the font for its state (`+0x48` + 4 × state) and its text (`Label_SetText`, [labels](cockpit-widgets.md#labels)); shows the cursor. The rect goes by value through `Rect_CopyCtorAlertCopy` (`0004695c`), (copy, rect), which allocates `0x10` bytes when given none. ES2's blits a plate picture for the state instead.
- **`PanelButton_Tick` (`0004698c`)**, (button), ES2's: when the paint counter `+0x1b` is not 0, paints once and takes 1 from it, so a button invalidated with 2 is painted on the next two passes.
- **`PanelButton_OnClick` (`000469ac`)**, (button, flags): the owner's slot `+0x10` with (owner, flags, button), then `AlertPanel_FocusChild`(owner, button), its own invalidate and the mixin's click sound. ES2's (`00455080`) does not move the focus. So every press of a button, a mouse click or a key through `AlertPanel_PressWidget`, focuses it and puts the pointer on its center: [Esc] moves the pointer to the cancel widget.

A mouse press shows the button pressed as in the cockpit: `Widget_OnMouseDown` puts it in state 1 and repaints it, and the release puts state 0 ([press, release and drag](cockpit-mouse.md#press-release-and-drag)).

## Drawing a frame from a style

**`CornerFrame_Draw` (`00044954`)**, (style, rect by value), draws a bevelled box from one 64-byte record of the [style table](../formats/styles-dat.md#the-layout): the inside, three rings of one-pixel edges and a small picture in each corner. It is ES2's `CornerFrame_Draw` (`00453240`), which `es2_xref.py` finds no caller of in ES2; ES1's panel buttons draw with it. With the render context the display's `+0x44` less 4 points at:

1. Fills (*x0* + 3, *y0* + 3)–(*x1* − 3, *y1* − 3), ordered, with `Raster_FillRect` under the brush {0, the record's dword `+0`}, put at the context's `+0x22c` and taken off after.
2. `CornerFrame_DrawEdgeRing` (`00044adc`), (style, *first*, *grow*, *inset*, rect by value), three times: (1, 0, 0), (5, −2, 1) and (9, −4, 2). With *left* = *x0* + *inset*, *top* = *y0* + *inset*, *w* = *x1* − *x0* + *grow* and *h* = *y1* − *y0* + *grow*, it draws with `Raster_DrawLine`, each under the pen {0, color} at the context's `+0x228` and skipped when that color dword is 0: dword *first* the left edge (*left*, *top* + 1)–(*left*, *top* + *h* − 1), dword *first* + 1 the top edge (*left* + 1, *top*)–(*left* + *w* − 1, *top*), *first* + 2 the right edge (*left* + *w*, *top* + 1)–(*left* + *w*, *top* + *h* − 1), *first* + 3 the bottom edge (*left* + 1, *top* + *h*)–(*left* + *w* − 1, *top* + *h*). A line is drawn with both its end pixels ([lines and clipped bitmaps](../rendering/screen-pages.md#lines-and-clipped-bitmaps)), so each edge leaves its two corners open. ES2's (`004533a4`).
3. `CornerFrame_DrawCorners` (`00044c64`), (style, rect by value): blits frames of the `dba\corners` bank (`WidgetCorners`, `000bd53e`) with flags 0, the record's `int16` `+0x34` at (*x0*, *y0*), `+0x36` at (*x1* − *cw*, *y0*), `+0x38` at (*x0*, *y1* − *ch*) and `+0x3a` at (*x1* − *cw*, *y1* − *ch*), *cw* and *ch* being its `int16` `+0x3c` and `+0x3e`. ES2's (`00453518`).

`CornerFrame_Draw` passes the rect to each by value through `Rect_CopyCtor` (`00044aac`), (copy, rect), which allocates `0x10` bytes when given none.

## The title bar — `AlertPanel_SetTitleText` (`000461ec`)

(panel, text), slot `+8`: frames the title bar's rect `+0x2ba` (*x0*, *y0*, *x1*, *y1*) with `Raster_DrawLineColour`: the top edge (*x0*, *y0*)–(*x1*, *y0*) and the left edge (*x0*, *y0*)–(*x0*, *y1*) in color `0x10`, the right edge (*x1*, *y1*)–(*x1*, *y0*) in `0x1b` and the bottom edge (*x1*, *y1*)–(*x0*, *y1*) in `0x1c`; blits corner frames 0, 1, 2 and 3 of `WidgetCorners` with flags 0 at (*x0*, *y0*), (*x1* − 4, *y0*), (*x0*, *y1* − 4) and (*x1* − 4, *y1* − 4); then sets the title label `+0x40`'s text (`Label_SetText`), which draws it. ES2's slot (`AlertPanel_SetTitleText`, `00454b58`) sets the text alone.

## The resource loaders and the color helpers

ES2's, each (its arguments in reverse of ES2's):

- **`CockpitFont_Load` (`00044d50`)** and **`CockpitCorners_Load` (`00044d88`)**, (path, slot), the same code: open the path as a `VolRStream`, store `ClassItem_LoadResource`'s result in the slot, destroy the stream. `Widget_LoadStylesAndFonts` loads the seven fonts with the first and `dba\corners` with the second ([the loads](../formats/styles-dat.md#the-loads)). ES2's `004535ec` and `00453640`.
- **`CockpitResource_Load` (`00044dc0`)**, (path, slot): the same with `VolRStream_Close` before the destroy, ES2's (`00453694`).
- **`Raster_FrameRectColour` (`00044e00`)**, (color, rect by value): `Raster_FillRect` on the rect under the brush {4, color}, which draws its four edges, at the render context's (`000bdbda`) `+0x22c`, then the brush put back. ES2's (`004536f4`).
- **`Raster_FillFrameRectColours` (`00044e50`)**, (frame color, fill color, rect by value): the same fill under {0, fill color} and then under {4, frame color}. ES2's (`00453738`).
- **`Raster_DrawLineColour` (`00044ec0`)**, (color, end, start), each point by value: `Raster_DrawLine`(start, end) under the pen {0, color} at the context's `+0x228`. ES2's (`00453798`).

`es2_xref.py` finds no reference to `CockpitResource_Load`, `Raster_FrameRectColour` or `Raster_FillFrameRectColours`, as for ES2's ([Open](#open)), and finds `Raster_DrawLineColour` called four times from `AlertPanel_SetTitleText`.

## The slider

**`AlertSliderGadget_Ctor` (`000469ec`)**, (slider, owner, rect), is ES2's (`004550b0`) with ES1's offsets: `0x7e` bytes when given none; the slider base `SliderWidget_CtorBase` (`000439d4`) with knob length 1, position 0, maximum `0x100` and minimum 0 ([the slider base](cockpit-mouse.md#the-slider-base)); the owner at `+0x3e` and the mixin's vptr at `+0x42`; a rect region at `+0x56`; the knob picture, frame 0 of `dba\alrtslid`, loaded once into `AlertSlider_KnobBitmap` (`00094fbc`) with `ClassItem_LoadFromFile` and kept at `+0x4e`; the knob 7 pixels long (`+0x2c`), its rect (*x0*, *y0* − 1)–(*x0* + 7, *y1* − 1) of the track's; a save buffer of the knob's rows × columns + `0x20` bytes at `+0x52`; an LED bar graph (`LedBarGraph_Ctor`, [bar graphs](cockpit-widgets.md#bar-graphs)) over the track less the knob's length, with its `+0x2c` 50, `+0x30` 49 and `+0x20` 63, at `+0x46`; `+0x4a` and `+0x7a` 0; the knob at the track's left edge; the maximum `0x400`; and the scale recomputed. ES2's doubles the knob length by the coordinate shift. `AlertSliderGadgetVtable` (`0009504a`) holds:

| Slot | ES1 |
|---|---|
| `+4` paint | `AlertSliderGadget_Paint` (`00046cc8`) |
| `+8` invalidate | `HSliderGadget_InvalidateDeferred` (`00046c78`): `+0x1b` = 2 |
| `+0xc` click | `AlertSliderGadget_OnClick` (`00046ca0`) |
| `+0x10` per frame | `AlertSliderGadget_FlushDeferredPaintNoOp` (`00046e65`), three `NOP`s and `RET 4` |
| `+0x14`–`+0x20` | `SliderWidget_GetValueH`, `SliderWidget_SetValueH`, `SliderWidget_DragToPointH`, `SliderWidget_RecomputeScaleH` |

Its mixin's table `00095032` holds `AlertSliderGadget_PaintThunk` (`00046c88`) and `AlertSliderGadget_FlushDeferredPaintNoOpThunk` (`00046c94`), each `ADD dword ptr [ESP+4], -0x3e` and a jump, and `Widget_NoClickSound`. The tables it passes through, `00094fc2` and `00095006`, hold `CTLSliderControl_PaintNoOpAlertCopy` (`00046c6f`) and `CTLSliderControl_InvalidateNoOpAlertCopy` (`00046c73`), a `NOP` and `RET 4` each, copies of the slider base table's `CTLSliderControl_PaintNoOp` (`00043a5e`) and `CTLSliderControl_InvalidateNoOp` (`00043a63`).

- **`AlertSliderGadget_OnClick` (`00046ca0`)**, (slider, value): the owner's slot `+0x10` with (owner, value, slider), then its own invalidate. ES2's (`0045586c`).
- **`AlertSliderGadget_Paint` (`00046cc8`)**, (slider): when the knob has moved since the last paint (`+0x2e` against the position `+0x28`), puts back the screen under the old knob (`Region_Restore` of the region at `+0x5a` at `+0x6e`); runs the bar graph's slots `+0xc` and `+4` with `+0x7a`, and copies `+0x7a` to `+0x4a`; moves the knob's rect to the position; sets the display's source page to its draw page; saves the screen under the new knob (`Region_Save`, the region's rect moved by the render context's `+0x210`/`+0x214` for the call); blits the knob there with flags 0; and keeps its corner at `+0x6e`.

`es2_xref.py` finds no call of `AlertSliderGadget_Ctor`, only the dword at `00077189` inside the library's code ([Open](#open)).

## Destroying a panel — `Alert_Dtor` (`00045d0c`)

(panel, flags), ES2's (`00454394`) without its two tests: it stores `AlertVtable`, frees each widget, the screen buffer `+0x28` when set and the title label `+0x40`, destroys `+0x2c` when set through its slot `+0x1c` with 3, clears `+0x2ce`, destroys the cursor record (`GLCursor_Dtor`, 2), the render context in place (this file's `GLViewport` and `GLBase` vtables, `00094e7e` and `00094dae`), the string table (`OwnedArray_Dtor`, 2) and the region in place. It tests neither the panel nor the flags and frees nothing of the panel itself. Each member's destructor calls it with 0 and then frees the panel when its own flags say so; `CockpitWidgets_HandleCommand` (`F11`, [keys](cockpit-panel.md#keys-and-the-view-device)), `Sim_DispatchCommand` (`Q`, `Ctrl+Q`, `Ctrl+Esc`, `P`, [commands](main-loop.md#commands--sim_dispatchcommand-00059df8)) and `Sim_MainTick` call it directly with 3 after running a panel, so the member's destructor does not run and the panel's memory stays allocated.

The region's and render context's copies, each (object, flags), storing its table and its bases' and freeing the object when bit 0 of the flags is set: `GLBase_DtorAlertCopy` (`00045c8c`, slot `+0x1c` of `00094dae`), `GLRegion_DtorAlertCopy` (`00045cb0`, of `00094dd6`), `GLRectangleRegion_DtorAlertCopy` (`00045cd8`, of `00094dfe`) and `GLViewport_DtorAlertCopy` (`00045de8`, of `00094e7e`).

## The status panel — `StatusAlertPanel_Ctor` (`00046efc`)

**The pause prompt (`P`), the quit prompt (`Ctrl+Q`, `Ctrl+Esc`), the answer to `Q` and every alert the mission raises are one panel: a title, up to four lines of text and one or two buttons, all picked by a status number from one string table.** It is ES2's status alert ([its text and layout](../../retail/simulation/alert-panels.md#the-status-alerts-text-and-layout), [what it decides](../../retail/simulation/mission-objectives.md#the-status-alert--gnl_alrt-00455934)) with ES2's pause panel ([ES2's](../../retail/simulation/alert-panels.md#the-pause-panel--004561c0)) folded in as statuses 0 and 1, and without ES2's status-5 substitution: the body is always the table's.

**`StatusAlertPanel_Ctor` (`00046efc`)**, (panel, rect, *keepsScreen*, status), allocates `0x548` bytes when given none. `es2_xref.py` finds six calls, five passing (0, 0, 1, status): `Sim_DispatchCommand`'s `P` (0), `Ctrl+Q` and `Ctrl+Esc` (1) and `Q` (`Mission_StatusForAlert`'s answer) ([commands](main-loop.md#commands--sim_dispatchcommand-00059df8)); `Sim_MainTick` with `Mission_PollStatus`'s ([the tick](main-loop.md#the-tick--sim_maintick-00059700)); and `Prefs_LimitDetailInLowMemory` with `0x11`. The sixth, `Sim_DeathFlash` (`00058414`), passes (0, 0, 0, 2), so its panel keeps no copy of the screen under it ([the death flash](main-loop.md#the-death-flash--sim_deathflash-00058414)) ([the detail setting](object-classes.md#the-detail-setting--shapedetail_applydetailsetting-00057c08)). In order:

1. The rect: 222 × 109, centered by `AlertPanel_CenterRect`(109, 222) at (48, 45), or with a rect given, `AlertPanel_SetRect` at its point ([placing a panel](#the-panel-object--alertpanel_ctorbase-00045988)); `AlertPanel_CtorBase`(panel, the rect, *keepsScreen*); `GeneralAlertVtable` (`0009507e`) at `+0x314`.
2. The text: `str\gnl_alrt` (`ResourcePath_BuildFolderName`, so `str\GNL_ALRT.STR`) into the base's string table `+0x30` with `StringTable_LoadFromFile`, then its three groups with `StringTable_LoadGroup`, each with no attribute destination (0): 19 titles at `+0x334`, 38 button captions at `+0x380` and 76 body lines at `+0x418`, which end at the object's last byte. The counts it passes, 19, 38 and 76, are capacities `StringTable_LoadGroup` does not read; the file's groups hold that many. Indexed by the status: the title `+0x334`[status] goes to `+0x318`, the captions from `+0x380`[2 × status] and the body from `+0x418`[4 × status] are pointed at by `+0x31c` and `+0x320`.
3. The buttons' rects, two built by `__vec_ctor` with `GauRect_ElementCtorStatusAlertPanelCopy` (`0004726c`, entered at its padding `00047269`), each (*x*, 70)–(*x* + 80, 90). A status of 0, 2, 3, 7, 8, `0x11` or `0x12` has one button, at *x* 71; 1, 4, 5, 6, 9, 10 and `0xb` to `0x10` have two, at *x* 22 and 120; any other has none (the jump table `000950c0`, behind an unsigned bound against `0x12`).
4. The plate: frame 0 of `dba\gnl2alrt` for statuses 0 and 1 and of `dba\gnl_alrt` for the rest, loaded with `ClassItem_LoadFromFile`, at `+0x2c`. Each array holds one 224 × 108 frame, whose last column falls past the panel's 223 and is cut ([drawing on a panel](#the-panel-object--alertpanel_ctorbase-00045988)).
5. Each button: `PanelButton_Ctor`(0, index, panel, rect) ([the panel's buttons](#the-panels-buttons)); its caption label, `Label_Ctor`(0, `WidgetFont_Cpylw`), at the button's `+0x2c`, centered on the rect by `Label_SetRect` with flags 2 and margin (0, −3) ([labels](cockpit-widgets.md#labels)); `Widget_RegisterClickable`(button, the panel's root `+0x281`); and the button added to the widget list `+0x28d`, whose count `+0x2b5` step 3 set.
6. The title: a label in `WidgetFont_Title` at `+0x40`, on the title bar `+0x2ba`, ((222 − *w* − 20) >> 1, 4)–(that + *w* + 20, 21), *w* the title's width (`HudFont_MeasureString`), with flags 2 and margin (0, −11). [The title bar](#the-title-bar--alertpanel_settitletext-000461ec) frames that rect.
7. The body: four labels in `WidgetFont_Green6x8` at `+0x324`, centered (flags 2, margin 0) on (50, 30 + 10*i*)–(172, 40 + 10*i*).
8. The cancel widget `+0x2e3`, which [Esc] presses, is widget 0; for status 2, `+0x313` is cleared, so leaving the panel keeps the alert cursor ([putting a panel up](#putting-a-panel-up-and-taking-it-down)).

Its table, `GeneralAlertVtable`, holds `StatusAlertPanel_Paint` (`000473c4`) at `+4`, `AlertPanel_SetTitleText` at `+8`, `StatusAlertPanel_RunModal` (`0004730c`, [the loop](#the-loop)) at `+0xc`, `StatusAlertPanel_OnChildClick` (`000474e4`) at `+0x10`, `AlertPanel_HandleEvent` at `+0x14` and `StatusAlertPanel_GetButtonCaption` (`0004755c`) at `+0x18`, the run, the click and the caption by their padding entries; `+0` and `+0x1c` are 0.

- **`StatusAlertPanel_Paint` (`000473c4`)**, (panel): blits the plate with flags 0 at the panel's corner (`Bitmap_Blit`(plate, 0, the region's rect)); sets the title through slot `+8`; counts the body's lines up to the first empty one, at most four, and puts line *i* on body label *i* + 1 when there is one line and on label *i* otherwise (`Label_SetText`), so a one-line body stands on the second row; then runs each widget's paint, slot `+4` of its `+0x1c` table. ES2's ([its paint](../../retail/simulation/alert-panels.md#the-status-alerts-paint--00456068)) also stops at a null line.
- **`StatusAlertPanel_OnChildClick` (`000474e4`)**, (panel, flags, child): finds the child by comparing it with each entry of the widget list and sets the close byte `+0x2b9` to 2 for widget 0 and 3 for widget 1, so the loop answers 0 for the left button and 1 for the right. A key reaches it through `AlertPanel_PressWidget` ([keys](#keys--alertpanel_handleevent-00046670)): [Return] and the trigger answer for the focused button, which the loop sets to the left one before its first pass and [Tab] moves, and [Esc] answers 0. ES2's (`00456160`) also resets the present timer.
- **`StatusAlertPanel_GetButtonCaption` (`0004755c`)**, (panel, index): `+0x31c`[index], the caption a button's paint asks its owner for.
- **`GeneralAlert_Dtor` (`000472b0`)**, (panel, flags): stores `GeneralAlertVtable`, frees the four body labels, runs `Alert_Dtor`(panel, 0) and frees the panel when bit 0 of the flags is set; ES2's (`00455f90`). `es2_xref.py` finds two calls, each with 3, from `00058414` and `Prefs_LimitDetailInLowMemory`; the other four callers destroy the panel with `Alert_Dtor` ([destroying a panel](#destroying-a-panel--alert_dtor-00045d0c)).

### What each status shows

The status's row of `str\GNL_ALRT.STR`, which has one row for each status from 0 to 18; the title is `MISSION ALERT` for every status but 0, 1 and 17. Where a status comes from `Mission_Status`, the cause is [the status](mission-status.md#the-status--mission_status-0000f4e8)'s; *clear* is [clear of threats](mission-status.md#clear-of-threats).

| Status | From | Buttons | Body |
|---|---|---|---|
| 0 | `P` | `CONTINUE` | none; the title is `PAUSE` |
| 1 | `Ctrl+Q`, `Ctrl+Esc` | `CONTINUE`, `QUIT` | none; the title is `QUIT TO DOS?` |
| 2 | the machine destroyed, after [the death flash](main-loop.md#the-death-flash--sim_deathflash-00058414) | `CONTINUE` | `YOU HAVE BEEN KILLED.`, and the Cybrids collect the remains |
| 3 | the machine immobilised | `CONTINUE` | the HERC disabled and friendly forces bringing the player back to base |
| 4 | not *clear*, in the group-word arm or with the selector's test unmet | `CONTINUE`, `ABORT MISSION` | the mission not complete and the player at risk |
| 5 | no writer found ([Open](#open)) | `CONTINUE`, `QUIT ANYWAY` | `YOUR MISSION IS NOT COMPLETE.` |
| 6 | *clear*, in the group-word arm (the squad's target badly damaged under a group word `+4` of 6) | `CONTINUE`, `RETURN TO BASE` | `YOU HAVE FAILED YOUR MISSION.` |
| 7 | 60000 past the mission box | `CONTINUE` | nearing the edge of the mission area, and the mission aborting past it |
| 8 | 170000 past it | `CONTINUE` | the mission area left, `MISSION ABORTED.` |
| 9 | the selector's test met, *clear* | `CONTINUE`, `RETURN TO BASE` | `MISSION OBJECTIVES COMPLETE.` |
| 10 | the selector's test met, not *clear* | `CONTINUE`, `ABORT MISSION` | still at risk if the player aborts in combat |
| 11 | selector 1 or 2 unmet, *clear* | `CONTINUE`, `QUIT ANYWAY` | not complete: not every waypoint reached |
| 12 | selector 0 unmet, *clear* | `CONTINUE`, `QUIT ANYWAY` | not complete: the base to detect not targeted |
| 13 | selector 4 unmet, *clear* | `CONTINUE`, `QUIT ANYWAY` | not complete: the player's base still at risk |
| 14 | selector 5 or 7 unmet, *clear* | `CONTINUE`, `QUIT ANYWAY` | not complete: the squad being protected still at risk |
| 15 | selector 3 unmet, *clear* | `CONTINUE`, `QUIT ANYWAY` | not complete: the target base's information not downloaded |
| 16 | selector 6, 8 or 9 unmet, *clear* | `CONTINUE`, `QUIT ANYWAY` | not complete: the mission target not found and destroyed |
| 17 | `Prefs_LimitDetailInLowMemory` | `CONTINUE` | `INSUFFICIENT MEMORY FOR MAXIMUM DETAIL.`; the title is `ALERT` |
| 18 | `Sim_MainTick`, for a status 3 whose [going out of the fight](mission-groups.md#going-out-of-the-fight) destroys the player | `CONTINUE` | the HERC disabled where the Cybrids hold the ground, and the player captured |

`Q` asks `Mission_Status` with its box tests skipped, so it never shows 7 or 8; the poll passes on only the statuses `Mission_StatusWorthRaising` marks, 2, 3, 6, 7, 8, 9 and 18 ([the poll](mission-status.md#the-poll--mission_pollstatus-0000f454)). So `Q` is what shows statuses 4, 10 and 11 to 16, where ES2's panel has rows 11 to 16 for objective conditions with no writer found ([its text](../../retail/simulation/alert-panels.md#its-text)). What a caller does with the answer is [the commands'](main-loop.md#commands--sim_dispatchcommand-00059df8) and [the tick's](main-loop.md#the-tick--sim_maintick-00059700).

### Against ES2's

| | ES1 | ES2's |
|---|---|---|
| `P`, `Ctrl+Q` | this panel, statuses 0 and 1, on the plate `dba\gnl2alrt` | [a pause panel](../../retail/simulation/alert-panels.md#the-pause-panel--004561c0) of its own |
| `Ctrl+Q`'s title | `QUIT TO DOS?` | `EXIT EARTHSIEGE?` |
| status 5's body | the table's | the outstanding objective's `mission.str` lines |
| status `0x13` | no buttons, and no row in the table | two buttons |
| size | 222 × 109 on the 320 × 200 screen, the plate 224 × 108 | 222 × 109 doubled to 444 × 218, the plate 444 × 214 |
| the buttons | 80 × 20 at *y* 70; one at *x* 71, two at 22 and 120 | 61 × 9 at *y* 80, in 320-wide coordinates; one at 80, two at 41 and 120 |
| the title | in a framed bar 17 pixels high at *y* 4, 20 pixels wider than the text | in a bar at *y* 0, 8 high, unframed |
| the body | the same rect: *x* 50 to 172, rows from *y* 30, 10 apart, in `green6x8` | the same |
| the layout's numbers | in the code | written once into a block, shifted for the video mode |

## The objectives panel — `ObjectivesPanel_Ctor` (`0004a1b4`)

**`F11` puts up the mission's objectives, as many as seven, each a line of text the shell wrote from the mission's `.ENG` file, under the title `OBJECTIVES`, with one button, `RETURN`.** It is ES2's objectives panel ([ES2's](../../retail/simulation/alert-panels.md#the-objectives-panel--obj_alrt-0045751c)) reading its lines from `data\object.str` where ES2's lists block 13's, with the lines left-aligned at one *x* where ES2's are centered, and smaller.

**`ObjectivesPanel_Ctor` (`0004a1b4`)**, (panel, rect, *keepsScreen*), allocates `0x39e` bytes when given none. `es2_xref.py` finds one call, from `F11`'s case in `CockpitWidgets_HandleCommand`, passing (0, 0, 1) ([keys](cockpit-panel.md#keys-and-the-view-device)). In order:

1. The rect: 315 × 109, centered by `AlertPanel_CenterRect`(109, 315) at (2, 45), or with a rect given, `AlertPanel_SetRect` at its point; `AlertPanel_CtorBase`(panel, the rect, *keepsScreen*); a second string table at `+0x318` (`StringTable_Ctor`); `ObjectiveAlertVtable` (`000955d6`) at `+0x314`.
2. The plate: frame 0 of `dba\hst_alrt`, loaded with `ClassItem_LoadFromFile`, at `+0x2c`. The array holds one 320 × 108 frame; the panel's rect covers 316 columns, edges included, so its last four are cut ([drawing on a panel](#the-panel-object--alertpanel_ctorbase-00045988)).
3. Its own text: `str\obj_alrt` into the base's table `+0x30`, its two groups of one string each to `+0x328`, the title `OBJECTIVES`, and `+0x32c`, the caption `RETURN`.
4. The objectives: `data\object.str`, with the data folder's prefix (`Path_ApplyAltDrive`), into the table at `+0x318`, its group 0 into the pointers from `+0x330`, and the group's count, which `StringTable_LoadGroup` returns, as an `int16` at `+0x380`. The pointers have room for 20, and `StringTable_LoadGroup` writes as many as the file's count; the retail `.ENG` files' `OBJ:` blocks hold 12 pieces at most. The file is a string table of one group ([layout](../../retail/formats/str-strings.md#layout)): what the shell writes ([the objectives](../formats/eng-mission-text.md#the-objectives)) is that layout, its byte after each text the count of the text's attributes, 0.
5. The title: a label in `WidgetFont_Green6x8` at `+0x40`, centered (flags 2, margin (0, −1)) on ((315 − *w*) >> 1, 3)–(that + *w*, 13), *w* the title's width. The title bar's rect `+0x2ba` is set to −20 in all four words, so the frame [the title bar](#the-title-bar--alertpanel_settitletext-000461ec) draws lies 20 pixels above and to the left of the panel's corner, outside its rect, and the clip draws none of it: its four lines are points beyond the rect's *x0*, and its corner pictures end left of it ([drawing on a panel](#the-panel-object--alertpanel_ctorbase-00045988)).
6. The button: `PanelButton_Ctor`(0, 0, panel, (127, 93)–(187, 103)), its caption label in `WidgetFont_Cpylw` centered with margin (0, −1), registered with the panel's root and made widget 0.
7. The lines: seven labels in `WidgetFont_Cpylw` at `+0x382`, on (20, 17 + 10*i*)–(200, 27 + 10*i*), placed with flags 1 and margin 0, which `Label_SetRect` and `Label_SetText` treat as left-aligned at *x* 20, since only flags 2 and 4 center or right-align ([labels](cockpit-widgets.md#labels)).
8. The cancel widget `+0x2e3` is widget 0, `RETURN`.

`ObjectiveAlertVtable` holds `ObjectivesPanel_Paint` (`0004a5a4`) at `+4`, `AlertPanel_SetTitleText` at `+8`, `ObjectivesPanel_RunModal` (`0004a4ec`, [the loop](#the-loop)) at `+0xc`, `ObjectivesPanel_OnChildClick` (`0004a6a0`) at `+0x10`, `AlertPanel_HandleEvent` at `+0x14` and `ObjectivesPanel_GetButtonCaption` (`0004a710`) at `+0x18`, the run, the click and the caption by their padding entries; `+0` and `+0x1c` are 0.

- **`ObjectivesPanel_Paint` (`0004a5a4`)**, (panel): blits the plate with flags 0 at the panel's corner; sets the title through slot `+8`; puts each of the file's lines that is not empty on the next line label (`Label_SetText`), stopping once seven are set, so an empty line leaves no gap and an eighth is not shown; then runs each widget's paint. ES2's ([its paint](../../retail/simulation/alert-panels.md#the-objectives-panels-paint--objectivespanel_paint-00457b58)) does the same with its own lines.
- **`ObjectivesPanel_OnChildClick` (`0004a6a0`)**, (panel, flags, child): sets the close byte to 1 for widget 0, so the loop answers 1. [Return], [Esc], the trigger and a click on `RETURN` all press widget 0. `AlertPanel_HandleEvent`'s cases ([keys](#keys--alertpanel_handleevent-00046670)) do not include `F11`'s `0x57`, so a second `F11` leaves the panel up. ES2's (`00457c30`) also resets the present timer.
- **`ObjectivesPanel_GetButtonCaption` (`0004a710`)**, (panel, index): `+0x32c`, `RETURN`, whatever the index.
- **`ObjectivesPanel_Dtor` (`0004a480`)**, (panel, flags): stores `ObjectiveAlertVtable`, frees the seven line labels, destroys the second string table (`OwnedArray_Dtor`, 2), runs `Alert_Dtor`(panel, 0) and frees the panel when bit 0 of the flags is set; ES2's (`00457a80`). `F11`'s case destroys the panel with `Alert_Dtor` ([destroying a panel](#destroying-a-panel--alert_dtor-00045d0c)), and `es2_xref.py` finds no branch or stored pointer to this destructor or its padding ([Open](#open)).

| | ES1 | ES2's |
|---|---|---|
| the lines | `data\object.str`, which the shell writes from the mission's `.ENG` file | block 13's `mission.str` lines |
| their place | left-aligned at *x* 20 | centered, and five pixels off the panel's center line |
| the title | in `green6x8`, with no frame | in `title` |
| the size | 315 × 109 at (2, 45), the plate `dba\hst_alrt`, 320 × 108 | 315 × 139 in 320-wide coordinates, the plate 630 × 230 at device scale |
| the button and the lines' rows | the button 60 × 10 at (127, 93); seven rows from *y* 17, 10 apart | the same, doubled |

Of the 72 retail `.ENG` files with an `OBJ:` block, `B0_C6_3A.ENG` holds 8 objectives and `B0_C7_2A.ENG` 10, so `F11` shows the first seven of each.

## The preferences and controls panels

`F12`'s and `Alt+P`'s preferences panel, `PreferencesPanel_Ctor` (`00047578`), and the controls panel its CONTROLS raises, `ControlsPanel_Ctor` (`00048408`), are members on this base, written up in [`preferences.md`](preferences.md).

## Functions

| ES1 | Address | ES2 |
|---|---|---|
| `CornerFrame_Draw`, `Rect_CopyCtor`, `CornerFrame_DrawEdgeRing`, `CornerFrame_DrawCorners` | `00044954`, `00044aac`, `00044adc`, `00044c64` | `00453240`, inlined, `004533a4`, `00453518` |
| `CockpitFont_Load`, `CockpitCorners_Load`, `CockpitResource_Load` | `00044d50`, `00044d88`, `00044dc0` | `004535ec`, `00453640`, `00453694` |
| `Raster_FrameRectColour`, `Raster_FillFrameRectColours`, `Raster_DrawLineColour` | `00044e00`, `00044e50`, `00044ec0` | `004536f4`, `00453738`, `00453798` |
| `AlertPanel_CtorBase`, `Alert_Dtor`, `AlertPanel_OnChildClickNoOp` | `00045988`, `00045d0c`, `00045d06` | `00454174`, `00454394`, `004558cb` |
| `GLBase_DtorAlertCopy`, `GLRegion_DtorAlertCopy`, `GLRectangleRegion_DtorAlertCopy`, `GLViewport_DtorAlertCopy` | `00045c8c`, `00045cb0`, `00045cd8`, `00045de8` | the library's |
| `AlertPanel_Enter`, `AlertPanel_Leave`, `AlertPanel_SetTitleText` | `00045f00`, `000460b8`, `000461ec` | `00454630`, `004548ac`, `00454b58` |
| `AlertPanel_LoadCursor`, `AlertPanel_RestoreCursorImage` | `000463c0`, `00046428` | `00454b70`, `00454c10` |
| `AlertPanel_SetFocus`, `AlertPanel_FocusChild`, `AlertPanel_FocusNext`, `AlertPanel_FocusPrevious` | `00046470`, `00046584`, `000465d8`, `00046600` | `00454c7c`, `00454d48`, `00454d7c`, `00454da0` |
| `AlertPanel_PressWidget`, `AlertPanel_HandleEvent`, `AlertPanel_SetRect`, `AlertPanel_CenterRect` | `0004662c`, `00046670`, `0004675c`, `0004679c` | `00454dcc`, `00454e10`, `00454ef8`, `00454f34` |
| `PanelButton_Ctor`, `PanelButton_Paint`, `Rect_CopyCtorAlertCopy`, `PanelButton_Tick`, `PanelButton_OnClick` | `000467c8`, `000468e0`, `0004695c`, `0004698c`, `000469ac` | `00454f64`, `00454ff8`, inlined, `00455064`, `00455080` |
| `CTLButtonControl_PaintNoOpAlertCopy`, `CTLControl_InvalidateNoOpAlertCopy`, `CTLControl_OnClickNoOpAlertCopy`, `CTLControl_FlushDeferredPaintNoOpAlertCopy` | `00046893`, `00046897`, `0004689b`, `0004689f` | [the gadget bases](cockpit-widgets.md#the-gadget-bases)' copies |
| `Widget_GetButtonValueAlertCopy`, `CTLControl_SetValueNoOpAlertCopy`, `CTLControl_DragMoveNoOpAlertCopy`, `Widget_InvalidateDeferredAlertCopy` | `000468a3`, `000468ad`, `000468b3`, `000468b8` | the same |
| `AlertSelectGadget_PaintThunk`, `AlertSelectGadget_TickThunk` | `000468c8`, `000468d4` | `004558c1`, `004558b7` |
| `AlertSliderGadget_Ctor`, `AlertSliderGadget_Paint`, `AlertSliderGadget_OnClick`, `HSliderGadget_InvalidateDeferred` | `000469ec`, `00046cc8`, `00046ca0`, `00046c78` | `004550b0`, `00455348`, `0045586c`, `004556d0` |
| `CTLSliderControl_PaintNoOpAlertCopy`, `CTLSliderControl_InvalidateNoOpAlertCopy`, `AlertSliderGadget_FlushDeferredPaintNoOp` | `00046c6f`, `00046c73`, `00046e65` | `0045571c`, `00455721`, `004554a0` |
| `AlertSliderGadget_PaintThunk`, `AlertSliderGadget_FlushDeferredPaintNoOpThunk` | `00046c88`, `00046c94` | `004558a3`, `00455899` |
| `Alert_StaticInit` | `00046e6c` | `004554a8` |
| `StatusAlertPanel_Ctor`, `GauRect_ElementCtorStatusAlertPanelCopy`, `GeneralAlert_Dtor`, `StatusAlertPanel_RunModal` | `00046efc`, `0004726c`, `000472b0`, `0004730c` | `00455934`, `0048dd23`, `00455f90`, `00455fe4` |
| `StatusAlertPanel_Paint`, `StatusAlertPanel_OnChildClick`, `StatusAlertPanel_GetButtonCaption` | `000473c4`, `000474e4`, `0004755c` | `00456068`, `00456160`, `004561ac` |
| `HeapPtr_DtorObjectivesPanelCopy`, `ObjectivesPanel_Ctor`, `ObjectivesPanel_Dtor`, `ObjectivesPanel_RunModal` | `0004a18c`, `0004a1b4`, `0004a480`, `0004a4ec` | [module copies](../formats/compiler.md#vtables), `0045751c`, `00457a80`, `00457ae4` |
| `ObjectivesPanel_Paint`, `ObjectivesPanel_OnChildClick`, `ObjectivesPanel_GetButtonCaption` | `0004a5a4`, `0004a6a0`, `0004a710` | `00457b58`, `00457c30`, `00457c6c` |

`AlertPanel_OnChildClickNoOp`, the seven stubs from `00046893` to `000468b3`, the four thunks, `CTLSliderControl_PaintNoOpAlertCopy`, `CTLSliderControl_InvalidateNoOpAlertCopy` and `AlertSliderGadget_FlushDeferredPaintNoOp` have no Ghidra function.

## Globals

| | Address |
|---|---|
| `AlertPanel_OpenCount`, `int16` | `00094d78` |
| `AlertPanel_CursorImage`, `dci\alert` | `00094d7c` |
| `AlertPanel_EnterHook`, `AlertPanel_LeaveHook` | `00094d80`, `00094d84` |
| `AlertVtable` | `00094e26` |
| `AlertSelectGadgetVtable`, `AlertSelectGadget_PanelGadgetVtable` | `00094f96`, `00094f7e` |
| `AlertSlider_KnobBitmap`, `dba\alrtslid`'s frame 0 | `00094fbc` |
| `AlertSliderGadgetVtable`, `AlertSliderGadget_PanelSliderGadgetVtable` | `0009504a`, `00095032` |
| `GeneralAlertVtable`, the status panel's | `0009507e` |
| `ObjectiveAlertVtable`, the objectives panel's | `000955d6` |
| `AlertPanel_ScreenRect`, four `int32` | `000b1708` |
| `Alert_FillBrush` | `000b1718` |
| `AlertPanel_RectScratch`, four `int32`, after its first-use byte `000b1720` | `000b1724` |
| `WidgetCorners`, `dba\corners` | `000bd53e` |
| `WidgetFont_Title`, `WidgetFont_Cpylw`, `WidgetFont_Active`, `WidgetFont_Pushed`, `WidgetFont_Inactive`, `WidgetFont_Green6x8`, `WidgetFont_Red6x8` | `000bd542` to `000bd55a`, 4 bytes apart |

## Open

- **Open:** which member builds a slider, `es2_xref.py` finding no call of `AlertSliderGadget_Ctor`: the preferences and controls panels build none, and the calibration panel, `00049154`, is not read ([`preferences.md`'s Open](preferences.md#open)).
- **Open:** what passes status 5 to the status panel: of the six calls `es2_xref.py` finds, four pass constants, and `Mission_Status`, whose answers the other two pass, has no arm that answers 5 ([the status](mission-status.md#the-status--mission_status-0000f4e8)).
- **Open:** what the objectives panel shows when `data\object.str` is missing, which `StringTable_LoadFromFile` would be asked to open; the shell leaves the file as it was for a mission whose `.ENG` file has no `OBJ:` block ([the objectives](../formats/eng-mission-text.md#the-objectives)).
- **Open:** what reaches `ObjectivesPanel_Dtor`, to which `es2_xref.py` finds no branch or stored pointer, nor to its padding `0004a47d`.
- **Open:** whether a key's press of a panel button shows it pressed in retail. `AlertPanel_PressWidget` does not change the state. A retail check: on the pause panel (`P`), [Return] closes it with no pressed look on CONTINUE, where a held mouse press shows it.
- **Open:** a press's move of the focus. A retail check: on the preferences panel (`F12`, [the panel](preferences.md#the-preferences-panel--preferencespanel_ctor-00047578)), a click near the edge of MUSIC puts the pointer on its center.
- **Open:** [Alt+Tab] and keypad 0 in a panel. A retail check: on the `Ctrl+Q` panel, [Alt+Tab] from CONTINUE moves the pointer to QUIT's center, and keypad 0 does so only with preferences option 10 on.
- **Open:** the slider's `+0x7a`, which its paint hands the bar graph.
- **Open:** the object file `000451e0`–`00045987`, which `es2_naming.py match` pairs with ES2's screenshot code (`Screenshot_BuildPath`, `Screenshot_Save`).
- **Open:** what reaches `CockpitResource_Load`, `Raster_FrameRectColour` and `Raster_FillFrameRectColours`, to which `es2_xref.py` finds no reference.
- **Open:** a positive control in the running game ([when a module is done](../../herculan/plan-es1-support.md#when-a-module-is-done)). Proposed: in DOSBox-X's debugger, `BP <CS>:00045f00` fires as `P` raises the pause panel and `BP <CS>:000460b8` as it closes, and the word at `00094d78` is 1 between them. For the members: `F11` in `B0_C7_2A` lists seven of its ten objectives, left-aligned, and `P` puts up a panel the size of `Q`'s.
