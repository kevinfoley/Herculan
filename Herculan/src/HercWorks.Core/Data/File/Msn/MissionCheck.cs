using HercWorks.Core.Data.File.Msn.Script;
using HercWorks.Core.Io.Transform.Common;

namespace HercWorks.Core.Data.File.Msn;

/// <summary>What kind of problem a <see cref="MissionCheckFinding"/> is, so a tool can phrase it in its own terms.</summary>
public enum MissionCheckKind {
	/// <summary>
	/// A ref to a GUID no record of the row it names carries, which the load resolves to none (<c>Msn_ResolveRow6Ref</c>
	/// (<c>00415b44</c>) and its siblings); a condition ref of that kind drops the record that carries it. The value is the GUID.
	/// </summary>
	MissingRecord,

	/// <inheritdoc cref="ScriptLintKind.NoTarget"/>
	NoTarget,

	/// <inheritdoc cref="ScriptLintKind.MessageNeverPosted"/>
	MessageNeverPosted,

	/// <summary>
	/// An action area ref behind its first slot that names no area, unset or a GUID no record carries, which DBSIM never tests
	/// (<see cref="ScriptLintKind.AreaAfterGap"/>); the value is that slot.
	/// </summary>
	AreaAfterGap,

	/// <summary>A roster past DBSIM's slot cap, counted as <see cref="RosterLimits.Slots"/> counts it; the value is the slots it needs.</summary>
	RosterFull,

	/// <summary>
	/// A group with neither a heading nor a first order, either unset or naming a GUID no record carries
	/// (<see cref="ScriptLintKind.NoHeadingNoOrder"/>).
	/// </summary>
	NoHeadingNoOrder,

	/// <inheritdoc cref="ScriptLintKind.UnknownRoster"/>
	UnknownRoster,

	/// <summary>
	/// A herc group with a member the mission has and no first order, unset or naming a GUID no record carries
	/// (<see cref="ScriptLintKind.HercGroupNoOrder"/>).
	/// </summary>
	HercGroupNoOrder,

	/// <inheritdoc cref="ScriptLintKind.UnknownCondition"/>
	UnknownCondition,

	/// <inheritdoc cref="ScriptLintKind.UnknownSubject"/>
	UnknownSubject,

	/// <summary>
	/// More <c>.ENG</c> line ids, every version's counted, than the <c>mission.str</c> lines DBSIM reads
	/// (<see cref="ScriptLintKind.TextTooLong"/>); the value is the ids.
	/// </summary>
	TextTooLong,

	/// <summary>
	/// A group outside the squad with a search/destroy order whose subject is on the group's own side, the squad counted
	/// human: the AI never targets its own side (docs/retail/simulation/ai-targeting.md#is-it-a-target-at-all--ai_istargetable-00411e80),
	/// and search/destroy installs only a target its order names
	/// (docs/retail/simulation/ai-navigation.md#searchdestroy-12--mech_behavioursearchdestroythink-0041d60c), so the group
	/// never fights it and takes no other target, a herc group until one shoots at it
	/// (docs/retail/simulation/ai-targeting.md#taking-fire--mech_aiontakingfire-0041f7b8-mech-vtable-0x50), a flyer group never
	/// (docs/retail/simulation/ai-flyers.md#the-thinks). The value is the order slot.
	/// </summary>
	OrderTargetsOwnSide,

	/// <summary>
	/// A group outside the squad whose every member the layout has is present only under one condition the group's own
	/// presence does not imply, so where it fails the group stands with no member, which DBSIM does not guard: a win or lose
	/// condition or an order asking about the group divides by its member count, and a trigger on a class of groups can fire
	/// on it (docs/retail/formats/script-dat.md#placement--the-actual-rule). The value is that condition's GUID.
	/// </summary>
	EmptyInSomeStates,

	/// <summary>
	/// An objective asking that a structure, or a Cybrid group holding a structure, be destroyed or immobilised (condition 1),
	/// where that structure's type is invulnerable (docs/retail/formats/bases-dat.md): the structure is never destroyed, and a
	/// Cybrid group is written off only once every member is (docs/retail/simulation/mission-objectives.md#what-each-condition-asks),
	/// so it never comes true. Checked only when the caller says which types are invulnerable. The value is the subject kind,
	/// 0 a group or 3 a structure.
	/// </summary>
	NeverWrittenOff,

	/// <summary>
	/// A group waiting on a drop-pod trigger (verb 2 or 3) with more than one member the layout has: the pod puts down only the
	/// group's leader and then lets the whole group in, so every other member goes live where the mission placed it, a
	/// position nothing else uses for a group arriving by pod (docs/retail/simulation/mission-deployment.md#the-drop-pod--meteor).
	/// The value is the member count.
	/// </summary>
	PodCarriesLeaderOnly,

	/// <summary>
	/// More positions, headings or trigger areas, counted as <see cref="SkipArrayLimits.Count"/> counts them, or more waypoints
	/// in one version of a route, than DBSIM's spawn pass holds (<see cref="ScriptLintKind.SkipArrayFull"/>). A row-6, 7 or 9
	/// finding is about the row as a whole; the value is the count.
	/// </summary>
	SkipArrayFull,

	/// <summary>
	/// More heads-down map markers, counted as <see cref="MapMarkerLimits.Count"/> counts them, than DBSIM builds
	/// (<see cref="ScriptLintKind.MapMarkersFull"/>). About the mission as a whole; the value is the count.
	/// </summary>
	MapMarkersFull,

	/// <summary>
	/// More objects, counted as <see cref="ObjectLimits.Count"/> counts them, than DBSIM's per-object tables hold
	/// (<see cref="ScriptLintKind.ObjectsFull"/>). About the mission as a whole; the value is the count.
	/// </summary>
	ObjectsFull,
}

/// <summary>One problem <see cref="MissionCheck"/> found, and the <c>.MSN</c> record it is about.</summary>
/// <param name="Kind">What kind of problem it is.</param>
/// <param name="Row">
/// The record's <c>.MSN</c> row, 1 to 17; <see cref="TextRow"/> for an <c>.ENG</c> line; <see cref="MissionRow"/> for the
/// mission as a whole. A <see cref="MissionCheckKind.RosterFull"/> is its roster's row, 12 to 14.
/// </param>
/// <param name="Key">
/// The record's GUID (rows 1 and 6 to 16), its row-17 index or the line's <c>.ENG</c> id; <c>-1</c> for row 2, a roster as a
/// whole and the mission.
/// </param>
/// <param name="Message">What is wrong, naming the record and the field, in <c>.MSN</c> terms.</param>
/// <param name="Value">The number the kind names (the GUID, the slot, the slots, the code or the line ids), or 0.</param>
/// <param name="RefersTo">For a <see cref="MissionCheckKind.MissingRecord"/>, the row the ref names; null for a subject of a kind with no row.</param>
/// <param name="InSomeVersions">
/// Whether only some versions of the mission have it: a record of the GUID under another condition than its base's, or an
/// option of a random table other than the one the layout shows.
/// </param>
public sealed record MissionCheckFinding(MissionCheckKind Kind, int Row, int Key, string Message, int Value = 0,
		int? RefersTo = null, bool InSomeVersions = false) {
	/// <summary>The <see cref="Row"/> of a finding about an <c>.ENG</c> line.</summary>
	public const int TextRow = 0;

	/// <summary>The <see cref="Row"/> of a finding about the mission as a whole.</summary>
	public const int MissionRow = -1;
}

/// <summary>
/// Checks a mission's <c>.MSN</c> and <c>.ENG</c>, every version at once, through its <see cref="MissionLayout"/>: refs to
/// records the mission does not have, records DBSIM never reads, shapes it faults on, rosters past its slots and text past
/// what it reads. Each finding names the <c>.MSN</c> record it is about (docs/herculan/handoff-editor-viewing.md#mission-check).
/// Advisory, as <see cref="ScriptDatLint"/> is; the DBSIM reasons for each kind are its (<see cref="ScriptLintKind"/>).
///
/// <para>Each thing of rows 6 to 16 is checked as its base (<see cref="LayoutEntry.Fields"/>), then as each record of its
/// GUID under another condition sets it over the base (<see cref="LayoutVariation"/>; the record whole for a row that
/// replaces), then with each option of a random table its records pick other than the one the layout shows copied in
/// (<see cref="LayoutPick"/>); a problem only those versions have is marked <see cref="MissionCheckFinding.InSomeVersions"/>.
/// A ref counts as missing when no record of the row it names carries the GUID at all; one whose records are present only
/// under other conditions is not checked.</para>
/// </summary>
public static class MissionCheck {
	private const int ConditionRow = 1;
	private const int SettingsRow = 2;
	private const int PointRow = 6;
	private const int HeadingRow = 7;
	private const int RouteRow = 8;
	private const int AreaRow = 9;
	private const int ActionRow = 10;
	private const int TimerRow = 11;
	private const int MechRow = 12;
	private const int FlyerRow = 13;
	private const int BaseRow = 14;
	private const int OrderRow = 15;
	private const int GroupRow = 16;
	private const int ObjectiveRow = 17;

	/// <summary>
	/// Runs every check over <paramref name="layout"/>, the layout of <paramref name="file"/>, whose records the options of its
	/// random tables are read from. <paramref name="isInvulnerable"/> answers which <c>BASES.DAT</c> types are invulnerable,
	/// for <see cref="MissionCheckKind.NeverWrittenOff"/>, which is not checked without it.
	/// </summary>
	public static List<MissionCheckFinding> Run(MissionLayout layout, MissionFile file, Func<int, bool>? isInvulnerable = null) {
		var check = new Checker(layout, file, isInvulnerable);
		for (int row = PointRow; row <= GroupRow; row++) {
			foreach (var entry in layout.Entries(row)) {
				check.Entry(entry);
			}
		}

		check.Groups();
		check.Objectives();
		check.Conditions();
		check.Header();
		check.Lines();
		check.Rosters();
		check.SkippedBlocks();
		check.MapMarkers();
		check.Objects();
		return check.Findings;
	}

	/// <summary>
	/// Whether <paramref name="objective"/> of <paramref name="layout"/> can never come true for an invulnerable structure
	/// (<see cref="MissionCheckKind.NeverWrittenOff"/>): it asks that its subject be destroyed or immobilised (condition 1), and
	/// the subject is a structure of a type <paramref name="isInvulnerable"/> answers true for, or a Cybrid structure group
	/// other than the squad holding one.
	/// </summary>
	public static bool NeverWrittenOff(MissionLayout layout, MissionObjective58 objective, Func<int, bool> isInvulnerable) {
		bool Invulnerable(short structure) =>
			layout.Entry<BaseRosterEntry62>(structure)?.Fields is { } record && isInvulnerable(record.TypeIndex);

		short subject = objective.SubjectRef;
		return objective.ConditionCode == WrittenOff && objective.SubjectKind switch {
			3 => Invulnerable(subject),
			0 => subject != SquadRoster.Squad(layout)
				&& layout.Entry<MissionGroup164>(subject)?.Fields is { MemberKind: 2, Side: CybridSide } group
				&& group.MemberRefs.Any(member => member != -1 && Invulnerable(member)),
			_ => false,
		};
	}

	/// <summary>
	/// The condition row-16 group <paramref name="group"/> of <paramref name="layout"/> stands with no member without
	/// (<see cref="MissionCheckKind.EmptyInSomeStates"/>): the one condition every member the layout has carries, when the
	/// group's own conditions, and what they require through row 1's requirement refs, do not include it; null when there is
	/// none, for the squad, and for a group with no member.
	/// </summary>
	public static short? EmptyUnless(MissionLayout layout, short group) {
		if (group == SquadRoster.Squad(layout) || layout.Entry(GroupRow, group) is not { Fields: MissionGroup164 fields } entry) {
			return null;
		}

		int? memberRow = fields.MemberKind switch { 0 => MechRow, 1 => FlyerRow, 2 => BaseRow, _ => null };
		if (memberRow is not { } row) {
			return null;
		}

		var codec = new MissionFileTransformer();
		var conditions = fields.MemberRefs.Where(member => member != -1).Select(member => layout.Entry(row, member))
			.OfType<LayoutEntry>().Select(member => codec.Words(member.Fields)[MergeRules.ConditionWord]).Distinct().ToList();
		if (conditions is not [var condition] || condition == -1) {
			return null;
		}

		var implied = new HashSet<short>();
		foreach (short start in entry.Presence.Conditions) {
			for (short at = start; at != -1 && implied.Add(at);) {
				at = layout.Condition(at)?.Fields.ConditionRef ?? -1;
			}
		}

		return implied.Contains(condition) ? null : condition;
	}

	// Condition 1: the subject destroyed or immobilised, a group written off.
	private const short WrittenOff = 1;

	/// <summary>The subject an order's or objective's subject kind names: 0 a group, 1 a herc, 2 a flyer, 3 a structure; null for any other.</summary>
	private static int? SubjectRow(short kind) => kind switch {
		0 => GroupRow,
		1 => MechRow,
		2 => FlyerRow,
		3 => BaseRow,
		_ => null,
	};

	// Group_IsWrittenOff (00413920)'s side whose groups are written off only at tier 4, every member out
	// (docs/retail/simulation/mission-objectives.md#what-each-condition-asks); the side the squad fights on.
	private const short CybridSide = 1;
	private const short HumanSide = 0;

	// Search/destroy, the verb that fights only its subject (docs/retail/simulation/ai-goals.md).
	private const short SearchDestroy = 0;

	// The two drop-pod arrivals of an action's verb (docs/retail/simulation/mission-deployment.md#arrival--group_deploymentcheck-004236c4).
	private const short PodWide = 2;
	private const short PodNarrow = 3;

	private sealed class Checker(MissionLayout layout, MissionFile file, Func<int, bool>? isInvulnerable) {
		private readonly MissionFileTransformer _codec = new();
		private readonly HashSet<(MissionCheckKind, int, int, string)> _seen = new();
		private readonly short _squad = SquadRoster.Squad(layout);

		public List<MissionCheckFinding> Findings { get; } = new();

		// ---- Rows 6 to 16 ----------------------------------------------------------------------------

		public void Entry(LayoutEntry entry) {
			int row = entry.Row;
			short guid = entry.Guid;
			Condition(row, guid, _codec.Words(entry.Fields)[MergeRules.ConditionWord], "ConditionRef", false);
			Record(row, guid, entry.Fields, false);
			foreach (var pick in entry.Presence.Picks) {
				Options(row, guid, entry.Fields, pick);
			}

			foreach (var variation in entry.Variations) {
				Condition(row, guid, variation.Condition, $"record {variation.Record.Index}'s ConditionRef", true);
				var version = MergeRules.Replaces(row) ? variation.Fields : Overlaid(row, entry.Fields, variation.Fields, variation.Words);
				Record(row, guid, version, true);
				if (variation.Pick != null) {
					Options(row, guid, version, variation.Pick);
				}
			}
		}

		// Each option of <pick> other than the one shown, copied into <under> as the load copies a picked option's template.
		private void Options(int row, short guid, MapObject under, LayoutPick pick) {
			foreach (var option in pick.Options.Skip(1)) {
				if (option.Record is { } id && MsnRows.Get(file, id.Row).GetValue(id.Index) is MapObject record) {
					Record(row, guid, WithOption(row, under, record), true);
				}
			}
		}

		private MapObject Overlaid(int row, MapObject under, MapObject over, IReadOnlyList<int> words) {
			var merged = _codec.Words(under);
			var top = _codec.Words(over);
			foreach (int word in words) {
				merged[word] = top[word];
			}

			return _codec.Record(row, merged);
		}

		private MapObject WithOption(int row, MapObject under, MapObject option) {
			var words = _codec.Words(under);
			var from = _codec.Words(option);
			foreach (var span in MergeRules.TemplateCopy(row)) {
				if (row == RouteRow) {
					words = [.. words[..span.First], .. from[span.First..]];
				} else {
					Array.Copy(from, span.First, words, span.First, span.Count);
				}
			}

			return _codec.Record(row, words);
		}

		// One version of row <row>'s GUID <guid>: its fields as that version has them.
		private void Record(int row, short guid, MapObject fields, bool some) {
			switch (fields) {
				case WaypointGroup route:
					for (int i = 0; i < route.Waypoints.Length; i++) {
						Ref(row, guid, $"Waypoints[{i}]", PointRow, route.Waypoints[i], some);
					}

					int waypointCap = SkipArrayLimits.Cap(RouteRow);
					if (route.Waypoints.Length > waypointCap) {
						Add(MissionCheckKind.SkipArrayFull, RouteRow, guid,
							$"Waypoints holds {route.Waypoints.Length}; DBSIM's spawn pass reads {waypointCap}.", route.Waypoints.Length,
							some: some);
					}

					break;
				case TriggerArea12 area:
					Ref(row, guid, "PointRef", PointRow, area.PointRef, some);
					if (area.Shape == 0) {
						Ref(row, guid, "SecondPointOrRadius", PointRow, area.SecondPointOrRadius, some);
					}

					break;
				case MissionAction82 action:
					Action(guid, action, some);
					break;
				case ActionTimer30 timer:
					Ref(row, guid, "PrimaryActionRef", ActionRow, timer.PrimaryActionRef, some);
					for (int i = 0; i < timer.SequenceRefs.Length; i++) {
						Ref(row, guid, $"SequenceRefs[{i}]", ActionRow, timer.SequenceRefs[i], some);
					}

					break;
				case MechRosterEntry144 herc:
					Roster(row, guid, herc.PositionRef, herc.HeadingRef, herc.EngagementActionRef, herc.DefeatActionRef, some);
					break;
				case FlyerRosterEntry102 flyer:
					Roster(row, guid, flyer.PositionRef, flyer.HeadingRef, flyer.EngagementActionRef, flyer.DefeatActionRef, some);
					break;
				case BaseRosterEntry62 structure:
					Roster(row, guid, structure.PositionRef, structure.HeadingRef, structure.EngagementActionRef,
						structure.DefeatActionRef, some);
					break;
				case MissionOrder22 order:
					Ref(row, guid, "RouteRef", RouteRow, order.RouteRef, some);
					Ref(row, guid, "ActionRef", ActionRow, order.ActionRef, some);
					if (order.SubjectKind != -1) {
						Subject(row, guid, order.SubjectKind, order.SubjectRef, some);
					}

					break;
				case MissionGroup164 group:
					Group(guid, group, some);
					break;
			}
		}

		private void Action(short guid, MissionAction82 action, bool some) {
			for (int i = 0; i < action.AreaRefs.Length; i++) {
				Ref(ActionRow, guid, $"AreaRefs[{i}]", AreaRow, action.AreaRefs[i], some);
			}

			if (ActionEditing.TargetBlock(action.Type) is { } block) {
				if (action.TargetRef == -1) {
					Add(MissionCheckKind.NoTarget, ActionRow, guid, $"type {action.Type} names no target.", action.Type, some: some);
				} else {
					Ref(ActionRow, guid, "TargetRef", block + 5, action.TargetRef, some);
				}
			}

			bool writes = Enumerable.Range(0, action.CounterPairs.Length / 2).Any(pair => action.CounterPairs[pair * 2] >= 0);
			if (action.MessageId > 0 && !writes) {
				Add(MissionCheckKind.MessageNeverPosted, ActionRow, guid, "MessageId is set and no CounterPairs counter is.", some: some);
			}

			int gap = Array.FindIndex(action.AreaRefs, area => !Has(AreaRow, area));
			if (gap >= 0 && action.AreaRefs.Skip(gap).Any(area => Has(AreaRow, area))) {
				Add(MissionCheckKind.AreaAfterGap, ActionRow, guid, $"AreaRefs names an area after slot {gap}, which names none.", gap,
					some: some);
			}
		}

		private void Roster(int row, short guid, short point, short heading, short engaged, short defeated, bool some) {
			Ref(row, guid, "PositionRef", PointRow, point, some);
			Ref(row, guid, "HeadingRef", HeadingRow, heading, some);
			Ref(row, guid, "EngagementActionRef", ActionRow, engaged, some);
			Ref(row, guid, "DefeatActionRef", ActionRow, defeated, some);
		}

		private void Group(short guid, MissionGroup164 group, bool some) {
			Ref(GroupRow, guid, "PositionRef", PointRow, group.PositionRef, some);
			Ref(GroupRow, guid, "HeadingRef", HeadingRow, group.HeadingRef, some);
			Ref(GroupRow, guid, "RouteRef", RouteRow, group.RouteRef, some);
			Ref(GroupRow, guid, "DeploymentActionRef", ActionRow, group.DeploymentActionRef, some);
			for (int i = 0; i < group.OrderRefs.Length; i++) {
				Ref(GroupRow, guid, $"OrderRefs[{i}]", OrderRow, group.OrderRefs[i], some);
			}

			int? members = group.MemberKind switch { 0 => MechRow, 1 => FlyerRow, 2 => BaseRow, _ => null };
			if (members is { } memberRow) {
				for (int i = 0; i < group.MemberRefs.Length; i++) {
					Ref(GroupRow, guid, $"MemberRefs[{i}]", memberRow, group.MemberRefs[i], some);
				}
			}

			bool firstOrder = group.OrderRefs.Length > 0 && Has(OrderRow, group.OrderRefs[0]);
			if (!Has(HeadingRow, group.HeadingRef) && !firstOrder) {
				Add(MissionCheckKind.NoHeadingNoOrder, GroupRow, guid, "neither HeadingRef nor OrderRefs[0] names a record.", some: some);
			}

			// Group 0's member list and roster stand for the squad, which DBSIM builds from player.mec instead (ScriptDatLint's
			// group-0 rule); the shell's squad build reads its member refs, so they are checked above.
			if (guid == _squad) {
				return;
			}

			if (members == null) {
				Add(MissionCheckKind.UnknownRoster, GroupRow, guid, $"MemberKind {group.MemberKind} is none of 0, 1 and 2.",
					group.MemberKind, some: some);
			} else if (group.MemberKind == 0 && !firstOrder && group.MemberRefs.Any(member => Has(MechRow, member))) {
				Add(MissionCheckKind.HercGroupNoOrder, GroupRow, guid, "it places HERCs, and OrderRefs[0] names no record.", some: some);
			}
		}

		// ---- Groups, as their bases have them ----------------------------------------------------------

		public void Groups() {
			foreach (var entry in layout.Entries(GroupRow)) {
				if (entry.Guid != _squad && entry.Fields is MissionGroup164 group) {
					OwnSideOrders(entry.Guid, group);
					Emptied(entry);
					PodGroup(entry.Guid, group);
				}
			}
		}

		// <group> when it waits on a drop-pod trigger and places more than one member.
		private void PodGroup(short guid, MissionGroup164 group) {
			if (layout.Entry<MissionAction82>(group.DeploymentActionRef)?.Fields is not { Verb: PodWide or PodNarrow } action
					|| group.MemberKind switch { 0 => MechRow, 1 => FlyerRow, 2 => BaseRow, _ => (int?)null } is not { } row) {
				return;
			}

			int members = group.MemberRefs.Count(member => Has(row, member));
			if (members > 1) {
				Add(MissionCheckKind.PodCarriesLeaderOnly, GroupRow, guid,
					$"DeploymentActionRef {group.DeploymentActionRef} arrives by drop pod (Verb {action.Verb}), which puts down the "
					+ $"leader alone, and MemberRefs names {members} members.", members);
			}
		}

		// Each search/destroy order of <group> whose subject is on its side.
		private void OwnSideOrders(short guid, MissionGroup164 group) {
			for (int slot = 0; slot < group.OrderRefs.Length; slot++) {
				if (layout.Entry(OrderRow, group.OrderRefs[slot])?.Fields is MissionOrder22 { Verb: SearchDestroy } order
						&& SideOf(order.SubjectKind, order.SubjectRef) == Normal(group.Side)) {
					Add(MissionCheckKind.OrderTargetsOwnSide, GroupRow, guid,
						$"OrderRefs[{slot}] is search/destroy on SubjectRef {order.SubjectRef}, of its own Side {group.Side}.", slot);
				}
			}
		}

		// The side of an order's subject of <kind>: a group's, or the group's placing a herc, flyer or structure, the squad
		// human; null for none.
		private short? SideOf(short kind, short subject) {
			short group = kind == 0 ? subject
				: SubjectRow(kind) is { } row && layout.Entry(row, subject) is { } entry ? entry.Presence.Group
				: (short)-1;
			return group == -1 ? null
				: group == _squad ? HumanSide
				: layout.Entry<MissionGroup164>(group)?.Fields is { } fields ? Normal(fields.Side)
				: null;
		}

		// A side field as a group's team is shown: Cybrid for 1, human for any other.
		private static short Normal(short side) => side == CybridSide ? CybridSide : HumanSide;

		// <entry> when every member the layout has carries one condition its own presence does not imply.
		private void Emptied(LayoutEntry entry) {
			if (EmptyUnless(layout, entry.Guid) is { } condition) {
				Add(MissionCheckKind.EmptyInSomeStates, GroupRow, entry.Guid,
					$"every member's ConditionRef is {condition}, which its own presence does not require.", condition);
			}
		}

		private void Subject(int row, int key, short kind, short subject, bool some) {
			if (SubjectRow(kind) is { } subjects) {
				Ref(row, key, "SubjectRef", subjects, subject, some);
			} else if (subject != -1) {
				Add(MissionCheckKind.MissingRecord, row, key, $"SubjectRef {subject} is of SubjectKind {kind}, which names no row.",
					subject, null, some);
			}
		}

		// ---- Row 17, row 1, row 2 and the .ENG ------------------------------------------------------

		public void Objectives() {
			foreach (var objective in layout.Objectives) {
				var fields = objective.Fields;
				int index = objective.Index;
				Condition(ObjectiveRow, index, fields.ConditionRef, "ConditionRef", false);
				Ref(ObjectiveRow, index, "RouteRef", RouteRow, fields.RouteRef, false);
				if (fields.ConditionCode is < 0 or > 10 or 5) {
					Add(MissionCheckKind.UnknownCondition, ObjectiveRow, index, $"ConditionCode {fields.ConditionCode} has no case.",
						fields.ConditionCode);
				}

				if (fields.SubjectKind is < 0 or > 3) {
					Add(MissionCheckKind.UnknownSubject, ObjectiveRow, index, $"SubjectKind {fields.SubjectKind} has no case.",
						fields.SubjectKind);
				} else {
					Subject(ObjectiveRow, index, fields.SubjectKind, fields.SubjectRef, false);
				}

				if (isInvulnerable != null && NeverWrittenOff(layout, fields, isInvulnerable)) {
					Add(MissionCheckKind.NeverWrittenOff, ObjectiveRow, index,
						$"ConditionCode 1 asks of SubjectRef {fields.SubjectRef}, which is or holds an invulnerable structure.",
						fields.SubjectKind);
				}
			}
		}

		public void Conditions() {
			foreach (var condition in layout.Conditions) {
				Condition(ConditionRow, condition.Guid, condition.Fields.ConditionRef, "ConditionRef", false);
			}
		}

		public void Header() {
			if (layout.Header is not { } header) {
				return;
			}

			foreach (short condition in header.Presence.Conditions) {
				Condition(SettingsRow, -1, condition, $"record {header.Base[0].Index}'s condition", false);
			}

			foreach (var variation in header.Variations) {
				Condition(SettingsRow, -1, variation.Condition, $"record {variation.Record.Index}'s condition", true);
			}
		}

		public void Lines() {
			foreach (var line in layout.Lines) {
				foreach (short condition in line.Presence.Conditions) {
					Condition(MissionCheckFinding.TextRow, line.Id, condition, $"record {line.Records[0]}'s ConditionRef", false);
				}

				foreach (var variation in line.Variations) {
					Condition(MissionCheckFinding.TextRow, line.Id, variation.Condition, $"record {variation.Record}'s ConditionRef", true);
				}
			}

			int ids = layout.Lines.Count;
			if (ids > MissionTextEditing.LineCap) {
				Add(MissionCheckKind.TextTooLong, MissionCheckFinding.MissionRow, -1,
					$"the .ENG has {ids} line ids; DBSIM reads only the first {MissionTextEditing.LineCap} lines.", ids);
			}
		}

		public void Rosters() {
			foreach (int row in new[] { MechRow, FlyerRow, BaseRow }) {
				int slots = RosterLimits.Slots(layout, row);
				int cap = RosterLimits.Cap(row);
				if (slots > cap) {
					string squad = row == MechRow ? ", one per squad position included" : "";
					Add(MissionCheckKind.RosterFull, row, -1, $"its GUIDs need {slots} slots{squad}; DBSIM holds {cap}.", slots);
				}
			}
		}

		public void SkippedBlocks() {
			foreach (int row in new[] { PointRow, HeadingRow, AreaRow }) {
				int count = SkipArrayLimits.Count(layout, row);
				int cap = SkipArrayLimits.Cap(row);
				if (count > cap) {
					Add(MissionCheckKind.SkipArrayFull, row, -1, $"its GUIDs number {count}; DBSIM's spawn pass reads {cap}.", count);
				}
			}
		}

		public void MapMarkers() {
			int count = MapMarkerLimits.Count(layout);
			if (count > MapMarkerLimits.Cap) {
				Add(MissionCheckKind.MapMarkersFull, MissionCheckFinding.MissionRow, -1,
					$"it needs {count} heads-down map markers; DBSIM builds {MapMarkerLimits.Cap}.", count);
			}
		}

		public void Objects() {
			int count = ObjectLimits.Count(layout);
			if (count > ObjectLimits.Cap) {
				Add(MissionCheckKind.ObjectsFull, MissionCheckFinding.MissionRow, -1,
					$"it puts {count} objects in the simulation; DBSIM's per-object tables hold {ObjectLimits.Cap}.", count);
			}
		}

		// ---- Findings --------------------------------------------------------------------------------

		private bool Has(int row, short guid) => guid != -1 && layout.Entry(row, guid) != null;

		private void Ref(int row, int key, string field, int target, short guid, bool some) {
			if (guid != -1 && layout.Entry(target, guid) == null) {
				Add(MissionCheckKind.MissingRecord, row, key, $"{field} references row-{target} GUID {guid}, which no record carries.",
					guid, target, some);
			}
		}

		private void Condition(int row, int key, short condition, string field, bool some) {
			if (condition != -1 && layout.Condition(condition) == null) {
				Add(MissionCheckKind.MissingRecord, row, key, $"{field} references row-1 GUID {condition}, which no record carries.",
					condition, ConditionRow, some);
			}
		}

		// A finding, named by its record; one already found, in the base or another version, is not added again.
		private void Add(MissionCheckKind kind, int row, int key, string what, int value = 0, int? refersTo = null, bool some = false) {
			string message = $"{Label(row, key)}: {what}";
			if (_seen.Add((kind, row, key, message))) {
				Findings.Add(new MissionCheckFinding(kind, row, key, message, value, refersTo, some));
			}
		}

		private static string Label(int row, int key) => row switch {
			MissionCheckFinding.MissionRow => "The mission",
			MissionCheckFinding.TextRow => $".ENG id {key}",
			SettingsRow => "Row 2",
			ObjectiveRow => $"Row 17 record {key}",
			_ when key == -1 => $"Row {row}",
			_ => $"Row {row} GUID {key}",
		};
	}
}
