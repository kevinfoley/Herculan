# `dts\<name>.DTS` — shapes

A `.DTS` holds a model's shape roots, each a tree of chunked objects — part lists, groups of polys, bitmap and cell-animation parts — whose chunk tags each executable's type registry maps to a class. `TSShape_ReadFromStream` (`00490d5c`) reads a shape's own fields. A `.DTS` carries no reference to any texture file. How the objects are drawn is [`../rendering/dts-texture-binding.md`](../rendering/dts-texture-binding.md), [`../rendering/dts-node-posing.md`](../rendering/dts-node-posing.md) and [`../rendering/dts-billboards.md`](../rendering/dts-billboards.md), and for a machine [`../rendering/mech-shape-drawing.md`](../rendering/mech-shape-drawing.md); the `.DGS` structure libraries are [`dgs-hd0-notes.md`](dgs-hd0-notes.md). Addresses are DBSIM virtual addresses unless VSHELL is named.

## Chunk tags

An object's on-disk chunk header is its tag, `[subtype:u16][supertype:u16]` (e.g. `0x0014000f` = `TSTexture4Poly`). DBSIM's tags and the classes they name are tabled in [`../rendering/dts-texture-binding.md`](../rendering/dts-texture-binding.md#poly-types-and-their-colour-mechanisms-dbsimexe) and [`../rendering/dts-billboards.md`](../rendering/dts-billboards.md#class-identification).

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
