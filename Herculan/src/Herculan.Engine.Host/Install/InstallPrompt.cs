using System.Numerics;
using Herculan.Engine.Content;
using Herculan.Engine.Host.Localization;
using ImGuiNET;
using Silk.NET.OpenGL;
using Silk.NET.OpenGL.Extensions.ImGui;

namespace Herculan.Engine.Host.Install;

/// <summary>
/// The window the host opens when <see cref="GameInstall.Locate"/> finds no install: it asks the player for
/// the Earthsiege 2 folder, by the platform's own folder picker (<see cref="NativeFolderPicker"/>) or by a
/// typed path, and accepts only a folder <see cref="GameInstall.IsInstallRoot"/> takes. Retail has no
/// equivalent; its installer wrote the path for it.
/// </summary>
sealed class InstallPrompt {
	private const int WindowWidth = 640;
	private const int WindowHeight = 220;

	private readonly LocalizationTable _localization;
	private string _path = "";
	private string? _error;
	private Task<string?>? _picking;
	private string? _chosen;

	private InstallPrompt(LocalizationTable localization) {
		_localization = localization;
	}

	/// <summary>
	/// Shows the prompt until the player picks an install or gives up. Returns the install root, or null
	/// when the player chose Quit or closed the window. <paramref name="fontPath"/> is the ImGui font.
	/// </summary>
	public static string? Run(LocalizationTable localization, string fontPath) {
		var prompt = new InstallPrompt(localization);
		using var window = new EngineWindow(prompt.Text("install_prompt.window_title"), WindowWidth, WindowHeight);

		ImGuiController? imgui = null;
		window.Load += (gl, input) => {
			imgui = new ImGuiController(gl, window.View, input, new ImGuiFontConfig(fontPath, 16));
		};

		window.Render += (deltaSeconds, gl) => {
			if (imgui == null) {
				return;
			}

			imgui.Update((float)deltaSeconds);
			gl.ClearColor(0.1f, 0.1f, 0.1f, 1f);
			gl.Clear(ClearBufferMask.ColorBufferBit);
			if (!prompt.Draw(window.View.Native?.Win32?.Hwnd ?? 0)) {
				window.Close();
			}
			imgui.Render();
		};

		window.Closing += () => {
			imgui?.Dispose();
			imgui = null;
		};

		window.Run();
		return prompt._chosen;
	}

	/// <summary>Builds one frame of the prompt. Returns false once the window should close.</summary>
	private bool Draw(nint owner) {
		if (_picking is { IsCompleted: true } picked) {
			_picking = null;
			if (picked.IsCompletedSuccessfully && picked.Result is { } folder) {
				_path = folder;
				if (Accept()) {
					return false;
				}
			} else if (picked.IsFaulted) {
				Console.Error.WriteLine($"The folder picker failed: {picked.Exception?.InnerException?.Message}");
				_error = Text("install_prompt.picker_failed");
			}
		}

		var viewport = ImGui.GetMainViewport();
		ImGui.SetNextWindowPos(viewport.WorkPos);
		ImGui.SetNextWindowSize(viewport.WorkSize);
		ImGui.Begin("##install", ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoMove
			| ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoBringToFrontOnFocus);

		ImGui.TextWrapped(Format("install_prompt.message", GameInstall.ArchiveFolderName));
		ImGui.Spacing();

		bool picking = _picking != null;
		ImGui.BeginDisabled(picking);

		string browseLabel = Text("install_prompt.browse");
		float spacing = ImGui.GetStyle().ItemSpacing.X;
		bool canBrowse = NativeFolderPicker.IsAvailable;
		float browseWidth = canBrowse
			? ImGui.CalcTextSize(browseLabel).X + ImGui.GetStyle().FramePadding.X * 2
			: 0f;

		ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X - (canBrowse ? browseWidth + spacing : 0f));
		bool closing = false;
		if (ImGui.InputTextWithHint("##path", Text("install_prompt.path_hint"), ref _path, 1024,
				ImGuiInputTextFlags.EnterReturnsTrue)) {
			closing = Accept();
		}

		if (canBrowse) {
			ImGui.SameLine();
			if (ImGui.Button(browseLabel)) {
				_error = null;
				_picking = NativeFolderPicker.PickAsync(Text("install_prompt.picker_title"), _path, owner);
			}
		}

		if (_error != null) {
			ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1f, 0.45f, 0.4f, 1f));
			ImGui.TextWrapped(_error);
			ImGui.PopStyleColor();
		} else if (picking) {
			ImGui.TextDisabled(Text("install_prompt.picker_open"));
		}

		// The two buttons sit at the foot of the window, half its width each.
		var buttonSize = new Vector2((ImGui.GetContentRegionAvail().X - spacing) * 0.5f, 0f);
		ImGui.SetCursorPosY(ImGui.GetWindowHeight() - ImGui.GetFrameHeight() - ImGui.GetStyle().WindowPadding.Y);
		if (ImGui.Button(Text("install_prompt.use_folder"), buttonSize)) {
			closing = Accept();
		}
		ImGui.SameLine();
		if (ImGui.Button(Text("install_prompt.quit"), buttonSize)) {
			closing = true;
		}

		ImGui.EndDisabled();
		ImGui.End();
		return !closing;
	}

	/// <summary>Takes <see cref="_path"/> as the install if it is one; otherwise says why not.</summary>
	private bool Accept() {
		string path = _path.Trim().Trim('"');
		if (path.Length > 0 && GameInstall.IsInstallRoot(path)) {
			_chosen = Path.GetFullPath(path);
			return true;
		}

		_error = path.Length == 0
			? Text("install_prompt.no_path")
			: Format("install_prompt.not_an_install", path, GameInstall.ArchiveFolderName);
		return false;
	}

	private string Text(string key) => _localization.GetString(key) ?? key;

	private string Format(string key, params object[] values) => string.Format(Text(key), values);
}
