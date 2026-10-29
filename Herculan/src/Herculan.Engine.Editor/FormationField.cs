using System.Numerics;
using Herculan.Engine.World;
using ImGuiNET;

namespace Herculan.Engine.Editor;

/// <summary>
/// A Formation in a group's view: a dropdown of the formations its kind picks from, set through <see cref="FormationEditor"/>
/// as one undo step, on the line of a foldout, collapsed by default, that opens onto a diagram of where each place stands
/// around the leader, front up. Formations have no names in any retail file, so each entry is told apart by its diagram,
/// shown in the foldout for the group's own and on hover for each entry.
/// </summary>
internal sealed class FormationField {
	private readonly FormationEditor _editor;

	public FormationField(FormationEditor editor) {
		_editor = editor;
	}

	/// <summary>
	/// Draws <paramref name="group"/>'s formation: its choice, with why it cannot be set on hover, and its diagram when the
	/// foldout is open. Why the last edit failed shows either way.
	/// </summary>
	public void Draw(PlannedGroup group) {
		var kind = group.Kind;
		int current = group.Setup.FormationId;
		string? why = _editor.WhyFixed(group.Guid);

		ImGui.PushID("formation");
		ImGui.AlignTextToFramePadding();
		bool open = ImGui.TreeNodeEx("Formation:##foldout", ImGuiTreeNodeFlags.AllowOverlap);
		ImGui.SameLine();
		ImGui.SetNextItemWidth(-1f);
		ImGui.BeginDisabled(why != null);
		if (EditorText.BeginCombo("##choice", Label(kind, current))) {
			for (int id = 0; id < _editor.Count(kind); id++) {
				if (ImGui.Selectable(Label(kind, id), id == current)) {
					_editor.Request(group.Guid, (short)id);
				}

				if (ImGui.IsItemHovered()) {
					ImGui.BeginTooltip();
					float size = ImGui.GetFontSize();
					Diagram(kind, id, new Vector2(size * 14f, size * 10f));
					ImGui.EndTooltip();
				}
			}

			ImGui.EndCombo();
		}

		ImGui.EndDisabled();
		EditorText.Tooltip(why);
		if (open) {
			DrawDetails(group, why);
			ImGui.TreePop();
		}

		EditorText.Reason(_editor.Failure, "Not changed: ");
		ImGui.PopID();
	}

	// What the foldout opens onto: why the formation cannot be set, the diagram of the group's own and how to read it.
	private void DrawDetails(PlannedGroup group, string? why) {
		var kind = group.Kind;
		int current = group.Setup.FormationId;
		EditorText.Reason(why);

		var places = _editor.Places(kind, current);
		if (places.Count == 0) {
			EditorText.Note("Every member stands on the group's position.");
		} else {
			float width = ImGui.GetContentRegionAvail().X;
			Diagram(kind, current, new Vector2(width, MathF.Min(width * 0.75f, ImGui.GetFontSize() * 12f)));
			int reach = places.Max(place => (int)MathF.Sqrt((float)place.X * place.X + (float)place.Y * place.Y));
			EditorText.Note($"Front is up; the filled marker is the leader, place 1. The farthest place is {MissionText.Metres(reach)} from it.");
			if (kind == MissionUnitKind.Base) {
				EditorText.Note("A structure standing on a place faces the way its line points.");
			}
		}

		if (kind == MissionUnitKind.Base && group.Setup.PaintsGround) {
			EditorText.Note("Picking a formation with no base pad also stops the group laying one.");
		}
	}

	// The dropdown's text for formation <id>: its number and how many places it has, and whether it lays a base pad.
	private string Label(MissionUnitKind kind, int id) {
		if (id < 0 || id >= _editor.Count(kind)) {
			return "None";
		}

		string pad = _editor.HasPad(kind, id) ? ", base pad" : "";
		return $"Formation {id}: {_editor.Places(kind, id).Count + 1} places{pad}";
	}

	// Formation <id>'s places drawn into a box of <size> at the cursor, front up and scaled to fit: the leader filled, every
	// other place a ring with its number beside it, each with a line the way a member standing there faces.
	private void Diagram(MissionUnitKind kind, int id, Vector2 size) {
		var places = _editor.Places(kind, id);
		var origin = ImGui.GetCursorScreenPos();
		ImGui.Dummy(size);
		if (places.Count == 0) {
			return;
		}

		var draw = ImGui.GetWindowDrawList();
		draw.AddRectFilled(origin, origin + size, ImGui.GetColorU32(ImGuiCol.FrameBg));
		draw.AddRect(origin, origin + size, ImGui.GetColorU32(ImGuiCol.Border));

		float radius = ImGui.GetFontSize() * 0.35f;
		float margin = radius * 2f + ImGui.CalcTextSize("8").X * 2f;
		int minX = Math.Min(0, places.Min(place => place.X)), maxX = Math.Max(0, places.Max(place => place.X));
		int minY = Math.Min(0, places.Min(place => place.Y)), maxY = Math.Max(0, places.Max(place => place.Y));
		float scaleX = maxX > minX ? (size.X - margin * 2f) / (maxX - minX) : float.MaxValue;
		float scaleY = maxY > minY ? (size.Y - margin * 2f) / (maxY - minY) : float.MaxValue;
		float scale = MathF.Min(scaleX, scaleY);
		var centre = origin + size / 2f;
		float midX = (minX + maxX) / 2f, midY = (minY + maxY) / 2f;
		Vector2 At(int x, int y) => centre + new Vector2((x - midX) * scale, -(y - midY) * scale);

		uint ink = ImGui.GetColorU32(ImGuiCol.Text);
		uint leader = ImGui.GetColorU32(ImGuiCol.CheckMark);
		Facing(draw, At(0, 0), 0, radius, leader);
		draw.AddCircleFilled(At(0, 0), radius, leader);
		for (int i = 0; i < places.Count; i++) {
			var at = At(places[i].X, places[i].Y);
			Facing(draw, at, places[i].Turn, radius, ink);
			draw.AddCircle(at, radius, ink, 0, 1.5f);
			draw.AddText(at + new Vector2(radius + 1f, -radius * 2.2f), ink, (i + 2).ToString());
		}
	}

	// A line from <at> the way a member standing there faces: front, turned <turn> binary-angle units counterclockwise.
	private static void Facing(ImDrawListPtr draw, Vector2 at, short turn, float radius, uint color) {
		float angle = turn * MathF.Tau / 65536f;
		var direction = new Vector2(-MathF.Sin(angle), -MathF.Cos(angle));
		draw.AddLine(at, at + direction * radius * 2.6f, color, 1.5f);
	}
}
