using Herculan.Engine.World;
using ImGuiNET;

namespace Herculan.Engine.Editor;

/// <summary>
/// The Properties panel's view of a route: whether it is a loop, which groups walk it, its waypoints, and the edits that
/// make a route (<see cref="RouteEditor"/>, <see cref="RoutePlacing"/>): add waypoints after the last, insert one after the
/// selected one, remove the selected one, close or open the loop, delete the route; and the selected waypoint's X and Y as
/// typed fields (<see cref="PointFields"/>).
/// </summary>
internal sealed class RouteView {
	// The heads-down map marks the player's route from its second waypoint and at most nine of them
	// (docs/retail/simulation/heads-down-display.md#markers): waypoints past the tenth get no marker.
	private const int MarkedWaypoints = 10;

	private readonly MissionIndex _index;
	private readonly SelectionState _selection;
	private readonly PointMover _mover;
	private readonly RouteEditor _editor;
	private readonly RoutePlacing _placing;
	private readonly Action<int> _groupLink;
	private readonly Action<DeleteNamers, IReadOnlyList<int>> _references;

	// The selected waypoint's point being typed.
	private readonly PointFields _point = new();

	/// <param name="groupLink">Draws a link that selects a group.</param>
	/// <param name="references">Draws what references something, one line per record, given the groups whose own route field references it.</param>
	public RouteView(MissionIndex index, SelectionState selection, PointMover mover, RouteEditor editor, RoutePlacing placing,
			Action<int> groupLink, Action<DeleteNamers, IReadOnlyList<int>> references) {
		_index = index;
		_selection = selection;
		_mover = mover;
		_editor = editor;
		_placing = placing;
		_groupLink = groupLink;
		_references = references;
	}

	public void Draw(RouteSelection selected) {
		if (_index.Route(selected.Route) is not { } route) {
			return;
		}

		ImGui.Text($"Route {route.Reference}");
		ImGui.Separator();
		ImGui.Text(route.Closed ? "Closed: a loop walked for ever" : "Open: walking it to the end completes the order");

		if (route.WalkedBy.Count > 0) {
			ImGui.Text("Walked by:");
			foreach (int group in route.WalkedBy) {
				_groupLink(group);
			}
		} else if (route.InAnOrder) {
			ImGui.TextWrapped("Walked by no group: only later order slots reference it, and a group only ever "
				+ "walks its first order's route.");
		} else {
			ImGui.TextWrapped("No order references it, so no group walks it.");
		}

		if (route.NamedBy.Count > 0) {
			ImGui.Text("Referenced in a later order by:");
			foreach (int group in route.NamedBy) {
				_groupLink(group);
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

		if (route.WalkedBy.Contains(0) && route.Points.Count > MarkedWaypoints) {
			EditorText.Note($"The heads-down map marks waypoints 1 to {MarkedWaypoints - 1} of the player's route, and none after.");
		}

		DrawEdits(route, selected.Waypoint);
		DrawMove(route, selected);
		DrawDelete(route);
	}

	// Add waypoints, Insert after, Remove, and Close or Open the loop.
	private void DrawEdits(EditorRoute route, int waypoint) {
		ImGui.Separator();
		if (_editor.WhyFixed(route.Reference) is { } reason) {
			EditorText.Reason(reason);
			return;
		}

		if (_editor.Shared(route.Reference)) {
			EditorText.Note("Its waypoint list is a variant's, shared by every route that can draw it: an edit here changes them all.");
		}

		if (_placing.IsAppendingTo(route.Reference)) {
			EditorText.Note(RoutePlacing.Hint);
		}

		ImGui.BeginDisabled(_placing.Active);
		if (Button("Add waypoints", _placing.WhyNoStart)) {
			_placing.BeginAppend(route.Reference);
		}

		ImGui.SameLine();
		bool closed = route.Closed;
		if (Button(closed ? "Open the loop" : "Close the loop", _editor.WhyNoClose(route.Reference, !closed))) {
			_editor.RequestClosed(route.Reference, !closed);
		}

		if (waypoint >= 0) {
			if (Button($"Insert after {waypoint}", _editor.WhyNoInsert(route.Reference, waypoint))) {
				_editor.RequestInsert(route.Reference, waypoint);
			}

			ImGui.SameLine();
			if (Button($"Remove {waypoint}", _editor.WhyNoRemove(route.Reference, waypoint))) {
				_editor.RequestRemove(route.Reference, waypoint);
			}
		} else {
			EditorText.Note("Select a waypoint to insert one after it or remove it.");
		}

		ImGui.EndDisabled();
		EditorText.Reason(_editor.Failure);
	}

	// The selected waypoint's X and Y as fields, which move it when one lets go as a plain drag of its move handles does,
	// its point with whatever else references it (PointMover); and what that moves with it.
	private void DrawMove(EditorRoute route, RouteSelection selected) {
		int count = route.Closed ? route.Points.Count - 1 : route.Points.Count;
		if (selected.Waypoint < 0 || selected.Waypoint >= count) {
			return;
		}

		var target = _mover.TargetOf(selected) as WaypointTarget;
		var at = route.Points[selected.Waypoint];
		ImGui.Separator();
		ImGui.Text($"Waypoint {selected.Waypoint}, in world units:");
		ImGui.PushID("waypoint");
		var typed = _point.Draw((route.Reference, selected.Waypoint), at, withZ: false, enabled: target != null && !_placing.Active);
		ImGui.PopID();
		if (typed is { } draft && target != null) {
			_mover.Commit(target, at, draft.X, draft.Y, 0);
		}

		PointFields.BoxWarning(_mover.LoadedBox, route.Points[selected.Waypoint]);
		if (target is not { } waypoint) {
			return;
		}

		int sharers = _mover.SharersOf(waypoint);
		ImGui.TextWrapped(sharers > 0
			? $"Its point is referenced by {sharers} other record(s) (trigger areas, groups, other waypoints), which move "
				+ "with it. Alt+drag moves the waypoint alone."
			: "Nothing else references its point.");

		if (selected.Waypoint <= 1 && route.WalkedBy.Any(group => _index.SetupOf(group)?.PointSource == MissionGroupPointSource.RouteStart)) {
			ImGui.TextWrapped("A group with no point of its own starts on the first waypoint, facing along the first "
				+ "leg, and moves and turns with them.");
		}
	}

	// A Delete button, or why the route cannot be deleted, with what names it as links. The delete waits for the frame's
	// ImGui to be rendered (RouteEditor.RequestDelete).
	private void DrawDelete(EditorRoute route) {
		ImGui.Separator();
		var blockers = _editor.BlockersOf(route.Reference);
		if (blockers.None) {
			ImGui.BeginDisabled(_placing.Active);
			if (ImGui.Button("Delete")) {
				_editor.RequestDelete(route.Reference);
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
		_references(blockers.Names, blockers.Groups);

		ImGui.PopID();
	}

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
