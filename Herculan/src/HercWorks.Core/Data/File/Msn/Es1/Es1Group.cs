namespace HercWorks.Core.Data.File.Msn.Es1;

/// <summary>
/// Row 11 (88 bytes) — a group (docs/es1/formats/msn-mission-file.md#groups). Unset values and
/// exported fields are as <see cref="Es1Mech"/>'s summary describes.
/// </summary>
public class Es1Group : MapObject {
	/// <summary>0x02 — condition ref.</summary>
	public short ConditionRef { get; set; }

	/// <summary>0x04 — not read by the load, not exported.</summary>
	public short Word04 { get; set; }

	/// <summary>0x06–0x2D — 20 words, exported; unset 0.</summary>
	public short[] Words06 { get; set; } = new short[20];

	/// <summary>0x2E — the row <see cref="MemberRefs"/> name: 0 mechs, 1 flyers, 2 bases.</summary>
	public short MemberKind { get; set; }

	/// <summary>0x30 — the formation, an index into <c>formations.dat</c>'s formations (docs/es1/formats/formations-dat.md); exported, unset <c>-1</c>.</summary>
	public short Word30 { get; set; }

	/// <summary>0x32 — the order, the AI state the members take (docs/es1/simulation/mech-ai.md#choosing-a-state--mech_aiselectbehaviour-0001892c); exported, unset <c>-1</c>.</summary>
	public short Word32 { get; set; }

	/// <summary>0x34 — point ref (row 3).</summary>
	public short PointRef { get; set; }

	/// <summary>0x36 — heading ref (row 4).</summary>
	public short HeadingRef { get; set; }

	/// <summary>0x38 — waypoint group ref (row 5).</summary>
	public short WaypointGroupRef { get; set; }

	/// <summary>0x3A–0x49 — eight member refs into the row <see cref="MemberKind"/> names.</summary>
	public short[] MemberRefs { get; set; } = new short[8];

	/// <summary>0x4A — the side, its low byte; exported, unset <c>-1</c>. For a base member the simulator's first pass keeps it beside the base's type.</summary>
	public short Word4A { get; set; }

	/// <summary>0x4C — the row <see cref="Ref4E"/> names: 0 groups, 1 mechs, 2 flyers, 3 bases.</summary>
	public short Ref4ERow { get; set; }

	/// <summary>0x4E — a ref into the row <see cref="Ref4ERow"/> names.</summary>
	public short Ref4E { get; set; }

	/// <summary>0x50 — action ref (row 7).</summary>
	public short ActionRef { get; set; }

	/// <summary>0x52 — a word, exported; unset <c>-1</c>.</summary>
	public short Word52 { get; set; }

	/// <summary>0x54 — the out-of-action flag number (docs/es1/formats/msn-mission-file.md#flyers); exported, unset <c>-1</c>.</summary>
	public short Word54 { get; set; }

	/// <summary>0x56 — the out-of-action operation, with <see cref="Word54"/>; exported, unset <c>-1</c>.</summary>
	public short Word56 { get; set; }
}
