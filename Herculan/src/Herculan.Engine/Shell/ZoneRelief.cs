using HercWorks.Core.Io.Transform.Common;
using Herculan.Engine.Content;

namespace Herculan.Engine.Shell;

/// <summary>
/// One zone's heights as the map reads them: <c>dat\zone%d.dat</c>'s cell shift and <c>dba\zone%d.dba</c>'s
/// first frame, a byte per cell with the bitmap's rows running north to south (<c>HeightGrid_FromBitmap</c> (<c>00428d5b</c>)).
/// </summary>
public sealed class ZoneRelief {
	private readonly byte[] _pixels;

	private ZoneRelief(int widthShift, int cellShift, int rows, byte[] pixels) {
		WidthShift = widthShift;
		CellShift = cellShift;
		Rows = rows;
		_pixels = pixels;
	}

	/// <summary><c>+0xfc</c>, log2 of the bitmap's width, which the relief uses for both sides.</summary>
	public int WidthShift { get; }

	/// <summary><c>+0x104</c>, log2 of a cell's world size.</summary>
	public int CellShift { get; }

	public int Rows { get; }

	private int Size => 1 << WidthShift;

	public static ZoneRelief? Load(GameContent content, int zone) {
		var header = new ZoneDatTransformer().Parse(content.Read("dat", $"ZONE{zone}.DAT"));
		if (header == null || ShellArt.ReadBankFrames(content, $"ZONE{zone}") is not { Length: > 0 } frames
				|| frames[0].ImageData is not { } pixels) {
			return null;
		}

		int widthShift = 0;
		while (1 << widthShift < frames[0].Cols) {
			widthShift++;
		}

		return new ZoneRelief(widthShift, header.CellShift, frames[0].Rows, pixels);
	}

	/// <summary>
	/// The height of cell (<paramref name="x"/>, <paramref name="y"/>): row <paramref name="y"/> counts up
	/// from the bitmap's last row. A read past the grid, which the original makes into the heap, is 0.
	/// </summary>
	private int Height(int x, int y) {
		int row = Rows - 1 - y;
		int at = row * Size + x;
		return x >= 0 && y >= 0 && row >= 0 && at < _pixels.Length ? _pixels[at] : 0;
	}

	/// <summary>
	/// <c>ShellMap_BuildRelief</c> (<c>00426fe0</c>): the cells under the bounds widened by the relief margin, drawn as two banded
	/// triangles each at a whole number of pixels per cell — the most that fits 640 by 400 — into a bitmap
	/// a cell wider and taller than the triangles fill. Each corner's colour is its height's step on the
	/// <c>0xd1</c> ramp; a cell off the grid has all four corners at 0.
	/// </summary>
	internal ShellSurface BuildRelief((int MinX, int MinY, int MaxX, int MaxY) bounds) {
		const int margin = 100000;
		int x0 = bounds.MinX - margin >> CellShift;
		int y0 = bounds.MinY - margin >> CellShift;
		int x1 = bounds.MaxX + margin >> CellShift;
		int y1 = bounds.MaxY + margin >> CellShift;
		int columns = x1 - x0 + 1;
		int rows = y1 - y0 + 1;
		int perCell = Math.Min(640 / columns, 400 / rows);
		var relief = new ShellSurface(columns * perCell, rows * perCell);

		int Color(int height) => Math.Min(height, 0x80 - 1) / (0x80 / 0x18) + 0xd2 - 1;

		int top = 0;
		for (int y = y1; y > y0; y--) {
			int bottom = top + perCell;
			int left = 0;
			for (int x = x0; x < x1; x++) {
				int right = left + perCell;
				int h00 = 0, h01 = 0, h11 = 0, h10 = 0;
				if (y >= 0 && y < Rows && x >= 0 && x < Size) {
					h00 = Color(Height(x, y));
					h01 = Color(Height(x, y + 1));
					h11 = Color(Height(x + 1, y + 1));
					h10 = Color(Height(x + 1, y));
				}

				BandedTriangle(relief, left, bottom, h00, left, top, h01, right, top, h11);
				BandedTriangle(relief, left, bottom, h00, right, top, h11, right, bottom, h10);
				left = right;
			}

			top = bottom;
		}

		return relief;
	}

	/// <summary>
	/// <c>Gfx_BandedTriangle</c> (<c>00457aa8</c>), the 8-bit "Gouraud" triangle: not interpolated per pixel but cut into one flat
	/// band per palette index between its corners' colours. The edge from the highest-coloured corner to
	/// the lowest is divided into one step per index; the band between steps <c>i</c> and <c>i + 1</c> is
	/// filled with the highest colour less <c>i</c>, closed along whichever of the other two edges it
	/// spans. A triangle with two corners on one pixel draws nothing, and one whose corners share a
	/// colour is filled flat.
	/// </summary>
	internal static void BandedTriangle(ShellSurface surface, int ax, int ay, int ac, int bx, int by, int bc, int cx,
			int cy, int cc) {
		if ((ax == bx && ay == by) || (ax == cx && ay == cy) || (cx == bx && cy == by)) {
			return;
		}

		int[] xs = { ax, bx, cx };
		int[] ys = { ay, by, cy };
		int[] cs = { ac, bc, cc };
		int high = Math.Max(Math.Max(ac, bc), cc);
		int low = Math.Min(Math.Min(ac, bc), cc);
		if (high == low) {
			surface.FillConvex(new[] { ax, ay, bx, by, cx, cy }, (byte)high);
			return;
		}

		int hi = 0, lo = 0;
		for (int i = 0; i < 3; i++) {
			if (cs[i] == high) {
				hi = i;
			}

			if (cs[i] == low) {
				lo = i;
			}
		}

		int mid = (hi + lo) switch { 1 => 2, 2 => 1, _ => 0 };
		int midColor = cs[mid];

		int[] Steps(int from, int to, int count) {
			var points = new int[(count + 1) * 2];
			points[0] = xs[from];
			points[1] = ys[from];
			for (int i = 1; i < count; i++) {
				points[i * 2] = (xs[to] - xs[from]) * i / count + xs[from];
				points[i * 2 + 1] = (ys[to] - ys[from]) * i / count + ys[from];
			}

			points[count * 2] = xs[to];
			points[count * 2 + 1] = ys[to];
			return points;
		}

		int total = high - low;
		int upper = high - midColor;
		int lower = midColor - low;
		var main = Steps(hi, lo, total);

		void Band(int[] side, int sideAt, int mainAt, int color) {
			surface.FillConvex(new[] {
				main[mainAt * 2], main[mainAt * 2 + 1], main[(mainAt + 1) * 2], main[(mainAt + 1) * 2 + 1],
				side[(sideAt + 1) * 2], side[(sideAt + 1) * 2 + 1], side[sideAt * 2], side[sideAt * 2 + 1],
			}, (byte)color);
		}

		if (upper == 0) {
			var side = Steps(mid, lo, lower);
			for (int i = 0; i < total; i++) {
				Band(side, i, i, high - i);
			}
		} else if (lower == 0) {
			var side = Steps(hi, mid, upper);
			for (int i = 0; i < total; i++) {
				Band(side, i, i, high - i);
			}
		} else {
			var first = Steps(hi, mid, upper);
			var second = Steps(mid, lo, lower);
			for (int i = 0; i < upper; i++) {
				Band(first, i, i, high - i);
			}

			for (int i = 0; i < lower; i++) {
				Band(second, i, upper + i, midColor - i);
			}
		}
	}
}
