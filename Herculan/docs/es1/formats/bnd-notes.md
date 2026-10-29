# .BND — per-subsystem tuning files

ES1's volumes hold `.BND` files as ES2's do: 63 in `SIMVOL0.VOL`'s `bnd\` and 8 in `SHELL0.VOL`'s `BND\`. The shell reads all eight of its own at start-up; the simulator's `CAM.BND` holds the constants its camera code is built with. ES2's simulator reads none of its own ([ES2's `.BND`](../../retail/formats/bnd-notes.md)). Addresses are `ES1DBSIM` image addresses unless `ES1GO` is named ([the X-32 executable](x32-executable.md#the-image)).

## The extension names

Both programs keep their resource folder names as strings each followed by a pointer to it, the pointers being what the code pushes. `ES1DBSIM`'s `bnd` is at `000980bc`, its pointer at `000980c0`. `ES1GO`'s pointer to `bnd`, `00086808`, is pushed at eight places, the [shell's loaders](#the-shells-files).

## The first dword

Every one of the 71 files opens with a dword that, read as seconds since 1970, falls in June to October 1994. For each of `SIMVOL0.VOL`'s 63 it is within four days of the archive entry's own MS-DOS timestamp ([the entry prefix](../../retail/formats/vol-archive.md#the-per-entry-prefix--fixed-9-bytes)); `SHELL0.VOL`'s eight all carry the entry timestamp 1994-10-17 and dwords 2 to 15 days earlier. `CAM.BND`'s is `0x2DFFC4F4`, 1994-06-16 03:20:20 UTC.

## The shell's files

Eight functions each read one whole file into a global with `Ovl_ReadFile` (`ES1GO 00051918`: `ES1DBSIM`'s, its asserts at severity 4), and each is registered at phase 1 with `RegisterSubsystemLoader` (`ES1GO 0000fca0`) by its own static initialiser; `es2_xref` finds these eight calls of it. `Shell_Main` (`ES1GO 00012ec7`) runs phase 1 with `Subsystem_RunPhase` (`ES1GO 0000fcda`) once, just after it mounts the volumes.

| File | Bytes | Into | Loader |
|---|---|---|---|
| `ALERT.BND` | 6 | `0009a318` | `Alert_LoadBnd` (`00002204`) |
| `CTL_ALRT.BND` | 134 | `0009a34c` | `CtlAlert_LoadBnd` (`00006024`) |
| `CAL_ALRT.BND` | 70 | `0009a3d4` | `CalAlert_LoadBnd` (`00006eec`) |
| `MAP.BND` | 32 | `0009a420` | `Map_LoadBnd` (`00008cd4`) |
| `SHELLMAP.BND` | 142 | `0009a44c` | `ShellMap_LoadBnd` (`00009210`) |
| `CTL_MGR.BND` | 6 | `0009adec` | `CtlMgr_LoadBnd` (`00010530`) |
| `MAIN.BND` | 20 | `0009ae18` | `Main_LoadBnd` (`00012704`) |
| `IMDEVICE.BND` | 10 | `000a0458` | `ImDevice_LoadBnd` (`0002562c`) |

### `BND\MAIN.BND`

| Offset | Value | Read by |
|---|---|---|
| 0 | the [first dword](#the-first-dword) | |
| 4 | `00 00 01 00` | |
| 8 | 3,700,000 | `Shell_Main`: when the heap is smaller, it sets the byte `Shell_LowMemory` (`0007fd09`) |
| 12 | 2,400,000 | `Shell_Main`: when the heap is smaller, a severity-5 assert (`src\main.cpp` line `0x219`) reports the shortfall and ends the program |
| 16 | 500,000 | |

The heap is what `Mem_InitHeap` gives for the dword `0007e738`, also 3,700,000 ([who makes the heap](../simulation/memory.md#who-makes-the-heap)).

## The simulator's files

`CAM.BND` is [below](#bndcambnd). For the other 62 no loader was found ([Open](#open)). Six share a name with a shell file and hold other bytes: `ALERT`, `CAL_ALRT`, `CTL_ALRT`, `CTL_MGR`, `IMDEVICE` and `MAIN`, the last 30 bytes where the shell's is 20. `GRENADE.BND`, `GUNLIST.BND` and `SMOKE.BND` are byte for byte ES2's files of those names.

## `bnd\CAM.BND`

20 bytes, where ES2's is 24 ([ES2's `CAM.BND`](../../retail/formats/bnd-notes.md#cambnds-full-24-byte-record)). Every value after the first dword is a constant of the simulator's [camera](../simulation/external-views.md#the-camera), in the file's order:

| Offset | Type | ES1 | Constant in `ES1DBSIM` | ES2 |
|---|---|---|---|---|
| 0 | `uint32` | `0x2DFFC4F4` | the [first dword](#the-first-dword) | `0x3134D036` |
| 4 | `int16` | 2500 | the orbit's least distance, `Cam_Update` | 2500 |
| 6 | `int16` | 30000 | the orbit's greatest distance, `Cam_Update` | 30000 |
| 8 | `int16` | `0x800` | the angle rates' scale, `Cam_ApproachRate` | `0x800` |
| 10 | `int16` | `0xc0` | the angle rates' step, `Cam_ApproachRate` | `0xc0` |
| 12 | `int16` | `0x400` | the zoom rate's and speed's scale, `Cam_ApproachZoom` | `0x400` |
| 14 | `int16` | `0x50` | the zoom rate's and speed's step, `Cam_ApproachZoom` | `0x50` |
| 16 | `int16` | `0x3000` | the pitch limit, `Cam_Update` modes 0 and 1 | `0x3000` |
| 18 | `int16` | 100 | the eye's least height, `Cam_Update` modes 0 and 1 | 550 |
| 20–23 | — | — | | 500, 8000 |

## Open

- **Open:** whether `ES1DBSIM` reads any `bnd\` file. `es2_xref` finds no reference to its `bnd` pointer `000980c0`, where the same sweep finds both pushes of `dmg`'s pointer `000980e0`; a case-insensitive search of the image's bytes finds no `cam` or `.bnd` string, where it finds `dmg` and `damage.cpp`.
- **Open:** what the shell reads of seven of its eight files, and of `MAIN.BND`'s offsets 0, 4 and 16; what the byte `Shell_LowMemory` (`0007fd09`) changes.
- **Open:** whether anything reads the first dword, and what wrote it.
