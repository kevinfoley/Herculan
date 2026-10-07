# `dat\LC_WPNS.DAT` (transport weapon slots)

The weapon table of the transport, the class (`LC_BASE`) `Base_Construct` builds for `BASES.DAT` type `0x22` alone ([`../simulation/structure-behaviour.md`](../simulation/structure-behaviour.md#the-transport--004045c8)). `Base_LoadResources` (`00405fac`) reads it between `dat\bforms` and `dat\bases`: an `int16` count into `g_LcWeaponSlotCount` (`004a963c`), then that many 22-byte records into a block whose pointer it stores at `g_LcWeaponSlots` (`004a9640`). The pointer is null in the image; the file is the only source of the values.

Its one reader is `Base_TransportThinkTick` (`004045c8`), which indexes the records by weapon slot. All three of the transport's weapon stations share them, each reading them in its own frame.

## Layout

| Offset | Type | Meaning |
|---|---|---|
| `+0x00` | `int16` | pitch arc: the aim error's pitch must lie in `[-arc, arc)` to fire, binary angle |
| `+0x02` | `int16` | yaw arc, the same test on the yaw error against the station's own heading |
| `+0x04` | `int32` | range: fires only at a target nearer than this |
| `+0x08` | `int32` ×3 | muzzle offset from the structure's origin; X and Y rotate with the station's heading, Z is added as it stands |
| `+0x14` | `int16` | refire delay, in the simulation's timer units ([Timer units](../simulation/dbsim-physics-notes.md#timer-units)) |

## Retail records

| Slot | Fires | Pitch arc | Yaw arc | Range | Offset | Refire |
|---|---|---|---|---|---|---|
| 0 | `Rocket_Fire` subtype 3 | `0x2000` (45°) | `0x2b00` (60°) | 50000 | (0, 1500, 2500) | 3000 (1.5 s) |
| 1 | `Bullet_FireBurst` subtype 3 | `0x2000` | `0x3800` (79°) | 40000 | (500, 1500, 3500) | 1500 (0.73 s) |
| 2 | `Bullet_FireBurst` subtype 3 | `0x2000` | `0x3800` | 40000 | (-500, 1500, 3500) | 1500 |

The two beam slots are one gun's left and right barrels, 1000 units higher than the launcher and either side of it.
