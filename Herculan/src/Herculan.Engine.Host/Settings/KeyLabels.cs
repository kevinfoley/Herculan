using System.Globalization;
using System.Text;
using Herculan.Engine.Host.Localization;
using Herculan.Engine.Input;
using Silk.NET.Input;

namespace Herculan.Engine.Host.Settings;

/// <summary>
/// A key's text in a key list, built when drawn from a <see cref="KeyMap"/> chord: what is printed on the player's own
/// keyboard for a letter, digit or symbol (<see cref="KeyboardLayout.Printed"/>), and the interface language's name,
/// from the <c>keycap.*</c> entries, for a modifier or any other key. A chord's parts are joined by "+", a line's
/// chords by ", ", and a numbered run reads "first … last".
/// </summary>
sealed class KeyLabels(LocalizationTable localization, KeyboardLayout layout) {
	private const string ChordSeparator = ", ";

	/// <summary>A row's keys, as one string.</summary>
	/// <param name="runJoin">What goes between a run's first key and its last.</param>
	public string Of(KeyMap map, KeyRow row, string runJoin) {
		var text = new StringBuilder();
		foreach (var entry in row.Keys) {
			if (text.Length > 0) {
				text.Append(ChordSeparator);
			}

			if (entry.RunEnd is { } end) {
				text.Append(Of(map, entry.Held[0])).Append(runJoin).Append(Of(map, end));
				continue;
			}

			for (int i = 0; i < entry.Held.Length; i++) {
				if (i > 0) {
					text.Append('+');
				}

				text.Append(Of(map, entry.Held[i]));
			}
		}

		return text.ToString();
	}

	/// <summary>One chord: its modifiers, in the order <see cref="KeyChord.ToString"/> gives them, then its key.</summary>
	public string Of(KeyMap map, KeyRef key) {
		var chord = map.Chords(key.Action)[key.Chord];
		var text = new StringBuilder();
		foreach (var (modifier, entry) in Modifiers) {
			if (chord.Modifiers.HasFlag(modifier)) {
				text.Append(localization.GetStringOrKey(entry)).Append('+');
			}
		}

		return text.Append(Of(chord.Key)).ToString();
	}

	/// <summary>A key on its own: as printed on the keyboard when the layout can move it, else by name.</summary>
	public string Of(Key key) {
		if (layout.Printed(key) is { } printed) {
			return printed;
		}

		var (entry, inner, argument) = NameOf(key);
		string text = localization.GetStringOrKey(entry);
		return inner != null ? string.Format(text, localization.GetStringOrKey(inner))
			: argument != null ? string.Format(text, argument)
			: text;
	}

	/// <summary>Every <c>.lang</c> entry the labels of <paramref name="rows"/> are built from.</summary>
	public static IEnumerable<string> Entries(KeyMap map, IEnumerable<KeyRow> rows) {
		var chords = rows.SelectMany(row => row.Keys).SelectMany(entry => entry.Shown)
			.Select(key => map.Chords(key.Action)[key.Chord]).ToList();
		foreach (var (modifier, entry) in Modifiers) {
			if (chords.Any(chord => chord.Modifiers.HasFlag(modifier))) {
				yield return entry;
			}
		}

		foreach (var key in chords.Select(chord => chord.Key).Distinct()) {
			if (Array.IndexOf(KeyboardLayout.Movable, key) >= 0) {
				continue;
			}

			var (entry, inner, _) = NameOf(key);
			yield return entry;
			if (inner != null) {
				yield return inner;
			}
		}
	}

	private static readonly (KeyModifiers Modifier, string Entry)[] Modifiers = [
		(KeyModifiers.Ctrl, "keycap.ctrl"),
		(KeyModifiers.Alt, "keycap.alt"),
		(KeyModifiers.Shift, "keycap.shift"),
	];

	// A named key's entry, and what fills its {0}: another entry's text, or a literal. The function keys and the keypad
	// share a format each ("F{0}", "Keypad {0}"); any other key is keycap.<its Silk name in snake case>.
	private static (string Entry, string? Inner, string? Argument) NameOf(Key key) => key switch {
		>= Key.F1 and <= Key.F25 => ("keycap.function", null, (key - Key.F1 + 1).ToString(CultureInfo.InvariantCulture)),
		>= Key.Keypad0 and <= Key.Keypad9 => ("keycap.keypad", null, (key - Key.Keypad0).ToString(CultureInfo.InvariantCulture)),
		Key.KeypadDecimal => ("keycap.keypad", null, "."),
		Key.KeypadDivide => ("keycap.keypad", null, "/"),
		Key.KeypadMultiply => ("keycap.keypad", null, "*"),
		Key.KeypadSubtract => ("keycap.keypad", null, "-"),
		Key.KeypadAdd => ("keycap.keypad", null, "+"),
		Key.KeypadEqual => ("keycap.keypad", null, "="),
		Key.KeypadEnter => ("keycap.keypad", "keycap.enter", null),
		_ => ("keycap." + SnakeCase(key.ToString()), null, null),
	};

	private static string SnakeCase(string name) {
		var text = new StringBuilder(name.Length + 4);
		for (int i = 0; i < name.Length; i++) {
			if (i > 0 && char.IsUpper(name[i])) {
				text.Append('_');
			}

			text.Append(char.ToLowerInvariant(name[i]));
		}

		return text.ToString();
	}
}
