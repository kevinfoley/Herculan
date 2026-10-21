# DBSIM.EXE — shape polygon colour and distance fade

**ES1 colours a shape's faces without ES2's lighting table (`.RMP`).** A flat face draws its colour as it is; a shaded face picks an entry along a colour ramp by how much light falls on it; a Gouraud face fills bands between the ramp entries its corners pick; a textured face maps a whole bitmap onto its four corners and darkens it through one of eight colour tables. Nothing fades with depth. What changes a shape's colours with distance is the ramps themselves, which the theater's distance bands re-point before each object is drawn, and an object beyond the last band is not drawn at all. Addresses are `ES1DBSIM` image addresses ([`../formats/x32-executable.md`](../formats/x32-executable.md#the-image)); ES2's are DBSIM's.

The poly types and their surface pairs are ES2's ([poly types](../../retail/rendering/dts-texture-binding.md#poly-types-and-their-colour-mechanisms-dbsimexe)): each renderer runs `TSPoly_FrontBackVisibilityTest` and takes the front pair (fill, line) or the back pair, the three flat types draw nothing when both chosen entries carry `0x14` in their top byte, and the textured type tests for −1. Each surface entry is the file's `{int16 value, int16 flag}` read as one `int32`, the flag in the high half. A face's light is `Light_ComputeShadeForFace` (`0005bc30`), 0 to 255, ES2's ([two shade calculations](../../retail/rendering/dts-texture-binding.md#two-shade-calculations--terrain-and-shapes-use-different-ones)) over ES1's own [lights](#the-lights), with the stored normal negated for a face seen from behind. Projection, near-plane clipping and the fill-then-outline pass are ES2's ([`view-transform-and-projection.md`](view-transform-and-projection.md)).

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
| `VGA_8` | 1 | `000a7b70` | `VGA8_BlitBitmap` (`0008b9a8`) | `VGA8_BlitBitmapClipped` (`0008c27c`) |
| `BMP_8` | 3 | `000a7c00` | `BMP8_BlitBitmap` (`0008d700`) | `0008dc3c` |

**The simulator draws through `VGA_8`, a 320-pixel-wide planar VGA display.** `Sim_CreateDisplay` (`0005a5f4`), which `Sim_StartMission` calls (`0005af1a`), builds a `0x90`-byte display with `GLDisplay_CtorAndOpen` (`0006a3fc`), named `VGA_8` and given no bitmap, and makes it current with `Raster_SelectRenderTarget` (`0006a644`), which installs the table of the driver its `+0x30` names, 1. `Sim_CreateDisplay` then sets the display's draw page `+0x3c` to 1 and its visible page `+0x34` to 0, passing each to the driver's routine at `0009df18` and `0009df1c`, its `+0x48` holding the four pages' origins, {0, 0}, {0, 200}, {0, 400} and {0, 600}, which `Sim_StartMission` moves when it widens the screen to 640 pixels ([the video memory](screen-pages.md#the-video-memory)); it builds the render context `000b5b6e` from the display's clip block (`00069a84`) with its origin (−160, −100) at `+0x220`/`+0x224` and 1 at `+0x208`, the clip mode `+0x204` of its block at `+4`, and installs it with `Raster_InstallRenderContext`. `Sim_RenderFrame` installs no context of its own: it moves `000b5b6e`'s origin by the view shake for the length of the scene's draw.

**The scene is drawn through `000b5b6e`, inside the current view's clip regions.** `Raster_InstallRenderContext` (`00069be0`) is the one writer of the installed context `000bdbda` that `es2_xref.py --decode` finds. Of the 26 calls of it `es2_xref.py` finds, each but `Sim_CreateDisplay`'s either reinstalls a context saved before it or is undone by a later call that does: `Cockpit_PopRenderContext` after `Cockpit_PushCanvasContext`, `AlertPanel_Leave` after `AlertPanel_Enter`, `Ttm_RunAll`'s second call after `TtmScript_Run`'s, the callers' after `0003e790` and `0003fe04`, and a second call in the same function for the rest. So `000b5b6e` is installed whenever the simulator's own code runs outside such a pair, the scene's draw and the gunsight's update among it ([the target box](../simulation/cockpit-gunsight-hud.md#what-the-box-is-drawn-through)). From the mission start's first `CockpitView_SetView` on, which `Sim_StartMission` makes before its first frame, `CockpitView_ApplyViewState` keeps it in clip mode 2 with the current view's regions, or the external view's one rect in view 4 ([`CockpitView_ApplyViewState`](../simulation/cockpit-views.md#cockpitview_applyviewstate-000222c0)), and `CockpitView_SetView` puts the mode back after the canopy's blit. So the scene is drawn inside the regions alone, and a [bitmap fill](#bitmap-fills-brush-modes-5-and-7) walks by rows.

The display functions are ES2's, under ES2's names ([below](#functions)). `GLDisplay_Open` (`0006a480`) opens the driver with `RasterDriver_OpenDisplay` (`0007330a`), which calls the driver's slots 8 (the bitmap, which `VGA_8` takes as a mode number, [the video memory](screen-pages.md#the-video-memory)) and 9, each of which must return 0, then 10, 11 (the bitmap, display `+4`) and 7 (display `+0x28`, display `+4`); it then allocates the display's two `0x22c`-byte clip blocks at `+0x40` and `+0x44`, the first set up by `ClipBlock_Init` (`000759b0`) for the display's width `+8` and height `+0xc`, with clip mode 0 and the rect (0, 0, width − 1, height − 1), and copied into the second.

`VGA_8`'s routines address the screen as four planes of `0009df86` bytes a row, 160 in a mission ([the video memory](screen-pages.md#the-video-memory)): column *x* of row *y* is byte *p* >> 2 of plane *p* & 3, *p* = *x* + 4 · (`0009df8e`[*y*] + `0009e604`) + `0009e610`, the plane chosen through the sequencer's map mask (port `3c4` index 2), in the segment at `0009df84`.

## Bitmap fills (brush modes 5 and 7)

**A bitmap fill stretches a bitmap over a polygon along straight lines across the screen, with no perspective, each pixel taking the nearest texel; mode 5 leaves the bitmap's palette index 0 unpainted, so what was drawn before shows through, and mode 7 paints every texel through a 256-byte colour table.** `Raster_DrawPolygonDispatch` hands both modes to the active driver's `+0x84` (`0009df58`); the simulator's driver is `VGA_8` ([above](#the-display-drivers)), whose routine is `VGA8_FillBitmapPolygon` (`000534ac`). ES2 draws them with driver 3's routine, by rows only, with the same rule for index 0 ([ES2's mode 5](../../retail/rendering/dts-billboards.md#brush-mode-5-skips-palette-index-0)).

The brush is at the clip block's `+0x228`: `+8` the texture points, an `int32` {*u*, *v*} in texels for each vertex; `+0xc` the bitmap header, its columns at `+2` and its pixels at `+0xa`; `+0x10` the colour table, read only when the mode is 7. Neither the brush's `+4` nor the header's flags enter. The clip rect is the block's {*x0*, *y0*, *x1*, *y1*} at `+0x20c`. Given the count and the points:

1. **Nothing is drawn** for fewer than three points, for a polygon whose smallest *x* is not below its largest or whose smallest *y* is not below its largest, or for one wholly outside the clip rect.
2. **The part above the clip rect is cut off first.** When the smallest *y* is less than *y0*, `VGA8_BitmapPolyClipTop` (`00053880`) cuts the polygon at *y* = *y0*, screen and texture points alike, into the 32-point buffers at `000b56d0` and `000b57d4`, and the routine starts over on the result, drawing nothing when fewer than three points are left. Walking the ring from vertex 0, it keeps each vertex with *y* ≥ *y0* and, when the edge to the next vertex crosses *y0*, adds the crossing *c* = *c*ᵢ + (*y0* − *y*ᵢ) · (*c*ₙ − *c*ᵢ) / (*y*ₙ − *y*ᵢ) for each of *x*, *u* and *v*, *i* being the vertex the edge leaves and *n* the one it reaches, the division truncating. The other three sides are not cut.
3. **The walk** crosses the polygon by columns when the clip mode `+0x204` is not 2, and by rows when it is 2; the scene, drawn in clip mode 2 ([above](#the-display-drivers)), walks rows. Write *w* for the walked coordinate (*x* by columns, *y* by rows) and *s* for the other. Two edges start from the first vertex with the smallest *w* and go round the ring toward the first vertex with the largest: by columns edge A forward and B backward, by rows A backward and B forward, which for a ring wound clockwise on screen puts A at the top (columns) or the left (rows). An edge runs from a vertex to the next one in its direction, passing over vertices with the same *w*. Over its Δ = Δ*w* steps, step *k* has *s* = *s*₀ + ⌈*k* · Δ*s* / Δ⌉, kept exact with a remainder, and *u* = *u*₀ · 2¹⁶ + *k* · `Math_Q16Divide`(Δ*u* · 2¹⁶, Δ · 2¹⁶) in 16.16, *v* likewise (`VGA8_BitmapEdgeSetupColumns`, `00053c18`, and `VGA8_BitmapEdgeStepColumns`, `00053a3c`; by rows `VGA8_BitmapEdgeSetupRows`, `00053a94`, and `VGA8_BitmapEdgeStepRows`, `000539e4`). After its last step an edge hands over to the next one, which starts from its vertex's own values; when the next one would start at the far vertex, the walk ends.
4. **The lines.** *w* goes up by one from the smallest. For each *w* not before the clip rect's near side (*x0* by columns, *y0* by rows) the routine draws the span from A's *s* to B's; then both edges step, and the walk ends when either edge ends, when *w* passes the far side (*x1*, *y1*), or when A's *s* exceeds B's. So the far vertex's line, the largest *w*, is never drawn.
5. **The span** (`VGA8_SpanBitmapColumn`, `0007394a`; `VGA8_SpanBitmapRow`, `000735a4`) runs from *a* = A's *s* to *b* = B's, both included, *n* = *b* − *a* + 1 pixels, and does nothing when *a* is beyond the clip rect's far side or *b* before its near side. Its steps are *du* = `Math_Q16Divide`(*u*_B − *u*_A, *n* · 2¹⁶) and *dv* likewise, and pixel *i* takes *u* = *u*_A + (*du* >> 1) + *i* · *du*, *v* likewise: the middles of *n* equal steps from A to B. It draws from the later of *a* and the near side to the earlier of *b* and the far side, the pixels it skips still stepping. The texel is byte ⌊*v* + ½⌋ · columns + ⌊*u* + ½⌋ of the pixels, *u* and *v* in texels with their integer parts read as unsigned 16-bit: the nearest texel, a texture point naming a texel's centre. Nothing wraps and nothing bounds the address. A column span writes one plane, 80 bytes a pixel. In clip mode 2 the row span draws its row's part inside every region of the clip block's list that holds the row, from the last region to the first, a rect (type 0, {*x0*, *y0*, *x1*, *y1*} at `+4`) or a span region (type 2, `+4` pointing at `{int16 top, int16 rows}` and an `int16` {*x0*, *x1*} per row), and stops at a region of any other type.
6. **The store.** With no colour table a texel of 0 is not written, leaving the pixel as it was, and any other texel is written as it is (`OR AH,AH` / `JZ` at `00073b47` by columns and `0007390a` by rows). With a table every texel, 0 included, is written as table[texel]. A mode-7 brush whose table is 0 thus draws as mode 5.

So with `VGA_8`:

- the [clouds](ground-and-sky.md#the-clouds) (`TexPoly_Render`), the [rolled ground](ground-and-sky.md#the-textured-ground) and the [rolled skyline](ground-and-sky.md#the-horizon-skyline) (`Bitmap_BlitRotatedScaled`) draw through mode 5 and leave each bitmap's index-0 texels unpainted;
- a [textured shape face](#the-four-poly-types) draws through mode 7 every texel as its entry in the [texture colour table](#the-texture-colour-tables) its light picks, index 0 included, and as mode 5 while `000973f4` is 0, before the first palette load builds the tables.

`BMP_8`'s routine (`0008e22c`) walks rows like ES2's and reads only the brush's `+8` and `+0xc`, passing its span no colour table ([Open](#open)).

## The four poly types

| Tag | Type | Vtable | Render (`+0x20`) | The surface value |
|---|---|---|---|---|
| `0x00140002` | `TSSolidPoly` | `0009b19a` | `TSSolidPoly_Render` (`0005403c`) | a brush colour |
| `0x00140003` | `TSShadedPoly` | `0009b1fe` | `TSShadedPoly_Render` (`000543e4`) | a brush colour, the face's light added |
| `0x00140009` | `TSGouraudPoly` | `0009b22a` | `TSGouraudPoly_Render` (`00054578`) | a ramp number |
| `0x0014000f` | `TSTexture4Poly` | `0009b1d2` | `TSTexture4Poly_Render` (`0005410c`) | a frame of the shape's bitmap bank |

The load functions in `g_TSObjectTypeRegistry` (`0009efa8`, entries `{tag, load function, name}`) store the vtables. The three `TSAlias*Poly` types have their own (`0009b266`, `0009b292`, `0009b2be`), whose render slots, `TSAliasSolidPoly_Render` (`00054c9c`), `TSAliasShadedPoly_Render` (`00054d6c`) and `TSAliasGouraudPoly_Render` (`00054f00`), are the code of the solid, shaded and Gouraud renders but for the fill, which goes through `PolyFill_FillThenOutline_Thunk` (`0005d3c0`) to the same `PolyFill_FillThenOutline`; so an alias poly draws as its plain type does. ES2's `TSAlias*Poly` renders (DBSIM `00475bc4`, `00475c80`, `00475de0`) also fill through the thunk. A plain poly's slot `+0x20` in the base vtable `0009b132` is `TSPoly_Render` (`00054038`) and `TSNullObj`'s (`0009b0da`) is `TSNullObj_Render` (`00054034`), each a `RET 4`.

A group draws its polys as ES2's `TSGroup_RenderPolys` does ([poly order](../../retail/rendering/dts-texture-binding.md#poly-order-within-a-group)): ES1's (`00054928`) installs the group's node, swaps the render context's line and fill brushes (`+0x228`, `+0x22c`) for `PolyFill_LineBrush` and `PolyFill_FillBrush` and its `+0x20c` for 0, binds the group's surfaces, points and indexes, draws each poly through its slot `+0x20` in file order, and puts the three back. `TSBSPGroup_Render` (`00054bbc`) does the same through `TSBSPGroup_RenderNode` (`00054aa4`), ES2's walk ([`TSBSPGroup` poly order](../../retail/rendering/dts-texture-binding.md#tsbspgroup-poly-order)), with the vtable `00096282` that `TSBSPGroup_Ctor` (`00054a34`) and `TSBSPGroup_Dtor` (`00054a6c`) store.

**`TSSolidPoly_Render`** puts the chosen fill and line entries in the brush (`000b5f04`) and the line colour (`PolyFill_LineColour`) as they are, with brush mode 0, and fills. A value with flag 0 is a palette index; one with flag `0x0400` is entry 0 of the ramp the value names. ES2's passes both through the `.RMP`'s unlit row first.

**`TSShadedPoly_Render`** does the same after ORing the face's light, & `0xff`, into bits 16–23 of both colours. With a flag of `0x0400` the fill resolves to entry (light · length) >> 8 of the ramp the value names, through the raster's table. ES2's looks the light up in the palette's own ramp (`Palette_ShadeRampLookup`) and passes the result through the `.RMP`'s unlit row; ES1's matches VSHELL's `TSShadedPoly_Render` (`00423024`), which ORs the light in the same way. What a flag of 0 draws is [Open](#open); the theater shapes have no such face ([below](#the-theater-shapes-faces)).

**`TSGouraudPoly_Render`** is ES2's ([`TSGouraudPoly`](../../retail/rendering/dts-texture-binding.md#tsgouraudpoly--same-ramp-number-per-vertex-light-no-rmp-row)): one light per corner, brush mode 1, the contour fill through the raster's ramp table.

**`TSTexture4Poly_Render`** maps a whole bitmap onto the poly's corners. The value, front or back by ES2's −1 rule, indexes the frames of the active bitmap bank (`000bdb5e`, its frame array at `+8`); a null frame draws nothing. The brush, installed at the render context's `+0x22c` for the fill and replaced by `PolyFill_FillBrush` after it, is `{7, the ring's point count, corners, the frame's bitmap header, a colour table}`, the count 4 or what the near-plane clip leaves. The corners' texture points are the frame's four corners, (0, 0), (columns − 1, 0), (columns − 1, rows − 1) and (0, rows − 1), in a 32-point buffer at `000b58d8` built on first use; a poly that crosses the near plane is clipped with `Poly_ClipRingToNearPlaneWithUV` into a second at `000b59dc`. It fills with `PolyFill_FillThenOutline`(*reversed*), *reversed* 0 when it draws the front value and 1 when it draws the back value (`[EBP-0x14]`, pushed at `00054399` and `000543aa`), and no winding test is made. So a front face's ring goes to `Raster_DrawPolygonDispatch` as projected, and a back face's to `Raster_DrawPolygonReversed`, which reverses the screen ring and, the brush being mode 7, not the texture points: on a back face the texture point of vertex *j* goes to the reversed ring's slot *j*. The colour table is 0 while `000973f4` is 0; otherwise number (`000973f4` − 1) · light / 255 of the tables at `000973f0`, the light being the face's. There is no atlas and no frame descriptor: ES2's uses a frame index into packed atlas pages and the `.RMP` row for its light ([`TSTexture4Poly`](../../retail/rendering/dts-texture-binding.md#tstexture4poly--frame-index-ramp-row-by-light-fullbright-on-demand)). Brush mode 7 draws every texel as its entry in the table, index 0 included, and with a table of 0 leaves index-0 texels unpainted ([bitmap fills](#bitmap-fills-brush-modes-5-and-7)).

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

## The lights

**A shape's faces are lit by one fixed sun, the same in every mission and theater, and by the flashes of impact effects near the shape; there is no ambient light.** A face's light sums the lights in `Light_ActiveList` (`000bdba6`) while it draws. `Light_Register` (`0005ba84`) has four callers, ES2's four ([ES2's effect lights](../../retail/rendering/effect-lights.md)): the mission's sun, `Light_ReregisterPersistent`, and the effect light manager's two getters.

### The sun

`Light_CreateMissionSun` (`0005a888`), which `Sim_StartMission` calls once (`0005ad1f`), after its call of `Light_ResetSystem` (`0005abb3`) has emptied the list, is ES2's ([ES2's sun](../../retail/rendering/terrain-lighting.md#the-sun)) with other constants:

1. it allocates a `0x34`-byte light, `TSLight`'s vtable `000972b6` and then `TSSunLight`'s `000972ca` at `+0x12`, type 1 at `+0x16`, and sets its intensity `+0x1a` to `0x100` through the vtable's `+8`, `maybe_TSLight_SetIntensity`;
2. it stores at `+0xc`, three `int16`, the direction `RotateVectorByMatrixQ14((0, 0x800, 0), BuildEulerRotationMatrixQ14(−8000, 0, 24000))`, where ES2's rotates (0, `0x1000`, 0) by (−6000, 0, 21000);
3. it calls `Light_Register` and `Light_AddPersistent` (`00005608`), which appends the light to the list at `000bc1a0`, its `int16` count at `000907f2`, with no bound.

Rotating (0, *L*, 0) takes the matrix's middle column alone: (*L* · *m* + `0x2000`) >> 14 of each of *m*₂ = −cos X · sin Z, *m*₃ = cos X · cos Z and *m*₅ = sin X, the matrix's entries in Q14 from the quarter-wave table at `000981cc`. X = −8000 is −43.9° and Z = 24000 is 131.8°, so the entries are −8789, −7869 and −11370 and the direction is (−1099, −984, −1421): (−0.537, −0.480, −0.694) at length 2048 in the Z-up world, the way the light travels. It falls at 44° from the vertical, where ES2's falls at 57°. No mission, world or palette file enters it.

### A face's light

`Light_ComputeShadeForFace` (`0005bc30`) adds, for a directional light, −(intensity · *v*) >> 22 when *v* = (normal · direction − `0x400000`) >> 1 is negative, and clamps the sum to 255 ([ES2's terms](../../retail/rendering/dts-texture-binding.md#two-shade-calculations--terrain-and-shapes-use-different-ones)); `DotProductVec3Short` (`000660bf`) is the plain sum of the three `int16` products. The direction it reads is the light's model-space copy `+0x2a`, which `Raster_SetModelTransform` rebuilds for each shape it draws ([`view-transform-and-projection.md`](view-transform-and-projection.md)), so a shape's heading turns its faces against the sun. With the sun alone:

```
dot   = normal · (−1099, −984, −1421)          in world axes
v     = (dot − 0x400000) >> 1
shade = min(255, −((0x100 · v) >> 22))        the shifts are arithmetic, so the shade rounds up
```

A poly's normal is a point of its group's list ([normals](../../retail/rendering/dts-texture-binding.md#normals-live-in-the-point-list)), and in the four theater libraries every one is 2047 or 2048 long, `0x800` as ES2's are, except 32 to 69 per library that name the point (0, 0, 0), whose faces dot to 0 and take 128 whichever way they turn. For a normal of length `0x800`, dot is `0x400000` · cos, cos between the normal and the way the light travels, and the shade is 128 · (1 − cos) rounded up: 128 + 128 · *facing* with *facing* = −cos. A face turned straight away from the sun is 0, one edge-on to it 128, one pointing straight up 217, and only a face within about 10° of facing the sun reaches 255. ES2's sun, twice as long, gives 128 + 256 · *facing*, at 255 for every face within 60° of the light.

### Effect lights

ES1's effect light manager is ES2's ([the manager](../../retail/rendering/effect-lights.md#the-manager--dat_004a968c)), with the same falloff and selection, the singleton at `000bc19c`:

- **The set-up.** `LightManager_RegisterSubsystem` (`000056f0`) registers `LightManager_InitSubsystem` (`000055b8`) at phase 2 and `EffectLightPool_FlushDeletes` (`000055f4`) at phase 5 with `RegisterSubsystemLoader`. The init builds the manager with `LightManager_Construct` (`00004bb8`), whose own `LightManager_SetFalloffConstants` (`00004da8`) call passes 3000 and 2000, calls the setter again with 2000 and 10, in stack order, so that A = 10 >> 5 = 0 and B = 2000 >> 5 = 62 as in ES2, stores 10000 at `+0xc`, and builds the handle pool `Pool_Init(0, 8, 3)` at `000aee18`: three handles of 8 bytes, where ES2's are `0xe`. `Sim_StartMission` also calls the flush (`0005acca`).
- **The lights it makes.** The manager's embedded light at `+0x328` is a `TSAmbientLight` (vtable `0009077a`, type 0); `Light_GetOrCreateDirectional` (`000052a4`) makes `TSSunLight`s (`0009078e`, type 1, `0x34` bytes) and `Light_GetOrCreatePoint` (`0000537c`) `TSOmniLight`s (`000907a2`, type 2, `0x40` bytes). A directional light's direction goes to `+0xc` and a point light's position to `+0`, the fields of ES1's [light layout](view-transform-and-projection.md#differences-from-es2).
- **Claiming a slot.** When the effect's `EXPLOS.DAT` row has a non-zero light mode, `Explosion_Construct` calls `EffectLight_Construct` (`000054b0`) with the row's `int16` at `+0x0c`, the first step of its intensity ramp ([`../formats/explos-dat.md`](../formats/explos-dat.md#construction)), and `00005d74` passes `EffectLight_SetIntensity` (`00005578`) the low byte of an entry of that ramp.
- **Per object.** `ObjList_DrawFrameObjects` stores the camera's position in the manager (`LightManager_SetCameraPosition`, `00004f60`), where ES2's `Sim_RenderFrame` does, and [`ObjList_DrawEntryRender`](#the-frames-draw) calls `LightManager_SelectLightsForObject` (`00004f78`) before each object, with the entry's position and radius; it measures each slot's distance with `Point3I_DistanceBetween` (`00064698`), `Math_FastMagnitude3D` of the difference.

A zone shape is drawn through the object list ([below](#which-objects-are-drawn)), so it takes the sun and any effect light within a slot's cull radius of it.

## Which objects are drawn

**Each frame the simulator draws the zone's shapes and every live machine, effect and projectile from one list, farthest first, recolouring each by its distance and leaving out what lies past the theater's last distance band.** An object's type tag is its `int16` at `+0`, its vptr at `+2`; its vtable's `+4` slot draws it, `+8` returns its position (three `int32`), and `+0x14` returns the radius the bands read.

### The object list

`ObjList_Init` (`00020c2c`), which `terrain.cpp`'s static initialiser registers for subsystem phase 2 ([the object list](../simulation/terrain.md#the-object-list)), allocates the list's arrays, 512 entries each: object pointers at `000bc58e` and `000bc592` (`0x800` bytes each), `0x36`-byte draw entries at `000bc598` built by `ObjListEntry_Ctor` (`00020c80`, which stores the vptr `0009255e` at `+0x14`), entry pointers at `000bc59c`, and through `ObjList_AllocSortNodes` (`00021560`) 512 sort nodes of `0x1d` bytes at `00092570`.

- **`ObjList_Add` (`00020d5c`)** stores an object at entry `00092558` of `000bc58e` and counts it, with no bound.
- **The mission start (`Sim_StartMission`, `0005a9dc`)**, after `DBSim_LoadScriptDat`, adds every zone shape object in `Zone_ShapeObjects`' order, stopping at the first null pointer ([the simulator's load](../formats/zone-terrain.md#the-simulators-load)), and once its other set-up is done stores the count at `000bc596`. The objects below that count, the zone's shapes, stay in the list for the mission.
- **`ObjList_RebuildFrame` (`00020de0`)**, which `Sim_RenderFrame` calls each frame, sets the count back to `000bc596` and adds, in turn, each object of the array at `000bc416` (count `000bc41c`) whose group (`+0x3f`, [`Mech_AttachToGroup`](../simulation/mech.md#construction)) has 0 at `+0x16`, no action holding it out ([the group record](../simulation/mission-groups.md#the-group-record)), then every element of the pools at `000af0b4`, `g_ExplosionPool`, `000aeef8` (150 of `0x4e` bytes, which [`Debris_LoadResources`](../formats/debris-dat.md#the-load) builds) and `000aef68` (200 of `0x46` bytes), each walked back from its end with `Pool_Prev`.

`ObjList_Remove` (`00020d84`) swaps an object out for the list's last; `es2_xref.py` finds no reference to it.

### The frame's draw

`ObjList_DrawFrameObjects` (`00021008`), given a blend byte and the camera, draws a frame's objects. `Sim_RenderFrame` passes the byte at `000b5b65`, `000364f8` passes 0. It:

1. stores the camera's position in the [light manager](#effect-lights) with `LightManager_SetCameraPosition` (`00004f60`);
2. calls slot `+4` of every element of the ground-shape pool `000af044` ([the ground-shape set](../formats/wld-world.md#the-ground-shape-set)) within 30000 of the camera by `Math_DistanceBetweenPoints`, walking it back from the end with `Pool_Prev`;
3. stores the blend byte at `000afa98`;
4. for each of the `00092558` objects in the list, but the one the camera rides in (`Camera_IsMountedOn`, `000003ac`: the camera's `+0x36` is 2 and its `+0x32` is the object) and one whose position's z (slot `+8`, `+8`) is negative, fills the next `0x36`-byte entry: the position at `+0`, the radius (slot `+0x14`) as an `int16` at `+0xc`, the distance from the camera at `+0xe`, the object at `+0x32`, and 0 at `+0x12`. For an object below `000bc596`, a zone shape, whose [dividing line](../formats/zone-terrain.md#dattheater-the-shapes-dividing-lines) `ZoneShapeObj_WorldSegment` (`00020738`) returns, it stores 2 at `+0x12` and the line's two ends, 24 bytes, at `+0x18` and `+0x24`;
5. keeps the entry, in the list at `000bc59c`, only when `WorldShades_IsBeyondLastBand` (`00025f38`) is false for its distance;
6. stores the camera at `0009256c` (`ObjList_SetSortEye`, `00021554`) and calls `ObjList_SortAndDraw` (`00021d04`) with the count and the list.

With a pixel pick pending it also marks the pick's pixel before step 6 and restores it after, and with the occlusion probe armed it tests the probe's corners last ([the pick and the probe](../simulation/terrain.md#the-pixel-pick-and-the-occlusion-probe)).

`ObjList_SortAndDraw` does nothing for a count of 0. Otherwise it builds a binary tree in the sort nodes (`{left, right, entry, byte}`), first inserting the entries with 1 or 2 at `+0x12` in list order (`ObjList_TreeInsertSegments`, `00021bec`), then the rest (`ObjList_TreeInsertRest`, `00021c60`), each through `ObjList_TreeInsert` (`00021ba8`) from the root, which goes right when `ObjList_EntryCompare` (`00021b20`) returns non-zero and left otherwise. It then draws the tree in order, left subtree, node, right subtree (`ObjList_TreeDraw`, `00021cd0`), calling slot `+4` of each entry's vptr `+0x14`, `ObjList_DrawEntryRender`. `ObjList_EntryCompare`, given the new entry and a node: between two entries of kind 0 it returns whether the new entry is nearer, so nearer entries go right and are drawn later; between two of kind 1 or 2 it is `ObjList_SortCompareSegments`' (`00021a4c`), and otherwise `ObjList_SortCompareSegment`'s (`000216c4`), [below](#objects-beside-a-dividing-line).

#### Objects beside a dividing line

**An object close to a zone shape that has a dividing line is drawn after the shape when it stands on the camera's side of the line, and before the shape when it does not; objects farther off are drawn farthest first.** The comparisons are ES2's ([the draw order within a cell](../../retail/rendering/terrain-drawing.md#the-draw-order-within-a-cell)), with these differences:

- ES1 sorts the frame's whole list in one tree, where ES2 sorts each terrain cell's objects.
- The entry's distance and kind are at `+0xe` and `+0x12`, where ES2's are at `+0x12` and `+0x16`; the line's ends are at `+0x18` and `+0x24` in both. A zone shape's entry is kind 2.
- The view's position is the point `0009256c` holds, where ES2's is `0049ac3c` + 4.
- ES1's `ObjList_SortCompareSegment` takes a first argument it does not read; `ObjList_EntryCompare` passes 1 and `ObjList_SortCompareSegments` 0.

ES1's addresses are in [Functions](#functions): `ObjList_PointNearSegment`, `ObjList_SortCompareSegment`, `ObjList_SegmentEndpointNear`, `ObjList_SortCompareSegments`, `Vec3I_DotSign` and `Vec3I_Cross`.

`ObjList_DrawEntryRender` (`00020f40`), whose address `00092562` holds, draws one entry: `LightManager_SelectLightsForObject` (`00004f78`) with the manager `000bc19c` and the entry's position and `+0xc` radius as four `int32`, `WorldShades_ApplyForObject` with the blend byte, the distance and the object, the object's slot `+4`, then, when the blend byte is set, `Light_ReregisterPersistent` (`00005690`), which passes each light of `Light_AddPersistent`'s list to `Light_Register` again; `Light_Register` refuses a light already in the list. Two later steps serve the pixel pick and the occlusion probe ([the pick and the probe](../simulation/terrain.md#the-pixel-pick-and-the-occlusion-probe)). ES2's (`ObjList_DrawEntryRender`, `0042876c`) also sets the depth fade from the entry's distance; ES1's sets none.

### The distance bands

The bands are ES2's ([distance colour bands](../../retail/rendering/distance-fog-and-sky.md#distance-colour-bands--worldshades_applyforobject-0042e8e8)), from the theater's world file ([the layout](../formats/wld-world.md#the-layout)) and `dat\shades` (`World_LoadShades`):

- **`WorldShades_DistanceBand` (`00025cb4`)** counts the thresholds the distance has reached, unsigned, at most one fewer than there are, from `WorldShades_BandsTagged` (`000afdc4`, count `000bc5b8`) for a non-zero tag and `WorldShades_BandsTag0` (`000afdc8`, count `000bc5ba`) for tag 0, a tag-5 object first adding `WorldShades_DistanceOffsets` (`000bc5c6`) entry 0, or entry 1 when its radius is 5000 or more, clamped at 0.
- **`WorldShades_ApplyForObject` (`00025d44`)**, given the blend byte, the distance (clamped at 0) and the object, re-points each `{ramp, column}` pair of the tag's `SHADES.DAT` list at range (column × rows + band) of `WorldShades_LevelRanges` (`000afdbc`), rows being `000bc5be`, with `Palette_SetRampsToRange` (`00028044`). With the blend byte set it passes `Palette_SetRampsToRangeList` (`000280e4`, [the palette module](palette.md#differences-from-es2)) a list of two ranges instead: the band's range less its last *q* + 1 indices, *q* being its length >> 2, then *q* + 1 indices from (start + length − *q*) of the band's `WorldShades_BlendRanges` (`000afdc0`) range, the two range pointers in a two-entry array. While `000928ea` is set it runs `WorldShades_ResetRamps` instead.
- **`WorldShades_ResetRamps` (`00025e88`)** points ramps 0, 2, 4, 6, 8, 10, 12 and 14 at the range {16, 16}; ES2's last is ramp 24. `000364f8` sets `000928ea` around its call of `ObjList_DrawFrameObjects` and clears it after.
- **`WorldShades_IsBeyondLastBand` (`00025f38`)**, given the distance and the object, takes the same table and offsets, a tag-8 object adding offset entry 0, and is true when the distance exceeds the table's last threshold, unsigned. It is the cull above.

`Palette_SetRampsToRange` (`00028044`), given the range, the count and the first ramp, in stack order, is ES2's: it writes the range's consecutive indices into each record of `g_ShadeRamps` (`00092c34`) from the first, and its length, then copies the records into the raster's table with `Raster_SetColourRamps`.

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

- **`MfdMapScreen_Paint` (`00036c2c`)**, the MFD's NAV MAP, fills its view with colour `0x17`, puts its camera 200000 above the active view object, calls `WorldShades_ResetRamps`, draws every zone shape, then plots a cross with `Gfx_PlotPixel` ([the NAV MAP](../simulation/mfd-nav-map.md#the-paint)).
- **`00040d4c`**, a cockpit map, fills with colour `0x36`, draws grid lines in `0x37` every 50000 units, points ramp 14 at the one-entry range {56, 1} with `Palette_SetRampsToRange`, so every lit face is palette entry 56, and draws every zone shape with the word its shape instance's `+8` points at set to 1 for the draw and 0 after.

## Functions

| ES1 | Address | Notes |
|---|---|---|
| `Camera_IsMountedOn` | `000003ac` | |
| `LightManager_Construct`, `TSLight_DtorCopy`, `TSAmbientLight_Dtor` | `00004bb8`, `00004d30`, `00004d68` | the light destructors are slot `+4` of their vtables, `00090766`, `0009077a`, `0009078e`, `000907a2`, `000972b6` and `000972ca` |
| `LightManager_SetFalloffConstants`, `LightManager_SetSlotFalloff`, `LightManager_ClaimSlot`, `LightManager_ReleaseSlot` | `00004da8`, `00004dc8`, `00004dfc`, `00004e84` | |
| `LightManager_SetSlotPosition`, `LightManager_SetSlotIntensity`, `LightManager_SetCameraPosition`, `LightManager_SelectLightsForObject` | `00004f14`, `00004f34`, `00004f60`, `00004f78` | |
| `LightManager_RecomputeCullRadius`, `Light_GetOrCreateDirectional`, `TSSunLight_DtorCopy` | `00005274`, `000052a4`, `0000533c` | |
| `Light_GetOrCreatePoint`, `TSOmniLight_Dtor`, `LightManager_RecycleLight` | `0000537c`, `0000540c`, `0000544c` | |
| `EffectLight_Construct`, `EffectLight_Destruct`, `EffectLight_SetIntensity`, `EffectLight_SetPosition` | `000054b0`, `00005534`, `00005578`, `00005598` | `EffectLight_Destruct` is slot `+0xc` of the handle's vtable `000907d6` |
| `LightManager_InitSubsystem`, `EffectLightPool_FlushDeletes` | `000055b8`, `000055f4` | |
| `Light_AddPersistent`, `Light_RemovePersistent`, `Light_ReregisterPersistent`, `Light_UnregisterPersistent` | `00005608`, `00005628`, `00005690`, `000056c0` | |
| `LightManager_RegisterSubsystem`, `LightManager_StaticDtor` | `000056f0`, `0000571c` | the static initialiser and its exit routine |
| `ZoneShapeObj_WorldSegment` | `00020738` | |
| `ObjList_Init`, `ObjListEntry_Ctor` | `00020c2c`, `00020c80` | `ObjList_Init` passes `__vec_new` the padding entry `00020c7f` before the constructor |
| `ObjList_Add`, `ObjList_Remove`, `ObjList_RebuildFrame` | `00020d5c`, `00020d84`, `00020de0` | |
| `ObjList_DrawEntryRender`, `ObjList_DrawFrameObjects` | `00020f40`, `00021008` | |
| `ObjList_SetSortEye`, `ObjList_AllocSortNodes` | `00021554`, `00021560` | |
| `ObjList_PointNearSegment`, `ObjList_SortCompareSegment` | `000215e0`, `000216c4` | |
| `ObjList_SegmentEndpointNear`, `ObjList_SortCompareSegments` | `000219fc`, `00021a4c` | |
| `ObjList_EntryCompare`, `ObjList_TreeInsert`, `ObjList_TreeInsertSegments` | `00021b20`, `00021ba8`, `00021bec` | |
| `ObjList_TreeInsertRest`, `ObjList_TreeDraw`, `ObjList_SortAndDraw` | `00021c60`, `00021cd0`, `00021d04` | |
| `World_LoadShades` | `00025bc8` | |
| `WorldShades_DistanceBand`, `WorldShades_ApplyForObject` | `00025cb4`, `00025d44` | the object last, as with every callee-cleaned function ([calling conventions](../formats/compiler.md#calling-conventions)) |
| `WorldShades_ResetRamps`, `WorldShades_IsBeyondLastBand` | `00025e88`, `00025f38` | |
| `Palette_SetRampsToRange` | `00028044` | |
| `Palette_UnpackColour`, `Palette_BuildTextureShadeTables` | `00028afc`, `00028b28` | |
| `VGA8_FillBitmapPolygon`, `VGA8_BitmapPolyPointCtor`, `VGA8_BitmapPolyClipTop` | `000534ac`, `00053864`, `00053880` | `VGA8_BitmapPolyPointCtor` is the element constructor of the clip buffers |
| `VGA8_BitmapEdgeStepRows`, `VGA8_BitmapEdgeStepColumns`, `VGA8_BitmapEdgeSetupRows`, `VGA8_BitmapEdgeSetupColumns` | `000539e4`, `00053a3c`, `00053a94`, `00053c18` | the row pair is ES2's `Raster_BitmapEdgeStep` and `Raster_BitmapEdgeSetup` (DBSIM `0048a944`, `0048a9a0`) |
| `TSSolidPoly_Render`, `TSTexture4Poly_Render` | `0005403c`, `0005410c` | |
| `TSShadedPoly_Render`, `TSGouraudPoly_Render` | `000543e4`, `00054578` | |
| `TSNullObj_Render`, `TSPoly_Render`, `TSTexture4Poly_PointElementCtor` | `00054034`, `00054038`, `000543c8` | the first two with no Ghidra function; the last the element constructor of `TSTexture4Poly_Render`'s point buffers, `VGA8_BitmapPolyPointCtor`'s code |
| `TSGroup_RenderPolys`, `TSBSPGroup_Ctor`, `TSBSPGroup_Dtor`, `TSBSPGroup_RenderNode`, `TSBSPGroup_Render` | `00054928`, `00054a34`, `00054a6c`, `00054aa4`, `00054bbc` | ES2's `004758c8`, `004759b4`, `004759e0`, `00475a20`, `00475af8`; `ES1GO` `0007cd3c`–`0007cfd0` |
| `TSAliasSolidPoly_Render`, `TSAliasShadedPoly_Render`, `TSAliasGouraudPoly_Render` | `00054c9c`, `00054d6c`, `00054f00` | `ES1GO` has the first two at `0007d0b0` and `0007d180` |
| `Sim_Main`, `Light_CreateMissionSun`, `TSLight_Dtor`, `TSSunLight_Dtor` | `00059350`, `0005a888`, `0005a964`, `0005a99c` | |
| `Sim_CreateDisplay`, `Sim_StartMission` | `0005a5f4`, `0005a9dc` | |
| `Point3I_DistanceBetween`, `Colour_Distance` | `00064698`, `000644c8` | |
| `DotProductVec3Short`, `DotProductVec3Int` | `000660bf`, `000660f2` | the light terms' dot products, for the `int16` normal and direction and for the `int32` displacement |
| `Vec3I_DotSign`, `Vec3I_Cross` | `000661a5`, `000662a5` | |
| `Raster_SetColourRamps`, `Raster_GetColourRamps`, `Palette_UploadWhole` | `000691f8`, `00069227`, `0006931c` | |
| `GLDisplay_Ctor`, `GLDisplay_CtorAndOpen`, `GLDisplay_Open`, `Raster_SelectRenderTarget` | `0006a3a8`, `0006a3fc`, `0006a480`, `0006a644` | ES2's (DBSIM `00480e4c`, `00480e98`, `00480f1c`, `00481118`), the constructor inlined in both of ES1's |
| `Raster_DriverByName`, `Raster_BlitDispatch` | `0006a714`, `0006a8e0` | `Raster_BlitDispatch`: [the skyline's blit](ground-and-sky.md#the-horizon-skyline) |
| `Raster_FindDriverTable`, `Raster_InstallDriverTable`, `RasterDriver_OpenDisplay` | `00073234`, `0007324e`, `0007330a` | `RasterDriver_OpenDisplay` is ES2's (DBSIM `0048930a`) |
| `VGA8_SpanBitmapRow`, `VGA8_SpanBitmapColumn` | `000735a4`, `0007394a` | |
| `Raster_InstallViewport`, `Raster_SetClipBlock`, `ClipBlock_Init` | `00075980`, `0007599c`, `000759b0` | ES2's (DBSIM `004893c0`, `0048944c`, `00489464`) |
| `GL_ClaimGlobal`, `GL_SetGlobal` | `0007d5bc`, `0007d5f8` | the GL object slots at `000bdd0c`; slot 7, `000bdd28`, is the current display |
| `VGA8_BlitBitmap`, `BMP8_BlitBitmap` | `0008b9a8`, `0008d700` | |
| `VGA8_BlitBitmapClipped`, `VGA8_BlitClippedRow` | `0008c27c`, `0008c41a` | `VGA8_BlitClippedRow` is a near-called block sharing the clipped blit's frame |
| `RotateVectorByMatrixQ14` | `0007586c` | |

## Open

- **Open:** what a `TSShadedPoly` with a flag of 0, whose fill reaches the span routine as a palette index with the light in bits 16–23, draws, and which flags the shapes outside the theater libraries (`dts\`, `dgs\` other than the four) carry on their shaded, solid and Gouraud faces.
- **Open:** the bitmap fills of `VGA_4` (`0008ab80`) and `BMP_8` (`0008e22c`, its edge functions `0008e3d0` and `0008e378`), what their spans do with index 0, and what `BMP_8`'s does with a mode-7 brush, whose table it does not pass; the simulator draws with neither ([the display drivers](#the-display-drivers)).
- **Open:** no reference found by `es2_xref.py` to `Raster_InstallDriverTable`'s other caller `00073285`, to `GLDisplay_CtorAndOpen`'s other caller `00069a68`, or to `GLDisplay_Ctor` (`0006a3a8`), so no other path to a display driver is known.
- **Open:** what `Sim_CreateDisplay`'s display field `+0x68` selects.
- **Open:** which entries carry kind 1.
- **Open:** what the blend byte `000b5b65` is set to.
- **Open:** which classes carry type tags 1 to 9 in ES1: what the array at `000bc416` and the pools `000af0b4`, `000aeef8` and `000aef68` hold, and what each one's constructor stores at `+0`.
- **Open:** what `00040d4c` is in the cockpit, and what the word at a shape instance's `+8` selects.
- **Open:** no reference to `Light_RemovePersistent` or `Light_UnregisterPersistent` found by `es2_xref.py`, and none to `LightManager_SetSlotFalloff` but the dword at `0002116c`.
- **Open:** what `00005d74`, which sets an effect light's intensity, is, and which ramp entry it reads.
- **Open:** whether a textured face is outlined. `PolyFill_FillThenOutline` draws the ring again in `PolyFill_LineColour` for any brush mode but 5 when that colour differs from the brush's `+4`, which for `TSTexture4Poly_Render`'s brush is the point count, and `TSTexture4Poly_Render` does not set the line colour, so it is what the poly drawn before it left. A retail check: a HERC's textured panel seen close, for a one-pixel edge in another colour.
- **Open:** no reference to `Raster_GetColourRamps` found by `es2_xref.py`.
