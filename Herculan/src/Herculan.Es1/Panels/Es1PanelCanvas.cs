using HercWorks.Core.Data.File.Dyn;
using Herculan.Engine.Content;
using Herculan.Engine.Render;
using Herculan.Es1.View;

namespace Herculan.Es1.Panels;

/// <summary>
/// A panel's render context, the <c>GLViewport</c> at <c>+0x44</c> that <c>AlertPanel_CtorBase</c> (<c>ES1DBSIM
/// 00045988</c>) builds and <c>AlertPanel_Enter</c> installs (docs/es1/simulation/alert-panels.md#the-panel-object--alertpanel_ctorbase-00045988):
/// clip mode 1 and the translation flag set, its rect the panel's on the screen and its origin (0, 0). So a draw's
/// point is the panel's own, moved by the rect's corner, the translation <c>Region_CopyRectBetweenPages</c> adds
/// (docs/es1/rendering/screen-pages.md#copying-between-pages), and in clip mode 1 a draw is cut to the rect, both
/// edges included: <c>Raster_DrawLine</c> (<c>0006a064</c>) cuts the line to the block's rect <c>+0x20c</c> with
/// <c>0007d4a4</c>, drawing nothing when it misses, and <c>Raster_BlitDispatch</c> hands a bitmap to
/// <c>VGA8_BlitBitmapClipped</c> (<c>0008c27c</c>), which skips its columns and rows outside the rect
/// (docs/es1/rendering/ground-and-sky.md#the-horizon-skyline). So a panel draws nothing outside its rect: the
/// objectives panel's title-bar frame and its plate's columns past 315. <c>GLViewport_Ctor</c> clears its
/// underline <c>+0x230</c> and field justification <c>+0x231</c>, so a string is drawn neither underlined nor moved.
/// Everything is drawn on the display's draw page, which <c>AlertPanel_Enter</c> makes the page on the screen.
///
/// <para>The cockpit's canvas context, which <c>Cockpit_CreateCanvasContext</c> (<c>0002ad78</c>) makes the same way
/// over the <c>.GAU</c> record's first 16 bytes and <c>Cockpit_PushCanvasContext</c> installs
/// (docs/es1/simulation/cockpit-panel.md#drawing-it), is one too, with the origin <c>+0x220</c>/<c>+0x224</c> the view
/// changes move: the translation is the rect's corner less the origin, as <c>Raster_DrawLine</c> and
/// <c>Raster_DrawPolygonDispatch</c> (<c>00068be0</c>) move a point by <c>+0x210</c> − <c>+0x220</c> and
/// <c>+0x214</c> − <c>+0x224</c> of the context. A panel's origin is (0, 0).</para>
///
/// <para>In this engine the context is passed to what draws rather than installed in <c>000bdbda</c>: no other
/// drawing runs while a panel is up, and the cockpit's is made afresh at each push from the cockpit's rect and origin.</para>
/// </summary>
public sealed class Es1PanelCanvas {
	/// <summary>The bitmap flag that has every byte written; without it a 0 byte leaves the pixel.</summary>
	private const byte OpaqueFlag = 0x10;

	/// <param name="origin">The context's origin, <c>+0x220</c>/<c>+0x224</c>; (0, 0) for a panel's.</param>
	public Es1PanelCanvas(Es1VideoMemory display, Es1Rect rect, (int X, int Y) origin = default) {
		Display = display;
		Rect = rect;
		Origin = origin;
	}

	public Es1VideoMemory Display { get; }

	/// <summary><c>+0x210</c>–<c>+0x21c</c>, the context's rect: a panel's on the screen, both its translation and its clip.</summary>
	public Es1Rect Rect { get; }

	/// <summary><c>+0x220</c>/<c>+0x224</c>, the origin, taken off the translation.</summary>
	public (int X, int Y) Origin { get; }

	/// <summary>The translation a point is moved by: the rect's corner less the origin.</summary>
	public (int X, int Y) Translation => (Rect.X0 - Origin.X, Rect.Y0 - Origin.Y);

	/// <summary>One pixel at the context's (<paramref name="x"/>, <paramref name="y"/>), unless it lies outside <see cref="Rect"/> once translated.</summary>
	public void Plot(int x, int y, byte color) {
		int screenX = x + Translation.X, screenY = y + Translation.Y;
		if (Rect.Contains(screenX, screenY)) {
			Display.Plot(screenX, screenY, color);
		}
	}

	/// <summary>
	/// <c>Raster_DrawPolygonDispatch</c> (<c>00068be0</c>) of three points or more under the brush {0,
	/// <paramref name="color"/>} in clip mode 1, the points wound clockwise on screen as its callers list them: the
	/// points translated, cut to <see cref="Rect"/> by <c>Poly_ClipToRect</c> (<c>000788e4</c>), scan-converted by
	/// <c>00076f54</c> and each span filled, both ends included, a span whose ends come out reversed skipped. Both
	/// routines are ES2's (docs/retail/rendering/polygon-fill.md#clipping-to-a-rectangle,
	/// docs/retail/rendering/polygon-fill.md#scan-conversion): <c>es2_naming.py diff</c> finds the clipper identical
	/// to DBSIM's <c>0048422c</c> and the scan converter DBSIM's <c>00484614</c> but for its table of unrolled stores.
	/// </summary>
	public void FillPolygon(IReadOnlyList<(int X, int Y)> points, byte color) {
		if (points.Count < 3) {
			throw new ArgumentException("A filled polygon has three points or more.", nameof(points));
		}

		var (dx, dy) = Translation;
		var clipped = ClipToRect(points.Select(p => (p.X + dx, p.Y + dy)).ToList(), Rect);
		if (clipped.Count == 0) {
			return;
		}

		var region = SpanRegion.ScanConvert(clipped);
		for (int row = 0; row < region.Spans.Length; row++) {
			var (x0, x1) = region.Spans[row];
			for (int x = x0; x <= x1; x++) {
				Display.Plot(x, region.Top + row, color);
			}
		}
	}

	/// <summary>
	/// <c>Poly_ClipToRect</c>: Sutherland–Hodgman against the inclusive <paramref name="rect"/>, every <i>x</i> bound first
	/// and then every <i>y</i> bound, each crossing computed from the outside end toward the other in 64 bits, the
	/// division truncating; a single point the <i>x</i> pass leaves is kept only when its <i>y</i> is inside.
	/// </summary>
	internal static List<(int X, int Y)> ClipToRect(List<(int X, int Y)> points, Es1Rect rect) {
		var xs = ClipAxis(points, rect.X0, rect.X1, vertical: true);
		if (xs.Count == 1) {
			return xs[0].Y >= rect.Y0 && xs[0].Y <= rect.Y1 ? xs : [];
		}

		return xs.Count == 0 ? xs : ClipAxis(xs, rect.Y0, rect.Y1, vertical: false);

		// One pass against the bounds lo and hi, of x when vertical, else of y.
		static List<(int X, int Y)> ClipAxis(List<(int X, int Y)> points, int lo, int hi, bool vertical) {
			var result = new List<(int X, int Y)>();
			for (int i = 0; i < points.Count; i++) {
				var previous = points[(i + points.Count - 1) % points.Count];
				var current = points[i];
				int previousSide = Side(previous), currentSide = Side(current);
				if (previousSide == 0) {
					result.Add(currentSide == 0 ? current : Crossing(current, previous, currentSide));
				} else if (currentSide == 0) {
					result.Add(Crossing(previous, current, previousSide));
					result.Add(current);
				} else if (currentSide != previousSide) {
					result.Add(Crossing(previous, current, previousSide));
					result.Add(Crossing(current, previous, currentSide));
				}
			}

			return result;

			int Side((int X, int Y) p) {
				int along = vertical ? p.X : p.Y;
				return along < lo ? -1 : along > hi ? 1 : 0;
			}

			// The point where the edge from the outside end a toward b meets the bound on a's side.
			(int X, int Y) Crossing((int X, int Y) a, (int X, int Y) b, int side) {
				int bound = side < 0 ? lo : hi;
				if (vertical) {
					return (bound, (int)(a.Y + (long)(bound - a.X) * (b.Y - a.Y) / (b.X - a.X)));
				}

				return ((int)(a.X + (long)(bound - a.Y) * (b.X - a.X) / (b.Y - a.Y)), bound);
			}
		}
	}

	/// <summary><c>Raster_FillRect</c> under the brush {0, <paramref name="color"/>}: the inclusive rect filled.</summary>
	public void FillRect(int x0, int y0, int x1, int y1, byte color) {
		for (int y = y0; y <= y1; y++) {
			for (int x = x0; x <= x1; x++) {
				Plot(x, y, color);
			}
		}
	}

	/// <summary>
	/// <c>Raster_DrawLineColour</c> (<c>00044ec0</c>)(<paramref name="color"/>, end, start): <c>Raster_DrawLine</c>
	/// (<c>0006a064</c>)(start, end) under the pen {0, color}: the two points translated, cut to <see cref="Rect"/> by
	/// <c>Line_ClipToRect</c> (<c>0007d4a4</c>, docs/retail/rendering/polygon-fill.md#clipping-a-line), nothing drawn when
	/// nothing is left, and the rest walked as the driver's line routine, <c>VGA8_DrawLine</c> (<c>0008cb90</c>, slot 34),
	/// walks it, both end pixels drawn (docs/es1/rendering/screen-pages.md#lines-and-clipped-bitmaps).
	/// </summary>
	public void DrawLine(int x0, int y0, int x1, int y1, byte color) {
		var (dx, dy) = Translation;
		DrawLineTranslated((x0 + dx, y0 + dy), (x1 + dx, y1 + dy), color);
	}

	/// <summary><see cref="DrawLine"/> of two points already translated, as <c>Raster_DrawPolygonDispatch</c>'s line loop hands them on with the translation flag cleared.</summary>
	private void DrawLineTranslated((int X, int Y) a, (int X, int Y) b, byte color) {
		if (!ClipLine(ref a, ref b, Rect)) {
			return;
		}

		if (b.X < a.X) {
			(a, b) = (b, a);
		}

		int runX = b.X - a.X, runY = Math.Abs(b.Y - a.Y), stepY = b.Y < a.Y ? -1 : 1;
		if (runY == 0) {
			for (int i = 0; i <= runX; i++) {
				Display.Plot(a.X + i, a.Y, color);
			}
		} else if (runX == 0) {
			for (int i = 0; i <= runY; i++) {
				Display.Plot(a.X, a.Y + stepY * i, color);
			}
		} else if (runX <= runY) {
			for (int i = 0; i <= runY; i++) {
				Display.Plot(a.X + i * (runX + 1) / (runY + 1), a.Y + stepY * i, color);
			}
		} else {
			for (int j = 0; j <= runX; j++) {
				Display.Plot(a.X + j, a.Y + stepY * (j * (runY + 1) / (runX + 1)), color);
			}
		}
	}

	/// <summary>
	/// <c>Line_ClipToRect</c>: the segment cut, in place, to the inclusive <paramref name="rect"/>, the bounds taken in the
	/// order <i>y0</i>, <i>x0</i>, <i>y1</i>, <i>x1</i>: both ends beyond one, false; one end beyond, that end moved onto
	/// it at the crossing computed from it toward the other in 64 bits, truncating. The ends may come back swapped, which
	/// the walk does not show.
	/// </summary>
	internal static bool ClipLine(ref (int X, int Y) a, ref (int X, int Y) b, Es1Rect rect) {
		return Cut(ref a, ref b, rect.Y0, vertical: false, below: true)
			&& Cut(ref a, ref b, rect.X0, vertical: true, below: true)
			&& Cut(ref a, ref b, rect.Y1, vertical: false, below: false)
			&& Cut(ref a, ref b, rect.X1, vertical: true, below: false);

		static bool Cut(ref (int X, int Y) a, ref (int X, int Y) b, int bound, bool vertical, bool below) {
			bool aOut = Beyond(a), bOut = Beyond(b);
			if (aOut && bOut) {
				return false;
			}

			if (aOut) {
				a = Crossing(a, b);
			} else if (bOut) {
				b = Crossing(b, a);
			}

			return true;

			bool Beyond((int X, int Y) p) {
				int along = vertical ? p.X : p.Y;
				return below ? along < bound : along > bound;
			}

			(int X, int Y) Crossing((int X, int Y) outside, (int X, int Y) other) => vertical
				? (bound, (int)(outside.Y + (long)(bound - outside.X) * (other.Y - outside.Y) / (other.X - outside.X)))
				: ((int)(outside.X + (long)(bound - outside.Y) * (other.X - outside.X) / (other.Y - outside.Y)), bound);
		}
	}

	/// <summary>
	/// <c>Raster_FillRect</c> (<c>ES2 DBSIM 004865f8</c>) under the brush {4, <paramref name="color"/>}: the inclusive rect's
	/// four edges as lines, (<i>x1</i>, <i>y0</i>) to (<i>x0</i>, <i>y0</i>), (<i>x0</i>, <i>y1</i>) to (<i>x0</i>,
	/// <i>y0</i>), (<i>x0</i>, <i>y1</i>) to (<i>x1</i>, <i>y1</i>) and (<i>x1</i>, <i>y0</i>) to (<i>x1</i>, <i>y1</i>)
	/// (docs/retail/rendering/polygon-fill.md#rectangles).
	/// </summary>
	public void OutlineRect(int x0, int y0, int x1, int y1, byte color) {
		DrawLine(x1, y0, x0, y0, color);
		DrawLine(x0, y1, x0, y0, color);
		DrawLine(x0, y1, x1, y1, color);
		DrawLine(x1, y0, x1, y1, color);
	}

	/// <summary>
	/// <c>Raster_DrawEllipse</c> (<c>000831f4</c>) of <paramref name="rect"/> under the brush {0 or 4,
	/// <paramref name="color"/>}: ES2's walk (docs/retail/rendering/polygon-fill.md#ellipses) with the simulator's one
	/// difference, a span list of the rows on the display alone, from row −min(<i>b</i>, <i>cy</i>) to before
	/// min(<i>b</i>, 199 − <i>cy</i>) about the centre, <i>cy</i> before the translation
	/// (docs/es1/rendering/screen-pages.md#ellipses-outlines-and-scaled-bitmaps); each list filled at the centre,
	/// translated and cut to <see cref="Rect"/> as <c>Raster_FillSpanList</c> (<c>0008f68c</c>) does in clip mode 1.
	/// <paramref name="outline"/> draws the one-pixel ring, mode 4.
	/// </summary>
	public void DrawEllipse(Es1Rect rect, byte color, bool outline) {
		int cx = (rect.X0 + rect.X1) >> 1, cy = (rect.Y0 + rect.Y1) >> 1;
		int a = Math.Abs(rect.X1 - rect.X0) >> 1, b = Math.Abs(rect.Y1 - rect.Y0) >> 1;
		if (a == 0 || b == 0) {
			// PLACEHOLDER: the degenerate ellipse's line, drawn under a pen whose mode is a stack word the function never
			// writes (docs/retail/rendering/polygon-fill.md#ellipses); what it draws is not settled, so nothing is drawn.
			return;
		}

		int low = -Math.Min(b, cy), high = Math.Min(b, Es1CockpitViews.ScreenHeight - 1 - cy);
		if (outline) {
			FillSpans(Walk(a, b, (x, s) => (-x, -s)));
			FillSpans(Walk(a, b, (x, s) => (s, x)));
		} else {
			FillSpans(Walk(a, b, (x, _) => (-x, x)));
		}

		void FillSpans(Dictionary<int, (int X0, int X1)> rows) {
			var (dx, dy) = Translation;
			foreach (var (row, (x0, x1)) in rows) {
				for (int x = x0; x <= x1; x++) {
					int screenX = cx + x + dx, screenY = cy + row + dy;
					if (Rect.Contains(screenX, screenY)) {
						Display.Plot(screenX, screenY, color);
					}
				}
			}
		}

		// The walk, each emit's span from span(x, s) written into rows −y and y − 1 when they lie in [low, high).
		Dictionary<int, (int X0, int X1)> Walk(int a, int b, Func<int, int, (int X0, int X1)> span) {
			var rows = new Dictionary<int, (int, int)>();
			int a2 = a * a, b2 = b * b;
			int x = 0, y = b, px = 0, py = 2 * a2 * b, s = 0;
			int d = a2 / 4 + b2 - a2 * b;
			while (px < py) {
				if (d > 0) {
					Emit(x, y);
					s = x + 1;
					y--;
					py -= 2 * a2;
					d -= py;
				}

				x++;
				px += 2 * b2;
				d += px + b2;
			}

			d += ((a2 - b2) * 3 / 2 - (px + py)) / 2;
			while (y > 0) {
				Emit(x, y);
				if (d < 0) {
					x++;
					s = x;
					px += 2 * b2;
					d += px;
				}

				y--;
				py -= 2 * a2;
				d += a2 - py;
			}

			return rows;

			void Emit(int ex, int ey) {
				var value = span(ex, s);
				foreach (int row in (ReadOnlySpan<int>)[-ey, ey - 1]) {
					if (row >= low && row < high) {
						rows[row] = value;
					}
				}
			}
		}
	}

	/// <summary>
	/// <c>Raster_DrawPolygonEitherWinding</c> of <paramref name="points"/> under the brush {0 or 4, <paramref name="color"/>}
	/// (docs/retail/rendering/polygon-fill.md#filling-a-polygon): an outline of three points or more whose
	/// <c>x[i]·y[i+1] − x[i+1]·y[i]</c> sum is 0 or less taken in reverse order; then <c>Raster_DrawPolygonDispatch</c>
	/// (<c>00068be0</c>) on the translated points, the translation flag cleared for the call: one point a pixel, two a line,
	/// and with <paramref name="outline"/>, mode 4, the line loop round the points, each under the pen {0, color};
	/// otherwise the fill (<see cref="FillPolygon"/>).
	/// </summary>
	public void DrawPolygon(IReadOnlyList<(int X, int Y)> points, byte color, bool outline) {
		if (points.Count == 0) {
			return;
		}

		var ring = points.ToList();
		if (ring.Count >= 3) {
			long sum = 0;
			for (int i = 0; i < ring.Count; i++) {
				var p = ring[i];
				var q = ring[(i + 1) % ring.Count];
				sum += (long)p.X * q.Y - (long)q.X * p.Y;
			}

			if (sum <= 0) {
				ring.Reverse();
			}
		}

		if (ring.Count >= 3 && !outline) {
			FillPolygon(ring, color);
			return;
		}

		var (dx, dy) = Translation;
		var moved = ring.Select(p => (p.X + dx, p.Y + dy)).ToList();
		if (moved.Count == 1) {
			if (Rect.Contains(moved[0].Item1, moved[0].Item2)) {
				Display.Plot(moved[0].Item1, moved[0].Item2, color);
			}

			return;
		}

		if (moved.Count == 2) {
			DrawLineTranslated(moved[1], moved[0], color);
			return;
		}

		for (int i = 0; i + 1 < moved.Count; i++) {
			DrawLineTranslated(moved[i + 1], moved[i], color);
		}

		DrawLineTranslated(moved[0], moved[^1], color);
	}

	/// <summary>
	/// <c>Bitmap_Blit</c>(<paramref name="bitmap"/>, <paramref name="flags"/>, point): the bitmap with its top-left at the
	/// panel's (<paramref name="x"/>, <paramref name="y"/>), cut to <see cref="Rect"/>, a 0 byte leaving the pixel unless
	/// the bitmap's flags have bit 4 (docs/es1/rendering/screen-pages.md#the-cursor, "Index 0"). Flag 1 runs the rows from
	/// the bottom and flag 2 the columns from the right, as the blits read them
	/// (docs/es1/rendering/screen-pages.md#ellipses-outlines-and-scaled-bitmaps).
	/// </summary>
	public void Blit(DynamixBitmap? bitmap, int x, int y, int flags = 0) {
		if (bitmap?.ImageData is not { } pixels) {
			return;
		}

		bool opaque = (bitmap.Flags & OpaqueFlag) != 0;
		for (int row = 0; row < bitmap.Rows; row++) {
			int sourceRow = (flags & 1) != 0 ? bitmap.Rows - 1 - row : row;
			for (int column = 0; column < bitmap.Cols; column++) {
				int sourceColumn = (flags & 2) != 0 ? bitmap.Cols - 1 - column : column;
				int source = sourceRow * bitmap.Cols + sourceColumn;
				if (source < pixels.Length && (opaque || pixels[source] != 0)) {
					Plot(x + column, y + row, pixels[source]);
				}
			}
		}
	}

	/// <summary>
	/// <c>Bitmap_BlitScaled</c> (<c>00067178</c>)(<paramref name="bitmap"/>, size, <paramref name="flags"/>, point) in clip
	/// mode 1: the point translated and the bitmap stretched over <paramref name="width"/> × <paramref name="height"/>
	/// pixels from it, cut to <see cref="Rect"/>, as <c>VGA8_BlitBitmapScaled</c> walks it
	/// (docs/es1/rendering/screen-pages.md#ellipses-outlines-and-scaled-bitmaps): the steps
	/// ((<i>columns</i> − 1) · 65536) / (<i>w</i> − 1) and ((<i>rows</i> − 1) · 65536) / (<i>h</i> − 1), each walk from
	/// its fraction, the column fraction carried from row to row after a cut at the left, and a 0 byte not written.
	/// </summary>
	public void BlitScaled(DynamixBitmap? bitmap, int width, int height, int flags, int x, int y) {
		if (bitmap?.ImageData is not { } pixels || bitmap.Cols <= 0 || bitmap.Rows <= 0 || width <= 0 || height <= 0) {
			return;
		}

		var (dx, dy) = Translation;
		x += dx;
		y += dy;
		if (x > Rect.X1 || x + width - 1 < Rect.X0 || y > Rect.Y1 || y + height - 1 < Rect.Y0) {
			return;
		}

		int left = Math.Max(0, Rect.X0 - x), right = Math.Max(0, x + width - 1 - Rect.X1);
		int top = Math.Max(0, Rect.Y0 - y), bottom = Math.Max(0, y + height - 1 - Rect.Y1);
		if (width - left - right <= 0 || height - top - bottom <= 0) {
			return;
		}

		int columns = bitmap.Cols, rows = bitmap.Rows;
		long stepX = width == 1 ? 0 : (long)(columns - 1) * 65536 / (width - 1);
		long stepY = height == 1 ? 0 : (long)(rows - 1) * 65536 / (height - 1);
		if ((flags & 2) != 0) {
			stepX = -stepX;
		}

		long fractionX = Start(stepX, width), fractionY = Start(stepY, height);
		long walkY = fractionY + top * stepY;
		long offset = (walkY >> 16) * columns;
		long stride = columns;
		if ((flags & 1) != 0) {
			offset = (long)columns * rows - columns - offset;
			stride = -columns;
		}

		if ((flags & 2) != 0) {
			offset += columns - 1;
		}

		fractionY = walkY & 0xffff;
		for (int row = top; row < height - bottom; row++) {
			long a = left * stepX + fractionX;
			fractionX = a & 0xffff;
			for (int column = left; column < width - right; column++) {
				long source = (a + 65536 * offset + (column - left) * stepX) >> 16;
				if (source >= 0 && source < pixels.Length && pixels[source] != 0) {
					Display.Plot(x + column, y + row, pixels[source]);
				}
			}

			fractionY += stepY;
			offset += (fractionY >> 16) * stride;
			fractionY &= 0xffff;
		}

		static long Start(long step, int size) {
			if (size == 1) {
				return 0;
			}

			long magnitude = Math.Abs(step);
			return magnitude >= 0x8000 ? magnitude % 65536 : 0x8000;
		}
	}

	/// <summary>
	/// <c>Region_Save</c> of a type-0 region whose rect is <paramref name="region"/>, in clip mode 1 with the translation
	/// flag set: <c>Region_SaveRect</c> (<c>00066470</c>) moves the rect by the translation and
	/// <c>Bitmap_GrabClipDispatch</c> (<c>0006aa70</c>) moves it again (docs/es1/rendering/screen-pages.md#lines-and-clipped-bitmaps),
	/// so the grab reads the display's source page under the rect moved twice, cut to <see cref="Rect"/>. The cells come
	/// back row by row over the rect's size. A cell cut off is 0 in its place in the buffer: the dispatch zeroes the
	/// buffer first, and <c>VGA8_ReadBitmap</c> (<c>0008be88</c>) reads the cut rect into the buffer at the cut's row and
	/// column.
	/// </summary>
	public ushort[] SaveRegion(Es1Rect region) {
		var (dx, dy) = Translation;
		int width = region.X1 - region.X0 + 1, rows = region.Y1 - region.Y0 + 1;
		var cells = new ushort[Math.Max(0, width) * Math.Max(0, rows)];
		int left = region.X0 + 2 * dx, top = region.Y0 + 2 * dy;
		for (int row = 0; row < rows; row++) {
			for (int column = 0; column < width; column++) {
				int x = left + column, y = top + row;
				if (Rect.Contains(x, y)) {
					cells[row * width + column] = Display.Cell(Display.SourcePage, x, y);
				}
			}
		}

		return cells;
	}

	/// <summary>
	/// <c>Region_Restore</c> of a type-0 region whose rect is <paramref name="region"/> at the point (<paramref name="x"/>,
	/// <paramref name="y"/>): <c>Region_RestoreRect</c> (<c>00066354</c>) blits <paramref name="cells"/> as a bitmap of
	/// the rect's size with the opaque flag <c>0x10</c> through <c>Raster_BlitDispatch</c>, which moves the point by the
	/// translation once and cuts the blit to <see cref="Rect"/>; every cell is written, on the draw page.
	/// </summary>
	public void RestoreRegion(Es1Rect region, int x, int y, ushort[] cells) {
		var (dx, dy) = Translation;
		int width = region.X1 - region.X0 + 1, rows = region.Y1 - region.Y0 + 1;
		for (int row = 0; row < rows; row++) {
			for (int column = 0; column < width; column++) {
				int screenX = x + dx + column, screenY = y + dy + row, source = row * width + column;
				if (source < cells.Length && Rect.Contains(screenX, screenY)) {
					Display.Plot(screenX, screenY, cells[source]);
				}
			}
		}
	}

	/// <summary>
	/// <c>Region_CopyBetweenPages</c> (<c>00066914</c>) of a type-0 region whose rect is <paramref name="region"/> at the
	/// offset (<paramref name="offsetX"/>, <paramref name="offsetY"/>), in clip mode 1 with the translation flag set:
	/// <c>Region_CopyRectBetweenPages</c> (<c>00066690</c>) moves the rect by the offset and the translation, cuts it to <see cref="Rect"/>,
	/// copying nothing when they do not meet, and hands it and the offset to the driver's slot 20, which copies the source
	/// page's rect moved back by the offset onto the draw page's rect (docs/es1/rendering/screen-pages.md#copying-between-pages).
	/// </summary>
	public void CopyRegion(Es1Rect region, int offsetX, int offsetY) {
		var (dx, dy) = Translation;
		int x0 = Math.Max(region.X0 + offsetX + dx, Rect.X0), y0 = Math.Max(region.Y0 + offsetY + dy, Rect.Y0);
		int x1 = Math.Min(region.X1 + offsetX + dx, Rect.X1), y1 = Math.Min(region.Y1 + offsetY + dy, Rect.Y1);
		if (x1 < x0 || y1 < y0) {
			return;
		}

		Display.CopyRect(x0 - offsetX, y0 - offsetY, x1 - offsetX, y1 - offsetY, offsetX, offsetY);
	}

	/// <summary>
	/// <c>HudFont_DrawString</c> (<c>00069e38</c>) from the pen (<paramref name="x"/>, <paramref name="y"/>): each glyph
	/// with its top row at <paramref name="y"/> less the font's ink height, its 0 bytes left, the pen moving right by its
	/// width (docs/es1/simulation/cockpit-widgets.md#labels).
	/// </summary>
	public void DrawString(HudFont? font, string? text, int x, int y) {
		if (font == null || text == null) {
			return;
		}

		int pen = x;
		foreach (char c in text) {
			if (font.GlyphIndex(c) is not { } index) {
				continue;
			}

			var glyph = font.Glyphs[index];
			Blit(glyph, pen, y - font.InkHeight);
			pen += glyph.Cols;
		}
	}
}
