# `nam\*.NAM` — unit type name lists

Reverse-engineered from `DBSIM.EXE` in the `ES2Recon` Ghidra project; all addresses are DBSIM virtual addresses.

A name list turns a unit type number into the base name of that type's resources. `SIMVOL0.VOL` holds two: `nam\MECHS.NAM`, the 21 ground chassis from `OUTLAW` (0) to `CERBERUS` (20), and `nam\FLYERS.NAM`, `SKIMMER`, `HOVTANK` and `DROPSHIP`. A mission's mech and flyer type fields ([`msn-mission-file.md`](msn-mission-file.md)) and `script.dat`'s blocks 7 and 8 ([`script-dat.md`](script-dat.md)) index them.

## The file

No header and no count: NUL-terminated ASCII names, one per type in type order, and then a byte `0x0a` where the next name would start. The `0x0a` ends the list. The reader's walk tests each name's first byte for it and is not bounded by the file's size.

## Loading a list

`LoadNameList` (`00492bf4`) takes a name, a capacity and a third argument its body does not read, and returns a table of `capacity` name pointers:

1. It builds `nam\<name>.nam` with `ResourcePath_BuildFolderName` (`00492ae0`, folder `nam`), which joins the folder, a `\` and `Path_WithExtension(name, folder)` (`00492a84`: the name cut at its first `.` and given the folder as its extension) in a static buffer.
2. It allocates the table (`Mem_NewArray`, `capacity` × 4 bytes) and zeroes it.
3. `VolFile_GetSize` (`00492b78`) opens the path as a volume stream (a loose file or a volume entry) and returns its size, or 0 when the stream reports an error after the open.
4. It allocates a buffer of that size. When the size is not 0 it opens the stream again, reads the whole file into the buffer, and `NameList_Split` (`00492b40`) walks the names from the start of the buffer until one begins with `0x0a`, storing each name's address in the next table entry and counting them.

The names stay in the buffer; the table points into it. A missing file leaves every entry null. `NameList_Split` takes no capacity, so a list with more names than the table holds writes past its end.

| Caller | Arguments | Table |
|---|---|---|
| `Mechs_InitAllTypes` (`0042049c`) | `mechs`, 0x55, 8 | `004a9de4` |
| `FlyerTypes_LoadAll` (`00422fb4`) | `flyers`, 0x22, 4 | `004a9e74` |

## Rejected readings

| Reading | Why it is wrong |
|---|---|
| The code between the table's allocation and the read loops over several files. | It runs once. `LoadNameList` keeps the per-file code of ES1's loader with the file number held at 0 in `EBX`, so the `_itoa` of the number and the test of the byte `006cbccc` against it, both behind tests of that number, are never reached. |
