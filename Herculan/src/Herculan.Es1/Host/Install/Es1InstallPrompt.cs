using Herculan.Engine.Install;
using Herculan.Engine.Localization;

namespace Herculan.Es1.Host.Install;

/// <summary>
/// Metaltech: Earthsiege's install prompt: the engine's (<see cref="InstallPromptWindow"/>) for
/// <see cref="Es1GameModule.Identity"/>, with a third button that installs from a disc first (<see cref="InstallPanel"/>
/// with <see cref="Es1DiscInstaller"/>, in a <see cref="StartupWindow"/> of its own).
/// </summary>
public static class Es1InstallPrompt {
	private const int InstallWindowWidth = 720;
	private const int InstallWindowHeight = 460;

	/// <summary>
	/// Shows the prompt until the player picks an install, makes one from a disc, or gives up. Returns the install root,
	/// or null when the player gave up or closed the window. <paramref name="refused"/> and <paramref name="dismissKey"/>
	/// are <see cref="InstallPromptWindow.Run"/>'s.
	/// </summary>
	public static string? Run(LocalizationTable localization, string fontPath, string? refused, string dismissKey) {
		while (true) {
			var (chosen, install) = InstallPromptWindow.Run(localization, fontPath, Es1GameModule.Identity, refused, dismissKey,
				"install_prompt.install");
			refused = null;
			if (!install) {
				return chosen;
			}

			if (RunInstall(localization, fontPath) is { } installed) {
				return installed;
			}
		}
	}

	// The install window until the player installs and chooses to use the install, or gives up; null for the latter.
	private static string? RunInstall(LocalizationTable localization, string fontPath) {
		using var panel = new InstallPanel(localization, new Es1DiscInstaller(localization));
		string? installed = null;
		StartupWindow.Run(localization, "es1_install.window_title", InstallWindowWidth, InstallWindowHeight, fontPath, owner => {
			var outcome = panel.Draw(owner);
			if (outcome == InstallPanel.Outcome.Installed) {
				installed = panel.Installed;
			}

			return outcome != InstallPanel.Outcome.Open;
		});
		return installed;
	}
}
