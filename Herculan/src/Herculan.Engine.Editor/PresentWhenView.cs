using HercWorks.Core.Data.File.Msn;
using ImGuiNET;

namespace Herculan.Engine.Editor;

/// <summary>
/// A record's Present when, drawn by the views of the records retail conditions (an object, a group, an order slot and an
/// objective): Always or a flag test or range over a draw, with a link to it; New flag test and New draw range; whether
/// it holds in the campaign state shown; each record's condition when the GUID's records differ; and notes on what the
/// condition changes: what loses its reference to the record where it is absent, the variant picks a variant-keyed record
/// moves, and the fields another record present only under its own condition supplies
/// (docs/herculan/handoff-editor-mission-editing.md#in-place, "Present when"). Each choice is a set
/// (<see cref="PresenceEditor"/>).
/// </summary>
internal sealed class PresentWhenView {
	private readonly PresenceEditor _editor;
	private readonly PanelLinks _links;
	private readonly Action<DeleteNamers, IReadOnlyList<int>> _references;

	/// <param name="references">Draws what references something, one line per record, given the groups whose own route field references it.</param>
	public PresentWhenView(PresenceEditor editor, PanelLinks links, Action<DeleteNamers, IReadOnlyList<int>> references) {
		_editor = editor;
		_links = links;
		_references = references;
	}

	/// <summary>Draws <paramref name="target"/>'s Present when; nothing for null, which the squad and a plain <c>script.dat</c> have.</summary>
	public void Draw(PresenceTarget? target) {
		if (target == null || _editor.Source is not { } source) {
			return;
		}

		var records = RecordConditions.Records(source, target);
		if (records.Count == 0) {
			return;
		}

		ImGui.PushID("presentWhen");
		string? why = _editor.WhyFixed(target);
		short? shared = RecordConditions.Shared(source, target);
		short current = shared ?? records[0].Condition;
		ImGui.Text("Present when:");
		ImGui.SetNextItemWidth(-1f);
		ImGui.BeginDisabled(why != null);
		if (ImGui.BeginCombo("##presence", shared == null ? "(differs by record)" : Name(source, current))) {
			if (ImGui.Selectable("Always", current == RecordConditions.Always) && current != RecordConditions.Always) {
				_editor.Request(target, RecordConditions.Always);
			}

			foreach (short choice in RecordConditions.Choices(source)) {
				if (ImGui.Selectable(Name(source, choice), choice == current) && choice != current) {
					_editor.Request(target, choice);
				}
			}

			ImGui.EndCombo();
		}

		if (ImGui.Button("New flag test")) {
			_editor.RequestNewFlagTest(target);
		}

		ImGui.SameLine();
		if (ImGui.Button("New draw range")) {
			_editor.RequestNewDrawRange(target);
		}

		if (ImGui.IsItemHovered()) {
			ImGui.SetTooltip("Adds a draw, which changes what every variant pick comes out as.");
		}

		ImGui.EndDisabled();
		if (shared == null) {
			DrawRecords(source, records);
		} else {
			EditorText.Reason(why);
			if (current != RecordConditions.Always) {
				DrawChosen(source, current);
			}
		}

		if (RecordConditions.DrawsVariant(source, target)) {
			EditorText.Note("It draws a variant, so where it is absent every later variant pick comes out differently.");
		}

		DrawSuppliers(source, target);
		if (records.Any(record => record.Condition != RecordConditions.Always)) {
			// Group_OrderTick moves on only to a next slot that holds an order (handoff "Orders").
			if (target.Slot > 0) {
				EditorText.Note("Where it is absent, the group stops at the order before it.");
			}

			DrawLosers(target);
		}

		if (_editor.FailureFor == target) {
			EditorText.Reason(_editor.Failure);
		}

		ImGui.PopID();
	}

	// The condition chosen: a link, and whether it holds in the campaign state shown.
	private void DrawChosen(MissionDocument source, short condition) {
		if (ConditionEditing.IndexOf(source, condition) == null) {
			EditorText.Note($"Condition {condition} is not in the file, so it is never present.");
			return;
		}

		_links.Link($"Condition {condition}", new ConditionSelection(condition));
		if (ConditionEditing.Outcome(source, condition).Survived) {
			ImGui.Text("Holds in the campaign state shown.");
		} else {
			EditorText.Emphasis("Fails in the campaign state shown.");
		}
	}

	// Each record's condition, one short line each, when they differ, and why none is set here.
	private static void DrawRecords(MissionDocument source, IReadOnlyList<PresenceRecord> records) {
		for (int i = 0; i < records.Count; i++) {
			short condition = records[i].Condition;
			ImGui.Text(condition == RecordConditions.Always ? $"Record {i + 1}: always" : $"Record {i + 1}: {Name(source, condition, ' ')}");
		}

		EditorText.Note("Its records carry different conditions, so it is not set here: setting one alone could leave another "
			+ "making a half-filled copy of it where the first is absent.");
	}

	// One note when a record other than the one that creates it supplies some of its fields under a condition of its own.
	private static void DrawSuppliers(MissionDocument source, PresenceTarget target) {
		var conditions = RecordConditions.ConditionedSuppliers(source, target);
		if (conditions.Count == 0) {
			return;
		}

		string named = string.Join(", ", conditions.Select(condition => Name(source, condition, ' ')));
		EditorText.Note(conditions.Count == 1
			? $"Some of these fields come from a record present only when {named}: an edit to them holds only then."
			: $"Some of these fields come from records present only when {named}: an edit to them holds only then.");
	}

	// What loses its reference to the record where it is absent, one identity link per line.
	private void DrawLosers(PresenceTarget target) {
		if (_editor.LosersOf(target) is not { Any: true } losers) {
			return;
		}

		ImGui.PushID("losers");
		EditorText.Note("Where it is absent, these lose their reference to it:");
		_references(losers.Names, []);
		foreach (int group in losers.MemberOf) {
			ImGui.PushID($"member{group}");
			_links.ShortGroup(group);
			ImGui.SameLine(0f, 0f);
			ImGui.Text(": member");
			ImGui.PopID();
		}

		ImGui.PopID();
	}

	// "Condition 5: flag 30 > 0", or with <separator> ' ' "condition 5 (flag 30 > 0)".
	private static string Name(MissionDocument source, short condition, char separator = ':') {
		if (condition == RecordConditions.Always) {
			return "Always";
		}

		string described = ConditionEditing.Describe(source.Mission, condition) ?? "not in the file";
		return separator == ':' ? $"Condition {condition}: {described}" : $"condition {condition} ({described})";
	}
}
