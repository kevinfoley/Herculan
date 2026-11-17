using Herculan.Engine.Games;
using Herculan.Es2.Cockpit;
using Herculan.Es2.Install;
using Herculan.Es2.Shell;

namespace Herculan.Es2.Host;

/// <summary>
/// The command line's flags that Earthsiege 2's front end and simulator read: its own, which
/// <see cref="Es2OptionParser"/> sets and says why each one exists, and the common ones every game takes
/// (<see cref="CommonOptions"/>), which <see cref="Es2GameModule"/> copies in as its run starts.
/// docs/herculan/herculan-command-line.md describes every flag.
/// </summary>
public sealed class Es2Options {
	/// <summary>The first of Earthsiege 2's own flags given (<see cref="IGameModule.FirstOwnOption"/>).</summary>
	public string? FirstOwnOption { get; set; }

	/// <inheritdoc cref="CommonOptions.MissionPath"/>
	/// <remarks>A script.dat, a save slot or a .MSN name.</remarks>
	public string? MissionPath { get; set; }

	/// <inheritdoc cref="CommonOptions.ScreenshotPath"/>
	public string? ScreenshotPath { get; set; }

	/// <inheritdoc cref="CommonOptions.SilentAudio"/>
	public bool SilentAudio { get; set; }
	public string? CdDrive { get; set; }
	public string? MusicDirectory { get; set; }
	public int MusicTrackSelect { get; set; }

	public bool WritePreferences { get; set; } = true;
	public bool MissionWindowed { get; set; }
	public bool ProbeJoystick { get; set; }
	public bool WriteJoystickMap { get; set; }

	/// <inheritdoc cref="CommonOptions.PlayTape"/>
	public string? PlayTape { get; set; }

	/// <inheritdoc cref="CommonOptions.DemoTape"/>
	public bool DemoTape { get; set; }

	/// <summary><c>--record</c>'s tape, with its extension made <see cref="Input.InputTapePlayer.Extension"/>, as <c>-r</c> forces it.</summary>
	public string? RecordTape { get; set; }

	public bool DeveloperMode { get; set; }

	/// <summary><c>--movie</c>: one cutscene, by path or AVI folder name, in place of the front end (<see cref="MovieHost"/>).</summary>
	public string? MoviePath { get; set; }

	/// <summary><c>--install</c>'s disc folder or image, which with <see cref="InstallDestination"/> installs and exits (<see cref="Install.DiscInstall"/>).</summary>
	public string? InstallSource { get; set; }

	public string? InstallDestination { get; set; }
	public RetailInstaller.Size InstallSize { get; set; } = RetailInstaller.Size.Maximum;
	public RetailInstaller.Language InstallLanguage { get; set; } = RetailInstaller.Language.English;
	public bool InstallDiscFiles { get; set; }

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
