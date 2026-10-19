namespace Herculan.Es1.Input;

/// <summary>
/// <c>JoystickAxisBuffer</c> (<c>ES1DBSIM 000b1754</c>), 17 bytes: the four axis values and the button byte a joystick
/// object's normalisation copies out, which <c>Joystick_ReadWithResponse</c> rescales in place. Every device shares
/// the one buffer, so a device whose joystick object is disabled reads what the last read left in it, as retail does
/// (docs/es1/simulation/joystick.md#the-record).
/// </summary>
public sealed class Es1JoystickAxisBuffer {
	/// <summary>Axes 0–3.</summary>
	public int[] Values { get; } = new int[4];

	/// <summary>The button byte, bits 0–3, the joystick object's <c>+0x25</c>.</summary>
	public byte Buttons { get; set; }
}
