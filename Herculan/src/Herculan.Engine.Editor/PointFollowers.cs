using HercWorks.Core.Data.File.Msn;
using HercWorks.Core.Data.File.Msn.Script;
using Herculan.Engine.Numerics;
using Herculan.Engine.Scene;
using Herculan.Engine.Sim;
using Herculan.Engine.Terrain;
using Herculan.Engine.World;

namespace Herculan.Engine.Editor;

/// <summary>
/// The route and trigger-area lines that stand on a <c>script.dat</c> block-1 point, laid again from the generated document
/// while a drag previews a move (<see cref="Sync"/>), so that what shares a moved point moves with it: route waypoints and
/// trigger areas, each resolved as the loader resolves them, and only what has changed touched. The objects and groups
/// standing on a moved point are placed from the layout with the drag's overrides instead (<see cref="SceneSync.Preview"/>).
///
/// <para>Not placed here: the ground under a moved structure or pad and a pad's paint, which are
/// <see cref="TerrainLeveller"/>'s; and an order's or an objective's own point, which the editor does not draw.</para>
/// </summary>
internal sealed class PointFollowers {
	private readonly ScriptDat _document;
	private readonly MissionDocument _source;
	private readonly GeneratedIndex _index;
	private readonly MissionOverlay _overlay;
	private readonly Dictionary<int, int[]> _waypointEntries = new();
	private PlacementOverrides _overrides = PlacementOverrides.None;

	/// <param name="source">The mission as its <c>.MSN</c>, whose GUIDs a drag's overrides name the points by.</param>
	public PointFollowers(ScriptDat document, MissionDocument source, GeneratedIndex index, MissionOverlay overlay) {
		_document = document;
		_source = source;
		_index = index;
		_overlay = overlay;

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

	/// <summary>
	/// Lays again every route and area line the document's points, or <paramref name="overrides"/>' row-6 GUIDs in their
	/// place, have moved since the last call.
	/// </summary>
	public void Sync(PlacementOverrides? overrides = null) {
		_overrides = overrides ?? PlacementOverrides.None;
		SyncRoutes();
		SyncAreas();
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

	// Block-1 point <reference>, at an override's coordinates when its GUID has one.
	private Vec3i? Coordinate(short reference) {
		if (reference < 0 || reference >= _document.Coordinates.Length) {
			return null;
		}

		if (_overrides.Points.Count > 0 && _overrides.Points.TryGetValue(_source.GuidOf(1, reference), out var moved)) {
			return moved;
		}

		var point = _document.Coordinates[reference];
		return new Vec3i(point.X, point.Y, point.Z);
	}
}
