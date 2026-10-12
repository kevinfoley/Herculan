# GO.EXE and DBSIM.EXE — bitmap arrays

ES1's bitmap arrays are ES2's container, loaded by ES2's code ([ES2's resource envelope](../../retail/formats/dfn-hfn-dci.md#the-shared-dynamix-resource-envelope)): 327 of `SHELL0.VOL`'s and `SIMVOL0.VOL`'s 328 `.DBA` read through ES2's bitmap array reader and write back byte for byte. `RES32\ICONS.DBA` holds old-format bitmaps, which both ES1 programs load as no object, as ES2's do, and the file stops 528 bytes short of its last item's end. Addresses are `ES1GO` image addresses unless `ES1DBSIM` is named ([the X-32 executable](x32-executable.md#the-image)); ES2's are DBSIM's.

## The class table

The GL class table, `{tag, load function, name}` entries, is at `0008b480` in `ES1GO` and `0009f198` in `ES1DBSIM`. `ES1GO`'s first four entries:

| Tag (BE dword) | Name | Load function |
|---|---|---|
| `0x00002800` | `Old GLBitmap` | `OldGLBitmap_Construct` (`00066058`) |
| `0x0E002800` | `GLBitmap` | `GLBitmap_Construct` (`00066020`) |
| `0x01002800` | `GLBitmapArray` | `GLBitmapArray_Construct` (`00066d7c`) |
| `0x0F002800` | `GLPalette` | `0005b1e8` |

`ES1DBSIM`'s table opens with the same two bitmap tags and names, their load functions `OldGLBitmap_Construct` (`000769a8`) and `00076970`. `OldGLBitmap_Construct` returns 0 in both, as ES2's does. `GLBitmap_Construct` takes 0x1a bytes from the current arena (`Arena_Alloc` on `000aa110`), constructs the bitmap with `000500ac` and calls its read slot, `+0x10` (ES2's is `+0xc`), with its two arguments. `GLBitmap_GetClassTag` (`00066060`) returns `0x28000e`, and `GLBitmap_GetStreamSize` (`00066068`) 13 plus the data length (`+0x0a`) plus four times the extra count (`+0x14`), the record's size after its envelope.

`GLBitmapArray_Construct` takes 0xc bytes from the current arena, stores the vtable `0008b626` and zeroes `+4` and `+8`, and calls the read slot `+0x10`, `GLBitmapArray_ReadFromStream` (`00066ed4`). That reads the frame count as one `int32` into `+4`, takes count × 4 bytes from the arena for `+8`, and fills each with `ClassItem_LoadResource`(0, stream), as ES2's does. `ClassItem_LoadResource` follows the item's load function with `ClassItem_SkipToEnd` (`00000ea8`), which reads single bytes until the stream's position reaches the end of the current item (the frame `0009a2c8`, `+8`) or its status is set. An old-format item therefore leaves a null element and its bytes passed over.

## `RES32\ICONS.DBA`

4,686 bytes; `DBA\ICONS.DBA`, 10,268 bytes, is an ordinary array. The header's size field is 5,206 and its count 11. All eleven items carry the old bitmap tag:

| Item | Offset | Size after the envelope |
|---|---|---|
| 0 | `0x00c` | 718 |
| 1–4 | `0x2e2`, `0x498`, `0x64e`, `0x804` | 430 |
| 5 | `0x9ba` | 174 |
| 6 | `0xa70` | 366 |
| 7 | `0xbe6` | 526 |
| 8 | `0xdfc` | 302 |
| 9 | `0xf32` | 590 |
| 10 | `0x1188` | 718 |

Item 10 runs to `0x145e`, 528 bytes past the file's end, which is where the header's size puts the end of the array (8 + 5,206). In every item, counting from its tag, the `int16` words at `+8` and `+10` multiply to the `uint32` at `+16` (11 × 16 = 176 in item 0), the byte at `+12` is 8, and the three bytes after it are 0; the file's bytes from `0xe0`, in item 0, hold an MS-DOS directory entry naming `QBASIC.INI`.

The shell's `icons` (`0007e249`) is pushed at `000093ed` with the folder pointer `00086840`, `dba`, so it names `DBA\ICONS.DBA`. A search of `GO.EXE`'s and `SHELL0.VOL`'s bytes for `icons.dba`, any case, finds only the archive's two directory entries.

## Open

- **Open:** what loads `RES32\ICONS.DBA`, if anything.
- **Open:** the old bitmap format, whose load function returns 0 in both programs. The words above read as a 16-byte header (rows, columns, bits per pixel, data length) and the pixels after it, and each item's size is 14 + 4 × that data length.
