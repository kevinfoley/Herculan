using ImGuiNET;

namespace Herculan.Engine.Editor;

/// <summary>A choice of none or any of the mission's actions, as the views that wire one into a record draw it.</summary>
internal static class ActionChoice {
	/// <summary>
	/// Draws a combo of None and each of <paramref name="count"/> actions, by block-5 index, showing <paramref name="current"/>
	/// (<c>-1</c> for none), and answers the one chosen when it differs, or null. Without <paramref name="offerNone"/>, None is
	/// left out, and <paramref name="whyNoNone"/>, when given, is said under the combo.
	/// </summary>
	public static int? Draw(string id, int current, int count, bool offerNone = true, string? whyNoNone = null) {
		int? chosen = null;
		if (ImGui.BeginCombo(id, current >= 0 ? $"Action {current}" : "None")) {
			if (offerNone && ImGui.Selectable("None", current < 0) && current >= 0) {
				chosen = -1;
			}

			for (int action = 0; action < count; action++) {
				if (ImGui.Selectable($"Action {action}", action == current) && action != current) {
					chosen = action;
				}
			}

			ImGui.EndCombo();
		}

		if (!offerNone && whyNoNone != null && current >= 0) {
			EditorText.Note(whyNoNone);
		}

		return chosen;
	}
}
