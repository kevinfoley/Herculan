# DBSIM.EXE — the preferences and controls panels

**`F12` and `Alt+P` stop the mission and put a strip of seven settings along the bottom of the screen, MUSIC, SOUNDS, RADIO, HORIZON, SKY, GROUND and SHAPES, with the outside view turning round the player's HERC above it; its CONTROLS button raises a second panel that chooses the joystick, what the stick drives, the throttle and rudder add-ons and the mouse, and calibrates the stick.** They are ES2's preferences and controls panels ([ES2's](../../retail/simulation/preferences.md)) on ES1's panel base ([the in-mission panels](alert-panels.md)) with ES1's options: the rows are the earlier design that ES2's v1.0 French and German tables translate ([ES2's text](../../retail/simulation/alert-panels.md#what-the-family-shares)), SHAPES where those say SHADOWS. What a player could see differently from ES2: SKY, GROUND and SHAPES are bars stepped an eighth of their range a click, and [=], [-] and the keypad's [+] and [-] step the focused bar; a change to HORIZON, SKY, GROUND or SHAPES shows at once in the view behind the panel; the controls panel chooses the stick and its add-ons instead of binding buttons, and closing it after a change of stick or rudder rebuilds the joystick devices at once; and changes made before the controls panel was closed can be left out of `data\prefs.cfg` ([saving](#saving)). Addresses are `ES1DBSIM` image addresses ([the X-32 executable](../formats/x32-executable.md#the-image)); ES2's are DBSIM's. The functions here remove their own arguments, so argument lists are in stack order ([calling conventions](../formats/compiler.md#calling-conventions)): a member function's object first, the rest in reverse of ES2's.

## Where the object files start and end

The two panels' code is `00047578`–`000490b7`: the preferences panel from `PreferencesPanel_Ctor` (`00047578`) to `PreferencesPanel_Revert` (`000483d4`), whose `RET 4` ends at `00048407`, and the controls panel from `ControlsPanel_Ctor` (`00048408`) to `ControlsPanel_Revert` (`0004903c`), whose `RET 4` at `000490b4` is followed by one zero byte and, at `000490b8`, the run of `ConfigPart`'s copies that opens the calibration panel's file, `cal_alrt.cpp` ([module copies](../formats/compiler.md#vtables)). No function of the two asserts, so no source path names them, and neither has an entry in the static constructor table. The status panel's `RET 8` ends at `00047577` and `PreferencesPanel_Revert`'s at `00048407`, each on a 4-byte boundary with no zero fill and no destructor copy after it, so whether `00047578` and `00048408` open files of their own is [Open](#open). Their data runs on in the same way: the status panel's jump table ends at `0009510b`, the preferences panel's data is `0009510c`–`00095243` and the controls panel's `00095244`–`0009537f`.

The preferences file the panels call into, `0004a724`–`0004ac5f`, follows the objectives panel's two zero bytes and ends with `Prefs_SetHandler`'s `RET 8` and one zero byte before the input devices' run ([where that file starts](joystick.md#where-the-object-files-start-and-end)), which holds the option handlers. `PreferencesPanel_Raise` (`00057820`) and `PreferencesPanel_OrbitViewHook` (`000577f8`) are in the file the run at `00057680` opens, before `main.cpp` ([the main loop's files](main-loop.md#where-the-object-files-start-and-end)).

## Raising the panel — `PreferencesPanel_Raise` (`00057820`)

**The panel stops the mission and shows the outside view of the player's HERC above it, turning round it while the panel is up; closing the panel puts the view back as it was, with no slide.** No arguments; `Sim_DispatchCommand`'s case for `F12` and `Alt+P` ([commands](main-loop.md#commands--sim_dispatchcommand-00059df8)), ES2's ([ES2's](../../retail/simulation/preferences.md#preferences-and-controls-dbsimexe)) on ES1's views. No tick runs until it returns: every frame it draws, and every frame the panel's loop draws, is `Cam_Update` and `Sim_RenderFrame` without `Sim_MainTick`, so nothing in the world moves but the camera. In order:

1. Builds the panel, `PreferencesPanel_Ctor`(0, 0) ([the panel](#the-preferences-panel--preferencespanel_ctor-00047578)).
2. Keeps `Sim_Frozen` (`000b5b92`) and sets it, so `Sim_EndFrame` keeps `Display_FlipWithoutPan` (`000bda96`) at 0, and sets that to 0 itself: each page flip from here waits for the next vertical retrace ([the retrace word](../rendering/screen-pages.md#the-simulators-own-page-routines)).
3. Copies the main camera (`ViewObjectPtr`, `000b5b8a`) into a camera on its stack (`TSCamera_CtorDefault`, the vtable `00096c5a` at `+0x12`, then `TSCamera_CopyFrom`, [the views](main-loop.md#the-views--viewchain_apply-0005894c)).
4. `ExternalView_SetRect` ([the external view](cockpit-views.md#the-external-view)) with the rect (0, 0)–(319, 110), the screen above the panel, and the projection pair (−160, −50): the center half way across and 50 rows down. The rect's bottom is the panel's *y0* − 1. In view 4 the installed render context clips in mode 2 through its copy of `ExternalView_ClipBlock`, whose one entry points at `ExternalView_ClipRegion` ([`CockpitView_ApplyViewState`](cockpit-views.md#cockpitview_applyviewstate-000222c0)), so the rect `ExternalView_SetRect` writes into that region is the clip too, besides the six values it writes into the context.
5. **In view 4**, the external view, it attaches the main camera to the player's machine (`Cam_AttachTo`(`LocalPlayerMech`)), which zeroes the orbit's angles, rates and distance ([the camera](external-views.md#the-camera)), sets the camera's mode `+0x36` to 1, the orbit, itself, and draws one frame (`Cam_Update`, `Sim_RenderFrame`), with no `Sim_EndFrame`. **In any other view** it first draws frames (`Sim_EndFrame`, `Cam_Update`, `Sim_RenderFrame`) while a view change is staged or a view command is pending (the published view manager's `+0x18` set or `+0x14` not −1): while `+0x18` is set, then again for as long as `+0x14` is not −1, at least one frame each time; keeps the current view `+0x10`; queues view command 2, the external view (`CockpitViewManager_QueueViewCommand`, [switching views](cockpit-views.md#switching-views)); and draws frames the same way until `+0x18` has been seen set and is clear again. The first frame whose `Sim_EndFrame` leaves `+0x18` set attaches the camera as above, between that `Sim_EndFrame` and its `Cam_Update`. Staging it installs the rect and fills the rest of the screen, the panel's strip, with color `0x3f` ([`CockpitView_ApplyViewState`](cockpit-views.md#cockpitview_applyviewstate-000222c0)); running it hides the cursor and clears `Cursor_FollowsMouse` ([the view's step](cockpit-views.md#cockpitview_stepviewtransition-00022904)).
6. Installs `PreferencesPanel_OrbitViewHook` as the panel's draw hook (`PreferencesPanel_SetDrawHook`, `00048180`), runs the panel (slot `+0xc`, [the loop](#the-loop--preferencespanel_run-00047a88)) and destroys it (`PreferencesPanel_Dtor`, 3).
7. Makes the display's (`000bdd28`) `+0x8c` the draw page (slot 17, `0009df18`).
8. Copies the main camera back from its stack copy and puts the default rect back (`ExternalView_ResetRect`).
9. **Back to view 4**: `View_FillOutside3dRect`, `ViewChain_DrawCaption` ([the views](main-loop.md#the-views--viewchain_apply-0005894c)), a frame (`Cam_Update`, `Sim_RenderFrame`, `Sim_EndFrame`), and the fill and the caption again, so both pages get the border and the caption. **Back to another view**: `Cam_Update`; `CockpitView_SetView` with the kept view, which leaves view 4 and blits the canopy ([`CockpitView_SetView`](cockpit-views.md#cockpitview_setview-00022558)); `Cockpit_SkipFrameDraw` (`00092d7c`) set for one `Sim_RenderFrame` and `Sim_EndFrame`, so the cockpit's scripts do not draw on that frame ([drawing it](cockpit-panel.md#drawing-it)); `CockpitView_SetView` again, on the other page; `Cockpit_SkipFrameDraw` cleared; and `Sim_RenderFrame`, which the main loop's next `Sim_EndFrame` shows.
10. Sets `Cursor_FollowsMouse` (`00094c5b`), puts `Sim_Frozen` back and destroys its camera in place (the vtable `00096c6a`, `TSContext_Dtor`).

**The pointer comes back with the cockpit's next instrument redraw.** The panel's `AlertPanel_Leave` puts `Cursor_FollowsMouse` back to the 0 the external view left and so leaves the cursor hidden ([putting a panel up](alert-panels.md#putting-a-panel-up-and-taking-it-down)), and neither step 9 nor step 10 shows it. `VGA8_ShowPage`, `Cursor_SyncPosition` and `Cursor_SetImage` show the cursor only when it was shown already ([the cursor](../rendering/screen-pages.md#the-cursor)). `Widget_DrawToCockpit` ([drawing it](cockpit-panel.md#drawing-it)) hides and shows it around every copy it makes, whether or not it was shown, and `VGA8_ShowCursor` draws a cursor that has a bitmap and is not shown, so the first instrument that copies its rect to the screen after the return shows the pointer again. The clock is one: `Cockpit_DrawFrame`, which every `Sim_RenderFrame` runs unless `Cockpit_SkipFrameDraw` is set, first calls `CockpitClock_Tick` (`000435c0`), which copies the clock's rect with `Widget_DrawToCockpit` on the frame after each advance (every 30 counts of `000981c4`, [the cockpit clock](../formats/ttm-scripts.md#the-cockpit-clock)) and while a view change is staged, the copy refused until the change has run. Back in a cockpit view the widgets are on again and no change is staged, so the pointer is drawn again by the next frame after the clock's next advance, or sooner when an instrument `CockpitView_SetView` invalidated copies first ([Open](#open)). `MessagePort_CopyBoxBetweenPages` and `PilotMessagePort_Show`, a radio line's box, show it as well outside view 4.

**`PreferencesPanel_OrbitViewHook` (`000577f8`)**, no arguments, frameless, with no Ghidra function: `Cam_Steer`(`ViewObjectPtr`, 0, 0, `0xffc0`), `Cam_Update`(`ViewObjectPtr`), `Sim_RenderFrame`. It is ES2's hook ([ES2's](../../retail/simulation/preferences.md#preferences-and-controls-dbsimexe)), its `Cam_Steer` arguments in reverse order: the heading rate approaches −`0x200` a pass and the pitch and zoom rates 0, so the view turns steadily round the machine at the least orbit distance, 2500 ([the camera](external-views.md#cam_update)). The panel's loop calls it once a pass, and each pass waits for a vertical retrace ([the loop](#the-loop--preferencespanel_run-00047a88)), so the turn is a step a retrace when the frame draws within one.

## The preferences panel — `PreferencesPanel_Ctor` (`00047578`)

**A strip 320 pixels wide and 89 high along the bottom of the screen: four settings with their values in words on the left, three with bars on the right, CONTROLS and DONE under the bars, and the title PREFERENCES at the top.** `PreferencesPanel_Ctor`, (panel, *keepsScreen*), allocates `0x38f` bytes when given none, returning 0 when that fails. `PreferencesPanel_Raise` passes (0, 0), so the panel saves no screen under itself. In order:

1. The rect (0, 111)–(319, 200), `AlertPanel_SetRect`(89, 319, 111, 0), given outright, as ES2's is; `AlertPanel_CtorBase`(panel, the rect, *keepsScreen*) ([the panel object](alert-panels.md#the-panel-object--alertpanel_ctorbase-00045988)); `PrefsAlertVtable` (`000951ae`) at `+0x314`.
2. The plate: frame 0 of `dba\prf_alrt`, loaded with `ClassItem_LoadFromFile`, at `+0x2c`. The array holds one 320 × 89 frame.
3. The text: `str\prf_alrt` (`str\PRF_ALRT.STR`) into the base's string table `+0x30`, and its four groups: the title `PREFERENCES` to `+0x348`; the nine captions `MUSIC`, `SOUNDS`, `RADIO`, `HORIZON`, `CONTROLS`, `DONE`, `SKY`, `GROUND`, `SHAPES` to `+0x34c`, indexed by the button; `OFF` and `ON` to `+0x370`; `TEXT ONLY`, `VOICE ONLY` and `TEXT / VOICE` to `+0x378`.
4. The nine buttons, `PanelButton_Ctor`(0, index, panel, rect) ([the panel's buttons](alert-panels.md#the-panels-buttons)), on the rects of the `int16` tables `0009510c` (*x*), `00095120` (*y*), `00095134` (width) and `00095148` (height), each with a caption label in `WidgetFont_Cpylw` centered by `Label_SetRect` with flags 2 and margin (0, −1). Each is registered with the panel's root and added to the widget list, CONTROLS and DONE after the other seven, so the widget order, which [Tab] walks, is MUSIC, SOUNDS, RADIO, HORIZON, SKY, GROUND, SHAPES, CONTROLS, DONE.
5. The title: a label in `WidgetFont_Title` at `+0x40` on the title bar `+0x2ba`, ((319 − *w* − 20) >> 1, 4)–(that + *w* + 20, 21), *w* the title's width, margin (0, −11), as the status panel's ([the status panel](alert-panels.md#the-status-panel--statusalertpanel_ctor-00046efc)).
6. Four readouts at `+0x318`: labels in `WidgetFont_Green6x8`, placed with flags 1, left-aligned, and margin (2, −1), their background color `+0x1c` `0x3f`, on the tables `0009515c` to `00095174`.
7. Three bars at `+0x334`: LED bar graphs of range `0xff` (`LedBarGraph_Ctor`, [bar graphs](cockpit-widgets.md#bar-graphs)), their unlit color `+0x20` `0x3f` and shades `+0x2c` and `+0x30` 50 and 49, on the tables `0009517c` to `00095194`.
8. The cancel widget `+0x2e3`, which [Esc] presses, is the last, DONE; the draw hook `+0x384` and the seven repaint counters `+0x388`–`+0x38e` are cleared.

In panel coordinates, which the screen has 111 rows lower:

| Widget | Button | Caption | Shows | On |
|---|---|---|---|---|
| 0 | 0 | `MUSIC` | `OFF` or `ON`, option 0 | (21, 32)–(81, 42); the readout (85, 32)–(145, 42) |
| 1 | 1 | `SOUNDS` | `OFF` or `ON`, option 1 | (21, 44)–(81, 54); (85, 44)–(145, 54) |
| 2 | 2 | `RADIO` | `TEXT ONLY`, `VOICE ONLY` or `TEXT / VOICE`, option 2 | (21, 56)–(81, 66); (85, 56)–(145, 66) |
| 3 | 3 | `HORIZON` | `OFF` or `ON`, option 3 | (21, 68)–(81, 78); (85, 68)–(145, 78) |
| 4 | 6 | `SKY` | a bar, option 4 | (158, 31)–(220, 41); the bar (227, 32)–(298, 39) |
| 5 | 7 | `GROUND` | a bar, option 5 | (158, 43)–(220, 53); (227, 44)–(298, 51) |
| 6 | 8 | `SHAPES` | a bar, option 6 | (158, 55)–(220, 65); (227, 56)–(298, 63) |
| 7 | 4 | `CONTROLS` | | (155, 70)–(227, 80) |
| 8 | 5 | `DONE` | | (228, 70)–(300, 80) |

`PrefsAlertVtable` holds `PreferencesPanel_Paint` (`00047e90`) at `+4`, `AlertPanel_SetTitleText` at `+8`, `PreferencesPanel_Run` (`00047a88`) at `+0xc`, `PreferencesPanel_OnClick` (`000480f4`) at `+0x10`, `PreferencesPanel_HandleEvent` (`00048078`) at `+0x14` and `PreferencesPanel_GetButtonCaption` (`00048168`) at `+0x18`, all but the run by their padding entries; `+0` and `+0x1c` are 0.

- **`PreferencesPanel_Paint` (`00047e90`)**, (panel): blits the plate with flags 0 at the panel's corner; sets the title through slot `+8`; runs each widget's paint; then refreshes rows 0 to 6 ([its rows](#its-rows)).
- **`PreferencesPanel_GetButtonCaption` (`00048168`)**, (panel, index): the caption `+0x34c`[index], by the button's index.
- **`PreferencesPanel_Dtor` (`00047a14`)**, (panel, flags): stores `PrefsAlertVtable`, frees the four readouts and the three bars, runs `Alert_Dtor`(panel, 0) and frees the panel when bit 0 of the flags is set ([destroying a panel](alert-panels.md#destroying-a-panel--alert_dtor-00045d0c)).

| | ES1 | ES2's |
|---|---|---|
| the rows | MUSIC, SOUNDS, RADIO, HORIZON, SKY, GROUND, SHAPES | [nine](../../retail/simulation/preferences.md#the-preferences-panel--prf_alrt-004566c4): MUSIC, SOUNDS, two message rows and five detail rows |
| a row's value | a word for the first four, a bar for the last three | a word for each |
| the size | 320 × 89 at (0, 111) | 638 × 178 at (0, 300) in device pixels |
| CONTROLS | chooses the stick and its add-ons ([the controls panel](#the-controls-panel--controlspanel_ctor-00048408)) | binds the stick's axes and buttons |

### Its rows

**`PreferencesPanel_RefreshRow` (`00047f84`)**, (panel, row), entered at its padding `00047f82`, sets one row's readout from its option byte in `Prefs_Options` (`000bd562`, [the option array](#the-option-array)): rows 0, 1 and 3 put `OFF` or `ON` (`+0x370`[option]) on readouts 0, 1 and 3, row 2 the message word (`+0x378`[option 2]) on readout 2, and rows 4, 5 and 6 hand option 4, 5 or 6 to their bar's set-value slot (`+8` of its table at `+0x28`), with 0 first. Row *n* is option *n*. ES2's reads its detail rows through maps of five words ([what a row reads](../../retail/simulation/preferences.md#what-a-row-reads--preferencespanel_refreshrow-004571f4)).

| Option | Row | Values | What reads it |
|---|---|---|---|
| 0 | `MUSIC` | 0 off, 1 on | its handler, `Prefs_ApplyMusicOption` ([below](#the-option-array)), and `Music_Tick` ([the music](main-loop.md#the-music--music_tick-000585dc)) |
| 1 | `SOUNDS` | 0 off, 1 on | its handler, `Prefs_ApplySoundsOption` |
| 2 | `RADIO` | 0 `TEXT ONLY`, 1 `VOICE ONLY`, 2 `TEXT / VOICE` | its handler, `Prefs_ApplyRadioOption`: the voice lines' load gate (`Sound_SpeechEnabled`, `000959e4`, [speech lines](../formats/sound-samples.md)) is on for 1 and 2, and the message boxes' show (`MessagePort_ShowEnabled`, [the lifecycle](cockpit-messages.md#the-lifecycle)) for 0 and 2 |
| 3 | `HORIZON` | 0 off, 1 on | `Hzline_DrawWithOffset`, which draws the horizon's skyline only while it is on ([the horizon skyline](../rendering/ground-and-sky.md#the-horizon-skyline)) |
| 4 | `SKY` | 0 to 255 | `Clouds_DriftAndDraw`, which draws (count · (value + 1)) >> 8 of the clouds ([each frame](../rendering/ground-and-sky.md#each-frame)) |
| 5 | `GROUND` | 0 to 255 | the ground, textured only above 240 ([the ground](../rendering/ground-and-sky.md#the-ground)), and the scattered points below it ([the scattered points](../rendering/ground-and-sky.md#the-scattered-points)) |
| 6 | `SHAPES` | 0 to 255 | the detail setting, how near shapes switch to their crude models ([the detail setting](object-classes.md#the-detail-setting--shapedetail_applydetailsetting-00057c08)), which `Sim_RenderFrame` reapplies when the byte changes |

The readers of options 3 to 6 read the byte every frame, so the outside view behind the panel shows a change on the next pass.

### The loop — `PreferencesPanel_Run` (`00047a88`)

**The panel's loop reads the controls, acts on the button pressed, then draws a frame of the outside view on the hidden page and flips it, once a vertical retrace; DONE, or [Esc], saves and closes, and there is no cancel.** `PreferencesPanel_Run`, (panel), slot `+0xc`, ES2's ([what a click does](../../retail/simulation/preferences.md#what-a-click-does--preferencespanel_run-00456d4c)) with ES1's rows:

1. With no sound manager (`Sound_Manager` (`000959ac`), [the sound manager](../formats/sound-samples.md)) it puts MUSIC, SOUNDS and RADIO, widgets 0 to 2, in state 2, which the mouse's hit test and `AlertPanel_PressWidget` pass over ([press, release and drag](cockpit-mouse.md#press-release-and-drag), [keys](alert-panels.md#keys--alertpanel_handleevent-00046670)); ES2's greys its first four rows.
2. Clears the action `+0x340` (−1); `AlertPanel_Enter`, which draws the panel on the page on the screen ([putting a panel up](alert-panels.md#putting-a-panel-up-and-taking-it-down)); `AlertPanel_SetFocus`(panel, 0), MUSIC.
3. Copies the panel's region from the page on the screen to the display's `+0x8c` at the same place (`Region_CopyBetweenPages`, the cursor hidden around it), so both pages hold it, and makes `+0x88` the draw page again.
4. Until the close byte `+0x2b9` is set: `Input_ReadPlayerInput` and the block to slot `+0x14`, `PreferencesPanel_HandleEvent`; a click on a button, through `PanelButton_OnClick`, reaches slot `+0x10`, `PreferencesPanel_OnClick`, which stores the widget's index at `+0x340` and bit 1 of the click's flags, the right button, at `+0x2e7` ([the panel's buttons](alert-panels.md#the-panels-buttons)). Then, for an action other than −1:

   | Action | Does |
   |---|---|
   | 0 `MUSIC`, 1 `SOUNDS`, 3 `HORIZON` | toggles its option, `Prefs_ToggleOption`(1, option) |
   | 2 `RADIO` | steps option 2 forward, `Prefs_StepOption`(1, 3, 2), only while `VoiceArchivePresent` (`000956bf`) is set |
   | 4 `SKY`, 5 `GROUND`, 6 `SHAPES` | `Prefs_IncreaseOption`(1, `0x20`, option 4, 5 or 6), or `Prefs_DecreaseOption` with the right-button byte set ([the option array](#the-option-array)) |
   | 7 `CONTROLS` | paints CONTROLS at once through its slot `+4`, builds the controls panel on the stack (`ControlsPanel_Ctor`(that, 0, 1)), runs it ([its loop](#its-loop--controlspanel_run-00048810)), repaints this panel through slot `+4`, focuses the cancel widget, DONE, and destroys the controls panel (`ControlsPanel_Dtor`, 2) |
   | 8 `DONE` | `PreferencesPanel_Save` ([saving](#saving)), entered at its padding `0004839d`, and the close byte 3 |

   Cases 0, 1 and 3 to 6, and case 2 when it steps, set their row's repaint counter `+0x388` + row to 2. The action goes back to −1, and `PreferencesPanel_CallDrawHook` (`00048198`, entered at its padding `00048195`) runs.
5. `AlertPanel_Leave`. With a draw hook and a screen buffer, which `PreferencesPanel_Raise`'s panel does not have, it then copies the region onto `+0x8c` under the panel's render context and puts the draw page back.
6. Returns bit 0 of the close byte, 1 for DONE.

**`PreferencesPanel_CallDrawHook` (`00048198`)**, (panel), does nothing without a hook `+0x384`; with one, it is ES2's call of the hook ([ES2's](../../retail/simulation/preferences.md#preferences-and-controls-dbsimexe)) with the present of ES2's loop inside it:

1. Makes `+0x8c`, the page not on the screen, the draw page; installs the render context `AlertPanel_Enter` found (`+0x27d`), calls the hook, which draws a frame of the outside view there, and installs the panel's (`+0x44`) again.
2. With a screen buffer `+0x28`, makes `+0x8c` the source page and saves the region, its rect moved by the panel's corner for the call, into the buffer (`Region_Save`).
3. Makes `+0x88` the source page, and runs **`PreferencesPanel_Tick` (`00047f14`)**, (panel), on the draw page `+0x8c`: each widget's per-frame slot `+0x10`, which paints a button whose paint counter is set ([`PanelButton_Tick`](alert-panels.md#the-panels-buttons)), then the first row whose repaint counter is not 0, refreshed and its counter taken down by 1.
4. Shows `+0x8c` (slot 18), which waits for the next vertical retrace while `Display_FlipWithoutPan` is 0; draws on `+0x88` (slot 17); swaps the two; and makes the new `+0x88`, the page now on the screen, the source and draw page.

So between passes the panel draws on the page on the screen, which is where a mouse press repaints a button, and each pass draws the next frame and the counted repaints on the other page and shows it. A counter of 2 paints a button or a row once on each page. The pass makes no `Sim_EndFrame`: no palette cycling, no view command and no view step run while the panel is up. ES2's loop presents with `Display_Present` and services the press flashes ([keys and the press flash](../../retail/simulation/alert-panels.md#keys-and-the-press-flash)).

**Keys — `PreferencesPanel_HandleEvent` (`00048078`)**, (panel, block), slot `+0x14`: `AlertPanel_HandleEvent` first ([keys](alert-panels.md#keys--alertpanel_handleevent-00046670)), answering 1 when it acts. Otherwise it looks the command word up in the four dwords at `00095224`: `0x0d` [=] and `0x4e` keypad [+] press the focused widget with flags 1, and `0x0c` [-] and `0x4a` keypad [-] with flags 2, both through `AlertPanel_PressWidget` and only while the focus `+0x2df` is SKY, GROUND or SHAPES, widgets 4 to 6; any of the four answers 1, any other code 0. So [=] raises the focused bar and [-] lowers it, and [Return] and the trigger, which press with flags 1, raise it. ES2's panel uses `AlertPanel_HandleEvent` alone.

A press of any button moves the focus to it, and the pointer to its center ([the panel's buttons](alert-panels.md#the-panels-buttons)); ES2's click moves the focus and also a highlight ([what a click does](../../retail/simulation/preferences.md#what-a-click-does--preferencespanel_run-00456d4c)).

### Saving

**`PreferencesPanel_Save` (`000483a0`)**, (panel), its argument unread: when any of options 0 to 6, the seven `int16` indices of `PreferencesPanel_SaveList` (`0009519c`), differs from its load-time copy (`Prefs_OptionChanged`), writes those seven to `data\prefs.cfg` with `Prefs_SaveSelectedOptions` ([the option array](#the-option-array)). It does not commit: the load-time copy stays as it was. ES2's saves nine options ([writing it back](../../retail/simulation/preferences.md#writing-it-back--prefs_saveselectedoptions-00459b78)). **`PreferencesPanel_Revert` (`000483d4`)**, (panel), its argument unread, makes the same test and copies the seven back from the load-time copy (`Prefs_RevertSelectedOptions`); `es2_xref.py` finds no reference to it.

The controls panel's DONE commits every option (`Prefs_CommitOptions`, [its loop](#its-loop--controlspanel_run-00048810)), which makes each option's load-time copy its current value. So a change made on the preferences panel before CONTROLS is opened and closed no longer differs when DONE is pressed, and unless a later change on the panel makes the test pass, `PreferencesPanel_Save` writes nothing: the change holds for the rest of the mission, and the next mission start loads the file's value ([the preferences start-up](input.md#the-preferences-start-up)). A retail check is in [Open](#open).

## The controls panel — `ControlsPanel_Ctor` (`00048408`)

**CONTROLS opens a panel the size of the pause prompt over the middle of the screen: which joystick is fitted, whether the stick drives the HERC or the turret, whether a Thrustmaster WCS throttle or RCS rudder is fitted, whether the mouse is on, a CALIBRATE button and DONE.** It is ES2's controls panel ([ES2's](../../retail/simulation/preferences.md#the-controls-panel--ctl_alrt-00457d1c)) in the earlier design ES2's v1.0 French and German tables translate, with ES1's options 7 to 11 and no button bindings. `ControlsPanel_Ctor`, (panel, rect, *keepsScreen*), allocates `0x3a6` bytes when given none, returning 0 when that fails; `PreferencesPanel_Run` passes (a stack object, 0, 1). In order:

1. The rect: 222 × 109, centered by `AlertPanel_CenterRect`(109, 222) at (48, 45), or with a rect given, `AlertPanel_SetRect` at its point; `AlertPanel_CtorBase`(panel, the rect, *keepsScreen*); `ControlsAlertVtable` (`000952c6`) at `+0x314`.
2. The plate: frame 0 of `dba\ctl_alrt`, one 224 × 107 frame, at `+0x2c`. The panel's rect covers 223 columns, edges included, so the plate's last column is cut ([drawing on a panel](alert-panels.md#the-panel-object--alertpanel_ctorbase-00045988)).
3. The text: `str\ctl_alrt`, `str\CTL_ALRT.STR`, and its six groups: the title `CONTROLS` to `+0x318`; the seven captions `JOYSTICK`, `JOY1 CONTROL`, `WCS`, `RCS`, `MOUSE`, `CALIBRATE`, `DONE` to `+0x31c`; `NO` and `YES` to `+0x338`; `OFF` and `ON` to `+0x340`; ten stick models to `+0x348`, `NONE`, `1 STANDARD JOYSTICK`, `2 STANDARD JOYSTICKS`, `THRUSTMASTER FCS`, `FLIGHTSTICK STD`, `FLIGHTSTICK PRO` and four empty strings; and five stick controls to `+0x370`, `NO JOYSTICK`, `MOVEMENT CONTROL`, `TURRET CONTROL` and two empty strings.
4. The seven buttons, in widget order, on the `int16` tables `00095244` (*x*), `00095254` (*y*), `00095264` (width) and `00095274` (height), each with a caption label in `WidgetFont_Cpylw` centered with margin (0, −2), the word of the table `00095284`.
5. The title as the preferences panel's, on (222 − *w* − 20) >> 1.
6. Five readouts at `+0x384`: labels in `WidgetFont_Green6x8`, flags 1 and margin (2, −2), background `0x3f`, on the tables `00095294` to `000952b8`.
7. The action `+0x398` cleared; the cancel widget `+0x2e3` the last, DONE.

In panel coordinates, which the screen has 48 columns right and 45 rows lower:

| Widget | Caption | Option | Shows | On |
|---|---|---|---|---|
| 0 | `JOYSTICK` | 7, the stick model ([building the devices](joystick.md#building-the-devices)) | the model's name | (20, 29)–(80, 39); the readout (84, 29)–(200, 39) |
| 1 | `JOY1 CONTROL` | 8, the stick mode ([the stick mode](input.md#the-stick-mode)) | `NO JOYSTICK`, `MOVEMENT CONTROL` or `TURRET CONTROL` | (20, 43)–(80, 53); (84, 43)–(200, 53) |
| 2 | `WCS` | 10, the throttle keys ([the axis keys](input.md#the-axis-keys--input_keyjoyaxiskey-0004c6ec), [the throttle](mech-locomotion.md#the-throttle--mech_applythrottleinput-00011afc)) | `NO` or `YES` | (20, 60)–(80, 70); (84, 60)–(106, 70) |
| 3 | `RCS` | 9, the rudder ([building the devices](joystick.md#building-the-devices)) | `NO` or `YES` | (20, 74)–(80, 84); (84, 74)–(106, 84) |
| 4 | `MOUSE` | 11 | `OFF` or `ON` | (113, 60)–(173, 70); (177, 60)–(200, 70) |
| 5 | `CALIBRATE` | | | (39, 93)–(99, 103) |
| 6 | `DONE` | | | (122, 93)–(182, 103) |

`ControlsAlertVtable` holds `ControlsPanel_Paint` (`00048c58`) at `+4`, `AlertPanel_SetTitleText` at `+8`, `ControlsPanel_Run` (`00048810`) at `+0xc`, `ControlsPanel_OnClick` (`00048db0`) at `+0x10`, `AlertPanel_HandleEvent` at `+0x14` and `ControlsPanel_GetButtonCaption` (`00048e80`) at `+0x18`, all but the keys by their padding entries; `+0` and `+0x1c` are 0. So the controls panel takes the family's keys alone ([keys](alert-panels.md#keys--alertpanel_handleevent-00046670)).

- **`ControlsPanel_Paint` (`00048c58`)**, (panel): the plate, the title, each widget's paint, then rows 1 to 5.
- **`ControlsPanel_RefreshRow` (`00048ce4`)**, (panel, row), between the cursor's hide and show: row 1 puts the stick model `+0x348`[`+0x39c`] on readout 0, row 2 the stick control `+0x370`[`+0x39d`] on readout 1, rows 3 and 4 `NO` or `YES` by `+0x39e` and `+0x39f` on readouts 2 and 3, row 5 `OFF` or `ON` by `+0x3a0` on readout 4. The bytes are the panel's copies of options 7, 8, 10, 9 and 11.
- **`ControlsPanel_OnClick` (`00048db0`)**, (panel, flags, child): stores bit 1 of the flags at `+0x2e7` and the widget's index plus 1 at the action `+0x398`, and for DONE also sets the close byte to 3.
- **`ControlsPanel_GetButtonCaption` (`00048e80`)**, (panel, index): `+0x31c`[index].
- **`ControlsPanel_Dtor` (`000487b4`)**, (panel, flags): stores `ControlsAlertVtable`, frees the five readouts, `Alert_Dtor`(panel, 0), and frees the panel for bit 0 of the flags.

### Its loop — `ControlsPanel_Run` (`00048810`)

**Each click changes one setting at once, the joystick's choices keep each other consistent, and DONE or [Esc] rebuilds the joystick devices, saves the five settings and closes; nothing moves behind the panel while it is up.** `ControlsPanel_Run`, (panel), slot `+0xc`:

1. Copies options 7, 8, 10, 9 and 11 to `+0x39c` to `+0x3a0`, and keeps: `+0x3a1` 2 when option 7 is 2 and 0 otherwise; `+0x3a2` option 8, or 1 when that is 0; `+0x3a3` option 9; and `+0x3a4` and `+0x3a5`, options 7 and 9 as last applied to the devices.
2. `AlertPanel_Enter`, `AlertPanel_SetFocus`(panel, 0), JOYSTICK.
3. Until the close byte is set: `Input_ReadPlayerInput` and the block to `AlertPanel_HandleEvent`; for an action other than 0, the case below, each row it changes redrawn with `ControlsPanel_RefreshRow`; the action back to 0; then each widget's per-frame slot `+0x10`.
4. `AlertPanel_Leave`; returns bit 0 of the close byte, 1.

The loop calls no draw hook, so the outside view stands still under it, and it draws on the page on the screen alone; the panel keeps the screen under itself and puts it back on leaving ([putting a panel up](alert-panels.md#putting-a-panel-up-and-taking-it-down)).

| Action | Button | Does |
|---|---|---|
| 1 | `JOYSTICK` | Steps option 7 over its six models, forward (`Prefs_StepOption`(1, 6, 7)) or with the right button back (`Prefs_StepOptionBack`), and once more forward when that gives `NONE` while the block's trigger (`+0x15`) is held, so a stick's fire button cannot choose `NONE`. With `NONE` and the panel's option 8 `+0x39d` not 0, it keeps that at `+0x3a2` and sets option 8 to 0, `NO JOYSTICK`, and leaves the RCS row alone. With a stick and option 8 at 0 it puts option 8 back from `+0x3a2`; then with `2 STANDARD JOYSTICKS` and the RCS row's byte `+0x39f` set, it keeps that at `+0x3a3` and XORs the byte with 1, clearing it, and with any other stick sets the byte from `+0x3a3` when that is set. The RCS byte here is the panel's copy: option 9 itself is cleared when the devices are rebuilt ([below](#rebuilding-the-joystick-devices--controlspanel_applydevices-00048e98)). |
| 2 | `JOY1 CONTROL` | With a stick model, steps option 8 forward or back modulo 3, again while it is 0, so it alternates between `MOVEMENT CONTROL` and `TURRET CONTROL`; with `NONE`, nothing. |
| 3 | `WCS` | Toggles option 10. |
| 4 | `RCS` | Toggles option 9 and keeps it at `+0x3a3`. Turned on with `2 STANDARD JOYSTICKS`, it sets option 7 to `1 STANDARD JOYSTICK` and `+0x3a1` to 2; turned off with `+0x3a1` at 2, it sets option 7 back to `2 STANDARD JOYSTICKS` and `+0x3a1` to 0. The rudder reads the port's second stick axis, which the second stick needs ([the classes](joystick.md#the-classes)). |
| 5 | `MOUSE` | Toggles option 11, whose handler turns the mouse driver off or on at once ([the option array](#the-option-array)). |
| 6 | `CALIBRATE` | With option 9 set or a stick model chosen: `ControlsPanel_ApplyDevices`; builds the calibration panel on the stack, `00049154`(that, this panel, 1), runs its loop `000496f0`, focuses the cancel widget, DONE, and destroys it with `00049678`(that, 2). The calibration panel is [Open](#open). |
| 7 | `DONE` | `ControlsPanel_ApplyDevices`, then `Prefs_CommitOptions`(1), which runs the handler of each option that differs from its load-time copy and makes the copies current ([the option array](#the-option-array)). |

Every write passes apply 1, so each change has run its option's handler already, and the commit runs a changed option's handler a second time, as ES2's does ([writing it back](../../retail/simulation/preferences.md#writing-it-back--prefs_saveselectedoptions-00459b78)).

### Rebuilding the joystick devices — `ControlsPanel_ApplyDevices` (`00048e98`)

**A new stick model or rudder choice takes effect when the controls panel closes, or when CALIBRATE is pressed: the old devices are destroyed and the new ones built and calibrated from their files at once.** (panel):

1. When the panel's option 7 (`+0x39c`) or option 9 (`+0x39f`) differs from the copy last applied (`+0x3a4`, `+0x3a5`): destroys the first and second joystick devices (`Input_GetJoystickDevice_Thunk`, `0004c63c`, and `Input_GetJoystick2Device_Thunk`, `0004c644`, each calling its getter ([the input devices](input.md#the-input-devices)); `JoystickDevice_Dtor` with 3); builds them for option 7 and option 9 themselves (`000bd569`, `000bd56b`), not the panel's copies, as `Input_CreateJoystickDevices` does ([building the devices](joystick.md#building-the-devices)), clearing option 9 with option 7 at 2; loads `data\joy1.cal` (through `Path_ApplyAltDrive`) into the first and `joy2.cal` (`00095360`) as given into the second (`Joystick_LoadCalibration`), so the second device's file is opened in the current directory, where `Input_CreateJoystickDevices` opens `data\joy2.cal` through `Path_ApplyAltDrive`, and enables each (`InputDevice_Enable` on `+0x78`); and stores the applied copy.
2. When option 11 differs from the panel's `+0x3a0`, with a mouse device, enables or disables it by `+0x3a0`. Action 5 sets both from one toggle, so this finds them equal.
3. Writes options 7, 8, 10, 9 and 11, the five `int16` indices of `ControlsPanel_SaveList` (`00095348`), to `data\prefs.cfg` (`Prefs_SaveSelectedOptions`), whether or not they changed.

The pointers to the destroyed devices stay in the registers that held them. Where step 1 builds no new first device, for `NONE`, the destroyed first device, when there was one, is the one it loads and enables; where it builds no new second device, for any model but `2 STANDARD JOYSTICKS` with option 9 clear, the destroyed second device, when there was one, is. So choosing `NONE` re-enables the freed stick device, which `Input_PollDeviceBlock` then polls each tick ([the live block](input.md#the-live-block--input_polldeviceblock-0004c2d0)); the next mission start builds the devices afresh ([the preferences start-up](input.md#the-preferences-start-up)). What the freed device reads is [Open](#open).

**`ControlsPanel_Revert` (`0004903c`)**, (panel): `Prefs_RevertOption`(1, option) for options 7, 8, 10, 9 and 11; copies option 7 to `+0x39c` and to `+0x39d`, where `ControlsPanel_Run` copies option 8, option 10 to `+0x39e`, 9 to `+0x39f` and 11 to `+0x3a0`; then `ControlsPanel_ApplyDevices`. `es2_xref.py` finds no reference to it.

## The option array

ES1's simulator keeps 17 options, `Prefs_Options` (`000bd562`), byte *n* option *n*, read from the 17 bytes of `data\prefs.cfg` by `Prefs_Load` ([the preferences start-up](input.md#the-preferences-start-up)), with ES2's two copies ([ES2's](../../retail/simulation/preferences.md#dataprefscfg--the-option-array)): `Prefs_LoadedOptions` (`000bd574`), the values as loaded or last committed, and `Prefs_PreviousOptions` (`000bd586`), each value before its last write. The shell keeps its own 17 options in the same file ([the shell's options](../formats/msn-mission-file.md#the-mode-and-the-mission-parameters)).

| Option | Byte | Set by | Handler |
|---|---|---|---|
| 0 | `000bd562` | `MUSIC` | `Prefs_ApplyMusicOption` (`0004ad0c`): `Sound_SetMusicEnabled` (`0004e294`)(value), then `Music_Tick` when the value is not 0 and `PrefsInitInProgress` (`000b1734`) is clear |
| 1 | `000bd563` | `SOUNDS` | `Prefs_ApplySoundsOption` (`0004acfc`): `Sound_SetEffectsEnabled` (`0004e2c8`)(value) |
| 2 | `000bd564` | `RADIO` | `Prefs_ApplyRadioOption` (`0004ad30`), frameless, with no Ghidra function: `Sound_SpeechEnabled` (`000959e4`) = (value ≠ 0), `MessagePort_ShowEnabled` (`000930d2`) = (value ≠ 1) |
| 3 to 5 | `000bd565` to `000bd567` | `HORIZON`, `SKY`, `GROUND` | none |
| 6 | `000bd568` | `SHAPES` | `Prefs_LimitDetailInLowMemory` ([the detail setting](object-classes.md#the-detail-setting--shapedetail_applydetailsetting-00057c08)) |
| 7 | `000bd569` | `JOYSTICK` | none |
| 8 | `000bd56a` | `JOY1 CONTROL` | `Input_SetStickMode` ([the stick mode](input.md#the-stick-mode)) |
| 9 | `000bd56b` | `RCS` | none |
| 10 | `000bd56c` | `WCS` | `Input_SetThrottleLeverMode` ([the throttle](mech-locomotion.md#the-throttle--mech_applythrottleinput-00011afc)) |
| 11 | `000bd56d` | `MOUSE` | `Prefs_ApplyMouseOption` (`0004ad54`): when the value differs from `Prefs_MouseOptionApplied` (`000956c0`), keeps it there and, with a mouse device, turns the mouse driver on (`Mouse_EnableDriver`, `INT 33h` function `0x20`) or off (`Mouse_DisableDriver`, function `0x1f`) ([the mouse driver](input.md#the-mouse-driver)). `Prefs_MouseOptionApplied` is `0xff` in the image, so the first call after the program starts applies whatever the value: `Prefs_Load` runs every handler with the byte it read, after `Input_BuildSourceTable` has made the mouse device ([setting up](input.md#setting-up--input_buildsourcetable-0004c95c)), so option 11 at 0 turns the driver off at the mission start, and a `data\prefs.cfg` that does not open, which `Prefs_Load` reads as 17 zero bytes, does the same |
| 12 to 16 | `000bd56e` to `000bd572` | the shell's MISSION PARAMS ([the parameters](../formats/msn-mission-file.md#the-mode-and-the-mission-parameters)) | none |

The functions the panels call, on the 17 bytes ([Functions](#functions) names ES2's counterparts):

Each first copies the option to `Prefs_PreviousOptions` and writes through `Prefs_SetOption` (`0004a914`), (apply, value, index), which runs the option's handler when apply is set.

- **`Prefs_ToggleOption` (`0004a94c`)**, (apply, index): the option XOR 1, which it returns.
- **`Prefs_StepOption` (`0004a988`)**, (apply, modulus, index): one more, 0 when that equals `modulus`.
- **`Prefs_StepOptionBack` (`0004a9d0`)**, (apply, modulus, index): one less, `modulus` − 1 from 0.
- **`Prefs_UndoSetOption` (`0004a900`)**, (index), frameless: the option back from `Prefs_PreviousOptions`, with no handler; ES2's (`00459924`). `Prefs_LimitDetailInLowMemory` calls it ([the detail setting](object-classes.md#the-detail-setting--shapedetail_applydetailsetting-00057c08)).
- **`Prefs_IncreaseOption` (`0004aa18`)** and **`Prefs_DecreaseOption` (`0004aa78`)**, (apply, amount, index): up by the amount, 255 when that would pass 255, and 0 from 255; down by the amount, 0 when that would pass 0, and 255 from 0. With the panel's `0x20`, a bar raised from 0 reads 0, 32, …, 224, 255 and then 0 again, and lowered from 255 reads 223, 191, …, 31, 0 and then 255 again: nine settings each way, of which GROUND textures the ground at 255 alone.
- **`Prefs_CommitOptions` (`0004a83c`)**, (apply): for each option that differs from `Prefs_LoadedOptions`, its handler with the value when apply is set, and then the copy updated; every option copied to `Prefs_PreviousOptions`.
- **`Prefs_RevertSelectedOptions` (`0004aaf0`)**, (indices, count): the named options copied back from `Prefs_LoadedOptions`, with no handler. `Prefs_Init` calls it in a demo ([the preferences start-up](input.md#the-preferences-start-up)).
- **`Prefs_SaveSelectedOptions` (`0004ab6c`)**, (indices, count): reads the file's 17 bytes, copies the named options over them and writes them back; when the file does not open it writes all 17 options from memory. The write stream opens the file created when missing and truncated when present ([the file classes](../formats/streams.md#the-file-classes)), so after a save that writes it holds exactly the 17 bytes. So every save is a read-modify-write of named options, as ES2's is ([writing it back](../../retail/simulation/preferences.md#writing-it-back--prefs_saveselectedoptions-00459b78)).
- **`Prefs_OptionChanged` (`0004ac2c`)**, (index): whether the option differs from `Prefs_LoadedOptions`.

## Functions

| ES1 | Address | ES2 |
|---|---|---|
| `PreferencesPanel_Raise`, `PreferencesPanel_OrbitViewHook` | `00057820`, `000577f8` | `0045cfd4`, `0045cfac` |
| `PreferencesPanel_Ctor`, `PreferencesPanel_Dtor`, `PreferencesPanel_Run`, `PreferencesPanel_Paint` | `00047578`, `00047a14`, `00047a88`, `00047e90` | `004566c4`, —, `00456d4c`, `004570e0` |
| `PreferencesPanel_Tick`, `PreferencesPanel_RefreshRow`, `PreferencesPanel_HandleEvent`, `PreferencesPanel_OnClick` | `00047f14`, `00047f84`, `00048078`, `000480f4` | part of `PreferencesPanel_Present` (`00457180`), `004571f4`, —, `004573a8` |
| `PreferencesPanel_GetButtonCaption`, `PreferencesPanel_SetDrawHook`, `PreferencesPanel_CallDrawHook` | `00048168`, `00048180`, `00048198` | `0045743c`, `00457450`, `00457464` |
| `PreferencesPanel_Save`, `PreferencesPanel_Revert` | `000483a0`, `000483d4` | `004574cc`, `004574e0` |
| `ControlsPanel_Ctor`, `ControlsPanel_Dtor`, `ControlsPanel_Run`, `ControlsPanel_Paint` | `00048408`, `000487b4`, `00048810`, `00048c58` | `00457d1c`, —, `00458650`, `00458c68` |
| `ControlsPanel_RefreshRow`, `ControlsPanel_OnClick`, `ControlsPanel_GetButtonCaption` | `00048ce4`, `00048db0`, `00048e80` | `00458d20`, `00458ebc`, — |
| `ControlsPanel_ApplyDevices`, `ControlsPanel_Revert` | `00048e98`, `0004903c` | — |
| `Prefs_UndoSetOption`, `Prefs_SetOption`, `Prefs_ToggleOption`, `Prefs_StepOption` | `0004a900`, `0004a914`, `0004a94c`, `0004a988` | `00459924`, `0045993c`, `00459980`, `004599b8` |
| `Prefs_CommitOptions`, `Prefs_StepOptionBack`, `Prefs_IncreaseOption`, `Prefs_DecreaseOption` | `0004a83c`, `0004a9d0`, `0004aa18`, `0004aa78` | `00459878`, `004599f4`, `00459a34`, `00459a90` |
| `Prefs_RevertSelectedOptions`, `Prefs_SaveSelectedOptions`, `Prefs_OptionChanged` | `0004aaf0`, `0004ab6c`, `0004ac2c` | `00459b04`, `00459b78`, `00459c38` |
| `Prefs_ApplySoundsOption`, `Prefs_ApplyMusicOption`, `Prefs_ApplyRadioOption`, `Prefs_ApplyMouseOption` | `0004acfc`, `0004ad0c`, `0004ad30`, `0004ad54` | `00459c6c`, `00459c98`, `00459cc4` for the message rows, — |
| `Input_GetJoystickDevice_Thunk`, `Input_GetJoystick2Device_Thunk` | `0004c63c`, `0004c644` | — |

`PreferencesPanel_OrbitViewHook` and `Prefs_ApplyRadioOption` have no Ghidra function. The preferences file's `0004a7fc`, `0004a8a8`, `0004ab28` and `0004ab58`, which `es2_xref.py` finds no reference to, are not read here.

## Globals

| | Address |
|---|---|
| `Prefs_Options`, `Prefs_LoadedOptions`, `Prefs_PreviousOptions`, 17 bytes each | `000bd562`, `000bd574`, `000bd586` |
| `Prefs_MouseOptionApplied`, byte | `000956c0` |
| `PrefsAlertVtable`, `ControlsAlertVtable` | `000951ae`, `000952c6` |
| `PreferencesPanel_SaveList`, `ControlsPanel_SaveList`, `int16` | `0009519c`, `00095348` |
| the preferences panel's rects: buttons *x*, *y*, width, height; readouts; bars | `0009510c`, `00095120`, `00095134`, `00095148`; `0009515c`–`00095174`; `0009517c`–`00095194` |
| the controls panel's rects: buttons *x*, *y*, width, height, caption margin; readouts | `00095244`, `00095254`, `00095264`, `00095274`, `00095284`; `00095294`–`000952b8` |

## Open

- **Open:** whether `00047578` and `00048408` each open an object file: the code before each ends on a 4-byte boundary with no zero fill or destructor copy, and the data runs on without a gap.
- **Open:** the calibration panel, `cal_alrt.cpp`, from `000490b8`: `00049154`, which the controls panel's CALIBRATE builds with (a stack object, the controls panel, 1), its loop `000496f0` and its destructor `00049678`, which it calls with 2; what it shows (`str\CAL_ALRT.STR` holds a device list and fifteen prompts, from `MOVE STICK TO UPPER LEFT, THEN FIRE.` to `CENTER THE PEDALS, THEN PRESS A KEY`), the order of its steps and when it writes `data\joy1.cal` ([the calibration file](joystick.md#the-calibration-file)); and whether it builds the panel slider (`AlertSliderGadget_Ctor`, [the slider](alert-panels.md#the-slider)), which neither panel here builds.
- **Open:** which instrument copies its rect to the screen first after `PreferencesPanel_Raise` returns to a cockpit view, and so how soon the pointer is drawn again: the slot `+8` that `CockpitView_SetView`'s `CockpitView_InvalidateWidgets` (`0002b4a0`) calls on each instrument, unread here, may make one copy on the next frame, before the clock's next advance.
- **Open:** whether a change made on the preferences panel before CONTROLS is opened and closed reaches `data\prefs.cfg`. A retail check: set SKY to its lowest, open CONTROLS, press DONE, press DONE on the preferences panel, and see whether the next mission's sky has its clouds back.
- **Open:** what the freed stick device reads after `NONE` is chosen with a stick fitted. A retail check under DOSBox-X with a stick: choose `NONE` on the controls panel, press DONE twice, and see whether the stick still steers the HERC.
- **Open:** a positive control in the running game ([when a module is done](../../herculan/plan-es1-support.md#when-a-module-is-done)). Proposed: `F12` in the forward view shows the outside view turning round the HERC above a strip of MUSIC, SOUNDS, RADIO and HORIZON beside SKY, GROUND and SHAPES; [=] on SKY adds clouds behind the panel at once; and `GROUND` at its highest textures the ground.
