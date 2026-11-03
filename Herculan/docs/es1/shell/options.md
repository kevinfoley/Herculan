# GO.EXE — PREFERENCES and CONTROLS

**OPTIONS' PREFERENCES and CONTROLS change the settings kept in `data\prefs.cfg`: PREFERENCES the shell's music, the click of its buttons, the simulator's detail and the pilots' speech, CONTROLS the joystick and its add-ons. Both go back to OPTIONS** ([the options menu](main-menu.md#the-options-menu)).

Addresses are `ES1GO` image addresses ([the X-32 executable](../formats/x32-executable.md#the-image)). The functions here remove their own arguments, so argument lists are in stack order ([calling conventions](../formats/compiler.md#calling-conventions)). The shell's 17 options are `Prefs_Options` (`000aa24e`), byte *n* option *n* of `data\prefs.cfg`, with a committed copy `Prefs_Committed` (`000aa260`); what each option means to the simulator is [its option array](../simulation/preferences.md#the-option-array).

## PREFERENCES — screen 1310

**A window titled PREFERENCES with four settings, each a button that steps it and a box beside it showing its value: MUSIC and SOUND, OFF or ON; DETAIL, LOW, MEDIUM, HIGH or MAX; SPEECH, TEXT ONLY, VOICE ONLY or TEXT AND VOICE. A change takes effect at once; ACCEPT writes the settings to `data\prefs.cfg`, CANCEL puts back the ones the window opened with and writes those, and either goes back to OPTIONS.** Without a sound manager MUSIC, SOUND and SPEECH are inactive, and without `vol\speech.vol` SPEECH does nothing.

`dlg\menu1310.dlg`'s window is (49, 50)–(281, 165). The pointer starts on ACCEPT, the control keyed `Space` ([opening a menu](menus.md#opening-a-menu--menu_open-0003969e)).

| Control | Id | Rect | Key |
|---|---|---|---|
| MUSIC | 1311 | (27, 27)–(75, 39) | |
| its box | 1314 | (77, 27)–(102, 39) | |
| SOUND | 1312 | (27, 41)–(75, 53) | |
| its box | 1315 | (77, 41)–(102, 53) | |
| DETAIL | 1313 | (129, 27)–(179, 39) | |
| its box | 1316 | (116, 41)–(192, 53) | |
| SPEECH | 1317 | (27, 63)–(75, 75) | |
| its box | 1318 | (77, 63)–(192, 75) | |
| CANCEL | 99 | (112, 86)–(159, 98) | `0x01`, `Esc` |
| ACCEPT | 100 | (160, 86)–(207, 98) | `0x39`, `Space` |

The buttons are text buttons (kind 3) and the boxes list boxes of style 1 (kind 15) ([the kinds](../formats/dlg-menu-dialogs.md#the-kinds)).

### Opening it

Screen 1310's case of `Shell_HandleMessage` (`0003e7eb`) allocates a `0x2c`-byte `PrefsMenu` (vtable `00081f60`, handler `PrefsMenu_OnMessage`, destructor `PrefsMenu_Dtor` (`000402d2`)): [the screen object](menus.md#the-screen-object) and four fields copied from the shell's settings, which CANCEL puts back.

| Offset | Copy of |
|---|---|
| `+0x26` | the byte `Shell_MusicOption` (`0007fcb8`) |
| `+0x27` | the byte `Shell_SoundOption` (`0007fcb9`) |
| `+0x28` | the `int16` `Shell_DetailOption` (`0007fcba`) |
| `+0x2a` | the `int16` `Shell_RadioOption` (`0007fcbc`) |

`Shell_LoadData` sets the four from `prefs.cfg` at the shell's start ([the shared data](startup.md#the-shared-data)). `Menu_Open`, with no sound manager (the dword `00082768` 0), makes MUSIC, SOUND and SPEECH inactive, each found in any state with `DUIWindow_FindControlById` (`0001be16`) and given state 2 at `+0x19` and 1 at `+0x1f`. The boxes' lines are word lists of `eng\watext.bin` lines from `Menu_ListLines` (`0003750d`), and each box's selected line is its setting ([the build](menus.md#opening-a-menu--menu_open-0003969e), kind 15):

| Box | Lines | Selected line | Alignment |
|---|---|---|---|
| 1314 | `PrefsMenu_OffOnLines` (`000811a8`): OFF and ON, lines 2 and 3 (`000376b1`) | `Shell_MusicOption` (`0003a973`) | 0 |
| 1315 | the same list (`000376b1`) | `Shell_SoundOption` (`0003a986`) | 0 |
| 1316 | `PrefsMenu_DetailLines` (`000811b0`): LOW, MEDIUM, HIGH and MAX, lines 4 to 7 (`000376c2`) | `Shell_DetailOption` (`0003a999`) | 2, centered |
| 1318 | `PrefsMenu_SpeechLines` (`000811bc`): TEXT ONLY, VOICE ONLY and TEXT AND VOICE, lines `0x10` to `0x12` (`000376d3`) | `Shell_RadioOption` (`0003a9b5`) | 0 |

Kind 16's tables name 1316 as well, with `res32\bluebar.dba` and the word `Shell_DetailOption`, and 1321 with `res32\controls.dba` ([the build](menus.md#opening-a-menu--menu_open-0003969e)); in `dlg\menu1310.dlg` 1316 is a list box, and of the 39 `.DLG` files in `SHELL0.VOL` none holds 1321, nor 1325 to 1327, which kind 15's table names (`HercWorks.Query menu`).

### The buttons — `PrefsMenu_OnMessage` (`0002b82b`)

`PrefsMenu_OnMessage`(menu, message) looks the message's target `+6`, the pressed button's id, up among the six `int16` at `00080aac` with `REPNE SCASD` and jumps to the target beside it (`00080ac4`, case bodies Ghidra left undefined); any other target returns at once. A setting's button changes the setting, selects its line in the box through the box's slot `+0x24` (`MTStrListBox_SelectLine`, [the list box](menu-controls.md#the-list-box)), the box found with `DUIWindow_FindEnabledControlById` (`0001bda4`), and stores the setting as an option with `Prefs_SetOption` (`00008378`)(1, value, option). That runs the option's handler, and the shell registers one only for option 11 ([the start-up](startup.md#the-start-up), step 9). The target, a button's id, is not a screen, so the window stays ([changing screens](menus.md#changing-screens--shell_handlemessage-0003c7d8)).

| Button | Case | The setting | Then |
|---|---|---|---|
| MUSIC | `0002b8b3` | `Shell_MusicOption` XOR 1, box 1314 | with the backdrop's music `ShellMusic_Object` (`0007fcfc`, made by `ShellMusic_Play`, [the backdrop](menus.md#the-backdrop)) set, plays it through its slot `+0x10` when the setting is now 1 and stops it through slot `+0x14` when 0 (the vtable at `+0x3b`, [sound objects](../formats/sound-samples.md#sound-objects)); option 0 |
| SOUND | `0002b854` | `Shell_SoundOption` XOR 1, box 1315 | option 1 |
| DETAIL | `0002b961` | `Shell_DetailOption` + 1, modulo 4, box 1316 | options 4, 5 and 6, SKY, GROUND and SHAPES, each the setting × 64 + `0x3f`: `0x3f`, `0x7f`, `0xbf` or `0xff`; then option 3, HORIZON, 0 for LOW and 1 otherwise |
| SPEECH | `0002ba18` | while the byte `Speech_ArchiveFound` (`0007e09c`) is set: `Shell_RadioOption` + 1, modulo 3, box 1318 | option 2 |
| CANCEL | `0002ba91` | the four copied back from `+0x26` to `+0x2a` | the music played or stopped as MUSIC does, for the byte put back; `Prefs_RevertAll` (`00008316`)(1), every option back to its committed copy; then ACCEPT's code |
| ACCEPT | `0002bb0d` | | `Prefs_CommitAll` (`000082b0`)(1), every option made the committed copy; `Prefs_Save` (`00008267`), the 17 options to `data\prefs.cfg`; and the message's target set to 1300, OPTIONS |

So both CANCEL and ACCEPT write `data\prefs.cfg`, CANCEL with the committed copy as it stood before PREFERENCES opened. `Speech_ArchiveFound` is set by `Prefs_Init` from `Speech_ArchivePresent` (`000087ce`), 1 when `fopen` opens `vol\speech.vol` (mode `rb`, a path relative to the current folder), and without it `Prefs_Init` sets option 2 to 0 ([the start-up](startup.md#the-start-up), step 9).

What the settings reach in the shell: `Shell_MusicOption` gates the backdrop's music ([the backdrop](menus.md#the-backdrop)), `Shell_SoundOption` a text button's click ([drawing a control](menu-controls.md#drawing-a-control)), and `Shell_RadioOption` the message box's text and speech ([the shared data](startup.md#the-shared-data)). `es2_xref.py --decode` finds `Shell_DetailOption` read by the 1310 case, the box 1316's build and kind 16's case for 1316, and written by `Shell_LoadData` and this handler; the detail itself is the simulator's, from options 3 to 6.

### DETAIL after a restart

`Shell_LoadData` sets `Shell_DetailOption` at each start of the shell to (`0x3f`, or `0xbf` with option 3 set, plus options 4, 5 and 6) / 256, the bases the words `0007fd10` and `0007fd18` hold ([the shared data](startup.md#the-shared-data)). For the four settings DETAIL writes that gives 0, 2, 2 and 3: LOW, HIGH, HIGH and MAX, so a DETAIL left at MEDIUM shows HIGH the next time the shell starts, and the options keep MEDIUM's values until DETAIL is pressed again ([Open](#open)). Settings the simulator's own panel made, whose bars move options 4 to 6 one at a time ([the preferences panel](../simulation/preferences.md#the-preferences-panel--preferencespanel_ctor-00047578)), show as whatever level their sum reaches.

## CONTROLS — screen 1320

**CONTROLS puts the simulator's controls panel over the backdrop: the same window, buttons and settings as `F12`'s CONTROLS in a mission ([the controls panel](../simulation/preferences.md#the-controls-panel--controlspanel_ctor-00048408)), with the shell's own copy of the code. DONE or [Esc] rebuilds the joystick devices, saves the five settings and goes back to OPTIONS.** What a player could see differently from the mission's panel: it opens at OPTIONS' window's corner (49, 50) rather than centered at (48, 45), its readouts sit on another background color in the shell's `nm_green` font, and [Ctrl+C] on it runs the shell's capture ([Open](#open)). No `dlg\menu1320.dlg` exists; the screen is a panel, not a `.DLG` menu.

### The case

Screen 1320's case of `Shell_HandleMessage` (`0003e9ae`–`0003ec5d`):

1. Sets the message's target `+0xe` to 1300, OPTIONS.
2. Shows the page last drawn and draws on the other, as [presenting a frame](../rendering/screen-pages.md#presenting-a-frame) does in its step 4: the display's (`000ab616`) visible page `+0x30` from `+0x88`, its draw page `+0x38` from `+0x84`, each passed to the raster as a page store is, then `+0x84` and `+0x88` swapped.
3. Starts the mouse queue (`MouseEventQueue_Start`, [the mouse queue](startup.md#the-mouse-queue)) and sets `CockpitMouseLive` (`000aa778`).
4. Builds the panel, `ControlsPanel_Ctor`(0, the point (49, 50), 1) at `0003eab3`, the point on the stack: the panel allocates itself, and with a point given it is placed at (49, 50) rather than centered.
5. With the keyboard device (`Input_GetKeyboardDevice_Thunk`) and `Shell_DebugMode` (`0007fd08`, `-X`) clear: removes `Shell_NavKeySubscriber` and `Shell_ScanKeySubscriber` ([the keyboard subscribers](startup.md#the-keyboard-subscribers)), keeps the device's mode (`KeyboardInputDevice_GetMode`) and sets it to `0x20000`, the mode the start-up gives it without `-X`, and adds `Esc`, `Tab` and `Enter`, the NUL-ended codes at `000817b8`, to `KeyQueue_Subscriber` (`Keyboard_AddSubscriberCodes`), so those keys reach the panel's input block instead of the dialog.
6. Runs the panel, its slot `+0xc`, `ControlsPanel_Run`, and destroys it with `ControlsPanel_Dtor`(panel, 3).
7. Stops the mouse queue (`MouseEventQueue_Stop`) and clears `CockpitMouseLive`.
8. With the same two conditions: adds `Shell_NavKeySubscriber` again for its codes at `0007e7f0` and `Shell_ScanKeySubscriber` for the NUL-ended codes at `0007e7f4`, `Space`, the four arrows, `Backspace`, `Home`, `End`, `Esc` and `Del`; takes `Esc`, `Tab` and `Enter` off `KeyQueue_Subscriber` (`Keyboard_RemoveSubscriberCodes`); and puts the device's mode back.
9. Runs on into screen 1300's case, which opens OPTIONS ([the options menu](main-menu.md#the-options-menu)).

So after CONTROLS `Shell_ScanKeySubscriber` is registered whether or not an edit box is up, which the start-up does not do ([Open](#open)).

### The panel — `ControlsPanel_Ctor` (`00006050`)

The shell's panel is the simulator's ([the controls panel](../simulation/preferences.md#the-controls-panel--controlspanel_ctor-00048408), [its loop](../simulation/preferences.md#its-loop--controlspanel_run-00048810), [rebuilding the joystick devices](../simulation/preferences.md#rebuilding-the-joystick-devices--controlspanel_applydevices-00048e98)) statement for statement, on [the shell's panel base](#the-panel-base--alertpanel_ctorbase-0000222d), with these differences:

- **The object** is `0x3a3` bytes, every field from the vptr on 3 bytes lower than the simulator's: `ControlsAlertVtable` (`0007dddc`) at `+0x311`, the strings from `+0x315` (title), `+0x319` (captions), `+0x335`, `+0x33d`, `+0x345` and `+0x36d`, the readouts at `+0x381`, the action at `+0x395`, the copies of options 7, 8, 10, 9 and 11 at `+0x399` to `+0x39d`, and the kept values at `+0x39e` to `+0x3a2`. The vtable's slots are the simulator's: `ControlsPanel_Paint` (`0000695f`), `AlertPanel_SetTitleText` (`00002c25`), `ControlsPanel_Run` (`000064c4`), `ControlsPanel_OnClick` (`00006af3`), `AlertPanel_HandleEvent` (`000031fe`), `ControlsPanel_GetButtonCaption` (`00006bce`).
- **Its numbers come from `CTL_ALRT.BND`** (`CtlAlert_Bnd`, `0009a34c`, [its fields](../formats/bnd-notes.md#bndalertbnd-and-bndctl_alrtbnd)) where the simulator's are immediates and tables: the size, the title bar's margin, top and height and the title label's offset, the counts of buttons and readouts and their rects, and the captions' and readouts' margins. The file's values are the simulator's, so the panel has the same size, rects and places.
- **Fonts and colors**: the captions in `Shell_Fonts` (`000aacc6`) entry 1, `cpylw`, the title in entry 0, `title`, as the simulator's; the readouts in entry 5, `nm_green` where the simulator's use `green6x8`, on background `0x1f` where the simulator's is `0x3f` ([the loads](../formats/styles-dat.md#the-loads)).
- **The loop** builds its input with `Shell_PollInput` (`0004de3a`, [the input](startup.md#the-input)), and `JOYSTICK`'s `NONE` test reads the trigger at the shell's block `+0x1d`. CALIBRATE builds the shell's calibration panel `00006f15`, runs `00007631` and destroys it with `00007561`(that, 2) (decision 9). DONE commits with `Prefs_CommitAll` (`000082b0`)(1) ([the buttons](#the-buttons--prefsmenu_onmessage-0002b82b)), which runs the handler of each option that differs from its committed copy; of options 7 to 11 the shell registers one only for 11, `MOUSE` ([the start-up](startup.md#the-start-up), step 9), so no handler runs in the shell for the stick mode or the throttle keys. The steps and toggles are the shell's `Prefs_ToggleOption` (`000083ac`), `Prefs_StepOption` (`000083dd`) and `Prefs_StepOptionBack` (`00008418`), the simulator's without its copy to a previous-value array; each writes through `Prefs_SetOption` with apply 1, so a `MOUSE` click runs the shell's handler `0000868c` at once.
- **Rebuilding the devices** (`ControlsPanel_ApplyDevices`, `00006bed`) loads both calibration files by the names `Shell_FileNames` (`0009afb8`) lines 1 and 2 give, each through `Path_ApplyAltDrive`, with `Joystick_LoadCalibration` (`000086d6`), where the simulator's opens the second as `joy2.cal` in the current directory; it saves options 7, 8, 10, 9 and 11 (`ControlsPanel_SaveList`, `0007de60`) to `data\prefs.cfg` with `Prefs_SaveSelectedOptions` (`0000855e`), the simulator's read-modify-write ([the option array](../simulation/preferences.md#the-option-array)).

`ControlsPanel_Revert` (`00006df6`) is the simulator's, calling `000084b8` for each option; `es2_xref.py` finds no reference to it. `CtlAlert_StaticInit` (`00006ebe`) registers `CtlAlert_LoadBnd` at phase 1 ([the shell's files](../formats/bnd-notes.md#the-shells-files)).

### The panel base — `AlertPanel_CtorBase` (`0000222d`)

**The shell's `src\alert.cpp`, `0000222d`–`00003d7b`, is the simulator's panel base ([the in-mission panels](../simulation/alert-panels.md)) statement for statement, on the shell's globals.** It opens with `Alert_LoadBnd` (`00002204`) and ends with `Alert_StaticInit` (`00003c22`), which registers `Alert_LoadBnd` at phase 1 and then sets `AlertPanel_ScreenRect` (`0009a320`) to {0, 0, 319, 199} and clears `Alert_FillBrush` (`0009a330`), as the simulator's does. `es2_xref.py` finds its constructor called by the controls panel and by the calibration panel `00006f15`. The differences:

- **The input block's copy** at `+0x2e8` is `0x29` bytes, the block at `000a0ba4`, where the simulator's is `0x2b`; so the vptr is at `+0x311`, and there is no byte `+0x313`: `AlertPanel_RestoreCursorImage` (`00002f5c`) puts the cursor image back on every leave. `AlertVtable` (`0007d920`) holds the simulator's slots: `AlertPanel_SetTitleText` (`00002c25`) at `+8`, `AlertPanel_OnChildClickNoOp` (`0000265b`) at `+0x10`, `AlertPanel_HandleEvent` (`000031fe`) at `+0x14`.
- **`AlertPanel_Enter` (`0000290b`)** latches no button, sets the display's source and draw pages (`+0x34`, `+0x38` of `000ab616`) to its `+0x84`, the page on the screen after the case's swap, calls the hook `0007d84c`, loads `dci\alert` once into `0007d848` with `00008cc2`, and keeps no open count.
- **`AlertPanel_Leave` (`00002b0a`)** calls the hook `0007d850`, puts back the draw page at `+0x38` and the `0x29` bytes, and does not move the mouse back to the pointer saved with the block; it keeps no open count.
- **`AlertPanel_SetFocus` (`00002fa2`)** reads the pointer from `Shell_PollInput`.
- **`AlertPanel_HandleEvent` (`000031fe`)** reads buttons 0 and 1 at the shell's block `+0x1d` and `+0x1e` and latches them with the shell's `Input_LatchButton` (`0004de18`); its table `0007d9f0` holds six codes, the simulator's five and `0x42e`, [Ctrl+C], whose case calls `00014ac7`(`CAPTURE`) and returns 1 ([Open](#open)). Keypad 0 still reads option 10, the shell's (`000aa258`).
- **The buttons and the slider** (`PanelButton_*`, `AlertSliderGadget_*`) are the simulator's, through this file's tables (`0007da34`, `0007da90`, `0007dac2`, `AlertSelectGadgetVtable` `0007daec`; the slider's `0007db22`, `0007db8a`, `0007dbc8`, `0007dbf0`); the slider takes its knob's length from `ALERT.BND` (`AlertSlider_KnobLength`, `0009a31c`, 7, the simulator's constant), and `es2_xref.py` finds no reference to `AlertSliderGadget_Ctor` (`00003684`). The no-op slots load their arguments into registers and return, where the simulator's are `NOP`s and a `RET`.
- **`AlertPanel_SetTitleText` (`00002c25`)** blits the corners of the bank the dword `000ab4ee` holds.

## Functions

| ES1 | Address | ES1DBSIM |
|---|---|---|
| `PrefsMenu_OnMessage`, `PrefsMenu_Dtor` | `0002b82b`, `000402d2` | |
| `Prefs_Save`, `Prefs_CommitAll`, `Prefs_RevertAll`, `Prefs_SetOption` | `00008267`, `000082b0`, `00008316`, `00008378` | |
| `Prefs_ToggleOption`, `Prefs_StepOption`, `Prefs_StepOptionBack`, `Prefs_SaveSelectedOptions` | `000083ac`, `000083dd`, `00008418`, `0000855e` | `0004a94c`, `0004a988`, `0004a9d0`, `0004ab6c` |
| `Speech_ArchivePresent`, `Joystick_LoadCalibration` | `000087ce`, `000086d6` | —, `0004ae0c` |
| `ControlsPanel_Ctor`, `ControlsPanel_Dtor`, `ControlsPanel_Run`, `ControlsPanel_Paint` | `00006050`, `00006455`, `000064c4`, `0000695f` | `00048408`, `000487b4`, `00048810`, `00048c58` |
| `ControlsPanel_RefreshRow`, `ControlsPanel_OnClick`, `ControlsPanel_GetButtonCaption` | `000069ef`, `00006af3`, `00006bce` | `00048ce4`, `00048db0`, `00048e80` |
| `ControlsPanel_ApplyDevices`, `ControlsPanel_Revert`, `CtlAlert_StaticInit` | `00006bed`, `00006df6`, `00006ebe` | `00048e98`, `0004903c`, — |
| `AlertPanel_CtorBase`, `Alert_Dtor`, `AlertPanel_Enter`, `AlertPanel_Leave` | `0000222d`, `0000266b`, `0000290b`, `00002b0a` | `00045988`, `00045d0c`, `00045f00`, `000460b8` |
| `AlertPanel_SetTitleText`, `AlertPanel_RestoreCursorImage`, `AlertPanel_SetFocus`, `AlertPanel_HandleEvent` | `00002c25`, `00002f5c`, `00002fa2`, `000031fe` | `000461ec`, `00046428`, `00046470`, `00046670` |
| `AlertPanel_OnChildClickNoOp`, `Alert_StaticInit` | `0000265b`, `00003c22` | `00045d06`, `00046e6c` |
| `GLBase_DtorAlertCopy`, `GLRegion_DtorAlertCopy`, `GLRectangleRegion_DtorAlertCopy`, `GLViewport_DtorAlertCopy` | `0000257b`, `000025a7`, `000025f0`, `000027cf` | `00045c8c`, `00045cb0`, `00045cd8`, `00045de8` |
| `PanelButton_Ctor`, `PanelButton_Paint`, `PanelButton_Tick`, `PanelButton_OnClick`, `Rect_CopyCtorAlertCopy` | `000033d1`, `0000353c`, `0000360a`, `00003630`, `000035dc` | `000467c8`, `000468e0`, `0004698c`, `000469ac`, `0004695c` |
| the button tables' copies: `CTLButtonControl_PaintNoOpAlertCopy`, `CTLControl_InvalidateNoOpAlertCopy`, `CTLControl_OnClickNoOpAlertCopy`, `CTLControl_FlushDeferredPaintNoOpAlertCopy` | `000034c0`, `000034ca`, `000034d4`, `000034e1` | `00046893`, `00046897`, `0004689b`, `0004689f` |
| `Widget_GetButtonValueAlertCopy`, `CTLControl_SetValueNoOpAlertCopy`, `CTLControl_DragMoveNoOpAlertCopy`, `Widget_InvalidateDeferredAlertCopy` | `000034eb`, `000034fb`, `00003509`, `00003516` | `000468a3`, `000468ad`, `000468b3`, `000468b8` |
| `AlertSliderGadget_Ctor`, `AlertSliderGadget_OnClick`, `AlertSliderGadget_Paint`, `AlertSliderGadget_FlushDeferredPaintNoOp` | `00003684`, `000039e4`, `00003a1c`, `00003c18` | `000469ec`, `00046ca0`, `00046cc8`, `00046e65` |
| `CTLSliderControl_PaintNoOpAlertCopy`, `CTLSliderControl_InvalidateNoOpAlertCopy`, `HSliderGadget_InvalidateDeferred` | `000039aa`, `000039b4`, `000039be` | `00046c6f`, `00046c73`, `00046c78` |
| `Input_GetJoystickDevice_Thunk`, `Input_GetJoystick2Device_Thunk`, `InputDevice_Disable` | `00027bd3`, `00027bdd`, `0002586e` | `0004c63c`, `0004c644`, `0004b2a4` |

## Globals

| | Address |
|---|---|
| `Shell_MusicOption`, `Shell_SoundOption`, bytes: `prefs.cfg` options 0 and 1 | `0007fcb8`, `0007fcb9` |
| `Shell_DetailOption`, `int16` 0 to 3 | `0007fcba` |
| `Shell_RadioOption`, `int16`: option 2 | `0007fcbc` |
| `Speech_ArchiveFound`, byte | `0007e09c` |
| `ShellMusic_Object`, the backdrop's music | `0007fcfc` |
| `PrefsMenu_OffOnLines`, `PrefsMenu_DetailLines`, `PrefsMenu_SpeechLines`, −1-ended `int16` lists | `000811a8`, `000811b0`, `000811bc` |
| `AlertVtable`, `ControlsAlertVtable` | `0007d920`, `0007dddc` |
| `ControlsPanel_SaveList`, `int16` 7, 8, 10, 9, 11 | `0007de60` |
| `AlertPanel_ScreenRect`, `Alert_FillBrush` | `0009a320`, `0009a330` |
| `CtlAlert_Bnd`, `CTL_ALRT.BND`; `AlertSlider_KnobLength`, `ALERT.BND`'s word | `0009a34c`; `0009a31c` |

## Open

- **Open:** DETAIL's MEDIUM read back as HIGH ([DETAIL after a restart](#detail-after-a-restart)). A retail check: in PREFERENCES set DETAIL to MEDIUM, ACCEPT, quit to DOS, start the game again and open PREFERENCES: the box shows MEDIUM or HIGH.
- **Open:** what `Shell_ScanKeySubscriber` does to the menus once CONTROLS has registered it ([the case](#the-case)): it hands `Space`, the arrows, `Backspace`, `Home`, `End`, `Esc` and `Del` to `Shell_ForwardKeyToDialog` with no edit box up. A retail check: open CONTROLS, press DONE, go back to the main menu and see whether `Space` and the arrows still move and press its buttons as before.
- **Open:** what `00014ac7` does with `CAPTURE`, the panel's [Ctrl+C] ([the panel base](#the-panel-base--alertpanel_ctorbase-0000222d)): `es2_xref.py` finds `Screenshot_BuildPath`'s two calls, with `.SCR` and `.PAL`, in it. A retail check: [Ctrl+C] on CONTROLS, then look for a new `CAPTURE*.SCR` in the game's folder.
- **Open:** the shell's calibration panel, `00006f15` and its loop `00007631` and destructor `00007561`, which CALIBRATE runs (decision 9).
