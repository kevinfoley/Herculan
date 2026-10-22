using System.Numerics;
using Herculan.Engine.Numerics;
using Herculan.Engine.Render;
using Herculan.Engine.Scene;
using Herculan.Engine.World;

namespace Herculan.Engine.Editor;

/// <summary>
/// The sphere a selection fills, in world units, for the camera to frame (<see cref="EditorCamera.Frame"/>): an object's
/// pick sphere, a group's members and point, an area's footprint, a route's waypoints, an objective's subject or an
/// action's areas. Ground features sit on the terrain, where they are drawn. All but an objective's subject are the
/// layout's (<see cref="MissionIndex"/>). An object out of the scene, and a group kept out of it
/// (<see cref="PlannedGroup.OffScene"/>), fill nothing.
/// </summary>
internal static class SelectionBounds {
	/// <summary>How large a sphere a single point is framed as, in world units (about 20 m).</summary>
	private const float PointRadius = 3000f;

	/// <summary>
	/// The sphere <paramref name="selection"/> fills, or null when it stands nowhere (a timer, or a group out of the scene).
	/// </summary>
	public static (Vector3 Centre, float Radius)? Of(EditorMission mission, EditorSelection selection) {
		var spheres = new List<(Vector3 Centre, float Radius)>();
		var index = mission.Generated;
		var records = index.Records;
		switch (selection) {
			case ObjectSelection selected when mission.Index.ObjectOf(selected) is { } placed:
				AddObject(spheres, mission, placed);
				break;
			case GroupSelection { Guid: var guid } when mission.Index.Group(guid) is { OffScene: false } group:
				foreach (var member in mission.Index.MembersOf(guid)) {
					AddObject(spheres, mission, member);
				}

				spheres.Add((OnGround(mission, group.Setup.Point), PointRadius));
				break;
			case AreaSelection { Guid: var guid } when mission.Index.Area(guid) is { } area:
				AddArea(spheres, mission, area.Area);
				break;
			case ActionSelection { Guid: var guid } when mission.Index.Action(guid) is { } action:
				foreach (short reference in action.Areas) {
					if (mission.Index.Area(reference) is { } area) {
						AddArea(spheres, mission, area.Area);
					}
				}

				break;
			case RouteSelection { Guid: var guid, Waypoint: var waypoint } when mission.Index.Route(guid) is { } route:
				if (waypoint >= 0 && waypoint < route.Points.Count) {
					spheres.Add((OnGround(mission, route.Points[waypoint]), PointRadius));
				} else {
					spheres.AddRange(route.Points.Select(point => (OnGround(mission, point), PointRadius)));
				}

				break;
			case ObjectiveSelection { Record: var record } when records.Objective(record) is { } objective
					&& objective < index.Mission.Objectives.Count:
				foreach (var subject in index.SubjectObjectsOf(index.Mission.Objectives[objective])) {
					AddObject(spheres, mission, subject);
				}

				break;
		}

		return Enclosing(spheres);
	}

	// An object's pick sphere, which covers its whole model, or its hit radius about its position when it has none; nothing
	// for an object out of the scene.
	private static void AddObject(List<(Vector3, float)> into, EditorMission mission, SceneObject placed) {
		if (!mission.Index.InScene(placed)) {
			return;
		}

		if (mission.Picker.PickableOf(placed) is { } pickable) {
			var render = pickable.CenterRender;
			into.Add((new Vector3(render.X, -render.Z, render.Y) * WorldScale.WorldUnitsPerMeter,
				pickable.RadiusRender * WorldScale.WorldUnitsPerMeter));
			return;
		}

		var position = placed.Object.Position;
		into.Add((new Vector3(position.X, position.Y, position.Z), MathF.Max(placed.Object.HitRadius, PointRadius)));
	}

	private static void AddArea(List<(Vector3, float)> into, EditorMission mission, MissionTriggerArea area) {
		if (area.Shape == MissionTriggerShape.Circle) {
			into.Add((OnGround(mission, area.A), area.Radius));
			return;
		}

		var centre = new Vec3i((area.A.X + area.B.X) / 2, (area.A.Y + area.B.Y) / 2, 0);
		float halfDiagonal = new Vector2(area.B.X - area.A.X, area.B.Y - area.A.Y).Length() / 2f;
		into.Add((OnGround(mission, centre), halfDiagonal));
	}

	private static Vector3 OnGround(EditorMission mission, Vec3i point) =>
		new(point.X, point.Y, mission.Scene.World.Terrain.HeightAtWorld(point.X, point.Y));

	// A sphere about the middle of the box holding every sphere, just large enough to hold them all: not the least
	// such sphere, but near enough to frame by.
	private static (Vector3 Centre, float Radius)? Enclosing(List<(Vector3 Centre, float Radius)> spheres) {
		if (spheres.Count == 0) {
			return null;
		}

		var min = new Vector3(float.MaxValue);
		var max = new Vector3(float.MinValue);
		foreach (var (centre, radius) in spheres) {
			min = Vector3.Min(min, centre - new Vector3(radius));
			max = Vector3.Max(max, centre + new Vector3(radius));
		}

		var middle = (min + max) / 2f;
		float enclosing = spheres.Max(sphere => Vector3.Distance(middle, sphere.Centre) + sphere.Radius);
		return (middle, enclosing);
	}
}
