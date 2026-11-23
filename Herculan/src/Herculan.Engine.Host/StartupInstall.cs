using HercWorks.Core.Io.Transform;
using Herculan.Engine.Content;
using Herculan.Engine.Games;
using Herculan.Engine.Install;
using Herculan.Engine.Localization;

namespace Herculan.Engine.Host;

/// <summary>
/// Which install a launch runs. An install named on the command line runs whichever game it holds, and is never
/// swapped for another: one the install prompt would refuse (<see cref="InstallPromptWindow.Refusal"/>) ends the
/// launch. Otherwise the game is the one whose own option was given (<see cref="HostOptions.FlaggedGame"/>), the
/// game saved by the chooser's "Don't ask again" (<see cref="StartupGame"/>), or the one the player picks in the chooser
/// (<see cref="GameChooser"/>); a <c>--screenshot</c> run, which has nobody to ask, takes the first game in its place.
/// That game's install is then found (<see cref="RetailInstalls.Locate"/>), or asked for in its install prompt
/// (<see cref="IGameModule.AskForInstall"/>) when the search comes up empty, finds one the prompt refuses, or is skipped by
/// <c>--ask-install</c>; once found, the game asks for what the install needs beyond itself
/// (<see cref="IGameModule.PrepareInstall"/>). A prompt opened from the chooser goes back to it when the player gives up. A switch from the
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
				var found = games.Select(each => (each.Identity, Usable(each.Identity, RetailInstalls.Locate(each.Identity)))).ToList();
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

	// The install of game: the one found, unless --ask-install skips the search or the prompt refuses it, else the one
	// chosen in its install prompt, whose dismissal is labelled dismissKey; null when the player dismissed the prompt, or
	// with the reason reported when canAsk is clear and the search found none it takes.
	private string? InstallOf(IGameModule game, bool canAsk, string dismissKey, Action? beforePrompt = null) {
		// beforePrompt runs once, before whichever of the install prompt and the game's own prompt opens first.
		bool prompted = false;
		void BeforePrompt() {
			if (!prompted) {
				prompted = true;
				beforePrompt?.Invoke();
			}
		}

		string? root = options.Common.AskForInstall ? null : RetailInstalls.Locate(game.Identity);
		string? refusal = root != null ? Refusal(game.Identity, root) : null;
		if (root != null && refusal == null) {
			return Prepared(game, root, canAsk, dismissKey, BeforePrompt);
		}

		if (!canAsk) {
			HostLog.Fail(root != null && refusal != null
				? Reported(root, refusal)
				: $"Could not find a {localization.GetStringOrKey(game.Identity.NameKey)} installation.\n" +
					$"Pass its path as the first argument, or set {game.Identity.PathVariable}.\n" +
					$"The path should be the folder containing the '{GameContent.ArchiveFolderName}' directory.");
			return null;
		}

		BeforePrompt();
		return game.AskForInstall(localization, fontPath, refusal != null ? root : null, dismissKey) is { } chosen
			? Prepared(game, chosen, canAsk, dismissKey, BeforePrompt)
			: null;
	}

	/// <summary>
	/// <paramref name="root"/> once <paramref name="game"/> has what it needs beyond the install
	/// (<see cref="IGameModule.PrepareInstall"/>), or null when the player gave up its prompt. With nobody to ask, the
	/// install is taken as it is, and the game reports what it lacks.
	/// </summary>
	public string? Prepared(IGameModule game, string root, bool canAsk, string dismissKey, Action? beforePrompt = null) =>
		!canAsk || game.PrepareInstall(localization, fontPath, root, dismissKey, beforePrompt) ? root : null;

	// An install named on the command line, of any game, or null with the reason reported.
	private string? Named(string path) {
		if (!RetailInstalls.IsInstallRoot(path)) {
			string names = string.Join(" or ", games.Select(game => localization.GetStringOrKey(game.Identity.NameKey)));
			HostLog.Fail($"{path} is not an {names} installation.\n" +
				$"The path should be the folder containing the '{GameContent.ArchiveFolderName}' directory.");
			return null;
		}

		var held = games.First(game => game.Identity.Game == RetailGameDetector.Detect(path));
		if (Refusal(held.Identity, path) is { } refusal) {
			HostLog.Fail(Reported(path, refusal));
			return null;
		}

		return Prepared(held, Path.GetFullPath(path), options.Common.ScreenshotPath == null, "install_prompt.quit");
	}

	// An install found, or null for one the install prompt refuses, which the chooser shows as not found.
	private string? Usable(GameIdentity game, string? root) => root != null && Refusal(game, root) == null ? root : null;

	// Why root, which holds game, cannot be its install (InstallPromptWindow.Refusal), or null when it can.
	private string? Refusal(GameIdentity game, string root) => InstallPromptWindow.Refusal(localization, game, root);

	// A refusal as the log reports it: the disc's, which does not name the folder, after the folder's path.
	private static string Reported(string root, string refusal) =>
		RetailInstalls.IsOnDisc(root) ? $"{root}: {refusal}" : refusal;
}
