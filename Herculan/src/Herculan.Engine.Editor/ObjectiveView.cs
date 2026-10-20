using HercWorks.Core.Data.File.Msn;
using HercWorks.Core.Data.File.Msn.Script;
using Herculan.Engine.Platform;
using Herculan.Engine.World;
using ImGuiNET;

namespace Herculan.Engine.Editor;

/// <summary>
/// The Properties panel's view of an objective: mandatory or a failure condition, its subject, its condition and, for
/// condition 0, its route, its failure text and its counter writes, each a field that changes it
/// (<see cref="ObjectiveEditor"/>), and a Delete button. What each field does is
/// docs/retail/simulation/mission-objectives.md's.
/// </summary>
internal sealed class ObjectiveView {
	private readonly MissionIndex _index;
	private readonly ScriptDat _document;
	private readonly ObjectiveEditor _editor;
	private readonly Action<int> _groupLink;
	private readonly Action<MissionUnitKind, int> _slotLink;
	private readonly Action<int> _routeLink;
	private readonly LineField _lines = new();
	private readonly PresentWhenView _presentWhen;
	private readonly PresenceEditor _presence;

	// A counter being typed, and the row-17 record and pair it is for, until its field lets go.
	private (int Record, int Slot)? _counterFor;
	private int _counter;

	/// <param name="document">The generated <c>script.dat</c>, whose records the view offers as subjects.</param>
	/// <param name="groupLink">Draws a link that selects a group, by block-11 index.</param>
	/// <param name="slotLink">Draws a link that selects the object in a roster slot.</param>
	/// <param name="routeLink">Draws a link that selects a route, by block-3 index.</param>
	/// <param name="presentWhen">Draws the objective's Present when (<paramref name="presence"/>'s target).</param>
	public ObjectiveView(MissionIndex index, ScriptDat document, ObjectiveEditor editor, Action<int> groupLink,
			Action<MissionUnitKind, int> slotLink, Action<int> routeLink, PresentWhenView presentWhen, PresenceEditor presence) {
		_index = index;
		_document = document;
		_editor = editor;
		_groupLink = groupLink;
		_slotLink = slotLink;
		_routeLink = routeLink;
		_presentWhen = presentWhen;
		_presence = presence;
	}

	private Mission Mission => _index.Mission;

	/// <summary>Row-17 record <paramref name="record"/>'s view, the objective generated at block-12 index <paramref name="index"/>.</summary>
	public void Draw(int record, int index) {
		if (index < 0 || index >= Mission.Objectives.Count) {
			return;
		}

		var objective = Mission.Objectives[index];
		bool locked = _editor.WhyFixed(record) != null;

		ImGui.Text($"Objective {index}");
		_presentWhen.Draw(_presence.ObjectiveTarget(index));
		ImGui.Separator();
		ImGui.BeginDisabled(locked);
		bool mandatory = objective.Required == MissionObjective.Mandatory;
		if (ImGui.RadioButton("Mandatory", mandatory) && !mandatory) {
			_editor.RequestRequired(record, true);
		}

		ImGui.SameLine();
		if (ImGui.RadioButton("Failure condition", !mandatory) && mandatory) {
			_editor.RequestRequired(record, false);
		}

		ImGui.EndDisabled();
		EditorText.Note(mandatory ? "The mission needs this to come true." : "The mission is lost the moment this comes true.");

		DrawSubject(record, objective, locked);
		DrawCondition(record, objective, locked);
		if (objective.ConditionCode == MissionObjective.ConditionOrderComplete) {
			DrawRoute(record, objective, locked);
		}

		DrawText(record, index, mandatory, locked);
		DrawCounters(record, objective, locked);
		DrawDelete(record);
		EditorText.Reason(_editor.Failure);
	}

	private void DrawSubject(int record, MissionObjective objective, bool locked) {
		ImGui.Spacing();
		short kind = (short)objective.SubjectKind;
		Label("Subject");
		ImGui.BeginDisabled(locked);
		if (ImGui.BeginCombo("##subject", MissionText.SubjectName(_index, kind, objective.SubjectRef))) {
			Choices(record, kind, objective.SubjectRef, ObjectiveEditing.GroupSubject, _document.Groups.Length);
			Choices(record, kind, objective.SubjectRef, ObjectiveEditing.HercSubject, _document.Mechs.Length);
			Choices(record, kind, objective.SubjectRef, ObjectiveEditing.StructureSubject, _document.Bases.Length);
			ImGui.EndCombo();
		}

		ImGui.EndDisabled();
		if (MissionIndex.UnitKindOf(objective.SubjectKind) is { } unit) {
			_slotLink(unit, objective.SubjectRef);
		} else if (objective.SubjectKind == MissionObjectiveSubject.Group) {
			_groupLink(objective.SubjectRef);
		} else {
			EditorText.Note($"Kind {kind}, which the simulator has no case for.");
		}
	}

	// The records of subject kind <choiceKind> the combo offers, under a heading.
	private void Choices(int record, short kind, int subject, short choiceKind, int count) {
		if (count == 0) {
			return;
		}

		ImGui.Separator();
		ImGui.TextDisabled(choiceKind switch {
			ObjectiveEditing.GroupSubject => "Groups",
			ObjectiveEditing.HercSubject => "Hercs",
			_ => "Structures",
		});
		for (int i = 0; i < count; i++) {
			bool current = kind == choiceKind && subject == i;
			if (ImGui.Selectable($"{MissionText.SubjectName(_index, choiceKind, i)}##{choiceKind}.{i}", current) && !current) {
				_editor.RequestSubject(record, choiceKind, _index.Records.SubjectGuid(choiceKind, i));
			}
		}
	}

	private void DrawCondition(int record, MissionObjective objective, bool locked) {
		bool groupSubject = objective.SubjectKind == MissionObjectiveSubject.Group;
		Label("Condition");
		ImGui.BeginDisabled(locked);
		if (ImGui.BeginCombo("##condition", Condition(objective.ConditionCode, groupSubject))) {
			foreach (short code in ObjectiveEditing.Conditions) {
				if (ImGui.Selectable(Condition(code, groupSubject), code == objective.ConditionCode) && code != objective.ConditionCode) {
					_editor.RequestCondition(record, code);
				}
			}

			ImGui.EndCombo();
		}

		ImGui.EndDisabled();
		ImGui.TextWrapped(MissionText.ObjectiveCondition(objective.ConditionCode, groupSubject));
		if (!ObjectiveEditing.TestsSubject(objective.ConditionCode)) {
			EditorText.Note("It tests the player alone: the subject is not looked at.");
		}
	}

	private static string Condition(short code, bool groupSubject) => $"{code}: {MissionText.ObjectiveCondition(code, groupSubject)}";

	// Condition 0's route, offered from the routes the subject's group's orders run, and the order slot it reads.
	private void DrawRoute(int record, MissionObjective objective, bool locked) {
		var choices = _editor.RouteChoices(record);
		Label("Route");
		ImGui.BeginDisabled(locked || choices.Count == 0);
		if (ImGui.BeginCombo("##route", objective.RouteRef >= 0 ? $"Route {objective.RouteRef}" : "None")) {
			foreach (int route in choices) {
				if (ImGui.Selectable($"Route {route}", route == objective.RouteRef) && route != objective.RouteRef) {
					_editor.RequestRoute(record, _index.Records.RouteGuid(route));
				}
			}

			ImGui.EndCombo();
		}

		ImGui.EndDisabled();
		if (objective.RouteRef >= 0) {
			_routeLink(objective.RouteRef);
		}

		int group = _index.OrderGroupOf(objective);
		if (group < 0) {
			return;
		}

		int slot = _index.SlotOnRoute(group, objective.RouteRef);
		ImGui.AlignTextToFramePadding();
		ImGui.Text("Asked of");
		ImGui.SameLine();
		_groupLink(group);
		EditorText.Note(slot >= 0
			? $"About its order {slot}, the first to run that route."
			: choices.Count == 0 ? "None of its orders runs a route, so this never comes true."
			: "None of its orders runs that route, so this never comes true.");
	}

	// The three lines of the failure text, each a field that applies when it lets go.
	private void DrawText(int record, int index, bool mandatory, bool locked) {
		ImGui.Separator();
		ImGui.Text("Failure text:");
		EditorText.Note(mandatory
			? "The status alert shows it while this is the first mandatory objective not yet true, up to its first empty line."
			: "Never shown: the status alert shows only a mandatory objective's text.");

		var lines = _editor.FailureLines(record);
		if (locked) {
			// An objective no record of the .MSN supplies, as its mission.str has it.
			foreach (string line in Mission.DescriptionOf(Mission.Objectives[index]).Where(line => line.Length > 0)) {
				ImGui.TextWrapped(line);
			}

			return;
		}

		if (lines.Count == 0) {
			string? why = _editor.WhyNoGiveText(record);
			ImGui.BeginDisabled(why != null);
			if (ImGui.Button("Add a failure text")) {
				_editor.RequestGiveText(record);
			}

			ImGui.EndDisabled();
			EditorText.Reason(why);
			return;
		}

		if (_editor.TextShared(record)) {
			EditorText.Note("Another record uses this text too: a change here gives this objective its own copy.");
		}

		for (int line = 0; line < lines.Count; line++) {
			if (lines[line] is not { } text) {
				EditorText.Note($"Line {line + 1}: no text shown in the editor.");
				continue;
			}

			if (_lines.Draw($"##failure{line}", $"{record}.{line}", text) is { } typed) {
				_editor.RequestText(record, line, typed);
			}
		}
	}

	// The counter writes, each a counter field and an operation, with Remove, and Add after the last.
	private void DrawCounters(int record, MissionObjective objective, bool locked) {
		ImGui.Separator();
		ImGui.Text("The first time it comes true:");
		int filled = 0;
		for (int slot = 0; slot < objective.CounterRefs.Count && slot < objective.CounterOps.Count; slot++) {
			if (objective.CounterRefs[slot] == -1 && objective.CounterOps[slot] == -1) {
				break;
			}

			filled++;
			ImGui.PushID($"pair{slot}");
			DrawPair(record, slot, objective.CounterRefs[slot], objective.CounterOps[slot], locked);
			ImGui.PopID();
		}

		if (filled == 0) {
			EditorText.Note("It writes no counter.");
		}

		string? why = _editor.WhyNoAddPair(record);
		ImGui.BeginDisabled(why != null);
		if (ImGui.Button("Add a counter write")) {
			_editor.RequestAddPair(record);
		}

		ImGui.EndDisabled();
		EditorText.Reason(why);
	}

	private void DrawPair(int record, int slot, short counter, short op, bool locked) {
		ImGui.BeginDisabled(locked);
		ImGui.AlignTextToFramePadding();
		ImGui.Text("Counter");
		ImGui.SameLine();
		int shown = _counterFor == (record, slot) ? _counter : counter;
		ImGui.SetNextItemWidth(ScaledImGui.Scaled(60f));
		if (ImGui.InputInt("##counter", ref shown, 0, 0)) {
			_counterFor = (record, slot);
			_counter = shown;
		}

		if (ImGui.IsItemDeactivatedAfterEdit() && _counterFor == (record, slot)) {
			_counterFor = null;
			if (_counter != counter) {
				_editor.RequestPair(record, slot, (short)Math.Clamp(_counter, 0, ObjectiveEditing.Counters - 1), Op(op));
			}
		}

		ImGui.SameLine();
		ImGui.SetNextItemWidth(ScaledImGui.Scaled(110f));
		if (ImGui.BeginCombo("##op", MissionText.ObjectiveCounterOp(op))) {
			for (short choice = ObjectiveEditing.CounterSet; choice <= ObjectiveEditing.CounterDecrement; choice++) {
				if (ImGui.Selectable(MissionText.ObjectiveCounterOp(choice), choice == op) && choice != op) {
					_editor.RequestPair(record, slot, (short)Math.Clamp((int)counter, 0, ObjectiveEditing.Counters - 1), choice);
				}
			}

			ImGui.EndCombo();
		}

		ImGui.SameLine();
		if (ImGui.SmallButton("Remove")) {
			_editor.RequestRemovePair(record, slot);
		}

		ImGui.EndDisabled();
		if (counter < 0 || op < 0) {
			EditorText.Note("Skipped: a write needs both a counter and an operation.");
		}
	}

	// An operation an edit keeps: the record's, or increment for one outside the four.
	private static short Op(short op) =>
		op is >= ObjectiveEditing.CounterSet and <= ObjectiveEditing.CounterDecrement ? op : ObjectiveEditing.CounterIncrement;

	private void DrawDelete(int record) {
		ImGui.Separator();
		string? why = _editor.WhyNoDelete(record);
		ImGui.BeginDisabled(why != null);
		if (ImGui.Button("Delete")) {
			_editor.RequestDelete(record);
		}

		ImGui.EndDisabled();
		EditorText.Reason(why, "Cannot be deleted: ");
	}

	// A field's label, with the choice after it filling the rest of the line.
	private static void Label(string text) {
		ImGui.AlignTextToFramePadding();
		ImGui.Text(text);
		ImGui.SameLine(ScaledImGui.Scaled(90f));
		ImGui.SetNextItemWidth(-1f);
	}
}
