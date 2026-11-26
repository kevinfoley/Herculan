using ImGuiNET;

namespace Herculan.Es2.Editor;

/// <summary>
/// Raising one of the editor's modals. ImGui keeps one popup open at the top level: opening a second takes the first down.
/// A modal that raises itself again whenever it is taken down therefore waits while another popup is up, since raising
/// itself then would take that one down, which would raise itself in turn, every frame. The unsaved-changes prompt
/// (<see cref="MissionSaving"/>) is the one that does not wait: closing the window puts it up over whatever is open.
/// </summary>
internal static class EditorModal {
	/// <summary>
	/// Opens a popup and sets a flag, unless it is already set or another popup
	/// is up; the <c>BeginPopupModal</c> after it then finds the modal shut, and the next frame tries again.
	/// </summary>
	/// <param name="title">The popup's title.</param>
	/// <param name="raised">The flag to set.</param>
	public static void Raise(string title, ref bool raised) {
		if (raised || ImGui.IsPopupOpen("", ImGuiPopupFlags.AnyPopupId)) {
			return;
		}

		ImGui.OpenPopup(title);
		raised = true;
	}
}
