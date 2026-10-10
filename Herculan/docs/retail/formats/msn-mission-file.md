# .MSN mission file (ZONES.VOL/MSN/*.msn) and its VSHELL load path

**Macro-structure: revision field + 17 array/skip rows in order, verified against all 62 real retail `.MSN` files (one truncated — see [Verification note](#verification-note)).** Reversed from `VSHELL.EXE` disassembly and validated against real data.

## Call chain — confirmed

- `Career_LoadCurrentMission` (`0044d4cc`) calls `MsnGen_LoadMission(path)` (`0041c73d`) with the career position's name from [`gam\career.dat`](../shell/campaign-loop.md#the-campaign-table--gamcareerdat), a path such as `MSN\TRAIN1.MSN`. It is reached through [the mission-name dialog](../shell/main-menu.md#the-mission-name-dialog), whose `Use Default` button it is; the dialog's other button, `MissionNameDialog_OnLoad` (`0044d5bd`), calls `MsnGen_LoadMission` with a typed `msn\<name>.msn` instead.
- `MsnGen_LoadMission` (asserts trace to `msn_gen.cpp`) seeds the campaign flags ([`../shell/campaign-loop.md`](../shell/campaign-loop.md#the-campaign-flag-array-is-the-msn-condition-store)), calls `MsnGen_ParseMsnFile(param_1)` (`00417b67`) with the `.msn` path, fills the header's training fields ([`script-dat.md`](script-dat.md#the-training-fields)), and has `WriteScriptDatFile` (`0041ac54`) export a subset of the loaded data as `data\script.dat` for DBSIM and the briefing's map, VSHELL's `ShellMap`. A training load then builds the squad from the mission ([`../shell/main-menu.md`](../shell/main-menu.md#starting-a-practice-mission)); a campaign load copies row 4's text into the career block instead ([`../shell/campaign-loop.md`](../shell/campaign-loop.md#loading-the-careers-mission)).
- Separately, `ShellMap_Constructor` (`00423f43`, vtable `&PTR_FUN_004721b0`) opens `data\mission.str` and `data\maplabel.str` as string tables, then reads `data\script.dat` directly via `ShellMap_LoadScriptDat` (`004243d7`, source `shellmap.cpp`) — the briefing's map ([`../shell/mission-map.md`](../shell/mission-map.md)), a second consumer of the same data exported from `.msn` parsing. See [`script-dat.md`](script-dat.md) for the relationship.
- The save-slot handoff copies the three loose working files in and out of a numbered slot, and the two directions are separate functions (source `career.cpp`): `Career_SaveSlot` (`00412a71`) saves, `data\` to `sav\`, and `Career_LoadSlot` (`00412bbf`) loads, `sav\` to `data\`. The pairs are `data\script.dat`/`sav\script%d.dat`, `data\mission.str`/`sav\missn%d.str` and `data\player.mec`/`sav\player%d.mec`, matching the dev note in `herc-works-mdk-main/docs/arch/3space_filetypes_sav.txt`. See [`save-games.md`](save-games.md).

## `MsnGen_ParseMsnFile` (`00417b67`) — the raw `.MSN` parser

Opens the stream (`VolRStream_CtorFromPath` (`00402ad9`)), then **asserts a revision field equals `5`**. It reads row 1, the conditions; row 2, the header patches; the mission's [`.ENG` text](#the-eng-string-table) (`Msn_LoadEngText` (`0041768c`)); then rows 3 to 17 in order, each `[uint16 count]` and that many records (fixed-size but for row 8's), with row 5 skipped. The parser does campaign-state-aware filtering *while* loading: every record goes through a condition test and survivors are **compacted in place**, and each ref into another row is renumbered to the index `script.dat` gives its target — the count of records before it whose GUID is not `-1`. A ref into a row not loaded yet waits: an order's group subject and an action's target are resolved in two passes after row 16.

### The conditions — row 1

Every record in the file carries a condition ref, at `0x02` (rows 2, 4, 5 and 17 carry it at `0x00`). `Msn_ConditionFilterGate` (`00417610`) passes a record whose condition is `-1` or names a surviving row-1 record; any other record is dropped. Row 1 is filtered by its own records as it is read, each against the survivors before it, and a row-1 record survives by its type:

| type | fields | survives when |
|---|---|---|
| 0 | `0x06` flag index, `0x08` operator, `0x0a` operand | its own condition passes and the comparison holds; `0x08` becomes the result |
| 1 | `0x06` bound | its own condition passes; `0x08` becomes a draw below the bound |
| 2 | `0x02` its parent, `0x06`-`0x08` a range | the parent is a type-1 record whose draw falls in the range (unsigned), or a type-3 record, which always passes (`Msn_ConditionParentValue` (`004159d0`) answers `-99`) |
| 3 | `0x06` a variant key, `0x0a` a bound | its own condition passes |

Type 0's operator is a `switch` on `0x119`–`0x11e`, comparing `DAT_00482af8[0x06]` — the campaign flag store: 1,000 `int16` persisted in every save slot and round-tripped to DBSIM through `data\mission.var`, where the same array is `DAT_004a9ef4` ([`../shell/campaign-loop.md`](../shell/campaign-loop.md)) — against the operand. Any other code fails.

| code | operator |
|------|----------|
| 0x119 | `==` |
| 0x11a | `!=` |
| 0x11b | `<`  |
| 0x11c | `<=` |
| 0x11d | `>`  (operands swapped) |
| 0x11e | `>=` (operands swapped) |

| offset | field | notes |
|---|---|---|
| `0x00` | GUID | what a condition ref names |
| `0x02` | condition | 43% populated; a type-2 record's parent |
| `0x04` | type | 4 types: `0`/`2`/`3`/`1` (47%/39%/12%/1.5%) |
| `0x06` | flag index / bound / range low / variant key | by type, above |
| `0x08` | operator / draw / range high | by type, above |
| `0x0A` | operand / type 3's bound | mostly `0` (71%) |
| `0x0C` | type 3's latest draw | `0` in every file; written at load |

The draws are VSHELL's generator's, described in [`../shell/campaign-loop.md`](../shell/campaign-loop.md#the-shells-generator).

### Variants

Rows 6, 7, 8, 12, 13, 14 and 15 carry a variant key at `0x04`. A record whose key is `-1` resolves its own refs. Any other key names a type-3 row-1 record by its `0x06`: `Msn_PickVariant` (`00415fc3`) draws below the first surviving such record's `0x0a`, stores the draw at its `0x0c`, and takes the type-2 child whose range holds it. When no child holds the draw it moves on to the next surviving type-3 record with the same key and draws again, so a second table under the same key is reached only by the draws the first one leaves uncovered; with none left the pick is `-1`. The record then copies its payload from the first record of its own row, loaded before it, whose condition is that child's GUID (`Msn_VariantSourceRow6` (`00416120`) for row 6, and a sibling per row). It draws again for every record that asks, so two records sharing a key can land on different variants.

The records a variant copies from are ordinary records whose GUID is `-1` — all 783 retail records conditioned on a type-2 child are — so they are never exported themselves: a mission authors each alternative once, conditioned on its type-2 child, and every record that wants a random pick among them names the key. What each row copies:

| row | copied from the variant |
|---|---|
| 6 | the coordinates |
| 7 | the heading |
| 8 | the waypoint list |
| 12 | everything from `0x08` to `0x8f` but `0x4a`, refs already resolved |
| 13 | everything from `0x08` to `0x65` but `0x36` |
| 14 | everything from `0x08` to `0x3d` but `0x0e` |
| 15 | `0x08`-`0x15` |

Row 6 also has a sum flag, `0x08`: when set, the point is the sum of the two earlier points whose GUIDs are the low words of its own X and Y.

### Repeated GUIDs

A record whose GUID an earlier survivor already has does not add a record. Rows 3, 6, 7, 8 and 9 **replace** the earlier record with it; rows 10 to 16 **overlay** it, copying each field that is not its unset value onto the earlier record (`Msn_MergeRow10` (`00416521`) for row 10 and a sibling per row). The unset value is `-1` for refs and most payload, `0` for the 20-word blocks at `0x06`/`0x08` and for row 10's message and row 11's delay, `100` for the trailing condition fields, `5` for row 12's ammunition types and `2` for its `0x88`. Records whose GUID is `-1` are never merged, and never written to `script.dat`.

### The header patch — row 2

82 bytes read into one reused buffer and never kept: a condition, ten header words and thirty flag indices. The ten header words (`DAT_00485446` upward) are reset once row 1 is read — the first to 1, the rest to 0 — and each row-2 record whose condition passes writes every header word that is not its unset value, 1 for the first and 0 for the rest, and every flag index that is not `-1` into the clear list at `DAT_0048545a`. Then every flag the clear list names is zeroed (`Msn_ClearPatchedFlags` (`00417659`)). The clear list lies in VSHELL's uninitialised data, so it starts at zero, and no reset of it has been found ([Open](#open)); a load therefore clears flag 0 thirty times over until a patch names something else. No retail patch names a flag: none of the 145 row-2 records in the 62 files sets a flag index, so every retail load clears flag 0 and nothing more. The clear comes after row 1's comparisons, and the debrief stores the mission's outcome in flag 0 before its reload compares anything ([`../shell/campaign-loop.md`](../shell/campaign-loop.md#the-debrief--game_processmissionresults-0040eae7)), so no condition of either load sees the cleared value. The header words are [`script-dat.md`](script-dat.md#header-format)'s, and every retail mission sets its zone this way: each of the 62 has a row-2 record with a non-zero zone word.

### Record-array table — **empirically confirmed byte-exact against 61/62 real `.MSN` files**

Two shapes need care:

- A **skip-only row** (`DAT_0047066a`) sits between the 144-byte array (#4) and the 22-byte array (#6): this load reads a count, then seeks forward `count * 0x40` (64) bytes **without storing anything**.
- The nested-array row (#8) is **not** `count * 18` bytes, the in-memory stride. Per record it is 10 fixed bytes (5 shorts, the 5th being a nested-entry count), followed by that many nested entries of **2 bytes each on disk** — the in-memory slot is 6 bytes, and its other 4 are zeroed, never read from the file.

| # | count global | on-disk shape | storage global | cross-refs into | best current guess |
|---|---|---|---|---|---|
| 1 | `DAT_0047064c` | 14 (`0xe`) bytes/record | `DAT_00470604` | `DAT_00482af8` (flags), self (via `0x02`) | **decoded — see [The conditions](#the-conditions--row-1).** The campaign flag comparisons, random draws and their ranges every other row's condition ref names |
| 2 | `DAT_00470648` | 82 (`0x52`) bytes/record | *(scratch, not stored)* | — | **decoded — see [The header patch](#the-header-patch--row-2)** |
| 3 | `DAT_00470666` | 8 bytes/record | `DAT_0047063c` | referenced by #4 | **decoded — see "Row #3 field decode" below.** A small campaign-variant value lookup: GUID + condition + payload, where the same GUID can carry several condition-gated payload variants and the last to survive wins |
| 4 | `DAT_00470668` | 144 (`0x90`) bytes/record | `DAT_00470640` | 3 sub-arrays (10, 30, 30 shorts) of `.ENG` string ids; 1 ref into #3 | **decoded — see "Row #4 field decode" below.** The mission's objective, briefing and intelligence text and its briefing movie. No GUID/identity field at all (offset `0x00` is the condition ref instead); nothing else in the file references this row |
| 5 | `DAT_0047066a` | **skip-only**, `count * 0x40` bytes, nothing stored | — | — | skipped by this load; the debrief's reload keeps it — see [Row #5](#row-5--the-debrief) |
| 6 | `DAT_0047064e` | 22 (`0x16`) bytes/record | `DAT_0047060c` | self (variant, sum) | **decoded — see "Row #6 field decode" below. A 3D world-position/waypoint record**: GUID + 3 dead fields + an int32 X/Y/Z triple. This is the record every row #9 corner and centre ref (492 in retail, all resolving), and several other rows' refs, ultimately resolve to |
| 7 | `DAT_00470650` | 10 bytes/record | `DAT_00470610` | self | **decoded — see "Row #7 field decode" below.** A minimal record: GUID + 3 fully-dead fields + one small discrete payload (`0`/`1`/`10`) — the simplest record type in the file. Rows #12, #13, #14 and #16 name it as a heading |
| 8 | `DAT_00470656` | **variable**: 10 fixed bytes/record + (nested-count × 2) bytes | `DAT_0047061c` | #6 (nested entries) | **decoded — see "Row #8 field decode" below.** A named, orderable list of row #6 world positions — a patrol route/waypoint chain, with real evidence of both spatial coherence and closed-loop (patrol circuit) structure |
| 9 | `DAT_0047065e` | 12 (`0xc`) bytes/record | `DAT_0047062c` | #6, self | **decoded — see "Row #9 field decode" below.** A **trigger area**, `script.dat` block 4: a box between two row-#6 points when its shape field is 0, otherwise a circle about one point with a radius in tens of units |
| 10 | `DAT_00470660` | 82 (`0x52`) bytes/record | `DAT_00470630` | #9 (8 shorts), `.ENG` text (5 shorts at `0x44`), and a target resolved after row 16 by the action's type (7/8/9/10 → #12/#13/#14/#16) | the mission **action**, `script.dat` block 5. The objective record is row #17 |
| 11 | `DAT_00470662` | 30 (`0x1e`) bytes/record | `DAT_00470634` | #10 (once) + #10 again (10 shorts) | **decoded — see "Row #11 field decode" below.** A mission timer: an action that arms it, a delay, and the actions fired on expiry. DBSIM reads all ten sequence slots, but not all of them are always used |
| 12 | `DAT_00470652` | 144 (`0x90`) bytes/record | `DAT_00470614` | #6, #7, #10 (×2) — sparse in retail (≤2.4% used) but all live at runtime; real payload is a 10-slot weapon fit | **decoded — see "Row #12 field decode" below.** The mission's **mech roster**: one record per HERC it can field, with type, weapon fit and optional placement. A second, distinct 144-byte type from #4; heaviest variant usage of any decoded row (48%) |
| 13 | `DAT_00470654` | 102 (`0x66`) bytes/record | `DAT_00470618` | #6, #7 (both declared, both dead in retail), #10 (×2, only the 2nd slot real) | **decoded — see "Row #13 field decode" below.** The mission's **flyer roster**: one record per flyer or ground vehicle it can field — a 20-short boolean span, the flyer type, the out-of-action report and a constant trailing field (always `100`) |
| 14 | `DAT_0047065c` | 62 (`0x3e`) bytes/record | `DAT_00470628` | #6, #7, #10 (×2) | **decoded — see "Row #14 field decode" below.** The mission's **base roster**: one record per structure it can place, with its type, optional placement, out-of-action report and action links |
| 15 | `DAT_00470658` | 22 (`0x16`) bytes/record | `DAT_00470620` | #6 (rare), #8 (dominant — 94% populated), #10 (rare), plus a **4-way** discriminated ref (0/1/2/3 → #16/#12/#13/#14, resolved in two passes since #16 loads after #15) | **decoded — see "Row #15 field decode" below.** A "typed link" record whose primary payload is a near-always-populated ref into row #8 — confirms it's structurally distinct from #6 (which is a flat position record), not just size-coincidentally 22 bytes |
| 16 | `DAT_0047065a` | 164 (`0xa4`) bytes/record | `DAT_00470624` | #6, #7, #8, #10, a **20-entry** discriminated-ref array (0/1/2 → #12/#13/#14), a 10-entry array into #15 | **decoded — see "Row #16 field decode" below.** The **mission group**, `script.dat` block 11: the roster slots it activates, where they stand, the orders they work through and the action that brings them in |
| 17 | `DAT_0047064a` | 58 (`0x3a`) bytes/record | `DAT_00470608` | #6 (declared, **never used in retail data**), #8, a `.ENG` id (dominant), a 4-way discriminated ref (0/1/2/3 → #16/#12/#13/#14) | **the mission objective — see "Row #17 field decode" below.** Structurally unusual — no leading GUID field at all (this record is never referenced by anything else in the file); the 42-byte tail is a nested pair-count array, the same idiom as row #8's nested waypoint list |

`DAT_00470664` is the count of `.ENG` records the load kept, read by `Msn_LoadEngText` (`0041768c`) between rows 2 and 3; rows 4, 10 and 17 renumber their text refs into that list, which is `data\mission.str` ([The `.ENG` string table](#the-eng-string-table)).

### Verification note

61 of 62 real `.MSN` files land on EOF with zero bytes of slack. **One outlier: `DEMO2.MSN`** undershoots by 42 bytes at row #17's tail, likely a stale/leftover developer test file with a genuinely truncated tail rather than evidence of a table error.

### Relationship to `data\script.dat`

`data\script.dat` is a GUID-filtered, field-subset re-export of these same `.msn` row arrays, written by `WriteScriptDatFile` (`0041ac54`) right after `.msn` parsing finishes. It is read independently by both DBSIM (the real gameplay simulator) and VSHELL's briefing map, `ShellMap`. For full verified block-by-block mapping and field-level detail on what each reader keeps vs. discards from each row, see [`script-dat.md`](script-dat.md) — treat that doc as authoritative.

## Row #6 field decode — the point record (`DAT_0047064e`, 22 bytes/record)

Central spatial-reference table: `{GUID, X, Y, Z}` points (2,661 real instances across 62 files).

| offset | field | notes |
|---|---|---|
| `0x00` | GUID | identity key |
| `0x02` | condition ref | **dead** — always `-1` |
| `0x04` | variant key | **dead** — always `-1` |
| `0x06` | ? | **dead** — always `-1` |
| `0x08` | sum flag | **dead** — always `0` |
| `0x0A` | X (int32) | range 77,591–3,825,420 |
| `0x0E` | Y (int32) | range 17,968–3,800,672 |
| `0x12` | Z (int32) | range 0–35,400 (altitude) |


## Row #8 field decode — the waypoint group (`DAT_00470656`, 10 fixed bytes + nested-count×2 bytes/record)

Ordered waypoint list, heavily referenced by row #15 (470 real instances across 62 files).

| offset | field | notes |
|---|---|---|
| `0x00` | GUID | identity; 47/470 are `-1` (conditional records) |
| `0x02` | condition ref | 10% real usage; correlates exactly with GUID `-1` |
| `0x04` | variant key | 2% real; a variant copies the waypoint list |
| `0x06` | ? | **dead** — always `-1` |
| `0x08` | nested count | 0–9 entries; mean 3.2 |
| nested | ref→row #6 | 2 bytes/entry; resolved to row #6 GUIDs |


Spatial validation: consecutive waypoints have median distance 191,245 units, against 323,406 for every pair of row-6 points in the same mission, confirming authored path structure. 109 of the 446 records with two or more waypoints (24%) form closed loops (first = last waypoint).

## Row #15 field decode — the order record (`DAT_00470658`, 22 bytes/record)

**A mission-group order.** Row #16 names up to ten of these and works through them in slot order; what each does is [`../simulation/ai-goals.md`](../simulation/ai-goals.md). 637 real instances across 62 files.

| offset | field | notes |
|---|---|---|
| `0x00` | GUID | identity key |
| `0x02` | condition ref | 5/637 real (0.8%); **compound pair** with `0x06` |
| `0x04` | variant key | **dead** — always `-1` |
| `0x06` | condition operand | correlates 100% with real `0x02`; values {1, -99} |
| `0x08` | **the verb** | range 0–6, the whole span DBSIM switches on: search/destroy, ram, guard, patrol, sleep, travel, follow |
| `0x0A` | formation | range 0–3. DBSIM copies it into the order record, and no DBSIM reader has been found ([`../simulation/ai-goals.md`](../simulation/ai-goals.md#open)); VSHELL's briefing map stands the squad in it ([`../shell/mission-map.md`](../shell/mission-map.md)) |
| `0x0C` | ref→row #6 | 7% real — a point; resolved into the order record, and no reader has been found ([`../simulation/ai-goals.md`](../simulation/ai-goals.md#open)) |
| `0x0E` | ref→row #8 | **94% real** — the route. Only the group's first order's is walked; the objective layer reads every slot's ([`../simulation/ai-goals.md`](../simulation/ai-goals.md#what-else-reads-an-order)) |
| `0x10` | discriminator | what `0x12` names: `-1` nothing, 0 a group (row #16), 1 a HERC (#12), 3 a structure (#14). `2` (a flyer, #13) never occurs |
| `0x12` | discriminated ref | **the order's subject** — what to hunt, guard or follow |
| `0x14` | ref→row #10 | 2% real — an action that, when it fires, moves the group to its next order |


## Row #9 field decode — the trigger area record (`DAT_0047065e`, 12 bytes/record)

A trigger area, `script.dat` block 4: what DBSIM tests a position against is [`../simulation/mission-deployment.md`](../simulation/mission-deployment.md#the-areas--block-4-resolved-by-triggerarea_resolve-00423358)'s. 335 real instances; every row-#10 area ref names one of these by GUID.

| offset | field | notes |
|---|---|---|
| `0x00` | GUID | identity key; dedup via GUID match |
| `0x02` | condition ref | **dead** — always `-1` |
| `0x04` | ? | **dead** — always `-1` |
| `0x06` | shape | 0 a box (157 real), 1 a circle (178 real) |
| `0x08` | ref→row #6 | the box's first corner or the circle's centre |
| `0x0A` | ref→row #6 or radius | a box's opposite corner; a circle's radius, which DBSIM multiplies by 10 (stored 100–20000) |


## Row #10 field decode — the action record (`DAT_00470660`, 82 bytes/record)

The mission action — `script.dat` block 5, laid out in [`../simulation/mission-deployment.md`](../simulation/mission-deployment.md). Sparse payload (most 82 bytes are dead). 338 real instances; heavy cross-referencing from rows #12/#13/#14/#16.

| offset | field | notes |
|---|---|---|
| `0x00` | GUID | identity key |
| `0x02` | condition ref | **dead** — always `-1` |
| `0x04` | ? | **dead** — always `-1` |
| `0x06` | type | whose position the trigger areas test, and for 7-10 what `0x50` names. 0/1/2/3/4/7/9 seen (142/170/3/15/4/3/1 of 338); `8`/`10` never occur |
| `0x08` | verb | how a group gated on this action arrives; 0–3 (one record `-1`) |
| `0x0A–0x11` | ref[0..3]→row #9 | the trigger areas; only 4 real slots |
| `0x12–0x19` | ref[4..7]→row #9 | **dead** — always `-1` |
| `0x1A` | pair count | how many of the pairs below are filled, from the front: `1` in 66 records, `0` in the other 272. Not exported to `script.dat` |
| `0x1C–0x43` | 10 (counter ref, operation) pairs | the mission counters the action writes when it activates, exported as `script.dat` block 5's two 10-short spans. 66 of 338 retail records, all in `TRAIN1`–`TRAIN4`, fill the first pair, every one with counter 0 and operation 6; the other 272 are `-1` throughout |
| `0x44–0x45` | text ref | 1 of 338 real; renumbered into `mission.str` like every other text ref |
| `0x46–0x4D` | text refs [1..4] | **dead** — always `-1` |
| `0x4E` | message | the mission message the action posts, **plus one**; 0 for none. `script.dat` block 5 `0x4E` carries it through and DBSIM subtracts the one at load ([`../simulation/mission-deployment.md`](../simulation/mission-deployment.md#the-four-ways-an-action-activates)). 66 real instances: 65 in the four `TRAIN*.MSN` and one in `C1_02.MSN` |
| `0x50` | target | 1% real. The load resolves it only for types 7-10, into rows #12/#13/#14/#16; any other type keeps it as authored |


## Row #3 field decode — the variant value record (`DAT_00470666`, 8 bytes/record)

Condition-gated variant table; same GUID with different conditions/payloads (194 real instances). Referenced by row #4.

| offset | field | notes |
|---|---|---|
| `0x00` | GUID | identity (multiple instances per GUID possible) |
| `0x02` | condition ref | 86 of 194 real (44%), the highest condition usage of any row (row #1 43.4%, row #12 43.1%) |
| `0x04` | ? | compound pair: `-1` or `-99`; `-99` correlates 100% with real `0x02` |
| `0x06` | payload | 54 distinct values; fetched by row #4 |


## Row #7 field decode — the heading record (`DAT_00470650`, 10 bytes/record)

Heading record (degrees → BAM conversion). 105 real instances; simplest record type in file.

| offset | field | notes |
|---|---|---|
| `0x00` | GUID | identity key |
| `0x02` | condition ref | **dead** — always `-1` |
| `0x04` | variant key | **dead** — always `-1` |
| `0x06` | ? | **dead** — always `-1` |
| `0x08` | payload | 0/1/10 (62%/34%/4%); multiplied by 182 → degrees to BAM |


## Row #11 field decode — the timer record (`DAT_00470662`, 30 bytes/record)

A mission timer: an action that arms it, a delay, and the actions fired when the delay runs out. 72 real instances.

| offset | field | notes |
|---|---|---|
| `0x00` | GUID | identity key |
| `0x02` | condition ref | **dead** — always `-1` |
| `0x04` | ? | **dead** — always `-1` |
| `0x06` | ref→row #10 | the action that arms the timer; 82% real. Unset means it runs from mission start |
| `0x08` | delay | DBSIM shifts it left 11 into the simulation's timer unit, so the unit is one second |
| `0x0A–0x1D` | ref[0..9]→row #10 | the actions fired on expiry. **Unused in retail data past slot 0**, but not dead: DBSIM resolves and fires all ten |


## Row #4 field decode — the mission's text package (`DAT_00470668`, 144 bytes/record)

**NOT UnitInfo** — no GUID field, completely refutes that C# type. Mission-level singleton (60 instances across 62 files).

| offset | field | notes |
|---|---|---|
| `0x00` | condition ref | **dead** — always `-1` |
| `0x02–0x14` | **objective lines** → `.ENG` ids (10 slots) | 1–4 real slots; mean 2.3. **The objective list as the player is shown it**, and what `script.dat` block 13 is built from ([`script-dat.md`](script-dat.md)). Always a consecutive run |
| `0x16–0x50` | **briefing lines** → `.ENG` ids (30 slots) | 0–4 real slots; mean 1.8 |
| `0x52–0x8c` | **intelligence lines** → `.ENG` ids (30 slots) | 0–3 real slots; mode 1 |
| `0x8e` | ref→row #3 | 87% real; replaced at load by that record's value, **the briefing movie id** |

All three arrays are ids into the mission's own [`.ENG` table](#the-eng-string-table), renumbered at load into `mission.str` lines. A campaign load hands the first record to `Career_SetBriefing` (`00412ece`), which copies the arrays and the movie id into the save's career block, where the mission tab reads them ([`../shell/campaign-loop.md`](../shell/campaign-loop.md#loading-the-careers-mission)).

## Row #5 — the debrief

64 bytes per record: a condition ref, thirty text refs into the `.ENG` table and a row-3 ref — row 4's shape with one text array where row 4 has three. The mission load skips the row; the campaign debrief reads it.

`Career_Advance` (`00412dc7`) calls `Msn_LoadDebrief` (`0041d2c3`) for the mission at the career position, the one just flown, before it advances the position. That runs a second loader over the file, `Msn_LoadDebriefRows` (`0041ca4e`):

- **row 1** against the flag array as the debrief holds it, with the same survival rules as the main load — including a draw from the shell's generator for each type-1 record that passes its gate;
- **row 2** skipped, so the header is not patched and no flag is cleared;
- the **`.ENG` text**, as the main load reads it, then written out as `data\mission.str`;
- **row 3**, filtered and merged as the main load does;
- **row 4** skipped;
- **row 5**, each record gated on its condition, its thirty text refs renumbered into `mission.str` lines and its row-3 ref replaced by that record's value, as row 4's is.

Records are read into the slot the survivor count names, so the first survivor holds slot 0; with none, slot 0 holds the last record read. `Msn_LoadDebrief` hands slot 0 by value to `Career_SetDebriefLines` (`00413386`), which copies the thirty lines to `0048407c` and the row-3 value, the debrief movie, to `004840ba`. `Career_BuildDebriefText` (`004133d2`) then assembles the mission tab's debrief from those lines ([`../shell/mission-screen.md`](../shell/mission-screen.md#the-summary-text-box)). None of this reaches the save: the career block ([`save-games.md`](save-games.md#career-block--152-bytes)) holds the briefing's arrays and movie id, field by field, and neither `0048407c`'s lines nor `004840ba`'s movie. What lasts is the draws: the next mission's load starts that many generator steps further on.


## Row #13 field decode — the flyer record (`DAT_00470654`, 102 bytes/record)

The flyer roster, `script.dat` block 8; condition and variant are both well used (24%/30% real, second only to row #12's 43%/48%). 124 instances.

| offset | field | notes |
|---|---|---|
| `0x00` | GUID | 76% real; identity key |
| `0x02` | condition ref | 24% real (tier: rows #1/#3/#13) |
| `0x04` | variant key | 30% real |
| `0x06` | ? | **dead** — always `-1` |
| `0x08–0x2F` | flag span (20 shorts) | 100% populated; boolean: 96.5% `0`, 3.5% `1`. Exported as `script.dat` block 8's `0x00`–`0x27`, which DBSIM reads and drops ([below](#what-dbsim-takes-from-a-flyer-record)) |
| `0x30` | ref→row #6 | always `-1` in retail, but **not dead** — DBSIM reads it as this flyer's spawn-position override (see `script-dat.md`) |
| `0x32` | ref→row #7 | same, for heading |
| `0x34` | **flyer type** | 68% real; always `0` when present — an index into `nam\FLYERS.NAM`, whose one flyer with data is `SKIMMER` |
| `0x36` | pair count | how many of the pairs below are filled, from the front: `1` in the one record that fills a slot (`C1_03.MSN`), `0` in the other 123. Not exported to `script.dat`; a variant does not copy it |
| `0x38–0x5E` | 10 (counter ref, operation) pairs | 99.9% `-1`; one retail record uses a slot. The flyer's [out-of-action report](../simulation/mission-deployment.md#the-out-of-action-report), exported as `script.dat` block 8's `0x2e`/`0x42` |
| `0x60` | engaged action, ref→row #10 | **dead** — always `-1`; exported as `script.dat` block 8's `0x56` |
| `0x62` | defeated action, ref→row #10 | **only live ref** — 21% real; block 8's `0x58` |
| `0x64` | constant | always exactly `100`, the shape of the mech and base rows' starting condition; exported as block 8's `0x5a`, which DBSIM reads and drops ([below](#what-dbsim-takes-from-a-flyer-record)) |

### What DBSIM takes from a flyer record

Both of DBSIM's passes read block 8's 92-byte records whole. `DBSim_LoadScriptDat` (`00424308`) keeps the type. `DBSim_SpawnMissionObjects` (`004253d8`) reads each live record into a stack buffer (`00425a21`) and takes `0x28` the position, `0x2a` the heading, `0x2c` the type, the counter spans at `0x2e` and `0x42`, and the two actions at `0x56` and `0x58`; no instruction of the function addresses the buffer's `0x00`–`0x27` or `0x5a`. So the 20-short span reaches no flyer, and where a HERC and a structure take a starting condition from their records, a flyer takes none ([Open](#open) covers a shell-side reader).


## Row #14 field decode — the base roster record (`DAT_0047065c`, 62 bytes/record)

The base roster, `script.dat` block 9. The largest roster (1,949 instances); clear `0x08`/`0x3C` correlation.

| offset | field | notes |
|---|---|---|
| `0x00` | GUID | 99% real |
| `0x02` | condition ref | 30% real (tier: rows #1/#3/#13) |
| `0x04` | variant key | 0.4% real |
| `0x06` | ? | **dead** — always `-1` |
| `0x08` | **base type** | 71% real; range 0–56 (43 values) — an index into `dat\BASES.DAT`'s 65-entry structure table, which names the model and its texture bank |
| `0x0A` | ref→row #6 | 6.4% sparse — this structure's spawn-position override |
| `0x0C` | ref→row #7 | 6.7% sparse — its heading |
| `0x0E` | pair count | how many of the pairs below are filled, from the front; 0/1/2 (64%/33%/3%). Not exported; a variant does not copy it |
| `0x10–0x36` | 10 (counter ref, operation) pairs | 698 of 1,949 records fill the first pair and 58 of those a second; slots 2–9 are always `-1`, and every filled operation is 2 (increment). The structure's [out-of-action report](../simulation/mission-deployment.md#the-out-of-action-report), exported as `script.dat` block 9's `0x06`/`0x1a` |
| `0x38` | engaged action, ref→row #10 | 0.4% real; block 9's `0x2e` |
| `0x3A` | defeated action, ref→row #10 | 0.1% real; block 9's `0x30` |
| `0x3C` | starting condition, per cent | 100%: `100` (71%) or `0` (29%); `100` goes with a real `0x08` in 1,931 of 1,949 records (9 have `100` and no type, 9 a type and `0`); block 9's `0x32`, read by `Base_Construct` ([`../simulation/structure-behaviour.md`](../simulation/structure-behaviour.md#starting-condition)) |


## Row #16 field decode — the group record (`DAT_0047065a`, 164 bytes/record)

The mission group, `script.dat` block 11; how DBSIM places it is [`script-dat.md`](script-dat.md#placement--the-actual-rule)'s. 1,247 instances.

| offset | field | notes |
|---|---|---|
| `0x00` | GUID | 100% real |
| `0x02` | condition ref | 2.5% sparse; **compound pair** with `0x04` |
| `0x04` | condition operand | 1.4% real; always `-99` when populated |
| `0x06` | paints ground | 100% real; 39/61 split. A base group that sets it stands on and paints its formation's terrain tile ([`script-dat.md`](script-dat.md#base-formation-terrain)) |
| `0x08` | ? | `0` in 1,246 of the 1,247 groups. `C2_05`'s group 188, one Cybrid Stingray hunting the Raptor II prototype, holds 25 in v1.0, and v1.10 sets it to 0. Exported as `script.dat` block 11's `+0x02` ([Open](#open)) |
| `0x0A–0x2C` | dead zone (18 shorts) | **always `0`** — padding |
| `0x2E` | discriminator | 89% real; 0/1/2 — selects which row the `0x38` array's entries point at (rows #12/#13/#14) |
| `0x30` | **formation id** | 85% real; range 0–16 — indexes the formation-offset table that spreads a group's members around its point (see `script-dat.md`'s placement section) |
| `0x32` | ref→row #6 | 37% real — **the group's spawn point** |
| `0x34` | ref→row #7 | 45% real — **the group's heading** |
| `0x36` | ref→row #8 | 43% real — the group's patrol route |
| `0x38–0x5E` | 20 member refs | the roster slots the group activates, into the row `0x2E` names; slot 0: 89% real → slot 8: 4.7% → slot 14: 0.6%; slots 15–19 never used |
| `0x60–0x72` | 10-entry ref→row #15 | **the group's orders**, worked through in slot order — slot 0: 47% real → slot 3+: never used. Slot 0's is also where the group's route and its spawn-point fallback come from |
| `0x74` | side | 0 human, 1 Cybrid; `-1` in 11% |
| `0x76` | deployment action, ref→row #10 | 31% real. While set, the group is not in the mission until that action fires ([`../simulation/mission-deployment.md`](../simulation/mission-deployment.md#the-deployment-gate--group0x14)) |
| `0x78` | pair count | 100% real; 0/1/2 (97%/2.8%/0.5%) — how many of the pairs below are filled. Not exported to `script.dat` |
| `0x7A–0xA0` | 10 (counter ref, operation) pairs | refs 20–650, operations {2, 23}; slots 2–9 always `-1`. The group's [out-of-action report](../simulation/mission-deployment.md#the-out-of-action-report), exported as `script.dat` block 11's `0x72`/`0x86` |
| `0xA2` | map shown | 6% sparse; 0/1. For a base group, what VSHELL's briefing map writes over each member's first out-of-action operation in its own copy of block 9: `-1` shows friendly members and hides hostile ones, 0 hides both, 1 shows both ([`../shell/mission-map.md`](../shell/mission-map.md#what-it-reads)) |


## Row #12 field decode — the mech roster record (`DAT_00470652`, 144 bytes/record)

The HERC roster, `script.dat` block 7; highest variant usage (48%). Three-way identity split: a GUID and a variant key (48%), a GUID alone (11%), or a conditional variant with no GUID (41%). 1,683 instances.

| offset | field | notes |
|---|---|---|
| `0x00` | GUID | 59% real (or `-1` for condition-only) |
| `0x02` | condition ref | 43% real (tier: rows #1/#3/#13/#14) |
| `0x04` | variant key | 48% real (highest in file) |
| `0x06` | condition operand | 3.9% real; compound pair with `0x02` |
| `0x08` | **AI radar setting** | 100% real; 0/1. DBSIM copies it to `mech+0x97`, which is the standing PASSIVE/ACTIVE the machine walks its route on — see [`../simulation/ai-weapons.md`](../simulation/ai-weapons.md) |
| `0x0A` | **AI cruise speed** | 100% real; `0` in 91% of records, which means "use the `0xaa` default". Copied to `mech+0x252`, the speed `Ai_DriveToPoint` walks at |
| `0x0C–0x2E` | dead zone (18 shorts) | **always `0`** — padding |
| `0x30` | **mech type** | 47% real; range 0–20 — an index into `nam\MECHS.NAM`; DBSIM asserts "Invalid mech type" on it |
| `0x32–0x44` | **weapon fit**, 10 slots | **real workhorse**: slot 0: 46% real → slot 9: 0.1%; bursty population. Resolved via `script.dat`: DBSIM hands this array straight to `Mech_ConfigureLoadout`, the same call the player's own fit from `player.mec` goes through |
| `0x46` | ref→row #6 | 0.1% populated in `.msn` data, but **not dead** — this is the spawn-position override DBSIM reads per mech (see `script-dat.md`); unset means "use the group's point" |
| `0x48` | ref→row #7 | the same override, for heading; set in none of the 1,683 retail records |
| `0x4A` | pair count | how many of the pairs below are filled, from the front; 0–4 (84% `0`). Not exported; a variant does not copy it |
| `0x4C–0x72` | 10 (counter ref, operation) pairs | sparse; slot 0 15.9% → slot 3 0.5%; slots 4–9 never used. The machine's [out-of-action report](../simulation/mission-deployment.md#the-out-of-action-report), exported as `script.dat` block 7's `0x42`/`0x56` |
| `0x74–0x87` | **ammunition type**, 10 slots | 100% real; values 0, 1, 2 and 5, the filler `5` on every slot that is not a launcher. The second array `Mech_ConfigureLoadout` takes alongside the weapon fit — [`script-dat.md`](script-dat.md)'s block 7 `0x6a` |
| `0x88` | constant | always `2` |
| `0x8A` | engaged action, ref→row #10 | 0.7% real; block 7's `0x80` |
| `0x8C` | defeated action, ref→row #10 | 2.4% real; block 7's `0x82` |
| `0x8E` | **starting condition**, per cent | 100% real; `100` (98.5%) or `50` (1.5%); block 7's `0x84` |

**Model:** A roster record with high variant/condition usage. The payload is the 10-slot **weapon fit** at `0x32`, plus the per-mech spawn-position and heading overrides at `0x46`/`0x48` — sparsely populated but live, and the pair of AI settings at `0x08`/`0x0A` ([`ai-navigation.md`](../simulation/ai-navigation.md)). Three identity patterns: a HERC whose fields come from a random variant (GUID and key), a HERC authored whole (GUID only), or a variant itself (no GUID, a condition naming its row-1 child).

## Row #17 field decode — the objective record (`DAT_0047064a`, 58 bytes/record)

**A mission objective.** No GUID (unreferenced row). Nested pair-count array in tail. 127 instances (61 files; `DEMO2.MSN` truncated here). DBSIM reads it as `script.dat` block 12 minus the condition ref and the pair count; what each field means at runtime is [`../simulation/mission-objectives.md`](../simulation/mission-objectives.md).

| offset | field | notes |
|---|---|---|
| `0x00` | condition ref | 2% real (values 79/80 only). Not exported to `script.dat` |
| `0x02` | **required flag** | 100% real; binary 72%/28%. `1` = must be satisfied; anything else = a failure condition |
| `0x04` | **condition code** | 100% real; values 0–4, 6 and 7 (5 and 8–10 never occur); mode `1` (53%). The simulator has cases for 0–4 and 6–10 — [`mission-objectives.md`](../simulation/mission-objectives.md#what-each-condition-asks) |
| `0x06` | **subject kind** | 0/1/3 (72%/17%/10%); `2` (flyer) never occurs |
| `0x08` | subject ref | → rows #16/#12/#13/#14 per `0x06` — group / mech / flyer / base |
| `0x0A` | ref→row #6 | **dead** — `-1` in all 127 records |
| `0x0C` | ref→row #8 | 23% real — the waypoint group condition 0 asks about |
| `0x0E` | **failure text** → `.ENG` id | **93% dominant**; first of three consecutive lines |
| `0x10–0x39` (42 bytes) | nested pair-count array | `0x10`: count (0/1/2), not exported; pairs at `0x12`/`0x14`, `0x16`/`0x18` |

**Nested pairs** are the mission counters the objective writes: first element the counter index (20–360, 17 distinct values), second the operation, `{6, 7}` only — increment and decrement out of the objective layer's four codes. Of the declared 10 pairs, retail fills at most 2.

## The `.ENG` string table

Every `.MSN` has a same-named `.ENG` beside it in `ZONES.VOL`, and it holds the mission's text: the objective lines row #4 lists, the failure paragraph row #17 names, and the briefing and debrief. Records are keyed by **id, not by position**, the same GUID-plus-condition idiom the `.MSN` rows use. After the 9-byte VOL entry prefix:

```
int16 count
count x {
    int16 id
    int16 conditionRef      -- a row-1 GUID, or -1
    int16 word              -- -99 beside a condition, -1 otherwise
    int16 length            -- includes the NUL terminator
    byte[length] text
}
```

**Verified byte-exact**: all 62 files consume to their declared content length with zero slack, 1,111 records, ids 42-253. 468 records in 45 files carry a condition.

A line ending `" \n"` is authored to break there; the reader that copies these into a mission strips one trailing newline.

`Msn_LoadEngText` (`0041768c`) loads it between rows 2 and 3, from the mission's path with everything from its first `.` replaced by the language's extension — `.eng`, or `.fre`/`.ger` by `Shell_Language` (`0048227a`), which a command-line switch sets. v1.0 ships only `.eng`; v1.10 adds the other two for every mission but `DEMO2` ([`../retail-builds.md`](../retail-builds.md#how-a-language-is-chosen)). A record whose condition fails is skipped, and one whose id is already loaded replaces that entry's text. `MissionStr_Write` (`004179f0`) writes every record that stays into `data\mission.str` as an ordinary [`.STR`](str-strings.md) of one group — the length of the rest, the count, then each line's length with its NUL, the line, and an attribute count of 0 — and rows #4, #10 and #17 have their ids renumbered to match, which is why `script.dat`'s refs are small where these are not.

A conditioned record is how a mission varies a line: an unconditioned record of the id comes first and later records of the same id replace it, so the line reads as the last record whose condition held.

### The debrief's loss line

The 30 missions of chapters 1 to 3 and `DEMO_01` and `DEMO_02` give one line of their row-5 debrief the same eleven records: a blank, then ten replacements keyed on three [campaign flags](../shell/campaign-loop.md#the-campaign-flag-array-is-the-msn-condition-store) — 8, the machines the debrief scrapped; 9, the pilots it counted lost; 10, the squadmates the player put out of the fight ([`../simulation/mission-deployment.md`](../simulation/mission-deployment.md#the-mission-counters--dat_004a9ef4)).

| order | condition | line |
|---|---|---|
| 1-3 | flag 8 `== 1`, `== 2`, `> 2` | one Herc, two Hercs, "a bunch of Hercs" lost |
| 4-6 | flag 9 `== 1`, `== 2`, `> 2` | one squadmate, two, the whole squad lost |
| 7-8 | flag 10 `== 1`, then a type-1 draw below 11: 0-9, 10 | the player caused a pilot's death; the rarer draw is the harsher line |
| 9-10 | flag 10 `== 2`, twice | "You killed TWO squadmates!", then a line opening with a space, " Good luck in the Brig, soldier." |

All ten share the id, so the debrief carries **one** of them: a squadmate killed by the player outranks a squadmate lost, which outranks a Herc lost. Record 10 replaces record 9 rather than following it, so two squadmates killed by the player read only the Brig line. Three or more match none of records 7-10, so the line reports the last Herc-lost or squadmate-lost record that holds, and stays blank when none does. Chapters 4 and 5, `DEMO`, `DEMO2` and the training missions carry no loss line, and the two demo missions run in training mode, whose debrief never reloads the mission's text ([`../shell/campaign-loop.md`](../shell/campaign-loop.md#the-debrief--game_processmissionresults-0040eae7)).


## Recurring patterns

- **Recurring pattern: most declared array/discriminator capacity goes unused in retail.**

- **Recurring pattern: `0x02`/`0x0X` "compound condition" pairs** — second field is real only when `0x02` is, drawn from a narrow set including sentinel `-99`. Confirmed in rows #3/#12/#15/#16 and the `.ENG` records.

- **Recurring pattern: a pair count in front of the counter pairs** — rows #10 (`0x1A`), #12 (`0x4A`), #13 (`0x36`), #14 (`0x0E`) and #16 (`0x78`) hold the number of filled (counter ref, operation) pairs, filled from slot 0 without gaps. It equals the filled count in every retail record of the five rows; `WriteScriptDatFile` exports all ten pairs whatever the count and does not export the count. Row #17's `0x10` is the same idiom for its nested pairs, and equals its filled count in all 127 records.

- **Recurring pattern: trailing scalar fields that are almost always a specific constant** — row #13's `0x64` (always `100`), row #14's `0x3C` (`100`/`0`), row #12's `0x8E` (`100` or `50`).

- **Note:** `DEMO2.MSN` undershoots by 42 bytes at row #17; treat as a known outlier rather than a table error.

## Rejected readings

| Reading | Why it is wrong |
|---|---|
| `0x04` is a parent index: the record copies an earlier record of its row | It is a variant key into row 1. The copy's source is found by the condition field of the candidates, never by index or GUID, and which candidate it is depends on a draw — see [Variants](#variants) |
| Row 1's `0x00` is authoring bookkeeping, never read | It is the GUID every condition ref in the file names; `Msn_ConditionFilterGate` searches for it |
| Row #9's type 1 is a reward: a point and a salvage value, the literal running 100–20000 in round numbers | It is a circle trigger area: DBSIM's `TriggerArea_Resolve` multiplies the literal by 10 into a radius about the point, and `TriggerArea_ContainsPoint` tests a position against it |

## Open

- **Deferred:** no reset of the row-2 clear list (`DAT_0048545a`) found by `es2_xref.py --binary VSHELL`: its three references are `Msn_ApplyHeaderPatch`'s write and `Msn_ClearPatchedFlags`'s two reads, and no other address from `00485440` to `00485495` but the header words is referenced. On retail data the answer changes no condition a load tests ([The header patch](#the-header-patch--row-2)).
- **Open:** what row #16's `0x08` does. No reader of it found. In VSHELL, `es2_xref.py` finds the row's storage (`0047065a`) referenced only by `MsnGen_ParseMsnFile`, `WriteScriptDatFile` and `MsnGen_FreeRows`, and the briefing map's block-11 array (`map+0x6e`, every load `es2_fieldscan.py` finds) is read at `+0x28`, `+0x2c`, `+0x2e`, `+0x30`, `+0x32`-`+0x59`, `+0x5a`, `+0x6e` and `+0x9a`. In DBSIM, `DBSim_LoadScriptDat` reads the record whole and tests `+0x28` and the member refs; `DBSim_SpawnMissionObjects` reads it whole and hands it to `DBSim_BuildGroupRecord`, and neither reads `+0x02`.
- **Deferred:** a VSHELL reader of row #13's 20-short flag span at `0x08–0x2F` or its constant at `0x64`. The functions found referencing the row's storage (`00470618`) by a disassembly search are the parser and its merge, variant and ref helpers, `MsnGen_FreeRows` and `WriteScriptDatFile`, where the same search over row 12's storage also finds `MsnGen_BuildPlayerHerc`; the briefing's map skips block 8 ([`script-dat.md`](script-dat.md#the-13-block-structure)). DBSIM's side is [What DBSIM takes from a flyer record](#what-dbsim-takes-from-a-flyer-record).
