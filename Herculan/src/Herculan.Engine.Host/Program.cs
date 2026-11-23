using Herculan.Engine.Games;
using Herculan.Engine.Host;
using Herculan.Engine.Install;
using Herculan.Engine.Localization;
using Herculan.Engine.Platform;
using Herculan.Engine.Settings;
using Herculan.Es1.Host;
using Herculan.Es2.Host;
using HercWorks.Core.Io.Transform;

// The thin front-end host from docs/herculan/planning.md's "Engine internal architecture" section, over the games of
// its "How the two games share code" section: it parses the command line, finds the install, and hands the window to
// the game that install holds (IGameModule.Run). Everything it does is wiring — no simulation rules, no rendering rules,
// no file formats — and nothing in it tests which game it holds: each game's facts come from its module. When a shell's
// "Game" menu switches game (GameSwitch), the other game runs in the same window.

HostLog.Start();

// The games, in the order the start-up chooser and the "Game" menu list them; the first is the one a --screenshot run
// with nobody to ask takes. The mission editor's assembly references Earthsiege 2's, so its turn is handed in here.
IReadOnlyList<IGameModule> games = [new Es2GameModule(HostEditorTurn.Run), new Es1GameModule()];
IReadOnlyList<GameIdentity> identities = [.. games.Select(game => game.Identity)];

var argumentErrors = new List<string>();
var options = HostOptions.Parse(args, games, argumentErrors);
if (options.ShowHelp) {
	Console.WriteLine(HostArguments.Usage(games));
	return 0;
}

if (argumentErrors.Count > 0) {
	HostLog.Fail(string.Join(Environment.NewLine, [.. argumentErrors, "Run with --help for the list of options."]));
	return 1;
}

foreach (var game in games) {
	if (game.RunWithoutInstall() is { } standaloneExit) {
		return standaloneExit;
	}
}

// Host-lifetime, not mission-lifetime. The accessibility settings, which every game shares, are loaded before the
// install is found, since every ImGui window reads them, the install prompt's included; each game's tweak settings are
// its own and are loaded once the install says which game runs (docs/herculan/planning.md#how-the-two-games-share-code).
// Every game's accessibility catalog is held whichever game runs, since a save writes only the settings it holds.
var localization = new LocalizationTable();
HostLog.Localization = localization;
A11ySettings.Current = new A11ySettings([.. games.SelectMany(game => game.AccessibilitySettings)]);
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
var startup = new StartupInstall(games, options, localization, imguiFontPath);
string? installRoot = startup.Find();
if (installRoot == null) {
	return 1;
}

// The window every turn of every game takes in turn, opened by the first turn that needs it, with --save-prtscn's
// captures attached, which are written under the install of the game running; where the window last stood, for one
// opened after an install prompt closed it.
HostWindow? window = null;
WindowPlacement? placement = null;
IGameModule? running = null;

// Each game in turn: the one found, then, for as long as a shell's "Switch to" asks for one (GameSwitch), the game asked
// for, on the install the request names (a game's Settings window restarting it) or with its install found as at start-up,
// without changing the saved start-up game. A game switched to runs its shell
// whatever the command line asked the first game for. Its install prompt opens a window of its own, so the shared one
// closes while it is up; the prompt's Back goes back to the game switched from.
try {
	bool switched = false;
	while (true) {
		var game = GameOf(installRoot);
		if (!options.Common.AskForInstall) {
			RetailInstalls.Remember(game.Identity, installRoot);
		}

		// An option only another game takes is refused by the install the command line ran, as it is of no use there.
		if (!switched && games.FirstOrDefault(other => other != game && other.FirstOwnOption != null) is { } flagged) {
			HostLog.Fail($"{flagged.FirstOwnOption} is an option of {localization.GetStringOrKey(flagged.Identity.NameKey)}, "
				+ $"not of {localization.GetStringOrKey(game.Identity.NameKey)}, which {installRoot} holds.");
			return 1;
		}

		running = game;
		int exit = game.Run(new GameRunContext {
			InstallRoot = installRoot,
			Switched = switched,
			Options = options.Common,
			Localization = localization,
			FontPath = imguiFontPath,
			ItalicFontPath = imguiItalicFontPath,
			Window = SharedWindow,
			Games = identities,
		});
		running = null;
		if (GameSwitch.Take() is not { } next) {
			return exit;
		}

		// A switch onto a named install, which a Settings window chose, still asks for what that install needs; giving up
		// keeps the install the run was on, as giving up the switch's install prompt does.
		installRoot = (next.Install is { } named
			? startup.Prepared(GameOf(named), named, options.Common.ScreenshotPath == null, "install_prompt.back", CloseWindow)
			: startup.ForSwitch(games.First(each => each.Identity.Game == next.Game), CloseWindow)) ?? installRoot;
		switched = true;
	}
} finally {
	window?.Dispose();
}

// The game an install holds. Every install found or named holds one of the games, since RetailGameDetector tells only
// these apart.
IGameModule GameOf(string root) {
	var held = RetailGameDetector.Detect(root);
	return games.First(game => game.Identity.Game == held);
}

// Closes the shared window before a prompt opens a window of its own, keeping its placement for the next.
void CloseWindow() {
	placement = window?.Placement;
	window?.Dispose();
	window = null;
}

HostWindow SharedWindow() {
	if (window == null) {
		window = new HostWindow(placement: placement);
		if (options.SavePrintScreens) {
			PrintScreenFiles.Attach(window, () => running?.ActiveInstall);
		}
	}

	return window;
}
