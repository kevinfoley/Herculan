using System.Text.RegularExpressions;
using HercWorks.Vol.Io;

namespace HercWorks.Query;

/// <summary>One archive entry, for <c>vol</c>.</summary>
/// <param name="Archive">The archive's file name, <c>SIMVOL0.VOL</c>.</param>
/// <param name="Size">The entry's length in bytes as stored.</param>
internal sealed record VolEntryRow(string Archive, string Folder, string Name, int Size);

/// <summary>
/// <c>vol</c>: the entries of the archives in an install's <c>VOL</c> folder (<see cref="Census.Archives"/>), by folder and
/// name, so whether the install ships a file is answered without reading it.
/// </summary>
internal static class VolQuery {
	/// <summary>
	/// The entries of <paramref name="archives"/> (named with or without <c>.VOL</c>; all when empty) whose name or
	/// <c>folder\name</c> matches <paramref name="pattern"/>: a <c>*</c>/<c>?</c> wildcard against the whole of either, else
	/// a substring of either, ignoring case; every entry when null. Each archive's entries in folder and name order.
	/// </summary>
	public static List<VolEntryRow> Run(IReadOnlyList<string> archivePaths, string? pattern) {
		var matches = Matcher(pattern);
		var rows = new List<VolEntryRow>();
		foreach (string path in archivePaths) {
			var vol = VolFileReader.ParseVolFile(path);
			string archive = Path.GetFileName(path).ToUpperInvariant();
			rows.AddRange(vol.FilesSet
				.Where(e => e.FileName != null)
				.Select(e => new VolEntryRow(archive, vol.Folders.TryGetValue(e.DirIdx, out var dir) ? dir.Label : "",
					e.FileName!.Trim(), e.RawBytes?.Length ?? 0))
				.Where(r => matches(r.Name) || matches(r.Folder + "\\" + r.Name))
				.OrderBy(r => r.Folder, StringComparer.OrdinalIgnoreCase)
				.ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase));
		}

		return rows;
	}

	public static void Write(TextWriter o, IReadOnlyList<VolEntryRow> rows, int archives, string? pattern) {
		o.WriteLine($"{rows.Count} entr{(rows.Count == 1 ? "y" : "ies")} in {archives} archive{(archives == 1 ? "" : "s")}" +
			(pattern == null ? "." : $" matching {pattern}."));
		foreach (var r in rows) {
			o.WriteLine($"{r.Archive,-12}  {r.Folder}\\{r.Name,-16}  {r.Size,8}");
		}
	}

	/// <summary>The test <see cref="Run"/> applies to a name and to a <c>folder\name</c>.</summary>
	internal static Func<string, bool> Matcher(string? pattern) {
		if (pattern == null) {
			return _ => true;
		}

		if (pattern.IndexOfAny(['*', '?']) < 0) {
			return s => s.Contains(pattern, StringComparison.OrdinalIgnoreCase);
		}

		var regex = new Regex("^" + Regex.Escape(pattern).Replace("\\*", ".*").Replace("\\?", ".") + "$", RegexOptions.IgnoreCase);
		return regex.IsMatch;
	}
}
