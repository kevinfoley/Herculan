namespace Herculan.Es1.Input;

/// <summary>
/// <c>IMThrustmasterFCS</c>: <c>IMThrustmasterFCS_Ctor</c> (<c>ES1DBSIM 0004bef8</c>) builds through
/// <c>JoystickDevice_Ctor</c> with mask <c>0xb</c> and kind 3, class 1, reports buttons 0–3 and reads a hat from axis 3
/// (docs/es1/simulation/joystick.md#the-classes).
/// </summary>
public sealed class Es1ThrustmasterFcsDevice : Es1JoystickDevice {
	/// <summary>The record's hat bytes in the order of their thresholds: <c>+0x14</c>, <c>+0x16</c>, <c>+0x15</c>, <c>+0x17</c>.</summary>
	private static readonly int[] HatBytes = [4, 6, 5, 7];

	public Es1ThrustmasterFcsDevice(int mode, IEs1GamePort port, Es1JoystickGlobals globals)
		: base(mode, 0xb, port, 3, globals) {
		ClassNumber = 1;
		ButtonMask = 0xf;
		ReadsHat = 1;
	}

	/// <summary>
	/// <c>IMThrustmasterFCS_ReadState</c> (<c>0004bf50</c>): <c>Joystick_ReadWithResponse</c>, then the record's fourth
	/// axis cleared and axis 3's normalised value, the joystick object's <c>+0x21</c>, turned into one hat byte: with
	/// t = −1 &lt;&lt; s, below t − (t &gt;&gt; 3) sets <c>+0x14</c>, and each next threshold, 1 &lt;&lt; (s − 2)
	/// higher, sets <c>+0x16</c>, <c>+0x15</c> and <c>+0x17</c>; −112, −80, −48 and −16 for s = 7. A value of 0, or one
	/// from the last threshold up, sets none (docs/es1/simulation/joystick.md#hats-and-throttles). Which direction each
	/// byte is is Open there.
	/// </summary>
	public override Es1JoystickRecord ReadState() {
		var record = ReadWithResponse();
		int a = Joystick.Values[3];
		record.Axes[3] = 0;
		if (a == 0) {
			return record;
		}

		int t = -1 << Joystick.Shift;
		int quarter = t >> 2;
		int threshold = t - (quarter >> 1);
		foreach (int hatByte in HatBytes) {
			if (a < threshold) {
				record.Buttons[hatByte] = 1;
				break;
			}

			threshold -= quarter;
		}

		return record;
	}
}
