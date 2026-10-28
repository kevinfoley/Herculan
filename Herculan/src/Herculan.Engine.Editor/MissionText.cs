using HercWorks.Core.Data.File.Msn;
using Herculan.Engine.Numerics;
using Herculan.Engine.World;

namespace Herculan.Engine.Editor;

/// <summary>
/// The editor's wording for what mission records do: arrivals, order verbs, objective conditions, distances and durations.
/// The meanings are the simulation's — each table cites where its codes are documented — and only the wording is the
/// editor's. Records themselves are named by <see cref="MissionNames"/>.
/// </summary>
internal static class MissionText {
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
		MissionAction.SubjectMechGroups => "any member of a HERC group that has arrived",
		MissionAction.SubjectFlyerGroups => "any member of a flyer group that has arrived",
		MissionAction.SubjectBaseGroups => "any member of a structure group that has arrived",
		MissionAction.SubjectTargetMech => "a particular HERC",
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
	/// What an arrival does, in one sentence for the Arrives choice's tooltip: how far from the player a group lands or
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

	/// <summary>
	/// An order verb as the Does choice names it, for the state it installs — the verb table in
	/// docs/retail/simulation/ai-goals.md#when-an-order-is-finished--group_isordercomplete-004239fc.
	/// </summary>
	public static string OrderVerb(short verb) => verb switch {
		MissionOrder.VerbSearchDestroy => "Search and destroy",
		MissionOrder.VerbRam => "Ram",
		MissionOrder.VerbGuard => "Guard",
		MissionOrder.VerbPatrol => "Patrol",
		MissionOrder.VerbSleep => "Sleep",
		MissionOrder.VerbTravel => "Travel",
		MissionOrder.VerbFollow => "Follow",
		_ => $"An unknown order ({verb})"
	};

	/// <summary>
	/// What an order verb does and when the order is done, in one or two sentences, for the Does choice's tooltip: the state it installs
	/// (docs/retail/simulation/ai-navigation.md, docs/retail/simulation/ai-combat-states.md) and its completion test
	/// (docs/retail/simulation/ai-goals.md#when-an-order-is-finished--group_isordercomplete-004239fc). "The group's route" is
	/// the one it walks, its first order's.
	/// </summary>
	public static string OrderVerbNote(short verb) => verb switch {
		MissionOrder.VerbSearchDestroy => "Walks the group's route and fights only its target. Done when the target is destroyed "
			+ "(a group: every member destroyed or immobilized).",
		MissionOrder.VerbRam => "Charges at enemies to crash into them, never firing. Never done by itself: only the trigger in "
			+ "\"Ends when\" moves the group on.",
		MissionOrder.VerbGuard => "Holds a ring around its target. Done when the target is destroyed, or when no enemy group's "
			+ "order points at the target.",
		MissionOrder.VerbPatrol => "Walks the group's route, its leader fighting what it finds. Done at the route's end; a loop "
			+ "never ends.",
		MissionOrder.VerbSleep => "Stands still and does not fight back. Never done by itself: only the trigger in \"Ends when\" moves "
			+ "the group on.",
		MissionOrder.VerbTravel => "Every member walks the group's route, firing at what it passes. Done at the route's end; a "
			+ "loop never ends.",
		MissionOrder.VerbFollow => "Keeps close to its target, firing at what it passes. Done only if the group's route has "
			+ "already run out.",
		_ => "An order the AI has no case for."
	};

	/// <summary>
	/// The note under a convoy's Does choice, in place of <see cref="OrderVerbNote"/>. A ground vehicle drives its group's route,
	/// the first order's, whatever the verb (docs/retail/simulation/ground-vehicles.md#the-ground-vehicle-tick--0046a5d0); the
	/// verb still picks the completion test, which <see cref="ConvoyOrderVerbNote"/> gives per verb.
	/// </summary>
	public const string ConvoyOrderNote =
		"Ground vehicles always drive a route; the selected order only affects when the order counts as done.";

	/// <summary>
	/// When a convoy's order of <paramref name="verb"/> counts as done, for the Does choice's tooltip: its completion test
	/// (docs/retail/simulation/ai-goals.md#when-an-order-is-finished--group_isordercomplete-004239fc), whose route-end test reads
	/// the cursor the convoy's leader steps.
	/// </summary>
	public static string ConvoyOrderVerbNote(short verb) => verb switch {
		MissionOrder.VerbSearchDestroy => "Done when the target is destroyed (a group: every member destroyed or immobilized).",
		MissionOrder.VerbGuard => "Done when the target is destroyed, or when no enemy group's order is against it any more.",
		MissionOrder.VerbPatrol or MissionOrder.VerbTravel or MissionOrder.VerbFollow =>
			"Done when the convoy reaches its route's last waypoint, at once on a route of one waypoint; a loop never ends.",
		MissionOrder.VerbRam or MissionOrder.VerbSleep => "Never done by itself; only the trigger in \"Ends when\" moves the group on.",
		_ => "Never done, as no completion test has a case for it."
	};

	/// <summary>
	/// What an order's target does under <paramref name="verb"/>, for the Target choice's tooltip, then what it does under any
	/// verb: an enemy guard on the same target holds while it is named
	/// (docs/retail/simulation/ai-goals.md#no-rival-group-is-still-working-to-it--group_norivalorderonsubject-00412e74), and, on
	/// the player's squad's orders (<paramref name="squad"/>), it is the mission target
	/// (docs/retail/simulation/mission-objectives.md#the-player-thinks-objective-arms). For a herc group the verb's part is
	/// docs/retail/simulation/ai-navigation.md#the-navigation-states, the guard's reach
	/// docs/retail/simulation/ai-targeting.md#taking-fire--mech_aiontakingfire-0041f7b8-mech-vtable-0x50 and the hold on a
	/// fight docs/retail/simulation/ai-targeting.md#abandoning-a-target--ai_shouldabandontarget-0041c4a8; for a flyer group
	/// (<paramref name="flyer"/>) docs/retail/simulation/ai-flyers.md#the-thinks; a convoy (<paramref name="convoy"/>) moves
	/// none of its vehicles by it, so only the completion test reads it.
	/// </summary>
	public static string OrderTargetNote(short verb, bool flyer, bool convoy, bool squad) {
		const string Holds = "A Cybrid keeps at it until it is destroyed, a human until it is destroyed or immobilized.";
		string own = convoy
			? verb is MissionOrder.VerbSearchDestroy or MissionOrder.VerbGuard
				? "Only decides when the order counts as done."
				: "Has no effect on a convoy."
			: flyer
			? verb == MissionOrder.VerbSearchDestroy ? "What it hunts: the flight attacks only the target." : "Has no effect on a flyer group."
			: verb switch {
				MissionOrder.VerbSearchDestroy => "What it hunts: it fights only the target and walks past everything else, unless shot "
					+ $"at. In a fight it goes for the target first. {Holds}",
				MissionOrder.VerbRam => "Has no effect on whom it rams: the group charges whatever enemy it picks, with no preference "
					+ "for the target.",
				MissionOrder.VerbGuard => "Sets where it stands and how far it reaches. With no target, it guards its route's first "
					+ $"waypoint and attacks any hostile HERC it knows of within {Metres(Sim.Ai.AiTargeting.OpenDefenceRange)}, holding "
					+ "until \"Ends when\" fires. With a target, it guards the target's position (a group's first member) and attacks "
					+ $"within {Metres(Sim.Ai.AiTargeting.DefenceRange)}; the order also ends once the target is destroyed, or once no "
					+ "enemy group's order names the target.",
				MissionOrder.VerbPatrol or MissionOrder.VerbTravel => "Doesn't change where it goes or whom it fights. A member "
					+ $"already fighting the target keeps at it longer. {Holds}",
				MissionOrder.VerbSleep => "Has no effect: a sleeping group stands still and doesn't fight back.",
				MissionOrder.VerbFollow => "What it follows (a group's first member). It walks to it and stops "
					+ $"{Metres(Sim.MechObject.FollowStandoffRange)} short, keeping up as the target moves. Reaching the target doesn't "
					+ "end the order.",
				_ => "An order the AI has no case for.",
			};

		string any = "An enemy group guarding this same target keeps guarding while this order names it.";
		return squad
			? $"{own}{Environment.NewLine}{Environment.NewLine}{any} This is the mission target: in most missions the computer "
				+ "announces it when the player first selects it. In a data-link mission it's what the player links with, and "
				+ "squadmates usually won't shoot it."
			: $"{own}{Environment.NewLine}{Environment.NewLine}{any}";
	}

	/// <summary>
	/// What an objective asks of its subject, as the What happens choice names it, for a group or for an object — the
	/// condition table in docs/retail/simulation/mission-objectives.md#what-each-condition-asks. Conditions 3 and 4, and 9 and
	/// 10, ask the same.
	/// </summary>
	public static string ObjectiveChoice(short code, bool group) => code switch {
		MissionObjective.ConditionOrderComplete => group ? "Finish their order on a route" : "Its group finishes its order on a route",
		MissionObjective.ConditionLost => group ? "Are written off" : "Is destroyed or immobilized",
		MissionObjective.ConditionClear => group ? "Are clear of threats" : "Is clear of threats",
		MissionObjective.ConditionDataLink or MissionObjective.ConditionDataLinkAlso => "The player completes a data link",
		MissionObjective.ConditionEngaged => group ? "Have a member engaged" : "Is engaged",
		MissionObjective.ConditionDisarmed => group ? "Are all disarmed" : "Is disarmed",
		MissionObjective.ConditionUnengaged => group ? "Have no member engaged" : "Is not engaged",
		MissionObjective.ConditionNoDataLink or MissionObjective.ConditionNoDataLinkAlso => "The player has not completed a data link",
		_ => $"An unknown test ({code})"
	};

	/// <summary>
	/// What an objective's condition means in play, in a sentence or two for the What happens choice's tooltips;
	/// <paramref name="clear"/> and <paramref name="clearTarget"/> are the clear-of-threats distances, in words.
	/// </summary>
	public static string ObjectiveNote(short code, bool group, string clear, string clearTarget) => code switch {
		MissionObjective.ConditionOrderComplete => "True once the group finishes by itself its first order on the route below; a "
			+ "trigger moving it on does not count.",
		MissionObjective.ConditionLost => group
			? "Written off: a Cybrid group once every member is destroyed or immobilized; a human group once about two thirds of "
				+ "its members are, or they are three quarters damaged on average."
			: "True once it is destroyed or immobilized.",
		MissionObjective.ConditionClear => (group ? "True while it is not written off and no living member has " : "True while it has ")
			+ $"an enemy aware of it within {clear} ({clearTarget} if it is that enemy's target), or an enemy sent after it still "
			+ "on its way.",
		MissionObjective.ConditionDataLink or MissionObjective.ConditionDataLinkAlso => "Tests the player alone: true once the player "
			+ "has completed a data link, holding position facing the target of their order. Only a mission whose start settings "
			+ "choose the data link ever completes one.",
		MissionObjective.ConditionEngaged => (group ? "True once any member has been engaged: " : "True once it has been engaged: ")
			+ $"an enemy within {Metres(Sim.Detection.EngagementRange)} has spotted it, or it has hit the target it is aiming at.",
		MissionObjective.ConditionDisarmed => group
			? "True once every member has no working weapon left; a group with no members counts as disarmed."
			: "True once it has no working weapon left.",
		MissionObjective.ConditionUnengaged => group
			? "True while no member has been engaged, the opposite of Have a member engaged."
			: "True while it has not been engaged, the opposite of Is engaged.",
		MissionObjective.ConditionNoDataLink or MissionObjective.ConditionNoDataLinkAlso => "Tests the player alone: true while the "
			+ "player has not completed a data link.",
		_ => "The mission has no test for this code: it answers as the objective before it does."
	};

	/// <summary>
	/// The number the editor shows for a record at <paramref name="position"/> in its row, from 0: every record number a
	/// designer sees counts from 1 (docs/herculan/handoff-editor-ux-plan.md#decided). It is the record's place among its row's
	/// identities, not its index in a file, so nothing is matched against a file through it.
	/// </summary>
	public static int Number(int position) => position + 1;

	/// <summary>
	/// The number the editor shows for row-<paramref name="row"/> GUID <paramref name="guid"/> in <paramref name="layout"/>
	/// (<see cref="Number"/>); null for <c>-1</c> or a GUID the row has no entry for.
	/// </summary>
	public static int? NumberIn(MissionLayout layout, int row, short guid) {
		if (guid == -1) {
			return null;
		}

		var entries = layout.Entries(row);
		for (int i = 0; i < entries.Count; i++) {
			if (entries[i].Guid == guid) {
				return Number(i);
			}
		}

		return null;
	}

	/// <summary>
	/// Waypoint <paramref name="index"/> of a route, from 0, as the route view names it: "Start" for the first, where a group
	/// stands, then "Waypoint 1" on. That is the cockpit's numbering: the waypoint indicator reads "WAYPOINT n" for the route's
	/// waypoint <i>n</i> counted from its start, and the heads-down map's first route marker is on waypoint 1
	/// (docs/retail/simulation/cockpit-gunsight-hud.md#waypoint-indicators, docs/retail/simulation/heads-down-display.md#markers).
	/// </summary>
	public static string Waypoint(int index) => index == 0 ? "Start" : $"Waypoint {index}";

	/// <summary><see cref="Waypoint"/> inside a sentence: "the start", "waypoint 2".</summary>
	public static string WaypointInSentence(int index) => index == 0 ? "the start" : $"waypoint {index}";

	/// <summary><see cref="Waypoint"/> as a label in the scene: "Start", "2".</summary>
	public static string WaypointMark(int index) => index == 0 ? "Start" : index.ToString();

	/// <summary>
	/// How far waypoint <paramref name="index"/> of <paramref name="points"/> lies from the one before it, along the ground:
	/// "420 m from waypoint 2"; null for the start, which has none before it.
	/// </summary>
	public static string? LegFromPrevious(IReadOnlyList<Vec3i> points, int index) {
		if (index <= 0 || index >= points.Count) {
			return null;
		}

		var (from, to) = (points[index - 1], points[index]);
		double dx = to.X - from.X, dy = to.Y - from.Y;
		return $"{Metres((int)Math.Round(Math.Sqrt(dx * dx + dy * dy)))} from {WaypointInSentence(index - 1)}";
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
