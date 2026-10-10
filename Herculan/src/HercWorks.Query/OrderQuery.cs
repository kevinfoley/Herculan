using HercWorks.Core.Data.File.Msn;

namespace HercWorks.Query;

/// <summary>One row #15 record (<see cref="MissionOrder22"/>) a group's order slot names.</summary>
/// <param name="Index">The record's position in row #15.</param>
/// <param name="PointFound">Whether <paramref name="PointRef"/> names a row #6 record; false for <c>-1</c>.</param>
/// <param name="RouteWaypoints">The route's waypoint count, or null when row #8 has no record with its GUID.</param>
/// <param name="RouteDiffers">
/// True on a slot after the first whose route is set and is not slot 0's. Slot 0's route is the one the
/// simulator installs (docs/retail/simulation/ai-goals.md#the-route-cursor-is-loaded-once).
/// </param>
internal sealed record OrderRecord(
	int Index, short ConditionRef, string? Condition, short Verb, short FormationId,
	short PointRef, bool PointFound, int? X, int? Y,
	short RouteRef, int? RouteWaypoints, bool RouteDiffers,
	short SubjectKind, short SubjectRef, short ActionRef);

/// <summary>One of a group's ten order slots that is set.</summary>
/// <param name="Records">Every row #15 record with the slot's GUID; empty when there is none.</param>
internal sealed record OrderSlot(int Slot, short Ref, IReadOnlyList<OrderRecord> Records);

/// <summary>One row #16 record and its orders.</summary>
/// <param name="Index">The record's position in row #16; 0 is the player's squad.</param>
/// <param name="Members">How many of the twenty member refs are set.</param>
/// <param name="PositionRef">The group's own row #6 point, its heading and its route: what a same-GUID overlay often changes.</param>
/// <param name="OutOfActionReport">The (counter ref, operation) pairs, interleaved, that are set.</param>
internal sealed record GroupOrders(
	int Index, short Guid, short ConditionRef, string? Condition, short MemberKind, int Members, short Side,
	short FormationId, short DeploymentActionRef, IReadOnlyList<OrderSlot> Slots,
	short PositionRef, short HeadingRef, short RouteRef, IReadOnlyList<short> OutOfActionReport) {
	public bool PlayerSquad => Index == 0;
	public bool RouteSwitch => Slots.Any(s => s.Records.Any(r => r.RouteDiffers));
}

internal sealed record OrderQueryMission(string Mission, int OrderRecords, IReadOnlyList<GroupOrders> Groups);

/// <summary>The result of <c>orders</c>, with the corpus counts taken before any filter.</summary>
/// <param name="OrderRecords">Every row #15 record of the missions searched.</param>
/// <param name="PointsByVerb">Row #15 records whose point names a row #6 record, by verb.</param>
/// <param name="RouteSwitchGroups">
/// Groups, counted by GUID per mission, with a record whose later order names a route other than slot 0's.
/// </param>
internal sealed record OrderQueryResult(
	int MissionsSearched, int OrderRecords, IReadOnlyDictionary<short, int> PointsByVerb,
	int RouteSwitchGroups, IReadOnlyList<string> RouteSwitchMissions, IReadOnlyList<OrderQueryMission> Missions);

/// <summary>
/// <c>orders</c>: every mission group's order slots (row #16 <see cref="MissionGroup164.OrderRefs"/>
/// into row #15), with each order's verb, formation, point, route, subject and ending action. What the
/// simulator makes of each field is docs/retail/simulation/ai-goals.md#the-order-record.
/// </summary>
internal static class OrderQuery {
	/// <param name="routeSwitch">List only groups with a later order naming a route other than slot 0's.</param>
	/// <param name="withPoint">List only groups with an order whose point names a row #6 record.</param>
	public static OrderQueryResult Run(RetailData data, IReadOnlyCollection<string> missions, bool routeSwitch, bool withPoint) {
		var searched = data.Missions
			.Where(m => missions.Count == 0 || missions.Contains(m.Name, StringComparer.OrdinalIgnoreCase))
			.Select(m => new OrderQueryMission(m.Name, m.File.Orders?.Length ?? 0, Search(m.File)))
			.ToList();

		var pointsByVerb = data.Missions
			.Where(m => missions.Count == 0 || missions.Contains(m.Name, StringComparer.OrdinalIgnoreCase))
			.SelectMany(m => (m.File.Orders ?? []).Where(o => m.File.GetPoint(o.PointRef) != null))
			.GroupBy(o => o.Verb)
			.OrderBy(g => g.Key)
			.ToDictionary(g => g.Key, g => g.Count());

		var switching = searched
			.SelectMany(m => m.Groups.Where(g => g.RouteSwitch).Select(g => (m.Mission, Key: g.Guid == -1 ? -1 - g.Index : g.Guid)))
			.Distinct()
			.ToList();

		var listed = searched
			.Select(m => m with {
				Groups = m.Groups
					.Where(g => !routeSwitch || g.RouteSwitch)
					.Where(g => !withPoint || g.Slots.Any(s => s.Records.Any(r => r.PointFound)))
					.ToList(),
			})
			.Where(m => m.Groups.Count > 0 || !(routeSwitch || withPoint))
			.ToList();

		return new OrderQueryResult(searched.Count, searched.Sum(m => m.OrderRecords), pointsByVerb,
			switching.Count, switching.Select(s => s.Mission).Distinct().ToList(), listed);
	}

	private static List<GroupOrders> Search(MissionFile file) {
		var groups = file.Groups ?? [];
		var result = new List<GroupOrders>();
		for (int i = 0; i < groups.Length; i++) {
			var group = groups[i];
			short firstRoute = file.GetOrder(group.OrderRefs[0])?.RouteRef ?? -1;
			var slots = new List<OrderSlot>();
			for (int slot = 0; slot < group.OrderRefs.Length; slot++) {
				short guid = group.OrderRefs[slot];
				if (guid == -1) {
					continue;
				}

				var records = (file.Orders ?? [])
					.Select((o, index) => (o, index))
					.Where(x => x.o.GUID == guid)
					.Select(x => Record(file, x.o, x.index, slot > 0 && x.o.RouteRef != -1 && x.o.RouteRef != firstRoute))
					.ToList();
				slots.Add(new OrderSlot(slot, guid, records));
			}

			result.Add(new GroupOrders(i, group.GUID, group.ConditionRef, Conditions.Describe(file, group.ConditionRef),
				group.MemberKind, group.MemberRefs.Count(r => r != -1), group.Side, group.FormationId, group.DeploymentActionRef, slots,
				group.PositionRef, group.HeadingRef, group.RouteRef,
				group.OutOfActionReport.Chunk(2).Where(p => p[0] != -1).SelectMany(p => p).ToList()));
		}

		return result;
	}

	private static OrderRecord Record(MissionFile file, MissionOrder22 order, int index, bool routeDiffers) {
		var point = file.GetPoint(order.PointRef);
		var route = file.GetWaypointGroup(order.RouteRef);
		return new OrderRecord(index, order.ConditionRef, Conditions.Describe(file, order.ConditionRef), order.Verb, order.FormationId,
			order.PointRef, point != null, point?.X, point?.Y,
			order.RouteRef, route?.Waypoints.Length, routeDiffers,
			order.SubjectKind, order.SubjectRef, order.ActionRef);
	}

	/// <summary>An order verb's name (docs/retail/simulation/ai-goals.md#when-an-order-is-finished--group_isordercomplete-004239fc).</summary>
	public static string Verb(short verb) => verb switch {
		0 => "search/destroy",
		1 => "ram",
		2 => "guard",
		3 => "patrol",
		4 => "sleep",
		5 => "travel",
		6 => "follow",
		_ => $"verb {verb}",
	};

	/// <summary>What an order's subject ref names, by its kind.</summary>
	public static string SubjectKind(short kind) => kind switch {
		-1 => "nothing",
		0 => "group",
		1 => "mech",
		2 => "flyer",
		3 => "base",
		_ => $"kind {kind}",
	};

	/// <summary>Which roster a group's member refs name, by its member kind.</summary>
	public static string MemberKind(short kind) => kind switch {
		0 => "mech",
		1 => "flyer",
		2 => "structure",
		_ => $"kind {kind}",
	};
}
