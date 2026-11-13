# GO.EXE — ARMING

**ARMING, from the service bay before a career mission or from a single mission's war map, opens ARMAMENT: a window down the right of the hangar's picture of the selected HERC. It names the HERC's chassis and its pilot, shows a weapon's picture with its type, range and size, power or missile guidance, and lists the weapons the player can fit, each with its count in stock. POINTS arrows, or a click on a mount in the HERC's picture, choose a mount; with a mount chosen, picking a weapon in the list fits it, the mount's old weapon going back into stock and one of the new weapon coming out. MISSILE TYPE turns the list into the four missile guidance kinds for a missile rack, HERCS arrows move to another HERC, and DONE goes back.** The window is a `.DLG` menu, built, opened, drawn and left as every menu is ([the menus](menus.md)); this page holds what ARMING adds. The bay's picture, its HERC diagram and its mount pictures are [the hangar's](hangar.md); the stock is [`inventor.cpp`'s](../formats/car-hercs-and-stock.md#the-weapon-stock).

Addresses are `ES1GO` image addresses ([the X-32 executable](../formats/x32-executable.md#the-image)). The functions here remove their own arguments, so argument lists are in stack order ([calling conventions](../formats/compiler.md#calling-conventions)). ES2's weapons screen does the same job ([the weapons screen](../../retail/shell/weapons-and-repair.md#the-weapons-screen)), built in code with a row per catalog weapon. `es2_naming.py match` against `DBSIM` and `VSHELL` finds no candidate for any function here; the functions that play the same part are `Arming_Enter` (`VSHELL 0043f548`) for the screen's case, `Arming_SelectRow` (`VSHELL 0043f71c`) for `Arming_SelectWeapon`, `Herc_FitMount` (`VSHELL 004114ec`) for the fit `Arming_SelectWeapon` calls, `Arming_ShowGuidance` (`VSHELL 0043fd69`) for `Arming_SelectMissileType`, and `Arming_NextHardpoint` and `Arming_PreviousHardpoint` (`VSHELL 0043dd09`, `0043dd49`) for the POINTS arrows.

## Where the object files start and end

**`inventor.cpp`** runs from `00027c4c` to `0002845b`. The code before it is a static initializer, `00027be7` (the static constructor table's entry `0009a25c`), whose `RET` at `00027c49` is followed by two zero bytes; `Inventory_SaveFile` (`00027f1b`) asserts with the path `src\inventor.cpp` (`0008085c`, pushed at `00027f51`); and `Tech_IsAvailable` (`00028409`) ends with a `RET 0xc` at `00028456`, followed by three zero bytes and the code at `0002845c`. `es1_boilerplate.py bounds` over `00027b00`–`00028560` finds no destructor copy and no static initializer of its own between those ([where object files meet](../formats/compiler.md#where-object-files-meet-in-the-code)). It holds the stock's functions ([the weapon stock](../formats/car-hercs-and-stock.md#the-weapon-stock)) and `Tech_IsAvailable`.

**ARMING's code, `0002d5bf` to `0002ecbf`, lies inside the menu handlers' file** ([where it starts and ends](main-menu.md#where-the-object-files-start-and-end)): `ServiceBayMenu_OnMessage`'s `RET 8` at `0002d5bc` runs straight into `Arming_DrawWeaponInfo` (`0002d5bf`), and the code at `0002ecc0` follows `ArmingMenu_OnMessage`'s `RET 8` at `0002ecbd`. `es1_boilerplate.py bounds` over `0002d400`–`0002edb9` finds no object-file boundary evidence. `Rect_CopyCtorMenuHandlersCopy` (`0002daa1`) is the file's copy of `Rect_CopyCtor` (`0004b398`), byte for byte its code, relocated operands aside. The menu's class, `ArmingMenu`, and its vtable `00081fe6` lie with the other menus' (`ArmingMenu_Dtor` (`000405ae`) at `+4`, `ArmingMenu_OnMessage` (`0002e54c`) at `+8`), and the three weapon-list helpers from `000370ec` to `0003737f` lie with `Menu_ListLines` (`0003750d`), followed by `Tech_IsAvailableListCopy` (`00037383`), a second copy of `Tech_IsAvailable` (`00028409`) that `es2_naming.py diff` finds identical once relocated operands are masked.

## The window

`dlg\menu1800.dlg` and `dlg\menu1820.dlg` lay out the same window, titled ARMAMENT, id 1800 in both, at (181, 2)–(316, 196) on the screen; 1820 lacks only the animation (`HercWorks.Query menu 1800 1820`). `MTWindow_Ctor` loads `res32\menu1800.dba` for both and gives both the label font `cstats_2` ([the window](menu-controls.md#the-window)). Rects are in the window:

| Id | Kind | Rect | Text, key | What it does |
|---|---|---|---|---|
| | label | (17, 24)–(42, 32) | HERC | |
| | label | (17, 33)–(42, 41) | PILOT | |
| 1903 | list box, style 1 | (44, 24)–(123, 33) | | the chassis' name, a one-line readout |
| 1904 | list box, style 1 | (44, 34)–(123, 43) | | the pilot's name, a one-line readout |
| 1803 | kind 17, 1800 only | (11, 44)–(123, 108) | | the weapon's animation ([the animation](#the-animation)) |
| 1803 | hotspot | (12, 45)–(122, 107) | | `Arming_DrawWeaponInfo` (`0002d5bf`) |
| 1811 | hotspot | (12, 45)–(122, 107) | | `Arming_DrawMissileInfo` (`0002dacf`) |
| 1808 | list box, style 0 | (11, 111)–(106, 139) | | the weapons, or the missile types |
| 1801 | icon button | (111, 113)–(121, 122) | `Up` | the list's previous line |
| 1802 | icon button | (111, 128)–(121, 137) | `Down` | the list's next line |
| 1806 | icon button | (15, 146)–(40, 156) | POINTS | the previous mount |
| 1807 | icon button | (41, 146)–(66, 156) | POINTS | the next mount |
| 2003 | icon button | (68, 146)–(93, 156) | HERCS | the previous HERC |
| 2004 | icon button | (94, 146)–(119, 156) | HERCS | the next HERC |
| 1111 | text button | (14, 157)–(120, 169) | MISSILE TYPE, `Esc` | the list's mode |
| 100 | text button | (48, 175)–(86, 187) | DONE, `Space` | back |

The texts are `eng\menu1800.bin`'s and `menu1820.bin`'s lines 0 to 8, the same in both. The four POINTS and HERCS buttons are icon buttons that keep their text, their key being 0; each pair shares one plate of `res32\corners.dba` with its text centered on it ([the buttons](menu-controls.md#buttons)).

**The screen object** is `0x28` bytes: a menu's `0x26` ([the screen object](menus.md#the-screen-object)), a byte `+0x26` the case sets to `0xff`, and the byte `+0x27`, the list's mode: 0 the weapons, 1 the missile types.

## Entering it

The bay's ARMING ([the bar's buttons](between-missions.md#the-bars-buttons)) and a single mission's war map's ([ARMING and SERVICE BAY](war-map.md#arming-and-service-bay)) go to 1800, or to 1820 while `Shell_LowMemory` (`0007fd09`) is set. Their case in `Shell_HandleMessage` (`0003f196`):

1. allocates the `0x28`-byte `ArmingMenu`, building its `Menu` base and region in line (the vtables `0008186c`, `00081896`, `000818c8` and `00081900`, the region's type `+0xe` 0, and `Menu`'s `00081bd8`), then stores its vtable `00081fe6`, `0xff` in `+0x26` and 0 in `+0x27`;
2. sets `Arming_LowMemPictures` (`00080d6c`) to 0, or, while `Shell_LowMemory` is set, to `Menu_LoadControlBitmaps` (`00037827`)(1803): `res32\allweap.dba`, loaded with `Resource_LoadBitmapArray` into the dword `000813d4` when that is 0, and returned as it stands otherwise. The picture tiles of 1611, 1316, 1321 and 1914 to 1916 load their banks into the same dword, and a tile's destructor frees and clears it ([the picture tile](menu-controls.md#the-picture-tile));
3. makes it the current menu and sets the dword `0007fce4` to `0008003c`, so a press outside the window tests the HERC's mount areas and posts 1702 ([the mouse](menu-input.md#the-mouse), [the clickable areas](../formats/herc-diagrams.md#the-clickable-areas));
4. when the chassis word `+0` of `Hangar_SelectedSlot` (`00080894`)'s HERC is −1, −1 standing also for slot −1 and an empty slot, calls `Hangar_SelectNext` (`0002986f`)(0), which selects the next slot that holds a HERC and is not the marked slot `Hangar_MarkedSlot` (`00080896`) ([the hangar](hangar.md)), and makes the pilot whose HERC that is the current pilot of `CareerRecord_Current`, in either mode (`CareerRecord_SelectPilot` of `CareerRecord_FindPilotBySlot`, [pilot entries](../formats/bst-battle-sets.md#pilot-entries)).

`Menu_Open` builds the window ([the build](menus.md#opening-a-menu--menu_open-0003969e)). The list 1808 takes the lines `Arming_BuildWeaponList`(−1, −1) names, with the counts `Inventory_FormatCounts` writes, and line 0 selected ([the list](#the-weapon-list)); 1903 takes the eight chassis names, the list `00081250`, `eng\watext.bin` `0x1b`–`0x22`, with the selected HERC's chassis selected, or −1; 1904 takes the pilot names of `000811e8`, `eng\watext.bin` `0x55` PLAYER, the 36 names and `0x7a` UNASSIGNED, its first line replaced in `Shell_Mode` 1 by the dword at the roster `Career_Roster` (`0009b218`)'s `+1`, the career's name, with the line `PilotEntry_NameIndex`(`CareerRecord_Current`, its current pilot `+0x12`) + 1 selected. For a current pilot of −1, a HERC no pilot has, `PilotEntry_NameIndex` answers 36, and the line is UNASSIGNED. Both readouts keep the alignment 0, left-aligned, on 1800 and 1820; the build centers them on any other screen ([the build](menus.md#opening-a-menu--menu_open-0003969e)). Then for 1800 `Menu_Open` makes MISSILE TYPE inactive, finding it in any state (`DUIWindow_FindControlById`) and writing state 2 and *changed*, and calls slot `+0x24` of control 1803, found with `DUIWindow_FindEnabledControlById`, with 0; for 1820 it makes MISSILE TYPE inactive only.

The entry leaves the shown weapon `Arming_ShownWeapon` (`00080d64`), the shown missile type `Arming_ShownMissileType` (`00080d66`) and `Arming_AnimatedWeapon` (`00080d68`) as the last visit left them, all three 0 in the image, and so the first drawing of the panel shows that weapon.

## The panel

**The panel under the HERC and PILOT lines describes the weapon in a few lines of text over its animation, or over its still picture when memory is low.** Both hotspots cover it, and the window draws each hotspot through its handler with (its rect, its id) ([drawing a window](menu-controls.md#drawing-a-window--duiwindow_draw-0001bfa3)); a hotspot whose byte `+0` is set is drawn again by the next update ([updating a window](menu-controls.md#updating-a-window--duiwindow_update-0001c067)), and that is how the handlers below ask for a redraw. With *l*, *t* the rect's corner, the labels are in `nm_white` (`000aacee`) and the values in `nm_ylw` (`000aacf6`) ([the shell's fonts](../formats/styles-dat.md#the-loads)), the font switched to `nm_ylw` only after each label, each drawn left-aligned with `HudFont_DrawString` from the pen point given: before its text each handler sets the alignment `+0x231` of the render context `000ab612` to 0.

**`Arming_DrawWeaponInfo` (`0002d5bf`)**, hotspot 1803's, draws nothing while `Arming_ShownWeapon` is 17, the empty mount. Otherwise, with *w* the weapon:

1. It clears the text: while `Shell_LowMemory` is clear, two bands, each the rect two corners span, the smaller of each coordinate its top left: (*l*, *t*) and (122, 59), and (12, 98) and the rect's far corner (*r*, *b*). Over the hotspot's rect (12, 45)–(122, 107) they are (12, 45)–(122, 59) and (12, 98)–(122, 107), which leave the animation between them. While it is set, the whole rect. Each is `Raster_FillFrameRectColours`(`0x1f`, `0x1f`, rect).
2. Labels at (*l* + 5, *t* + 6) `0xcf` TYPE: and (*l* + 5, *t* + 15) `0xd1` RANGE:, both lines of `eng\watext.bin` (`0009afa8`).
3. At (*l* + 5, *t* + 59), by *w*: for 0 to 2, the autocannons, `0xd4` SIZE: and, 25 pixels right, the value `0xf8` + *w* (20 mm, 35 mm, 50 mm); for 9 to 11, the missile racks, `0xd2` MISSILES: and, 35 pixels right, `0x10a` NONE when `Arming_ShownMissileType` is 5 and `0x106` + the type otherwise (SEMI-ACTIVE RADAR, ACTIVE RADAR, ANTI-RADIATION, ELECTRO-OPTICAL); for 5 and 13, the grenade launcher and the jammer pod, nothing; for any other, `0xd3` POWER: and, 30 pixels right, `0xf8` + *w*.
4. The values at (*l* + 30, *t* + 6), the name `0xd5` + *w* (SCORPION AUTOCANNON to SPECIAL ELF), and at (*l* + 30, *t* + 15), the range: `0x10b` + the missile type for 9 to 11 and `0xea` + *w* otherwise.
5. While `Shell_LowMemory` is set, frame *w* of `Arming_LowMemPictures` with `Bitmap_Blit`, flags 0, at the point *w* of `Arming_PicturePoints` (`000a04dc`), 18 `int32` pairs in the window's coordinates, which the menu handlers' file's static initializer `MenuHandlers_StaticInit` (`00030ee9`) fills ([their values](repair-and-build.md#where-the-object-files-start-and-end)).

[Refreshing](#refreshing) stores `Arming_ShownWeapon` −1 for a mount selected with no HERC, which ARMING does not reach: `es2_xref.py` finds `Herc_MountHighlightShown` (`0007ff98`) set to 1 in `Herc_HighlightNextMount`, `Herc_HighlightPreviousMount` and `Herc_HighlightMount`, called from `Hangar_NextMount`, `Hangar_PrevMount` and `Hangar_SelectMount`, each of which first tests that `Hangar_SelectedSlot` holds a HERC; and ARMING changes the slot only in its HERCS cases, which clear the byte.

**`Arming_DrawMissileInfo` (`0002dacf`)**, hotspot 1811's, draws only while the byte `Herc_MountHighlightShown` (`0007ff98`) is set, a mount being selected ([the hangar](hangar.md)), and `Arming_ShownMissileType` is not 5. With *m* the type: while `Shell_LowMemory` is set it clears the whole rect as above; TYPE: and RANGE: as above; at (*l* + 30, *t* + 6) `0x106` + *m* and at (*l* + 30, *t* + 15) `0x10b` + *m*; and while `Shell_LowMemory` is set, frame *m* + 14 of `Arming_LowMemPictures` at the point *m* + 14. While memory is not low it clears nothing, and the animation clears for it: each of its redraws follows a slot `+0x24` call, which marks the animation changed, and an update draws a changed control before its hotspots ([updating a window](menu-controls.md#updating-a-window--duiwindow_update-0001c067)), the control's `CornerFrame_Draw` filling the inside of its rect, (14, 47)–(120, 105), in its style record's dword `+0` ([drawing a frame from a style](../simulation/alert-panels.md#drawing-a-frame-from-a-style)). The screen's own draw, which draws every hotspot, comes with no mount selected: DONE clears the byte, and so does `Herc_BuildDiagram`.

ARMING calls the animation's slot `+0x24` with a weapon 0 to 16, −1 for the empty mount, or 14 + a missile type, only while `Shell_LowMemory` is clear ([the animation](#the-animation)).

## The animation

**Over the panel's middle a `.TTM` script shows the weapon, or the missile type.** The kind-17 control 1803 is an `MTAnimListBox` (vtable `0007f534`, `0x49` bytes; the class name is stored before the vtable), a slider control like [the list box](menu-controls.md#the-list-box) that shows one of the 18 scripts `Menu_OpenControlTtms` (`00037996`) opened for it, their handle bytes at `000a056c`: 0 to 13 the weapons' `w1800w*.ttm`, 14 to 17 the missile types' `w1800r0*.ttm`. It opens each with `Ttm_Open`(0, 0, the dword `0007fcdc`, name), playing and not looping, runs it once and hides it (mode 2) ([the scripts](../formats/ttm-scripts.md#choosing-a-page)). The dword `0007fcdc` holds the render context `00083bc8` that [drawing a screen](menus.md#drawing-a-screen) sets to the current menu's window rect, clip mode 1, origin (0, 0) and translation 0, so the scripts draw in the screen's coordinates, cut to ARMING's window once the screen is drawn (`w1800w00.ttm` draws its frames at (218, 74)); at the build it still holds the previous screen's rect.

Each retail script loads the picture of its own name, `w1800w05.ttm` `w1800w05.BMP`, but for `w1800r03.ttm`, ELECTRO-OPTICAL's, which loads `w1800g00.BMP`: `SHELL0.VOL` holds no `W1800R03.DBA`, and that type's picture is `W1800G00.DBA`, a finned round with a short thick body where the other three types' are slender missiles. The low-memory stills are the same art: each frame of `ALLWEAP.DBA` is the finished frame of its script's bank, frame 17 `W1800G00.DBA`'s, except frame 5, the grenade launcher's, an 8×2 bitmap. `SHELL0.VOL` also holds `W1800G00.TTM` and `W1800G01.TTM`, with a `W1800G01.DBA` no other script loads; a byte search of `GO.EXE` finds no name format for either ([cut content](../cut-content.md#weapons)).

`MTAnimListBox_Ctor` (`0001eff6`)(control, `0x13`, 0, `0xffff`, id, handles, rect) builds it on `DUIControl_Ctor`(control, 0, id, rect), so `Tab` passes over it, with the slider's fields as [the list box](menu-controls.md#the-list-box) has them: the range `+0x24` 0 to `+0x26` `0x13`, the tracked point `+0x28` and its previous value `+0x2a` −1, `+0x2c` 1 and `+0x1b` 1, tracking. Then it stores `DUIVSlider`'s vtable and its own, the handles at `+0x3e`, `+0x26` `0x13` again, the script shown `+0x42` 0, `STYLES.DAT` record 3 (`00082654` + `0xc0`) at `+0x44`, and clears *changed*. It writes neither the byte `+0x48` nor the thumb rect `+0x2e`.

| Slot | Function | Does |
|---|---|---|
| `+4` | `MTAnimListBox_Dtor` (`0001f164`) | `Menu_CloseControlTtms`(`+0x3e`), clears `+0x3e`, stores the base classes' vtables, and frees the control when bit 0 of the flags is set; the handle bytes keep the closed handles |
| `+8` | `MTAnimListBox_Draw` (`0001f20f`) | `CornerFrame_Draw` with the record `+0x44` over the rect; while `+0x48` is set, `Raster_FrameRectColour`(10, rect); while `+0x42` is 0 or more, the script shown set playing (`Ttm_SetMode`(0, its handle)); *changed* cleared |
| `+0xc` | `MTAnimListBox_TrackSelect` (`0001f3ca`) | the tracked point `+0x28` above the rect's top: slot `+0x1c`; below its bottom: slot `+0x20`. Slot `+0x38` keeps the point within the two, so a press reaches neither |
| `+0x10`, `+0x18` | `MTAnimListBox_Highlight` (`0001f416`), `MTAnimListBox_Unhighlight` (`0001f42c`) | set or clear `+0x48`, and set *changed* |
| `+0x1c` | `MTAnimListBox_SelectPrev` (`0001f275`) | while `+0x42` is 0 or more: hides the script shown (`Ttm_SetMode`(2, …)), takes the one before, sets it to page 1 (`Ttm_SetPage`) and playing; from 0 that is index −1, the byte before the handles. *Changed* set either way |
| `+0x20` | `MTAnimListBox_SelectNext` (`0001f2dc`) | hides the script shown while `+0x42` is 0 or more; `+0x42` becomes (`+0x42` + 1) mod `+0x26`, 19, one past the 18 handles; that script set to page 1 and playing; *changed* set |
| `+0x24` | `MTAnimListBox_Select` (`0001f359`)(control, index) | hides the script shown while `+0x42` is 0 or more; `+0x42` becomes the index; for an index of 0 or more, that script set to page 1 and playing, so it starts again; *changed* set |
| `+0x2c` | `DUIVSlider_GetValue` (`0001ab2c`) | the slider's value from its thumb rect ([the picture tile](menu-controls.md#the-picture-tile)), which this control never fills |
| `+0x38` | `DUIVSlider_SetTrackPoint` (`0001aac3`) | the slider's ([the picture tile](menu-controls.md#the-picture-tile)) |

Slot `+0x14` is `BarGraph_NoOpButtonCopy` (`0001d041`), and the others are the slider's. So each slot `+0x24` call from ARMING hides the script shown and starts the one named: for a weapon 0 to 13 its own; for 14 to 16, the special weapons, the missile types' scripts r00 to r02, there being no weapon script past 13; for 14 + a missile type that type's; for −1 none. The slot touches none of the panel's text; it marks the control changed, so the next update draws the control's corner frame over its rect ([updating a window](menu-controls.md#updating-a-window--duiwindow_update-0001c067)). A release over the control posts its id with slot `+0x2c`'s value, and ARMING's 1803 case reads only the id ([what a press does](#what-a-press-does)).

## The weapon list

**The list shows, with no mount selected, every weapon the player has or can have; with a mount selected, NONE and the weapons that mount takes.** `Arming_BuildWeaponList` (`000371ad`)(mount, chassis) fills `Arming_ListLines` (`000ab30c`) with `eng\watext.bin` line numbers, ended by −1, and returns it; `WaText_LinesFromList` makes the lines. A weapon's line is word *w* of `Arming_CareerWeaponLines` (`00081274`) in a career, `0x2e` + *w*, "[??] ATC 20MM" to "[??] BULLDOG ELF", and of `Arming_SingleWeaponLines` (`0008129c`) in a single mission, `0x47`–`0x54` for 0 to 13 ("ATC 20MM" to "ECM") and `0x3c`–`0x3e` for 14 to 16, the career table's own bracketed lines, so a single mission lists the special weapons as "[??] BULLDOG EMP" to "[??] BULLDOG ELF"; word 17 of each is `0x2d` NONE. A weapon is offered when `CareerRecord_Current`'s [equipment state](../formats/bst-battle-sets.md#equipment-availability) (its `+0xa8`) makes it available (`Tech_IsAvailableListCopy`, `00037383`) or its stack in the stock `[000aad62]` is not empty (`Inventory_SlotCount`):

- with a mount of −1, each weapon 0 to 16 offered, in that order, the chassis not read;
- otherwise NONE first, then, for each of the chassis's layout records for that mount from the second to the last, the mount's count being its word `+4` ([the mount pictures](../formats/herc-diagrams.md#the-mount-pictures)), the record's weapon, its word `+0`, when it is offered or is the selected HERC's current mount's weapon (`Herc_CurrentMountWeapon`, −1 with no HERC).

`Arming_ListLines` is a static array: each build writes from word 0 and ends with −1, and leaves the words past the end as earlier builds left them.

**The counts.** `Inventory_FormatCounts` (`00028242`)(stock, lines) does nothing in a single mission, so the special weapons' `[??]` stays. In a career, for each line that starts with `[`, it takes the line's weapon with `Arming_WeaponAtListPosition`(the line's index) and writes the first two characters of `"%2d"` of that weapon's stack count, the word at the stock's `+2` + 6 × the weapon, over the two after the `[`. It makes no test of the weapon, so a `[` line that answers 17 reads past the 17 stacks. The lines are the text table's own, so a count stays written until the next. The build and `Arming_SelectWeapon` write them.

`Arming_WeaponAtListPosition` (`0003714e`)(*i*) is the first weapon 0 to 17 whose line equals the list's word *i*, or 17, so the list's −1 end answers 17. `Arming_WeaponListPosition` (`000370ec`)(*w*) is the first *i* below 17 whose word is not 0 and equals *w*'s line, or 17. It scans words 0 to 16 whatever the list's end, so a word left past the end by an earlier, longer build can match; and it reads *w*'s line with no test of *w*, so for −1 it reads the word before the table: in a career `00081272`, −1, the end of the word list `00081264`, which matches the list's own −1 end and answers its index; in a single mission `0008129a`, 0, which matches nothing. A list box selects a line past its last as its last ([the list box](menu-controls.md#the-list-box)).

## What a press does

`ArmingMenu_OnMessage` (`0002e54c`)(menu, message) looks the message's target up among the eleven ids at `ArmingMenu_CaseTable` (`00080db8`) with `REPNE SCASD` and jumps through the target beside it. An id not in the table does nothing. The value is the message's word `+4` ([a message's path](menu-input.md#a-messages-path)).

| Id | Case | What it does |
|---|---|---|
| 1803 | `0002e572` | sets hotspot 1803's byte `+0`, redrawing the weapon's text |
| 1808 | `0002e5b2` | `Arming_SelectListLine`(menu, value): a line picked in the list |
| 1801 | `0002e5c5` | the list's slot `+0x1c`, `MTStrListBox_SelectPrev`, then `Arming_SelectListLine`(menu, its selected line) |
| 1802 | `0002e653` | the same with slot `+0x20`, `MTStrListBox_SelectNext` |
| 2004 | `0002e6e1` | HERCS >, [below](#hercs) |
| 2003 | `0002e896` | < HERCS, [below](#hercs) |
| 1702 | `0002ea0f` | a press on a mount area: `Hangar_SelectMount` (`00029a14`)(value), which selects that mount, or none for −1; `+0x27` = 0; `Arming_Refresh` |
| 1807 | `0002ea2c` | POINTS >: `Hangar_NextMount` (`00029a52`), the next mount, wrapping to the first; `+0x27` = 0; `Arming_Refresh` |
| 1806 | `0002ea43` | < POINTS: `Hangar_PrevMount` (`00029a87`), the previous mount, wrapping to the last; `+0x27` = 0; `Arming_Refresh` |
| 1111 | `0002ea5a` | MISSILE TYPE, [below](#missile-type) |
| 100 | `0002ec7f` | DONE, [below](#done) |

The three mount calls work on the selected HERC's current mount `+0x70`, selecting it, which sets the byte `Herc_MountHighlightShown` (`0007ff98`), and redraw its mount pictures ([the hangar](hangar.md)); from no mount, POINTS > selects the first and < POINTS the last. So `Up` and `Down` select a line exactly as a click on it does, fitting each weapon they pass while a mount is selected.

### Picking a line

`Arming_SelectListLine` (`0002e126`)(menu, line) calls `Arming_SelectMissileType` while `+0x27` is 1 and `Arming_SelectWeapon` otherwise.

**`Arming_SelectWeapon` (`0002e157`)(menu, line)**, with *w* = `Arming_WeaponAtListPosition`(line) stored in `Arming_ShownWeapon`:

1. When *w* differs from `Arming_AnimatedWeapon`: while `Shell_LowMemory` is clear, the animation's slot `+0x24` with *w*, or −1 for 17; hotspot 1803 redrawn; with a mount selected (`Herc_MountHighlightShown`, `0007ff98`), *w* fitted ([fitting a weapon](#fitting-a-weapon)), the list rebuilt for the mount (`Arming_BuildWeaponList`(the HERC's `+0x70`, its chassis), or −1 for each with no HERC) into `Arming_WeaponLines` (`00080d60`), its counts written, its lines put in the list (slot `+0x44`) and *w*'s line selected (slot `+0x24`, `Arming_WeaponListPosition`); and `Arming_AnimatedWeapon` set to *w*.
2. When *w* is 9 to 11, a missile rack: `Arming_ShownMissileType` set to the current mount's word `+0x18` + 2 × `+0x70` (0 with no HERC), and hotspot 1803 redrawn. The word is read with no test of `+0x70`, as in [MISSILE TYPE](#missile-type) and [refreshing](#refreshing); with no mount selected, `+0x70` is the −1 that `Hangar_SelectMount`(−1) leaves on each change of HERC ([the hangar's selection](repair-and-build.md#the-hangars-selection)), and the word is `+0x16`, mount 9's weapon. No chassis has more than nine mounts, so neither `Herc_Ctor` nor `Herc_ReadFromStream` writes it, and the HERC's block comes zero-filled from the heap ([the heap](../simulation/memory.md#the-heap)): the type shown is 0, SARH.
3. With a mount selected: MISSILE TYPE made up (state 0) for 9 to 11 and inactive (state 2) for any other, each marked changed.

So with no mount selected a line only shows its weapon, and the line already shown does nothing.

### Fitting a weapon

**Fitting moves a unit between the mount and the stock; a unit in stock is its condition, a word on its weapon's stack.** `Arming_SelectWeapon` passes *w* to `Hangar_FitWeapon` (`00029abc`) ([repair and build](repair-and-build.md)), which works on `Hangar_SelectedSlot`'s HERC's current mount:

1. Nothing happens when the mount already carries *w*.
2. The incoming unit is 100 for 17, the empty mount, and otherwise the head of *w*'s stack, popped with `Inventory_Take`; with *w*'s stack empty nothing happens.
3. Unless the mount was empty, the outgoing unit, the mount's condition (its per-mount status word), is pushed onto the old weapon's stack with `Inventory_Add`.
4. The mount's condition becomes the incoming unit and its weapon *w*, or 17 when that condition is 0; its missile type becomes 5 for a weapon other than 9 to 11, and 0, SARH, for 9 to 11 when it was 5 ([the HERC record](../formats/car-hercs-and-stock.md#the-herc-record)).

It erases the HERC's mount pictures before step 3 and draws them again after step 4 ([mounts](repair-and-build.md#the-hangars-selection)). `Hangar_FitWeapon` reads the mount's weapon before any test of the slot, and pops the incoming unit before it tests that the slot holds a HERC, so with no HERC a popped unit would be lost; ARMING fits only with a mount selected, which takes a HERC ([the panel](#the-panel)).

A unit's condition goes with it: a damaged weapon taken off returns to stock at its condition, and the next fit of that weapon takes the last unit returned or delivered. ES2's `Herc_FitMount` moves its catalog units the same way but returns a unit at the condition it was fitted at ([fitting a weapon](../../retail/shell/weapons-and-repair.md#fitting-a-weapon)).

### MISSILE TYPE

**MISSILE TYPE turns the list into the four guidance kinds of the selected mount's missile rack and back.** It is up only while a mount carrying a missile rack is selected (above and [refreshing](#refreshing)). While it is inactive its key `Esc` does nothing: `DUIWindow_FindControlByKey` passes over a control in state 2, so the window's key subscriber does not claim the key ([the keys](menu-input.md#the-keys)), and `Shell_HandleInput` takes `Esc` only with `Ctrl` or `Alt` ([the input](startup.md#the-input)). Its case, with `+0x27` 0:

1. `Arming_ShownMissileType` set to the current mount's word `+0x18` + 2 × `+0x70`, or 0 with no HERC;
2. the list given `Menu_ListLines`(1809), `eng\watext.bin` `0x40` SARH, `0x41` ARH, `0x42` ARM and `0x44` EO, types 0 to 3, with the type's line selected;
3. while `Shell_LowMemory` is clear, the animation's slot `+0x24` with 14 + the type; hotspot 1811 redrawn;
4. the button's text set to `eng\watext.bin` line 1, WEAPON TYPE (`MTButton_SetTextFromTable`, slot `+0x3c`), and `+0x27` set to 1.

With `+0x27` 1: while `Shell_LowMemory` is clear, the animation's slot `+0x24` with `Arming_ShownWeapon`; hotspot 1803 redrawn; and `Arming_Refresh`, which puts the weapons back.

**`Arming_SelectMissileType` (`0002e47f`)(menu, type)** stores the type in `Arming_ShownMissileType` and, with a HERC in the selected slot, through `Herc_SetCurrentMountAmmo` (`00024a1e`)(HERC, type) in its current mount's word `+0x18` + 2 × `+0x70` unless `+0x70` is −1; while `Shell_LowMemory` is clear it calls the animation's slot `+0x24` with 14 + the type; and it redraws hotspot 1811. The simulator reads the word as the mount's ammunition type ([the second pass](../formats/script-dat.md#the-second-pass)).

### Refreshing

**`Arming_Refresh` (`0002dc54`)(menu)** runs after a change of HERC or mount and when MISSILE TYPE goes back:

1. `Arming_ShownMissileType` set to the current mount's word `+0x18` + 2 × `+0x70`, or 0 with no HERC;
2. the list's lines replaced by `Arming_BuildWeaponList`(the HERC's `+0x70`, its chassis), −1 for each with no HERC, kept in `Arming_WeaponLines`; it writes no counts;
3. with no mount selected: `Arming_ShownWeapon` set to 0, and, when `Arming_AnimatedWeapon` is not 0, line 0 selected, the animation's slot `+0x24` with 0 while `Shell_LowMemory` is clear, hotspot 1803 redrawn and `Arming_AnimatedWeapon` set to 0; MISSILE TYPE inactive;
4. with a mount selected: `Arming_ShownWeapon` set to the mount's weapon (−1 with no HERC) and its line selected; while `Shell_LowMemory` is clear, the animation's slot `+0x24` with −1 for the empty mount, or with the weapon when it differs from `Arming_AnimatedWeapon`; hotspot 1803 redrawn; `Arming_AnimatedWeapon` set to the weapon; MISSILE TYPE up for 9 to 11 and inactive otherwise;
5. MISSILE TYPE's text set to line 0, MISSILE TYPE, and `+0x27` set to 0.

### HERCS

HERCS > calls `Hangar_SelectNext` (`0002986f`) and < HERCS `Hangar_SelectPrev` (`00029946`), with 2 in a single mission (`Shell_Mode` (`000aad56`) 0) and 0 in a career. With 0 they step `Hangar_SelectedSlot` to the next or previous slot that holds a HERC and is not the marked slot; with 2, on until a HERC that no pilot of the single mission's record has, or back to the start. Both then clear the HERC's mount and redraw the bay, `00029946` only while `Hangar_SelectedSlot` is not −1 ([the hangar](hangar.md)). In a single mission the case then gives the HERC to the record's current pilot, storing the slot in `SingleMission_Record` (`0009b014`)'s pilot entry `+0x12`, its `+8`. Both cases go on alike:

1. the pilot of the slot's HERC made the current record's current pilot (`CareerRecord_SelectPilot`(record, `CareerRecord_FindPilotBySlot`(record, slot)));
2. 1904 selects the line `PilotEntry_NameIndex`(the roster's current record `0009b23b` + `0x1f7` × `[0009b21f]`, that pilot) + 1, UNASSIGNED for a HERC no pilot has (the pilot −1, for which `PilotEntry_NameIndex` answers 36), and 1903 the HERC's chassis, or −1 (slot `+0x24`);
3. the byte `Herc_MountHighlightShown` (`0007ff98`) cleared, and `Arming_Refresh`.

### DONE

DONE (`Space`) calls `Hangar_ClearSelectedMount` (`0002961c`), which clears the mount selection and redraws the mount pictures ([the hangar](hangar.md)), and saves the current record with `CareerRecord_SaveCurrent` (`00014f28`). In a single mission it then writes the hangar to `car\sm_init.dat` through `Path_ApplyAltDrive` with `Hangar_SaveFile` ([the HERC lists](../formats/car-hercs-and-stock.md#the-herc-lists)) and sets the message's target to 1501, the war map; in a career it sets the target to 1700, the service bay before a mission. The stock is not saved here.

**Leaving.** `ArmingMenu_Dtor` (`000405ae`) frees `Arming_LowMemPictures` with `Shell_FreeBitmapArraySlot` (`00037924`), which clears `000813d4`, when it is set, and leaves `Arming_LowMemPictures` itself as it is, for the next entry to set again; then it does what every menu's destructor does ([the screens](menus.md#the-screens)).

## Functions

| ES1 | Address |
|---|---|
| `Arming_DrawWeaponInfo`, `Rect_CopyCtorMenuHandlersCopy`, `Arming_DrawMissileInfo`, `Arming_Refresh` | `0002d5bf`, `0002daa1`, `0002dacf`, `0002dc54` |
| `Arming_SelectListLine`, `Arming_SelectWeapon`, `Arming_SelectMissileType`, `ArmingMenu_OnMessage`, `ArmingMenu_Dtor` | `0002e126`, `0002e157`, `0002e47f`, `0002e54c`, `000405ae` |
| `Arming_WeaponListPosition`, `Arming_WeaponAtListPosition`, `Arming_BuildWeaponList`, `Tech_IsAvailableListCopy` | `000370ec`, `0003714e`, `000371ad`, `00037383` |
| `MTAnimListBox_Ctor`, `MTAnimListBox_Dtor`, `MTAnimListBox_Draw`, `MTAnimListBox_SelectPrev`, `MTAnimListBox_SelectNext`, `MTAnimListBox_Select` | `0001eff6`, `0001f164`, `0001f20f`, `0001f275`, `0001f2dc`, `0001f359` |
| `MTAnimListBox_TrackSelect`, `MTAnimListBox_Highlight`, `MTAnimListBox_Unhighlight` | `0001f3ca`, `0001f416`, `0001f42c` |
| `Inventory_CopyStream`, `Inventory_SkipStream`, `Inventory_FormatCounts` | `0002800c`, `00028149`, `00028242` |

## Globals

| | Address |
|---|---|
| `Arming_WeaponLines`, the lines `Arming_SelectWeapon` and `Arming_Refresh` last gave the list | `00080d60` |
| `Arming_ShownWeapon`, `int16`, the weapon the panel describes | `00080d64` |
| `Arming_ShownMissileType`, `int16`, the missile type the panel describes | `00080d66` |
| `Arming_AnimatedWeapon`, `int16`, the weapon last shown, against which a pick is a change | `00080d68` |
| `Arming_LowMemPictures`, `res32\allweap.dba` while memory is low, else 0 | `00080d6c` |
| `ArmingMenu_CaseTable`, the handler's eleven ids and their targets at `00080de4` | `00080db8` |
| `Arming_PicturePoints`, 18 `int32` points, a weapon's or 14 + a missile type's | `000a04dc` |
| `Arming_ListLines`, the line numbers `Arming_BuildWeaponList` fills, ended by −1 | `000ab30c` |
| `Arming_CareerWeaponLines`, `Arming_SingleWeaponLines`, 18 `int16` lines and a −1 each | `00081274`, `0008129c` |

## Open

- **Open:** what RANGE: shows for a missile rack while `Arming_ShownMissileType` is 5: line `0x10b` + 5, `0x110`, one past the 272 lines of `eng\watext.bin`, for which `Text_GetString` adds to the pool the word after the offset array, which the heap's layout decides ([the string tables](../formats/bin-string-tables.md)). `Arming_SelectWeapon` reaches it when a rack is picked for a selected mount that carries a weapon other than a rack while the rack's stack is empty: the fit does nothing ([fitting a weapon](#fitting-a-weapon)) and step 2 reads the mount's word 5. A career offers the racks with their stacks empty, all three being available from its start ([the retail files](../formats/bst-battle-sets.md#the-retail-files)).
- **Open:** whether any retail machine carries one of the special weapons 14 to 16, whose POWER: takes lines `0x106`–`0x108`, the guidance names. Both equipment files give them −1, −1 and neither armory file stocks them ([the retail stock](../formats/car-hercs-and-stock.md#the-retail-stock)), so they reach the list through the stock's salvage pairs, each a destroyed machine's hardpoint weapon ([after a mission](between-missions.md#after-a-mission)), or through a HERC that carries one, which the retail HERC lists do not ([the retail HERC lists](../formats/car-hercs-and-stock.md#the-retail-herc-lists)).
- **Open:** in a single mission the HERCS cases take the pilot's name from the roster's current record rather than `SingleMission_Record`.
- **Open:** a retail check under DOSBox-X: in a new career, ARMING from the bay; with no mount chosen, clicking a weapon in the list shows its picture and text and changes no count; POINTS > then a click on another weapon swaps it in, the old weapon's bracketed count going up by one and the new one's down by one; on a mount with a missile rack MISSILE TYPE lights and lists SARH, ARH, ARM and EO; DONE returns to the bay's strip.
