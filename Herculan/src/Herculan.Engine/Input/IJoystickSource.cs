namespace Herculan.Engine.Input;

/// <summary>
/// The stick as the simulator reads it: what it can do, and this frame's reading in the abstract four-axis,
/// one-hat, eight-button shape the retail bindings are written against (<see cref="JoystickDeviceMap"/>).
/// </summary>
public interface IJoystickSource {
	/// <summary>What the CONTROLS panel greys its rows against; <see cref="JoystickCapabilities.None"/> with nothing attached.</summary>
	JoystickCapabilities Capabilities { get; }

	/// <summary>This frame's reading, already deadzoned and through the response curve.</summary>
	JoystickReading Read();
}
