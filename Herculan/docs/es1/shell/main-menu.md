# GO.EXE — the main menu

**The main menu is the first menu after the intro: a window titled MAIN MENU with six buttons, INSTANT ACTION >, SINGLE MISSION, RESUME, CAREER, OPTIONS and EXIT TO DOS, and a small box that shows the name of the current career. INSTANT ACTION loads a random single mission and goes through a message and the war map to the mission; SINGLE MISSION and CAREER open their menus; RESUME, greyed out while there is no career, picks up the current career where it stood; OPTIONS opens the options menu; EXIT TO DOS quits at once. The quit keys instead ask first, with EXIT TO DOS? and its CANCEL and PROCEED buttons.** The menu is built, opened, drawn and left as every menu is ([the menus](menus.md)); this page holds what its buttons and its list do.

Addresses are `ES1GO` image addresses ([the X-32 executable](../formats/x32-executable.md#the-image)). The functions here remove their own arguments unless a line says otherwise, so argument lists are in stack order ([calling conventions](../formats/compiler.md#calling-conventions)). ES2's main menu is a different set of buttons built by code ([ES2's main menu](../../retail/shell/main-menu.md#the-main-menu)); nothing here is ported from it.

## Where the object files start and end

**The memory log's file** runs from `0002a3d8`, after the `RET 0x4` at `0002a3d3` and two zero bytes, to `0002a4e9`, whose `RET` is followed by two zero bytes. It holds `Shell_ShowDrawPage` (`0002a3d8`), which stores the shell display `000ab616`'s draw page `+0x38` as its visible page `+0x30` and, while it is the current display, passes it to the raster's routine at `000865f8` through `00084486` ([setting a page](../rendering/screen-pages.md#the-pages)), and five functions over the file `MemLog_File` (`00080944`):

- `MemLog_Open` (`0002a41a`) opens `memory.log` with `fopen` in mode `w` and answers whether it opened;
- `MemLog_Printf` (`0002a443`)(format, …) writes with `vfprintf` while `MemLog_File` is set;
- `MemLog_New` (`0002a46a`)(size, pointer, name) writes `new %20s %p size %d` with the name, the pointer and the size;
- `MemLog_Delete` (`0002a49f`)(pointer, name) writes `delete %14s %p` with the name and the pointer;
- `MemLog_Close` (`0002a4ce`) closes it with `fclose` while `MemLog_File` is set, and leaves it set.

`es2_xref.py` finds no reference to any of the six ([Open](#open)).

**The menu handlers' file** starts at `0002a4ec`, `Shell_DeleteMissionMap`. It holds the mission-map helpers, the handlers of four small screens and the main menu's, `Disk_HasFreeBytes` (`0002a9ad`) and, from `0002aa0e`, the career menu's handler. Where it ends is past this page's code ([Open](#open)).

## The main menu

**`dlg\menu1000.dlg` lays out a menu window titled MAIN MENU, six text buttons in a column and the list box 1002; `eng\menu1000.bin` holds the title and the buttons' texts.** The window is (49, 50)–(281, 165) on the screen and loads `res32\menu1000.dba` ([the window](menus.md#the-window)); the buttons are text buttons with no key ([buttons](menus.md#buttons)), so the menu opens with the pointer on the first, INSTANT ACTION > ([opening a menu](menus.md#opening-a-menu--menu_open-0003969e)). The menu is screen 1000, backdrop 1000, class `MainMenu`, and its handler, slot `+8` of `MainMenu`'s vtable (`00081bf4`), is `MainMenu_OnMessage` (`0002a70a`) ([the screens](menus.md#the-screens)). It has no control 103 and no control with a key, so opening it presses nothing, its key list is empty and `DUIWindow_KeySubscriber` is registered for no code; no control holds the input, so typed characters reach none; and it has no kind-2 record, so no `Shell_ScanKeySubscriber` is registered ([the menus' input](menu-input.md)).

### The buttons

**A button's release posts its id; `MainMenu_OnMessage` acts on it, and `Shell_HandleMessage` then opens the screen of that number ([changing screens](menus.md#changing-screens--shell_handlemessage-0003c7d8)).** The handler looks the message's target `+6` up among the six ids of the `REPNE SCASD` table at `000809b8`, its cases at `000809d0`, and does nothing for any other id. Each case but EXIT TO DOS sets `Shell_ReturnScreen` (`0007fce8`), the screen a later RETURN or CANCEL goes back to ([the return screen](#the-return-screen)).

| Button | Id | The handler | Then |
|---|---|---|---|
| INSTANT ACTION > | 1499 | `Shell_Mode` (`000aad56`) 0, `Shell_ReturnScreen` 1000, the single-mission index `Mission_Kind` (`000aad5a`) −1; `SingleMission_Start` (`00036807`); `Shell_PrepareMission` (`0002a523`); `anm\esiege3.anm` into `000aacfe` with the byte `0007fcd7` 1 | screen 1499, the message, then the war map ([the path to the mission](#instant-actions-path-to-the-mission)) |
| SINGLE MISSION | 1100 | `Shell_Mode` 0, `Shell_ReturnScreen` 1100 | [the SINGLE MISSION menu](single-mission.md#the-single-mission-menu), `MissionMenu` |
| RESUME | 1700 | `Shell_Mode` 1, `Shell_ReturnScreen` 1000, `Mission_Kind` (`000aad5a`) −1, then [the career's screen](#resume) | 1250, 1260, 5555 or the career's screen |
| CAREER | 1200 | `Shell_Mode` 1, `Shell_ReturnScreen` 1200 | the career menu, `CareerMenu` |
| OPTIONS | 1300 | `Shell_ReturnScreen` 1000 | the options menu, `OptionsMenu` |
| EXIT TO DOS | 1003 | `Shell_ExitCode` (`0007fcac`) 0, the byte `Shell_QuitFlag` (`0007fd05`) 1 | no screen: 1003 is not in `Shell_ScreenIds`, so the main menu stays up and the frame loop ends at its next test ([the ways out](startup.md#the-ways-out)) |

The mode decides how a mission loads ([the mode](../formats/msn-mission-file.md#the-mode-and-the-mission-parameters)). INSTANT ACTION's `Mission_Kind` (`000aad5a`) of −1 makes `SingleMission_Start` draw the list and the mission at random ([starting the mission](single-mission.md#starting-the-mission)), and its load writes `data\script.dat` before the button's message has changed the screen ([loading a mission](../formats/msn-mission-file.md#loading-a-mission)). The `.ANM` set with `0007fcd7` 1 plays when the next screen is drawn, after its palette is uploaded ([drawing a screen](menus.md#drawing-a-screen)).

**`Shell_PrepareMission` (`0002a523`)** runs after every mission load the shell's menus make, the main menu's, the SINGLE MISSION menu's, the career screens' and the service bay's. It deletes the mission map with `Shell_DeleteMissionMap` (`0002a4ec`), writes the mission's objectives from its `.eng` path `000aad2a` to `data\object.str` with `Mission_WriteObjectives` ([the objectives](../formats/eng-mission-text.md#the-objectives)), starts `maybe_MouseEventQueue_Start` (`00010559`), clears the byte `0007e5df`, and builds a new mission map, `Shell_MissionMap` (`000809a4`), with `000094d9`(0, `000a02d4`), a `0x4c8`-byte object that reads `data\object.str` and the `maplabel` resources. `Shell_DeleteMissionMap`, with a map, calls `maybe_MouseEventQueue_Stop` (`000106af`), `0000a9c8`(map), destroys it with `00009a73`(map, 3) and clears `Shell_MissionMap`. The war map and its alerts delete it when they leave for the mission or back.

### RESUME

**RESUME is inactive, drawn in the inactive look and passed over by the pointer, while the careers log holds no career ([opening a menu](menus.md#opening-a-menu--menu_open-0003969e), step 3). Pressed, it loads the current career once a session and goes to the screen the career stood at: the service bay, the war map of its next mission, or a warning when the career is over or its pilot lost.** After setting the mode, the case at `0002a78e`, on the roster `0009b218` and its current record, the `0x1f7`-byte record at `0009b23b` + `0x1f7` × the word `0009b21f` ([the career record](../formats/bst-battle-sets.md#the-career-record)):

1. While the byte `Career_Loaded` (`000809aa`) is clear: `Career_LoadCurrent` (`00016c1e`)(roster) reads the current career's file. When `Career_PeekNextCampaign` (`00015aa5`)(record) gives −1, the career failed, or `c`, the career won, the target becomes 1260 and `Shell_ReturnScreen` 1000, and the case ends. Otherwise the target becomes the record's word `+4`, the career's screen; unless that is 1710, and while the current pilot's condition (`PilotEntry_Condition` (`0001618d`)(record, the record's `+0xe`)) is not 0, `Career_StartMission` (`00016e93`)(roster, 0) loads the career's next mission. `000809b4` becomes `Roster_CountUsedRecords` (`00017026`)(roster) − 1, and `Career_Loaded` is set.
2. When the current pilot's condition is 0, the target becomes 1250 and `Shell_ReturnScreen` 1000. Otherwise the target becomes the record's `+4`; unless it is 1710, `Shell_PrepareMission` runs; when it is 1710 and the record's word `+0xa8` is not −1, the target becomes 5555; and `anm\esiege3.anm` goes into `000aacfe` with `0007fcd7` 1.

Screens 1250 and 1260 are `KIAWarningAlert`, whose handler `KIAWarningAlert_OnMessage` (`0002b34c`) turns message 99 into `Shell_ReturnScreen`, so its back button returns to the main menu. Screen 5555 is `TechReleaseAlert`: `TechReleaseAlert_OnMessage` (`0002a6a9`) turns its message 100 into 1710, sets the record's words `+0xa8` and `+0xaa` to −1 and saves the record with `CareerRecord_SaveCurrent` (`00014f28`). `Shell_ProcessMissionResults` sets the record's `+4` to 1710 after a mission ([after a mission](../formats/bst-battle-sets.md#after-a-mission)), so a career resumed after a mission opens the service bay, screen 1710, without a load. The career menu's handler (`0002aa0e`) also sets `Career_Loaded`, and `0002ae96`, screen 1270's handler, clears it when it deletes a career.

### The list

**The box at the top of the window shows the current career's name in green, or nothing with no current career, and does nothing when clicked.** It is `dlg\menu1000.dlg`'s kind-15 record, a list box ([the list box](menus.md#the-list-box)) of style 1 at (112, 47)–(196, 59) in the window, built with the line 0 selected: its id 1002 is not among the 23 ids at `00081a00` that set their own ([the kinds](../formats/dlg-menu-dialogs.md#the-kinds)). Its lines are `Menu_ListLines` (`0003750d`)(1002), which for 1002 returns `CareerLog_CurrentNameArray` (`000182f8`)(the careers log `0009b20c`): one line, the current career's name, or none when the log has no current entry ([the careers log](../formats/career-log-and-player-mec.md#writing)).

How a list box draws its lines and takes a press is [the list box's](menus.md#the-list-box). On the main menu, with style 1, the list never shows a selection and a press changes nothing on it. The release posts (0, the list's index, its selected line, 1002); `MainMenu_OnMessage`'s table does not hold 1002 and `Shell_ScreenIds` does not either, so nothing follows.

### The return screen

**`Shell_ReturnScreen` (`0007fce8`) is the screen the options menu's RETURN button and the KIA warning's back button go to, and it names that button.** The main menu's cases set it to 1000, 1100 or 1200, and `Shell_OpenHomeMenu` (`0003b3db`) to the home menu's number ([the first screen](menus.md#the-first-screen--shell_openfirstscreen-0003ba47)). `es2_xref.py --decode` finds its other writers in `AnyDisabledAlert_OnMessage` (`0002a581`), 1200, and the career menu's handler `0002aa0e`, 1000 and 1200, and its readers in `KIAWarningAlert_OnMessage`, `OptionsMenu_OnMessage` (`0002b79d`), `0002c71a`, `Menu_BuildFromDlg` (`0003a1f7`) and eleven places in code Ghidra left undefined ([Open](#open)).

- **The options menu's RETURN button**, 100: `OptionsMenu_OnMessage` turns it into `Shell_ReturnScreen`, or 1200 when that is 1710. When `Menu_BuildFromDlg` builds menu 1300's button 100, it takes the button's text by `Shell_ReturnScreen` among the four values at `000819e0`: 1000 the record's own line of `eng\menu1300.bin`, RETURN TO MAIN; 1100 the next, RETURN TO MISSION; 1710 and 1200 the one after, RETURN TO CAREER. For any other value it reads no line and the button keeps the text pointer an earlier record left.
- **The options menu's other buttons**: 1301, VIEW DEMO, becomes 9999 with `Shell_DemoRequested` (`000817ce`) set ([the ways out](startup.md#the-ways-out)); 1302, CREDITS, becomes 1300 with the byte `000809a9` 2, which plays the credits ([changing screens](menus.md#changing-screens--shell_handlemessage-0003c7d8), step 7), and `anm\esiege2.anm` into `000aacfe` with `0007fcd7` 2.

## INSTANT ACTION's path to the mission

**INSTANT ACTION > shows a message over the instant-action picture, waits for a key or a click, opens the war map with the mission's briefing, and BEGIN MISSION > there ends the shell with exit code 2, which starts the simulator.** In order:

1. The release of button 1499 posts (0, its index, 1, 1499) ([a message's path](menu-input.md#a-messages-path)); the frame loop's step 4 hands it to `Shell_HandleMessage`, which runs `MainMenu_OnMessage`: the mission loads and its map is built ([the buttons](#the-buttons)).
2. `Shell_HandleMessage` closes the main menu, shows backdrop 3000, the group of screen 1499 ([the backdrop](menus.md#the-backdrop)), and runs 1499's case, which makes no menu and puts up the message box `Shell_MessageBox` (`0009b010`) with kind 0 in `Shell_Mode` 0 ([the screens](menus.md#the-screens)). Its `Shell_DrawScreen` shows the message, line `000ab222` of `eng\C0_text.bin` with the lip-sync list `C0` ([which file and line](../formats/bin-string-tables.md#which-file-and-line)), and plays `anm\esiege3.anm`.
3. In the frame loop's step 5, `MessageBox_Run` (`000382f6`)(`Shell_MessageBox`, the input block) calls the routine at `0008662c` and holds the frame loop in a loop of its own while the box is up: `Shell_PresentFrame`, `maybe_SncList_Tick` (`0009afd4`), `Ttm_RunAll` and the text box `00018dcb`(`0009afc8`) each pass, until `Input_PollDeviceBlock` gives a key (word `+0`) or the first button (byte `+0xa`). Then `MessageBox_Answer` (`0003837f`) posts the message (0, −1, 0, the kind's screen from `MessageBox_NextScreen` (`00081690`)), 1501 for kind 0, closes the box with `MessageBox_Close` (`000382d5`), which sets its kind to −1 and deletes the dialog `0009afc8`'s box, and calls the routine at `00086628`.
4. The next step 4 opens screen 1501, the war map, `WarMapMenu`, on backdrop 6000, which shows the mission's briefing ([the briefing](../formats/eng-mission-text.md#the-briefing)). `dlg\menu1501.dlg` gives it ARMING (1800), CANCEL (99, key `Esc`), BEGIN MISSION > (100), the map area 1502, and icon buttons 101–109 and 1503–1506.
5. BEGIN MISSION >: `WarMapMenu_OnMessage` (`0002c057`) turns 100 into 3333 when the player's pilot has no HERC (`CareerRecord_PlayerHasNoHerc` (`000161dc`) of `CareerRecord_Current` (`00014ef4`)), into 3000 when `0001620d`(record) answers non-zero for one of its pilots, and otherwise writes `data\player.mec` with `PlayerMec_Write` (`0002299e`), turns it into 9999, deletes the mission map and clears the byte `000809a8`. Screen 3000, `UnArmedAlert`, turns its own 100 into 9999 in `Shell_Mode` 0, and into 1700 with message box kind 6 in mode 1, and deletes the mission map and clears `000809a8` (`UnArmedAlert_OnMessage`, `0002a650`).
6. Screen 9999 sets `Shell_ExitCode` to 2, makes the loading screen `SimLoadMenu` and draws it ([changing screens](menus.md#changing-screens--shell_handlemessage-0003c7d8)); the next pass's step 3 sets `Shell_QuitFlag`, the loop ends, the shutdown writes the code to `data\exit.cfg`, and `Shell_Main` returns 2 ([the ways out](startup.md#the-ways-out)).

CANCEL on the war map (99) goes to the SINGLE MISSION menu, 1100, in `Shell_Mode` 0, so an instant action's way back from its briefing is that menu, not the main menu; in mode 1 it clears the record's byte `+1`, sets its `+4` to 1500, saves the roster with `000168b0` and goes to the career menu, 1200. Either way it puts `anm\esiege2.anm` into `000aacfe` with `0007fcd7` 2, deletes the mission map and clears `000809a8`.

The message box (`Shell_MessageBox`, `0009b010`): `MessageBox_SetKind` (`00037eac`)(box, kind) stores the kind at `+0` and, for −1, clears the bytes `+2`, *to show*, and `+3`, *up*, and for any other kind sets both. `Shell_DrawScreen` shows a box whose `+2` is set ([drawing a screen](menus.md#drawing-a-screen), step 3), and `+3` keeps the window, the mouse's presses and releases and the screen's tick off while it is up. `MessageBox_NextScreen` holds, for kinds 0 to 11: 1501, 1500, 1700, 1510, 1520, 1490, 9999, and 1520 for 7 to 11. `MessageBox_Answer` posts that screen for every kind but 2 and 7, which turn the page of a multi-page message ([Open](#open)).

## The quit question

**`Ctrl+Esc`, `Alt+Esc`, `Ctrl+Q`, `Alt+Q`, `Ctrl+X` or `Alt+X` on any screen puts up a window over the current picture titled EXIT TO DOS?, with the line "Are you sure you want to quit?" and two buttons, CANCEL and PROCEED; the pointer starts on PROCEED. PROCEED quits to DOS; CANCEL puts back the screen the keys were pressed on.** The keys set `Shell_QuitRequested` ([the input](startup.md#the-input)), and the frame loop's step 6 calls `Shell_OpenQuitAlert` (`000408cc`) ([the frame loop](startup.md#the-frame-loop)), which sets the byte `000817bd` to 3 while the byte `000809a8` is set and to 0 otherwise and then, unless the current screen is 999 already:

1. with a current menu, keeps `Shell_PreviousScreen` (`000817c2`) in `Shell_ResumeScreen` (`000817be`), closes the menu with `Menu_Close`, destroys it through its slot `+4` with 3 and clears `Shell_CurrentMenu` (`000817c8`). On a screen whose case makes no menu, 1499 among them, `Shell_ResumeScreen` keeps −1;
2. clears `Shell_BackdropChanged` (`000817c6`), makes a `0x26`-byte `QuitAlert` (vtable `00081e48`, over `Menu`'s and the shell's region classes'), makes it the current menu, and opens it with `Menu_Open`: as menu 888 on backdrop 1000; on backdrop 4000 as menu 999 after loading `regn4000`'s palette again with `ShellBackdrop_LoadPalette` (`0003950a`)(`Shell_Backdrop`, 4000); on any other as menu 999 with no palette load;
3. sets `Shell_PreviousScreen` to the current screen and `Shell_CurrentScreen` (`000817c0`) to 999, and draws the screen with `Shell_DrawScreen`.

It does not change the backdrop or the music. A message naming screen 999 takes another path: `Shell_HandleMessage`'s case for 999 (`0003d553`) makes the `QuitAlert` the current menu, and its step 10 opens it as menu 999 whatever the backdrop, with no 888 and no palette load ([changing screens](menus.md#changing-screens--shell_handlemessage-0003c7d8)). Menus 888 and 999 differ only in their window: `dlg\menu888.dlg`'s is (49, 50)–(281, 165), the main menu's rect, with the bitmaps `res32\menu1240.dba`; `dlg\menu999.dlg`'s is (49, 45)–(270, 152) with `res32\menu999.dba`. Each then holds a kind-1 label at (50, 47)–(184, 57), the button 99 at (26, 89)–(108, 101) and the button 100 at (115, 89)–(197, 101) with the key `Space`, and its `eng\menu<n>.bin` the lines EXIT TO DOS?, Are you sure you want to quit?, CANCEL and PROCEED. The `Space` key puts the focus and the pointer on PROCEED when the menu opens ([opening a menu](menus.md#opening-a-menu--menu_open-0003969e), step 4), and a `Space` press presses it. Windows 999 and 1240 draw their label in `nm_green` (`000aacda`), where most windows use `cpylw` ([the window](menus.md#the-window)).

The answers:

- **PROCEED**, 100: `QuitAlert_OnMessage` (`0002a554`) sets `Shell_ExitCode` to 0 and `Shell_QuitFlag`; 100 is not in `Shell_ScreenIds`, so the question stays up until the frame loop ends at its next test ([the ways out](startup.md#the-ways-out)).
- **CANCEL**, 99: `QuitAlert_OnMessage` does nothing with it, and `Shell_HandleMessage`'s *back* step turns it into `Shell_PreviousScreen`, the screen the question was opened on, and puts `Shell_ResumeScreen` back in `Shell_CurrentScreen` when it is not −1 ([changing screens](menus.md#changing-screens--shell_handlemessage-0003c7d8), step 3); from a screen with no menu, that step finds −1 and the target alone sends it back. The question closes and that screen's case builds and opens its menu again, with its own previous screen in place for its own *back*.

## Functions

| ES1 | Address |
|---|---|
| `Shell_ShowDrawPage`, `MemLog_Open`, `MemLog_Printf`, `MemLog_New`, `MemLog_Delete`, `MemLog_Close` | `0002a3d8`, `0002a41a`, `0002a443`, `0002a46a`, `0002a49f`, `0002a4ce` |
| `Shell_DeleteMissionMap`, `Shell_PrepareMission` | `0002a4ec`, `0002a523` |
| `QuitAlert_OnMessage`, `AnyDisabledAlert_OnMessage`, `UnArmedAlert_OnMessage`, `TechReleaseAlert_OnMessage`, `MainMenu_OnMessage` | `0002a554`, `0002a581`, `0002a650`, `0002a6a9`, `0002a70a` |
| `Disk_HasFreeBytes`, `KIAWarningAlert_OnMessage`, `OptionsMenu_OnMessage`, `WarMapMenu_OnMessage` | `0002a9ad`, `0002b34c`, `0002b79d`, `0002c057` |
| `Career_LoadCurrent`, `Roster_CountUsedRecords` | `00016c1e`, `00017026` |
| `maybe_MouseEventQueue_Start`, `maybe_MouseEventQueue_Stop` | `00010559`, `000106af` |
| `WaText_LinesFromList`, `Menu_ListLines` | `000373fa`, `0003750d` |
| `MessageBox_SetKind`, `MessageBox_Close`, `MessageBox_Run`, `MessageBox_Answer` | `00037eac`, `000382d5`, `000382f6`, `0003837f` |
| `Shell_OpenQuitAlert` | `000408cc` |

## Globals

| | Address |
|---|---|
| `Shell_ReturnScreen`, `int16` | `0007fce8` |
| `Shell_MissionMap`, the mission map | `000809a4` |
| `Career_Loaded`, byte | `000809aa` |
| `Shell_ResumeScreen`, `int16` | `000817be` |
| `Shell_MessageBox`: `int16` kind, bytes *to show* `+2` and *up* `+3` | `0009b010` |
| `MessageBox_NextScreen`, 12 `int16` | `00081690` |
| `MemLog_File` | `00080944` |

## Open

- **Open:** what calls `Shell_ShowDrawPage` and the memory log's five functions: `es2_xref.py` finds no branch, stored pointer or vtable slot holding their addresses. A retail check: a breakpoint on `MemLog_Open` (`0002a41a`) under DOSBox-X's debugger, or a look for `MEMORY.LOG` beside `GO.EXE` after a session.
- **Open:** where the menu handlers' file that starts at `0002a4ec` ends, past `0002aa0e`.
- **Open:** what the mission map `000094d9` builds is and draws, what `0000a9c8` and `00009a73` do to it, what `maybe_MouseEventQueue_Start` (`00010559`) queues for it (it registers `00010900` with the mouse driver for the events `0x1f` and allocates two arrays of `0009adf0` 14-byte entries), and what the byte `0007e5df` is.
- **Open:** what reads `000809b4`, which RESUME and the career menu set to `Roster_CountUsedRecords` − 1, and what `Career_LoadCurrent`'s last call `00016d45` does; what `0001620d` tests of a record's pilots that sends the war map to 3000 (it asks `0002a354` of each pilot's slot), and `000168b0` and `00016f22`, which the war map's CANCEL and `AnyDisabledAlert_OnMessage` call on the roster.
- **Open:** the readers of `Shell_ReturnScreen` in code Ghidra left undefined (at `0002b223`, `0002b2bf`, `0002c682`, `0002c6a6`, `0002c70d`, `0002d393` and `0002d3f8`) and `0002c71a`'s.
- **Open:** `MessageBox_Answer`'s kinds 2 and 7, which step `000814e8` through a multi-page message and run `.TTM` scripts by the career's campaign; `MessageBox_SetKind`'s caller `00020b68`, the static initializer; and the war map handler's other cases, the zoom buttons 108 and 109 on the map's object, ARMING (1800, to 1820 while the byte `0007fd09` is set), 1700, and the four icon buttons 1503 to 1506 at `0002c129`–`0002c280` in code Ghidra left undefined.
- **Open:** a retail check under DOSBox-X: with no career saved, RESUME is drawn greyed and a click on it does nothing; `Ctrl+Q` on the main menu shows EXIT TO DOS? with the pointer on PROCEED, CANCEL brings the main menu back, and PROCEED returns to the DOS prompt; INSTANT ACTION > shows a message over a new picture, a key opens the war map, and CANCEL there opens SINGLE MISSION rather than the main menu.
