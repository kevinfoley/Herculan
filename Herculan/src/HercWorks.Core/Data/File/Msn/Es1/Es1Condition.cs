namespace HercWorks.Core.Data.File.Msn.Es1;

/// <summary>
/// Row 1 (14 bytes) — a condition, ES2's record and its four types
/// (docs/retail/formats/msn-mission-file.md#the-conditions--row-1). A type-0 record compares one of
/// ES1's 100 flags under one of four operator codes (docs/es1/formats/msn-mission-file.md#conditions).
/// </summary>
public class Es1Condition : MapObject {
	/// <summary>0x02 — this record's own condition; for type 2, its parent.</summary>
	public short ConditionRef { get; set; }

	/// <summary>0x04 — 0 a flag comparison, 1 a draw, 2 a range over the parent's draw, 3 a variant key's parent.</summary>
	public short Type { get; set; }

	/// <summary>0x06 — the flag index (type 0), the draw's bound (type 1), the range's low end (type 2) or the variant key (type 3).</summary>
	public short FlagIndexOrRangeLower { get; set; }

	/// <summary>0x08 — for type 0 the operator, <c>0x114</c>–<c>0x117</c>; for type 1 the draw, written at load; for type 2 the range's high end.</summary>
	public short OperatorOrRangeUpperOrResult { get; set; }

	/// <summary>0x0A — the comparison operand (type 0), or the bound a variant is drawn below (type 3).</summary>
	public short ComparisonOperand { get; set; }

	/// <summary>0x0C — type 3's latest draw, written at load.</summary>
	public short LatestDraw { get; set; }
}
