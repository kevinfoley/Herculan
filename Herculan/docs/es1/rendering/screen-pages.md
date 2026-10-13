# GO.EXE and DBSIM.EXE — screen pages and 2D drawing

**The shell draws each frame on one page of video memory while it shows another, and keeps the screen's clean background on a third, so that what it drew can be wiped by copying the background back over it.** The VGA display holds four 320 × 200 pages. Each frame the shell shows the page it has just drawn, makes the other its page to draw on, and copies the background over the parts of that page drawn two frames earlier. A script's animation is drawn again on every tick ([running a script](../formats/ttm-scripts.md#running-a-script)), so this copy is what erases its last frame; a rect a script fixes is copied into the background, and stays. The `.TTM` drawing commands ([the commands](../formats/ttm-scripts.md#the-commands)) draw through the routines below. Addresses are `ES1GO` image addresses unless `ES1DBSIM` is named ([the image](../formats/x32-executable.md#the-image)).

ES2's simulator has the same three pages, on paged display targets only ([ES2's `-b` paged path](../../retail/simulation/cockpit-views.md#the--b-paged-path)).

## The pages

The shell's display, the object at `000ab616`, is made by `Shell_CreateDisplay` (`00045b25`), which `00045d24` calls: `0x8c` bytes, constructed by `0005a388` for the driver named `VGA_8`. Its fields from `+0x24` on sit four bytes lower than the simulator's and ES2's, whose `GLDisplay_GetAttribute` has an attribute at `+0x24` the shell's lacks:

| Offset | Holds |
|---|---|
| `+8`, `+0xc` | the width and height, `GLDisplay_GetAttribute` (`0005a6e4`) items 0 and 1 |
| `+0x30` | the visible page, the one on the screen |
| `+0x34` | the source page, which a copy between pages reads |
| `+0x38` | the draw page, which drawing writes |
| `+0x40` | the clip block `Ttm_RunScript` sets ([choosing a page](../formats/ttm-scripts.md#choosing-a-page)) |
| `+0x44` | the four pages' origins, `int32` {*x*, *y*} pairs: (0, 0), (0, 200), (0, 400), (0, 600), each built by `ShellDisplay_PagePointCtor` (`00045d09`) |
| `+0x64` | four more such points, built the same way and then zeroed |
| `+0x84`, `+0x88` | the two pages [presenting a frame](#presenting-a-frame) swaps, 0 and 1 |

`Shell_CreateDisplay` sets the draw page to 1, the source page to `+0x88`, 1, and the visible page to `+0x84`, 0, then calls `0005a5d0`(display) and `0005a77c`(display, 2, 9), and makes the render context `00082754` with `000512a0`(0), its `+0x208` set to 1 and `+0x20c` to 0, and installs it. A render context holds its clip block at `+4`, so its `+0x208` and `+0x20c` are the block's clip mode `+0x204` and translation flag `+0x208` ([the display drivers](polygon-shading.md#the-display-drivers)).

**Setting a page.** While the display is the current one, `000ab616`, a store to its visible, source or draw page is copied to `00084486`, `0008448a` or `0008448e` and passed to the raster's routine at `000865f8`, `000865f0` or `000865f4`: slots 18, 16 and 17 of the shell's routine table at `000865b0`, which `Raster_InstallDriverTable` fills from the driver's. A function that changes the source or draw page for a copy puts back the low byte of the value it found. `VGA_8`'s table (`0008b2f0`) holds:

| Slot | Routine | Does |
|---|---|---|
| 15 | `VGA8_SetPageOrigin` (`00070313`) | given a page and a point {*x*, *y*}: stores the page's offset, the row table `00088852`'s entry *y* + *x* >> 2, at `00088e92` + 4 · page, and its pan, (*x* & 3) · 2 while `00084476` is set and 0 otherwise, at the byte `00088eba` + page; then sets the page again through slot 17, 16 or 18 when it is the current draw (`0008448e`), source (`0008448a`) or visible (`00084486`) page |
| 16 | `VGA8_SetSourcePage` (`000701d4`) | the page's offset to `00088ec4` and its pan >> 1 to `00088ed0` |
| 17 | `VGA8_SetDrawPage` (`000701f5`) | the same to `00088ec8` and `00088ed4`, by which the drawing routines address the draw page, as in [the simulator's `VGA_8`](polygon-shading.md#the-display-drivers) |
| 18 | `VGA8_ShowPage` (`00070216`) | shows the page: by `00084472`, 0 waits for nothing, 1 waits out a vertical retrace in progress, and any other value waits for the next retrace to start and then to end (port `3DA` bit 3); writes the page's offset to the CRTC start address (port `3D4`, registers `0xc` and `0xd`); when `00084472` is not 0 waits for the next retrace to start; writes the pan to the attribute controller's register `0x13` (port `3C0`, index `0x33`); and stores the offset and pan >> 1 at `00088ecc` and `00088ed8`, calling the routine at `0008662c` before and the one at `00086628` after while `00084424` is set, and copying the 8 bytes at `00084428` to `00084452` before and back after. `0008444e` is 0 while it runs and 1 after |

### Copying between pages

`Region_CopyBetweenPages` (`00051c40`), given a region and an offset point, C convention, is ES2's (DBSIM `00487d54`): for a region of type 0, a rect {*x0*, *y0*, *x1*, *y1*} at `+4`, `Region_CopyRectBetweenPages` (`000519bc`); for type 2, a pointer at `+4` to a [span list](../../retail/rendering/polygon-fill.md#span-lists), `Region_CopySpansBetweenPages` (`00051aed`); for any other type nothing. A region is laid out as `Region_Restore`'s.

- **`Region_CopyRectBetweenPages`** adds the offset to the rect, and the block's translation (`+0x20c` − `+0x21c`, `+0x210` − `+0x220`) too when `+0x208` is set, and passes the rect and the offset, as it was given, to slot 20 (`00086600`). In clip mode 1 it first intersects the rect with the block's rect at `+0x20c` (`0006659c`), copying nothing when they do not meet; in any other clip mode it walks the block's region list from its last entry to its first, copying the rect's intersection with each rect region through slot 20 and its cut to each span region (`0004fd65`) through slot 19 (`000865fc`), and passing over a region of any other type.
- **`Region_CopySpansBetweenPages`** copies the span list moved by the same amount into the scratch list `00084495` points to and passes it with the offset to slot 19: whole in clip mode 0; cut to the block's rect (`0004fd65`, into the list `00084499` points to) in mode 1; in any other mode cut to each rect region (`0004fd65`) and intersected with each span region (`0004fc50`) from the list's last entry to its first.

The two are ES2's `Region_CopyRectBetweenPages` (DBSIM `00487ad0`) and `Region_CopySpansBetweenPages` (DBSIM `00487c01`), statement for statement.

`VGA8_CopyRectBetweenPages` (`000733bc`), `VGA_8`'s slot 20, given an inclusive rect and an offset, copies the rect of the source page, moved back by the offset, onto the rect of the draw page. When the two pages' pans differ in their low two bits, (*offset.x* + `00088ed4` − `00088ed0`) & 3 not 0, it copies row by row: for each row *y* the routine at slot 21 (`00086604`) reads the source row *y* − *offset.y*, columns *x0* − *offset.x* to *x1* − *offset.x*, into a stack buffer, and slot 22 (`00086608`) writes it to row *y*, columns *x0* to *x1*. Otherwise it copies through the VGA latches, in write mode 1 (graphics controller register 5 set to `0x41`, put back to `0x40` after), a byte per four columns with the planes the edges cover enabled: a rect whose two ends fall in one group of four columns with the plane mask from the 4 × 4 table `00088ee0`, any other as its partial left group (`00088ef0`), its whole groups with all four planes, and its partial right group (`00088ef4`), the row stride being `0008884a`. In the one-group case it reads source row *y* + *offset.y*, where the other cases read *y* − *offset.y*; every copy in this doc passes the offset (0, 0).

## Dirty boxes

**Every draw on a shell screen records the box it covered, and the frames that follow copy the background back over that box.** The list's head is at `000a09c0`, and each record is `0x13` bytes:

| Offset | Holds |
|---|---|
| `+0` | `uint16` *x* |
| `+2` | `uint16` *y* |
| `+4` | `uint16` width |
| `+6` | `uint16` height |
| `+8` | `uint16` area, width × height when the box is added |
| `+0xa` | `int8` the delay, the number of frames before the box is copied |
| `+0xb` | 0 for the first record of a block of ten, 1 for the others |
| `+0xf` | the next record |

Records come from a free list at `000a09bc`. `DirtyBox_Alloc` (`00040e58`) takes its head, first allocating a block of ten (`0xbe` bytes) linked in order in front of the list when it is empty, and returns 0 when that allocation fails; `DirtyBox_Free` (`00040ee4`) pushes a record back.

`DirtyBox_Add` (`00040f00`), given a delay, a height, a width, *y* and *x* in stack order, callee-cleaned:

1. returns when the box misses the display: *x* > *W* − 1, *x* + width < 0, *y* > *H* − 1 or *y* + height < 0, *W* and *H* being the display's width and height (`GLDisplay_GetAttribute` of `000ab616`, items 0 and 1);
2. adds a negative *x* to the width and sets *x* to 0, and *y* and the height likewise;
3. widens the box to whole groups of four columns: the width plus *x* mod 4, rounded up to a multiple of 4, from *x* less *x* mod 4;
4. when *x* + width > *W* − 1 sets the width to *W* − 1 − *x*, and when *y* + height > *H* − 1 the height to *H* − 1 − *y*;
5. when the width and the height are both non-zero, takes a record with `DirtyBox_Alloc`, which it does not test for 0, fills it, the next pointer 0, and passes it to `DirtyList_Insert`.

`DirtyBox_AddRect` (`00041062`), given a delay and an inclusive rect {*x0*, *y0*, *x1*, *y1*} by value, callee-cleaned, adds the box (*x0*, *y0*) of width *x1* − *x0* + 1 and height *y1* − *y0* + 1.

`DirtyList_Insert` (`000411a5`), given a record, looks through the list for the record of the same delay that saves the most when merged: one whose box touches or overlaps the new one, each box's *x* at most the other's *x* + width and its *y* at most the other's *y* + height, and for which, with *U* the area of the box around both and *S* the sum of the two areas (`+8`), both cut to 16 bits, *U* ≤ *S* and *S* − *U* is the largest above 0. It sets that record's box to the box around both, leaving its area as it was, and frees the new record; with none it puts the new record at the head of the list. A merged box is not compared with the others again.

`DirtyList_Flush` (`000413db`), given a draw page and a source page in stack order, callee-cleaned, installs the render context `00082248`, sets the display's source and draw pages to those, and for each record whose delay is 0 copies its box from the source page to the draw page with `Region_CopyBetweenPages`, offset (0, 0), as the type-0 region {*x*, *y*, *x* + width, *y* + height}: the rect is inclusive, so the copy covers one column and one row past the box. It lowers every record's delay by 1, puts the source and draw pages back, installs the render context it found, and with `DirtyList_Prune` (`0004109c`) frees every record whose delay is now below 0. A box added with delay 0 is therefore copied at the next flush, and one added with delay 1 at the flush after that.

`DirtyList_CreateContext` (`00040d54`) makes the flush's render context with `000512a0`(0) and stores it at `00082248`, setting its clip block's clip mode to 1, its translation flag to 1, its rect to (0, 0) to (319, 199) and its origin to (0, 0). `0003ba47`, which sets up the scripts ([the shell](../formats/ttm-scripts.md#the-shell)), calls it first.

The `.TTM` commands add every box with delay 1, `0xa601`'s rect through `DirtyBox_AddRect`; `es2_xref.py` finds further calls of `DirtyBox_Add` from `0002141c`, `000217a9` and `00021a6e`, with delay 0, and from `0003b138` and `0003c1b7`, and of `DirtyBox_AddRect` from `0001907e`, `00019b54`, `0001c067` and `0001c270`.

## Presenting a frame

**The shell shows a new frame each time its frame timer ticks: it shows the page it drew, switches drawing to the other, and restores that page's dirty boxes from the background.** `Shell_PresentFrame` (`00012980`), which `es2_xref.py` finds called twice from `Shell_Main` and nine times from `00037dce`, `000382f6`, `0003837f`, `0003b8df` and `0003c7d8`:

1. waits until the byte `0007fd06` is set, and clears it. `Shell_FrameTick` (`00012974`) sets it: `00045d24` registers it with `Timer_AddCallback` at a count of 2, the scripts' count ([the timer](../formats/ttm-scripts.md#the-timer));
2. when there is a palette at `0007fcd0` and the byte `0007fcd4` is set, uploads it with `Palette_UploadWhole` and clears the byte. The scripts' palette commands leave their palette there;
3. when the byte `0007fcd5` is set, calls `00020a70` with the dword at `00081938` and clears it;
4. makes `+0x88` the visible page and `+0x84` the draw page, and swaps `+0x84` and `+0x88`;
5. when the byte `0007fcd7` is 0, `0007fcd6` is set and the word `000817c4` is 1000, calls `0004bbe9`(6) and clears `0007fcd6`;
6. flushes the dirty list with `DirtyList_Flush`(the new `+0x88`, the word `0007fccc`): the draw page it has just set, and page 2, the word's value in the image, which `0003ba47` also passes as `Ttm_Configure`'s *a*;
7. when the byte `0007fd07` is 1, calls `00050ed4` with the address `0009aff0`, `00027bc9`, and `00051037` with the dwords at `0009aff0` and `0009aff4`.

So pages 0 and 1 take turns, one drawn while the other shows, and page 2 holds the background. A page is drawn every other frame, and a box drawn on it with delay 1 is restored at the second flush after, when that page is next the one drawn.

### Fixing a rect

`Display_CopyRectToPage` (`0004c17c`), given a page, a source page and a rect in stack order, callee-cleaned, sets the display's source page to the source and its draw page to the page, copies the rect from one to the other with `Region_CopyBetweenPages`, offset (0, 0), adds the rect with `DirtyBox_AddRect` at delay 0, and puts the two pages back. `TtmScript_FixRect` passes `Ttm_Configure`'s *a*, page 2, and the draw page, so a fixed rect is copied into the background now and onto the other page at the next frame. `es2_xref.py` finds one other call, from `0003c7d8`.

## Ellipses, outlines and scaled bitmaps

**`Raster_DrawEllipse` (`00055b9c`) is ES2's ([ellipses](../../retail/rendering/polygon-fill.md#ellipses)) but for one step: its span list holds only rows on the display.** With *cy* the centre's row as given, before any translation, and *H* the selected display's height (the dword at `00084462`; the simulator's `00098daa`, which `Raster_SelectRenderTarget`, `ES1DBSIM 0006a644`, sets from the display's `+0xc`), the list starts at row −min(*b*, *cy*) and ends before row min(*b*, *H* − 1 − *cy*), relative to the centre; an `emit` writes only the rows that fall in that range, and with no row in it the ellipse draws nothing. The list is the scratch list `00084499` points to, filled by `Raster_FillSpanList` (`0006a1ac`), ES2's statement for statement. ES2's keeps every row from −*b* to *b* − 1.

**Brush mode 4 outlines.** `Raster_FillRect` ([rectangles](../../retail/rendering/polygon-fill.md#rectangles)), `Raster_DrawEllipse` and `Raster_DrawPolygonDispatch` are ES2's in what mode 4 does: the rect's four edges, the one-pixel ring, the polygon's line loop, all in the brush's colour. A script's third brush `+0x1e3` is mode 4, so `TOKEN_RECT`, `TOKEN_CIRCLE` and `TOKEN_POLY` draw outlines, and `TOKEN_RECTFILL`, `TOKEN_CIRCFILL` and `TOKEN_POLYFILL` fill with the second brush and then outline.

**Scaled bitmaps.** `Bitmap_BlitScaled` (`000503f0`), given a bitmap, a size {*w*, *h*}, flags and a point in stack order, callee-cleaned, unpacks a packed bitmap (`+0x12` non-zero) into `Bitmap_UnpackScratch` for the call as `Bitmap_Blit` does, and passes the bitmap's header (`+4`) to `Bitmap_BlitScaledClipDispatch` (`00065f34`). ES2's (DBSIM `004816bc`) does the same through `GL_BlitScaled` (`00481b2f`), which drops its display argument and calls the dispatch. That, ES2's (DBSIM `0048899c`), moves the point by the block's translation when `+0x208` is 1 and calls slot 29 (`00086624`) with (header, *w*, *h*, point, clip, flags): the clip 0 in clip mode 0, the block's rect `+0x20c` in mode 1, and in mode 2 the rect when the block's region count `+0` is above 0, calling nothing otherwise.

`VGA8_BlitBitmapScaled` (`00074b18`), `VGA_8`'s slot 29, C convention, stretches the bitmap, its rows at `+0`, columns at `+2`, size at `+6` and pixels at `+0xa`, over *w* × *h* pixels from the point:

1. **Clipping.** With a clip rect it returns when the box misses it; *L*, *R*, *T* and *B* are how far the box passes its left, right, top and bottom edges, 0 when it does not. The part drawn is *w* − *L* − *R* columns by *h* − *T* − *B* rows, and it returns when either is 0. With no clip rect nothing is cut.
2. **The steps.** `VGA8_ScaleStepSetup` (`00074f77`) gives *sx* = ((columns − 1) · 65536) / (*w* − 1), negated with flag 2, and *sy* = ((rows − 1) · 65536) / (*h* − 1), truncated toward zero, a size of 1 giving a step of 0. Each walk starts at the fraction *f* = \|step\| mod 65536 when \|step\| is at least `0x8000`, `0x8000` when it is less, and 0 for a size of 1.
3. **The rows.** The first row drawn is source row ⌊(*f_y* + *T* · *sy*) / 65536⌋, and its fraction carries on. The source offset is that row times the columns; with flag 1 it is instead size − columns − that, and the stride −columns, so the rows run from the bottom; with flag 2 it adds columns − 1. Each destination row from *y* + *T* is drawn by `VGA8_BlitScaledRow` (`00074cbd`), a block sharing the routine's frame, and then the fraction adds *sy* and the offset adds its whole part times the stride.
4. **A row.** In clip mode 2 the row is drawn once for each region of the block's list, from the first entry to the last, that holds it, cut to it: a rect region {type 0, *x0* `+4`, *y0* `+8`, *x1* `+0xc`, *y1* `+0x10`} to its columns, a span region (type 2) to its span on that row, any other type passed over. In other modes it is drawn once. With *l* and *r* the region's cut at the left and the right, the row's columns run from *x* + *L* + *l* to *x* + *w* − 1 − *R* − *r*. It sets *A* = (*L* + *l*) · *sx* + *f_x*, keeps *A*'s low 16 bits as the new *f_x*, and gives pixel *j* of the run the byte ⌊(*A* + 65536 · offset + *j* · *sx*) / 65536⌋ of the pixels, the division a floor. A byte of 0 is not written. The pixels are written a plane at a time, every fourth column, on the draw page.

Because *f_x* keeps its new value, a row cut at its left, *L* + *l* not 0, starts the next row's walk from a different fraction.

`TOKEN_SIZEDRAWBRUSH` draws through `Bitmap_BlitScaled` ([the commands](../formats/ttm-scripts.md#the-commands)).

## The simulator

**`ES1DBSIM` has the same pages, region copy, dirty boxes, ellipse and scaled-bitmap dispatch, at its own addresses, with ES2's display layout.** Its display is ES2's, the visible page at `+0x34`, the source page `+0x38`, the draw page `+0x3c`, the clip block `Ttm_RunAll` sets at `+0x44`, and the page origins at `+0x48` ([`Sim_CreateDisplay`](polygon-shading.md#the-display-drivers) sets the draw page to 1 and the visible page to 0); `GLDisplay_GetAttribute` (`ES1DBSIM 0006a780`) is ES2's (DBSIM `00481244`). While the display is the current one, `000bdd28`, a store to the source or draw page is copied to `00098dd6` or `00098dda` and passed to the routine at `0009df14` or `0009df18`, slots 16 and 17. `VGA_8`'s are `VGA8_SetSourcePage` (`ES1DBSIM 00052644`) and `VGA8_SetDrawPage` (`ES1DBSIM 0005266a`), the shell's with the page tables at `0009e5ce` and `0009e5f6`, the offsets at `0009e600` and `0009e604` and the pans at `0009e60c` and `0009e610`, each also storing the page itself at `00098dd6` or `00098dda`.

| Shell | `ES1DBSIM` | Difference |
|---|---|---|
| `Region_CopyBetweenPages`, `Region_CopyRectBetweenPages`, `Region_CopySpansBetweenPages` | `00066914`, `00066690`, `000667c1` | none |
| `DirtyBox_Alloc`, `DirtyBox_Free`, `DirtyBox_Add` | `000838e4`, `00083970`, `0008398c` | the list's head at `000bbc84`, the free list at `000bbc80` |
| `DirtyList_Insert`, `DirtyList_Prune` | `00083bb4` (entered through its padding at `00083bb1`), `00083ae4` | none |
| `DirtyList_Flush` | `00083e80` | installs no render context; `es2_xref.py` finds no reference to it ([Open](#open)) |
| `Raster_DrawEllipse`, `Raster_FillSpanList` | `000831f4`, `0008f68c` | the scratch list `00098de5` points to |
| `Bitmap_BlitScaled`, `Bitmap_BlitScaledClipDispatch` | `00067178`, `00076884` | none; `VGA_8`'s slot 29 is `0008c744` ([Open](#open)) |
| `GLBitmapArray_Dtor`, `GLPalette_Dtor` | `000673c8`, `00069258` | none |

The cockpit's script draws add their boxes with delay 1. `TtmScript_FixRect` (`ES1DBSIM 00079c38`) passes `DirtyBox_Add` the rect's *x0* and *y0* as the box's corner and its *x1* and *y1* as its width and height, with delay 0.

## Functions

| ES1GO | Address | Notes |
|---|---|---|
| `Shell_PresentFrame`, `Shell_FrameTick` | `00012980`, `00012974` | |
| `DirtyList_CreateContext`, `DirtyBox_Alloc`, `DirtyBox_Free` | `00040d54`, `00040e58`, `00040ee4` | |
| `DirtyBox_Add`, `DirtyBox_AddRect`, `DirtyList_Prune` | `00040f00`, `00041062`, `0004109c` | |
| `DirtyList_Insert`, `DirtyList_Flush` | `000411a5`, `000413db` | |
| `Shell_CreateDisplay`, `ShellDisplay_PagePointCtor` | `00045b25`, `00045d09` | `ShellDisplay_PagePointCtor` is the element constructor `__vec_ctor` builds the page origins with, returning its argument or 8 new bytes |
| `Display_CopyRectToPage` | `0004c17c` | |
| `Bitmap_BlitScaled` | `000503f0` | |
| `Region_CopyRectBetweenPages`, `Region_CopySpansBetweenPages`, `Region_CopyBetweenPages` | `000519bc`, `00051aed`, `00051c40` | |
| `Raster_DrawEllipse`, `GLDisplay_GetAttribute` | `00055b9c`, `0005a6e4` | |
| `Bitmap_BlitScaledClipDispatch`, `Raster_FillSpanList` | `00065f34`, `0006a1ac` | |
| `VGA8_SetSourcePage`, `VGA8_SetDrawPage`, `VGA8_ShowPage`, `VGA8_SetPageOrigin` | `000701d4`, `000701f5`, `00070216`, `00070313` | |
| `VGA8_CopyRectBetweenPages` | `000733bc` | |
| `VGA8_BlitBitmapScaled`, `VGA8_BlitScaledRow`, `VGA8_ScaleStepSetup` | `00074b18`, `00074cbd`, `00074f77` | the last two take their arguments in registers and the caller's frame |

## Open

- **Open:** whether a mission's dirty boxes are ever copied, and what bounds the list if they are not: `es2_xref.py` finds no reference to the simulator's `DirtyList_Flush` (`ES1DBSIM 00083e80`). The boxes of a script redrawing the same frames merge.
- **Open:** the simulator's `VGA_8` slot 29 (`ES1DBSIM 0008c744`, with `0008c8d0` and `0008cb75`) is not the shell's code: its instructions differ in length and the decompile handles the flags elsewhere. Its slot 18 (`ES1DBSIM 00052690`) is the shell's `VGA8_ShowPage` with a test of a word and a store more.
- **Open:** what the routines at slots 21 and 22 (`00086604`, `00086608`; `VGA_8`'s `000739e8` and `000738ca`) and 30 and 31 (`00086628`, `0008662c`; `00070388` and `0007045f`) do, and the bytes `00084428` and `00084452` `VGA8_ShowPage` swaps round the second pair; what sets `00084472`, `00084476` and `00084424`.
- **Open:** `Shell_PresentFrame`'s steps 3, 5 and 7: `00020a70`, `0004bbe9`, `00050ed4`, `00027bc9` and `00051037`, and the bytes `0007fcd5`, `0007fcd6`, `0007fcd7` and `0007fd07`.
- **Open:** `Shell_CreateDisplay`'s `0005a388`, `0005a5d0`, `0005a77c` and `000512a0`, the `0x20` bytes at `+0x64`, and what `00045d24`, which calls it and registers the frame tick, is.
- **Open:** which screens `0002141c`, `000217a9`, `00021a6e`, `0003b138`, `0003c1b7`, `0001907e`, `00019b54`, `0001c067`, `0001c270` and `0003c7d8` are, which add dirty boxes or fix rects.
- **Open:** the callees `0006659c`, `0004fd65` and `0004fc50` the region copy clips with, matched by position to ES2's `Rect_Intersect`, `SpanList_ClipToRect` and `SpanRegion_Intersect`.
