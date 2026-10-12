# DBSIM.EXE — shape polygon colour and distance fade

**ES1 colours a shape's faces without ES2's lighting table (`.RMP`).** A flat face draws its colour as it is; a shaded face picks an entry along a colour ramp by how much light falls on it; a Gouraud face fills bands between the ramp entries its corners pick; a textured face maps a whole bitmap onto its four corners and darkens it through one of eight colour tables. Nothing fades with depth. What changes a shape's colours with distance is the ramps themselves, which the theater's distance bands re-point before each object is drawn, and an object beyond the last band is not drawn at all. Addresses are `ES1DBSIM` image addresses ([`../formats/x32-executable.md`](../formats/x32-executable.md#the-image)); ES2's are DBSIM's.

The poly types and their surface pairs are ES2's ([poly types](../../retail/rendering/dts-texture-binding.md#poly-types-and-their-colour-mechanisms-dbsimexe)): each renderer runs `TSPoly_FrontBackVisibilityTest` and takes the front pair (fill, line) or the back pair, the three flat types draw nothing when both chosen entries carry `0x14` in their top byte, and the textured type tests for −1. Each surface entry is the file's `{int16 value, int16 flag}` read as one `int32`, the flag in the high half. A face's light is `Light_ComputeShadeForFace` (`0005bc30`), 0 to 255, ES2's ([two shade calculations](../../retail/rendering/dts-texture-binding.md#two-shade-calculations--terrain-and-shapes-use-different-ones)), with the stored normal negated for a face seen from behind. Projection, near-plane clipping and the fill-then-outline pass are ES2's ([`view-transform-and-projection.md`](view-transform-and-projection.md)).

## The colour ramps

The fills look ramps up in the raster's ramp table, `g_RasterColourRamps` (`0009a6f5`): 8-byte records `{int16 *entries, int16 length}` indexed by ramp number, ES2's (`004a4fbd`). `Palette_UploadWhole` (`0006931c`), given a palette, sends its colours to the display through the routine at `0009df08` (first entry `+6`, count `+4`, colours `+8`), then copies its ramp records (first `+0xe`, count `+0xc`, records `+0x10`) into the table with `Raster_SetColourRamps` (`000691f8`). `Raster_GetColourRamps` (`00069227`) copies records out of the table the same way.

A fill with brush mode 0 resolves its colour by the top byte ([`../../retail/rendering/polygon-fill.md`](../../retail/rendering/polygon-fill.md#the-brush-colour)): 0, the colour itself; 4, entry (*s* · length) >> 8 of ramp *r*, *r* being the colour's low 16 bits and *s* bits 16–23; anything else draws nothing.

## The four poly types

| Tag | Type | Vtable | Render (`+0x20`) | The surface value |
|---|---|---|---|---|
| `0x00140002` | `TSSolidPoly` | `0009b19a` | `TSSolidPoly_Render` (`0005403c`) | a brush colour |
| `0x00140003` | `TSShadedPoly` | `0009b1fe` | `TSShadedPoly_Render` (`000543e4`) | a brush colour, the face's light added |
| `0x00140009` | `TSGouraudPoly` | `0009b22a` | `TSGouraudPoly_Render` (`00054578`) | a ramp number |
| `0x0014000f` | `TSTexture4Poly` | `0009b1d2` | `TSTexture4Poly_Render` (`0005410c`) | a frame of the shape's bitmap bank |

The load functions in `g_TSObjectTypeRegistry` (`0009efa8`, entries `{tag, load function, name}`) store the vtables; the three `TSAlias*Poly` types have their own (`0009b266`, `0009b292`, `0009b2be`), whose render slots `00054c9c`, `00054d6c` and `00054f00` are not read here ([Open](#open)).

**`TSSolidPoly_Render`** puts the chosen fill and line entries in the brush (`000b5f04`) and the line colour (`PolyFill_LineColour`) as they are, with brush mode 0, and fills. A value with flag 0 is a palette index; one with flag `0x0400` is entry 0 of the ramp the value names. ES2's passes both through the `.RMP`'s unlit row first.

**`TSShadedPoly_Render`** does the same after ORing the face's light, & `0xff`, into bits 16–23 of both colours. With a flag of `0x0400` the fill resolves to entry (light · length) >> 8 of the ramp the value names, through the raster's table. ES2's looks the light up in the palette's own ramp (`Palette_ShadeRampLookup`) and passes the result through the `.RMP`'s unlit row; ES1's matches VSHELL's `TSShadedPoly_Render` (`00423024`), which ORs the light in the same way. What a flag of 0 draws is [Open](#open).

**`TSGouraudPoly_Render`** is ES2's ([`TSGouraudPoly`](../../retail/rendering/dts-texture-binding.md#tsgouraudpoly--same-ramp-number-per-vertex-light-no-rmp-row)): one light per corner, brush mode 1, the contour fill through the raster's ramp table.

**`TSTexture4Poly_Render`** maps a whole bitmap onto the poly's corners. The value, front or back by ES2's −1 rule, indexes the frames of the active bitmap bank (`000bdb5e`, its frame array at `+8`); a null frame draws nothing. The brush, installed at the render context's `+0x22c` for the fill and replaced by `PolyFill_FillBrush` after it, is `{7, 4, corners, the frame's bitmap header, a colour table}`. The corners' texture points are the frame's four corners, (0, 0), (columns − 1, 0), (columns − 1, rows − 1) and (0, rows − 1), in a 32-point buffer at `000b58d8` built on first use; a poly that crosses the near plane is clipped with `Poly_ClipRingToNearPlaneWithUV` into a second at `000b59dc`. The colour table is 0 while `000973f4` is 0; otherwise number (`000973f4` − 1) · light / 255 of the tables at `000973f0`, the light being the face's. There is no atlas and no frame descriptor: ES2's uses a frame index into packed atlas pages and the `.RMP` row for its light ([`TSTexture4Poly`](../../retail/rendering/dts-texture-binding.md#tstexture4poly--frame-index-ramp-row-by-light-fullbright-on-demand)). What brush mode 7 does with the table is [Open](#open).

### The texture colour tables

`Palette_LoadAndActivate` (`00027e5c`) calls `Palette_BuildTextureShadeTables` (`00028b28`) after it makes a palette the active one (`00092c20`). The first call builds eight 256-byte tables at `00092c40`, stores the array at `000973f0` and 8 at `000973f4` and `000973f8`; later calls find `00092c40` set and build nothing, so the tables follow the first palette loaded. With the palette's colours at `+8` (a dword each, its three low bytes the channels, unpacked by `Palette_UnpackColour`, `00028afc`) and its ramp records at `+0x10` (count `+0xc`), entry *c* of table *L* (0 to 7) is:

1. for each ramp whose entries hold *c*, the last such ramp deciding: *ref* = entry ((length − 1) · *L*) / 7 and *mid* = entry `Math_Q10Multiply(length − 1, 0x28a)`, about 0.63 of the way along; per channel, *ratio* = (*ref* << 10) / *mid*, 0 when *mid*'s channel is 0;
2. *c* itself when no ramp holds *c*;
3. otherwise the target `Math_Q10Multiply(c's channel, ratio)` per channel, and the palette index, 0 to 255, whose colour is nearest it by `Colour_Distance` (`000644c8`, `Math_FastMagnitude3D` of the difference), the first of equals and only below a distance of 1000.

So table *L* scales every ramp colour by how bright its ramp's step *L*/7 is against the step at 0.63, and re-quantises to the palette; a face's light picks table 0 in the dark up to table 7 at full light.

## Which objects are drawn

`ObjList_DrawFrameObjects` (`00021008`), given a blend byte and the eye position, draws a frame's objects. `Sim_RenderFrame` passes the byte at `000b5b65`, `000364f8` passes 0. It:

1. calls slot `+4` of every element of the ground-shape pool `000af044` ([the ground-shape set](../formats/wld-world.md#the-ground-shape-set)) within 30000 of the eye by `Math_DistanceBetweenPoints`, walking it back from the end with `Pool_Prev`;
2. stores the blend byte at `000afa98`;
3. for each of the `00092558` objects in the array at `000bc58e` for which `000003ac` (eye, object) returns 0 and whose slot `+8` block holds a non-negative `int32` at `+8`, fills a `0x36`-byte entry from the array at `000bc598`: the block's first 12 bytes (the position), the slot `+0x14` value as an `int16` at `+0xc`, the distance from the eye at `+0xe`, the object at `+0x32`, and 0 at `+0x12`; or 2 at `+0x12` and 24 bytes at `+0x18` from the block `00020738` returns, for an object among the array's first `000bc596` for which it returns one;
4. keeps the entry, in the list at `000bc59c`, only when `WorldShades_IsBeyondLastBand` (`00025f38`) is false for its distance;
5. calls `00021554` with the eye and `00021d04` with the count and the list ([Open](#open)).

An object's type tag is its `int16` at `+0`, its vptr at `+2`; its vtable's `+0x14` slot is the radius the bands read.

`ObjList_DrawEntryRender` (`00020f40`), whose address `00092562` holds, draws one entry: `00004f78` with `000bc19c` and the entry's position and `+0xc` value, `WorldShades_ApplyForObject` with the blend byte, the distance and the object, the object's slot `+4`, then `00005690` when the blend byte is set. ES2's (`ObjList_DrawEntryRender`, `0042876c`) also sets the depth fade from the entry's distance; ES1's sets none.

### The distance bands

The bands are ES2's ([distance colour bands](../../retail/rendering/distance-fog-and-sky.md#distance-colour-bands--worldshades_applyforobject-0042e8e8)), from the theater's world file ([the layout](../formats/wld-world.md#the-layout)) and `dat\shades` (`World_LoadShades`):

- **`WorldShades_DistanceBand` (`00025cb4`)** counts the thresholds the distance has passed, at most one fewer than there are, from `WorldShades_BandsTagged` (`000afdc4`, count `000bc5b8`) for a non-zero tag and `WorldShades_BandsTag0` (`000afdc8`, count `000bc5ba`) for tag 0, a tag-5 object first adding `WorldShades_DistanceOffsets` (`000bc5c6`) entry 0, or entry 1 when its radius is 5000 or more, clamped at 0.
- **`WorldShades_ApplyForObject` (`00025d44`)**, given the blend byte, the distance (clamped at 0) and the object, re-points each `{ramp, column}` pair of the tag's `SHADES.DAT` list at range (column × rows + band) of `WorldShades_LevelRanges` (`000afdbc`), rows being `000bc5be`, with `Palette_SetRampsToRange` (`00028044`). With the blend byte set it passes `000280e4` a list of two ranges instead: the band's range less its last *q* + 1 indices, *q* being its length >> 2, then *q* + 1 indices from (start + length − *q*) of the band's `WorldShades_BlendRanges` (`000afdc0`) range, the two range pointers in a two-entry array. While `000928ea` is set it runs `WorldShades_ResetRamps` instead.
- **`WorldShades_ResetRamps` (`00025e88`)** points ramps 0, 2, 4, 6, 8, 10, 12 and 14 at the range {16, 16}; ES2's last is ramp 24. `000364f8` sets `000928ea` around its call of `ObjList_DrawFrameObjects` and clears it after.
- **`WorldShades_IsBeyondLastBand` (`00025f38`)**, given the distance and the object, takes the same table and offsets, a tag-8 object adding offset entry 0, and is true when the distance exceeds the table's last threshold, unsigned. It is the cull above.

`Palette_SetRampsToRange` (`00028044`), given the range, the count and the first ramp, in stack order, is ES2's: it writes the range's consecutive indices into each record of the array at `00092c34` from the first, and its length, then copies the records into the raster's table with `Raster_SetColourRamps`.

So the shaded, Gouraud and solid faces whose colours go through a ramp take the band's palette range for that ramp, and their colours change in steps as the object recedes, one step per threshold. Past the last threshold the object is not drawn: in retail the tag-0 table ends at 90000 to 364000 by theater ([the retail files](../formats/wld-world.md#the-layout)).

## Functions

| ES1 | Address | Notes |
|---|---|---|
| `ObjList_DrawEntryRender`, `ObjList_DrawFrameObjects` | `00020f40`, `00021008` | |
| `WorldShades_DistanceBand`, `WorldShades_ApplyForObject` | `00025cb4`, `00025d44` | the object last, as with every callee-cleaned function ([calling conventions](../formats/compiler.md#calling-conventions)) |
| `WorldShades_ResetRamps`, `WorldShades_IsBeyondLastBand` | `00025e88`, `00025f38` | |
| `Palette_SetRampsToRange` | `00028044` | |
| `Palette_UnpackColour`, `Palette_BuildTextureShadeTables` | `00028afc`, `00028b28` | |
| `TSSolidPoly_Render`, `TSTexture4Poly_Render` | `0005403c`, `0005410c` | |
| `TSShadedPoly_Render`, `TSGouraudPoly_Render` | `000543e4`, `00054578` | |
| `Colour_Distance` | `000644c8` | |
| `Raster_SetColourRamps`, `Raster_GetColourRamps`, `Palette_UploadWhole` | `000691f8`, `00069227`, `0006931c` | |

## Open

- **Open:** what a `TSShadedPoly` with a flag of 0, whose fill reaches the span routine as a palette index with the light in bits 16–23, draws, and which flags ES1's retail shapes carry on their shaded, solid and Gouraud faces.
- **Open:** what brush mode 7 does with the texture's colour table: `Raster_DrawPolygonDispatch` hands it to the routine at `0009df58`, which the image holds as 0 and the driver fills.
- **Open:** the `TSAlias*Poly` renderers `00054c9c`, `00054d6c` and `00054f00`.
- **Open:** what `000003ac`, `00020738`, `00021554`, `00021d04`, `00004f78`, `00004f60` and `00005690` do: the visibility test, the second block, the sort and draw of the list, the object's transform, and what the blend byte adds after a draw.
- **Open:** what the blend byte `000b5b65` is set to, and `000280e4` (matched to ES2's `Palette_SetRampsToRangeList`), which the blend path calls.
- **Open:** which classes carry which type tags in ES1, what ES1's `SHADES.DAT` lists, and what sets the record array at `00092c34`.
- **Open:** what lights ES1 registers, and so what light a face receives.
- **Open:** no reference to `Raster_GetColourRamps` found by `es2_xref.py`.
