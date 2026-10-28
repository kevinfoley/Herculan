using Herculan.Es1.Ai;

namespace Herculan.Es1.Sim;

/// <summary>
/// The aircraft's behaviour states: <c>Flyer_BuildStateTable</c> (<c>ES1DBSIM 000105b0</c>)'s seven descriptors, the
/// flyer's three dispatch slots and <c>Mech_AiTick</c> (<c>0000d8e8</c>) on a flyer
/// (docs/es1/simulation/shields-and-behaviour-states.md#the-aircrafts-state-table,
/// docs/es1/simulation/mission-groups.md#the-tick). The move is <see cref="Es1FlyerFlight.MovementTick"/>, the thinks and
/// the reassess <see cref="Es1FlyerAi"/>'s; <c>attacking</c>'s think is a stub answering 0 until part F3
/// (docs/herculan/plan-es1-first-mission.md#after-step-8).
/// </summary>
public static class Es1FlyerBehaviour {
	/// <summary>The descriptor indices, <c>FlyerBehaviourStateTable</c> + <c>0x24</c> × the index.</summary>
	public const int Deciding = 0, Attacking = 1, Patrolling = 2, SearchAndDestroy = 3, Sleeping = 4, Scouting = 5, Dead = 6;

	/// <summary>The sound catalog row <c>Flyer_DispatchMove</c> plays at the flyer.</summary>
	public const short SoundRow = 0x10;

	/// <summary><c>FlyerBehaviourStateTable</c> (<c>000af58c</c>), descriptor <i>n</i> at <c>+0x24</c> × <i>n</i>.</summary>
	public static IReadOnlyList<Es1BehaviourDescriptor> States { get; } = BuildStateTable();

	/// <summary>
	/// <c>Mech_AiTick</c> on a flyer, through its table's slots: with a descriptor, the reassess (slot <c>+0x20</c>) when
	/// the countdown is 0; the countdown run unless the (new) descriptor's flag 0 is set; the move (slot <c>+0x18</c>,
	/// <see cref="DispatchMove"/>); the think (slot <c>+0x1c</c>), its non-zero answer zeroing the countdown, unless
	/// <c>+0xa9</c> is set, which is cleared instead; then <c>Behaviour_CountTick</c>.
	/// </summary>
	public static void AiTick(Es1Flyer flyer, Es1SimGlobals globals) {
		var block = flyer.Behaviour;
		if (block.Descriptor == null) {
			return;
		}

		if (block.Countdown == 0) {
			DispatchReassess(flyer, globals);
		}

		if (!block.Descriptor.Flags[0]) {
			int countdown = block.Countdown;
			globals.Clock.TimerCountDown(ref countdown);
			block.Countdown = countdown;
		}

		DispatchMove(flyer, globals);
		if (!flyer.ThinkSuppressed) {
			if (DispatchThink(flyer, globals) != 0) {
				block.Countdown = 0;
			}
		} else {
			flyer.ThinkSuppressed = false;
		}

		block.CountTick();
	}

	/// <summary>
	/// Slot <c>+0x18</c>, <c>Flyer_DispatchMove</c> (<c>0001afa8</c>): the state's move with the flyer, or 0 for a state
	/// without one. PLACEHOLDER: before it, catalog row <see cref="SoundRow"/> at the flyer's position while it is not
	/// destroyed (<c>Sound_PlayAt</c> when the row is not playing, otherwise <c>Sound_OfferPosition</c>), and
	/// <c>Sound_Stop</c> of the row once it is; ES1's sound has no plan step
	/// (docs/herculan/plan-es1-first-mission.md#what-the-slice-leaves-out).
	/// </summary>
	public static short DispatchMove(Es1Flyer flyer, Es1SimGlobals globals) =>
		flyer.Behaviour.Descriptor?.FlyerMove is { } move ? move(flyer, globals) : (short)0;

	/// <summary>Slot <c>+0x1c</c>, <c>Behaviour_DispatchThinkFlyerCopy</c> (<c>0001b034</c>): the state's think, or 0 without one.</summary>
	public static short DispatchThink(Es1Flyer flyer, Es1SimGlobals globals) =>
		flyer.Behaviour.Descriptor?.FlyerThink is { } think ? think(flyer, globals) : (short)0;

	/// <summary>Slot <c>+0x20</c>, <c>Behaviour_DispatchReassessFlyerCopy</c> (<c>0001b05c</c>): the state's reassess, or 0 without one.</summary>
	public static short DispatchReassess(Es1Flyer flyer, Es1SimGlobals globals) =>
		flyer.Behaviour.Descriptor?.FlyerReassess is { } reassess ? reassess(flyer, globals) : (short)0;

	/// <summary>
	/// <c>Flyer_BehaviourAttackThink</c> (<c>0001c3f8</c>). Unported: part F3 ports it with the attack run. Until then it
	/// answers 0, and as <c>attacking</c>'s flag 0 stops its countdown, a flyer that engages stays in it, its move stepping
	/// it along its nose with the rates it last had.
	/// </summary>
	private static short AttackThink(Es1Flyer flyer, Es1SimGlobals globals) => 0;

	/// <summary><c>Flyer_BuildStateTable</c>: name, dwell, flag mask, think, move and reassess of each of the seven states.</summary>
	private static Es1BehaviourDescriptor[] BuildStateTable() {
		Es1FlyerStateFunction fly = Es1FlyerFlight.MovementTick;
		Es1FlyerStateFunction select = Es1FlyerAi.SelectBehaviour;
		return [
			new("deciding", 0, 0, null, null, select),
			new("attacking", 50000, 1, AttackThink, fly, select),
			new("patrolling", 5000, 0, Es1FlyerAi.PatrolThink, fly, select),
			new("search and destroy", 5000, 0, Es1FlyerAi.SearchDestroyThink, fly, select),
			new("sleeping", 500, 1, null, null, null),
			new("scouting", 5000, 0, Es1FlyerAi.ScoutThink, fly, select),
			new("dead", 500, 1, null, null, null),
		];
	}
}
