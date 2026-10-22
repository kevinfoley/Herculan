using HercWorks.Core.Data.File.Dyn;
using HercWorks.Core.Data.File.Gau;
using Herculan.Es1.View;

namespace Herculan.Es1.Throttle;

/// <summary>
/// The throttle gauge, the cockpit's instrument <c>+0x1ed</c>, as <c>ThrottleGauge_Ctor</c> (<c>ES1DBSIM 0003b0d4</c>)
/// builds it on the <c>.GAU</c> block at 1000 (docs/es1/simulation/cockpit-throttle-gauge.md#the-gauge--throttlegauge_ctor-0003b0d4):
/// the gauge base's rect and cockpit, its one child the slider <c>+0x14</c>, the speed fraction <c>+0x7a</c>, the
/// throttle <c>+0x7e</c> and the slider's orientation <c>+0x8a</c>, with the slots of <c>ThrottleGaugeVtable</c>
/// (<c>00094206</c>) the cockpit and the slider call, and the pair the exchange with the throttle reads and writes
/// (docs/es1/simulation/cockpit-throttle-gauge.md#the-exchange-with-the-throttle).
/// </summary>
public sealed class Es1ThrottleGauge {
	/// <summary>
	/// <c>ThrottleGauge_Ctor</c>(0, <paramref name="cockpit"/>, <paramref name="block"/>): <c>PanelGauge_Ctor</c>'s rect,
	/// the block's first 16 bytes; the track, the block's track plus its origin; with the block's <c>int32</c>
	/// <c>0x10</c> equal to 1 the vertical slider and <c>+0x8a</c> 0, otherwise the horizontal one and 1; the slider
	/// registered on the cockpit's root (<c>Widget_RegisterClickable</c>), so the root's hit test reaches it; one child;
	/// and the pair 0.
	/// </summary>
	/// <param name="lights">The frames of the <c>dba\</c> file the block's light name names (<see cref="Es1ThrottleSlider.LightName"/>); none when null.</param>
	/// <param name="pushButtons"><c>CockpitPButtonsBank</c> (<c>00092d68</c>), the frames of <c>dba\pbuttons</c>; none when null.</param>
	internal Es1ThrottleGauge(Es1Cockpit cockpit, Es1GauThrottleBlock block, IReadOnlyList<DynamixBitmap>? lights,
			IReadOnlyList<DynamixBitmap>? pushButtons) {
		Cockpit = cockpit;
		Rect = new Es1Rect(block.Origin.X, block.Origin.Y, block.Int2, block.Int3);
		var track = Es1Rect.From(block.Track).Offset(block.Origin.X, block.Origin.Y);
		Horizontal = block.Lights != 1;
		Slider = new Es1ThrottleSlider(this, block, track, lights, pushButtons, Horizontal);
		cockpit.Root.RegisterClickable(Slider);
	}

	/// <summary><c>+0x10</c>, the cockpit, through which the slider draws.</summary>
	public Es1Cockpit Cockpit { get; }

	/// <summary><c>+0</c>–<c>+0xc</c>, the gauge base's rect, the block's first 16 bytes; nothing of the gauge reads it.</summary>
	public Es1Rect Rect { get; }

	/// <summary><c>+0x14</c>, the slider, the gauge's one child (<c>+0x64</c> = 1).</summary>
	public Es1ThrottleSlider Slider { get; }

	/// <summary><c>+0x8a</c>: 0 for the vertical slider, 1 for the horizontal.</summary>
	public bool Horizontal { get; }

	/// <summary><c>+0x7a</c>, the speed fraction, Q10 of the top speed the HERC is going at.</summary>
	public int SpeedFraction { get; private set; }

	/// <summary><c>+0x7e</c>, the throttle the knob shows, ±<c>0x400</c>, up and right forward.</summary>
	public int Throttle { get; private set; }

	/// <summary><c>ThrottleGauge_GetValues</c> (<c>0003b250</c>)(gauge): the pair from <c>+0x7a</c>, {speed fraction, throttle}.</summary>
	public (int SpeedFraction, int Throttle) GetValues() => (SpeedFraction, Throttle);

	/// <summary>
	/// <c>ThrottleGauge_SetValues</c> (<c>0003b200</c>)(gauge, pair): when the throttle differs from <c>+0x7e</c>, the
	/// slider's set-value slot with it, which places the knob and runs the slider's click, so
	/// <see cref="OnChildValue"/> stores it; when the speed fraction differs from <c>+0x7a</c>, the slider's paint counter
	/// set to 2; then the pair stored.
	/// </summary>
	public void SetValues(int speedFraction, int throttle) {
		if (throttle != Throttle) {
			Slider.SetValue((short)throttle);
		}

		if (speedFraction != SpeedFraction) {
			Slider.PaintCounter = 2;
		}

		SpeedFraction = speedFraction;
		Throttle = throttle;
	}

	/// <summary>
	/// Slot <c>+4</c>, <c>ThrottleGauge_OnChildValue</c> (<c>0003b25c</c>)(gauge, <paramref name="value"/>,
	/// <paramref name="child"/>), which the slider's click calls: for the first child, the value at <c>+0x7e</c>,
	/// negated for the vertical slider, so up and right are forward; for another child, nothing.
	/// </summary>
	public void OnChildValue(short value, Es1Widget child) {
		if (ReferenceEquals(child, Slider)) {
			Throttle = Horizontal ? value : -value;
		}
	}

	/// <summary>
	/// Slot <c>+8</c>, <c>ThrottleGauge_Paint</c> (<c>0003b1d0</c>), which <c>CockpitView_InvalidateWidgets</c> runs on a
	/// view change: the slider's paint.
	/// </summary>
	public void Paint() => Slider.Paint();

	/// <summary>
	/// Slot <c>+0xc</c>, <c>ThrottleGauge_Update</c> (<c>0003b1e8</c>), which <c>Cockpit_DrawFrame</c> runs once a
	/// frame: the slider's per-frame slot.
	/// </summary>
	public void Update() => Slider.Tick();

	/// <summary>Slot <c>+0x10</c>, <c>PanelGauge_KeyDispatchNoneThrottleGaugeCopy</c> (<c>0003b1c5</c>)(gauge, code): 0, taking no key.</summary>
	public static short KeyDispatch(ushort code) {
		_ = code;
		return 0;
	}
}
