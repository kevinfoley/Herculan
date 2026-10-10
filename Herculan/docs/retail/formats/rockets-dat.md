# `dat\ROCKETS.DAT` and `dts\ROCKETS.DTS` (launcher round type table and shapes)

The two resources `Rocket_LoadTypeTable_Unguided` (`0040a818`) loads once at startup. The table is read as `int16 count` then that many 14-byte records into `maybe_RocketTypeTable_Unguided`, and the shape file is loaded into `DAT_004a975c`. How the round flies, steers and animates from them is in [`../simulation/rockets.md`](../simulation/rockets.md).

## `dat\ROCKETS.DAT`

**Indexed by the firing `PROJ.DAT` record's subtype id** — `Rocket_GetTypeRecord` (`0040a234`) is `table + id * 14`.

**The layout is not `BULLETS.DAT`'s.** The two files share a stride and their first two fields and nothing else; the readers are different functions reading different offsets ([`../simulation/projectiles.md`](../simulation/projectiles.md#datbulletsdat)).

| Offset | Meaning |
|---|---|
| `+0x00` | model: root of `ROCKETS.DTS` |
| `+0x02` | lifetime, in **ticks** — a plain `+1` counter, not the bullet's `0x200` age units |
| `+0x04` | acceleration, per 125 ms |
| `+0x06` | the shot record's slack, which is what a bullet keeps at `+0x04` |
| `+0x08` | animation frame interval; 0 = static shape |
| `+0x0a` | which of the shape's sequences that interval steps |
| `+0x0c` | fire sound id, played as `id + 10` |

Retail (5 records, one per `Rocket` subtype id):

| id | Weapon | Shape | Life | Accel | Slack | Anim | Seq | Sfx |
|---|---|---|---|---|---|---|---|---|
| 0 | `SARH` | 0 | 80 | 250 | 200 | 256 | 0 | 5 |
| 1 | `ARH` | 0 | 80 | 250 | 200 | 256 | 0 | 5 |
| 2 | `ARM` | 0 | 80 | 250 | 200 | 256 | 0 | 5 |
| 3 | `EO` | 0 | 80 | 250 | 200 | 256 | 0 | 5 |
| 4 | `BMSL` | 1 | 80 | 250 | 300 | 0 | 0 | 5 |

## `dts\ROCKETS.DTS`

Both roots are a `TSDetailPart` over four LODs (`details = [4, 12, 45, 255]`). At the highest, the shape is a static body plus a **two-cell `TSCellAnimPart` holding geometry** — the cells are flat-poly cones at the tail, and their surface colours are the palette's flame range against the body's grey. In root 1 that level is a `TSBSPPart` whose tree reaches the body alone, so root 1's flame is never drawn at its finest level ([`../rendering/dts-texture-binding.md`](../rendering/dts-texture-binding.md#tsbsppart-child-selection)):

| | model-space centre Y | surface colours |
|---|---|---|
| body | 69 (root 0) / 139 (root 1) | 200 — grey `(116,116,116)` |
| flame cell 0 | 17 / 33 | 109, 94, 87, 86 — red `(224,4,0)` through orange |
| flame cell 1 | 8 / 17 | 93, 109, 86, 88 — pale yellow `(248,236,168)` through orange |

Both roots declare one sequence of two frames (`TSShape.SequenceList == [2]`, the `shape+0x20` array a projectile's tick mods by) and every `TSCellAnimPart` in them carries `AnimSequence == 0` — the sequence every `ROCKETS.DAT` record names.

**There is no `ROCKETS.DBA` and no bank is bound.** Unlike `Bullet_LoadResources`, the rocket loader never writes the shapes' bound-bank pointer, and the shapes hold no `TSBitmapPart` to want one: a rocket is entirely ramp-coloured `TSSolidPoly`/`TSShadedPoly` geometry (57 polys; [`../rendering/dts-texture-binding.md`](../rendering/dts-texture-binding.md)). The cell-animation mechanism is in [`../rendering/dts-billboards.md`](../rendering/dts-billboards.md).
