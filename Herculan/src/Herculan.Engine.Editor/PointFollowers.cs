using HercWorks.Core.Data.File.Msn;
using HercWorks.Core.Data.File.Msn.Script;
using Herculan.Engine.Numerics;
using Herculan.Engine.Scene;
using Herculan.Engine.Sim;
using Herculan.Engine.Terrain;
using Herculan.Engine.World;

namespace Herculan.Engine.Editor;

/// <summary>
/// Everything in the editor's scene that stands on a <c>script.dat</c> block-1 point, placed again from the document
/// after its points change (<see cref="Sync"/>), so that whatever shares a moved point moves with it: route waypoints,
/// trigger areas, each group's point and heading with the members spread from them and a paints-ground group's pad,
/// and objects whose roster record names a point of its own. Each is placed by the loader's rules
/// (docs/retail/formats/script-dat.md#placement--the-actual-rule), and only what has changed is touched.
///
/// <para>Not placed here: the ground under a moved structure or pad and a pad's paint, which are
/// <see cref="TerrainLeveller"/>'s; and an order's or an objective's own point, which the editor does not draw.</para>
/// </summary>
internal sealed class PointFollowers {
	private readonly ScriptDat _document;
	private readonly HeightGrid _terrain;
	private readonly MissionIndex _index;
	private readonly MissionOverlay _overlay;
	private readonly Action<SceneObject, Vec3i, int> _place;
	private readonly Dictionary<int, int[]> _waypointEntries = new();
	private Vec3i[] _synced;

	/// <param name="place">Stands an object at a position and heading, for whatever draws and picks it.</param>
	public PointFollowers(ScriptDat document, MissionScene scene, MissionIndex index, MissionOverlay overlay,
			Action<SceneObject, Vec3i, int> place) {
		_document = document;
		_terrain = scene.World.Terrain;
		_index = index;
		_overlay = overlay;
		_place = place;

		// Which entry of each route's block-3 list each of its resolved waypoints came from: the load drops a ref past
		// block 1's end, so the two can differ. Taken before an edit appends a point such a ref would then name.
		foreach (var route in index.Routes) {
			if (route.Reference >= document.WaypointGroups.Length) {
				continue;
			}

			var refs = document.WaypointGroups[route.Reference].Waypoints;
			int[] entries = Enumerable.Range(0, refs.Length)
				.Where(i => refs[i] >= 0 && refs[i] < document.Coordinates.Length)
				.ToArray();
			if (entries.Length == route.Points.Count) {
				_waypointEntries[route.Reference] = entries;
			}
		}

		_synced = Points();
	}

	/// <summary>The block-3 entries <paramref name="route"/>'s waypoints came from, in order, or null when they cannot be told.</summary>
	public int[]? WaypointEntries(int route) => _waypointEntries.GetValueOrDefault(route);

	/// <summary>
	/// Where the simulator stands <paramref name="sceneObject"/> for a point of its own at (<paramref name="x"/>,
	/// <paramref name="y"/>, <paramref name="z"/>): <paramref name="z"/> is read for a flyer alone (rule 5).
	/// </summary>
	public static Vec3i Standing(HeightGrid terrain, SceneObject sceneObject, int x, int y, int z) => sceneObject.Object switch {
		FlyerObject => new Vec3i(x, y, z == 0 ? FlyerObject.DefaultHoverHeight : z),
		MechObject mech => new Vec3i(x, y, MissionScene.MechSpawnHeight(terrain, mech, new Vec3i(x, y, 0))),
		_ => new Vec3i(x, y, terrain.HeightAtWorld(x, y))
	};

	/// <summary>
	/// Where the editor draws <paramref name="simObject"/>, spawned at <paramref name="spawn"/>: where it stands once the
	/// simulator's first move has run. A walking mech spawned by its group has the height of the ground under the group's
	/// point, one height for every member, and its move (<c>Mech_MovementTick</c>, <c>0041a360</c>) sets its Z to the ground
	/// under the machine itself plus its ride height every tick, moving or not; drawn at the spawn height, a member away
	/// from the point on a slope would float or sit underground. A RAZOR's move does not do this, so it keeps the spawn
	/// height, as does anything that is not a mech. The editor's choice of what to draw, not a retail placement: nothing
	/// writes a mech's drawn height into the file, whose points hold no height for a mech
	/// (docs/retail/formats/script-dat.md#placement--the-actual-rule, rule 5).
	/// </summary>
	public static Vec3i Settled(HeightGrid terrain, SimObject simObject, Vec3i spawn) => simObject is MechObject { Type.IsFlyer: false } mech
		? new Vec3i(spawn.X, spawn.Y, terrain.HeightAtWorld(spawn.X, spawn.Y) + mech.Type.RideHeight)
		: spawn;

	/// <summary>Places again everything the document's points have moved since the last call.</summary>
	public void Sync() {
		var points = Points();
		var changed = new HashSet<int>();
		for (int i = 0; i < Math.Max(points.Length, _synced.Length); i++) {
			if (i >= points.Length || i >= _synced.Length || points[i] != _synced[i]) {
				changed.Add(i);
			}
		}

		_synced = points;
		SyncRoutes();
		SyncAreas();
		SyncGroups();
		SyncOwnPoints(changed);
	}

	private void SyncRoutes() {
		foreach (var route in _index.Routes) {
			if (WaypointEntries(route.Reference) is not { } entries) {
				continue;
			}

			var refs = _document.WaypointGroups[route.Reference].Waypoints;
			bool moved = false;
			for (int i = 0; i < entries.Length; i++) {
				if (Coordinate(refs[entries[i]]) is { } point && point != route.Points[i]) {
					route.Move(i, point);
					moved = true;
				}
			}

			if (moved) {
				_overlay.MoveRoute(route);
			}
		}
	}

	// A circle's second corner is its centre, and its radius the record's stored one scaled, as the loader resolves them.
	private void SyncAreas() {
		foreach (var area in _index.Areas.ToList()) {
			if (area.Reference < 0 || area.Reference >= _document.TriggerAreas.Length) {
				continue;
			}

			var record = _document.TriggerAreas[area.Reference];
			bool circle = area.Shape == MissionTriggerShape.Circle;
			var a = Coordinate(record.PointRef) ?? area.A;
			var b = circle ? a : Coordinate(record.SecondPointOrRadius) ?? area.B;
			int radius = circle ? TriggerAreaEditing.Radius(record.SecondPointOrRadius) : area.Radius;
			if (a != area.A || b != area.B || radius != area.Radius) {
				var moved = area with { A = a, B = b, Radius = radius };
				_index.SetArea(moved);
				_overlay.MoveArea(moved);
			}
		}
	}

	// A group stands at its own point, else its slot-0 route's first waypoint, and faces along that route's first leg
	// when it names no heading (rules 3 and 4); a paints-ground group's point snaps into its pad's tile.
	private void SyncGroups() {
		for (int group = 0; group < _document.Groups.Length; group++) {
			if (_index.SetupOf(group) is not { } setup) {
				continue;
			}

			var orders = group < _index.Mission.GroupOrders.Count ? _index.Mission.GroupOrders[group] : null;
			var route = orders is { Count: > 0 } && orders[0] is { RouteRef: var routeRef } && _index.Route(routeRef) is { } walked
				? walked.Points
				: Array.Empty<Vec3i>();

			var own = Coordinate(_document.Groups[group].PositionRef);
			var authored = own ?? (route.Count > 0 ? route[0] : Vec3i.Zero);
			var source = own != null ? MissionGroupPointSource.OwnPoint
				: route.Count > 0 ? MissionGroupPointSource.RouteStart
				: MissionGroupPointSource.None;
			// Only a structure group snaps. One that paints its ground with no structure placed has no pad, and so no
			// layout to snap by; it has nothing to move either.
			bool snaps = setup.PaintsGround && group < _index.Mission.GroupKinds.Count
				&& _index.Mission.GroupKinds[group] == MissionUnitKind.Base;
			var pad = _index.BasePads.FirstOrDefault(candidate => candidate.GroupIndex == group);
			if (snaps && pad == null) {
				continue;
			}

			var point = authored;
			if (snaps && pad != null) {
				var (x, y) = pad.Layout.SnapAnchor(authored.X, authored.Y);
				point = new Vec3i(x, y, authored.Z);
			}

			int heading = setup.HeadingFromRoute ? MissionPlacementRules.RouteBearing(route) : setup.Heading;
			if (point == setup.Point && authored == setup.AuthoredPoint && source == setup.PointSource && heading == setup.Heading) {
				continue;
			}

			var moved = setup with { Point = point, AuthoredPoint = authored, PointSource = source, Heading = heading };
			_index.SetSetup(group, moved);
			Vec3i? anchor = PlaceMembers(group, setup, moved, pad?.Anchor);
			if (pad != null && anchor is { } newAnchor) {
				var movedPad = pad with { Anchor = newAnchor };
				_index.SetBasePad(movedPad);
				_overlay.MovePad(movedPad);
			}
		}
	}

	// Spreads the members with no point of their own from the group's new point and turns those with no heading of
	// their own by as much as the group turned. Answers where the member that stood on the pad's anchor now stands.
	private Vec3i? PlaceMembers(int group, MissionGroupSetup before, MissionGroupSetup after, Vec3i? padAnchor) {
		Vec3i? anchor = null;
		foreach (var member in _index.GroupMembers[group]) {
			var placement = member.Placement;
			if (!placement.IsPlayerLance && OwnPointRef(placement) is not null) {
				continue;
			}

			Vec3i position;
			if (member.Object is FlyerObject) {
				position = FlyerSpread(after, placement.FlyerFormationOffset);
			} else {
				// A mech spawns at the ground's height at the group's point, before the spread, and is drawn where its
				// first move stands it; a structure takes the height under itself.
				var spread = GroundSpread(after, placement.FormationOffset);
				int z = member.Object is MechObject mech
					? MissionScene.MechSpawnHeight(_terrain, mech, after.Point)
					: _terrain.HeightAtWorld(spread.X, spread.Y);
				position = Settled(_terrain, member.Object, new Vec3i(spread.X, spread.Y, z));
			}

			int heading = member.Object.Heading;
			if (placement.IsPlayerLance || !HasOwnHeading(placement)) {
				heading = member.Object is BaseObject
					? (short)(heading + after.Heading - before.Heading)
					: after.Heading;
			}

			if (padAnchor is { } old && member.Object.Position.X == old.X && member.Object.Position.Y == old.Y) {
				anchor = position;
			}

			_place(member, position, heading);
		}

		return anchor;
	}

	private static Vec3i GroundSpread(MissionGroupSetup setup, (int X, int Y)? offset) =>
		offset is { } o ? MissionPlacementRules.FormationSpread(setup.Point, setup.Heading, o.X, o.Y) : setup.Point;

	// A flyer keeps the point's Z, its slot's Z added, and 5000 when that comes out zero.
	private static Vec3i FlyerSpread(MissionGroupSetup setup, Vec3i? offset) {
		var spread = offset is { } o ? MissionPlacementRules.FormationSpread(setup.Point, setup.Heading, o.X, o.Y) : setup.Point;
		int z = setup.Point.Z + (offset?.Z ?? 0);
		return new Vec3i(spread.X, spread.Y, z == 0 ? FlyerObject.DefaultHoverHeight : z);
	}

	// An object whose roster record names a point that moved stands on it again.
	private void SyncOwnPoints(HashSet<int> changed) {
		foreach (var sceneObject in _index.Objects) {
			if (sceneObject.Placement.IsPlayerLance || OwnPointRef(sceneObject.Placement) is not { } reference
					|| !changed.Contains(reference) || Coordinate(reference) is not { } point) {
				continue;
			}

			var position = Standing(_terrain, sceneObject, point.X, point.Y, point.Z);
			if (position != sceneObject.Object.Position) {
				_place(sceneObject, position, sceneObject.Object.Heading);
			}
		}
	}

	// The block-1 ref of a roster record's own point, or null when it names none the file has.
	private short? OwnPointRef(MissionPlacement placement) {
		short? reference = placement.Kind switch {
			MissionUnitKind.Mech when placement.SlotIndex >= 0 && placement.SlotIndex < _document.Mechs.Length =>
				_document.Mechs[placement.SlotIndex].PositionRef,
			MissionUnitKind.Flyer when placement.SlotIndex >= 0 && placement.SlotIndex < _document.Flyers.Length =>
				_document.Flyers[placement.SlotIndex].PositionRef,
			MissionUnitKind.Base when placement.SlotIndex >= 0 && placement.SlotIndex < _document.Bases.Length =>
				_document.Bases[placement.SlotIndex].PositionRef,
			_ => null
		};
		return reference is >= 0 && reference < _document.Coordinates.Length ? reference : null;
	}

	private bool HasOwnHeading(MissionPlacement placement) {
		short? reference = placement.Kind switch {
			MissionUnitKind.Mech when placement.SlotIndex >= 0 && placement.SlotIndex < _document.Mechs.Length =>
				_document.Mechs[placement.SlotIndex].HeadingRef,
			MissionUnitKind.Flyer when placement.SlotIndex >= 0 && placement.SlotIndex < _document.Flyers.Length =>
				_document.Flyers[placement.SlotIndex].HeadingRef,
			MissionUnitKind.Base when placement.SlotIndex >= 0 && placement.SlotIndex < _document.Bases.Length =>
				_document.Bases[placement.SlotIndex].HeadingRef,
			_ => null
		};
		return reference is >= 0 && reference < _document.Headings.Length;
	}

	private Vec3i? Coordinate(short reference) =>
		reference >= 0 && reference < _document.Coordinates.Length
			? new Vec3i(_document.Coordinates[reference].X, _document.Coordinates[reference].Y, _document.Coordinates[reference].Z)
			: null;

	private Vec3i[] Points() =>
		Array.ConvertAll(_document.Coordinates, point => new Vec3i(point.X, point.Y, point.Z));
}
