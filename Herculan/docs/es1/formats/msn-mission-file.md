# GO.EXE — .MSN mission files

ES1's missions are the `.MSN` files in `ZONES.VOL`'s `MSN\` folder. The shell, `GO.EXE`, loads one, keeps the records the campaign's flags allow, and hands what is left to the simulator as `data\script.dat`, as ES2's shell does ([ES2's `.MSN`](../../retail/formats/msn-mission-file.md)). An ES1 mission holds eleven of ES2's seventeen rows, in ES2's order, and five of them have shorter records. It carries no text: the load reads no `.ENG` file, and ES2's rows of briefing and debrief text, its timers, its orders and its objectives are absent. The shell's briefing, debriefing and objectives read the mission's `.ENG` themselves ([`eng-mission-text.md`](eng-mission-text.md)). Addresses are `ES1GO` image addresses unless `ES1DBSIM` is named ([the X-32 executable](x32-executable.md#the-image)); ES2's are VSHELL's.

## Loading a mission

`MsnGen_LoadMission` (`00035873`) takes the mission's path; `es2_xref.py` finds its callers in `Career_StartMission` (`00016e93`) and `SingleMission_Start` (`00036807`), and the path is formatted as `msn\%s` in `000aad14` from a battle set's or the single missions' lists ([`bst-battle-sets.md`](bst-battle-sets.md)). In order, it:

1. reads the entry whole into a buffer and reads it through a memory stream ([streams](streams.md#goexe));
2. opens `data\script.dat` as a `FileRWStream`, asserting at severity 5 (`mg.cpp` line `0x4c5`) when it cannot. The file names are entries of the text table at `0009afb8`, which `FileNames_Load` (`0002094d`) loads from `cfg\filename.bin` when `Shell_Main` calls it: `Text_GetString` (`00018717`) gives entry 3, `data\script.dat`; 5, `data\mission.var`; and 10, `car\sm_play.mec`;
3. reads the revision word. When it is not 5 the load formats `ERROR: Script must be compiled with MC` and the number into a stack buffer that nothing reads, and goes on;
4. seeds the flags ([The flags](#the-flags));
5. reads the eleven rows with `MsnGen_ParseMsnFile` (`0003276c`), which always returns 1;
6. copies header words 3 and 4 to `000aad5a` and `000aad5c`, and overrides the header words the mode sets ([Header patches](#header-patches));
7. writes the 200 bytes of flags to `data\mission.var` through a `FileWStream` (assert at line `0x505`), and the mission to `data\script.dat` with `WriteScriptDatFile` (`0003445b`), laid out in [`script-dat.md`](script-dat.md);
8. counts the first group's member refs before its first `-1` into `Squad_MemberCount` (`0007ebc8`), which `CareerRecord_MarkTopPilots` reads ([a career's missions](bst-battle-sets.md#a-careers-missions)). The count stops at nothing but a `-1`: a group with eight members reads on into its `0x4a` word;
9. in mode 0 builds the squad from those members ([The squad](#the-squad));
10. writes `data\player.mec` with `PlayerMec_Write` (`0002299e`, [the player file](career-log-and-player-mec.md#the-player-file)), in either mode;
11. frees the arrays of conditions, points, headings, waypoint groups (with their waypoint lists), mechs, groups and bases. It does not free the trigger areas, actions or flyers.

### The mode and the mission parameters

**A mission is a single mission or a career's by the shell's mode, and a single mission takes five of its header words from the MISSION PARAMS menu: AMMO, DAMAGE, DIFFICULTY, VISIBILITY and TIME OF DAY.**

The mode is `Shell_Mode` (`000aad56`), 0 for a single mission and 1 for a career, which the main menu's buttons set ([the main menu's buttons](../shell/main-menu.md#the-buttons)). In steps 4 and 6, mode 1 takes its values from the `0x1f7`-byte record at `0009b23b + 0x1f7 × [0009b21f]` and mode 0 from the words at `000aacbc`–`000aacc4`; step 9 runs only in mode 0.

In the SINGLE MISSION menu, `MissionMenu_OnMessage` (`0002b375`) keeps the value of its list control (id 1101), which the two arrows step, in `000aad5a`, and BRIEFING (1501) calls `SingleMission_Start`, which passes `000aad5a` to `SingleMission_BuildPath` as the list index. Step 6 of the load then stores header word 3 there, which is not always the index of the list in `msn\sing_msn.dat` that names the mission: after its header patches `SM_SCR01.MSN`'s is 4, the list that names it, and `SM_ASS01.MSN`'s is 9, where lists 6 and 7 name it. `Mission_ReadRestore` (`00036512`) also writes the mode, reading it back from `data\restore.dat` ([which file](eng-mission-text.md#which-file)); the other 37 instructions `es2_xref.py` finds holding the mode's address read it.

The five words are the MISSION PARAMS menu's settings, `dlg\menu1110.dlg`, and options 12 to 16 of `data\prefs.cfg`:

| Word | Header word | Button | Option | Values | The simulator's reading |
|---|---|---|---|---|---|
| `MissionParams_Ammo` (`000aacbc`) | 5 | AMMO | 12 | 0, 1 | [Open](#open) |
| `MissionParams_Damage` (`000aacbe`) | 6 | DAMAGE | 13 | 0, 1 | [Open](#open) |
| `MissionParams_Difficulty` (`000aacc0`) | 7 | DIFFICULTY | 14 | 0 to 3 | the [difficulty scale](../simulation/world.md#the-difficulty-scale--damage_scalebydifficulty-0001f82c)'s index |
| `MissionParams_Visibility` (`000aacc2`) | 8 | VISIBILITY | 15 | 0, 1 | the [world file](wld-world.md#which-file)'s *d* and the [contact scan range](../simulation/world.md#small-functions) |
| `MissionParams_TimeOfDay` (`000aacc4`) | 9 | TIME OF DAY | 16 | 0 to 2 | the world file's *v*; also flag 3 in either mode |

The shell keeps 17 option bytes with a handler each: `Prefs_Load` (`000081ca`) reads the 17 bytes of `data\prefs.cfg` into `Prefs_Options` (`000aa24e`), zeroes them when the file does not open, calls each option's handler (`Prefs_Handlers`, `000aa272`) and copies them into `Prefs_Committed` (`000aa260`). At start-up `Shell_LoadData` (`00020630`) copies options 12 to 16 into the five words, and `Shell_HandleMessage` (`0003c7d8`) copies them in again at `0003e718`, keeping copies at `+0x26`–`+0x2e` of the object in `EBX` there, the offsets CANCEL puts the words back from. `MissionParams_OnMessage` (`0002b4fe`) handles the menu's buttons:

- AMMO, DAMAGE, DIFFICULTY, VISIBILITY and TIME OF DAY (ids 1111 to 1115) each add 1 to their word modulo the number of its values, show it in controls 1116 to 1120 and store it in their option with `Prefs_SetOption` (`00008378`), which calls the option's handler;
- CANCEL (99) puts back the five words from the menu's `+0x26`–`+0x2e` and the options from `Prefs_Committed` (`Prefs_RevertAll`, `00008316`), then does what ACCEPT does;
- ACCEPT (100) copies the options into `Prefs_Committed` (`Prefs_CommitAll`, `000082b0`) and writes all 17 to `data\prefs.cfg` (`Prefs_Save`, `00008267`).

### The draws

**Every random choice the load makes comes from one generator whose state at the load depends on the second the shell started and on every draw since, so a port can make retail's choices only from a known state.**

#### The shell's generator

`Math_RandomNext` (`00038723`) is the simulator's generator ([the helpers that match](../simulation/fixed-point-math.md#helpers-that-match), ES2's [algorithm](../../retail/simulation/random-generator.md#the-algorithm)) over the shell's own state, `Shell_RandomState` (`0009aec0`). `Math_RandomBelow` (`00038774`) is `(Math_RandomNext(state) & 0x7fff) % bound` and returns 0 without a draw for a bound of 0. `Math_RandomSeed` (`000386c8`) sets the cursors `+0x70` to `0x18` and `+0x71` to `0x37` and copies the 112-byte `Math_RandomSeedTable` (`00081720`), which is the simulator's and ES2's seed table byte for byte.

The static initializer `00020b68` seeds `Shell_RandomState` and a second state, `Shell_RandomState2` (`0009af34`), with `Math_RandomSeed`. At start-up `Shell_LoadData` (`00020630`) seeds `Shell_RandomState` again with `Math_RandomSeedAndSkip` (`000386f4`), which seeds it and discards *n* draws, *n* being `time` (`0005cf78`) modulo 128: the seconds since 1970 that the C library's `time` makes from the DOS clock and date. So a session starts at one of 128 states, as ES2's shell does from the tick count ([ES2's shell generator](../../retail/shell/campaign-loop.md#the-shells-generator)). `es2_xref.py` finds `Shell_RandomState`'s address in the load's functions below, in `PilotEntry_InitRandom` ([pilot entries](bst-battle-sets.md#pilot-entries)) and in `MTWindow_Ctor` (a random background for menu 9999), `DigitalDialog_ShowMessage`, `TtmScript_RunPage`, `TtmScript_RunSoundCommand`, `StrList_PickRandom`, `SingleMission_BuildPath`, `Mission_BuildPathFromFiles`, `000120f9`, `000162f4` and `00037bf0`: the menus, the animation scripts and the mission's path draw on it between start-up and a load.

#### The order of the load's draws

In mode 0, in this order:

1. flags 4, 5 and 6, each `Math_RandomBelow`(12) ([The flags](#the-flags));
2. in the conditions row, in file order, each type-1 condition whose parent passes `Msn_ConditionFilterGate`, a draw below its word `0x06`, kept at its `0x08`;
3. in the rows of points, headings, waypoint groups, mechs, flyers and bases, in that order and in file order, each record whose condition passes and whose random table key is not `-1`: `Msn_PickOption`'s draws, one below the word `0x0a` of each type-3 condition whose `0x06` is the key, in condition order, until one has a type-2 record whose range holds the draw ([random tables](#filtering-random-tables-and-repeats));
4. the theater, header word 0, a draw below 4;
5. for each squad member after the first, four to six draws ([The squad](#the-squad));
6. the player's pilot word, a draw below 11 ([a single mission's file](career-log-and-player-mec.md#a-single-missions-file)), the load's last.

Mode 1 makes the draws of steps 1 to 3 and none of 4 to 6.

### The squad

**In a single mission the player takes the hangar's stock HERC of the chassis the mission gives the player's machine, and each squadmate the HERC the mission gives its own machine, with a random name and rank.**

Step 9 builds the squad from the first group's members, *n* of them, `Squad_MemberCount`:

1. It loads `car\sm_init.dat` into the hangar with `Hangar_LoadFile` (`00028c25`, [the HERC lists](car-hercs-and-stock.md#the-herc-lists)): slots 0 to 7 hold a HERC of chassis 0 to 7, and the slot count is 11.
2. It sets up `SingleMission_Record` (`0009b014`), the [career record](bst-battle-sets.md#the-career-record) a single mission uses, with `CareerRecord_Init`: every pilot entry through `PilotEntry_Init` ([pilot entries](bst-battle-sets.md#pilot-entries)), so each is on strength with no HERC, and the equipment from `bst\tech_sng.dat`.
3. For each member *i* from 0 to *n* − 1, with *m* its mech record, the record `MsnGen_HercArrayIndex` (`0003581a`) gives for the ref, which is block-6 record *ref* of `data\script.dat`, it fills an entry set up by `PilotEntry_Init`:
   - the first member, the player, gets name words 0 and 0 and the slot numbered by *m*'s type `0x30`, which `sm_init.dat` fills with its HERC of that chassis. *m*'s weapons and words are not used;
   - each other member gets the name group (`+0`) a draw below 4 plus 4(*i* − 1), the name within it (`+2`) a draw below 3, the rank (`+0xd`) a draw below 6, and `+0x17`, `+0x19` and `+0x1b` from `PilotEntry_DrawForRank` (`00015402`): with *r* the rank and *t* the threshold `PilotRankThresholds`[*r*] (0, 1, 5, 15, 30, −1), `+0x17` is *t* plus a draw below *t* + 1, no draw for *r* = 5, and for *r* not 0, `+0x19` and `+0x1b` are draws below 5*r* and 10*r*. It gets a new HERC: `Herc_Ctor` (`00022f5e`) for chassis −1, then the chassis *m*'s `0x30`, the mount count of that chassis from the table at `000803c8` ([the HERC record](car-hercs-and-stock.md#the-herc-record)), for each mount *k* the weapon *m*'s `0x32` + 2*k* and the word *m*'s `0x4e` + 2*k*, `+0x2c` *m*'s `0x62`, and every status word 100 but internal word 9 (`Herc_SetAllStatus`(*h*, 100), `00024c3c`). `Hangar_AddHerc` (`00029d0f`) stores it in the first empty slot, 8 for the first squadmate, and the entry takes that slot; with no empty slot left the entry's slot is −1 and the HERC is kept nowhere.
   
   It then copies the entry over pilot entry *i* of the record, at `0009b028` + `0x25` × *i*. Nothing bounds *i* at the record's four entries: a fifth member's entry lands on the equipment state at `+0xa8`. The first group of each of the 14 single missions holds 1 to 4 member refs, `SM_SCT50.MSN`'s 4.
4. It makes the player's entry, the record's `+0xe`, 0, the current pilot (`CareerRecord_SelectPilot`, `00015ad4`), which sets the hangar's selected slot `Hangar_SelectedSlot` (`00080894`) to the player's.
5. It writes the hangar to `car\sm_play.mec` with `Hangar_SaveFile` (`0002909c`), which in mode 0 holds slots 0 to 7 only ([the HERC lists](car-hercs-and-stock.md#the-herc-lists)).

`PlayerMec_Write` ends, in mode 0, by writing `SingleMission_Record` whole to `car\sm_play.gam` (`SingleMission_SaveRecord`, `00014f54`); after the mission `Shell_ProcessMissionResults` reads both `car\` files back ([after a mission](bst-battle-sets.md#after-a-mission)). A byte search of `DBSIM.EXE` finds no `sm_play`, where the same search finds `player.mec`.

### The flags

The conditions compare against 100 `int16` flags at `000ab244`, which the load sets before it reads the rows:

| Flag | Mode 1 | Otherwise |
|---|---|---|
| all | kept as they are | 0 |
| 1 | the record's word at `+6` | 99 |
| 2 | the record's word at `+8` | 0 |
| 3 | TIME OF DAY, `000aacc4` | TIME OF DAY, `000aacc4` |
| 4, 5, 6 | each a draw below 12 | each a draw below 12 |

The draws are the load's first ([the draws](#the-draws)). No row writes a flag: the header patch has no flag list ([Header patches](#header-patches)), so `data\mission.var` is the array as seeded.

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

Rows 3 to 6 are ES2's rows 6 to 9 field for field: the [point](../../retail/formats/msn-mission-file.md#row-6-field-decode--the-point-record-dat_0047064e-22-bytesrecord) with its random table key and sum flag and its `int32` X, Y and Z at `0x0a`, `0x0e` and `0x12`; the [heading](../../retail/formats/msn-mission-file.md#row-7-field-decode--the-heading-record-dat_00470650-10-bytesrecord), its value at `0x08`; the [waypoint group](../../retail/formats/msn-mission-file.md#row-8-field-decode--the-waypoint-group-dat_00470656-10-fixed-bytes--nested-count2-bytesrecord), five words, the fifth a count of the point refs that follow; and the [trigger area](../../retail/formats/msn-mission-file.md#row-9-field-decode--the-trigger-area-record-dat_0047065e-12-bytesrecord), whose `0x08` is a point ref and whose `0x0a` is a second point ref when its shape `0x06` is 0. The loader reads a waypoint group's five words one at a time and resolves each waypoint as it reads it.

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
| 5, 6, 7 | 0, 0, 2 | AMMO, DAMAGE and DIFFICULTY ([the mission parameters](#the-mode-and-the-mission-parameters)) |
| 8, 9 | the patch's | VISIBILITY and TIME OF DAY |

`WriteScriptDatFile` writes the mode in place of word 2. So of a mission's own patches, words 1, 3 and 4 reach the simulator in either mode, and words 8 and 9 in mode 1, and a single mission's theater is drawn. Word 0 is the theater and word 1 the zone whose shapes the simulator and the shell's map load ([`zone-terrain.md`](zone-terrain.md#which-zone-and-which-theater)), and words 0, 8 and 9 name the theater's world file ([`wld-world.md`](wld-world.md#which-file)); what words 4 to 6 mean to the simulator is [Open](#open).

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
| `0x04` | random table key |
| `0x06` | not read by the load, not exported |
| `0x08`–`0x2f` | 20 words, exported, unset 0 |
| `0x30` | type: the word the simulator's first pass keeps for the slot ([`script-dat.md`](script-dat.md#reading)); the squad builder looks up a count for it in the table at `000803c8`, the chassis' mount counts ([the HERC record](car-hercs-and-stock.md#the-herc-record)) |
| `0x32`–`0x45` | ten words, exported, unset `-1`; the squad builder copies the first *n*, *n* the type's count |
| `0x46` | point ref (row 3), resolved |
| `0x48` | heading ref (row 4), resolved |
| `0x4a`, `0x4c` | two words, exported, unset `-1`: the out-of-action pair, [below](#flyers) |
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
| `0x00`–`0x04` | GUID, condition, random table key |
| `0x06` | not read by the load, not exported |
| `0x08`–`0x2f` | 20 words, exported, unset 0 |
| `0x30` | point ref (row 3), resolved |
| `0x32` | heading ref (row 4), resolved |
| `0x34` | type, kept by the simulator's first pass |
| `0x36`, `0x38` | two words, exported, unset `-1`: the out-of-action pair |
| `0x3a`, `0x3c` | action refs (row 7), resolved |
| `0x3e` | a word, exported, unset 100 |

**The out-of-action pair** of a mech, flyer or base record (mech `0x4a`/`0x4c`, flyer `0x36`/`0x38`, base `0x0e`/`0x10`) is a flag number and an operation: the simulator's second pass stores it at the object's `+0x174`/`+0x176` ([the second pass](script-dat.md#the-second-pass)), and `Mech_ReportOutOfAction` writes that flag by that operation when the object goes out of the fight, nothing for a negative flag number ([the out-of-action report](../simulation/sim-object-base.md#the-out-of-action-report--mech_reportoutofaction-0000d77c)). It is ES2's first (counter, operation) pair, with ES1's one pair in place of ES2's ten. The action refs are the object's `+0x16c`/`+0x170` action pointers: a struck enemy fires the first, and the object's going out of the fight the second.

### Bases

24 bytes, where ES2's are 62:

| Offset | Field |
|---|---|
| `0x00`–`0x04` | GUID, condition, random table key |
| `0x06` | not read by the load, not exported |
| `0x08` | type, kept by the simulator's first pass |
| `0x0a` | point ref (row 3), resolved |
| `0x0c` | heading ref (row 4), resolved |
| `0x0e`, `0x10` | two words, exported, unset `-1`: the out-of-action pair, [above](#flyers) |
| `0x12`, `0x14` | action refs (row 7), resolved |
| `0x16` | the starting condition, exported, unset 100: the simulator places the structure undamaged at 100 or a negative value, destroyed and wrecked at 0, and damaged by the rest of 100 per cent otherwise ([construction](../simulation/structures.md#construction--base_construct-00003cbc)) |

In `B0_C2_60.MSN` four bases hold 41 at `0x0e` and 2 at `0x10`, and `0x16` holds 0, 5, 20, 50 or 100 across the two files walked.

### Groups

88 bytes, where ES2's are 164:

| Offset | Field |
|---|---|
| `0x00` | GUID |
| `0x02` | condition |
| `0x04` | not read by the load, not exported |
| `0x06`–`0x2d` | 20 words, exported, unset 0 |
| `0x2e` | member kind: 0 mechs, 1 flyers, 2 bases |
| `0x30` | the formation, exported, unset `-1` ([formations](formations-dat.md)) |
| `0x32` | the order, exported, unset `-1`: the AI state the group's members take ([choosing a state](../simulation/mech-ai.md#choosing-a-state--mech_aiselectbehaviour-0001892c)) |
| `0x34` | point ref (row 3), resolved |
| `0x36` | heading ref (row 4), resolved |
| `0x38` | waypoint group ref (row 5), resolved |
| `0x3a`–`0x49` | eight member refs into the row `0x2e` names, resolved |
| `0x4a` | a word, exported, unset `-1`: the group's side, its low byte; for a base member the simulator's first pass keeps it beside the base's type |
| `0x4c` | the row `0x4e` names: 0 groups, 1 mechs, 2 flyers, 3 bases |
| `0x4e` | a ref into that row, resolved: the group's target |
| `0x50` | action ref (row 7), resolved |
| `0x52` | a word, exported, unset `-1` |
| `0x54`, `0x56` | the group's out-of-action (flag, operation) pair, exported, unset `-1` |

A kind outside those listed leaves its refs as authored. ES2's group names up to ten order records (ES2's row 15), each with a ref of this kind-and-index shape (its subject) and an action ref; an ES1 group holds one of each itself, and has eight member refs where ES2's has twenty. A HERC group whose record holds the order's unset −1, 32 of them the player's squad, takes an order from a later record of its GUID ([repeated GUIDs](#filtering-random-tables-and-repeats)), some under a condition or the halves of a draw: the mode-0 load this page gives, run over the 75 files in this layout with 32 sequences of draws, leaves every HERC group an order of 1, 3, 6, 8, 10 or 11, so no HERC's AI meets the −1 ([choosing a state](../simulation/mech-ai.md#choosing-a-state--mech_aiselectbehaviour-0001892c)). `B0_C1_20.MSN`, in the other layout, gives its six −1 HERC groups orders by such records too. In `SM_SCR01.MSN` the squad's 10, overlaid with 6, reaches retail's `data\script.dat` as 6, and its structure group's −1 as −1. The simulator's group record and what reads `0x4a`, `0x4c`/`0x4e` and `0x54`/`0x56` are in [`mission-groups.md`](../simulation/mission-groups.md#the-group-record). The first group stands for the player's squad, as ES2's does: the mode-0 load builds a squad from its members, and the simulator's first pass skips it when it marks members live and marks `data\player.mec`'s machines live instead ([`script-dat.md`](script-dat.md#reading)).

## Filtering, random tables and repeats

The load treats each record as ES2's does ([the parser](../../retail/formats/msn-mission-file.md#msngen_parsemsnfile-00417b67--the-raw-msn-parser)): a record whose condition fails `Msn_ConditionFilterGate` (`00032711`) is dropped, the survivors are packed from slot 0, and each ref is resolved in place to the index `data\script.dat` gives its target, the number of surviving records before it whose GUID is not `-1`.

**Random tables.** Points, headings, waypoint groups, mechs, flyers and bases carry a random table key at `0x04`. A record whose key is not `-1` takes the template of the option `Msn_PickOption` (`000318d5`) picks, as ES2's does ([random tables](../../retail/formats/msn-mission-file.md#random-tables)), and copies everything after its four leading words from it: X, Y and Z; the heading; the waypoint list; a mech's `0x08`–`0x69`; a flyer's `0x08`–`0x3f`; a base's `0x08`–`0x17`. The copied refs are already resolved. A record whose key is `-1` resolves its own refs.

**Groups resolve their `0x4e` ref as they are read.** When `0x4c` names the group row, the load searches it up to the count the file declares, slots not yet read included, where ES2 resolves an order's group subject after the last group is read.

**Repeated GUIDs.** A record whose GUID a survivor already has replaces it for points, headings, waypoint groups and trigger areas, and overlays it for actions, mechs, flyers, bases and groups, copying each field that is not its unset value (the tables above), as ES2's rows do ([repeated GUIDs](../../retail/formats/msn-mission-file.md#repeated-guids)). The earlier record's slot is found differently by row. Mechs, flyers and bases use the record's own slot, as ES2's rows all do. The other six use the number of surviving records before it whose GUID is not `-1`, so when a survivor with GUID `-1` precedes it, the replacement or overlay lands on an earlier slot than the record with the GUID.

## A file in another layout

Of the 77 files, 75 follow the layout above and end on their last group's last byte, `SM_SCT50.MSN` (56,392 bytes) and `B0_C2_60.MSN` (41,736) among them. Two do not, `B0_C1_20.MSN` (6,402 bytes) and `B0_C1_30.MSN` (2,694). Read by the layout above, each has no conditions and then a header-patch count, 1,120 and 1,130, far more records than the file holds, and that is what `GO.EXE`'s loader would take from each. No battle set or mission list names either file: `BTLSET_0.DAT` names `any_msn.dat` at the two positions of its campaign 1 between `B0_C1_10`/`B0_C1_11` and `B0_C1_40` ([the retail battle sets](bst-battle-sets.md#the-retail-battle-sets)), and the layout below is a reading of the bytes ([Open](#open)).

Both files walk to their last byte in this layout:

1. the revision word, 5;
2. ten header words, with no condition and no count;
3. the conditions, as above;
4. rows 3 to 11 as above, with no header-patch row, except that a mech record is 88 bytes.

| | `B0_C1_20.MSN` | `B0_C1_30.MSN` |
|---|---|---|
| Header words | 0, 1120, 1, 3, 2, 0, 0, 0, 0, 0 | 0, 1130, 1, 2, 1, 0, 0, 0, 0, 0 |
| Rows 1, 3–11 | 4, 33, 12, 6, 0, 4, 34, 2, 5, 23 | 5, 26, 12, 4, 3, 3, 8, 2, 6, 8 |

Word 1 holds 1120 and 1130, the numbers of `ZONE1120.DAT` and `ZONE1130.DAT`. A header patch holds the zone in its word 1 ([which zone](zone-terrain.md#which-zone-and-which-theater)), as `B0_C1_40.MSN`'s first patch holds 1140.

An 88-byte mech is the record above to `0x4d`, then one word at `0x4e` where that record has ten, then that record's `0x62`–`0x69` at `0x50`–`0x57`. All 42 hold 4 at `0x4e`, 2 at `0x50` and 100 at `0x56`, and four of `B0_C1_20.MSN`'s hold the GUIDs of its actions 46–49 at `0x52`, where the record above has its first action ref.

## Rejected readings

| Reading | Why it is wrong |
|---|---|
| An ES1 `.MSN` is ES2's revision-5 layout, since it opens with the word 5 | ES1's loader checks for 5 too and reads eleven rows; ES2's reader runs off the end of most ES1 files |

## Open

- **Open:** what reads [the other layout](#a-file-in-another-layout), and so what its header words and its mechs' word at `0x4e` mean. The byte search and the path builders that turned up no reader are in [`bst-battle-sets.md`](bst-battle-sets.md#open).
- **Open:** whether mech `0x62` and flyer `0x3e` are ES2's constant and starting condition. The simulator's second pass does not read them; it hands mech `0x4e`–`0x61` to `Mech_ConfigureLoadout` as the ammunition types and `0x68` to `Mech_ApplyStartingCondition` as the starting condition ([the second pass](script-dat.md#the-second-pass)).
- **Open:** what the exported fields mean that no code read so far uses: the 20 words at mech and flyer `0x08` and group `0x06`, of which the simulator's second pass reads only mech `0x08` and `0x0a` ([the second pass](script-dat.md#the-second-pass)), an action's type and verb, the group word `0x52`, and header words 4 to 6. The simulator's mission status reads group `0x32` and header word 3 ([the status](../simulation/mission-status.md#the-status--mission_status-0000f4e8)).
- **Open:** whether a group's `0x50` action holds the group out of the mission until it fires, as ES2's group action `0x70` does: the simulator keeps it at the group's `+0x16`, and the functions that sweep objects for shots, blasts and trigger areas skip the group's members while it is set ([the group record](../simulation/mission-groups.md#the-group-record)). And whether `0x30` and `0x32`, which the simulator copies to the group record's `+2` and `+4`, hold an order's verb and formation. In `B0_C2_60.MSN` the first group, the squad, holds 0 at `0x4a` and the groups that name it or one of its mechs at `0x4e` hold 1.
- **Open:** what reads header word 4 at `000aad5c`.
- **Open:** a set of the three `data\` files retail's shell wrote for `SM_SCR01.MSN` holds DIFFICULTY 2 in `data\script.dat`'s word 7 beside a `data\prefs.cfg` whose option 14 is 0, written after it; the simulator's `0004a7fc` (`ES1DBSIM`) writes its own 17 options to `data\prefs.cfg`. A retail check: set DIFFICULTY in MISSION PARAMS, ACCEPT, and read `data\prefs.cfg`'s byte 14 before the mission starts and after it ends.
- **Open:** what the group slots not yet read hold, which the load searches when a group's `0x4e` names the group row. In the 75 files in this layout every group that targets a group names one earlier in the file, so a search reaches them only when its target's record was dropped by its condition.
- **Open:** a set of retail's three files for `SM_SCR01.MSN` (the one above) matches the mode-0 load this page gives, with the shell's generator 69 draws past its seed, in all of `data\mission.var` and of `data\script.dat`'s first 3,260 bytes but two places: the three mechs whose template is condition 38's hold weapons 1, 7 and 9 and mount words 5, 5 and 1, where that template holds 1, 6 and 17 and 5, 5 and 5, and no mech record of the file holds weapon 7; and in `data\player.mec` the player's first pilot word is the draw three after the one [the order of the draws](#the-order-of-the-loads-draws) gives it. The three mechs are GUIDs 37, 45 and 51, `data\script.dat`'s block-6 records 4, 12 and 18, each of type 8 with the template's other fields. The functions the load runs hold no step that makes either difference: `MsnGen_ParseMsnFile` copies a keyed record's `0x08`–`0x69` from its template, the first record read before it whose condition is the picked option's range (`Msn_TemplateRow12`, `Msn_PickOption`) and overlays a repeated GUID with `Msn_MergeRow12`, which copies only fields that are not unset; `WriteScriptDatFile` writes the mech array as it stands, before the squad build; and of the 16 functions `es2_xref.py` finds calling `Math_RandomBelow` (`00038774`), the load reaches `MsnGen_LoadMission`, `MsnGen_ParseMsnFile`, `Msn_PickOption`, `PilotEntry_DrawForRank` and `PlayerMec_WritePilotWords`, at the steps the order gives, and `PilotEntry_InitRandom` only in mode 1. So the set may come from a shell state the load alone does not give, such as an earlier load in the same session. A retail check: start the shell, load `SM_SCR01` once from SINGLE MISSION, and break on `WriteScriptDatFile` (`0003445b`) to read the three mechs' weapons in the array at `00080f30` and the generator state `Shell_RandomState` (`0009aec0`) before the write.