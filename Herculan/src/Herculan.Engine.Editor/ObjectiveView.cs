using HercWorks.Core.Data.File.Msn;
using Herculan.Engine.Platform;
using Herculan.Engine.World;
using ImGuiNET;

namespace Herculan.Engine.Editor;

/// <summary>
/// The Properties panel's view of an objective, a win or a lose condition, from the <c>.MSN</c>'s layout by its row-17 index,
/// read as one sentence ("Must happen: Cybrid hercs (group 4) are written off"): whether it must or must not happen, who it
/// asks about, what happens to them and, for a finished order, on which route; the status alert text; the campaign variables
/// it sets (<see cref="CampaignVariableWrites"/>); each a choice or field that changes it (<see cref="ObjectiveEditor"/>);
/// and a Delete button. What each part does in play is docs/retail/simulation/mission-objectives.md's.
/// </summary>
internal sealed class ObjectiveView {
	private const int MechRow = 12;
	private const int BaseRow = 14;

	/// <summary>
	/// The conditions the What happens choice offers, in its order: each code <c>Mission_EvaluateObjectives</c> has a case for,
	/// less 4 and 10, which ask what 3 and 9 ask (the editor's choice); a record holding either shows it as the one it repeats.
	/// </summary>
	private static readonly short[] Offered = [
		MissionObjective.ConditionLost, MissionObjective.ConditionOrderComplete, MissionObjective.ConditionClear,
		MissionObjective.ConditionEngaged, MissionObjective.ConditionUnengaged, MissionObjective.ConditionDisarmed,
		MissionObjective.ConditionDataLink, MissionObjective.ConditionNoDataLink,
	];

	private readonly MissionIndex _index;
	private readonly ObjectiveEditor _editor;
	private readonly PanelLinks _links;
	private readonly LineField _lines = new();
	private readonly PresentWhenView _presentWhen;
	private readonly PresenceEditor _presence;
	private readonly CampaignVariableWrites _writes = new();

	/// <param name="presentWhen">Draws the objective's Present when (<paramref name="presence"/>'s target).</param>
	public ObjectiveView(MissionIndex index, ObjectiveEditor editor, PanelLinks links, PresentWhenView presentWhen,
			PresenceEditor presence) {
		_index = index;
		_editor = editor;
		_links = links;
		_presentWhen = presentWhen;
		_presence = presence;
	}

	private MissionNames Names => _links.Names;

	private MissionLayout Layout => _index.Layout;

	/// <summary>Row-17 record <paramref name="record"/>'s view; nothing for an index the row has no record at.</summary>
	public void Draw(int record) {
		if (Layout.Objectives.FirstOrDefault(objective => objective.Index == record) is not { } objective) {
			return;
		}

		var fields = objective.Fields;
		bool locked = _editor.WhyFixed(record) != null;
		bool mandatory = fields.Required == ObjectiveEditing.Mandatory;
		var name = Names.Objective(objective);
		ImGui.TextUnformatted(mandatory ? "Win condition" : "Lose condition");
		if (name.Number is { } number) {
			ImGui.SameLine(0f, ImGui.GetStyle().ItemInnerSpacing.X);
			ImGui.TextDisabled(number);
		}

		EditorText.Wrapped(name.Text);
		_presentWhen.Draw(_presence.ObjectiveTarget(record));
		ImGui.Separator();
		DrawRole(record, mandatory, locked);
		DrawWho(record, fields, locked);
		DrawWhat(record, fields, locked);
		if (fields.ConditionCode == MissionObjective.ConditionOrderComplete) {
			DrawRoute(record, fields, locked);
		}

		DrawText(record, mandatory, locked);
		DrawWrites(record, fields, locked);
		DrawDelete(record);
		EditorText.Reason(_editor.Failure);
	}

	// Must happen (a win condition) or must not happen (a lose condition), and what each does to the mission.
	private void DrawRole(int record, bool mandatory, bool locked) {
		Label("Role");
		ImGui.BeginDisabled(locked);
		if (ImGui.BeginCombo("##role", Role(mandatory))) {
			foreach (bool choice in new[] { true, false }) {
				if (ImGui.Selectable(Role(choice), choice == mandatory) && choice != mandatory) {
					_editor.RequestRequired(record, choice);
				}
			}

			ImGui.EndCombo();
		}

		ImGui.EndDisabled();
		EditorText.Note(mandatory
			? "The mission is won once every win condition holds, no lose condition does, and no enemy nearby is aware of the player."
			: "The mission is lost once this holds and no enemy nearby is aware of the player.");
	}

	private static string Role(bool mandatory) => mandatory ? "Must happen (win condition)" : "Must not happen (lose condition)";

	// Who it asks about: a group, a herc or a structure, picked by name, with a link.
	private void DrawWho(int record, MissionObjective58 fields, bool locked) {
		ImGui.Spacing();
		Label("Who");
		ImGui.BeginDisabled(locked);
		if (NameText.BeginCombo("##who", Names.Subject(fields.SubjectKind, fields.SubjectRef))) {
			Heading("Groups");
			foreach (var group in _index.Groups) {
				Choice(record, fields, ObjectiveEditing.GroupSubject, group.Guid, Names.Group(group.Guid));
			}

			Heading("Hercs");
			foreach (var herc in Layout.Entries(MechRow)) {
				Choice(record, fields, ObjectiveEditing.HercSubject, herc.Guid, Names.Object(new ObjectKey(MechRow, herc.Guid)));
			}

			Heading("Structures and ground vehicles");
			foreach (var structure in Layout.Entries(BaseRow)) {
				Choice(record, fields, ObjectiveEditing.StructureSubject, structure.Guid, Names.Object(new ObjectKey(BaseRow, structure.Guid)));
			}

			ImGui.EndCombo();
		}

		ImGui.EndDisabled();
		if (fields.SubjectKind == ObjectiveEditing.GroupSubject) {
			_links.LayoutGroup(fields.SubjectRef);
		} else if (MissionIndex.SubjectKey(fields.SubjectKind, fields.SubjectRef) is { } key) {
			_links.Object(key);
		} else {
			EditorText.Note($"An unknown kind of subject ({fields.SubjectKind}), which the mission has no test for: it answers as the "
				+ "objective before it does.");
		}
	}

	private static void Heading(string text) {
		ImGui.Separator();
		ImGui.TextDisabled(text);
	}

	private void Choice(int record, MissionObjective58 fields, short kind, short guid, RecordName name) {
		bool current = fields.SubjectKind == kind && fields.SubjectRef == guid;
		if (NameText.Selectable(name, $"{kind}.{guid}", current) && !current) {
			_editor.RequestSubject(record, kind, guid);
		}
	}

	// What happens to them, a choice of the conditions the mission tests, each with what it means.
	private void DrawWhat(int record, MissionObjective58 fields, bool locked) {
		bool group = fields.SubjectKind == ObjectiveEditing.GroupSubject;
		short code = fields.ConditionCode;
		Label("What happens");
		ImGui.BeginDisabled(locked);
		if (ImGui.BeginCombo("##what", MissionText.ObjectiveChoice(code, group))) {
			foreach (short choice in Offered) {
				bool current = MissionText.ObjectiveChoice(choice, group) == MissionText.ObjectiveChoice(code, group);
				if (ImGui.Selectable(MissionText.ObjectiveChoice(choice, group), current) && !current) {
					_editor.RequestCondition(record, choice);
				}

				if (ImGui.IsItemHovered()) {
					ImGui.SetTooltip(ObjectiveNote(choice, group));
				}
			}

			ImGui.EndCombo();
		}

		ImGui.EndDisabled();
		EditorText.Note(ObjectiveNote(code, group));
	}

	// What a condition means in play (docs/retail/simulation/mission-objectives.md#what-each-condition-asks).
	private static string ObjectiveNote(short code, bool group) =>
		MissionText.ObjectiveNote(code, group, MissionText.Metres(ClearRange), MissionText.Metres(ClearTargetRange));

	// Mission_IsClearOfThreats (004137b4): how near an enemy aware of the subject must be, and how near when the subject is its
	// selected target, in world units.
	private const int ClearRange = 80000;
	private const int ClearTargetRange = 100000;

	// A finished order's route, from the routes the subject's group's orders name, and which order that reads.
	private void DrawRoute(int record, MissionObjective58 fields, bool locked) {
		var choices = _editor.RouteChoices(record);
		Label("Route");
		ImGui.BeginDisabled(locked || choices.Count == 0);
		var current = fields.RouteRef != -1 ? Names.Route(fields.RouteRef) : new RecordName("None");
		if (NameText.BeginCombo("##route", current)) {
			foreach (short route in choices) {
				if (NameText.Selectable(Names.Route(route), $"route{route}", route == fields.RouteRef) && route != fields.RouteRef) {
					_editor.RequestRoute(record, route);
				}
			}

			ImGui.EndCombo();
		}

		ImGui.EndDisabled();
		if (fields.RouteRef != -1 && _index.Route(fields.RouteRef) != null) {
			_links.LayoutRoute(fields.RouteRef);
		}

		short group = _index.SubjectGroup(fields);
		if (group == -1) {
			EditorText.Note("No group places it, so this never comes true.");
			return;
		}

		int slot = _index.SlotOnRoute(group, fields.RouteRef);
		ImGui.AlignTextToFramePadding();
		ImGui.TextUnformatted("Asked of");
		ImGui.SameLine();
		_links.ShortLayoutGroup(group);
		EditorText.Note(slot >= 0
			? $"It reads the group's {MissionText.Ordinal(slot + 1)} order, the first on this route."
			: choices.Count == 0 ? "None of the group's orders has a route, so this never comes true."
			: "None of the group's orders is on this route, so this never comes true.");
	}

	// The status alert text's three lines, each a field that applies when it lets go; shown only for a win condition.
	private void DrawText(int record, bool mandatory, bool locked) {
		ImGui.Separator();
		ImGui.TextUnformatted("Status alert text:");
		if (mandatory) {
			EditorText.Note("Shown in the status alert when the player presses Q, while this is the first win condition in the list "
				+ "not yet met, no lose condition holds and no enemy nearby is aware of the player; up to its first empty line.");
		} else {
			EditorText.Note("Never shown for a lose condition.");
		}

		var lines = _editor.FailureLines(record);
		if (lines.Count == 0) {
			string? why = locked ? null : _editor.WhyNoGiveText(record);
			ImGui.BeginDisabled(locked || !mandatory || why != null);
			if (ImGui.Button("Add status alert text")) {
				_editor.RequestGiveText(record);
			}

			ImGui.EndDisabled();
			EditorText.Reason(why);
			return;
		}

		ImGui.BeginDisabled(locked || !mandatory);
		for (int line = 0; line < lines.Count; line++) {
			if (lines[line] is not { } text) {
				EditorText.Note($"Line {line + 1}: not in the mission's text.");
				continue;
			}

			if (_lines.Draw($"##alert{line}", $"{record}.{line}", text) is { } typed) {
				_editor.RequestText(record, line, typed);
			}
		}

		ImGui.EndDisabled();
	}

	// The campaign variables it sets the first time its condition holds, a win or a lose condition alike
	// (docs/retail/simulation/mission-objectives.md#the-record), each a sentence with its operation and variable to change it,
	// Remove, and Add after the last.
	private void DrawWrites(int record, MissionObjective58 fields, bool locked) {
		ImGui.Separator();
		var filled = fields.Pairs.TakeWhile(pair => pair.CounterRef != -1 || pair.Op != -1)
			.Select(pair => (pair.CounterRef, pair.Op)).ToList();
		switch (_writes.Draw("The first time it comes true, sets campaign variables", WriteLayer.Objective, filled, _editor.Source,
				locked, locked ? null : _editor.WhyNoAddPair(record))) {
			case SetWriteRequest set:
				_editor.RequestPair(record, set.Slot, set.Variable, set.Op);
				break;
			case AddWriteRequest:
				_editor.RequestAddPair(record);
				break;
			case RemoveWriteRequest remove:
				_editor.RequestRemovePair(record, remove.Slot);
				break;
		}
	}

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

	// A field's label, with the choice after it filling the rest of the line, the choices lined up past the widest label.
	private static void Label(string text) {
		ImGui.AlignTextToFramePadding();
		ImGui.TextUnformatted(text);
		ImGui.SameLine(MathF.Max(ScaledImGui.Scaled(90f), ImGui.CalcTextSize("What happens").X + ImGui.GetStyle().ItemSpacing.X));
		ImGui.SetNextItemWidth(-1f);
	}
}
