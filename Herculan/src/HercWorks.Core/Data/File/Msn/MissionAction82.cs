namespace HercWorks.Core.Data.File.Msn;

/// <summary>
/// Row #10 (82 bytes/record) — a mission action, <c>script.dat</c> block 5: a one-shot latch that,
/// when activated, writes mission counters and posts a message, and that groups and orders wait on.
/// See docs/retail/formats/msn-mission-file.md, "Row #10 field decode", and
/// docs/retail/simulation/mission-deployment.md for what activates one and what it does.
/// </summary>
public class MissionAction82 : MapObject {
	/// <summary>0x02 — condition ref.</summary>
	public short ConditionRef { get; set; }
	public const int ConditionRefWord = 0x02 / 2;

	/// <summary>0x04 — always -1 in retail; the load does not read it.</summary>
	public short Unk04 { get; set; }

	/// <summary>
	/// 0x06, 0-10 — whose position the action's trigger areas test, and for 7-10 what
	/// <see cref="TargetRef"/> names (7/8/9 a mech, flyer or base, 10 a group).
	/// </summary>
	public short Type { get; set; }
	public const int TypeWord = 0x06 / 2;

	/// <summary>0x08 — how a group waiting on this action arrives.</summary>
	public short Verb { get; set; }

	/// <summary>0x0A-0x19 — refs into row #9 (<see cref="TriggerArea12"/>), <c>-1</c> for none. DBSIM stops at the first <c>-1</c>.</summary>
	public short[] AreaRefs { get; set; } = new short[8];
	public const int AreaRefsWord = 0x0A / 2;

	/// <summary>0x1A — how many of <see cref="CounterPairs"/> are filled, from the front; not exported to <c>script.dat</c>. See docs/retail/formats/msn-mission-file.md, "Row #10 field decode".</summary>
	public short Unk1A { get; set; }

	/// <summary>
	/// 0x1C-0x43 — ten interleaved (counter ref, operation) pairs: the mission counters the action
	/// writes when it activates, a counter ref of <c>-1</c> for an unused pair. Operation 6 increments
	/// the counter and 5 clears it. The export separates them into
	/// <see cref="Script.ScriptAction.CounterRefs"/> and <see cref="Script.ScriptAction.CounterOps"/>.
	/// </summary>
	public short[] CounterPairs { get; set; } = new short[20];
	public const int CounterPairsWord = 0x1C / 2;

	/// <summary>0x44-0x4D — <c>.ENG</c> text ids; exported, but DBSIM reads and drops them.</summary>
	public short[] TextRefs { get; set; } = new short[5];
	public const int TextRefsWord = 0x44 / 2;

	/// <summary>0x4E — the mission message the action posts, plus one; 0 for none.</summary>
	public short MessageId { get; set; }
	public const int MessageIdWord = 0x4E / 2;

	/// <summary>0x50 — for types 7-10, a ref into row #12, #13, #14 or #16 by type; otherwise kept as authored.</summary>
	public short TargetRef { get; set; }
	public const int TargetRefWord = 0x50 / 2;
}
