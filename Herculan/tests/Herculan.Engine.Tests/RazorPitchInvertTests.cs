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

	// JOYSTICK on movement in the RAZOR's block, which puts stick Y on the elevator; on turret in the walker's.
	private static SimulatorPreferences Bindings() {
		var preferences = SimulatorPreferences.Defaults();
		preferences.Set(Prefs.RazorControlsBase, 1, apply: false);
		preferences.Set(Prefs.HercControlsBase, 2, apply: false);
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

	[Fact]
	public void AHercsTurretIsLeftAlone() {
		var input = new JoystickBindings { InvertRazorPitch = true }
			.Resolve(new JoystickReading(StickY: 0x40), Stick, Bindings());

		Assert.Equal(0x40, input.Axes.TorsoPitch);
	}
}
