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

A campaign's `+0` word is the *k* of its talking-head files `C<k>_TEXT.BIN` and `D<k>_TEXT.BIN` ([the kinds](../shell/message-box.md#the-kinds)).

`PilotNames_Load` reads `car\names.dat` from `SHELL0.VOL`: a count byte of groups, then per group a count byte and that many names, each read by `Stream_ReadCString` into a 16-byte buffer with a bound of 16, into an array of name arrays at `0007ebc4`. The retail file holds twelve groups of three, each a name and the name followed by 1 and 2 (`DUGGAN`, `DUGGAN 1`, `DUGGAN 2`; `RIGGS`, …). `PilotNames_Free` frees twelve groups of three names, whatever the file held.

### Which battle set

`BattleSet_Load` loads the battle set whose index is the word `+5` of the roster at `0009b218` (`0009b21d`). `es2_xref.py` finds three calls to it:

- `Shell_LoadData` (`00020630`), which also loads the shell's fonts and `eng\watext.bin`, after it loads the [careers log](career-log-and-player-mec.md#loading) `Career_Log` (`0009b20c`) with `CareerLog_Load` (`00017e7f`);
- `BattleSetDialog_OnMessage` (`0002bb29`), the handler of screen 1330, `BattlesetAlert` ([the screens](../shell/menus.md#the-screens)), which on message 100, its ACCEPT button, stores in the roster's word `+5` the selected line of its list box 1331 (`0x533`, the control's slot `+0x2c`), loads that battle set, and changes the message to `0x514`, 1300;
- `Career_StartMission`, before it builds each mission's path ([a career's next mission](../shell/career.md#a-careers-next-mission)).

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

A career's mission is drawn from the battle set by the [career record](#the-career-record)'s campaign `+6` and position `+8`, each time `Career_StartMission` (`00016e93`) loads the career's next mission ([a career's next mission](../shell/career.md#a-careers-next-mission)).

`Career_BuildMissionPath` (`00015a13`) passes the outcome, `Career_OutcomeChoice`(`000aad58`), the position and the campaign to `BattleSet_GetMissionName` (`000140a5`), formats the name as `msn\%s` into `000aad14`, and sets the text path (`Mission_SetTextPath`). `BattleSet_GetMissionName` takes the position's name for the outcome (`Campaign_GetPositionName`, `000142f6`, through `StrList_Get`). A name containing `.MSN` is returned as it is. Any other name is a list: it opens `bst\<name>`, reads one name list, and copies a name `StrList_PickRandom` draws from it over the position's name, which it returns. `Career_StartMission` reloads the battle set before every build, so a position naming a list draws again for each mission it starts.

`Career_OutcomeChoice` (`000159e9`) is 1 when the outcome word exceeds the word of the table at `0007ec80` that the word `000aad5e` indexes, and 0 otherwise. The table holds 4, 4, 4, 3, 3, 3, 4, 3, 3, 3. For each header word 3 its word is the last ordinal of the debriefing's success bands, so outcome 1 is a mission lost ([which text](../shell/debriefing.md#which-text)); which campaign each outcome leads to is [moving on](../shell/between-missions.md#moving-on)'s. `000aad58` is the first word of `data\results.dat`, the debriefing's ordinal ([which text](../shell/debriefing.md#which-text)), and `Mission_ReadRestore` loads `000aad5e` from the word saved from `Mission_Kind` (`000aad5a`) ([the restore file](eng-mission-text.md#which-file)).

### The career record

A record is `0x1f7` bytes. `CareerRecord_Init` (`000158ff`) sets one up; `es2_xref.py` finds it called by `Career_New` (`000163ac`) for each roster record ([`Career_New`](../shell/career.md#career_new)) and by `MsnGen_LoadMission` for the record at `0009b014`. The roster's records are the career and its backups ([the roster](../shell/career.md#the-roster)).

| Offset | Field | At `CareerRecord_Init` |
|---|---|---|
| `+0` | byte, nonzero for a record in use: the career, and a backup made ([the roster](../shell/career.md#the-roster)) | 0 |
| `+4` | the career's screen ([the career's screen](../shell/between-missions.md#the-careers-screen)) | `0x6a4`, 1700 |
| `+6` | the campaign | 0 |
| `+8` | the position in it | 0 |
| `+0xa` | the missions played | 0 |
| `+0xc` | header word 3 of the mission last started | |
| `+0xe` | word, a pilot entry index | 0 |
| `+0x10` | the pilot count | 4 |
| `+0x12` | word, the pilot entry `CareerRecord_FinishMission` stores its share in | 0 |
| `+0x14` | four `0x25`-byte pilot entries | |
| `+0xa8` | the [equipment state](#equipment-availability), `0x81` bytes | loaded |
| `+0x129` | dword, the career's salvage in kilograms, `Career_Salvage` (`0007fcf0`) as `Career_Save` and `Career_New` store it: 60000 at the start ([the stats](../shell/career.md#the-stats)) | |
| `+0x12d` | 100 `int16`, the shell's flags `000ab244` as the career's saves store them | 0 (`CareerRecord_ClearWords`, `000161ae`) |
| `+0x1f5` | word, the word `Career_AutoRepairMode` (`0007fcf8`) as the career's saves store it | |

The init also zeroes byte `+1`, the word `+2` and the word `000aad58`. For each pilot entry it calls `PilotEntry_Init` (`000151c8`) when the mode word `000aad56` is 0 or the entry is the one `+0xe` names, and `PilotEntry_InitRandom` (`00015228`) otherwise ([pilot entries](#pilot-entries)).

`CareerRecord_Current` (`00014ef4`) is the record the shell works on: `SingleMission_Record` (`0009b014`) in mode 0 and the roster's record `0009b23b` + `0x1f7` × [`0009b21f`] otherwise.

#### Pilot entries

A pilot entry is `0x25` bytes. Making an entry the record's current pilot `+0x12` sets the hangar's selected slot `Hangar_SelectedSlot` (`00080894`, [the HERC lists](car-hercs-and-stock.md#the-herc-lists)) to its slot: `CareerRecord_SelectPilot` (`00015ad4`) selects a given entry, and `CareerRecord_NextPilot` (`00015b0a`) and `CareerRecord_PrevPilot` (`00015b85`) step to the next or the previous entry, modulo 4, that is on strength with a slot, which can be the one they started from. `CareerRecord_CountPilots` (`00015bfb`) counts the entries on strength with a slot, `CareerRecord_FindPilotBySlot` (`00015c39`) returns the first entry with a given slot, or −1, and `CareerRecord_PlayerHasNoHerc` (`000161dc`) is 1 when the player's entry `+0xe` has no slot.

| Offset | Field | `PilotEntry_Init` | `PilotEntry_InitRandom` |
|---|---|---|---|
| `+0x00` | the name's group in `car\names.dat` | −1 | −1 |
| `+0x02` | the name within the group, 0 to 2 | −1 | −1 |
| `+0x04`, `+0x06` | two words | 0, 0 | 0, 0 |
| `+0x08` | the hangar slot of the pilot's HERC, −1 for none | −1 | −1 |
| `+0x0a` | byte, on strength | 1 | 1 |
| `+0x0b` | word | 0 | *d*, a draw below 2 |
| `+0x0d` | the rank | from `+0x17` | from `+0x17` |
| `+0x0f` | the pilot's condition | 100 | 100 |
| `+0x11`, `+0x13`, `+0x15` | a mission's gains to `+0x17`, `+0x19`, `+0x1b` | 0 | 0 |
| `+0x17` | the HERC kills, which the stats show as HERCS: and the rank is read from | 0 | (a draw below 3 + 1) × `+0x1f` |
| `+0x19` | the structure kills, the stats' STRUCTURES: | 0 | a draw below 2 × `+0x1f` |
| `+0x1b` | the flyer kills, the stats' FLYERS: ([the stats](../shell/career.md#the-stats)) | 0 | (a draw below 2 + 1) × `+0x1f` |
| `+0x1d` | word | 0 | *d* |
| `+0x1f` | word | 0 | a draw below 5 + 5*d* |
| `+0x21` | dword, the pilot's pay share | 0 | 0 |

`PilotEntry_InitRandom`'s draws are on `Shell_RandomState` ([the shell's generator](msn-mission-file.md#the-shells-generator)) in the order *d*, `+0x1f`, `+0x17`, `+0x1b`, `+0x19`. Both then call `PilotEntry_ApplyGains` (`00015317`), which, when the byte `+0xa` is set, adds the three gains to `+0x17`, `+0x19` and `+0x1b` and to the words `0009ae5c`, `0009ae5e` and `0009ae60`, and sets the rank to the number of `PilotRankThresholds` (`0007ebd4`: 0, 1, 5, 15 and 30) below `+0x17`, at most 4. `PilotEntry_NameIndex` (`00015f50`) is 3 × `+0` + `+2`, the name's index among the twelve groups of three, and `PilotEntry_Name` (`00015fc0`) the name itself ([`PilotNames_Load`](#battle-sets)). `PilotEntry_Condition` (`0001618d`) returns `+0xf`. A single mission's squad sets its entries otherwise ([the squad](msn-mission-file.md#the-squad)).

## Single missions

`SingleMissions_Load` (`000361ac`) reads `msn\sing_msn.dat`: a byte, the count, into the low byte of the word `00081084`, whose high byte is 0 in the image, then that many name lists into a new array at `00081080` (`SingleMissions_ListCtor`, `0003625e`). Only a count that is not 0 frees the old array and makes a new one, so a count of 0 keeps the lists already loaded. `BattleSet_Load` calls it after `BattleSet_ReadFile`, so `Shell_LoadData`'s call loads the lists at start-up ([which battle set](#which-battle-set)). The retail file holds eight lists: `sm_rcn01`, `sm_rcn02`; `sm_sct01`, `sm_sct02`, `sm_sct50`; `sm_ptl01`, `sm_ptl02`; `sm_rad01`, `sm_rad02`; `sm_scr01`; `sm_sar01`; `sm_ass01`; `sm_ass01`. Which list a single mission draws from is [the SINGLE MISSION menu's](../shell/single-mission.md#starting-the-mission).

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

`Tech_Unlock`, given the state and a mission count *m*, first copies entry `0x2d` of the text table at `0009afa8`, `NONE`, into `0007d7e0`. Its grade test passes unless the mode is 1 and *m* is not 0; then it passes when `Mission_OutcomeGrade` (`00036837`) of the debriefing's ordinal `000aad58` and the mission's kind `000aad5e` is below 2, which by the tables below holds for the ordinals of the success bands, those `Career_OutcomeChoice` takes as outcome 0. A chassis entry that is not yet available and holds no −1 becomes available when *first* ≤ *m* and the grade test passes, or when *second* ≤ *m*; a weapon entry the same with = in place of ≤. Each item made available sets its byte, copies its name into `0007d7e0`, and stores its kind and index at `+0` and `+2`, so `0007d7e0` ends holding the last item's name, or `NONE`. The names are entries `0x1b` + *i* (chassis) and `0xd5` + *i* (weapons) of `0009afa8`, which `Shell_LoadData` (`00020630`) loads from `eng\watext.bin` in `SHELL0.VOL`. `es2_xref.py` finds two calls: `Tech_LoadFile`'s with 0, and `Shell_ProcessMissionResults`' after each mission in mode 1, with the missions played that `CareerRecord_FinishMission` has just counted up ([after a mission](../shell/between-missions.md#after-a-mission)).

`Mission_OutcomeGrade`, given an ordinal and a kind, returns the ordinal's word of the table at `000810f4`, 0, 0, 1, 1, 1, 2, 2, 3, 3, 0, when the kind is 0, 1, 2 or 6, and of the table at `00081108`, 0, 0, 1, 1, 2, 2, 3, 3, otherwise, with no bounds test. The debriefing's talking head takes its line from the same value ([the kinds](../shell/message-box.md#the-kinds)).

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
- **Open:** what lines screen 1330's list 1331 shows, the battle sets `BattleSetDialog_OnMessage` stores the selection of: `Menu_ListLines` takes them from the target of 1331 among its cases at `00081364`, which no reading has followed; and what opens screen 1330.
- **Open:** what reads `TEST_BTL.DAT` and the eleven `ANY_*.DAT` other than `ANY_MSN.DAT`. `BATLSETS.BIN` names only the three `BTLSET_*`, the retail battle sets name only `any_msn.dat`, and a byte search of `GO.EXE` finds neither `test_btl` nor `any_`, where it finds `tech_sng.dat`.
- **Open:** what reads and restores the equipment state besides the init: what reads back the bytes `CareerRecord_WriteWeaponFlags` writes to the [player file](career-log-and-player-mec.md#the-player-file) (`es2_xref.py` finds its one call there), what saves the chassis' bytes, and what reads the state to offer the items; and what calls `Tech_FormatLockedName`, to which `es2_xref.py` finds no branch and no stored pointer.
- **Open:** how the equipment indexes map to the shell's chassis and weapon lists, `MECHS.NAM` and `WEAPONS.DAT`'s 17 templates among them.
- **Open:** what the pilot entry's words `+0x0b`, `+0x1d` and `+0x1f` mean, and `+0x04` and `+0x06` beyond the name column and the name `CareerRecord_ReplaceLostPilots` takes from them ([lost squadmates](../shell/between-missions.md#lost-squadmates)).
