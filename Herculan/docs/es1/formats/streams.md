# DBSIM.EXE and GO.EXE — file streams

ES1's simulator and its shell read and write files through the stream classes ES2 has: a base class; `FileRStream`, `FileWStream` and `FileRWStream`, which read, write, or read and write a plain file; `VolRStream`, which opens a loose file and falls back on the volume archives ([VOL archives](../../retail/formats/vol-archive.md)); and `RLERStream` and `LZHRStream`, which decode a compressed volume entry. The classes have ES2's members, sizes and field offsets, rearranged by Symantec C++'s layout ([object layout](compiler.md#object-layout)). Addresses are in `ES1DBSIM` unless `ES1GO` is named ([the X-32 executable](x32-executable.md#the-image)); `GO.EXE` has [the same seven classes](#goexe).

## The base class

The base object is 8 bytes: the status at `+0` and the vptr at `+4`, where ES2's puts the vptr first. Its vtable, `000989d2`, has ES2's slots one place later, because an ES1 vtable's slots start at `+4` ([vtables](compiler.md#vtables)). The base class's stubs return the value after the comma:

| Slot | Base (`000989d2`) | `FileRStream` (`00098a76`) | `FileWStream` (`00098ab2`) | `FileRWStream` (`0009b576`) | `VolRStream` (`0009683e`) | `RLERStream` (`0009ec3a`) | `LZHRStream` (`0009ebfe`) |
|---|---|---|---|---|---|---|---|
| `+4` | `Stream_BaseDtor` | `FileRStream_Dtor` | `FileWStream_Dtor` | `FileRWStream_Dtor` | `VolRStream_Dtor` | `RLERStream_Dtor` | `LZHRStream_Dtor` |
| `+8` | `StreamIO_GetPosition`, 0 | `FileRStream_Tell` | `FileWStream_Tell` | `FileRWStream_Tell` | `VolRStream_GetPosition` | `RLERStream_GetPosition` | `LZHRStream_GetPosition` |
| `+0xc` | `StreamIO_SetPosition`, 1 | `FileRStream_Seek` | `FileWStream_Seek` | `FileRWStream_Seek` | `VolRStream_SetPosition` | `RLERStream_SetPosition` | `LZHRStream_SetPosition` |
| `+0x10` | `StreamIO_GetCapabilities`, 0 | `FileRStream_GetCapabilities` | `FileWStream_GetCapabilities` | `FileRWStream_GetCapabilities` | `VolRStream_GetCapabilities` | `RLERStream_GetCapabilities` | `LZHRStream_GetCapabilities` |
| `+0x14` | `StreamIO_Flush`, 1 | `FileRStream_Flush`, 1 | `FileWStream_Flush`, 1 | `FileRWStream_Flush`, 1 | `VolRStream_Flush`, 1 | `RLERStream_Flush` | `LZHRStream_Flush` |
| `+0x18` | `StreamIO_Write`, 1 | `FileRStream_Write`, status 4 | `FileWStream_Write` | `FileRWStream_Write` | `VolRStream_Write`, status 4 | `RLERStream_Write`, status 4 | `LZHRStream_Write`, status 4 |
| `+0x1c` | `StreamIO_Read`, 1 | `FileRStream_Read` | `FileWStream_Read`, status 4 | `FileRWStream_Read` | `VolRStream_Read` | `RLERStream_Read` | `LZHRStream_Read` |

Most slots hold the address of the `0x90` padding before their function. The base class's stubs other than `StreamIO_GetCapabilities`, and the flush of the file classes and `VolRStream`, have no prologue: `StreamIO_Flush` is `MOV AL,1; RET 4` at `00064d6c`, entered at `00064d69`.

The status is 0 when the stream is good, 1 when a file cannot be opened or a DOS call fails with `errno` set, 2 at the end of the data, 3 when closing the handle fails or a DOS call fails with `errno` 0, 4 after a write to a stream that only reads or a read from one that only writes, 5 with no file, and 6 for a filter with no source.

`GetCapabilities` answers a query number:

| Query | Base | `FileRStream` | `FileWStream` | `FileRWStream` | `VolRStream` on a file | `VolRStream` on a volume entry | `RLERStream` | `LZHRStream` |
|---|---|---|---|---|---|---|---|---|
| 0 | 0 | 2 | 3 | 1 | 2 | the entry's compression type | 7 | 9 |
| 1 | 0 | 0 | 1 | 1 | 0 | 0 | 0 | 0 |
| 2 | 0 | 1 | 0 | 1 | 1 | 1 | 1 | 1 |
| 3 | 0 | 1 | 1 | 1 | 1 | 1 | 1 | 1 |
| 4 | 0 | 0 | 0 | 0 | 0 | 1 unless the type is 2 | 1 | 1 |

Any other query answers 0.

## The file classes

`FileRStream`, `FileWStream` and `FileRWStream` have the same code, statement for statement, apart from their vtables, the flags they open a path with, and the slots and answers in the tables above. Each holds the base, the DOS handle as a short at `+8` (-1 for none) and at `+0xa` whether the stream owns it. `FileWStream` allocates `0xc` bytes and the other two `0xb`, the sizes ES2's classes have. Each has a constructor that opens nothing, one that opens a path and one that adopts a handle without owning it, and `Close` closes only a handle the stream owns.

| Class | Opens a path with | Which means |
|---|---|---|
| `FileRStream` | `open(path, 0)` | read-only |
| `FileWStream` | `open(path, 0x301, 0x180)` | write-only, created when missing, truncated when present |
| `FileRWStream` | `open(path, 0x102, 0x180)` | read and write, created when missing |

The runtime's `open` (`00074947`) creates a missing file for flag `0x100`, truncates an existing one with a zero-length write for `0x200`, refuses an existing one for `0x400` with `0x100`, and moves to the end for `0x8`; mode bit `0x80` makes a created file writable. ES2's `FileWStream` and `FileRWStream` pass the same flags with `0x8000` added.

## VolRStream

`0x2f` bytes:

| Offset | Holds |
|---|---|
| `+8` | the DOS handle of a loose file, -1 for none |
| `+0xa` | the volume's stream that holds the open entry |
| `+0xe` | the stream reads go through: `+0xa` itself, or a filter |
| `+0x12` | the entry's offset in the volume |
| `+0x16` | the position in the entry |
| `+0x1a` | the volume stream's absolute position after the last access |
| `+0x1e` | the entry's compression type |
| `+0x22` | the entry's size |
| `+0x26` | whether the stream owns the handle |
| `+0x27` | the RLE filter, made on first use |
| `+0x2b` | the LZH filter, made on first use |

`VolRStream_Open` tries the path as a loose file first. When that fails it asks the volume group `g_VolumeGroup` (`00096838`) for the entry, and reads type 2 from the volume directly, 7 through the RLE filter and 9 through the LZH filter ([the filters](#the-rle-and-lzh-filters)), as VSHELL's reader does ([VOL archives](../../retail/formats/vol-archive.md)). Seeking an entry of type 7 or 9 closes the filter, seeks the volume stream to the entry's start, attaches the filter again and seeks the filter. `VolRStream_SetGroup` makes the group, `VolRStream_LoadVolume` adds a volume to it and `VolRStream_DeleteGroup` (`00056a7c`) deletes it. A read that runs past the entry's end reads one byte more than is left, from the volume stream itself rather than through `+0xe`, and stops the stream with status 2.

## The volume class

A volume is one open `.VOL` archive. ES1's archives have ES2's layout ([VOL archives](../../retail/formats/vol-archive.md#file-layout)): the signature `VOLN`, a program mask, a search precedence, the directory list and the entry list, and a 9-byte prefix before each entry's data. The install's seven:

| Archive | Program mask | Precedence |
|---|---|---|
| `SIMVOL0`, `SIMSOUND`, `SIMVOICE` | 1 | 5 |
| `SHELL0`, `SPEECH` | `0x100` | 5 |
| `ZONES` | `0x101` | 5 |
| `DETECT` | `0x10000` | 5 |

`Volume_LoadVolume(path, mask, bufferSize)` opens the archive as a `FileRStream`, checks the first three bytes of the signature and ignores the fourth, and loads nothing unless the program mask shares a bit with `mask`. It reads the rest through a `BuffRStream` over the file, with a buffer of `bufferSize` bytes and at least `0x80`, and returns no volume when that stream's status is not 0 at the end. The volume is `0x30` bytes:

| Offset | Holds |
|---|---|
| `+0` | the `FileRStream` |
| `+4` | the `BuffRStream`, which entries are read through |
| `+8` | the path the volume was loaded from, up to `+0x1b` |
| `+0x1c` | the search precedence |
| `+0x1d` | the directory count |
| `+0x1e` | the entry count, a word |
| `+0x20` | the program mask |
| `+0x24` | a pointer to each directory name |
| `+0x28` | the directory names |
| `+0x2c` | the entries, `0x12` bytes each |

`Volume_FindEntry` splits the path it is given with `Path_Split`. When the path has a directory, it must equal one of the volume's directory names, ignoring case; a path without one matches the entries whose directory index is `0xff`. The first entry with that directory and the path's name and extension, ignoring case, is the one found: it seeks the buffered stream to the entry's prefix, reads the prefix, and fills `VolRStream`'s entry fields from `+0xa`, giving the data's offset as the prefix's offset plus 9.

## The volume group

`g_VolumeGroup` is a `0x29`-byte object: up to 10 volume pointers from `+0` and their count at `+0x28`, where ES2's group holds `0x1e`. The volumes are kept in descending order of their search precedence, and `VolumeGroup_FindEntry` takes the entry from the first volume that has it.

`VolRStream_SetGroup(mask, extension, dir, dirFirst)` makes the group on its first call and refills it on later ones through `VolumeGroup_SetGroup`. That removes the volumes whose program mask shares no bit with `mask`, then loads each file matching `vol\*<extension>`, once under `dir` and once without it (`dir` first when `dirFirst` is nonzero), whose `vol\<name>` is not loaded already, passing `mask` to the volume loader. It tests the limit of 10 once, before it scans; the install's `VOL\` holds seven volumes. `VolRStream_LoadVolume` adds one named volume, with the mask `0xffffffff`, unless one of that name is loaded.

`main` (`00059350`) sets the volume buffer size to `0x200` and then calls `VolRStream_SetGroup` with mask 1 and extension `.vol`, so of the install's archives the simulator loads `SIMVOL0`, `SIMSOUND`, `SIMVOICE` and `ZONES`. When that call makes the group, `VolumeGroup_Ctor` sets the buffer size to `0x400` before it loads a volume.

## The RLE and LZH filters

A filter is a stream that decodes what it reads from another stream, its source. `RLERStream` and `LZHRStream` are ES2's classes of those names statement for statement, and decode ES2's formats ([the decompression filters](../../retail/runtime-library.md#the-decompression-filters)): run-length bytes, and Okumura's LZHUF. ES2's call the source through small wrappers where ES1's call its slots directly.

A filter holds its source at `+8`; with none it has status 6, and `Flush` answers whether it has one. `Open` attaches a source at its current position and takes its status, and `Close` calls the filter's own `Flush` before it detaches. The position a filter reports is a count of decoded bytes. `SetPosition` to a position below the count seeks the source to 0, not to where the filter was attached, and starts the count again, leaving the decoder's state as it was; either way the filter decodes and discards up to the position in 500-byte reads into a stack buffer. `RLERStream_SetPosition` refuses any nonzero status, `LZHRStream_SetPosition` only status 6.

| `RLERStream`, `0x92` bytes | Holds |
|---|---|
| `+0xc` | the byte the current run repeats |
| `+0xd` | the copies of it still to come |
| `+0xe` | the literal bytes still to come |
| `+0x8e` | the count: one per byte decoded, and one more per run's value byte read |

| `LZHRStream`, `0x28` bytes | Holds |
|---|---|
| `+0xc` | the Huffman coder |
| `+0x10` | the window, `0x103b` bytes (LZHUF's 4096 + 60 − 1), of which the decoder uses the first 4096 |
| `+0x14` | the window position of the current match |
| `+0x18` | the match's length |
| `+0x1c` | the bytes of it copied so far |
| `+0x20` | the window position the next byte is written to |
| `+0x24` | the count |

The coder, `LZHuffman` (`00082734`–`00082f5e`), is ES2's class of that name, `0x16` bytes with no vptr: the source at `+0`; three tables, LZHUF's frequencies at `+4`, parent links at `+8` and child links at `+0xc`; the input bits at `+0x10` with their count at `+0x12`; and the output bits at `+0x13` with their count at `+0x15`. Its encoder half, `LZHuffman_EncodeChar`, `LZHuffman_EncodePosition`, `LZHuffman_PutCode` and `LZHuffman_EncodeEnd`, writes through the stream's slot `+0x18`. LZHUF's four position tables are at `000a7c90` (`p_len`), `000a7cd0` (`p_code`), `000a7d10` (`d_code`) and `000a7e10` (`d_len`), byte for byte ES2's.

## GO.EXE

`ES1GO` has the same seven classes:

| Class | Vtable | Constructors |
|---|---|---|
| base | `00089fa2` | `0005d9c0` |
| `FileRStream` | `00086f8a` | `00052a20`, `00052a5c`, `00052ab0` |
| `FileWStream` | `00086fc6` | `00052d9c`, `00052dd8`, `00052e2c` |
| `FileRWStream` | `0008880e` | `0005aa54`, `0005aa90`, `0005aae4` |
| `VolRStream` | `00083c16` | `0004cf84`, `0004cfe3`, `0004d064` |
| `RLERStream` | `0008b00a` | `00064460`, `000644c8` |
| `LZHRStream` | `0008b08a` | `00064ec0`, `00064ef8` |

The base class, the three file classes and the two filters are `ES1DBSIM`'s code byte for byte, relocated operands aside, and so are `LZHuffman` (`0006f85c`–`00070086`), its position tables (`000942c4`–`00094543`), and the runtime's `read`, `write`, `open` and `close` the file classes call. `VolRStream` has `ES1DBSIM`'s statements in different code: its functions follow one another with no `0x90` padding, some branches are laid out in the other order, and its stubs load arguments they do not use (`VolRStream_Write`, `0004d6b8`, loads all three before it stores status 4). The volume class (`0004ca58`–`0004cf83`) and the volume group (`0004d82c`–`0004dde3`) are the same: `ES1DBSIM`'s statements in different code, their loops tested at the top where `ES1DBSIM`'s are tested at the bottom. `BuffRStream`'s constructor, `Path_Split` and `strrchr` are `ES1DBSIM`'s code byte for byte. The group's pointer is `00083c04`. `main` (`00012ec7`) makes it with mask `0x100` and extension `.vol`, so of the install's archives the shell loads `SHELL0`, `SPEECH` and `ZONES`, after the same buffer size of `0x200`, and deletes it through `VolRStream_DeleteGroup` (`0004d37b`).

## Open

- **Open:** `BuffRStream` (`ES1DBSIM` vtable `0009eb96`, `ES1GO` `0008b04e`) beyond `BuffRStream_Ctor`, `BuffRStream_InitBuffer` and `BuffRStream_SetSource`.
- **Open:** which program loads `DETECT.VOL`, whose program mask `0x10000` shares no bit with either `main`'s.
- **Open:** where the directory and the `dirFirst` flag that each `main` passes to `VolRStream_SetGroup` come from.
- **Open:** what `RLERStream` holds from `+0xf` to `+0x8d`; its twelve functions use none of it, and ES2's class has the same gap.
- **Open:** what each `GetCapabilities` query asks; no caller's use of the answers has been read.
- **Open:** a caller of `ES1DBSIM`'s `VolRStream_DeleteGroup`: `es2_xref` finds none, at its entry or at its padding.
