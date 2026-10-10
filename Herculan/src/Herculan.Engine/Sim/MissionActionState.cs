using Herculan.Engine.Numerics;
using Herculan.Engine.World;

namespace Herculan.Engine.Sim;

/// <summary>
/// One mission action as the simulation holds it: the loaded <see cref="MissionAction"/> record,
/// its resolved trigger subject, and the one piece of runtime state the original keeps in the same
/// record — the <b>activation flag</b> at <c>+0x0a</c>, zeroed at load and set once, for good, by
/// <c>Action_Activate</c> (<c>00423430</c>).
///
/// <para>Everything that waits on a mission action watches this object: a group that has not entered
/// the mission (<see cref="MissionGroup.DeploymentCheck"/>) and a group ready to move on from its
/// current order (<see cref="MissionGroup.AiTick"/>). Both only ever ask <see cref="Activated"/>.</para>
/// </summary>
public sealed class MissionActionState {
	public MissionActionState(MissionAction record) => Record = record;

	/// <summary>What the file said.</summary>
	public MissionAction Record { get; }

	/// <summary>
	/// <c>action+0x0a</c> — whether the action has activated. One-shot: <c>Action_TestTrigger</c>
	/// (<c>004234b8</c>) answers "in the area" for an action that already has without testing
	/// anything, and <c>Action_Activate</c> does nothing a second time.
	/// </summary>
	public bool Activated { get; private set; }

	/// <summary>
	/// <c>action+0x36</c> resolved — the object trigger types 7-9 test the position of. Null when the
	/// action names none, or names one the mission never placed.
	/// </summary>
	public SimObject? TargetObject { get; internal set; }

	/// <summary>
	/// <c>action+0x36</c> resolved the other way — the group trigger type 10 walks. Null on the same
	/// terms.
	/// </summary>
	public MissionGroup? TargetGroup { get; internal set; }

	/// <summary>
	/// <c>Action_TestTrigger</c> (<c>004234b8</c>) — offers one position to each of the action's
	/// areas in turn and activates on the first that catches it.
	/// </summary>
	/// <returns>
	/// Whether the action is now activated, which includes an action that already was — the original
	/// returns 1 for that case before it looks at any area, and the caller reads the answer as "stop
	/// offering subjects".
	/// </returns>
	public bool TestTrigger(SimWorld world, Vec3i position) {
		if (Activated) {
			return true;
		}

		for (int i = 0; i < Record.Areas.Count; i++) {
			if (Record.Areas[i].Contains(position)) {
				Activate(world);
				return true;
			}
		}

		return false;
	}

	/// <summary>
	/// <c>Action_Activate</c> (<c>00423430</c>) — latches the flag and applies the action's counter
	/// operations.
	///
	/// <para>The counter walk is the original's: ten slots, each with a ref and an operation, and a
	/// slot with a negative ref is skipped whole. <b>The message is inside that loop</b>, so an action
	/// naming five counters posts its line five times and one naming none posts it not at all. The
	/// post goes to the pilot and squad port with no speaker; see <see cref="MissionAction.MessageId"/>.</para>
	/// </summary>
	public void Activate(SimWorld world) {
		if (Activated) {
			return;
		}

		Activated = true;

		for (int slot = 0; slot < MissionAction.CounterSlots; slot++) {
			short counter = slot < Record.CounterRefs.Count ? Record.CounterRefs[slot] : (short)-1;
			if (counter < 0) {
				continue;
			}

			short op = slot < Record.CounterOps.Count ? Record.CounterOps[slot] : (short)0;
			if (op == MissionAction.CounterIncrement) {
				world.Mission.BumpCounter(counter, 1);
			} else if (op == MissionAction.CounterClear) {
				world.Mission.ClearCounter(counter);
			}

			if (Record.MessageId >= 0) {
				world.Sounds?.CommandSay(Record.MessageId);
			}
		}
	}
}
