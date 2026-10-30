using HercWorks.Core.Io.Transform.Common;
using HercWorks.Vol.Io;

namespace HercWorks.Query;

/// <summary>One <c>.BIN</c> string table found in an archive, for <c>bin</c>.</summary>
/// <param name="Archive">The archive's file name, <c>SHELL0.VOL</c>.</param>
/// <param name="Count">The table's line count.</param>
/// <param name="Lines">The lines asked for (all when none were), each with its index; a null text is an index past the table's end.</param>
internal sealed record BinTable(string Archive, string Folder, string Name, int Count, IReadOnlyList<BinLine> Lines);

internal sealed record BinLine(int Index, string? Text);

/// <summary>
/// <c>bin</c>: the lines of a <c>.BIN</c> string table (<see cref="BinStringFileTransformer"/>) in any archive of an install's
/// <c>VOL</c> folder (<see cref="Census.Archives"/>), so a line a function reads by index (<c>eng\watext.bin</c> line <c>0xbe</c>)
/// is answered without a scratch reader.
/// </summary>
internal static class BinQuery {
	/// <summary>
	/// Every entry of <paramref name="archivePaths"/> named <paramref name="file"/> (<c>name</c> or <c>folder\name</c>, ignoring
	/// case; <c>.BIN</c> added when it has no extension), with the lines <paramref name="indexes"/> names, or all of them when it
	/// names none.
	/// </summary>
	public static List<BinTable> Run(IReadOnlyList<string> archivePaths, string file, IReadOnlyList<int> indexes) {
		if (Path.GetExtension(file).Length == 0) {
			file += ".BIN";
		}

		var tables = new List<BinTable>();
		foreach (string path in archivePaths) {
			var vol = VolFileReader.ParseVolFile(path);
			foreach (var e in vol.FilesSet.Where(e => e.FileName != null)) {
				string name = e.FileName!.Trim();
				string folder = vol.Folders.TryGetValue(e.DirIdx, out var dir) ? dir.Label : "";
				if (!string.Equals(name, file, StringComparison.OrdinalIgnoreCase)
						&& !string.Equals(folder + "\\" + name, file, StringComparison.OrdinalIgnoreCase)) {
					continue;
				}

				string[] lines = (e.RawBytes is { } bytes ? Lines(name, bytes) : null)
					?? throw new InvalidDataException($"{folder}\\{name} in {Path.GetFileName(path)} is not a .BIN string table.");
				var picked = indexes.Count == 0
					? lines.Select((text, i) => new BinLine(i, text))
					: indexes.Select(i => new BinLine(i, i >= 0 && i < lines.Length ? lines[i] : null));
				tables.Add(new BinTable(Path.GetFileName(path).ToUpperInvariant(), folder, name, lines.Length, picked.ToList()));
			}
		}

		return tables;
	}

	/// <summary>
	/// The file's lines through the reader the registry binds to its name: <see cref="Es1DialogTextTransformer"/> for ES1's
	/// <c>*_TEXT.BIN</c> talking-head text, which puts a version word before the table, else
	/// <see cref="BinStringFileTransformer"/>; null when the bytes are not that layout.
	/// </summary>
	private static string[]? Lines(string name, byte[] bytes) {
		try {
			return name.EndsWith("_TEXT.BIN", StringComparison.OrdinalIgnoreCase)
				? new Es1DialogTextTransformer().Parse(bytes)?.Lines
				: new BinStringFileTransformer().Parse(bytes)?.Values;
		} catch (Exception ex) when (ex is InvalidDataException or ArgumentException or IndexOutOfRangeException) {
			return null;
		}
	}

	public static void Write(TextWriter o, IReadOnlyList<BinTable> tables) {
		foreach (var t in tables) {
			o.WriteLine($"{t.Archive} {t.Folder}\\{t.Name}: {t.Count} line{(t.Count == 1 ? "" : "s")}");
			foreach (var l in t.Lines) {
				string text = l.Text == null ? "(past the end)" : $"\"{l.Text.Replace("\n", "\\n")}\"";
				o.WriteLine($"  {l.Index,4}  0x{l.Index:x3}  {text}");
			}

			o.WriteLine();
		}
	}

	/// <summary>A line index as the docs write one, <c>0xbe</c>, or in decimal; null when it is neither.</summary>
	public static int? ParseIndex(string s) =>
		s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
			? int.TryParse(s[2..], System.Globalization.NumberStyles.HexNumber, null, out int hex) ? hex : null
			: int.TryParse(s, out int dec) ? dec : null;
}
