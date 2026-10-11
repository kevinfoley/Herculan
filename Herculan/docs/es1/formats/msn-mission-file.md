# GO.EXE — .MSN mission files

ES1's missions are the `.MSN` files in `ZONES.VOL`'s `MSN\` folder. The shell, `GO.EXE`, loads one, keeps the records the campaign's flags allow, and hands what is left to the simulator as `data\script.dat`, as ES2's shell does ([ES2's `.MSN`](../../retail/formats/msn-mission-file.md)). An ES1 mission holds eleven of ES2's seventeen rows, in ES2's order, and five of them have shorter records. It carries no text: no `.ENG` file is read, and ES2's rows of briefing and debrief text, its timers, its orders and its objectives are absent. Addresses are `ES1GO` image addresses unless `ES1DBSIM` is named ([the X-32 executable](x32-executable.md#the-image)); ES2's are VSHELL's.

## Loading a mission

`MsnGen_LoadMission` (`00035873`) takes the mission's path; `es2_xref.py` finds its callers at `00016e93` and `00036807`, and the path is built in `000aad14` by four `sprintf`s of `msn\%s` (`00015a13`, `00036649` twice, and `000366c1`). In order, it:

1. reads the entry whole into a buffer and reads it through a memory stream ([streams](streams.md#goexe));
2. opens `data\script.dat` as a `FileRWStream`, asserting at severity 5 (`mg.cpp` line `0x4c5`) when it cannot. The file names are entries of the text table at `0009afb8`, which `0002094d` loads from `cfg\filename.bin`: `Text_GetString` (`00018717`) gives entry 3, `data\script.dat`; 5, `data\mission.var`; and 10, `car\sm_play.mec`;
3. reads the revision word. When it is not 5 the load formats `ERROR: Script must be compiled with MC` and the number into a stack buffer that nothing reads, and goes on;
4. seeds the flags ([The flags](#the-flags));
5. reads the eleven rows with `MsnGen_ParseMsnFile` (`0003276c`), which always returns 1;
6. copies header words 3 and 4 to `000aad5a` and `000aad5c`, and overrides the header words the mode sets ([Header patches](#header-patches));
7. writes the 200 bytes of flags to `data\mission.var` through a `FileWStream` (assert at line `0x505`), and the mission to `data\script.dat` with `WriteScriptDatFile` (`0003445b`), laid out in [`script-dat.md`](script-dat.md);
8. in mode 0 builds a squad from the first group: it passes `car\sm_init.dat` to `00028c25`, fills a `0x25`-byte record at `0009b028` for each member ref of the first group up to the first `-1`, taking the member's mech record through `MsnGen_HercArrayIndex` (`0003581a`), and passes `car\sm_play.mec` to `0002909c` ([Open](#open));
9. frees the arrays of conditions, points, headings, waypoint groups (with their waypoint lists), mechs, groups and bases. It does not free the trigger areas, actions or flyers.

The mode is the word `000aad56`. In steps 4 and 6, mode 1 takes its values from the `0x1f7`-byte record at `0009b23b + 0x1f7 × [0009b21f]` and any other mode from the words at `000aacbc`–`000aacc4`; step 8 runs only in mode 0.

### The flags

The conditions compare against 100 `int16` flags at `000ab244`, which the load sets before it reads the rows:

| Flag | Mode 1 | Otherwise |
|---|---|---|
| all | kept as they are | 0 |
| 1 | the record's word at `+6` | 99 |
| 2 | the record's word at `+8` | 0 |
| 3 | `000aacc4` | `000aacc4` |
| 4, 5, 6 | each a draw below 12 | each a draw below 12 |

The draws are `Math_RandomBelow` (`00038774`) on the shell's generator at `0009aec0`, the generator the conditions' own draws and the variant picks use. No row writes a flag: the header patch has no flag list ([Header patches](#header-patches)), so `data\mission.var` is the array as seeded.

## The layout

All values are little-endian `int16` but the points' coordinates. The file is the revision word, 5, and then the eleven rows in this order, each an `int16` count and that many records:

| # | ES2's row | Record | Bytes | Count, array |
|---|---|---|---|---|
| 1 | 1, conditions | [as ES2's](#conditions) | 14 | `00080f4e`, `00080f24` |
| 2 | 2, header patches | [shorter](#header-patches) | 22 | `00080f4c`, read into `000ab22e` |
| 3 | 6, points | as ES2's | 22 | `00080f50`, `00080f28` |
| 4 | 7, headings | as ES2's | 10 | `00080f52`, `00080f2c` |
| 5 | 8, waypoint groups | as ES2's | 10 + 2 per waypoint | `00080f58`, `00080f38` |
| 6 | 9, trigger areas | as ES2's | 12 | `00080f5e`, `00080f44` |
| 7 | 10, actions | [shorter](#actions) | 30 | `00080f60`, `00080f48` |
| 8 | 12, mechs | [shorter](#mechs) | 106 | `00080f54`, `00080f30` |
| 9 | 13, flyers | [shorter](#flyers) | 64 | `00080f56`, `00080f34` |
| 10 | 14, bases | [shorter](#bases) | 24 | `00080f5c`, `00080f40` |
| 11 | 16, groups | [shorter](#groups) | 88 | `00080f5a`, `00080f3c` |

Nothing follows the groups. ES2's `.ENG` text and its rows 3, 4, 5, 11, 15 and 17 have no place in the file.

Rows 3 to 6 are ES2's rows 6 to 9 field for field: the [point](../../retail/formats/msn-mission-file.md#row-6-field-decode--the-point-record-dat_0047064e-22-bytesrecord) with its variant key and sum flag and its `int32` X, Y and Z at `0x0a`, `0x0e` and `0x12`; the [heading](../../retail/formats/msn-mission-file.md#row-7-field-decode--the-heading-record-dat_00470650-10-bytesrecord), its value at `0x08`; the [waypoint group](../../retail/formats/msn-mission-file.md#row-8-field-decode--the-waypoint-group-dat_00470656-10-fixed-bytes--nested-count2-bytesrecord), five words, the fifth a count of the point refs that follow; and the [trigger area](../../retail/formats/msn-mission-file.md#row-9-field-decode--the-trigger-area-record-dat_0047065e-12-bytesrecord), whose `0x08` is a point ref and whose `0x0a` is a second point ref when its shape `0x06` is 0. The loader reads a waypoint group's five words one at a time and resolves each waypoint as it reads it.

In the tables below, *exported* means `WriteScriptDatFile` copies the field to `data\script.dat` without the load reading it; what the simulator makes of such a field is [Open](#open). Rows 3 to 11 are exported, each without its records whose GUID is `-1`.

### Conditions

ES2's record and its four types ([the conditions](../../retail/formats/msn-mission-file.md#the-conditions--row-1)), filtered as ES2's are. A type-0 record's operator at `0x08` is one of four codes, where ES2's are six from `0x119`:

| Code | Holds when the flag is |
|---|---|
| `0x114` | equal to the operand |
| `0x115` | not equal to it |
| `0x116` | less than it |
| `0x117` | greater than it |

Any other code fails. The flag index at `0x06` indexes the 100 flags at `000ab244` with no bounds test.

### Header patches

22 bytes: a condition at `0x00` and ten header words at `0x02`–`0x14`, without ES2's thirty flag indices. Once the conditions are read the load sets the header words at `000ab21a`–`000ab22c` to 1, 0, 0, …, and `Msn_ApplyHeaderPatch` (`00031c84`) copies, for each patch whose condition passes, its first word when it is not 1 and each other word when it is not 0, as ES2's does ([the header patch](../../retail/formats/msn-mission-file.md#the-header-patch--row-2)). The patch is read into one buffer and not kept.

After the rows the mode overrides some of the words:

| Word | Mode 1 | Otherwise |
|---|---|---|
| 0 | the word of the table at `00080f14` (1, 2, 2, 0, 0, 3, 3, 3) indexed by the record's word at `+6`, flag 1's | a draw below 4 |
| 5, 6, 7 | 0, 0, 2 | `000aacbc`, `000aacbe`, `000aacc0` |
| 8, 9 | the patch's | `000aacc2`, `000aacc4` |

`WriteScriptDatFile` writes the mode in place of word 2. So of a mission's own patches, words 1, 3 and 4 reach the simulator in either mode, and words 8 and 9 in mode 1. What the words mean is [Open](#open).

### Actions

30 bytes, where ES2's are 82:

| Offset | Field |
|---|---|
| `0x00` | GUID |
| `0x02` | condition |
| `0x04` | not read by the load, not exported |
| `0x06` | type, exported |
| `0x08` | verb, exported |
| `0x0a`–`0x19` | eight trigger-area refs (row 6), resolved |
| `0x1a`, `0x1c` | two words, exported, unset `-1` |

ES2's pair count, its other nine (counter, operation) pairs, text refs, message and target have no place. In `B0_C2_60.MSN` four actions hold 37–40 at `0x1a` and 6 at `0x1c` ([Open](#open)).

### Mechs

106 bytes, where ES2's are 144. To `0x49` the record is [ES2's](../../retail/formats/msn-mission-file.md#row-12-field-decode--the-mech-roster-record-dat_00470652-144-bytesrecord):

| Offset | Field |
|---|---|
| `0x00` | GUID |
| `0x02` | condition |
| `0x04` | variant key |
| `0x06` | not read by the load, not exported |
| `0x08`–`0x2f` | 20 words, exported, unset 0 |
| `0x30` | type: the word the simulator's first pass keeps for the slot ([`script-dat.md`](script-dat.md#reading)); the squad builder looks up a slot count for it in the table at `000803c8` |
| `0x32`–`0x45` | ten words, exported, unset `-1`; the squad builder copies the first *n*, *n* the type's count |
| `0x46` | point ref (row 3), resolved |
| `0x48` | heading ref (row 4), resolved |
| `0x4a`, `0x4c` | two words, exported, unset `-1` |
| `0x4e`–`0x61` | ten words, exported, unset 5; the squad builder copies the first *n* beside `0x32`'s |
| `0x62` | a word, exported, unset 2; the squad builder copies it |
| `0x64` | action ref (row 7), resolved |
| `0x66` | action ref (row 7), resolved |
| `0x68` | a word, exported, unset 100 |

ES2's pair count and nine of its ten (counter, operation) pairs have no place: `0x4a`/`0x4c` stands where ES2's first pair would stand without the count. From `0x4e` on, the fields stand in the order of ES2's ammunition types, constant 2, engaged and defeated actions and starting condition, with their unset values ([Open](#open)).

### Flyers

64 bytes, where ES2's are 102:

| Offset | Field |
|---|---|
| `0x00`–`0x04` | GUID, condition, variant key |
| `0x06` | not read by the load, not exported |
| `0x08`–`0x2f` | 20 words, exported, unset 0 |
| `0x30` | point ref (row 3), resolved |
| `0x32` | heading ref (row 4), resolved |
| `0x34` | type, kept by the simulator's first pass |
| `0x36`, `0x38` | two words, exported, unset `-1` |
| `0x3a`, `0x3c` | action refs (row 7), resolved |
| `0x3e` | a word, exported, unset 100 |

### Bases

24 bytes, where ES2's are 62:

| Offset | Field |
|---|---|
| `0x00`–`0x04` | GUID, condition, variant key |
| `0x06` | not read by the load, not exported |
| `0x08` | type, kept by the simulator's first pass |
| `0x0a` | point ref (row 3), resolved |
| `0x0c` | heading ref (row 4), resolved |
| `0x0e`, `0x10` | two words, exported, unset `-1` |
| `0x12`, `0x14` | action refs (row 7), resolved |
| `0x16` | a word, exported, unset 100 |

In `B0_C2_60.MSN` four bases hold 41 at `0x0e` and 2 at `0x10`, and `0x16` holds 0, 5, 20, 50 or 100 across the two files walked ([Open](#open)).

### Groups

88 bytes, where ES2's are 164:

| Offset | Field |
|---|---|
| `0x00` | GUID |
| `0x02` | condition |
| `0x04` | not read by the load, not exported |
| `0x06`–`0x2d` | 20 words, exported, unset 0 |
| `0x2e` | member kind: 0 mechs, 1 flyers, 2 bases |
| `0x30`, `0x32` | words, exported, unset `-1` |
| `0x34` | point ref (row 3), resolved |
| `0x36` | heading ref (row 4), resolved |
| `0x38` | waypoint group ref (row 5), resolved |
| `0x3a`–`0x49` | eight member refs into the row `0x2e` names, resolved |
| `0x4a` | a word, exported, unset `-1`; for a base member the simulator's first pass keeps it beside the base's type |
| `0x4c` | the row `0x4e` names: 0 groups, 1 mechs, 2 flyers, 3 bases |
| `0x4e` | a ref into that row, resolved |
| `0x50` | action ref (row 7), resolved |
| `0x52`, `0x54`, `0x56` | words, exported, unset `-1` |

A kind outside those listed leaves its refs as authored. ES2's group names up to ten order records (ES2's row 15), each with a ref of this kind-and-index shape (its subject) and an action ref; an ES1 group holds one of each itself, and has eight member refs where ES2's has twenty ([Open](#open)). The first group stands for the player's squad, as ES2's does: the mode-0 load builds a squad from its members, and the simulator's first pass skips it when it marks members live and marks `data\player.mec`'s machines live instead ([`script-dat.md`](script-dat.md#reading)).

## Filtering, variants and repeats

The load treats each record as ES2's does ([the parser](../../retail/formats/msn-mission-file.md#msngen_parsemsnfile-00417b67--the-raw-msn-parser)): a record whose condition fails `Msn_ConditionFilterGate` (`00032711`) is dropped, the survivors are packed from slot 0, and each ref is resolved in place to the index `data\script.dat` gives its target, the number of surviving records before it whose GUID is not `-1`.

**Variants.** Points, headings, waypoint groups, mechs, flyers and bases carry a variant key at `0x04`. A record whose key is not `-1` takes the record `Msn_PickVariant` (`000318d5`) picks, as ES2's does ([variants](../../retail/formats/msn-mission-file.md#variants)), and copies everything after its four leading words from it: X, Y and Z; the heading; the waypoint list; a mech's `0x08`–`0x69`; a flyer's `0x08`–`0x3f`; a base's `0x08`–`0x17`. The copied refs are already resolved. A record whose key is `-1` resolves its own refs.

**Groups resolve their `0x4e` ref as they are read.** When `0x4c` names the group row, the load searches it up to the count the file declares, slots not yet read included, where ES2 resolves an order's group subject after the last group is read.

**Repeated GUIDs.** A record whose GUID a survivor already has replaces it for points, headings, waypoint groups and trigger areas, and overlays it for actions, mechs, flyers, bases and groups, copying each field that is not its unset value (the tables above), as ES2's rows do ([repeated GUIDs](../../retail/formats/msn-mission-file.md#repeated-guids)). The earlier record's slot is found differently by row. Mechs, flyers and bases use the record's own slot, as ES2's rows all do. The other six use the number of surviving records before it whose GUID is not `-1`, so when a survivor with GUID `-1` precedes it, the replacement or overlay lands on an earlier slot than the record with the GUID.

## A file in another layout

Of the 77 files, 75 follow the layout above and end on their last group's last byte, `SM_SCT50.MSN` (56,392 bytes) and `B0_C2_60.MSN` (41,736) among them. Two do not, `B0_C1_20.MSN` (6,402 bytes) and `B0_C1_30.MSN` (2,694): read by the layout above, each has no conditions and then a header-patch count, 1,120 and 1,130, far more records than the file holds, and that is what `GO.EXE`'s loader would take from each.

`B0_C1_30.MSN`'s bytes do walk to their end as follows, a reading of the file and not of any code: the revision 5; ten words, 0, 1130, 1, 2, 1 and five 0s, without a condition; the conditions, 5 records; then rows 3 to 11 as above, except that its 8 mech records are 88 bytes. The first of them reads as above to `0x49`, then holds `-1`, `-1`, 4, 2, `-1`, `-1`, 100 at `0x4a`–`0x56`.

## Rejected readings

| Reading | Why it is wrong |
|---|---|
| An ES1 `.MSN` is ES2's revision-5 layout, since it opens with the word 5 | ES1's loader checks for 5 too and reads eleven rows; ES2's reader runs off the end of most ES1 files |

## Open

- **Open:** which code reads `B0_C1_30.MSN`'s layout, whether any campaign position or list names that file or `B0_C1_20.MSN`, and what its ten leading words and its mech records' words from `0x4a` hold.
- **Open:** whether `B0_C1_20.MSN` walks to its end by `B0_C1_30.MSN`'s reading. Its first two words after the revision are 0 and 1120, as `B0_C1_30.MSN`'s are 0 and 1130.
- **Open:** whether the word pairs at action `0x1a`/`0x1c`, mech `0x4a`/`0x4c`, flyer `0x36`/`0x38` and base `0x0e`/`0x10` are each one (counter ref, operation) pair of ES2's out-of-action report and action write ([out-of-action report](../../retail/simulation/mission-deployment.md#the-out-of-action-report)), and whether mech `0x4e`–`0x61`, `0x62`, `0x64`, `0x66` and `0x68`, flyer `0x3a`, `0x3c` and `0x3e`, and base `0x12`, `0x14` and `0x16` are ES2's ammunition types, constant, engaged and defeated actions and starting condition. They stand where those stand in ES2's records less the pair counts and the other nine pairs; the simulator's readers of them are not read.
- **Open:** what the exported fields mean that no code read so far uses: the 20 words at mech and flyer `0x08` and group `0x06`, an action's type and verb, the group words `0x30`, `0x32`, `0x4a` and `0x52`–`0x56`, and the ten header words.
- **Open:** whether a group's `0x4c`/`0x4e` ref and `0x50` action are an order of ES2's kind, its subject and the action that ends it, and so whether `0x30`, `0x32` and `0x4a` hold an order's verb and formation and the group's side. In `B0_C2_60.MSN` the first group, the squad, holds 0 at `0x4a` and the groups that name it or one of its mechs at `0x4e` hold 1.
- **Open:** the squad the mode-0 load builds: what `00028c25` does with `car\sm_init.dat`, the record at `0009b028` and the functions that fill it (`000158ff`, `000151c8`, `00015402`, `00022f5e`, `00024c3c`, `00029d0f`, `00015ad4`), what `0002909c` writes to `car\sm_play.mec`, and what the table at `000803c8` counts.
- **Open:** what the modes are; what the `0x1f7`-byte records from `0009b23b` (indexed by `0009b21f`) that mode 1 seeds flags 1 and 2 from hold, what the words at `000aacbc`–`000aacc4` the other modes use are, and what reads header words 3 and 4 at `000aad5a` and `000aad5c`.
- **Open:** how a mission is chosen: `00016e93` and `00036807`, the path builders `00015a13`, `00036649` and `000366c1`, and `000361ac`, which opens `msn\sing_msn.dat`.
- **Open:** whether a retail group's `0x4e` names a later group, which the load resolves against slots not yet read.
- **Open:** what loads `cfg\filename.bin` into the text table (`00018755`).
