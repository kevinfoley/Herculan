using HercWorks.Core.Data.File.Msn;
using Herculan.Engine.Platform;
using Herculan.Engine.World;
using ImGuiNET;

namespace Herculan.Engine.Editor;

/// <summary>
/// The group view's orders, from the <c>.MSN</c>'s layout by the group's GUID: each filled slot of the ten, numbered from 1st,
/// with what it does, its route, its target and the trigger that ends it as choices that change them
/// (<see cref="OrderEditor"/>), each with a link, its Present when, and buttons that add an order after the last, remove one
/// or move one up or down. A later order's route moves nothing (a group walks only its first order's route), so it is offered
/// only when an objective reads it. What each field does is docs/retail/simulation/ai-goals.md's.
/// </summary>
internal sealed class OrderSlotsView {
	private const short NoSubject = -1;
	private const short GroupSubject = 0;
	private const short HercSubject = 1;
	private const short StructureSubject = 3;
	private const int MechRow = 12;
	private const int BaseRow = 14;

	private readonly MissionIndex _index;
	private readonly OrderEditor _editor;
	private readonly PanelLinks _links;
	private readonly PresentWhenView _presentWhen;
	private readonly PresenceEditor _presence;

	/// <param name="presentWhen">Draws each slot's Present when (<paramref name="presence"/>'s target).</param>
	public OrderSlotsView(MissionIndex index, OrderEditor editor, PanelLinks links, PresentWhenView presentWhen,
			PresenceEditor presence) {
		_index = index;
		_editor = editor;
		_links = links;
		_presentWhen = presentWhen;
		_presence = presence;
	}

	private MissionNames Names => _links.Names;

	/// <summary>The orders of row-16 GUID <paramref name="guid"/>.</summary>
	public void Draw(short guid) {
		ImGui.Separator();
		ImGui.TextUnformatted("Orders:");
		bool locked = _editor.WhyFixed(guid) != null;
		var slots = _editor.Slots(guid);
		if (slots.Count == 0) {
			EditorText.Note("None.");
		}

		foreach (var slot in slots) {
			ImGui.PushID($"order{slot.Slot}");
			DrawSlot(guid, slot, slots[^1].Slot, locked);
			ImGui.PopID();
		}

		ImGui.Spacing();
		if (Button("Add an order", _editor.WhyNoAdd(guid))) {
			_editor.RequestAdd(guid);
		}

		if (slots.Count > 1) {
			EditorText.Note("The group works through its orders in turn, and stays on the last.");
		}

		EditorText.Reason(_editor.Failure);
	}

	// One slot of the group of row-16 GUID <guid>; <locked> draws its choices disabled, its links still working.
	private void DrawSlot(short guid, EditableOrder slot, int last, bool locked) {
		var order = slot.Order;
		ImGui.Spacing();
		ImGui.AlignTextToFramePadding();
		ImGui.TextUnformatted($"{MissionText.Ordinal(slot.Slot + 1)} order");
		ImGui.SameLine();
		ImGui.BeginDisabled(slot.Slot == 0 || locked);
		if (SmallButton("Up", slot.Slot == 0 ? null : _editor.WhyNoMove(guid, slot.Slot, slot.Slot - 1))) {
			_editor.RequestMove(guid, slot.Slot, slot.Slot - 1);
		}

		ImGui.EndDisabled();
		ImGui.SameLine();
		ImGui.BeginDisabled(slot.Slot == last || locked);
		if (SmallButton("Down", slot.Slot == last ? null : _editor.WhyNoMove(guid, slot.Slot, slot.Slot + 1))) {
			_editor.RequestMove(guid, slot.Slot, slot.Slot + 1);
		}

		ImGui.EndDisabled();
		ImGui.SameLine();
		string? whyKept = locked ? null : _editor.WhyNoRemove(guid, slot.Slot);
		ImGui.BeginDisabled(locked);
		if (SmallButton("Remove", whyKept)) {
			_editor.RequestRemove(guid, slot.Slot);
		}

		ImGui.EndDisabled();
		ImGui.Indent();
		DrawVerb(guid, slot.Slot, order.Verb, order.SubjectKind == NoSubject, locked);
		DrawRoute(guid, slot.Slot, order.RouteRef, locked);
		DrawTarget(guid, slot.Slot, order.Verb, order.SubjectKind, order.SubjectRef, locked);
		DrawEnding(guid, slot.Slot, order.Verb, order.ActionRef, slot.Slot != last, locked);
		_presentWhen.Draw(_presence.OrderTarget(guid, slot.Slot));
		ImGui.Unindent();
	}

	// What the order does, a choice of the seven verbs, each with what it does and when it is done on hover, the closed choice
	// and each entry alike, the closed one adding that a verb with no target gets one when chosen; for a convoy only when it is
	// done, under a note saying that is all the verb decides. A verb the group cannot take (OrderEditor.WhyNoVerb) is disabled,
	// saying why on hover.
	private void DrawVerb(short group, int slot, short verb, bool noSubject, bool locked) {
		bool convoy = _editor.IsConvoy(group);
		Func<short, string> note = convoy ? MissionText.ConvoyOrderVerbNote : MissionText.OrderVerbNote;
		Label("Does");
		ImGui.BeginDisabled(locked);
		bool open = ImGui.BeginCombo("##verb", MissionText.OrderVerb(verb));
		if (!open) {
			EditorText.Tooltip(noSubject
				? note(verb) + Environment.NewLine + Environment.NewLine + "Search and destroy, Ram and Follow need a target: choosing one gives the order the player's squad."
				: note(verb));
		}

		if (open) {
			for (short choice = 0; choice <= GroupOrders.LastVerb; choice++) {
				string? why = choice == verb ? null : _editor.WhyNoVerb(group, choice);
				var flags = why != null ? ImGuiSelectableFlags.Disabled : ImGuiSelectableFlags.None;
				if (ImGui.Selectable(MissionText.OrderVerb(choice), choice == verb, flags) && choice != verb) {
					_editor.RequestVerb(group, slot, choice);
				}

				if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) {
					ImGui.SetTooltip(why ?? note(choice));
				}
			}

			ImGui.EndCombo();
		}

		ImGui.EndDisabled();
		if (convoy) {
			EditorText.Note(MissionText.ConvoyOrderNote);
		}
	}

	// The order's route: the group's own on its first order; on a later one only a key an objective can read, so offered only
	// when one reads it (the editor's choice), with those objectives as links.
	private void DrawRoute(short group, int slot, short route, bool locked) {
		IReadOnlyList<LayoutObjective> readers = slot > 0 ? _index.ObjectivesReadingOrder(group, slot) : [];
		bool moot = slot > 0 && readers.Count == 0;
		Label("Route");
		string? whyRouteNeeded = locked ? null : _editor.WhyNoRouteNone(group, slot);
		ImGui.BeginDisabled(locked || moot);
		var current = route != -1 ? Names.Route(route) : new RecordName("None");
		if (NameText.BeginCombo("##route", current)) {
			var flags = whyRouteNeeded != null ? ImGuiSelectableFlags.Disabled : ImGuiSelectableFlags.None;
			if (ImGui.Selectable("None", route == -1, flags) && route != -1) {
				_editor.RequestRoute(group, slot, -1);
			}

			foreach (var entry in _index.Layout.Entries<WaypointGroup>()) {
				if (NameText.Selectable(Names.Route(entry.Guid), $"route{entry.Guid}", entry.Guid == route) && entry.Guid != route) {
					_editor.RequestRoute(group, slot, entry.Guid);
				}
			}

			ImGui.EndCombo();
		}

		ImGui.EndDisabled();
		if (route != -1 && _index.Route(route) != null) {
			_links.LayoutRoute(route);
		}

		if (slot > 0) {
			EditorText.Note("No effect on movement: a group only walks its first order's route.");
			foreach (var objective in readers) {
				ImGui.PushID($"reader{objective.Index}");
				ImGui.TextUnformatted("Read by");
				ImGui.SameLine();
				_links.Objective(objective.Index);
				ImGui.PopID();
			}
		} else if (_index.Group(group) is { Number: 0 }) {
			EditorText.Note("The player's route.");
		} else if (whyRouteNeeded != null) {
			EditorText.Reason(whyRouteNeeded);
		}
	}

	// The order's target, none for a verb that does not need one, else a group, a herc or a structure, picked by name; what it
	// does under the order's verb on hover (MissionText.OrderTargetNote).
	private void DrawTarget(short group, int slot, short verb, short kind, short subject, bool locked) {
		Label("Target");
		ImGui.BeginDisabled(locked);
		bool open = NameText.BeginCombo("##target", Names.Subject(kind, subject));
		if (!open && _index.Group(group) is { } ordered) {
			EditorText.Tooltip(MissionText.OrderTargetNote(verb, ordered.Kind == MissionUnitKind.Flyer, _editor.IsConvoy(group), ordered.IsSquad));
		}

		if (open) {
			// No None for a verb that needs a subject (GroupOrders.NeedsSubject).
			if (!GroupOrders.NeedsSubject(verb) && ImGui.Selectable("None", kind == NoSubject) && kind != NoSubject) {
				_editor.RequestSubject(group, slot, NoSubject, -1);
			}

			Heading("Groups");
			foreach (var choice in _index.Groups) {
				Choice(group, slot, kind, subject, GroupSubject, choice.Guid, Names.Group(choice.Guid));
			}

			Heading("Hercs");
			foreach (var herc in _index.Layout.Entries(MechRow)) {
				Choice(group, slot, kind, subject, HercSubject, herc.Guid, Names.Object(new ObjectKey(MechRow, herc.Guid)));
			}

			Heading("Structures and ground vehicles");
			foreach (var structure in _index.Layout.Entries(BaseRow)) {
				Choice(group, slot, kind, subject, StructureSubject, structure.Guid, Names.Object(new ObjectKey(BaseRow, structure.Guid)));
			}

			ImGui.EndCombo();
		}

		ImGui.EndDisabled();
		if (kind == GroupSubject) {
			_links.LayoutGroup(subject);
		} else if (MissionIndex.SubjectKey(kind, subject) is { } key) {
			_links.Object(key);
		}

		// The mission check's OrderTargetsOwnSide, said where it is changed.
		if (verb == HercGroups.SearchDestroy && _index.Group(group) is { IsSquad: false } own && SideOf(kind, subject) == own.Side) {
			EditorText.Problem("Its target is on its own team, which it never attacks, and it goes after no other enemy: choose an enemy, "
				+ "or \"Patrol\" under \"Does\".");
		}
	}

	// The team of an order's subject of <kind>: a group's, or the group's placing a herc or structure; null for none.
	private MissionSide? SideOf(short kind, short subject) =>
		kind == GroupSubject ? _index.Group(subject)?.Side
			: MissionIndex.SubjectKey(kind, subject) is { } key ? _index.Plan.Object(key)?.Side
			: null;

	private static void Heading(string text) {
		ImGui.Separator();
		ImGui.TextDisabled(text);
	}

	private void Choice(short group, int slot, short kind, short subject, short choiceKind, short guid, RecordName name) {
		bool current = kind == choiceKind && subject == guid;
		if (NameText.Selectable(name, $"{choiceKind}.{guid}", current) && !current) {
			_editor.RequestSubject(group, slot, choiceKind, guid);
		}
	}

	// The trigger that moves the group on from this order when it fires, none or any trigger, with a link; <hasNext> says a
	// later order waits on it.
	private void DrawEnding(short group, int slot, short verb, short action, bool hasNext, bool locked) {
		Label("Ends when");
		ImGui.BeginDisabled(locked);
		if (ActionChoice.Draw("##ends", action, _index, Names) is { } chosen) {
			_editor.RequestAction(group, slot, chosen);
		}

		ImGui.EndDisabled();
		bool set = action != -1 && _index.Action(action) != null;
		if (set) {
			_links.Trigger(action);
			EditorText.Note("When that trigger fires, the group moves on to its next order.");
		} else if (hasNext && verb is MissionOrder.VerbRam or MissionOrder.VerbSleep) {
			EditorText.Note("Ram and Sleep never end by themselves: with no trigger here, the group never reaches its next order.");
		}
	}

	// A field's label, with the choice after it filling the rest of the line, the choices lined up past the widest label.
	private static void Label(string text) {
		ImGui.AlignTextToFramePadding();
		ImGui.TextUnformatted(text);
		ImGui.SameLine(MathF.Max(ScaledImGui.Scaled(80f), ImGui.CalcTextSize("Ends when").X + ImGui.GetStyle().ItemSpacing.X));
		ImGui.SetNextItemWidth(-1f);
	}

	// A button, disabled with <reason> on hover when there is one.
	private static bool Button(string label, string? reason) {
		ImGui.BeginDisabled(reason != null);
		bool clicked = ImGui.Button(label);
		ImGui.EndDisabled();
		EditorText.Tooltip(reason);
		return clicked;
	}

	private static bool SmallButton(string label, string? reason) {
		ImGui.BeginDisabled(reason != null);
		bool clicked = ImGui.SmallButton(label);
		ImGui.EndDisabled();
		EditorText.Tooltip(reason);
		return clicked;
	}
}
