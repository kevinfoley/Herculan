namespace HercWorks.Core.Data.File.Msn.Es1;

/// <summary>
/// Row 2 (22 bytes) — a header patch: a condition and ten header words, without ES2's thirty flag
/// indices. The load applies each patch whose condition passes and keeps none
/// (docs/es1/formats/msn-mission-file.md#header-patches).
/// </summary>
public class Es1HeaderPatch {
	/// <summary>0x00 — condition ref.</summary>
	public short ConditionRef { get; set; }

	/// <summary>
	/// 0x02–0x15 — the ten header words; the unset value is 1 for the first and 0 for the rest. What
	/// they mean is open (docs/es1/formats/msn-mission-file.md#open).
	/// </summary>
	public short[] HeaderWords { get; set; } = new short[10];
}
