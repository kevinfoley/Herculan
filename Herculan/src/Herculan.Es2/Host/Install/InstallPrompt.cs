using Herculan.Engine.Install;
using Herculan.Engine.Localization;

namespace Herculan.Es2.Host.Install;

/// <summary>
/// Earthsiege 2's install prompt: the engine's (<see cref="InstallPromptWindow"/>) for <see cref="Es2GameModule.Identity"/>,
/// with a third button that installs from a disc first (<see cref="InstallWindow"/>). The Settings menu asks the same way
/// (<see cref="Create"/>).
/// </summary>
public static class InstallPrompt {
	/// <summary>
	/// The prompt's body: <c>install_prompt</c>'s strings, refusing what <see cref="InstallPromptWindow.Refusal"/> refuses
	/// for Earthsiege 2.
	/// </summary>
	/// <param name="localization">The localization table.</param>
	/// <param name="dismissKey">The string of the button that gives up.</param>
	/// <param name="initialPath">The path the prompt starts at.</param>
	/// <param name="messageKey">The string of the message, when not the startup's own.</param>
	/// <param name="alternativeKey">The string of the alternative button, or null for none.</param>
	internal static PathPrompt Create(LocalizationTable localization, string dismissKey, string initialPath = "",
			string? messageKey = null, string? alternativeKey = null) =>
		InstallPromptWindow.Create(localization, Es2GameModule.Identity, dismissKey, initialPath, messageKey, alternativeKey);

	/// <summary>
	/// Shows the prompt until the player picks an install, makes one from a disc, or gives up. Returns the install root,
	/// or null when the player gave up or closed the window.
	/// </summary>
	/// <param name="localization">The localization table.</param>
	/// <param name="fontPath">The ImGui font.</param>
	/// <param name="refused">A folder found at startup that cannot be the install, which the prompt first opens on
	/// with the reason showing.</param>
	/// <param name="dismissKey">The giving-up button's string (<see cref="InstallPromptWindow.Run"/>).</param>
	public static string? Run(LocalizationTable localization, string fontPath, string? refused = null,
			string dismissKey = "install_prompt.quit") {
		while (true) {
			var (chosen, install) = InstallPromptWindow.Run(localization, fontPath, Es2GameModule.Identity, refused, dismissKey,
				"install_prompt.install");
			refused = null;
			if (!install) {
				return chosen;
			}

			if (InstallWindow.Run(localization, fontPath) is { } installed) {
				return installed;
			}
		}
	}
}
