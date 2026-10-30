# ESCD.EXE — the launcher

**Typing `ES` runs the batch file `ES.BAT`, which runs ES1's launcher, `ESCD.EXE`. It refuses to start without enough memory or 40,000 bytes of free disk, runs `detect.exe` while there is no `data\prefs.cfg`, plays the intro flic when it finds the flic player, and then runs the shell, `GO.EXE`, and the simulator, `DBSIM.EXE`, one after the other, each time by the number the program before left in `data\exit.cfg`. After a mission that leaves 4 it plays the end flic before the shell. When the number is 0 it puts the screen mode back and returns to DOS.** ES2's supervisor, `ES.EXE`, runs the same two programs in turn by their exit statuses ([ES2's supervisor](../../retail/command-line.md#esexe--the-supervisor)); [the differences](#differences-from-es2s-esexe) are listed below.

Addresses are `ES1ESCD` image addresses ([the X-32 executable](../formats/x32-executable.md#the-image)). `es.cpp`'s functions other than `Launcher_Main` remove their own arguments, so their argument lists are in stack order ([calling conventions](../formats/compiler.md#calling-conventions)); the runtime's functions (`spawnl`, `fopen`, `getenv`, …) leave theirs to the caller.

## Where the object files start and end

`es.cpp` runs from `Launcher_PrefixPath` (`00000304`) to `00000b5f`, with no `ConfigPart` run at its start ([module copies](../formats/compiler.md#vtables)). The startup's code before it ends with a `RET` at `00000302` and one zero byte. Its functions start on 4-byte boundaries after `0x90` padding, and its last, `Launcher_Main` (`000005cc`), ends with a `RET` at `00000b5f`. Its asserts name `src\es.cpp`: `Launcher_HasDiskSpace`'s (line `0x4f`, at `000003ea`) and `Launcher_ReadExitCfg`'s (line `0x67`, at `00000478`).

`00000b60`–`00000c43` hold the four routines `es.cpp` calls to save and restore the video mode and to install and remove its critical-error handler, and the handler itself. They have no stack frame, follow one another with no padding, and two start off a 4-byte boundary (`Launcher_RestoreVideoMode` (`00000b83`), `CritErr_Handler` (`00000c07`)); the handler's `IRETD` at `00000c40` is followed by three zero bytes ([where object files meet](../formats/compiler.md#where-object-files-meet-in-the-code)). Whether they are `es.cpp`'s own code or a file of their own is [Open](#open). `00000c44`, code Ghidra has not made a function of, opens the next file, whose functions from `00000c84` are `es2_naming.py match` candidates for ES2's `memory.cpp` functions (`Memory_Init`, VSHELL `004077c5`, at 0.82 for the first) and whose asserts at `00000e0c` and `00000f15` name `src\memory.cpp`.

## Starting — `Launcher_Main` (`000005cc`)

`Launcher_Main` (`000005cc`), (argc, argv), called by the startup at `0xd` ([startup](../formats/compiler.md#startup)). It reads none of its arguments as switches: `Launcher_BuildArgs` passes all of them on to each program it runs ([a program's command line](#a-programs-command-line--launcher_buildargs-00000518)). In order:

1. `Mem_New` (`00001664`)(argc × 4 + 8), whose answer it does not keep; the heap is still off, so the memory comes from the extender ([the heap](../simulation/memory.md#the-heap)).
2. **The memory check.** *free* = `X32_LargestFreeBlock` (`00002e63`)() − 25,000, the extender's largest free block ([`ESCD.EXE`'s kernel](../formats/x32-executable.md#escdexes-kernel)). Below 3,435,000 it prints `Insufficient memory.` with `puts` (`00002248`) and `You need %d more bytes of free memory to play EarthSiege.` with `printf` (`0000232e`), the number 3,435,000 − *free*, and ends with `Mem_HeapOff` (`00001658`) and `exit`(0) (`00001b50`). So the launcher needs a largest free block of 3,460,000 bytes.
3. **The drive prefixes**, two strings: `ALT_DRIVE` and `PREFER_ALT` read with `getenv`, `PREFER_ALT` counting as set when `getenv` finds it at all.
    - `Launcher_PreferredPrefix` (`0000657c`) = `ALT_DRIVE` when `PREFER_ALT` is set, the empty string otherwise. With `PREFER_ALT` set and `ALT_DRIVE` not, the copy reads from address 0, which faults, since the data selector refuses offsets `0`–`0x7f` ([loading the image](../formats/x32-executable.md#loading-the-image)); neither of the installer's `ES.BAT`s sets that pair ([below](#the-installers-esbat)).
    - `Launcher_OtherPrefix` (`00006580`) = the empty string when both are set, `ALT_DRIVE` when only it is set, the empty string when it is not.

    Both are upper-cased in place (`strupr`, `00002294`), and `Assert_SetLogDir` (`00001814`) copies `Launcher_PreferredPrefix` into `00006624`, as `GO.EXE`'s step 3 does ([the shell's start-up](startup.md#the-start-up)). The data files are read and written through `Launcher_PreferredPrefix`, and the flic player is looked for through `Launcher_OtherPrefix`.
4. **The disk check**: `Launcher_HasDiskSpace` (`000003bc`)(40,000, `Launcher_PreferredPrefix`) ([below](#the-disk-check--launcher_hasdiskspace-000003bc)). On 0 it prints `Insufficient disk space.` and `You need at least 40,000 bytes of hard drive space to play EarthSiege.` with `puts` and ends with `Mem_HeapOff` and `exit`(0).
5. **`data\es.got`**, opened `wb` through `Launcher_PrefixPath`(`Launcher_PreferredPrefix`, …): when it opens, *free*, the figure of step 2, is written to it as 4 bytes. A file that does not open is passed over.
6. `Mem_InitHeap` (`000015dc`)(20,000): a heap of 20,000 bytes, or the extender's largest free block less 15,000 when that is smaller, asserting `Memory alloc failed` at severity 4 (`src\memory.cpp` line `0x1ee`) when the extender's allocation (`000001ca`) answers −1 ([the heap](../simulation/memory.md#the-heap)). Its answer goes into the local of step 2, which is not read again.
7. `Launcher_SaveVideoMode` (`00000b60`): the BIOS's equipment byte at `0040:0010` (linear `0x410`, through the flat data selector the kernel stores at `000057a6`) into `Launcher_SavedEquipmentByte` (`00005a39`), and the video mode `INT 10h` `AH=0Fh` answers into `Launcher_SavedVideoMode` (`00005a38`).
8. **The flics' command lines**, only when `ALT_DRIVE` is set ([below](#the-flics)).
9. **The flic player**: `Launcher_FlicPlayerFound` (`00005858`) = `Launcher_FileReadable` (`000004ac`)(the probe path of step 8, or null when `ALT_DRIVE` is not set, which answers 0) ([below](#the-flic-player--launcher_filereadable-000004ac)).

Then [the loop](#the-loop).

### The flics

With `ALT_DRIVE` set, `Launcher_Main` takes three drive numbers (1 for `A:`): the starting drive from `dos_getdrive` (`00001f9c`); the preferred drive, the first character of `Launcher_PreferredPrefix` less `0x40` when that prefix is not empty and the starting drive when it is; and the flic drive, the first character of `Launcher_OtherPrefix` less `0x40` when `Launcher_PreferredPrefix` is empty and the starting drive when it is not. It builds two command lines in locals, each path by `Launcher_DrivePath` (`00000360`)(flic drive, name), the drive's letter and `:` before the name:

| Flic | Program | File | Switches |
|---|---|---|---|
| the intro | *flic drive*`:playflic.exe` | *flic drive*`:flic\intro3.dfl` | `-B1000000`, `-S83` |
| the end | *flic drive*`:playflic.exe` | *flic drive*`:flic\end.dfl` | `-B1000000`, `-S83` |

What the two switches tell `PLAYFLIC.EXE` is [Open](#open). The probe path of step 9 is `Launcher_PrefixPath`(`Launcher_OtherPrefix`, `playflic.exe`).

A flic plays as `dos_setdrive` (`00001fab`)(preferred drive), `spawnl` (`00001be0`)(0, program, program, file, `-B1000000`, `-S83`, null), and `dos_setdrive`(starting drive): the current drive is the preferred one while `PLAYFLIC.EXE` runs. Mode 0 is `P_WAIT`: the launcher waits for the program to end.

### The disk check — `Launcher_HasDiskSpace` (`000003bc`)

`Launcher_HasDiskSpace` (`000003bc`), (bytes, prefix): with an empty prefix the drive is the current one (`dos_getdrive`); otherwise it is the prefix's first character, upper-cased, less `0x40`, after an assert at severity 4 (line `0x4f`) unless that character is a letter (bits 1 and 2 of the runtime's character-class table at `0000602d`). It answers 1 when `dos_getdiskfreespace` (`00003258`) gives at least bytes for the drive, as an unsigned comparison, and 0 otherwise. `dos_getdiskfreespace` gives the free clusters × sectors a cluster × bytes a sector from `INT 21h` `AH=36h`, and −1 for a drive DOS does not know, which the unsigned comparison passes.

### The flic player — `Launcher_FileReadable` (`000004ac`)

**A disc that is missing or not ready does not stop the launcher with DOS's `Abort, Retry, Fail?`: the open is retried once and then fails quietly, and the flics are left out.** `Launcher_FileReadable` (`000004ac`), (path), answers 0 for a null path. Otherwise it zeroes the words `CritErr_Count` (`00005a46`), `00005a48` and `00005a4a`, installs the critical-error handler (`CritErr_InstallHandler`, `00000bac`), opens the path `rb` with `fopen`, removes the handler (`CritErr_RemoveHandler`, `00000bec`), closes the file when it opened, and answers 1 when it opened and the handler counted no critical error, 0 otherwise.

- `CritErr_InstallHandler` (`00000bac`) keeps `DS` in `00005a50` for the handler, saves `INT 24h`'s protected-mode vector (`INT 21h` `AX=2502h`, `CL=24h`) in `00005a3c`–`00005a41` and its real-mode vector (`AX=2503h`) in `00005a42`, and points `INT 24h` at `CritErr_Handler` (`00000c07`) with `AX=2506h`, the extender's call that makes the interrupt always reach the handler in protected mode.
- `CritErr_Handler` (`00000c07`), DOS's critical-error interrupt, loads `DS` from `00005a50`, counts the error in `CritErr_Count`, keeps `AX` and `DI`, DOS's description of the error, in `00005a4a` and `00005a48`, and answers in `AL` 1, retry, while the count is below the word `CritErr_RetryLimit` (`00005a4c`, 2 in the image), and 3, fail, from then on.
- `CritErr_RemoveHandler` (`00000bec`) puts both saved vectors back with `AX=2507h`.

## The loop

**Each pass runs one program and then reads the next state from `data\exit.cfg`; the programs' exit statuses are not read.** `Launcher_State` (`00005854`), a dword, is 1 in the image, so the first pass is always state 1, whatever `exit.cfg` holds. While it is not 0, `Launcher_Main` takes the case for it from the table at `00005a20`:

| State | Runs |
|---|---|
| 1 | `data\prefs.cfg`, opened `rb` through `Launcher_PreferredPrefix`, closed at once when it opens; when it does not, `spawnl`(0, *p*, *p*, *a*, null) with *p* = `Launcher_PrefixPath`(`Launcher_OtherPrefix` when `Launcher_FlicPlayerFound` is set, `Launcher_PreferredPrefix` otherwise, `detect.exe`) and *a* = `-c` when it is set, the empty string otherwise. Then, with `Launcher_FlicPlayerFound` set, the intro flic ([the flics](#the-flics)). Then state 3's case. |
| 3, 6 | `Launcher_FlicPlayerFound` set again by `Launcher_FileReadable`(the probe path), then `spawnv` (`00001bf7`)(0, `go.exe`, `Launcher_BuildArgs`(argv, argc)) |
| 4 | `Launcher_FlicPlayerFound` set again; with it set, the end flic; then `go.exe` as in 3 |
| 2, 5 | `Launcher_FlicPlayerFound` set again; then `spawnv`(0, `dbsim.exe`, `Launcher_BuildArgs`(argv, argc)) |
| any other | nothing |

After each case `Launcher_ReadExitCfg` (`00000448`) opens `data\exit.cfg` `r+b` through `Launcher_PrefixPath`(`Launcher_PreferredPrefix`, …), asserting at severity 4 (line `0x67`) when it does not open, reads it with `fread`(`Launcher_State`, 4, 1, file) and closes it. The shell's codes are 0, 2 and 5 ([the ways out](startup.md#the-ways-out)), and the simulator's 3, 4, 6 or 0 ([the end](../simulation/main-loop.md#the-end--sim_shutdown-0005b568)), each written as an `int16`. A state outside 0–6 runs nothing and reads the file again, which no program has changed, so the launcher goes on reading it.

`go.exe` and `dbsim.exe` are given to `spawnv` by those names alone, with no drive or folder, and neither is given the state: `GO.EXE` reads `exit.cfg` itself ([the shell's start-up](startup.md#the-start-up), step 4). The launcher writes no `exit.cfg`, so on the first pass the shell starts from the code the last run left in it.

### A program's command line — `Launcher_BuildArgs` (`00000518`)

`Launcher_BuildArgs` (`00000518`), (argv, argc), fills the vector `Launcher_ChildArgs` (`000065c4`) and returns it: the launcher's own argc arguments, `argv[0]` included, then by `Launcher_State`:

| State | Added | With `Launcher_FlicPlayerFound` |
|---|---|---|
| 1, 3, 4, 6 (`go.exe`) | | `-a`, `-c` |
| 2 (`dbsim.exe`) | | `-c` |
| 5 (`dbsim.exe`) | `-D` | `-c` |

and last `-eggplant` and a null. So `ES` typed with switches passes them to both programs. `GO.EXE` takes `-a` as `-A`, which skips its own intro scripts, and `-c` as `-C`, which lets it honor `ALT_DRIVE` ([the shell's command line](startup.md#the-command-line)); `DBSIM.EXE` takes `-c` the same way and `-D` as a demo ([the simulator's command line](../simulation/main-loop.md#the-command-line)). Without the flic player, then, the shell plays its own intro scripts and neither program uses `ALT_DRIVE`.

## The end

With `Launcher_State` 0, `Launcher_Main` calls `Launcher_RestoreVideoMode` (`00000b83`), which sets text mode 3 (`INT 10h` `AX=3`), writes the saved equipment byte back to `0040:0010` and sets the saved mode (`INT 10h` `AH=0`); then `Mem_HeapOff` and returns. It returns no value: `EAX` holds what the last `INT 10h` left, which the startup passes to `exit`.

The two refusals of steps 2 and 4 end with `exit`(0) before the video mode is saved. A fatal assert (steps 4 and 6, and `Launcher_ReadExitCfg`) ends the program through `Assert_Report` (`000030a0`) without restoring the mode ([severities](../simulation/asserts.md#severities)).

## The files

| File | Through | What the launcher does |
|---|---|---|
| `data\es.got` | `Launcher_PreferredPrefix` | writes the 4-byte free-memory figure of step 2 |
| `data\prefs.cfg` | `Launcher_PreferredPrefix` | opens it to see that it is there; runs `detect.exe` in state 1 when it is not |
| `data\exit.cfg` | `Launcher_PreferredPrefix` | reads up to 4 bytes into `Launcher_State` after each program |
| `playflic.exe` | `Launcher_OtherPrefix` | opens it to see that it is there |
| `flic\intro3.dfl`, `flic\end.dfl` | the flic drive | passed to `PLAYFLIC.EXE` |

A path through a prefix is `Launcher_PrefixPath` (`00000304`)(prefix, path): the prefix followed by the path less a drive letter of its own, built in `00006584`. A path on a drive is `Launcher_DrivePath` (`00000360`)(drive, path): the drive's letter (drive + `0x40`) and `:` followed by the path less its own drive letter, built in `000065a4`.

## The installer's `ES.BAT`

The installer, `INSTALL\INSTALL.SCR`, writes `ES.BAT` in the install folder (its `%1` is the hard drive, `%2` the CD drive):

| Install | `ES.BAT` | `Launcher_PreferredPrefix` | `Launcher_OtherPrefix` |
|---|---|---|---|
| minimum (4.5 MB) and medium (11 MB) | `mode co80`, `ALT_DRIVE` = the hard drive, `PREFER_ALT=1`, changes to the CD drive, runs `escd`, changes back to the hard drive | the hard drive | empty: the CD's current folder |
| full (32 MB), which copies `GO.EXE`, `DBSIM.EXE`, `DETECT.EXE` and `ESCD.EXE` | `mode co80`, `ALT_DRIVE` = the CD drive, `PREFER_ALT` removed, runs `escd` | empty: the install folder | the CD drive |

So in a minimum or medium install the data files are on the hard drive and the programs, the flic player and the flics on the CD, and in a full install the programs and data are on the hard drive and the flics on the CD. `INSTALL\ES.BAT` and `INSTALL\HDSTUFF\ES.BAT` change to the parent folder and run `es`.

## Differences from ES2's `ES.EXE`

ES2's supervisor ([`ES.EXE`](../../retail/command-line.md#esexe--the-supervisor)) differs:

- it takes the next state from each program's exit status, where ES1's reads it from `data\exit.cfg` and passes it to neither program;
- it parses its own switches (`-s`, `-a`, `-L`, `-r`, `-SPRUNKNOWN`, `-X`, `-D`) and passes the shell `-X<state>` and the simulator a CD track counter `-R<n>`; ES1's parses none and passes its own arguments on whole;
- its memory check reads Windows' memory status and shows a message box; ES1's checks the extender's largest free block and prints to DOS;
- it has no disk check, no `es.got`, no `detect.exe` step, no flics and no `ALT_DRIVE`/`PREFER_ALT`, and state 4 runs the shell as 3 does, where ES1's plays the end flic first.

## Functions

| ES1 | Address | Its counterpart |
|---|---|---|
| `Launcher_Main` | `000005cc` | `ES.EXE`'s `WinMain` (`00401168`, ES.EXE) |
| `Launcher_PrefixPath`, `Launcher_DrivePath` | `00000304`, `00000360` | |
| `Launcher_HasDiskSpace`, `Launcher_ReadExitCfg`, `Launcher_FileReadable`, `Launcher_BuildArgs` | `000003bc`, `00000448`, `000004ac`, `00000518` | |
| `Launcher_SaveVideoMode`, `Launcher_RestoreVideoMode` | `00000b60`, `00000b83` | |
| `CritErr_InstallHandler`, `CritErr_RemoveHandler`, `CritErr_Handler` | `00000bac`, `00000bec`, `00000c07` | |
| `Mem_New`, `Mem_InitHeap`, `Mem_HeapOff` | `00001664`, `000015dc`, `00001658` | `ES1GO`'s `Mem_New` (`000019f0`), `Mem_InitHeap` (`0000195f`), `Mem_HeapOff` (`000019e1`) |
| `Assert_SetLogDir`, `Assert_Report` | `00001814`, `000030a0` | `ES1GO`'s (`0000035e`, `0004f498`) |
| `X32_LargestFreeBlock` | `00002e63` | `ES1GO`'s (`0004f738`) |
| `strupr`, `puts` | `00002294`, `00002248` | |
| `spawnl`, `spawnv`, `exit`, `getenv`, `printf`, `fopen`, `fread`, `fwrite`, `fclose`, `strcat` | `00001be0`, `00001bf7`, `00001b50`, `000022bc`, `0000232e`, `000020d8`, `00002edc`, `0000211c`, `00002034`, `000032cc` | the runtime's |
| `dos_getdrive`, `dos_setdrive`, `dos_getdiskfreespace` | `00001f9c`, `00001fab`, `00003258` | the runtime's |

## Globals

| | Address |
|---|---|
| `Launcher_State`, dword, 1 in the image | `00005854` |
| `Launcher_FlicPlayerFound`, byte | `00005858` |
| `Launcher_PreferredPrefix`, `Launcher_OtherPrefix`, 4 bytes each | `0000657c`, `00006580` |
| `Launcher_ChildArgs`, the vector | `000065c4` |
| `Launcher_PrefixPath`'s and `Launcher_DrivePath`'s buffers | `00006584`, `000065a4` |
| `Launcher_SavedVideoMode`, `Launcher_SavedEquipmentByte` | `00005a38`, `00005a39` |
| the saved `INT 24h` vectors, protected-mode (6 bytes) and real-mode | `00005a3c`, `00005a42` |
| `CritErr_Count`, the error's `DI` and `AX`, `CritErr_RetryLimit`, words | `00005a46`, `00005a48`, `00005a4a`, `00005a4c` |
| `Mem_UseHeap` | `00005c18` |
| `DS` for the handler | `00005a50` |
| the case table of the loop, `Launcher_BuildArgs`' | `00005a20`, `00005884` |

## Open

- **Open:** whether `00000b60`–`00000c43` are `es.cpp`'s code or an object file of their own; no assert and no zero fill stands between `Launcher_Main`'s `RET` and `00000b60`, a 4-byte boundary.
- **Open:** what `PLAYFLIC.EXE`'s `-B1000000` and `-S83` do, and what `DETECT.EXE` does with `-c` and writes; neither program is in the Ghidra project.
- **Open:** what reads `data\es.got`: a search of `GO.EXE`, `DBSIM.EXE`, `DETECT.EXE` and `PLAYFLIC.EXE` for the string `es.got` finds none.
- **Open:** whether `fread` (`00002edc`) keeps the two bytes of `exit.cfg`'s `int16` when it reads a 4-byte item from a 2-byte file, so that `Launcher_State` takes the code in its low word. A retail check: start a mission from the shell under DOSBox-X; the simulator starting shows the launcher read 2.
- **Open:** a retail check of the flics under DOSBox-X: from a full install with the CD mounted, `ES` plays `INTRO3.DFL` and then opens the main menu without the shell's intro scripts; with the CD drive empty, the shell's intro scripts play instead. Letting the HERC be destroyed in a career mission, the simulator leaving 4, plays `END.DFL` before the shell.
