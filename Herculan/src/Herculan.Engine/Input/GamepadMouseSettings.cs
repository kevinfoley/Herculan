using System.Globalization;
using System.Text;

namespace Herculan.Engine.Input;

/// <summary>A gamepad's left or right stick or trigger.</summary>
public enum GamepadSide {
	/// <summary>The left one.</summary>
	Left,

	/// <summary>The right one.</summary>
	Right,
}

/// <summary>
/// Which stick moves the pointer and how fast, and which trigger is the left button, while a gamepad works as the mouse
/// in the front end and the mission editor (<see cref="Platform.GamepadPointer"/>; docs/herculan/herculan-key-bindings.md,
/// "Gamepad in the front end"). The other trigger is the right button. <b>This engine's own</b>, kept in
/// <c>herculan-joystick.cfg</c>'s <c>[Gamepad]</c> section.
/// </summary>
public sealed record GamepadMouseSettings {
	/// <summary>The slowest and fastest <see cref="CursorSpeed"/>.</summary>
	public const int MinCursorSpeed = 25;

	/// <inheritdoc cref="MinCursorSpeed"/>
	public const int MaxCursorSpeed = 400;

	/// <summary>The default <see cref="CursorSpeed"/>: the pace the pointer had before it could be set.</summary>
	public const int DefaultCursorSpeed = 100;

	/// <summary>The stick that moves the pointer.</summary>
	public GamepadSide CursorStick { get; init; } = GamepadSide.Right;

	/// <summary>
	/// How fast the stick moves the pointer, as a percentage of the default pace, from <see cref="MinCursorSpeed"/> to
	/// <see cref="MaxCursorSpeed"/>.
	/// </summary>
	public int CursorSpeed { get; init; } = DefaultCursorSpeed;

	/// <summary>The trigger held as the left button; the other is held as the right.</summary>
	public GamepadSide LeftClickTrigger { get; init; } = GamepadSide.Right;

	/// <summary>Whether the settings are the defaults, which a file need not carry.</summary>
	public bool IsDefault => this == new GamepadMouseSettings();

	/// <summary>
	/// Reads the <c>CursorStick</c>, <c>CursorSpeed</c> and <c>LeftClickTrigger</c> settings of a <c>[Gamepad]</c>
	/// section. A stick or trigger that is missing or neither <c>Left</c> nor <c>Right</c>, and a speed that is missing or
	/// not a whole number, keeps its default; a speed out of range is held to it.
	/// </summary>
	public static GamepadMouseSettings FromValues(Dictionary<string, string> values) {
		var defaults = new GamepadMouseSettings();
		return new GamepadMouseSettings {
			CursorStick = Side(values, "cursorStick", defaults.CursorStick),
			CursorSpeed = values.TryGetValue("cursorSpeed", out string? speed)
				&& int.TryParse(speed.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int percent)
				? Math.Clamp(percent, MinCursorSpeed, MaxCursorSpeed)
				: defaults.CursorSpeed,
			LeftClickTrigger = Side(values, "leftClickTrigger", defaults.LeftClickTrigger),
		};
	}

	/// <summary>Writes the three settings <see cref="FromValues"/> reads, with a comment on what they are.</summary>
	public void WriteValues(StringBuilder text) {
		text.AppendLine(";; The gamepad as the mouse, in the front end and the mission editor: the stick");
		text.AppendLine(";; that moves the pointer, and the trigger that is the left button (the other");
		text.AppendLine(";; trigger is the right), Left or Right; and the pointer's speed, as a percentage");
		text.AppendLine($";; of the default, {MinCursorSpeed} to {MaxCursorSpeed}.");
		text.AppendLine($"CursorStick = {CursorStick}");
		text.AppendLine(CultureInfo.InvariantCulture, $"CursorSpeed = {CursorSpeed}");
		text.AppendLine($"LeftClickTrigger = {LeftClickTrigger}");
	}

	private static GamepadSide Side(Dictionary<string, string> values, string key, GamepadSide fallback) =>
		values.TryGetValue(key, out string? text) && Enum.TryParse(text.Trim(), ignoreCase: true, out GamepadSide side)
			&& Enum.IsDefined(side)
			? side
			: fallback;
}
