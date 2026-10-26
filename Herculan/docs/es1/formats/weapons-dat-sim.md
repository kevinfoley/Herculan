# DBSIM.EXE — `dat\WEAPONS.DAT`, the weapon mount templates and the projectile records

ES1's simulator keeps two tables in this one file: what each of its seventeen weapons is when fitted to a HERC (its armour, hit spheres, model, range, energy, magazine and muzzle), and the nineteen projectile records the weapons fire. ES2 keeps the first in its own `WEAPONS.DAT` ([ES2's templates](../../retail/formats/weapons-dat-sim.md)) behind a count, and the second in a separate `PROJ.DAT` ([ES2's projectile records](../../retail/formats/proj-dat.md)); ES1's file has neither count and its `dat\` holds no `PROJ.DAT`. A search of `GO.EXE`'s image for `weapons` finds only the format string `%d WEAPONS`. Addresses are `ES1DBSIM` image addresses ([the X-32 executable](x32-executable.md#the-image)); ES2's are DBSIM's.

## The layout

| Offset | Size | Holds |
|---|---|---|
| 0 | 2,088 in retail | 17 templates, weapon ids 0 to 16, each [ES2's template record](../../retail/formats/weapons-dat-sim.md#weaponmounttemplate-record-variable-length): a `.DMG` piece, a `.COL` cluster and a 48-byte tail |
| 2,088 | 684 | 19 projectile records of 36 bytes, [ES2's record layout](../../retail/formats/proj-dat.md#layout) |

The retail file is 2,772 bytes and the walk ends on its last byte. The templates are variable-length, so the projectile records start wherever the seventeenth template ends.

## The load

`Weapons_LoadResourceTables` (`0000bf48`) is a phase-2 subsystem loader: the static initialiser `Weapons_RegisterSubsystem` (`0000d67c`, [gunlist.cpp's end](../simulation/weapon-mounts.md#where-the-code-sits)) registers it with `RegisterSubsystemLoader` (`00000ac4`), which appends a 6-byte {function, phase} record at `000bbcb0` for `Subsystem_RunPhase` (`00000af4`). It follows [ES2's](../../retail/formats/weapons-dat-sim.md) with these differences:

1. When `g_LowMemory` ([low memory](chassis-dat.md#low-memory)) is 0 it loads `dba\wpntex` into `WeaponTexBank` (`000af1f0`); it loads `dts\mechwpn2`, or `dts\mwpn2b` in low memory, with `Shape_LoadAllRoots` (tag `0x140008`) into `WeaponModels_Mechwpn2Roots` (`000bc2c8`), and stores the `wpntex` bank in every root's `+0x26`. The weapon models a mount carries come from `dts\mechwpns` instead ([the weapon models](../simulation/weapon-mounts.md#the-weapon-models)).
2. It opens `dat\weapons`, asserting at severity 5 (`gunlist.cpp` line `0xdd`) when the stream status is set, and reads no count: it sets `WeaponMountTemplate_Count` (`000bc2c6`) to 17 and builds 17 records of `0x58` bytes at `WeaponMountTemplate_Table` (`000bc2c2`) with `__vec_new` and the constructor `WeaponMountTemplate_Ctor` (`0000c0b4`).
3. `WeaponMountTemplate_ReadRecord` (`0000bb8c`) reads each, as ES2's does: `HercPiece_ReadRecord` (`00009890`) reads the piece's 8 bytes and its 4-byte dependents into `+0x00`, `Collision_ReadCluster` (`0000938c`) the component index into `+0x18` and the spheres through `Collision_ReadSphereArray` (`00008e00`) into `+0x12`, and the 48-byte tail goes to `+0x22`; then it writes 0 to `+0x48`. `Collision_ReadSphereArray` reads `count × 8` bytes when that product, as an `int16`, is non-zero, where ES2's tests `count & 0x1fff`.
4. The loader stores at `+0x52` the template's name from the 17-entry table at `000911fc` and at `+0x56` its index, as ES2's does from its 33-entry table.
5. It then reads the next `0x2ac` bytes, the projectile records, into `ProjDat_RecordTable` (`000bc2ce`), where ES2's opens `dat\proj` and reads a count and that many records.

| Id | Name | Id | Name | Id | Name |
|---|---|---|---|---|---|
| 0 | `ATC20` | 6 | `LAS100` | 12 | `PBEAM` |
| 1 | `ATC35` | 7 | `LAS200` | 13 | `ECM` |
| 2 | `ATC50` | 8 | `LAS300` | 14 | `EMP` |
| 3 | `ELF` | 9 | `6` | 15 | `PBEAM` |
| 4 | `EMP` | 10 | `8` | 16 | `MISSL` |
| 5 | `GREN` | 11 | `10` | | |

### The projectile lookups

`Proj_LookupRecordByIndex` (`0000c2bc`) is ES2's, `index × 0x24` into the table. `Proj_LookupRecord` (`0000c2d0`) takes (subtype id, type) in stack order and returns the first of records 0 to `0x12` whose `+0` is the type and `+2` the subtype id, or 0; ES2's bounds its search by the file's count.

### The projectile index

`MechLoadout_ConstructWeaponMounts` (`0000c31c`) reads a template's tail-relative `0x1c` (absolute `+0x3e`), as ES2's does, with ES1's own special values:

- **20** (`0x14`): no lookup. `ECM` carries it.
- **21** (`0x15`): `Proj_LookupRecord(ammunition type, 0)`, a `Rocket` record by the hardpoint's ammunition type, 5 rewritten to 0. The three pods carry it.
- **Otherwise** a direct index through `Proj_LookupRecordByIndex`. `GREN` carries 22, and `22 × 0x24` lies past the `0x2ac` bytes the table holds.

ES2's values are 33 for none and 34 for the rocket lookup. A weapon id below 0 becomes 17, which builds no mount and leaves the slot 0, where ES2's clamps to 0. The factory's switch builds mounts of four sizes from three constructors, five classes in all ([the mount classes](../simulation/weapon-mount-classes.md#the-classes)):

| Weapon ids | Mount bytes | Class | Constructor |
|---|---|---|---|
| 0, 1, 2, 9, 10, 11, 16 | `0x81` | ammunition | `WeaponMount_CtorAmmunition` (`0000a9e4`) |
| 3 | `0x82` | ELF | `WeaponMount_CtorEnergy` (`0000a84c`), then vptr `ElfMountVtable` (`00091302`) |
| 4, 6, 7, 8, 12, 14, 15 | `0x7f` | energy | `WeaponMount_CtorEnergy` |
| 5 | `0x85` | grenade | `WeaponMount_CtorAmmunition`, then vptr `GrenadeMountVtable` (`00091376`) |
| 13 | `0x7f` | ECM | `EcmMount_Ctor` (`0000ab50`), given no projectile record |

## Retail values

The piece of every template has debris byte 0, sequence byte −1, parent −1, flags 1 and one dependent, spill weight 20 on internal 12, where ES2's pieces carry −1 for debris; its armour is in the table below. Every cluster's component index is 19.

| Id | Armour | Spheres | Shapes (`+0x22`) | Internal max | Range | Energy low, high | Magazine | Barrels | Projectile | Refire | Rating |
|---|---|---|---|---|---|---|---|---|---|---|---|
| 0 `ATC20` | 500 | 6 | 31, 15, 0, 32 | 500 | 70000 | 0, 5 | 1000 | 1 | 0 | 1200 | 20 |
| 1 `ATC35` | 1000 | 9 | 16, 18, 7, 17 | 500 | 70000 | 0, 5 | 1000 | 1 | 1 | 1200 | 350 |
| 2 `ATC50` | 1500 | 10 | 19, 21, 8, 20 | 500 | 70000 | 0, 5 | 1000 | 1 | 2 | 1200 | 500 |
| 3 `ELF` | 2500 | 7 | 4 | 500 | 15000 | 400, 70 | 0 | 1 | 15 | 0 | 1000 |
| 4 `EMP` | 2500 | 9 | 3 | 500 | 40000 | 350, 10000 | 0 | 1 | 6 | 2000 | 600 |
| 5 `GREN` | 500 | 4 | 5 | 500 | 35000 | 0, 0 | 8 | 1 | 22 | 3000 | 400 |
| 6 `LAS100` | 500 | 7 | 22, 24, 1, 23 | 500 | 50000 | 80, 80 | 0 | 1 | 3 | 1200 | 200 |
| 7 `LAS200` | 1000 | 8 | 25, 27, 9, 26 | 500 | 50000 | 100, 100 | 0 | 1 | 4 | 1200 | 350 |
| 8 `LAS300` | 1500 | 8 | 28, 30, 10, 29 | 500 | 50000 | 120, 120 | 0 | 1 | 5 | 1200 | 500 |
| 9 `6` | 1500 | 4 | 5 | 500 | 60000 | 0, 1 | 6 | 1 | 21 | 2500 | 600 |
| 10 `8` | 1500 | 4 | 5 | 500 | 60000 | 0, 1 | 8 | 1 | 21 | 2500 | 700 |
| 11 `10` | 2000 | 4 | 5 | 500 | 60000 | 0, 1 | 10 | 1 | 21 | 2500 | 800 |
| 12 `PBEAM` | 2500 | 6 | 2 | 500 | 35000 | 300, 10000 | 0 | 1 | 14 | 1000 | 600 |
| 13 `ECM` | 1000 | 7 | 11 | 500 | 0 | 0, 100 | 0 | 0 | 20 | 0 | 0 |
| 14 `EMP` | 15000 | 21 | 12 | 15000 | 50000 | 350, 10000 | 0 | 3 | 16 | 5000 | 500 |
| 15 `PBEAM` | 15000 | 5 | 13 | 15000 | 40000 | 300, 10000 | 0 | 1 | 17 | 3000 | 600 |
| 16 `MISSL` | 15000 | 6 | 14 | 15000 | 60000 | 0, 1 | 36 | 1 | 18 | 5000 | 800 |

A single shape number stands for the same shape at all four mounting codes. The columns are the tail's fields at [ES2's offsets](../../retail/formats/weapons-dat-sim.md#decoded-tail-fields): internal max `+0x2a`, range `+0x30`, energy `+0x36` and `+0x38`, magazine `+0x3a`, barrels `+0x3c`, projectile index `+0x3e`, refire `+0x4c`, rating `+0x4e`. The minimum range at `+0x2c` is 15000 on `GREN` and 0 on the rest. `GREN`'s side muzzle offset reads 250, 300, 100, the only non-zero `+0x48`, which the load overwrites with 0. The AI penalty at `+0x34` is 10, 20 and 30 on the autocannons and lasers by size, 5 on `ELF`, 150 on the small `EMP` and both `PBEAM`s, 300 on `GREN`, 500 on the pods, 600 on `MISSL`, 1 on the big `EMP` and 0 on `ECM`. The damage icon at `+0x50` matches ES2's numbering: 0 `ELF`, 1 the lasers, 2 the autocannons, 3 both `EMP`s and `ECM`, 4 both `PBEAM`s, 5 `GREN`, the pods and `MISSL`.

The 19 projectile records hold, in their first six fields, the values ES2's `PROJ.DAT` holds in its records 0 to 18 ([ES2's retail records](../../retail/formats/proj-dat.md#the-retail-records)): the three autocannon rounds, the three lasers, the EMP round, three grenades, four rockets, two beams, the big EMP round, the big beam and the big rocket. Their impact effects name ES1's own [`EXPLOS.DAT`](explos-dat.md) types: 11 to 14 for a shot the shields absorb, and 0 to 8 on ground and armour.

## Open

- **Open:** what each tail field means to ES1's mounts and fire code; the readers this page names are the loader, `WeaponMountTemplate_ReadRecord` and `MechLoadout_ConstructWeaponMounts`, which reads `+0x3e`. The values sit at ES2's offsets with ES2's kind of value.
- **Open:** what lies at the pointer `Proj_LookupRecordByIndex(22)` returns for `GREN`, past the table's end; ES1's grenade records are 7 to 9. The constructor keeps it at the mount's `+0x20` unread, and `WeaponMount_GetDisplayName`, `WeaponMount_GetAmmoType` and `GrenadeMount_FireDispatch` read its `+0` and `+2` ([the mount classes](../simulation/weapon-mount-classes.md#firing)); the throw function the dispatch passes the `+2` to reads none of its arguments ([the grenade](../simulation/projectiles.md#the-grenade)).
- **Open:** which `EXPLOS.DAT` impact array each of the three projectile arrays is in ES1, read in ES2's file order here.
