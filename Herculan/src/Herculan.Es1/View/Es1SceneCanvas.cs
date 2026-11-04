using HercWorks.Core.Data.File.Dyn;
using Herculan.Engine.Render;
using Herculan.Engine.Content;
using Herculan.Es1.Panels;

namespace Herculan.Es1.View;

/// <summary>
/// The simulator's render context <c>000b5b6e</c> as the 2D draws over the scene find it in a cockpit view
/// (docs/es1/simulation/cockpit-gunsight-hud.md#what-the-box-is-drawn-through): clip mode 2 with the clip block the view
/// state copied in, the translation flag 0, so a point goes to the draw page as it is, and each draw is cut to every
/// region of the list. <see cref="Es1VideoMemory.DrawWorld"/> fills the same regions with the world.
/// </summary>
public sealed class Es1SceneCanvas : IEs1LabelSurface {
	public Es1SceneCanvas(Es1VideoMemory display, Es1ClipBlock clip) {
		Display = display;
		Clip = clip;
	}

	public Es1VideoMemory Display { get; }

	/// <summary>The clip block, clip mode 2.</summary>
	public Es1ClipBlock Clip { get; }

	/// <summary>
	/// <c>Raster_DrawLine</c> (<c>0006a064</c>) under the pen {0, <paramref name="color"/>}: the line, both end pixels drawn,
	/// cut to every region, a rect with <c>Line_ClipToRect</c> (<c>0007d4a4</c>) and a span block by its spans. The box's
	/// corners draw only horizontal and vertical lines, along which the cut keeps exactly the pixels a region holds, so a
	/// sloped line's walk is not ported.
	/// </summary>
	/// <exception cref="ArgumentException">The line is neither horizontal nor vertical.</exception>
	public void DrawLine(int x0, int y0, int x1, int y1, byte color) {
		if (x0 != x1 && y0 != y1) {
			throw new ArgumentException("Only a horizontal or vertical line is drawn.");
		}

		for (int y = Math.Min(y0, y1); y <= Math.Max(y0, y1); y++) {
			for (int x = Math.Min(x0, x1); x <= Math.Max(x0, x1); x++) {
				if (Clip.Contains(x, y)) {
					Display.Plot(x, y, color);
				}
			}
		}
	}

	/// <summary>
	/// <c>Raster_FillRect</c> under the brush {0, <paramref name="color"/>} in clip mode 2: the inclusive rect filled,
	/// cut to each region of the list (docs/retail/rendering/polygon-fill.md#rectangles).
	/// </summary>
	public void FillRect(int x0, int y0, int x1, int y1, byte color) {
		for (int y = y0; y <= y1; y++) {
			for (int x = x0; x <= x1; x++) {
				if (Clip.Contains(x, y)) {
					Display.Plot(x, y, color);
				}
			}
		}
	}

	/// <summary>
	/// <c>HudFont_DrawString</c> (<c>00069e38</c>) from the pen (<paramref name="x"/>, <paramref name="y"/>) in clip mode
	/// 2: each glyph blitted with its top row at <paramref name="y"/> less the font's ink height, its 0 bytes left unless
	/// the glyph's flags have bit 4, each row cut to each region of the list
	/// (docs/es1/rendering/screen-pages.md#lines-and-clipped-bitmaps), the pen moving right by its width.
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
			if (glyph.ImageData is { } pixels) {
				bool opaque = (glyph.Flags & 0x10) != 0;
				int top = y - font.InkHeight;
				for (int row = 0; row < glyph.Rows; row++) {
					for (int column = 0; column < glyph.Cols; column++) {
						int source = row * glyph.Cols + column;
						if (source < pixels.Length && (opaque || pixels[source] != 0) && Clip.Contains(pen + column, top + row)) {
							Display.Plot(pen + column, top + row, pixels[source]);
						}
					}
				}
			}

			pen += glyph.Cols;
		}
	}

	/// <summary>
	/// <c>Bitmap_Blit</c> of <paramref name="art"/> with flags 0 at (<paramref name="x"/>, <paramref name="y"/>) in clip
	/// mode 2: its 0 bytes left unless its flags have bit 4 (docs/es1/rendering/screen-pages.md#the-cursor), each row cut
	/// to each region of the list (docs/es1/rendering/screen-pages.md#lines-and-clipped-bitmaps).
	/// </summary>
	public void Blit(DynamixBitmap art, int x, int y) {
		var pixels = art.ImageData ?? [];
		bool opaque = (art.Flags & 0x10) != 0;
		for (int row = 0; row < art.Rows; row++) {
			for (int column = 0; column < art.Cols; column++) {
				int source = row * art.Cols + column;
				if (source < pixels.Length && (opaque || pixels[source] != 0) && Clip.Contains(x + column, y + row)) {
					Display.Plot(x + column, y + row, pixels[source]);
				}
			}
		}
	}

	/// <summary>
	/// <c>Raster_DrawPolygonDispatch</c> (<c>00068be0</c>) of three points or more under the brush {0,
	/// <paramref name="color"/>}, wound clockwise on screen, once for each region: for a rect, the polygon cut to it by
	/// <c>Poly_ClipToRect</c> (<c>000788e4</c>) and scan-converted, as in clip mode 1 (<see cref="Es1PanelCanvas.FillPolygon"/>);
	/// for a span block, the polygon scan-converted and each row's span intersected with the block's (<c>00073b7c</c>),
	/// the block's last column included.
	/// </summary>
	public void FillPolygon(IReadOnlyList<(int X, int Y)> points, byte color) {
		if (points.Count < 3) {
			throw new ArgumentException("A filled polygon has three points or more.", nameof(points));
		}

		foreach (var (x0, y0, x1, y1) in Clip.Rects) {
			var clipped = Es1PanelCanvas.ClipToRect(points.ToList(), new Es1Rect(x0, y0, x1, y1));
			if (clipped.Count > 0) {
				Fill(SpanRegion.ScanConvert(clipped), (_, _) => true);
			}
		}

		if (Clip.Spans.Count == 0) {
			return;
		}

		var region = SpanRegion.ScanConvert(points);
		foreach (var block in Clip.Spans) {
			Fill(region, (x, y) => {
				int row = y - block.FirstRow;
				return row >= 0 && row < block.Rows.Length && x >= block.Rows[row].Start && x <= block.Rows[row].End;
			});
		}

		void Fill(SpanRegion spans, Func<int, int, bool> inside) {
			for (int row = 0; row < spans.Spans.Length; row++) {
				var (left, right) = spans.Spans[row];
				for (int x = left; x <= right; x++) {
					if (inside(x, spans.Top + row)) {
						Display.Plot(x, spans.Top + row, color);
					}
				}
			}
		}
	}
}
