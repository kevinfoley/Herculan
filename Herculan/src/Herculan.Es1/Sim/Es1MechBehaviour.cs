namespace Herculan.Es1.Sim;

/// <summary>
/// The HERC's behaviour states: <c>Behaviour_BuildStateTable</c> (<c>ES1DBSIM 0000fee4</c>)'s twenty descriptors, the three
/// dispatchers and <c>Mech_AiTick</c> (<c>0000d8e8</c>) (docs/es1/simulation/shields-and-behaviour-states.md#the-hercs-state-table,
/// docs/es1/simulation/mission-groups.md#the-tick). The move is <see cref="Es1MechMovement.MovementTick"/> wherever the
/// table says walk.
/// </summary>
public static class Es1MechBehaviour {
	/// <summary>The descriptor indices construction and the reassess install.</summary>
	public const int Deciding = 0, Player = 1;

	/// <summary>
	/// The descriptor indices a group's order installs (<see cref="Ai.Es1StateChoice"/>), and those the AI's thinks install,
	/// <c>BehaviourStateTable</c> + <c>0x28</c> × the index.
	/// </summary>
	public const int Patrolling = 7, Travelling = 8, BulldogTravel = 9, SearchDestroy = 10, Sleeping = 11, Skirting = 12,
		Guarding = 13, DrivingOff = 14, Ramming = 15, Fleeing = 16;

	/// <summary>The descriptor indices the damage write installs (docs/es1/simulation/mech.md#the-damage-write--mech_componentdamagewrite-00013250).</summary>
	public const int InLimbo = 17, Dead = 18, Disabled = 19;

	/// <summary><c>BehaviourStateTable</c> (<c>000af26c</c>), descriptor <i>n</i> at <c>+0x28</c> × <i>n</i>.</summary>
	public static IReadOnlyList<Es1BehaviourDescriptor> States { get; } = BuildStateTable();

	/// <summary>
	/// <c>Mech_AiTick</c> (<c>0000d8e8</c>): with a descriptor, the reassess when the countdown is 0; the countdown run
	/// unless the (new) descriptor's flag 0 is set; the move; the think, its non-zero answer zeroing the countdown, unless
	/// <c>+0xa9</c> is set, which is cleared instead; then <c>Behaviour_CountTick</c>.
	/// </summary>
	public static void AiTick(Es1Mech mech, Es1SimGlobals globals) {
		var block = mech.Behaviour;
		if (block.Descriptor == null) {
			return;
		}

		if (block.Countdown == 0) {
			DispatchReassess(mech, globals);
		}

		if (!block.Descriptor.Flags[0]) {
			int countdown = block.Countdown;
			globals.Clock.TimerCountDown(ref countdown);
			block.Countdown = countdown;
		}

		DispatchMove(mech, globals);
		if (!mech.ThinkSuppressed) {
			if (DispatchThink(mech, globals) != 0) {
				block.Countdown = 0;
			}
		} else {
			mech.ThinkSuppressed = false;
		}

		block.CountTick();
	}

	/// <summary><c>Behaviour_DispatchMove</c> (<c>000112b8</c>), slot <c>+0x18</c>: the descriptor's move, or 0 without one.</summary>
	public static short DispatchMove(Es1Mech mech, Es1SimGlobals globals) =>
		mech.Behaviour.Descriptor?.Move is { } move ? move(mech, globals) : (short)0;

	/// <summary><c>Behaviour_DispatchThink</c> (<c>000112e0</c>), slot <c>+0x1c</c>: the descriptor's think, or 0 without one.</summary>
	public static short DispatchThink(Es1Mech mech, Es1SimGlobals globals) =>
		mech.Behaviour.Descriptor?.Think is { } think ? think(mech, globals) : (short)0;

	/// <summary><c>Behaviour_DispatchReassess</c> (<c>00011308</c>), slot <c>+0x20</c>: the descriptor's reassess, or 0 without one.</summary>
	public static short DispatchReassess(Es1Mech mech, Es1SimGlobals globals) =>
		mech.Behaviour.Descriptor?.Reassess is { } reassess ? reassess(mech, globals) : (short)0;

	/// <summary>
	/// <c>Mech_BehaviourInertThink</c> (<c>00018308</c>), the think of <c>dead</c> and <c>disabled</c>
	/// (docs/es1/simulation/mech-ai.md#the-combat-states): the control law with no speed asked for and no steer
	/// (<see cref="Es1Locomotion.LocomotionTick"/>), so the machine walks its speed off, or falls when immobilized; it
	/// answers 0.
	/// </summary>
	private static short InertThink(Es1Mech mech, Es1SimGlobals globals) {
		mech.Locomotion.LocomotionTick(globals, 0, 0, 0);
		return 0;
	}

	/// <summary>
	/// <c>Behaviour_BuildStateTable</c>: name, dwell, flag mask, think, move, reassess and the <c>+0x24</c> word of each of
	/// the twenty states, in table order.
	/// </summary>
	private static Es1BehaviourDescriptor[] BuildStateTable() {
		Es1StateFunction walk = Es1MechMovement.MovementTick;
		Es1StateFunction select = Ai.Es1StateChoice.SelectBehaviour;
		var combat = Unported("Mech_AiCombatReassess", 0x00016e04, "8c");
		Es1StateFunction travel = Ai.Es1NavigationStates.TravelThink;
		Es1StateFunction inert = InertThink;
		return [
			new("deciding", 0, 0, null, null, select, 3),
			new("player", 10, 1, Es1PlayerThink.Think, walk, null, 3),
			new("attacking", 50000, 2, Unported("Mech_BehaviourAttackThink", 0x000163a0, "8c"), walk, combat, 0),
			new("flanking", 50000, 2, Unported("Mech_BehaviourFlankThink", 0x000173c8, "8c"), walk, combat, 0),
			new("facing off", 50000, 2, Unported("Mech_BehaviourFaceOffThink", 0x000172f0, "8c"), walk, combat, 0),
			new("attacking base", 50000, 2, Unported("Mech_BehaviourAttackBaseThink", 0x00016718, "8c"), walk, combat, 0),
			new("attacking flyer", 50000, 2, Unported("Mech_BehaviourAttackFlyerThink", 0x0001684c, "8c"), walk, combat, 0),
			new("patrolling", 10, 1, Ai.Es1NavigationStates.PatrolThink, walk, select, 3),
			new("travelling", 10, 1, travel, walk, select, 3),
			new("bulldog travel", 10, 9, travel, walk, select, 3),
			new("search destroy", 10, 1, Ai.Es1NavigationStates.SearchDestroyThink, walk, select, 3),
			new("sleeping", 10, 9, Ai.Es1NavigationStates.SleepThink, walk, select, 3),
			new("skirting", 10, 1, Unported("Mech_BehaviourSkirtThink", 0x00017aa4, "8e"), walk, select, 0),
			new("guarding", 10, 5, Ai.Es1NavigationStates.GuardThink, walk, select, 3),
			new("driving off en", 50000, 6, Unported("Mech_BehaviourDriveOffThink", 0x00017c84, "8c"), walk, select, 0),
			new("ramming", 10, 9, Unported("Mech_BehaviourRamThink", 0x00018320, "8e"),
				Unported("Mech_BehaviourRamTick", 0x00018278, "8e"), select, 0),
			new("fleeing", 30000, 0x12, Unported("Mech_BehaviourFleeThink", 0x0001713c, "8c"), walk, combat, 5),
			new("in limbo", 10, 0x21, null, null, null, 6),
			new("dead", 0, 0x21, inert, walk, null, 6),
			new("disabled", 0, 0x21, inert, walk, null, 7),
		];
	}

	/// <summary>
	/// PLACEHOLDER: a state function the slice does not port, which answers 0 and does nothing; <paramref name="step"/> is
	/// the part of plan step 8 that ports it (docs/herculan/plan-es1-first-mission.md#steps).
	/// </summary>
	private static Es1StateFunction Unported(string name, uint address, string step) => new UnportedState(name, address, step).Run;

	/// <summary>An unported state function, named for the debugger.</summary>
	private sealed record UnportedState(string Name, uint Address, string Step) {
		public short Run(Es1Mech mech, Es1SimGlobals globals) => 0;
	}
}
