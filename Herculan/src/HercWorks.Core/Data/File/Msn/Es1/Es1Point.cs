namespace HercWorks.Core.Data.File.Msn.Es1;

/// <summary>
/// Row 3 (22 bytes) — a point, ES2's row 6 field for field
/// (docs/retail/formats/msn-mission-file.md#row-6-field-decode--the-point-record-dat_0047064e-22-bytesrecord).
/// </summary>
public class Es1Point : MapObject {
	/// <summary>0x02 — condition ref.</summary>
	public short ConditionRef { get; set; }

	/// <summary>0x04 — random table key; a template supplies X, Y and Z (docs/es1/formats/msn-mission-file.md#filtering-random-tables-and-repeats).</summary>
	public short RandomTableKey { get; set; }

	/// <summary>0x06 — unnamed, as in ES2's record.</summary>
	public short Word06 { get; set; }

	/// <summary>0x08 — the sum flag.</summary>
	public short SumFlag { get; set; }

	/// <summary>0x0A — X.</summary>
	public int X { get; set; }

	/// <summary>0x0E — Y.</summary>
	public int Y { get; set; }

	/// <summary>0x12 — Z.</summary>
	public int Z { get; set; }
}
