# Terrain drawing — the visible region and the cell walk (DBSIM.EXE)

Addresses are DBSIM virtual addresses.

Which terrain cells a frame draws, in what order, and how the objects standing on them are painted in between. There is no depth buffer: the ground and everything on it reach the screen in painter's order, a cell at a time. How one cell is textured and lit is [`terrain-texturing.md`](terrain-texturing.md) and [`terrain-lighting.md`](terrain-lighting.md); how it fogs is [`distance-fog-and-sky.md`](distance-fog-and-sky.md).

## The frame

```
Sim_RenderFrame (0045fb9c)
 ├─ Terrain_SetupVisibleRegion (0046ca98)        ← the visible region
 ├─ Scene_SubmitFrameObjects (0042841c)          ← every object filed under a cell
 └─ when CockpitView_ShowsWorld (0042db18) is nonzero:
     ├─ Terrain_SetupVisibleRegion (0046ca98)    ← again, same view
     ├─ Scene_DrawTerrainPass (0042e700)
     │   ├─ Terrain_ProjectFarEdgeAhead (00470910)
     │   ├─ Hzline_Draw (0042ebe8)               ← the sky backdrop, distance-fog-and-sky.md
     │   └─ Scene_DrawTerrain (00428140)
     │       └─ Terrain_DrawVisibleCells (0046d0a4)
     │           └─ the cell walk → Terrain_DrawCellQuad (0046d344) per cell
     │                              └─ Terrain_DrawCellObjects (0046e4a0) → ObjList_DrawCellObjects (00428c60)
     └─ ObjList_DrawAfterTerrain (0042883c)
```

`CockpitView_ShowsWorld` is 0 when the current view's `.VUE` rect has no height and no view transition is running — the heads-down view on every HERC but RAZOR ([`cockpit-views.md`](cockpit-views.md#vue--per-view-geometry)) — so that view draws no ground, though its objects are still submitted.

## The visible region — `Terrain_SetupVisibleRegion` (`0046ca98`)

It takes the height grid and the view object. First it copies the view's position into `DAT_006b4fc8`/`cc`/`d0`: the viewer every terrain face test and `HeightGrid_PickDrawCell` measure from. Then it writes the draw radius `grid+0x10c` ([`terrain-texturing.md`](terrain-texturing.md#grid0x10c--the-lod--draw-radius-field)) and builds the region:

1. `Terrain_BuildDrawRegionQuad` (`0046d220`): the square of half-width `F = grid+0x10c << cellShift` round the viewer, clamped to the grid, at the grid's base height.
2. `ViewFrustum_Ctor` (`00494898`) and `ViewFrustum_Build` (`004948d8`): six planes from the view, with a margin `M` of one and a half cells (`(1 << cellShift) + (1 << (cellShift - 1))`).
3. `ViewFrustum_ClipGroundPolygon` (`00494d4c`): the square cut by those planes.
4. The result's points, shifted down by the cell shift, go to `grid+0x28` (pointer `grid+0xcc`, count `grid+0xc8`), and `grid+0x11c` says whether anything was left. When nothing is, the flag is 0 and the previous polygon stays where it was.

Before building, it installs at `grid+0x1c` the grid origin in view space, and at `grid+4` and `grid+0x10` the view-space step of one cell along X and along Y. From these the walk's run callbacks build each cell's corners without transforming a point per cell.

### The planes

`ViewFrustum_Build` reads the view's position (`+4`), its euler triple (`+0x10`, pitch, roll, heading), its perspective shift `s` (`+0x1a`, so the focal length is `f = 2^s` pixels) and, through its render context (`+0x16`), the view rect at `+0x210` and the pair at `+0x220` whose negation is the projection centre `(cx, cy)` within the rect ([`cockpit-views.md`](cockpit-views.md#the-projection-centre-is-not-the-middle-of-the-view)). With `w`, `h` the rect's size, each plane is built in view axes (across, depth, up), rotated by the view's `BuildEulerRotationMatrixQ14` matrix, and handed to `Plane_FromPointNormal` (`0047e344`):

| Plane | `+` | Through | Normal |
|---|---|---|---|
| near | `0x18` | `M` behind the eye | `(0, F, 0)` |
| far | `0x28` | `F` ahead | `−(0, F, 0)` |
| top | `0x38` | `M` above | `(0, cy·F >> s, −F)` |
| bottom | `0x48` | `M` below | `(0, (h − cy)·F >> s, F)` |
| left | `0x58` | `M` to the left | `(F, cx·F >> s, 0)` |
| right | `0x68` | `M` to the right | `(−F, (w − cx)·F >> s, 0)` |

`Plane_FromPointNormal` scales the normal to length `0x800`, each component through a 64-bit multiply and divide by the normal's magnitude: `Point3I_ExactMagnitude` (`0047e958`, an integer square root `Math_ISqrt` (`0047de90`) of the summed squares) when every component is strictly inside ±15001, and `Math_FastMagnitude3D` otherwise. The plane's distance is `−(point · normal)`.

The four side planes pass `M` outside the eye rather than through it, and the near plane sits `M` behind it, so the region is the view's frustum loosened by a cell and a half on every side.

### The clip

`ViewFrustum_ClipGroundPolygon(frustum, polygon, lowZ, highZ, out)` is called with the grid's `+0x110` and `+0x114`, the lowest and highest heights its ground can take ([`terrain-heightmap.md`](terrain-heightmap.md#the-heightgrid-struct)). It copies the six planes and shifts every one that is not vertical (`Plane_IsVertical` (`0049512a`): no Z component and not all zero) along Z with `Plane_ShiftAlongZ` (`00495148`, `d −= v · nz`): by `lowZ` when its Z component is 0 or less and by `−highZ` when it is positive. Each plane is then tested at whichever of the two heights passes it more easily, so a point of the ground square is kept when some height the zone can have above it is inside the loosened frustum. The square is taken relative to the eye (`Poly3_SubtractOffset`, `00494f98`), cut by `Poly_SplitByPlane` (`0047e630`), keeping the front, against left, right, top, bottom, far and near in that order, and moved back (`Poly3_AddOffset`, `00494fe0`).

`ViewFrustum_ClipPolygon` (`00494ba8`) is the same clip with the planes as built.

## The cell walk — `Terrain_DrawVisibleCells` (`0046d0a4`)

`Scene_DrawTerrain` (`00428140`) installs the transform at `DAT_004aab08` as the model transform, sets the render context's clip mode (`+0x208`, see [`hud-target-indicator.md`](hud-target-indicator.md)) to 1 for the duration when its clip block holds one region and that region's first dword is 0, and calls `Terrain_DrawVisibleCells(g_TerrainDrawGrid, view)`. `g_TerrainDrawGrid` (`0049aac8`) is the grid `Terrain_LoadZone` builds.

`Terrain_DrawVisibleCells` does nothing while `grid+0x11c` is clear. Otherwise it points the render context's `+0x22c` at a zero dword for the duration, puts the viewer's cell (the view's position shifted down by the cell shift) in `DAT_006b4fd4`/`d8`, and that cell's pending-object count in `DAT_006b4fe0`. Then it walks the region polygon around the viewer's cell.

**Rows or columns, by heading.** When the view's heading (`+0x14`), as a magnitude with `−0x8000` read as `0x7fff`, lies in `0x2001`..`0x5fff`, the view looks more along X than along Y, and the walk goes by column: `CellWalk_PolygonByColumn` (`004723f8`) with `Terrain_DrawCellColumnRun` (`0046dea4`). Otherwise it goes by row: `CellWalk_Polygon` (`00471e38`) with `Terrain_DrawCellRowRun` (`0046dcd8`). `CellWalk_SetCallback` (`00471e24`) installs the callback with the grid as its value. The walks are the DBSIM copies of [`../polygon-fill.md`](../polygon-fill.md#walking-a-polygons-cells)'s, and both go far to near: the column walk always, and the row walk because the two flags it tests hold the values that select that branch in the image.

With `(dx, dy)` a cell's offset from the viewer's cell, a row walk paints:

1. the rows with `dy ≥ 1`, the farthest first; on each, the cells with `dx ≥ 1` from the far end in, then those with `dx ≤ 0` from the far end in;
2. the rows with `dy ≤ 0`, the farthest first and `dy = 0` last; on each, the cells with `dx ≤ −1` from the far end in, then those with `dx ≥ 0` from the far end in.

A column walk is the same turned a quarter:

1. the columns with `dx ≤ −1`, the farthest first; on each, the cells with `dy ≥ 1` from the far end in, then those with `dy ≤ 0` from the far end in;
2. the columns with `dx ≥ 0`, the farthest first and `dx = 0` last; on each, the cells with `dy ≤ −1` from the far end in, then those with `dy ≥ 0` from the far end in.

Only cells inside the region polygon are visited, and the viewer's own cell is last in either walk. Each run goes from its far end toward the viewer's column (row), the rows of each half go from the farthest toward the viewer's, and a row is finished on both sides of the viewer's column before the next begins. It is not a sort by distance.

**The runs.** A run callback builds the four view-space corners of the run's first cell from the grid origin and the two cell steps, calls `Terrain_DrawCellQuad(cellX, cellY)` for each cell from one end of the run to the other by the context's step, and moves the corners one step each time. `Terrain_DrawCellQuad` adds each corner's height through `HeightOffsetTable_Get` (`00495240`) against the table at `grid+0xd0`, resolves the texture with `Terrain_ResolveCellTexture` (`0046bcf4`) and fills the cell's triangles through the perspective-correct `Raster_DrawTexturedPolyNear` (`0046865c`) for a near cell or the screen-linear `Raster_SetupTexturedSpan` (`00468078`) for the rest ([which is which](dts-texture-binding.md#screen-linear-and-perspective-correct-fills)) when texturing is on, `Terrain_FillCellUntextured` (`0046bb40`) when it is off. When the run is on the viewer's row (column) and covers the viewer's cell, it clears `DAT_006b4fe0`. After the walk, `Terrain_DrawVisibleCells` calls `Terrain_DrawCellObjects` for the viewer's cell if `DAT_006b4fe0` is still set, so the objects of a viewer's cell that the walk did not reach are drawn after all the ground.

## Objects in the walk

`Scene_SubmitFrameObjects` files each object in `ObjList::drawTable` under a cell ([below](#what-is-filed-where)), and `Terrain_DrawCellQuad` ends with `Terrain_DrawCellObjects` for its own cell. That calls `ObjList_DrawCellObjects` (`00428c60`), which draws the cell's tag-9 objects, the ground shapes ([`../simulation/ground-shapes.md`](../simulation/ground-shapes.md#the-draw-pass)), on the spot in filing order with the ramp's row count `DAT_004a5b1c` zeroed around each draw ([`dts-texture-binding.md`](dts-texture-binding.md#tstexture4poly--frame-index-ramp-row-by-light-fullbright-on-demand)), turns every other object into a render entry, and draws those at the end of the cell farthest first: `ObjList_DrawSorted` (`00429620`) files them in a binary tree keyed on the entry's distance and walks it in order. An object the camera rides is skipped, and one farther away than its class's draw distance gets no entry:

| Type tag at `+4` | Draw distance, × the terrain draw radius |
|---|---|
| 0, a drop pod: `Meteor_Construct` never writes the tag, so it keeps the zero `Pool_Init` left | 900/1024 |
| 8, an explosion whose type record's `+0x26` is set (2 otherwise) | 1000/1024 |
| 5, a structure, shape radius under 7000 | 800/1024 |
| 5, shape radius 7000 or more | 1200/1024 |
| anything else — 7 a HERC, 6 a flyer, 3 a bullet, launcher round or beam tracer, 2 the other explosions, 1 debris, 4 a smoke ball or fire | 800/1024 |

`ObjList_SetDrawDistances` (`00428bc0`) scales the radius `grid+0x10c << cellShift` ([`terrain-texturing.md`](terrain-texturing.md#grid0x10c--the-lod--draw-radius-field)) by the five Q10 factors at `0049abb0` into `DAT_004cfa0c` from `Terrain_SetupVisibleRegion`, once a frame, and `ObjList_IsBeyondDrawDistance` (`00428c08`) picks the entry by the tag and compares the render entry's distance from the view (`Math_DistanceBetweenPoints`) against it. The entry's position is the object's (vtable `+0x04`), raised for tags 5 and 7 by the Z translation (`+0x1c`) of the node vtable `+0x24` returns — a structure's aim-point height, a HERC's camera node — or by 500 when it returns none. The shape radius is `SimObject_GetShapeRadius`, vtable `+0x10`. So a large structure is drawn as far as the walk reaches, and everything else that is not a tag-9 ground shape vanishes short of the terrain's edge. What is filed under a cell is painted over that cell's ground and under every cell painted after it. A tag-9 object has no fade of its own: its solid faces fog with the one its cell's quad installed ([`distance-fog-and-sky.md`](distance-fog-and-sky.md#what-gets-faded)).

### What is filed where

`Scene_SubmitFrameObjects` (`0042841c`) clears the per-cell object counts (`HeightGrid_ClearCellScratch`, `0046e840`) and walks the pools from the tail, oldest first, in this order:

| Pool | Submitted | Filed under |
|---|---|---|
| `g_FlatObjPool`, ground shapes | within 30000 of the view | `Scene_SubmitObject` (`004282d8`): the cell `HeightGrid_PickDrawCell` picks from the position and the shape radius |
| `g_StructurePool` (`004a9624`), structures | all | `Scene_SubmitObjectWithRadius` (`0042837c`): the pick by the body radius (vtable `+0x5c`) |
| `GlobalMechList`, machines | all | the cached cell of the structure at `mech+0x2b0`, through `Scene_SubmitObjectAtCell` (`004283b4`), when one is recorded ([`../simulation/mech-locomotion.md`](../simulation/mech-locomotion.md#the-structure-a-machine-stands-in)); the pick by the body radius otherwise |
| `g_FlyerPool` (`004a9e3d`), flyers | all | the pick by the body radius, which is 0 |
| `g_ProjectilePool` (`004a9746`), bullets, launcher rounds and beam tracers | all | `Scene_SubmitObject`; a tracer has no shape, so radius 0, and a straight beam is one tracer per 5000-unit span ([`../simulation/beam-visuals.md`](../simulation/beam-visuals.md#chain)) |
| `g_ExplosionPool`, impact effects | unless `Explosion_IsHiddenFromOwnerCockpit` | the owner's cached cell (`Explosion_GetOwnerDrawCell`, `00408228`) when it has an owner, `Scene_SubmitObject` otherwise ([`../simulation/impact-effects.md`](../simulation/impact-effects.md#drawing)) |
| `g_DebrisPool`, debris | all | `Scene_SubmitObject` |
| `g_SmokeBallPool` (`004a96f2`), smoke balls | all | `Scene_SubmitObject` |
| `g_FirePool`, fires | all | the cached cell of the object at `fire+0x4a` (`Fire_GetOwnerDrawCell`, `0046b74c`), which a fire always has |
| `g_MeteorPool`, drop pods | all | `Scene_SubmitObject` |

The structure, machine and flyer walks skip an object whose mission group still carries an action (`*(obj+0x45)+0x14`, [`../simulation/hit-detection.md`](../simulation/hit-detection.md)), and store the cell they filed it under at `obj+0x1e8`/`+0x1ea`, or `0xffff` for the no-cell bucket; that is the cached cell the later walks read. The structures come first, so a machine standing in one reads the cell picked this frame. `HeightGrid_ClaimDrawCell` turns a cached `0xffff` pair into the no-cell bucket.

### `HeightGrid_PickDrawCell` (`0046e528`)

`(grid, pos, radius)` starts from the cell holding `pos` and compares it with the viewer's cell (`DAT_006b4fc8`/`cc` shifted down):

| The object's cell | Moves X | Moves Y |
|---|---|---|
| `x > vx`, `y > vy` | −1 when `pos.x < (x << shift) + radius` | −1 when `pos.y < (y << shift) + radius` |
| `x ≤ vx`, `y > vy` | +1 when `pos.x > ((x + 1) << shift) − radius` and `x < vx` | −1 as above |
| `x ≤ vx`, `y ≤ vy` | +1 as above | +1 when `pos.y > ((y + 1) << shift) − radius` and `y < vy` |
| `x > vx`, `y ≤ vy` | −1 as above | +1 as above |

So an object within its radius of the edge its cell shares with the next cell toward the viewer goes to that nearer cell, at most one step on each axis. When it has moved, the move is undone on both axes if `Terrain_FaceNormalAt` finds a face under `pos` and `Terrain_FaceVisibilityTest` says that face turns away from the viewer, measured at `pos` itself. Then each axis whose new cell lies one past the region's bounding box (`grid+0x28`'s smallest `x` less one or largest plus one, and the same for `y`) goes back to the object's own cell. A cell off the grid gives 0, and `ObjList_AddToDrawTable` then files the object in the no-cell bucket; a cell on it goes through `HeightGrid_ClaimDrawCell` (`0046e7c0`).

## After the walk — `ObjList_DrawAfterTerrain` (`0042883c`)

It calls slot 0 of every object in the no-cell bucket (`DAT_004cf910`, count `DAT_004cf9b0`) with no fade installed of its own. Then, when the local player's machine is a flyer — its type record (`+0x1f2`) has the flyer flag `+0x50` set, which only the RAZOR does ([`../simulation/mech-locomotion.md`](../simulation/mech-locomotion.md#mech-type-record)) — and the camera is not riding it (`Cam_IsAttachedTo`), it runs `Terrain_DrawCellObjects` for the player's cached cell (`+0x1e8`/`+0x1ea`), which draws nothing once the walk has emptied that cell. So a RAZOR seen from outside is drawn, with whatever shares its cell, even when the walk does not reach that cell. Everything it draws lands over all the ground.

### The pixel pick and the occlusion probe

Two screen tests ride on the object draw, each armed by a function `es2_xref.py` finds no reference to ([Open](#open)).

- **The pick.** `ObjPick_Arm` (`004282a0`) stores a screen point at `004cf9b5` and sets `DAT_004cf9b4`. While it is set, `ObjList_DrawAfterTerrain` points the display's source page `+0x38` at its `+0x8c`, saves the pixel under the point (`g_RasterRoutines_GetPixel`, slot 4) at `004cfa00`, writes 1 there (`g_RasterRoutines_PutPixel`, slot 5) and clears the picked object `004cf9c0`; after `ObjList_SetViewObject` it writes the saved pixel back and clears the flag. `ObjList_DrawEntryRender` re-reads the pixel after each entry's draw and, when it is no longer 1, records the entry's object at `004cf9c0` and writes 1 again. `Gunsight_UpdateAndPaint` hands that object (`ObjPick_GetObject`, `004282c0`) to `TargetSelect_SetObject`.
- **The occlusion probe.** `ObjProbe_Arm` (`00428960`) records one object at `004cf9f8` and the four corners of a screen square sized from its radius, and sets `DAT_0049abbc`. `ObjList_DrawEntryRender` saves and marks each corner with 2 just after drawing that object (`ObjProbe_MarkCorners`, `00428ae0`); `ObjProbe_TestCorners` (`00428b38`), the last step of `ObjList_DrawAfterTerrain`, flags every corner that no longer reads 2 as covered and restores the rest. `ObjProbe_IsHidden` (`00428ab8`) answers whether all four are covered. The arm's off-screen test never advances its point pointer, so all four corners take the first corner's answer.

## Rejected readings

| Reading | Why it is wrong |
|---|---|
| The terrain is drawn back to front by distance from the viewer | The walk orders cells by row (or column) and by side of the viewer's cell, not by distance: a far cell at the end of a near row is painted after a nearer cell in a farther row |

## Open

- **Open:** what arms [the pick and the probe](#the-pixel-pick-and-the-occlusion-probe). `es2_xref.py` finds no reference to `ObjPick_Arm` or `ObjProbe_Arm`, and they hold the only stores of 1 to `DAT_004cf9b4` and `DAT_0049abbc`. Unless something else arms the pick, `ObjPick_GetObject` always returns 0 and a gunsight click selects nothing.
