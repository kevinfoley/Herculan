using System.Diagnostics;
using Herculan.Engine.Platform;
using Herculan.Engine.Updates;

namespace Herculan.Engine.Host.Settings;

/// <summary>
/// The "Auto check for updates" setting and the check it makes, once per run, as the front end first reaches the main
/// menu (<see cref="FrontEndWindow.OfferWelcome"/>) or when the welcome is dismissed with the box ticked. Its answer is
/// held here for the whole run, since the front end's window and menu bar are rebuilt on every shell turn, and a newer
/// release is announced once (<see cref="TakeNews"/>), by <see cref="UpdateDialog"/>. Settings' "Check for updates"
/// makes its own check (<see cref="Check"/>). What a check sends and reads is <see cref="ReleaseCheck"/>'s.
/// </summary>
static class UpdateChecks {
	private static Task<ReleaseCheckResult>? _automatic;
	private static bool _announced;
	private static bool? _automaticSetting;

	/// <summary>
	/// Where the setting is kept: <c>check-for-updates.txt</c> in <see cref="UserConfig.Folder"/>. Its presence turns the
	/// setting on; it holds nothing. Absent, as on a first start, the setting is off, so no check is made until the player
	/// asks for one.
	/// </summary>
	public static string SettingFile => UserConfig.PathOf("check-for-updates.txt");

	/// <summary>
	/// Whether the check is made automatically, read from <see cref="SettingFile"/> once a run. A failed write is logged
	/// and leaves the setting as it was.
	/// </summary>
	public static bool Automatic {
		get => _automaticSetting ??= File.Exists(SettingFile);
		set {
			if (value ? UserConfig.Write(SettingFile, "") : DeleteSettingFile()) {
				_automaticSetting = value;
			}
		}
	}

	private static bool DeleteSettingFile() {
		try {
			File.Delete(SettingFile);
			return true;
		} catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
			Console.Error.WriteLine($"Could not delete {SettingFile}: {ex.Message}");
			return false;
		}
	}

	/// <summary>This build's version.</summary>
	public static Version Current { get; } = ReleaseCheck.ParseVersion(AboutWindow.DisplayVersion()) ?? new Version(0, 0, 0, 0);

	/// <summary>Starts this run's automatic check, when the setting is on and it has not been started already.</summary>
	public static void StartAutomatic() {
		if (_automatic == null && Automatic) {
			_automatic = Check();
			_automatic.ContinueWith(task => Console.WriteLine(Describe(task.Result)), TaskScheduler.Default);
		}
	}

	/// <summary>Makes a check now. The task never faults.</summary>
	public static Task<ReleaseCheckResult> Check() => ReleaseCheck.RunAsync(Current);

	/// <summary>
	/// The automatic check's answer when it found a newer release that has not been announced yet, and marks it
	/// announced; otherwise null. Call from the frame loop.
	/// </summary>
	public static ReleaseCheckResult? TakeNews() {
		if (_announced || _automatic is not { IsCompleted: true } done || done.Result.Status != ReleaseCheckStatus.NewerAvailable) {
			return null;
		}

		_announced = true;
		return done.Result;
	}

	/// <summary>
	/// Opens a release's page in the default browser. The page is the one <see cref="ReleaseCheck"/> built under
	/// <see cref="ReleaseCheck.ReleasesPage"/>; anything else is refused.
	/// </summary>
	public static void OpenPage(string page) {
		if (!page.StartsWith(ReleaseCheck.ReleasesPage, StringComparison.Ordinal)) {
			return;
		}

		try {
			Process.Start(new ProcessStartInfo(page) { UseShellExecute = true })?.Dispose();
		} catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException) {
			Console.WriteLine($"The release page could not be opened: {e.Message}. It is {page}.");
		}
	}

	private static string Describe(ReleaseCheckResult result) => result.Status switch {
		ReleaseCheckStatus.NewerAvailable => $"Update check: HERCULAN {ReleaseCheck.Display(result.Latest!)} is available; this is {ReleaseCheck.Display(Current)}.",
		ReleaseCheckStatus.UpToDate => $"Update check: this is the latest release ({ReleaseCheck.Display(Current)}).",
		_ => $"Update check failed: {result.Error}.",
	};
}
