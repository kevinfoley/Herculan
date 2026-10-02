# Command lines: ES.EXE, VSHELL and DBSIM

How the three retail executables start one another and what every switch each one parses does. The player-facing summary is [`launch-options.md`](launch-options.md); HERCULAN's own flags are in [`engine/herculan-command-line.md`](engine/herculan-command-line.md). Addresses name their binary; `ES.EXE` is not in the Ghidra project, and its 2.5 KB of code was read whole from a direct disassembly. <!-- doc-lint: ok -->

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

`<n>` counts simulator launches from 0 within one run of `ES.EXE`, so successive missions play CD tracks 2, 3, 4, 5, 6, 2… ([`formats/audio.md`](formats/audio.md#which-track-and-whether-there-is-one)). The simulator's list also has slots for `-m` and `-Z`; their conditions test a local initialised to 0 and one initialised to 1 that nothing afterwards writes, so neither is ever passed. In the simulator-only loop every pass forces the state to 2, so the simulator reruns the mission already in `data\` and the shell never starts; an exit code of 0 still ends it.

### Exit codes

| Code | Set by | Meaning to `ES.EXE` |
|---|---|---|
| 0 | the shell quitting ([`QUIT`](shell/screen-layout.md#quit) or closing its window); DBSIM when `004d2582` is set, which `Ctrl+Q`'s `EXIT EARTHSIEGE?` confirmation does — the panel closing the simulator's window raises too | quit |
| 2 | VSHELL `Shell_SetExitCode(2)` (`0040876a`) — the mission launch paths, including `Msn_BuildPath` (`0044d5bd`, VSHELL) and the debrief's `REPLAY MISSION?` | fly a mission |
| 3 | DBSIM when a mission ends other than by a quit or a demo | shell, into the debrief |
| 4 | DBSIM in place of 3 when the player's machine is destroyed (`+0x99`) and `MissionModeFlag` (`004a9ed6`) is up — which the load zeroes, so never | shell, into the debrief |
| 5 | VSHELL `FUN_0043156f` — the main menu's `VIEW DEMO` button | fly a demo tape |
| 6 | DBSIM after a demo (`DemoMode`, `004d25b4`) | shell |

DBSIM returns its code from `WinMain` out of `004d283c`, written in `Sim_Shutdown` (`00461eec`, DBSIM) after the mission's results ([`simulation/mission-objectives.md`](simulation/mission-objectives.md#what-the-mission-leaves-the-shell--mission_writeresults-0042412c)). The shell side of `-X3`/`-X4` and `-X6` is [`shell/campaign-loop.md`](shell/campaign-loop.md).

## VSHELL

`FUN_0040107c` (VSHELL) parses the switches; each accepts `-` or `/` and either case. `FUN_004073bc(0)` sets the option block to its defaults immediately before.

| Switch | Store | Effect |
|---|---|---|
| `-eggplant` | `0046c084` = 1 | Without it the shell shows "You cannot run this exe directly" and quits |
| `-e…` other than `-eggplant` | `0048227a` = 3 | Language slot 3; see [Open](#open) |
| `-f`, `-g` | `0048227a` = 1, 2 | French, German: the `LANG0.VOL` folder the `.BIN` string tables are opened under ([`simulation/preferences.md`](simulation/preferences.md#what-each-byte-is), byte 43) |
| `-s` | `00482272` = 0 | No sound |
| `-m` | `00482270` = 1 | No mouse |
| `-k` | `00482271` = 0 | No keyboard |
| `-X<n>` | `Shell_SetExitCode(n)` → `0046e210` | Copied into `0048227e` right after the parse; see [`shell/campaign-loop.md`](shell/campaign-loop.md) |
| `-r` | `0048227e` = 3 | Overwritten by the `-X` copy; no effect ([`shell/campaign-loop.md`](shell/campaign-loop.md#rejected-readings)) |
| `-@` | `00482284` = 1 | The mission picker below |
| `-a` | `00482275` = 0 | Turns the shell's movies off: [the movie queue](shell/screen-layout.md#the-shells-movies) takes nothing and plays nothing |
| `-l` | `00482280` = 0 | Read only by the unreferenced function at `0042f2e8`; no effect |
| `-v`, `-?` | `00482272` = 0 | `printf` the version or the usage text, turn sound off, and call `Shell_ShutdownDevicesAndSound` (`004092dc`). The parse runs before `Shell_Main` (`00401525`) builds `devices.cpp`'s viewport (`Devices_Init`, `0040db38`) and the sound manager, so that call releases nothing, and the parse goes on to the next argument |

`-d`, tested separately in VSHELL's `Shell_WinMain` (`00406507`), clears `0046d740`, which the same function overwrites from `ShellOption_DisplayMode` before anything reads it.

### `-@`: the mission picker

`FUN_00412ce1` (VSHELL), called at campaign start (`FUN_00412a2f`) and after each debrief (`Game_ProcessMissionResults`), shows a panel titled `DEBUG` naming the campaign's next mission (`FUN_0044db25`), built by `FUN_0044d6a8` with two buttons: `Use Default` (`FUN_0044d55a`), and one that hides the panel and loads `msn\<name>.msn` through `Msn_BuildPath`, `<name>` being the text at `DAT_0048dc18+0x45`. Without `-@` the function posts two type-`0x20` events, values 2 and 1, to `Use Default` through `FUN_00468440`, which dismiss the panel before it is ever seen; with it the panel is left waiting. Retail confirms both halves: `ES.EXE -s -SPRUNKNOWN` puts the panel up on starting a new game, and a normal launch never shows it.

## DBSIM

Two parsers. `FUN_0045e6b0` (DBSIM) runs first from `WinMain`, after `VideoMode_Configure(0)`, for the switches the window needs: `-v`, `-Z`, `-X`, `-c`. `Sim_ParseCommandLine` (`0045e73c`) runs later for the rest. Both are case-sensitive and test the character after `-`; neither accepts `/`.

| Switch | Store | Effect |
|---|---|---|
| `-eggplant` or `-EGGPLANT` | `004d25a8` = 1 | Without it `WinMain` shows "dbsim.exe is not meant to be run…" and exits |
| `-v<d>` | `VideoMode_Configure(d)` | [`formats/cockpit-views.md`](formats/cockpit-views.md#video-modes) |
| `-Z1`, `-Z0`/`-Z` | `004d25e2` | Full screen or windowed; [`simulation/preferences.md`](simulation/preferences.md#the-video-mode-and-full-screen-bytes) |
| `-b` | `Display_UseScrollWindow` = 0 | [`formats/cockpit-views.md`](formats/cockpit-views.md#the--b-paged-path) |
| `-S` | `CmdLineSwitch_S` = 1 | No effect; [`formats/cockpit-views.md`](formats/cockpit-views.md#the--b-paged-path) |
| `-SPRUNKNOWN` | `DAT_0049ef60` toggled | The developer keys below |
| `-s` | `004d254c` toggled from 1 | `Sound_Init(0)`: no sound driver |
| `-R<n>` | `Music_TrackSelect` | [`formats/audio.md`](formats/audio.md#which-track-and-whether-there-is-one) |
| `-E`, `-F`, `-G` | `004d25ba` = `s`, `f`, `g` | The language letter, `r` by default. `Voice_ArchiveName` (`0045ef68`) puts it last in `simvoice` unless it is `r`, and `Language_StringFilePath` (`0045ef00`) puts it last in `str`, giving the `st<letter>\` string folder. `s` is Spanish: `SIMALERT.VOL` has an `STS\` folder, and no `SIMVOICS.VOL` ships |
| `-l` | `CockpitArt_LoadOnDemand` = 1 | [`formats/audio.md`](formats/audio.md#memory-budget-and-eviction), [`formats/terrain-texturing.md`](formats/terrain-texturing.md#base-formation-pads) |
| `-C<name>` | `CockpitOverride_Index` (`0049ac4c`), `CockpitOverride_Name` (`0049ac50`) | `_stricmp` against 13 names at `0049ac64`: `ROADRUNNER`, `OUTLAW`, `RAPTOR2`, `TOMAHAWK`, `PATRIOT`, `PANTHER`, `SAMSON`, `COLOSSUS`, `APOCA`, `RAZOR`, `MAVERICK`, `OGRE`, `TEST3`. A match replaces the herc index and name the cockpit view manager takes from the player's machine (`+0x27`, `+0x2d`), and the name the canopy-crack art is built from |
| `-t<n>` | `CommBox0PilotOverride` | `HddGauge_LoadPilotFrames` (`0044a7c0`) takes it as the pilot index of squad comm box 0 when it is non-negative; [`formats/heads-down-display.md`](formats/heads-down-display.md#squad-comm-boxes) |
| `-r<name>`, `-p<name>`, `-D` | | [`formats/tap-input-tape.md`](formats/tap-input-tape.md#the-switches) |
| `-d` | `004d2562` | Opens the checkpoint file `<tape stem>.dmp`; [`formats/tap-input-tape.md`](formats/tap-input-tape.md#the-checkpoint-file) |
| `-B` | `004d25b0` = 1 | In `Sim_HandleWindowKey` (`0045fd60`): `Ctrl+B` (`0x430`) calls `__break` (`004679d4`), an `INT3`; and `Alt+Enter` (`0x21c`) stops toggling full screen while a tape plays |
| `-m` | `004d2701` = 1 | `Sim_InitMissionSession` sends control code 5 to `\\.\DARKMONO.VXD` (`maybe_Mono_Clear`, `004954b8`), a developer's monochrome-monitor driver that does not ship |
| `-P` | block `+0x0d` = 1 | No effect. Its three readers — `0045f2ab` and `0045f37b` in `Sim_Run` (`0045f144`), `00461f86` in `Sim_Shutdown` (`00461eec`) — are each a `CMP` followed by an instruction that overwrites the flags or a `CALL`, with no branch between |
| `-T<n>`, `-V<n>`, `-W<n>` | block `+0x5a`, `+0x56`, `+0x58` | `Main_StaticInit` sets all three to -1 |
| `-a` | block `+0x7d` = 0 | `Main_StaticInit` sets it to 1 |
| `-c` | block `+0x72` = 1 | |
| `-X<n>` | `004d283c` | Zeroed by `Sim_Run` (`0045f144`) before `Sim_ParseCommandLine` runs; no effect |

"Block" is the `0xc3`-byte global block at `004d2540` ([`formats/cockpit-views.md`](formats/cockpit-views.md#video-modes)). For `-T`, `-V`, `-W`, `-a` and `-c`, three searches find only the stores above ([Open](#open)): `es2_xref.py` on the five addresses, which finds one dword each in the whole PE, the parser's own; every absolute operand from `004d2590` to `004d25bf`, which also rules out a wider load overlapping one of these fields; and the displacements off the base in the fifteen register holders and the three blit helpers it is pushed to, none of which spills, copies or rebases it. The same searches find the reads of the neighbouring `+0x54`, `+0x7b` and `+0x7c`. No `.EXE` on the disc passes any of the five: `ES.EXE`'s simulator list above has none of them, and VSHELL's unreferenced list below has none either.

### `-SPRUNKNOWN`: the developer keys

`DAT_0049ef60` gates these commands. Codes are set-1 scancodes plus `0x200` for `Alt` and `0x400` for `Ctrl` ([`formats/cockpit-input.md`](formats/cockpit-input.md#keyboard-commands-are-scancodes)), and the key names are read from `VkToScancode`.

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
| `0x602`-`0x60a` | `Ctrl+Alt+1`-`9` | Sets the step (`004a9d50`) and angle step (`004a9d52`) from two tables at `0049a020` and `0049a032`, both 500, 1000, 1500, 2000, 3000, 4500, 6000, 7500, 9000 |

Both steps start at 2000, entry 3 of both tables: the mech module's static initialiser (`0041bc5c`) registers `00415464` as subsystem phase 2, which `Sim_InitMissionSession` runs at every mission start, and that stub loads the two entries. The handler is reached through `Sim_DispatchCommand`'s default case, which passes a command it does not claim to the `+0x2c` slot of the viewed object `ViewChain_Viewed` — `LocalPlayerMech` only when `DAT_0049ef5c` is set, and that dword is 0 in the image with only compares among the seven references `es2_xref.py` finds. `Mech_HandleCommand` works its weapon and all-stop cases on the global `PlayerMech`, so with a machine viewed it is the six move and turn cases above that act on that machine. What a viewed object of another class does with the commands is [Open](#open). The arrow keys share their scancodes with keypad 8, 2, 6 and 4, and `Input_KeyjoyAxisKey` (`0045a308`) takes those as held axes. With the flag up it also passes a keypad code on when `Ctrl` or `Alt` is held, which is what lets the six reach the dispatcher — but only after it has recorded the key as held, so each of the six still steers or moves the throttle as its bare arrow does. The `Alt` bit it stores at `004d245a` does not change that: its one reader, in `Input_BuildKeyboardAxes`, is a compare whose branch lands on the same instruction as its fall-through.

## Rejected readings

| Reading | Why it is wrong |
|---|---|
| VSHELL launches DBSIM, from its own argument list `dummy -eggplant -Z -s -v3 -h -F -G -m -D`. | That list is in VSHELL's data, and a function at `0042f2e8` (VSHELL) builds an `argv` from it, appending `-D` when `00482282` is non-zero. Ghidra never disassembled that function, and `es2_xref.py` finds no branch or stored pointer reaching it. It returns without spawning anything. `ES.EXE` launches DBSIM, with its own list. |
| The demo attract mode cannot be started, because the `-D` in VSHELL's list sits behind a flag nothing sets. | That list is the unreferenced one above. The main menu's `VIEW DEMO` button exits the shell with code 5, and `ES.EXE` answers 5 with `dbsim … -D`. |
| `ES.EXE` passes `-SPRUNKNOWN` to DBSIM on every launch. | The string is in its simulator list, but the slot is conditional on `ES.EXE` having been given `-SPRUNKNOWN` itself. |

## Open

- **Open:** what the simulator's `-T<n>`, `-V<n>`, `-W<n>`, `-a` and `-c` feed. The searches above find no reader, and a null result does not prove there is none; code Ghidra has not disassembled is covered only by the address sweeps, not by the displacement search.
- **Open:** VSHELL's language slot 3 from `-e…`. Its readers test for 0, 1 and 2.
- **Open:** what `-C` does with the four names that have no cockpit files (`ROADRUNNER`, `PATRIOT`, `PANTHER`, `TEST3`), and what `-E` does when `SIMVOICS.VOL` is missing.
- **Open:** where `printf` output from VSHELL's `-v` and `-?` goes.
- **Open:** what the command handlers of a structure and a flyer do with the commands `Sim_DispatchCommand` passes them when `Ctrl+N`/`Ctrl+P` has left one of them viewed.
