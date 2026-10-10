using Herculan.Engine.World;

namespace Herculan.Engine.Sim;

/// <summary>
/// The mission's own state while it runs: the block-5 actions and block-6 timers, the objective layer, the mission
/// counters, the salvage list, the mission box and the status alert the tick last raised. DBSIM keeps each in
/// globals of its own; <see cref="SimWorld.Mission"/> holds them.
/// </summary>
public sealed class MissionRuntime {
	/// <summary>
	/// <c>DAT_004a9eac</c>, count <c>DAT_004a9ea8</c> — the mission's block-5 actions, in file order.
	/// The trigger layer walks them once a frame; see <see cref="MissionTriggers"/>.
	/// </summary>
	public IReadOnlyList<MissionActionState> Actions => _actions;

	/// <summary>Installs the mission's action array. Done once, at load.</summary>
	public void SetActions(IReadOnlyList<MissionActionState> actions) {
		_actions.Clear();
		_actions.AddRange(actions);
	}

	private readonly List<MissionActionState> _actions = new();

	/// <summary>
	/// <c>ActionTimer_Array</c> (<c>004a9ebc</c>), count <c>ActionTimer_Count</c> (<c>004a9eb8</c>) — the mission's block-6 timers. See
	/// <see cref="MissionActionTimerState"/>.
	/// </summary>
	public IReadOnlyList<MissionActionTimerState> ActionTimers => _actionTimers;

	/// <summary>Installs the mission's action-timer array. Done once, at load.</summary>
	public void SetActionTimers(IReadOnlyList<MissionActionTimerState> timers) {
		_actionTimers.Clear();
		_actionTimers.AddRange(timers);
	}

	private readonly List<MissionActionTimerState> _actionTimers = new();

	/// <summary>
	/// The mission's objective layer — block 12's records and the status they add up to. Empty until
	/// a scene installs one, which is what a headless test or a mission with no objectives leaves it.
	/// See <see cref="MissionObjectives"/>.
	/// </summary>
	public MissionObjectives Objectives { get; private set; } = MissionObjectives.Empty;

	/// <summary>Installs the mission's objective layer. Done once, at load.</summary>
	public void SetObjectives(MissionObjectives objectives) => Objectives = objectives;

	/// <summary>
	/// The mission's <see cref="World.MissionBox"/>. Two things read it: the Heads-Down Display's map
	/// frames itself on it, and the objective layer's two boundary statuses are the player leaving it.
	/// An empty box turns both off.
	/// </summary>
	public MissionBox Box { get; set; } =
		new(int.MaxValue, int.MaxValue, int.MinValue, int.MinValue);

	/// <summary>
	/// The status the tick last handed up for the status alert, not yet shown: the objective poll's,
	/// or <see cref="MissionStatus.PlayerDestroyed"/> once the player-death camera has run its course.
	/// The original raises the modal panel inside the tick; the host raises it from this latch.
	/// </summary>
	public MissionStatus PendingAlert { get; set; } = MissionStatus.None;

	/// <summary>
	/// <c>DAT_004a9ef4</c> — the mission counters, which are the campaign's flag array for the length
	/// of a mission: they start from <c>mission.var</c> (<see cref="LoadCounters"/>) and
	/// <c>Mission_WriteResults</c> (<c>0042412c</c>) writes them back to it as the mission ends (<see cref="MissionResults"/>). See
	/// docs/retail/simulation/mission-deployment.md#the-mission-counters--dat_004a9ef4.
	///
	/// <para>Written by an action firing (<see cref="MissionActionState.Activate"/>), an objective
	/// (<see cref="MissionObjectiveState"/>), the player downing a squadmate
	/// (<see cref="MechObject.CreditNeutralised"/>), and an object or a whole group going out of the
	/// fight (<see cref="ApplyOutOfActionReport"/>).</para>
	/// </summary>
	public IReadOnlyList<short> Counters => _counters;

	/// <summary>
	/// Starts the counters from <see cref="World.Mission.Counters"/>. Done once, at load; a shorter
	/// list leaves the rest at zero.
	/// </summary>
	public void LoadCounters(IReadOnlyList<short> counters) {
		Array.Clear(_counters);
		for (int i = 0; i < counters.Count && i < CounterSlots; i++) {
			_counters[i] = counters[i];
		}
	}

	/// <summary>Adds to one counter. Refs outside the array are dropped rather than throwing.</summary>
	internal void BumpCounter(int index, short amount) {
		if ((uint)index < CounterSlots) {
			_counters[index] = (short)(_counters[index] + amount);
		}
	}

	/// <summary>Writes one counter outright — the objective layer's operation 4, which sets it to 1.</summary>
	internal void SetCounter(int index, short value) {
		if ((uint)index < CounterSlots) {
			_counters[index] = value;
		}
	}

	/// <summary>Zeroes one counter.</summary>
	internal void ClearCounter(int index) {
		if ((uint)index < CounterSlots) {
			_counters[index] = 0;
		}
	}

	/// <summary>
	/// Runs one object's or group's out-of-action writes — the slot walk
	/// <c>Mech_ReportOutOfAction</c> (<c>00411bc8</c>) and <c>Group_ReportIfAllOutOfAction</c>
	/// (<c>00423f30</c>) both spell out inline. A slot with a negative ref is skipped, and an
	/// operation outside the set writes nothing.
	/// </summary>
	internal void ApplyOutOfActionReport(World.OutOfActionReport report) {
		for (int slot = 0; slot < World.OutOfActionReport.Slots && slot < report.CounterRefs.Count; slot++) {
			short counter = report.CounterRefs[slot];
			if (counter < 0 || slot >= report.CounterOps.Count) {
				continue;
			}

			short op = report.CounterOps[slot];
			if (op == World.OutOfActionReport.OpClear) {
				ClearCounter(counter);
			} else if (op == World.OutOfActionReport.OpIncrement) {
				BumpCounter(counter, 1);
			} else if (op >= World.OutOfActionReport.OpSetFirst && op <= World.OutOfActionReport.OpSetLast) {
				SetCounter(counter, (short)(op - World.OutOfActionReport.SetBias));
			}
		}
	}

	/// <summary>
	/// How many counters the array holds — 1000: <c>mission.var</c> is 2,000 bytes each way.
	/// </summary>
	public const int CounterSlots = 1000;

	private readonly short[] _counters = new short[CounterSlots];

	/// <summary>
	/// <c>DAT_004a9ef0</c>/<c>DAT_004a9eee</c> — the weapons the mission has recovered, each a catalog id and a
	/// percentage condition, which <see cref="MissionResults"/> hands the shell as <c>results.dat</c>'s salvage
	/// pairs. <c>DBSim_LoadScriptDat</c> empties it at load; <see cref="QueueSalvage"/> is its only writer.
	/// </summary>
	public IReadOnlyList<(short WeaponId, short Condition)> Salvage => _salvage;

	/// <summary>
	/// <c>Salvage_QueueWeapon</c> (<c>00426ac8</c>) — one weapon onto <see cref="Salvage"/>. Two callers: a
	/// Cybrid mount the destruction roll knocks off (<c>MechObject.RollWeaponMountDestruction</c>), and
	/// each surviving mount of an enemy wreck at the mission's end (<see cref="MechObject.SalvageValue"/>).
	///
	/// <para>The original's list is a fixed block the append never checks, and a long enough list
	/// writes past it (docs/retail/simulation/component-damage.md#what-a-wreck-is-worth--mech_salvagevalue-00418e60).
	/// This list grows instead.</para>
	/// </summary>
	internal void QueueSalvage(short weaponId, short condition) => _salvage.Add((weaponId, condition));

	private readonly List<(short WeaponId, short Condition)> _salvage = new();

	/// <summary>
	/// <c>Sim_MainTick</c>'s mission poll: <see cref="MissionObjectives.Poll"/>, and on
	/// <see cref="MissionStatus.PlayerImmobilised"/> the outnumbered damage on the player's group first,
	/// which turns the answer into <see cref="MissionStatus.ImmobilisedThenDestroyed"/> when it kills the
	/// player. See docs/retail/simulation/mission-objectives.md#the-poll--mission_pollstatus-004131ac.
	/// </summary>
	internal void Poll(SimWorld world, MechObject pilot) {
		var alert = Objectives.Poll(world, pilot);
		if (alert == MissionStatus.None) {
			return;
		}

		if (alert == MissionStatus.PlayerImmobilised && pilot.Group is { } group) {
			MechObject.ApplyGroupOutnumberedDamage(world, group);
			if (pilot.Destroyed) {
				alert = MissionStatus.ImmobilisedThenDestroyed;
			}
		}

		PendingAlert = alert;
	}
}
