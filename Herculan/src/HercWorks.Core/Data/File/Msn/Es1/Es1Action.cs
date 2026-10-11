namespace HercWorks.Core.Data.File.Msn.Es1;

/// <summary>
/// Row 7 (30 bytes) — an action (docs/es1/formats/msn-mission-file.md#actions). Fields the doc calls
/// exported are copied to <c>data\script.dat</c> without the load reading them; what they mean is open
/// (docs/es1/formats/msn-mission-file.md#open).
/// </summary>
public class Es1Action : MapObject {
	/// <summary>0x02 — condition ref.</summary>
	public short ConditionRef { get; set; }

	/// <summary>0x04 — not read by the load, not exported.</summary>
	public short Word04 { get; set; }

	/// <summary>0x06 — the type, exported.</summary>
	public short Type { get; set; }

	/// <summary>0x08 — the verb, exported.</summary>
	public short Verb { get; set; }

	/// <summary>0x0A–0x19 — eight trigger-area refs (row 6).</summary>
	public short[] AreaRefs { get; set; } = new short[8];

	/// <summary>0x1A — a word, exported; unset <c>-1</c>.</summary>
	public short Word1A { get; set; }

	/// <summary>0x1C — a word, exported; unset <c>-1</c>.</summary>
	public short Word1C { get; set; }
}
