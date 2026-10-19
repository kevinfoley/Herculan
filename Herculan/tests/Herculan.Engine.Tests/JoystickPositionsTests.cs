using Herculan.Engine.Input;
using Xunit;

namespace Herculan.Engine.Tests;

/// <summary>
/// <see cref="JoystickDeviceMap.ReadPositions"/> hands a game the stick before any response curve, and
/// <see cref="JoystickDeviceMap.Read"/> is ES2's curve laid on top of it at the map's own deadzone.
/// </summary>
public class JoystickPositionsTests {
	// Stick X at full travel, stick Y inside the original's deadzone (0.1 of 0x80 is 13 counts, under 0x19), the throttle
	// at full travel, and the rudder at half.
	private static float Axis(int index) => index switch { 0 => 1f, 1 => 0.1f, 2 => 1f, 3 => 0.5f, _ => 0f };

	private static bool Button(int index) => index == 2;

	// Catches a deadzone or curve creeping into the positional read, a dropped invert, and an unmapped role reading its axis.
	[Fact]
	public void PositionsAreLinearWithTheInvertsAndNoDeadzone() {
		var map = new JoystickDeviceMap { InvertThrottle = true, RudderAxis = JoystickDeviceMap.Unmapped };

		var positions = map.ReadPositions(Axis, Button, JoystickHat.North);

		Assert.Equal(new JoystickPositions(JoystickReading.RawFull, 13, -JoystickReading.RawFull, 0, JoystickHat.North, 1 << 2), positions);
	}

	// Catches the reading falling back to the original's deadzone in place of the map's, and the hat or buttons not passing through.
	[Fact]
	public void TheReadingIsTheResponseOverThePositionsAtTheMapsDeadzone() {
		var map = new JoystickDeviceMap { Deadzone = 5, InvertStickX = true };

		var positions = map.ReadPositions(Axis, Button, JoystickHat.East);
		var reading = map.Read(Axis, Button, JoystickHat.East);

		Assert.NotEqual(0, reading.StickY);
		Assert.Equal(new JoystickReading(
			JoystickReading.ApplyResponse(positions.StickX, 5),
			JoystickReading.ApplyResponse(positions.StickY, 5),
			JoystickReading.ApplyResponse(positions.Throttle, 5),
			JoystickReading.ApplyResponse(positions.Rudder, 5),
			positions.Hat,
			positions.Buttons), reading);
	}
}
