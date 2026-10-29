using HercWorks.Core.Io.Transform.Common;
using HercWorks.Core.Io.Transform.Shell;
using HercWorks.Vol;
using HercWorks.Vol.Io;

namespace HercWorks.Query;

/// <summary>One <c>.DLG</c> record beside its text line.</summary>
/// <param name="Index">The record's position in the file, which is also the number of the <c>.BIN</c> line it reads.</param>
/// <param name="Text">That line, or null when the <c>.BIN</c> has none at the position (or there is no <c>.BIN</c>).</param>
internal sealed record MenuRecordRow(int Index, byte Kind, byte Style, byte Key, short Id, short Left, short Top, short Right, short Bottom,
	string? Text);

/// <summary>One shell menu: its <c>.DLG</c> records, or null when <c>dlg\</c> has no file for it, and its <c>.BIN</c> line count.</summary>
/// <param name="Lines">The lines of <c>eng\MENU&lt;n&gt;.BIN</c>, or null when <c>eng\</c> has no file for it.</param>
internal sealed record MenuQueryMenu(int Number, IReadOnlyList<MenuRecordRow>? Records, IReadOnlyList<string>? Lines);

/// <summary>
/// <c>menu</c> over an ES1 install: each shell menu's <c>dlg\MENU&lt;n&gt;.DLG</c> records
/// (docs/es1/formats/dlg-menu-dialogs.md#layout), each beside the line of <c>eng\MENU&lt;n&gt;.BIN</c> its position reads
/// (docs/es1/formats/dlg-menu-dialogs.md#the-text). A menu with a <c>.BIN</c> and no <c>.DLG</c> is listed with its lines.
/// </summary>
internal static class MenuQuery {
	public const string ShellArchive = "SHELL0.VOL";

	/// <summary>The menus <paramref name="numbers"/> names (all when empty), in number order; null when the install has no <see cref="ShellArchive"/>.</summary>
	public static List<MenuQueryMenu>? Run(string installRoot, IReadOnlyCollection<int> numbers) {
		if (RetailData.FindArchive(installRoot, ShellArchive) is not { } path) {
			return null;
		}

		var vol = VolFileReader.ParseVolFile(path);
		var dialogs = MenuFiles(vol, "DLG", ".DLG");
		var tables = MenuFiles(vol, "ENG", ".BIN");
		var dlgReader = new Es1MenuDialogTransformer();
		var binReader = new BinStringFileTransformer();
		return dialogs.Keys.Union(tables.Keys)
			.Where(n => numbers.Count == 0 || numbers.Contains(n))
			.Order()
			.Select(n => {
				string[]? lines = tables.TryGetValue(n, out byte[]? bin) ? binReader.Parse(bin)?.Values : null;
				var records = dialogs.TryGetValue(n, out byte[]? dlg) && dlgReader.Parse(dlg) is { } file
					? file.Controls.Select((c, i) => new MenuRecordRow(i, c.Kind, c.Style, c.Key, c.Id, c.Left, c.Top, c.Right, c.Bottom,
						lines != null && i < lines.Length ? lines[i] : null)).ToList()
					: null;
				return new MenuQueryMenu(n, records, lines);
			})
			.ToList();
	}

	public static void Write(TextWriter o, IReadOnlyList<MenuQueryMenu> menus) {
		foreach (var m in menus) {
			string bin = m.Lines == null ? "no eng\\.BIN" : $"eng\\MENU{m.Number}.BIN {m.Lines.Count} line{(m.Lines.Count == 1 ? "" : "s")}";
			if (m.Records == null) {
				o.WriteLine($"MENU{m.Number}: no dlg\\.DLG, {bin}");
				for (int i = 0; i < m.Lines!.Count; i++) {
					o.WriteLine($"  {i,3}  {Line(m.Lines[i])}");
				}

				o.WriteLine();
				continue;
			}

			o.WriteLine($"MENU{m.Number}: {m.Records.Count} record{(m.Records.Count == 1 ? "" : "s")}, {bin}");
			o.WriteLine("    #  kind                   style  key   id     rect                    text");
			foreach (var r in m.Records) {
				string rect = $"({r.Left}, {r.Top})-({r.Right}, {r.Bottom})";
				string key = r.Key == 0 ? "" : $"0x{r.Key:x2}";
				o.WriteLine($"  {r.Index,3}  {KindName(r.Kind, r.Style),-22} {r.Style,5}  {key,-4}  {r.Id,-5}  {rect,-22}  {(r.Text == null ? "-" : Line(r.Text))}");
			}

			o.WriteLine();
		}
	}

	/// <summary>What a record's kind builds, by docs/es1/formats/dlg-menu-dialogs.md#the-kinds; the number where that table names no class.</summary>
	private static string KindName(byte kind, byte style) => kind switch {
		0 => style == 0 ? "window" : style == 1 ? "window, plain" : "window, none built",
		1 => "label",
		3 => style == 1 ? "icon button" : "text button",
		4 => "label, right-aligned",
		5 => "label, centered",
		14 => "hotspot",
		15 => "list box",
		_ => $"kind {kind}",
	};

	private static string Line(string text) => $"\"{text.Replace("\n", "\\n")}\"";

	/// <summary>The archive's <c>MENU&lt;n&gt;</c> files in <paramref name="folder"/> with <paramref name="extension"/>, by n.</summary>
	private static Dictionary<int, byte[]> MenuFiles(Voln vol, string folder, string extension) {
		var files = new Dictionary<int, byte[]>();
		foreach (var e in RetailData.EntriesIn(vol, folder)) {
			string name = e.FileName!.Trim();
			if (name.StartsWith("MENU", StringComparison.OrdinalIgnoreCase)
					&& name.EndsWith(extension, StringComparison.OrdinalIgnoreCase)
					&& int.TryParse(name[4..^extension.Length], out int n) && e.RawBytes is { } bytes) {
				files[n] = bytes;
			}
		}

		return files;
	}
}
