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
	private const int WindowWidth = 640;
	private const int WindowHeight = 260;

	/// <summary>
	/// The prompt's body: <c>install_prompt</c>'s strings, refusing any folder that is not an install.
	/// <paramref name="dismissKey"/> is the string of the button that gives up, and <paramref name="messageKey"/>
	/// the message's, when not the startup's own.
	/// </summary>
	public static PathPrompt Create(LocalizationTable localization, string dismissKey, string initialPath = "",
			string? messageKey = null, string? alternativeKey = null) =>
		new(localization, "install_prompt", dismissKey, path => GameInstall.IsInstallRoot(path)
			? null
			: string.Format(localization.GetStringOrKey("install_prompt.not_an_install"),
				path, GameInstall.ArchiveFolderName), initialPath, messageKey, alternativeKey: alternativeKey);

	/// <summary>
	/// Shows the prompt until the player picks an install, makes one from a disc, or gives up. Returns the
	/// install root, or null when the player chose Quit or closed the window. <paramref name="fontPath"/> is
	/// the ImGui font.
	/// </summary>
	public static string? Run(LocalizationTable localization, string fontPath) {
		while (true) {
			var (chosen, install) = RunPrompt(localization, fontPath);
			if (!install) {
				return chosen;
			}

			if (InstallWindow.Run(localization, fontPath) is { } installed) {
				return installed;
			}
		}
	}

	// The prompt's window: the folder chosen, or whether the player asked to install from a disc instead.
	private static (string? Chosen, bool Install) RunPrompt(LocalizationTable localization, string fontPath) {
		var prompt = Create(localization, "install_prompt.quit", alternativeKey: "install_prompt.install");
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

			var viewport = ImGui.GetMainViewport();
			ImGui.SetNextWindowPos(viewport.WorkPos);
			ImGui.SetNextWindowSize(viewport.WorkSize);
			ImGui.Begin("##install", ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoMove
				| ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoBringToFrontOnFocus);
			if (language.Draw()) {
				window.Title = Title();
			}
			ImGui.Separator();
			var outcome = prompt.Draw(window.View.Native?.Win32?.Hwnd ?? 0, GameInstall.ArchiveFolderName);
			ImGui.End();
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
}
