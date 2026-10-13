# GO.EXE — the careers log and the player file (`.LOG`, `.MEC`)

**The shell keeps a list of the careers the player has started, reading it when it starts and writing it back when it quits, and before a mission it writes the player's HERCs and pilots to a file the simulator reads.** The list is `car\names.log`; each entry names a career and the two files `Career_New` writes for it, `CAR\CAR_`*n*`.CAR` and the hangar `CAR\CAR_`*n*`.MEC` ([starting a career](bst-battle-sets.md#starting-a-career)). The simulator's file is `data\player.mec`. `SHELL0.VOL` holds a `car\NAMES.LOG` with no careers and a `car\CAREER1.MEC` for which no reader was found. ES2 lists its saves in a fixed directory of twelve slots ([`sav\GAMEFILE.STR`](../../retail/formats/save-games.md#the-twelve-slots--savgamefilestr)) and writes its `data\player.mec` in its own layout ([ES2's player file](../../retail/shell/campaign-loop.md#launching-a-mission--game_exportmissionhandoff-0040f0d4)). Addresses are `ES1GO` image addresses unless an `ES1DBSIM` one is named ([the X-32 executable](x32-executable.md#the-image)).

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

`es2_xref.py` finds two calls: from `00020630`, the start-up load ([which battle set](bst-battle-sets.md#which-battle-set)), which then copies the current entry to the word `0007fcea`, 0 when it is negative; and from `CareerLog_Delete`.

### Adding a career

`CareerLog_FindOrAdd` (`00018099`), given the log, a word and a name in stack order, takes entry `0x16` of the text table at `0009afa8`, `UNTITLED`, for an empty name ([the shell's text](bin-string-tables.md)), and returns an entry:

1. With no entries, it copies the name and the word into entry 0, `CAR\CAR_0.CAR` and `CAR\CAR_0.MEC` into its paths, sets the count to 1, the byte `+8` to 1 and the current entry and `0007fcea` to 0, and returns entry 0.
2. Otherwise it returns the first entry whose name equals the name, ignoring case (`stricmp`), and whose word equals the word, making it the current entry and `0007fcea`.
3. With no match and the byte `+8` clear, it fills entry *n*, the count, as in step 1 with the paths `CAR\CAR_`*n*`.CAR` and `CAR\CAR_`*n*`.MEC`, adds 1 to the count, sets the byte, and makes *n* current.
4. With no match and the byte set, it copies the name and the word over the last entry, keeping its paths, and makes it current.

So the shell adds at most one career between a load and the next, and a later new career overwrites that one's name. `es2_xref.py` finds one call, from `Career_New`, which keeps the entry at the roster's `+1` and opens its two paths.

### Writing

`CareerLog_Write` (`00017f5f`), given the log and an entry to leave out or −1, deletes the file (`remove`), opens it as a `FileRWStream`, asserting at severity 5 (`career.cpp` line `0x51b`, "Unable to open careers log file") when it cannot, and writes the count, one less when an entry is left out. When that count is not 0 it writes the current entry and every entry but the one left out, 100 bytes each. It closes and destroys the stream. `es2_xref.py` finds two calls: from `Shell_Main` with −1, where `VolRStream_DeleteGroup` and `Mem_HeapOff` follow it, and from `CareerLog_Delete`.

`CareerLog_Delete` (`0001828e`), given the log and an entry below the count, deletes the entry's two files (`remove`), takes 1 from the current entry when it is not 0, writes the log leaving the entry out, and loads it again. `es2_xref.py` finds one call, from `0002ae96` with the word `0007fcea` when the word `+6` of its second argument is 100 ([Open](#open)).

`CareerLog_CurrentNameArray` (`000182f8`) returns a new null-terminated array of string pointers: empty when the current entry is −1, and otherwise the 64-byte buffer at `0007ebc0`, allocated on first use, holding the current entry's name (`sprintf` with `%s`). The test loads the current entry into `AX` and compares all of `EAX` with −1, so it holds only when `EAX`'s upper half is `0xffff` on entry. `es2_xref.py` finds one call, from `0003750d`.

### The retail file

`NAMES.LOG` is 2 bytes, a count of 0, so the load leaves no careers and the current entry −1.

## The player file

`PlayerMec_Write` (`0002299e`) deletes entry 4 of the file-name table, `data\player.mec`, opens it as a `FileRWStream` with no test that it opened, writes it with `PlayerMec_WriteEntries` (`00022a05`), closes it, calls `00014f28` and destroys the stream. `es2_xref.py` finds three calls: from `0002c057`, `0002d327` and `MsnGen_LoadMission`.

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

`PlayerMec_WriteEntries`, given the stream, takes the career record `00014ef4` returns ([the career record](bst-battle-sets.md#the-career-record)) and keeps the record's word `+0x12` and the word `00080894`. It writes the record's word `+0xe` and the count `00015bfb` returns for the record, and calls `00015ad4` with the record and its four bytes at `+0xe`. Then, *n* times:

1. it writes the two pilot words with `PlayerMec_WritePilotWords` (`0001548f`) for the entry the record's word `+0x12` names;
2. with *h* the HERC in hangar slot `00080894` ([the HERC lists](car-hercs-and-stock.md#the-herc-lists)), when the slot is not −1 and holds one: in mode 0 it calls `00024c3c`(*h*, 100) and `00024c14`(*h*, 100, 9, `0x784`), and in any other mode `00024c14`(*h*, `0001618d`(record, its four bytes at `+0xe`), 9, `0x784`) ([Open](#open));
3. it writes *h* with `Herc_Write` (`000233ce`, [the HERC record](car-hercs-and-stock.md#the-herc-record)) when there is one, and nothing in its place otherwise;
4. it calls `00015b0a` with the record.

It then writes the record's 17 weapon bytes with `CareerRecord_WriteWeaponFlags` (`000160ac`, [equipment availability](bst-battle-sets.md#equipment-availability)), calls `00015ad4` with the record, the kept word `+0x12` and the kept `00080894`, and restores `00080894`.

`PlayerMec_WritePilotWords`, given an entry, the record's word `+0xe` and the stream: when the two are equal, it writes in mode 0 `Math_RandomBelow`(`0009aec0`, 11) and 0, and in any other mode the word `+0x40` of the careers-log entry the roster `0009b218` holds at `+1`, then, with the record `0009b23b` + `0x1f7` × [`0009b21f`] and `00015ad4` called on it and its four bytes at `+0xe`, the word at the record's `+0x21` + `0x25` × its word `+0x12`. For any other entry it writes `00015f50`(record, entry), the record being `0009b014` in mode 0 and `0009b23b` + `0x1f7` × [`0009b21f`] otherwise, and then the entry itself in mode 0 or the word at that record's `+0x21` + `0x25` × entry otherwise. The mode is the word `000aad56`; a career is mode 1 ([equipment availability](bst-battle-sets.md#equipment-availability)).

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
| `+0x3c`, `+0x56`, `+0x6a` | the 66-byte block, read as 26, 20 and 20 bytes |

It reads every entry with a HERC record and does not test *m*, which has room for twelve mounts at `+0x08`.

### Differences from ES2

ES2's file ([ES2's player file](../../retail/shell/campaign-loop.md#launching-a-mission--game_exportmissionhandoff-0040f0d4)) has the same header and the same entry shape, with these differences:

- ES2 writes the player's entry as a literal 0; ES1 writes the career record's word `+0xe`.
- ES2's two pilot fields are the pilot's name index and skill; ES1's are `PlayerMec_WritePilotWords`' ([Open](#open)).
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
- **Open:** what the careers-log entry's word `+0x40` is: `Career_New` passes its second argument, and the player file's first pilot word is that word in mode 1. What `0002ae96` is, whose message word 100 deletes a career, and what `0003750d` does with the current name.
- **Open:** what `PlayerMec_WriteEntries`' callees do: `00014ef4` (which record), `00015bfb` (the count), `00015ad4` (which entry and slot it selects), `00015b0a`, `00024c3c`, `00024c14`, `0001618d`, `00015f50` and `00014f28`; and so what the two pilot words are. Whether an entry is ever written without a HERC record, which the simulator's read would take as the next entry's words.
- **Open:** what the simulator does with the two pilot words, the mount words, the word `+0x3a` and the 17 bytes at `000af9e8`.
- **Open:** what `00020ae3` and `00020fff`, which `es2_xref.py` finds calling `CareerLog_FreeEntries`, do with the log.
