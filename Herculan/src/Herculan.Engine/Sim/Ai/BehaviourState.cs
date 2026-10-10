namespace Herculan.Engine.Sim.Ai;

/// <summary>
/// Which of the two reassess implementations a behaviour state installs in its <c>+0x30</c> slot.
/// The roster splits cleanly in two and there is no third — see
/// docs/retail/simulation/ai-dispatch.md, "The 22 states".
/// </summary>
public enum ReassessSlot {
	/// <summary>No reassess at all: the player's two states, <c>in limbo</c>, <c>dead</c>, <c>disabled</c>.</summary>
	None,

	/// <summary><c>Mech_AiSelectBehaviour</c> (<c>0041eb34</c>) — <c>deciding</c> and states 8-17.</summary>
	SelectBehaviour,

	/// <summary><c>Mech_AiCombatReassess</c> (<c>0041cf18</c>) — states 3-7 and 18.</summary>
	CombatReassess
}

/// <summary>
/// Which think function a behaviour state installs in its <c>+0x18</c> slot. <see cref="None"/> is
/// the two states that genuinely have none — see docs/retail/simulation/ai-navigation.md,
/// docs/retail/simulation/ai-combat-states.md and docs/retail/simulation/ai-dispatch.md.
/// </summary>
public enum ThinkSlot {
	/// <summary>No think: <c>deciding</c> and <c>in limbo</c>.</summary>
	None,

	/// <summary>
	/// <c>Mech_BehaviourPlayerThink</c> (<c>0041c194</c>), shared by <c>player</c> and
	/// <c>player fly</c>: the mission-progress slot the machine the player is flying runs in place of
	/// an AI one. See docs/retail/simulation/player-waypoints.md.
	/// </summary>
	Player,

	/// <summary><c>Mech_BehaviourPatrolThink</c> (<c>0041d7d0</c>).</summary>
	Patrol,

	/// <summary><c>Mech_BehaviourSearchDestroyThink</c> (<c>0041d60c</c>).</summary>
	SearchDestroy,

	/// <summary><c>Mech_BehaviourTravelThink</c> (<c>0041d9cc</c>), shared by both travel states.</summary>
	Travel,

	/// <summary><c>Mech_BehaviourFollowThink</c> (<c>0041daac</c>).</summary>
	Follow,

	/// <summary><c>Mech_BehaviourGuardThink</c> (<c>0041e224</c>).</summary>
	Guard,

	/// <summary><c>Mech_BehaviourAttackThink</c> (<c>0041c594</c>).</summary>
	Attack,

	/// <summary><c>Mech_BehaviourFlankThink</c> (<c>0041d4e4</c>).</summary>
	Flank,

	/// <summary><c>Mech_BehaviourFaceOffThink</c> (<c>0041d41c</c>).</summary>
	FaceOff,

	/// <summary><c>Mech_BehaviourAttackBaseThink</c> (<c>0041c86c</c>).</summary>
	AttackBase,

	/// <summary><c>Mech_BehaviourAttackFlyerThink</c> (<c>0041c9cc</c>).</summary>
	AttackFlyer,

	/// <summary><c>Mech_BehaviourSkirtThink</c> (<c>0041dd64</c>).</summary>
	Skirt,

	/// <summary><c>Mech_BehaviourDriveOffThink</c> (<c>0041def0</c>), which is <see cref="FaceOff"/>'s.</summary>
	DriveOff,

	/// <summary><c>Mech_BehaviourFleeThink</c> (<c>0041d2c4</c>).</summary>
	Flee,

	/// <summary><c>Mech_BehaviourSleepThink</c> (<c>0041c418</c>).</summary>
	Sleep,

	/// <summary><c>Mech_BehaviourInertThink</c> (<c>0041e554</c>), shared by <c>dead</c> and <c>disabled</c>.</summary>
	Inert,

	/// <summary><c>Mech_BehaviourRamThink</c> (<c>0041e570</c>), the one AI think with a move slot of its own.</summary>
	Ram
}

/// <summary>
/// Which move function a behaviour state installs in its <c>+0x24</c> slot, which
/// <c>Behaviour_DispatchMove</c> (<c>00415afc</c>) calls once per AI tick ahead of the think. See
/// docs/retail/simulation/ai-dispatch.md, "The 22 states".
/// </summary>
public enum MoveSlot {
	/// <summary>No move: <c>deciding</c> and <c>in limbo</c>. The dispatcher returns without calling anything.</summary>
	None,

	/// <summary><c>Mech_MovementTick</c> (<c>0041a360</c>), the walk — the other 19 states.</summary>
	Walk,

	/// <summary><c>Razor_MovementTick</c> (<c>004198f4</c>) — <c>player fly</c>.</summary>
	Fly,

	/// <summary><c>Mech_BehaviourRamTick</c> (<c>0041e488</c>) — <c>ramming</c>.</summary>
	Ram
}

/// <summary>
/// One of DBSIM's 22 behaviour state descriptors — the <c>0x3e</c>-byte records at
/// <c>BehaviourStateTable</c> (<c>004993a4</c>) that <c>Behaviour_BuildStateTable</c>
/// (<c>00413ed4</c>) fills at startup. Field meanings, the flag-bit consumers and the whole
/// dispatch model are in docs/retail/simulation/ai-dispatch.md; this is a transcription of the table the
/// initialiser writes, read out of the disassembly rather than out of any data file.
///
/// <para><b>The move slot is dispatched from the object pass, not the AI tick.</b>
/// <see cref="MechObject.Tick"/> runs <see cref="Move"/> for every machine ahead of the group pass that
/// runs the think, so the original's "integrate on the last think's decisions" ordering falls out.</para>
/// </summary>
public sealed class BehaviourState {
	private BehaviourState(int index, string name, int dwell, int flags, ReassessSlot reassess,
			ThinkSlot think = ThinkSlot.None, int objectiveLine = 3, MoveSlot move = MoveSlot.Walk) {
		Index = index;
		Name = name;
		Dwell = dwell;
		Flags = flags;
		Reassess = reassess;
		Think = think;
		ObjectiveLine = objectiveLine;
		Move = move;
	}

	/// <inheritdoc cref="MoveSlot"/>
	public MoveSlot Move { get; }

	/// <summary>
	/// Descriptor <c>+0x3c</c> — the <c>STRINGS0.STR</c> group 40 index the [F7] comm box prints on a
	/// squadmate's <c>OBJECTIVE:</c> line. Four values are used across the table: 0 <c>ATTACK</c>,
	/// 3 <c>FORM UP</c>, 5 <c>FLEE</c>, 6 <c>DEAD</c> and 7 <c>IMMOBILE</c>. Read only by
	/// <see cref="MechObject.SquadOrderLineIndex"/>.
	/// </summary>
	public int ObjectiveLine { get; }

	/// <summary>The state's index into the three parallel tables — descriptor <c>004993a4 + 0x3e*N</c>.</summary>
	public int Index { get; }

	/// <summary>Descriptor <c>+0x00</c> — the game's own name for the state, out of <c>BehaviourStateNames</c>.</summary>
	public string Name { get; }

	/// <summary>
	/// Descriptor <c>+0x04</c> — the dwell time, in <see cref="SimMath.TimerCountDown"/>'s unit and
	/// not in milliseconds: the 50000 the combat states state is about 24 seconds. Only meaningful
	/// for the eight states whose <see cref="SuppressesDwell"/> is clear; for the rest the countdown
	/// is loaded and never stepped.
	/// </summary>
	public int Dwell { get; }

	/// <summary>
	/// Descriptor <c>+0x08</c> as the 16-bit mask it is built from, before
	/// <c>Behaviour_ExpandFlagBits</c> unpacks it to one byte per bit. Only bits 0-5 are ever set.
	/// </summary>
	public int Flags { get; }

	/// <inheritdoc cref="ReassessSlot"/>
	public ReassessSlot Reassess { get; }

	/// <inheritdoc cref="ThinkSlot"/>
	public ThinkSlot Think { get; }

	/// <summary>
	/// Bit 0 — <see cref="MechObject.AiTick"/> does not run the dwell countdown. True of every state
	/// but <c>deciding</c>, the five combat states, <c>driving off en</c> and <c>fleeing</c>, so
	/// those eight are the only ones a clock can end.
	/// </summary>
	public bool SuppressesDwell => (Flags & 0x01) != 0;

	/// <summary>
	/// Bit 1 — committed to a fight. Suppresses a fresh reaction to incoming fire, keeps the combat
	/// reassess's leader sweep from re-entering this machine, and discounts a candidate that lacks it
	/// in <see cref="AiTargeting.SelectTarget"/>.
	/// </summary>
	public bool Committed => (Flags & 0x02) != 0;

	/// <summary>Bit 2 — holding a place: fire is answered by defending the post rather than chasing the shooter.</summary>
	public bool HoldsPlace => (Flags & 0x04) != 0;

	/// <summary>Bit 3 — incoming fire is ignored entirely.</summary>
	public bool IgnoresFire => (Flags & 0x08) != 0;

	/// <summary>
	/// Bit 4 — this machine has already broken off, which is <c>fleeing</c> and nothing else. The
	/// squad order handler refuses <c>ATTACK MY TARGET</c> to a machine in it.
	/// </summary>
	public bool BrokenOff => (Flags & 0x10) != 0;

	/// <summary>
	/// Bits 4 and 5 as <see cref="AiTargeting.TargetStateTier"/> reads them off a <i>target's</i>
	/// descriptor: 2 for bit 5 (<c>in limbo</c>, <c>dead</c>, <c>disabled</c>), 1 for bit 4
	/// (<c>fleeing</c>), 0 otherwise.
	/// </summary>
	public int DisengageTier => (Flags & 0x20) != 0 ? 2 : (Flags & 0x10) != 0 ? 1 : 0;

	/// <inheritdoc />
	public override string ToString() => Name;

	// The table, in index order. Dwell times and flag masks are the immediates
	// Behaviour_BuildStateTable writes; the reassess column is which of the two functions its +0x30
	// triple names.
	public static readonly BehaviourState Deciding = new(0, "deciding", 0, 0x00, ReassessSlot.SelectBehaviour, move: MoveSlot.None);
	public static readonly BehaviourState Player = new(1, "player", 10, 0x01, ReassessSlot.None, ThinkSlot.Player);
	public static readonly BehaviourState PlayerFly = new(2, "player fly", 10, 0x01, ReassessSlot.None, ThinkSlot.Player, move: MoveSlot.Fly);
	public static readonly BehaviourState Attacking = new(3, "attacking", 50000, 0x02, ReassessSlot.CombatReassess, ThinkSlot.Attack, objectiveLine: 0);
	public static readonly BehaviourState Flanking = new(4, "flanking", 50000, 0x02, ReassessSlot.CombatReassess, ThinkSlot.Flank, objectiveLine: 0);
	public static readonly BehaviourState FacingOff = new(5, "facing off", 50000, 0x02, ReassessSlot.CombatReassess, ThinkSlot.FaceOff, objectiveLine: 0);
	public static readonly BehaviourState AttackingBase = new(6, "attacking base", 50000, 0x02, ReassessSlot.CombatReassess, ThinkSlot.AttackBase, objectiveLine: 0);
	public static readonly BehaviourState AttackingFlyer = new(7, "attacking flyer", 50000, 0x02, ReassessSlot.CombatReassess, ThinkSlot.AttackFlyer, objectiveLine: 0);
	public static readonly BehaviourState Patrolling = new(8, "patrolling", 10, 0x01, ReassessSlot.SelectBehaviour, ThinkSlot.Patrol);
	public static readonly BehaviourState Travelling = new(9, "travelling", 10, 0x01, ReassessSlot.SelectBehaviour, ThinkSlot.Travel);
	public static readonly BehaviourState Following = new(10, "following", 10, 0x01, ReassessSlot.SelectBehaviour, ThinkSlot.Follow);
	public static readonly BehaviourState BulldogTravel = new(11, "bulldog travel", 10, 0x09, ReassessSlot.SelectBehaviour, ThinkSlot.Travel);
	public static readonly BehaviourState SearchDestroy = new(12, "search/destroy", 10, 0x01, ReassessSlot.SelectBehaviour, ThinkSlot.SearchDestroy);
	public static readonly BehaviourState Sleeping = new(13, "sleeping", 10, 0x09, ReassessSlot.SelectBehaviour, ThinkSlot.Sleep);
	public static readonly BehaviourState Skirting = new(14, "skirting", 10, 0x03, ReassessSlot.SelectBehaviour, ThinkSlot.Skirt, objectiveLine: 0);
	public static readonly BehaviourState Guarding = new(15, "guarding", 10, 0x05, ReassessSlot.SelectBehaviour, ThinkSlot.Guard);
	public static readonly BehaviourState DrivingOffEnemy = new(16, "driving off en", 50000, 0x06, ReassessSlot.SelectBehaviour, ThinkSlot.DriveOff, objectiveLine: 0);
	public static readonly BehaviourState Ramming = new(17, "ramming", 10, 0x09, ReassessSlot.SelectBehaviour, ThinkSlot.Ram, objectiveLine: 0, move: MoveSlot.Ram);
	public static readonly BehaviourState Fleeing = new(18, "fleeing", 15000, 0x12, ReassessSlot.CombatReassess, ThinkSlot.Flee, objectiveLine: 5);
	public static readonly BehaviourState InLimbo = new(19, "in limbo", 10, 0x21, ReassessSlot.None, objectiveLine: 6, move: MoveSlot.None);
	public static readonly BehaviourState Dead = new(20, "dead", 0, 0x21, ReassessSlot.None, ThinkSlot.Inert, objectiveLine: 6);
	public static readonly BehaviourState Disabled = new(21, "disabled", 0, 0x21, ReassessSlot.None, ThinkSlot.Inert, objectiveLine: 7);

	/// <summary>All 22, in the order the descriptor table holds them.</summary>
	public static readonly IReadOnlyList<BehaviourState> All = new[] {
		Deciding, Player, PlayerFly, Attacking, Flanking, FacingOff, AttackingBase, AttackingFlyer,
		Patrolling, Travelling, Following, BulldogTravel, SearchDestroy, Sleeping, Skirting,
		Guarding, DrivingOffEnemy, Ramming, Fleeing, InLimbo, Dead, Disabled
	};
}

/// <summary>
/// The behaviour block embedded in every machine at <c>mech+0x4d</c> — <c>0x45</c> bytes running to
/// <c>mech+0x91</c>, of which the three fields below are what the dispatch layer reads.
/// <c>Behaviour_SetState</c> (<c>00413e50</c>) is the only writer, and it has 30 call sites, which
/// are the state machine's edge list. See docs/retail/simulation/ai-dispatch.md.
/// </summary>
public struct BehaviourBlock {
	/// <summary>Block <c>+0x00</c> — the installed descriptor. Null before the constructor installs one.</summary>
	public BehaviourState? State { get; private set; }

	/// <summary>
	/// Block <c>+0x05</c> — the dwell countdown, in <see cref="SimMath.TimerCountDown"/>'s unit.
	/// Reaching zero is what makes
	/// <see cref="MechObject.AiTick"/> run the reassess slot; only a state whose
	/// <see cref="BehaviourState.SuppressesDwell"/> is clear ever counts down at all.
	/// </summary>
	public int DwellCountdown;

	/// <summary>Block <c>+0x09</c> — the AI ticks the state has run; see <see cref="CountTick"/>.</summary>
	public int TickCount;

	/// <summary>Block <c>+0x36</c> — set by <see cref="SetState"/>, cleared by the next <see cref="CountTick"/>.</summary>
	public bool JustInstalled;

	/// <summary>
	/// <c>Behaviour_SetState</c> (<c>00413e50</c>): installs a descriptor and arms its countdown at
	/// the descriptor's dwell plus a 0-15 count jitter, zeroing the tick counter and the block's two
	/// scratch regions — which here is everything the block holds.
	/// </summary>
	public void SetState(BehaviourState state) {
		State = state;
		DwellCountdown = state.Dwell + NextJitter();
		TickCount = 0;
		JustInstalled = true;
	}

	/// <summary>
	/// <c>Behaviour_CountTick</c> (<c>00413eb0</c>), the last step of every AI tick: the first call
	/// after <see cref="SetState"/> only clears <see cref="JustInstalled"/>, every later one counts.
	/// See docs/retail/simulation/ai-dispatch.md#the-mechs-behaviour-block--mech0x4d.
	/// </summary>
	public void CountTick() {
		if (JustInstalled) {
			JustInstalled = false;
			return;
		}

		TickCount++;
	}

	/// <summary>
	/// <c>Behaviour_DwellJitter</c> (<c>004a9bf4</c>): stepped by 13 per state change and masked to
	/// four bits, shared with <see cref="FlyerBehaviourBlock"/>. See
	/// docs/retail/simulation/ai-dispatch.md#the-mechs-behaviour-block--mech0x4d.
	/// </summary>
	internal static int NextJitter() {
		_jitter += 0xd;
		return _jitter & 0xf;
	}

	/// <summary>
	/// Puts the jitter counter back to the zero DBSIM starts each mission from. Retail gets that by
	/// flying one mission per process; this host flies several in one, so every
	/// <see cref="SimWorld"/> calls this as it is built. The counter stays a static rather than a
	/// field of the world because the state changes that step it carry no world reference.
	/// </summary>
	internal static void ResetJitter() => _jitter = 0;

	private static int _jitter;
}
