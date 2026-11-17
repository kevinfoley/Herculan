namespace Herculan.Engine.Games;

/// <summary>
/// The command-line flags every game takes, which the host parses and hands each game's run
/// (<see cref="GameRunContext.Options"/>). A game's own flags are its <see cref="IGameModule"/>'s to parse.
/// docs/herculan/herculan-command-line.md describes every flag.
/// </summary>
public sealed class CommonOptions {
	/// <summary>The mission named on the command line, if any, as the game running reads a mission name.</summary>
	public string? MissionPath { get; set; }

	/// <summary>
	/// Whether a mission is flown instead of starting in the front end: <c>--mission</c>, or implied by a named mission,
	/// <c>--play</c> or <c>--demo</c>.
	/// </summary>
	public bool RunMission { get; set; }

	/// <summary><c>--screenshot</c>'s file, which also makes the run one nobody watches.</summary>
	public string? ScreenshotPath { get; set; }

	/// <summary><c>--no-sound</c>: no audio device.</summary>
	public bool SilentAudio { get; set; }

	/// <summary><c>--ask-install</c>: the install is asked for at once, and no install is remembered.</summary>
	public bool AskForInstall { get; set; }

	/// <summary><c>--play</c>'s tape, as given: a path or a stem, which the game running resolves.</summary>
	public string? PlayTape { get; set; }

	/// <summary><c>--demo</c>.</summary>
	public bool DemoTape { get; set; }

	/// <summary><c>--record</c>'s tape, as given, which the game running gives its own tape extension.</summary>
	public string? RecordTape { get; set; }
}
