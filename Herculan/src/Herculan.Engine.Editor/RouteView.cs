using Herculan.Engine.World;
using ImGuiNET;

namespace Herculan.Engine.Editor;

/// <summary>
/// The Properties panel's view of a route, read from the layout and <see cref="MissionIndex"/> by its row-8 GUID, every route
/// of the <c>.MSN</c> included (docs/herculan/handoff-editor-placement.md#routes): whether it is a loop, which groups walk it,
/// with a choice that makes it a group's first order's route (<see cref="OrderEditor"/>), its waypoints, "Start" and then
/// "Waypoint 1" on, each with its distance from the one before (<see cref="MissionText.Waypoint"/>), and the edits that make a route (<see cref="RouteEditor"/>, <see cref="RoutePlacing"/>): add waypoints after the last,
/// insert one after the selected one, remove the selected one, close or open the loop, delete the route; the selected
/// waypoint's X and Y as typed fields (<see cref="PointFields"/>); and, when its waypoints are an option of a random table,
/// which one, since an edit changes that option (docs/herculan/handoff-editor-conditions.md#random-tables).
/// </summary>
internal sealed class RouteView {
	// The heads-down map marks the player's route from its second waypoint and at most nine of them
	// (docs/retail/simulation/heads-down-display.md#markers): waypoints past the tenth get no marker.
	private const int MarkedWaypoints = 10;

	private readonly MissionIndex _index;
	private readonly MissionNames _names;
	private readonly SelectionState _selection;
	private readonly PointMover _mover;
	private readonly RouteEditor _editor;
	private readonly RoutePlacing _placing;
	private readonly OrderEditor _orders;
	private readonly PanelLinks _links;
	private readonly PointSharers _sharers;
	private readonly Action<short> _tableLine;

	// The selected waypoint's point being typed.
	private readonly PointFields _point = new();

	/// <param name="orders">Gives the route to a group, as its first order's route.</param>
	/// <param name="links">Draws the links to the groups walking it and to what references it.</param>
	/// <param name="sharers">Lists what else stands on the selected waypoint's point.</param>
	/// <param name="tableLine">Draws, for a route GUID whose waypoints are an option of a random table, which one (<see cref="RandomTableView.DrawRouteTable"/>).</param>
	/// <param name="names">What the title names the route by.</param>
	public RouteView(MissionIndex index, MissionNames names, SelectionState selection, PointMover mover, RouteEditor editor,
			RoutePlacing placing, OrderEditor orders, PanelLinks links, PointSharers sharers, Action<short> tableLine) {
		_index = index;
		_names = names;
		_selection = selection;
		_mover = mover;
		_editor = editor;
		_placing = placing;
		_orders = orders;
		_links = links;
		_sharers = sharers;
		_tableLine = tableLine;
	}

	/// <summary>
	/// <paramref name="selected"/>'s view; its waypoints are the layout's (<see cref="MissionIndex"/>), which a drag's preview
	/// moves. A route none of whose waypoints stands anywhere shows its name, why, and Delete.
	/// </summary>
	public void Draw(RouteSelection selected) {
		var name = _names.Route(selected.Guid);
		EditorText.Wrapped(name.Text);
		if (name.Number is { } number) {
			ImGui.TextDisabled(number);
		}

		if (_index.Route(selected.Guid) is not { } placed) {
			ImGui.Separator();
			EditorText.Note("None of its waypoints references a position the mission has, so it stands nowhere and no group "
				+ "can walk it.");
			DrawDelete(selected.Guid);
			return;
		}

		ImGui.Separator();
		ImGui.Text(placed.Closed ? "Closed: a loop walked for ever" : "Open: walking it to the end completes the order");

		if (placed.WalkedBy.Count > 0) {
			ImGui.Text("Walked by:");
			foreach (short group in placed.WalkedBy) {
				ImGui.PushID($"walked{group}");
				_links.LayoutGroup(group);
				ImGui.PopID();
			}
		} else if (placed.NamedBy.Count > 0) {
			ImGui.TextWrapped("Walked by no group: only later order slots reference it, and a group only ever "
				+ "walks its first order's route.");
		} else {
			ImGui.TextWrapped("No order references it, so no group walks it.");
		}

		DrawWalkWith(placed);
		if (placed.NamedBy.Count > 0) {
			ImGui.Text("Referenced in a later order by:");
			foreach (short group in placed.NamedBy) {
				ImGui.PushID($"named{group}");
				_links.LayoutGroup(group);
				ImGui.PopID();
			}
		}

		ImGui.Separator();
		DrawWaypoints(placed, selected);
		DrawEdits(placed, selected.Guid, selected.Waypoint);
		DrawMove(placed, selected);
		DrawDelete(selected.Guid);
	}

	// A choice of the groups with a first order that walk another route, which makes this one that order's route: a group walks
	// only its first order's (docs/retail/simulation/ai-goals.md#the-route-cursor-is-loaded-once). What that does is on the closed
	// choice's hover.
	private void DrawWalkWith(PlannedRoute placed) {
		ImGui.SetNextItemWidth(-1f);
		if (!EditorText.BeginCombo("##walkWith", "Have a group walk it...")) {
			EditorText.Tooltip("It becomes that group's 1st order's route; a group with no position of its own then starts at its start.");
		} else {
			bool any = false;
			foreach (var group in _index.Groups) {
				if (placed.WalkedBy.Contains(group.Guid) || group.Orders.Count == 0 || group.Orders[0] == -1
						|| _orders.Slots(group.Guid) is not [{ Slot: 0 }, ..]) {
					continue;
				}

				any = true;
				if (NameText.Selectable(_links.Names.Group(group.Guid), $"walk{group.Guid}", false)) {
					_orders.RequestRoute(group.Guid, 0, placed.Guid);
				}
			}

			if (!any) {
				ImGui.TextDisabled("No other group has an order.");
			}

			ImGui.EndCombo();
		}

		EditorText.Reason(_orders.Failure);
	}

	// The waypoints, one selectable row each: "Start", then "Waypoint 2: 420 m from waypoint 1"; and, on the player's route,
	// that the cockpit numbers them the same way and how many the heads-down map marks.
	private void DrawWaypoints(PlannedRoute placed, RouteSelection selected) {
		int count = Count(placed);
		ImGui.Text($"Waypoints: {count}");
		for (int i = 0; i < count; i++) {
			string row = MissionText.LegFromPrevious(placed.Points, i) is { } leg
				? $"{MissionText.Waypoint(i)}: {leg}"
				: MissionText.Waypoint(i);
			if (ImGui.Selectable($"{row}##wp{i}", selected.Waypoint == i)) {
				_selection.Current = selected with { Waypoint = i };
			}
		}

		if (_index.PlayerRoute?.Guid != selected.Guid) {
			return;
		}

		EditorText.Note("The cockpit numbers these waypoints the same way: heading for Waypoint 2, the waypoint indicator "
			+ "reads WAYPOINT 2.");
		if (placed.Points.Count > MarkedWaypoints) {
			EditorText.Note($"The heads-down map marks waypoints 1 to {MarkedWaypoints - 1} of the player's route, and none after.");
		}
	}

	// Add waypoints, Insert after, Remove, and Close or Open the loop, of <route>, row-8 GUID <guid>.
	private void DrawEdits(PlannedRoute route, short guid, int waypoint) {
		ImGui.Separator();
		if (_editor.WhyFixed(guid) is { } reason) {
			EditorText.Reason(reason);
			return;
		}

		if (_editor.Shared(guid)) {
			_tableLine(guid);
		}

		if (_placing.IsAppendingTo(guid)) {
			EditorText.Note(RoutePlacing.Hint);
		}

		ImGui.BeginDisabled(_placing.Active);
		if (Button("Add waypoints", _placing.WhyNoStart)) {
			_placing.BeginAppend(guid);
		}

		ImGui.SameLine();
		bool closed = route.Closed;
		if (Button(closed ? "Open the loop" : "Close the loop", _editor.WhyNoClose(guid, !closed))) {
			_editor.RequestClosed(guid, !closed);
		}

		if (waypoint >= 0) {
			string which = MissionText.WaypointInSentence(waypoint);
			if (Button($"Insert after {which}", _editor.WhyNoInsert(guid, waypoint))) {
				_editor.RequestInsert(guid, waypoint);
			}

			ImGui.SameLine();
			if (Button($"Remove {which}", _editor.WhyNoRemove(guid, waypoint))) {
				_editor.RequestRemove(guid, waypoint);
			}
		} else {
			EditorText.Note("Select a waypoint to insert one after it or remove it.");
		}

		ImGui.EndDisabled();
		EditorText.Reason(_editor.Failure);
	}

	// The selected waypoint's X and Y as fields, which move it when one lets go as a plain drag of its move handles does,
	// its point with whatever else references it (PointMover); and what that moves with it.
	private void DrawMove(PlannedRoute placed, RouteSelection selected) {
		if (selected.Waypoint < 0 || selected.Waypoint >= Count(placed)) {
			return;
		}

		var target = _mover.TargetOf(selected) as WaypointTarget;
		var at = placed.Points[selected.Waypoint];
		ImGui.Separator();
		ImGui.Text($"{MissionText.Waypoint(selected.Waypoint)}, in world units:");
		ImGui.PushID("waypoint");
		var typed = _point.Draw((selected.Guid, selected.Waypoint), at, withZ: false, enabled: target != null && !_placing.Active);
		ImGui.PopID();
		if (typed is { } draft && target != null) {
			_mover.Commit(target, at, draft.X, draft.Y, 0);
		}

		PointFields.BoxWarning(_mover.LoadedBox, at);
		if (target is not { } waypoint) {
			return;
		}

		_sharers.Draw(_mover.SharersOf(waypoint));

		if (selected.Waypoint <= 1 && placed.WalkedBy.Any(group => _index.Group(group)?.Setup.PointSource == MissionGroupPointSource.RouteStart)) {
			EditorText.Note("A group with no position of its own stands on the start, facing Waypoint 1, so it moves and turns "
				+ "with these two.");
		}
	}

	// A Delete button, or why the route cannot be deleted, with what references it as links. The delete waits for the frame's
	// ImGui to be rendered (RouteEditor.RequestDelete).
	private void DrawDelete(short guid) {
		ImGui.Separator();
		var blockers = _editor.BlockersOf(guid);
		if (blockers.None) {
			ImGui.BeginDisabled(_placing.Active);
			if (ImGui.Button("Delete")) {
				_editor.RequestDelete(guid);
			}

			ImGui.EndDisabled();
			return;
		}

		if (blockers.Fixed is { } reason) {
			EditorText.Reason(reason, "Cannot be deleted: ");
			return;
		}

		ImGui.PushID("delete");
		ImGui.TextWrapped("Cannot be deleted while these reference it:");
		_links.References(blockers.Names, blockers.Groups);

		ImGui.PopID();
	}

	// How many waypoints the view lists: a closed route's last entry is its first again, and is listed once.
	private static int Count(PlannedRoute route) => route.Closed ? route.Points.Count - 1 : route.Points.Count;

	// A button, disabled with <reason> on hover when there is one.
	private static bool Button(string label, string? reason) {
		ImGui.BeginDisabled(reason != null);
		bool clicked = ImGui.Button(label);
		ImGui.EndDisabled();
		if (reason != null && ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) {
			ImGui.SetTooltip(reason);
		}

		return clicked;
	}
}
