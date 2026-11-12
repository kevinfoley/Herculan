using Herculan.Engine.Input;
using Silk.NET.Input;

namespace Herculan.Es1.Input;

/// <summary>
/// <b>This engine's own:</b> a Custom profile's keyed actions (<see cref="Es1CustomControls.KeyActions"/>) into ES1's
/// keyboard as their keys, through <see cref="Es1HostKeyboard"/>: an action's key goes down, after its modifiers, the
/// frame an input bound to it is pressed, and up the frame the last such input is let go, so <c>Keyboard_Dispatch</c>,
/// the typematic and a recording take it as the key itself
/// (docs/es1/simulation/input.md#a-key--keyboard_int9handler-0007dde4-and-keyboard_dispatch-0007de20). A modifier two
/// held actions share stays down until neither is held.
/// </summary>
public sealed class Es1CustomKeys(Es1HostKeyboard keyboard) {
	private readonly HashSet<KeyAction> _down = [];
	private readonly HashSet<Key> _modifiers = [];

	/// <summary>Once a host frame, before the tick: presses the actions newly in <paramref name="held"/> and lets go of the rest.</summary>
	public void Update(IEnumerable<KeyAction> held) {
		var now = held.Where(action => Es1CustomControls.ChordOf(action) != null).ToHashSet();
		foreach (var action in _down.Except(now).ToList()) {
			_down.Remove(action);
			keyboard.KeyUp(Es1CustomControls.ChordOf(action)!.Value.Key);
		}

		var wanted = now.SelectMany(action => Modifiers(Es1CustomControls.ChordOf(action)!.Value)).ToHashSet();
		foreach (var modifier in _modifiers.Except(wanted).ToList()) {
			_modifiers.Remove(modifier);
			keyboard.KeyUp(modifier);
		}

		foreach (var action in now.Except(_down).ToList()) {
			var chord = Es1CustomControls.ChordOf(action)!.Value;
			foreach (var modifier in Modifiers(chord)) {
				if (_modifiers.Add(modifier)) {
					keyboard.KeyDown(modifier);
				}
			}

			_down.Add(action);
			keyboard.KeyDown(chord.Key);
		}
	}

	/// <summary>Lets go of everything held, as the mission's end or a lost focus does.</summary>
	public void Release() => Update([]);

	private static IEnumerable<Key> Modifiers(KeyChord chord) => Es1HostKeyboard.ModifierKeys(chord);
}
