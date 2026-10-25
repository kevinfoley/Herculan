using HercWorks.Core.Data.File;

namespace HercWorks.Query;

/// <summary>One <c>STRINGS0.STR</c> group: its index and how many entries it holds.</summary>
internal sealed record StringGroup(int Group, int Entries);

/// <summary>One entry of a group: its text, the text's length without the NUL, and its attribute bytes.</summary>
internal sealed record StringEntry(int Index, string Text, int Length, byte[] Attributes);

/// <summary>
/// <c>strings</c>: the install's <c>STRINGS0.STR</c> as <see cref="StringFile"/> reads it
/// (docs/retail/formats/str-strings.md, docs/es1/formats/str-strings.md#the-groups): its groups, or one group's
/// entries with their attribute bytes.
/// </summary>
internal static class StringQuery {
	public static IReadOnlyList<StringGroup> Groups(StringFile strings) =>
		Enumerable.Range(0, strings.GroupCount).Select(g => new StringGroup(g, strings.Group(g).Count)).ToList();

	public static IReadOnlyList<StringEntry> Entries(StringFile strings, int group) =>
		strings.Group(group).Select((e, i) => new StringEntry(i, e.Text, e.Text.Length, e.Attributes)).ToList();

	public static void WriteGroups(TextWriter output, IReadOnlyList<StringGroup> groups) {
		output.WriteLine($"{"Group",5}  Entries");
		foreach (var g in groups) {
			output.WriteLine($"{g.Group,5}  {g.Entries,7}");
		}

		output.WriteLine($"{groups.Count} groups; --group <n> lists one group's entries with their attribute bytes.");
	}

	public static void WriteEntries(TextWriter output, int group, IReadOnlyList<StringEntry> entries) {
		output.WriteLine($"Group {group}: {entries.Count} entries");
		output.WriteLine($"{"Index",5}  {"Length",6}  {"Attributes",-16}  Text");
		foreach (var e in entries) {
			string attributes = e.Attributes.Length == 0 ? "-" : string.Join(" ", e.Attributes.Select(b => $"{b:x2}"));
			output.WriteLine($"{e.Index,5}  {e.Length,6}  {attributes,-16}  {e.Text.Replace("\n", "\\n")}");
		}

		output.WriteLine("Length: the text's characters without the NUL. Attributes: the entry's attribute bytes in hex, - for none.");
	}
}
