using Herculan.Es1.View;

namespace Herculan.Es1.Panels;

/// <summary>
/// A vertical LED bar graph, <c>LEDBarGraphV</c> (docs/es1/simulation/cockpit-widgets.md#led-bar-graphs): a
/// <c>BarGraphV</c>'s rect, span, start, end and position on the rect's rows, its unlit color <c>+0x20</c> and its two
/// shades <c>+0x2c</c> and <c>+0x30</c>, drawn through the canvas the caller hands the draws: the constructor, the paint
/// slot <c>+4</c> (<see cref="PaintToValue"/>) and the repaint slot <c>+8</c> (<see cref="Repaint"/>). With a span of 1
/// or more, a rect whose bottom is below its top, it fills from the bottom.
/// </summary>
public sealed class Es1LedBarGraphV : IEs1LedBarGraph {
	// The rect's y0 and y1 as the last fill wrote them, which GetRect answers.
	private int _drawnY0, _drawnY1;

	/// <summary>
	/// <c>LEDBarGraphV_Ctor</c> (<c>ES1DBSIM 0003216c</c>)(0, <paramref name="range"/>, <paramref name="rect"/>), through
	/// <c>BarGraphV_Ctor</c> (<c>00031aac</c>): the rect; the span (<i>y1</i> − <i>y0</i>) · <c>0x10000</c> / range; the
	/// start <i>y0</i> and the end <i>y1</i>; and the position at the start for a span below 1 and at the end otherwise.
	/// No color is set.
	/// </summary>
	public Es1LedBarGraphV(short range, Es1Rect rect) {
		Rect = rect;
		Span = (rect.Y1 - rect.Y0) * 0x10000 / range;
		Start = rect.Y0;
		End = rect.Y1;
		Position = Span < 1 ? Start : End;
		(_drawnY0, _drawnY1) = (rect.Y0, rect.Y1);
	}

	/// <summary><c>+0</c>, the rect as built, whose columns every fill takes; <see cref="GetRect"/> answers it with the rows the last fill wrote.</summary>
	public Es1Rect Rect { get; }

	/// <summary><c>+0x10</c>, the span; its sign picks the edge the bar fills from.</summary>
	public int Span { get; }

	/// <summary><c>+0x14</c> and <c>+0x18</c>, the first and last row.</summary>
	public int Start { get; }

	/// <inheritdoc cref="Start"/>
	public int End { get; }

	/// <summary><c>+0x1c</c>, the fill position the paint keeps; the repaint draws at it.</summary>
	public int Position { get; private set; }

	/// <summary><c>+0x20</c>, the unlit color.</summary>
	public byte UnlitColor { get; init; }

	/// <summary><c>+0x2c</c>, the even rows' shade.</summary>
	public byte EvenShade { get; init; }

	/// <summary><c>+0x30</c>, the odd rows' shade.</summary>
	public byte OddShade { get; init; }

	/// <inheritdoc/>
	public Es1Rect GetRect() => Rect with { Y0 = _drawnY0, Y1 = _drawnY1 };

	/// <summary>
	/// Slot <c>+4</c>, <c>LedBarGraphV_PaintToValue</c> (<c>000321a0</c>)(bar, <paramref name="value"/>): the new position
	/// <i>p</i>, start + (value · span &gt;&gt; 16) for a span below 1 and end − (value · span &gt;&gt; 16) otherwise;
	/// nothing when it is the stored <i>q</i>. Otherwise, filling from the start, <i>q</i> to <i>p</i> drawn through
	/// <see cref="FillPinstripe"/> as the bar grows and <i>p</i> to <i>q</i> filled in <see cref="UnlitColor"/> as it
	/// shrinks; filling from the end, <i>p</i> to <i>q</i> through the pinstripe as it grows and <i>q</i> to <i>p</i>
	/// unlit as it shrinks; both ends included; then <i>p</i> stored.
	/// </summary>
	public void PaintToValue(Es1PanelCanvas canvas, short value) {
		if (Span < 1) {
			int p = (value * Span >> 16) + Start;
			if (p == Position) {
				return;
			}

			if (p < Position) {
				FillUnlit(canvas, p, Position);
			} else {
				FillPinstripe(canvas, Position, p);
			}

			Position = p;
		} else {
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
		}
	}

	/// <summary>
	/// Slot <c>+8</c>, <c>LedBarGraphV_Repaint</c> (<c>000322f4</c>)(bar, a, b), neither argument read: the whole bar at
	/// the stored position <i>q</i>. With a span below 1, start to <i>q</i> through <see cref="FillPinstripe"/> and
	/// <i>q</i> + 1 to end unlit; otherwise start to <i>q</i> unlit and <i>q</i> + 1 to end through the pinstripe.
	/// </summary>
	public void Repaint(Es1PanelCanvas canvas, int value) {
		_ = value;
		if (Span < 1) {
			FillPinstripe(canvas, Start, Position);
			FillUnlit(canvas, Position + 1, End);
		} else {
			FillUnlit(canvas, Start, Position);
			FillPinstripe(canvas, Position + 1, End);
		}
	}

	/// <summary>The rows <paramref name="y0"/> to <paramref name="y1"/> of the rect's columns filled through a temporary brush {0, <see cref="UnlitColor"/>}, written into the rect's <i>y0</i> and <i>y1</i>.</summary>
	private void FillUnlit(Es1PanelCanvas canvas, int y0, int y1) {
		(_drawnY0, _drawnY1) = (y0, y1);
		canvas.FillRect(Rect.X0, y0, Rect.X1, y1, UnlitColor);
	}

	/// <summary>
	/// <c>LedBarGraphV_FillPinstripe</c> (<c>000323f0</c>)(bar, rect) on the rows <paramref name="y0"/> to
	/// <paramref name="y1"/> of the rect's columns, which its callers first write into the rect's <i>y0</i> and <i>y1</i>:
	/// a line from (<i>x0</i>, <i>y</i>) to (<i>x1</i>, <i>y</i>) at each even row <i>y</i> in <see cref="EvenShade"/>,
	/// then at each odd one in <see cref="OddShade"/>, the row's parity its own.
	/// </summary>
	private void FillPinstripe(Es1PanelCanvas canvas, int y0, int y1) {
		(_drawnY0, _drawnY1) = (y0, y1);
		for (int y = (y0 & 1) != 0 ? y0 + 1 : y0; y <= y1; y += 2) {
			canvas.DrawLine(Rect.X0, y, Rect.X1, y, EvenShade);
		}

		for (int y = (y0 & 1) == 0 ? y0 + 1 : y0; y <= y1; y += 2) {
			canvas.DrawLine(Rect.X0, y, Rect.X1, y, OddShade);
		}
	}
}
