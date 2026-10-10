# DBSIM.EXE — file streams

ES1's simulator reads its files through the stream classes ES2 has: a base class, `FileRStream` for a plain file, and `VolRStream`, which opens a loose file and falls back on the volume archives ([VOL archives](../../retail/formats/vol-archive.md)). The classes have ES2's members, sizes and field offsets, rearranged by Symantec C++'s layout ([object layout](compiler.md#object-layout)). Addresses are in `ES1DBSIM` ([the X-32 executable](x32-executable.md#the-image)).

## The base class

The base object is 8 bytes: the status at `+0` and the vptr at `+4`, where ES2's puts the vptr first. Its vtable, `000989d2`, has ES2's slots one place later, because an ES1 vtable's slots start at `+4` ([vtables](compiler.md#vtables)):

| Slot | Base (`000989d2`) | `FileRStream` (`00098a76`) | `VolRStream` (`0009683e`) |
|---|---|---|---|
| `+4` | `Stream_BaseDtor` | `FileRStream_Dtor` | `VolRStream_Dtor` |
| `+8` | `StreamIO_GetPosition`, 0 | `FileRStream_Tell` | `VolRStream_GetPosition` |
| `+0xc` | `StreamIO_SetPosition`, 1 | `FileRStream_Seek` | `VolRStream_SetPosition` |
| `+0x10` | `StreamIO_GetCapabilities`, 0 | `FileRStream_GetCapabilities` | `VolRStream_GetCapabilities` |
| `+0x14` | `StreamIO_Flush`, 1 | `FileRStream_Flush`, 1 | `VolRStream_Flush`, 1 |
| `+0x18` | `StreamIO_Write`, 1 | `FileRStream_Write`, status 4 | `VolRStream_Write`, status 4 |
| `+0x1c` | `StreamIO_Read`, 1 | `FileRStream_Read` | `VolRStream_Read` |

Most slots hold the address of the `0x90` padding before their function. The base class's stubs other than `StreamIO_GetCapabilities`, and the two classes' flush, have no prologue: `StreamIO_Flush` is `MOV AL,1; RET 4` at `00064d6c`, entered at `00064d69`.

The status is 0 when the stream is good, 1 when a file cannot be opened or a DOS call fails with `errno` set, 2 at the end of the data, 3 when closing the handle fails or a DOS call fails with `errno` 0, 4 after a write to these read-only streams, and 5 with no file.

## FileRStream

`0xb` bytes: the base, the DOS handle as a short at `+8` (-1 for none) and at `+0xa` whether the stream owns it. `FileRStream_Ctor` opens a path, `FileRStream_CtorFromHandle` adopts a handle without owning it, and `FileRStream_Close` closes only a handle it owns.

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

`VolRStream_Open` tries the path as a loose file first. When that fails it asks the volume group at `00096838` (made by `VolRStream_SetGroup`, loaded by `VolRStream_LoadVolume`) for the entry, and reads type 2 from the volume directly, 7 through the RLE filter and 9 through the LZH filter, as VSHELL's reader does ([VOL archives](../../retail/formats/vol-archive.md)). A read that runs past the entry's end reads one byte more than is left, from the volume stream itself rather than through `+0xe`, and stops the stream with status 2.

## Open

- **Open:** the volume group at `00096838` (`000566b8`, `00056620`, `00056180`, `000562d8`), and the RLE and LZH filter classes (`00074f9c`, `00075068`, `0007509c`; `00074b8c`, `00074c70`, `00074ccc`).
- **Open:** the 12-byte stream class whose vtable is `00098ab2` (constructors `000658bc` and `000658f8`), which has its own read and write slots.
- **Open:** `GO.EXE`'s copies of these classes.
