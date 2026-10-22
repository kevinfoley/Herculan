using System.Collections.Immutable;
using Silk.NET.Input;

namespace Herculan.Engine.Input;

/// <summary>
/// The groups a key list falls into: docs/retail/key-bindings.md's sections, in its order, then HERCULAN's own keys.
/// </summary>
public enum KeySection {
	Driving,
	Weapons,
	TargetingAndShields,
	DisplaysAndViews,
	FlashComm,
	CommandDisplay,
	DamageDetail,
	OutsideView,
	PanelsAndGame,
	Herculan,
}

/// <summary>One action's keys: its section and its chords, the first of them its primary key.</summary>
public sealed record KeyBinding(KeyAction Action, KeySection Section, ImmutableArray<KeyChord> Chords);

/// <summary>
/// Which keys do what in a mission, and in the front end the few keys it shares with one: every <see cref="KeyAction"/>
/// with its section and its chords, in display order. The host builds one instance and passes it to every key handler,
/// which takes each key it reads from here, so this is the one table the bindings are read from. <see cref="Retail"/> is
/// ES2's own.
///
/// <para>The handlers still test the modifiers themselves, as retail's command handlers match a whole command code:
/// a handler reads a chord's key, and the [Alt] or [Ctrl] it tests beside it is its own, so a chord's
/// <see cref="KeyChord.Modifiers"/> describe the binding without deciding anything. Where one action has several
/// chords with different gates (keypad [+] beside [=]), the handler reads them by position. Rebinding would first have
/// to move those tests, and that order, onto the map.</para>
/// </summary>
public sealed class KeyMap {
	private readonly KeyBinding[] _byAction;

	/// <param name="bindings">Every action once, in display order, each with at least one chord.</param>
	public KeyMap(IEnumerable<KeyBinding> bindings) {
		Bindings = bindings.ToImmutableArray();
		var actions = Enum.GetValues<KeyAction>();
		var byAction = new KeyBinding?[actions.Max(action => (int)action) + 1];
		foreach (var binding in Bindings) {
			if (binding.Chords.IsDefaultOrEmpty) {
				throw new ArgumentException($"{binding.Action} has no keys.", nameof(bindings));
			}

			if (byAction[(int)binding.Action] != null) {
				throw new ArgumentException($"{binding.Action} is bound twice.", nameof(bindings));
			}

			byAction[(int)binding.Action] = binding;
		}

		foreach (var action in actions) {
			if (byAction[(int)action] == null) {
				throw new ArgumentException($"{action} has no binding.", nameof(bindings));
			}
		}

		_byAction = byAction!;
	}

	/// <summary>ES2's own keys (<see cref="RetailKeyMap"/>).</summary>
	public static KeyMap Retail { get; } = RetailKeyMap.Build();

	/// <summary>Every action's binding, in display order.</summary>
	public ImmutableArray<KeyBinding> Bindings { get; }

	public KeySection SectionOf(KeyAction action) => _byAction[(int)action].Section;

	public ImmutableArray<KeyChord> Chords(KeyAction action) => _byAction[(int)action].Chords;

	/// <summary>The action's primary chord, its first.</summary>
	public KeyChord Chord(KeyAction action) => Chords(action)[0];

	/// <summary>The key of the action's primary chord, which is the only one for most actions.</summary>
	public Key KeyOf(KeyAction action) => Chord(action).Key;

	/// <summary>Whether any of the action's keys is down, its modifiers left to the caller.</summary>
	public bool IsDown(IKeyState keys, KeyAction action) {
		foreach (var chord in Chords(action)) {
			if (keys.IsKeyPressed(chord.Key)) {
				return true;
			}
		}

		return false;
	}

	/// <summary>Whether any of the action's keys repeats this frame (<see cref="IKeyState.IsKeyRepeated"/>).</summary>
	public bool IsRepeated(IKeyState keys, KeyAction action) {
		foreach (var chord in Chords(action)) {
			if (keys.IsKeyRepeated(chord.Key)) {
				return true;
			}
		}

		return false;
	}
}
