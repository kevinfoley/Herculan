using HercWorks.Core.Data.File.Dyn;
using HercWorks.Core.Io.Transform.Common;
using HercWorks.Vol.Io;

namespace HercWorks.Query;

/// <summary>One <c>.DPL</c> palette of the archives, for <c>dpl</c>: where it was found and its colors as Core reads them.</summary>
/// <param name="Archive">The archive's file name.</param>
/// <param name="Folder">The folder it lies in.</param>
/// <param name="Name">Its file name.</param>
/// <param name="Colors">Each entry's color, red in the low byte, or −1 for an entry the palette lacks.</param>
internal sealed record DplPalette(string Archive, string Folder, string Name, IReadOnlyList<int> Colors);

/// <summary>A run of consecutive entries in which two palettes differ.</summary>
/// <param name="First">The run's first entry.</param>
/// <param name="Last">The run's last entry.</param>
internal sealed record DplRun(int First, int Last);

/// <summary>One palette set against the first: the runs of entries in which the two differ.</summary>
internal sealed record DplComparison(DplPalette Palette, int Differing, IReadOnlyList<DplRun> Runs);

/// <summary>What <c>dpl</c> found: the first palette named and each other set against it.</summary>
internal sealed record DplReport(DplPalette? Base, IReadOnlyList<DplComparison> Comparisons, IReadOnlyList<string> Missing);

/// <summary>
/// <c>dpl</c>: <c>.DPL</c> palettes of an install's archives (<see cref="Census.Archives"/>) read through
/// <see cref="DynamixPaletteTransformer"/>, the first named set against each of the others entry by entry.
/// </summary>
internal static class DplQuery {
	/// <summary>
	/// The palettes the names name, each as <c>NAME</c> or <c>folder\NAME</c> with <c>.DPL</c> optional,
	/// the first archive holding it winning; the first is set against the rest.
	/// </summary>
	/// <param name="archivePaths">The archives to search.</param>
	/// <param name="names">The palette names.</param>
	public static DplReport Run(IReadOnlyList<string> archivePaths, IReadOnlyList<string> names) {
		var found = new Dictionary<string, DplPalette>(StringComparer.OrdinalIgnoreCase);
		foreach (string path in archivePaths) {
			var vol = VolFileReader.ParseVolFile(path);
			string archive = Path.GetFileName(path).ToUpperInvariant();
			foreach (var entry in vol.FilesSet) {
				if (entry.FileName?.Trim() is not { } name || !name.EndsWith(".DPL", StringComparison.OrdinalIgnoreCase)) {
					continue;
				}

				string folder = vol.Folders.TryGetValue(entry.DirIdx, out var dir) ? dir.Label : "";
				foreach (string wanted in names) {
					if (found.ContainsKey(wanted) || !Matches(wanted, folder, name)) {
						continue;
					}

					if (new DynamixPaletteTransformer().Parse(entry.RawBytes) is { } palette) {
						found[wanted] = new DplPalette(archive, folder, name, Colors(palette));
					}
				}
			}
		}

		var missing = names.Where(n => !found.ContainsKey(n)).ToList();
		if (names.Count == 0 || !found.TryGetValue(names[0], out var first)) {
			return new DplReport(null, [], missing);
		}

		var comparisons = names.Skip(1).Where(found.ContainsKey).Select(n => Compare(first, found[n])).ToList();
		return new DplReport(first, comparisons, missing);
	}

	/// <summary>Whether a wanted name names the entry <c>folder\name</c>.</summary>
	/// <param name="wanted">The wanted name.</param>
	/// <param name="folder">The entry's folder.</param>
	/// <param name="name">The entry's name.</param>
	private static bool Matches(string wanted, string folder, string name) {
		string file = wanted.EndsWith(".DPL", StringComparison.OrdinalIgnoreCase) ? wanted : wanted + ".DPL";
		int slash = file.LastIndexOf('\\');
		return slash < 0
			? string.Equals(file, name, StringComparison.OrdinalIgnoreCase)
			: string.Equals(file[(slash + 1)..], name, StringComparison.OrdinalIgnoreCase)
				&& string.Equals(file[..slash], folder, StringComparison.OrdinalIgnoreCase);
	}

	private static int[] Colors(DynamixPalette palette) {
		var colors = new int[256];
		for (int index = 0; index < colors.Length; index++) {
			colors[index] = -1;
			if (palette.Colors.TryGetValue(index, out var entry)) {
				var color = entry.GetColor();
				colors[index] = color.R | color.G << 8 | color.B << 16;
			}
		}

		return colors;
	}

	private static DplComparison Compare(DplPalette first, DplPalette other) {
		var runs = new List<DplRun>();
		int differing = 0;
		for (int index = 0; index < 256; index++) {
			if (first.Colors[index] == other.Colors[index]) {
				continue;
			}

			differing++;
			if (runs.Count > 0 && runs[^1].Last == index - 1) {
				runs[^1] = runs[^1] with { Last = index };
			} else {
				runs.Add(new DplRun(index, index));
			}
		}

		return new DplComparison(other, differing, runs);
	}

	public static void Write(TextWriter o, DplReport report) {
		foreach (string name in report.Missing) {
			o.WriteLine($"No palette {name} in the archives.");
		}

		if (report.Base is not { } first) {
			return;
		}

		o.WriteLine($"{first.Archive} {first.Folder}\\{first.Name}, set against {report.Comparisons.Count} palette" +
			$"{(report.Comparisons.Count == 1 ? "" : "s")}; colors are 8-bit r,g,b, '-' for an entry the palette lacks.");
		foreach (var comparison in report.Comparisons) {
			var other = comparison.Palette;
			o.WriteLine();
			o.WriteLine($"{other.Archive} {other.Folder}\\{other.Name}: {comparison.Differing} of 256 entries differ" +
				(comparison.Runs.Count > 0 ? $", in {comparison.Runs.Count} run{(comparison.Runs.Count == 1 ? "" : "s")}: " +
					string.Join(" ", comparison.Runs.Select(r => r.First == r.Last ? $"{r.First}" : $"{r.First}-{r.Last}")) : "."));
			foreach (var run in comparison.Runs) {
				for (int index = run.First; index <= run.Last; index++) {
					o.WriteLine($"  {index,3} (0x{index:x2}): {Format(first.Colors[index]),-11} -> {Format(other.Colors[index])}");
				}
			}
		}
	}

	private static string Format(int color) => color < 0 ? "-" : $"{color & 0xff},{color >> 8 & 0xff},{color >> 16 & 0xff}";
}
