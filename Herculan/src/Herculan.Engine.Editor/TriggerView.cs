using HercWorks.Core.Data.File.Msn;
using Herculan.Engine.Platform;
using Herculan.Engine.World;
using ImGuiNET;

namespace Herculan.Engine.Editor;

/// <summary>
/// The Properties panel's view of a trigger (a row-10 mission action), from the <c>.MSN</c>'s layout and
/// <see cref="MissionIndex"/> by GUID, read as "when this happens, do that": whose position its areas test, picked as one
/// choice with the herc, flyer, structure or group under it for the four kinds that test a particular one; the areas; what
/// else fires it (countdowns, objects engaged or defeated), as links; the Then list (<see cref="TriggerThenList"/>); Delete,
/// with what references it as identity links; and under Advanced the leftover text and the message's file and id. Every choice
/// is an edit (<see cref="ActionEditor"/>). <see cref="DrawSummary"/> is the same read-only, as a trigger area's view lists
/// each trigger testing it. What each part does in play is docs/retail/simulation/mission-deployment.md#the-four-ways-an-action-activates.
/// </summary>
internal sealed class TriggerView {
	private const int MissionGroupRow = 16;

	private readonly MissionIndex _index;
	private readonly ActionEditor _editor;
	private readonly SelectionState _selection;
	private readonly PanelLinks _links;
	private readonly TriggerThenList _then;

	public TriggerView(MissionIndex index, ActionEditor editor, SelectionState selection, PanelLinks links, TriggerMessages messages) {
		_index = index;
		_editor = editor;
		_selection = selection;
		_links = links;
		_then = new TriggerThenList(index, editor, links, messages);
	}

	private MissionDocument Source => _editor.Source;

	private MissionLayout Layout => _index.Layout;

	private MissionNames Names => _links.Names;

	/// <summary>Row-10 GUID <paramref name="guid"/>'s view; nothing for a GUID the layout has no trigger for.</summary>
	public void Draw(short guid) {
		if (Layout.Entry<MissionAction82>(guid)?.Fields is not { } fields) {
			return;
		}

		bool locked = _editor.WhyFixed(guid) != null;
		var name = Names.Trigger(guid);
		EditorText.Wrapped(name.Text);
		if (name.Number is { } number) {
			ImGui.TextDisabled(number);
		}

		ImGui.Separator();
		DrawWho(guid, fields, locked);
		DrawAreas(guid, locked);
		DrawFiredBy(guid);
		ImGui.Separator();
		_then.Draw(guid, fields, locked);
		DrawDelete(guid);
		EditorText.Reason(_editor.Failure);
		DrawAdvanced(guid, fields);
	}

	/// <summary>Row-10 GUID <paramref name="guid"/>'s wiring, read-only, every record it mentions a link: what fires it, and what follows.</summary>
	public void DrawSummary(short guid) {
		if (Layout.Entry<MissionAction82>(guid)?.Fields is not { } fields) {
			return;
		}

		ImGui.AlignTextToFramePadding();
		if (_selection.Is(new ActionSelection(guid))) {
			NameText.Draw(Names.Trigger(guid));
		} else {
			_links.Trigger(guid);
		}

		ImGui.Indent();
		EditorText.Wrapped($"Fires when {MissionText.TriggerSubject(fields.Type)} enters:");
		if (TargetRow(fields.Type) != null) {
			DrawTargetLink(guid, fields.Type);
		}

		foreach (short area in FilledAreas(guid)) {
			ImGui.PushID(area);
			_links.LayoutArea(area);
			ImGui.PopID();
		}

		DrawFiredBy(guid);
		_then.DrawSummary(guid, fields);
		ImGui.Unindent();
	}

	// "Fires when", whose position its areas test as one choice of the eleven kinds, and for the four that test a particular
	// herc, flyer, structure or group, which one, in a second choice under it.
	private void DrawWho(short guid, MissionAction82 fields, bool locked) {
		ImGui.Text("Fires when:");
		ImGui.SetNextItemWidth(-1f);
		ImGui.BeginDisabled(locked);
		if (ImGui.BeginCombo("##who", MissionText.TriggerWho(fields.Type))) {
			for (short type = 0; type <= ActionEditing.LastType; type++) {
				string? why = type == fields.Type ? null : _editor.WhyNoType(guid, type);
				var flags = why != null ? ImGuiSelectableFlags.Disabled : ImGuiSelectableFlags.None;
				if (ImGui.Selectable(MissionText.TriggerWho(type), type == fields.Type, flags) && type != fields.Type) {
					_editor.RequestType(guid, type);
				}

				if (why != null && ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) {
					ImGui.SetTooltip(why);
				}
			}

			ImGui.EndCombo();
		}

		ImGui.EndDisabled();
		DrawTarget(guid, fields.Type, locked);
		if (fields.Type is >= MissionAction.SubjectHumanGroups and <= MissionAction.SubjectBaseGroups) {
			EditorText.Note("A group still waiting to arrive is not tested.");
		}
	}

	// For the four kinds that test a particular record, which one, as a choice of the layout's records of its row, with a link.
	private void DrawTarget(short guid, short type, bool locked) {
		if (TargetRow(type) is not { } row) {
			return;
		}

		short target = ActionEditing.Target(Source, guid);
		var choices = Targets(row);
		var preview = target == -1 ? new RecordName("No target chosen") : choices.FirstOrDefault(choice => choice.Guid == target).Name;
		ImGui.SetNextItemWidth(-1f);
		ImGui.BeginDisabled(locked);
		if (NameText.BeginCombo("##target", preview)) {
			foreach (var (choice, name) in choices) {
				if (NameText.Selectable(name, $"target{choice}", choice == target) && choice != target) {
					_editor.RequestTarget(guid, choice);
				}
			}

			ImGui.EndCombo();
		}

		ImGui.EndDisabled();
		if (target == -1) {
			EditorText.Emphasis("No target chosen.");
		} else {
			DrawTargetLink(guid, type);
		}
	}

	private void DrawTargetLink(short guid, short type) {
		short target = ActionEditing.Target(Source, guid);
		if (target == -1 || TargetRow(type) is not { } row) {
			return;
		}

		if (row == MissionGroupRow) {
			_links.LayoutGroup(target);
		} else {
			_links.Object(new ObjectKey(row, target));
		}
	}

	// The row a target of <type> is a record of: 12 to 14 for types 7 to 9, 16 for type 10; null for the others.
	private static int? TargetRow(short type) => ActionEditing.TargetBlock(type) is { } block ? block + 5 : null;

	// Every record of <row> a target can be, by GUID in layout order, by its name.
	private IReadOnlyList<(short Guid, RecordName Name)> Targets(int row) {
		if (row == MissionGroupRow) {
			return _index.Groups.Select(group => (group.Guid, Names.Group(group.Guid))).ToList();
		}

		return Layout.Entries(row).Select(entry => (entry.Guid, Names.Object(new ObjectKey(row, entry.Guid)))).ToList();
	}

	// "enters one of these areas:", each a choice of any area of the mission with Remove and a link, and Add after the last.
	private void DrawAreas(short guid, bool locked) {
		ImGui.Text("enters one of these areas:");
		ImGui.Indent();
		var areas = FilledAreas(guid);
		for (int slot = 0; slot < areas.Count; slot++) {
			short area = areas[slot];
			ImGui.PushID($"area{slot}");
			ImGui.BeginDisabled(locked);
			ImGui.SetNextItemWidth(ScaledImGui.Scaled(170f));
			if (AreaCombo("##area", Names.Area(area)) is { } chosen && chosen != area) {
				_editor.RequestArea(guid, slot, chosen);
			}

			ImGui.SameLine();
			if (ImGui.SmallButton("Remove")) {
				_editor.RequestRemoveArea(guid, slot);
			}

			ImGui.EndDisabled();
			_links.LayoutArea(area);
			ImGui.PopID();
		}

		if (areas.Count == 0) {
			EditorText.Note("None: only a countdown or an object can fire it.");
		}

		string? why = _editor.WhyNoAddArea(guid);
		ImGui.BeginDisabled(locked || why != null);
		ImGui.SetNextItemWidth(ScaledImGui.Scaled(170f));
		if (AreaCombo("##addarea", new RecordName("Add an area")) is { } added) {
			_editor.RequestAddArea(guid, added);
		}

		ImGui.EndDisabled();
		EditorText.Reason(why);
		ImGui.Unindent();
	}

	// The areas it tests, up to the first empty slot or one naming no area, where the load stops.
	private IReadOnlyList<short> FilledAreas(short guid) {
		var areas = ActionEditing.Areas(Source, guid);
		return areas.Take(ActionEditing.FilledAreas(areas)).ToList();
	}

	// A combo of every area of the mission, in layout order; answers the row-9 GUID of the one chosen, or null.
	private short? AreaCombo(string id, RecordName preview) {
		short? chosen = null;
		if (NameText.BeginCombo(id, preview)) {
			foreach (var area in Layout.Entries<TriggerArea12>()) {
				if (NameText.Selectable(Names.Area(area.Guid), $"area{area.Guid}", false)) {
					chosen = area.Guid;
				}
			}

			ImGui.EndCombo();
		}

		return chosen;
	}

	// What else fires it, as links: the countdowns that fire it when they run out and the objects that fire it when engaged or
	// defeated; with no area either, an emphasized note that nothing does.
	private void DrawFiredBy(short guid) {
		var links = _index.Links(guid);
		if (!links.FiredOtherwise) {
			if (FilledAreas(guid).Count == 0) {
				EditorText.Emphasis("Nothing fires it. Add an area above, add it to a countdown's \"When it runs out, fires\", or pick "
					+ "it as an object's \"When engaged, fires\" or \"When defeated, fires\".");
			}

			return;
		}

		ImGui.Spacing();
		ImGui.Text("Also fired by:");
		ImGui.Indent();
		foreach (short countdown in links.FiringCountdowns) {
			ImGui.PushID($"firing{countdown}");
			_links.Countdown(countdown);
			ImGui.SameLine(0f, 0f);
			ImGui.TextUnformatted(", when it runs out");
			ImGui.PopID();
		}

		foreach (var (key, when) in links.Engaged.Select(key => (key, "engaged")).Concat(links.Defeated.Select(key => (key, "defeated")))) {
			ImGui.PushID($"{when}{key.Row}.{key.Guid}");
			_links.Object(key);
			ImGui.SameLine(0f, 0f);
			ImGui.TextUnformatted($", when {when}");
			ImGui.PopID();
		}

		ImGui.Unindent();
	}

	// A Delete button, or why the trigger cannot be deleted, with what references it as short identity links, one per line.
	private void DrawDelete(short guid) {
		ImGui.Separator();
		var blockers = _editor.BlockersOf(guid);
		if (blockers.None) {
			if (ImGui.Button("Delete")) {
				_editor.RequestDelete(guid);
			}

			return;
		}

		if (blockers.Fixed is { } reason) {
			EditorText.Reason(reason, "Cannot be deleted: ");
			return;
		}

		ImGui.PushID("delete");
		ImGui.TextWrapped("Cannot be deleted while these reference it:");
		foreach (short countdown in blockers.Countdowns) {
			ImGui.PushID($"countdown{countdown}");
			_links.Countdown(countdown);
			ImGui.PopID();
		}

		foreach (short group in blockers.Groups) {
			ImGui.PushID($"group{group}");
			_links.ShortLayoutGroup(group);
			ImGui.PopID();
		}

		foreach (var key in blockers.Objects) {
			ImGui.PushID($"object{key.Row}.{key.Guid}");
			_links.Object(key);
			ImGui.PopID();
		}

		foreach (var order in blockers.Orders) {
			ImGui.PushID($"order{order.Group}.{order.Slot}");
			var group = Names.GroupBrief(order.Group);
			_links.Link(group with { Text = $"{MissionText.Ordinal(order.Slot + 1)} order of {group.Text}" }, new GroupSelection(order.Group));
			ImGui.PopID();
		}

		if (blockers.Options > 0) {
			ImGui.TextWrapped(blockers.Options == 1 ? "1 option of a random choice" : $"{blockers.Options} options of random choices");
		}

		if (blockers.UnrunOrders > 0) {
			ImGui.TextWrapped(blockers.UnrunOrders == 1 ? "1 order no group runs" : $"{blockers.UnrunOrders} orders no group runs");
		}

		ImGui.PopID();
	}

	// Advanced, collapsed, shown only when there is something to show: each leftover text line, which the simulator reads and
	// drops, and the message's file and id.
	private void DrawAdvanced(short guid, MissionAction82 fields) {
		var leftover = fields.TextRefs.Where(id => id != -1).Distinct().ToList();
		short message = ActionEditing.Message(Source, guid);
		if ((leftover.Count == 0 && message < 0) || !ImGui.CollapsingHeader("Advanced")) {
			return;
		}

		foreach (short id in leftover) {
			EditorText.Wrapped($"Leftover text (no effect in play): {Layout.Line(id)?.Text ?? $"text {id}, not in the mission's text"}");
		}

		if (message >= 0) {
			EditorText.Wrapped($"Message: id {message} in str\\{TriggerMessages.FileName(TriggerMessages.TrainingNumber(Layout))}");
		}
	}
}
