# DBSIM.EXE — the status and objectives panels

**ES1's status panel (`P`, `Q`, `Ctrl+Q` and the mission's alerts) and objectives panel (`F11`) are members of the [in-mission panels](alert-panels.md)' base, as ES2's are.** What a player could see differently: the pause and quit prompts are the mission alert's panel at its size, the quit prompt titled `QUIT TO DOS?`; a mission alert's text is always its own string table's, where ES2's shows the mission's objective text for status 5; and the objectives panel lists the lines the shell wrote from the mission's `.ENG` file, left-aligned. Addresses are `ES1DBSIM` image addresses ([the X-32 executable](../formats/x32-executable.md#the-image)); ES2's are DBSIM's. The functions here remove their own arguments, so argument lists are in stack order ([calling conventions](../formats/compiler.md#calling-conventions)): a member function's object first, the rest in reverse of ES2's.

## Where the object files start and end

The status panel's code, `00046efc`–`00047577`, starts on the 4-byte boundary after `alert.cpp`'s zero byte ([where `alert.cpp` ends](alert-panels.md#where-the-object-files-start-and-end)), with `StatusAlertPanel_Ctor` and no destructor copy; `StatusAlertPanel_GetButtonCaption`'s `RET 8` ends at `00047577`, on the boundary before the preferences panel's code at `00047578`, so no zero fill tells whether `00047578` opens another file ([the preferences panel's files](preferences.md#where-the-object-files-start-and-end)). The objectives panel's file, `0004a18c`–`0004a723`, follows a `RET 4` at `0004a187` and two zero bytes: it opens with `HeapPtr_DtorObjectivesPanelCopy` (`0004a18c`), a lone copy of the 40-byte destructor that opens a run of `ConfigPart`'s copies ([module copies](../formats/compiler.md#vtables)), to which `es2_xref.py` finds no reference, and ends with `ObjectivesPanel_GetButtonCaption`'s `RET 8` at `0004a71f` and two zero bytes. No function of either file asserts, so neither names its source path, and neither has an entry in the static constructor table, whose entry after `Alert_StaticInit`'s (`000adc58`) holds `0004c64c` (`000adc5c`).

## The status panel — `StatusAlertPanel_Ctor` (`00046efc`)

**The pause prompt (`P`), the quit prompt (`Ctrl+Q`, `Ctrl+Esc`), the answer to `Q` and every alert the mission raises are one panel: a title, up to four lines of text and one or two buttons, all picked by a status number from one string table.** It is ES2's status alert ([its text and layout](../../retail/simulation/alert-panels.md#the-status-alerts-text-and-layout), [what it decides](../../retail/simulation/mission-objectives.md#the-status-alert--gnl_alrt-00455934)) with ES2's pause panel ([ES2's](../../retail/simulation/alert-panels.md#the-pause-panel--004561c0)) folded in as statuses 0 and 1, and without ES2's status-5 substitution: the body is always the table's.

**`StatusAlertPanel_Ctor` (`00046efc`)**, (panel, rect, *keepsScreen*, status), allocates `0x548` bytes when given none. `es2_xref.py` finds six calls, five passing (0, 0, 1, status): `Sim_DispatchCommand`'s `P` (0), `Ctrl+Q` and `Ctrl+Esc` (1) and `Q` (`Mission_StatusForAlert`'s answer) ([commands](main-loop.md#commands--sim_dispatchcommand-00059df8)); `Sim_MainTick` with `Mission_PollStatus`'s ([the tick](main-loop.md#the-tick--sim_maintick-00059700)); and `Prefs_LimitDetailInLowMemory` with `0x11`. The sixth, `Sim_DeathFlash` (`00058414`), passes (0, 0, 0, 2), so its panel keeps no copy of the screen under it ([the death flash](main-loop.md#the-death-flash--sim_deathflash-00058414)) ([the detail setting](object-classes.md#the-detail-setting--shapedetail_applydetailsetting-00057c08)). In order:

1. The rect: 222 × 109, centered by `AlertPanel_CenterRect`(109, 222) at (48, 45), or with a rect given, `AlertPanel_SetRect` at its point ([placing a panel](alert-panels.md#the-panel-object--alertpanel_ctorbase-00045988)); `AlertPanel_CtorBase`(panel, the rect, *keepsScreen*); `GeneralAlertVtable` (`0009507e`) at `+0x314`.
2. The text: `str\gnl_alrt` (`ResourcePath_BuildFolderName`, so `str\GNL_ALRT.STR`) into the base's string table `+0x30` with `StringTable_LoadFromFile`, then its three groups with `StringTable_LoadGroup`, each with no attribute destination (0): 19 titles at `+0x334`, 38 button captions at `+0x380` and 76 body lines at `+0x418`, which end at the object's last byte. The counts it passes, 19, 38 and 76, are capacities `StringTable_LoadGroup` does not read; the file's groups hold that many. Indexed by the status: the title `+0x334`[status] goes to `+0x318`, the captions from `+0x380`[2 × status] and the body from `+0x418`[4 × status] are pointed at by `+0x31c` and `+0x320`.
3. The buttons' rects, two built by `__vec_ctor` with `GauRect_ElementCtorStatusAlertPanelCopy` (`0004726c`, entered at its padding `00047269`), each (*x*, 70)–(*x* + 80, 90). A status of 0, 2, 3, 7, 8, `0x11` or `0x12` has one button, at *x* 71; 1, 4, 5, 6, 9, 10 and `0xb` to `0x10` have two, at *x* 22 and 120; any other has none (the jump table `000950c0`, behind an unsigned bound against `0x12`).
4. The plate: frame 0 of `dba\gnl2alrt` for statuses 0 and 1 and of `dba\gnl_alrt` for the rest, loaded with `ClassItem_LoadFromFile`, at `+0x2c`. Each array holds one 224 × 108 frame, whose last column falls past the panel's 223 and is cut ([drawing on a panel](alert-panels.md#the-panel-object--alertpanel_ctorbase-00045988)).
5. Each button: `PanelButton_Ctor`(0, index, panel, rect) ([the panel's buttons](alert-panels.md#the-panels-buttons)); its caption label, `Label_Ctor`(0, `WidgetFont_Cpylw`), at the button's `+0x2c`, centered on the rect by `Label_SetRect` with flags 2 and margin (0, −3) ([labels](cockpit-widgets.md#labels)); `Widget_RegisterClickable`(button, the panel's root `+0x281`); and the button added to the widget list `+0x28d`, whose count `+0x2b5` step 3 set.
6. The title: a label in `WidgetFont_Title` at `+0x40`, on the title bar `+0x2ba`, ((222 − *w* − 20) >> 1, 4)–(that + *w* + 20, 21), *w* the title's width (`HudFont_MeasureString`), with flags 2 and margin (0, −11). [The title bar](alert-panels.md#the-title-bar--alertpanel_settitletext-000461ec) frames that rect.
7. The body: four labels in `WidgetFont_Green6x8` at `+0x324`, centered (flags 2, margin 0) on (50, 30 + 10*i*)–(172, 40 + 10*i*).
8. The cancel widget `+0x2e3`, which [Esc] presses, is widget 0; for status 2, `+0x313` is cleared, so leaving the panel keeps the alert cursor ([putting a panel up](alert-panels.md#putting-a-panel-up-and-taking-it-down)).

Its table, `GeneralAlertVtable`, holds `StatusAlertPanel_Paint` (`000473c4`) at `+4`, `AlertPanel_SetTitleText` at `+8`, `StatusAlertPanel_RunModal` (`0004730c`, [the loop](alert-panels.md#the-loop)) at `+0xc`, `StatusAlertPanel_OnChildClick` (`000474e4`) at `+0x10`, `AlertPanel_HandleEvent` at `+0x14` and `StatusAlertPanel_GetButtonCaption` (`0004755c`) at `+0x18`, the run, the click and the caption by their padding entries; `+0` and `+0x1c` are 0.

- **`StatusAlertPanel_Paint` (`000473c4`)**, (panel): blits the plate with flags 0 at the panel's corner (`Bitmap_Blit`(plate, 0, the region's rect)); sets the title through slot `+8`; counts the body's lines up to the first empty one, at most four, and puts line *i* on body label *i* + 1 when there is one line and on label *i* otherwise (`Label_SetText`), so a one-line body stands on the second row; then runs each widget's paint, slot `+4` of its `+0x1c` table. ES2's ([its paint](../../retail/simulation/alert-panels.md#the-status-alerts-paint--00456068)) also stops at a null line.
- **`StatusAlertPanel_OnChildClick` (`000474e4`)**, (panel, flags, child): finds the child by comparing it with each entry of the widget list and sets the close byte `+0x2b9` to 2 for widget 0 and 3 for widget 1, so the loop answers 0 for the left button and 1 for the right. A key reaches it through `AlertPanel_PressWidget` ([keys](alert-panels.md#keys--alertpanel_handleevent-00046670)): [Return] and the trigger answer for the focused button, which the loop sets to the left one before its first pass and [Tab] moves, and [Esc] answers 0. ES2's (`00456160`) also resets the present timer.
- **`StatusAlertPanel_GetButtonCaption` (`0004755c`)**, (panel, index): `+0x31c`[index], the caption a button's paint asks its owner for.
- **`GeneralAlert_Dtor` (`000472b0`)**, (panel, flags): stores `GeneralAlertVtable`, frees the four body labels, runs `Alert_Dtor`(panel, 0) and frees the panel when bit 0 of the flags is set; ES2's (`00455f90`). `es2_xref.py` finds two calls, each with 3, from `00058414` and `Prefs_LimitDetailInLowMemory`; the other four callers destroy the panel with `Alert_Dtor` ([destroying a panel](alert-panels.md#destroying-a-panel--alert_dtor-00045d0c)).

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
2. The plate: frame 0 of `dba\hst_alrt`, loaded with `ClassItem_LoadFromFile`, at `+0x2c`. The array holds one 320 × 108 frame; the panel's rect covers 316 columns, edges included, so its last four are cut ([drawing on a panel](alert-panels.md#the-panel-object--alertpanel_ctorbase-00045988)).
3. Its own text: `str\obj_alrt` into the base's table `+0x30`, its two groups of one string each to `+0x328`, the title `OBJECTIVES`, and `+0x32c`, the caption `RETURN`.
4. The objectives: `data\object.str`, with the data folder's prefix (`Path_ApplyAltDrive`), into the table at `+0x318`, its group 0 into the pointers from `+0x330`, and the group's count, which `StringTable_LoadGroup` returns, as an `int16` at `+0x380`. The pointers have room for 20, and `StringTable_LoadGroup` writes as many as the file's count; the retail `.ENG` files' `OBJ:` blocks hold 12 pieces at most. The file is a string table of one group ([layout](../../retail/formats/str-strings.md#layout)): what the shell writes ([the objectives](../formats/eng-mission-text.md#the-objectives)) is that layout, its byte after each text the count of the text's attributes, 0.
5. The title: a label in `WidgetFont_Green6x8` at `+0x40`, centered (flags 2, margin (0, −1)) on ((315 − *w*) >> 1, 3)–(that + *w*, 13), *w* the title's width. The title bar's rect `+0x2ba` is set to −20 in all four words, so the frame [the title bar](alert-panels.md#the-title-bar--alertpanel_settitletext-000461ec) draws lies 20 pixels above and to the left of the panel's corner, outside its rect, and the clip draws none of it: its four lines are points beyond the rect's *x0*, and its corner pictures end left of it ([drawing on a panel](alert-panels.md#the-panel-object--alertpanel_ctorbase-00045988)).
6. The button: `PanelButton_Ctor`(0, 0, panel, (127, 93)–(187, 103)), its caption label in `WidgetFont_Cpylw` centered with margin (0, −1), registered with the panel's root and made widget 0.
7. The lines: seven labels in `WidgetFont_Cpylw` at `+0x382`, on (20, 17 + 10*i*)–(200, 27 + 10*i*), placed with flags 1 and margin 0, which `Label_SetRect` and `Label_SetText` treat as left-aligned at *x* 20, since only flags 2 and 4 center or right-align ([labels](cockpit-widgets.md#labels)).
8. The cancel widget `+0x2e3` is widget 0, `RETURN`.

`ObjectiveAlertVtable` holds `ObjectivesPanel_Paint` (`0004a5a4`) at `+4`, `AlertPanel_SetTitleText` at `+8`, `ObjectivesPanel_RunModal` (`0004a4ec`, [the loop](alert-panels.md#the-loop)) at `+0xc`, `ObjectivesPanel_OnChildClick` (`0004a6a0`) at `+0x10`, `AlertPanel_HandleEvent` at `+0x14` and `ObjectivesPanel_GetButtonCaption` (`0004a710`) at `+0x18`, the run, the click and the caption by their padding entries; `+0` and `+0x1c` are 0.

- **`ObjectivesPanel_Paint` (`0004a5a4`)**, (panel): blits the plate with flags 0 at the panel's corner; sets the title through slot `+8`; puts each of the file's lines that is not empty on the next line label (`Label_SetText`), stopping once seven are set, so an empty line leaves no gap and an eighth is not shown; then runs each widget's paint. ES2's ([its paint](../../retail/simulation/alert-panels.md#the-objectives-panels-paint--objectivespanel_paint-00457b58)) does the same with its own lines.
- **`ObjectivesPanel_OnChildClick` (`0004a6a0`)**, (panel, flags, child): sets the close byte to 1 for widget 0, so the loop answers 1. [Return], [Esc], the trigger and a click on `RETURN` all press widget 0. `AlertPanel_HandleEvent`'s cases ([keys](alert-panels.md#keys--alertpanel_handleevent-00046670)) do not include `F11`'s `0x57`, so a second `F11` leaves the panel up. ES2's (`00457c30`) also resets the present timer.
- **`ObjectivesPanel_GetButtonCaption` (`0004a710`)**, (panel, index): `+0x32c`, `RETURN`, whatever the index.
- **`ObjectivesPanel_Dtor` (`0004a480`)**, (panel, flags): stores `ObjectiveAlertVtable`, frees the seven line labels, destroys the second string table (`OwnedArray_Dtor`, 2), runs `Alert_Dtor`(panel, 0) and frees the panel when bit 0 of the flags is set; ES2's (`00457a80`). `F11`'s case destroys the panel with `Alert_Dtor` ([destroying a panel](alert-panels.md#destroying-a-panel--alert_dtor-00045d0c)), and `es2_xref.py` finds no branch or stored pointer to this destructor or its padding ([Open](#open)).

| | ES1 | ES2's |
|---|---|---|
| the lines | `data\object.str`, which the shell writes from the mission's `.ENG` file | block 13's `mission.str` lines |
| their place | left-aligned at *x* 20 | centered, and five pixels off the panel's center line |
| the title | in `green6x8`, with no frame | in `title` |
| the size | 315 × 109 at (2, 45), the plate `dba\hst_alrt`, 320 × 108 | 315 × 139 in 320-wide coordinates, the plate 630 × 230 at device scale |
| the button and the lines' rows | the button 60 × 10 at (127, 93); seven rows from *y* 17, 10 apart | the same, doubled |

Of the 72 retail `.ENG` files with an `OBJ:` block, `B0_C6_3A.ENG` holds 8 objectives and `B0_C7_2A.ENG` 10, so `F11` shows the first seven of each.

## Functions

| ES1 | Address | ES2 |
|---|---|---|
| `StatusAlertPanel_Ctor`, `GauRect_ElementCtorStatusAlertPanelCopy`, `GeneralAlert_Dtor`, `StatusAlertPanel_RunModal` | `00046efc`, `0004726c`, `000472b0`, `0004730c` | `00455934`, `0048dd23`, `00455f90`, `00455fe4` |
| `StatusAlertPanel_Paint`, `StatusAlertPanel_OnChildClick`, `StatusAlertPanel_GetButtonCaption` | `000473c4`, `000474e4`, `0004755c` | `00456068`, `00456160`, `004561ac` |
| `HeapPtr_DtorObjectivesPanelCopy`, `ObjectivesPanel_Ctor`, `ObjectivesPanel_Dtor`, `ObjectivesPanel_RunModal` | `0004a18c`, `0004a1b4`, `0004a480`, `0004a4ec` | [module copies](../formats/compiler.md#vtables), `0045751c`, `00457a80`, `00457ae4` |
| `ObjectivesPanel_Paint`, `ObjectivesPanel_OnChildClick`, `ObjectivesPanel_GetButtonCaption` | `0004a5a4`, `0004a6a0`, `0004a710` | `00457b58`, `00457c30`, `00457c6c` |

## Globals

| | Address |
|---|---|
| `GeneralAlertVtable`, the status panel's | `0009507e` |
| `ObjectiveAlertVtable`, the objectives panel's | `000955d6` |

## Open

- **Open:** what passes status 5 to the status panel: of the six calls `es2_xref.py` finds, four pass constants, and `Mission_Status`, whose answers the other two pass, has no arm that answers 5 ([the status](mission-status.md#the-status--mission_status-0000f4e8)).
- **Open:** what the objectives panel shows when `data\object.str` is missing, which `StringTable_LoadFromFile` would be asked to open; the shell leaves the file as it was for a mission whose `.ENG` file has no `OBJ:` block ([the objectives](../formats/eng-mission-text.md#the-objectives)).
- **Open:** what reaches `ObjectivesPanel_Dtor`, to which `es2_xref.py` finds no branch or stored pointer, nor to its padding `0004a47d`.
- **Open:** a positive control in the running game ([when a module is done](../../herculan/plan-es1-support.md#when-a-module-is-done)). Proposed: `F11` in `B0_C7_2A` lists seven of its ten objectives, left-aligned, and `P` puts up a panel the size of `Q`'s.
