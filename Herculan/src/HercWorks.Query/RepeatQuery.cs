using HercWorks.Core.Data.File.Msn;

namespace HercWorks.Query;

/// <summary>One record of a repeated GUID.</summary>
/// <param name="Index">The record's position in its row.</param>
/// <param name="Payload">What the record says, in brief: a point's coordinates, a heading, a route's waypoints.</param>
internal sealed record RepeatedRecord(int Index, string Payload);

/// <summary>A GUID that two or more unconditioned records of one row carry.</summary>
/// <param name="Records">In file order. The load keeps the last, in the first one's slot.</param>
internal sealed record RepeatedGuid(int Row, short Guid, IReadOnlyList<RepeatedRecord> Records);

internal sealed record RepeatQueryMission(string Mission, IReadOnlyList<RepeatedGuid> Guids);

/// <summary>
/// <c>repeats</c>: the GUIDs that two or more unconditioned records carry in rows #3, #6, #7, #8 and #9,
/// where each later record replaces the earlier
/// (docs/retail/formats/msn-mission-file.md#repeated-guids), so every ref to the GUID reads the last.
/// </summary>
internal static class RepeatQuery {
	public static IReadOnlyList<RepeatQueryMission> Run(RetailData data, IReadOnlyCollection<string> missions) =>
		data.Missions
			.Where(m => missions.Count == 0 || missions.Contains(m.Name, StringComparer.OrdinalIgnoreCase))
			.Select(m => new RepeatQueryMission(m.Name, Search(m.File)))
			.ToList();

	private static List<RepeatedGuid> Search(MissionFile file) {
		var found = new List<RepeatedGuid>();
		found.AddRange(Repeats(3, file.Variants, r => r.ConditionRef, r => $"value {r.Value}"));
		found.AddRange(Repeats(6, file.Points, r => r.ConditionRef, r => $"({r.X}, {r.Y}, {r.Z})"));
		found.AddRange(Repeats(7, file.Headings, r => r.ConditionRef, r => $"heading {r.Degrees}"));
		found.AddRange(Repeats(8, file.WaypointGroups, r => r.ConditionRef, r => $"waypoints {string.Join(", ", r.Waypoints)}"));
		found.AddRange(Repeats(9, file.TriggerAreas, r => r.ConditionRef, r => r.Shape == 0
			? $"box, points {r.PointRef} and {r.SecondPointOrRadius}"
			: $"circle r {r.SecondPointOrRadius * 10} about point {r.PointRef}"));
		return found;
	}

	private static IEnumerable<RepeatedGuid> Repeats<T>(int row, T[]? records, Func<T, short> condition, Func<T, string> payload) where T : MapObject =>
		(records ?? [])
			.Select((record, index) => (record, index))
			.Where(x => x.record.GUID != -1 && condition(x.record) == -1)
			.GroupBy(x => x.record.GUID)
			.Where(g => g.Count() > 1)
			.Select(g => new RepeatedGuid(row, g.Key, g.Select(x => new RepeatedRecord(x.index, payload(x.record))).ToList()));

	public static void Write(TextWriter output, IReadOnlyList<RepeatQueryMission> missions) {
		foreach (var mission in missions) {
			foreach (var guid in mission.Guids) {
				output.WriteLine($"{mission.Mission}  row {guid.Row}  GUID {guid.Guid}: "
					+ string.Join("; ", guid.Records.Select(r => $"#{r.Index} {r.Payload}"))
					+ $" — reads #{guid.Records[^1].Index}");
			}
		}

		int carrying = missions.Count(m => m.Guids.Count > 0);
		var byRow = missions.SelectMany(m => m.Guids).GroupBy(g => g.Row).OrderBy(g => g.Key)
			.Select(g => $"row {g.Key}: {g.Count()} in {missions.Count(m => m.Guids.Any(x => x.Row == g.Key))}");
		output.WriteLine($"{missions.Sum(m => m.Guids.Count)} repeated GUID(s) in {carrying} of {missions.Count} mission(s)"
			+ (carrying > 0 ? $" ({string.Join(", ", byRow)})." : "."));
	}
}
