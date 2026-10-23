using HercWorks.Core.Data.File.Msn;
using Herculan.Engine.Numerics;
using Herculan.Engine.Platform;
using Herculan.Engine.World;
using ImGuiNET;

namespace Herculan.Engine.Editor;

/// <summary>
/// The Properties panel's view of a trigger area, read from the layout and <see cref="MissionIndex"/> by its row-9 GUID, every
/// area of the <c>.MSN</c> included: its shape, a box's corners or a circle's centre and radius as typed fields
/// (<see cref="PointFields"/>), what shares their points, whether one stands outside the mission's play area as opened, the
/// triggers testing it, and the edits that make one (<see cref="AreaEditor"/>,
/// <see cref="PointMover"/>): switch box and circle, move a corner, the centre or the radius, delete the area; and a new trigger
/// testing it (<see cref="ActionEditor"/>). A field
/// applies when it loses the focus, as one step of the undo history, through the same <see cref="PointMover.Commit"/> a
/// drag of the move handles makes, so a point shared with anything else moves with it.
/// </summary>
internal sealed class TriggerAreaView {
	private readonly MissionIndex _index;
	private readonly MissionNames _names;
	private readonly PointMover _mover;
	private readonly AreaEditor _editor;
	private readonly AreaPlacing _placing;
	private readonly ActionEditor _actions;
	private readonly Action<short> _drawTrigger;
	private readonly Action<DeleteNamers> _references;
	private readonly PointSharers _sharers;

	// A corner or centre being typed; and a radius being typed, and for which area's row-9 GUID, until its field lets go.
	private readonly PointFields _corners = new();
	private short? _radiusDraftFor;
	private int _radiusDraft;

	/// <param name="actions">Creates a trigger testing the area.</param>
	/// <param name="drawTrigger">Draws a trigger's wiring by its row-10 GUID, read-only, as the trigger view summarizes it.</param>
	/// <param name="references">Draws what references something, one line per record.</param>
	/// <param name="names">What the title names the area by.</param>
	/// <param name="sharers">Lists what else stands on a corner's or the center's point.</param>
	public TriggerAreaView(MissionIndex index, MissionNames names, PointMover mover, AreaEditor editor, AreaPlacing placing,
			ActionEditor actions, Action<short> drawTrigger, Action<DeleteNamers> references, PointSharers sharers) {
		_index = index;
		_names = names;
		_mover = mover;
		_editor = editor;
		_placing = placing;
		_actions = actions;
		_drawTrigger = drawTrigger;
		_references = references;
		_sharers = sharers;
	}

	/// <summary>
	/// Row-9 GUID <paramref name="guid"/>'s view; its shape and corners are the layout's (<see cref="MissionIndex"/>), which a
	/// drag's preview moves. An area whose corner or center references a position the layout has none for shows its name, why,
	/// and Delete.
	/// </summary>
	public void Draw(short guid) {
		var name = _names.Area(guid);
		EditorText.Wrapped(name.Text);
		if (name.Number is { } number) {
			ImGui.TextDisabled(number);
		}

		ImGui.Separator();
		if (_index.Area(guid)?.Area is not { } area) {
			EditorText.Reason(_editor.WhyFixed(guid));
			DrawDelete(guid);
			return;
		}

		string? reason = _editor.WhyFixed(guid);
		bool fixedNow = reason != null || _placing.Active;
		DrawShape(guid, area, fixedNow);

		ImGui.BeginDisabled(fixedNow);
		if (area.Shape == MissionTriggerShape.Circle) {
			DrawCorner(guid, 0, "Center", area.A, reason != null);
			DrawRadius(guid, area.Radius, reason != null);
		} else {
			DrawCorner(guid, 0, "Corner 1", area.A, reason != null);
			DrawCorner(guid, 1, "Corner 2", area.B, reason != null);
			ImGui.Text($"Size: {MissionText.Distance(Math.Abs(area.B.X - area.A.X))} by {MissionText.Distance(Math.Abs(area.B.Y - area.A.Y))}");
			EditorText.Note("Height is ignored: the box catches anything over its ground.");
		}

		ImGui.EndDisabled();
		EditorText.Reason(reason);
		EditorText.Reason(_editor.Failure);

		bool tested = false;
		foreach (short trigger in _index.Area(guid)?.TestedBy ?? []) {
			ImGui.Separator();
			ImGui.PushID(trigger);
			_drawTrigger(trigger);
			ImGui.PopID();
			tested = true;
		}

		if (!tested) {
			ImGui.Separator();
			EditorText.Note("No trigger tests it, so it does nothing.");
		}

		ImGui.Spacing();
		ImGui.BeginDisabled(_placing.Active);
		if (ImGui.Button("New trigger testing this area")) {
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
	// whether it stands outside the play area as opened. The play area is of block 1's points alone, so a circle's edge past
	// it grows nothing and only the centre is tested.
	private void DrawCorner(short guid, int corner, string label, Vec3i at, bool fixedNow) {
		var point = new Vec3i(at.X, at.Y, 0);
		ImGui.PushID($"corner{corner}");
		ImGui.Text($"{label}, in world units:");
		if (_corners.Draw((guid, corner), point, withZ: false) is { } draft) {
			_mover.Commit(new AreaCornerTarget(guid, corner), point, draft.X, draft.Y, 0);
		}

		PointFields.BoxWarning(_mover.LoadedBox, point);
		if (!fixedNow) {
			_sharers.Draw(_mover.SharersOf(new AreaCornerTarget(guid, corner)));
		}

		ImGui.PopID();
	}

	// The radius as a field, in world units, stored in tens when it lets go.
	private void DrawRadius(short guid, int radius, bool fixedNow) {
		if (_radiusDraftFor is { } draftFor && draftFor != guid && !ImGui.IsAnyItemActive()) {
			_radiusDraftFor = null;
		}

		int shown = _radiusDraftFor == guid ? _radiusDraft : radius;
		bool done = false;
		ImGui.PushID("radius");
		ImGui.Text("Radius, in world units:");
		if (Field("##radius", ref shown, ref done)) {
			_radiusDraftFor = guid;
			_radiusDraft = shown;
		}

		if (done && _radiusDraftFor == guid) {
			int draft = _radiusDraft;
			_radiusDraftFor = null;
			if (TriggerAreaEditing.Radius(TriggerAreaEditing.StoredRadius(draft)) != radius) {
				_mover.CommitRadius(guid, draft);
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
