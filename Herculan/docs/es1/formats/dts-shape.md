# DBSIM.EXE — shape chunks and shape files

**ES1's simulator loads and writes a shape's chunks, and reads shape and palette files, as ES2's does.** Its type registry, `g_TSObjectTypeRegistry` (`0009efa8`), holds ES2's 18 entries in ES2's order, with the same tags and names, followed by the same count, and `Light_ResetSystem` (`000632ec`) registers it with `ClassItem_RegisterBucket` (`0005513c`) ([chunk tags](../../retail/formats/dts-shape.md#chunk-tags)). The part classes' load, read, write, size and debug-print functions, their constructors and their destructors decompile to the same statements as ES2's once the two compilers' differences are set aside, so a part chunk has ES2's layout and its object ES2's fields at ES2's offsets ([part chunks](../../retail/formats/dts-shape.md#part-chunks)). The same holds for `TSShape`, the chunk at a shape file's root, and for the `libio.cpp` loaders that read a file's chunks ([loading a file](../../retail/formats/dts-shape.md#loading-a-file)). This page gives ES1's addresses and the differences. Addresses are `ES1DBSIM` image addresses ([`x32-executable.md`](x32-executable.md#the-image)).

## Differences from ES2

**Every slot is one place later.** An ES1 vtable's slots start at `+4` ([vtables](compiler.md#vtables)), so a part's vtable holds its tag at `+4`, its body length at `+8`, its memory size at `+0xc`, then its read, write, debug print, destructor and draw functions at `+0x10` to `+0x20`, and its two find functions at `+0x24` and `+0x28`. The vptr is at `+0`, as in ES2's classes, since the root class holds no data.

**Arguments come in the reverse order.** The functions remove their own arguments ([calling conventions](compiler.md#calling-conventions)), and each takes ES2's arguments reversed after `this`: the load functions `(mode, stream)` and the read and write slots `(this, mode, stream)` in stack order, the debug prints `(this, indent, depth)`, and `ClassItem_DebugPrintType` `(item, indent, unused)`.

**The reads and writes call the stream's slots directly**, `+0x1c` to read and `+0x18` to write ([the base class](streams.md#the-base-class)), where ES2's call small wrappers around them; the bytes read and written are the same.

**A constructor allocates its object when it is given none** ([constructors and destructors](compiler.md#constructors-and-destructors)).

**The BSP nodes and a shape's node transforms are built as counted arrays and used as plain ones.** `TSBSPPart_ReadFromStream` (`0005e224`) takes 14 bytes per node plus 4 from the current arena (`000bd954`) and builds the nodes in it with `__vec_new` and `TSBSPPart_NodeCtor` (`0005e2f0`), which stores the count in the block's first dword and the nodes after it. It stores the block itself at `+0x18`, not the address `__vec_new` returns, and reads the nodes over the block from its start, so node 0 overwrites the count and the last 4 bytes go unused. `TSBSPPart_Dtor` (`000531f8`) passes `+0x18` to `__vec_delete` (`00061088`) with no destructor and flags 7, which calls `operator_delete` on the address 4 bytes before the block. The memory size counts 16 bytes per node. `TSShape_ReadFromStream` (`00065d04`) does the same with its node transforms at `+0x18`, 32 bytes each plus 4, built with `Transform_CtorCopy` (`00065e18`, entered through its padding at `00065e16`), and `TSShape_Dtor` (`00062f7c`) frees them the same way; the memory size counts 32 bytes per transform.

**An arena takes the heap's largest free block.** `Arena_Push` (`000634b0`) takes no size: it allocates the whole of the heap's largest free block (`Heap_LargestFree`, `00000e88`, then `Heap_Alloc`, which zero-fills it; [the heap](../simulation/memory.md#the-heap)) and pushes it. `Arena_Pop` (`000634d8`) gives back what the loads did not use with `Heap_Realloc` before popping. So where ES2's `Shape_LoadDts`, `Shape_LoadAllRoots` and `BaseType_ResolveShape` load into 200,000 or 500,000 bytes, ES1's have the heap's largest free block, and there is no heap byte count to correct. The stack is at `000b5ae0`, its depth at `000962c0`.

**Popping the last arena leaves none.** `ArenaStack_Pop` (`000554bc`) tests the depth with a signed test and stores null in the current arena when it has gone negative, so a load function's `Arena_Alloc` then falls back to `operator_new`. ES2's branch for that can never run ([arenas](../../retail/formats/dts-shape.md#arenas)).

**`TSPartBase` has three vtables and three destructors**, as `TSBase` has eleven vtables ([vtables](compiler.md#vtables)). `000974aa`, which the load functions install, holds `TSPartBase_Dtor` (`0005d614`) in its destructor slot; `000960f6`, which `TSPartList_Ctor` (`00052cc0`) installs, holds `TSPartBase_DtorCopy` (`00052d68`), and `0009621a`, which `TSGroup_Ctor` (`000547b4`) and `TSGroup_Dtor` (`00054888`) install, `TSPartBase_DtorGroupCopy` (`0005485c`), both the same code. Their other slots hold the same functions, `000974aa`'s tag slot entering through the padding before `TSPartBase_GetClassTag`. The load functions install `TSBase`'s `0009747e`, and `TSPartList_Ctor` its `000960ca`.

## Drawing and finding parts

**The parts draw and search as ES2's do, through slots one place later**: draw `+0x20`, find `+0x24`, find a slot `+0x28` ([the draw path](../../retail/rendering/dts-node-posing.md#the-draw-path), [poly order](../../retail/rendering/dts-texture-binding.md#poly-order-within-a-group)). Each draw installs the part's own node first (`TSGroup_BindNodeTransform`, [node transforms](../rendering/dts-node-posing.md#differences-from-es2)) and ends with `Render_NoOp` (`0005298c`, `RET 8`).

| Class | ES1 | ES2 (DBSIM) | Difference |
|---|---|---|---|
| `TSPartList` | `TSPartList_Render` (`00052e18`), `TSPartList_FindPart` (`00052e88`), `TSPartList_FindPartSlot` (`00052f0c`) | `004766fc`, `00476744`, `00476784` | none |
| `TSCellAnimPart` | `TSCellAnimPart_Render` (`00052fd0`) | `004767e4` | the cell frames at `000bd912` |
| `TSDetailPart` | `TSDetailPart_Render` (`000530b0`) | `004768bc` | none; `g_TSDetailPartSizeScaleQ10` is the `int16` at `0009615a`, `0x400` in the image, set only by `TSDetailPart_SetSizeScale` (`00053094`, no reference found by `es2_xref.py`), and `g_TSDetailPartBias` the dword at `0009615c`, which four draws set around themselves ([the detail setting](../simulation/object-classes.md#the-detail-setting--shapedetail_applydetailsetting-00057c08), [level selection](../../retail/rendering/dts-texture-binding.md#tsdetailpart-level-selection-and-structure-detail)) |
| `TSBSPPart` | `TSBSPPart_Render` (`00053364`), `TSBSPPart_RenderNode` (`00053240`) | `00476b0c`, `00476a1c` | no depth-buffer flag, below |
| `TSBitmapPart` | `TSBitmapPart_Render` (`00052b90`) | `004762e8` | upright and unsquashed, below |

**A BSP part's walk has no depth-buffer flag.** ES2's chooses the order of a node's two sides by whether the eye is behind its plane, compared with `maybe_g_DepthBufferEnabled`; ES1's has only the comparison, and takes the front entry (`+0xa`) first when the eye is behind the plane and the back (`+0xc`) first otherwise, ES2's order with the flag at its image value of 0 ([child selection](../../retail/rendering/dts-texture-binding.md#tsbsppart-child-selection)). The eye is copied to `000b56ac`, and a node with a transform is tested through `Transform_LocalPointDotNormal` (`000733c0`), ES2's (`004910b4`).

**A bitmap part is drawn upright and unsquashed, and not at all once it would be 200 pixels wide or high.** ES1's `TSBitmapPart_Render` takes the bitmap and the scale *s* as ES2's does ([the billboard](../../retail/rendering/dts-billboards.md#tsbitmappart_render-004762e8)), and then draws it with `Bitmap_BlitScaled` (`00067178`), flags 0, at {`Math_Q8Multiply`(*s*, columns), `Math_Q8Multiply`(*s*, rows)}, its top-left corner the projected centre less `Math_Q8Multiply`(*s*, x offset) and `Math_Q8Multiply`(*s*, y offset), both offsets read signed. The blit writes only a bitmap's non-zero bytes, so palette index 0 is transparent ([the simulator's scaled blit](../rendering/screen-pages.md#the-simulator)). It draws only when both sizes are below 200. It neither turns the bitmap with the model's Z axis nor shortens it as that axis points at the viewer, reads the y offset signed where ES2's reads it unsigned, and has no test of the divided centre against ±1000; a null bitmap returns without `Render_NoOp`.

## Chunk headers

The chunk framing is ES2's ([chunk headers](../../retail/formats/dts-shape.md#chunk-headers)), calling the stream's slots directly as the class reads do ([above](#differences-from-es2)) and taking the arguments in reverse: `ClassItem_ReadTypeTag` (`00055754`) `(tag, stream)`, `ClassItem_OpenItemForWrite` (`000556cc`) `(length, tag, stream)`, `ClassItem_TryEveryLoader` (`00055568`) `(tag, stream)`, which passes a probe `(stream, tag, header)`. The registry's buckets are at `000bd958` (count `000962b4`), the probe buckets at `000bd9f8` (count `000962b8`), `ClassItem_ProbeHeaderSize` at `000962bc` and `ClassItem_ProbeHeader` at `000bda48`; `es2_xref.py` finds no reference to `ClassItem_RegisterProbeBucket` (`00055170`). `ArenaStack_PushAlloc` (`00055468`), with no reference found either, carves its block with `Arena_Alloc` where ES2's calls `Arena_AllocDup`.

## Where the object files start and end

**`00052814`–`0005513b`**, between the zero fill after `VGA8_SetPageOrigin` ([the page routines](../rendering/screen-pages.md#the-simulators-own-page-routines)) and the two zero bytes after `TSAliasGouraudPoly_Render`'s `RET 4` at `00055137`, holds in order: the part draws and finds and the transform states (`00052814`–`000534ab`), with the static initializer and exit routine at `000533a8` and `000533c0` before `TSTransformState_Ctor`; `VGA_8`'s bitmap fill and `Raster_FillRect` (`000534ac`–`00054033`); `TSNullObj_Render` and `TSPoly_Render` (`00054034`, `00054038`, each a `RET 4`); the poly renders, `TSGroup` and `TSBSPGroup`, and the `TSAlias*Poly` renders ([poly types](../rendering/polygon-shading.md#the-four-poly-types)). ES2's counterpart, `DBSIM 004758c8`–`00476bc0`, runs from the group renders to `TSTransformState_Ctor`. The code holds two copies of `TSPartBase`'s table, `000960f6` and `0009621a`, with no zero fill between their users, so whether it is one object file is [Open](#open).

**`0005513c`–`0005617f`**, from the zero bytes after `00055137` to the three after `Volume_FindEntry`'s `RET 0xc` at `0005617a`, holds the class-item and arena functions (`0005513c`–`00055977`), [the mouse driver](../simulation/input.md#the-mouse-driver) (`00055978`–`00055d1f`) and the volume class (`00055d20`–`0005617f`, [streams](streams.md#the-volume-class)).

## Functions

| ES1 | Address | Notes |
|---|---|---|
| `TSPartBase_Construct` | `0005d564` | |
| `TSPartBase_Dtor`, `TSPartBase_DtorCopy` | `0005d614`, `00052d68` | |
| `TSPartBase_GetClassTag`, `TSPartBase_GetStreamSize`, `TSPartBase_GetMemorySize` | `0005d640`, `0005d648`, `0005d650` | |
| `TSPartBase_ReadFromStream`, `TSPartBase_WriteToStream`, `TSPartBase_DebugPrint` | `0005d658`, `0005d6c8`, `0005d73c` | |
| `TSPartBase_FindPart`, `TSPartBase_FindPartSlot` | `00052990`, `000529b4` | |
| `TSBitmapPart_Construct`, `TSBitmapPart_Dtor` | `0005d840`, `0005d918` | |
| `TSBitmapPart_DebugPrint`, `TSBitmapPart_ReadFromStream`, `TSBitmapPart_WriteToStream` | `0005d948`, `0005d9d8`, `0005da38` | |
| `TSBitmapPart_GetMemorySize`, `TSBitmapPart_GetClassTag`, `TSBitmapPart_GetStreamSize` | `0005daa4`, `0005daac`, `0005dab4` | |
| `TSPartList_Construct`, `TSPartList_Ctor`, `TSPartList_Dtor` | `0005dac8`, `00052cc0`, `00052d94` | |
| `TSPartList_GetClassTag`, `TSPartList_GetStreamSize`, `TSPartList_GetMemorySize` | `0005db00`, `0005db08`, `0005db70` | `TSPartList_GetStreamSize` keeps `this` in `ECX` across its call to `TSPartBase_GetStreamSize`, which leaves `ECX` alone; the decompile shows the reads as `extraout_ECX` |
| `TSPartList_ReadFromStream`, `TSPartList_WriteToStream`, `TSPartList_DebugPrint` | `0005dbcc`, `0005dc58`, `0005dccc` | |
| `TSCellAnimPart_Construct`, `TSCellAnimPart_Dtor` | `0005dd7c`, `0005dde0` | |
| `TSCellAnimPart_GetClassTag`, `TSCellAnimPart_GetStreamSize`, `TSCellAnimPart_GetMemorySize` | `0005de0c`, `0005de14`, `0005de30` | |
| `TSCellAnimPart_ReadFromStream`, `TSCellAnimPart_WriteToStream`, `TSCellAnimPart_DebugPrint` | `0005de8c`, `0005deb8`, `0005def0` | |
| `TSDetailPart_Construct`, `TSDetailPart_Ctor`, `TSDetailPart_Dtor` | `0005df44`, `00053028`, `0005305c` | |
| `TSDetailPart_GetClassTag`, `TSDetailPart_GetStreamSize`, `TSDetailPart_GetMemorySize` | `0005df7c`, `0005df84`, `0005dfb0` | |
| `TSDetailPart_ReadFromStream`, `TSDetailPart_WriteToStream`, `TSDetailPart_DebugPrint` | `0005e018`, `0005e070`, `0005e0b0` | |
| `TSBSPPart_Construct`, `TSBSPPart_Ctor`, `TSBSPPart_Dtor` | `0005e144`, `000531bc`, `000531f8` | |
| `TSBSPPart_GetClassTag`, `TSBSPPart_GetStreamSize`, `TSBSPPart_GetMemorySize` | `0005e17c`, `0005e184`, `0005e1b8` | |
| `TSBSPPart_ReadFromStream`, `TSBSPPart_NodeCtor`, `TSBSPPart_WriteToStream`, `TSBSPPart_DebugPrint` | `0005e224`, `0005e2f0`, `0005e338`, `0005e3a8` | |
| `TSShape_Construct`, `TSShape_Ctor`, `TSShape_Dtor` | `00065c18`, `00062f44`, `00062f7c` | the vtable `00097fee`; `0001fe6c` and `0008f51c` also call the constructor |
| `TSShape_GetClassTag`, `TSShape_GetStreamSize`, `TSShape_GetMemorySize` | `00065c50`, `00065c58`, `00065c90` | `TSShape_GetClassTag` has no Ghidra function |
| `TSShape_ReadFromStream`, `TSShape_WriteToStream`, `TSShape_DebugPrint` | `00065d04`, `00065e8c`, `00065f1c` | |
| `Transform_CtorCopy` | `00065e18` | `Transform_Ctor`'s code again |
| `TSBase_GetClassTag`, `TSBase_GetStreamSize`, `TSBase_GetMemorySize`, `TSBase_ReadFromStream`, `TSBase_WriteToStream` | `00063214`, `0006321c`, `00063224`, `0006322c`, `00063230` | slots `+4` to `+0x14` of `TSBase`'s vtables; frameless, with no Ghidra function |
| `TSBase_Render` | `0005ba80` | `ret 4`, with no Ghidra function |
| `Palette_LoadFromFile`, `ClassItem_LoadAfterPeek` | `00063448`, `000634f8` | `Palette_LoadFromFile` takes `(tag, path)` and returns the caller's `ESI` for a tag it does not load, where ES2's returns `EBX` |
| `Shape_LoadDts`, `Shape_LoadDtsCopy`, `Palette_LoadDpl` | `0006351c`, `00063550`, `00063584` | |
| `Shape_LoadAllRoots`, `BaseType_ResolveShape` | `000635a8`, `000636a4` | `(skip, tag, path, out)` and `(tag, index, path)`; the assert names `C:\P\M\s\util\libio.cpp`, lines `0x77` and `0x97` |
| `Arena_Push`, `Arena_Pop`, `ArenaStack_Push`, `ArenaStack_Pop` | `000634b0`, `000634d8`, `00055424`, `000554bc` | |
| `ClassItem_PeekTypeTag`, `ClassItem_Skip` | `0005533c`, `000553e4` | |
| `ClassItem_RegisterBucket` | `0005513c` | the buckets at `000bd958`, their count at `000962b4` |
| `ClassItem_StoreItem`, `ClassItem_ItemTotalSize`, `ClassItem_DebugPrintType` | `00055248`, `00055610`, `00055648` | |
| `__vec_delete` | `00061088` | the Symantec runtime's, from `vecnew.cpp` |
| `Render_NoOp`, `TSBitmapPart_Render` | `0005298c`, `00052b90` | [drawing parts](#drawing-and-finding-parts) |
| `TSPartList_Render`, `TSPartList_FindPart`, `TSPartList_FindPartSlot`, `TSCellAnimPart_Render` | `00052e18`, `00052e88`, `00052f0c`, `00052fd0` | `ES1GO` `0005980c`, `0005987c`, `00059900`, `000599c4` |
| `TSDetailPart_SetSizeScale`, `TSDetailPart_Render` | `00053094`, `000530b0` | `TSDetailPart_SetSizeScale` has no Ghidra function |
| `TSBSPPart_RenderNode`, `TSBSPPart_Render`, `Transform_LocalPointDotNormal` | `00053240`, `00053364`, `000733c0` | `ES1GO` `00059c44`, `00059d68` for the first two |
| `TSPartBase_DtorGroupCopy` | `0005485c` | |
| `ClassItem_RegisterProbeBucket`, `ArenaStack_PushAlloc`, `ClassItem_FindHandler`, `ClassItem_TryEveryLoader` | `00055170`, `00055468`, `000554e4`, `00055568` | [chunk headers](#chunk-headers); the last two entered also at their padding, `000554e2` and `00055567` |
| `ClassItem_OpenItemForWrite`, `ClassItem_ReadTypeTag`, `ClassItem_CloseItemForWrite`, `ClassItem_CloseItem` | `000556cc`, `00055754`, `00055860`, `00055920` | |

## Globals

| ES1 | Address | ES2 (DBSIM) |
|---|---|---|
| `g_TSObjectTypeRegistry`, its count | `0009efa8`, `0009f080` | `004a63c8`, `004a64a0` |
| the current arena | `000bd954` | `006c58b4` |

## Open

- **Open:** how many `TSBSPPart` and `TSBSPGroup` chunks ES1's volumes hold, which decides whether the walk's difference from ES2's shows: neither `HercWorks.Query` nor `HercWorks.Core` lists shape chunks by class.
- **Open:** what a bitmap part looks like in ES1 from close by, where ES2's would be drawn and ES1's is not once it reaches 200 pixels; a retail check is a weapon's impact or an explosion sprite seen from a few meters.
- **Open:** whether `00052814`–`0005513b` is one object file, with two copies of `TSPartBase`'s table, and whether `0005513c`–`0005617f` is one, the class items, the hand-written mouse code and the volume class; no assert names a source file in either.
- **Open:** the `ClassItem` vtable `0009745a`, which the load functions store before `TSBase`'s.
- **Open:** what `operator_delete` does with the address `TSBSPPart_Dtor` and `TSShape_Dtor` pass it, 4 bytes before an arena block, and whether a loaded `TSBSPPart` or `TSShape` is ever destroyed.
- **Open:** no reference to `Shape_LoadDtsCopy` found by `es2_xref.py`.
- **Open:** the classes whose constructors `0001fe6c` (vtable `000923da`, which shares `TSShape`'s debug print and draw) and `0008f51c` call `TSShape_Ctor` from.
