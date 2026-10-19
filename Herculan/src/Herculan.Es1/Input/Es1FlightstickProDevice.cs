namespace Herculan.Es1.Input;

/// <summary>
/// <c>IMFlightstickPro</c>: <c>IMFlightstickPro_Ctor</c> (<c>ES1DBSIM 0004c114</c>) builds on <c>IMFlightstick</c>'s,
/// then class 3, buttons 0–3 and a hat read from the buttons (docs/es1/simulation/joystick.md#the-classes).
/// </summary>
public sealed class Es1FlightstickProDevice : Es1FlightstickDevice {
	public Es1FlightstickProDevice(int mode, IEs1GamePort port, Es1JoystickGlobals globals)
		: base(mode, port, globals) {
		ClassNumber = 3;
		ButtonMask = 0xf;
		ReadsHat = 1;
	}

	/// <summary>
	/// <c>IMFlightstickPro_ReadState</c> (<c>0004c164</c>): <c>IMFlightstick_ReadState</c>, then, while buttons 0 and 1
	/// are both held, buttons 2 and 3 pick one hat byte, both <c>+0x14</c>, 2 alone <c>+0x15</c>, 3 alone
	/// <c>+0x16</c>, neither <c>+0x17</c>, and the four button bytes are cleared
	/// (docs/es1/simulation/joystick.md#hats-and-throttles). Which direction each byte is is Open there.
	/// </summary>
	public override Es1JoystickRecord ReadState() {
		var record = base.ReadState();
		byte[] buttons = record.Buttons;
		if (buttons[0] == 0 || buttons[1] == 0) {
			return record;
		}

		int hatByte = buttons[2] != 0 ? (buttons[3] != 0 ? 4 : 5) : (buttons[3] != 0 ? 6 : 7);
		buttons[hatByte] = 1;
		Array.Clear(buttons, 0, 4);
		return record;
	}
}
