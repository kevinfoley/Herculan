using Herculan.Engine.Numerics;
using Herculan.Es1.Messages;

namespace Herculan.Es1.Sim;

/// <summary>
/// The globals of the player's think, each an <c>int32</c> past the image's file bytes, so 0 when the simulator starts.
/// The think is their only reader and writer (<c>es2_xref.py</c> finds no other reference), so retail resets none of them
/// at a mission's start; the simulator runs one mission, and this engine builds them with each simulation.
/// </summary>
public sealed class Es1PlayerThinkGlobals {
	/// <summary><c>MissionTarget_Announced</c> (<c>000af70c</c>), computer message 5's latch.</summary>
	public bool MissionTargetAnnounced { get; set; }

	/// <summary><c>DataLink_Step</c> (<c>000af700</c>), the data link's lines posted, 0 to <see cref="Es1PlayerThink.DataLinkSteps"/>.</summary>
	public int DataLinkStep { get; set; }

	/// <summary><c>DataLink_Timer</c> (<c>000af704</c>)'s count, the <c>int32</c> at <c>000af705</c> that <c>Timer_CountDown</c> steps.</summary>
	public int DataLinkTimer;
}

/// <summary>
/// <c>Mech_BehaviourPlayerThink</c> (<c>ES1DBSIM 00015f80</c>), the think of the <c>player</c> state
/// (docs/es1/simulation/mech-ai.md#the-players-think--mech_behaviourplayerthink-00015f80, which links ES2's derivation).
/// </summary>
public static class Es1PlayerThink {
	/// <summary>The ground range of a waypoint's arrival, and the 3D range of the data link.</summary>
	public const int ArrivalRange = 10000;

	/// <summary>Selector 5's ground range from <see cref="Es1Mech.AiGoalPosition"/>.</summary>
	public const int GoalRange = 40000;

	/// <summary>The selectors, script.dat header word 3, that run an arm.</summary>
	public const short SelectorTarget = 0, SelectorGoal = 5, SelectorDataLink = 3;

	/// <summary>The computer's messages: a waypoint reached, the mission's target, the data link's first line and its break-off.</summary>
	public const short WaypointMessage = 0, TargetMessage = 5, DataLinkMessage = 0x1d, DataLinkBrokenMessage = 0x21;

	/// <summary>The data link's lines.</summary>
	public const int DataLinkSteps = 4;

	/// <summary>The half-arc, either side of the heading plus the twist, inside which the subject holds the link.</summary>
	public const int DataLinkArc = 0x2000;

	/// <summary><c>DataLink_StepDelays</c> (<c>00091ce8</c>), the <c>int32</c> wait loaded before each line is posted.</summary>
	public static readonly int[] DataLinkStepDelays = [5000, 5000, unchecked((int)0xffff9c40), 0];

	/// <summary>
	/// The think, for the first member of the machine's group alone; it answers 0. The waypoint arm: with a waypoint after
	/// the cursor (<see cref="Es1MissionGroup.WaypointAt"/>) within <see cref="ArrivalRange"/> by
	/// <c>Math_DistanceBetweenPoints2D</c> and not closing the route, computer message <see cref="WaypointMessage"/> and the
	/// cursor stepped. Then by <see cref="Es1SimGlobals.HeaderWord3"/>, read before the waypoint arm: <see cref="TargetArm"/>,
	/// <see cref="GoalArm"/> or <see cref="DataLinkArm"/>; any other selector runs none.
	/// </summary>
	public static short Think(Es1Mech mech, Es1SimGlobals globals) {
		short selector = globals.HeaderWord3;
		// Retail reads the group's first member through the group with no test; a machine with no group, or with none first,
		// runs nothing here: an engine guard.
		if (mech.Group is not { Members: [var first, ..] } group || !ReferenceEquals(first, mech)) {
			return 0;
		}

		if (group.WaypointAt(group.RouteCursor + 1) is { } next
				&& DistanceBetweenPoints2D(next, mech.Position) < ArrivalRange
				&& !group.NextWaypointClosesRoute) {
			Es1MessageTicker.ComputerPostMessage(globals, WaypointMessage);
			group.AdvanceCursor();
		}

		switch (selector) {
			case SelectorTarget:
				TargetArm(mech, group, globals);
				break;
			case SelectorGoal:
				GoalArm(mech);
				break;
			case SelectorDataLink:
				DataLinkArm(mech, group, globals);
				break;
		}

		return 0;
	}

	/// <summary>
	/// Selector 0: once, while <see cref="Es1PlayerThinkGlobals.MissionTargetAnnounced"/> is clear, when the machine's
	/// target <c>+0x15e</c> is one <see cref="Es1MissionGroup.IsOrderTarget"/> passes, the latch set, computer message
	/// <see cref="TargetMessage"/> and <see cref="Es1Mech.ObjectiveReached"/> set.
	/// </summary>
	private static void TargetArm(Es1Mech mech, Es1MissionGroup group, Es1SimGlobals globals) {
		var state = globals.PlayerThink;
		if (state.MissionTargetAnnounced || mech.Target is not { } target || !group.IsOrderTarget(target)) {
			return;
		}

		state.MissionTargetAnnounced = true;
		Es1MessageTicker.ComputerPostMessage(globals, TargetMessage);
		mech.ObjectiveReached = true;
	}

	/// <summary>
	/// Selector 5: <see cref="Es1Mech.ObjectiveReached"/> set within <see cref="GoalRange"/> of
	/// <see cref="Es1Mech.AiGoalPosition"/> by <c>Math_DistanceBetweenPoints2D</c>. With no goal, where retail reads
	/// through a null pointer, nothing: an engine guard.
	/// </summary>
	private static void GoalArm(Es1Mech mech) {
		if (mech.AiGoalPosition() is { } goal && DistanceBetweenPoints2D(goal, mech.Position) < GoalRange) {
			mech.ObjectiveReached = true;
		}
	}

	/// <summary>
	/// Selector 3, until <see cref="DataLinkSteps"/> lines are posted. The subject is <see cref="Es1MissionGroup.OrderTargetObject"/>.
	/// It holds the link while it is not destroyed (<c>+0x93</c>), its group holds no action (<c>+0x16</c>), it lies within
	/// <see cref="ArrivalRange"/> by <c>Math_DistanceBetweenPoints</c>, and the bearing to it (<c>Math_HeadingToward</c>)
	/// less the heading, as an <c>int16</c>, plus the twist <c>+0x22e</c>, lies in [−<see cref="DataLinkArc"/>,
	/// <see cref="DataLinkArc"/>), the sum unwrapped. Holding it, once <c>Timer_CountDown</c> has run the timer out: the
	/// timer loaded from <see cref="DataLinkStepDelays"/> at the step, computer message <see cref="DataLinkMessage"/> + the
	/// step, the step up by 1, and at <see cref="DataLinkSteps"/> <see cref="Es1Mech.DataLinkComplete"/> set. Otherwise
	/// <see cref="DataLinkBrokenMessage"/> and the step back to 0 when a line has been posted, and the timer 0.
	/// </summary>
	private static void DataLinkArm(Es1Mech mech, Es1MissionGroup group, Es1SimGlobals globals) {
		var state = globals.PlayerThink;
		if (state.DataLinkStep == DataLinkSteps) {
			return;
		}

		// Retail reads the subject and its group with no test; with none, and for a subject with no group, the link is not
		// held, an engine guard.
		if (group.OrderTargetObject() is { Destroyed: false, Group.Action: null } subject
				&& DistanceBetweenPoints(subject.Position, mech.Position) < ArrivalRange
				&& InDataLinkArc(mech, subject)) {
			if (globals.Clock.TimerCountDown(ref state.DataLinkTimer) != 0) {
				return;
			}

			state.DataLinkTimer = DataLinkStepDelays[state.DataLinkStep];
			Es1MessageTicker.ComputerPostMessage(globals, (short)(DataLinkMessage + state.DataLinkStep));
			state.DataLinkStep++;
			if (state.DataLinkStep == DataLinkSteps) {
				mech.DataLinkComplete = true;
			}

			return;
		}

		if (state.DataLinkStep != 0) {
			Es1MessageTicker.ComputerPostMessage(globals, DataLinkBrokenMessage);
			state.DataLinkStep = 0;
		}

		state.DataLinkTimer = 0;
	}

	/// <summary>The data link's arc test: (<c>int16</c>)(bearing − heading) + twist + <see cref="DataLinkArc"/>, unsigned, below twice the arc.</summary>
	private static bool InDataLinkArc(Es1Mech mech, IEs1SimObject subject) {
		short bearing = Es1MissionGroup.HeadingToward(mech.Position, subject.Position);
		int error = (short)(bearing - mech.Heading) + mech.Twist;
		return (uint)(error + DataLinkArc) < 2 * DataLinkArc;
	}

	/// <summary><c>Math_DistanceBetweenPoints2D</c> (<c>00063c64</c>)(<paramref name="a"/>, <paramref name="b"/>): <c>Math_FastMagnitude2D</c>(b − a).</summary>
	internal static int DistanceBetweenPoints2D(Vec3i a, Vec3i b) => SimMath.FastMagnitude2D(b.X - a.X, b.Y - a.Y);

	/// <summary><c>Math_DistanceBetweenPoints</c> (<c>00063c20</c>): <c>Math_FastMagnitude3D</c> of the differences.</summary>
	private static int DistanceBetweenPoints(Vec3i a, Vec3i b) => SimMath.FastMagnitude3D(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
}
