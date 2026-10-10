using Herculan.Engine.Numerics;
using Herculan.Engine.Scene;
using Herculan.Engine.World;

namespace Herculan.Engine.Host.Editor;

/// <summary>
/// How the editor names mission records in its panels. The meanings are the simulation's — each
/// table cites where its codes are documented — and only the wording is the editor's.
/// </summary>
internal static class MissionText {
	/// <summary>An object, by type and the roster slot the mission's refs name it by.</summary>
	public static string Describe(SceneObject placed) {
		var placement = placed.Placement;
		string type = placement.TypeName ?? $"{placement.Kind} type {placement.TypeIndex}";
		string where = placement.IsPlayerLance
			? "player squad"
			: $"{placement.Kind.ToString().ToLowerInvariant()} slot {placement.SlotIndex}";
		return $"{type} ({where})";
	}

	/// <summary>A group: its index, roster and side.</summary>
	public static string DescribeGroup(Mission mission, int group) {
		if (group == 0) {
			return "Group 0 (player squad)";
		}

		string kind = group < mission.GroupKinds.Count ? mission.GroupKinds[group].ToString() : "?";
		string side = group < mission.GroupSides.Count ? mission.GroupSides[group].ToString() : "?";
		return $"Group {group} ({kind}, {side})";
	}

	/// <summary>
	/// Whose position an action's trigger areas are offered — the type table in
	/// docs/retail/simulation/mission-deployment.md#trigger-areas--actions_evaluatetriggers-00426b70.
	/// </summary>
	public static string TriggerSubject(short type) => type switch {
		MissionAction.SubjectPlayer => "the player's machine",
		MissionAction.SubjectPlayerGroup => "any machine of the player's group",
		MissionAction.SubjectHumanGroups => "any member of a deployed human group",
		MissionAction.SubjectCybridGroups => "any member of a deployed Cybrid group",
		MissionAction.SubjectMechGroups => "any member of a deployed mech group",
		MissionAction.SubjectFlyerGroups => "any member of a deployed flyer group",
		MissionAction.SubjectBaseGroups => "any member of a deployed structure group",
		MissionAction.SubjectTargetMech => "its target mech",
		MissionAction.SubjectTargetFlyer => "its target flyer",
		MissionAction.SubjectTargetBase => "its target structure",
		MissionAction.SubjectTargetGroup => "any member of its target group",
		_ => $"nothing (type {type})"
	};

	/// <summary>
	/// How a group waiting on an action arrives — the verb table in
	/// docs/retail/simulation/mission-deployment.md#arrival--group_deploymentcheck-004236c4.
	/// </summary>
	public static string Arrival(short verb) => verb switch {
		MissionAction.VerbPodWide => "drop pod, within 90° of the player's heading",
		MissionAction.VerbPodNarrow => "drop pod, within 22.5° of the player's heading",
		MissionAction.VerbWalkBehind => "on foot, from behind the player",
		MissionAction.VerbWalkAhead => "on foot, ahead of the player",
		_ => "in place"
	};

	/// <summary><see cref="Arrival"/> in a word or two, for a label.</summary>
	public static string ArrivalKind(short verb) => verb switch {
		MissionAction.VerbPodWide or MissionAction.VerbPodNarrow => "drop pod",
		MissionAction.VerbWalkBehind or MissionAction.VerbWalkAhead => "on foot",
		_ => "in place"
	};

	/// <summary>The state an order verb installs — docs/retail/simulation/ai-goals.md#when-an-order-is-finished--group_isordercomplete-004239fc.</summary>
	public static string OrderVerb(short verb) => verb switch {
		MissionOrder.VerbSearchDestroy => "search/destroy",
		MissionOrder.VerbRam => "ramming",
		MissionOrder.VerbGuard => "guarding",
		MissionOrder.VerbPatrol => "patrolling",
		MissionOrder.VerbSleep => "sleeping",
		MissionOrder.VerbTravel => "travelling",
		MissionOrder.VerbFollow => "following",
		_ => $"verb {verb}"
	};

	/// <summary>What one of an action's counter operations does to its counter.</summary>
	public static string ActionCounterOp(short op) => op switch {
		MissionAction.CounterIncrement => "+1",
		MissionAction.CounterClear => "cleared",
		_ => $"op {op}, no effect"
	};

	/// <summary>
	/// What one of an objective's counter operations does — a different code set from the action's,
	/// see docs/retail/simulation/mission-objectives.md#the-record.
	/// </summary>
	public static string ObjectiveCounterOp(short op) => op switch {
		MissionObjective.CounterSet => "set to 1",
		MissionObjective.CounterClear => "cleared",
		MissionObjective.CounterIncrement => "+1",
		MissionObjective.CounterDecrement => "-1",
		_ => $"op {op}, no effect"
	};

	/// <summary>
	/// What one of an out-of-action report's operations does — docs/retail/simulation/mission-deployment.md#the-out-of-action-report.
	/// </summary>
	public static string OutOfActionOp(short op) => op switch {
		OutOfActionReport.OpClear => "cleared",
		OutOfActionReport.OpIncrement => "+1",
		>= OutOfActionReport.OpSetFirst and <= OutOfActionReport.OpSetLast => $"set to {op - OutOfActionReport.SetBias}",
		_ => $"op {op}, no effect"
	};

	/// <summary>
	/// The question an objective asks of its subject — the condition table in
	/// docs/retail/simulation/mission-objectives.md#what-each-condition-asks.
	/// </summary>
	public static string ObjectiveCondition(short code, bool groupSubject) => code switch {
		MissionObjective.ConditionOrderComplete => groupSubject
			? "has finished its order on the route below"
			: "its group has finished its order on the route below",
		MissionObjective.ConditionLost => groupSubject ? "is written off" : "is destroyed or immobilised",
		MissionObjective.ConditionClear => groupSubject
			? "is not written off, and every living member is clear of threats"
			: "is clear of threats",
		MissionObjective.ConditionDataLink or MissionObjective.ConditionDataLinkAlso =>
			"the player has completed a data link (the subject is not read)",
		MissionObjective.ConditionEngaged => groupSubject ? "has a member that has been engaged" : "has been engaged",
		MissionObjective.ConditionDisarmed => groupSubject ? "has every member disarmed" : "is disarmed",
		MissionObjective.ConditionUnengaged => groupSubject ? "has no member engaged" : "has not been engaged",
		MissionObjective.ConditionNoDataLink or MissionObjective.ConditionNoDataLinkAlso =>
			"the player has not completed a data link (the subject is not read)",
		_ => $"code {code} has no case: it answers whatever the record before it answered"
	};

	/// <summary>An objective in one line: its index, whether it is mandatory or a failure condition, and its question.</summary>
	public static string DescribeObjective(MissionObjective objective, int index) {
		string question = objective.ConditionCode switch {
			MissionObjective.ConditionOrderComplete => $"order on route {objective.RouteRef} done",
			MissionObjective.ConditionLost => "lost",
			MissionObjective.ConditionClear => "clear of threats",
			MissionObjective.ConditionDataLink or MissionObjective.ConditionDataLinkAlso => "data link",
			MissionObjective.ConditionEngaged => "engaged",
			MissionObjective.ConditionDisarmed => "disarmed",
			MissionObjective.ConditionUnengaged => "not engaged",
			MissionObjective.ConditionNoDataLink or MissionObjective.ConditionNoDataLinkAlso => "no data link",
			_ => $"code {objective.ConditionCode}"
		};

		string subject = objective.SubjectKind switch {
			MissionObjectiveSubject.Group => $"group {objective.SubjectRef}",
			MissionObjectiveSubject.Mech => $"mech slot {objective.SubjectRef}",
			MissionObjectiveSubject.Flyer => $"flyer slot {objective.SubjectRef}",
			MissionObjectiveSubject.Base => $"base slot {objective.SubjectRef}",
			_ => $"subject kind {(int)objective.SubjectKind}"
		};

		string role = objective.Required == MissionObjective.Mandatory ? "mandatory" : "failure";
		return $"Objective {index}, {role}: {subject} {question}";
	}

	/// <summary>A binary angle as compass degrees.</summary>
	public static string Heading(int binaryAngle) =>
		$"{BinaryAngle.ToRadians(binaryAngle) * (180f / MathF.PI):F1} deg";

	/// <summary>A timer's delay and what starts it.</summary>
	public static string TimerSummary(MissionActionTimer timer) {
		int seconds = timer.Delay >> MissionActionTimer.DelayShift;
		return timer.PrimaryActionRef >= 0
			? $"{seconds} s after action {timer.PrimaryActionRef} fires"
			: $"{seconds} s into the mission";
	}

	/// <summary>A distance in world units, with metres beside it.</summary>
	public static string Distance(int worldUnits) =>
		$"{worldUnits} ({worldUnits / Render.WorldScale.WorldUnitsPerMeter:F0} m)";
}
