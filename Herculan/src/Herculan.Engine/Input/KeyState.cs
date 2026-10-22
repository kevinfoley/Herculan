using Silk.NET.Input;

namespace Herculan.Engine.Input;

/// <summary>
/// Which keys are down — all the simulator's key handling ever asks of a keyboard. Two things answer it:
/// the real device (<see cref="LiveKeys"/>) and a replaying input tape (<see cref="TapeKeys"/>), so
/// every handler reads a tape's keystrokes through exactly the code a live one takes.
/// </summary>
public interface IKeyState {
	bool IsKeyPressed(Key key);

	/// <summary>
	/// Whether <paramref name="key"/>'s auto-repeat falls due this frame, which a binding that acts on every
	/// key-down event reads beside the key's edge (<see cref="KeyLatch.PressOrRepeat"/>). Never, unless a
	/// keyboard says otherwise: a tape holds each repeat as a press of its own.
	/// </summary>
	bool IsKeyRepeated(Key key) => false;
}

/// <summary>
/// The window's own keyboard as DBSIM reads it, and the auto-repeat Windows would give it. Retail takes a key by its
/// virtual-key code through <see cref="VkToScancode"/>, so on a layout other than US every key here is the one the
/// layout makes of the physical key (<see cref="Layout"/>), and the handlers, a recording and the auto-repeat all see
/// retail's keys: on a French AZERTY keyboard the key printed A is [A].
/// </summary>
public sealed class LiveKeys : IKeyState {
	private readonly KeyRepeat _repeat = new(KeyRepeat.SystemTypematic());

	public LiveKeys(IKeyboard device) {
		Device = device;
		Positional = new PositionalKeys(this);
		Layout.Refresh();
	}

	public IKeyboard Device { get; }

	/// <summary>The layout's translation of a physical key into retail's, taken up again every frame by <see cref="AdvanceRepeat"/>.</summary>
	public KeyboardLayout Layout { get; } = new(VkToScancode.KeyOf);

	/// <summary>
	/// The same keyboard with every key in its US place, for this engine's own keys that are placed rather than
	/// named, such as the fly camera's [W] [A] [S] [D].
	/// </summary>
	public IKeyState Positional { get; }

	/// <summary>
	/// Keys the host holds down on another device's behalf, such as the [Alt] order key a gamepad's squad-order radial
	/// stands for. Each reads as held to every handler and to a recording, as the key itself would, but never
	/// auto-repeats. This engine's own.
	/// </summary>
	public HashSet<Key> HeldByHost { get; } = [];

	public bool IsKeyPressed(Key key) => Layout.IsDown(Device, key) || HeldByHost.Contains(key);

	/// <summary>Windows repeats the physical key pressed last, so the repeat is kept by physical key and read as the key it makes.</summary>
	public bool IsKeyRepeated(Key key) => _repeat.Repeated is { } physical && Layout.Translate(physical) == key;

	/// <summary>Moves the keyboard on by one host frame, before anything reads the keys: a layout switched since the last, then the auto-repeat.</summary>
	public void AdvanceRepeat(double deltaSeconds) {
		Layout.Refresh();
		_repeat.Advance(Device.IsKeyPressed, deltaSeconds);
	}

	private sealed class PositionalKeys(LiveKeys keys) : IKeyState {
		public bool IsKeyPressed(Key key) => keys.Device.IsKeyPressed(key) || keys.HeldByHost.Contains(key);

		public bool IsKeyRepeated(Key key) => keys._repeat.Repeated == key;
	}
}

/// <summary>
/// Another keyboard with one key reading as up, for a press the host has taken for itself, such as the menu bar's
/// [Shift+Esc], which nothing of the game's may see.
/// </summary>
public sealed class KeysWithout(IKeyState keys, Key hidden) : IKeyState {
	public bool IsKeyPressed(Key key) => key != hidden && keys.IsKeyPressed(key);

	public bool IsKeyRepeated(Key key) => key != hidden && keys.IsKeyRepeated(key);
}

/// <summary>
/// A tape's keystrokes, as keys held for exactly one host frame.
///
/// <para>Every key handler fires on a key's down edge, and retail acts once per key-down
/// <b>event</b> — including each auto-repeat, which is how a held <c>[=]</c> steps the weapon's power
/// several times. So each recorded press is a one-frame pulse, and playback leaves a frame with
/// nothing down between two of them for the next edge to register. The tape's own release codes
/// are therefore not needed for anything.</para>
/// </summary>
public sealed class TapeKeys : IKeyState {
	private readonly HashSet<Key> _down = new();

	public bool IsKeyPressed(Key key) => _down.Contains(key);

	/// <summary>Whether anything is down this frame.</summary>
	public bool Any => _down.Count > 0;

	/// <summary>Lets go of everything, ending the frame's pulses.</summary>
	public void Release() => _down.Clear();

	/// <summary>Presses one command code: its key, and Alt or Ctrl when the code carries them.</summary>
	public void Press(int code) {
		foreach (var key in KeysOf(code & 0xff)) {
			_down.Add(key);
		}

		if ((code & InputTapePlayer.AltBit) != 0) {
			_down.Add(Key.AltLeft);
		}

		if ((code & InputTapePlayer.CtrlBit) != 0) {
			_down.Add(Key.ControlLeft);
		}
	}

	/// <summary>
	/// Every scancode a recording presses: each <see cref="KeysOf"/> maps, less the four modifiers,
	/// which reach a command code as its Alt and Ctrl bits rather than as keys of their own.
	/// </summary>
	public static readonly int[] RecordedScancodes = Enumerable.Range(1, 0x58)
		.Where(code => code is not (0x1d or 0x2a or 0x36 or 0x38) && KeysOf(code).Any())
		.ToArray();

	/// <summary>
	/// The keys a set-1 scancode stands for. <c>VkToScancode</c> (<c>004a1104</c>) gives the arrow and
	/// editing cluster the keypad's own scancodes, so each of those codes is both keys here.
	/// </summary>
	public static IEnumerable<Key> KeysOf(int scancode) => scancode switch {
		0x01 => new[] { Key.Escape },
		>= 0x02 and <= 0x0a => new[] { Key.Number1 + (scancode - 0x02) },
		0x0b => new[] { Key.Number0 },
		0x0c => new[] { Key.Minus },
		0x0d => new[] { Key.Equal },
		0x0e => new[] { Key.Backspace },
		0x0f => new[] { Key.Tab },
		0x10 => new[] { Key.Q },
		0x11 => new[] { Key.W },
		0x12 => new[] { Key.E },
		0x13 => new[] { Key.R },
		0x14 => new[] { Key.T },
		0x15 => new[] { Key.Y },
		0x16 => new[] { Key.U },
		0x17 => new[] { Key.I },
		0x18 => new[] { Key.O },
		0x19 => new[] { Key.P },
		0x1a => new[] { Key.LeftBracket },
		0x1b => new[] { Key.RightBracket },
		0x1c => new[] { Key.Enter },
		0x1d => new[] { Key.ControlLeft },
		0x1e => new[] { Key.A },
		0x1f => new[] { Key.S },
		0x20 => new[] { Key.D },
		0x21 => new[] { Key.F },
		0x22 => new[] { Key.G },
		0x23 => new[] { Key.H },
		0x24 => new[] { Key.J },
		0x25 => new[] { Key.K },
		0x26 => new[] { Key.L },
		0x27 => new[] { Key.Semicolon },
		0x28 => new[] { Key.Apostrophe },
		0x29 => new[] { Key.GraveAccent },
		0x2a => new[] { Key.ShiftLeft },
		0x2b => new[] { Key.BackSlash },
		0x2c => new[] { Key.Z },
		0x2d => new[] { Key.X },
		0x2e => new[] { Key.C },
		0x2f => new[] { Key.V },
		0x30 => new[] { Key.B },
		0x31 => new[] { Key.N },
		0x32 => new[] { Key.M },
		0x33 => new[] { Key.Comma },
		0x34 => new[] { Key.Period },
		0x35 => new[] { Key.Slash },
		0x36 => new[] { Key.ShiftRight },
		0x37 => new[] { Key.KeypadMultiply },
		0x38 => new[] { Key.AltLeft },
		0x39 => new[] { Key.Space },
		0x3a => new[] { Key.CapsLock },
		>= 0x3b and <= 0x44 => new[] { Key.F1 + (scancode - 0x3b) },
		0x45 => new[] { Key.NumLock },
		0x46 => new[] { Key.ScrollLock },
		0x47 => new[] { Key.Keypad7, Key.Home },
		0x48 => new[] { Key.Keypad8, Key.Up },
		0x49 => new[] { Key.Keypad9, Key.PageUp },
		0x4a => new[] { Key.KeypadSubtract },
		0x4b => new[] { Key.Keypad4, Key.Left },
		0x4c => new[] { Key.Keypad5 },
		0x4d => new[] { Key.Keypad6, Key.Right },
		0x4e => new[] { Key.KeypadAdd },
		0x4f => new[] { Key.Keypad1, Key.End },
		0x50 => new[] { Key.Keypad2, Key.Down },
		0x51 => new[] { Key.Keypad3, Key.PageDown },
		0x52 => new[] { Key.Keypad0, Key.Insert },
		0x53 => new[] { Key.KeypadDecimal, Key.Delete },
		0x57 => new[] { Key.F11 },
		0x58 => new[] { Key.F12 },
		_ => Array.Empty<Key>(),
	};
}
