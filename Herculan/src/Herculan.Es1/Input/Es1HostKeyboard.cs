using System.Runtime.InteropServices;
using Herculan.Engine.Input;
using Silk.NET.Input;

namespace Herculan.Es1.Input;

/// <summary>
/// <b>This engine's own:</b> the host's stand-in for the keyboard and the <c>INT 9</c> handler
/// (<c>Keyboard_Int9Handler</c>, <c>ES1DBSIM 0007dde4</c>), which hands each byte the keyboard sends to
/// <c>Keyboard_Dispatch</c> as it arrives (docs/es1/simulation/input.md#a-key--keyboard_int9handler-0007dde4-and-keyboard_dispatch-0007de20).
///
/// <para>Each host key-down sends the key's set-1 make byte and each key-up its break byte (<c>| 0x80</c>), in the
/// order the host reports them, from the host's key events, so a press shorter than a host frame still arrives. The
/// byte is <see cref="TapeKeys.KeysOf"/> reversed, which gives the grey arrows and editing keys the keypad's codes,
/// with the other keys ES1 folds onto a plain one: keypad <c>Enter</c> and <c>/</c>, and the right <c>Ctrl</c> and
/// <c>Alt</c>. To the simulator's <c>INT 9</c> keyboard a grey key sends its plain key's byte only: ES1 dispatches the
/// <c>E0</c> prefix as a code nothing wants (input.md's "Extended keys"), so it is not sent. To the shell's <c>INT 15h</c>
/// keyboard, whose handler counts the prefix (<see cref="Es1Keyboard.Interrupt"/>), the grey key's bytes follow an
/// <c>E0</c> (<see cref="SendsExtendedPrefix"/>).</para>
///
/// <para>A held key repeats as the keyboard's typematic does: the key pressed last sends its make byte again after the
/// host's repeat delay and then at its rate (<see cref="KeyRepeat.SystemTypematic"/>), until it goes up, since ES1 is
/// not seen setting the rate (input.md's Open on the rate). The host's own repeated key-downs are ignored in its
/// place. It is not <see cref="KeyRepeat"/>, which is polled and watches only the keys <see cref="TapeKeys.KeysOf"/>
/// names.</para>
/// </summary>
public sealed class Es1HostKeyboard {
	private static readonly Dictionary<Key, byte> MakeCodes = BuildMakeCodes();

	private readonly Action<byte> _send;
	private readonly (double Delay, double Interval) _typematic;
	private readonly HashSet<Key> _held = new();
	private Key? _repeating;
	private double _untilRepeat;
	private IKeyboard? _device;

	/// <param name="send">Where each byte goes: <see cref="Es1Keyboard.Dispatch"/>.</param>
	/// <param name="typematic">The repeat delay and interval in seconds.</param>
	public Es1HostKeyboard(Action<byte> send, (double Delay, double Interval) typematic) {
		_send = send;
		_typematic = typematic;
	}

	/// <summary>
	/// Sends to <paramref name="keyboard"/>'s interrupt handler at the host's own repeat delay and rate, with the
	/// <c>E0</c> prefix when the keyboard is in <see cref="Es1Keyboard.Int15Mode"/>.
	/// </summary>
	public Es1HostKeyboard(Es1Keyboard keyboard) : this(keyboard.Interrupt, KeyRepeat.SystemTypematic()) {
		SendsExtendedPrefix = keyboard.Mode == Es1Keyboard.Int15Mode;
	}

	/// <summary>The grey keys, which a keyboard sends after an <c>E0</c>.</summary>
	private static readonly HashSet<Key> ExtendedKeys = [
		Key.Up, Key.Down, Key.Left, Key.Right, Key.Home, Key.End, Key.PageUp, Key.PageDown, Key.Insert, Key.Delete,
		Key.KeypadEnter, Key.KeypadDivide, Key.ControlRight, Key.AltRight,
	];

	/// <summary>Whether a grey key's make and break bytes each follow an <c>E0</c>.</summary>
	public bool SendsExtendedPrefix { get; init; }

	/// <summary>The set-1 make byte <paramref name="key"/> sends, or null for a key with none.</summary>
	public static byte? MakeCodeOf(Key key) => MakeCodes.TryGetValue(key, out byte code) ? code : null;

	/// <summary>Takes the device's key events until <see cref="Detach"/>.</summary>
	public void Attach(IKeyboard device) {
		Detach();
		_device = device;
		device.KeyDown += OnKeyDown;
		device.KeyUp += OnKeyUp;
	}

	public void Detach() {
		if (_device is { } device) {
			device.KeyDown -= OnKeyDown;
			device.KeyUp -= OnKeyUp;
			_device = null;
		}
	}

	/// <summary>A host key-down: the make byte, and the typematic restarted on this key. A key already down is ignored.</summary>
	public void KeyDown(Key key) {
		if (MakeCodeOf(key) is not { } make || !_held.Add(key)) {
			return;
		}

		_repeating = key;
		_untilRepeat = _typematic.Delay;
		Send(key, make);
	}

	/// <summary>A host key-up: the break byte, and the typematic stopped when this key had it.</summary>
	public void KeyUp(Key key) {
		if (MakeCodeOf(key) is not { } make || !_held.Remove(key)) {
			return;
		}

		if (_repeating == key) {
			_repeating = null;
		}

		Send(key, (byte)(make | Es1Keyboard.BreakBit));
	}

	/// <summary>One host frame of the typematic: each repeat that fell due sends the repeating key's make byte.</summary>
	public void Advance(double deltaSeconds) {
		if (_repeating is not { } key) {
			return;
		}

		byte make = MakeCodes[key];
		_untilRepeat -= deltaSeconds;
		while (_untilRepeat <= 0) {
			_untilRepeat += _typematic.Interval;
			Send(key, make);
		}
	}

	/// <summary>
	/// The shift-flag byte <c>INT 16h</c> function 2 would answer, which <c>Keyboard_SetMode</c> seeds the modifiers
	/// from: right and left <c>Shift</c> 1 and 2, <c>Ctrl</c> 4, <c>Alt</c> 8, and the <c>Scroll Lock</c>,
	/// <c>Num Lock</c>, <c>Caps Lock</c> and <c>Insert</c> toggles <c>0x10</c>, <c>0x20</c>, <c>0x40</c> and
	/// <c>0x80</c>. On Windows from <c>GetKeyState</c>; elsewhere the held keys from <paramref name="device"/> and no
	/// toggle, which the host's input does not expose.
	/// </summary>
	public static byte ReadBiosShiftFlags(IKeyboard? device) {
		if (OperatingSystem.IsWindows()) {
			return (byte)((Down(VkRightShift) ? 0x01 : 0) | (Down(VkLeftShift) ? 0x02 : 0) | (Down(VkControl) ? 0x04 : 0)
				| (Down(VkMenu) ? 0x08 : 0) | (Toggled(VkScroll) ? 0x10 : 0) | (Toggled(VkNumLock) ? 0x20 : 0)
				| (Toggled(VkCapital) ? 0x40 : 0) | (Toggled(VkInsert) ? 0x80 : 0));
		}

		if (device == null) {
			return 0;
		}

		return (byte)((device.IsKeyPressed(Key.ShiftRight) ? 0x01 : 0) | (device.IsKeyPressed(Key.ShiftLeft) ? 0x02 : 0)
			| (device.IsKeyPressed(Key.ControlLeft) || device.IsKeyPressed(Key.ControlRight) ? 0x04 : 0)
			| (device.IsKeyPressed(Key.AltLeft) || device.IsKeyPressed(Key.AltRight) ? 0x08 : 0));
	}

	// One of the key's bytes, after an E0 for a grey key when SendsExtendedPrefix is set.
	private void Send(Key key, byte value) {
		if (SendsExtendedPrefix && ExtendedKeys.Contains(key)) {
			_send(Es1Keyboard.ExtendedPrefix);
		}

		_send(value);
	}

	private void OnKeyDown(IKeyboard device, Key key, int scancode) => KeyDown(key);

	private void OnKeyUp(IKeyboard device, Key key, int scancode) => KeyUp(key);

	private static Dictionary<Key, byte> BuildMakeCodes() {
		var codes = new Dictionary<Key, byte>();
		for (int scancode = 0x01; scancode <= 0x58; scancode++) {
			foreach (var key in TapeKeys.KeysOf(scancode)) {
				codes[key] = (byte)scancode;
			}
		}

		codes[Key.KeypadEnter] = 0x1c;
		codes[Key.KeypadDivide] = 0x35;
		codes[Key.ControlRight] = 0x1d;
		codes[Key.AltRight] = 0x38;
		return codes;
	}

	private const int VkRightShift = 0xa1, VkLeftShift = 0xa0, VkControl = 0x11, VkMenu = 0x12, VkScroll = 0x91,
		VkNumLock = 0x90, VkCapital = 0x14, VkInsert = 0x2d;

	[System.Runtime.Versioning.SupportedOSPlatform("windows")]
	private static bool Down(int vk) => (GetKeyState(vk) & 0x8000) != 0;

	[System.Runtime.Versioning.SupportedOSPlatform("windows")]
	private static bool Toggled(int vk) => (GetKeyState(vk) & 1) != 0;

	[DllImport("user32.dll")]
	[System.Runtime.Versioning.SupportedOSPlatform("windows")]
	private static extern short GetKeyState(int virtualKey);
}
