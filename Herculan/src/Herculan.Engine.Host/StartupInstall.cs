using Herculan.Engine.Content;
using Herculan.Engine.Install;
using Herculan.Engine.Localization;
using Herculan.Es2.Host.Install;
using HercWorks.Core.Io.Transform;

namespace Herculan.Engine.Host;

/// <summary>
/// Which install a launch runs. An install named on the command line runs whichever game it holds, and is never
/// second-guessed. Otherwise the game is Metaltech: Earthsiege when one of its options is given, the game saved by the
/// chooser's "Don't ask again" (<see cref="StartupGame"/>), or the one the player picks in the chooser
/// (<see cref="GameChooser"/>); a <c>--screenshot</c> run, which has nobody to ask, takes Earthsiege 2 in its place. That
/// game's install is then found (<see cref="RetailInstalls.Locate"/>), or asked for in its install prompt when the search
/// comes up empty, finds one on a disc, or is skipped by <c>--ask-install</c>. A prompt opened from the chooser goes
/// back to it when the player gives up. A switch from the "Game" menu finds the other game's install the same way
/// (<see cref="ForSwitch"/>). This engine's own; retail has no counterpart.
/// </summary>
static class StartupInstall {
	/// <summary>
	/// The install root to run, or null when the player quit or none could be found, which has been reported.
	/// <paramref name="fontPath"/> is the ImGui font of the windows that ask.
	/// </summary>
	public static string? Find(HostOptions options, LocalizationTable localization, string fontPath) {
		if (options.InstallPath is { } named) {
			return Named(named, localization);
		}

		bool canAsk = options.ScreenshotPath == null;
		RetailGame? fixedGame = Es1Requested(options) ? RetailGame.Earthsiege1
			: StartupGame.Load() ?? (canAsk ? null : RetailGame.Earthsiege2);
		bool dontAskAgain = false;
		while (true) {
			RetailGame game;
			if (fixedGame is { } fixedChoice) {
				game = fixedChoice;
			} else {
				var found = RetailInstalls.Games.ToDictionary(each => each, each => Usable(RetailInstalls.Locate(each)));
				(var chosen, dontAskAgain) = GameChooser.Run(localization, fontPath, found, dontAskAgain);
				if (chosen is not { } choice) {
					return null;
				}

				game = choice;
			}

			string dismissKey = fixedGame == null ? "install_prompt.back" : "install_prompt.quit";
			if (InstallOf(game, options, localization, fontPath, canAsk, dismissKey) is { } root) {
				return Settled(game, root, dontAskAgain);
			}

			// A fixed game is always the case without asking, so a failed search ends here too.
			if (fixedGame != null) {
				return null;
			}
		}
	}

	/// <summary>
	/// The install of <paramref name="game"/>, which the "Game" menu's "Switch to" asked for (<see cref="GameSwitch"/>),
	/// found as <see cref="Find"/> finds a game's, its prompt's Back giving up; null when the player gave up. The saved
	/// start-up game is not touched. <paramref name="beforePrompt"/> runs before the prompt opens, a window of its own.
	/// </summary>
	public static string? ForSwitch(RetailGame game, HostOptions options, LocalizationTable localization, string fontPath,
			Action beforePrompt) =>
		InstallOf(game, options, localization, fontPath, canAsk: true, "install_prompt.back", beforePrompt);

	// The install of game: the one found, unless --ask-install skips the search, else the one chosen in its install prompt,
	// whose dismissal is labelled dismissKey; null when the player dismissed the prompt, or with the reason reported when
	// canAsk is clear and the search found none.
	private static string? InstallOf(RetailGame game, HostOptions options, LocalizationTable localization, string fontPath,
			bool canAsk, string dismissKey, Action? beforePrompt = null) {
		string? root = options.AskForInstall ? null : RetailInstalls.Locate(game);
		string? onDisc = root != null && RetailInstalls.IsOnDisc(root) ? root : null;
		if (onDisc == null && root != null) {
			return root;
		}

		if (!canAsk) {
			HostLog.Fail(onDisc != null
				? $"{onDisc}: {localization.GetStringOrKey("install_prompt.on_disc")}"
				: $"Could not find a {localization.GetStringOrKey(GameChooser.NameKey(game))} installation.\n" +
					$"Pass its path as the first argument, or set {RetailInstalls.PathVariable(game)}.\n" +
					$"The path should be the folder containing the '{GameContent.ArchiveFolderName}' directory.");
			return null;
		}

		beforePrompt?.Invoke();
		return game == RetailGame.Earthsiege1
			? InstallPromptWindow.Run(localization, fontPath, game, onDisc, dismissKey).Chosen
			: InstallPrompt.Run(localization, fontPath, onDisc, dismissKey);
	}

	/// <summary>Whether an option only Metaltech: Earthsiege takes was given (<see cref="HostOptions.Es1"/>).</summary>
	public static bool Es1Requested(HostOptions options) =>
		options.Es1.Staged || options.Es1.Simulator || options.Es1.ShellArguments.Count > 0;

	// An install named on the command line, of either game, or null with the reason reported.
	private static string? Named(string path, LocalizationTable localization) {
		if (!RetailInstalls.IsInstallRoot(path)) {
			HostLog.Fail($"{path} is not an Earthsiege 2 or Metaltech: Earthsiege installation.\n" +
				$"The path should be the folder containing the '{GameContent.ArchiveFolderName}' directory.");
			return null;
		}

		if (RetailInstalls.IsOnDisc(path)) {
			HostLog.Fail($"{path}: {localization.GetStringOrKey("install_prompt.on_disc")}");
			return null;
		}

		return Path.GetFullPath(path);
	}

	// An install found, or null for one on a disc, which the chooser shows as not found.
	private static string? Usable(string? root) => root != null && !RetailInstalls.IsOnDisc(root) ? root : null;

	// The install chosen, with the chooser's game saved to start without asking when "Don't ask again" was ticked.
	private static string Settled(RetailGame game, string root, bool dontAskAgain) {
		if (dontAskAgain) {
			StartupGame.Save(game);
		}

		return root;
	}
}
