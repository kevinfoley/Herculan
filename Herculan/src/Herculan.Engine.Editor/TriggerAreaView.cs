using HercWorks.Core.Data.File.Msn;
using Herculan.Engine.Numerics;
using Herculan.Engine.Platform;
using Herculan.Engine.World;
using ImGuiNET;

namespace Herculan.Engine.Editor;

/// <summary>
/// The Properties panel's view of a trigger area: its shape, a box's corners or a circle's centre and radius as typed
/// fields (<see cref="PointFields"/>), what shares their points, whether one stands outside the mission box as opened, the actions testing it, and the edits that make one (<see cref="AreaEditor"/>,
/// <see cref="PointMover"/>): switch box and circle, move a corner, the centre or the radius, delete the area; and a new action
/// testing it (<see cref="ActionEditor"/>). A field
/// applies when it loses the focus, as one step of the undo history, through the same <see cref="PointMover.Commit"/> a
/// drag of the move handles makes, so a point shared with anything else moves with it.
/// </summary>
internal sealed class TriggerAreaView {
	private readonly GeneratedIndex _index;
	private readonly PointMover _mover;
	private readonly AreaEditor _editor;
	private readonly AreaPlacing _placing;
	private readonly ActionEditor _actions;
	private readonly Action<int> _drawAction;
	private readonly Action<DeleteNamers> _references;

	// A corner or centre being typed; and a radius being typed, and for which area, until its field lets go.
	private readonly PointFields _corners = new();
	private int? _radiusDraftFor;
	private int _radiusDraft;

	/// <param name="actions">Creates an action testing the area.</param>
	/// <param name="drawAction">Draws an action's wiring, read-only, as the action view summarises it.</param>
	/// <param name="references">Draws what references something, one line per record.</param>
	public TriggerAreaView(GeneratedIndex index, PointMover mover, AreaEditor editor, AreaPlacing placing, ActionEditor actions,
			Action<int> drawAction, Action<DeleteNamers> references) {
		_index = index;
		_mover = mover;
		_editor = editor;
		_placing = placing;
		_actions = actions;
		_drawAction = drawAction;
		_references = references;
	}

	/// <summary>Row-9 GUID <paramref name="guid"/>'s view, the area generated at block-4 index <paramref name="reference"/>.</summary>
	public void Draw(short guid, int reference) {
		if (_index.Area(reference) is not { } area) {
			return;
		}

		ImGui.Text($"Trigger area {reference}");
		ImGui.Separator();
		string? reason = _editor.WhyFixed(guid);
		bool fixedNow = reason != null || _placing.Active;
		DrawShape(guid, area, fixedNow);

		ImGui.BeginDisabled(fixedNow);
		if (area.Shape == MissionTriggerShape.Circle) {
			DrawCorner(reference, 0, "Centre", area.A, reason != null);
			DrawRadius(reference, area.Radius, reason != null);
		} else {
			DrawCorner(reference, 0, "Corner 1", area.A, reason != null);
			DrawCorner(reference, 1, "Corner 2", area.B, reason != null);
			ImGui.Text($"Size: {MissionText.Distance(Math.Abs(area.B.X - area.A.X))} by {MissionText.Distance(Math.Abs(area.B.Y - area.A.Y))}");
			EditorText.Note("Height is ignored: the box catches anything over its ground.");
		}

		ImGui.EndDisabled();
		EditorText.Reason(reason);
		EditorText.Reason(_editor.Failure);

		bool tested = false;
		foreach (int action in _index.ActionsTesting(reference)) {
			ImGui.Separator();
			ImGui.PushID(action);
			_drawAction(action);
			ImGui.PopID();
			tested = true;
		}

		if (!tested) {
			ImGui.Separator();
			EditorText.Note("No action tests it, so it fires nothing.");
		}

		ImGui.Spacing();
		ImGui.BeginDisabled(_placing.Active);
		if (ImGui.Button("New action testing this area")) {
			_actions.RequestCreate(guid);
		}

		ImGui.EndDisabled();
		EditorText.Reason(_actions.Failure);

		DrawDelete(guid);
	}

	// Box or Circle, a change switching the area to the other shape.
	private void DrawShape(short guid, MissionTriggerArea area, bool fixedNow) {
		bool circle = area.Shape == MissionTriggerShape.Circle;
		ImGui.AlignTextToFramePadding();
		ImGui.Text("Shape:");
		ImGui.BeginDisabled(fixedNow);
		ImGui.SameLine();
		if (ImGui.RadioButton("Box##shape", !circle) && circle) {
			_editor.RequestShape(guid, circle: false);
		}

		ImGui.SameLine();
		if (ImGui.RadioButton("Circle##shape", circle) && !circle) {
			_editor.RequestShape(guid, circle: true);
		}

		ImGui.EndDisabled();
		EditorText.Note(circle
			? "As a box it becomes the square around the circle."
			: "As a circle it becomes the circle about its middle, as wide as its longer side.");
	}

	// A corner's or the centre's X and Y as fields, which move its point when one lets go, what shares the point, and
	// whether it stands outside the mission box as opened. The box is of block 1's points alone, so a circle's edge past
	// it grows nothing and only the centre is tested.
	private void DrawCorner(int reference, int corner, string label, Vec3i at, bool fixedNow) {
		var point = new Vec3i(at.X, at.Y, 0);
		ImGui.PushID($"corner{corner}");
		ImGui.Text($"{label}, in world units:");
		if (_corners.Draw((reference, corner), point, withZ: false) is { } draft) {
			_mover.Commit(new AreaCornerTarget(reference, corner), point, draft.X, draft.Y, 0);
		}

		PointFields.BoxWarning(_mover.LoadedBox, point);
		if (!fixedNow && _mover.SharersOf(new AreaCornerTarget(reference, corner)) is > 0 and var sharers) {
			string what = label == "Centre" ? "the centre" : "the corner";
			EditorText.Note($"Its point is referenced by {sharers} other record(s) (waypoints, other areas, groups), which move "
				+ $"with it. Alt+drag moves {what} alone.");
		}

		ImGui.PopID();
	}

	// The radius as a field, in world units, stored in tens when it lets go.
	private void DrawRadius(int reference, int radius, bool fixedNow) {
		if (_radiusDraftFor is { } draftFor && draftFor != reference && !ImGui.IsAnyItemActive()) {
			_radiusDraftFor = null;
		}

		int shown = _radiusDraftFor == reference ? _radiusDraft : radius;
		bool done = false;
		ImGui.PushID("radius");
		ImGui.Text("Radius, in world units:");
		if (Field("##radius", ref shown, ref done)) {
			_radiusDraftFor = reference;
			_radiusDraft = shown;
		}

		if (done && _radiusDraftFor == reference) {
			int draft = _radiusDraft;
			_radiusDraftFor = null;
			if (TriggerAreaEditing.Radius(TriggerAreaEditing.StoredRadius(draft)) != radius) {
				_mover.CommitRadius(reference, draft);
			}
		}

		EditorText.Note(fixedNow
			? MissionText.Distance(radius)
			: $"{MissionText.Distance(radius)}. Kept to the nearest {TriggerAreaEditing.RadiusScale} units, at most "
				+ $"{TriggerAreaEditing.Radius(short.MaxValue)}.");
		ImGui.PopID();
	}

	// A Delete button, or why the area cannot be deleted, with the actions referencing it as links. The delete waits for
	// the frame's ImGui to be rendered (AreaEditor.RequestDelete).
	private void DrawDelete(short guid) {
		ImGui.Separator();
		var blockers = _editor.BlockersOf(guid);
		if (blockers.None) {
			ImGui.BeginDisabled(_placing.Active);
			if (ImGui.Button("Delete")) {
				_editor.RequestDelete(guid);
			}

			ImGui.EndDisabled();
			return;
		}

		if (blockers.Fixed is { } reason) {
			EditorText.Reason(reason, "Cannot be deleted: ");
			return;
		}

		ImGui.PushID("delete");
		ImGui.TextWrapped("Cannot be deleted while these reference it:");
		_references(blockers.Names);
		ImGui.PopID();
	}

	private static bool Field(string label, ref int value, ref bool done) {
		ImGui.SetNextItemWidth(ScaledImGui.Scaled(140f));
		bool changed = ImGui.InputInt(label, ref value, 0, 0);
		done |= ImGui.IsItemDeactivatedAfterEdit();
		return changed;
	}
}
