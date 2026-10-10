# DTS/DBA texture binding and poly shading

Covers how a `.DTS` poly gets a colour: which `.DBA` is bound to a model, how a textured poly maps its UVs, and how the three untextured poly types resolve their surface value. VSHELL findings are from disassembly of `VSHELL.EXE` in the `ES2Recon` Ghidra project; the shading sections are from `DBSIM.EXE` and are marked as such.

**Which `.DBA` is bound to a model is an application-level decision**, the `.DTS` naming no texture file ([`../formats/dts-shape.md`](../formats/dts-shape.md)) — see "DBA binding" below.

## TSBitmapPart's texture lookup (VSHELL)

Placement, sizing and rotation are in [`dts-billboards.md`](dts-billboards.md). The lookup is a plain frame index into whichever DBA is currently active:

1. `TSShape` carries its bound DBA pointer at `+0x26` (`TSShape_GetBoundBitmapArray`, `0046296d`). The field is the shape's: a `TSShape` is `0x2a` bytes, a `TSShapeInstance` `0xc` (its shape at `+4` and its own copy of the shape's per-sequence frames at `+8`).
2. `g_ActiveBitmapArray` (`DAT_005d8010`) is a process-wide "currently active DBA" global, with two accessors: `TSBase_GetActiveBitmapArray` / `TSBase_SetActiveBitmapArray`.
3. `TSShape_Render` (`00462730`, `TSShape`'s vtable `+0x1c`) and `TSShapeInstance_Render` (`00462894`, `TSShapeInstance`'s, which works on the instance's shape) save the global, swap in the shape's `+0x26`, call `TSPartList_Render` (`0042203a`, which dispatches each child's vtable `+0x1c`), then restore.
4. `TSBitmapPart_Render` (`00421db2`) reads `poly+0x10`, the part's bitmap tag, as a frame index, bounds-checks it against `g_ActiveBitmapArray`'s count, and looks up `*(int*)(*g_ActiveBitmapArray+8) + frameIndex*4`. The part's x and y offsets (`poly+0x12`/`+0x13`, single bytes) place the result.

Resolution is `activeDba.Frames[poly+0x10]`, with no UV interpolation.

## TSTexture4Poly (VSHELL)

A poly's colour index names a surface of its group through the stride in [`../formats/dts-shape.md`](../formats/dts-shape.md#surface-stride). Related symbols: `TSGroup_RenderPolys` (`00423497`), `TSBSPGroup_Render` (`00423709`, [below](#tsbspgroup-poly-order)), `g_ActiveSurfaceRecords` (`005d88a2`).

### Render path and UV generation

`g_TexturedPolyUseBitmapBrush` (`00471890`) selects one of two textured fills:

- `== 0` — the only branch that reaches a 3D rasterizer. Builds a per-vertex 3D position array from the group's points and a 4-entry UV-corner array, then calls `TSTexture4Poly_RasterizeA` / `RasterizeB` (`004202dd` / `00420900`), the [screen-linear and perspective-correct fills](#screen-linear-and-perspective-correct-fills).
- `!= 0` — a 2D bitmap fill. It resolves the frame's bitmap through the same pointer-table lookup `TSBitmapPart_Render` uses and installs it in a type-7 bitmap brush, the UVs the bitmap's extent inset by 3 pixels on each side and the remap row chosen by the face's shade. It projects the vertices (`Poly_ProjectShapeVertices`, `0045e694`), clips them to the near plane with their UVs when one fell behind it (`Poly_ClipRingToNearPlaneWithUV`, `0045ee2c`), and fills via `PolyFill_FillThenOutline` (`0045f364`) → `GL_FillPolygon` (`0045f8e7`) / `GL_FillPolygonReversed` (`0045f8d2`). DBSIM has the same branch behind its own `g_TexturedPolyUseBitmapBrush` (`0049f26c`). Both flags are 0 in the images.

The front/back value indexes a **20-byte-stride per-frame descriptor table** reached via `g_ActiveBitmapArray[1]` — one extra pointer dereference from `*g_ActiveBitmapArray`. The first 16 bytes are four `int32` fields `F0..F3`; a 5th field at byte 16 is passed to the rasterizer as a texture-data handle. UV corners, in vertex order:

```
V0 = (F0, F1)
V1 = (F2, F1)
V2 = (F2, F3)
V3 = (F0, F3)
```

from the decompiled assignment sequence `_DAT_0048a190`..`_DAT_0048a1ac` = `F0,F1,F2,F1,F2,F3,F0,F3`. The corners are the descriptor's own. DBSIM builds the descriptors as sub-rectangles of a shared atlas page ([below](#the-frame-descriptor-table-and-the-span-routines-dbsim)); VSHELL's builder is [Open](#open).

A `TSPoly` carries no per-vertex UVs: the frame rect's four corners map to the poly's four corners, and `TSTexture4Poly_Render` hands the rasterizer the whole quad, so one map covers the face.

### Three-vertex texture polys

The name says quad, but 40 `TSTexture4Poly`s across the retail fleet carry **three** vertices, and the original textures them like any other.

There is no vertex-count branch in the UV setup of either binary's render method: both fill all four corners unconditionally and pass the poly's own vertex count on to the rasterizer, which walks the corner array one entry per vertex. Traced in DBSIM — `Raster_SetupTexturedSpan` (`00468078`) steps `param_2 += 2` inside a `param_3`-bounded loop, `param_3` being what `TSTexture4Poly_Render` (`00474e9c`) reads from `poly+8`. A triangle therefore takes `V0`, `V1`, `V2` — the frame's top-left, top-right and bottom-right — and the fourth corner is written but never read.

**Seen from behind, a textured triangle is drawn with one corner borrowed from an earlier poly.** The position array is a static one (`006b7a9c`, four 12-byte slots) shared by every call. When the poly faces away, DBSIM swaps position slots 1 and 3 and UV corners 1 and 3 after copying the vertices in, to reverse the winding. On a triangle the copy filled slots 0-2 only, so the swap puts the triangle's own corner 1 into slot 3, where the rasterizer does not read it, and moves into slot 1 whatever slot 3 already held from the last texture poly that wrote it: corner 3 of a front-facing quad, or corner 1 of a back-facing quad or triangle. That point is projected through the current poly's own transform. The triangle drawn is corner 0, that stale point and corner 2, mapped with `(F0, F1)`, `(F0, F3)` and `(F2, F3)`. Two retail three-vertex texture polys draw a back, both in `CERBERUS.DTS` root 0, each in cell 0 of a cell-animation part, children 13 and 14 of its `TSBSPPart`. In both, the texture poly drawn just before the triangle in its group is a quad that draws both sides, so the borrowed point is that quad's corner 3 while the quad faces the eye and its corner 1 while it faces away. In child 13 the quad's corner 1 is the triangle's own corner 1, so with the quad turned away only the mapping of that corner changes; in child 14 the quad's corner 3 is the triangle's own corner 0, so with the quad facing the eye the triangle drawn has two corners at one point.

Over every `dts\*.DTS` the VOLs ship, 3 and 4 are the only vertex counts this type takes. Six each on `APOCA`, `APOC_DEB` and `TOMA_DEB`; three each on `HYPERION` and `HYPE_DEB`; two each on `CERBERUS`, `COLOSSUS`, `COLO_DEB`, `OUTLAW`, `OUTL_DEB`, `SAMSON`, `SAMS_DEB` and `TOMAHAWK`. Every one resolves to an in-range frame of its mech's bank. A count outside `[3, 4]` would run past the exe's own four-corner array, and no retail shape has one.

### Type identification

`TSTexture4Poly` is a mesh poly: it lives in a `TSGroup` and references real 3D vertices through its vertex-list offset and vertex count, structurally unlike `TSBitmapPart`'s 2D quad. It has its own vtable.

- `g_TSObjectTypeRegistry` (`0047f258`, VSHELL) — 18 entries, 12-byte stride, each `{tag:uint32, constructorFnPtr, nameStringPtr}`. `tag` matches an object's on-disk chunk header ([`../formats/dts-shape.md`](../formats/dts-shape.md#chunk-tags)).
- `TSTexture4Poly_Construct` (`0045ffe0`) stamps the vtable of each level of the class's inheritance chain in turn as the inlined constructors run, finishing with `g_TSTexture4PolyVtable` (`0047ee0c`).
- Slot `+0x1c` of that vtable is `TSTexture4Poly_Render` (`00422af5`), which:
- reads `poly+0xc`, the colour index, as an index into the per-surface runtime record array (`DAT_005d88a2`, 4-byte stride);
- runs `TSPoly_FrontBackVisibilityTest` (`0045e480`) on the points `poly+4`/`poly+6` index (`TSPoly.Normal`/`Center`), and a positive result picks the front colour pair ([below](#tspoly_frontbackvisibilitytest));
- resolves the descriptor at `DAT_005d8010[1] + idx*0x14`;
- builds world-space positions for all `poly+8` vertices;
- derives UV corners from the descriptor's own fields, not from per-vertex file data (`TSPoly` carries no on-disk UV fields);
- calls the rasterizers, which do per-edge clipping, fixed-point interpolation of screen position plus a 4th interpolant across spans, and per-pixel inner-loop draws.

RTTI type-name strings are unreferenced in this binary; the type registry is the reliable route to a constructor. See `project_es2_exe_recon` for the same dead end on `.BND`/`MECH`.

## DBA binding

Which `.DBA` a model gets is not in the `.DTS`.

VSHELL's `dba\rpr_<code>.dba`, `dba\<code>_int.dba`, `_bod`/`_wep`/`_out` are 2D Herc-display UI graphics, not mesh textures. In-game mech body textures are shared atlases in `simvol0/dba/`: `LIGHT`, `MEDIUM`, `HEAVY` (weight class), `ENEMY`, `NEWHERCS`, `APOCATEX`, `RAZORTEX`.

### DBSIM's mech-to-texture mapping

`MechType_InitOne` (`004201a8`) sets each root shape's `TSShape+0x26` to `&g_MechTextureGroupSlots + typeRecord[0x96]*8`. `g_MechTextureGroupSlots` (`004a9df6`) is an 8-byte-stride array, one slot per texture group. `typeRecord+0x96` is the mech type record's texture group, record offset 148 ([`mech-locomotion.md`](../simulation/mech-locomotion.md#mech-type-record)).

Byte-verified against every `simvol0/dat/*.DAT` (226 bytes each: 9-byte VOL prefix + 216-byte content + 1 trailer, matching the function's `0xd8` = 216-byte read):

| Record offset 148 | Group | Mechs |
|---|---|---|
| 0 | light | OUTLAW |
| 1 | medium | TOMAHAWK |
| 2 | heavy | SAMSON, COLOSSUS |
| 3 | enemy | DIABLO, CERBERUS, HYPERION, MIRIMAC, MONGOOSE, HEADHUNT, PITBULL, ACHILLES, RAMSES, SCARAB, STINGRAY, SPIDER |
| 4 | apocatex | APOCA |
| 5 | razortex | RAZOR |
| 6 | newhercs | OGRE, MAVERICK, RAPTOR2 |

### The flyers' bank

Every flyer chassis shares **one** bank, and it is group 3's `ENEMY`, the Cybrid mechs' own. `FlyerType_LoadResources` (`00422ed0`) writes the literal slot address `0x004a9e0e` into the shape's `+0x26`, which is `g_MechTextureGroupSlots` (`004a9df6`) plus `3 * 8` — there is no per-chassis choice, which fits a roster that is entirely Cybrid.

### Fleet audit

Every `dts\*.DTS` with a matching `dat\*.DAT`, 22 mechs:

- **21 of 22 have no out-of-range texture polys** — every front value of surface `colourIndex / 4` lands inside the selected bank's frame count. Across ~2000 polys, a wrong stride or a wrong bank mapping would produce out-of-range indices.
- **TOMAHAWK has 4 anomalous polys**, all identical: colour index 0 into a 1-entry surface array whose front and back values are both 3084 (`0xC0C`) against a 36-frame bank. Reads as a degenerate group in the source art.
- **Three-vertex texture polys resolve too**, on the same corner order as quads — see [Three-vertex texture polys](#three-vertex-texture-polys).

### Cutout frames

`BASETEX` frames 11, 36, 38, 39, 52, 53, 60, 61 and 63-65 are 20-73% palette index 0 each: the lattice girders on a structure's support towers, which show sky through the frame. Mech skins carry a handful of stray index-0 texels that are paint, not cutouts (9 of 44376 in `LIGHT`, 7 of 68464 in `MEDIUM`).

## Poly types and their colour mechanisms (DBSIM.EXE)

DBSIM's DTS type registry (`g_TSObjectTypeRegistry`, `004a63c8` — 12-byte `{tag, ctor, name}` entries keyed by the on-disk chunk marker) identifies a shape object's class by construction. Use it, not structural resemblance to VSHELL's renderers.

| Tag | Type | Vtable | Render (`+0x1c`) | Surface value means |
|---|---|---|---|---|
| `0x00140002` | `TSSolidPoly` | `004a5ef8` | `00474db4` | palette index |
| `0x00140003` | `TSShadedPoly` | `004a6000` | `0047542c` | shade-ramp number |
| `0x00140009` | `TSGouraudPoly` | `004a5fd4` | `004755c8` | shade-ramp number |
| `0x0014000f` | `TSTexture4Poly` | `004a5f24` | `00474e9c` | `.DBA` frame index |

The four are different mechanisms. Only `TSTexture4Poly` samples a bitmap.

The group's surface array is read raw (`TSGroup_ReadFromStream`, `0048e8e4`), so a renderer's surface value is the file's own `{int16 colour, int16 flag}` pair packed into one int32, flag in the high half. Every renderer runs [`TSPoly_FrontBackVisibilityTest`](#tspoly_frontbackvisibilitytest) first and takes the front entries (`surface[0]` fill, `[1]` line) or the back ones (`[2]`, `[3]`) by its answer, so each side of a poly has its own pair.

**Either side can be "do not draw", and that is the format's back-face culling.** The three flat renderers (`TSSolidPoly_Render`, `TSShadedPoly_Render`, `TSGouraudPoly_Render`) draw nothing when the chosen fill and line both carry `0x14` in their int32's top byte; retail uses it on back pairs only (flag 5120, against 1024 or 0 on the front). `TSTexture4Poly_Render` tests for `-1` instead (value and flag both `0xffff`): a back value of `-1` draws nothing, and a front value of `-1` draws the back frame from the front too, lit with the normal negated as a back is. Over the 55 retail `.DTS` and both `.DGS`, counting every LOD and cell:

| Type | Polys | Back not drawn |
|---|---|---|
| `TSShadedPoly` | 39645 | 35668 (flag 5120) |
| `TSTexture4Poly` | 4508 | 4414 (back `-1`) |
| `TSSolidPoly` | 1775 | 750 (flag 5120) |
| `TSGouraudPoly` | 1493 | 1413 (flag 5120) |

Per file, back pairs flagged 5120 are 790 of 998 polys in `SAMSON.DTS`, 2122 of 2202 in `BASES_AN.DTS` and 5090 of 6988 in `MECHWPNS.DTS`. No front pair is ever flagged or `-1`, and every poly that does draw its back draws it with the same values as its front, so on retail data the back pair decides only whether the back is drawn.

Retail usage counts: `TSSolidPoly` is rare — 12 polys in `BULLETS.DTS`, 57 in `ROCKETS.DTS`, 73 across the whole mech and building fleet. `TSShadedPoly` is nearly everything else: 1227 of APOCA's 1368 polys, 2049 of `BASES_AN`'s.

### `TSSolidPoly` — palette index, unlit, fill plus outline

`TSSolidPoly_Render` (`00474db4`) computes no light term and resolves **two** colours per face:

```
pick front pair (surface[0]=Front, surface[1]=FrontLine) or back pair ([2]/[3]) by visibility
skip if both have 0x14 in the top byte
row  = Raster_ShadeRampRow(0x80)        // the fixed unlit row; no light term is ever computed
fill = row[Front];  line = row[FrontLine]
PolyFill_FillThenOutline (0048d518) -> fill the polygon, then when line != fill re-draw it in `line`
```

The second pass is the rasterizer's **mode 4**, which `Raster_DrawPolygonDispatch` (`00483dac`)'s `iVar11 == 4` branch walks as a line loop over the poly's own vertex list, closing back to the first vertex — an outline, not a second fill. The `line != fill` test is on the **ramped** bytes, so two surface values that resolve to the same ramp output draw no outline.

**A two-vertex `TSSolidPoly` is a line, and a one-vertex one a pixel, not a degenerate face.** `Raster_DrawPolygonDispatch` (`00483dac`) draws any ring of fewer than three points the same way whatever the pass: one point through `Gfx_PlotPixel` (`004894e0`), two through `Raster_DrawLine` (`004838f8`), in the colour the pass carries. So the fill pass draws the line or pixel in the fill colour, and the outline pass, when the line colour ramps apart from the fill, draws it again in the line colour: what ends on screen is the line colour when it differs and the fill colour when it does not. The retail files carry 261 line polys and 80 point polys, all two-sided with flag 0 on every entry. `MECHWPNS.DTS` carries 92 of the lines and `SAMSON.DTS` none; a Particle Beam Weapon's four struts between housing and barrel are the visible case (`Reference/PBW_Comparison.png`), in their line colour, palette 198, `#5C5C5C`. The points sit on twelve roots: 34 on `SKIMMER.DTS` root 0, 16 on `PITBULL.DTS` root 0, 10 each in `MECHWPNS.DTS` and `MECHWPN2.DTS`, and the rest on `BASES.DGS` shape 29, `COLOSSUS.DTS`, `MAVERICK.DTS` and `OUTLAW.DTS`.

`PolyFill_FillBrush` (`006c60d8`)/`DAT_006c60dc` are one **brush** — `{mode, colour}` — and the default one: the fill dispatches on whatever `clipBlock+0x228` points at, which is normally this pair. A caller that installs a brush of its own there instead leaves these writes inert; the beam draw is the one that does, see [`../simulation/beam-visuals.md`](../simulation/beam-visuals.md#beamdats-colour-index-is-the-fill-brush-and-only-the-jagged-path-uses-it). `DAT_006c60d4`, the line colour, sits just below it and is not part of the brush.

Across all 55 retail `.DTS`, 11 roots carry a surface whose line colour differs from its fill: `BULLETS.DTS` root 4 (ATC35), five weapon-model roots in `MECHWPNS`/`MECHWPN2`, and 3-edge slivers on two `HYPERION` LODs and one `MIRIMAC` root. ATC35's three quads are gold `#D0CC3C` with no outline, `#ECCCAC` outlined `#E4E4E4`, and `#DCCCA0` outlined `#D8D4D4`.

Corroboration that the value is a palette index: of the 1517 plain `TSSolidPoly` surfaces across every retail `.DTS`, **all 1517** carry a zero flag and a value inside 0-255 — no distribution that tight is a per-shape frame index. The projectiles make it visible: their values are 85, 93, 94, 104 and 246 — the fire ramp and near-white — and none is a valid frame of the eight-frame `BULLETS.DBA`.

### `TSShadedPoly` — shade-ramp number, per-face light, fixed `.RMP` row

`TSShadedPoly_Render` (`0047542c`) resolves a face's colour in two lookups, and the same pair again for the line colour:

```
shade        = Light_ComputeShadeForFace(normal, center)     // 0048bedc, 0..255
paletteIndex = Palette_ShadeRampLookup(surface.Front, shade) // 00430e34
byte         = Raster_ShadeRampRow(0x80)[paletteIndex]       // 00468054, the FIXED unlit row
```

All of a shaded face's lighting is in the first lookup; the `.RMP` row is the same literal `0x80` the unlit solid renderer passes and never varies with light.

**The line entry outlines the face** exactly as a solid poly's does: the line value goes through both lookups at the face's own shade, and `PolyFill_FillThenOutline` redraws the edge loop in that byte whenever it differs from the fill's. Whether a face is outlined therefore depends on its light and on the depth slice. 304 retail polys name a line ramp apart from their fill ramp, 260 of them a ramp-12 fill with a ramp-2 or ramp-8 line: 156 on the flyer `SKIMMER.DTS`, 45 on `COLOSSUS.DTS`, 20 on `SAMSON.DTS`, and the rest on `OUTLAW`, `ACHILLES`, `TOMAHAWK`, `MONGOOSE`, the weapon models and the structure libraries. In `WORLD0` unfogged the ramp-12 pairs resolve apart at every shade, while the weapon models' ramp-194 fill with a ramp-102 line and `BASES_AN.DTS`'s ramp-102 fill with a ramp-135 line resolve alike at every shade, so those draw no outline.

`Palette_ShadeRampLookup` (`00430e34`):

```
idx = value & 0xff;  if (idx >= ActivePalette.rampCount) idx = 0;
pos = Q8Multiply(shade, ramp[idx].length);
if (pos == ramp[idx].length) pos = ramp[idx].length - 1;
return ramp[idx].indices[pos];
```

The surface value names a *material*; the light level picks a step along that material's brightness sequence.

### `TSGouraudPoly` — same ramp number, per-vertex light, no `.RMP` row

`TSGouraudPoly_Render` (`004755c8`; its tag function `TSGouraudPoly_GetClassTag` (`0048e450`) returns `0x140009`) shares the surface-pair selection and the light function, and differs in both of the things that decide a pixel:

```
for each vertex i:
    shades[i] = Light_ComputeShadeForFace(points[normalList[i]], points[vertexList[i]])
DAT_006c60e4 = shades;  PolyFill_FillBrush = 1; // fill mode 1: Raster_FillContourPolygon
PolyFill_FillThenOutline(...)
```

- The shade is computed **per vertex**, walking the normal list and the vertex list in step.
- **The ramp is looked up per vertex, and the face is filled in contour bands between the results.** Brush mode 1 of `Raster_DrawPolygonDispatch` (`00483dac`) is `Raster_FillContourPolygon` (`004879c0`): each vertex's shade picks the palette index at entry `(shade * length) >> 8` of the ramp the fill value names in the raster's ramp table `g_RasterColourRamps` (`004a4fbd`), and the poly is fanned into triangles from the first point of its ring, each filled by `Raster_FillContourTriangle` (`00486ddc`). That cuts the triangle at every whole palette index between its highest and lowest corner, along straight lines through points spaced evenly down its edges, which is where the index interpolated linearly in screen space crosses each whole value, and fills each band flat with the band's **upper** index. So a point takes the ceiling of its interpolated index, and the lowest corner's own index shows only where every corner has it. The interpolation is over palette indices, not entry positions; over a ramp of consecutive indices, such as `WORLD2`'s ramp 8 (172-187), the two agree, and a face shading from entry 3 down to 0 draws three equal bands, 3, 2 and 1.
- **Each band is a plain palette index.** The contour fill draws its bands through the dispatch's mode-0 brush, which writes the byte as it is (`Driver3_FillSpans`, `00489e78`), so neither the fixed `.RMP` row nor the [depth fade](distance-fog-and-sky.md#the-fade--raster_setdepthfadefromdistance-00467fec) touches it: a Gouraud surface's colour is the material ramp's entry straight through the palette at any distance.
- **Seen from behind, a Gouraud poly is shaded with its corners' light levels in reverse order.** For a poly facing away the renderer lights each vertex with its normal negated and passes "back" to `PolyFill_FillThenOutline` (`0048d518`), which fills through `Raster_DrawPolygonReversed` (`00484116`). That copies the screen points last to first but leaves the brush's shade list in vertex order, so ring slot `j` is corner `n - 1 - j` filled with corner `j`'s shade, and the fan runs from the last corner. A quad lit brightest at corner 0 shows its brightest corner at corner 3 from behind. 80 retail `TSGouraudPoly`s draw their back ([above](#poly-types-and-their-colour-mechanisms-dbsimexe)).

Distinguishing evidence: the `.RMP` row shifts every ramp entry down one step and collapses two pairs, so for `WORLD2`'s ramp 8 the shaded chain can never emit palette 178 (`#68687c`). A retail capture of the ramp-8 octagonal tower, structure type 15 (`Reference/Scramble_Training_Base_4.png`), shows `#9090a4 #848498 #7c7c90 #707084 #68687c #606074 #545468 #4c4c60 #444454 #3c3c4c #343444` on it — a consecutive run of the **raw** ramp entries, `#68687c` included. Neither the fixed-row chain nor a per-pixel row selected by the interpolated shade contains all eleven.

### `TSTexture4Poly` — frame index, ramp row by light, fullbright on demand

`TSTexture4Poly_Render` (`00474e9c`) picks the side's value by the same visibility test as the flat types, with its own `-1` rule for an undrawn side ([above](#poly-types-and-their-colour-mechanisms-dbsimexe)), and spends it as a **`.DBA` frame index**: the frame descriptor is `g_CurrentShapeDbaContext[1] + value * 0x14`, and its 5th int32 is the atlas page handle it passes to `Raster_SetupTexturedSpan` (`00468078`), which projects and near-plane-clips the vertices and hands the ring to `Raster_DrawPolygon` (`00468310`), whose span routine samples it. Light enters per pixel through the row selection, not as a multiplier — the span writes `Raster_ShadeRampRow(shade)[texelPaletteIndex]`, so the face's shade picks a row of the theater `.RMP` and the texel picks the column.

**The row count is a switch.** `DAT_004a5b1c` is the `.RMP`'s row count, installed as 32 by `World_LoadTheater` (`0042e010`), and this renderer is its only reader. When it is **zero** the poly is drawn in `Raster_DrawPolygon`'s mode 0 instead: a plain texture copy, with neither a light term nor a ramp lookup, so the texel's palette index reaches the framebuffer unchanged.

Two draws zero it, each for the duration of one object's shape render and restoring it from `DAT_004a5b20` afterwards: `Bullet_Draw` (`0040a120`) and `ObjList_DrawCellObjects` (`00428c60`) around a ground shape's draw ([`../simulation/ground-shapes.md`](../simulation/ground-shapes.md#the-draw-pass)). **That is what makes a round fullbright**, and it is a property of the draw rather than of the shape: the same shape drawn by anything else would be lit. The vtable slot is shared with the launcher rounds, so both classes get it. The one retail projectile shape it reaches is `BULLETS.DTS` root 8, the plasma cannon's round — every other projectile shape is `TSSolidPoly` geometry with no texture to copy; the other textured shape it reaches is the drop pod's square, root 3 of `FLAT2.DTS`.

None of the ramp's own rows is the identity this bypasses: row 0 lands at 0.36x the source colour and row 31 at 1.16x. Skipping the ramp skips the depth bias with it, so a fullbright surface does not fog either.

### The frame descriptor table and the span routines (DBSIM)

`BitmapArray_PackToAtlas` (`00469f38`) turns a loaded `.DBA` into the descriptor table `TSTexture4Poly_Render` reads as `slot[1]`. It packs every frame into 256x256 atlas pages with `Bitmap_PlaceInAtlasPage` (`00469d10`) and returns one 20-byte descriptor per frame. The mech texture groups (`g_MechTextureGroupSlots`), the terrain bank, `WPNTEX`, `BULLETS.DBA` and `BEAMTEX.DBA` are all built this way.

| Offset | Field |
|---|---|
| `+0x00` | `x0` (int32), the frame's left edge in its atlas page |
| `+0x04` | `y0` (int32) |
| `+0x08` | `x1` (int32), `x0 + width - 1` |
| `+0x0c` | `y1` (int32), `y0 + height - 1` |
| `+0x10` | page index (int16), the texture-data handle the rasterizer takes |
| `+0x12` | int16, 1 when any texel of the frame is palette index 0, else 0 |

`+0x12` is the transparency argument `TSTexture4Poly_Render` passes through `Raster_SetupTexturedSpan` as `Raster_DrawPolygon`'s last parameter, so the colour-key skip is decided per frame at load and a frame with no index 0 draws the same either way.

`Raster_DrawPolygon` (`00468310`) is `(vertexCount, vertices, mode, atlasPage, shadePtr, transparency)`. Its `mode` selects the span routine, and `transparency` selects that routine's opaque or colour-key half:

| mode | span routine | interpolants |
|---|---|---|
| 0 | `Raster_SpanTextured` (`0046ab10`) | u, v |
| 1 | `Raster_SpanTexturedShaded` (`0046ac48`) | u, v, and a shade level from `shadePtr` |
| 2 | `Raster_SpanTexturedGouraud` (`0046adad`) | u, v, and a third interpolant at vertex `+0x18`, where `Raster_SetupTexturedSpan` stores it |

Mode 0 with `transparency` zero is the opaque half of `Raster_SpanTextured`: fetch `atlasPage[v][u]`, store that palette byte to the framebuffer, step the fixed-point u and v. The non-zero form skips index 0 as a colour key and does not blend. Nothing in that path applies alpha, a shade level or a colour lookup. The beam draw submits through it: [`../simulation/beam-visuals.md`](../simulation/beam-visuals.md#drawing--beamtracer_draw-0040bc14-vtable-slot-0).

Before the spans, `Raster_ClipPolygonX` (`004698c4`) and `Raster_ClipPolygonY` (`00469b60`) clip the ring to the render context's rect and `Raster_BuildEdgeTables` (`00477c3c`) walks it into per-row left and right edge entries. Each span routine has a twin that draws only a list of visible runs on the row — `Raster_SpanTexturedClipped` (`0048b418`), `Raster_SpanTexturedShadedClipped` (`0048b4a3`), `Raster_SpanTexturedGouraudClipped` (`0048b52e`) — which `Raster_DrawPolygon` uses when `ActiveScanlineClipSpans` is set and either the context's clip mode is 2 or the [depth buffer](#the-depth-buffer) is on.

### Screen-linear and perspective-correct fills

`Raster_DrawPolygon` steps u, v and the ramp row linearly down each edge and across each row, so its texture map is **linear in screen space**. It is the fill `TSTexture4Poly_Render` uses while `g_TexturedPolyPerspective` (`0049f274`) is 0, which is its value in the image, and the one `Terrain_DrawCellQuad` uses for a cell whose nearest corner is at view depth `2 * DAT_0049aad0` or more ([`terrain-drawing.md`](terrain-drawing.md#the-cell-walk--terrain_drawvisiblecells-0046d0a4)).

`Raster_DrawTexturedPolyNear` (`0046865c`) is the **perspective-correct** fill: the nearer terrain cells, and `TSTexture4Poly_Render` when `g_TexturedPolyPerspective` is set. It stores `u/z`, `v/z` and `1/z` per vertex and draws each row in runs of `2^k` pixels, dividing back to u and v at each run's end and stepping linearly inside the run. `k` is 10 when the row's two ends have equal `1/z`, otherwise 4, 5, 6 or 7 as `|Δ(1/z)| >> 12` exceeds 3000, 1000, 300 or none of them. When `g_PerspectiveUseQuadraticFit` (`0049f278`) is set — `Terrain_DrawCellQuad` sets it for a cell whose nearest corner is beyond view depth 25000 — the row's start and its last partial run's end are taken from a quadratic fit that `Raster_BuildEdgeTables` makes through each edge's ends and middle, instead of a divide. Every mode draws with `Raster_SpanTexturedShaded`: mode 1's single ramp row applies, and mode 2's per-vertex row is interpolated down the edges but no span reads it.

VSHELL has the same pair: `TSTexture4Poly_RasterizeA` (`004202dd`) is the screen-linear fill (DBSIM's `Raster_SetupTexturedSpan` and `Raster_DrawPolygon` as one function) and `TSTexture4Poly_RasterizeB` (`00420900`) the perspective-correct one, chosen by `g_TexturedPolyPerspective` (`00471898`). RasterizeB's run thresholds are the globals `DAT_00471980`, `DAT_00471984` and `DAT_00471988`; its `g_PerspectiveUseQuadraticFit` (`0047188c`) is set per cell by `hgrid.cpp`'s cell callback (`Terrain_DrawCellRowRun`, `00429dac`).

### The depth buffer

When `maybe_g_DepthBufferEnabled` (`0049f270`) is non-zero, `Raster_SetupTexturedSpan` stores each vertex's view depth, and for each row `Raster_DrawPolygon` (and `Raster_DrawTexturedPolyNear`) runs `Raster_DepthTestSpan` (`0048b748`) before drawing. It walks the row's dwords in the buffer at `g_DepthBuffer` (`006c6014`), comparing each against the interpolated depth with `(row + g_DepthBufferFrameTag)` (`004a5b04`) in its top byte; where the stored dword is greater it writes the new value, and the pixels that pass form the row's visible runs. Those runs are drawn through the clipped span routines when `ActiveScanlineClipSpans` is set; otherwise the whole span is drawn. The same flag makes the `TSBSPPart` walk draw the viewer's side of each plane first ([below](#tsbsppart-child-selection)) and the terrain cell walk run near to far ([`polygon-fill.md`](polygon-fill.md#walking-a-polygons-cells)). It is 0 in the image; what writes it is [Open](polygon-fill.md#open).

`DepthBuffer_Alloc` (`0048b960`) allocates the buffer as width × height zeroed dwords and records the two sizes. `DepthBuffer_ClearWrappedRows` (`0048b7f0`) sets to `0xffffffff` every row whose `(row + g_DepthBufferFrameTag) & 0xff` is 0 and then decrements the tag, so with one clear per frame each row's tag byte falls by one a frame and a row is reset once every 256 frames rather than every frame. Whether anything calls them is [Open](#open).

### The projection, clip and fill chain (DBSIM)

A flat poly is drawn in three steps: project the face's vertices to screen points, clip the ring against the near plane if a vertex fell behind it, and fill the result. The face arrives in globals: `DAT_006c6968` the vertex count, `DAT_006c696a` the offset into the vertex-index list `DAT_006c6976`, and the per-point state byte `DAT_006c697e` (0 untouched, 1 behind the near plane, 2 projected) that memoises a vertex shared between faces. The screen points come out in `Poly_ScreenPoints` (`006cbb86`), count `DAT_006cbc86`.

| Function | Does |
|---|---|
| `Poly_ProjectShapeVertices` (`0048c848`) | Projects over the group's 6-byte `int16` point triples at `DAT_006c696c`. Called by `TSSolidPoly_Render`, `TSShadedPoly_Render` and `TSTexture4Poly_Render` |
| `Poly_ProjectIndexedVertices` (`0048c964`) | The same over 12-byte `int32` points at `DAT_006c6970`; returns non-zero when any vertex fell behind the near plane. Called by `BeamTracer_Draw` and by `TexPoly_Render` (`0042ff2d`) |
| `Poly_ClipRingToNearPlane` (`0048ce14`) | Run only when a vertex fell behind the plane: clips the ring and rebuilds the screen-point list. Called by `TSSolidPoly_Render`, `TSShadedPoly_Render` and `BeamTracer_Draw`, among others |
| `PolyFill_Fill` (`0048d4b4`) | Fills the screen polygon through `Raster_DrawPolygonEitherWinding`, then runs `PolyFill_FillThenOutline`'s outline pass without its mode-5 guard |

Both fills' outline pass is gated on the default brush (`DAT_006c60d4 != DAT_006c60dc`) and writes that brush, so a caller that installed its own brush on the context, as the beam draw does, gets an identical flat redraw instead of an outline.

### Shaded-poly surface values are ramp numbers

The ramps they index are the `.DPL`'s shade-ramp table ([`../formats/dpl-palette.md`](../formats/dpl-palette.md#the-shade-ramp-table)).

Corroboration that the surface value is a ramp number and not a frame index: across the retail fleet shaded-poly surface values cluster on **0-15**, overwhelmingly on the even (multi-step) slots. `APOCA.DTS` uses exactly four values over 1227 polys (12, 2, 0, 8); `SAMSON.DTS` five; `BASES_AN.DTS` nine, topped by 14, 8, 4, 12. A frame index into a 24-to-66-frame bank does not concentrate like that. Colour check: every distinct tone the tall chimney (structure type 14) shows in `Reference/Scramble_Training_Base_2.png` is in the set `WORLD2`'s ramp 8 produces, and its surfaces name ramp 8.

### Two shade calculations — terrain and shapes use different ones

Both walk the active light list. The mission sun is the only entry a mission starts with, and an impact effect can add more ([`effect-lights.md`](effect-lights.md)); for the sun alone both reduce to a function of `facing = -cos` between the surface normal and the direction the light travels (positive = lit). Normals carry length `0x800` and the sun's direction `0x1000`, so the raw dot is `0x800000 * cos`.

| | `Light_ComputeShadeForNormal` (`0048c060`, terrain) | `Light_ComputeShadeForFace` `0048bedc` (shapes) |
|---|---|---|
| gate | `t = dot` | `t = (dot - 0x400000) >> 1` |
| accumulate | `if (t < 0) shade -= (intensity * t) >> 22` | same |
| reduces to | `512 * facing` | `128 + 256 * facing` |
| edge-on (facing 0) | 0 | 128 |
| reaches 0 at | 90 degrees off the light | 120 degrees off |
| saturates at | facing 0.5 | facing 0.496 |

`Light_ComputeShadeForNormal` is what `Terrain_BuildSurface` bakes a cell with; see [`terrain-lighting.md`](terrain-lighting.md). Every poly renderer of a *shape* calls `Light_ComputeShadeForFace`. For a shape the falloff is half as steep, so a curved surface spends its gradient over twice the angular range, and a shadowed side is a mid tone that keeps falling rather than a floor of black.

Intensity scales both terms together (`shade = I * (0.5 + facing)` for the shape curve), so it does not move the zero crossing.

### The sun

One hardcoded directional light per mission, `Light_CreateMissionSun` (`00461240`), intensity `0x100`, direction **(±0.758, -0.359, -0.544)** at length `0x1000` in the sim's Z-up world. No ambient light is created anywhere in the binary. The constants it is built from and the derivation of that direction are in [`terrain-lighting.md`](terrain-lighting.md#the-sun).

### Normals live in the point list

A poly's normal is not a vector stored on the poly. It is a **point index**, and the shape's normals are extra entries in the same `TSGroup.Points` array its corners come from. All flat renderers dereference `TSPoly.Normal` (`poly + 4`) with the 6-byte point stride against the group's point base — `*(ushort *)(poly + 4) * 6 + DAT_006c696c` — and hand it, with the same treatment of `TSPoly.Center` (`poly + 6`), to `TSPoly_FrontBackVisibilityTest`.

A `TSGouraudPoly`'s normal list is the per-vertex form: an offset into the group's **index** array running parallel to the poly's vertex list, whose entries are point indices of normals rather than of corners. `TSGouraudPoly_Render` walks the two lists in step, one light call per vertex.

Verified on `BASES.DGS` shape 11 (structure type 15): every entry the list reaches has length exactly **2048** — the `0x800` the shade calculation is scaled around — and adjacent side panels share the normal at the edge between them.

**Stored normals oppose the corner winding.** For every poly in `BASES.DGS`, `BASES_AN.DTS` and `APOCA.DTS` — 12,656 of them, no exceptions and no intermediate values —

```
dot(normalize(cross(p1 - p0, p2 - p0)), storedNormal) == -1.000
```

so a normal derived from the corner order is the negation of the one the file carries. A renderer must use the stored normal, not the winding: the front/back sign is derived from the poly's normal and then applied to the **corner** normals, which come from this same point list. Mixing the two conventions inverts the light term. It is invisible on a flat poly, where the face and corner normals are the same vector and the sign cancels, and shows up only once per-vertex normals are in use.

### `TSPoly_FrontBackVisibilityTest`

Per **poly**, not per pixel. Takes the poly's own stored normal and centre points and answers "front" for a positive result: with a perspective focal shift it returns `dot(normal, eyeInModelSpace − centre)`, and with a shift of 0 it returns 1 when the normal, rotated into view space, has negative depth. DBSIM's copy is `0048c620`, VSHELL's `0045e480`. When it answers "back", the renderer negates *all* of that poly's normals before lighting them and takes the back surface pair instead of the front ([above](#poly-types-and-their-colour-mechanisms-dbsimexe)).

A result of zero is "back", which is every answer a poly with a zero stored normal gets: 175 of the 261 retail line polys and 9 of the 80 point polys carry `(0, 0, 0)`, all of them drawing alike from both sides.

The centre is the stored one, not a corner, and it need not lie on the poly's plane: 7644 of the 47080 retail polys of three or more corners have a centre more than one unit off their own plane, as far as 695 units off on a `CERBERUS.DTS` root 4 poly whose corners lie within 714 units of it. For such a poly the answer differs from one measured at a corner whenever the eye is between the poly's plane and the parallel plane through its centre.

### Poly order within a group

**A shape's polys are painted one after another, and wherever two overlap on screen the later one shows.** `TSGroup_RenderPolys` (`004758c8`) draws a group's polys in file order, and `TSPartList_Render` (`004766fc`) a part list's children in order. With the [depth buffer](#the-depth-buffer) off, nothing between them tests depth: the [front/back test](#tspoly_frontbackvisibilitytest) decides which polys draw at all, and among those the order decides what is on top. The children of a `TSBSPPart` are ordered by its tree walk instead ([below](#tsbsppart-child-selection)).

**Decals rely on it.** The TDF logo, `BASETEX` frame 25, is a `TSTexture4Poly` of its own, painted after the wall it sits on in the same `TSBSPPart` child: on `BASES.DGS` shapes 1, 2, 4, 12 and 40 (structure types 1, 2, 4, 16 and 17) and `BASES_AN.DTS` root 0 (type 5). Several are not in their wall's plane. Shape 1's tilts from 19.4 units behind its wall to 9.1 in front of it, shape 12's stand up to 44.4 units off theirs and root 0's up to 47.3. Painted after the wall, each shows whole.

**Coincident twins.** Across the retail `.DTS` and `.DGS` files, 84 pairs of polys drawn together in one `TSBSPPart` child, cell and detail level share their corners and their facing. Every pair is of one type but one: 73 `TSShadedPoly`, 8 `TSGouraudPoly`, 2 `TSSolidPoly`, and a `TSShadedPoly` followed by a `TSGouraudPoly` in `BHULKS.DGS` shape 0. Some pairs name different ramps, such as ramp 2 then 12 on `APOCA.DTS` root 6, and the later one is what shows. A textured poly over the same corners as an untextured one is common, 4199 pairs and 46 of them in `SAMSON.DTS` root 0, but 4183 of those pairs are two cells of one sequence or two levels of one detail part, which are never on screen together.

### `TSBSPPart` child selection

**A `TSBSPPart`'s `Parts` array is a pool its BSP tree indexes into, not a list that is drawn in order.** `TSBSPPart_Render` (`00476b0c`) binds the part's own node (`TSGroup_BindNodeTransform`), copies `g_EyeInModelSpace` — the eye in that node's space — to `006bb36c`, and calls `TSBSPPart_RenderNode` (`00476a1c`) on node 0. That walks the tree at `part+0x18` (14-byte nodes: an `int16` normal triple, an `int32` coefficient, then a front and a back `int16`):

```
t = transforms[node]                                   // part+0x1c, one int16 per node
eye = t == -1 ? eyeInPartSpace                          // every retail part's own transform is -1: the object
              : eyeInPartSpace brought into node t's space   // Transform_LocalPointDotNormal (004910b4) with
                                                             // g_ShapeNodeWorldTransforms[t]
d = dot(node.normal, eye) - node.coeff
first, second = (d < 0) == maybe_g_DepthBufferEnabled ? (back, front) : (front, back)
for each of first, second:
    if (value < 0)            draw nothing
    else if (value & 0x4000)  render Parts[value & 0x3fff]     // leaf
    else                      recurse into node `value`
```

So a child no node reaches is never drawn. Both sides of every node are walked whichever side the eye is on, so which children are drawn is fixed by the tree and only their order follows the eye. With the depth-buffer flag at its image value of 0, the side of each plane the eye is on is painted last, an eye exactly on the plane counting as in front.

**The order is the whole of the visibility between children.** With the depth buffer off, nothing tests depth, so wherever two children overlap on screen the one the walk reaches later is what shows, whichever is nearer. Retail data makes that visible two ways:

- **Coplanar faces in two children.** 51 overlapping pairs of polys in different children lie in the plane that separates them, among them eight markings on a face of the `MECHWPNS.DTS` and `MECHWPN2.DTS` roots 5 and 6 at their finest level, panels on `STINGRAY.DTS` roots 0-2, `MIRIMAC.DTS` root 4, `TOMAHAWK.DTS` root 5 and `COLOSSUS.DTS` root 3, and the floor plates of `BASES_AN.DTS` roots 2 and 6. The child on the eye's side of that plane covers the other.
- **Planes that do not separate their children.** At the rest pose, 422 of the 586 parts have a poly more than 1.5 units on the wrong side of a plane above it in the tree, so the later child is painted over a nearer earlier one where they overlap. Seen from 48 directions three bounding radii out, that covers up to 15% of the drawn pixels of `BASES_AN.DTS` root 5's level 3 from one direction, and over 1% in some direction for 57 parts.

Retail data has unreached children. Of the 586 `TSBSPPart`s across the retail `.DTS` and `.DGS` files, every node is reached from node 0 and no child is named twice, but 15 leave children out:

| Shape | Unreached children |
|---|---|
| `HYPERION.DTS` root 5 | one single-poly group |
| `PITBULL.DTS` root 5 | five cell-animation parts |
| `ROCKETS.DTS` root 1, finest level | the exhaust flame's cell-animation part ([`../formats/rockets-dat.md`](../formats/rockets-dat.md#dtsrocketsdts)) |
| `BASES.DGS` shape 3, middle level | one cell-animation part |
| `BASES.DGS` shape 13, two levels | an empty group in one, a 7-poly group in the other |
| `BASES.DGS` shapes 41 and 43, all three levels | one group each |
| `BASES.DGS` shape 44, three of its five levels | two cell-animation parts each |

`part+0x1c` is a parallel `int16` per **node**, not per child: the transform whose world matrix the splitting plane is brought into. `Shape_StampTransformId` (`00417530`) stamps a single transform id across all of them when a weapon model is attached to a machine.

### `TSBSPGroup` poly order

A `TSBSPGroup` is a `TSGroup` with a BSP tree over its own polys. VSHELL's `TSBSPGroup_Render` (`00423709`) sets up as a plain group's render does, then draws through `TSBSPGroup_RenderNode` (`0042362c`) from node 0 instead of walking the polys in order. DBSIM's `TSBSPGroup_Render` (`00475af8`) does the same through its own copy of the walk, `TSBSPGroup_RenderNode` (`00475a20`). VSHELL walks the nodes ([file layout](../formats/dts-shape.md#tsbspgroup-nodes)) as 10-byte records at `group+0x2a` whose constant is an `int32`. The splitting plane is the node poly's own stored normal:

```
d = dot(polys[node.poly].normal, eyeInModelSpace) - node.constant
first, second = d < 0 ? (front, back) : (back, front)
draw first; draw polys[node.poly]; draw second
    child < 0           nothing
    child & 0x4000      polys[child & 0x3fff]
    otherwise           recurse into node `child`
```

So each node's poly is drawn between the two half-spaces it splits, far side first, and a poly no node reaches is not drawn. No retail shape carries a `TSBSPGroup` ([`../formats/dts-shape.md`](../formats/dts-shape.md#tsbspgroup-nodes)).

## `TSDetailPart` level selection and STRUCTURE DETAIL

`TSDetailPart` is the shape-internal half of DBSIM's LOD system — its parts are one piece of a shape at several levels of detail, and the one drawn is chosen by projected size. `TSDetailPart_Render` (`004768bc`, vtable installed by `TSDetailPart_Ctor` (`00476834`)):

```
size = (radius << DAT_006c60ac) / max(FastMagnitude3D(viewOffset) - radius, 1)   // projected size
if (size == 0) size = 1
t    = Q10Multiply(g_TSDetailPartSizeScaleQ10, size)          // global detail scale, Q10
i    = g_TSDetailPartBias                             // global detail BIAS -- the STRUCTURE DETAIL setting
while (i < count - 1 && details[i] < t) i++
render(parts[min(i - g_TSDetailPartBias, count - 1)])
```

`radius` is the part's own `ClassItem` bounding radius (`part+8`). `viewOffset` is the part's **own node**, not the object's origin: `TSGroup_BindNodeTransform` runs first, and installing the node's transform (`Raster_SetModelTransform` (`0048c338`), or `Raster_RestoreState` (`0048d6e0`) for a node already composed) leaves the camera's position in that node's space in `g_EyeInModelSpace` (`006c60a0`-`a8`), whose length is the node's distance from the camera. `g_TSDetailPartSizeScaleQ10` (`004a1034`) is Q10 one, 1024, in the image ([Open](#open)). Thresholds are walked in file order and the part index is `i - bias`, so:

- `details[]` is ascending and index-aligned with `Parts[]`: **part 0 is the coarsest**, the last is the finest. Retail structure shapes end at 255 (`BASES.DGS` shape 5: `[5, 15, 35, 255]`).
- A **larger** `g_TSDetailPartBias` shifts the whole scale toward the coarse end. At bias 0 a close object reaches `count - 1`.

### Where the bias comes from

`g_TSDetailPartBias` (`004a1038`) is 0 except inside a draw slot that brackets its render with `TSDetailPart_SetBias` (`004768ac`) and a restore to 0:

| Draw slot | Bias pushed |
|---|---|
| `Structure_DrawWithDetailBias` (`004034f4`), slot `+0x00` of all five structure vtables | `g_TSDetailBiasFromStructureDetail` (`004a9638`) |
| `Flyer_Draw` (`004215cc`) | `DAT_004a9e48`, the same value, written beside it |
| `WeaponMount_RenderWithDetailBias` (`0040ded8`), slot `+0x1c` of all eleven weapon-mount vtables, `GUN_STATE`'s (`00498e44`) and every class below it | `g_TSDetailBiasFromHercDetail` (`004a98ec`) |
| `Debris_Draw` (`00408e6c`) | the piece's own `+0x50`: 0 from `Debris_Construct`, and `g_TSDetailBiasFromHercDetail` for the gun `WeaponMount_Destroy` throws |

`Bullet_Draw` (`0040a120`), the draw slot of both projectile classes, pushes nothing, so a launcher round's `ROCKETS.DTS` levels are chosen at bias 0 whatever either setting says. A machine's own draw pushes nothing either, and its chassis shapes carry no `TSDetailPart`; its roots are selected one level up ([`mech-shape-drawing.md`](mech-shape-drawing.md#the-lod-root-is-chosen-per-frame-per-object)).

**STRUCTURE DETAIL** reaches the first two rows through `StructureDetail_ApplySetting` (`0045d4f0`), which `Sim_RenderFrame` calls whenever the byte changes and `Sim_InitMissionSession` once at bring-up. Its key table is the identity over the three settings and its values (`g_StructureDetailValues`, `0049f02c`) are `{2, 1, 0}`: LOW is bias 2, MED HIGH 1, MAXIMUM 0. A byte past 2 matches no key and leaves the bias where it was. **HERC DETAIL** reaches the other two through `ShapeDetail_ApplyHercDetailSetting`'s `g_HercDetailTSDetailBiasValues`, `{2, 2, 1, 1, 0}` over its five settings.

Across the retail shape files `TSDetailPart`s sit in the structure libraries (`BASES.DGS`, `BASES_AN.DTS`, `BHULKS.DGS`), the flyer `SKIMMER.DTS`, the weapons (`MECHWPNS.DTS`, `MECHWPN2.DTS`) and `ROCKETS.DTS`. None is nested inside another or inside a `TSCellAnimPart`; cell-animation parts inside a level are common.

Levels are not always the same shape at different densities. `BASES.DGS` shape 10 (structure type 14, the tall chimney) is a 4-sided box with its corners on the world axes at level 0, and an octagon with its *vertices* on the axes at level 1 — a 45-degree difference in cross-section.

## Rejected readings

Readings a fresh pass could land on. Each is disproven; do not reintroduce.

| Reading | Why it is wrong |
|---|---|
| `g_TexturedPolyUseBitmapBrush` selects a flat, untextured fallback that resolves the frame only for its bounds-check assert | The resolved bitmap is the fill: the branch installs it, with UVs and a remap row, in a type-7 bitmap brush before calling the same fill chain `TSSolidPoly_Render` uses, and restores the default colour brush after. See [Render path and UV generation](#render-path-and-uv-generation) |
| `00474e9c` is `TSSolidPoly_Render` | It is `TSTexture4Poly_Render`; the type registry settles it. Assigned by resemblance to VSHELL's renderer |
| A flat poly's front value is a `.DBA` frame index sampled as a dither swatch | Only `TSTexture4Poly`'s is a frame index |
| A flat/shaded surface renders as the frame's **average colour** | The value is a palette index (`TSSolidPoly`) or a ramp number (`TSShadedPoly`/`TSGouraudPoly`). Averaging `BASETEX` frames 0/8/12 gives browns and greens where ramps 0/8/12 are greys and blue-greys |
| The front value as a direct `.DPL` palette index for the lit types | Right idea, wrong table — it indexes the ramp table, not the colour table |
| `abs()` on the light term (to keep winding-flipped triangles from going black) | Gives a surface pointing away from the sun the same light as one facing it. The original flips the normal toward the **eye**, then lights it signed |
| The terrain shade curve (`512 * facing`) applied to shapes | Shapes use `Light_ComputeShadeForFace`; see the table above |
| The fixed `.RMP` row applied to `TSGouraudPoly` | That path never calls `Raster_ShadeRampRow` |
| A brightness multiplier over an expanded RGB texel, in place of the indexed lookup | See [`terrain-lighting.md`](terrain-lighting.md#rejected-readings), which carries this row |
| Interpolating the normal and computing the shade per fragment (Phong) | The original lights each vertex once and fills between the ramp entries those shades pick; the two differ wherever the 0 clamp bites |
| A Gouraud face interpolates its shade and looks the ramp up per pixel, rounding down | It looks the ramp up per vertex and contour-fills between the corners rounding up ([`TSGouraudPoly`](#tsgouraudpoly--same-ramp-number-per-vertex-light-no-rmp-row)). Rounding down puts the band of a face's lowest entry on every graded face and no band of its highest, which made the type-15 octagon's back facet look graded ([Type-15 band widths](#type-15-band-widths)) |
| Reversing a back-facing Gouraud poly's ring for the fill reverses its per-vertex shades with it, so the back is shaded as the front is | `Raster_DrawPolygonReversed` reverses only the screen points, and a brush list only for mode 5; the mode-1 shade list keeps vertex order ([`TSGouraudPoly`](#tsgouraudpoly--same-ramp-number-per-vertex-light-no-rmp-row)) |
| The undefined bytes at `0047522f` inside `TSTexture4Poly_Render` copy a back face's vertices in reverse | That 76-byte loop does write vertex `i` to slot `count - 1 - i`, but it sits after the unconditional jump at `0047522a`, the branch at `0047515e` takes `g_TexturedPolyUseBitmapBrush`'s image value of 0 to the forward copy at `0047527b`, and `es2_xref.py` finds no branch or pointer into `0047522f`. See [Three-vertex texture polys](#three-vertex-texture-polys) |
| Normals are not reachable, so Gouraud cannot be implemented | Normals are extra entries in the point list; a Gouraud poly's normal list indexes them per vertex |
| A winding-derived face normal stands in for the stored one, the eye-facing flip cancelling the sign | It cancels only while the corner normals are that same vector. Once they come from the point list the sign is derived from one convention and applied to the other, and every Gouraud poly lights inside out — dark toward the sun. The two conventions are exactly opposed; see "Normals live in the point list" |

## Type-15 band widths

**The octagonal tower (structure type 15) shows the contour fill's rounding plainly: its facet facing away from the sun is one flat dark band, and the graded bands beside it belong to the next facet.** Row 200 of `Reference/Scramble_Training_Base_4.png` crosses three facets, all ramp 8:

| Facet | Width | Bands, entry: px |
|---|---|---|
| lit, seen at a steep angle | 28 px | 9 to 4, six bands of 4-5 px |
| the next | 97 px | 3: 32, 2: 32, 1: 33 |
| facing away from the sun | 61 px | 0: 61 |

Adjacent facets share the normal at the edge between them, so the corner entries run 9, 3, 0 and 0 across the three. [`Raster_FillContourTriangle`](#tsgouraudpoly--same-ramp-number-per-vertex-light-no-rmp-row) gives each whole step an equal band labelled with its upper entry: the first facet draws 9 to 4, the second 3, 2 and 1 with no band of 0, and the back facet, whose corners both light to entry 0 (`128 + 256 * facing` is negative at both), is filled flat. A lookup per pixel rounding down would draw the second facet as 2, 1 and 0 and leave the band of 3 to a graded back facet, which this light curve cannot produce.

## Open

- **Deferred:** whether anything writes `g_TSDetailPartSizeScaleQ10`. Its setter `TSDetailPart_SetSizeScale` (`0047689c`) has no reference `es2_xref.py` finds, which does not settle it, nor does it find one to VSHELL's copy (`TSDetailPart_SetSizeScale`, VSHELL `004221cd`). The image holds 1024.
- **Deferred:** [the `TSBSPGroup` poly order](#tsbspgroup-poly-order), which no retail shape exercises.
- **Deferred:** whether anything allocates or clears the depth buffer: `es2_xref.py` finds no caller of `DepthBuffer_Alloc` (`0048b960`), `DepthBuffer_ClearWrappedRows` (`0048b7f0`) or `DepthBuffer_ClearWrappedRowSpans` (`0048b83b`), nor of VSHELL's copies (`DepthBuffer_Alloc`, VSHELL `0045d3f4`; the clears and fills at `0045bab0`-`0045bb9d`).
- **Deferred:** the function that populates VSHELL's `g_ActiveBitmapArray[1]` descriptor table, which decides whether `F0/F1` (frame UV top-left) can be nonzero there. DBSIM's builder is [`BitmapArray_PackToAtlas`](#the-frame-descriptor-table-and-the-span-routines-dbsim), which places frames as atlas sub-rectangles. VSHELL's copy of it (`0045d270`) builds the table `hgrid.cpp` reads at `0048a310`, not that one. The global is installed from a shape's `+0x26` by `TSShape_Render` and `TSShapeInstance_Render`, the only callers of `TSBase_SetActiveBitmapArray` `es2_xref.py --binary VSHELL` finds. `es2_fieldscan.py --binary VSHELL 26` finds no write of `+0x26` in a shape function: its 15 writes sit in stream, text-box, player, map, sound, font, light and animation functions, while its read side finds `TSShape_GetBoundBitmapArray`. `es2_xref.py --binary VSHELL` finds no reference to `ANShapeInstance_Ctor` (VSHELL `004630c8`), the only caller of `TSShapeInstance_Ctor` (VSHELL `004627dc`) it finds.
- **Deferred:** what sets up VSHELL's shade ramps for its textured spans. `es2_xref.py --binary VSHELL` finds the ramp-table base `0047e890` written only in `ShadeRamp_Allocate` (`0045d5c8`), and the ramp dimensions and reciprocal table (`00485780`, `00485784`, `00485788`) only in `Raster_SetShadeRampDimensions` (`00420234`), and it finds no caller of either, while the shaded and Gouraud spans and `Raster_BuildEdgeTables` read them.
- **Deferred:** what writes `g_TexturedPolyPerspective` (`0049f274`), and so whether a retail shape is ever textured perspective-correct. `es2_xref.py` finds only the one read in `TSTexture4Poly_Render`.
