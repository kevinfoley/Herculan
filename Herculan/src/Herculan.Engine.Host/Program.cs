using Herculan.Engine.Content;
using Herculan.Engine.Localization;
using Herculan.Engine.Host;
using Herculan.Engine.Install;
using Herculan.Engine.Platform;
using Herculan.Engine.Settings;
using Herculan.Es1.Host;
using Herculan.Es1.Settings;
using Herculan.Es2.Host;
using Herculan.Es2.Host.Install;
using Herculan.Es2.Host.Shell;
using Herculan.Es2.Install;
using Herculan.Es2.Settings;
using HercWorks.Core.Io.Transform;

// The thin front-end host from docs/herculan/planning.md's "Engine internal architecture" section:
// it locates an install, asks the engine to build a scene from a real mission, and runs a real-time
// loop over it. Everything it does is wiring — no simulation rules, no rendering rules, no file
// formats — so a second host (the mission editor that decision exists to keep possible) can differ
// here and reuse everything below it unchanged.
//
// This file is the entry point alone: parse the command line, find the install, and hand over to the
// turn that was asked for — an install (DiscInstall), Metaltech: Earthsiege's launcher (Es1Launcher) or one of its
// missions (Es1ZoneViewer), a movie
// (MovieHost), one mission, or by default ES.EXE's loop of front end and simulator (Launcher), in the window opened here;
// and, when a shell's "Game" menu switches game (GameSwitch), the other game's launcher in the same window.

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
	return DiscInstall.Run(options.InstallSource, options.InstallDestination, options.InstallSize, options.InstallLanguage, options.InstallDiscFiles);
}

// Host-lifetime, not mission-lifetime. The accessibility settings, which both games share, are loaded before the
// install is found, since every ImGui window reads them, the install prompt's included; each game's tweak settings are
// its own and are loaded once the install says which game runs (docs/herculan/planning.md#how-the-two-games-share-code).
// Earthsiege 2's accessibility catalog is held whichever game runs, since a save writes only the settings it holds.
var localization = new LocalizationTable();
HostLog.Localization = localization;
A11ySettings.Current = new A11ySettings(Es2A11ySettingDefinitions.All);
A11ySettings.Current.LoadFromDisk();
HostWindow.Icons = WindowIcon.Load();

// The one font every ImGui window the host opens is drawn in.
string imguiFontPath = Path.Combine(AppContext.BaseDirectory,
	"Assets", "Fonts", "Open_Sans", "static", "OpenSans-Regular.ttf");
string imguiItalicFontPath = Path.Combine(AppContext.BaseDirectory,
	"Assets", "Fonts", "Open_Sans", "static", "OpenSans-Italic.ttf");

// Which install runs, of which game (StartupInstall): one named on the command line, or the game the start-up chooser
// picks, asking for its install when none is found. Each game's install is remembered for the next launch, unless
// --ask-install asked for it.
string? installRoot = StartupInstall.Find(options, localization, imguiFontPath);
if (installRoot == null) {
	return 1;
}

// The window every turn of either game takes in turn, opened by the first turn that needs it, with --save-prtscn's
// captures attached, which are written under the install of the Earthsiege 2 session running; where the window last
// stood, for one opened after an install prompt closed it.
HostWindow? window = null;
WindowPlacement? placement = null;
HostSession? es2Session = null;

// Each game in turn: the one found, then, for as long as a shell's "Switch to" asks for one (GameSwitch), the game asked
// for, on the install the request names (a game's Settings window restarting it) or with its install found as at start-up,
// without changing the saved start-up game. A game switched to runs its shell
// whatever the command line asked the first game for. Its install prompt opens a window of its own, so the shared one
// closes while it is up; the prompt's Back goes back to the game switched from.
try {
	bool switched = false;
	while (true) {
		if (!options.AskForInstall) {
			RetailInstalls.Remember(installRoot);
		}

		int exit = RetailGameDetector.Detect(installRoot) == RetailGame.Earthsiege1
			? RunEs1(installRoot, switched)
			: RunEs2(installRoot, switched);
		if (GameSwitch.Take() is not { } next) {
			return exit;
		}

		installRoot = next.Install ?? StartupInstall.ForSwitch(next.Game, options, localization, imguiFontPath, () => {
			placement = window?.Placement;
			window?.Dispose();
			window = null;
		}) ?? installRoot;
		switched = true;
	}
} finally {
	window?.Dispose();
}

HostWindow SharedWindow() {
	if (window == null) {
		window = new HostWindow(placement: placement);
		if (options.SavePrintScreens) {
			PrintScreenFiles.Attach(window, () => es2Session?.InstallRoot);
		}
	}

	return window;
}

// A Metaltech: Earthsiege install, with no mission named and none of --play, --demo, --record and --sim, runs the
// launcher, ESCD.EXE, as ES.BAT does, with GO.EXE's switches as its own command line, which it hands on to the shell and
// the simulator. A named mission loads as a single mission and runs the simulator alone; --sim
// runs it on the install's data\ files, as DBSIM.EXE does; --play and --demo play a tape, as its -p and -D do, and
// --record records one, as its -r does.
int RunEs1(string root, bool switched) {
	// Loaded for both turns: the shell's SYSTEM REPAIR and the menu bar's Tweaks window read them, and so does the
	// simulator, for its textures and sounds.
	TweakSettings.Current = new TweakSettings(Es1TweakSettingDefinitions.All, Es1TweakSettingDefinitions.FileName);
	TweakSettings.Current.LoadFromDisk();

	var es1 = options.Es1;
	bool simulator = !switched && (options.MissionPath != null || options.PlayTape != null || options.DemoTape
		|| options.RecordTape != null || es1.Simulator);
	if (!simulator) {
		if (es1.Staged) {
			HostLog.Fail($"--theater and --world stage the simulator: add {Es1Options.SimulatorFlag} or name a mission.");
			return 1;
		}

		return Es1Launcher.Run(root, es1.ShellArguments, imguiFontPath, imguiItalicFontPath,
			inMission => new Es1ShellMenus(localization, root, inMission), options.ScreenshotPath, SharedWindow(),
			options.SilentAudio);
	}

	if (es1.ShellArguments.Count > 0) {
		HostLog.Fail($"{es1.ShellArguments[0]} is a switch of GO.EXE's shell, which does not run with the simulator.");
		return 1;
	}

	return Es1ZoneViewer.Run(root, options.MissionPath, es1.ToOverrides(), imguiFontPath, imguiItalicFontPath,
		new Es1ShellMenus(localization, root, inMission: true), options.PlayTape, options.DemoTape, options.RecordTape,
		silent: options.SilentAudio);
}

int RunEs2(string root, bool switched) {
	if (!switched && StartupInstall.Es1Requested(options)) {
		HostLog.Fail($"--theater, --world, {Es1Options.SimulatorFlag} and GO.EXE's switches are for a Metaltech: Earthsiege install.");
		return 1;
	}

	TweakSettings.Current = new TweakSettings(Es2TweakSettingDefinitions.All, Es2TweakSettingDefinitions.FileName);
	TweakSettings.Current.LoadFromDisk();

	using var session = new HostSession(root, localization, imguiFontPath, imguiItalicFontPath) {
		RememberInstall = !options.AskForInstall,
	};
	es2Session = session;
	try {
		// A drive.cfg that opens no disc is given an image found the way the install was, written in as if chosen in Settings.
		if (session.Disc == null && GameInstall.FindDiscImage(root, ShellHost.DiscCheckMovie) is { } foundImage) {
			try {
				GameInstall.WriteDiscImage(root, foundImage);
				session.ReopenDisc();
			} catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
				Console.Error.WriteLine($"Found the disc image {foundImage}, but could not write it into data\\drive.cfg ({ex.Message}).");
			}
		}

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
		if (!switched && options.MoviePath != null) {
			return MovieHost.Run(root, session.Disc, options.MoviePath, options.ScreenshotPath, options.SilentAudio);
		}

		return options.RunMission && !switched
			? Launcher.RunMission(session, SharedWindow(), options.Es2)
			: Launcher.RunShellLoop(session, SharedWindow(), options.Es2, HostEditorTurn.Run);
	} finally {
		es2Session = null;
	}
}
