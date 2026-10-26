using HercWorks.Core.Data.File.Msn;
using Herculan.Engine.Platform;
using ImGuiNET;

namespace Herculan.Engine.Editor;

/// <summary>
/// An object's Heading in its view. For a structure or ground vehicle, a choice of its own heading, typed in whole degrees, or
/// its group's, which its formation slot may turn (<see cref="StructureHeadings"/>), set through
/// <see cref="StructureHeadingEditor"/> as one undo step; then one sentence for each other version of the mission that names
/// another heading. A herc's or flyer's heading is shown, not set.
/// </summary>
internal sealed class StructureHeadingField {
	private const int StructureRow = 14;

	private static readonly string[] Sources = ["Own heading", "Group's heading"];

	private readonly StructureHeadingEditor _editor;

	// Degrees being typed, and the GUID of the structure they are for, until the field lets go.
	private short? _draftFor;
	private int _draft;

	public StructureHeadingField(StructureHeadingEditor editor) {
		_editor = editor;
	}

	/// <summary>Draws <paramref name="key"/>'s heading, <paramref name="facing"/> the binary angle its object faces.</summary>
	public void Draw(ObjectKey key, int facing) {
		var source = _editor.Source;
		if (key.Row != StructureRow || source.Layout.Entry(key.Row, key.Guid) == null) {
			ImGui.Text($"Heading: {MissionText.Heading(facing)}");
			return;
		}

		short guid = key.Guid;
		short? own = StructureHeadings.Degrees(source, guid);
		string? why = _editor.WhyFixed(guid);
		ImGui.PushID("heading");
		ImGui.AlignTextToFramePadding();
		ImGui.Text("Heading:");
		ImGui.SameLine();
		ImGui.BeginDisabled(why != null);
		ImGui.SetNextItemWidth(ChoiceWidth());
		int chosen = own != null ? 0 : 1;
		if (ImGui.BeginCombo("##source", Sources[chosen])) {
			if (ImGui.Selectable(Sources[0], chosen == 0) && chosen != 0) {
				_editor.Request(guid, DegreesOf(facing));
			}

			if (ImGui.Selectable(Sources[1], chosen == 1) && chosen != 1) {
				_editor.Request(guid, null);
			}

			ImGui.EndCombo();
		}

		if (own is { } degrees) {
			ImGui.SameLine();
			DrawDegrees(guid, degrees);
		}

		ImGui.EndDisabled();
		if (own == null) {
			EditorText.Note($"Facing {MissionText.Heading(facing)}: its group's heading, turned by its place in the group's formation.");
		}

		EditorText.Reason(why);
		DrawOverrides(source, guid);
		EditorText.Reason(_editor.Failure);
		ImGui.PopID();
	}

	// The degrees, typed, taken to 0 to 359 and applied when the field lets go.
	private void DrawDegrees(short guid, short current) {
		if (_draftFor != null && _draftFor != guid && !ImGui.IsAnyItemActive()) {
			_draftFor = null;
		}

		int shown = _draftFor == guid ? _draft : current;
		ImGui.SetNextItemWidth(ScaledImGui.Scaled(56f));
		if (ImGui.InputInt("##degrees", ref shown, 0, 0)) {
			_draftFor = guid;
			_draft = shown;
		}

		if (ImGui.IsItemDeactivatedAfterEdit() && _draftFor == guid) {
			_draftFor = null;
			short value = (short)((_draft % 360 + 360) % 360);
			if (value != current) {
				_editor.Request(guid, value);
			}
		}

		ImGui.SameLine();
		ImGui.Text("deg");
	}

	// One sentence per other version that names another heading, from the records of the GUID under other conditions.
	private static void DrawOverrides(MissionDocument source, short guid) {
		var overrides = StructureHeadings.Overrides(source, guid);
		if (overrides.Count == 0) {
			return;
		}

		foreach (var set in overrides) {
			var rule = set.Condition == RecordConditions.Always ? PresenceRule.Always : PresenceRules.ReadCondition(source, set.Condition).Rule;
			string faces = set.Degrees is { } degrees ? $"{degrees} deg" : "its group's way";
			EditorText.Wrapped($"It faces {faces} {PresenceSentences.Occasion(rule)}.");
		}

		EditorText.Note("A change here does not change it in those versions.");
	}

	// The choice's width: its longest label, its frame's padding either side and the arrow button, which is a frame high.
	private static float ChoiceWidth() =>
		Sources.Max(label => ImGui.CalcTextSize(label).X) + ImGui.GetStyle().FramePadding.X * 2 + ImGui.GetFrameHeight();

	// A binary angle in whole degrees, 0 to 359, as a heading record holds them.
	private static short DegreesOf(int binaryAngle) =>
		(short)(((int)Math.Round((binaryAngle & 0xFFFF) * 360.0 / 65536.0)) % 360);
}
