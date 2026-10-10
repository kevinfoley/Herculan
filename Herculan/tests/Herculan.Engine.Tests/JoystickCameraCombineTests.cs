using Herculan.Engine.Content;
using Herculan.Engine.Input;
using HercWorks.Core.Data.File.Cfg;
using Xunit;

namespace Herculan.Engine.Tests;

/// <summary>
/// <see cref="JoystickBindings.CombineForCamera"/>'s turret pitch — what <c>Sim_PollPlayerInput</c> still hands
/// the machine while the camera has the controls (docs/retail/simulation/joystick-input.md#while-the-camera-has-the-controls).
/// </summary>
public class JoystickCameraCombineTests {
	private static readonly JoystickCapabilities StickWithLever = new(Present: true, ButtonCount: 4,
		HasThrottle: true, HasRudder: true, HasHat: true);

	private static readonly PilotAxes KeyboardPitching = new(0, 0, 0, 0x80);

	private static SimulatorPreferences Bindings(byte stick, byte throttle, byte hat) {
		var preferences = SimulatorPreferences.Defaults();
		preferences.Set(Prefs.HercControlsBase, stick, apply: false);
		preferences.Set(Prefs.HercControlsBase + 1, throttle, apply: false);
		preferences.Set(Prefs.HercControlsBase + 3, hat, apply: false);
		return preferences;
	}

	[Fact]
	public void ALeverOnTheTurretPairPitches() {
		var axes = new JoystickBindings().CombineForCamera(new JoystickReading(StickY: 0x40, Throttle: 0x90),
			StickWithLever, Bindings(stick: 2, throttle: 2, hat: 0), KeyboardPitching);

		Assert.Equal(0x90, axes.TorsoPitch);
		Assert.Equal(0x40, axes.Throttle);
	}

	[Fact]
	public void TheHatOnTheTurretPairPitches() {
		var axes = new JoystickBindings().CombineForCamera(new JoystickReading(Hat: JoystickHat.South),
			StickWithLever, Bindings(stick: 1, throttle: 1, hat: 2), KeyboardPitching);

		Assert.Equal(-JoystickBindings.HatAxis, axes.TorsoPitch);
	}

	[Fact]
	public void TheKeyboardAndTheStickDoNotPitch() {
		var axes = new JoystickBindings().CombineForCamera(new JoystickReading(StickY: 0x40, Throttle: 0x90),
			StickWithLever, Bindings(stick: 2, throttle: 1, hat: 1), KeyboardPitching);

		Assert.Equal(0, axes.TorsoPitch);
	}
}
