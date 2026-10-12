# DBSIM.EXE and GO.EXE — unit type name lists

ES1 reads its unit type names as ES2 does ([`../../retail/formats/nam-name-list.md`](../../retail/formats/nam-name-list.md)): the same file layout, ended by a name starting with `0x0a`, the same `nam\<name>.nam` path, and a table of pointers into one buffer holding the files whole. ES1's loader also reads up to eight numbered mod lists after the base list. Addresses are `ES1DBSIM` image addresses unless marked `ES1GO` ([the X-32 executable](x32-executable.md#the-image)); ES2 names and addresses are DBSIM's.

## Mod lists

`LoadNameList` (`00060784`) takes a name, a mod mask, a capacity and a per-list entry count, in declaration order (stack order is the reverse, [calling conventions](compiler.md#calling-conventions)):

1. It reads the size of `nam\<name>.nam` and, for each list number k from 1 to 8 whose mask bit k−1 is set, of `nam\<name>k.nam`, with `VolFile_GetSize` (`00060738`; 0 when the stream's status word is set after the open).
2. It allocates the table (`capacity` × 4 bytes, zeroed) and one buffer for the sizes' total, and reads each list of non-zero size into it, the next list after the last.
3. `NameList_Split` (`000606e8`) stores each list's names into its own run of entries. The base list starts at entry 0 and has `capacity − 8 × count` entries; list k starts at `capacity − 8 × count + (k − 1) × count`.

So a list's type numbers are fixed by its number and not by the lists before it: with the simulator's arguments, mech types 0–15 are the base list and 16–25 list 1, up to 86–95 for list 8.

| Caller | Name | Mask | Capacity | Count | Base list |
|---|---|---|---|---|---|
| `Mechs_InitAllTypes` (`00019ee0`) | `mechs` | 0 | 0x60 | 10 | 16 entries |
| `FlyerTypes_LoadAll` (`0001c770`) | `flyers` | 0 | 0x52 | 10 | 2 entries |

Both pass a mask of 0, so the simulator reads the base lists only. The two callers then walk 16 and 2 type numbers respectively.

`MechMods_Probe` (`00060934`) builds the mask the mod lists would need: for n from 1 to 8 it opens `MECMODn.DYN` in the working directory with `fopen` (mode `rb`) and closes it, setting bit n−1 of `MechMods_Present` (`000b5e6e`) for each that opens.

## The shell's copy

`GO.EXE` holds the same three functions, `NameList_Split`, `VolFile_GetSize` and `LoadNameList` (`ES1GO 00050a01`, `00050a75`, `00050ac9`), with fatal asserts (severity 4, [asserts](../simulation/asserts.md#severities)) that carry the source path `util\mods.cpp`:

| Line | `LoadNameList` stops when |
|---|---|
| 0x9b, 0xb7 | the table or the buffer cannot be allocated |
| 0xab | the name with its list number is longer than 8 characters, tested for every k from 0 to 8 |
| 0xb0 | a list it reads (the base list, or a list whose mask bit is set) has size 0 |
| 0xcc | a list's stream reports an error after the open |
| 0xcf | a list holds more names than its entries |

`NameList_Split` stops at line 100 when a name is followed by an empty one. `NameList_Split` adds 1 to the dword `ES1GO 000ab5fe` for each name, and `LoadNameList` after each step.

`GO.EXE`'s `Path_WithExtension` (`ES1GO 0005095a`) and `ResourcePath_BuildFolderName` (`ES1GO 000509ae`) precede them, as `ES1DBSIM`'s (`00060644`, `00060698`) do.

## Open

- **Open:** no caller of `MechMods_Probe` found by `es2_xref.py`, no reader of `MechMods_Present` found by a search of the disassembly for its address, and no caller of `ES1GO`'s `LoadNameList` found by `es2_xref.py`. What a `MECMODn.DYN` file holds, and whether any ES1 release shipped one, is open.
- **Open:** what reads the dword `ES1GO 000ab5fe`.
- **Open:** the names in ES1's `flyers` list. `mechs` names the sixteen chassis ([the chassis file](chassis-dat.md#which-files)).
