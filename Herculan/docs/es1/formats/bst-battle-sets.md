# GO.EXE — battle sets and mission lists

ES1's shell takes the name of each mission it loads from lists in `ZONES.VOL`. A career plays a battle set's campaigns one mission position at a time, each position naming a mission, or a list to draw one from, for each of two outcomes of the mission before; a single mission is drawn from `msn\sing_msn.dat`. The shell formats the name as `msn\%s` into `000aad14`, the path `MsnGen_LoadMission` opens ([`msn-mission-file.md`](msn-mission-file.md#loading-a-mission)). ES2's shell takes its missions from a campaign table instead ([`gam\career.dat`](../../retail/shell/campaign-loop.md#the-campaign-table--gamcareerdat)). Addresses are `ES1GO` image addresses ([the X-32 executable](x32-executable.md#the-image)).

## Name lists

A name list is a byte, the count *n*, then *n* names, each with its NUL. `StrList_Read` (`000201b7`) reads one from a stream into a 6-byte list: the count into the low byte of the word `+0`, and a pointer at `+2` to *n* new 13-byte buffers, each filled by `Stream_ReadCString` (`00020154`), which reads a byte at a time until it has read a NUL or 13 bytes. A count of 0 allocates nothing and skips the byte after it. `StrList_Get` (`00020254`) returns name *i*, or the only name when *n* is 1, whatever *i* is; `StrList_PickRandom` (`00020289`) returns name `Math_RandomBelow`(*n*) on the shell's generator `0009aec0`; `StrList_Dtor` (`000202b4`) frees the names and the pointer array.

`BST\ANY_MSN.DAT` is one name list of 13 missions: `sm_rcn01`, `sm_rcn02`, `sm_sct01`, `sm_sct02`, `sm_sct50`, `sm_ptl01`, `sm_ptl02`, `sm_rad01`, `sm_rad02`, `sm_scr01`, `sm_sar01`, and `sm_ass01` twice, each with `.MSN`.

## Battle sets

`BST\BATLSETS.BIN` is a string table ([`.BIN` string tables](bin-string-tables.md#the-table)) of three names, `btlset_0.dat`, `btlset_1.dat` and `btlset_2.dat`. A battle set file is a byte, the count of campaigns; then for each campaign a byte, the count of its positions, and for each position a name list.

| Object | Bytes | Fields |
|---|---|---|
| battle set (`000a02cc`) | 6 | `+0` campaign count; `+2` the campaign array |
| campaign | 8 | `+0` the word of the table `0007ea94` for its index; `+2` position count; `+4` the position array |
| position | 6 | a name list |

`BattleSet_Load` (`00013f01`) loads `bst\batlsets.bin`, reads the battle set its entry *index* names with `BattleSet_ReadFile` (`00013fad`), and then loads the single missions' lists (`SingleMissions_Load`, [Single missions](#single-missions)) and calls `00015070`. `BattleSet_ReadFile` frees the set's campaigns (`BattleSet_FreeCampaigns`, `00013f7a`), opens `bst\<name>`, reads the count byte into the low byte of `+0`, makes that many campaigns with `Campaign_Ctor` (`00014075`), reads each with `Campaign_Read` (`0001422c`) and gives campaign *i* the word *i* of the table at `0007ea94`: 0, 1, 2, 3, 1, 2, 3, 1, 2, 3. `Campaign_Read` reads the position count into the low byte of `+2` and, when it is nonzero, frees the old positions, makes new ones with `Campaign_PositionCtor` (`000142c7`) and reads each with `StrList_Read`. `Campaign_Dtor` (`00014355`) and `BattleSet_Dtor` (`00013eac`) free them; `BattleSet_Dtor` also calls `00015153`.

A campaign's `+0` word is the *k* of its talking-head files `C<k>_TEXT.BIN` and `D<k>_TEXT.BIN` ([which file and line](bin-string-tables.md#which-file-and-line)).

### The retail battle sets

`BTLSET_0.DAT` holds eight campaigns. Its campaign 1 has eight positions, each naming two files, the same file twice where one is listed:

| Position | Outcome 0 | Outcome 1 |
|---|---|---|
| 0 | `B0_C1_00.MSN` | |
| 1 | `B0_C1_10.MSN` | `B0_C1_11.MSN` |
| 2 | `any_msn.dat` | |
| 3 | `any_msn.dat` | |
| 4 | `B0_C1_40.MSN` | |
| 5 | `B0_C1_50.MSN` | |
| 6 | `B0_C1_60.MSN` | `B0_C1_61.MSN` |
| 7 | `B0_C1_70.MSN` | |

In `BTLSET_1.DAT`, `BTLSET_2.DAT` and `TEST_BTL.DAT` the position after `B0_C1_10`/`B0_C1_11` names `B0_C1_40`, and none of the three names `any_msn.dat`. All 38 of `ZONES.VOL`'s `any_msn.dat` names are in `BTLSET_0.DAT`.

## A career's missions

The career record is `0x1f7` bytes: the word `+6` is its campaign and `+8` its position in it. `Career_StartMission` (`00016e93`), given a roster and a word *b*, sets the roster's word `+7` to `00016845`(roster, *b*), loads the battle set whose index is the roster's word `+5` into `000a02cc`, builds the path from the record at roster + `0x23` + `0x1f7` × word `+7` with `Career_BuildMissionPath`, loads the mission with `MsnGen_LoadMission`, calls `00015cbb` with the record, copies the word `000aad5a` (header word 3) to the record's `+0xc`, and saves the paths with `Mission_WriteRestore`.

`Career_BuildMissionPath` (`00015a13`) passes the outcome, `Career_OutcomeChoice`(`000aad58`), the position and the campaign to `BattleSet_GetMissionName` (`000140a5`), formats the name as `msn\%s` into `000aad14`, and sets the text path (`Mission_SetTextPath`). `BattleSet_GetMissionName` takes the position's name for the outcome (`Campaign_GetPositionName`, `000142f6`, through `StrList_Get`). A name containing `.MSN` is returned as it is. Any other name is a list: it opens `bst\<name>`, reads one name list, and copies a name `StrList_PickRandom` draws from it over the position's name, which it returns. `Career_StartMission` reloads the battle set before every build, so a position naming a list draws again for each mission it starts.

`Career_OutcomeChoice` (`000159e9`) is 1 when the outcome word exceeds the word of the table at `0007ec80` that the word `000aad5e` indexes, and 0 otherwise. The table holds 4, 4, 4, 3, 3, 3, 4, 3, 3, 3. `000aad58` is the first word of `data\results.dat`, the debriefing's ordinal ([the debriefing](eng-mission-text.md#the-debriefing)), and `Mission_ReadRestore` loads `000aad5e` from the word saved from `000aad5a` ([the restore file](eng-mission-text.md#which-file)).

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

## Open

- **Open:** which missions the career and single-mission paths can reach. A byte search of the seven `VOL` archives and of `GO.EXE` and `DBSIM.EXE` finds `B0_C1_20` and `B0_C1_30` only in `ZONES.VOL`'s directory, and no battle set, name list or `GO.EXE` string names either file ([a file in another layout](msn-mission-file.md#a-file-in-another-layout)).
- **Open:** what calls `Mission_BuildPathFromFiles` and with which pattern: `es2_xref.py` finds no branch to it, and its one stored-pointer hit, `000097e5`, is inside the instruction at `000097e4`.
- **Open:** which battle set a career plays: what sets the roster's word `+5`, what `00016845` returns for `+7`, whether `Career_StartMission`'s roster is `0009b218` (which would make mode 1's flags 1 and 2 the campaign and the position, [the flags](msn-mission-file.md#the-flags)), and `Career_StartMission`'s callers.
- **Open:** what the callers of `Career_AdvanceCampaign` and `Career_PeekNextCampaign` do with the campaign table's −1 and 99, and how the outcome table at `0007ec80` relates to the debriefings' order.
- **Open:** the campaigns of `BTLSET_0.DAT` other than campaign 1, and of `BTLSET_1.DAT`, `BTLSET_2.DAT` and `TEST_BTL.DAT`; what reads `TEST_BTL.DAT`, `TECH_0.DAT`, `TECH_SNG.DAT` and the ten `ANY_*.DAT` other than `ANY_MSN.DAT`. `GO.EXE`'s strings name `tech_0.dat` and `tech_sng.dat`, and no battle set names any of the ten.
- **Open:** what `00015070`, `00015153`, `00015cbb` and `00027e02` do.
