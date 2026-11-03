# GO.EXE — the menus

**Every screen of ES1's shell between the intro and a mission is a menu: a picture behind it, a framed window with a title, labels and buttons, laid out by the menu's `.DLG` file ([the menu dialogs](../formats/dlg-menu-dialogs.md)). A button is pressed with the mouse or with `Enter`, and acts when it is let go over the same button; its number names the next screen. The shell then closes the open menu, changes the picture and the music behind when the next screen belongs to another group, builds the next menu and draws it.** What a player would see:

- pointing at a button gives it the focus, and `Tab`, `Shift+Tab` and `Alt+Tab` move the focus to the next or previous button and put the pointer on it ([the keys](menu-input.md#the-keys));
- a button pressed and then dragged off pops back up, and acts only if the pointer comes back over it before the release ([the mouse](menu-input.md#the-mouse));
- a pressed text button clicks when the byte `0007fcb9` is set, which `prefs.cfg` option 1 sets ([drawing a control](menu-controls.md#drawing-a-control));
- a menu opens with the pointer on the button whose key is `Space`, or on its first control ([opening a menu](#opening-a-menu--menu_open-0003969e)).

Addresses are `ES1GO` image addresses ([the X-32 executable](../formats/x32-executable.md#the-image)). The functions here remove their own arguments, so argument lists are in stack order ([calling conventions](../formats/compiler.md#calling-conventions)). The frame loop that runs a screen is [the start-up's](startup.md#the-frame-loop), how the open menu takes the mouse and the keys is [the menus' input](menu-input.md), and the window and its controls are [the menu window and its controls](menu-controls.md).

## Where the object files start and end

**`shell.cpp`** runs from `Shell_StockItemsFromFlags` (`00038e48`) to `00040b93`. The file before ends with its static initializer `DamageValues_StaticInit` (`00038e2f`) (the static constructor table's entry `0009a264`), whose `RET` at `00038e46` is followed by one zero byte. `Shell_ProcessMissionResults`' asserts name `src\shell.cpp` (lines `0xc7` and `0xd7`). Its last code is its static initializer, `Shell_StaticInit` (`00040b74`, entry `0009a268`), which builds [the backdrop](#the-backdrop), and the exit routine `Shell_StaticDtor` (`00040b83`, exit table entry `0009a2a0`), which destroys it; that `RET` ends at `00040b93`, on a 4-byte boundary. `00040b94`, a stub taking three arguments and returning the first, follows, and then the word stacks from `WordStack_Push` (`00040ba6`), whose last `RET 8`, at `00040d50`, is followed by one zero byte and `DirtyList_CreateContext` (`00040d54`) ([Open](#open)). The file's data holds the class names and vtables of its screens from `00081bd0` to `00082058`, and its copies of `ClassItem`'s, `GLBase`'s, `GLRegion`'s and `GLRectangleRegion`'s vtables (`0008186c`, `00081896`, `000818c8`, `00081900`) with the three destructors `GLBase_DtorShellCopy` (`00039342`), `GLRegion_DtorShellCopy` (`0003936e`) and `GLRectangleRegion_DtorShellCopy` (`000393b7`) in their slot `+0x1c`.

**The active window's file** runs from `ShellMouse_AddCallbacks` (`0001c460`) to `DUIWindow_HandleNavKey` (`0001cc86`), whose `RET 4` at `0001cde4` is followed by one zero byte. It holds the active window and the mouse's callbacks.

The image stores each class's name just before its vtable, and the word before the vtable is the name's offset back from it: `Menu`'s vtable `00081bd8` has −8 at `00081bd6`, its name at `00081bd0`. The classes here, `DUIWindow`, `MTWindow`, `DUIControl`, `DUIButtonControl`, `MTButton`, `MTBmpButton`, `MTStrListBox` and the screens', go by those stored names, and so do their functions.

## The screens

**A screen is a number. The numbers a message may name are the 41 of the zero-ended list `Shell_ScreenIds` (`000817e0`), and each has a case of its own in the table at `00082060`, though some cases (1490, 1499, 1320) make no menu.** The table holds the same 41 numbers as the list, in another order, followed by their targets at `00082104`, case bodies of `Shell_HandleMessage` (`0003c7d8`). Most cases allocate a `0x26`-byte screen object, store their class's vtable, and make it the current menu, then jump to the shared step at `0003f850`, where `Shell_HandleMessage` opens it with `Menu_Open`. The cases, in the table's order: 9999 `0003cab5`, 1490 `0003cccc`, 1499 `0003ccdd`, 1520 `0003cd09`, 1510 `0003cd27`, 3000 `0003cd38`, 3333, 6666, 7770 and 7772 one case `0003ced7`, 7771 `0003d076`, 4444 `0003d215`, 5555 `0003d3b4`, 999 `0003d553`, 1000 `0003d6f2`, 1200 `0003d891`, 1210 `0003da30`, 1220 `0003dbcf`, 1230 `0003dd6e`, 1240 `0003df0d`, 1250 and 1260 `0003e0ac`, 1270 `0003e24b`, 1100 `0003e3ea`, 1110 `0003e589`, 1310 `0003e7eb`, 1320 `0003e9ae`, 1300 `0003ec5d`, 1330 `0003edfc`, 1500 and 1501 `0003ef9b`, 1610 `0003efac`, 1700 and 1710 `0003efcc`, 1800 and 1820 `0003f196`, 1900 `0003f3e8`, 1920 `0003f5a4`, 1910 `0003f5bf`, 2000 `0003f6f8` and 2010 `0003f71a` (`es2_peek.py ES1GO 00082060 41`, `00082104 41`). The handler is the class's slot `+8`:

| Screen | Class (vtable) | Handler | Backdrop |
|---|---|---|---|
| 9999 | `SimLoadMenu` (`00081d7e`) | `SimLoadMenu_OnMessage` (`0003f96d`) | 1000 |
| 1490, 1499 | none: `MessageBox_SetKind` (`00037eac`)(`0009b010`, *n*), with *n* 2 for 1490 and for 1499 0 in `Shell_Mode` 0 and 1 otherwise ([the kinds](message-box.md#the-kinds)) | | 7000, 3000 |
| 1510 | `DebriefingMenu_Ctor` (`0002c422`), [the debriefing](debriefing.md) | `DebriefingMenu_OnMessage` (`0002c638`) | by the career |
| 1520 | while the byte `MessageBox_Kind4Shown` (`00081d20`) is 0, `MessageBox_SetKind`(`0009b010`, 4) and the byte set to 1; then as 1510 | `DebriefingMenu_OnMessage` (`0002c638`) | by the career |
| 3000 | `UnArmedAlert` (`00081da0`) | `UnArmedAlert_OnMessage` (`0002a650`) | |
| 3333, 6666, 7770, 7772 | `UnAssignedAlert` (`00081dc2`) | `UnAssignedAlert_OnMessage` (`0003fae8`) | |
| 7771 | `AnyDisabledAlert` (`00081de8`) | `AnyDisabledAlert_OnMessage` (`0002a581`) | |
| 4444 | `ScrapAlert` (`00081e06`), [SCRAP](repair-and-build.md#scrap--screen-4444) | `ScrapAlert_OnMessage` (`0002f111`) | |
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
| 1310 | `PrefsMenu` (`00081f60`), [PREFERENCES](options.md#preferences--screen-1310): a `0x2c`-byte object | `PrefsMenu_OnMessage` (`0002b82b`) | 1000 |
| 1320 | none: code of its own at `0003e9ae`, which changes the keyboard's subscribers and mode | | 1000 |
| 1300 | `OptionsMenu` (`00081f7e`), [the options menu](main-menu.md#the-options-menu) | `OptionsMenu_OnMessage` (`0002b79d`) | |
| 1330 | `BattlesetAlert` (`00081fa2`), [LOAD BATTLESET](main-menu.md#load-battleset) | `0002bb29` | |
| 1500, 1501 | `WarMapMenu_Ctor` (`0002bbbd`), [the war map](war-map.md) | `WarMapMenu_OnMessage` (`0002c057`) | 6000 |
| 1610 | `CrewAssignMenu_Ctor` (`0002c743`), [CREW](hangar.md#opening-it), then `ShellBackdrop_LoadPalette`(backdrop, 1610), `res32\regn1610.dpl` | | 4000 |
| 1700, 1710 | `ServiceBayMenu` (`00081fc6`); the dword `0007fce4` set to 0, and backdrop 4000's palette loaded again when the screen left is 1610 ([opening the bay](between-missions.md#the-service-bays-bar)) | `ServiceBayMenu_OnMessage` (`0002d327`) | 4000 |
| 1800, 1820 | `ArmingMenu` (`00081fe6`), [ARMING](arming.md#entering-it): a `0x28`-byte object; the dword `0007fce4` set to `0008003c` ([the mouse](menu-input.md#the-mouse)) | `ArmingMenu_OnMessage` (`0002e54c`) | 4000 |
| 1900 | `RepairMenu` (`00082006`), [REPAIR](repair-and-build.md#repair--screen-1900); `0007fce4` set to `0008003c` | `RepairMenu_OnMessage` (`0002f009`) | 4000 |
| 1920 | `SystemRepairMenu_Ctor` (`0002f1aa`)(0), [SYSTEM REPAIR](repair-and-build.md#system-repair--screen-1920); `0007fce4` set to `0008003c` | `SystemRepairMenu_OnMessage` (`0002f8d4`) | |
| 1910 | `RepairPrefsAlert` (`0008202c`), [PREFERENCES](repair-and-build.md#preferences--screen-1910) | `RepairPrefsAlert_OnMessage` (`0003052f`) | |
| 2000 | `Hangar_SelectNext` (`0002986f`)(3), then `FabricationMenu_Ctor` (`000307c9`)(0) makes the menu, [BUILD](repair-and-build.md#build--screen-2000); `0007fce4` set to `0008003c` | `FabricationMenu_OnMessage` (`000309d7`) | 4000 |
| 2010 | `SalvageAllocAlert` (`00082050`), [SALVAGE](repair-and-build.md#salvage--screen-2010) | `SalvageAllocAlert_OnMessage` (`00030ed3`) | |

Every class derives from `Menu` (`00081bd8`), whose slot `+4` is `Menu_Dtor` (`0003b6d1`) and slot `+8` `Menu_OnMessage` (`0003b764`), which does nothing. Each subclass's slot `+4` is its destructor, `MainMenu_Dtor` (`0003b771`) and the 22 others of the same code ([Functions](#functions)): it stores its vtable, then `Menu`'s, then the region's three, and frees the object when bit 0 of its flags is set. The war map's, `WarMapMenu_Dtor` (`0002bf15`), the debriefing's, `DebriefingMenu_Dtor` (`0002c574`), and ARMING's, `ArmingMenu_Dtor` (`000405ae`), do more first ([leaving the war map](war-map.md#leaving-it), [leaving the debriefing](debriefing.md#leaving-it), [leaving ARMING](arming.md#done)). Screen 1320's code is [Open](#open).

### The screen object

| Offset | Holds |
|---|---|
| `+0` | `int16` the menu's number |
| `+2` | the saved background: a buffer of `Region_PixelCount`(`+0xe`) bytes |
| `+6` | the window, kind 0 of the `.DLG` ([the window](menu-controls.md#the-window)) |
| `+0xa` | a `GLRectangleRegion`: its vptr, then at `+0xe` the region `Region_Save` and `Region_Restore` take, type 0 with the window's rect at `+0x12`–`+0x21` ([the build](../formats/dlg-menu-dialogs.md#the-kinds)) |
| `+0x22` | the vtable |

### The backdrop

**Behind every menu is a full-screen picture with its own palette and music, one for each group of screens.** The group of a screen is `Shell_BackdropForScreen` (`00040afa`)(current, screen), which looks the screen up among the 20 dwords at `000821a8` and answers 1000 for 1000, 1200, 1100, 1310, 1320 and 9999; 7000 for 1490; 3000 for 1499; 6000 for 1500 and 1501; 5000 plus the word at `0007ea94` + 2 × the current career record's `+6`, its campaign, for 1510 and 1520 (the table's ten words hold 0, 1, 2, 3, 1, 2, 3, 1, 2 and 3 for campaigns 0 to 9, [the campaigns](../formats/bst-battle-sets.md#battle-sets)); 4000 for 1700, 1710, 1900, 1800, 1820, 2000, 1610 and 5555; and *current* for any other. The current group is the word `Shell_BackdropId` (`000817c4`).

`ShellBackdrop_Show` (`000395a0`)(backdrop, group):

1. sets the word `Shell_MusicTrack` (`00081938`) to the word at `000817d0` + 2 × (group / 1000 − 1), one of 1, 1, 3, 4, 3, 3, 1 and 0 for groups 1000 to 8000; when that differs from the word `00081936` it sets the byte `0007fcd5`, so that [presenting the next frame](../rendering/screen-pages.md#presenting-a-frame) starts that track with `ShellMusic_Play` (`00020a70`), and copies it to `00081936`;
2. destroys the previous picture `ShellBackdrop_Picture` (`000aa7ac`) when there is one (slot `+0x1c`, flags 3);
3. `ShellBackdrop_LoadPalette` (`0003950a`)(backdrop, group): destroys the palette `0007fcd0` when there is one, loads `res32\regn<group>.dpl` with `Resource_LoadPalette` into it, group 3000 taking 3000 plus the word at `0007ea94` + 2 × the career record's `+6`, and sets the byte `0007fcd4`, so the next frame uploads it;
4. loads `res32\regn<group>.dbm` with `Resource_LoadBitmap` into `ShellBackdrop_Picture` and gives it to `ShellBackdrop_SetPicture` (`00021334`)(backdrop + 4, 0, 0, bitmap), which draws it at (0, 0) on page 2, the background ([the pages](../rendering/screen-pages.md#presenting-a-frame)), and sets the region's rect to the bitmap's;
5. opens the group's scripts with `ShellBackdrop_OpenGroupTtms` (`00011f19`)(group), below, and for group 4000 calls `HangarBay_AssignScripts` (`000122e0`) ([giving the scripts their bays](hangar.md#giving-the-scripts-their-bays)).

**The group's scripts.** `ShellBackdrop_OpenGroupTtms` (`00011f19`)(group) keeps the scripts of the backdrop's group open in an array of 3-byte entries, the dword `ShellBackdrop_GroupTtms` (`0007e718`), their count the word `ShellBackdrop_GroupTtmCount` (`0007e71c`). When the count is above 0 it deletes the array with `__vec_delete` and the entries' destructor `TtmEntry_Dtor` (`00011edd`) and clears the dword and the count. It sets the word `ShellBackdrop_GroupTtmList` (`0007e716`) to group / 1000 − 1, and when the count of `cfg\tm_files.dat`'s lists, the byte `0007e714`, is not 0, takes the count of the list `0007e716` of `0007e710` ([the animation lists](../formats/shell-file-lists.md#the-animation-lists)) into `0007e71c`; when that is not 0 it makes that many entries with `__vec_new` and their constructor `TtmEntry_Ctor` (`0001201b`), and opens each name of the list with `Ttm_Open`(0, 0, 0, name), playing on the default render context, its handle into the entry's byte `+0`. A group whose list is empty leaves the dword 0. An entry is a handle byte, −1 from the constructor, and the word `+1`, −99 from it; the destructor closes a handle that is not −1 with `Ttm_Close` and sets it to −1, sets the bytes `+1` and `+2` to −99 and −1, and frees the entry when bit 0 of its flags is set. Message kinds 5 to 11 hide entries 3 and 6 while they are up ([the kinds](message-box.md#the-kinds)).

`ShellMusic_Play` (`00020a70`)(track), with a sound manager (`00082768`), calls `00020a18`, makes the sound object `ShellMusic_Object` (`0007fcfc`) with `SoundManager_CreateObject`(manager, 2, 0, track), loads it with `SoundObject_LoadSample`, calls its slot `+0x28` with (−1, −1, `0xffff`), and plays it through slot `+0x10` when the byte `0007fcb8` is set, which `prefs.cfg` option 0 sets ([the shared data](startup.md#the-shared-data)).

The backdrop is the object `Shell_Backdrop` (`000a09a0`), `0x1c` bytes with a `GLRectangleRegion` at `+4`: `ShellBackdrop_Ctor` (`0003918e`) builds the region with a zero rect and clears `ShellMusic_Object` (`0007fcfc`); `ShellBackdrop_Dtor` (`00039422`) destroys the palette `0007fcd0` and clears it. `ShellBackdrop_SetPicture` with no bitmap zeroes the rect's right `+0x10` (twice) and leaves its bottom `+0x14`. `ShellBackdrop_Draw` (`0002141c`)(backdrop + 4) copies the region from page 2 to the draw page and adds a dirty box of delay 0 at the rect's corner whose width and height are the rect's right `+0x10` and bottom `+0x14` themselves: for the full-screen picture, 319 × 199 from (0, 0), before `DirtyBox_Add`'s rounding ([dirty boxes](../rendering/screen-pages.md#dirty-boxes)).

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
3. sets controls' states by the number, finding each with `DUIWindow_FindControlById` (`0001be16`), which takes a control in any state, or `DUIWindow_FindEnabledControlById` (`0001bda4`), which passes over inactive ones; it makes a control inactive by writing state 2 to `+0x19` and 1 to *changed* `+0x1f` itself, with no call of the control's slot `+0xc`. For the main menu, 1000: with no careers in the log (`0009b210`, [the careers log](../formats/career-log-and-player-mec.md#loading)'s count), button 1700, found in any state, is made inactive. For 1100 it selects the line `Mission_Kind` (`000aad5a`) holds in the list 1101 ([the SINGLE MISSION menu](single-mission.md#the-single-mission-menu)). 1200's are the career menu's ([the career menu](career.md#the-career-menu)). 1920's and 2000's are [SYSTEM REPAIR's](repair-and-build.md#system-repair--screen-1920) and [BUILD's](repair-and-build.md#build--screen-2000). For 1610 it frames tile 1615 in color 4 (slot `+0x14`, [the picture tile](menu-controls.md#the-picture-tile)) and makes NONE (1623) inactive ([CREW](hangar.md#opening-it)). For 1800 it makes MISSILE TYPE (1111) inactive and calls the animation 1803's slot `+0x24` with 0, and for 1820 it makes 1111 inactive only ([ARMING](arming.md#entering-it)), after 1610's. For 1310 with no sound manager (`00082768` 0) it makes MUSIC (1311), SOUND (1312) and SPEECH (1317) inactive ([PREFERENCES](options.md#opening-it)), and 1700's in `Shell_Mode` 1 are [the service bay's](between-missions.md#the-service-bays-bar);
4. unless the number is 9999, puts the focus on the first control whose key is `Space` (`0x39`), or on control 0, with `DUIWindow_PointAtControl` (`0001c3ec`), and stores it in `DUIWindow_FocusIndex` (`0007f0d8`);
5. for 1900 only, after the focus: makes control 1901 inactive when the word `Hangar_HercCount` (`00080892`), less 1 when `Hangar_HasMarkedHerc` (`0002a277`) answers non-zero, is below 2 ([REPAIR](repair-and-build.md#repair--screen-1900));
6. allocates the saved background, `Region_PixelCount` of the region `+0xe` bytes with `Mem_New`, zeroed with `memset`, at `+2`, and sets the byte `DUIWindow_InputReady` (`0007f0dc`).

**The build.** `Menu_BuildFromDlg` (`0003a1f7`)(menu, number), whose one call `es2_xref.py` finds is `Menu_Open`'s, opens `eng\menu<n>.bin` through `VolRStream_CtorFromPath` and, when that opens, reads it into a text table with `Text_LoadDataFile`; then it opens `dlg\menu<n>.dlg`, reads the count, and for each record reads the four rect words into the low halves of four zeroed dwords with `VolRStream_Read`, then through the stream's slot `+0x1c` the bytes kind, style and key, in the file's order, and the id as two bytes. It handles the record by its kind ([the kinds](../formats/dlg-menu-dialogs.md#the-kinds)), then counts it: the counter (`[EBP-0x114]`) is the record's text line, and every record advances it, one that builds nothing included (the loop's end `0003b0e3` is also id 1211's target). At the end it closes and destroys both streams and the table.

What the build does beyond [the kinds' table](../formats/dlg-menu-dialogs.md#the-kinds):

- **Kinds 0, 1, 4 and 5**, before the text: the five ids at `0008196c` jump through the targets at `00081980`. 1211 (`0003b0e3`) skips the record; the other four take a text other than the record's own line and then go on to the kind's code as any record does, all from the current career record, `0009b23b` + `0x1f7` × the word `0009b21f` ([the career record](../formats/bst-battle-sets.md#the-career-record)): 5556 (`0003a5c8`) line (the record's word `+0xa8`) + 2 of the menu's own table; 5557 (`0003a60e`) line (that word) + 4 of it; 5558 (`0003a654`) line (the record's word `+0xaa`) + `0x1b` of `eng\watext.bin` (`0009afa8`) when the word `+0xa8` is 0, and line (`+0xaa`) + `0xd5` of it otherwise; 1223 (`0003a705`) the text `Roster_SaveSlotText` (`0001838d`)(`Career_Roster`, `0009b218`) answers, the backup line of the overwrite warning ([SAVE and the overwrite warning](career.md#save-and-the-overwrite-warning)).
- **Kind 0** passes the text's length (`REPNE SCASB`) in the high word of the dword whose low word is the id; `MTWindow_Ctor` reads only the low word. A style other than 0 or 1 builds no window and leaves `+6` as it was, and the rect is still copied to `+0x12`.
- **Kind 3**: style 1 takes its text, the record's own line, only when the key byte is 0, and otherwise passes no text. Both button constructors get the key as the low byte of the dword read from the key byte's address, which holds the style, a stack byte and the id's low byte above it. Menu 1300's button 100 looks `Shell_ReturnScreen` up among the four values at `000819e0`, targets `000819f0`: 1000 the record's own line (`0003a863`), 1100 the line after it (`0003a87d`), 1710 and 1200 the line two after (`0003a89b`); for any other value it reads no line and keeps the text pointer (`[EBP-0x208]`) the previous record left ([the return screen](main-menu.md#the-return-screen)).
- **Kind 14**: each of the 17 ids at `00081b08` adds a hotspot with `DUIWindow_AddHotspot`(window, *a*, id, handler, rect), the targets at `00081b4c` differing only in the handler and, for 1502, *a*: 1612–1614 `PilotPortraitHotspot_Draw` (`00017a19`); 1619–1622 `PilotLinesHotspot_Draw` (`00017b4a`); 1615–1618 `BayNumberHotspot_Draw` (`00017d9f`); 1231 `CareerStatsHotspot_Draw` (`0001705e`); 1522 `DebriefingResultsHotspot_Draw` (`000175d7`); 1502 `WarMapMenu_MapAreaNoOp` (`0002bbaf`) with *a* the rect `ShellMap_Rect` (`000a02d4`); 2005 `FabricationMenu_DrawPanel` (`0002845c`); 1803 `Arming_DrawWeaponInfo` (`0002d5bf`); 1811 `Arming_DrawMissileInfo` (`0002dacf`). Any other id builds nothing. The handlers are CREW's ([the screen](hangar.md#the-screen)), BUILD's ([BUILD](repair-and-build.md#build--screen-2000)), ARMING's ([the panel](arming.md#the-panel)) and the others' pages'.
- **Kind 15** calls `Menu_ListLines`(id) before it looks the id up among the 23 at `00081a00` (1314, 1315, 1316, 1318, 1116–1120, 1325–1327, 1903, 1904, 1905, 1907, 1923, 1906, 1927, 1202, 1922, 1808, 1223), so a list whose id is among them has its lines built before its own code runs. The selected word (`[EBP-0xfc]`) and the alignment (`[EBP-0xf8]`, the list's `+0x48`, [drawing the lines](menu-controls.md#the-list-box)) are zeroed for each record before the lookup, and the targets at `00081a5c` set them:

| Ids | Case | Selected word | Alignment |
|---|---|---|---|
| 1314, 1315 | `0003a973`, `0003a986` | the byte `Shell_MusicOption` (`0007fcb8`), `Shell_SoundOption` (`0007fcb9`) | 0 |
| 1316 | `0003a999` | the word `Shell_DetailOption` (`0007fcba`) | 2 |
| 1318 | `0003a9b5` | the word `Shell_RadioOption` (`0007fcbc`) | 0 |
| 1903 | `0003aa57` | the chassis of `Hangar_SelectedSlot` (`00080894`)'s HERC, −1 for slot −1 or an empty slot | 2, centered, unless the menu is 1800 or 1820 |
| 1904 | `0003aac4` | `PilotEntry_NameIndex` (`00015f50`)(`CareerRecord_Current`, its current pilot `+0x12`) + 1 | as 1903 |
| 1905 | `0003ab09` | `Repair_LevelForCondition` (`000389fb`) of `Herc_OverallCondition` (`00024b6a`) of the selected HERC, of 0 for none | 2 |
| 1906, 1907, 1923, 1927 | `0003ab50` | 0 | 2 |
| 1202 | `0003ab5f` | the word `0007fcea` | 0 |
| 1922 | `0003ab71` | `Repair_LevelForCondition` of `Herc_GetStatus` (`00024b86`)(the selected HERC, 0, `0x784`), of 0 for none | 2 |
| 1808 | `0003abdd` | 0; it first writes the counts, `Inventory_FormatCounts` (`00028242`)(`[000aad62]`, the lines) ([the counts](arming.md#the-weapon-list)) | 0 |
| 1223 | `0003abf0` | the word `000809b4` | 0 |

1314 to 1318 are [PREFERENCES'](options.md#opening-it). The cases of 1116–1120 (`0003a9c7` onward) are [the MISSION PARAMS menu's](single-mission.md#the-mission-params-menu); those of 1325–1327, up to `0003aa45`, are for ids that none of the retail `.DLG` files holds ([PREFERENCES](options.md#opening-it)).
- **Kind 16** passes `Menu_LoadControlBitmaps` (`00037827`)(id), the bank, and a word, −1, zeroed or set for the ten ids at `00081ab8`, targets `00081ae0`: 1321 (`0003ac78`) and 1611 (`0003ac98`) 0; 1316 (`0003ac86`) the word `0007fcba`; 1615 to 1618 (`0003aca6`) the chassis of the HERC in the slot `+8` of pilot entry id − 1615 of the roster's current record (`0009b218` + `0x23` + `0x1f7` × the roster's word `+7`), −1 for a slot of −1 or an empty one; 1914, 1915 and 1916 (`0003ad45`, `0003ad65`, `0003ad87`) 1 when `Career_AutoRepairMode` (`0007fcf8`) is 0, 1 or 2 respectively, and 0 otherwise ([the picture tile](menu-controls.md#the-picture-tile)).
- A control is added with `DUIWindow_AddControl`, which also adds its key ([the window](menu-controls.md#the-window)). Text buttons carry keys as icon buttons do: button 99 of menu 1230 `0x01` Esc, button 1624 of menu 1610 `0x39` Space. Some labels carry a non-zero key byte, which the build does not read.

`Menu_ListLines` (`0003750d`)(id) looks the id up among the 28 at `000812f4` (1921, 1924, 1925, 1926, 1907, 1906, 1927, 1923, 1116, 1117, 1119, 1120, 1118, 1314, 1315, 1316, 1318, 1331, 1101, 1002, 1202, 1904, 1903, 1905, 1922, 1808, 1809, 1223), targets at `00081364` in bytes Ghidra left undefined; 1002's (`00037706`) returns `CareerLog_CurrentNameArray` (`000182f8`)(`Career_Log`, `0009b20c`), the main menu's list ([the main menu's list](main-menu.md#the-list)), 1101's and 1116–1120's are the single mission's menus' ([a single mission](single-mission.md)), and 1202's and 1223's the career's ([the list](career.md#the-list), [the backup list](career.md#the-backup-list)). ARMING's four are `WaText_LinesFromList` of a word list: 1904 (`00037728`) of `000811e8`, `eng\watext.bin` `0x55`–`0x7a`, its first line pointer replaced in `Shell_Mode` 1 by the dword at `Career_Roster` (`0009b218`)'s `+1`; 1903 (`00037760`) of `00081250`, `0x1b`–`0x22`; 1808 (`00037782`) of `Arming_BuildWeaponList` (`000371ad`)(−1, −1); 1809 (`0003779a`) of `000812c4`, `0x40`, `0x41`, `0x42` and `0x44` ([the window](arming.md#entering-it)). PREFERENCES' four are the same: 1314 and 1315 (`000376b1`) of `PrefsMenu_OffOnLines` (`000811a8`), 1316 (`000376c2`) of `PrefsMenu_DetailLines` (`000811b0`) and 1318 (`000376d3`) of `PrefsMenu_SpeechLines` (`000811bc`) ([opening PREFERENCES](options.md#opening-it)). Any other id gets `WaText_LinesFromList` (`000373fa`)(`000811a4`): a new zero-ended array of the lines of `eng\watext.bin` that the `int16` words of the list number, as many as `WordList_CountNonNegative` (`000373d3`) counts before the first negative word. The list at `000811a4` opens with −1, so that array holds only its end, and the list box is empty. Kind 16's bank is `Menu_LoadControlBitmaps`' ([the picture tile](menu-controls.md#the-picture-tile)).

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
2. with `Shell_BackdropChanged` (`000817c6`) clear, that is after a change of screen within the backdrop's group: on screens 1700 and 1710, the player's bay is shown again: `Hangar_ShowSlot` (`00029651`)(the slot `+8` of `CareerRecord_Current`'s pilot entry `+0xe`, the player's) ([showing a bay](hangar.md#showing-a-bay)), between `Screen_BlindsClear` (`0004c053`) and `Screen_BlindsCopy` (`0004bc0d`) when the record's current pilot `+0x12` differs from `+0xe` and the message box is not up (`0009b013`), and then `CareerRecord_SelectPilot` (`00015ad4`)(record, `+0xe`); on any other screen nothing. With it set, after a new backdrop: unless `0007fcd7` is set or the screen is 1000, the draw page and the visible page are cleared to color 0 (`VGA8_ClearPage`, `0008663c`); with `0007fcd7` 2 the `.ANM` at `000aacfe` is played with `Anm_Play`(1, `0xa`, name) ([playing](../formats/anm-animation.md#playing)) and `0007fcd7` cleared; the backdrop is drawn with `ShellBackdrop_Draw`; and backdrop 4000 calls `ShellBackdrop_PlayAllScripts` (`0001240b`), `Hangar_ShowSlot` of the player's slot and `CareerRecord_SelectPilot`(record, `+0xe`) with no blinds, any other `ShellBackdrop_RunScripts` (`00012467`) ([running the scripts](hangar.md#running-the-scripts));
3. `DigitalDialog_ShowMessage`(`0009b010`), which loads the message box's line while it is to be shown ([showing a message](message-box.md#showing-a-message)), and `Ttm_RunAll`;
4. with no current menu, only sets the draw page back to the one it found; with a current menu: makes the render contexts `0007fcdc` with `RenderContext_GetForRect` (`0004c88a`) and `0007fcd8` with `RenderContext_GetFullScreenAround` (`0004c53c`), both of the window's rect, and makes `0007fcd8` alone the scripts' default with `Ttm_SetDefaultContext` (`0003c4b2`–`0003c4b8`), installing neither; `0007fcdc` is only stored. It sets the source and draw pages to page 2, and saves the background under the window into `+2` with `Region_Save`, in whatever render context is installed. `RenderContext_GetForRect` sets the context `00083bc8`, made with `GLViewport_Ctor` (`000512a0`)(0) on first use, to the rect, origin (0, 0), no clip regions (`+4` 0), clip mode 1 and translation 0; `RenderContext_GetFullScreenAround` sets the full-screen context `00083bc4`, the object `RenderContext_GetFullScreen` also sets, to (0, 0)–(319, 199), origin (0, 0), clip mode 2 and translation 0, with four clip regions, the 24-byte entries at `000a0a9c`: (0, 0, 319, top − 1), (0, top, left − 1, bottom), (right + 1, top, 319, bottom) and (0, bottom + 1, 319, 199), the screen above the rect, left of it, right of it and below it, so that the scripts drawn through `0007fcd8` stay off the window. Unless the message box is up (the byte `0009b013`, `Shell_MessageBox` (`0009b010`)'s `+3`), it draws the window on page 2 with `DUIWindow_Draw` (`0001bfa3`), first, while the byte `000817bd` is not 0, the war map's briefing under it on both pages ([leaving it](war-map.md#leaving-it)); then sets the draw page back. Still unless the message box is up: on backdrop 1000, except screen 9999, it adds the whole screen as a dirty box, `DirtyBox_Add`(1, 200, 320, 0, 0), so the next frames copy page 2 onto both pages; otherwise it draws the window again on the draw page and, while `000809a8` is set, sets `000817bd` to 0 when it is not 0 and otherwise calls `ShellMap_ResetViewAndClear` (`00009c64`)(`Shell_MissionMap` (`000809a4`)) when the map is set, and sets `000809a8` to 3. It sets the byte `0007fcd6`, which makes the next frame wait 0.1 s ([presenting a frame](../rendering/screen-pages.md#presenting-a-frame));
5. with `0007fcd7` 1 uploads the palette `0007fcd0`, clears `0007fcd4`, plays the `.ANM` with `Anm_Play`(1, `0xb`, name) and clears `0007fcd7`;
6. puts the source page back, adds the whole screen as a dirty box with delay 0, shows the cursor again unless the screen is 9999 (slot 30, `00086628`), and sets `DUIWindow_InputReady`.

**Each pass after that** the frame loop's step 5 runs `Shell_TickScreen` (`0003c115`): unless the message box is up it updates the current menu's window with `DUIWindow_Update` (`0001c067`, [updating a window](menu-controls.md#updating-a-window--duiwindow_update-0001c067)); while the object `Shell_MissionMap` (`000809a4`) is set, the byte `000809a8` is above 1 and `0007fcd4` is clear it runs the map's step `ShellMap_IntroStep` (`00009cfb`) and lowers `000809a8` ([the map's tick](map.md#the-tick)); it sets it to 2 when it is 1 and the object's byte `+0x4c0` is clear, and to 3 when it is 1 and `+0x4c2` is set; these last two read the object without testing that `Shell_MissionMap` is set ([Open](#open)).

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
| `Raster_DrawLineColour`, `GLViewport_Ctor`, `RenderContext_GetFullScreenAround`, `RenderContext_GetForRect` | `0004ba84`, `000512a0`, `0004c53c`, `0004c88a` |
| `WordList_CountNonNegative`, `Roster_SaveSlotText`, `PilotPortraitHotspot_Draw` | `000373d3`, `0001838d`, `00017a19` |

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
- **Open:** the byte `000809a9` that `Shell_HandleMessage` tests, and whether `Shell_TickScreen` can find `000809a8` at 1 with no `Shell_MissionMap` (`000809a4`), its last two tests reading the object unchecked; [the map](map.md) holds the rest of the object and `000809a8`. The byte `000817bd` keeps the war map's briefing drawn behind an alert ([leaving it](war-map.md#leaving-it)), and `Shell_DrawScreen` reaches the map's slot `+4` through it after the map is deleted ([the loading screen](war-map.md#the-loading-screen-after-the-unarmed-alert)).
- **Open:** screen 1320's code at `0003e9ae`.
