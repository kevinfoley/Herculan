# GO.EXE — the starting HERCs and the weapon stock

**A new career and a single mission each start the player with a hangar of HERCs and a stock of spare weapons, read from two pairs of files in `SHELL0.VOL`'s `car\`.** `car\car_init.dat` and `car\c_armory.dat` are a career's, `car\sm_init.dat` and `car\s_armory.dat` a single mission's. A HERC list is a few header words and a counted list of HERCs, each with its chassis, a weapon per mount and a 66-byte block of 100s; a stock file is a counted list of slots, each with a count of words, every one 100 in retail. The shell writes both objects back in the same layouts into the two files a new career opens ([the files](../shell/career.md#the-files)). ES2 starts a career from `gam\hercs.dat` ([ES2's starting hangar](../../retail/formats/herc-catalogs.md#gamhercsdat--the-starting-hangar)) and keeps its stock per catalog weapon ([ES2's armory stock record](../../retail/formats/save-games.md#armory-stock-record)). Addresses are `ES1GO` image addresses ([the X-32 executable](x32-executable.md#the-image)).

## The HERC lists

```
int16    slot count                 -> word 00080890
int16                               -> word 00080894
int16    marked slot                -> word 00080896
int16    marked slot's word         -> word 00080898
int32                               -> dword 0008089c
int16    HERC count                 -> word 00080892
HERC count x {
  int16  hangar slot
         a HERC record (below)
}
```

`Hangar_LoadFile` (`00028c25`) opens the path through `VolRStream_CtorFromPath`, reads it with `Hangar_Read` (`00028c50`) and destroys the stream. `Hangar_Read`:

1. empties the hangar with `Hangar_Clear` (`00028b19`): each HERC in the slot array `000808a0` below the slot count is destroyed (`000232a4` with 3) and its pointer zeroed, the array is freed, `00022981` runs when the dword `0009a30c` is below the dword `0007e738`, and the six header fields become 0, 0, −1, −1, 0 and 0 in the order slot count, HERC count, `00080894`, marked slot, marked slot's word, dword;
2. reads the slot count, and `Hangar_AllocSlots` (`00028ac9`) makes an array of that many null pointers at `000808a0` when it is not 0;
3. reads the other four header fields and the HERC count;
4. for each HERC reads its slot, makes a HERC with `Herc_Ctor` (`00022f5e`) for chassis −1, stores it in that slot with no bounds test, and reads it with `Herc_Read` (`00023501`), passing the marked slot's word when the slot is the marked slot and that word is not 0, and 0 otherwise.

`Herc_Read` reads the record with `Herc_ReadFromStream` (`0002352f`) and, unless the byte `Shell_LowMemory` (`0007fd09`) is set, builds the HERC's diagram with `Herc_BuildDiagram` (`00023883`, [the mount pictures](herc-diagrams.md#the-mount-pictures)), handing it the word.

`es2_xref.py` finds three calls to `Hangar_LoadFile`: from `Career_New` with `car\car_init.dat` ([`Career_New`](../shell/career.md#career_new)), from `MsnGen_LoadMission` in mode 0 with `car\sm_init.dat` ([loading a mission](msn-mission-file.md#loading-a-mission)), and from `00014fd6`, which `Shell_ProcessMissionResults` calls outside mode 1. `Hangar_ReadFromSave` (`00028bd6`), given *n* and a path, opens the file as a `FileRStream`, passes over *n* lists with `HercList_Skip` (`00028db6`) and then reads the next with `Hangar_Read`: a career's hangar file holds a list for each of its records ([the files](../shell/career.md#the-files)).

`Hangar_Write` (`00029107`) writes the five header fields as read. When the mode word `000aad56` is not 0 it then writes the HERC count and, for each slot below the slot count that holds a HERC, the slot and the HERC (`Herc_Write`). In mode 0 it writes a count of 8 and then, for each of slots 0 to 7 that holds a HERC, the slot and the HERC, so the count it writes is 8 whatever the slots hold. `Hangar_SaveFile` (`0002909c`), given a path, writes the hangar there through a `FileRWStream`, asserting at severity 5 (`mechman.cpp` line `0x132`) when it cannot open it; a single mission's load saves it as `car\sm_play.mec` ([the squad](msn-mission-file.md#the-squad)).

`Hangar_AddHerc` (`00029d0f`) puts a HERC in the first empty slot below the slot count (`Hangar_FindFreeSlot`, `0002983a`), builds its diagram unless the byte `Shell_LowMemory` (`0007fd09`) is set, adds 1 to the HERC count and returns the slot, or returns −1 and stores nothing when no slot is empty. The second header word, `Hangar_SelectedSlot` (`00080894`), is the slot of the career record's current pilot ([pilot entries](bst-battle-sets.md#pilot-entries)), the HERC `data\player.mec`'s writer writes next.

### The HERC record

| Offset in the HERC | Bytes | Field |
|---|---|---|
| `+0x00` | 2 | chassis, 0–7 |
| `+0x02` | 2 | mount count *n* |
| `+0x04` | 2*n* | per mount, its weapon; 17 (`0x11`) is an empty mount |
| `+0x18` | 2*n* | per mount, a word ([Open](#open)) |
| `+0x2c` | 2 | a word ([Open](#open)) |
| `+0x2e` | 66 | the status block: 13 external words, 10 internal words and 10 per-mount words, 100 in every retail record |

`Herc_ReadFromStream` reads the fields in that order straight into the `0xa8`-byte HERC, which has room for ten mounts.

The status block's three parts are the three spans the simulator reads, 26, 20 and 20 bytes ([the simulator's read](career-log-and-player-mec.md#the-simulators-read)). The shell addresses a word by an index and a kind: `0x785` an external area, the truncated mean of the external words the −1-ended list at `0007ef54` + 8 × index names (`HercStatus_ExternalAverage`, `0001a3ba`); `0x784` internal word *index* at `+0x1a` + 2 × index; `0x786` the per-mount word of mount *index* at `+0x2e` + 2 × index. `HercStatus_Get` (`00015896`) reads one, returning the kind itself for any other kind, and `HercStatus_Set` (`0001a492`) writes one (an external area through `0001a42c`). On a HERC's block, `Herc_GetStatus` (`00024b86`), given a HERC, an index and a kind, reads one through `HercStatus_GetHercCopy` (`00024bab`), a second copy of `HercStatus_Get`'s code; `Herc_SetStatus` (`00024c14`) writes one, and `Herc_GetMountStatus` (`00024b23`) reads a mount's. In the HERC the external words start at `+0x2e`, the internal words at `+0x48` and the per-mount words at `+0x5c`, so internal word 9 is the word at `+0x5a`. Internal word 9 carries a pilot's condition: a career's player file writes the player's condition into every HERC's, and the results read it back as each pilot's ([the player file](career-log-and-player-mec.md#the-player-file), [after a mission](bst-battle-sets.md#after-a-mission)). `HercStatus_Ctor` (`0001a29c`) sets every word of a block to 100 but internal word 9, which it leaves as it was, and `Herc_SetAllStatus` (`00024c3c`), given a HERC and a value, sets the external words, internal words 0 to 8 and the per-mount words of the mounts below the mount count to the value, or to 100 for a mount whose weapon `00024ab7` gives as 17. `Herc_Write` (`000233ce`) writes them in the same order; before it writes the per-mount words it sets to 0 the word of each mount whose weapon is 9, 10 or 11 and whose word is 5, as `Herc_BuildDiagram` does. The weapon numbers are those of the [equipment state](bst-battle-sets.md#equipment-availability), whose 17 weapons end at 16, so 17 is one past them.

**After a mission the shell reads a HERC's status block back from `data\results.dat`**, 66 bytes laid out as the block, which the simulator fills from its damage readouts: readouts 1–13, 20–29 and 32–41, each as a percentage ([what the results read](../simulation/mission-groups.md#what-the-results-read)). Readout 20 + *i* is internal *i* ([the tenth mount pair](../simulation/damage-readouts.md#the-tenth-mount-pair)), so internal word 9 is internal 9, the pilot ([the damage write](../simulation/mech.md#the-damage-write--mech_componentdamagewrite-00013250)). `Hangar_ReadStatusBlock` (`00028e50`), given a stream and a hangar slot, does nothing when the slot is −1 or holds no HERC. Otherwise it frees the HERC's diagram (`Herc_FreeDiagram`, `0002330e`, [the mount pictures](herc-diagrams.md#the-mount-pictures)), reads 66 bytes straight over the status block at `+0x2e` through the stream's vtable slot `+0x1c`, and builds the diagram again with `Herc_BuildDiagram`(HERC, 0), whatever the byte `Shell_LowMemory` holds; the build makes each mount whose word arrived at 0 empty (weapon 17) and sets that word back to 100. `es2_xref.py` finds one call, from `CareerRecord_ReadPilotResults` ([after a mission](bst-battle-sets.md#after-a-mission)). ES2's `Herc_ReadStatusBlock` (`00411720`) reads the same 66 bytes over a HERC it is given and deletes a mount that arrived at 0 itself, taking 1 from the HERC's count of filled mounts ([ES2's debrief](../../retail/shell/campaign-loop.md#the-debrief--game_processmissionresults-0040eae7)); ES1's HERC keeps its chassis's mount count and marks the mount empty.

`Herc_Ctor`, given a HERC (or none, when it allocates `0xa8` bytes) and a chassis, sets up the status block at `+0x2e` with `HercStatus_Ctor` and the object at `+0x72`, stores the chassis at `+0`, and for a chassis other than −1 takes the mount count from the eight words at `000803c8`, 3, 3, 4, 5, 6, 7, 8 and 9, and gives every mount weapon 17 and word 0; for −1 the mount count is 0. It sets `+0x2c` to 2 and `+0x70` to −1 and zeroes `+0x94` to `+0xa7`. ES2's HERC carries a 66-byte status block of the same size ([ES2's status block](../../retail/formats/save-games.md#the-66-byte-status-block)).

### The retail HERC lists

`CAR_INIT.DAT`: slot count 8, word `00080894` 0, marked slot −1, its word 0, dword 0, and four HERCs, every mount word 5 and every `+0x2c` word 2. The chassis and weapon names are the [equipment tables'](bst-battle-sets.md#the-retail-files).

| Slot | Chassis | Weapons |
|---|---|---|
| 0 | 0, `ROADRUNNER` | 0, 0, empty |
| 1 | 0, `ROADRUNNER` | 6, 6, empty |
| 2 | 1, `OUTLAW` | 7, 7, empty |
| 3 | 1, `OUTLAW` | 1, 1, empty |

`SM_INIT.DAT`: slot count 11, word `00080894` 7, marked slot −1, its word 0, dword 0, and eight HERCs in slots 0 to 7, slot *s* holding chassis *s* with that chassis's mount count from `000803c8`. Every `+0x2c` word is 2. The mount words are 5 except on weapons 9 and 10, where they are 0 or 1:

| Slot | Weapons | Mount words |
|---|---|---|
| 0 | 6, 6, empty | 5, 5, 5 |
| 1 | 0, 0, empty | 5, 5, 5 |
| 2 | 6, 0, 0, 6 | 5, 5, 5, 5 |
| 3 | 6, 0, 9, 0, 6 | 5, 5, 0, 5, 5 |
| 4 | 0, 6, 0, 9, 9, 9 | 5, 5, 5, 0, 1, 0 |
| 5 | 6, 6, 0, 9, 0, 9, 9 | 5, 5, 5, 0, 5, 1, 0 |
| 6 | 6, 6, 6, 10, 6, 0, 0, 0 | 5, 5, 5, 0, 5, 5, 5, 5 |
| 7 | 7, 7, empty, empty, 9, 7, 7, 9, 9 | 5, 5, 5, 5, 0, 5, 5, 1, 0 |

The install's loose `CAR\SM_INIT.DAT` holds the archive's 786 bytes but for the word `00080894`, which is 0.

## The weapon stock

The stock is the object the dword `000aad62` points to, built in `inventor.cpp`; `Career_LoadCurrent` and `Career_Save` index the dwords from `000aad62` by career record, 41 of them, a stock for each ([the files](../shell/career.md#the-files)), and `es2_xref.py --decode` finds them written by `00027c4c` ([Open](#open)). A stock is an `int16` slot count at `+0` and at `+2` a pointer to that many 6-byte stacks of words, each an `int16` count and a pointer to its head node; a node is 6 bytes, the word and a pointer to the next node.

```
int16    record count
record count x {
  int16  slot
  int16  value count
  value count x int16
}
```

`Inventory_LoadFile` (`00027e02`), given the stock and a path, opens the path through `VolRStream_CtorFromPath`, reads it with `Inventory_Read` (`00027e3c`), and closes and destroys the stream. `Inventory_Read` empties every stack below the slot count (`Inventory_ClearAll`, `00027d76`, through `WordStack_Clear`), calls `Inventory_TraceSlots` (`000281d7`), reads the records, pushing each value onto the stack of the record's slot with `WordStack_Push` (`00040ba6`) with no bounds test on the slot, and calls `Inventory_TraceSlots` again. A slot's last value read is its stack's head.

- `WordStack_Push` puts a new node holding the word at the head and adds 1 to the count.
- `WordStack_Pop` (`00040bfd`) returns −1 when the stack is empty (`WordStack_IsEmpty`, `00040c57`, tests the count for 0); otherwise it unlinks and frees the head, subtracts 1 from the count, and returns the head's word.
- `WordStack_Clear` (`00040d19`) pops until a pop returns −1, so a stored −1 also ends it.
- `WordStack_Write` (`00040ca2`) writes the count and then each word from the head.
- `Inventory_TraceSlots` calls `NoOp_TwoArgs` (`00040d41`), which returns at once, for each slot but slot 5.

`Inventory_Write` (`00027f88`) writes the slot count and then, for every slot, the slot and its stack through `WordStack_Write`: the layout above, every slot present and each slot's words from the head, so writing a stock and reading it back reverses each slot's words, as ES2's armory save does. `Inventory_SaveFile` (`00027f1b`) writes a stock to a `FileRWStream` on a path, asserting at severity 5 (`inventor.cpp` line `0x5f`) when it cannot open it. `Inventory_CopyThroughScratch` (`00027cb3`), given two indexes, saves the stock the dword `000aad62` + 4 × the first points to as `car\scratch.arm`, loads that file into the stock of the second, and deletes it. `Inventory_Add` (`00028311`) pushes a word onto a slot's stack and `Inventory_Take` (`0002833f`) pops one, each then calling `Inventory_TraceSlots`.

`es2_xref.py` finds three calls to `Inventory_LoadFile`: `Career_New` with `car\c_armory.dat` ([`Career_New`](../shell/career.md#career_new)), `SingleMission_Start` with `car\s_armory.dat` ([starting the mission](../shell/single-mission.md#starting-the-mission)), and `Inventory_CopyThroughScratch`; and a call to `Inventory_Read` from `Career_LoadCurrent` (`00016c1e`). After a career mission `Shell_ProcessMissionResults` passes each pair (*a*, *b*) of `data\results.dat` to `Inventory_Add`, pushing *b* onto slot *a* ([after a mission](bst-battle-sets.md#after-a-mission)).

### The retail stock

Every value is 100.

| File | Slot: values |
|---|---|
| `C_ARMORY.DAT` | 0: 4, 1: 2, 2: 2, 6: 5, 7: 3, 8: 1, 9: 4, 10: 3, 11: 3 |
| `S_ARMORY.DAT` | 0: 22, 1: 24, 2: 16, 3: 12, 4: 15, 6: 23, 7: 23, 8: 15, 9: 22, 10: 17, 11: 9, 12: 14, 13: 8 |

`C_ARMORY.DAT`'s slots are the weapons `bst\tech_0.dat` makes available from a career's start, and `S_ARMORY.DAT`'s those `bst\tech_sng.dat` makes available ([the retail equipment files](bst-battle-sets.md#the-retail-files)).

## Open

- **Open:** what the header words `00080896`, `00080898` and the dword `0008089c` are; what the per-mount words (5, or 0 and 1 on weapons 9 and 10), which the simulator takes as ammunition types ([the second pass](script-dat.md#the-second-pass)), and the word `+0x2c` are to the shell; and which external areas the lists at `0007ef54` group.
- **Open:** that a stock slot is a weapon number and a value that unit's condition, and what reads the stacks to offer the units; `Career_LoadCurrent` (`00016c1e`)'s use of `Inventory_Read`.
- **Open:** what `00027c4c` builds into the dwords from `000aad62`, and when.
- **Open:** whether the install's loose `CAR\SM_INIT.DAT` or the archive's is the one `VolRStream_CtorFromPath` opens.
