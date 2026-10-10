# Command lines: ES.EXE, VSHELL and DBSIM

How the three retail executables start one another and what every switch each one parses does. The player-facing summary is [`launch-options.md`](launch-options.md); HERCULAN's own flags are in [`../herculan/herculan-command-line.md`](../herculan/herculan-command-line.md). Addresses name their binary and are v1.0's; v1.10's launcher is the one difference this doc covers ([`retail-builds.md`](retail-builds.md)). `ES.EXE` is not in the Ghidra project, and both builds' code was read whole from a direct disassembly. <!-- doc-lint: ok -->

## ES.EXE — the supervisor

`ES.EXE`'s `WinMain` (`00401168`, ES.EXE) parses its own command line, checks memory, and then runs the shell and the simulator in turn with `spawnv(P_WAIT, …)`, feeding each one's exit code back in as the next state.

### Its switches

Case-sensitive, tested on the character after `-`:

| Switch        | Effect                                                                                                                              |
| ------------- | ----------------------------------------------------------------------------------------------------------------------------------- |
| `-s`          | Toggles no-sound: adds `-s` to both child command lines                                                                             |
| `-a`          | Toggles `-a` on the shell's command line                                                                                            |
| `-L`          | Toggles `-l` (low-memory mode) on the simulator's                                                                                   |
| `-r<name>`    | Adds `-r<name>` to the simulator's: record every mission to a tape                                                                  |
| `-SPRUNKNOWN` | Exact match. Toggles the developer mode: adds `-@` to the shell's command line and `-SPRUNKNOWN` to the simulator's                 |
| `-X`          | Toggles the simulator-only loop below — only if `-SPRUNKNOWN` came earlier on the line, since the case tests that flag as it parses |
| `-D<n>`       | Parsed with `atol`; the result is discarded                                                                                         |

### Memory check

`GlobalMemoryStatus`, then `GlobalAlloc` probes from 20,000,000 bytes down in steps of 200,000 until one succeeds. Under `0x73a000` bytes of physical memory it warns "You must have at least 8 megs…" and carries on. With no page file, a page file under 8,000,000 bytes, or free page file plus the probe short of 18,000,000 bytes (12,000,000 under `0xdac000` of physical memory), it shows its own message and quits.

### The loop

The state word at `00402070` (ES.EXE) starts at 1 and is replaced by each child's exit code:

| State | Runs |
|---|---|
| 0 | Nothing — `ES.EXE` exits |
| 1, 3, 4, 6 | `vshell dummy -eggplant -X<state> [-s] [-a] [-@]` |
| 2 | `dbsim dummy -eggplant -X<state> -R<n> [-s] [-l] [-r<name>] [-SPRUNKNOWN]` |
| 5 | The same, with `-D` |

`<n>` counts simulator launches from 0 within one run of `ES.EXE`, so successive missions play CD tracks 2, 3, 4, 5, 6, 2… ([`simulation/audio.md`](simulation/audio.md#which-track-and-whether-there-is-one)). The simulator's list also has slots for `-m` and `-Z`; their conditions test a local initialised to 0 and one initialised to 1 that nothing afterwards writes, so neither is ever passed. In the simulator-only loop every pass forces the state to 2, so the simulator reruns the mission already in `data\` and the shell never starts; an exit code of 0 still ends it.

### v1.10's language switch

v1.10's `VER95\ES.EXE` is v1.0's with two additions after the switch parse. It reads the first byte of `data\language.cfg`, which the installer wrote. `E`, or no file, changes nothing; any other byte becomes a switch, `-` and that byte, appended to both lists after their last optional slot. The installer writes only `E`, `F` and `G`, so a French install launches `vshell … -F` and `dbsim … -F`, and each program reads its own French switch from it ([`retail-builds.md`](retail-builds.md#how-a-language-is-chosen)). It then loads `error.str` as a string table and takes its memory-check messages from it; without the file it shows `Missing string resource: error.str.` and exits.

### Exit codes

| Code | Set by | Meaning to `ES.EXE` |
|---|---|---|
| 0 | the shell quitting ([`QUIT`](shell/main-menu.md#quit) or closing its window); DBSIM when `004d2582` is set, which `Ctrl+Q`'s `EXIT EARTHSIEGE?` confirmation does — the panel closing the simulator's window raises too | quit |
| 2 | VSHELL `Shell_SetExitCode(2)` (`0040876a`) — the mission launch paths, including `MissionNameDialog_OnLoad` (`0044d5bd`, VSHELL) and the debrief's `REPLAY MISSION?` | fly a mission |
| 3 | DBSIM when a mission ends other than by a quit or a demo | shell, into the debrief |
| 4 | DBSIM in place of 3 when the player's machine is destroyed (`+0x99`) and `MissionModeFlag` (`004a9ed6`) is up — which the load zeroes, so never | shell, into the debrief |
| 5 | VSHELL `FUN_0043156f` — the main menu's `VIEW DEMO` button | fly a demo tape |
| 6 | DBSIM after a demo (`DemoMode`, `004d25b4`) | shell |

DBSIM returns its code from `WinMain` out of `004d283c`, written in `Sim_Shutdown` (`00461eec`, DBSIM) after the mission's results ([`simulation/mission-objectives.md`](simulation/mission-objectives.md#what-the-mission-leaves-the-shell--mission_writeresults-0042412c)). The shell side of `-X3`/`-X4` and `-X6` is [`shell/campaign-loop.md`](shell/campaign-loop.md).

## VSHELL

`Shell_ParseCommandLine` (`0040107c`, VSHELL) parses the switches; each accepts `-` or `/` and either case, but `-@` and `-X`, which are tested in one case only. `EsGlobal_Init(0)` (`004073bc`) sets the switch block to its defaults immediately before ([`shell/startup.md`](shell/startup.md#the-startup--shell_main-00401525)).

| Switch | Store | Effect |
|---|---|---|
| `-eggplant` | `Shell_EggplantGiven` (`0046c084`) = 1 | Without it the shell shows "You cannot run this exe directly" and quits |
| `-e…` other than `-eggplant` | `Shell_Language` (`0048227a`) = 3 | Language slot 3; see [Open](#open) |
| `-f`, `-g` | `Shell_Language` = 1, 2 | French, German, over the value `EsGlobal_Init` copies from [`prefs.cfg` byte 43](simulation/preferences.md#what-each-byte-is); what it selects is [`retail-builds.md`](retail-builds.md#how-a-language-is-chosen)'s |
| `-s` | `Shell_SoundEnabled` (`00482272`) = 0 | No sound |
| `-m` | `ShellSwitch_M` (`00482270`) = 1 | The one reader `es2_xref.py` finds builds [the unreferenced simulator command line](#vshells-unreferenced-simulator-command-line); no effect ([Rejected readings](#rejected-readings)) |
| `-k` | `Shell_KeyboardEnabled` (`00482271`) = 0 | No keyboard: the startup builds no keyboard producer |
| `-X<n>` | `Shell_SetExitCode(n)` → `0046e210` | Copied into `Shell_StartupCode` (`0048227e`) right after the parse; see [`shell/campaign-loop.md`](shell/campaign-loop.md) |
| `-r` | `Shell_StartupCode` = 3 | Overwritten by the `-X` copy; no effect ([`shell/campaign-loop.md`](shell/campaign-loop.md#rejected-readings)) |
| `-@` | `Shell_MissionPickerEnabled` (`00482284`) = 1 | The mission picker below |
| `-a` | `Shell_MoviesEnabled` (`00482275`) = 0 | Turns the shell's movies off: [the movie queue](shell/movies-and-sound.md#the-shells-movies) takes nothing and plays nothing |
| `-l` | `ShellSwitch_L` (`00482280`) = 0 | The one reader `es2_xref.py` finds builds [the unreferenced simulator command line](#vshells-unreferenced-simulator-command-line); no effect |
| `-v`, `-?` | `Shell_SoundEnabled` = 0 | `printf` the version or the usage text, turn sound off, and call `Shell_ShutdownDevicesAndSound` (`004092dc`). The parse runs before `Shell_Main` (`00401525`) builds `devices.cpp`'s viewport (`Devices_Init`, `0040db38`) and the sound manager, so that call releases nothing, and the parse goes on to the next argument |

`-d`, tested separately in VSHELL's `Shell_WinMain` (`00406507`), clears `Shell_StartedFullScreen` (`0046d740`), which the same function overwrites from `ShellOption_DisplayMode` before anything reads it.

### VSHELL's unreferenced simulator command line

`Shell_BuildSimArgv` (`0042f2e8`) builds a DBSIM command line from the shell's own switches, but `es2_xref.py` finds no branch, stored pointer or vtable slot reaching it, and it spawns nothing: it returns the argument count and drops the array. `ES.EXE` [launches DBSIM](#the-loop) with its own list. What it builds:

- Nothing unless `ShellSwitch_L` (`00482280`), which `-l` clears, and the exit code (`Shell_GetExitCode`, `00408778`) are both non-zero.
- When `Shell_StartedFullScreen` (`0046d740`) is set, it first releases DirectDraw (`Display_ReleaseDirectDraw`, `00407011`).
- Then a 16-pointer array (`Mem_NewArray(0x40)`): `dummy` and `-eggplant`; `-Z` when `Shell_StartedFullScreen` is clear; `-s` when `Shell_SoundEnabled` is clear; `-v3` and `-h` always; `-F` when `Shell_Language` is 1 and `-G` when it is 2; `-m` when `ShellSwitch_M` is set; `-D` when `ShellSwitch_D` (`00482282`) is non-zero; then an empty string.

To DBSIM's parsers below, that `-Z` is windowed, so the simulator would open as the shell started; `-v3` is video mode 3; `-m` is the monochrome-monitor driver, not VSHELL's mouse switch; and `-D` plays a tape. Neither parser has an `h` case. The only store to `ShellSwitch_D` `es2_xref.py` finds is `EsGlobal_Init`'s 0.

### `-@`: the mission picker

`-@` leaves up [the mission-name dialog](shell/main-menu.md#the-mission-name-dialog), titled `DEBUG`, which every career's mission load goes through and which the shell otherwise clicks past itself before it is drawn: its `Use Default` loads the career position's mission and its `ACCEPT` a typed `msn\<name>.msn`. Retail confirms both halves: `ES.EXE -s -SPRUNKNOWN` puts the dialog up on starting a new game, and a normal launch never shows it.

## DBSIM

Two parsers. `WinMain_ParseSwitches` (`0045e6b0`) (DBSIM) runs first from `WinMain`, after `VideoMode_Configure(0)`, for the switches the window needs: `-v`, `-Z`, `-X`, `-c`. `Sim_ParseCommandLine` (`0045e73c`) runs later for the rest. Both are case-sensitive and test the character after `-`; neither accepts `/`.

| Switch | Store | Effect |
|---|---|---|
| `-eggplant` or `-EGGPLANT` | `004d25a8` = 1 | Without it `WinMain` shows "dbsim.exe is not meant to be run…" and exits |
| `-v<d>` | `VideoMode_Configure(d)` | [`simulation/cockpit-views.md`](simulation/cockpit-views.md#video-modes) |
| `-Z1`, `-Z0`/`-Z` | `004d25e2` | Full screen or windowed; [`simulation/preferences.md`](simulation/preferences.md#the-video-mode-and-full-screen-bytes) |
| `-b` | `Display_UseScrollWindow` = 0 | [`simulation/cockpit-views.md`](simulation/cockpit-views.md#the--b-paged-path) |
| `-S` | `CmdLineSwitch_S` = 1 | No effect; [`simulation/cockpit-views.md`](simulation/cockpit-views.md#the--b-paged-path) |
| `-SPRUNKNOWN` | `DAT_0049ef60` toggled | The developer keys below |
| `-s` | `004d254c` toggled from 1 | `Sound_Init(0)`: no sound driver |
| `-R<n>` | `Music_TrackSelect` | [`simulation/audio.md`](simulation/audio.md#which-track-and-whether-there-is-one) |
| `-E`, `-F`, `-G` | `004d25ba` = `s`, `f`, `g` | The language letter, `r` by default. `Voice_ArchiveName` (`0045ef68`) puts it last in `simvoice` unless it is `r`, and `Language_StringFilePath` (`0045ef00`) puts it last in `str`, giving the `st<letter>\` string folder ([`retail-builds.md`](retail-builds.md#how-a-language-is-chosen)). `s` is Spanish: `SIMALERT.VOL` has an `STS\` folder, and no `SIMVOICS.VOL` ships in either build |
| `-l` | `CockpitArt_LoadOnDemand` = 1 | [`simulation/audio.md`](simulation/audio.md#memory-budget-and-eviction), [`rendering/terrain-texturing.md`](rendering/terrain-texturing.md#base-formation-pads) |
| `-C<name>` | `CockpitOverride_Index` (`0049ac4c`), `CockpitOverride_Name` (`0049ac50`) | `_stricmp` against 13 names at `0049ac64`: `ROADRUNNER`, `OUTLAW`, `RAPTOR2`, `TOMAHAWK`, `PATRIOT`, `PANTHER`, `SAMSON`, `COLOSSUS`, `APOCA`, `RAZOR`, `MAVERICK`, `OGRE`, `TEST3`. A match replaces the herc index and name the cockpit view manager takes from the player's machine (`+0x27`, `+0x2d`), and the name the canopy-crack art is built from |
| `-t<n>` | `CommBox0PilotOverride` | `HddGauge_LoadPilotFrames` (`0044a7c0`) takes it as the pilot index of squad comm box 0 when it is non-negative; [`simulation/heads-down-display.md`](simulation/heads-down-display.md#squad-comm-boxes) |
| `-r<name>`, `-p<name>`, `-D` | | [`formats/tap-input-tape.md`](formats/tap-input-tape.md#the-switches) |
| `-d` | `004d2562` | Opens the checkpoint file `<tape stem>.dmp`; [`formats/tap-input-tape.md`](formats/tap-input-tape.md#the-checkpoint-file) |
| `-B` | `004d25b0` = 1 | In `Sim_HandleWindowKey` (`0045fd60`): `Ctrl+B` (`0x430`) calls `__break` (`004679d4`), an `INT3`; and `Alt+Enter` (`0x21c`) stops toggling full screen while a tape plays |
| `-m` | `004d2701` = 1 | `Sim_InitMissionSession` sends control code 5 to `\\.\DARKMONO.VXD` (`maybe_Mono_Clear`, `004954b8`), a developer's monochrome-monitor driver that does not ship |
| `-P` | block `+0x0d` = 1 | No effect. Its three readers — `0045f2ab` and `0045f37b` in `Sim_Run` (`0045f144`), `00461f86` in `Sim_Shutdown` (`00461eec`) — are each a `CMP` followed by an instruction that overwrites the flags or a `CALL`, with no branch between |
| `-T<n>`, `-V<n>`, `-W<n>` | block `+0x5a`, `+0x56`, `+0x58` | `Main_StaticInit` sets all three to -1 |
| `-a` | block `+0x7d` = 0 | `Main_StaticInit` sets it to 1 |
| `-c` | block `+0x72` = 1 | |
| `-X<n>` | `004d283c` | Zeroed by `Sim_Run` (`0045f144`) before `Sim_ParseCommandLine` runs; no effect |

"Block" is the `0xc3`-byte global block at `004d2540` ([`simulation/cockpit-views.md`](simulation/cockpit-views.md#video-modes)). For `-T`, `-V`, `-W`, `-a` and `-c`, three searches find only the stores above ([Open](#open)): `es2_xref.py` on the five addresses, which finds one dword each in the whole PE, the parser's own; every absolute operand from `004d2590` to `004d25bf`, which also rules out a wider load overlapping one of these fields; and the displacements off the base in the fifteen register holders and the three blit helpers it is pushed to, none of which spills, copies or rebases it. The same searches find the reads of the neighbouring `+0x54`, `+0x7b` and `+0x7c`. No `.EXE` of either build passes any of the five: `ES.EXE`'s simulator list above has none of them, and [VSHELL's unreferenced list](#vshells-unreferenced-simulator-command-line) has none either.

### `-SPRUNKNOWN`: the developer keys

`DAT_0049ef60` gates these commands. Codes are set-1 scancodes plus `0x200` for `Alt` and `0x400` for `Ctrl` ([`simulation/cockpit-input.md`](simulation/cockpit-input.md#keyboard-commands-are-scancodes)), and the key names are read from `VkToScancode`.

In `Sim_DispatchCommand` (`0045fdac`):

| Code | Key | Effect |
|---|---|---|
| `0x21f` | `Alt+S` | Toggles the simulation freeze `004d2576`. Also live while a tape records or plays, flag or no flag |
| `0x24e` | `Alt+keypad +` | Sets `004d2580` and clears the freeze; `Sim_MainTick` re-freezes on its next pass, so one frame runs |
| `0x431`, `0x419` | `Ctrl+N`, `Ctrl+P` | Next or previous object in `maybe_GlobalLiveObjectList` from `ViewChain_Viewed` (`004d2708`), skipping objects with `+0x2e` below -99000. Stored in `ViewChain_Chosen` (`004d25a0`) and either viewed through `ViewChain_ViewObject` (`0045df18`), or reached through an external-view command when the view `ViewChain_View` (`004d2572`) is 2. Sets `InputDrivesCamera` |
| `0x421` | `Ctrl+F` | `V`'s step (`0x2f`) with `ViewChain_FollowChosen` (`004d25b8`) set, except that from the outside view it goes to the free camera, and the cockpit it returns to rides `ViewChain_Chosen`'s eye rather than the player's — see [`simulation/external-views.md`](simulation/external-views.md#the-chain-of-views) |
| `0x414` | `Ctrl+T` | Toggles `InputDrivesCamera` (`004d2574`), which hands the controls to a camera: every reader tests it alongside the missile-camera flag `004d25aa`. While it is set, `Input_BuildPlayerDevice` re-points the axis sources, `Sim_PollPlayerInput` (`00460764`) gives the machine none of the steering, throttle or turret axes, hands the re-pointed ones to `Cam_Steer` (`00401c74`) on the view object ([`simulation/external-views.md`](simulation/external-views.md#steering-the-camera--cam_steer)) and skips `Mech_PlayerFireTick`, and `Mech_ApplyThrottleInput` ignores the throttle lever. `ViewChain_ViewObject`, which Ctrl+N/P view through, sets it whenever the viewed object is not `LocalPlayerMech` (`004d256a`) |
| `0x634`, `0x633` | `Ctrl+Alt+.`, `Ctrl+Alt+,` | Raise or lower the component index `004d2584`, 0 to 29 |
| `0x620` | `Ctrl+Alt+D` | 300 damage to that component of the viewed object `ViewChain_Viewed` — the player's machine until the camera moves — through vtable `+0x74` ([`simulation/component-damage.md`](simulation/component-damage.md)) |
| `0x631` | `Ctrl+Alt+N` | 32000 damage to component 0 of the first object in the live list whose group is deployed (`+0x14` null) and on side 1, Cybrid ([`formats/script-dat.md`](formats/script-dat.md#the-two-pass-read--and-what-it-means-for-dbsim-keeps)), that has `+0x99` clear and lies within 99,999 units of the player |

The freeze does not stop `Sim_MainTick`. It skips the effect, projectile, meteor and structure pools, the mech pool's `+0x14` walk that reaches `Mech_MovementTick` through `Behaviour_DispatchMove`, the group ticks, the action timers and triggers, `Sim_DetectionTick` and `Mech_PerTickSystemsUpdate`; `Razor_ApplyFlightInput` is gated on it too. `Sim_PollPlayerInput` and `Mission_PollStatus` run regardless. So a frozen player machine still fires through `Mech_PlayerFireTick`, twists and pitches its turret, and runs `Mech_ApplyThrottleInput`: the throttle still moves, and `Mech_LocomotionTick` still ramps the speed toward what it asks for and turns the machine by the turn-rate tent over that speed, less wherever its gait state machine suppresses turning. It covers no ground, the refire timers wait with the systems pass, and what it fires waits with the pools. A modal panel is a harder stop: its own loop never calls `Sim_MainTick`.

Every one of these keys, like every command, acts once per key-down event, so holding one repeats it at the keyboard's auto-repeat rate.

In `Mech_HandleCommand` (`004157c8`), on the player's machine:

| Code | Key | Effect |
|---|---|---|
| `0x248`, `0x250` | `Alt+Up`, `Alt+Down` | Moves the machine `±step` along its own y axis |
| `0x24d`, `0x24b` | `Alt+Right`, `Alt+Left` | `±step` along its own x axis |
| `0x44b`, `0x44d` | `Ctrl+Left`, `Ctrl+Right` | Adds or subtracts the angle step to its yaw |
| `0x602`-`0x60a` | `Ctrl+Alt+1`-`9` | Sets the step `DevKeys_MoveStep` (`004a9d50`) and angle step `DevKeys_TurnStep` (`004a9d52`) from two tables, `DevKeys_MoveStepTable` (`0049a020`) and `DevKeys_TurnStepTable` (`0049a032`), both 500, 1000, 1500, 2000, 3000, 4500, 6000, 7500, 9000 |

Both steps start at 2000, entry 3 of both tables: the mech module's static initialiser `Mech_RegisterSubsystem` (`0041bc5c`) registers `Mech_ResetMissionGlobals` (`00415464`) as subsystem phase 2, which `Sim_InitMissionSession` runs at every mission start, and that function loads the two entries. The handler is reached through `Sim_DispatchCommand`'s default case, which passes a command it does not claim to the `+0x2c` slot of the viewed object `ViewChain_Viewed` — `LocalPlayerMech` only when `DAT_0049ef5c` is set, and that dword is 0 in the image with only compares among the seven references `es2_xref.py` finds. `Mech_HandleCommand` works its weapon and all-stop cases on the global `PlayerMech`, so with a machine viewed it is the six move and turn cases above that act on that machine. What a viewed object of another class does with the commands is [Open](#open). The arrow keys share their scancodes with keypad 8, 2, 6 and 4, and `Input_KeyjoyAxisKey` (`0045a308`) takes those as held axes. With the flag up it also passes a keypad code on when `Ctrl` or `Alt` is held, which is what lets the six reach the dispatcher — but only after it has recorded the key as held, so each of the six still steers or moves the throttle as its bare arrow does. The `Alt` bit it stores at `004d245a` does not change that: its one reader, in `Input_BuildKeyboardAxes`, is a compare whose branch lands on the same instruction as its fall-through.

## Rejected readings

| Reading | Why it is wrong |
|---|---|
| VSHELL launches DBSIM, from its own argument list `dummy -eggplant -Z -s -v3 -h -F -G -m -D`. | That list is in VSHELL's data, and `Shell_BuildSimArgv` (`0042f2e8`, VSHELL) builds an `argv` from it under the [conditions above](#vshells-unreferenced-simulator-command-line), but `es2_xref.py` finds no branch or stored pointer reaching it, and it returns without spawning anything. `ES.EXE` launches DBSIM, with its own list. |
| VSHELL's `-m` turns the mouse off. | The usage text says so (`-m -M /m /M Disables mouse`), and the parser's `-m` case does store 1 in `ShellSwitch_M`. `Shell_Main` (`00401525`) builds the mouse producer without testing it, and its one reader `es2_xref.py` finds is `Shell_BuildSimArgv` (`0042f2e8`), to which `es2_xref.py` finds no reference, which would pass `-m` on to DBSIM. |
| The demo attract mode cannot be started, because the `-D` in VSHELL's list sits behind a flag nothing sets. | That list is the unreferenced one above. The main menu's `VIEW DEMO` button exits the shell with code 5, and `ES.EXE` answers 5 with `dbsim … -D`. |
| `ES.EXE` passes `-SPRUNKNOWN` to DBSIM on every launch. | The string is in its simulator list, but the slot is conditional on `ES.EXE` having been given `-SPRUNKNOWN` itself. |

## Open

- **Deferred:** what the simulator's `-T<n>`, `-V<n>`, `-W<n>`, `-a` and `-c` feed. The searches above find no reader, and a null result does not prove there is none; code Ghidra has not disassembled is covered only by the address sweeps, not by the displacement search.
- **Deferred:** VSHELL's language slot 3 from `-e…`. Its readers test for 0, 1 and 2.
- **Deferred:** what `-C` does with the four names that have no cockpit files (`ROADRUNNER`, `PATRIOT`, `PANTHER`, `TEST3`), and what `-E` does when `SIMVOICS.VOL` is missing.
- **Deferred:** where `printf` output from VSHELL's `-v` and `-?` goes.
- **Deferred:** what the command handlers of a structure and a flyer do with the commands `Sim_DispatchCommand` passes them when `Ctrl+N`/`Ctrl+P` has left one of them viewed.
