using Silk.NET.Input;

namespace Herculan.Engine.Input;

/// <summary>
/// Which keys are down — all the simulator's key handling ever asks of a keyboard. The real device answers it
/// (<see cref="LiveKeys"/>), and so can a replay of recorded keystrokes, which every handler then reads through
/// exactly the code a live keyboard takes.
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
	/// stands for. Each reads as held to every handler and to a recording, as the key itself would, and auto-repeats only
	/// through <see cref="RepeatedByHost"/>. This engine's own.
	/// </summary>
	public HashSet<Key> HeldByHost { get; } = [];

	/// <summary>
	/// Keys of <see cref="HeldByHost"/> whose auto-repeat the host says falls due this frame, such as the arrow a
	/// gamepad's stick holds on the Heads-Down Display. Each reads as repeated to every handler and to a recording, as
	/// the keyboard's own repeat does. This engine's own.
	/// </summary>
	public HashSet<Key> RepeatedByHost { get; } = [];

	public bool IsKeyPressed(Key key) => Layout.IsDown(Device, key) || HeldByHost.Contains(key);

	/// <summary>Windows repeats the physical key pressed last, so the repeat is kept by physical key and read as the key it makes.</summary>
	public bool IsKeyRepeated(Key key) =>
		_repeat.Repeated is { } physical && Layout.Translate(physical) == key || RepeatedByHost.Contains(key);

	/// <summary>Moves the keyboard on by one host frame, before anything reads the keys: a layout switched since the last, then the auto-repeat.</summary>
	public void AdvanceRepeat(double deltaSeconds) {
		Layout.Refresh();
		_repeat.Advance(Device.IsKeyPressed, deltaSeconds);
	}

	private sealed class PositionalKeys(LiveKeys keys) : IKeyState {
		public bool IsKeyPressed(Key key) => keys.Device.IsKeyPressed(key) || keys.HeldByHost.Contains(key);

		public bool IsKeyRepeated(Key key) => keys._repeat.Repeated == key || keys.RepeatedByHost.Contains(key);
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
