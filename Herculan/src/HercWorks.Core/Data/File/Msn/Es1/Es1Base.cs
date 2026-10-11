namespace HercWorks.Core.Data.File.Msn.Es1;

/// <summary>
/// Row 10 (24 bytes) — a base (docs/es1/formats/msn-mission-file.md#bases). Unset values and
/// exported fields are as <see cref="Es1Mech"/>'s summary describes.
/// </summary>
public class Es1Base : MapObject {
	/// <summary>0x02 — condition ref.</summary>
	public short ConditionRef { get; set; }

	/// <summary>0x04 — variant key; a variant copies 0x08–0x17.</summary>
	public short VariantKey { get; set; }

	/// <summary>0x06 — not read by the load, not exported.</summary>
	public short Word06 { get; set; }

	/// <summary>0x08 — the type, which the simulator's first pass keeps.</summary>
	public short Type { get; set; }

	/// <summary>0x0A — point ref (row 3).</summary>
	public short PointRef { get; set; }

	/// <summary>0x0C — heading ref (row 4).</summary>
	public short HeadingRef { get; set; }

	/// <summary>0x0E — a word, exported; unset <c>-1</c>.</summary>
	public short Word0E { get; set; }

	/// <summary>0x10 — a word, exported; unset <c>-1</c>.</summary>
	public short Word10 { get; set; }

	/// <summary>0x12 — action ref (row 7).</summary>
	public short ActionRef12 { get; set; }

	/// <summary>0x14 — action ref (row 7).</summary>
	public short ActionRef14 { get; set; }

	/// <summary>0x16 — a word, exported; unset 100.</summary>
	public short Word16 { get; set; }
}
