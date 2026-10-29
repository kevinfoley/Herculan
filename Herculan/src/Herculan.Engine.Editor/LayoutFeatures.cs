using HercWorks.Core.Data.File.Msn;
using Herculan.Engine.Numerics;
using Herculan.Engine.World;

namespace Herculan.Engine.Editor;

/// <summary>
/// One row-8 route as placed: its place in the layout's row 8 (<paramref name="Position"/>, from 0; <see cref="Number"/> is
/// what the editor shows), its waypoints, and the groups whose orders name it.
/// </summary>
/// <param name="Points">
/// Its waypoints, in order: each entry of its list naming a point the layout has, as the load drops a waypoint naming none.
/// </param>
/// <param name="Entries">The entry of the route's list each of <paramref name="Points"/> comes from.</param>
/// <param name="PointGuids">The row-6 GUID each of <paramref name="Points"/> names.</param>
/// <param name="WalkedBy">
/// The row-16 GUIDs of the groups whose order slot 0 names it, in layout order: the only route a group ever walks
/// (docs/retail/simulation/ai-goals.md#the-route-cursor-is-loaded-once).
/// </param>
/// <param name="NamedBy">The row-16 GUIDs of the groups that name it only in a later order slot, which no group walks.</param>
internal sealed record PlannedRoute(short Guid, int Position, IReadOnlyList<Vec3i> Points, IReadOnlyList<int> Entries,
		IReadOnlyList<short> PointGuids, IReadOnlyList<short> WalkedBy, IReadOnlyList<short> NamedBy) {
	/// <summary>Its number as the editor shows it (<see cref="MissionText.Number"/>).</summary>
	public int Number => MissionText.Number(Position);

	/// <summary>
	/// Whether the route closes on itself, its last waypoint its first, and so is walked as a loop that never ends. Compared
	/// by position, as the simulation's route cursor compares it (<see cref="Sim.MissionGroup.RouteCursor"/>).
	/// </summary>
	public bool Closed => Points.Count > 1 && Points[^1] == Points[0];

	/// <summary>Whether any group walks it.</summary>
	public bool Walked => WalkedBy.Count > 0;
}

/// <summary>
/// One row-9 trigger area as placed: its place in the layout's row 9 (<paramref name="Position"/>, from 0;
/// <see cref="Number"/> is what the editor shows), where it stands and the actions testing it.
/// </summary>
/// <param name="Area">
/// Its shape and where it stands, resolved as the load resolves a block-4 record (<see cref="MissionLoader.TriggerArea"/>): a
/// circle's radius its stored one times ten. Its <see cref="MissionTriggerArea.Reference"/> is unset, as the area is named by
/// its GUID.
/// </param>
/// <param name="PointGuids">The row-6 GUIDs of its first corner or centre and, for a box, its second corner.</param>
/// <param name="TestedBy">The row-10 GUIDs of the actions that test it (<see cref="PlannedAction.Areas"/>), in layout order.</param>
internal sealed record PlannedArea(short Guid, int Position, MissionTriggerArea Area, IReadOnlyList<short> PointGuids,
		IReadOnlyList<short> TestedBy) {
	/// <summary>Its number as the editor shows it (<see cref="MissionText.Number"/>).</summary>
	public int Number => MissionText.Number(Position);
}

/// <summary>
/// One row-10 action as placed: its place in the layout's row 10 (<paramref name="Position"/>, from 0; <see cref="Number"/> is
/// what the editor shows), its type, which says whose position its areas test, and the areas it tests: its area slots up to
/// the first unset one or the first naming an area the layout has not, which the generation resolves to unset and the load
/// stops at (<see cref="MissionLoader"/>'s action pass), each area that stands somewhere (<see cref="LayoutPlan.Area"/>).
/// </summary>
/// <param name="Areas">The row-9 GUIDs of the areas it tests, in slot order.</param>
internal sealed record PlannedAction(short Guid, int Position, short Type, IReadOnlyList<short> Areas) {
	/// <summary>Its number as the editor shows it (<see cref="MissionText.Number"/>).</summary>
	public int Number => MissionText.Number(Position);
}

/// <summary>
/// The routes, trigger areas and actions of a layout, as <see cref="LayoutPlacement.Place"/> places them beside its groups
/// and roster records: from every record of rows 8 to 10, a conditional one included, each by its base's fields, its points
/// read from the layout's row 6 with a drag's overrides in their place (<see cref="PlacementOverrides"/>). A route none of
/// whose waypoints names a point the layout has, and an area whose corner or centre names none, stand nowhere and are left
/// out, as the load leaves them out of the generated mission. No condition is tested.
/// </summary>
internal static class LayoutFeatures {
	private const int AreaRow = 9;

	/// <summary>The routes, areas and actions of <paramref name="layout"/>, whose groups are <paramref name="groups"/>.</summary>
	public static (IReadOnlyList<PlannedRoute> Routes, IReadOnlyList<PlannedArea> Areas, IReadOnlyList<PlannedAction> Actions)
			Place(MissionLayout layout, IReadOnlyList<PlannedGroup> groups, PlacementOverrides overrides) {
		var routes = Routes(layout, groups, overrides);
		var shapes = Shapes(layout, overrides);
		var actions = new List<PlannedAction>();
		var tested = new Dictionary<short, List<short>>();
		var actionEntries = layout.Entries<MissionAction82>();
		for (int number = 0; number < actionEntries.Count; number++) {
			var entry = actionEntries[number];
			var areas = new List<short>();
			foreach (short area in entry.Fields.AreaRefs) {
				if (area == -1 || layout.Entry(AreaRow, area) == null) {
					break;
				}

				if (shapes.ContainsKey(area)) {
					areas.Add(area);
					if (!tested.TryGetValue(area, out var by)) {
						tested[area] = by = new List<short>();
					}

					if (!by.Contains(entry.Guid)) {
						by.Add(entry.Guid);
					}
				}
			}

			actions.Add(new PlannedAction(entry.Guid, number, entry.Fields.Type, areas));
		}

		var placedAreas = shapes.Values
			.Select(shape => new PlannedArea(shape.Guid, shape.Position, shape.Area, shape.Points,
				tested.GetValueOrDefault(shape.Guid) ?? (IReadOnlyList<short>)[]))
			.OrderBy(area => area.Position)
			.ToList();
		return (routes, placedAreas, actions);
	}

	private static List<PlannedRoute> Routes(MissionLayout layout, IReadOnlyList<PlannedGroup> groups, PlacementOverrides overrides) {
		var walkedBy = new Dictionary<short, List<short>>();
		var namedBy = new Dictionary<short, List<short>>();
		foreach (var group in groups) {
			for (int slot = 0; slot < group.Orders.Count; slot++) {
				if (group.Orders[slot] == -1 || layout.Entry<MissionOrder22>(group.Orders[slot])?.Fields is not { RouteRef: not -1 } order) {
					continue;
				}

				var into = slot == 0 ? walkedBy : namedBy;
				if (!into.TryGetValue(order.RouteRef, out var by)) {
					into[order.RouteRef] = by = new List<short>();
				}

				if (!by.Contains(group.Guid)) {
					by.Add(group.Guid);
				}
			}
		}

		var routes = new List<PlannedRoute>();
		var entries = layout.Entries<WaypointGroup>();
		for (int number = 0; number < entries.Count; number++) {
			var entry = entries[number];
			var detached = overrides.DetachedWaypoint is { } moved && moved.Route == entry.Guid ? moved : default;
			var points = new List<Vec3i>();
			var listEntries = new List<int>();
			var guids = new List<short>();
			var list = entry.Fields.Waypoints;
			for (int i = 0; i < list.Length; i++) {
				if (LayoutPlacement.Point(layout, list[i], overrides) is not { } at) {
					continue;
				}

				points.Add(detached.Entries?.Contains(i) == true ? detached.Point : at);
				listEntries.Add(i);
				guids.Add(list[i]);
			}

			if (points.Count == 0) {
				continue;
			}

			var walked = walkedBy.GetValueOrDefault(entry.Guid) ?? [];
			var named = (namedBy.GetValueOrDefault(entry.Guid) ?? []).Except(walked).ToList();
			routes.Add(new PlannedRoute(entry.Guid, number, points, listEntries, guids, walked, named));
		}

		return routes;
	}

	// Every area that stands somewhere, by its GUID: a circle about its first point, a box between its two.
	private static Dictionary<short, (short Guid, int Position, MissionTriggerArea Area, IReadOnlyList<short> Points)> Shapes(
			MissionLayout layout, PlacementOverrides overrides) {
		var shapes = new Dictionary<short, (short, int, MissionTriggerArea, IReadOnlyList<short>)>();
		var entries = layout.Entries<TriggerArea12>();
		for (int number = 0; number < entries.Count; number++) {
			var entry = entries[number];
			var fields = entry.Fields;
			if (Corner(layout, entry.Guid, 0, fields.PointRef, overrides) is not { } a) {
				continue;
			}

			if (fields.Shape != 0) {
				int radius = overrides.Radius is { } dragged && dragged.Area == entry.Guid
					? dragged.Radius
					: TriggerAreaEditing.Radius(fields.SecondPointOrRadius);
				shapes[entry.Guid] = (entry.Guid, number, new MissionTriggerArea(MissionTriggerShape.Circle, a, a, radius),
					[fields.PointRef]);
			} else if (Corner(layout, entry.Guid, 1, fields.SecondPointOrRadius, overrides) is { } b) {
				shapes[entry.Guid] = (entry.Guid, number, new MissionTriggerArea(MissionTriggerShape.Box, a, b, 0),
					[fields.PointRef, fields.SecondPointOrRadius]);
			}
		}

		return shapes;
	}

	// Corner <corner> of area <area>, naming row-6 GUID <point>: the drag's when the corner is detached, else the point's.
	private static Vec3i? Corner(MissionLayout layout, short area, int corner, short point, PlacementOverrides overrides) {
		var at = LayoutPlacement.Point(layout, point, overrides);
		return at != null && overrides.DetachedCorner is { } moved && moved.Area == area && moved.Corner == corner ? moved.Point : at;
	}
}
