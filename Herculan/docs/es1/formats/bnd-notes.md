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
| `CTL_MGR.BND` | 6 | `0009adec` | `CtlMgr_LoadBnd` (`00010530`); offset 4, the word `CtlMgr_QueueSize` (`0009adf0`), 100 (the bytes `6f e6 8e 2e 64 00`), is the size of the shell's mouse queues ([the mouse queue](../shell/startup.md#the-mouse-queue)) |
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

### `BND\MAP.BND` and `BND\SHELLMAP.BND`

The war map's map's constants ([the map](../shell/map.md#the-tuning-files)). `MAP.BND`, at `0009a420`, the base class's, the same numbers ES2's `Map` has in its image ([the six buttons](../../retail/shell/mission-map.md#the-six-buttons)):

| Offset | Type | Value | Read by |
|---|---|---|---|
| 0 | `uint32` | the [first dword](#the-first-dword) | |
| 4 | `int32` | 100000 | `Map_PanStepFor` (`0000911c`), the step's top |
| 8 | `int32` | 5000 | `Map_PanStepFor`, the step's bottom |
| 12 | `int32` | 5,000,000 | `MapBnd_PanLimit` (`0009a42c`): `Map_PanNorth` (`000090be`), `Map_PanSouth`, `Map_PanWest` and `Map_PanEast`, the position's bound |
| 16 | `int32` | 50000 | `MapBnd_ZoomStep` (`0009a430`): `Map_ZoomIn` (`00009043`) and `Map_ZoomOut` (`00009010`), the altitude's step |
| 20 | `int32` | 50000 | `Map_PanStepFor`, the altitude's bottom; `Map_ZoomIn`, the least altitude |
| 24 | `int32` | 1,000,000 | `Map_PanStepFor`, the altitude's top; `Map_ZoomOut`, the greatest altitude |
| 28 | `int32` | 200000 | `Map_Ctor` (`00008cfd`), the starting altitude |

`SHELLMAP.BND`, at `0009a44c`, 142 bytes; the offsets the shell's map reads, the ticks being [the map's clock's](../shell/map.md#the-tick), 60 a second:

| Offset | Type | Value | Global | Read by |
|---|---|---|---|---|
| 0 | `uint32` | the [first dword](#the-first-dword) | | |
| 4 | `int32` | 200000 | `ShellMapBnd_GridSpacing` (`0009a450`) | `ShellMap_PaintGrid` (`0000ab58`), the grid's world spacing |
| `0xc` | `int16` | 0 | `ShellMapBnd_FirstMarkerFrame` (`0009a458`) | `ShellMap_PaintNavMarkers` (`0000bb1c`), the first marker's `dba\icons` frame |
| `0xe` | `int16` | 8 | `ShellMapBnd_LastMarkerFrame` (`0009a45a`) | the same, the last |
| `0x12` | `int16` | 18 | `ShellMapBnd_PlayerFrame` (`0009a45e`) | `ShellMap_PaintSquad` (`0000bc47`), the frame for member ref 0 |
| `0x14` | `int16` | 27 | `ShellMapBnd_SquadmateFrame` (`0009a460`) | the same, for any other |
| `0x1c` | `int16` | 72 | `ShellMapBnd_Side1BaseFrame` (`0009a468`) | `ShellMap_PaintBases` (`0000b840`), a side-1 base's frame |
| `0x1e` | `int16` | 73 | `ShellMapBnd_Side1SmallBaseFrame` (`0009a46a`) | the same, of type 5, 6 or 8 |
| `0x20` | `int16` | 74 | `ShellMapBnd_BaseFrame` (`0009a46c`) | the same, another side's |
| `0x22` | `int16` | 75 | `ShellMapBnd_SmallBaseFrame` (`0009a46e`) | the same, of type 5, 6 or 8 |
| `0x24` | `int16` | 7 | `ShellMapBnd_SquadGadgetSize` (`0009a470`) | `ShellMap_BuildGadgets` (`0000d1bb`), a squad member's gadget size; `ShellMap_PaintSquad`, the icon's size |
| `0x28` | `int16` | 9 | `ShellMapBnd_MarkerSize` (`0009a474`) | `ShellMap_PaintNavMarkers`, a marker's size |
| `0x2a` | `int16` | 13 | `ShellMapBnd_BaseGadgetSize` (`0009a476`) | `ShellMap_BuildGadgets`, a base's gadget size; `ShellMap_PaintBases`, the icon's size |
| `0x2c` | `int16` | 7 | `ShellMapBnd_SmallBaseGadgetSize` (`0009a478`) | the same, a base of type 5, 6 or 8 |
| `0x30` | `int16` | 14 | `ShellMapBnd_ShapeRamp` (`0009a47c`) | `ShellMap_PaintShapes` (`0000aff0`), the colour ramp it sets |
| `0x32` | byte | 1 | `ShellMapBnd_DrawShapes` (`0009a47e`) | `ShellMap_Constructor` (`000094d9`): non-zero draws the zone's shapes, 0 the 2D icons |
| `0x34` | `int16` | 10 | `ShellMapBnd_LineSpacing` (`0009a480`) | the constructor, a text label's height |
| `0x36` | `int32` | 1000 | `ShellMapBnd_FullViewMargin` (`0009a482`) | `ShellMap_FitFullView` (`0000ce8a`), the margin |
| `0x3a` | `int32` | 20000 | `ShellMapBnd_SquadViewMargin` (`0009a486`) | `ShellMap_FitSquad` (`0000d0a9`), the margin |
| `0x3e` | `int16` | 3 | `ShellMapBnd_PenRadius` (`0009a48a`) | `ShellMap_PaintPath` (`0000b4ec`), the pen's half size |
| `0x40` | `int32` | 100000 | `ShellMapBnd_SquadWorldSize` (`0009a48c`) | `ShellMap_PaintSquad`, the icon's world size |
| `0x44` | `int16` | 1 | `ShellMapBnd_SquadPixelBelow` (`0009a490`) | the same, the size below which it is one pixel |
| `0x46` | `int32` | 25000 | `ShellMapBnd_BaseWorldSize` (`0009a492`) | `ShellMap_PaintBases`, a base's world size |
| `0x4a` | `int32` | 25000 | `ShellMapBnd_SmallBaseWorldSize` (`0009a496`) | the same, of type 5, 6 or 8 |
| `0x4e` | `int16` | 0 | `ShellMapBnd_BasePixelBelow` (`0009a49a`) | the same, the size below which it is one pixel |
| `0x50` | `int16` | 0 | `ShellMapBnd_SmallBasePixelBelow` (`0009a49c`) | the same, of type 5, 6 or 8 |
| `0x52` | `int32` | 10 | `ShellMapBnd_ClearWait` (`0009a49e`) | `ShellMap_Clear` (`0000aa3c`), the slow paint's wait |
| `0x56` | `int32` | 10 | `ShellMapBnd_ShapesWait` (`0009a4a2`) | `ShellMap_PaintShapes`, the same |
| `0x5a` | `int32` | 1 | `ShellMapBnd_GridLineWait` (`0009a4a6`) | `ShellMap_PaintGrid`, the same after each line |
| `0x5e` | `int32` | 60 | `ShellMapBnd_FullViewHold` (`0009a4aa`) | `ShellMap_IntroStep` (`00009cfb`), the full view's hold |
| `0x62` | `int32` | 120 | `ShellMapBnd_ZoomTicks` (`0009a4ae`) | the same, each zoom's ticks |
| `0x66` | `int32` | 30 | `ShellMapBnd_MemberTicks` (`0009a4b2`) | the same, between squad members |
| `0x6a` | `int32` | 50000 | `ShellMapBnd_LegDistanceUnit` (`0009a4b6`) | the same, a leg's distance unit |
| `0x6e` | `int32` | 30 | `ShellMapBnd_LegTicksPerUnit` (`0009a4ba`) | the same, a leg's ticks per unit |
| `0x72` | `int32` | 30 | `ShellMapBnd_SectionPause` (`0009a4be`) | the same, after the squad and after a stop's lines |
| `0x76` | `int32` | 30 | `ShellMapBnd_StepPause` (`0009a4c2`) | the same, between lines and before a leg |
| `0x7a` | `int32` | 3 | `ShellMapBnd_TicksPerChar` (`0009a4c6`) | the same, a line's ticks per character |
| `0x7e` | `int16` | 3 | `ShellMapBnd_GadgetTextMargin` (`0009a4ca`) | `MapGadget_Ctor` (`0000da8d`), the text's margin on each side |
| `0x80` | `int16` | 10 | `ShellMapBnd_CalloutHeight` (`0009a4cc`) | `ShellMap_PlaceCallout` (`0000d72b`), the box's height |
| `0x82` | `int16` | 5 | `ShellMapBnd_CalloutGap` (`0009a4ce`) | the same, the gap |
| `0x84` | `int16` | 5 | `ShellMapBnd_PointerWidth` (`0009a4d0`) | the same, the pointer's width |
| `0x86` | `int32` | 50000 | `ShellMapBnd_BoundsMargin` (`0009a4d2`) | `ShellMap_LoadScriptDat` (`0000c068`), the bounds' margin |
| `0x8a` | `int32` | 75000 | `ShellMapBnd_ShapeArenaSize` (`0009a4d6`) | `ShellMap_LoadResources` (`000093b3`) and `ShellMap_LoadShapeRoots` (`000092b9`), the shape arena's size |

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
- **Open:** what the shell reads of `ALERT`, `CTL_ALRT`, `CAL_ALRT`, `CTL_MGR` but its offset 4, and `IMDEVICE`, of `MAIN.BND`'s offsets 0, 4 and 16, and of `SHELLMAP.BND`'s 8 to `0xb`, `0x10`, `0x16` to `0x1b`, `0x26`, `0x2e` and `0x33`, whose globals (`0009a454` to `0009a47f`) `es2_xref.py --decode` finds no reference to; what the byte `Shell_LowMemory` (`0007fd09`) changes.
- **Open:** whether anything reads the first dword, and what wrote it.
