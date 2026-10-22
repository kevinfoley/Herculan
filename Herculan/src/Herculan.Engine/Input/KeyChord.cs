using Silk.NET.Input;

namespace Herculan.Engine.Input;

/// <summary>The modifiers a <see cref="KeyChord"/> is pressed with.</summary>
[Flags]
public enum KeyModifiers {
	None = 0,
	Alt = 1,
	Ctrl = 2,

	/// <summary>HERCULAN's own keys only: retail strips [Shift] from every command code (SimCommandMask).</summary>
	Shift = 4,
}

/// <summary>
/// A key and the modifiers held with it: the [Alt] and [Ctrl] bits a retail command code carries over the key's
/// scancode (docs/retail/simulation/cockpit-input.md#keyboard-commands-are-scancodes). A key is retail's US-named one,
/// whatever the layout makes it (<see cref="LiveKeys.Layout"/>).
/// </summary>
public readonly record struct KeyChord(Key Key, KeyModifiers Modifiers = KeyModifiers.None) {
	public static implicit operator KeyChord(Key key) => new(key);

	public static KeyChord WithAlt(Key key) => new(key, KeyModifiers.Alt);

	public static KeyChord WithCtrl(Key key) => new(key, KeyModifiers.Ctrl);

	public static KeyChord WithShift(Key key) => new(key, KeyModifiers.Shift);

	/// <summary>The modifiers, then the key by its <see cref="Silk.NET.Input.Key"/> name: "Alt+R", "F1".</summary>
	public override string ToString() =>
		(Modifiers.HasFlag(KeyModifiers.Ctrl) ? "Ctrl+" : "")
		+ (Modifiers.HasFlag(KeyModifiers.Alt) ? "Alt+" : "")
		+ (Modifiers.HasFlag(KeyModifiers.Shift) ? "Shift+" : "")
		+ Key;
}
