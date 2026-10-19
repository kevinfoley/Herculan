namespace Herculan.Es1.Input;

/// <summary>
/// The joystick devices' shared state, globals in retail: the axes the devices have claimed and their count, the axis
/// buffer and the record (docs/es1/simulation/joystick.md#functions, "Globals"). One set of devices shares one.
/// </summary>
public sealed class Es1JoystickGlobals {
	/// <summary><c>JoystickClaimedAxes</c> (<c>ES1DBSIM 0009571c</c>): the axes a device has claimed, bits 0–3.</summary>
	public byte ClaimedAxes { get; set; }

	/// <summary><c>JoystickClaimedAxisCount</c> (<c>0009571d</c>): how many axes the devices have claimed.</summary>
	public byte ClaimedAxisCount { get; set; }

	/// <summary><c>JoystickAxisBuffer</c> (<c>000b1754</c>).</summary>
	public Es1JoystickAxisBuffer AxisBuffer { get; } = new();

	/// <summary><c>JoystickRecord</c> (<c>000b1738</c>).</summary>
	public Es1JoystickRecord Record { get; } = new();
}
