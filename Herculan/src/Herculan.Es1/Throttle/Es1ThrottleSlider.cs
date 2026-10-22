using HercWorks.Core.Data.File.Dyn;
using HercWorks.Core.Data.File.Gau;
using Herculan.Es1.Panels;
using Herculan.Es1.View;

namespace Herculan.Es1.Throttle;

/// <summary>
/// The throttle's slider (docs/es1/simulation/cockpit-throttle-gauge.md#the-slider): the slider base on the track with
/// the gadget mixin's owner <c>+0x3e</c>, the knob's bitmap <c>+0x4e</c> and the screen saved under it <c>+0x52</c>, the
/// region <c>+0x56</c> it was saved from, the knob's top-left as last drawn <c>+0x6e</c>/<c>+0x72</c>, the bars'
/// pictures <c>+0x76</c>, the speed fraction <c>+0x7a</c> and the one the bars were last painted to <c>+0x4a</c>, and
/// the forward and reverse bars <c>+0x7e</c>, <c>+0x82</c>. Vertical, it is <c>ThrottleVSliderGadgetVtable</c>
/// (<c>0009435e</c>)'s; horizontal, the <c>RAPTOR</c>'s, <c>ThrottleHSliderGadgetVtable</c> (<c>000943fa</c>)'s.
///
/// <para>Its paints draw through the render context the cockpit has installed around the call, its canvas context,
/// which this engine takes from <see cref="Es1Cockpit.PushCanvasContext"/>.</para>
/// </summary>
public sealed class Es1ThrottleSlider : Es1SliderWidget {
	/// <summary>The light names at <c>0009328c</c>, which the block's <c>int32</c> <c>0x10</c> indexes (docs/es1/formats/str-strings.md).</summary>
	private static readonly string[] LightNames = ["WPN_LED1", "THR_LED1", "THR_LED2", "", ""];

	/// <summary>The horizontal knob's length, whatever its bitmap, and the columns its left lies left of the track's middle.</summary>
	private const short HorizontalKnobLength = 12, HorizontalKnobHalf = 6;

	/// <summary>The value at each end of the throttle, and the bars' ranges.</summary>
	private const short Full = 0x400;

	private readonly Es1ThrottleGauge _gauge;

	/// <summary>
	/// <c>ThrottleSlider_CtorV</c> (<c>0003b2c8</c>) or <c>ThrottleSlider_CtorFixed</c> (<c>0003ba74</c>)(0,
	/// <paramref name="block"/>, <paramref name="gauge"/>, <paramref name="track"/>)
	/// (docs/es1/simulation/cockpit-throttle-gauge.md#building-it): <c>SliderWidget_CtorBase</c>(slider, 1, 0,
	/// <c>0x100</c>, 0, track) and the scale; the owner; the bars' pictures; the knob, frame <c>int32</c> <c>0x11</c> of
	/// <c>dba\pbuttons</c>, placed in the track's middle, the screen's buffer the knob rect's size; the bars on the block's
	/// rects plus its origin, frame 1 the lit picture and frame 0 (forward) or 2 (reverse) the unlit; and the fractions 0,
	/// the position on the knob, the range ±<c>0x400</c> and the scale again.
	/// </summary>
	internal Es1ThrottleSlider(Es1ThrottleGauge gauge, Es1GauThrottleBlock block, Es1Rect track,
			IReadOnlyList<DynamixBitmap>? lights, IReadOnlyList<DynamixBitmap>? pushButtons, bool horizontal)
		: base(horizontal, 1, 0, 0x100, 0, track) {
		_gauge = gauge;
		RecomputeScale();

		Knob = Frame(pushButtons, block.KnobFrame);
		if (horizontal) {
			KnobLength = HorizontalKnobLength;
			int left = ((track.X1 - track.X0) >> 1) + track.X0 - HorizontalKnobHalf;
			KnobRect = new Es1Rect(left, track.Y0, left + KnobLength, track.Y1);
		} else {
			// The bitmap's rows, its int16 +4; with no bitmap, which a missing dba\pbuttons leaves, 0, an engine guard.
			KnobLength = Knob?.Rows ?? 0;
			int bottom = (KnobLength >> 1) + (track.Y1 - ((track.Y1 - track.Y0) >> 1));
			KnobRect = new Es1Rect(track.X0, bottom - KnobLength, track.X1, bottom);
		}

		DrawnCorner = (KnobRect.X0, KnobRect.Y0);
		SavedRegion = KnobRect;
		Screen = new ushort[(KnobRect.X1 - KnobRect.X0 + 1) * (KnobRect.Y1 - KnobRect.Y0 + 1)];

		int ox = block.Origin.X, oy = block.Origin.Y;
		var forward = Es1Rect.From(block.ForwardBar).Offset(ox, oy);
		var reverse = Es1Rect.From(block.ReverseBar).Offset(ox, oy);
		DynamixBitmap? forwardUnlit = Frame(lights, 0), lit = Frame(lights, 1), reverseUnlit = Frame(lights, 2);
		if (horizontal) {
			ForwardBar = Es1BitmapBarGraph.Horizontal(forwardUnlit, lit, Full, forward);
			ReverseBar = Es1BitmapBarGraph.Horizontal(reverseUnlit, lit, -Full, reverse);
		} else {
			ForwardBar = Es1BitmapBarGraph.VerticalBar(lit, forwardUnlit, Full, forward);
			ReverseBar = Es1BitmapBarGraph.VerticalBar(lit, reverseUnlit, -Full, reverse);
		}

		Position = (short)(horizontal ? KnobRect.X0 : KnobRect.Y1);
		PreviousPosition = Position;
		Maximum = Full;
		Minimum = -Full;
		RecomputeScale();
	}

	/// <summary><c>+0x4e</c>, the knob's bitmap; null when <c>dba\pbuttons</c> or its frame is missing, and nothing is blitted.</summary>
	public DynamixBitmap? Knob { get; }

	/// <summary><c>+0x7e</c>, the forward bar.</summary>
	public Es1BitmapBarGraph ForwardBar { get; }

	/// <summary><c>+0x82</c>, the reverse bar.</summary>
	public Es1BitmapBarGraph ReverseBar { get; }

	/// <summary><c>+0x7a</c>, the speed fraction, copied from the gauge by the per-frame paint.</summary>
	public int SpeedFraction { get; private set; }

	/// <summary><c>+0x4a</c>, the speed fraction the bars were last painted to by the per-frame paint.</summary>
	public int BarsFraction { get; private set; }

	/// <summary><c>+0x6e</c>/<c>+0x72</c>, the knob's top-left as last drawn, where the saved screen goes back.</summary>
	public (int X, int Y) DrawnCorner { get; private set; }

	/// <summary>
	/// The region <c>+0x56</c>'s rect <c>+0x5e</c>–<c>+0x6d</c>: the knob's rect plus the canvas origin when the screen
	/// under it was saved, whose size the restore takes.
	/// </summary>
	private Es1Rect SavedRegion { get; set; }

	/// <summary><c>+0x52</c>, the screen saved under the knob.</summary>
	private ushort[] Screen { get; set; }

	/// <summary>The <c>dba\</c> file the block's <c>int32</c> <c>0x10</c> names the bars' pictures by, or null past the table.</summary>
	public static string? LightName(int index) => index >= 0 && index < LightNames.Length ? LightNames[index] : null;

	/// <summary>
	/// Slot <c>+4</c>, the full paint, which the gauge's paint runs on a view change, while
	/// <c>CockpitView_RectCornerVisible</c> passes the track (docs/es1/simulation/cockpit-throttle-gauge.md#drawing-it).
	/// Vertical, <c>ThrottleSlider_DrawKnobV</c> (<c>0003b690</c>): each bar reset, painted to its full value and painted
	/// to <see cref="SpeedFraction"/>, so the whole bar is drawn; the knob's rows from the position; and the knob drawn
	/// over a saved copy of the screen. Horizontal, <c>ThrottleSlider_PaintH</c> (<c>0003bda8</c>): the same on the
	/// <i>x</i> axis, but for the bars' resets.
	/// </summary>
	public override void Paint() {
		var cockpit = _gauge.Cockpit;
		if (!cockpit.RectCornerVisible(Rect)) {
			return;
		}

		var canvas = cockpit.PushCanvasContext();
		short fraction = (short)SpeedFraction;
		if (Horizontal) {
			// Retail's ThrottleSlider_PaintH resets the forward bar twice, before each bar's pair of paints, and the reverse
			// bar never, so the reverse bar is painted full and back from its stored position: a HERC backing up has only the
			// bar's unlit part drawn, and its lit part comes back strip by strip as the speed changes
			// (docs/es1/simulation/cockpit-throttle-gauge.md#the-bars). Ported as retail has it, the user's choice; a tweak
			// that draws the lit part is an Open item of docs/herculan/plan-es1-first-mission.md.
			ForwardBar.Reset();
			ForwardBar.PaintToValue(canvas, Full);
			ForwardBar.PaintToValue(canvas, fraction);
			ForwardBar.Reset();
			ReverseBar.PaintToValue(canvas, -Full);
			ReverseBar.PaintToValue(canvas, fraction);
			KnobRect = KnobRect with { X0 = Position, X1 = Position + KnobLength };
		} else {
			ForwardBar.Reset();
			ForwardBar.PaintToValue(canvas, Full);
			ForwardBar.PaintToValue(canvas, fraction);
			ReverseBar.Reset();
			ReverseBar.PaintToValue(canvas, -Full);
			ReverseBar.PaintToValue(canvas, fraction);
			KnobRect = KnobRect with { Y0 = Position - KnobLength, Y1 = Position };
		}

		DrawKnob(canvas);
	}

	/// <summary>
	/// Slot <c>+8</c>, <c>VSliderGadget_InvalidateDeferred</c> (<c>0003b668</c>) or
	/// <c>HSliderGadget_InvalidateDeferredThrottleGaugeCopy</c> (<c>0003bd80</c>): the paint counter set to 2.
	/// </summary>
	public override void Invalidate() => PaintCounter = 2;

	/// <summary>
	/// Slot <c>+0xc</c>, <c>VSliderGadget_OnClick</c> (<c>000316a0</c>) or <c>HSliderGadget_OnClick</c>
	/// (<c>0003166c</c>)(slider, <paramref name="value"/>): the owner's <see cref="Es1ThrottleGauge.OnChildValue"/> with
	/// the value; the slider invalidated; and the mixin's click sound, <c>Widget_NoClickSound</c>, which plays nothing.
	/// </summary>
	public override void OnClick(short value) {
		_gauge.OnChildValue(value, this);
		Invalidate();
	}

	/// <summary>
	/// Slot <c>+0x10</c>, the per-frame paint, once a frame through the gauge's update, acting only while the paint
	/// counter is 2 and <c>CockpitView_RectCornerVisible</c> passes the track, so it stays at 2 while the track is out of
	/// sight. <c>ThrottleSlider_PaintV</c> (<c>0003b804</c>) or <c>ThrottleSlider_FlushPaintH</c> (<c>0003bf18</c>): the
	/// screen under the knob as last drawn put back; the gauge's speed fraction taken, and when it differs from the one
	/// the bars were last painted to, both bars painted to it, each drawing only the strip between its old and new fill;
	/// the rect to copy, the old knob's, the bars' and the new knob's; the knob drawn at the position; that rect copied to
	/// the page on the screen (<see cref="Es1Cockpit.DrawToCockpit"/>); and the counter 0.
	/// </summary>
	public override void Tick() {
		var cockpit = _gauge.Cockpit;
		if (PaintCounter != 2 || !cockpit.RectCornerVisible(Rect)) {
			return;
		}

		var canvas = cockpit.PushCanvasContext();
		canvas.RestoreRegion(SavedRegion, DrawnCorner.X, DrawnCorner.Y, Screen);
		SpeedFraction = _gauge.SpeedFraction;
		if (SpeedFraction != BarsFraction) {
			ForwardBar.PaintToValue(canvas, (short)SpeedFraction);
			ReverseBar.PaintToValue(canvas, (short)SpeedFraction);
			BarsFraction = SpeedFraction;
		}

		var old = KnobRect;
		var forward = ForwardBar.Rect;
		Es1Rect copied;
		if (Horizontal) {
			copied = new Es1Rect(Math.Min(old.X0, ReverseBar.Start), Math.Min(old.Y0, forward.Y0),
				Math.Max(old.X1, ForwardBar.End), Math.Max(old.Y1, forward.Y1));
			KnobRect = KnobRect with { X0 = Position, X1 = Position + KnobLength };
			copied = copied with { X0 = Math.Min(copied.X0, KnobRect.X0), X1 = Math.Max(copied.X1, KnobRect.X1) };
		} else {
			copied = new Es1Rect(Math.Min(old.X0, forward.X0), Math.Min(old.Y0, ForwardBar.Start),
				Math.Max(old.X1, forward.X1), Math.Max(old.Y1, ReverseBar.End));
			KnobRect = KnobRect with { Y0 = Position - KnobLength, Y1 = Position };
			copied = copied with { Y0 = Math.Min(copied.Y0, KnobRect.Y0), Y1 = Math.Max(copied.Y1, KnobRect.Y1) };
		}

		DrawKnob(canvas);
		cockpit.DrawToCockpit(copied);
		PaintCounter = 0;
	}

	/// <summary>
	/// The paints' last steps: the display's draw page made its source page; the region's rect the knob's rect plus the
	/// canvas origin, <c>+0x220</c>/<c>+0x224</c>, and the screen under it saved (<c>Region_Save</c>, which moves it by
	/// the translation twice, so with the cockpit's canvas rect at (0, 0) the screen under the knob as blitted); the knob
	/// blitted at its rect's top-left (<c>Bitmap_Blit</c>, flags 0); and that top-left kept as where the saved screen goes
	/// back.
	/// </summary>
	private void DrawKnob(Es1PanelCanvas canvas) {
		var display = canvas.Display;
		display.SourcePage = display.DrawPage;
		SavedRegion = KnobRect.Offset(canvas.Origin.X, canvas.Origin.Y);
		Screen = canvas.SaveRegion(SavedRegion);
		canvas.Blit(Knob, KnobRect.X0, KnobRect.Y0);
		DrawnCorner = (KnobRect.X0, KnobRect.Y0);
	}

	/// <summary>Frame <paramref name="index"/> of <paramref name="bank"/>; null when the bank or the frame is missing.</summary>
	private static DynamixBitmap? Frame(IReadOnlyList<DynamixBitmap>? bank, int index) =>
		bank != null && index >= 0 && index < bank.Count ? bank[index] : null;
}
