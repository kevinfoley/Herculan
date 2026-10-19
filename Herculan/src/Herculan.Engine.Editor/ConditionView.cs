using HercWorks.Core.Data.File.Msn;
using Herculan.Engine.Platform;
using ImGuiNET;

namespace Herculan.Engine.Editor;

/// <summary>
/// The Properties panel's view of a condition, a row-1 record: its kind; a flag test's flag, operator and value, a draw's bound
/// and the number it drew, or a range's draw and ends; what it requires; whether it holds in the campaign state shown; what
/// references it, as links; and Delete, refused while anything does. Each a field or choice that changes it
/// (<see cref="ConditionEditor"/>). A variant table and a range over one are shown read-only. What each kind tests is
/// docs/retail/formats/msn-mission-file.md#the-conditions--row-1.
/// </summary>
internal sealed class ConditionView {
	private readonly ConditionEditor _editor;
	private readonly PanelLinks _links;

	// A number being typed, and the field it is for, until the field lets go.
	private string? _draftFor;
	private int _draft;

	// The condition drawn, whose GUID keeps one condition's draft out of another's field.
	private short _shown = -1;

	public ConditionView(ConditionEditor editor, PanelLinks links) {
		_editor = editor;
		_links = links;
	}

	public void Draw(short guid) {
		if (_editor.Source is not { } source || ConditionEditing.IndexOf(source, guid) == null) {
			return;
		}

		_shown = guid;
		var record = ConditionEditing.Record(source, guid);
		string? why = _editor.WhyFixed(guid);
		bool locked = why != null;
		ImGui.Text($"Condition {guid}");
		ImGui.Text(KindName(source, record));
		ImGui.Separator();
		if (ConditionEditing.InVariantTable(source, guid)) {
			ImGui.TextWrapped(ConditionEditing.Describe(source.Mission, record));
			EditorText.Note("Variant tables are edited in a later step.");
		} else {
			EditorText.Reason(why);
		}

		ImGui.PushID($"condition{guid}");
		switch (record.Type) {
			case ConditionEditing.FlagTest:
				DrawFlagTest(guid, record, locked);
				DrawRequirement(source, guid, record, locked);
				break;
			case ConditionEditing.Draw:
				DrawDraw(source, guid, record, locked);
				DrawRequirement(source, guid, record, locked);
				break;
			case ConditionEditing.Range:
				DrawRange(source, guid, record, locked);
				break;
		}

		ImGui.Spacing();
		if (_editor.Holds(guid)) {
			ImGui.Text("Holds in the campaign state shown.");
		} else {
			EditorText.Emphasis("Fails in the campaign state shown.");
		}

		DrawUses(source, guid, record, locked);
		ImGui.PopID();
		EditorText.Reason(_editor.Failure);
	}

	// What kind of condition the record is, in a word or two.
	private static string KindName(MissionDocument source, MissionCondition14 record) => record.Type switch {
		ConditionEditing.FlagTest => "Flag test",
		ConditionEditing.Draw => "Draw",
		ConditionEditing.Range when ConditionEditing.ParentOf(source, record.GUID)?.Type == ConditionEditing.VariantTable =>
			"Range over a variant table",
		ConditionEditing.Range => "Range over a draw",
		ConditionEditing.VariantTable => "Variant table",
		_ => $"Type {record.Type}",
	};

	// The flag (with what it holds where that is known), the operator and the value.
	private void DrawFlagTest(short guid, MissionCondition14 record, bool locked) {
		int flag = record.FlagIndexOrRangeLower;
		if (IntField("Flag", "flag", flag, 0, ConditionEditing.Flags - 1, locked) is { } typedFlag) {
			_editor.RequestFlag(guid, (short)typedFlag);
		}

		if (flag is >= 0 and < ConditionEditing.Flags && CampaignState.Label(flag) is { } label) {
			EditorText.Note(label);
		}

		Label("Test");
		ImGui.BeginDisabled(locked);
		string current = ConditionEditing.OperatorText(record.OperatorOrRangeUpperOrResult) ?? $"code {record.OperatorOrRangeUpperOrResult}";
		if (ImGui.BeginCombo("##operator", $"flag {current} value")) {
			foreach (short op in ConditionEditing.Operators) {
				if (ImGui.Selectable($"flag {ConditionEditing.OperatorText(op)} value", op == record.OperatorOrRangeUpperOrResult)
						&& op != record.OperatorOrRangeUpperOrResult) {
					_editor.RequestOperator(guid, op);
				}
			}

			ImGui.EndCombo();
		}

		ImGui.EndDisabled();
		if (IntField("Value", "value", record.ComparisonOperand, short.MinValue, short.MaxValue, locked) is { } typedValue) {
			_editor.RequestValue(guid, (short)typedValue);
		}

		EditorText.Note($"Holds while {ConditionEditing.Describe(_editor.Source!.Mission, record)}.");
	}

	// The bound, the number drawn in the state shown, and New range over this draw.
	private void DrawDraw(MissionDocument source, short guid, MissionCondition14 record, bool locked) {
		if (IntField("Below", "bound", record.FlagIndexOrRangeLower, 1, ConditionEditing.MaxBound, locked) is { } bound) {
			_editor.RequestBound(guid, (short)bound);
		}

		EditorText.Note($"It draws a number from 0 to {record.FlagIndexOrRangeLower - 1}.");
		if (ConditionEditing.Outcome(source, guid).Draw is { } drawn) {
			ImGui.Text($"Drawn in the campaign state shown: {drawn}");
		} else {
			EditorText.Note("Not drawn in the campaign state shown: what it requires fails.");
		}

		EditorText.Note("Adding or deleting a draw, or changing whether it is made, changes what every later draw and "
			+ "variant pick comes out as.");
		ImGui.BeginDisabled(locked);
		if (ImGui.Button("New range over this draw")) {
			_editor.RequestCreateRange(guid);
		}

		ImGui.EndDisabled();
	}

	// The draw it is over, a choice of the draws before it with a link, and its two ends.
	private void DrawRange(MissionDocument source, short guid, MissionCondition14 record, bool locked) {
		var parent = ConditionEditing.ParentOf(source, guid);
		if (parent?.Type == ConditionEditing.VariantTable) {
			ImGui.Text("Variant table:");
			ImGui.SameLine();
			_links.Link($"Condition {parent.GUID}", new ConditionSelection(parent.GUID));
			ImGui.Text($"From {(ushort)record.FlagIndexOrRangeLower} to {(ushort)record.OperatorOrRangeUpperOrResult}");
			return;
		}

		Label("Draw");
		ImGui.BeginDisabled(locked);
		var draws = ConditionEditing.Earlier(source, guid)
			.Where(other => ConditionEditing.Record(source, other).Type == ConditionEditing.Draw).ToList();
		if (ImGui.BeginCombo("##draw", parent?.Type == ConditionEditing.Draw ? $"Condition {parent.GUID}" : "(none)")) {
			foreach (short draw in draws) {
				if (ImGui.Selectable($"Condition {draw}: {ConditionEditing.Describe(source.Mission, draw)}", draw == record.ConditionRef)
						&& draw != record.ConditionRef) {
					_editor.RequestRequirement(guid, draw);
				}
			}

			ImGui.EndCombo();
		}

		ImGui.EndDisabled();
		if (parent?.Type == ConditionEditing.Draw) {
			_links.Link($"Condition {parent.GUID}", new ConditionSelection(parent.GUID));
		}

		short low = record.FlagIndexOrRangeLower, high = record.OperatorOrRangeUpperOrResult;
		if (IntField("From", "low", low, 0, ConditionEditing.MaxBound, locked) is { } typedLow) {
			_editor.RequestEnds(guid, (short)typedLow, high);
		}

		if (IntField("To", "high", high, 0, ConditionEditing.MaxBound, locked) is { } typedHigh) {
			_editor.RequestEnds(guid, low, (short)typedHigh);
		}

		if (parent?.Type == ConditionEditing.Draw) {
			short bound = parent.FlagIndexOrRangeLower;
			if (low > high || low >= bound) {
				EditorText.Note($"It never holds: condition {parent.GUID} draws 0 to {bound - 1}.");
			} else {
				EditorText.Note($"Condition {parent.GUID} draws 0 to {bound - 1}, so a range past {bound - 1} never holds there.");
			}
		}

		EditorText.Note("A range requires nothing else: it holds when its draw is made and the number drawn is from the one end "
			+ "to the other.");
	}

	// What it requires: none or a condition before it, with a link.
	private void DrawRequirement(MissionDocument source, short guid, MissionCondition14 record, bool locked) {
		Label("Requires");
		ImGui.BeginDisabled(locked);
		string current = record.ConditionRef == -1 ? "None" : $"Condition {record.ConditionRef}";
		if (ImGui.BeginCombo("##requires", current)) {
			if (ImGui.Selectable("None", record.ConditionRef == -1) && record.ConditionRef != -1) {
				_editor.RequestRequirement(guid, -1);
			}

			foreach (short earlier in ConditionEditing.Earlier(source, guid)) {
				if (ImGui.Selectable($"Condition {earlier}: {ConditionEditing.Describe(source.Mission, earlier)}",
						earlier == record.ConditionRef) && earlier != record.ConditionRef) {
					_editor.RequestRequirement(guid, earlier);
				}
			}

			ImGui.EndCombo();
		}

		ImGui.EndDisabled();
		if (record.ConditionRef != -1) {
			_links.Link($"Condition {record.ConditionRef}", new ConditionSelection(record.ConditionRef));
			EditorText.Note("It holds only while that condition holds too.");
		}

		EditorText.Note("Only a condition before it can be required.");
	}

	// What references it, one short line each, then Delete or why it cannot be deleted.
	private void DrawUses(MissionDocument source, short guid, MissionCondition14 record, bool locked) {
		var uses = _editor.UsesOf(guid);
		ImGui.Separator();
		ImGui.Text("Used by:");
		ImGui.PushID("uses");
		DrawUseList(uses);
		if (!uses.Any) {
			EditorText.Note("Nothing.");
		}

		ImGui.PopID();
		ImGui.Separator();
		if (locked) {
			return;
		}

		if (uses.Any) {
			ImGui.TextWrapped("Cannot be deleted while the records under Used by reference it.");
			return;
		}

		if (ImGui.Button("Delete")) {
			_editor.RequestDelete(guid);
		}

		if (record.Type == ConditionEditing.Draw) {
			EditorText.Note("Deleting a draw changes what every later draw and variant pick comes out as.");
		}
	}

	private void DrawUseList(ConditionUses uses) {
		for (int i = 0; i < uses.Shown.Count; i++) {
			var use = uses.Shown[i];
			ImGui.PushID(i);
			if (use.Target is { } target) {
				_links.Link(use.Label, target);
			} else {
				ImGui.Text(use.Label);
			}

			ImGui.PopID();
		}

		if (uses.LeftOut.Count > 0) {
			int count = uses.LeftOut.Sum(kind => kind.Count);
			ImGui.TextWrapped($"{count} more, which this state of the mission leaves out: "
				+ string.Join(", ", uses.LeftOut.Select(kind => $"{kind.Count} {kind.Kind}")));
		}
	}

	// A typed number labelled <label>, kept to <min>..<max>; answers the value when the field lets go after a change.
	private int? IntField(string label, string field, int current, int min, int max, bool locked) {
		string id = $"{field}{_shown}";
		if (_draftFor != null && _draftFor != id && !ImGui.IsAnyItemActive()) {
			_draftFor = null;
		}

		int shown = _draftFor == id ? _draft : current;
		Label(label);
		ImGui.BeginDisabled(locked);
		if (ImGui.InputInt($"##{id}", ref shown, 0, 0)) {
			_draftFor = id;
			_draft = shown;
		}

		int? typed = null;
		if (ImGui.IsItemDeactivatedAfterEdit() && _draftFor == id) {
			_draftFor = null;
			int value = Math.Clamp(_draft, min, max);
			if (value != current) {
				typed = value;
			}
		}

		ImGui.EndDisabled();
		return typed;
	}

	// A field's label, with the field after it filling the rest of the line.
	private static void Label(string text) {
		ImGui.AlignTextToFramePadding();
		ImGui.Text(text);
		ImGui.SameLine(ScaledImGui.Scaled(90f));
		ImGui.SetNextItemWidth(-1f);
	}
}
