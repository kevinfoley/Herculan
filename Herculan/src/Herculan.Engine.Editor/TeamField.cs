using Herculan.Engine.World;
using ImGuiNET;

namespace Herculan.Engine.Editor;

/// <summary>
/// A group's Team in the group and object views (docs/herculan/handoff-editor-roster.md#sides): Human or Cybrid, a choice that
/// changes it (<see cref="SideChanger"/>) as one undo step, from the layout by the group's row-16 GUID. The team is the
/// group's, so a member's view shows it fixed, saying why on hover, and the group's view says when other members change with it. The player's
/// squad's is shown fixed, with no note.
/// </summary>
internal sealed class TeamField {
	private readonly MissionIndex _index;
	private readonly SideChanger _sides;

	public TeamField(MissionIndex index, SideChanger sides) {
		_index = index;
		_sides = sides;
	}

	/// <summary>Draws row-16 GUID <paramref name="guid"/>'s team, fixed in a member's view (<paramref name="ofMember"/>).</summary>
	public void Draw(short guid, bool ofMember) {
		if (_index.Group(guid) is not { } group) {
			return;
		}

		string? reason = _sides.WhyFixed(guid);
		// One item for the label and both buttons, so the member view's tooltip shows over any of them.
		ImGui.BeginGroup();
		ImGui.AlignTextToFramePadding();
		ImGui.Text("Team:");
		ImGui.BeginDisabled(ofMember || reason != null);
		foreach (var option in Enum.GetValues<MissionSide>()) {
			ImGui.SameLine();
			if (ImGui.RadioButton(option + "##team", group.Side == option)) {
				_sides.Set(guid, option);
			}
		}

		ImGui.EndDisabled();
		ImGui.EndGroup();
		if (ofMember) {
			EditorText.Tooltip("Team controlled by group.");
		} else if (reason != null && !group.IsSquad) {
			EditorText.Reason(reason);
		} else if (reason == null && group.Members.Count > 1) {
			EditorText.Note($"All {group.Members.Count} members change with it.");
		}
	}
}
