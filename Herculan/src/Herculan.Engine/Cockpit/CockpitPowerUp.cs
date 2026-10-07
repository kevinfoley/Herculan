using Herculan.Engine.Sim;

namespace Herculan.Engine.Cockpit;

/// <summary>
/// The weapon panel's, shield meter's and MFD's power-up animations: on taking a walking machine the
/// weapon rows wink on one at a time, each energy row's charge bar fills from empty, the shield rings
/// light from dark up to the real charge, and the scanner's dish grows out from a small ring. <c>Cockpit_PowerUpTick</c> (<c>00432924</c>) arms each
/// widget with <c>Widget_BeginPowerUpAnimation</c> (<c>00438ddc</c>) and each widget runs its own
/// ramp from the tick it was armed on. The delays, the ramps and their sources are
/// docs/retail/formats/cockpit-hud-widgets.md's power-up section.
///
/// <para>The compass's wind-up is the same sequence's other animation, and is
/// <see cref="HeadingTapeSweep"/>. A flyer's cockpit skips all of them.</para>
/// </summary>
public sealed class CockpitPowerUp {
	/// <summary>
	/// How many weapon rows the sequence arms — the ten widget slots at <c>cockpit+0x70</c>, indexed by
	/// <c>.GAU</c> weapon row.
	/// </summary>
	public const int RowCount = 10;

	/// <summary>
	/// The gap between one row winking on and the next, in coarse ticks. <c>PowerUp_RowArmDelays</c> (<c>0049b05a</c>) is ten
	/// shorts reading 20, 40, … 200, so row <c>n</c> (0-based) arms once more than <c>20 * (n + 1)</c>
	/// ticks have passed: 320 ms apart, the last at 3.2 s.
	/// </summary>
	public const int RowArmStep = 20;

	/// <summary>How far a charge bar's ramp climbs per coarse tick — the <c>0x19</c> of <c>EnergyWeaponGauge_PowerUpFill</c> (<c>00440e84</c>) and <c>TurboPodGauge_PowerUpFill</c> (<c>00441d88</c>).</summary>
	public const int ChargeRampPerTick = 0x19;

	/// <summary>How many coarse ticks after arming a charge bar stops ramping and reads its live value.</summary>
	public const int ChargeRampTicks = 0x33;

	/// <summary>
	/// The <c>RADAR</c> bank, whose ten frames are the dish powering up and nothing else:
	/// <c>MfdDisplay_Ctor</c> loads it for this animation and <c>MfdDisplay_Update</c> frees it the
	/// moment the display is done.
	/// </summary>
	public const string MfdBank = "RADAR";

	/// <summary>How many frames the dish's animation steps through, the bank's whole ten.</summary>
	public const int MfdFrameCount = 10;

	/// <summary>How long each frame is held, in coarse ticks — the <c>7</c> every entry of the constructor's frame table states.</summary>
	public const int MfdFrameTicks = 7;

	/// <summary>
	/// How long after the animation started the display counts as powered up if it is showing any
	/// other screen — the <c>+0x46</c> <c>MfdDisplay_Update</c> stamps at <c>+0x345</c> when it starts
	/// the sequence.
	/// </summary>
	public const int MfdOtherScreenTicks = 0x46;

	private readonly long _poweredAt;
	private readonly long?[] _rowArmedAt = new long?[RowCount];
	private long? _shieldsArmedAt;
	private bool _finished;
	private bool _shieldsDone;
	private long? _mfdArmedAt;
	private long? _mfdAnimationStart;
	private bool _mfdEnded;
	private bool _mfdDone;

	private CockpitPowerUp(long poweredAt, bool finished) {
		_poweredAt = poweredAt;
		_finished = finished;
		_shieldsDone = finished;
		_mfdDone = finished;
	}

	/// <summary>
	/// The sequence for a cockpit powering up at <paramref name="coarseTicks"/>. A flyer's comes back
	/// finished, every widget armed and done — <c>Gau_BuildCockpitWidgets</c>' own branch on
	/// <c>FlyerFlag</c>, the same one <see cref="HeadingTapeSweep.ForPowerUp"/> follows.
	/// </summary>
	public static CockpitPowerUp ForPowerUp(MechObject pilot, long coarseTicks) {
		ArgumentNullException.ThrowIfNull(pilot);
		return new CockpitPowerUp(coarseTicks, finished: pilot.Type.IsFlyer);
	}

	/// <summary>
	/// A sequence that has already run: every row showing and every value live. For a host that wants
	/// the steady-state cockpit from the first frame.
	/// </summary>
	public static CockpitPowerUp Finished { get; } = new(0, finished: true);

	/// <summary>
	/// <c>Cockpit_PowerUpTick</c>'s arming pass, once a frame: each row whose delay has passed, and the
	/// shield meter and the MFD on the first tick after the power-up began.
	/// </summary>
	public void Tick(long coarseTicks) {
		if (_finished) {
			return;
		}

		long elapsed = coarseTicks - _poweredAt;
		bool rowsDone = true;
		for (int row = 0; row < RowCount; row++) {
			if (_rowArmedAt[row] == null && elapsed > RowArmStep * (row + 1)) {
				_rowArmedAt[row] = coarseTicks;
			}

			rowsDone &= _rowArmedAt[row] is { } armedAt && coarseTicks - armedAt >= ChargeRampTicks;
		}

		if (_shieldsArmedAt == null && elapsed != 0) {
			_shieldsArmedAt = coarseTicks;
		}

		if (_mfdArmedAt == null && elapsed != 0) {
			_mfdArmedAt = coarseTicks;
		}

		_finished = rowsDone && _shieldsDone && _mfdDone;
	}

	/// <summary>
	/// Whether a weapon row is drawn yet. Every weapon and pod gauge's paint, and each of its children's,
	/// opens on the owning gauge's armed byte at <c>+0x8c</c>, so a row the sequence has not reached
	/// draws nothing at all and the console art shows where it will be.
	/// </summary>
	public bool RowPowered(int row) =>
		_finished || row < 0 || row >= RowCount || _rowArmedAt[row] != null;

	/// <summary>
	/// The most a row's charge bar can show this tick, in the bar's own units, or
	/// <see cref="int.MaxValue"/> once the ramp is over. <c>EnergyWeaponGauge_PowerUpFill</c> (<c>00440e84</c>, energy) and
	/// <c>TurboPodGauge_PowerUpFill</c> (<c>00441d88</c>, Turbo Pod) both clamp the live value to <c>elapsed * 0x19</c> until
	/// <see cref="ChargeRampTicks"/> have passed since the row was armed, then hand it over whole — which
	/// for the Turbo Pod, whose bar runs to 2500, is a visible jump from 1250 to a full tank.
	/// </summary>
	public int RowChargeCap(int row, long coarseTicks) {
		if (_finished || row < 0 || row >= RowCount || _rowArmedAt[row] is not { } armedAt) {
			return int.MaxValue;
		}

		long elapsed = coarseTicks - armedAt;
		return elapsed >= ChargeRampTicks ? int.MaxValue : (int)(elapsed * ChargeRampPerTick);
	}

	/// <summary>
	/// What the shield meter shows for a pair of live facing charges, on the rings' 0..<c>0x800</c>
	/// scale. Before the meter is armed <c>ShieldsGauge_SetStateBlock</c> (<c>00443858</c>) zeroes both,
	/// so the rings are dark; once armed, <c>ShieldsGauge_PowerUpFill</c> (<c>004437a4</c>) shows each facing as the lesser of its live
	/// value and the coarse ticks since arming, and latches done the first tick both have caught up. An
	/// even split's <c>0x200</c> is therefore about eight seconds of fill. Call it once a frame — it
	/// latches as it goes.
	/// </summary>
	public (int Front, int Rear) ShieldCharges(int front, int rear, long coarseTicks) {
		if (_shieldsDone) {
			return (front, rear);
		}

		if (_shieldsArmedAt is not { } armedAt) {
			return (0, 0);
		}

		long ramp = coarseTicks - armedAt;
		int shownFront = (int)Math.Min(ramp, front);
		int shownRear = (int)Math.Min(ramp, rear);
		if (shownFront == front && shownRear == rear) {
			_shieldsDone = true;
		}

		return (shownFront, shownRear);
	}

	/// <summary>
	/// Whether <c>MfdDisplay_Update</c> gets as far as the sensor dropout this frame: not before the
	/// display is armed, and not while the dish is still growing on the scanner, both of which return
	/// first. Ask it after <see cref="MfdFrame"/>, which latches the display done.
	/// </summary>
	public bool MfdReachesDropout(bool scannerShowing) =>
		_mfdDone || (_mfdArmedAt != null && !scannerShowing);

	/// <summary>
	/// Which <see cref="MfdBank"/> frame the MFD shows at the scanner dish's position in place of the
	/// scanner screen, or null once the display has powered up. Call it once a frame — like the
	/// original's update, it starts and latches as it goes.
	///
	/// <para>Before the display is armed its repaint puts frame 0 over the dish. Once armed on the
	/// scanner, <c>MfdDisplay_Update</c> (<c>00446328</c>) starts the sequence and steps it on the coarse
	/// clock, frame <c>k</c> once more than <c>7k</c> ticks have passed, and returns before the screen's
	/// own update — so neither the plot nor a squadmate's transmission is drawn while it runs. Frame 9
	/// ends it and the next update marks the display done. On any other screen the display is done once
	/// <see cref="MfdOtherScreenTicks"/> have passed since the sequence started, or at once if it never
	/// started.</para>
	/// </summary>
	/// <param name="scannerShowing">Whether the MFD is on the scanner, mode 3, this frame.</param>
	/// <param name="coarseTicks">The current coarse tick.</param>
	public int? MfdFrame(bool scannerShowing, long coarseTicks) {
		if (_mfdDone) {
			return null;
		}

		if (_mfdArmedAt == null) {
			return scannerShowing ? 0 : null;
		}

		if (!scannerShowing) {
			if (_mfdAnimationStart is not { } started || coarseTicks > started + MfdOtherScreenTicks) {
				_mfdDone = true;
			}

			return null;
		}

		// The update after the one that showed the last frame finds the sequence stopped and marks the
		// display done; the scanner screen paints from then on.
		if (_mfdEnded) {
			_mfdDone = true;
			return null;
		}

		if (_mfdAnimationStart is not { } start) {
			_mfdAnimationStart = coarseTicks;
			return 0;
		}

		// SpriteSequence_Step (00471d7c) advances past every frame whose hold has run out, so the frame showing is the
		// first whose end is still at or after now.
		long elapsed = coarseTicks - start;
		int frame = elapsed <= MfdFrameTicks ? 0 : (int)Math.Min((elapsed - 1) / MfdFrameTicks, MfdFrameCount - 1);
		if (frame == MfdFrameCount - 1) {
			_mfdEnded = true;
		}

		return frame;
	}
}
