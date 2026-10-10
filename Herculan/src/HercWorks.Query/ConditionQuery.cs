namespace HercWorks.Query;

/// <summary>One row-1 record: what it asks, read through its chain, and every record its GUID gates.</summary>
/// <param name="Index">The record's position in row 1.</param>
/// <param name="Type">0 a flag test, 1 a draw, 2 a range of its parent's draw, 3 a variant table.</param>
internal sealed record ConditionRecord(int Index, short Guid, short Type, string Description, IReadOnlyList<GatedRecord> Gates);

internal sealed record ConditionQueryMission(string Mission, IReadOnlyList<ConditionRecord> Conditions);

/// <summary>
/// <c>conditions</c>: every row-1 record of the named missions, with the records its GUID gates
/// (docs/retail/formats/msn-mission-file.md#the-conditions--row-1). <c>flag</c> lists only the type-0
/// records testing one flag; this lists the draws and variant tables between them as well.
/// </summary>
internal static class ConditionQuery {
	public static List<ConditionQueryMission> Run(RetailData data, IReadOnlyCollection<string> missions) =>
		data.Missions
			.Where(m => missions.Count == 0 || missions.Contains(m.Name, StringComparer.OrdinalIgnoreCase))
			.Select(m => new ConditionQueryMission(m.Name, (m.File.Conditions ?? [])
				.Select((c, i) => new ConditionRecord(i, c.GUID, c.Type, Conditions.DescribeRecord(m.File, c), FlagQuery.Gates(m, c.GUID)))
				.ToList()))
			.ToList();

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
