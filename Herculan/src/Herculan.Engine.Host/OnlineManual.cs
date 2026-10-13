using HercWorks.Core.Io;
using System.Diagnostics;
using HercWorks.Help;
using HercWorks.Help.Html;
using Herculan.Engine.Content;
using Herculan.Engine.Install;

namespace Herculan.Engine.Host;

/// <summary>
/// The on-line manual: retail's <c>WinHelpA(hwnd, path, HELP_CONTENTS, 0)</c> on
/// <c>&lt;language&gt;\ES2GUIDE.HLP</c> on the disc (docs/retail/formats/winhelp.md; here through
/// <see cref="GameInstall.ReadDiscFile"/>), which current Windows cannot
/// open. The first open in a run converts the help file to one HTML page under the user's local
/// application data and hands that page to the default browser, which shows the contents topic as
/// <c>HELP_CONTENTS</c> does. The conversion and its security rules are docs/herculan/online-manual.md.
///
/// <para>The page is rewritten on the first open of every run rather than reused from an earlier
/// one, so a page left in that folder by anything else is never what opens. A run converts again when the
/// install or its language has changed since the last open.</para>
/// </summary>
internal static class OnlineManual {
	/// <summary>The help file's name inside each language folder.</summary>
	public const string FileName = RetailInstaller.ManualFileName;

	/// <summary>
	/// The readme inside each language folder, which the help file's <c>Readme</c> action opens
	/// (docs/retail/formats/winhelp.md#macros, docs/herculan/online-manual.md#the-page).
	/// </summary>
	public const string ReadmeName = "README.WRI";

	private static readonly object Gate = new();
	private static string? _page;
	private static string? _pageSource;
	private static bool _converting;

	/// <summary>
	/// Opens the manual, converting it first if this run has not for this install and language. The conversion
	/// runs off the calling thread so the window keeps drawing; failures are reported on the console.
	/// </summary>
	public static void Open(string installRoot, GameDisc? disc) {
		string source = CaseInsensitivePath.Combine(installRoot, Language(installRoot).Folder);
		lock (Gate) {
			if (_converting) {
				return;
			}

			if (_page != null && _pageSource == source) {
				Launch(_page);
				return;
			}

			_converting = true;
		}

		Task.Run(() => {
			string? page = null;
			try {
				page = Convert(installRoot, disc);
			} catch (Exception e) when (e is IOException or UnauthorizedAccessException or ObjectDisposedException
					or HercWorks.Disc.DiscFormatException) {
				Console.WriteLine($"The on-line manual could not be written: {e.Message}");
			} finally {
				lock (Gate) {
					_page = page;
					_pageSource = source;
					_converting = false;
				}
			}

			if (page != null) {
				Launch(page);
			}
		});
	}

	// Language_GetFolderName (DBSIM 0045efe0)'s mapping of data\language.cfg's first byte: F French,
	// G German, S Spanish, anything else, or no file, English. VSHELL's 004317ea knows only E, F and G
	// and asserts on any other byte or a missing file (004087b9); this follows DBSIM, since an install
	// without the file is otherwise playable. It is the file's byte, not the language the game runs in
	// (LauncherLanguage): a v1.0 install's French manual opens over an English game.
	internal static (string Folder, string Code) Language(string installRoot) => GameInstall.ReadLanguageLetter(installRoot) switch {
		(byte)'F' => ("FRENCH", "fr"),
		(byte)'G' => ("GERMAN", "de"),
		(byte)'S' => ("SPANISH", "es"),
		_ => ("ENGLISH", "en"),
	};

	private static string? Convert(string installRoot, GameDisc? disc) {
		var (folder, code) = Language(installRoot);
		string source = Path.Combine(folder, FileName);
		using var stream = GameInstall.OpenDiscFile(installRoot, disc, source);
		if (stream == null) {
			Console.WriteLine($"No {source} on the disc or in the install — the on-line manual is not installed for this language.");
			return null;
		}

		if (stream.Length > HelpLimits.Default.MaxFileBytes) {
			Console.WriteLine($"{source} is {stream.Length} bytes, over the {HelpLimits.Default.MaxFileBytes}-byte limit.");
			return null;
		}

		var bytes = new byte[stream.Length];
		stream.ReadExactly(bytes);
		if (HelpFile.Parse(bytes, out string? error) is not { } help) {
			Console.WriteLine($"{source} cannot be shown: {error}.");
			return null;
		}

		string directory = Path.Combine(
			Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.Create),
			"HERCULAN", "manual");
		Directory.CreateDirectory(directory);
		string page = Path.Combine(directory, folder + ".html");
		File.WriteAllText(page, HelpHtmlWriter.Write(help, code, Readme(CaseInsensitivePath.Combine(installRoot, folder, ReadmeName))));
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
