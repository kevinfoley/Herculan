using HercWorks.Core.Data.File.Msn;
using HercWorks.Core.Data.File.Msn.Script;
using Herculan.Engine.Platform;
using Herculan.Engine.World;
using ImGuiNET;

namespace Herculan.Engine.Editor;

/// <summary>
/// The group view's orders: each filled slot of the ten with its verb, route, subject and the action that ends it as choices
/// that change them (<see cref="OrderEditor"/>), each with a link, and buttons that add an order after the last, remove
/// one or move one up or down. What each field does is docs/retail/simulation/ai-goals.md's.
/// </summary>
internal sealed class OrderSlotsView {
	private const short NoSubject = -1;
	private const short GroupSubject = 0;
	private const short HercSubject = 1;
	private const short StructureSubject = 3;

	private readonly MissionIndex _index;
	private readonly ScriptDat _document;
	private readonly OrderEditor _editor;
	private readonly Action<int> _routeLink;
	private readonly Action<int> _actionLink;
	private readonly Action<MissionOrderSubject, int> _subjectLink;

	/// <param name="routeLink">Draws a link that selects a route, by block-3 index.</param>
	/// <param name="actionLink">Draws a link that selects an action, by block-5 index.</param>
	/// <param name="subjectLink">Draws a link that selects an order's subject.</param>
	public OrderSlotsView(MissionIndex index, ScriptDat document, OrderEditor editor, Action<int> routeLink,
			Action<int> actionLink, Action<MissionOrderSubject, int> subjectLink) {
		_index = index;
		_document = document;
		_editor = editor;
		_routeLink = routeLink;
		_actionLink = actionLink;
		_subjectLink = subjectLink;
	}

	public void Draw(int group) {
		ImGui.Separator();
		ImGui.Text("Orders:");
		string? fixedReason = _editor.WhyFixed(group);
		var slots = _editor.Slots(group);
		if (slots.Count == 0) {
			EditorText.Note("None.");
		}

		foreach (var slot in slots) {
			ImGui.PushID($"order{slot.Slot}");
			DrawSlot(group, slot, slots[^1].Slot, locked: fixedReason != null);
			ImGui.PopID();
		}

		ImGui.Spacing();
		if (Button("Add an order", _editor.WhyNoAdd(group))) {
			_editor.RequestAdd(group);
		}

		if (slots.Count > 1) {
			EditorText.Note("The group works through its orders in turn, and stays on the last.");
		}

		EditorText.Reason(_editor.Failure);
	}

	// One slot; <locked> draws its choices disabled, its links still working.
	private void DrawSlot(int group, EditableOrder slot, int last, bool locked) {
		var order = slot.Order;
		ImGui.Spacing();
		ImGui.AlignTextToFramePadding();
		ImGui.Text($"Order {slot.Slot}");
		ImGui.SameLine();
		ImGui.BeginDisabled(slot.Slot == 0 || locked);
		if (SmallButton("Up", slot.Slot == 0 ? null : _editor.WhyNoMove(group, slot.Slot, slot.Slot - 1))) {
			_editor.RequestMove(group, slot.Slot, slot.Slot - 1);
		}

		ImGui.EndDisabled();
		ImGui.SameLine();
		ImGui.BeginDisabled(slot.Slot == last || locked);
		if (SmallButton("Down", slot.Slot == last ? null : _editor.WhyNoMove(group, slot.Slot, slot.Slot + 1))) {
			_editor.RequestMove(group, slot.Slot, slot.Slot + 1);
		}

		ImGui.EndDisabled();
		ImGui.SameLine();
		string? whyKept = _editor.WhyNoRemove(group, slot.Slot);
		if (SmallButton("Remove", whyKept)) {
			_editor.RequestRemove(group, slot.Slot);
		}

		ImGui.Indent();
		EditorText.Reason(whyKept);
		if (slot.Shared) {
			EditorText.Note("Another group has this order too: a change here gives this group its own copy.");
		}

		ImGui.BeginDisabled(locked);
		DrawVerb(group, slot.Slot, order.Verb, order.SubjectKind == NoSubject);
		DrawRoute(group, slot.Slot, order.RouteRef);
		DrawSubject(group, slot.Slot, order.Verb, order.SubjectKind, order.SubjectRef);
		ImGui.EndDisabled();
		if (order.RouteRef >= 0) {
			_routeLink(order.RouteRef);
		}

		if (order.SubjectKind != NoSubject && order.SubjectRef >= 0) {
			_subjectLink((MissionOrderSubject)order.SubjectKind, order.SubjectRef);
		}

		DrawEnding(group, slot.Slot, order.ActionRef, locked);
		ImGui.Unindent();
	}

	// The action that moves the group on from this order when it fires, none or any action, with a link.
	private void DrawEnding(int group, int slot, short action, bool locked) {
		Label("Ends when");
		ImGui.BeginDisabled(locked);
		if (ActionChoice.Draw("##ends", action, _index.Mission.Actions.Count) is { } chosen) {
			_editor.RequestAction(group, slot, chosen);
		}

		ImGui.EndDisabled();
		if (action >= 0) {
			_actionLink(action);
			EditorText.Note("When it fires, the group moves on to its next order.");
		}
	}

	private void DrawVerb(int group, int slot, short verb, bool noSubject) {
		Label("Verb");
		if (ImGui.BeginCombo("##verb", MissionText.OrderVerb(verb))) {
			for (short choice = 0; choice <= GroupOrders.LastVerb; choice++) {
				if (ImGui.Selectable(MissionText.OrderVerb(choice), choice == verb) && choice != verb) {
					_editor.RequestVerb(group, slot, choice);
				}
			}

			ImGui.EndCombo();
		}

		if (noSubject) {
			EditorText.Note("Search/destroy, ramming and following need a subject: choosing one gives the order the player's squad.");
		}
	}

	private void DrawRoute(int group, int slot, short route) {
		Label("Route");
		string? whyRouteNeeded = _editor.WhyNoRouteNone(group, slot);
		if (ImGui.BeginCombo("##route", route >= 0 ? $"Route {route}" : "None")) {
			var flags = whyRouteNeeded != null ? ImGuiSelectableFlags.Disabled : ImGuiSelectableFlags.None;
			if (ImGui.Selectable("None", route < 0, flags) && route >= 0) {
				_editor.RequestRoute(group, slot, -1);
			}

			foreach (var choice in _index.Routes) {
				if (ImGui.Selectable($"Route {choice.Reference}", choice.Reference == route) && choice.Reference != route) {
					_editor.RequestRoute(group, slot, choice.Reference);
				}
			}

			ImGui.EndCombo();
		}

		if (slot > 0) {
			EditorText.Note("Never walked: a group walks only its first order's route.");
		} else if (group == 0) {
			EditorText.Note("The player's route.");
		} else if (whyRouteNeeded != null) {
			EditorText.Reason(whyRouteNeeded);
		}
	}

	private void DrawSubject(int group, int slot, short verb, short kind, short subject) {
		Label("Subject");
		if (!ImGui.BeginCombo("##subject", SubjectName(kind, subject))) {
			return;
		}

		// No None for a verb that needs a subject (GroupOrders.NeedsSubject).
		if (!GroupOrders.NeedsSubject(verb) && ImGui.Selectable("None", kind == NoSubject) && kind != NoSubject) {
			_editor.RequestSubject(group, slot, NoSubject, -1);
		}

		Choices(group, slot, kind, subject, GroupSubject, _document.Groups.Length);
		Choices(group, slot, kind, subject, HercSubject, _document.Mechs.Length);
		Choices(group, slot, kind, subject, StructureSubject, _document.Bases.Length);
		ImGui.EndCombo();
	}

	// The records of subject kind <choiceKind> the combo offers, under a heading.
	private void Choices(int group, int slot, short kind, short subject, short choiceKind, int count) {
		if (count == 0) {
			return;
		}

		ImGui.Separator();
		ImGui.TextDisabled(choiceKind switch { GroupSubject => "Groups", HercSubject => "Hercs", _ => "Structures" });
		for (int i = 0; i < count; i++) {
			bool current = kind == choiceKind && subject == i;
			if (ImGui.Selectable($"{SubjectName(choiceKind, (short)i)}##{choiceKind}.{i}", current) && !current) {
				_editor.RequestSubject(group, slot, choiceKind, i);
			}
		}
	}

	private string SubjectName(short kind, short subject) => MissionText.SubjectName(_index, kind, subject);

	// A field's label, with the choice after it filling the rest of the line.
	private static void Label(string text) {
		ImGui.AlignTextToFramePadding();
		ImGui.Text(text);
		ImGui.SameLine(ScaledImGui.Scaled(90f));
		ImGui.SetNextItemWidth(-1f);
	}

	// A button, disabled with <reason> on hover when there is one.
	private static bool Button(string label, string? reason) {
		ImGui.BeginDisabled(reason != null);
		bool clicked = ImGui.Button(label);
		ImGui.EndDisabled();
		Tooltip(reason);
		return clicked;
	}

	private static bool SmallButton(string label, string? reason) {
		ImGui.BeginDisabled(reason != null);
		bool clicked = ImGui.SmallButton(label);
		ImGui.EndDisabled();
		Tooltip(reason);
		return clicked;
	}

	private static void Tooltip(string? reason) {
		if (reason != null && !EditorText.IsReadOnly(reason) && ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) {
			ImGui.SetTooltip(reason);
		}
	}
}
