using Herculan.Engine.Localization;
using Herculan.Engine.Platform;
using ImGuiNET;
using Silk.NET.OpenGL;

namespace Herculan.Engine.Install;

/// <summary>
/// A small window of its own that the host opens at start-up before any game's window, as the game chooser
/// (<see cref="GameChooser"/>) and the install prompt (<see cref="InstallPromptWindow"/>) are: the Settings window's
/// interface-language combo over the window's own content, since a first start has no Settings menu to reach it from.
/// This engine's own; retail has no counterpart.
/// </summary>
public static class StartupWindow {
	/// <summary>
	/// Shows the window until <paramref name="body"/> returns true or the player closes it. <paramref name="titleKey"/> is
	/// the title's string, read again when the combo changes the language. <paramref name="body"/> draws the content under
	/// the combo each frame, given the native window a picker belongs to. <paramref name="fontPath"/> is the ImGui font.
	/// </summary>
	public static void Run(LocalizationTable localization, string titleKey, int width, int height, string fontPath,
			Func<nint, bool> body) {
		var language = new InterfaceLanguageCombo(localization);
		string Title() => localization.GetStringOrKey(titleKey);
		using var window = new EngineWindow(Title(), width, height);

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

			nint owner = window.View.Native?.Win32?.Hwnd ?? 0;
			bool done = Draw(language, () => body(owner), out bool languageChanged);
			if (languageChanged) {
				window.Title = Title();
			}
			imgui.Render();

			// Last: Close raises Closing at once, which disposes the controller.
			if (done) {
				window.Close();
			}
		};

		window.Closing += () => {
			imgui?.Dispose();
			imgui = null;
		};

		window.Run();
	}

	/// <summary>
	/// The window's one frame of content, filling the viewport: the language combo over <paramref name="body"/>, whose
	/// answer it returns. <paramref name="languageChanged"/> is whether the combo changed the locale this frame.
	/// </summary>
	public static bool Draw(InterfaceLanguageCombo language, Func<bool> body, out bool languageChanged) {
		var viewport = ImGui.GetMainViewport();
		ImGui.SetNextWindowPos(viewport.WorkPos);
		ImGui.SetNextWindowSize(viewport.WorkSize);
		ImGui.Begin("##startup", ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoMove
			| ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoBringToFrontOnFocus);
		languageChanged = language.Draw();
		ImGui.Separator();
		bool done = body();
		ImGui.End();
		return done;
	}
}
