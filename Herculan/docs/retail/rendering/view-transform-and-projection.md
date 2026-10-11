# View transforms and projection (DBSIM.EXE and VSHELL.EXE)

**Every shape, effect and HUD marker reaches the screen through one small piece of global state: the camera being drawn, the transform of the object being drawn, and up to ten lights.** Installing a camera fixes the projection; installing an object's transform rebuilds the object-to-camera transform and moves the lights into the object's space; a polygon is then projected vertex by vertex, clipped where it crosses the plane just in front of the eye, and filled. This doc owns that state and the functions that build and read it. What a light adds to a face is in [`dts-texture-binding.md`](dts-texture-binding.md#two-shade-calculations--terrain-and-shapes-use-different-ones) and [`effect-lights.md`](effect-lights.md#what-a-light-contributes); the face's front/back test is [`TSPoly_FrontBackVisibilityTest`](dts-texture-binding.md#tspoly_frontbackvisibilitytest); where a view's projection centre comes from is in [`../simulation/cockpit-views.md`](../simulation/cockpit-views.md#the-projection-centre-is-not-the-middle-of-the-view).

Addresses are DBSIM's. VSHELL carries the same functions, compiled from the same source, and names a few of them differently; [the last section](#vshells-copies) lists them.

## Coordinates

A transform is the 32-byte record in [`../simulation/sim-object-layout.md`](../simulation/sim-object-layout.md): nine Q14 `int16` rotation entries, the rank byte at `+0x12`, and an `int32` translation at `+0x14`. View space has **x across, y the depth away from the eye, z up**: the projection divides x and z by y.

## Contexts, cameras and lights

`TSContext` is a position and an orientation, `0x16` bytes: the vtable at `+0`, an `int32` position triple at `+4`, an `int16` angle triple at `+0x10`. `View_CtorBase` (`0048bb64`) builds one from a position and an angle triple.

A camera, `TSCamera`, is `0x22` bytes: a `TSContext` and then

| Offset | Field |
|---|---|
| `+0x16` | the render context it draws into |
| `+0x1a` | the focal shift `s`: the focal length is `2^s` pixels |
| `+0x1e` | the near-plane depth |

`View_Ctor` (`0048bc98`) takes `(camera, renderContext, shift, nearPlane, position, angles)`. `TSOrthoCamera` adds `+0x22`, the orthographic divisor, which the projection uses in place of the depth when the shift is 0.

The four `View_Ctor` calls in DBSIM's decompile pass a near-plane depth of 0x100 or `1 << shift`: `Sim_InitMissionSession`'s main view 0x100 with a shift of 8 or 9 ([`../simulation/cockpit-views.md`](../simulation/cockpit-views.md#the-views-focal-length) says which), `MfdMapScreen_Ctor`'s 0x100 with 8, `MfdMissileViewScreen_Ctor`'s 0x80 with 7, and `HddCommandScreen_Ctor`'s `1 << shift`. The [near-plane clip](#clipping-at-the-near-plane) depends on the two being equal.

A light, `TSLight`, is a `TSContext` (its world position, and its direction as the angle triple's storage) followed by

| Offset | Field |
|---|---|
| `+0x16` | type: 0 ambient, 1 directional, 2 point |
| `+0x1a` | intensity, which `maybe_TSLight_SetIntensity` (`0048bbe0`) sets |
| `+0x1e` | a second `TSContext`, the light's position (`+0x22`) and direction (`+0x2e`) in the space of the model being drawn |
| `+0x34` | the point light's falloff offset |
| `+0x38` | the point light's range numerator |
| `+0x3c` | intensity × range numerator, kept by the omni light's intensity setter (`maybe_TSOmniLight_SetIntensity`, `0048bc60`); none of the three shade functions below reads it |

## The active light list

The lights a face is shaded by are the ones in the active list, `006c6130`, ten pointers with the count at `006c612c`. `Light_Register` (`0048bd44`) appends a light and returns 1, or returns 0 when the list is full or already holds it; `Light_Unregister` (`0048bd8c`) moves the last entry into the removed one's place and returns 1, or 0 when the light is not in the list. Removal therefore reorders the list, and the first light registered stays at entry 0 for as long as it is registered.

Three functions sum a shade byte over the list, each clamped to 255. `Light_ComputeShadeForFace` (`0048bedc`, shapes) and `Light_ComputeShadeForNormal` (`0048c060`, terrain) take a normal and a point; [`dts-texture-binding.md`](dts-texture-binding.md#two-shade-calculations--terrain-and-shapes-use-different-ones) gives both. `Light_ComputeShadeAtPoint` (`0048bdcc`) takes only a point: an ambient or directional light adds its intensity whatever its direction, and a point light adds `intensity * (0x7ff * rangeNumerator / (falloffOffset + |position - point|)) >> 11`, with the light's model-space position.

The shade functions read each light's model-space copy at `+0x1e`. `Light_TransformToModelSpace` (`0048bbf0`) fills it: the light's position through a transform, and its direction through the transform's rotation alone.

## Installing a camera

`Raster_InstallViewProjection` (`0048c1d8`) makes a camera the one drawn through:

1. The shift goes to `006c60ac` and the near-plane depth to `006c60b0`; when the shift is 0, the orthographic divisor goes to `006c60b4`.
2. From the camera's render context, the clip rect at `+0x210`..`+0x21c` is copied to `006c60c0`..`006c60cc`, and the projection centre `006c60b8`/`006c60bc` is the rect's top-left minus the context's `+0x220` pair.
3. The camera's own transform, built from its angles and position (`View_BuildTransform`), is inverted (`Transform_InvertRigid`) into the world-to-view transform at `006c6018` and installed with `Raster_SetViewTransform`.

`Raster_SetViewTransform` (`0048c2a4`) stores the view transform's address at `006c6078` (null installs the identity at `006c6080`) and rebuilds the three derived values:

- `g_ModelToView` (`006c6038`) = `Transform_Concat(model, view)`: the model's transform applied first;
- `g_ViewToModel` (`006c6058`), its inverse;
- `g_EyeInModelSpace` (`006c60a0`), the view-space origin carried back through `g_ViewToModel`: where the eye is in the model's coordinates.

## Installing a model's transform

`Raster_SetModelTransform` (`0048c338`) makes a transform the model transform, at `g_ModelTransformPtr` (`006c607c`), null again meaning the identity, and rebuilds the same three values against the installed view. When `006cbc88` is set, it then inverts the model transform and runs `Light_TransformToModelSpace` on every registered light with that inverse, so each light's `+0x1e` copy is in the model's space. What sets the flag is in [`effect-lights.md`](effect-lights.md#per-object-selection--lightmanager_selectlightsforobject-00407098).

A shape with several nodes installs each node's transform as it draws, and keeps the ones it has built in save slots so a node drawn again needs no rebuild. `Raster_SaveState` (`0048d60c`) writes a slot:

| Slot offset | Saved |
|---|---|
| `+0x00` | `g_ModelToView`, 0x20 bytes |
| `+0x20` | the model transform pointer |
| `+0x24` | the view transform pointer |
| `+0x28` | `g_EyeInModelSpace`, three `int32` |
| `+0x34` | when `006cbc88` is set, `0x16` bytes per registered light: the position and direction of its model-space copy |

`Raster_RestoreState` (`0048d6e0`) puts a slot back and recomputes `g_ViewToModel` from the restored `g_ModelToView`. Both light loops step through the slot but address the light through list entry 0 every time, so only that light's copy is saved and restored; what that does to the lights is in [`effect-lights.md`](effect-lights.md#a-part-drawn-through-a-saved-state-shades-the-effect-lights-in-another-nodes-frame).

## Projecting a point

With `s` the shift at `006c60ac` and `(cx, cy)` the projection centre:

```
screen.x = cx + (x << s) / y          screen.y = cy - (z << s) / y          // s != 0
screen.x = cx + (x << 8) / k          screen.y = cy - (z << 8) / k          // s == 0, k = 006c60b4
```

| Function | Does |
|---|---|
| `Raster_ModelToView` (`0048c470`) | a model-space `int32` point through `g_ModelToView` |
| `Raster_PerspectiveDivide` (`0048c4f0`) | `{(x << s) / y, (z << s) / y}`, or the orthographic form, without the centre |
| `Raster_ProjectToScreen` (`0048c5c4`) | adds the centre: `{x + cx, cy - y}` |
| `Raster_PerspectiveScale` (`0048c4c0`) | one length at a depth: `(length << s) / depth`, or `(length << 8) / k` |
| `Raster_ModelToViewArray` (`0048c48c`), `Raster_PerspectiveDivideArray` (`0048c54c`), `Raster_ProjectToScreenArray` (`0048c5e8`) | the same over `count` points |

**The orthographic form of `Raster_PerspectiveDivideArray` never ends for a non-zero count**: its loop advances neither pointer nor the count, so it rewrites the first point forever. The perspective form is correct.

## Projecting and clipping a polygon

A polygon is drawn in three steps: project its vertices to screen points, clip the ring against the near plane if a vertex fell behind it, and fill the result. The polygon arrives in a scratch block at `006c6968`:

| Address | Offset | Holds |
|---|---|---|
| `006c6968` | `+0x0` | the polygon's vertex count, `uint16` |
| `006c696a` | `+0x2` | its offset into the vertex-index list, `uint16` |
| `006c696c` | `+0x4` | the shape's point array, 6-byte `int16` triples |
| `006c6970` | `+0x8` | the point array of the `int32` callers, 12-byte triples |
| `006c6974` | `+0xc` | where the beam draw stores its point count ([`../simulation/beam-visuals.md`](../simulation/beam-visuals.md)); none of the functions below reads it |
| `006c6976` | `+0xe` | the vertex-index list, `uint16` point numbers |
| `006c697e` | `+0x16` | one state byte per point, 1000: 0 not yet seen, 1 behind the near plane, 2 projected |
| `006c6d66` | `+0x3fe` | `Poly_ViewCache`: each point's view-space position, 1000 × 12 bytes |
| `006c9c46` | `+0x32de` | `Poly_ScreenCache`: each projected point's screen position, 1000 × 8 bytes |
| `006cbb86` | `+0x521e` | `Poly_ScreenPoints`: the ring to fill, 32 screen points of 8 bytes |
| `006cbc86` | `+0x531e` | the ring's point count, `uint16` |

The state bytes make a vertex shared between polygons cost one transform: a point marked 2 is copied from the screen cache. What resets them is [Open](#open).

| Function | Points | Does |
|---|---|---|
| `Poly_ProjectShapeVertices` (`0048c848`) | `int16` | With the shift 0, transforms (`Transform_ApplyToShortPoint`) and projects every vertex, sets the ring's count and returns 0. Otherwise returns `Poly_ProjectShapeVerticesPerspective` |
| `Poly_ProjectShapeVerticesPerspective` (`00491194`) | `int16` | Sets the ring's count to the vertex count, then transforms each vertex not yet seen: one deeper than the near plane is projected and marked 2, any other marked 1. Returns 1 when a vertex is marked 1 |
| `Poly_ProjectIndexedVertices` (`0048c964`) | `int32` | The same for both modes in one function, a vertex as deep as the near plane counting as in front; sets the ring's count only when no vertex is behind |
| `Poly_ProjectViewVertices` (`0048cbd8`) | `int32`, already in view space | As `Poly_ProjectIndexedVertices` without the transform; when a vertex is behind, it copies the ring's points into the view cache for the clip |

Each writes a projected vertex to the ring by its position in the polygon, so a vertex behind the plane leaves its ring entry unwritten and the clip rebuilds the ring.

### Clipping at the near plane

`Poly_ClipRingToNearPlane` (`0048ce14`) rebuilds the ring from the view cache, walking each edge from the previous vertex `a` to the next `b`:

- both projected: emit `b`'s screen point;
- one projected: emit the point where the edge crosses the near plane, then `b`'s screen point when `b` is the projected one;
- neither: emit nothing.

The crossing point is placed without a divide:

```
t      = (near - a.y) / (b.y - a.y)
screen = { cx + a.x + (b.x - a.x) * t,   cy - (a.z + (b.z - a.z) * t) }
```

That is the perspective projection of a point at depth `near` only when `near = 2^s`. It holds for every camera [above](#contexts-cameras-and-lights) but the main view at shift 9, whose near-plane depth of 0x100 is half of `2^9`: there a crossing point lands at half its projected distance from the centre.

It returns the new count. `Poly_ClipRingToNearPlaneWithUV` (`0048cfec`, `(uvIn, uvOut)`) carries an `int32` `{u, v}` pair per vertex through the same walk, interpolated at a crossing by the same `t`, and `Poly_ClipRingToNearPlaneWithShade` (`0048d280`, `(shadeIn, shadeOut)`) a `uint16` shade.

### Filling

`PolyFill_FillThenOutline` (`0048d518`, `(reversed)`) fills the ring, when it has any points, with the brush the render context holds: through `GL_DrawPolygonReversed` (`0048daa0`) when `reversed` is non-zero and `GL_DrawPolygonDispatch` (`0048dab5`) otherwise, each of which ignores its first argument and calls `Raster_DrawPolygonReversed` (`00484116`) or `Raster_DrawPolygonDispatch` (`00483dac`). Then, when the line colour `006c60d4` differs from the brush colour `006c60dc` and the brush mode `PolyFill_FillBrush` (`006c60d8`) is not 5, it sets the brush to mode 4 in the line colour and draws the ring again the same way: mode 4 is the dispatch's line loop, so this is the outline. `PolyFill_FillThenOutline_Thunk` (`0048d5fc`) forwards to it. `PolyFill_Fill` (`0048d4b4`) fills through `GL_DrawPolygonEitherWinding` (`0048d8ae`) and so `Raster_DrawPolygonEitherWinding` (`004841af`), and runs the same outline pass without the mode-5 test. The three `Raster_*` fills are in [`polygon-fill.md`](polygon-fill.md). A caller that installed its own brush in the render context gets a second flat fill instead of an outline ([`dts-texture-binding.md`](dts-texture-binding.md#tssolidpoly--palette-index-unlit-fill-plus-outline)).

Before any of this runs, the static constructor `Raster_StaticInit` (`0048dc7c`) sets the line brush `PolyFill_LineBrush` (`006c60d0`) to mode 0, a flat colour (`LineBrush_Ctor`), and the fill brush to mode 1 with its `+8` and `+0xc` cleared (`FillBrush_Ctor`).

## VSHELL's copies

| DBSIM | VSHELL |
|---|---|
| `View_Ctor` (`0048bc98`) | `TSCamera_Ctor` (`0045db04`) |
| `Raster_PerspectiveDivide` (`0048c4f0`) | `Raster_Project` (`0045e350`) |
| `Raster_PerspectiveScale` (`0048c4c0`) | `Raster_ProjectSize` (`0045e320`) |
| `Raster_PerspectiveDivideArray` (`0048c54c`) | `Raster_ProjectArray` (`0045e3ac`) |
| `Raster_ProjectToScreen` (`0048c5c4`) | `Raster_ToScreen` (`0045e424`) |
| `Raster_ProjectToScreenArray` (`0048c5e8`) | `Raster_ToScreenArray` (`0045e448`) |

The others keep their DBSIM names. VSHELL also has `PolyFill_Fill_Thunk` (`0045f440`), six bytes that call `PolyFill_Fill`.

## Open

- **Open:** what resets the per-point state bytes at `006c697e` between shapes. None of the projection or clip functions writes 0 to them.
- **Open:** no caller of DBSIM's `Light_ComputeShadeAtPoint`, `Raster_ModelToViewArray`, `Raster_PerspectiveDivideArray` or `Raster_ProjectToScreenArray` found by `es2_xref.py`.
- **Open:** no reference to VSHELL's `PolyFill_Fill_Thunk` found by `es2_xref.py`.
