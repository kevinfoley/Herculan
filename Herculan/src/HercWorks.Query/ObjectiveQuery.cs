using HercWorks.Core.Data.File.Msn;

namespace HercWorks.Query;

/// <summary>One row #17 record (<see cref="MissionObjective58"/>), its subject and text resolved.</summary>
/// <param name="Index">The record's position in row #17.</param>
/// <param name="Subject">What the subject ref names, described; null when its row has no record with the GUID.</param>
/// <param name="RouteOrders">For a route ref: the subject group's order slots whose order runs that route.</param>
/// <param name="Text">The failure text: the <c>.ENG</c> record with the text ref's id and the two after it.</param>
internal sealed record ObjectiveReport(
	int Index, short ConditionRef, string? Condition, short Required, short ConditionCode,
	short SubjectKind, short SubjectRef, string? Subject, short PointRef, short RouteRef,
	IReadOnlyList<int> RouteOrders, short TextRef, IReadOnlyList<string> Text, IReadOnlyList<CounterPair> Pairs);

internal sealed record ObjectiveQueryMission(string Mission, IReadOnlyList<ObjectiveReport> Objectives);

/// <summary>
/// <c>objectives</c>: every row #17 objective with its condition, subject, route, failure text and
/// counter writes. What the simulator makes of each is docs/retail/simulation/mission-objectives.md.
/// </summary>
internal static class ObjectiveQuery {
	public static IReadOnlyList<ObjectiveQueryMission> Run(RetailData data, IReadOnlyCollection<string> missions) =>
		data.Missions
			.Where(m => missions.Count == 0 || missions.Contains(m.Name, StringComparer.OrdinalIgnoreCase))
			.Select(m => new ObjectiveQueryMission(m.Name, Search(data, m)))
			.ToList();

	private static List<ObjectiveReport> Search(RetailData data, Mission mission) {
		var file = mission.File;
		var objectives = file.Objectives ?? [];
		var reports = new List<ObjectiveReport>();
		for (int i = 0; i < objectives.Length; i++) {
			if (objectives[i] is not { } o) {
				continue;
			}

			reports.Add(new ObjectiveReport(
				i, o.ConditionRef, Conditions.Describe(file, o.ConditionRef), o.Required, o.ConditionCode,
				o.SubjectKind, o.SubjectRef, Subject(data, file, o.SubjectKind, o.SubjectRef), o.PointRef, o.RouteRef,
				RouteOrders(file, o), o.TextRef, Text(mission, o.TextRef),
				o.Pairs.Where(p => p.CounterRef >= 0 && p.Op >= 0).ToList()));
		}

		return reports;
	}

	private static string? Subject(RetailData data, MissionFile file, short kind, short guid) {
		switch (kind) {
			case 0:
				var groups = (file.Groups ?? []).Select((g, index) => (g, index)).Where(x => x.g.GUID == guid).ToList();
				return groups.Count == 0 ? null : string.Join("; ", groups.Select(x =>
					$"#{x.index}, {x.g.MemberRefs.Count(r => r != -1)} {OrderQuery.MemberKind(x.g.MemberKind)}(s), {FlagQuery.Side(x.g.Side)}"));
			case 1:
				return file.GetMech(guid) is { } mech ? data.TypeName(RosterKind.Mech, mech.TypeIndex) ?? $"type 0x{mech.TypeIndex:x2}" : null;
			case 2:
				return file.GetFlyer(guid) is { } flyer ? data.TypeName(RosterKind.Flyer, flyer.TypeIndex) ?? $"type 0x{flyer.TypeIndex:x2}" : null;
			case 3:
				return file.GetBase(guid) is { } structure ? data.TypeName(RosterKind.Base, structure.TypeIndex) ?? $"type 0x{structure.TypeIndex:x2}" : null;
			default:
				return null;
		}
	}

	/// <summary>The order slots of the subject's group (or of each group holding the subject) whose order runs the record's route.</summary>
	private static List<int> RouteOrders(MissionFile file, MissionObjective58 o) {
		if (o.RouteRef == -1 || o.SubjectKind != 0) {
			return [];
		}

		var slots = new List<int>();
		foreach (var group in (file.Groups ?? []).Where(g => g.GUID == o.SubjectRef)) {
			for (int slot = 0; slot < group.OrderRefs.Length; slot++) {
				if (group.OrderRefs[slot] != -1 && file.GetOrder(group.OrderRefs[slot])?.RouteRef == o.RouteRef) {
					slots.Add(slot);
				}
			}
		}

		return slots;
	}

	private static List<string> Text(Mission mission, short id) {
		var strings = mission.Text?.Strings ?? [];
		int start = Array.FindIndex(strings, s => s.Guid == id);
		return start < 0 ? [] : strings.Skip(start).Take(3).Select(s => $"{s.Guid}: {s.Val}").ToList();
	}

	/// <summary>A condition code's question (docs/retail/simulation/mission-objectives.md#what-each-condition-asks).</summary>
	public static string Code(short code) => code switch {
		0 => "order on route complete",
		1 => "lost",
		2 => "clear of threats",
		3 or 4 => "data link done",
		6 => "engaged",
		7 => "disarmed",
		8 => "not engaged",
		9 or 10 => "no data link",
		_ => "no case",
	};

	/// <summary>An objective counter operation (docs/retail/simulation/mission-objectives.md#the-record).</summary>
	public static string Op(short op) => op switch {
		4 => "set",
		5 => "clear",
		6 => "increment",
		7 => "decrement",
		_ => $"op {op}",
	};
}
