using Herculan.Engine.Install;
using Herculan.Engine.Localization;
using Herculan.Engine.Platform;
using ImGuiNET;
using Silk.NET.OpenGL;

namespace Herculan.Es2.Host.Install;

/// <summary>
/// Earthsiege 2's <see cref="InstallPanel"/> (<see cref="Es2DiscInstaller"/>) as a window of its own, which the startup's
/// <see cref="InstallPrompt"/> opens when the player has no install yet.
/// </summary>
static class InstallWindow {
	internal const int WindowWidth = 720;
	internal const int WindowHeight = 650;

	/// <summary>
	/// Shows the panel until the player installs and chooses to use the install, or gives up. Returns the install
	/// root, or null when the window was dismissed or closed. The window
	/// opens at <see cref="WindowWidth"/> by <see cref="WindowHeight"/> grown with the font
	/// (<see cref="ScaledImGui.ScaledWindowSize"/>).
	/// </summary>
	/// <param name="localization">The localization table.</param>
	/// <param name="fontPath">The ImGui font.</param>
	public static string? Run(LocalizationTable localization, string fontPath) {
		using var panel = new InstallPanel(localization, new Es2DiscInstaller(localization));
		string? installed = null;
		var (width, height) = ScaledImGui.ScaledWindowSize(WindowWidth, WindowHeight);
		using var window = new EngineWindow(localization.GetStringOrKey("install.window_title"), width, height);

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

			var outcome = Draw(panel, window.View.Native?.Win32?.Hwnd ?? 0);
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

	/// <summary>The window's one frame of content: the panel, filling the viewport.</summary>
	internal static InstallPanel.Outcome Draw(InstallPanel panel, nint owner) {
		var viewport = ImGui.GetMainViewport();
		ImGui.SetNextWindowPos(viewport.WorkPos);
		ImGui.SetNextWindowSize(viewport.WorkSize);
		ImGui.Begin("##install_window", ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoMove
			| ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoBringToFrontOnFocus);
		var outcome = panel.Draw(owner);
		ImGui.End();
		return outcome;
	}
}
