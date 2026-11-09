using Herculan.Engine.Platform;
using Herculan.Es2.Cockpit;
using Herculan.Es2.Host.Shell;
using Herculan.Es2.Host.Simulator;
using Herculan.Es2.Shell;
using Herculan.Es2.World;

namespace Herculan.Es2.Host;

/// <summary>
/// ES.EXE's part: which of the front end and the simulator runs, in what order, and what each one's exit code
/// starts the next with (docs/retail/command-line.md#the-loop).
/// The host opens the one window every turn takes, and builds the mission editor's turn (<see cref="EditorTurn"/>).
/// </summary>
public static class Launcher {
	/// <summary>
	/// Under <c>--mission</c> the one mission named runs. The codes that would bring a shell back up mean nothing
	/// with no launcher to read them, so a mission that ends normally leaves the host with 0.
	/// </summary>
	public static int RunMission(HostSession session, HostWindow window, Es2Options options) {
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
	/// Editor item's: the mission editor, whose Back to game brings the shell up again as the restart does, whose Play
	/// runs its mission and then brings the editor back on it, and whose window closing ends the run as the shell's does.
	/// So is Custom Missions' Play: the mission it hands over, then the shell again as after the editor, however the
	/// mission ended, with no debrief; the window closing in the mission ends the run.
	/// </summary>
	public static int RunShellLoop(HostSession session, HostWindow window, Es2Options options, EditorTurn editor) {
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
				if (!RunEditorTurns(session, window, options, editor, staging, ref launches)) {
					return 0;
				}

				state = shellExit;
				continue;
			}

			if (shellExit == ShellHost.CustomMissionExitCode) {
				if (launched != null) {
					SimulatorHost.Run(session, window, options, staging, launched, false, options.MusicTrackSelect + launches++);
					window.Hold(null);
					if (window.Closed || TerminationSignal.Requested) {
						return 0;
					}
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
	/// The mission editor's turns: the editor, and after each Play the mission it asks for, run as a shell launch is but in
	/// developer mode, then the editor again on that mission, however the mission ended. Returns whether the shell comes up next (Back to game);
	/// false when the window was closed, in the editor or in a mission, which ends the run.
	/// </summary>
	private static bool RunEditorTurns(HostSession session, HostWindow window, Es2Options options, EditorTurn editor, SimulatorStaging staging,
			ref int launches) {
		string? mission = null;
		while (true) {
			var end = editor(session, window, mission);
			window.Hold(null);
			if (end is EditorTurnEnd.Closed || TerminationSignal.Requested) {
				return false;
			}

			if (end is not EditorTurnEnd.Play play) {
				return true;
			}

			SimulatorHost.Run(session, window, options, staging, play.Launch, false, options.MusicTrackSelect + launches++, developer: true);
			window.Hold(null);
			if (window.Closed || TerminationSignal.Requested) {
				return false;
			}

			mission = play.Mission;
		}
	}
}
