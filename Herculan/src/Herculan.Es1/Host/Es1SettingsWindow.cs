using Herculan.Engine.Install;
using Herculan.Engine.Localization;
using Herculan.Engine.Menus;
using Herculan.Engine.Platform;
using Herculan.Es1.Host.Install;
using Herculan.Es1.Install;
using ImGuiNET;

namespace Herculan.Es1.Host;

/// <summary>
/// Metaltech: Earthsiege's Settings window (<see cref="SettingsWindowBase"/>): where the install is, the disc the game's
/// movies are played from (<see cref="Es1Disc"/>), and the interface language. Retail's installer wrote <c>ES.BAT</c>, and
/// nothing changed it after. Unlike Earthsiege 2's, it has no Install from a Disc, no game language, since nothing ES1
/// reads changes with it, and no Audio.
///
/// <para>The launcher reads the install and the disc once, as <c>ESCD.EXE</c> starts, so changing either ends the shell's
/// turn and starts the launcher again on them (<see cref="GameSwitch"/>), as typing <c>ES</c> again would. The install is
/// asked for with the start-up's own prompt (<see cref="InstallPromptWindow"/>), and the disc as a folder or an image.
/// A disc is not used while the environment sets <c>ALT_DRIVE</c>, which the window says.</para>
/// </summary>
sealed class Es1SettingsWindow : SettingsWindowBase {
	private readonly string _installRoot;
	private readonly Action<string>? _restart;

	// The disc kept, read as the window opens (Es1Disc.Remembered), or null for none.
	private string? _disc;
	private bool _discRead;

	/// <param name="installRoot">The install the turn runs on.</param>
	/// <param name="restart">Ends the shell's turn and starts the launcher again on the install it is given; null in a
	/// mission. It is called between this window's <c>Begin</c> and <c>End</c>, so it must not tear down the ImGui context
	/// there.</param>
	public Es1SettingsWindow(LocalizationTable localization, string installRoot, Action<string>? restart)
		: base(localization, "es1_settings", inMission: restart == null) {
		_installRoot = installRoot;
		_restart = restart;
	}

	protected override void DrawRows() {
		ImGui.SeparatorText(Localization.GetStringOrKey("es1_settings.folders"));
		ImGui.TextUnformatted(Localization.GetStringOrKey("settings.install_folder"));
		ImGui.TextDisabled(_installRoot);
		ImGui.BeginDisabled(InMission);
		if (ImGui.Button(Localization.GetStringOrKey("settings.change") + "##change_install")) {
			RaisePrompt(InstallPromptWindow.Create(Localization, Es1GameModule.Identity, "general.cancel", _installRoot,
				"es1_settings.install_message"), Restart, InstallPromptWindow.MessageArguments);
		}

		ImGui.EndDisabled();
		InMissionTooltip();

		DiscRows();

		ImGui.SeparatorText(Localization.GetStringOrKey("es1_settings.language"));
		InterfaceLanguage.Draw();
	}

	protected override void Forget() => _discRead = false;

	// The disc's label, what it plays, its folder or image, and the buttons that choose one or take it out.
	private void DiscRows() {
		if (!_discRead) {
			_disc = Es1Disc.Remembered();
			_discRead = true;
		}

		string? disc = _disc;
		ImGui.TextUnformatted(Localization.GetStringOrKey("es1_settings.disc"));
		ImGui.TextDisabled(disc ?? Localization.GetStringOrKey("settings.no_disc_image"));
		ImGui.TextWrapped(Localization.GetStringOrKey("es1_settings.disc_note"));
		if (Environment.GetEnvironmentVariable(Es1Launcher.AltDriveVariable) != null) {
			ImGui.TextDisabled(string.Format(Localization.GetStringOrKey("es1_settings.alt_drive_overrides"), Es1Launcher.AltDriveVariable));
		}

		ImGui.BeginDisabled(InMission);
		if (ImGui.Button(Localization.GetStringOrKey("es1_settings.choose_disc_folder"))) {
			RaisePrompt(new PathPrompt(Localization, "es1_disc_prompt", "general.cancel", DiscRefusal,
				disc != null && !Es1Disc.IsImage(disc) ? disc : ""), ApplyDisc);
		}

		InMissionTooltip();
		ImGui.SameLine();
		if (ImGui.Button(Localization.GetStringOrKey("es1_settings.choose_disc_image"))) {
			RaisePrompt(new PathPrompt(Localization, "es1_image_prompt", "general.cancel", DiscRefusal,
				disc != null && Es1Disc.IsImage(disc) ? disc : "",
				fileFilter: new NativePathPicker.FileFilter(Localization.GetStringOrKey("install.image_filter"), ["iso", "bin", "cue"])),
				ApplyDisc);
		}

		InMissionTooltip();
		if (disc != null) {
			ImGui.SameLine();
			if (ImGui.Button(Localization.GetStringOrKey("settings.remove") + "##remove_disc")) {
				if (Es1Disc.Forget()) {
					Restart(_installRoot);
				} else {
					Error = string.Format(Localization.GetStringOrKey("es1_settings.save_failed"), Es1Disc.PathFile);
				}
			}

			InMissionTooltip();
		}

		ImGui.EndDisabled();
	}

	// The disc kept for the launcher, which starts again on it.
	private void ApplyDisc(string path) {
		if (!Es1Disc.Remember(path)) {
			Error = string.Format(Localization.GetStringOrKey("es1_settings.save_failed"), Es1Disc.PathFile);
			return;
		}

		Restart(_installRoot);
	}

	private void Restart(string installRoot) {
		Close();
		_restart?.Invoke(installRoot);
	}

	private string? DiscRefusal(string path) => Es1DiscPrompt.Refusal(Localization, path);
}
