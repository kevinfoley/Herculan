# GO.EXE — a single mission

**SINGLE MISSION on the main menu opens a menu of the same name: a list of seven mission types, RECONNAISSANCE to STRIKE, with an up and a down arrow beside it, and the buttons MISSION PARAMS, OPTIONS, RETURN TO MAIN and BRIEFING >. BRIEFING > loads a mission of the selected type, drawn at random from that type's list, and goes through a message to the war map and its briefing, as INSTANT ACTION does. MISSION PARAMS opens a second menu of five settings, AMMO, DAMAGE, DIFFICULTY, VISIBILITY and TIME OF DAY, each a button that steps the value shown beside it; CANCEL puts the values back and ACCEPT keeps them, and both save them to `data\prefs.cfg` and return to SINGLE MISSION.** Both menus are built, opened, drawn and left as every menu is ([the menus](menus.md)); this page holds what their controls do.

Addresses are `ES1GO` image addresses ([the X-32 executable](../formats/x32-executable.md#the-image)). The functions here remove their own arguments, so argument lists are in stack order ([calling conventions](../formats/compiler.md#calling-conventions)). ES2's shell has no single-mission menu built from a `.DLG`; nothing here is ported from it.

## The SINGLE MISSION menu

**`dlg\menu1100.dlg` lays out a menu window titled SINGLE MISSION with the list box 1101 on the left, its two arrows beside it, and four text buttons on the right; `eng\menu1100.bin` holds the title and the buttons' texts.** The window is (49, 50)–(281, 165) on the screen, the main menu's rect, and loads `res32\menu1100.dba` ([the window](menu-controls.md#the-window)). The rects below are in the window:

| Id | Kind | Rect | Key | Text |
|---|---|---|---|---|
| 1110 | text button | (130, 26)–(203, 38) | | MISSION PARAMS |
| 1300 | text button | (130, 40)–(203, 52) | | OPTIONS |
| 1000 | text button | (130, 54)–(203, 66) | `0x01` Esc | RETURN TO MAIN |
| 1501 | text button | (130, 82)–(203, 94) | `0x39` Space | BRIEFING > |
| 101 | icon button | (106, 31)–(117, 41) | `0x48` Up | |
| 102 | icon button | (106, 81)–(117, 91) | `0x50` Down | |
| 1101 | list box, style 0 | (21, 29)–(101, 94) | | |

The arrows take the frames of their keys from `res32\corners.dba`, 20 and 22 for Up and 21 and 23 for Down ([buttons](menu-controls.md#buttons)). `Esc` presses and releases RETURN TO MAIN, `Up` and `Down` the arrows, and `Space` BRIEFING > ([the keys](menu-input.md#the-keys)); `Menu_Open` puts the pointer on BRIEFING >, the control whose key is `Space` ([opening a menu](menus.md#opening-a-menu--menu_open-0003969e)). `Tab` stops at the six buttons and passes over the list.

**Opening it.** The main menu's SINGLE MISSION sets `Shell_Mode` to 0 and `Shell_ReturnScreen` to 1100 ([the main menu's buttons](main-menu.md#the-buttons)). Screen 1100's case in `Shell_HandleMessage` (`0003c7d8`) at `0003e3ea` allocates a `0x26`-byte screen object ([the screen object](menus.md#the-screen-object)), stores the shell's region classes' vtables in its `GLRectangleRegion` at `+0xa` with `+0xe` 0, then `Menu`'s vtable and `MissionMenu`'s (`00081c3a`) at `+0x22`, and makes it `Shell_CurrentMenu` (`000817c8`). The screen's backdrop group is 1000, the main menu's, so opening it from the main menu changes neither the picture nor the music ([the backdrop](menus.md#the-backdrop)). `Menu_Open` (`0003969e`)(menu, 1100) then selects the line `Mission_Kind` (`000aad5a`) holds in the list, with the list's slot `+0x24`, `MTStrListBox_SelectLine` (`0001ea4f`): a negative value leaves line 0 selected, and a value past the last line selects the last ([the list box](menu-controls.md#the-list-box)).

### The list

**The list shows the seven mission types, one line each, the selected one in `nm_red1` on a filled box and the others in `nm_green`.** Its lines are `Menu_ListLines` (`0003750d`)(1101), the case at `000376f5`: `WaText_LinesFromList` (`000373fa`) of the word list at `00081264`, lines `0x23` to `0x29` of `eng\watext.bin`:

| Line | Text | `msn\sing_msn.dat` list |
|---|---|---|
| 0 | RECONNAISSANCE | `sm_rcn01`, `sm_rcn02` |
| 1 | SCOUT | `sm_sct01`, `sm_sct02`, `sm_sct50` |
| 2 | PATROL | `sm_ptl01`, `sm_ptl02` |
| 3 | RAID | `sm_rad01`, `sm_rad02` |
| 4 | SCRAMBLE | `sm_scr01` |
| 5 | SEARCH AND RESCUE | `sm_sar01` |
| 6 | STRIKE | `sm_ass01` |

The line is the index of the list of `msn\sing_msn.dat` that BRIEFING > draws from ([single missions](../formats/bst-battle-sets.md#single-missions)). That file holds eight lists; the eighth, a second `sm_ass01`, has no line. `SingleMission_Start`'s two callers pass the list's line (BRIEFING >) or −1 (INSTANT ACTION >), so only the −1, a list drawn at random, can reach it ([starting the mission](#starting-the-mission)). The id 1101 is not among the 23 at `00081a00` that set their own selection, so the build selects line 0 and `Menu_Open` then selects `Mission_Kind`'s.

A press on the list or a move while it is held selects the line under the pointer, and above or below it the line before or after ([the list box](menu-controls.md#the-list-box)); the release posts (0, the list's index, its selected line, 1101), and `MissionMenu_OnMessage` stores the line in `Mission_Kind`. 1101 is not in `Shell_ScreenIds`, so the screen stays.

`Mission_Kind` is also header word 3 of the mission last loaded: the load's step 6 stores it there ([loading a mission](../formats/msn-mission-file.md#loading-a-mission)). Counted from line `0x23` of `eng\watext.bin` as the list's lines are, `SM_SCR01.MSN`'s 4 is SCRAMBLE, the line that names it, and `SM_ASS01.MSN`'s 9 is ASSAULT, three lines past STRIKE ([the mode](../formats/msn-mission-file.md#the-mode-and-the-mission-parameters)). So when the menu opens again after BRIEFING >, from the war map's CANCEL ([CANCEL](war-map.md#cancel)), the list selects the line of the loaded mission's word 3, the last line, STRIKE, for 9.

**After a mission the list is back on RECONNAISSANCE.** The shell that comes back from the simulator is a new process ([the launcher's loop](launcher.md#the-loop)). `Mission_Kind` lies past the image's file bytes, so the startup's zero fill makes it 0 ([loading the image](../formats/x32-executable.md#loading-the-image)), and `es2_xref.py --decode` finds its writers only in `MainMenu_OnMessage` (−1), `MissionMenu_OnMessage` (the selected line) and `MsnGen_LoadMission`; `Mission_ReadRestore` puts the saved word in `000aad5e` ([which file](../formats/eng-mission-text.md#which-file)). So the debriefing's MISSION >, and the pilot-lost path that opens SINGLE MISSION directly ([opening it](debriefing.md#opening-it)), select line 0.

### The buttons

**A release posts the control's id; `MissionMenu_OnMessage` (`0002b375`), slot `+8` of `MissionMenu`'s vtable, acts on four ids, and `Shell_HandleMessage` then opens the screen the message names ([changing screens](menus.md#changing-screens--shell_handlemessage-0003c7d8)).** The handler looks the message's target `+6` up among the four ids of the `REPNE SCASD` table at `00080a54`, its cases at `00080a64`, and does nothing for any other id.

| Control | Id | The handler | Then |
|---|---|---|---|
| the list | 1101 | the message's value `+4`, the selected line, into `Mission_Kind` | no screen |
| up arrow | 101 | the list's slot `+0x1c`, `MTStrListBox_SelectPrev` (`0001e8df`), then its value (slot `+0x2c`) into `Mission_Kind` | no screen: 101 is not in `Shell_ScreenIds` |
| down arrow | 102 | the list's slot `+0x20`, `MTStrListBox_SelectNext` (`0001e919`), then its value into `Mission_Kind` | no screen |
| BRIEFING > | 1501 | the list's value into `Mission_Kind`; `SingleMission_Start` (`00036807`); `Shell_PrepareMission` (`0002a523`); `anm\esiege3.anm` into `000aacfe` with the byte `0007fcd7` 1; the target becomes 1499 | screen 1499, the message, then the war map |
| MISSION PARAMS | 1110 | nothing | [the MISSION PARAMS menu](#the-mission-params-menu) |
| OPTIONS | 1300 | nothing | the options menu, `OptionsMenu` |
| RETURN TO MAIN | 1000 | nothing | the main menu |

Each case finds the list with `DUIWindow_FindEnabledControlById` (`0001bda4`)(the window `+6`, 1101), the arrows twice, once for the step and once for the value, and indexes the window's controls `+0x1f` with the answer untested, so a −1, no enabled 1101, reads the dword before the array. The arrows move the selection one line within the seven and stop at the ends.

**BRIEFING >** loads the mission before the screen changes, as INSTANT ACTION > does, and from screen 1499 on takes INSTANT ACTION's path: the message box over backdrop 3000, then [the war map](war-map.md) 1501 with the mission's briefing ([the path to the mission](main-menu.md#instant-actions-path-to-the-mission)). `Shell_Mode` stays 0, as SINGLE MISSION on the main menu set it, and `Shell_ReturnScreen` 1100. The `.ANM` plays when the next screen is drawn ([drawing a screen](menus.md#drawing-a-screen)).

**OPTIONS** opens the options menu with `Shell_ReturnScreen` as the main menu's SINGLE MISSION left it, 1100, so its RETURN button reads RETURN TO MISSION and comes back here ([the return screen](main-menu.md#the-return-screen)). Reached instead from the war map after INSTANT ACTION >, which sets `Shell_ReturnScreen` to 1000, the options menu's button reads RETURN TO MAIN and goes to the main menu.

### Starting the mission

`SingleMission_Start` (`00036807`) loads `car\s_armory.dat` into the weapon stock with `Inventory_LoadFile` (`00027e02`)(the dword `000aad62`, the stock's pointer, [the weapon stock](../formats/car-hercs-and-stock.md#the-weapon-stock)), builds the path with `SingleMission_BuildPath` (`00036649`) from `Mission_Kind`, loads the mission with `MsnGen_LoadMission` ([loading a mission](../formats/msn-mission-file.md#loading-a-mission)) and saves the paths with `Mission_WriteRestore` ([the restore file](../formats/eng-mission-text.md#which-file)). `SingleMission_BuildPath`, given a list index, or −1 for a list drawn at random below the count `00081084` on `Shell_RandomState` (`0009aec0`), formats a name `StrList_PickRandom` draws from that list as `msn\%s` (`000810cc` for −1, `000810d3` otherwise) into `000aad14` and sets the text path. So −1 makes two draws, the list and then the name below the list's count word `+0`, and an index one, all before `MsnGen_LoadMission`'s draws ([the draws](../formats/msn-mission-file.md#the-draws)). `StrList_PickRandom` (`00020289`) on a list of count 0 draws nothing, `Math_RandomBelow`(0) being 0, and reads the first slot of its null name array. `es2_xref.py` finds two calls of `SingleMission_Start`: `MissionMenu_OnMessage`'s BRIEFING > and `MainMenu_OnMessage`'s INSTANT ACTION >, which passes −1.

## The MISSION PARAMS menu

**`dlg\menu1110.dlg` lays out a menu window titled MISSION PARAMS with a column of five text buttons, AMMO to TIME OF DAY, a list box beside each that shows its value, and CANCEL and ACCEPT; `eng\menu1110.bin` holds the title and the buttons' texts.** The window is (49, 50)–(281, 165), the SINGLE MISSION menu's rect, and loads `res32\menu1110.dba`. The rects are in the window:

| Id | Kind | Rect | Key | Text |
|---|---|---|---|---|
| 99 | text button | (152, 68)–(201, 80) | `0x01` Esc | CANCEL |
| 100 | text button | (152, 81)–(201, 93) | `0x39` Space | ACCEPT |
| 1111 | text button | (23, 29)–(85, 41) | | AMMO |
| 1112 | text button | (23, 42)–(85, 54) | | DAMAGE |
| 1113 | text button | (23, 55)–(85, 67) | | DIFFICULTY |
| 1114 | text button | (23, 68)–(85, 80) | | VISIBILITY |
| 1115 | text button | (23, 81)–(85, 93) | | TIME OF DAY |
| 1116 to 1120 | list box, style 1 | (87, 29)–(150, 41), and each 13 lower | | |

The pointer starts on ACCEPT; `Esc` presses and releases CANCEL and `Space` ACCEPT.

**Opening it.** Screen 1110's case at `0003e589` allocates a `0x30`-byte screen object, the `0x26` bytes of [the screen object](menus.md#the-screen-object) and five `int16` from `+0x26`, and builds it as 1100's case does with `MissionOptionsAlert`'s vtable (`00081f42`). At `0003e718` it copies options 12 to 16 of `Prefs_Options` (`000aa24e`) into the five words `MissionParams_Ammo` (`000aacbc`) to `MissionParams_TimeOfDay` (`000aacc4`), each byte zero-extended, and the five words into the object's `+0x26`, `+0x28`, `+0x2a`, `+0x2c` and `+0x2e`, the values CANCEL puts back; then it makes the object `Shell_CurrentMenu`. Each option is read as `MOVSX ECX, index; MOV AL, [ECX + 000aa24e]; MOVZX EAX, AL`. When `Mem_New` fails (`0003e5a4`) the case skips the build and the copy and stores the null object as `Shell_CurrentMenu`. 1110 is not among the screens `Shell_BackdropForScreen` (`00040afa`) gives a group, so the backdrop stays. `Menu_Open` changes no control for 1110.

**The values.** The build gives each list its word as the selected line: 1116 to 1120 are among the 23 ids at `00081a00`, whose cases `0003a9c7`, `0003a9d9`, `0003a9eb`, `0003a9fd` and `0003aa0f` each load one of `MissionParams_Ammo`, `MissionParams_Damage`, `MissionParams_Difficulty`, `MissionParams_Visibility` and `MissionParams_TimeOfDay` as the selected word and jump to the common tail `0003abfd`, `MTStrListBox_Ctor`(0, `0x100`, 0, the alignment, the word, the style, the id, the lines, the rect) and `DUIWindow_AddControl` ([the kinds](../formats/dlg-menu-dialogs.md#the-kinds)). The alignment is a local the kind's code zeroes before the lookup, which these five leave 0, left-aligned ([the list box](menu-controls.md#the-list-box)). Their lines are `Menu_ListLines`' cases for them (`0003765c`, `0003766d`, `000376a0`, `0003767e`, `0003768f`), each `push` of a word list, `WaText_LinesFromList` and `RET 4`, the list's `int16` words, −1-ended, naming lines of `eng\watext.bin`:

| List | Word | Word list | Lines | Texts, value 0 first |
|---|---|---|---|---|
| 1116 | AMMO, `MissionParams_Ammo` | `000811c4` | 8, 9 | LIMITED, UNLIMITED |
| 1117 | DAMAGE, `MissionParams_Damage` | `000811cc` | 10, 11 | VULNERABLE, INVULNERABLE |
| 1118 | DIFFICULTY, `MissionParams_Difficulty` | `00081244` | 23, 24, 25, 26 | TRAINING, NO SWEAT, CHALLENGING, BRUTAL |
| 1119 | VISIBILITY, `MissionParams_Visibility` | `000811d4` | 12, 4 | NORMAL, LOW |
| 1120 | TIME OF DAY, `MissionParams_TimeOfDay` | `000811dc` | 13, 14, 15 | DAY, DUSK, NIGHT |

The lists overlap in the image: `000811c4`'s −1 is followed by a 0 word and then `000811cc`'s first word, and `000811d4`'s −1 by a 0 word and `000811dc`'s.

A list of style 1 draws every line in `nm_green` with no selection box and shows its lines from the selected one, as many as its 12-pixel height holds ([the list box](menu-controls.md#the-list-box)): `nm_green`'s cell height is 10 ([fonts](../formats/dfn-hfn-dci.md#fonts)), so the line height is 9 and each list shows one line, the value selected. A press on one selects nothing, and its release posts its id, which the handler and `Shell_ScreenIds` do not hold.

**The buttons.** `MissionParams_OnMessage` (`0002b4fe`), slot `+8` of `MissionOptionsAlert`'s vtable, looks the target up among the seven ids of the table at `00080a74`, its cases at `00080a90`.

- AMMO, DAMAGE, DIFFICULTY, VISIBILITY and TIME OF DAY (1111 to 1115) each add 1 to their word as an `int16` and take a signed remainder, by 2, 2, 4, 2 and 3: AMMO, DAMAGE and VISIBILITY compile it as `CDQ; XOR EAX,EDX; AND EAX,1; ADD EDX,EAX`, DIFFICULTY and TIME OF DAY as an `IDIV`. They select the new value in their list 1116 to 1120 with its slot `+0x24`, `MTStrListBox_SelectLine`, and store it as option 12 to 16 with `Prefs_SetOption` (`00008378`)(1, value, option), which takes the word in a dword and stores its low byte. No handler is registered for options 12 to 16 ([Open](#open)), so `Prefs_SetOption` only stores the byte. The screen stays: 1111 to 1115 are not in `Shell_ScreenIds`.
- CANCEL (99) copies the object's `+0x26`–`+0x2e` back into the five words and puts every option back from `Prefs_Committed` (`000aa260`) with `Prefs_RevertAll` (`00008316`)(1), then does what ACCEPT does.
- ACCEPT (100) copies the options into `Prefs_Committed` with `Prefs_CommitAll` (`000082b0`)(1), writes all 17 to `data\prefs.cfg` with `Prefs_Save` (`00008267`), and makes the target 1100.

So both buttons return to the SINGLE MISSION menu and both write `data\prefs.cfg`, CANCEL with the values the menu opened with. The handler sets the target before `Shell_HandleMessage`'s *back* step, so CANCEL's 99 never becomes `Shell_PreviousScreen`. How the five words reach the mission is [the mission parameters](../formats/msn-mission-file.md#the-mode-and-the-mission-parameters).

### The options

**The shell keeps the 17 bytes of `data\prefs.cfg` as options, a handler each, and a committed copy that CANCEL goes back to.** `Prefs_Load` (`000081ca`) reads the 17 bytes through `Path_ApplyAltDrive` into `Prefs_Options` (`000aa24e`), zeroes them when the file does not open, and for each option calls its handler (`Prefs_Handlers`, `000aa272`) when one is registered and then copies it into `Prefs_Committed` (`000aa260`). At start-up `Shell_LoadData` (`00020630`) copies options 12 to 16 into the five words ([the shared data](startup.md#the-shared-data)), and the MISSION PARAMS menu copies them in again each time it opens. `Prefs_SetHandler` (`00008659`)(handler, option) stores a handler in `Prefs_Handlers` at the option's index, as `ES1DBSIM`'s `Prefs_SetHandler` (`ES1DBSIM 0004ac4c`) does in its own table. The other four differ from the simulator's ([the option array](../simulation/preferences.md#the-option-array)):

| `ES1GO` | Does | `ES1DBSIM`'s |
|---|---|---|
| `Prefs_SetOption` (`00008378`), (apply, value, index) | stores the value's low byte, then with apply set calls the option's handler, when there is one, with it | `Prefs_SetOption` (`0004a914`), which first keeps the old value in `Prefs_PreviousOptions`; the shell keeps no previous copy |
| `Prefs_CommitAll` (`000082b0`), (apply) | for each option that differs from `Prefs_Committed`, with apply set its handler when there is one, then the committed copy updated | `Prefs_CommitOptions` (`0004a83c`), with `Prefs_LoadedOptions` for `Prefs_Committed`, which then copies every option into `Prefs_PreviousOptions` |
| `Prefs_RevertAll` (`00008316`), (apply) | for each option that differs from `Prefs_Committed`, the committed value put back and, with apply set, its handler called with it | the same statements as `0004a8a8` (`es2_naming.py diff` differs only in the loop's shape and the names) |
| `Prefs_Save` (`00008267`) | writes the 17 options outright to `data\prefs.cfg`, through `Path_ApplyAltDrive`, with a `FileWStream`, when it opens | `Prefs_SaveSelectedOptions` (`0004ab6c`), which reads the file first and copies only the named options over it |

So the start-up's `Prefs_SetOption`(1, 0, *n*) of options 0, 1 and 2 with no sound manager ([the devices](startup.md#the-devices)) runs after `Prefs_Load` has made the committed copy, which keeps the file's values. MISSION PARAMS' CANCEL then puts options 0 to 2 back to the file's values with `Prefs_RevertAll`, and ACCEPT commits their 0s and `Prefs_Save` writes MUSIC, SOUNDS and RADIO off into `data\prefs.cfg`. `es2_xref.py` finds two other calls of `Prefs_CommitAll`, both on screens the player opens on purpose: the PREFS menu's handler `0002b82b` (screen 1310, [the screens](menus.md#the-screens)), followed there by `Prefs_Save`, and `000064c4`, slot `+0x10` of the vtable `0007dddc` that `00006050` stores in the panel screen 1320's code builds (`0003eab3`), on its case 7 and with no save. Either one, run before MISSION PARAMS, makes the 0s the committed copy, and then CANCEL writes options 0 to 2 off as well. With a sound manager the start-up does not clear them, and neither button changes them.

## Functions

| ES1 | Address |
|---|---|
| `MissionMenu_OnMessage`, `MissionParams_OnMessage` | `0002b375`, `0002b4fe` |
| `MissionMenu_Dtor`, `MissionOptionsAlert_Dtor` | `0003bfca`, `0004021b` |
| `SingleMission_Start`, `SingleMission_BuildPath` | `00036807`, `00036649` |
| `Prefs_SetHandler`, `Prefs_SetOption`, `Prefs_RevertAll`, `Prefs_CommitAll`, `Prefs_Save` | `00008659`, `00008378`, `00008316`, `000082b0`, `00008267` |

## Globals

| | Address |
|---|---|
| `Mission_Kind`, `int16`: the list's line, or header word 3 after a load | `000aad5a` |
| `MissionParams_Ammo` to `MissionParams_TimeOfDay`, five `int16` | `000aacbc`–`000aacc4` |
| the case tables of `MissionMenu_OnMessage` and `MissionParams_OnMessage` | `00080a54`, `00080a74` |

## Open

- **Open:** what reads options 12 to 16 through a handler: `es2_xref.py --decode` on `Prefs_Handlers` finds writes only in `Prefs_ClearHandlersAndInit`'s zeroing loop and in `Prefs_SetHandler`, whose one call `es2_xref.py` finds is `Prefs_Init`'s, for option 11 ([the devices](startup.md#the-devices)).
- **Open:** a retail check under DOSBox-X: SINGLE MISSION shows the seven types with the pointer on BRIEFING >; `Down` moves the red line; RETURN TO MAIN on `Esc`; select STRIKE, BRIEFING >, then CANCEL on the war map, and the list comes back on STRIKE; select SCOUT, BRIEFING >, CANCEL, and the list comes back on the line of the loaded mission's header word 3.
