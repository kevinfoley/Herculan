using System.Numerics;
using Herculan.Engine.Localization;
using Herculan.Engine.Updates;
using ImGuiNET;

namespace Herculan.Engine.Menus;

/// <summary>
/// A Settings window's Updates rows, which each game's window draws the same: the "Auto check for updates" setting
/// (<see cref="UpdateChecks.Automatic"/>), which takes effect from the next start, and Check for Updates with what the last
/// press found: a newer release with a button to its page, this build being the latest, or why the check failed.
/// </summary>
public sealed class UpdateCheckRows {
	private readonly LocalizationTable _localization;

	// The check, from the press until Forget.
	private Task<ReleaseCheckResult>? _check;

	public UpdateCheckRows(LocalizationTable localization) {
		_localization = localization;
	}

	/// <summary>Drops what the last press found, as the window closes.</summary>
	public void Forget() => _check = null;

	/// <summary>Draws the rows into the current window.</summary>
	public void Draw() {
		bool automatic = UpdateChecks.Automatic;
		if (ImGui.Checkbox(_localization.GetStringOrKey("settings.auto_check_updates"), ref automatic)) {
			UpdateChecks.Automatic = automatic;
		}

		bool checking = _check is { IsCompleted: false };
		ImGui.BeginDisabled(checking);
		if (ImGui.Button(_localization.GetStringOrKey("settings.check_updates"))) {
			_check = UpdateChecks.Check();
		}
		ImGui.EndDisabled();

		if (_check == null) {
			return;
		}

		if (checking) {
			ImGui.TextDisabled(_localization.GetStringOrKey("settings.checking_updates"));
			return;
		}

		var result = _check.Result;
		switch (result.Status) {
			case ReleaseCheckStatus.NewerAvailable:
				ImGui.TextWrapped(string.Format(_localization.GetStringOrKey("update.message"),
					ReleaseCheck.Display(result.Latest!), ReleaseCheck.Display(UpdateChecks.Current)));
				if (ImGui.Button(_localization.GetStringOrKey("update.view_release"))) {
					UpdateChecks.OpenPage(result.Page!);
				}
				break;
			case ReleaseCheckStatus.UpToDate:
				ImGui.TextWrapped(string.Format(_localization.GetStringOrKey("settings.up_to_date"),
					ReleaseCheck.Display(UpdateChecks.Current)));
				break;
			default:
				ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1f, 0.45f, 0.4f, 1f));
				ImGui.TextWrapped(string.Format(_localization.GetStringOrKey("settings.update_check_failed"), result.Error));
				ImGui.PopStyleColor();
				break;
		}
	}
}
