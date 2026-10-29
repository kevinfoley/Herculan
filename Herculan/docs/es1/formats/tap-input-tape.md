# DBSIM.EXE — `.TAP` input tapes

**ES1's simulator records a mission's input to a tape and plays it back as ES2's does ([ES2's tapes](../../retail/formats/tap-input-tape.md)): the seven files the mission starts from, then one record a frame.** What differs:

- a frame's record is 18 bytes before its mouse events where ES2's is 24, and carries one command word and no list of command codes ([the frames](#the-frames));
- the tape has no joystick capability block;
- the demo pick never repeats the name it picked last, and since a run of the simulator plays one mission, `-D` never picks the first name on `tapes\demolist.str`'s list when the list holds more than one ([the demo tape](#the-demo-tape));
- in playback the checkpoint flush formats a `Playback error. Frame #%ld` message each frame and discards it, where ES2's does nothing ([the checkpoint file](#the-checkpoint-file)).

Addresses are `ES1DBSIM` image addresses ([the X-32 executable](x32-executable.md#the-image)); ES2's are DBSIM's. The game's functions here remove their own arguments, so argument lists are in stack order ([calling conventions](compiler.md#calling-conventions)); the runtime's take theirs in the C convention. What the input build does with a frame is [`input.md`](../simulation/input.md#recording-and-playback)'s.

## The bundle

A tape opens with seven length-prefixed blocks, ES2's seven ([ES2's bundle](../../retail/formats/tap-input-tape.md#the-bundle)), each a `uint32` size and that many bytes. `Sim_ParseCommandLine` (`00058e74`) writes and reads them ([the command line](../simulation/main-loop.md#the-command-line)). The name of `-p`, `-r` and `-D` is `TapeStem` (`000b5b7a`), the first one given; the extension is forced to `tap`. `-p` opens the tape, `rb`, as `TapeFile` (`000b5b82`) and unpacks seven files from it with `0005756c`: `data\script.dat`, `data\player.mec`, `data\mission.var`, `tapes\prefs.cfg`, `data\restore.dat`, `data\object.str` and `tapes\keyjoy.cfg`, the two configuration files through `Config_BuildPath` (`00059300`), which gives `tapes\` once `TapePlayback` is set. `-r` opens it `wb`, packs the same seven with `000574a0` from `data\`, `keyjoy.cfg` with no folder, and closes it. `-D` opens and unpacks a tape as `-p` does ([the demo tape](#the-demo-tape)).

**`Tape_PackFile` (`000574a0`)**, (path, tape): opens the path `rb` and takes its size with `fseek` (`0007566d`) to the end and `ftell` (`000756ef`), then seeks back to the start and clears the stream's error flag; a file that does not open has size 0. It writes the size, then the file in chunks of at most 500 bytes.

**`Tape_UnpackFile` (`0005756c`)**, (path or null, tape): opens the path `wb` unless it is null, reads the size, and reads that many bytes in chunks of at most 500, writing each to the destination when there is one. No read's count is checked, so a tape cut short inside the bundle unpacks whatever the stack buffer held.

Both are ES2's, ES2's arguments in the other order. Where the bundle's handling differs from ES2's:

- the preferences are [the preferences start-up](../simulation/input.md#the-preferences-start-up)'s, which under `DemoMode` loads them twice and puts seven options back, where ES2's `-p` copies seven bytes of `data\prefs.cfg` over `tapes\prefs.cfg`;
- `Keyjoy_LoadConfig` reads `Config_BuildPath`'s `keyjoy.cfg`, so playback reads the tape's `tapes\keyjoy.cfg` ([`keyjoy.cfg`](../simulation/input.md#keyjoycfg--keyjoy_loadconfig-0004d918)), where ES2's always reads `data\keyjoy.cfg`.

## The frames

After the bundle, one record a frame, which `Input_BuildPlayerDevice` (`0004cb14`) writes while recording and reads in playback ([recording and playback](../simulation/input.md#recording-and-playback)):

| Offset | Type | Field |
|---|---|---|
| `+0x00` | `int16` | the command word |
| `+0x02` | `int16` × 4 | the four axes |
| `+0x0a` | `int16` | `SimTickDelta` |
| `+0x0c` | `uint16` | bit 0 button 0 (the input block's `+0x15`), bits 1–7 buttons 1–7, bits 8–11 the four source buttons |
| `+0x0e` | `uint32` | the mouse-event count *n* |
| `+0x12` | 14 × *n* | the mouse events, in `CockpitMouseQueue_Push`'s record ([the callback and the queue](../simulation/cockpit-mouse.md#the-callback-and-the-queue)) |

Where ES2's differs ([ES2's stream](../../retail/formats/tap-input-tape.md#the-stream)): ES2's tape carries the eight-byte joystick capability block once after the bundle; its 24-byte header holds the trigger, the hat and the stick's own eight buttons in bytes of their own and a command count; and the command codes follow the mouse events. ES1's frame carries the one command word the build took from the keyboard ([the command word](../simulation/input.md#the-command-word)) and no list.

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

`vcr.cpp`, by the assert's source path at `00000ccc`, is `00000b38`–`00000d4b`. It follows the subsystem table's file, `RegisterSubsystemLoader` (`00000ac4`) and `Subsystem_RunPhase` (`00000af4`), whose `RET 4` at `00000b33` is followed by two zero bytes, and opens with a run of `ConfigPart`'s copies ([module copies](compiler.md#vtables)): `HeapPtr_DtorVcrCopy` (`00000b38`), `ConfigPart_DtorVcrCopy` (`00000b60`, slot `+0x1c` of `00090122`), `ConfigPart_GetClassTagVcrCopy` (`00000b9f`) and `TSPartBase_DtorVcrCopy` (`00000ba8`, slot `+0x1c` of `00090156`). It ends with `Vcr_StaticInit`, whose `RET` at `00000d48` is followed by three zero bytes; the next file opens at `00000d4c`, which `es2_xref.py` finds called from `calloc`, before `Heap_Init` (`00000d8c`).

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
- **Open:** a positive control in the running game for the done checklist ([when a module is done](../../herculan/plan-es1-support.md#when-a-module-is-done)): after a mission run with `-rtest`, `TEST.TAP`'s first four bytes are `data\script.dat`'s size.
