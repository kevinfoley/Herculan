using Herculan.Engine.Install;
using Herculan.Engine.Localization;
using Herculan.Engine.Platform;
using Herculan.Es1.Install;

namespace Herculan.Es1.Host.Install;

/// <summary>
/// The window that asks for Metaltech: Earthsiege's disc before a minimum or medium install runs
/// (<see cref="Es1GameModule.PrepareInstall"/>): a disc folder, or a disc image, each a <see cref="PathPrompt"/> whose third
/// button switches to the other, taking only what <see cref="Es1Disc.Check"/> takes. This engine's own; retail's
/// <c>ES.BAT</c> changes to the CD drive and fails there.
/// </summary>
public static class Es1DiscPrompt {
	private const int WindowWidth = 640;
	private const int WindowHeight = 280;

	/// <summary>
	/// Shows the window until the player picks a disc or gives up. Returns the disc's folder or image, in full, or null
	/// when the player gave up or closed the window.
	/// </summary>
	/// <param name="localization">The table the strings are read from.</param>
	/// <param name="fontPath">The ImGui font.</param>
	/// <param name="installRoot">The install that needs the disc, which the message names.</param>
	/// <param name="dismissKey">The string of the button that gives up.</param>
	public static string? Run(LocalizationTable localization, string fontPath, string installRoot, string dismissKey) {
		var folder = new PathPrompt(localization, "es1_disc_needed", dismissKey, path => Refusal(localization, path),
			alternativeKey: "es1_disc_needed.use_image");
		var image = new PathPrompt(localization, "es1_image_needed", dismissKey, path => Refusal(localization, path),
			messageKey: "es1_disc_needed.message",
			fileFilter: new NativePathPicker.FileFilter(localization.GetStringOrKey("install.image_filter"), ["iso", "bin", "cue"]),
			alternativeKey: "es1_image_needed.use_folder");
		var showing = folder;
		string? chosen = null;
		StartupWindow.Run(localization, "es1_disc_needed.window_title", WindowWidth, WindowHeight, fontPath, owner => {
			switch (showing.Draw(owner, installRoot)) {
				case PathPrompt.Outcome.Accepted:
					chosen = showing.Chosen;
					return true;
				case PathPrompt.Outcome.Alternative:
					showing = showing == folder ? image : folder;
					return false;
				case PathPrompt.Outcome.Dismissed:
					return true;
				default:
					return false;
			}
		});
		return chosen;
	}

	/// <summary>Why <paramref name="path"/> cannot be the disc, in the player's language, or null when it can.</summary>
	/// <param name="localization">The table the reason is read from.</param>
	/// <param name="path">The folder or image chosen.</param>
	public static string? Refusal(LocalizationTable localization, string path) => Es1Disc.Check(path, out string? detail) switch {
		null => null,
		Es1Disc.Problem.Missing => string.Format(localization.GetStringOrKey("es1_disc.missing"), path),
		Es1Disc.Problem.Unreadable => string.Format(localization.GetStringOrKey("es1_disc.unreadable"), path, detail),
		_ => string.Format(localization.GetStringOrKey("es1_disc.not_es1"), path, Es1Disc.FlicPlayer.ToUpperInvariant(),
			Es1Disc.IntroFlic.ToUpperInvariant()),
	};
}
