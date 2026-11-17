using Herculan.Engine.Content;
using Herculan.Engine.Games;
using Herculan.Engine.Install;
using Herculan.Engine.Localization;

namespace Herculan.Engine.Host;

/// <summary>
/// Which install a launch runs. An install named on the command line runs whichever game it holds, and is never
/// second-guessed. Otherwise the game is the one whose own option was given (<see cref="HostOptions.FlaggedGame"/>), the
/// game saved by the chooser's "Don't ask again" (<see cref="StartupGame"/>), or the one the player picks in the chooser
/// (<see cref="GameChooser"/>); a <c>--screenshot</c> run, which has nobody to ask, takes the first game in its place.
/// That game's install is then found (<see cref="RetailInstalls.Locate"/>), or asked for in its install prompt
/// (<see cref="IGameModule.AskForInstall"/>) when the search comes up empty, finds one on a disc, or is skipped by
/// <c>--ask-install</c>. A prompt opened from the chooser goes back to it when the player gives up. A switch from the
/// "Game" menu finds the other game's install the same way (<see cref="ForSwitch"/>). This engine's own; retail has no
/// counterpart.
/// </summary>
/// <param name="games">Every game the host runs, in the order the chooser lists them.</param>
/// <param name="fontPath">The ImGui font of the windows that ask.</param>
sealed class StartupInstall(IReadOnlyList<IGameModule> games, HostOptions options, LocalizationTable localization, string fontPath) {
	/// <summary>The install root to run, or null when the player quit or none could be found, which has been reported.</summary>
	public string? Find() {
		if (options.InstallPath is { } named) {
			return Named(named);
		}

		bool canAsk = options.Common.ScreenshotPath == null;
		var saved = StartupGame.Load();
		IGameModule? fixedGame = options.FlaggedGame
			?? games.FirstOrDefault(game => game.Identity.Game == saved)
			?? (canAsk ? null : games[0]);
		bool dontAskAgain = false;
		while (true) {
			IGameModule game;
			if (fixedGame != null) {
				game = fixedGame;
			} else {
				var found = games.Select(each => (each.Identity, Usable(RetailInstalls.Locate(each.Identity)))).ToList();
				(var chosen, dontAskAgain) = GameChooser.Run(localization, fontPath, found, dontAskAgain);
				if (chosen == null) {
					return null;
				}

				game = games.First(each => each.Identity == chosen);
			}

			string dismissKey = fixedGame == null ? "install_prompt.back" : "install_prompt.quit";
			if (InstallOf(game, canAsk, dismissKey) is { } root) {
				if (dontAskAgain) {
					StartupGame.Save(game.Identity.Game);
				}

				return root;
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
	public string? ForSwitch(IGameModule game, Action beforePrompt) =>
		InstallOf(game, canAsk: true, "install_prompt.back", beforePrompt);

	// The install of game: the one found, unless --ask-install skips the search, else the one chosen in its install prompt,
	// whose dismissal is labelled dismissKey; null when the player dismissed the prompt, or with the reason reported when
	// canAsk is clear and the search found none.
	private string? InstallOf(IGameModule game, bool canAsk, string dismissKey, Action? beforePrompt = null) {
		string? root = options.Common.AskForInstall ? null : RetailInstalls.Locate(game.Identity);
		string? onDisc = root != null && RetailInstalls.IsOnDisc(root) ? root : null;
		if (onDisc == null && root != null) {
			return root;
		}

		if (!canAsk) {
			HostLog.Fail(onDisc != null
				? $"{onDisc}: {localization.GetStringOrKey("install_prompt.on_disc")}"
				: $"Could not find a {localization.GetStringOrKey(game.Identity.NameKey)} installation.\n" +
					$"Pass its path as the first argument, or set {game.Identity.PathVariable}.\n" +
					$"The path should be the folder containing the '{GameContent.ArchiveFolderName}' directory.");
			return null;
		}

		beforePrompt?.Invoke();
		return game.AskForInstall(localization, fontPath, onDisc, dismissKey);
	}

	// An install named on the command line, of any game, or null with the reason reported.
	private string? Named(string path) {
		if (!RetailInstalls.IsInstallRoot(path)) {
			string names = string.Join(" or ", games.Select(game => localization.GetStringOrKey(game.Identity.NameKey)));
			HostLog.Fail($"{path} is not an {names} installation.\n" +
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
}
