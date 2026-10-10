using Herculan.Engine.Content;
using Herculan.Engine.Numerics;
using Herculan.Engine.World;

namespace Herculan.Engine.Sim;

/// <summary>
/// The mission's objective layer — the block-12 records, the poll that reads them, and the one
/// number the whole thing produces: <see cref="Status"/>.
///
/// <para>Three of the original's functions live here.
/// <c>Mission_EvaluateObjectives</c> (<c>00413280</c>) walks the record array and reduces it to a
/// status; <c>Mission_Status</c> (<c>004135e8</c>) puts the player's own condition and the mission
/// box in front of that and <b>posts the computer message</b> when the answer changes;
/// <c>Mission_PollStatus</c> (<c>004131ac</c>) is what <c>Sim_MainTick</c> calls, and it is the
/// throttle — the status is only worked out every <see cref="PollInterval"/>, and an alert-worthy
/// one has to hold for <see cref="AlertDelay"/> before it is handed up.</para>
///
/// <para>See docs/retail/simulation/mission-objectives.md.</para>
/// </summary>
public sealed class MissionObjectives {
	private readonly MissionObjectiveState[] _objectives;

	public MissionObjectives(IReadOnlyList<MissionObjectiveState> objectives) {
		_objectives = objectives.ToArray();
	}

	/// <summary>
	/// An objective layer with no records — every mission is in progress and nothing ends it. A fresh
	/// one each time: the layer carries running countdowns, so a shared instance would let one world's
	/// poll clock another's.
	/// </summary>
	public static MissionObjectives Empty => new(Array.Empty<MissionObjectiveState>());

	/// <summary>
	/// <c>DAT_004a9ec4</c>, count <c>DAT_004a9ec0</c> — the objective records, in file order.
	/// </summary>
	public IReadOnlyList<MissionObjectiveState> Objectives => _objectives;

	/// <inheritdoc cref="HercWorks.Core.Data.File.Msn.Script.ScriptDat.ObjectiveTextRefs"/>
	public IReadOnlyList<int> ObjectiveTextRefs { get; init; } = Array.Empty<int>();

	/// <summary>
	/// The mission's own selector — <c>script.dat</c>'s header at <c>+0x06</c> (<c>DAT_004a9ed8</c>),
	/// which picks which arm of the player's think watches for progress. See
	/// <see cref="MechObject.PlayerThink"/>.
	/// </summary>
	public int ObjectiveType { get; init; }

	/// <summary>
	/// <c>DAT_004a9ed0</c> — the last status handed up to the caller, which is what stops the same
	/// alert being raised twice. Starts at <see cref="MissionStatus.None"/>, which the spawn pass
	/// sets as <c>0xffff</c>.
	/// </summary>
	public MissionStatus Announced { get; private set; } = MissionStatus.None;

	/// <summary>The status the last completed evaluation produced.</summary>
	public MissionStatus Status { get; private set; } = MissionStatus.None;

	/// <summary>
	/// The objective whose failure text the alert would print — <c>DAT_004d1f1c</c>, which the
	/// evaluator points at the <b>first</b> mandatory record that is not satisfied. Null once they
	/// all are.
	/// </summary>
	public MissionObjectiveState? Outstanding { get; private set; }

	/// <summary>
	/// <c>Mission_PollStatus</c> (<c>004131ac</c>), run once a tick from <c>Sim_MainTick</c> with the
	/// player's machine, and only while that machine is not destroyed.
	///
	/// <para>Two countdowns shape it, and between them they are why a finished mission takes several
	/// seconds to say so. <b>The poll interval</b> (<c>MissionPollTimer</c>, <c>004a9ee6</c>) is
	/// re-armed to <see cref="PollInterval"/> every time the answer is not worth raising, so the
	/// objectives are actually read about once every five seconds — except that a player outside the
	/// mission box is read every tick, which is what makes the boundary warning prompt.
	/// <see cref="DeferPoll"/> is its other writer. <b>The alert delay</b> (<c>MissionAlertTimer</c>,
	/// <c>004a9ee9</c>) is armed the first time an alert-worthy status appears, and the status is not
	/// handed up until it runs out. The original exempts <see cref="MissionStatus.PlayerDestroyed"/>
	/// from it, an arm its one caller never reaches, since it polls only a machine still standing.</para>
	/// </summary>
	/// <returns>
	/// The status the caller should raise its modal alert for, or <see cref="MissionStatus.None"/>
	/// for "carry on".
	/// </returns>
	public MissionStatus Poll(SimWorld world, MechObject player) {
		SimMath.CountdownTimerTick(ref _alertDelay);

		if (SimMath.CountdownTimerTick(ref _pollInterval) != 0
				&& !IsOutsideMissionBox(world, player, 0)) {
			return MissionStatus.None;
		}

		var status = Evaluate(world, player, quiet: false);

		if (!RaisesAlert(status) || status == Announced) {
			_pollInterval = PollInterval;
			return MissionStatus.None;
		}

		if (!_alertArmed) {
			_alertArmed = true;
			_alertDelay = AlertDelay;
		}

		if (_alertDelay != 0 && status != MissionStatus.PlayerDestroyed) {
			_pollInterval = PollInterval;
			return MissionStatus.None;
		}

		Announced = status;
		return status;
	}

	/// <summary>
	/// <c>Ai_ChooseWeapon</c>'s write to <c>MissionPollTimer_Count</c> (<c>004a9ee7</c>): put the next
	/// poll at least <paramref name="minimum"/> away, leaving one already further off alone. See
	/// docs/retail/simulation/ai-weapons.md, "Running dry".
	/// </summary>
	internal void DeferPoll(short minimum) {
		if (_pollInterval < minimum) {
			_pollInterval = minimum;
		}
	}

	/// <summary>
	/// <c>Mission_StatusForAlert</c> (<c>00413180</c>) with its publish flag set — the [Q] path.
	/// Evaluates quietly, then records the answer as <see cref="Announced"/>, which is the whole of
	/// what that flag does, and disarms the poll's pending alert, which <c>Sim_DispatchCommand</c>'s
	/// [Q] case does itself once its panel closes (<c>00460585</c>).
	///
	/// <para>Both of those matter to what the player sees next. Recording the answer means the poll
	/// will not raise the same status again as a change, so a player who presses [Q], reads
	/// <c>MISSION OBJECTIVES COMPLETE</c> and carries on is not shown it a second time ten seconds
	/// later; disarming the delay means a status that was part-way through its ten-second wait starts
	/// that wait again.</para>
	/// </summary>
	public MissionStatus QueryForPlayer(SimWorld world, MechObject player) {
		var status = Evaluate(world, player, quiet: true);
		Announced = status;
		_alertArmed = false;
		return status;
	}

	/// <summary>
	/// <c>Mission_Status</c> (<c>004135e8</c>) — the whole judgement, in the original's order: the
	/// player's own condition first, then the mission box, then the objectives.
	///
	/// <para><b>This is where the computer speaks.</b> Four of the statuses carry a
	/// <c>SYSTEM.STR</c> line, and the post happens on the <i>change</i>: the running baseline is
	/// the last status announced, and after a post a <see cref="MessageLockout"/> countdown holds the
	/// answer still so the same line cannot be queued twice while the caller keeps polling. The
	/// baseline catches up on the first evaluation after that lockout expires, which is what
	/// sequences the spoken line ahead of the alert panel.</para>
	/// </summary>
	/// <param name="quiet">
	/// The original's third argument. Set, it skips the lockout, the mission-box arms and the message
	/// post, and just answers the question — what <see cref="QueryForPlayer"/>, the [Q] key's path,
	/// uses. <b>A quiet evaluation can therefore never answer
	/// <see cref="MissionStatus.LeavingMissionZone"/> or
	/// <see cref="MissionStatus.RulesOfEngagementViolated"/></b>: those two are the arms it skips, so
	/// [Q] outside the mission box reports on the objectives as though the player were inside it.
	/// </param>
	public MissionStatus Evaluate(SimWorld world, MechObject player, bool quiet) {
		if (!quiet && SimMath.CountdownTimerTick(ref _messageLockout) != 0) {
			SimMath.CountdownTimerTick(ref _messageLockout);
			return _heldStatus;
		}

		MissionStatus status;

		if (player.Destroyed) {
			status = MissionStatus.PlayerDestroyed;
		} else if (player.Immobilised) {
			status = MissionStatus.PlayerImmobilised;
		} else if (!quiet && IsOutsideMissionBox(world, player, RulesOfEngagementMargin)) {
			status = MissionStatus.RulesOfEngagementViolated;
		} else if (!quiet && IsOutsideMissionBox(world, player, 0)) {
			status = MissionStatus.LeavingMissionZone;
		} else {
			status = EvaluateObjectives(world, player);
		}

		Status = status;

		if (quiet) {
			return status;
		}

		if (_heldStatus != MissionStatus.None) {
			_announcedStatus = status;
			_heldStatus = MissionStatus.None;
		}

		int message = status == _announcedStatus ? -1 : MessageFor(status);

		if (message < 0) {
			_announcedStatus = status;
			return status;
		}

		world.Sounds?.Say(message);
		_messageLockout = MessageLockout;
		_heldStatus = _announcedStatus;
		return _announcedStatus;
	}

	/// <summary>
	/// <c>Mission_EvaluateObjectives</c> (<c>00413280</c>) — one walk of the record array, and the
	/// only place the objectives are read.
	///
	/// <para>Each record's condition is asked of its own subject and the answer is used twice: a
	/// mandatory record that answers no clears the "all met" flag and, if it is the first to do so,
	/// becomes <see cref="Outstanding"/>; a non-mandatory record that answers yes sets the "lost"
	/// flag. Either way an answer of yes applies that record's counters, once.</para>
	///
	/// <para>The three outcomes are then each split by whether the player is
	/// <see cref="IsClearOfThreats">clear</see>. A failure and a success share their not-clear value,
	/// <see cref="MissionStatus.DecidedButEngaged"/>, so the mission does not conclude while the player
	/// is still in a fight; an undecided mission answers <see cref="MissionStatus.InProgressEngaged"/>
	/// there.</para>
	///
	/// <para>Besides <see cref="Evaluate"/>, <see cref="MissionResults.Write"/> calls it directly as the
	/// mission ends, and <see cref="MissionStatus.Complete"/> is the only answer that wins.</para>
	/// </summary>
	internal MissionStatus EvaluateObjectives(SimWorld world, MechObject player) {
		bool lost = false;
		bool allMet = true;
		bool satisfied = false;

		Outstanding = null;

		for (int i = 0; i < _objectives.Length; i++) {
			var objective = _objectives[i];

			// The original leaves the working register untouched for a subject kind past 3 and for
			// condition 5, which has no case in either of its two switches -- so such a record
			// silently reuses the previous record's answer. Carried rather than corrected: nothing
			// here should quietly disagree with the original about a mission that reaches it. For the
			// first record the original carries whatever its caller left in the register; HERCULAN starts
			// from false, which is a divergence from retail.
			if ((uint)objective.Record.SubjectKind <= (uint)MissionObjectiveSubject.Base
					&& Test(world, player, objective) is bool answer) {
				satisfied = answer;
			}

			objective.Satisfied = satisfied;

			if (objective.IsMandatory) {
				if (!satisfied && allMet) {
					allMet = false;
					Outstanding = objective;
				}
			} else if (satisfied) {
				lost = true;
			}

			if (satisfied) {
				objective.ApplyCounters(world);
			}
		}

		bool clear = IsClearOfThreats(world, player);

		return lost || allMet
			? clear ? lost ? MissionStatus.Failed : MissionStatus.Complete : MissionStatus.DecidedButEngaged
			: clear ? MissionStatus.InProgress : MissionStatus.InProgressEngaged;
	}

	/// <summary>
	/// One record's condition, asked of its own subject, or <see langword="null"/> for a code with no
	/// case (5, or outside 0-10), which leaves the running answer alone. The original writes the cases
	/// out twice, once for a group subject and once for an object one; they are the same ten questions
	/// and only the subject differs, so they are folded here. See
	/// docs/retail/simulation/mission-objectives.md, "What each condition asks".
	/// </summary>
	private static bool? Test(SimWorld world, MechObject player, MissionObjectiveState objective) {
		var record = objective.Record;
		bool isGroup = record.SubjectKind == MissionObjectiveSubject.Group;
		var group = isGroup ? objective.SubjectGroup : objective.SubjectObject?.Group;
		var subject = objective.SubjectObject;

		switch (record.ConditionCode) {
			case MissionObjective.ConditionOrderComplete:
				return group?.OrderCompletedForRoute(record.RouteRef) ?? false;

			case MissionObjective.ConditionLost:
				return isGroup
					? group is { } lostGroup && lostGroup.ConditionTier() >= WriteOffTier(lostGroup)
					: subject is { Neutralised: true };

			case MissionObjective.ConditionClear:
				return isGroup
					? group is { } safeGroup && IsGroupClear(world, safeGroup)
					: subject is { } safeObject && IsClearOfThreats(world, safeObject);

			case MissionObjective.ConditionDataLink:
			case MissionObjective.ConditionDataLinkAlso:
				return player.DataLinkComplete;

			case MissionObjective.ConditionNoDataLink:
			case MissionObjective.ConditionNoDataLinkAlso:
				return !player.DataLinkComplete;

			case MissionObjective.ConditionEngaged:
				return isGroup ? AnyMemberEngaged(group) : subject is { Engaged: true };

			case MissionObjective.ConditionUnengaged:
				return isGroup ? !AnyMemberEngaged(group) : subject is { Engaged: false };

			case MissionObjective.ConditionDisarmed:
				return isGroup ? AllMembersDisarmed(group) : subject is { } armed && IsDisarmed(armed);

			default:
				return null;
		}
	}

	/// <summary>
	/// <c>Group_IsWrittenOff</c> (<c>00413920</c>) — the condition tier at which a group counts as written off, which is
	/// <b>not the same for both sides</b>: a human group is written off at tier 3 and a Cybrid one
	/// only at 4, destroyed outright. So "wipe out this Cybrid group" means every machine, and "this
	/// convoy did not make it" is answered a tier earlier.
	/// </summary>
	private static int WriteOffTier(MissionGroup group) =>
		group.Side == MissionSide.Cybrid ? MissionGroup.ConditionDestroyed : 3;

	/// <summary>
	/// <c>Group_AllMembersClearOfThreats</c> (<c>00413950</c>) — a group is clear when it is not written off <i>and</i> every member
	/// still alive is itself clear. A single member with something on it answers no for the whole
	/// group.
	/// </summary>
	private static bool IsGroupClear(SimWorld world, MissionGroup group) {
		if (group.ConditionTier() >= WriteOffTier(group)) {
			return false;
		}

		var members = group.Members;
		for (int i = 0; i < members.Count; i++) {
			var member = members[i];
			if (!member.Destroyed && !IsClearOfThreats(world, member)) {
				return false;
			}
		}

		return true;
	}

	/// <summary>
	/// <c>Mission_IsClearOfThreats</c> (<c>004137b4</c>) — whether nothing hostile is both aware of
	/// <paramref name="subject"/> and near it. This is the escort objective's whole test, and it is
	/// also what gates every conclusive mission status.
	///
	/// <para>It walks the live object list and skips anything undeployed, on the subject's own side,
	/// or already out of the fight. What is left disqualifies the subject two ways:</para>
	///
	/// <list type="bullet">
	/// <item><description>it knows about the subject and is within <see cref="ThreatRange"/> — or
	/// <see cref="DesignatedThreatRange"/>, the wider of the two, when the subject is the thing it is
	/// currently shooting at; or</description></item>
	/// <item><description><b>for a subject that is not on the player's own group</b>, its group's
	/// current order names the subject and it is either still on its way (its route has somewhere
	/// left to go) or already knows where the subject is. An assigned hunter counts as a threat at
	/// any range, which is what stops an escort being called safe while something is walking towards
	/// it.</description></item>
	/// </list>
	/// </summary>
	public static bool IsClearOfThreats(SimWorld world, SimObject subject) {
		if (subject.Group is not { } subjectGroup) {
			return true;
		}

		var objects = world.Objects;
		bool subjectIsPlayerGroup = ReferenceEquals(world.PlayerMech?.Group, subjectGroup);

		for (int i = 0; i < objects.Count; i++) {
			var other = objects[i];

			if (other.Removed || other.AwaitingDeployment || other.Group is not { } otherGroup
					|| otherGroup.Side == subjectGroup.Side || other.OutOfAction) {
				continue;
			}

			bool knows = Ai.AiTargeting.Knows(other, subject);

			if (!subjectIsPlayerGroup && otherGroup.IsOrderTarget(subject)
					&& (otherGroup.WaypointAt(otherGroup.RouteCursor + 1) != null || knows)) {
				return false;
			}

			if (knows) {
				int range = ReferenceEquals(SimObject.SelectedTargetOf(other), subject)
					? DesignatedThreatRange
					: ThreatRange;

				if (subject.Position.ApproxDistanceTo(other.Position) < range) {
					return false;
				}
			}
		}

		return true;
	}

	/// <summary><c>Group_AnyMemberEngaged</c> (<c>00412d90</c>) — any member of the group has been engaged.</summary>
	private static bool AnyMemberEngaged(MissionGroup? group) {
		if (group == null) {
			return false;
		}

		for (int i = 0; i < group.Members.Count; i++) {
			if (group.Members[i].Engaged) {
				return true;
			}
		}

		return false;
	}

	/// <summary>
	/// <c>Group_AllMembersDisarmed</c> (<c>00412c58</c>) — <b>every</b> member of the group is disarmed. An empty group answers
	/// yes, which is the original's own loop shape: it returns 1 when the walk runs off the end.
	/// </summary>
	private static bool AllMembersDisarmed(MissionGroup? group) {
		if (group == null) {
			return false;
		}

		for (int i = 0; i < group.Members.Count; i++) {
			if (!IsDisarmed(group.Members[i])) {
				return false;
			}
		}

		return true;
	}

	/// <summary><c>obj+0xa5</c> on its own, which each of the three shootable classes answers.</summary>
	private static bool IsDisarmed(SimObject subject) => subject switch {
		MechObject mech => mech.Disarmed,
		FlyerObject flyer => flyer.Disarmed,
		BaseObject structure => structure.Disarmed,
		_ => false
	};

	/// <summary>
	/// <c>Mission_IsOutsideBox</c> (<c>0041373c</c>) — whether a position is outside the mission's bounding box, grown by
	/// <paramref name="margin"/> on every side. See <see cref="MissionRuntime.Box"/>.
	/// </summary>
	private static bool IsOutsideMissionBox(SimWorld world, SimObject subject, int margin) {
		var box = world.Mission.Box;

		if (box.IsEmpty) {
			return false;
		}

		return !(box.MinX - margin < subject.Position.X && subject.Position.X < box.MaxX + margin
			&& box.MinY - margin < subject.Position.Y && subject.Position.Y < box.MaxY + margin);
	}

	/// <summary>Whether a status is one the table at <c>0049935c</c> marks worth interrupting for.</summary>
	private static bool RaisesAlert(MissionStatus status) => status is MissionStatus.PlayerDestroyed
		or MissionStatus.PlayerImmobilised or MissionStatus.Failed or MissionStatus.LeavingMissionZone
		or MissionStatus.RulesOfEngagementViolated or MissionStatus.Complete;

	/// <summary>
	/// The <c>SYSTEM.STR</c> line a status change speaks, or <c>-1</c> for one that says nothing.
	/// Four of the nine carry one; the two that say nothing but are still alert-worthy — the player's
	/// machine destroyed and immobilised — are the two the alert panel itself is about.
	/// </summary>
	private static int MessageFor(MissionStatus status) => status switch {
		MissionStatus.Failed => SystemMessages.MissionFailed,
		MissionStatus.LeavingMissionZone => SystemMessages.ApproachingZoneBoundary,
		MissionStatus.RulesOfEngagementViolated => SystemMessages.RulesOfEngagementViolated,
		MissionStatus.Complete => SystemMessages.MissionSuccessful,
		_ => -1
	};

	/// <summary>
	/// How far beyond the mission box counts as abandoning the mission rather than drifting out of
	/// it — <c>SimObject_IsFarOutsideMissionBox</c>'s (<c>00413780</c>) literal, and the difference between the two boundary statuses.
	/// </summary>
	public const int RulesOfEngagementMargin = 110000;

	/// <summary>
	/// Range at which a hostile that knows about a subject disqualifies it from being clear —
	/// <c>Mission_IsClearOfThreats</c>'s 80000, about 480 m.
	/// </summary>
	public const int ThreatRange = 80000;

	/// <summary>The wider range used when the subject is that hostile's own current target.</summary>
	public const int DesignatedThreatRange = 100000;

	/// <summary>
	/// How long the poll waits between readings once it has one that is not worth raising —
	/// <c>MissionPollTimer</c>'s re-arm, in <see cref="SimMath.CountdownTimerTick"/>'s unit rather than
	/// milliseconds: about 4.9 seconds.
	/// </summary>
	public const short PollInterval = 10000;

	/// <summary>
	/// How long an alert-worthy status has to stand before the poll hands it up —
	/// <c>MissionAlertTimer</c>'s arm, in the same unit — about 4.9 seconds. Armed once per latch of
	/// <c>MissionAlertArmed</c> (<c>004a9eec</c>), which only the [Q] path clears — see
	/// <see cref="QueryForPlayer"/>.
	/// </summary>
	public const short AlertDelay = 10000;

	/// <summary>
	/// How long <see cref="Evaluate"/> holds its answer still after posting a message —
	/// <c>DAT_004a9bec</c>'s arm, in the same unit: about a quarter of a second.
	/// </summary>
	public const short MessageLockout = 500;

	private short _pollInterval;
	private short _alertDelay;
	private bool _alertArmed;
	private short _messageLockout;
	private MissionStatus _announcedStatus;
	private MissionStatus _heldStatus = MissionStatus.None;
}
