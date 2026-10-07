# .VOL — the archive container and its per-entry prefix

Every one of the game's data files ships inside a `.VOL`. v1.0 has eleven archives, 3,004 entries total: `SIMVOL0`, `SIMPATCH`, `ZONES`, `SHELL0`, `LANG0`, `SIMALERT`, `SIMSOUND`, `SIMVOICE`, `SIMVOICF`, `SIMVOICG`, `SHLSOUND`. v1.10 adds `SHELL1` and `SIMLANG` ([`../retail-builds.md`](../retail-builds.md)). The counts below are v1.0's.

What the game reads of an entry is its **content**, past the per-entry prefix below. Every format doc in this folder describes offsets from the start of that content. A format whose own file is unpacked with the prefix still attached — as [bnd-notes.md](bnd-notes.md) covers for `.BND` — is easy to misread as owning these nine bytes.

## File layout

```
0x00   4       "VOLN"
0x04   uint32  program mask: 0x00000001 DBSIM, 0x00000100 VSHELL, both for ZONES
0x08   byte    search precedence: 0x05 base, 0x0A first (SIMPATCH, SHELL1)
0x09   byte    directory count
0x0a   uint16  directory-list byte size
0x0c   ...     directory list: name + '\' + 0x00, repeated
       uint16  entry count
       int32   entry-list byte size
       ...     entry list, 18 bytes each:
                 13  name, NUL-padded (a few entries carry junk after the NUL)
                 1   directory index
                 4   uint32 offset of this entry's prefix
       ...     entry data, at the offsets above
```

The first entry's data begins at the byte immediately after the entry list, with no gap (verified in all eleven archives).

## Which archives are mounted

Neither program mounts an archive by name. At startup each scans `vol\*.vol` twice, first under the current directory, which is the install, then under the directory `data\drive.cfg` names, which on a retail install is the disc. It loads every file whose program mask shares a bit with its own: `VolRStream_SetGroup` (VSHELL `00402fe3`, DBSIM `00473154`) hands `VolumeGroup_SetGroup` (VSHELL `00403723`, DBSIM `00467bd4`) mask `0x100` from VSHELL's `Shell_Main` (`00401525`) and `1` from DBSIM's `Sim_Run` (`0045f144`). Its last argument would put the `drive.cfg` pass first; both programs pass 0.

The directory is the first token of `drive.cfg`, read with `fscanf("%s")` (`DriveCfg_Read`, VSHELL `0040d327`, and the top of `Sim_Run`), so a path with a space in it is cut at the space; a backslash is appended. Each program then reads the second token, the install's directory, the same way and discards it, and reads nothing more of the file; v1.10's `VSHELL.EXE`s and `VER95\DBSIM.EXE` do the same. Neither read is bounded: the first token lands in an 80-byte stack buffer in VSHELL and an 82-byte one in DBSIM, the second in an 80-byte buffer just below the first, so a second token longer than 79 characters runs into the first token before it is copied out, and a first token longer than 78, with its backslash, overruns its own buffer. A file whose `vol\<name>` equals the stored name of an archive already loaded is skipped. The first pass stores names in exactly that form, so the second pass skips any archive the install already supplied; the comparison is exact, case included. So an archive the installer copied is read from the install, one it left on the disc is found there, and an archive added to either folder is mounted with no change to the programs. v1.10's `SHELL1.VOL` and `SIMLANG.VOL` reach them that way, and a v1.10 install, which holds only its own language's voice archive ([`../retail-builds.md`](../retail-builds.md#the-installer)), mounts the other two from the disc.

A group holds 30 archives. `VolumeGroup_SetGroup` returns before scanning when the group is full, but does not check while it scans, so a 31st file is written past the end of the list.

`VolumeGroup_AddVolume` (VSHELL `00403a5c`, DBSIM `00467e90`) inserts each archive after every one whose precedence byte is equal or higher, and `VolumeGroup_FindEntry` returns the first volume that has the entry. So a `0x0A` archive's entry hides the same `folder\name` in a `0x05` one, and of two archives of equal precedence the one loaded first answers: the install's before the disc's, and within one folder in `findfirst`'s order. That order never decides what is read from the retail archives. Among one program's archives of equal precedence, the only shared `folder\name`s are SIMSOUND.VOL's 556 that SIMVOL0.VOL also carries, in both builds, and v1.0's 213 that SIMVOICE.VOL, SIMVOICF.VOL and SIMVOICG.VOL all carry under their one `SIMVOICE\` label; every one has the same content in each archive.

## The per-entry prefix — fixed 9 bytes

```
+0   byte    compression type       0x02 (stored) in all 3,004 entries
+1   int32   content size, LE       the content alone
+5   uint16  MS-DOS packed date     source file's timestamp
+7   uint16  MS-DOS packed time
+9   size    content
+9+size  byte  trailer, repeats content's last byte
```

Entry stride is therefore `size + 10`, and the last entry's trailer is the archive's last byte.

**`+1` is the content length, not the entry length.** Independently confirmed against the 729 RIFF WAVs in the sound and voice archives, whose own `RIFF` chunk header states their length: for 723 of them `riffSize + 8` equals the field at `+1` exactly. The other six (`CVM_0028.WAV` and `CVM_0035.WAV`, in each of the three voice archives) carry 470 bytes of data past the end of their RIFF chunk — a property of the source files, identical across all three languages, not of the container.

**`+5` is an MS-DOS timestamp, not a magic number.** Read as `[date:uint16][time:uint16]`, all 3,004 values decode to a valid calendar date and clock time, clustering in 1994 (1,145), 1995 (1,143) and 1996 (715). Read the other way round — time first — 2,515 of 2,578 are invalid, dating files to 2041 and later. Files built in the same batch share near-identical stamps: `ROCKET.BND`, `PSTATUS.BND`, `APPINPUT.BND` and `PMISSILE.BND` are all stamped 1996-01-27 15:23:2x.

**The trailer repeats the last content byte** — in all 2,578 entries checked, with no exception, including the 1,617 whose last byte is nonzero. It sits outside the declared size. VSHELL's `VolRStream_Read` (`004033f9`) reaches it: a read that runs past the content copies `size - position + 1` bytes, the rest of the content and then the trailer, and reports end of data.

**`+0` is a compression type, and every retail entry is stored.** VSHELL's `VolRStream_Open` (`00402d25`) reads type 2 straight from the archive, 7 through an `RLERStream` filter and 9 through an `LZHRStream` (an LZHUF-style decoder: 4,036-byte window, adaptive Huffman over 314 symbols), and asserts `Unknown compression type in volume file.` on any other value. All 3,004 entries are type 2, and the RIFF check above confirms the content is stored verbatim.

## Loose files on disk carry no prefix

The retail install's own override tree is content-only. `DATA\MAT0.DAT` (244 bytes) and `DATA\MFORMS.DAT` (142 bytes) are byte-identical to the content of the `dat\MAT0.DAT` and `dat\MFORMS.DAT` entries in SIMVOL0.VOL, whose size fields read 244 and 142 — no prefix, no trailer. So does `DATA\script.dat`, which the game reads straight off disk at offset 0.

**A loose file at an entry's own path takes precedence over the archive.** `VolRStream_Open` (VSHELL `00402d25`, DBSIM `00472fdc`) tries `_open(path)` before it searches the volume group, and the path is relative, so a loose file in the install wins; the disc is not searched for loose files. With a content-only `DAT\PROJ.DAT` in the game folder, beside `DBSIM.EXE`, DBSIM loaded it in place of `SIMVOL0.VOL`'s `dat\PROJ.DAT`: the record table it built in memory (`004a9980`) held the loose file's values. The retail install ships no `DAT` folder; this was a file placed there for the test ([`proj-dat.md`](proj-dat.md#lookup)).

What does carry a prefix is anything unpacked by a tool that copies the archive bytes wholesale. `ES2/VOL/extractVol.py` slices each entry from its offset to the next entry's offset, so every file under `ES2/VOL/simvol0/`, `ES2/VOL/ZONES/` and `ES2/VOL/SHELL0/` is `prefix + content + trailer`, ten bytes longer than the file the game reads.

That extraction is uniform. Comparing all 1,672 entries of those three archives against their extracted counterparts, byte for byte, against each entry's content as the directory and prefix delimit it: 1,672 are `prefix + content + trailer`, none are content-only, none differ otherwise, none are missing. A `ES2/VOL/<name>/` file is always ten bytes longer than its content, never sometimes.

## Rejected readings

| Reading | Why it is wrong |
|---|---|
| The 4 bytes at `+5` are an opaque magic number | They are a packed MS-DOS date and time, so the value differs between files built at different times. |
| The trailer is padding or alignment | The gap is exactly one byte for every entry regardless of size, and its value is the content's last byte, not zero. |
| Entries are compressed, because `+0` is a compression type | Type 2 is stored: the content is verbatim, and 723 WAVs match their own RIFF length. |

## Open

- **Open:** whether every resource either program reads goes through `VolRStream_Open`, and so can be overridden by a loose file. Only `DAT\PROJ.DAT` has been seen to override.
