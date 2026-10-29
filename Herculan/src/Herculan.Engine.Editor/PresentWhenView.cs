using HercWorks.Core.Data.File.Msn;
using Herculan.Engine.Platform;
using ImGuiNET;

namespace Herculan.Engine.Editor;

/// <summary>
/// A record's Present when, drawn by the views of the records retail conditions (an object, a group, an order slot and an
/// objective), in the view itself (docs/herculan/editor-design.md#present-when): a sentence stating it; Always,
/// Only when a campaign variable... or In some loads..., with the clauses as rows, each campaign variable picked from a list
/// (<see cref="CampaignVariablePicker"/>); what its variations change; and, under a collapsed Advanced header, the condition
/// records behind it. Where the clauses cannot state it, the sentence (when it can be said), why, and Advanced. Then notes on
/// what being absent changes: what loses its reference to the record, and for a later order where its group stops. Each
/// change is one set (<see cref="PresenceEditor"/>).
/// </summary>
internal sealed class PresentWhenView {
	// The chance a new In some loads... starts at (the editor's choice).
	private const int NewPercent = 50;

	private static readonly string[] Modes = ["Always", "Only when a campaign variable...", "In some loads..."];

	private readonly PresenceEditor _editor;
	private readonly PanelLinks _links;
	private readonly CampaignVariablePicker _picker = new();

	// The target given a variable row not yet filled, by Only when a campaign variable... or And also when....
	private PresenceTarget? _adding;

	// A number being typed, and the ImGui ID of the field it is for, until the field lets go.
	private uint? _draftFor;
	private int _draft;

	public PresentWhenView(PresenceEditor editor, PanelLinks links) {
		_editor = editor;
		_links = links;
	}

	/// <summary>Draws <paramref name="target"/>'s Present when; nothing for null, which the squad has.</summary>
	public void Draw(PresenceTarget? target) {
		if (target == null) {
			return;
		}

		var source = _editor.Source;
		var records = RecordConditions.Records(source, target);
		if (records.Count == 0) {
			return;
		}

		ImGui.PushID("presentWhen");
		var reading = PresenceRules.Read(source, target);
		string? why = _editor.WhyFixed(target) ?? reading.WhyFixed;
		EditorText.Emphasis(PresenceSentences.Presence(reading.Rule));
		if (why == null) {
			DrawClauses(target, reading.Rule!);
		} else if (why == RecordConditions.SquadPresence
				|| (target == PresenceTarget.OrderSlot(target.Group, 0) && why == GroupOrders.WhyNeedsFirstOrder(source, target.Group))) {
			// The squad's records, and the first order every such group keeps: said on hover, not under every one of them.
			EditorText.Tooltip(why);
		} else {
			EditorText.Reason(why);
		}

		DrawOutsideCampaign(reading.Rule);

		DrawVariations(source, target);
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

		DrawAdvanced(source, target, records);
		ImGui.PopID();
	}

	// The choice of Always, Only when a campaign variable... and In some loads..., then a row per clause and the buttons that
	// add one.
	private void DrawClauses(PresenceTarget target, PresenceRule rule) {
		bool adding = _adding == target;
		int mode = rule.Variables.Count > 0 || adding ? 1 : rule.Chance != null ? 2 : 0;
		ImGui.SetNextItemWidth(-1f);
		if (EditorText.BeginCombo("##present", Modes[mode])) {
			if (ImGui.Selectable(Modes[0], mode == 0) && mode != 0) {
				_adding = null;
				Ask(target, rule, PresenceRule.Always);
			}

			if (ImGui.Selectable(Modes[1], mode == 1) && mode != 1) {
				_adding = target;
			}

			var chanceOnly = new PresenceRule([], rule.Chance ?? ChanceClause.OfPercent(NewPercent));
			if (ImGui.Selectable(Modes[2], mode == 2) && mode != 2) {
				_adding = null;
				Ask(target, rule, chanceOnly);
			}

			ImGui.EndCombo();
		}

		for (int i = 0; i < rule.Variables.Count; i++) {
			ImGui.PushID(i);
			DrawVariable(target, rule, i);
			ImGui.PopID();
		}

		if (adding) {
			DrawNewVariable(target, rule);
		}

		if (rule.Chance is { } chance) {
			DrawChance(target, rule, chance);
		}

		if (mode == 0) {
			return;
		}

		if (!adding && ImGui.Button("And also when...")) {
			_adding = target;
		}

		if (rule.Chance == null && mode == 1) {
			if (!adding) {
				ImGui.SameLine();
			}

			var withChance = rule.WithChance(ChanceClause.OfPercent(NewPercent));
			ImGui.BeginDisabled(rule.Variables.Count == 0);
			if (ImGui.Button("And only in some loads...")) {
				Ask(target, rule, withChance);
			}

			ImGui.EndDisabled();
		}
	}

	// One variable clause: the variable, then the test, the value and Remove on one line.
	private void DrawVariable(PresenceTarget target, PresenceRule rule, int index) {
		var clause = rule.Variables[index];
		if (index > 0) {
			EditorText.Note("and");
		}

		if (_picker.Draw("##variable", clause.Variable, _editor.Source) is { } variable && variable != clause.Variable) {
			Ask(target, rule, rule.WithVariable(index, clause with { Variable = variable }));
		}

		var style = ImGui.GetStyle();
		float remove = ImGui.CalcTextSize("Remove").X + style.FramePadding.X * 2;
		float rest = ImGui.GetContentRegionAvail().X - remove - style.ItemSpacing.X * 2;
		if (CampaignVariableLabels.MeaningOf(clause) is { } meaning) {
			ImGui.SetNextItemWidth(rest + style.ItemSpacing.X);
			DrawMeaning(target, rule, index, meaning);
		} else {
			DrawTest(target, rule, index, rest);
		}

		ImGui.SameLine();
		var without = rule.WithoutVariable(index);
		if (ImGui.Button("Remove")) {
			Ask(target, rule, without);
		}
	}

	// A clause testing for a value the retail docs give a meaning: a choice of those meanings, each writing its own test.
	private void DrawMeaning(PresenceTarget target, PresenceRule rule, int index, VariableMeaning current) {
		var clause = rule.Variables[index];
		if (!EditorText.BeginCombo("##meaning", current.Name)) {
			return;
		}

		foreach (var meaning in CampaignVariableLabels.Meanings(clause.Variable)) {
			if (ImGui.Selectable(meaning.Name, meaning == current) && meaning != current) {
				Ask(target, rule, rule.WithVariable(index, clause with { Operator = meaning.Operator, Value = meaning.Operand }));
			}
		}

		ImGui.EndCombo();
	}

	// A clause comparing its variable with a number: the test, then the value, sharing <width>.
	private void DrawTest(PresenceTarget target, PresenceRule rule, int index, float width) {
		var clause = rule.Variables[index];
		ImGui.SetNextItemWidth(width * 0.55f);
		if (EditorText.BeginCombo("##operator", PresenceSentences.Operator(clause.Operator))) {
			foreach (short op in ConditionEditing.Operators) {
				if (ImGui.Selectable(PresenceSentences.Operator(op), op == clause.Operator) && op != clause.Operator) {
					Ask(target, rule, rule.WithVariable(index, clause with { Operator = op }));
				}
			}

			ImGui.EndCombo();
		}

		ImGui.SameLine();
		ImGui.SetNextItemWidth(width * 0.45f);
		if (Number("value", clause.Value, short.MinValue, short.MaxValue) is { } value && value != clause.Value) {
			Ask(target, rule, rule.WithVariable(index, clause with { Value = (short)value }));
		}
	}

	// The variable row not yet filled: picking a variable adds its clause, "is more than 0" as a new flag test reads
	// (ConditionEditing.NewOperator, NewValue).
	private void DrawNewVariable(PresenceTarget target, PresenceRule rule) {
		ImGui.PushID("adding");
		if (rule.Variables.Count > 0) {
			EditorText.Note("and");
		}

		if (_picker.Draw("##variable", null, _editor.Source) is { } variable) {
			_adding = null;
			Ask(target, rule, rule.WithVariableAdded(new VariableClause(variable, ConditionEditing.NewOperator, ConditionEditing.NewValue)));
		}

		if (ImGui.Button("Cancel")) {
			_adding = null;
		}

		ImGui.PopID();
	}

	// "In about [N]% of loads", and Remove.
	private void DrawChance(PresenceTarget target, PresenceRule rule, ChanceClause chance) {
		ImGui.PushID("chance");
		if (rule.Variables.Count > 0) {
			EditorText.Note("and");
		}

		ImGui.AlignTextToFramePadding();
		ImGui.Text("In about");
		ImGui.SameLine();
		ImGui.SetNextItemWidth(ScaledImGui.Scaled(48f));
		if (Number("percent", chance.Percent, ChanceClause.MinPercent, ChanceClause.MaxPercent) is { } percent) {
			Ask(target, rule, rule.WithChance(ChanceClause.OfPercent(percent)));
		}

		ImGui.SameLine();
		ImGui.TextUnformatted("% of loads");
		ImGui.SameLine();
		var without = rule.WithChance(null);
		if (ImGui.Button("Remove")) {
			Ask(target, rule, without);
		}

		ImGui.PopID();
	}

	/// <summary>
	/// For a herc, flyer, structure or ground vehicle whose group the mission check finds standing with no member where its
	/// members are absent (<see cref="MissionCheck.EmptyUnless"/>), what that does, and a button that sets the member's Present
	/// when on <paramref name="group"/> instead (<see cref="RecordConditions.MoveToGroup"/>); nothing otherwise.
	/// </summary>
	public void DrawGroupLeftEmpty(PresenceTarget? target, short group) {
		var source = _editor.Source;
		if (target == null || MissionCheck.EmptyUnless(source.Layout, group) is null) {
			return;
		}

		ImGui.PushID("leftEmpty");
		EditorText.Problem("Where it is absent, its group has no members, which the mission does not handle: a win or lose "
			+ "condition, an order or a trigger that asks about the group can crash it or fire by mistake.");
		string? why = _editor.WhyNoMoveToGroup(target, group);
		ImGui.BeginDisabled(why != null);
		if (ImGui.Button("Move \"Present when\" to its group")) {
			_editor.RequestMoveToGroup(target, group);
		}

		ImGui.EndDisabled();
		EditorText.Reason(why);
		ImGui.PopID();
	}

	// Outside the campaign every campaign variable but the three random numbers is 0, so a rule with a clause on any other that
	// fails at 0 never holds there (ShellTrainingLaunch's SeedDrawnFlags): said for a mission not played in the campaign.
	private void DrawOutsideCampaign(PresenceRule? rule) {
		if (rule == null || _editor.Campaign()
				|| !rule.Variables.Any(clause => !CampaignVariableLabels.IsRandomNumber(clause.Variable) && !CampaignVariableLabels.Holds(clause, 0))) {
			return;
		}

		EditorText.Problem("Never present in this mission: outside the campaign (a new mission, or a practice mission) every "
			+ "campaign variable but the three random numbers is 0.");
	}

	// One sentence per condition of the records of the target that change some of its fields: which fields, and when.
	private static void DrawVariations(MissionDocument source, PresenceTarget target) {
		var conditions = RecordConditions.VariationConditions(source, target);
		if (conditions.Count == 0 || source.Layout.Entry(target.Row, RecordConditions.GuidOf(source, target)) is not { } entry) {
			return;
		}

		foreach (short condition in conditions) {
			var words = entry.Variations.Where(variation => variation.Condition == condition).SelectMany(variation => variation.Words);
			var rule = condition == RecordConditions.Always ? PresenceRule.Always : PresenceRules.ReadCondition(source, condition).Rule;
			EditorText.Wrapped(PresenceSentences.Variation(entry.Row, RecordAspects.Of(entry.Row, words), rule));
		}

		EditorText.Note("An edit in this view does not change those details in those versions.");
	}

	// The condition records behind it, collapsed: the condition every record carries, a choice of the mission's flag tests
	// and ranges over a draw with a link, or each record's own when they differ.
	private void DrawAdvanced(MissionDocument source, PresenceTarget target, IReadOnlyList<PresenceRecord> records) {
		if (!ImGui.CollapsingHeader("Advanced")) {
			return;
		}

		ImGui.Indent();
		short? shared = RecordConditions.Shared(source, target);
		short current = shared ?? records[0].Condition;
		ImGui.SetNextItemWidth(-1f);
		ImGui.BeginDisabled(_editor.WhyFixed(target) != null);
		if (EditorText.BeginCombo("##condition", shared == null ? "(differs by record)" : Name(source, current))) {
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

		ImGui.EndDisabled();
		if (shared == null) {
			DrawRecords(source, records);
		} else if (current != RecordConditions.Always) {
			DrawChosen(source, current);
		}

		if (RecordConditions.PicksFromRandomTable(source, target)) {
			EditorText.Note("It picks from a random table, so where it is absent every later random table roll comes out differently.");
		}

		ImGui.Unindent();
	}

	// The condition chosen, as a link.
	private void DrawChosen(MissionDocument source, short condition) {
		if (ConditionEditing.IndexOf(source, condition) == null) {
			EditorText.Note("It references a condition the mission does not have, so it is never present.");
			return;
		}

		_links.Link($"Condition {condition}", new ConditionSelection(condition));
	}

	// Each record's condition, one short line each, when they differ, and why none is set.
	private static void DrawRecords(MissionDocument source, IReadOnlyList<PresenceRecord> records) {
		for (int i = 0; i < records.Count; i++) {
			short condition = records[i].Condition;
			ImGui.Text(condition == RecordConditions.Always ? $"Record {i + 1}: always" : $"Record {i + 1}: {Name(source, condition, ' ')}");
		}

		EditorText.Note("Its records carry different conditions, so it is not set here: setting one alone could leave another "
			+ "making a half-filled copy of it where the first is absent.");
	}

	// What loses its reference to the record where it is absent, one identity link per line.
	private void DrawLosers(PresenceTarget target) {
		if (_editor.LosersOf(target) is not { Any: true } losers) {
			return;
		}

		ImGui.PushID("losers");
		EditorText.Note("Where it is absent, these lose their reference to it:");
		_links.References(losers.Names);
		foreach (short group in losers.MemberOf) {
			ImGui.PushID($"member{group}");
			_links.ShortLayoutGroup(group, EditorText.AfterRoom(": member"));
			EditorText.After(": member");
			ImGui.PopID();
		}

		ImGui.PopID();
	}

	// Asks for <wanted> unless it is what <rule> already is.
	private void Ask(PresenceTarget target, PresenceRule rule, PresenceRule wanted) {
		if (!wanted.Equals(rule)) {
			_editor.RequestRule(target, wanted);
		}
	}

	// A typed number for <field>, kept to <min>..<max>, its width set by the caller; answers the value when the field lets go
	// after a change.
	private int? Number(string field, int current, int min, int max) {
		string label = $"##{field}";
		uint id = ImGui.GetID(label);
		if (_draftFor != null && _draftFor != id && !ImGui.IsAnyItemActive()) {
			_draftFor = null;
		}

		int shown = _draftFor == id ? _draft : current;
		if (ImGui.InputInt(label, ref shown, 0, 0)) {
			_draftFor = id;
			_draft = shown;
		}

		if (ImGui.IsItemDeactivatedAfterEdit() && _draftFor == id) {
			_draftFor = null;
			int value = Math.Clamp(_draft, min, max);
			return value != current ? value : null;
		}

		return null;
	}

	// "Condition 5: flag 30 > 0", or with <separator> ' ' "condition 5 (flag 30 > 0)".
	private static string Name(MissionDocument source, short condition, char separator = ':') {
		if (condition == RecordConditions.Always) {
			return "Always";
		}

		string described = ConditionEditing.Describe(source.Mission, condition) ?? "missing";
		return separator == ':' ? $"Condition {condition}: {described}" : $"condition {condition} ({described})";
	}
}
