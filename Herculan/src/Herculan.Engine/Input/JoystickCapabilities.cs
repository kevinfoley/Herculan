using Herculan.Engine.Cockpit;

namespace Herculan.Engine.Input;

/// <summary>
/// What the input layer reports a stick can do, which is all the CONTROLS panel greys its rows
/// against — the eight-byte block <c>Input_QueryCapabilities</c> (<c>004777f8</c>)
/// (<c>DAT_006bb72c</c>) rebuilds on every call, behind the gate on whether a stick enumerated at all
/// (docs/retail/simulation/joystick-input.md#the-capability-block--input_querycapabilities-004777f8,
/// docs/retail/simulation/joystick-input.md#a-stick-that-does-not-enumerate).
/// </summary>
/// <param name="Present">
/// Whether a stick was enumerated at all — retail's <c>Input_GetDevice(3)</c> (<c>0045c508</c>) gate.
/// False greys every row, and is the default.
/// </param>
/// <param name="ButtonCount">
/// How many buttons the stick reports, capped at eight — the block's <c>+2</c>, built as
/// <c>DAT_006bb438 + DAT_006bb5cc</c> clamped. Rows past it are greyed.
/// </param>
/// <param name="HasThrottle">The block's <c>+4</c>, from the device flags' bit 0. Greys the THROTTLE row.</param>
/// <param name="HasRudder">The block's <c>+5</c>, from bit 1. Greys the RUDDER row.</param>
/// <param name="HasHat">The block's <c>+6</c>, from bit 4. Greys the HAT row.</param>
public readonly record struct JoystickCapabilities(
	bool Present = false,
	int ButtonCount = 0,
	bool HasThrottle = false,
	bool HasRudder = false,
	bool HasHat = false) {

	/// <summary>The most buttons the panel has rows for. The block clamps to this itself.</summary>
	public const int MaxButtons = 8;

	/// <summary>
	/// No stick — what is reported with nothing attached, and what retail reports for a device it
	/// cannot enumerate.
	/// </summary>
	public static readonly JoystickCapabilities None = new();

	/// <summary>
	/// Whether row <paramref name="row"/> of the panel is live. Rows 0-3 are JOYSTICK, THROTTLE,
	/// RUDDER and HAT; 4-11 are BUTTON 1-8. The JOYSTICK row has no capability byte of its own — once
	/// a stick is present at all, it is always live.
	/// </summary>
	public bool RowEnabled(int row) {
		if (!Present) {
			return false;
		}

		return row switch {
			0 => true,
			1 => HasThrottle,
			2 => HasRudder,
			3 => HasHat,
			_ => row - ControlsPanelLayout.AxisRowCount < Math.Min(ButtonCount, MaxButtons),
		};
	}
}
