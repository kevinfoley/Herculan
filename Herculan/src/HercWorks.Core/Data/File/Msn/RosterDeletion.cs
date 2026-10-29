using HercWorks.Core.Io.Transform.Common;

namespace HercWorks.Core.Data.File.Msn;

/// <summary>The <c>.MSN</c> records that name a roster object or a group as their subject or target, by kind.</summary>
/// <param name="Objectives">Row-17 objectives whose subject it is.</param>
/// <param name="Actions">Row-10 actions whose target it is (types 7 to 10).</param>
/// <param name="Orders">Row-15 orders whose subject it is, templates included.</param>
public sealed record MsnNamers(IReadOnlyList<MsnRecordId> Objectives, IReadOnlyList<MsnRecordId> Actions,
		IReadOnlyList<MsnRecordId> Orders) {
	/// <summary>Whether any record names it.</summary>
	public bool Any => Objectives.Count > 0 || Actions.Count > 0 || Orders.Count > 0;
}

/// <summary>What deleting a roster object would take with it, and what refuses it (<see cref="RosterDeletion.Check"/>).</summary>
/// <param name="InSquad">
/// It is a member of the player's squad: a row-12 GUID that a record of group 0, the layout's first group (<see cref="SquadRoster.Squad(MissionLayout)"/>), names.
/// </param>
/// <param name="Namers">The objectives, actions and orders naming it.</param>
/// <param name="FiredActions">
/// The row-10 records of the actions it fires when engaged or defeated: those its own records name, and those of every
/// template it can pick.
/// </param>
/// <param name="MemberOf">The row-16 group GUIDs whose member slots name it under the group's roster.</param>
/// <param name="EmptiedGroups">
/// The groups of <paramref name="MemberOf"/> it is the last member of, in every record, which go with it, and what names
/// each.
/// </param>
public sealed record RosterDeleteCheck(bool InSquad, MsnNamers Namers, IReadOnlyList<MsnRecordId> FiredActions,
		IReadOnlyList<short> MemberOf, IReadOnlyList<(short Group, MsnNamers Namers)> EmptiedGroups) {
	/// <summary>
	/// Whether the mission names or needs it: an objective, action or order naming it or a group the delete would remove,
	/// or an action of its own it fires.
	/// </summary>
	public bool Needed => Namers.Any || FiredActions.Count > 0 || EmptiedGroups.Any(group => group.Namers.Any);

	/// <summary>Whether nothing refuses the delete.</summary>
	public bool Allowed => !InSquad && !Needed;
}

/// <summary>What deleting a whole group would take with it, and what refuses it (<see cref="RosterDeletion.CheckGroup"/>).</summary>
/// <param name="IsSquad">It is group 0, the player's squad, whose machines a practice load builds from its records.</param>
/// <param name="Namers">The objectives, actions and orders naming the group.</param>
/// <param name="Row">The roster row its members are records of, 12 to 14; 0 for a roster no row holds.</param>
/// <param name="Members">Its members no other group names, which go with it, and what deleting each on its own would meet.</param>
/// <param name="Kept">Its members another group names too, which stay, placed by that group.</param>
public sealed record GroupDeleteCheck(bool IsSquad, MsnNamers Namers, int Row,
		IReadOnlyList<(short Guid, RosterDeleteCheck Check)> Members, IReadOnlyList<short> Kept) {
	/// <summary>
	/// Whether nothing refuses the delete: it is not the squad, nothing names the group, and nothing names or needs a member
	/// that goes with it.
	/// </summary>
	public bool Allowed => !IsSquad && !Namers.Any
		&& Members.All(member => !member.Check.Namers.Any && member.Check.FiredActions.Count == 0);
}

/// <summary>
/// Deletes a roster object — a row-12 mech, row-13 flyer or row-14 structure — from a <see cref="MissionDocument"/> as
/// record edits: every record of its GUID is removed from its row, whatever its condition, and the GUID is cleared from
/// every row-16 member slot naming it under the group's roster, in every record of the group's GUID. A group left with
/// no member in any record loses all its row-16 records, and each order it names that no slot of any group names after it
/// loses its row-15 records. Each point a removed record named that no field names any more loses its row-6 records, as a
/// route's delete takes its points (<see cref="RouteEditing.RemoveLeftBehind"/>). Nothing is renumbered: <c>.MSN</c> refs name GUIDs and the
/// generation resolves every index again. The templates a deleted record picked from are GUID <c>-1</c>
/// records shared by every record picking them, and stay. Removing a record that picks from a random table moves every later
/// draw (docs/herculan/editor-design.md#what-moves-the-random-draws).
///
/// <para>A GUID with no record resolves to <c>-1</c> (<c>Msn_ResolveRow6Ref</c> (<c>00415b44</c>) and its siblings),
/// so the delete is refused while an objective, an action or an order names the object, or names a group the delete
/// would remove, and while the object fires an action of its own when engaged or defeated, which would then never
/// fire. Every record counts whatever its condition, since one that fails in the generation's state names the object
/// in another. The player's squad is refused too: a practice load builds the player's machine and squadmates from its
/// row-12 records (docs/retail/shell/main-menu.md#starting-a-practice-mission); <see cref="SquadRoster.Remove"/> removes a
/// squadmate.</para>
///
/// <para>A group can be deleted whole (<see cref="DeleteGroup"/>), its members with it but for those another group places
/// too, refused while anything names the group or names or needs a member that goes. A base's pad goes with its group, as
/// the group's own flag is what lays it.</para>
/// </summary>
public static class RosterDeletion {
	private const int ActionRow = 10;
	private const int MechRow = 12;
	private const int FlyerRow = 13;
	private const int StructureRow = 14;
	private const int OrderRow = 15;
	private const int GroupRow = 16;
	private const int ObjectiveRow = 17;

	/// <summary>What deleting row <paramref name="row"/>'s GUID <paramref name="guid"/> would take with it, and what refuses it.</summary>
	public static RosterDeleteCheck Check(MissionDocument document, int row, short guid) {
		var mission = document.Mission;
		var records = RosterRecords(mission, row, guid);
		if (records.Count == 0) {
			throw new InvalidOperationException($"Row {row} has no record with GUID {guid}.");
		}

		// The roster a group names (0 mechs, 1 flyers, 2 structures), the subject kind an order or objective names it
		// by (1 to 3), and the action type that targets it (7 to 9).
		short memberKind = (short)(row - MechRow);
		var namers = NamersOf((short)(row - 11), (short)(row - 5), guid, mission);

		var memberOf = new List<short>();
		var emptied = new List<(short, MsnNamers)>();
		var groups = mission.Groups ?? [];
		foreach (short group in groups.Select(record => record.GUID).Where(group => group != -1).Distinct()) {
			var ofGroup = groups.Where(record => record.GUID == group).ToList();
			if (KindOf(ofGroup) != memberKind || !ofGroup.Any(record => record.MemberRefs.Contains(guid))) {
				continue;
			}

			memberOf.Add(group);
			if (ofGroup.All(record => record.MemberRefs.All(member => member == -1 || member == guid))) {
				emptied.Add((group, NamersOf(0, 10, group, mission)));
			}
		}

		short squad = SquadRoster.Squad(document);
		bool inSquad = row == MechRow && squad != -1 && memberOf.Contains(squad);
		return new RosterDeleteCheck(inSquad, namers, FiredActions(mission, row, records), memberOf, emptied);
	}

	/// <summary>
	/// Deletes row <paramref name="row"/>'s GUID <paramref name="guid"/> as one <see cref="MissionDocument.Change"/>.
	/// Throws <see cref="InvalidOperationException"/> when <see cref="Check"/> refuses it.
	/// </summary>
	public static MsnEdit Delete(MissionDocument document, int row, short guid) {
		var check = Check(document, row, guid);
		if (!check.Allowed) {
			throw new InvalidOperationException($"Row {row}'s GUID {guid} is referenced or needed by the mission, or is in the player's squad.");
		}

		return document.Change(() => Take(document, row, guid, check));
	}

	/// <summary>
	/// What deleting row-16 group GUID <paramref name="group"/> whole would take with it, and what refuses it: each member no
	/// other group names goes with it, refused as its own delete is, but for the group it empties, which is this one; a member
	/// another group names stays. Every record counts whatever its condition.
	/// </summary>
	public static GroupDeleteCheck CheckGroup(MissionDocument document, short group) {
		var records = (document.Mission.Groups ?? []).Where(record => record.GUID == group).ToList();
		if (group == -1 || records.Count == 0) {
			throw new InvalidOperationException($"Row 16 has no record with GUID {group}.");
		}

		int row = KindOf(records) is >= 0 and <= 2 and var kind ? MechRow + kind : 0;
		var members = new List<(short, RosterDeleteCheck)>();
		var kept = new List<short>();
		if (row != 0) {
			foreach (short member in records.SelectMany(record => record.MemberRefs).Where(member => member != -1).Distinct()) {
				if (RosterRecords(document.Mission, row, member).Count == 0) {
					continue;
				}

				var check = Check(document, row, member);
				if (check.MemberOf.Any(other => other != group)) {
					kept.Add(member);
				} else {
					members.Add((member, check));
				}
			}
		}

		return new GroupDeleteCheck(SquadRoster.Squad(document) == group, GroupNamers(document, group), row, members, kept);
	}

	/// <summary>
	/// Deletes row-16 group GUID <paramref name="group"/> as one <see cref="MissionDocument.Change"/>: every record of each
	/// member that goes with it (<see cref="CheckGroup"/>), then the group as a delete of its last member removes it. Throws
	/// <see cref="InvalidOperationException"/> when <see cref="CheckGroup"/> refuses it.
	/// </summary>
	public static MsnEdit DeleteGroup(MissionDocument document, short group) {
		var check = CheckGroup(document, group);
		if (!check.Allowed) {
			throw new InvalidOperationException($"Group {group} is referenced or needed by the mission, or is the player's squad.");
		}

		return document.Change(() => {
			var points = new List<short>();
			foreach (var (member, _) in check.Members) {
				points.AddRange(PointsOf(document, check.Row, member));
				document.Remove(check.Row, member);
			}

			RemoveGroup(document, group);
			RouteEditing.RemoveLeftBehind(document, points);
		});
	}

	/// <summary>
	/// The record edits of a delete <paramref name="check"/> allowed: every record of the GUID out of its row, and the GUID
	/// out of every group naming it, a group it leaves empty removed, then each point its records named that nothing names
	/// any more, as a route's delete removes its points (<see cref="RouteEditing.RemoveLeftBehind"/>). Made inside the
	/// caller's <see cref="MissionDocument.Change"/>.
	/// </summary>
	internal static void Take(MissionDocument document, int row, short guid, RosterDeleteCheck check) {
		var points = PointsOf(document, row, guid);
		document.Remove(row, guid);
		foreach (short group in check.MemberOf) {
			if (check.EmptiedGroups.Any(emptied => emptied.Group == group)) {
				RemoveGroup(document, group);
			} else {
				document.ClearMember(group, guid);
			}
		}

		RouteEditing.RemoveLeftBehind(document, points);
	}

	// Removes every row-16 record of <group>, and the records of each order it names that no slot of any group names
	// after it, as GroupOrders.Remove takes an order no slot names: an order no group names is never run, and the route it
	// names could not be deleted while it stays. Then each point those records named that nothing names any more.
	internal static void RemoveGroup(MissionDocument document, short group) {
		var orders = (document.Mission.Groups ?? []).Where(record => record.GUID == group)
			.SelectMany(record => record.OrderRefs).Where(order => order != -1).Distinct().ToList();
		var points = PointsOf(document, GroupRow, group);
		document.Remove(GroupRow, group);
		foreach (short order in orders) {
			if (GroupOrders.SlotsNaming(document, order) == 0 && (document.Mission.Orders ?? []).Any(record => record.GUID == order)) {
				points.AddRange(PointsOf(document, OrderRow, order));
				document.Remove(OrderRow, order);
			}
		}

		RouteEditing.RemoveLeftBehind(document, points);
	}

	/// <summary>
	/// The row-6 GUIDs the records of row <paramref name="row"/>'s GUID <paramref name="guid"/> name as their point, whatever
	/// their condition: a roster record's or a group's <c>PositionRef</c>, an order's <c>PointRef</c> — the fields of those
	/// rows <see cref="MissionDocument.PointRefs"/> counts.
	/// </summary>
	internal static List<short> PointsOf(MissionDocument document, int row, short guid) {
		var mission = document.Mission;
		IEnumerable<short> points = row switch {
			MechRow => (mission.Mechs ?? []).Where(record => record.GUID == guid).Select(record => record.PositionRef),
			FlyerRow => (mission.Flyers ?? []).Where(record => record.GUID == guid).Select(record => record.PositionRef),
			StructureRow => (mission.Bases ?? []).Where(record => record.GUID == guid).Select(record => record.PositionRef),
			OrderRow => (mission.Orders ?? []).Where(record => record.GUID == guid).Select(record => record.PointRef),
			GroupRow => (mission.Groups ?? []).Where(record => record.GUID == guid).Select(record => record.PositionRef),
			_ => throw new ArgumentOutOfRangeException(nameof(row), row, "Rows 12 to 16 name a point by GUID."),
		};
		return guid == -1 ? [] : points.Where(point => point != -1).Distinct().ToList();
	}

	/// <summary>The objectives, actions (type 10) and orders naming row-16 group GUID <paramref name="group"/> as their subject or target.</summary>
	public static MsnNamers GroupNamers(MissionDocument document, short group) => NamersOf(0, 10, group, document.Mission);

	// What names <guid> as an objective's or order's subject of <subjectKind>, or as an action's target of <actionType>.
	private static MsnNamers NamersOf(short subjectKind, short actionType, short guid, MissionFile mission) {
		var objectives = new List<MsnRecordId>();
		var objectiveRow = mission.Objectives ?? [];
		for (int i = 0; i < objectiveRow.Length; i++) {
			if (objectiveRow[i] is { } objective && objective.SubjectKind == subjectKind && objective.SubjectRef == guid) {
				objectives.Add(new MsnRecordId(ObjectiveRow, i));
			}
		}

		var actions = new List<MsnRecordId>();
		var actionRow = mission.Actions ?? [];
		for (int i = 0; i < actionRow.Length; i++) {
			if (actionRow[i].Type == actionType && actionRow[i].TargetRef == guid) {
				actions.Add(new MsnRecordId(ActionRow, i));
			}
		}

		var orders = new List<MsnRecordId>();
		var orderRow = mission.Orders ?? [];
		for (int i = 0; i < orderRow.Length; i++) {
			if (orderRow[i].SubjectKind == subjectKind && orderRow[i].SubjectRef == guid) {
				orders.Add(new MsnRecordId(OrderRow, i));
			}
		}

		return new MsnNamers(objectives, actions, orders);
	}

	// A group's roster, as the generator's overlay supplies it: the last record of the GUID that sets it, else the first.
	private static short KindOf(List<MissionGroup164> records) =>
		records.LastOrDefault(record => record.MemberKind != -1)?.MemberKind ?? records[0].MemberKind;

	// The row-10 records of the actions the object's records, and every template they can pick, fire when engaged or
	// defeated: the first record of each such GUID. A GUID no record carries resolves to -1 and fires nothing.
	private static List<MsnRecordId> FiredActions(MissionFile mission, int row, List<RosterRecord> records) {
		var templates = records.Where(record => record.Key != -1)
			.SelectMany(record => Templates(mission, row, record.Key));
		var actionRow = mission.Actions ?? [];
		return records.Concat(templates)
			.SelectMany(record => new[] { record.Engaged, record.Defeated })
			.Where(action => action != -1)
			.Distinct()
			.Select(action => Array.FindIndex(actionRow, record => record.GUID == action))
			.Where(index => index >= 0)
			.Select(index => new MsnRecordId(ActionRow, index))
			.ToList();
	}

	// The roster records a record of random table key <key> can copy from: those whose condition is a type-2 child of a
	// type-3 condition keyed <key> (Msn_TemplateRow6 (00416120) and its siblings).
	private static IEnumerable<RosterRecord> Templates(MissionFile mission, int row, short key) {
		var conditions = mission.Conditions ?? [];
		var parents = conditions.Where(condition => condition.Type == 3 && condition.FlagIndexOrRangeLower == key)
			.Select(condition => condition.GUID).ToHashSet();
		var children = conditions.Where(condition => condition.Type == 2 && parents.Contains(condition.ConditionRef))
			.Select(condition => condition.GUID).ToHashSet();
		return RosterRow(mission, row).Where(record => children.Contains(record.Condition));
	}

	private static List<RosterRecord> RosterRecords(MissionFile mission, int row, short guid) =>
		guid == -1 ? [] : RosterRow(mission, row).Where(record => record.Guid == guid).ToList();

	private static IEnumerable<RosterRecord> RosterRow(MissionFile mission, int row) => row switch {
		MechRow => (mission.Mechs ?? []).Select(record => new RosterRecord(record.GUID, record.ConditionRef, record.RandomTableKey,
			record.EngagementActionRef, record.DefeatActionRef)),
		13 => (mission.Flyers ?? []).Select(record => new RosterRecord(record.GUID, record.ConditionRef, record.RandomTableKey,
			record.EngagementActionRef, record.DefeatActionRef)),
		StructureRow => (mission.Bases ?? []).Select(record => new RosterRecord(record.GUID, record.ConditionRef, record.RandomTableKey,
			record.EngagementActionRef, record.DefeatActionRef)),
		_ => throw new ArgumentOutOfRangeException(nameof(row), row, "Rows 12 to 14 are the rosters."),
	};

	// The words of a roster record the delete reads.
	private readonly record struct RosterRecord(short Guid, short Condition, short Key, short Engaged, short Defeated);
}
