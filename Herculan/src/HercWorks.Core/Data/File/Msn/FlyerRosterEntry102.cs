namespace HercWorks.Core.Data.File.Msn;

/// <summary>
/// Row #13 (102 bytes/record) — one flyer the mission can field, exported as <c>script.dat</c> block 8
/// (<see cref="Script.ScriptFlyerRecord"/>). The member names match <see cref="Script.ScriptFlyerRecord"/>'s
/// and the engine's <c>MissionPlacement</c> where they are the same datum.
/// See docs/retail/formats/msn-mission-file.md, "Row #13 field decode".
/// </summary>
public class FlyerRosterEntry102 : MapObject {
	/// <summary>0x02 — condition ref.</summary>
	public short ConditionRef { get; set; }
	public const int ConditionRefWord = 0x02 / 2;

	/// <summary>
	/// 0x04 — variant key: unless <c>-1</c>, everything from 0x08 to 0x65 except <see cref="PairCount"/>
	/// is copied from a randomly picked variant (docs/retail/formats/msn-mission-file.md#variants).
	/// </summary>
	public short VariantKey { get; set; }
	public const int VariantKeyWord = 0x04 / 2;

	/// <summary>0x06 — always -1 in retail; the load does not read it.</summary>
	public short Unk06 { get; set; }

	/// <summary>0x08-0x2F — 20 shorts, each 0 or 1. What reads them is open (docs/retail/formats/msn-mission-file.md#open).</summary>
	public short[] FlagSpan { get; set; } = new short[20];
	public const int FlagSpanWord = 0x08 / 2;

	/// <summary>0x30 — ref into row #6 (<see cref="MapPoint22"/>): a spawn-position override.</summary>
	public short PositionRef { get; set; }
	public const int PositionRefWord = 0x30 / 2;

	/// <summary>0x32 — ref into row #7 (<see cref="Heading10"/>): a heading override.</summary>
	public short HeadingRef { get; set; }
	public const int HeadingRefWord = 0x32 / 2;

	/// <summary>0x34 — the flyer type, an index into <c>nam\FLYERS.NAM</c>.</summary>
	public short TypeIndex { get; set; }

	/// <summary>0x36 — how many of the pairs in <see cref="OutOfActionReport"/> are filled, from the front. Not exported to <c>script.dat</c>; a variant does not copy it.</summary>
	public short PairCount { get; set; }

	/// <summary>
	/// 0x38-0x5F — the flyer's out-of-action report: ten interleaved (counter ref, operation) pairs,
	/// a counter ref of <c>-1</c> for an unused one, written to the mission counters when it goes out
	/// of the fight. The export separates them into
	/// <see cref="Script.ScriptFlyerRecord.CounterRefs"/> and <see cref="Script.ScriptFlyerRecord.CounterOps"/>.
	/// </summary>
	public short[] OutOfActionReport { get; set; } = new short[20];
	public const int OutOfActionReportWord = 0x38 / 2;

	/// <summary>
	/// 0x60 — ref into row #10 (<see cref="MissionAction82"/>): the action this flyer fires when it is
	/// engaged, <c>-1</c> for none. DBSIM resolves it into the flyer's own <c>+0x1b2</c>, where a
	/// machine's lands (<see cref="MechRosterEntry144.EngagementActionRef"/>).
	/// </summary>
	public short EngagementActionRef { get; set; }
	public const int EngagementActionRefWord = 0x60 / 2;

	/// <summary>
	/// 0x62 — ref into row #10: the action this flyer fires when it is defeated, <c>-1</c> for none.
	/// DBSIM resolves it into the flyer's own <c>+0x1b6</c>, and <c>Flyer_ComponentDamageWrite</c>
	/// (<c>00421bb4</c>) fires it.
	/// </summary>
	public short DefeatActionRef { get; set; }
	public const int DefeatActionRefWord = 0x62 / 2;

	/// <summary>0x64 — always 100 in retail; what reads it is open (docs/retail/formats/msn-mission-file.md#open).</summary>
	public short UnkVal_100 { get; set; }
	public const int UnkVal_100Word = 0x64 / 2;
}
