using Herculan.Engine.Host;
using Herculan.Engine.Host.Install;
using Herculan.Engine.Host.Shell;
using Herculan.Engine.Host.Localization;
using Herculan.Engine.Install;
using Herculan.Engine.Platform;
using Herculan.Engine.Settings;

// The thin front-end host from docs/herculan/planning.md's "Engine internal architecture" section:
// it locates an install, asks the engine to build a scene from a real mission, and runs a real-time
// loop over it. Everything it does is wiring — no simulation rules, no rendering rules, no file
// formats — so a second host (the mission editor that decision exists to keep possible) can differ
// here and reuse everything below it unchanged.
//
// This file is the entry point alone: parse the command line, find the install, and hand over to the
// turn that was asked for — an install (DiscInstall), a movie (MovieHost), one mission, or by default
// ES.EXE's loop of front end and simulator (Launcher).

HostLog.Start();

var argumentErrors = new List<string>();
var options = HostOptions.Parse(args, argumentErrors);
if (options.ShowHelp) {
	Console.WriteLine(HostArguments.Usage);
	return 0;
}

if (argumentErrors.Count > 0) {
	HostLog.Fail(string.Join(Environment.NewLine, [.. argumentErrors, "Run with --help for the list of options."]));
	return 1;
}

if (options.InstallSource != null && options.InstallDestination != null) {
	return DiscInstall.Run(options.InstallSource, options.InstallDestination, options.InstallSize, options.InstallLanguage);
}

// Host-lifetime, not mission-lifetime: neither reads the install, and both need to survive into
// the shell and --movie once those have a menu bar of their own to raise TweaksMenu from — see
// docs/herculan/planning.md. Built before that branch so nothing below has to change when they do.
var localization = new LocalizationTable();
HostLog.Localization = localization;
TweakSettings.Current.LoadFromDisk();
EngineWindow.Icons = WindowIcon.Load();

// The one font every ImGui window the host opens is drawn in.
string imguiFontPath = Path.Combine(AppContext.BaseDirectory,
	"Assets", "Fonts", "Open_Sans", "static", "OpenSans-Regular.ttf");

// An install named on the command line is never second-guessed, and a --screenshot run has nobody to ask,
// so only a search that came up empty, or --ask-install's skipped one, asks the player.
string? installRoot = options.InstallPath != null ? GameInstall.Locate(options.InstallPath)
	: options.AskForInstall ? null
	: GameInstall.Locate();
if (installRoot == null && options.InstallPath == null && options.ScreenshotPath == null) {
	installRoot = InstallPrompt.Run(localization, imguiFontPath);
	if (installRoot == null) {
		return 1;
	}
}
if (installRoot == null) {
	HostLog.Fail(
		"Could not find an Earthsiege 2 installation.\n" +
		$"Pass its path as the first argument, or set {GameInstall.PathVariable}.\n" +
		$"The path should be the folder containing the '{GameInstall.ArchiveFolderName}' directory.");
	return 1;
}
if (!options.AskForInstall) {
	GameInstall.Remember(installRoot);
}
using var session = new HostSession(installRoot, localization, imguiFontPath) {
	RememberInstall = !options.AskForInstall,
	SavePrintScreens = options.SavePrintScreens,
};

// The disc GameInstall.OpenDiscFile falls back from, said once: a disc without the movie the shell's startup
// probes for (ShellHost.DiscCheckMovie) is not the CD.
if (session.Disc is { } startupDisc) {
	Console.WriteLine($"Disc: {startupDisc.Location}");
	if (!startupDisc.FileExists(ShellHost.DiscCheckMovie)) {
		Console.Error.WriteLine($"data\\drive.cfg specifies {startupDisc.Location}, but it is not a retail CD. "
			+ "Falling back to the install directory for some files.");
	}
}

// --movie shares even less: no archives, no zone, no shell art — one file and a quad. See MovieHost.
if (options.MoviePath != null) {
	return MovieHost.Run(installRoot, session.Disc, options.MoviePath, options.ScreenshotPath, options.SilentAudio);
}

return options.RunMission
	? Launcher.RunMission(session, options)
	: Launcher.RunShellLoop(session, options);
