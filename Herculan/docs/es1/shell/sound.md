# GO.EXE — the shell's sounds

**The front end makes its sound manager from `resource.cfg` at start-up, as the simulator does, with no catalog: it names 20 files and makes one sound, the buttons' click.** Addresses are `ES1GO` image addresses ([the X-32 executable](../formats/x32-executable.md#the-image)). The manager is the simulator's code ([the copies](../sound/sound-manager.md#the-copies)).

## Making the manager

`ShellSound_Init` (`ES1GO 00046235`; `es2_xref` finds its one call, from `Shell_InitDevices`, `ES1GO 00045d24`):

1. returns at once while `Shell_DebugMode` (`ES1GO 0007fd08`) is set, leaving no manager;
2. opens `resource.cfg`, its name through `Path_ApplyAltDrive`, as a `FileRStream`, and asserts `ERROR: Run soundset first.` (`src\system.cpp`, line `0x151`, severity 5) when it does not open;
3. reads `[SoundFX]` `Driver#`, `IRQ`, `DMA` and `PortAddress` and `[Music]` `Driver#` and `PortAddress` through the shell's `Profile_GetString` and `Profile_GetInt` ([the shell's copy](../formats/profile-files.md#the-shells-copy)), each with a default of 0: `IRQ` and `DMA` as numbers, the other four as strings read by `sscanf` with `%X`, so one of the four keys or its section missing from the file faults the shell ([reading a value](../formats/profile-files.md#reading-a-value)). An empty `[SoundFX]` `Driver#` is taken as 0 before `sscanf`; the other three strings reach `sscanf` empty or not, so an empty one leaves its value as the stack held it;
4. closes the file, and with the `[SoundFX]` device 0 returns, `ShellSound_Manager` (`ES1GO 00082768`) left 0;
5. otherwise makes the manager with `SoundManager_Ctor`, stores the six values over `SoundDriver_Defaults` (`ES1GO 00082310`) as `Sound_Init` stores its arguments, and runs `SoundManager_LoadDrivers` without reading its answer;
6. sets the master volume to 95, where the simulator's stays 90, and the memory limit to 200,000, and gives the manager the 20 names at `ShellSound_Names` (`ES1GO 000828c0`) with `SoundManager_SetSampleNames`;
7. makes the click, `ShellSound_ButtonSound` (`ES1GO 0007fd00`), with `SoundManager_CreateObject`(manager, 2, 0, 9) and loads it at once.

It sets no hearing range. `Shell_ShutdownDevices` destroys the click and then the manager ([the shutdown](startup.md#the-shutdown)).

## The shell's names

| Index | Name | Plays |
|---|---|---|
| 0 | `res32\xxxxxxxx.sfx` | a talking head's line: `ShellSnc_OpenSound` renames index 0 `snc\` followed by the line's `.RAW` for each line and makes a sound on it ([loading a line](message-box.md#loading-a-line)) |
| 1–8 | `res32\menu.hmp` twice, `res32\shella.hmp`, `res32\shellb.hmp`, `res32\shellc.hmp`, `res32\shella.hmp`, `res32\death.hmp`, `res32\win2.hmp` | songs, by the track `ShellMusic_Play` is given: 1, 3 and 4 the backdrop groups' ([the backdrop](menus.md#the-backdrop)), 7 the lost pilot's ending ([the first screen](menus.md#the-first-screen--shell_openfirstscreen-0003ba47)) |
| 9 | `res32\button.sfx` | the buttons' click |
| 10, 11 | `res32\menuclsa.raw`, `res32\menuopn2.raw` | the animations' sounds ([playing](../formats/anm-animation.md#playing)) |
| 12, 13 | `res32\intro1.hmp`, `res32\intro2.hmp` | songs: the intro's ([the start-up](startup.md#the-start-up)), 13 the won career's ending too |
| 14 | `res32\xxxxxxxx.raw` | a script's sound: `TOKEN_HDS_LOAD` renames index `0xe` to the script's path for its name and makes a sound on it ([the commands](../formats/ttm-scripts.md#the-commands)) |
| 15–19 | `res32\rubble.hmp`, `res32\ice.hmp`, `res32\desert.hmp`, `res32\volcano.hmp`, `res32\losing.hmp` | songs: 15–18 a campaign's opening lines' ([the kinds](message-box.md#the-kinds)), 19 the lost career's ending |

A 0 dword at `ES1GO 00082910` ends the table. `SHELL0.VOL` holds all 20 files, the placeholders `RES32\XXXXXXXX.SFX` and `RES32\XXXXXXXX.RAW` 804 bytes each, so `SoundManager_SetSampleNames` takes every name, `+0x502` is 19, and the click's index 9 passes `SoundManager_CreateObject`'s test.

## The click

**Every button of the front end clicks the same sound, `res32\button.sfx`, while the click is switched on.** A menu's text or bitmap button stops `ShellSound_ButtonSound` when it plays and plays it again, while `Shell_SoundOption` is set ([drawing a control](menu-controls.md#drawing-a-control)). The buttons of the shell's copy of the simulator's panels ([the panel base](options.md#the-panel-base--alertpanel_ctorbase-0000222d)) click through `Widget_ClickSoundAlertCopy` (`ES1GO 00008954`), slot `+0xc` of their mixin tables `ES1GO 0007da66` and `0007dac2`: while option 1 of `Prefs_Options` (`ES1GO 000aa24e`) is set it calls `ShellSound_PlayClick` (`ES1GO 0004dde4`)(`0x11`), which plays `ShellSound_ButtonSound` through its slot `+0x10` for row `0x11`, with no stop first, and does nothing for any other row. The simulator's original plays the catalog's row `0x11` instead ([the gadget bases](../simulation/cockpit-widgets.md#the-gadget-bases)).

## The speech archive

`Speech_ArchivePresent` (`ES1GO 000087ce`) returns 1 when `vol\speech.vol`, the `.RAW` lines' archive, opens; `es2_xref` finds its one call, from the shell's `Prefs_Init` (`ES1GO 00008920`), which keeps the answer in `Speech_ArchiveFound` (`ES1GO 0007e09c`) and sets option 2 to 0 when it is 0.
