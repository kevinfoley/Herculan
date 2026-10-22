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
	/// (<c>0006a064</c>)(start, end) under the pen {0, color}, cut to <see cref="Rect"/> with both end pixels drawn, as the driver's line routine, <c>VGA8_DrawLine</c>
	/// (<c>0008cb90</c>, slot 34), draws them (docs/es1/rendering/screen-pages.md#lines-and-clipped-bitmaps). The panels
	/// draw only horizontal and vertical lines, so a sloped line's walk is not ported.
	/// </summary>
	/// <exception cref="ArgumentException">The line is neither horizontal nor vertical.</exception>
	public void DrawLine(int x0, int y0, int x1, int y1, byte color) {
		if (x0 != x1 && y0 != y1) {
			throw new ArgumentException("Only a horizontal or vertical line is drawn.");
		}

		for (int y = Math.Min(y0, y1); y <= Math.Max(y0, y1); y++) {
			for (int x = Math.Min(x0, x1); x <= Math.Max(x0, x1); x++) {
				Plot(x, y, color);
			}
		}
	}

	/// <summary>
	/// <c>Bitmap_Blit</c>(<paramref name="bitmap"/>, 0, point): the bitmap with its top-left at the panel's
	/// (<paramref name="x"/>, <paramref name="y"/>), unmirrored, a 0 byte leaving the pixel unless the bitmap's flags have
	/// bit 4 (docs/es1/rendering/screen-pages.md#the-cursor, "Index 0").
	/// </summary>
	public void Blit(DynamixBitmap? bitmap, int x, int y) {
		if (bitmap?.ImageData is not { } pixels) {
			return;
		}

		bool opaque = (bitmap.Flags & OpaqueFlag) != 0;
		for (int row = 0; row < bitmap.Rows; row++) {
			for (int column = 0; column < bitmap.Cols; column++) {
				int source = row * bitmap.Cols + column;
				if (source < pixels.Length && (opaque || pixels[source] != 0)) {
					Plot(x + column, y + row, pixels[source]);
				}
			}
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
