using Herculan.Engine.Games;

namespace Herculan.Engine.Host;

/// <summary>
/// The command line, parsed. The host takes the flags every game takes (<see cref="Common"/>) and hands each other flag
/// to the games in turn (<see cref="IGameModule.TryParseOption"/>); docs/herculan/herculan-command-line.md describes
/// every flag. Each comment below says why the flag exists, which is what that page links back to.
/// </summary>
sealed class HostOptions {
	private readonly List<string> _positional = new();

	/// <summary>Whether <c>--help</c> was asked for, which ends parsing and the run.</summary>
	public bool ShowHelp { get; private set; }

	/// <summary>The install named on the command line, if any.</summary>
	public string? InstallPath => _positional.Count > 0 ? _positional[0] : null;

	/// <summary>Whether <c>--save-prtscn</c> was given (<see cref="PrintScreenFiles"/>).</summary>
	public bool SavePrintScreens { get; private set; }

	public CommonOptions Common { get; } = new();

	/// <summary>
	/// The game one of whose own flags was given (<see cref="IGameModule.FirstOwnOption"/>), which a launch with no
	/// install named runs; null when no game's own flag was. <see cref="Parse"/> refuses flags of two games.
	/// </summary>
	public IGameModule? FlaggedGame { get; private set; }

	/// <summary>
	/// Parses the command line, recording every problem rather than stopping at the first. <c>--help</c> stops at once
	/// and sets <see cref="ShowHelp"/>.
	/// </summary>
	/// <param name="args">The command-line arguments.</param>
	/// <param name="games">The games the host runs.</param>
	/// <param name="errors">Receives a message for each problem found.</param>
	public static HostOptions Parse(string[] args, IReadOnlyList<IGameModule> games, List<string> errors) {
		var options = new HostOptions();
		var common = options.Common;
		for (int i = 0; i < args.Length; i++) {
			if (HostArguments.IsHelp(args[i])) {
				options.ShowHelp = true;
				return options;
			} else if (args[i] == "--screenshot") {
				if (OptionValues.TryReadString(args, ref i, errors, out string path)) {
					common.ScreenshotPath = path;
				}
			} else if (args[i] == "--save-prtscn") {
				// Also writes each frame [PrtScn] copies to the clipboard while full screen into the install's Screenshots
				// folder, so a run's captures are kept without pasting each one somewhere. See PrintScreenFiles.
				options.SavePrintScreens = true;
			} else if (args[i] == "--ask-install") {
				// Opens the install prompt without searching — the game's environment variable, the remembered install and
				// the game's folder above the executable are all passed over — and remembers nothing, neither the folder
				// picked there nor one the Settings menu switches to. For trying another install without losing the one
				// remembered.
				common.AskForInstall = true;
			} else if (args[i] == "--mission") {
				// Fly one mission instead of the front end, which is what runs by default — the simulator's turn alone, for
				// looking at the simulator without clicking through the shell to reach it. A named mission, --play and
				// --demo imply it.
				common.RunMission = true;
			} else if (args[i] == "--no-sound" || args[i] == "--silent") {
				// Skip the output device entirely. Same effect as running on a machine with no sound card:
				// the catalog, the director and the message port all still run, nothing is heard. For a
				// --screenshot run, an automated run, or anywhere the noise is not what is being looked at.
				common.SilentAudio = true;
			} else if (args[i] == "--play") {
				// The simulators' own -p<name>: replay an input tape — a path, or a stem looked up in the install's
				// TAPES folder, as -p's own "tapes\demo1" is. The mission comes out of the tape, so it replaces
				// the positional mission argument.
				if (OptionValues.TryReadString(args, ref i, errors, out string tape)) {
					common.PlayTape = tape;
				}
			} else if (args[i] == "--record") {
				// The simulators' own -r<name>: record this mission's input to a tape, which --play replays. Each game
				// forces its tape extension on the name, as -r forces it.
				if (OptionValues.TryReadString(args, ref i, errors, out string tape)) {
					common.RecordTape = tape;
				}
			} else if (args[i] == "--demo") {
				// The simulators' own -D: a tape picked from TAPES\demolist.str, as VIEW DEMO plays one, which ends the
				// mission when it runs out or the moment a key is pressed. With --play, that tape instead.
				common.DemoTape = true;
			} else if (TryParseGameOption(games, args, ref i, errors)) {
			} else if (args[i].StartsWith("--")) {
				errors.Add($"Unknown option {args[i]}.");
			} else {
				options._positional.Add(args[i]);
			}
		}

		common.MissionPath = options._positional.Count > 1 ? options._positional[1] : null;
		if (common.MissionPath != null || common.PlayTape != null || common.DemoTape) {
			common.RunMission = true;
		}
		if (common.RecordTape != null && (common.PlayTape != null || common.DemoTape)) {
			errors.Add("--record cannot be combined with --play or --demo.");
		}
		if (common.AskForInstall && common.ScreenshotPath != null && options._positional.Count == 0) {
			errors.Add("--ask-install needs someone to ask: name the install, or drop --screenshot.");
		}
		if (options._positional.Count > 2) {
			errors.Add($"Unexpected argument {options._positional[2]}: the only positional arguments are the install and the mission.");
		}

		var flagged = games.Where(game => game.FirstOwnOption != null).ToList();
		if (flagged.Count > 1) {
			errors.Add($"{flagged[0].FirstOwnOption} and {flagged[1].FirstOwnOption} are options of different games.");
		}
		options.FlaggedGame = flagged.Count == 1 ? flagged[0] : null;
		foreach (var game in games) {
			game.CheckOptions(common, errors);
		}

		return options;
	}

	// The first game that takes the flag at args[i].
	private static bool TryParseGameOption(IReadOnlyList<IGameModule> games, string[] args, ref int i, List<string> errors) {
		foreach (var game in games) {
			if (game.TryParseOption(args, ref i, errors)) {
				return true;
			}
		}

		return false;
	}
}
