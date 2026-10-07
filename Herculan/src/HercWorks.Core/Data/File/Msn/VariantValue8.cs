namespace HercWorks.Core.Data.File.Msn;

/// <summary>
/// Row #3 (8 bytes/record) — a condition-gated value. Several records may share a GUID with
/// different conditions; the load keeps the last one to survive. Rows #4 and #5 name one by GUID
/// and are given its <see cref="Value"/> — the briefing or debrief movie id.
/// See docs/retail/formats/msn-mission-file.md, "Row #3 field decode".
/// </summary>
public class VariantValue8 : MapObject {
	/// <summary>0x02 — condition ref.</summary>
	public short ConditionRef { get; set; }
	public const int ConditionRefWord = 0x02 / 2;

	/// <summary>0x04 — compound-condition partner: <c>-99</c> exactly when <see cref="ConditionRef"/> is set, else <c>-1</c>.</summary>
	public short CompoundConditionPartner { get; set; }

	/// <summary>0x06 — the value a row-4 or row-5 ref is replaced by.</summary>
	public short Value { get; set; }
	public const int ValueWord = 0x06 / 2;
}
