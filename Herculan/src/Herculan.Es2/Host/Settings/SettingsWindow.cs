using System.Numerics;
using HercWorks.Core.Data.File.Cfg;
using Herculan.Engine.Audio;
using Herculan.Engine.Content;
using Herculan.Engine.Install;
using Herculan.Es2.Host.Install;
using Herculan.Engine.Menus;
using Herculan.Es2.Host.Shell;
using Herculan.Engine.Platform;
using Herculan.Es2.Content;
using Herculan.Es2.Install;
using ImGuiNET;

namespace Herculan.Es2.Host.Settings;

/// <summary>
/// Earthsiege 2's Settings window (<see cref="SettingsWindowBase"/>): where the install is, which folder
/// <c>data\drive.cfg</c> names as the disc, the disc image HERCULAN reads in its place, the two languages, and Audio.
/// Retail's installer wrote <c>drive.cfg</c> and <c>language.cfg</c>, and nothing changed them after.
///
/// <para>The install and the disc are read once per shell turn — the archives are mounted from them as the turn
/// starts (<see cref="GameInstall.MountShell(string, GameDisc?)"/>) — so changing any of them brings the shell back
/// up through <c>restartShell</c>. The install is asked for with the startup's own prompt (<see cref="InstallPrompt"/>),
/// or made from a disc (<see cref="InstallPanel"/> with <see cref="Es2DiscInstaller"/>); the disc folder is written into
/// the install's <c>drive.cfg</c> (<see cref="GameInstall.WriteDiscDirectory"/>), where the original game reads it too,
/// and the disc image onto HERCULAN's own line of it (<see cref="GameInstall.WriteDiscImage"/>), which the original
/// game never reads.</para>
///
/// <para>The game language is <c>data\language.cfg</c>'s letter (<see cref="GameInstall.WriteLanguage"/>), offered
/// for each retail language whose folder (<see cref="RetailInstaller.LanguageFolder"/>) is on the disc or in the
/// install. It is a shell turn's like the folders, so it too restarts the shell and is greyed in a mission. Choosing
/// it also switches the interface to the matching <c>.lang</c> file, when there is one. The interface language is
/// any <c>.lang</c> file, by the name it gives itself, and changes at once, in a mission too.</para>
///
/// <para>Audio holds the music and sound-effects volumes (<see cref="AudioVolumes"/>), which retail has no control for.
/// They change what is sounding at once, in a mission too, and are saved as a slider is let go.</para>
/// </summary>
sealed class SettingsWindow : SettingsWindowBase, IDisposable {
	// Taller than the startup's own install window by its title bar.
	private static readonly Vector2 InstallSize = new(720f, 680f);

	internal enum PromptKind {
		Install,
		DiscFolder,
		DiscImage,
	}

	private readonly HostSession _session;
	private readonly Action? _restartShell;
	private InstallPanel? _install;

	// The game languages the row offers, found as the window opens; finding them reads the disc.
	private IReadOnlyList<RetailInstaller.Language>? _gameLanguages;

	/// <param name="restartShell">Brings the shell back up on the new folders; null in a mission. It is called
	/// between this window's <c>Begin</c> and <c>End</c>, so it must not tear down the ImGui context there.</param>
	public SettingsWindow(HostSession session, Action? restartShell)
		: base(session.Localization, "settings", inMission: restartShell == null) {
		_session = session;
		_restartShell = restartShell;
	}

	/// <summary>Whether the window, or the install window it opened, is up. Set by the menu bar.</summary>
	public override bool IsOpen {
		get => base.IsOpen || _install != null;
		set => base.IsOpen = value;
	}

	/// <summary>
	/// Takes the window down, and the folder prompt over it with nothing changed, and the install window unless a
	/// copy is running in it.
	/// </summary>
	public override void Close() {
		base.Close();
		if (_install is { Busy: false }) {
			_install.Dispose();
			_install = null;
		}
	}

	/// <summary>Draws the window and the install window, when each is up.</summary>
	public override void Draw(nint owner, ImGuiCond centre) {
		base.Draw(owner, centre);
		if (_install != null) {
			DrawInstall(owner, centre);
		}
	}

	protected override void DrawRows() {
		ImGui.SeparatorText(Localization.GetStringOrKey("settings.folders"));
		string installRoot = _session.InstallRoot;
		if (FolderRow(Localization.GetStringOrKey("settings.install_folder"), installRoot, "##change_install")) {
			OpenPrompt(PromptKind.Install);
		}

		ImGui.BeginDisabled(InMission || _install != null);
		if (ImGui.Button(Localization.GetStringOrKey("settings.install_from_disc"))) {
			OpenInstall();
		}
		ImGui.EndDisabled();

		string? image = GameInstall.DiscImagePath(installRoot);
		string? disc = GameInstall.DiscDirectory(installRoot);
		if (FolderRow(Localization.GetStringOrKey("settings.disc_folder"), disc ?? Localization.GetStringOrKey("settings.no_disc"), "##change_disc")) {
			OpenPrompt(PromptKind.DiscFolder);
		}
		if (image != null) {
			ImGui.TextDisabled(Localization.GetStringOrKey("settings.image_overrides"));
		}

		if (FolderRow(Localization.GetStringOrKey("settings.disc_image"), image ?? Localization.GetStringOrKey("settings.no_disc_image"), "##change_image",
				removable: image != null)) {
			OpenPrompt(PromptKind.DiscImage);
		}

		ImGui.SeparatorText(Localization.GetStringOrKey("settings.languages"));
		GameLanguageRow(installRoot);
		InterfaceLanguage.Draw();

		ImGui.SeparatorText(Localization.GetStringOrKey("settings.audio"));
		AudioRows();
	}

	protected override void Forget() => _gameLanguages = null;

	// The install window, floating beside Settings. Using what it installed is the install's own Change.
	private void DrawInstall(nint owner, ImGuiCond centre) {
		ImGui.SetNextWindowSize(ScaledImGui.Scaled(InstallSize), ImGuiCond.Appearing);
		ImGui.SetNextWindowPos(ImGui.GetMainViewport().GetCenter(), centre, new Vector2(0.5f, 0.5f));
		var outcome = InstallPanel.Outcome.Open;
		if (ImGui.Begin(Localization.GetStringOrKey("install.title") + "###install", ImGuiWindowFlags.NoCollapse)) {
			outcome = _install!.Draw(owner);
		}

		ImGui.End();

		if (outcome != InstallPanel.Outcome.Open) {
			string? installed = outcome == InstallPanel.Outcome.Installed ? _install!.Installed : null;
			_install!.Dispose();
			_install = null;
			if (installed != null) {
				ApplyInstall(installed);
			}
		}
	}

	// A folder's label, its path and a Change button, with a Remove button beside it when the row can be
	// emptied; only a shell turn offers them. Remove takes the disc image out at once.
	private bool FolderRow(string label, string path, string buttonId, bool removable = false) {
		ImGui.TextUnformatted(label);
		ImGui.TextDisabled(path);
		ImGui.BeginDisabled(InMission);
		bool clicked = ImGui.Button(Localization.GetStringOrKey("settings.change") + buttonId);
		bool removed = false;
		if (removable) {
			ImGui.SameLine();
			removed = ImGui.Button(Localization.GetStringOrKey("settings.remove") + buttonId + "_remove");
		}
		ImGui.EndDisabled();
		InMissionTooltip();

		if (removed) {
			ApplyDiscImage(null);
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
		ImGui.BeginDisabled(InMission);
		ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X * 0.5f);
		if (ImGui.BeginCombo(Localization.GetStringOrKey("settings.game_language") + "###game_language",
				current is { } known ? Localization.GetStringOrKey(Es2DiscInstaller.LanguageKey(known)) : folder)) {
			foreach (var language in _gameLanguages) {
				if (ImGui.Selectable(Localization.GetStringOrKey(Es2DiscInstaller.LanguageKey(language)), language == current) && language != current) {
					chosen = language;
				}
			}
			ImGui.EndCombo();
		}
		ImGui.EndDisabled();
		InMissionTooltip();

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
			Error = string.Format(Localization.GetStringOrKey("settings.write_failed"), GameInstall.LanguageCfgName, ex.Message);
			return;
		}

		// The manual's page language is the locale code of the letter just written.
		string locale = OnlineManual.Language(installRoot).Code;
		if (Localization.GetLocales().Contains(locale)) {
			Localization.SelectedLocale = locale;
		}

		Restart();
	}

	// The two volumes as percentage sliders. Dragging sets the volume at once; letting go saves it.
	private void AudioRows() {
		var volumes = AudioVolumes.Current;
		volumes.Music = VolumeSlider("settings.music_volume", volumes.Music, out bool musicDone);
		volumes.Effects = VolumeSlider("settings.effects_volume", volumes.Effects, out bool effectsDone);
		if (musicDone || effectsDone) {
			volumes.Save();
		}
	}

	private float VolumeSlider(string key, float volume, out bool done) {
		int percent = (int)MathF.Round(volume * 100f);
		ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X * 0.5f);
		ImGui.SliderInt(Localization.GetStringOrKey(key) + "###" + key, ref percent, 0, 100, "%d%%", ImGuiSliderFlags.AlwaysClamp);
		done = ImGui.IsItemDeactivatedAfterEdit();
		return percent / 100f;
	}

	/// <summary>
	/// Raises a folder prompt over the window, starting from the folder or image the
	/// install names now. The window's Change buttons raise it; a test lays it out without one.
	/// </summary>
	/// <param name="kind">The prompt's kind.</param>
	internal void OpenPrompt(PromptKind kind) {
		string installRoot = _session.InstallRoot;
		switch (kind) {
			case PromptKind.Install:
				RaisePrompt(InstallPrompt.Create(Localization, "general.cancel", installRoot, "settings.install_message"),
					ApplyInstall, InstallPromptWindow.MessageArguments);
				break;
			case PromptKind.DiscFolder:
				RaisePrompt(new PathPrompt(Localization, "disc_prompt", "general.cancel", DiscRefusal,
					GameInstall.DiscDirectory(installRoot) ?? ""), ApplyDiscFolder, Drive.FileName);
				break;
			default:
				RaisePrompt(new PathPrompt(Localization, "image_prompt", "general.cancel", ImageRefusal,
					GameInstall.DiscImagePath(installRoot) ?? "",
					fileFilter: new NativePathPicker.FileFilter(Localization.GetStringOrKey("install.image_filter"), ["iso", "bin", "cue"])),
					ApplyDiscImage, Drive.FileName);
				break;
		}
	}

	/// <summary>Opens the install window beside the window, as its Install from a Disc button does.</summary>
	internal void OpenInstall() => _install = new InstallPanel(Localization, new Es2DiscInstaller(Localization));

	// The install becomes the next turn's and is remembered for the next launch, unless the session says not to.
	private void ApplyInstall(string path) {
		_session.InstallRoot = path;
		if (_session.RememberInstall) {
			try {
				RetailInstalls.Remember(Es2GameModule.Identity, path);
			} catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
				Error = string.Format(Localization.GetStringOrKey("settings.write_failed"), Drive.FileName, ex.Message);
				return;
			}
		}

		Restart();
	}

	// The disc folder, written into the install's drive.cfg.
	private void ApplyDiscFolder(string path) => WriteDrive(() => GameInstall.WriteDiscDirectory(_session.InstallRoot, path));

	// The disc image, written into the install's drive.cfg; null takes it out.
	private void ApplyDiscImage(string? path) => WriteDrive(() => GameInstall.WriteDiscImage(_session.InstallRoot, path));

	// Writes drive.cfg, then opens the disc it names and brings the shell back up on it.
	private void WriteDrive(Action write) {
		try {
			write();
			_session.ReopenDisc();
		} catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
			Error = string.Format(Localization.GetStringOrKey("settings.write_failed"), Drive.FileName, ex.Message);
			return;
		}

		Restart();
	}

	private void Restart() {
		Close();
		_restartShell?.Invoke();
	}

	private string? DiscRefusal(string path) => GameInstall.CheckDiscDirectory(path) switch {
		null => null,
		GameInstall.DiscDirectoryProblem.Missing => string.Format(Localization.GetStringOrKey("disc_prompt.missing"), path),
		GameInstall.DiscDirectoryProblem.Whitespace => string.Format(Localization.GetStringOrKey("disc_prompt.whitespace"), path, Drive.FileName),
		_ => string.Format(Localization.GetStringOrKey("disc_prompt.not_latin1"), path, Drive.FileName),
	};

	private string? ImageRefusal(string path) => GameInstall.CheckDiscImage(path, ShellHost.DiscCheckMovie, out string? detail) switch {
		null => null,
		GameInstall.DiscImageProblem.Missing => string.Format(Localization.GetStringOrKey("image_prompt.missing"), path),
		GameInstall.DiscImageProblem.Unreadable => string.Format(Localization.GetStringOrKey("image_prompt.unreadable"), path, detail),
		_ => string.Format(Localization.GetStringOrKey("image_prompt.not_es2"), path),
	};

	/// <summary>Cancels a copy still running in the install window, which removes what it wrote.</summary>
	public void Dispose() {
		_install?.Dispose();
		_install = null;
	}
}
