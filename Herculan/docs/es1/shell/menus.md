# GO.EXE — the menus

**Every screen of ES1's shell between the intro and a mission is a menu: a picture behind it, a framed window with a title, labels and buttons, laid out by the menu's `.DLG` file ([the menu dialogs](../formats/dlg-menu-dialogs.md)). A button is pressed with the mouse or with `Enter`, and acts when it is let go over the same button; its number names the next screen. The shell then closes the open menu, changes the picture and the music behind when the next screen belongs to another group, builds the next menu and draws it.** What a player would see:

- pointing at a button gives it the focus, and `Tab`, `Shift+Tab` and `Alt+Tab` move the focus to the next or previous button and put the pointer on it ([the keys](menu-input.md#the-keys));
- a button pressed and then dragged off pops back up, and acts only if the pointer comes back over it before the release ([the mouse](menu-input.md#the-mouse));
- a pressed text button clicks when the byte `0007fcb9` is set, which `prefs.cfg` option 1 sets ([drawing a control](#drawing-a-control));
- a menu opens with the pointer on the button whose key is `Space`, or on its first control ([opening a menu](#opening-a-menu--menu_open-0003969e)).

Addresses are `ES1GO` image addresses ([the X-32 executable](../formats/x32-executable.md#the-image)). The functions here remove their own arguments, so argument lists are in stack order ([calling conventions](../formats/compiler.md#calling-conventions)). The frame loop that runs a screen is [the start-up's](startup.md#the-frame-loop), and how the open menu takes the mouse and the keys is [the menus' input](menu-input.md).

## Where the object files start and end

**`shell.cpp`** runs from `Shell_StockItemsFromFlags` (`00038e48`) to `00040b93`. The file before ends with its static initializer `DamageValues_StaticInit` (`00038e2f`) (the static constructor table's entry `0009a264`), whose `RET` at `00038e46` is followed by one zero byte. `Shell_ProcessMissionResults`' asserts name `src\shell.cpp` (lines `0xc7` and `0xd7`). Its last code is its static initializer, `Shell_StaticInit` (`00040b74`, entry `0009a268`), which builds [the backdrop](#the-backdrop), and the exit routine `Shell_StaticDtor` (`00040b83`, exit table entry `0009a2a0`), which destroys it; that `RET` ends at `00040b93`, on a 4-byte boundary. `00040b94`, a stub taking three arguments and returning the first, follows, and then the word stacks from `WordStack_Push` (`00040ba6`), whose last `RET 8`, at `00040d50`, is followed by one zero byte and `DirtyList_CreateContext` (`00040d54`) ([Open](#open)). The file's data holds the class names and vtables of its screens from `00081bd0` to `00082058`, and its copies of `ClassItem`'s, `GLBase`'s, `GLRegion`'s and `GLRectangleRegion`'s vtables (`0008186c`, `00081896`, `000818c8`, `00081900`) with the three destructors `GLBase_DtorShellCopy` (`00039342`), `GLRegion_DtorShellCopy` (`0003936e`) and `GLRectangleRegion_DtorShellCopy` (`000393b7`) in their slot `+0x1c`.

**The window file** runs from `0001a4f4`, after `HercStatus_Set`'s `RET 0x10` at `0001a4f0` and one zero byte, to `DUIWindow_PointAtControl` (`0001c3ec`), whose `RET 8` at `0001c45c` is followed by one zero byte. It holds `DUIControl` and `DUIWindow`, with its own copies of the region classes' vtables (`0007f00c`, `0007f036`, `0007f068`, `0007f0a0`) and their destructors (`GLBase_DtorWindowCopy`, `0001aec1`, and the two after it).

**The active window's file** runs from `ShellMouse_AddCallbacks` (`0001c460`) to `DUIWindow_HandleNavKey` (`0001cc86`), whose `RET 4` at `0001cde4` is followed by one zero byte. It holds the active window and the mouse's callbacks.

**The button file** starts at `MTButton_Ctor` (`0001cde8`). It holds a second copy of `DUIControl`'s vtable (`0007f1ea`), `DUIButtonControl` (`0007f130`), `MTButton` (`0007f18c`), `MTBmpButton` (`0007f24a`), and the slider and list classes from `DUISliderControl` (`0007f2f8`) on ([Open](#open)).

The image stores each class's name just before its vtable, and the word before the vtable is the name's offset back from it: `Menu`'s vtable `00081bd8` has −8 at `00081bd6`, its name at `00081bd0`. The classes here, `DUIWindow`, `MTWindow`, `DUIControl`, `DUIButtonControl`, `MTButton`, `MTBmpButton`, `MTStrListBox` and the screens', go by those stored names, and so do their functions.

## The screens

**A screen is a number. The numbers a message may name are the 41 of the zero-ended list `Shell_ScreenIds` (`000817e0`), and each has a case of its own in the table at `00082060`, though some cases (1490, 1499, 1320) make no menu.** The table holds the same 41 numbers as the list, in another order, followed by their targets at `00082104`, case bodies of `Shell_HandleMessage` (`0003c7d8`). Most cases allocate a `0x26`-byte screen object, store their class's vtable, and make it the current menu, then jump to the shared step at `0003f850`, where `Shell_HandleMessage` opens it with `Menu_Open`. The cases, in the table's order: 9999 `0003cab5`, 1490 `0003cccc`, 1499 `0003ccdd`, 1520 `0003cd09`, 1510 `0003cd27`, 3000 `0003cd38`, 3333, 6666, 7770 and 7772 one case `0003ced7`, 7771 `0003d076`, 4444 `0003d215`, 5555 `0003d3b4`, 999 `0003d553`, 1000 `0003d6f2`, 1200 `0003d891`, 1210 `0003da30`, 1220 `0003dbcf`, 1230 `0003dd6e`, 1240 `0003df0d`, 1250 and 1260 `0003e0ac`, 1270 `0003e24b`, 1100 `0003e3ea`, 1110 `0003e589`, 1310 `0003e7eb`, 1320 `0003e9ae`, 1300 `0003ec5d`, 1330 `0003edfc`, 1500 and 1501 `0003ef9b`, 1610 `0003efac`, 1700 and 1710 `0003efcc`, 1800 and 1820 `0003f196`, 1900 `0003f3e8`, 1910 `0003f5a4`, 1920 `0003f5bf`, 2000 `0003f6f8` and 2010 `0003f71a` (`es2_peek.py ES1GO 00082060 41`, `00082104 41`). The handler is the class's slot `+8`:

| Screen | Class (vtable) | Handler | Backdrop |
|---|---|---|---|
| 9999 | `SimLoadMenu` (`00081d7e`) | `SimLoadMenu_OnMessage` (`0003f96d`) | 1000 |
| 1490, 1499 | none: `MessageBox_SetKind` (`00037eac`)(`0009b010`, *n*), with *n* 2 for 1490 and for 1499 0 in `Shell_Mode` 0 and 1 otherwise ([the kinds](message-box.md#the-kinds)) | | 7000, 3000 |
| 1510 | `DebriefingMenu_Ctor` (`0002c422`), [the debriefing](debriefing.md) | `DebriefingMenu_OnMessage` (`0002c638`) | by the career |
| 1520 | while the byte `MessageBox_Kind4Shown` (`00081d20`) is 0, `MessageBox_SetKind`(`0009b010`, 4) and the byte set to 1; then as 1510 | `DebriefingMenu_OnMessage` (`0002c638`) | by the career |
| 3000 | `UnArmedAlert` (`00081da0`) | `UnArmedAlert_OnMessage` (`0002a650`) | |
| 3333, 6666, 7770, 7772 | `UnAssignedAlert` (`00081dc2`) | `UnAssignedAlert_OnMessage` (`0003fae8`) | |
| 7771 | `AnyDisabledAlert` (`00081de8`) | `AnyDisabledAlert_OnMessage` (`0002a581`) | |
| 4444 | `ScrapAlert` (`00081e06`) | `0002f111` | |
| 5555 | `TechReleaseAlert` (`00081e2c`) | `TechReleaseAlert_OnMessage` (`0002a6a9`) | 4000 |
| 999 | `QuitAlert` (`00081e48`) | `QuitAlert_OnMessage` (`0002a554`) | |
| 1000 | `MainMenu` (`00081bf4`) | `0002a70a` | 1000 |
| 1200 | `CareerMenu` (`00081c12`) | `CareerMenu_OnMessage` (`0002aa0e`) | 1000 |
| 1210 | `NewCareerAlert` (`00081e6a`) | `NewCareerAlert_OnMessage` (`0002aeec`) | |
| 1220 | `CareerRecordsMenu` (`00081e90`) | `CareerRecordsMenu_OnMessage` (`0002af94`) | |
| 1230 | `CareerStatsAlert` (`00081eb4`) | `CareerStatsAlert_OnMessage` (`0002b2cc`) | |
| 1240 | `SaveWarningAlert` (`00081ed8`) | `SaveWarningAlert_OnMessage` (`0002b30a`) | |
| 1250, 1260 | `KIAWarningAlert` (`00081efa`) | `KIAWarningAlert_OnMessage` (`0002b34c`) | |
| 1270 | `DeleteAlert` (`00081f1a`) | `DeleteAlert_OnMessage` (`0002ae96`) | |
| 1100 | `MissionMenu` (`00081c3a`), [the SINGLE MISSION menu](single-mission.md#the-single-mission-menu) | `0002b375` | 1000 |
| 1110 | `MissionOptionsAlert` (`00081f42`), [the MISSION PARAMS menu](single-mission.md#the-mission-params-menu): a `0x30`-byte object | `0002b4fe` | |
| 1310 | `PrefsMenu` (`00081f60`) | `0002b82b` | 1000 |
| 1320 | none: code of its own at `0003e9ae`, which changes the keyboard's subscribers and mode | | 1000 |
| 1300 | `OptionsMenu` (`00081f7e`), [the options menu](main-menu.md#the-options-menu) | `OptionsMenu_OnMessage` (`0002b79d`) | |
| 1330 | `BattlesetAlert` (`00081fa2`), [LOAD BATTLESET](main-menu.md#load-battleset) | `0002bb29` | |
| 1500, 1501 | `WarMapMenu_Ctor` (`0002bbbd`), [the war map](war-map.md) | `WarMapMenu_OnMessage` (`0002c057`) | 6000 |
| 1610 | `CrewAssignMenu_Ctor` (`0002c743`), then `ShellBackdrop_LoadPalette`(backdrop, 1610), `res32\regn1610.dpl` | | 4000 |
| 1700, 1710 | `ServiceBayMenu` (`00081fc6`); the dword `0007fce4` set to 0, and backdrop 4000's palette loaded again when the screen left is 1610 ([opening the bay](between-missions.md#the-service-bays-bar)) | `ServiceBayMenu_OnMessage` (`0002d327`) | 4000 |
| 1800, 1820 | `ArmingMenu` (`00081fe6`) | `0002e54c` | 4000 |
| 1900 | `RepairMenu` (`00082006`) | `0002f009` | 4000 |
| 1920 | `0002f1aa` | | |
| 1910 | `RepairPrefsAlert` (`0008202c`) | `0003052f` | |
| 2000 | `0002986f`(3), then `000307c9`(0) makes the menu; sets the dword `0007fce4` to `0008003c` ([the mouse](menu-input.md#the-mouse)) | | 4000 |
| 2010 | `SalvageAllocAlert` (`00082050`) | `00030ed3` | |

Every class derives from `Menu` (`00081bd8`), whose slot `+4` is `Menu_Dtor` (`0003b6d1`) and slot `+8` `Menu_OnMessage` (`0003b764`), which does nothing. Each subclass's slot `+4` is its destructor, `MainMenu_Dtor` (`0003b771`) and the 22 others of the same code ([Functions](#functions)): it stores its vtable, then `Menu`'s, then the region's three, and frees the object when bit 0 of its flags is set. The war map's, `WarMapMenu_Dtor` (`0002bf15`), and the debriefing's, `DebriefingMenu_Dtor` (`0002c574`), do more first ([leaving the war map](war-map.md#leaving-it), [leaving the debriefing](debriefing.md#leaving-it)). The screens other than the main menu's and 9999's are later modules' ([Open](#open)).

### The screen object

| Offset | Holds |
|---|---|
| `+0` | `int16` the menu's number |
| `+2` | the saved background: a buffer of `Region_PixelCount`(`+0xe`) bytes |
| `+6` | the window, kind 0 of the `.DLG` ([the window](#the-window)) |
| `+0xa` | a `GLRectangleRegion`: its vptr, then at `+0xe` the region `Region_Save` and `Region_Restore` take, type 0 with the window's rect at `+0x12`–`+0x21` ([the build](../formats/dlg-menu-dialogs.md#the-kinds)) |
| `+0x22` | the vtable |

### The backdrop

**Behind every menu is a full-screen picture with its own palette and music, one for each group of screens.** The group of a screen is `Shell_BackdropForScreen` (`00040afa`)(current, screen), which looks the screen up among the 20 dwords at `000821a8` and answers 1000 for 1000, 1200, 1100, 1310, 1320 and 9999; 7000 for 1490; 3000 for 1499; 6000 for 1500 and 1501; 5000 plus the word at `0007ea94` + 2 × the current career record's `+6`, its campaign, for 1510 and 1520 (the table's ten words hold 0, 1, 2, 3, 1, 2, 3, 1, 2 and 3 for campaigns 0 to 9, [the campaigns](../formats/bst-battle-sets.md#battle-sets)); 4000 for 1700, 1710, 1900, 1800, 1820, 2000, 1610 and 5555; and *current* for any other. The current group is the word `Shell_BackdropId` (`000817c4`).

`ShellBackdrop_Show` (`000395a0`)(backdrop, group):

1. sets the word `Shell_MusicTrack` (`00081938`) to the word at `000817d0` + 2 × (group / 1000 − 1), one of 1, 1, 3, 4, 3, 3, 1 and 0 for groups 1000 to 8000; when that differs from the word `00081936` it sets the byte `0007fcd5`, so that [presenting the next frame](../rendering/screen-pages.md#presenting-a-frame) starts that track with `ShellMusic_Play` (`00020a70`), and copies it to `00081936`;
2. destroys the previous picture `ShellBackdrop_Picture` (`000aa7ac`) when there is one (slot `+0x1c`, flags 3);
3. `ShellBackdrop_LoadPalette` (`0003950a`)(backdrop, group): destroys the palette `0007fcd0` when there is one, loads `res32\regn<group>.dpl` with `Resource_LoadPalette` into it, group 3000 taking 3000 plus the word at `0007ea94` + 2 × the career record's `+6`, and sets the byte `0007fcd4`, so the next frame uploads it;
4. loads `res32\regn<group>.dbm` with `Resource_LoadBitmap` into `ShellBackdrop_Picture` and gives it to `ShellBackdrop_SetPicture` (`00021334`)(backdrop + 4, 0, 0, bitmap), which draws it at (0, 0) on page 2, the background ([the pages](../rendering/screen-pages.md#presenting-a-frame)), and sets the region's rect to the bitmap's;
5. opens the group's scripts with `ShellBackdrop_OpenGroupTtms` (`00011f19`)(group), below, and for group 4000 calls `HangarBay_AssignScripts` (`000122e0`).

**The group's scripts.** `ShellBackdrop_OpenGroupTtms` (`00011f19`)(group) keeps the scripts of the backdrop's group open in an array of 3-byte entries, the dword `ShellBackdrop_GroupTtms` (`0007e718`), their count the word `ShellBackdrop_GroupTtmCount` (`0007e71c`). When the count is above 0 it deletes the array with `__vec_delete` and the entries' destructor `TtmEntry_Dtor` (`00011edd`) and clears the dword and the count. It sets the word `ShellBackdrop_GroupTtmList` (`0007e716`) to group / 1000 − 1, and when the count of `cfg\tm_files.dat`'s lists, the byte `0007e714`, is not 0, takes the count of the list `0007e716` of `0007e710` ([the animation lists](../formats/shell-file-lists.md#the-animation-lists)) into `0007e71c`; when that is not 0 it makes that many entries with `__vec_new` and their constructor `TtmEntry_Ctor` (`0001201b`), and opens each name of the list with `Ttm_Open`(0, 0, 0, name), playing on the default render context, its handle into the entry's byte `+0`. A group whose list is empty leaves the dword 0. An entry is a handle byte, −1 from the constructor, and the word `+1`, −99 from it; the destructor closes a handle that is not −1 with `Ttm_Close` and sets it to −1, sets the bytes `+1` and `+2` to −99 and −1, and frees the entry when bit 0 of its flags is set. Message kinds 5 to 11 hide entries 3 and 6 while they are up ([the kinds](message-box.md#the-kinds)).

`ShellMusic_Play` (`00020a70`)(track), with a sound manager (`00082768`), calls `00020a18`, makes the sound object `0007fcfc` with `SoundManager_CreateObject`(manager, 2, 0, track), loads it with `SoundObject_LoadSample`, calls its slot `+0x28` with (−1, −1, `0xffff`), and plays it through slot `+0x10` when the byte `0007fcb8` is set, which `prefs.cfg` option 0 sets ([the shared data](startup.md#the-shared-data)).

The backdrop is the object `Shell_Backdrop` (`000a09a0`), `0x1c` bytes with a `GLRectangleRegion` at `+4`: `ShellBackdrop_Ctor` (`0003918e`) builds the region with a zero rect and clears `0007fcfc`; `ShellBackdrop_Dtor` (`00039422`) destroys the palette `0007fcd0` and clears it. `ShellBackdrop_SetPicture` with no bitmap zeroes the rect's right `+0x10` (twice) and leaves its bottom `+0x14`. `ShellBackdrop_Draw` (`0002141c`)(backdrop + 4) copies the region from page 2 to the draw page and adds a dirty box of delay 0 at the rect's corner whose width and height are the rect's right `+0x10` and bottom `+0x14` themselves: for the full-screen picture, 319 × 199 from (0, 0), before `DirtyBox_Add`'s rounding ([dirty boxes](../rendering/screen-pages.md#dirty-boxes)).

## Opening and closing a screen

### The first screen — `Shell_OpenFirstScreen` (`0003ba47`)

`Shell_OpenFirstScreen`(exit code), [the start-up's](startup.md#the-start-up) step 15, sets up the scripts and then, by the code:

- **3 or 4**, back from a mission: `Mission_ReadRestore` and `Shell_ProcessMissionResults`, and `Shell_PreviousScreen` (`000817c2`) set to 999. In `Shell_Mode` 0, when the current career record's pilot is lost (`PilotEntry_Condition` 0, of a starting 100; [pilot entries](../formats/bst-battle-sets.md#pilot-entries)) the backdrop 1000 and `MissionMenu`, screen 1100 (`Shell_CurrentScreen` (`000817c0`) 1100, `Shell_BackdropId` 1000); otherwise `MessageBox_SetKind` (`00037eac`)(`0009b010`, 3) and the backdrop 5000. In mode 1, a lost pilot plays `endfa.ttm` (with music track 7) when `Shell_PlayIntro` is set and opens the home menu either way; otherwise by `Career_PeekNextCampaign`(the record the roster's word `+7` names), its `AL` sign-extended: 99, the campaign won, music 13 and `end_win1.ttm` to `end_win3.ttm` and `intro\earth.ttm`, then the home menu; −1 music 19 and `failed.ttm` and `intro\earth.ttm`, then the home menu, neither ending testing `Shell_PlayIntro`; any other `00037eac`(`0009b010`, 7), `MessageBox_Line` (`000aad5c`) set by `MessageBox_PickT1Line` (`00037bf0`)(7), `Shell_BackdropId` 4000 and the backdrop 4000, with no menu made, so `Shell_CurrentScreen` stays −1. The scripts outside `intro\` play with the prefix `res32\` set at the function's start. Mode 1 then calls `CareerRecord_AutoRepairSquad` (`0001601b`), `Roster_ForEachPilotNoOp` (`00016fdd`) and `Career_Save` (`000168b0`) on the careers block ([back at the shell](between-missions.md#back-at-the-shell)). In a mode other than 0 and 1 the case opens no screen.
- **6**, back from a demo, and **any other** after the intro ([the start-up](startup.md#the-start-up)): `anm\esiege2.anm` put into `000aacfe` with the byte `0007fcd7` set to 2, so [the draw](#drawing-a-screen) plays it; the backdrop 1000; a `MainMenu` made the current menu `Shell_CurrentMenu` (`000817c8`) and opened with `Menu_Open`(menu, 1000); `Shell_CurrentScreen` 1000, `Shell_PreviousScreen` 999 and `Shell_BackdropId` 1000.

`Shell_OpenHomeMenu` (`0003b3db`) does the same as the main menu's case with `anm\esiege2.anm` and the backdrop 1000, then in `Shell_Mode` 0 opens `MainMenu` as 1000 and in mode 1 `CareerMenu` as 1200, setting `Shell_CurrentScreen` and the word `0007fce8` to that number.

`Shell_PlayTtm` (`0003b8df`)(name) clears the draw page and page 2 to color 0 with the raster's slot 35, `VGA8_ClearPage` (`0008663c`, [lines and clipped bitmaps](../rendering/screen-pages.md#lines-and-clipped-bitmaps)), shows a frame, and clears the draw page again, each call reading the display's draw page `+0x38` as it then is, so the third clears the page drawn after the frame's swap. It makes the full-screen render context `0007fcd8` the scripts' default, opens the script with `Ttm_Open`(0, 0, 0, name), playing, not looping, on the default context, and runs it with `Ttm_RunScript`, `Input_PollDeviceBlock` and `Shell_PresentFrame`, a frame each, while its mode (`Ttm_GetMode`) is 0 and the block's word `+0` and byte `+0xa` are 0. The poll is `Input_PollDeviceBlock` itself, not `Shell_PollInput`: no button latch, and the lock bits stay in the keyboard word ([the input](startup.md#the-input)). When the mode is still 0, the script cut short by a key or the button, it clears the two pages, shows a frame and clears the new draw page as at the start; a script that ended by itself skips that. A key word or button byte left in the last poll sets the byte `000817bc`, which skips `lead_in.ttm` ([the start-up](startup.md#the-start-up)). Then `Ttm_Close` and `0007fcd0` cleared. On a failed open the handle `0xff` reaches `Ttm_RunScript`, which answers 0 for it, and `Ttm_GetMode` and `Ttm_Close`, which index the dword before the handle table.

### Opening a menu — `Menu_Open` (`0003969e`)

`Menu_Open`(menu, number):

1. stores the number at the menu's `+0`, sets the byte `Shell_SyncPointer` (`0007fd07`), and builds the window and its controls with `Menu_BuildFromDlg` ([the build](../formats/dlg-menu-dialogs.md#the-kinds));
2. makes the window the active one with `DUIWindow_SetActive` (`0001c536`) ([the active window](menu-input.md#the-active-window));
3. sets controls' states by the number, finding each with `DUIWindow_FindControlById` (`0001be16`), which takes a control in any state, or `DUIWindow_FindEnabledControlById` (`0001bda4`), which passes over inactive ones; it makes a control inactive by writing state 2 to `+0x19` and 1 to *changed* `+0x1f` itself, with no call of the control's slot `+0xc`. For the main menu, 1000: with no careers in the log (`0009b210`, [the careers log](../formats/career-log-and-player-mec.md#loading)'s count), button 1700, found in any state, is made inactive. For 1100 it selects the line `Mission_Kind` (`000aad5a`) holds in the list 1101 ([the SINGLE MISSION menu](single-mission.md#the-single-mission-menu)). 1200's are the career menu's ([the career menu](career.md#the-career-menu)); the other numbers' changes (1310, 1920, 2000, 1610, 1800, 1820 and 1700) are later modules';
4. unless the number is 9999, puts the focus on the first control whose key is `Space` (`0x39`), or on control 0, with `DUIWindow_PointAtControl` (`0001c3ec`), and stores it in `DUIWindow_FocusIndex` (`0007f0d8`);
5. for 1900 only, after the focus: makes control 1901 inactive when the word `00080892`, less 1 when `Hangar_HasMarkedHerc` (`0002a277`) answers non-zero, is below 2 (a later module's);
6. allocates the saved background, `Region_PixelCount` of the region `+0xe` bytes with `Mem_New`, zeroed with `memset`, at `+2`, and sets the byte `DUIWindow_InputReady` (`0007f0dc`).

**The build.** `Menu_BuildFromDlg` (`0003a1f7`)(menu, number), whose one call `es2_xref.py` finds is `Menu_Open`'s, opens `eng\menu<n>.bin` through `VolRStream_CtorFromPath` and, when that opens, reads it into a text table with `Text_LoadDataFile`; then it opens `dlg\menu<n>.dlg`, reads the count, and for each record reads the four rect words into the low halves of four zeroed dwords with `VolRStream_Read`, then through the stream's slot `+0x1c` the bytes kind, style and key, in the file's order, and the id as two bytes. It handles the record by its kind ([the kinds](../formats/dlg-menu-dialogs.md#the-kinds)), then counts it: the counter (`[EBP-0x114]`) is the record's text line, and every record advances it, one that builds nothing included (the loop's end `0003b0e3` is also id 1211's target). At the end it closes and destroys both streams and the table.

What the build does beyond [the kinds' table](../formats/dlg-menu-dialogs.md#the-kinds):

- **Kinds 0, 1, 4 and 5**, before the text: the five ids at `0008196c` jump through the targets at `00081980`. 1211 (`0003b0e3`) skips the record; the other four take a text other than the record's own line and then go on to the kind's code as any record does, all from the current career record, `0009b23b` + `0x1f7` × the word `0009b21f` ([the career record](../formats/bst-battle-sets.md#the-career-record)): 5556 (`0003a5c8`) line (the record's word `+0xa8`) + 2 of the menu's own table; 5557 (`0003a60e`) line (that word) + 4 of it; 5558 (`0003a654`) line (the record's word `+0xaa`) + `0x1b` of `eng\watext.bin` (`0009afa8`) when the word `+0xa8` is 0, and line (`+0xaa`) + `0xd5` of it otherwise; 1223 (`0003a705`) the text `Roster_SaveSlotText` (`0001838d`)(`Career_Roster`, `0009b218`) answers, the backup line of the overwrite warning ([SAVE and the overwrite warning](career.md#save-and-the-overwrite-warning)).
- **Kind 0** passes the text's length (`REPNE SCASB`) in the high word of the dword whose low word is the id; `MTWindow_Ctor` reads only the low word. A style other than 0 or 1 builds no window and leaves `+6` as it was, and the rect is still copied to `+0x12`.
- **Kind 3**: style 1 takes its text, the record's own line, only when the key byte is 0, and otherwise passes no text. Both button constructors get the key as the low byte of the dword read from the key byte's address, which holds the style, a stack byte and the id's low byte above it. Menu 1300's button 100 looks `Shell_ReturnScreen` up among the four values at `000819e0`, targets `000819f0`: 1000 the record's own line (`0003a863`), 1100 the line after it (`0003a87d`), 1710 and 1200 the line two after (`0003a89b`); for any other value it reads no line and keeps the text pointer (`[EBP-0x208]`) the previous record left ([the return screen](main-menu.md#the-return-screen)).
- **Kind 14**: each of the 17 ids at `00081b08` adds a hotspot with `DUIWindow_AddHotspot`(window, *a*, id, handler, rect), the targets at `00081b4c` differing only in the handler and, for 1502, *a*: 1612–1614 `maybe_PilotPortraitHotspot_Draw` (`00017a19`); 1619–1622 `00017b4a`; 1615–1618 `00017d9f`; 1231 `CareerStatsHotspot_Draw` (`0001705e`); 1522 `DebriefingResultsHotspot_Draw` (`000175d7`); 1502 `WarMapMenu_MapAreaNoOp` (`0002bbaf`) with *a* the rect `000a02d4`; 2005 `0002845c`; 1803 `0002d5bf`; 1811 `0002dacf` ([Open](#open)). Any other id builds nothing.
- **Kind 15** calls `Menu_ListLines`(id) before it looks the id up among the 23 at `00081a00` (1314, 1315, 1316, 1318, 1116–1120, 1325–1327, 1903, 1904, 1905, 1907, 1923, 1906, 1927, 1202, 1922, 1808, 1223), so a list whose id is among them has its lines built before its own code runs. The selected word passed for any other id is the low word of a local zeroed before the lookup, 0.
- A control is added with `DUIWindow_AddControl`, which also adds its key ([the window](#the-window)). Text buttons carry keys as icon buttons do: button 99 of menu 1230 `0x01` Esc, button 1624 of menu 1610 `0x39` Space. Some labels carry a non-zero key byte, which the build does not read.

`Menu_ListLines` (`0003750d`)(id) looks the id up among the 28 at `000812f4` (1921, 1924, 1925, 1926, 1907, 1906, 1927, 1923, 1116, 1117, 1119, 1120, 1118, 1314, 1315, 1316, 1318, 1331, 1101, 1002, 1202, 1904, 1903, 1905, 1922, 1808, 1809, 1223), targets at `00081364` in bytes Ghidra left undefined; 1002's (`00037706`) returns `CareerLog_CurrentNameArray` (`000182f8`)(`Career_Log`, `0009b20c`), the main menu's list ([the main menu's list](main-menu.md#the-list)), 1101's and 1116–1120's are the single mission's menus' ([a single mission](single-mission.md)), and 1202's and 1223's the career's ([the list](career.md#the-list), [the backup list](career.md#the-backup-list)). Any other id gets `WaText_LinesFromList` (`000373fa`)(`000811a4`): a new zero-ended array of the lines of `eng\watext.bin` that the `int16` words of the list number, as many as `WordList_CountNonNegative` (`000373d3`) counts before the first negative word. The list at `000811a4` opens with −1, so that array holds only its end, and the list box is empty. `00037827`, kind 16's lines, jumps elsewhere for the eleven ids at `0008144c` and otherwise returns 0.

`es2_xref.py --decode` finds one write of `Shell_SyncPointer`, `Menu_Open`'s, and one read, `Shell_PresentFrame`'s, which does not clear it, so from the first menu on every frame's step 7 puts the cursor and the mouse driver at the pointer `Shell_PointerX`, `Shell_PointerY` (`0009aff0`, `0009aff4`) with `Cursor_SyncPosition` and `Mouse_SetPosition` ([presenting a frame](../rendering/screen-pages.md#presenting-a-frame)).

### Closing a menu — `Menu_Close` (`0003b138`)

`Menu_Close`(menu):

1. clears `DUIWindow_InputReady`. While the byte `0008444e` is set, it puts the saved background back with `Region_Restore` through the region `+0xe`, at the window's corner (its dwords `+0` and `+4`): first on the draw page the display (`+0x38`) holds at entry, then with the draw page set to page 2 (`0007fccc`), and then puts the entry's draw page back. It adds the window's rect with `DirtyBox_Add`(1, bottom − top, right − left, top, left), delay 1, the differences as the width and height rather than the inclusive sizes ([dirty boxes](../rendering/screen-pages.md#dirty-boxes));
2. frees the saved background, and clears the active window with `DUIWindow_ClearActive` (`0001c57c`);
3. destroys the window: for menus 1501, 1500, 1700, 1710, 1510 and 1520 (the six numbers at `00081b90`) with `DUIWindow_Dtor` (`0001afa1`), for any other with `MTWindow_Dtor` (`00036a09`), flags 3; clears `+6`; and makes the full-screen render context `0007fcd8` again with `RenderContext_GetFullScreen`. That function and `RenderContext_GetFullScreenAround` set the one object `00083bc4`, so the scripts' default context, which [the draw](#drawing-a-screen) points at it, is back to the whole screen, clip mode 1 and no regions, with no `Ttm_SetDefaultContext` call.

`MTWindow_Dtor`(window, flags) frees each label's text, destroys the bitmap array `+0x5a3` with `GLBitmapArray_Dtor`, and runs `DUIWindow_Dtor`(window, 0). `DUIWindow_Dtor` destroys each control through its slot `+4` with 3, frees each hotspot's `+0x34`, destroys the render context `+0x17` through its slot `+0x1c` with 3, and the hotspots with `__vec_dtor` of `WindowHotspot_Dtor` (`0001b06f`).

### Changing screens — `Shell_HandleMessage` (`0003c7d8`)

**The frame loop's step 4 hands the pending message to `Shell_HandleMessage`, which gives it to the current menu's handler and then, when the message names a screen, closes the menu and opens that screen.** The message is four words, `Shell_PendingMessage` (`0007fcb0`–`0007fcb7`): a code, the control's index, a value and the target screen ([a message's path](menu-input.md#a-messages-path)). `Shell_HandleMessage`(the dwords `0007fcb0`, `0007fcb4`), on a copy of the four on its stack:

1. sets the pending message to (−1, −1, 0, 0), and answers 0 when the code is −1;
2. calls the current menu's slot `+8` with the copy, which may change it;
3. with target 99, *back*: unless the current screen is 1000, or the previous one is 1500 or 1501 and the object `Shell_MissionMap` (`000809a4`) is not set, the target becomes `Shell_PreviousScreen`; and when the word `Shell_ResumeScreen` (`000817be`) is not −1, `Shell_CurrentScreen` takes it and it is set to −1;
4. answers 0 when the target is not in `Shell_ScreenIds`;
5. for target 3000 or 3333 from screen 1500 or 1501 sets the byte `000817bd` to 3 while the byte `000809a8` is set, and to 0 otherwise;
6. with a current menu, closes it with `Menu_Close`, destroys it through its slot `+4` with flags 3 while `Shell_CurrentMenu` is still set, and clears `Shell_CurrentMenu`;
7. with the byte `000809a9` 1 clears it, hides the cursor (`0008662c`, [the cursor](../rendering/screen-pages.md#the-cursor)), sets the scripts' path prefix to `intro\` with `Ttm_SetPathPrefix`, plays music 13, `iintro.ttm` and `earth_w.ttm` with `Shell_PlayTtm`, and sets the prefix back to `res32\`; with 2 does the same with `credit_a.ttm` and `credit_b.ttm` and no music, leaving the byte for step 8;
8. takes the target's group with `Shell_BackdropForScreen`; when it is not 0 and differs from `Shell_BackdropId`, or the byte `0007fcd7` is 2, it first, when the old group is 1000, hides the cursor and presents a frame with `Shell_PresentFrame` itself, then sets the byte `Shell_BackdropChanged` (`000817c6`) to 1, stores the group in `Shell_BackdropId` and shows the new backdrop with `ShellBackdrop_Show`; otherwise it sets `Shell_BackdropChanged` to 0. With `000809a9` 2 it then sets `Shell_MusicTrack` to 1 and `0007fcd5`, and clears `000809a9`;
9. runs the target's case in the table (`REPNE SCASD` over the 41 dwords, `JMP [EDI+0xa0]`);
10. opens the new menu with `Menu_Open`(menu, target) when a case made one; sets `Shell_PreviousScreen` to 1000 for target 1000 and to the current screen otherwise, `Shell_CurrentScreen` to the target; draws the screen with `Shell_DrawScreen` and answers 1.

**Screen 9999** sets `Shell_ExitCode` to 5 when `Shell_DemoRequested` (`000817ce`) is set and to 2 otherwise, makes a `SimLoadMenu`, loads the palette `res32\regn9999.dpl`, clears `0007fcd5`, puts the string at `00080994` into `000aacfe` with `0007fcd7` 2, and copies the rect `0009b000` from the draw page to the visible page with `Display_CopyRectToPage`. The frame loop then sees 9999 and ends ([the ways out](startup.md#the-ways-out)).

The current menu is destroyed at the shutdown by `Shell_CloseScreens` (`0003c081`), [the shutdown's](startup.md#the-shutdown) step 3, through its slot `+4` with 3.

## Drawing a screen

**A screen is drawn once when it opens and redrawn only where something changed.** `Shell_DrawScreen` (`0003c1b7`), while the byte `0008444e` is set:

1. clears `DUIWindow_InputReady` and hides the cursor, the raster's slot 31 (`0008662c`, [the cursor](../rendering/screen-pages.md#the-cursor));
2. with `Shell_BackdropChanged` (`000817c6`) clear, that is after a change of screen within the backdrop's group: on screens 1700 and 1710, the career record's HERC diagram (`Hangar_ShowSlot` (`00029651`), `CareerRecord_SelectPilot`; [the HERC diagrams](../formats/herc-diagrams.md)) is drawn, between `0004c053` and `0004bc0d` when the record's `+0x12` and `+0xe` differ, and on any other screen nothing. With it set, after a new backdrop: unless `0007fcd7` is set or the screen is 1000, the draw page and the visible page are cleared to color 0 (`VGA8_ClearPage`, `0008663c`); with `0007fcd7` 2 the `.ANM` at `000aacfe` is played with `Anm_Play`(1, `0xa`, name) ([playing](../formats/anm-animation.md#playing)) and `0007fcd7` cleared; the backdrop is drawn with `ShellBackdrop_Draw`; and backdrop 4000 calls `ShellBackdrop_PlayAllScripts` (`0001240b`) and the HERC diagram, any other `ShellBackdrop_RunScripts` (`00012467`);
3. `DigitalDialog_ShowMessage`(`0009b010`), which loads the message box's line while it is to be shown ([showing a message](message-box.md#showing-a-message)), and `Ttm_RunAll`;
4. with no current menu, only sets the draw page back to the one it found; with a current menu: makes the render contexts `0007fcdc` with `RenderContext_GetForRect` (`0004c88a`) and `0007fcd8` with `RenderContext_GetFullScreenAround` (`0004c53c`), both of the window's rect, and makes `0007fcd8` alone the scripts' default with `Ttm_SetDefaultContext` (`0003c4b2`–`0003c4b8`), installing neither; `0007fcdc` is only stored. It sets the source and draw pages to page 2, and saves the background under the window into `+2` with `Region_Save`, in whatever render context is installed. `RenderContext_GetForRect` sets the context `00083bc8`, made with `GLViewport_Ctor` (`000512a0`)(0) on first use, to the rect, origin (0, 0), no clip regions (`+4` 0), clip mode 1 and translation 0; `RenderContext_GetFullScreenAround` sets the full-screen context `00083bc4`, the object `RenderContext_GetFullScreen` also sets, to (0, 0)–(319, 199), origin (0, 0), clip mode 2 and translation 0, with four clip regions, the 24-byte entries at `000a0a9c`: (0, 0, 319, top − 1), (0, top, left − 1, bottom), (right + 1, top, 319, bottom) and (0, bottom + 1, 319, 199), the screen above the rect, left of it, right of it and below it, so that the scripts drawn through `0007fcd8` stay off the window. Unless the message box is up (the byte `0009b013`, `Shell_MessageBox` (`0009b010`)'s `+3`), it draws the window on page 2 with `DUIWindow_Draw` (`0001bfa3`), first, while the byte `000817bd` is not 0, the war map's briefing under it on both pages ([leaving it](war-map.md#leaving-it)); then sets the draw page back. Still unless the message box is up: on backdrop 1000, except screen 9999, it adds the whole screen as a dirty box, `DirtyBox_Add`(1, 200, 320, 0, 0), so the next frames copy page 2 onto both pages; otherwise it draws the window again on the draw page and, while `000809a8` is set, sets `000817bd` to 0 when it is not 0 and otherwise calls `00009c64`(`Shell_MissionMap` (`000809a4`)) when the map is set, and sets `000809a8` to 3. It sets the byte `0007fcd6`, which makes the next frame wait 0.1 s ([presenting a frame](../rendering/screen-pages.md#presenting-a-frame));
5. with `0007fcd7` 1 uploads the palette `0007fcd0`, clears `0007fcd4`, plays the `.ANM` with `Anm_Play`(1, `0xb`, name) and clears `0007fcd7`;
6. puts the source page back, adds the whole screen as a dirty box with delay 0, shows the cursor again unless the screen is 9999 (slot 30, `00086628`), and sets `DUIWindow_InputReady`.

**Each pass after that** the frame loop's step 5 runs `Shell_TickScreen` (`0003c115`): unless the message box is up it updates the current menu's window with `DUIWindow_Update` (`0001c067`, [updating a window](#updating-a-window--duiwindow_update-0001c067)); and while the object `Shell_MissionMap` (`000809a4`) is set and the byte `000809a8` is above 1 it calls `00009cfb` and lowers `000809a8` when `0007fcd4` is clear, sets it to 2 when it is 1 and the object's byte `+0x4c0` is clear, and to 3 when it is 1 and `+0x4c2` is set; these last two read the object without testing that `Shell_MissionMap` is set ([Open](#open)).

## The window

**The window is the `.DLG`'s first record: a box of the menu's picture with its title on a framed plate, its labels, and its controls.** `DUIWindow_Ctor` (`0001abcf`, class `DUIWindow`) builds a `0x4ed`-byte window over the rect:

| Offset | Holds |
|---|---|
| `+0` | the rect, `int32` left, top, right, bottom, on the screen |
| `+0x10` | a byte: 0 hit-tests the rect; otherwise the pointer must lie within the distance `+0x15` of the point (`+0x11`, `+0x13`), measured as \|dx\| + \|dy\|. The constructor zeroes it and leaves `+0x11`–`+0x16` unwritten |
| `+0x17` | the render context, `GLViewport_Ctor` (`000512a0`)(0), with the rect copied to its `+0x210`, a zero point to its `+0x220` and 1 to its `+0x208` |
| `+0x1b` | `int16` the control count |
| `+0x1d` | `int16` `0xffff` |
| `+0x1f` | the controls, up to 64 pointers |
| `+0x11f` | the keys, a zero-ended 64-byte list of scan codes and their release codes |
| `+0x19f` | 15 hotspots of `0x38` bytes (`__vec_ctor` of `WindowHotspot_Ctor`, `0001ad4e`) |
| `+0x4e7` | `int16` the hotspot count, which `DUIWindow_Ctor` does not write: `MTWindow_Ctor` zeroes it, and a plain window keeps what the allocation held |
| `+0x4e9` | the vtable: `DUIWindow`'s `0007eff0`, slot `+4` `DUIWindow_AddLabel` (`0001ae9b`), which does nothing, and `+8` `DUIWindow_DrawBackground` (`0001aeb7`), which does nothing |

`MTWindow_Ctor` (`00036874`, class `MTWindow`) takes the window record's id, the title and the rect. It builds the window in `0x5a7` bytes with `DUIWindow_Ctor`, six label entries of `0x12` bytes at `+0x537`, and the vtable `00081140`, whose slot `+4` is `MTWindow_AddLabel` and `+8` `MTWindow_Draw` (`00036b4c`). It loads `res32\menu<id>.dba` with `Resource_LoadBitmapArray` into `+0x5a3`; id 9999 loads `res32\menu<9999 − r>.dba`, `r` from `Math_RandomBelow(5)`. It sets `+0x4ed` to the `title` font (`Shell_Fonts` (`000aacc6`), [the shell's fonts](../formats/styles-dat.md#the-loads)) and the label font `+0x4f1` by the id: the six ids at `00081174` jump through the targets beside them, 999, 1240 and 4444 to `nm_green` (`000aacda`), 1610 and 1800 to `cstats_2` (`000aacea`) and 1920 to `active` (`000aacce`), and any other id takes `000aacca`, `cpylw`, which `Widget_LoadStylesAndFonts` stores over `Shell_LoadData`'s `gray` ([the loads](../formats/styles-dat.md#the-loads)); every id then goes on alike (`00036987`). It copies the title to `+0x4f5`, or a 0 byte when there is none, and zeroes the label count `+0x535` and the hotspot count `+0x4e7`.

`MTWindow_AddLabel` (`00036a93`), the slot kinds 1, 4 and 5 call, keeps up to six labels and drops the rest: for each, a copy of the text in a new buffer of the length given, at `+0`, the point at `+4` and `+8`, the `int16` width at `+0xc` and the mode as a dword at `+0xe`, the entry's last four bytes.

`DUIWindow_AddHotspot` (`0001b2de`)(window, *a*, id, handler, rect) keeps up to 15 areas: for each, 1 at `+1`, the rect at `+2` and again at `+0x24`, *a* as a dword at `+0x12`, the id at `+0x16`, the handler at `+0x18` and 0 at `+0x34`. The byte `+0` is set when the area is pressed. `DUIWindow_HitHotspot` (`0001beca`) takes *a* as a pointer to a rect: a point inside the hotspot's rect but also inside that rect, when *a* is not 0, does not hit it.

`DUIWindow_AddControl` (`0001b207`) appends a control to `+0x1f`, counted at `+0x1b`, with no test against the 64 slots. When the control's key byte `+0x1d` is non-zero it passes the key, and the key with bit 7 set, to `DUIWindow_AddKey` (`0001b296`), which appends a byte to the key list unless the list holds it already; its walk has no bound either, so a 65th distinct code would be written past the 64 bytes the constructor zeroes.

### The menu window's drawing — `MTWindow_Draw` (`00036b4c`)

`MTWindow_Draw`(window) draws, in the window's render context, whose origin is the window's corner:

1. the first bitmap of `+0x5a3`, `res32\menu<id>.dba`, with `Bitmap_Blit` at the window's own first two dwords, its corner on the screen: `DUIWindow_Draw` has cleared the context's translation `+0x20c`, so the corner is not added again. It then sets `+0x20c`, and the rest is drawn relative to the corner;
2. the plate, for every menu window: the test at `00036b86` (`ADD EBX,0x4f5`) is of the title buffer's address, never 0, so a window with an empty title gets the plate too. *y0* 4 on backdrops 4000 and 2000 and 5 on the others, *y1* = *y0* + 17; a margin *m* of 20 on backdrop 4000 and 40 on the others; *x0* = (*w* − title width − *m*) / 2, truncating, with *w* the window's `int16` right − left, and *x1* = title width + *x0* + *m*, the widths by `HudFont_MeasureString` in the `title` font. Four lines through `Raster_DrawLineColour` (`0004ba84`), each under the pen {0, color} at the render context's `+0x228`: the top (*x0*, *y0*)–(*x1*, *y0*) and the left (*x0*, *y0*)–(*x0*, *y1*) in `0x10`, the right (*x1*, *y1*)–(*x1*, *y0*) in `0x1b` and the bottom (*x1*, *y1*)–(*x0*, *y1*) in `0x1c`. Then frames 0 to 3 of `res32\corners.dba` (`000ab4ee`) with `Bitmap_Blit`, flags 0, at (*x0*, *y0*), (*x1* − 4, *y0*), (*x0*, *y1* − 4) and (*x1* − 4, *y1* − 4), so only the right and bottom ones are set in. Last the title in the `title` font, centered between *x0* and *x1* (the render context's `+0x231` 2, `+0x235` *x1* − *x0*; `HudFont_DrawString`), its pen at (*x0*, (*y1* − *y0*) / 2 + *y0* − 1), the division truncating;
3. each label, in the font `+0x4f1`: `HudFont_DrawString` at (*x*, *y* + the font's cell height − 3), the label's mode as `+0x231` and its width as `+0x235`. So kind 1's mode 0 draws the text from *x*, kind 4's mode 1 ends it at *x* + width, and kind 5's mode 2 centers it in the width ([the kinds](../formats/dlg-menu-dialogs.md#the-kinds)).

### Drawing a window — `DUIWindow_Draw` (`0001bfa3`)

`DUIWindow_Draw`(window) installs the window's render context, clears its `+0x20c`, calls the window's slot `+8`, sets `+0x20c` again, calls each control's slot `+8`, calls each hotspot's handler with (the hotspot's `+2`, its id), and installs the render context it found.

### Updating a window — `DUIWindow_Update` (`0001c067`)

`DUIWindow_Update`(window) installs the window's render context with `+0x20c` set and clears `DUIWindow_InputReady`. For each control whose byte `+0x1f`, *changed*, is 1 or whose `+0x1b` is 2, it draws the control through its slot `+8` on page 2 and again on the draw page, and adds its rect (slot `+0x48`, moved by the window's corner) with `DirtyBox_AddRect`, delay 0. Then `DUIWindow_UpdateHotspots` (`0001c270`) does the same for each hotspot whose byte `+0` is set, calling its handler on page 2 and on the draw page, and clears the byte. Last it sets `DUIWindow_InputReady` and installs the render context it found.

## The controls

**A control is a rect, an id, a state and a vtable; the id is the screen a press of it names.** `DUIControl_CtorWithKey` (`0001a7e5`)(control, *focusable*, key, rect) and `DUIControl_Ctor` (`0001a716`)(control, *focusable*, id, rect) build the `DUIControl` base, `0x24` bytes, zeroing the byte `+0x10` and storing the vtable `0007ef92` and the rect with `DUIControl_SetRect` (`0001a8b3`), which also clears `+0x19` and `+0x1f`; neither writes `+0x1b`, which the derived constructors set. `DUIControl_SetHitMode` (`0001a4f4`)(control, mode) stores the mode at `+0x10` and, for mode 1, sets the distance `+0x15` to (right − left) >> 1 and the point `+0x11`, `+0x13` to (left + that, top + that), so the control is hit inside the diamond inscribed in the top square of its rect:

| Offset | Holds |
|---|---|
| `+0` | the rect, `int32`, relative to the window |
| `+0x10` | a byte: 0 hit-tests the rect; otherwise the distance test of the window's `+0x10`, with the point at `+0x11`, `+0x13` and the distance `+0x15` |
| `+0x17` | `int16` the id |
| `+0x19` | `int16` the state: 0 up, 1 pushed, 2 inactive |
| `+0x1b` | `int16` how a press is tracked: 0 a button's, 1 the control follows the pointer while pressed, 2 the control holds the input ([the mouse](menu-input.md#the-mouse)) |
| `+0x1d` | the key, a scan code, or 0 |
| `+0x1e` | *focusable*: 1 for buttons, which `Tab` stops at |
| `+0x1f` | *changed*: set when the control needs drawing |
| `+0x20` | the vtable |

The slots the window code calls:

| Slot | Called by | `DUIControl` (`0007ef92`) | `MTButton` (`0007f18c`) |
|---|---|---|---|
| `+4` | `DUIWindow_Dtor`, (control, flags) | `DUIControl_Dtor` (`0001a61a`) | `MTButton_Dtor` (`0001d10d`) |
| `+8` | `DUIWindow_Draw`, `DUIWindow_Update`, (control) | `DUIControl_Draw` (`0001a647`), nothing | `MTButton_Draw` (`0001d200`) |
| `+0xc` | after every state change, (control) | `DUIControl_Invalidate` (`0001a651`), nothing | `MTButton_Invalidate` (`0001d17b`): sets *changed* |
| `+0x2c` | a release, (control): the message's value | `DUIControl_GetValue` (`0001a6ac`): 1 | `DUIButtonControl_GetValue` (`0001d088`): 1 |
| `+0x38` | a press or a move of a tracking control, (control, point relative to the window) | | |
| `+0x3c` | a typed character to the holding control, (control, character) | | `MTButton_SetTextFromTable` (`0001d393`) |
| `+0x40` | a key code to the holding control, (control, code & `0x7f`) | | |
| `+0x48` | `DUIWindow_Update`, (control): the rect | `BarGraph_GetRectWindowCopy` (`0001a70a`) | `BarGraph_GetRectButtonCopy` (`0001d0e6`) |

`MTButton`'s slots `+0x10` and `+0x18`, `MTButton_Highlight` (`0001d1ce`) and `MTButton_Unhighlight` (`0001d1e7`), set and clear the byte `+0x94` and set *changed* ([Open](#open)).

### Buttons

Kind 3's style 1 builds an icon button, `MTBmpButton_Ctor` (`0001d47d`, class `MTBmpButton`), with no text when the key is non-zero; any other style a text button, `MTButton_Ctor` (`0001cde8`, class `MTButton`). The main menu's six are text buttons with no key. Menu 1300's button 100 takes its text by `Shell_ReturnScreen` (`0007fce8`) among the four values at `000819e0` ([the return screen](main-menu.md#the-return-screen)); when it is none of them, the text pointer keeps the value an earlier record left in it.

`MTButton_Ctor` builds a `0x95`-byte button with `DUIControl_CtorWithKey`(button, 1, key, rect), sets `+0x1b` to 0, zeroes the bytes `+0x94` and `+0x1f`, and stores the id at `+0x17`, its vtable `0007f18c` at `+0x20`, the `STYLES.DAT` records 0, 1 and 2 (the pointer `00082654`) at `+0x30`, `+0x34` and `+0x38`, and the `active`, `pushed` and `inactive` fonts (`000aacce`, `000aacd2`, `000aacd6`) at `+0x24`, `+0x28` and `+0x2c`, one per state. It copies the text to `+0x3c` and places it three times at `+0x7c`, once per font: x the rect's left, y the bottom less ((`int16` height) − the font's cell height) / 2, truncating, less 2: the up point with the font `+0x24`, the pushed one a pixel lower, and the inactive one with the font `+0x2c`. With no text, `+0x3c` and the points are 0. `MTButton_SetTextFromTable` (`0001d393`)(button, line) copies line *line* of `eng\watext.bin` (`0009afa8`) to `+0x3c`, places it again and sets *changed*; `Menu_Open` calls it on the buttons of several menus.

`MTBmpButton_Ctor` builds a `0xb1`-byte button over a text button, stores its vtable `0007f24a` at `+0x20` and the rect's left and top at `+0x95`, and takes its pictures by key from `res32\corners.dba`, the array `Shell_Main` loads into `000ab4ee`: frame *a* at `+0xa5` and `+0xad`, frame *b* at `+0xa9`: the frames of states 0, 1 and 2, so an inactive icon button looks as it does up. Eight ids take other frames, and four of them also give themselves the diamond hit area with `DUIControl_SetHitMode`(button, 1).

| Key | Frames *a*, *b* | Other ids |
|---|---|---|
| `0x48` Up, `0x49` PgUp | 20, 22 | 1503: 36, 37 and `0001a4f4` |
| `0x4a` keypad − | 29, 31 | 109: 34, 35 |
| `0x4b` Left | 25, 27 | 1505: 40, 41 and `0001a4f4`; 2003: 44, 46 |
| `0x4d` Right | 24, 26 | 1506: 42, 43 and `0001a4f4`; 2004: 45, 47 |
| `0x4e` keypad + | 28, 30 | 108: 32, 33 |
| `0x50` Down, `0x51` PgDn | 21, 23 | 1504: 38, 39 and `0001a4f4` |

With any other key the constructor looks the id up among the four at `0007f2c4`, 1806, 2003, 1807 and 2004, and jumps to `0001d97a` for the first two and `0001d9f9` for the others, code Ghidra left undefined ([Open](#open)); any other id sets no frames.

`MTBmpButton`'s vtable (`0007f24a`) has its own slots `+4`, `+8`, `+0xc` and `+0x48`, and `MTButton`'s for `+0x10`, `+0x18`, `+0x2c` and `+0x3c`:

| Slot | Function | Does |
|---|---|---|
| `+4` | `MTBmpButton_Dtor` (`0001da8d`) | stores its vtable, then `MTButton`'s, `DUIButtonControl`'s and the button file's copy of `DUIControl`'s (`0007f1ea`), and frees the button when bit 0 of the flags is set |
| `+8` | `MTBmpButton_Draw` (`0001db87`) | `Bitmap_Blit` of the state's frame (`+0xa5` + 4 × state), flags 0, at the point `+0x95`; in state 1, the click as `MTButton_Draw`'s, after the blit; the text, centered (the render context's `+0x231` 2) in the frame's `int16` width `+6`, in the state's font at the state's point; *changed* cleared |
| `+0xc` | `MTBmpButton_Invalidate` (`0001dc9f`) | sets *changed* |
| `+0x48` | `MTBmpButton_GetRect` (`0001db1d`) | sets `+0x9d`, `+0xa1` to the point `+0x95` plus the state's frame's width `+6` and height `+4`, and answers the rect at `+0x95` |

### Drawing a control

`MTButton_Draw` (`0001d200`)(button), by the state `+0x19`:

- **1, pushed**: while the byte `0007fcb9` and the sound object `0007fd00` are set, it stops the object when its slot `+0x30` answers non-zero (slot `+0x14`) and plays it (slot `+0x10`): the click. Then as state 0;
- **0, up**, and pushed: `00044b86` with the state's `STYLES.DAT` record over the button's rect, and, while the byte `+0x94` is set, `0004b867`(10, rect);
- **2, inactive**: `00044b86` with record 2;
- any other state draws no frame;

then the text in the state's font at the state's point, centered in the button's width, the `int16` right − left (the render context's `+0x231` 2, `+0x235` the width), and *changed* cleared. The text test is of the text buffer's address, never 0, so the text is always drawn, and an empty one draws nothing.

`CornerFrame_Draw` is `ES1DBSIM`'s `CornerFrame_Draw` (`ES1DBSIM 00044954`) with the shell display's `+0x40` less 4 for the simulator's `+0x44` less 4: `Raster_InstallRenderContext` (`000513fc`) stores the installed context + 4 there, so it draws in the installed context, the one `000ab612` holds ([drawing a frame from a style](../simulation/alert-panels.md#drawing-a-frame-from-a-style)), with `CornerFrame_DrawEdgeRing` (`00044dc8`), `CornerFrame_DrawCorners` (`000451c1`), which takes its pictures from the shell's corner bank, and `Rect_CopyCtorFrameCopy` (`00044d9a`). `Raster_FillFrameRectColours` and `Raster_FrameRectColour` (`0004b867`) are `ES1DBSIM`'s (`ES1DBSIM 00044e50`, `00044e00`) on the render context `000ab612`: the second frames a rect's four edges in one color ([the color helpers](../simulation/alert-panels.md#the-resource-loaders-and-the-color-helpers)), the first fills it in one and then frames it in the other. A button draws with the same two: its state's `STYLES.DAT` record through `CornerFrame_Draw`, and, while highlighted, a frame in color 10 through `Raster_FrameRectColour`.

### The list box

Kind 15 builds `MTStrListBox_Ctor` (`0001e11a`, class `MTStrListBox`, vtable `0007f474`) over `DUIVSlider` (`0007f412`) and `DUISliderControl` (`0007f2f8`), a `0x5a`-byte control on `DUIControl_Ctor`(list, 0, id, rect), so `Tab` passes over it: `+0x1b` 1, the range `+0x24` 0 to `+0x26` the line count `0001eac5`(list, lines), the selected line at `+0x28`, `+0x2a` and `+0x54` from the record's word, the style at `+0x3e`, the lines at `+0x4c`, `STYLES.DAT` record 3 at `+0x50` (`00082654` + `0xc0`, where `MTButton_Ctor`'s records 0 to 2 are `+0`, `+0x40` and `+0x80`), the fonts `nm_green` (`000aacda`) and `nm_red1` (`000aacde`) at `+0x40` and `+0x44`, the line height `+0x2c`: `HudFont_CellHeight2` of `nm_green` (passed the first line, which it does not read) − 1, replaced by the rect's `int16` height − 1 when the height is below it, so a height equal to it stays. The lines shown `+0x58` are the height over the line height (`int16` division), 1 when that is 0, then at most the line count, so 0 for an empty list; the first line shown `+0x56` is 0 when the selected line is below the lines shown, and otherwise selected − shown + 1, so that the selected line is the last shown. The main menu's 1002 is one, at (112, 47)–(196, 59). Its lines are `Menu_ListLines`(id) ([the main menu's list](main-menu.md#the-list)).

`MTStrListBox`'s vtable (`0007f474`) holds the slots the window code calls ([the controls](#the-controls)):

| Slot | Function | Does |
|---|---|---|
| `+4` | `MTStrListBox_Dtor` (`0001e41e`) | frees the lines array `+0x4c`, and for id 1002 first its first line and the name buffer pointer `0007ebc0`, which it clears; stores `DUIVSlider`'s, `DUISliderControl`'s and `DUIControl`'s vtables in turn and frees the list when bit 0 of the flags is set |
| `+8` | `MTStrListBox_Draw` (`0001e584`) | `CornerFrame_Draw` (`00044b86`) with `STYLES.DAT` record 3 (`+0x50`) over the list's rect, then `MTStrListBox_DrawLines` (`0001e5b7`), and *changed* cleared |
| `+0xc` | `MTStrListBox_TrackSelect` (`0001e96a`) | with style 0 only: selects the line under the tracked point, below |
| `+0x1c`, `+0x20` | `MTStrListBox_SelectPrev` (`0001e8df`), `MTStrListBox_SelectNext` (`0001e919`) | moves the selected line `+0x54` up or down one within the lines, scrolling the first line shown `+0x56` to keep it shown, and sets *changed* |
| `+0x24` | `MTStrListBox_SelectLine` (`0001ea4f`)(list, line) | selects the line, clamped to the last when past it and unchanged when negative, shows from it or from the last page, and sets *changed* |
| `+0x2c` | `MTStrListBox_GetSelected` (`0001e3d3`) | the selected line `+0x54`: the message's value |
| `+0x30` | `MTStrListBox_SelectedText` (`0001e3e5`) | the selected line's text |
| `+0x38` | `MTStrListBox_SetTrackPoint` (`0001e404`)(list, point) | stores the point's y at `+0x28` |
| `+0x44` | `MTStrListBox_SetLines` (`0001e50a`)(list, lines) | frees the old array, takes the new one, counts it into `+0x26` with `MTStrListBox_CountLines` (`0001eac5`), shows from line 0 and sets the lines shown `+0x58` to the rect's height over the line height, at most the count |
| `+0x48` | `BarGraph_GetRectButtonCopy` (`0001d0e6`) | the rect |

**Drawing the lines.** `MTStrListBox_DrawLines` sets the render context `000ab612`'s alignment `+0x231` to the list's `+0x48`, 0 from the record, and its field width `+0x235` to the rect's width less 2. For each row *r* from 0 while *r* is below the lines shown and the line `+0x56` + *r* exists, with *h* the line height `+0x2c` and (*x0*, *y0*, *x1*) the rect: in style 0 the selected line is drawn on a box (*x0* + 1, *y0* + *r* × *h* + 1)–(*x1* − 1, *y0* + *r* × *h* + *h*) filled in color 4 and framed in color 5 (`Raster_FillFrameRectColours` (`0004b8d8`)(5, 4, box)), its text in the font `+0x44`, `nm_red1`; every other line, and every line in style 1, in the font `+0x40`, `nm_green`; the text with `HudFont_DrawString` at (*x0* + 3, *y0* + *r* × *h* + *h* − 1).

**A press and a move.** The list tracks the pointer (`+0x1b` 1), so a press and each move while it is held call slot `+0x38` with the point relative to the window and then slot `+0xc` ([the mouse](menu-input.md#the-mouse)). `MTStrListBox_TrackSelect` returns at once unless the style `+0x3e` is 0. With style 0: a y above the rect's top selects the previous line with slot `+0x1c` and then the first line shown; a y below its bottom selects the next with slot `+0x20` and then one more while that is below the last line; a y inside selects the line of the shown row it falls in and sets *changed*.

## Functions

| ES1 | Address |
|---|---|
| `Shell_StockItemsFromFlags` | `00038e48` |
| `Shell_ProcessMissionResults` | `00038eb5` |
| `ShellBackdrop_Ctor`, `ShellBackdrop_Dtor`, `ShellBackdrop_LoadPalette`, `ShellBackdrop_Show` | `0003918e`, `00039422`, `0003950a`, `000395a0` |
| `ShellBackdrop_SetPicture`, `ShellBackdrop_Draw`, `Shell_BackdropForScreen` | `00021334`, `0002141c`, `00040afa` |
| `ShellMusic_Play` | `00020a70` |
| `ShellBackdrop_OpenGroupTtms`, `TtmEntry_Dtor`, `TtmEntry_Ctor` | `00011f19`, `00011edd`, `0001201b` |
| `GLBase_DtorShellCopy`, `GLRegion_DtorShellCopy`, `GLRectangleRegion_DtorShellCopy`, `Rect_CopyCtorShellCopy` | `00039342`, `0003936e`, `000393b7`, `0003c7aa` |
| `Menu_Open`, `Menu_BuildFromDlg`, `Menu_Close` | `0003969e`, `0003a1f7`, `0003b138` |
| `Menu_Dtor`, `Menu_OnMessage` | `0003b6d1`, `0003b764` |
| `MainMenu_Dtor`, `CareerMenu_Dtor`, `MissionMenu_Dtor` | `0003b771`, `0003b828`, `0003bfca` |
| `SimLoadMenu_Dtor`, `SimLoadMenu_OnMessage`, `UnArmedAlert_Dtor`, `UnAssignedAlert_Dtor`, `UnAssignedAlert_OnMessage` | `0003f8b6`, `0003f96d`, `0003f97a`, `0003fa31`, `0003fae8` |
| `AnyDisabledAlert_Dtor`, `ScrapAlert_Dtor`, `TechReleaseAlert_Dtor`, `QuitAlert_Dtor`, `NewCareerAlert_Dtor`, `CareerRecordsMenu_Dtor`, `CareerStatsAlert_Dtor` | `0003faf5`, `0003fbac`, `0003fc63`, `0003fd1a`, `0003fdd1`, `0003fe88`, `0003ff3f` |
| `KIAWarningAlert_Dtor`, `DeleteAlert_Dtor`, `MissionOptionsAlert_Dtor`, `PrefsMenu_Dtor`, `OptionsMenu_Dtor`, `BattlesetAlert_Dtor`, `ServiceBayMenu_Dtor`, `RepairMenu_Dtor`, `RepairPrefsAlert_Dtor`, `SalvageAllocAlert_Dtor` | `000400ad`, `00040164`, `0004021b`, `000402d2`, `00040389`, `00040440`, `000404f7`, `000406a7`, `0004075e`, `00040815` |
| `Shell_OpenHomeMenu`, `Shell_PlayTtm`, `Shell_OpenFirstScreen` | `0003b3db`, `0003b8df`, `0003ba47` |
| `Shell_CloseScreens`, `Shell_TickScreen`, `Shell_DrawScreen`, `Shell_HandleMessage` | `0003c081`, `0003c115`, `0003c1b7`, `0003c7d8` |
| `Shell_StaticInit`, `Shell_StaticDtor` | `00040b74`, `00040b83` |
| `DUIControl_Dtor`, `DUIControl_Draw`, `DUIControl_Invalidate`, `DUIControl_GetValue`, `DUIControl_Ctor`, `DUIControl_CtorWithKey`, `DUIControl_SetRect` | `0001a61a`, `0001a647`, `0001a651`, `0001a6ac`, `0001a716`, `0001a7e5`, `0001a8b3` |
| `BarGraph_NoOpWindowCopy`, `GridShape_GetMemorySizeWindowCopy`, `BarGraph_GetRectWindowCopy` | `0001a665`, `0001a6ba`, `0001a70a` |
| `DUIWindow_Ctor`, `WindowHotspot_Ctor`, `DUIWindow_AddLabel`, `DUIWindow_DrawBackground`, `DUIWindow_Dtor`, `WindowHotspot_Dtor` | `0001abcf`, `0001ad4e`, `0001ae9b`, `0001aeb7`, `0001afa1`, `0001b06f` |
| `GLBase_DtorWindowCopy`, `GLRegion_DtorWindowCopy`, `GLRectangleRegion_DtorWindowCopy`, `Rect_CopyCtorWindowCopy` | `0001aec1`, `0001aeed`, `0001af36`, `0001c242` |
| `DUIWindow_Draw`, `DUIWindow_Update`, `DUIWindow_UpdateHotspots`, `DUIWindow_PointAtControl` | `0001bfa3`, `0001c067`, `0001c270`, `0001c3ec` |
| `MTButton_Ctor`, `DUIButtonControl_Dtor`, `MTButton_Dtor`, `MTButton_Invalidate`, `DUIControl_DtorButtonCopy` | `0001cde8`, `0001cfd8`, `0001d10d`, `0001d17b`, `0001d18d` |
| `MTButton_Highlight`, `MTButton_Unhighlight`, `MTButton_Draw`, `MTButton_SetTextFromTable`, `DUIButtonControl_GetValue` | `0001d1ce`, `0001d1e7`, `0001d200`, `0001d393`, `0001d088` |
| `BarGraph_NoOpButtonCopy`, `GridShape_GetMemorySizeButtonCopy`, `BarGraph_GetRectButtonCopy`, `Rect_CopyCtorButtonCopy` | `0001d041`, `0001d096`, `0001d0e6`, `0001d365` |
| `MTBmpButton_Ctor`, `MTStrListBox_Ctor` | `0001d47d`, `0001e11a` |
| `MTStrListBox_GetSelected`, `MTStrListBox_SelectedText`, `MTStrListBox_SetTrackPoint`, `MTStrListBox_Dtor`, `MTStrListBox_SetLines`, `MTStrListBox_Draw`, `MTStrListBox_DrawLines` | `0001e3d3`, `0001e3e5`, `0001e404`, `0001e41e`, `0001e50a`, `0001e584`, `0001e5b7` |
| `MTStrListBox_SelectPrev`, `MTStrListBox_SelectNext`, `MTStrListBox_TrackSelect`, `MTStrListBox_SelectLine`, `MTStrListBox_CountLines` | `0001e8df`, `0001e919`, `0001e96a`, `0001ea4f`, `0001eac5` |
| `CornerFrame_Draw`, `Rect_CopyCtorFrameCopy`, `CornerFrame_DrawEdgeRing`, `CornerFrame_DrawCorners` | `00044b86`, `00044d9a`, `00044dc8`, `000451c1` |
| `Raster_FrameRectColour`, `Raster_FillFrameRectColours` | `0004b867`, `0004b8d8` |
| `MTWindow_Ctor`, `MTWindow_Dtor`, `MTWindow_AddLabel`, `MTWindow_Draw` | `00036874`, `00036a09`, `00036a93`, `00036b4c` |
| `DUIControl_SetHitMode`, `MTBmpButton_Dtor`, `MTBmpButton_GetRect`, `MTBmpButton_Draw`, `MTBmpButton_Invalidate` | `0001a4f4`, `0001da8d`, `0001db1d`, `0001db87`, `0001dc9f` |
| `Raster_DrawLineColour`, `GLViewport_Ctor`, `RenderContext_GetFullScreenAround`, `RenderContext_GetForRect` | `0004ba84`, `000512a0`, `0004c53c`, `0004c88a` |
| `WordList_CountNonNegative`, `Roster_SaveSlotText`, `maybe_PilotPortraitHotspot_Draw` | `000373d3`, `0001838d`, `00017a19` |

## Globals

The values in the image are the ones the shell starts with.

| | Address | In the image |
|---|---|---|
| `Shell_CurrentScreen`, `Shell_PreviousScreen`, `Shell_BackdropId`, `int16` | `000817c0`, `000817c2`, `000817c4` | −1 each |
| `Shell_ResumeScreen`, `int16` | `000817be` | −1 |
| `Shell_BackdropChanged`, byte: 1 after `Shell_HandleMessage` shows a new backdrop | `000817c6` | 1 |
| the bytes `000817bc`, `000817bd` | | 0 |
| the scripts' palette `0007fcd0`, and the bytes `0007fcd4` to `0007fcd7` | `0007fcd0`–`0007fcd7` | 0 |
| page 2, the background, `int16` | `0007fccc` | 2 |
| `Shell_CurrentMenu`, the open screen object | `000817c8` | |
| `Shell_ScreenIds`, zero-ended `int16` list | `000817e0` | |
| `Shell_Backdrop` | `000a09a0` | |
| `Shell_MusicTrack`, `int16`; the track last set | `00081938`; `00081936` | −1; −1, so the first backdrop always starts its track |
| `ShellBackdrop_Picture` | `000aa7ac` | |
| `ShellBackdrop_GroupTtms`, the group's script entries; `ShellBackdrop_GroupTtmCount`, `int16`; `ShellBackdrop_GroupTtmList`, `int16` | `0007e718`; `0007e71c`; `0007e716` | |
| `Shell_SyncPointer`, byte | `0007fd07` | |

## Open

- **Open:** whether `00040b94`, a stub returning its first argument, is `shell.cpp`'s last function or the word stacks' file's first: it starts on a 4-byte boundary right after `Shell_StaticDtor`, and `WordStack_Push` follows it with no fill.
- **Open:** where the button file ends, and whether the slider and list classes from `0001deaa` share it.
- **Open:** what `HangarBay_AssignScripts` (`000122e0`), `ShellBackdrop_PlayAllScripts` (`0001240b`) and `ShellBackdrop_RunScripts` (`00012467`) do for a backdrop.
- **Open:** what calls `MTButton_Highlight` and `MTButton_Unhighlight`: no caller of `MTButton`'s slots `+0x10` and `+0x18` was found in the window, active window's or shell code read here.
- **Open:** the object `Shell_MissionMap` (`000809a4`) and the bytes `000809a8` and `000809a9` that `Shell_DrawScreen`, `Shell_TickScreen` and `Shell_HandleMessage` test, and `00009cfb` and `00009c64`. The byte `000817bd` keeps the war map's briefing drawn behind an alert ([leaving it](war-map.md#leaving-it)), and `Shell_DrawScreen` reaches the map's slot `+4` through it after the map is deleted ([the loading screen](war-map.md#the-loading-screen-after-the-unarmed-alert)).
- **Open:** the other screens' cases and handlers in [the screens](#the-screens) table, among them screen 1320's code at `0003e9ae`, screen 2000's `0002986f` and `000307c9`, the hangar test of `ShellMouse_OnLeftPress` with the dword `0007fce4`, the icon button's ids 1806, 2003, 1807 and 2004 at `0001d97a` and `0001d9f9`, and the kind-14 hotspot handlers other than `maybe_PilotPortraitHotspot_Draw` (`00017b4a`, `00017d9f`, `DebriefingResultsHotspot_Draw` (`000175d7`), `0002845c`, `0002d5bf`, `0002dacf`; 1502's is [the war map's](war-map.md#the-screen)).
- **Open:** what `maybe_PilotPortraitHotspot_Draw` (`00017a19`) draws, for ids 1612–1614 with *n* = id − 1611: frame (pilot entry *n*'s word `+0`) of the bitmap array `CrewAssignMenu_Portraits` (`0007ebcc`) at the rect's corner plus (1, 1), and, while the career record's byte `+1` is set and the entry's byte `+0xa` is 0, frame 0 of the array `CrewAssignMenu_Mesh` (`0007ebd0`) at the rect's corner; which banks the two arrays are.
- **Open:** the list box's slots `+0x10`, `+0x14`, `+0x18`, `+0x28`, `+0x34`, `+0x3c` and `+0x40` (`0001d037`, `BarGraph_NoOpButtonCopy`, `0001d04f`, `0001d07b`, `0001d0a2`, `0001d0bd`, `0001d0cb`), and the 19 ids other than 1002, 1101, 1116–1120, 1202 and 1223 ([the SINGLE MISSION menu](single-mission.md#the-list), [the MISSION PARAMS menu](single-mission.md#the-mission-params-menu), [the career](career.md#the-list)) that `Menu_ListLines` gives their own lines (the table at `000812f4`).
