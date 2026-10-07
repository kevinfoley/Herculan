using Herculan.Engine.Sim;
using Silk.NET.Input;

namespace Herculan.Engine.Input;

/// <summary>The modifier and axis readings every key handler shares, over whichever <see cref="IKeyState"/> it reads.</summary>
public static class KeyChords {
	public static bool CtrlHeld(IKeyState keyboard) =>
		keyboard.IsKeyPressed(Key.ControlLeft) || keyboard.IsKeyPressed(Key.ControlRight);

	public static bool AltHeld(IKeyState keyboard) =>
		keyboard.IsKeyPressed(Key.AltLeft) || keyboard.IsKeyPressed(Key.AltRight);

	// Neither [Alt] nor [Ctrl] held: the key arrives as its bare scancode, the code the cockpit's [Enter] and
	// [Tab] cases and an alert panel's [Enter] and [Esc] match exactly. With [Alt] or [Ctrl] it is another
	// code (0x21c is [Alt+Enter]), or Key_WndProcHook keeps it for itself (0x20f, 0x201, 0x401). [Shift] is
	// not tested: SimCommandMask strips it from the cockpit's commands.
	public static bool Unmodified(IKeyState keyboard) => !AltHeld(keyboard) && !CtrlHeld(keyboard);

	// One signed axis from a pair of keys, plus optional aliases for each direction — the arrow cluster
	// and the numeric keypad are the same key on the hardware the manual is describing, and a host window
	// sees them as two.
	public static int Axis(IKeyState keyboard, Key positive, Key negative,
			Key? positiveAlias = null, Key? negativeAlias = null) {
		bool up = keyboard.IsKeyPressed(positive) || (positiveAlias is { } p && keyboard.IsKeyPressed(p));
		bool down = keyboard.IsKeyPressed(negative) || (negativeAlias is { } n && keyboard.IsKeyPressed(n));
		return (up ? 1 : 0) - (down ? 1 : 0);
	}

	/// <summary>The observer camera's keys: this engine's own fly camera, not anything of the original's.</summary>
	public static CameraInput FlyCameraInput(IKeyState? keyboard) {
		if (keyboard == null) {
			return default;
		}

		return new CameraInput {
			Forward = Axis(keyboard, Key.W, Key.S),
			Strafe = Axis(keyboard, Key.D, Key.A),
			Vertical = Axis(keyboard, Key.R, Key.F),
			Yaw = Axis(keyboard, Key.Right, Key.Left),
			Pitch = Axis(keyboard, Key.Up, Key.Down),
			Boost = keyboard.IsKeyPressed(Key.ShiftLeft) || keyboard.IsKeyPressed(Key.ShiftRight),
		};
	}
}

/// <summary>
/// One binding's held state, so its handler acts on the key going down rather than on every frame it is
/// held — the original dispatches a command per key-down event. Each binding keeps its own latch even
/// where two share a key, because each is refreshed under its own conditions.
/// </summary>
public sealed class KeyLatch {
	private bool _held;

	/// <summary>Records <paramref name="down"/> and says whether it is a fresh press.</summary>
	public bool Press(bool down) {
		bool edge = down && !_held;
		_held = down;
		return edge;
	}

	/// <summary>Reads <paramref name="key"/> off <paramref name="keys"/> and says whether it is a fresh press.</summary>
	public bool Press(IKeyState keys, Key key) => Press(keys.IsKeyPressed(key));

	/// <summary>Forgets the key, so one still held reads as a fresh press next time.</summary>
	public void Reset() => _held = false;
}
