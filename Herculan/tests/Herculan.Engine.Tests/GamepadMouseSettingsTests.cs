using Herculan.Engine.Input;
using Xunit;

namespace Herculan.Engine.Tests;

/// <summary>How a gamepad's mouse settings are kept in <c>herculan-joystick.cfg</c>'s <c>[Gamepad]</c> section.</summary>
public class GamepadMouseSettingsTests {
	[Fact]
	public void DefaultsAreRightStickAndRightTriggerClicks() {
		var settings = new GamepadMouseSettings();

		Assert.Equal(GamepadSide.Right, settings.CursorStick);
		Assert.Equal(GamepadSide.Right, settings.LeftClickTrigger);
		Assert.Equal(100, settings.CursorSpeed);
	}

	[Fact]
	public void ChangedSettingsSurviveASaveWithNoDeviceSection() {
		string path = Path.GetTempFileName();
		try {
			var file = new JoystickMapFile {
				Mouse = new GamepadMouseSettings { CursorStick = GamepadSide.Left, LeftClickTrigger = GamepadSide.Left, CursorSpeed = 250 },
			};
			file.Save(path);

			var read = JoystickMapFile.Load(path);
			Assert.Equal(GamepadSide.Left, read.Mouse.CursorStick);
			Assert.Equal(GamepadSide.Left, read.Mouse.LeftClickTrigger);
			Assert.Equal(250, read.Mouse.CursorSpeed);
			Assert.NotNull(read.For(isGamepad: true, guid: null));
		} finally {
			File.Delete(path);
		}
	}

	[Theory]
	[InlineData("")]
	[InlineData("CursorStick = sideways\nLeftClickTrigger = 7\nCursorSpeed = fast")]
	public void MissingOrUnknownValuesKeepTheDefaults(string body) {
		string path = Path.GetTempFileName();
		try {
			File.WriteAllText(path, "[Gamepad]\n" + body + "\n");

			Assert.True(JoystickMapFile.Load(path).Mouse.IsDefault);
		} finally {
			File.Delete(path);
		}
	}

	[Theory]
	[InlineData("1", GamepadMouseSettings.MinCursorSpeed)]
	[InlineData("9000", GamepadMouseSettings.MaxCursorSpeed)]
	public void ACursorSpeedOutOfRangeIsHeldToIt(string value, int expected) {
		string path = Path.GetTempFileName();
		try {
			File.WriteAllText(path, "[Gamepad]\nCursorSpeed = " + value + "\n");

			Assert.Equal(expected, JoystickMapFile.Load(path).Mouse.CursorSpeed);
		} finally {
			File.Delete(path);
		}
	}
}
