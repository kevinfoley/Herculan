namespace HercWorks.Core.Data.File.Msn;

/// <summary>
/// Row #4 (144 bytes/record) — the mission's text: its objective, briefing and intelligence lines
/// as <c>.ENG</c> string ids, and its briefing movie. No GUID; a campaign load hands the first
/// surviving record to the career block, and its objective lines become <c>script.dat</c> block 13
/// (<see cref="Script.ScriptDat.ObjectiveTextRefs"/>). See docs/retail/formats/msn-mission-file.md,
/// "Row #4 field decode".
/// </summary>
public class MissionText144 {
	/// <summary>0x00 — condition ref.</summary>
	public short ConditionRef { get; set; }
	public const int ConditionRefWord = 0x00 / 2;

	/// <summary>0x02-0x15 — the objective lines as the player is shown them, <c>.ENG</c> ids, <c>-1</c> for an unused slot.</summary>
	public short[] ObjectiveLines { get; set; } = new short[10];
	public const int ObjectiveLinesWord = 0x02 / 2;

	/// <summary>0x16-0x51 — the briefing lines, <c>.ENG</c> ids.</summary>
	public short[] BriefingLines { get; set; } = new short[30];

	/// <summary>0x52-0x8D — the intelligence lines, <c>.ENG</c> ids.</summary>
	public short[] IntelligenceLines { get; set; } = new short[30];

	/// <summary>0x8E — ref into row #3 (<see cref="VariantValue8"/>), replaced at load by that record's value: the briefing movie id.</summary>
	public short MovieRef { get; set; }
	public const int MovieRefWord = 0x8E / 2;
}
