namespace Herculan.Es1.Input;

/// <summary>
/// <c>JoystickRecord</c> (<c>ES1DBSIM 000b1738</c>, <c>0x1c</c> bytes): one device's four axes and eight buttons as
/// slot <c>+4</c> of its vtable returns them (docs/es1/simulation/joystick.md#the-record). Retail fills the one global
/// record on every read; <c>Input_ReadJoystickState</c> copies it out (<see cref="Clone"/>).
/// </summary>
public sealed class Es1JoystickRecord {
	/// <summary><c>+0</c>–<c>+0xc</c>: axes 0–3, the stick's X and Y, the port's second X and Y.</summary>
	public int[] Axes { get; } = new int[4];

	/// <summary>
	/// <c>+0x10</c>–<c>+0x17</c>: buttons 0–3 (port bits 4–7), then the four bytes the hat readers set
	/// (docs/es1/simulation/joystick.md#hats-and-throttles).
	/// </summary>
	public byte[] Buttons { get; } = new byte[8];

	/// <summary><c>+0x18</c>: the joystick object's axis mask <c>+0x6f</c>, its low byte.</summary>
	public byte AxisMask { get; set; }

	/// <summary><c>+0x19</c>: the device's button mask <c>+0x85</c>.</summary>
	public byte ButtonMask { get; set; }

	/// <summary><c>+0x1a</c>: the device's <c>+0x86</c>, 1 for the two classes that read a hat.</summary>
	public byte ReadsHat { get; set; }

	/// <summary><c>Input_ReadJoystickState</c> (<c>0004c4ac</c>)'s copy of the <c>0x1c</c> bytes.</summary>
	public Es1JoystickRecord Clone() {
		var copy = new Es1JoystickRecord { AxisMask = AxisMask, ButtonMask = ButtonMask, ReadsHat = ReadsHat };
		Array.Copy(Axes, copy.Axes, Axes.Length);
		Array.Copy(Buttons, copy.Buttons, Buttons.Length);
		return copy;
	}
}
