# DBSIM.EXE — animated shapes and animation threads

**ES1's simulator poses an animated shape and plays its animations as ES2's does.** Its animated shape instance, animation threads and sequence frame steps are the functions [`../../retail/rendering/dts-node-posing.md`](../../retail/rendering/dts-node-posing.md) describes, built by a different compiler: each ES1 function below decompiles to the same statements as its ES2 twin once the two compilers' differences are set aside, and the record layouts are ES2's. That covers the shape instance's fields, the thread's `0x52` bytes, and the animation list and sequence fields the thread reads. This page gives ES1's addresses and the differences. Addresses are `ES1DBSIM` image addresses ([`../formats/x32-executable.md`](../formats/x32-executable.md#the-image)).

## Differences from ES2

**The frame steps sit one vtable slot further on.** An ES1 vtable opens with a zero dword ([`../formats/compiler.md`](../formats/compiler.md#vtables)), so a sequence's next-frame, previous-frame and two short-of-target steps are its slots `+0x24`, `+0x28`, `+0x2c` and `+0x30`, where ES2's are `+0x20`–`+0x2c`. `ANSequence`'s vtable is `000ad96a` and `ANCyclicSequence`'s `000ada36`; the functions that store them are in the persistence code at `0008e554`–`0008ec90`.

**Every function but the blend takes its arguments in reverse.** The sequence, thread and shape-instance functions all return with `ret N`, removing their own arguments, so they read them in the reverse of ES2's order, `this` still first ([calling conventions](../formats/compiler.md#calling-conventions)): `AnimThread_SetSequence` reads `(thread, offset, frame, sequence)`. `Anim_BlendKeyframeTransforms` is a C function, takes ES2's order, and takes its Q10 factor as an int16.

**The constructors allocate for themselves.** Given a null `this`, `ShapeInst_Construct` allocates `0x42` bytes and `AnimThread_Ctor` `0x52`. The per-node arrays are built through `__vec_new` with the element constructors `maybe_Point3SPair_Ctor` and `Transform_Ctor`, which inline their members' constructors ([constructors and destructors](../formats/compiler.md#constructors-and-destructors)).

**A seek rounds where ES2's truncates.** `AnimThread_SeekToPosition` scales the position with ES1's `Math_Q14Multiply`, which rounds to nearest where ES2's truncates ([the primitives](../simulation/fixed-point-math.md#the-primitives)), so a seek can land one animation tick further into the sequence than ES2's does for the same position.

**The shape renders inline three of ES2's calls.** `TSShape_Render` (`00062fcc`) and `TSShapeInstance_Render` (`00063148`) store the cell frames to `000bd912`, read the render context from `000bdbda` and swap the active bitmap array at `000bdb5e` inline, where ES2's call `ShapeRender_BindCellFrames`, `Raster_GetRenderContext` and the bitmap-array accessors ([drawing a shape](../../retail/rendering/dts-node-posing.md#drawing-a-shape)). They call `00052850`, `000529bc` and `00052e18` where ES2's call `ShapeInst_BindNodeTransformArray`, `TSBounds_ScreenVisibility` and `TSPartList_Render`; the last two are among the part functions [`../formats/dts-shape.md`](../formats/dts-shape.md#open) lists. Their test runs while the byte `00097fe8` is set, which is 1 in the image; a grep of the disassembly for its address finds only these two reads.

**ES1's shape-instance constructor fills no static transform.** ES2's also sets a static transform at `DBSIM 006c572c` to the identity on every construction; ES1's sets only the instance's world slot 0.

## Functions

| ES1 | Address | Notes |
|---|---|---|
| `ANSequence_NextFrame`, `ANSequence_PrevFrame` | `00061134`, `0006115c` | |
| `ANSequence_FrameShortOfTarget`, `ANSequence_FrameShortOfTargetBackward` | `00061174`, `0006119c` | |
| `ANCyclicSequence_NextFrame`, `ANCyclicSequence_PrevFrame` | `000611c4`, `000611ec` | |
| `ANCyclicSequence_FrameShortOfTarget_Always`, `ANCyclicSequence_FrameShortOfTargetBackward_Always` | `00061214`, `0006121c` | |
| `ShapeInst_Construct`, `ANShapeInstance_Dtor` | `00061224`, `00061460` | the vtable `00097fbe`, its slot `+0x20` `ANShapeInstance_Render` |
| `maybe_Point3SPair_Ctor`, `Transform_Ctor` | `00061374`, `000613ec` | the element constructors of the local and world arrays |
| `ShapeInst_CountThreads`, `ShapeInst_SortThreadsByPriority` | `000614a8`, `000614d4` | |
| `ShapeInst_RegisterThread`, `ShapeInst_RemoveThread` | `00061560`, `000615b4` | |
| `AnimThread_StepAll`, `ShapeInst_EvalAllNodeLocals` | `00061634`, `0006168c` | |
| `ShapeInst_ReadRootTransform`, `ShapeInst_SeedRootTransform` | `000616e4`, `00061724` | |
| `ShapeInst_ComposeChildTransform`, `ShapeInst_ExpandRootTransform`, `ShapeInst_BuildWorldTransforms` | `00061754`, `000617bc`, `00061810` | |
| `ShapeInstance_StepAnimation` | `00061944` | |
| `ShapeInst_PushModelTransform`, `ShapeInst_PopModelTransform`, `ANShapeInstance_Render` | `00061968`, `00061998`, `000619b0` | |
| `AnimThread_Ctor`, `AnimThread_SequencePriority`, `AnimThread_ElapsedTicks` | `000619f0`, `00061b1c`, `00061b40` | |
| `AnimThread_LoadFrameGroundMovement`, `AnimThread_CommitGroundMovement`, `AnimThread_UncommitGroundMovement` | `00061bc8`, `00061c4c`, `00061cd4` | |
| `AnimThread_ReadRootTransform`, `AnimThread_WriteRootTransform` | `00061da8`, `00061eb8` | |
| `AnimThread_SetSequence`, `AnimThread_SeekToPosition` | `0006200c`, `000620c4` | |
| `AnimThread_FindTransition`, `AnimThread_SetTarget`, `AnimThread_SetPlaybackRate` | `000621ac`, `0006265c`, `000626c8` | |
| `AnimThread_Advance`, `AnimThread_EvalNodeLocals` | `00062718`, `00062dfc` | |
| `Anim_BlendKeyframeTransforms` | `000763ec` | |
| `TSShape_Render` | `00062fcc` | `TSShape`'s vtable `00097fee` slot `+0x20` ([`../formats/dts-shape.md`](../formats/dts-shape.md)) |
| `TSShapeInstance_Dtor`, `TSShapeInstance_Render` | `00063110`, `00063148` | the vtable `00098072`; `ANShapeInstance_Dtor` and `ANShapeInstance_Render` call them as their base's |

## Open

- **Open:** no caller of `ShapeInst_ReadRootTransform` or `AnimThread_ElapsedTicks` found by `es2_xref.py`.
- **Open:** what the byte `00097fe8` is for, as for ES2's.
- **Open:** the callers in ES1's game objects, `00001da0`–`0000205c`, which construct the instance and its threads and step them.
