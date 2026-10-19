namespace Herculan.Es1.Input;

/// <summary>
/// <b>This engine's own:</b> the stand-in for the PC's game port at <c>0x201</c> and the stick's calibration together,
/// the seam the host fills from a modern controller. Retail times each axis line's drop after a write to the port
/// (docs/es1/simulation/joystick.md#reading-the-axes) and turns the count into a signed value through the calibration
/// block (docs/es1/simulation/joystick.md#normalising-an-axis, docs/es1/simulation/joystick.md#the-calibration-file); here each axis arrives as the
/// value <c>DVJoystick_ReadAxes</c> (<c>ES1DBSIM 0006b638</c>) gives a calibrated stick under the shift 7
/// <c>Joystick_LoadCalibration</c> sets, −128 at the stick's minimum, 0 across its center band and +128 at its maximum.
/// The BIOS's joystick service (<c>DVJoystick_DetectBios</c>, <c>DVJoystick_ReadRawBios</c>), which retail tries when
/// no axis answers on the port, is a second source of the same counts and is covered by this stand-in too. A port
/// where no axis answers is the "no stick attached" case.
/// </summary>
public interface IEs1GamePort {
	/// <summary>
	/// Axis <paramref name="axis"/>, 0–3, answers detection: <c>DVJoystick_DetectGamePort</c> (<c>0006bf4d</c>) counts
	/// an axis whose line drops within 4,000 port reads (docs/es1/simulation/joystick.md#construction-and-detection).
	/// Read once, when a joystick object is initialised.
	/// </summary>
	bool AxisAnswers(int axis);

	/// <summary>
	/// Axis <paramref name="axis"/>'s calibrated value, −128 to +128; read for each axis the poll times, once a tick.
	/// Axes 0 and 1 are the stick's X and Y, 2 and 3 the port's second X and Y (a throttle, rudder or hat on the
	/// one-stick models, docs/es1/simulation/joystick.md#the-classes).
	/// </summary>
	int ReadAxis(int axis);

	/// <summary>
	/// The four buttons as <c>DVJoystick_ReadButtons</c> (<c>0006bf10</c>) stores them, bits 0–3 for port bits 4–7, a
	/// set bit for a held button (docs/es1/simulation/joystick.md#reading-the-buttons).
	/// </summary>
	byte Buttons { get; }
}
