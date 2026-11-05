using HercWorks.Core.Io.Transform.Dbsim;
using HercWorks.Vol.Io;

namespace HercWorks.Query;

/// <summary>
/// One simulator lip-sync script, for <c>snc</c>: its (frame, delta) pairs as <see cref="LipSyncScriptTransformer"/> reads
/// them (docs/retail/formats/snc-lip-sync.md#layout).
/// </summary>
/// <param name="Archive">The archive's file name, <c>SIMVOL0.VOL</c>.</param>
/// <param name="Length">The file's <c>int32</c> length.</param>
/// <param name="Pairs">The script's pairs.</param>
/// <param name="Unread">The bytes after the length's end: an odd length's last byte, and anything past it.</param>
internal sealed record SncScript(string Archive, string Folder, string Name, int Length,
	IReadOnlyList<SncPair> Pairs, int Unread);

/// <summary>One (frame, delta) pair of a script.</summary>
internal sealed record SncPair(byte Frame, byte Delta);

/// <summary>An <c>snc\</c> entry <see cref="LipSyncScriptTransformer"/> refuses, as the ES1 shell's own layout is.</summary>
internal sealed record SncRefused(string Archive, string Folder, string Name, string Reason);

/// <summary>What <c>snc</c> found.</summary>
internal sealed record SncReport(IReadOnlyList<SncScript> Scripts, IReadOnlyList<SncRefused> Refused);

/// <summary>
/// <c>snc</c>: every <c>.SNC</c> lip-sync script in an install's archives (<see cref="Census.Archives"/>), read through
/// HercWorks.Core, with what the players depend on: the first frame, the deltas' range, a delta of 0, and a <c>0xff</c>
/// byte, which a player reads as the terminator it appends.
/// </summary>
internal static class SncQuery {
	/// <summary>The byte <c>Snc_Load</c> appends, which a frame or delta of the same value is read as.</summary>
	private const byte Terminator = 0xff;

	/// <summary>
	/// Every <c>.SNC</c> entry of <paramref name="archivePaths"/> whose name or <c>folder\name</c> matches
	/// <paramref name="pattern"/> (as <c>vol --name</c> matches), each archive's in folder and name order.
	/// </summary>
	public static SncReport Run(IReadOnlyList<string> archivePaths, string? pattern) {
		var scripts = new List<SncScript>();
		var refused = new List<SncRefused>();
		var parser = new LipSyncScriptTransformer();
		var matches = VolQuery.Matcher(pattern);
		foreach (string path in archivePaths) {
			var vol = VolFileReader.ParseVolFile(path);
			string archive = Path.GetFileName(path).ToUpperInvariant();
			var entries = vol.FilesSet
				.Where(e => e.FileName != null)
				.Select(e => (Folder: vol.Folders.TryGetValue(e.DirIdx, out var dir) ? dir.Label : "", Name: e.FileName!.Trim(),
					Bytes: e.RawBytes))
				.Where(e => e.Name.EndsWith(".SNC", StringComparison.OrdinalIgnoreCase)
					&& (matches(e.Name) || matches(e.Folder + "\\" + e.Name)))
				.OrderBy(e => e.Folder, StringComparer.OrdinalIgnoreCase)
				.ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase);
			foreach (var (folder, name, bytes) in entries) {
				try {
					if (parser.Parse(bytes) is { } script) {
						scripts.Add(new SncScript(archive, folder, name, script.Length,
							script.Events.Select(e => new SncPair(e.Frame, e.Delta)).ToList(), script.Unread.Length));
					} else {
						refused.Add(new SncRefused(archive, folder, name, "empty"));
					}
				} catch (InvalidDataException ex) {
					refused.Add(new SncRefused(archive, folder, name, ex.Message));
				}
			}
		}

		return new SncReport(scripts, refused);
	}

	public static void Write(TextWriter o, SncReport report) {
		var scripts = report.Scripts;
		o.WriteLine($"{scripts.Count} script{(scripts.Count == 1 ? "" : "s")} read, {report.Refused.Count} refused.");
		foreach (var group in scripts.GroupBy(s => s.Archive)) {
			o.WriteLine($"  {group.Key}: {group.Count()}");
		}

		if (scripts.Count > 0) {
			var pairs = scripts.SelectMany(s => s.Pairs).ToList();
			o.WriteLine($"Pairs per script {scripts.Min(s => s.Pairs.Count)}-{scripts.Max(s => s.Pairs.Count)}; " +
				$"frames {pairs.Min(p => p.Frame)}-{pairs.Max(p => p.Frame)}; deltas {pairs.Min(p => p.Delta)}-{pairs.Max(p => p.Delta)}.");
			Census(o, "open on frame 0", scripts.Where(s => s.Pairs.Count > 0 && s.Pairs[0].Frame == 0));
			Census(o, "have no pair", scripts.Where(s => s.Pairs.Count == 0));
			Census(o, "hold a delta of 0", scripts.Where(s => s.Pairs.Any(p => p.Delta == 0)));
			Census(o, $"hold a byte 0x{Terminator:x2}", scripts.Where(s => s.Pairs.Any(p => p.Frame == Terminator || p.Delta == Terminator)));
			Census(o, "have an odd length or bytes past it", scripts.Where(s => s.Unread > 0));
			o.WriteLine("First frames: " + string.Join(", ", scripts.Where(s => s.Pairs.Count > 0)
				.GroupBy(s => s.Pairs[0].Frame).OrderBy(g => g.Key).Select(g => $"{g.Key} x{g.Count()}")) + ".");
		}

		foreach (var group in report.Refused.GroupBy(r => (r.Archive, r.Folder))) {
			var first = group.First();
			o.WriteLine($"Refused {group.Count()} in {group.Key.Archive} {group.Key.Folder}\\, such as {first.Name}: {first.Reason}");
		}

		o.WriteLine();
		foreach (var s in scripts) {
			o.WriteLine($"{s.Archive,-12}  {s.Folder}\\{s.Name,-14} {s.Length,4}  " +
				string.Join(" ", s.Pairs.Select(p => $"{p.Frame}:{p.Delta}")) + (s.Unread > 0 ? $"  +{s.Unread}" : ""));
		}
	}

	private static void Census(TextWriter o, string what, IEnumerable<SncScript> matching) {
		var list = matching.ToList();
		o.WriteLine($"{list.Count} {what}" + (list.Count is > 0 and <= 20
			? ": " + string.Join(", ", list.Select(s => s.Name)) : "") + ".");
	}
}
