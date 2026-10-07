using Herculan.Engine.Cockpit;

namespace Herculan.Engine.Input;

/// <summary>
/// What the input layer reports a stick can do — the eight-byte block <c>Input_QueryCapabilities</c> (<c>004777f8</c>)
/// (<c>DAT_006bb72c</c>) rebuilds on every call, and the whole of what the CONTROLS panel greys its
/// rows against.
///
/// <para>The panel reaches it in two steps. <c>Input_GetDevice(3)</c> (<c>0045c508</c>) is asked first, and when it comes
/// back null or with its low bit clear the panel takes <b>no</b> capability block at all and
/// disables all twelve rows at once — which is what a modern USB stick the retail code cannot
/// enumerate produces, and what the reference capture shows. Only past that gate does it read the
/// block below and disable rows one at a time.</para>
/// </summary>
/// <param name="Present">
/// Whether a stick was enumerated at all. False greys every row. This engine has no joystick input
/// of its own yet, so it is the default.
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
	/// No stick — what this engine reports until it grows joystick input, and what retail reports for
	/// a device it cannot enumerate.
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
