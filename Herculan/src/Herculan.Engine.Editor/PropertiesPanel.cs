using System.Numerics;
using HercWorks.Core.Data.File;
using HercWorks.Core.Data.File.Msn.Script;
using Herculan.Engine.Cockpit;
using Herculan.Engine.Numerics;
using Herculan.Engine.Platform;
using Herculan.Engine.Render;
using Herculan.Engine.Scene;
using Herculan.Engine.World;
using ImGuiNET;

namespace Herculan.Engine.Editor;

/// <summary>
/// The Properties panel down the right of the window: one view per kind of
/// <see cref="EditorSelection"/>. Every record a view mentions is a link that selects it, so the
/// panel is also how a mission's wiring is walked — area to action, action to the groups it brings
/// in, group to its route.
/// </summary>
internal sealed class PropertiesPanel {
	// In pixels on a 100% display (ScaledImGui.Scaled).
	public const float PanelWidth = 340f;

	private readonly MissionIndex _index;
	private readonly SelectionState _selection;
	private readonly PointMover _mover;
	private readonly SideChanger _sides;
	private readonly ObjectDeleter _deleter;
	private readonly SquadMachinePanel _machine;
	private readonly MissionView _mission;
	private readonly RouteView _route;
	private readonly TriggerAreaView _area;
	private readonly OrderSlotsView _orders;
	private readonly ObjectiveView _objective;
	private readonly ObjectiveEditor _objectives;
	private readonly ActionView _action;
	private readonly ActionEditor _actions;
	private readonly TimerView _timer;
	private readonly ConditionView _condition;
	private readonly PresenceEditor _presence;
	private readonly PresentWhenView _presentWhen;
	private readonly PanelLinks _links;
	private readonly StringFile? _strings;

	// An object's or a group's point being typed.
	private readonly PointFields _point = new();

	/// <param name="document">The generated <c>script.dat</c>, whose records the order view offers as routes and subjects.</param>
	public PropertiesPanel(MissionIndex index, ScriptDat document, SelectionState selection, PointMover mover,
			SideChanger sides, ObjectDeleter deleter, SquadMachinePanel machine, MissionView mission, RouteEditor routes,
			RoutePlacing placing, AreaEditor areas, AreaPlacing areaPlacing, OrderEditor orders, ObjectiveEditor objectives,
			ActionEditor actions, TimerEditor timers, ConditionEditor conditions, PresenceEditor presence, StringFile? strings) {
		_index = index;
		_selection = selection;
		_mover = mover;
		_sides = sides;
		_deleter = deleter;
		_machine = machine;
		_mission = mission;
		_links = new PanelLinks(index, selection);
		_actions = actions;
		_action = new ActionView(index, document, actions, selection, _links);
		_timer = new TimerView(index, document, timers, _links);
		_condition = new ConditionView(conditions, _links);
		_presence = presence;
		_presentWhen = new PresentWhenView(presence, _links, (names, ownRoute) => References(names, ownRoute));
		_route = new RouteView(index, selection, mover, routes, placing, _links.Group,
			(names, ownRoute) => References(names, ownRoute));
		_area = new TriggerAreaView(index, mover, areas, areaPlacing, actions, _action.DrawSummary, names => References(names));
		_orders = new OrderSlotsView(index, document, orders, _links.Route, _links.Action, _links.Subject, _presentWhen, presence);
		_objectives = objectives;
		_objective = new ObjectiveView(index, document, objectives, _links.Group, _links.Slot, _links.Route, _presentWhen, presence);
		machine.References = names => References(names);
		_strings = strings;
	}

	private Mission Mission => _index.Mission;

	public void Draw(Vector2 display, float menuBarHeight) {
		float width = ScaledImGui.Scaled(PanelWidth);

		ImGui.SetNextWindowPos(new Vector2(display.X - width, menuBarHeight));
		ImGui.SetNextWindowSize(new Vector2(width, display.Y - menuBarHeight));
		ImGui.Begin("Properties", ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoCollapse);

		switch (_selection.Current) {
			case MissionSelection:
				_mission.Draw();
				break;
			case ObjectSelection selected:
				DrawObject(selected.Object);
				break;
			case GroupSelection group:
				DrawGroup(group.Group);
				break;
			case AreaSelection area:
				_area.Draw(area.Area);
				break;
			case ActionSelection action:
				_action.Draw(action.Action);
				break;
			case RouteSelection route:
				_route.Draw(route);
				break;
			case ObjectiveSelection objective:
				_objective.Draw(objective.Objective);
				break;
			case TimerSelection timer:
				_timer.Draw(timer.Timer);
				break;
			case ConditionSelection condition:
				_condition.Draw(condition.Guid);
				break;
			default:
				ImGui.TextWrapped("Nothing selected. Click a mech, flyer, building, trigger area or waypoint "
					+ "in the scene, or pick from the Mission list.");
				break;
		}

		ImGui.End();
	}

	private void DrawObject(SceneObject sel) {
		var placement = sel.Placement;
		ImGui.TextWrapped(Title(sel));
		ImGui.AlignTextToFramePadding();
		ImGui.Text("Group:");
		ImGui.SameLine();
		_links.Group(placement.GroupIndex);
		_presentWhen.Draw(_presence.ObjectTarget(sel));
		ImGui.Separator();
		ImGui.Text($"Kind: {placement.Kind}");
		ImGui.Text($"Type index: {placement.TypeIndex}");
		ImGui.Text(placement.IsPlayerLance ? "Player squad" : $"Roster slot: {placement.SlotIndex}");
		TeamField(placement.GroupIndex, ofMember: true);

		if (sel.Model is { } model) {
			ImGui.Separator();
			ImGui.Text($"Model: {model.Key}");
			ImGui.Text($"Triangles: {model.TriangleVertexCount / 3}");
			ImGui.Text(model.Atlas is { } atlas
				? $"Texture: {atlas.FrameCount} frames ({atlas.Width}x{atlas.Height})"
				: "Texture: none");
		}

		ImGui.Separator();
		DrawPosition(sel);
		ImGui.Text($"Heading: {MissionText.Heading(sel.Object.Heading)}");
		ImGui.Text($"Hit radius: {sel.Object.HitRadius} units");

		if (!placement.IsPlayerLance) {
			ImGui.Text($"Starting condition: {placement.StartingCondition}%");
		} else if (ReferenceEquals(placement, Mission.Player)) {
			_machine.DrawPlayer();
		} else {
			_machine.DrawSquadmateObject(placement.SlotIndex);
		}

		if (_index.IsWaiting(sel)) {
			ImGui.TextWrapped("Its group is waiting on an action: nothing of it is drawn or ticked until "
				+ "that fires, and this position is a placeholder unless the group arrives in place.");
		}

		DrawOwnActions(placement);
		if (placement.OutOfActionReport is { } report) {
			OutOfActionWrites("When out of the fight", report);
		}

		ObjectiveLinks(_index.ObjectivesOfObject(sel), new ObjectSelection(sel));
		DeleteControls(sel);
	}

	/// <summary>
	/// A Delete button (<see cref="ObjectDeleter"/>), or why the object cannot be deleted, with what references or needs it
	/// as links. The delete itself waits for the frame's ImGui to be rendered (<see cref="ObjectDeleter.Request"/>).
	/// </summary>
	private void DeleteControls(SceneObject sel) {
		ImGui.Separator();
		var blockers = _deleter.BlockersOf(sel);
		if (blockers.None) {
			if (ImGui.Button("Delete")) {
				_deleter.Request(sel);
			}

			foreach (var (group, _) in blockers.RemovedGroups.Where(removed => removed.Group >= 0)) {
				ImGui.PushID($"goes{group}");
				ImGui.AlignTextToFramePadding();
				ImGui.Text("Deleted with it, as it is the last member:");
				_links.Group(group);
				ImGui.PopID();
			}

			return;
		}

		if (blockers.Fixed is { } reason) {
			EditorText.Reason(reason, "Cannot be deleted: ");
			return;
		}

		ImGui.PushID("delete");
		ImGui.TextWrapped("Cannot be deleted while these reference or need it:");
		References(blockers.Object, fired: blockers.OwnActions);
		foreach (var (group, names) in blockers.RemovedGroups.Where(removed => removed.Names.Any)) {
			ImGui.PushID($"last{group}");
			ImGui.Spacing();
			if (group >= 0) {
				ImGui.TextWrapped("It is the last member of its group, which would go with it. These reference the group:");
			} else {
				ImGui.TextWrapped("It is the last member of a group this state of the mission leaves out, which would go "
					+ "with it. These reference the group:");
			}

			References(names);
			ImGui.PopID();
		}

		ImGui.PopID();
	}

	/// <summary>
	/// What references something, one short line per record: an objective or an action by its link alone, and a group by
	/// its link and what of it references, its orders (<paramref name="names"/>) and its own route field
	/// (<paramref name="ownRoute"/>, block-11 indices) on its one line. <paramref name="fired"/> are the actions it fires
	/// when engaged or defeated, by block-5 index, -1 for one this state of the mission leaves out.
	/// </summary>
	private void References(DeleteNamers names, IReadOnlyList<int>? ownRoute = null, IReadOnlyList<int>? fired = null) {
		foreach (int objective in names.Objectives) {
			ImGui.PushID($"objective{objective}");
			_links.Link($"Objective {objective}", new ObjectiveSelection(objective));
			ImGui.PopID();
		}

		foreach (int action in names.Actions) {
			ImGui.PushID($"target{action}");
			_links.Action(action);
			ImGui.PopID();
		}

		foreach (int action in fired ?? []) {
			ImGui.PushID($"fired{action}");
			if (action >= 0) {
				_links.Action(action);
				ImGui.SameLine(0f, 0f);
				ImGui.Text(": fired by it");
			} else {
				ImGui.TextWrapped("An action it fires, which this state of the mission leaves out");
			}

			ImGui.PopID();
		}

		var groups = names.Orders.Select(order => order.Group).Concat(ownRoute ?? []).Distinct().ToList();
		foreach (int group in groups) {
			var parts = names.Orders.Where(order => order.Group == group).Select(order => $"order {order.Slot}").ToList();
			if (ownRoute?.Contains(group) == true) {
				parts.Add("own route");
			}

			ImGui.PushID($"group{group}");
			_links.ShortGroup(group);
			ImGui.SameLine(0f, 0f);
			ImGui.Text($": {string.Join(", ", parts)}");
			ImGui.PopID();
		}

		if (names.Elsewhere > 0) {
			ImGui.TextWrapped($"{names.Elsewhere} more, which this state of the mission leaves out");
		}
	}

	/// <summary>
	/// The object's position, as fields that move it when it can be moved (<see cref="PointMover"/>). A field's edit
	/// is applied when it loses the focus, as one undo step.
	/// </summary>
	private void DrawPosition(SceneObject sel) {
		var pos = sel.Object.Position;
		if (_mover.WhyFixed(sel) is { } reason) {
			ImGui.Text($"Position: {pos.X}, {pos.Y}, {pos.Z} units");
			ImGui.Text($"          {Meters(pos)}");
			EditorText.Reason(reason);
			return;
		}

		bool ownHeight = PointMover.HasOwnHeight(sel);
		ImGui.Text("Position, in world units:");
		ImGui.PushID("position");
		var typed = _point.Draw(sel, pos, ownHeight);
		ImGui.PopID();
		if (!ownHeight) {
			EditorText.Note($"Z: {pos.Z}, from the ground under it");
		}

		EditorText.Note(Meters(pos));
		if (typed is { } draft) {
			_mover.Commit(new ObjectTarget(sel), pos, draft.X, draft.Y, draft.Z);
		}

		PointFields.BoxWarning(_mover.LoadedBox, sel.Object.Position);

		var placement = sel.Placement;
		if (placement.Kind is MissionUnitKind.Mech or MissionUnitKind.Flyer
				&& placement.GroupIndex >= 0 && placement.GroupIndex < _index.GroupMembers.Count
				&& _index.GroupMembers[placement.GroupIndex] is { Count: > 1 } members && !ReferenceEquals(members[0], sel)) {
			ImGui.TextWrapped("Once the mission runs it holds formation on its group's leader, so it can move off a "
				+ "point given here.");
		}
	}

	/// <summary>
	/// The group's team (block 11's side), as a choice that changes it when it can be changed (<see cref="SideChanger"/>),
	/// as one undo step. The team is the group's, so a member's view shows it fixed, and the group's view says when other
	/// members change with it. The player's squad's is shown fixed, with no note.
	/// </summary>
	private void TeamField(int group, bool ofMember) {
		var current = _index.SideOf(group);
		string? reason = _sides.WhyFixed(group);
		if (current is not { } team) {
			ImGui.Text("Team: ?");
			return;
		}

		ImGui.AlignTextToFramePadding();
		ImGui.Text("Team:");
		ImGui.BeginDisabled(ofMember || reason != null);
		foreach (var option in Enum.GetValues<MissionSide>()) {
			ImGui.SameLine();
			if (ImGui.RadioButton(option + "##team", team == option)) {
				_sides.Set(group, option);
			}
		}

		ImGui.EndDisabled();
		int members = group >= 0 && group < _index.GroupMembers.Count ? _index.GroupMembers[group].Count : 0;
		if (ofMember) {
			EditorText.Note("Team controlled by group");
		} else if (reason != null && group != 0) {
			EditorText.Reason(reason);
		} else if (reason == null && members > 1) {
			EditorText.Note($"All {members} members change with it.");
		}
	}

	// The object's name: a mech's or flyer's type name, or for a structure or ground vehicle its type and the name the
	// MFD's status screen prints for it (MfdStatusSubject.BaseName).
	private string Title(SceneObject sel) {
		var placement = sel.Placement;
		if (sel.Object is Sim.BaseObject structure) {
			string kind = structure.Type.IsVehicle ? "Ground vehicle" : "Base";
			string? name = MfdStatusSubject.BaseName(structure.Type, _strings);
			return $"{kind} type {placement.TypeIndex}: {(string.IsNullOrWhiteSpace(name) ? "(no name)" : name)}";
		}

		return placement.TypeName ?? $"{placement.Kind} type {placement.TypeIndex}";
	}

	private static string Meters(Vec3i pos) =>
		$"{pos.X / WorldScale.WorldUnitsPerMeter:F1}, {pos.Y / WorldScale.WorldUnitsPerMeter:F1}, "
			+ $"{pos.Z / WorldScale.WorldUnitsPerMeter:F1} m";

	private void DrawGroup(int group) {
		ImGui.TextWrapped(MissionText.DescribeGroup(_index, group));
		_presentWhen.Draw(_presence.GroupTarget(group));
		TeamField(group, ofMember: false);
		if (group == 0) {
			_machine.DrawSquad();
		}

		ImGui.Separator();
		DrawDeployment(group);

		if (_index.SetupOf(group) is { } setup) {
			ImGui.Separator();
			DrawSetup(group, setup);
		}

		ImGui.Separator();
		var members = group < _index.GroupMembers.Count ? _index.GroupMembers[group] : Array.Empty<SceneObject>();
		ImGui.Text($"Members: {members.Count}");
		foreach (var member in members) {
			_links.Object(member);
		}

		if (group < Mission.GroupOutOfActionReports.Count) {
			OutOfActionWrites("When all of it is out of the fight", Mission.GroupOutOfActionReports[group]);
		}

		ObjectiveLinks(_index.ObjectivesOfGroup(group), new GroupSelection(group));
		_orders.Draw(group);
	}

	/// <summary>
	/// The action the group waits on before it is in the mission, a choice of none or any action
	/// (<see cref="ActionEditor.RequestDeployment"/>), with a link and how the group arrives. The player's squad has none: no
	/// retail squad record carries one (<see cref="HercWorks.Core.Data.File.Msn.ActionWiring"/>).
	/// </summary>
	private void DrawDeployment(int group) {
		int waitsOn = _index.DeploymentActionOf(group);
		if (group == 0 && waitsOn < 0) {
			ImGui.Text("In the mission from the start.");
			return;
		}

		string? why = _actions.WhyNoDeployment(group);
		ImGui.AlignTextToFramePadding();
		ImGui.Text("Waits on:");
		ImGui.SameLine();
		ImGui.SetNextItemWidth(-1f);
		ImGui.BeginDisabled(why != null);
		if (ActionChoice.Draw("##deployment", waitsOn, Mission.Actions.Count) is { } chosen) {
			_actions.RequestDeployment(group, chosen);
		}

		ImGui.EndDisabled();
		if (waitsOn >= 0) {
			_links.Action(waitsOn);
			ImGui.TextWrapped($"Not in the mission until that action fires; then it arrives {MissionText.Arrival(Mission.Actions[waitsOn].Verb)}.");
		} else {
			ImGui.Text("In the mission from the start.");
		}

		EditorText.Reason(why);
		EditorText.Reason(_actions.Failure);
	}

	/// <summary>
	/// The actions a roster object fires when engaged and when defeated, each a choice of none or any action
	/// (<see cref="ActionEditor.RequestObjectAction"/>), with a link. Not for a machine of the player's squad, which is built
	/// from <c>player.mec</c>: no retail squad record carries either.
	/// </summary>
	private void DrawOwnActions(MissionPlacement placement) {
		if (placement.IsPlayerLance) {
			return;
		}

		ImGui.Separator();
		string? why = _actions.WhyNoObjectAction(placement);
		OwnAction("When engaged, fires", placement, defeated: false, placement.EngagementActionRef, why);
		OwnAction("When defeated, fires", placement, defeated: true, placement.DefeatActionRef, why);
		EditorText.Note("Engaged: an enemy within 50,000 units spots it, or shoots it. Defeated: put out of the fight, "
			+ "or for a herc, left with no working weapon.");
		EditorText.Reason(why);
		EditorText.Reason(_actions.Failure);
	}

	private void OwnAction(string label, MissionPlacement placement, bool defeated, int current, string? why) {
		ImGui.PushID(label);
		ImGui.AlignTextToFramePadding();
		ImGui.Text($"{label}:");
		string? noNone = _actions.WhyNoNone(placement, defeated);
		ImGui.SameLine();
		ImGui.SetNextItemWidth(-1f);
		ImGui.BeginDisabled(why != null);
		if (ActionChoice.Draw("##own", current, Mission.Actions.Count, offerNone: noNone == null, whyNoNone: noNone) is { } chosen) {
			_actions.RequestObjectAction(placement, defeated, chosen);
		}

		ImGui.EndDisabled();
		if (current >= 0) {
			_links.Action(current);
		}

		ImGui.PopID();
	}

	/// <summary>The group's own point, heading and formation.</summary>
	private void DrawSetup(int group, MissionGroupSetup setup) {
		string source = setup.PointSource switch {
			MissionGroupPointSource.OwnPoint => "its own point",
			MissionGroupPointSource.RouteStart => "its route's first waypoint",
			_ => "no point and no route: the origin"
		};

		// The authored point, which a move writes; a flyer group's Z is where its first member flies (PointMover.PositionOf).
		var target = new GroupTarget(group);
		var at = _mover.PositionOf(target);
		ImGui.Text("Point, in world units:");
		ImGui.PushID("point");
		var typed = _point.Draw(target, at, _mover.HasOwnHeight(target), enabled: _mover.WhyFixed(group) == null);
		ImGui.PopID();
		EditorText.Note($"from {source}");
		if (setup.AuthoredPoint != setup.Point) {
			ImGui.TextWrapped($"It stands at {setup.Point.X}, {setup.Point.Y}: painting its ground moves it to a fixed "
				+ "spot in its terrain tile.");
		}

		if (typed is { } draft) {
			_mover.Commit(target, at, draft.X, draft.Y, draft.Z);
		}

		if (_mover.StandsOnPoint(group)) {
			PointFields.BoxWarning(_mover.LoadedBox, _mover.PositionOf(target));
		}

		ImGui.Text($"Heading: {MissionText.Heading(setup.Heading)}");
		if (setup.HeadingFromRoute) {
			EditorText.Note("No heading of its own: it faces along its route's first leg.");
		}

		string kind = _index.KindOf(group)?.ToString().ToLowerInvariant() ?? "?";
		ImGui.Text($"Formation: {setup.FormationId} ({kind} table)");
		ImGui.Text(setup.PaintsGround ? "Paints ground: yes" : "Paints ground: no");

		foreach (var pad in _index.BasePads) {
			if (pad.GroupIndex != group) {
				continue;
			}

			var (x, y) = pad.TileOrigin;
			int size = pad.Layout.TileSize;
			ImGui.TextWrapped($"Pad: tile {x} to {x + size}, {y} to {y + size} ({MissionText.Distance(size)} square), "
				+ $"material {pad.Layout.MaterialIndex}. Only which tile the authored point falls in matters; "
				+ "a move within the tile changes nothing.");
		}

		ImGui.Separator();
		DrawGroupMove(group, setup);
	}

	/// <summary>What dragging the group's move handles moves with it (<see cref="PointMover"/>), or why it cannot be moved.</summary>
	private void DrawGroupMove(int group, MissionGroupSetup setup) {
		if (_mover.WhyFixed(group) is { } reason) {
			EditorText.Reason(reason);
			return;
		}

		if (_mover.TargetOf(new GroupSelection(group)) is not GroupTarget target) {
			return;
		}

		int sharers = _mover.SharersOf(target);
		string others = $"{sharers} other record(s) (trigger areas, groups, waypoints) referencing its point";
		if (setup.PointSource == MissionGroupPointSource.OwnPoint) {
			ImGui.TextWrapped(sharers > 0
				? $"Its point is referenced by {sharers} other record(s) (trigger areas, groups, waypoints), which move with it. Alt+drag moves the group alone."
				: "Nothing else references its point.");
		} else if (_mover.StartOf(group) != null) {
			ImGui.TextWrapped("Dragging moves its route's first waypoint, the start of the route"
				+ (sharers > 0 ? $", and the {others} with it" : "")
				+ ". Alt+drag gives the group a point of its own.");
		} else {
			ImGui.TextWrapped("Dragging gives it a point of its own.");
		}

		int waitsOn = _index.DeploymentActionOf(group);
		if (waitsOn >= 0 && Mission.Actions[waitsOn].Verb is MissionAction.VerbPodWide or MissionAction.VerbPodNarrow
				or MissionAction.VerbWalkBehind or MissionAction.VerbWalkAhead) {
			ImGui.TextWrapped($"It arrives {MissionText.ArrivalKind(Mission.Actions[waitsOn].Verb)}, placed about the "
				+ "player when the action fires: its point is a placeholder, and moving it does not move where it arrives.");
		}
	}

	/// <summary>An out-of-action report's counter writes, or nothing when it makes none.</summary>
	private static void OutOfActionWrites(string heading, OutOfActionReport report) {
		bool any = false;
		for (int i = 0; i < report.CounterRefs.Count && i < report.CounterOps.Count; i++) {
			if (report.CounterRefs[i] < 0) {
				continue;
			}

			if (!any) {
				ImGui.Separator();
				ImGui.Text($"{heading}:");
				any = true;
			}

			ImGui.BulletText($"counter {report.CounterRefs[i]}: {MissionText.OutOfActionOp(report.CounterOps[i])}");
		}
	}

	/// <summary>
	/// Links to the objectives asked of the selection, and a button that adds one about <paramref name="subject"/>, the
	/// selection itself (<see cref="ObjectiveEditor"/>).
	/// </summary>
	private void ObjectiveLinks(IEnumerable<int> objectives, EditorSelection subject) {
		ImGui.Separator();
		ImGui.Text("Related objectives:");
		ImGui.PushID("objectives");
		bool any = false;
		foreach (int objective in objectives) {
			_links.Objective(objective);
			any = true;
		}

		if (!any) {
			EditorText.Note("None.");
		}

		string? why = _objectives.WhyNoCreate(subject);
		ImGui.BeginDisabled(why != null);
		if (ImGui.Button("Add objective")) {
			_objectives.RequestCreate(subject);
		}

		ImGui.EndDisabled();
		EditorText.Reason(why);
		EditorText.Reason(_objectives.Failure);
		ImGui.PopID();
	}
}
