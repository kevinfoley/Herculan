using System.Text.Json.Serialization;
using HercWorks.Core.Data.File.Msn;
using HercWorks.Core.Data.File.Msn.Es1;

namespace HercWorks.Query;

/// <summary>One record of a repeated GUID.</summary>
/// <param name="Index">The record's position in its row.</param>
/// <param name="Payload">What the record says, in brief: a point's coordinates, a heading, a route's waypoints.</param>
internal sealed record RepeatedRecord(int Index, string Payload);

/// <summary>A GUID that two or more unconditioned records of one row carry.</summary>
/// <param name="Records">In file order. The load keeps the last, in the first one's slot.</param>
/// <param name="NoGuidBefore">
/// ES1 only: how many records with GUID <c>-1</c> precede the first. ES1's load finds the slot it replaces by counting the
/// survivors before it whose GUID is not <c>-1</c> (docs/es1/formats/msn-mission-file.md#filtering-variants-and-repeats),
/// so when one of those survives, the replacement lands on an earlier slot than the first record's.
/// </param>
internal sealed record RepeatedGuid(int Row, short Guid, IReadOnlyList<RepeatedRecord> Records,
	[property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? NoGuidBefore = null);

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

	/// <summary>
	/// <see cref="Run"/> over an ES1 install: its rows 3 to 6, the points, headings, waypoint groups and trigger areas, whose
	/// later records replace the earlier (docs/es1/formats/msn-mission-file.md#filtering-variants-and-repeats).
	/// </summary>
	public static IReadOnlyList<RepeatQueryMission> RunEs1(RetailData data, IReadOnlyCollection<string> missions) =>
		data.Es1Missions
			.Where(m => missions.Count == 0 || missions.Contains(m.Name, StringComparer.OrdinalIgnoreCase))
			.Select(m => new RepeatQueryMission(m.Name, SearchEs1(m.File)))
			.ToList();

	private static List<RepeatedGuid> SearchEs1(Es1MissionFile file) {
		var found = new List<RepeatedGuid>();
		found.AddRange(Repeats(3, file.Points, r => r.ConditionRef, r => $"({r.X}, {r.Y}, {r.Z})", es1: true));
		found.AddRange(Repeats(4, file.Headings, r => r.ConditionRef, r => $"heading {r.Value}", es1: true));
		found.AddRange(Repeats(5, file.WaypointGroups, r => r.ConditionRef, r => $"waypoints {string.Join(", ", r.Waypoints)}", es1: true));
		found.AddRange(Repeats(6, file.TriggerAreas, r => r.ConditionRef, r => r.Shape == 0
			? $"box, points {r.PointRef} and {r.Word0A}"
			: $"shape {r.Shape} about point {r.PointRef}, word 0x0a {r.Word0A}", es1: true));
		return found;
	}

	private static IEnumerable<RepeatedGuid> Repeats<T>(int row, T[]? records, Func<T, short> condition, Func<T, string> payload,
			bool es1 = false) where T : MapObject {
		var all = records ?? [];
		return all
			.Select((record, index) => (record, index))
			.Where(x => x.record.GUID != -1 && condition(x.record) == -1)
			.GroupBy(x => x.record.GUID)
			.Where(g => g.Count() > 1)
			.Select(g => new RepeatedGuid(row, g.Key, g.Select(x => new RepeatedRecord(x.index, payload(x.record))).ToList(),
				es1 ? all.Take(g.First().index).Count(r => r.GUID == -1) : null));
	}

	public static void Write(TextWriter output, IReadOnlyList<RepeatQueryMission> missions) {
		foreach (var mission in missions) {
			foreach (var guid in mission.Guids) {
				output.WriteLine($"{mission.Mission}  row {guid.Row}  GUID {guid.Guid}: "
					+ string.Join("; ", guid.Records.Select(r => $"#{r.Index} {r.Payload}"))
					+ (guid.NoGuidBefore is > 0
						? $" — {guid.NoGuidBefore} GUID -1 record(s) precede #{guid.Records[0].Index}, so #{guid.Records[^1].Index} replaces an earlier slot when they survive"
						: $" — reads #{guid.Records[^1].Index}"));
			}
		}

		int carrying = missions.Count(m => m.Guids.Count > 0);
		var byRow = missions.SelectMany(m => m.Guids).GroupBy(g => g.Row).OrderBy(g => g.Key)
			.Select(g => $"row {g.Key}: {g.Count()} in {missions.Count(m => m.Guids.Any(x => x.Row == g.Key))}");
		output.WriteLine($"{missions.Sum(m => m.Guids.Count)} repeated GUID(s) in {carrying} of {missions.Count} mission(s)"
			+ (carrying > 0 ? $" ({string.Join(", ", byRow)})." : "."));
	}
}
