# GO.EXE — the career

**The career menu lists the careers the player has started. NEW asks for a name and starts a career at its first mission's briefing; PROCEED loads the career selected in the list and goes on where it stood; DELETE removes it after a warning; STATS shows its pilot's record; SAVE/RESTORE keeps a backup of the career for each point it has reached, one per mission played, and puts a backup back as the career.** Between missions the career stands at the service bay, whose strip of buttons leads to the next mission ([the service bay's bar](between-missions.md#the-service-bays-bar)), and after each mission it takes in the mission's results ([after a mission](between-missions.md#after-a-mission)). The menu and its windows are built, opened and drawn as every menu is ([the menus](menus.md)); this page holds what their buttons and lists do, the career's two files and how they are written, and the career's way from a mission's end to the next mission. ES2 saves a campaign in one of twelve slots ([ES2's save slots](../../retail/formats/save-games.md#the-twelve-slots--savgamefilestr)); nothing here is ported from it.

Addresses are `ES1GO` image addresses ([the X-32 executable](../formats/x32-executable.md#the-image)). The functions here remove their own arguments, so argument lists are in stack order ([calling conventions](../formats/compiler.md#calling-conventions)). The roster is `Career_Roster` (`0009b218`) and the careers log `Career_Log` (`0009b20c`) throughout.

## Where the object file starts and ends

**`career.cpp`** runs from `CareerRecord_Current` (`00014ef4`) to `0001865b`. The file before ends with the screenshot's exit routine `Screenshot_StaticExit` (`00014ee0`, exit table entry `0009a28c`), whose `RET` at `00014ef0` is followed by three zero bytes; the asserts from `00014f94` to `00017fec` name `src\career.cpp`. Its last code is its static initializer `Career_StaticInit` (`00018629`, the static constructor table's entry `0009a248`), which zeroes the three words from `0009ae5c`, the squad's kill totals ([the results panel](debriefing.md#the-results-panel)); its `RET` at `00018658` is followed by three zero bytes and `TextTable_Ctor` (`0001865c`). `es1_boilerplate.py scan` over the file finds one copy of a library function, `Rect_CopyCtorCareerCopy` (`000175a9`), byte for byte `Rect_CopyCtor` (`0004b398`), between `CareerStatsHotspot_Draw` (`0001705e`) and `DebriefingResultsHotspot_Draw` (`000175d7`). The file holds the career record and pilot entry functions ([the career record](../formats/bst-battle-sets.md#the-career-record)), the roster's functions below, the hotspot handlers from `0001705e` to `00017d9f`, and the careers log's ([the careers log](../formats/career-log-and-player-mec.md#the-careers-log)). The handlers of this page's six screens are in [the menu handlers' file](main-menu.md#where-the-object-files-start-and-end), and the name box's class in the controls' code from `0001f442` ([the name box](#the-name-box)).

## The career menu

**`dlg\menu1200.dlg` lays out a window titled CAREER with seven text buttons, NEW, STATS, DELETE, OPTIONS, SAVE/RESTORE, MAIN MENU and PROCEED, two arrow buttons, and the list box 1202 of the careers' names.** The window is (49, 50)–(281, 165) on the screen; MAIN MENU carries `Esc` and PROCEED `Space`, so the menu opens with the pointer on PROCEED ([opening a menu](menus.md#opening-a-menu--menu_open-0003969e)); the icon buttons 101 and 102 carry `Up` and `Down` and stand at (106, 31) and (106, 81), right of the list at (21, 29)–(102, 94). The menu is screen 1200, backdrop 1000, class `CareerMenu` (`00081c12`), and its handler is `CareerMenu_OnMessage` (`0002aa0e`) ([the screens](menus.md#the-screens)). The main menu's CAREER button opens it in `Shell_Mode` 1 with `Shell_ReturnScreen` 1200 ([the buttons](main-menu.md#the-buttons)), and `Shell_OpenHomeMenu` opens it as the home menu in mode 1 ([the first screen](menus.md#the-first-screen--shell_openfirstscreen-0003ba47)).

**With no career in the log, NEW reads NEW > and PROCEED, STATS, DELETE and SAVE/RESTORE are greyed out; with one, NEW reads NEW and PROCEED reads PROCEED >.** `Menu_Open`'s case for 1200 (`0003996c`) tests the log's count `+4` (`0009b210`). At 0 it gives button 1700 line `0xbe` of `eng\watext.bin`, PROCEED, and button 1210 line `0xbd`, NEW >, through their slot `+0x3c`, `MTButton_SetTextFromTable` ([buttons](menus.md#buttons)), and makes 1700, 1220, 1230 and 1201 inactive; otherwise 1700 takes line `0xbf`, PROCEED >, and 1210 line `0xbc`, NEW. Each button is found with `DUIWindow_FindControlById`, so the lines replace the ones `eng\menu1200.bin` gave.

### The buttons

**`CareerMenu_OnMessage` looks the message's target `+6` up among the seven ids of the `REPNE SCASD` table at `000809ec`, its cases at `00080a08`, five of them in bytes Ghidra left undefined from `0002acd3`. Every message, in the table or not, then ends in one step: the word `Career_SelectedEntry` (`0007fcea`) takes the selected line of the list 1202, its slot `+0x2c` ([the list](#the-list)).** `Shell_HandleMessage` then opens the screen the target names when it is one of `Shell_ScreenIds` ([changing screens](menus.md#changing-screens--shell_handlemessage-0003c7d8)).

| Button | Id | The handler | Then |
|---|---|---|---|
| NEW | 1210 | when `Disk_HasFreeBytes` (`0002a9ad`)(60000) answers 0, the target becomes 6666 | the REGISTRATION window ([starting a career](#starting-a-career)), or the warning 6666 |
| PROCEED | 1700 | loads the selected career ([PROCEED](#proceed)) | the career's screen, 1700 or 1710, or 1250, 1260 or 5555 |
| SAVE/RESTORE, STATS | 1220, 1230 | loads the selected career as [PROCEED](#proceed) does, with no screen of its own | [the backups](#backups), [the stats](#the-stats) |
| DELETE | 1201 | the target becomes 1270 | [the delete warning](#deleting-a-career) |
| the arrows | 101, 102 | the list's slot `+0x1c`, `MTStrListBox_SelectPrev`, or `+0x20`, `MTStrListBox_SelectNext` ([the list box](menus.md#the-list-box)) | no screen |
| OPTIONS, MAIN MENU | 1300, 1000 | nothing | the options menu, whose RETURN button reads RETURN TO CAREER and comes back while `Shell_ReturnScreen` is 1200 ([the return screen](main-menu.md#the-return-screen)); the main menu |

Screen 6666 is `UnAssignedAlert` ([the screens](menus.md#the-screens)): `dlg\menu6666.dlg`'s window WARNING!, with the bitmaps `res32\menu999.dba`, at (49, 45)–(271, 153), the lines "You do not have enough space on the" and "hard drive to start a new career.", and one button, OKAY, id 1200 with the key `Space`.

### PROCEED

**PROCEED runs the main menu's RESUME on the career the list selects ([RESUME](main-menu.md#resume)), with two differences.** The case at `0002aa54`:

1. compares the selected line with the log's current entry `+6` (`0009b212`). When they are equal and `Career_Loaded` (`000809aa`) is set, the career is loaded already and the case goes to RESUME's step 2. Otherwise it makes the selected line the log's current entry and runs RESUME's step 1, which loads it with `Career_LoadCurrent` (`00016c1e`), whatever `Career_Loaded` holds;
2. in RESUME's step 2, a lost pilot sets `Shell_ReturnScreen` to 1200 where RESUME sets 1000, so the KIA warning's back button returns to the career menu.

The case for SAVE/RESTORE and STATS (`0002acd3`) makes the same comparison and, when it loads, the same load with `Career_LoadCurrent`, but leaves out the rest: it tests no campaign with `Career_PeekNextCampaign`, it calls `Career_StartMission` (`00016e93`)(roster, 0) whenever the current pilot's condition is not 0, the record's screen `+4` untested, and it sets `Career_BackupLine` (`000809b4`) to `Roster_CountUsedRecords` (`00017026`) − 1 and `Career_Loaded` but changes no target.

### The list

**The list shows the name of every career in the log, one to a line, with the current one selected; a click on a name, or the arrows, selects another.** The list 1202 is a style-0 list box ([the list box](menus.md#the-list-box)): its lines are `Menu_ListLines`(1202), which is `CareerLog_NameLines` (`000184ff`)(`Career_Log`), and `Menu_BuildFromDlg` builds it with the line `Career_SelectedEntry` (`0007fcea`) selected (`0003ab5f`), 1202 being among the 23 ids at `00081a00` that set their own ([the build](menus.md#opening-a-menu--menu_open-0003969e)). `Shell_LoadData` sets that word to the log's current entry, or 0 when that is negative ([loading](../formats/career-log-and-player-mec.md#loading)), and the handler's last step to the list's selection after every message, so the menu opens again on the line last selected.

`CareerLog_NameLines`, given the log, frees the buffers its previous call made and the array that held them, `CareerLog_NameBuffers` (`0007edfc`), allocates a new array there and a second one to return, each of count + 1 pointers, and for each entry in log order a 64-byte buffer holding the entry's name (`sprintf` with `%s`, `0007ee00`); both arrays end with a null pointer. The list frees the returned array when it is destroyed ([the list box](menus.md#the-list-box)), and the buffers live until the next call.

A press on the list selects the line under the pointer, and its release posts (0, the list's index, the selected line, 1202) ([the mouse](menu-input.md#the-mouse)); 1202 is not in the handler's table or in `Shell_ScreenIds`, so the release only stores the selection.

## Starting a career

**NEW opens a window titled REGISTRATION with the line PLEASE ENTER NAME, a framed box to type the name in under it, and ACCEPT and CANCEL buttons. ACCEPT starts a career under the name typed, loads its first mission and puts up the campaign's talking head, which leads to the war map; CANCEL, or `Esc`, goes back to the career menu.** When the drive holds fewer than 60,000 free bytes, NEW opens the warning 6666 instead ([the buttons](#the-buttons)).

`dlg\menu1210.dlg`'s window is (49, 50)–(281, 165); ACCEPT, 100, is at (152, 80)–(198, 92), CANCEL, 99 with the key `Esc`, at (100, 80)–(146, 92), the label at (24, 43), and the kind-2 record 103 at (24, 53)–(197, 65), the name box ([the name box](#the-name-box)), whose line of `eng\menu1210.bin`, REGISTERED TO:, the build does not read. No control has the key `Space`, so the window opens with the pointer on its first control, ACCEPT. The box's id 103 makes `DUIWindow_Activate` press it when the window opens, so it holds the input from the start: typed characters and the edit keys go to it ([the active window](menu-input.md#the-active-window), [the keys](menu-input.md#the-keys)). Screen 1210 is `NewCareerAlert` (`00081e6a`), and its handler `NewCareerAlert_OnMessage` (`0002aeec`) acts on message 100 alone:

1. takes the box's text, control 103's slot `+0x30`, `MTEditBox_GetText` (`0001f647`), the control found with `DUIWindow_FindEnabledControlById`;
2. `Career_New` (`000163ac`)(roster, 0, the text) ([`Career_New`](#career_new));
3. `Career_StartMission`(roster, 0) loads the career's first mission ([a career's next mission](#a-careers-next-mission)), and `Shell_PrepareMission` (`0002a523`) writes its objectives and builds its map ([`Shell_PrepareMission`](main-menu.md#the-buttons));
4. makes the target 1490, puts `anm\esiege3.anm` into `000aacfe` with the byte `0007fcd7` 1, and calls `MessageBox_SetKind` (`00037eac`)(`Shell_MessageBox`, 2).

Screen 1490 shows the message box of kind 2, the campaign's talking head, whose end posts 1700 ([the kinds](message-box.md#the-kinds)). CANCEL's 99 the handler leaves as it is, and `Shell_HandleMessage`'s *back* step turns it into the previous screen, the career menu ([changing screens](menus.md#changing-screens--shell_handlemessage-0003c7d8), step 3).

`Career_New` passes the word 0 and the name to `CareerLog_FindOrAdd`, which gives an empty name `UNTITLED` and returns an entry already in the log whose name matches, ignoring case ([adding a career](../formats/career-log-and-player-mec.md#adding-a-career)); so a name already in the log starts the new career in that career's files.

### The name box

**The box shows the name as it is typed, in green, after a blinking caret line; it takes up to 12 characters. `Backspace`, `Del`, `Left`, `Right`, `Home` and `End` edit it, `Esc` clears it, and a click puts the caret on the character under the pointer.** `Menu_BuildFromDlg`'s kind 2 builds `MTEditBox_Ctor` (`0001f442`)(0, id, rect), class `MTEditBox` (vtable `0007f590`, its name at `0007f584`), a `0x90`-byte control on `DUIControl_Ctor`(box, 0, id, rect), so `Tab` passes over it, built through `DUISliderControl`'s (`0007f2f8`) and `0007f356`'s vtables ([the controls](menus.md#the-controls)):

| Offset | Holds | At the build |
|---|---|---|
| `+0x1b` | how a press is tracked: 2, the box holds the input | 2 |
| `+0x28` | `int16` the caret's *x*, and the *x* of the last press | |
| `+0x2e`–`+0x3a` | the caret, a line from (*x*, top + 3) to (*x*, bottom − 3) | *x* left + 3 |
| `+0x3e` | the font, `000aace2`, `nm_green` ([the loads](../formats/styles-dat.md#the-loads)) | |
| `+0x42` | the alignment | 0 |
| `+0x46` | the text, 13 bytes | zeroed |
| `+0x86` | `int16` the cursor: the index the next character goes to | 0 |
| `+0x88` | `int16` the first character shown | 0 |
| `+0x8a` | `int16` the text's length + 1 | 1 |
| `+0x8c` | the frame, `STYLES.DAT` record 3 (`00082654` + `0xc0`) | |

Unless `-X` was given (`Shell_DebugMode`, `0007fd08`) and while the byte `0007f118` is clear, the constructor registers `Shell_ScanKeySubscriber` with the keyboard for the ten codes at `0007e7f4` and sets the byte ([the keyboard subscribers](startup.md#the-keyboard-subscribers)); `MTEditBox_Dtor` (`0001f66f`) removes it and clears the byte, stores the vtables `0007f356`, `0007f2f8` and the button file's copy of `DUIControl`'s, `0007f1ea`, in turn, and frees the box when bit 0 of its flags is set. The vtable's slots:

| Slot | Function | Does |
|---|---|---|
| `+4` | `MTEditBox_Dtor` (`0001f66f`) | above |
| `+8` | `MTEditBox_Draw` (`0001f738`) | sets the render context `000ab612`'s alignment to `+0x42`; draws the frame with `CornerFrame_Draw` (`00044b86`) over the box's rect; when the state `+0x19` is 1, pushed, and `Shell_BlinkOn` (`0007fce0`) is 1, the caret in color `0x17` with `Raster_DrawLineColour` (`0004ba84`); then each non-zero byte of the text from index `+0x88` to index `+0x8a`, that one included, with `HudFont_DrawGlyph` in the font, from *x* left + 3 at *y* bottom − 3, *x* advancing by `HudFont_GlyphWidth`; and clears *changed* |
| `+0xc` | `MTEditBox_PlaceCursor` (`0001f85f`) | by the *x* `+0x28`: left of left + 3, while `+0x88` is above 0, takes 1 from `+0x88` and the cursor and puts the caret at left + 3; from there to right − 3, puts the cursor on the character under *x*, a zero byte counting 1 pixel wide, with `MTEditBox_UpdateCaret`; right of right − 3, adds 1 to `+0x88` with `MTEditBox_UpdateCaret` until the caret is no further right than right − 3. Each but the first path's no-op sets *changed* |
| `+0x30` | `MTEditBox_GetText` (`0001f647`) | the text `+0x46` |
| `+0x38` | `MTEditBox_SetTrackPoint` (`0001f656`)(box, point) | stores the point's *x* at `+0x28` |
| `+0x3c` | `MTEditBox_TypeChar` (`0001fa19`)(box, character) | a character from `0x20` to `0x7f`, with the cursor below 12, is stored at the cursor over what was there, and the cursor and `+0x8a` go up 1, then `MTEditBox_UpdateCaret`; any character sets *changed* |
| `+0x40` | `MTEditBox_HandleKey` (`0001fa7e`)(box, code) | below |

`MTEditBox_UpdateCaret` (`0001f9a0`) sets the caret's *x* to left + 3 plus the widths of the characters from `+0x88` up to the cursor, in `+0x2e`, `+0x36` and `+0x28`. A press on the box calls its slot `+0x38` with the point and then its slot `+0xc`, and the box stays pushed while it holds the input ([the mouse](menu-input.md#the-mouse)); `DUIWindow_Update` draws a control whose `+0x1b` is 2 on every pass ([updating a window](menus.md#updating-a-window--duiwindow_update-0001c067)), so the caret blinks with `Shell_BlinkOn`, set for about 0.33 s of every 0.68 s ([the devices](startup.md#the-devices)).

`MTEditBox_HandleKey` does nothing while the box is inactive (state 2). Otherwise it looks the code up among the ten of the `REPNE SCASD` table at `0007f5e0`, its cases at `0007f608`, seven of them in bytes Ghidra left undefined from `0001fac6`, and every code then sets *changed*:

| Key | Code | Does |
|---|---|---|
| `Space` | `0x39` | types a space, slot `+0x3c` with `0x20` |
| `Up`, `Down` | `0x48`, `0x50` | nothing |
| `Left` | `0x4b` | the cursor less 1 unless it is 0, then `MTEditBox_UpdateCaret` |
| `Right` | `0x4d` | the cursor plus 1 while it is below `+0x8a` − 1, then `MTEditBox_UpdateCaret` |
| `Backspace` | `0x0e` | with the cursor at 0: the cursor and `+0x88` 0 and `+0x8a` 1, the text left as it is; with the cursor at `+0x8a` − 1, the end: `+0x8a` and the cursor less 1 and the character there cleared to 0; elsewhere: the cursor less 1 and the character there replaced by a space. Then `MTEditBox_UpdateCaret` |
| `Esc` | `0x01` | the text cleared (13 zero bytes), the cursor and `+0x88` 0, `+0x8a` 1, the caret at left + 3 |
| `Home` | `0x47` | the cursor and `+0x88` 0, the caret at left + 3 |
| `End` | `0x4f` | the cursor `+0x8a` − 1, then `MTEditBox_UpdateCaret` |
| `Del` | `0x53` | with the cursor before `+0x8a` − 1: the characters after it moved one to the left, the one at `+0x8a` − 1 cleared to 0, and `+0x8a` less 1 |

The keys reach the box through `Shell_ScanKeySubscriber`, `Shell_ForwardKeyToDialog` and `DUIWindow_SendKey`, which passes the code with bit 7 cleared, and the characters through `DUIWindow_SendChar` ([the keys](menu-input.md#the-keys)); with Num Lock on, the subscriber passes `Home`, `Up`, `Left`, `Right`, `End` and `Down` on to other subscribers rather than the box ([the keyboard subscribers](startup.md#the-keyboard-subscribers), [Open](#open)).

### `Career_New`

`Career_New` (`000163ac`), given a roster and two arguments:

1. stores at the roster's `+1` the careers-log entry `CareerLog_FindOrAdd` (`00018099`) returns for `Career_Log`, the second argument and the third, whose `+0x42` and `+0x53` name the career file and the hangar file ([adding a career](../formats/career-log-and-player-mec.md#adding-a-career)); sets the roster's word `+5`, the battle set, to the word `0007fcec`, the words `+7` and `+9` to 0 and the byte `+0` to 1; and sets `Career_Salvage` (`0007fcf0`) to 60000;
2. `Roster_ShuffleNameGroups` (`00016677`)(roster): for each squadmate row *r* from 0 to 2, `Words_RandomPermutation` (`000162f4`)(4, four words) and then the roster's word `+0xb` + 8*r* + 2*i* = 4*r* + word *i*, so each squadmate's four name groups of `car\names.dat`, 4*r* to 4*r* + 3, in a random order ([the roster](#the-roster));
3. loads `car\c_armory.dat` into the weapon stock `000aad62` with `Inventory_LoadFile` and `car\car_init.dat` into the hangar with `Hangar_LoadFile` ([the starting HERCs and the weapon stock](../formats/car-hercs-and-stock.md));
4. opens a `FileRWStream` on each of the two file names, asserting at severity 5 (`career.cpp` lines `0x2d1` and `0x2d8`) when one does not open, and writes to the first the roster's byte `+0`, its word `+5` and its `0x18` bytes from `+0xb`;
5. for each of the 41 records: sets it up with `CareerRecord_Init`; when it is the record the word `+7` names, sets its byte `+0` to 1, calls `Roster_InitSquadPilots` (`000166ed`)(roster), stores `Career_Salvage` at its `+0x129`, copies its 100 words at `+0x12d` into the flags at `000ab244`, and stores the word `Career_AutoRepairMode` (`0007fcf8`) at its `+0x1f5`; then writes the record's `0x1f7` bytes and the stock (`Inventory_Write`) to the first file and the hangar (`Hangar_Write`) to the second;
6. closes both files, makes the player's entry `+0xe` of the record the word `+7` names that record's current pilot (`CareerRecord_SelectPilot`, [pilot entries](../formats/bst-battle-sets.md#pilot-entries)), and calls `Roster_ForEachPilotNoOp` (`00016fdd`)(roster).

`Words_RandomPermutation`(*n*, words) sets the *n* words to −1 and then, for *i* from 0 to *n* − 1, puts *i* in the (*k* + 1)-th word still −1, *k* being `Math_RandomBelow`(`Shell_RandomState`, *n* − *i*) ([the shell's generator](../formats/msn-mission-file.md#the-shells-generator)): a random order of 0 to *n* − 1.

`Roster_InitSquadPilots`(roster) sets, on the record the word `+7` names, pilot entry 0's slot `+8` to 0, the player's HERC in hangar slot 0, and calls `BarGraph_NoOpCareerCopy` (`0001600d`)(record, 0), which does nothing. Then, on record 0 whatever `+7` holds, it sets each squadmate's entry *i* from 1 to 3: the name group `+0` to the roster's word `+0xb` + 8(*i* − 1), the first of its row; the name `+2` to 0; `+4` to 1, the row's next column, which `CareerRecord_ReplaceLostPilots` reads ([lost squadmates](between-missions.md#lost-squadmates)); `+6` to 0; the hangar slot `+8` to *i*; and on strength `+0xa` to 1. `Career_New` calls it with `+7` 0, so both parts are record 0's.

`Roster_ForEachPilotNoOp`(roster) calls `BarGraph_NoOpCareerCopy`(the record the word `+7` names, *i*) for *i* from 0 to 3.

## Deleting a career

**DELETE puts up a warning, "Are you sure you want to delete this career?", with CANCEL and DELETE and the pointer on DELETE. DELETE removes the career selected in the list, and its two files, and returns to the career menu; CANCEL returns without it.** `dlg\menu1270.dlg`'s window is (49, 45)–(271, 153) with the bitmaps `res32\menu999.dba` (its id 999), with the two lines as labels, CANCEL 99 with `Esc` and DELETE 100 with `Space`. Screen 1270 is `DeleteAlert` (`00081f1a`), and its handler `DeleteAlert_OnMessage` (`0002ae96`), for message 100:

1. `CareerLog_Delete` (`0001828e`)(`Career_Log`, `Career_SelectedEntry`), which deletes the entry's two files and writes and loads the log again without it ([writing](../formats/career-log-and-player-mec.md#writing)). The career menu's last step stored the list's selection in `Career_SelectedEntry` when DELETE was pressed;
2. `Career_SelectedEntry` set to the log's current entry `+6` after the load, and `Career_Loaded` cleared;
3. the target 1200.

## Backups

**SAVE/RESTORE opens CAREER BACKUPS: a list of the selected career's backups, each shown by the campaign and mission it was made before, with SAVE, RESTORE, STATS and DONE. SAVE makes a backup of the career as it stands, asking first when a backup of the same point exists; RESTORE puts the selected backup back as the career and goes on from it; STATS shows the selected backup's record; DONE returns to the career menu.** A career's backups are records 1 to 40 of the roster, and SAVE writes the one numbered the career's missions played + 1, so a career keeps one backup for each count of missions played ([the roster](#the-roster)).

`dlg\menu1220.dlg`'s window is (49, 50)–(281, 165), with SAVE 1221, RESTORE 1222, STATS 1230 and DONE 1200 with `Esc`, the icon buttons 101 and 102 with `Up` and `Down` at (136, 31) and (136, 79), and the list box 1223 at (18, 27)–(134, 91). No control has the key `Space`, so the menu opens with the pointer on SAVE. Screen 1220 is `CareerRecordsMenu` (`00081e90`), and its handler `CareerRecordsMenu_OnMessage` (`0002af94`) looks the target up among the six ids of the `REPNE SCASD` table at `00080a24`, its cases at `00080a3c`, in bytes Ghidra left undefined from `0002afba`:

| Button | Id | The handler | Then |
|---|---|---|---|
| the arrows | 101, 102 | list 1223's slot `+0x1c` or `+0x20` | no screen |
| SAVE | 1221 | [SAVE](#save-and-the-overwrite-warning) | the warning 1240, or no screen |
| RESTORE | 1222 | [RESTORE](#restore) | the career's screen, 1700 or 1710, or 1250 |
| STATS | 1230 | the roster's word `+7` set to `Roster_NthUsedRecord` (`00016845`)(roster, the selected line + 1), the backup the line shows, and `Career_BackupLine` (`000809b4`) to the selected line | [the stats](#the-stats) of that backup |
| DONE | 1200 | `Shell_ReturnScreen` 1200 | the career menu |

### The backup list

**Each line reads CAMPAIGN *c*: MISSION *m*, the campaign as the player counts them and the mission within it, one line for each backup in the order of their records.** The list 1223 is a style-0 list box: its lines are `Menu_ListLines`(1223), which is `Roster_BackupListLines` (`000183bd`)(roster), and `Menu_BuildFromDlg` builds it with the line `Career_BackupLine` (`000809b4`) selected (`0003abf0`). `Career_BackupLine` is set by the career menu's load (`Roster_CountUsedRecords` − 1, the last line), by RESUME's load ([RESUME](main-menu.md#resume)), and by RESTORE and STATS here (the selected line).

`Roster_BackupListLines` allocates `Roster_CountUsedRecords` + 1 pointers and, for each record *n* from 1 to 40 whose byte `+0` is set, in order:

1. takes *k* as the record's campaign `+6`, replaced by the last *c* from 0 to 7 whose word of `Career_CampaignStarts` (`0007ebf0`: 0, 7, 7, 15, 15, 24, 24, 32) equals the record's missions played `+0xa`;
2. writes `CAMPAIGN %1d: MISSION %1d` (`0007ede1`) with word *k* of `Career_CampaignNumbers` (`0007ebe0`: 1, 2, 2, 3, 3, 4, 4, 5) and the missions played − word *k* of `Career_CampaignStarts` + 1 into the 25-byte buffer `Roster_BackupLines` (`000aa7d4`) + 25*n*, and adds a pointer to it;

and ends the array with a null pointer. The words of `Career_CampaignStarts` are the missions before each campaign along the retail `BTLSET_0.DAT`'s branches, whose campaigns hold seven positions (0), eight (1, 2, 5 and 6) and nine (3 and 4) ([the retail battle sets](../formats/bst-battle-sets.md#the-retail-battle-sets)), and its paired campaigns 1 and 2, 3 and 4, and 5 and 6 share a number, so the list counts the branches' campaigns 1 to 5.

### SAVE and the overwrite warning

**SAVE, 1221**: when `Roster_SaveSlotUsed` (`000167a1`)(roster), the byte `+0` of the record numbered record 0's missions played `+0xa` + 1, is set, the target becomes 1240. Otherwise `Roster_SaveBackup` (`000167cd`)(roster) makes the backup; the list takes new lines from `Roster_BackupListLines` through its slot `+0x44`, `MTStrListBox_SetLines`, and selects the line `Roster_CountUsedRecords` − 1, the last, through its slot `+0x24`; the target stays 1221, which is not in `Shell_ScreenIds`, so the window stays up.

`Roster_SaveBackup` sets the roster's word `+7` to the missions played `+0xa` of the record `+7` names, plus 1, calls `Career_Save`(roster, that word) ([saving](#saving--career_save-000168b0)), and sets `+7` back to 0.

**The warning** is screen 1240, `SaveWarningAlert` (`00081ed8`): `dlg\menu1240.dlg`'s window WARNING! at (49, 50)–(281, 165), CANCEL 99 with `Esc`, OVERWRITE 100 with `Space`, the label "You already have a backup of" at (60, 43), and the label 1223 at (72, 53), whose text `Menu_BuildFromDlg` takes from `Roster_SaveSlotText` (`0001838d`)(roster) ([the build](menus.md#opening-a-menu--menu_open-0003969e)): `Roster_BackupLines` + 25 × (the missions played of the record `+7` names + 1), the line the backup list last wrote for the backup SAVE would replace. Window 1240 draws its labels in `nm_green` ([the window](menus.md#the-window)). Its handler `SaveWarningAlert_OnMessage` (`0002b30a`) makes the backup with `Roster_SaveBackup` on 100 and sets the target to 1220 on 100 and on 99, so both answers return to the backups, whose list is built again as they open.

### RESTORE

**RESTORE, 1222**, puts the selected backup back as the career and goes on from it:

1. `Roster_RestoreBackup` (`00016817`)(roster, the selected line + 1): the roster's word `+7` becomes `Roster_NthUsedRecord`(roster, *n*), the backup the line shows; `Roster_ActivateRecord` (`00016d45`)(roster) makes it the career ([making a record current](#making-a-record-current--roster_activaterecord-00016d45)); and `+7` becomes 0 again;
2. `Career_BackupLine` becomes the selected line, and the target the career's screen, record 0's word `+4`;
3. when the current pilot's condition (`PilotEntry_Condition`(record 0, its `+0xe`)) is 0, the target becomes 1250 and `Shell_ReturnScreen` 1220, so the KIA warning's back button returns to the backups. Otherwise, unless the target is 1710, `Career_StartMission`(roster, 0) and `Shell_PrepareMission`; then `Career_Save`(roster, the roster's dword `+7`, whose low word is 0), which writes the restored career as the career, record 0, to the files; and `anm\esiege3.anm` into `000aacfe` with `0007fcd7` 1.

## The stats

**STATS opens CAREER STATS, a panel of a career's record: the career's name, the pilot's status, the current mission and the missions played, the salvage, and the player's kills of HERCs, flyers and structures.** `dlg\menu1230.dlg`'s window is (49, 50)–(281, 165), with DONE 99 with `Esc` at (90, 91)–(129, 103) and the kind-14 hotspot 1231 at (17, 27)–(205, 88), whose handler is `CareerStatsHotspot_Draw` (`0001705e`) ([the build](menus.md#opening-a-menu--menu_open-0003969e)). Screen 1230 is `CareerStatsAlert` (`00081eb4`), and its handler `CareerStatsAlert_OnMessage` (`0002b2cc`), for 99, sets the roster's word `+7` back to `Roster_NthUsedRecord`(roster, 0), 0; `Shell_HandleMessage`'s *back* step then returns to the screen STATS was pressed on. From the career menu the record shown is the career's, record 0, after the menu's load; from the backups, the backup selected.

`DUIWindow_Draw` calls `CareerStatsHotspot_Draw` with (the hotspot's rect, 1231) when the window is drawn ([drawing a window](menus.md#drawing-a-window--duiwindow_draw-0001bfa3)). It reads only the rect (*x0*, *y0*, …), in the window's render context `000ab612`:

1. sets the context's alignment `+0x231` to 0, left, and fills the rect with color `0x1f` framed in `0x1f` with `Raster_FillFrameRectColours` (`0004b8d8`), on a copy of the rect `Rect_CopyCtorCareerCopy` makes;
2. draws lines of `eng\watext.bin` (`0009afa8`) in the font `000aacea`, `cstats_2`, and the values in `000aace6`, `cstats_1` ([the loads](../formats/styles-dat.md#the-loads)), with `HudFont_DrawString`, each number formatted with `sprintf` as `%d` unless the table says otherwise.

The record is the roster's record its word `+7` names (`0009b23b` + `0x1f7` × the word `0009b21f`), which `CareerRecord_Current` (`00014ef4`) also returns in mode 1, and the pilot is its entry `+0xe`, the player's ([pilot entries](../formats/bst-battle-sets.md#pilot-entries)):

| Line | Label | At | Value | At |
|---|---|---|---|---|
| `0xa7` | NAME: | (*x0* + 8, *y0* + 9) | the career's name, the careers-log entry at the roster's `+1` | (*x0* + 36, *y0* + 9) |
| `0xa8` | STATUS: | (*x0* + 113, *y0* + 9) | ACTIVE (`0xb6`), or DECEASED (`0xb7`) when the pilot's condition, `PilotEntry_Condition` of `CareerRecord_Current`'s record and its `+0xe`, is 0, in `cstats_1` | (*x0* + 147, *y0* + 9) |
| `0xa9` | CURRENT MISSION: | (*x0* + 8, *y0* + 18) | the record's position `+8` + 1 | (*x0* + 81, *y0* + 18) |
| `0xaa` | TOTAL MISSIONS: | (*x0* + 8, *y0* + 27) | `CareerRecord_Current`'s missions played `+0xa` | (*x0* + 81, *y0* + 27) |
| `0xab` | SALVAGE: | (*x0* + 8, *y0* + 36) | the record's dword `+0x129`, `%ld KG` (`0007ed3b`) | (*x0* + 81, *y0* + 36) |
| `0xac` | KILLS | (*x0* + 8, *y0* + 45) | | |
| `0xad` | HERCS: | (*x0* + 46, *y0* + 45) | the pilot's word `+0x17` | (*x0* + 81, *y0* + 45) |
| `0xaf` | FLYERS: | (*x0* + 46, *y0* + 54) | the pilot's word `+0x1b` | (*x0* + 81, *y0* + 54) |
| `0xae` | STRUCTURES: | (*x0* + 106, *y0* + 45) | the pilot's word `+0x19` | (*x0* + 161, *y0* + 45) |
| `0xb0` | MISC: | (*x0* + 106, *y0* + 54) | the text `0` (`0007ed4b`), with no value read | (*x0* + 161, *y0* + 54) |

The kills are the words the debriefing's gains add to after each mission, HERCs, structures and flyers in that order ([the results panel](debriefing.md#the-results-panel)), and the rank is read from the HERC kills ([pilot entries](../formats/bst-battle-sets.md#pilot-entries)).

## The roster and the career's files

### The roster

**The roster, `Career_Roster` (`0009b218`), holds the loaded career: a header and 41 career records ([the career record](../formats/bst-battle-sets.md#the-career-record)). Record 0 is the career; records 1 to 40 are its backups, record *n* made after *n* − 1 missions.**

| Offset | Holds |
|---|---|
| `+0` | byte: `Career_New` sets it to 1 and `Roster_AdvanceCareer` clears it; the career file's first byte ([Open](#open)) |
| `+1` | the career's careers-log entry, whose `+0x42` and `+0x53` name its two files |
| `+5` | `int16` the battle set ([which battle set](../formats/bst-battle-sets.md#which-battle-set)) |
| `+7` | `int16` the record the shell works on: 0, except within the functions on this page that set it and put it back, and on the stats screen from the backups |
| `+9` | `int16` the career's word `+2`, kept here while it is loaded: `Roster_ActivateRecord` copies it in and `Career_Save` back ([Open](#open)) |
| `+0xb` | 12 `int16` name groups of `car\names.dat`, three rows of four, row *r* for squadmate *r* + 1 ([`Career_New`](#career_new), step 2) |
| `+0x23` | 41 records of `0x1f7` bytes |

`CareerRecord_Current` returns the record `+7` names in mode 1. `Roster_NthUsedRecord` (`00016845`)(roster, *n*) returns 0 for an *n* of 0; otherwise it counts the records 1 to 40 whose byte `+0` is not 0 and returns the index of the *n*-th, or 0 when there are fewer. `Roster_CountUsedRecords` (`00017026`)(roster) counts those records, the backups. `Roster_GetRecordWord2` (`00017014`)(roster) returns the word `+9`, and `maybe_Roster_ReadPlayerEntry` (`00016fb0`)(roster, stream) reads 2 bytes through the stream's slot `+0x1c` into the roster's `+0x31`, record 0's player entry `+0xe`; `es2_xref.py` finds no reference to either ([Open](#open)).

### The files

**A career is two files in `car\`: the career file, `CAR_`*n*`.CAR`, with the roster's header and every record, and the hangar file, `CAR_`*n*`.MEC`, with a hangar for every record.** Their names are the careers-log entry's ([adding a career](../formats/career-log-and-player-mec.md#adding-a-career)).

```
CAR_n.CAR
byte      the roster's +0
int16     the battle set, the roster's +5
24 bytes  the name groups, the roster's +0xb
41 x {
  0x1f7 bytes  a career record
               its weapon stock, as Inventory_Write writes it
}

CAR_n.MEC
41 x a hangar list, list n the hangar of record n
```

The stock is laid out as [the weapon stock](../formats/car-hercs-and-stock.md#the-weapon-stock), the hangar list as [the HERC lists](../formats/car-hercs-and-stock.md#the-herc-lists). `Career_New` writes record 0 and 40 empty records, each with the starting stock, and the starting hangar 41 times. `Career_LoadCurrent` (`00016c1e`) reads the career file whole, each record's stock into the stock the dword `000aad62` + 4*n* points to, and then makes record 0 current with `Roster_ActivateRecord`, which reads its hangar ([making a record current](#making-a-record-current--roster_activaterecord-00016d45)).

### Saving — `Career_Save` (`000168b0`)

**`Career_Save`(roster, slot) writes the career, record 0, to both files, and with a slot other than 0 also copies it to that backup.** The slot is the low word of its second argument. With no careers-log entry at the roster's `+1` it returns at once. Otherwise:

1. it writes the 100 flags at `000ab244` to `data\mission.var` (entry 5 of the file-name table) through a `FileWStream`, asserting at severity 5 (`career.cpp` line `0x346`) when it cannot open it;
2. it deletes the career file and opens it as a `FileRWStream`, asserting at line `0x350` when it cannot, and writes the roster's byte `+0`, its word `+5` and the 24 bytes from `+0xb`;
3. it updates record 0: the byte `+0` set to 1, the word `+2` from the roster's `+9`, `+0x129` from `Career_Salvage` (`0007fcf0`), the 100 words `+0x12d` from the flags, and `+0x1f5` from the word `Career_AutoRepairMode` (`0007fcf8`);
4. with a slot other than 0, it copies record 0 over record *slot* (`memmove`) and the stock with `Inventory_CopyThroughScratch` (`00027cb3`)(0, slot);
5. it writes the 41 records, each followed by its stock (`Inventory_Write` of the dword `000aad62` + 4*n*), and closes the file;
6. it writes the hangar file again through `car\temp.mec`, entry 12 of the file-name table: it opens the hangar file as a `FileRStream` and `temp.mec` with `FileRWStream_Open` (`0005ac0c`), and for each of the 41 lists, for the slot's passes over the file's list with `HercList_Skip` (`00028db6`) and writes the hangar with `Hangar_Write`, and for any other copies the file's list with `HercList_Copy` (`000292da`)(temp, file). It closes both, deletes the hangar file and renames `temp.mec` to it, asserting at severity 5 (line `0x382`, "Unable to rename file") when the rename fails.

So a save with slot 0 writes the career as it stands; a backup's writes record *slot* and hangar list *slot* from the career as it stands, record 0 as step 3 leaves it, and hangar list 0 as the file held it. `es2_xref.py` finds eight calls, from `CareerRecord_SaveCurrent` (`00014f28`) in mode 1 ([the player file](../formats/career-log-and-player-mec.md#the-player-file)), `Roster_SaveBackup`, RESTORE, `Shell_OpenFirstScreen` after a mission in mode 1 ([the first screen](menus.md#the-first-screen--shell_openfirstscreen-0003ba47)), the war map's handler `WarMapMenu_OnMessage` ([CANCEL](war-map.md#cancel)), `AnyDisabledAlert_OnMessage` (`0002a581`) and the service bay's handler `ServiceBayMenu_OnMessage` (`0002d327`).

`HercList_Skip`(stream) passes over one hangar list: it moves the stream's position 12 bytes on (its slots `+8` and `+0xc`), past the header's four words and dword, reads the HERC count through slot `+0x1c`, and for each HERC moves 2 bytes on, past the slot, and reads the HERC with `Herc_ReadFromStream` into a HERC of its own (`Herc_Ctor`, chassis −1), which it destroys at the end with `000232a4`(HERC, 2). `HercList_Copy`(dest, src) copies one: the four words, the dword and the HERC count, each read from *src* through its slot `+0x1c` and written to *dest* through its slot `+0x18`, then for each HERC its slot word the same way and the HERC read with `Herc_ReadFromStream` into a HERC of its own and written with `Herc_Write` ([the HERC record](../formats/car-hercs-and-stock.md#the-herc-record)).

### Making a record current — `Roster_ActivateRecord` (`00016d45`)

**`Roster_ActivateRecord`(roster) makes the record the word `+7` names the career the shell works on.** On that record *r*:

1. `Career_Salvage` becomes *r*'s `+0x129`; the word `000aad5e`, which `Career_OutcomeChoice` reads, *r*'s `+0xc`, the header word 3 of its last mission ([a career's missions](../formats/bst-battle-sets.md#a-careers-missions)); the roster's `+9` *r*'s `+2`; the flags at `000ab244` *r*'s 100 words `+0x12d`; and the word `000aad58`, the debriefing's ordinal, flag 0;
2. with `+7` not 0, *r* is copied over record 0 (`memmove`) and its stock over record 0's with `Inventory_CopyThroughScratch`(`+7`, 0);
3. the word `Career_AutoRepairMode` (`0007fcf8`) becomes *r*'s `+0x1f5`;
4. `Hangar_ReadFromSave` (`00028bd6`)(`+7`, the hangar file's path) reads hangar list `+7` into the hangar ([the HERC lists](../formats/car-hercs-and-stock.md#the-herc-lists));
5. `CareerRecord_SelectPilot`(*r*, its `+0xe`) and `Roster_ForEachPilotNoOp`(roster).

`es2_xref.py` finds two calls: `Career_LoadCurrent`'s last, with `+7` 0, and `Roster_RestoreBackup`'s.

### Moving a career on — `Roster_AdvanceCareer` (`00016f22`)

`Roster_AdvanceCareer`(roster, a second argument it does not read) clears the roster's byte `+0` and the byte `+1` of the record `+7` names, and moves that record on with `Career_AdvanceCampaign` (`00015a5a`) ([moving on](between-missions.md#moving-on)); when its campaign changed, it sets the record's word `+2` and the roster's `+9` to 0 and clears the record's 100 words with `CareerRecord_ClearWords` (`000161ae`). `es2_xref.py` finds two calls, from `AnyDisabledAlert_OnMessage` and the service bay's handler `ServiceBayMenu_OnMessage` (`0002d327`).

## A career's next mission

`Career_StartMission` (`00016e93`), given a roster and a word *b*, sets the roster's word `+7` to `Roster_NthUsedRecord`(roster, *b*), loads the battle set whose index is the roster's word `+5` into `000a02cc`, builds the path from the [career record](../formats/bst-battle-sets.md#the-career-record) at roster + `0x23` + `0x1f7` × word `+7` with `Career_BuildMissionPath` ([a career's missions](../formats/bst-battle-sets.md#a-careers-missions)), loads the mission with `MsnGen_LoadMission`, marks the record's pilots with `CareerRecord_MarkTopPilots` (`00015cbb`), copies the word `Mission_Kind` (`000aad5a`) (header word 3) to the record's `+0xc`, and saves the paths with `Mission_WriteRestore`. `es2_xref.py` finds seven calls to it, each passing the roster `0009b218` and *b* 0, so the word `+7` is 0 and the record is the roster's first, at `0009b23b`, the record [mode 1's flags](../formats/msn-mission-file.md#the-flags) 1 and 2 are seeded from: the campaign and the position.

`CareerRecord_MarkTopPilots` orders the pilot entries 1, 2 and 3 (the table at `0007ec9c`) by their word `+0xd`, greatest first, with a bubble sort swapping through `Word_Swap` (`00015c93`), and sets the byte `+0xa` of the entry in place *p* (0 to 2) to 1 when *p* + 1 is less than `Squad_MemberCount` (`0007ebc8`), the number of the mission's first group's members ([loading a mission](../formats/msn-mission-file.md#loading-a-mission)), and to 0 otherwise. So the career's pilots with the greatest ranks fill the squad's places after the player's.

## Functions

| ES1 | Address |
|---|---|
| `CareerMenu_OnMessage`, `DeleteAlert_OnMessage`, `NewCareerAlert_OnMessage`, `CareerRecordsMenu_OnMessage`, `CareerStatsAlert_OnMessage`, `SaveWarningAlert_OnMessage` | `0002aa0e`, `0002ae96`, `0002aeec`, `0002af94`, `0002b2cc`, `0002b30a` |
| `Words_RandomPermutation`, `Career_New`, `Roster_ShuffleNameGroups`, `Roster_InitSquadPilots` | `000162f4`, `000163ac`, `00016677`, `000166ed` |
| `Roster_SaveSlotUsed`, `Roster_SaveBackup`, `Roster_RestoreBackup`, `Roster_NthUsedRecord`, `Career_Save` | `000167a1`, `000167cd`, `00016817`, `00016845`, `000168b0` |
| `Career_LoadCurrent`, `Roster_ActivateRecord`, `Career_StartMission`, `Roster_AdvanceCareer` | `00016c1e`, `00016d45`, `00016e93`, `00016f22` |
| `maybe_Roster_ReadPlayerEntry`, `Roster_ForEachPilotNoOp`, `Roster_GetRecordWord2`, `Roster_CountUsedRecords` | `00016fb0`, `00016fdd`, `00017014`, `00017026` |
| `CareerStatsHotspot_Draw`, `Rect_CopyCtorCareerCopy` | `0001705e`, `000175a9` |
| `Roster_SaveSlotText`, `Roster_BackupListLines`, `CareerLog_NameLines`, `Career_StaticInit` | `0001838d`, `000183bd`, `000184ff`, `00018629` |
| `MTEditBox_Ctor`, `MTEditBox_GetText`, `MTEditBox_SetTrackPoint`, `MTEditBox_Dtor`, `MTEditBox_Draw` | `0001f442`, `0001f647`, `0001f656`, `0001f66f`, `0001f738` |
| `MTEditBox_PlaceCursor`, `MTEditBox_UpdateCaret`, `MTEditBox_TypeChar`, `MTEditBox_HandleKey` | `0001f85f`, `0001f9a0`, `0001fa19`, `0001fa7e` |
| `HercList_Skip`, `HercList_Copy` | `00028db6`, `000292da` |

## Globals

| | Address |
|---|---|
| `Career_Roster`, the roster | `0009b218` |
| `Career_Log`, the careers log | `0009b20c` |
| `Career_SelectedEntry`, `int16`: the career menu's selected line | `0007fcea` |
| `Career_BackupLine`, `int16`: the backup list's selected line | `000809b4` |
| `Career_Salvage`, `int32`: the career's salvage in kilograms | `0007fcf0` |
| `Roster_BackupLines`, 41 buffers of 25 bytes | `000aa7d4` |
| `CareerLog_NameBuffers`, the career list's name buffers | `0007edfc` |
| `Career_CampaignNumbers`, `Career_CampaignStarts`, eight `int16` each | `0007ebe0`, `0007ebf0` |

## Open

- **Open:** what the roster's byte `+0` marks, which `Career_New` sets to 1 and `Roster_AdvanceCareer` clears.
- **Open:** what the career record's word `+2` (the roster's `+9`) counts, which a new campaign clears.
- **Open:** what calls `maybe_Roster_ReadPlayerEntry` (`00016fb0`) and `Roster_GetRecordWord2` (`00017014`): `es2_xref.py` finds no branch, stored pointer or vtable slot holding either.
- **Open:** whether SAVE can run on a career with 40 missions played, which names record 41, past the roster's 41 records: the career menu's SAVE/RESTORE loads a career without `Career_PeekNextCampaign`'s test.
- **Open:** which of `DUIWindow_KeySubscriber`, for CANCEL's `Esc`, and `Shell_ScanKeySubscriber`, for the name box's, the keyboard gives `Esc` to first in the REGISTRATION window; whether a key's release reaches the name box, which `DUIWindow_SendKey` would hand it as the press's code with bit 7 cleared; and what the Num Lock pass-on leaves of the box's `Home`, `Left`, `Right` and `End`.
- **Open:** a retail check under DOSBox-X: NEW, a name typed, ACCEPT plays the campaign's talking head; the career menu then lists the name; SAVE/RESTORE with SAVE pressed lists CAMPAIGN 1: MISSION 1, and SAVE again shows the warning with that line; `Left` pressed once in the name box moves the caret one character.
