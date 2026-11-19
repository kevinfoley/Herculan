using Herculan.Engine.Install;
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
	/// Runs the front end and the simulator in turn, as <c>ES.EXE</c> does, with each turn's exit code choosing the next
	/// turn and the state it starts in (docs/retail/command-line.md#the-loop). Each simulator launch is given the count of
	/// launches before it, as retail's <c>-R&lt;n&gt;</c> is. Returns the exit code that ended the run.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Retail spawns an executable per turn, each with a window of its own. Here every turn runs in the one
	/// <see cref="HostWindow"/>, which shows black between turns where retail showed whatever was behind it, and the
	/// mission's loading screen while a mission loads. Files pass between turns through the install's <c>data\</c> folder,
	/// as retail's do.
	/// </para>
	/// <para>
	/// <c>--shell-tab</c> and <c>--shell-practice</c> stage the first shell turn only; the other <c>--shell-*</c> flags
	/// apply to every shell turn. Each later shell turn starts in the campaign mode the previous one ended in.
	/// </para>
	/// <para>
	/// Three shell exits are this engine's own, outside retail's codes:
	/// </para>
	/// <list type="bullet">
	/// <item><see cref="ShellHost.SettingsRestartCode"/>: the shell runs again, on whatever install the session now names.</item>
	/// <item>
	/// <see cref="ShellHost.EditorExitCode"/>: the mission editor runs. Its "Back to game" brings the shell back up; its
	/// "Play" runs the mission and then reopens the editor on it.
	/// </item>
	/// <item>
	/// <see cref="ShellHost.CustomMissionExitCode"/>: the mission "Custom Missions" hands over runs, and the shell comes back
	/// up however the mission ended, with no debrief.
	/// </item>
	/// </list>
	/// <para>
	/// Closing the window ends the run with 0 in the shell, in the editor, and in a mission that the editor's "Play" or
	/// "Custom Missions" started; so does a termination signal after any turn. The
	/// "Game" menu's "Switch to" ends it with 0 after the shell's turn, leaving the window open for the other game
	/// (<see cref="GameSwitch"/>). "VIEW DEMO" with <c>--record</c> given ends it with 1.
	/// </para>
	/// </remarks>
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
			// the window was left holding; so does the "Game" menu's "Switch to" (GameSwitch), whose game the host runs next.
			if (TerminationSignal.Requested || GameSwitch.Requested != null) {
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
