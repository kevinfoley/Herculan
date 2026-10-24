using System.Numerics;
using Herculan.Engine.Host.Localization;
using Herculan.Engine.Platform;
using ImGuiNET;

namespace Herculan.Engine.Host.Settings;

/// <summary>
/// The notice that something the player asked for failed, raised over the front end by <see cref="FrontEndWindow.ShowError"/>:
/// what failed, the reason below it, and OK. Retail has no such dialog. It is a modal popup like <see cref="UpdateDialog"/>,
/// so the shell under it takes no clicks while it is up. It comes up with the highlight on OK, so a gamepad's A presses it,
/// and [Esc] or the gamepad's Back takes it down as OK does (<see cref="HostMenuBar.BackOut"/>). OK ignores a click or
/// an A for the first <see cref="ConfirmDelay"/> seconds, so a click or a press already on its way as the failure comes
/// up does not take the notice down before it is read; [Esc] and Back are not held.
/// A failure raised while one is up waits behind it and comes up when it is dismissed.
/// </summary>
sealed class ErrorDialog {
	// In pixels on a 100% display (ScaledImGui.Scaled).
	private const float PanelWidth = 460f;
	// "###" so that the popup's ID is this part alone, the same for OpenPopup and for the titled BeginPopupModal.
	private const string PopupId = "###error";

	/// <summary>How long OK ignores clicks and A after the dialog comes up, in seconds.</summary>
	private const double ConfirmDelay = 0.75;

	private readonly LocalizationTable _localization;
	private readonly (string key, Action callback)[] _buttons;
	private readonly Queue<(string Message, string Detail)> _waiting = new();
	private (string Message, string Detail)? _shown;
	// What the popup draws, kept through the frame that closes it after _shown has gone.
	private (string Message, string Detail) _text = (string.Empty, string.Empty);
	private bool _popupRaised;
	// ImGui's clock when the popup last came up, which ConfirmDelay counts from.
	private double _raisedAt;

	public ErrorDialog(LocalizationTable localization) {
		_localization = localization;
		_buttons = [("general.ok", Confirm)];
	}

	/// <summary>Whether the dialog is up, or a failure is waiting to raise it.</summary>
	public bool IsOpen => _shown != null || _waiting.Count > 0;

	/// <summary>Raises the dialog with <paramref name="message"/>, what failed, over <paramref name="detail"/>, why.</summary>
	public void Show(string message, string detail) {
		if (_shown == null && _waiting.Count == 0) {
			_shown = (message, detail);
		} else {
			_waiting.Enqueue((message, detail));
		}
	}

	/// <summary>Takes the dialog down as OK does, raising the next failure waiting, if there is one.</summary>
	public void Dismiss() => _shown = null;

	/// <summary>Draws the dialog, if it is up. Call once per frame, inside the ImGui frame.</summary>
	public void Draw(ImGuiCond centre) {
		// A dismissal from outside the popup ([Esc]) still has to close it from inside, as WelcomeDialog's does.
		if (_shown == null && !_popupRaised) {
			if (_waiting.Count == 0) {
				return;
			}

			_shown = _waiting.Dequeue();
		}

		bool raising = false;
		if (_shown is { } shown && !_popupRaised) {
			_text = shown;
			ImGui.OpenPopup(PopupId);
			_popupRaised = true;
			_raisedAt = ImGui.GetTime();
			raising = true;
		}

		ImGui.SetNextWindowSize(new Vector2(ScaledImGui.Scaled(PanelWidth), 0f));
		ImGui.SetNextWindowPos(ImGui.GetMainViewport().GetCenter(), centre, new Vector2(0.5f, 0.5f));
		// Not open, though raised: another modal opened over it closed it, so it is raised again on the next frame.
		if (!ImGui.BeginPopupModal(_localization.GetStringOrKey("error.title") + PopupId, ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove)) {
			_popupRaised = false;
			return;
		}

		// The highlight on OK as it comes up, so a gamepad's A takes it down however the failure was reached.
		if (raising) {
			ImGuiNavigation.FocusWindowItems();
		}

		ImGui.TextWrapped(_text.Message);
		if (_text.Detail.Length > 0) {
			ImGui.Dummy(new Vector2(0, ScaledImGui.Scaled(6)));
			ImGui.TextWrapped(_text.Detail);
		}

		ImGui.Dummy(new Vector2(0, ScaledImGui.Scaled(10)));
		LocalizedImGuiHelpers.DrawButtonRow(_localization, _buttons);

		if (_shown == null) {
			ImGui.CloseCurrentPopup();
			_popupRaised = false;
		}

		ImGui.EndPopup();
	}

	// OK, which holds off until ConfirmDelay has passed.
	private void Confirm() {
		if (ImGui.GetTime() - _raisedAt >= ConfirmDelay) {
			Dismiss();
		}
	}
}
