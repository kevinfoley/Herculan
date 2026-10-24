using Herculan.Engine.Host.Localization;
using Herculan.Engine.Install;
using Herculan.Engine.Platform;
using ImGuiNET;
using Silk.NET.OpenGL;

namespace Herculan.Engine.Host.Install;

/// <summary>
/// The window the host opens when <see cref="GameInstall.Locate"/> finds no install, or at once under
/// <c>--ask-install</c>: it asks the player for
/// the Earthsiege 2 folder, by the platform's own folder picker or by a typed path, and accepts only a folder
/// <see cref="GameInstall.IsInstallRoot"/> takes, or offers to install from a disc first
/// (<see cref="InstallWindow"/>). Retail has no equivalent; its installer wrote the path for it. The Settings
/// menu asks the same way (<see cref="Create"/>). Above the prompt sits the Settings window's interface-language
/// combo, since a first start has no Settings menu to reach it from.
/// </summary>
static class InstallPrompt {
	internal const int WindowWidth = 640;
	internal const int WindowHeight = 260;

	/// <summary>
	/// The prompt's body: <c>install_prompt</c>'s strings, refusing any folder that is not an install, and one on a
	/// disc (<see cref="GameInstall.IsOnDisc"/>), which can hold every folder of an install but none of its saves.
	/// <paramref name="dismissKey"/> is the string of the button that gives up, and <paramref name="messageKey"/>
	/// the message's, when not the startup's own.
	/// </summary>
	public static PathPrompt Create(LocalizationTable localization, string dismissKey, string initialPath = "",
			string? messageKey = null, string? alternativeKey = null) =>
		new(localization, "install_prompt", dismissKey, path => Refusal(localization, path), initialPath, messageKey,
			alternativeKey: alternativeKey);

	/// <summary>Why <paramref name="path"/> cannot be the install, in the player's language, or null when it can.</summary>
	public static string? Refusal(LocalizationTable localization, string path) =>
		!GameInstall.IsInstallRoot(path)
			? string.Format(localization.GetStringOrKey("install_prompt.not_an_install"), path, GameInstall.ArchiveFolderName)
			: GameInstall.IsOnDisc(path) ? localization.GetStringOrKey("install_prompt.on_disc")
			: null;

	/// <summary>
	/// Shows the prompt until the player picks an install, makes one from a disc, or gives up. Returns the
	/// install root, or null when the player chose Quit or closed the window. <paramref name="fontPath"/> is
	/// the ImGui font. <paramref name="refused"/> is a folder found at startup that cannot be the install, which the
	/// prompt first opens on with the reason showing.
	/// </summary>
	public static string? Run(LocalizationTable localization, string fontPath, string? refused = null) {
		while (true) {
			var (chosen, install) = RunPrompt(localization, fontPath, refused);
			refused = null;
			if (!install) {
				return chosen;
			}

			if (InstallWindow.Run(localization, fontPath) is { } installed) {
				return installed;
			}
		}
	}

	// The prompt's window: the folder chosen, or whether the player asked to install from a disc instead.
	private static (string? Chosen, bool Install) RunPrompt(LocalizationTable localization, string fontPath, string? refused) {
		var prompt = Create(localization, "install_prompt.quit", refused ?? "", alternativeKey: "install_prompt.install");
		if (refused != null) {
			prompt.Accept();
		}

		var language = new InterfaceLanguageCombo(localization);
		bool install = false;
		string Title() => localization.GetStringOrKey("install_prompt.window_title");
		using var window = new EngineWindow(Title(), WindowWidth, WindowHeight);

		ScaledImGui? imgui = null;
		window.Load += (gl, input) => {
			imgui = new ScaledImGui(gl, window, input, fontPath);
		};

		window.Render += (deltaSeconds, gl) => {
			if (imgui == null) {
				return;
			}

			imgui.Update((float)deltaSeconds);
			gl.ClearColor(0.1f, 0.1f, 0.1f, 1f);
			gl.Clear(ClearBufferMask.ColorBufferBit);

			var outcome = Draw(prompt, language, window.View.Native?.Win32?.Hwnd ?? 0, out bool languageChanged);
			if (languageChanged) {
				window.Title = Title();
			}
			install = outcome == PathPrompt.Outcome.Alternative;
			imgui.Render();

			// Last: Close raises Closing at once, which disposes the controller.
			if (outcome != PathPrompt.Outcome.Open) {
				window.Close();
			}
		};

		window.Closing += () => {
			imgui?.Dispose();
			imgui = null;
		};

		window.Run();
		return (prompt.Chosen, install);
	}

	/// <summary>
	/// The window's one frame of content, filling the viewport: the language combo over the prompt.
	/// <paramref name="languageChanged"/> is whether the combo changed the locale this frame.
	/// </summary>
	internal static PathPrompt.Outcome Draw(PathPrompt prompt, InterfaceLanguageCombo language, nint owner, out bool languageChanged) {
		var viewport = ImGui.GetMainViewport();
		ImGui.SetNextWindowPos(viewport.WorkPos);
		ImGui.SetNextWindowSize(viewport.WorkSize);
		ImGui.Begin("##install", ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoMove
			| ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoBringToFrontOnFocus);
		languageChanged = language.Draw();
		ImGui.Separator();
		var outcome = prompt.Draw(owner, GameInstall.ArchiveFolderName);
		ImGui.End();
		return outcome;
	}
}
