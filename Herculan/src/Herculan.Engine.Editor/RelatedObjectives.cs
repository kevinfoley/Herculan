using HercWorks.Core.Data.File.Msn;
using ImGuiNET;

namespace Herculan.Engine.Editor;

/// <summary>
/// The win and lose conditions about a group or an object, in their views (docs/herculan/editor-design.md#objectives):
/// a link to each, its sentence, and a button that adds a win condition about it (<see cref="ObjectiveEditor"/>).
/// </summary>
internal sealed class RelatedObjectives {
	private readonly ObjectiveEditor _objectives;
	private readonly PanelLinks _links;

	public RelatedObjectives(ObjectiveEditor objectives, PanelLinks links) {
		_objectives = objectives;
		_links = links;
	}

	/// <summary>Draws <paramref name="objectives"/>, those about <paramref name="subject"/>, and Add a win condition about it.</summary>
	public void Draw(IEnumerable<LayoutObjective> objectives, EditorSelection subject) {
		ImGui.Separator();
		ImGui.TextUnformatted("Win and lose conditions about it:");
		ImGui.PushID("objectives");
		bool any = false;
		foreach (var objective in objectives) {
			ImGui.PushID(objective.Index);
			_links.Objective(objective.Index);
			ImGui.PopID();
			any = true;
		}

		if (!any) {
			EditorText.Note("None.");
		}

		string? why = _objectives.WhyNoCreate(subject);
		ImGui.BeginDisabled(why != null);
		if (ImGui.Button("Add a win condition")) {
			_objectives.RequestCreate(subject);
		}

		ImGui.EndDisabled();
		EditorText.Reason(why);
		EditorText.Reason(_objectives.Failure);
		ImGui.PopID();
	}
}
