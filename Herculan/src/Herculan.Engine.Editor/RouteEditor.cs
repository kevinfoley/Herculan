using HercWorks.Core.Data.File.Msn;
using Herculan.Engine.Numerics;

namespace Herculan.Engine.Editor;

/// <summary>
/// What keeps a route from being deleted (<see cref="RouteEditor.BlockersOf"/>). <see cref="Fixed"/> is why it cannot be
/// deleted at all; <see cref="Names"/> the orders and objectives referencing it, and <see cref="Groups"/> the row-16 GUIDs of
/// the groups whose own route field references it.
/// </summary>
internal sealed record RouteDeleteBlockers(string? Fixed, DeleteNamers Names, IReadOnlyList<short> Groups) {
	/// <summary>Whether nothing does, and the route can be deleted.</summary>
	public bool None => Fixed == null && !Names.Any && Groups.Count == 0;

	public static RouteDeleteBlockers Because(string reason) => new(reason, DeleteNamers.None, []);
}

/// <summary>
/// Creates, edits and deletes the open mission's routes, each edit one step of <see cref="PointMover"/>'s undo history,
/// through <see cref="RouteEditing"/>'s record edits on the mission's <c>.MSN</c>; the scene and the indices are then synced
/// with the document (<see cref="SceneSync"/>), as a new route's records are exported last and a delete moves
/// every later route, and every later point when a point goes with it, down one
/// (docs/herculan/handoff-editor-document.md#the-tools).
///
/// <para>Routes are taken by their row-8 GUID, as the selections name them, and a route not shown in the editor is not
/// edited. A waypoint here is one the route view lists, by its index in <see cref="PlannedRoute.Points"/>; it is mapped to
/// the entry of the <c>.MSN</c> list it comes from (<see cref="PlannedRoute.Entries"/>). A route none of whose waypoints
/// names a point the layout has is not edited.</para>
///
/// <para>The view's edits wait for the frame's ImGui to be rendered (<see cref="RunRequest"/>), since the rebuild changes
/// what the panels list; <see cref="RoutePlacing"/> calls <see cref="Create"/> and <see cref="Extend"/> at the same
/// point of the frame.</para>
/// </summary>
internal sealed class RouteEditor {
	private readonly MissionDocument _source;
	private readonly GeneratedIndex _index;
	private readonly PointMover _mover;
	private readonly SceneSync _sync;
	private readonly SelectionState _selection;
	private readonly ObjectDeleter _deleter;
	private Func<string?>? _requested;

	public RouteEditor(MissionDocument source, GeneratedIndex index, PointMover mover, SceneSync sync,
			SelectionState selection, ObjectDeleter deleter) {
		_source = source;
		_index = index;
		_mover = mover;
		_sync = sync;
		_selection = selection;
		_deleter = deleter;
	}

	/// <summary>Why the last edit run could not be made, or null.</summary>
	public string? Failure { get; private set; }

	/// <summary>Why row-8 GUID <paramref name="route"/>'s waypoints cannot be edited, or null when they can.</summary>
	public string? WhyFixed(short route) {
		if (_index.Records.Route(route) == null) {
			return GeneratedRecords.NotShown;
		}

		return _index.Scene.Route(route) == null
			? "Some of its waypoints reference points the mission does not have, so they cannot be edited one by one."
			: null;
	}

	/// <summary>Whether <paramref name="route"/>'s list is a variant's, shared by every route that can draw it.</summary>
	public bool Shared(short route) => WhyFixed(route) == null && _source.WaypointsShared(route);

	/// <summary>Why no waypoint can be inserted after waypoint <paramref name="waypoint"/> of <paramref name="route"/>, or null.</summary>
	public string? WhyNoInsert(short route, int waypoint) =>
		WhyFixed(route) ?? RouteEditing.WhyNoInsert(_source, route, Entry(route, waypoint));

	/// <summary>Why waypoint <paramref name="waypoint"/> of <paramref name="route"/> cannot be removed, or null.</summary>
	public string? WhyNoRemove(short route, int waypoint) =>
		WhyFixed(route) ?? RouteEditing.WhyNoRemove(_source, route, Entry(route, waypoint));

	/// <summary>Why <paramref name="route"/> cannot be closed (<paramref name="closed"/>) or opened, or null.</summary>
	public string? WhyNoClose(short route, bool closed) => WhyFixed(route) ?? RouteEditing.WhyNoClose(_source, route, closed);

	/// <summary>What keeps <paramref name="route"/> from being deleted; <see cref="RouteDeleteBlockers.None"/> when nothing does.</summary>
	public RouteDeleteBlockers BlockersOf(short route) {
		var source = _source;
		if (_index.Records.Route(route) == null) {
			return RouteDeleteBlockers.Because(GeneratedRecords.NotShown);
		}

		var namers = RouteEditing.Namers(source, route);
		var names = _deleter.Resolve(source, new MsnNamers(namers.Objectives, [], namers.Orders));
		var groups = namers.Groups.Select(record => source.RecordAt(record)?.GUID ?? -1).Where(group => group != -1).Distinct().ToList();
		return new RouteDeleteBlockers(null, names, groups);
	}

	/// <summary>Asks for a waypoint halfway along the leg after waypoint <paramref name="waypoint"/> of <paramref name="route"/>.</summary>
	public void RequestInsert(short route, int waypoint) {
		int entry = Entry(route, waypoint);
		_requested = () => WhyNoInsert(route, waypoint)
			?? Commit(() => RouteEditing.InsertAfter(_source, route, entry), route, waypoint + 1);
	}

	/// <summary>Asks for waypoint <paramref name="waypoint"/> of <paramref name="route"/> to be removed.</summary>
	public void RequestRemove(short route, int waypoint) {
		int entry = Entry(route, waypoint);
		_requested = () => WhyNoRemove(route, waypoint)
			?? Commit(() => RouteEditing.RemoveAt(_source, route, entry), route, -1);
	}

	/// <summary>Asks for <paramref name="route"/> to be closed into a loop, or opened.</summary>
	public void RequestClosed(short route, bool closed) =>
		_requested = () => WhyNoClose(route, closed) ?? Commit(() => RouteEditing.SetClosed(_source, route, closed), route, -1);

	/// <summary>Asks for <paramref name="route"/> to be deleted.</summary>
	public void RequestDelete(short route) {
		_requested = () => {
			var blockers = BlockersOf(route);
			if (!blockers.None) {
				return blockers.Fixed ?? "The mission references it; its view says what.";
			}

			return Commit(() => RouteEditing.Delete(_source, route), -1, -1);
		};
	}

	/// <summary>Makes the edit asked for since the last call, if any; <see cref="Failure"/> says why one could not be made.</summary>
	public void RunRequest() {
		if (_requested is { } edit) {
			_requested = null;
			Failure = edit();
		}
	}

	/// <summary>
	/// Adds a route through <paramref name="waypoints"/>, closed when <paramref name="closed"/>, and selects it; with fewer
	/// than <see cref="RouteEditing.MinWaypoints"/> waypoints, adds nothing. Answers why it could not, or null.
	/// </summary>
	public string? Create(IReadOnlyList<Vec3i> waypoints, bool closed) {
		if (waypoints.Count < RouteEditing.MinWaypoints) {
			return null;
		}

		var source = _source;
		var at = waypoints.Select(point => (point.X, point.Y)).ToList();
		short route = -1;
		return Commit(() => RouteEditing.Create(source, at, closed, out route), () => route, -1);
	}

	/// <summary>
	/// Adds <paramref name="waypoints"/> after the last waypoint of row-8 GUID <paramref name="route"/>, closing an open
	/// route after them when <paramref name="close"/>, and selects it. Answers why it could not, or null; nothing to add is
	/// not a failure.
	/// </summary>
	public string? Extend(short route, IReadOnlyList<Vec3i> waypoints, bool close) {
		var source = _source;
		if (_index.Records.Route(route) == null) {
			return "The route is no longer in the mission.";
		}

		if (waypoints.Count == 0 && (!close || RouteEditing.IsClosed(source.Waypoints(route)))) {
			return null;
		}

		var at = waypoints.Select(point => (point.X, point.Y)).ToList();
		return Commit(() => RouteEditing.Extend(source, route, at, close), route, -1);
	}

	// The entry of <route>'s list waypoint <waypoint> comes from, or -1 when it has none.
	private int Entry(short route, int waypoint) =>
		_index.Scene.Route(route)?.Entries is { } entries && waypoint >= 0 && waypoint < entries.Count ? entries[waypoint] : -1;

	private string? Commit(Func<MsnEdit> edit, short route, int waypoint) => Commit(edit, () => route, waypoint);

	// Makes the change, rebuilding from it, records it as one step of the history and selects the route, at <waypoint>
	// while the route view lists one there; a route that is gone (-1) leaves the selection to the rebuild.
	private string? Commit(Func<MsnEdit> edit, Func<short> route, int waypoint) {
		MsnEdit change;
		try {
			change = _sync.Run(edit);
		} catch (InvalidOperationException ex) {
			return ex.Message;
		}

		_mover.Record(new RecordsEdit(_sync, change));
		if (route() is not -1 and var guid) {
			bool listed = waypoint < _index.Scene.Route(guid)?.Points.Count;
			_selection.Current = new RouteSelection(guid, listed ? waypoint : -1);
		}

		return null;
	}
}
