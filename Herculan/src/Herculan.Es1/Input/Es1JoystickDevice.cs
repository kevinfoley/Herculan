namespace Herculan.Es1.Input;

/// <summary>
/// A joystick device (<c>IMJoystick</c>, 0x8b bytes): the joystick object, the input device it registers as, the
/// response mode and the class's masks, and slot <c>+4</c> of its vtable, <see cref="ReadState"/>, which turns the
/// stick into one record (docs/es1/simulation/joystick.md#the-device-object). The classes are its subclasses; retail's
/// <c>IMJoystick1</c> and <c>IMJoystick2</c> share this class's vtable.
/// </summary>
public class Es1JoystickDevice {
	/// <summary>The band <c>Joystick_ReadWithResponse</c> takes off an axis before its rescale, and under which mode 1's square reads 0.</summary>
	private const int Band = 25;

	private readonly Es1JoystickGlobals _globals;

	/// <summary>
	/// <c>JoystickDevice_Ctor</c> (<c>ES1DBSIM 0004b6c0</c>)(this, mode, mask, port, kind): builds the joystick object
	/// with the mask, registers the device as input device <paramref name="kind"/>, disabled, and claims each axis of
	/// the mask's low byte that answered and no earlier device claimed, so the object's <c>+0x73</c> counts only those;
	/// then the object's mask becomes the mask's low byte whether its axes answered or not
	/// (docs/es1/simulation/joystick.md#the-device-object).
	/// </summary>
	protected Es1JoystickDevice(int mode, int mask, IEs1GamePort port, int kind, Es1JoystickGlobals globals) {
		_globals = globals;
		Joystick.Init(mask, port);
		Kind = kind;
		Mode = mode;
		Joystick.AxisCount = 0;
		int candidates = mask & 0xff & Joystick.AxisMask;
		for (int axis = 0; axis < 8; axis++) {
			int bit = 1 << axis;
			if ((candidates & bit) != 0 && (globals.ClaimedAxes & bit) == 0) {
				globals.ClaimedAxes |= (byte)bit;
				Joystick.AxisCount++;
				globals.ClaimedAxisCount++;
			}
		}

		Joystick.AxisMask = mask & 0xff;
	}

	/// <summary><c>+0</c>: the joystick object.</summary>
	public Es1Joystick Joystick { get; } = new();

	/// <summary><c>+0x78</c>: the input device's enabled byte, which <c>InputDevice_Enable</c> (<c>0004b1c0</c>) sets.</summary>
	public bool InputEnabled { get; private set; }

	/// <summary><c>+0x79</c>: the input device's kind, 3 for the first stick and 4 for the second.</summary>
	public int Kind { get; }

	/// <summary><c>+0x7d</c>: the response mode, 0 linear, 1 squared, 2 digital.</summary>
	public int Mode { get; }

	/// <summary><c>+0x81</c>: the class's number, 0 to 4 (docs/es1/simulation/joystick.md#the-classes).</summary>
	public int ClassNumber { get; protected set; }

	/// <summary><c>+0x85</c>: the buttons the device reports, bits 0–3.</summary>
	public byte ButtonMask { get; protected set; }

	/// <summary><c>+0x86</c>: 1 for the two classes that read a hat.</summary>
	public byte ReadsHat { get; protected set; }

	/// <summary><c>InputDevice_Enable</c> (<c>0004b1c0</c>) on <c>+0x78</c>.</summary>
	public void Enable() => InputEnabled = true;

	/// <summary>Slot <c>+4</c> of the device's vtable: the record. This class's is <see cref="ReadWithResponse"/>.</summary>
	public virtual Es1JoystickRecord ReadState() => ReadWithResponse();

	/// <summary>
	/// <c>Joystick_ReadWithResponse</c> (<c>0004b828</c>), slot <c>+4</c> of <c>IMJoystickVtable</c> (<c>00095756</c>):
	/// fills the shared record from the shared axis buffer (docs/es1/simulation/joystick.md#the-record). Mode 0 or 1
	/// normalises the axes into the buffer and mode 2 reads their signs; while the joystick object is disabled neither
	/// writes, and the buffer keeps what the last device's read left, as retail does. Each axis in the mask is then
	/// rescaled in the buffer past a band of 25, and mode 1 squares it into the record.
	/// </summary>
	protected Es1JoystickRecord ReadWithResponse() {
		var buffer = _globals.AxisBuffer;
		var record = _globals.Record;
		switch (Mode) {
			case 0:
			case 1:
				Joystick.ReadAxes(buffer);
				break;
			case 2:
				Joystick.ReadAxesDigital(buffer);
				break;
		}

		int s = Joystick.Shift;
		int k = (1 << (s + 8)) / ((1 << s) - Band);
		for (int i = 0; i < 4; i++) {
			int bit = 1 << i;
			if ((Joystick.AxisMask & bit) == 0) {
				record.Axes[i] = 0;
			} else {
				if (k != 0) {
					int v = buffer.Values[i];
					v = v > Band ? v - Band : v < -Band ? v + Band : 0;
					buffer.Values[i] = (k * v) >> 8;
				}

				int value = buffer.Values[i];
				switch (Mode) {
					case 1:
						// The square of the value's low word, its sign the whole dword's.
						short word = (short)value;
						int squared = (word * word) >> (s - 1);
						if (value < 0) {
							squared = -squared;
						}

						record.Axes[i] = squared > -Band && squared < Band ? 0 : squared;
						break;
					case 0:
					case 2:
						record.Axes[i] = value;
						break;

					// Any other mode leaves the record's axis as the last read left it, as retail does.
				}
			}

			record.Buttons[i] = (byte)((ButtonMask & bit) != 0 && (buffer.Buttons & bit) != 0 ? 1 : 0);
		}

		Array.Clear(record.Buttons, 4, 4);
		record.AxisMask = (byte)Joystick.AxisMask;
		record.ButtonMask = ButtonMask;
		record.ReadsHat = ReadsHat;
		return record;
	}
}
