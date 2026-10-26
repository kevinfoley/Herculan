using HercWorks.Core.Io.Transform.Common;

namespace HercWorks.Core.Data.File.Msn;

/// <summary>The <c>.MSN</c> records that name a roster object or a group as their subject or target, by kind.</summary>
/// <param name="Objectives">Row-17 objectives whose subject it is.</param>
/// <param name="Actions">Row-10 actions whose target it is (types 7 to 10).</param>
/// <param name="Orders">Row-15 orders whose subject it is, variant alternatives included.</param>
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
/// variant alternative it can draw.
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

/// <summary>
/// Deletes a roster object — a row-12 mech, row-13 flyer or row-14 structure — from a <see cref="MissionDocument"/> as
/// record edits: every record of its GUID is removed from its row, whatever its condition, and the GUID is cleared from
/// every row-16 member slot naming it under the group's roster, in every record of the group's GUID. A group left with
/// no member in any record loses all its row-16 records, and each order it names that no slot of any group names after it
/// loses its row-15 records. Nothing is renumbered: <c>.MSN</c> refs name GUIDs and the
/// generation resolves every index again. The variant alternatives a deleted record drew from are GUID <c>-1</c>
/// records shared by every record drawing them, and stay. Removing a record that draws a variant moves every later
/// draw (docs/herculan/handoff-editor-document.md#conditions-and-variants).
///
/// <para>A GUID with no record resolves to <c>-1</c> (<c>Msn_ResolveRow6Ref</c> (<c>00415b44</c>) and its siblings),
/// so the delete is refused while an objective, an action or an order names the object, or names a group the delete
/// would remove, and while the object fires an action of its own when engaged or defeated, which would then never
/// fire. Every record counts whatever its condition, since one that fails in the generation's state names the object
/// in another. The player's squad is refused too: a practice load builds the player's machine and squadmates from its
/// row-12 records (docs/retail/shell/main-menu.md#starting-a-practice-mission); <see cref="SquadRoster.Remove"/> removes a
/// squadmate.</para>
/// </summary>
public static class RosterDeletion {
	private const int ActionRow = 10;
	private const int MechRow = 12;
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
	/// The record edits of a delete <paramref name="check"/> allowed: every record of the GUID out of its row, and the GUID
	/// out of every group naming it, a group it leaves empty removed. Made inside the caller's <see cref="MissionDocument.Change"/>.
	/// </summary>
	internal static void Take(MissionDocument document, int row, short guid, RosterDeleteCheck check) {
		document.Remove(row, guid);
		foreach (short group in check.MemberOf) {
			if (check.EmptiedGroups.Any(emptied => emptied.Group == group)) {
				RemoveGroup(document, group);
			} else {
				document.ClearMember(group, guid);
			}
		}
	}

	// Removes every row-16 record of <group>, and the records of each order it names that no slot of any group names
	// after it, as GroupOrders.Remove takes an order no slot names: an order no group names is never run, and the route it
	// names could not be deleted while it stays.
	internal static void RemoveGroup(MissionDocument document, short group) {
		var orders = (document.Mission.Groups ?? []).Where(record => record.GUID == group)
			.SelectMany(record => record.OrderRefs).Where(order => order != -1).Distinct().ToList();
		document.Remove(GroupRow, group);
		foreach (short order in orders) {
			if (GroupOrders.SlotsNaming(document, order) == 0 && (document.Mission.Orders ?? []).Any(record => record.GUID == order)) {
				document.Remove(OrderRow, order);
			}
		}
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

	// The row-10 records of the actions the object's records, and every alternative they can draw, fire when engaged or
	// defeated: the first record of each such GUID. A GUID no record carries resolves to -1 and fires nothing.
	private static List<MsnRecordId> FiredActions(MissionFile mission, int row, List<RosterRecord> records) {
		var alternatives = records.Where(record => record.Variant != -1)
			.SelectMany(record => Alternatives(mission, row, record.Variant));
		var actionRow = mission.Actions ?? [];
		return records.Concat(alternatives)
			.SelectMany(record => new[] { record.Engaged, record.Defeated })
			.Where(action => action != -1)
			.Distinct()
			.Select(action => Array.FindIndex(actionRow, record => record.GUID == action))
			.Where(index => index >= 0)
			.Select(index => new MsnRecordId(ActionRow, index))
			.ToList();
	}

	// The roster records a record of variant key <variant> can copy from: those whose condition is a type-2 child of a
	// type-3 condition keyed <variant> (Msn_VariantSourceRow6 (00416120) and its siblings).
	private static IEnumerable<RosterRecord> Alternatives(MissionFile mission, int row, short variant) {
		var conditions = mission.Conditions ?? [];
		var parents = conditions.Where(condition => condition.Type == 3 && condition.FlagIndexOrRangeLower == variant)
			.Select(condition => condition.GUID).ToHashSet();
		var children = conditions.Where(condition => condition.Type == 2 && parents.Contains(condition.ConditionRef))
			.Select(condition => condition.GUID).ToHashSet();
		return RosterRow(mission, row).Where(record => children.Contains(record.Condition));
	}

	private static List<RosterRecord> RosterRecords(MissionFile mission, int row, short guid) =>
		guid == -1 ? [] : RosterRow(mission, row).Where(record => record.Guid == guid).ToList();

	private static IEnumerable<RosterRecord> RosterRow(MissionFile mission, int row) => row switch {
		MechRow => (mission.Mechs ?? []).Select(record => new RosterRecord(record.GUID, record.ConditionRef, record.VariantKey,
			record.EngagementActionRef, record.DefeatActionRef)),
		13 => (mission.Flyers ?? []).Select(record => new RosterRecord(record.GUID, record.ConditionRef, record.VariantKey,
			record.EngagementActionRef, record.DefeatActionRef)),
		StructureRow => (mission.Bases ?? []).Select(record => new RosterRecord(record.GUID, record.ConditionRef, record.VariantKey,
			record.EngagementActionRef, record.DefeatActionRef)),
		_ => throw new ArgumentOutOfRangeException(nameof(row), row, "Rows 12 to 14 are the rosters."),
	};

	// The words of a roster record the delete reads.
	private readonly record struct RosterRecord(short Guid, short Condition, short Variant, short Engaged, short Defeated);
}
