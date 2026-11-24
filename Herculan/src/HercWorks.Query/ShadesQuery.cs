using HercWorks.Core.Io.Transform.Common;
using HercWorks.Core.Io.Transform.Dbsim;
using HercWorks.Vol.Io;

namespace HercWorks.Query;

/// <summary>
/// One <c>dat\SHADES.DAT</c> list, for <c>shades</c>: a type tag's <c>{ramp, column}</c> pairs as
/// <see cref="WorldShadesTransformer"/> reads them (docs/es1/rendering/polygon-shading.md#the-shades-file).
/// </summary>
/// <param name="Tag">The type tag.</param>
/// <param name="Pairs">The pairs in file order.</param>
/// <param name="DistinctRamps">How many different ramps the pairs name.</param>
internal sealed record ShadesList(byte Tag, IReadOnlyList<ShadesPair> Pairs, int DistinctRamps);

/// <summary>One pair of a list: the ramp re-pointed and the range column it takes.</summary>
internal sealed record ShadesPair(byte Ramp, byte Column);

/// <summary>One archive's <c>SHADES.DAT</c>.</summary>
/// <param name="Archive">The archive's file name.</param>
/// <param name="TagCapacity">The file's first byte: how many tags the load's table has room for.</param>
/// <param name="Lists">The lists in file order.</param>
internal sealed record ShadesFile(string Archive, string Folder, string Name, byte TagCapacity, IReadOnlyList<ShadesList> Lists);

/// <summary>
/// One <c>.DPL</c> palette's shade ramps, for <c>shades</c>, as <see cref="DynamixPaletteTransformer"/> reads them: how
/// many, and each that is not a run, a ramp whose entries are not each one more than the last.
/// </summary>
/// <param name="Ramps">How many ramps the palette carries.</param>
/// <param name="Empty">The ramps with no entry.</param>
/// <param name="NotRuns">Each non-empty ramp whose entries are not a consecutive ascending run: its number and entries.</param>
internal sealed record ShadesPalette(string Archive, string Folder, string Name, int Ramps, IReadOnlyList<int> Empty,
	IReadOnlyList<ShadesRamp> NotRuns);

/// <summary>A ramp's number and entries.</summary>
internal sealed record ShadesRamp(int Ramp, IReadOnlyList<short> Entries);

/// <summary>What <c>shades</c> found.</summary>
internal sealed record ShadesReport(IReadOnlyList<ShadesFile> Files, IReadOnlyList<ShadesPalette> Palettes,
	IReadOnlyList<SncRefused> Refused);

/// <summary>
/// <c>shades</c>: every <c>SHADES.DAT</c> and every <c>.DPL</c> palette in an install's archives
/// (<see cref="Census.Archives"/>), read through HercWorks.Core: each list's pairs, and each palette's ramps that are
/// not consecutive runs, which are the two things a per-object ramp state needs to know about the data.
/// </summary>
internal static class ShadesQuery {
	/// <summary>Every <c>SHADES.DAT</c> and <c>.DPL</c> of <paramref name="archivePaths"/>, each archive's in folder and name order.</summary>
	public static ShadesReport Run(IReadOnlyList<string> archivePaths) {
		var files = new List<ShadesFile>();
		var palettes = new List<ShadesPalette>();
		var refused = new List<SncRefused>();
		foreach (string path in archivePaths) {
			var vol = VolFileReader.ParseVolFile(path);
			string archive = Path.GetFileName(path).ToUpperInvariant();
			var entries = vol.FilesSet
				.Where(e => e.FileName != null)
				.Select(e => (Folder: vol.Folders.TryGetValue(e.DirIdx, out var dir) ? dir.Label : "", Name: e.FileName!.Trim(),
					Bytes: e.RawBytes))
				.OrderBy(e => e.Folder, StringComparer.OrdinalIgnoreCase)
				.ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase);
			foreach (var (folder, name, bytes) in entries) {
				try {
					if (string.Equals(name, "SHADES.DAT", StringComparison.OrdinalIgnoreCase)) {
						if (new WorldShadesTransformer().Parse(bytes) is not { } shades) {
							refused.Add(new SncRefused(archive, folder, name, "empty"));
							continue;
						}

						files.Add(new ShadesFile(archive, folder, name, shades.TagCapacity, shades.Lists
							.Select(l => new ShadesList(l.Tag, l.Pairs.Select(p => new ShadesPair(p.Ramp, p.Column)).ToList(),
								l.Pairs.Select(p => p.Ramp).Distinct().Count()))
							.ToList()));
					} else if (name.EndsWith(".DPL", StringComparison.OrdinalIgnoreCase)) {
						if (new DynamixPaletteTransformer().Parse(bytes)?.ShadeRamps is not { } ramps) {
							refused.Add(new SncRefused(archive, folder, name, "empty"));
							continue;
						}

						palettes.Add(new ShadesPalette(archive, folder, name, ramps.Count,
							Enumerable.Range(0, ramps.Count).Where(r => ramps[r].Length == 0).ToList(),
							Enumerable.Range(0, ramps.Count)
								.Where(r => ramps[r].Length > 0 && !IsRun(ramps[r]))
								.Select(r => new ShadesRamp(r, ramps[r]))
								.ToList()));
					}
				} catch (InvalidDataException ex) {
					refused.Add(new SncRefused(archive, folder, name, ex.Message));
				}
			}
		}

		return new ShadesReport(files, palettes, refused);
	}

	/// <summary>Whether each entry of <paramref name="entries"/> is one more than the one before it.</summary>
	private static bool IsRun(short[] entries) {
		for (int i = 1; i < entries.Length; i++) {
			if (entries[i] != entries[i - 1] + 1) {
				return false;
			}
		}

		return true;
	}

	public static void Write(TextWriter o, ShadesReport report) {
		o.WriteLine($"{report.Files.Count} SHADES.DAT, {report.Palettes.Count} palette{(report.Palettes.Count == 1 ? "" : "s")}, " +
			$"{report.Refused.Count} refused.");
		foreach (var file in report.Files) {
			o.WriteLine();
			o.WriteLine($"{file.Archive} {file.Folder}\\{file.Name}: room for {file.TagCapacity} tags, {file.Lists.Count} lists; " +
				$"most pairs in a list {(file.Lists.Count > 0 ? file.Lists.Max(l => l.Pairs.Count) : 0)}, " +
				$"most distinct ramps {(file.Lists.Count > 0 ? file.Lists.Max(l => l.DistinctRamps) : 0)}.");
			foreach (var list in file.Lists) {
				o.WriteLine($"  tag {list.Tag,3}: {list.Pairs.Count,2} pairs, {list.DistinctRamps,2} ramps  " +
					string.Join(" ", list.Pairs.Select(p => $"{p.Ramp}:{p.Column}")));
			}
		}

		if (report.Palettes.Count > 0) {
			o.WriteLine();
			var withRamps = report.Palettes.Where(p => p.Ramps > 0).ToList();
			o.WriteLine($"{withRamps.Count} palettes carry ramps; ramp counts " +
				string.Join(", ", withRamps.GroupBy(p => p.Ramps).OrderBy(g => g.Key).Select(g => $"{g.Key} x{g.Count()}")) + "; " +
				$"{withRamps.Count(p => p.NotRuns.Count == 0)} have every non-empty ramp a consecutive run.");
			foreach (var palette in withRamps) {
				o.WriteLine($"  {palette.Archive,-12}  {palette.Folder}\\{palette.Name,-14} {palette.Ramps,3} ramps" +
					(palette.Empty.Count > 0 ? $", empty: {string.Join(" ", palette.Empty)}" : "") +
					(palette.NotRuns.Count > 0 ? $", not runs: {palette.NotRuns.Count}" : ""));
				foreach (var ramp in palette.NotRuns) {
					o.WriteLine($"      ramp {ramp.Ramp,3}: {string.Join(" ", ramp.Entries)}");
				}
			}
		}

		foreach (var r in report.Refused) {
			o.WriteLine($"Refused {r.Archive} {r.Folder}\\{r.Name}: {r.Reason}");
		}
	}
}
