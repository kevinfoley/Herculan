using System.Numerics;
using HercWorks.Core.Data.File;
using HercWorks.Core.Data.File.Msn;
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
///
/// <para>The trigger, countdown and objective views and a group's orders (<see cref="TriggerView"/>,
/// <see cref="CountdownView"/>, <see cref="ObjectiveView"/>, <see cref="OrderSlotsView"/>) read the <c>.MSN</c>'s layout by
/// GUID or row-17 index, every record included, and so do a group's Waits on and an object's own two triggers. The others
/// read the generated <c>script.dat</c>, so each is drawn for the generated record the selection's <c>.MSN</c> record stands
/// for (<see cref="GeneratedRecords"/>); a record the generation has none for shows its kind and that it is not shown in the
/// editor, or for an object or a group its title, its Present when, which can bring it back, and that it is absent from the
/// mission as opened, a group's orders after it. Every record is named by <see cref="MissionNames"/>.</para>
/// </summary>
internal sealed class PropertiesPanel {
	// In pixels on a 100% display (ScaledImGui.Scaled).
	public const float PanelWidth = 340f;

	private readonly GeneratedIndex _index;
	private readonly SelectionState _selection;
	private readonly PointMover _mover;
	private readonly SideChanger _sides;
	private readonly ObjectDeleter _deleter;
	private readonly SquadMachinePanel _machine;
	private readonly RosterFitView _fit;
	private readonly MissionView _mission;
	private readonly RouteView _route;
	private readonly TriggerAreaView _area;
	private readonly OrderSlotsView _orders;
	private readonly ObjectiveView _objective;
	private readonly ObjectiveEditor _objectives;
	private readonly TriggerView _trigger;
	private readonly ActionEditor _actions;
	private readonly CountdownView _countdown;
	private readonly ConditionView _condition;
	private readonly PresenceEditor _presence;
	private readonly PresentWhenView _presentWhen;
	private readonly VariantView _variants;
	private readonly PanelLinks _links;
	private readonly StringFile? _strings;

	// An object's or a group's point being typed.
	private readonly PointFields _point = new();

	public PropertiesPanel(GeneratedIndex index, SelectionState selection, PointMover mover,
			SideChanger sides, ObjectDeleter deleter, SquadMachinePanel machine, RosterFitView fit, MissionView mission, RouteEditor routes,
			RoutePlacing placing, AreaEditor areas, AreaPlacing areaPlacing, OrderEditor orders, ObjectiveEditor objectives,
			ActionEditor actions, TimerEditor timers, ConditionEditor conditions, PresenceEditor presence, VariantEditor variants,
			RandomChoices choices, TriggerMessages messages, MissionNames names, StringFile? strings) {
		_index = index;
		_selection = selection;
		_mover = mover;
		_sides = sides;
		_deleter = deleter;
		_machine = machine;
		_fit = fit;
		_mission = mission;
		_links = new PanelLinks(index.Records, index.Scene, names, selection);
		_actions = actions;
		_trigger = new TriggerView(index.Scene, actions, selection, _links, messages);
		_countdown = new CountdownView(index.Scene, timers, _links);
		_variants = new VariantView(variants, choices, _links);
		_condition = new ConditionView(conditions, _links, _variants);
		_presence = presence;
		_presentWhen = new PresentWhenView(presence, _links, (namers, ownRoute) => References(namers, ownRoute));
		_route = new RouteView(index, names, selection, mover, routes, placing, _links.Group,
			(namers, ownRoute) => References(namers, ownRoute), _variants.DrawRouteChoice);
		_area = new TriggerAreaView(index, names, mover, areas, areaPlacing, actions, _trigger.DrawSummary, namers => References(namers));
		_orders = new OrderSlotsView(index.Scene, orders, _links, _presentWhen, presence);
		_objectives = objectives;
		_objective = new ObjectiveView(index.Scene, objectives, _links, _presentWhen, presence);
		machine.References = namers => References(namers);
		_strings = strings;
	}

	private Mission Mission => _index.Mission;

	private GeneratedRecords Records => _index.Records;

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
				if (_index.Scene.ObjectOf(selected) is { } placed) {
					DrawObject(placed);
				}

				break;
			case GroupSelection { Guid: var guid }:
				if (Records.Group(guid) is { } group) {
					DrawGroup(guid, group);
				} else if (_index.Scene.Group(guid) != null) {
					Absent(_links.Names.Group(guid).Full, _presence.GroupTarget(guid));
					_orders.Draw(guid);
				} else {
					NotShown("Group");
				}

				break;
			case AreaSelection { Guid: var guid }:
				if (Records.Area(guid) is { } area) {
					_area.Draw(guid, area);
				} else {
					NotShown("Trigger area");
				}

				break;
			case ActionSelection { Guid: var guid }:
				if (_index.Scene.Action(guid) != null) {
					_trigger.Draw(guid);
				} else {
					NotShown("Trigger");
				}

				break;
			case RouteSelection route:
				if (Records.Route(route.Guid) is { } reference) {
					_route.Draw(route, reference);
				} else {
					NotShown("Route");
				}

				break;
			case ObjectiveSelection { Record: var record }:
				if (_objectives.WhyFixed(record) == null) {
					_objective.Draw(record);
				} else {
					NotShown("Win or lose condition");
				}

				break;
			case TimerSelection { Guid: var guid }:
				if (_index.Scene.Layout.Entry<ActionTimer30>(guid) != null) {
					_countdown.Draw(guid);
				} else {
					NotShown("Countdown");
				}

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

	// The view of a selected record the generation has none for, which the views, reading the generated file, cannot show.
	private static void NotShown(string kind) {
		ImGui.Text(kind);
		EditorText.Note("Not shown in the editor.");
	}

	// The view of a selected object, group or objective the generation has none for: its title, its Present when, which can
	// bring it back, and that the rest cannot be shown.
	private void Absent(string title, PresenceTarget? target) {
		ImGui.TextWrapped(title);
		_presentWhen.Draw(target);
		ImGui.Separator();
		EditorText.Note(GeneratedRecords.Absent);
	}

	private void DrawObject(SceneObject sel) {
		var placement = sel.Placement;
		if (placement.SlotIndex < 0 && !placement.IsPlayerLance) {
			Absent(Title(sel), _presence.ObjectTarget(sel));
			return;
		}

		ImGui.TextWrapped(Title(sel));
		ImGui.AlignTextToFramePadding();
		ImGui.Text("Group:");
		ImGui.SameLine();
		if (_index.Scene.GroupOf(sel) is { } ownGroup) {
			_links.ShortLayoutGroup(ownGroup.Guid);
		}
		_presentWhen.Draw(_presence.ObjectTarget(sel));
		_variants.DrawObjectChoice(sel);
		ImGui.Separator();
		ImGui.Text($"Kind: {placement.Kind}");
		if (!_fit.Covers(sel)) {
			ImGui.Text($"Type index: {placement.TypeIndex}");
		}

		ImGui.Text(placement.IsPlayerLance ? "Player squad" : $"Roster slot: {placement.SlotIndex}");
		TeamField(placement.GroupIndex, ofMember: true);
		_fit.Draw(sel);

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
			ImGui.TextUnformatted($"Starting condition: {placement.StartingCondition}%");
		} else if (_index.Scene.Planned(sel) is { Squad: true, MemberIndex: 0 }) {
			_machine.DrawPlayer();
		} else {
			_machine.DrawSquadmateObject(placement.SlotIndex);
		}

		if (_index.Scene.GroupOf(sel) is { OffScene: true } landing) {
			EditorText.Note(MissionText.OffScene(landing.ArrivalVerb, member: true));
		} else if (_index.Scene.IsWaiting(sel)) {
			ImGui.TextWrapped("Its group is waiting on a trigger: nothing of it is drawn or ticked until "
				+ "that fires, and this position is a placeholder unless the group arrives in place.");
		}

		var key = _index.Scene.KeyOf(sel);
		if (key is { } own && !placement.IsPlayerLance) {
			DrawOwnActions(own);
		}

		OutOfActionWrites(sel);

		ObjectiveLinks(key is { } subject ? _index.Scene.ObjectivesAbout(subject) : Enumerable.Empty<LayoutObjective>(),
			_index.Scene.SelectionOf(sel)!);
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
				ImGui.TextWrapped("It is the last member of a group absent from the mission as opened, which would go "
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
	/// (<paramref name="ownRoute"/>, block-11 indices) on its one line. <paramref name="fired"/> are the triggers it fires
	/// when engaged or defeated, by block-5 index, -1 for one not shown in the editor.
	/// </summary>
	private void References(DeleteNamers names, IReadOnlyList<int>? ownRoute = null, IReadOnlyList<int>? fired = null) {
		foreach (int objective in names.Objectives) {
			ImGui.PushID($"objective{objective}");
			_links.GeneratedObjective(objective);
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
				ImGui.TextWrapped("A trigger it fires, not shown in the editor");
			}

			ImGui.PopID();
		}

		var groups = names.Orders.Select(order => order.Group).Concat(ownRoute ?? []).Distinct().ToList();
		foreach (int group in groups) {
			var parts = names.Orders.Where(order => order.Group == group).Select(order => $"{MissionText.Ordinal(order.Slot + 1)} order").ToList();
			if (ownRoute?.Contains(group) == true) {
				parts.Add("own route");
			}

			ImGui.PushID($"group{group}");
			_links.ShortGroup(group);
			ImGui.SameLine(0f, 0f);
			ImGui.TextUnformatted($": {string.Join(", ", parts)}");
			ImGui.PopID();
		}

		if (names.Elsewhere > 0) {
			ImGui.TextWrapped($"{names.Elsewhere} more, not shown in the editor");
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
		if (typed is { } draft && _index.Scene.KeyOf(sel) is { } key) {
			_mover.Commit(new ObjectTarget(key), pos, draft.X, draft.Y, draft.Z);
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

	// Row-16 GUID <guid>'s view, the group generated at block-11 index <group>.
	private void DrawGroup(short guid, int group) {
		var name = _links.Names.Group(guid);
		EditorText.Wrapped(name.Text);
		if (name.Number is { } number) {
			ImGui.TextDisabled(number);
		}

		_presentWhen.Draw(_presence.GroupTarget(guid));
		TeamField(group, ofMember: false);
		if (group == 0) {
			_machine.DrawSquad();
		}

		ImGui.Separator();
		DrawDeployment(guid);

		if (_index.SetupOf(group) is { } setup) {
			ImGui.Separator();
			DrawSetup(guid, group, setup);
		}

		ImGui.Separator();
		var members = group < _index.GroupMembers.Count ? _index.GroupMembers[group] : Array.Empty<SceneObject>();
		ImGui.Text($"Members: {members.Count}");
		foreach (var member in members) {
			_links.Object(member);
		}

		// Group_ReportIfAllOutOfAction: the last member to go finds every other destroyed or immobilized; a disarmed one
		// counts as standing (docs/retail/simulation/mission-deployment.md#the-out-of-action-report).
		if (group < Mission.GroupOutOfActionReports.Count) {
			OutOfActionWrites("When every member is destroyed or immobilized, sets campaign variables", Mission.GroupOutOfActionReports[group]);
		}

		ObjectiveLinks(_index.Scene.ObjectivesAbout(guid), new GroupSelection(guid));
		_orders.Draw(guid);
	}

	/// <summary>
	/// The trigger the group waits on before it is in the mission, from the layout, a choice of none or any trigger
	/// (<see cref="ActionEditor.RequestDeployment"/>), with a link and how the group arrives. The player's squad has none: no
	/// retail squad record carries one (<see cref="HercWorks.Core.Data.File.Msn.ActionWiring"/>).
	/// </summary>
	private void DrawDeployment(short guid) {
		if (_index.Scene.Group(guid) is not { } planned) {
			return;
		}

		short waitsOn = planned.Waiting ? planned.DeploymentAction : (short)-1;
		if (planned.Number == 0 && waitsOn == -1) {
			ImGui.TextUnformatted("In the mission from the start.");
			return;
		}

		string? why = _actions.WhyNoDeployment(guid);
		ImGui.AlignTextToFramePadding();
		ImGui.TextUnformatted("Waits on:");
		ImGui.SameLine();
		ImGui.SetNextItemWidth(-1f);
		ImGui.BeginDisabled(why != null);
		if (ActionChoice.Draw("##deployment", waitsOn, _index.Scene, _links.Names) is { } chosen) {
			_actions.RequestDeployment(guid, chosen);
		}

		ImGui.EndDisabled();
		if (waitsOn != -1) {
			_links.Trigger(waitsOn);
		}

		if (planned.OffScene) {
			EditorText.Note(MissionText.OffScene(planned.ArrivalVerb, member: false));
		} else if (waitsOn != -1) {
			EditorText.Wrapped($"Not in the mission until that trigger fires; then it arrives {MissionText.Arrival(planned.ArrivalVerb)}.");
		} else {
			ImGui.TextUnformatted("In the mission from the start.");
		}

		EditorText.Reason(why);
		EditorText.Reason(_actions.Failure);
	}

	/// <summary>
	/// The triggers the roster record <paramref name="key"/> fires when engaged and when defeated, from the layout, each a choice
	/// of none or any trigger (<see cref="ActionEditor.RequestObjectAction"/>), with a link, and what engaged and defeated mean
	/// (docs/retail/simulation/mission-deployment.md#an-objects-own-two-actions--0x1b2-and-0x1b6), the engagement distance in
	/// metres. Not for a machine of the player's squad, which is built from <c>player.mec</c>: no retail squad record carries
	/// either.
	/// </summary>
	private void DrawOwnActions(ObjectKey key) {
		(short engaged, short defeated) = _index.Scene.Layout.Entry(key.Row, key.Guid)?.Fields switch {
			MechRosterEntry144 herc => (herc.EngagementActionRef, herc.DefeatActionRef),
			FlyerRosterEntry102 flyer => (flyer.EngagementActionRef, flyer.DefeatActionRef),
			BaseRosterEntry62 structure => (structure.EngagementActionRef, structure.DefeatActionRef),
			_ => ((short)-1, (short)-1),
		};

		ImGui.Separator();
		string? why = _actions.WhyNoObjectAction(key);
		OwnAction("When engaged, fires", key, defeated: false, engaged, why);
		OwnAction("When defeated, fires", key, defeated: true, defeated, why);
		EditorText.Note($"A trigger set here fires when it is engaged: an enemy within {MissionText.Metres(Sim.Detection.EngagementRange)} "
			+ "spots it, or an enemy shoots it. Or when it is defeated: destroyed; a herc also when immobilized or left with no "
			+ "working weapon.");
		EditorText.Reason(why);
		EditorText.Reason(_actions.Failure);
	}

	// One of the two, row-10 GUID <current> or -1: a choice of the triggers, with a link. A ref the layout has no trigger for
	// fires nothing, as the generation resolves it to none.
	private void OwnAction(string label, ObjectKey key, bool defeated, short current, string? why) {
		if (_index.Scene.Action(current) == null) {
			current = -1;
		}

		ImGui.PushID(label);
		ImGui.AlignTextToFramePadding();
		ImGui.TextUnformatted($"{label}:");
		string? noNone = _actions.WhyNoNone(key, defeated);
		ImGui.SameLine();
		ImGui.SetNextItemWidth(-1f);
		ImGui.BeginDisabled(why != null);
		if (ActionChoice.Draw("##own", current, _index.Scene, _links.Names, offerNone: noNone == null, whyNoNone: noNone) is { } chosen) {
			_actions.RequestObjectAction(key, defeated, chosen);
		}

		ImGui.EndDisabled();
		if (current != -1) {
			_links.Trigger(current);
		}

		ImGui.PopID();
	}

	/// <summary>The group's own point, heading and formation; its point as the mover moves it, from the layout.</summary>
	private void DrawSetup(short guid, int group, MissionGroupSetup setup) {
		string source = setup.PointSource switch {
			MissionGroupPointSource.OwnPoint => "its own point",
			MissionGroupPointSource.RouteStart => "its route's first waypoint",
			_ => "no point and no route: the origin"
		};

		// The authored point, which a move writes; a flyer group's Z is where its first member flies (PointMover.PositionOf).
		var target = new GroupTarget(guid);
		bool movable = _mover.WhyFixedGroup(guid) == null;
		var at = movable ? _mover.PositionOf(target) : setup.AuthoredPoint;
		ImGui.Text("Point, in world units:");
		ImGui.PushID("point");
		var typed = _point.Draw(target, at, movable && _mover.HasOwnHeight(target), enabled: movable);
		ImGui.PopID();
		EditorText.Note($"from {source}");
		if (setup.AuthoredPoint != setup.Point) {
			ImGui.TextWrapped($"It stands at {setup.Point.X}, {setup.Point.Y}: painting its ground moves it to a fixed "
				+ "spot in its terrain tile.");
		}

		if (typed is { } draft) {
			_mover.Commit(target, at, draft.X, draft.Y, draft.Z);
		}

		if (_mover.StandsOnPoint(guid)) {
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
		DrawGroupMove(guid, group, setup);
	}

	/// <summary>What dragging the group's move handles moves with it (<see cref="PointMover"/>), or why it cannot be moved.</summary>
	private void DrawGroupMove(short guid, int group, MissionGroupSetup setup) {
		if (_mover.WhyFixedGroup(guid) is { } reason) {
			EditorText.Reason(reason);
			return;
		}

		var target = new GroupTarget(guid);
		int sharers = _mover.SharersOf(target);
		string others = $"{sharers} other record(s) (trigger areas, groups, waypoints) referencing its point";
		if (setup.PointSource == MissionGroupPointSource.OwnPoint) {
			ImGui.TextWrapped(sharers > 0
				? $"Its point is referenced by {sharers} other record(s) (trigger areas, groups, waypoints), which move with it. Alt+drag moves the group alone."
				: "Nothing else references its point.");
		} else if (_mover.OnRouteStart(guid)) {
			ImGui.TextWrapped("Dragging moves its route's first waypoint, the start of the route"
				+ (sharers > 0 ? $", and the {others} with it" : "")
				+ ". Alt+drag gives the group a point of its own.");
		} else {
			ImGui.TextWrapped("Dragging gives it a point of its own.");
		}

		if (_index.Scene.Group(guid) is { Waiting: true } planned && planned.ArrivalVerb is MissionAction.VerbPodWide
				or MissionAction.VerbPodNarrow or MissionAction.VerbWalkBehind or MissionAction.VerbWalkAhead) {
			ImGui.TextWrapped($"It arrives {MissionText.ArrivalKind(planned.ArrivalVerb)}, placed about the "
				+ "player when the trigger fires: its point is a placeholder, and moving it does not move where it arrives.");
		}
	}

	/// <summary>
	/// The campaign variables an object sets when it goes out of the fight, read-only, headed by what sends it out
	/// (docs/retail/simulation/mission-deployment.md#the-out-of-action-report): a herc's report runs the first time it is
	/// destroyed or immobilized, a flyer's, a structure's and a ground vehicle's when it is destroyed. Nothing when it sets
	/// none. A squad herc's record sets none in play, since the squad's machines start with empty slots, so its view says so
	/// where its record lists any.
	/// </summary>
	private void OutOfActionWrites(SceneObject sel) {
		var placement = sel.Placement;
		string heading = placement.Kind == MissionUnitKind.Mech
			? "When destroyed or immobilized, sets campaign variables"
			: "When destroyed, sets campaign variables";
		if (placement.IsPlayerLance) {
			if (_index.Scene.KeyOf(sel) is { } key && _index.Scene.Layout.Entry(key.Row, key.Guid)?.Fields is MechRosterEntry144 herc
					&& WritesOf(herc.OutOfActionReport).Any(write => write.Variable >= 0)) {
				ImGui.Separator();
				ImGui.TextWrapped($"{heading}:");
				EditorText.Note("None in play: a HERC of the player's squad sets none of its own, whatever its record lists.");
			}

			return;
		}

		OutOfActionWrites(heading, placement.OutOfActionReport ?? OutOfActionReport.None);
	}

	// <report>'s writes under <heading>, or nothing when it makes none.
	private static void OutOfActionWrites(string heading, OutOfActionReport report) {
		var writes = CampaignVariableWrites.Pairs(report.CounterRefs, report.CounterOps);
		if (writes.Any(write => write.Variable >= 0)) {
			CampaignVariableWrites.DrawReadOnly(heading, WriteLayer.OutOfAction, writes);
		}
	}

	// The (variable, code) pairs of a record's interleaved out-of-action words.
	private static IReadOnlyList<(short Variable, short Op)> WritesOf(short[] words) =>
		Enumerable.Range(0, words.Length / 2).Select(pair => (words[pair * 2], words[pair * 2 + 1])).ToList();

	/// <summary>
	/// Links to the win and lose conditions asked of the selection, each its sentence, and a button that adds a win condition
	/// about <paramref name="subject"/>, the selection itself (<see cref="ObjectiveEditor"/>).
	/// </summary>
	private void ObjectiveLinks(IEnumerable<LayoutObjective> objectives, EditorSelection subject) {
		ImGui.Separator();
		ImGui.TextUnformatted("Win and lose conditions about it:");
		ImGui.PushID("objectives");
		bool any = false;
		foreach (var objective in objectives) {
			ImGui.PushID(objective.Index);
			_links.Objective(objective.Index);
			ImGui.PopID();
			any = true;
		}

		if (!any) {
			EditorText.Note("None.");
		}

		string? why = _objectives.WhyNoCreate(subject);
		ImGui.BeginDisabled(why != null);
		if (ImGui.Button("Add a win condition")) {
			_objectives.RequestCreate(subject);
		}

		ImGui.EndDisabled();
		EditorText.Reason(why);
		EditorText.Reason(_objectives.Failure);
		ImGui.PopID();
	}
}
