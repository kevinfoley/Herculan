namespace Herculan.Es1.Input;

/// <summary>
/// <c>IMFlightstick</c>: <c>IMFlightstick_Ctor</c> (<c>ES1DBSIM 0004c054</c>) builds through <c>JoystickDevice_Ctor</c>
/// with mask <c>0xb</c> and kind 3, class 2, reports buttons 0 and 1 and reads a throttle from axis 3
/// (docs/es1/simulation/joystick.md#the-classes).
/// </summary>
public class Es1FlightstickDevice : Es1JoystickDevice {
	/// <summary>The size under which the throttle reads 0.</summary>
	private const int ThrottleBand = 0x40;

	public Es1FlightstickDevice(int mode, IEs1GamePort port, Es1JoystickGlobals globals)
		: base(mode, 0xb, port, 3, globals) {
		ClassNumber = 2;
		ButtonMask = 3;
	}

	/// <summary>
	/// <c>IMFlightstick_ReadState</c> (<c>0004c0a4</c>): <c>Joystick_ReadWithResponse</c>, then the record's fourth axis
	/// becomes a throttle from axis 3's normalised value <c>a</c>, the joystick object's <c>+0x21</c>: t = 2a + (1 &lt;&lt; s)
	/// as an <c>int16</c> from <c>a</c>'s low word, squared and shifted right s − 1, t's sign, and 0 under 64 in size
	/// (docs/es1/simulation/joystick.md#hats-and-throttles).
	/// </summary>
	public override Es1JoystickRecord ReadState() {
		var record = ReadWithResponse();
		int s = Joystick.Shift;
		short t = (short)((short)Joystick.Values[3] * 2 + (1 << s));
		int throttle = (t * t) >> (s - 1);
		if (t < 0) {
			throttle = -throttle;
		}

		record.Axes[3] = throttle > -ThrottleBand && throttle < ThrottleBand ? 0 : throttle;
		return record;
	}
}
