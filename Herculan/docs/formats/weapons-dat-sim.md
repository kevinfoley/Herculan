# simvol0/dat/WEAPONS.DAT (sim-side weapon mount template table)

Distinct from `SHELL0/GAM/WEAPONS.DAT` (the UI-facing weapon catalog, see `docs/formats/weapons-dat.md`). This is DBSIM.EXE's runtime weapon-mount table, loaded by `Weapons_LoadResourceTables` (`0x0040fc8c`) via a resource literally named `"weapons"`. The layout below reads the retail file byte-exact.

## Structure

```
0x00  UINT16  count          -- 33 in the real file, matches SHELL0/GAM/WEAPONS.DAT's catalog count
0x02  template[count]        -- variable-length records, back to back (NOT a fixed stride --
                                       see below)
```

### `WeaponMountTemplate` record (variable length)

Built entirely from **reused low-level record readers**: `HercPiece_ReadRecord` (see [`dmg-damage-file.md`](dmg-damage-file.md#the-piece-record)), `Collision_ReadCluster`, `Collision_ReadSphereArray` (see `docs/formats/collision-spheres.md`). In-memory struct is 88 bytes (`0x58`), but on-disk record is variable-length; extra in-memory bytes are runtime-only (a pointer + self-index the loader fills in after reading).

Read order (all fields little-endian):

```
+0   short               -- 0 for id0/NONE; one of {1500, 2000, 2500, 15000} for every real weapon
                             seen -- too few distinct values to be a per-weapon-unique stat ([Open](#open)).
+2   short               -- 0 for NONE; exactly -1 (0xFFFF) for every real weapon seen.
+4   short               -- 0 for NONE; exactly 0x01FF (511) for every real weapon seen.
+6   short dependentCount -- 0 for NONE, 1 for every real weapon seen.
     dependentCount*4 bytes -- that many raw 16-bit pairs, present only if the count != 0. Always
                             exactly (20, 12) in every real weapon record seen. This is
                             HercPiece_ReadRecord's "dependent sub-component list" mechanism
                             reused generically; for weapons it never varies ([Open](#open)).
     short clusterComponent   -- read via Collision_ReadCluster; constant 0x13 (19) in EVERY
                                  real record seen, including id0/NONE. In a real collision model
                                  this field is the component index; here it never varies ([Open](#open)).
     short sphereCount        -- read via Collision_ReadSphereArray; real count is this value
                                  masked with 0x1FFF (top 3 bits are reserved for flags in the
                                  original collision-record format; never observed set here). 0 for
                                  NONE.
     (sphereCount & 0x1FFF) * 8 bytes  -- present only if the masked count != 0. Each 8-byte
                                  entry is 4 int16s. Pattern suggests (offsetish, offsetish,
                                  0-or-small, rate-ish) tuples ([Open](#open)).
+0x22 (relative) 48 raw bytes (0x30)  -- decoded fields below ([Open](#open)). Two
                             bytes at relative offset 0x26 are zeroed in memory at runtime (not
                             real file data).
```

In-memory boundary confirmed: front block 0x00-0x11, sub-sphere/sub-mesh block 0x12-0x21, tail 0x22-0x51, runtime-only pointer at 0x52, self-index at 0x56. Total 0x58 (88) bytes.

## Decoded tail fields

Offsets are absolute in-memory (tail-relative = absolute − 0x22).

| Absolute | Field | Read by |
|---|---|---|
| `0x2c` | **minimum engagement range**, int32. **Zero in all 33 retail records** | `WeaponMount_RangeAllows` |
| `0x30` | **range**, int32, in world units | `WeaponMount_FireDispatch_GunBeam`, `WeaponMount_RangeAllows` |
| `0x34` | the AI's **shot-value penalty**, subtracted from the damage credit when it picks a hardpoint. 500 the MSL launchers, 600 BMSL; 150 the EMP cannons, the particle beams, PLAS and MAGN; 10, 20 or 30 the autocannons and lasers by size; 5 the ELFs; 1 the big EMP (id 19); 0 a pod | `Ai_ChooseWeapon` — [`../simulation/ai-weapons.md`](../simulation/ai-weapons.md) |
| `0x36` | energy fire threshold, low | `WeaponMount_EnergyCanFire` |
| `0x38` | energy fire threshold, high, **and the per-shot cost** — for an ammunition mount, rounds per shot | `WeaponMount_EnergyCanFire`, both fire dispatchers |
| `0x3a` | magazine size | `WeaponMount_CtorAmmunition` (`0040e140`) |
| `0x3c` | barrel count; `3` fires three shots spread along the muzzle offset's own X | `WeaponMount_FireDispatch_GunBeam` |
| `0x3e` | **`PROJ.DAT` index**, below | `MechLoadout_ConstructWeaponMounts` |
| `0x40`–`0x44` | muzzle offset, three int16, in the firing bone's space | `WeaponMount_PrepareShot` |
| `0x46` | lateral muzzle offset, for a side-mounted hardpoint | `WeaponMountTemplate_SideMuzzleOffset` |
| `0x4a` | vertical muzzle offset, for a top- or bottom-mounted one | `WeaponMountTemplate_SideMuzzleOffset` |
| `0x4c` | refire delay, in sim timer units | `WeaponMount_PrepareShot` |
| `0x50` | **damage-detail icon**, the `WEAPONS` bank frame before the `.PDG` hardpoint's offset; -1 draws none. 0 the ELFs, 1 laser, 2 autocannon, 3 EMP, 4 particle beam, 5 missile, 6-11 LAEW, ENERGY, ECM, TARG, SHIELD, TURBO, 12 MINE, 13 PLAS and MAGN | `PaperDoll_BuildWeaponIcons` — [`cockpit-hud-widgets.md`](cockpit-hud-widgets.md#weapon-icons) |
| `0x22`–`0x28` | **weapon model**, four `MECHWPNS.DTS` shape indices, one per `.GL +6` mounting code | `WeaponMount_ShapeForMountingCode` (`0040fab0`) |

`0x22`–`0x28` are read as `template[0x22 + code * 2]`, where `code` is the hardpoint's mounting byte: the same gun modelled for the four ways it can hang off a chassis, so `ATC20` reads four different shapes and a shoulder launcher reads one shape four times. Mounting code 4 is the invisible hardpoint and has no entry — nothing is drawn for it. **The shape's cell animation is the muzzle flash**; see [`../simulation/weapon-mounts.md`](../simulation/weapon-mounts.md#the-muzzle-flash).

`0x30` is the ray length the beam dispatch hands `Bullet_FireBurst`. It is also the value `WeaponMounts_ToggleChainMember` (`004110ac`) requires to be positive before it will put a hardpoint into a fire chain, and every pod carries zero, so that gate holds on it too. Retail values run 75000 (ATC20) down to 15000 (ELF2) — 450 m to 90 m at the simulation's own scale, which does *not* match the manual's 20 m figure for the ELF.

`0x36`/`0x38` are the energy readiness threshold pair; how a mount combines them with its charge target is in [`../simulation/weapon-mounts.md`](../simulation/weapon-mounts.md#energy). `0x38` is also what a shot costs, so the two shapes real data takes — an equal pair (LAS100 80/80) and a small low against a 10000 high (PBEAM 300/10000) — are a fixed-cost weapon and a charge-up one, see [`../simulation/weapon-firing.md`](../simulation/weapon-firing.md#the-beam-branch). The ELFs are a third shape, (400, 70), that their own mount class reads differently: [`../simulation/weapon-mounts.md`](../simulation/weapon-mounts.md#elf-and-elf2).

`0x3a` is both the round count an ammunition mount powers up with and its cap (ATC20 2000, ATC35 1500, ATC50 1000, ATC75 750, ATC100 500, MSL6/8/10/24 6/8/10/24, MISSL 36, PLAS 20, LAEW 0), and the ammunition dispatch spends `0x38` rounds per shot.

`0x4c` is 1200 on most weapons — about 15 sim ticks — and **zero on `ELF` and `ELF2`**, whose own mount class does not consult the refire timer at all; what paces those two is their capacitor. The mount scales it by its own `+0x63`, `0x400` unless a damaged gun has lowered it ([`../simulation/weapon-mounts.md`](../simulation/weapon-mounts.md#the-certain-path--the-condition-notification)).

`0x3c` is 1 everywhere except catalog id 19 (the big EMP), where it is 3. `0x3e == 0x13` is true for exactly one weapon too — id 23, `EMP2` — because the value is that weapon's own `PROJ.DAT` row; the gun dispatch reads it as a burst flag. The two conditions therefore pick out different weapons. See [`../simulation/weapon-firing.md`](../simulation/weapon-firing.md#the-gun-branches).

See [`../simulation/weapon-mounts.md`](../simulation/weapon-mounts.md) for the mount fields and [`../simulation/weapon-firing.md`](../simulation/weapon-firing.md) for the fire path.

## `+0x52` and `+0x56` — runtime-only, written by the loader

Neither is file data. `Weapons_LoadResourceTables` writes the record's own table index into `+0x56` — which is what identifies the sim table and the shell catalog as sharing one 0-32 weapon id — and a pointer from a 33-entry string array at `00498eb0` into `+0x52`. That pointer is the name a weapon gauge prints, and it is **not** the shell catalog's name for the same id. See [`../simulation/weapon-mounts.md`](../simulation/weapon-mounts.md#names--weaponmount_getdisplayname-0040e18c).

## The `PROJ.DAT` index — tail-relative offset 0x1c, absolute offset 0x3e

Answers how a weapon id maps to a `PROJ.DAT` record. Read via `WeaponMountTemplate_GetByWeaponId` (`0x0040fe84`) and `MechLoadout_ConstructWeaponMounts` (`0x0040fff8`). Both `simvol0/dat/WEAPONS.DAT` and `SHELL0/GAM/WEAPONS.DAT` share the same 33-entry weapon-id indexing.

- **`0x21` (33) -- no `PROJ.DAT` lookup.** Real case: `ECM` (electronic-warfare, no projectile).
- **`0x22` (34) -- resolved via `Proj_LookupRecord(category=0/*Rocket*/, secondaryKey)`**, a `(category, subtypeId)` search. Real cases: `MSL6`, `MSL8`, `MSL10`, `FLYMSL` (tube/rack missile launchers). The secondary key is the hardpoint's own ammunition type out of the mission file's second loadout array — see [`../simulation/weapon-mounts.md`](../simulation/weapon-mounts.md).
- **Otherwise -- direct flat array index into `PROJ.DAT`** (`index * 0x24 + ProjDat_RecordTable`, via `Proj_LookupRecordByIndex` at `0x0040ffb0`). Confirmed for all other real weapons.
- **0 for non-firing entries** (`NONE`, `LAEW`, `MINE`, `TARG`, `SHLD`, `TURB`, `ENRG`). Field is inert for passive stat-boost systems. `LAEW` coincidentally resolves to index 0 (`ATC20`).

The records each index reaches: [`proj-dat.md`](proj-dat.md#the-retail-records), which also says how a fired shot resolves its record a second time.

## Open

- **Open:** what the first word (`+0`) means — it looks like a tier, and it is **not** the range, which is `0x30`.
- **Open:** whether the dependent pair and the cluster component word, both reused constant fields from `.DMG`/`.COL`, carry any real per-weapon value.
- **Open:** the firing-sequence tuple fields' exact meaning.
- **Open:** `0x4e` (200 for LAS100 rising to 800 for the big launchers).
- **Open:** the rest of the tail outside the fields decoded above.
