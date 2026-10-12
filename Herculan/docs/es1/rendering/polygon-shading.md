# DBSIM.EXE — shape polygon colour and distance fade

**ES1 colours a shape's faces without ES2's lighting table (`.RMP`).** A flat face draws its colour as it is; a shaded face picks an entry along a colour ramp by how much light falls on it; a Gouraud face fills bands between the ramp entries its corners pick; a textured face maps a whole bitmap onto its four corners and darkens it through one of eight colour tables. Nothing fades with depth. What changes a shape's colours with distance is the ramps themselves, which the theater's distance bands re-point before each object is drawn, and an object beyond the last band is not drawn at all. Addresses are `ES1DBSIM` image addresses ([`../formats/x32-executable.md`](../formats/x32-executable.md#the-image)); ES2's are DBSIM's.

The poly types and their surface pairs are ES2's ([poly types](../../retail/rendering/dts-texture-binding.md#poly-types-and-their-colour-mechanisms-dbsimexe)): each renderer runs `TSPoly_FrontBackVisibilityTest` and takes the front pair (fill, line) or the back pair, the three flat types draw nothing when both chosen entries carry `0x14` in their top byte, and the textured type tests for −1. Each surface entry is the file's `{int16 value, int16 flag}` read as one `int32`, the flag in the high half. A face's light is `Light_ComputeShadeForFace` (`0005bc30`), 0 to 255, ES2's ([two shade calculations](../../retail/rendering/dts-texture-binding.md#two-shade-calculations--terrain-and-shapes-use-different-ones)), with the stored normal negated for a face seen from behind. Projection, near-plane clipping and the fill-then-outline pass are ES2's ([`view-transform-and-projection.md`](view-transform-and-projection.md)).

## The colour ramps

The fills look ramps up in the raster's ramp table, `g_RasterColourRamps` (`0009a6f5`): 8-byte records `{int16 *entries, int16 length}` indexed by ramp number, ES2's (`004a4fbd`). `Palette_UploadWhole` (`0006931c`), given a palette, sends its colours to the display through the routine at `0009df08` (first entry `+6`, count `+4`, colours `+8`), then copies its ramp records (first `+0xe`, count `+0xc`, records `+0x10`) into the table with `Raster_SetColourRamps` (`000691f8`). `Raster_GetColourRamps` (`00069227`) copies records out of the table the same way.

The theater palettes in `SIMVOL0.VOL`'s `dpl\` (every `WORLD<M>.DPL`, `DEATH1.DPL` and `DEATH2.DPL`) end, after their 256 colours, in 256 ramp records, each an `int16` length and that many `int16` palette indices, which take them to their last byte; `IMPACT1.DPL` and `COCKPIT.DPL` end in a count of 0. Ramps 14 to 255 have one entry each (255 in ramps 14 to 19 of `WORLD0` and `WORLD1`). Ramps 0 to 13 of `WORLD0.DPL` and `WORLD1.DPL`:

| Ramp | `WORLD0` | `WORLD1` |
|---|---|---|
| 0, 1 | 104–109, 104–111 | the same |
| 2, 3 | 112–127, 112–131 | the same |
| 4, 5 | 132–147, 132–151 | the same |
| 6, 7 | 152–167, 152–171 | the same |
| 8, 9 | 172–187, 172–191 | the same |
| 10, 11 | 192–195; 6, 93, 94, 95, 96 | 192–197, 192–199 |
| 12, 13 | 200–205; 199, 198, 197 | 200–205, 200–207 |

The other theater palettes have `WORLD0`'s ramp lengths, but `WORLD3`, `DEATH1` and `DEATH2`, which have `WORLD1`'s.

A fill with brush mode 0 resolves its colour by the top byte ([`../../retail/rendering/polygon-fill.md`](../../retail/rendering/polygon-fill.md#the-brush-colour)): 0, the colour itself; 4, entry (*s* · length) >> 8 of ramp *r*, *r* being the colour's low 16 bits and *s* bits 16–23; anything else draws nothing.

### The display drivers

The raster's routines that touch the display or a canvas (`0009df08`, the polygon fills' `0009df58`, the blits' `0009df3c` and `0009df44`, and the rest of the 36 dwords from `0009ded4`, all 0 in the image) are a copy of one driver's routine table. `Raster_InstallDriverTable` (`0007324e`), given a driver number, finds its table in the list at `0009df68`, `{number, table}` pairs ended by a negative number (`Raster_FindDriverTable`, `00073234`), and copies the `0009df64` dwords (36) of it to `0009ded4`; it returns 0 for a number the list lacks. The drivers, by the names `Raster_DriverByName` (`0006a714`) turns into numbers, 4 for a name it does not know:

| Name | Number | Table | Blit (`+0x68`, at `0009df3c`) | Clipped blit (`+0x70`, at `0009df44`) |
|---|---|---|---|---|
| `VGA_4` | 0 | `000a7ae0` | `00089964` | `0008a230` |
| `VGA_8` | 1 | `000a7b70` | `VGA8_BlitBitmap` (`0008b9a8`) | `0008c27c` |
| `BMP_8` | 3 | `000a7c00` | `BMP8_BlitBitmap` (`0008d700`) | `0008dc3c` |

## The four poly types

| Tag | Type | Vtable | Render (`+0x20`) | The surface value |
|---|---|---|---|---|
| `0x00140002` | `TSSolidPoly` | `0009b19a` | `TSSolidPoly_Render` (`0005403c`) | a brush colour |
| `0x00140003` | `TSShadedPoly` | `0009b1fe` | `TSShadedPoly_Render` (`000543e4`) | a brush colour, the face's light added |
| `0x00140009` | `TSGouraudPoly` | `0009b22a` | `TSGouraudPoly_Render` (`00054578`) | a ramp number |
| `0x0014000f` | `TSTexture4Poly` | `0009b1d2` | `TSTexture4Poly_Render` (`0005410c`) | a frame of the shape's bitmap bank |

The load functions in `g_TSObjectTypeRegistry` (`0009efa8`, entries `{tag, load function, name}`) store the vtables; the three `TSAlias*Poly` types have their own (`0009b266`, `0009b292`, `0009b2be`), whose render slots `00054c9c`, `00054d6c` and `00054f00` are not read here ([Open](#open)).

**`TSSolidPoly_Render`** puts the chosen fill and line entries in the brush (`000b5f04`) and the line colour (`PolyFill_LineColour`) as they are, with brush mode 0, and fills. A value with flag 0 is a palette index; one with flag `0x0400` is entry 0 of the ramp the value names. ES2's passes both through the `.RMP`'s unlit row first.

**`TSShadedPoly_Render`** does the same after ORing the face's light, & `0xff`, into bits 16–23 of both colours. With a flag of `0x0400` the fill resolves to entry (light · length) >> 8 of the ramp the value names, through the raster's table. ES2's looks the light up in the palette's own ramp (`Palette_ShadeRampLookup`) and passes the result through the `.RMP`'s unlit row; ES1's matches VSHELL's `TSShadedPoly_Render` (`00423024`), which ORs the light in the same way. What a flag of 0 draws is [Open](#open); the theater shapes have no such face ([below](#the-theater-shapes-faces)).

**`TSGouraudPoly_Render`** is ES2's ([`TSGouraudPoly`](../../retail/rendering/dts-texture-binding.md#tsgouraudpoly--same-ramp-number-per-vertex-light-no-rmp-row)): one light per corner, brush mode 1, the contour fill through the raster's ramp table.

**`TSTexture4Poly_Render`** maps a whole bitmap onto the poly's corners. The value, front or back by ES2's −1 rule, indexes the frames of the active bitmap bank (`000bdb5e`, its frame array at `+8`); a null frame draws nothing. The brush, installed at the render context's `+0x22c` for the fill and replaced by `PolyFill_FillBrush` after it, is `{7, 4, corners, the frame's bitmap header, a colour table}`. The corners' texture points are the frame's four corners, (0, 0), (columns − 1, 0), (columns − 1, rows − 1) and (0, rows − 1), in a 32-point buffer at `000b58d8` built on first use; a poly that crosses the near plane is clipped with `Poly_ClipRingToNearPlaneWithUV` into a second at `000b59dc`. The colour table is 0 while `000973f4` is 0; otherwise number (`000973f4` − 1) · light / 255 of the tables at `000973f0`, the light being the face's. There is no atlas and no frame descriptor: ES2's uses a frame index into packed atlas pages and the `.RMP` row for its light ([`TSTexture4Poly`](../../retail/rendering/dts-texture-binding.md#tstexture4poly--frame-index-ramp-row-by-light-fullbright-on-demand)). What brush mode 7 does with the table is [Open](#open).

### The texture colour tables

`Palette_LoadAndActivate` (`00027e5c`) calls `Palette_BuildTextureShadeTables` (`00028b28`) after it makes a palette the active one (`00092c20`). The first call builds eight 256-byte tables at `00092c40`, stores the array at `000973f0` and 8 at `000973f4` and `000973f8`; later calls find `00092c40` set and build nothing, so the tables follow the first palette loaded. With the palette's colours at `+8` (a dword each, its three low bytes the channels, unpacked by `Palette_UnpackColour`, `00028afc`) and its ramp records at `+0x10` (count `+0xc`), entry *c* of table *L* (0 to 7) is:

1. for each ramp whose entries hold *c*, the last such ramp deciding: *ref* = entry ((length − 1) · *L*) / 7 and *mid* = entry `Math_Q10Multiply(length − 1, 0x28a)`, about 0.63 of the way along; per channel, *ratio* = (*ref* << 10) / *mid*, 0 when *mid*'s channel is 0;
2. *c* itself when no ramp holds *c*;
3. otherwise the target `Math_Q10Multiply(c's channel, ratio)` per channel, and the palette index, 0 to 255, whose colour is nearest it by `Colour_Distance` (`000644c8`, `Math_FastMagnitude3D` of the difference), the first of equals and only below a distance of 1000.

So table *L* scales every ramp colour by how bright its ramp's step *L*/7 is against the step at 0.63, and re-quantises to the palette; a face's light picks table 0 in the dark up to table 7 at full light.

### The theater shapes' faces

The shapes that stand on a zone ([`../formats/zone-terrain.md`](../formats/zone-terrain.md)) have plain lit faces and flat faces, nothing else. The four libraries `DESERT.DGS`, `TEMPER.DGS`, `ICE.DGS` and `URBAN.DGS` in `SIMVOL0.VOL`, 13 shapes each, hold only `TSShadedPoly` and `TSSolidPoly` faces, besides 6 plain `TSPoly` chunks in `DESERT.DGS` and 7 in `URBAN.DGS`:

| | `DESERT` | `TEMPER` | `ICE` | `URBAN` |
|---|---|---|---|---|
| `TSShadedPoly` | 262 | 303 | 233 | 328 |
| `TSSolidPoly` | 111 | 136 | 108 | 101 |

- **Every shaded face's front fill and line are value 14 with flag `0x0400`, ramp 14.** Its back pair is 0 with flag `0x1400`, not drawn, but for three faces of `URBAN.DGS` whose back pair is ramp 14 too.
- **Every solid face has flag 0 on all four entries**, a palette index from 14 to 56, two or three distinct values a library, but for one face of `URBAN.DGS` whose back pair is 0 with flag `0x1400`.

So a theater shape's lit faces all shade along ramp 14, a single entry in every theater palette (255 in `WORLD0` and `WORLD1`), which the [distance bands](#the-zone-shapes) re-point for each shape before it is drawn; its flat faces draw their palette index unchanged at any distance.

## Which objects are drawn

**Each frame the simulator draws the zone's shapes and every live machine, effect and projectile from one list, farthest first, recolouring each by its distance and leaving out what lies past the theater's last distance band.** An object's type tag is its `int16` at `+0`, its vptr at `+2`; its vtable's `+4` slot draws it, `+8` returns its position (three `int32`), and `+0x14` returns the radius the bands read.

### The object list

`ObjList_Init` (`00020c2c`), whose address `es2_xref.py` finds only as the dword at `00021519`, allocates the list's arrays, 512 entries each: object pointers at `000bc58e` and `000bc592` (`0x800` bytes each), `0x36`-byte draw entries at `000bc598` built by `ObjListEntry_Ctor` (`00020c80`, which stores the vptr `0009255e` at `+0x14`), entry pointers at `000bc59c`, and through `ObjList_AllocSortNodes` (`00021560`) 512 sort nodes of `0x1d` bytes at `00092570`.

- **`ObjList_Add` (`00020d5c`)** stores an object at entry `00092558` of `000bc58e` and counts it, with no bound.
- **The mission start (`Sim_StartMission`, `0005a9dc`)**, after `DBSim_LoadScriptDat`, adds every zone shape object in `Zone_ShapeObjects`' order, stopping at the first null pointer ([the simulator's load](../formats/zone-terrain.md#the-simulators-load)), and once its other set-up is done stores the count at `000bc596`. The objects below that count, the zone's shapes, stay in the list for the mission.
- **`ObjList_RebuildFrame` (`00020de0`)**, which `Sim_RenderFrame` calls each frame, sets the count back to `000bc596` and adds, in turn, each object of the array at `000bc416` (count `000bc41c`) whose type record (`+0x3f`) has 0 at `+0x16`, then every element of the pools at `000af0b4`, `g_ExplosionPool`, `000aeef8` (150 of `0x4e` bytes) and `000aef68` (200 of `0x46` bytes), each walked back from its end with `Pool_Prev`.

`ObjList_Remove` (`00020d84`) swaps an object out for the list's last; `es2_xref.py` finds no reference to it.

### The frame's draw

`ObjList_DrawFrameObjects` (`00021008`), given a blend byte and the camera, draws a frame's objects. `Sim_RenderFrame` passes the byte at `000b5b65`, `000364f8` passes 0. It:

1. calls slot `+4` of every element of the ground-shape pool `000af044` ([the ground-shape set](../formats/wld-world.md#the-ground-shape-set)) within 30000 of the camera by `Math_DistanceBetweenPoints`, walking it back from the end with `Pool_Prev`;
2. stores the blend byte at `000afa98`;
3. for each of the `00092558` objects in the list, but the one the camera rides in (`Camera_IsMountedOn`, `000003ac`: the camera's `+0x36` is 2 and its `+0x32` is the object) and one whose position's z (slot `+8`, `+8`) is negative, fills the next `0x36`-byte entry: the position at `+0`, the radius (slot `+0x14`) as an `int16` at `+0xc`, the distance from the camera at `+0xe`, the object at `+0x32`, and 0 at `+0x12`. For an object below `000bc596`, a zone shape, whose box `ZoneShapeObj_WorldBox` (`00020738`) returns ([the zone's shapes](../formats/zone-terrain.md#the-simulators-load)), it stores 2 at `+0x12` and the box's 24 bytes at `+0x18`;
4. keeps the entry, in the list at `000bc59c`, only when `WorldShades_IsBeyondLastBand` (`00025f38`) is false for its distance;
5. stores the camera at `0009256c` (`ObjList_SetSortEye`, `00021554`) and calls `ObjList_SortAndDraw` (`00021d04`) with the count and the list.

`ObjList_SortAndDraw` does nothing for a count of 0. Otherwise it builds a binary tree in the sort nodes (`{left, right, entry, byte}`), first inserting the entries with 1 or 2 at `+0x12` in list order (`ObjList_TreeInsertBoxed`, `00021bec`), then the rest (`ObjList_TreeInsertRest`, `00021c60`), each through `ObjList_TreeInsert` (`00021ba8`) from the root, which goes right when `ObjList_EntryCompare` (`00021b20`) returns non-zero and left otherwise. It then draws the tree in order, left subtree, node, right subtree (`ObjList_TreeDraw`, `00021cd0`), calling slot `+4` of each entry's vptr `+0x14`, `ObjList_DrawEntryRender`. `ObjList_EntryCompare`, given the new entry and a node: between two entries of kind 0 it returns whether the new entry is nearer, so nearer entries go right and are drawn later; between two of kind 1 or 2 it calls `00021a4c`, and otherwise `000216c4` ([Open](#open)).

`ObjList_DrawEntryRender` (`00020f40`), whose address `00092562` holds, draws one entry: `00004f78` with `000bc19c` and the entry's position and `+0xc` value, `WorldShades_ApplyForObject` with the blend byte, the distance and the object, the object's slot `+4`, then `00005690` when the blend byte is set. ES2's (`ObjList_DrawEntryRender`, `0042876c`) also sets the depth fade from the entry's distance; ES1's sets none.

### The distance bands

The bands are ES2's ([distance colour bands](../../retail/rendering/distance-fog-and-sky.md#distance-colour-bands--worldshades_applyforobject-0042e8e8)), from the theater's world file ([the layout](../formats/wld-world.md#the-layout)) and `dat\shades` (`World_LoadShades`):

- **`WorldShades_DistanceBand` (`00025cb4`)** counts the thresholds the distance has reached, unsigned, at most one fewer than there are, from `WorldShades_BandsTagged` (`000afdc4`, count `000bc5b8`) for a non-zero tag and `WorldShades_BandsTag0` (`000afdc8`, count `000bc5ba`) for tag 0, a tag-5 object first adding `WorldShades_DistanceOffsets` (`000bc5c6`) entry 0, or entry 1 when its radius is 5000 or more, clamped at 0.
- **`WorldShades_ApplyForObject` (`00025d44`)**, given the blend byte, the distance (clamped at 0) and the object, re-points each `{ramp, column}` pair of the tag's `SHADES.DAT` list at range (column × rows + band) of `WorldShades_LevelRanges` (`000afdbc`), rows being `000bc5be`, with `Palette_SetRampsToRange` (`00028044`). With the blend byte set it passes `000280e4` a list of two ranges instead: the band's range less its last *q* + 1 indices, *q* being its length >> 2, then *q* + 1 indices from (start + length − *q*) of the band's `WorldShades_BlendRanges` (`000afdc0`) range, the two range pointers in a two-entry array. While `000928ea` is set it runs `WorldShades_ResetRamps` instead.
- **`WorldShades_ResetRamps` (`00025e88`)** points ramps 0, 2, 4, 6, 8, 10, 12 and 14 at the range {16, 16}; ES2's last is ramp 24. `000364f8` sets `000928ea` around its call of `ObjList_DrawFrameObjects` and clears it after.
- **`WorldShades_IsBeyondLastBand` (`00025f38`)**, given the distance and the object, takes the same table and offsets, a tag-8 object adding offset entry 0, and is true when the distance exceeds the table's last threshold, unsigned. It is the cull above.

`Palette_SetRampsToRange` (`00028044`), given the range, the count and the first ramp, in stack order, is ES2's: it writes the range's consecutive indices into each record of the array at `00092c34` from the first, and its length, then copies the records into the raster's table with `Raster_SetColourRamps`.

So the shaded, Gouraud and solid faces whose colours go through a ramp take the band's palette range for that ramp, and their colours change in steps as the object recedes, one step per threshold. Past the last threshold the object is not drawn: in retail the tag-0 table ends at 90000 to 364000 by theater ([the retail files](../formats/wld-world.md#the-layout)).

### The shades file

`World_LoadShades` (`00025bc8`), given the name `shades`, is ES2's, and so is the file's layout:

| | |
|---|---|
| `uint8` | *N*, the number of type tags the table has room for |
| `uint8` | *K*, the number of lists |
| *K* × | `uint8` tag, `uint8` count, count × `{uint8 ramp, uint8 column}` |

The load allocates *N* five-byte records `{uint8 count, pointer}` at `WorldShades_TypeTable` (`000bc5c0`), *N* at `000bc5c4`, and zeroes each count; each list then goes to the record its tag indexes, unchecked against *N*, its pairs to an array of their own. `WorldShades_ApplyForObject` indexes the table by the tag, unchecked too, and does nothing for a record whose count is 0.

`SIMVOL0.VOL`'s `SHADES.DAT`, 60 bytes, has *N* = 10 and six lists:

| Tag | `{ramp, column}` pairs |
|---|---|
| 0 | {14, 7} |
| 1, 5, 7 | {0, 0}, {2, 1}, {4, 2}, {6, 3}, {8, 4}, {12, 6} |
| 3 | {0, 0}, {12, 6} |
| 6 | {8, 4}, {12, 6} |

Tags 1, 3, 5, 6 and 7 hold ES2's lists ([ES2's `SHADES.DAT`](../../retail/rendering/distance-fog-and-sky.md#distance-colour-bands--worldshades_applyforobject-0042e8e8)); ES2's table has no row for tag 0. Tags 2, 4, 8 and 9 have none.

### The zone shapes

Every zone shape object carries tag 0: neither `TSObj_Ctor` nor `Terrain_LoadZoneShapes` writes `+0`, and `operator_new` takes the object from the heap, which zero-fills it ([the heap](../simulation/memory.md#the-heap)); `Sim_Main` (`00059350`) turns the heap on before anything else and off only on its way out. So for a zone shape, each frame:

1. the cull and the band use `WorldShades_BandsTag0` and its count, with no distance offset;
2. `WorldShades_ApplyForObject` re-points ramp 14, and only ramp 14, at band *b*'s range of column 7: entry 7 × 16 + *b* of `WorldShades_LevelRanges`, which runs over the 16 bands from the world file's first column-7 range to its second ([the retail ranges](../formats/wld-world.md#the-distance-colour-ranges));
3. its shaded faces, all on ramp 14 ([above](#the-theater-shapes-faces)), take entry (light · length) >> 8 of that range, and its solid faces their own palette indices.

In `WORLD0`, for one, a near shape's lit faces shade across palette entries 240 to 249, and one in the last band, short of the cull at 271500, is entry 253 whatever its light. Ramp 14 keeps the range of the last object that set it; the lists of tags 1 to 7 leave it alone.

In `000364f8`'s second view, which draws through `ObjList_DrawFrameObjects` with `000928ea` set, ramp 14 takes `WorldShades_ResetRamps`' range {16, 16} instead, and the cull still applies ([above](#the-distance-bands)). Two other views draw the zone's shapes straight through each object's slot `+4`, outside the object list, with no band and no cull:

- **`00036c2c`** fills its view with colour `0x17`, puts its camera 200000 above the position `00059280` returns, calls `WorldShades_ResetRamps`, draws every zone shape, then draws markers with `00069058`.
- **`00040d4c`**, a cockpit map, fills with colour `0x36`, draws grid lines in `0x37` every 50000 units, points ramp 14 at the one-entry range {56, 1} with `Palette_SetRampsToRange`, so every lit face is palette entry 56, and draws every zone shape with the word its shape instance's `+8` points at set to 1 for the draw and 0 after.

## Functions

| ES1 | Address | Notes |
|---|---|---|
| `Camera_IsMountedOn` | `000003ac` | |
| `ZoneShapeObj_WorldBox` | `00020738` | |
| `ObjList_Init`, `ObjListEntry_Ctor` | `00020c2c`, `00020c80` | `ObjList_Init` passes `__vec_new` the padding entry `00020c7f` before the constructor |
| `ObjList_Add`, `ObjList_Remove`, `ObjList_RebuildFrame` | `00020d5c`, `00020d84`, `00020de0` | |
| `ObjList_DrawEntryRender`, `ObjList_DrawFrameObjects` | `00020f40`, `00021008` | |
| `ObjList_SetSortEye`, `ObjList_AllocSortNodes` | `00021554`, `00021560` | |
| `ObjList_EntryCompare`, `ObjList_TreeInsert`, `ObjList_TreeInsertBoxed` | `00021b20`, `00021ba8`, `00021bec` | |
| `ObjList_TreeInsertRest`, `ObjList_TreeDraw`, `ObjList_SortAndDraw` | `00021c60`, `00021cd0`, `00021d04` | |
| `World_LoadShades` | `00025bc8` | |
| `WorldShades_DistanceBand`, `WorldShades_ApplyForObject` | `00025cb4`, `00025d44` | the object last, as with every callee-cleaned function ([calling conventions](../formats/compiler.md#calling-conventions)) |
| `WorldShades_ResetRamps`, `WorldShades_IsBeyondLastBand` | `00025e88`, `00025f38` | |
| `Palette_SetRampsToRange` | `00028044` | |
| `Palette_UnpackColour`, `Palette_BuildTextureShadeTables` | `00028afc`, `00028b28` | |
| `TSSolidPoly_Render`, `TSTexture4Poly_Render` | `0005403c`, `0005410c` | |
| `TSShadedPoly_Render`, `TSGouraudPoly_Render` | `000543e4`, `00054578` | |
| `Colour_Distance` | `000644c8` | |
| `Sim_Main`, `Sim_StartMission` | `00059350`, `0005a9dc` | |
| `Raster_SetColourRamps`, `Raster_GetColourRamps`, `Palette_UploadWhole` | `000691f8`, `00069227`, `0006931c` | |
| `Raster_DriverByName`, `Raster_BlitDispatch` | `0006a714`, `0006a8e0` | `Raster_BlitDispatch`: [the skyline's blit](ground-and-sky.md#the-horizon-skyline) |
| `Raster_FindDriverTable`, `Raster_InstallDriverTable` | `00073234`, `0007324e` | |
| `VGA8_BlitBitmap`, `BMP8_BlitBitmap` | `0008b9a8`, `0008d700` | |

## Open

- **Open:** what a `TSShadedPoly` with a flag of 0, whose fill reaches the span routine as a palette index with the light in bits 16–23, draws, and which flags the shapes outside the theater libraries (`dts\`, `dgs\` other than the four) carry on their shaded, solid and Gouraud faces.
- **Open:** what brush mode 7 does with the texture's colour table: `Raster_DrawPolygonDispatch` hands it to the routine at `0009df58`, entry `+0x84` of the active [display driver's table](#the-display-drivers), `0008ab80` for `VGA_4`, `000534ac` for `VGA_8` and `0008e22c` for `BMP_8`.
- **Open:** the `TSAlias*Poly` renderers `00054c9c`, `00054d6c` and `00054f00`.
- **Open:** what `00004f78`, `00004f60` and `00005690` do: the object's transform, and what the blend byte adds after a draw.
- **Open:** how `ObjList_EntryCompare` orders an entry with a box against another (`00021a4c`, `000216c4`), and so where a zone shape falls in the draw order among the machines and effects.
- **Open:** what the blend byte `000b5b65` is set to, and `000280e4` (matched to ES2's `Palette_SetRampsToRangeList`), which the blend path calls.
- **Open:** which classes carry type tags 1 to 9 in ES1: what the array at `000bc416` and the pools `000af0b4`, `000aeef8` and `000aef68` hold, and what each one's constructor stores at `+0`; and what sets the record array at `00092c34`.
- **Open:** what `00036c2c` and `00040d4c` are in the cockpit, what `00059280` returns, and what the word at a shape instance's `+8` selects.
- **Open:** what lights ES1 registers, and so what light a face receives.
- **Open:** no reference to `Raster_GetColourRamps` found by `es2_xref.py`.
