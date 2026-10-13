using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using Herculan.Engine.Host.Localization;

namespace Herculan.Engine.Host;

/// <summary>
/// Where the host's standard output and error go. The host is built as a Windows application, so started from
/// Explorer or a shortcut it opens no console; started from a terminal, it attaches to that terminal's console so
/// <c>--help</c> and the command-line runs still print there. The terminal does not wait for a Windows application,
/// so its prompt can come back before the output ends. Everything written to either stream is copied to
/// <see cref="LogPath"/> as well, the logs of the <see cref="KeptRuns"/> runs before it kept beside it, each named
/// for when it was last written.
///
/// <para>A crash, which nothing else catches, is written to the log and shown in the operating system's error box
/// (<see cref="NativeAlert"/>), as is a startup failure the host reports through <see cref="Fail"/> when there is no
/// console to read it in.</para>
/// </summary>
static class HostLog {
	// Local rather than roaming: a log is this machine's and only good for diagnosis, so it has no business following
	// the player's profile the way the settings beside it in the roaming folder do.
	private static readonly string LogFolder = Path.Combine(
		Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.DoNotVerify),
		"Herculan");

	// Where older builds wrote the logs, with the settings. Whatever is left there is moved into LogFolder.
	private static readonly string LegacyLogFolder = Path.Combine(
		Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData, Environment.SpecialFolderOption.DoNotVerify),
		"Herculan");

	/// <summary>This run's log.</summary>
	public static readonly string LogPath = Path.Combine(LogFolder, "herculan.log");

	/// <summary>How many earlier runs' logs are kept, so a few relaunches after a crash do not lose it.</summary>
	public const int KeptRuns = 9;

	// The earlier runs' logs: herculan-<last written>.log, which sorts by time. Older builds kept one run as
	// herculan.previous.log, which is archived the same way.
	private const string ArchivePrefix = "herculan-";
	private static readonly string LegacyPreviousLogPath = Path.Combine(LegacyLogFolder, "herculan.previous.log");

	/// <summary>
	/// Whether standard output reaches someone: a terminal the host attached to, or a redirect. Always true off
	/// Windows, where an application has no console to attach to and its output goes wherever it was started from.
	/// </summary>
	public static bool HasConsole { get; private set; } = true;

	/// <summary>The strings a crash or failure box is shown in; English until the host has its table.</summary>
	public static LocalizationTable? Localization { get; set; }

	/// <summary>Attaches to the terminal, if any, opens the log, and starts catching crashes. Called first thing.</summary>
	public static void Start() {
		if (OperatingSystem.IsWindows()) {
			bool attached = AttachConsole(AttachParentProcess);
			HasConsole = attached || Console.IsOutputRedirected || Console.IsErrorRedirected;
		}

		if (OpenLog() is { } log) {
			Console.SetOut(new TeeWriter(Console.Out, log));
			Console.SetError(new TeeWriter(Console.Error, log));
		}

		AppDomain.CurrentDomain.UnhandledException += (_, e) => {
			string detail = e.ExceptionObject.ToString() ?? "";
			Console.Error.WriteLine(detail);
			string message = e.ExceptionObject is Exception ex ? ex.Message : detail;
			NativeAlert.ShowError(Text("crash.window_title", "HERCULAN Engine — Error"),
				string.Format(Text("crash.message", "HERCULAN stopped because of an error: {0} The details are in {1}."),
					message, LogPath), writeToError: false);
		};
	}

	/// <summary>
	/// Reports a failure the host is about to exit on: on standard error, and in an error box as well when there is no
	/// console to read it in.
	/// </summary>
	public static void Fail(string message) {
		if (HasConsole) {
			Console.Error.WriteLine(message);
		} else {
			NativeAlert.ShowError(Text("crash.window_title", "HERCULAN Engine — Error"), message);
		}
	}

	// The log, synchronised since both streams and every thread write to it; null when it cannot be opened, which
	// leaves the streams as they were.
	private static TextWriter? OpenLog() {
		try {
			Directory.CreateDirectory(LogFolder);
			MoveLegacyLogs();
			Archive(LogPath);
			PruneArchives();

			var stream = new FileStream(LogPath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
			return TextWriter.Synchronized(new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true });
		} catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
			Console.Error.WriteLine($"Could not open {LogPath} ({ex.Message}); there is no log for this run.");
			return null;
		}
	}

	// Renames an earlier run's log to herculan-<last written>.log, with a count after it should two runs share a second.
	private static void Archive(string path) {
		if (!File.Exists(path)) {
			return;
		}

		string stamp = File.GetLastWriteTime(path).ToString("yyyy-MM-dd-HHmmss", CultureInfo.InvariantCulture);
		string target = Path.Combine(LogFolder, $"{ArchivePrefix}{stamp}.log");
		for (int n = 2; File.Exists(target); n++) {
			target = Path.Combine(LogFolder, $"{ArchivePrefix}{stamp}-{n}.log");
		}

		File.Move(path, target);
	}

	// Moves the logs older builds left in LegacyLogFolder into LogFolder, archived, for PruneArchives to cap with the
	// rest. A log that cannot be moved stays where it is, and does not cost this run its own log.
	private static void MoveLegacyLogs() {
		if (LegacyLogFolder == LogFolder || !Directory.Exists(LegacyLogFolder)) {
			return;
		}

		try {
			foreach (string old in Directory.GetFiles(LegacyLogFolder, ArchivePrefix + "*.log")) {
				string target = Path.Combine(LogFolder, Path.GetFileName(old));
				if (!File.Exists(target)) {
					File.Move(old, target);
				}
			}

			Archive(LegacyPreviousLogPath);
			Archive(Path.Combine(LegacyLogFolder, Path.GetFileName(LogPath)));
		} catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
		}
	}

	// Deletes all but the newest KeptRuns archived logs. One that cannot be deleted is left for the next run.
	private static void PruneArchives() {
		var archives = Directory.GetFiles(LogFolder, ArchivePrefix + "*.log")
			.OrderByDescending(Path.GetFileName, StringComparer.Ordinal)
			.Skip(KeptRuns);
		foreach (string old in archives) {
			try {
				File.Delete(old);
			} catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
			}
		}
	}

	private static string Text(string key, string english) => Localization?.GetString(key) ?? english;

	private const int AttachParentProcess = -1;

	[DllImport("kernel32.dll")]
	[System.Runtime.Versioning.SupportedOSPlatform("windows")]
	private static extern bool AttachConsole(int processId);

	// Writes everything to both writers. A failed write to the log is dropped, so a full disk never stops the host.
	private sealed class TeeWriter(TextWriter console, TextWriter log) : TextWriter {
		public override Encoding Encoding => console.Encoding;

		public override void Write(char value) {
			console.Write(value);
			Log(() => log.Write(value));
		}

		public override void Write(char[] buffer, int index, int count) {
			console.Write(buffer, index, count);
			Log(() => log.Write(buffer, index, count));
		}

		public override void Write(string? value) {
			console.Write(value);
			Log(() => log.Write(value));
		}

		public override void Flush() {
			console.Flush();
			Log(log.Flush);
		}

		private static void Log(Action write) {
			try {
				write();
			} catch (IOException) {
			}
		}
	}
}
