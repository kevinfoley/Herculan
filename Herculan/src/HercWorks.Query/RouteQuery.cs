using HercWorks.Core.Data.File.Msn;

namespace HercWorks.Query;

/// <summary>One waypoint of a route: the row #6 point it names.</summary>
/// <param name="Found">False when row #6 has no record with the GUID; the coordinates are then null.</param>
/// <param name="Leg">Ground-plane distance from the waypoint before, in world units; null on the first or when either point is missing.</param>
internal sealed record RouteWaypoint(short PointRef, bool Found, int? X, int? Y, int? Z, int? Leg);

/// <summary>A row #15 order or a row #17 objective that names a route.</summary>
/// <param name="Row">15 for an order a group's slot names, 17 for an objective.</param>
/// <param name="Owner">The group's GUID for an order; the objective's index in row #17 for an objective.</param>
/// <param name="Slot">The group's order slot naming the order; null for an objective.</param>
/// <param name="Installed">
/// True for slot 0's order, whose route is the one the simulator walks for the whole mission
/// (docs/retail/simulation/ai-goals.md#the-route-cursor-is-loaded-once).
/// </param>
internal sealed record RouteUser(int Row, short Owner, int? Slot, short? OrderGuid, short? Verb, bool Installed);

/// <summary>One row #8 waypoint group (<see cref="WaypointGroup"/>).</summary>
/// <param name="Index">The record's position in row #8.</param>
/// <param name="Closed">
/// Whether the route closes on itself: more than one waypoint, the last naming the same point as the first.
/// <c>Route_AdvanceCursor</c> (<c>0042313c</c>) wraps such a route's cursor back to 0 on reaching its last
/// waypoint, so it never runs out (docs/retail/simulation/ai-goals.md#the-route-cursor-is-loaded-once). The
/// simulator compares resolved point pointers, which a ref resolves to one per GUID, so comparing the refs is
/// the same test.
/// </param>
internal sealed record RouteReport(
	int Index, short Guid, short ConditionRef, string? Condition, IReadOnlyList<RouteWaypoint> Waypoints, bool Closed,
	IReadOnlyList<RouteUser> UsedBy);

internal sealed record RouteQueryMission(string Mission, IReadOnlyList<RouteReport> Routes);

/// <summary>
/// <c>routes</c>: every row #8 waypoint group of the missions named, with its waypoints' coordinates, whether it
/// closes on itself, and the group orders and objectives that name it.
/// </summary>
internal static class RouteQuery {
	/// <param name="lowGuid">The lowest route GUID listed.</param>
	/// <param name="highGuid">The highest route GUID listed.</param>
	public static IReadOnlyList<RouteQueryMission> Run(RetailData data, IReadOnlyCollection<string> missions, short lowGuid, short highGuid) =>
		data.Missions
			.Where(m => missions.Count == 0 || missions.Contains(m.Name, StringComparer.OrdinalIgnoreCase))
			.Select(m => new RouteQueryMission(m.Name, Search(m.File).Where(r => r.Guid >= lowGuid && r.Guid <= highGuid).ToList()))
			.ToList();

	private static List<RouteReport> Search(MissionFile file) {
		var routes = file.WaypointGroups ?? [];
		var reports = new List<RouteReport>();
		for (int i = 0; i < routes.Length; i++) {
			var route = routes[i];
			var waypoints = new List<RouteWaypoint>();
			MapPoint22? previous = null;
			foreach (short reference in route.Waypoints) {
				var point = file.GetPoint(reference);
				int? leg = point != null && previous != null ? (int)Math.Round(Math.Sqrt(
					Math.Pow((double)point.X - previous.X, 2) + Math.Pow((double)point.Y - previous.Y, 2))) : null;
				waypoints.Add(new RouteWaypoint(reference, point != null, point?.X, point?.Y, point?.Z, waypoints.Count == 0 ? null : leg));
				previous = point;
			}

			bool closed = route.Waypoints.Length > 1 && route.Waypoints[^1] == route.Waypoints[0];
			reports.Add(new RouteReport(i, route.GUID, route.ConditionRef, Conditions.Describe(file, route.ConditionRef), waypoints, closed,
				Users(file, route.GUID)));
		}

		return reports;
	}

	private static List<RouteUser> Users(MissionFile file, short guid) {
		var users = new List<RouteUser>();
		if (guid == -1) {
			return users;
		}

		foreach (var group in file.Groups ?? []) {
			for (int slot = 0; slot < group.OrderRefs.Length; slot++) {
				short orderGuid = group.OrderRefs[slot];
				if (orderGuid == -1) {
					continue;
				}

				foreach (var order in (file.Orders ?? []).Where(o => o.GUID == orderGuid && o.RouteRef == guid)) {
					users.Add(new RouteUser(15, group.GUID, slot, orderGuid, order.Verb, slot == 0));
				}
			}
		}

		var objectives = file.Objectives ?? [];
		for (int i = 0; i < objectives.Length; i++) {
			if (objectives[i] is { } objective && objective.RouteRef == guid) {
				users.Add(new RouteUser(17, (short)i, null, null, null, false));
			}
		}

		return users;
	}
}
