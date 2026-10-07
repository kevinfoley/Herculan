using HercWorks.Core.Data.File.Msn;

namespace HercWorks.Query;

/// <summary>One trigger area an action tests (<see cref="TriggerArea12"/>), its points resolved.</summary>
/// <param name="Found">False when row #9 has no record with the GUID; the other fields are then unset.</param>
/// <param name="X">The circle's centre or the box's first corner; null when row #6 has no such point.</param>
/// <param name="Radius">A circle's radius in world units (the stored value times ten); null for a box.</param>
/// <param name="X2">A box's opposite corner; null for a circle, or when row #6 has no such point.</param>
internal sealed record ActionArea(
	short Guid, bool Found, short Shape, short PointRef, int? X, int? Y, int? Radius, short? SecondPointRef, int? X2, int? Y2);

/// <summary>A record that names an action as the one it fires.</summary>
/// <param name="What">Which ref names it: <c>defeat</c>, <c>engagement</c> or <c>timer</c>.</param>
internal sealed record ActionSource(string Row, short Guid, string What);

/// <summary>A group that waits on an action, and the row #6 point it is placed on as authored.</summary>
/// <param name="PointRef">
/// The group's <see cref="MissionGroup164.PositionRef"/>, or when that is unset the first waypoint of
/// its first order's route; <c>-1</c> when neither is set. Before any paints-ground anchor move.
/// </param>
internal sealed record WaitingGroup(short Guid, short PointRef, int? X, int? Y);

/// <summary>One row #10 action and what decides when it activates.</summary>
/// <param name="Index">The action's position in row #10.</param>
/// <param name="Areas">The areas DBSIM tests: the refs before the first <c>-1</c>.</param>
/// <param name="TargetRef">For types 7-10 the mech, flyer, base or group the trigger follows; null otherwise.</param>
/// <param name="FiredBy">The roster records and timers that fire the action without any area.</param>
/// <param name="DeploysGroups">The groups that wait on the action.</param>
internal sealed record ActionReport(
	int Index, short Guid, short ConditionRef, string? Condition, short Type, short Verb,
	IReadOnlyList<ActionArea> Areas, short? TargetRef, IReadOnlyList<ActionSource> FiredBy,
	IReadOnlyList<WaitingGroup> DeploysGroups, short MessageId);

internal sealed record ActionQueryMission(string Mission, IReadOnlyList<ActionReport> Actions);

/// <summary>
/// <c>actions</c>: every row #10 action (<see cref="MissionAction82"/>) with its trigger subject, its
/// trigger areas, its verb, and the records that fire it or wait on it. The trigger rules are
/// docs/retail/simulation/mission-deployment.md, "Trigger areas".
/// </summary>
internal static class ActionQuery {
	public static IReadOnlyList<ActionQueryMission> Run(RetailData data, IReadOnlyCollection<string> missions) =>
		data.Missions
			.Where(m => missions.Count == 0 || missions.Contains(m.Name, StringComparer.OrdinalIgnoreCase))
			.Select(m => new ActionQueryMission(m.Name, Search(m.File)))
			.ToList();

	private static List<ActionReport> Search(MissionFile file) {
		var actions = file.Actions ?? [];
		var reports = new List<ActionReport>();
		for (int i = 0; i < actions.Length; i++) {
			var action = actions[i];
			var areas = action.AreaRefs.TakeWhile(r => r != -1).Select(r => Area(file, r)).ToList();
			reports.Add(new ActionReport(
				i, action.GUID, action.ConditionRef, Conditions.Describe(file, action.ConditionRef), action.Type, action.Verb,
				areas, action.Type is >= 7 and <= 10 ? action.TargetRef : null, Sources(file, action.GUID),
				(file.Groups ?? []).Where(g => g.DeploymentActionRef == action.GUID && action.GUID != -1).Select(g => Waiting(file, g)).ToList(),
				action.MessageId));
		}

		return reports;
	}

	private static WaitingGroup Waiting(MissionFile file, MissionGroup164 group) {
		short point = group.PositionRef;
		if (point == -1 && group.OrderRefs.FirstOrDefault(r => r != -1) is var order and not -1
				&& file.GetWaypointGroup(file.GetOrder(order)?.RouteRef ?? -1) is { Waypoints: [var first, ..] }) {
			point = first;
		}

		var position = file.GetPoint(point);
		return new WaitingGroup(group.GUID, point, position?.X, position?.Y);
	}

	private static ActionArea Area(MissionFile file, short guid) {
		if (file.GetTriggerArea(guid) is not { } area) {
			return new ActionArea(guid, false, -1, -1, null, null, null, null, null, null);
		}

		var first = file.GetPoint(area.PointRef);
		if (area.Shape != 0) {
			return new ActionArea(guid, true, area.Shape, area.PointRef, first?.X, first?.Y, area.SecondPointOrRadius * 10, null, null, null);
		}

		var second = file.GetPoint(area.SecondPointOrRadius);
		return new ActionArea(guid, true, area.Shape, area.PointRef, first?.X, first?.Y, null, area.SecondPointOrRadius, second?.X, second?.Y);
	}

	private static List<ActionSource> Sources(MissionFile file, short guid) {
		var sources = new List<ActionSource>();
		if (guid == -1) {
			return sources;
		}

		void Add(string row, short owner, short engagement, short defeat) {
			if (engagement == guid) {
				sources.Add(new ActionSource(row, owner, "engagement"));
			}

			if (defeat == guid) {
				sources.Add(new ActionSource(row, owner, "defeat"));
			}
		}

		foreach (var m in file.Mechs ?? []) {
			Add("mech", m.GUID, m.EngagementActionRef, m.DefeatActionRef);
		}

		foreach (var f in file.Flyers ?? []) {
			Add("flyer", f.GUID, f.EngagementActionRef, f.DefeatActionRef);
		}

		foreach (var b in file.Bases ?? []) {
			Add("base", b.GUID, b.EngagementActionRef, b.DefeatActionRef);
		}

		foreach (var t in file.ActionTimers ?? []) {
			if (t.SequenceRefs.Contains(guid)) {
				sources.Add(new ActionSource("timer", t.GUID, "timer"));
			}
		}

		return sources;
	}

	/// <summary>Whose position an action's areas test, by its type (docs/retail/simulation/mission-deployment.md, "Trigger areas").</summary>
	public static string Subject(short type) => type switch {
		0 => "player",
		1 => "player's group",
		2 => "human groups",
		3 => "Cybrid groups",
		4 => "mech groups",
		5 => "flyer groups",
		6 => "base groups",
		7 => "mech",
		8 => "flyer",
		9 => "base",
		10 => "group",
		_ => "?",
	};

	/// <summary>How a group waiting on the action arrives, by its verb (docs/retail/simulation/mission-deployment.md, "Arrival").</summary>
	public static string Arrival(short verb) => verb switch {
		2 or 3 => "drop pod",
		4 or 5 => "on foot",
		_ => "in place",
	};
}
