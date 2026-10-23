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
	/// Whose position a trigger's areas are offered, in a phrase that follows "fires when" — the type table in
	/// docs/retail/simulation/mission-deployment.md#trigger-areas--actions_evaluatetriggers-00426b70. A group still waiting to
	/// arrive is never tested.
	/// </summary>
	public static string TriggerSubject(short type) => type switch {
		MissionAction.SubjectPlayer => "the player's HERC",
		MissionAction.SubjectPlayerGroup => "any HERC of the player's squad",
		MissionAction.SubjectHumanGroups => "any member of a human group that has arrived",
		MissionAction.SubjectCybridGroups => "any member of a Cybrid group that has arrived",
		MissionAction.SubjectMechGroups => "any member of a herc group that has arrived",
		MissionAction.SubjectFlyerGroups => "any member of a flyer group that has arrived",
		MissionAction.SubjectBaseGroups => "any member of a structure group that has arrived",
		MissionAction.SubjectTargetMech => "a particular herc",
		MissionAction.SubjectTargetFlyer => "a particular flyer",
		MissionAction.SubjectTargetBase => "a particular structure or ground vehicle",
		MissionAction.SubjectTargetGroup => "any member of a particular group",
		_ => $"an unknown kind ({type})"
	};

	/// <summary><see cref="TriggerSubject"/> as a choice in a trigger's Who picker names it, capitalized.</summary>
	public static string TriggerWho(short type) {
		string subject = TriggerSubject(type);
		return char.ToUpperInvariant(subject[0]) + subject[1..];
	}

	/// <summary>
	/// How a group waiting on a trigger arrives, in a phrase that follows "it arrives" — the verb table in
	/// docs/retail/simulation/mission-deployment.md#arrival--group_deploymentcheck-004236c4, where every value but 2 to 5 is
	/// in place.
	/// </summary>
	public static string Arrival(short verb) => verb switch {
		MissionAction.VerbPodWide => "by drop pod, within 90° of the player's heading",
		MissionAction.VerbPodNarrow => "by drop pod, within 22.5° of the player's heading",
		MissionAction.VerbWalkBehind => "on foot, from behind the player",
		MissionAction.VerbWalkAhead => "on foot, ahead of the player",
		_ => "in place"
	};

	/// <summary><see cref="Arrival"/> in a word or two, for a label: "by drop pod", "on foot" or "in place".</summary>
	public static string ArrivalKind(short verb) => verb switch {
		MissionAction.VerbPodWide or MissionAction.VerbPodNarrow => "by drop pod",
		MissionAction.VerbWalkBehind or MissionAction.VerbWalkAhead => "on foot",
		_ => "in place"
	};

	/// <summary>The arrivals a trigger's Arrives choice offers, in its order: the four landings about the player, then in place (1).</summary>
	public static IReadOnlyList<short> Arrivals { get; } = [
		MissionAction.VerbPodWide, MissionAction.VerbPodNarrow, MissionAction.VerbWalkBehind, MissionAction.VerbWalkAhead, 1,
	];

	/// <summary>An arrival as the Arrives choice names it; every value but 2 to 5 is in place.</summary>
	public static string ArrivalChoice(short verb) => verb switch {
		MissionAction.VerbPodWide => "By drop pod, in front of the player",
		MissionAction.VerbPodNarrow => "By drop pod, straight ahead of the player",
		MissionAction.VerbWalkBehind => "On foot, from behind the player",
		MissionAction.VerbWalkAhead => "On foot, from ahead of the player",
		_ => "In place"
	};

	/// <summary>
	/// What an arrival does, in one sentence for the note under the Arrives choice: how far from the player a group lands or
	/// walks in (<see cref="ArrivalDistance"/>) and on what bearing, or that it goes live where it stands.
	/// </summary>
	public static string ArrivalNote(short verb) => verb is MissionAction.VerbPodWide or MissionAction.VerbPodNarrow
			or MissionAction.VerbWalkBehind or MissionAction.VerbWalkAhead
		? $"It arrives {Arrival(verb)}, {ArrivalDistance(verb)} from the player, wherever they are when the trigger fires."
		: "It goes live where it stands when the trigger fires.";

	/// <summary>
	/// How far from the player a group arrives (<see cref="Sim.MissionGroup.PodDistance"/> and the two walk-on distances), in
	/// metres.
	/// </summary>
	public static string ArrivalDistance(short verb) {
		int distance = verb switch {
			MissionAction.VerbWalkBehind => Sim.MissionGroup.WalkOnDistanceBehind,
			MissionAction.VerbWalkAhead => Sim.MissionGroup.WalkOnDistanceAhead,
			_ => Sim.MissionGroup.PodDistance,
		};

		return Metres(distance);
	}

	/// <summary>A distance in world units, in whole metres alone: "300 m".</summary>
	public static string Metres(int worldUnits) => $"{worldUnits / Render.WorldScale.WorldUnitsPerMeter:F0} m";

	/// <summary>
	/// Why a group kept out of the scene (<see cref="PlannedGroup.OffScene"/>) is not shown in it, in one sentence with how
	/// far from the player it arrives and how (<see cref="Arrival"/>): for the group's own view, or for a member's
	/// (<paramref name="member"/>).
	/// </summary>
	public static string OffScene(short verb, bool member) {
		string away = $"{ArrivalDistance(verb)} from the player, wherever they are";
		return member
			? $"Not shown in the scene: its group arrives {away}, when the trigger it waits on fires ({Arrival(verb)})."
			: $"Not shown in the scene: it arrives {away}, when that trigger fires ({Arrival(verb)}).";
	}

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

	/// <summary>
	/// The question an objective asks of its subject — the condition table in
	/// docs/retail/simulation/mission-objectives.md#what-each-condition-asks.
	/// </summary>
	public static string ObjectiveCondition(short code, bool groupSubject) => code switch {
		MissionObjective.ConditionOrderComplete => groupSubject
			? "has finished its order on the route below"
			: "its group has finished its order on the route below",
		MissionObjective.ConditionLost => groupSubject ? "is written off" : "is destroyed or immobilized",
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
			return ObjectName(index, planned);
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

	/// <summary>A placed roster record by its <see cref="MemberName"/> and its group's number: "SCARAB (group 5)"; a squad herc as "(player squad)".</summary>
	public static string ObjectName(MissionIndex index, PlannedObject planned) =>
		planned.Squad || index.Group(planned.Group) is not { } group
			? MemberName(index, planned)
			: $"{MemberName(index, planned)} (group {group.Number})";

	/// <summary>A group: its display number, roster and side, as the outliner names it; group 0 the player's squad.</summary>
	public static string GroupName(PlannedGroup group) {
		if (group.Number == 0) {
			return "Group 0 (player squad)";
		}

		string kind = Enum.IsDefined(group.Kind) ? group.Kind.ToString() : "?";
		return $"Group {group.Number} ({kind}, {group.Side})";
	}

	/// <summary>Row-10 GUID <paramref name="guid"/> as the editor names a trigger until triggers get names: "Trigger 4", by its place in row 10.</summary>
	public static string TriggerName(MissionLayout layout, short guid) =>
		NumberIn(layout, 10, guid) is { } number ? $"Trigger {number}" : "A trigger not in the mission";

	/// <summary>Row-11 GUID <paramref name="guid"/> as the editor names a countdown: "Countdown 2", by its place in row 11.</summary>
	public static string CountdownName(MissionLayout layout, short guid) =>
		NumberIn(layout, 11, guid) is { } number ? $"Countdown {number}" : "A countdown not in the mission";

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

	/// <summary>A list position from 1 as an ordinal: "1st", "2nd", "3rd", "4th", "11th".</summary>
	public static string Ordinal(int position) => (position % 100, position % 10) switch {
		(11 or 12 or 13, _) => $"{position}th",
		(_, 1) => $"{position}st",
		(_, 2) => $"{position}nd",
		(_, 3) => $"{position}rd",
		_ => $"{position}th",
	};

	/// <summary>A binary angle as compass degrees.</summary>
	public static string Heading(int binaryAngle) =>
		$"{BinaryAngle.ToRadians(binaryAngle) * (180f / MathF.PI):F1} deg";

	/// <summary>
	/// A row-11 countdown's delay and what starts it: "30 s after trigger 4" or "30 s from mission start", the starting trigger by
	/// its display number; a start the layout has no trigger for is the mission's start, as the generation resolves it to none.
	/// </summary>
	public static string CountdownSummary(MissionIndex index, ActionTimer30 countdown) =>
		index.Action(countdown.PrimaryActionRef) is { } start
			? $"{countdown.Delay} s after trigger {start.Number}"
			: $"{countdown.Delay} s from mission start";

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
