using Silk.NET.Input;

namespace Herculan.Es1.Input;

/// <summary>
/// <b>This engine's own:</b> [Alt+Enter], either <c>Enter</c> with either <c>Alt</c> held and no <c>Ctrl</c>, toggles the
/// window between full screen and a window as the key goes down. ES1 is a DOS program and has no such key; it is the key
/// Earthsiege 2's toggle answers. The <c>Enter</c> it takes stays the toggle's until it goes up: the game sees neither its
/// press, the host's repeats of it, nor its release, so a panel or dialog that <c>Enter</c> closes stays up. The <c>Alt</c>
/// is the game's as any other key is.
/// </summary>
public sealed class Es1FullScreenKey {
	private readonly Action _toggle;

	// The Enter key the toggle took, until it goes up.
	private Key? _down;

	/// <param name="toggle">The window's full-screen toggle.</param>
	public Es1FullScreenKey(Action toggle) => _toggle = toggle;

	/// <summary>
	/// A host key-down, <paramref name="held"/> answering whether a modifier key is down. True when the key is the
	/// toggle's, which then toggled unless this is the host's repeat of the key it already took.
	/// </summary>
	public bool KeyDown(Key key, Func<Key, bool> held) {
		if (key == _down) {
			return true;
		}

		if (key is not (Key.Enter or Key.KeypadEnter) || !(held(Key.AltLeft) || held(Key.AltRight))
				|| held(Key.ControlLeft) || held(Key.ControlRight)) {
			return false;
		}

		_down = key;
		_toggle();
		return true;
	}

	/// <summary>A host key-up: true when it is the toggle's <c>Enter</c> going up.</summary>
	public bool KeyUp(Key key) {
		if (key != _down) {
			return false;
		}

		_down = null;
		return true;
	}

	/// <summary>Forgets the key the toggle took, as when the keyboard is detached.</summary>
	public void Reset() => _down = null;
}
