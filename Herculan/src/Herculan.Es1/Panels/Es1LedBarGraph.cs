using Herculan.Es1.View;

namespace Herculan.Es1.Panels;

/// <summary>
/// The slots of an LED bar graph's table that the energy meter calls on either orientation
/// (docs/es1/simulation/cockpit-energy-shields.md#drawing-the-meter), drawn through the canvas the caller hands them.
/// </summary>
public interface IEs1LedBarGraph {
	/// <summary>Slot <c>+4</c>, the paint to <paramref name="value"/>, which draws only the strip between the old position and the new.</summary>
	void PaintToValue(Es1PanelCanvas canvas, short value);

	/// <summary>Slot <c>+8</c>, the repaint, (bar, 0, <paramref name="value"/>), which draws the whole bar.</summary>
	void Repaint(Es1PanelCanvas canvas, int value);

	/// <summary>
	/// <c>BarGraph_GetRect</c> (<c>ES1DBSIM 00031738</c>)(bar): the bar's rect as the fills last left it, the rect as built
	/// with the ends of the last strip drawn on the bar's axis.
	/// </summary>
	Es1Rect GetRect();
}

/// <summary>
/// A horizontal LED bar graph, <c>LEDBarGraphH</c> (docs/es1/simulation/cockpit-widgets.md#led-bar-graphs): a
/// <c>BarGraphH</c>'s rect, span, start, end and position, its unlit color <c>+0x20</c> and its two shades
/// <c>+0x2c</c> and <c>+0x30</c>, drawn through the render context installed when it draws: the constructor, the paint
/// slot <c>+4</c> (<see cref="PaintToValue"/>), the repaint slot <c>+8</c> (<see cref="RepaintToValue"/>) and the reset
/// slot <c>+0xc</c> (<see cref="Reset"/>). Here the render context is the canvas the caller hands the draws.
/// </summary>
public sealed class Es1LedBarGraph : IEs1LedBarGraph {
	// The rect's x0 and x1 as the last fill wrote them, which GetRect answers.
	private int _drawnX0, _drawnX1;

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
		(_drawnX0, _drawnX1) = (rect.X0, rect.X1);
		Reset();
	}

	/// <summary>
	/// <c>+0</c>, the rect as built, whose rows every fill takes. Retail writes each fill's columns into the rect's
	/// <i>x0</i> and <i>x1</i> and leaves them there, which <see cref="GetRect"/> answers.
	/// </summary>
	public Es1Rect Rect { get; }

	/// <inheritdoc/>
	public Es1Rect GetRect() => Rect with { X0 = _drawnX0, X1 = _drawnX1 };

	/// <inheritdoc/>
	void IEs1LedBarGraph.Repaint(Es1PanelCanvas canvas, int value) => RepaintToValue(canvas, value);

	/// <summary><c>+0x10</c>, the span; its sign picks the edge the bar fills from.</summary>
	public int Span { get; }

	/// <summary><c>+0x14</c> and <c>+0x18</c>, the first and last column.</summary>
	public int Start { get; }

	/// <inheritdoc cref="Start"/>
	public int End { get; }

	/// <summary><c>+0x1c</c>, the fill position the paint keeps; the repaint leaves it.</summary>
	public int Position { get; private set; }

	/// <summary><c>+0x20</c>, the unlit color.</summary>
	public byte UnlitColor { get; init; }

	/// <summary><c>+0x2c</c>, the even columns' shade.</summary>
	public byte EvenShade { get; init; }

	/// <summary><c>+0x30</c>, the odd columns' shade.</summary>
	public byte OddShade { get; init; }

	/// <summary>
	/// Slot <c>+0xc</c>, <c>BarGraphH_ResetToEmpty</c> (<c>000317a4</c>): the position at the end for a span below 1 and
	/// at the start otherwise. Nothing is drawn.
	/// </summary>
	public void Reset() => Position = Span < 1 ? End : Start;

	/// <summary>
	/// Slot <c>+4</c>, <c>LedBarGraph_PaintToValue</c> (<c>00031e5c</c>)(bar, <paramref name="value"/>): the new position
	/// <i>p</i>, start + (value · span &gt;&gt; 16) for a span of 1 or more and end − (value · span &gt;&gt; 16)
	/// otherwise; nothing when it is the stored <i>q</i>. Otherwise the strip that becomes lit, <i>q</i> to <i>p</i>, drawn
	/// through <see cref="FillPinstripe"/>, and the strip that becomes unlit filled in <see cref="UnlitColor"/>, from
	/// <i>p</i> + 1 to <i>q</i> for a bar filling from its start and from <i>q</i> to <i>p</i> for one filling from its
	/// end; then <i>p</i> stored.
	/// </summary>
	public void PaintToValue(Es1PanelCanvas canvas, short value) {
		if (Span < 1) {
			int p = End - (value * Span >> 16);
			if (p == Position) {
				return;
			}

			if (Position < p) {
				FillUnlit(canvas, Position, p);
			} else {
				FillPinstripe(canvas, p, Position);
			}

			Position = p;
		} else {
			int p = Start + (value * Span >> 16);
			if (p == Position) {
				return;
			}

			if (p < Position) {
				FillUnlit(canvas, p + 1, Position);
			} else {
				FillPinstripe(canvas, Position, p);
			}

			Position = p;
		}
	}

	/// <summary>
	/// <c>LedBarGraph_RepaintToValue</c> (<c>00031fac</c>)(bar, a, <paramref name="value"/>), slot <c>+8</c>, its
	/// <i>a</i> not read: the whole bar for the value, which it does not store. The position <i>p</i> is start +
	/// (value · span &gt;&gt; 16) for a span of 1 or more and end − (value · span &gt;&gt; 16) otherwise; start to
	/// <i>p</i> is drawn through <see cref="FillPinstripe"/>, and <i>p</i> + 1 to end filled through the brush
	/// {0, <see cref="UnlitColor"/>}, both on the rect's rows.
	/// </summary>
	public void RepaintToValue(Es1PanelCanvas canvas, int value) {
		int p = Span < 1 ? End - (value * Span >> 16) : Start + (value * Span >> 16);
		FillPinstripe(canvas, Start, p);
		FillUnlit(canvas, p + 1, End);
	}

	/// <summary>The columns <paramref name="x0"/> to <paramref name="x1"/> of the rect's rows filled through the brush {0, <see cref="UnlitColor"/>}, written into the rect's <i>x0</i> and <i>x1</i>.</summary>
	private void FillUnlit(Es1PanelCanvas canvas, int x0, int x1) {
		(_drawnX0, _drawnX1) = (x0, x1);
		canvas.FillRect(x0, Rect.Y0, x1, Rect.Y1, UnlitColor);
	}

	/// <summary>
	/// <c>LedBarGraph_FillPinstripe</c> (<c>00032078</c>)(bar, rect) on the columns <paramref name="x0"/> to
	/// <paramref name="x1"/> of the rect's rows, which its callers first write into the rect's <i>x0</i> and <i>x1</i>: a
	/// line from (<i>x</i>, <i>y0</i>) to (<i>x</i>, <i>y1</i>) at each even column <i>x</i> in <see cref="EvenShade"/>,
	/// then at each odd one in <see cref="OddShade"/>, the column's parity its own, not its offset from
	/// <paramref name="x0"/>.
	/// </summary>
	private void FillPinstripe(Es1PanelCanvas canvas, int x0, int x1) {
		(_drawnX0, _drawnX1) = (x0, x1);
		for (int x = (x0 & 1) != 0 ? x0 + 1 : x0; x <= x1; x += 2) {
			canvas.DrawLine(x, Rect.Y0, x, Rect.Y1, EvenShade);
		}

		for (int x = (x0 & 1) == 0 ? x0 + 1 : x0; x <= x1; x += 2) {
			canvas.DrawLine(x, Rect.Y0, x, Rect.Y1, OddShade);
		}
	}
}
