using Herculan.Engine.Content;
using Herculan.Engine.Games;
using Herculan.Engine.Localization;
using HercWorks.Core.Io.Transform;

namespace Herculan.Engine.Install;

/// <summary>
/// The prompt the host opens when <see cref="RetailInstalls.Locate"/> finds no install of the game to run, or at once
/// under <c>--ask-install</c>: it asks the player for that game's folder, by the platform's own folder picker or by a
/// typed path, and accepts only a folder that holds that game (<see cref="RetailInstalls.IsInstallOf"/>) and is not on a
/// disc. A game may add a third button, as both games do to install from a disc. Its strings are the game's
/// <see cref="GameIdentity.PromptKeyPrefix"/>'s, beside the shared <c>install_prompt.browse</c>, <c>picker_open</c>, <c>picker_failed</c>,
/// <c>quit</c>, <c>back</c> and <c>on_disc</c>. Retail has no equivalent; each game's installer wrote the path for it.
/// </summary>
public static class InstallPromptWindow {
	public const int WindowWidth = 640;
	public const int WindowHeight = 260;

	/// <summary>
	/// The prompt's body for <paramref name="game"/>, refusing what <see cref="Refusal"/> refuses. <paramref name="dismissKey"/>
	/// is the string of the button that gives up, <paramref name="messageKey"/> the message's when not the start-up's own,
	/// and <paramref name="alternativeKey"/> the string of a third button, or null for none.
	/// </summary>
	public static PathPrompt Create(LocalizationTable localization, GameIdentity game, string dismissKey, string initialPath = "",
			string? messageKey = null, string? alternativeKey = null) =>
		new(localization, game.PromptKeyPrefix, dismissKey, path => Refusal(localization, game, path), initialPath, messageKey,
			alternativeKey: alternativeKey);

	/// <summary>
	/// Why <paramref name="path"/> cannot be <paramref name="game"/>'s install, in the player's language, or null when it can:
	/// it has no archive folder, it holds another game (<see cref="RetailGameDetector.Detect"/>), the game refuses it (<see cref="GameIdentity.InstallRefusal"/>), or it is
	/// on a disc (<see cref="RetailInstalls.IsOnDisc"/>), which can hold every folder of an install but none of its saves.
	/// </summary>
	public static string? Refusal(LocalizationTable localization, GameIdentity game, string path) {
		string prefix = game.PromptKeyPrefix;
		if (!RetailInstalls.IsInstallRoot(path)) {
			return string.Format(localization.GetStringOrKey(prefix + ".not_an_install"), path, GameContent.ArchiveFolderName);
		}

		if (RetailGameDetector.Detect(path) != game.Game) {
			return string.Format(localization.GetStringOrKey(prefix + ".other_game"), path, RetailGameDetector.Es1Marker,
				RetailGameDetector.Es1ResourceMarker);
		}

		if (game.InstallRefusal?.Invoke(localization, path) is { } refused) {
			return refused;
		}

		return RetailInstalls.IsOnDisc(path) ? localization.GetStringOrKey("install_prompt.on_disc") : null;
	}

	/// <summary>
	/// What the prompt's message is given: the archive folder's name, <see cref="RetailGameDetector.Es1Marker"/> and
	/// <see cref="RetailGameDetector.Es1ResourceMarker"/>, in that order.
	/// </summary>
	public static object[] MessageArguments => [GameContent.ArchiveFolderName, RetailGameDetector.Es1Marker, RetailGameDetector.Es1ResourceMarker];

	/// <summary>Draws <paramref name="prompt"/> into the current window, its message given <see cref="MessageArguments"/>.</summary>
	public static PathPrompt.Outcome DrawPrompt(PathPrompt prompt, nint owner) => prompt.Draw(owner, MessageArguments);

	/// <summary>
	/// The window's one frame of content: the language combo over the prompt (<see cref="StartupWindow.Draw"/>).
	/// </summary>
	public static PathPrompt.Outcome Draw(PathPrompt prompt, InterfaceLanguageCombo language, nint owner, out bool languageChanged) {
		var outcome = PathPrompt.Outcome.Open;
		StartupWindow.Draw(language, () => (outcome = DrawPrompt(prompt, owner)) != PathPrompt.Outcome.Open, out languageChanged);
		return outcome;
	}

	/// <summary>
	/// Shows <paramref name="game"/>'s prompt in a window of its own (<see cref="StartupWindow"/>) until the player picks an
	/// install, takes the third button, or gives up. Returns the install root, or null when the player gave up or closed
	/// the window, and whether the third button was taken. <paramref name="refused"/> is a folder found at start-up that
	/// cannot be the install, which the prompt first opens on with the reason showing. <paramref name="dismissKey"/> is
	/// <c>install_prompt.quit</c>, or <c>install_prompt.back</c> when the game chooser opened the prompt.
	/// </summary>
	public static (string? Chosen, bool Alternative) Run(LocalizationTable localization, string fontPath, GameIdentity game,
			string? refused, string dismissKey, string? alternativeKey = null) {
		var prompt = Create(localization, game, dismissKey, refused ?? "", alternativeKey: alternativeKey);
		if (refused != null) {
			prompt.Accept();
		}

		bool alternative = false;
		StartupWindow.Run(localization, game.PromptKeyPrefix + ".window_title", WindowWidth, WindowHeight, fontPath, owner => {
			var outcome = DrawPrompt(prompt, owner);
			alternative = outcome == PathPrompt.Outcome.Alternative;
			return outcome != PathPrompt.Outcome.Open;
		});
		return (prompt.Chosen, alternative);
	}
}
