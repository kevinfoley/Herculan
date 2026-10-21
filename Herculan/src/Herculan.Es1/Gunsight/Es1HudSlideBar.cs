using Herculan.Es1.View;

namespace Herculan.Es1.Gunsight;

/// <summary>
/// A child of the gunsight, <c>GunsightChild_CtorBase</c> (<c>ES1DBSIM 000337fc</c>)'s rect holder with its table at
/// <c>+0x10</c>: slot <c>+4</c> its paint and slot <c>+8</c> its repaint, which calls the paint in every class
/// (docs/es1/simulation/cockpit-gunsight-hud.md#building-the-gunsight).
/// </summary>
public abstract class Es1GunsightChild {
	/// <summary><c>GunsightChild_CtorBase</c>(child, <paramref name="rect"/>): the rect copied to <c>+0</c>.</summary>
	protected Es1GunsightChild(Es1Cockpit cockpit, Es1Rect rect) {
		Cockpit = cockpit;
		Rect = rect;
	}

	/// <summary><c>+0</c>–<c>+0xc</c>, in the cockpit's canvas.</summary>
	public Es1Rect Rect { get; }

	/// <summary><c>CockpitViewInstance</c>, which the children draw through and read the machine from.</summary>
	protected Es1Cockpit Cockpit { get; }

	/// <summary>Slot <c>+4</c>, the paint.</summary>
	public abstract void Paint();

	/// <summary>Slot <c>+8</c>, the repaint: the paint.</summary>
	public void Repaint() => Paint();
}

/// <summary>
/// The gunsight's sliding bars, ES2's rotation gauges on <c>RotationGaugeVtable</c> (<c>000936de</c>)
/// (docs/es1/simulation/cockpit-gunsight-hud.md#the-slide-bars): the limits <c>+0x24</c>/<c>+0x26</c>, the travel
/// <c>+0x28</c>, the range <c>+0x2c</c>, the track's point <c>+0x2e</c>/<c>+0x32</c> and the value <c>+0x3a</c>, with
/// the table's slots from <c>+0xc</c>: the recompute, the add-delta and the set-limits.
/// </summary>
public abstract class Es1HudSlideBar : Es1GunsightChild {
	/// <summary>
	/// <c>HudSlideBar_CtorBase</c> (<c>0003385c</c>)(bar, <paramref name="rect"/>): the limits −<c>0x4800</c> and
	/// <c>0x4800</c>, the range their difference and the value their middle (<see cref="SetLimitsOnly"/>).
	/// </summary>
	protected Es1HudSlideBar(Es1Cockpit cockpit, Es1Rect rect) : base(cockpit, rect) {
		SetLimitsOnly(0x4800, -0x4800);
	}

	/// <summary><c>+0x24</c>, the lower limit.</summary>
	public short Min { get; private set; }

	/// <summary><c>+0x26</c>, the upper limit.</summary>
	public short Max { get; private set; }

	/// <summary><c>+0x28</c>, the travel: the rect's width or height, as the recompute sets it.</summary>
	public int Travel { get; protected set; }

	/// <summary><c>+0x2c</c>, the range, <see cref="Max"/> − <see cref="Min"/> as an <c>int16</c>.</summary>
	public short Range { get; private set; }

	/// <summary><c>+0x2e</c>/<c>+0x32</c>, where the track, frame 0 of the bar's bank, is blitted.</summary>
	public (int X, int Y) TrackPoint { get; protected set; }

	/// <summary><c>+0x3a</c>, the value the bar shows.</summary>
	public short Value { get; protected set; }

	/// <summary>Slot <c>+0xc</c>, the recompute of the travel and the track's point from the rect.</summary>
	public abstract void Recompute();

	/// <summary>
	/// Slot <c>+0x10</c>, <c>HudSlideBar_AddDelta</c> (<c>00033910</c>)(bar, <paramref name="delta"/>): the value less the
	/// delta, held to the limits, so the bar tracks an angle <c>Gunsight_SetValues</c> hands it old − new.
	/// </summary>
	public virtual void AddDelta(short delta) {
		short value = (short)(Value - delta);
		Value = value < Min ? Min : value > Max ? Max : value;
	}

	/// <summary>
	/// Slot <c>+0x14</c>, <c>HudSlideBar_SetLimits</c> (<c>000338cc</c>)(bar, <paramref name="max"/>, <paramref name="min"/>):
	/// the limits, range and value of <see cref="SetLimitsOnly"/>, then the recompute.
	/// </summary>
	public virtual void SetLimits(short max, short min) {
		SetLimitsOnly(max, min);
		Recompute();
	}

	/// <summary>The limits moved by <paramref name="delta"/> each, the range and value left: <c>HudWaypointIndicator_ShiftLimits</c>'s body.</summary>
	protected void ShiftLimits(short delta) {
		Min = (short)(Min + delta);
		Max = (short)(Max + delta);
	}

	/// <summary>
	/// The place along the travel that <paramref name="angle"/> maps to: ((<paramref name="angle"/> − <see cref="Min"/>)
	/// &amp; <c>0xffff</c>) × <see cref="Travel"/> / <see cref="Range"/>, unsigned, as the paints compute it.
	/// </summary>
	protected int Map(short angle) =>
		(int)((uint)(((ushort)angle - (ushort)Min) & 0xffff) * (uint)Travel / (ushort)Range);

	/// <summary>
	/// <see cref="Map"/> of the value from <paramref name="start"/>, plus 1 when the value is negative: the bar's place
	/// before its own offset (docs/es1/simulation/cockpit-gunsight-hud.md#the-slide-bars).
	/// </summary>
	protected int BarPosition(int start) => start + Map(Value) + (Value < 0 ? 1 : 0);

	/// <summary>The limits stored, the range their difference and the value their middle, <c>int16</c> sum shifted right as unsigned.</summary>
	private void SetLimitsOnly(short max, short min) {
		Min = min;
		Max = max;
		Range = (short)(Max - Min);
		Value = (short)((ushort)(short)(Min + Max) >> 1);
	}
}

/// <summary>
/// The horizontal bar, the rotation indicator: <c>HudRotationIndicator_Ctor</c> (<c>00033968</c>), a <c>0x3c</c>-byte
/// bar on <c>HFixRotationGaugeVtable</c> (<c>000936fe</c>) drawn from <see cref="Es1GunsightBanks.HSlid"/>
/// (docs/es1/simulation/cockpit-gunsight-hud.md#the-slide-bars).
/// </summary>
public class Es1HudRotationIndicator : Es1HudSlideBar {
	/// <summary>The value's band, strictly within which the bar shows frame 2.</summary>
	public const short CentreBand = 300;

	/// <summary>The constructor: the bar base and <c>HudRotationIndicator_Recompute</c> directly.</summary>
	public Es1HudRotationIndicator(Es1Cockpit cockpit, Es1GunsightBanks banks, Es1Rect rect) : base(cockpit, rect) {
		Banks = banks;
		RecomputeRotation();
	}

	/// <summary>The banks the class and its subclasses draw from.</summary>
	protected Es1GunsightBanks Banks { get; }

	/// <summary><c>HudRotationIndicator_Recompute</c> (<c>000339dc</c>).</summary>
	public override void Recompute() => RecomputeRotation();

	/// <summary>
	/// <c>HudRotationIndicator_Paint</c> (<c>00033a04</c>), in the canvas push: frame 0 at the track's point, then the
	/// bar at (<i>x0</i> + <i>t</i> − 15, <i>y0</i>), frame 2 while the value lies strictly within ±<see cref="CentreBand"/>
	/// and frame 1 otherwise.
	/// </summary>
	public override void Paint() {
		var canvas = Cockpit.PushCanvasContext();
		canvas.Blit(Es1GunsightBanks.Frame(Banks.HSlid, 0), TrackPoint.X, TrackPoint.Y);
		int frame = Value < CentreBand && Value > -CentreBand ? 2 : 1;
		canvas.Blit(Es1GunsightBanks.Frame(Banks.HSlid, frame), BarPosition(Rect.X0) - 15, Rect.Y0);
	}

	/// <summary>The travel the rect's width and the track's point (<i>x0</i>, <i>y0</i> − 1).</summary>
	private void RecomputeRotation() {
		Travel = Rect.X1 - Rect.X0;
		TrackPoint = (Rect.X0, Rect.Y0 - 1);
	}
}

/// <summary>
/// The vertical bar: <c>HudSlideBarV_Ctor</c> (<c>00033ac4</c>), a <c>0x3c</c>-byte bar on
/// <c>VFixRotationGaugeVtable</c> (<c>00093726</c>) drawn from <see cref="Es1GunsightBanks.VSlid"/>
/// (docs/es1/simulation/cockpit-gunsight-hud.md#the-slide-bars).
/// </summary>
public sealed class Es1HudSlideBarV : Es1HudSlideBar {
	/// <summary>The value's band, strictly within which the bar shows frame 2.</summary>
	public const short CentreBand = 400;

	private readonly Es1GunsightBanks _banks;

	/// <summary>The constructor: the bar base and <c>HudSlideBarV_Recompute</c> directly.</summary>
	public Es1HudSlideBarV(Es1Cockpit cockpit, Es1GunsightBanks banks, Es1Rect rect) : base(cockpit, rect) {
		_banks = banks;
		RecomputeVertical();
	}

	/// <summary><c>HudSlideBarV_Recompute</c> (<c>00033b38</c>).</summary>
	public override void Recompute() => RecomputeVertical();

	/// <summary>
	/// <c>HudSlideBarV_Paint</c> (<c>00033b60</c>), in the canvas push: frame 0 at the track's point, then the bar at
	/// (<i>x0</i>, <i>y0</i> + <i>t</i> − 7), frame 2 while the value lies strictly within ±<see cref="CentreBand"/> and
	/// frame 1 otherwise.
	/// </summary>
	public override void Paint() {
		var canvas = Cockpit.PushCanvasContext();
		canvas.Blit(Es1GunsightBanks.Frame(_banks.VSlid, 0), TrackPoint.X, TrackPoint.Y);
		int frame = Value < CentreBand && Value > -CentreBand ? 2 : 1;
		canvas.Blit(Es1GunsightBanks.Frame(_banks.VSlid, frame), Rect.X0, BarPosition(Rect.Y0) - 7);
	}

	/// <summary>The travel the rect's height and the track's point (<i>x0</i>, <i>y0</i>).</summary>
	private void RecomputeVertical() {
		Travel = Rect.Y1 - Rect.Y0;
		TrackPoint = (Rect.X0, Rect.Y0);
	}
}
