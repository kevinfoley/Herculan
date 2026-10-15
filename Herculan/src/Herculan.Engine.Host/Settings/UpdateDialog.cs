using System.Numerics;
using Herculan.Engine.Host.Localization;
using Herculan.Engine.Platform;
using Herculan.Engine.Updates;
using ImGuiNET;

namespace Herculan.Engine.Host.Settings;

/// <summary>
/// The notice that a newer HERCULAN release is out, raised over the front end when the automatic check
/// (<see cref="UpdateChecks"/>) finds one: the two versions, with View Release, which opens the release's page in the
/// browser, and Close. Retail has no such dialog. It is a modal popup like <see cref="WelcomeDialog"/>, raised only
/// once the welcome is down, and [Esc] takes it down as Close does (<see cref="HostMenuBar.BackOut"/>).
/// </summary>
sealed class UpdateDialog {
	// In pixels on a 100% display (ScaledImGui.Scaled).
	private const float PanelWidth = 460f;
	// "###" so that the popup's ID is this part alone, the same for OpenPopup and for the titled BeginPopupModal.
	private const string PopupId = "###update";

	private readonly LocalizationTable _localization;
	private readonly (string key, Action callback)[] _buttons;
	private ReleaseCheckResult? _release;
	private bool _open;
	private bool _popupRaised;

	public UpdateDialog(LocalizationTable localization) {
		_localization = localization;
		_buttons = [("update.view_release", ViewRelease), ("general.close", Dismiss)];
	}

	/// <summary>Whether the dialog is up.</summary>
	public bool IsOpen => _open;

	/// <summary>Raises the dialog for <paramref name="release"/>, a newer release the check found.</summary>
	public void Offer(ReleaseCheckResult release) {
		_release = release;
		_open = true;
		_popupRaised = false;
	}

	/// <summary>Takes the dialog down as Close does.</summary>
	public void Dismiss() => _open = false;

	/// <summary>Draws the dialog, if it is up. Call once per frame, inside the ImGui frame.</summary>
	public void Draw(ImGuiCond centre) {
		// A dismissal from outside the popup ([Esc]) still has to close it from inside, as WelcomeDialog's does.
		if ((!_open && !_popupRaised) || _release?.Latest is not { } latest) {
			return;
		}

		if (_open && !_popupRaised) {
			ImGui.OpenPopup(PopupId);
			_popupRaised = true;
		}

		ImGui.SetNextWindowSize(new Vector2(ScaledImGui.Scaled(PanelWidth), 0f));
		ImGui.SetNextWindowPos(ImGui.GetMainViewport().GetCenter(), centre, new Vector2(0.5f, 0.5f));
		if (!ImGui.BeginPopupModal(_localization.GetStringOrKey("update.title") + PopupId, ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove)) {
			_popupRaised = _open;
			return;
		}

		ImGui.TextWrapped(string.Format(_localization.GetStringOrKey("update.message"),
			ReleaseCheck.Display(latest), ReleaseCheck.Display(UpdateChecks.Current)));
		ImGui.Dummy(new Vector2(0, ScaledImGui.Scaled(10)));
		LocalizedImGuiHelpers.DrawButtonRow(_localization, _buttons);

		if (!_open) {
			ImGui.CloseCurrentPopup();
			_popupRaised = false;
		}

		ImGui.EndPopup();
	}

	private void ViewRelease() {
		Dismiss();
		if (_release?.Page is { } page) {
			UpdateChecks.OpenPage(page);
		}
	}
}
