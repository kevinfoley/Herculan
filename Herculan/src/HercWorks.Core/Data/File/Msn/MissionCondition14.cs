using System.Text;

namespace HercWorks.Core.Data.File.Msn;

/// <summary>
/// Row #1 (14 bytes/record) — the mission's conditions. Every other row's condition field names one
/// of these by its <see cref="MapObject.GUID"/>, and a record whose condition names no surviving
/// row-1 record is dropped at load. <see cref="Type"/> decides how a row-1 record itself survives —
/// 0 a flag comparison, 1 a random draw, 2 a range over its parent's draw, 3 a variant key's parent —
/// and type 3 is what the other rows' variant keys name. The load is
/// <see cref="Io.Transform.Common.MissionGenerator"/>; the rules are
/// docs/retail/formats/msn-mission-file.md#the-conditions--row-1.
/// </summary>
public class MissionCondition14 : MapObject {
	/// <summary>0x02 — this record's own condition; for type 2, its parent.</summary>
	public short ConditionRef { get; set; }
	public const int ConditionRefWord = 0x02 / 2;

	/// <summary>0x04 — 0 a flag comparison, 1 a draw, 2 a range over the parent's draw, 3 a variant key's parent.</summary>
	public short Type { get; set; }
	public const int TypeWord = 0x04 / 2;

	/// <summary>0x06 — the flag index (type 0), the draw's bound (type 1), the range's low end (type 2) or the variant key (type 3).</summary>
	public short FlagIndexOrRangeLower { get; set; }
	public const int FlagIndexOrRangeLowerWord = 0x06 / 2;

	/// <summary>
	/// 0x08 — for type 0 the 0x119-0x11e operator code, overwritten at load with the result; for type
	/// 1 the draw, written at load; for type 2 the range's high end.
	/// </summary>
	public short OperatorOrRangeUpperOrResult { get; set; }
	public const int OperatorOrRangeUpperOrResultWord = 0x08 / 2;

	/// <summary>0x0A — the comparison operand (type 0), or the bound a variant is drawn below (type 3).</summary>
	public short ComparisonOperand { get; set; }
	public const int ComparisonOperandWord = 0x0A / 2;

	/// <summary>0x0C — 0 in every file; the load stores a type-3 record's latest variant draw here.</summary>
	public short LatestDraw { get; set; }
	public const int LatestDrawWord = 0x0C / 2;

	public MissionCondition14() { }

	public MissionCondition14(short guid, short conditionRef, short type, short flagIndexOrRangeLower,
		short operatorOrRangeUpperOrResult, short comparisonOperand, short latestDraw) {
		GUID = guid;
		ConditionRef = conditionRef;
		Type = type;
		FlagIndexOrRangeLower = flagIndexOrRangeLower;
		OperatorOrRangeUpperOrResult = operatorOrRangeUpperOrResult;
		ComparisonOperand = comparisonOperand;
		LatestDraw = latestDraw;
	}

	public override string ToString() {
		var b = new StringBuilder();
		b.Append('[')
			.Append(GUID).Append(", ")
			.Append(ConditionRef).Append(", ")
			.Append(Type).Append(", ")
			.Append(FlagIndexOrRangeLower).Append(", ")
			.Append(OperatorOrRangeUpperOrResult).Append(", ")
			.Append(ComparisonOperand).Append(", ")
			.Append(LatestDraw)
			.Append(']');

		return b.ToString();
	}
}
