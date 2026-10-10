using System.Numerics;
using HercWorks.Core.Data.File.Cfg;
using Herculan.Engine.Content;
using Herculan.Engine.Host.Install;
using Herculan.Engine.Host.Localization;
using Herculan.Engine.Host.Shell;
using Herculan.Engine.Install;
using Herculan.Engine.Platform;
using ImGuiNET;

namespace Herculan.Engine.Host.Settings;

/// <summary>
/// The floating "Settings" window opened from the menu bar: where the install is, which folder
/// <c>data\drive.cfg</c> names as the disc, the disc image HERCULAN reads in its place, and the two languages.
/// Retail has no such screen: its installer wrote <c>drive.cfg</c> and <c>language.cfg</c>, and nothing changed
/// them after.
///
/// <para>The install and the disc are read once per shell turn — the archives are mounted from them as the turn
/// starts (<see cref="GameContent.MountShell(string, GameDisc?)"/>) — so changing any of them brings the shell back
/// up through <c>restartShell</c>. A mission has none to give, and there they are greyed. The install is asked for
/// with the startup's own prompt (<see cref="InstallPrompt"/>), or made from a disc (<see cref="InstallPanel"/>);
/// the disc folder is written into the install's <c>drive.cfg</c> (<see cref="GameInstall.WriteDiscDirectory"/>),
/// where the original game reads it too, and the disc image onto HERCULAN's own line of it
/// (<see cref="GameInstall.WriteDiscImage"/>), which the original game never reads.</para>
///
/// <para>The game language is <c>data\language.cfg</c>'s letter (<see cref="GameInstall.WriteLanguage"/>), offered
/// for each retail language whose folder (<see cref="RetailInstaller.LanguageFolder"/>) is on the disc or in the
/// install. It is a shell turn's like the folders, so it too restarts the shell and is greyed in a mission. Choosing
/// it also switches the interface to the matching <c>.lang</c> file, when there is one. The interface language is
/// any <c>.lang</c> file, by the name it gives itself (<see cref="LocalizationTable.GetLanguages"/>), and changes at
/// once, in a mission too.</para>
/// </summary>
sealed class SettingsWindow : IDisposable {
	// In pixels on a 100% display (ScaledImGui.Scaled).
	private const float PanelWidth = 520f;
	private static readonly Vector2 PromptSize = new(640f, 240f);
	private static readonly Vector2 InstallSize = new(720f, 600f);
	private const string PromptId = "##folder_prompt";

	private enum PromptKind {
		Install,
		DiscFolder,
		DiscImage,
	}

	private readonly HostSession _session;
	private readonly Action? _restartShell;
	private PathPrompt? _prompt;
	private PromptKind _promptKind;
	private InstallPanel? _install;
	private bool _open;
	private string? _error;

	// The game languages the row offers, found as the window opens; finding them reads the disc.
	private IReadOnlyList<RetailInstaller.Language>? _gameLanguages;
	private readonly InterfaceLanguageCombo _interfaceLanguage;

	/// <param name="restartShell">Brings the shell back up on the new folders; null in a mission. It is called
	/// between this window's <c>Begin</c> and <c>End</c>, so it must not tear down the ImGui context there.</param>
	public SettingsWindow(HostSession session, Action? restartShell) {
		_session = session;
		_restartShell = restartShell;
		_interfaceLanguage = new InterfaceLanguageCombo(session.Localization);
	}

	/// <summary>Whether the window, or the install window it opened, is up. Set by the menu bar.</summary>
	public bool IsOpen {
		get => _open || _install != null;
		set => _open = value;
	}

	/// <summary>
	/// Takes the window down, and the folder prompt over it with nothing changed, and the install window unless a
	/// copy is running in it.
	/// </summary>
	public void Close() {
		_prompt = null;
		_error = null;
		_open = false;
		ForgetLanguages();
		if (_install is { Busy: false }) {
			_install.Dispose();
			_install = null;
		}
	}

	/// <summary>
	/// Draws the window and the install window, when each is up. <paramref name="owner"/> is the native window a picker
	/// belongs to, and <paramref name="centre"/> when each is centred on the window (<see cref="PanelCentring.Condition"/>).
	/// </summary>
	public void Draw(nint owner, ImGuiCond centre) {
		if (_open) {
			DrawSettings(owner, centre);
		}

		if (_install != null) {
			DrawInstall(owner, centre);
		}
	}

	private void DrawSettings(nint owner, ImGuiCond centre) {
		ImGui.SetNextWindowSize(new Vector2(ScaledImGui.Scaled(PanelWidth), 0f));
		ImGui.SetNextWindowPos(ImGui.GetMainViewport().GetCenter(), centre, new Vector2(0.5f, 0.5f));

		bool stayOpen = true;
		if (ImGui.Begin(_session.Localization.GetStringOrKey("settings.title") + "###settings", ref stayOpen, ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoResize)) {
			ImGui.SeparatorText(_session.Localization.GetStringOrKey("settings.folders"));
			string installRoot = _session.InstallRoot;
			if (FolderRow(_session.Localization.GetStringOrKey("settings.install_folder"), installRoot, "##change_install")) {
				OpenPrompt(InstallPrompt.Create(_session.Localization, "general.cancel", installRoot,
					"settings.install_message"), PromptKind.Install);
			}

			ImGui.BeginDisabled(_restartShell == null || _install != null);
			if (ImGui.Button(_session.Localization.GetStringOrKey("settings.install_from_disc"))) {
				_install = new InstallPanel(_session.Localization);
			}
			ImGui.EndDisabled();

			string? image = GameInstall.DiscImagePath(installRoot);
			string? disc = GameInstall.DiscDirectory(installRoot);
			if (FolderRow(_session.Localization.GetStringOrKey("settings.disc_folder"), disc ?? _session.Localization.GetStringOrKey("settings.no_disc"), "##change_disc")) {
				OpenPrompt(new PathPrompt(_session.Localization, "disc_prompt", "general.cancel", DiscRefusal, disc ?? ""),
					PromptKind.DiscFolder);
			}
			if (image != null) {
				ImGui.TextDisabled(_session.Localization.GetStringOrKey("settings.image_overrides"));
			}

			if (FolderRow(_session.Localization.GetStringOrKey("settings.disc_image"), image ?? _session.Localization.GetStringOrKey("settings.no_disc_image"), "##change_image",
					removable: image != null)) {
				OpenPrompt(new PathPrompt(_session.Localization, "image_prompt", "general.cancel", ImageRefusal, image ?? "",
					fileFilter: new NativePathPicker.FileFilter(_session.Localization.GetStringOrKey("install.image_filter"), ["iso", "bin", "cue"])),
					PromptKind.DiscImage);
			}

			ImGui.SeparatorText(_session.Localization.GetStringOrKey("settings.languages"));
			GameLanguageRow(installRoot);
			_interfaceLanguage.Draw();

			if (_error != null) {
				ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1f, 0.45f, 0.4f, 1f));
				ImGui.TextWrapped(_error);
				ImGui.PopStyleColor();
			}

			ImGui.Separator();
			if (ImGui.Button(_session.Localization.GetStringOrKey("general.close"), new Vector2(ImGui.GetContentRegionAvail().X, 0f))) {
				stayOpen = false;
			}

			DrawPrompt(owner, centre);
		}

		ImGui.End();

		if (!stayOpen) {
			_prompt = null;
			_error = null;
			_open = false;
			ForgetLanguages();
		}
	}

	// The install window, floating beside Settings. Using what it installed is the install's own Change.
	private void DrawInstall(nint owner, ImGuiCond centre) {
		ImGui.SetNextWindowSize(ScaledImGui.Scaled(InstallSize), ImGuiCond.Appearing);
		ImGui.SetNextWindowPos(ImGui.GetMainViewport().GetCenter(), centre, new Vector2(0.5f, 0.5f));
		var outcome = InstallPanel.Outcome.Open;
		if (ImGui.Begin(_session.Localization.GetStringOrKey("install.title") + "###install", ImGuiWindowFlags.NoCollapse)) {
			outcome = _install!.Draw(owner);
		}

		ImGui.End();

		if (outcome != InstallPanel.Outcome.Open) {
			string? installed = outcome == InstallPanel.Outcome.Installed ? _install!.Installed : null;
			_install!.Dispose();
			_install = null;
			if (installed != null) {
				_promptKind = PromptKind.Install;
				Apply(installed);
			}
		}
	}

	// A folder's label, its path and a Change button, with a Remove button beside it when the row can be
	// emptied; only a shell turn offers them. Remove takes the disc image out at once.
	private bool FolderRow(string label, string path, string buttonId, bool removable = false) {
		ImGui.TextUnformatted(label);
		ImGui.TextDisabled(path);
		ImGui.BeginDisabled(_restartShell == null);
		bool clicked = ImGui.Button(_session.Localization.GetStringOrKey("settings.change") + buttonId);
		bool removed = false;
		if (removable) {
			ImGui.SameLine();
			removed = ImGui.Button(_session.Localization.GetStringOrKey("settings.remove") + buttonId + "_remove");
		}
		ImGui.EndDisabled();
		if (_restartShell == null && ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) {
			ScaledImGui.Tooltip(_session.Localization.GetStringOrKey("settings.in_mission"));
		}

		if (removed) {
			_promptKind = PromptKind.DiscImage;
			Apply(null);
		}

		return clicked;
	}

	// The install's language.cfg as a combo of the retail languages whose folder is on the disc or in the install.
	// A letter that is none of them (Spanish) shows as its folder's name.
	private void GameLanguageRow(string installRoot) {
		string folder = OnlineManual.Language(installRoot).Folder;
		RetailInstaller.Language? current = null;
		foreach (var language in RetailInstaller.Languages) {
			if (RetailInstaller.LanguageFolder(language).Folder == folder) {
				current = language;
			}
		}

		_gameLanguages ??= RetailInstaller.Languages
			.Where(language => GameInstall.DiscFolderExists(installRoot, _session.Disc, RetailInstaller.LanguageFolder(language).Folder))
			.ToList();

		RetailInstaller.Language? chosen = null;
		ImGui.BeginDisabled(_restartShell == null);
		ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X * 0.5f);
		if (ImGui.BeginCombo(_session.Localization.GetStringOrKey("settings.game_language") + "###game_language",
				current is { } known ? _session.Localization.GetStringOrKey(InstallPanel.LanguageKey(known)) : folder)) {
			foreach (var language in _gameLanguages) {
				if (ImGui.Selectable(_session.Localization.GetStringOrKey(InstallPanel.LanguageKey(language)), language == current) && language != current) {
					chosen = language;
				}
			}
			ImGui.EndCombo();
		}
		ImGui.EndDisabled();
		if (_restartShell == null && ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) {
			ScaledImGui.Tooltip(_session.Localization.GetStringOrKey("settings.in_mission"));
		}

		if (chosen is { } next) {
			ApplyGameLanguage(next);
		}
	}

	// Writes the game language into language.cfg, switches the interface to the .lang file of the same language
	// when there is one, and brings the shell back up on it.
	private void ApplyGameLanguage(RetailInstaller.Language language) {
		string installRoot = _session.InstallRoot;
		try {
			GameInstall.WriteLanguage(installRoot, language);
		} catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
			_error = string.Format(_session.Localization.GetStringOrKey("settings.write_failed"), GameInstall.LanguageCfgName, ex.Message);
			return;
		}

		// The manual's page language is the locale code of the letter just written.
		string locale = OnlineManual.Language(installRoot).Code;
		if (_session.Localization.GetLocales().Contains(locale)) {
			_session.Localization.SelectedLocale = locale;
		}

		Close();
		_restartShell?.Invoke();
	}

	private void ForgetLanguages() {
		_gameLanguages = null;
		_interfaceLanguage.Forget();
	}

	private void OpenPrompt(PathPrompt prompt, PromptKind kind) {
		_prompt = prompt;
		_promptKind = kind;
		_error = null;
		ImGui.OpenPopup(PromptId);
	}

	// The prompt as a modal over the window, the size of the startup's own.
	private void DrawPrompt(nint owner, ImGuiCond centre) {
		if (_prompt == null) {
			return;
		}

		ImGui.SetNextWindowSize(ScaledImGui.Scaled(PromptSize));
		ImGui.SetNextWindowPos(ImGui.GetMainViewport().GetCenter(), centre, new Vector2(0.5f, 0.5f));
		if (!ImGui.BeginPopupModal(PromptId, ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoMove)) {
			return;
		}

		var outcome = _promptKind == PromptKind.Install
			? _prompt.Draw(owner, GameInstall.ArchiveFolderName)
			: _prompt.Draw(owner, Drive.FileName);
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

	// The install becomes the next turn's and is remembered for the next launch, unless the session says not to;
	// the disc folder and the disc
	// image are written into the install's drive.cfg, null taking the image out. Either way the shell comes
	// back up on it.
	private void Apply(string? path) {
		try {
			switch (_promptKind) {
				case PromptKind.DiscFolder:
					GameInstall.WriteDiscDirectory(_session.InstallRoot, path!);
					_session.ReopenDisc();
					break;
				case PromptKind.DiscImage:
					GameInstall.WriteDiscImage(_session.InstallRoot, path);
					_session.ReopenDisc();
					break;
				default:
					_session.InstallRoot = path!;
					if (_session.RememberInstall) {
						GameInstall.Remember(path!);
					}
					break;
			}
		} catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
			_error = string.Format(_session.Localization.GetStringOrKey("settings.write_failed"), Drive.FileName, ex.Message);
			return;
		}

		Close();
		_restartShell?.Invoke();
	}

	private string? DiscRefusal(string path) => GameInstall.CheckDiscDirectory(path) switch {
		null => null,
		GameInstall.DiscDirectoryProblem.Missing => string.Format(_session.Localization.GetStringOrKey("disc_prompt.missing"), path),
		GameInstall.DiscDirectoryProblem.Whitespace => string.Format(_session.Localization.GetStringOrKey("disc_prompt.whitespace"), path, Drive.FileName),
		_ => string.Format(_session.Localization.GetStringOrKey("disc_prompt.not_latin1"), path, Drive.FileName),
	};

	private string? ImageRefusal(string path) => GameInstall.CheckDiscImage(path, ShellHost.DiscCheckMovie, out string? detail) switch {
		null => null,
		GameInstall.DiscImageProblem.Missing => string.Format(_session.Localization.GetStringOrKey("image_prompt.missing"), path),
		GameInstall.DiscImageProblem.Unreadable => string.Format(_session.Localization.GetStringOrKey("image_prompt.unreadable"), path, detail),
		_ => string.Format(_session.Localization.GetStringOrKey("image_prompt.not_es2"), path),
	};

	/// <summary>Cancels a copy still running in the install window, which removes what it wrote.</summary>
	public void Dispose() {
		_install?.Dispose();
		_install = null;
	}
}
