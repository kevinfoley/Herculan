# `dts\<name>.DTS` — shapes

A `.DTS` holds a model's shape roots, each a tree of chunked objects — part lists, groups of polys, bitmap and cell-animation parts — whose chunk tags each executable's type registry maps to a class. `TSShape_ReadFromStream` (`00490d5c`) reads a shape's own fields. A `.DTS` carries no reference to any texture file. How the objects are drawn is [`../rendering/dts-texture-binding.md`](../rendering/dts-texture-binding.md), [`../rendering/dts-node-posing.md`](../rendering/dts-node-posing.md) and [`../rendering/dts-billboards.md`](../rendering/dts-billboards.md), and for a machine [`../rendering/mech-shape-drawing.md`](../rendering/mech-shape-drawing.md); the `.DGS` structure libraries are [`dgs-hd0-notes.md`](dgs-hd0-notes.md). Addresses are DBSIM virtual addresses unless VSHELL is named.

## Chunk tags

An object's on-disk chunk header is its tag, `[subtype:u16][supertype:u16]` (e.g. `0x0014000f` = `TSTexture4Poly`). DBSIM's type registry, `g_TSObjectTypeRegistry` (`004a63c8`), maps each tag to the function that loads its class: 18 entries of 12 bytes, `{tag, load function, name}`, followed by their count. `Light_ResetSystem` (`0048dbfc`) registers the table with `ClassItem_RegisterBucket` (`00479fac`), and `ClassItem_LoadResource` (`0047a038`) calls the load function whose tag a chunk carries. In the table's order:

| Tag | Name | Load function |
|---|---|---|
| `0x00140001` | `TSPoly` | `TSPoly_Construct` (`0048de58`) |
| `0x00140002` | `TSSolidPoly` | `TSSolidPoly_Construct` (`0048e06c`) |
| `0x00140003` | `TSShadePoly` | `TSShadedPoly_Construct` (`0048e2d4`) |
| `0x00140014` | `TSGroup` | `TSGroup_Construct` (`0048e7a4`) |
| `0x00140005` | `TSBasePart` | `TSPartBase_Construct` (`0048f7d0`) |
| `0x00140007` | `TSPartList` | `TSPartList_Construct` (`0048fc8c`) |
| `0x00140008` | `TSShape` | `TSShape_Construct` (`00490c5c`) |
| `0x00140009` | `TSGouraudPoly` | `TSGouraudPoly_Construct` (`0048e398`) |
| `0x0014000a` | `TSBSPGroup` | `TSBSPGroup_Construct` (`0048ed04`) |
| `0x0014000b` | `TSCellAnimPart` | `TSCellAnimPart_Construct` (`0048fee8`) |
| `0x0014000c` | `TSDetailPart` | `TSDetailPart_Construct` (`00490094`) |
| `0x00140015` | `TSBSPPart` | `TSBSPPart_Construct` (`0049027c`) |
| `0x0014000e` | `TSNullObj` | `TSNullObj_Construct` (`0048dd94`) |
| `0x0014000f` | `TSTexture4Poly` | `TSTexture4Poly_Construct` (`0048e210`) |
| `0x00140010` | `TSAliasSolidPoly` | `TSAliasSolidPoly_Construct` (`0048e540`) |
| `0x00140011` | `TSAliasShadedPoly` | `TSAliasShadedPoly_Construct` (`0048e604`) |
| `0x00140012` | `TSAliasGouraudPoly` | `TSAliasGouraudPoly_Construct` (`0048e6d4`) |
| `0x00140013` | `TSBitmapPart` | `TSBitmapPart_Construct` (`0048fa68`) |

A load function takes the two arguments `ClassItem_LoadResource` passes it, takes the object's size from the current arena (`006c58b4`) with `Arena_Alloc`, runs the class's constructor, and calls the object's read slot with the same two arguments. The vtables and draw functions of the poly classes are tabled in [`../rendering/dts-texture-binding.md`](../rendering/dts-texture-binding.md#poly-types-and-their-colour-mechanisms-dbsimexe), and those of `TSBitmapPart` and `TSCellAnimPart` in [`../rendering/dts-billboards.md`](../rendering/dts-billboards.md#class-identification).

## Part chunks

The part classes form one hierarchy. `TSBasePart` is the head of every part; `TSBitmapPart` and `TSPartList` extend it, and `TSCellAnimPart`, `TSDetailPart`, `TSBSPPart` and `TSShape`, the chunk a shape file's root is, extend `TSPartList`. Each class's read slot calls its base's and then reads its own fields, so a chunk's body is its base's body followed by the fields below, little-endian and unpadded:

| Class | Fields after the base's | In memory |
|---|---|---|
| `TSBasePart` | `int16` transform, `int16` id number, `int16` radius, `int16[3]` centre | `+4`, `+6`, `+8`, `+0xa`; `0x10` bytes, the vtable at `+0` |
| `TSBitmapPart` | `int16` bitmap, `int8` x offset, `int8` y offset | `+0x10`, `+0x12`, `+0x13`; `0x14` bytes |
| `TSPartList` | `uint16` child count, then that many child chunks | the count at `+0x10`, a pointer array at `+0x12`; `0x16` bytes |
| `TSCellAnimPart` | `int16` animation sequence | `+0x16`; `0x18` bytes |
| `TSDetailPart` | one `int16` size threshold per child | a pointer to the thresholds at `+0x16`; `0x1a` bytes |
| `TSBSPPart` | `uint16` node count, the nodes at 14 bytes each, then one `int16` transform id per node | the count at `+0x16`, the nodes at `+0x18`, the transform ids at `+0x1c`; `0x20` bytes |
| `TSShape` | `uint16` node transform count, `uint16` sequence count, one `uint16` frame count per sequence, then the node transforms at 32 bytes each ([below](#the-shapes-own-node-transforms)) | the node transform count at `+0x16`, the transforms at `+0x18`, a per-sequence cell frame array at `+0x1c`, the frame counts at `+0x20`, the sequence count at `+0x24`, the bound bitmap array at `+0x26`; `0x2a` bytes |

What the fields do when the part is drawn is [`../rendering/dts-node-posing.md`](../rendering/dts-node-posing.md#the-draw-path) (the transform), [`../rendering/dts-billboards.md`](../rendering/dts-billboards.md) (the bitmap, its offsets and the animation sequence) and [`../rendering/dts-texture-binding.md`](../rendering/dts-texture-binding.md#tsdetailpart-level-selection-and-structure-detail) (the thresholds, and the [node layout](../rendering/dts-texture-binding.md#tsbsppart-child-selection)). A shape's frame counts and cell frame array are the per-sequence `cellFrames` of [`../rendering/dts-billboards.md`](../rendering/dts-billboards.md#tscellanimpart_render-004767e4), and its bound bitmap array is the `.DBA` its bitmap parts draw from ([`../rendering/dts-texture-binding.md`](../rendering/dts-texture-binding.md#tsbitmapparts-texture-lookup-vshell)); how the shape is drawn is [`../rendering/dts-node-posing.md`](../rendering/dts-node-posing.md#drawing-a-shape).

`TSPartList_ReadFromStream` (`0048fd94`) takes the pointer array from the current arena and loads each child with `ClassItem_LoadResource` in mode 0, so a child is a whole chunk of any registered class, header included. `TSDetailPart_ReadFromStream` (`0049017c`) reads exactly as many thresholds as the list has children. `TSBSPPart_ReadFromStream` (`0049037c`) builds the node array with `Rtl_VectorNew` and `TSBSPPart_NodeCtor` (`00490803`) and reads the nodes over it as one block. Each array comes from the current arena. The constructors clear the counts and array pointers, `TSPartList_Ctor` (`00476604`) at `+0x10` and `+0x12`, `TSDetailPart_Ctor` (`00476834`) at `+0x16`, `TSBSPPart_Ctor` (`004769a0`) at `+0x16`, `+0x18` and `+0x1c`, `TSShape_Ctor` (`00490928`) at `+0x18`, `+0x1c` and `+0x20`. `TSShape_ReadFromStream` (`00490d5c`) takes both per-sequence arrays and the transforms from the current arena, builds the transforms with `Rtl_VectorNew` and `Transform_Ctor`, and zeroes the cell frame array once the rest is read. `TSPartList_Dtor` (`00476648`) destroys each non-null child through its destructor slot with flags 3 and frees the pointer array; `TSDetailPart_Dtor` (`0047685c`), `TSBSPPart_Dtor` (`004769d4`) and `TSShape_Dtor` (`00490958`) free their own arrays first.

Each write slot writes the same fields in the same order, and `TSPartList_WriteToStream` (`0048fe00`) writes each child with `ClassItem_StoreItem` in mode 0. The vtable gives a class's tag at slot `0`, the length of its body at `+4`, the memory it occupies at `+8`, and its read, write, debug print, destructor and draw functions at `+0xc` to `+0x1c`:

| Class | Body length (`+4`) | Memory (`+8`) |
|---|---|---|
| `TSBasePart` | 12 | `0x10` |
| `TSBitmapPart` | 16 | `0x14` |
| `TSPartList` | 14 plus each child's `ClassItem_ItemTotalSize`; 0 when any child's is 0 | `0x16`, 4 per child, and each child's |
| `TSCellAnimPart` | `TSPartList`'s plus 2, or 0 | `0x18`, 4 per child, and each child's |
| `TSDetailPart` | `TSPartList`'s plus 2 per child, or 0 | `0x1a`, 6 per child, and each child's |
| `TSBSPPart` | `TSPartList`'s plus 2 and 16 per node, or 0 | `0x20`, 4 per child, 16 per node, and each child's |
| `TSShape` | `TSPartList`'s plus 4, 2 per sequence and 32 per node transform, or 0 | `0x2a`, 4 per child, 4 per sequence, 32 per node transform, and each child's |

The debug print takes a depth and an indent: when the depth is nonzero it prints the class line (`ClassItem_DebugPrintType`, `0047a488`), one line per field, indented by the indent, and each child with the depth less 1 and the indent plus 1. `TSShape_DebugPrint` (`00490e94`) prints `transformListSize`, `seqListSize`, then `seqLenList` and each frame count on its own line, indented one further.

## Surface stride

A poly's colour index (`poly+0xc`) is on disk as `surfaceIndex * 4`, so `surfaceIndex = colourIndex / 4`. The front value is the first int32 slot of the group's surface `surfaceIndex`; the back value is the third, 2 slots (8 bytes) later.

Two independent sources agree:

- Raw disassembly of VSHELL's `TSTexture4Poly_Render` (`00422af5`): `MOVZX ESI,word ptr [EBX+0xc]` → `SHL ESI,0x2` → added to `g_ActiveSurfaceRecords` (`DAT_005d88a2`) as a byte offset; front = `*(int32*)(base+offset)`, back = `+8`.
- The file format itself: a group's on-disk colour count is four times its surface count, one per slot of each surface's four `{int16 value, int16 flag}` slots — front fill, front line, back fill, back line.

## The shape's own node transforms

A `TSShape` chunk ends, after its part list, with two counts and two arrays, which `TSShape_ReadFromStream` (`00490d5c`) reads in this order:

```
int16  nodeTransformCount
int16  sequenceCount
int16  sequenceFrameCounts[sequenceCount]          // see ../rendering/dts-billboards.md
byte   nodeTransforms[nodeTransformCount][0x20]
```

Each node transform is the 32-byte transform record `Transform_Concat` composes ([`../simulation/sim-object-layout.md`](../simulation/sim-object-layout.md#the-objects-frame-is-a-transform-and-its-position-is-that-transforms-translation)). The loader builds the array with `Transform_Ctor` and reads the bytes straight over it, so the file holds each record exactly as memory does. What a shape instance draws through them is [`../rendering/dts-node-posing.md`](../rendering/dts-node-posing.md#the-draw-path).

**No retail shape has any.** All 479 shape roots in the 55 retail `.DTS` files carry a count of 0.

## `TSBSPGroup` nodes

In the file a `TSBSPGroup`'s BSP node is four `int16`s — plane constant, poly, front, back. None of the 57 retail `.DTS` and `.DGS` files contains a `TSBSPGroup`. How the tree orders the group's polys is [`../rendering/dts-texture-binding.md`](../rendering/dts-texture-binding.md#tsbspgroup-poly-order).

## Loading a file

**A shape, a palette or a structure library is read as a run of chunks, each loaded whole into a block of memory set aside for the load.** The loaders are in `libio.cpp`; each opens the file as a stream (`VolRStream_CtorFromPath`), reads a chunk's tag without consuming it (`ClassItem_PeekTypeTag`, `0047a1fc`), and loads the chunk with `ClassItem_LoadResource` in mode 0. `ClassItem_Skip` (`0047a2a8`) passes over a chunk: it reads the 8-byte tag and seeks past the body, the tag's second dword rounded up to an even length.

| Function | Loads |
|---|---|
| `Palette_LoadFromFile` (`00474a24`), `(path, &tag)` | the first chunk, when its tag is `0x1e0003` (`ANShape`), `0x140008` (`TSShape`) or `0x28000f` (a palette), storing the tag through its second argument when the load returns an object. It returns the object; for any other tag it returns whatever `EBX` held, not 0 |
| `Shape_LoadDts` (`00474b2c`), `(name)` | `<name>.dts` through `Palette_LoadFromFile`, inside a pushed arena of 200,000 bytes |
| `Shape_LoadDtsCopy` (`00474b68`) | the same, with its own copy of the extension string ([Open](#open)) |
| `Palette_LoadDpl` (`00474ba4`), `(name)` | `<name>.dpl` through `Palette_LoadFromFile`, into the current arena |
| `Shape_LoadAllRoots` (`00474bcc`), `(out, path, tag, skip)` | every chunk of the file, inside a pushed arena of 500,000 bytes: the first `skip` chunks are skipped and the rest loaded into a 100-slot stack buffer, whose size the loop does not test. The pointers are copied into an array taken from the same arena, stored through `out`, and the count returned |
| `BaseType_ResolveShape` (`00474cd8`), `(path, index, tag)` | the chunk at `index`: the ones before it are skipped, and the one load runs inside a pushed arena of 200,000 bytes |
| `ClassItem_LoadAfterPeek` (`00474b04`), `(stream)` | the next chunk of a stream already open, into the current arena |

A chunk whose tag is not `tag` stops `Shape_LoadAllRoots` and `BaseType_ResolveShape` with a severity-5 assert, which is fatal (`Internal Error`), before it is loaded or skipped.

### Chunk headers

**Every chunk opens with an 8-byte header, its class's tag and the length of its body, and the body is padded to an even length with a zero byte.** The same framing carries the fonts, cursors and bitmaps of [`dfn-hfn-dci.md`](dfn-hfn-dci.md). While a chunk is read or written, `ClassItem_CurrentFrame` points at a frame on the stack of the call handling it: the tag at `+0`, the stream position the chunk starts at `+4`, the position it ends at `+8`, and at `+0xc` a byte set when the length is even. `ClassItem_TagTotalSize` (`0047a434`) gives a chunk's whole size: the length plus 8, rounded up to even. A tag whose byte 3 has its high nibble set takes the length as the whole size, and such a chunk is written with no header.

**Reading.** `ClassItem_LoadResource` (`0047a038`), `(stream, mode)`:

1. points the frame pointer at a frame of its own, keeping the one it found;
2. reads the header with `ClassItem_ReadTypeTag` (`0047a5a8`): it records the start position, fills the tag with {−1, 0}, reads 8 bytes over it, and for a tag the registry holds sets the end to the position after the header plus the length rounded up to even, and `+0xc` to whether the length is even;
3. for a tag other than −1, finds the registry entry with `ClassItem_FindHandler` (`0047a394`), which walks the buckets `ClassItem_RegisterBucket` (`00479fac`) appended, each `{entries, count}` of 12-byte `{tag, load function, name}` entries, and returns the first entry whose tag matches; with one, calls its load function with `(stream, mode)`;
4. reads byte by byte to the frame's end (`ClassItem_CloseItem`, `0047a730`), stopping when the stream's status is not 0, so a load that read short leaves the stream at the next chunk. For a tag the registry lacks, which no probe below takes, nothing writes the frame's end, and this read runs to whatever the stack held there;
5. puts the frame pointer back and returns the object, or 0.

A tag the registry lacks goes to the probe buckets when there are any (`ClassItem_RegisterProbeBucket`, `00479fdc`, which also raises `ClassItem_ProbeHeaderSize`, `004a16b0`, to the largest header size its entries give): `ClassItem_ReadTypeTag` copies the 8 bytes to `ClassItem_ProbeHeader` (`006c59a8`), reads `ClassItem_ProbeHeaderSize` − 8 bytes more after them, and `ClassItem_TryEveryLoader` (`0047a3d8`) calls each probe entry's function with (the probe header, the tag, the stream) until one answers non-zero. That one may rewrite the tag: the frame takes the tag, ends at the start plus its length, with `+0xc` clear, and the stream seeks back to the start, so the chunk's load function reads the file from its first byte. `es2_xref.py` finds no caller of `ClassItem_RegisterProbeBucket`. `ClassItem_PeekTypeTag` reads a header the same way and seeks back to where it started.

**Writing.** `ClassItem_StoreItem` (`0047a0a0`), `(item, stream, mode)`, takes a frame of its own and runs three steps on the stream, or, when the item's body length (its vtable `+4`) is 0 and the stream answers `GetCapabilities` query 3 with 0, on a buffered stream it puts over it:

1. `ClassItem_OpenItemForWrite` (`0047a52c`), `(stream, tag, length)`: the tag at `+0`, the position at `+4`, the position plus `ClassItem_TagTotalSize` at `+8`, `+0xc` set for an even length; then the header, unless the tag's byte 3 has its high nibble set;
2. the item's write slot, `(item, stream, mode)`;
3. `ClassItem_CloseItemForWrite` (`0047a68c`): for a chunk opened with length 0, so that its end is its start plus 8, measures the body written, adds it to the end, writes a zero pad byte when it is odd, seeks to the start plus 4, writes the measured length and seeks back to the end; for any other, a zero pad byte when `+0xc` is clear.

### Arenas

An arena is a block handed out front to back, `{base, limit, cursor}`. `Arena_Alloc` (`0047a1bc`) returns `base + cursor` and advances the cursor, and falls back to the general allocator when the arena is null or has no room; it does not clear what it returns. DBSIM carries a second copy, `Arena_AllocDup` (`0047a17c`), which the shape readers call. The arenas form a stack of 12-byte entries at `006c59ec`, its depth at `004a16d8`, and the current arena `006c58b4`, which every load function and reader allocates from, points at the top entry.

`Arena_Push` (`00474ab0`), `(size)`, saves the heap byte count `006bb390` in `006b7a98`, allocates a zeroed block of `size` (`Mem_HeapAlloc`) and pushes `{block, size, 0}` (`ArenaStack_Push`, `0047a2f0`). `Arena_Pop` (`00474ad8`) shrinks the block to its cursor with `realloc`, sets the heap byte count to the saved count plus the cursor, and pops (`ArenaStack_Pop`, `0047a368`), pointing `006c58b4` at the entry below. The block itself stays allocated, and the objects loaded into it with it. `ArenaStack_Pop` tests the depth with `TEST` then `JNC`, so its branch that stores null for a negative depth can never run.

## Open

- **Open:** no reference to `Shape_LoadDtsCopy` found by `es2_xref.py`.
