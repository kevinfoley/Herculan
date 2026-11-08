namespace Herculan.Engine.Input;

/// <summary>
/// What the input layer reports a stick can do — the eight-byte block <c>Input_QueryCapabilities</c> (<c>004777f8</c>)
/// (<c>DAT_006bb72c</c>) rebuilds on every call, behind the gate on whether a stick enumerated at all
/// (docs/retail/simulation/joystick-input.md#the-capability-block--input_querycapabilities-004777f8,
/// docs/retail/simulation/joystick-input.md#a-stick-that-does-not-enumerate).
/// </summary>
/// <param name="Present">
/// Whether a stick was enumerated at all — retail's <c>Input_GetDevice(3)</c> (<c>0045c508</c>) gate.
/// False is the default.
/// </param>
/// <param name="ButtonCount">
/// How many buttons the stick reports, capped at eight — the block's <c>+2</c>, built as
/// <c>DAT_006bb438 + DAT_006bb5cc</c> clamped.
/// </param>
/// <param name="HasThrottle">The block's <c>+4</c>, from the device flags' bit 0.</param>
/// <param name="HasRudder">The block's <c>+5</c>, from bit 1.</param>
/// <param name="HasHat">The block's <c>+6</c>, from bit 4.</param>
public readonly record struct JoystickCapabilities(
	bool Present = false,
	int ButtonCount = 0,
	bool HasThrottle = false,
	bool HasRudder = false,
	bool HasHat = false) {

	/// <summary>The most buttons the block reports; it clamps to this itself.</summary>
	public const int MaxButtons = 8;

	/// <summary>
	/// No stick — what is reported with nothing attached, and what retail reports for a device it
	/// cannot enumerate.
	/// </summary>
	public static readonly JoystickCapabilities None = new();
}
