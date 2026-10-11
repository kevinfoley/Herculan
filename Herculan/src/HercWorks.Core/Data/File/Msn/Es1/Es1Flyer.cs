namespace HercWorks.Core.Data.File.Msn.Es1;

/// <summary>
/// Row 9 (64 bytes) — a flyer (docs/es1/formats/msn-mission-file.md#flyers). Unset values and
/// exported fields are as <see cref="Es1Mech"/>'s summary describes.
/// </summary>
public class Es1Flyer : MapObject {
	/// <summary>0x02 — condition ref.</summary>
	public short ConditionRef { get; set; }

	/// <summary>0x04 — variant key; a variant copies 0x08–0x3F.</summary>
	public short VariantKey { get; set; }

	/// <summary>0x06 — not read by the load, not exported.</summary>
	public short Word06 { get; set; }

	/// <summary>0x08–0x2F — 20 words, exported; unset 0.</summary>
	public short[] Words08 { get; set; } = new short[20];

	/// <summary>0x30 — point ref (row 3).</summary>
	public short PointRef { get; set; }

	/// <summary>0x32 — heading ref (row 4).</summary>
	public short HeadingRef { get; set; }

	/// <summary>0x34 — the type, which the simulator's first pass keeps.</summary>
	public short Type { get; set; }

	/// <summary>0x36 — a word, exported; unset <c>-1</c>.</summary>
	public short Word36 { get; set; }

	/// <summary>0x38 — a word, exported; unset <c>-1</c>.</summary>
	public short Word38 { get; set; }

	/// <summary>0x3A — action ref (row 7).</summary>
	public short ActionRef3A { get; set; }

	/// <summary>0x3C — action ref (row 7).</summary>
	public short ActionRef3C { get; set; }

	/// <summary>0x3E — a word, exported; unset 100.</summary>
	public short Word3E { get; set; }
}
