namespace Herculan.Es1.Input;

/// <summary>
/// <c>IMJoystick2</c>, the port's second stick: <c>IMJoystick2_Ctor</c> (<c>ES1DBSIM 0004be94</c>) builds through
/// <c>JoystickDevice_Ctor</c> with mask <c>0xc</c> and kind 4, and reports buttons 2 and 3; it reads through
/// <c>Joystick_ReadWithResponse</c> (docs/es1/simulation/joystick.md#the-classes).
/// </summary>
public sealed class Es1Joystick2Device : Es1JoystickDevice {
	public Es1Joystick2Device(int mode, IEs1GamePort port, Es1JoystickGlobals globals)
		: base(mode, 0xc, port, 4, globals) {
		ButtonMask = 0xc;
	}
}
