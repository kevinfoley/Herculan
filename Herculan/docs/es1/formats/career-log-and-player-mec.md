# GO.EXE — the careers log and the player file (`.LOG`, `.MEC`)

**The shell keeps a list of the careers the player has started, reading it when it starts and writing it back when it quits, and before a mission it writes the player's HERCs and pilots to a file the simulator reads.** The list is `car\names.log`; each entry names a career and the two files `Career_New` writes for it, `CAR\CAR_`*n*`.CAR` and the hangar `CAR\CAR_`*n*`.MEC` ([`Career_New`](../shell/career.md#career_new)). The simulator's file is `data\player.mec`. `SHELL0.VOL` holds a `car\NAMES.LOG` with no careers and a `car\CAREER1.MEC` for which no reader was found. ES2 lists its saves in a fixed directory of twelve slots ([`sav\GAMEFILE.STR`](../../retail/formats/save-games.md#the-twelve-slots--savgamefilestr)) and writes its `data\player.mec` in its own layout ([ES2's player file](../../retail/shell/campaign-loop.md#launching-a-mission--game_exportmissionhandoff-0040f0d4)). Addresses are `ES1GO` image addresses unless an `ES1DBSIM` one is named ([the X-32 executable](x32-executable.md#the-image)).

## The careers log

The path is entry 13 of the file-name table at `0009afb8`, `car\names.log`, read through `Text_GetString` (`00018717`) and `Path_ApplyAltDrive` (`00020989`); the table comes from `cfg\filename.bin` ([loading a mission](msn-mission-file.md#loading-a-mission)).

```
int16    entry count n
if n is not 0:
  int16  the current entry
  n x 100-byte entry
```

| Offset in the entry | Bytes | Field |
|---|---|---|
| `+0x00` | 64 | the career's name, NUL-terminated |
| `+0x40` | 2 | a word, matched with the name ([Open](#open)) |
| `+0x42` | 17 | the career file's path, `CAR\CAR_`*n*`.CAR` |
| `+0x53` | 17 | the hangar file's path, `CAR\CAR_`*n*`.MEC` |

An entry is written from memory whole, so the bytes after each name's NUL are whatever its buffer held. The log is the object at `0009b20c`: the entries at `+0`, the count at `+4`, the current entry at `+6`, and at `+8` a byte set when an entry has been added since the load.

### Loading

`CareerLog_Load` (`00017e7f`), given the log, frees its entries (`CareerLog_FreeEntries`, `00017e59`, which deletes the array at `+0` and zeroes the pointer) and opens the path as a `FileRStream`. When it opens it reads the count into `+4`; otherwise the count is 0. It allocates room for the count plus one entries, 100 bytes each, at `+0`. With a count of 0 it sets the current entry to −1; otherwise it reads the current entry into `+6` and the entries' bytes. It closes the stream when the count is not 0, clears the byte `+8`, and destroys the stream.

`es2_xref.py` finds two calls: from `Shell_LoadData` (`00020630`), the start-up load ([which battle set](bst-battle-sets.md#which-battle-set)), which then copies the current entry to the word `0007fcea`, 0 when it is negative; and from `CareerLog_Delete`.

### Adding a career

`CareerLog_FindOrAdd` (`00018099`), given the log, a word and a name in stack order, takes entry `0x16` of the text table at `0009afa8`, `UNTITLED`, for an empty name ([the shell's text](bin-string-tables.md)), and returns an entry:

1. With no entries, it copies the name and the word into entry 0, `CAR\CAR_0.CAR` and `CAR\CAR_0.MEC` into its paths, sets the count to 1, the byte `+8` to 1 and the current entry and `0007fcea` to 0, and returns entry 0.
2. Otherwise it returns the first entry whose name equals the name, ignoring case (`stricmp`), and whose word equals the word, making it the current entry and `0007fcea`.
3. With no match and the byte `+8` clear, it fills entry *n*, the count, as in step 1 with the paths `CAR\CAR_`*n*`.CAR` and `CAR\CAR_`*n*`.MEC`, adds 1 to the count, sets the byte, and makes *n* current.
4. With no match and the byte set, it copies the name and the word over the last entry, keeping its paths, and makes it current.

So the shell adds at most one career between a load and the next, and a later new career overwrites that one's name. `es2_xref.py` finds one call, from `Career_New`, which keeps the entry at the roster's `+1` and opens its two paths.

### Writing

`CareerLog_Write` (`00017f5f`), given the log and an entry to leave out or −1, deletes the file (`remove`), opens it as a `FileRWStream`, asserting at severity 5 (`career.cpp` line `0x51b`, "Unable to open careers log file") when it cannot, and writes the count, one less when an entry is left out. When that count is not 0 it writes the current entry and every entry but the one left out, 100 bytes each. It closes and destroys the stream. `es2_xref.py` finds two calls: from `Shell_Main` with −1, where `VolRStream_DeleteGroup` and `Mem_HeapOff` follow it, and from `CareerLog_Delete`.

`CareerLog_Delete` (`0001828e`), given the log and an entry below the count, compared as `int16`, so a negative entry passes and reads before the entries, deletes the entry's two files (`remove`) by the paths as `+0x42` and `+0x53` store them, where `Career_New`, `Career_Save`, `Career_LoadCurrent` and `Roster_ActivateRecord` pass a career's paths through `Path_ApplyAltDrive` first, takes 1 from the current entry when it is not 0, writes the log leaving the entry out, and loads it again. `es2_xref.py` finds one call, from `DeleteAlert_OnMessage` (`0002ae96`), screen 1270's DELETE, with the career list's selected line `Career_SelectedEntry` (`0007fcea`) ([deleting a career](../shell/career.md#deleting-a-career)).

`CareerLog_CurrentNameArray` (`000182f8`) returns a new null-terminated array of string pointers: empty when the current entry is −1, and otherwise the 64-byte buffer at `0007ebc0`, allocated on first use, holding the current entry's name (`sprintf` with `%s`). The test loads the current entry into `AX` and compares all of `EAX` with −1, so it holds only when `EAX`'s upper half is `0xffff` on entry. `es2_xref.py` finds one call, from `Menu_ListLines` (`0003750d`), which gives the array as the lines of the main menu's list 1002 ([the list](../shell/main-menu.md#the-list)).

### The retail file

`NAMES.LOG` is 2 bytes, a count of 0, so the load leaves no careers and the current entry −1.

## The player file

`PlayerMec_Write` (`0002299e`) deletes entry 4 of the file-name table, `data\player.mec`, opens it as a `FileRWStream` with no test that it opened, writes it with `PlayerMec_WriteEntries` (`00022a05`), closes it, saves the career record with `CareerRecord_SaveCurrent` (`00014f28`) and destroys the stream. `CareerRecord_SaveCurrent` writes a single mission's record to `car\sm_play.gam` (`SingleMission_SaveRecord`, `00014f54`, [the squad](msn-mission-file.md#the-squad)) and in mode 1 calls `Career_Save` (`000168b0`) with the roster and its current record ([saving](../shell/career.md#saving--career_save-000168b0)). `es2_xref.py` finds three calls to `PlayerMec_Write`: from `WarMapMenu_OnMessage` (`0002c057`), `ServiceBayMenu_OnMessage` (`0002d327`) and `MsnGen_LoadMission`.

```
int16    the player's entry
int16    entry count n
n x {
  int16  pilot word 1
  int16  pilot word 2
         a HERC record, when one is written (below)
}
17 bytes the weapons' available bytes
```

`PlayerMec_WriteEntries`, given the stream, takes the record `CareerRecord_Current` (`00014ef4`) returns, the single mission's `SingleMission_Record` (`0009b014`) in mode 0 and the roster's current record in mode 1 ([the career record](bst-battle-sets.md#the-career-record)), and keeps its current pilot `+0x12` and the hangar's selected slot `Hangar_SelectedSlot` (`00080894`). It writes the record's player entry `+0xe` and the count *n* of its pilot entries on strength with a HERC slot (`CareerRecord_CountPilots`, `00015bfb`, [pilot entries](bst-battle-sets.md#pilot-entries)), and makes the player's entry the current pilot with `CareerRecord_SelectPilot` (`00015ad4`), which selects its slot. Then, *n* times, for the current pilot:

1. its two pilot words, with `PlayerMec_WritePilotWords` (`0001548f`, below);
2. with *h* the HERC in the selected slot ([the HERC lists](car-hercs-and-stock.md#the-herc-lists)), when the slot is not −1 and holds one: in mode 0 every status word of *h* set to 100, by `Herc_SetAllStatus`(*h*, 100) (`00024c3c`) and `Herc_SetStatus`(*h*, 100, 9, `0x784`) (`00024c14`) for the internal word 9 the first leaves; in mode 1 internal word 9 set to the condition of the player's entry, `Herc_SetStatus`(*h*, `PilotEntry_Condition`(record, `+0xe`), 9, `0x784`), whichever entry is being written ([the HERC record](car-hercs-and-stock.md#the-herc-record));
3. *h* with `Herc_Write` (`000233ce`) when there is one, and nothing in its place otherwise;
4. the next entry on strength with a slot, modulo 4, becomes the current pilot (`CareerRecord_NextPilot`, `00015b0a`), selecting its slot.

So the entries run from the player's on in entry order. It then writes the record's 17 weapon bytes with `CareerRecord_WriteWeaponFlags` (`000160ac`, [equipment availability](bst-battle-sets.md#equipment-availability)), selects the kept current pilot again, and restores `Hangar_SelectedSlot`.

`PlayerMec_WritePilotWords`, given an entry, the player's entry `+0xe` and the stream, writes two words by the mode `Shell_Mode` (`000aad56`, [the mode](msn-mission-file.md#the-mode-and-the-mission-parameters)):

| Entry | Word | Mode 0 | Mode 1 |
|---|---|---|---|
| the player's | 1 | `Math_RandomBelow`(`Shell_RandomState`, 11) | the word `+0x40` of the careers-log entry the roster `Career_Roster` (`0009b218`) holds at `+1` |
| the player's | 2 | 0 | its rank `+0xd`, after `CareerRecord_SelectPilot` on it |
| any other | 1 | `PilotEntry_NameIndex` (`00015f50`): 3 × its name group `+0` plus its name `+2`, or −1 for entry 0 | the same |
| any other | 2 | the entry's number | its rank `+0xd` |

### A single mission's file

**A single mission's player file holds the player's stock HERC and each squadmate's HERC as the mission gives it, every one undamaged, with drawn pilot names.** For a mode-0 load of a mission whose first group has *n* members, at most four ([the squad](msn-mission-file.md#the-squad)), every byte is:

| Field | Value |
|---|---|
| the player's entry | 0 |
| entry count | *n*, less any squadmate whose HERC found no empty hangar slot |
| entry 0, words 1 and 2 | a draw below 11, the load's last draw ([the order](msn-mission-file.md#the-order-of-the-loads-draws)), and 0 |
| entry *k*, words 1 and 2 | 3 × (4(*k* − 1) + a draw below 4) + a draw below 3, and *k* |
| entry 0's HERC | the HERC `car\sm_init.dat` holds in the slot the player's mech type names: its chassis, mount count, weapons, per-mount words and `+0x2c`, 2 in every retail record ([the retail HERC lists](car-hercs-and-stock.md#the-retail-herc-lists)) |
| entry *k*'s HERC | the chassis the member's mech type `0x30` gives, its mount count from the table at `000803c8`, that many weapons from the mech's `0x32` and per-mount words from its `0x4e`, a `-1` written as it is, and `+0x2c` the mech's `0x62` |
| per-mount words | as above, but a 5 on a weapon 9, 10 or 11 is written 0 |
| the 66-byte block | 33 words of 100: `PlayerMec_WriteEntries` sets each word but those of mounts past the mount count, which `sm_init.dat`'s records and `HercStatus_Ctor` hold at 100 |
| the 17 bytes | `bst\tech_sng.dat`'s: 1 for weapons 0 to 4 and 6 to 13, 0 for weapons 5 and 14 to 16 ([the retail files](bst-battle-sets.md#the-retail-files)) |

### The simulator's read

`DBSim_LoadScriptDat` (`ES1DBSIM 0001d850`, [reading](script-dat.md#reading)) opens `data\player.mec` with a severity-5 assert (`world.cpp` line `0x95`), reads the first word into `ES1DBSIM 000af9fa` and the count into `000af9e2`, and reads each entry into a `0x7e`-byte record of the array it allocates at `000af9e4`, then 17 bytes into `000af9e8`:

| Offset in the record | Field, in file order |
|---|---|
| `+0x00`, `+0x02` | the two pilot words |
| `+0x04` | the chassis |
| `+0x06` | the mount count *m* |
| `+0x08` | *m* weapons |
| `+0x20` | *m* mount words |
| `+0x3a` | the HERC's word `+0x2c` |
| `+0x3c`, `+0x56`, `+0x6a` | the 66-byte block, read as 26, 20 and 20 bytes: 13 external, ten internal and ten hardpoint conditions, the squad machine's starting damage ([the squad's condition](../simulation/damage-readouts.md#the-functions)) |

It reads every entry with a HERC record and does not test *m*, which has room for twelve mounts at `+0x08`.

### Differences from ES2

ES2's file ([ES2's player file](../../retail/shell/campaign-loop.md#launching-a-mission--game_exportmissionhandoff-0040f0d4)) has the same header and the same entry shape, with these differences:

- ES2 writes the player's entry as a literal 0; ES1 writes the career record's word `+0xe`.
- ES2's two pilot fields are the pilot's name index and skill; ES1's are a name index, the player's drawn in a single mission and taken from the careers log in a career, and the rank in a career or the entry's number in a single mission.
- ES2's arrays are as long as the HERC's mount capacity, an empty hardpoint writing weapon 0 and ammunition type 5; ES1's are as long as the mount count, an empty mount being weapon 17.
- ES2's word after the arrays is a literal 0; ES1's is the HERC's word `+0x2c`.
- ES2's file ends with an `int16` 33 and 33 unlock bytes; ES1's with 17 available bytes and no count.

## The archive's `CAREER1.MEC`

`SHELL0.VOL`'s `car\CAREER1.MEC` is 357 bytes, kept whole as opaque. Its bytes walk as the player file's header and tail around four entries that each have one word where the player file's have a word per mount:

```
int16 0, int16 4
4 x { int16, int16, int16 chassis, int16 3, 3 x int16 weapon, int16 4, int16 2, 66 bytes }
17 x byte 1
```

| Entry | Words | Chassis | Weapons |
|---|---|---|---|
| 0 | 0, 5 | 0 | 0, 6, 17 |
| 1 | 3, 15 | 1 | 0, 0, 9 |
| 2 | 6, 25 | 1 | 1, 1, 17 |
| 3 | 9, 35 | 0 | 0, 6, 17 |

The 66-byte blocks hold words such as 50, 64, 70, 75, 80 and 95. Read as the player file, the second entry's mount count comes out 0 and the third's 25, past the file's end; read as a hangar list (`Hangar_Read`, `00028c50`), the header gives a HERC count of 0 after 14 bytes. A byte search of `GO.EXE` and `DBSIM.EXE` finds no `career1`, where the same search finds `car_%d` in `GO.EXE` and `player.mec` in `DBSIM.EXE`; in `SHELL0.VOL` the name occurs only in the archive's directory, and `cfg\filename.bin` names `data\player.mec`, `car\sm_play.mec` and `car\temp.mec` ([Open](#open)).

## Open

- **Open:** what reads `car\CAREER1.MEC`: the byte searches above find its name in no executable, and its entries match neither the player file's layout nor the hangar list's.
- **Open:** what the careers-log entry's word `+0x40` is for: `Career_New` passes its second argument, which `es2_xref.py`'s one call of it, NEW's ACCEPT, gives as 0 ([starting a career](../shell/career.md#starting-a-career)), and the player file's first pilot word is that word in mode 1.
- **Open:** whether an entry is ever written without a HERC record, which the simulator's read takes the next entry's words for: in a single mission it can when the player's machine has a type other than 0 to 7, a slot `sm_init.dat` does not fill, and in a career when a pilot's slot holds none. The record of the first group's first member holds type 1, 6 or 7 in 12 of the 14 single missions and takes a template's in `SM_ESC50.MSN` and `SM_SCT50.MSN`, whose candidates are not walked.
- **Open:** what the simulator does with the two pilot words, the mount words, the word `+0x3a` and the 17 bytes at `000af9e8`.
- **Open:** what `00020fff`, which `es2_xref.py` finds calling `CareerLog_FreeEntries` beside the shell's shutdown (`Shell_FreeData`, `00020ae3`, [the shutdown](../shell/startup.md#the-shutdown)), does with the log.
