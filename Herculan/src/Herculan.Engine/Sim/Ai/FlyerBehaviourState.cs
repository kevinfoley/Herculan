namespace Herculan.Engine.Sim.Ai;

/// <summary>
/// Which think function a flyer behaviour state installs in its <c>+0x18</c> slot.
/// </summary>
public enum FlyerThinkSlot {
	/// <summary>No think at all: <c>deciding</c>, <c>sleeping</c> and <c>dead</c>.</summary>
	None,

	/// <summary><c>Flyer_BehaviourAttackThink</c> (<c>00422ca8</c>) — the <c>attacking</c> think.</summary>
	Attack,

	/// <summary><c>Flyer_BehaviourPatrolThink</c> (<c>00422b34</c>) — <c>patrolling</c>: fly the route, and take any target.</summary>
	Patrol,

	/// <summary><c>Flyer_BehaviourSearchDestroyThink</c> (<c>00422a80</c>) — <c>search and destroy</c>: fly the route, take only the order's target.</summary>
	SearchDestroy,

	/// <summary><c>Flyer_BehaviourScoutThink</c> (<c>00422bdc</c>) — <c>scouting</c>: fly the route and nothing else.</summary>
	Scout
}

/// <summary>
/// One of the <b>seven</b> behaviour state descriptors the <c>Flyer</c> class has of its own — the
/// <c>0x3c</c>-byte records at <c>00499cf8</c> that <c>Flyer_BuildStateTable</c> (<c>00414c65</c>) fills at startup, which is
/// the flyer's counterpart of <c>Behaviour_BuildStateTable</c>'s 22-entry mech table
/// (<see cref="BehaviourState"/>). The two tables share nothing but their shape: a flyer's states
/// have their own names, their own dwell times and their own thinks, and the stride is two bytes
/// shorter because a flyer descriptor carries no <c>OBJECTIVE:</c> string index — an aircraft never
/// appears on the player's [F7] comm page.
///
/// <para>The dispatch is identical, though, and deliberately so: the flyer's vtable slots
/// <c>+0x14</c>/<c>+0x18</c>/<c>+0x1c</c> (<c>Flyer_DispatchMove</c> (<c>004217fc</c>), <c>Flyer_DispatchThink</c> (<c>0042184c</c>),
/// <c>Flyer_DispatchReassess</c> (<c>00421888</c>)) are the same three descriptor dispatchers a machine has, so
/// <c>Mech_AiTick</c> (<c>00411cec</c>) drives an aircraft exactly as it drives a HERC. See
/// docs/simulation/ai-dispatch.md for the model and docs/simulation/ai-flyers.md for this
/// table.</para>
///
/// <para><b>Five of the seven reassess, all through one function</b> — <c>Flyer_AiSelectBehaviour</c>
/// (<c>00422d00</c>), which maps the group's current order verb onto a state. <c>sleeping</c> and
/// <c>dead</c> carry a null reassess triple. There is no combat reassess: an aircraft picks its
/// target inside its think and leaves the state alone. See docs/simulation/ai-flyers.md, "The
/// seven states".</para>
/// </summary>
public sealed class FlyerBehaviourState {
	private FlyerBehaviourState(int index, string name, int dwell, int flags,
			FlyerThinkSlot think = FlyerThinkSlot.None, bool moves = true, bool reassesses = true) {
		Index = index;
		Name = name;
		Dwell = dwell;
		Flags = flags;
		Think = think;
		Moves = moves;
		Reassesses = reassesses;
	}

	/// <summary>The state's index into the table — descriptor <c>00499cf8 + 0x3c*N</c>.</summary>
	public int Index { get; }

	/// <summary>Descriptor <c>+0x00</c> — the game's own name for the state.</summary>
	public string Name { get; }

	/// <summary>
	/// Descriptor <c>+0x04</c> — the dwell time, in <see cref="SimMath.TimerCountDown"/>'s unit
	/// and not in milliseconds: the 50000 <c>attacking</c> states is about 24 seconds.
	/// </summary>
	public int Dwell { get; }

	/// <summary>Descriptor <c>+0x08</c> as the 16-bit mask <c>Flyer_BuildStateTable</c> (<c>00414c65</c>) writes.</summary>
	public int Flags { get; }

	/// <inheritdoc cref="FlyerThinkSlot"/>
	public FlyerThinkSlot Think { get; }

	/// <summary>
	/// Whether the descriptor's <c>+0x24</c> move slot holds <c>Flyer_MovementTick</c> (<c>004218c4</c>), the flyer's move.
	/// Four of the seven do; <c>deciding</c>, <c>sleeping</c> and <c>dead</c> hold a null triple, so
	/// an aircraft in any of them stops dead in the air rather than gliding.
	/// </summary>
	public bool Moves { get; }

	/// <summary>
	/// Whether the descriptor's reassess slot holds <c>Flyer_AiSelectBehaviour</c> (<c>00422d00</c>).
	/// Five of the seven do; <c>sleeping</c> and <c>dead</c> hold a null triple, so
	/// <c>Flyer_DispatchReassess</c> (<c>00421888</c>) returns without calling anything, and an aircraft
	/// in either stays there whatever its group's orders do.
	/// </summary>
	public bool Reassesses { get; }

	/// <summary>
	/// Bit 0 — <see cref="FlyerObject.AiTick"/> does not run the dwell countdown. The flyer table sets
	/// it on exactly the three states whose dwell would otherwise decide something:
	/// <c>attacking</c>, <c>sleeping</c> and <c>dead</c>. The four that <i>do</i> count down reassess
	/// when it runs out — <c>deciding</c> into its group's order, the other three back into themselves
	/// while the order stands — so the clock is what re-reads the order rather than what ends a
	/// state.
	/// </summary>
	public bool SuppressesDwell => (Flags & 0x01) != 0;

	/// <inheritdoc />
	public override string ToString() => Name;

	// The table, in index order. Names, dwell times and flag masks are the immediates Flyer_BuildStateTable (00414c65)
	// writes; the think and move columns are which functions each state's source block at 00499e9c
	// names.
	public static readonly FlyerBehaviourState Deciding =
		new(0, "deciding", 0, 0x00, moves: false);

	public static readonly FlyerBehaviourState Attacking =
		new(1, "attacking", 50000, 0x01, FlyerThinkSlot.Attack);

	public static readonly FlyerBehaviourState Patrolling =
		new(2, "patrolling", 5000, 0x00, FlyerThinkSlot.Patrol);

	public static readonly FlyerBehaviourState SearchAndDestroy =
		new(3, "search and destroy", 5000, 0x00, FlyerThinkSlot.SearchDestroy);

	public static readonly FlyerBehaviourState Sleeping =
		new(4, "sleeping", 500, 0x01, moves: false, reassesses: false);

	public static readonly FlyerBehaviourState Scouting =
		new(5, "scouting", 5000, 0x00, FlyerThinkSlot.Scout);

	public static readonly FlyerBehaviourState Dead =
		new(6, "dead", 500, 0x01, moves: false, reassesses: false);

	/// <summary>All seven, in the order the descriptor table holds them.</summary>
	public static readonly IReadOnlyList<FlyerBehaviourState> All = new[] {
		Deciding, Attacking, Patrolling, SearchAndDestroy, Sleeping, Scouting, Dead
	};
}

/// <summary>
/// The behaviour block a flyer carries at <c>flyer+0x4d</c> — the same <c>0x45</c> bytes a machine
/// has, written by the same <c>Behaviour_SetState</c> (<c>00413e50</c>), holding a flyer descriptor
/// instead of a mech one. See <see cref="BehaviourBlock"/>, which this mirrors.
/// </summary>
public struct FlyerBehaviourBlock {
	/// <summary>Block <c>+0x00</c> — the installed descriptor.</summary>
	public FlyerBehaviourState? State { get; private set; }

	/// <summary>Block <c>+0x05</c> — the dwell countdown, in <see cref="SimMath.TimerCountDown"/>'s unit.</summary>
	public int DwellCountdown;

	/// <inheritdoc cref="BehaviourBlock.TickCount"/>
	public int TickCount;

	/// <inheritdoc cref="BehaviourBlock.JustInstalled"/>
	public bool JustInstalled;

	/// <summary>
	/// <c>Behaviour_SetState</c> (<c>00413e50</c>) — installs a descriptor and arms its countdown at
	/// the descriptor's dwell plus the shared 0-15 count jitter. The jitter global is process-wide and
	/// stepped by every state change in the mission, aircraft and machines alike, which is why it
	/// lives on <see cref="BehaviourBlock"/> rather than being duplicated here. The scratch the
	/// original zeroes alongside is held on the aircraft; <c>FlyerObject.SetBehaviourState</c> zeroes
	/// it and is the only caller.
	/// </summary>
	public void SetState(FlyerBehaviourState state) {
		State = state;
		DwellCountdown = state.Dwell + BehaviourBlock.NextJitter();
		TickCount = 0;
		JustInstalled = true;
	}

	/// <inheritdoc cref="BehaviourBlock.CountTick"/>
	public void CountTick() {
		if (JustInstalled) {
			JustInstalled = false;
			return;
		}

		TickCount++;
	}
}
