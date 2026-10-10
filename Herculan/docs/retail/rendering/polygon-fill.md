# Polygon fill and scan conversion

VSHELL and DBSIM link the same 2D graphics code for filling a convex polygon: a winding check, a rectangle clipper, and a Bresenham scan converter that turns the outline into one span per row. VSHELL also carries an `int32` copy of the scan converter, which `hgrid.cpp` uses to walk the grid cells inside a polygon in a fixed order around a centre cell. Addresses are VSHELL's, with DBSIM's copy in parentheses where it has been identified.

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

`hgrid.cpp`'s cell renderer `HGrid_RenderAround` (`00429b4a`) walks the polygon at the grid's `+0xc8` around the cell of the position it is passed (shifted down by the cell shift at `+0x104`) with `CellWalk_Polygon`, callback `Terrain_DrawCellRowRun` (`00429dac`). DBSIM's terrain draw walks its visible region with the DBSIM copies, by row or by column: see [`terrain-drawing.md`](terrain-drawing.md#the-cell-walk--terrain_drawvisiblecells-0046d0a4).

`CellWalk_PolygonByColumn` (`0042f9b8`; DBSIM `004723f8`) is the far-to-near walk by column. It turns the polygon's points in place a quarter turn about the centre with `Point_RotateQuarterAbout` (`0042f968`; DBSIM `004723ac`: `x' = ox + (y − oy)`, `y' = oy − (x − ox)`), walks, turns each run's end cells back with `Point_UnrotateQuarterAbout` (`0042f990`; DBSIM `004723d4`) and calls `callback(ctx, column, yFrom, yTo)`, then turns the points back. It does not test the two flags, and the step fields swap roles: `+0xc` is set per half and `+0x10` per quadrant. `CellWalk_Rect` (`0042fe5a`) is the far-to-near walk over a rectangle `{x0, y0, x1, y1}` with no scan conversion, each half running its first quadrant's rows before its second's.

## Rejected readings

| Reading | Why it is wrong |
|---|---|
| `CellWalk_PolygonByColumn`'s context is the value `Rtl_VectorNew` returns | Ghidra's decompile shows it so. The disassembly keeps the first argument in `EDI` and writes the context fields through it; the vector is a 60-point scratch array for the clipped quadrants |

## Open

- **Deferred:** what writes `maybe_g_DepthBufferEnabled` (`00471888`, DBSIM `0049f270`) and `DAT_00471884` (DBSIM `DAT_0049aad4`), if anything does through a base register, and so whether the near-to-far walk ever runs.
- **Deferred:** `es2_xref.py` finds no reference to VSHELL's `CellWalk_PolygonByColumn` or `CellWalk_Rect`.
