namespace HercWorks.Core.Data.File;

/// <summary>
/// FILE - .STR — a string table (<c>simvol0\str\*.STR</c>, <c>LANG0\ENG\CAMPAIGN.STR</c>,
/// <c>tapes\demolist.str</c>, <c>data\mission.str</c>): an <c>int32</c> content length, then groups of
/// <c>{ int16 count; count x { int16 length incl. NUL; text; uint8 attributeCount; attributes } }</c>
/// until the content runs out. See docs/retail/formats/str-strings.md#layout.
///
/// <para>A group's index is its position in the file, which is how the simulator registers them
/// (<c>SimStrings_LoadAll</c>, <c>00437598</c>) and how <see cref="Group"/> hands them out.</para>
/// </summary>
public class StringFile {
	/// <summary>The groups, in file order.</summary>
	public Entry[][] Groups { get; set; } = [];

	/// <summary>One string and its attribute bytes (empty when it has none).</summary>
	public readonly record struct Entry(string Text, byte[] Attributes);

	/// <summary>How many groups the file held.</summary>
	public int GroupCount => Groups.Length;

	/// <summary>
	/// Group <paramref name="index"/>'s strings, or an empty list when the file had no such group —
	/// a caller drawing text is better off drawing none than an invented caption.
	/// </summary>
	public IReadOnlyList<Entry> Group(int index) =>
		index >= 0 && index < Groups.Length ? Groups[index] : Array.Empty<Entry>();

	/// <summary>String <paramref name="index"/> of <paramref name="group"/>, or null when either index is out of range.</summary>
	public string? Text(int group, int index) {
		var entries = Group(group);
		return index >= 0 && index < entries.Count ? entries[index].Text : null;
	}
}
