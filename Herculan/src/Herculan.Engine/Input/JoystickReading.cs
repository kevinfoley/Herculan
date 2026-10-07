using Herculan.Engine.Content;
using Herculan.Engine.Numerics;

namespace Herculan.Engine.Input;

/// <summary>
/// One frame of a joystick, in the abstract shape the binding layer consumes — four axes, a
/// four-way hat and eight buttons.
///
/// <para>This is the retail device block <c>Joystick_ReadWithResponse</c> (<c>0045c314</c>) builds at <c>DAT_004d24b0</c> and
/// <c>Input_PollDeviceBlock</c> (<c>0045ba8c</c>) copies to <c>DAT_004d2487</c> (the device struct's <c>+0x0d</c> onwards): the
/// four normalised axes at <c>+0x0d</c>/<c>+0x11</c>/<c>+0x15</c>/<c>+0x19</c>, the eight button
/// bytes at <c>+0x1d</c>..<c>+0x24</c> and the four hat bytes at <c>+0x25</c>..<c>+0x28</c>.
/// <b>Nothing in it names hardware</b>, which is the whole reason a modern stick can drive a retail
/// <c>prefs.cfg</c> unchanged — see <see cref="JoystickDeviceMap"/> for the layer that decides which
/// physical control fills which field here.</para>
///
/// <para><b>The retail axis roles are fixed.</b> <c>Joystick_Poll</c> (<c>00477614</c>) reads <c>joyGetPosEx</c>'s X and
/// Y for the stick, Z for the throttle and R for the rudder, falling back to a <i>second</i> stick's
/// X and Y when the first reports no <c>JOYCAPS_HASZ</c>/<c>HASR</c>. U and V are not even requested
/// in its <c>dwFlags</c> (<c>0xccf</c>), so a fourth and fifth analogue control has nowhere to go in
/// the original. The map is what lets this engine point them at these four fields anyway.</para>
/// </summary>
/// <param name="StickX">The stick's left/right axis, <see cref="Sim.MechControls.AxisFull"/> at full right.</param>
/// <param name="StickY">The stick's fore/aft axis, positive pulled back.</param>
/// <param name="Throttle">The throttle axis. Present only when <see cref="JoystickCapabilitiesOf"/> says so.</param>
/// <param name="Rudder">The rudder axis, positive right.</param>
/// <param name="Hat">Which of the hat's four directions are held.</param>
/// <param name="Buttons">
/// One bit per logical button, bit 0 being BUTTON 1. Only the low eight are read: the capability
/// block clamps the count to eight and the CONTROLS panel has exactly eight rows.
/// </param>
public readonly record struct JoystickReading(
		int StickX = 0, int StickY = 0, int Throttle = 0, int Rudder = 0,
		JoystickHat Hat = JoystickHat.None, byte Buttons = 0) {

	/// <summary>
	/// The width of the device layer's own reading, before the response curve:
	/// <c>Joystick_NormaliseAxes</c> (<c>00477750</c>) normalises a raw <c>joyGetPosEx</c> value as
	/// <c>(raw &lt;&lt; 8) / 0xffff - 0x80</c>, so an axis arrives spanning <c>-0x80..+0x7f</c>. The
	/// device object's resolution field (<c>+0x16</c>, set to 7 by <c>JoystickDevice_CtorBase</c> (<c>004774d0</c>)) is what fixes
	/// that width.
	///
	/// <para>It is <b>not</b> what a stick delivers to the simulation —
	/// <see cref="ApplyResponse"/> is what settles that, and it lands on
	/// <see cref="Sim.MechControls.AxisFull"/>.</para>
	/// </summary>
	public const int RawFull = 0x80;

	/// <summary>
	/// The deadzone the original applies, in <see cref="RawFull"/> units — <c>0x19</c> of
	/// <c>0x80</c>, about a fifth of the travel. It is <b>subtracted</b>, not rescaled away: the
	/// squared curve is what puts full deflection back on full scale.
	/// </summary>
	public const int Deadzone = 0x19;

	/// <summary>Nothing held, every axis centred.</summary>
	public static readonly JoystickReading Neutral = new();

	/// <summary>Whether logical button <paramref name="index"/> (0-based) is held.</summary>
	public bool Button(int index) =>
		index >= 0 && index < JoystickCapabilities.MaxButtons && (Buttons & (1 << index)) != 0;

	/// <summary>
	/// The response curve every joystick axis goes through — <c>Joystick_ReadWithResponse</c> (<c>0045c314</c>)'s arm for response
	/// mode 1, which is the mode the joystick device is built with:
	/// <c>Joystick_InitAndSeedBindings</c> (<c>00459dd4</c>) constructs it as <c>JoystickDevice_Ctor(obj, 3, 0x201, 0xf, 1)</c> (<c>0045c27c</c>), and that last
	/// argument is the field at <c>+0x28</c> the curve is selected on. (Modes 0 and 2 exist; nothing
	/// constructs a joystick with either — see docs/retail/formats/joystick-input.md.)
	///
	/// <para><b>It is a squared curve, and it is what makes a stick reach full scale.</b> The
	/// deadzone is subtracted to give <c>±103</c>, then the result is squared and divided by
	/// <c>(103² >> 8) = 41</c>, so full deflection comes out at <c>103² / 41 = 258</c> — a shade over
	/// <see cref="Sim.MechControls.AxisFull"/>, which the control laws clamp it to. Reading the
	/// deadzone as a linear rescale instead lands full deflection on <c>0x80</c>, and everything
	/// downstream is then half-scale: half the turn rate, half the throttle ramp, and a physical
	/// throttle lever that can neither idle nor open fully, because
	/// <see cref="Sim.MechObject"/>'s absolute-lever read measures <c>|axis − 0x100| × 2</c> and only
	/// spans the throttle's whole range if the axis really does reach <c>±0x100</c>.</para>
	///
	/// <para>The curve is also why a stick feels fine to fly with a deadzone this wide: the first
	/// third of the travel past the band is worth almost nothing, so the centre is soft rather than
	/// stepped.</para>
	/// </summary>
	/// <param name="raw">One axis in <see cref="RawFull"/> units, as the normalisation leaves it.</param>
	/// <param name="deadzone">The centre band, <see cref="Deadzone"/> unless the device map moved it.</param>
	public static int ApplyResponse(int raw, int deadzone = Deadzone) {
		deadzone = Math.Clamp(deadzone, 0, RawFull - 1);
		raw = Math.Clamp(raw, -RawFull, RawFull);

		int offset = raw switch {
			> 0 when raw > deadzone => raw - deadzone,
			< 0 when raw < -deadzone => raw + deadzone,
			_ => 0,
		};

		if (offset == 0) {
			return 0;
		}

		// Q16Divide(1, (span * span) >> 8) then Q16Multiply(offset * offset, that) — the original's
		// own pair, kept in the same order so the same counts fall out.
		int span = RawFull - deadzone;
		int reciprocal = SimMath.Q16Divide(1, span * span >> 8);
		int scaled = SimMath.Q16Multiply(offset * offset, reciprocal);
		return offset < 0 ? -scaled : scaled;
	}

	/// <summary>
	/// What <c>Input_QueryCapabilities</c> would report for a device shaped like this one. The
	/// CONTROLS panel greys its rows against it, so it is what makes a row live rather than blank.
	/// </summary>
	public static JoystickCapabilities JoystickCapabilitiesOf(bool present, int buttonCount,
			bool hasThrottle, bool hasRudder, bool hasHat) =>
		present
			? new JoystickCapabilities(true, Math.Clamp(buttonCount, 0, JoystickCapabilities.MaxButtons),
				hasThrottle, hasRudder, hasHat)
			: JoystickCapabilities.None;
}

/// <summary>
/// The hat's four directions, as the original keeps them: four separate bytes at the device struct's
/// <c>+0x25</c>..<c>+0x28</c>, filled by <c>Joystick_Poll</c> (<c>00477614</c>) from <c>dwPOV</c>.
///
/// <para><b>Only the four cardinals exist.</b> That function tests <c>dwPOV</c> against exactly
/// <c>0</c>, <c>9000</c>, <c>18000</c> and <c>27000</c> and drops anything else, so a hat held on a
/// diagonal reports nothing at all in retail. <see cref="JoystickDeviceMap"/> keeps that rule rather
/// than resolving a diagonal to its two components.</para>
/// </summary>
[Flags]
public enum JoystickHat {
	/// <summary>Centred.</summary>
	None = 0,

	/// <summary><c>dwPOV</c> 0, device byte <c>+0x25</c>.</summary>
	North = 1,

	/// <summary><c>dwPOV</c> 18000, device byte <c>+0x26</c>.</summary>
	South = 2,

	/// <summary><c>dwPOV</c> 27000, device byte <c>+0x27</c>.</summary>
	West = 4,

	/// <summary><c>dwPOV</c> 9000, device byte <c>+0x28</c>.</summary>
	East = 8,
}
