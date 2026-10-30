# GO.EXE — the debriefing

**Back from a single mission, after the talking head's line on how it went, the shell shows the debriefing: a window titled DEBRIEFING at the left of the screen, with the mission's debriefing text in a box at its top, two small arrow buttons at the box's right that turn its pages (`Up` and `Down` do the same), and below it a panel of the mission's results: the salvage, the kills of HERCs, flyers and structures by the player and by the whole squad, the squad's losses, the weapons recovered and the new technology recovered. MISSION > goes to the SINGLE MISSION menu and MAIN MENU to the main menu, each playing the shell's opening animation on the way. A career's debriefing has MAIN MENU and PROCEED > instead.** The menu is built, opened, drawn and left as every menu is ([the menus](menus.md)); this page holds what the debriefing adds. Which debriefing text a mission shows is [the `.ENG` file's](../formats/eng-mission-text.md#the-debriefing).

Addresses are `ES1GO` image addresses ([the X-32 executable](../formats/x32-executable.md#the-image)). The functions here remove their own arguments, so argument lists are in stack order ([calling conventions](../formats/compiler.md#calling-conventions)). ES2's debrief is its campaign loop's results step and a view of its mission screen ([the debrief](../../retail/shell/campaign-loop.md#the-debrief--game_processmissionresults-0040eae7)); nothing here is ported from it.

## Where the object file starts and ends

The debriefing's code, `0002c422` to `0002c742`, lies inside the menu handlers' file ([where it starts and ends](main-menu.md#where-the-object-files-start-and-end)), right after [the war map's](war-map.md#where-the-object-file-starts-and-ends): `DebriefingMenu_Ctor` (`0002c422`) follows `WarMapMenu_OnMessage`'s `RET 8` at `0002c41f`, and the `RET 8` of `0002c71a` at `0002c740` runs straight into `0002c743`, screen 1610's ([the screens](menus.md#the-screens)). The class's name `DebriefingMenu` stands at `00080c3c`, before its vtable `00080c4e`, which holds the destructor at `+4` and the handler at `+8`; the handler's case table `00080c64` follows. `es1_boilerplate.py bounds` over `0002c474`–`0002c842` finds no object-file boundary evidence.

| Address | Function | The vtable slot that holds it |
|---|---|---|
| `0002c422` | `DebriefingMenu_Ctor` | none: screen 1510's and 1520's case of `Shell_HandleMessage` (`0003c7d8`) calls it |
| `0002c574` | `DebriefingMenu_Dtor` | `+4` of `00080c4e` |
| `0002c638` | `DebriefingMenu_OnMessage` | `+8` of `00080c4e` |
| `0002c71a` | `Shell_BackToReturnScreen` | none found by `es2_xref.py` ([Open](#open)) |

## The screen

**`dlg\menu1510.dlg`, a single mission's debriefing, and `dlg\menu1520.dlg`, a career's, lay out a menu window titled DEBRIEFING, two text buttons along its bottom, the two page arrows and the results panel; their `eng\menu<n>.bin` hold the title and the buttons' texts.** The window is a menu window, style 0 (`MTWindow_Ctor`, [the kinds](../formats/dlg-menu-dialogs.md#the-kinds)), id 1510 or 1520, so it draws `res32\menu1510.dba` or `res32\menu1520.dba` and the title on its plate ([the menu window's drawing](menus.md#the-menu-windows-drawing--mtwindow_draw-00036b4c)). The window is (5, 2)–(168, 196) in 1510 and (5, 2)–(167, 196) in 1520; the other rects are in the window (`HercWorks.Query menu 1510 1520`):

| Id | Kind | Rect | Key | Text |
|---|---|---|---|---|
| 1100 | text button, 1510 only | (88, 178)–(158, 190) | | MISSION > |
| 1000 | text button | (4, 178)–(74, 190) in 1510, (5, 178)–(80, 190) in 1520 | `0x01` Esc in 1520 only | MAIN MENU |
| 1710 | text button, 1520 only | (83, 178)–(158, 190) | `0x39` Space | PROCEED > |
| 101 | icon button | (141, 29)–(152, 39) | `0x48` Up | |
| 102 | icon button | (141, 75)–(152, 85) | `0x50` Down | |
| 1522 | hotspot, kind 14 | (8, 87)–(154, 172) in 1510, (7, 87)–(154, 172) in 1520 | | the results panel |

The icon buttons take their frames from `res32\corners.dba` by key ([buttons](menus.md#buttons)). In 1510 no control has the key `Space`, so `Menu_Open` puts the pointer on control 0, MISSION >; in 1520 it goes to PROCEED > ([opening a menu](menus.md#opening-a-menu--menu_open-0003969e), step 4). `Menu_Open` changes no control for 1510 or 1520 (step 3). `Menu_Close` destroys both windows with `DUIWindow_Dtor`, as it does the plain windows of 1500 and 1501, not with `MTWindow_Dtor` ([closing a menu](menus.md#closing-a-menu--menu_close-0003b138), [Open](#open)).

### Opening it

**A single mission's way back reaches the debriefing through the talking head.** The shell, started again with exit code 3 or 4, runs `Shell_OpenFirstScreen` (`0003ba47`): `Mission_ReadRestore` (`00036512`) puts back the mission's paths and words ([which file](../formats/eng-mission-text.md#which-file)), `Shell_ProcessMissionResults` (`00038eb5`) reads `data\results.dat` and `data\mission.var` ([after a mission](../formats/bst-battle-sets.md#after-a-mission)), and in `Shell_Mode` (`000aad56`) 0, the pilot not lost, it puts up the message box's kind 3 over backdrop 5000 ([the first screen](menus.md#the-first-screen--shell_openfirstscreen-0003ba47)). With the pilot lost it opens the SINGLE MISSION menu instead and no debriefing is shown. Kind 3's answer, a key, a click or the end of the line, posts (0, −1, 0, 1510) through `MessageBox_Answer` (`0003837f`) ([the answer](message-box.md#the-answer)), and the next pass of the frame loop gives it to `Shell_HandleMessage` ([changing screens](menus.md#changing-screens--shell_handlemessage-0003c7d8)):

1. step 8 takes 1510's group, 5000 plus the table word for the current career record's campaign `+6` ([the backdrop](menus.md#the-backdrop)), and shows that backdrop when it differs from the current one;
2. step 9 runs 1510's case, which makes `DebriefingMenu_Ctor` (`0002c422`)(0) the current menu. The constructor builds the screen object over `Menu`'s vtable and stores `DebriefingMenu`'s `00080c4e`, and loads the `DEB:` block whose ordinal is the word `000aad58`, the first word of `data\results.dat`, from the mission's `.eng` path `000aad2a` into `Shell_Dialog` (`0009afc8`) with `Dialog_LoadConditional` (`00018cdb`) ([the debriefing](../formats/eng-mission-text.md#the-debriefing));
3. step 10 opens it with `Menu_Open`(menu, 1510) and draws it with `Shell_DrawScreen`.

1520's case first puts up the message box's kind 4 while the byte `MessageBox_Kind4Shown` (`00081d20`) is 0, and is otherwise 1510's ([the screens](menus.md#the-screens)); the career's way to it is [Open](#open).

The text box is the block's `ConditionalDialogBox` in `DialogBox_Backdrop5000Rect`, (17, 31)–(143, 85) on the screen, with `+0` 0 ([where the box stands](message-box.md#where-the-box-stands)): the debriefing's backdrop group is 5000 when the constructor runs. How its text is laid out in pages and drawn is [the text box's](message-box.md#laying-out-the-text); the frame loop's step 5 draws it each pass with `Dialog_Draw` ([the frame loop](startup.md#the-frame-loop)).

### The results panel

**Below the text the window shows a framed panel of the mission's results: the salvage, a table of kills with a column for the player and one for the squad, the squad's losses, the weapons recovered and the new technology recovered.** The panel is the hotspot 1522: `Menu_BuildFromDlg`'s kind-14 case adds it with `DebriefingResultsHotspot_Draw` (`000175d7`) as its handler ([the build](menus.md#opening-a-menu--menu_open-0003969e)), and `DUIWindow_Draw` calls the handler with (the hotspot's rect, 1522) when the window is drawn, as `DUIWindow_UpdateHotspots` does whenever the hotspot's pressed byte `+0` is set ([drawing a window](menus.md#drawing-a-window--duiwindow_draw-0001bfa3), [updating a window](menus.md#updating-a-window--duiwindow_update-0001c067)). The handler reads only the rect (*x0*, *y0*, *x1*, *y1*), in the window's render context `000ab612`, whose origin is the window's corner:

1. sets the context's alignment `+0x231` to 0, left, and fills the rect with color `0x1f` framed in `0x1f` with `Raster_FillFrameRectColours` (`0004b8d8`);
2. draws ten lines of `eng\watext.bin` (`0009afa8`, `Text_GetString`) in the font `000aacea`, `cstats_2` ([the window](menus.md#the-window)), with `HudFont_DrawString`;
3. draws the values in the font `000aace6`, `cstats_1`, which `Shell_LoadData` loads from `dfn\cstats_1.dfn` (`000207fa`) ([the loads](../formats/styles-dat.md#the-loads)), each formatted with `sprintf` as a decimal, `%ld` (`0007ed4d`) for the salvage and `%d` for the rest;
4. sets the alignment to 2, centered, with the field width `+0x235` *x1* − *x0*, and draws the text at `0007d808` at (*x0*, *y0* + 81), and leaves the alignment at 2.

| Line | Label | At | Value | At |
|---|---|---|---|---|
| `0xab` | SALVAGE: | (*x0* + 5, *y0* + 8) | the dword `0007fcf4`, `results.dat`'s salvage ([writing the results](../simulation/world.md#writing-the-results--mission_writeresults-0001d698)) | (*x0* + 89, *y0* + 8) |
| `0xac` | KILLS | (*x0* + 5, *y0* + 17) | | |
| `0xb1` | YOURS | (*x0* + 83, *y0* + 17) | | |
| `0xb2` | TOTAL | (*x0* + 118, *y0* + 17) | | |
| `0xad` | HERCS: | (*x0* + 14, *y0* + 26) | yours: the word `+0x25`; total: the word `0009ae5c` | *x0* + 94 and *x0* + 127, *y0* + 26 |
| `0xaf` | FLYERS: | (*x0* + 14, *y0* + 35) | yours: `+0x29`; total: `0009ae60` | *x0* + 94 and *x0* + 127, *y0* + 35 |
| `0xae` | STRUCTURES: | (*x0* + 14, *y0* + 44) | yours: `+0x27`; total: `0009ae5e` | *x0* + 94 and *x0* + 127, *y0* + 44 |
| `0xb3` | SQUAD LOSSES: | (*x0* + 5, *y0* + 53) | the word `0007ebca` | (*x0* + 94, *y0* + 53) |
| `0xb4` | WEAPONS RECOVERED: | (*x0* + 5, *y0* + 62) | the word `000817cc`, `results.dat`'s count of salvage pairs | (*x0* + 127, *y0* + 62) |
| `0xb5` | NEW TECHNOLOGY RECOVERED: | (*x0* + 5, *y0* + 71) | the text at `0007d808`, centered across the panel | (*x0*, *y0* + 81) |

The `+0x25`, `+0x27` and `+0x29` words are of `CareerRecord_Current` (`00014ef4`)'s record, copied together as six bytes: pilot entry 0's `+0x11`, `+0x13` and `+0x15`, a mission's gains to its `+0x17`, `+0x19` and `+0x1b` ([pilot entries](../formats/bst-battle-sets.md#pilot-entries)). The totals are the words `CareerRecord_ReadPilotResults` zeroes and `PilotEntry_ApplyGains` adds each pilot's three gains to, in the same order, and `0007ebca` the count `Shell_ProcessMissionResults` makes of the single mission's pilots whose condition is 0 ([after a mission](../formats/bst-battle-sets.md#after-a-mission)). The image holds `NONE` at `0007d808` ([Open](#open)).

### The buttons and keys

**A release posts its control's id ([a message's path](menu-input.md#a-messages-path)); `DebriefingMenu_OnMessage` (`0002c638`), slot `+8` of `DebriefingMenu`'s vtable, acts on five ids, and `Shell_HandleMessage` then opens the screen the message names ([changing screens](menus.md#changing-screens--shell_handlemessage-0003c7d8)).** The handler looks the message's target `+6` up among the five ids of the `REPNE SCASD` table at `00080c64`, its cases at `00080c78`, and does nothing for any other id, 1522 among them. Three of its cases lie in bytes Ghidra left undefined, from `0002c67f`.

| Control | Id | The handler | Then |
|---|---|---|---|
| up arrow, `Up` | 101 | `Dialog_PrevPage` (`00018df0`)(`Shell_Dialog`) | no screen: 101 is not in `Shell_ScreenIds` |
| down arrow, `Down` | 102 | `Dialog_NextPage` (`00018e18`)(`Shell_Dialog`) | no screen |
| MAIN MENU | 1000 | `Shell_ReturnScreen` (`0007fce8`) set to 1000; `anm\esiege2.anm` (the string `00080994`) put into `000aacfe` with `sprintf` and the byte `0007fcd7` set to 2 | the main menu, 1000 |
| MISSION > | 1100 | `Shell_ReturnScreen` set to 1100; `anm\esiege2.anm` into `000aacfe` and `0007fcd7` 2 | the SINGLE MISSION menu, 1100 |
| PROCEED > | 1710 | the target becomes 5555 when the current career record's word `+0xa8` is not −1, the record the roster `0009b218`'s word `+7` numbers among the records from `0009b23b`, `0x1f7` bytes each; `Shell_ReturnScreen` set to 1200 | 5555 or 1710 |

Each page arrow steps the box a page and draws it ([stepping pages](message-box.md#stepping-pages)); the debriefing keeps no page count of its own. With `0007fcd7` 2 the next screen change shows the new backdrop even within the same group and its draw plays `anm\esiege2.anm` ([changing screens](menus.md#changing-screens--shell_handlemessage-0003c7d8), step 8; [drawing a screen](menus.md#drawing-a-screen), step 2). `Shell_ReturnScreen` names the screen the options menu's RETURN button goes back to and its text ([the return screen](main-menu.md#the-return-screen)). Screen 5555 is `TechReleaseAlert` ([RESUME](main-menu.md#resume)).

So a single mission's debriefing leaves for the SINGLE MISSION menu or the main menu, and neither of its buttons carries a key.

### Leaving it

**`DebriefingMenu_Dtor` (`0002c574`)**, slot `+4`, runs when `Shell_HandleMessage`'s step 6, `Shell_OpenQuitAlert` or `Shell_CloseScreens` destroys the debriefing ([changing screens](menus.md#changing-screens--shell_handlemessage-0003c7d8), [the quit question](main-menu.md#the-quit-question)). `DebriefingMenu_Dtor`(menu, flags), with a menu:

1. stores `DebriefingMenu`'s vtable `00080c4e`;
2. deletes the debriefing's box with `Dialog_DeleteBox` (`00018d93`)(`Shell_Dialog`), with no test of `000817bd`;
3. stores `Menu`'s vtable `00080ba8` at `+0x22` and the region's three at `+0xa` in turn (`00080b7c`, `00080b44`, `00080b12`), and frees the menu with `Mem_Delete` when bit 0 of *flags* is set.

So the debriefing deletes its box on every way out, the quit question included, where [the war map](war-map.md#leaving-it) keeps its briefing's box across its alerts and the quit question; whenever 1510's case runs again, the constructor loads the block again on its first page.

## Functions

| ES1 | Address |
|---|---|
| `DebriefingMenu_Ctor`, `DebriefingMenu_Dtor`, `DebriefingMenu_OnMessage` | `0002c422`, `0002c574`, `0002c638` |
| `DebriefingResultsHotspot_Draw` | `000175d7` |
| `Shell_BackToReturnScreen`: (menu, message), a message's target 99 becomes `Shell_ReturnScreen` (`0007fce8`), any other is left | `0002c71a` |

## Globals

| | Address |
|---|---|
| the case table of `DebriefingMenu_OnMessage`, five ids and their targets | `00080c64` |
| the text the results panel shows as the new technology recovered, `NONE` in the image | `0007d808` |

## Open

- **Open:** what reaches `Shell_BackToReturnScreen` (`0002c71a`): `es2_xref.py` finds no branch, stored pointer or vtable slot holding it.
- **Open:** what writes the text at `0007d808`, which `Tech_FormatLockedName` (`000021b5`) and `MessageBox_PickT1Line` (`00037bf0`) also pass (`es2_xref.py --decode` finds those four address uses and no other), and so what NEW TECHNOLOGY RECOVERED shows after a single mission, where `Tech_Unlock` does not run.
- **Open:** what `Menu_Close`'s `DUIWindow_Dtor` on the menu windows 1510 and 1520 leaves of the work `MTWindow_Dtor` does, their bitmap arrays `res32\menu1510.dba` and `res32\menu1520.dba` among it.
- **Open:** what the SINGLE MISSION menu's list selects after a mission: `Menu_Open` selects the line `Mission_Kind` (`000aad5a`) holds ([the list](single-mission.md#the-list)), and the shell started after the mission restores the saved word into `000aad5e`, not `000aad5a` ([which file](../formats/eng-mission-text.md#which-file)).
- **Open:** the career's way to the debriefing, screen 1520 and kind 4, and what screen 5555 and PROCEED >'s 1710 then show (the career module's).
- **Open:** a retail check under DOSBox-X: finish a single mission (INSTANT ACTION >), take the talking head's line with a key, and the DEBRIEFING window shows the text in its top box, the results panel below it with SALVAGE:, the KILLS table under YOURS and TOTAL, SQUAD LOSSES:, WEAPONS RECOVERED: and NEW TECHNOLOGY RECOVERED:; `Down` turns the text's page when it has more than one; MISSION > shows the opening animation and the SINGLE MISSION menu.
