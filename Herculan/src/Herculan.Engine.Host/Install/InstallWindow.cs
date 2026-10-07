using Herculan.Engine.Host.Localization;
using Herculan.Engine.Platform;
using ImGuiNET;
using Silk.NET.OpenGL;

namespace Herculan.Engine.Host.Install;

/// <summary>
/// <see cref="InstallPanel"/> as a window of its own, which the startup's <see cref="InstallPrompt"/> opens when the
/// player has no install yet.
/// </summary>
static class InstallWindow {
	private const int WindowWidth = 720;
	private const int WindowHeight = 600;

	/// <summary>
	/// Shows the panel until the player installs and chooses to use the install, or gives up. Returns the install
	/// root, or null when the window was dismissed or closed. <paramref name="fontPath"/> is the ImGui font.
	/// </summary>
	public static string? Run(LocalizationTable localization, string fontPath) {
		using var panel = new InstallPanel(localization);
		string? installed = null;
		using var window = new EngineWindow(localization.GetStringOrKey("install.window_title"),
			WindowWidth, WindowHeight);

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
			ImGui.Begin("##install_window", ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoMove
				| ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoBringToFrontOnFocus);
			var outcome = panel.Draw(window.View.Native?.Win32?.Hwnd ?? 0);
			ImGui.End();
			if (outcome == InstallPanel.Outcome.Installed) {
				installed = panel.Installed;
			}

			imgui.Render();

			// Last: Close raises Closing at once, which disposes the controller.
			if (outcome != InstallPanel.Outcome.Open) {
				window.Close();
			}
		};

		window.Closing += () => {
			imgui?.Dispose();
			imgui = null;
		};

		window.Run();
		return installed;
	}
}
