namespace HercWorks.Core.Io;

/// <summary>
/// Paths into a retail install or disc folder, matched ignoring case as DOS and Windows match them. One folder of an
/// install mixes cases — the installer copies <c>DATA\PREFS.CFG</c>, both programs write <c>data\script.dat</c> — and
/// every reader names its file in a case of its own choosing, so on a case-sensitive file system a plain
/// <see cref="Path.Combine(string, string)"/> misses files that are there.
/// </summary>
public static class CaseInsensitivePath {
	private static readonly char[] Separators = ['\\', '/'];

	/// <summary>
	/// <paramref name="root"/> joined with <paramref name="relative"/>, each name after the root taken as the entry
	/// already there whose name matches it ignoring case, an exact match first. From the first name with no match on,
	/// the names are kept as given, so the result also serves for a file or folder about to be created.
	/// <paramref name="root"/> is taken as given. Each part is relative and may hold several names, separated by
	/// <c>\</c> or <c>/</c>.
	/// </summary>
	public static string Combine(string root, params string[] relative) {
		string path = root;
		bool matching = true;
		foreach (string part in relative) {
			foreach (string name in part.Split(Separators, StringSplitOptions.RemoveEmptyEntries)) {
				string exact = Path.Combine(path, name);
				if (matching && !Path.Exists(exact)) {
					if (Match(path, name) is { } found) {
						exact = found;
					} else {
						matching = false;
					}
				}

				path = exact;
			}
		}

		return path;
	}

	// The entry in directory named name ignoring case, or null; of two that differ only in case, the ordinal first.
	private static string? Match(string directory, string name) {
		try {
			return Directory.Exists(directory)
				? Directory.EnumerateFileSystemEntries(directory)
					.Where(entry => string.Equals(Path.GetFileName(entry), name, StringComparison.OrdinalIgnoreCase))
					.Order(StringComparer.Ordinal)
					.FirstOrDefault()
				: null;
		} catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
			return null;
		}
	}
}
