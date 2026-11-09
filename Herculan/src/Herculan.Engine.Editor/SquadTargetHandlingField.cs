using ImGuiNET;

namespace Herculan.Engine.Editor;

/// <summary>
/// The "Squad target handling" dropdown, set through <see cref="SquadTargetHandlingEditor"/> as one undo step, drawn in the
/// Mission Settings window and above the player's squad's orders: the choice with what each does on hover, a note for each
/// condition under which a row-2 record switches it, and the mission check's
/// <see cref="HercWorks.Core.Data.File.Msn.MissionCheckKind.DataLinkNoTarget"/> where it is changed.
/// </summary>
internal sealed class SquadTargetHandlingField {
	private readonly SquadTargetHandlingEditor _editor;

	public SquadTargetHandlingField(SquadTargetHandlingEditor editor) {
		_editor = editor;
	}

	/// <summary>Draws the choice, on one line with its label.</summary>
	public void Draw() {
		short current = _editor.Current;
		ImGui.PushID("squadTargetHandling");
		ImGui.AlignTextToFramePadding();
		ImGui.Text("Squad target handling:");
		ImGui.SameLine();
		ImGui.SetNextItemWidth(-1f);
		if (EditorText.BeginCombo("##choice", SquadTargetHandlingEditor.Label(current))) {
			foreach (short choice in SquadTargetHandlingEditor.Choices) {
				if (ImGui.Selectable(SquadTargetHandlingEditor.Label(choice), choice == current) && choice != current) {
					_editor.Request(choice);
				}

				EditorText.Tooltip(SquadTargetHandlingEditor.Note(choice));
			}

			ImGui.EndCombo();
		}

		EditorText.Tooltip(SquadTargetHandlingEditor.Note(current));
		foreach (var (condition, described, choice) in _editor.Variations) {
			string what = described != null ? $" ({described})" : "";
			EditorText.Note($"When condition {condition}{what} holds, it is \"{SquadTargetHandlingEditor.Label(choice)}\" instead. "
				+ $"Choosing \"{SquadTargetHandlingEditor.Label(Es2.Sim.MechObject.SquadTargetHandlingAnnounce)}\" removes this, as it is "
				+ "what a mission has when no record sets one.");
		}

		if (_editor.DataLinkWithoutTarget) {
			EditorText.Problem("\"Data link\" requires at least one of the player squad's orders to have a valid target.");
		}

		EditorText.Reason(_editor.Failure, "Not changed: ");
		ImGui.PopID();
	}
}
