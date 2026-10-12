# DBSIM.EXE — `wld\world<N>.wld`, theater parameters

ES1's world file sets up a mission's surroundings: the sky backdrop and its bitmap bank, the clouds, the scattered points, the distance colour bands, the ground texture's fade and the ground-shape set. It is ES2's world file ([ES2's `.WLD`](../../retail/formats/wld-world.md)) less two of its leading `int16`, and ES1 uses three of the strings and three of the `int16` that ES2 reads and ignores. The simulator reads it; a search of `GO.EXE` for the strings `wld` and `world` finds neither, where the same search of `DBSIM.EXE` finds both. Addresses are `ES1DBSIM` image addresses ([the X-32 executable](x32-executable.md#the-image)); ES2's are DBSIM's.

## Which file

`DBSim_LoadScriptDat` (`0001d850`) calls `World_LoadTheater` (`000251c4`) with `data\script.dat`'s 20-byte header at `000bc49c`, after `Terrain_LoadZoneShapes` ([`script-dat.md`](script-dat.md#reading)). With *t* header word 0, the theater, *v* word 9 and *d* word 8:

- the palette is `dpl\world<M>`, M = 6*t* + 2*v*;
- the world file and the palette-cycle file are `wld\world<N>` and `cyc\world<N>`, N = M + *d*.

ES2's are both `world<N>`, N = 2 × word 0 + word 9 ([ES2's load](../../retail/formats/wld-world.md#the-load)). Words 8 and 9 come from the mission's header patches in mode 1 and from `000aacc2` and `000aacc4` otherwise ([header patches](msn-mission-file.md#header-patches)).

`SIMVOL0.VOL` holds `WORLD0.WLD` to `WORLD23.WLD`, six per theater in the order of `Terrain_TheaterNames` (`000924a0`): `desert`, `temper`, `ice`, `urban` ([which theater](zone-terrain.md#which-zone-and-which-theater)). Its `dpl\` holds `WORLD<M>.DPL` for every even M to 22, and `WORLD1.DPL` and `WORLD3.DPL`.

The two files of a pair differ in a pattern. With *d* 0 the file names a sky bank, its ground texture reads `dat\tex`, its distance offsets are −20000 and −50000, and its tag-0 distance thresholds end at 161500 to 364000. With *d* 1 it names none, reads `dat\tex_lo` (`tex_lo2` in `WORLD9`), its two distance offsets are equal, −20000 or −26000, but in `WORLD9` (−20000, −30000), and its thresholds end at 90000 to 168000.

## The layout

Little-endian, read field by field. ES2's layout ([layout](../../retail/formats/wld-world.md#the-worldn-descriptor--layout)) with twelve leading `int16` where ES2's has fourteen:

| | |
|---|---|
| 12 × `int16` | [the header shorts](#the-header-shorts) |
| `int32` count + count × `int32` | the distance thresholds for an object with a type tag, as ES2's |
| `int32` count + count × `int32` | the same for tag 0, as ES2's |
| `int16` rows, `int16` cols | as ES2's |
| cols × 4 bytes, `int16`, cols × 4 bytes | the two tables of index ranges and the unused `int16` between them, as ES2's |
| 4 bytes, 4 bytes | the 1-wide ramp's two ranges, as ES2's |
| `int16` | the number of clouds; ES2 reads it and does not use it |
| `int16` | the number of scattered points; ES2 reads it and does not use it |
| `int32`, `int32` | the distance offsets, as ES2's |
| 5 NUL-terminated strings | sky bank, cloud bank, impact palette, ground texture bank, ground texture fade file |

All 24 retail files walk to their last byte by this layout, 281 to 287 bytes. Every one holds 16 thresholds in each band table, 16 rows and 8 columns (ES2's files 11), 8 in the `int16` between the range tables, the ranges (86, 16) and (102, 1) for the 1-wide ramp, 20 clouds and 250 points. The strings:

| | Retail values |
|---|---|
| sky bank | `world<N>` where *d* is 0, empty where it is 1 |
| clouds | `clouds2` |
| impact palette | `impact1` |
| ground texture | `trenches` for `desert`, `temper` and `ice`, `cracks` for `urban` |
| fade file | `tex` where *d* is 0, `tex_lo` where it is 1, `tex_lo2` in `WORLD9` |

`SIMVOL0` holds `dba\WORLD<M>.DBA` for every even M to 22, `dba\CLOUDS2.DBA`, `dpl\IMPACT1.DPL`, `dba\TRENCHES.64K` and `dba\CRACKS.64K` (65,536 bytes each), and `dat\TEX.DAT`, `dat\TEX_LO.DAT` and `dat\TEX_LO2.DAT`, each a count of 15 and 15 ascending `int32`. It holds no `cyc\` entry, nor does `ZONES.VOL`.

### The header shorts

| Short | Byte | Field | Retail values |
|---|---|---|---|
| 0–7 | 0 | ES2's [`hzline` shorts](../../retail/formats/wld-world.md#the-hzline-shorts), into the same fields | short 0 is 2 where *d* is 0, 6 to 14 where it is 1; zenith 214 or 240; band height 1 to 3; band count 4 to 13; ground colour 224 to 247; ground band height 1 to 3; ground band count 6 to 12; line offset 0, 1 in `WORLD21` |
| 8 | 16 | the [scattered points'](#the-scattered-points) colour | 233; 251 in `WORLD3`, `WORLD8` and `WORLD9`; 228 in `WORLD12` |
| 9 | 18 | `World_FlatSetSelector` (`000928e2`), ES2's short 9 | 0; 1 in `WORLD16`, `WORLD18`, `WORLD19` and `WORLD20`; −20 in `WORLD21` |
| 10, 11 | 20 | stored at `000bc5d0` and `000bc5d2`, where ES2 stores its shorts 10 and 11 ([Open](#open)) | short 10 is 47, 242 in `WORLD12` and `WORLD13`; short 11 is 47, 60, 219, 221, 222 or 223 |

ES2's shorts 12 and 13, which ES2 reads and does not use, have no place in ES1's file.

## The load

`World_LoadTheater` (`000251c4`, callee-cleaned) follows [ES2's load](../../retail/formats/wld-world.md#the-load) with these differences:

1. It copies the header to `000bc5a4` and loads `dpl\world<M>` with `Palette_LoadAndActivate` (`00027e5c`), which also calls `00028b28` after making the palette the active one at `00092c20` ([Open](#open)).
2. It opens `wld\world<N>` and builds the `hzline` with `Hzline_Ctor` (`00026180`) into `g_TheaterHzline` (`000928d4`): `0x71` bytes where ES2's are `0x7a`, the same defaults, and the byte at `+0x70` set to 1 where ES2 sets `+0x70` to 0 and `+0x71`, the one-colour flag, to 1. It stores shorts 0–7 as ES2's does, through `Hzline_SetColors` (`00026234`), and short 4 + short 6 − 1 at `000928e0`.
3. It keeps short 8 for the points, stores short 9 at `World_FlatSetSelector` (`000928e2`) and shorts 10 and 11 at `000bc5d0` and `000bc5d2`, and sets `000bc5ce` to `0x40` and `000bc5d4` to `0x62`, as ES2 sets `004cfd74` and `004cfd7a`.
4. The two band tables go to `WorldShades_BandsTagged` (`000afdc4`) and `WorldShades_BandsTag0` (`000afdc8`), each count's low 16 bits to `000bc5b8` and `000bc5ba`; rows and columns to `000bc5be` and `000bc5bc`; the expanded ranges to `WorldShades_LevelRanges` (`000afdbc`) and `WorldShades_BlendRanges` (`000afdc0`), through `Palette_InterpolateIndexRanges` (`000289e0`). Each range table is read into a 64-byte stack buffer, 16 columns, and the column count is not checked against it.
5. The distance offsets go to `WorldShades_DistanceOffsets` (`000bc5c6`).
6. It reads each string a byte at a time, without a bound, into a 12-byte stack buffer, the first four into one and the fifth into another; the longest retail string is 8 characters. The first, when not empty, goes to `Hzline_LoadBitmapBank` (`00026dc8`), which loads `dba\<name>` into the `hzline`'s `+0x50`, where ES2's copy of the function, which `es2_xref.py` finds no caller of, would put it ([ES2's horizon strip](../../retail/rendering/distance-fog-and-sky.md#open)). The second, when not empty, [builds the clouds](#the-clouds). The third, when not empty, loads `dpl\<name>` with `Palette_LoadDpl` into `ImpactPaletteObject` (`000928f0`), as ES2's does. The fourth and fifth go to [the ground texture](#the-ground-texture).
7. When the number of points is not 0 it [builds the points](#the-scattered-points).
8. It closes the file and reads `cyc\world<N>` as ES2's load does, into `000928e4`, the count at `000928e8`, through `PaletteCycle_Register` (`000281cc`), whose five slots' due times are the dword at `000981c4` plus the record's `int16` at `+4`, where ES2's take `Time_GetCoarseTicks`. Its records step 12 bytes through 6 × count bytes, as ES2's do.
9. It loads `dat\shades` with `World_LoadShades` (`00025bc8`), ES2's.
10. It calls `GndTex_Load` (`00028de8`) and then `FlatObj_LoadResources` (`00006cf8`) with `World_FlatSetSelector`.

It reads no `rmp\world<N>`, and calls neither `Terrain_BindTextureBank` nor `Raster_SetShadeRampDimensions`. A string or count that is empty or 0 leaves the pointer it would set as it was.

### The clouds

When the second string is not empty, the load allocates `0x113` bytes, constructs them with `maybe_Clouds_Ctor` (`000271c0`) with the cloud count and the name, and stores the vptr `000928f6` at `+0` and the object at `World_Clouds` (`000928d8`). The 12-byte member at `+0x107` has no constructor code. `maybe_Clouds_Ctor`, given the object, the count *c* and the name, in stack order:

- stores in turn the vptrs `00092ade`, `00092b02`, `00092b2e`, `00092b5a`, `00092b86` and `00092bb2`, constructs five 12-byte elements at `+0x21` with `00027498`, and stores the vptr `00092bde`;
- allocates *c* × 4 elements of 12 bytes (`00027498`) at `+0x0e`, *c* elements of 6 bytes (`00027510`) at `+0x12` and *c* zeroed bytes at `+0x16`, and stores *c* at `+0x1f` and `+0xf9` as `int16`;
- loads `dba\<name>` with `ClassItem_LoadFromFile` to `+0x1a`;
- zeroes the three dwords at `+0x5d`, the five elements at `+0x21`, the dwords at `+0x6d`, `+0x71` and `+0x75`, and the byte at `+0x1e`.

What draws the clouds is [Open](#open).

### The scattered points

When the number of points is not 0, `PointScatter_Ctor` (`00026dec`), given a null object and the count, allocates `0x17` bytes, an array of count 12-byte points at `+0` through `__vec_new` (each built by `PointScatter_PointCtor`, `00026e5c`, which stores nothing), the count at `+5` as `int16`, `0xff` at `+0x15` as `int16` and 0 in the byte at `+4`. The load stores it at `World_PointScatter` (`000928dc`) and short 8 at its `+0x15`.

`PointScatter_Scatter` (`00026ee0`), given the object and a centre of three `int32`, stores the centre at `+9`, `+0xd` and `+0x11`, and places each point at

```
a = Math_RandomNext(000b5c48)            angle, 0x10000 to the turn
r = Math_RandomNext(000b5c48)
x = centre.x + Q14(2r · cos a)
y = centre.y + Q14(2r · cos(a − 0x4000))
z = 0
```

with `Math_CosBam` and `Math_Q14Multiply`. `PointScatter_Update` (`00026e8c`), given the object and the centre, scatters the points the first time (setting `+4`), and when `000bd567` is less than `000956be` stores count × `000bd567` / `000956be` at `+7` and calls `00026fe0` ([Open](#open)).

### The ground texture

`GndTex_Load` (`00028de8`, `gndtex.cpp`), given the fifth string, the fourth, and shorts 0, 5, 6 and 4, in stack order:

1. sets `00092c4c` to 1, builds `dba\<fourth>.64k`, calls `00029714` with `0x140` and `0x17c`, and passes the name to `00029830` ([Open](#open));
2. opens `dat\<fifth>`, asserting at severity 5 (`gndtex.cpp` line `0x3f`) when it cannot, and reads an `int32` count to `000b0d74` and that many `int32` to `000b0d78`: in retail, 15 ascending distances, from 20000 to 112400 in `TEX.DAT`;
3. stores short 5 at `000b0d7c`, half of short 0 at `000b0d84`, 15 at `000b0d80` and `0x140` at `00092c80`;
4. builds 15 tables of 16 bytes at `000b0d70` (allocating count × 4 bytes there first, then replacing them with the 15 pointers): with *g* short 4, *b* = *g* & `0xf0` and *top* = *g* + short 6 − 1, entry *j* of table *i* is

```
s = top − (b + j);   if s < 0: s = s − 1
entry = b + j + (s · i) / 14              integer division, truncating
```

so table 0 is the palette row of the ground colour unchanged and each later table moves every entry of that row further toward *top*.

ES2 has no `gndtex.cpp`: its ground is textured from the bank its fourth string names through `Terrain_BindTextureBank` ([terrain texturing](../../retail/rendering/terrain-texturing.md#the-answer-end-to-end)).

### The ground-shape set

`FlatObj_LoadResources` (`00006cf8`), ES2's ([the shape set](../../retail/simulation/ground-shapes.md#the-shape-set--flatobj_loadresources-004097a8)), loads `dts\flat` for a selector of 0 and `dts\flat2` otherwise with `Shape_LoadAllRoots` (skip 0, tag `0x140008`), the roots to `000bc1d8` and their number to `000bc1dc`, and builds a `Pool_Init` pool of 150 elements of `0x3b` bytes at `000af044`, where ES2's elements are `0x41` bytes. It loads no `dba\` bank, binds none to the shapes and builds no delete queue.

## Rejected readings

| Reading | Why it is wrong |
|---|---|
| ES1's `.WLD` is ES2's layout, since its tables and strings follow ES2's in the same order | ES1's loader reads twelve leading `int16`, not fourteen. ES2's layout takes ES1's first band count as its shorts 12 and 13 and the first threshold as the count |
| The first string, `world<N>`, is a name for the world, as ES2's `world24` is read and not used | ES1 loads it as the `hzline`'s bitmap bank, `dba\WORLD<N>.DBA` |

## Open

- **Open:** what ES1's `hzline` draws with the fields and the bank the load gives it, whether it draws the horizon strip ES2's `Hzline_BlitHorizonStrip` (`0042f398`) would, and what `000928e0` (short 4 + short 6 − 1) is read for.
- **Open:** what `000bc5d0` and `000bc5d2` (shorts 10 and 11), `000bc5ce` and `000bc5d4` are read for, in ES1 and in ES2.
- **Open:** what `00028b28`, which `Palette_LoadAndActivate` calls after loading the palette, does, and what the dword at `000981c4` that `PaletteCycle_Register` adds a cycle's delay to counts.
- **Open:** the clouds' class: its vtables, what the five elements at `+0x21`, the arrays at `+0x0e`, `+0x12` and `+0x16` and the member at `+0x107` hold, and what draws it. `00025a08` stores the vptr `000928f6` and five others in turn and calls `TSBase_Dtor`; whether it is the clouds' destructor is not read.
- **Open:** what calls `PointScatter_Update` and with which centre, what `000bd567` and `000956be` are, what `00026fe0` draws, and so what the points look like.
- **Open:** what `00029714` and `00029830` do with `dba\<name>.64k`, and what reads `000b0d70`–`000b0d84`, `00092c4c` and `00092c80`: how the ground texture and its 15 fade tables are drawn.
- **Open:** what reads `WORLD1.DPL` and `WORLD3.DPL`, which no M names.
