using Herculan.Engine.Render;
using Herculan.Es1.Panels;

namespace Herculan.Es1.View;

/// <summary>
/// The simulator's render context <c>000b5b6e</c> as the 2D draws over the scene find it in a cockpit view
/// (docs/es1/simulation/cockpit-gunsight-hud.md#what-the-box-is-drawn-through): clip mode 2 with the clip block the view
/// state copied in, the translation flag 0, so a point goes to the draw page as it is, and each draw is cut to every
/// region of the list. <see cref="Es1VideoMemory.DrawWorld"/> fills the same regions with the world.
/// </summary>
public sealed class Es1SceneCanvas {
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
