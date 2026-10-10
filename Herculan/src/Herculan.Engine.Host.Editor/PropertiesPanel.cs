using System.Numerics;
using Herculan.Engine.Numerics;
using Herculan.Engine.Platform;
using Herculan.Engine.Render;
using Herculan.Engine.Scene;
using Herculan.Engine.World;
using ImGuiNET;

namespace Herculan.Engine.Host.Editor;

/// <summary>
/// The Properties panel down the right of the window: one view per kind of
/// <see cref="EditorSelection"/>. Every record a view mentions is a link that selects it, so the
/// panel is also how a mission's wiring is walked — area to action, action to the groups it brings
/// in, group to its route.
/// </summary>
internal sealed class PropertiesPanel {
	// In pixels on a 100% display (ScaledImGui.Scaled).
	private const float PanelWidth = 340f;

	private readonly MissionIndex _index;
	private readonly SelectionState _selection;

	public PropertiesPanel(MissionIndex index, SelectionState selection) {
		_index = index;
		_selection = selection;
	}

	private Mission Mission => _index.Mission;

	public void Draw(Vector2 display, float menuBarHeight) {
		float width = ScaledImGui.Scaled(PanelWidth);

		ImGui.SetNextWindowPos(new Vector2(display.X - width, menuBarHeight));
		ImGui.SetNextWindowSize(new Vector2(width, display.Y - menuBarHeight));
		ImGui.Begin("Properties", ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoCollapse);

		switch (_selection.Current) {
			case ObjectSelection selected:
				DrawObject(selected.Object);
				break;
			case GroupSelection group:
				DrawGroup(group.Group);
				break;
			case AreaSelection area:
				DrawArea(area.Area);
				break;
			case ActionSelection action:
				DrawAction(action.Action);
				break;
			case RouteSelection route:
				DrawRoute(route);
				break;
			case ObjectiveSelection objective:
				DrawObjective(objective.Objective);
				break;
			case TimerSelection timer:
				DrawTimer(timer.Timer);
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
		ImGui.TextWrapped(placement.TypeName ?? $"{placement.Kind} #{placement.TypeIndex}");
		ImGui.Separator();
		ImGui.Text($"Kind: {placement.Kind}");
		ImGui.Text($"Type index: {placement.TypeIndex}");
		ImGui.Text(placement.IsPlayerLance ? "Player squad" : $"Roster slot: {placement.SlotIndex}");
		ImGui.Text($"Side: {placement.Side}");
		ImGui.AlignTextToFramePadding();
		ImGui.Text("Group:");
		ImGui.SameLine();
		GroupLink(placement.GroupIndex);

		if (sel.Model is { } model) {
			ImGui.Separator();
			ImGui.Text($"Model: {model.Key}");
			ImGui.Text($"Triangles: {model.TriangleVertexCount / 3}");
			ImGui.Text(model.Atlas is { } atlas
				? $"Texture: {atlas.FrameCount} frames ({atlas.Width}x{atlas.Height})"
				: "Texture: none");
		}

		ImGui.Separator();
		var pos = sel.Object.Position;
		ImGui.Text($"Position: {pos.X}, {pos.Y}, {pos.Z} units");
		ImGui.Text($"          {pos.X / WorldScale.WorldUnitsPerMeter:F1}, {pos.Y / WorldScale.WorldUnitsPerMeter:F1}, " +
			$"{pos.Z / WorldScale.WorldUnitsPerMeter:F1} m");
		ImGui.Text($"Heading: {MissionText.Heading(sel.Object.Heading)}");
		ImGui.Text($"Hit radius: {sel.Object.HitRadius} units");

		if (!placement.IsPlayerLance) {
			ImGui.Text($"Starting condition: {placement.StartingCondition}%");
		}

		if (_index.IsWaiting(sel)) {
			ImGui.TextWrapped("Its group is waiting on an action: nothing of it is drawn or ticked until "
				+ "that fires, and this position is a placeholder unless the group arrives in place.");
		}

		if (placement.EngagementActionRef >= 0 || placement.DefeatActionRef >= 0) {
			ImGui.Separator();
			LabelledActionLink("When engaged, fires", placement.EngagementActionRef);
			LabelledActionLink("When defeated, fires", placement.DefeatActionRef);
		}

		if (placement.OutOfActionReport is { } report) {
			OutOfActionWrites("When out of the fight", report);
		}

		ObjectiveLinks(_index.ObjectivesOfObject(sel));
	}

	private void DrawGroup(int group) {
		ImGui.TextWrapped(MissionText.DescribeGroup(Mission, group));
		ImGui.Separator();

		int waitsOn = group < Mission.GroupDeploymentActions.Count ? Mission.GroupDeploymentActions[group] : -1;
		if (waitsOn >= 0) {
			ImGui.AlignTextToFramePadding();
			ImGui.Text("Not in the mission until");
			ImGui.SameLine();
			ActionLink(waitsOn);
			ImGui.TextWrapped($"fires; then arrives {MissionText.Arrival(Mission.Actions[waitsOn].Verb)}.");
		} else {
			ImGui.Text("In the mission from the start.");
		}

		if (_index.SetupOf(group) is { } setup) {
			ImGui.Separator();
			DrawSetup(group, setup);
		}

		ImGui.Separator();
		var members = group < _index.GroupMembers.Count ? _index.GroupMembers[group] : Array.Empty<SceneObject>();
		ImGui.Text($"Members: {members.Count}");
		foreach (var member in members) {
			ObjectLink(member);
		}

		if (group < Mission.GroupOutOfActionReports.Count) {
			OutOfActionWrites("When all of it is out of the fight", Mission.GroupOutOfActionReports[group]);
		}

		ObjectiveLinks(_index.ObjectivesOfGroup(group));

		if (group >= Mission.GroupOrders.Count) {
			return;
		}

		ImGui.Separator();
		ImGui.Text("Orders:");
		var orders = Mission.GroupOrders[group];
		for (int slot = 0; slot < orders.Count; slot++) {
			if (orders[slot] is not { } order) {
				continue;
			}

			ImGui.PushID(slot);
			ImGui.BulletText($"{slot}: {MissionText.OrderVerb(order.Verb)}");
			if (order.SubjectKind != MissionOrderSubject.None && order.SubjectRef >= 0) {
				ImGui.Indent();
				ImGui.AlignTextToFramePadding();
				ImGui.Text("subject:");
				ImGui.SameLine();
				SubjectLink(order.SubjectKind, order.SubjectRef);
				ImGui.Unindent();
			}

			if (order.RouteRef >= 0) {
				ImGui.Indent();
				ImGui.AlignTextToFramePadding();
				ImGui.Text(slot == 0 ? "route:" : "route (never walked):");
				ImGui.SameLine();
				RouteLink(order.RouteRef);
				ImGui.Unindent();
			}

			if (order.ActionRef >= 0) {
				ImGui.Indent();
				ImGui.AlignTextToFramePadding();
				ImGui.Text("ends when");
				ImGui.SameLine();
				ActionLink(order.ActionRef);
				ImGui.SameLine();
				ImGui.Text("fires");
				ImGui.Unindent();
			}

			ImGui.PopID();
		}
	}

	private void DrawArea(int reference) {
		if (_index.Area(reference) is not { } area) {
			return;
		}

		ImGui.Text($"Trigger area {reference}");
		ImGui.Separator();

		if (area.Shape == MissionTriggerShape.Circle) {
			ImGui.Text("Circle");
			ImGui.Text($"Centre: {area.A.X}, {area.A.Y}");
			ImGui.Text($"Radius: {MissionText.Distance(area.Radius)}");
		} else {
			ImGui.Text("Box (height ignored)");
			ImGui.Text($"X: {Math.Min(area.A.X, area.B.X)} to {Math.Max(area.A.X, area.B.X)}");
			ImGui.Text($"Y: {Math.Min(area.A.Y, area.B.Y)} to {Math.Max(area.A.Y, area.B.Y)}");
			ImGui.Text($"Size: {MissionText.Distance(Math.Abs(area.B.X - area.A.X))} by "
				+ $"{MissionText.Distance(Math.Abs(area.B.Y - area.A.Y))}");
		}

		foreach (int action in _index.ActionsTesting(reference)) {
			ImGui.Separator();
			ImGui.PushID(action);
			DrawAction(action);
			ImGui.PopID();
		}
	}

	private void DrawAction(int action) {
		if (action < 0 || action >= Mission.Actions.Count) {
			return;
		}

		var record = Mission.Actions[action];
		var links = _index.LinksOf(action);

		ImGui.AlignTextToFramePadding();
		if (_selection.Current is ActionSelection { Action: var current } && current == action) {
			ImGui.Text($"Action {action}");
		} else {
			ActionLink(action);
		}

		ImGui.Spacing();
		ImGui.Text("Fires when:");
		ImGui.Indent();
		if (record.Areas.Count > 0) {
			ImGui.TextWrapped($"{MissionText.TriggerSubject(record.Type)} enters");
			foreach (var area in record.Areas) {
				AreaLink(area.Reference);
			}
		}

		if (record.Type >= MissionAction.SubjectTargetMech && record.TargetRef >= 0) {
			ImGui.AlignTextToFramePadding();
			ImGui.Text("target:");
			ImGui.SameLine();
			if (record.Type == MissionAction.SubjectTargetGroup) {
				GroupLink(record.TargetRef);
			} else {
				var kind = record.Type switch {
					MissionAction.SubjectTargetMech => MissionUnitKind.Mech,
					MissionAction.SubjectTargetFlyer => MissionUnitKind.Flyer,
					_ => MissionUnitKind.Base
				};
				SlotLink(kind, record.TargetRef);
			}
		}

		foreach (int timer in links.TimersFiringIt) {
			TimerLink(timer);
			ImGui.TextWrapped("runs out");
		}

		foreach (var placed in links.EngagedObjects) {
			ImGui.Text("engaged:");
			ImGui.SameLine();
			ObjectLink(placed);
		}

		foreach (var placed in links.DefeatedObjects) {
			ImGui.Text("defeated:");
			ImGui.SameLine();
			ObjectLink(placed);
		}

		if (record.Areas.Count == 0 && links.TimersFiringIt.Count == 0 && links.EngagedObjects.Count == 0
				&& links.DefeatedObjects.Count == 0) {
			ImGui.TextDisabled("nothing in this mission fires it");
		}

		ImGui.Unindent();

		ImGui.Spacing();
		ImGui.Text("Then:");
		ImGui.Indent();
		bool any = false;

		foreach (int group in links.WaitingGroups) {
			GroupLink(group);
			ImGui.TextWrapped($"arrives {MissionText.Arrival(record.Verb)}");
			any = true;
		}

		foreach (var ends in links.OrdersItEnds) {
			ImGui.PushID($"order{ends.Group}.{ends.Slot}");
			GroupLink(ends.Group);
			ImGui.TextWrapped($"moves on from order {ends.Slot} ({MissionText.OrderVerb(ends.Order.Verb)})");
			ImGui.PopID();
			any = true;
		}

		foreach (int timer in links.TimersItStarts) {
			TimerLink(timer);
			ImGui.TextWrapped("starts, and when it runs out fires");
			ImGui.Indent();
			TimerSequence(timer);
			ImGui.Unindent();
			any = true;
		}

		for (int i = 0; i < record.CounterRefs.Count && i < record.CounterOps.Count; i++) {
			if (record.CounterRefs[i] >= 0) {
				ImGui.Text($"counter {record.CounterRefs[i]}: {MissionText.ActionCounterOp(record.CounterOps[i])}");
				any = true;
			}
		}

		if (record.MessageId >= 0) {
			// Posted once per counter the action names, so one naming none never says it — see
			// docs/retail/simulation/mission-deployment.md#the-four-ways-an-action-activates.
			string text = string.Join(" ", _index.CommandMessages?.Instruction(record.MessageId)
				.Select(entry => entry.Text) ?? Array.Empty<string>());
			int posts = record.CounterRefs.Count(reference => reference >= 0);
			ImGui.TextWrapped($"message {record.MessageId}: {(text.Length > 0 ? text : "(no text)")}");
			ImGui.TextDisabled(posts == 0 ? "never posted: the action names no counter" : $"posted {posts}x");
			any = true;
		}

		if (!any) {
			ImGui.TextDisabled("nothing waits on it");
		}

		ImGui.Unindent();
	}

	/// <summary>The actions a timer fires, as links, skipping unset and out-of-range slots.</summary>
	private void TimerSequence(int timer) {
		bool any = false;
		foreach (short fired in Mission.ActionTimers[timer].SequenceRefs) {
			if (fired >= 0 && fired < Mission.Actions.Count) {
				ImGui.PushID($"timer{timer}.{fired}");
				ActionLink(fired);
				ImGui.PopID();
				any = true;
			}
		}

		if (!any) {
			ImGui.TextDisabled("no action");
		}
	}

	private void DrawTimer(int timer) {
		if (timer < 0 || timer >= Mission.ActionTimers.Count) {
			return;
		}

		var record = Mission.ActionTimers[timer];
		ImGui.Text($"Timer {timer}");
		ImGui.Separator();
		ImGui.Text($"Delay: {record.Delay >> MissionActionTimer.DelayShift} s");
		if (record.PrimaryActionRef >= 0) {
			ImGui.AlignTextToFramePadding();
			ImGui.Text("Counts down once");
			ImGui.SameLine();
			ActionLink(record.PrimaryActionRef);
			ImGui.SameLine();
			ImGui.Text("fires");
		} else {
			ImGui.Text("Counts down from the start of the mission");
		}

		ImGui.Spacing();
		ImGui.Text("When it runs out, fires:");
		ImGui.Indent();
		TimerSequence(timer);
		ImGui.Unindent();
	}

	private void DrawObjective(int index) {
		if (index < 0 || index >= Mission.Objectives.Count) {
			return;
		}

		var objective = Mission.Objectives[index];
		bool groupSubject = objective.SubjectKind == MissionObjectiveSubject.Group;

		ImGui.Text($"Objective {index}");
		ImGui.Separator();
		ImGui.TextWrapped(objective.Required == MissionObjective.Mandatory
			? "Mandatory: the mission needs this to come true."
			: "Failure condition: the mission is lost the moment this comes true.");

		ImGui.Spacing();
		ImGui.AlignTextToFramePadding();
		ImGui.Text("Subject:");
		ImGui.SameLine();
		if (MissionIndex.UnitKindOf(objective.SubjectKind) is { } kind) {
			SlotLink(kind, objective.SubjectRef);
		} else if (groupSubject) {
			GroupLink(objective.SubjectRef);
		} else {
			ImGui.TextDisabled($"kind {(int)objective.SubjectKind}, which no case reads");
		}

		ImGui.TextWrapped(MissionText.ObjectiveCondition(objective.ConditionCode, groupSubject));

		if (objective.ConditionCode == MissionObjective.ConditionOrderComplete) {
			ImGui.Indent();
			ImGui.AlignTextToFramePadding();
			ImGui.Text("route:");
			ImGui.SameLine();
			if (objective.RouteRef >= 0) {
				RouteLink(objective.RouteRef);
			} else {
				ImGui.TextDisabled("none");
			}

			int group = _index.OrderGroupOf(objective);
			if (group >= 0) {
				int slot = _index.SlotOnRoute(group, objective.RouteRef);
				ImGui.AlignTextToFramePadding();
				ImGui.Text("asked of");
				ImGui.SameLine();
				GroupLink(group);
				ImGui.TextWrapped(slot >= 0
					? $"about its order slot {slot}, the first to run that route"
					: "none of its orders runs that route, so this never comes true");
			}

			ImGui.Unindent();
		}

		var lines = Mission.DescriptionOf(objective).Where(line => line.Length > 0).ToList();
		if (lines.Count > 0) {
			ImGui.Separator();
			ImGui.Text($"Failure text (mission.str line {objective.TextRef}):");
			foreach (string line in lines) {
				ImGui.TextWrapped(line);
			}
		}

		bool writes = false;
		for (int i = 0; i < objective.CounterRefs.Count && i < objective.CounterOps.Count; i++) {
			// A slot is applied only when both its ref and its operation are non-negative.
			if (objective.CounterRefs[i] < 0 || objective.CounterOps[i] < 0) {
				continue;
			}

			if (!writes) {
				ImGui.Separator();
				ImGui.Text("The first time it comes true:");
				writes = true;
			}

			ImGui.BulletText($"counter {objective.CounterRefs[i]}: {MissionText.ObjectiveCounterOp(objective.CounterOps[i])}");
		}
	}

	/// <summary>The group's own point, heading and formation.</summary>
	private void DrawSetup(int group, MissionGroupSetup setup) {
		string source = setup.PointSource switch {
			MissionGroupPointSource.OwnPoint => "its own point",
			MissionGroupPointSource.RouteStart => "its route's first waypoint",
			_ => "no point and no route: the origin"
		};

		ImGui.Text($"Point: {setup.Point.X}, {setup.Point.Y}");
		ImGui.TextDisabled($"from {source}");
		if (setup.AuthoredPoint != setup.Point) {
			ImGui.TextWrapped($"Authored at {setup.AuthoredPoint.X}, {setup.AuthoredPoint.Y}; painting its ground "
				+ "moves it to a fixed spot in its terrain tile.");
		}

		ImGui.Text($"Heading: {MissionText.Heading(setup.Heading)}");
		if (setup.HeadingFromRoute) {
			ImGui.TextDisabled("names none: the bearing of its route's first leg");
		}

		string kind = group < Mission.GroupKinds.Count ? Mission.GroupKinds[group].ToString().ToLowerInvariant() : "?";
		ImGui.Text($"Formation: {setup.FormationId} ({kind} table)");
		ImGui.Text(setup.PaintsGround ? "Paints ground: yes" : "Paints ground: no");

		foreach (var pad in Mission.BasePads) {
			if (pad.GroupIndex != group) {
				continue;
			}

			var (x, y) = pad.TileOrigin;
			int size = pad.Layout.TileSize;
			ImGui.TextWrapped($"Pad: tile {x} to {x + size}, {y} to {y + size} ({MissionText.Distance(size)} square), "
				+ $"material {pad.Layout.MaterialIndex}. Only which tile the authored point falls in matters; "
				+ "a move within the tile changes nothing.");
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

	/// <summary>Links to the objectives asked of the selection, or nothing when none is.</summary>
	private void ObjectiveLinks(IEnumerable<int> objectives) {
		bool any = false;
		foreach (int objective in objectives) {
			if (!any) {
				ImGui.Separator();
				ImGui.Text("Objectives asked of it:");
				any = true;
			}

			ObjectiveLink(objective);
		}
	}

	private void DrawRoute(RouteSelection selected) {
		if (_index.Route(selected.Route) is not { } route) {
			return;
		}

		ImGui.Text($"Route {route.Reference}");
		ImGui.Separator();
		ImGui.Text(route.Closed ? "Closed: a loop walked for ever" : "Open: walking it to the end completes the order");

		if (route.WalkedBy.Count > 0) {
			ImGui.Text("Walked by:");
			foreach (int group in route.WalkedBy) {
				GroupLink(group);
			}
		} else {
			ImGui.TextWrapped("Walked by no group: only later order slots name it, and a group only ever "
				+ "walks its first order's route.");
		}

		if (route.NamedBy.Count > 0) {
			ImGui.Text("Named in a later order by:");
			foreach (int group in route.NamedBy) {
				GroupLink(group);
			}
		}

		ImGui.Separator();
		int count = route.Closed ? route.Points.Count - 1 : route.Points.Count;
		ImGui.Text($"Waypoints: {count}");
		for (int i = 0; i < count; i++) {
			var point = route.Points[i];
			bool isSelected = selected.Waypoint == i;
			if (ImGui.Selectable($"{i}: {point.X}, {point.Y}##wp{i}", isSelected)) {
				_selection.Current = new RouteSelection(route.Reference, i);
			}
		}
	}

	private void LabelledActionLink(string label, int action) {
		if (action < 0) {
			return;
		}

		ImGui.AlignTextToFramePadding();
		ImGui.Text($"{label}:");
		ImGui.SameLine();
		ActionLink(action);
	}

	private void SubjectLink(MissionOrderSubject kind, int reference) {
		switch (kind) {
			case MissionOrderSubject.Group:
				GroupLink(reference);
				break;
			case MissionOrderSubject.Mech:
				SlotLink(MissionUnitKind.Mech, reference);
				break;
			case MissionOrderSubject.Flyer:
				SlotLink(MissionUnitKind.Flyer, reference);
				break;
			default:
				SlotLink(MissionUnitKind.Base, reference);
				break;
		}
	}

	private void SlotLink(MissionUnitKind kind, int slot) {
		if (_index.ObjectAt(kind, slot) is { } placed) {
			ObjectLink(placed);
		} else {
			ImGui.TextDisabled($"{kind.ToString().ToLowerInvariant()} slot {slot} (not placed)");
		}
	}

	private void ObjectLink(SceneObject placed) =>
		Link(MissionText.Describe(placed), new ObjectSelection(placed));

	private void GroupLink(int group) =>
		Link(MissionText.DescribeGroup(Mission, group), new GroupSelection(group));

	private void ActionLink(int action) => Link($"Action {action}", new ActionSelection(action));

	private void AreaLink(int area) {
		string shape = _index.Area(area)?.Shape == MissionTriggerShape.Circle ? "circle" : "box";
		Link($"Area {area} ({shape})", new AreaSelection(area));
	}

	private void RouteLink(int route) => Link($"Route {route}", new RouteSelection(route));

	private void TimerLink(int timer) =>
		Link($"Timer {timer} ({MissionText.TimerSummary(Mission.ActionTimers[timer])})", new TimerSelection(timer));

	private void ObjectiveLink(int objective) =>
		Link(MissionText.DescribeObjective(Mission.Objectives[objective], objective), new ObjectiveSelection(objective));

	/// <summary>
	/// A clickable label that selects <paramref name="target"/>. The ID is the label itself, which is
	/// unique within each view's own ID scope (the views push one per repeated block).
	/// </summary>
	private void Link(string label, EditorSelection target) {
		ImGui.PushStyleColor(ImGuiCol.Text, LinkColor);
		if (ImGui.Selectable(label, false, ImGuiSelectableFlags.None, ImGui.CalcTextSize(label))) {
			_selection.Current = target;
		}

		ImGui.PopStyleColor();
	}

	private static readonly Vector4 LinkColor = new(0.55f, 0.78f, 1f, 1f);
}
