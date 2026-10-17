using Herculan.Engine.Cockpit;
using Herculan.Engine.Content;
using Herculan.Engine.Editor;
using Herculan.Engine.Host.Shell;
using Herculan.Engine.Host.Simulator;
using Herculan.Engine.Platform;
using Herculan.Engine.Shell;
using Herculan.Engine.World;

namespace Herculan.Engine.Host;

/// <summary>
/// ES.EXE's part: which of the front end and the simulator runs, in what order, and what each one's exit code
/// starts the next with (docs/retail/command-line.md#the-loop).
/// </summary>
static class Launcher {
	/// <summary>
	/// Under <c>--mission</c> the one mission named runs. The codes that would bring a shell back up mean nothing
	/// with no launcher to read them, so a mission that ends normally leaves the host with 0.
	/// </summary>
	public static int RunMission(HostSession session, HostOptions options) {
		using var window = OpenWindow(session);
		var staging = new SimulatorStaging(options.StagedStart, options.StagedScreenshot, screenshotRun: options.ScreenshotPath != null);
		int missionExit = SimulatorHost.Run(session, window, options, staging, null, options.DemoTape, options.MusicTrackSelect);
		return ShellHost.ReturnsToShell(missionExit) ? 0 : missionExit;
	}

	/// <summary>
	/// The front end and the simulator in turn, each one's exit code the next one's state. Rock &amp; Roll closes
	/// the shell on 2 and hands back a mission; VIEW DEMO closes it on 5, which the launcher answers with -D, a
	/// demo tape; the mission's own code then brings the shell back up, 3 into the debrief and 6 after a demo;
	/// and 0, QUIT's, ends the run. The original spawns an executable per turn, each opening and closing a window of
	/// its own; here each is a turn in this one process's one window (<see cref="HostWindow"/>), which shows black
	/// between turns where the original showed whatever was behind it, and the mission's loading screen while a
	/// mission loads. The files between turns cross through the install's <c>data\</c>, as the original's do. The
	/// <c>--shell-*</c> staging flags stage the first turn only; each later turn starts in the mode the last one
	/// ended in. Every simulator launch is handed the count of those before it, as -R&lt;n&gt;. The Settings menu's
	/// restart is this engine's own turn: the shell again, on whatever install the session now names. So is the
	/// Editor item's: the mission editor, whose Back to game brings the shell up again as the restart does, and whose
	/// window closing ends the run as the shell's does.
	/// </summary>
	public static int RunShellLoop(HostSession session, HostOptions options) {
		using var window = OpenWindow(session);
		var shell = options.Shell;
		var staging = new SimulatorStaging(options.StagedStart, options.StagedScreenshot, screenshotRun: options.ScreenshotPath != null);
		ShellCampaignMode? shellMode = null;
		int state = ShellHost.StartupCode;
		int launches = 0;
		while (true) {
			bool firstTurn = state == ShellHost.StartupCode;
			(int shellExit, ShellLaunch? launched, shellMode) = ShellHost.Run(session, window, shell.Palette, options.ScreenshotPath,
				shellMode, firstTurn ? shell.Tab : ShellScreen.MainMenuTab, shell.Bay, firstTurn && shell.Practice,
				options.SilentAudio, options.WritePreferences, shell.Windowed, shell.Movies, state,
				missionPicker: options.DeveloperMode);
			window.Hold(null);
			// A termination signal closed the window (TerminationSignal), so it ends the run as QUIT would, whatever
			// the window was left holding.
			if (TerminationSignal.Requested) {
				return 0;
			}

			if (shellExit == ShellHost.SettingsRestartCode) {
				state = shellExit;
				continue;
			}

			if (shellExit == ShellHost.EditorExitCode) {
				var editorExit = RunEditor(session, window);
				window.Hold(null);
				if (editorExit == EditorExit.Closed || TerminationSignal.Requested) {
					return 0;
				}

				state = shellExit;
				continue;
			}

			if (launched != null) {
				state = SimulatorHost.Run(session, window, options, staging, launched, false, options.MusicTrackSelect + launches++);
			} else if (shellExit == ShellHost.DemoExitCode) {
				if (options.RecordTape != null) {
					Console.Error.WriteLine("--record cannot be combined with VIEW DEMO's tape.");
					return 1;
				}

				state = SimulatorHost.Run(session, window, options, staging, null, true, options.MusicTrackSelect + launches++);
			} else {
				return shellExit;
			}

			window.Hold(null);

			if (TerminationSignal.Requested) {
				return 0;
			}

			if (!ShellHost.ReturnsToShell(state)) {
				return state;
			}
		}
	}

	/// <summary>
	/// The mission editor's turn, on the install's <c>DATA\script.dat</c>, in the window the shell left. Returns how
	/// it ended.
	/// </summary>
	private static EditorExit RunEditor(HostSession session, HostWindow window) {
		GameContent? shellContent = null;
		return EditorHost.Run(new EditorLaunch(session.InstallRoot,
			GameContent.MountSimulator(session.InstallRoot, session.Disc),
			() => shellContent ??= GameContent.MountShell(session.InstallRoot, session.Disc),
			session.ImGuiFontPath) {
			Window = window,
			BoldFontPath = Path.Combine(Path.GetDirectoryName(session.ImGuiFontPath)!, "OpenSans-Bold.ttf"),
		});
	}

	/// <summary>The window every turn of the run takes in turn, with <c>--save-prtscn</c>'s captures attached.</summary>
	private static HostWindow OpenWindow(HostSession session) {
		var window = new HostWindow();
		if (session.SavePrintScreens) {
			PrintScreenFiles.Attach(window, session);
		}

		return window;
	}
}
