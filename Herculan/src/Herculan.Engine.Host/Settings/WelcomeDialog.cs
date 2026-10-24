using System.Numerics;
using Herculan.Engine.Host.Localization;
using Herculan.Engine.Platform;
using ImGuiNET;

namespace Herculan.Engine.Host.Settings;

/// <summary>
/// The welcome HERCULAN shows the first time the front end reaches the main menu, after the intro movies and the startup
/// sequence: what the tweaks are and how to reach the menu bar, an "Auto check for updates" box, unticked unless the
/// setting is already on, and View Tweaks, which opens the Tweaks panel, and OK. Retail has no such dialog. It is a
/// modal popup, so the shell under it takes no clicks while it is up.
///
/// <para>Once dismissed it is not shown again: <see cref="ShownFile"/> records that, and is written by either button,
/// by [Esc] (<see cref="HostMenuBar.BackOut"/>) and by nothing else, so a window closed while it is up shows it on the
/// next start. The same dismissals save the box (<see cref="UpdateChecks.Automatic"/>) and, ticked, start this run's
/// check.</para>
/// </summary>
sealed class WelcomeDialog {
	// In pixels on a 100% display (ScaledImGui.Scaled).
	private const float PanelWidth = 460f;
	// "###" so that the popup's ID is this part alone, the same for OpenPopup and for the titled BeginPopupModal.
	private const string PopupId = "###welcome";

	private readonly LocalizationTable _localization;
	private readonly Action _viewTweaks;
	private readonly (string key, Action callback)[] _buttons;
	private bool _open;
	private bool _popupRaised;
	private bool _checkForUpdates;

	/// <param name="viewTweaks">Opens the Tweaks panel; run after the dialog has closed.</param>
	public WelcomeDialog(LocalizationTable localization, Action viewTweaks) {
		_localization = localization;
		_viewTweaks = viewTweaks;
		_buttons = [("welcome.view_tweaks", ViewTweaks), ("general.ok", Dismiss)];
	}

	/// <summary>
	/// Where the dismissal is remembered: <c>welcome-shown.txt</c> in <see cref="UserConfig.Folder"/>. Its presence is the
	/// record; it holds nothing.
	/// </summary>
	public static string ShownFile => UserConfig.PathOf("welcome-shown.txt");

	/// <summary>Whether the dialog is up.</summary>
	public bool IsOpen => _open;

	/// <summary>Raises the dialog, unless it has been dismissed before, on this run or an earlier one.</summary>
	public void Offer() {
		if (!_open && !File.Exists(ShownFile)) {
			Raise();
		}
	}

	/// <summary>Raises the dialog whether or not it has been dismissed before.</summary>
	internal void Raise() {
		_open = true;
		_popupRaised = false;
		_checkForUpdates = UpdateChecks.Automatic;
	}

	/// <summary>Takes the dialog down as OK does.</summary>
	public void Dismiss() {
		_open = false;
		RememberShown();
		if (_checkForUpdates != UpdateChecks.Automatic) {
			UpdateChecks.Automatic = _checkForUpdates;
		}
		UpdateChecks.StartAutomatic();
	}

	/// <summary>Draws the dialog, if it is up. Call once per frame, inside the ImGui frame.</summary>
	public void Draw(ImGuiCond centre) {
		// A dismissal from outside the popup ([Esc]) still has to close it from inside, or ImGui keeps it on its popup
		// stack and goes on taking the mouse from the shell.
		if (!_open && !_popupRaised) {
			return;
		}

		if (_open && !_popupRaised) {
			ImGui.OpenPopup(PopupId);
			_popupRaised = true;
		}

		ImGui.SetNextWindowSize(new Vector2(ScaledImGui.Scaled(PanelWidth), 0f));
		ImGui.SetNextWindowPos(ImGui.GetMainViewport().GetCenter(), centre, new Vector2(0.5f, 0.5f));
		if (!ImGui.BeginPopupModal(_localization.GetStringOrKey("welcome.title") + PopupId, ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove)) {
			_popupRaised = _open;
			return;
		}

		ImGui.TextWrapped(_localization.GetStringOrKey("welcome.message"));
		ImGui.Dummy(new Vector2(0, ScaledImGui.Scaled(10)));
		ImGui.Checkbox(_localization.GetStringOrKey("settings.auto_check_updates"), ref _checkForUpdates);
		ImGui.Dummy(new Vector2(0, ScaledImGui.Scaled(10)));
		LocalizedImGuiHelpers.DrawButtonRow(_localization, _buttons);

		if (!_open) {
			ImGui.CloseCurrentPopup();
			_popupRaised = false;
		}

		ImGui.EndPopup();
	}

	private void ViewTweaks() {
		Dismiss();
		_viewTweaks();
	}

	private static void RememberShown() => UserConfig.Write(ShownFile, "");
}
