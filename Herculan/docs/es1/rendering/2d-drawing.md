# GO.EXE and DBSIM.EXE — 2D drawing

**The 2D primitives, pixels, lines, rectangles, ellipses, polygons and bitmaps, are ES2's but for the differences below: each works on the clip block of the installed render context, by its translation flag and clip mode, and draws through the display driver's routines.** [Screen pages](screen-pages.md) gives the pages they draw on and the dirty boxes the shell's draws record. Addresses in [the shell's ellipses and scaled bitmaps](#ellipses-outlines-and-scaled-bitmaps) are `ES1GO` image addresses, and from [the simulator's](#the-simulators-ellipse-and-scaled-blit) on `ES1DBSIM` image addresses, unless the other binary is named ([the image](../formats/x32-executable.md#the-image)).

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

## The simulator's ellipse and scaled blit

`ES1DBSIM` has the shell's ellipse and scaled-bitmap dispatch at its own addresses:

| Shell | `ES1DBSIM` | Difference |
|---|---|---|
| `Raster_DrawEllipse`, `Raster_FillSpanList` | `000831f4`, `0008f68c` | the scratch list `00098de5` points to |
| `Bitmap_BlitScaled`, `Bitmap_BlitScaledClipDispatch` | `00067178`, `00076884` | none; `VGA_8`'s slot 29 differs, below |

**The simulator's scaled blit leaves palette index 0 unwritten, as the shell's does, so a scaled bitmap's 0 bytes are transparent, but it steps through the bitmap differently.** `VGA8_BlitBitmapScaled` (`ES1DBSIM 0008c744`), `VGA_8`'s slot 29, C convention, with its row block `VGA8_BlitScaledRow` (`0008c8d0`) and `VGA8_ScaleStepSetup` (`0008cb75`), clips as [the shell's](#ellipses-outlines-and-scaled-bitmaps) does (its step 1) and draws each row in the same pieces, regions and plane order (its step 4), testing each byte and writing only the non-zero ones whatever the header's flags. Against the shell's:

- **The steps.** *sx* = columns · 65536 / *w*, truncated toward zero, then 1 added away from zero, the columns negated with flag 2; *sy* the same of the rows over *h*, negated with flag 1; a size of 0 gives a step of 0.
- **No starting fraction.** Each row's walk starts afresh at *A* = (*L* + *l*) · *sx*, and pixel *j* of the run takes byte ⌊(*A* + *j* · *sx*) / 65536⌋ of the source row, the floor of a signed value; nothing carries from one row to the next.
- **The rows.** With *T* = 0 the first row's offset and fraction are 0; otherwise the offset is (*T* · *sy* >> 16, an arithmetic shift) × columns and the fraction *f* the low 16 bits of (*T* + *B*) · *sy*. Flag 1 adds size − columns, flag 2 adds columns. Destination row *k* from *y* + *T* reads the source row at that offset plus ((*f* + *k* · *sy*) / 65535, truncated toward zero) × columns: the running sum is never reduced, and the divisor is 65535.

So with flag 2 the first pixel of each row reads the byte after the source row, the next row's first.

## Pixels

**A pixel is moved by the clip block's translation and then clipped like a line's end points.** `Gfx_PlotPixel` (`ES1DBSIM 00069058`), (point), C convention, is ES2's (DBSIM `004894e0`), the same statements after normalisation, and works on the clip block `00098d64` points at, the installed render context's `+4`:

- **The pen** is the block's `+0x224`, the context's `+0x228`. Mode 0 resolves its color by the top byte, as a fill's brush does ([the colour ramps](polygon-shading.md#the-colour-ramps)): 0 the value itself, 4 an entry of the ramp the low 16 bits name, any other plots nothing; the pixel goes to `g_RasterRoutines` slot 5 (`0009dee8`) with the point and the color. Mode 2 hands the pen's `+4` to slot 6 (`0009deec`). Any other mode plots nothing.
- **The translation.** While `+0x208` is set the point moves by the rect's corner less the origin, (`+0x20c` − `+0x21c`, `+0x210` − `+0x220`), as [`Raster_DrawPolygonDispatch`](#clipped-polygons) moves its points.
- **The clip**, of the moved point, by the clip mode `+0x204`: 0 none; 1 the rect `+0x20c`–`+0x218`, its edges inside; any other value the regions of the list, the `int16` count at `+0` and the pointers from `+4`, walked from the last to the first: a rect region (type 0) by its rect, a span region (type 2) by its run on the point's row (the region's top row, its row count, then a {*x0*, *x1*} pair a row), each inclusive, and a region of another type passed over. The pixel is drawn once, in the first region that holds it.

## Lines and clipped bitmaps

**In clip mode 1 a line is cut to the clip block's rect and drawn with both its end pixels, and a bitmap loses the columns and rows outside the rect; the rect's edges are inside.** `Raster_DrawLine` (`ES1DBSIM 0006a064`), ES2's, moves its two points by the block's translation when `+0x208` is set and, in clip mode 1, cuts them to the rect `+0x20c` with `Line_ClipToRect` (`0007d4a4`), ES2's ([clipping a line](../../retail/rendering/polygon-fill.md#clipping-a-line)), drawing nothing when that answers 0. A flat color then goes to `VGA_8`'s slot 34 (`0009df5c`); a remap pen, mode 2, goes in every clip mode to slot 1 (`0009ded8`) as the span list `Line_ScanConvertToSpanRegion` makes of the line.

**In clip mode 2 a line is drawn in pieces, one for each region of the clip list it crosses, each as a span list: ES2's walk ([drawing a line](../../retail/rendering/polygon-fill.md#drawing-a-line)), at the same block offsets.** A rect region is type 0 and a span region type 2. The points move by the translation and the regions do not. Each piece goes to the pen's span routine, slot 0 (`0009ded4`) for a flat color, so in this mode slot 34 draws nothing.

- **`Line_ScanConvertToSpanRegion` (`ES1DBSIM 0007d358`)**, (a, b, out), C convention, ES2's (DBSIM `00483c60`): the span list of the one-pixel line from *a* to *b*, its top row the smaller *y* and its count |*dy*| + 1. A point is one row {*x*, *x*}, a horizontal line one row {the smaller *x*, the larger}, a vertical line {*x*, *x*} on each row; any other line, walked from its smaller-*x* end by Bresenham's steps with the error starting at 2*dy* − *dx* when it is shallow and 2*dx* − *dy* when it is steep, puts on each row the run of columns it covers there.
- **`SpanRegion_Intersect` (`ES1DBSIM 00073b7c`)**, (a, b, out), C convention, ES2's (DBSIM `00486af4`): over the rows both span lists cover, each row's run {the larger *x0*, the smaller *x1*}. The result runs from the first row whose two runs meet to the last, a row between them whose runs do not meet written {1, 0}, so it draws nothing; it returns *out*, or 0 when no row meets. `es2_xref.py` finds it called by `Raster_DrawLine`, `Raster_DrawPolygonDispatch`, `Raster_FillRect`, `Raster_FillSpanList` and `Region_CopySpansBetweenPages`.

**`VGA8_DrawLine` (`0008cb90`)**, slot 34, (color, *a*, *b*), C convention, writes the color on the draw page (the offset `0009e604`, the pan `0009e610` added to each *x*) through the sequencer's map mask (port `3C4` index 2). It starts from the point with the smaller *x*, *a* when the two are equal; *dx* is the distance across and *dy* the distance down or up:

- *dy* = 0: the run of *dx* + 1 pixels, in one write with the mask `0009e61c`[4 · (*x* & 3) + ((*dx* + 1) & 3)] when it stays in one group of four columns, and otherwise the first group's partial mask `0009e62c`[*x* & 3], the whole groups with all four planes, and the last group's `0009e630`[the remaining count & 3];
- *dx* = 0: the *dy* + 1 pixels of the column;
- *dx* ≤ *dy*: on each row *i* from 0 to *dy*, the pixel at column *x* + ⌊*i* · (*dx* + 1) / (*dy* + 1)⌋;
- *dx* > *dy*: on each column *j* from 0 to *dx*, the pixel *y* ± ⌊*j* · (*dy* + 1) / (*dx* + 1)⌋ rows away, a row's pixels in one group of four written together.

Both end points are drawn, and which point comes first does not change the pixels, so the order `Line_ClipToRect` leaves them in does not show.

**`VGA8_ClearPage` (`0008cd3c`)**, slot 35 (`0009df60`, the dword at `000a7bfc` in `VGA_8`'s table), (page, color), C convention, fills a whole page with one color. When the dword `00098da6` equals `00098dc6`, the display's `+0x28` that attribute 2 copies ([the video memory](screen-pages.md#the-video-memory)), it writes (`00098da6` × `00098daa`, the selected display's height) / 16 dwords of the color byte four times over from `VGA8_PageOffsets` (`0009e5ce`)[page] with all four planes enabled (the map mask `0xf`, put back after). Otherwise it keeps the draw page `Raster_GetDrawPage` (`0008fcde`) returns, the dword `00098dda`, makes the page the draw page through slot 17, passes the rect {0, 0, `00098da6` − 1, `00098daa` − 1} and the color to slot 2 (`0009dedc`), and makes the kept page the draw page again. `Sim_DeathFlash` blacks out both pages with it ([the death flash](../simulation/main-loop.md#the-death-flash--sim_deathflash-00058414)). The shell's is the same code (`ES1GO 00075afc`, slot 35 of its table `0008b2f0`).

**`VGA8_BlitBitmapClipped` (`0008c27c`)**, slot 28, given a clip rect, trims the bitmap at the point: *L* is how far the rect's *x0* lies right of the point's *x*, *R* how far the bitmap's last column, *x* + columns − 1, lies right of *x1*, each 0 when it does not, and *T* and *B* the same in rows. It draws nothing when the rect's *x0* is right of the last column or the point's *x* right of *x1*, the same in *y*, or when columns − *L* − *R* or rows − *T* − *B* is 0, and otherwise the rest. `Raster_BlitDispatch` (`0006a8e0`) passes it the block's rect `+0x20c` in clip mode 1 ([the display drivers](polygon-shading.md#the-display-drivers)).

**`Bitmap_GrabClipDispatch` (`0006aa70`)**, (header, point, flags), C convention, is ES2's (DBSIM `00488868`), the blit's dispatch the other way round: it zeroes the bitmap's pixels, clears the low four bits of its flags byte `+5`, sets its format byte `+4` to 8 when the display's depth (`00098db2`) is 8 or 4 and the byte differs from it, moves the point by the block's translation when `+0x208` is 1, and reads the screen into the bitmap through slot 27 (`0009df40`, `VGA8_ReadBitmap`): with no clip rect in clip mode 0, with the rect `+0x20c` in mode 1, and once for each rect region of the list in mode 2. Each read puts every pixel it reads at that pixel's own row and column of the bitmap ([the cursor](screen-pages.md#the-cursor)), so the pixels a clip cuts off stay 0 and a mode 2 grab assembles its pieces in place.

**Saving and restoring a rect region.** `Region_Save` (`000665d6`) and `Region_Restore` (`0006641f`) take a type-0 region to these two, ES2's (DBSIM `00487da0`, `00487f6c`), each wrapping the caller's buffer in a bitmap header on the stack: rows *y1* − *y0* + 1 and columns *x1* − *x0* + 1 of the region's rect, format 8, and their product as the size.

- **`Region_SaveRect` (`00066470`)**, (region, buffer): flags 0; when the clip block's `+0x208` is set it moves the rect by the block's translation, then calls `Bitmap_GrabClipDispatch`(header, the rect's corner, 0), which moves it again when `+0x208` is 1, so a saved rect is moved twice ([putting a panel up](../simulation/alert-panels.md#putting-a-panel-up-and-taking-it-down)). The buffer is zeroed before the read.
- **`Region_RestoreRect` (`00066354`)**, (region, point, buffer): flags `0x10`, drawn with `Raster_BlitDispatch` (`0006a8e0`)(header, point, 0), which moves the point by the translation when `+0x208` is 1 and cuts to the rect `+0x20c` in clip mode 1, and in mode 2 while the region count `+0` is above 0: an opaque blit of the region's size, every byte written, index 0 included ([index 0](screen-pages.md#the-cursor)), at the point rather than at the region's own corner.

`Line_ClipToRect` is an object file of its own, `0007d4a4`–`0007d5a3`, between the zero fills after the `RET`s at `0007d4a2` and `0007d5a1`; `toupper` (`0007d5a4`) follows it.

## Clipped polygons

**A filled polygon is clipped and scan-converted by ES2's two routines ([filling a polygon](../../retail/rendering/polygon-fill.md#filling-a-polygon)).** `Raster_DrawPolygonDispatch` (`ES1DBSIM 00068be0`) clips a span fill to the rect `+0x20c` in clip mode 1, and in clip mode 2 to each region of the block's list, a rect region through the same clip and a span region through `SpanRegion_Intersect` ([lines](#lines-and-clipped-bitmaps)); it scan-converts each result into the span region the span routine fills. While the block's translation flag `+0x208` is set it moves the points once, into a copy on its stack, by (`+0x20c` − `+0x21c`, `+0x210` − `+0x220`), and clears `+0x208` until it returns, when it puts the flag back: the line loop its brush mode 4 and its two-point case draw through `Raster_DrawLine`, which moves its points itself while the flag is set ([lines](#lines-and-clipped-bitmaps)), is not moved twice.

- **`Poly_ClipToRect` (`000788e4`)** is ES2's (DBSIM `0048422c`, [clipping to a rectangle](../../retail/rendering/polygon-fill.md#clipping-to-a-rectangle)), identical after normalisation.
- **`Poly_ScanConvertToSpanRegion` (`00076f54`)** is ES2's (DBSIM `00484614`, [scan conversion](../../retail/rendering/polygon-fill.md#scan-conversion)) but for the unrolled stores its computed jump enters: ES1's jumps back from `000788dc` by 12 bytes a store, each store (`STOSW`, `ADD EDI,ECX`, `ADD [EBP−0x1b8],EBX`, `ADC EAX,ESI`) stepping its accumulator in the frame, where ES2's saves `EBP` at `004a33ec`, holds the accumulator in `EBP` and jumps back from `004865e4` by 8 bytes a store. After the run a `JMP` at `000788dc` goes back into the function, and three zero bytes follow it, so `Poly_ClipToRect` opens an object file of its own ([where object files meet](../formats/compiler.md#where-object-files-meet-in-the-code)).

`es2_xref.py` finds three calls of `Poly_ClipToRect` and four of `Poly_ScanConvertToSpanRegion`, all from `Raster_DrawPolygonDispatch`.

## Functions

| ES1GO | Address | Notes |
|---|---|---|
| `Raster_DrawEllipse` | `00055b9c` | |
| `Bitmap_BlitScaled` | `000503f0` | |
| `Bitmap_BlitScaledClipDispatch`, `Raster_FillSpanList` | `00065f34`, `0006a1ac` | |
| `VGA8_ClearPage`, `Raster_GetDrawPage` | `00075afc`, `0007b9ba` | slot 35 and its getter, the simulator's ([lines and clipped bitmaps](#lines-and-clipped-bitmaps)) |
| `VGA8_BlitBitmapScaled`, `VGA8_BlitScaledRow`, `VGA8_ScaleStepSetup` | `00074b18`, `00074cbd`, `00074f77` | the last two take their arguments in registers and the caller's frame |
| `Bitmap_UnpackToScratch`, `Bitmap_BlitTransparent` | `00050544`, `0006b664` | the simulator's code (`ES1DBSIM` `000672cc`, `0007d66c`); the second draws a type-1 bitmap for `Raster_BlitDispatch` |
| `VGA8_FillRect`, `VGA8_RemapRect`, `VGA8_PutPixel`, `VGA8_RemapPixel` | `00072cc8`, `00072e74`, `00072f53`, `00072f99` | slots 2, 3, 5 and 6, the simulator's code (`ES1DBSIM` `0008b070`, `0008b21c`, `0008b2fb`, `0008b341`) |
| `VGA8_BlitBitmap` | `00073d7c` | slot 26, the simulator's code (`ES1DBSIM` `0008b9a8`) |
