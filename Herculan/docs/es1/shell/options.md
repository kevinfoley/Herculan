# GO.EXE — PREFERENCES and CONTROLS

**OPTIONS' PREFERENCES and CONTROLS change the settings kept in `data\prefs.cfg`: PREFERENCES the shell's music, the click of its buttons, the simulator's detail and the pilots' speech, CONTROLS the joystick and its add-ons. Both go back to OPTIONS** ([the options menu](main-menu.md#the-options-menu)).

Addresses are `ES1GO` image addresses ([the X-32 executable](../formats/x32-executable.md#the-image)). The functions here remove their own arguments, so argument lists are in stack order ([calling conventions](../formats/compiler.md#calling-conventions)). The shell's 17 options are `Prefs_Options` (`000aa24e`), byte *n* option *n* of `data\prefs.cfg`, with a committed copy `Prefs_Committed` (`000aa260`); what each option means to the simulator is [its option array](../simulation/preferences.md#the-option-array).

## PREFERENCES — screen 1310

**A window titled PREFERENCES with four settings, each a button that steps it and a box beside it showing its value: MUSIC and SOUND, OFF or ON; DETAIL, LOW, MEDIUM, HIGH or MAX; SPEECH, TEXT ONLY, VOICE ONLY or TEXT AND VOICE. A change takes effect at once; ACCEPT writes the settings to `data\prefs.cfg`, CANCEL puts back the ones the window opened with and writes those, and either goes back to OPTIONS.** Without a sound manager MUSIC, SOUND and SPEECH are inactive, and without `vol\speech.vol` SPEECH does nothing.

`dlg\menu1310.dlg`'s window is (49, 50)–(281, 165). The pointer starts on ACCEPT, the control keyed `Space` ([opening a menu](menus.md#opening-a-menu--menu_open-0003969e)).

| Control | Id | Rect | Key |
|---|---|---|---|
| MUSIC | 1311 | (27, 27)–(75, 39) | |
| its box | 1314 | (77, 27)–(102, 39) | |
| SOUND | 1312 | (27, 41)–(75, 53) | |
| its box | 1315 | (77, 41)–(102, 53) | |
| DETAIL | 1313 | (129, 27)–(179, 39) | |
| its box | 1316 | (116, 41)–(192, 53) | |
| SPEECH | 1317 | (27, 63)–(75, 75) | |
| its box | 1318 | (77, 63)–(192, 75) | |
| CANCEL | 99 | (112, 86)–(159, 98) | `0x01`, `Esc` |
| ACCEPT | 100 | (160, 86)–(207, 98) | `0x39`, `Space` |

The buttons are text buttons (kind 3) and the boxes list boxes of style 1 (kind 15) ([the kinds](../formats/dlg-menu-dialogs.md#the-kinds)).

### Opening it

Screen 1310's case of `Shell_HandleMessage` (`0003e7eb`) allocates a `0x2c`-byte `PrefsMenu` (vtable `00081f60`, handler `PrefsMenu_OnMessage`, destructor `PrefsMenu_Dtor` (`000402d2`)): [the screen object](menus.md#the-screen-object) and four fields copied from the shell's settings, which CANCEL puts back.

| Offset | Copy of |
|---|---|
| `+0x26` | the byte `Shell_MusicOption` (`0007fcb8`) |
| `+0x27` | the byte `Shell_SoundOption` (`0007fcb9`) |
| `+0x28` | the `int16` `Shell_DetailOption` (`0007fcba`) |
| `+0x2a` | the `int16` `Shell_RadioOption` (`0007fcbc`) |

`Shell_LoadData` sets the four from `prefs.cfg` at the shell's start ([the shared data](startup.md#the-shared-data)). `Menu_Open`, with no sound manager (the dword `00082768` 0), makes MUSIC, SOUND and SPEECH inactive, each found in any state with `DUIWindow_FindControlById` (`0001be16`) and given state 2 at `+0x19` and 1 at `+0x1f`. The boxes' lines are word lists of `eng\watext.bin` lines from `Menu_ListLines` (`0003750d`), and each box's selected line is its setting ([the build](menus.md#opening-a-menu--menu_open-0003969e), kind 15):

| Box | Lines | Selected line | Alignment |
|---|---|---|---|
| 1314 | `PrefsMenu_OffOnLines` (`000811a8`): OFF and ON, lines 2 and 3 (`000376b1`) | `Shell_MusicOption` (`0003a973`) | 0 |
| 1315 | the same list (`000376b1`) | `Shell_SoundOption` (`0003a986`) | 0 |
| 1316 | `PrefsMenu_DetailLines` (`000811b0`): LOW, MEDIUM, HIGH and MAX, lines 4 to 7 (`000376c2`) | `Shell_DetailOption` (`0003a999`) | 2, centered |
| 1318 | `PrefsMenu_SpeechLines` (`000811bc`): TEXT ONLY, VOICE ONLY and TEXT AND VOICE, lines `0x10` to `0x12` (`000376d3`) | `Shell_RadioOption` (`0003a9b5`) | 0 |

Kind 16's tables name 1316 as well, with `res32\bluebar.dba` and the word `Shell_DetailOption`, and 1321 with `res32\controls.dba` ([the build](menus.md#opening-a-menu--menu_open-0003969e)); in `dlg\menu1310.dlg` 1316 is a list box, and of the 39 `.DLG` files in `SHELL0.VOL` none holds 1321, nor 1325 to 1327, which kind 15's table names (`HercWorks.Query menu`).

### The buttons — `PrefsMenu_OnMessage` (`0002b82b`)

`PrefsMenu_OnMessage`(menu, message) looks the message's target `+6`, the pressed button's id, up among the six `int16` at `00080aac` with `REPNE SCASD` and jumps to the target beside it (`00080ac4`, case bodies Ghidra left undefined); any other target returns at once. A setting's button changes the setting, selects its line in the box through the box's slot `+0x24` (`MTStrListBox_SelectLine`, [the list box](menu-controls.md#the-list-box)), the box found with `DUIWindow_FindEnabledControlById` (`0001bda4`), and stores the setting as an option with `Prefs_SetOption` (`00008378`)(1, value, option). That runs the option's handler, and the shell registers one only for option 11 ([the start-up](startup.md#the-start-up), step 9). The target, a button's id, is not a screen, so the window stays ([changing screens](menus.md#changing-screens--shell_handlemessage-0003c7d8)).

| Button | Case | The setting | Then |
|---|---|---|---|
| MUSIC | `0002b8b3` | `Shell_MusicOption` XOR 1, box 1314 | with the backdrop's music `ShellMusic_Object` (`0007fcfc`, made by `ShellMusic_Play`, [the backdrop](menus.md#the-backdrop)) set, plays it through its slot `+0x10` when the setting is now 1 and stops it through slot `+0x14` when 0 (the vtable at `+0x3b`, [sound objects](../formats/sound-samples.md#sound-objects)); option 0 |
| SOUND | `0002b854` | `Shell_SoundOption` XOR 1, box 1315 | option 1 |
| DETAIL | `0002b961` | `Shell_DetailOption` + 1, modulo 4, box 1316 | options 4, 5 and 6, SKY, GROUND and SHAPES, each the setting × 64 + `0x3f`: `0x3f`, `0x7f`, `0xbf` or `0xff`; then option 3, HORIZON, 0 for LOW and 1 otherwise |
| SPEECH | `0002ba18` | while the byte `Speech_ArchiveFound` (`0007e09c`) is set: `Shell_RadioOption` + 1, modulo 3, box 1318 | option 2 |
| CANCEL | `0002ba91` | the four copied back from `+0x26` to `+0x2a` | the music played or stopped as MUSIC does, for the byte put back; `Prefs_RevertAll` (`00008316`)(1), every option back to its committed copy; then ACCEPT's code |
| ACCEPT | `0002bb0d` | | `Prefs_CommitAll` (`000082b0`)(1), every option made the committed copy; `Prefs_Save` (`00008267`), the 17 options to `data\prefs.cfg`; and the message's target set to 1300, OPTIONS |

So both CANCEL and ACCEPT write `data\prefs.cfg`, CANCEL with the committed copy as it stood before PREFERENCES opened. `Speech_ArchiveFound` is set by `Prefs_Init` from `Speech_ArchivePresent` (`000087ce`), 1 when `fopen` opens `vol\speech.vol` (mode `rb`, a path relative to the current folder), and without it `Prefs_Init` sets option 2 to 0 ([the start-up](startup.md#the-start-up), step 9).

What the settings reach in the shell: `Shell_MusicOption` gates the backdrop's music ([the backdrop](menus.md#the-backdrop)), `Shell_SoundOption` a text button's click ([drawing a control](menu-controls.md#drawing-a-control)), and `Shell_RadioOption` the message box's text and speech ([the shared data](startup.md#the-shared-data)). `es2_xref.py --decode` finds `Shell_DetailOption` read by the 1310 case, the box 1316's build and kind 16's case for 1316, and written by `Shell_LoadData` and this handler; the detail itself is the simulator's, from options 3 to 6.

### DETAIL after a restart

`Shell_LoadData` sets `Shell_DetailOption` at each start of the shell to (`0x3f`, or `0xbf` with option 3 set, plus options 4, 5 and 6) / 256, the bases the words `0007fd10` and `0007fd18` hold ([the shared data](startup.md#the-shared-data)). For the four settings DETAIL writes that gives 0, 2, 2 and 3: LOW, HIGH, HIGH and MAX, so a DETAIL left at MEDIUM shows HIGH the next time the shell starts, and the options keep MEDIUM's values until DETAIL is pressed again ([Open](#open)). Settings the simulator's own panel made, whose bars move options 4 to 6 one at a time ([the preferences panel](../simulation/preferences.md#the-preferences-panel--preferencespanel_ctor-00047578)), show as whatever level their sum reaches.

## Functions

| ES1 | Address |
|---|---|
| `PrefsMenu_OnMessage`, `PrefsMenu_Dtor` | `0002b82b`, `000402d2` |
| `Prefs_Save`, `Prefs_CommitAll`, `Prefs_RevertAll`, `Prefs_SetOption` | `00008267`, `000082b0`, `00008316`, `00008378` |
| `Speech_ArchivePresent` | `000087ce` |

## Globals

| | Address |
|---|---|
| `Shell_MusicOption`, `Shell_SoundOption`, bytes: `prefs.cfg` options 0 and 1 | `0007fcb8`, `0007fcb9` |
| `Shell_DetailOption`, `int16` 0 to 3 | `0007fcba` |
| `Shell_RadioOption`, `int16`: option 2 | `0007fcbc` |
| `Speech_ArchiveFound`, byte | `0007e09c` |
| `ShellMusic_Object`, the backdrop's music | `0007fcfc` |
| `PrefsMenu_OffOnLines`, `PrefsMenu_DetailLines`, `PrefsMenu_SpeechLines`, −1-ended `int16` lists | `000811a8`, `000811b0`, `000811bc` |

## Open

- **Open:** DETAIL's MEDIUM read back as HIGH ([DETAIL after a restart](#detail-after-a-restart)). A retail check: in PREFERENCES set DETAIL to MEDIUM, ACCEPT, quit to DOS, start the game again and open PREFERENCES: the box shows MEDIUM or HIGH.
