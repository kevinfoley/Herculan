using Herculan.Engine.Content;
using Herculan.Engine.Input;
using HercWorks.Core.Data.File.Cfg;
using Xunit;

namespace Herculan.Engine.Tests;

/// <summary>
/// <see cref="JoystickBindings.FlightThrottleIsLever"/> — <c>FlightModel_Step</c>'s test for reading the throttle
/// axis as a lever position (docs/retail/simulation/razor-flight.md#throttle).
/// </summary>
public class FlightThrottleLeverTests {
	private static readonly JoystickCapabilities StickWithLever = new(Present: true, ButtonCount: 4,
		HasThrottle: true, HasRudder: true, HasHat: true);

	private static SimulatorPreferences ThrottleRows(byte herc, byte razor) {
		var preferences = SimulatorPreferences.Defaults();
		preferences.Set(Prefs.HercControlsBase + 1, herc, apply: false);
		preferences.Set(Prefs.RazorControlsBase + 1, razor, apply: false);
		return preferences;
	}

	/// <summary>
	/// The RAZOR's THROTTLE word is the row's 2, which the walker's lever mode does not count as a lever.
	/// </summary>
	[Fact]
	public void TheRazorsThrottleBindingIsALever() {
		var preferences = ThrottleRows(herc: 0, razor: 2);

		Assert.True(JoystickBindings.FlightThrottleIsLever(StickWithLever, preferences));
		Assert.Equal(0, new JoystickBindings { PilotingRazor = true }
			.ThrottleLeverMode(StickWithLever, preferences));
	}

	[Fact]
	public void AStickWithoutAThrottleIsNotALever() {
		var noThrottle = StickWithLever with { HasThrottle = false };

		Assert.False(JoystickBindings.FlightThrottleIsLever(noThrottle, ThrottleRows(herc: 0, razor: 2)));
	}

	/// <summary>The walker's binding does not reach the flight model, nor the RAZOR's PITCH word.</summary>
	[Fact]
	public void OnlyTheRazorsThrottleWordCounts() {
		Assert.False(JoystickBindings.FlightThrottleIsLever(StickWithLever, ThrottleRows(herc: 1, razor: 0)));
		Assert.False(JoystickBindings.FlightThrottleIsLever(StickWithLever, ThrottleRows(herc: 2, razor: 1)));
	}
}
