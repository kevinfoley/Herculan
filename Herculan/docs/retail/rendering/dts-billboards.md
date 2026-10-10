# `TSBitmapPart` billboards and `TSCellAnimPart` flipbooks (DBSIM.EXE)

Addresses are DBSIM virtual addresses.

The one shape-object render slot that puts pixels on screen with no polygon involved. It is what `BULLETS.DTS` roots 2 and 3 (the three EMP cannons' rounds) and all twenty `EXPLOS.DTS` roots (every impact effect, see [`../simulation/impact-effects.md`](../simulation/impact-effects.md)) are made of.

Which `.DBA` a sprite resolves against is the shape instance's own bound bank, the same binding a mesh uses — see [`dts-texture-binding.md`](dts-texture-binding.md).

## Class identification

Via DBSIM's `g_TSObjectTypeRegistry` (`004a63c8`), not resemblance:

| Tag | Type | Constructor | Vtable | Render (`+0x1c`) |
|---|---|---|---|---|
| `0x00140013` | `TSBitmapPart` | `0048fa68` | `004a62d0` | `004762e8` |
| `0x0014000b` | `TSCellAnimPart` | `0048fee8` | `004a629c` | `004767e4` |

## `TSCellAnimPart_Render` (`004767e4`)

```
child = children[ cellFrames[animSequence] % childCount ];
child->vtable[+0x1c]();
```

**One child per frame, not a container.** `cellFrames` is the drawing shape instance's own per-sequence `ushort` array, published to the global `DAT_006b7bf0` by whatever installs the instance for drawing. Walking every child the way a `TSPartList` is walked stacks the whole animation on top of itself.

**Children need not be bitmaps.** `BULLETS.DTS` root 8 (plasma) is a two-cell animation over real `TSGroup` geometry, and both `ROCKETS.DTS` roots animate their exhaust flame the same way — that file holds no `TSBitmapPart` at all (see [`../formats/rockets-dat.md`](../formats/rockets-dat.md#dtsrocketsdts)). `animSequence` (`part+0x16`, the label `TSCellAnimPart_DebugPrint` prints it under) picks which entry of `cellFrames` the part reads; a projectile's own tick names the same sequence in its type record and mods the counter by `TSShape.SequenceList[seq]`, the shape's per-sequence frame count at `shape+0x20`.

## `TSBitmapPart_Render` (`004762e8`)

The bitmap tag (`part+0x10`) indexes the bound bank directly. The rest is a screen-space blit of a rotated, scaled quad, built from four things:

**Scale.** `scale = (radius * 4 << focalShift) / depth`, where `radius` is `TSBasePart.Radius` and `depth` is the part's centre in view space. Every dimension below is `Math_Q8Multiply`'d by it. Since the projection maps a view-plane length `L` at depth `D` to `(L << focalShift) / D` pixels, the projection constant cancels: **one bitmap pixel spans `radius / 64` world units**, whatever the field of view is.

**Rotation.** The model origin and the model point `(0, 0, 0x800)` — a fixed distance up the model's *own* Z axis — are both transformed and projected, and the quad is blitted rotated by the screen angle between them. For a shot in flight that axis is the shot's frame, not the world's.

**Vertical squash.** Before projecting, the same axis's length surviving in the view plane is measured (`Vec2_DistanceBetween` on the view-space x/z pair, range `0..0x800`). The drawn height is

```
height = cols + (rows - cols) * measured / 0x800
```

so it runs from the bitmap's *width* (axis pointing at the viewer) to its *height* (axis across the view). A round puff therefore reads as a disc from overhead rather than collapsing to nothing. The drawn width is always `cols`.

**Anchor.** The destination quad's corners are `(0,0), (w,0), (w,h), (0,h)`, each rotated and then offset by the projected centre displaced by `-(ofsX, ofsY')` — same scale, same rotation — where `ofsY' = ofsY * height / rows`. So the part's centre lands on bitmap pixel `(ofsX, ofsY')`. `ofsX` is read signed (`*(char *)(part + 0x12)`), `ofsY` unsigned (`*(byte *)(part + 0x13)`).

In-memory bitmap object:

| Offset | Contents |
|---|---|
| `+4` | rows, `int16` |
| `+6` | cols, `int16` |
| `+8` | bits per pixel, byte |
| `+9` | flags, byte: bits 0-3 the bitmap type, bit `0x10` opaque |
| `+0xa` | data length |
| `+0xe` | data pointer |
| `+0x12` | packing type: 0 raw, 1 RLE, 3 LZH |
| `+0x14` | extra dword count, `int16` |
| `+0x16` | extra dwords pointer |

`GLBitmap_ReadFromStream` (`00481c80`) fills these from the bitmap record ([`../formats/dfn-hfn-dci.md`](../formats/dfn-hfn-dci.md#the-bitmap-record)) in file order, `+9` taking the file byte's bits 0-4 and keeping its own bits 5-7. A bitmap of type 1 is transparent: `Bitmap_BlitClipDispatch` (`004886cc`) hands it to `Bitmap_BlitTransparent` (`00488cec`). Bit `0x10` makes the unrotated blits `Driver3_BlitClipped` (`004898a0`) and `Driver3_BlitScaled` (`0048aebc`) copy every texel; without it they skip palette index 0. Every one of the 3,515 bitmap records in the v1.0 and v1.10 archives carries 8 bits per pixel, flags 0 and packing 0, so none is packed and none is opaque; the [map raster](../simulation/heads-down-display.md#terrain-raster) is built with 8, type 0 and bit `0x10`.

`Bitmap_BlitRotatedScaled` (`00488a8c`) takes a pointer to `+4` and builds its source quad from it as `(0,0), (p[1]-1, 0), (p[1]-1, p[0]-1), (0, p[0]-1)`; its flip argument mirrors that quad, bit 2 in x and bit 1 in y. Callers reach it through `Bitmap_BlitRotatedUnpacked` (`00481750`), which first unpacks a packed bitmap into a scratch buffer with `Bitmap_UnpackToScratch` (`00481804`) — one decoder class per packing type — and points `+0xa`/`+0xe` at the copy for the length of the blit. The packed data is a `uint32` unpacked size followed by an RLE or LZH stream, whose formats are in [`../runtime-library.md`](../runtime-library.md#the-decompression-filters).

Every retail bitmap part carries `Transform == -1` and a centre of the origin, so the node composition `00476014` performs is the identity throughout retail data.

### Brush mode 5 skips palette index 0

`Bitmap_BlitRotatedScaled` installs the brush `{5, 4, &sourceQuad, dimensions}` and calls `Raster_DrawPolygonDispatch`, which hands modes 5 and 7 to slot 33 of `g_RasterRoutines` (`004a57c4`, 36 function pointers). The table is zero in the image: `RasterDriver_InstallRoutines` (`0048929e`), called from `Raster_SelectRenderTarget` (`00481118`), copies it in from the per-driver list `g_RasterDriverTable` (`004a5858`). That list gives drivers 0 and 1 a null table and driver 3 `004a5884`, so slot 33 is always `Raster_FillBitmapPolygon` (`0048a818`).

That routine walks the destination polygon's two edges (`Raster_BitmapEdgeSetup`, `0048a9a0`; `Raster_BitmapEdgeStep`, `0048a944`), interpolating the source quad along each, and fills each scanline with `Raster_SpanBitmap` (`0048aad8`). The span samples the nearest texel through the bitmap's cols (`+6`) and data pointer (`+0xe`). Its last argument picks one of two loops:

- **Mode 5** passes null: a texel of index 0 is not stored (`or al,al` / `je` at `0048ae87`). The mode fixes this; no field of the brush or the bitmap object changes it.
- **Mode 7** passes brush `+0x10`, a remap table: every texel, index 0 included, is stored as `remap[texel]`.

Neither routine reads the brush's second field.

### The offset is a hotspot

All twenty `EXPLOS.DTS` roots carry an offset near half their frame's size — shape 6 is `(23, 22)` against a 48x47 frame, shape 9 `(52, 53)` against 112x107 — which is what fixes the mechanism as "anchor lands on this pixel" rather than "quad starts here".

`BULLETS.DTS` roots 2 and 3 are the exception and are authored oddly: all five parts read `(45, 45)` against 40x30 frames, so the EMP puff draws up and to the left of the round rather than centred on it. That is the retail data's own behaviour.
