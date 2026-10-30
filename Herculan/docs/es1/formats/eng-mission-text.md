# GO.EXE — .ENG mission text

Each ES1 mission's briefing, objective list and debriefings are in a `.ENG` file beside its `.MSN` in `ZONES.VOL`'s `MSN\` folder. The shell, `GO.EXE`, shows the briefing on the war map, writes the objectives to `data\object.str` for the simulator, and shows one of the debriefings after the mission, picked by how the mission went. A piece of text can vary with a campaign flag. The file shares only its name with ES2's `.ENG`, a table of id-keyed lines that the mission load reads ([ES2's `.ENG`](../../retail/formats/msn-mission-file.md#the-eng-string-table)): ES1's is a set of tagged text blocks, and its mission load reads none of it ([`msn-mission-file.md`](msn-mission-file.md)). Addresses are `ES1GO` image addresses unless `ES1DBSIM` is named ([the X-32 executable](x32-executable.md#the-image)).

## Which file

`Mission_SetTextPath` (`000364be`) copies the mission's path from `000aad14`, `msn\<name>.msn`, to `000aad2a` and replaces everything from its first `.` with `.eng`. The three functions that build the mission path call it after their `sprintf` (`00015a13`, `00036649`, `000366c1`). So `B0_C0_0A.MSN`'s text is `B0_C0_0A.ENG`.

`Mission_WriteRestore` (`0003634b`) saves both paths to `data\restore.dat` (file-name entry 8 of `cfg\filename.bin`, assert at `mission.cpp` line `0x55`): the mode word `000aad56`, the word `Mission_Kind` (`000aad5a`), then each path as an `int16` length counting its NUL and the path with its NUL, the `.msn` path first. `Mission_ReadRestore` (`00036512`, assert at line `0x75`) reads them back in that order into `000aad56`, `000aad5e`, `000aad14` and `000aad2a`: the second word lands in `000aad5e`, not in the `000aad5a` it was written from. `MsnGen_LoadMission`'s two callers (`Career_StartMission`, `00016e93`; `SingleMission_Start`, `00036807`) call the writer; `Shell_OpenFirstScreen` (`0003ba47`) calls the reader.

Of the 79 files, 77 belong to the 77 `.MSN`. `B4_C1_20.ENG` and `B4_C1_30.ENG` have no `.MSN` and are byte for byte `B0_C1_20.ENG` and `B0_C1_30.ENG`, the texts of the two missions [in another layout](msn-mission-file.md#a-file-in-another-layout). `SM_RCN03.ENG` is byte for byte `SM_RCN02.ENG`.

## The layout

All values are little-endian. The file is an `int16` version, 1 in every file, and then blocks to the end of the file:

| Bytes | Field |
|---|---|
| 4 | the tag: `BRF:` the briefing, `OBJ:` the objectives, `DEB:` a debriefing |
| 4 | `int32`, the count of the bytes that follow, to the block's last byte |
| 2 | `int16`, the sum of the lengths of the block's literal pieces |
| 2 | `OBJ:` only: `int16`, the count of its pieces |
| … | pieces, each led by a byte: 1 a literal, 2 a choice |
| 1 | `0x10`, the end |

Every file has one `BRF:` block, first, then eight or nine `DEB:` blocks, and one `OBJ:` block in 72 files:

| Files | Blocks |
|---|---|
| 39 | `BRF` `OBJ` 8 × `DEB` |
| 25 | `BRF` `OBJ` 9 × `DEB` |
| 5 | `BRF` 8 × `DEB` |
| 5 | `BRF` 8 × `DEB` `OBJ` |
| 3 | `BRF` 9 × `DEB` `OBJ` |
| 2 | `BRF` 9 × `DEB` |

**A literal piece** is byte 1, an `int16` length counting the NUL, and the text with its NUL.

**A choice piece** picks one of several texts by the value of a [campaign flag](msn-mission-file.md#the-flags):

| Bytes | Field |
|---|---|
| 1 | 2 |
| 2 | `int16`, the flag's index |
| 2 | `int16` *n*, one more than the count of texts |
| 2 × *n* | `int16` offsets: of each text, and last of the end of the texts, each from the byte after the ranges |
| 4 × *n* | *n* pairs of `int16`: the lowest and highest value of the flag that picks each text, and last a pair the loader does not test |
| … | the texts, each an `int16` length counting the NUL and the text with its NUL |

In the retail files the texts follow one another from offset 0, the last offset is where the texts end, and the untested pair is 0, 0. Choices appear in `BRF:` and `DEB:` blocks only, 197 of them, with *n* from 2 to 9, on 23 flags.

The pieces of a block are one text: the literal pieces end mid-sentence where the author cut them, and a piece's text runs on from the last character of the one before it.

## Reading a block

`Dialog_LoadConditional` (`00018cdb`) loads a block into the shell's dialog record, `Shell_Dialog` (`0009afc8`), for both the briefing and the debriefing ([the dialog record](../shell/message-box.md#the-dialog-record)). It deletes the box it held (`Dialog_DeleteBox`, `00018d93`) and its stream, opens the file through `VolRStream_CtorFromPath`, and reads the version. When it is 1 the box is a `ConditionalDialogBox` built from the block; any other version leaves no box. It closes the stream.

`ConditionalDialogBox_Ctor` (`00019453`) takes the block's ordinal among the blocks of its tag and the tag:

1. `Stream_FindNthTag` (`0004b6d6`) finds the tag's occurrence of that ordinal, counting from the byte after the version. It calls `Stream_FindTag` (`0004b658`) once more than the ordinal. `Stream_FindTag` reads a byte at a time, stepping through the tag while the bytes match it and starting again from the tag's first byte at a byte that does not, without testing that byte against the first; it stops when the whole tag has matched, the stream after it, or at the end of the stream, when it seeks back to where the search began. So a block is found by its tag's bytes, not by the counts, and an ordinal past the tag's last block reads the last block. In every retail file a raw search for each tag finds exactly the blocks of that tag.
2. It reads the `int32` count and the `int16` sum, and does not use the sum. A count of 0 leaves the box without text. Otherwise it allocates that many bytes for the text: more than the text needs, since the count also covers the block's lengths and piece bytes.
3. It reads a piece byte and handles pieces until it reads `0x10`. A literal's text is read to the end of the text so far, over the NUL of the piece before it. A choice reads the flag's value with `MissionVar_ReadFlag` (`0003628d`), the counts, the offsets into a stack array with room for ten, and then tests each pair but the last: of the pairs whose range holds the value, inclusive at both ends and signed, the last one picks its text. It notes the stream's position, reads the picked text, if any, at that position plus its offset onto the text so far, and seeks to that position plus the last offset. A byte other than 1, 2 or `0x10` is passed over.
4. It lays the text out in the box ([laying out the text](../shell/message-box.md#laying-out-the-text)).

`MissionVar_ReadFlag` opens `data\mission.var` (file-name entry 5; assert at `mission.cpp` line `0x4b`), seeks to twice the index and returns the `int16` there.

## The briefing

The war map shows block 0 of `BRF:` in its text box, a page at a time ([the war map's briefing](../shell/war-map.md#the-briefing)).

## The debriefing

The debriefing screen shows one `DEB:` block in its text box ([the debriefing](../shell/debriefing.md)). `DebriefingMenu_Ctor` (`0002c422`) loads the block whose ordinal is the word `000aad58`. The code at `00038f3c` reads that word as the first word of `data\results.dat` (file-name entry 7) and, at `000390a8`, copies it to flag 0 of the shell's flags `000ab244` ([after a mission](bst-battle-sets.md#after-a-mission)).

**The ordinal is the mission's outcome: the report line of the player's squad, picked by the mission's header word 3, less 1.** The simulator writes it at the mission's end as `Group_StatusLineIndex` (`ES1DBSIM 0000f0f0`)(the player's group, header word 3) ([writing the results](../simulation/world.md#writing-the-results--mission_writeresults-0001d698)), whose answer lies in bands, each band spread by the squad's damage tier, 0 to 3 ([the group report](../simulation/mission-status.md#the-group-report--group_statuslineindex-0000f0f0)). So header word 3 decides how many ordinals a mission can give:

| Header word 3 | Bands of the ordinal | Ordinals |
|---|---|---|
| 0, 1, 2, 6 | 0; 1 to 4; 5 to 8 | 0 to 8, nine |
| 3, 5 | 0 to 3; 4 to 7 | 0 to 7, eight |
| 4 | 0; 1 to 3; 4 to 7 | 0 to 7, eight |
| 7 | 0, 1, 2, 3; 4 to 7 | 0 to 7, eight |
| 8 | 0 to 3; 4; 4 to 7 | 0 to 7, eight |
| 9 | 0; 1 to 3; 4 and 5; 6 and 7 | 0 to 7, eight |

The lower ordinals report success and the upper failure. In `SM_SCR01.ENG` (header word 3 is 4) blocks 0 to 3 report the Cybrid attack driven off with more damage to the base at each, 4 to 7 failure; in `SM_ASS01.ENG` (9) blocks 0 to 3 report the Cybrid base destroyed with more damage to the squad at each, 4 to 7 that it was not; in `B0_C1_20.ENG` (3) the first four report success and the last four failure.

That is why a file has eight or nine `DEB:` blocks. Header word 3 as `HercWorks.Query patches` gives it ([the status](../simulation/mission-status.md#the-status--mission_status-0000f4e8)) and the count of each `.ENG`'s `DEB:` blocks agree for 69 of the 77 missions. The eight others: `B0_C0_4B` (word 3 is 4), `B0_C6_1A` (3) and `B0_C7_6A` (9) have nine blocks, whose ninth no outcome picks; `B0_C3_2A` and `B0_C6_6A` (6), `SM_ESC50` (1), and `SM_RCN02` and `SM_RCN03` (0, the same text) have eight, so an ordinal of 8 reads the eighth, the last ([reading a block](#reading-a-block)). `B4_C1_20.ENG` and `B4_C1_30.ENG` have the counts of the files they copy. `Mission_OutcomeGrade` (`00036837`), which grades the ordinal for the talking head's line and the career, keeps its table of ten words for kinds 0, 1, 2 and 6 and its table of eight for the others ([equipment availability](bst-battle-sets.md#equipment-availability)).

**The flags a debriefing's choices read are the ones the mission left.** The choices read `data\mission.var` through `MissionVar_ReadFlag`, and the simulator's `Mission_WriteResults` writes its 100 flags back over that file at the mission's end, before the exit code that brings the shell back ([writing the results](../simulation/world.md#writing-the-results--mission_writeresults-0001d698), [the end](../simulation/main-loop.md#the-end--sim_shutdown-0005b568)): the flags as the mission load seeded them, with what the mission's actions wrote and flag 7 set to 0 at the simulator's load ([reading](script-dat.md#reading)). The copy of the ordinal to flag 0 is made in the shell's flags, not in the file. The `DEB:` choices of the 23 files that have any test flags 4, 7, 13, 20, 21, 26, 30, 31, 35, 36, 38, 44, 50, 70, 71, 96, 97 and 98, flag 0 in none.

## The objectives

`Mission_WriteObjectives` (`00018824`), which `Shell_PrepareMission` (`0002a523`) calls with the `.eng` path, finds the file's first `OBJ:` block and writes its pieces to `data\object.str` (file-name entry 6) for the simulator. It reads the `int32` count, the `int16` sum and the `int16` piece count, and then for each piece a byte it does not test, the `int16` length and the text. With no `OBJ:` block it writes nothing and leaves any `data\object.str` as it was. Otherwise it removes the old file and writes, through a `FileRWStream` (assert at `dialog.cpp` line `0x56`):

| Bytes | Field |
|---|---|
| 4 | `int32`, the count of the bytes after it, written as 9999 and set once the rest is written |
| 2 | `int16`, the count of objectives |
| per objective | `int16` length counting the NUL, the text with its NUL, and a 0 byte |

That is a string table of one group ([ES2's `.STR` layout](../../retail/formats/str-strings.md#layout)), the 0 byte each text's count of attributes. Each literal piece of an `OBJ:` block is one objective, and the retail blocks hold only literals. The simulator's objectives panel (`F11`) reads the file as a string table and shows its first seven lines that are not empty ([the objectives panel](../simulation/alert-panels.md#the-objectives-panel--objectivespanel_ctor-0004a1b4)).

## Rejected readings

| Reading | Why it is wrong |
|---|---|
| The leading `01 00` is a record count, as ES2's `.ENG` opens | It is a version: `Dialog_LoadConditional` builds a box from the file only when it is 1, and the blocks run to the end of the file without a count |

## Open

- **Open:** whether the shell writes its flags `000ab244`, flag 0 the ordinal among them, back to `data\mission.var` before the debriefing's block is read; the writer this page's sources name is the mission load's step 7 ([loading a mission](msn-mission-file.md#loading-a-mission)).
- **Open:** why `Mission_ReadRestore` reads the word written from `Mission_Kind` (`000aad5a`) into `000aad5e`, and what the two words hold.
