# GO.EXE — the war map

**Before every mission the shell shows the war map: the map of the mission's area across the top of the screen and, below it, the mission's briefing in a box, with two small arrow buttons at its right, `PgUp` and `PgDn`, that turn its pages. Along the bottom are CANCEL, ARMING and BEGIN MISSION >, and at the lower left the map's four arrows and its two zoom buttons. BEGIN MISSION > leaves the shell for the mission; CANCEL goes back to the SINGLE MISSION menu, or in a career to the career menu; ARMING opens the hangar's arming screen. A career's war map has SERVICE BAY where a single mission's has ARMING.** The menu is built, opened, drawn and left as every menu is ([the menus](menus.md)); this page holds what the war map adds. The map itself, its area and its six buttons, is [Open](#open).

Addresses are `ES1GO` image addresses ([the X-32 executable](../formats/x32-executable.md#the-image)). The functions here remove their own arguments, so argument lists are in stack order ([calling conventions](../formats/compiler.md#calling-conventions)). ES2's briefing is a view of its mission screen, built in code ([the mission screen](../../retail/shell/mission-screen.md)); nothing here is ported from it.

## Where the object file starts and ends

The war map's code, `0002bbaf` to `0002c421`, lies inside the menu handlers' file ([where it starts and ends](main-menu.md#where-the-object-files-start-and-end)): `BattleSetDialog_OnMessage`'s `RET 8` at `0002bbac` runs straight into `WarMapMenu_MapAreaNoOp` (`0002bbaf`), and `DebriefingMenu_Ctor` (`0002c422`) follows the handler's `RET 8` at `0002c41f`. The class's name `WarMapMenu` and its vtable `00080bc6` follow the file's copies of the region classes' and `Menu`'s vtables, and the war map's own functions sit among the file's copies of their functions:

| Address | Function | The vtable slot that holds it |
|---|---|---|
| `0002bbaf` | `WarMapMenu_MapAreaNoOp`: the map area's hotspot handler, the code of `BarGraph_NoOp` (`00003de0`) | none: `Menu_BuildFromDlg`'s kind-14 case ([the build](menus.md#opening-a-menu--menu_open-0003969e)) |
| `0002bbbd` | `WarMapMenu_Ctor` | none: screen 1500's and 1501's case of `Shell_HandleMessage` (`0003c7d8`) calls it |
| `0002bd95`–`0002bf08` | `GLBase_DtorMenuHandlersCopy`, `GLRegion_DtorMenuHandlersCopy`, `GLRectangleRegion_DtorMenuHandlersCopy`, `Menu_DtorMenuHandlersCopy`, `Menu_OnMessageMenuHandlersCopy` | the file's copies ([the menu handlers' file](main-menu.md#where-the-object-files-start-and-end)) |
| `0002bf15` | `WarMapMenu_Dtor` | `+4` of `00080bc6` |
| `0002c057` | `WarMapMenu_OnMessage` | `+8` of `00080bc6` |

## The screen

**`dlg\menu1501.dlg`, a single mission's war map, and `dlg\menu1500.dlg`, a career's, lay out a plain window over the whole screen, three text buttons along the bottom, the map area, and eight icon buttons; their `eng\menu<n>.bin` hold the buttons' texts.** The window is (1, 1)–(319, 199), id 1501 in both files, a plain window (`DUIWindow_Ctor`, [the kinds](../formats/dlg-menu-dialogs.md#the-kinds)) whose slot `+8` draws nothing, so the picture behind the controls is the backdrop's, `res32\regn6000.dbm` ([the backdrop](menus.md#the-backdrop)). `Menu_Close` destroys it with `DUIWindow_Dtor` ([closing a menu](menus.md#closing-a-menu--menu_close-0003b138)). The rects are in the window (`HercWorks.Query menu 1500 1501`):

| Id | Kind | Rect | Key | Text |
|---|---|---|---|---|
| 1800 | text button, 1501 only | (59, 185)–(101, 197) | | ARMING |
| 1700 | text button, 1500 only | (59, 185)–(123, 197) | | SERVICE BAY |
| 99 | text button | (12, 185)–(57, 197) | `0x01` Esc | CANCEL |
| 100 | text button | (103, 185)–(174, 197) in 1501, (125, 185)–(198, 197) in 1500 | | BEGIN MISSION > |
| 1502 | hotspot, kind 14 | (14, 11)–(282, 142) | | the map area |
| 1503 | icon button | (25, 151)–(41, 167) | `0x48` Up | |
| 1504 | icon button | (25, 167)–(41, 183) | `0x50` Down | |
| 1505 | icon button | (17, 159)–(33, 175) | `0x4b` Left | |
| 1506 | icon button | (33, 159)–(49, 175) | `0x4d` Right | |
| 108 | icon button | (53, 150)–(66, 164) | `0x4e` keypad + | |
| 109 | icon button | (53, 167)–(66, 181) | `0x4a` keypad − | |
| 101 | icon button | (271, 150)–(282, 160) | `0x49` PgUp | |
| 102 | icon button | (271, 172)–(282, 182) | `0x51` PgDn | |

The icon buttons take their frames from `res32\corners.dba` by key, and 1503–1506, 108 and 109 by id, the four arrows with the diamond hit area ([buttons](menus.md#buttons)). No control has the key `Space`, so `Menu_Open` puts the pointer on control 0, ARMING or SERVICE BAY ([opening a menu](menus.md#opening-a-menu--menu_open-0003969e), step 4). The kind-14 record makes the hotspot 1502 with `WarMapMenu_MapAreaNoOp` (`0002bbaf`) as its handler and the rect `000a02d4` as its *a* ([the build](menus.md#opening-a-menu--menu_open-0003969e)); `DUIWindow_Draw` and `DUIWindow_UpdateHotspots` call the handler with (the hotspot's rect, 1502), and it returns at once ([drawing a window](menus.md#drawing-a-window--duiwindow_draw-0001bfa3)).

### Opening it

The message box's kind 0 posts 1501 and kind 1 posts 1500 ([the kinds](message-box.md#the-kinds)); RESUME goes to 1500 when the career stood there ([RESUME](main-menu.md#resume)). `Shell_HandleMessage` shows backdrop 6000, the group of both screens, and runs their case at `0003ef9b`, which makes `WarMapMenu_Ctor` (`0002bbbd`)(0) the current menu; its step 10 opens it with `Menu_Open`(menu, the screen), which changes no control for 1500 or 1501 ([changing screens](menus.md#changing-screens--shell_handlemessage-0003c7d8)).

`WarMapMenu_Ctor`(this):

1. allocates `0x26` bytes when *this* is 0, and builds the screen object as the other screens' cases do ([the screen object](menus.md#the-screen-object)): at `+0xa` the vtables of `ClassItem`, `GLBase`, `GLRegion` and `GLRectangleRegion` in turn (`00080ae8`, `00080b12`, `00080b44`, `00080b7c`) with `+0xe` 0, at `+0x22` `Menu`'s `00080ba8` and then `WarMapMenu`'s `00080bc6`;
2. loads the briefing, block 0 of `BRF:` of the mission's `.eng` path `000aad2a`, into `Shell_Dialog` (`0009afc8`) with `Dialog_LoadConditional` (`00018cdb`) ([reading a block](../formats/eng-mission-text.md#reading-a-block)), and turns its page with `Dialog_NextPage` (`00018e18`) as many times as the `int16` `WarMap_BriefingPage` (`000809b2`), compared signed ([the briefing](#the-briefing));
3. sets the byte `000809a8` to 3, which `Shell_DrawScreen`, `Shell_TickScreen` and the map's keys test with the mission map ([drawing a screen](menus.md#drawing-a-screen), [Open](#open));
4. unless the byte `Shell_DebugMode` (`0007fd08`) is set (`-X`), takes the keyboard device with `Input_GetKeyboardDevice_Thunk` (`00027bbf`) and, with one, keeps its mode in `WarMap_SavedKeyboardMode` (`000a04d8`) (`KeyboardInputDevice_GetMode`, `00025d19`), sets mode `0x20000` with `KeyboardInputDevice_SetMode` (`00025ce6`), and gives `KeyQueue_Subscriber` (`0005d6a0`) the five scan codes `WarMap_MapKeys` (`000809ac`) with `Keyboard_AddSubscriberCodes` (`0001ffdc`): `0x39` `Space`, `0x33` `,`, `0x34` `.`, `0x0d` `=` and `0x0c` `-`, the keys that work the mission map ([the input](startup.md#the-input), [the devices](startup.md#the-devices)). `KeyboardInputDevice_SetMode` calls `Keyboard_SetMode` (`0001fd10`), which unhooks the current mode, seeds `Keyboard_Modifiers` (`0007f642`) again from `INT 16h` function 2's shift flags and hooks the new one, so opening the war map reseeds the modifiers though the mode is `0x20000` before and after;
5. sets the byte `000aa778` to 1 ([Open](#open)), and returns the object.

### The briefing

**The briefing stands in a box across the lower middle of the screen, one page at a time; `PgUp` and `PgDn`, or the arrows 101 and 102 beside it, turn the pages, and the war map remembers the page across the alerts and the quit question it can open.** The box is the `.ENG` block's `ConditionalDialogBox`, in `DialogBox_Backdrop6000Rect`, (69, 150)–(267, 184), with `+0` 0, since the backdrop is 6000 when the constructor runs ([where the box stands](message-box.md#where-the-box-stands)). How its text is laid out in pages and drawn is [the text box's](message-box.md#laying-out-the-text); the frame loop's step 5 draws it each pass with `Dialog_Draw` ([the frame loop](startup.md#the-frame-loop)).

- **101**, `PgUp`: `Dialog_PrevPage` (`00018df0`)(`Shell_Dialog`) and `WarMap_BriefingPage` − 1.
- **102**, `PgDn`: `Dialog_NextPage`(`Shell_Dialog`) and `WarMap_BriefingPage` + 1.

Each draws the box after the step ([stepping pages](message-box.md#stepping-pages)). The word moves on every press, but the box's page stops at the first and the last ([stepping pages](message-box.md#stepping-pages)), so the word counts presses, below 0 or past the last page when the player presses on at either end. The constructor's turns stop at the last page and make none for a word of 0 or less. Neither key leaves the screen: 101 and 102 are not in `Shell_ScreenIds`.

`WarMap_BriefingPage` goes back to 0 when `WarMapMenu_Dtor` deletes the box, on every way out but three, below ([leaving it](#leaving-it)).

### The buttons

**A release posts its control's id ([a message's path](menu-input.md#a-messages-path)); `WarMapMenu_OnMessage` (`0002c057`), slot `+8` of `WarMapMenu`'s vtable, acts on twelve ids, and `Shell_HandleMessage` then opens the screen the message names ([changing screens](menus.md#changing-screens--shell_handlemessage-0003c7d8)).** The handler looks the message's target `+6` up among the twelve ids of the `REPNE SCASD` table at `00080bdc`, its cases at `00080c0c`, and does nothing for any other id, 1502 among them.

| Control | Id | The handler | Then |
|---|---|---|---|
| BEGIN MISSION > | 100 | [below](#begin-mission) | 3333, 3000 or 9999 |
| CANCEL | 99 | [below](#cancel) | 1100 or 1200 |
| ARMING | 1800 | the target becomes 1820 while the byte `Shell_LowMemory` (`0007fd09`) is set | [the arming screen](#arming-and-service-bay) |
| SERVICE BAY | 1700 | `0000a9c8`(`Shell_MissionMap` (`000809a4`)), with no test that the map is set; in `Shell_Mode` (`000aad56`) 1, the current career record's byte `+1` set to 1 | the service bay, 1700 |
| page arrows | 101, 102 | [the briefing](#the-briefing) | no screen |
| zoom and map arrows | 108, 109, 1503–1506 | [the map's buttons](#the-maps-buttons) | no screen |

#### BEGIN MISSION >

1. When `CareerRecord_PlayerHasNoHerc` (`000161dc`)(`CareerRecord_Current` (`00014ef4`)) answers non-zero, the target becomes 3333, `UnAssignedAlert`.
2. Otherwise, when `CareerRecord_AnyPilotUnarmed` (`0001620d`)(`CareerRecord_Current`) answers non-zero, the target becomes 3000, `UnArmedAlert`.
3. Otherwise it writes `data\player.mec` with `PlayerMec_Write` (`0002299e`) ([the player file](../formats/career-log-and-player-mec.md#the-player-file)), the target becomes 9999, it deletes the mission map with `Shell_DeleteMissionMap` (`0002a4ec`) and clears `000809a8`. `PlayerMec_WritePilotWords` (`0001548f`) draws the player's first word, `Math_RandomBelow`(`Shell_RandomState`, 11), on every mode-0 write, so this write draws once more than the mission load's and its word can differ from the load's file; and `PlayerMec_Write` ends with `CareerRecord_SaveCurrent` (`00014f28`), in mode 0 `SingleMission_SaveRecord` (`00014f54`), so `car\sm_play.gam` is written again too ([a single mission's file](../formats/career-log-and-player-mec.md#a-single-missions-file)).

`CareerRecord_PlayerHasNoHerc` answers 1 in `AL` (`EAX` `0xffffff01`, the slot's −1 above it) when the slot `+8` of the record's player entry `+0xe` is −1 ([pilot entries](../formats/bst-battle-sets.md#pilot-entries)). `CareerRecord_AnyPilotUnarmed`(record) tests the current pilot `+0x12`'s slot with `Hangar_SlotIsUnarmed` (`0002a354`) and steps the current pilot with `CareerRecord_NextPilot` (`00015b0a`), as many times as `CareerRecord_CountPilots` (`00015bfb`) counts, called again on each pass, and answers the byte it sets to 1 when any test did; the steps move the current pilot and the hangar's selected slot ([pilot entries](../formats/bst-battle-sets.md#pilot-entries)). `Hangar_SlotIsUnarmed`(slot) answers 0 for slot −1 or an empty slot of the hangar's array `000808a0`, and otherwise `Herc_IsUnarmed` (`00022ebf`) of its HERC: 1 when every mount holds weapon 17, the empty mount, so also for a HERC with no mounts ([the HERC record](../formats/car-hercs-and-stock.md#the-herc-record)).

**In a single mission** (`Shell_Mode` 0) the record is `SingleMission_Record` (`0009b014`), which the mission load fills ([the squad](../formats/msn-mission-file.md#the-squad)). The player's entry takes as its slot the type of the first group's first mech record, so the first test answers 1 only for a type of −1. That slot holds `car\sm_init.dat`'s HERC of that chassis for a type of 0 to 7, and each of the eight carries a weapon ([the retail HERC lists](../formats/car-hercs-and-stock.md#the-retail-herc-lists)); each squadmate's HERC carries its own mech record's weapons. So a single mission goes on to `data\player.mec` and 9999 unless a squadmate's mech record leaves every mount empty ([Open](#open)). In a career the record is the roster's current one.

The two alerts are menus of their own over the war map's backdrop, windows titled WARNING! (`HercWorks.Query menu 3000 3333`), made by `Shell_HandleMessage`'s cases at `0003cd38` (3000) and `0003ced7` (3333) as `0x26`-byte menus over `Menu`'s vtable. Menu 3000 says "One or more Hercs in your squad is unarmed." over CANCEL (99, `Esc`) and PROCEED (100, `Space`): `UnArmedAlert_OnMessage` (`0002a650`) turns PROCEED into 9999 in `Shell_Mode` 0, and into 1700 with message box kind 6 in mode 1, and deletes the mission map and clears `000809a8`. It does not call `PlayerMec_Write`, so the mission starts with the `data\player.mec` the mission load wrote ([the squad](../formats/msn-mission-file.md#the-squad)), and [its draw of 9999](#the-loading-screen-after-the-unarmed-alert) reads through the deleted map. Menu 3333 says "You do not have a herc!" over OKAY (99), and `UnAssignedAlert`'s handler does nothing ([the screens](menus.md#the-screens)). Their 99, *back*, returns to the war map: the previous screen is 1500 or 1501 and the mission map is still set ([changing screens](menus.md#changing-screens--shell_handlemessage-0003c7d8), step 3). On the way to either, `Shell_HandleMessage`'s step 5 sets the byte `000817bd` to 3, so the war map keeps its briefing ([leaving it](#leaving-it)).

**Screen 9999** ends the shell with exit code 2 ([the ways out](startup.md#the-ways-out), [INSTANT ACTION's path](main-menu.md#instant-actions-path-to-the-mission)).

#### CANCEL

In `Shell_Mode` 1 it clears the current career record's byte `+1` (the roster `Career_Roster` (`0009b218`)'s record `0009b23b` + `0x1f7` × its word `+7`), sets the record's word `+4`, the career's screen, to 1500, saves the roster with `Career_Save` (`000168b0`)(roster, the roster's word `+7`) and makes the target 1200, the career menu; in any other mode it makes the target 1100, the SINGLE MISSION menu. Then it puts `anm\esiege2.anm` (the string `00080994`) into `000aacfe` with `sprintf` and the byte `0007fcd7` 2, so the next backdrop's draw plays it ([drawing a screen](menus.md#drawing-a-screen)), deletes the mission map with `Shell_DeleteMissionMap` and clears `000809a8`. The handler sets the target before `Shell_HandleMessage`'s *back* step, so CANCEL's 99 never becomes `Shell_PreviousScreen`.

So a single mission's way back from its briefing is the SINGLE MISSION menu, also after INSTANT ACTION >, and its list then selects the loaded mission's header word 3 ([the list](single-mission.md#the-list)).

#### ARMING and SERVICE BAY

ARMING goes to screen 1800, or 1820 while `Shell_LowMemory` is set; both are `ArmingMenu` (`00081fe6`) on backdrop 4000 ([the screens](menus.md#the-screens)). SERVICE BAY goes to 1700, `ServiceBayMenu`, also on backdrop 4000. Neither deletes the mission map, so `Shell_MissionMap` stays built for the way back, and `Shell_HandleMessage`'s step 10 leaves 1500 or 1501 in `Shell_PreviousScreen`, which a *back* from the next screen returns to while the map is set ([changing screens](menus.md#changing-screens--shell_handlemessage-0003c7d8), step 3). Neither sets `000817bd`, so the war map deletes the briefing's box and the page goes back to the first ([leaving it](#leaving-it)). The service bay's buttons are [the career's](between-missions.md#the-bars-buttons): its BRIEFING comes back to the war map at once while the record's byte `+1`, which SERVICE BAY sets, is set ([BRIEFING](between-missions.md#briefing)), and until that byte is set its BEGIN MISSION is greyed out ([the service bay's bar](between-missions.md#the-service-bays-bar)). What the arming screens do is the hangar's ([Open](#open)).

#### The map's buttons

Each of 108, 109 and 1503 to 1506, while `Shell_MissionMap` is set, calls a slot of the table at the map's `+0x4a` with the map: 108 `+8`, 109 `+0xc`, 1503 `+0x10`, 1504 `+0x14`, 1505 `+0x18` and 1506 `+0x1c`. Then, map or none, it sets `000809a8` to 3 and sets the pressed byte `+0` of the hotspot 1502, the window `+6`'s hotspot at `+0x19f` + `0x38` × `DUIWindow_FindHotspotById` (`0001be6f`)(window, 1502), so the next `DUIWindow_Update` calls `WarMapMenu_MapAreaNoOp` and clears it ([updating a window](menus.md#updating-a-window--duiwindow_update-0001c067)). The index is not tested: for −1 the byte written is the window's `+0x167`. What the six slots do is the mission map's ([Open](#open)).

### Leaving it

**`WarMapMenu_Dtor` (`0002bf15`)**, slot `+4`, runs when `Shell_HandleMessage`'s step 6, `Shell_OpenQuitAlert` or `Shell_CloseScreens` destroys the war map ([changing screens](menus.md#changing-screens--shell_handlemessage-0003c7d8), [the quit question](main-menu.md#the-quit-question)). `WarMapMenu_Dtor`(menu, flags), with a menu:

1. stores `WarMapMenu`'s vtable `00080bc6`;
2. while the byte `000817bd` is 0, deletes the briefing's box with `Dialog_DeleteBox` (`00018d93`)(`Shell_Dialog`) and sets `WarMap_BriefingPage` to 0;
3. clears `000809a8`;
4. unless `Shell_DebugMode` is set, with a keyboard device from `Input_GetKeyboardDevice_Thunk`, takes the five codes back from `KeyQueue_Subscriber` with `Keyboard_RemoveSubscriberCodes` (`00020030`) and puts the device's mode back from `WarMap_SavedKeyboardMode` with `KeyboardInputDevice_SetMode`, which reseeds the modifiers as the constructor's call does;
5. clears `000aa778`;
6. stores `Menu`'s vtable at `+0x22` and the region's three at `+0xa` in turn, and frees the menu when bit 0 of *flags* is set.

`000817bd` is set to 3 only on the way to 3000 or 3333 (`Shell_HandleMessage`'s step 5) and to the quit question (`Shell_OpenQuitAlert`), each while `000809a8` is set, which the constructor did. While it is not 0 and the message box is not up, `Shell_DrawScreen`'s step 4 ([drawing a screen](menus.md#drawing-a-screen)), with page 2 as the draw page, calls slot `+4` of `Shell_MissionMap`'s table `+0x4a` with the map and `Dialog_Draw`(`Shell_Dialog`); sets the draw page to the one the draw found and makes the same two calls; sets the draw page back to page 2 (`0007fccc`); lowers `000817bd` by 1; and only then draws the next screen's window on page 2. It makes the slot's call with no test that `Shell_MissionMap` is set. When it draws the war map again, `000809a8` set, it sets `000817bd` to 0. So the briefing stays on screen behind the two alerts and the quit question, and when the war map comes back its constructor loads the block again and turns to the same page. Every other way out, ARMING and SERVICE BAY among them, deletes the box and starts the next visit on the first page.

`Shell_DeleteMissionMap` (`0002a4ec`), while the map is set, stops the mouse queue (`maybe_MouseEventQueue_Stop`), calls `0000a9c8`(map) and `00009a73`(map, 3), and then clears `Shell_MissionMap`, so after BEGIN MISSION >, CANCEL or PROCEED a *back* (99) from a later screen does not return to the war map ([changing screens](menus.md#changing-screens--shell_handlemessage-0003c7d8), step 3).

### The loading screen after the unarmed alert

**In a single mission, PROCEED on the unarmed alert makes the loading screen's draw read through the mission map it has just deleted, and the shell faults there: a retail bug.** On that path:

1. BEGIN MISSION > posts 3000 while `000809a8` is 3, from the constructor, so `Shell_HandleMessage`'s step 5 sets `000817bd` to 3; the war map's destructor then keeps the briefing's box in `Shell_Dialog` and clears `000809a8`.
2. The alert's draw lowers `000817bd` to 2. Its second half leaves it: `000809a8` is clear.
3. PROCEED: `UnArmedAlert_OnMessage` makes the target 9999, deletes the map, which clears `Shell_MissionMap`, and clears `000809a8`. Step 5 writes `000817bd` only for 3000 and 3333 (`0003c8db`–`0003c918`), and screen 9999's case (`0003cab5`) does not write it, so it is still 2. `es2_xref.py --decode` finds its writers in step 5, `Shell_OpenQuitAlert` and `Shell_DrawScreen` alone, and the neighbouring `000817bc` only as a byte; the quit question opened from the alert sets it to 0, `000809a8` being clear, and so avoids what follows.
4. 9999's `Shell_DrawScreen`, the `SimLoadMenu` current and the message box down (no kind set since kind 0 closed), takes the `000817bd` branch (`0003c56b`): it pushes `Shell_MissionMap`, 0, loads it into `EBX` and reads `[EBX + 0x4a]` (`0003c584`), a data access to offset `0x4a`, which the image's expand-down data selector refuses ([loading the image](../formats/x32-executable.md#loading-the-image)). The fault comes before the slot's call and before `Dialog_Draw`, so the briefing is not drawn under the loading screen.

What the extender does with that fault is [Open](#open), as is whether any single mission reaches the path ([Open](#open)). In a career PROCEED goes to 1700 with message box kind 6 instead (the career's). ES2's shell has no counterpart to compare: its briefing is a view of its mission screen ([the mission screen](../../retail/shell/mission-screen.md)), and `es2_naming.py match` of `Shell_DrawScreen`'s range against `VSHELL` finds no candidate.

## Functions

| ES1 | Address |
|---|---|
| `WarMapMenu_MapAreaNoOp`, `WarMapMenu_Ctor`, `WarMapMenu_Dtor`, `WarMapMenu_OnMessage` | `0002bbaf`, `0002bbbd`, `0002bf15`, `0002c057` |
| `CareerRecord_AnyPilotUnarmed`, `Hangar_SlotIsUnarmed`, `Herc_IsUnarmed` | `0001620d`, `0002a354`, `00022ebf` |

## Globals

| | Address |
|---|---|
| `WarMap_BriefingPage`, `int16`: `PgDn` presses less `PgUp` presses since the box was last deleted | `000809b2` |
| `WarMap_MapKeys`, five scan codes and a 0 | `000809ac` |
| `WarMap_SavedKeyboardMode`, the keyboard's mode while the war map is up | `000a04d8` |
| the case table of `WarMapMenu_OnMessage`, twelve ids and their targets | `00080bdc` |

## Open

- **Open:** the mission map, S5's `map.md` (ES2's is [the briefing's mission map](../../retail/shell/mission-map.md)): the area 1502 and what `Shell_MissionMap` (`000809a4`) draws there; the slots `+8` to `+0x1c` of its `+0x4a` table that the zoom buttons 108 and 109 and the arrows 1503 to 1506 call; `0000a9c8`, which SERVICE BAY calls on it; the byte `000809a8`; and the byte `000aa778`, which the constructor sets, the destructor clears and the mouse callback `00010900` that `maybe_MouseEventQueue_Start` (`00010559`) registers tests (`es2_xref.py --decode` finds two more writers, in `Shell_HandleMessage` at `0003ea5a` and `0003eb9b`).
- **Open:** whether any single mission gives a squadmate a mech record whose every weapon is 17, which would send BEGIN MISSION > to 3000 and PROCEED into [the fault](#the-loading-screen-after-the-unarmed-alert): the first group's member records of the 14 single missions, read through `HercWorks.Query`.
- **Open:** what the X-32 extender does when the shell's data access to offset `0x4a` faults on [that path](#the-loading-screen-after-the-unarmed-alert): an error message and an exit to DOS, or a hang. A retail check under DOSBox-X, once a single mission with an unarmed squadmate is known: BEGIN MISSION > shows WARNING! "One or more Hercs in your squad is unarmed."; PROCEED, where the loading picture should come up, shows what the fault does; then whether `ESCD.EXE` runs the simulator next.
- **Open:** what the arming screens 1800 and 1820 and the service bay's display do and post to come back (S4's).
- **Open:** a retail check under DOSBox-X: INSTANT ACTION > and a key on the message open the war map with the pointer on ARMING and the briefing in a box under the map; `PgDn` turns its page; `Ctrl+Q` then CANCEL brings the war map back on the same page; ARMING and back brings it back on the first page; `Esc` opens SINGLE MISSION; BEGIN MISSION > shows the loading picture and leaves the shell.
