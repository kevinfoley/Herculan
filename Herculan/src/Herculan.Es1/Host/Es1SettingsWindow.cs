using System.Numerics;
using Herculan.Engine.Install;
using Herculan.Engine.Localization;
using Herculan.Engine.Menus;
using Herculan.Engine.Platform;
using Herculan.Es1.Install;
using HercWorks.Core.Io.Transform;
using ImGuiNET;

namespace Herculan.Es1.Host;

/// <summary>
/// Metaltech: Earthsiege's floating "Settings" window, opened from the menu bar: where the install is, the disc
/// the game's movies are played from (<see cref="Es1Disc"/>), the interface language and the update check. Retail has no
/// such screen: its installer wrote <c>ES.BAT</c>, and nothing changed it after. Earthsiege 2's window
/// (<c>Herculan.Es2.Host.Settings.SettingsWindow</c>) is its counterpart; this one has no Install from a Disc, no game
/// language, since nothing ES1 reads changes with it, and no Audio.
///
/// <para>The launcher reads the install and the disc once, as <c>ESCD.EXE</c> starts, so changing either ends the shell's
/// turn and starts the launcher again on them (<see cref="GameSwitch"/>), as typing <c>ES</c> again would. The install is
/// asked for with the start-up's own prompt (<see cref="InstallPromptWindow"/>), and the disc as a folder or an image.
/// A disc is not used while the environment sets <c>ALT_DRIVE</c>, which the window says. A mission has no shell turn to
/// end, and there the install and disc rows are greyed, as Earthsiege 2's are.</para>
/// </summary>
sealed class Es1SettingsWindow : IDrawnMenuBarPanel {
	// In pixels on a 100% display (ScaledImGui.Scaled).
	private const float PanelWidth = 520f;
	private static readonly Vector2 PromptSize = new(640f, 240f);
	private const string PromptId = "##es1_folder_prompt";

	private enum PromptKind {
		Install,
		DiscFolder,
		DiscImage,
	}

	private readonly LocalizationTable _localization;
	private readonly string _installRoot;
	private readonly Action<string>? _restart;
	private readonly InterfaceLanguageCombo _interfaceLanguage;
	private readonly UpdateCheckRows _updates;
	private PathPrompt? _prompt;
	private PromptKind _promptKind;
	private bool _promptRaised;
	private bool _open;
	private string? _error;

	// The disc kept, read as the window opens (Es1Disc.Remembered), or null for none.
	private string? _disc;
	private bool _discRead;

	/// <param name="installRoot">The install the turn runs on.</param>
	/// <param name="restart">Ends the shell's turn and starts the launcher again on the install it is given; null in a
	/// mission. It is called between this window's <c>Begin</c> and <c>End</c>, so it must not tear down the ImGui context
	/// there.</param>
	public Es1SettingsWindow(LocalizationTable localization, string installRoot, Action<string>? restart) {
		_localization = localization;
		_installRoot = installRoot;
		_restart = restart;
		_interfaceLanguage = new InterfaceLanguageCombo(localization);
		_updates = new UpdateCheckRows(localization);
	}

	public bool IsOpen {
		get => _open;
		set => _open = value;
	}

	void IMenuBarPanel.Open() => IsOpen = true;

	/// <summary>Takes the window down, and the prompt over it with nothing changed.</summary>
	void IMenuBarPanel.BackOut() => Close();

	private void Close() {
		_prompt = null;
		_error = null;
		_open = false;
		_discRead = false;
		_updates.Forget();
		_interfaceLanguage.Forget();
	}

	public void Draw(nint owner, ImGuiCond centre) {
		if (!_open) {
			return;
		}

		ImGui.SetNextWindowSize(new Vector2(ScaledImGui.Scaled(PanelWidth), 0f));
		ImGui.SetNextWindowPos(ImGui.GetMainViewport().GetCenter(), centre, new Vector2(0.5f, 0.5f));

		bool stayOpen = true;
		if (ImGui.Begin(_localization.GetStringOrKey("settings.title") + "###es1_settings", ref stayOpen, ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoResize)) {
			ImGui.SeparatorText(_localization.GetStringOrKey("es1_settings.folders"));
			ImGui.TextUnformatted(_localization.GetStringOrKey("settings.install_folder"));
			ImGui.TextDisabled(_installRoot);
			ImGui.BeginDisabled(_restart == null);
			if (ImGui.Button(_localization.GetStringOrKey("settings.change") + "##change_install")) {
				OpenPrompt(PromptKind.Install);
			}

			ImGui.EndDisabled();
			InMissionTooltip();

			DiscRows();

			ImGui.SeparatorText(_localization.GetStringOrKey("es1_settings.language"));
			_interfaceLanguage.Draw();

			ImGui.SeparatorText(_localization.GetStringOrKey("settings.updates"));
			_updates.Draw();

			if (_error != null) {
				ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1f, 0.45f, 0.4f, 1f));
				ImGui.TextWrapped(_error);
				ImGui.PopStyleColor();
			}

			ImGui.Separator();
			if (ImGui.Button(_localization.GetStringOrKey("general.close"), new Vector2(ImGui.GetContentRegionAvail().X, 0f))) {
				stayOpen = false;
			}

			DrawPrompt(owner, centre);
		}

		ImGui.End();

		if (!stayOpen) {
			Close();
		}
	}

	// The disc's label, what it plays, its folder or image, and the buttons that choose one or take it out.
	private void DiscRows() {
		if (!_discRead) {
			_disc = Es1Disc.Remembered();
			_discRead = true;
		}

		string? disc = _disc;
		ImGui.TextUnformatted(_localization.GetStringOrKey("es1_settings.disc"));
		ImGui.TextDisabled(disc ?? _localization.GetStringOrKey("settings.no_disc_image"));
		ImGui.TextWrapped(_localization.GetStringOrKey("es1_settings.disc_note"));
		if (Environment.GetEnvironmentVariable(Es1Launcher.AltDriveVariable) != null) {
			ImGui.TextDisabled(string.Format(_localization.GetStringOrKey("es1_settings.alt_drive_overrides"), Es1Launcher.AltDriveVariable));
		}

		ImGui.BeginDisabled(_restart == null);
		if (ImGui.Button(_localization.GetStringOrKey("es1_settings.choose_disc_folder"))) {
			OpenPrompt(PromptKind.DiscFolder);
		}

		InMissionTooltip();
		ImGui.SameLine();
		if (ImGui.Button(_localization.GetStringOrKey("es1_settings.choose_disc_image"))) {
			OpenPrompt(PromptKind.DiscImage);
		}

		InMissionTooltip();
		if (disc != null) {
			ImGui.SameLine();
			if (ImGui.Button(_localization.GetStringOrKey("settings.remove") + "##remove_disc")) {
				if (Es1Disc.Forget()) {
					Restart(_installRoot);
				} else {
					_error = string.Format(_localization.GetStringOrKey("es1_settings.save_failed"), Es1Disc.PathFile);
				}
			}

			InMissionTooltip();
		}

		ImGui.EndDisabled();
	}

	// In a mission, where the item just drawn is greyed, the tooltip that says where it can be changed.
	private void InMissionTooltip() {
		if (_restart == null && ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) {
			ScaledImGui.Tooltip(_localization.GetStringOrKey("settings.in_mission"));
		}
	}

	// Raises the prompt of kind over the window, starting from the install, or the disc when it is of that kind.
	private void OpenPrompt(PromptKind kind) {
		string? disc = _disc;
		_prompt = kind switch {
			PromptKind.Install => InstallPromptWindow.Create(_localization, RetailGame.Earthsiege1, "general.cancel", _installRoot,
				"es1_settings.install_message"),
			PromptKind.DiscFolder => new PathPrompt(_localization, "es1_disc_prompt", "general.cancel", DiscRefusal,
				disc != null && !Es1Disc.IsImage(disc) ? disc : ""),
			_ => new PathPrompt(_localization, "es1_image_prompt", "general.cancel", DiscRefusal,
				disc != null && Es1Disc.IsImage(disc) ? disc : "",
				fileFilter: new NativePathPicker.FileFilter(_localization.GetStringOrKey("install.image_filter"), ["iso", "bin", "cue"])),
		};
		_promptKind = kind;
		_promptRaised = false;
		_error = null;
	}

	// The prompt as a modal over the window, the size of the start-up's own; opened here so that its popup ID is the
	// window's.
	private void DrawPrompt(nint owner, ImGuiCond centre) {
		if (_prompt == null) {
			return;
		}

		if (!_promptRaised) {
			ImGui.OpenPopup(PromptId);
			_promptRaised = true;
		}

		ImGui.SetNextWindowSize(ScaledImGui.Scaled(PromptSize));
		ImGui.SetNextWindowPos(ImGui.GetMainViewport().GetCenter(), centre, new Vector2(0.5f, 0.5f));
		if (!ImGui.BeginPopupModal(PromptId, ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoMove)) {
			return;
		}

		var outcome = _promptKind == PromptKind.Install ? InstallPromptWindow.DrawPrompt(_prompt, owner) : _prompt.Draw(owner);
		if (outcome != PathPrompt.Outcome.Open) {
			ImGui.CloseCurrentPopup();
		}

		ImGui.EndPopup();

		if (outcome == PathPrompt.Outcome.Accepted && _prompt.Chosen is { } chosen) {
			_prompt = null;
			Apply(chosen);
		} else if (outcome == PathPrompt.Outcome.Dismissed) {
			_prompt = null;
		}
	}

	// The install the launcher starts again on, or the disc kept for it.
	private void Apply(string path) {
		if (_promptKind == PromptKind.Install) {
			Restart(path);
			return;
		}

		if (!Es1Disc.Remember(path)) {
			_error = string.Format(_localization.GetStringOrKey("es1_settings.save_failed"), Es1Disc.PathFile);
			return;
		}

		Restart(_installRoot);
	}

	private void Restart(string installRoot) {
		Close();
		_restart?.Invoke(installRoot);
	}

	private string? DiscRefusal(string path) => Es1Disc.Check(path, out string? detail) switch {
		null => null,
		Es1Disc.Problem.Missing => string.Format(_localization.GetStringOrKey("es1_disc.missing"), path),
		Es1Disc.Problem.Unreadable => string.Format(_localization.GetStringOrKey("es1_disc.unreadable"), path, detail),
		_ => string.Format(_localization.GetStringOrKey("es1_disc.not_es1"), path, Es1Disc.FlicPlayer.ToUpperInvariant(),
			Es1Disc.IntroFlic.ToUpperInvariant()),
	};
}
