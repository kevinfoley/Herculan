using Silk.NET.Input;

namespace Herculan.Engine.Input;

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
		foreach (var key in Scancodes.KeysOf(code & 0xff)) {
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
	/// Every scancode a recording presses: each <see cref="Scancodes.KeysOf"/> maps, less the four modifiers,
	/// which reach a command code as its Alt and Ctrl bits rather than as keys of their own.
	/// </summary>
	public static readonly int[] RecordedScancodes = Enumerable.Range(1, 0x58)
		.Where(code => code is not (0x1d or 0x2a or 0x36 or 0x38) && Scancodes.KeysOf(code).Any())
		.ToArray();
}
