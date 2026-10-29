# GO.EXE — the menus

**Every screen of ES1's shell between the intro and a mission is a menu: a picture behind it, a framed window with a title, labels and buttons, laid out by the menu's `.DLG` file ([the menu dialogs](../formats/dlg-menu-dialogs.md)). A button is pressed with the mouse or with `Enter`, and acts when it is let go over the same button; its number names the next screen. The shell then closes the open menu, changes the picture and the music behind when the next screen belongs to another group, builds the next menu and draws it.** What a player would see:

- pointing at a button gives it the focus, and `Tab`, `Shift+Tab` and `Alt+Tab` move the focus to the next or previous button and put the pointer on it ([the keys](#the-keys));
- a button pressed and then dragged off pops back up, and acts only if the pointer comes back over it before the release ([the mouse](#the-mouse));
- a pressed text button clicks when the byte `0007fcb9` is set, which `prefs.cfg` option 1 sets ([drawing a control](#drawing-a-control));
- a menu opens with the pointer on the button whose key is `Space`, or on its first control ([opening a menu](#opening-a-menu--menu_open-0003969e)).

Addresses are `ES1GO` image addresses ([the X-32 executable](../formats/x32-executable.md#the-image)). The functions here remove their own arguments, so argument lists are in stack order ([calling conventions](../formats/compiler.md#calling-conventions)). The frame loop that runs a screen is [the start-up's](startup.md#the-frame-loop).

## Where the object files start and end

**`shell.cpp`** runs from `Shell_StockItemsFromFlags` (`00038e48`) to `00040b93`. The file before ends with its static initializer `00038e2f` (the static constructor table's entry `0009a264`), whose `RET` at `00038e46` is followed by one zero byte. `Shell_ProcessMissionResults`' asserts name `src\shell.cpp` (lines `0xc7` and `0xd7`). Its last code is its static initializer, `Shell_StaticInit` (`00040b74`, entry `0009a268`), which builds [the backdrop](#the-backdrop), and the exit routine `Shell_StaticDtor` (`00040b83`, exit table entry `0009a2a0`), which destroys it; that `RET` ends at `00040b93`, on a 4-byte boundary. `00040b94`, a stub taking three arguments and returning the first, follows, and then the word stacks from `WordStack_Push` (`00040ba6`), whose last `RET 8`, at `00040d50`, is followed by one zero byte and `DirtyList_CreateContext` (`00040d54`) ([Open](#open)). The file's data holds the class names and vtables of its screens from `00081bd0` to `00082058`, and its copies of `ClassItem`'s, `GLBase`'s, `GLRegion`'s and `GLRectangleRegion`'s vtables (`0008186c`, `00081896`, `000818c8`, `00081900`) with the three destructors `GLBase_DtorShellCopy` (`00039342`), `GLRegion_DtorShellCopy` (`0003936e`) and `GLRectangleRegion_DtorShellCopy` (`000393b7`) in their slot `+0x1c`.

**The window file** runs from `0001a4f4`, after `HercStatus_Set`'s `RET 0x10` at `0001a4f0` and one zero byte, to `DUIWindow_PointAtControl` (`0001c3ec`), whose `RET 8` at `0001c45c` is followed by one zero byte. It holds `DUIControl` and `DUIWindow`, with its own copies of the region classes' vtables (`0007f00c`, `0007f036`, `0007f068`, `0007f0a0`) and their destructors (`GLBase_DtorWindowCopy`, `0001aec1`, and the two after it).

**The dialog file** runs from `ShellMouse_AddCallbacks` (`0001c460`) to `DUIWindow_HandleNavKey` (`0001cc86`), whose `RET 4` at `0001cde4` is followed by one zero byte. It holds the active window and the mouse's callbacks.

**The button file** starts at `MTButton_Ctor` (`0001cde8`). It holds a second copy of `DUIControl`'s vtable (`0007f1ea`), `DUIButtonControl` (`0007f130`), `MTButton` (`0007f18c`), `MTBmpButton` (`0007f24a`), and the slider and list classes from `DUISliderControl` (`0007f2f8`) on ([Open](#open)).

The image stores each class's name just before its vtable, and the word before the vtable is the name's offset back from it: `Menu`'s vtable `00081bd8` has −8 at `00081bd6`, its name at `00081bd0`. The classes here, `DUIWindow`, `MTWindow`, `DUIControl`, `DUIButtonControl`, `MTButton`, `MTBmpButton`, `MTStrListBox` and the screens', go by those stored names, and so do their functions.

## The screens

**A screen is a number. The numbers a message may name are the zero-ended list `Shell_ScreenIds` (`000817e0`); 41 of them build something of their own when opened, by the table at `00082060`, and the rest only become the current screen.** The table's 41 numbers are followed by their targets at `00082104`, case bodies of `Shell_HandleMessage` (`0003c7d8`). Most cases allocate a `0x26`-byte screen object, store their class's vtable, and make it the current menu; `Shell_HandleMessage` then opens it with `Menu_Open`. The handler is the class's slot `+8`:

| Screen | Class (vtable) | Handler | Backdrop |
|---|---|---|---|
| 9999 | `SimLoadMenu` (`00081d7e`) | `SimLoadMenu_OnMessage` (`0003f96d`) | 1000 |
| 1490, 1499, 1520 | none: `00037eac`(`0009b010`, *n*), with *n* 2 for 1490 and for 1499 0 in `Shell_Mode` 0 and 1 otherwise; 1520 also sets the byte `00081d20` | | 7000, 3000, by the career |
| 1510 | `DebriefingMenu_Ctor` (`0002c422`) | | by the career |
| 3000 | `UnArmedAlert` (`00081da0`) | `0002a650` | |
| 3333, 6666, 7770, 7772 | `UnAssignedAlert` (`00081dc2`) | `UnAssignedAlert_OnMessage` (`0003fae8`) | |
| 7771 | `AnyDisabledAlert` (`00081de8`) | `0002a581` | |
| 4444 | `ScrapAlert` (`00081e06`) | `0002f111` | |
| 5555 | `TechReleaseAlert` (`00081e2c`) | `0002a6a9` | 4000 |
| 999 | `QuitAlert` (`00081e48`) | `0002a554` | |
| 1000 | `MainMenu` (`00081bf4`) | `0002a70a` | 1000 |
| 1200 | `CareerMenu` (`00081c12`) | `0002aa0e` | 1000 |
| 1210 | `NewCareerAlert` (`00081e6a`) | `0002aeec` | |
| 1220 | `CareerRecordsMenu` (`00081e90`) | `0002af94` | |
| 1230 | `CareerStatsAlert` (`00081eb4`) | `0002b2cc` | |
| 1240 | `SaveWarningAlert` (`00081ed8`) | `0002b30a` | |
| 1250, 1260 | `KIAWarningAlert` (`00081efa`) | `0002b34c` | |
| 1270 | `DeleteAlert` (`00081f1a`) | `0002ae96` | |
| 1100 | `MissionMenu` (`00081c3a`) | `0002b375` | 1000 |
| 1110 | `MissionOptionsAlert` (`00081f42`) | `0002b4fe` | |
| 1310 | `PrefsMenu` (`00081f60`) | `0002b82b` | 1000 |
| 1320 | none: code of its own at `0003e9ae`, which changes the keyboard's subscribers and mode | | 1000 |
| 1300 | `OptionsMenu` (`00081f7e`) | `0002b79d` | |
| 1330 | `BattlesetAlert` (`00081fa2`) | `0002bb29` | |
| 1500, 1501 | `WarMapMenu_Ctor` (`0002bbbd`) | | 6000 |
| 1610 | `0002c743`, then `ShellBackdrop_LoadPalette` | | 4000 |
| 1700, 1710 | `ServiceBayMenu` (`00081fc6`) | `0002d327` | 4000 |
| 1800, 1820 | `ArmingMenu` (`00081fe6`) | `0002e54c` | 4000 |
| 1900 | `RepairMenu` (`00082006`) | `0002f009` | 4000 |
| 1920 | `0002f1aa` | | |
| 1910 | `RepairPrefsAlert` (`0008202c`) | `0003052f` | |
| 2000 | `0002986f`, `000307c9` | | 4000 |
| 2010 | `SalvageAllocAlert` (`00082050`) | `00030ed3` | |

Every class derives from `Menu` (`00081bd8`), whose slot `+4` is `Menu_Dtor` (`0003b6d1`) and slot `+8` `Menu_OnMessage` (`0003b764`), which does nothing. Each subclass's slot `+4` is its destructor, `MainMenu_Dtor` (`0003b771`) and the 22 others of the same code ([Functions](#functions)): it stores its vtable, then `Menu`'s, then the region's three, and frees the object when bit 0 of its flags is set. The screens other than the main menu's and 9999's are later modules' ([Open](#open)).

### The screen object

| Offset | Holds |
|---|---|
| `+0` | `int16` the menu's number |
| `+2` | the saved background: a buffer of `Region_PixelCount`(`+0xe`) bytes |
| `+6` | the window, kind 0 of the `.DLG` ([the window](#the-window)) |
| `+0xa` | a `GLRectangleRegion`: its vptr, then at `+0xe` the region `Region_Save` and `Region_Restore` take, type 0 with the window's rect at `+0x12`–`+0x21` ([the build](../formats/dlg-menu-dialogs.md#the-kinds)) |
| `+0x22` | the vtable |

### The backdrop

**Behind every menu is a full-screen picture with its own palette and music, one for each group of screens.** The group of a screen is `Shell_BackdropForScreen` (`00040afa`)(current, screen), which looks the screen up among the 20 numbers at `000821a8` and answers 1000 for 1000, 1200, 1100, 1310, 1320 and 9999; 7000 for 1490; 3000 for 1499; 6000 for 1500 and 1501; 5000 plus the word at `0007ea94` + 2 × the current career record's `+6` for 1510 and 1520; 4000 for 1700, 1710, 1900, 1800, 1820, 2000, 1610 and 5555; and *current* for any other. The current group is the word `Shell_BackdropId` (`000817c4`).

`ShellBackdrop_Show` (`000395a0`)(backdrop, group):

1. sets the word `Shell_MusicTrack` (`00081938`) to the word at `000817d0` + 2 × (group / 1000 − 1), one of 1, 1, 3, 4, 3, 3, 1 and 0 for groups 1000 to 8000; when that differs from the word `00081936` it sets the byte `0007fcd5`, so that [presenting the next frame](../rendering/screen-pages.md#presenting-a-frame) starts that track with `ShellMusic_Play` (`00020a70`), and copies it to `00081936`;
2. destroys the previous picture `ShellBackdrop_Picture` (`000aa7ac`) when there is one (slot `+0x1c`, flags 3);
3. `ShellBackdrop_LoadPalette` (`0003950a`)(backdrop, group): destroys the palette `0007fcd0` when there is one, loads `res32\regn<group>.dpl` with `Resource_LoadPalette` into it, group 3000 taking 3000 plus the word at `0007ea94` + 2 × the career record's `+6`, and sets the byte `0007fcd4`, so the next frame uploads it;
4. loads `res32\regn<group>.dbm` with `Resource_LoadBitmap` into `ShellBackdrop_Picture` and gives it to `ShellBackdrop_SetPicture` (`00021334`)(backdrop + 4, 0, 0, bitmap), which draws it at (0, 0) on page 2, the background ([the pages](../rendering/screen-pages.md#presenting-a-frame)), and sets the region's rect to the bitmap's;
5. calls `00011f19`(group), and for group 4000 `000122e0`.

`ShellMusic_Play` (`00020a70`)(track), with a sound manager (`00082768`), calls `00020a18`, makes the sound object `0007fcfc` with `SoundManager_CreateObject`(manager, 2, 0, track), loads it with `SoundObject_LoadSample`, calls its slot `+0x28` with (−1, −1, `0xffff`), and plays it through slot `+0x10` when the byte `0007fcb8` is set, which `prefs.cfg` option 0 sets ([the shared data](startup.md#the-shared-data)).

The backdrop is the object `Shell_Backdrop` (`000a09a0`), `0x1c` bytes with a `GLRectangleRegion` at `+4`: `ShellBackdrop_Ctor` (`0003918e`) builds the region with a zero rect and clears `0007fcfc`; `ShellBackdrop_Dtor` (`00039422`) destroys the palette `0007fcd0` and clears it. `ShellBackdrop_Draw` (`0002141c`)(backdrop) copies the region from page 2 to the draw page.

## Opening and closing a screen

### The first screen — `Shell_OpenFirstScreen` (`0003ba47`)

`Shell_OpenFirstScreen`(exit code), [the start-up's](startup.md#the-start-up) step 15, sets up the scripts and then, by the code:

- **3 or 4**, back from a mission: `Mission_ReadRestore` and `Shell_ProcessMissionResults`, and `Shell_PreviousScreen` (`000817c2`) set to 999. In `Shell_Mode` 0, when the current career record's pilot is fit (`PilotEntry_Condition` 0) the backdrop 1000 and `MissionMenu`, screen 1100 (`Shell_CurrentScreen` (`000817c0`) 1100, `Shell_BackdropId` 1000); otherwise `00037eac`(`0009b010`, 3) and the backdrop 5000. In mode 1, a fit pilot plays `endfa.ttm` (with music track 7) when `Shell_PlayIntro` is set and opens the home menu; otherwise by `Career_PeekNextCampaign`: `c`, the campaign won, music 13 and `end_win1.ttm` to `end_win3.ttm` and `intro\earth.ttm`, then the home menu; −1 music 19 and `failed.ttm` and `intro\earth.ttm`, then the home menu; any other `00037eac`(`0009b010`, 7), `000aad5c` set by `00037bf0`(7) and the backdrop 4000. Mode 1 then calls `0001601b`, `00016fdd` and `000168b0` on the careers block.
- **6**, back from a demo, and **any other** after the intro ([the start-up](startup.md#the-start-up)): `anm\esiege2.anm` put into `000aacfe` with the byte `0007fcd7` set to 2, so [the draw](#drawing-a-screen) plays it; the backdrop 1000; a `MainMenu` made the current menu `Shell_CurrentMenu` (`000817c8`) and opened with `Menu_Open`(menu, 1000); `Shell_CurrentScreen` 1000, `Shell_PreviousScreen` 999 and `Shell_BackdropId` 1000.

`Shell_OpenHomeMenu` (`0003b3db`) does the same as the main menu's case with `anm\esiege2.anm` and the backdrop 1000, then in `Shell_Mode` 0 opens `MainMenu` as 1000 and in mode 1 `CareerMenu` as 1200, setting `Shell_CurrentScreen` and the word `0007fce8` to that number.

`Shell_PlayTtm` (`0003b8df`)(name) calls the raster slot `0008663c` with (the draw page, 0) and (page 2, 0), shows a frame, calls it again for the draw page, makes the full-screen render context `0007fcd8` the scripts' default, opens the script with `Ttm_Open` and runs it with `Ttm_RunScript`, a frame each, until it ends or a key or the first button comes in (`Input_PollDeviceBlock`, its word `+0` or byte `+0xa`); one cut short sets the byte `000817bc`, which skips `lead_in.ttm` ([the start-up](startup.md#the-start-up)). Then `Ttm_Close` and `0007fcd0` cleared.

### Opening a menu — `Menu_Open` (`0003969e`)

`Menu_Open`(menu, number):

1. stores the number at the menu's `+0`, sets the byte `Shell_SyncPointer` (`0007fd07`), and builds the window and its controls with `Menu_BuildFromDlg` ([the build](../formats/dlg-menu-dialogs.md#the-kinds));
2. makes the window the active one with `DUIWindow_SetActive` (`0001c536`) ([the active window](#the-active-window));
3. sets controls' states by the number. For the main menu, 1000: with no careers in the log (`0009b210`, [the careers log](../formats/career-log-and-player-mec.md#loading)'s count), button 1700 is made inactive, state 2. The other numbers' changes (1310, 1920, 1100, 1200, 2000, 1610, 1800, 1820, 1700 and 1900) are later modules';
4. unless the number is 9999, puts the focus on the first control whose key is `Space` (`0x39`), or on control 0, with `DUIWindow_PointAtControl` (`0001c3ec`), and stores it in `DUIWindow_FocusIndex` (`0007f0d8`);
5. allocates the saved background, `Region_PixelCount` of the region `+0xe` bytes, zeroed, at `+2`, and sets the byte `DUIWindow_InputReady` (`0007f0dc`).

**The build.** `Menu_BuildFromDlg` (`0003a1f7`)(menu, number), whose one call `es2_xref.py` finds is `Menu_Open`'s, opens `eng\menu<n>.bin` through `VolRStream_CtorFromPath` and, when that opens, reads it into a text table with `Text_LoadDataFile`; then it opens `dlg\menu<n>.dlg`, reads the count, and for each record reads the four rect words into the low halves of four zeroed dwords, the three bytes and the id. It handles the record by its kind ([the kinds](../formats/dlg-menu-dialogs.md#the-kinds)), then counts it. At the end it closes and destroys both streams and the table.

`es2_xref.py --decode` finds one write of `Shell_SyncPointer`, `Menu_Open`'s, and one read, `Shell_PresentFrame`'s, which does not clear it, so from the first menu on every frame's step 7 puts the cursor and the mouse driver at the pointer `Shell_PointerX`, `Shell_PointerY` (`0009aff0`, `0009aff4`) with `Cursor_SyncPosition` and `Mouse_SetPosition` ([presenting a frame](../rendering/screen-pages.md#presenting-a-frame)).

### Closing a menu — `Menu_Close` (`0003b138`)

`Menu_Close`(menu):

1. clears `DUIWindow_InputReady`. While the byte `0008444e` is set, it puts the saved background back over the window's rect with `Region_Restore`, first on page 2 and then on the draw page, and adds the window's rect with `DirtyBox_Add`, delay 1 ([dirty boxes](../rendering/screen-pages.md#dirty-boxes));
2. frees the saved background, and clears the active window with `DUIWindow_ClearActive` (`0001c57c`);
3. destroys the window: for menus 1501, 1500, 1700, 1710, 1510 and 1520 (the six numbers at `00081b90`) with `DUIWindow_Dtor` (`0001afa1`), for any other with `MTWindow_Dtor` (`00036a09`), flags 3; clears `+6`; and makes the full-screen render context `0007fcd8` again with `RenderContext_GetFullScreen`.

`MTWindow_Dtor`(window, flags) frees each label's text, destroys the bitmap array `+0x5a3` with `GLBitmapArray_Dtor`, and runs `DUIWindow_Dtor`(window, 0). `DUIWindow_Dtor` destroys each control through its slot `+4` with 3, frees each hotspot's `+0x34`, destroys the render context `+0x17` through its slot `+0x1c` with 3, and the hotspots with `__vec_dtor` of `WindowHotspot_Dtor` (`0001b06f`).

### Changing screens — `Shell_HandleMessage` (`0003c7d8`)

**The frame loop's step 4 hands the pending message to `Shell_HandleMessage`, which gives it to the current menu's handler and then, when the message names a screen, closes the menu and opens that screen.** The message is four words, `Shell_PendingMessage` (`0007fcb0`–`0007fcb7`): a code, the control's index, a value and the target screen ([a message's path](#a-messages-path)). `Shell_HandleMessage`(the dwords `0007fcb0`, `0007fcb4`), on a copy of the four on its stack:

1. sets the pending message to (−1, −1, 0, 0), and answers 0 when the code is −1;
2. calls the current menu's slot `+8` with the copy, which may change it;
3. with target 99, *back*: unless the current screen is 1000, or the previous one is 1500 or 1501 and the object `000809a4` is not set, the target becomes `Shell_PreviousScreen`; and when the word `000817be` is not −1, `Shell_CurrentScreen` takes it and it is set to −1;
4. answers 0 when the target is not in `Shell_ScreenIds`;
5. for target 3000 or 3333 from screen 1500 or 1501 sets the byte `000817bd` to 3 while the byte `000809a8` is set, and to 0 otherwise;
6. closes the current menu with `Menu_Close`, destroys it through its slot `+4` with flags 3, and clears `Shell_CurrentMenu`;
7. with the byte `000809a9` 1 clears it and plays music 13, `intro\iintro.ttm` and `intro\earth_w.ttm`; with 2 plays `intro\credit_a.ttm` and `credit_b.ttm`; each first calls the routine at `0008662c`;
8. takes the target's group with `Shell_BackdropForScreen`; when it is not 0 and differs from `Shell_BackdropId`, or the byte `0007fcd7` is 2, it first presents a frame after calling `0008662c` when the group was 1000, and shows the new backdrop with `ShellBackdrop_Show`, storing the group in `Shell_BackdropId`. With `000809a9` 2 it sets `Shell_MusicTrack` to 1 and `0007fcd5`, and clears `000809a9`;
9. runs the target's case in the table, when it has one;
10. opens the new menu with `Menu_Open`(menu, target) when a case made one; sets `Shell_PreviousScreen` to 1000 for target 1000 and to the current screen otherwise, `Shell_CurrentScreen` to the target; draws the screen with `Shell_DrawScreen` and answers 1.

**Screen 9999** sets `Shell_ExitCode` to 5 when `Shell_DemoRequested` (`000817ce`) is set and to 2 otherwise, makes a `SimLoadMenu`, loads the palette `res32\regn9999.dpl`, clears `0007fcd5`, puts the string at `00080994` into `000aacfe` with `0007fcd7` 2, and copies the rect `0009b000` from the draw page to the visible page with `Display_CopyRectToPage`. The frame loop then sees 9999 and ends ([the ways out](startup.md#the-ways-out)).

The current menu is destroyed at the shutdown by `Shell_CloseScreens` (`0003c081`), [the shutdown's](startup.md#the-shutdown) step 3, through its slot `+4` with 3.

## Drawing a screen

**A screen is drawn once when it opens and redrawn only where something changed.** `Shell_DrawScreen` (`0003c1b7`), while the byte `0008444e` is set:

1. clears `DUIWindow_InputReady` and calls the routine at `0008662c`;
2. with the byte `000817c6` clear: on screens 1700 and 1710, the career record's HERC diagram (`00029651`, `CareerRecord_SelectPilot`; [the HERC diagrams](../formats/herc-diagrams.md)) is drawn, between `0004c053` and `0004bc0d` when the record's `+0x12` and `+0xe` differ. With it set: unless `0007fcd7` is set or the screen is 1000, `0008663c` is called with (the draw page, 0) and (the visible page, 0); with `0007fcd7` 2 the `.ANM` at `000aacfe` is played with `Anm_Play`(1, `0xa`, name) ([playing](../formats/anm-animation.md#playing)) and `0007fcd7` cleared; the backdrop is drawn with `ShellBackdrop_Draw`; and backdrop 4000 calls `0001240b` and the HERC diagram, any other `00012467`;
3. `DigitalDialog_ShowMessage`(`0009b010`) and `Ttm_RunAll`;
4. with a current menu: makes the render contexts `0007fcdc` (`0004c88a`, of the window's rect) and `0007fcd8` (`0004c53c`) and the scripts' default, sets the source and draw pages to page 2, and saves the background under the window into `+2` with `Region_Save`. Unless the message box is up (the byte `0009b013`, `0009b010`'s `+3`), it draws the window on page 2 with `DUIWindow_Draw` (`0001bfa3`), after running the object `000809a4`'s slot `+4` and the text box `00018dcb` twice, once per page, while the byte `000817bd` counts down; then sets the draw page back. Still unless the message box is up: on backdrop 1000, except screen 9999, it adds the whole screen as a dirty box, delay 1, so the next frames copy page 2 onto both pages; otherwise it draws the window again on the draw page and, while `000809a8` is set, calls `00009c64` with `000809a4` and sets `000809a8` to 3. It sets the byte `0007fcd6`, which makes the next frame wait 0.1 s ([presenting a frame](../rendering/screen-pages.md#presenting-a-frame));
5. with `0007fcd7` 1 uploads the palette `0007fcd0`, clears `0007fcd4`, plays the `.ANM` with `Anm_Play`(1, `0xb`, name) and clears `0007fcd7`;
6. puts the source page back, adds the whole screen as a dirty box with delay 0, calls `00086628` unless the screen is 9999, and sets `DUIWindow_InputReady`.

**Each pass after that** the frame loop's step 5 runs `Shell_TickScreen` (`0003c115`): unless the message box is up it updates the current menu's window with `DUIWindow_Update` (`0001c067`, [updating a window](#updating-a-window--duiwindow_update-0001c067)); and while the object `000809a4` is set and the byte `000809a8` is above 1 it calls `00009cfb` and lowers `000809a8` when `0007fcd4` is clear, sets it to 2 when it is 1 and the object's byte `+0x4c0` is clear, and to 3 when it is 1 and `+0x4c2` is set ([Open](#open)).

## The window

**The window is the `.DLG`'s first record: a box of the menu's picture with its title on a framed plate, its labels, and its controls.** `DUIWindow_Ctor` (`0001abcf`, class `DUIWindow`) builds a `0x4ed`-byte window over the rect:

| Offset | Holds |
|---|---|
| `+0` | the rect, `int32` left, top, right, bottom, on the screen |
| `+0x10` | a byte: 0 hit-tests the rect; otherwise the pointer must lie within the distance `+0x15` of the point (`+0x11`, `+0x13`), measured as \|dx\| + \|dy\| |
| `+0x17` | the render context, `000512a0`(0), with the rect copied to its `+0x210`, a zero point to its `+0x220` and 1 to its `+0x208` |
| `+0x1b` | `int16` the control count |
| `+0x1d` | `int16` `0xffff` |
| `+0x1f` | the controls, up to 64 pointers |
| `+0x11f` | the keys, a zero-ended 64-byte list of scan codes and their release codes |
| `+0x19f` | 15 hotspots of `0x38` bytes (`__vec_ctor` of `WindowHotspot_Ctor`, `0001ad4e`) |
| `+0x4e7` | `int16` the hotspot count |
| `+0x4e9` | the vtable: `DUIWindow`'s `0007eff0`, slot `+4` `DUIWindow_AddLabel` (`0001ae9b`), which does nothing, and `+8` `DUIWindow_DrawBackground` (`0001aeb7`), which does nothing |

`MTWindow_Ctor` (`00036874`, class `MTWindow`) takes the window record's id, the title and the rect. It builds the window in `0x5a7` bytes with `DUIWindow_Ctor`, six label entries of `0x12` bytes at `+0x537`, and the vtable `00081140`, whose slot `+4` is `MTWindow_AddLabel` and `+8` `MTWindow_Draw` (`00036b4c`). It loads `res32\menu<id>.dba` with `Resource_LoadBitmapArray` into `+0x5a3`; id 9999 loads `res32\menu<9999 − r>.dba`, `r` from `Math_RandomBelow(5)`. It sets `+0x4ed` to the `title` font (`000aacc6`, [the shell's fonts](../formats/styles-dat.md#the-loads)); the six ids at `00081174`, 999, 1240, 4444, 1610, 1800 and 1920, go elsewhere from here ([Open](#open)). For the rest it sets `+0x4f1` to the `gray` font (`000aacca`), copies the title to `+0x4f5`, or a 0 byte when there is none, and zeroes the label count `+0x535` and the hotspot count `+0x4e7`.

`MTWindow_AddLabel` (`00036a93`), the slot kinds 1, 4 and 5 call, keeps up to six labels and drops the rest: for each, a copy of the text in a new buffer of the length given, at `+0`, the point at `+4` and `+8`, the width at `+0xc` and the mode at `+0xe`.

`DUIWindow_AddHotspot` (`0001b2de`) keeps up to 15 areas: for each, 1 at `+1`, the rect at `+2` and again at `+0x24`, the second argument at `+0x12`, the id at `+0x16`, the handler at `+0x18` and 0 at `+0x34`. The byte `+0` is set when the area is pressed.

`DUIWindow_AddControl` (`0001b207`) appends a control to `+0x1f`, counted at `+0x1b`. When the control's key byte `+0x1d` is non-zero it passes the key, and the key with bit 7 set, to `DUIWindow_AddKey` (`0001b296`), which appends a byte to the key list unless the list holds it already.

### The menu window's drawing — `MTWindow_Draw` (`00036b4c`)

`MTWindow_Draw`(window) draws, in the window's render context, whose origin is the window's corner:

1. the first bitmap of `+0x5a3`, `res32\menu<id>.dba`, at (0, 0), with `Bitmap_Blit`;
2. with a title, its plate: *y0* 4 on backdrops 4000 and 2000 and 5 on the others, *y1* = *y0* + 17; a margin *m* of 20 on backdrop 4000 and 40 on the others; *x0* = (window width − title width − *m*) / 2 and *x1* = *x0* + title width + *m*, the widths by `HudFont_MeasureString` in the `title` font. Four lines through `0004ba84` in colors `0x10`, `0x10`, `0x1b` and `0x1c`, then frames 0 to 3 of `res32\corners.dba` (`000ab4ee`) at the plate's corners, set 4 pixels in, and the title in the `title` font, centered between *x0* and *x1* (the render context's `+0x231` 2, `+0x235` the width; `HudFont_DrawString`), at the plate's middle row less 1;
3. each label, in the `gray` font: `HudFont_DrawString` at (*x*, *y* + the font's cell height − 3), the label's mode as `+0x231` and its width as `+0x235`. So kind 1's mode 0 draws the text from *x*, kind 4's mode 1 ends it at *x* + width, and kind 5's mode 2 centers it in the width ([the kinds](../formats/dlg-menu-dialogs.md#the-kinds)).

### Drawing a window — `DUIWindow_Draw` (`0001bfa3`)

`DUIWindow_Draw`(window) installs the window's render context, clears its `+0x20c`, calls the window's slot `+8`, sets `+0x20c` again, calls each control's slot `+8`, calls each hotspot's handler with (the hotspot's `+2`, its id), and installs the render context it found.

### Updating a window — `DUIWindow_Update` (`0001c067`)

`DUIWindow_Update`(window) installs the window's render context with `+0x20c` set and clears `DUIWindow_InputReady`. For each control whose byte `+0x1f`, *changed*, is 1 or whose `+0x1b` is 2, it draws the control through its slot `+8` on page 2 and again on the draw page, and adds its rect (slot `+0x48`, moved by the window's corner) with `DirtyBox_AddRect`, delay 0. Then `DUIWindow_UpdateHotspots` (`0001c270`) does the same for each hotspot whose byte `+0` is set, calling its handler on page 2 and on the draw page, and clears the byte. Last it sets `DUIWindow_InputReady` and installs the render context it found.

## The controls

**A control is a rect, an id, a state and a vtable; the id is the screen a press of it names.** `DUIControl_CtorWithKey` (`0001a7e5`)(control, *focusable*, key, rect) and `DUIControl_Ctor` (`0001a716`)(control, *focusable*, id, rect) build the `DUIControl` base, `0x24` bytes, storing the vtable `0007ef92` and the rect with `DUIControl_SetRect` (`0001a8b3`), which also clears `+0x19` and `+0x1f`:

| Offset | Holds |
|---|---|
| `+0` | the rect, `int32`, relative to the window |
| `+0x10` | a byte: 0 hit-tests the rect; otherwise the distance test of the window's `+0x10`, with the point at `+0x11`, `+0x13` and the distance `+0x15` |
| `+0x17` | `int16` the id |
| `+0x19` | `int16` the state: 0 up, 1 pushed, 2 inactive |
| `+0x1b` | `int16` how a press is tracked: 0 a button's, 1 the control follows the pointer while pressed, 2 the control holds the input ([the mouse](#the-mouse)) |
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
| `+0x40` | a key code to the holding control, (control, code & `0xff7f`) | | |
| `+0x48` | `DUIWindow_Update`, (control): the rect | `BarGraph_GetRectWindowCopy` (`0001a70a`) | `BarGraph_GetRectButtonCopy` (`0001d0e6`) |

`MTButton`'s slots `+0x10` and `+0x18`, `MTButton_Highlight` (`0001d1ce`) and `MTButton_Unhighlight` (`0001d1e7`), set and clear the byte `+0x94` and set *changed* ([Open](#open)).

### Buttons

Kind 3's style 1 builds an icon button, `MTBmpButton_Ctor` (`0001d47d`, class `MTBmpButton`), with no text when the key is non-zero; any other style a text button, `MTButton_Ctor` (`0001cde8`, class `MTButton`). The main menu's six are text buttons with no key. Menu 1300's button 100 takes its text from code chosen by the word `0007fce8` among the four values at `000819e0` ([Open](#open)); when it is none of them, the text pointer keeps the value an earlier record left in it.

`MTButton_Ctor` builds a `0x95`-byte button with `DUIControl_CtorWithKey`(button, 1, key, rect), sets `+0x1b` to 0, and stores the id at `+0x17`, its vtable `0007f18c` at `+0x20`, the `STYLES.DAT` records 0, 1 and 2 (the pointer `00082654`) at `+0x30`, `+0x34` and `+0x38`, and the `active`, `pushed` and `inactive` fonts (`000aacce`, `000aacd2`, `000aacd6`) at `+0x24`, `+0x28` and `+0x2c`, one per state. It copies the text to `+0x3c` and places it three times at `+0x7c`, once per font: x the rect's left, y the bottom less half of (height − the font's cell height) less 2, the pushed one a pixel lower than the up one. With no text, `+0x3c` and the points are 0. `MTButton_SetTextFromTable` (`0001d393`)(button, line) copies line *line* of `eng\watext.bin` (`0009afa8`) to `+0x3c`, places it again and sets *changed*; `Menu_Open` calls it on the buttons of several menus.

`MTBmpButton_Ctor` builds a `0xb1`-byte button over a text button, stores its vtable `0007f24a` at `+0x20` and the rect's left and top at `+0x95`, and takes its pictures by key from `res32\corners.dba`, the array `Shell_Main` loads into `000ab4ee`: frame *a* at `+0xa5` and `+0xad`, frame *b* at `+0xa9`. Eight ids take other frames, and four of them also call `0001a4f4`(button, 1).

| Key | Frames *a*, *b* | Other ids |
|---|---|---|
| `0x48` Up, `0x49` PgUp | 20, 22 | 1503: 36, 37 and `0001a4f4` |
| `0x4a` keypad − | 29, 31 | 109: 34, 35 |
| `0x4b` Left | 25, 27 | 1505: 40, 41 and `0001a4f4`; 2003: 44, 46 |
| `0x4d` Right | 24, 26 | 1506: 42, 43 and `0001a4f4`; 2004: 45, 47 |
| `0x4e` keypad + | 28, 30 | 108: 32, 33 |
| `0x50` Down, `0x51` PgDn | 21, 23 | 1504: 38, 39 and `0001a4f4` |

Any other key sets no frames, and four ids at `0007f2c4` jump to code Ghidra left undefined.

### Drawing a control

`MTButton_Draw` (`0001d200`)(button), by the state `+0x19`:

- **1, pushed**: while the byte `0007fcb9` and the sound object `0007fd00` are set, it stops the object when its slot `+0x30` answers non-zero (slot `+0x14`) and plays it (slot `+0x10`): the click. Then as state 0;
- **0, up**, and pushed: `00044b86` with the state's `STYLES.DAT` record over the button's rect, and, while the byte `+0x94` is set, `0004b867`(10, rect);
- **2, inactive**: `00044b86` with record 2;

then, with text, the text in the state's font at the state's point, centered in the button's width (the render context's `+0x231` 2, `+0x235` the width), and *changed* cleared.

### The list box

Kind 15 builds `MTStrListBox_Ctor` (`0001e11a`, class `MTStrListBox`, vtable `0007f474`) over `DUIVSlider` (`0007f412`) and `DUISliderControl` (`0007f2f8`), a `0x5a`-byte control on `DUIControl_Ctor`(list, 0, id, rect), so `Tab` passes over it: `+0x1b` 1, the range `+0x24` 0 to `+0x26` the line count `0001eac5`(list, lines), the selected line at `+0x28`, `+0x2a` and `+0x54` from the record's word, the style at `+0x3e`, the lines at `+0x4c`, `STYLES.DAT` record 4 at `+0x50`, the fonts `nm_green` (`000aacda`) and `nm_red1` (`000aacde`) at `+0x40` and `+0x44`, the line height `+0x2c` (the font's cell height − 1, at most the rect's height − 1), the lines shown `+0x58` (the height over that, 1 to the count) and the first line shown `+0x56`, 0 or so that the selected line is the last shown. The main menu's 1002 is one, at (112, 47)–(196, 59). Its slots are a later module's ([Open](#open)).

## The input

### The active window

**One window at a time takes the input: `DUIWindow_Active` (`0007f0d0`).** `DUIWindow_SetActive` (`0001c536`)(window) stores it, runs `DUIWindow_Activate` (`0001b0ff`) and sets the byte `DUIWindow_KeysEnabled` (`0007f0dd`). `DUIWindow_Activate`:

1. when the window has an enabled control with id 103, makes it pushed and changed, the pressed control `DUIWindow_PressedIndex` (`0007f0d4`), and the holding control `DUIWindow_HeldIndex` (`0007f0d6`) with the byte `DUIWindow_Holding` (`0007f0db`) set, and calls its slot `+0xc`;
2. unless `-X` was given, registers `DUIWindow_KeySubscriber` (`0001ba73`) with the keyboard for the window's key list `+0x11f` (`Keyboard_AddSubscriber`, after `00027bbf`);
3. sets `DUIWindow_FocusIndex` to the control under the pointer, `DUIWindow_HitControl` (`0001bc11`)(window, the pointer relative to the window), −1 for none.

`DUIWindow_ClearActive` (`0001c57c`) runs `DUIWindow_Deactivate` (`0001b25b`), which sets `DUIWindow_HeldIndex` to −1, clears `DUIWindow_Holding` and removes `DUIWindow_KeySubscriber`, and then clears `DUIWindow_Active` and `DUIWindow_KeysEnabled`.

`DUIWindow_HitControl`(window, x, y) answers the first control, in the order they were added, that is not inactive and whose rect holds the point (or, with its `+0x10` set, within its distance), and −1 for none.

### The mouse

`ShellMouse_AddCallbacks` (`0001c460`) registers three callbacks with the mouse driver ([the mouse driver's events](../simulation/cockpit-mouse.md)): mask 1, the pointer moved; 2, the left button pressed; 4, the left button released. Each is given the pointer's point.

- **`ShellMouse_OnMove` (`0001c605`)**, while `DUIWindow_InputReady` is set and there is an active window: clears `DUIWindow_InputReady` for its run and stores the point in `Shell_PointerX` and `Shell_PointerY`. While the byte `DUIWindow_Tracking` (`0007f0da`) is set it calls the pressed control's slot `+0x38` with the point relative to the window and its slot `+0xc`. Otherwise, with the point inside the window (its rect, or its distance test), it runs `DUIWindow_OnDrag` (`0001b8fc`)(window, point): `DUIWindow_FocusIndex` becomes the control under the pointer; while a control is pressed, it is made pushed when the pointer is over it again, or it holds the input, and up when the pointer has left it, each change calling slot `+0xc`; and a hotspot under the pointer has its byte `+0` set.
- **`ShellMouse_OnLeftPress` (`0001c7d8`)**, unless the message box is up (`0009b013`): with the point inside the active window, `DUIWindow_OnPress` (`0001b436`)(window, point): it lets go of the holding control (state 0, changed, `DUIWindow_Holding` cleared, `DUIWindow_HeldIndex` −1); takes the control under the pointer as `DUIWindow_PressedIndex`; makes it pushed and changed; for a `+0x1b` of 1 calls its slot `+0x38` with the point and sets `DUIWindow_Tracking`, for 2 calls slot `+0x38` and makes it the holding control; calls its slot `+0xc`; and sets the byte `+0` of a hotspot under the pointer. With no active window, or the point outside it, `DUIWindow_PressedIndex` becomes −1, and while the dword `0007fce4` is set the point is tested against the selected hangar slot's rects, posting the message (0, −1, the rect's index or −1, 1702) ([Open](#open)).
- **`ShellMouse_OnLeftRelease` (`0001caaa`)**, unless the message box is up: while `DUIWindow_Tracking` is set, it puts the pressed control up and changed, clears `DUIWindow_Tracking` and posts the message (0, the index, its slot `+0x2c`, its id) wherever the pointer is. Otherwise, with the point inside the window, `DUIWindow_OnRelease` (`0001b683`)(window, point): when the control under the pointer is the pressed one it posts (0, the index, slot `+0x2c`, the id); a control under the pointer that does not hold the input is put up and changed (slot `+0xc`); `DUIWindow_PressedIndex` becomes −1; and a hotspot under the pointer has its byte `+0` set. With the point outside, `DUIWindow_PressedIndex` becomes −1 and nothing is posted.

So a click of a button posts its id as the target, and the press shows the pushed state from the press to the release.

### The keys

**`DUIWindow_HandleNavKey` (`0001cc86`)**, the dialog's key handler, which `Shell_NavKeySubscriber` and `Shell_HandleInput` call ([the input](startup.md#the-input)), does nothing without an active window, and otherwise acts on seven codes, the `REPNE SCASD` table at `0007f0e0`:

| Code | Does |
|---|---|
| `Tab` (`0x0f`) | puts the pressed control, if any, up and changed and clears `DUIWindow_PressedIndex`; moves `DUIWindow_FocusIndex` to the next control, wrapping, passing over controls whose *focusable* byte is 0; and puts the pointer on its center with `DUIWindow_PointAtControl` |
| `Shift+Tab` (`0x80f`), `Alt+Tab` (`0x20f`) | moves the focus to the previous focusable control, wrapping, and puts the pointer on it |
| `Enter` (`0x1c`), `Shift+Enter` (`0x81c`) | presses the focused control, `DUIWindow_PressControl` (`0001b5bb`) |
| `Enter`'s release (`0x9c`), with `Shift` (`0x89c`) | releases it, `DUIWindow_ReleaseControl` (`0001b7d5`) |

The other codes `Shell_NavKeySubscriber` passes, the arrows, `Space`, `Backspace`, `Home`, `End`, `Esc` and `Del`, match none of them. The focus wraps by the remainder of the control count, and the walk does not stop when no control is focusable.

`DUIWindow_PointAtControl`(window, index) sets `Shell_PointerX` and `Shell_PointerY` to the control's center on the screen, which the next frame gives the cursor and the mouse driver ([opening a menu](#opening-a-menu--menu_open-0003969e)).

`DUIWindow_PressControl`(window, index), for an index of 0 or more whose control is not inactive, lets go of the holding control, makes the control `DUIWindow_PressedIndex`, pushed and changed, and calls its slot `+0xc`. `DUIWindow_ReleaseControl`(window, index), for the same, posts the message (0, index, slot `+0x2c`, the id) when the control is the pressed one, puts it up and changed unless it holds the input, and sets `DUIWindow_PressedIndex` to −1.

**`DUIWindow_KeySubscriber` (`0001ba73`)**(code), registered for the window's key list ([the keyboard subscribers](startup.md#the-keyboard-subscribers)), answers 1 at once while `DUIWindow_KeysEnabled` is clear. Otherwise it answers 2 for a repeat of the last code `DUIWindow_LastKey` (`0007f0c4`); takes a code whose low seven bits match a last code that was a press as that key's release; stores the code as the last; answers 2 for a code with `Alt`, `Ctrl` or the shift bit (`0xe00`); and looks the scan code up among the enabled controls' keys (`DUIWindow_FindControlByKey`, `0001bd41`). Found, the control takes the focus and the pointer, is pressed (`DUIWindow_PressControl`) on a press code and released (`DUIWindow_ReleaseControl`) on a release code, and the answer is 1; otherwise 2.

**Typed characters and other keys** go only to the holding control, the one whose `+0x1b` is 2: `DUIWindow_SendChar` (`0001bb85`)(window, word), for a word with bit `0x4000` and without `0x8000`, calls its slot `+0x3c` with the word's low byte; `DUIWindow_SendKey` (`0001bbd2`)(window, word), from `Shell_ForwardKeyToDialog`, its slot `+0x40` with the word & `0xff7f` ([the input](startup.md#the-input)).

## A message's path

**A control's release writes the four words of `Shell_PendingMessage`; the frame loop's next step 4 passes them to `Shell_HandleMessage`.** `es2_xref.py --decode` on `0007fcb0` and `0007fcb4` finds them written by `DUIWindow_OnRelease`, `DUIWindow_ReleaseControl`, `ShellMouse_OnLeftRelease` and `ShellMouse_OnLeftPress`'s hangar test, beside `Shell_HandleMessage`'s reset:

| Word | Holds |
|---|---|
| `0007fcb0` | the code: 0 from a control, −1 for no message |
| `0007fcb2` | the control's index in the window |
| `0007fcb4` | the value, the control's slot `+0x2c`: 1 for a button |
| `0007fcb6` | the target: the control's id |

So on the main menu a click of a button runs the main menu's handler, slot `+8` of `MainMenu`'s vtable, with the message, and then opens the screen of the button's number when it is one of `Shell_ScreenIds`. The main menu's 1003 is not among them, so its click changes no screen ([the ways out](startup.md#the-ways-out)). The handler's cases are the main menu's buttons' module ([Open](#open)).

## Functions

| ES1 | Address |
|---|---|
| `Shell_StockItemsFromFlags` | `00038e48` |
| `Shell_ProcessMissionResults` | `00038eb5` |
| `ShellBackdrop_Ctor`, `ShellBackdrop_Dtor`, `ShellBackdrop_LoadPalette`, `ShellBackdrop_Show` | `0003918e`, `00039422`, `0003950a`, `000395a0` |
| `ShellBackdrop_SetPicture`, `ShellBackdrop_Draw`, `Shell_BackdropForScreen` | `00021334`, `0002141c`, `00040afa` |
| `ShellMusic_Play` | `00020a70` |
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
| `DUIWindow_Activate`, `DUIWindow_AddControl`, `DUIWindow_Deactivate`, `DUIWindow_AddKey`, `DUIWindow_AddHotspot`, `DUIWindow_Offset` | `0001b0ff`, `0001b207`, `0001b25b`, `0001b296`, `0001b2de`, `0001b395` |
| `DUIWindow_OnPress`, `DUIWindow_PressControl`, `DUIWindow_OnRelease`, `DUIWindow_ReleaseControl`, `DUIWindow_OnDrag` | `0001b436`, `0001b5bb`, `0001b683`, `0001b7d5`, `0001b8fc` |
| `DUIWindow_KeySubscriber`, `DUIWindow_SendChar`, `DUIWindow_SendKey`, `DUIWindow_HitControl` | `0001ba73`, `0001bb85`, `0001bbd2`, `0001bc11` |
| `DUIWindow_FindControlByKey`, `DUIWindow_FindEnabledControlById`, `DUIWindow_FindControlById`, `DUIWindow_FindHotspotById`, `DUIWindow_HitHotspot` | `0001bd41`, `0001bda4`, `0001be16`, `0001be6f`, `0001beca` |
| `DUIWindow_Draw`, `DUIWindow_Update`, `DUIWindow_UpdateHotspots`, `DUIWindow_PointAtControl` | `0001bfa3`, `0001c067`, `0001c270`, `0001c3ec` |
| `ShellMouse_OnMove`, `ShellMouse_OnLeftPress`, `ShellMouse_OnLeftRelease` | `0001c605`, `0001c7d8`, `0001caaa` |
| `DUIWindow_SetActive`, `DUIWindow_DrawActive`, `DUIWindow_OffsetActive`, `DUIWindow_ClearActive`, `DUIWindow_EnableControl`, `DUIWindow_DisableControl`, `DUIWindow_HandleNavKey` | `0001c536`, `0001c556`, `0001c566`, `0001c57c`, `0001c59d`, `0001c5d0`, `0001cc86` |
| `MTButton_Ctor`, `DUIButtonControl_Dtor`, `MTButton_Dtor`, `MTButton_Invalidate`, `DUIControl_DtorButtonCopy` | `0001cde8`, `0001cfd8`, `0001d10d`, `0001d17b`, `0001d18d` |
| `MTButton_Highlight`, `MTButton_Unhighlight`, `MTButton_Draw`, `MTButton_SetTextFromTable`, `DUIButtonControl_GetValue` | `0001d1ce`, `0001d1e7`, `0001d200`, `0001d393`, `0001d088` |
| `BarGraph_NoOpButtonCopy`, `GridShape_GetMemorySizeButtonCopy`, `BarGraph_GetRectButtonCopy`, `Rect_CopyCtorButtonCopy` | `0001d041`, `0001d096`, `0001d0e6`, `0001d365` |
| `MTBmpButton_Ctor`, `MTStrListBox_Ctor` | `0001d47d`, `0001e11a` |
| `MTWindow_Ctor`, `MTWindow_Dtor`, `MTWindow_AddLabel`, `MTWindow_Draw` | `00036874`, `00036a09`, `00036a93`, `00036b4c` |

## Globals

| | Address |
|---|---|
| `Shell_CurrentScreen`, `Shell_PreviousScreen`, `Shell_BackdropId`, `int16` | `000817c0`, `000817c2`, `000817c4` |
| `Shell_CurrentMenu`, the open screen object | `000817c8` |
| `Shell_ScreenIds`, zero-ended `int16` list | `000817e0` |
| `Shell_PendingMessage`, four `int16` | `0007fcb0` |
| `Shell_Backdrop` | `000a09a0` |
| `Shell_MusicTrack`, `int16`; the track last set | `00081938`; `00081936` |
| `ShellBackdrop_Picture` | `000aa7ac` |
| `Shell_SyncPointer`, byte | `0007fd07` |
| `Shell_PointerX`, `Shell_PointerY` | `0009aff0`, `0009aff4` |
| `DUIWindow_Active` | `0007f0d0` |
| `DUIWindow_PressedIndex`, `DUIWindow_HeldIndex`, `DUIWindow_FocusIndex`, `int16` | `0007f0d4`, `0007f0d6`, `0007f0d8` |
| `DUIWindow_Tracking`, `DUIWindow_Holding`, `DUIWindow_InputReady`, `DUIWindow_KeysEnabled`, bytes | `0007f0da`, `0007f0db`, `0007f0dc`, `0007f0dd` |
| `DUIWindow_LastKey`, `int16` | `0007f0c4` |

## Open

- **Open:** whether `00040b94`, a stub returning its first argument, is `shell.cpp`'s last function or the word stacks' file's first: it starts on a 4-byte boundary right after `Shell_StaticDtor`, and `WordStack_Push` follows it with no fill.
- **Open:** where the button file ends, and whether the slider and list classes from `0001deaa` share it.
- **Open:** what the byte `000817c6` is, which picks between `Shell_DrawScreen`'s two backdrop paths, and who writes `000817be`, the screen `Shell_HandleMessage`'s *back* puts in place.
- **Open:** what `0002141c`'s tail adds after its copy, `00011f19`, `000122e0`, `0001240b` and `00012467` do for a backdrop.
- **Open:** what `00044b86` draws of a `STYLES.DAT` record and what `0004b867`(10, rect) draws for a highlighted button, and what calls `MTButton_Highlight` and `MTButton_Unhighlight`: no caller of `MTButton`'s slots `+0x10` and `+0x18` was found in the window, dialog or shell code read here.
- **Open:** the lines `0004ba84` draws for the title plate: which of the four colors takes which edge.
- **Open:** the routines at `0008662c` and `00086628` that `Shell_DrawScreen` and `Shell_HandleMessage` call, and `0008663c`, which `Shell_PlayTtm` and `Shell_DrawScreen` call with a page and 0.
- **Open:** the object `000809a4` and the bytes `000809a8`, `000809a9` and `000817bd` that `Shell_DrawScreen`, `Shell_TickScreen` and `Shell_HandleMessage` test, and `00009cfb` and `00009c64`.
- **Open:** the other screens' cases and handlers in [the screens](#the-screens) table, among them screen 999 and the main menu's handler `0002a70a`, the message box `00037eac`, screen 1320's code at `0003e9ae`, the hangar test of `ShellMouse_OnLeftPress` with the dword `0007fce4`, and `MTBmpButton`'s slots `0001da8d`, `0001db87` and `0001dc9f`.
- **Open:** the list box's slots and what the main menu's list 1002 lists (`0003750d`(1002), [the kinds](../formats/dlg-menu-dialogs.md#the-kinds)), and what a press and a move do to it through slot `+0x38`.
- **Open:** a retail check under DOSBox-X: on the main menu, `Tab` moves the pointer onto the next button and `Enter` presses it; pressing a button and dragging off it before the release opens nothing.
