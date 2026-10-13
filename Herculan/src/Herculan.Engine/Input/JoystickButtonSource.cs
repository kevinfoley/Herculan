using System.Globalization;

namespace Herculan.Engine.Input;

/// <summary>
/// What drives one of the eight buttons the retail format binds (<see cref="JoystickDeviceMap.Buttons"/>): a device
/// button, or an axis pushed more than halfway from its centre toward one end.
///
/// <para><b>An axis as a button is this engine's addition</b>, for a gamepad's analogue triggers, which GLFW reports
/// as axes; retail reads buttons from <c>joyGetPosEx</c>'s button mask alone. A trigger rests at −1, so a trigger
/// bound with <see cref="AxisSign"/> +1 counts as pressed past three-quarters of its pull.</para>
/// </summary>
/// <param name="Index">The device button or axis, 0-based; −1 when nothing drives the button.</param>
/// <param name="AxisSign">0 for a device button; +1 or −1 for an axis, the end it is pushed toward.</param>
public readonly record struct JoystickButtonSource(int Index, int AxisSign = 0) {
	/// <summary>How far an axis has to be from its centre, as a fraction of the way to its end, to count as pressed.</summary>
	public const float AxisThreshold = 0.5f;

	/// <summary>Nothing drives the button, which greys its row on the CONTROLS panel.</summary>
	public static readonly JoystickButtonSource Unmapped = new(JoystickDeviceMap.Unmapped);

	/// <summary>Device button <paramref name="index"/>.</summary>
	public static JoystickButtonSource Button(int index) => new(index);

	/// <summary>Device axis <paramref name="index"/> pushed toward the end <paramref name="sign"/> names.</summary>
	public static JoystickButtonSource Axis(int index, int sign) => new(index, sign < 0 ? -1 : 1);

	/// <summary>Whether a device input drives the button.</summary>
	public bool IsMapped => Index >= 0;

	/// <summary>Whether it is an axis rather than a device button.</summary>
	public bool IsAxis => AxisSign != 0;

	/// <summary>Whether the button is held, given the device's axes (−1 to +1) and buttons.</summary>
	public bool Pressed(Func<int, float> axis, Func<int, bool> button) =>
		IsMapped && (IsAxis ? axis(Index) * AxisSign > AxisThreshold : button(Index));

	/// <summary>
	/// The setting's text in <c>herculan-joystick.cfg</c>: the button's number, <c>axis5+</c> or <c>axis5-</c> for an
	/// axis, or −1.
	/// </summary>
	public override string ToString() =>
		!IsMapped ? JoystickDeviceMap.Unmapped.ToString(CultureInfo.InvariantCulture)
		: IsAxis ? $"axis{Index.ToString(CultureInfo.InvariantCulture)}{(AxisSign > 0 ? '+' : '-')}"
		: Index.ToString(CultureInfo.InvariantCulture);

	/// <summary>Reads <see cref="ToString"/>'s text back, or null when it is neither form.</summary>
	public static JoystickButtonSource? Parse(string text) {
		text = text.Trim();
		if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int button)) {
			return button < 0 ? Unmapped : Button(button);
		}

		if (text.Length > "axis".Length + 1 && text.StartsWith("axis", StringComparison.OrdinalIgnoreCase)
			&& text[^1] is '+' or '-'
			&& int.TryParse(text["axis".Length..^1], NumberStyles.None, CultureInfo.InvariantCulture, out int axis)) {
			return Axis(axis, text[^1] == '+' ? 1 : -1);
		}

		return null;
	}
}
