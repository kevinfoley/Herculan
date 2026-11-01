# GO.EXE — the hangar: the service bay's display and CREW

**Between missions the service bay shows the hangar: an animated picture of the bays, with the selected bay's HERC drawn into it — its body, its damage and its weapons. CREW (screen 1610) shows the squad's four pilots in columns, each with the HERC it pilots; a column is picked, its pilot's HERC is changed by stepping through the hangar's HERCs, and ACCEPT keeps the changes or CANCEL drops them, both returning to the bay.** The bay's strip of buttons is [the career's](between-missions.md#the-service-bays-bar); ARMING is [`arming.md`](arming.md), and REPAIR, BUILD and scrapping are [`repair-and-build.md`](repair-and-build.md). How the HERC's picture is put together is [the HERC diagrams](../formats/herc-diagrams.md).

Addresses are `ES1GO` image addresses ([the X-32 executable](../formats/x32-executable.md#the-image)). The functions here remove their own arguments, so argument lists are in stack order ([calling conventions](../formats/compiler.md#calling-conventions)). The current career record is `CareerRecord_Current` (`00014ef4`)'s, and a pilot entry is `0x25` bytes from the record's `+0x14` ([pilot entries](../formats/bst-battle-sets.md#pilot-entries)).

## Where the code lies

- **CREW**, `0002c743` to `0002d326`, lies inside [the menu handlers' file](main-menu.md#where-the-object-files-start-and-end): `CrewAssignMenu_Ctor` (`0002c743`) follows the debriefing's last `RET 8` at `0002c740` ([the debriefing](debriefing.md)), and `CrewAssignMenu_OnMessage`'s case bodies end with the `RET 8` at `0002d324`, right before `ServiceBayMenu_OnMessage` (`0002d327`). The class's name `CrewAssignMenu` stands at `00080c8c`, before its vtable `00080c9e`, which holds the destructor at `+4` and the handler at `+8`; the handler's case table `00080cd0` follows. `es1_boilerplate.py bounds` over `0002c643`–`0002d426` finds no object-file boundary evidence.
- **The bay's scripts**, `TtmEntry_AssignSlot` (`00012047`) to `ShellBackdrop_RunAllScripts` (`000126c9`), end their object file: the `RET` at `00012700` is followed by three zero bytes and `Main_LoadBnd` (`00012704`). The file also holds `TtmLists_Load` (`00011dfc`) and `ShellBackdrop_OpenGroupTtms` (`00011f19`) ([the backdrop](menus.md#the-backdrop)); where it starts is [Open](#open).
- **The HERC's picture** is drawn by the two object files [the HERC diagrams' code](../formats/herc-diagrams.md#where-the-code-lies) places, and **`Hangar_ShowSlot`** (`00029651`) lies among the hangar's functions ([`repair-and-build.md`](repair-and-build.md)).

## The service bay's display

**The bay is backdrop 4000's picture with the group's scripts playing over it: some always, and some only while the bay they stand for is the selected one. Over them the selected bay's HERC is drawn, built from sprites.** The group's scripts are the ones `ShellBackdrop_OpenGroupTtms` opens ([the backdrop](menus.md#the-backdrop)); each script's 3-byte entry of `ShellBackdrop_GroupTtms` holds its handle in the byte `+0` and a word `+1`, −99 for a script that shows nothing yet, 99 for one that always plays, or the hangar slot it stands for.

### Giving the scripts their bays

`HangarBay_AssignScripts` (`000122e0`)(), called by `ShellBackdrop_Show` for group 4000 ([the backdrop](menus.md#the-backdrop)), by CREW's ACCEPT ([the buttons](#the-buttons)) and by `ScrapAlert`'s handler `ScrapAlert_OnMessage` (`0002f111`) (`es2_xref.py` finds those three calls):

1. frees every entry with `TtmEntry_FreeAll` (`0001208e`), which sets each entry's word `+1` to −99, and takes the slot `+8` of the player's entry `+0xe` of the current record;
2. gives entries 0 to 3 the value 99, and entry 4 when `Mission_Kind` (`000aad5a`) is 4, in any `Shell_Mode` but 0 only while the byte `+1` of the record the roster's word `+7` names (`0009b23c` + `0x1f7` × the word `0009b21f`, read directly rather than through `CareerRecord_Current`) is also set ([BRIEFING](between-missions.md#briefing));
3. for each of the hangar's HERCs (the count `Hangar_HercCount` (`00080892`)) steps `Hangar_SelectedSlot` (`00080894`) to the next slot that holds a HERC and is not the marked one, with `Hangar_NextHercSlot` (`00029729`), and calls `HangarBay_AssignSlotScripts` (`000120f9`)(slot, 0) for the player's slot and `000120f9`(slot, 2) for any other, which give the slot its scripts ([`repair-and-build.md`](repair-and-build.md));
4. makes the player's entry the current pilot with `CareerRecord_SelectPilot` (`00015ad4`)(record, its `+0xe`), which puts `Hangar_SelectedSlot` back on the player's slot, and runs the scripts with `ShellBackdrop_RunScripts`.

`TtmEntry_AssignSlot` (`00012047`)(slot, entry) stores the slot in the entry's word `+1` when that is −99 and answers 1 in `AL`; otherwise it answers 0 and changes nothing. It does not test the entry against the group's count `ShellBackdrop_GroupTtmCount`, and `HangarBay_AssignSlotScripts` asks for entries 6, 7, 8, 9 + slot (up to 16 for slot 7) and the entries of the words `0007e700`, 22, 17, 20, 23, 20, 21, 18 and 24 for chassis 0 to 7 ([the hangar's selection](repair-and-build.md#the-hangars-selection)). `TtmEntry_FreeAll` and `TtmEntry_FreeSlot` (`000120ba`)(slot), which sets −99 in every entry holding the slot, go only up to the count.

### Running the scripts

`ShellBackdrop_RunScripts` (`00012467`)() sets each of the group's scripts playing or stopped, mode 0 or 2 with `Ttm_SetMode` ([a run](../formats/ttm-scripts.md#a-run)), by the group `Shell_BackdropId` / 1000 × 1000, and runs each once with `Ttm_RunScript`:

| Group | Playing | Stopped |
|---|---|---|
| 3000 | of the first eight scripts, the pair 2*k*, 2*k* + 1 where *k* is the current career record's campaign word (`CareerRecord_CampaignWord`) in `Shell_Mode` 1, and 0 in mode 0 | the other pairs |
| 5000 | script `Shell_BackdropId` mod 1000 | the others |
| 4000 | each script whose word `+1` is `Hangar_SelectedSlot` or 99 | the others |
| any other | every script, through `ShellBackdrop_PlayAllScripts` | none |

For group 3000 it takes the pairs *k* = 0 to 3 in turn, setting both scripts' modes and then running both, and reads the eight entries with no test against the count; the other groups go up to the count. `ShellBackdrop_PlayAllScripts` (`0001240b`)() sets every script playing and runs each once; `Shell_DrawScreen` calls it right after drawing backdrop 4000 ([drawing a screen](menus.md#drawing-a-screen)). `ShellBackdrop_RunAllScripts` (`000126c9`)() runs each once without setting a mode ([Open](#open)).

### Showing a bay

**`Hangar_ShowSlot` (`00029651`)(slot) takes the selected bay's HERC out of the picture and shows another bay.** `Shell_DrawScreen` calls it with the player's slot, the slot `+8` of the player's entry `+0xe` of `CareerRecord_Current`'s record, and then makes the player's entry the current pilot with `CareerRecord_SelectPilot`(record, `+0xe`), on two paths ([drawing a screen](menus.md#drawing-a-screen)):

- drawing a new backdrop 4000, right after `ShellBackdrop_PlayAllScripts`, with no wipe;
- on screen 1700 or 1710 with the backdrop unchanged, between two wipes, `Screen_BlindsClear`(8, 0, 1) before and `Screen_BlindsCopy`(8, the visible page, the draw page, 1) after ([the wipes](repair-and-build.md#the-hangars-selection)), each run only while the record's current pilot `+0x12` is not the player's entry `+0xe` and the message box is not up (the byte `0009b013` clear).

So the bay opens on the player's HERC, and coming back to it from CREW with a squadmate current wipes the picture over to the player's. `es2_xref.py` finds its other two calls in `RepairMenu_OnMessage` (`0002f009`) and `SystemRepairMenu_OnMessage` (`0002f8d4`) ([`repair-and-build.md`](repair-and-build.md)). It:

1. takes the selected slot's HERC out of the picture with `Hangar_LeaveSelectedHerc` (`000295e7`), which calls `Herc_EraseFromBay` on it;
2. sets the marked slot `Hangar_MarkedSlot` (`00080896`) to −1 when it is the selected slot and that slot holds no HERC; the slot is read from the array `Hangar_Slots` (`000808a0`) points to with no test for −1, so with both slots −1 it reads the dword before the array;
3. for slot −1 takes the first empty slot (`Hangar_FindFreeSlot`, `0002983a`), and when there is none steps `Hangar_SelectedSlot` with `Hangar_NextUnpilotedHercSlot` (`0002978c`) to a slot holding a HERC that no pilot entry holds, or back to where it began, and takes that;
4. stores the slot in `Hangar_SelectedSlot` and calls `Hangar_ShowSelectedHerc` (`000294d8`), which moves scripts 0, 1 and 2 to the slot's pages, draws the slot's HERC with `Herc_DrawInBay` and runs the scripts with `ShellBackdrop_RunScripts`; then `Hangar_SelectMount` (`00029a14`)(−1), which takes the mount highlight off the HERC with `Herc_HighlightMount`(HERC, −1) ([the HERC diagrams](../formats/herc-diagrams.md#the-mount-highlight)).

So a player with no HERC is shown an empty bay.

### Drawing the HERC into its bay

`Herc_DrawInBay` (`000241f9`)(HERC, remaining) builds the HERC's diagram first with `Herc_BuildDiagram`(HERC, remaining) when the byte `Shell_LowMemory` (`0007fd09`) is set, then draws, in this order: the body (`Sprite_Draw` of `+0x72`), the damage marks (`Herc_DrawDamageMarks`), one run of script 1, the mounts (`Herc_DrawMounts`), and one run each of scripts 0 and 2 ([the HERC diagrams](../formats/herc-diagrams.md#drawing-the-diagram)). The scripts are the handles of entries 0, 1 and 2, read through the dword `ShellBackdrop_GroupTtms` with no test that it is set, as `Hangar_ShowSelectedHerc` reads them. `Hangar_ShowSelectedHerc` (`000294d8`) passes the marked slot's word `Hangar_MarkedMissionsLeft` (`00080898`) as *remaining* for the marked HERC and 0 for any other. `Herc_EraseFromBay` (`000241cd`)(HERC) erases the mounts (`Herc_EraseMounts`) and the body (`Sprite_Erase` of `+0x72`) and, with `Shell_LowMemory` set, frees the diagram with `Herc_FreeDiagram`.

## CREW

**CREW ASSIGNMENT fills the screen with four columns, one per pilot entry of the career record: the player's insignia or a squadmate's portrait, the pilot's name, skill rating, HERC and its condition, and a picture of the HERC with its bay's number. A column is picked by clicking its HERC or with HERC; the arrows, a click on the picked column's HERC and NONE change the HERC its pilot is given.** Screen 1610's case (`0003efac`) makes the menu with `CrewAssignMenu_Ctor`(0) into `Shell_CurrentMenu` (`000817c8`) and loads `res32\regn1610.dpl` as the palette with `ShellBackdrop_LoadPalette`(`Shell_Backdrop` (`000a09a0`), 1610) ([the screens](menus.md#the-screens)); the menu is `0x40` bytes and on backdrop 4000, so the change of screen shows no new backdrop, and screen 1700's case loads 4000's palette again on the way out ([leaving it](#leaving-it)).

### The screen

`dlg\menu1610.dlg` lays out a menu window titled CREW ASSIGNMENT at (0, 0)–(320, 200), whose labels take the font `cstats_2` ([the window](menu-controls.md#the-window)) (`HercWorks.Query menu 1610`). Columns are 62 pixels apart; *n* is the column, 0 to 3, the pilot entry it shows.

| Ids | Kind | Rect | Shows |
|---|---|---|---|
| 1611 | kind 16 | (67, 23)–(124, 76) | `res32\insignia.dba` frame 0, over column 0 |
| 1612–1614 | hotspot | (129 + 62(*n* − 1), 23)–(186 + 62(*n* − 1), 76) | the portrait of entry *n*, 1 to 3 |
| 1619–1622 | hotspot | (68 + 62*n*, 78)–(123 + 62*n*, 109), but 1622's right edge is 310 | entry *n*'s four lines |
| 1615–1618 | kind 16 | (67 + 62*n*, 112)–(125 + 62*n*, 195) | `res32\mechs.dba`, the frame of entry *n*'s HERC's chassis |
| 1615–1618 | hotspot | (68 + 62*n*, 113)–(124 + 62*n*, 194) | the bay number over it |
| HERC, 1624 | text button, `Space` | (12, 115)–(54, 127) | |
| REASSIGN | label, centered | (14, 130)–(56, 138) | |
| 2003, 2004 | icon buttons, `←` (`0x4b`) and `→` (`0x4d`) | (12, 138)–(33, 150), (33, 138)–(54, 150) | |
| NONE, 1623 | text button, `Esc` | (12, 150)–(54, 162) | |
| ACCEPT, 100 | text button, `Space` | (12, 165)–(54, 177) | |
| CANCEL, 1700 | text button, `Esc` | (12, 180)–(54, 192) | |
| | labels | (5, 77)–(67, 87), 8 lower each | RANK/NAME:, SKILL RATING:, CURRENT HERC:, HERC STATUS: |

The build gives tile 1611 the picture 0 and each of 1615–1618 the chassis of the HERC in the slot `+8` of pilot entry id − 1615 of the record the roster's word `+7` names (`0009b218` + `0x23` + `0x1f7` × the word `0009b21f`), read directly rather than through `CareerRecord_Current`, with no test of the slot against the hangar's slot count: −1 for a slot of −1 or a slot holding no HERC ([the kinds](../formats/dlg-menu-dialogs.md#the-kinds)).

The hotspots draw through their handlers ([the build](menus.md#opening-a-menu--menu_open-0003969e)), whenever the window is drawn and when their byte `+0` is set before an update. Controls draw before hotspots, so a tile is drawn under its bay number.

- **A portrait**, `PilotPortraitHotspot_Draw` (`00017a19`), for entry *n* = id − 1611 of the record the roster's word `+7` names, read directly: `Bitmap_Blit`, flags 0, of the picture (the entry's word `+0`) of `CrewAssignMenu_Portraits` (`0007ebcc`), `res32\pilots.dba`, from the bank's array `+8` with no bounds test, at the rect's corner plus (1, 1), and over it, while that record's byte `+1` is set and the entry's byte `+0xa` is 0, picture 0 of `CrewAssignMenu_Mesh` (`0007ebd0`), `res32\mesh.dba`, at the corner.
- **A column's lines**, `PilotLinesHotspot_Draw` (`00017b4a`), for entry *n* = id − 1619 of `CareerRecord_Current`'s record: sets the render context's alignment (`000ab612` + `0x231`) to 0, fills the rect in colour `0x1f` with a `0x1f` frame (`Raster_FillFrameRectColours`), and writes four lines from (left + 2, top + 7), 8 apart, in `nm_white` (`000aacee`), or `nm_ylw` (`000aacf6`) for the record's current pilot `+0x12` ([the shell's fonts](../formats/styles-dat.md#the-loads)): the name, which for the player's entry `+0xe` is the name the roster's dword `+1` (`0009b219`) points at and otherwise `PilotEntry_Name`(the record the roster's word `+7` names, *n*); the skill rating, `eng\watext.bin` line `0xa1` + the entry's rank `+0xd`, ROOKIE to ELITE; and for a slot `+8` of −1 line `0xa6` UNASSIGNED and `---` (`0007ed6e`), otherwise line `0x1b` + the chassis of the slot's HERC and `%d%%` (`0007ed69`) of `Herc_OverallCondition` (`00024b6a`), for an empty slot line `0x1a` and 0.
- **A bay number**, `BayNumberHotspot_Draw` (`00017d9f`), for entry *n* = id − 1615 of `CareerRecord_Current`'s record with a slot `+8` of 0 or more: sets the alignment to 0 and writes `%d` (`0007ed72`) of the slot + 1 in `big_red` (`000aacfa`) at the rect's corner plus (3, 8), drawing nothing under it.

Both kind-16 pictures are picture tiles ([the picture tile](menu-controls.md#the-picture-tile)). `Menu_LoadControlBitmaps` (`00037827`)(id) answers their banks: `res32\insignia.dba` for 1611 in the dword `000813d4` and `res32\mechs.dba` for 1615–1618 in `000813dc`, each loaded only while the dword is 0. Each tile's destructor frees its bank through `Shell_FreeBitmapArraySlot` (`00037924`), which tests `000813d4`, `000813d8` and `000813dc` in that order and clears the one that holds it: the first of the four HERC tiles destroyed frees `mechs.dba` and the other three find `000813dc` cleared. So closing CREW frees both banks, and each opening loads them again. A press on a tile never steps its picture, and CREW's handler reads only the message's target, not the value a release posts.

### Opening it

`CrewAssignMenu_Ctor` (`0002c743`)(menu), with a null menu allocating `0x40` bytes, builds the menu's base and region, stores the vtable `00080c9e`, stops group script 3 (`Ttm_SetMode`(2, its handle, the byte at `ShellBackdrop_GroupTtms` + 9)), loads `res32\pilots.dba` into `CrewAssignMenu_Portraits` and `res32\mesh.dba` into `CrewAssignMenu_Mesh`, and sets the menu's fields:

| Offset | Holds | At opening |
|---|---|---|
| `+0x26` | the career record | `CareerRecord_Current` |
| `+0x2a` | the picked column's tile id, 1615 to 1618 | 1615 |
| `+0x2c` | the bay selected when CREW opened | `Hangar_SelectedSlot` |
| `+0x2e` | the slot the last assignment gave | −1 |
| `+0x30` | the slot the picked column's pilot held when the column was picked | −1 |
| `+0x32` | four words, each entry's slot `+8` | as the entries hold them |
| `+0x3a`, `+0x3c` | the record's current pilot `+0x12` | the same in both |
| `+0x3e` | the entry the last assignment took its HERC from, or −1 | −1 |

`Menu_Open`'s case for 1610 then frames tile 1615 in colour 4 and makes NONE inactive ([opening a menu](menus.md#opening-a-menu--menu_open-0003969e)), so CREW opens on column 0, without making entry 0 the current pilot.

### The buttons

`CrewAssignMenu_OnMessage` (`0002d19e`)(menu, message) looks the message's target `+6` up among the eleven ids of the `REPNE SCASD` table at `00080cd0`, its cases at `00080cfc`, in bytes Ghidra left inside the function, and does nothing for any other id. Of the targets only 1700 is a screen ([changing screens](menus.md#changing-screens--shell_handlemessage-0003c7d8)).

| Control | Id | Case | Does |
|---|---|---|---|
| `←` | 2003 | `0002d1c4` | `CrewAssignMenu_UndoSwap`; `Hangar_PrevHercSlot` (`000296c9`) steps `Hangar_SelectedSlot` back to the previous slot holding a HERC other than the marked one; `CrewAssignMenu_AssignSelectedSlot` |
| `→` | 2004 | `0002d1de` | the same, stepping forward with `Hangar_NextHercSlot` (`00029729`) |
| NONE | 1623 | `0002d1f8` | `CrewAssignMenu_UndoSwap`; `Hangar_SelectedSlot` and `+0x30` set to −1; `CrewAssignMenu_AssignSelectedSlot` |
| CANCEL | 1700 | `0002d220` | `Hangar_SelectedSlot` back to `+0x2c`; `CareerRecord_SelectPilot`(record, the dword at `+0x3a`, `+0x3c` in its high half); each entry's slot `+8` back to its word of `+0x32`; the target stays 1700, the bay |
| ACCEPT | 100 | `0002d284` | the target becomes 1700; `CareerRecord_SaveCurrent` (`00014f28`) saves the career; `CareerRecord_SelectPilot`(record, the player's entry `+0xe`); `HangarBay_AssignScripts`; `Hangar_SelectedSlot` back to `+0x2c` |
| a HERC tile | 1615–1618 | `0002d2bb` | for the picked column's tile (the target, sign-extended, equal to `+0x2a`), as `→`; for another, `CrewAssignMenu_SelectColumn`(menu, id) |
| HERC | 1624 | `0002d2f7` | `CrewAssignMenu_SelectColumn` with the next column's tile, 1618 going round to 1615 |
| the insignia | 1611 | `0002d320` | nothing |

CANCEL's `CareerRecord_SelectPilot` runs before the slots are put back, so it selects the slot the pilot holds after the changes; the bay's draw selects the player's slot again ([showing a bay](#showing-a-bay)).

**The keys.** `dlg\menu1610.dlg` lists HERC (record 1) before ACCEPT (record 6), both with `Space`, and NONE (record 5) before CANCEL (record 7), both with `Esc`. `DUIWindow_FindControlByKey` (`0001bd41`) takes the first control that is not inactive with the key, so `Space` always presses HERC and never ACCEPT, and `Esc` presses NONE while it is active and CANCEL while it is inactive: at the opening and while column 0 is picked.

### Assigning a HERC

**Changing the picked column's HERC gives its pilot the bay `Hangar_SelectedSlot` has stepped to. When another pilot holds that HERC, the two swap: the other pilot takes the HERC the picked pilot had when the column was picked, and its tile is framed in colour 10. Stepping on hands the HERC back before the next; picking another column keeps the swap.**

`CrewAssignMenu_SelectColumn` (`0002cf16`)(menu, id):

1. unframes the old column's tile (slot `+0x18`) and sets the redraw bytes of its tile's and its lines' hotspots (id and id + 4);
2. with an entry in `+0x3e`, unframes that entry's tile, sets its tile hotspot's redraw byte, and sets `+0x3e` to −1;
3. makes NONE inactive for tile 1615, column 0, and active for any other (its `+0x19` 2 or 0, *changed* `+0x1f` 1);
4. frames the new tile in colour 4 (slot `+0x14`), sets the redraw bytes of its two hotspots, makes entry id − 1615 the current pilot with `CareerRecord_SelectPilot`, which selects its slot, and stores the id in `+0x2a` and `Hangar_SelectedSlot` in `+0x30`.

`CrewAssignMenu_AssignSelectedSlot` (`0002cc21`)(menu):

1. when `Hangar_SelectedSlot` is the slot `+0x2e` the last assignment gave, sets it to −1, so stepping that comes back to the same HERC gives no HERC; the store is a word (`0002cc46`), and the decompile's dword store, which would also write `Hangar_MarkedSlot`, is Ghidra's;
2. stores the slot in `+0x2e` and in `+0x3e` the entry `CareerRecord_FindPilotBySlot` finds holding it;
3. when there is one, gives that entry the slot `+0x30`, frames its tile in colour 10, sets its tile to the chassis of `+0x30`'s HERC (slot `+0x24`, −1 for none) and sets its two hotspots' redraw bytes;
4. sets the tile of the current pilot `+0x12` to the chassis of the slot's HERC, gives that entry the slot, sets its two hotspots' redraw bytes, and calls `Roster_ForEachPilotNoOp`(`Career_Roster`).

When the entry step 2 finds is the current pilot itself, as in a hangar of one HERC, where the step comes back to the pilot's own slot, its own tile is framed in colour 10 and it is given `+0x30` and then the slot again. It finds the tiles with `DUIWindow_FindEnabledControlById` and the hotspots with `DUIWindow_FindHotspotById` and uses the index they answer with no test for −1.

`CrewAssignMenu_UndoSwap` (`0002ca5a`)(menu), with an entry in `+0x3e`, unframes that entry's tile; when the picked column is the current pilot it gives that entry back the slot the current pilot now holds, sets its tile to that HERC's chassis and sets its two hotspots' redraw bytes. It leaves `+0x3e` for `CrewAssignMenu_AssignSelectedSlot` to set.

The column's pilot is the current pilot only after a column has been picked: until then `+0x2a` is 1615 and the current pilot is whatever entry the bay left current, the player's ([showing a bay](#showing-a-bay)), so the arrows and NONE change the player's HERC, and `+0x30` is −1, so a squadmate holding the HERC stepped to is left with none.

### Leaving it

`CrewAssignMenu_Dtor` (`0002c952`)(menu, flags) stores the vtable, destroys the two banks with `GLBitmapArray_Dtor`(bank, 3) and clears `CrewAssignMenu_Portraits` and `CrewAssignMenu_Mesh`, sets group script 3 playing again (`Ttm_SetMode`(0, its handle)), stores the base classes' vtables, and frees the menu when bit 0 of *flags* is set. Screen 1700's case then loads backdrop 4000's palette again ([the service bay's bar](between-missions.md#the-service-bays-bar)).

### Against ES2's crew screen

`es2_naming.py match` over `0002c743`–`0002d326` against `VSHELL` and `DBSIM` finds no candidate (the best scores 0.50), and the screens differ in what they do. ES2's [crew screen](../../retail/shell/squad-and-crew.md#the-crew-screen) assigns the bay a roster click picks, at once, and leaves the HERC's old pilot with none; ES1's steps through the hangar's HERCs for the picked pilot, swaps a HERC another pilot holds, and keeps the changes only on ACCEPT. ES2's bay picture is a set of sprites on a panel ([the bay picture](../../retail/shell/squad-and-crew.md#the-bay-picture)); ES1's is the backdrop's scripts with the HERC drawn into them.

## Functions

| ES1 | Address |
|---|---|
| `CrewAssignMenu_Ctor`, `CrewAssignMenu_Dtor`, `CrewAssignMenu_OnMessage` | `0002c743`, `0002c952`, `0002d19e` |
| `CrewAssignMenu_SelectColumn`, `CrewAssignMenu_AssignSelectedSlot`, `CrewAssignMenu_UndoSwap` | `0002cf16`, `0002cc21`, `0002ca5a` |
| `HangarBay_AssignScripts`, `TtmEntry_AssignSlot`, `TtmEntry_FreeAll`, `TtmEntry_FreeSlot` | `000122e0`, `00012047`, `0001208e`, `000120ba` |
| `ShellBackdrop_RunScripts`, `ShellBackdrop_PlayAllScripts`, `ShellBackdrop_RunAllScripts` | `00012467`, `0001240b`, `000126c9` |
| `Hangar_ShowSlot`, `Herc_DrawInBay`, `Herc_EraseFromBay` | `00029651`, `000241f9`, `000241cd` |
| `PilotLinesHotspot_Draw`, `BayNumberHotspot_Draw` | `00017b4a`, `00017d9f` |

## Globals

| | Address |
|---|---|
| `CrewAssignMenu_Portraits`, `CrewAssignMenu_Mesh`: `res32\pilots.dba` and `res32\mesh.dba` from CREW's opening to its closing | `0007ebcc`, `0007ebd0` |
| `CrewAssignMenu_OnMessage`'s case table, eleven ids and their targets | `00080cd0` |

## Open

- **Open:** where the object file that `TtmLists_Load` (`00011dfc`) and the bay's scripts share starts: `es1_boilerplate.py bounds` over `00011400`–`00011f20` finds zero fill after the `RET 8` at `000119da`, up to `000119e0`.
- **Open:** what each of group 4000's scripts shows (the list of `cfg\tm_files.dat` [the animation lists](../formats/shell-file-lists.md#the-animation-lists) gives the group), and so what scripts 0 to 4, entry 6, entries 7 and 8 and entries 9 + slot, which `HangarBay_AssignSlotScripts` (`000120f9`) hands out, draw in the bay.
- **Open:** what reads `ShellBackdrop_RunAllScripts` (`000126c9`): `es2_xref.py` finds no branch to it and no stored pointer; and what reads the menu's `+0x3c`, which none of CREW's six functions reads but as the high half of the dword CANCEL passes to `CareerRecord_SelectPilot`.
- **Open:** a retail check under DOSBox-X: from the bay before a mission, CREW shows four columns with the player's insignia over the first; `→` changes the first column's HERC picture and bay number and CURRENT HERC; picking the second column with a click on its HERC, then stepping to the HERC the player holds, frames the first column's HERC in another colour and gives it the second pilot's old HERC; CANCEL returns to the bay with nothing changed, and ACCEPT with the changes kept; `Space` steps the picked column on, and `Esc` with the first column picked returns to the bay.
