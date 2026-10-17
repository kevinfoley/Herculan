using Herculan.Engine.Input;
using Xunit;

namespace Herculan.Engine.Tests;

/// <summary>How a gamepad's mouse settings are kept in <c>herculan-joystick.cfg</c>'s <c>[Gamepad]</c> section.</summary>
public class GamepadMouseSettingsTests {
	/// <summary>
	/// The stick and the trigger are set to opposite sides, one of them the default, so each case catches
	/// a setting that is dropped, and either catches the two being read from each other's key.
	/// </summary>
	[Theory]
	[InlineData(GamepadSide.Left, GamepadSide.Right)]
	[InlineData(GamepadSide.Right, GamepadSide.Left)]
	public void ChangedSettingsSurviveASaveWithNoDeviceSection(GamepadSide stick, GamepadSide trigger) {
		string path = Path.GetTempFileName();
		try {
			var file = new JoystickMapFile {
				Mouse = new GamepadMouseSettings { CursorStick = stick, LeftClickTrigger = trigger, CursorSpeed = 250 },
			};
			file.Save(path);

			var read = JoystickMapFile.Load(path);
			Assert.Equal(stick, read.Mouse.CursorStick);
			Assert.Equal(trigger, read.Mouse.LeftClickTrigger);
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
