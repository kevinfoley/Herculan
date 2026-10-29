using ImGuiNET;

namespace Herculan.Engine.Editor;

/// <summary>A choice of none or any of the mission's triggers, as the views that wire one into a record draw it.</summary>
internal static class ActionChoice {
	/// <summary>
	/// Draws a combo of None and each trigger of the layout, by <see cref="MissionNames.Trigger"/> cut to fit with its number
	/// dimmed, showing row-10 GUID <paramref name="current"/> (<c>-1</c> for none), and answers the row-10 GUID of the one chosen
	/// when it differs (<c>-1</c> for None), or null. Without <paramref name="offerNone"/>, None is left out, and
	/// <paramref name="whyNoNone"/>, when given, is said under the combo. <paramref name="none"/> is what None is called.
	/// </summary>
	public static short? Draw(string id, short current, MissionIndex index, MissionNames names, bool offerNone = true,
			string? whyNoNone = null, string none = "None") {
		short? chosen = null;
		bool set = current != -1 && index.Action(current) != null;
		if (NameText.BeginCombo(id, set ? names.Trigger(current) : new RecordName(none))) {
			if (offerNone && ImGui.Selectable(none, !set) && set) {
				chosen = -1;
			}

			foreach (var trigger in index.Actions) {
				if (NameText.Selectable(names.Trigger(trigger.Guid), $"trigger{trigger.Guid}", trigger.Guid == current)
						&& trigger.Guid != current) {
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
