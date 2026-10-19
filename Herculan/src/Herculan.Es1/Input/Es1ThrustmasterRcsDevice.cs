namespace Herculan.Es1.Input;

/// <summary>
/// <c>IMThrustmasterRCS</c>, the rudder pedals of preferences option 9: <c>IMThrustmasterRCS_Ctor</c>
/// (<c>ES1DBSIM 0004bfe0</c>) builds through <c>JoystickDevice_Ctor</c> with mask 4 and kind 4, class 4, and reports no
/// button (docs/es1/simulation/joystick.md#the-classes).
/// </summary>
public sealed class Es1ThrustmasterRcsDevice : Es1JoystickDevice {
	public Es1ThrustmasterRcsDevice(int mode, IEs1GamePort port, Es1JoystickGlobals globals)
		: base(mode, 4, port, 4, globals) {
		ClassNumber = 4;
		ButtonMask = 0;
	}

	/// <summary>
	/// <c>IMThrustmasterRCS_ReadState</c> (<c>0004c034</c>): <c>Joystick_ReadWithResponse</c>, then the record's third
	/// axis, its one axis, negated (docs/es1/simulation/joystick.md#hats-and-throttles).
	/// </summary>
	public override Es1JoystickRecord ReadState() {
		var record = ReadWithResponse();
		record.Axes[2] = -record.Axes[2];
		return record;
	}
}
