using Herculan.Es2.Cockpit;
using Herculan.Es2.Shell;

namespace Herculan.Es2.Host;

/// <summary>
/// The command line's flags that Earthsiege 2's front end and simulator read. The host's parser sets them;
/// docs/herculan/herculan-command-line.md describes every flag, and the parser says why each one exists.
/// </summary>
public sealed class Es2Options {
	/// <summary>The mission named on the command line, if any: a script.dat, a save slot or a .MSN name.</summary>
	public string? MissionPath { get; set; }

	/// <summary><c>--screenshot</c>'s file, which also makes the run one nobody watches.</summary>
	public string? ScreenshotPath { get; set; }

	public bool SilentAudio { get; set; }
	public string? CdDrive { get; set; }
	public string? MusicDirectory { get; set; }
	public int MusicTrackSelect { get; set; }

	public bool WritePreferences { get; set; } = true;
	public bool MissionWindowed { get; set; }
	public bool ProbeJoystick { get; set; }
	public bool WriteJoystickMap { get; set; }

	public string? PlayTape { get; set; }
	public bool DemoTape { get; set; }
	public string? RecordTape { get; set; }
	public bool DeveloperMode { get; set; }

	/// <summary>Whether a mission named by its <c>.MSN</c> keeps its own theater and time of day (<see cref="MissionFileLaunch.Write(string, string, bool, out string?)"/>).</summary>
	public bool OwnWorld { get; set; }

	public ShellOptions Shell { get; } = new();
	public StagedStart StagedStart { get; } = new();
	public StagedScreenshot StagedScreenshot { get; } = new();
}

/// <summary>The front end's flags. Each <c>--shell-*</c> flag sets <see cref="Staged"/>.</summary>
public sealed class ShellOptions {
	/// <summary>Whether any <c>--shell-*</c> flag was given, which a lone mission cannot honour.</summary>
	public bool Staged { get; set; }
	public string? Palette { get; set; }
	public int Tab { get; set; } = ShellScreen.MainMenuTab;
	public int Bay { get; set; }
	public bool Practice { get; set; }
	public bool Windowed { get; set; }
	public bool Movies { get; set; } = true;
}
