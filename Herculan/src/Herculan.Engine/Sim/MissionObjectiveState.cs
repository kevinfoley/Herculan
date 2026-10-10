using Herculan.Engine.World;

namespace Herculan.Engine.Sim;

/// <summary>
/// One mission objective as the simulation holds it: the loaded <see cref="MissionObjective"/>
/// record, its resolved subject, and the one piece of runtime state the original keeps in the same
/// record — the <b>applied flag</b> at <c>+0x22</c>, zeroed at load and set the first time the
/// objective's condition comes back true.
///
/// <para>The flag is not "the objective is met". It only gates the counter writes, so a condition
/// that comes true, goes false and comes true again is re-read every poll but pays its counters
/// once. <see cref="Satisfied"/> is the live answer.</para>
/// </summary>
public sealed class MissionObjectiveState {
	public MissionObjectiveState(MissionObjective record) => Record = record;

	/// <summary>What the file said.</summary>
	public MissionObjective Record { get; }

	/// <summary>
	/// <c>record+0x06</c> resolved — the object this objective is about, for the three object
	/// subject kinds. Null when the objective names a group, or names a roster slot nothing placed.
	/// </summary>
	public SimObject? SubjectObject { get; internal set; }

	/// <summary>
	/// <c>record+0x06</c> resolved the other way — the group, for subject kind
	/// <see cref="MissionObjectiveSubject.Group"/>.
	/// </summary>
	public MissionGroup? SubjectGroup { get; internal set; }

	/// <summary>
	/// <c>record+0x22</c> — whether the counter operations have been applied. One-shot.
	/// </summary>
	public bool CountersApplied { get; private set; }

	/// <summary>
	/// What the last poll made of this objective's condition. Meaningful only after the first poll;
	/// it is what the mission status and any display of the objective list both read.
	/// </summary>
	public bool Satisfied { get; internal set; }

	/// <summary>
	/// Whether this record is one the mission has to satisfy, as against a failure condition that
	/// loses it — <see cref="MissionObjective.Required"/>'s <c>== 1</c> test, spelled once.
	/// </summary>
	public bool IsMandatory => Record.Required == MissionObjective.Mandatory;

	/// <summary>
	/// The counter walk the evaluator runs the first time the condition holds — ten (ref, operation)
	/// pairs, a slot with a negative ref or a negative operation skipped whole.
	///
	/// <para><b>The operation codes are the objective layer's, not the action layer's</b>: 4 sets,
	/// 5 clears, 6 increments and 7 decrements, where <c>Action_Activate</c> knows only 5 and 6 and
	/// reads them as clear and increment. The two overlap on 5 and disagree on 6 with nothing to
	/// warn a reader. See <see cref="MissionActionState.Activate"/>.</para>
	/// </summary>
	internal void ApplyCounters(SimWorld world) {
		if (CountersApplied) {
			return;
		}

		CountersApplied = true;

		for (int slot = 0; slot < MissionObjective.CounterSlots; slot++) {
			short counter = slot < Record.CounterRefs.Count ? Record.CounterRefs[slot] : (short)-1;
			short op = slot < Record.CounterOps.Count ? Record.CounterOps[slot] : (short)-1;

			if (counter < 0 || op < 0) {
				continue;
			}

			switch (op) {
				case MissionObjective.CounterSet:
					world.Mission.SetCounter(counter, 1);
					break;
				case MissionObjective.CounterClear:
					world.Mission.ClearCounter(counter);
					break;
				case MissionObjective.CounterIncrement:
					world.Mission.BumpCounter(counter, 1);
					break;
				case MissionObjective.CounterDecrement:
					world.Mission.BumpCounter(counter, -1);
					break;
			}
		}
	}
}
