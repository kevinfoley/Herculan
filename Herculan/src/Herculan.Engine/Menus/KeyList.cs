using System.Collections.Immutable;
using Herculan.Engine.Input;

namespace Herculan.Engine.Menus;

/// <summary>
/// One key a <see cref="KeyListRow"/> shows: chords held together, joined by "+" (most often one), or a numbered run
/// from <see cref="Held"/>'s chord to <see cref="RunEnd"/>.
/// </summary>
public readonly record struct KeyListEntry(ImmutableArray<KeyChord> Held, KeyChord? RunEnd = null) {
	/// <summary>One chord.</summary>
	public static KeyListEntry Of(KeyChord chord) => new([chord]);

	/// <summary>Chords held together, such as the fire key with a throttle key.</summary>
	public static KeyListEntry Together(params KeyChord[] chords) => new([.. chords]);

	/// <summary>A numbered run, shown as its first and last chords.</summary>
	public static KeyListEntry Run(KeyChord first, KeyChord last) => new([first], last);

	/// <summary>Every chord the entry shows.</summary>
	public IEnumerable<KeyChord> Shown => RunEnd is { } end ? [Held[0], end] : Held;
}

/// <summary>A line of the <see cref="KeysWindow"/>: the <c>.lang</c> entry that says what it does, and its keys.</summary>
public sealed record KeyListRow(string Description, ImmutableArray<KeyListEntry> Keys);

/// <summary>A collapsible section of the <see cref="KeysWindow"/>: the <c>.lang</c> entry of its heading, and its rows.</summary>
public sealed record KeyListSection(string Heading, ImmutableArray<KeyListRow> Rows);

/// <summary>How a key list names a letter, digit or symbol key, which a keyboard layout can move.</summary>
public enum KeyNaming {
	/// <summary>
	/// By what is printed on the key that sends its virtual-key code (<see cref="KeyboardLayout.Printed"/>), for a game
	/// that reads those codes.
	/// </summary>
	VirtualKey,

	/// <summary>
	/// By what is printed on the key in its US place (<see cref="KeyboardLayout.PrintedAt"/>), for a game that reads keys
	/// by their place.
	/// </summary>
	Position,
}

/// <summary>What the <see cref="KeysWindow"/> lists, in order, and how it names the keys.</summary>
public sealed record KeyList(ImmutableArray<KeyListSection> Sections, KeyNaming Naming) {
	/// <summary>
	/// A <see cref="KeyMap"/>'s actions, laid out by <see cref="KeyRows"/> and read as virtual-key codes: a section per
	/// <see cref="KeySection"/>, in the order the map first gives each.
	/// </summary>
	public static KeyList Of(KeyMap map) {
		var sections = map.Bindings.Select(binding => binding.Section).Distinct()
			.Select(section => new KeyListSection(KeysWindow.SectionEntry(section), [.. KeyRows.All
				.Where(row => row.Section == section)
				.Select(row => new KeyListRow(row.Description, [.. row.Keys.Select(entry => EntryOf(map, entry))]))]));
		return new KeyList([.. sections], KeyNaming.VirtualKey);
	}

	/// <summary>Every chord the list shows.</summary>
	public IEnumerable<KeyChord> Chords => Sections.SelectMany(section => section.Rows).SelectMany(row => row.Keys)
		.SelectMany(entry => entry.Shown);

	private static KeyListEntry EntryOf(KeyMap map, KeyRowEntry entry) => new(
		[.. entry.Held.Select(key => ChordOf(map, key))], entry.RunEnd is { } end ? ChordOf(map, end) : null);

	private static KeyChord ChordOf(KeyMap map, KeyRef key) => map.Chords(key.Action)[key.Chord];
}

/// <summary>
/// Builds a <see cref="KeyList"/> a section at a time: <see cref="Section"/> starts one, and each <see cref="Row"/>
/// after it goes into it.
/// </summary>
public sealed class KeyListBuilder(string rowPrefix) {
	private readonly List<KeyListSection> _sections = [];
	private string? _heading;
	private readonly List<KeyListRow> _rows = [];

	/// <summary>Starts a section under the <c>.lang</c> entry <paramref name="heading"/>.</summary>
	public void Section(string heading) {
		Flush();
		_heading = heading;
	}

	/// <summary>A row whose description is the <c>.lang</c> entry the row prefix and <paramref name="id"/> make.</summary>
	public void Row(string id, params KeyListEntry[] keys) {
		if (_heading == null) {
			throw new InvalidOperationException("A row needs a section.");
		}

		_rows.Add(new KeyListRow(rowPrefix + id, [.. keys]));
	}

	public KeyList Build(KeyNaming naming) {
		Flush();
		return new KeyList([.. _sections], naming);
	}

	private void Flush() {
		if (_heading != null && _rows.Count > 0) {
			_sections.Add(new KeyListSection(_heading, [.. _rows]));
		}

		_rows.Clear();
	}
}
