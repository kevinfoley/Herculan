using Herculan.Engine.Content;
using Herculan.Engine.Input;
using HercWorks.Core.Data.File.Cfg;
using Xunit;

namespace Herculan.Engine.Tests;

/// <summary>
/// <see cref="JoystickBindings.InvertRazorPitch"/>: the device's share of the RAZOR's elevator, game axis 1, reversed
/// in a RAZOR alone.
/// </summary>
public class RazorPitchInvertTests {
	private static readonly JoystickCapabilities Stick = new(Present: true, ButtonCount: 4,
		HasThrottle: false, HasRudder: false, HasHat: false);

	// JOYSTICK on movement in the RAZOR's block, which puts stick Y on the elevator; the walker's block as given.
	private static SimulatorPreferences Bindings(JoystickAxisAssignment walkerStick = JoystickAxisAssignment.Turret) {
		var preferences = SimulatorPreferences.Defaults();
		preferences.Set(Prefs.RazorControlsBase, (byte)JoystickAxisAssignment.Movement, apply: false);
		preferences.Set(Prefs.HercControlsBase, (byte)walkerStick, apply: false);
		return preferences;
	}

	[Fact]
	public void TheRazorsElevatorIsReversed() {
		var reading = new JoystickReading(StickY: 0x40);
		var plain = new JoystickBindings { PilotingRazor = true }.Resolve(reading, Stick, Bindings());
		var inverted = new JoystickBindings { PilotingRazor = true, InvertRazorPitch = true }
			.Resolve(reading, Stick, Bindings());

		Assert.Equal(0x40, plain.Axes.Throttle);
		Assert.Equal(-0x40, inverted.Axes.Throttle);
	}

	/// <summary>
	/// In a walker the switch reverses nothing, wherever the stick's Y lands: on game axis 1 itself, the throttle, when
	/// the stick drives movement, or on the turret's pitch when it drives the turret.
	/// </summary>
	[Theory]
	[InlineData(JoystickAxisAssignment.Movement)]
	[InlineData(JoystickAxisAssignment.Turret)]
	public void AWalkersStickIsLeftAlone(JoystickAxisAssignment walkerStick) {
		var axes = new JoystickBindings { InvertRazorPitch = true }
			.Resolve(new JoystickReading(StickY: 0x40), Stick, Bindings(walkerStick)).Axes;

		Assert.Equal(0x40, walkerStick == JoystickAxisAssignment.Movement ? axes.Throttle : axes.TorsoPitch);
	}
}
