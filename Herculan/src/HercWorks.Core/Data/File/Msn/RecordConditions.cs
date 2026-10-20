using HercWorks.Core.Io.Transform.Common;

namespace HercWorks.Core.Data.File.Msn;

/// <summary>
/// A record whose condition a view sets (<see cref="RecordConditions"/>): every record of a row-12 to 14 roster object or
/// row-16 group GUID (<see cref="Of"/>); the order in a group's slot (<see cref="OrderSlot"/>), every record of its
/// row-15 GUID; or a row-17 objective record, which has no GUID (<see cref="Objective"/>).
/// </summary>
public sealed record PresenceTarget {
	private const int OrderRow = 15;
	private const int ObjectiveRow = 17;

	private PresenceTarget(int row, short guid, short group, int slot, int index) {
		Row = row;
		Guid = guid;
		Group = group;
		Slot = slot;
		Index = index;
	}

	/// <summary>The <c>.MSN</c> row: 12 to 16 or 17.</summary>
	public int Row { get; }

	/// <summary>The GUID for rows 12 to 14 and 16; <c>-1</c> for an order slot and an objective.</summary>
	public short Guid { get; }

	/// <summary>An order slot's row-16 group GUID, else <c>-1</c>.</summary>
	public short Group { get; }

	/// <summary>An order slot's slot, else <c>-1</c>.</summary>
	public int Slot { get; }

	/// <summary>An objective's index in row 17, else <c>-1</c>.</summary>
	public int Index { get; }

	/// <summary>Every record of row <paramref name="row"/>'s GUID <paramref name="guid"/>: row 12, 13, 14 or 16.</summary>
	public static PresenceTarget Of(int row, short guid) => row is (>= 12 and <= 14) or 16
		? new PresenceTarget(row, guid, -1, -1, -1)
		: throw new ArgumentOutOfRangeException(nameof(row), row, "Rows 12 to 14 and 16 are the GUID rows a presence is set for.");

	/// <summary>The order in row-16 group <paramref name="group"/>'s slot <paramref name="slot"/>.</summary>
	public static PresenceTarget OrderSlot(short group, int slot) => new(OrderRow, -1, group, slot, -1);

	/// <summary>Row-17 record <paramref name="index"/>.</summary>
	public static PresenceTarget Objective(int index) => new(ObjectiveRow, -1, -1, -1, index);
}

/// <summary>One record of a <see cref="PresenceTarget"/> and the row-1 GUID its condition field references, <c>-1</c> for none.</summary>
public readonly record struct PresenceRecord(MsnRecordId Record, short Condition);

/// <summary>
/// Sets which campaign states a record is present in: the condition field (<c>0x02</c>, row 17's <c>0x00</c>) of a roster
/// object, an order, a group or an objective, to a flag test, a range over a draw, or nothing. How the load gates a
/// record on its condition is docs/retail/formats/msn-mission-file.md#the-conditions--row-1, and what the editor offers
/// docs/herculan/handoff-editor-conditions.md#present-when.
///
/// <list type="bullet">
/// <item>A GUID can be carried by several records, a base and the overlays or replacements that apply in other states
/// (docs/retail/formats/msn-mission-file.md#repeated-guids). When every record carries the same condition, the set writes
/// all of them (<see cref="MissionDocument.RewriteEvery{T}"/>). When they differ the set is refused
/// (<see cref="WhyFixed"/>): writing one alone could leave another creating the slot, every field the first supplied unset,
/// in a state where the first is absent. GUID <c>-1</c> records, the variant alternatives, are other records and are never
/// written. A variant-keyed record's condition is its own word: rows 12 to 15 copy from <c>0x08</c> on
/// (docs/retail/formats/msn-mission-file.md#variants), so the record drawing the variant is the one written. Whether it
/// survives decides whether it draws, so a set on one moves every later draw in the states it changes.</item>
/// <item>An order another group slot runs too is first given to the slot as its own copy (<see cref="GroupOrders"/>), as
/// every order edit is.</item>
/// <item>A row-17 record has no GUID and is written itself (<see cref="MissionDocument.Rewrite{T}"/>).</item>
/// <item>A group's first order is refused where the group cannot be left without one (<see cref="GroupOrders.WhyNeedsFirstOrder"/>):
/// where it is absent, slot 0 holds none.</item>
/// <item>The player's squad, group 0, its orders and the hercs its member slots reference, is refused: the squad build reads group 0's
/// members (docs/retail/shell/main-menu.md#starting-a-practice-mission), and the editor leaves their presence alone, as it
/// leaves their wiring (the editor's choice).</item>
/// <item>The conditions offered are flag tests and ranges over a draw (<see cref="Offered"/>). A draw holds whenever it is
/// made, and a variant table and the ranges over it are what variant keys draw by, which the editor does not author
/// (<see cref="ConditionEditing.InVariantTable"/>).</item>
/// </list>
/// </summary>
public static class RecordConditions {
	private const int OrderRow = 15;
	private const int GroupRow = 16;
	private const int ObjectiveRow = 17;
	private const int SquadBlock = 11;

	/// <summary>The condition field's value for a record present in every state.</summary>
	public const short Always = -1;

	/// <summary>
	/// The records of <paramref name="target"/>, in file order, each with its condition: every record of its GUID, or the
	/// objective record itself. Empty for a target the file does not have.
	/// </summary>
	public static IReadOnlyList<PresenceRecord> Records(MissionDocument document, PresenceTarget target) {
		if (target.Row == ObjectiveRow) {
			var objectives = document.Mission.Objectives ?? [];
			return target.Index >= 0 && target.Index < objectives.Length && objectives[target.Index] is { } objective
				? [new PresenceRecord(new MsnRecordId(ObjectiveRow, target.Index), objective.ConditionRef)]
				: [];
		}

		short guid = GuidOf(document, target);
		var records = new List<PresenceRecord>();
		if (guid == -1) {
			return records;
		}

		var row = MsnRows.Get(document.Mission, target.Row);
		for (int i = 0; i < row.Length; i++) {
			if (row.GetValue(i) is MapObject record && record.GUID == guid) {
				records.Add(new PresenceRecord(new MsnRecordId(target.Row, i), ConditionOf(record)));
			}
		}

		return records;
	}

	/// <summary>The condition every record of <paramref name="target"/> carries, or null when they differ or there are none.</summary>
	public static short? Shared(MissionDocument document, PresenceTarget target) {
		var records = Records(document, target);
		return records.Count > 0 && records.All(record => record.Condition == records[0].Condition) ? records[0].Condition : null;
	}

	/// <summary>
	/// The row-12 to 16 GUID <paramref name="target"/> addresses now: its own, or the order in its slot; <c>-1</c> for an
	/// objective, an empty slot or a group no record carries.
	/// </summary>
	public static short GuidOf(MissionDocument document, PresenceTarget target) =>
		target.Row == OrderRow
			? target.Slot is >= 0 and < GroupOrders.Slots ? GroupOrders.Orders(document, target.Group)[target.Slot] : (short)-1
			: target.Guid;

	/// <summary>Whether a record's presence can be set to row-1 GUID <paramref name="condition"/>: a flag test, or a range over a draw.</summary>
	public static bool Offered(MissionDocument document, short condition) =>
		ConditionEditing.IndexOf(document, condition) != null && ConditionEditing.Record(document, condition) is var record
			&& (record.Type == ConditionEditing.FlagTest
				|| (record.Type == ConditionEditing.Range && ConditionEditing.ParentOf(document, condition)?.Type == ConditionEditing.Draw));

	/// <summary>Every condition <see cref="Offered"/>, by GUID in file order.</summary>
	public static IReadOnlyList<short> Choices(MissionDocument document) =>
		ConditionEditing.Records(document).Select(record => record.GUID).Where(guid => guid != -1).Distinct()
			.Where(guid => Offered(document, guid)).ToList();

	/// <summary>Why <paramref name="target"/>'s presence cannot be set at all, or null when it can.</summary>
	public static string? WhyFixed(MissionDocument document, PresenceTarget target) {
		var records = Records(document, target);
		if (records.Count == 0) {
			return target.Row == OrderRow ? $"There is no order in slot {target.Slot}." : "It is not in the file.";
		}

		if (InSquad(document, target)) {
			return "The squad is built from group 0's members, so its presence is not set here.";
		}

		// Where a first order is absent, slot 0 holds none, and the later orders do not move up.
		if (target.Row == OrderRow && target.Slot == 0 && GroupOrders.WhyNeedsFirstOrder(document, target.Group) is { } first) {
			return first;
		}

		return Shared(document, target) == null ? "Its records carry different conditions." : null;
	}

	/// <summary>
	/// Why <paramref name="target"/> cannot be made present only when row-1 GUID <paramref name="condition"/> holds, or always for
	/// <see cref="Always"/>; null when it can.
	/// </summary>
	public static string? WhyNoPresence(MissionDocument document, PresenceTarget target, short condition) =>
		WhyFixed(document, target)
			?? (condition == Always ? null
				: ConditionEditing.IndexOf(document, condition) == null ? $"Condition {condition} is not in the file."
				: !Offered(document, condition) ? $"Condition {condition} is not a flag test or a range over a draw."
				: null);

	/// <summary>
	/// Makes <paramref name="target"/> present only while row-1 GUID <paramref name="condition"/> holds, or always for
	/// <see cref="Always"/>, as one <see cref="MissionDocument.Change"/>. Throws <see cref="InvalidOperationException"/> when
	/// <see cref="WhyNoPresence"/> refuses it. <paramref name="written"/> is the GUID whose records now carry it: the target's,
	/// an order slot's own copy of its order, or <c>-1</c> for an objective.
	/// </summary>
	public static MsnEdit SetPresence(MissionDocument document, PresenceTarget target, short condition, out short written) {
		if (WhyNoPresence(document, target, condition) is { } reason) {
			throw new InvalidOperationException(reason);
		}

		short guid = -1;
		var change = document.Change(() => guid = Write(document, target, condition));
		written = guid;
		return change;
	}

	/// <summary>
	/// Appends a new flag test (<see cref="ConditionEditing.CreateFlagTest"/>) and makes <paramref name="target"/> present only
	/// while it holds, as one <see cref="MissionDocument.Change"/>; <paramref name="condition"/> is its GUID, and
	/// <paramref name="written"/> as <see cref="SetPresence"/> answers it.
	/// </summary>
	public static MsnEdit SetPresenceToNewFlagTest(MissionDocument document, PresenceTarget target, out short condition,
			out short written) {
		if (WhyFixed(document, target) is { } reason) {
			throw new InvalidOperationException(reason);
		}

		short created = -1, guid = -1;
		var change = document.Change(() => {
			ConditionEditing.AppendFlagTest(document, out created);
			guid = Write(document, target, created);
		});
		condition = created;
		written = guid;
		return change;
	}

	/// <summary>
	/// Appends a new draw and a range over it (<see cref="ConditionEditing.CreateDraw"/>, <see cref="ConditionEditing.CreateRange"/>)
	/// and makes <paramref name="target"/> present only while the range holds, as one <see cref="MissionDocument.Change"/>;
	/// <paramref name="condition"/> is the range's GUID, and <paramref name="written"/> as <see cref="SetPresence"/> answers it.
	/// The draw is made ahead of every variant pick, so every pick changes.
	/// </summary>
	public static MsnEdit SetPresenceToNewDrawRange(MissionDocument document, PresenceTarget target, out short condition,
			out short written) {
		if (WhyFixed(document, target) is { } reason) {
			throw new InvalidOperationException(reason);
		}

		short created = -1, guid = -1;
		var change = document.Change(() => {
			ConditionEditing.AppendDraw(document, out short draw);
			ConditionEditing.AppendRange(document, draw, out created);
			guid = Write(document, target, created);
		});
		condition = created;
		written = guid;
		return change;
	}

	/// <summary>
	/// The conditions of <paramref name="target"/>'s variations that set some field (<see cref="LayoutEntry.Variations"/>):
	/// an edit writes the base, so where such a condition holds, a field the variation sets keeps the variation's value.
	/// Distinct, in file order, <see cref="Always"/> included; empty for an objective, which no other record overlays, and for
	/// a target no record carries.
	/// </summary>
	public static IReadOnlyList<short> VariationConditions(MissionDocument document, PresenceTarget target) {
		if (target.Row == ObjectiveRow || document.Layout.Entry(target.Row, GuidOf(document, target)) is not { } entry) {
			return [];
		}

		return entry.Variations.Where(variation => variation.Words.Count > 0).Select(variation => variation.Condition).Distinct()
			.ToList();
	}

	/// <summary>Whether a record of <paramref name="target"/> draws a variant: its records' variant keys are set.</summary>
	public static bool DrawsVariant(MissionDocument document, PresenceTarget target) =>
		Records(document, target).Any(record => document.RecordAt(record.Record) switch {
			MechRosterEntry144 mech => mech.VariantKey != -1,
			FlyerRosterEntry102 flyer => flyer.VariantKey != -1,
			BaseRosterEntry62 structure => structure.VariantKey != -1,
			MissionOrder22 order => order.VariantKey != -1,
			_ => false,
		});

	// Writes <condition> into every record of <target>, inside the caller's change, an order slot's order given to the slot
	// first; answers the GUID written, -1 for an objective.
	private static short Write(MissionDocument document, PresenceTarget target, short condition) {
		switch (target.Row) {
			case ObjectiveRow:
				document.Rewrite<MissionObjective58>(ObjectiveRow, target.Index, record => record.ConditionRef = condition);
				return -1;
			case OrderRow:
				short order = GroupOrders.Own(document, target.Group, target.Slot);
				document.RewriteEvery<MapObject>(OrderRow, order, record => SetCondition(record, condition));
				return order;
			default:
				document.RewriteEvery<MapObject>(target.Row, target.Guid, record => SetCondition(record, condition));
				return target.Guid;
		}
	}

	// Group 0, as the generation exports it, an order in one of its slots, or a herc its member slots reference in any of its
	// records.
	private static bool InSquad(MissionDocument document, PresenceTarget target) {
		short squad = document.GuidOf(SquadBlock, 0);
		return squad != -1 && target.Row switch {
			GroupRow => target.Guid == squad,
			OrderRow => target.Group == squad,
			>= 12 and <= 14 => RosterDeletion.Check(document, target.Row, target.Guid).InSquad,
			_ => false,
		};
	}

	private static short ConditionOf(MapObject record) => record switch {
		MechRosterEntry144 mech => mech.ConditionRef,
		FlyerRosterEntry102 flyer => flyer.ConditionRef,
		BaseRosterEntry62 structure => structure.ConditionRef,
		MissionOrder22 order => order.ConditionRef,
		MissionGroup164 group => group.ConditionRef,
		_ => throw new InvalidOperationException($"A {record.GetType().Name}'s presence is not set here."),
	};

	private static void SetCondition(MapObject record, short condition) {
		switch (record) {
			case MechRosterEntry144 mech: mech.ConditionRef = condition; break;
			case FlyerRosterEntry102 flyer: flyer.ConditionRef = condition; break;
			case BaseRosterEntry62 structure: structure.ConditionRef = condition; break;
			case MissionOrder22 order: order.ConditionRef = condition; break;
			case MissionGroup164 group: group.ConditionRef = condition; break;
			default: throw new InvalidOperationException($"A {record.GetType().Name}'s presence is not set here.");
		}
	}
}
