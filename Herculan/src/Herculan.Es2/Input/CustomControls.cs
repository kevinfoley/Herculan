using System.Collections.Immutable;
using HercWorks.Core.Data.File.Cfg;
using Herculan.Engine.Content;
using Herculan.Engine.Input;

namespace Herculan.Es2.Input;

/// <summary>
/// A Custom profile (<see cref="ControllerProfile"/>) as Earthsiege 2 reads it, for the machine the mission pilots.
/// <b>This engine's own</b> (docs/herculan/plan-controller-profiles.md, step 2.4). The profile reaches the retail input
/// build through fixed CONTROLS settings that stand in for <c>prefs.cfg</c>'s, so the control law, the press-once
/// dispatch, the views and a recording all take it as they take a stick:
///
/// <list type="bullet">
/// <item>Each axis function goes into the <see cref="JoystickReading"/> slot that the fixed rows route to that game
/// axis, at its own invert and deadzone, and the rows are chosen by how the throttle is read: a lever has to be in the
/// throttle slot under THROTTLE for <c>Input_SetThrottleLeverMode</c> (<c>00459d20</c>) and <c>FlightModel_Step</c>
/// (<c>00466a54</c>) to read it as one. Where retail negates a slot on its way to a RAZOR's axis, the slot is negated
/// first, so a function reads its axis as the profile says.</item>
/// <item>BUTTON 1 is FIRE, held by <see cref="KeyAction.Fire"/>'s inputs, so a modal panel's trigger answers it too;
/// BUTTON 3 is MFD DISPLAYS for <see cref="KeyAction.MfdNextScreen"/> and BUTTON 4 CHANGE DIRECTION for
/// <see cref="KeyAction.ReverseThrottleLever"/>. BUTTON 2 is OFF, being the second button an alert panel answers.</item>
/// <item>The HAT row is VIEWS, and the hat's four directions are held by <see cref="KeyAction.ViewUp"/>,
/// <see cref="KeyAction.ViewDown"/>, <see cref="KeyAction.ViewLeft"/> and <see cref="KeyAction.ViewRight"/>.</item>
/// </list>
///
/// The host sends every other action as its key (<see cref="HeldActions"/>). The CONTROLS panel shows these rows and
/// takes no click on any of them.
/// </summary>
public sealed class CustomControls : IControlsOverride {
	private const byte Movement = (byte)JoystickAxisAssignment.Movement;
	private const byte Turret = (byte)JoystickAxisAssignment.Turret;

	// The button rows' actions, by the profile action holding each; BUTTON 2 is held by none.
	private static readonly ImmutableArray<(KeyAction? Action, JoystickAction Code)> ButtonRows = [
		(KeyAction.Fire, JoystickAction.Fire),
		(null, JoystickAction.Off),
		(KeyAction.MfdNextScreen, JoystickAction.MfdDisplays),
		(KeyAction.ReverseThrottleLever, JoystickAction.ChangeDirection),
	];

	private static readonly ImmutableArray<(KeyAction Action, JoystickHat Direction)> Views = [
		(KeyAction.ViewUp, JoystickHat.North), (KeyAction.ViewDown, JoystickHat.South),
		(KeyAction.ViewLeft, JoystickHat.West), (KeyAction.ViewRight, JoystickHat.East),
	];

	private static readonly ImmutableHashSet<KeyAction> ThroughReading =
		[.. ButtonRows.Where(row => row.Action != null).Select(row => row.Action!.Value), .. Views.Select(view => view.Action)];

	private readonly ControllerProfile _profile;
	private readonly bool _razor;
	private readonly byte[] _options = new byte[2 * Prefs.ControlsOptionCount];

	/// <param name="profile">The device's profile.</param>
	/// <param name="razor">Whether the mission pilots a RAZOR, whose half of the profile is read.</param>
	public CustomControls(ControllerProfile profile, bool razor) {
		_profile = profile;
		_razor = razor;
		WriteBlock(Prefs.HercControlsBase, Layout(razor: false).Rows);
		WriteBlock(Prefs.RazorControlsBase, Layout(razor: true).Rows);
	}

	/// <summary>Whether the HERC's throttle lever is read centre-zero, which the bindings apply (<see cref="JoystickBindings.BipolarThrottle"/>).</summary>
	public bool BipolarThrottle => !_razor && _profile.ThrottleReading == ThrottleReading.Bipolar;

	/// <summary>
	/// What the CONTROLS panel and the bindings read the device as: the four button rows above, the hat, and the throttle
	/// and rudder slots where a function is bound to them.
	/// </summary>
	public JoystickCapabilities Capabilities {
		get {
			var functions = Layout(_razor).Functions;
			return new JoystickCapabilities(true, ButtonRows.Length,
				HasThrottle: _profile.Axis(functions[2]).IsBound,
				HasRudder: _profile.Axis(functions[3]).IsBound,
				HasHat: true);
		}
	}

	/// <summary>One frame of <paramref name="device"/> in the shape the retail bindings read, as the summary describes.</summary>
	public JoystickReading Read(ControllerState device) {
		var (functions, flipped, _) = Layout(_razor);
		int Slot(int slot) {
			var axis = _profile.Axis(functions[slot]);
			if (!axis.IsBound) {
				return 0;
			}

			int raw = (int)MathF.Round(Math.Clamp(device.Axis(axis.Index), -1f, 1f) * JoystickReading.RawFull);
			return JoystickReading.ApplyResponse(axis.Invert != flipped[slot] ? -raw : raw, axis.Deadzone);
		}

		var held = Held(device);
		byte buttons = 0;
		for (int row = 0; row < ButtonRows.Length; row++) {
			if (ButtonRows[row].Action is { } action && held.Contains(action)) {
				buttons |= (byte)(1 << row);
			}
		}

		var hat = JoystickHat.None;
		foreach (var (action, direction) in Views) {
			if (held.Contains(action)) {
				hat |= direction;
			}
		}

		return new JoystickReading(Slot(0), Slot(1), Slot(2), Slot(3), hat, buttons);
	}

	/// <summary>The actions held on <paramref name="device"/> that <see cref="Read"/> does not carry, for the host to send.</summary>
	public IEnumerable<KeyAction> HeldActions(ControllerState device) => Held(device).Except(ThroughReading);

	/// <summary>Whether an input bound to <paramref name="action"/> is held on <paramref name="device"/>.</summary>
	public bool IsHeld(ControllerState device, KeyAction action) => HeldInput(device, action) != null;

	/// <summary>The first input bound to <paramref name="action"/> that is held on <paramref name="device"/>, or null for none.</summary>
	public ControllerInput? HeldInput(ControllerState device, KeyAction action) => _profile.HeldInput(device, action, _razor);

	/// <inheritdoc/>
	public bool AxisRowsFixed => true;

	/// <inheritdoc/>
	public bool ButtonRowsFixed => true;

	/// <inheritdoc/>
	public bool Covers(int index) => index >= Prefs.HercControlsBase && index < Prefs.HercControlsBase + _options.Length;

	/// <inheritdoc/>
	public byte this[int index] {
		get => Covers(index) ? _options[index - Prefs.HercControlsBase] : (byte)0;
		set { }
	}

	/// <inheritdoc/>
	public byte Recommended(int index) => this[index];

	/// <inheritdoc/>
	public void Save() { }

	private HashSet<KeyAction> Held(ControllerState device) => _profile.Held(device, _razor);

	private void WriteBlock(int start, byte[] rows) {
		int at = start - Prefs.HercControlsBase;
		rows.CopyTo(_options, at);
		for (int row = 0; row < ButtonRows.Length; row++) {
			_options[at + Prefs.ControlsAxisCount + row] = (byte)ButtonRows[row].Code;
		}
	}

	/// <summary>
	/// The function in each slot (stick X, stick Y, throttle, rudder), whether retail negates that slot on its way to the
	/// machine's axis, and the four axis rows that route them, for one machine as its throttle is read. A HERC's game axes
	/// are steering, throttle, turret twist and turret pitch; a RAZOR's roll, pitch, yaw and throttle. A RAZOR negates the
	/// throttle slot on any row and the stick's Y on the turret pair (<c>JoystickBindings</c>'s source pass).
	/// </summary>
	private (AxisFunction[] Functions, bool[] Flipped, byte[] Rows) Layout(bool razor) => (razor, razor
			? _profile.RazorThrottleReading != ThrottleReading.Rate
			: _profile.ThrottleReading != ThrottleReading.Rate) switch {
		// JOYSTICK on movement, THROTTLE and RUDDER on the turret: the throttle slot is turret pitch.
		(false, false) => ([AxisFunction.Steer, AxisFunction.Throttle, AxisFunction.TurretPitch, AxisFunction.TurretTwist],
			[false, false, false, false], [Movement, Turret, Turret, Movement]),
		// JOYSTICK on the turret, THROTTLE and RUDDER on movement: the lever in the throttle slot.
		(false, true) => ([AxisFunction.TurretTwist, AxisFunction.TurretPitch, AxisFunction.Throttle, AxisFunction.Steer],
			[false, false, false, false], [Turret, Movement, Movement, Movement]),
		// The stick on yaw and throttle, the throttle slot on pitch and the rudder slot on roll, as a gamepad's RAZOR rows.
		(true, false) => ([AxisFunction.RazorYaw, AxisFunction.RazorThrottle, AxisFunction.RazorPitch, AxisFunction.RazorRoll],
			[false, true, true, false], [Turret, Movement, Movement, Movement]),
		// THROTTLE on the RAZOR's THROTTLE word, the lever's own test; the stick on roll and pitch, the rudder slot on yaw.
		(true, true) => ([AxisFunction.RazorRoll, AxisFunction.RazorPitch, AxisFunction.RazorThrottle, AxisFunction.RazorYaw],
			[false, false, true, false], [Movement, Turret, Turret, Movement]),
	};
}
