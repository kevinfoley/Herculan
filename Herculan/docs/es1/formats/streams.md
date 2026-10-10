# DBSIM.EXE and GO.EXE — file streams

ES1's simulator and its shell read and write files through the stream classes ES2 has: a base class; `FileRStream`, `FileWStream` and `FileRWStream`, which read, write, or read and write a plain file; and `VolRStream`, which opens a loose file and falls back on the volume archives ([VOL archives](../../retail/formats/vol-archive.md)). The classes have ES2's members, sizes and field offsets, rearranged by Symantec C++'s layout ([object layout](compiler.md#object-layout)). Addresses are in `ES1DBSIM` unless `ES1GO` is named ([the X-32 executable](x32-executable.md#the-image)); `GO.EXE` has [the same five classes](#goexe).

## The base class

The base object is 8 bytes: the status at `+0` and the vptr at `+4`, where ES2's puts the vptr first. Its vtable, `000989d2`, has ES2's slots one place later, because an ES1 vtable's slots start at `+4` ([vtables](compiler.md#vtables)). The base class's stubs return the value after the comma:

| Slot | Base (`000989d2`) | `FileRStream` (`00098a76`) | `FileWStream` (`00098ab2`) | `FileRWStream` (`0009b576`) | `VolRStream` (`0009683e`) |
|---|---|---|---|---|---|
| `+4` | `Stream_BaseDtor` | `FileRStream_Dtor` | `FileWStream_Dtor` | `FileRWStream_Dtor` | `VolRStream_Dtor` |
| `+8` | `StreamIO_GetPosition`, 0 | `FileRStream_Tell` | `FileWStream_Tell` | `FileRWStream_Tell` | `VolRStream_GetPosition` |
| `+0xc` | `StreamIO_SetPosition`, 1 | `FileRStream_Seek` | `FileWStream_Seek` | `FileRWStream_Seek` | `VolRStream_SetPosition` |
| `+0x10` | `StreamIO_GetCapabilities`, 0 | `FileRStream_GetCapabilities` | `FileWStream_GetCapabilities` | `FileRWStream_GetCapabilities` | `VolRStream_GetCapabilities` |
| `+0x14` | `StreamIO_Flush`, 1 | `FileRStream_Flush`, 1 | `FileWStream_Flush`, 1 | `FileRWStream_Flush`, 1 | `VolRStream_Flush`, 1 |
| `+0x18` | `StreamIO_Write`, 1 | `FileRStream_Write`, status 4 | `FileWStream_Write` | `FileRWStream_Write` | `VolRStream_Write`, status 4 |
| `+0x1c` | `StreamIO_Read`, 1 | `FileRStream_Read` | `FileWStream_Read`, status 4 | `FileRWStream_Read` | `VolRStream_Read` |

Most slots hold the address of the `0x90` padding before their function. The base class's stubs other than `StreamIO_GetCapabilities`, and every class's flush, have no prologue: `StreamIO_Flush` is `MOV AL,1; RET 4` at `00064d6c`, entered at `00064d69`.

The status is 0 when the stream is good, 1 when a file cannot be opened or a DOS call fails with `errno` set, 2 at the end of the data, 3 when closing the handle fails or a DOS call fails with `errno` 0, 4 after a write to a stream that only reads or a read from one that only writes, and 5 with no file.

`GetCapabilities` answers a query number:

| Query | Base | `FileRStream` | `FileWStream` | `FileRWStream` | `VolRStream` on a file | `VolRStream` on a volume entry |
|---|---|---|---|---|---|---|
| 0 | 0 | 2 | 3 | 1 | 2 | the entry's compression type |
| 1 | 0 | 0 | 1 | 1 | 0 | 0 |
| 2 | 0 | 1 | 0 | 1 | 1 | 1 |
| 3 | 0 | 1 | 1 | 1 | 1 | 1 |
| 4 | 0 | 0 | 0 | 0 | 0 | 1 unless the type is 2 |

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

`VolRStream_Open` tries the path as a loose file first. When that fails it asks the volume group `g_VolumeGroup` (`00096838`) for the entry, and reads type 2 from the volume directly, 7 through the RLE filter and 9 through the LZH filter, as VSHELL's reader does ([VOL archives](../../retail/formats/vol-archive.md)). `VolRStream_SetGroup` makes the group, `VolRStream_LoadVolume` adds a volume to it and `VolRStream_DeleteGroup` (`00056a7c`) deletes it. A read that runs past the entry's end reads one byte more than is left, from the volume stream itself rather than through `+0xe`, and stops the stream with status 2.

## GO.EXE

`ES1GO` has the same five classes:

| Class | Vtable | Constructors |
|---|---|---|
| base | `00089fa2` | `0005d9c0` |
| `FileRStream` | `00086f8a` | `00052a20`, `00052a5c`, `00052ab0` |
| `FileWStream` | `00086fc6` | `00052d9c`, `00052dd8`, `00052e2c` |
| `FileRWStream` | `0008880e` | `0005aa54`, `0005aa90`, `0005aae4` |
| `VolRStream` | `00083c16` | `0004cf84`, `0004cfe3`, `0004d064` |

The base class and the three file classes are `ES1DBSIM`'s code byte for byte, relocated operands aside, and so are the runtime's `read`, `write`, `open` and `close` they call. `VolRStream` has `ES1DBSIM`'s statements in different code: its functions follow one another with no `0x90` padding, some branches are laid out in the other order, and its stubs load arguments they do not use (`VolRStream_Write`, `0004d6b8`, loads all three before it stores status 4). Its group pointer is `00083c04`, which `VolRStream_DeleteGroup` (`0004d37b`) deletes when `main` (`00012ec7`) calls it.

## Open

- **Open:** the volume group (`ES1DBSIM` `000566b8`, `00056620`, `00056180`, `000562d8`, `000561d8`; `ES1GO` `0004dd96`, `0004dcf7`, `0004d82c`, `0004d98f`, `0004d89a`), and the RLE and LZH filter classes (`ES1DBSIM` `00074f9c`, `00075068`, `0007509c`; `00074b8c`, `00074c70`, `00074ccc`; `ES1GO` `00064460`, `0006452c`, `00064560`; `00064ec0`, `00064fa4`, `00065000`).
- **Open:** what each `GetCapabilities` query asks; no caller's use of the answers has been read.
- **Open:** a caller of `ES1DBSIM`'s `VolRStream_DeleteGroup`: `es2_xref` finds none, at its entry or at its padding.
