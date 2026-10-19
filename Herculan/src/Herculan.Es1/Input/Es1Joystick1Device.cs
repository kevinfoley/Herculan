namespace Herculan.Es1.Input;

/// <summary>
/// <c>IMJoystick1</c>, the port's first stick: <c>IMJoystick1_Ctor</c> (<c>ES1DBSIM 0004be30</c>) builds through
/// <c>JoystickDevice_Ctor</c> with mask 3 and kind 3, and reports buttons 0 and 1; it reads through
/// <c>Joystick_ReadWithResponse</c> (docs/es1/simulation/joystick.md#the-classes).
/// </summary>
public sealed class Es1Joystick1Device : Es1JoystickDevice {
	public Es1Joystick1Device(int mode, IEs1GamePort port, Es1JoystickGlobals globals)
		: base(mode, 3, port, 3, globals) {
		ButtonMask = 3;
	}
}
