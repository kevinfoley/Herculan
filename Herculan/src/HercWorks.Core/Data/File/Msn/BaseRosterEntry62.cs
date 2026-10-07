namespace HercWorks.Core.Data.File.Msn;

/// <summary>
/// Row #14 (62 bytes/record) — one structure the mission can place, exported as <c>script.dat</c>
/// block 9 (<see cref="Script.ScriptBaseRecord"/>). The member names match
/// <see cref="Script.ScriptBaseRecord"/>'s and the engine's <c>MissionPlacement</c> where they are
/// the same datum. See docs/retail/formats/msn-mission-file.md, "Row #14 field decode".
/// </summary>
public class BaseRosterEntry62 : MapObject {
	/// <summary>0x02 — condition ref.</summary>
	public short ConditionRef { get; set; }
	public const int ConditionRefWord = 0x02 / 2;

	/// <summary>
	/// 0x04 — variant key: unless <c>-1</c>, everything from 0x08 to 0x3D except <see cref="PairCount"/>
	/// is copied from a randomly picked variant (docs/retail/formats/msn-mission-file.md#variants).
	/// </summary>
	public short VariantKey { get; set; }
	public const int VariantKeyWord = 0x04 / 2;

	/// <summary>0x06 — always -1 in retail; the load does not read it.</summary>
	public short Unk06 { get; set; }

	/// <summary>
	/// 0x08 — the base type, an index into the 65-entry table in <c>dat\BASES.DAT</c> (which in turn
	/// names the model and its texture bank).
	/// </summary>
	public short TypeIndex { get; set; }
	public const int TypeIndexWord = 0x08 / 2;

	/// <summary>0x0A — ref into row #6 (<see cref="MapPoint22"/>): a spawn-position override.</summary>
	public short PositionRef { get; set; }
	public const int PositionRefWord = 0x0A / 2;

	/// <summary>0x0C — ref into row #7 (<see cref="Heading10"/>): a heading override.</summary>
	public short HeadingRef { get; set; }
	public const int HeadingRefWord = 0x0C / 2;

	/// <summary>0x0E — how many of the pairs in <see cref="OutOfActionReport"/> are filled, from the front. Not exported to <c>script.dat</c>; a variant does not copy it.</summary>
	public short PairCount { get; set; }

	/// <summary>
	/// 0x10-0x37 — the structure's out-of-action report: ten interleaved (counter ref, operation)
	/// pairs, a counter ref of <c>-1</c> for an unused one, written to the mission counters when it
	/// goes out of the fight. The export separates them
	/// into <see cref="Script.ScriptBaseRecord.CounterRefs"/> and <see cref="Script.ScriptBaseRecord.CounterOps"/>.
	/// The briefing map writes over the exported first two operations in its own copy only
	/// (docs/retail/shell/mission-map.md#what-it-reads).
	/// </summary>
	public short[] OutOfActionReport { get; set; } = new short[20];
	public const int OutOfActionReportWord = 0x10 / 2;

	/// <summary>
	/// 0x38 — ref into row #10 (<see cref="MissionAction82"/>): the action this structure fires when it
	/// is engaged, <c>-1</c> for none. DBSIM resolves it into the structure's own <c>+0x1b2</c>, where
	/// a machine's lands (<see cref="MechRosterEntry144.EngagementActionRef"/>).
	/// </summary>
	public short EngagementActionRef { get; set; }
	public const int EngagementActionRefWord = 0x38 / 2;

	/// <summary>
	/// 0x3A — ref into row #10: the action it fires when its last component goes, <c>-1</c> for none.
	/// DBSIM resolves it into the structure's own <c>+0x1b6</c>, and <c>Base_ApplyDamage</c>
	/// (<c>00404d70</c>) fires it.
	/// </summary>
	public short DefeatActionRef { get; set; }
	public const int DefeatActionRefWord = 0x3A / 2;

	/// <summary>
	/// 0x3C — the structure's starting condition, per cent: exported as block 9's <c>0x32</c>, which
	/// <c>Base_Construct</c> (<c>00405314</c>) reads (docs/retail/simulation/structure-behaviour.md). 100 or 0
	/// in retail, almost always 100 where <see cref="TypeIndex"/> is set (docs/retail/formats/msn-mission-file.md,
	/// "Row #14 field decode").
	/// </summary>
	public short StartingCondition { get; set; }
	public const int StartingConditionWord = 0x3C / 2;
}
