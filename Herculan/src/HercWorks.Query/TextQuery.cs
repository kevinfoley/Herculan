namespace HercWorks.Query;

/// <summary>One <c>.ENG</c> record, in file order: a later record with the same id replaces the line when its condition holds.</summary>
/// <param name="Condition">What the record's condition asks, or null for an unconditioned record.</param>
internal sealed record TextRecord(short Id, short ConditionRef, string? Condition, string Text);

internal sealed record TextQueryMission(string Mission, IReadOnlyList<TextRecord> Records);

/// <summary>
/// <c>text</c>: a mission's <c>.ENG</c> records with their conditions decoded
/// (docs/retail/formats/msn-mission-file.md#the-eng-string-table).
/// </summary>
internal static class TextQuery {
	public static List<TextQueryMission> Run(RetailData data, IReadOnlyCollection<string> missions, short lowId, short highId) =>
		data.Missions
			.Where(m => missions.Contains(m.Name, StringComparer.OrdinalIgnoreCase))
			.Select(m => new TextQueryMission(m.Name, (m.Text?.Strings ?? [])
				.Where(s => s.Guid >= lowId && s.Guid <= highId)
				.Select(s => new TextRecord(s.Guid, s.ConditionRef, Conditions.Describe(m.File, s.ConditionRef), s.Val ?? ""))
				.ToList()))
			.ToList();

	public static void Write(TextWriter o, IReadOnlyList<TextQueryMission> missions) {
		foreach (var m in missions) {
			o.WriteLine($"{m.Mission}: {m.Records.Count} text records");
			foreach (var r in m.Records) {
				o.WriteLine(r.Condition == null ? $"  {r.Id}, always" : $"  {r.Id}, cond {r.ConditionRef}: {r.Condition}");
				o.WriteLine($"      {r.Text.Replace("\n", "\\n")}");
			}

			o.WriteLine();
		}
	}
}
