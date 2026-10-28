using HercWorks.Core.Data.File.Msn;
using Herculan.Engine.Numerics;
using Herculan.Engine.World;

namespace Herculan.Engine.Editor;

/// <summary>
/// Waypoint <paramref name="Index"/> of the route of row-8 GUID <paramref name="Route"/>, by its index in
/// <see cref="PlannedRoute.Points"/>. <paramref name="WithLast"/> when it is a closed route's first waypoint, whose last
/// waypoint moves with it so the route stays closed (<see cref="PlannedRoute.Closed"/>). <paramref name="Detach"/> when the
/// move gives it a point of its own rather than moving the point it names.
/// </summary>
internal sealed record WaypointTarget(short Route, int Index, bool WithLast, bool Detach = false) : MoveTarget;

/// <summary>
/// Corner <paramref name="Corner"/> of the trigger area of row-9 GUID <paramref name="Area"/>: 0 a box's first corner or a
/// circle's centre (row 9's <c>0x08</c>), 1 a box's second corner (<c>0x0A</c>). <paramref name="Detach"/> when the move gives
/// the corner a point of its own rather than moving the point it names.
/// </summary>
internal sealed record AreaCornerTarget(short Area, int Corner, bool Detach = false) : MoveTarget;

/// <summary>
/// The radius of the circle trigger area of row-9 GUID <paramref name="Area"/>, dragged by a point on its edge: the radius is
/// that point's ground-plane distance from the centre.
/// </summary>
internal sealed record AreaRadiusTarget(short Area) : MoveTarget;

/// <summary>
/// <see cref="PointMover"/>'s waypoint, area corner and radius moves, by GUID, over the routes and areas as the layout places
/// them (<see cref="MissionIndex"/>): what a selection offers to move, what shares a moved point, the overrides a drag's
/// preview is placed with (<see cref="PlacementOverrides"/>), and what a commit writes into the <c>.MSN</c>
/// (docs/herculan/handoff-editor-document.md#the-tools).
///
/// <para>A waypoint moves the point it names, so everything else naming it moves too: retail missions share a point between
/// a waypoint and the trigger area that fires on reaching it, and between a route's first waypoint and the groups standing
/// on it. A detached move (<see cref="WaypointTarget.Detach"/>) gives it a point of its own instead, and its preview moves
/// the route's line alone. Either way its point is written with Z 0, as all but one waypoint in the retail missions has it;
/// the simulator reads a waypoint's Z only as the starting altitude of a flight standing on its route's first waypoint and
/// for the heads-down map's route icons (docs/retail/simulation/ai-goals.md#the-route-cursor-is-loaded-once).</para>
///
/// <para>A trigger area's corner, or a circle's centre, moves as a waypoint does: the point it names moves, and whatever
/// shares it, or a detached move gives it a point of its own, which its row-9 record then names
/// (<see cref="MissionDocument.SetArea"/>); its point is written with Z 0, which the area test ignores
/// (docs/retail/simulation/mission-deployment.md#the-areas--block-4-resolved-by-triggerarea_resolve-00423358). A circle's
/// radius writes row 9's stored radius (<see cref="TriggerAreaEditing.SetRadius"/>), and its preview shows the radius that
/// will be stored, in tens of world units.</para>
/// </summary>
internal sealed class FeatureMoves {
	private const int RouteRow = 8;
	private const int AreaRow = 9;

	private readonly MissionDocument _source;
	private readonly MissionIndex _index;

	public FeatureMoves(MissionDocument source, MissionIndex index) {
		_source = source;
		_index = index;
	}

	/// <summary>Whether <paramref name="target"/> is a waypoint, an area corner or a radius, which these moves are.</summary>
	public static bool Moves(MoveTarget target) => target is WaypointTarget or AreaCornerTarget or AreaRadiusTarget;

	/// <summary>The waypoint <paramref name="selection"/> names, or null when it names none the layout places.</summary>
	public WaypointTarget? TargetOf(RouteSelection selection) =>
		selection.Waypoint >= 0 && _index.Route(selection.Guid) is { } route && selection.Waypoint < route.Points.Count
			? new WaypointTarget(selection.Guid, selection.Waypoint, selection.Waypoint == 0 && route.Closed)
			: null;

	/// <summary>
	/// What of trigger area <paramref name="area"/> the move handles drag: a box's corners, or a circle's radius and centre,
	/// the radius first, so its small handle wins where the centre's arrow crosses it; none when it cannot be moved.
	/// </summary>
	public IReadOnlyList<MoveTarget> TargetsOf(short area) {
		if (WhyFixedArea(area) != null || _index.Area(area) is not { } placed) {
			return [];
		}

		return placed.Area.Shape == MissionTriggerShape.Circle
			? [new AreaRadiusTarget(area), new AreaCornerTarget(area, 0)]
			: [new AreaCornerTarget(area, 0), new AreaCornerTarget(area, 1)];
	}

	/// <summary>Why a trigger area the selection names is not edited once the <c>.MSN</c> has no such area.</summary>
	public const string AreaGone = "Not found.";

	/// <summary>Why trigger area <paramref name="area"/>, by its row-9 GUID, cannot be moved or edited, or null when it can.</summary>
	public string? WhyFixedArea(short area) =>
		_index.Area(area) != null ? null
		: _source.Layout.Entry(AreaRow, area) == null ? AreaGone
		: "A corner or its center references a position the mission does not have, so it cannot be moved or reshaped.";

	/// <summary>Why <paramref name="target"/> cannot be moved, or null when it can.</summary>
	public string? WhyFixed(MoveTarget target) => target switch {
		WaypointTarget waypoint => _index.Route(waypoint.Route) is { } route && waypoint.Index < route.Points.Count
			? null
			: RouteEditor.Gone,
		AreaCornerTarget corner => WhyFixedArea(corner.Area),
		AreaRadiusTarget radius => WhyFixedArea(radius.Area),
		_ => throw new ArgumentOutOfRangeException(nameof(target))
	};

	/// <summary>
	/// The other fields naming the points <paramref name="target"/>'s waypoint names (trigger areas, groups, other waypoints),
	/// which move with it unless it is detached: every field naming them but the waypoint's own entries, in the route's
	/// record or in the template of a random table option that supplies its list.
	/// </summary>
	public IReadOnlyList<PointRef> SharersOf(WaypointTarget target) {
		if (WhyFixed(target) != null) {
			return [];
		}

		var entries = EntriesOf(target);
		var list = _source.Layout.Entry(RouteRow, target.Route)?.Supplier(MergeRules.RouteListWord);
		bool Own(PointRef reference) => reference.Record.Row == RouteRow && entries.Contains(reference.Field)
			&& (reference.Record == list?.CopiedFrom || _source.RecordAt(reference.Record)?.GUID == target.Route);
		return PointsOf(target).Distinct().SelectMany(_source.PointRefs).Where(reference => !Own(reference)).ToList();
	}

	/// <summary>
	/// The other fields naming the point <paramref name="target"/>'s corner names (waypoints, other areas, groups), which move
	/// with it unless it is detached: every field naming it but that corner of the area's own records.
	/// </summary>
	public IReadOnlyList<PointRef> SharersOf(AreaCornerTarget target) {
		if (WhyFixed(target) != null || PointsOf(target) is not [var point]) {
			return [];
		}

		return _source.PointRefs(point).Where(reference => reference.Record.Row != AreaRow || reference.Field != target.Corner
			|| _source.RecordAt(reference.Record)?.GUID != target.Area).ToList();
	}

	/// <summary>Where <paramref name="target"/> stands now, in world units, a drag's preview included.</summary>
	public Vec3i PositionOf(MoveTarget target) => target switch {
		WaypointTarget waypoint when _index.Route(waypoint.Route) is { } route => route.Points[waypoint.Index],
		AreaCornerTarget corner when _index.Area(corner.Area)?.Area is { } area =>
			corner.Corner == 0 ? new Vec3i(area.A.X, area.A.Y, 0) : new Vec3i(area.B.X, area.B.Y, 0),
		AreaRadiusTarget radius when _index.Area(radius.Area)?.Area is { } area => new Vec3i(area.A.X + area.Radius, area.A.Y, 0),
		_ => throw new ArgumentOutOfRangeException(nameof(target))
	};

	/// <summary>
	/// Where a drag of <paramref name="target"/> to <paramref name="position"/> stands what it moves before it lets go: the
	/// points it names, with whatever shares them; the waypoint or corner alone on a point of its own when it is detached; or
	/// the circle at the radius that would be stored.
	/// </summary>
	public PlacementOverrides Overrides(MoveTarget target, Vec3i position) => target switch {
		WaypointTarget { Detach: true } waypoint => new PlacementOverrides { DetachedWaypoint = (waypoint.Route, EntriesOf(waypoint), position) },
		AreaCornerTarget { Detach: true } corner => new PlacementOverrides { DetachedCorner = (corner.Area, corner.Corner, position) },
		WaypointTarget or AreaCornerTarget => new PlacementOverrides {
			Points = PointsOf(target).Distinct().ToDictionary(point => point, _ => position)
		},
		AreaRadiusTarget radius => new PlacementOverrides {
			Radius = (radius.Area, TriggerAreaEditing.Radius(TriggerAreaEditing.StoredRadius(RadiusAt(radius, position))))
		},
		_ => throw new ArgumentOutOfRangeException(nameof(target))
	};

	/// <summary>Whether a radius handle at <paramref name="position"/> gives the radius already stored, so nothing is written.</summary>
	public bool Unchanged(AreaRadiusTarget target, Vec3i position) =>
		_source.Layout.Entry<TriggerArea12>(target.Area)?.Fields is { } record
		&& TriggerAreaEditing.StoredRadius(RadiusAt(target, position)) == record.SecondPointOrRadius;

	/// <summary>
	/// The radius, in world units, a radius handle at <paramref name="position"/> gives: its ground-plane distance from the
	/// centre.
	/// </summary>
	public int RadiusAt(AreaRadiusTarget target, Vec3i position) {
		var centre = _index.Area(target.Area)?.Area.A ?? position;
		double dx = (double)position.X - centre.X, dy = (double)position.Y - centre.Y;
		return (int)Math.Min(Math.Round(Math.Sqrt(dx * dx + dy * dy)), int.MaxValue);
	}

	/// <summary>
	/// Writes a move of <paramref name="target"/> to <paramref name="position"/> into the <c>.MSN</c>, inside one
	/// <see cref="MissionDocument.Change"/>: the points a waypoint or a corner names; a circle's stored radius; or a point of
	/// its own for a detached waypoint or corner.
	/// </summary>
	public void Write(MoveTarget target, Vec3i position) {
		switch (target) {
			case WaypointTarget { Detach: false } or AreaCornerTarget { Detach: false }:
				foreach (short point in PointsOf(target).Distinct()) {
					_source.SetPoint(point, position.X, position.Y, position.Z);
				}

				break;
			case AreaRadiusTarget radius:
				TriggerAreaEditing.SetRadius(_source, radius.Area, RadiusAt(radius, position));
				break;
			default:
				GiveOwnPoint(target, position);
				break;
		}
	}

	/// <summary>What a failed move of <paramref name="target"/> is called on the console.</summary>
	public string Describe(MoveTarget target) => target switch {
		WaypointTarget waypoint => $"{MissionText.WaypointInSentence(waypoint.Index)} of route {_index.Route(waypoint.Route)?.Number}",
		AreaCornerTarget corner => $"corner {corner.Corner + 1} of trigger area {_index.Area(corner.Area)?.Number}",
		AreaRadiusTarget radius => $"the radius of trigger area {_index.Area(radius.Area)?.Number}",
		_ => target.ToString()
	};

	// Gives a detached waypoint or corner a point of its own at position, and points its field at it: the route's list
	// entries, or the area's corner. A closed route's first and last waypoints name one point between them more often than
	// not, and move as one. A list a template supplies names points every route picking that template shares, so none of its
	// namers counts as moving and a point is appended.
	private void GiveOwnPoint(MoveTarget target, Vec3i position) {
		short[] points = PointsOf(target);
		int moving = target is WaypointTarget { Route: var shared }
			&& _source.Layout.Entry(RouteRow, shared)?.Supplier(MergeRules.RouteListWord).CopiedFrom != null ? 0 : 1;
		short[] placed;
		if (points.Length > 1 && points.All(point => point == points[0])) {
			_source.PlacePoint(points[0], moving * points.Length, position.X, position.Y, position.Z, out short one);
			placed = Array.ConvertAll(points, _ => one);
		} else {
			placed = new short[points.Length];
			for (int i = 0; i < points.Length; i++) {
				_source.PlacePoint(points[i], moving, position.X, position.Y, position.Z, out placed[i]);
			}
		}

		switch (target) {
			case WaypointTarget waypoint:
				var entries = EntriesOf(waypoint);
				for (int i = 0; i < entries.Count; i++) {
					if (placed[i] != points[i]) {
						_source.SetWaypoint(waypoint.Route, entries[i], placed[i]);
					}
				}

				break;
			case AreaCornerTarget corner when placed[0] != points[0]:
				short own = placed[0];
				_source.SetArea(corner.Area, record => {
					if (corner.Corner == 0) {
						record.PointRef = own;
					} else {
						record.SecondPointOrRadius = own;
					}
				});
				break;
		}
	}

	// The entries of the route's list a waypoint stands for: its own, and for a closed route's first waypoint, which is also
	// its last, the last's too.
	private IReadOnlyList<int> EntriesOf(WaypointTarget waypoint) {
		var entries = _index.Route(waypoint.Route)?.Entries ?? throw new InvalidOperationException(RouteEditor.Gone);
		return waypoint.WithLast ? [entries[0], entries[^1]] : [entries[waypoint.Index]];
	}

	// The row-6 GUIDs a waypoint's entries or a corner's field name.
	private short[] PointsOf(MoveTarget target) {
		switch (target) {
			case WaypointTarget waypoint:
				var guids = _index.Route(waypoint.Route)?.PointGuids ?? throw new InvalidOperationException(RouteEditor.Gone);
				return waypoint.WithLast ? [guids[0], guids[^1]] : [guids[waypoint.Index]];
			case AreaCornerTarget corner:
				var area = _index.Area(corner.Area) ?? throw new InvalidOperationException(AreaGone);
				return corner.Corner < area.PointGuids.Count ? [area.PointGuids[corner.Corner]] : [];
			default:
				throw new ArgumentOutOfRangeException(nameof(target));
		}
	}
}
