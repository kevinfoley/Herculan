# DBSIM.EXE — `.TAP` input tapes

**ES1's simulator records a mission's input to a tape and plays it back as ES2's does ([ES2's tapes](../../retail/formats/tap-input-tape.md)): the seven files the mission starts from, then one record a frame.** What differs:

- a frame's record is 18 bytes before its mouse events where ES2's is 24, and carries one command word and no list of command codes ([the frames](#the-frames));
- the tape has no joystick capability block;
- a tape played with `-p` ends the mission when it runs out or `Ctrl+E` is pressed, as under `-D`, where ES2's ends the mission so under `-D` alone ([recording and playback](../simulation/input.md#recording-and-playback));
- a tape plays on the install's own `data\prefs.cfg`, its own copy unpacked into `tapes\` and no reader of it found ([the bundle](#the-bundle));
- the demo pick never repeats the name it picked last, and since a run of the simulator plays one mission, `-D` never picks the first name on `tapes\demolist.str`'s list when the list holds more than one ([the demo tape](#the-demo-tape));
- in playback the checkpoint flush formats a `Playback error. Frame #%ld` message each frame and discards it, where ES2's does nothing ([the checkpoint file](#the-checkpoint-file)).

Addresses are `ES1DBSIM` image addresses ([the X-32 executable](x32-executable.md#the-image)); ES2's are DBSIM's. The game's functions here remove their own arguments, so argument lists are in stack order ([calling conventions](compiler.md#calling-conventions)); the runtime's take theirs in the C convention. What the input build does with a frame is [`input.md`](../simulation/input.md#recording-and-playback)'s.

## The bundle

A tape opens with seven length-prefixed blocks, ES2's seven ([ES2's bundle](../../retail/formats/tap-input-tape.md#the-bundle)), each a `uint32` size and that many bytes. `Sim_ParseCommandLine` (`00058e74`) writes and reads them ([the command line](../simulation/main-loop.md#the-command-line)). The tape's name is `TapeStem` (`000b5b7a`): `-p` and `-r` store theirs only while it is 0, so the first of them given is kept, and `-D` stores its pick whatever it holds (`00058edd`), so a `-D` after `-p` or `-r` replaces the name, and one whose pick is null clears it. `Path_WithExtension` (`00060644`) forces the extension `tap`, cutting the name at its first `.`, so a name through a folder whose name holds a `.` loses the rest of its path.

- **`-p`** (`00058fe0`) opens `<stem>.tap` from the current folder, `rb`, with no `Path_ApplyAltDrive`, as `TapeFile` (`000b5b82`), and sets `TapePlayback` (`000b5b76`) whether or not it opened: nothing tests the open, so a tape that does not open is unpacked from a null stream ([Open](#open)).
- **`-D`** (`00058ed6`) clears `TapePlayback` and `DemoMode` when its pick is null; otherwise it sets both, then opens `Path_ApplyAltDrive`(`Config_BuildPath`(`<stem>.tap`)), so `tapes\<stem>.tap`, and clears both again when the tape does not open ([the demo tape](#the-demo-tape)).
- **The unpack** (`00059020`–`000590c1`), which `-p` falls into and `-D` jumps to, writes seven files with `Tape_UnpackFile`, each destination through `Path_ApplyAltDrive`: `data\script.dat` (`00096d90`), `data\player.mec` (`00096da0`), `data\mission.var` (`00096db0`), `prefs.cfg` (`00096dc1`), `data\restore.dat` (`00096dcb`), `data\object.str` (`00096ddc`) and `keyjoy.cfg` (`00096dec`), the two configuration files first through `Config_BuildPath` (`00059300`), which gives `tapes\` once `TapePlayback` is set.
- **`-r`** (`000590cb`) opens `<stem>.tap` `wb`, a stream nothing tests or stores, packs seven files into it with `Tape_PackFile`, each source through `Path_ApplyAltDrive` alone: `data\script.dat` (`00096dfe`), `data\player.mec` (`00096e0e`), `data\mission.var` (`00096e1e`), `data\prefs.cfg` (`00096e2f`), `data\restore.dat` (`00096e3e`), `data\object.str` (`00096e4f`) and `keyjoy.cfg` (`00096e5f`), the last from the current folder; then it closes the tape and sets `TapeRecording` (`000b5b78`). The ES1 install holds `DATA\KEYJOY.CFG` and none in its own folder, so a recording made from there packs an empty `keyjoy.cfg`, as each [retail tape](#the-retail-tapes) carries.

**`Tape_PackFile` (`000574a0`)**, (path, tape): opens the path `rb` and takes its size with `fseek` (`0007566d`) to the end and `ftell` (`000756ef`), then seeks back to the start and clears the stream's error flag; a file that does not open has size 0. It writes the size, then the file in chunks of at most 500 bytes, and closes the source with `fclose` even when it did not open, a null handle.

**`Tape_UnpackFile` (`0005756c`)**, (path or null, tape): opens the path `wb` unless it is null, reads the size, and reads that many bytes in chunks of at most 500, writing each to the destination when there is one. A destination that does not open is treated as a null one, its bytes read and dropped, and `fclose` gets the null handle. No read's count is checked, so a tape cut short inside the bundle unpacks whatever the stack buffer held. The loop runs while the size left is not 0, taking the whole of it when it is below 500, so a negative size, which no retail tape holds, makes one pass whose read count is the size itself.

Both are ES2's, ES2's arguments in the other order. Where the bundle's handling differs from ES2's:

- the tape's `prefs.cfg` is unpacked into `tapes\`, but `Prefs_Load` (`0004a744`) opens `data\prefs.cfg` itself, through `Path_ApplyAltDrive`, and `es2_xref.py` finds four calls of `Config_BuildPath`, `Keyjoy_LoadConfig`'s and three in `Sim_ParseCommandLine`, so a tape is played on the install's preferences and no reader of `tapes\prefs.cfg` was found ([Open](#open)); under `DemoMode` [the preferences start-up](../simulation/input.md#the-preferences-start-up) loads `data\prefs.cfg` twice. ES2's `-p` copies seven bytes of `data\prefs.cfg` over `tapes\prefs.cfg`;
- `Keyjoy_LoadConfig` reads `Config_BuildPath`'s `keyjoy.cfg`, so playback reads the tape's `tapes\keyjoy.cfg` ([`keyjoy.cfg`](../simulation/input.md#keyjoycfg--keyjoy_loadconfig-0004d918)), where ES2's always reads `data\keyjoy.cfg`.

## The frames

After the bundle, one record a frame, which `Input_BuildPlayerDevice` (`0004cb14`) writes while recording and reads in playback ([recording and playback](../simulation/input.md#recording-and-playback)):

| Offset | Type | Field |
|---|---|---|
| `+0x00` | `int16` | the command word |
| `+0x02` | `int16` × 4 | the four axes, steering, throttle, twist and pitch, as step 7 of the build leaves them: before the camera-axis step and `Backturn`, which playback runs again on them |
| `+0x0a` | `int16` | `SimTickDelta` |
| `+0x0c` | `uint16` | bit 0 set when button 0 (the input block's `+0x15`) is not 0, bits 1–7 the same for buttons 1–7, bits 8–11 for the four source buttons; bits 12–15 0 |
| `+0x0e` | `int32` | the mouse-event count *n* |
| `+0x12` | 14 × *n* | the mouse events, in `CockpitMouseQueue_Push`'s record ([the callback and the queue](../simulation/cockpit-mouse.md#the-callback-and-the-queue)) |

Where ES2's differs ([ES2's stream](../../retail/formats/tap-input-tape.md#the-stream)): ES2's tape carries the eight-byte joystick capability block once after the bundle; its 24-byte header holds the trigger, the hat and the stick's own eight buttons in bytes of their own and a command count; and the command codes follow the mouse events. ES1's frame carries the one command word the build took from the keyboard ([the command word](../simulation/input.md#the-command-word)) and no list.

**The pointer and the mouse bytes are not recorded**, as in ES2 ([ES2's stream](../../retail/formats/tap-input-tape.md#the-stream)): the input block's `+2`–`+0xc` are on no record, and playback leaves them 0, the build copying them from the live block only in its live step ([recording and playback](../simulation/input.md#recording-and-playback)). [The gunsight drag](../simulation/main-loop.md#the-players-controls--sim_pollplayerinput-0005a0c0) reads them, so a replay never arms it. The mouse events replay through the cockpit's queue.

### The page parity

**A tape replays the views as they were recorded only when its first frame finds the same page on the screen as the recording's did.** The view command's page gate holds a glance, or the return from one, until the display's `+0x88` is its page ([the view command](../simulation/cockpit-views.md#cockpitview_processviewcommand-000226f0)), and `Sim_EndFrame` swaps `+0x88` and `+0x8c` every frame, so the frame on which a recorded view change lands depends on the pages' parity, which the tape does not carry. `Sim_StartMission` leaves `+0x88` at 0 ([the mission start](../simulation/main-loop.md#the-mission-start--sim_startmission-0005a9dc)), and a mission that opens with the lift has swapped the pair 115 times more before the first frame, an odd count ([the ride](../simulation/lift-start.md#the-ride--liftstart_rise-00057ffc)), so the lift is part of what a tape replays. `CockpitViewManager_QueueViewCommand` (`00022634`) latches a command whenever none is pending, so a hat held across a glance's return queues the opposite glance one frame after the return lands, unless the gate held the return a frame. Traced through those rules, `DEMO2`–`DEMO4`'s hat buttons take each view change once from the parity the lift leaves, the gate holding each return a frame, and bounce into the opposite glance at each return from the other parity ([Open](#open)). For `DEMO2`, run frame by frame after the ride: from the lift's parity the right glance lands at frame 37 and its return at 53, the pan down to the Heads-Down Display at 58 and back up at 626, the left glance at 1588 and its return at 1618; from the other parity frames 53–55 go into the left glance, the pan down at 57–58 never happens, and frames 1617–1620 go back into the right glance.

### The retail tapes

The install's `TAPES` folder holds four, read through `HercWorks.Core`:

| | `DEMO1` | `DEMO2` | `DEMO3` | `DEMO4` |
|---|---|---|---|---|
| Frames | 2308 | 2951 | 2470 | 1571 |
| Mouse events | 1332 | 983 | 42 | 370 |
| `object.str`, bytes | 38 | 38 | 290 | 201 |

In each, `script.dat` is 4558 bytes, `player.mec` 241, `mission.var` 200, `prefs.cfg` 17, `restore.dat` 42 and `keyjoy.cfg` 0, and `SimTickDelta` runs from 64 to 450, 25 to 176 ms.

## The demo tape

**`DemoTape_PickRandom` (`0005771c`)**, no arguments, gives `-D` its tape's name ([the command line](../simulation/main-loop.md#the-command-line)):

1. On its first call it builds the string table `DemoTape_NameTable` (`000b5cc0`) (`StringTable_Ctor`, guard `DemoTape_NameTableBuilt`, `000b5cbc`).
2. Until a load succeeds, it opens `tapes\demolist.str` through `Path_ApplyAltDrive` as a `FileRStream` and, when the stream's status is 0, loads the table and its first group into `DemoTape_Names` (`000b5cd4`), with room for 20 (`StringTable_LoadGroup`(table, 0, `0x14`, names), [the load](str-strings.md#the-load)), the count into `DemoTape_NameCount` (`000b5cd0`). A count above 0 sets `DemoTape_ListLoaded` (`00096c40`). With it clear the call returns 0.
3. It draws `time` (`000757a0`) modulo the count, and draws again while the draw equals `DemoTape_LastPick` (`00096c42`) and the count is above 1. The draw becomes `DemoTape_LastPick`, and the call returns that name.

`time` counts whole seconds, so a draw that repeats the last one is drawn again until the clock reaches the next second, up to a second later. `DemoTape_LastPick` is 0 in the image, and `es2_xref.py --decode` finds its one write in step 3; `es2_xref.py` finds one call of `DemoTape_PickRandom`, from `Sim_ParseCommandLine`. So in a run of the simulator, which plays one mission ([the main loop](../simulation/main-loop.md)), the pick is never entry 0 while the list holds more than one name. The install's `TAPES\DEMOLIST.STR` holds one group of four names, `DEMO1` to `DEMO4`, so `-D` plays `DEMO2`, `DEMO3` or `DEMO4` ([Open](#open)).

ES2's draws once, `time() % count`, and takes entry 0 for a count of 1 ([ES2's switches](../../retail/formats/tap-input-tape.md#the-switches)). The exit routine `00058d5c` destroys the table while `DemoTape_NameTableBuilt` is set ([where the code sits](#where-the-code-sits)).

## The checkpoint file

`-d` opens `<stem>.dmp` as `DmpFile` (`000b5b7e`), mode `wb` while recording, which it closes at once while leaving the handle set, and `rb` otherwise ([the command line](../simulation/main-loop.md#the-command-line)). The checkpoint routine and its flush are ES2's ([ES2's checkpoint file](../../retail/formats/tap-input-tape.md#the-checkpoint-file)):

- **`Vcr_Checkpoint` (`00000bd4`)**, (label, size, data), with `DmpFile` set: while recording it copies the data onto `Vcr_Buffer` (`000adcd8`) at `Vcr_Cursor` (`00090188`), advancing the cursor and the total `Vcr_Total` (`0009018c`); in playback it reads `size` bytes from the `.dmp` file into the buffer and, when they differ from the data, formats the label into a stack buffer it discards and sets `Vcr_MismatchFlag` (`00090190`).
- **`Vcr_FlushCheckpoints` (`00000c84`)**, subsystem phase 6, run after every frame ([the subsystem phases](../simulation/main-loop.md#the-subsystem-phases)), with `DmpFile` set: while recording it opens `<stem>.dmp` in mode `ab` (assert `vcr.cpp` line `0x30`, severity 5), writes the buffer up to its total, closes the file, and puts the cursor back on the buffer and the total to 0. In playback it formats `Playback error. Frame #%ld` with `FrameRate_GetFrameCount` (`0006020c`, [the frame count](../simulation/debug-monitor.md)) into a stack buffer and discards it, where ES2's does nothing.

`Vcr_StaticInit` (`00000d3c`), the static constructor table's entry `000adbb4`, registers the flush. `es2_xref.py` finds no caller of `Vcr_Checkpoint` ([Open](#open)); with none, the total stays 0 and each flush appends nothing, so what `-d` does is make an empty `<stem>.dmp` while recording and hold one open in playback.

## Where the code sits

`vcr.cpp`, by the assert's source path at `00000ccc`, is `00000b38`–`00000d4b`. It follows the subsystem table's file, `RegisterSubsystemLoader` (`00000ac4`) and `Subsystem_RunPhase` (`00000af4`), whose `RET 4` at `00000b33` is followed by two zero bytes, and opens with a run of `ConfigPart`'s copies ([module copies](compiler.md#vtables)): `HeapPtr_DtorVcrCopy` (`00000b38`), `ConfigPart_DtorVcrCopy` (`00000b60`, slot `+0x1c` of `00090122`), `ConfigPart_GetClassTagVcrCopy` (`00000b9f`) and `TSPartBase_DtorVcrCopy` (`00000ba8`, slot `+0x1c` of `00090156`). It ends with `Vcr_StaticInit`, whose `RET` at `00000d48` is followed by three zero bytes; the next file, `memory.cpp`, opens at `Memory_Ctor` (`00000d4c`) ([the heap](../simulation/memory.md#where-the-object-file-starts-and-ends)).

The bundle's two functions are in a file of their own, `000574a0`–`0005767f`, which opens with no copy after `Main_StaticInit`'s file ([the main loop's files](../simulation/main-loop.md#where-the-object-files-start-and-end)) and its one zero byte: `Tape_PackFile`, `Tape_UnpackFile`, `ClassItem_LoadFromFile` (`0005760c`) and `Cursor_LoadCursorResource` (`00057640`), whose `RET` at `0005767f` ends it before the run at `00057680`. `DemoTape_PickRandom` is the first function after that run, in the file whose exit routine `00058d5c` ends it before `main.cpp` (the same table).

## Functions

| ES1 | Address | ES2 |
|---|---|---|
| `HeapPtr_DtorVcrCopy`, `ConfigPart_DtorVcrCopy`, `ConfigPart_GetClassTagVcrCopy`, `TSPartBase_DtorVcrCopy` | `00000b38`, `00000b60`, `00000b9f`, `00000ba8` | |
| `Vcr_Checkpoint`, `Vcr_FlushCheckpoints`, `Vcr_StaticInit` | `00000bd4`, `00000c84`, `00000d3c` | `Vcr_Checkpoint` (`00401dc0`), `Vcr_FlushCheckpoints` (`00401e5c`), `Vcr_StaticInit` (`00401ef8`) |
| `Tape_PackFile`, `Tape_UnpackFile` | `000574a0`, `0005756c` | `0045cc88`, `0045cd40` |
| `DemoTape_PickRandom` | `0005771c` | `0045ce9c` |
| the runtime's `fseek`, `ftell`, `time` | `0007566d`, `000756ef`, `000757a0` | |

The class tag has no Ghidra function. `time` reads DOS's date and time (`INT 21h` functions `0x2a` and `0x2c`) and adds 25,200 seconds, seven hours, to the count from 1970 they give.

## Globals

| | Address |
|---|---|
| `DemoTape_NameTableBuilt`, byte; `DemoTape_NameTable`, `0x10` bytes | `000b5cbc`, `000b5cc0` |
| `DemoTape_NameCount`, `int16`; `DemoTape_Names`, 20 pointers | `000b5cd0`, `000b5cd4` |
| `DemoTape_ListLoaded`, byte; `DemoTape_LastPick`, `int16` | `00096c40`, `00096c42` |
| `Vcr_Buffer`, starting right after the exit table's last entry (`000adcd4`) | `000adcd8` |
| `Vcr_Cursor`, `Vcr_Total`; `Vcr_MismatchFlag`, `int16` | `00090188`, `0009018c`, `00090190` |

## Open

- **Open:** a caller of `Vcr_Checkpoint` (`00000bd4`). `es2_xref.py` finds no branch to it, the same sweep finding `Input_BuildPlayerDevice`'s seven callers. The two stored dwords it lists, `00000bd2` at `0002f350` and `00000bd3` at `00073eeb`, which reach the padding before it, are bytes of other instructions: `0002f350` is the imm32 tail of `MOV byte ptr [0x000bd222],0x0` at `0002f34d` in `MessagePort_StepToVariant`, and `00073eeb` the rel32 of `CALL 0x00074ac2` at `00073eea` in `00073ee8`.
- **Open:** a reader of `Vcr_MismatchFlag`; `es2_xref.py --decode` finds `Vcr_Checkpoint`'s write and no read.
- **Open:** a retail check of the demo pick, and whether the shell's demo runs the simulator with `-D`. Run `DBSIM -eggplant -D` eight times and note each tape's opening scene, then `DBSIM -eggplant -ptapes\demo1` once: its opening scene is not among the eight.
- **Open:** a reader of the unpacked `tapes\prefs.cfg`. `es2_xref.py` finds four calls of `Config_BuildPath`, `Keyjoy_LoadConfig`'s and three in `Sim_ParseCommandLine`, and a grep of the decompile for `tapes` finds only `tapes\demolist.str` and `Config_BuildPath`'s prefix.
- **Open:** what the runtime's `fread` and `fwrite` do with the null stream that `-p`'s and `-r`'s untested opens hand them. A retail check, on a copy of the install, since the unpack first truncates `data\script.dat`: `DBSIM -eggplant -pnosuch` from its folder, noting whether the simulator stops, hangs or starts a mission.
- **Open:** a retail check that `DEMO2`–`DEMO4` replay their view changes once each from the lift's parity. Their start points trip the lift's gate ([when it runs](../simulation/lift-start.md#when-it-runs)), and only `DEMO2` was traced frame by frame. Proposed: `DBSIM -eggplant -ptapes\demo2` shows the HERC rising before play starts, then one glance right and back, with no swing into the left glance on the return.
- **Open:** a positive control in the running game for the done checklist ([when a module is done](../../herculan/plan-es1-support.md#when-a-module-is-done)): after a mission run with `-rtest`, `TEST.TAP`'s first four bytes are `data\script.dat`'s size.
