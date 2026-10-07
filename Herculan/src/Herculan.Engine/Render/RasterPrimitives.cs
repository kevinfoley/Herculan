using System.Numerics;

namespace Herculan.Engine.Render;

/// <summary>
/// One-pixel primitives stamped a pixel or a span at a time through the caller's fill-rect callback,
/// which reproduces the aliasing of the software rasterizer they stand in for rather than the
/// anti-aliased edges a GPU primitive would give. Positions are whatever space that callback takes.
/// </summary>
public static class RasterPrimitives {
	/// <summary>
	/// A one-device-pixel line, stamped a pixel at a time by Bresenham — the same approach
	/// <see cref="AddCircleOutline"/> takes, and for the same reason: the original rasterizes it
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
	/// A filled disc, one row of spans at a time — the original's general ellipse rasterizer
	/// (<c>Raster_DrawEllipse</c>, <c>00488070</c>) with the brush in fill mode, which is how the repeater draws a blip: a
	/// radius-2 disc in black with a radius-1 one in the contact's colour inside it.
	/// </summary>
	public static void AddFilledCircle(float centerX, float centerY, int radius, Vector3 color,
			Action<float, float, float, float, Vector3> fillRect) {
		for (int dy = -radius; dy <= radius; dy++) {
			int dx = (int)MathF.Round(MathF.Sqrt(radius * radius - dy * dy));
			fillRect(centerX - dx, centerY + dy, centerX + dx + 1, centerY + dy + 1, color);
		}
	}

	/// <summary>
	/// A one-device-pixel circle outline, stamped a pixel at a time by the midpoint algorithm. The
	/// original rasterizes it through its general ellipse routine (<c>Raster_DrawEllipse</c>, <c>00488070</c>) with the brush
	/// in outline mode; this reproduces the same aliased ring without a second drawing primitive. The
	/// scanner's passive-range ring, the HUD scanner repeater's rim and the MISSILE CAM's sight ring
	/// are its uses.
	/// </summary>
	public static void AddCircleOutline(float centerX, float centerY, int radius, Vector3 color,
			Action<float, float, float, float, Vector3> fillRect) {
		if (radius <= 0) {
			return;
		}

		void Plot(int dx, int dy) =>
			fillRect(centerX + dx, centerY + dy, centerX + dx + 1, centerY + dy + 1, color);

		int px = radius;
		int py = 0;
		int error = 1 - radius;
		while (px >= py) {
			Plot(px, py); Plot(py, px); Plot(-py, px); Plot(-px, py);
			Plot(-px, -py); Plot(-py, -px); Plot(py, -px); Plot(px, -py);
			py++;
			if (error < 0) {
				error += 2 * py + 1;
			} else {
				px--;
				error += 2 * (py - px) + 1;
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
