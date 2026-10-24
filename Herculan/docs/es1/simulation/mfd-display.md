# DBSIM.EXE — the MFD display

**ES1's MFD is ES2's multi-function display ([the MFD](../../retail/simulation/mfd.md)): a screen with a title over it, six F-key buttons that switch it between its six pictures, and the buttons each picture uses, and it switches itself to the missile camera while a launcher is armed whose missiles have the projectile type and subtype of ES2's electro-optical missile.** What a player would see differs: every button sits where the chassis' layout file puts it; the F-key buttons have no lit state, so only the title says which screen is up; a button the screen does not use stays drawn, dimmed, rather than vanishing; clicking the title steps to the next screen; there is no SELECT button, and F1 always shows the player's own HERC; the scanner's MODE button switches it between active and passive, and its ranges are 30000, 80000 and 120000 world units; the status screens and the NAV MAP are redrawn twice a second; a display whose feed is damaged shows `OFFLINE`; and the display starts on the STATUS screen, where ES2's starts on the SCANNER. Addresses are `ES1DBSIM` image addresses ([the X-32 executable](../formats/x32-executable.md#the-image)); ES2's are DBSIM's. Every function here removes its own arguments, so argument lists are in stack order ([calling conventions](../formats/compiler.md#calling-conventions)).

The five screens have docs of their own: [the status screen](mfd-status-screen.md) (modes 0 and 4), [FLASH COMM](mfd-flash-comm.md) (mode 1), [the NAV MAP](mfd-nav-map.md) (mode 2), [the scanner](mfd-scanner.md) (mode 3) and [the missile camera](mfd-missile-camera.md) (mode 5).

## Where the object file starts and ends

The code is `00039bb4`–`0003b0ab` and the initialized data `00093ef6`–`00094205`, between [the energy meter's](cockpit-energy-shields.md#where-the-object-files-start-and-end) and [the throttle gauge's](cockpit-throttle-gauge.md#where-the-object-file-starts-and-ends). Both open with the copy of `ConfigPart`'s destructors and tables that starts each object file which carries one ([module copies](../formats/compiler.md#vtables)): `HeapPtr_DtorMfdDisplayCopy` (`00039bb4`), `ConfigPart_DtorMfdDisplayCopy` (`00039bdc`), `ConfigPart_GetClassTagMfdDisplayCopy` (`00039c1b`, frameless, with no Ghidra function) and `TSPartBase_DtorMfdDisplayCopy` (`00039c24`, by its padding `00039c21`), each the code of `panel.cpp`'s ([where `panel.cpp` starts](cockpit-panel.md#where-the-module-starts-and-ends)), and the tables `00093ef6` and `00093f2a`. `es2_xref.py` finds no reference to `00039bb4` ([Open](#open)). The energy meter's `RET 0xc` at `00039bb0` is followed by one zero byte; the last function, `MFDListGadget_OnClick`, ends with a `RET 8` at `0003b0a7`, followed by two zero bytes and the throttle gauge file's lone copy of the 40-byte destructor at `0003b0ac` ([where object files meet](../formats/compiler.md#where-object-files-meet-in-the-code)). The file has no static initializer, and no assert in it names a source path.

The code holds, after the run, the display's constructor, its repaint and update, the screens' base constructor `MFDisplay_Ctor` ([the status screen](mfd-status-screen.md#the-screen-object)), the buttons' click and key handlers, the mode, range and row helpers, the order transmit ([FLASH COMM](mfd-flash-comm.md#the-order-helpers)), and the two gadget classes. The data holds, after the run's tables, the missile camera switch's globals and the `OFFLINE` label's pointer (`00093f5a`–`00093f63`), `MfdButtonVisibility` (`00093f64`), `MfdDisplayVtable` (`00093fb6`), the jump tables of the constructor's switch (`00093fd0`), `MfdButton_OnClick` (`00094020`), `MfdDisplay_KeyDispatch` (`00094054`, `00094084`) and `MfdFlashComm_HandleAltKey` (`000940bc`), two empty strings (`00094004`, `00094005`), and the gadget classes' tables from `000940f6`.

## The display object

**`MfdDisplay_Ctor` (`00039c64`)**, (display, cockpit, block), allocates `0xcd` bytes when given none and runs `PanelGauge_Ctor` ([the gauge base](cockpit-widgets.md#the-gauge-base)), which copies the block's first 16 bytes as the gauge's rect. `Gau_MfdPanelWidget` (`0002cdac`) builds it with (0, the cockpit, `.GAU` block 728) and then calls `MfdDisplay_SetMode`(display, 0) ([building the cockpit](cockpit-panel.md#building-it)), so the display starts on the STATUS screen; ES2's starts on the SCANNER ([modes](../../retail/simulation/mfd.md#modes)).

| Offset | Holds | ES2's |
|---|---|---|
| `+0x00` | the gauge's rect, the block's first 16 bytes | `+0x04` |
| `+0x10` | the cockpit | `+0x14` |
| `+0x14` | the 13 buttons, `+0x14` + 4*i* | `+0x18` |
| `+0x64` | the button count, 13 | — |
| `+0x68`–`+0x75` | the sensor dropout's condition, burst and dark state, and the repaint byte `+0x75` ([sensor dropout](cockpit-widgets.md#sensor-dropout)) | — |
| `+0x76` | the vptr, `MfdDisplayVtable` | `+0x00` |
| `+0x7a` | the block the screens share, four dwords: the FLASH COMM row, a dword whose reader is [Open](#open), the status screens' subject and the scanner's range | `+0xb1` |
| `+0x8a` | the mode, −1 before the first `MfdDisplay_SetMode` | `+0xc1` |
| `+0x8e` | the scanner range's index, 2 at first | `+0xc5` |
| `+0x96` | the six screens, `+0x96` + 4 × mode | `+0xcd` |
| `+0xae` | six bytes, one a mode: while the current mode's is set, the display runs that screen's update ([drawing it](#drawing-it)) | `+0xe5` |
| `+0xb4` | the screen's rect | `+0xeb` |
| `+0xc4` | the screen's click surface, an `MFDListGadget` | `+0xfb` |
| `+0xc8` | a byte the constructor clears ([Open](#open)) | — |
| `+0xc9` | the clock count past which modes 0, 2 and 4 next refresh | `+0x100` |

`MfdDisplayVtable` (`00093fb6`) holds, from `+4`, each by its padding entry: `MfdButton_OnClick` (`0003a469`), the child-click slot; `MfdDisplay_Repaint` (`0003a1ed`), the paint slot `CockpitView_InvalidateWidgets` calls; `MfdDisplay_Update` (`0003a2a5`), the per-frame slot `Cockpit_DrawFrame` calls; and `MfdDisplay_KeyDispatch` (`0003a5e7`), the key slot ([drawing the cockpit](cockpit-panel.md#drawing-it)).

## Building it

With (*x*, *y*) the block's first two `int32` and the root widget the dword at `CockpitViewInstance`'s `+0`, the constructor:

1. stores `MfdDisplayVtable` at `+0x76`;
2. builds the 13 buttons, *i* from 0 to 12, each with `MFDSelectGadget_Ctor`(0, *i*, display, the block's rect at `+0x10` + 16*i* moved by (*x*, *y*)) and its pictures as the table below gives, stores it at `+0x14` + 4*i* and registers it with `Widget_RegisterClickable` on the root widget; buttons 0 to 5 and 7 to 10 each get a caption label, a `Label_Ctor` in `ColorSchemePanels`' `cpred`, centered by `Label_SetRect`(label, 2, the button's rect, margin (0, 0)) with fill color 0, stored at the button's `+0x2c`, and buttons 6, 11 and 12 none (the jump table `00093fd0`);
3. builds the title, a `Label_Ctor` in `cpred` centered over button 6's rect with zero margins and the fill color `0x3f`, as button 6's caption label, and stores 13 at `+0x64`;
4. sets the screen's rect `+0xb4` to the block's rect at `+0xe0` (offsets 952 to 967 of the file) moved by (*x*, *y*), builds the click surface over it with `MFDListGadget_Ctor`(0, display, rect), stores it at `+0xc4` and registers it;
5. stores the scanner's ranges 30000, 80000 and 120000 in `MfdScannerRanges` (`000bd4f4`), sets the mode `+0x8a` to −1 and the index `+0x8e` to 2, clears the shared block (`memset`(`+0x7a`, 0, 16)) and then stores, in this order, 0 at the subject `+0x82`, `MfdScannerRanges`[2], 120000, at `+0x86`, and 0 at the FLASH COMM row `+0x7a`;
6. clears the six screen pointers and builds the screens in mode order with (0, `+0xb4`), FLASH COMM's with (0, display, `+0xb4`): `MfdStatusScreen_Ctor`, `MfdFlashCommScreen_Ctor`, `MfdMapScreen_Ctor`, `MfdRadarScreen_Ctor`, `MfdStatusScreen_Ctor` again and `MfdMissileViewScreen_Ctor`, pointing each one's `+4` at the shared block `+0x7a`;
7. sets `+0xc9` to the clock (`000981c4`) + 30;
8. the first time, while `MfdDisplay_OfflineLabel` (`00093f60`) is 0, builds there a `Label_Ctor` in `cpred` centered over the screen's rect with zero margins and the fill color `0x3f` ([the dropout](#the-dropout));
9. clears the byte `+0xc8`.

The scanner's ranges are ES2's 50000, 100000 and 200000 ([modes](../../retail/simulation/mfd.md#modes)) cut to 30000, 80000 and 120000; `TargetSelect_CanTarget` reads the third directly ([the target selection](cockpit-panel.md#the-target-selection)). ES2's display builds its buttons from fixed tables relative to the screen, of two classes, and its title, message label and background in fixed places ([geometry](../../retail/simulation/mfd.md#geometry)); ES1's places each from the layout file and draws no background frame.

### The buttons

| *i* | Caption | Pictures in `dba\pbuttons`, by state 0, 1, 2 | Label | What a click does ([below](#clicks-and-keys)) |
|---|---|---|---|---|
| 0–5 | none | 10 + 2*i*, 11 + 2*i* | built, never written | `MfdDisplay_SetMode`(display, *i*) |
| 6 | the title, the screen's name | none | the title | `MfdDisplay_CycleMode` |
| 7 | `MODE` | 3, 4, 5 | the caption | in mode 3, `Mech_ToggleRadarMode` |
| 8 | `RANGE` | 3, 4, 5 | the caption | `MfdDisplay_CycleScannerRange` |
| 9 | `TARGET` | 3, 4, 5 | the caption | `Cockpit_CycleTarget` |
| 10 | `XMIT` | 3, 4, 5 | the caption | in mode 1, the order transmit |
| 11 | none | 29, 30, 31 | none | in mode 1, the previous available row |
| 12 | none | 32, 33, 34 | none | in mode 1, the next available row |

The captions are `STRINGS0.STR` group 5 ([the groups](../formats/str-strings.md#the-groups)): entries 0 to 5, `STATUS`, `FLASH COMM`, `NAV MAP`, `SCANNER`, `TARGET` and `MISSILE CAM`, are the title of each mode, and entries 7 to 10 the captions of buttons 7 to 10. Buttons 0 to 5 get two pictures and no text ([Open](#open)). The bank is `CockpitPButtonsBank` (`00092d68`, [building the cockpit](cockpit-panel.md#building-it)), whose frames the constructor reads from its array at `+8`. ES2's F column is lit for the current mode and its SELECT, RANGE, TARGET and XMIT light while held, from the `MFD` bank, with the captions `F1`–`F6` composed and the title a separate label ([two button classes](../../retail/simulation/mfd.md#two-button-classes)).

**`MFDSelectGadget_Ctor` (`0003ae50`)**, (gadget, index, display, rect), allocates `0x3c` bytes when given none, returning 0 when that fails, runs `Widget_CtorRect` on the rect and stores the file's copy of the button base table (`CTLButtonControlVtableMfdDisplayCopy`, `000940f6`) and 0 at `+0x19`; then the mixin's table `PanelGadgetVtableMfdDisplayCopy` (`0009411e`) at `+0x24`, the display at `+0x20`, `PanelSelectGadgetVtableMfdDisplayCopy` (`00094136`) at `+0x1c`, the index at `+0x28` and 0 at the state `+0x17`; and last `MFDSelectGadget_PanelGadgetVtable` (`0009415e`) at `+0x24` and `MFDSelectGadgetVtable` (`00094176`) at `+0x1c`. ES2's `MFDSelectGadget_Ctor` (`004472e4`) stores the same three levels with `PanelSelectGadget`'s own mixin table between them. A button's fields are the widget's ([widget fields](cockpit-mouse.md#hit-testing)), then the display at `+0x20`, the mixin's vptr at `+0x24`, the index at `+0x28`, the caption label at `+0x2c` and the pictures at `+0x30`, `+0x34` and `+0x38`.

| Slot | `MFDSelectGadgetVtable` | `MFDSelectGadget_PanelGadgetVtable` |
|---|---|---|
| `+4` | `MFDSelectGadget_Paint` (by its padding `0003af22`) | `MFDSelectGadget_PaintThunk` (`0003af0c`) |
| `+8` | `Widget_InvalidateDeferredMfdDisplayCopy` (`0003aefc`, by its padding `0003aefb`): the paint counter `+0x1b` = 2 | `MFDSelectGadget_FlushDeferredPaintThunk` (`0003af18`) |
| `+0xc` | `Widget_ForwardClickToOwner` (`000314ac`, [the gadget bases](cockpit-widgets.md#the-gadget-bases)) | `Widget_ClickSound` (`000314a0`) |
| `+0x10` | `ShieldFacing_FlushDeferredPaintMfdDisplayCopy` (`0003afa8`, by its padding `0003afa7`) | |
| `+0x14`–`+0x1c` | the file's copies `Widget_GetButtonValueMfdDisplayCopy` (`0003aee7`), `CTLControl_SetValueNoOpMfdDisplayCopy` (`0003aef1`) and `CTLControl_DragMoveNoOpMfdDisplayCopy` (`0003aef7`) | |

The two thunks add −`0x20` to the object and jump to the paint and to the flush by their padding. `CTLButtonControlVtableMfdDisplayCopy` holds the file's copies of the button base's seven slots, `0003aed8`, `0003aedb`, `0003aedf`, `0003aee3`, `0003aee7`, `0003aef1` and `0003aef7` ([`panel.cpp`'s copies](cockpit-panel.md#functions)); `PanelSelectGadgetVtableMfdDisplayCopy` holds `0003aed8`, `0003aefc`, `Widget_ForwardClickToOwner`, `0003aee3` and the last three.

**`MFDSelectGadget_Paint` (`0003af24`)**, (button): for button 6, `Label_SetText`(the caption label, group 5's entry for the display's mode `+0x8a`); for any other, `Bitmap_Blit`(the picture `+0x30` + 4 × the state `+0x17`, 0, the button's rect) at its top-left, and then for buttons 7 to 10 sets the caption label's font to `ColorSchemePanels`[4 + state], `cpon`, `cppress` or `cpoff`, and writes group 5's entry for the index. The state is the widget's: 0 up, 1 pressed while the pointer holds it ([press, release and drag](cockpit-mouse.md#press-release-and-drag)), 2 put by `MfdDisplay_SetMode` on a button the mode does not use. A button in state 2 is drawn with its third picture and its caption in `cpoff`; `Widget_HitTestChildren` passes it over, and a key press skips it. The paint makes no visibility test.

**`ShieldFacing_FlushDeferredPaintMfdDisplayCopy` (`0003afa8`)**, (button), is the code of [`ShieldFacing_FlushDeferredPaint`](cockpit-energy-shields.md#the-facings--shieldsgauge_facingctor-000396ac): with the paint counter at 2 it calls the paint and takes 1 off; at 1 it copies the button's rect to the screen with `Widget_DrawToCockpit`(0, button, 0) and takes 1 off.

### The click surface

**`MFDListGadget_Ctor` (`0003afe0`)**, (gadget, display, rect), allocates `0x28` bytes when given none, returning 0 when that fails, runs `Widget_CtorRect` on the rect with `CTLButtonControlVtableMfdDisplayCopy` and 0 at `+0x19`, stores `PanelGadgetVtableMfdDisplayCopy` at `+0x24` and the display at `+0x20`, then `MFDListGadget_PanelGadgetVtable` (`0009419e`) at `+0x24`, `PanelListGadgetVtableMfdDisplayCopy` (`000941b6`) at `+0x1c`, 0 at the state `+0x17` and `MFDListGadgetVtable` (`000941de`) at `+0x1c`. ES2's display has the same surface over its screen ([the order list's mouse path](../../retail/simulation/mfd.md#keyboard)).

`MFDListGadgetVtable` holds, from `+4`, `MFDListGadget_PaintNoOp` (`0003b07a`, two `NOP`s and `RET 4`), `MFDListGadget_InvalidateNoOp` (`0003b07f`, a `NOP` and `RET 4`), `MFDListGadget_OnClick` (by its padding `0003b087`), `MFDListGadget_FlushDeferredPaintNoOp` (`0003b083`, a `NOP` and `RET 4`) and the copies `0003aee8`, `0003aef4` and `0003aef8`; `PanelListGadgetVtableMfdDisplayCopy` the same with 0 at `+0xc`. The mixin's table holds `MFDListGadget_PaintNoOpThunk` (`0003b064`) and `MFDListGadget_FlushDeferredPaintNoOpThunk` (`0003b070`), each adding −`0x20` to the object and jumping to `0003b07a` or `0003b083`, and `Widget_ClickSound`. The stubs and thunks have no Ghidra function.

**`MFDListGadget_OnClick` (`0003b088`)**, (gadget, flags): with bit 0 of flags set, `MfdFlashComm_HandleListClick`(the display `+0x20`, `CockpitMouse_CanvasPoint` (`000b16d4`), gadget) ([picking a row](mfd-flash-comm.md#picking-a-row)). It plays no sound, as ES2's `MFDListGadget_OnClick` (`00447630`) plays none. The buttons are registered before it, so a button over the screen's rect takes the point ([hit testing](cockpit-mouse.md#hit-testing)).

## Modes

**`MfdDisplay_SetMode` (`0003a9ec`)**, (display, mode), does nothing when the mode is already `+0x8a`. Otherwise it stores it, and when `g_MfdMissileCamMode` (`00093f5c`) is not −1 clears `g_MfdMissileCamArmed` (`00093f5e`) and sets `g_MfdMissileCamMode` to −1. For buttons 6 to 12 it reads the byte `MfdButtonVisibility` + 13 × mode + *i*: set with the button in state 2, it puts the button in state 0; clear with the button in state 0, in state 2; either change calls the button's invalidate, slot `+8` of its table at `+0x1c`. It sets the repaint byte `+0x75` to 2 and button 6's paint counter `+0x1b` to 2, and for mode 0 stores the cockpit's machine (`CockpitViewInstance`'s `+0x1ff`) and for mode 4 the cockpit's target selection (`+0x20c`) as the subject `+0x82`. ES2's (`00446e38`) also lights the new mode's button and puts out the old one's, takes mode 0's subject from its roster, and in mode 3 presses PASS or ACTIVE to match the machine's radar mode ([button visibility](../../retail/simulation/mfd.md#button-visibility)).

`MfdButtonVisibility` (`00093f64`) holds six rows of 13 bytes, a row a mode; buttons 0 to 6 are set in every row:

| Mode | Buttons in use besides 0–6 |
|---|---|
| 0 STATUS, 2 NAV MAP, 4 TARGET, 5 MISSILE CAM | 9 `TARGET` |
| 1 FLASH COMM | 9 `TARGET`, 10 `XMIT`, 11 and 12 |
| 3 SCANNER | 7 `MODE`, 8 `RANGE`, 9 `TARGET` |

**`MfdDisplay_CycleMode` (`0003a9c4`)**, (display), is ES2's (`00446e14`): `MfdDisplay_SetMode`(display, mode + 1, 6 wrapping to 0). `es2_xref.py` finds one call, from `MfdButton_OnClick`'s case 6; ES2's player input calls it too ([Open](#open)).

**`MfdDisplay_CycleScannerRange` (`0003ab1c`)** and **`MfdDisplay_SetScannerRange` (`0003ab44`)**, (display) and (display, index), are ES2's (`00446fc8`, `00446fec`): the cycle passes `+0x8e` + 1, 3 wrapping to 0, to the set, which, when the index differs from `+0x8e`, stores it there and `MfdScannerRanges`[index] at `+0x86`. From the starting index 2 the first step goes to 30000.

**`MfdDisplay_InScannerRange` (`0003ae00`)**, (display, object): whether `Math_DistanceBetweenPoints` from the object's `+0x20` to the cockpit's machine's `+0x20` is less than the range `+0x86`, ES2's (`0044725c`) on ES1's offsets. `es2_xref.py` finds one call, from `00040254` ([Open](#open)).

### The subject

**F1 shows the player's own HERC and F5 the current target.** The status screens read the subject at `+0x82` afresh at each paint ([the status screen's paint](mfd-status-screen.md#the-paint)). `MfdDisplay_SetMode` stores it as above, and `MfdDisplay_Update` stores it again at each of the half-second refreshes of modes 0 and 4 ([drawing it](#drawing-it)): the cockpit's machine for mode 0, the target selection for mode 4. ES2's mode 0 steps a roster of the player and the squadmates with SELECT ([the subject](../../retail/simulation/mfd.md#the-subject)); ES1's display has no roster and no SELECT.

### The missile camera switch

**`MfdDisplay_SyncMissileCamMode` (`0003ad08`)**, (display), is ES2's (`00447164`, [modes](../../retail/simulation/mfd.md#modes)) on ES1's offsets: the condition is that `CockpitView_FindArmedMountGauge`(`CockpitViewInstance`), which walks the first `+0x94` of the cockpit's weapon-row slots ([the helpers](cockpit-panel.md#squadmate-and-pilot-helpers)), returns a gauge whose mount's projectile record has type 0 (`WeaponGauge_MountProjectileType`) and subtype 3 (`WeaponGauge_MountProjectileSubtype`) ([the projectile getters](cockpit-weapon-gauges.md#the-row-gauge-base)). With the condition and `g_MfdMissileCamMode` −1: when `g_MfdMissileCamArmed` is clear it sets `g_MfdMissileCamMode` to 5 and the flag; when set it saves the mode in `g_MfdMissileCamSavedMode` (`00093f5a`, `int16`), selects mode 5 and sets `g_MfdMissileCamMode` to 5. Without the condition and with `g_MfdMissileCamMode` not −1, it selects the saved mode and sets the flag. In the image `g_MfdMissileCamMode` is −1 and the flag 1, so the first time the condition holds the display switches to mode 5, and a mode chosen by hand while it holds stays until it ends, when the saved mode comes back. `MfdDisplay_Update` runs it each frame the display's screen is on screen. Which ES1 launchers fire such a missile is [Open](#open).

## Drawing it

**`MfdDisplay_Repaint` (`0003a1f0`)**, (display), the paint slot, runs only while `CockpitView_RectCornerVisible` passes the screen's rect: it calls the paint, slot `+4`, of each of the `+0x64` buttons; installs at the render context's (`000bdbda`) `+0x22c` a brush on its own stack whose mode is 0 and whose color it never writes; and then, with the display not dark (`+0x74` clear), calls the current screen's paint, slot `+4` of the table at its `+8`, and when dark writes into `MfdDisplay_OfflineLabel` `OFFLINE` (`STRINGS0.STR` group 30) for a condition `+0x68` of 2 or more and an empty string below that. ES2's (`00446138`) paints the mode buttons, the background frame, the visible buttons, the screen or the power-up's frame, and then the title ([paint order](../../retail/simulation/mfd.md#paint-order)).

**`MfdDisplay_Update` (`0003a2a8`)**, (display), the per-frame slot, returns while the view manager's current view (`CockpitViewManager_Published`'s `+0x10`) is 4, the external view. Otherwise:

1. it stores `Player_DependentCondition`(4) at `+0x68` and, when that is non-zero, runs `PanelGauge_TickDropout` ([sensor dropout](cockpit-widgets.md#sensor-dropout));
2. the rest runs only while `CockpitView_RectCornerVisible` passes the screen's rect: it calls each button's flush, slot `+0x10`, and `MfdDisplay_SyncMissileCamMode`;
3. with the repaint byte `+0x75` at 2, it paints what `MfdDisplay_Repaint` paints after the buttons, writing `OFFLINE` or the other empty string (`00094005`) when dark, and takes 1 off; at 1, it copies the screen's rect to the screen with `Widget_DrawToCockpit`(0, `+0xb4`, 0), takes 1 off and sets the mode's byte `+0xae` + mode;
4. with the mode's byte set, by mode: for 0, 2 and 4, once the clock is past `+0xc9`, it sets `+0xc9` to the clock + 30, stores the subject for mode 0 or 4 as `MfdDisplay_SetMode` does, and calls the screen's update, slot `+8`, and otherwise calls nothing; for 3 and 5 it calls the update; for 1 it clears the byte and calls the update.

`MfdDisplay_SetMode` and each flip of the dropout set `+0x75` to 2, so a new screen is painted on one frame and copied to the screen on the next. The mode's byte stays set for modes 0, 2 to 5, and FLASH COMM's is set again by its row and button handlers ([picking a row](mfd-flash-comm.md#picking-a-row)) and by `MfdButton_OnClick`'s cases 8 to 12. So, with the clock at 60 counts a second ([the tick length](fixed-point-math.md#the-tick-length--time_beginsimtick-00063f40)):

| Mode | The screen's update runs |
|---|---|
| 0, 4 status | every 30 counts, half a second, the subject read again each time |
| 1 FLASH COMM | once each time its byte is set |
| 2 NAV MAP | every 30 counts; its paint slot is empty, so the map first shows at the first refresh after the switch |
| 3 SCANNER, 5 MISSILE CAM | every frame |

Modes 0, 2 and 4 share the one deadline `+0xc9`. Each update is called between `Cockpit_DrawFrame`'s canvas push and pop, so the installed render context is the canvas context, whose translation flag `+0x20c` is set ([the canvas context](cockpit-panel.md#building-it)). ES2's update refreshes modes 0 and 4 every 30 coarse ticks and the others every frame, redraws the title over modes 2 and 5, and draws a transmitting squadmate's picture over the screen ([paint order](../../retail/simulation/mfd.md#paint-order), [transmissions](../../retail/simulation/mfd.md#transmissions)); ES1's draws no picture ([Open](#open)).

### The dropout

**A display whose feed is damaged goes blank or shows `OFFLINE` in bursts, as ES1's other instruments flicker ([sensor dropout](cockpit-widgets.md#sensor-dropout)).** The condition is `Player_DependentCondition`(4). While the dropout holds the display dark, each repaint writes `MfdDisplay_OfflineLabel` instead of painting the screen: `OFFLINE` in `cpred` on a fill of `0x3f` for a condition of 2 or more, and the fill alone for 1. At a condition of 3 the tick holds the display dark without setting the repaint byte `+0x75`, so the label is painted at the next repaint, such as the one `MfdDisplay_SetMode` asks for through that byte. The screen's update in step 4 still runs on its own schedule and paints over the label: at once on the scanner, at the next refresh on the status screens and the NAV MAP, and on FLASH COMM only the rows it rewrites ([Open](#open)). ES2's dark display skips the screen's paint, with no label ([paint order](../../retail/simulation/mfd.md#paint-order)); ES2's constructor builds a label the same way, once per process, at `0049cbd0` ([Open there](../../retail/simulation/mfd.md#open)).

## Clicks and keys

**A button's click reaches the display through its click slot, `Widget_ForwardClickToOwner`, which calls `MfdButton_OnClick` and then invalidates the button and plays the click sound `0x11` ([the gadget bases](cockpit-widgets.md#the-gadget-bases)).** `CockpitWidgets_HandleCommand` presses buttons 0 to 5 on `F1`–`F6` and passes every key it does not take to `MfdFlashComm_HandleAltKey` and then down its chain of key slots, the display's last ([keys](cockpit-panel.md#keys-and-the-view-device)). The instruments before it in that chain take no `T`: the weapon row at `+0x1d4`, the energy meter, the shield display and the throttle answer 0 for every code ([the weapon rows](cockpit-weapon-gauges.md#the-row-gauge-base), [the shield display](cockpit-energy-shields.md#the-display-object), [the energy meter](cockpit-energy-shields.md#the-meter-object), [the throttle](cockpit-throttle-gauge.md#the-gauge--throttlegauge_ctor-0003b0d4)), the console's `ConsoleButtons_HandleCommand` takes `0x29`, `0x229`, `0x214` and `0x26` ([the console buttons](cockpit-weapon-gauges.md#the-console-buttons)), and the gunsight's answers 0.

- **`MfdButton_OnClick` (`0003a46c`)**, (display, flags, child), slot `+4`, finds the child among the buttons and switches on its index: 0 to 5 call `MfdDisplay_SetMode`(display, index); 6 `MfdDisplay_CycleMode`(display); 7, in mode 3, `Mech_ToggleRadarMode` on the cockpit's machine, which flips its radar `+0x90` ([the HERC's cockpit](mech.md#the-cockpit)); 8 `MfdDisplay_CycleScannerRange`(display) and 9 `Cockpit_CycleTarget`, in any mode; 10, in mode 1 and while `CockpitMouse_DoubleClick` (`00094c5c`) is clear, `MfdFlashComm_Transmit`; and 11 and 12, in mode 1, `MfdFlashComm_PrevAvailableRow` and `MfdFlashComm_NextAvailableRow` ([picking a row](mfd-flash-comm.md#picking-a-row)). Cases 8 to 12 then set the byte `+0xae` + mode. ES2's (`0044681c`, [button visibility](../../retail/simulation/mfd.md#button-visibility)) takes (display, button), gives 7 and 9 one case that steps the status screen's subject in mode 0 and cycles the target in the others, stores 0 or 1 in the machine's `+0x96` on 11 and 12, PASS and ACTIVE, and tests the mode only in that shared case.
- **`Cockpit_CycleTarget` (`0003ab08`)**, (display): `TargetSelect_Cycle`(`CockpitViewInstance`) ([the target selection](cockpit-panel.md#the-target-selection)), removing its argument with `RET 4`. ES2's (`00446f88`) takes none.
- **`MfdDisplay_KeyDispatch` (`0003a5e8`)**, (display, code), slot `+0x10`: it answers 0 while `CockpitView_RectCornerVisible` refuses the screen's rect `+0xb4`. Six codes, the table `00094054`, press a button when its byte in `MfdButtonVisibility` for the mode is set, and return 1 either way: `0x20` (`D`) button 7, `0x213` ([Alt]+R) button 8, or `MfdDisplay_CycleScannerRange`(display) when its byte is 0, `0x14` (`T`) button 9, `0x2d` (`X`) button 10, `0x33` (`,`) button 11 and `0x34` (`.`) button 12. A press skips a button whose `int16` `+0x17` is 2 or 3 and otherwise calls its click, slot `+0xc` of its table at `+0x1c`, with (button, 1), as `CockpitWidgets_HandleCommand`'s does. In mode 1, `0x1e`, `0x22`, `0x23`, `0x18`, `0x2e`, `0x12` and `0x21` (`A`, `G`, `H`, `O`, `C`, `E`, `F`), the table `00094084`, pass (display, row, `+0xc4`) to `MfdFlashComm_SelectRow` for rows 0, 1, 2, 3, 4, 4 and 5 and return 1. Any other code answers 0. The handlers of `0x14` to `0x34` and of rows 2 to 5 are undefined bytes in `ES1Recon`, read here as the image's bytes. ES2's (`004469c0`, [keyboard](../../retail/simulation/mfd.md#keyboard)) presses button 9 on [Alt]+T (`0x214`) and steps to the previous or next available row on `,` and `.`.

So `T` steps the target selection as `Tab` does in every mode while the screen is on screen, outside view 1, `D` switches the radar between active and passive and [Alt]+R presses RANGE on the SCANNER alone, [Alt]+R steps the range from any other screen, and `X`, `,` and `.` press XMIT and the row arrows on FLASH COMM alone.

`0003ab16`–`0003ab1a`, after `Cockpit_CycleTarget`, is two `0x90` bytes and a frameless `RET 8`, `MfdDisplay_StubNoOp` (`0003ab18`), before `MfdDisplay_CycleScannerRange`'s padding byte; after `MfdDisplay_Update`'s `RET 4` at `0003a45d` stand two more frameless stubs, `MfdDisplay_StubRet8` (`0003a460`, `RET 8`) and `MfdDisplay_StubReturnZero` (`0003a464`, `XOR EAX,EAX` and `RET 4`). `es2_xref.py` finds no branch or stored pointer to any of them ([Open](#open)).

## Functions

| ES1 | Address | ES2 |
|---|---|---|
| `HeapPtr_DtorMfdDisplayCopy`, `ConfigPart_DtorMfdDisplayCopy`, `ConfigPart_GetClassTagMfdDisplayCopy`, `TSPartBase_DtorMfdDisplayCopy` | `00039bb4`, `00039bdc`, `00039c1b`, `00039c24` | the run; slots `+0x1c` and `+4` of `00093ef6` and `+0x1c` of `00093f2a` |
| `MFDisplay_Ctor` | `00039c50` | `004451f0` |
| `MfdDisplay_Ctor`, `MfdDisplay_Repaint`, `MfdDisplay_Update` | `00039c64`, `0003a1f0`, `0003a2a8` | `00445218`, `00446138`, `00446328` |
| `MfdDisplay_StubRet8`, `MfdDisplay_StubReturnZero` | `0003a460`, `0003a464` | — |
| `MfdButton_OnClick`, `MfdDisplay_KeyDispatch` | `0003a46c`, `0003a5e8` | `0044681c`, `004469c0` |
| `MfdDisplay_CycleMode`, `MfdDisplay_SetMode` | `0003a9c4`, `0003a9ec` | `00446e14`, `00446e38` |
| `Cockpit_CycleTarget`, `MfdDisplay_StubNoOp` | `0003ab08`, `0003ab18` | `00446f88`, — |
| `MfdDisplay_CycleScannerRange`, `MfdDisplay_SetScannerRange` | `0003ab1c`, `0003ab44` | `00446fc8`, `00446fec` |
| `MfdDisplay_SyncMissileCamMode`, `MfdDisplay_InScannerRange` | `0003ad08`, `0003ae00` | `00447164`, `0044725c` |
| `MFDSelectGadget_Ctor`, `MFDSelectGadget_Paint`, `ShieldFacing_FlushDeferredPaintMfdDisplayCopy` | `0003ae50`, `0003af24`, `0003afa8` | `004472e4`, —, — |
| `MFDSelectGadget_PaintThunk`, `MFDSelectGadget_FlushDeferredPaintThunk` | `0003af0c`, `0003af18` | — |
| `CTLButtonControl_PaintNoOpMfdDisplayCopy`, `CTLControl_InvalidateNoOpMfdDisplayCopy`, `CTLControl_OnClickNoOpMfdDisplayCopy`, `CTLControl_FlushDeferredPaintNoOpMfdDisplayCopy`, `Widget_GetButtonValueMfdDisplayCopy`, `CTLControl_SetValueNoOpMfdDisplayCopy`, `CTLControl_DragMoveNoOpMfdDisplayCopy`, `Widget_InvalidateDeferredMfdDisplayCopy` | `0003aed8`, `0003aedb`, `0003aedf`, `0003aee3`, `0003aee7`, `0003aef1`, `0003aef7`, `0003aefc` | the same slots as the shield display file's copies ([its functions](cockpit-energy-shields.md#functions)) |
| `MFDListGadget_Ctor`, `MFDListGadget_OnClick` | `0003afe0`, `0003b088` | —, `MFDListGadget_OnClick` (`00447630`) |
| `MFDListGadget_PaintNoOpThunk`, `MFDListGadget_FlushDeferredPaintNoOpThunk`, `MFDListGadget_PaintNoOp`, `MFDListGadget_InvalidateNoOp`, `MFDListGadget_FlushDeferredPaintNoOp` | `0003b064`, `0003b070`, `0003b07a`, `0003b07f`, `0003b083` | — |

The FLASH COMM functions in the file, `MfdFlashComm_HandleAltKey` (`0003a890`), `MfdFlashComm_NextAvailableRow` (`0003ab70`), `MfdFlashComm_PrevAvailableRow` (`0003abac`), `MfdDisplay_SetSharedRow` (`0003abe8`), `MfdFlashComm_HandleListClick` (`0003ac04`), `MfdFlashComm_SelectRow` (`0003acd4`) and `MfdFlashComm_Transmit` (`0003adc8`), are in [the FLASH COMM screen's doc](mfd-flash-comm.md#functions).

## Globals

| | Address |
|---|---|
| `g_MfdMissileCamSavedMode` (`int16`, 0), `g_MfdMissileCamMode` (`int16`, −1), `g_MfdMissileCamArmed` (byte, 1) | `00093f5a`, `00093f5c`, `00093f5e` |
| `MfdDisplay_OfflineLabel` | `00093f60` |
| `MfdButtonVisibility`, six rows of 13 bytes | `00093f64` |
| `MfdDisplayVtable` | `00093fb6` |
| `CTLButtonControlVtableMfdDisplayCopy`, `PanelGadgetVtableMfdDisplayCopy`, `PanelSelectGadgetVtableMfdDisplayCopy` | `000940f6`, `0009411e`, `00094136` |
| `MFDSelectGadget_PanelGadgetVtable`, `MFDSelectGadgetVtable` | `0009415e`, `00094176` |
| `MFDListGadget_PanelGadgetVtable`, `PanelListGadgetVtableMfdDisplayCopy`, `MFDListGadgetVtable` | `0009419e`, `000941b6`, `000941de` |
| `MfdScannerRanges`, three `int32` | `000bd4f4` |

## Open

- **Open:** what calls `HeapPtr_DtorMfdDisplayCopy`, `MfdDisplay_StubNoOp`, `MfdDisplay_StubRet8` or `MfdDisplay_StubReturnZero`: `es2_xref.py` finds no branch or stored pointer to any of them or their padding.
- **Open:** what reads the shared block's second dword (`+0x7e`) and the byte `+0xc8`: the constructor clears the first with the block's `memset` and the second with a byte store, and `es2_fieldscan.py` over `00032ed8`–`0003b0ab` finds no read of either in the display's code, against `+0xc9`'s read and write in `MfdDisplay_Update` and write in the constructor as control.
- **Open:** whether ES1 cycles the display's mode from anything but the title: `es2_xref.py` finds `MfdDisplay_CycleMode` called from `MfdButton_OnClick` alone, where ES2's `Sim_PollPlayerInput` calls ES2's on a command.
- **Open:** what `00040254`, the caller of `MfdDisplay_InScannerRange`, is, unread.
- **Open:** which ES1 projectile records have type 0 and subtype 3, the launchers that switch the display to the missile camera.
- **Open:** whether another module draws a squadmate's picture over the display while he transmits, as ES2's display does itself: `MfdDisplay_Update` draws none. A retail check: order a squadmate with FLASH COMM's XMIT and watch the display while he answers.
- **Open:** whether `OFFLINE` shows at all on the SCANNER, whose update paints over it on the frame it is copied. A retail check: with the display's feed damaged (`Player_DependentCondition`(4) 2 or more), the STATUS screen shows `OFFLINE` for up to half a second at a time, the SCANNER keeps its plot.
- **Open:** what the `dba\pbuttons` frames 3 to 5, 10 to 21 and 29 to 34 show.
- **Open:** a positive control in the running game for the done checklist ([when a module is done](../../herculan/plan-es1-support.md#when-a-module-is-done)). Proposed: in DOSBox-X's debugger, `BP <CS>:0003a9ec` fires once with the word at `[ESP+8]` 0 as the cockpit is built, and again with 3 when `F4` is pressed; the title then reads `SCANNER` and the `MODE` and `RANGE` buttons are drawn undimmed.
