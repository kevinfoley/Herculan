# `dat\EXPLOS.DAT` (impact-effect tables)

The table `Explosion_LoadResources` (`00407b54`), the `EXPLO.CPP` subsystem's init, loads at startup alongside the shapes and texture banks it indexes. What an effect does once built — construction, the tick, which `PROJ.DAT` array picks the type — is [`../simulation/impact-effects.md`](../simulation/impact-effects.md). Addresses are DBSIM virtual addresses.

## Resources loaded with it

- **`dba\EXPLO0.DBA`..`EXPLO14.DBA`**, fifteen banks, from the name template `explo666` at `00497ba0` (the loader overwrites from the sixth character on with the index). With `CockpitArt_LoadOnDemand` set it loads thirteen `EXPLO<n>S.DBA` banks instead.
- **`dts\EXPLOS.DTS`** (or `EXPLOS2.DTS`), 20 roots, every one a `TSCellAnimPart` of `TSBitmapPart`s ([`../rendering/dts-billboards.md`](../rendering/dts-billboards.md)). `EXPLOS2.DAT` is the low-memory twin of the table below.
- **The table**, below.

The loader also builds the effect pool: 40 entries of 0x5b bytes, so a forty-first simultaneous effect is not built (every spawn site tests the allocation).

## Layout

```
int16 shapeCount
{ int16 animSequence; int16 textureBankIndex; }[shapeCount]
int16 typeCount
byte[0x28][typeCount]
```

Retail is 964 bytes: 20 shapes, 22 types, nothing left over. `shapeCount` matches `EXPLOS.DTS`'s root count exactly — the first table is one row per root, in order — and the loader writes `shape->boundBank = banks[textureBankIndex]` straight into each shape instance's own bank pointer.

`animSequence` is the cell-animation sequence the effect drives; zero on every retail row, matching every `TSCellAnimPart` in `EXPLOS.DTS`. Negative means the shape has no flipbook: construction skips the frame reset and the tick ends the effect on its first timer expiry.

## Type row (0x28 bytes)

Reached as `table + typeId * 0x28` (`Explosion_GetTypeRecord`, `00407b20`). The id is what an entry of a `PROJ.DAT` impact-effect array holds.

| Offset | Meaning |
|---|---|
| `+0x00` | shape index: which shape row, i.e. which `EXPLOS.DTS` root |
| `+0x02` | frame interval: ticks each flipbook frame is held; **1 on every retail row** |
| `+0x04` | ground shape: nonzero lays root 1 of the theater's flat set on the ground under the effect, stepped with its flipbook and deleted with it ([`../simulation/ground-shapes.md`](../simulation/ground-shapes.md#an-impact-effects-shape)); **0 on every row of `EXPLOS.DAT` and `EXPLOS2.DAT`** |
| `+0x06` | light mode: nonzero attaches a light source; 0, 1 or 2 in retail. `Explosion_Construct` tests it only against zero, so 1 and 2 attach the same light ([`../rendering/effect-lights.md`](../rendering/effect-lights.md#claiming-a-slot)) |
| `+0x08`..`+0x1f` | intensity ramp, twelve `int16`: the light's intensity per frame; low byte passed to the light as each frame is stepped |
| `+0x20` | proximity radius, `int32`: 0 or 20000. Read by `Explosion_ProximityTest` alone, below |
| `+0x24` | sound id, played as `id + 10`; negative is silent |
| `+0x26` | object class: 0 registers the effect under class tag 2, else 8 |

`Explosion_ProximityTest` (`00408100`) returns 1 when the row's light mode is exactly 2 and the effect lies within the proximity radius of a point it is handed. It is a proximity test with no reference anywhere in the image: no rel32 branch, no stored pointer, and the effect class's vtable (`00497d4c`, six slots, the last a stub shared with other classes) has no slot for it. Light mode 2 and the proximity radius therefore have no effect on a running mission.
