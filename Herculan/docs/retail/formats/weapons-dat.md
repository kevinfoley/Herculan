# WEAPONS.DAT (SHELL0/GAM) and its companion WEAPONS.BIN

Reverse-engineered from `VSHELL.EXE` disassembly and verified against real retail files. This is the SHELL0/GAM weapon **catalog** (33 entries: id 0 = NONE + 32 real weapons) — distinct from the simulator's `simvol0/dat/WEAPONS.DAT` (see [`weapons-dat-sim.md`](weapons-dat-sim.md)). Both share the same 0-32 weapon id.

> **The simulator does not read this file.** DBSIM carries its own 33-entry name table and its names > differ (`EMPC`/`SHLD` here, `EMP`/`SHIELD` there), so a cockpit fed from this catalog prints the > wrong words. Both spellings, and the full names, are in the table below.

## The weapon id space — three spellings per weapon

One 0-32 id addresses the same weapon in this catalog, in `simvol0/dat/WEAPONS.DAT` and in `player.mec`, but **three different name strings describe it** and no two agree throughout:

- the **catalog code** — a 4-6 character string stored in this file, one per record, and what the `player.mec` editor shows;
- the **simulator code** — DBSIM's own 33-entry string array at `00498eb0`, what a cockpit weapon gauge prints. See [`../simulation/weapon-mounts.md`](../simulation/weapon-mounts.md#names--weaponmount_getdisplayname-0040e18c) for why the simulator ignores the catalog's spelling and when a gauge prints neither;
- the **full name** — `WEAPONS.BIN`, the only player-facing prose form.

| id | Catalog | Simulator | Full name (`WEAPONS.BIN`) | |
|---|---|---|---|---|
| 0 | `NONE` | *(empty)* | None | empty hardpoint |
| 1 | `ATC20` | `ATC20` | Autocannon 20mm | |
| 2 | `ATC35` | `ATC35` | Autocannon 35mm | |
| 3 | `ATC50` | `ATC50` | Autocannon 50mm | |
| 4 | `ATC75` | `ATC75` | Autocannon 75mm | |
| 5 | `ATC100` | `ATC100` | Autocannon 100mm | |
| 6 | `ELFW` | `ELF` | Electron Flux | |
| 7 | `EMPC` | `EMP` | Elec.Mag. Pulse | |
| 8 | `L100` | `LAS100` | Laser 100 GW | |
| 9 | `L200` | `LAS200` | Laser 200 GW | |
| 10 | `L300` | `LAS300` | Laser 300 GW | |
| 11 | `L400` | `LAS400` | Laser 400 GW | |
| 12 | `L500` | `LAS500` | Laser 500 GW | |
| 13 | `MSL6` | `6` | Missile Rack 6X | gauge prints the loaded round, not this |
| 14 | `MSL8` | `8` | Missile Rack 8X | gauge prints the loaded round, not this |
| 15 | `MSL10` | `10` | Missile Rack 10X | gauge prints the loaded round, not this |
| 16 | `FLYMSL` | `24` | Razor Missile Rack | gauge prints the loaded round, not this |
| 17 | `PBW` | `PBEAM` | Particle Beam | |
| 18 | `ECM` | `ECM` | Radar Jammer | no projectile |
| 19 | `BEMP` | `EMP` | Bull Elec. Mag. Pulse | Bull only |
| 20 | `BPBW` | `PBEAM` | Bull Particle Beam | Bull only |
| 21 | `BMSL` | `MISSL` | Bull Missile Rack | Bull only |
| 22 | `ELF2` | `ELF2` | Electron Flux II | |
| 23 | `EMP2` | `EMP2` | Elec.Mag. Pulse II | |
| 24 | `PBW2` | `PBW2` | Particle Beam II | |
| 25 | `PLAS` | `PLAS` | Plasma Cannon | |
| 26 | `LAEW` | `LAEW` | Locust Launcher | cut — see [`../cut-content.md`](../cut-content.md) |
| 27 | `MINE` | `MINE` | Mine Launcher. | cut — see [`../cut-content.md`](../cut-content.md) |
| 28 | `MFAC` | `MAGN` | MagnetoFusion Cannon. | |
| 29 | `TARG` | `TARG` | Targeting Pod. | pod, no projectile |
| 30 | `SHLD` | `SHIELD` | Shield Pod. | pod, no projectile |
| 31 | `TURB` | `TURBO` | Turbo Pod. | pod, no projectile |
| 32 | `ENRG` | `ENERGY` | Energy Pod. | pod, no projectile |

Full names are quoted verbatim: ids 27-32 really do carry a trailing full stop in `WEAPONS.BIN`, and the lasers really are `GW` there where the codes say plain `L100`.

The three **Bull** weapons are the Cybrid four-legged HERC's own armament and can never be equipped to a player HERC in retail. They are oversized versions of the player equivalents — `BEMP` is the only weapon in the game with a barrel count of 3, and `BMSL` carries 36 rounds — so reading a `BEMP` figure as an EMP-family stat overstates the family by 4x on shields. Their sim-side templates are also the only ones giving the mount 15000 armour and a 15000 internal, where every other weapon gives 1500-2500 and 500 ([`weapons-dat-sim.md`](weapons-dat-sim.md#weaponmounttemplate-record-variable-length)).

### The rank byte, and what retail actually fits

Each record's trailing `int16` — the one that lands in `DAT_00483fa2[id]` rather than in the record — sorts the catalog, and it separates the three groups cleanly. Twenty-eight weapons hold **ranks 1-28**, a permutation with no gap or repeat — `PLAS` is 1 and `MSL6` is 28, so a low rank is an advanced weapon. `NONE` and the three Bull weapons hold **99**. `MFAC` alone holds **0**, outside the sequence in both directions.

Parsing the mech roster (row #12's ten-slot fit at `0x32`) of all 62 retail `.MSN` files — 1683 records — gives the complementary view. Eight ids are fitted by nothing:

| Never fitted | Rank | Reading |
|---|---|---|
| `ATC75` (4), `L400` (11), `FLYMSL` (16), `ECM` (18) | 12, 13, 24, 25 | ordinary player weapons; the roster is the **AI** side only, and the player's own fit comes from `player.mec` |
| `BPBW` (20) | 99 | Bull armament that no retail mission ever spawns, unlike `BEMP` (12 missions) and `BMSL` (2) |
| `LAEW` (26), `MINE` (27) | 8, 15 | inert templates — nothing could fire them anyway |
| `MFAC` (28) | 0 | a complete, working weapon that nothing in retail fits |

**The roster scan cannot settle player availability by itself** — `ATC75` and `L400` are plainly buildable and appear in it exactly as rarely as `MFAC` does. What settles it is `gam\arm_weap.dat`, the armory screen's panel layout: it lists **26 weapons**, and the seven ids with no panel are `NONE`, the three Bull weapons, `LAEW`, `MINE` and `MFAC`. A weapon with no panel cannot be queued, so those seven are unbuyable however the rank byte reads. `ATC75` and `L400` both have panels.

That also puts rank 0 in its place: `MFAC` is unbuyable because it has no panel, not because of its rank, and the rank's only established job is ordering. See [`herc-catalogs.md`](herc-catalogs.md#gamarm_weapdat), and [`../shell/armory.md`](../shell/armory.md#what-the-armory-will-sell) for the purchase path.

`LAEW`, `MINE` and `MFAC` are indexed in [`../cut-content.md`](../cut-content.md).

## WEAPONS.BIN — weapon name strings (fully confirmed, byte-exact)

Lives in `ES2/VOL/LANG0.VOL` (one copy per language folder: `ENG`, `FRE`, `GER`) — **not** in `SHELL0.VOL` alongside `WEAPONS.DAT` itself, which is why it's easy to miss when only looking at the loose-extracted `SHELL0/GAM/` tree.

Verified byte-exact against all 3 language copies of v1.0, which are identical, and v1.10's translated two:

```
0x00  uint32 count          -- 33, matches the WEAPONS.DAT catalog's real entry count exactly
0x04  uint32 stringDataSize -- size in bytes of the string-pool region below (505 for the real file:
                               579 total - 4 - 4 - 33*2 = 505, confirms this field's meaning exactly)
0x08  uint16[count] offsets -- relative offsets (from the start of the string pool, i.e. from
                               byte 0x08 + count*2) to each entry's null-terminated name string
...   string pool           -- packed null-terminated ASCII strings, one per weapon, in catalog
                               id order (offsets[0] = "None", offsets[1] = "Autocannon 20mm", etc.)
```

Read by VSHELL: `WeaponsBin_LookupName` (`00408240`) indexes it as `offsets[id] + stringPoolStart`, and `WeaponsBin_Open` (`00408605`) opens it by literal filename.

### The `.BIN` string tables

**The container is not weapon-specific.** That same open/lookup pair loads every `.BIN` file in `LANG0.VOL`; the names those two functions carry in this project's symbol set record the first file identified, not the format's scope.

| File | Handle | Entries | Contents |
|---|---|---|---|
| `weapons.bin` | `maybe_WeaponsBinFileHandle` | 33 | the weapon names above, by catalog id |
| `estext.bin` | `0046dcc0` | 342 | the shell's UI text — `[2]` `MAIN MENU`, `[0x21]` `EMPTY`, `[0x3d]` `REPAIR` |
| `esnames.bin` | local to `Squad_GenerateRoster` (`0040fa31`) | 36 | the pilot-name pool: `DUGGAN`, `BRUTUS`, `BUTCHER`, `RIGGS` … `HOYLE` |
| `missions.bin` | `0046fb34` | 61 | mission paths, `MSN\TRAIN1.MSN` … `MSN\C5_10.MSN`, indexed by `gam\career.dat` |
| `wpn_info.bin` | — | 130 | the armory's stat panel, **5 strings per armory slot** |
| `wpn_desc.bin` | `0048d728` | 111 | the weapons screen's description, **3 strings per weapon id**, then 3 per guidance kind |

All six walk byte-exact against the retail files of both builds, in every language: `8 + count*2 + poolSize` equals the file length in every case, and each translation has the English entry count.

`wpn_info.bin` is indexed by **`gam\arm_weap.dat`'s panel order, not by catalog id** — 26 slots of five strings each. Slot 0's five are `Type: Autocannon 20 mm`, `Range: 450 m Salvage Required: 5,000 kg`, an empty line, and two description lines. Their `Salvage Required` figures agree with this file's `0x14` price times 1000 for all 26 slots, which is what confirms both the indexing and the unit. See [`herc-catalogs.md`](herc-catalogs.md#gamarm_weapdat).

**The folder is the shell's language.** `WeaponsBin_Open` prefixes every name with `eng\`, `fre\` or `ger\` by `Shell_Language` (`0048227a`), which `-f` and `-g` set ([`../retail-builds.md`](../retail-builds.md#how-a-language-is-chosen)).

**v1.0's `LANG0.VOL` is not localized.** Every entry — all six `.BIN` files and `CAMPAIGN.STR` — is byte-identical across the `ENG\`, `FRE\` and `GER\` directories, three copies of the English text. v1.10's `FRE\` and `GER\` translate `estext.bin`, `weapons.bin`, `wpn_desc.bin`, `wpn_info.bin` and `CAMPAIGN.STR`, and keep the English `esnames.bin` and `missions.bin`.

All 33 names are in the id-space table above, transcribed from `WEAPONS_ENG.BIN`.

## WEAPONS.DAT catalog record (29 bytes)

Loaded by `VSHELL.EXE`'s `LoadWeaponsDat` (`00411fc4`, file-level) → `WeaponsDat_ReadRecord` (`00411d57`, per-record). Stored in-memory as a flat array at `DAT_00483be4`, 29-byte (`0x1d`) stride per weapon id (confirmed via `IMUL EDX,EDX,0x1d` in `Armory_ScrapValueTons`).

```
0x00–0x0f  (16 bytes) the catalog code, NUL-terminated ASCII. The file supplies strlen+1 bytes of
           it behind an int16 length, so the stored block is short and the rest of the buffer is
           whatever the zero-initialized array held
0x10–0x13  pointer to this weapon's WEAPONS.BIN full name, filled at load from
           WeaponsBin_LookupName(handle, id) — runtime only
0x14–0x15  uint16 price in tons, scaled ×1000 at load to give the cost in kg. Used in
           cost calculations (Armory_DeliverQueue (00412428), Armory_ScrapValueTons (0041266a)) as (price/1000) × field_0x17 / 10
0x16       byte — the player's unlock flag for this weapon (see below)
0x17–0x18  short — how many units of this weapon the player owns. Not a catalog value: it counts
           the runtime list at 0x19, and Armory_ScrapValueTons multiplies it by the price to value the stock
0x19–0x1c  head of that owned-unit list ({ entry*, next* } nodes, 10-byte entries), runtime only —
           which is why the file never supplies it
```

**The whole record is accounted for**, and none of it holds a numeric weapon stat: 16 bytes of code, a name pointer, a price, a flag, an owned count and a list head is 29 exactly. Damage, heat, rate of fire and range live on the simulator side ([`weapons-dat-sim.md`](weapons-dat-sim.md)); the only range figure the shell has is the prose in `wpn_info.bin`.

`0x16`, `0x17` and the `0x19` list are the armory's per-weapon inventory, and the save file is where they persist: [`save-games.md`](save-games.md) documents the block that writes all 33 of them, and the armory itself is `armory.cpp` (`Armory_AddUnit` (`00411efd`) appends a purchased unit to the list).

### `0x16` is the weapon-unlock flag

The catalog ships a starting value per weapon; from then on it is campaign state, and four VSHELL sites establish what it means:

- **Campaign progress sets it**, at the debrief — [below](#campaign-grants--armory_grantcampaignweapons-004126be).
- **The armory screen gates its rows on it.** Where it is `0` the weapon's row is disabled — `+0x49` cleared — and its four text columns, the `0x14` price among them, drawn in the background colour, so the list shows a gap. Where it is `1` the row is live and readable ([`../shell/build-and-armory.md`](../shell/build-and-armory.md#the-armory-rows)).
- **Purchasing skips a locked weapon**, whatever the player can afford.
- One HERC-fit check refuses to accept the weapon while the flag is clear.

All 33 flags are also exported to `data\player.mec`, which nothing traced reads back — see [`../shell/campaign-loop.md`](../shell/campaign-loop.md).

Retail ships fourteen weapons locked: `ATC75`, `ATC100`, `L400`, `L500`, the three Bull weapons, `LAEW`, `MINE`, `MFAC` and the four pods. Eight of them can be unlocked; the Bull weapons, `LAEW`, `MINE` and `MFAC` have no flag slot and stay locked for the whole campaign.

### Campaign grants — `Armory_GrantCampaignWeapons` (`004126be`)

The debrief runs this in a campaign only, right after the chassis grant `Herc_GrantUnlocks` ([`herc-catalogs.md`](herc-catalogs.md#chassis-unlocks--herc_grantunlocks-004118c5)), on the flag array just read back from `data\mission.var` — so the flags it tests are the ones the mission itself left ([`../shell/campaign-loop.md`](../shell/campaign-loop.md#the-debrief--game_processmissionresults-0040eae7)). It makes two passes over that array, from three static tables.

**Unlocks.** Two 33-entry `int16` tables, indexed by weapon id: a flag slot at `0046fa82` (`-1` for none) and the value that slot must hold at `0046fa40`.

```
for id in 0..32:
    slot = unlockSlot[id]
    if slot != -1 && weapons[id].+0x16 == 0:
        if flags[slot] == unlockValue[id]: weapons[id].+0x16 = 1
        flags[slot] = 0
```

| Weapon | Flag slot | Value |
|---|---|---|
| `ATC75` (4) | `0x34` | 2 |
| `ATC100` (5) | `0x35` | 1 |
| `L400` (11) | `0x32` | 1 |
| `L500` (12) | `0x33` | 1 |
| `SHLD` (30) | `0x38` | 1 |
| `TARG` (29) | `0x39` | 1 |
| `TURB` (31) | `0x3a` | 2 |
| `ENRG` (32) | `0x3b` | 1 |

The slot is cleared whether or not it matched, so a flag holding the wrong value at one debrief is spent. Once a weapon is unlocked its slot is no longer tested or cleared. The slots sit directly below the chassis grant's `0x3c`–`0x3f`.

**Unit grants.** For each flag slot `s` from `0x15` to `0x31`, the debrief adds `flags[s]` new units of the weapon `0046f8e0[s - 0x15]` names to the armory stock, each through `Armory_AddNewUnit` (`0041229d`) — the helper the `results.dat` salvage pairs use — as `{ id, 100, 100, 5 }`: condition 100 and ammo type 5 whatever the weapon, missile racks included. It never clears these slots, but the simulator's mission load zeroes `0x15`–`0x2a` ([`../simulation/mission-deployment.md`](../simulation/mission-deployment.md#the-mission-counters--dat_004a9ef4)), so each debrief grants what the mission just flown left there and the save keeps those values until the next launch. The function returns the number of units added, which the debrief adds to the salvage-pair count it hands the report (`Debrief_WriteReport` (`0040f34c`)).

| Slot | Weapon | Slot | Weapon |
|---|---|---|---|
| `0x15` | `ECM` (18) | `0x20` | `EMPC` (7) |
| `0x16` | `ATC50` (3) | `0x21` | `ELFW` (6) |
| `0x17` | `ATC75` (4) | `0x22` | `PBW2` (24) |
| `0x18` | `ATC100` (5) | `0x23` | `EMP2` (23) |
| `0x19` | `L300` (10) | `0x24` | `ELF2` (22) |
| `0x1a` | `L400` (11) | `0x25` | `PLAS` (25) |
| `0x1b` | `L500` (12) | `0x26` | `MFAC` (28) |
| `0x1c` | `MSL10` (15) | `0x27` | `TARG` (29) |
| `0x1d` | `MINE` (27) | `0x28` | `SHLD` (30) |
| `0x1e` | `LAEW` (26) | `0x29` | `TURB` (31) |
| `0x1f` | `PBW` (17) | `0x2a` | `ENRG` (32) |

A granted unit does not unlock its weapon: the stock and `+0x17` count grow, and the armory row stays disabled until the unlock pass sets `+0x16`.

**The loop overruns the table.** It runs to slot `0x31`, seven entries past the 22 the table holds, and those seven words — `10169`, `65`, `8199`, `0`, `-4`, `-1`, `0` — are not weapon ids. A nonzero flag in `0x2b`, `0x2c`, `0x2d`, `0x2f` or `0x30` would append a unit to a list outside the record array; one in `0x2e` or `0x31` would stock `NONE`, and since the mission load does not zero these seven, at every debrief after. No retail campaign reaches it. The flag array starts a career zeroed, and none of the 1,358 counter pairs in the 62 `.MSN` files' actions, rosters, groups and objectives names one of the seven, nor does any header patch, whose clear lists are all empty ([`msn-mission-file.md`](msn-mission-file.md#the-header-patch--row-2)). Neither executable addresses them directly, and all nine retail saves hold them at 0.

The retail saves agree with both passes. Across `GAME_0`–`GAME_6` the only locked weapons to turn unlocked are table entries — `ATC75`, `ATC100`, `L400`, `L500` and `TARG` by `GAME_2`, `TURB` by `GAME_4`, `SHLD` by `GAME_5` — and every unlock slot from `0x32` to `0x3b` holds 0. The unit-grant slots persist: `0x16` and `0x1c` hold 1 in all seven saves, and the `ATC50` and `MSL10` stocks those slots feed grow from 4 to 79 and from 2 to 65 over them.

## File-level format

Verified byte-exact: the walk below consumes all 707 bytes of the retail file.

```
int16   record count -- 33
per record:
  int16       weapon id
  int16       blockLen
  blockLen B  the catalog code, NUL included -- read into 0x00 of the record above
  int16       price in tons
  byte        unlock flag
  int16       the rank byte (see below)
int16   starting stock count -- 39
        that many 6-byte weapon unit records: { int16 id; int16 condition; int16 ammo type }
```

Because the code is length-prefixed and the trailer is fixed, walking name-then-trailer from the first record recovers all 33 codes in id order. That walk produced the id-space table above; the codes never need hand-transcribing.

The rank goes into a *separate* parallel array (`DAT_00483fa2[id]`), not into the 29-byte struct.

The trailing block is the **armory's starting stock**: `LoadWeaponsDat` allocates one ten-byte weapon unit per entry, reads three of its fields, and appends it to that catalog record's `0x19` list through `Armory_AddUnit` (`00411efd`) — so the player opens a career owning these 39 units. Retail's are four `ATC50`, three `ECM` and two each of sixteen other ids, all at condition 100. The unit record is shared with the HERC mounts and the save's armory stock; see [`herc-catalogs.md`](herc-catalogs.md#the-weapon-unit-record).

## Rejected readings

| Reading | Why it is wrong |
|---|---|
| `0x00–0x13` is a stat block holding damage, range or rate of fire | It is a 16-byte buffer for the catalog code plus a 4-byte runtime name pointer. `WeaponsDat_ReadRecord` reads `blockLen` bytes into `0x00` where `blockLen` is `strlen(code)+1`, and `LoadWeaponsDat` writes `0x10` from `WeaponsBin_LookupName`. The record carries no numeric stat but the price |
| `0x17` is an ammo or round count | It is the number of units the player owns. The save writes it as the length of the `0x19` linked list and then serializes exactly that many 10-byte units (`FUN_00411e64`), and the reader allocates that many nodes back. Round counts do exist — `BMSL` carries 36 — but they live on the simulator side |
| `0x19–0x1c` is unread file data | It is a runtime list head. `WeaponsDat_ReadRecord` fills only `0x00`–`0x16` from the file — 23 of the 29 bytes; the last six are filled by the armory and the save loader |
| The file ends after the 33rd record | 236 bytes of starting stock follow it, and a reader that stops early silently drops the player's opening inventory |

## How to apply

Both files are fully decoded and safe to write transformers for directly. `WEAPONS.BIN` is the smaller win — 579 bytes, a trivial indexed string table, and the same shape for all six `.BIN` files, so one reader serves them all.

`WEAPONS.DAT` needs a transformer that keeps two things straight. The 29-byte in-memory record is **not** the on-disk record: only `0x00`–`0x16` come from the file, the code block is length-prefixed and variable, and the rank lives in a parallel array rather than in the struct. And the file does not end with the catalog — a round-trip that drops the 39-unit starting stock loses the player's opening inventory silently, since nothing about the file's length gives it away.
