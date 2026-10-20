# GO.EXE and DBSIM.EXE — bitmap arrays, fonts and cursors

ES1's bitmap arrays, fonts and cursors are ES2's containers, loaded by ES2's code ([ES2's resource envelope](../../retail/formats/dfn-hfn-dci.md#the-shared-dynamix-resource-envelope)): 327 of `SHELL0.VOL`'s and `SIMVOL0.VOL`'s 328 `.DBA` read through ES2's bitmap array reader and write back byte for byte, all 30 `.DFN` read through ES2's font layout, and all 11 `.DCI` through ES2's cursor layout. `RES32\ICONS.DBA` holds old-format bitmaps, which both ES1 programs load as no object, as ES2's do, and the file stops 528 bytes short of its last item's end. Addresses are `ES1GO` image addresses unless `ES1DBSIM` is named ([the X-32 executable](x32-executable.md#the-image)); ES2's are DBSIM's.

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

## Fonts

The class tables register `GLFont` under tag `0x05002800` and `Old GLFont` under `0x03002800`, as ES2's do; `GLFont`'s load function is `GLFont_Construct` (`00066634`; `ES1DBSIM 0007cd9c`). `GLFont_Construct` takes 0x32 bytes from the current arena (`Arena_Alloc` on `000aa110`), stores the vtable `0008b5da` (`ES1DBSIM 000a01ba`) and calls its read slot, `+0x10` (ES2's is `+0xc`), `GLFont_ReadFromStream` (`00066978`; `ES1DBSIM 0007d0e0`). That reads [ES2's layout](../../retail/formats/dfn-hfn-dci.md#layout) in ES2's order and under ES2's two conditions, arrays from the current arena; `GLFont_WriteToStream` (`0006678c`; `ES1DBSIM 0007cef4`) writes it back the same way, and `GLFont_GetStreamSize` (`000666ec`; `ES1DBSIM 0007ce54`) and `GLFont_GetMemorySize` (`0006673c`; `ES1DBSIM 0007cea4`) compute ES2's sizes, from `0x1a` and `0x32`.

`SIMVOL0.VOL` holds 16 `.DFN` and `SHELL0.VOL` 14, four names in both: `ACTIVE`, `INACTIVE`, `PUSHED` and `TITLE`, whose two copies differ. There are no `.HFN`. Every file has 223 glyphs from code 32, `widthsFlag` -1 and an array count of 0, and its pool, offsets and widths end where its envelope's size does. The cell height is 8 in the shell's `MAP` and the simulator's ten `CP*` and `RED6X8`, 18 in both `TITLE`, and 10 in the rest; `inkHeight` (`0x1a`) is 7 in every file, where ES2's are 8 and 11.

## Cursors

The class tables register `GLCursor` under tag `0x0B002800`. Its load function, `GLCursor_Construct` (`000663b0`; `ES1DBSIM 0007dc68`), takes 0x10 bytes from the current arena, constructs the cursor with `GLCursor_Ctor` (`00051460`; `ES1DBSIM 0006b348`), which stores the vtable `00086e4a` (`ES1DBSIM 0009b672`) and zeroes the bitmap `+4`, and calls the read slot, `+0x10` (ES2's is `+0xc`). `GLCursor_ReadFromStream` (`00066434`; `ES1DBSIM 0007dcec`) reads 8 bytes, the hotspot's x and y, into `+8`, then one class item, the bitmap, into `+4` with `ClassItem_LoadResource`: [ES2's layout](../../retail/formats/dfn-hfn-dci.md#dci--cursor-image). The other slots match ES2's one place later:

| Slot | Function | Does |
|---|---|---|
| `+0x04` | `GLCursor_GetClassTag` (`000663e8`; `ES1DBSIM 0007dca0`) | returns `0x28000b` |
| `+0x08` | `GLCursor_GetStreamSize` (`000663f0`; `ES1DBSIM 0007dca8`) | `ClassItem_ItemTotalSize` of the bitmap plus 8, or 0 when that is 0 |
| `+0x0c` | `GLCursor_GetMemorySize` (`00066418`; `ES1DBSIM 0007dcd0`) | the bitmap's slot `+0xc` plus 0x10 |
| `+0x14` | `GLCursor_WriteToStream` (`00066464`; `ES1DBSIM 0007dd1c`) | the hotspot, then the bitmap with `ClassItem_StoreItem` |
| `+0x1c` | `GLCursor_Dtor` (`000514e4`; `ES1DBSIM 0006b3cc`) | destroys the bitmap through its slot `+0x1c` with 3, stores the base vtable `00086e22` (`ES1DBSIM 0009b64a`), and frees the cursor when bit 0 of its argument is set |

| Archive | Files | Size, hotspot |
|---|---|---|
| `SIMVOL0` | `dci\`: `CURSOR`, `ECURSOR`, `MCURSOR`, `NCURSOR`, `PCURSOR`, `SCURSOR`, `WCURSOR` and `ALERT` | ES2's bytes, ES2's seven in `SIMVOL0.VOL` and its `SIMALERT.VOL`'s `ALERT` ([ES2's table](../../retail/formats/dfn-hfn-dci.md#dci--cursor-image)) |
| `SHELL0` | `DCI\ALERT.DCI` | the simulator's `ALERT.DCI`: 8 × 8, (0, 0) |
| `SHELL0` | `RES32\ARROW.DCI` | the same bytes as `ALERT.DCI` |
| `SHELL0` | `RES32\ICONS.DCI` | 16 wide, 11 high, (1, 1) |

The shell's start-up, `00045d24`, loads `res32\arrow.dci` with `Resource_LoadPath` (`0004b823`), which opens the path with `VolRStream_CtorFromPath`, loads one class item with `ClassItem_LoadResource`, closes and destroys the stream, and returns the item. It then passes the cursor's bitmap `+4` and its hotspot `+8` to `Cursor_SetImage`.

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
- **Open:** what loads the cursors the simulator's two loaders leave: `Cursor_LoadCursorResource` (`ES1DBSIM 00057640`) loads `dci\cursor` for the pointer ([the mission start](../simulation/main-loop.md#the-mission-start--sim_startmission-0005a9dc)) and `ColorSchemePanels_LoadAll` the cockpit's seven ([building it](../simulation/cockpit-panel.md#building-it)); `AlertPanel_Enter` (`00045f00`) loads `dci\alert` once for the in-mission panels ([putting a panel up](../simulation/alert-panels.md#putting-a-panel-up-and-taking-it-down)); what loads `RES32\ICONS.DCI` and the shell's `DCI\ALERT.DCI` was not read. A case-insensitive search of both images finds `.dci` only in `res32\arrow.dci`, the simulator's seven cursor names without their extension, and a `dci` folder name in each.
- **Open:** what the old record's byte at `+0x0c` and the three zeroes after it are, and what wrote the 2 + 3 × count bytes past each item's pixels.
