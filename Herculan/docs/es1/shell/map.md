# GO.EXE — the war map's map

**The war map's top area shows the mission's ground from above: the zone's shapes drawn in 3D, a label beside each squad member and each base, the mission's objective lines, and a box with a pointer beside the label the player has picked.** It is one C++ object, `Shell_MissionMap` (`000809a4`), the descendant of ES2's briefing map ([ES2's mission map](../../retail/shell/mission-map.md)): the same `Map` base with its camera and pan step, the same three camera positions and intro states, on ES1's layout and with ES1's contents, and an introduction that shows the mission's objective lines. This page covers the object's build, its running and its end. The screen around it is [the war map](war-map.md).

Addresses are `ES1GO` image addresses ([the X-32 executable](../formats/x32-executable.md#the-image)). The functions here remove their own arguments, so argument lists are in stack order ([calling conventions](../formats/compiler.md#calling-conventions)).

## Where the object files start and end

The map is two object files, as ES2's is:

- **`map.cpp`, `00008cb0`–`0000920f`**: the base class `Map`. It opens after the 2 zero bytes past the previous file's `RET 8` at `00008cab`, with two destructor copies (`00008cb0`, `00008cc2`), and ends with its static initialiser `Map_StaticInit` (`00009174`, static constructor table entry `0009a228`) and the 1 zero byte past its `RET` at `0000920e`.
- **`src\shellmap.cpp`, `00009210`–`0000ddc7`**: the class `ShellMap`, the source path its asserts push. It opens with `ShellMap_LoadBnd` (`00009210`) and ends with its static initialiser `0000dcbd` (entry `0009a22c`); `Context_GetTransform` (`0000ddc8`) follows.

## The tuning files

The map's constants come from two `.BND` files the shell reads at start-up, `MAP.BND` into `0009a420` and `SHELLMAP.BND` into `0009a44c` ([the shell's files](../formats/bnd-notes.md#the-shells-files)); the values named here are the retail files', and the files' tables are in [`bnd-notes.md`](../formats/bnd-notes.md#bndmapbnd-and-bndshellmapbnd). **The byte `ShellMapBnd_DrawShapes` (`0009a47e`, `SHELLMAP.BND` offset `0x32`) picks the map's drawing: 1 in the retail file, the zone's shapes in 3D; 0, the 2D icons that `Bitmap_BlitRotatedUnpacked` draws.** `ShellMap_Constructor` copies it to `ShellMap_DrawShapes` (`000aa2b6`), which the draw step `0000aff0` tests. `es2_xref.py --decode` finds one instruction holding `0009a47e`'s address, the constructor's read; the file's load writes it through `Ovl_ReadFile`'s buffer pointer.

## The base class

`Map_Ctor` (`00008cfd`), (map, rect), is ES2's `Map_Ctor` (VSHELL `0041fdc8`, [the object](../../retail/shell/mission-map.md#the-object)) with every field 4 bytes lower, its vtable pointer at `+0x4a` where ES2's is at `+0`:

| Field | ES1 | ES2 |
|---|---|---|
| the `GLViewport`, rect copied to its `+0x210`–`+0x21c`, centre `+0x220`/`+0x224` the negated half size, `+0x208` 1 | `+0` | `+4` |
| the `TSCamera` | `+8` | `+0xc` |
| focal shift, 7 | `+0xc` | `+0x10` |
| camera position x, y | `+0xe`, `+0x12` | `+0x12`, `+0x16` |
| altitude, `MapBnd_StartAltitude` (`0009a43c`), 200000 | `+0x16` | `+0x1a` |
| pan x, y | `+0x36`, `+0x3a` | `+0x3a`, `+0x3e` |
| pan step | `+0x3e` | `+0x42` |
| width, height (right − left, bottom − top) | `+0x42`, `+0x46` | `+0x46`, `+0x4a` |

The camera is `TSCamera_Ctor`(0, `Map_CameraOrientation` (`0009a444`), the position `+0xe`, `0x80`, 7, the viewport); `Map_StaticInit` sets the orientation to `{0xc000, 0, 0}` and registers `Map_LoadBnd` at phase 1. `Map_PanStepFor` (`0000911c`) is ES2's formula with `MAP.BND`'s constants, the same numbers as ES2's: ((altitude − 50000 `>>` 3) × (100000 − 5000 `>>` 3)) / (1000000 − 50000 `>>` 3) × 8 + 5000. `Map_Dtor` (`00008e9c`) stores `MapVtable` (`0007e1ce`), destroys the viewport through its slot `+0x1c` with 3 and the camera through slot `+4` of its table at `+0x12` with 3.

`MapVtable` holds 0 at `+0` and `+4` and the six camera moves at `+8`–`+0x1c`, `Map_ZoomIn` (`00009043`), `Map_ZoomOut` (`00009010`), `Map_PanNorth` (`000090be`), `Map_PanSouth` (`000090df`), `Map_PanWest` (`00009076`) and `Map_PanEast` (`0000909d`); `ShellMapVtable` (`0007e268`) holds `ShellMap_Paint` (`00009ca7`) at `+4` ([a paint](#a-paint)) and seven moves at `+8`–`+0x20`, `ShellMap_ZoomIn` (`0000d43c`) to `ShellMap_ResetPan` (`0000d56c`) ([the camera moves](#the-camera-moves)).

## Building it

`Shell_PrepareMission` (`0002a523`) builds the map after loading a mission, with `ShellMap_Constructor` (`000094d9`)(0, the rect `000a02d4`). It allocates `0x4c8` bytes, runs `Map_Ctor`, constructs the member arrays, stores `ShellMapVtable`, and:

1. sets the intro's slow-paint byte `+0x346` to 1 and its squad, path and marker counts `+0x347`–`+0x349` to 0 ([the intro](#the-intro)), and copies `ShellMapBnd_DrawShapes` to `ShellMap_DrawShapes`;
2. loads `data\object.str` (line 6 of `cfg\filename.bin`, `Text_GetString`(`0009afb8`, 6), through `Path_ApplyAltDrive`) into the string table `+0x138` with `StringTable_LoadFromFile` (`0001026a`), and its group 0, the objective lines `Mission_WriteObjectives` wrote, into `+0x148` with `StringTable_LoadGroup` (`000103b8`), the count to `+0x198`. ES2's map reads `data\mission.str` there;
3. loads `str\maplabel` into the table `+0x19a`, group 0 into `+0x1aa` (count `+0x272`) and group 1 into `+0x274` (count `+0x33c`). Retail's `MAPLABEL.STR` holds two groups: YOU and SQUAD MEMBER, then the base types' names in type order, SUPPLY DEPOT, HERC FACTORY, MINING FACILITY, GENERATOR, HERC HANGAR, LISTENING POST, RADAR TOWER, REFINERY and GUN TOWER;
4. builds 22 labels with `Label_Ctor`(0, `ShellMap_LabelFont` (`000aa2c0`)), each with a zero margin, over rows of the rect's width `ShellMapBnd_LineSpacing` (`0009a480`, 10) pixels high: `+0x362` over the top row with flags 2 (centred), then twenty at `+0x366` with flags 1, the first over the top row as well and each next one a row lower; and the callout label `+0x482`, its fill colour `+0x1c` `0x3f`;
5. builds the sun: a `0x34`-byte light, the two `TSContext`s at `+0` and `+0x1e` of a `TSLight` (vtable `TSLightVtable` (`0007e2aa`) at `+0x12`), then `TSSunLightVtable` (`0007e2ca`) and `+0x16` 1; slot `+8`, `maybe_TSLight_SetIntensity`, sets 100; the words `+0xc`, `+0xe`, `+0x10` become 7000, 0, −7000; `Light_Register`. It is kept at `+0x4e`;
6. clears the array pointers `+0x52`, `+0x58`, `+0x64`, `+0x6e`, `+0x5e` and `+0xd0`, then runs [the load](#what-it-reads), `ShellMap_FitFullView` and `ShellMap_FitSquad` ([the views](#the-camera-views));
7. clears the intro state `+0x33e` and the dword `+0x340`; makes the gadget window `+0x134` with `CTLWindow_Ctor` (`0001144a`)(0, the rect) and makes it the active root with `Widget_SetActiveRoot` (`0001070d`), which stores it in `Widget_ActiveRoot` (`0007e5d8`), ES1DBSIM's `Widget_ClearPressed` without its pressed-index write; clears the gadget count `+0x47e` and the dword `+0x4bc`;
8. builds [the gadgets](#the-gadgets) with `ShellMap_BuildGadgets` (`0000d1bb`), sets the selected gadget `+0x480` to −1 and runs `ShellMap_PlaceCallout` (`0000d72b`), which returns at once for −1;
9. sets the byte `+0x4c1`, the intro's closing paints, to 2 and its end byte `+0x4c0` to 0.

The mouse queue the gadgets are hit-tested through is started by `Shell_PrepareMission` itself, with `maybe_MouseEventQueue_Start` (`00010559`), before the constructor runs ([the war map](war-map.md#leaving-it)).

## What it reads

`ShellMap_LoadScriptDat` (`0000c068`), (map), ES2's `ShellMap_LoadScriptDat` (VSHELL `004243d7`, [what it reads](../../retail/shell/mission-map.md#what-it-reads)), opens `data\script.dat` (line 3 of `cfg\filename.bin`) with `FileRStream_Ctor`, asserting twice at severity 5 (lines `0x4c1` and `0x4c2`) when the stream's status is set, and reads the 20-byte header and the nine blocks ([ES1's `script.dat`](../formats/script-dat.md#writing)):

| Block | Kept |
|---|---|
| header | word 3, the mission's kind ([the list](single-mission.md#the-list)): `+0x4c7` is 1 for kind 0, 1 or 2, RECONNAISSANCE, SCOUT and PATROL, else 0; it hides side-1 bases ([the icons](#the-icons)) |
| 1, points | at `+0x52`, 12 bytes each, the count at `+0x56`; each widens the bounds |
| 2, headings | at `+0x58`, the count at `+0x5c` |
| 3, waypoint groups | at `+0x64`, 6 bytes each, the count at `+0x68`: the waypoint count and a pointer to the waypoints' `int16` point refs |
| 4, trigger areas; 5, actions; 7, flyers | skipped |
| 6, mechs | at `+0x6e`, `0x62` bytes each, the count at `+0x72` |
| 8, bases | at `+0x5e`, 16 bytes each, the count at `+0x62` |
| 9, groups | record 0, the player's squad, at `+0x74`, `0x52` bytes; each later record is read and dropped after its members are walked |

**The bounds** start at `0x7fffffff` (low corner `+0x4a4`/`+0x4a8`) and `0x80000000` (high corner `+0x4b0`/`+0x4b4`); every point's x and y widen them, and then each side moves out by `ShellMapBnd_BoundsMargin` (`0009a4d2`, 50000). The z words `+0x4ac` and `+0x4b8` take the margin too, though nothing sets them before.

**The bases' sides.** For each group record after the first whose roster `+0x28` is 2, each of its eight member refs (`+0x34`) that is not negative writes the group's side `+0x44` into that base's `+8`, as ES1DBSIM's load does ([reading](../formats/script-dat.md#reading)); a later group overwrites an earlier one, and the ref is not checked against the block's count.

Then `ShellMap_LoadZoneShapes`(map, header word 0, header word 1) loads the zone's shapes ([the shell's load](../formats/zone-terrain.md#the-shells-load)). **The nav path** at `+0x6a` is a 6-byte copy of the waypoint group the squad's record names at `+0xa6` (group `+0x32`), or null when that is −1. `ShellMap_PlaceSquad` (`0000cab1`) then places the squad ([`formations-dat.md`](../formats/formations-dat.md)), into the eight 12-byte positions at `+0xd4`, one for each member ref at `+0xa8`.

## The camera views

Two of ES2's three camera positions are fitted, each to a box of world points widened by the margin *m* on every side, with one formula:

```
halfX = ((maxX + m) - (minX - m)) >> 1      centreX = (minX - m) + halfX
halfY = ((maxY + m) - (minY - m)) >> 1      centreY = (minY - m) + halfY
altitude = max((halfX << shift) / 160, (halfY << shift) / 100) << 1
```

*shift* being the focal shift `+0xc`, 7. ES2's `ShellMap_FitSquad` (VSHELL `004252e1`) divides by half the map's width and height `+0x46`/`+0x4a` where ES1's divides by 160 and 100, and does not double ([the camera](../../retail/shell/mission-map.md#the-camera)).

| View | Function | Box | *m* | Kept at |
|---|---|---|---|---|
| full | `ShellMap_FitFullView` (`0000ce8a`) | every base's point (`+2`) that is not −1, every squad position whose member ref is not −1, and every path point that is not −1 | `ShellMapBnd_FullViewMargin` (`0009a482`), 1000 | `+0x34a`, `+0x34e`, `+0x352` |
| squad | `ShellMap_FitSquad` (`0000d0a9`) | the squad positions whose member ref is not −1 | `ShellMapBnd_SquadViewMargin` (`0009a486`), 20000 | `+0x356`, `+0x35a`, `+0x35e` |

ES2's full view fits the mission's bounds ([the camera](../../retail/shell/mission-map.md#the-camera)); `es2_fieldscan.py` finds ES1's bounds at `+0x4a4`–`+0x4b4` read by `ShellMap_PaintGrid`, which outlines them ([a paint](#a-paint)).

## The gadgets

**Each squad member and each base a group names gets a small clickable square with its name.** `ShellMap_BuildGadgets` (`0000d1bb`) puts the camera at the full view (`+0xe`, `+0x12`, `+0x16` from `+0x34a`–`+0x352`), installs the viewport and the camera with `Map_PushContext` (`00008f65`) and `ShellMap_InstallCamera` (`00008fa2`) ([a paint](#a-paint)), then builds one `MapGadget` with `MapGadget_Ctor` (`0000da8d`)(0, text, size, map, the world position):

- for each of the eight squad slots whose member ref `+0xa8` is not −1: text YOU for slot 0 and SQUAD MEMBER for the rest, size `ShellMapBnd_SquadGadgetSize` (`0009a470`, 7), the slot's position at `+0xd4`. A member at or above the squad size gets the position `ShellMap_PlaceSquad` left unwritten;
- for each base whose side `+8` is not −1: the type's name, group 1's line indexed by the type `+0`, unchecked against the group's count; size `ShellMapBnd_SmallBaseGadgetSize` (`0009a478`, 7) for types 5, 6 and 8 (LISTENING POST, RADAR TOWER, GUN TOWER) and `ShellMapBnd_BaseGadgetSize` (`0009a476`, 13) for the others; the base's point `+2`.

Each goes into the array `+0x3b6`, counted at `+0x47e`, which has room for 50, and into the window `+0x134`'s clickables with `Widget_RegisterClickable` (`0001084f`). Then `Map_PopContext` (`00008f86`) puts the render context back. Bases of side 1 get gadgets in every mission kind, though `+0x4c7` hides their icons ([the icons](#the-icons)).

`MapGadget_Ctor` allocates `0x38` bytes when given none (0 when that fails), runs `Widget_CtorRect` (`00011011`) on the rect `0009a524`, stores `CTLButtonControlVtableShellMapCopy` (`0007e3c8`) at `+0x1c` and clears the word `+0x19`, then stores `MapGadgetVtable` (`0007e3f8`) at `+0x1c`, the map at `+0x20`, the world position at `+0x24`–`+0x2c`, the size at `+0x30`, the text at `+0x32` and the width `+0x36`, the text's width by `HudFont_MeasureString` in `ShellMap_LabelFont` plus twice `ShellMapBnd_GadgetTextMargin` (`0009a4ca`, 3), and runs `MapGadget_Place` (`0000dbfb`) ([picking a gadget](#picking-a-gadget)).

### The callout

`ShellMap_PlaceCallout` (`0000d72b`), (map), lays out the box for the selected gadget, index `+0x480`, and returns for −1. With *g* the gadget's rect's left and top (`+0`, `+4`), *s* its size and *w* its width, the box `ShellMapBnd_CalloutHeight` (`0009a4cc`, 10) high, the gap `ShellMapBnd_CalloutGap` (`0009a4ce`, 5) and the pointer's width *p* `ShellMapBnd_PointerWidth` (`0009a4d0`, 5):

- **a gadget right of the viewport's centre** (`(x1 − x0) / 2 + x0` of the viewport's rect `+0x210`–`+0x21c`, below *g*.x): the box spans x from *g*.x − gap − 1 − *w* to *g*.x − gap − 1, and the pointer, the three points at `+0x486`, is (*g*.x − gap + *p*, *h*/2), (*g*.x − gap, *h*/2 + *p*/2) and (*g*.x − gap, *h*/2 − *p*/2);
- **otherwise** the box spans x from *g*.x + *s* + gap to that plus *w*, and the pointer is (box left − *p* − 1, *h*/2), (box left − 1, *h*/2 − *p*/2) and (box left − 1, *h*/2 + *p*/2).

Each pointer y then gains *g*.y + *s*/2 − *h*/2; the box's y runs from *g*.y + *s*/2 − *h*/2 to that plus *h*; and the box and the three points all gain the viewport rect's left and top. The callout label `+0x482` takes the box with flags 2, centred, and a zero margin.

## Running it

**Once built, the map plays an introduction: it shows the whole area, zooms in on the squad and shows its members one at a time, flies along the nav path while the objective lines come up one at a time across the top, and zooms back out. Then it lists the objectives down its left side, and is redrawn when a button, a key or a click changes it.**

### The tick

`Shell_TickScreen` (`0003c115`) runs `ShellMap_IntroStep` (`00009cfb`) once a shell frame while the byte `000809a8` is above 1 and the byte `0007fcd4` is clear, lowering `000809a8` each time ([drawing a screen](menus.md#drawing-a-screen)). With `000809a8` at 1 it sets it back to 2 while the intro's end byte `+0x4c0` is clear, so the intro steps every frame, and to 3, two more steps, when the redraw byte `+0x4c2` is set. A step after the intro paints once and clears `+0x4c2`. `000809a8` starts at 3 ([opening it](war-map.md#opening-it)), and the map's keys and buttons set it to 3 ([the camera moves](#the-camera-moves)).

`ShellMap_SelectGadget` sets `+0x4c2` ([picking a gadget](#picking-a-gadget)), and so does `ShellMap_ResetViewAndClear` (`00009c64`), which `Shell_DrawScreen` (`0003c1b7`) runs when it draws the war map's window ([drawing a screen](menus.md#drawing-a-screen)). It is ES2's: it sets the camera's position and altitude from the full view, leaving the pan and the gadgets' rects as they were ([Open](#open)), and runs `ShellMap_Clear` between `Map_PushContext` and `Map_PopContext`.

**The clock** is `ShellMap_Ticks` (`0007e204`), to which `ShellMap_TimerTick` (`000092ae`) adds 1. `ShellMap_LoadResources` (`000093b3`) registers it with `Timer_AddCallback` at a count of 2, every second interrupt of the shell's timer: 60 ticks a second ([the devices](startup.md#the-devices)).

### The intro

`ShellMap_IntroStep` (`00009cfb`), (map), is ES2's `ShellMap_IntroStep` ([the intro](../../retail/shell/mission-map.md#the-intro)) stepped once a tick, with ES1's fields: the state `+0x33e`, the deadline `+0x340` on `ShellMap_Ticks`, which every test reads afresh, the line `+0x344` of the objective lines `+0x148` (count `+0x198`), the state a wait goes on to `+0x49e`, the slow-paint byte `+0x346` and the revealed squad, path and marker counts `+0x347`, `+0x348` and `+0x349`. A camera move runs from `ShellMap_IntroFrom` (`0009a4ec`) to `ShellMap_IntroTo` (`0009a4fc`), each three `int32` (x, y, altitude): each step the camera is the start plus `ShellMap_IntroDelta` (`0009a4dc`), (to − from) / the move's ticks truncated once, times the ticks gone; at the deadline it is the end. The moving and waiting states paint at each step. The ticks are `SHELLMAP.BND`'s ([`bnd-notes.md`](../formats/bnd-notes.md#bndmapbnd-and-bndshellmapbnd)):

| State | Does |
|---|---|
| 0 | the first step, the deadline being 0: sets the display's (`000ab616`) draw page `+0x38` to its visible page `+0x30`, the camera to the full view and paints with `+0x346` set, [the slow paint](#a-paint); puts the draw page back, clears `+0x346` and paints again; then the deadline is 60 ticks on (`ShellMapBnd_FullViewHold`, `0009a4aa`). Later steps paint until the deadline, then run state 1 |
| 1, 2 | the deadline moves 120 ticks on (`ShellMapBnd_ZoomTicks`, `0009a4ae`) and the camera moves to the squad view; then state 4 |
| 3, 4 | one more squad member (`+0x347`) every 30 ticks (`ShellMapBnd_MemberTicks`, `0009a4b2`) until the squad size `+0xc6`; then the line 0, the waypoint number `ShellMap_IntroWaypoint` (`0009a510`) 1 and the ref pointer `ShellMap_IntroWaypointRef` (`0009a508`) on the nav path's first ref (null with no path), and 30 ticks (`ShellMapBnd_SectionPause`, `0009a4be`) before state 5 |
| 5 | the line at or past the count: state 6 at once. No path, or the waypoint number below the path's waypoint count: 30 ticks (`ShellMapBnd_StepPause`, `0009a4c2`), then state 6. Otherwise the line goes back one and state 0x10 runs with the byte `ShellMap_IntroLineWait` (`0009a512`) set |
| 6, 7 | the path and marker counts, the waypoint number and the ref pointer go up by one; with no path or the number past the waypoint count, state 0x11. Otherwise the camera moves to the ref's point, keeping its altitude, over (*d* / 50000 + 1) × 30 ticks (`ShellMapBnd_LegDistanceUnit`, `0009a4b6`; `ShellMapBnd_LegTicksPerUnit`, `0009a4ba`), *d* being `Math_FastMagnitude3D` of the camera less the point; the ticks are kept in `ShellMap_IntroLegTicks` (`0009a50c`) |
| 8 | at the point, with the line below the count: 3 ticks a character of the line (`ShellMapBnd_TicksPerChar`, `0009a4c6`) less the leg's ticks, when that is above 0 (state 0xd, then 9); otherwise state 9 at once |
| 9 | the line goes up. Below the count: 30 ticks (state 0xe), then 10. Just at the count, with a path and the waypoint number below its waypoint count: the line goes back one, and state 5. Otherwise 30 ticks (`ShellMapBnd_SectionPause`, state 0xf), then 10 |
| 10, 0xb | with the line below the count, state 0xc and then 0xb: the line goes up and 30 ticks (state 0xe) lead to state 5. Past the last line, state 5 |
| 0xc, 0xd | with the line below the count, 3 ticks a character of it; at the deadline, the state `+0x49e` |
| 0xe, 0xf | the deadline, then the state `+0x49e` |
| 0x10 | the lines left when the path has ended: past the last line, state 5. With `0009a512` set and the line not empty, 30 ticks (state 0xe) and back here with the byte clear. Otherwise the line goes up past every empty line, and state 0xc then 0x10 with the byte set |
| 0x11, 0x12 | clears `+0x346`, reveals the whole squad, path and markers (path count less 1), and moves the camera back to the full view over 120 ticks; then state 0x13 |
| 0x13 | a paint every step. The first two lower `+0x4c1` from 2 to 0, and the second sets `+0x4c0`: the intro is over |

So each stop on the path shows two lines, the first for at least 3 ticks a character counting the leg that led there, with 30 ticks between lines and before each leg; lines left after the last stop come one at a time with 30 ticks between. ES2's line waits are 0 and nothing draws its lines; ES2's state 0x10 does not skip empty lines, its state 0xb skips the wait before the last line, and its state 0 sets the deadline before its paints. `Space` runs `ShellMap_SkipIntro`, which moves a state below `0x12` to `0x11` ([destroying it](#destroying-it)).

### A paint

`ShellMap_Paint` (`00009ca7`), slot `+4` of `ShellMapVtable`, is ES2's ([a paint](../../retail/shell/mission-map.md#a-paint)) without the clamp. `Map_PushContext` (`00008f65`) keeps the installed render context (`000ab612`) at `+4` and installs the viewport; `ShellMap_InstallCamera` (`00008fa2`) puts the camera at the position plus the pan (`+0x36`, `+0x3a`), at the altitude, with `Raster_InstallViewProjection`; the steps below run; `Map_PopContext` (`00008f86`) installs the kept context again. The clear and the grid set the viewport's `+0x20c` and draw in its centred coordinates, from view points (`Raster_ModelToView` and `Raster_PerspectiveDivide`, y negated); the other steps clear `+0x20c` and draw at the screen points of `Map_Project` (`00008ee6`), (map, out, world), ES2's projection ([the camera](../../retail/shell/mission-map.md#the-camera)): `Raster_SetModelTransform`(0), `Raster_ModelToView`, `Raster_PerspectiveDivide` and `Raster_ProjectToScreen`. Colours are palette indices.

| Step | Function | Draws |
|---|---|---|
| 1 | `ShellMap_Clear` (`0000aa3c`) | the viewport's rect, (`+0x220`, `+0x224`) to that plus the width `+0x42` and height `+0x46`, in `0x36` |
| 2 | `ShellMap_PaintShapes` (`0000aff0`) | [the zone's shapes](#the-shapes) |
| 3 | `ShellMap_PaintGrid` (`0000ab58`) | the grid in `0x37`; outside the slow paint, the bounds' outline in `0x35` |
| 4 | `ShellMap_PaintPath` (`0000b4ec`) | the nav path in `0x38` |
| 5 | `ShellMap_PaintBases` (`0000b840`) | the bases ([the icons](#the-icons)) |
| 6 | `ShellMap_PaintNavMarkers` (`0000bb1c`) | the nav markers |
| 7 | `ShellMap_PaintSquad` (`0000bc47`) | the squad |
| 8 | `ShellMap_CurrentLine` (`0000bd83`) | [the objective lines](#the-lines) |
| 9 | `ShellMap_PaintCallout` (`0000bf1b`) | the callout ([picking a gadget](#picking-a-gadget)) |

ES2's order is the same, with the relief where ES1 draws the shapes. **The slow paint**, state 0's first, draws into the visible page and waits after the clear 10 ticks (`ShellMapBnd_ClearWait`, `0009a49e`), after the shapes 10 (`ShellMapBnd_ShapesWait`, `0009a4a2`) and after each grid line 1 (`ShellMapBnd_GridLineWait`, `0009a4a6`, through `ShellMap_WaitUntil` (`0007e32c`)); each wait spins until `ShellMap_Ticks` passes its value at the start plus the wait.

**The grid.** The spacing *s*, kept at `+0x22`, is the absolute x distance between the view points of the world origin and of (`ShellMapBnd_GridSpacing` (`0009a450`), the same, 0), 200000; ES2's measures sixteen spacings and divides. With *o* the origin's view point and *L*, *T* the viewport's left and top in its coordinates (the context's `+0x220`, `+0x224`), vertical lines run from x = *o*.x − ((*o*.x − *L*) / *s* + 1) × *s* in steps of *s* while x is below −*L*, each from (x, −*T*) to (x, *T*), under the pen {0, `0x37`}; then horizontal lines from y = *o*.y − ((*o*.y − *T*) / *s* + 1) × *s* while y is below −*T*, each from (−*L*, y) to (*L*, y). The outline is `Raster_FillRect` under the brush {4, `0x35`} on the view points of the bounds' low corner (`+0x4a4`, `+0x4a8`) and high corner (`+0x4b0`, `+0x4b4`).

#### The shapes

`ShellMap_PaintShapes` (`0000aff0`), with `ShellMap_DrawShapes` set as in retail, first makes the colour ramp `ShellMapBnd_ShapeRamp` (`0009a47c`), 14, a ramp of one entry, `0x38` (`0007e330`), with `Raster_SetColourRamps`. Then for each of the `+0xc8` zone objects at `+0xd0` it calls the object's slot `+4`, `TSObj_Render` (`0000e7c9`); when the object's shape instance (`+0x2c`) has a shape whose sequence 0 has frames ([`TSShape`](../../retail/formats/dts-shape.md#part-chunks)), the instance's sequence-0 frame is 1 for the call and 0 after. With the flag clear, which retail's `SHELLMAP.BND` never gives, each object is instead `dba\mapshp`'s (`000aa2bc`) first bitmap through `Bitmap_BlitRotatedUnpacked`, turned by the object's yaw `+8` and sized from its shape's `+8` × `0x5a82` `>> 14` projected.

**The shapes go through the simulator's shape renderer, the same code at the shell's addresses, but for the objects' layout, five functions of the shell's own and one table.** A zone object is the shell's `TS_OBJ`, whose fields sit 2 bytes lower than the simulator's: the orientation block at `+4` and the shape instance at `+0x2c`, where `ES1DBSIM`'s are at `+6` and `+0x2e` ([the zone's shapes](../formats/zone-terrain.md#the-shells-load)). Its tables, `MatObjVtable` (`0007e512`), `TSObjVtable` (`0007e536`) and `AnimObjVtable` (`0007e55c`), hold four slots to the simulator's five. `TSObj_Render` (`0000e7c9`) installs the object's rotation with `SimObject_InstallModelTransform` (`0000e116`), `Raster_SetModelTransform` of `Context_GetTransform` (`0000ddc8`)(object + 4), and calls the instance's slot `+0x20`; from there the parts and polys draw as the simulator's do ([the zone shapes](../rendering/polygon-shading.md#the-zone-shapes)), except through these:

| What | The shell's | How it differs from `ES1DBSIM`'s |
|---|---|---|
| the rotation | `BuildEulerRotationMatrixQ14` (`000589c4`), which `Context_GetTransform` runs | each of its 15 table indexes is the angle `>> 4` truncated, where the simulator's rounds it (`SAR 4` then `ADC 0`) |
| the cosine table | `Math_CosTableQ14` (`0008b648`), 1025 `int16`, which the rotation and `Math_BuildRotation2D` (`0005533c`) read | entry *i* is floor(16384 · cos(*i* / 4096 turn)), where the simulator's (`ES1DBSIM 000981cc`) is rounded to the nearest; 554 entries are one less |
| the bounds test | `TSBounds_ScreenVisibility` (`00059444`) | the near test passes when the centre's depth plus the radius equals the near plane, where the simulator's needs it above; the projected radius is ((radius `<<` focal shift) + depth / 2) / depth, without the simulator's field-of-view scale; the screen box is the centre ± that radius, without the simulator's 3-pixel margin |
| a detail part | `TSDetailPart_Render` (`00059aa8`) | the level counts from 0, with no bias: the first whose threshold is at least the projected size, else the last; the size, scaled by `TSDetailPart_SizeScaleQ10` (`00088686`, `0x400`), is capped at the radius scaled by `TSDetailPart_SizeCapQ10` (`00088688`) when that word is non-zero, and it is 0 in the image ([Open](#open)) |
| a bitmap part | `TSBitmapPart_Render` (`00059590`) | no 200-pixel limit: the scaled bitmap is drawn at any projected size |
| a four-corner texture poly | `TSTexture4Poly_Render` (`0007c4d0`) | its two texture point arrays are on the stack and built at each call, where the simulator's are static; it draws the same |

The simulator's routines the path reaches include the solid, shaded and Gouraud polys, brush mode 1's contour fill `Raster_FillContourPolygon` (`0006a05c`) and `Raster_FillContourTriangle` (`000696cc`), the type-1 bitmap draw `Bitmap_BlitTransparent` (`0006b664`), `Bitmap_UnpackToScratch` (`00050544`), the clipping helpers `Rect_Intersect` (`0006659c`), `SpanList_FromRect` (`000665fc`) and `SpanList_ClipToRect` (`0004fd65`), and `VGA_8`'s slots 0 to 6 and 24 to 26 (its table `0008b2f0`): `VGA8_FillSpans`, `VGA8_RemapSpans`, `VGA8_FillRect`, `VGA8_RemapRect`, `VGA8_PutPixel`, `VGA8_RemapPixel`, `VGA8_SpanFill`, `VGA8_SpanFillRegion` and `VGA8_BlitBitmap`. The 2D routines on the path that differ from the simulator's are in their pages: `Raster_BlitDispatch` ([the display drivers](../rendering/polygon-shading.md#the-display-drivers)), `Raster_FillRect` and `VGA_8`'s `VGA8_BlitBitmapScaled`, `VGA8_WriteRow`, `VGA8_WriteRowTransparent` and `VGA8_SetDrawPage` ([the simulator](../rendering/screen-pages.md#the-simulator)), and `VGA8_FillBitmapPolygon` ([bitmap fills](../rendering/polygon-shading.md#bitmap-fills-brush-modes-5-and-7)).

#### The path

With a nav path and its revealed count `+0x348` not 0, under the pen {0, `0x38`}: while the count is short of the path's waypoint count, a line from the point of the last revealed ref to the camera's position (`+0xe`, `+0x12`, 0) and a filled circle (`Raster_DrawEllipse` under the brush {0, `0x38`}) on the box `ShellMapBnd_PenRadius` (`0009a48a`), 3, pixels each way around that position: the pen the intro draws with. Once all are revealed, lines through the refs' points in order.

#### The lines

`ShellMap_CurrentLine` (`0000bd83`) is ES2's ([a paint](../../retail/shell/mission-map.md#a-paint)), drawing the lines ES2's only picks. By the state, through the case table `ShellMap_CurrentLineCases` (`0007e334`):

- **7 and `0xd`**, the line below the count: the top label `+0x362` (centred) gets the line when it is not empty, and keeps it at `+0x4bc`; an empty line shows the kept one, or nothing with none kept.
- **`0xe`**: the top label is cleared when the line is below the count and not empty; otherwise it shows the kept line, or nothing.
- **`0x13`**: each objective line that is not empty goes, in order, into the next of the twenty labels `+0x366`, one a row from the top; the loop has no bound at 20.

`Label_SetText` draws as it sets ([labels](../simulation/cockpit-widgets.md#labels)), so a paint redraws the text. In other states the paint draws no line.

### The icons

`ShellMap_PaintIcon` (`0000b9cb`), (map, bitmap, colour, below, size, world size, point), ES2's rule ([bases](../../retail/shell/mission-map.md#bases)): with *p* the point's `Map_Project` and *z* = (world size `<<` the focal shift `+0xc`) / the altitude, a *z* below *below* plots one pixel of colour at *p*; a *z* below the bitmap's height `+4` draws it scaled to *z* by *z* (`Bitmap_BlitScaled`) at *p* less *z*/2; otherwise it draws it as it is at *p* less *size*/2. The frames are `dba\icons`' (`000aa2b8`, [`ShellMap_LoadResources`](startup.md#the-start-up)):

| Icon | Shown when | Frame | Colour | World size | Size | Pixel below |
|---|---|---|---|---|---|---|
| a base of type 5, 6 or 8 (LISTENING POST, RADAR TOWER, GUN TOWER), side 1 | `+0x4c7` clear | 73 | `0x35` | 25000 | 7 | 0 |
| the same, any other side | the side not negative | 75 | `0x32` | 25000 | 7 | 0 |
| any other base, side 1 | `+0x4c7` clear | 72 | `0x35` | 25000 | 13 | 0 |
| any other base, any other side | the side not negative | 74 | `0x32` | 25000 | 13 | 0 |
| a squad slot whose member ref is 0 | among the first `+0x347` slots whose ref is not −1 | 18 | `0x32` | 100000 | 7 | 1 |
| any other squad slot | the same | 27 | `0x32` | 100000 | 7 | 1 |

`ShellMap_PaintBases` walks the `+0x62` bases at `+0x5e`: the side is `+8`, the type `+0`, the point `+2` into the points `+0x52`. `+0x4c7` is set for the mission kinds 0 to 2 ([what it reads](#what-it-reads)), so those missions do not show side-1 bases. `ShellMap_PaintSquad` walks the eight member refs `+0xa8` and draws at the slots' positions `+0xd4`. The figures are `SHELLMAP.BND`'s, the size the gadgets' ([`bnd-notes.md`](../formats/bnd-notes.md#bndmapbnd-and-bndshellmapbnd)); with 0 as the bases' pixel threshold, a base is never one pixel.

**The nav markers** are frames 0 to 8 (`ShellMapBnd_FirstMarkerFrame` (`0009a458`) to `ShellMapBnd_LastMarkerFrame` (`0009a45a`)), one for each path point after the first, as many as `+0x349`, at most nine and at most the waypoint count less 1, each drawn as it is at its point less half of 9 (`ShellMapBnd_MarkerSize`, `0009a474`).

### The camera moves

**The two zoom buttons and the four arrows beside the map move its camera once the introduction is over; the keys `=` and `-` zoom as the buttons do.** Each `ShellMapVtable` move does nothing while `+0x4c0` is clear; otherwise it runs the base class's move and then places the gadgets again with `ShellMap_PlaceGadgets` and the callout with `ShellMap_PlaceCallout` ([picking a gadget](#picking-a-gadget)). The war map's buttons call the slots ([the map's buttons](war-map.md#the-maps-buttons)), and `Shell_HandleInput` the two zooms ([the input](startup.md#the-input)); each then sets `000809a8` to 3 ([the tick](#the-tick)).

| Control | Slot | Function | Effect |
|---|---|---|---|
| 108, `=` | `+8` | `ShellMap_ZoomIn` (`0000d43c`), `Map_ZoomIn` | the altitude down `MapBnd_ZoomStep` (`0009a430`), 50000, when it stays at or above `MapBnd_AltitudeLow`, 50000 |
| 109, `-` | `+0xc` | `ShellMap_ZoomOut` (`0000d469`), `Map_ZoomOut` | the altitude up 50000 when it stays at or below `MapBnd_AltitudeHigh`, 1,000,000; then an altitude above the full view's `+0x352` becomes it |
| 1503 (key `0x48`) | `+0x10` | `ShellMap_PanNorth` (`0000d4b8`), `Map_PanNorth` | the pan y `+0x3a` up the step `+0x3e` when the position y `+0x12` plus the step is at most `MapBnd_PanLimit` (`0009a42c`), 5,000,000 |
| 1504 (key `0x50`) | `+0x14` | `ShellMap_PanSouth` (`0000d4e5`), `Map_PanSouth` | the pan y down the step when the position y less the step is at least −5,000,000 |
| 1505 (key `0x4b`) | `+0x18` | `ShellMap_PanWest` (`0000d512`), `Map_PanWest` | the pan x `+0x36` down the step, on the position x `+0xe` |
| 1506 | `+0x1c` | `ShellMap_PanEast` (`0000d53f`), `Map_PanEast` | the pan x up the step, on the position x |
| | `+0x20` | `ShellMap_ResetPan` (`0000d56c`), `Map_ResetPan` (`00009106`) | the pan zeroed ([Open](#open)) |

Every zoom, and every capped altitude, derives the pan step again with `Map_PanStepFor`; the intro's moves do not, so until the first zoom a pan moves 20000, the step for the constructor's 200000. The base class's moves are ES2's `Map_*` ones ([the six buttons](../../retail/shell/mission-map.md#the-six-buttons)), where ES2's map overrides them with moves that clamp. The pan limits test the camera's position without the pan, and after the intro the position stays the full view's centre, so a pan grows without limit while that centre is within 5,000,000 less the step of 0 on its axis.

### Picking a gadget

**Each icon of a squad member or a base has an invisible square over it; clicking one, or stepping through them with `.` and `,`, picks it, and the map then shows its name in a box with a pointer to it.**

`MapGadget_Place` (`0000dbfb`), (gadget), sets the gadget's rect `+0`–`+0xc` from `Map_Project` of its world position `+0x24` with the map `+0x20`'s camera: the point less half the size `+0x30` to that plus the size, less the installed render context's rect left and top (`+0x210`, `+0x214`). The map's viewport is installed whenever it runs, so the rect is relative to the viewport, which `ShellMap_PlaceCallout` adds back for the callout. It runs at the build and after every camera move, through `ShellMap_PlaceGadgets` (`0000da37`) with the camera and pan installed; the intro's camera does not move the gadgets.

The gadget draws nothing. Its table `MapGadgetVtable` (`0007e3f8`) holds the button base's slots ([the gadget bases](../simulation/cockpit-widgets.md#the-gadget-bases)), most of them this file's stubs, each a frame around `MOV EAX,[EBP+8]` and a `RET` of its arguments; `CTLButtonControlVtableShellMapCopy` (`0007e3c8`) holds the file's copies of all seven:

| Slot | `MapGadgetVtable` | `CTLButtonControlVtableShellMapCopy` |
|---|---|---|
| `+4` | `MapGadget_PaintNoOp` (`0000dbc9`) | `CTLButtonControl_PaintNoOpShellMapCopy` (`0000db73`) |
| `+8` | `CTLControl_InvalidateNoOpShellMapCopy` (`0000db7d`) | the same |
| `+0xc` | `MapGadget_OnClick` (`0000dbd3`) | `CTLControl_OnClickNoOpShellMapCopy` (`0000db87`) |
| `+0x10` | `MapGadget_FlushDeferredPaintNoOp` (`0000dbf1`) | `CTLControl_FlushDeferredPaintNoOpShellMapCopy` (`0000db94`) |
| `+0x14` | `Widget_GetButtonValueShellMapCopy` (`0000db9e`): `Widget_ClickButtons` (`0007e66c`) in `AX` | the same |
| `+0x18` | `CTLControl_SetValueNoOpShellMapCopy` (`0000dbae`) | the same |
| `+0x1c` | `CTLControl_DragMoveNoOpShellMapCopy` (`0000dbbc`) | the same |

**A click** on a gadget reaches its slot `+0xc` through the window `+0x134`'s clickables. `MapGadget_OnClick`, (gadget, value), the value not read, runs `ShellMap_SelectGadget` (`0000d3d2`)(the map `+0x20`, gadget), which finds the gadget's index among the `+0x47e` at `+0x3b6`, makes it the selected gadget `+0x480`, runs `ShellMap_PlaceCallout` and sets the redraw byte `+0x4c2` ([the tick](#the-tick)).

**`.` and `,`** run `ShellMap_SelectNextGadget` (`0000d599`) and `ShellMap_SelectPrevGadget` (`0000d657`) ([the input](startup.md#the-input)), during the intro as well. The first steps `+0x480` forward, wrapping from the count `+0x47e` to 0; the second steps it back, −1 becoming 0 and below 0 the count less 1. Each stops at the first gadget whose rect lies strictly inside the viewport's rect `+0x210`–`+0x21c` and runs `ShellMap_PlaceCallout`, or returns on coming back to the index it started from. That test sets the gadget's viewport-relative rect against the viewport's screen rect, so a gadget passes when its square is more than the rect's left (`+0x210`) right of the viewport's left edge and more than its top (`+0x214`) below its top edge, and up to those amounts past its right and bottom edges; `HercWorks.Query menu 1500` gives the area 1502's record the rect (14, 11)–(282, 142). Starting from −1, before anything is picked, the index never comes back to its start, so with no gadget passing the loop does not end ([Open](#open)).

**The callout** is drawn by `ShellMap_PaintCallout` (`0000bf1b`) in state `0x13` with a gadget selected: `Label_SetText` puts the gadget's text `+0x32` in the callout label `+0x482`, which fills its box in `0x3f` first ([building it](#building-it), step 4); `Raster_FillRect` under the brush {4, `0x3b`} frames the label's rect, and `Raster_DrawPolygonDispatch`(3, the pointer `+0x486`) under the brush {0, `0x3b`} fills the pointer. [The callout](#the-callout) gives the box's and the pointer's layout.

## Destroying it

`Shell_DeleteMissionMap` (`0002a4ec`) stops the mouse queue, runs `ShellMap_SkipIntro` (`0000a9c8`), ES2's: a state `+0x33e` below `0x12` becomes `0x11`; then `ShellMap_Dtor` (`00009a73`)(map, 3). The destructor stores `ShellMapVtable`, unregisters and destroys the sun through its slot `+4` with 3, frees the 21 labels `+0x362`/`+0x366`, the points (`__vec_delete`), the headings, each waypoint group's refs and the groups, the mechs, the bases, the nav path, each zone object through its slot `+0xc` with 3 and their array `+0xd0`, the callout label, the `TEX1` bitmaps `+0x4c3` (`GLBitmapArray_Dtor` with 3) and the gadgets `+0x3b6` (`Mem_Delete` each, no destructor), deletes the window `+0x134` with `WidgetRoot_Delete` (`0001074d`), frees both string tables with `OwnedArray_Dtor` (`00010231`), runs `Map_Dtor` and frees the object for flag bit 0. The `TSSunLight` and `TSLight` destructors, `TSSunLight_Dtor` (`00009a0d`) and `TSLight_Dtor` (`000099cd`), destroy the two contexts (`TSContext_Dtor` `+0x1e` with 2, `+0` with 0); they are VSHELL's `00427e3d` and `00427e02` without the destructor count.

`ShellMap_RevealAll` (`0000a9ea`), (map), clears `+0x346`, sets `+0x347` to the squad size `+0xc6`, and with a nav path `+0x348` to its waypoint count and `+0x349` to one less, then the state `+0x33e` to `0x13`, ES2's last intro state ([the intro](../../retail/shell/mission-map.md#the-intro)). `es2_xref.py` finds no reference to it ([Open](#open)).

## Open

- **Open:** `es2_xref.py` finds no reference to `TSDetailPart_SetSizeScale` (`00059a88`) or `TSDetailPart_SetSizeCap` (`00059a98`), the writers of the detail parts' scale and cap; a table filled at run time has not been looked for.
- **Open:** after `ShellMap_ResetViewAndClear` the camera has the full view's altitude, with the pan and the pan step as they were, and the gadgets keep the rects the last camera move gave them. A retail check: after the intro, zoom in twice with 108 and pan once, open ARMING and come back, then click a squad member's icon: whether the callout comes up beside it.
- **Open:** whether `.` or `,` with nothing yet picked and no gadget inside the viewport hangs the shell, as the reading of `ShellMap_SelectNextGadget` says. A retail check: after the intro, without clicking an icon, zoom in fully with 108 and pan with an arrow until no icon is in the map, then press `.`.
- **Open:** side-1 bases get gadgets in the mission kinds 0 to 2, which hide their icons. A retail check: in a RECONNAISSANCE mission, press `.` repeatedly: whether the callout ever points at a spot with no icon.
- **Open:** what calls slot `+0x20` of `ShellMapVtable`, `ShellMap_ResetPan`: `es2_xref.py` finds only the table's pointer, and the war map's buttons and keys reach slots `+8` to `+0x1c`.
- **Open:** `es2_xref.py` finds no reference to `ShellMap_RevealAll` (`0000a9ea`); a table filled at run time has not been looked for.
