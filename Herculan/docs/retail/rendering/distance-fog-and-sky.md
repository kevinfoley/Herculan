# Distance fog and sky (DBSIM.EXE)

Addresses are DBSIM virtual addresses.

Two mechanisms that share one palette: the distance fade, and the backdrop the sky is painted as. In seven of the ten theaters the fade ends on exactly the colour the backdrop paints at and below the horizon.

## Visibility range

`Terrain_DrawCellQuad` installs it per cell:

```
Raster_SetVisibilityRange(grid[+0x10c] << grid[+0x108])      // 00467fdc -> DAT_004a08c4
```

`grid+0x10c` is the **view radius in cells** and `grid+0x108` the cell shift. [`terrain-texturing.md`](terrain-texturing.md#grid0x10c--the-lod--draw-radius-field) is the canonical account of the field, its writer and the rest of its consumers; the radius comes from the player's terrain-detail setting, so every range below is per setting, not per zone.

| Cell shift | Zones | Range at detail 0 / 1 / 2 |
|---|---|---|
| 12 | 1 | 147 m / 246 m / 344 m |
| 13 | 10 | 295 m / 492 m / 688 m |
| 14 | 26 | 590 m / 983 m / 1376 m |
| 15 | 2 | 590 m / 983 m / 1376 m |

Shift 15 is the only case the `>>` correction touches. All three table entries are even, so it halves them exactly and those two zones land on the same ranges as shift 14 — the correction exists to stop the largest cells reaching further, not to normalise the smaller ones, which it leaves short.

**The same radius is the far clip.** `Terrain_BuildDrawRegionQuad` (`0046d220`) builds the terrain draw region as a square of that half-width around the viewer, so the world ends exactly where the fade saturates — which is why the edge does not read as a clip.

## The fade — `Raster_SetDepthFadeFromDistance` (`00467fec`)

```
if (d >= range)   d = range;
if (d <= range/2) bias = 0;
else {
  t    = min(Q16Divide((d - range/2) * 2, range), 1.0)
  bias = Q16Multiply(t, depthSlices - 1) * shadeLevels * 256
}
```

`bias` is a whole number of 8192-byte slices, added by `Raster_ShadeRampRow` (`00468054`) to the row offset it reads `world<N>.rmp` at. So nothing inside **half** the range is fogged at all, the fade runs over the outer half only, and it is quantised to the ramp's 12 slices. This is what the file's 11 "unused" height slices are for — see [`dts-texture-binding.md`](dts-texture-binding.md).

**Fog is a ramp lookup, not a blend.** Every fogged pixel is still `world<N>.rmp[row + bias][index]`, so the fade is whatever that table does. Its slices average out close to a linear fade toward the fog colour, but they are not one: each palette index fogs at its own rate, and distinct colours stay distinct almost to the last slice.

`Raster_ShadeRampRow` is not the only reader of the bias. The textured fill `Raster_DrawPolygon` (`00468310`) computes `shade * (shadeLevels - 1) + bias` inline for the one ramp row its span mode 1 draws a poly with, and `Raster_SetupTexturedSpan` (`00468078`) the same for each vertex's row in span mode 2, so textured polys fog by exactly the same rule without calling `Raster_ShadeRampRow`. Those inline readers sit behind the span-mode test, so a mode-0 fill — the plain texture copy a fullbright poly takes, see [`dts-texture-binding.md`](dts-texture-binding.md#tstexture4poly--frame-index-ramp-row-by-light-fullbright-on-demand) — never spends it and does not fog.

**A `TSGouraudPoly` does not fog.** Its fill is brush mode 1 of `Raster_DrawPolygonDispatch` (`00483dac`) — a different mode 1 from `Raster_DrawPolygon`'s — which is `Raster_FillContourPolygon` (`004879c0`): it looks each corner's palette index up in the raster's ramp table and fills the bands between them through the dispatch's mode-0 brush, which writes each band's palette index as it is ([`dts-texture-binding.md`](dts-texture-binding.md#tsgouraudpoly--same-ramp-number-per-vertex-light-no-rmp-row)). Neither the bias nor a `.RMP` row enters that path. What changes such a face's colour with distance is the ramp table itself, which [the distance colour bands](#distance-colour-bands--worldshades_applyforobject-0042e8e8) re-point per object.

### The distance measured

The original's view space is **(across, depth, up)**: `Raster_PerspectiveDivide` (`0048c4f0`) divides components 0 and 2 by component **1** to project. Fog is measured against that depth, not against distance from the eye — radial distance is larger everywhere off the view axis, by `1/cos` of the angle off it, which reaches 18% at the corner of the view.

`Terrain_DrawCellQuad` passes the **minimum** of its four corners' depth, once for the whole cell. So a cell is fogged as if it were all at its leading edge, and the ground is systematically less fogged than its own depth says — by half the cell's depth extent, averaged over the cell. That is not a rounding detail: a shift-13 cell is 49 m across against a fade covering 344 m in twelve slices, so ignoring it costs a whole slice through the middle distance.

## What gets faded

`Raster_SetDepthFadeFromDistance` has exactly three callers:

| Caller | Argument |
|---|---|
| `Terrain_DrawCellQuad` (`0046d344`) | the cell's own distance |
| `ObjList_DrawEntryRender` (`0042876c`) | the drawn object's own distance, from its render entry `+0x12` |
| `TexPoly_DrawAtPoints` (`0042fa18`) | `0` |

The third does **not** reset anything drawn through `TSSolidPoly_Render`. It is slot `+0x34` of `TexPoly` (vtable `0049afe6`), a `TSTexture4Poly` subclass that draws one textured poly at each of a set of points around the viewer; [`../simulation/random-generator.md`](../simulation/random-generator.md#open) tracks whether anything builds one. The poly renderers the DTS type registry points at are the `00474xxx`/`00475xxx` family, whose group-level setup is `TSGroup_RenderPolys` (`004758c8`) / `TSBSPGroup_Render` (`00475af8`) — neither of which touches the bias.

### A projectile is faded like anything else

`ObjList_DrawEntryRender` (`0042876c`) is the render entry's vtable slot 0 (`ObjList_DrawEntryConstruct` (`00428e10`) stamps `PTR_FUN_0049ac38`), and it sets the fade on the line before it calls the object's own slot 0. A bullet reaches it:

1. `Scene_SubmitFrameObjects` (`0042841c`) walks the bullet pool `g_ProjectilePool` (`004a9746`) → `Scene_SubmitObject` (`004282d8`) → `ObjList_AddToDrawTable` (`004282f8`), which buckets the round into `ObjList::drawTable` by terrain cell.
2. The per-cell hook `ObjList_DrawCellObjects` (`00428c60`) branches on the object's type tag at `+4`. `Bullet_Construct` writes **3**, so it takes the deferred branch and gets a 0x36-byte render entry carrying its distance at `+0x12`. (Tag 9 is the immediate branch, drawn on the spot under the fade its cell's quad installed.)
3. `ObjList_DrawSorted` (`00429620`) → `ObjList_SortTreeDraw` (`004295f0`) walks those entries in sorted order and calls each entry's slot 0.

So **a flat solid face is not pinned to ramp row 15 at distance**; it fades from its own range like anything else drawn.

## The sky — `hzline`

The sky is a backdrop of flat palette fills, painted into the view every frame before any terrain. `Scene_DrawTerrainPass` (`0042e700`), which `Sim_RenderFrame` runs only for a view that shows the world ([`terrain-drawing.md`](terrain-drawing.md#the-frame)), calls `Hzline_Draw` (`0042ebe8`) on the theater's horizon object `g_TheaterHzline` (`0049aee0`) ahead of `Scene_DrawTerrain`, so the ground and everything on it paint over the backdrop. `Hzline_DrawWithOffset` (`0042ec08`) does the work in three steps: build the horizon line, band the sky above it, fill below it in one colour. Nothing in it is textured or fogged, and every pixel is a palette index, so the sky follows any palette swap.

### The object

`World_LoadTheater` (`0042e010`) builds it with `Hzline_Ctor` (`0042eb5c`) — Borland class `hzline`, `0x7a` bytes over `TSGroup` — and fills it from the first eight `int16`s of `wld\world<N>.wld` ([their retail values](../formats/wld-world.md#the-hzline-shorts)), through `Hzline_SetColors` (`0042ebbc`) for shorts 1, 4 and 7:

| Short | Field | Meaning |
|---|---|---|
| 0 | `+0x6c` | the first band past the horizon colour starts `+0x6c >> 1` rows above the line (`t` below) |
| 1 | `+0x28` | the zenith colour. `Hzline_SetColors` stores `short1 + short3 - 1`, the **horizon colour** `C` |
| 2 | `+0x54` | band height `h`, in screen rows at every resolution |
| 3 | `+0x58` | band count `N`, the horizon colour and the zenith fill included |
| 4 | `+0x2c` | first ground colour |
| 5 | `+0x5c` | ground band height |
| 6 | `+0x60` | ground band count |
| 7 | `+0x30` | the line's vertical offset in rows: `Hzline_BuildHorizon` adds it and `Hzline_DrawWithOffset` adds half of it again |

So the sky is the palette run from 208 up to `C` = 223 in `WORLD0`, `WORLD2` and `WORLD6`, and up to `C` = 222 in the other seven, which never draw entry 223. Shorts 4–6 reach only a branch no frame takes ([Below the line](#below-the-line)). `World_LoadTheater` also stores `short4 + short6 - 1` at `0049aee8` ([Open](#open)).

### The horizon line

`Hzline_DrawWithOffset` copies the view's pitch, negated, and its roll (`view+0x10`, `+0x12`) to `+0x34`/`+0x36`. `Hzline_BuildHorizon` (`0042ec68`) then writes the line's two screen ends to `+0x38`/`+0x3c` and `+0x40`/`+0x44`, with `f = 2^DAT_006c60ac` the focal length, `(cx, cy)` the projection centre (`DAT_006c60b8`/`bc`) and `k` the coordinate shift (`VideoMode_XCoordShift` across, `VideoMode_YCoordShift` down; 1 in the 640-wide mode):

```
mid  = (cx, cy) + f·tan(pitch) · (sin roll, cos roll)        screen axes, y down; cos(pitch) floored at 1
ends = mid ∓ (256 << k) · (cos roll, −sin roll)               ±512 px at 640x480
```

That is the vanishing line of level ground, with pitch positive looking up. The line is lengthened when the view's canvas origin (`DAT_004cfa24`/`28`, [`../simulation/cockpit-views.md`](../simulation/cockpit-views.md#view-geometry)) is non-zero, so it still spans a view whose projection centre sits outside its own window: by its own length at both ends when the origin's y is non-zero, otherwise at the end on the side its x points to.

`+0x48`/`+0x4c` hold `(sin roll, −cos roll)` in Q14, from which `Hzline_FillSky` steps `h·(−sin roll, −cos roll)` per band, toward the sky. The test that would negate the pair once the pitch passes vertical compares the unsigned angle with `−0x4000` and is never true.

### Above the line

`Hzline_FillSky` (`0042ee88`) installs the solid brush `{0, colour}` at the render context's `+0x22c` and paints `N − 1` bands outward from the line, the first in `C` and each next one palette entry lower, then the zenith colour over everything beyond the last.

- **Level** (`roll` 0): `Raster_FillRect` rects, band `i` (colour `C − i`) from row `H − t − i·h` down to the previous band's top row inclusive, with `H` the line's row. Each band's top row is painted over by the next, so with the retail `t` = 1 and `h` = 6 the horizon colour covers the line's own row and colour `C − i` covers rows `H − 6i` to `H − 6i + 5`. The zenith fill runs from the top of the view (context `+0x214`) to the last band, when the view reaches that high.
- **Rolled**: `Raster_DrawPolygonReversed` quads, each edge parallel to the line. Band `i`'s far edge is the line moved by `(−h·i·sin roll, −(t + (i − 1)·h)·cos roll)`: the vertical term counts one band fewer than the level fill's `t + i·h`, so the moment the view rolls at all the gradient drops toward the ground by `h − 1` rows, 5 in retail data (one band, less the row the level fill's overpainting takes back). The first quad starts `2h` below the line, inside what the ground fill then covers, and the zenith quad reaches 5000 px past the last band.

Both retail captures fit this. `Reference/Apocalypse_Cockpit.png` (lossless, `WORLD2`) steps through consecutive `WORLD2.DPL` entries every 6 rows, 209 on rows 107–112 up to 219 on rows 167–172, which puts its line on row 191; `Reference/Simulator5_Preferences.jpg` (`WORLD0`) runs orange `#985C20` (208) to olive `#747060` (223) at the horizon.

### Below the line

`Hzline_DrawWithOffset` moves the line down one row, and `Hzline_FillGround` (`0042f0b0`) fills from it to the bottom of the view (context `+0x21c`). `Scene_DrawTerrainPass` sets the line's `+0x71` immediately before its call — only `Terrain_ProjectFarEdgeAhead` runs in between, writing `+0x72` on — so the branch taken is always the one-colour fill: colour `C`, through `Raster_FillRect` when level, otherwise a polygon of the line and the view-rect corners on its lower side through `Raster_DrawPolygonDispatch`. The terrain paints over it, and what stays visible is the strip between the terrain's far edge and the horizon.

With `+0x71` clear the routine would paint `+0x60` bands of `+0x5c` rows downward from colour `+0x2c + +0x60 − 1`. That is the only use of shorts 4–6.

**`Terrain_ProjectFarEdgeAhead`'s point does not reach the frame.** It is written to `+0x72`/`+0x76` ([`terrain-texturing.md`](terrain-texturing.md#grid0x10c--the-lod--draw-radius-field)), and only the rolled branch reads it: it moves the line by the offset from the line's midpoint to the point and stores the moved ends as polygon vertices 2 and 3, then on every path overwrites vertex 2 with a view-rect corner and either overwrites vertex 3 too or draws three vertices. The level branch does not read it.

## Where the two meet

The backdrop paints `C` on and below the horizon, so terrain at the edge of the visibility range is drawn against `C`. A theater's fog colour — the commonest output of `world<N>.rmp`'s last depth slice at the unlit shade — is `C`'s own colour in seven theaters, and palette entry 221, a step darker, in theaters 2, 4 and 6:

| | 0 | 1 | 2 | 3 | 4 | 5 | 6 | 7 | 8 | 9 |
|---|---|---|---|---|---|---|---|---|---|---|
| `C` | 223 | 222 | 223 | 222 | 222 | 222 | 223 | 222 | 222 | 222 |
| `C`'s colour | `#747060` | `#1C1818` | `#FCDCBC` | `#242828` | `#DC0400` | `#101010` | `#F4F4FC` | `#0C3058` | `#040404` | `#040404` |
| Fog | `#747060` | `#1C1818` | `#F4D4BC` | `#242828` | `#D80400` | `#101010` | `#ECF0FC` | `#0C3058` | `#040404` | `#040404` |

Where they match, fully fogged terrain meets the backdrop without a seam.

## Distance colour bands — `WorldShades_ApplyForObject` (`0042e8e8`)

Apart from the fade, an object's shade ramps are re-pointed by its distance. `ObjList_DrawEntryRender` calls `WorldShades_ApplyForObject` (call at `004287b3`) before each deferred object's draw, and `Terrain_FillCellUntextured` (call at `0046bc6a`) before an untextured cell's.

`World_LoadShades` (`0042e760`) loads `dat\shades` into `WorldShades_TypeTable` (`004cfd64`): per object type tag (`obj+4`), a list of `{ramp, column}` byte pairs. A tag with no list is left alone. Retail `SHADES.DAT`:

| Tag | Ramps re-pointed | From columns |
|---|---|---|
| 1, 5, 7 | 0, 2, 4, 6, 8, 12 | 0, 1, 2, 3, 4, 6 |
| 3, a projectile ([above](#a-projectile-is-faded-like-anything-else)) | 0, 12 | 0, 6 |
| 6 | 8, 12 | 4, 6 |

1. `WorldShades_DistanceBand` (`0042e878`) counts the thresholds the distance has reached, at most one fewer than there are: `WorldShades_BandsTagged` (`004cfd84`) for a non-zero tag, `WorldShades_BandsTag0` (`004cfd88`) for tag 0. A tag-5 object first has one of the two `WorldShades_DistanceOffsets` (`004cfd6c`) added — the second when its radius (vtable `+0x10`) is 5000 or more — clamped at 0.
2. For each pair, `Palette_SetRampsToRange` re-points the ramp at the `(column, band)` entry of `WorldShades_LevelRanges` (`004cfd7c`), the index ranges `World_LoadTheater` expands from the `.WLD`'s two ramp tables ([`../formats/wld-world.md`](../formats/wld-world.md#the-worldn-descriptor--layout)).

Both threshold sets are read from the theater's `.WLD`; their retail values are in [`../formats/wld-world.md`](../formats/wld-world.md#the-worldn-descriptor--layout).

With its third argument non-zero — `ObjList_DrawEntryRender` passes the byte at `004cfa08` — step 2 instead builds a two-segment range list for `Palette_SetRampsToRangeList` from the band's range and the band's entry of `WorldShades_BlendRanges` (`004cfd80`) ([Open](#open)). While `0049aef2` is set (0 in the image) the whole step is replaced by `WorldShades_ResetRamps` (`0042ea0c`), which points ramps 0, 2, 4, 6, 8, 10, 12 and 24 at the range `{16, 16}`.

## Rejected readings

| Reading | Why it is wrong |
|---|---|
| Fog is a blend toward the fog colour, so the ramp slices can be replaced by a linear fade of the same mean strength | The slices fog each palette index at its own rate and keep distinct colours apart almost to the last one. A uniform blend fades a whole surface evenly and washes distant terrain into featureless pastel. A `TSGouraudPoly`, whose fill reads no `.RMP` row, is not faded at all ([above](#the-fade--raster_setdepthfadefromdistance-00467fec)) |
| Fog is measured against distance from the eye | It is measured against view-space **depth**, component 1 of `Raster_PerspectiveDivide`'s input. Radial distance is larger everywhere off the view axis, by 18% at the corner of the view |
| The game draws past the visibility range, so the far clip is a separate free parameter | `Terrain_BuildDrawRegionQuad` (`0046d220`) makes the draw region that same radius: the world ends exactly where the fade saturates |
| The fill below the horizon stops at `Terrain_ProjectFarEdgeAhead`'s point, the projected far edge of the terrain, which `Scene_DrawTerrainPass` computes every frame for it | The rolled branch of `Hzline_FillGround` puts the moved line into polygon vertices it then overwrites or leaves past the vertex count, and the level branch never reads the point. The fill always runs from the horizon line to the bottom of the view |
| The `.WLD`'s ground fields (shorts 4–6) colour the ground below the horizon | They feed only the branch `+0x71` clear selects, and `Scene_DrawTerrainPass` sets `+0x71` before every draw. Below the line is the sky's own horizon colour `C` |

## Open

- **Unported:** [the distance colour bands](#distance-colour-bands--worldshades_applyforobject-0042e8e8).
- **Open:** which classes carry type tags 1, 5 and 6.
- **Deferred:** whether `004cfa08` (copied from `004d2549`) is ever non-zero. With it set, `WorldShades_ApplyForObject` hands `Palette_SetRampsToRangeList` a descriptor whose segment pointers are one per segment (`0042e9ca`/`0042e9cd`) while the callee reads a ramp's segments contiguously from the first pointer, so its second segment would be that pointer's own bytes.
- **Deferred:** what `DAT_0049aee4` holds. `Scene_DrawTerrainPass` calls its slot `+0x34` with the view, after the backdrop, when it is non-null; it is zero in the image and `es2_xref.py` finds no reference besides that read.
- **Deferred:** what reads `0049aee8`, which `World_LoadTheater` sets to `short4 + short6 - 1`; `es2_xref.py` finds only that store.
- **Deferred:** whether anything draws a horizon strip. `Hzline_BlitHorizonStrip` (`0042f398`) blits a bitmap set's frames end to end along the horizon line, scrolled by the view's heading and rotated with its roll, and `Hzline_LoadBitmapBank` (`0042f65c`) loads a bank from the `dba` folder into `+0x50`. `es2_xref.py` finds no reference to either, and `World_LoadTheater` reads the `.WLD`'s second string, `clouds2`, into a buffer the next string overwrites.
