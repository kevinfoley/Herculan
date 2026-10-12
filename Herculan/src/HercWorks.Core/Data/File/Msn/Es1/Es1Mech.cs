namespace HercWorks.Core.Data.File.Msn.Es1;

/// <summary>
/// Row 8 (106 bytes; 88 in a file in the other layout) — a mech (docs/es1/formats/msn-mission-file.md#mechs). Each unset value given
/// is the one a record with a repeated GUID leaves alone when it overlays an earlier one. Fields
/// the doc calls exported are copied to <c>data\script.dat</c> without the load reading them; what they
/// mean is open (docs/es1/formats/msn-mission-file.md#open).
/// </summary>
public class Es1Mech : MapObject {
	/// <summary>0x02 — condition ref.</summary>
	public short ConditionRef { get; set; }

	/// <summary>0x04 — variant key; a variant copies 0x08–0x69.</summary>
	public short VariantKey { get; set; }

	/// <summary>0x06 — not read by the load, not exported.</summary>
	public short Word06 { get; set; }

	/// <summary>0x08–0x2F — 20 words, exported; unset 0.</summary>
	public short[] Words08 { get; set; } = new short[20];

	/// <summary>0x30 — the type, which the simulator's first pass keeps for the slot.</summary>
	public short Type { get; set; }

	/// <summary>0x32–0x45 — ten words, exported; unset <c>-1</c>. The mode-0 squad builder copies the first <i>n</i>, <i>n</i> a slot count it looks up for <see cref="Type"/>.</summary>
	public short[] Words32 { get; set; } = new short[10];

	/// <summary>0x46 — point ref (row 3).</summary>
	public short PointRef { get; set; }

	/// <summary>0x48 — heading ref (row 4).</summary>
	public short HeadingRef { get; set; }

	/// <summary>0x4A — a word, exported; unset <c>-1</c>.</summary>
	public short Word4A { get; set; }

	/// <summary>0x4C — a word, exported; unset <c>-1</c>.</summary>
	public short Word4C { get; set; }

	/// <summary>
	/// 0x4E–0x61 — ten words, exported; unset 5. The mode-0 squad builder copies as many of them as of
	/// <see cref="Words32"/>'s. In a file in the other layout, one word, and the four fields after it
	/// stand 18 bytes earlier (docs/es1/formats/msn-mission-file.md#a-file-in-another-layout).
	/// </summary>
	public short[] Words4E { get; set; } = new short[10];

	/// <summary>0x62 — a word, exported; unset 2.</summary>
	public short Word62 { get; set; }

	/// <summary>0x64 — action ref (row 7).</summary>
	public short ActionRef64 { get; set; }

	/// <summary>0x66 — action ref (row 7).</summary>
	public short ActionRef66 { get; set; }

	/// <summary>0x68 — a word, exported; unset 100.</summary>
	public short Word68 { get; set; }
}
