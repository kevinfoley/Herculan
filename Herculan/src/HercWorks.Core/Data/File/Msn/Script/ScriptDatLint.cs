namespace HercWorks.Core.Data.File.Msn.Script;

/// <summary>The <c>script.dat</c> block a <see cref="ScriptLintFinding"/> is about, by its block number.</summary>
public enum ScriptBlock {
	Points = 1,
	Headings = 2,
	Routes = 3,
	TriggerAreas = 4,
	Actions = 5,
	ActionTimers = 6,
	Mechs = 7,
	Flyers = 8,
	Bases = 9,
	Orders = 10,
	Groups = 11,
	Objectives = 12,
	ObjectiveLines = 13
}

/// <summary>What kind of problem a <see cref="ScriptLintFinding"/> is, so a tool can phrase it in its own terms.</summary>
public enum ScriptLintKind {
	/// <summary>A ref past the end of the block it indexes (<see cref="ScriptLintFinding.RefersTo"/>), or below -1; the value is the ref.</summary>
	BrokenRef,

	/// <summary>An action of type 7 to 10, which tests a particular herc, flyer, structure or group, with no target.</summary>
	NoTarget,

	/// <summary>An action with a message that writes no counter, so DBSIM never posts the message.</summary>
	MessageNeverPosted,

	/// <summary>An action area ref behind its first unset slot, which DBSIM never tests.</summary>
	AreaAfterGap,

	/// <summary>A roster past DBSIM's slot cap; the value is the slots it needs.</summary>
	RosterFull,

	/// <summary>A group with neither a heading nor a slot-0 order, which DBSIM faults loading.</summary>
	NoHeadingNoOrder,

	/// <summary>A group whose roster discriminator has no case; the value is the discriminator.</summary>
	UnknownRoster,

	/// <summary>A herc group with members and no slot-0 order, whose AI faults on a null behaviour.</summary>
	HercGroupNoOrder,

	/// <summary>An objective condition code with no case; the value is the code.</summary>
	UnknownCondition,

	/// <summary>An objective subject kind with no case; the value is the kind.</summary>
	UnknownSubject,

	/// <summary><c>mission.str</c> longer than DBSIM reads; the value is its line count.</summary>
	TextTooLong,
}

/// <summary>One problem <see cref="ScriptDatLint"/> found, and the record it is in.</summary>
/// <param name="Block">The block holding the record.</param>
/// <param name="Index">The record's index in that block, or -1 for the block as a whole.</param>
/// <param name="Message">What is wrong, naming the record, in <c>script.dat</c> terms.</param>
/// <param name="Kind">What kind of problem it is.</param>
/// <param name="Value">The number the kind names (the ref, the slots, the code or the lines), or 0.</param>
/// <param name="RefersTo">For a <see cref="ScriptLintKind.BrokenRef"/>, the block the ref indexes, when it is one block.</param>
public sealed record ScriptLintFinding(ScriptBlock Block, int Index, string Message, ScriptLintKind Kind, int Value = 0,
	ScriptBlock? RefersTo = null);

/// <summary>
/// Checks a <c>script.dat</c> for what an edit can break and no retail file does: refs past the end of
/// the block they index, records DBSIM never reads, and shapes it faults on. Advisory — it says what
/// is wrong and leaves the decision to the caller.
/// </summary>
public static class ScriptDatLint {
	/// <summary>
	/// Slots DBSIM's per-roster arrays hold — mech (shared with the player's squad), flyer and
	/// structure. See docs/retail/formats/script-dat.md#the-two-pass-read--and-what-it-means-for-dbsim-keeps.
	/// </summary>
	public const int MechSlotCap = 100;

	/// <inheritdoc cref="MechSlotCap"/>
	public const int FlyerSlotCap = 50;

	/// <inheritdoc cref="MechSlotCap"/>
	public const int BaseSlotCap = 140;

	/// <summary>Runs every check over <paramref name="script"/>, in block order, then over <c>mission.str</c>'s length.</summary>
	/// <param name="squadSize">
	/// How many machines <c>player.mec</c> brings, which share the block-7 slot cap; 0 when the
	/// caller does not know, in which case that cap is checked against the roster alone.
	/// </param>
	/// <param name="textLines">How many lines the <c>mission.str</c> beside it has; 0 when the caller does not know, which checks nothing.</param>
	public static List<ScriptLintFinding> Check(ScriptDat script, int squadSize = 0, int textLines = 0) {
		var findings = new List<ScriptLintFinding>();

		int points = script.Coordinates.Length;
		int headings = script.Headings.Length;
		int routes = script.WaypointGroups.Length;
		int actions = script.Actions.Length;

		for (int i = 0; i < routes; i++) {
			Refs(findings, ScriptBlock.Routes, i, $"Route {i} waypoints", script.WaypointGroups[i].Waypoints, points, ScriptBlock.Points);
		}

		for (int i = 0; i < script.TriggerAreas.Length; i++) {
			var area = script.TriggerAreas[i];
			Ref(findings, ScriptBlock.TriggerAreas, i, $"Trigger area {i} point ref", area.PointRef, points, ScriptBlock.Points);
			if (area.Shape == 0) {
				Ref(findings, ScriptBlock.TriggerAreas, i, $"Trigger area {i} second corner", area.SecondPointOrRadius, points, ScriptBlock.Points);
			}
		}

		for (int i = 0; i < actions; i++) {
			var action = script.Actions[i];
			Refs(findings, ScriptBlock.Actions, i, $"Action {i} trigger area refs", action.AreaRefs, script.TriggerAreas.Length, ScriptBlock.TriggerAreas);

			if (TargetCount(script, action.Type) is { } targets) {
				Ref(findings, ScriptBlock.Actions, i, $"Action {i} target ref", action.TargetRef, targets.Count, targets.Block);

				// DBSim_SpawnMissionObjects (004253d8) resolves a type 7-10 target through Mission_ResolveRefByKind
				// (00425348), which indexes with no range test, and Actions_EvaluateTriggers then reads a position through
				// the result: an unset ref names no object of the mission.
				if (action.TargetRef == -1) {
					findings.Add(new(ScriptBlock.Actions, i,
						$"Action {i} is type {action.Type} but has no target; DBSIM resolves the unset ref to no object of the mission.",
					ScriptLintKind.NoTarget, action.Type));
				}
			}

			// Action_Activate (00423430) queues the message inside its counter loop, once per pair naming a counter.
			if (action.MessageId > 0 && action.CounterRefs.All(counter => counter < 0)) {
				findings.Add(new(ScriptBlock.Actions, i,
					$"Action {i} has a message but writes no counter, so DBSIM never posts it.", ScriptLintKind.MessageNeverPosted));
			}

			// DBSim_LoadScriptDat counts an action's areas up to the first negative ref and allocates
			// exactly that many, so a ref behind the gap is never tested.
			int gap = Array.FindIndex(action.AreaRefs, value => value < 0);
			if (gap >= 0 && action.AreaRefs.Skip(gap).Any(value => value >= 0)) {
				findings.Add(new(ScriptBlock.Actions, i,
					$"Action {i} references a trigger area after its first unset slot ({gap}); DBSIM never tests it.",
					ScriptLintKind.AreaAfterGap, gap));
			}
		}

		for (int i = 0; i < script.ActionTimers.Length; i++) {
			var timer = script.ActionTimers[i];
			Ref(findings, ScriptBlock.ActionTimers, i, $"Action timer {i} action ref", timer.PrimaryActionRef, actions, ScriptBlock.Actions);
			Refs(findings, ScriptBlock.ActionTimers, i, $"Action timer {i} sequence refs", timer.SequenceRefs, actions, ScriptBlock.Actions);
		}

		for (int i = 0; i < script.Mechs.Length; i++) {
			var mech = script.Mechs[i];
			Ref(findings, ScriptBlock.Mechs, i, $"Herc {i} point ref", mech.PositionRef, points, ScriptBlock.Points);
			Ref(findings, ScriptBlock.Mechs, i, $"Herc {i} heading ref", mech.HeadingRef, headings, ScriptBlock.Headings);
			Ref(findings, ScriptBlock.Mechs, i, $"Herc {i} engaged action", mech.EngagementActionRef, actions, ScriptBlock.Actions);
			Ref(findings, ScriptBlock.Mechs, i, $"Herc {i} defeated action", mech.DefeatActionRef, actions, ScriptBlock.Actions);
		}

		for (int i = 0; i < script.Flyers.Length; i++) {
			var flyer = script.Flyers[i];
			Ref(findings, ScriptBlock.Flyers, i, $"Flyer {i} point ref", flyer.PositionRef, points, ScriptBlock.Points);
			Ref(findings, ScriptBlock.Flyers, i, $"Flyer {i} heading ref", flyer.HeadingRef, headings, ScriptBlock.Headings);
			Ref(findings, ScriptBlock.Flyers, i, $"Flyer {i} engaged action", flyer.EngagementActionRef, actions, ScriptBlock.Actions);
			Ref(findings, ScriptBlock.Flyers, i, $"Flyer {i} defeated action", flyer.DefeatActionRef, actions, ScriptBlock.Actions);
		}

		for (int i = 0; i < script.Bases.Length; i++) {
			var structure = script.Bases[i];
			Ref(findings, ScriptBlock.Bases, i, $"Base {i} point ref", structure.PositionRef, points, ScriptBlock.Points);
			Ref(findings, ScriptBlock.Bases, i, $"Base {i} heading ref", structure.HeadingRef, headings, ScriptBlock.Headings);
			Ref(findings, ScriptBlock.Bases, i, $"Base {i} engaged action", structure.EngagementActionRef, actions, ScriptBlock.Actions);
			Ref(findings, ScriptBlock.Bases, i, $"Base {i} defeated action", structure.DefeatActionRef, actions, ScriptBlock.Actions);
		}

		Cap(findings, ScriptBlock.Mechs, script.Mechs.Length + squadSize, MechSlotCap,
			squadSize > 0 ? $"{script.Mechs.Length} hercs plus a squad of {squadSize}" : $"{script.Mechs.Length} hercs");
		Cap(findings, ScriptBlock.Flyers, script.Flyers.Length, FlyerSlotCap, $"{script.Flyers.Length} flyers");
		Cap(findings, ScriptBlock.Bases, script.Bases.Length, BaseSlotCap, $"{script.Bases.Length} bases");

		for (int i = 0; i < script.Orders.Length; i++) {
			var order = script.Orders[i];
			Ref(findings, ScriptBlock.Orders, i, $"Order {i} route ref", order.RouteRef, routes, ScriptBlock.Routes);
			Ref(findings, ScriptBlock.Orders, i, $"Order {i} action ref", order.ActionRef, actions, ScriptBlock.Actions);
			if (order.SubjectKind >= 0) {
				var subjects = SubjectCount(script, order.SubjectKind);
				Ref(findings, ScriptBlock.Orders, i, $"Order {i} subject ref", order.SubjectRef, subjects.Count, subjects.Block);
			}
		}

		for (int i = 0; i < script.Groups.Length; i++) {
			CheckGroup(script, i, findings);
		}

		for (int i = 0; i < script.Objectives.Length; i++) {
			var objective = script.Objectives[i];
			Ref(findings, ScriptBlock.Objectives, i, $"Objective {i} route ref", objective.RouteRef, routes, ScriptBlock.Routes);
			var subjects = SubjectCount(script, objective.SubjectKind);
			Ref(findings, ScriptBlock.Objectives, i, $"Objective {i} subject ref", objective.SubjectRef, subjects.Count, subjects.Block);

			// Mission_EvaluateObjectives (00413280) switches on codes 0-10 with no case for 5, and on
			// subject kinds 0-3; anything else leaves its result register as the previous record set it.
			if (objective.ConditionCode is < 0 or > 10 or 5) {
				findings.Add(new(ScriptBlock.Objectives, i,
					$"Objective {i} asks condition {objective.ConditionCode}, which has no case: it answers whatever the record before it answered.",
					ScriptLintKind.UnknownCondition, objective.ConditionCode));
			}

			if (objective.SubjectKind is < 0 or > 3) {
				findings.Add(new(ScriptBlock.Objectives, i,
					$"Objective {i} has subject kind {objective.SubjectKind}, which has no case: it answers whatever the record before it answered.",
					ScriptLintKind.UnknownSubject, objective.SubjectKind));
			}
		}

		// DBSim_LoadScriptDat (00424308) reads at most 50 lines of mission.str.
		if (textLines > MissionTextEditing.LineCap) {
			findings.Add(new(ScriptBlock.ObjectiveLines, -1,
				$"mission.str has {textLines} lines; DBSIM reads only the first {MissionTextEditing.LineCap}.", ScriptLintKind.TextTooLong,
				textLines));
		}

		return findings;
	}

	private static void CheckGroup(ScriptDat script, int i, List<ScriptLintFinding> findings) {
		var group = script.Groups[i];
		Ref(findings, ScriptBlock.Groups, i, $"Group {i} point ref", group.PositionRef, script.Coordinates.Length, ScriptBlock.Points);
		Ref(findings, ScriptBlock.Groups, i, $"Group {i} heading ref", group.HeadingRef, script.Headings.Length, ScriptBlock.Headings);
		Ref(findings, ScriptBlock.Groups, i, $"Group {i} route ref", group.RouteRef, script.WaypointGroups.Length, ScriptBlock.Routes);
		Ref(findings, ScriptBlock.Groups, i, $"Group {i} action ref", group.DeploymentActionRef, script.Actions.Length, ScriptBlock.Actions);
		Refs(findings, ScriptBlock.Groups, i, $"Group {i} order refs", group.OrderRefs, script.Orders.Length, ScriptBlock.Orders);

		bool slotZeroOrder = group.OrderRefs.Length > 0 && group.OrderRefs[0] >= 0 && group.OrderRefs[0] < script.Orders.Length;

		// With no heading of its own, DBSim_SpawnMissionObjects (004253d8) takes the bearing of slot 0's
		// route and reads that order's +0x08 with no null test (004260f6).
		if (group.HeadingRef < 0 && !slotZeroOrder) {
			findings.Add(new(ScriptBlock.Groups, i,
				$"Group {i} has neither a heading nor a slot-0 order; DBSIM faults loading it.", ScriptLintKind.NoHeadingNoOrder));
		}

		// Record 0 is the player squad placeholder: DBSIM never reads its member list (it fills the
		// squad from data\player.mec instead), so whatever indexes it carries are inert.
		if (i == 0) {
			return;
		}

		(int rosterCount, ScriptBlock roster) = group.MemberKind switch {
			0 => (script.Mechs.Length, ScriptBlock.Mechs),
			1 => (script.Flyers.Length, ScriptBlock.Flyers),
			2 => (script.Bases.Length, ScriptBlock.Bases),
			_ => (-1, ScriptBlock.Groups)
		};

		if (rosterCount < 0) {
			findings.Add(new(ScriptBlock.Groups, i,
				$"Group {i} roster is {group.MemberKind}; only 0 (hercs), 1 (flyers) and 2 (bases) exist.", ScriptLintKind.UnknownRoster,
				group.MemberKind));
			return;
		}

		Refs(findings, ScriptBlock.Groups, i, $"Group {i} member slots", group.MemberRefs, rosterCount, roster);

		// A machine whose group order is null gets Mech_AiSelectBehaviour's default case, which installs
		// a null behaviour descriptor — docs/retail/simulation/ai-goals.md#a-group-with-no-order-at-all.
		if (group.MemberKind == 0 && !slotZeroOrder && group.MemberRefs.Any(member => member >= 0)) {
			findings.Add(new(ScriptBlock.Groups, i,
				$"Group {i} places hercs but has no slot-0 order; their AI installs a null behaviour and DBSIM faults.",
				ScriptLintKind.HercGroupNoOrder));
		}
	}

	// <label>'s ref <value> into <target>, which has <count> records: past the end, or below -1.
	private static void Ref(List<ScriptLintFinding> findings, ScriptBlock block, int index, string label,
			short value, int count, ScriptBlock? target) {
		string name = target is { } indexed ? BlockName(indexed) : "subjects";
		if (value >= count) {
			findings.Add(new(block, index, $"{label}: {value} is past the end of the {count} {name}.", ScriptLintKind.BrokenRef,
				value, target));
		} else if (value < -1) {
			findings.Add(new(block, index, $"{label}: {value} is not a valid index (-1 means unset).", ScriptLintKind.BrokenRef,
				value, target));
		}
	}

	private static void Refs(List<ScriptLintFinding> findings, ScriptBlock block, int index, string label,
			short[] values, int count, ScriptBlock? target) {
		foreach (short value in values) {
			Ref(findings, block, index, label, value, count, target);
		}
	}

	private static void Cap(List<ScriptLintFinding> findings, ScriptBlock block, int slots, int cap, string what) {
		if (slots > cap) {
			findings.Add(new(block, -1,
				$"{what} need {slots} slots; DBSIM holds {cap} and writes the rest over the globals that follow.",
				ScriptLintKind.RosterFull, slots));
		}
	}

	// What a block's records are called in a finding's message.
	private static string BlockName(ScriptBlock block) => block switch {
		ScriptBlock.Points => "points",
		ScriptBlock.Headings => "headings",
		ScriptBlock.Routes => "routes",
		ScriptBlock.TriggerAreas => "trigger areas",
		ScriptBlock.Actions => "actions",
		ScriptBlock.ActionTimers => "action timers",
		ScriptBlock.Mechs => "hercs",
		ScriptBlock.Flyers => "flyers",
		ScriptBlock.Bases => "bases",
		ScriptBlock.Orders => "orders",
		ScriptBlock.Groups => "groups",
		ScriptBlock.Objectives => "objectives",
		_ => "objective lines",
	};

	/// <summary>
	/// What a subject ref of this kind indexes and how many records it can index: 0 group, 1 herc, 2 flyer, 3 base, the
	/// numbering orders and objectives share. No block and 0 for a kind with none, so any ref is reported.
	/// </summary>
	private static (int Count, ScriptBlock? Block) SubjectCount(ScriptDat script, short kind) => kind switch {
		0 => (script.Groups.Length, ScriptBlock.Groups),
		1 => (script.Mechs.Length, ScriptBlock.Mechs),
		2 => (script.Flyers.Length, ScriptBlock.Flyers),
		3 => (script.Bases.Length, ScriptBlock.Bases),
		_ => (0, null)
	};

	/// <summary>
	/// What an action's target indexes: types 7/8/9/10 name a herc, flyer, base or group
	/// (docs/retail/simulation/mission-deployment.md#trigger-areas--actions_evaluatetriggers-00426b70).
	/// Null for any other type, whose target DBSIM zeroes, so it goes unchecked.
	/// </summary>
	private static (int Count, ScriptBlock Block)? TargetCount(ScriptDat script, short type) => type switch {
		7 => (script.Mechs.Length, ScriptBlock.Mechs),
		8 => (script.Flyers.Length, ScriptBlock.Flyers),
		9 => (script.Bases.Length, ScriptBlock.Bases),
		10 => (script.Groups.Length, ScriptBlock.Groups),
		_ => null
	};
}
