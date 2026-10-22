using HercWorks.Core.Data.File.Msn;
using Herculan.Engine.Numerics;
using Herculan.Engine.Scene;
using Herculan.Engine.World;

namespace Herculan.Engine.Editor;

/// <summary>
/// How the editor names mission records in its panels. The meanings are the simulation's — each
/// table cites where its codes are documented — and only the wording is the editor's.
/// </summary>
internal static class MissionText {
	private const int RouteRow = 8;
	private const int MechRow = 12;
	private const int FlyerRow = 13;
	private const int BaseRow = 14;

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
	public static string DescribeGroup(GeneratedIndex index, int group) {
		if (group == 0) {
			return "Group 0 (player squad)";
		}

		string kind = index.KindOf(group)?.ToString() ?? "?";
		string side = index.SideOf(group)?.ToString() ?? "?";
		return $"Group {group} ({kind}, {side})";
	}

	/// <summary>
	/// An order's or objective's subject as a choice names it: kind -1 none, 0 a group, 1 a herc, 2 a flyer and 3 a
	/// structure, the numbering both records share, by block index.
	/// </summary>
	public static string SubjectName(GeneratedIndex index, short kind, int subject) {
		if (kind == -1 || subject < 0) {
			return "None";
		}

		return kind switch {
			0 => DescribeGroup(index, subject),
			1 => index.ObjectAt(MissionUnitKind.Mech, subject) is { } herc ? Describe(herc) : $"Herc slot {subject} (not placed)",
			3 => index.ObjectAt(MissionUnitKind.Base, subject) is { } structure ? Describe(structure) : $"Structure slot {subject} (not placed)",
			2 => index.ObjectAt(MissionUnitKind.Flyer, subject) is { } flyer ? Describe(flyer) : $"Flyer slot {subject} (not placed)",
			_ => $"Subject kind {kind}, slot {subject}",
		};
	}

	/// <summary>
	/// Whose position an action's trigger areas are offered — the type table in
	/// docs/retail/simulation/mission-deployment.md#trigger-areas--actions_evaluatetriggers-00426b70.
	/// </summary>
	public static string TriggerSubject(short type) => type switch {
		MissionAction.SubjectPlayer => "the player's Herc",
		MissionAction.SubjectPlayerGroup => "any machine of the player's squad",
		MissionAction.SubjectHumanGroups => "any member of a deployed human group",
		MissionAction.SubjectCybridGroups => "any member of a deployed Cybrid group",
		MissionAction.SubjectMechGroups => "any member of a deployed herc group",
		MissionAction.SubjectFlyerGroups => "any member of a deployed flyer group",
		MissionAction.SubjectBaseGroups => "any member of a deployed structure group",
		MissionAction.SubjectTargetMech => "its target herc",
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
		string question = ObjectiveQuestion(objective.ConditionCode, $"order on route {objective.RouteRef} done");
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

	/// <summary>
	/// A row-17 objective in one line, as <see cref="DescribeObjective(MissionObjective, int)"/> describes a generated one, from
	/// the layout (docs/herculan/handoff-editor-viewing.md#selection-and-panels): its subject a group by its display number, an
	/// object by <see cref="SubjectName"/>, and its route by its place in row 8.
	/// </summary>
	/// <param name="number">Its place in <see cref="MissionLayout.Objectives"/>.</param>
	public static string DescribeObjective(MissionIndex index, LayoutObjective objective, int number) {
		var fields = objective.Fields;
		string done = NumberIn(index.Layout, RouteRow, fields.RouteRef) is { } route
			? $"order on route {route} done"
			: "order done, no route";
		string question = ObjectiveQuestion(fields.ConditionCode, done);
		string role = fields.Required == MissionObjective.Mandatory ? "mandatory" : "failure";
		return $"Objective {number}, {role}: {SubjectName(index, fields.SubjectKind, fields.SubjectRef)} {question}";
	}

	/// <summary>
	/// An objective's subject from the layout: kind 0 a group by its display number, 1 a herc, 2 a flyer and 3 a structure or
	/// ground vehicle by its <see cref="MemberName"/> and its group's number, or by its type when no group places it.
	/// </summary>
	private static string SubjectName(MissionIndex index, short kind, short subject) {
		if (kind == 0) {
			return index.Group(subject) is { } group ? $"group {group.Number}" : "no group";
		}

		int? row = kind switch { 1 => MechRow, 2 => FlyerRow, 3 => BaseRow, _ => null };
		if (row is not { } roster) {
			return $"subject kind {kind}";
		}

		var key = new ObjectKey(roster, subject);
		if (index.Plan.Object(key) is { } planned) {
			return planned.Squad || index.Group(planned.Group) is not { } group
				? MemberName(index, planned)
				: $"{MemberName(index, planned)} (group {group.Number})";
		}

		short? type = index.Layout.Entry(roster, subject)?.Fields switch {
			MechRosterEntry144 herc => herc.TypeIndex,
			FlyerRosterEntry102 flyer => flyer.TypeIndex,
			BaseRosterEntry62 structure => structure.TypeIndex,
			_ => null
		};

		string what = roster switch { MechRow => "Herc", FlyerRow => "Flyer", _ => "Structure" };
		return type is { } typeIndex ? $"{what} type {typeIndex} (in no group)" : $"no {what.ToLowerInvariant()}";
	}

	// The question an objective asks, in a word or two; <orderDone> for an order-complete one.
	private static string ObjectiveQuestion(short code, string orderDone) => code switch {
		MissionObjective.ConditionOrderComplete => orderDone,
		MissionObjective.ConditionLost => "lost",
		MissionObjective.ConditionClear => "clear of threats",
		MissionObjective.ConditionDataLink or MissionObjective.ConditionDataLinkAlso => "data link",
		MissionObjective.ConditionEngaged => "engaged",
		MissionObjective.ConditionDisarmed => "disarmed",
		MissionObjective.ConditionUnengaged => "not engaged",
		MissionObjective.ConditionNoDataLink or MissionObjective.ConditionNoDataLinkAlso => "no data link",
		_ => $"code {code}"
	};

	/// <summary>
	/// A placed roster record as the outliner names it (docs/herculan/handoff-editor-ux-review.md#4-identity-strings-built-from-roster-slots-and-enum-names):
	/// its type name, else its kind and type number; numbered "2 of 3" in member order when its group has more than one of
	/// its type; and "(player squad)" for a squad herc.
	/// </summary>
	public static string MemberName(MissionIndex index, PlannedObject planned) {
		string type = TypeName(index, planned);
		string name = type;
		if (index.Group(planned.Group) is { } group) {
			var same = group.Members
				.Where(key => key.Row == planned.Key.Row && index.Plan.Object(key)?.TypeIndex == planned.TypeIndex)
				.ToList();
			if (same.Count > 1) {
				// A comma keeps "Structure type 40, 2 of 3" from reading as one number.
				string count = $"{same.IndexOf(planned.Key) + 1} of {same.Count}";
				name = char.IsDigit(type[^1]) ? $"{type}, {count}" : $"{type} {count}";
			}
		}

		return planned.Squad ? $"{name} (player squad)" : name;
	}

	// A placed record's type name, else its kind and type number: a ground vehicle told from a structure by its object's type.
	private static string TypeName(MissionIndex index, PlannedObject planned) {
		if (planned.TypeName is { } name) {
			return name;
		}

		string kind = planned.Kind switch {
			MissionUnitKind.Mech => "Herc",
			MissionUnitKind.Flyer => "Flyer",
			_ => index.ObjectOf(planned.Key)?.Object is Sim.BaseObject { Type.IsVehicle: true } ? "Ground vehicle" : "Structure"
		};

		return $"{kind} type {planned.TypeIndex}";
	}

	/// <summary>
	/// Where row-<paramref name="row"/> GUID <paramref name="guid"/> stands in its row of <paramref name="layout"/>, its display
	/// number; null for <c>-1</c> or a GUID the row has no entry for.
	/// </summary>
	public static int? NumberIn(MissionLayout layout, int row, short guid) {
		if (guid == -1) {
			return null;
		}

		var entries = layout.Entries(row);
		for (int i = 0; i < entries.Count; i++) {
			if (entries[i].Guid == guid) {
				return i;
			}
		}

		return null;
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

	/// <summary>
	/// A row-11 timer's delay and what starts it, as <see cref="TimerSummary(MissionActionTimer)"/> says it of a generated one,
	/// the starting action by its display number; a start the layout has no action for is the mission's start, as the
	/// generation resolves it to none.
	/// </summary>
	public static string TimerSummary(MissionIndex index, ActionTimer30 timer) =>
		index.Action(timer.PrimaryActionRef) is { } start
			? $"{timer.Delay} s after action {start.Number} fires"
			: $"{timer.Delay} s into the mission";

	/// <summary>A duration of <paramref name="seconds"/> in hours, minutes and seconds, leaving out the parts that are 0.</summary>
	public static string Duration(int seconds) {
		if (seconds < 60) {
			return $"{seconds} s";
		}

		var parts = new List<string>();
		if (seconds >= 3600) {
			parts.Add($"{seconds / 3600} h");
		}

		if (seconds / 60 % 60 != 0) {
			parts.Add($"{seconds / 60 % 60} min");
		}

		if (seconds % 60 != 0) {
			parts.Add($"{seconds % 60} s");
		}

		return string.Join(" ", parts);
	}

	/// <summary>A distance in world units, with metres beside it.</summary>
	public static string Distance(int worldUnits) =>
		$"{worldUnits} ({worldUnits / Render.WorldScale.WorldUnitsPerMeter:F0} m)";
}
