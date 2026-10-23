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
/// option of a random choice other than the one the layout shows.
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
/// replaces), then with each option of a random choice its records draw other than the one the layout shows copied in
/// (<see cref="LayoutDraw"/>); a problem only those versions have is marked <see cref="MissionCheckFinding.InSomeVersions"/>.
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

	/// <summary>Runs every check over <paramref name="layout"/>, the layout of <paramref name="file"/>, whose records the options of its random choices are read from.</summary>
	public static List<MissionCheckFinding> Run(MissionLayout layout, MissionFile file) {
		var check = new Checker(layout, file);
		for (int row = PointRow; row <= GroupRow; row++) {
			foreach (var entry in layout.Entries(row)) {
				check.Entry(entry);
			}
		}

		check.Objectives();
		check.Conditions();
		check.Header();
		check.Lines();
		check.Rosters();
		return check.Findings;
	}

	/// <summary>The subject an order's or objective's subject kind names: 0 a group, 1 a herc, 2 a flyer, 3 a structure; null for any other.</summary>
	private static int? SubjectRow(short kind) => kind switch {
		0 => GroupRow,
		1 => MechRow,
		2 => FlyerRow,
		3 => BaseRow,
		_ => null,
	};

	private sealed class Checker(MissionLayout layout, MissionFile file) {
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
			foreach (var draw in entry.Presence.Draws) {
				Options(row, guid, entry.Fields, draw);
			}

			foreach (var variation in entry.Variations) {
				Condition(row, guid, variation.Condition, $"record {variation.Record.Index}'s ConditionRef", true);
				var version = MergeRules.Replaces(row) ? variation.Fields : Overlaid(row, entry.Fields, variation.Fields, variation.Words);
				Record(row, guid, version, true);
				if (variation.Draw != null) {
					Options(row, guid, version, variation.Draw);
				}
			}
		}

		// Each option of <draw> other than the one shown, copied into <under> as the load copies a drawn option.
		private void Options(int row, short guid, MapObject under, LayoutDraw draw) {
			foreach (var option in draw.Options.Skip(1)) {
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
			foreach (var span in MergeRules.VariantCopy(row)) {
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
				Add(MissionCheckKind.HercGroupNoOrder, GroupRow, guid, "it places hercs, and OrderRefs[0] names no record.", some: some);
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
