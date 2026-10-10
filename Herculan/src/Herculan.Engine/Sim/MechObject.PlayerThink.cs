using Herculan.Engine.Content;
using Herculan.Engine.Numerics;
using Herculan.Engine.World;

namespace Herculan.Engine.Sim;

/// <summary>
/// The think slot of <c>player</c> and <c>player fly</c> — <c>Mech_BehaviourPlayerThink</c>
/// (<c>0041c194</c>). The machine the player is flying holds a behaviour state like any other, and
/// its think decides nothing about movement: it is where the simulation watches the player's own
/// progress through the mission. The derivation is docs/retail/simulation/player-waypoints.md and
/// docs/retail/simulation/mission-objectives.md.
/// </summary>
public partial class MechObject {
	/// <summary>
	/// <c>Mech_BehaviourPlayerThink</c> (<c>0041c194</c>). Everything it does is gated on the machine
	/// being its group's <b>leader</b>, which for the player's own squad it always is, and it always
	/// returns zero — the player's state never ends itself.
	///
	/// <para><b>The waypoint arm.</b> Identical in shape to <see cref="FollowRoute"/>'s: the route
	/// cursor names the last waypoint reached, so the one being walked at is the one after it, and
	/// reaching it inside <see cref="ArrivalRange"/> on the ground plane steps the cursor. The
	/// player's arrival differs from an AI machine's in two ways — it announces itself on the
	/// computer's message port, and it refuses the step when the waypoint ahead is a closed route's
	/// terminating duplicate (<see cref="MissionGroup.NextWaypointClosesRoute"/>), so a patrol circuit
	/// stops announcing rather than repeating its first waypoint forever.</para>
	///
	/// <para><b>Then one of three objective arms</b>, chosen by the mission's own
	/// <see cref="ScriptDatHeader.ObjectiveType"/>. They are the only writers of
	/// <see cref="SimObject.MissionGoalReached"/> and <see cref="SimObject.DataLinkComplete"/>
	/// anywhere in the image, and those two flags are what the objective conditions read back. None
	/// of them touches the route.</para>
	/// </summary>
	private bool PlayerThink(SimWorld world) {
		if (Group is not { } group || !ReferenceEquals(group.Leader, this)) {
			return false;
		}

		if (group.WaypointAt(group.RouteCursor + 1) is { } next
				&& GroundDistanceTo(next) < ArrivalRange
				&& !group.NextWaypointClosesRoute) {
			world.Sounds?.Say(SystemMessages.WaypointReached);
			group.AdvanceRouteCursor();
		}

		switch (world.Mission.Objectives.ObjectiveType) {
			case ObjectiveTypeTargetDetected:
				TargetDetectedArm(world, group);
				break;
			case ObjectiveTypeGoalPosition:
				GoalPositionArm();
				break;
			case ObjectiveTypeDataLink:
			case ObjectiveTypeDataLinkAlt:
				DataLinkArm(world, group);
				break;
		}

		return false;
	}

	/// <summary>
	/// Objective type 0 — <b>the mission target has been picked up</b>. One-shot for the whole
	/// mission: the first time the player selects a target that is what their group's current order
	/// names, the computer says <c>MISSION TARGET DETECTED</c> and the goal flag goes up.
	///
	/// <para>It reads the player's own selected target, so it fires on the pilot noticing the thing,
	/// not on the sensors finding it.</para>
	/// </summary>
	private void TargetDetectedArm(SimWorld world, MissionGroup group) {
		if (_missionTargetAnnounced || Target is not { } selected || !group.IsOrderTarget(selected)) {
			return;
		}

		_missionTargetAnnounced = true;
		world.Sounds?.Say(SystemMessages.MissionTargetDetected);
		MissionGoalReached = true;
	}

	/// <summary>
	/// Objective type 5 — <b>the goal has been reached</b>. Closing to
	/// <see cref="GoalArrivalRange"/> of <see cref="GoalPosition"/> on the ground plane raises the
	/// goal flag, and that is all it does: it says nothing and it does not latch, because the flag it
	/// writes is already one-way.
	///
	/// <para>The range is four times the waypoint arm's, so the goal is a much larger circle than a
	/// waypoint — 240 m against 60.</para>
	/// </summary>
	private void GoalPositionArm() {
		if (GroundDistanceTo(GoalPosition()) < GoalArrivalRange) {
			MissionGoalReached = true;
		}
	}

	/// <summary>
	/// Objective types 3 and 7 — <b>the data link</b>. The player parks in front of the thing their
	/// order names and reads it, and the computer narrates four lines while they do:
	/// <c>ENGAGING DATA LINK</c>, <c>INITIATING COMMAND OVERRIDE PROTOCOL</c>,
	/// <c>TRANSFERRING DATA</c>, <c>DATA TRANSFER COMPLETE</c>. Breaking off part-way says
	/// <c>DATA TRANSFER ABORTED</c> and puts the sequence back to the start.
	///
	/// <para>Holding the link needs four things at once: the subject alive, its group in the mission,
	/// the player within <see cref="DataLinkRange"/> in three dimensions, and the player's <b>gun</b>
	/// — body heading plus turret twist — inside <see cref="DataLinkArc"/> of it. Nothing tests the
	/// player's speed, so the link can be held while walking past.</para>
	///
	/// <para>Only the completed link is durable: <see cref="SimObject.DataLinkComplete"/> goes up at
	/// the end of the fourth step and nothing lowers it. It goes up when the last line is
	/// <i>queued</i>, not when it is read out, so the objective is satisfied at ten seconds of
	/// holding while the computer is still narrating — see <see cref="DataLinkStepDelays"/>.</para>
	/// </summary>
	private void DataLinkArm(SimWorld world, MissionGroup group) {
		if (_dataLinkStep >= DataLinkSteps) {
			return;
		}

		if (group.OrderTarget is { Destroyed: false, AwaitingDeployment: false } subject
				&& Position.ApproxDistanceTo(subject.Position) < DataLinkRange
				&& InDataLinkArc(subject)) {
			if (SimMath.TimerCountDown(ref _dataLinkCountdown) != 0) {
				return;
			}

			_dataLinkCountdown = DataLinkStepDelays[_dataLinkStep];
			world.Sounds?.Say(SystemMessages.EngagingDataLink + _dataLinkStep);
			_dataLinkStep++;

			if (_dataLinkStep == DataLinkSteps) {
				DataLinkComplete = true;
			}

			return;
		}

		if (_dataLinkStep != 0) {
			world.Sounds?.Say(SystemMessages.DataTransferAborted);
			_dataLinkStep = 0;
		}

		_dataLinkCountdown = 0;
	}

	/// <summary>
	/// Whether the machine's aim — heading plus turret twist, the same sum every arc test in the
	/// simulation takes — is pointed within <see cref="DataLinkArc"/> of the subject.
	/// </summary>
	private bool InDataLinkArc(SimObject subject) {
		short bearing = Detection.HeadingToward(subject.Position, Position);
		return (ushort)(bearing - Heading + AimTwist + DataLinkArc) < DataLinkArc * 2;
	}

	/// <summary><see cref="ScriptDatHeader.ObjectiveType"/> 0 — watch for the order target.</summary>
	public const int ObjectiveTypeTargetDetected = 0;

	/// <summary>Type 5 — watch for the goal position.</summary>
	public const int ObjectiveTypeGoalPosition = 5;

	/// <summary>Type 3 — the data link, and the one that also shields the subject from the AI.</summary>
	public const int ObjectiveTypeDataLink = 3;

	/// <summary>Type 7 — the data link without that shield.</summary>
	public const int ObjectiveTypeDataLinkAlt = 7;

	/// <summary>
	/// Ground range inside which the goal-position arm counts the player as arrived — the original's
	/// 40000, four times <see cref="ArrivalRange"/>.
	/// </summary>
	public const int GoalArrivalRange = 40000;

	/// <summary>Range inside which the data link holds, measured in three dimensions.</summary>
	public const int DataLinkRange = 10000;

	/// <summary>Half-arc the machine's aim has to keep the subject inside to hold the link. 45°.</summary>
	public const short DataLinkArc = 0x2000;

	/// <summary>Messages the data-link sequence speaks, and so steps it has.</summary>
	public const int DataLinkSteps = 4;

	/// <summary>
	/// How long the link has to be held before each of the four lines — the table at
	/// <c>0049a318</c>, read straight, in <see cref="SimMath.TimerCountDown"/>'s unit rather than
	/// milliseconds. <b>The delay is written before the line it precedes is posted</b>,
	/// so entry <i>n</i> is the wait between line <i>n</i> and line <i>n+1</i>.
	///
	/// <para><b>The link takes about five seconds of holding station</b>: two waits of 5000 counts,
	/// about 2.4 seconds each, then nothing. The third
	/// entry is stored negative (<c>0xffff9c40</c>) and <c>Timer_CountDown</c> clamps at zero, so the
	/// fourth step imposes no wait of its own and <c>DATA TRANSFER COMPLETE</c> is queued the tick
	/// after <c>TRANSFERRING DATA</c>.</para>
	///
	/// <para><b>That is not what the player sees.</b> The two lines are queued a tick apart but shown
	/// ten seconds apart, because <c>TRANSFERRING DATA</c> is the one entry in <c>SYSTEM.STR</c> whose
	/// display timings are 10 s and 20 s rather than 3 s and 6 s, and the port will not let a message
	/// yield before its minimum — see <see cref="Cockpit.MessagePort"/>. So the transfer reads on
	/// screen as a long operation while the simulation has already finished it.</para>
	///
	/// <para>The stored <c>0x9c40</c> is 40000, which is what the entry would hold if the sign half
	/// were zero; whether that is the author's intent or a coincidence is not established, and the
	/// value is kept as the file has it either way.</para>
	/// </summary>
	public static readonly int[] DataLinkStepDelays = [5000, 5000, unchecked((int)0xffff9c40), 0];

	/// <summary><c>DAT_004a9d7c</c> — the mission-target announcement's one-shot latch.</summary>
	private bool _missionTargetAnnounced;

	/// <summary><c>DAT_004a9d70</c> — how many of the four data-link lines have been spoken.</summary>
	private int _dataLinkStep;

	/// <summary><c>DAT_004a9d74</c> — the countdown between them.</summary>
	private int _dataLinkCountdown;
}
