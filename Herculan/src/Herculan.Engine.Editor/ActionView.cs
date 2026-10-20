using HercWorks.Core.Data.File.Msn;
using HercWorks.Core.Data.File.Msn.Script;
using Herculan.Engine.Content;
using Herculan.Engine.Platform;
using Herculan.Engine.World;
using ImGuiNET;

namespace Herculan.Engine.Editor;

/// <summary>
/// The Properties panel's view of a mission action: whose position its trigger areas test, its target, its areas, its
/// verb, its counter writes and its message, each a choice or field that changes it (<see cref="ActionEditor"/>), what fires
/// it and what follows as links, and a Delete button. <see cref="DrawSummary"/> is the same wiring read-only, as a trigger
/// area's view lists each action testing it. What each field does is
/// docs/retail/simulation/mission-deployment.md#the-four-ways-an-action-activates.
/// </summary>
internal sealed class ActionView {
	private readonly MissionIndex _index;
	private readonly ScriptDat _document;
	private readonly ActionEditor _editor;
	private readonly SelectionState _selection;
	private readonly PanelLinks _links;

	// A counter being typed, and the action and pair it is for, until its field lets go.
	private (int Action, int Slot)? _counterFor;
	private int _counter;

	/// <param name="document">The generated <c>script.dat</c>, whose records the view offers as targets.</param>
	public ActionView(MissionIndex index, ScriptDat document, ActionEditor editor, SelectionState selection, PanelLinks links) {
		_index = index;
		_document = document;
		_editor = editor;
		_selection = selection;
		_links = links;
	}

	private Mission Mission => _index.Mission;

	private GeneratedRecords Records => _index.Records;

	/// <summary>Row-10 GUID <paramref name="guid"/>'s view, the action generated at block-5 index <paramref name="action"/>.</summary>
	public void Draw(short guid, int action) {
		if (action < 0 || action >= Mission.Actions.Count) {
			return;
		}

		var record = Mission.Actions[action];
		var links = _index.LinksOf(action);
		bool locked = _editor.WhyFixed(guid) != null;

		ImGui.Text($"Action {action}");
		ImGui.Separator();
		ImGui.Text("Fires when:");
		DrawType(guid, record, locked);
		DrawTarget(guid, record, locked);
		DrawAreas(guid, record, locked);
		DrawFiredBy(record, links, editing: true);

		ImGui.Separator();
		ImGui.Text("Then:");
		DrawVerb(guid, record, locked);
		DrawFollowers(record, links);
		DrawCounters(guid, action, record, locked);
		DrawMessage(guid, record, locked);
		DrawUnusedText(action);
		DrawDelete(guid);
		EditorText.Reason(_editor.Failure);
	}

	/// <summary>The action's wiring, read-only, every record it mentions a link: what fires it, and what follows.</summary>
	public void DrawSummary(int action) {
		if (action < 0 || action >= Mission.Actions.Count) {
			return;
		}

		var record = Mission.Actions[action];
		var links = _index.LinksOf(action);
		ImGui.AlignTextToFramePadding();
		if (_selection.Is(Records.ActionAt(action))) {
			ImGui.Text($"Action {action}");
		} else {
			_links.Action(action);
		}

		ImGui.Spacing();
		ImGui.Text("Fires when:");
		ImGui.Indent();
		if (record.Areas.Count > 0) {
			ImGui.TextWrapped($"{MissionText.TriggerSubject(record.Type)} enters");
			foreach (var area in record.Areas) {
				_links.Area(area.Reference);
			}
		}

		DrawTargetLink(record);
		DrawFiredBy(record, links, editing: false);
		ImGui.Unindent();

		ImGui.Spacing();
		ImGui.Text("Then:");
		ImGui.Indent();
		bool any = DrawFollowers(record, links);
		for (int i = 0; i < record.CounterRefs.Count && i < record.CounterOps.Count; i++) {
			if (record.CounterRefs[i] >= 0) {
				ImGui.Text($"counter {record.CounterRefs[i]}: {MissionText.ActionCounterOp(record.CounterOps[i])}");
				any = true;
			}
		}

		if (record.MessageId >= 0) {
			ImGui.TextWrapped($"message {record.MessageId}: {MessageText(record.MessageId) ?? "(no text)"}");
			int posts = Posts(record);
			EditorText.Note(posts == 0 ? "never posted: the action writes no counter" : $"posted {posts}x");
			any = true;
		}

		if (!any) {
			EditorText.Note("nothing waits on it");
		}

		ImGui.Unindent();
	}

	// Whose position the areas test, as a choice of every type DBSIM has a case for.
	private void DrawType(short action, MissionAction record, bool locked) {
		Label("Type");
		ImGui.BeginDisabled(locked);
		if (ImGui.BeginCombo("##type", TypeName(record.Type))) {
			for (short type = 0; type <= ActionEditing.LastType; type++) {
				string? why = type == record.Type ? null : _editor.WhyNoType(action, type);
				var flags = why != null ? ImGuiSelectableFlags.Disabled : ImGuiSelectableFlags.None;
				if (ImGui.Selectable(TypeName(type), type == record.Type, flags) && type != record.Type) {
					_editor.RequestType(action, type);
				}

				if (why != null && ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) {
					ImGui.SetTooltip(why);
				}
			}

			ImGui.EndCombo();
		}

		ImGui.EndDisabled();
		ImGui.TextWrapped($"Its areas test {MissionText.TriggerSubject(record.Type)}.");
		if (record.Type is >= MissionAction.SubjectHumanGroups and <= MissionAction.SubjectBaseGroups) {
			EditorText.Note("A group waiting on an action is not tested.");
		}
	}

	private static string TypeName(short type) => $"{type}: {MissionText.TriggerSubject(type)}";

	// For types 7 to 10, the herc, flyer, structure or group whose position the areas test; for the others, a note.
	private void DrawTarget(short action, MissionAction record, bool locked) {
		if (ActionEditing.TargetBlock(record.Type) is null) {
			EditorText.Note("Its target is not used for this type.");
			return;
		}

		(short kind, int count) = record.Type switch {
			MissionAction.SubjectTargetMech => ((short)1, _document.Mechs.Length),
			MissionAction.SubjectTargetFlyer => ((short)2, _document.Flyers.Length),
			MissionAction.SubjectTargetBase => ((short)3, _document.Bases.Length),
			_ => ((short)0, _document.Groups.Length),
		};

		Label("Target");
		ImGui.BeginDisabled(locked);
		if (ImGui.BeginCombo("##target", MissionText.SubjectName(_index, kind, record.TargetRef))) {
			for (int i = 0; i < count; i++) {
				bool current = record.TargetRef == i;
				if (ImGui.Selectable($"{MissionText.SubjectName(_index, kind, i)}##target{i}", current) && !current) {
					_editor.RequestTarget(action, Records.SubjectGuid(kind, i));
				}
			}

			ImGui.EndCombo();
		}

		ImGui.EndDisabled();
		DrawTargetLink(record);
	}

	private void DrawTargetLink(MissionAction record) {
		if (record.Type < MissionAction.SubjectTargetMech || record.TargetRef < 0) {
			return;
		}

		if (record.Type == MissionAction.SubjectTargetGroup) {
			_links.Group(record.TargetRef);
			return;
		}

		_links.Slot(record.Type switch {
			MissionAction.SubjectTargetMech => MissionUnitKind.Mech,
			MissionAction.SubjectTargetFlyer => MissionUnitKind.Flyer,
			_ => MissionUnitKind.Base
		}, record.TargetRef);
	}

	// The areas it tests, each a choice of any area of the mission with Remove and a link, and Add after the last.
	private void DrawAreas(short action, MissionAction record, bool locked) {
		ImGui.Spacing();
		ImGui.Text("Trigger areas:");
		ImGui.Indent();
		for (int slot = 0; slot < record.Areas.Count; slot++) {
			int area = record.Areas[slot].Reference;
			ImGui.PushID($"area{slot}");
			ImGui.BeginDisabled(locked);
			ImGui.SetNextItemWidth(ScaledImGui.Scaled(170f));
			if (AreaCombo("##area", AreaName(area)) is { } chosen && chosen != area) {
				_editor.RequestArea(action, slot, Records.AreaGuid(chosen));
			}

			ImGui.SameLine();
			if (ImGui.SmallButton("Remove")) {
				_editor.RequestRemoveArea(action, slot);
			}

			ImGui.EndDisabled();
			_links.Area(area);
			ImGui.PopID();
		}

		if (record.Areas.Count == 0) {
			EditorText.Note("None: only a timer or an object can fire it.");
		}

		string? why = _editor.WhyNoAddArea(action);
		ImGui.BeginDisabled(why != null);
		ImGui.SetNextItemWidth(ScaledImGui.Scaled(170f));
		if (AreaCombo("##addarea", "Add an area") is { } added) {
			_editor.RequestAddArea(action, Records.AreaGuid(added));
		}

		ImGui.EndDisabled();
		EditorText.Reason(why);
		ImGui.Unindent();
	}

	// A combo of every area of the mission, by block-4 index; answers the one chosen, or null.
	private int? AreaCombo(string id, string preview) {
		int? chosen = null;
		if (ImGui.BeginCombo(id, preview)) {
			foreach (var area in _index.Areas) {
				if (ImGui.Selectable($"{AreaName(area.Reference)}##{area.Reference}")) {
					chosen = area.Reference;
				}
			}

			ImGui.EndCombo();
		}

		return chosen;
	}

	private string AreaName(int area) {
		var shape = _index.Area(area)?.Shape;
		return shape == MissionTriggerShape.Circle ? $"Area {area} (circle)" : shape == null ? $"Area {area}" : $"Area {area} (box)";
	}

	// The timers and objects that fire it, as links; and, in the summary, a note when nothing does.
	private void DrawFiredBy(MissionAction record, ActionLinks links, bool editing) {
		bool shown = false;
		foreach (int timer in links.TimersFiringIt) {
			ImGui.PushID($"firing{timer}");
			_links.Timer(timer);
			ImGui.TextWrapped("runs out");
			ImGui.PopID();
			shown = true;
		}

		foreach (var placed in links.EngagedObjects) {
			ImGui.Text("engaged:");
			ImGui.SameLine();
			_links.Object(placed);
			shown = true;
		}

		foreach (var placed in links.DefeatedObjects) {
			ImGui.Text("defeated:");
			ImGui.SameLine();
			_links.Object(placed);
			shown = true;
		}

		if (!shown && record.Areas.Count == 0) {
			EditorText.Note(editing
				? "Nothing in this mission fires it: give it an area here, or a timer or an object its action."
				: "nothing in this mission fires it");
		}
	}

	// How the groups waiting on it arrive, as a choice of the six verbs; a value no verb has shows as it is.
	private void DrawVerb(short action, MissionAction record, bool locked) {
		Label("Verb");
		ImGui.BeginDisabled(locked);
		if (ImGui.BeginCombo("##verb", VerbName(record.Verb))) {
			for (short verb = 0; verb <= ActionEditing.LastVerb; verb++) {
				if (ImGui.Selectable(VerbName(verb), verb == record.Verb) && verb != record.Verb) {
					_editor.RequestVerb(action, verb);
				}
			}

			ImGui.EndCombo();
		}

		ImGui.EndDisabled();
		EditorText.Note("How a group waiting on it arrives: placed about the player when it fires, unless in place.");
	}

	private static string VerbName(short verb) =>
		verb is >= 0 and <= ActionEditing.LastVerb ? $"{verb}: {MissionText.Arrival(verb)}" : $"other ({verb}): {MissionText.Arrival(verb)}";

	// The groups waiting on it, the orders it ends and the timers it starts, as links; whether there was any.
	private bool DrawFollowers(MissionAction record, ActionLinks links) {
		bool any = false;
		foreach (int group in links.WaitingGroups) {
			ImGui.PushID($"waiting{group}");
			_links.Group(group);
			ImGui.TextWrapped($"arrives {MissionText.Arrival(record.Verb)}");
			ImGui.PopID();
			any = true;
		}

		foreach (var ends in links.OrdersItEnds) {
			ImGui.PushID($"order{ends.Group}.{ends.Slot}");
			_links.Group(ends.Group);
			ImGui.TextWrapped($"moves on from order {ends.Slot} ({MissionText.OrderVerb(ends.Order.Verb)})");
			ImGui.PopID();
			any = true;
		}

		foreach (int timer in links.TimersItStarts) {
			ImGui.PushID($"starts{timer}");
			_links.Timer(timer);
			ImGui.TextWrapped("starts, and when it runs out fires");
			ImGui.Indent();
			bool fires = false;
			foreach (short fired in Mission.ActionTimers[timer].SequenceRefs) {
				if (fired >= 0 && fired < Mission.Actions.Count) {
					ImGui.PushID(fired);
					_links.Action(fired);
					ImGui.PopID();
					fires = true;
				}
			}

			if (!fires) {
				EditorText.Note("no action");
			}

			ImGui.Unindent();
			ImGui.PopID();
			any = true;
		}

		return any;
	}

	// The counter writes, each a counter field and an operation, with Remove, and Add after the last.
	private void DrawCounters(short guid, int action, MissionAction record, bool locked) {
		ImGui.Spacing();
		ImGui.Text("Counter writes:");
		int filled = ActionEditing.FilledPairs(Enumerable.Range(0, Math.Min(record.CounterRefs.Count, record.CounterOps.Count))
			.Select(slot => (record.CounterRefs[slot], record.CounterOps[slot])).ToList());
		for (int slot = 0; slot < filled; slot++) {
			ImGui.PushID($"pair{slot}");
			DrawPair(guid, action, slot, record.CounterRefs[slot], record.CounterOps[slot], locked);
			ImGui.PopID();
		}

		if (filled == 0) {
			EditorText.Note("It writes no counter.");
		}

		string? why = _editor.WhyNoAddPair(guid);
		ImGui.BeginDisabled(why != null);
		if (ImGui.Button("Add a counter write")) {
			_editor.RequestAddPair(guid);
		}

		ImGui.EndDisabled();
		EditorText.Reason(why);
	}

	// Counter write <slot> of the action of row-10 GUID <guid>, generated at block-5 index <action>.
	private void DrawPair(short guid, int action, int slot, short counter, short op, bool locked) {
		ImGui.BeginDisabled(locked);
		ImGui.AlignTextToFramePadding();
		ImGui.Text("Counter");
		ImGui.SameLine();
		int shown = _counterFor == (action, slot) ? _counter : counter;
		ImGui.SetNextItemWidth(ScaledImGui.Scaled(60f));
		if (ImGui.InputInt("##counter", ref shown, 0, 0)) {
			_counterFor = (action, slot);
			_counter = shown;
		}

		if (ImGui.IsItemDeactivatedAfterEdit() && _counterFor == (action, slot)) {
			_counterFor = null;
			if (_counter != counter) {
				_editor.RequestPair(guid, slot, (short)Math.Clamp(_counter, 0, ActionEditing.Counters - 1), Op(op));
			}
		}

		ImGui.SameLine();
		ImGui.SetNextItemWidth(ScaledImGui.Scaled(90f));
		if (ImGui.BeginCombo("##op", MissionText.ActionCounterOp(op))) {
			foreach (short choice in new[] { ActionEditing.CounterClear, ActionEditing.CounterIncrement }) {
				if (ImGui.Selectable(MissionText.ActionCounterOp(choice), choice == op) && choice != op) {
					_editor.RequestPair(guid, slot, (short)Math.Clamp((int)counter, 0, ActionEditing.Counters - 1), choice);
				}
			}

			ImGui.EndCombo();
		}

		ImGui.SameLine();
		if (ImGui.SmallButton("Remove")) {
			_editor.RequestRemovePair(guid, slot);
		}

		ImGui.EndDisabled();
		if (counter < 0) {
			EditorText.Note("Skipped: a write needs a counter.");
		}
	}

	// An operation an edit keeps: the record's, or +1 for one Action_Activate has no case for.
	private static short Op(short op) => op is ActionEditing.CounterClear or ActionEditing.CounterIncrement ? op : ActionEditing.CounterIncrement;

	// The message, a choice of none or a line of the COMMAND<n>.STR the mission speaks from, and when it is posted.
	private void DrawMessage(short action, MissionAction record, bool locked) {
		ImGui.Spacing();
		int training = Mission.Header.TrainingMissionNumber;
		string file = SquadMessages.CommandResourceName(training);
		var messages = _index.CommandMessages;
		Label("Message");
		ImGui.BeginDisabled(locked || messages == null);
		if (ImGui.BeginCombo("##message", record.MessageId >= 0 ? $"Line {record.MessageId}" : "None")) {
			if (ImGui.Selectable("None", record.MessageId < 0) && record.MessageId >= 0) {
				_editor.RequestMessage(action, -1);
			}

			foreach (int id in messages?.Ids ?? []) {
				string text = MessageText(id) ?? "(no text)";
				string label = text.Length > 40 ? text[..40] + "..." : text;
				if (ImGui.Selectable($"Line {id}: {label}##message{id}", id == record.MessageId) && id != record.MessageId) {
					_editor.RequestMessage(action, (short)id);
				}
			}

			ImGui.EndCombo();
		}

		ImGui.EndDisabled();
		if (record.MessageId >= 0) {
			ImGui.TextWrapped(MessageText(record.MessageId) ?? $"Line {record.MessageId} is not in {file}.");
		}

		EditorText.Note(messages == null
			? $"The install has no str\\{file}."
			: training == 0
				? $"From str\\{file}, as every mission but training missions 1 to 4. The pilot and squad channel shows it, signed HQ."
				: $"From str\\{file}, training mission {training}'s instructor script.");

		int posts = Posts(record);
		const string rule = "It is posted once for each counter write, and not at all when the action writes none.";
		if (record.MessageId >= 0 && posts == 0) {
			EditorText.Emphasis($"Never posted: it writes no counter. {rule}");
		} else if (record.MessageId >= 0) {
			EditorText.Note($"Posted {posts} time(s). {rule}");
		} else {
			EditorText.Note(rule);
		}
	}

	// Action_Activate (00423430) queues the message once per pair naming a counter.
	private static int Posts(MissionAction record) => record.CounterRefs.Count(counter => counter >= 0);

	// The text of message <id> in the mission's COMMAND<n>.STR, an instruction's sentences joined; null when it has none.
	private string? MessageText(int id) {
		var entries = _index.CommandMessages?.Instruction(id);
		return entries is { Count: > 0 } ? string.Join(" ", entries.Select(entry => entry.Text)) : null;
	}

	// The 0x44 text ref, which 1 of the 338 retail records sets and DBSIM reads and drops, shown as it is.
	private void DrawUnusedText(int action) {
		short line = action < _document.Actions.Length ? _document.Actions[action].TextRefs[0] : (short)-1;
		if (line < 0) {
			return;
		}

		ImGui.Spacing();
		ImGui.TextWrapped($"Text line {line}: {Mission.TextAt(line)}");
		EditorText.Note("Unused: the simulator reads it and drops it.");
	}

	// A Delete button, or why the action cannot be deleted, with what references it as short links, one per line.
	private void DrawDelete(short action) {
		ImGui.Separator();
		var blockers = _editor.BlockersOf(action);
		if (blockers.None) {
			if (ImGui.Button("Delete")) {
				_editor.RequestDelete(action);
			}

			return;
		}

		if (blockers.Fixed is { } reason) {
			EditorText.Reason(reason, "Cannot be deleted: ");
			return;
		}

		ImGui.PushID("delete");
		ImGui.TextWrapped("Cannot be deleted while these reference it:");
		foreach (int timer in blockers.Timers) {
			ImGui.PushID($"timer{timer}");
			_links.ShortTimer(timer);
			ImGui.PopID();
		}

		foreach (int group in blockers.Groups) {
			ImGui.PushID($"group{group}");
			_links.ShortGroup(group);
			ImGui.PopID();
		}

		foreach (var (kind, slot) in blockers.Objects) {
			ImGui.PushID($"object{kind}{slot}");
			if (_index.ObjectAt(kind, slot) is { } placed) {
				_links.Link(ShortObject(placed), new ObjectSelection(placed));
			} else {
				EditorText.Note($"{KindName(kind)} slot {slot} (not placed)");
			}

			ImGui.PopID();
		}

		foreach (var order in blockers.Orders) {
			ImGui.PushID($"order{order.Group}.{order.Slot}");
			_links.Link($"{PanelLinks.ShortGroupName(order.Group)}, order {order.Slot}", Records.GroupAt(order.Group));
			ImGui.PopID();
		}

		if (blockers.Elsewhere > 0) {
			ImGui.TextWrapped($"{blockers.Elsewhere} more, not shown in the editor");
		}

		ImGui.PopID();
	}

	// An object by its identity alone: its kind and roster slot.
	private static string ShortObject(Scene.SceneObject placed) =>
		placed.Object is Sim.BaseObject { Type.IsVehicle: true }
			? $"Ground vehicle slot {placed.Placement.SlotIndex}"
			: $"{KindName(placed.Placement.Kind)} slot {placed.Placement.SlotIndex}";

	private static string KindName(MissionUnitKind kind) => kind switch {
		MissionUnitKind.Mech => "Herc",
		MissionUnitKind.Flyer => "Flyer",
		_ => "Structure"
	};

	// A field's label, with the choice after it filling the rest of the line.
	private static void Label(string text) {
		ImGui.AlignTextToFramePadding();
		ImGui.Text(text);
		ImGui.SameLine(ScaledImGui.Scaled(90f));
		ImGui.SetNextItemWidth(-1f);
	}
}
