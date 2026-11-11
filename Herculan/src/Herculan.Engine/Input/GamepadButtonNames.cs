namespace Herculan.Engine.Input;

/// <summary>A family of gamepads whose buttons carry the same printed names, as <see cref="GamepadButtonNames"/> tells them apart.</summary>
public enum GamepadFamily {
	/// <summary>A gamepad whose name names none of the others; its buttons are named by number.</summary>
	Unknown,

	/// <summary>An Xbox 360 pad.</summary>
	Xbox360,

	/// <summary>An Xbox One or Series pad, and any other the driver names only as Xbox or XInput.</summary>
	Xbox,

	/// <summary>A DualShock 3.</summary>
	PlayStation3,

	/// <summary>A DualShock 4.</summary>
	PlayStation4,

	/// <summary>A DualSense.</summary>
	PlayStation5,

	/// <summary>A Nintendo Switch Pro Controller.</summary>
	SwitchPro,

	/// <summary>A Steam Deck's own controls.</summary>
	SteamDeck,
}

/// <summary>
/// The names printed on a gamepad's buttons, for the Controller window's lists. <b>This engine's own.</b> GLFW reads every
/// gamepad in one standard layout (docs/herculan/joystick-config.md, "Gamepads") and says nothing of which pad it is but
/// its name, so the family is guessed from the name, and a pad it names none of is named by button number. GLFW's layout
/// puts each button where the Xbox pad's is, so a Switch Pro Controller's south button, printed B, is button 0.
/// </summary>
public static class GamepadButtonNames {
	// GLFW's buttons 0-10, in its layout's order: the four face buttons south, east, west, north, the bumpers, Back, Start,
	// the guide button and the stick clicks. A name in braces is a localization key: the PlayStation's face buttons are
	// shapes, and the shapes have names in each language. The rest are the names printed on the pads, kept as printed.
	private static readonly string[] Xbox360Names = ["A", "B", "X", "Y", "LB", "RB", "Back", "Start", "Xbox", "LS", "RS"];
	private static readonly string[] XboxNames = ["A", "B", "X", "Y", "LB", "RB", "View", "Menu", "Xbox", "LS", "RS"];
	private static readonly string[] PlayStation3Names = [.. PlayStationFaces, "L1", "R1", "Select", "Start", "PS", "L3", "R3"];
	private static readonly string[] PlayStation4Names = [.. PlayStationFaces, "L1", "R1", "Share", "Options", "PS", "L3", "R3"];
	private static readonly string[] PlayStation5Names = [.. PlayStationFaces, "L1", "R1", "Create", "Options", "PS", "L3", "R3"];
	private static readonly string[] SwitchProNames = ["B", "A", "Y", "X", "L", "R", "−", "+", "Home", "LS", "RS"];
	private static readonly string[] SteamDeckNames = ["A", "B", "X", "Y", "L1", "R1", "View", "Menu", "Steam", "L3", "R3"];

	private static string[] PlayStationFaces => ["{controller.ps_cross}", "{controller.ps_circle}", "{controller.ps_square}", "{controller.ps_triangle}"];

	// The d-pad's buttons, 11-14 in GLFW's layout, and the hat directions they also are.
	private static readonly (int Button, JoystickHat Direction, string Key)[] DPad = [
		(ControllerDevice.GamepadDPadUp, JoystickHat.North, "controller.up"),
		(ControllerDevice.GamepadDPadRight, JoystickHat.East, "controller.right"),
		(ControllerDevice.GamepadDPadDown, JoystickHat.South, "controller.down"),
		(ControllerDevice.GamepadDPadLeft, JoystickHat.West, "controller.left"),
	];

	/// <summary>The family <paramref name="name"/>, a device's name as GLFW gives it, belongs to.</summary>
	public static GamepadFamily FamilyOf(string name) {
		bool Has(string part) => name.Contains(part, StringComparison.OrdinalIgnoreCase);
		return true switch {
			_ when Has("DualSense") || Has("PS5") => GamepadFamily.PlayStation5,
			_ when Has("DualShock 4") || Has("PS4") => GamepadFamily.PlayStation4,
			_ when Has("DualShock 3") || Has("PS3") || Has("PlayStation 3") => GamepadFamily.PlayStation3,
			_ when Has("Switch Pro") || Has("Pro Controller") => GamepadFamily.SwitchPro,
			_ when Has("Steam Deck") => GamepadFamily.SteamDeck,
			_ when Has("360") => GamepadFamily.Xbox360,
			_ when Has("Xbox") || Has("XInput") => GamepadFamily.Xbox,
			_ => GamepadFamily.Unknown,
		};
	}

	/// <summary>
	/// Button <paramref name="button"/>'s name on <paramref name="device"/>, through <paramref name="text"/> for a localized
	/// one, or <c>controller.button</c>'s "Button" and its number for a joystick, a pad of no known family, or a button the
	/// family has no name for.
	/// </summary>
	public static string Label(ControllerDevice? device, int button, Func<string, string> text) =>
		Name(device, button, text) ?? string.Format(text("controller.button"), button);

	/// <summary>
	/// The name of hat <paramref name="hat"/> pushed toward <paramref name="direction"/>: a known pad's d-pad direction, or
	/// <c>controller.hat_direction</c>'s.
	/// </summary>
	public static string HatLabel(ControllerDevice? device, int hat, JoystickHat direction, Func<string, string> text) {
		var entry = DPad.First(entry => entry.Direction == direction);
		return hat == 0 && Family(device) != GamepadFamily.Unknown
			? string.Format(text("controller.dpad"), text(entry.Key))
			: string.Format(text("controller.hat_direction"), hat, text(entry.Key));
	}

	private static string? Name(ControllerDevice? device, int button, Func<string, string> text) {
		string[]? names = Family(device) switch {
			GamepadFamily.Xbox360 => Xbox360Names,
			GamepadFamily.Xbox => XboxNames,
			GamepadFamily.PlayStation3 => PlayStation3Names,
			GamepadFamily.PlayStation4 => PlayStation4Names,
			GamepadFamily.PlayStation5 => PlayStation5Names,
			GamepadFamily.SwitchPro => SwitchProNames,
			GamepadFamily.SteamDeck => SteamDeckNames,
			_ => null,
		};

		if (names == null) {
			return null;
		}

		if (button >= 0 && button < names.Length) {
			string name = names[button];
			return name.StartsWith('{') ? text(name[1..^1]) : name;
		}

		return DPad.Where(entry => entry.Button == button).Select(entry => string.Format(text("controller.dpad"), text(entry.Key)))
			.FirstOrDefault();
	}

	private static GamepadFamily Family(ControllerDevice? device) =>
		device is { IsGamepad: true } ? FamilyOf(device.Name) : GamepadFamily.Unknown;
}
