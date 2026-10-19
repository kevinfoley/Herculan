namespace Herculan.Es1.Input;

/// <summary>
/// The simulator's keyboard as the input build reads it: the keyboard <c>Input_BuildSourceTable</c>
/// (<c>ES1DBSIM 0004c95c</c>) sets up, with its two subscribers, the command queue and the axis keys
/// (docs/es1/simulation/input.md#the-simulators-two-subscribers). The host's key events reach it through
/// <see cref="Es1HostKeyboard"/>.
/// </summary>
public sealed class Es1KeyboardInput {
	/// <summary>
	/// <c>KeyQueueWantedCodes</c> (<c>0009581c</c>), the 78 make codes the queue takes: <c>Esc</c> to <c>]</c>,
	/// <c>Enter</c>, <c>A</c> to <c>`</c>, <c>\</c> to <c>/</c>, keypad <c>*</c>, <c>Caps Lock</c>, <c>F1</c> to
	/// <c>F10</c>, the keypad's thirteen keys, <c>F11</c> and <c>F12</c>.
	/// </summary>
	private static readonly byte[] KeyQueueWantedCodes = [
		.. Range(0x01, 0x1c), .. Range(0x1e, 0x29), .. Range(0x2b, 0x35), 0x37, 0x3a, .. Range(0x3b, 0x44),
		.. Range(0x47, 0x53), 0x57, 0x58,
	];

	/// <summary>
	/// <c>Input_BuildSourceTable</c>'s keyboard: <c>KeyboardInputDevice_Ctor</c>(0, <c>0x10000</c>), the queue's codes
	/// (<c>Keyboard_AddSubscriberCodes</c>), then <c>SimInput_SetEnabled</c>(1), so a code on both lists is offered to
	/// the axis keys first.
	/// </summary>
	/// <param name="biosShiftFlags">The host's lock and shift state, <see cref="Es1HostKeyboard.ReadBiosShiftFlags"/>.</param>
	public Es1KeyboardInput(byte biosShiftFlags) {
		Keyboard = new Es1Keyboard(biosShiftFlags);
		Keyboard.AddSubscriberCodes(Keyboard.QueueSubscriber, KeyQueueWantedCodes);
		AxisKeys = new Es1AxisKeys(Keyboard);
		AxisKeys.SetEnabled(true);
	}

	public Es1Keyboard Keyboard { get; }

	public Es1AxisKeys AxisKeys { get; }

	/// <summary>
	/// The next command word, <c>KeyQueue_Pop</c> (<c>0006c1f8</c>) as <c>Input_ReadDeviceEvent</c> (<c>0004b51c</c>)
	/// reads it in mode <c>0x10000</c>: 0 when the queue is empty. The device's drain flag, which
	/// <c>KeyboardInputDevice_Ctor</c> clears, never empties it (docs/es1/simulation/input.md#the-command-word).
	/// </summary>
	public ushort PopCommand() => Keyboard.Queue.Pop();

	/// <summary><c>Input_BuildKeyboardAxes</c>(slow, <c>000b188c</c>).</summary>
	public Es1KeyboardAxesBlock BuildKeyboardAxes(bool slow) => AxisKeys.BuildKeyboardAxes(slow);

	/// <summary>Clears the three key-state blocks.</summary>
	public void ResetKeyState() => AxisKeys.ResetKeyState();

	private static IEnumerable<byte> Range(int first, int last) =>
		Enumerable.Range(first, last - first + 1).Select(code => (byte)code);
}
