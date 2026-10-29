using System.Diagnostics;
using System.Numerics;
using Herculan.Engine.Platform;
using ImGuiNET;

namespace Herculan.Engine.Editor;

/// <summary>
/// Play in the editor run on its own, which has no simulator: the game, built beside the editor, started as a program of
/// its own on the mission's saved files, as its command line plays a mission named by its <c>.MSN</c>
/// (docs/herculan/herculan-command-line.md), and watched until it exits. While it runs the editor shows a modal saying so,
/// which takes the editor's mouse and keyboard.
/// </summary>
internal sealed class PlayInGame {
	// In pixels on a 100% display (ScaledImGui.Scaled).
	private const float PanelWidth = 380f;

	private const string Title = "Playing###play-in-game";

	private Process? _game;
	private bool _raised;

	/// <summary>The game's program beside the editor's, or null when it is not there.</summary>
	public static string? GamePath {
		get {
			string path = Path.Combine(AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "Herculan.exe" : "Herculan");
			return File.Exists(path) ? path : null;
		}
	}

	/// <summary>The mission being played, while the game runs; null otherwise.</summary>
	public string? Mission { get; private set; }

	/// <summary>Whether the game is running a mission the editor started.</summary>
	public bool Running => _game != null;

	/// <summary>
	/// Starts the game on <paramref name="mission"/> in <paramref name="installRoot"/>, on the mission's own theater and time of
	/// day as the editor shows it (<c>--own-world</c>), in developer mode (<c>--developer</c>); whether it started, and why not
	/// in <paramref name="failure"/>.
	/// </summary>
	public bool Start(string installRoot, string mission, out string? failure) {
		failure = null;
		if (GamePath is not { } path) {
			failure = "The game is not beside the editor.";
			return false;
		}

		var start = new ProcessStartInfo(path) { UseShellExecute = false, WorkingDirectory = AppContext.BaseDirectory };
		start.ArgumentList.Add(installRoot);
		start.ArgumentList.Add(mission);
		start.ArgumentList.Add("--own-world");
		start.ArgumentList.Add("--developer");
		try {
			_game = Process.Start(start);
		} catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException or InvalidOperationException) {
			failure = ex.Message;
		}

		if (_game == null) {
			failure ??= "The game did not start.";
			return false;
		}

		Mission = mission;
		Console.WriteLine($"Playing {mission} in {path} (process {_game.Id}).");
		return true;
	}

	/// <summary>
	/// The game's exit code once it has exited since the last call, which ends the play; null while it runs or when none
	/// was started.
	/// </summary>
	public int? TakeExit() {
		if (_game is not { HasExited: true } game) {
			return null;
		}

		int code = game.ExitCode;
		game.Dispose();
		_game = null;
		Mission = null;
		return code;
	}

	/// <summary>Draws the modal while the game runs. Call once per frame, inside the ImGui frame, outside any menu.</summary>
	public void Draw() {
		if (_game == null) {
			// The frame after the game exits takes the modal down, from inside it as ImGui closes a popup.
			if (_raised) {
				_raised = false;
				if (ImGui.BeginPopupModal(Title)) {
					ImGui.CloseCurrentPopup();
					ImGui.EndPopup();
				}
			}

			return;
		}

		if (!_raised) {
			ImGui.OpenPopup(Title);
			_raised = true;
		}

		ImGui.SetNextWindowSize(new Vector2(ScaledImGui.Scaled(PanelWidth), 0f));
		ImGui.SetNextWindowPos(ImGui.GetMainViewport().GetCenter(), ImGuiCond.Appearing, new Vector2(0.5f, 0.5f));
		if (!ImGui.BeginPopupModal(Title, ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove)) {
			_raised = false;
			return;
		}

		ImGui.TextWrapped($"{Mission} is being played in the game. The editor comes back when the game closes.");
		ImGui.EndPopup();
	}
}
