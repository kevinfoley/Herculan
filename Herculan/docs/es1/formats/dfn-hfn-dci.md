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

Item 10 runs to `0x145e`, 528 bytes past the file's end, which is where the header's size puts the end of the array (8 + 5,206). No item is padded: every size is even. Item 10's 176 pixels end 2 bytes before the file does.

### The old bitmap record

Both programs load an old-format item as no object, so its layout comes from the file's bytes alone. Counting from the item's tag:

```
+0x00  4 bytes  00 00 28 00   the old bitmap tag
+0x04  uint32   size of what follows
+0x08  int16    rows
+0x0a  int16    cols
+0x0c  uint8    8 in every item
+0x0d  3 bytes  0 in every item
+0x10  uint32   pixel count, rows × cols in every item
+0x14  [count]  pixels, row by row, one palette index each
```

Read with `cols` as the row length, items 0 and 1 draw small icons in indices `0x14`–`0x16` on index 0: an 11-row arrow and a 13-row figure 8 wide. The new record's fields come in the same order, its flags and packing bytes where the first two of these zeroes are and its data length one byte earlier, with an extra count after it ([ES2's bitmap record](../../retail/formats/dfn-hfn-dci.md#the-bitmap-record)).

The rest of each item, 2 + 3 × count bytes, so that every size is 14 + 4 × count, is not image data. In item 0 it holds zeros, an MS-DOS directory entry naming `QBASIC.INI` at file offset `0xe0`, and from `0x200` a run of 16-bit values that mostly count up by one, with `0xFFFF` among them.

### What loads it

The shell's `icons` (`0007e249`) is pushed at `000093ed` with the folder pointer `00086840`, `dba`, so it names `DBA\ICONS.DBA`; the simulator's `icons` (`ES1DBSIM 00094982`) is pushed at `ES1DBSIM 00041cc7` with its own `dba` pointer `000980f8`. `GO.EXE` names its `RES32\` files as whole literal paths (74 of them, among them `res32\corners.dba`, which `Shell_Main` passes to `0004b7df`), and its six bare `res32\` prefixes sit among `.ttm` names.

## Open

- **Open:** what loads `RES32\ICONS.DBA`, if anything. A case-insensitive search of both images and of `SHELL0.VOL`'s and `SIMVOL0.VOL`'s bytes finds `icons` only in the two `dba` names above and the archives' directory entries, where the same search finds `res32\corners.dba` in `GO.EXE`.
- **Open:** what the old record's byte at `+0x0c` and the three zeroes after it are, and what wrote the 2 + 3 × count bytes past each item's pixels.
