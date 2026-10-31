# GO.EXE — repairing, building and scrapping HERCs

**After a career mission the service bay's REPAIR and BUILD open small windows down the right of the hangar's picture. REPAIR shows the selected HERC's type, pilot, condition and the salvage a full repair needs; REPAIR ALL repairs it as far as the salvage reaches, REPAIR opens a second window that repairs one system at a time, a level at a time, with + and −, SCRAP turns the HERC into salvage, and PREFS sets which HERCs the shell repairs on its own after a mission. BUILD shows one chassis at a time with its mass, speed and capacity and its price; BUILD pays for it at once and the HERC takes one to three missions to finish, during which the same button reads SCRAP and gives the salvage back.** The bay's bar is [between missions](between-missions.md#the-service-bays-bar)'s, the bay's own display and CREW [hangar.md](hangar.md)'s, and fitting weapons [arming.md](arming.md)'s. ES2's repair, build and scrap screens are VSHELL's ([the repair screen](../../retail/shell/weapons-and-repair.md#the-repair-screen), [the build screen](../../retail/shell/build-and-armory.md#the-build-screen)); the twins are named in [Functions](#functions).

Addresses are `ES1GO` image addresses ([the X-32 executable](../formats/x32-executable.md#the-image)). The functions here remove their own arguments, so argument lists are in stack order ([calling conventions](../formats/compiler.md#calling-conventions)). A screen's lists are kind-15 list boxes of style 1 used as readouts: the handlers set a list's lines with its slot `+0x44` and select a line with its slot `+0x24`, and read the selected line with slot `+0x2c` ([the list box](menus.md#the-list-box)). A control is made inactive or active by writing state 2 or 0 to its `+0x19` and 1 to *changed* `+0x1f` ([opening a menu](menus.md#opening-a-menu--menu_open-0003969e)). The selected HERC is the HERC in the hangar's slot `Hangar_SelectedSlot` (`00080894`) of `Hangar_Slots` (`000808a0`) ([the HERC lists](../formats/car-hercs-and-stock.md#the-herc-lists)); "no HERC" below means that slot is −1 or empty.

## Where the object files start and end

**`mechman.cpp`** runs from `0002845c` to `0002a3d7`. The file before, `inventor.cpp` (`arming.md`), ends with a `RET 0xc` at `00028456` and three zero bytes; `Hangar_SaveFile` (`0002909c`) asserts with the path `src\mechman.cpp` (`00080920`, pushed at `000290ce`); and the file ends with a `RET 4` at `0002a3d3` and two zero bytes before the memory log's file at `0002a3d8` ([the memory log's file](main-menu.md#where-the-object-files-start-and-end)). It has no static initializer. `es1_boilerplate.py scan` over it finds one library copy, `Rect_CopyCtorMechManCopy` (`00028a9b`), byte for byte `Rect_CopyCtor` (`0004b398`). It holds the hangar's HERC list ([the HERC lists](../formats/car-hercs-and-stock.md#the-herc-lists)), the selection helpers below, BUILD's information panel, the build, the scrap and the repairs' entry points.

**The screens' code**, `0002ecc0` to `00031367`, is the last part of [the menu handlers' file](main-menu.md#where-the-object-files-start-and-end): REPAIR's, SYSTEM REPAIR's, PREFERENCES', SCRAP's, BUILD's and SALVAGE's handlers and helpers, then the file's static initializer `MenuHandlers_StaticInit` (`00030ee9`, entry `0009a260`) and its exit routine `MenuHandlers_StaticExit` (`00031367`, exit entry `0009a29c`), a bare `PUSH EBP`, `MOV EBP,ESP`, `POP EBP`, `RET`. The initializer stores 18 (*x*, *y*) dword pairs at `000a04dc`: (37, 72), (29, 69), (23, 72), (19, 66), (21, 63), (0, 0), (34, 72), (26, 72), (18, 70), (36, 67), (32, 66), (32, 60), (17, 71), (42, 71), (28, 75), (17, 74), (25, 74) and (29, 69); `es2_xref.py --decode` finds them read by ARMING's hotspot handlers `Arming_DrawWeaponInfo` (`0002d5bf`) and `Arming_DrawMissileInfo` (`0002dacf`) (`arming.md`). `es1_boilerplate.py scan` over the range finds `HercStatus_GetMenuHandlersCopy` (`000304c6`), byte for byte `HercStatus_Get` (`00015896`), and `Tech_IsAvailableMenuHandlersCopy` (`00030e83`), byte for byte `Tech_IsAvailable` (`00028409`).

## The hangar's selection

**The hangar's screens work on one HERC at a time, the selected one, and step through the HERCs with the HERCS arrows; the HERC being built is skipped.** `mechman.cpp`'s helpers do the stepping and the screen change that goes with it.

- `Hangar_NextHercSlot` (`00029729`)() adds 1 to `Hangar_SelectedSlot` modulo the slot count `Hangar_SlotCount` (`00080890`) until the slot holds a HERC and is not the marked slot `Hangar_MarkedSlot` (`00080896`), or comes back to where it started; `Hangar_PrevHercSlot` (`000296c9`)() does the same downwards, from slot 0 to the last.
- `Hangar_NextUnpilotedHercSlot` (`0002978c`)() and `Hangar_PrevUnpilotedHercSlot` (`000297cb`)() step with those until `CareerRecord_FindPilotBySlot` (`00015c39`)(`CareerRecord_Current` (`00014ef4`), slot) answers −1, a HERC no pilot holds, or the selection comes back to where it started.
- `Hangar_SelectMarkedSlot` (`0002980a`)() gives the marked slot `Hangar_FindFreeSlot` (`0002983a`)'s answer when it is −1, and selects it.
- `Hangar_SelectNext` (`0002986f`)(mode) calls `Hangar_LeaveSelectedHerc` (`000295e7`), then by mode: 0 `Hangar_NextHercSlot`; 1 `CareerRecord_NextPilot` (`00015b0a`)(the current career record); 2 `Hangar_NextUnpilotedHercSlot`; 3 `Hangar_SelectMarkedSlot` and, when the marked slot is then not −1, `HangarBay_AssignSlotScripts` (`000120f9`)(the marked slot, 1). It then wipes the screen to color 0 with `Screen_BlindsClear` (`0004c053`)(8, 0, 1), shows the new HERC with `Hangar_ShowSelectedHerc` (`000294d8`), selects no mount with `Hangar_SelectMount` (`00029a14`)(−1), and wipes the new picture in with `Screen_BlindsCopy` (`0004bc0d`)(8, the display's visible page `+0x30`, its draw page `+0x38`, 1).
- `Hangar_SelectPrev` (`00029946`)(mode) is the same downwards for modes 0 to 2 (`Hangar_PrevHercSlot`, `CareerRecord_PrevPilot` (`00015b85`), `Hangar_PrevUnpilotedHercSlot`), with no mode 3, and wipes and shows only when the selected slot is then not −1.

`Hangar_LeaveSelectedHerc` (`000295e7`)() calls `Herc_EraseFromBay` (`000241cd`)(the selected HERC) and `Hangar_ShowSelectedHerc` (`000294d8`)(), for a selected slot that is not −1, sets the pages of the bay's group scripts by the slot ([the group's scripts](menus.md#the-backdrop)) with `Ttm_SetPage`, on the handles of the 3-byte entries 0, 1 and 2 of `ShellBackdrop_GroupTtms`: entry 0's script to the page `000aa7b0`[slot] + 1, entry 1's to `000aa7c0`[slot] + 1 and entry 2's to slot mod 8 + 1; then, for a HERC, `Herc_DrawInBay` (`000241f9`)(HERC, the marked slot's count `Hangar_MarkedMissionsLeft` (`00080898`) for the marked slot, otherwise 0), and last `ShellBackdrop_RunScripts` (`00012467`)() ([showing a bay](hangar.md#showing-a-bay)). `HangarBay_AssignSlotScripts` (`000120f9`)(slot, mode) gives a slot its scripts and picks those two words, by mode ([giving the scripts their bays](hangar.md#giving-the-scripts-their-bays)): 0, `TtmEntry_AssignSlot` (`00012047`)(slot, 6), a draw below 5 into `000aa7b0`[slot] and 2 into `000aa7c0`[slot]; 2, `TtmEntry_AssignSlot`(slot, slot + 9), into `000aa7b0`[slot] 3 plus a draw below 2 for slot 0 and a draw below 5 for any other, and into `000aa7c0`[slot] a draw below 5 for a chassis below 5 and below 3 for the others; 1, `TtmEntry_FreeSlot` (`000120ba`)(slot), then, with no marked HERC or a selected HERC at 0% built (`Hangar_SelectedBuildProgress`, `0002a2a1`), `TtmEntry_AssignSlot`(slot, 8) and 3 plus a draw below 2 into `000aa7b0`[slot], and otherwise `TtmEntry_AssignSlot`(slot, 7), `TtmEntry_AssignSlot`(slot, the entry the word `0007e700`[chassis]) while the selected HERC is below 51% built, and a draw below 3 into `000aa7b0`[slot]; for mode 1, a draw below 5 into `000aa7c0`[slot]. The draws are `Math_RandomBelow`(`Shell_RandomState`, *n*). `Hangar_SelectedBuildProgress` (`0002a2a1`)() is `Hangar_MarkedHercProgress` (`00029f93`)'s (*c* − `Hangar_MarkedMissionsLeft`) × 100 / *c* on the selected HERC's chassis ([the marked HERC](between-missions.md#the-marked-herc)), with chassis −1 for no HERC.

**The wipes.** `Screen_BlindsCopy` (`0004bc0d`)(*n*, *to*, *from*, flag) installs the full-screen render context `0007fcd8`, sets the shell display `000ab616`'s source page `+0x34` to *from* and its draw page `+0x38` to *to* (through the raster's routines `000865f0` and `000865f4` while it is the current display), and in *n* passes copies one-pixel rows 320 wide from *from* to *to* with `Region_CopyBetweenPages`, pass *i* the rows *i*, *i* + *n*, *i* + 2*n*, … below the row count `0009b00c`, waiting a tick (`Shell_WaitTicks`(1)) after each pass; then it puts both pages and the render context back. Its fourth argument is not read. `Screen_BlindsClear` (`0004c053`)(*n*, color, flag) clears the draw page to the color through the raster's routine `0008663c` ([lines and clipped bitmaps](../rendering/screen-pages.md#lines-and-clipped-bitmaps)), blinds it over the visible page with `Screen_BlindsCopy`(*n*, the visible page `+0x30`, the draw page, flag), and copies the region `0009aff8` from page 2, the background (`0007fccc`), onto the draw page. So the HERCS arrows black the screen out in eight interleaved passes, a tick apart, and bring the new HERC in the same way.

**Mounts.** `Hangar_SelectMount` (`00029a14`)(mount), `Hangar_NextMount` (`00029a52`)() and `Hangar_PrevMount` (`00029a87`)() call, on the selected HERC, `Herc_HighlightMount` (`00024326`)(HERC, mount), which makes *mount* the current mount `+0x70`, `Herc_HighlightNextMount` (`000242a6`)(HERC), which steps it forward modulo the mount count, and `Herc_HighlightPreviousMount` (`000242e8`)(HERC), which steps it back; `Hangar_ClearSelectedMount` (`0002961c`)() calls `Herc_HideMountHighlight` (`00024287`)(HERC), which leaves no mount current. `Hangar_FitWeapon` (`00029abc`)(weapon) fits a weapon to the selected HERC's current mount: nothing when the mount already holds *weapon*; the condition is 100 for weapon 17, the empty mount, and otherwise the word `Inventory_Take` (`0002833f`)(the stock `000aad62` points to, weapon) pops, nothing more happening when that is −1; then `Herc_EraseMounts` (`00024561`)(HERC), and, when the mount held a weapon other than 17, `Inventory_Add` (`00028311`) of the mount's condition (`Herc_CurrentMountStatus` (`00024b51`)(HERC)) onto the old weapon's slot, so the old weapon goes back to the stock; then `Herc_SetCurrentMountStatus` (`00024aea`)(HERC, condition), `Herc_SetCurrentMountWeapon` (`00024788`)(HERC, weapon) and `Herc_DrawMounts` (`00024670`)(HERC). ARMING calls these (`arming.md`). `Hangar_ReloadSelectedLoadout` (`00028ec9`)(), outside `Shell_Mode` 1, opens entry 10 of the file-name table, `car\sm_play.mec` ([the squad](../formats/msn-mission-file.md#the-squad)), as a `FileRStream` and runs `HercList_RefitSelectedFromStream` (`00028f16`)(stream), which passes over the list's 12 header bytes and, for each HERC, reads its slot; the HERC in the selected slot is read into a HERC of its own, and for each of its mounts `Hangar_SelectMount`(mount) and `Hangar_FitWeapon`(that HERC's weapon on the mount) refit the selected HERC; the others are passed over field by field. `es2_xref.py` finds no caller of `Hangar_ReloadSelectedLoadout` ([Open](#open)).

## REPAIR — screen 1900

**The repair window shows the selected HERC's type, its pilot, its condition, the salvage a full repair needs and the salvage available; the HERCS arrows step to another HERC.** `dlg\menu1900.dlg`'s window REPAIR is (181, 2)–(316, 196) (`HercWorks.Query menu 1900`):

| Control | Id | Rect | Key | Does |
|---|---|---|---|---|
| REPAIR | 1920 | (14, 139)–(49, 151) | | opens [SYSTEM REPAIR](#system-repair--screen-1920) |
| SCRAP | 1901 | (50, 139)–(85, 151) | | the target becomes 4444, [SCRAP](#scrap--screen-4444) |
| PREFS | 1910 | (86, 139)–(121, 151) | | opens [PREFERENCES](#preferences--screen-1910) |
| REPAIR ALL | 1902 | (14, 153)–(67, 165) | `Space` | [below](#repair-all) |
| HERCS, icon buttons | 2003, 2004 | (69, 154)–(94, 166), (95, 154)–(120, 166) | | `Hangar_SelectPrev`(0) for 2003, `Hangar_SelectNext`(0) for 2004, then `RepairMenu_RefreshHerc` |
| PROCEED | 1710 | (46, 175)–(88, 187) | `Esc` | `CareerRecord_SaveCurrent` (`00014f28`), then the bay after a mission |
| five centered labels | 0 | (21, 28), (21, 48), (21, 68), (21, 88) and (21, 108), each to *x* 114 and 10 high | | HERC TYPE, PILOT, CONDITION, SALVAGE REQUIRED, SALVAGE AVAILABLE |
| list boxes | 1903, 1904, 1905, 1906, 1907 | (20, 37), (20, 57), (20, 77), (20, 97) and (20, 117), each to (116, *y* + 10) | | the five readouts below their labels |

`RepairMenu_OnMessage` (`0002f009`) looks the message's target `+6` up among the five ids of the `REPNE SCASD` table `RepairMenu_CaseTable` (`00080e20`), 2004, 2003, 1902, 1901 and 1710; REPAIR and PREFS are not in it, so their ids open their screens ([changing screens](menus.md#changing-screens--shell_handlemessage-0003c7d8)). The window is `RepairMenu` (`00082006`) on backdrop 4000.

**The readouts.** `Menu_BuildFromDlg` gives each list its lines with `Menu_ListLines` (`0003750d`) and its selected line from the kind-15 ids of `00081a00` ([the build](menus.md#opening-a-menu--menu_open-0003969e)): 1903 the chassis names, `eng\watext.bin` lines `0x1b`–`0x22` (the word list `00081250`), ROADRUNNER, OUTLAW, RAPTOR, TOMAHAWK, PATRIOT, RHINO, SAMSON and COLOSSUS for chassis 0 to 7, on the selected HERC's chassis word; 1904 the pilots' names, lines `0x55`–`0x7a` (`000811e8`), whose first, PLAYER, is replaced in `Shell_Mode` 1 by the roster's dword `+1`, on `PilotEntry_NameIndex` (`00015f50`)(the current record, its current pilot `+0x12`) + 1; 1905 the five levels, lines `0x7b`–`0x7f` (`00081238`), NOMINAL, LIGHT DAMAGE, MODERATE DAMAGE, HEAVY DAMAGE and DESTROYED, on `Repair_LevelForCondition` (`000389fb`) of `Herc_OverallCondition` (`00024b6a`); 1906 the number `Repair_HercCost` (`00038c50`)(100, the selected HERC), 0 for no HERC; and 1907 the number `Career_Salvage` (`0007fcf0`). A number list is `WaText_NumberLine` (`00037468`)(value, line): a one-line array whose line is the buffer of `eng\watext.bin` line *line*, a placeholder "0123456789", into which it prints the value with `%ld`; `Menu_NumberLines` (`000374af`)(value, id) passes line `0xb9` for 1907, `0xba` for 1906 and 1927 and `0xbb` for 1923. So the salvage figures are in kilograms, the pool's own unit, where ES2's repair screen also prints kilograms ([one currency, two units](../../retail/shell/armory.md#one-currency-two-units)).

`RepairMenu_RefreshHerc` (`0002ee70`)(menu) selects the selected slot's pilot, `CareerRecord_SelectPilot` (`00015ad4`)(record, `CareerRecord_FindPilotBySlot`(record, slot)), selects line `PilotEntry_NameIndex`(record, that entry) + 1 of 1904, the chassis word (−1 for no HERC) of 1903 and the level of the overall condition (0 for no HERC) of 1905, and runs `RepairMenu_RefreshSalvage` (`0002ecc0`)(menu), which sets 1907's lines to the salvage and 1906's to `Repair_HercCost`(100, HERC) while the overall condition is below 100, and to 0 otherwise, each selecting line 0. A HERC no pilot holds gets entry −1 and `PilotEntry_NameIndex`'s 36, so 1904 reads UNASSIGNED, line `0x7a`.

**Opening it.** Screen 1900's case (`0003f3e8`) makes a `RepairMenu` the current menu and sets the dword `0007fce4` to `0008003c`, as screens 1920 and 2000 do ([the mouse](menu-input.md#the-mouse)); when the selected slot holds no HERC it calls `Hangar_SelectNext`(0) and selects that slot's pilot with `CareerRecord_SelectPilot`. `Menu_Open` then makes SCRAP inactive when the HERC count `Hangar_HercCount` (`00080892`), less 1 when `Hangar_HasMarkedHerc` (`0002a277`) answers non-zero, is below 2 ([opening a menu](menus.md#opening-a-menu--menu_open-0003969e), step 5): the last finished HERC cannot be scrapped. The bay's REPAIR opens it ([the bar's buttons](between-missions.md#the-bars-buttons)).

### REPAIR ALL

**REPAIR ALL repairs the selected HERC as far down the after-mission repair ladder as the whole salvage pool reaches.** Its case calls `Repair_AutoSelected` (`00029bdd`)(), which is `Repair_Auto` (`00029bf3`)(`Career_Salvage`, `Hangar_SelectedSlot`) ([repairs](between-missions.md#repairs)): from level 0, the full repair to 100, one level down while the cost is above the pool; when the cost is then below the pool it sets every status word to the level's target, takes the cost from `Career_Salvage`, and stores pool − cost as the share `+0x21` of the pilot entry that holds the slot, the record's dword `+0x10` when none does. Then `Hangar_ShowSlot` (`00029651`)(slot) shows the bay again ([showing a bay](hangar.md#showing-a-bay)), 1905 selects the new level, `RepairMenu_RefreshSalvage` runs and `Roster_ForEachPilotNoOp` (`00016fdd`)(the roster) does nothing. ES2's REPAIR ALL rebuilds to 100 for `Repair_HercCost` and is gated on the pool covering it ([repairing and cancelling](../../retail/shell/weapons-and-repair.md#repairing-and-cancelling)); ES1's is never inactive and repairs nothing when even the lowest level costs the pool or more.

## SYSTEM REPAIR — screen 1920

**SYSTEM REPAIR lists one system of the HERC at a time, its internal parts, its external areas or its weapons, each with its condition; + repairs the selected item one level, − undoes one level, CANCEL undoes every change since the window opened, and PROCEED keeps them.** `dlg\menu1920.dlg`'s window REPAIR is (181, 2)–(316, 196):

| Control | Id | Rect | Key | Does |
|---|---|---|---|---|
| list box, style 0 | 1921 | (17, 29)–(105, 67) | | the system's items, a click selects one |
| arrows, icon buttons | 101, 102 | (107, 29)–(118, 39), (107, 58)–(118, 68) | `Up`, `Down` | the list's previous or next item |
| label SYSTEM, then INT, EXT, WPN | 0; 1924, 1925, 1926 | (17, 69)–(50, 79); (53, 69)–(74, 78), (75, 69)–(96, 78), (97, 69)–(118, 78) | `I`, `E`, `W` | the system shown |
| label STATUS, list box | 0; 1922 | (17, 95)–(49, 105); (17, 84)–(118, 94) | | the item's level, NOMINAL to DESTROYED |
| labels SALVAGE, AVAILABLE, REQUIRED | 0 | (49, 107)–(84, 117), (20, 129)–(64, 139), (74, 129)–(113, 139) | | |
| list boxes | 1907, 1927 | (17, 118)–(66, 129), (69, 118)–(118, 129) | | the pool, the cost of one level up |
| label ALLOCATED, list box | 0; 1923 | (17, 155)–(63, 165); (17, 141)–(92, 152) | | the salvage spent in this window |
| +, − icon buttons | 106, 107 | (94, 141)–(105, 151), (107, 141)–(118, 151) | keypad +, keypad − | [below](#and) |
| CANCEL | 99 | (22, 175)–(66, 187) | `Esc` | [below](#cancel-and-proceed) |
| PROCEED | 1900 | (67, 175)–(111, 187) | `Space` | [below](#cancel-and-proceed) |

**The window object.** Screen 1920's case (`0003f5a4`) makes the menu with `SystemRepairMenu_Ctor` (`0002f1aa`)(0), class `SystemRepairMenu`, vtable `00080e5c` (slot `+4` `SystemRepairMenu_Dtor` (`0002f460`), `+8` `SystemRepairMenu_OnMessage` (`0002f8d4`)), a `0x72`-byte object: the screen object's `0x26` bytes ([the screen object](menus.md#the-screen-object)), then

| Offset | Holds |
|---|---|
| `+0x26` | `int16` the system shown: `0x784` internal, `0x785` external, `0x786` weapons, the [status kinds](../formats/car-hercs-and-stock.md#the-herc-record), which are also the ids of INT, EXT and WPN |
| `+0x28` | `int16` the selected item, the line of 1921 |
| `+0x2a` | `Career_Salvage` when the window opened |
| `+0x2e` | the selected HERC's 66-byte status block when the window opened |
| `+0x70` | `int16` the salvage allocated in the window |

The constructor builds the status block with `HercStatus_Ctor`, then sets the system to internal, the item to 0, the allocated salvage to 0 and `SystemRepair_ItemLines` (`00080e10`) to 0, keeps `Career_Salvage`, copies the HERC's block over its own with `Hangar_CopySelectedStatus` (`0002f3e9`), and puts the cost of one level up of internal 0 in `Repair_StepCost` (`000ab212`). `Hangar_CopySelectedStatus`(buffer) copies the selected HERC's block `+0x2e` into the buffer, allocating 66 bytes when it is null; with no HERC it copies nothing and answers the slot number. The build selects 1922's line `Repair_LevelForCondition` of internal 0's condition, and 1921's lines are `Hangar_SelectedComponentLines` (`000377cd`)(1921), which is `Herc_BuildStatusLines` (`00024d99`)(the selected HERC, 1921): the internal parts' names, the same list as for `0x784`, each line starting with the item's condition ([the status lines](../formats/herc-diagrams.md#the-status-lines)). `Menu_Open` makes + inactive when `Repair_StepCost`, sign-extended, is above `Career_Salvage`, compared unsigned, and active otherwise; `SystemRepairMenu_Refresh` writes the same test after every change.

**The cost of a level.** `Repair_UnitValueCost` (`00038bb9`)(up, condition, index, kind, chassis) is ES2's `Repair_UnitValueCost` (`00413968`), its arguments in reverse and the kinds `0x785`, `0x784` and `0x786` for ES2's modes 0, 1 and 2: it prices `Repair_LevelStepCost` (`00038a72`)(up, condition, value) with the chassis row's word *index* of `000a0580` for an external area, the row's internal word *index* for an internal part, and `000ab332`[*index*] for a weapon, any other kind answering the kind ([the load](../formats/herc-catalogs.md#the-load)). `Repair_LevelStepCost` is ES2's ([what one repair level costs](../../retail/shell/armory.md#what-one-repair-level-costs)), its arguments in reverse, over ES1's five levels ([repairs](between-missions.md#repairs)): 0 for a level below 0 or above 4; up, (100 − condition) × value / 100 at level 0 and (the floor of the level above − condition) × value / 100 at any other; down, (100 − the level's floor) × value / 100 for a condition of 100 and (condition − the floor of the level below) × value / 100 for any other. ES1's screen uses both directions. **A weapon is priced by its mount's number, not by the weapon it carries**: the handlers pass the item, the list's line, as the index, where ES2's repair screen keys the weapon table by the fitted weapon ([what one repair level costs](../../retail/shell/armory.md#what-one-repair-level-costs), [Open](#open)).

`Repair_TargetForLevel` (`00038a30`)(level) and `Repair_FloorForLevel` (`00038a5c`)(level) are ES2's: 100 for level 0 and `RepairTargetTable` (`00081790`)[level] + 1, the floor of the level above, for any other; and `RepairLevelTable` (`0008179c`)[level], the level's own floor: 91, 75, 52, 1 and 0.

**Refreshing.** `SystemRepairMenu_SelectItem` (`0002f517`)(menu) takes 1921's selected line as the item, selects in 1922 the level of the item's condition (`Herc_GetStatus`, 0 for no HERC), sets 1923's lines to the allocated salvage and selects line 0, and runs `SystemRepairMenu_Refresh` (`0002f680`)(menu), which sets 1907's lines to `Career_Salvage`, puts `Repair_UnitValueCost`(1, the item's condition, item, system, chassis) in `Repair_StepCost`, sets 1927's lines to it, and makes + inactive or active by the test above. `Hangar_FormatSelectedStatusLine` (`00030478`)(item, line, system) calls `Herc_FormatStatusLine` (`00024ef4`)(the selected HERC, item, line, system), which writes the item's condition over the line's first three characters unless it starts with `-`.

`SystemRepairMenu_OnMessage` (`0002f8d4`) first reads 1921's selected line, then looks the target up among the ten ids of `SystemRepairMenu_CaseTable` (`00080e6c`), 1921, 1924, 1925, 1926, 106, 107, 101, 102, 1900 and 99:

| Target | Does |
|---|---|
| 1921, a click on the list | `SystemRepairMenu_SelectItem` |
| 1924, 1925, 1926 | the system becomes the id; `SystemRepair_ItemLines` becomes `Menu_ListLines`(the id), the system's items with their conditions (a weapon's line `0x8f` + weapon, "???% ATC 20MM" for weapon 0, an empty mount's `0xa0`, "---- NONE", [the status lines](../formats/herc-diagrams.md#the-status-lines)); 1921 takes them and selects line 0; `SystemRepairMenu_SelectItem` |
| 101, 102 | 1921's slot `+0x1c` or `+0x20`, the previous or next line ([the list box](menus.md#the-list-box)); `SystemRepairMenu_SelectItem` |
| 106, +; 107, − | [below](#and) |
| 1900, PROCEED; 99, CANCEL | [below](#cancel-and-proceed) |

### + and −

**+** takes the level *L* 1922 shows; adds `Repair_StepCost` to the allocated salvage and shows it in 1923; sets the item to `Repair_TargetForLevel`(*L*) with `Herc_SetStatus` (`00024c14`); fetches the system's lines again when `SystemRepair_ItemLines` is 0, rewrites the item's line with `Hangar_FormatSelectedStatusLine` and marks 1921 changed; takes 1 from *L* when it is above 0 and selects it in 1922; takes `Repair_StepCost` from `Career_Salvage` and from the share `+0x21` of the current record's current pilot `+0x12`, the HERC's pilot since `RepairMenu_RefreshHerc` selected it; and runs `SystemRepairMenu_Refresh`. An item at 100 costs 0 and stays at 100.

**−** undoes a level of what + did, and nothing below the item's condition when the window opened. With *o* the item's condition in the window's own block `+0x2e` (`HercStatus_GetMenuHandlersCopy`) and *c* its condition on the HERC, it returns at once when *c* is *o* or less. Otherwise, with *L* the level 1922 shows and *L*ₒ the level of *o*: when *L* is *L*ₒ, or *L* + 1 is *L*ₒ and *c* is below 100, the refund is `Repair_UnitValueCost`(1, *o*, …), what one level up from *o* cost; otherwise `Repair_UnitValueCost`(0, *c*, …), one level down from *c*. It takes the refund from the allocated salvage and adds it to `Career_Salvage` and to the current pilot's share; shows the allocated salvage; adds 1 to *L* when *c* is below 100 and selects it in 1922; sets the item to *o* when *L* is now *L*ₒ, and to `Repair_FloorForLevel`(*L*) otherwise; rewrites the item's line; and runs `SystemRepairMenu_Refresh`.

### CANCEL and PROCEED

**CANCEL** puts back the share of the current pilot, share + the salvage at opening − `Career_Salvage`, and `Career_Salvage` itself, and copies the window's block `+0x2e` over the selected HERC's; the target becomes 1900, so CANCEL goes back to REPAIR rather than to the bay. ES2's CANCEL undoes in the same way and stays on its screen ([repairing and cancelling](../../retail/shell/weapons-and-repair.md#repairing-and-cancelling)).

**PROCEED** goes to REPAIR, 1900, and does nothing first when `Career_Salvage` equals the salvage at opening. Otherwise it sets the selected item to `Repair_TargetForLevel` of the level 1922 shows and shows the bay again with `Hangar_ShowSlot`(slot). After a +, 1922 shows the level above the one + priced, so PROCEED raises the item one level further for nothing ([Open](#open)).

## PREFERENCES — screen 1910

**PREFERENCES chooses which HERCs the shell repairs on its own after a mission: REPAIR ALL leaves every repair to the player, REPAIR MINE repairs the squadmates' HERCs, AUTO REPAIR all four.** `dlg\menu1910.dlg`'s window PREFERENCES is (181, 2)–(316, 108), with the text buttons REPAIR ALL 1911, REPAIR MINE 1912 and AUTO REPAIR 1913 at (31, 43), (31, 57) and (31, 71), each to (117, *y* + 12), and three kind-16 controls 1914, 1915 and 1916 at (19, 44), (19, 58) and (19, 72), each to (28, *y* + 10), beside them. Screen 1910's case (`0003f5bf`) makes a `RepairPrefsAlert` (`0008202c`) the current menu. `Menu_BuildFromDlg` builds each kind-16 control with the word 1 when `Career_AutoRepairMode` (`0007fcf8`) is 0, 1 or 2 for 1914, 1915 or 1916 respectively, and 0 otherwise ([the kinds](../formats/dlg-menu-dialogs.md#the-kinds)). `RepairPrefsAlert_OnMessage` (`0003052f`), for 1911, 1912 or 1913, sets `Career_AutoRepairMode` to 0, 1 or 2, sets the three controls' value with their slot `+0x24` to 1 for the button's and 0 for the others, and makes the target 1900, back to REPAIR; it does nothing for any other target. So the window writes only 0, 1 and 2, the values `CareerRecord_AutoRepairSquad` reads as none, the squadmates and all four ([repairs](between-missions.md#repairs)); ES2 takes the same choice from `prefs.cfg` ([repair levels](../../retail/shell/armory.md#repair-levels)).

## SCRAP — screen 4444

**SCRAP asks first, giving the salvage the HERC will yield; its SCRAP turns the HERC into salvage and its weapons into stock or salvage, and REPAIR moves to the next HERC.** `dlg\menu4444.dlg`'s window WARNING! is (181, 2)–(316, 109), with the labels "This Herc will yield" at (31, 40) and "### tons of salvage." at (31, 51), whose `###` `Hangar_FormatScrapValue` (`0002a1aa`) fills with the frame's value in tons ([lost squadmates](between-missions.md#lost-squadmates)), CANCEL 1900 at (16, 88)–(66, 100) and SCRAP 100 with `Space` at (69, 88)–(119, 100). Screen 4444 is `ScrapAlert` (`00081e06`); its handler `ScrapAlert_OnMessage` (`0002f111`), for 100 only:

1. scraps the selected HERC with `Hangar_ScrapSelectedHerc` (`0002a046`), which for a finished HERC adds its frame's value and its worn weapons' to `Career_Salvage`, puts its other weapons back in the stock, takes it off its pilot and steps to the next HERC with `Hangar_NextHercSlot` ([lost squadmates](between-missions.md#lost-squadmates));
2. finds the pilot of the slot now selected, `CareerRecord_FindPilotBySlot`; calls `HangarBay_AssignScripts` (`000122e0`)() ([giving the scripts their bays](hangar.md#giving-the-scripts-their-bays)); wipes the draw page onto the visible one with `Screen_BlindsCopy`(8, visible, draw, 1); selects that pilot with `CareerRecord_SelectPilot`, keeping the selected slot when it is −1;
3. makes the target 1900.

CANCEL's 1900 goes back to REPAIR. ES2's scrap also takes the HERC's pilot off strength ([scrapping](../../retail/shell/armory.md#scrapping)); ES1's leaves the pilot on strength with no HERC.

## BUILD — screen 2000

**BUILD shows one chassis at a time: its picture, its type, mass, speed and weapon capacity, the salvage it needs and the salvage available. The arrows step through the chassis the career has made available; BUILD pays the price at once and starts the HERC in a free bay, where it takes one to three missions to finish. While it is being built the screen shows that HERC, the button reads SCRAP and gives the salvage allocated to it back.** The bay's BUILD opens it unless every slot is taken ([the bar's buttons](between-missions.md#the-bars-buttons)). `dlg\menu2000.dlg`'s window BUILD is (181, 2)–(316, 196):

| Control | Id | Rect | Key | Does |
|---|---|---|---|---|
| HERCS, icon buttons | 2003, 2004 | (15, 157)–(40, 167), (41, 157)–(66, 167) | | the previous or next available chassis |
| BUILD | 2001 | (67, 156)–(120, 168) | `Space` | builds, or scraps the HERC being built |
| DONE | 1710 | (48, 175)–(86, 187) | `Esc` | `CareerRecord_SaveCurrent`, then the bay after a mission |
| hotspot | 2005 | (14, 27)–(120, 148) | | the information panel, `FabricationMenu_DrawPanel` (`0002845c`) |

**Opening it.** Screen 2000's case (`0003f6f8`) calls `Hangar_SelectNext`(3), which gives the marked slot a free slot when it has none and selects it, and makes the menu with `FabricationMenu_Ctor` (`000307c9`)(0), class `FabricationMenu`, vtable `00080ece` (slot `+4` `FabricationMenu_Dtor` (`00030910`), `+8` `FabricationMenu_OnMessage` (`000309d7`)), a screen object of `0x26` bytes that loads `res32\blueherx.dba` into `Fabrication_ChassisArt` (`000ab20e`) with `Resource_LoadBitmapArray`; the destructor destroys the array with `GLBitmapArray_Dtor`(array, 3). `Menu_Open` then, with no marked HERC, makes BUILD inactive when `Career_Salvage` is below the shown chassis' price × 1000, compared unsigned; with one, it gives BUILD `eng\watext.bin` line `0xcd`, SCRAP, with `MTButton_SetTextFromTable`, and makes both arrows inactive. The shown chassis is `Fabrication_Chassis` (`000808a4`), 0 in the image, which the screen keeps between visits; the entry does not test it against what the career has made available.

**The panel.** `DUIWindow_Draw` calls `FabricationMenu_DrawPanel` (`0002845c`) with (the hotspot's rect, 2005) ([drawing a window](menus.md#drawing-a-window--duiwindow_draw-0001bfa3)). In the window's render context `000ab612`, alignment `+0x231` 0 unless said:

1. it fills the rect with color `0x1f` framed in `0x1f` (`Raster_FillFrameRectColours`, on a copy of the rect `Rect_CopyCtorMechManCopy` makes); with a marked HERC, the shown chassis becomes the selected HERC's chassis word, −1 for no HERC;
2. blits frame *chassis* of `res32\blueherx.dba` at the rect's corner + (1, 1);
3. draws, in `nm_green` (`000aacda`) for the labels and `nm_white` (`000aacee`) for the values ([the loads](../formats/styles-dat.md#the-loads)), the lines of `eng\watext.bin`: TYPE: (`0xc4`) at (75, 34) and the chassis' name (`0x1b` + chassis) at (79, 43); MASS: (`0xc5`) at (75, 52) and `%d TONS` of `Chassis_MassTable` (`000808a8`)[chassis] at (79, 61); SPEED: (`0xc6`) at (75, 70) and `%d KPH` of `Chassis_SpeedTable` (`000808c8`) at (79, 79); CAPACITY: (`0xc7`) at (75, 88) and `%d WEAPONS` of the mount counts at `000803c8` at (79, 97);
4. with a marked HERC: a box (16, 111)–(118, 117) filled and framed in color 4; UNDER CONSTRUCTION (`0xcc`) in `nm_red1` (`000aacde`) at (17, 117), centered (alignment 2) in a field 102 wide; % COMPLETE: (`0xc8`) at (17, 126) and `%d` of (*c* − `Hangar_MarkedMissionsLeft`) × 100 / *c* at (67, 126), *c* the chassis' word of `Chassis_BuildMissions` (`000808d8`);
5. at (17, 135) SALVAGE ALLOCATED: (`0xcb`) with a marked HERC and SALVAGE REQUIRED: (`0xc9`) without, and at (87, 135) `%3d TONS` of `Hangar_MarkedValue` (`0008089c`) or of the chassis' price `Chassis_PriceTable` (`000808b8`) in tons; SALVAGE AVAILABLE: (`0xca`) at (17, 144) and `%3d TONS` of `Career_Salvage` / 1000, unsigned, at (87, 144).

| Chassis | Name | Mass, tons | Speed, KPH | Mounts | Price, tons | Missions to build |
|---|---|---|---|---|---|---|
| 0 | ROADRUNNER | 18 | 84 | 3 | 50 | 1 |
| 1 | OUTLAW | 27 | 80 | 3 | 60 | 1 |
| 2 | RAPTOR | 36 | 76 | 4 | 80 | 2 |
| 3 | TOMAHAWK | 45 | 71 | 5 | 100 | 2 |
| 4 | PATRIOT | 50 | 64 | 6 | 120 | 2 |
| 5 | RHINO | 54 | 56 | 7 | 140 | 3 |
| 6 | SAMSON | 63 | 52 | 8 | 170 | 3 |
| 7 | COLOSSUS | 77 | 46 | 9 | 200 | 3 |

(`es2_peek.py ES1GO 000808a8 32 --words`; the names `HercWorks.Query bin watext 0x1b`–`0x22`; the mount counts are `Herc_Ctor`'s.)

**The buttons.** `FabricationMenu_OnMessage` (`000309d7`) takes `CareerRecord_Current` and looks the target up among the four ids of `FabricationMenu_CaseTable` (`00080ef4`), 2003, 2004, 2001 and 1710:

- **2003** takes 1 from `Fabrication_Chassis`, from 0 to 7, and **2004** adds 1 modulo 8, each until `Tech_IsAvailableMenuHandlersCopy`(record + `0xa8`, chassis, 0) answers non-zero, the chassis' available byte ([equipment availability](../formats/bst-battle-sets.md#equipment-availability)), with no stop when none is; then BUILD is made inactive when the price × 1000 is above `Career_Salvage`, compared unsigned, and active otherwise, and the hotspot's pressed byte (`DUIWindow_FindHotspotById`, the hotspot's byte at the window's `+0x19f` + `0x38` × index) is set, so `DUIWindow_UpdateHotspots` draws the panel again.
- **2001** with a marked HERC scraps it with `Hangar_ScrapSelectedHerc`, the selected slot being the marked one: `Career_Salvage` gets `Hangar_MarkedValue` × 1000 back, the slot is emptied, `HangarBay_AssignSlotScripts`(slot, 1) runs and the marked slot keeps its number ([lost squadmates](between-missions.md#lost-squadmates)); then `Screen_BlindsCopy`(8, visible, draw, 1), BUILD reads BUILD (`0xce`), and both arrows are made active. Without one it builds with `Hangar_BuildIntoSelected` (`00029d68`)(`Fabrication_Chassis`), BUILD reads SCRAP (`0xcd`), and both arrows are made inactive. Either way the panel is drawn again.
- **1710**, DONE, `CareerRecord_SaveCurrent`.

`Hangar_BuildIntoSelected` (`00029d68`)(chassis), for a selected slot that is not −1, sets `Hangar_MarkedMissionsLeft` to the chassis' word of `Chassis_BuildMissions` and `Hangar_MarkedValue` to its price in tons, makes a HERC of the chassis in the selected slot with `Herc_Ctor` (`00022f5e`)(0, chassis), every mount empty, takes the price × 1000 from `Career_Salvage`, builds its diagram with `Herc_BuildDiagram`(HERC, `Hangar_MarkedMissionsLeft`) unless `Shell_LowMemory` is set, and adds 1 to `Hangar_HercCount`. The selected slot is the marked one, which screen 2000's entry chose, so the new HERC is the marked HERC that `Hangar_AdvanceMarkedHerc` counts down after each career mission and brings to full condition ([the marked HERC](between-missions.md#the-marked-herc)). Its scrap value `Hangar_MarkedValue` falls with its progress *p* from the whole price to 80% of it, (*v* × 80 × *p* / 10000 + (100 − *p*) × *v*) / 100 tons.

**Against ES2.** ES2's BUILD buys into the bay the squad panel has selected, needs the pool net of the weapon queue to be more than the price, and the chassis arrives empty and unbuilt to be finished by a count in its record ([scrapping and building are gated on the bay](../../retail/shell/build-and-armory.md#scrapping-and-building-are-gated-on-the-bay), [buying a chassis](../../retail/shell/armory.md#buying-a-chassis--herc_order-00411019)). ES1 builds one HERC at a time, into the marked slot, whose countdown and value are the hangar's header words, not the HERC's; a pool equal to the price builds; the arrows and the entry show only the chassis, not a bay; and the build is undone on the same button for the value then allocated.

## SALVAGE — screen 2010

`dlg\menu2010.dlg`'s window SALVAGE is (181, 2)–(316, 196), with ACCEPT 100 with `Space` at (48, 153)–(86, 165) and CANCEL 2000 with `Esc` at (48, 176)–(86, 188). Screen 2010's case (`0003f71a`) makes a `SalvageAllocAlert` (`00082050`) the current menu, and its handler `SalvageAllocAlert_OnMessage` (`00030ed3`) answers the message's target and changes nothing. A byte search of the image for 2010 finds only `Shell_ScreenIds` and `Shell_HandleMessage`'s case table, and no `.DLG` control has the id 2010 ([Open](#open)).

## Functions

| ES1 | Address |
|---|---|
| `FabricationMenu_DrawPanel`, `Rect_CopyCtorMechManCopy` | `0002845c`, `00028a9b` |
| `Hangar_ReloadSelectedLoadout`, `HercList_RefitSelectedFromStream` | `00028ec9`, `00028f16` |
| `Hangar_ShowSelectedHerc`, `Hangar_LeaveSelectedHerc`, `Hangar_ClearSelectedMount` | `000294d8`, `000295e7`, `0002961c` |
| `Hangar_PrevHercSlot`, `Hangar_NextHercSlot`, `Hangar_NextUnpilotedHercSlot`, `Hangar_PrevUnpilotedHercSlot`, `Hangar_SelectMarkedSlot` | `000296c9`, `00029729`, `0002978c`, `000297cb`, `0002980a` |
| `Hangar_SelectNext`, `Hangar_SelectPrev` | `0002986f`, `00029946` |
| `Hangar_SelectMount`, `Hangar_NextMount`, `Hangar_PrevMount`, `Hangar_FitWeapon` | `00029a14`, `00029a52`, `00029a87`, `00029abc` |
| `Repair_AutoSelected`, `Hangar_BuildIntoSelected`, `Hangar_SelectedBuildProgress` | `00029bdd`, `00029d68`, `0002a2a1` |
| `RepairMenu_RefreshSalvage`, `RepairMenu_RefreshHerc`, `RepairMenu_OnMessage`, `ScrapAlert_OnMessage` | `0002ecc0`, `0002ee70`, `0002f009`, `0002f111` |
| `SystemRepairMenu_Ctor`, `SystemRepairMenu_Dtor`, `SystemRepairMenu_SelectItem`, `SystemRepairMenu_Refresh`, `SystemRepairMenu_OnMessage` | `0002f1aa`, `0002f460`, `0002f517`, `0002f680`, `0002f8d4` |
| `Hangar_FormatSelectedStatusLine`, `HercStatus_GetMenuHandlersCopy`, `RepairPrefsAlert_OnMessage` | `00030478`, `000304c6`, `0003052f` |
| `FabricationMenu_Ctor`, `FabricationMenu_Dtor`, `FabricationMenu_OnMessage` | `000307c9`, `00030910`, `000309d7` |
| `Tech_IsAvailableMenuHandlersCopy`, `SalvageAllocAlert_OnMessage`, `MenuHandlers_StaticInit`, `MenuHandlers_StaticExit` | `00030e83`, `00030ed3`, `00030ee9`, `00031367` |
| `HangarBay_AssignSlotScripts`, `Screen_BlindsCopy`, `Screen_BlindsClear` | `000120f9`, `0004bc0d`, `0004c053` |
| `WaText_NumberLine`, `Menu_NumberLines`, `Hangar_SelectedComponentLines` | `00037468`, `000374af`, `000377cd` |

ES1 and ES2 (`VSHELL`) share these, with ES1's differences above:

| ES1 | Address | ES2 |
|---|---|---|
| `Hangar_CopySelectedStatus` | `0002f3e9` | `HercList_CopySelectedStatus` (`00434eb7`) |
| `Repair_TargetForLevel`, `Repair_FloorForLevel` | `00038a30`, `00038a5c` | `00413838`, `00413860` |
| `Repair_LevelStepCost`, `Repair_UnitValueCost` | `00038a72`, `00038bb9` | `00413871`, `00413968` |

## Globals

| | Address |
|---|---|
| `Hangar_SlotCount`, `Hangar_HercCount`, `int16` | `00080890`, `00080892` |
| `Hangar_MarkedSlot`, `int16`: the slot of the HERC being built, −1 for none | `00080896` |
| `Hangar_MarkedMissionsLeft`, `int16`: the career missions it still needs | `00080898` |
| `Hangar_MarkedValue`, `int32`: its scrap value in tons | `0008089c` |
| `Hangar_Slots`, the slot array of HERC pointers | `000808a0` |
| `Fabrication_Chassis`, `int16`: BUILD's shown chassis | `000808a4` |
| `Chassis_MassTable`, `Chassis_PriceTable`, `Chassis_SpeedTable`, `Chassis_BuildMissions`: eight `int16` each | `000808a8`, `000808b8`, `000808c8`, `000808d8` |
| `SystemRepair_ItemLines`, the lines of SYSTEM REPAIR's list | `00080e10` |
| `Repair_StepCost`, `int16`: the cost of + on the selected item | `000ab212` |
| `Fabrication_ChassisArt`, `res32\blueherx.dba` | `000ab20e` |
| `RepairMenu_CaseTable`, `SystemRepairMenu_CaseTable`, `FabricationMenu_CaseTable` | `00080e20`, `00080e6c`, `00080ef4` |

## Open

- **Open:** a retail check under DOSBox-X of PROCEED after +: in a career after the first mission, REPAIR, then REPAIR on a HERC with damage; select an internal part whose STATUS reads MODERATE DAMAGE and press + once (STATUS reads LIGHT DAMAGE, ALLOCATED shows the cost); PROCEED, then REPAIR again: the code sets the part to NOMINAL's floor, 91, so STATUS reads NOMINAL.
- **Open:** whether SYSTEM REPAIR's weapon list prices a mount by its number rather than its weapon in retail: `Repair_UnitValueCost` reads `000ab332`[item], and which weapon each of the 18 values prices is open ([the shell's value tables](../formats/herc-catalogs.md#open)).
- **Open:** what opens screen 2010, SALVAGE: a byte search of `ES1GO` for the value 2010 found it only in `Shell_ScreenIds` (`0008181a`) and the case table (`00082100`), and `HercWorks.Query menu` lists no control with the id 2010.
- **Open:** no caller of `Hangar_ReloadSelectedLoadout` (`00028ec9`) found by `es2_xref.py`.
- **Open:** what the kind-16 controls of PREFERENCES draw for their word 1 and 0 (`0001eaef`, [the kinds](../formats/dlg-menu-dialogs.md#open)).
- **Open:** whether REPAIR can open with only the marked HERC in the hangar: `Hangar_NextHercSlot` then comes back to the slot it started from, and SYSTEM REPAIR's `Hangar_CopySelectedStatus` copies 66 bytes from the address its answer, a slot number, names.
- **Open:** a retail check of BUILD: open BUILD from the bay after a mission; the panel reads TYPE: ROADRUNNER, MASS: 18 TONS, SPEED: 84 KPH, CAPACITY: 3 WEAPONS, SALVAGE REQUIRED: 50 TONS; BUILD, then the panel reads UNDER CONSTRUCTION, % COMPLETE: 0 and SALVAGE ALLOCATED: 50 TONS, and the button reads SCRAP.
