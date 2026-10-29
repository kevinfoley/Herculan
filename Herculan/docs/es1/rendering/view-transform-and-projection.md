# DBSIM.EXE — view transforms and projection

**ES1's simulator places, lights and projects what it draws exactly as ES2's does.** Its cameras, lights, transform globals, projection and near-plane clip are the functions [`../../retail/rendering/view-transform-and-projection.md`](../../retail/rendering/view-transform-and-projection.md) describes, built by a different compiler: each ES1 function below decompiles to the same statements as its ES2 twin once the two compilers' differences are set aside, and the formulas, field offsets past `+0x16`, save-slot layout and polygon scratch-block layout are ES2's. This page gives ES1's addresses and the differences. Addresses are `ES1DBSIM` image addresses ([`../formats/x32-executable.md`](../formats/x32-executable.md#the-image)).

## Differences from ES2

**A context keeps its vtable last.** ES1's `TSContext` holds its position at `+0`, its angle triple at `+0xc` and its vtable at `+0x12` ([`../formats/compiler.md`](../formats/compiler.md#object-layout)), where ES2's holds the vtable at `+0` and the other two 4 bytes later. A camera's and a light's own fields start at `+0x16` in both games and keep ES2's offsets, but a light's model-space copy, itself a `TSContext` at `+0x1e`, holds its position at `+0x1e` and its direction at `+0x2a`, where ES2's are at `+0x22` and `+0x2e`.

**ES1 calls fewer helpers.** Where ES2 calls `Transform_InvertRigid`, ES1 inverts inline: it copies the whole 32-byte transform, transposes the rotation (`Transform_TransposeRotation`), negates the translation and rotates it through the transposed rotation (`Transform_RotatePoint`), the same arithmetic. `Raster_InstallViewProjection` builds the camera's world-to-view transform the same way, from `BuildEulerRotationMatrixQ14` of the camera's angles. The shade functions read a light's fields directly where ES2 calls accessors, the near-plane clips add the projection centre inline where ES2 calls `Point2I_Add`, and the two polygon fills call `Raster_DrawPolygonDispatch`, `Raster_DrawPolygonReversed` and `Raster_DrawPolygonEitherWinding` directly where ES2 goes through `GL_*` wrappers that drop a first argument.

**The installed view is laid out differently.** The clip rect is four `int32` at `000b5ee8`–`000b5ef4` and the projection centre two at `000b5ee0`, and the focal shift, near plane and orthographic divisor sit apart from them, at `000bdb52`–`000bdb5a`. ES2 keeps a copy of the rect's top-left beside the centre; ES1 does not. The world-to-view transform is a static at `000b5e04` with a one-byte first-use flag before it and no constructor call.

**Every ES1 camera keeps the near-plane clip exact.** The four calls to `TSCamera_Ctor` in the decompile pass a near-plane depth of `2^shift`: the main view 0x100 with shift 8 (`0005a9dc`), and three cameras 0x80 with shift 7 (`000360c0`, `00036b18`, `HddCommandScreen_Ctor` (`0003ee58`)). So the crossing points the [clip](../../retail/rendering/view-transform-and-projection.md#clipping-at-the-near-plane) places without a divide always land where the projection would put them.

**The model-space flag is not next to the scratch block.** ES1's `Light_ModelSpaceEnabled` is at `000bdbce`; ES2's sits right after the ring's point count ([`../../retail/rendering/effect-lights.md`](../../retail/rendering/effect-lights.md#per-object-selection--lightmanager_selectlightsforobject-00407098)).

ES2's two faults are ES1's too: `Raster_PerspectiveDivideArray`'s orthographic loop never advances, and `Raster_SaveState` and `Raster_RestoreState` address every light through list entry 0.

## Functions

| ES1 | Address | Notes |
|---|---|---|
| `TSContext_Ctor`, `TSContext_CtorWithFrame`, `TSContext_Dtor` | `0005b754`, `0005b7d0`, `0005b878` | |
| `TSContext_Assign` | `0005d448` | copies the position and angles, not the vtable; ES2's `Raster_SaveState` copies the fields inline |
| `maybe_TSLight_SetIntensity`, `maybe_TSOmniLight_SetIntensity` | `0005b8a0`, `0005b91c` | |
| `Light_TransformToModelSpace` | `0005b8b4` | |
| `TSCamera_CtorDefault`, `TSCamera_Ctor`, `TSCamera_Dtor` | `0005b944`, `0005b99c`, `0005b970` | `TSCamera_Ctor` is DBSIM's `View_Ctor`, arguments in the reverse order ([compiler.md](../formats/compiler.md#calling-conventions)) |
| `TSOrthoCamera_Ctor`, `TSOrthoCamera_Dtor` | `0005b9e0`, `0005ba24` | |
| `Light_Register`, `Light_Unregister` | `0005ba84`, `0005bad4` | the list at `000bdba6`, count `000bdba2` |
| `Light_ResetSystem` | `000632ec` | sets `Light_ModelSpaceEnabled` and builds the identity transform inline, where ES2 calls `Light_EnableModelSpaceTransform` and `Transform_Reset`; it registers the [shape type registry](../formats/dts-shape.md) |
| `Raster_StaticInit` | `00063388` | a static constructor, its pointer at `000adc7c` ([static construction](../formats/compiler.md#static-construction-and-exit)). It sets the two brushes' modes inline and builds the three caches with `__vec_ctor` and the element constructors `maybe_Point3I_Ctor` (`000633fc`) and `maybe_Point2I_Ctor` (`0006342c`), where ES2's passes `Rtl_ReturnArg` for the 8-byte ones; it calls nothing for the transforms, eye point, projection centre and clip rect ES2's also constructs |
| `Light_ComputeShadeAtPoint`, `Light_ComputeShadeForFace`, `Light_ComputeShadeForNormal` | `0005bb24`, `0005bc30`, `0005bdd8` | |
| `Raster_InstallViewProjection`, `Raster_SetViewTransform`, `Raster_SetModelTransform` | `0005bf68`, `0005c078`, `0005c140` | |
| `Raster_ModelToView`, `Raster_ModelToViewArray` | `0005c290`, `0005c2ac` | |
| `Raster_PerspectiveScale`, `Raster_PerspectiveDivide`, `Raster_PerspectiveDivideArray` | `0005c2e8`, `0005c318`, `0005c37c` | |
| `Raster_ProjectToScreen`, `Raster_ProjectToScreenArray` | `0005c410`, `0005c438` | |
| `TSPoly_FrontBackVisibilityTest`, `Raster_FrontFacing` | `0005c47c`, `0005c530` | |
| `Poly_ProjectShapeVertices`, `Poly_ProjectShapeVerticesPerspective` | `0005c5d4`, `00075a28` | |
| `Poly_ProjectIndexedVertices`, `Poly_ProjectViewVertices` | `0005c724`, `0005c9bc` | |
| `Poly_ClipRingToNearPlane`, `…WithUV`, `…WithShade` | `0005cc4c`, `0005ce10`, `0005d06c` | |
| `PolyFill_Fill`, `PolyFill_FillThenOutline`, `PolyFill_Fill_Thunk`, `PolyFill_FillThenOutline_Thunk` | `0005d298`, `0005d2f0`, `0005d3b8`, `0005d3c0` | |
| `Raster_SaveState`, `Raster_RestoreState` | `0005d3d0`, `0005d470` | |
| `Raster_DrawPolygonReversed`, `Raster_DrawPolygonEitherWinding` | `00068f43`, `00068fdc` | ES2's are in [`../../retail/rendering/polygon-fill.md`](../../retail/rendering/polygon-fill.md) |

## Globals

| ES1 | Address | ES2 (DBSIM) |
|---|---|---|
| `Raster_WorldToView` | `000b5e04` | `006c6018` |
| `g_ModelToView`, `g_ViewToModel` | `000b5e74`, `000b5e94` | `006c6038`, `006c6058` |
| `Transform_Identity` | `000b5eb4` | `006c6080` |
| `g_EyeInModelSpace` | `000b5ed4` | `006c60a0` |
| `Raster_ProjectionCentre`, `Raster_ViewClipRect` | `000b5ee0`, `000b5ee8` | `006c60b8`, `006c60c0` |
| `PolyFill_LineBrush`, `PolyFill_LineColour` (its `+4`), `PolyFill_FillBrush` (mode, then colour at `+4`) | `000b5ef8`, `000b5efc`, `000b5f00` | `006c60d0`, `006c60d4`, `006c60d8` |
| the polygon scratch block, from the vertex count | `000b6720` | `006c6968` |
| `Poly_ViewCache`, `Poly_ScreenCache`, `Poly_ScreenPoints`, the ring's count | `000b6b1e`, `000b99fe`, `000bb93e`, `000bba3e` | `006c6d66`, `006c9c46`, `006cbb86`, `006cbc86` |
| `Raster_ViewTransformPtr`, `g_ModelTransformPtr` | `000bdb4a`, `000bdb4e` | `006c6078`, `006c607c` |
| `Raster_FocalShift`, `Raster_NearPlane`, `Raster_OrthoDivisor` | `000bdb52`, `000bdb56`, `000bdb5a` | `006c60ac`, `006c60b0`, `006c60b4` |
| `Light_ActiveCount`, `Light_ActiveList` | `000bdba2`, `000bdba6` | `006c612c`, `006c6130` |
| `Light_ModelSpaceEnabled` | `000bdbce` | `006cbc88` |

The scratch block's fields keep ES2's offsets from its start.

## Open

- **Open:** no reference to `PolyFill_Fill_Thunk` found by `es2_xref.py`, and Ghidra has no function there: it is the six bytes `call PolyFill_Fill; ret` between `PolyFill_FillThenOutline` and `PolyFill_FillThenOutline_Thunk`, as VSHELL's is.
- **Open:** no caller of `Light_ComputeShadeAtPoint`, `Light_ComputeShadeForNormal`, `TSOrthoCamera_Ctor`, `Raster_ModelToViewArray`, `Raster_PerspectiveDivideArray`, `Raster_ProjectToScreenArray` or `Poly_ProjectViewVertices` found by `es2_xref.py`. ES2's terrain bakes its shade with `Light_ComputeShadeForNormal`.
- **Open:** what resets the per-point state bytes at `000b6736` between shapes, as in ES2.
