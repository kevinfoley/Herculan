using HercWorks.Core.Data.File.Msn;
using Herculan.Engine.Platform;
using ImGuiNET;

namespace Herculan.Engine.Editor;

/// <summary>
/// A Heading in an object's or a group's view (<see cref="OwnHeadings"/>): a choice of its own heading, typed in whole
/// degrees, or what it faces without one, set through <see cref="HeadingEditor"/> as one undo step; then one sentence for each
/// other version of the mission that names another heading. A structure or ground vehicle without one faces its group's heading,
/// turned by its formation slot; a group faces along its first order's route. A herc's or flyer's own heading is shown, not
/// set.
/// </summary>
internal sealed class HeadingField {
	private const int StructureRow = 14;
	private const int GroupRow = 16;

	private static readonly string[] ObjectSources = ["Own heading", "Group's heading"];
	private static readonly string[] GroupSources = ["Own heading", "From its route"];

	private readonly HeadingEditor _editor;

	// Degrees being typed, and the row and GUID they are for, until the field lets go.
	private (int Row, short Guid)? _draftFor;
	private int _draft;

	public HeadingField(HeadingEditor editor) {
		_editor = editor;
	}

	/// <summary>Draws <paramref name="key"/>'s heading, <paramref name="facing"/> the binary angle its object faces.</summary>
	public void DrawObject(ObjectKey key, int facing) {
		if (key.Row != StructureRow || _editor.Source.Layout.Entry(key.Row, key.Guid) == null) {
			ImGui.Text($"Heading: {MissionText.Heading(facing)}");
			return;
		}

		Draw(StructureRow, key.Guid, facing, ObjectSources,
			$"Facing {MissionText.Heading(facing)}: its group's heading, turned by its place in the group's formation.");
	}

	/// <summary>
	/// Draws row-16 GUID <paramref name="guid"/>'s heading, <paramref name="facing"/> the binary angle it faces, and whether
	/// its first order's route has a first leg to face along (<paramref name="alongRoute"/>).
	/// </summary>
	public void DrawGroup(short guid, int facing, bool alongRoute) {
		if (_editor.Source.Layout.Entry(GroupRow, guid) == null) {
			ImGui.Text($"Heading: {MissionText.Heading(facing)}");
			return;
		}

		Draw(GroupRow, guid, facing, GroupSources, alongRoute
			? $"Facing {MissionText.Heading(facing)}: along its route's first leg."
			: $"Facing {MissionText.Heading(facing)}: its first order has no route of two or more waypoints to face along.",
			"It turns the group's formation, and every member with no heading of its own.");
	}

	// The choice, with what the heading turns (<turns>) on hover, the degrees for an own heading or <without> for none, then
	// the other versions.
	private void Draw(int row, short guid, int facing, string[] sources, string without, string? turns = null) {
		var source = _editor.Source;
		short? own = OwnHeadings.Degrees(source, row, guid);
		string? why = _editor.WhyFixed(row, guid);
		string? noNone = _editor.WhyNoNone(row, guid);
		ImGui.PushID($"heading{row}");
		ImGui.AlignTextToFramePadding();
		ImGui.Text("Heading:");
		EditorText.Tooltip(turns);
		ImGui.SameLine();
		ImGui.BeginDisabled(why != null);
		ImGui.SetNextItemWidth(ChoiceWidth(sources));
		int chosen = own != null ? 0 : 1;
		bool open = EditorText.BeginCombo("##source", sources[chosen]);
		if (!open) {
			EditorText.Tooltip(turns);
		}

		if (open) {
			if (ImGui.Selectable(sources[0], chosen == 0) && chosen != 0) {
				_editor.Request(row, guid, DegreesOf(facing));
			}

			var flags = noNone != null ? ImGuiSelectableFlags.Disabled : ImGuiSelectableFlags.None;
			if (ImGui.Selectable(sources[1], chosen == 1, flags) && chosen != 1) {
				_editor.Request(row, guid, null);
			}

			if (noNone != null && ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) {
				ImGui.SetTooltip(noNone);
			}

			ImGui.EndCombo();
		}

		if (own is { } degrees) {
			ImGui.SameLine();
			DrawDegrees(row, guid, degrees);
		}

		ImGui.EndDisabled();
		if (own == null) {
			EditorText.Note(without);
		}

		EditorText.Reason(why);
		DrawOverrides(source, row, guid);
		EditorText.Reason(_editor.Failure);
		ImGui.PopID();
	}

	// The degrees, typed, taken to 0 to 359 and applied when the field lets go.
	private void DrawDegrees(int row, short guid, short current) {
		if (_draftFor != null && _draftFor != (row, guid) && !ImGui.IsAnyItemActive()) {
			_draftFor = null;
		}

		int shown = _draftFor == (row, guid) ? _draft : current;
		ImGui.SetNextItemWidth(ScaledImGui.Scaled(56f));
		if (ImGui.InputInt("##degrees", ref shown, 0, 0)) {
			_draftFor = (row, guid);
			_draft = shown;
		}

		if (ImGui.IsItemDeactivatedAfterEdit() && _draftFor == (row, guid)) {
			_draftFor = null;
			short value = (short)((_draft % 360 + 360) % 360);
			if (value != current) {
				_editor.Request(row, guid, value);
			}
		}

		ImGui.SameLine();
		ImGui.Text("deg");
	}

	// One sentence per other version that names another heading, from the records of the GUID under other conditions.
	private static void DrawOverrides(MissionDocument source, int row, short guid) {
		var overrides = OwnHeadings.Overrides(source, row, guid);
		if (overrides.Count == 0) {
			return;
		}

		foreach (var set in overrides) {
			var rule = set.Condition == RecordConditions.Always ? PresenceRule.Always : PresenceRules.ReadCondition(source, set.Condition).Rule;
			string faces = set.Degrees is { } degrees ? $"{degrees} deg" : "as it does with no heading of its own";
			EditorText.Wrapped($"It faces {faces} {PresenceSentences.Occasion(rule)}.");
		}

		EditorText.Note("A change here does not change it in those versions.");
	}

	// The choice's width: its longest label, its frame's padding either side and the arrow button, which is a frame high.
	private static float ChoiceWidth(string[] sources) =>
		sources.Max(label => ImGui.CalcTextSize(label).X) + ImGui.GetStyle().FramePadding.X * 2 + ImGui.GetFrameHeight();

	// A binary angle in whole degrees, 0 to 359, as a heading record holds them.
	private static short DegreesOf(int binaryAngle) =>
		(short)(((int)Math.Round((binaryAngle & 0xFFFF) * 360.0 / 65536.0)) % 360);
}
