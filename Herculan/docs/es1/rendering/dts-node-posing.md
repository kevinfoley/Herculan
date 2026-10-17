# DBSIM.EXE — animated shapes and animation threads

**ES1's simulator poses an animated shape and plays its animations as ES2's does.** Its animated shape instance, animation threads and sequence frame steps are the functions [`../../retail/rendering/dts-node-posing.md`](../../retail/rendering/dts-node-posing.md) describes, built by a different compiler: each ES1 function below decompiles to the same statements as its ES2 twin once the two compilers' differences are set aside, and the record layouts are ES2's. That covers the shape instance's fields, the thread's `0x52` bytes, and the animation list and sequence fields the thread reads. This page gives ES1's addresses and the differences. Addresses are `ES1DBSIM` image addresses ([`../formats/x32-executable.md`](../formats/x32-executable.md#the-image)).

## Differences from ES2

**The frame steps sit one vtable slot further on.** An ES1 vtable opens with a zero dword ([`../formats/compiler.md`](../formats/compiler.md#vtables)), so a sequence's next-frame, previous-frame and two short-of-target steps are its slots `+0x24`, `+0x28`, `+0x2c` and `+0x30`, where ES2's are `+0x20`–`+0x2c`. `ANSequence`'s vtable is `000ad96a` and `ANCyclicSequence`'s `000ada36`; the functions that store them are in the persistence code at `0008e554`–`0008ec90`.

**Every function but the blend takes its arguments in reverse.** The sequence, thread and shape-instance functions all return with `ret N`, removing their own arguments, so they read them in the reverse of ES2's order, `this` still first ([calling conventions](../formats/compiler.md#calling-conventions)): `AnimThread_SetSequence` reads `(thread, offset, frame, sequence)`. `Anim_BlendKeyframeTransforms` is a C function, takes ES2's order, and takes its Q10 factor as an int16.

**The constructors allocate for themselves.** Given a null `this`, `ShapeInst_Construct` allocates `0x42` bytes and `AnimThread_Ctor` `0x52`. The per-node arrays are built through `__vec_new` with the element constructors `maybe_Point3SPair_Ctor` and `Transform_Ctor`, which inline their members' constructors ([constructors and destructors](../formats/compiler.md#constructors-and-destructors)).

**A seek rounds where ES2's truncates.** `AnimThread_SeekToPosition` scales the position with ES1's `Math_Q14Multiply`, which rounds to nearest where ES2's truncates ([the primitives](../simulation/fixed-point-math.md#the-primitives)), so a seek can land one animation tick further into the sequence than ES2's does for the same position.

**The shape renders inline three of ES2's calls.** `TSShape_Render` (`00062fcc`) and `TSShapeInstance_Render` (`00063148`) store the cell frames to `000bd912`, read the render context from `000bdbda` and swap the active bitmap array at `000bdb5e` inline, where ES2's call `ShapeRender_BindCellFrames`, `Raster_GetRenderContext` and the bitmap-array accessors ([drawing a shape](../../retail/rendering/dts-node-posing.md#drawing-a-shape)). The calls they keep are ES2's: `ShapeInst_BindNodeTransformArray` (`00052850`), `TSBounds_ScreenVisibility` (`000529bc`) and `TSPartList_Render` (`00052e18`, [drawing parts](../formats/dts-shape.md#drawing-and-finding-parts)). Their screen test runs while the byte `00097fe8` is set, which is 1 in the image; a grep of the disassembly for its address finds only these two reads.

**A node's transform is installed as ES2's does it** ([the draw path](../../retail/rendering/dts-node-posing.md#the-draw-path)): every part's draw starts with `TSGroup_BindNodeTransform` (`00052890`), which passes the part's transform id to `ShapeNode_InstallTransform` (`000528ac`), and the two take their arguments in reverse, `(part, out)` and `(part, id, out)` in stack order. `ShapeNode_InstallTransform` stores at `out + 0x20` the state slot installed before it, or −1 when it changes nothing. The globals are `g_ShapeNodeWorldTransforms` (`000bd90e`), the flag block `g_ShapeNodeTransformCacheFlags` (`000bd916`, the flag for node *id* at `000bd917` + *id*), the installed slot `000bd948` and `g_ShapeObjectModelTransformPtr` (`000b1f8c`), slot 0's `+0x20`. `TSBounds_ScreenVisibility` keeps its width and factor at `000960a0` and `000b56b8`.

**ES1's shape-instance constructor fills no static transform.** ES2's also sets a static transform at `DBSIM 006c572c` to the identity on every construction; ES1's sets only the instance's world slot 0.

## The transform states

`g_TSTransformStates` (`000b1f6c`) is ES2's 52 states of `0x110` bytes, slot 0 the object's and slot *id* + 1 node *id*'s. `TSTransformState_StaticInit` (`000533a8`, static constructor table entry `000adc6c`) builds them with `__vec_ctor` and `TSTransformState_Ctor` (`000533d8`, passed as its padding entry `000533d7`), which allocates for itself and builds the state's ten `TSContext`s at `+0x34` with `__vec_ctor`; ES2's builds them with `Rtl_VectorNew`. `TSTransformState_StaticDtor` (`000533c0`, exit table entry `000adccc`) destroys them with `__vec_dtor` and `TSTransformState_Dtor` (`00052814`), which destroys the ten contexts through `TSTransformState_ContextDtor` (`00052844`), a jump to slot `+4` of the vtable at each context's `+0x12`. The initializer and its exit routine stand in the middle of their code, not at its end ([where the object files start and end](../formats/dts-shape.md#where-the-object-files-start-and-end)).

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
| `ShapeInst_BindNodeTransformArray`, `TSGroup_BindNodeTransform`, `ShapeNode_InstallTransform` | `00052850`, `00052890`, `000528ac` | ES2's `00475fd8`, `00476014`, `00476030`; `ES1GO` `000592d8`, `00059318`, `00059334` |
| `TSBounds_ScreenVisibility` | `000529bc` | ES2's `0047613c` |
| `TSTransformState_Dtor`, `TSTransformState_ContextDtor` | `00052814`, `00052844` | `TSTransformState_ContextDtor` has no Ghidra function |
| `TSTransformState_StaticInit`, `TSTransformState_StaticDtor`, `TSTransformState_Ctor` | `000533a8`, `000533c0`, `000533d8` | ES2's `00476b64`, `00476ba4`, `00476bc0`; the first two have no Ghidra function; `ES1GO` has the destructor and constructor at `0005929c` and `00059ddc` |

## Open

- **Open:** no caller of `ShapeInst_ReadRootTransform` or `AnimThread_ElapsedTicks` found by `es2_xref.py`.
- **Open:** what the byte `00097fe8` is for, as for ES2's.
- **Open:** the callers in ES1's game objects, `00001da0`–`0000205c`, which construct the instance and its threads and step them.
