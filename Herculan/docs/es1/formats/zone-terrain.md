# DBSIM.EXE and GO.EXE — `dat\zoneNNNN`, a zone's shapes

ES1's zone file lists the shapes that stand on a mission's ground: for each, which of the theater's shapes it is, where it stands and which way it faces. It shares only its name with ES2's zone file, which is the 16-byte header of the zone's heightmap ([ES2's zone files](../../retail/formats/zone-terrain.md)). ES1's ground is flat and has no heightmap: its relief is these shapes, ridges and spires among them, obstacles and cover that cannot be crossed. The simulator reads it when it loads the world, and the shell reads it for its map screen. Addresses are `ES1DBSIM` image addresses unless `ES1GO` is named ([the X-32 executable](x32-executable.md#the-image)).

## Which zone and which theater

The zone and the theater are header words 1 and 0 of `data\script.dat` ([`script-dat.md`](script-dat.md)), the [header patch](msn-mission-file.md#header-patches) words a mission's load leaves there. Word 1 comes from the mission's own patches; the load overrides word 0 by its mode, from a table or by a draw below 4.

- **The simulator.** `DBSim_LoadScriptDat` (`0001d850`), having read `data\script.dat`'s header into `000bc49c`, calls `Terrain_LoadZoneShapes` (`00020238`) with word 0 as the theater and word 1 as the zone, after `Mechs_InitAllTypes` and `FlyerTypes_LoadAll`.
- **The shell.** The map screen's load, `ShellMap_LoadScriptDat` (`ES1GO 0000c068`), reads `data\script.dat`'s 20-byte header itself and calls `ShellMap_LoadZoneShapes` (`ES1GO 0000c7e3`) with the same two words.

Each loader copies `zoneXXXX` into a buffer, calls `ltoa` with the zone number, the address of the `XXXX` and radix 10, and opens `dat\zone<number>` through `ResourcePath_BuildFolderName` and `VolRStream_CtorFromPath`; the retail files are named that way, `ZONE1110.DAT` for zone 1110. A negative zone loads nothing.

The theater indexes a table of four names, which names the shape library `dgs\<name>` and, in the simulator, the [shape table](#dattheater-the-shapes-dividing-lines) `dat\<name>`. The two programs' tables are in different orders ([Open](#open)):

| Index | `Terrain_TheaterNames` (`000924a0`) | `ShellMap_TheaterNames` (`ES1GO 0007e228`) |
|---|---|---|
| 0 | `desert` | `temper` |
| 1 | `temper` | `ice` |
| 2 | `ice` | `desert` |
| 3 | `urban` | `urban` |

## The layout

Little-endian throughout: two `int16` and then *n* records of 20 bytes, 4 + 20 × *n* bytes in all. Neither loader checks the file's size.

| Offset | Type | Field |
|---|---|---|
| `0x00` | `int16` | read by both loaders into a local that neither uses again ([Open](#open)) |
| `0x02` | `int16` | *n*, the number of records |
| `0x04` | 20 × *n* | the records |

A record:

| Offset | Type | Field |
|---|---|---|
| `0x00` | `int16` | the shape: an index into the theater's shape library, not checked against its count |
| `0x02` | 3 × `int32` | the position, X, Y and Z |
| `0x0e` | 3 × `int16` | the orientation: the object's three Euler angles |

The angles go to the object's orientation block, whose rotation `Context_GetTransform` (`000016e8`) builds from them with `BuildEulerRotationMatrixQ14`, ES2's builder ([fixed-point math](../simulation/fixed-point-math.md#helpers-that-match)), so they are ES2's object angles, the third the heading, at 0x10000 to the turn ([ES2's object frame](../../retail/simulation/sim-object-layout.md#the-objects-frame-is-a-transform-and-its-position-is-that-transforms-translation)).

### The retail files

`SIMVOL0.VOL` holds 15 and `ZONES.VOL` 91, all 4 + 20 × *n* bytes; 5,129 records in all, from 1 to 184 a file. In every record Z is 0, the first two angles are 0, and the heading is a multiple of `0x800`, a 32nd of a turn. Shape indices run 0 to 12. The word at `0x00` is 0 to 3: 0 in 14 of `SIMVOL0`'s files and 1 in `ZONE106.DAT`; 0, 1, 2 and 3 in 21, 33, 19 and 18 of `ZONES`'.

The simulator mounts both archives (`SIMVOL0`'s program mask is 1, `ZONES`' `0x101`), the shell only `ZONES`. Eight names are in both: `ZONE0`, `ZONE1`, `ZONE2`, `ZONE3`, `ZONE106` and `ZONE1570` with the same bytes, `ZONE6` and `ZONE8` with different ones ([Open](#open)). Seven are only in `SIMVOL0`: `ZONE7`, `ZONE10`, `ZONE25`, `ZONE26`, `ZONE30`, `ZONE31` and `ZONE32`.

## `dat\<theater>`, the shapes' dividing lines

**A theater's shape table gives some of its shapes a line on the ground that decides whether an object near the shape is drawn in front of it or behind it.** The simulator reads it with the zone ([step 5](#the-simulators-load)); the shell reads none of the four files.

The file is records of 24 bytes and nothing else, record *i* for root shape *i* of `dgs\<name>`:

| Offset | Type | Field |
|---|---|---|
| `0x00` | 3 × `int32` | the line's first end, X, Y and Z, in the shape's frame |
| `0x0c` | 3 × `int32` | its second end |

A record whose first Z is negative gives its shape no line: the shape's object keeps a null pointer and is sorted as any other object.

`SIMVOL0.VOL`'s `DESERT.DAT`, `TEMPER.DAT`, `ICE.DAT` and `URBAN.DAT` are 312 bytes each, 13 records for the 13 shapes of each library. Every record without a line is (0, 0, −1, 0, 0, −1). The others have both Z at 0:

| Theater | Shape | First end (X, Y) | Second end (X, Y) |
|---|---|---|---|
| `desert` | 0 | (250, −17065) | (−7750, 17060) |
| `desert` | 1 | (−3750, 6900) | (5850, −6450) |
| `desert` | 2 | (3500, 22000) | (2000, −22000) |
| `desert` | 9 | (9800, 4600) | (−6600, −8200) |
| `temper` | 0 | (348, 16704) | (0, −17000) |
| `temper` | 2 | (−200, −17064) | (−3000, 17064) |
| `temper` | 9 | (9800, 4600) | (−6600, −8200) |
| `ice` | 0 | (−1200, 11400) | (2400, −11400) |
| `ice` | 1 | (−7100, 8300) | (5700, −9500) |
| `ice` | 2 | (3500, 22000) | (2000, −22000) |
| `ice` | 4 | (−6200, 9100) | (200, −9100) |
| `ice` | 9 | (−2000, 4500) | (8700, −4800) |
| `urban` | 2 | (−5000, −17000) | (441, 16540) |

Each frame the draw puts a zone shape's line through the shape's transform into its draw entry, and the sort tests which side of it the camera and the other object stand on ([the frame's draw](../rendering/polygon-shading.md#the-frames-draw)).

## The simulator's load

`Terrain_LoadZoneShapes` (`00020238`, `terrain.cpp`), with the theater and the zone in stack order:

1. For a negative zone it sets the count `Zone_ShapeCount` (`000bc588`) to 0 and the pointers `Zone_TheaterShapeRoots` (`000bc580`) and `Zone_ShapeObjects` (`000bc584`) to null, and returns.
2. It opens the zone file. When it cannot, it asserts at severity 5 (`terrain.cpp` line `0x32`).
3. It reads the word at `0x00`, and the count into `Zone_ShapeCount`.
4. It loads the theater's shape library: `Shape_LoadAllRoots` (`000635a8`) with skip 0, tag `0x2bc0001` and `dgs\<name>` puts every root shape of `DESERT.DGS`, `TEMPER.DGS`, `ICE.DGS` or `URBAN.DGS` in an array at `Zone_TheaterShapeRoots` and returns their number, *k* ([loading a file](../../retail/formats/dts-shape.md#loading-a-file)).
5. It reads *k* × 24 bytes of `dat\<name>` into an array of *k* 24-byte records, `Zone_TheaterShapeRecords` (`000bc58a`), built by `__vec_new` with `TheaterShapeRecord_ElementCtor` (`000204d8`), whose code writes nothing to the record, and closes that file. Record *i* is root shape *i*'s [dividing line](#dattheater-the-shapes-dividing-lines); the file's length is not checked against *k*.
6. When the count is not 0, it allocates an array of that many object pointers, `Zone_ShapeObjects`, and for each record reads its 2, 12 and 6 bytes and builds an object of `0x3f` bytes:
   - `TSObj_Ctor` (`00001b04`) with the record's root shape, then the vptr `000924be` at `+2`. A failed allocation skips both and the stores below write through the null pointer;
   - the angles into the orientation block's angles (`+6`) and the position into its translation (`+0x20`), and 0 into its rotation-built word (`+0x2c`), so the rotation is built from the angles when first asked for;
   - at `+0x3b`, past `TS_OBJ`'s `0x3b` bytes, a pointer to the shape's dividing line in `Zone_TheaterShapeRecords`, or null when that record's `int32` at `+8` is negative.
7. It closes the zone file.

With a count of 0, `Zone_ShapeObjects` keeps whatever it held. The loader frees nothing a previous load built.

### The zone shape object

The object is a `TS_OBJ` with the record pointer added, and its type tag, the `int16` at `+0`, is 0: nothing in the load writes it and the heap zero-fills ([the zone's shapes](../rendering/polygon-shading.md#the-zone-shapes)). Its vtable `000924be` is the one `TS_OBJ`'s module copy `000924fa` has but for the destructor, and the slots are `TS_OBJ`'s own code, copied into `terrain.cpp` ([vtables](compiler.md#vtables)):

| Slot | Function | Does |
|---|---|---|
| `+4` | `TSObj_RenderCopy` (`00020550`) | installs the object's model transform and calls its shape instance's (`+0x2e`) slot `+0x20`, the render |
| `+8` | `TSObj_GetPositionCopy` (`00020570`) | returns the object's `+0x20`, the position |
| `+0xc` | `ZoneShapeObj_Dtor` (`00020584`) | with the object non-null: stores the vptrs `000924be` and `000924fa`, calls the shape instance's slot `+0x1c` with 3 when it has one, stores `0009251a` and `0009253a`, and frees the object when bit 0 of its second argument is set |
| `+0x10` | `TSObj_ReturnZeroCopy` (`000205d3`) | returns 0: a `NOP`, then the code of `TS_OBJ`'s `TSObj_ReturnZero` (`000018e8`) |
| `+0x14` | `TSObj_GetRadiusCopy` (`000205dc`) | returns the `int16` at `+8` of the instance's shape (`+4`), 0 without a shape instance or shape |

`TSObj_RenderCopy`, `TSObj_GetPositionCopy` and `TSObj_GetRadiusCopy` are the same code as `TS_OBJ`'s `TSObj_Render` (`00001cdc`), `TSObj_GetPosition` (`00001cfc`) and `TSObj_GetRadius` (`00001d58`) in its vtable `000902ea`. `TSObj_DtorCopy` (`00020624`), `000924fa`'s destructor, is `ZoneShapeObj_Dtor` without the first store, and `MatObj_DtorCopy` (`00020680`), `0009251a`'s, stores `0009251a` and `0009253a` and frees. `0009251a` and `0009253a` are `terrain.cpp`'s copies of `MAT_OBJ`'s `000902ca` and `DRAWABLE`'s `0009020a`. Both hold `TSObj_ReturnZeroCopy` at `+0x10` and `Drawable_GetRadiusCopy` (`000206aa`), which returns 0, at `+0x14`; `0009253a` holds 0 at `+4` and `+8` and `Drawable_DtorCopy` (`000206b4`) at `+0xc`, which stores `0009253a` at `+2` and frees nothing, the code of `DRAWABLE`'s destructor `Drawable_Dtor` (`000018d4`, [the 3Space object classes](../simulation/object-classes.md#the-destructors)).

What the zone's shapes answer to a walking machine, a shot and a line of sight is in [`terrain.md`](../simulation/terrain.md#the-tests-against-the-shapes).

The record a zone shape points at is its shape's [dividing line](#dattheater-the-shapes-dividing-lines). `ZoneShapeObj_TransformSegment` (`000206c8`), given the object and a 24-byte buffer, puts the line's two ends through the object's transform (`Context_GetTransform`, `Transform_ApplyToPoint`) into the buffer and returns it, or returns 0 when the record pointer is null; `ZoneShapeObj_WorldSegment` (`00020738`) does it into the static buffer `000afa28`.

## The shell's load

`ShellMap_LoadZoneShapes` (`ES1GO 0000c7e3`, `src\shellmap.cpp`), with the map screen's object, the theater and the zone in stack order, reads the file as the simulator does and builds the objects differently:

- A negative zone sets the map's count (`+0xc8`) to 0 and its root array (`+0xca`) and object array (`+0xd0`) to null.
- A zone file that does not open asserts at severity 5 (line `0x565`). The count goes to `+0xc8`. The two loads below come only after the file opens.
- `ShellMap_LoadShapeRoots` (`ES1GO 000092b9`) loads `dgs\<name>`'s root shapes into an array at `+0xca` and their number to `+0xce`. It is `Shape_LoadAllRoots` with no skip, its arena based at the pointer in `ES1GO 0007e20c` and limited by `ShellMapBnd_ShapeArenaSize` (`ES1GO 0009a4d6`), 75000 ([`SHELLMAP.BND`](bnd-notes.md#bndmapbnd-and-bndshellmapbnd)), a chunk with another tag asserting at line `0x61`. The shell's archive is `SHELL0.VOL`, whose four `.DGS` are of other sizes than `SIMVOL0`'s.
- `ShellMap_LoadResource` (`ES1GO 0000923c`) loads `dba\tex1`, `SHELL0`'s `TEX1.DBA`, to `+0x4c3`.
- It reads no `dat\<name>`.
- For each record it reads the shape index (2 bytes), the position (12) and the angles (6), stores the `TEX1` bitmap array in the root shape's `+0x26`, where a `TSShape` keeps its bound bitmap array ([`TSShape`](../../retail/formats/dts-shape.md#part-chunks)), then builds the object with the shell's `TSObj_Ctor` (`ES1GO 0000e518`) and the root shape. The shell's `TS_OBJ` has its vptr at `+0` and its orientation block at `+4`, two bytes lower than the simulator's, so the angles go to `+4`–`+9`, copied as a dword and a word, the position to `+0x1e` and the 0 to `+0x2a`. Neither the shape index nor the object is checked: the index against the root count, the object against null. The object has no record pointer.

The shell's `TSObj_Ctor`, (object, shape), allocates `0x32` bytes when given no object, installs `DRAWABLE`'s vtable `ES1GO 0007e428` at `+0` and zeroes the orientation block, then installs `MatObjVtable` (`ES1GO 0007e512`) and `TSObjVtable` (`ES1GO 0007e536`) at `+0` in turn. It stores `TSShapeInstance_Ctor`(0, shape) at `+0x2c`, or 0 for a null shape, and clears the byte at `+0x30`. The simulator's builds a `0x3b`-byte object with the vptr at `+2`, the shape instance at `+0x2e` and 8 bytes cleared at `+0x32` ([the 3Space object classes](../simulation/object-classes.md)).

## Rejected readings

| Reading | Why it is wrong |
|---|---|
| A `dat\<theater>` record's two points are the opposite corners of the shape's bounding box | The draw's sort builds a plane through the line between them and asks which side of it the camera and another object stand on ([the frame's draw](../rendering/polygon-shading.md#the-frames-draw)); in every retail record both points lie at Z 0, where a box would span the shape's height |

## Open

- **Open:** what the word at `0x00` is. It holds 0 to 3, as a theater index does, and neither loader reads it after storing it; whether it names the zone's theater, and in which program's order.
- **Open:** whether the shell and the simulator load different theaters for a retail mission. With header word 0 at 0, the simulator loads `dgs\desert` and the shell `dgs\temper`; which of the two the zones' shapes are authored for is not read.
- **Open:** which archive's `ZONE6.DAT` and `ZONE8.DAT` the simulator reads. ES1's volume code has not been read against ES2's precedence rules ([ES2's archives](../../retail/formats/vol-archive.md#which-archives-are-mounted)).
