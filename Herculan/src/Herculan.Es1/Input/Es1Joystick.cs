using System.Numerics;

namespace Herculan.Es1.Input;

/// <summary>
/// The game-port joystick object (<c>DVJoystick_*</c>, 0x78 bytes), at <c>+0</c> of a joystick device
/// (docs/es1/simulation/joystick.md#the-joystick-object). The raw counts and the calibration block are not ported: the
/// counts' slots hold the calibrated value <see cref="IEs1GamePort"/> gives, and the normalisation copies it.
/// </summary>
public sealed class Es1Joystick {
	/// <summary>The calibrated value of the calibration's maximum under the shift 7: the normalisation holds a count to the maximum.</summary>
	private const int FullScale = 128;

	/// <summary>
	/// <c>+4</c>–<c>+0x10</c>: in retail the raw counts of axes 0–3; here the calibrated value the port gave at the last
	/// poll.
	/// </summary>
	public int[] PortValues { get; } = new int[4];

	/// <summary><c>+0x14</c>: the buttons, bits 0–3, 1 held.</summary>
	public byte Buttons { get; set; }

	/// <summary><c>+0x15</c>–<c>+0x21</c>: the four axis values the last normalisation computed.</summary>
	public int[] Values { get; } = new int[4];

	/// <summary><c>+0x25</c>: the copy of the buttons the normalisation copies out after the four values.</summary>
	public byte ButtonsCopy { get; set; }

	/// <summary><c>+0</c> bit 0: enabled; the poll and the two normalisations do nothing while it is clear.</summary>
	public bool Enabled { get; private set; }

	/// <summary><c>+0x66</c>, <c>int16</c>: the normalisation's shift, 0 from Init and 7 from the calibration's load.</summary>
	public ushort Shift { get; set; }

	/// <summary><c>+0x6d</c>: the I/O port, here the port's stand-in.</summary>
	public IEs1GamePort Port { get; private set; } = Es1NoStickPort.Instance;

	/// <summary><c>+0x6f</c>, dword: the axis mask, bits 0–3.</summary>
	public int AxisMask { get; set; }

	/// <summary><c>+0x73</c>, dword: the number of axes the poll reads.</summary>
	public int AxisCount { get; set; }

	/// <summary>
	/// <c>DVJoystick_Init</c> (<c>ES1DBSIM 0006b4bc</c>)(mask, port), as <c>DVJoystick_Ctor</c> (<c>0006b42c</c>) runs it
	/// once <c>GL_ClaimGlobal</c> has put the object in the empty GL slot of the device's kind; the slot itself is not
	/// modelled (docs/es1/simulation/joystick.md#construction-and-detection). A mask of <c>0xf</c> takes the axes detected; any
	/// other enables the object only when every axis it names answers. Detection that finds no axis leaves the object
	/// disabled with no axes. Returns whether it enabled the object.
	/// </summary>
	public bool Init(int mask, IEs1GamePort port) {
		Port = port;
		Buttons = 0;
		ButtonsCopy = 0;
		Shift = 0;
		Array.Clear(PortValues);
		Array.Clear(Values);

		// DVJoystick_Detect (0006be74) through DVJoystick_DetectGamePort (0006bf4d): the axes that answer and their count.
		int detected = 0;
		for (int axis = 0; axis < 4; axis++) {
			if (port.AxisAnswers(axis)) {
				detected |= 1 << axis;
			}
		}

		AxisMask = detected;
		AxisCount = BitOperations.PopCount((uint)detected);
		if (detected == 0) {
			return false;
		}

		if (mask == 0xf) {
			mask = detected;
		}

		AxisMask = detected & mask;
		if (AxisMask == 0 || AxisMask != mask) {
			Enabled = false;
			return false;
		}

		AxisCount = BitOperations.PopCount((uint)AxisMask);
		Enabled = true;
		return true;
	}

	/// <summary>
	/// <c>DVJoystick_Dtor</c> (<c>0006b470</c>)(this, 0), as <c>JoystickDevice_Dtor</c> runs it: with the GL slot held,
	/// <c>DVJoystick_Shutdown</c> (<c>0006b604</c>) clears bit 0 of <c>+0</c>, so the poll and the normalisations do
	/// nothing (docs/es1/simulation/joystick.md#construction-and-detection).
	/// </summary>
	public void Shutdown() => Enabled = false;

	/// <summary>
	/// <c>DVJoystick_Poll</c> (<c>0006b614</c>): while enabled, <c>DVJoystick_ReadRaw</c> (<c>0006be9f</c>) takes each
	/// axis in <see cref="AxisMask"/> from the port, and <c>DVJoystick_ReadButtons</c> (<c>0006bf10</c>) the buttons into
	/// <see cref="Buttons"/> and <see cref="ButtonsCopy"/> (docs/es1/simulation/joystick.md#reading-the-axes,
	/// docs/es1/simulation/joystick.md#reading-the-buttons). Retail stores <see cref="AxisCount"/> axes, which on every
	/// path that polls equals the axes in the mask; its time-out, a masked axis that never drops, has no counterpart
	/// in a port that answers at once.
	/// </summary>
	public void Poll() {
		if (!Enabled) {
			return;
		}

		for (int axis = 0; axis < 4; axis++) {
			if ((AxisMask & (1 << axis)) != 0) {
				PortValues[axis] = Port.ReadAxis(axis);
			}
		}

		Buttons = ButtonsCopy = (byte)(Port.Buttons & 0xf);
	}

	/// <summary>
	/// <c>DVJoystick_ReadAxes</c> (<c>0006b638</c>)(out): nothing while disabled. Otherwise each axis in the mask takes its
	/// port value, held to at most +128 as retail holds a count to the calibration's maximum and to nothing at the
	/// minimum; axes outside the mask keep their last value. The four values and <see cref="ButtonsCopy"/> are copied to
	/// <paramref name="buffer"/> (docs/es1/simulation/joystick.md#normalising-an-axis).
	/// </summary>
	public bool ReadAxes(Es1JoystickAxisBuffer buffer) {
		if (!Enabled) {
			return false;
		}

		for (int axis = 0; axis < 4; axis++) {
			if ((AxisMask & (1 << axis)) != 0) {
				Values[axis] = Math.Min(PortValues[axis], FullScale);
			}
		}

		CopyOut(buffer);
		return true;
	}

	/// <summary>
	/// <c>DVJoystick_ReadAxesDigital</c> (<c>0006b7bc</c>)(out): nothing while disabled. Otherwise all four axes, whatever
	/// the mask, read −1 below the center band, +1 above it and 0 within it; with the port's calibrated values, whose
	/// band is 0, that is the value's sign. Then the same copy (docs/es1/simulation/joystick.md#normalising-an-axis).
	/// </summary>
	public bool ReadAxesDigital(Es1JoystickAxisBuffer buffer) {
		if (!Enabled) {
			return false;
		}

		for (int axis = 0; axis < 4; axis++) {
			Values[axis] = Math.Sign(PortValues[axis]);
		}

		CopyOut(buffer);
		return true;
	}

	/// <summary>
	/// <c>Input_PollJoysticks</c>'s copy into a second device's object of what the first's poll read: the four counts'
	/// slots, the buttons and the buttons' copy (docs/es1/simulation/input.md#the-live-block--input_polldeviceblock-0004c2d0).
	/// </summary>
	public void CopyPollFrom(Es1Joystick first) {
		Array.Copy(first.PortValues, PortValues, 4);
		Buttons = first.Buttons;
		ButtonsCopy = first.Buttons;
	}

	private void CopyOut(Es1JoystickAxisBuffer buffer) {
		Array.Copy(Values, buffer.Values, 4);
		buffer.Buttons = ButtonsCopy;
	}
}
