using Herculan.Engine.Numerics;
using Herculan.Es2.World;

namespace Herculan.Es2.Sim;

/// <summary>
/// One <see cref="MissionActionTimer"/> as the simulation holds it: the record plus its running
/// countdown. <c>ActionTimer_Tick</c> (<c>004230a4</c>) is the whole class.
/// </summary>
public sealed class MissionActionTimerState {
	public MissionActionTimerState(MissionActionTimer record, MissionActionState? primary,
			IReadOnlyList<MissionActionState?> sequence) {
		Record = record;
		Primary = primary;
		Sequence = sequence;
		Remaining = record.Delay;
	}

	/// <summary>What the file said.</summary>
	public MissionActionTimer Record { get; }

	/// <summary>The action that arms the timer, or null for one that runs from mission start.</summary>
	public MissionActionState? Primary { get; }

	/// <summary>The actions it fires, unset slots left null.</summary>
	public IReadOnlyList<MissionActionState?> Sequence { get; }

	/// <summary>What is left on the countdown, in <see cref="SimMath.TickDelta"/>'s timer unit.</summary>
	public int Remaining { get; private set; }

	/// <summary>
	/// <c>ActionTimer_Tick</c> (<c>004230a4</c>) — one frame of the timer.
	///
	/// <para>The countdown only moves while the timer is armed, and it is armed by its primary action
	/// having activated, or by there being no primary at all. When it reaches zero every action the
	/// timer names is activated and it is reloaded with <see cref="MissionActionTimer.SpentReload"/>,
	/// which puts the next expiry hours away — and by then every one of those actions has activated,
	/// so activating them again does nothing.</para>
	/// </summary>
	public void Tick(SimWorld world) {
		if (Primary is { Activated: false }) {
			return;
		}

		Remaining -= SimMath.TickDelta;
		if (Remaining > 0) {
			return;
		}

		Remaining = 0;

		for (int i = 0; i < Sequence.Count; i++) {
			Sequence[i]?.Activate(world);
		}

		Remaining = MissionActionTimer.SpentReload;
	}
}
