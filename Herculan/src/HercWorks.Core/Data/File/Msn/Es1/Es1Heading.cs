namespace HercWorks.Core.Data.File.Msn.Es1;

/// <summary>
/// Row 4 (10 bytes) — a heading, ES2's row 7 field for field
/// (docs/retail/formats/msn-mission-file.md#row-7-field-decode--the-heading-record-dat_00470650-10-bytesrecord).
/// </summary>
public class Es1Heading : MapObject {
	/// <summary>0x02 — condition ref.</summary>
	public short ConditionRef { get; set; }

	/// <summary>0x04 — variant key; a variant copies the heading.</summary>
	public short VariantKey { get; set; }

	/// <summary>0x06 — unnamed, as in ES2's record.</summary>
	public short Word06 { get; set; }

	/// <summary>0x08 — the heading's value.</summary>
	public short Value { get; set; }
}
