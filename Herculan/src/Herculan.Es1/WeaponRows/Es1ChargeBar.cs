using HercWorks.Core.Data.File.Dyn;
using Herculan.Es1.Panels;
using Herculan.Es1.View;

namespace Herculan.Es1.WeaponRows;

/// <summary>
/// An energy row's charge bar, <c>WeaponSliderGadget</c> (docs/es1/simulation/cockpit-weapon-gauges.md#the-charge-bar--weaponslidergadget):
/// an LED bar graph <c>+0x46</c> that fills with the weapon's charge <c>+0x7a</c>, under a one-column knob from
/// <c>dba\pbuttons</c> at the weapon's power level, the slider base's knob, which the pointer can drag. Its slots are
/// <c>WeaponSliderGadgetVtable</c> (<c>00093d4a</c>)'s; the value, set-value and drag slots are
/// <c>SliderWidget_*H</c>'s.
/// </summary>
public sealed class Es1ChargeBar : Es1SliderWidget {
	/// <summary><c>pbuttons</c>' frame the knob is.</summary>
	public const int KnobFrame = 26;

	/// <summary>The bar's range, the maximum of the knob's.</summary>
	public const short Full = 0x400;

	/// <summary>The shades and the unlit color the bar takes when the constructor's colors argument is 0.</summary>
	public const byte EvenShade = 0x32, OddShade = 0x31, UnlitColor = 0x3f;

	private readonly Es1EnergyWeaponGauge _gauge;

	/// <summary>
	/// <c>WeaponSliderGadget_Ctor</c> (<c>000386a0</c>)(0, <paramref name="colors"/>, <paramref name="gauge"/>,
	/// <paramref name="rect"/>): <c>SliderWidget_CtorBase</c>(bar, 1, 0, <c>0x100</c>, 0, rect) and the scale; the knob,
	/// <c>pbuttons</c>' frame 26, one column long on {<i>x0</i>, <i>y0</i> − 1, <i>x0</i> + 1, <i>y1</i> + 1}, the saved
	/// region and its top-left starting there; the bar, <c>LedBarGraph_Ctor</c>(0, <c>0x400</c>, rect), given
	/// <see cref="EvenShade"/>, <see cref="OddShade"/> and <see cref="UnlitColor"/> when <paramref name="colors"/> is 0
	/// and no color otherwise; the value and the value last painted 0; the position at <i>x0</i>, the maximum
	/// <c>0x400</c> and the scale again.
	/// </summary>
	/// <param name="colors">The energy block's <c>int32</c> at 304, 0 in the eight retail files.</param>
	/// <param name="pushButtons"><c>CockpitPButtonsBank</c>; no knob is blitted when it or the frame is missing.</param>
	internal Es1ChargeBar(int colors, Es1EnergyWeaponGauge gauge, Es1Rect rect, IReadOnlyList<DynamixBitmap>? pushButtons)
		: base(true, 1, 0, 0x100, 0, rect) {
		_gauge = gauge;
		RecomputeScale();
		Knob = pushButtons != null && KnobFrame < pushButtons.Count ? pushButtons[KnobFrame] : null;
		KnobLength = 1;
		KnobRect = new Es1Rect(rect.X0, rect.Y0 - 1, rect.X0 + 1, rect.Y1 + 1);
		DrawnCorner = (KnobRect.X0, KnobRect.Y0);
		SavedRegion = KnobRect;
		Screen = new ushort[(KnobRect.X1 - KnobRect.X0 + 1) * (KnobRect.Y1 - KnobRect.Y0 + 1)];
		Bar = colors == 0
			? new Es1LedBarGraph(Full, rect) { EvenShade = EvenShade, OddShade = OddShade, UnlitColor = UnlitColor }
			: new Es1LedBarGraph(Full, rect);
		Position = (short)rect.X0;
		PreviousPosition = Position;
		Maximum = Full;
		RecomputeScale();
	}

	/// <summary><c>+0x46</c>, the bar.</summary>
	public Es1LedBarGraph Bar { get; }

	/// <summary><c>+0x4e</c>, the knob's bitmap.</summary>
	public DynamixBitmap? Knob { get; }

	/// <summary><c>+0x7a</c>, the value the bar shows, the capacitor's charge, which the gauge's set-state writes.</summary>
	public int Value { get; set; }

	/// <summary><c>+0x4a</c>, the value the bar was last painted to.</summary>
	public int PaintedValue { get; private set; }

	/// <summary><c>+0x6e</c>/<c>+0x72</c>, the knob's top-left as last drawn, where the saved screen goes back.</summary>
	public (int X, int Y) DrawnCorner { get; private set; }

	/// <summary>The region <c>+0x56</c>'s rect <c>+0x5e</c>–<c>+0x6a</c>: the knob's rect plus the canvas origin when the screen under it was saved.</summary>
	private Es1Rect SavedRegion { get; set; }

	/// <summary><c>+0x52</c>, the screen saved under the knob.</summary>
	private ushort[] Screen { get; set; }

	/// <summary>
	/// Slot <c>+4</c>, <c>WeaponSliderGadget_Paint</c> (<c>000389ac</c>): when <c>CockpitView_RectCornerVisible</c> passes
	/// the rect and the state is not 2: the saved screen put back when the knob has moved off the position; the bar reset
	/// and painted to <see cref="Value"/>, which becomes <see cref="PaintedValue"/>; and the knob moved to the position,
	/// the screen under it saved and the knob blitted (<see cref="DrawKnob"/>).
	/// </summary>
	public override void Paint() {
		var cockpit = _gauge.Cockpit;
		if (!cockpit.RectCornerVisible(Rect) || State == Hidden) {
			return;
		}

		var canvas = cockpit.PushCanvasContext();
		if (KnobRect.X0 != Position) {
			canvas.RestoreRegion(SavedRegion, DrawnCorner.X, DrawnCorner.Y, Screen);
		}

		Bar.Reset();
		Bar.PaintToValue(canvas, (short)Value);
		PaintedValue = Value;
		KnobRect = KnobRect with { X0 = Position, X1 = Position + KnobLength };
		DrawKnob(canvas);
	}

	/// <summary>Slot <c>+8</c>, <c>HSliderGadget_InvalidateDeferredWeaponGaugeCopy</c> (<c>00038984</c>): the paint counter set to 2.</summary>
	public override void Invalidate() => PaintCounter = 2;

	/// <summary>
	/// Slot <c>+0xc</c>, <c>HSliderGadget_OnClick</c> (<c>0003166c</c>)(bar, <paramref name="value"/>): the gauge's
	/// <see cref="Es1EnergyWeaponGauge.OnChildClick"/> with the value, the bar invalidated, and the mixin's
	/// <c>Widget_NoClickSound</c>, which plays nothing.
	/// </summary>
	public override void OnClick(short value) {
		_gauge.OnChildClick(value, this);
		Invalidate();
	}

	/// <summary>
	/// Slot <c>+0x10</c>, <c>WeaponSliderGadget_FlushDeferredPaint</c> (<c>00038b30</c>): at a paint counter of 2, with
	/// the rect on screen and the state not 2, the saved screen put back; the bar painted to <see cref="Value"/> only
	/// when that differs from <see cref="PaintedValue"/>, drawing only the strip between; the knob moved, saved and blitted
	/// as the paint does; {the bar's start, the knob's <i>y0</i>, the bar's end, the knob's <i>y1</i>} copied to the page
	/// on the screen; and the counter 0.
	/// </summary>
	public override void Tick() {
		var cockpit = _gauge.Cockpit;
		if (PaintCounter != 2 || !cockpit.RectCornerVisible(Rect) || State == Hidden) {
			return;
		}

		var canvas = cockpit.PushCanvasContext();
		canvas.RestoreRegion(SavedRegion, DrawnCorner.X, DrawnCorner.Y, Screen);
		if (Value != PaintedValue) {
			Bar.PaintToValue(canvas, (short)Value);
			PaintedValue = Value;
		}

		KnobRect = KnobRect with { X0 = Position, X1 = Position + KnobLength };
		DrawKnob(canvas);
		cockpit.DrawToCockpit(new Es1Rect(Bar.Start, KnobRect.Y0, Bar.End, KnobRect.Y1));
		PaintCounter = 0;
	}

	/// <summary>
	/// The paints' last steps, as the throttle's slider takes them: the display's draw page made its source page; the
	/// region's rect the knob's rect plus the canvas origin, and the screen under it saved; the knob blitted at its
	/// rect's top-left; and that top-left kept as where the saved screen goes back.
	/// </summary>
	private void DrawKnob(Es1PanelCanvas canvas) {
		var display = canvas.Display;
		display.SourcePage = display.DrawPage;
		SavedRegion = KnobRect.Offset(canvas.Origin.X, canvas.Origin.Y);
		Screen = canvas.SaveRegion(SavedRegion);
		canvas.Blit(Knob, KnobRect.X0, KnobRect.Y0);
		DrawnCorner = (KnobRect.X0, KnobRect.Y0);
	}
}
