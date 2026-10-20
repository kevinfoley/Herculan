using Herculan.Es1.View;

namespace Herculan.Es1.Panels;

/// <summary>
/// A horizontal LED bar graph, <c>LEDBarGraphH</c> (docs/es1/simulation/cockpit-widgets.md#led-bar-graphs): a
/// <c>BarGraphH</c>'s rect, span, start, end and position, its unlit color <c>+0x20</c> and its two shades
/// <c>+0x2c</c> and <c>+0x30</c>, drawn through the render context installed when it draws. As far as the preferences
/// panel calls it: the constructor and slot <c>+8</c>, <see cref="RepaintToValue"/>; the paint slot <c>+4</c>, which
/// repaints only the strip that changed, is the cockpit's instruments', plan step 6c. Here the render context is the
/// panel's canvas, which the draws take.
/// </summary>
public sealed class Es1LedBarGraph {
	/// <summary>
	/// <c>LedBarGraph_Ctor</c> (<c>ES1DBSIM 00031e28</c>)(0, <paramref name="range"/>, <paramref name="rect"/>), through
	/// <c>BarGraphH_Ctor</c> (<c>00031740</c>): the rect; the span (<i>x1</i> − <i>x0</i>) · <c>0x10000</c> / range, a
	/// 16.16 count of pixels a unit of value; the start <i>x0</i> and the end <i>x1</i>; and the position at the end
	/// for a span below 1 and at the start otherwise. No color is set.
	/// </summary>
	public Es1LedBarGraph(short range, Es1Rect rect) {
		Rect = rect;
		Span = (rect.X1 - rect.X0) * 0x10000 / range;
		Start = rect.X0;
		End = rect.X1;
		Position = Span < 1 ? End : Start;
	}

	/// <summary><c>+0</c>, the rect, whose rows every fill takes.</summary>
	public Es1Rect Rect { get; }

	/// <summary><c>+0x10</c>, the span; its sign picks the edge the bar fills from.</summary>
	public int Span { get; }

	/// <summary><c>+0x14</c> and <c>+0x18</c>, the first and last column.</summary>
	public int Start { get; }

	/// <inheritdoc cref="Start"/>
	public int End { get; }

	/// <summary><c>+0x1c</c>, the fill position the paint keeps; the repaint leaves it.</summary>
	public int Position { get; }

	/// <summary><c>+0x20</c>, the unlit color.</summary>
	public byte UnlitColor { get; init; }

	/// <summary><c>+0x2c</c>, the even columns' shade.</summary>
	public byte EvenShade { get; init; }

	/// <summary><c>+0x30</c>, the odd columns' shade.</summary>
	public byte OddShade { get; init; }

	/// <summary>
	/// <c>LedBarGraph_RepaintToValue</c> (<c>00031fac</c>)(bar, a, <paramref name="value"/>), slot <c>+8</c>, its
	/// <i>a</i> not read: the whole bar for the value, which it does not store. The position <i>p</i> is start +
	/// (value · span &gt;&gt; 16) for a span of 1 or more and end − (value · span &gt;&gt; 16) otherwise; start to
	/// <i>p</i> is drawn through <see cref="FillPinstripe"/>, and <i>p</i> + 1 to end filled through the brush
	/// {0, <see cref="UnlitColor"/>}, both on the rect's rows. Retail writes each run's columns into the rect's
	/// <i>x0</i> and <i>x1</i> for the fill and leaves them there; nothing the panel calls reads them.
	/// </summary>
	public void RepaintToValue(Es1PanelCanvas canvas, int value) {
		int p = Span < 1 ? End - (value * Span >> 16) : Start + (value * Span >> 16);
		FillPinstripe(canvas, Start, p);
		canvas.FillRect(p + 1, Rect.Y0, End, Rect.Y1, UnlitColor);
	}

	/// <summary>
	/// <c>LedBarGraph_FillPinstripe</c> (<c>00032078</c>)(bar, rect) on the columns <paramref name="x0"/> to
	/// <paramref name="x1"/> of the rect's rows: a line from (<i>x</i>, <i>y0</i>) to (<i>x</i>, <i>y1</i>) at each even
	/// column <i>x</i> in <see cref="EvenShade"/>, then at each odd one in <see cref="OddShade"/>, the column's parity its
	/// own, not its offset from <paramref name="x0"/>.
	/// </summary>
	private void FillPinstripe(Es1PanelCanvas canvas, int x0, int x1) {
		for (int x = (x0 & 1) != 0 ? x0 + 1 : x0; x <= x1; x += 2) {
			canvas.DrawLine(x, Rect.Y0, x, Rect.Y1, EvenShade);
		}

		for (int x = (x0 & 1) == 0 ? x0 + 1 : x0; x <= x1; x += 2) {
			canvas.DrawLine(x, Rect.Y0, x, Rect.Y1, OddShade);
		}
	}
}
