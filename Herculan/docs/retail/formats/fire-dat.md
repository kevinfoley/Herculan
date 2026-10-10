# `dat\FIRE.DAT` and `dts\FIRE.DTS` (burning-object resources)

What `FireEffect_LoadResources` (`0046b0a4`), the `fire.cpp` subsystem's phase-2 loader, reads. How a fire behaves once lit is [`../simulation/destruction-effects.md`](../simulation/destruction-effects.md#fire). Addresses are DBSIM virtual addresses.

- **`dts\FIRE.DTS`** (`FIRE2.DTS` under the low-memory art setting): four roots, each a flipbook of billboards ([`../rendering/dts-billboards.md`](../rendering/dts-billboards.md)) of 24, 24, 24 and 27 frames.
- **`dba\FIRE0.DBA` and `FIRE1.DBA`**, the two texture banks (`FIRE0S.DBA` and `FIRE1S.DBA` under the same setting).
- **`dat\FIRE.DAT`**, below.

## Layout

```
int32 count              // 4 in retail; the loader reads it into a local and takes the shape count from the .DTS
byte  bank[shapeCount]   // which of the two banks textures each shape
```

The loader writes each shape's bound-bank pointer (`shape+0x26`) from the bank its byte names. Retail has four shapes and banks `[0, 0, 0, 1]`.

The loader also builds the fire pool, ten entries of 0x5b bytes.
