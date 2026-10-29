using HercWorks.Core.Data.File.Msn.Es1;

namespace HercWorks.Query;

/// <summary>One row-1 record: what it asks, read through its chain, and every record its GUID gates.</summary>
/// <param name="Index">The record's position in row 1.</param>
/// <param name="Type">0 a flag test, 1 a draw, 2 a range of its parent's draw, 3 a random table.</param>
internal sealed record ConditionRecord(int Index, short Guid, short Type, string Description, IReadOnlyList<GatedRecord> Gates);

internal sealed record ConditionQueryMission(string Mission, IReadOnlyList<ConditionRecord> Conditions);

/// <summary>
/// <c>conditions</c>: every row-1 record of the named missions, with the records its GUID gates
/// (docs/retail/formats/msn-mission-file.md#the-conditions--row-1). <c>flag</c> lists only the type-0
/// records testing one flag; this lists the draws and random tables between them as well.
/// </summary>
internal static class ConditionQuery {
	public static List<ConditionQueryMission> Run(RetailData data, IReadOnlyCollection<string> missions) =>
		data.Missions
			.Where(m => missions.Count == 0 || missions.Contains(m.Name, StringComparer.OrdinalIgnoreCase))
			.Select(m => new ConditionQueryMission(m.Name, (m.File.Conditions ?? [])
				.Select((c, i) => new ConditionRecord(i, c.GUID, c.Type, Conditions.DescribeRecord(m.File, c), FlagQuery.Gates(m, c.GUID)))
				.ToList()))
			.ToList();

	/// <summary>
	/// <see cref="Run"/> over an ES1 install, whose row 1 is ES2's record with ES1's operator codes
	/// (docs/es1/formats/msn-mission-file.md#conditions), gating ES1's eleven rows.
	/// </summary>
	public static List<ConditionQueryMission> RunEs1(RetailData data, IReadOnlyCollection<string> missions) =>
		data.Es1Missions
			.Where(m => missions.Count == 0 || missions.Contains(m.Name, StringComparer.OrdinalIgnoreCase))
			.Select(m => new ConditionQueryMission(m.Name, m.File.Conditions
				.Select((c, i) => new ConditionRecord(i, c.GUID, c.Type, Conditions.DescribeRecord(m.File, c), Es1Gates(m.File, c.GUID)))
				.ToList()))
			.ToList();

	/// <summary>Every record of an ES1 mission whose condition field is <paramref name="guid"/>, labelled by ES1's row numbers.</summary>
	private static List<GatedRecord> Es1Gates(Es1MissionFile file, short guid) {
		var gates = new List<GatedRecord>();
		void Add<T>(int row, T[] records, Func<T, short> condition, Func<T, int, short> id, string noun) {
			for (int i = 0; i < records.Length; i++) {
				if (condition(records[i]) == guid) {
					short key = id(records[i], i);
					gates.Add(new GatedRecord(row.ToString(), key, $"row {row} {noun} {key}"));
				}
			}
		}

		Add(1, file.Conditions, r => r.ConditionRef, (r, _) => r.GUID, "cond");
		Add(2, file.HeaderPatches, r => r.ConditionRef, (_, i) => (short)i, "patch");
		Add(3, file.Points, r => r.ConditionRef, (r, _) => r.GUID, "point GUID");
		Add(4, file.Headings, r => r.ConditionRef, (r, _) => r.GUID, "heading GUID");
		Add(5, file.WaypointGroups, r => r.ConditionRef, (r, _) => r.GUID, "waypoint group GUID");
		Add(6, file.TriggerAreas, r => r.ConditionRef, (r, _) => r.GUID, "area GUID");
		Add(7, file.Actions, r => r.ConditionRef, (r, _) => r.GUID, "action GUID");
		Add(8, file.Mechs, r => r.ConditionRef, (r, _) => r.GUID, "mech GUID");
		Add(9, file.Flyers, r => r.ConditionRef, (r, _) => r.GUID, "flyer GUID");
		Add(10, file.Bases, r => r.ConditionRef, (r, _) => r.GUID, "base GUID");
		Add(11, file.Groups, r => r.ConditionRef, (r, _) => r.GUID, "group GUID");
		return gates;
	}

	public static void Write(TextWriter o, IReadOnlyList<ConditionQueryMission> missions) {
		foreach (var m in missions) {
			o.WriteLine($"{m.Mission}: {m.Conditions.Count} conditions");
			foreach (var c in m.Conditions) {
				o.WriteLine($"  #{c.Index} cond {c.Guid}, type {c.Type}: {c.Description}");
				if (c.Gates.Count > 0) {
					o.WriteLine($"      gates {string.Join(", ", c.Gates.Select(g => g.Label))}");
				}
			}

			o.WriteLine();
		}
	}
}
