using Herculan.Engine.Content;
using Herculan.Engine.Render;
using Herculan.Engine.Render.Cockpit;
using Herculan.Engine.Sim;

namespace Herculan.Engine.Cockpit;

/// <summary>
/// An energy weapon row's charge bar as the slider it is built as: where it can be pressed, and what
/// position a point on it means. Used only under
/// <see cref="Settings.TweakSettingDefinitions.ChargeBarPowerLevel"/>; in retail no press reaches it.
///
/// <para><b>What retail builds.</b> <c>EnergyWeaponGauge_Ctor</c> (<c>00440a68</c>) hands
/// <c>WeaponSliderGadget_Ctor</c> (<c>00442950</c>) the row's value field, <c>x0+0x24..x0+0x35</c>,
/// <c>y0..y0+5</c> GAU, as its rect. That constructor runs <c>SliderWidget_CtorBase</c>
/// (<c>004524a8</c>), which sets the drag flag, then makes the knob one GAU unit wide and the range
/// 0..<see cref="WeaponMount.ChargeBarRange"/>. Its drag handler (<c>SliderWidget_DragToPointH</c>, <c>004524f8</c>)
/// clamps the pointer into <c>[left, right - knobWidth]</c> and its getter
/// (<c>SliderWidget_GetValueH</c>, <c>00452544</c>) reads <c>(x - left) * 0x10000 / scale</c> with the
/// scale from <c>SliderWidget_RecomputeScaleH</c> (<c>004525a8</c>), so the 32 device pixels of travel
/// span the whole range. Nothing draws the knob: <c>WeaponSliderGadget_Paint</c> (<c>00442b38</c>)
/// paints the LED bar alone.</para>
///
/// <para><b>Why it is unreachable, and what the tweak changes.</b> The row's
/// <c>ChainedWeaponSelectGadget</c> is registered first and its rect is the whole hardpoint rect,
/// which contains the bar in every retail <c>.GAU</c>; first hit wins. The tweak lists the bar ahead
/// of its row instead, so the bar takes presses on its own span and the rest of the row still arms
/// the weapon. See docs/retail/formats/cockpit-input.md, "Where retail rects overlap", and
/// docs/retail/simulation/weapon-firing.md#the-charge-bar.</para>
/// </summary>
public readonly struct ChargeBarSlider {
	private const int Scale = (int)CockpitArt.GauToPixelScale;

	/// <summary>
	/// A slider from its measurements in device pixels. <see cref="For"/> is the normal way in.
	/// </summary>
	public ChargeBarSlider(int left, int top, int trackRight, int bottom, int knobWidth) {
		Left = left;
		Top = top;
		TrackRight = trackRight;
		Bottom = bottom;
		KnobWidth = knobWidth;
	}

	/// <summary>The value field's left edge, device pixels — the slider's <c>+0x00</c>.</summary>
	public int Left { get; }

	/// <summary>Its top edge.</summary>
	public int Top { get; }

	/// <summary>
	/// The slider's <c>+0x08</c>: the value field's last GAU column, shifted, so the start of that
	/// column's device pixels rather than the end of them.
	/// </summary>
	public int TrackRight { get; }

	/// <summary>The value field's bottom GAU row, shifted the same way.</summary>
	public int Bottom { get; }

	/// <summary>The knob's width, one GAU unit — <c>1 &lt;&lt; VideoMode_XCoordShift</c>.</summary>
	public int KnobWidth { get; }

	/// <summary>How far the knob's left edge can travel, in device pixels.</summary>
	public int Travel => TrackRight - KnobWidth - Left;

	/// <summary>
	/// Row <paramref name="gaugeSlot"/>'s charge bar, or null when this herc's <c>.GAU</c> has no such
	/// row. Whether the row holds an energy mount is the caller's question.
	/// </summary>
	public static ChargeBarSlider? For(CockpitArt art, int gaugeSlot) {
		ArgumentNullException.ThrowIfNull(art);
		if (art.Gau.Weapons is not { } rows
			|| gaugeSlot < 0 || gaugeSlot >= Math.Min(art.Gau.WeaponListTotal, rows.Length)) {
			return null;
		}

		var rect = rows[gaugeSlot];
		var slider = new ChargeBarSlider(
			(rect.Origin.X + ConsoleGaugePainter.ValueFieldLeft) * Scale,
			rect.Origin.Y * Scale,
			(rect.Origin.X + ConsoleGaugePainter.ValueFieldRight) * Scale,
			(rect.Origin.Y + ValueFieldBottom) * Scale,
			Scale);
		return slider.Travel > 0 ? slider : null;
	}

	/// <summary>The value field's bottom edge in GAU units below the row's top: <c>y0+5</c>.</summary>
	private const int ValueFieldBottom = 5;

	/// <summary>
	/// The bar as a clickable widget: the value field, inclusive of the last GAU unit on both axes as
	/// every <c>.GAU</c>-built widget here is, and draggable.
	/// </summary>
	public CockpitWidget Widget(int gaugeSlot) =>
		new(CockpitWidgetId.ChargeBar(gaugeSlot), CockpitSurface.Forward,
			Left, Top, TrackRight + Scale - 1, Bottom + Scale - 1, Lit: false, Draggable: true);

	/// <summary>
	/// The position a pointer at <paramref name="deviceX"/> commits: the drag handler's clamp, then the
	/// getter's arithmetic. A pointer past either end pins the slider there.
	/// </summary>
	public int PositionAt(float deviceX) {
		if (Travel <= 0) {
			return 0;
		}

		int scale = Travel * 65536 / WeaponMount.ChargeBarRange;
		int knobLeft = (int)Math.Clamp(MathF.Round(deviceX), Left, TrackRight - KnobWidth);
		return (int)(((long)(knobLeft - Left) << 16) / scale);
	}
}
