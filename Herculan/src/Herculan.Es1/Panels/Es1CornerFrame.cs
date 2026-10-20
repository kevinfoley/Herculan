using HercWorks.Core.Data.File.Dat.Sim;
using HercWorks.Core.Data.File.Dyn;
using Herculan.Es1.View;

namespace Herculan.Es1.Panels;

/// <summary>
/// <c>CornerFrame_Draw</c> (<c>ES1DBSIM 00044954</c>) and its two callees, the bevelled box a panel button is drawn as
/// from one record of the style table (docs/es1/simulation/alert-panels.md#drawing-a-frame-from-a-style): the inside,
/// three rings of one-pixel edges, and a picture from <c>dba\corners</c> in each corner.
/// </summary>
public static class Es1CornerFrame {
	/// <summary>The three rings' (first dword, grow, inset), outer to inner.</summary>
	private static readonly (int First, int Grow, int Inset)[] Rings = [(1, 0, 0), (5, -2, 1), (9, -4, 2)];

	/// <summary>
	/// <c>CornerFrame_Draw</c>(<paramref name="style"/>, <paramref name="rect"/>) on <paramref name="canvas"/>: the inside
	/// (<i>x0</i> + 3, <i>y0</i> + 3)–(<i>x1</i> − 3, <i>y1</i> − 3), ordered, filled in the record's dword <c>+0</c>;
	/// the three rings; and the corners from <paramref name="corners"/>, the frames of <c>WidgetCorners</c>
	/// (<c>000bd53e</c>). With no style nothing is drawn.
	/// </summary>
	public static void Draw(Es1PanelCanvas canvas, Es1WidgetStyle? style, Es1Rect rect, IReadOnlyList<DynamixBitmap>? corners) {
		if (style == null) {
			return;
		}

		int left = rect.X0 + 3, right = rect.X1 - 3, top = rect.Y0 + 3, bottom = rect.Y1 - 3;
		canvas.FillRect(Math.Min(left, right), Math.Min(top, bottom), Math.Max(left, right), Math.Max(top, bottom),
			(byte)style.InsideColor);
		foreach (var (first, grow, inset) in Rings) {
			DrawEdgeRing(canvas, style, first, grow, inset, rect);
		}

		DrawCorners(canvas, style, rect, corners);
	}

	/// <summary>
	/// <c>CornerFrame_DrawEdgeRing</c> (<c>00044adc</c>)(style, <paramref name="first"/>, <paramref name="grow"/>,
	/// <paramref name="inset"/>, rect): with <i>L</i> = <i>x0</i> + inset, <i>T</i> = <i>y0</i> + inset, <i>w</i> =
	/// <i>x1</i> − <i>x0</i> + grow and <i>h</i> = <i>y1</i> − <i>y0</i> + grow, four lines, each in the record's dword
	/// and skipped when that dword is 0: dword <paramref name="first"/> the left edge (<i>L</i>, <i>T</i> + 1)–(<i>L</i>,
	/// <i>T</i> + <i>h</i> − 1), the next the top edge (<i>L</i> + 1, <i>T</i>)–(<i>L</i> + <i>w</i> − 1, <i>T</i>), the
	/// next the right edge (<i>L</i> + <i>w</i>, <i>T</i> + 1)–(<i>L</i> + <i>w</i>, <i>T</i> + <i>h</i> − 1), the last
	/// the bottom edge (<i>L</i> + 1, <i>T</i> + <i>h</i>)–(<i>L</i> + <i>w</i> − 1, <i>T</i> + <i>h</i>).
	/// </summary>
	public static void DrawEdgeRing(Es1PanelCanvas canvas, Es1WidgetStyle style, int first, int grow, int inset, Es1Rect rect) {
		int left = rect.X0 + inset, top = rect.Y0 + inset;
		int width = rect.X1 - rect.X0 + grow, height = rect.Y1 - rect.Y0 + grow;
		Edge(canvas, style.Dword(first), left, top + 1, left, top + height - 1);
		Edge(canvas, style.Dword(first + 1), left + 1, top, left + width - 1, top);
		Edge(canvas, style.Dword(first + 2), left + width, top + 1, left + width, top + height - 1);
		Edge(canvas, style.Dword(first + 3), left + 1, top + height, left + width - 1, top + height);
	}

	/// <summary>
	/// <c>CornerFrame_DrawCorners</c> (<c>00044c64</c>)(style, rect): the record's corner frames blitted with flags 0, the
	/// top left's at (<i>x0</i>, <i>y0</i>), the top right's at (<i>x1</i> − <i>cw</i>, <i>y0</i>), the bottom left's at
	/// (<i>x0</i>, <i>y1</i> − <i>ch</i>) and the bottom right's at (<i>x1</i> − <i>cw</i>, <i>y1</i> − <i>ch</i>),
	/// <i>cw</i> and <i>ch</i> the record's corner size.
	/// </summary>
	public static void DrawCorners(Es1PanelCanvas canvas, Es1WidgetStyle style, Es1Rect rect, IReadOnlyList<DynamixBitmap>? corners) {
		int right = rect.X1 - style.CornerWidth, bottom = rect.Y1 - style.CornerHeight;
		canvas.Blit(Frame(corners, style.CornerFrame(0)), rect.X0, rect.Y0);
		canvas.Blit(Frame(corners, style.CornerFrame(1)), right, rect.Y0);
		canvas.Blit(Frame(corners, style.CornerFrame(2)), rect.X0, bottom);
		canvas.Blit(Frame(corners, style.CornerFrame(3)), right, bottom);
	}

	/// <summary>Frame <paramref name="index"/> of <paramref name="corners"/>; none when the bank or the frame is missing.</summary>
	internal static DynamixBitmap? Frame(IReadOnlyList<DynamixBitmap>? corners, int index) =>
		corners != null && index >= 0 && index < corners.Count ? corners[index] : null;

	/// <summary>One edge in <paramref name="color"/> under the pen {0, color}, none for a color of 0.</summary>
	private static void Edge(Es1PanelCanvas canvas, int color, int x0, int y0, int x1, int y1) {
		if (color != 0) {
			canvas.DrawLine(x0, y0, x1, y1, (byte)color);
		}
	}
}
