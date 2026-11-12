using System.Collections.Immutable;
using Herculan.Engine.Input;
using Silk.NET.Input;

namespace Herculan.Es1.Input;

/// <summary>
/// One frame of a Custom profile as the input build takes it (<see cref="Es1CustomControls.Read"/>).
/// </summary>
/// <param name="Steering">The steering function, in the port's calibrated ±128 (<see cref="IEs1GamePort"/>).</param>
/// <param name="Throttle">The throttle function, the same.</param>
/// <param name="Twist">The turret twist function, the same.</param>
/// <param name="Pitch">The turret pitch function, the same.</param>
/// <param name="Fire">Whether an input bound to <see cref="KeyAction.Fire"/> is held: source button 0, the trigger.</param>
/// <param name="Views">The input block's buttons 4–7, 1 held, which queue view commands 1, 0, 4 and 5.</param>
public sealed record Es1CustomFrame(int Steering, int Throttle, int Twist, int Pitch, bool Fire, byte[] Views);

/// <summary>
/// <b>This engine's own:</b> a Custom profile (<see cref="ControllerProfile"/>) as <i>Metaltech: Earthsiege</i> reads it, the
/// HERC's half (docs/herculan/joystick-config.md, "Setting up a stick"). In place of option 7's configuration
/// (<see cref="Es1StickConfigurations"/>) the input build takes each axis function straight onto its own axis of the input
/// block, Fire onto the trigger and the four views onto the block's buttons 4–7 (<see cref="Es1InputBuild.Custom"/>); every
/// other action goes in as the key ES1 gives it (<see cref="ChordOf"/>, <see cref="Es1CustomKeys"/>).
///
/// <para>An axis reads as the port gives a calibrated stick: the device's travel as ±128, inverted as the profile says,
/// 0 across the profile's deadzone and the rest of the travel spread over the whole range, as the calibration's center
/// band leaves it (docs/es1/simulation/joystick.md#normalising-an-axis).</para>
/// </summary>
public sealed class Es1CustomControls {
	private const int FullScale = 128;

	// The block's buttons 4–7 in order: view commands 1, 0, 4 and 5 (Es1Cockpit.PollViewDevice).
	private static readonly ImmutableArray<KeyAction> ViewButtons =
		[KeyAction.ViewUp, KeyAction.ViewDown, KeyAction.ViewRight, KeyAction.ViewLeft];

	/// <summary>The actions the host answers itself, which go in as no key.</summary>
	public static readonly ImmutableHashSet<KeyAction> HostActions =
		[KeyAction.MenuBar, KeyAction.PauseMenu, KeyAction.SquadOrdersWheel, KeyAction.ScreensWheel];

	private static readonly ImmutableHashSet<KeyAction> ThroughBlock = [KeyAction.Fire, .. ViewButtons];

	private readonly ControllerProfile _profile;

	public Es1CustomControls(ControllerProfile profile) {
		_profile = profile;
	}

	/// <summary>
	/// The key ES1 gives <paramref name="action"/>, its first where it has several, or null for an action ES1 sends no key
	/// for: one Earthsiege 2 alone has, and <see cref="KeyAction.MfdNextScreen"/>, which is the MFD's own step button.
	/// Most are Earthsiege 2's (docs/es1/simulation/main-loop.md#commands--sim_dispatchcommand-00059df8); these differ.
	/// </summary>
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
		_ => KeyMap.Retail.Chords(action) is { Length: > 0 } chords ? chords[0] : null,
	};

	/// <summary>The profile's axes, Fire and views on <paramref name="device"/> this frame.</summary>
	public Es1CustomFrame Read(ControllerState device) {
		var held = _profile.Held(device, razor: false);
		return new Es1CustomFrame(
			Axis(device, AxisFunction.Steer), Axis(device, AxisFunction.Throttle),
			Axis(device, AxisFunction.TurretTwist), Axis(device, AxisFunction.TurretPitch),
			held.Contains(KeyAction.Fire),
			[.. ViewButtons.Select(view => held.Contains(view) ? (byte)1 : (byte)0)]);
	}

	/// <summary>The actions held on <paramref name="device"/> that go in as a key (<see cref="ChordOf"/>).</summary>
	public IEnumerable<KeyAction> KeyActions(ControllerState device) =>
		_profile.Held(device, razor: false).Where(action => ChordOf(action) != null);

	/// <summary>The first input bound to <paramref name="action"/> held on <paramref name="device"/>, or null for none.</summary>
	public ControllerInput? HeldInput(ControllerState device, KeyAction action) => _profile.HeldInput(device, action, razor: false);

	private int Axis(ControllerState device, AxisFunction function) {
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
