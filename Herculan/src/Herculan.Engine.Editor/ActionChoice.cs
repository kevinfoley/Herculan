using ImGuiNET;

namespace Herculan.Engine.Editor;

/// <summary>A choice of none or any of the mission's triggers, as the views that wire one into a record draw it.</summary>
internal static class ActionChoice {
	/// <summary>
	/// Draws a combo of None and each trigger of the layout, by <see cref="MissionText.TriggerName"/>, showing row-10 GUID
	/// <paramref name="current"/> (<c>-1</c> for none), and answers the row-10 GUID of the one chosen when it differs (<c>-1</c>
	/// for None), or null. Without <paramref name="offerNone"/>, None is left out, and <paramref name="whyNoNone"/>, when given,
	/// is said under the combo. <paramref name="none"/> is what None is called.
	/// </summary>
	public static short? Draw(string id, short current, MissionIndex index, bool offerNone = true, string? whyNoNone = null,
			string none = "None") {
		short? chosen = null;
		bool set = current != -1 && index.Action(current) != null;
		if (ImGui.BeginCombo(id, set ? MissionText.TriggerName(index.Layout, current) : none)) {
			if (offerNone && ImGui.Selectable(none, !set) && set) {
				chosen = -1;
			}

			foreach (var trigger in index.Actions) {
				if (ImGui.Selectable($"Trigger {trigger.Number}##{trigger.Guid}", trigger.Guid == current) && trigger.Guid != current) {
					chosen = trigger.Guid;
				}
			}

			ImGui.EndCombo();
		}

		if (!offerNone && whyNoNone != null && set) {
			EditorText.Note(whyNoNone);
		}

		return chosen;
	}
}
