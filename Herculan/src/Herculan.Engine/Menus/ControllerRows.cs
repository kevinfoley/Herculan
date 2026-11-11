using Herculan.Engine.Input;

namespace Herculan.Engine.Menus;

/// <summary>
/// <b>This engine's own:</b> what one game's <see cref="ControllerWindow"/> shows besides the device itself: the rows the
/// game's bindings read from <see cref="JoystickDeviceMap"/>, what the "Game reads" column reads through them, and the
/// rows and options the game adds of its own. Every key is a localization key.
/// </summary>
public sealed record ControllerRows {
	/// <summary>The four axis rows' labels, in the map's slot order: stick X, stick Y, throttle, rudder.</summary>
	public required IReadOnlyList<string> AxisLabelKeys { get; init; }

	/// <summary>The throttle row's Invert tooltip; null for none.</summary>
	public string? ThrottleInvertTipKey { get; init; }

	/// <summary>
	/// The hat row's label, or null for a game that reads no hat, which shows neither the hat row nor the hat diagonals
	/// option.
	/// </summary>
	public string? HatLabelKey { get; init; }

	/// <summary>How many of the map's BUTTON rows the game reads, from BUTTON 1, at most <see cref="JoystickCapabilities.MaxButtons"/>.</summary>
	public required int ButtonRows { get; init; }

	/// <summary>Whether the game takes the map's centre deadzone off its axes, which shows the deadzone option.</summary>
	public bool ReadsDeadzone { get; init; }

	/// <summary>One frame of a device read through a map, as the game reads it, each axis on ±<see cref="ReadsFull"/>.</summary>
	public required Func<ControllerDevice, JoystickDeviceMap, ControllerReads> Reads { get; init; }

	/// <summary>The full deflection of an axis in <see cref="Reads"/>.</summary>
	public required int ReadsFull { get; init; }

	/// <summary>The note under the rows, which says where the game sets what each control does.</summary>
	public required string NoteKey { get; init; }

	/// <summary>Rows of the game's own, after the BUTTON rows, each bound to a device button or axis as a BUTTON row is.</summary>
	public IReadOnlyList<ControllerButtonRow> ExtraButtonRows { get; init; } = [];

	/// <summary>Options of the game's own, after the deadzone and hat diagonals.</summary>
	public IReadOnlyList<ControllerOption> Options { get; init; } = [];

	/// <summary>
	/// Which of <see cref="ControllerActions.Offered"/> the game has. A Custom profile can bind any of them, and one the
	/// running game lacks does nothing in it.
	/// </summary>
	public required IReadOnlySet<KeyAction> Actions { get; init; }

	/// <summary>
	/// Whether the game has a RAZOR, which gives the Custom view a RAZOR's axis functions and actions beside a HERC's. A game
	/// without one shows the HERC's alone and leaves the RAZOR's in the file as they are.
	/// </summary>
	public bool HasRazor { get; init; }

	/// <summary>Every offered action but <paramref name="lacking"/>, for <see cref="Actions"/>.</summary>
	public static IReadOnlySet<KeyAction> OfferedExcept(IEnumerable<KeyAction> lacking) =>
		ControllerActions.Offered.Select(entry => entry.Action).Except(lacking).ToHashSet();
}

/// <summary>One frame as a game reads it, for the Controller window's "Game reads" column.</summary>
/// <param name="StickX">The stick X slot's value.</param>
/// <param name="StickY">The stick Y slot's value.</param>
/// <param name="Throttle">The throttle slot's value.</param>
/// <param name="Rudder">The rudder slot's value.</param>
/// <param name="Hat">The hat's directions.</param>
/// <param name="Buttons">One bit per BUTTON row, bit 0 being BUTTON 1.</param>
public readonly record struct ControllerReads(int StickX, int StickY, int Throttle, int Rudder, JoystickHat Hat, byte Buttons);

/// <summary>A row of a game's own in the Controller window, bound to a device button or axis direction.</summary>
/// <param name="LabelKey">The row's label.</param>
/// <param name="TipKey">The label's tooltip.</param>
/// <param name="Get">The row's input in a map.</param>
/// <param name="Set">The map with the row's input changed.</param>
public sealed record ControllerButtonRow(string LabelKey, string TipKey,
	Func<JoystickDeviceMap, JoystickButtonSource> Get, Func<JoystickDeviceMap, JoystickButtonSource, JoystickDeviceMap> Set);

/// <summary>A checkbox of a game's own under the Controller window's Options.</summary>
/// <param name="LabelKey">The checkbox's label.</param>
/// <param name="TipKey">Its tooltip.</param>
/// <param name="Get">The option in a map.</param>
/// <param name="Set">The map with the option changed.</param>
public sealed record ControllerOption(string LabelKey, string TipKey,
	Func<JoystickDeviceMap, bool> Get, Func<JoystickDeviceMap, bool, JoystickDeviceMap> Set);
