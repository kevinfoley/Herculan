using Herculan.Engine.Install;
using Herculan.Engine.Localization;
using HercWorks.Core.Io.Transform;

namespace Herculan.Es2.Host.Install;

/// <summary>
/// Earthsiege 2's install prompt: the engine's (<see cref="InstallPromptWindow"/>) for <see cref="RetailGame.Earthsiege2"/>,
/// with a third button that installs from a disc first (<see cref="InstallWindow"/>). The Settings menu asks the same way
/// (<see cref="Create"/>).
/// </summary>
public static class InstallPrompt {
	/// <summary>
	/// The prompt's body: <c>install_prompt</c>'s strings, refusing what <see cref="InstallPromptWindow.Refusal"/> refuses
	/// for Earthsiege 2. <paramref name="dismissKey"/> is the string of the button that gives up, and
	/// <paramref name="messageKey"/> the message's, when not the startup's own.
	/// </summary>
	internal static PathPrompt Create(LocalizationTable localization, string dismissKey, string initialPath = "",
			string? messageKey = null, string? alternativeKey = null) =>
		InstallPromptWindow.Create(localization, RetailGame.Earthsiege2, dismissKey, initialPath, messageKey, alternativeKey);

	/// <summary>
	/// Shows the prompt until the player picks an install, makes one from a disc, or gives up. Returns the install root,
	/// or null when the player gave up or closed the window. <paramref name="fontPath"/> is the ImGui font.
	/// <paramref name="refused"/> is a folder found at startup that cannot be the install, which the prompt first opens on
	/// with the reason showing. <paramref name="dismissKey"/> is the giving-up button's string
	/// (<see cref="InstallPromptWindow.Run"/>).
	/// </summary>
	public static string? Run(LocalizationTable localization, string fontPath, string? refused = null,
			string dismissKey = "install_prompt.quit") {
		while (true) {
			var (chosen, install) = InstallPromptWindow.Run(localization, fontPath, RetailGame.Earthsiege2, refused, dismissKey,
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
