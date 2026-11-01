# GO.EXE — the war map's map

**The war map's top area shows the mission's ground from above: the zone's shapes drawn in 3D, a label beside each squad member and each base, the mission's objective lines, and a box with a pointer beside the label the player has picked.** It is one C++ object, `Shell_MissionMap` (`000809a4`), the descendant of ES2's briefing map ([ES2's mission map](../../retail/shell/mission-map.md)): the same `Map` base with its camera and pan step, the same three camera positions and intro states, on ES1's layout and with ES1's contents. This page covers the object's build and its end; its running (the tick, the draw, the keys and the camera moves) is [Open](#open). The screen around it is [the war map](war-map.md).

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

`MapVtable` holds 0 at `+0` and `+4` and six camera moves at `+8`–`+0x1c` (`00009043`, `00009010`, `000090be`, `000090df`, `00009076`, `0000909d`); `ShellMapVtable` (`0007e268`) holds the draw `00009ca7` at `+4` and seven moves at `+8`–`+0x20`, `0000d43c` to `0000d56c` ([Open](#open)).

## Building it

`Shell_PrepareMission` (`0002a523`) builds the map after loading a mission, with `ShellMap_Constructor` (`000094d9`)(0, the rect `000a02d4`). It allocates `0x4c8` bytes, runs `Map_Ctor`, constructs the member arrays, stores `ShellMapVtable`, and:

1. sets the intro bytes `+0x346`–`+0x349` to 1, 0, 0, 0 and copies `ShellMapBnd_DrawShapes` to `ShellMap_DrawShapes`;
2. loads `data\object.str` (line 6 of `cfg\filename.bin`, `Text_GetString`(`0009afb8`, 6), through `Path_ApplyAltDrive`) into the string table `+0x138` with `StringTable_LoadFromFile` (`0001026a`), and its group 0, the objective lines `Mission_WriteObjectives` wrote, into `+0x148` with `StringTable_LoadGroup` (`000103b8`), the count to `+0x198`. ES2's map reads `data\mission.str` there;
3. loads `str\maplabel` into the table `+0x19a`, group 0 into `+0x1aa` (count `+0x272`) and group 1 into `+0x274` (count `+0x33c`). Retail's `MAPLABEL.STR` holds two groups: YOU and SQUAD MEMBER, then the base types' names in type order, SUPPLY DEPOT, HERC FACTORY, MINING FACILITY, GENERATOR, HERC HANGAR, LISTENING POST, RADAR TOWER, REFINERY and GUN TOWER;
4. builds 22 labels with `Label_Ctor`(0, `ShellMap_LabelFont` (`000aa2c0`)), each with a zero margin, over rows of the rect's width `ShellMapBnd_LineSpacing` (`0009a480`, 10) pixels high: `+0x362` over the top row with flags 2 (centred), then twenty at `+0x366` with flags 1, the first over the top row as well and each next one a row lower; and the callout label `+0x482`, its fill colour `+0x1c` `0x3f`;
5. builds the sun: a `0x34`-byte light, the two `TSContext`s at `+0` and `+0x1e` of a `TSLight` (vtable `TSLightVtable` (`0007e2aa`) at `+0x12`), then `TSSunLightVtable` (`0007e2ca`) and `+0x16` 1; slot `+8`, `maybe_TSLight_SetIntensity`, sets 100; the words `+0xc`, `+0xe`, `+0x10` become 7000, 0, −7000; `Light_Register`. It is kept at `+0x4e`;
6. clears the array pointers `+0x52`, `+0x58`, `+0x64`, `+0x6e`, `+0x5e` and `+0xd0`, then runs [the load](#what-it-reads), `ShellMap_FitFullView` and `ShellMap_FitSquad` ([the views](#the-camera-views));
7. clears the intro state `+0x33e` and the dword `+0x340`; makes the gadget window `+0x134` with `CTLWindow_Ctor` (`0001144a`)(0, the rect) and makes it the active root with `Widget_SetActiveRoot` (`0001070d`), which stores it in `Widget_ActiveRoot` (`0007e5d8`), ES1DBSIM's `Widget_ClearPressed` without its pressed-index write; clears the gadget count `+0x47e` and the dword `+0x4bc`;
8. builds [the gadgets](#the-gadgets) with `ShellMap_BuildGadgets` (`0000d1bb`), sets the selected gadget `+0x480` to −1 and runs `ShellMap_PlaceCallout` (`0000d72b`), which returns at once for −1;
9. sets the bytes `+0x4c1` to 2 and `+0x4c0` to 0.

The mouse queue the gadgets are hit-tested through is started by `Shell_PrepareMission` itself, with `maybe_MouseEventQueue_Start` (`00010559`), before the constructor runs ([the war map](war-map.md#leaving-it)).

## What it reads

`ShellMap_LoadScriptDat` (`0000c068`), (map), ES2's `ShellMap_LoadScriptDat` (VSHELL `004243d7`, [what it reads](../../retail/shell/mission-map.md#what-it-reads)), opens `data\script.dat` (line 3 of `cfg\filename.bin`) with `FileRStream_Ctor`, asserting twice at severity 5 (lines `0x4c1` and `0x4c2`) when the stream's status is set, and reads the 20-byte header and the nine blocks ([ES1's `script.dat`](../formats/script-dat.md#writing)):

| Block | Kept |
|---|---|
| header | word 3, the mission's kind ([the list](single-mission.md#the-list)): `+0x4c7` is 1 for kind 0, 1 or 2, else 0 |
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

ES2's full view fits the mission's bounds ([the camera](../../retail/shell/mission-map.md#the-camera)); ES1's keeps the bounds at `+0x4a4` for other readers.

## The gadgets

**Each squad member and each base a group names gets a small clickable square with its name.** `ShellMap_BuildGadgets` (`0000d1bb`) puts the camera at the full view (`+0xe`, `+0x12`, `+0x16` from `+0x34a`–`+0x352`), runs `00008f65` and `00008fa2`, then builds one `MapGadget` with `MapGadget_Ctor` (`0000da8d`)(0, text, size, map, the world position):

- for each of the eight squad slots whose member ref `+0xa8` is not −1: text YOU for slot 0 and SQUAD MEMBER for the rest, size `ShellMapBnd_SquadGadgetSize` (`0009a470`, 7), the slot's position at `+0xd4`. A member at or above the squad size gets the position `ShellMap_PlaceSquad` left unwritten;
- for each base whose side `+8` is not −1: the type's name, group 1's line indexed by the type `+0`, unchecked against the group's count; size `ShellMapBnd_SmallBaseGadgetSize` (`0009a478`, 7) for types 5, 6 and 8 (LISTENING POST, RADAR TOWER, GUN TOWER) and `ShellMapBnd_BaseGadgetSize` (`0009a476`, 13) for the others; the base's point `+2`.

Each goes into the array `+0x3b6`, counted at `+0x47e`, which has room for 50, and into the window `+0x134`'s clickables with `Widget_RegisterClickable` (`0001084f`). Then `00008f86` runs.

`MapGadget_Ctor` allocates `0x38` bytes when given none (0 when that fails), runs `Widget_CtorRect` (`00011011`) on the rect `0009a524`, stores `CTLButtonControlVtableShellMapCopy` (`0007e3c8`) at `+0x1c` and clears the word `+0x19`, then stores `MapGadgetVtable` (`0007e3f8`) at `+0x1c`, the map at `+0x20`, the world position at `+0x24`–`+0x2c`, the size at `+0x30`, the text at `+0x32` and the width `+0x36`, the text's width by `HudFont_MeasureString` in `ShellMap_LabelFont` plus twice `ShellMapBnd_GadgetTextMargin` (`0009a4ca`, 3), and runs `0000dbfb`, which places it ([Open](#open)).

### The callout

`ShellMap_PlaceCallout` (`0000d72b`), (map), lays out the box for the selected gadget, index `+0x480`, and returns for −1. With *g* the gadget's rect's left and top (`+0`, `+4`), *s* its size and *w* its width, the box `ShellMapBnd_CalloutHeight` (`0009a4cc`, 10) high, the gap `ShellMapBnd_CalloutGap` (`0009a4ce`, 5) and the pointer's width *p* `ShellMapBnd_PointerWidth` (`0009a4d0`, 5):

- **a gadget right of the viewport's centre** (`(x1 − x0) / 2 + x0` of the viewport's rect `+0x210`–`+0x21c`, below *g*.x): the box spans x from *g*.x − gap − 1 − *w* to *g*.x − gap − 1, and the pointer, the three points at `+0x486`, is (*g*.x − gap + *p*, *h*/2), (*g*.x − gap, *h*/2 + *p*/2) and (*g*.x − gap, *h*/2 − *p*/2);
- **otherwise** the box spans x from *g*.x + *s* + gap to that plus *w*, and the pointer is (box left − *p* − 1, *h*/2), (box left − 1, *h*/2 − *p*/2) and (box left − 1, *h*/2 + *p*/2).

Each pointer y then gains *g*.y + *s*/2 − *h*/2; the box's y runs from *g*.y + *s*/2 − *h*/2 to that plus *h*; and the box and the three points all gain the viewport rect's left and top. The callout label `+0x482` takes the box with flags 2, centred, and a zero margin.

## Destroying it

`Shell_DeleteMissionMap` (`0002a4ec`) stops the mouse queue, runs `ShellMap_SkipIntro` (`0000a9c8`), ES2's: a state `+0x33e` below `0x12` becomes `0x11`; then `ShellMap_Dtor` (`00009a73`)(map, 3). The destructor stores `ShellMapVtable`, unregisters and destroys the sun through its slot `+4` with 3, frees the 21 labels `+0x362`/`+0x366`, the points (`__vec_delete`), the headings, each waypoint group's refs and the groups, the mechs, the bases, the nav path, each zone object through its slot `+0xc` with 3 and their array `+0xd0`, the callout label, the `TEX1` bitmaps `+0x4c3` (`GLBitmapArray_Dtor` with 3) and the gadgets `+0x3b6` (`Mem_Delete` each, no destructor), deletes the window `+0x134` with `WidgetRoot_Delete` (`0001074d`), frees both string tables with `OwnedArray_Dtor` (`00010231`), runs `Map_Dtor` and frees the object for flag bit 0. The `TSSunLight` and `TSLight` destructors, `TSSunLight_Dtor` (`00009a0d`) and `TSLight_Dtor` (`000099cd`), destroy the two contexts (`TSContext_Dtor` `+0x1e` with 2, `+0` with 0); they are VSHELL's `00427e3d` and `00427e02` without the destructor count.

`ShellMap_RevealAll` (`0000a9ea`), (map), clears `+0x346`, sets `+0x347` to the squad size `+0xc6`, and with a nav path `+0x348` to its waypoint count and `+0x349` to one less, then the state `+0x33e` to `0x13`, ES2's last intro state ([the intro](../../retail/shell/mission-map.md#the-intro)). `es2_xref.py` finds no reference to it ([Open](#open)).

## Open

- **Open:** the running, RE 130b: the tick `00009cfb`, the keys, the camera moves `00009010`–`00009106` and `0000d43c`–`0000d56c`, the draw `00009ca7` and its steps `0000aa3c`–`0000bf1b`, the projection `00008ee6` and `00008f65`, `00008f86`, `00008fa2`, the gadgets' placement `0000dbfb` and slots `0000db73`–`0000dbf1`, and the readers of `+0x4c7`, `+0x4c0`, `+0x4c1` and the bounds `+0x4a4`.
- **Open:** whether a gadget's rect is relative to the viewport, as the offset the callout adds implies; `0000dbfb` sets it.
- **Open:** `es2_xref.py` finds no reference to `ShellMap_RevealAll` (`0000a9ea`); a table filled at run time has not been looked for.
