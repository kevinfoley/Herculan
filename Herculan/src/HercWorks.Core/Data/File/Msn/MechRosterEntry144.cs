namespace HercWorks.Core.Data.File.Msn;

/// <summary>
/// Row #12 (144 bytes/record) — one HERC the mission can field, exported as <c>script.dat</c> block 7
/// (<see cref="Script.ScriptMechRecord"/>): its type, weapon fit, AI settings, optional placement,
/// out-of-action report and action links. The member names match <see cref="Script.ScriptMechRecord"/>'s
/// and the engine's <c>MissionPlacement</c> where they are the same datum.
/// See docs/formats/msn-mission-file.md, "Row #12 field decode".
/// </summary>
public class MechRosterEntry144 : MapObject {
	/// <summary>0x02 — condition ref.</summary>
	public short ConditionRef { get; set; }
	public const int ConditionRefWord = 0x02 / 2;

	/// <summary>
	/// 0x04 — variant key: unless <c>-1</c>, everything from 0x08 to 0x8F except <see cref="PairCount"/>
	/// is copied from a randomly picked variant (docs/formats/msn-mission-file.md#variants).
	/// </summary>
	public short VariantKey { get; set; }
	public const int VariantKeyWord = 0x04 / 2;

	/// <summary>0x06 — compound-condition partner: -99 or 2, set only alongside <see cref="ConditionRef"/>.</summary>
	public short CompoundConditionPartner { get; set; }

	/// <summary>
	/// 0x08 — the machine's standing AI radar setting, 0 PASSIVE or 1 ACTIVE: the one an AI machine
	/// walks its route on. <c>DBSim_SpawnMissionObjects</c> (<c>004253d8</c>) copies it to
	/// <c>mech+0x97</c>. 0 or 1 in every retail record.
	/// </summary>
	public short AiRadarActive { get; set; }
	public const int AiRadarActiveWord = 0x08 / 2;

	/// <summary>
	/// 0x0A — the speed the machine's AI walks at, copied to <c>mech+0x252</c>. Zero, which is 91% of
	/// retail records, means the AI's own default. See <c>docs/simulation/ai-navigation.md</c>.
	/// </summary>
	public short AiCruiseSpeed { get; set; }

	/// <summary>0x0C-0x2F — 18 shorts, 0 in every retail record.</summary>
	public short[] DeadZone { get; set; } = new short[18];

	/// <summary>0x30 — the mech type, an index into <c>nam\MECHS.NAM</c>.</summary>
	public short TypeIndex { get; set; }
	public const int TypeIndexWord = 0x30 / 2;

	/// <summary>
	/// 0x32-0x45 — the 10-slot weapon fit, one weapon catalog id per slot, <c>-1</c> for empty; DBSIM
	/// hands it to <c>Mech_ConfigureLoadout</c>.
	/// <para><b>Slot positions are load-bearing.</b> Each hardpoint picks its slot by index, so
	/// compacting the array fits the wrong weapon to the wrong hardpoint and a slot no hardpoint
	/// addresses contributes nothing. See docs/simulation/weapon-mounts.md, "The join".</para>
	/// </summary>
	public short[] WeaponRefs { get; set; } = new short[10];
	public const int WeaponRefsWord = 0x32 / 2;

	/// <summary>0x46 — ref into row #6 (<see cref="MapPoint22"/>): a spawn-position override; unset means the group's point.</summary>
	public short PositionRef { get; set; }
	public const int PositionRefWord = 0x46 / 2;

	/// <summary>0x48 — ref into row #7 (<see cref="Heading10"/>): a heading override.</summary>
	public short HeadingRef { get; set; }
	public const int HeadingRefWord = 0x48 / 2;

	/// <summary>0x4A — how many of the pairs in <see cref="OutOfActionReport"/> are filled, from the front. Not exported to <c>script.dat</c>; a variant does not copy it.</summary>
	public short PairCount { get; set; }

	/// <summary>
	/// 0x4C-0x73 — the machine's out-of-action report: ten interleaved (counter ref, operation) pairs,
	/// a counter ref of <c>-1</c> for an unused one, written to the mission counters when it goes out
	/// of the fight. The export separates them into <see cref="Script.ScriptMechRecord.CounterRefs"/>
	/// and <see cref="Script.ScriptMechRecord.CounterOps"/>, and <c>DBSim_SpawnMissionObjects</c>
	/// (<c>004253d8</c>) copies the refs to <c>mech+0x1ba</c> through
	/// <c>SimObject_SetOutOfActionCounters</c> (<c>00411b90</c>). See
	/// docs/simulation/mission-deployment.md#the-out-of-action-report.
	/// </summary>
	public short[] OutOfActionReport { get; set; } = new short[20];
	public const int OutOfActionReportWord = 0x4C / 2;

	/// <summary>
	/// 0x74-0x87 — the second of the two parallel per-slot arrays <c>Mech_ConfigureLoadout</c> takes,
	/// alongside <see cref="WeaponRefs"/>. It is the ammunition type each missile launcher is loaded
	/// with, the value a launcher's mount resolves through <c>Proj_LookupRecord(Rocket, key)</c> and
	/// then prints as its name; non-launcher slots carry a filler 5.
	///
	/// <para>Located by the two stack locals <c>DBSim_SpawnMissionObjects</c> (<c>004253d8</c>) hands
	/// the loadout call, which sit exactly 64 bytes apart in a frame holding one exported record —
	/// placing the second array 64 bytes past <see cref="WeaponRefs"/> in the export, 0x74 here since
	/// the writer drops 0x4a between them; VSHELL's squad build reads it here too. Confirmed against
	/// the retail mission: every slot whose <see cref="WeaponRefs"/> entry is a launcher
	/// (<c>MSL10</c>, id 15) reads 1 here and every other slot reads 5.</para>
	/// </summary>
	public short[] WeaponSecondary { get; set; } = new short[10];
	public const int WeaponSecondaryWord = 0x74 / 2;

	/// <summary>0x88 — always 2 in retail; what reads it is not established.</summary>
	public short Constant2 { get; set; }
	public const int Constant2Word = 0x88 / 2;

	/// <summary>
	/// 0x8A — ref into row #10 (<see cref="MissionAction82"/>): the mission action this machine fires
	/// when it is <b>engaged</b>, <c>-1</c> for none. <c>DBSim_SpawnMissionObjects</c>
	/// (<c>004253d8</c>) resolves it into <c>mech+0x1b2</c>. It fires once a hostile is within
	/// 50,000 units and one side has spotted or shot the other; the three sites that fire it are in
	/// docs/simulation/mission-deployment.md, "An object's own two actions".
	/// </summary>
	public short EngagementActionRef { get; set; }
	public const int EngagementActionRefWord = 0x8A / 2;

	/// <summary>
	/// 0x8C — ref into row #10: the mission action this machine fires when it is <b>defeated</b>,
	/// resolved into <c>mech+0x1b6</c>. A machine fires it on death
	/// (<c>Mech_ComponentDamageWrite</c>, <c>00417de4</c>) and again on running out of working
	/// weapons; it is not a death action alone.
	///
	/// <para><b>This is how a retail mission chains its reinforcements.</b> The shipped
	/// <c>script.dat</c> has five of its ten mech records naming one, which is what brings each wave
	/// in as the last is beaten — see docs/simulation/mission-deployment.md.</para>
	/// </summary>
	public short DefeatActionRef { get; set; }
	public const int DefeatActionRefWord = 0x8C / 2;

	/// <summary>
	/// 0x8E — the machine's <b>starting condition, as a percentage</b>. 100 is pristine; anything
	/// under 80 has <c>DBSim_SpawnMissionObjects</c> pre-damage the machine through
	/// <c>Mech_ApplyStartingCondition</c> (<c>004178e8</c>) before it ever takes a shot, in four
	/// widening bands at 80 / 60 / 40 / 20. Below 20 the machine is placed as a <b>wreck</b>: a leg
	/// destroyed outright, immobilised and collapsed where it stands. See
	/// docs/simulation/component-damage.md#starting-condition--mech_applystartingcondition-004178e8.
	/// </summary>
	public short StartingCondition { get; set; }
	public const int StartingConditionWord = 0x8E / 2;
}
