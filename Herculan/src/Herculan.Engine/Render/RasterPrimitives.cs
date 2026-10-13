using System.Numerics;

namespace Herculan.Engine.Render;

/// <summary>
/// One-pixel primitives stamped a pixel or a span at a time through the caller's fill-rect callback,
/// which reproduces the aliasing of the software rasterizer they stand in for rather than the
/// anti-aliased edges a GPU primitive would give. Positions are whatever space that callback takes.
/// </summary>
public static class RasterPrimitives {
	/// <summary>
	/// A one-device-pixel line, stamped a pixel at a time by Bresenham: the original rasterizes it
	/// (<c>Raster_DrawLine</c>) rather than drawing a quad, and a quad thin enough to match would
	/// alias differently.
	/// </summary>
	public static void AddLine(float x0, float y0, float x1, float y1, Vector3 color,
			Action<float, float, float, float, Vector3> fillRect) {
		int px = (int)MathF.Round(x0), py = (int)MathF.Round(y0);
		int qx = (int)MathF.Round(x1), qy = (int)MathF.Round(y1);
		int dx = Math.Abs(qx - px), dy = -Math.Abs(qy - py);
		int stepX = px < qx ? 1 : -1, stepY = py < qy ? 1 : -1;
		int error = dx + dy;

		while (true) {
			fillRect(px, py, px + 1, py + 1, color);
			if (px == qx && py == qy) {
				return;
			}

			int doubled = error * 2;
			if (doubled >= dy) {
				error += dy;
				px += stepX;
			}

			if (doubled <= dx) {
				error += dx;
				py += stepY;
			}
		}
	}

	/// <summary>
	/// A filled ellipse — <c>Raster_DrawEllipse</c> (<c>00488070</c>) with the brush in fill mode (0), handed the rect
	/// <c>centre ± (halfWidth, halfHeight)</c>. Its midpoint walk writes <c>2 * halfHeight</c> rows of spans, from the
	/// top row down and from the bottom row up at once, starting <c>halfHeight</c> above the centre, so the shape is
	/// one row shorter than it is wide and sits half a row high: the HUD scanner's radius-2 blip disc is 5 wide by 4
	/// tall, and its radius-1 core a 3 by 2 block. Each span reaches <c>±x</c> about the centre, both ends included.
	/// A zero half-extent draws the line <c>Raster_DrawLine</c> would instead.
	/// </summary>
	public static void AddFilledEllipse(float centerX, float centerY, int halfWidth, int halfHeight, Vector3 color,
			Action<float, float, float, float, Vector3> fillRect) {
		if (halfWidth == 0 || halfHeight == 0) {
			fillRect(centerX - halfWidth, centerY - halfHeight, centerX + halfWidth + 1, centerY + halfHeight + 1, color);
			return;
		}

		var rows = EllipseRows(halfWidth, halfHeight);
		for (int i = 0; i < rows.Length; i++) {
			float row = centerY - halfHeight + i;
			fillRect(centerX - rows[i].Outer, row, centerX + rows[i].Outer + 1, row + 1, color);
		}
	}

	/// <summary>
	/// A one-device-pixel ellipse outline — <c>Raster_DrawEllipse</c> (<c>00488070</c>) with the brush in outline mode
	/// (4), handed the rect <c>centre ± (halfWidth, halfHeight)</c>. The same walk as <see cref="AddFilledEllipse"/>
	/// over the same <c>2 * halfHeight</c> rows, but each row keeps where the last row's run ended as well as its own
	/// outer edge, and the two passes fill the run between them on the left, <c>[-outer, -inner]</c>, and on the right,
	/// <c>[inner, outer]</c>. The top and bottom rows run all the way across. The scanner's passive-range ring, the
	/// HUD scanner repeater's rim and the MISSILE CAM's sight ring are its uses.
	/// </summary>
	public static void AddEllipseOutline(float centerX, float centerY, int halfWidth, int halfHeight, Vector3 color,
			Action<float, float, float, float, Vector3> fillRect) {
		if (halfWidth == 0 || halfHeight == 0) {
			fillRect(centerX - halfWidth, centerY - halfHeight, centerX + halfWidth + 1, centerY + halfHeight + 1, color);
			return;
		}

		var rows = EllipseRows(halfWidth, halfHeight);
		for (int i = 0; i < rows.Length; i++) {
			var (outer, inner) = rows[i];
			if (outer < inner) {
				continue;
			}

			float row = centerY - halfHeight + i;
			fillRect(centerX - outer, row, centerX - inner + 1, row + 1, color);
			fillRect(centerX + inner, row, centerX + outer + 1, row + 1, color);
		}
	}

	/// <summary>
	/// <c>Raster_DrawEllipse</c>'s midpoint walk for half-extents <paramref name="a"/> and <paramref name="b"/>, both
	/// nonzero: <c>2b</c> rows from the top, each with its outer half-width and, for the outline, the column its run
	/// starts at. Each y from <c>b</c> down to 1 writes its row from both ends of the list at once, the top half
	/// downward and the bottom half upward. In the first region x steps every column and y only when the error says
	/// so, and a row's run then starts one past the last row's edge; in the second y steps every row, and the run
	/// starts where x last stepped to.
	/// </summary>
	private static (int Outer, int Inner)[] EllipseRows(int a, int b) {
		var rows = new (int Outer, int Inner)[2 * b];
		int forward = 0;
		int backward = rows.Length - 1;
		long a2 = (long)a * a;
		long b2 = (long)b * b;
		long x = 0;
		long inner = 0;
		int y = b;
		long d = b2 - a2 * b + (a2 >> 2);
		long dx = 0;
		long dy = 2 * a2 * b;

		void Emit() {
			if (forward <= backward) {
				rows[forward++] = ((int)x, (int)inner);
				rows[backward--] = ((int)x, (int)inner);
			}
		}

		if (dy > 0) {
			do {
				if (d > 0) {
					Emit();
					inner = x + 1;
					y--;
					dy -= 2 * a2;
					d -= dy;
				}

				x++;
				dx += 2 * b2;
				d += b2 + dx;
			} while (dx < dy);
		}

		d += HalveTowardZero(HalveTowardZero((a2 - b2) * 3) - (dx + dy));
		for (; y > 0; y--) {
			Emit();
			if (d < 0) {
				x++;
				inner = x;
				dx += 2 * b2;
				d += dx;
			}

			dy -= 2 * a2;
			d += a2 - dy;
		}

		return rows;

		static long HalveTowardZero(long value) => value / 2;
	}

	/// <summary>
	/// A filled convex polygon wound clockwise on screen — <c>Raster_DrawPolygonDispatch</c> (<c>00483dac</c>) with the
	/// brush in fill mode: scan-converted into a <see cref="SpanRegion"/> and filled a row at a time, both ends of each
	/// span included. A row whose span comes out reversed is skipped, as the span fill skips it.
	/// </summary>
	public static void AddFilledPolygon(IReadOnlyList<(int X, int Y)> points, Vector3 color,
			Action<float, float, float, float, Vector3> fillRect) {
		var region = SpanRegion.ScanConvert(points);
		for (int i = 0; i < region.Spans.Length; i++) {
			var (x0, x1) = region.Spans[i];
			if (x1 >= x0) {
				fillRect(x0, region.Top + i, x1 + 1, region.Top + i + 1, color);
			}
		}
	}

	/// <summary>
	/// <c>PaperDoll_RecolorRect</c>'s walk over an inclusive rect relative to the doll's origin: every
	/// point whose palette index <paramref name="indexAt"/> reports as <paramref name="key"/> is filled
	/// with <paramref name="tint"/>, as merged horizontal runs. An icon can sit left of or above the
	/// origin, so <paramref name="indexAt"/> answers for any point and -1 for one holding nothing.
	/// </summary>
	public static void AddIndexedRecolor(int x0, int y0, int x1, int y1, Func<int, int, int> indexAt, int key,
			Vector3 tint, float left, float top, Action<float, float, float, float, Vector3> fillRect) {
		for (int y = y0; y <= y1; y++) {
			int run = int.MinValue;
			for (int x = x0; x <= x1 + 1; x++) {
				if (x <= x1 && indexAt(x, y) == key) {
					run = run == int.MinValue ? x : run;
				} else if (run != int.MinValue) {
					fillRect(left + run, top + y, left + x, top + y + 1, tint);
					run = int.MinValue;
				}
			}
		}
	}
}
