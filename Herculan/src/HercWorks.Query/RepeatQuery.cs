using System.Text.Json.Serialization;
using HercWorks.Core.Data.File.Msn;
using HercWorks.Core.Data.File.Msn.Es1;

namespace HercWorks.Query;

/// <summary>One record of a repeated GUID.</summary>
/// <param name="Index">The record's position in its row.</param>
/// <param name="Condition">The row-1 GUID its condition field references, <c>-1</c> for none.</param>
/// <param name="Payload">What the record says, in brief: a point's coordinates, a heading, a route's waypoints; empty for rows 10 to 16.</param>
internal sealed record RepeatedRecord(int Index, short Condition, string Payload);

/// <summary>
/// A GUID that two or more records of one row carry: in rows #3 and #6 to #9 the unconditioned ones, which replace each other;
/// in rows #10 to #16 every one, conditioned or not, which overlay each other.
/// </summary>
/// <param name="Records">In file order.</param>
/// <param name="NoGuidBefore">
/// ES1 only: how many records with GUID <c>-1</c> precede the first. ES1's load finds the slot it replaces by counting the
/// survivors before it whose GUID is not <c>-1</c> (docs/es1/formats/msn-mission-file.md#filtering-variants-and-repeats),
/// so when one of those survives, the replacement lands on an earlier slot than the first record's.
/// </param>
internal sealed record RepeatedGuid(int Row, short Guid, IReadOnlyList<RepeatedRecord> Records,
	[property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? NoGuidBefore = null) {
	/// <summary>Whether its records carry different conditions.</summary>
	public bool MixesConditions => Records.Any(record => record.Condition != Records[0].Condition);
}

internal sealed record RepeatQueryMission(string Mission, IReadOnlyList<RepeatedGuid> Guids);

/// <summary>
/// <c>repeats</c>: the GUIDs that two or more unconditioned records carry in rows #3, #6, #7, #8 and #9, where each later
/// record replaces the earlier, so every ref to the GUID reads the last; and the GUIDs two or more records carry in rows #10
/// to #16, where each later surviving record overlays the fields it sets, with whether those records carry different
/// conditions (docs/retail/formats/msn-mission-file.md#repeated-guids).
/// </summary>
internal static class RepeatQuery {
	private static readonly int[] OverlayRows = [10, 11, 12, 13, 14, 15, 16];

	public static IReadOnlyList<RepeatQueryMission> Run(RetailData data, IReadOnlyCollection<string> missions) =>
		data.Missions
			.Where(m => missions.Count == 0 || missions.Contains(m.Name, StringComparer.OrdinalIgnoreCase))
			.Select(m => new RepeatQueryMission(m.Name, Search(m.File)))
			.ToList();

	private static List<RepeatedGuid> Search(MissionFile file) {
		var found = new List<RepeatedGuid>();
		found.AddRange(Repeats(3, file.MovieChoices, r => r.ConditionRef, r => $"value {r.Value}", unconditionedOnly: true));
		found.AddRange(Repeats(6, file.Points, r => r.ConditionRef, r => $"({r.X}, {r.Y}, {r.Z})", unconditionedOnly: true));
		found.AddRange(Repeats(7, file.Headings, r => r.ConditionRef, r => $"heading {r.Degrees}", unconditionedOnly: true));
		found.AddRange(Repeats(8, file.WaypointGroups, r => r.ConditionRef, r => $"waypoints {string.Join(", ", r.Waypoints)}",
			unconditionedOnly: true));
		found.AddRange(Repeats(9, file.TriggerAreas, r => r.ConditionRef, r => r.Shape == 0
			? $"box, points {r.PointRef} and {r.SecondPointOrRadius}"
			: $"circle r {r.SecondPointOrRadius * 10} about point {r.PointRef}", unconditionedOnly: true));
		found.AddRange(Repeats(10, file.Actions, r => r.ConditionRef, _ => "", unconditionedOnly: false));
		found.AddRange(Repeats(11, file.ActionTimers, r => r.ConditionRef, _ => "", unconditionedOnly: false));
		found.AddRange(Repeats(12, file.Mechs, r => r.ConditionRef, _ => "", unconditionedOnly: false));
		found.AddRange(Repeats(13, file.Flyers, r => r.ConditionRef, _ => "", unconditionedOnly: false));
		found.AddRange(Repeats(14, file.Bases, r => r.ConditionRef, _ => "", unconditionedOnly: false));
		found.AddRange(Repeats(15, file.Orders, r => r.ConditionRef, _ => "", unconditionedOnly: false));
		found.AddRange(Repeats(16, file.Groups, r => r.ConditionRef, _ => "", unconditionedOnly: false));
		return found;
	}

	/// <summary>
	/// <see cref="Run"/> over an ES1 install: its rows 3 to 6, the points, headings, waypoint groups and trigger areas, whose
	/// later unconditioned records replace the earlier (docs/es1/formats/msn-mission-file.md#filtering-variants-and-repeats).
	/// </summary>
	public static IReadOnlyList<RepeatQueryMission> RunEs1(RetailData data, IReadOnlyCollection<string> missions) =>
		data.Es1Missions
			.Where(m => missions.Count == 0 || missions.Contains(m.Name, StringComparer.OrdinalIgnoreCase))
			.Select(m => new RepeatQueryMission(m.Name, SearchEs1(m.File)))
			.ToList();

	private static List<RepeatedGuid> SearchEs1(Es1MissionFile file) {
		var found = new List<RepeatedGuid>();
		found.AddRange(Repeats(3, file.Points, r => r.ConditionRef, r => $"({r.X}, {r.Y}, {r.Z})", unconditionedOnly: true, es1: true));
		found.AddRange(Repeats(4, file.Headings, r => r.ConditionRef, r => $"heading {r.Value}", unconditionedOnly: true, es1: true));
		found.AddRange(Repeats(5, file.WaypointGroups, r => r.ConditionRef, r => $"waypoints {string.Join(", ", r.Waypoints)}",
			unconditionedOnly: true, es1: true));
		found.AddRange(Repeats(6, file.TriggerAreas, r => r.ConditionRef, r => r.Shape == 0
			? $"box, points {r.PointRef} and {r.Word0A}"
			: $"shape {r.Shape} about point {r.PointRef}, word 0x0a {r.Word0A}", unconditionedOnly: true, es1: true));
		return found;
	}

	private static IEnumerable<RepeatedGuid> Repeats<T>(int row, T[]? records, Func<T, short> condition, Func<T, string> payload,
			bool unconditionedOnly, bool es1 = false) where T : MapObject {
		var all = records ?? [];
		return all
			.Select((record, index) => (record, index))
			.Where(x => x.record.GUID != -1 && (!unconditionedOnly || condition(x.record) == -1))
			.GroupBy(x => x.record.GUID)
			.Where(g => g.Count() > 1)
			.Select(g => new RepeatedGuid(row, g.Key,
				g.Select(x => new RepeatedRecord(x.index, condition(x.record), payload(x.record))).ToList(),
				es1 ? all.Take(g.First().index).Count(r => r.GUID == -1) : null));
	}

	public static void Write(TextWriter output, IReadOnlyList<RepeatQueryMission> missions) {
		foreach (var mission in missions) {
			foreach (var guid in mission.Guids) {
				bool overlays = OverlayRows.Contains(guid.Row);
				output.WriteLine($"{mission.Mission}  row {guid.Row}  GUID {guid.Guid}: "
					+ string.Join("; ", guid.Records.Select(r => $"#{r.Index}"
						+ (r.Payload.Length > 0 ? $" {r.Payload}" : "")
						+ (overlays ? r.Condition == -1 ? " always" : $" when {r.Condition}" : "")))
					+ (overlays ? guid.MixesConditions ? " — mixes conditions" : ""
						: guid.NoGuidBefore is > 0
							? $" — {guid.NoGuidBefore} GUID -1 record(s) precede #{guid.Records[0].Index}, so #{guid.Records[^1].Index} replaces an earlier slot when they survive"
							: $" — reads #{guid.Records[^1].Index}"));
			}
		}

		int carrying = missions.Count(m => m.Guids.Count > 0);
		var byRow = missions.SelectMany(m => m.Guids).GroupBy(g => g.Row).OrderBy(g => g.Key)
			.Select(g => $"row {g.Key}: {g.Count()} in {missions.Count(m => m.Guids.Any(x => x.Row == g.Key))}"
				+ (OverlayRows.Contains(g.Key) ? $", {g.Count(x => x.MixesConditions)} mixing conditions" : ""));
		output.WriteLine($"{missions.Sum(m => m.Guids.Count)} repeated GUID(s) in {carrying} of {missions.Count} mission(s)"
			+ (carrying > 0 ? $" ({string.Join("; ", byRow)})." : "."));
	}
}
