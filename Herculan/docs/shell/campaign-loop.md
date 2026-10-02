# The campaign loop

How VSHELL starts a campaign, hands a mission to DBSIM, and folds the result back into the save. **Addresses are in `VSHELL.EXE` except where the text names DBSIM**; the shell's source module is named in the assertion strings for each function cited.

The shell and the simulator are separate processes that never run at the same time. They communicate through loose files in `data\` plus one number: the shell is *relaunched* when the mission ends, and `Shell_BuildScreensAndStart` (`004012b0`), which the startup `Shell_Main` (`00401525`) (`vshell.cpp`) runs once its windows are built, responds to a `-X3` or `-X4` command-line switch by loading slot 10 and running the debrief in place of the intro movies:

```
Game_LoadSlot(10, 0);            // 0040e4f2: load the campaign autosave
Game_ProcessMissionResults();    // 0040eae7: consume results.dat
```

`-X6`, a return from a demo, skips the intro movies too and puts the startup sequence straight up. The number is DBSIM's exit code, which `ES.EXE` passes back as `-X`; the codes and the launcher loop are in [`../command-line.md`](../command-line.md#exit-codes). VSHELL's parser, `FUN_0040107c`, stores `-X<n>` through `Shell_SetExitCode` (`0040876a`) into `0046e210`; `Shell_Main` (`00401525`) copies that into `0048227e` right after the parse, having zeroed it before through `FUN_004073bc(0)`.

## The files crossing between the two binaries

| File | Written by | Read by | Carries |
|---|---|---|---|
| `data\mission.var` | shell, `MissionVar_Write` (`0040e9cb`) | DBSIM | the 2000-byte campaign flag array |
| `data\player.mec` | shell, `Game_ExportMissionHandoff` (`0040f0d4`) | DBSIM | the player's HERC, wingmen and their fits |
| `data\script.dat` | shell, `WriteScriptDatFile` | DBSIM | the mission itself |
| `data\mission.var` | DBSIM, `Mission_WriteResults` (`0042412c`) | shell, `MissionVar_Read` (`0040ea59`) | the same flags, mutated by the mission |
| `data\results.dat` | DBSIM, `Mission_WriteResults` | shell, `Game_ProcessMissionResults` (`0040eae7`) | outcome, salvage, damage and per-pilot counters |

`mission.var` appears twice deliberately: it is a round trip. The shell writes the flag array out before launch and reads the same file back at debrief.

### The campaign flag array is the `.msn` condition store

`maybe_CampaignFlagArray` (`00482af8`, 1000 `int16`) is the same array three separate systems touch:

- the `.msn` condition/trigger opcodes compare against it while filtering record arrays at load ([`../formats/msn-mission-file.md`](../formats/msn-mission-file.md));
- it is persisted in every save slot, immediately after the salvage pool ([`../formats/save-games.md`](../formats/save-games.md));
- it is the entire content of `data\mission.var`, in both directions.

Before a mission is loaded, `MsnGen_SeedCampaignFlags` (`0040e94e`) writes flags 1 to 6: a training load clears the array first, a campaign load writes flags 1 and 2 as the career position's stage and mission, and both write flag 3 from `00482606` ([Open](#open)) and flags 4, 5 and 6 a draw below 12 each. The mission's conditions then compare against them, and its header patch clears the flags it names ([`../formats/msn-mission-file.md`](../formats/msn-mission-file.md#the-header-patch--row-2)).

The debrief writes three slots from its own accounting: 0 the mission's outcome code (`_maybe_CampaignFlagArray = DAT_00482ae9`), 9 the pilots lost and 8 the machines scrapped ([below](#the-debrief--game_processmissionresults-0040eae7)). On the simulator side the same array is `DAT_004a9ef4`, which DBSIM reads from `mission.var` at mission load, less a few slots it resets, and writes back at mission end — see [`../simulation/mission-deployment.md`](../simulation/mission-deployment.md).

It is also what the campaign's rewards are keyed on: the debrief tests the flags the mission left to unlock chassis and weapons and to stock weapon units ([below](#the-debrief--game_processmissionresults-0040eae7)).

## Starting a campaign — `Game_NewCareer` (`0040e2ed`)

Takes the pilot name and the skill (`DAT_0048260c`'s mode decides the rest: 1 campaign, 0 training, the mode the practice missions and `INSTANT ACTION` run in; both of those pass the literal `TRAINEE` and the practice screen's difficulty; the campaign's is [the registration screen](screen-layout.md#the-registration-screen)). It sets the game state (`0048260e`) to 2, clears the briefing and debrief movies' once-per-load flags and the campaign flag array (`CampaignFlags_Clear`, `0040e939`), loads `gam\weapons.dat` (which also empties the build queue), generates the pilot roster, builds the player (`Player_Create`, `00410107`: roster id 0, a name index drawn below 11, the skill, bay 0, squad position 0, on strength, one machine on strength, and the three squad-member pointers, each squad's record 0 through `Squad_TakeMember` (`0040fb4f`) — [`../formats/save-games.md`](../formats/save-games.md#pilot-record--59-bytes-0x3b-in-memory)), loads `gam\hercs.dat` in a campaign and nothing in training, initializes the career position — whose last step starts the mission's load ([`screen-layout.md`](screen-layout.md#starting-a-practice-mission)) — and seeds the salvage pool:

```
DAT_00482af4 = rand(0..10) * 1000 + 100000;
```

The pool is in kilograms and every screen divides by 1000 to print tons ([`armory.md`](armory.md#one-currency-two-units)).

**A training career keeps three things from the game before it.** Nothing in `Game_NewCareer` writes the career block past the position, the chassis availability flags or block 11 ([`../formats/save-games.md`](../formats/save-games.md#savgame_sav--block-order)). A campaign's own steps overwrite the first two: `Registration_OnAccept` calls `LoadHercInfDat` first, and the mission load's `Career_SetBriefing` writes the career block's text ([Loading the career's mission](#loading-the-careers-mission)). A training career runs neither, so it carries the text, the briefing movie id and the chassis flags of whatever game the shell last loaded or started. With none since the startup, it carries the startup's zeros and `gam\herc_inf.dat`'s flags, which the startup (`Shell_InitGameState`, `0040e17e`) loads. Both modes carry block 11 the same way. Retail `GAME_T.SAV` is such a career: `herc_inf.dat`'s flags, zero text and a zero block 11.

The two catalog loads also stock the player: `gam\weapons.dat`'s trailing block gives the armory 39 weapon units ([`../formats/weapons-dat.md`](../formats/weapons-dat.md#file-level-format)) and `gam\hercs.dat` puts four Outlaws and one part-built Razor in the hangar ([`../formats/herc-catalogs.md`](../formats/herc-catalogs.md#gamhercsdat--the-starting-hangar)).

### The pilot roster is generated, not authored — `Squad_GenerateRoster` (`0040fa31`)

Three squads of twelve. For each squad the routine draws two independent shuffles (`Util_Shuffle` (`0040f95c`), a Fisher-Yates over 4 and over 3), and pilot `[row][col]` takes:

```
rosterId = shuffle4[col] + squad*4          // 0-11, stored at pilot +0x00
nameIndex = shuffle3[row] + rosterId*3      // 0-35, stored at pilot +0x02
```

The name string itself is copied out of `esnames.bin` at that index. That file holds exactly 36 names — `DUGGAN`, `BRUTUS`, `BUTCHER`, `RIGGS` … `HOYLE` — so the index space is used exactly once over, and every pilot's name is unique within a run. Skill tier comes from `Pilot_DrawSkill` (`0040f9dc`), a draw below 100 against the thresholds at `0046f5ec` — 70, 85, 95, 100 — so a pilot draws skill 0 seven times in ten. The draws run squad by squad: the shuffle of four, the shuffle of three, then twelve skill draws, row by row.

### The shell's generator

Every draw the campaign makes goes through one generator at `0x482325`: `ShellRandom_Below` (`004659ec`) is `(ShellRandom_Next() & 0x7fff) % n`, and `ShellRandom_Next` (`004659a8`) is DBSIM's own additive lagged Fibonacci step ([`../simulation/random-generator.md`](../simulation/random-generator.md)). It is seeded once, at startup (`004075a1`): `ShellRandom_Seed` (`0046597c`) copies the same 112-byte table DBSIM starts from, byte for byte, out of `0047f7b4`, sets the same two cursors, and steps it `GetTickCount() & 0x7f` times. So a session starts at one of 128 states, and everything after follows from the order of the draws.

The briefing map keeps a second state at `0x48106e`. `HeightGrid_FromBitmap` (`00428d5b`) and `HeightGrid_Load` (`00429010`) call `ShellRandom_Next` on it directly for their `& 0xfff` material rolls ([`mission-map.md`](mission-map.md)), and a static initialiser (`00401e4c`, listed at `00480fb2`) resets it to the seed table through `ShellRandom_Reset` (`00465950`) without the tick-count stepping ([Open](#open)).

## The campaign table — `gam\career.dat`

Loaded once by `LoadCareerDat` (`00412906`), which first opens `missions.bin` as a string table and keeps the handle in `0046fb34`. A flat stage table, verified byte-exact against the retail 148-byte file:

```
int16   stageCount -- 6
per stage:
  int16   campaign index
  int16   missionCount
  int16 x missionCount -- name indices into missions.bin
```

`CareerDat_ReadStage` (`00412864`) resolves each index through `missions.bin` as it loads, so the file stores indices rather than names. In memory a stage is 8 bytes: the campaign index, the count, and a pointer to the resolved strings. Retail's six stages:

| Stage | Campaign index | Missions | Contents |
|---|---|---|---|
| 0 | 5 | 11 | `TRAIN1`–`TRAIN8`, `DEMO`, `DEMO_01`, `DEMO_02` — the practice missions, then the demos |
| 1 | 0 | 10 | `C1_01`–`C1_10` |
| 2 | 1 | 10 | `C2_01`–`C2_10` |
| 3 | 2 | 10 | `C3_01`–`C3_10` |
| 4 | 3 | 10 | `C4_01`–`C4_10` |
| 5 | 4 | 10 | `C5_01`–`C5_10` |

Stages 1–5 are the five campaign chapters. Stage 0 holds the eight practice missions in the order the practice screen lists them, then the three `INSTANT ACTION` plays in turn. `Career_SeedPosition` (`00412a2f`) seeds a new career at stage 1 mission 0, and a training-mode one at stage 0 on the mission the practice screen selected, `DAT_00479bb8` ([`screen-layout.md`](screen-layout.md#which-mission-a-row-is)). The chapter numbering also explains the two bounds in the advance below: `stage > 4` means "already in the final chapter", and the campaign is complete once the stage index reaches the stage count of 6.

The names in `missions.bin` carry their directory — `MSN\C1_01.MSN` — so they are paths ready to open, not bare mission names.

The player's position is the pair `(0046fb18, 0046fb1a)` — stage, then mission within stage — which is the first thing in the save's career block. Retail `GAME_T.SAV` sits at `(0, 4)`, on `Strike Training Mission`.

## Loading the career's mission

`Career_LoadCurrentMission` (`0044d4cc`) loads the mission at the career position: the stage's resolved name for that mission, passed to `MsnGen_LoadMission` (`0041c73d`). A training career takes the half [`screen-layout.md`](screen-layout.md#starting-a-practice-mission) describes. In a campaign:

1. `MsnGen_SeedCampaignFlags` writes flags 1 and 2 as the stage and the mission ([above](#the-campaign-flag-array-is-the-msn-condition-store)), and the mission is parsed against the career's flags ([`../formats/msn-mission-file.md`](../formats/msn-mission-file.md)).
2. The header takes the stage's campaign index as its theater, 0 in both cheat fields and the player pilot's skill as its difficulty ([`../formats/script-dat.md`](../formats/script-dat.md#the-training-fields)), and `WriteScriptDatFile` writes `data\script.dat`.
3. `Career_SetBriefing` (`00412ece`) is handed row 4's first record by value. It copies the objective, briefing and intelligence arrays into the career block, counting the entries that are not `-1`, and the record's row-3 value into `004840b8`, the briefing movie ([`../formats/save-games.md`](../formats/save-games.md#career-block--152-bytes)).
4. `Squad_SetPositionsInPlay` (`004102ff`) sets the positions in play from the mission's group 0. No machine is built: a campaign flies its own hangar.
5. `Game_ExportMissionHandoff` writes `data\mission.var` and `data\player.mec` ([below](#launching-a-mission--game_exportmissionhandoff-0040f0d4)).

`Career_LoadCurrentMission` then rebuilds the briefing's map (`ShellMap_Build`, [`mission-map.md`](mission-map.md)) and the career's assembled text (`Career_BuildBriefingText`), and stages the slot-10 summary (`Stats_StageCurrentGame(10)`). It then puts the mission tab back up in the view it holds, which after a debrief is 4, and posts a press to the tab's button (`Mission_ShowView(MissionScreenView, 1)`, [`screen-layout.md`](screen-layout.md#the-three-views)).

Every campaign save in the retail install is this path's output. Each of `GAME_0`–`GAME_6` gives back its slot's `script%d.dat`, `missn%d.str`, career-block text and flag array byte for byte. That happens from its own flags with flag 0 holding 1, a won mission's outcome, which the load's header patch then clears. The generator is 21 to 94 steps past its seed table, within the 0 to 127 a freshly started shell's seeding leaves it at.

## Launching a mission — `Game_ExportMissionHandoff` (`0040f0d4`)

1. `MissionVar_Write` (`0040e9cb`) writes the flag array to `data\mission.var`.
2. `_remove` deletes the previous output file.
3. Opens the export and writes `int16 0` and the squad count (`00482a7a`), then one entry per machine via `PlayerMec_WriteEntry` (`004106b7`): the player's first, then, for each position from 1 up to the positions in play (`00482a78`), the squad member at that position if its on-strength byte (`+0x24`) is set, each preceded by two fields taken from its pilot.

This is `data\player.mec`, and the record it emits is the one DBSIM's reader (`DBSim_LoadScriptDat`, `00424308`, and `DBSim_SpawnMissionObjects`, `004253d8`) takes. Read from each side independently, writer and reader agree field for field. In file order, every field an `int16` but the last:

| Field | Written from |
|---|---|
| *(file header)* player entry index | literal `0` — the player is always entry 0 |
| *(file header)* entry count | `00482a7a` |
| pilot name index | the pilot's `esnames.bin` name index (pilot `+0x02`). DBSIM reads the same field as an index into `str\PILOTS.STR` — [`heads-down-display.md`](../formats/heads-down-display.md#who-is-in-it) |
| skill | the pilot's skill tier (pilot `+0x25`) |
| mech type | HERC record `+0x00` |
| slot count | HERC record `+0x4c`, the mount capacity; both arrays below are this long |
| weapon ids, one per slot | the mounted unit's id — **`0` when the hardpoint is empty** |
| ammunition types, one per slot | the unit's `+0x08` — **`5` when the hardpoint is empty** |
| one field | literal `0` |
| condition block, 26 + 20 + 20 bytes: external, internal and hardpoint conditions | one contiguous 66-byte span, HERC record `+0x08`–`+0x49`, the same bytes the save stores for that machine |

The player's own entry reads its two leading fields from `00482a7e` and `00482aa1`, which are the same two pilot fields reached directly: the player structure at `00482a78` embeds its pilot record at `+0x04`, putting the name index at `00482a7e` and the skill tier at `00482aa1`.

The `5` filler that [`../simulation/weapon-mounts.md`](../simulation/weapon-mounts.md) observes in every non-launcher slot is written here — it is this function's literal default for a hardpoint with no unit mounted. Where a unit *is* mounted the field is that unit's own ammo type, which the armory and the weapons screen set ([`../formats/herc-catalogs.md`](../formats/herc-catalogs.md#the-weapon-unit-record)), so `5` reaches the file by two routes and means the same thing on both.

### The trailing weapon table

After the last entry the export writes one more block, `PlayerMec_WriteUnlockTable` (`00412253`):

```
int16   33 (0x21), written as a literal
33 x byte   weapons.dat record +0x16, one per catalog id, walked at the 29-byte stride
```

35 bytes, and they close the gap in the retail sample: a 263-byte `player.mec` has only 228 bytes of entries, which leaves exactly 35 unaccounted for. They are this table, not slack — and an export cannot carry a stale tail anyway, since the export and copy streams open with `_open(path, 0x8301, 0x180)`, `O_BINARY|O_CREAT|O_TRUNC|O_WRONLY`. `FileRWStream_Open` (`0044e46c`, `0x8102`) skips `O_TRUNC`; it opens the save files ([`../formats/save-games.md`](../formats/save-games.md)), `data\script.dat` (`WriteScriptDatFile`) and `data\mission.str` (`MissionStr_Write`, `004179f0`).

The byte is the weapon's unlock flag, the same one every save slot stores for all 33 catalog ids ([`../formats/weapons-dat.md`](../formats/weapons-dat.md)).

**No reader of this table is known** ([Open](#open)). Besides the export, three VSHELL functions reference a `data\player.mec` literal: `Career_SaveSlot` (`00412a71`) and `Career_LoadSlot` (`00412bbf`) copy the file whole to and from `sav\player%d.mec`, and `ShellMap_ReadSquadHeader` (`00424db0`), the briefing's map ([`mission-map.md`](mission-map.md#the-squads-positions)), reads only the leading two `int16`, the player entry index and the squad size, before closing it and moving on to `data\mforms.dat` for formation layout. In DBSIM, `DBSim_LoadScriptDat`'s reader stops at the last entry, `Sim_ReadLoadingScreenChoice` (`004613b4`) reads the first five `int16` for the loading screen, and `Sim_ParseCommandLine` (`0045e73c`) packs the file whole into an input tape under `-r` and unpacks it under `-p` ([`../formats/tap-input-tape.md`](../formats/tap-input-tape.md#the-bundle)). The save file, not the export, is where the flags are authoritative: the export is regenerated from it at every launch, so the copy here is duplicated state.

Both path strings are referenced as bare addresses (`0046f511`, `0046f521`), so the decompile does not show their text; read out of the binary they are two separate copies of the same literal, `data\player.mec`. The function removes that file and immediately recreates it.

The whole layout is verified byte-exact against every retail `player.mec` — the loose `DATA` copy and all nine `sav\player%d.mec` — 143 to 467 bytes, 1 to 4 entries each, every one consuming to the last byte with the 33-flag table present.

## The debrief — `Game_ProcessMissionResults` (`0040eae7`)

The longest function in `game.cpp` and the whole of the post-mission accounting.

1. Read `data\mission.var` back into the flag array.
2. Open `data\results.dat` and read: `int16` outcome to `00482ae9`, an `int32` added to the salvage pool, then `int16 count` and that many `{ int16, int16 }` salvage pairs, each applied by `Armory_AddNewUnit` (`0041229d`) in campaign mode only.
3. Copy the outcome into flag slot 0.
4. For the player, then for each on-strength squad member at positions 1 up to the machines-on-strength count `00482a7a` — not the positions in play the export walks: when the pilot's bay holds a machine, read its 66-byte status block over its `+0x08` span (`Herc_ReadStatusBlock` (`00411720`), which also destroys any mount whose condition arrived at 0 and sets that hardpoint back to 100); set the pilot's condition from the machine's overall slot (`HercStatus_Get(block, 1, 9)` (`00411d06`)), or to 100 with no machine; read the pilot's three mission counters and accumulate them (`Pilot_AccumulateMissionStats`, `0041000e`); and settle the machine (`Herc_SettleAfterMission`, `00410c7c`). A machine whose mean condition (`HercStatus_OverallCondition`) is below 30 is scrapped as the shell's scrap is — valued, its mounts at 80 or better returned to stock, the bay emptied — with the value going into the pool and one added to the hangar's `+0x24`, `00482ae7`; the pilot keeps the bay. A hangar load zeroes that word ([`../formats/herc-catalogs.md`](../formats/herc-catalogs.md#gamhercsdat--the-starting-hangar)), and the debrief began by loading slot 10, so it counts the machines this debrief scrapped. Any other machine has its overall slot reset to 100. A pilot with no machine reads no status block, so the file's 66 bytes for that machine are read as its counters.
5. Run the squad's progression ([below](#pilot-progression)) and copy the count of pilots it lost into flag 9 (`00482b0a`). If the player's HERC condition reached 0 the campaign is over and the debrief stops here, in either mode. Otherwise flag 8 (`00482b08`) takes the scrap count, and the rest runs in a campaign only; a training debrief ends by putting the startup sequence up, which brings the main menu.
6. Deliver the weapon build queue and charge it (`Game_DeliverWeaponQueue` (`0040f324`), through `Armory_DeliverQueue`), then deduct repairs (`Game_AutoRepairSquad` (`0040e804`), [`armory.md`](armory.md#repair-levels)), then advance every unfinished chassis in the eight bays one mission (`HercList_BuildTickAll` (`00410a2b`), through `Herc_BuildTick`).
7. Grant from the flags the mission left: chassis through `Herc_GrantUnlocks` ([`../formats/herc-catalogs.md`](../formats/herc-catalogs.md#chassis-unlocks--herc_grantunlocks-004118c5)), then weapon unlocks and weapon units through `Armory_GrantCampaignWeapons` ([`../formats/weapons-dat.md`](../formats/weapons-dat.md#campaign-grants--armory_grantcampaignweapons-004126be)). Both clear the unlock slots they test, so a save written after the debrief holds them at 0.
8. Reconcile the build queue with the pool (`Armory_RefreshQueue`, [`armory.md`](armory.md#scrapping)), write the report texts (`Debrief_WriteReport` (`0040f34c`), handed the salvage-pair count plus the units just granted), then advance the campaign and set the next screen from the result.

Written out, the file is:

```
int16   outcome
int32   salvage awarded to the pool
int16   salvageCount
        salvageCount x { int16, int16 }
per machine, the player's first then each on-strength squad member in order:
  66 B    the HERC status block, read straight over the record's +0x08 span
  int16   counter A   -> pilot +0x2d
  int16   counter C   -> pilot +0x31
  int16   counter B   -> pilot +0x2f
```

DBSIM's `Mission_WriteResults` writes it, and what fills each field is [`../simulation/mission-objectives.md`](../simulation/mission-objectives.md#what-the-mission-leaves-the-shell--mission_writeresults-0042412c)'s: the machine blocks are the player's group in the simulator's order, which is `player.mec`'s, player first. The retail `data\results.dat` walks exactly: 152 bytes = the 8-byte header, no salvage, and two 72-byte machine blocks — matching the two entries in the `player.mec` beside it.

### Pilot progression

After the results stream is closed the debrief calls `Squad_ProgressAll(00482a78)` (`004103ba`), the squad-wide progression pass. It runs `Pilot_Progress` (`00410066`) on the player when the player's condition is non-zero, then walks positions 1 up to the positions in play: an on-strength squad member whose condition is non-zero is progressed, and one at zero is replaced. `Squad_TakeMember` (`0040fb4f`) makes the squad's next record by its cursor the member and resets it to the pilot defaults — no bay, off strength, no position, condition 100, every counter 0 — and the machines-on-strength count drops by one. The pass returns how many pilots were lost, the player counted when at zero, for the report.

`Pilot_Progress(pilot, isPlayer)` does nothing for a pilot whose on-strength byte `+0x24` is clear. Otherwise it advances two independent ladders, both capped at 3 and both keyed on the pilot's counters ([`../formats/save-games.md`](../formats/save-games.md#pilot-record--59-bytes-0x3b-in-memory)):

```
divisors = (pilot +0x27 == 0) ? { kills: 20, missions: 10 }
                             : { kills: 10, missions: 15 };
if (pilot +0x25 < 3 && !isPlayer && (pilot +0x33 + pilot +0x35) % divisors.kills == 0)
    pilot +0x25 += 1;                 // skill
if (pilot +0x29 < 3 && pilot +0x39 % divisors.missions == 0)
    pilot +0x29 += 1;                 // rank
```

Two consequences worth holding onto:

- **The player's skill never changes.** The player is the one call site passing `isPlayer = 1`, and the `jnz` at `0041009f` jumps clear of the skill block on that argument — so the level chosen on the registration screen stands for the whole career, while the player's rank still advances on missions flown. That level is also **the simulator's difficulty setting**, handed over in every `script.dat` the campaign writes — see [`../simulation/difficulty.md`](../simulation/difficulty.md).
- **Skill counts machines only.** The test sums Herc and Flyer kills and ignores Base kills entirely.

The counters have already been accumulated and `+0x39` already incremented by the `Pilot_AccumulateMissionStats` (`0041000e`) loop above, so a pilot's first debrief tests `missionsFlown == 1`. The kill test has no such offset: a squad pilot whose Herc and Flyer totals are still `0` satisfies `0 % divisor == 0` and takes a skill step on every mission survived until the cap.

That last point is confirmed in the instruction stream — the sum is never tested, only the remainder:

```
004100a1  0f bf 41 33       movsx eax, word [ecx+0x33]   ; career Herc kills
004100a5  0f bf 51 35       movsx edx, word [ecx+0x35]   ; career Flyer kills
004100a9  03 c2             add   eax, edx               ; the sum -- never examined again
004100b0  99                cdq
004100b1  f7 fb             idiv  ebx
004100b3  85 d2             test  edx, edx               ; the remainder, and nothing else
004100b5  75 04             jnz   004100bb
004100b7  66 ff 41 25       inc   word [ecx+0x25]        ; skill++
```

Eight jumps in the function resolve to four targets — `00410088`, `00410090`, `004100bb`, `004100d9` — and the instruction-length chain from the entry point hits all four, ending at the `ret` at `004100dc`, so the decode is bounded on every branch.

### Where the debrief goes next

`Career_Advance` (`00412dc7`) first reloads the mission at the career position for its debrief text (`Msn_LoadDebrief` (`0041d2c3`), [`../formats/msn-mission-file.md`](../formats/msn-mission-file.md#row-5--the-debrief)). Its conditions see flags 8 and 9 as step 5 left them and the simulator's flag 10, which pick [the debrief's loss line](../formats/msn-mission-file.md#the-debriefs-loss-line). That reload draws from the shell's generator for the mission's random conditions, as the mission's own load did, so the next load starts that many steps further on. It then advances `(stage, mission)` and returns the branch, which lands in `0048260e`:

| Condition | State | Effect |
|---|---|---|
| player's HERC condition reached 0 | 3 | campaign over: [`REPLAY MISSION?`](#replay-mission) |
| advance returned 0 — the mission failed and the new position is a stage's first mission or past stage 4 | 0 | the war is lost: [`REPLAY MISSION?`](#replay-mission) |
| advance returned 1 — succeeded past the last stage | 1 | autosave to slot 10, play AVIs `0x53` and `0x54`, then palette 1 and the startup sequence |
| otherwise | 2 | the debrief: the mission tab's view set to 4, then the next mission's load, which puts the tab up in that view with [the mission report](screen-layout.md#the-mission-report) ([`screen-layout.md`](screen-layout.md#the-three-views)) |

**The position advances whether or not the mission was won.** `Career_Advance` increments the mission index before it inspects the outcome; the outcome only chooses the branch. Rolling past a stage's mission count resets the mission index to 0 and increments the stage.

Two scripted events are hard-coded into the advance, keyed on the position *after* it increments, and only on the branch that goes on to the next mission:

- Stage 1 mission 3 calls `Player_SetBay(4)` (`0040e6c8`), which moves the player into bay 4.
- Stage 1 mission 6 calls `Hangar_WithdrawChassis(8)` (`0040e7cd`), which takes the first Razor out of the hangar. `HercList_RemoveFirstOfType` (`00410bbe`) strips its mounts into stock as a scrap does, empties the bay for no salvage and returns its index, and the pilot in that bay loses it (bay `-1`); the pilot's on-strength byte is left alone. With no Razor in the hangar `HercList_RemoveFirstOfType` (`00410bbe`) returns what its last probe read, bay 7's chassis type or `-1`, and the pilot in the bay of that number loses it instead.

The autosave, `Game_SaveSlot(10, NULL)` (`0040e37b`), has five call sites: `Shell_Main` (`00401525`) once its main loop ends, which includes every launch of a mission; `Game_ProcessMissionResults` on the branch that completes the campaign; `SaveScreen_OnRestore` after a restore; `TabHandler_MainMenu` on the way to the main menu; and `ReplayDialog_OnNo`. `SaveScreen_OnAccept` is the sixth call, saving the selected slot instead. This is why `GAME_R.SAV` mirrors the newest ordinary save.

### Replay mission?

States 0 and 3 put up `REPLAY MISSION?` (`ReplayDialog_Show(state)`, `0044ca57`), built once at startup by `ReplayDialog_Build` (`0044c71c`): the shell's backdrop over the whole display, and on it a titled panel with two lines and two buttons. State 0's lines are `The war is lost. Do you` / `want to replay the mission?` and state 3's `You were killed. Do you` / the same.

- **Yes** (`ReplayDialog_OnYes`, `0044cb44`) hides the dialog, loads slot 10 again, sets exit code 2 and ends the shell. Slot 10 is the autosave the shell wrote on its way out to the mission, so the debrief's accounting is thrown away and the same mission flies again from `data\` as `Career_LoadSlot` leaves it: the slot's `script.dat`, `mission.str` and `player.mec` copied in, beside the `mission.var` the simulator wrote as the failed attempt ended. Neither the load nor the handler writes that file, so the replay starts from the counters the last attempt left, less the slots the simulator's load zeroes.
- **No** (`ReplayDialog_OnNo`, `0044cbbd`) saves slot 10 — the game as the debrief left it, in state 0 or 3, which `CONTINUE GAME` then answers with `END OF GAME` — hides the dialog and shows the main menu.

## Rejected readings

| Reading | Why it is wrong |
|---|---|
| `-r` relaunches the shell into the debrief. | The usage text says so (`"-r -R Returning from sim"`), and the parser's `-r` case does store 3 in `0048227e`. `Shell_Main` (`00401525`) overwrites `0048227e` with the `-X` value at `004015af`, straight after the parse returns (the stack cleanup and the `Shell_GetExitCode` call come between), so `-r` has no effect: only `-X3` and `-X4` reach the debrief. |

## Open

- **Open:** what writes `00482606`, which `MsnGen_SeedCampaignFlags` copies into flag 3 before every mission load. Its one reference found, by a disassembly search and `es2_xref.py`, is that read; the startup memset from `MissionScreenView` clears it, so a load with nothing else writing it sees 0.
- **Open:** whether anything steps the map generator at `0x48106e` from the clock. `es2_xref.py --binary VSHELL 0048106e` finds the static initialiser's reset (`00401e5c`) and the two draws, and nothing else.
- **Open:** a reader of `player.mec`'s trailing weapon table. None is among the functions that reference a `player.mec` path literal in `VSHELL.EXE` or `DBSIM.EXE` (a byte scan for the strings and for absolute references to them); a path built another way would escape that scan.
