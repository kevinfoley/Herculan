# GO.EXE — battle sets, mission lists and equipment

ES1's shell takes the name of each mission it loads from lists in `ZONES.VOL`. A career plays a battle set's campaigns one mission position at a time, each position naming a mission, or a list to draw one from, for each of two outcomes of the mission before; a single mission is drawn from `msn\sing_msn.dat`. The shell formats the name as `msn\%s` into `000aad14`, the path `MsnGen_LoadMission` opens ([`msn-mission-file.md`](msn-mission-file.md#loading-a-mission)). Which chassis and weapons the player can take comes from `bst\tech_0.dat` in a career, where more of them become available as missions are played, and from `bst\tech_sng.dat` outside one ([Equipment availability](#equipment-availability)). ES2's shell takes its missions from a campaign table instead ([`gam\career.dat`](../../retail/shell/campaign-loop.md#the-campaign-table--gamcareerdat)). Addresses are `ES1GO` image addresses ([the X-32 executable](x32-executable.md#the-image)).

## Name lists

A name list is a byte, the count *n*, then *n* names, each with its NUL. `StrList_Read` (`000201b7`) reads one from a stream into a 6-byte list: the count into the low byte of the word `+0`, and a pointer at `+2` to *n* new 13-byte buffers, each filled by `Stream_ReadCString` (`00020154`), which reads a byte at a time until it has read a NUL or 13 bytes. A count of 0 allocates nothing and skips the byte after it. `StrList_Get` (`00020254`) returns name *i*, or the only name when *n* is 1, whatever *i* is; `StrList_PickRandom` (`00020289`) returns name `Math_RandomBelow`(*n*) on the shell's generator `0009aec0`; `StrList_Dtor` (`000202b4`) frees the names and the pointer array.

`ZONES.VOL`'s `BST\` folder holds twelve files that are one name list each. A battle set position names one by its file name ([A career's missions](#a-careers-missions)), and the retail battle sets name only `any_msn.dat`. Each name ends `.MSN`:

| File | Names |
|---|---|
| `ANY_MSN.DAT` | `sm_rcn01`, `sm_rcn02`, `sm_sct01`, `sm_sct02`, `sm_sct50`, `sm_ptl01`, `sm_ptl02`, `sm_rad01`, `sm_rad02`, `sm_scr01`, `sm_sar01`, `sm_ass01`, `sm_ass01` |
| `ANY_ASL.DAT`, `ANY_STK.DAT` | `sm_ass01` |
| `ANY_PTL.DAT` | `sm_ptl01`, `sm_ptl02` |
| `ANY_RAD.DAT` | `sm_rad01`, `sm_rad02` |
| `ANY_RCN.DAT` | `sm_rcn01`, `sm_rcn02` |
| `ANY_SAR.DAT` | `sm_sar01` |
| `ANY_SCR.DAT` | `sm_scr01` |
| `ANY_SCT.DAT` | `sm_sct01`, `sm_sct02`, `sm_sct50` |
| `ANY_STRK.DAT` | `B0_C2_00`, six times |
| `ANY_CNV.DAT`, `ANY_ESC.DAT` | none: the file is the count byte 0, with no byte after it |

## Battle sets

`BST\BATLSETS.BIN` is a string table ([`.BIN` string tables](bin-string-tables.md#the-table)) of three names, `btlset_0.dat`, `btlset_1.dat` and `btlset_2.dat`. A battle set file is a byte, the count of campaigns; then for each campaign a byte, the count of its positions, and for each position a name list. Nothing follows the last campaign.

| Object | Bytes | Fields |
|---|---|---|
| battle set (`000a02cc`) | 6 | `+0` campaign count; `+2` the campaign array |
| campaign | 8 | `+0` the word of the table `0007ea94` for its index; `+2` position count; `+4` the position array |
| position | 6 | a name list |

`BattleSet_Load` (`00013f01`) loads `bst\batlsets.bin`, reads the battle set its entry *index* names with `BattleSet_ReadFile` (`00013fad`), and then loads the single missions' lists (`SingleMissions_Load`, [Single missions](#single-missions)) and the pilot names (`PilotNames_Load`, `00015070`). `BattleSet_ReadFile` frees the set's campaigns (`BattleSet_FreeCampaigns`, `00013f7a`), opens `bst\<name>`, reads the count byte into the low byte of `+0`, makes that many campaigns with `Campaign_Ctor` (`00014075`), reads each with `Campaign_Read` (`0001422c`) and gives campaign *i* the word *i* of the table at `0007ea94`: 0, 1, 2, 3, 1, 2, 3, 1, 2, 3. `Campaign_Read` reads the position count into the low byte of `+2` and, when it is nonzero, frees the old positions, makes new ones with `Campaign_PositionCtor` (`000142c7`) and reads each with `StrList_Read`. `Campaign_Dtor` (`00014355`) and `BattleSet_Dtor` (`00013eac`) free them; `BattleSet_Dtor` also frees the pilot names (`PilotNames_Free`, `00015153`).

A campaign's `+0` word is the *k* of its talking-head files `C<k>_TEXT.BIN` and `D<k>_TEXT.BIN` ([which file and line](bin-string-tables.md#which-file-and-line)).

`PilotNames_Load` reads `car\names.dat` from `SHELL0.VOL`: a count byte of groups, then per group a count byte and that many names, each read by `Stream_ReadCString` into a 16-byte buffer with a bound of 16, into an array of name arrays at `0007ebc4`. The retail file holds twelve groups of three, each a name and the name followed by 1 and 2 (`DUGGAN`, `DUGGAN 1`, `DUGGAN 2`; `RIGGS`, …). `PilotNames_Free` frees twelve groups of three names, whatever the file held.

### Which battle set

`BattleSet_Load` loads the battle set whose index is the word `+5` of the roster at `0009b218` (`0009b21d`). `es2_xref.py` finds three calls to it:

- `00020630`, which also loads the shell's fonts and `eng\watext.bin`, after it calls `00017e7f` with `0009b20c`;
- `BattleSetDialog_OnMessage` (`0002bb29`), a dialog's message handler, which on message 100 stores in the roster's word `+5` the value of the dialog's control `0x533` (its vtable slot `+0x2c`), loads that battle set, and changes the message to `0x514`;
- `Career_StartMission`, before it builds each mission's path ([A career's missions](#a-careers-missions)).

`BATLSETS.BIN` names three files, so the word selects `BTLSET_0` to `BTLSET_2`. A byte search of `GO.EXE` for `test_btl` finds nothing, where the same search finds `tech_sng.dat` ([Open](#open)).

### The retail battle sets

Every position of the four retail files names two files, the same file twice where the table below shows one. Names are written without their `B0_` and `.MSN`; *a*/*b* gives outcome 0's and outcome 1's where they differ, and *any* is `any_msn.dat`.

`BTLSET_0.DAT`, eight campaigns:

| Campaign | Positions |
|---|---|
| 0 | `C0_0A`, `C0_1A`, `C0_2A`, `C0_3A`/`C0_3B`, `C0_4A`, `C0_5A`, `C0_6A` |
| 1 | `C1_00`, `C1_10`/`C1_11`, any, any, `C1_40`, `C1_50`, `C1_60`/`C1_61`, `C1_70` |
| 2 | `C2_00`, `C2_10`/`C2_11`, `C2_20`, any, any, `C2_50`, `C2_60`, `C2_70` |
| 3 | `C3_0A`, any, `C3_2A`, any, any, `C3_6A`, any, any, `C3_8A` |
| 4 | `C4_00`, any, `C4_20`, `C4_30`/`C4_31`, any, `C4_50`, any, any, `C4_80` |
| 5, 6 | `C5_00`, any, `C5_20`, `C5_30`/`C5_31`, any, `C5_50`, any, `C5_70` |
| 7 | `C7_0A` to `C7_7A`, eight |

`BTLSET_1.DAT` and `BTLSET_2.DAT` are the same bytes, eight campaigns with no list among their names:

| Campaign | Positions |
|---|---|
| 0 | `C1_00` |
| 1 | `C1_00`, `C1_10`/`C1_11`, `C1_40`, `C1_50`, `C1_60`/`C1_61`, `C1_70` |
| 2 | `C2_00`, `C2_10`/`C2_11`, `C2_20`, `C2_50`, `C2_60`, `C2_70` |
| 3 | `C3_0A` to `C3_8A`, nine |
| 4 | `C4_00`, `C4_20`, `C4_30`/`C4_31`, `C4_50`, `C4_80` |
| 5, 7 | `C5_00`, `C5_20`, `C5_30`/`C5_31`, `C5_50`, `C5_70` |
| 6 | `C6_0A` to `C6_7A`, eight |

`TEST_BTL.DAT` is `BTLSET_1.DAT` but for three campaigns: 0 is `C0_0A`; 1 opens with `C1_60` where `BTLSET_1`'s opens with `C1_00`; 2 is `C2_00` alone; and 7 is `C7_0A` to `C7_7A`.

All 38 of `ZONES.VOL`'s `any_msn.dat` names are in `BTLSET_0.DAT`. Every mission a list names is in `ZONES.VOL`; of its 77 missions, `B0_C0_4B`, `B0_C1_20`, `B0_C1_30`, `SM_ESC50` and `SM_RCN03` are named by no battle set or list.

## A career's missions

`Career_StartMission` (`00016e93`), given a roster and a word *b*, sets the roster's word `+7` to `Roster_NthUsedRecord` (`00016845`)(roster, *b*), loads the battle set whose index is the roster's word `+5` into `000a02cc`, builds the path from the [career record](#the-career-record) at roster + `0x23` + `0x1f7` × word `+7` with `Career_BuildMissionPath`, loads the mission with `MsnGen_LoadMission`, marks the record's pilots with `CareerRecord_MarkTopPilots` (`00015cbb`), copies the word `000aad5a` (header word 3) to the record's `+0xc`, and saves the paths with `Mission_WriteRestore`. `es2_xref.py` finds seven calls to it, each passing the roster `0009b218` and *b* 0, so the word `+7` is 0 and the record is the roster's first, at `0009b23b`, the record [mode 1's flags](msn-mission-file.md#the-flags) 1 and 2 are seeded from: the campaign and the position.

`Roster_NthUsedRecord` returns 0 for an *n* of 0; otherwise it counts the records 1 to 40 whose first byte is not 0 and returns the index of the *n*-th, or 0 when there are fewer.

`CareerRecord_MarkTopPilots` orders the pilot entries 1, 2 and 3 (the table at `0007ec9c`) by their word `+0xd`, greatest first, with a bubble sort swapping through `Word_Swap` (`00015c93`), and sets the byte `+0xa` of the entry in place *p* (0 to 2) to 1 when *p* + 1 is less than the word `0007ebc8`, and to 0 otherwise.

`Career_BuildMissionPath` (`00015a13`) passes the outcome, `Career_OutcomeChoice`(`000aad58`), the position and the campaign to `BattleSet_GetMissionName` (`000140a5`), formats the name as `msn\%s` into `000aad14`, and sets the text path (`Mission_SetTextPath`). `BattleSet_GetMissionName` takes the position's name for the outcome (`Campaign_GetPositionName`, `000142f6`, through `StrList_Get`). A name containing `.MSN` is returned as it is. Any other name is a list: it opens `bst\<name>`, reads one name list, and copies a name `StrList_PickRandom` draws from it over the position's name, which it returns. `Career_StartMission` reloads the battle set before every build, so a position naming a list draws again for each mission it starts.

`Career_OutcomeChoice` (`000159e9`) is 1 when the outcome word exceeds the word of the table at `0007ec80` that the word `000aad5e` indexes, and 0 otherwise. The table holds 4, 4, 4, 3, 3, 3, 4, 3, 3, 3. `000aad58` is the first word of `data\results.dat`, the debriefing's ordinal ([the debriefing](eng-mission-text.md#the-debriefing)), and `Mission_ReadRestore` loads `000aad5e` from the word saved from `000aad5a` ([the restore file](eng-mission-text.md#which-file)).

### The career record

A record is `0x1f7` bytes. `CareerRecord_Init` (`000158ff`) sets one up; `es2_xref.py` finds it called by `000163ac` for a roster record and by `MsnGen_LoadMission` for the record at `0009b014`.

| Offset | Field | At `CareerRecord_Init` |
|---|---|---|
| `+0` | byte, nonzero for a record in use ([`Roster_NthUsedRecord`](#a-careers-missions)) | 0 |
| `+4` | word | `0x6a4`; `0x6ae` after each career mission |
| `+6` | the campaign | 0 |
| `+8` | the position in it | 0 |
| `+0xa` | the missions played | 0 |
| `+0xc` | header word 3 of the mission last started | |
| `+0xe` | word, a pilot entry index | 0 |
| `+0x10` | the pilot count | 4 |
| `+0x12` | word, the pilot entry `CareerRecord_FinishMission` stores its share in | 0 |
| `+0x14` | four `0x25`-byte pilot entries | |
| `+0xa8` | the [equipment state](#equipment-availability), `0x81` bytes | loaded |
| `+0x12d` | 100 `int16` | 0 (`CareerRecord_ClearWords`, `000161ae`) |

The init also zeroes byte `+1`, the word `+2` and the word `000aad58`. For each pilot entry it calls `000151c8` when the mode word `000aad56` is 0 or the entry is the one `+0xe` names, and `00015228` otherwise ([Open](#open)).

### After a mission

`Shell_ProcessMissionResults` (`00038eb5`) reads the simulator's results back:

1. In mode 1 it calls `00016c1e` with the roster, otherwise `00014fd6`.
2. It opens `data\results.dat` (entry 7 of the file-name table, severity-5 assert at `shell.cpp` line 199) and reads a word into `000aad58`, a dword into `0007fcf4`, a word count into `000817cc`, and that many pairs of words (*a*, *b*); in mode 1 it passes each to `00028311`(`000aad62`, &*b*, *a*). It then calls `0001568d` with the stream.
3. It reads `data\mission.var` (entry 5, assert at line `0xd7`) whole into the 100 flags at `000ab244`, and sets flag 0 to `000aad58`.
4. In mode 1 it calls `00038e48`, adds `0007fcf4` to `0007fcf0`, calls `00028375`(`000aad62`), and on the record `0009b23b` + `0x1f7` × [`0009b21f`] calls `CareerRecord_FinishMission` (`000160cb`) and `Tech_Unlock` with the missions played ([Equipment availability](#equipment-availability)); it then calls `00029e01` and sets the record's word `+4` to `0x6ae`. Otherwise it calls `00015db7` with what `00014ef4` returns.

`CareerRecord_FinishMission` divides `0007fcf0` by *n*, the count `00015bfb` returns for the record, and *n* times calls `00015b0a` and stores the share at the dword `+0x21` of the pilot entry `+0x12` names; it then calls `00015e0b` and adds 1 to the missions played.

### Moving on

`Career_AdvanceCampaign` (`00015a5a`) moves a career record on with `BattleSet_Advance` (`000141d0`) and returns whether its campaign changed. `BattleSet_Advance` adds 1 to the position (`Campaign_NextPosition`, `0001431f`, which is true while the position is at least 0 and below the campaign's count); past the campaign's last position it sets the campaign to the table at `0007ead0` for the campaign and the outcome, and the position to 0. `Career_PeekNextCampaign` (`00015aa5`) returns, through `BattleSet_PeekNextCampaign` (`0001418e`), 0 when the next position is in the campaign, and otherwise the campaign the table gives, writing nothing.

| Campaign | Outcome 0 | Outcome 1 |
|---|---|---|
| 0 | 1 | 2 |
| 1 | 3 | 4 |
| 2 | 3 | −1 |
| 3 | 5 | 6 |
| 4 | 5 | −1 |
| 5 | 7 | −1 |
| 6 | 7 | −1 |
| 7 | 99 | −1 |

## Single missions

`SingleMissions_Load` (`000361ac`) reads `msn\sing_msn.dat`: a byte, the count, into the low byte of the word `00081084`, then that many name lists into a new array at `00081080` (`SingleMissions_ListCtor`, `0003625e`). The retail file holds eight lists: `sm_rcn01`, `sm_rcn02`; `sm_sct01`, `sm_sct02`, `sm_sct50`; `sm_ptl01`, `sm_ptl02`; `sm_rad01`, `sm_rad02`; `sm_scr01`; `sm_sar01`; `sm_ass01`; `sm_ass01`.

`SingleMission_Start` (`00036807`) passes `car\s_armory.dat` and the word `000aad62` to `00027e02`, builds the path with `SingleMission_BuildPath` (`00036649`) from the word `000aad5a`, loads the mission with `MsnGen_LoadMission` and saves the paths with `Mission_WriteRestore`. `SingleMission_BuildPath`, given a list index, or −1 for a list drawn at random, formats a name `StrList_PickRandom` draws from that list as `msn\%s` into `000aad14` and sets the text path.

`Mission_BuildPathFromFiles` (`000366c1`), given a `findfirst` pattern, counts the files that match, copies their names, formats one drawn at random as `msn\%s` into `000aad14`, sets the text path and frees the names. [Open](#open).

## Equipment availability

A career starts with three of the eight chassis and nine of the seventeen weapons, and makes the others available as missions are played, some up to two missions sooner depending on how the last mission ended; four weapons never become available this way. Outside a career all eight chassis and thirteen of the weapons are available from the start.

A career is the shell's mode 1, the mode word `000aad56`, in which the mission load seeds the flags from the roster's record ([the flags](msn-mission-file.md#the-flags)) and `Shell_ProcessMissionResults` updates that record.

The state is the `0x81` bytes at a career record's `+0xa8`:

| Offset | Field |
|---|---|
| `+0` | `int16`, the kind of the item last made available, −1 once the init has loaded the file |
| `+2` | `int16`, its index |
| `+4` | 8 chassis entries of 5 bytes: `int16` *first*, `int16` *second*, byte available |
| `+0x2c` | 17 weapon entries of 5 bytes, the same |

The file is an `int16` count and that many 8-byte records, nothing after them:

| Offset | Field |
|---|---|
| `0x00` | `int16` kind: 0 a chassis, 1 a weapon |
| `0x02` | `int16` index into the kind's entries |
| `0x04` | `int16` *first* |
| `0x06` | `int16` *second* |

`CareerRecord_Init` clears the available bytes with `Tech_Clear` (`00001ee3`), which leaves the counts as they were, and loads `bst\tech_0.dat` when the mode word `000aad56` is 1 and `bst\tech_sng.dat` otherwise with `Tech_LoadFile` (`00001dd4`). That opens the file, reads it with `Tech_Read` (`00001e16`), which copies each record's *first* and *second* into the entry its kind and index name, with no bounds test on the index, and reads and drops a record of any other kind, then calls `Tech_Unlock` (`00001f78`) with 0. The init then sets `+0` and `+2` to −1.

`Tech_Unlock`, given the state and a mission count *m*, first copies entry `0x2d` of the text table at `0009afa8`, `NONE`, into `0007d7e0`. Its grade test passes unless the mode is 1 and *m* is not 0; then it passes when `Mission_OutcomeGrade` (`00036837`) of the debriefing's ordinal `000aad58` and the mission's kind `000aad5e` is below 2. A chassis entry that is not yet available and holds no −1 becomes available when *first* ≤ *m* and the grade test passes, or when *second* ≤ *m*; a weapon entry the same with = in place of ≤. Each item made available sets its byte, copies its name into `0007d7e0`, and stores its kind and index at `+0` and `+2`, so `0007d7e0` ends holding the last item's name, or `NONE`. The names are entries `0x1b` + *i* (chassis) and `0xd5` + *i* (weapons) of `0009afa8`, which `00020630` loads from `eng\watext.bin` in `SHELL0.VOL`. `es2_xref.py` finds two calls: `Tech_LoadFile`'s with 0, and `Shell_ProcessMissionResults`' after each mission in mode 1, with the missions played that `CareerRecord_FinishMission` has just counted up ([After a mission](#after-a-mission)).

`Mission_OutcomeGrade`, given an ordinal and a kind, returns the ordinal's word of the table at `000810f4`, 0, 0, 1, 1, 1, 2, 2, 3, 3, 0, when the kind is 0, 1, 2 or 6, and of the table at `00081108`, 0, 0, 1, 1, 2, 2, 3, 3, otherwise, with no bounds test. The debriefing's talking head takes its line from the same value ([which file and line](bin-string-tables.md#which-file-and-line)).

`Tech_WriteWeaponFlags` (`00001f2b`) writes the 17 weapons' available bytes to a stream, one byte each; `CareerRecord_WriteWeaponFlags` (`000160ac`) calls it with the record's state. `Tech_FormatLockedName` (`0000216f`), given the state, an index and a kind, copies the item's name into `0007d808` when it is not yet available ([Open](#open)).

### The retail files

Both files hold 25 records, the 8 chassis and the 17 weapons. Each cell gives *first*, *second*; 0, 0 is available from the start.

| Chassis | `TECH_0.DAT` | `TECH_SNG.DAT` |
|---|---|---|
| 0–2, `ROADRUNNER`, `OUTLAW`, `RAPTOR` | 0, 0 | 0, 0 |
| 3, `TOMAHAWK` | 8, 9 | 0, 0 |
| 4, `PATRIOT` | 16, 18 | 0, 0 |
| 5, `RHINO` | 21, 23 | 0, 0 |
| 6, `SAMSON` | 27, 28 | 0, 0 |
| 7, `COLOSSUS` | 30, 31 | 0, 0 |

| Weapon | `TECH_0.DAT` | `TECH_SNG.DAT` |
|---|---|---|
| 0–2, `SCORPION`, `COBRA` and `ATLAS AUTOCANNON` | 0, 0 | 0, 0 |
| 3, `ELECTRON FLUX` | 25, 25 | 0, 0 |
| 4, `EM PULSE CANNON` | 5, 6 | 0, 0 |
| 5, `GRENADE LAUNCHER` | −1, −1 | −1, −1 |
| 6–8, `SUNBEAM`, `SKYFIRE` and `STARBURST LASER` | 0, 0 | 0, 0 |
| 9–11, `6`, `8` and `10 MISSILE ARRAY` | 0, 0 | 0, 0 |
| 12, `PARTICLE BEAM` | 13, 14 | 0, 0 |
| 13, `JAMMER POD` | 2, 4 | 0, 0 |
| 14–16, `SPECIAL EMP`, `SPECIAL PBW`, `SPECIAL ELF` | −1, −1 | −1, −1 |

## Open

- **Open:** whether the career and single-mission paths can reach the five missions no list names ([the retail battle sets](#the-retail-battle-sets)). A byte search of the seven `VOL` archives and of `GO.EXE` and `DBSIM.EXE` finds `B0_C1_20` and `B0_C1_30` only in `ZONES.VOL`'s directory ([a file in another layout](msn-mission-file.md#a-file-in-another-layout)).
- **Open:** what calls `Mission_BuildPathFromFiles` and with which pattern: `es2_xref.py` finds no branch to it, and its one stored-pointer hit, `000097e5`, is inside the instruction at `000097e4`.
- **Open:** which dialog `BattleSetDialog_OnMessage` serves, found by `es2_xref.py` only as the stored pointer at `00081faa`, and which values its control `0x533` gives; and what `00017e7f` does with `0009b20c` before `00020630` loads the battle set.
- **Open:** what reads `TEST_BTL.DAT` and the eleven `ANY_*.DAT` other than `ANY_MSN.DAT`. `BATLSETS.BIN` names only the three `BTLSET_*`, the retail battle sets name only `any_msn.dat`, and a byte search of `GO.EXE` finds neither `test_btl` nor `any_`, where it finds `tech_sng.dat`.
- **Open:** what the callers of `Career_AdvanceCampaign` and `Career_PeekNextCampaign` do with the campaign table's −1 and 99, how the outcome table at `0007ec80` relates to the debriefings' order, and which debriefings `Mission_OutcomeGrade` rates below 2.
- **Open:** what reads and restores the equipment state besides the init: what calls `CareerRecord_WriteWeaponFlags` (`es2_xref.py` finds one call, from `00022a05`) and what reads the bytes back, what saves the chassis' bytes, and what reads the state to offer the items; and what calls `Tech_FormatLockedName`, to which `es2_xref.py` finds no branch and no stored pointer.
- **Open:** how the equipment indexes map to the shell's chassis and weapon lists, `MECHS.NAM` and `WEAPONS.DAT`'s 17 templates among them.
- **Open:** the pilot entries: what `000151c8` and `00015228` set up, what the word `+0xd` and the byte `+0xa` are, what `0007ebc8` holds, and what `00015bfb`, `00015b0a` and `00015e0b` do with the pay share.
- **Open:** what `Shell_ProcessMissionResults`' callees do with the results: `00016c1e`, `00014fd6`, `00028311` with the word pairs, `0001568d`, `00038e48`, `00028375`, `00029e01`, `00015db7` and `00014ef4`; and what `0007fcf4` and `0007fcf0` count.
- **Open:** what `00027e02` makes of `car\s_armory.dat`: it reads the file with `00027e3c`, an `int16` count of records, each an `int16` slot and an `int16` count of `int16` values passed one at a time to `00040ba6` with the object's 6-byte element for the slot.
