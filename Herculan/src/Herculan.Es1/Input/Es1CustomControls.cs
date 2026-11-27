using System.Collections.Immutable;
using Herculan.Engine.Input;
using Silk.NET.Input;

namespace Herculan.Es1.Input;

/// <summary>
/// One frame of a Custom profile as the input read takes it (<see cref="Es1CustomControls.Read"/>).
/// </summary>
/// <param name="Steering">The steering function, ±256 under the squared response, as a stick's record gives it.</param>
/// <param name="Throttle">The throttle function, the same.</param>
/// <param name="Twist">The turret twist function, the same.</param>
/// <param name="Pitch">The turret pitch function, the same.</param>
/// <param name="Fire">Whether an input bound to <see cref="KeyAction.Fire"/> is held: source button 0, the trigger.</param>
/// <param name="Views">The input block's buttons 4–7, 1 held, which queue view commands 1, 0, 4 and 5.</param>
/// <param name="LeverTarget">
/// Where a throttle read as a lever puts the HERC's throttle (<see cref="Es1CustomControls.LeverTarget"/>), with
/// <paramref name="Throttle"/> then 0; null for a throttle read as a rate.
/// </param>
/// <param name="BipolarLever">Whether the lever is idle in the middle, its sign its direction.</param>
public sealed record Es1CustomFrame(int Steering, int Throttle, int Twist, int Pitch, bool Fire, byte[] Views,
	int? LeverTarget = null, bool BipolarLever = false);

/// <summary>
/// <b>This engine's own:</b> a Custom profile (<see cref="ControllerProfile"/>) as <i>Metaltech: Earthsiege</i> reads it, the
/// HERC's half (docs/herculan/joystick-config.md, "Setting up a stick"). In place of option 7's configuration
/// (<see cref="Es1StickConfigurations"/>) the input read takes each axis function straight onto its own axis of the input
/// block, Fire onto the trigger and the four views onto the block's buttons 4–7 (<see cref="Es1PlayerInputReader.Custom"/>); every
/// other action goes in as the key ES1 gives it (<see cref="ChordOf"/>, <see cref="Es1CustomKeys"/>).
///
/// <para>An axis reads as a calibrated stick's record gives it: the device's travel as ±128, inverted as the profile says,
/// 0 across the profile's deadzone and the rest of the travel spread over the whole range, as the calibration's center
/// band leaves it (docs/es1/simulation/joystick.md#normalising-an-axis), then squared to ±256 as every retail device's
/// response is (docs/es1/simulation/joystick.md#the-record), with the profile's deadzone as its only band.</para>
///
/// <para>A throttle the profile reads as a lever (<see cref="ControllerProfile.ThrottleReading"/>) gives a throttle
/// position instead (<see cref="LeverTarget"/>), which the input read turns into the rate that takes the throttle
/// there: ES1's throttle reads every axis as a rate
/// (docs/es1/simulation/mech-locomotion.md#the-throttle--mech_applythrottleinput-00011afc), so the lever is carried
/// over from Earthsiege 2's (docs/retail/simulation/mech-locomotion.md#control-law-mech_locomotiontick).</para>
/// </summary>
public sealed class Es1CustomControls {
	private const int FullScale = 128;

	// The squared response's shift, the calibration shift 7 less 1.
	private const int ResponseShift = 6;

	// Full throttle, Es1Locomotion.ThrottleFull, and the setting under which Earthsiege 2's lever reads idle.
	private const int ThrottleFull = 0x400;
	private const int LeverDeadband = 100;

	// The block's buttons 4–7 in order: view commands 1, 0, 4 and 5 (Es1Cockpit.PollViewDevice).
	private static readonly ImmutableArray<KeyAction> ViewButtons =
		[KeyAction.ViewUp, KeyAction.ViewDown, KeyAction.ViewRight, KeyAction.ViewLeft];

	/// <summary>The actions the host answers itself, which go in as no key.</summary>
	public static readonly ImmutableHashSet<KeyAction> HostActions =
		[KeyAction.MenuBar, KeyAction.PauseMenu, KeyAction.SquadOrdersWheel, KeyAction.ScreensWheel];

	private static readonly ImmutableHashSet<KeyAction> ThroughBlock = [KeyAction.Fire, .. ViewButtons];

	/// <summary>The MFD's six screens' actions in mode order, each pressing its button through its function key.</summary>
	public static readonly ImmutableArray<KeyAction> MfdScreens = [
		KeyAction.MfdStatus, KeyAction.MfdFlashComm, KeyAction.MfdNavMap, KeyAction.MfdScanner, KeyAction.MfdTarget,
		KeyAction.MfdMissileCam,
	];

	private readonly ControllerProfile _profile;

	public Es1CustomControls(ControllerProfile profile) {
		_profile = profile;
	}

	/// <summary>
	/// The key ES1 gives the action, its first where it has several, or null for an action ES1 sends no key
	/// for: one Earthsiege 2 alone has, and <see cref="KeyAction.MfdNextScreen"/>, the MFD's title button, which has no
	/// key and goes in as the next screen's (<see cref="NextScreenChord"/>). Most are Earthsiege 2's
	/// (docs/es1/simulation/main-loop.md#commands--sim_dispatchcommand-00059df8); these differ.
	/// </summary>
	/// <param name="action">The action.</param>
	public static KeyChord? ChordOf(KeyAction action) => action switch {
		_ when ControllerActions.Es2Only.Contains(action) => null,
		KeyAction.MfdNextScreen => null,
		KeyAction.NextTarget => new KeyChord(Key.Tab),
		KeyAction.AutoTurretTracking => KeyChord.WithAlt(Key.T),
		KeyAction.ReverseThrottleLever => KeyChord.WithCtrl(Key.Number1),
		KeyAction.ThrottleStepUp => KeyChord.WithCtrl(Key.I),
		KeyAction.ThrottleStepDown => KeyChord.WithCtrl(Key.M),
		KeyAction.PreviousChain => KeyChord.WithAlt(Key.GraveAccent),
		_ when HostActions.Contains(action) || ThroughBlock.Contains(action) => null,
		_ => KeyMap.Default.Chords(action) is { Length: > 0 } chords ? chords[0] : null,
	};

	/// <summary>
	/// <b>This engine's own:</b> the key <see cref="KeyAction.MfdNextScreen"/> goes in as, the function key of the MFD's
	/// next screen, 5 wrapping to 0 and −1, before the first screen is set, going to 0. Pressing that screen's button sets
	/// the mode that clicking the title sets through <c>MfdDisplay_CycleMode</c>
	/// (docs/es1/simulation/mfd-display.md#clicks-and-keys), and a recording takes the key.
	/// </summary>
	/// <param name="mode">The MFD's mode.</param>
	public static KeyChord NextScreenChord(int mode) => ChordOf(MfdScreens[(mode + 1) % MfdScreens.Length])!.Value;

	/// <summary>The profile's axes, Fire and views on the device this frame.</summary>
	/// <param name="device">The controller's state.</param>
	public Es1CustomFrame Read(ControllerState device) {
		var held = _profile.Held(device, razor: false);
		int? lever = LeverTarget(device);
		return new Es1CustomFrame(
			Axis(device, AxisFunction.Steer), lever == null ? Axis(device, AxisFunction.Throttle) : 0,
			Axis(device, AxisFunction.TurretTwist), Axis(device, AxisFunction.TurretPitch),
			held.Contains(KeyAction.Fire),
			[.. ViewButtons.Select(view => held.Contains(view) ? (byte)1 : (byte)0)],
			lever, _profile.ThrottleReading == ThrottleReading.Bipolar);
	}

	/// <summary>
	/// Where the throttle's axis puts the HERC's throttle when the profile reads it as a lever, before
	/// <c>ThrottleLeverMode</c> gives it a direction; null for a rate, or with no axis bound. An end-to-end lever is idle
	/// at its + end, where a stick is pulled back, and full at its − end, as Earthsiege 2's lever is, the profile's
	/// deadzone lying at the idle end over the same share of the travel; a bipolar lever is idle in the middle, forward of
	/// it forward and aft of it reverse, each half the whole range. Either reads idle under Earthsiege 2's lever's
	/// deadband.
	/// </summary>
	public int? LeverTarget(ControllerState device) {
		var axis = _profile.Axis(AxisFunction.Throttle);
		if (_profile.ThrottleReading == ThrottleReading.Rate || !axis.IsBound) {
			return null;
		}

		int setting;
		if (_profile.ThrottleReading == ThrottleReading.Bipolar) {
			setting = -Calibrated(device, AxisFunction.Throttle) * ThrottleFull / FullScale;
		} else {
			int raw = (int)MathF.Round(Math.Clamp(device.Axis(axis.Index), -1f, 1f) * FullScale);
			int deadzone = 2 * Math.Clamp(axis.Deadzone, 0, FullScale - 1);
			int beyond = FullScale - (axis.Invert ? -raw : raw) - deadzone;
			setting = beyond <= 0 ? 0 : beyond * ThrottleFull / (2 * FullScale - deadzone);
		}

		return Math.Abs(setting) < LeverDeadband ? 0 : setting;
	}

	/// <summary>The actions held on the device that go in as a key (<see cref="ChordOf"/>, <see cref="NextScreenChord"/>).</summary>
	/// <param name="device">The controller's state.</param>
	public IEnumerable<KeyAction> KeyActions(ControllerState device) =>
		_profile.Held(device, razor: false).Where(action => ChordOf(action) != null || action == KeyAction.MfdNextScreen);

	/// <summary>The first input bound to the action held on the device, or null for none.</summary>
	/// <param name="device">The controller's state.</param>
	/// <param name="action">The action.</param>
	public ControllerInput? HeldInput(ControllerState device, KeyAction action) => _profile.HeldInput(device, action, razor: false);

	// A function's axis under the squared response every retail device is built with, mode 1 of Joystick_ReadWithResponse
	// (docs/es1/simulation/joystick.md#the-record): the square of the calibrated value over 64, its sign the value's, so
	// full deflection is ±256 as the keyboard's whole entry is. The profile's deadzone is the only band: retail's band of
	// 25 and its rescale are what Calibrated's deadzone and spread already do at the default 25, and retail's zeroing of
	// a squared output under 25 is left out.
	private int Axis(ControllerState device, AxisFunction function) {
		int value = Calibrated(device, function);
		int squared = (value * value) >> ResponseShift;
		return value < 0 ? -squared : squared;
	}

	// A function's axis as the port gives a calibrated stick, before the response: ±128, 0 across the deadzone.
	private int Calibrated(ControllerState device, AxisFunction function) {
		var axis = _profile.Axis(function);
		if (!axis.IsBound) {
			return 0;
		}

		int raw = (int)MathF.Round(Math.Clamp(device.Axis(axis.Index), -1f, 1f) * FullScale);
		if (axis.Invert) {
			raw = -raw;
		}

		int deadzone = Math.Clamp(axis.Deadzone, 0, FullScale - 1);
		int beyond = Math.Abs(raw) - deadzone;
		return beyond <= 0 ? 0 : Math.Sign(raw) * beyond * FullScale / (FullScale - deadzone);
	}
}
