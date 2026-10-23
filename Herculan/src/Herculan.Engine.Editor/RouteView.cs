using Herculan.Engine.World;
using ImGuiNET;

namespace Herculan.Engine.Editor;

/// <summary>
/// The Properties panel's view of a route: whether it is a loop, which groups walk it, its waypoints, and the edits that
/// make a route (<see cref="RouteEditor"/>, <see cref="RoutePlacing"/>): add waypoints after the last, insert one after the
/// selected one, remove the selected one, close or open the loop, delete the route; the selected waypoint's X and Y as
/// typed fields (<see cref="PointFields"/>); and, when its waypoints are an option of a random choice, which one, since an
/// edit changes that option (docs/herculan/handoff-editor-conditions.md#variants).
/// </summary>
internal sealed class RouteView {
	// The heads-down map marks the player's route from its second waypoint and at most nine of them
	// (docs/retail/simulation/heads-down-display.md#markers): waypoints past the tenth get no marker.
	private const int MarkedWaypoints = 10;

	private readonly GeneratedIndex _index;
	private readonly MissionNames _names;
	private readonly SelectionState _selection;
	private readonly PointMover _mover;
	private readonly RouteEditor _editor;
	private readonly RoutePlacing _placing;
	private readonly PanelLinks _links;
	private readonly PointSharers _sharers;
	private readonly Action<short> _choice;

	// The selected waypoint's point being typed.
	private readonly PointFields _point = new();

	/// <param name="links">Draws the links to the groups walking it and to what references it.</param>
	/// <param name="sharers">Lists what else stands on the selected waypoint's point.</param>
	/// <param name="choice">Draws, for a route GUID whose waypoints are an option of a random choice, which one (<see cref="VariantView.DrawRouteChoice"/>).</param>
	/// <param name="names">What the title names the route by.</param>
	public RouteView(GeneratedIndex index, MissionNames names, SelectionState selection, PointMover mover, RouteEditor editor,
			RoutePlacing placing, PanelLinks links, PointSharers sharers, Action<short> choice) {
		_index = index;
		_names = names;
		_selection = selection;
		_mover = mover;
		_editor = editor;
		_placing = placing;
		_links = links;
		_sharers = sharers;
		_choice = choice;
	}

	/// <summary>
	/// <paramref name="selected"/>'s view, the route generated at block-3 index <paramref name="reference"/>; its waypoints are
	/// the layout's (<see cref="MissionIndex"/>), which a drag's preview moves.
	/// </summary>
	public void Draw(RouteSelection selected, int reference) {
		if (_index.Route(reference) is not { } route || _index.Scene.Route(selected.Guid) is not { } placed) {
			return;
		}

		var name = _names.Route(selected.Guid);
		EditorText.Wrapped(name.Text);
		if (name.Number is { } number) {
			ImGui.TextDisabled(number);
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

		if (placed.NamedBy.Count > 0) {
			ImGui.Text("Referenced in a later order by:");
			foreach (short group in placed.NamedBy) {
				ImGui.PushID($"named{group}");
				_links.LayoutGroup(group);
				ImGui.PopID();
			}
		}

		ImGui.Separator();
		int count = placed.Closed ? placed.Points.Count - 1 : placed.Points.Count;
		ImGui.Text($"Waypoints: {count}");
		for (int i = 0; i < count; i++) {
			var point = placed.Points[i];
			bool isSelected = selected.Waypoint == i;
			if (ImGui.Selectable($"{i}: {point.X}, {point.Y}##wp{i}", isSelected)) {
				_selection.Current = selected with { Waypoint = i };
			}
		}

		if (_index.Scene.PlayerRoute?.Guid == selected.Guid && placed.Points.Count > MarkedWaypoints) {
			EditorText.Note($"The heads-down map marks waypoints 1 to {MarkedWaypoints - 1} of the player's route, and none after.");
		}

		DrawEdits(placed, selected.Guid, selected.Waypoint);
		DrawMove(route, placed, selected);
		DrawDelete(selected.Guid);
	}

	// Add waypoints, Insert after, Remove, and Close or Open the loop, of <route>, row-8 GUID <guid>.
	private void DrawEdits(PlannedRoute route, short guid, int waypoint) {
		ImGui.Separator();
		if (_editor.WhyFixed(guid) is { } reason) {
			EditorText.Reason(reason);
			return;
		}

		if (_editor.Shared(guid)) {
			_choice(guid);
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
			if (Button($"Insert after {waypoint}", _editor.WhyNoInsert(guid, waypoint))) {
				_editor.RequestInsert(guid, waypoint);
			}

			ImGui.SameLine();
			if (Button($"Remove {waypoint}", _editor.WhyNoRemove(guid, waypoint))) {
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
	private void DrawMove(EditorRoute route, PlannedRoute placed, RouteSelection selected) {
		int count = placed.Closed ? placed.Points.Count - 1 : placed.Points.Count;
		if (selected.Waypoint < 0 || selected.Waypoint >= count) {
			return;
		}

		var target = _mover.TargetOf(selected) as WaypointTarget;
		var at = placed.Points[selected.Waypoint];
		ImGui.Separator();
		ImGui.Text($"Waypoint {selected.Waypoint}, in world units:");
		ImGui.PushID("waypoint");
		var typed = _point.Draw((route.Reference, selected.Waypoint), at, withZ: false, enabled: target != null && !_placing.Active);
		ImGui.PopID();
		if (typed is { } draft && target != null) {
			_mover.Commit(target, at, draft.X, draft.Y, 0);
		}

		PointFields.BoxWarning(_mover.LoadedBox, at);
		if (target is not { } waypoint) {
			return;
		}

		_sharers.Draw(_mover.SharersOf(waypoint));

		if (selected.Waypoint <= 1 && placed.WalkedBy.Any(group => _index.Scene.Group(group)?.Setup.PointSource == MissionGroupPointSource.RouteStart)) {
			ImGui.TextWrapped("A group with no point of its own starts on the first waypoint, facing along the first "
				+ "leg, and moves and turns with them.");
		}
	}

	// A Delete button, or why the route cannot be deleted, with what names it as links. The delete waits for the frame's
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
