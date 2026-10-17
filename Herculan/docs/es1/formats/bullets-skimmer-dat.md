# DBSIM.EXE — `dat\BULLETS.DAT` and `dat\SKIMMER.DAT`

Two small tables of ES1's simulator: the travelling rounds the autocannons and EMP cannons fire, and the one flyer type with data. Each is a shortened ES2 record: a bullet record is ES2's first five fields of seven, and the flyer file is ES2's nine numbers without the name. A search of `GO.EXE`'s image for `bullets`, `skimmer` and `flyers` finds none of them. Addresses are `ES1DBSIM` image addresses ([the X-32 executable](x32-executable.md#the-image)); ES2's are DBSIM's.

## `dat\BULLETS.DAT`

```
int16 count
byte[10][count]
```

The retail file is 82 bytes: 8 records. Each is the first ten bytes of [ES2's 14-byte record](../../retail/simulation/projectiles.md#datbulletsdat), without the firing scatter at `+0x0a` and the flag at `+0x0c`. A record is reached as `table + id × 10` (`Bullet_GetTypeRecord`, `00007b94`, from the round's type byte at `+0x3b`), the id being the firing projectile record's subtype id. Offsets in bold are ones the functions this page names read.

| Offset | ES2's field |
|---|---|
| **`+0x00`** | the root of `dts\bullets` the round is drawn as |
| `+0x02` | lifetime |
| `+0x04` | clip radius |
| **`+0x06`** | the frame interval |
| `+0x08` | fire sound id |

| Id | Projectile records | Shape | Life | Radius | Interval | Sound |
|---|---|---|---|---|---|---|
| 0 | 0, `ATC20` | 0 | 17 | 100 | 0 | 8 |
| 1 | 1, `ATC35` | 4 | 17 | 100 | 0 | 8 |
| 2 | 2, `ATC50` | 5 | 17 | 100 | 0 | 8 |
| 3 | — | 1 | 50 | 100 | 0 | 2 |
| 4 | — | 6 | 50 | 100 | 0 | 2 |
| 5 | — | 7 | 50 | 100 | 0 | 2 |
| 6 | 6, `EMP` | 2 | 30 | 100 | 256 | 2 |
| 7 | 16, the big `EMP` | 3 | 30 | 200 | 256 | 2 |

The projectile records are those of type 2 whose subtype id is the bullet's ([the projectile records](weapons-dat-sim.md#the-layout)); no type-2 record names ids 3 to 5.

### The load

`Bullet_LoadResources` (`00007bb0`) is a phase-2 subsystem loader, registered by the static initialiser at `000081f8` with `RegisterSubsystemLoader` (`00000ac4`). It follows ES2's: it opens `dat\bullets`, asserting at severity 5 (`bullet.cpp` line `0x29`) when the stream status is set, reads the count into `000bc1fa` and count × 10 bytes into `g_BulletTypeTable` (`000bc1f6`), and keeps the greatest `+0x00` plus one at `000bc200`. It loads `dts\bullets` with `Shape_LoadAllRoots` (tag `0x140008`) into `000bc1fc` and `dba\bullets` with `ClassItem_LoadFromFile`, and stores that bank in every root's `+0x26`, where ES2's packs the bank into an atlas and stores the address of its slot.

`Bullet_Construct` (`00007d08`), given the round and the id, allocates the round from the pool `000af0b4` when it is null (returning 0 when the pool is full), builds it on root `+0x00`, sets the type tag to 3 and the type byte, stores `Proj_LookupRecord(id, 2)` at `+0x48` and loads the frame countdown at `+0x53` from `+0x06`. ES2's stores the record at `+0x4e` and the countdown at `+0x59`.

## `dat\SKIMMER.DAT`

`nam\FLYERS.NAM` names two flyer types, `SKIMMER` (0) and `HOVTANK` (1); `SIMVOL0.VOL`'s `dat\` holds `SKIMMER.DAT` and no `HOVTANK.DAT`. ES2's flyer file is 46 bytes, nine `int16` and a 28-byte name ([ES2's flyers](../../retail/simulation/ai-flyers.md)); ES1's is 34.

| Offset | ES2's field | `SKIMMER` | ES2's `SKIMMER` |
|---|---|---|---|
| 0 | max turn rate | 500 | 800 |
| 2, 4 | max reverse speed, max forward speed | 1200, 1000 | 1000, 1000 |
| 6, 8 | speed and turn-rate steps | 300, 150 | 300, 150 |
| 10 | the part the cockpit eye rides | 4 | 4 |
| 12 | walk sequence | −1 | −1 |
| 14 | the bank limit | 14000 | 14000 |
| 16 | — | 1500 | 1500 |
| 18–33 | the first 16 bytes of the name | 0 | `Landskimmer` |

### The load

`FlyerTypes_LoadAll` ([name lists](nam-name-list.md#mod-lists)) calls `FlyerType_LoadResources` (`0001c6c8`) for each of types 0 and 1 that `FlyerType_RecordIndex` (`0001c678`) gives a record, its byte in the 2-entry table at `000bc466` not being `0xff`. `FlyerType_Register` (`0001c688`) gives a type the next record the first time it is asked, and reserves one `.DMG` slot and one collision slot with `DmgTable_Reserve` and `CollisionTable_Reserve`, as ES2's does.

`FlyerType_LoadResources`, given the type number, takes its name from `000bc468` and its `0x2e`-byte record from `000bc46c`, and:

1. reads 34 bytes of `dat\<name>` into the record's `+0x00` with `Ovl_ReadFile`, which asserts when the file does not open or the read fails;
2. loads `dts\<name>` with `Shape_LoadDts` into `+0x2a`, and stores in the shape's `+0x26` the `dba\enemy` bank, the fourth of the four [chassis texture banks](chassis-dat.md#the-load) at `000bc45c`;
3. loads `dmg\<name>` with `Damage_LoadMechDmgFile` ([`dmg-damage-file.md`](dmg-damage-file.md#the-load)) into `+0x22` and `col\<name>` with `Collision_RegisterObject` (`00009580`, [the loadout's join](dmg-damage-file.md#the-loadouts-join)) into `+0x26`.

ES2's reads 46 bytes into a `0x70`-byte record, keeps the damage file, collision model and shape at `+0x2e`, `+0x32` and `+0x36`, writes the address of a fixed bank slot, and reads `fm\<name>`; ES1's has no flight model file.

## Open

- **Open:** what ES1's bullet fire and tick read of `+0x02`, `+0x04` and `+0x08`, and what the flyer code reads of `SKIMMER.DAT`'s offsets 0–17 and the zeroes at 18–33. The values sit at ES2's offsets with ES2's kind of value.
- **Open:** whether any mission places a `HOVTANK`. Once type 1 has a record, step 1 finds no `dat\HOVTANK` and its severity-5 assert ends the program ([`HOVTANK.DMG`](dmg-damage-file.md#hovtankdmg)).
