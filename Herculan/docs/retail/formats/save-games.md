# Shell save files — `sav\GAMEFILE.STR` and `sav\GAME_?.SAV`

The campaign save. Written and read only by VSHELL; DBSIM never opens either file. **Every address in this doc is in `VSHELL.EXE`.**

Reversed from decompilation and validated by walking all nine retail `ES2/SAV/GAME_*.SAV` end to end: seven consume byte-exact, and the two that do not are explained by the non-truncating writer below.

## The twelve slots — `sav\GAMEFILE.STR`

A slot directory, not a save: twelve filenames and twelve display labels. Read by `GameFileStr_Read` (`0040ddc8`), written by `GameFileStr_Write` (`0040df4b`).

```
0x00   int32   physical file length - 4 (see "Streams never truncate")
0x04   int16   count -- always written as 12
       12x { int16 len; char[len] filename, NUL included; byte 0 }
       int16   count -- always written as 12
       12x { int16 len; char[len] label, NUL included; byte inUse }
```

The reader ignores the leading length and stops after the second count block. Both counts are literal `0xc` in the writer, so the slot count is fixed in code, not data.

| Slot | Filename | Label | Role |
|---|---|---|---|
| 0-9 | `GAME_0.SAV`-`GAME_9.SAV` | `" 1. KEVIN"`, `" 8. EMPTY"` | player-named saves; the label carries its own `"N. "` prefix |
| 10 | `GAME_R.SAV` | `RESUME` | campaign autosave |
| 11 | `GAME_T.SAV` | `TRAINING` | training autosave |

Slot 10 and 11 are one slot from the caller's side. `Game_SaveSlot` (`0040e37b`) and `Game_LoadSlot` (`0040e4f2`) both open with `if (slot == 10 && DAT_0048260c == 0) slot = 11`, so `DAT_0048260c` is the campaign/training mode flag — 1 selects `GAME_R`, 0 selects `GAME_T`. Retail `GAME_T.SAV` holds pilot `TRAINEE` and career stage 0; `GAME_R.SAV` is byte-identical in length, salvage and career position to `GAME_6.SAV`, the newest ordinary save.

**An empty slot's label is completed at load time.** After reading a label the reader tests `label[4]`, and when it is NUL appends string `0x21` from `estext.bin`. A stored `" 8. "` becomes `" 8. EMPTY"` in a localized build. Once such a slot is written back the completed label is in the file, which is why every retail label already reads `EMPTY`.

The in-memory slot table is a 94-byte (`0x5e`) stride at `00482610`: filename at `+0x00` (13 bytes), label at `+0x0d` (80 bytes), in-use byte at `+0x5d`. `SaveSlot_BuildPath` (`0040e150`) builds the path to open by prefixing the filename with the string at `0046f434`, `sav\`.

## Writing a slot

`Game_SaveSlot(slot, label)` (`0040e37b`) writes nothing while `DAT_0048260a` is clear. That is the game-in-progress flag: the shell's startup clears it (`Shell_InitGameState`, `0040e17e`), and `Game_LoadSlot` and `Game_NewCareer` set it. Otherwise, once slot 10 has become 11 in training:

1. `Game_SetSlotInUse` (`0040e115`) copies `label` over the slot's label when the slot is below 10, sets the slot's in-use byte whatever the slot, and writes `GAMEFILE.STR` out whole (`GameFileStr_Write`, `0040df4b`). Slots 10 and 11 keep `RESUME` and `TRAINING`.
2. `sav\` and the slot's filename are opened and the [blocks](#savgame_sav--block-order) written in order, `Career_SaveSlot` copying [the slot handoff](#the-slot-handoff) out as it writes block 3.

The save screen's `ACCEPT` is the one caller that passes a label: the row's own string, its `"%2d. "` prefix included ([`../shell/main-menu.md`](../shell/main-menu.md#saving-is-a-rename)). Every autosave passes slot 10 and `NULL`.

## The slot summary — `stats.cpp`'s scan

The save screen shows a slot's pilot and career position without loading it. Eleven [staging records](#pilot-record--59-bytes-0x3b-in-memory) at `0048cddc` hold that summary, one per slot; `Stats_ScanSlots` (`00430378`, `stats.cpp`) fills them from the files and `Stats_StageCurrentGame(slot)` (`0043084c`) writes one from the live game.

**The scan walks every block of a save with explicit skip counts**, which makes it a second, independent statement of the whole [block order](#savgame_sav--block-order). For each slot the directory marks in use it skips 33 records of `{ byte; int16 count; count x 10 }` for the armory stock; skips 2 + 10 + 10 for the build queue; reads the stage and the mission off the head of the career block and skips its remaining 148 bytes; skips 36 pilot records of `4 + (int16 len + name) + 2 + 1 + 22`; skips 12 for the eight shorts between the squad and the player; skips the player block's two leading `int16` and reads the pilot record itself; walks the hangar's counted list at `0x48` fixed bytes plus 12 per occupied mount; skips 18 for the chassis flags; reads the salvage pool; and stops without touching blocks 9 to 11. Every count agrees with the table below.

It reaches those fields by seeking rather than reading — `FileRStream_Tell` (`0044e864`) is tell and `FileRStream_Seek` (`0044e880`) is seek — so what reads as a stream loop in a decompile is a run of binary skips.

## Streams never truncate

`FileRWStream_Open` (`0044e46c`), the read/write-stream open behind every file in this doc, calls `_open(path, 0x8102, 0x180)` — `O_BINARY | O_CREAT | O_RDWR`, with **no `O_TRUNC`**. Writing a shorter payload over a longer file leaves the old tail in place.

This is observable in retail data. `GAME_4.SAV` carries 164 bytes past its last field and `GAME_T.SAV` 36; `GAMEFILE.STR` is 345 bytes of which the reader consumes 338, so 7 are the remains of a longer label block. Consequences for any reader:

- **Parse by structure, never by file size.** Trailing bytes are not a parse failure.
- `GAMEFILE.STR`'s leading length is `lseek(fd, 0, SEEK_END) - 4` taken *after* writing (`FileRWStream_GetSize`, `0044e518`), so it measures the physical file including stale tail, not the payload.
- Where the game needs a clean file it deletes first: `Game_ExportMissionHandoff` (`0040f0d4`) calls `_remove` on its output path before opening it for write.

The exports and copies use a different stream class whose open is `_open(path, 0x8301, 0x180)` — `O_BINARY | O_CREAT | O_TRUNC | O_WRONLY` — so they do truncate. Stale tails are a property of the save files here and of `data\script.dat` and `data\mission.str`, whose writers (`WriteScriptDatFile`, `MissionStr_Write`) also open through `FileRWStream_Open`.

## `sav\GAME_?.SAV` — block order

No header, no magic, no length field: the file is the concatenation below. Written by `Game_SaveSlot` (`0040e37b`) and read back field-for-field by `Game_LoadSlot` (`0040e4f2`).

| # | Bytes | Content | Writer / reader |
|---|---|---|---|
| 1 | varies | armory stock — 33 records, one per `weapons.dat` catalog id | `Armory_Write` (`004121cf`) / `Armory_Read` (`0041215d`) |
| 2 | 22 | the armory build queue: free-slot count (`0046f8d4`), then 5x `{ int16 slot; int16 weapon id }` from `0046f8d6` | same |
| 3 | 152 | career block (below) | `Career_SaveSlot` (`00412a71`) / `Career_LoadSlot` (`00412bbf`) |
| 4 | varies | 3 squads x 12 pilot records, then 3x `int16` at `00483b48`, each squad's current member, and 3x `int16` at `00483b4e`, the member `Squad_TakeMember` (`0040fb4f`) takes next, modulo 12 | `Squads_Write` (`0040fc16`) / `Squads_Read` (`0040fc77`) |
| 5 | varies | the player: `int16` squad positions in play (`00482a78`), `int16` machines on strength (`00482a7a`), then one pilot record — **the same shape as a squadmate's**, roster id included | `Player_Write` (`0041016d`) / `Player_Read` (`004101b8`) |
| 6 | varies | hangar: `int16` count, then that many `{ int16 slot; HERC record }` | `Hangar_Write` (`00410658`) / `Hangar_Read` (`0041080a`) |
| 7 | 18 | the 9 chassis availability flags — `herc_inf.dat` record `+0x0e`, stride 16 from `00483b62` | `ChassisAvailability_Write` (`00411954`) / `ChassisAvailability_Read` (`00411989`) |
| 8 | 4 | the salvage pool (`00482af4`) | inline |
| 9 | 2000 | the campaign flag array (`00482af8`) | inline |
| 10 | 2 | game state (`0048260e`) | inline |
| 11 | 20 | `004832c8` | inline |

**`00482a78` counts squad positions, the player's as position 0.** `Squad_UpdateOnStrength` (`00410366`) sets a squad member's on-strength byte only when its position (`+0x27`) is below it and it has a bay, and the auto-repair pass (`0040e862`) and the `player.mec` export (`0040f1e4`) bound their loops over positions by it. `00482a7a` is the number of machines on strength, the export's entry count ([`../shell/campaign-loop.md`](../shell/campaign-loop.md)).

Blocks 8, 9 and 11 are one contiguous span in memory: the salvage pool at `00482af4`, the flag array immediately after at `00482af8`, and its 2000 bytes ending exactly at `004832c8`. Block 10 comes from `0048260e`, elsewhere entirely. Block 11 is the 20 bytes past the end of what `data\mission.var` carries; only the save writer and reader are traced touching them.

Block 7 walks `herc_inf.dat`'s in-memory table at a 16-byte stride, so the nine `int16` are the availability flag of each chassis — campaign state rather than catalog data, and the counterpart of block 1's per-weapon unlock byte. See [`herc-catalogs.md`](herc-catalogs.md#chassis-unlocks--herc_grantunlocks-004118c5).

The hangar holds up to 8 HERCs. Both the hangar and each HERC's mounts serialize **sparsely** — occupied entries only, each preceded by its slot index — so a reader must use the count field and cannot assume dense packing.

### Armory stock record

One per catalog id, all 33 written unconditionally:

```
byte    weapons.dat record +0x16
int16   number of owned units (weapons.dat record +0x17)
        that many 10-byte weapon unit records
```

The byte is the weapon's unlock flag and the owned units are a linked list at the catalog record's `+0x19` at runtime. `Armory_ReadWeaponStock` (`00411dbb`) reads one weapon's entry and pushes each unit onto the list's head as it reads it, so the file's last unit is the head — the one the weapons screen fits next. `FUN_00411e64`, `Armory_Write`'s per-weapon writer, walks the list from the head, so the head is the first unit written: **every save and load reverses a weapon's units**, and the unit fitted next alternates between the two ends of the stock from one load to the next. See [`weapons-dat.md`](weapons-dat.md) for the catalog record those fields belong to and [`herc-catalogs.md`](herc-catalogs.md#the-weapon-unit-record) for the unit record, which is the same five `int16` a HERC's mounts serialize.

### Pilot record — 59 bytes (`0x3b`) in memory

Serialized by `Pilot_Write` (`0040fd5f`), read by `Pilot_Read` (`0040fefc`), initialized by `Pilot_Init` (`0040fcd8`) and `Pilot_SetDefaults` (`0040fd17`). On disk it is 31 bytes plus the name: three `int16` lead — roster id, name index, then the name's length — and eleven follow the on-strength byte.

**One record shape serves both the squad block and the player block.** Block 5's two leading `int16` belong to the block, not to the record; a reader that treats the player's record as a shorter form of a squadmate's lands its name two bytes early and desynchronizes everything after it.

| Offset | On disk | Field |
|---|---|---|
| `+0x00` | `int16` | roster id, `0-11` — squad index x 4 plus a per-squad shuffle |
| `+0x02` | `int16` | name index into `esnames.bin`, `0-35` |
| `+0x04` | `int16 len` + `char[len]` | name, copied from `esnames.bin`; 30 bytes in memory |
| `+0x22` | `int16` | assigned hangar slot; `-1` when unassigned |
| `+0x24` | `byte` | on strength — gates repair billing, results accounting and the `player.mec` export |
| `+0x25` | `int16` | skill `0-3` — `ROOKIE`, `REGULAR`, `VETERAN`, `ELITE` (`estext.bin` `0x35`-`0x38`), drawn against the weight table at `0046f5ec` |
| `+0x27` | `int16` | squad position, 1-3 — the crew screen row the pilot fills ([`../shell/squad-and-crew.md`](../shell/squad-and-crew.md#the-rows)); initialized `-1` by `Pilot_SetDefaults` (`0040fd17`). The player's is 0: `Player_Create` (`00410107`, from `Game_NewCareer`) writes it straight after `Pilot_Init`. A squad member's comes only from `Squad_SetMemberPosition`, whose four callers pass `-1`, `k + 1` or a crew row from 1 to 3, so no member holds 0. Selects the promotion divisors in `Pilot_Progress` (`00410066`) |
| `+0x29` | `int16` | rank `0-3` — `Lieutenant`, `Captain`, `Major`, `Lt Colonel` (`estext.bin` `0x39`-`0x3c`), seeded from the skill through `0046f5f4`, `{0, 1, 2, 2}`, by `Squad_GenerateRoster`; `Player_Create` writes 0 |
| `+0x2b` | `int16` | condition, initialized 100 and overwritten at debrief from the HERC's damage |
| `+0x2d` | `int16` | Herc kills, this mission |
| `+0x2f` | `int16` | Flyer kills, this mission |
| `+0x31` | `int16` | Base kills, this mission |
| `+0x33` | `int16` | Herc kills, career total |
| `+0x35` | `int16` | Flyer kills, career total |
| `+0x37` | `int16` | Base kills, career total |
| `+0x39` | `int16` | missions flown |

The three pairs are proved by `Pilot_AccumulateMissionStats` (`0041000e`), which reads the per-mission counters from `results.dat` in the order Herc, Base, Flyer and then accumulates `+0x33 += +0x2d`, `+0x35 += +0x2f`, `+0x37 += +0x31`, `+0x39 += 1`.

What names them is the save screen, whose detail panel prints six of these fields as a `Current`/`Total` pair per row against the labels ` Herc Kills:`, `Flyer Kills:` and ` Base Kills:` (`estext.bin` `0x27`-`0x29`) — see [`../shell/main-menu.md`](../shell/main-menu.md#the-save-screen). It reads them out of a **67-byte (`0x43`) staging record**: this record, then `int32` salvage in kilograms, `int16` campaign stage and `int16` mission within the stage. `Stats_StageCurrentGame` (`0043084c`) fills the player's copy field by field from `00482aa9` upward, which is the pilot record embedded at `00482a7c`, so each screen offset binds to one pilot offset directly — and that binding is what settles this table's `+0x25`, `+0x29` and six kill offsets rather than leaving them inferred from order. `Pilot_Progress` reads the same naming back: it promotes on `+0x33 + +0x35`, the two kill kinds that are machines, and never on `+0x37`.

Skill and rank advance separately, in `Pilot_Progress` — see [`../shell/campaign-loop.md`](../shell/campaign-loop.md#pilot-progression).

### HERC record — 122 bytes (`0x7a`) in memory

Serialized by `Herc_Write` (`0041123e`), read by `Herc_Read` (`004110c2`).

```
int16   +0x00  chassis type, 0-8
int16   +0x02  chassis type again, via the identity map at 0046f728
66 B    +0x08 -- the status block, below
int16   +0x4a  build progress, percent complete
int16   +0x78  build time remaining, in missions
int16   +0x4c  mount capacity
int16   +0x4e  mounts occupied
        that many { int16 slot; 10-byte weapon unit record }
```

`+0x00` and `+0x02` are two id spaces that coincide: `+0x02` is `+0x00` passed through `Herc_TypeToIndex` (`00410d30`), a search of the nine-entry table at `0046f728` whose contents are `0`–`8` in order. `+0x02` is the one the chassis stat table and the `estext.bin` name are indexed by, `+0x00` the one the mount-capacity table is. The `+0x4a`/`+0x78` pair is construction state, not damage — see [`herc-catalogs.md`](herc-catalogs.md#gamherc_infdat--the-chassis-stat-table).

**`+0x4c` and `+0x4e` are not interchangeable.** The writer loops to the capacity at `+0x4c` and emits only non-null mounts; the reader loops exactly `+0x4e` times. Reading the writer alone yields a record that desynchronizes whenever a HERC has an empty hardpoint.

The same record has a shorter form in `gam\hercs.dat` and `gam\ini_*.dat`, where the file carries four fields and the rest are derived at load ([`herc-catalogs.md`](herc-catalogs.md#the-herc-catalog-record)).

#### The 66-byte status block

Three condition arrays, all initialized to 100 by `HercStatus_InitAll` (`00411b88`) and all holding 0–100 in retail data. `HercStatus_Get(block, mode, index)` (`00411d06`) is the accessor and `FUN_00411cbd` the matching setter, and the three modes are the three arrays — mode 1 addresses `block + 0x1a + index*2`, mode 2 `block + 0x2e + index*2`, and mode 0 averages a group of the first array. The split is exactly `player.mec`'s three condition spans, which were derived independently from the offsets DBSIM copies them to.

| Span | Size | Content |
|---|---|---|
| `+0x00`–`+0x19` | 26 | 13x `int16` external component condition |
| `+0x1a`–`+0x2d` | 20 | 10x `int16` internal component condition. Indices 0–8 are the nine internals; **index 9 is the machine's overall condition** — read as `(block, 1, 9)` at debrief to set the pilot's, and reset to 100 by `FUN_00411cbd(block, 1, 9, 100)` when a HERC is kept rather than scrapped |
| `+0x2e`–`+0x41` | 20 | 10x `int16` per-hardpoint condition, one per mount slot. A mount whose entry reaches 0 is destroyed and `+0x4e` decremented (`Herc_ReadStatusBlock`, `00411720`) |

`HercStatus_OverallCondition(block, hardpoints)` (`00411bd4`) is what proves the 13/9 split and index 9's role: it sums the 13 external entries, the first 9 internal entries and one per occupied hardpoint, then divides by `hardpoints + 0x16` — exactly `13 + 9 + hardpoints`. Index 9 is excluded from its own average, and `Herc_SettleAfterMission` (`00410c7c`) writes the result back there at debrief.

**The 13 external entries are facets, not named components.** Mode 0 addresses them through the group table at `0046f8a4` — six groups of up to three indices each, `-1` terminated, partitioning all 13 exactly once — and it is the *group* that carries a name and the granularity the repair bay and the scrap valuation price at ([`../shell/armory.md`](../shell/armory.md#repairing-and-scrapping)). `HercStatus_GroupMean` (`00411c29`) returns a group's mean.

| Group | Facets | Name (`estext.bin`) | Facet meaning |
|---|---|---|---|
| 0 | `{0, 1}` | `0x4e` `Cockpit` | front, rear |
| 1 | `{2, 4}` | `0x4f` `Left Torso` | front, rear |
| 2 | `{3, 5}` | `0x50` `Right Torso` | front, rear |
| 3 | `{6}` | `0x51` `Chassis` | — |
| 4 | `{7, 9, 11}` | `0x52` `Left Leg` | thigh, calf, foot |
| 5 | `{8, 10, 12}` | `0x53` `Right Leg` | thigh, calf, foot |

The cockpit and leg facets follow the `.DMG` component numbering ([`dmg-damage-file.md`](dmg-damage-file.md#the-two-index-spaces)): 0 and 1 front and rear cockpit, and 7-12 each leg's upper leg, lower leg and foot in the same order. The torso facets' front/rear reading is [Open](#open).

The nine internal entries are named one-for-one by `estext.bin` `0x54`–`0x5c` — `Left Leg Servos`, `Right Leg Servos`, `Sensor Array`, `Targeting Computer`, `Shield Generator`, `Engine`, `Hydraulics`, `Stabilizers`, `Life Support` — matching the `.DMG` internal slots 0-8 in order ([`dmg-damage-file.md`](dmg-damage-file.md#the-two-index-spaces)).

`estext.bin` `0x5d`–`0x63` holds seven more names — `Left Nacelle`, `Right Nacelle`, `Fuselage`, `Left Wing`, `Right Wing`, `Left Wing Servos`, `Right Wing Servos` — which are the Razor's parts in place of a walker's torsos, chassis, legs and leg servos. **The substitution is per chassis type, not per component.** `Repair_SetComponentNames` (`00433cdf`) holds two fifteen-entry tables of string indices and picks the second whenever the machine's type is 8, replacing all fifteen names at once ([`../shell/weapons-and-repair.md`](../shell/weapons-and-repair.md#which-names-a-chassis-shows)). The condition arrays themselves are unchanged — a Razor's thirteen external facets group the same six ways as a walker's.

Retail data agrees throughout: the per-hardpoint array carries most of the partial figures, and a machine at full health reads 100 everywhere.

Because the span is copied verbatim into `player.mec`, a machine's status bytes are identical in the save and in the export ([`../shell/campaign-loop.md`](../shell/campaign-loop.md)).

### Career block — 152 bytes

| Bytes | Global | Field |
|---|---|---|
| 2 | `0046fb18` | campaign stage |
| 2 | `0046fb1a` | mission within the stage |
| 2 | `00483fe8` | populated count of the array below |
| 20 | `00483fea` | 10x `int16`, line indices into `data\mission.str`; `-1` is an empty slot |
| 2 | `00483ffe` | populated count |
| 60 | `00484000` | 30x `int16` line indices |
| 2 | `0048403c` | populated count |
| 60 | `0048403e` | 30x `int16` line indices |
| 2 | `004840b8` | the briefing movie id |

`Career_SetBriefing` (`00412ece`) fills all seven fields after the position from the loaded mission's row 4 ([`../shell/campaign-loop.md`](../shell/campaign-loop.md#loading-the-careers-mission)). The three index arrays are the mission tab's objectives (`00483fea`), briefing (`00484000`) and intelligence report (`0048403e`), assembled into single strings by `Career_BuildBriefingText` (`00412f97`) by concatenating the `mission.str` lines each array names ([`../shell/mission-screen.md`](../shell/mission-screen.md#the-summary-text-box)). The save in `savGAME_6.SAV` names lines 3-4, 5 and 6 of its `missn6.str`: the two objective lines, the briefing paragraph and the intelligence paragraph. A fourth 30-entry array at `0048407c` is the debrief's and is absent from the save: `Career_Advance` (`00412dc7`) fills it through `Career_SetDebriefLines` (`00413386`) from the mission just flown, before it moves the position on, and `Career_BuildDebriefText` (`004133d2`) assembles it.

## The slot handoff

Both career-block functions also copy the three loose working files that the shell and the simulator exchange, and the copy direction is what separates them:

| Function | Direction | Files |
|---|---|---|
| `Career_SaveSlot` (`00412a71`) | save — `data\` to `sav\` | `data\script.dat` to `sav\script%d.dat`, `data\mission.str` to `sav\missn%d.str`, `data\player.mec` to `sav\player%d.mec` |
| `Career_LoadSlot` (`00412bbf`) | load — `sav\` to `data\` | the same three, reversed |

`FileUtil_CopyFile` (`0040d4d5`, `fileutil.cpp`) is the copy itself. On load, `Career_LoadSlot` additionally calls `Career_BuildBriefingText` (`00412f97`) to rebuild the assembled briefing text from the restored `data\mission.str`, so the three text arrays in the career block are indices that only mean anything alongside the slot's own `missn%d.str`.

For what those files carry see [`script-dat.md`](script-dat.md) and [`msn-mission-file.md`](msn-mission-file.md); for how a mission's results re-enter the save see [`../shell/campaign-loop.md`](../shell/campaign-loop.md).

## Open

- **Open:** whether the torso groups' facets (2 and 4, 3 and 5) are front and rear, or the shoulder and weapon bracket that `.DMG` numbers 2-5.
