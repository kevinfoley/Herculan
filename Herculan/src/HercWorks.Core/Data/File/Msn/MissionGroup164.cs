namespace HercWorks.Core.Data.File.Msn;

/// <summary>
/// Row #16 (164 bytes/record) — a mission group, <c>script.dat</c> block 11: the roster slots it
/// activates, where they stand, the orders they work through, and the action that brings them into
/// the mission. See docs/retail/formats/msn-mission-file.md, "Row #16 field decode", and
/// docs/retail/formats/script-dat.md#placement--the-actual-rule.
/// </summary>
public class MissionGroup164 : MapObject {
	/// <summary>0x02 — condition ref.</summary>
	public short ConditionRef { get; set; }
	public const int ConditionRefWord = 0x02 / 2;

	/// <summary>0x04 — compound-condition partner: -99, set only alongside <see cref="ConditionRef"/>.</summary>
	public short CompoundConditionPartner { get; set; }

	/// <summary>
	/// 0x06 — for a base group, set when the group stands on and paints its formation's terrain tile
	/// (docs/retail/formats/script-dat.md#base-formation-terrain).
	/// </summary>
	public short PaintsGround { get; set; }
	public const int PaintsGroundWord = 0x06 / 2;

	/// <summary>0x08 — 0 in all but one retail record; what reads it is not established.</summary>
	public short NearConstant { get; set; }

	/// <summary>0x0A-0x2D — 18 shorts, 0 in every retail record.</summary>
	public short[] DeadZone { get; set; } = new short[18];

	/// <summary>0x2E — which roster <see cref="MemberRefs"/> names: 0 mechs (row #12), 1 flyers (#13), 2 bases (#14).</summary>
	public short MemberKind { get; set; }
	public const int MemberKindWord = 0x2E / 2;

	/// <summary>0x30 — the formation the members spread into, by <see cref="MemberKind"/>'s formation table.</summary>
	public short FormationId { get; set; }

	/// <summary>0x32 — ref into row #6 (<see cref="MapPoint22"/>): the group's spawn point. Unset means the first waypoint of its first order's route.</summary>
	public short PositionRef { get; set; }
	public const int PositionRefWord = 0x32 / 2;

	/// <summary>0x34 — ref into row #7 (<see cref="Heading10"/>): the group's heading. Unset means the bearing of its route's first leg.</summary>
	public short HeadingRef { get; set; }
	public const int HeadingRefWord = 0x34 / 2;

	/// <summary>0x36 — ref into row #8 (<see cref="WaypointGroup"/>); the group's own route.</summary>
	public short RouteRef { get; set; }
	public const int RouteRefWord = 0x36 / 2;

	/// <summary>
	/// 0x38-0x5F — the roster slots the group activates, <c>-1</c> for unused; a member's slot index
	/// here is its formation slot, so slot 0 stands exactly on the group's point. Not read for record
	/// 0, the player's squad.
	/// </summary>
	public short[] MemberRefs { get; set; } = new short[20];
	public const int MemberRefsWord = 0x38 / 2;

	/// <summary>0x60-0x73 — refs into row #15 (<see cref="MissionOrder22"/>): the group's orders, in slot order.</summary>
	public short[] OrderRefs { get; set; } = new short[10];
	public const int OrderRefsWord = 0x60 / 2;

	/// <summary>0x74 — the group's side: 0 human, 1 Cybrid.</summary>
	public short Side { get; set; }
	public const int SideWord = 0x74 / 2;

	/// <summary>
	/// 0x76 — ref into row #10 (<see cref="MissionAction82"/>): when set, the group is not in the mission
	/// until that action fires (docs/retail/simulation/mission-deployment.md#the-deployment-gate--group0x14).
	/// </summary>
	public short DeploymentActionRef { get; set; }
	public const int DeploymentActionRefWord = 0x76 / 2;

	/// <summary>0x78 — how many of the pairs in <see cref="OutOfActionReport"/> are filled, from the front. Not exported to <c>script.dat</c>.</summary>
	public short PairCount { get; set; }

	/// <summary>
	/// 0x7A-0xA1 — the group's out-of-action report: ten interleaved (counter ref, operation) pairs,
	/// a counter ref of <c>-1</c> for an unused one, written once every member is out of the fight.
	/// The export separates them into <see cref="Script.ScriptGroup.CounterRefs"/> and
	/// <see cref="Script.ScriptGroup.CounterOps"/>, and <c>DBSim_BuildGroupRecord</c>
	/// (<c>00423b34</c>) copies the refs to <c>group+0x1c</c> and the operations to
	/// <c>group+0x30</c>. See docs/retail/simulation/mission-deployment.md#the-out-of-action-report.
	/// </summary>
	public short[] OutOfActionReport { get; set; } = new short[20];
	public const int OutOfActionReportWord = 0x7A / 2;

	/// <summary>0xA2 — for a base group, what VSHELL's briefing map writes into each member's shown field (docs/retail/shell/mission-map.md).</summary>
	public short MapShown { get; set; }
	public const int MapShownWord = 0xA2 / 2;
}
