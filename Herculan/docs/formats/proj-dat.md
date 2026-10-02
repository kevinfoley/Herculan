# `dat\PROJ.DAT` (projectile records)

What a weapon's shot is: which projectile class fires it, how hard it hits shields and armour, what share of the hit explodes, how fast it travels and which effects it spawns on impact. `Weapons_LoadResourceTables` (`0040fc8c`) opens `"wpntex"`, `"mechwpn2"` and `"weapons"` ([`weapons-dat-sim.md`](weapons-dat-sim.md)), then `"proj"`, and reads the count and the records in one flat read into `ProjDat_RecordTable` (`DAT_004a9980`).

How the classes behave is in [`../simulation/weapon-damage-types.md`](../simulation/weapon-damage-types.md#type--a-firing-mechanism-selector), [`../simulation/projectiles.md`](../simulation/projectiles.md) and [`../simulation/rockets.md`](../simulation/rockets.md). What a shot does to its target is in [`../simulation/damage-system.md`](../simulation/damage-system.md).

## Layout

After the [VOL entry prefix](vol-archive.md#the-per-entry-prefix--fixed-9-bytes): `int16 count` (27 in retail), then that many 36-byte records, read in one flat read and nothing after them: 974 bytes of content in retail. The extracted `simvol0/dat/PROJ.DAT` is 984, the prefix and the archive's one-byte trailer included.

| Offset | Field |
|---|---|
| `+0x00` | `Type`, the projectile class: 0 `Rocket`, 2 `Bullet`, 3 `Grenade`, 4 `Beam` |
| `+0x02` | subtype id: the record of `ROCKETS.DAT`, `BULLETS.DAT` or `BEAM.DAT` that `Type` selects ([`rockets-dat.md`](rockets-dat.md), [`../simulation/projectiles.md`](../simulation/projectiles.md#datbulletsdat), [`beam-dat.md`](beam-dat.md)) |
| `+0x04` | shield damage |
| `+0x06` | armour damage |
| `+0x08` | splash factor, a Q10 fraction |
| `+0x0a` | `Speed`: a travelling round's speed, a rocket's ceiling. 0 on every `Beam` |
| `+0x0c` | shield impact effects: four `EXPLOS.DAT` effect types |
| `+0x14` | ground impact effects |
| `+0x1c` | armour impact effects |

The three effect arrays are the shot record's twelve-entry effect array, and [`../simulation/impact-effects.md`](../simulation/impact-effects.md#which-effect-a-shot-spawns) says which a hit picks. The shot record puts armour damage before shield damage where this file puts shield first ([`../simulation/weapon-firing.md`](../simulation/weapon-firing.md#the-shot-record)).

**The two damage figures are the weapon's own base stats against each defence**, in the scale a shot of full power (1024) hits at. A shot is worth `Q10(power, figure)` where `power` is the charge it was fired at, `min(template+0x38, mount+0x7d)`, so a mount holding 960 hits for 0.94 of the figure and one turned up to 1200 for 1.17 of it. A magazine-fed bullet and a rocket carry no charge and apply the figure as it stands. The splash factor's own multiply is Q10 as well (`Math_Q10Multiply`, `0047dfa4`), against the already shield-reduced armour damage: [`../simulation/damage-system.md`](../simulation/damage-system.md#direct-fire-damage-armor-then-part-deterministic-shield-gated).

## Lookup

Two functions resolve a record, and a fired shot uses both.

- **`Proj_LookupRecordByIndex` (`0040ffb0`)** is `table + index * 0x24`. `MechLoadout_ConstructWeaponMounts` hands it the weapon template's `PROJ.DAT` index, and the mount keeps the record it returns: [`weapons-dat-sim.md`](weapons-dat-sim.md#the-projdat-index--tail-relative-offset-0x1c-absolute-offset-0x3e) says which weapons take this route and which do not. Its two other callers, `Base_ArmedThinkTick` and `Flyer_AttackRun`, hand it the literal 2 for the speed they lead a target by.
- **`Proj_LookupRecord` (`0040ffc8`)** is a linear search on the first two fields, `(Type, subtype id)`, and returns the **first** match. Five callers, each passing its `Type` as a literal: `Rocket_Construct` (0), `Grenade_Construct` (3, itself with no caller found: [Open](#open)), `Bullet_Construct` (2), `Bullet_FireBurst` (4), and the loadout path for a launcher (0), which searches by the hardpoint's ammunition type.

The mount's fire dispatch hands `Rocket_Fire`, `Bullet_Fire` and `Bullet_FireBurst` only the mount's record's subtype id, and each constructor looks the record up again by `(Type, id)`. The record a shot applies damage, splash and impact effects from is therefore the first with that pair, not necessarily the one the mount holds. The mount's own record still supplies the dispatch's `Type` test, the `Speed` the AI leads by, and the figures `Ai_ChooseWeapon` scores weapons with.

Only a record that shares its `(Type, id)` with an earlier one is shadowed. Four of the 27 are: records 23–26, the last four in the file. The power a beam is fired at is still the mount's own (`min(template+0x38, mount+0x7d)`, [above](#layout)), so a shadowed laser applies the earlier record's figures at its own power; an autocannon round carries no charge and applies them as they stand.

| Record | Weapon | Same `(Type, id)` as | Figures applied | A full shot hits like |
|---|---|---|---|---|
| 23 | ATC75 | 1, ATC35 | 120 / 480 in place of 220 / 700 | ATC35 |
| 24 | ATC100 | 2, ATC50 | 180 / 600 in place of 260 / 800 | ATC50 |
| 25 | L400 | 4, L200 | 1800 / 960 in place of 3000 / 1920 | L200 × 1.2: L400's power is 120, L200's 100 |
| 26 | L500 | 5, L300 | 2000 / 1200 in place of 3000 / 2000 | L300: both fire at 120 |

Measured in retail play, at VETERAN, by reading a structure's component damage after each hit (a structure takes the armour figure alone, scaled by the difficulty's 2100): ATC75 984 a hit and ATC100 1230, which are ATC35's and ATC50's 480 and 600 rather than their own 700 and 800 (1435, 1640); LAS400 229 and LAS500 287, which are 960 and 1200 at power 120 rather than their own 1920 and 2000 (461, 479). ATC50 at 1230 and LAS300 at 287, which carry their own records, were the controls.

The subtype id also picks the round's `BULLETS.DAT` record or the beam's `BEAM.DAT` record, so the four also fly and draw as the earlier weapon. No `PROJ.DAT` record names `BULLETS.DAT` 3–5, 10 or 11 (of 12) or `BEAM.DAT` 8 or 9 (of 10), and four of those continue the shadowed weapons' families:

| Record | Weapon | Its own record | Evidence |
|---|---|---|---|
| 23 | ATC75 | `BULLETS.DAT` 10 | lifetime 14, after ATC20/35/50's 20/18/16; scatter 63 and fire sound 8 like theirs |
| 24 | ATC100 | `BULLETS.DAT` 11 | lifetime 12 |
| 25 | L400 | `BEAM.DAT` 8 | half-width 35, after L100/200/300's 20/25/30; colour 88 like theirs |
| 26 | L500 | `BEAM.DAT` 9 | half-width 40 |

`BULLETS.DAT` 10 and 11 differ from records 0–2 in one more field, `+0x0c` (0 where those carry 1), the flag `Bullet_Fire` tests to arm a per-lifetime rate whose reader is [Open](../simulation/projectiles.md#open). Both use record 2's shape.

Renumbering records 23–26 to subtype ids 10, 11, 8 and 9 fixes the damage in retail: with the renumbered file in place ([loaded loose](vol-archive.md#loose-files-on-disk-carry-no-prefix)), ATC75 and ATC100 measured 1435 and 1640 a hit under the same conditions, their own figures. The bullet and beam subtype ids the simulator tests by value are bullet 9 (the [plasma branch](../simulation/projectiles.md#the-plasma-branch): `Bullet_FirePowered`, `Bullet_TickUpdate`) and beam 1 and 7 ([ELF](../simulation/beam-visuals.md#elf-and-elf2--the-jagged-branch): `Bullet_FireBurst`, `BeamTracer_Ctor`, `BeamTracer_Draw`), none of which the renumbering touches ([Open](#open)); the fixed ids the armed bases and flyers fire are bullet 2, beam 3 and rockets 0 and 3, which still resolve to records 2, 3, 10 and 13; and an autocannon mount reports "not a launcher" (5) to the missile-lock code whatever its subtype (`WeaponMount_GetAmmoType`, `0040e644`). VSHELL names none of `PROJ.DAT`, `BULLETS.DAT` or `BEAM.DAT`.

Record 22 is claimed by two weapons, `PLAS` and `MFAC`, and resolves to itself.

## The retail records

In file order. The index is what a weapon template's `PROJ.DAT` index field names. Weapon names are the shell catalog's ([`weapons-dat.md`](weapons-dat.md)); index 22's two claimants are catalog ids 25 (`PLAS`) and 28 (`MFAC`, which the simulator's name table calls `MAGN`).

| Index | Weapon                                 | `Type` | Subtype id | Shield | Armour | Splash   | Speed |
| ----- | -------------------------------------- | ------ | ---------- | ------ | ------ | -------- | ----- |
| 0     | ATC20                                  | 2      | 0          | 60     | 360    | 0        | 5000  |
| 1     | ATC35                                  | 2      | 1          | 120    | 480    | 0        | 5000  |
| 2     | ATC50                                  | 2      | 2          | 180    | 600    | 0        | 5000  |
| 3     | L100                                   | 4      | 3          | 1500   | 600    | 0        | 0     |
| 4     | L200                                   | 4      | 4          | 1800   | 960    | 0        | 0     |
| 5     | L300                                   | 4      | 5          | 2000   | 1200   | 0        | 0     |
| 6     | EMPC                                   | 2      | 6          | 2000   | 400    | 0        | 2000  |
| 7-9   | (grenade, no weapon names it)          | 3      | 0-2        | 1000   | 1000   | 500-1000 | 1000  |
| 10-13 | MSL6/8/10, FLYMSL (by ammunition type) | 0      | 0-3        | 400    | 1600   | 500      | 6000  |
| 14    | PBW                                    | 4      | 0          | 1000   | 1000   | 0        | 0     |
| 15    | ELFW                                   | 4      | 1          | 150    | 200    | 0        | 0     |
| 16    | BEMP                                   | 2      | 7          | 8000   | 2000   | 0        | 2000  |
| 17    | BPBW                                   | 4      | 2          | 4000   | 4000   | 0        | 0     |
| 18    | BMSL                                   | 0      | 4          | 3000   | 7200   | 500      | 6000  |
| 19    | EMP2                                   | 2      | 8          | 2000   | 400    | 0        | 2000  |
| 20    | PBW2                                   | 4      | 6          | 1400   | 1400   | 0        | 0     |
| 21    | ELF2                                   | 4      | 7          | 200    | 300    | 0        | 0     |
| 22    | PLAS, MFAC                             | 2      | 9          | 3000   | 3000   | 1000     | 1000  |
| 23    | ATC75                                  | 2      | 1          | 220    | 700    | 0        | 5000  |
| 24    | ATC100                                 | 2      | 2          | 260    | 800    | 0        | 5000  |
| 25    | L400                                   | 4      | 4          | 3000   | 1920   | 0        | 0     |
| 26    | L500                                   | 4      | 5          | 3000   | 2000   | 0        | 0     |

What the figures say about the weapon families:

- **Beams** (`Type` 4) are every record with `Speed` 0. The lasers and the particle beams carry the large figures; `ELFW` and `ELF2` are the two small ones, because their mount fires them at a fixed power of 1200 ([`../simulation/weapon-mounts.md`](../simulation/weapon-mounts.md#elf-and-elf2)).
- **Autocannons** (`Type` 2, `Speed` 5000) have armour well above shield damage.
- **EMP cannons** (`Type` 2, `Speed` 2000) have shield damage well above armour damage, as the manual describes.
- **Missiles** (`Type` 0) have a splash factor of 500 throughout. The five records are the five `ROCKETS.DAT` records, and the four the `MSL` launchers reach are `SARH`/`ARH`/`ARM`/`EO` while `BMSL` takes the fifth.
- **The Plasma cannon** is the one `Type` 2 record with a splash factor, 1000, and the only `Bullet` that explodes on impact: [`../simulation/weapon-damage-types.md`](../simulation/weapon-damage-types.md#plasma-cannon).
- **`Type` 3** carries shield equal to armour on all three records. No weapon template's `PROJ.DAT` index is 7, 8 or 9 (all 33), and the one lookup by `Type` 3 is `Grenade_Construct`'s; the grenade mount's fire dispatch calls the empty `Grenade_FireNoOp` instead ([Open](#open)).

## Open

- **Open:** no reference to `Grenade_Construct` (`0040ac3c`) found by `es2_xref.py` (no rel32 branch, stored pointer or vtable slot in the image; `es2_late_entries.py` lists no late start beside it). It is the only `Proj_LookupRecord` call with `Type` 3, so the `Type` 3 records' reachability rests on it.
- **Open:** no bullet or beam subtype test by value beyond bullet 9 and beam 1 and 7 found, by a constant-comparison search of `Bullet_Fire`, `Bullet_FirePowered`, `Bullet_TickUpdate`, `Bullet_FireBurst`, `BeamTracer_Ctor`, `BeamTracer_Draw` and `BeamTracer_LifeTick`, and of every comparison against an object's `+0x41` in the decompile. Another such test would be where renumbering records 23–26 could collide.
