# Polygon fill and scan conversion

VSHELL and DBSIM link the same 2D graphics code for filling a convex polygon: a winding check, a rectangle clipper, and a Bresenham scan converter that turns the outline into one span per row. The same code fills [rectangles and ellipses](#rectangles-and-ellipses), and with brush mode 4 outlines them instead. VSHELL also carries an `int32` copy of the scan converter, which `hgrid.cpp` uses to walk the grid cells inside a polygon in a fixed order around a centre cell. Addresses are VSHELL's, with DBSIM's copy in parentheses where it has been identified.

Points are `int32` `{x, y}` pairs, `y` growing downward. A span region is `int16`: the top row, the row count, then an `{x0, x1}` pair per row, `x0` the left end.

## Filling a polygon

| Step | Function | Does |
|---|---|---|
| 1 | `Gfx_FillPolygonEitherWinding` (`00455b9b`; DBSIM `Raster_DrawPolygonEitherWinding`, `004841af`) | sums `x[i]·y[i+1] − x[i+1]·y[i]` around the outline in 64 bits; a sum of zero or less goes to step 2, anything else, and any outline of one or two points, straight to step 3 |
| 2 | `Gfx_FillPolygonReversed` (`00455b02`; DBSIM `Raster_DrawPolygonReversed`, `00484116`) | copies the points in reverse order and fills that; when the fill at context `+0x228` is type 5 it reverses the fill's per-vertex list at `+8` too, for the length of the call |
| 3 | `Gfx_FillPolygon` (`00455798`; DBSIM `Raster_DrawPolygonDispatch`, `00483dac`) | translates, dispatches on the fill type, and for the span fills clips by the context's clip mode and scan-converts |
| 4 | `Poly_ClipToRect` (`00455c18`; DBSIM `0048422c`) | [clips to a rectangle](#clipping-to-a-rectangle) |
| 5 | `Poly_ScanConvertToSpanRegion` (`00456000`; DBSIM `00484614`) | [scan-converts](#scan-conversion) into the span region the span routine fills |

A positive sum is clockwise as drawn on screen, which is the winding step 5 needs. `Gfx_FillPolygon` itself does not check it.

The step-2 copies, `Gfx_FillPolygon`'s translated copy and its clip output each hold 20 points, and the clipper's and scan converter's working buffers 50. None is bounds-checked.

### The brush colour

A type-0 fill is one colour, which DBSIM's `Raster_DrawPolygonDispatch` resolves by the colour's top byte before it scan-converts. A top byte of 0 hands the colour to the span routine as it is, a palette index. A top byte of 4 names a ramp entry: with *r* the colour's low 16 bits, sign-extended, and *s* its bits 16–23, it is the `int16` at entry (*s* · length) >> 8 of record *r* of the raster's ramp table `g_RasterColourRamps` (`004a4fbd`), each record `{int16 *entries, int16 length}`. Any other top byte draws nothing.

### Clipping to a rectangle

`Poly_ClipToRect(count, points, out, rect)` clips against `rect = {x0, y0, x1, y1}`, inclusive, and returns the new count. It is Sutherland–Hodgman in two passes, all of `x` first and then all of `y`. A point is outside when `x < x0` or `x > x1` (then `y < y0` or `y > y1`). For each edge from the previous point to the current one:

| Previous | Current | Emits |
|---|---|---|
| inside | inside | the current point |
| inside | outside | the crossing |
| outside | inside | the crossing, then the current point |
| outside | outside, other side | the previous point's crossing, then the current point's |
| outside | outside, same side | nothing |

A crossing is always computed from the outside endpoint `a` toward the other endpoint `b`, as `y = a.y + (bound − a.x) · (b.y − a.y) / (b.x − a.x)` for the `x` pass (and the transpose for `y`), in 64 bits, the division truncating toward zero. So an edge clips to the same point whichever way it is walked. When the `x` pass leaves a single point, the `y` pass keeps it only if its `y` is inside; when it leaves none, the result is empty and nothing is drawn.

### Clipping a line

`Line_ClipToRect(a, b, rect)` (`0045554c`; DBSIM `00483b60`) cuts the segment between the points `a` and `b`, in place, to `rect = {x0, y0, x1, y1}`, inclusive, and returns 1, or 0 when nothing of it is left. It takes the bounds once each, in the order `y0`, `x0`, `y1`, `x1`. When both endpoints lie beyond a bound it returns 0, leaving both points as they were; when one does, that endpoint moves onto the bound, at the crossing computed from it toward the other as [the rectangle clipper's](#clipping-to-a-rectangle) is, in 64 bits with the division truncating toward zero; when neither does, nothing changes. A point on a bound is inside. To move `b` it first swaps the two points and leaves them swapped, so each bound that moves `b` reverses their order: the result can hold the endpoints the other way round. `Raster_DrawLine` (DBSIM `004838f8`) cuts a line through it in clip mode 1 and for each rect region of a clip list.

### Scan conversion

`Poly_ScanConvertToSpanRegion(count, points, out)` writes the span region for a convex, clockwise outline.

**The two chains.** The top vertex is the one with the smallest `y`, the rightmost of those; the bottom vertex the one with the largest `y`, the leftmost of those. The right chain runs forward from the top vertex to the first vertex whose `y` reaches the bottom, and fills the `x1` column. The left chain runs forward from the bottom vertex to the first vertex whose `y` is back at the top, and fills `x0`. A flat top or bottom edge lies in neither chain. The region's top row is the top vertex's `y` and its row count `bottom − top + 1`. When every point shares one `y`, the region is that one row, from the bottom vertex's `x` to the top vertex's: the leftmost point to the rightmost.

**Each edge** writes every row from one endpoint's `y` to the other's, both included, one `x` per row, in its chain's column. With `dx = |x1 − x0|` and `dy = |y1 − y0|`:

| Edge | `x` on each row |
|---|---|
| vertical, `dx = 0` | the edge's `x` |
| diagonal, `dx = dy` | stepping by 1 per row from the upper endpoint |
| steep, `dy > dx` | from the endpoint with the smaller `x`, row `i` takes `xs + ⌊(2i·dx + dy) / (2dy)⌋`: the line's `x` rounded to nearest, halves toward the larger `x`. Both chains write the same value |
| shallow, `dx > dy` | from the outer endpoint `(xo, yo)` — the larger-`x` end on the right chain, the smaller-`x` end on the left — row 0 takes `xo` and row `r ≥ 1` takes `xo ∓ ⌈(2r − 1)·dx / (2dy)⌉`, `−` on the right chain and `+` on the left |
| horizontal, `dy = 0` | its smaller `x` on the left chain, its larger on the right; reached only by an outline that is not convex |

A shallow edge's value is the outermost pixel its Bresenham line puts on that row, where a pixel exactly halfway between two rows counts for the row farther from the outer endpoint. Its last row therefore reaches past its inner endpoint by up to half a row's run. The steep and diagonal edges are plain Bresenham with error `2dx − dy`, the steep loop stepping `x` when the error is not negative. The vertical and diagonal edges go through a computed jump into a run of unrolled stores just below `00457350` (DBSIM `004865e4`), one per row, each adding a fixed-point step to `x`.

**Rows two edges share.** Where two edges meet, both write the vertex's row and the later one stands. The right chain is written from the top down, so at a right-chain vertex the lower edge's value stands. The left chain is written from the bottom up, so the upper edge's does.

## Rectangles and ellipses

**A rectangle or an ellipse is filled with the brush's colour, or, with brush mode 4, only its outline is drawn.** Both read the fill brush `{mode, colour}` at the clip block's `+0x228` and resolve its colour by [the brush colour](#the-brush-colour)'s top-byte rule. An outline is drawn with lines (`Raster_DrawLine`, `004838f8`) under a temporary pen `{0, colour}` at `+0x224`, the pen at `+0x224` being put back after. With mode 4, a polygon is drawn as a line loop in the same way (`Raster_DrawPolygonDispatch`, [the outline pass](dts-texture-binding.md#tssolidpoly--palette-index-unlit-fill-plus-outline)).

### Rectangles

`Raster_FillRect` (DBSIM `004865f8`), given an inclusive rect {*x0*, *y0*, *x1*, *y1*}, by the brush's mode:

- **0** fills it with the colour through `g_RasterRoutines` slot 2; **6** remaps it through slot 3 with the brush's second field. The rect first moves by the block's translation, (`+0x20c` − `+0x21c`, `+0x210` − `+0x220`), when `+0x208` is set, and is clipped by the clip mode `+0x204`: 0 not at all, 1 to the block's rect at `+0x20c`, otherwise to each region of the block's list, from its last entry to its first: a rect region's intersection goes to the mode's routine, and a span region's intersection with the rect, as a span list, to slot 0 whatever the mode.
- **4** draws four lines, the rect's edges, both ends included: (*x1*, *y0*) to (*x0*, *y0*), (*x0*, *y1*) to (*x0*, *y0*), (*x0*, *y1*) to (*x1*, *y1*) and (*x1*, *y0*) to (*x1*, *y1*). The lines move and clip as lines do ([clipping a line](#clipping-a-line)).
- Any other mode draws nothing.

### Ellipses

`Raster_DrawEllipse` (DBSIM `00488070`), given a rect, draws in modes 0 and 4 and does nothing in any other. The centre is (*cx*, *cy*) = ((*x0* + *x1*) >> 1, (*y0* + *y1*) >> 1) and the half-extents *a* = \|*x1* − *x0*\| >> 1 and *b* = \|*y1* − *y0*\| >> 1. With *a* = 0 it draws the line (*cx*, *cy* + *b*) to (*cx*, *cy* − *b*), with *b* = 0 the line (*cx* + *a*, *cy*) to (*cx* − *a*, *cy*), in either mode, under a temporary pen whose colour is the brush's and whose mode is the stack word the function never writes. Otherwise it writes a [span list](#span-lists) of 2*b* rows, the top row *b* above the centre, into a scratch buffer and fills it at (*cx*, *cy*), from this walk, all in `int32` with each division truncating toward zero:

```
a2 = a·a,  b2 = b·b
x = 0,  y = b,  px = 0,  py = 2·a2·b
d = a2/4 + b2 − a2·b
while px < py:                       the flat part, x stepping every time
    if d > 0:  emit(x, y);  y −= 1;  py −= 2·a2;  d −= py
    x += 1;  px += 2·b2;  d += px + b2
d += ((a2 − b2)·3/2 − (px + py)) / 2
while y > 0:                          the steep part, y stepping every time
    emit(x, y)
    if d < 0:  x += 1;  px += 2·b2;  d += px
    y −= 1;  py −= 2·a2;  d += a2 − py
```

`emit(x, y)` writes one span into row −*y* of the list and the same span into row *y* − 1, so the bottom half sits one row higher than the top half's mirror: the shape is 2*a* + 1 pixels wide and 2*b* rows tall, half a row above the centre. Every *y* from *b* down to 1 is emitted once.

- **Mode 0** emits {−*x*, *x*} and fills the list once.
- **Mode 4** sets the brush's mode to 0, runs the walk twice and fills the list after each run, then sets the mode back to 4. Each run keeps a second column *s*, 0 at the start. In the flat part an emit uses *s* and then sets it to *x* + 1; in the steep part an emit uses it, and each step of *x* sets it to the new *x*. The first run emits {−*x*, −*s*}, the ring's left side, and the second {*s*, *x*}, its right side: a ring one pixel thick, whose top and bottom rows are each one run straight across, both runs drawing the centre column.

### Span lists

A span list is an `int16` top row, an `int16` row count and an `int16` {*x0*, *x1*} pair per row. `Raster_FillSpanList` (DBSIM `00486928`), given one and an origin, copies it into a scratch list moved by the origin, and by the block's translation when `+0x208` is set, and fills it with the brush: mode 0 through `g_RasterRoutines` slot 0 with the colour, mode 6 remapping with the brush's second field; any other mode draws nothing. It clips by the clip mode as `Raster_FillRect` does, cutting the list to a rect or intersecting it with a span region.

## Walking a polygon's cells

`Poly_ScanConvert` (`00465e0a`; DBSIM `00493086`) is the same scan converter writing `int32`: the region is `{top, count}` followed by `int32` `{x0, x1}` pairs, by the rules above. `Poly_ScanConvertEitherWinding` (`00465d39`; DBSIM `00492fb5`) and `Poly_ScanConvertReversed` (`00465db9`; DBSIM `00493035`) are its steps 1 and 2. `Poly_BuildSpanList` (`0043000d`; DBSIM `00472a08`) moves a polygon `{count, points*}` to the origin, scan-converts it, moves the spans back, and fills a header `{top, bottom, rows, first pair*}`.

The walk cuts the polygon into four quadrants around a centre cell `(ox, oy)` with `Poly_ClipToHalfPlane` (`004300c7`; DBSIM `00472ac0`). `Poly_ClipToHalfPlane(keepHigh, alongX, value, in, out)` keeps the part of a polygon with `x` (or `y`) `≥ value` when `keepHigh` is set and `≤ value` otherwise, points on the line kept, stores the new count in `out` and returns its low byte. It is one Sutherland–Hodgman pass, the first edge running from the last point to the first. Each point is on the kept side, on the line, or on the dropped side, and a 19-byte table at `004301da` (DBSIM `00472bdd`), indexed by `(keepHigh ? 0 : 10) + 3·previous + current + 4` with the sides as +1, 0 and −1, picks what each edge emits:

| Previous | Current | Emits |
|---|---|---|
| any | on the line | the current point |
| kept or on the line | kept | the current point |
| dropped | kept | the crossing, then the current point |
| kept | dropped | the crossing |
| on the line or dropped | dropped | nothing |

A crossing lies on the line, at `value` on the cut axis; for an `x` cut its `y` is `prev.y + (cur.y − prev.y) · (value − prev.x) / (cur.x − prev.x)` (the transpose for a `y` cut). Unlike the [rectangle clipper's](#clipping-to-a-rectangle), it is measured from the previous point, the product kept to 32 bits and the division truncating toward zero, so an edge can clip to a different point depending on which way it is walked.

| Quadrant | Rows | Columns |
|---|---|---|
| 0 | `y ≥ oy + 1` | `x ≥ ox + 1` |
| 1 | `y ≥ oy + 1` | `x ≤ ox` |
| 2 | `y ≤ oy` | `x ≤ ox − 1` |
| 3 | `y ≤ oy` | `x ≥ ox` |

The centre cell is in quadrant 3. The walk context is `0x1c` bytes: `+0` `ox`, `+4` `oy`, `+8` the quadrant, `+0xc` and `+0x10` the steps along `x` and `y` (±1), `+0x14` the callback and `+0x18` a value for it, the last two set by `CellWalk_SetCallback` (`0042f3b0`; DBSIM `00471e24`).

`CellWalk_Polygon(ctx, centre, polygon)` (`0042f3c4`; DBSIM `00471e38`) calls `callback(ctx, row, xFrom, xTo)` once per row of each quadrant, the run going from `xFrom` to `xTo` by the `x` step. With the values the image holds, it walks **far to near**:

1. rows below the centre, `y ≥ oy + 1`, from the last row up to `oy + 1`: on each row quadrant 0's run from its right end leftward, then quadrant 1's from its left end rightward;
2. rows `y ≤ oy`, from the first row down to `oy`: on each row quadrant 2's run from its left end rightward, then quadrant 3's from its right end leftward.

Every run ends beside the centre's column and the rows close in on the centre's row, so the centre cell comes last. A half whose rows are all negative is skipped. The choice is `DAT_00471888 == 0 && DAT_00471884 != 0` (DBSIM `0049f270` and `DAT_0049aad4`), 0 and 1 in both images; every absolute-address operand on any of the four is a read. The first is `maybe_g_DepthBufferEnabled`, which also turns on the textured rasterizers' per-pixel depth test ([`dts-texture-binding.md`](dts-texture-binding.md#the-depth-buffer)). The other branch walks **near to far**, a quadrant at a time: 2, 3, 0, 1, each from the centre's row outward and each run from beside the centre's column outward.

`hgrid.cpp`'s cell renderer `HGrid_RenderAround` (`00429b4a`) walks the polygon at the grid's `+0xc8` around the cell of the position it is passed (shifted down by the cell size shift at `+0x104`) with `CellWalk_Polygon`, callback `Terrain_DrawCellRowRun` (`00429dac`). DBSIM's terrain draw walks its visible region with the DBSIM copies, by row or by column: see [`terrain-drawing.md`](terrain-drawing.md#the-cell-walk--terrain_drawvisiblecells-0046d0a4).

`CellWalk_PolygonByColumn` (`0042f9b8`; DBSIM `004723f8`) is the far-to-near walk by column. It turns the polygon's points in place a quarter turn about the centre with `Point_RotateQuarterAbout` (`0042f968`; DBSIM `004723ac`: `x' = ox + (y − oy)`, `y' = oy − (x − ox)`), walks, turns each run's end cells back with `Point_UnrotateQuarterAbout` (`0042f990`; DBSIM `004723d4`) and calls `callback(ctx, column, yFrom, yTo)`, then turns the points back. It does not test the two flags, and the step fields swap roles: `+0xc` is set per half and `+0x10` per quadrant. `CellWalk_Rect` (`0042fe5a`) is the far-to-near walk over a rectangle `{x0, y0, x1, y1}` with no scan conversion, each half running its first quadrant's rows before its second's.

## Rejected readings

| Reading | Why it is wrong |
|---|---|
| `CellWalk_PolygonByColumn`'s context is the value `Rtl_VectorNew` returns | Ghidra's decompile shows it so. The disassembly keeps the first argument in `EDI` and writes the context fields through it; the vector is a 60-point scratch array for the clipped quadrants |

## Open

- **Deferred:** what writes `maybe_g_DepthBufferEnabled` (`00471888`, DBSIM `0049f270`) and `DAT_00471884` (DBSIM `DAT_0049aad4`), if anything does through a base register, and so whether the near-to-far walk ever runs.
- **Deferred:** `es2_xref.py` finds no reference to VSHELL's `CellWalk_PolygonByColumn` or `CellWalk_Rect`.
