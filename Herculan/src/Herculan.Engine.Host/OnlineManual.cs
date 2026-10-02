using System.Diagnostics;
using HercWorks.Help;
using HercWorks.Help.Html;

namespace Herculan.Engine.Host;

/// <summary>
/// The on-line manual: retail's <c>WinHelpA(hwnd, path, HELP_CONTENTS, 0)</c> on
/// <c>&lt;language&gt;\ES2GUIDE.HLP</c> (docs/formats/winhelp.md), which current Windows cannot
/// open. The first open in a run converts the help file to one HTML page under the user's local
/// application data and hands that page to the default browser, which shows the contents topic as
/// <c>HELP_CONTENTS</c> does. The conversion and its security rules are docs/engine/online-manual.md.
///
/// <para>The page is rewritten on the first open of every run rather than reused from an earlier
/// one, so a page left in that folder by anything else is never what opens.</para>
/// </summary>
internal static class OnlineManual {
	/// <summary>The help file's name inside each language folder.</summary>
	public const string FileName = "ES2GUIDE.HLP";

	/// <summary>
	/// The readme inside each language folder, which the help file's <c>Readme</c> action opens
	/// (docs/formats/winhelp.md#macros, docs/engine/online-manual.md#the-page).
	/// </summary>
	public const string ReadmeName = "README.WRI";

	private static readonly object Gate = new();
	private static string? _page;
	private static bool _converting;

	/// <summary>
	/// Opens the manual, converting it first if this run has not. The conversion runs off the calling
	/// thread so the window keeps drawing; failures are reported on the console.
	/// </summary>
	public static void Open(string installRoot) {
		lock (Gate) {
			if (_converting) {
				return;
			}

			if (_page != null) {
				Launch(_page);
				return;
			}

			_converting = true;
		}

		Task.Run(() => {
			string? page = null;
			try {
				page = Convert(installRoot);
			} catch (Exception e) when (e is IOException or UnauthorizedAccessException) {
				Console.WriteLine($"The on-line manual could not be written: {e.Message}");
			} finally {
				lock (Gate) {
					_page = page;
					_converting = false;
				}
			}

			if (page != null) {
				Launch(page);
			}
		});
	}

	// Language_GetFolderName (DBSIM 0045efe0) and VSHELL's 004317ea pick the folder by the first byte of
	// data\language.cfg: F French, G German, S Spanish, anything else English. A missing file reads as
	// English here; VSHELL asserts on it instead (004087b9), and an install without it is otherwise
	// playable.
	private static (string Folder, string Code) Language(string installRoot) {
		string path = Path.Combine(installRoot, "DATA", "LANGUAGE.CFG");
		int letter = -1;
		try {
			using var file = File.OpenRead(path);
			letter = file.ReadByte();
		} catch (Exception e) when (e is IOException or UnauthorizedAccessException) {
		}

		return letter switch {
			'F' => ("FRENCH", "fr"),
			'G' => ("GERMAN", "de"),
			'S' => ("SPANISH", "es"),
			_ => ("ENGLISH", "en"),
		};
	}

	private static string? Convert(string installRoot) {
		var (folder, code) = Language(installRoot);
		string source = Path.Combine(installRoot, folder, FileName);
		var info = new FileInfo(source);
		if (!info.Exists) {
			Console.WriteLine($"No {source} — the on-line manual is not installed for this language.");
			return null;
		}

		if (info.Length > HelpLimits.Default.MaxFileBytes) {
			Console.WriteLine($"{source} is {info.Length} bytes, over the {HelpLimits.Default.MaxFileBytes}-byte limit.");
			return null;
		}

		if (HelpFile.Parse(File.ReadAllBytes(source), out string? error) is not { } help) {
			Console.WriteLine($"{source} cannot be shown: {error}.");
			return null;
		}

		string directory = Path.Combine(
			Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.Create),
			"HERCULAN", "manual");
		Directory.CreateDirectory(directory);
		string page = Path.Combine(directory, folder + ".html");
		File.WriteAllText(page, HelpHtmlWriter.Write(help, code, Readme(Path.Combine(installRoot, folder, ReadmeName))));
		Console.WriteLine($"On-line manual: {source} written as {page}.");
		return page;
	}

	// The readme's text for the page, or null — and the page's note in its place — when it is missing or
	// unreadable. A bad readme never stops the manual opening.
	private static string? Readme(string path) {
		try {
			var info = new FileInfo(path);
			if (!info.Exists) {
				Console.WriteLine($"No {path} — the on-line manual's Readme shows a note.");
				return null;
			}

			if (info.Length > HelpLimits.Default.MaxReadmeBytes) {
				Console.WriteLine($"{path} is {info.Length} bytes, over the {HelpLimits.Default.MaxReadmeBytes}-byte limit.");
				return null;
			}

			if (WriteDocument.ReadText(File.ReadAllBytes(path), out string? error) is not { } text) {
				Console.WriteLine($"{path} cannot be shown: {error}.");
				return null;
			}

			return text;
		} catch (Exception e) when (e is IOException or UnauthorizedAccessException) {
			Console.WriteLine($"{path} could not be read: {e.Message}");
			return null;
		}
	}

	// The page is this class's own file at a path it built, so handing it to the shell opens it in
	// whatever the user has registered for .html — nothing the help file says is passed along.
	private static void Launch(string page) {
		try {
			Process.Start(new ProcessStartInfo(page) { UseShellExecute = true })?.Dispose();
		} catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException) {
			Console.WriteLine($"The on-line manual could not be opened: {e.Message}. It is at {page}.");
		}
	}
}
