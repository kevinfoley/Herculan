using HercWorks.Core.Data.File.Dyn;
using Herculan.Es1.View;

namespace Herculan.Es1.Panels;

/// <summary>
/// A bitmap bar graph, <c>LEDBMBarGraphH</c> or <c>LEDBMBarGraphV</c> (docs/es1/simulation/cockpit-widgets.md#bitmap-bar-graphs):
/// a <c>BarGraphH</c>'s or <c>BarGraphV</c>'s rect, span, start, end and fill position (docs/es1/simulation/cockpit-widgets.md#bar-graphs),
/// whose lit and unlit parts are slices of a full and an empty picture, and whose paint draws only the strip between
/// its old and new fill. As far as the throttle's slider calls it: the constructors, the paint slot <c>+4</c>, the reset
/// slot <c>+0xc</c> and <c>BarGraph_GetRect</c>; the repaint slot <c>+8</c> does nothing. The render context it draws
/// through is the one installed when it draws, which this engine hands it.
/// </summary>
public sealed class Es1BitmapBarGraph {
	private Es1Rect _rect;

	private Es1BitmapBarGraph(bool vertical, short range, Es1Rect rect, DynamixBitmap? lit, DynamixBitmap? unlit) {
		Vertical = vertical;
		_rect = rect;
		Lit = lit;
		Unlit = unlit;
		Span = vertical ? (rect.Y1 - rect.Y0) * 0x10000 / range : (rect.X1 - rect.X0) * 0x10000 / range;
		Start = vertical ? rect.Y0 : rect.X0;
		End = vertical ? rect.Y1 : rect.X1;
		Reset();
	}

	/// <summary>
	/// <c>LedBmBarGraphH_Ctor</c> (<c>ES1DBSIM 000324e4</c>)(0, <paramref name="unlit"/>, <paramref name="lit"/>,
	/// <paramref name="range"/>, <paramref name="rect"/>), through <c>BarGraphH_Ctor</c> (<c>00031740</c>): the span
	/// (<i>x1</i> − <i>x0</i>) · <c>0x10000</c> / range, start <i>x0</i>, end <i>x1</i>, the position at the end for a
	/// span below 1 and at the start otherwise; the unlit picture at <c>+0x64</c> and the lit at <c>+0x60</c>.
	/// </summary>
	public static Es1BitmapBarGraph Horizontal(DynamixBitmap? unlit, DynamixBitmap? lit, short range, Es1Rect rect) =>
		new(false, range, rect, lit, unlit);

	/// <summary>
	/// <c>LedBmBarGraphV_Ctor</c> (<c>00032a18</c>)(0, <paramref name="lit"/>, <paramref name="unlit"/>,
	/// <paramref name="range"/>, <paramref name="rect"/>), through <c>BarGraphV_Ctor</c> (<c>00031aac</c>): the same on
	/// <i>y</i>, the position at the start for a span below 1 and at the end otherwise; the lit picture at <c>+0x64</c>
	/// and the unlit at <c>+0x60</c>.
	/// </summary>
	public static Es1BitmapBarGraph VerticalBar(DynamixBitmap? lit, DynamixBitmap? unlit, short range, Es1Rect rect) =>
		new(true, range, rect, lit, unlit);

	/// <summary>Whether the bar is a <c>BarGraphV</c>, filling along <i>y</i>.</summary>
	public bool Vertical { get; }

	/// <summary>
	/// <c>+0</c>–<c>+0xc</c>, the rect, which <c>BarGraph_GetRect</c> (<c>00031738</c>) answers. Each paint that draws
	/// writes its strip's ends into the rect's <i>x0</i> and <i>x1</i> (<c>LEDBMBarGraphH</c>) or <i>y0</i> and <i>y1</i>
	/// (<c>LEDBMBarGraphV</c>) and leaves them there.
	/// </summary>
	public Es1Rect Rect => _rect;

	/// <summary><c>+0x10</c>, the span, 16.16 pixels a unit of value; its sign picks the edge the bar fills from.</summary>
	public int Span { get; }

	/// <summary><c>+0x14</c> and <c>+0x18</c>, the bar's first and last column or row.</summary>
	public int Start { get; }

	/// <inheritdoc cref="Start"/>
	public int End { get; }

	/// <summary><c>+0x1c</c>, the fill position the paint keeps.</summary>
	public int Position { get; private set; }

	/// <summary>The lit picture: <c>+0x60</c> of an <c>LEDBMBarGraphH</c>, <c>+0x64</c> of an <c>LEDBMBarGraphV</c>.</summary>
	public DynamixBitmap? Lit { get; }

	/// <summary>The unlit picture: <c>+0x64</c> of an <c>LEDBMBarGraphH</c>, <c>+0x60</c> of an <c>LEDBMBarGraphV</c>.</summary>
	public DynamixBitmap? Unlit { get; }

	/// <summary>
	/// Slot <c>+0xc</c>, <c>BarGraphH_ResetToEmpty</c> (<c>000317a4</c>) or <c>BarGraphV_ResetToEmpty</c>
	/// (<c>00031b10</c>): the position put back on the edge the bar fills from, the end of a <c>BarGraphH</c> or the start
	/// of a <c>BarGraphV</c> for a span below 1, the other edge otherwise. Nothing is drawn.
	/// </summary>
	public void Reset() => Position = FillsFromStart ? Start : End;

	/// <summary>Whether the bar fills from its start: a <c>BarGraphH</c> with a span of 1 or more, a <c>BarGraphV</c> with one below 1.</summary>
	private bool FillsFromStart => (Span < 1) == Vertical;

	/// <summary>
	/// Slot <c>+4</c>, <c>LedBmBarGraphH_PaintToValue</c> (<c>00032764</c>) or <c>LedBmBarGraphV_PaintToValue</c>
	/// (<c>00032c1c</c>), (bar, <paramref name="value"/>): the new position <i>p</i>, start + (value · span ≫ 16) for a
	/// bar filling from its start and end − (value · span ≫ 16) for one filling from its end, held to at most the end and
	/// then to at least the start; when it differs from the stored <i>q</i>, the strip between them, both ends included,
	/// is drawn from the lit picture when the bar grows and the unlit one when it shrinks
	/// (<see cref="DrawBitmapSpan"/>), and <i>p</i> stored.
	/// </summary>
	public void PaintToValue(Es1PanelCanvas canvas, short value) {
		bool fromStart = FillsFromStart;
		int step = value * Span >> 16;
		int p = fromStart ? Start + step : End - step;
		if (p > End) {
			p = End;
		}

		if (p < Start) {
			p = Start;
		}

		int q = Position;
		if (p == q) {
			return;
		}

		bool grows = fromStart ? p > q : p < q;
		int low = Math.Min(p, q), high = Math.Max(p, q);
		_rect = Vertical ? _rect with { Y0 = low, Y1 = high } : _rect with { X0 = low, X1 = high };
		DrawBitmapSpan(canvas, grows ? Lit : Unlit);
		Position = p;
	}

	/// <summary>
	/// <c>LedBmBarGraphH_DrawBitmapSpan</c> (<c>00032838</c>) or <c>LedBmBarGraphV_DrawBitmapSpan</c> (<c>00032cf4</c>),
	/// (bar, <paramref name="bitmap"/>): the strip now in <see cref="Rect"/> takes the same columns (rows) of the picture
	/// as it covers of the bar. With (<i>ox</i>, <i>oy</i>) the canvas origin: the display's draw page made its source
	/// page; the screen saved under the region {2<i>ox</i>, 2<i>oy</i>, 2<i>ox</i> + <i>w</i>, 2<i>oy</i> + <i>h</i>},
	/// <i>w</i> and <i>h</i> the columns and rows of the picture at <c>+0x64</c>; the picture blitted at the origin; the
	/// strip copied from it, the region {<i>x0</i> − start + <i>ox</i>, <i>oy</i>, <i>x1</i> − start + <i>ox</i>,
	/// <i>y1</i> − <i>y0</i> + <i>oy</i>} at the offset (start − <i>ox</i>, <i>y0</i> − <i>oy</i>) across, or
	/// {<i>ox</i>, <i>y0</i> − start + <i>oy</i>, <i>x1</i> − <i>x0</i> + <i>ox</i>, <i>y1</i> − start + <i>oy</i>} at
	/// (<i>x0</i> − <i>ox</i>, start − <i>oy</i>) down; and the saved screen put back at the origin. Region_Save and the
	/// blit each move by the canvas's translation, Region_Save twice, so with the cockpit's canvas rect at (0, 0) the
	/// picture is drawn and taken off again at the canvas's top-left on the screen.
	///
	/// <para>With no picture at <c>+0x64</c> or none to draw, which a missing <c>dba\</c> file leaves, nothing is drawn,
	/// an engine guard: retail's load has no such case.</para>
	/// </summary>
	private void DrawBitmapSpan(Es1PanelCanvas canvas, DynamixBitmap? bitmap) {
		var sized = Vertical ? Lit : Unlit;
		if (sized == null || bitmap == null) {
			return;
		}

		var (ox, oy) = canvas.Origin;
		var saved = new Es1Rect(2 * ox, 2 * oy, 2 * ox + sized.Cols, 2 * oy + sized.Rows);
		var strip = _rect;
		Es1Rect copied;
		int offsetX, offsetY;
		if (Vertical) {
			copied = new Es1Rect(ox, strip.Y0 - Start + oy, strip.X1 - strip.X0 + ox, strip.Y1 - Start + oy);
			(offsetX, offsetY) = (strip.X0 - ox, Start - oy);
		} else {
			copied = new Es1Rect(strip.X0 - Start + ox, oy, strip.X1 - Start + ox, strip.Y1 - strip.Y0 + oy);
			(offsetX, offsetY) = (Start - ox, strip.Y0 - oy);
		}

		var display = canvas.Display;
		display.SourcePage = display.DrawPage;
		var screen = canvas.SaveRegion(saved);
		canvas.Blit(bitmap, ox, oy);
		canvas.CopyRegion(copied, offsetX, offsetY);
		canvas.RestoreRegion(saved, ox, oy, screen);
	}
}
