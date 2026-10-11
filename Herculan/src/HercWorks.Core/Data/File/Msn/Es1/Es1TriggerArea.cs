namespace HercWorks.Core.Data.File.Msn.Es1;

/// <summary>
/// Row 6 (12 bytes) — a trigger area, ES2's row 9 field for field
/// (docs/retail/formats/msn-mission-file.md#row-9-field-decode--the-trigger-area-record-dat_0047065e-12-bytesrecord).
/// </summary>
public class Es1TriggerArea : MapObject {
	/// <summary>0x02 — condition ref.</summary>
	public short ConditionRef { get; set; }

	/// <summary>0x04 — unnamed, as in ES2's record.</summary>
	public short Word04 { get; set; }

	/// <summary>0x06 — the shape; 0 makes <see cref="Word0A"/> a second point ref.</summary>
	public short Shape { get; set; }

	/// <summary>0x08 — point ref (row 3).</summary>
	public short PointRef { get; set; }

	/// <summary>0x0A — a second point ref (row 3) when <see cref="Shape"/> is 0.</summary>
	public short Word0A { get; set; }
}
