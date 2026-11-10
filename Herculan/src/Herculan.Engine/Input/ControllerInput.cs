using System.Globalization;

namespace Herculan.Engine.Input;

/// <summary>Which kind of device input a <see cref="ControllerInput"/> is.</summary>
public enum ControllerInputKind {
	/// <summary>A device button.</summary>
	Button,

	/// <summary>An axis pushed past halfway toward one end.</summary>
	Axis,

	/// <summary>One direction of a hat.</summary>
	Hat,
}

/// <summary>
/// One device input a Custom profile binds to an action (<see cref="ControllerProfile"/>): a button, an axis pushed past
/// halfway toward one end, as <see cref="JoystickButtonSource"/> reads one, or one direction of a hat. <b>This engine's
/// own</b>; retail binds eight buttons and one hat by position.
/// </summary>
/// <param name="Kind">Button, axis or hat.</param>
/// <param name="Index">The device button, axis or hat, 0-based.</param>
/// <param name="AxisSign">For an axis, +1 or −1, the end it is pushed toward; otherwise 0.</param>
/// <param name="Direction">For a hat, one cardinal direction; otherwise <see cref="JoystickHat.None"/>.</param>
public readonly record struct ControllerInput(ControllerInputKind Kind, int Index, int AxisSign = 0, JoystickHat Direction = JoystickHat.None)
	: IComparable<ControllerInput> {
	private static readonly (JoystickHat Direction, string Name)[] HatNames = [
		(JoystickHat.North, "up"), (JoystickHat.East, "right"), (JoystickHat.South, "down"), (JoystickHat.West, "left"),
	];

	/// <summary>Device button <paramref name="index"/>.</summary>
	public static ControllerInput Button(int index) => new(ControllerInputKind.Button, index);

	/// <summary>Device axis <paramref name="index"/> pushed toward the end <paramref name="sign"/> names.</summary>
	public static ControllerInput Axis(int index, int sign) => new(ControllerInputKind.Axis, index, sign < 0 ? -1 : 1);

	/// <summary>Hat <paramref name="index"/> pushed toward <paramref name="direction"/>, one of the four cardinals.</summary>
	public static ControllerInput Hat(int index, JoystickHat direction) => new(ControllerInputKind.Hat, index, 0, direction);

	/// <summary>
	/// Whether the input is held, given the device's axes (−1 to +1), buttons and hats. A hat diagonal holds both of its
	/// directions.
	/// </summary>
	public bool Pressed(Func<int, float> axis, Func<int, bool> button, Func<int, JoystickHat> hat) => Kind switch {
		ControllerInputKind.Button => button(Index),
		ControllerInputKind.Axis => axis(Index) * AxisSign > JoystickButtonSource.AxisThreshold,
		_ => (hat(Index) & Direction) != 0,
	};

	/// <summary>
	/// The input's name in <c>herculan-joystick.cfg</c>: <c>button3</c>; <c>axis5+</c> or <c>axis5-</c>; <c>hat0up</c>,
	/// <c>hat0right</c>, <c>hat0down</c> or <c>hat0left</c>.
	/// </summary>
	public override string ToString() {
		string index = Index.ToString(CultureInfo.InvariantCulture);
		var direction = Direction;
		return Kind switch {
			ControllerInputKind.Button => $"button{index}",
			ControllerInputKind.Axis => $"axis{index}{(AxisSign > 0 ? '+' : '-')}",
			_ => $"hat{index}{HatNames.First(entry => entry.Direction == direction).Name}",
		};
	}

	/// <summary>Reads <see cref="ToString"/>'s text back, in any case, or null when it is none of its forms.</summary>
	public static ControllerInput? Parse(string text) {
		text = text.Trim().ToLowerInvariant();
		if (text.StartsWith("button", StringComparison.Ordinal)) {
			return Number(text["button".Length..]) is { } button ? Button(button) : null;
		}

		if (text.StartsWith("axis", StringComparison.Ordinal) && text.Length > "axis".Length + 1 && text[^1] is '+' or '-') {
			return Number(text["axis".Length..^1]) is { } axis ? Axis(axis, text[^1] == '+' ? 1 : -1) : null;
		}

		if (text.StartsWith("hat", StringComparison.Ordinal)) {
			foreach (var (direction, name) in HatNames) {
				if (text.EndsWith(name, StringComparison.Ordinal)) {
					return Number(text["hat".Length..^name.Length]) is { } hat ? Hat(hat, direction) : null;
				}
			}
		}

		return null;

		static int? Number(string digits) =>
			int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out int value) ? value : null;
	}

	/// <summary>Buttons, then axes, then hats; by index; an axis's + end before its −; a hat's directions clockwise from up.</summary>
	public int CompareTo(ControllerInput other) {
		int order = Kind.CompareTo(other.Kind);
		if (order == 0) {
			order = Index.CompareTo(other.Index);
		}

		if (order == 0) {
			order = other.AxisSign.CompareTo(AxisSign);
		}

		return order != 0 ? order : HatOrder(Direction).CompareTo(HatOrder(other.Direction));

		static int HatOrder(JoystickHat direction) => Array.FindIndex(HatNames, entry => entry.Direction == direction);
	}
}
