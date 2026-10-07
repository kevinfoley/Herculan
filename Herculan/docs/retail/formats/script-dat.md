# `data\script.dat` — the real DBSIM gameplay handoff format

**Format summary:** A GUID-filtered, field-subset re-export of the same in-memory `.msn` row arrays populated by `MsnGen_ParseMsnFile` (`00417b67`, the `.msn` parser). Written by `WriteScriptDatFile` (`0041ac54`, VSHELL) immediately after `.msn` parsing completes. DBSIM reads `script.dat` (not `.msn`) for actual gameplay simulation. Every one of `script.dat`'s 13 count-prefixed record blocks maps 1:1 to one of [`msn-mission-file.md`](msn-mission-file.md)'s 17 already-decoded rows.

## Call chain — confirmed

- `WriteScriptDatFile` (`0041ac54`, VSHELL, `msn_gen.cpp`) — called from `MsnGen_LoadMission` (`0041c73d`) right after `MsnGen_ParseMsnFile` (`00417b67`, `.msn` parser) finishes, once per mission load. Writes `data\script.dat`: a fixed 20-byte header + 13 count-prefixed record blocks, from the same `DAT_0047xxxx` globals the `.msn` parser fills.
- `DBSim_LoadScriptDat` (`00424308`, DBSIM) — sim-side reader, **pass 1**. Opens `data\mission.var`, `data\player.mec`, `data\mission.str`, `data\script.dat` (in that order) during world init; reads the same 13-block structure, same order, matching strides. `mission.var`, `player.mec` and `script.dat` each open under a severity-5 `Assert_Report` (`00479b6c`, `WORLD.CPP` lines `0xa5`, `0xb2` and `0xdc`): a file that will not open raises a `MessageBoxA` titled `Error`, Sierra's support notice followed by the assert's source line (`Assert_Emit`, `00479e18`), and once it is closed the simulator exits with code 1.
- `DBSim_SpawnMissionObjects` (`004253d8`, DBSIM) — **pass 2**, builds the world. Re-opens `data\script.dat` from the top, skips blocks 1-6, walks blocks 7-13 again: constructs one object per slot pass 1 marked live, reads its position, heading, loadout and action links from its own record. See "The two-pass read" below.
- `ShellMap_LoadScriptDat` (`004243d7`, VSHELL, `shellmap.cpp`), called from `ShellMap_Constructor` (`00423f43`) — the reader behind the briefing's map ([`../shell/mission-map.md`](../shell/mission-map.md)), independent of DBSIM's. Same 13-block structure and order; keeps a different subset per block (see per-block table). Confirms record shape/strides via a second, independently compiled reader.

## Row mapping

`WriteScriptDatFile` walks the *same* row-array globals `.msn`'s parser populates (cross-referenced by global address against `msn-mission-file.md`'s table):

| `.msn` row | count global | storage global | on-disk stride (`.msn`) |
|---|---|---|---|
| #4 text | `DAT_00470668` | `DAT_00470640` | 144B |
| #6 points | `DAT_0047064e` | `DAT_0047060c` | 22B |
| #7 headings | `DAT_00470650` | `DAT_00470610` | 10B |
| #8 waypoint groups | `DAT_00470656` | `DAT_0047061c` | variable |
| #9 trigger areas | `DAT_0047065e` | `DAT_0047062c` | 12B |
| #10 actions | `DAT_00470660` | `DAT_00470630` | 82B |
| #11 timers | `DAT_00470662` | `DAT_00470634` | 30B |
| #12 mech roster | `DAT_00470652` | `DAT_00470614` | 144B |
| #13 flyer roster | `DAT_00470654` | `DAT_00470618` | 102B |
| #14 base roster | `DAT_0047065c` | `DAT_00470628` | 62B |
| #15 orders | `DAT_00470658` | `DAT_00470620` | 22B |
| #16 groups | `DAT_0047065a` | `DAT_00470624` | 164B |
| #17 objectives | `DAT_0047064a` | `DAT_00470608` | 58B |

**Filtering:** All rows except #4 and #17 apply a two-pass GUID-filter (rows with offset `0x00 == -1` are dropped). Row #17 (no GUID field) is written unconditionally. Row #4 is exported differently (see block 13 below).

**Omitted rows:** #1 (trigger/flag store), #2 (campaign-patch scratch), #3 (variant-value table), #5 (skip-only). Not needed at runtime — VSHELL filters conditions during `.msn` load, `script.dat` carries only survivors.

## Fixed-size file structure

Real files: `ES2\DATA\script.dat` (the live file) plus 9 distinct save-slot snapshots in `ES2\SAV\` (`script0.dat`–`script11.dat`; two pairs are byte-identical to each other and to the live file, so 9 genuinely distinct files there — 10 total). **Note:** these 10 are snapshots of the format, not 10 distinct retail missions.

**Every file is exactly 13,520 bytes** despite wildly different real record counts per block (e.g. row #16's count ranges 7-40 across the corpus). The writer opens the file through `FileRWStream_Open` (`0044e46c`), which has no `O_TRUNC`, so a mission shorter than one written before it leaves that one's tail behind, and a file only ever grows to the longest mission it has held. 13,520 bytes is the length `C1_06.MSN`'s script comes out at with no flag set, the longest of the 62 missions loaded that way. `data\mission.str` is written the same way and keeps a tail the same way.

All 10 parse cleanly, zero desync in the block sequence; only 1 lands exactly on EOF, the rest carry stale trailing bytes past block 13's declared end. **Read only through block 13's declared end; ignore trailing bytes.**

## The two-pass read — and what it means for "DBSIM keeps"

DBSIM reads `script.dat` **twice**, and the two passes want different things:

| pass | function | what it does |
|---|---|---|
| 1 | `DBSim_LoadScriptDat` (`00424308`) | Counts. Keeps blocks 1-6 (the shared reference tables: coordinates, headings, waypoint groups, links, actions, action timers). For blocks 7-9 it keeps **only the type field** of each record, and for block 11 it keeps **only which slots each record activates**. It then allocates one object pool per class, sized to the live count. Nothing is placed. |
| 2 | `DBSim_SpawnMissionObjects` (`004253d8`) | Builds. Re-opens the file, skips blocks 1-6, and re-reads blocks 7-13 in full — constructing each live object and reading its position ref, heading ref, weapon fit and action links straight out of its own record, then grouping them from block 11. |

**Pass 1 alone looks like `script.dat` carries no placement data** (blocks 7-9 reduced to one field each, block 10 discarded, block 11 just flipping activation flags) — that describes pass 1 only, not the format. Pass 2 reads the rest.

Per record type, what pass 2 reads (offsets into the exported record, not the `.msn` source row):

| block | record | offset | field |
|---|---|---|---|
| 7 (mechs) | 134B | `0x00` | AI radar setting → `mech+0x97` |
| | | `0x02` | AI cruise speed → `mech+0x252`; 0 means the default. Both are [`ai-navigation.md`](../simulation/ai-navigation.md)'s |
| | | `0x28` | mech type → index into `nam\MECHS.NAM` |
| | | `0x2a`-`0x3d` | weapon fit, 10 slots → `Mech_ConfigureLoadout` |
| | | `0x3e` | ref → block 1 (position) |
| | | `0x40` | ref → block 2 (heading) |
| | | `0x42` / `0x56` | ten mission-counter refs and their ten operations → `SimObject_SetOutOfActionCounters` (`00411b90`), `mech+0x1ba`/`+0x1ce` — written when the machine goes out of the fight, [`../simulation/mission-deployment.md`](../simulation/mission-deployment.md#the-out-of-action-report) |
| | | `0x6a`-`0x7d` | ammunition type, 10 slots, paired with the weapon fit → `Mech_ConfigureLoadout`'s second array. Only the four launchers read it; every other slot carries the filler 5 |
| | | `0x80` | ref → block 5 — the action this machine fires when it is **engaged** (`mech+0x1b2`) |
| | | `0x82` | ref → block 5 — the action it fires when it is **defeated** (`mech+0x1b6`). Five of `TRAIN8.MSN`'s ten mech records carry one, and that is what chains its reinforcement waves |
| | | `0x84` | **starting condition, per cent.** 100 is pristine; under 80 the machine spawns pre-damaged and under 20 it is placed as a wreck — `Mech_ApplyStartingCondition` (`004178e8`), whose bands are in [`../simulation/component-damage.md`](../simulation/component-damage.md#starting-condition--mech_applystartingcondition-004178e8). The available `script.dat` files are **saves**, formatted from a handful of the 50-odd `.MSN` missions, so they cannot say how the campaign uses this: across those ten, 138 of 139 mech records read 100 and one reads 50 |
| 8 (flyers) | 92B | `0x28` | ref → block 1 (position) |
| | | `0x56` / `0x58` | refs → block 5, the flyer's own engaged/defeated actions |
| | | `0x2e` / `0x42` | mission-counter refs and operations, as block 7's `0x42` / `0x56` |
| | | `0x2a` | ref → block 2 (heading) |
| | | `0x2c` | flyer type → index into `nam\FLYERS.NAM` |
| 9 (bases) | 52B | `0x00` | base type → index into `dat\BASES.DAT`'s 65-entry table |
| | | `0x2e` / `0x30` | refs → block 5, the structure's own engaged/defeated actions |
| | | `0x06` / `0x1a` | mission-counter refs and operations, as block 7's `0x42` / `0x56` — `SimObject_SetOutOfActionCounters` (`00411b90`), run by `Base_ApplyDamage` when the structure goes out of the fight |
| | | `0x32` | **starting condition, per cent** — read by `Base_Construct` (`00405314`); [`../simulation/structure-behaviour.md`](../simulation/structure-behaviour.md#starting-condition) |
| | | `0x02` | ref → block 1 (position) |
| | | `0x04` | ref → block 2 (heading) |
| 11 (groups) | 156B | `0x28` | discriminator: 0/1/2 → block 7/8/9 |
| | | `0x2a` | **formation id** (see below) |
| | | `0x2c` | ref → block 1 (the group's own spawn point) |
| | | `0x2e` | ref → block 2 (the group's heading) |
| | | `0x32`-`0x59` | 20 member refs into the block the discriminator names |
| | | `0x5a`-`0x6d` | 10 refs → block 10, **the group's orders**; slot 0's also supplies the group's spawn route |
| | | `0x6e` | side: 0 = human, 1 = Cybrid (group record `+0x12`) |
| | | `0x70` | ref → block 5 (action). **Set = the group has not entered the mission yet** — see rule 8 |
| | | `0x72` / `0x86` | the group's ten mission-counter refs and operations → `group+0x1c`/`+0x30`, written once every member is out of the fight. Read for record 0 as well |

## Placement — the actual rule

1. **Existence.** Every block-11 record *past the first* activates its members. A roster slot no block-11 record names never spawns, which is why a mission's rosters are routinely bigger than its live object count.
2. **Position.** `DBSim_BuildGroupRecord` (`00423b34`) builds each block-11 record into a group record carrying its point, heading and member list; `Mech_AttachToGroup` (`00417aa8`, mechs) / `Flyer_AttachToGroup` (`00421ee8`, flyers) / `Base_AttachToGroup` (`00405c3c`, bases) then attach each member, filling in the member's position **only if it does not already have one**. So a roster record's own position ref wins where set, and the group's is the fallback. In every retail file the roster refs are unset, so in practice objects stand at their group's point.
3. **Groups with no point** fall back to their route: the group's block-10 link **slot 0** resolves to a waypoint group (link record `+0x08` → block 3), whose first waypoint is the spawn point (`Route_WaypointAt`, `00423b0c`). Patrol squads are placed this way. Only slot 0 is consulted — both this and the heading fallback below read the same `groupRecord+0x44` entry.
4. **Heading**, when the group's own block-2 ref is unset, is the bearing along the route's **first leg**: `DBSim_SpawnMissionObjects` passes waypoints [1] and [0] (second first) to `Math_HeadingToward` (`00492828`), which is `atan2(dy, dx) - 0x4000` — the quarter turn every bearing in the sim carries, since a machine's forward axis is model Y. Fewer than two waypoints leaves it at zero. **Every mech group in every retail mission reaches this**, the player's squad included; none of them carry a heading ref.
5. **Ground height** is not in the file. Mechs and bases get `Terrain_HeightQuery` plus the type's own foot offset (`typeRecord+0x16`, and +5000 when `typeRecord+0x50` is set, which lifts a RAZOR clear of the ground at the start). `Mech_AttachToGroup` queries the ground at the group's point before the formation spread, so every member of a mech group starts at the height of the ground under that point; flyers get no query at all — they hold the spawn coordinate's Z, or 5000 units when that is zero. Bases are then queried a **second** time: placing one levels the terrain under it, so `DBSim_SpawnMissionObjects` re-settles the whole base list once the roster is down — see [`terrain-heightmap.md`](terrain-heightmap.md#structure-footprints--the-flattening-pass).
6. **The player's squad is not in `script.dat`.** Block 11's **record 0** holds its spawn point and names the block-7 slots that stand for its machines — pass 1 skips it when marking activation, and pass 2 overwrites its member list with the entries read from `data\player.mec`. That file's own format is in [`../shell/campaign-loop.md`](../shell/campaign-loop.md#launching-a-mission--game_exportmissionhandoff-0040f0d4).

   **Record 0's member refs are how the mission names the squad.** Every mech ref — an order's subject, an action's target, an objective's subject, another group's member — reaches its machine through one slot-to-pool map, `DAT_004aa812`, which `Mission_ResolveRefByKind` (`00425348`) reads. `DBSim_LoadScriptDat` (`00424308`) fills it in three steps: the slots marked live get consecutive pool indices in slot order; then the slot record 0 names at member position *k* is pointed at the squad's *k*-th machine, the pool index after the last live roster mech plus *k*; then each `player.mec` entry *k* is marked live at slot `block-7 count + k` with that same index. The second step runs after the first, so a slot that record 0 and a live group both name means the squad member. A position whose ref is `-1` leaves that squad machine reachable by no ref, and a ref at a position past the squad's size points at no machine. `C1_01`'s one objective is condition 0 asked of mech slot 3, and slot 3 is record 0's first member: the objective is about the player's own patrol.

Otherwise the squad is an ordinary group and **spreads like one**: pass 2 gives every `player.mec` entry the unset-position sentinel and writes the entries into record 0's member array in file order, so entry *i* attaches as member slot *i* and takes slot *i*'s formation offset (rule 7). Placing the whole squad on the bare point instead stacks it, and `Mech_CollisionTest` then refuses every machine its first step, the player's included.
7. **Formation spread** is applied per member: the member's slot index *within the group's 20 member refs* (0-19, not a compacted live-member count — `DBSim_BuildGroupRecord` passes the raw loop index straight through) goes to the object's own vtable `+0x78`, and that offset is rotated by the group leader's heading before being added to the group's point. Slot 0 (the first member the group claims) always takes no offset, so it lands exactly on the group's point.

- **Mechs.** Vtable `+0x78` is `Mech_ApplyFormationOffset` (`00417898`), reading `Formation_GetSlotOffset(formationId, slot)` (`004205cc`): 28 bytes/formation, seven (x, y) `int16` pairs. Load site: `Mech_LoadResources` (`0041fdb0`) streams `dat\mforms` and writes the vector pointer `Formation_GetSlotOffset` reads (`_DAT_004a9df0`); registered into DBSIM's subsystem-loader table via a thunk at `00420654`. `dat\MFORMS.DAT` is 142 content bytes = 2-byte count (5) + five fixed 28-byte formations, no trailer.
- **Bases.** Vtable `+0x78` is `Base_ApplyFormationOffset` (`00405c04`) for every base subtype (all five base vtables: `0x497940`/`0x4979d4`/`0x4978ac`/`0x497784`/`0x497818`). Nonzero slot calls `BaseFormation_GetSlotOffset(formationId, slot)` (`00405b9c`), reading the table `Base_LoadResources` (`00405fac`, `base.cpp`) streams from `dat\BFORMS.DAT` (opened via literal string `"bforms"`). File is 3,186 content bytes: a count (17) then per formation a slot count + that many 10-byte (x:int32, y:int32, trailing:int16) entries + the three-`int32` terrain trailer and its buffer list ([below](#the-per-formation-trailer)) — byte-exact, nothing left over. `Formation_RotateAndAddOffset` (`00411d64`) reduces to a plain 2D rotation: `worldDX = dx·cosθ − dy·sinθ`, `worldDY = dx·sinθ + dy·cosθ`, added to the group's point.
- **A base formation slot also turns the structure.** The 10-byte slot record's **trailing `int16` is a per-slot heading**, and it is applied on a completely different path from the (x, y) offset above: not by vtable `+0x78`, but by `Base_AttachToGroup` (`00405c3c`) itself, and only when the structure's own record names no heading (the `-0x8000` sentinel — a block-9 record whose heading ref is `-1`):

     ```
     h = group.heading;
     if (slot != 0) h += formation.slots[slot - 1].trailingInt16;
     object.heading = (short)h;      // a short, so the sum wraps
     ```

Every nonzero value in the retail table is a clean turn: 8190 (45°), 16380 (90°), 32760 (180°) or their negatives. Eleven of the seventeen formations use at least one. Reading only the two `int32`s and skipping this short puts every member of a group in the right place facing the same way, which is the failure mode to watch for.

Confirmed on the Scramble training base: group 1 uses formation 9, and roster slots 6 and 8 are two of its three identical silo-cluster structures (type 7). Formation 9's slots 6 and 8 carry 16380 and 32760, and in retail those two stand turned by 90° and 180° while the third does not. The 90° one is at world (989519, 1033792), the base the mismatch was reported against.
- **Mechs:** `Mech_AttachToGroup` (`00417aa8`) has the same heading-fallback shape, but `MFORMS.DAT`'s 28-byte formations are seven bare (x, y) `int16` pairs with no room for a per-slot heading ([Open](#open)).
- **Anchor adjustment — implemented.** A base group with its paints-ground flag set is moved onto a fixed spot in its terrain tile before any per-member offset is added. See [Base formation terrain](#base-formation-terrain).
- **Flyers.** Vtable `+0x78` is `Flyer_ApplyFormationOffset` (`00421e98`), reading `dat\FFORMS.DAT` — [below](#the-flyer-formation-table). `Flyer_AttachToGroup` (`00421ee8`) is the flyer attach equivalent. `TRAIN8.MSN` has one two-flyer group, in formation 3.
- **Verification:** all 10 available missions — 26/26 multi-mech groups and 18/18 multi-base groups get distinct member positions, 0 exceptions; BFORMS.DAT/MFORMS.DAT both still parse byte-exact.
8. **A group whose record names a block-5 action (`0x70`) is not in the mission yet** — undrawn, unsimulated and non-solid until that action fires and the group arrives, on foot or by drop pod. Its placed position is a placeholder the arrival overwrites, which is why retail missions leave such groups stacked on shared points (routinely the player's own spawn). See [`../simulation/mission-deployment.md`](../simulation/mission-deployment.md); **do not read a waiting group's position as where the mission means it to be.**

## The flyer formation table

`Flyer_LoadResources` (`00422d90`, `flyersys.cpp`) opens `dat\fforms` (string at `0049a68c`), reads a 2-byte record count and then that many `0x12`-byte records. `dat\FFORMS.DAT` is 92 content bytes, a count of 5 and five records, with nothing left over. `FlyerFormation_GetSlotOffset` (`00423044`) resolves an offset as `base + formationId * 0x12 + slot * 6 - 6`, so a record is **three** slots of three `int16` (x, y, z), and the slot index is **one-based**: the group's first member, the flight leader, takes no offset at all.

The offset carries a Z, where `MFORMS.DAT`'s and `BFORMS.DAT`'s entries do not. `Flyer_ApplyFormationOffset` hands all three components to the shared `Formation_RotateAndAddOffset` (`00411d64`), which rotates the (x, y) by the **group leader's** heading and adds the Z unrotated. Every slot is 400 units higher than the one before (400, 800, 1200). Formations 0, 1, 2 and 4 are a trailing column, 2500, 5000 and 7500 units aft (−y). Formation 3, the one `TRAIN8.MSN`'s two-flyer group uses, is a box: slot 1 at (−2500, 0), slot 2 at (0, −2500) and slot 3 at (−2500, −2500).

Once placed, a wingman is held to the same offset in flight — [`../simulation/ai-flyers.md`](../simulation/ai-flyers.md#station-keeping).

## Base formation terrain

When a block-11 record's paints-ground flag (`0x06`) is set — 39 of 61 retail records — a **base** group does two things no other group does, both from the same `BFORMS.DAT` record and both gated by that one flag. `Base_AttachToGroup` (`00405c3c`) moves the group's shared anchor onto a fixed spot in a terrain tile, and `Base_ApplyFormationTerrain` (`00405db0`) paints that tile with the formation's own material. They go together: the move is what puts the structures on the pad. Machines are unaffected — `Mech_AttachToGroup` (`00417aa8`) takes no such flag and has none of this arithmetic.

The painting side, including the tile geometry both share, is in [`terrain-texturing.md`](terrain-texturing.md#base-formation-pads).

### The per-formation trailer

Each formation's slot list is followed by three `int32`s and a length-prefixed buffer list. The in-memory record `Base_LoadResources` (`00405fac`) builds is `0x1c` bytes:

| offset | field |
|---|---|
| `+0x00` | slot count |
| `+0x04` | pointer to the slot array |
| `+0x08` | **material index**, or `-1` for a formation with no terrain of its own |
| `+0x0c` | anchor position across the tile, in 256ths from the low-x edge |
| `+0x10` | anchor position down the tile, in 256ths from the **high**-y edge |
| `+0x14` | **`dim`** — buffer count, and the side of the square layout map |
| `+0x18` | pointer to the `dim` row pointers |

Eleven formations carry a material and a map; the other six carry `-1`, `0`, `0` and no buffers. Formation 5's map reads as a legible L-shaped compound, which is the shape of the concrete in that material's frame:

```
.#####..
.#####..
.#####..
.##..##.
.....##.
.....##.
........
........
```

### The anchor

```
tile = dim << 13                                 the painted tile, sized in terrain-texturing.md
x'   = (x & ~(tile-1)) + acrossFraction * tile/256
y'   = (y & ~(tile-1)) + tile - downFraction * tile/256
```

The original spells this `(x & ~mask) + b*step` / `((y & ~mask) + mask + 1) - c*step` with `mask = dim*0x2000 - 1` and `step = dim*0x20`, computed per member on a copy of the group's point, which is equivalent. The operands are `+0x14`, `+0x0c` and `+0x10` — the map dimension and the two fractions. **Only which tile the mission's point falls in survives** — the position within the tile is discarded and replaced by the formation's own fraction of it.

Four independent checks agree on this reading. All 111 members of all eleven mapped formations fall inside their own map's bounds, against 64 for the inverted y convention. Every `dim` is predicted by its material's block shift, 11 of 11. Across the ten retail handoffs every base group's structures then sit entirely inside the tile painted for them, 11 of 11, where the unmoved anchor leaves them straddling a tile edge. And in the mission editor the move closes the player-to-base distance in the Scramble training mission by 147 m, in the direction and by roughly the amount that base was visibly too far away.

A formation's layout map also marks its cells for the per-object flattening pass in [`terrain-heightmap.md`](terrain-heightmap.md#structure-footprints--the-flattening-pass), which owns that pass and what the two inputs add up to.

## The 13-block structure

The "DBSIM keeps" column below describes **pass 1 only** — see the two-pass section above for what pass 2 goes back for.

| # | `.msn` row | on-disk shape | GUID-filtered? | DBSIM pass 1 keeps | VSHELL `ShellMap` keeps |
|---|---|---|---|---|---|
| header | — | fixed 20 bytes, 10 shorts from unrelated `DAT_004854xx` globals (not part of the 17-row `.msn` table at all) | — | parses into several scalar fields, one passed on to a later call | parses into several scalar fields, one passed on to a later call |
| 1 | #6 points | count + count×12B (X,Y,Z int32 triple only — GUID/condition/etc. dropped) | yes | full (min/max bbox tracked live as read) | full |
| 2 | #7 headings | count + count×2B (the `0x08` payload short only) | yes | full, **× 182 (`0xb6`) at load** — the same degrees→BAM conversion already confirmed elsewhere in DBSIM; this reframes row #7's payload as **a heading/orientation in degrees**, not a difficulty tier | full, **not** multiplied: the map turns its squad's formation by the value as stored |
| 3 | #8 waypoint groups | count + per record: nested-count (2B) + nested-count×2B (waypoint refs into block 1) | yes | full, nested refs resolved to block-1 pointers (stride 3 ints) | full, the refs kept as block-1 indices |
| 4 | #9 trigger areas | count + count×6B (`0x06` shape, `0x08` ref1, `0x0A` ref2/radius) | yes | full, resolved by `TriggerArea_Resolve` (`00423358`) into a 10-byte record — **a trigger area**: type flag at `+0`, a block-1 pointer at `+2`, then at `+6` either a second block-1 pointer (type 0, an XY box) or the literal × 10 (type != 0, a radius). Tested by `TriggerArea_ContainsPoint` (`004233a4`), see [`../simulation/mission-deployment.md`](../simulation/mission-deployment.md#the-areas--block-4-resolved-by-triggerarea_resolve-00423358) | **skipped** (seek past, discarded) |
| 5 | #10 actions | count + count×74B (`0x06` type, `0x08` verb, `0x0A`-`0x19` ref[0..7] into row 9, `0x1C`-`0x43` counter pairs as two interleaved spans, `0x44`-`0x4D` text ref[0..4], `0x4E` message, `0x50` target; `0x1A`, the pair count, is not exported) | yes | reads all 74B but only **keeps** type, verb, the 8 refs (resolved to row-9 pointers), the counter refs and operations, the message (decremented by 1), and target — **the text refs are read then discarded** | **skipped** (seek past, discarded) |
| 6 | #11 timers | count + count×24B (`0x06` ref into row10, `0x08` delay, `0x0A`-`0x1D` 10-slot ref array into row10) | yes | full, resolved via `DBSim_BuildActionTimerRecord` (`00423104`) — **a mission timer**, see below | **skipped** (seek past, discarded) |
| 7 | #12 mech roster | count + count×134B (`0x08`-`0x2F` 40B span, `0x30` mech type, `0x32`-`0x45` weapon fit, `0x46`/`0x48` 2 shorts, two 20-short interleaved spans, `0x74`-`0x87` 20B span, 4 trailing shorts; the pair count at `0x4A` is the one field of row #12 skipped/not exported) | yes | reads all 134B but keeps **only the mech type (`0x30`)** — confirmed via the writer's own assert string on this field ("Invalid mech type"). Pass 2 comes back for the rest | **full 134B kept**; the map places a squad member only when its block-7 index is below this block's count |
| 8 | #13 flyer roster | count + count×92B (`0x08`-`0x33` flag span + refs, `0x34` flyer type, `0x38`-`0x5F` out-of-action pairs, `0x60`-`0x64` action refs + a constant `100`; the pair count at `0x36` is skipped/not exported) | yes | reads all 92B but keeps only **the flyer type (`0x34`)**. Pass 2 comes back for the rest | **skipped** (seek past, discarded) |
| 9 | #14 base roster | count + count×52B (`0x08` base type, `0x0A`/`0x0C` refs, `0x10`-`0x37` out-of-action pairs as two interleaved spans, `0x38`/`0x3A` action refs + `0x3C` starting condition; the pair count at `0x0E` is not exported) | yes | reads all 52B but keeps only **the base type (`0x08`)** — an index into `dat\BASES.DAT`'s 65-entry table. Pass 2 comes back for the rest | **full 52B kept**, and in that copy each type-2 block-11 record writes its `0x9a` over its members' `0x1a` (operation 0) and its side `0x6e` over `0x1c` (operation 1) — [`../shell/mission-map.md`](../shell/mission-map.md#what-it-reads). The file is never rewritten, so DBSIM's pass 2 reads the record's own operations |
| 10 | #15 orders | count + count×14B (`0x08`-`0x15`: verb, formation, point, route, subject kind and ref, action) | yes | pass 1 reads all 14B and discards it; **pass 2 resolves it** into a 22-byte **mission-group order** ([below](#block-10-in-memory--22-bytes-0x16)) — and a group's route and spawn point come from its slot-0 link's `0x08` | **full 14B kept** — the map takes its nav path, its squad's anchor and formation from record 0's first order |
| 11 | #16 groups | count + count×156B (`0x06`-`0x77`, then the out-of-action pairs as two interleaved spans and `0xA2`; the pair count at `0x78` is not exported) | yes | **this is DBSIM's entity-activation mechanism**: for each populated cross-ref entry, the discriminator (0/1/2) marks the referenced **block-7/block-8/block-9** slot as a *live, simulated* object (via `DAT_004aa7ae`/`DAT_004aa8da`/`DAT_004aa93e`+`DAT_004aaa56` flag arrays), turning declared roster entries into things DBSIM actually spawns. **Record 0 is skipped here** — it is the player's squad, and its refs feed the slot map instead ([rule 6](#placement--the-actual-rule)). Pass 2 comes back for the group's own position/heading/route | full 156B kept; every type-2 record moves the bases it names and sets whether the map shows them, and record 0 is the squad the map draws |
| 12 | #17 objectives | count (unfiltered — all records, matching row #17's "no GUID field" nature) + count×54B | **no** | reads all 54B and discards it; **pass 2 comes back and builds the mission's objectives** from it — see below | **skipped** (seek past, discarded) |
| 13 | #4 text | flat tail: **one** count (how many of row #4's 10-slot sub-array A are populated, from its front — assumes no gaps) + that many×2B (the refs themselves) | n/a — single mission-level record, not a per-entity array | full — **the mission's objective list as the player is shown it**: one `data\mission.str` line index per entry. Count into `DAT_004a9ec8`, refs into `DAT_004a9ecc`, and the only reader is the in-mission objectives panel (`obj_alrt`, `ObjectivesPanel_Ctor` (`0045751c`)), which prints one label per entry | not read (VSHELL's `ShellMap` reader stops after block 12) |

### Block 5 in memory — 58 bytes (`0x3a`)

The runtime action record `DBSim_LoadScriptDat` builds. **The field-to-offset mapping is that function's read order**, which is the only statement of it: two shorts, sixteen bytes of block-4 refs into a stack buffer, twenty bytes to `+0x0c`, twenty more to `+0x20`, ten bytes read and dropped, then `+0x34` and `+0x36`. What each field then means is [`../simulation/mission-deployment.md`](../simulation/mission-deployment.md)'s.

| offset | from | field |
|---|---|---|
| `0x00` | `0x06` | type — selects whose position the trigger tests |
| `0x02` | `0x08` | verb — selects how a group holding this action arrives |
| `0x04` / `0x06` | `0x0A`-`0x19` | count of, and pointer to, the resolved block-4 trigger areas. **The count stops at the first negative ref**, not at the eighth slot: the load pass counts up to the first `-1` and allocates exactly that many, so a populated slot behind a gap is never tested |
| `0x0a` | — | **activation flag** — runtime only, zeroed at load, set once by `Action_Activate` (`00423430`) |
| `0x0c` | `0x1C`-stride span | ten mission-counter refs |
| `0x20` | `0x1E`-stride span | ten parallel operations: 6 increments the counter, 5 clears it |
| `0x34` | `0x4E` | the mission message queued on activation, **file value − 1** |
| `0x36` | `0x50` | target ref, resolved in place to an object or group pointer by `DBSim_SpawnMissionObjects` |

The text refs at `0x44`-`0x4D`, `mission.str` lines like every other text ref in the file, are the ten bytes read and dropped; DBSIM has no use for them.

### Block 6 in memory — 49 bytes (`0x31`)

`DBSim_BuildActionTimerRecord` (`00423104`) resolves each ref to a block-5 record pointer and arms the timer through `ActionTimer_SetDelay` (`004679c0`), which stores the file value **shifted left 11** into the simulation's timer unit — 2048 counts, so the on-disk unit is exactly one second.

| offset | from | field |
|---|---|---|
| `0x00` | `0x06` | the primary action, or null. Null means the timer runs from mission start |
| `0x04`-`0x28` | `0x0A`-`0x1D` | ten action pointers, activated together when the timer expires |
| `0x2c` | `0x08` | the countdown, in the simulation's timer unit — [`../simulation/dbsim-physics-notes.md`](../simulation/dbsim-physics-notes.md#timer-units) |

This is the mission's timer, and it is why an action carrying no trigger area of its own is ordinary rather than dead.

### Block 10 in memory — 22 bytes (`0x16`)

The **mission-group order** record. `DBSim_SpawnMissionObjects` (`004253d8`) allocates `orderCount * 0x16` and fills one record per block-10 entry, resolving each of that entry's four refs to a pointer as it goes. The seven source fields land in a different order than they are read in. What each field means to the simulation, and which of them nothing reads, is [`../simulation/ai-goals.md`](../simulation/ai-goals.md#the-order-record)'s.

| offset | from | field |
|---|---|---|
| `0x00` | `0x08` | verb, 0-6 |
| `0x02` | `0x0a` | short, copied verbatim |
| `0x04` | `0x0c` | ref → block 1 (a point), resolved to a pointer |
| `0x08` | `0x0e` | ref → block 3 (a waypoint group), resolved to a pointer — the order's route |
| `0x0c` | `0x10` | subject kind: `-1` nothing, 0 a group, 1 a mech, 2 a flyer, 3 a base |
| `0x0e` | `0x12` | subject ref, resolved against the kind by `Mission_ResolveRefByKind` (`00425348`) — a group record for kind 0, an object for kinds 1-3 |
| `0x12` | `0x14` | ref → block 5 (the action that ends the order), resolved to a pointer |

### Block 12 in memory — 76 bytes (`0x4c`)

The **mission objective** record. `DBSim_SpawnMissionObjects` (`004253d8`) reads the 54 bytes pass 1 threw away into a stack buffer and resolves them in that buffer's own order, which is the only statement of the file layout. What each field then means is [`../simulation/mission-objectives.md`](../simulation/mission-objectives.md)'s.

| offset | from | field |
|---|---|---|
| `0x00` | `0x00` | required flag |
| `0x02` | `0x02` | condition code |
| `0x04` | `0x04` | subject kind, 0-3 |
| `0x06` | `0x06` | subject ref, resolved by kind through `Mission_ResolveRefByKind` (`00425348`) |
| `0x0a` | `0x08` | block-1 point, or null. Read by nothing |
| `0x0e` | `0x0a` | block-3 waypoint group, or null |
| `0x12`-`0x21` | `0x0c` | **four `char*`**: three consecutive `data\mission.str` lines from the stored index, then the empty string at `0049a870` |
| `0x22` | — | runtime only, zeroed by the allocation |
| `0x24` | `0x0e` | ten mission-counter refs |
| `0x38` | `0x22` | ten parallel operations |

## Verification

Three independent real readers (`DBSim_LoadScriptDat`, `DBSim_SpawnMissionObjects` and `ShellMap_LoadScriptDat`) agree on identical block order and strides. A byte walker over 10 real files (`ES2\DATA\script.dat` + 9 distinct save-slot snapshots) parses all of them cleanly with zero desync through the end of block 13, and a writer built on the layout reproduces all 10 byte-exact. Placing all 10 by the rule above resolves every zone, theater, roster, group and player squad with no unclaimed live slots, and lands every placed object inside its zone's bounds.

## Header format

| offset | meaning |
|---|---|
| 0 | theater index, 0-4 — selects `wld\world<index * 2 + variant>.wld` (texture bank, palette) |
| 2 | zone index — passed to `Terrain_LoadZone` |
| 4 | a mode flag (`MissionModeFlag`, `004a9ed6`). The writer emits a literal 1 and the reader stores 0 over it before anything reads it, so the value on disk never reaches a consumer; what it gates is [the two cheat fields below](#the-training-fields) |
| 6 | **mission objective type** (`DAT_004a9ed8`) — which arm of the player's think watches for progress, and whether the AI is kept off the data-link subject. See [`../simulation/mission-objectives.md`](../simulation/mission-objectives.md#the-player-thinks-objective-arms). Set by the `.MSN` header patch, and 0 where none sets it: 2 in `TRAIN1`, `TRAIN3` and `TRAIN4`; 3 in `TRAIN2` and six campaign missions (`C1_08`, `C2_04`, `C3_03`, `C5_01`, `C5_03`, `C5_06`); 7 in `C1_09`, behind a condition. Selector 5 is in no mission's patch |
| 8 | **training mission number** (`ScriptDatTrainingMission`), 0 for anything that is not one — see [below](#the-training-mission-number). Set by the `.MSN` header patch: `TRAIN1`-`TRAIN4` carry 1-4; `TRAIN5`-`TRAIN8` and every campaign mission leave it 0 |
| 10 | **unlimited ammunition and energy** (`UnlimitedAmmoFlag`, `004a9edc`) when 1 |
| 12 | **player invulnerable** (`PlayerInvulnerableFlag`, `004a9ede`) when 1 |
| 14 | **mission difficulty**, 0-3 (`MissionDifficulty`, `004a9ee0`) — see [`../simulation/difficulty.md`](../simulation/difficulty.md). A mission's own header patch is overwritten before the file is written |
| 16 | zero across the corpus and unread by `DBSim_LoadScriptDat` |
| 18 | theater variant, 0 or 1 — low bit of world number, and the practice missions screen's `Day` / `Night` row |

`WriteScriptDatFile` writes the ten shorts from `00485446` upward, and `DBSim_LoadScriptDat` reads all 20 bytes into `004a9ed2` in one call, so a field's address in DBSIM is `0x4a9ed2 + offset`.

The three world fields are confirmed by `DBSim_LoadScriptDat` → `Terrain_LoadZone` / `maybe_World_LoadTheater`. See [`terrain-texturing.md`](terrain-texturing.md) for theater details.

### The training mission number

`DBSim_LoadScriptDat` only stores offset 8. Every reader takes the copy `TrainingMissionNumber` (`004aa7ac`) made at the end of the load (`00425321`), and four sites read it:

- **No music.** `Sim_InitMissionSession` (`00461c8f`) sets the CD track only when it is 0, so a training mission runs in silence — see [`audio.md`](audio.md#which-track-and-whether-there-is-one).
- **A different pilot and squad port.** `Gau_BuildCockpitWidgets` (`00431cda`) builds the `0x4ef`-byte training port at `view+0x207` instead of the ordinary `0x4df`-byte one, and on that arm alone raises the port's `.GAU` rect by the herc's training lift — [`cockpit-messages.md`](cockpit-messages.md#the-training-port).
- **Its instructor's script.** The same function (`00431dab`) indexes `COMMAND<n>.STR` into the port with this number as the digit whatever its value, so an ordinary mission loads `COMMAND0.STR`.
- **Its own voice clips.** The training port's paint (`004368fb`): the instructor speaks from the `TM<n>_` name template rather than the squad's `P<bank>_` one, with this number as the digit. Both are [`cockpit-messages.md`](cockpit-messages.md#the-training-port)'s.

### The training fields

Offsets 10, 12 and 14 are written by `MsnGen_LoadMission` (`0041c73d`, VSHELL) rather than parsed out of the `.msn`, over what the mission's own [header patch](msn-mission-file.md#the-header-patch--row-2) left there, on `CampaignModeFlag` (`DAT_0048260c`):

| Mode | 10, unlimited | 12, invulnerable | 14, difficulty |
|---|---|---|---|
| 1, campaign | forced 0 | forced 0 | `PlayerPilotSkill` (`00482aa1`), the player pilot record's skill at `+0x25` |
| 0, training — the practice missions and `INSTANT ACTION` | `prefs.cfg` option `0x25` | option `0x26` | option `0x27` |

The options are the practice missions screen's rows ([`../shell/screen-layout.md`](../shell/screen-layout.md#the-parameters)); what the three fields do in the simulator is [`../simulation/difficulty.md`](../simulation/difficulty.md).

The same code sets two more fields. A campaign load writes offset 0, the theater, from the stage's campaign index in `gam\career.dat`. A training load writes offset 0 as 1 whatever the mission patched it to — `TRAIN5.MSN` patches in 3 and flies on theater 1 — offset 16 as 0, and offset 18, the variant, from the practice screen's `Time of Day`.

## Reading script.dat

Stop after block 13's declared end and ignore trailing bytes. Files may have stale buffer content past the real data (see "Fixed-size file" section). DBSIM's reader ignores trailing bytes; padding is not required.

## Rejected readings

| Reading | Why it is wrong |
|---|---|
| The anchor formula's three operands are the trailer's three `int32`s in file order | They are `+0x14`, `+0x0c`, `+0x10`. File order feeds the material index in where `dim` belongs, and a material index of 6 makes `mask` `0xbfff` — not a power of two minus one — so structures land tens of thousands of world units away while still passing a distinct-positions check |
| The paints-ground anchor move is the structure-footprint flattening, or feeds it directly | The flattening is per-object, driven by each structure's own shape radius, and reads nothing from `BFORMS.DAT`. What the flag's `BFORMS.DAT` record contributes there is the layout map's cell marks, not the move |
| Block 13 is the mission's herc/weapon unlock package | Its source, row #4's sub-array A, is described as refs into a small shared LUT, and DBSIM's own field name for the row #17 twin is a LUT ref — so "unlocks reaching DBSIM" is the obvious reading. It is a list of `data\mission.str` line indices: the values run to 243 in the `.MSN` corpus, they are consecutive runs, and the one consumer prints them as labels. Weapon unlocks reach a mission through the tail of `data\player.mec` and are authoritative in the save slot — see [`save-games.md`](save-games.md) |
| Record 0's member refs are dead because pass 2 replaces its member list with `player.mec`'s machines | Pass 1 reads them first and points each named block-7 slot at the squad machine in the same position ([placement rule 6](#placement--the-actual-rule)). A reader that numbers the squad's machines 0, 1, … in place of those slots resolves a mission's refs to the squad — `C1_01`'s objective among them — to nothing, and its refs to roster slots 0, 1, … to the squad |
| Block 12 is dead because the load pass reads and discards it | Pass 1 discards it exactly as it discards most of blocks 7-9; pass 2 comes back. Anything reading only `DBSim_LoadScriptDat` reaches the same wrong conclusion about the roster |

## Open

- **Open:** whether mechs get any per-slot heading turn on formation attach, the way bases do — `MFORMS.DAT`'s 28-byte formations have no field for one, so the fallback shape `Mech_AttachToGroup` shares with `Base_AttachToGroup` may simply have nothing to read.
- **Open:** which arm of the player's think objective type 2 selects. `TRAIN1`-`TRAIN4` patch header offset 6, the objective type, to 2, 3, 2 and 2.
