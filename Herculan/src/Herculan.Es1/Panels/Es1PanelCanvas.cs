using HercWorks.Core.Data.File.Dyn;
using Herculan.Engine.Content;
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
/// <para>In this engine the context is passed to what draws rather than installed in <c>000bdbda</c>: no other
/// drawing runs while a panel is up.</para>
/// </summary>
public sealed class Es1PanelCanvas {
	/// <summary>The bitmap flag that has every byte written; without it a 0 byte leaves the pixel.</summary>
	private const byte OpaqueFlag = 0x10;

	public Es1PanelCanvas(Es1VideoMemory display, Es1Rect rect) {
		Display = display;
		Rect = rect;
	}

	public Es1VideoMemory Display { get; }

	/// <summary><c>+0x254</c>–<c>+0x260</c> of the panel, the context's rect: the panel's on the screen, both its translation and its clip.</summary>
	public Es1Rect Rect { get; }

	/// <summary>One pixel at the panel's (<paramref name="x"/>, <paramref name="y"/>), unless it lies outside <see cref="Rect"/>.</summary>
	public void Plot(int x, int y, byte color) {
		int screenX = x + Rect.X0, screenY = y + Rect.Y0;
		if (Rect.Contains(screenX, screenY)) {
			Display.Plot(screenX, screenY, color);
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
