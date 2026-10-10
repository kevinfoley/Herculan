# The HERC and armory catalogs — `SHELL0/GAM`

The forty files in `SHELL0.VOL` under `GAM\` are the shell's static data: the HERC chassis catalog, the armory's stock and price tables, and the screen layouts for the construction, armory and repair-bay screens. **Every address in this doc is in `VSHELL.EXE`**, and the shell's source module is named in the assertion strings for each function cited.

Every format here is verified byte-exact against the retail files — each parse consumes its file to the last byte.

For the campaign table `gam\career.dat` see [`../shell/campaign-loop.md`](../shell/campaign-loop.md); for the weapon catalog `gam\weapons.dat` see [`weapons-dat.md`](weapons-dat.md); for what the economy does with the numbers here see [`../shell/armory.md`](../shell/armory.md).

## The chassis type space

Nine chassis, addressed by a `0`–`8` type id that every file in this cluster and the save share. The display name is `estext.bin` string `0x6e + type` (`HercRecord_ResolveName`, `00410e79`'s tail via `00410e31`), which fixes the order:

| Type | Name | `INI_`/`ARM_`/`RPR_` file stem | Hardpoints |
|---|---|---|---|
| 0 | Outlaw | `OUTL` | 3 |
| 1 | Raptor II | `RAPT` | 5 |
| 2 | Tomahawk | `TOMA` | 5 |
| 3 | Samson | `SAMS` | 8 |
| 4 | Colossus | `COLO` | 9 |
| 5 | Apocalypse | `APOC` | 9 |
| 6 | Ogre | `OGRE` | 10 |
| 7 | Maverick | `MAVR` | 4 |
| 8 | Razor | `RAZR` | 7 |

The three per-chassis file families are each reached through a nine-entry pointer table of literal filenames, all three beginning at `OUTL` — `0046ff2c` (`arm_`), `0046fe94` (`rpr_`) and `004706fc` (`ini_`) — which is the same ordering read a second way.

**Hardpoint capacity comes from the code, not the data.** `Herc_CapacityForType` (`00410d54`) maps the type through `Herc_TypeToIndex` (`00410d30`) — an identity search over the nine-entry table at `0046f728` — and returns `0046f73a[type]`, the column above. `herc_inf.dat` carries its own hardpoint field and the two disagree for the Raptor II; see [Rejected readings](#rejected-readings).

## `gam\hercs.dat` — the starting hangar

`LoadHercsDat` (`004104ed`, `herclist.cpp`) fills the hangar object at `Hangar_BayRecords` (`00482ac3`) at new-career time. The hangar is eight pointer slots at `+0x00`, an occupied count at `+0x20`, the selected bay at `+0x22` — the bay the squad panel shows, and the one the build screen buys into and the scrap dialog scraps ([`../shell/build-and-armory.md`](../shell/build-and-armory.md#the-scrap-dialog)) — and [the debrief's scrap count](../shell/campaign-loop.md#the-debrief--game_processmissionresults-0040eae7) at `+0x24`; `00482ae3`, `00482ae5` and `00482ae7` reach the last three directly. `HercsDat_Clear` (`0041048b`) empties all of it before every load.

```
int16   count
per entry:
  int16   hangar slot, 0-7
          one HERC catalog record (below)
```

Retail's 148 bytes are four Outlaws in slots 0–3 and a Razor in slot 4:

| Slot | Chassis | Build % | Missions left | Hardpoint fits (id, ammo type) |
|---|---|---|---|---|
| 0 | Outlaw | 100 | 0 | `ATC50`, `ATC50`, `EMPC` |
| 1 | Outlaw | 100 | 0 | `L300`, `L300`, `MSL8` ammo 1 |
| 2 | Outlaw | 100 | 0 | `L300`, `L300`, `ELFW` |
| 3 | Outlaw | 100 | 0 | `ATC50`, `L300`, `ECM` |
| 4 | Razor | 0 | 3 | none |

Slot 4 is a chassis **under construction**, not a damaged one: 0% built with three missions still to run is exactly the state `Herc_Order` (`00411019`) leaves behind when the player buys one. A new career therefore opens with a Razor already on the slipway, three missions out.

### The HERC catalog record

`HercRecord_ReadCatalogForm` (`00410e79`, `herc.cpp`) reads the `gam\*.dat` form. It is the same 122-byte in-memory record the save serializes, but the file supplies only four of its fields:

```
int16   +0x00   chassis type, 0-8
int16   +0x4a   build progress, percent complete
int16   +0x78   build time remaining, in missions
int16   +0x4e   hardpoints occupied
        that many { int16 hardpoint index; weapon unit, 6 bytes }
```

Everything else comes from the constructor (`00410d7a`) or is derived after the read: `+0x4c` from `Herc_CapacityForType`, `+0x02` from `Herc_TypeToIndex`, the name pointer at `+0x04` from `estext.bin`, and the whole 66-byte status block left at the constructor's uniform 100. A chassis loaded from one of these files is therefore always at full condition.

For the full 122-byte record and the status block's three condition arrays see [`save-games.md`](save-games.md#herc-record--122-bytes-0x7a-in-memory).

**Hardpoints serialize sparsely**, occupied entries only, each preceded by its index — so a reader must use the count and cannot assume dense packing. Retail's `INI_APOC.DAT` exercises this: it fits hardpoints 0–5 and 7, leaving 6 empty.

### The weapon unit record

One mounted or stocked weapon. Ten bytes in memory, five `int16`, constructed by `WeaponUnit_Ctor` (`004119d8`) as `{ -1, -1, 100, 100, 5 }`:

| Offset | Field |
|---|---|
| `+0x00` | weapon catalog id |
| `+0x02` | armory class index, derived by `Weapon_ClassIndexForId` (`004119b4`) whenever a unit is constructed. The catalog form leaves it out; the save form writes it and reads it back as written |
| `+0x04` | the hardpoint condition the unit is fitted at: `Herc_FitMount` (`004114ec`) writes it into the machine's status block ([`../shell/weapons-and-repair.md`](../shell/weapons-and-repair.md#fitting-a-weapon)). 100 from the constructor, and the catalog form leaves it there |
| `+0x06` | condition |
| `+0x08` | ammo type — the guidance kind |

Two file forms share it. The `gam\*.dat` form is six bytes — `+0x00`, `+0x06`, `+0x08` (`WeaponUnit_ReadCatalogForm`, `00411a36`) — and the save form is all ten (`WeaponUnit_ReadSaveForm` (`00411aa6`) / `WeaponUnit_WriteSaveForm` (`00411aff`)).

`Weapon_ClassIndexForId` searches the thirty-entry table at `0046f868` for the id and returns its position. That table holds ids `0`–`18` and `22`–`32`: **every id except the three Bull weapons**, which therefore resolve to `-1`. It is an independent statement of the same exclusion `arm_weap.dat` makes below.

Ammo type `0`–`3` are the guidance kinds `SARH`, `ARH`, `ARM`, `EO`; `5` means the hardpoint carries nothing guided. The weapons screen's four buttons caption them `ARM`, `ARH`, `SARH`, `EO` (`estext.bin` `0xa1`–`0xa4`), which is not the kinds' order: each button passes its own kind ([`../shell/weapons-and-repair.md`](../shell/weapons-and-repair.md#guidance-kinds)). Retail data holds `5` on every unit that is not a missile rack, and the racks vary by file: the eight in `weapons.dat`'s starting stock (two each of ids 13–16) hold `0`, `hercs.dat`'s one `MSL8` holds `1`, and the four fitted in the `ini_*.dat` files and the four in `trn_herc.dat` hold `5`. `Armory_DeliverQueue` (`00412428`) writes `1` for ids 13–16 and `5` for everything else, and the weapons screen rewrites it when it fits a unit or a guidance button is pressed. The `5` that [`../simulation/weapon-mounts.md`](../simulation/weapon-mounts.md) observes in every non-launcher slot originates here.

## `gam\ini_*.dat` — the stock fit per chassis

Nine files, one per type, each a **bare HERC catalog record** with no leading hangar slot. `MsnGen_BuildPlayerHerc` (`0041c58d`) reads one, through the nine-entry table at `004706fc`, to build a practice mission's player machine when the practice screen's Herc Type option chooses the chassis ([`../shell/main-menu.md`](../shell/main-menu.md#starting-a-practice-mission)). A chassis bought in the campaign is not fitted from it: `Herc_Order` builds the record with no hardpoints occupied ([`../shell/armory.md`](../shell/armory.md#buying-a-chassis--herc_order-00411019)). All nine consume exactly, and each file's leading type field matches its filename stem.

## `gam\trn_herc.dat` — a second stock-fit set

The same record again, nine of them for types 0–8, with **no count prefix** — the file is a bare sequence terminated by EOF, and 432 bytes consume exactly. Every record reads 100% built with 0 missions remaining, and the hardpoint counts match the nine `ini_*.dat` files one for one.

Four of the nine fits are byte-identical to the matching `ini_*.dat`. The other five differ in exactly one hardpoint, and always by upgrading it:

| Chassis | `ini_*.dat` | `trn_herc.dat` |
|---|---|---|
| Outlaw | `PBW` | `PLAS` |
| Tomahawk | `PBW` | `PLAS` |
| Samson | `PBW` | `MFAC` |
| Apocalypse | `PBW` | `MFAC` |
| Ogre | `EMP2` | `MFAC` |

**This is the only place in retail data that fits `MFAC` to a player machine.** The weapon has no armory panel and ships locked, so nothing in the purchase path can reach it ([`../cut-content.md`](../cut-content.md)) — but three chassis here carry it.

That does not make it reachable: the literal `trn_herc.dat` appears in neither `VSHELL.EXE`'s nor `DBSIM.EXE`'s data segment, and no reference to it appears in the VSHELL decompile, where the sibling `arm_`, `rpr_` and `ini_` families are each reached through a nine-entry table of literal filenames. A path assembled at runtime cannot be ruled out from string evidence alone ([Open](#open)).

## `gam\herc_inf.dat` — the chassis stat table

`LoadHercInfDat` (`0041181c`, `hercinfo.cpp`) reads a count and then bulk-reads `count << 4` bytes into `00483b54` — a flat sixteen-byte stride, nine records, 146 bytes exactly.

Both the stride and the price field's offset are confirmed in raw disassembly rather than from decompiler pointer math: `Herc_ScrapValue` addresses the table as `shl edx, 4` followed by `movsx ecx, word [edx + 0x00483b5c]` (`00413b93`), which is a 16-byte stride reaching `0x00483b54 + 8`.

```
int16   count -- 9
per record, 16 bytes:
  int16   +0x00   chassis type, 0-8
  int16   +0x02   mass, tons
  int16   +0x04   top speed, kph
  int16   +0x06   hardpoints
  int16   +0x08   price, in tons -- x1000 gives the salvage cost
  int16   +0x0a   meaning open -- see Open
  int16   +0x0c   build time, in missions
  int16   +0x0e   availability flag
```

The first four stats are exactly what the Herc Construction screen prints, which is what names them: `Herc_BuildScreenRefresh` (`00446cfa`) formats `+0x02` as `"%d TONS"`, `+0x04` as `"%d KPH"`, `+0x06` bare, and `+0x08` as `"%d TONS"` against the labels `Mass`, `Speed`, `Hardpoints` and `Salvage Reqd` (`estext.bin` `0xbb`, `0xbe`, `0xbf`, `0xc0`).

| Chassis | Mass | Speed | Hardpoints | Price | `+0x0a` | Build | Available |
|---|---|---|---|---|---|---|---|
| Outlaw | 27 | 80 | 3 | 60 | 40 | 1 | yes |
| Raptor II | 40 | 74 | 4 | 90 | 50 | 2 | no |
| Tomahawk | 45 | 71 | 5 | 100 | 60 | 2 | yes |
| Samson | 63 | 52 | 8 | 170 | 95 | 2 | yes |
| Colossus | 77 | 46 | 9 | 200 | 110 | 3 | yes |
| Apocalypse | 70 | 60 | 9 | 190 | 100 | 3 | yes |
| Ogre | 80 | 50 | 10 | 230 | 125 | 3 | no |
| Maverick | 25 | 85 | 4 | 55 | 30 | 1 | no |
| Razor | 50 | 200 | 7 | 120 | 70 | 3 | no |

`+0x08` is the record's load-bearing field: `Herc_Order` (`00411019`) returns `herc_inf[type].+0x08 * 1000` as the price to charge, and the purchase screen tests the salvage pool against the same product.

`+0x0c` is copied into the new chassis's `+0x78` by `Herc_Order`, and `Herc_BuildTick` (`00411086`) decrements it and recomputes the percentage:

```
+0x78 -= 1;
+0x4a = ((herc_inf[type].+0x0c - +0x78) * 100) / herc_inf[type].+0x0c;
```

So `+0x78` counts missions down to delivery and `+0x4a` is the `% Complete` figure (`estext.bin` `0x6c`) the hangar shows against it.

`+0x0e` is the **chassis availability flag**, and it is campaign state rather than a catalog constant: the four chassis that ship as `0` are exactly the four `Herc_GrantUnlocks` (`004118c5`) can turn on. It is the array the save persists — see [`save-games.md`](save-games.md#savgame_sav--block-order) — and the purchase screen refuses a chassis whose flag is clear.

### Chassis unlocks — `Herc_GrantUnlocks` (`004118c5`)

The campaign debrief runs it ([`../shell/campaign-loop.md`](../shell/campaign-loop.md#the-debrief--game_processmissionresults-0040eae7)). It walks the nine records and, for a chassis still locked, tests one campaign-flag slot against one expected value; on a match the flag is set, and either way the slot is cleared. The weapon unlock that runs straight after it has the same shape, with its slots and values in static tables rather than code ([`weapons-dat.md`](weapons-dat.md#campaign-grants--armory_grantcampaignweapons-004126be)).

| Chassis | Flag slot | Expected value |
|---|---|---|
| Raptor II | `0x3c` | 2 |
| Maverick | `0x3d` | 2 |
| Ogre | `0x3e` | 2 |
| Razor | `0x3f` | 1 |

The expected value is a loop-carried local that starts at 2 and is set to 1 in the Razor's own branch. Because the Razor is the last record in the table, nothing after it reads the changed value; a table reordered to put type 8 earlier would silently drop every later chassis's threshold to 1.

## `gam\damage.dat` — the component and weapon value table

One 102-byte file, read whole at load and immediately turned into the tables the repair bay and the scrap screen price against.

```
int16       chassis scale -- 800
6 x int16   external component group percentages -- 100 50 50 50 100 100
9 x int16   internal component percentages -- 100 100 50 50 25 100 25 25 25
int16       weapon scale -- 1000
int16       count -- 33
count x int16   per-weapon scrap value
```

The loader, `LoadDamageDat_Shell` (`00413628`, `hercdisp.cpp`), expands it against `herc_inf.dat`'s prices through `FixedMulQ10` (`00450a84`), a Q10 fixed-point multiply — `(a * b) >> 10`:

```
for each chassis type t, price = herc_inf[t].+0x08 * 1000:
  HercExternalGroupValues[t][g] = (800 * ((external[g] * price) >> 10)) >> 10    for g in 0..5
  HercInternalValues[t][i]      = (800 * ((internal[i] * price) >> 10)) >> 10    for i in 0..8
for each weapon id w in 0..32:
  WeaponScrapValues[w] = (1000 * weaponValue[w]) >> 10
```

`HercExternalGroupValues` (`004840cc`) is a 9 × 33 `int16` table and `HercInternalValues` (`004840e6`) is its column 13; the two together end exactly where the 33-entry weapon table `WeaponScrapValues` (`0048431e`) begins. The six external and nine internal figures are the units the whole cost model works in — see [`../shell/armory.md`](../shell/armory.md#repairing-and-scrapping).

The per-weapon column is **the weapon's `weapons.dat` price times 100**, holding for all thirty non-Bull ids, with the three Bull weapons deliberately zeroed where `weapons.dat` prices them at 50. Since the catalog price times 1000 is the cost in kg, a weapon scraps for a tenth of what it cost.

## The screen-layout families

`gam\arm_weap.dat`, and the nine-file `gam\arm_*.dat` and `gam\rpr_*.dat` families, are widget geometry: each record positions one panel against a frame of the matching `DBA\` sprite sheet. They carry no simulation quantities. What makes `arm_weap.dat` worth reading anyway is its record *list*, which is the authoritative set of weapons the armory offers.

### `gam\arm_weap.dat`

Read by `Arming_BuildScreen` (`0043e52c`, `warmingi.cpp` — the weapon-fitting screen) alongside `dba\arm_weap.dba`:

```
int16   count -- 26
count x { int16 weaponId; int32 x; int32 y; int16 dbaFrame }
int16   count -- 4
count x { int16 ammoType; int32 x; int32 y; int16 dbaFrame }
```

The loader derives each panel's rect as `{ x, y, x + frameWidth, y + frameHeight }` from the named DBA frame, which is what identifies the two `int32` as a top-left corner.

**Twenty-six weapons have a panel.** The seven ids with none are `NONE` (0), the three Bull weapons (19–21), and `LAEW` (26), `MINE` (27) and `MFAC` (28). This settles player availability outright, where the `.MSN` roster scan cannot: see [`weapons-dat.md`](weapons-dat.md#the-rank-byte-and-what-retail-actually-fits) and [`../cut-content.md`](../cut-content.md).

The four trailing panels are the guidance kinds by their own ids: `SARH` 0, `ARH` 1, `ARM` 2 and `EO` 3, the order `wpn_desc.bin` describes them in and the values a mount carries ([`../shell/weapons-and-repair.md`](../shell/weapons-and-repair.md#guidance-kinds)).

`WPN_INFO.BIN` is indexed by this file's panel order rather than by weapon id, five strings per panel — see [`weapons-dat.md`](weapons-dat.md#the-bin-string-tables).

### `gam\rpr_*.dat` — repair-bay layout

Nine files, loaded together by `LoadRepairLayouts` (`00413d1c`, `hercdisp.cpp`) into a per-chassis structure of 33 counts followed by 33 record pointers, `0xc6` bytes per chassis.

```
int16   count
count x layout record            -- the chassis's own components
1 x     layout record            -- one further record per chassis, at 00484534
int16   groupCount
groupCount x { int16 weaponId; int16 count; count x layout record }

layout record, 14 bytes on disk into a 26-byte struct:
  int16   +0x00   part id within its picture
  int32   +0x02   x
  int32   +0x06   y
  int16   +0x12   frame index into the matching dba\ sheet
  int16   +0x16   blit flags: 0, or 2 to mirror left to right
```

`Repair_BuildDiagrams` (`004140a9`) is what names those fields: it walks the counted list placing one grid part per record at `(+0x02, +0x06)` from frame `+0x12` of `dba\rpr_<chassis>.dba` with `+0x16` as its blit flags, and looks a fitted weapon's record up in the per-weapon groups by id and by `slot + 6`. **The trailing single record is the internals diagram** — one part from `dba\<chassis>_int.dba`, over the same rect, shown while the internals list is the one being worked in ([`../shell/weapons-and-repair.md`](../shell/weapons-and-repair.md#the-damage-diagram)).

Retail's component counts are 4, 6 or 12 — the Razor's twelve against the walkers' four or six, which is the flyer's own component set.

### `gam\arm_*.dat` — armory layout

The same shape with two leading records instead of a counted list, and two 8-byte blocks per record instead of one:

```
2 x     layout record
int16   groupCount
groupCount x { int16 weaponId; int16 count; count x layout record }

layout record, 22 bytes on disk into a 26-byte struct:
  int16   +0x00   part id within its picture
  8 B     +0x02   x, y
  8 B     +0x0a   a second x, y
  int16   +0x12   frame index into the matching dba\ sheet
  int16   +0x16   blit flags: 0, or 2 to mirror left to right
```

The group records' four `int32` are each incremented by one as they are read — a one-pixel inset applied at load; the two leading records' are not.

`Squad_BuildBayPictures` (`00414e5b`) places the two leading records as the top and bottom halves of the squad panel's bay picture and a fitted weapon's group record as its part, by id `slot + 2` ([`../shell/squad-and-crew.md`](../shell/squad-and-crew.md#the-bay-picture)). The second x, y is where `Arming_MarkHardpoint` (`004155db`), on the arming screen, draws a mount's frame from the chassis's `_out` bank. Every retail file puts the two halves in parts 0 and 1, at `(1, 1)` and `(1, 140)`.

### `gam\arm_hots.dat` and `gam\rpr_hots.dat` — the clickable regions

The two files are one format, read by the same code: `Hotspots_BuildOverlay` (`0043c1a0`, `wsquadi.cpp`) opens `arm_hots.dat` when it is building for the arming tab and `rpr_hots.dat` for the repair tab, and does nothing else differently.

```
int16   groupCount            -- 9; the reader asserts it
groupCount x {
  int16   hercId              -- 0-8, sequential in both retail files
  int16   areaCount
  areaCount x { int32 x0; int32 y0; int32 x1; int32 y1 }
}
```

Both walk exactly to EOF — 998 and 902 bytes of content.

Each area becomes one `Panel` parented to that chassis's picture, and **the area's index within its group is what selects the panel's click handler**: the builder holds a table of handler pointers at `0048d53c` and passes `table[areaIndex]`. So these files carry position only; which hardpoint or which body location an area *is* comes from its ordering.

`arm_hots.dat` carries the weapon hardpoints — counts 3, 5, 5, 8, 9, 9, 10, 4, 7 across the nine chassis, 60 in all, matching each chassis's mount capacity, and the widest at 10 is exactly the length of the arming screen's handler table. `rpr_hots.dat` carries six areas for every chassis, the HERC's damage locations; the repair screen's handler table is 16 long because it covers those six *and* up to ten weapon mounts, which the same loop places from `rpr_*.dat` geometry starting at index 6. Two of the nine repair groups, the Tomahawk's and the Maverick's — the two chassis whose `rpr_*.dat` lists four components — hold all-zero rects in areas 1 and 2. The repair tab places all six areas of a group whatever they hold.

The four `int32` are an inclusive rect and not a position and a size: the 16 bytes go straight to `ESRect_Ctor` as its rect argument, which is `{x0, y0, x1, y1}` everywhere else in the executable ([`../shell/screen-layout.md`](../shell/screen-layout.md#the-canvas-is-640x480-and-rects-are-inclusive)), and the first chassis's first two arming areas — `(37, 94, 70, 125)` and `(157, 94, 191, 125)` — are a left and right hardpoint mirrored about x≈114, which only holds read as two corners.

## Rejected readings

| Reading | Why it is wrong |
|---|---|
| An `*_hots.dat` area is a position and a size — `{x, y, w, h}` | Four `int32` beginning with what reads as a top-left corner invites it, and the retail values stay inside the canvas read either way, so a parse alone will not settle it. They are two inclusive corners: the bytes are handed straight to `ESRect_Ctor`, and the left/right hardpoint pairs are only mirror images when read that way |
| `herc_inf.dat` `+0x06` gives a chassis's hardpoint count | It is what the Herc Construction screen *prints*, and for the Raptor II it prints 4 where the machine the player receives has 5. `Herc_CapacityForType` reads the in-code table at `0046f73a`, and that is the figure the record's `+0x4c` holds and `Hotspots_BuildOverlay` bounds both tabs' mount loops on. The other eight chassis agree, so a reader checking one file will not notice |
| The 26 bytes at HERC status block `+0x00` are opaque | They are 13 `int16` component conditions, initialized to 100 alongside the other two arrays by `HercStatus_InitAll` (`00411b88`) and averaged with them by `HercStatus_OverallCondition` (`00411bd4`), whose divisor is `13 + 9 + hardpoints`. See [`save-games.md`](save-games.md#the-66-byte-status-block) |
| `hercs.dat`'s fifth entry is a wrecked Razor | Its `+0x4a` of 0 is build progress, not condition. The status block a `gam\*.dat` chassis carries is never read from the file and stays at the constructor's uniform 100 |
| The `+0x4a`/`+0x78` pair is repair state | `Herc_Order` sets them when a chassis is *bought*, from `herc_inf.dat` `+0x0c`, and `Herc_BuildTick` drives them one mission at a time until delivery. Repair works on the status block instead |
| `+0x4a` is a health or condition ratio | It is the build percentage. The data will not separate the two readings: `+0x4a` is 100 in all nine `ini_*.dat` and in `trn_herc.dat`, which is equally consistent with an undamaged machine. Only `hercs.dat`'s part-built Razor reads anything else, and it reads 0 while that machine is at full condition — so a condition reading has it dead on arrival |
| `+0x78` picks a frame of `[chassis]_bod.dba` | It is the mission countdown to delivery. Both readings fit the shipped files, where the field is 0 everywhere except that same Razor's 3; what settles it is `Herc_BuildTick`, which decrements it and recomputes `+0x4a` from it against `herc_inf.dat`'s build time. A damage-model frame index would not be arithmetic on a purchase countdown |

## Open

- **Deferred:** whether any reader indexes the `HercExternalGroupValues` (`004840cc`) columns `LoadDamageDat_Shell` leaves alone (6-12 and 22-32). `RepairCost_StaticInit` (`00413cb2`) builds the nine rows with `HercStatus_InitAll`, so those columns hold 100.
- **Deferred:** whether anything loads `trn_herc.dat`. No reader is named in either executable's string data, but a runtime-assembled path cannot be ruled out from that alone.
- **Deferred:** what `herc_inf.dat`'s `+0x0a` field holds. No reader found by `es2_xref.py --binary VSHELL 00483b5e` or by a scan of VSHELL's code for any dword addressing the table (`00483b40`–`00483bf0`): every other field is addressed directly, and the base `00483b54` only by `LoadHercInfDat`'s bulk read and `Herc_GrantUnlocks`'s `+0x00` test. DBSIM does not name `herc_inf.dat`.
