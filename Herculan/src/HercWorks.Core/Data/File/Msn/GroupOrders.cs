namespace HercWorks.Core.Data.File.Msn;

/// <summary>
/// Edits a group's ten order slots in a <see cref="MissionDocument"/>, as record edits: a slot's verb, route and subject,
/// and adding, removing and moving orders. What each field means is docs/retail/simulation/ai-goals.md's.
///
/// <list type="bullet">
/// <item>The slots stay filled from slot 0 without a gap: <c>Group_OrderTick</c> (<c>00423a74</c>) moves a group on only
/// to a next slot that holds an order, so an order after an empty slot is never reached. An add fills the slot after the
/// last filled one, and a remove moves every later order up a slot.</item>
/// <item>An order is a row-15 record a group's slot names by GUID, and one order can be named by more than one slot: 7 of
/// the 609 order GUIDs the retail groups name are named by two or three groups, each in the same slot of each
/// (<c>HercWorks.Query records</c>, every row-16 record whatever its condition). Editing a slot whose order another slot
/// also names first gives the slot its own copy, every record of the GUID copied under a new GUID
/// (<see cref="MissionDocument.AppendCopy"/>), so the other slots keep the order as it was.</item>
/// <item>A field is written through the record that supplies it (<see cref="MissionDocument.SetField"/>): 5 of the 637
/// retail orders carry a condition. A slot is written in the group's record that supplies it the same way.</item>
/// <item>Removing an order removes its row-15 records only when no slot of any record of any group names it any more.</item>
/// <item>The last order of a group is kept when DBSIM would fault without one (<see cref="WhyNoRemove"/>): a group with no
/// heading of its own takes its heading from slot 0's route with no test that the slot holds an order, and a herc group's
/// AI installs a null behaviour with none (docs/retail/simulation/ai-goals.md#a-group-with-no-order-at-all). A flyer group
/// keeps its last order too: every retail flyer group naming a member has one, and what its AI does with none is not
/// established; this editor's rule.</item>
/// <item>Two shapes no retail order has, where a fault is plausible, are not made: a first order naming no route in a group
/// with no point of its own (<see cref="WhyNoFirstRoute"/>), and search/destroy, ramming or following naming no subject
/// (<see cref="NeedsSubject"/>).</item>
/// </list>
/// </summary>
public static class GroupOrders {
	/// <summary>The order slots a group carries.</summary>
	public const int Slots = 10;

	/// <summary>Patrol: the verb of 36 of the 63 slot-0 orders of the retail squad's records, each naming no subject.</summary>
	public const short Patrol = 3;

	private const short Ram = 1;
	private const short Follow = 6;

	/// <summary>The highest verb, of seven (docs/retail/simulation/ai-goals.md#when-an-order-is-finished--group_isordercomplete-004239fc).</summary>
	public const short LastVerb = 6;

	private const int OrderBlock = 10;
	private const int RouteBlock = 3;
	private const int GroupBlock = 11;
	private const int OrderRow = 15;
	private const int GroupRow = 16;
	private const short MechRoster = 0;
	private const short FlyerRoster = 1;

	/// <summary>
	/// The row-15 GUID in each of row-16 group <paramref name="group"/>'s ten order slots as generated, <c>-1</c> for an
	/// empty slot; all <c>-1</c> for a group this state of the mission leaves out.
	/// </summary>
	public static short[] Orders(MissionDocument document, short group) {
		var orders = Enumerable.Repeat((short)-1, Slots).ToArray();
		if (document.ExportedIndex(GroupRow, group) is not { } index) {
			return orders;
		}

		var refs = document.Script.Groups[index].OrderRefs;
		for (int slot = 0; slot < Slots && slot < refs.Length; slot++) {
			orders[slot] = refs[slot] >= 0 ? document.GuidOf(OrderBlock, refs[slot]) : (short)-1;
		}

		return orders;
	}

	/// <summary>How many order slots are filled, from slot 0 to the last filled one.</summary>
	public static int Filled(short[] orders) => Array.FindLastIndex(orders, order => order != -1) + 1;

	/// <summary>
	/// How many order slots name row-15 GUID <paramref name="order"/>: each group's slot counted once, whichever of its
	/// records name it, whatever their condition.
	/// </summary>
	public static int SlotsNaming(MissionDocument document, short order) =>
		order == -1 ? 0 : (document.Mission.Groups ?? [])
			.SelectMany(record => record.OrderRefs.Select((named, slot) => (record.GUID, named, slot)))
			.Where(entry => entry.named == order)
			.Select(entry => (entry.GUID, entry.slot))
			.Distinct()
			.Count();

	/// <summary>Whether another order slot names row-15 GUID <paramref name="order"/> too, so an edit made here copies it first.</summary>
	public static bool Shared(MissionDocument document, short order) => SlotsNaming(document, order) > 1;

	/// <summary>
	/// The order a new slot of <paramref name="group"/> gets: for the player's squad, patrol (<see cref="Patrol"/>) naming
	/// no subject; for any other group, search/destroy on the player's squad, as a new herc group's first order
	/// (<see cref="HercGroups"/>). Either on slot 0's route, or none when the group has no order yet.
	/// </summary>
	public static MissionOrder22 NewOrder(MissionDocument document, short group) {
		short squad = document.GuidOf(GroupBlock, 0);
		short route = RouteOf(document, Orders(document, group)[0]);
		return group == squad || squad == -1
			? HercGroups.Order(Patrol, route, -1, -1)
			: HercGroups.Order(HercGroups.SearchDestroy, route, HercGroups.GroupSubject, squad);
	}

	/// <summary>
	/// Whether an order of <paramref name="verb"/> needs a subject: search/destroy, ramming and following. Every retail order
	/// of the three names one (385, 17 and 9 of the 637 orders, <c>HercWorks.Query records</c>), and
	/// <c>Group_IsOrderComplete</c> (<c>004239fc</c>) reads search/destroy's subject's condition
	/// (docs/retail/simulation/ai-goals.md#when-an-order-is-finished--group_isordercomplete-004239fc).
	/// </summary>
	public static bool NeedsSubject(short verb) => verb is HercGroups.SearchDestroy or Ram or Follow;

	/// <summary>
	/// Why route <paramref name="route"/>, a row-8 GUID or <c>-1</c>, cannot be <paramref name="group"/>'s first order's
	/// route, or null when it can: a group with no point of its own stands on its first order's route's first waypoint
	/// (docs/retail/formats/script-dat.md#placement--the-actual-rule, rule 3), so it needs one. Each of the 35 retail group
	/// records whose slot-0 order names no route names a point of its own (8 of them no heading).
	/// </summary>
	public static string? WhyNoFirstRoute(MissionDocument document, short group, short route) =>
		route == -1 && document.ExportedIndex(GroupRow, group) is { } index && document.Script.Groups[index].PositionRef < 0
			? "With no point of its own, the group stands on its first order's route, so that order needs one."
			: null;

	/// <summary>Why no order can be added to <paramref name="group"/>, or null when one can.</summary>
	public static string? WhyNoAdd(MissionDocument document, short group) {
		if (document.ExportedIndex(GroupRow, group) == null) {
			return "The group is not in this state of the mission.";
		}

		int filled = Filled(Orders(document, group));
		return filled >= Slots ? $"All {Slots} order slots are filled."
			: filled == 0 ? WhyNoFirstRoute(document, group, NewOrder(document, group).RouteRef)
			: null;
	}

	/// <summary>
	/// Appends <paramref name="order"/> under a new GUID, <paramref name="guid"/>, and names it in the slot after
	/// <paramref name="group"/>'s last order, as one <see cref="MissionDocument.Change"/>. Throws
	/// <see cref="InvalidOperationException"/> when <see cref="WhyNoAdd"/> refuses it.
	/// </summary>
	public static MsnEdit Add(MissionDocument document, short group, MissionOrder22 order, out short guid) {
		if (WhyNoAdd(document, group) is { } reason) {
			throw new InvalidOperationException(reason);
		}

		int slot = Filled(Orders(document, group));
		if ((slot == 0 ? WhyNoFirstRoute(document, group, order.RouteRef) : null) is { } noRoute) {
			throw new InvalidOperationException(noRoute);
		}

		if (NeedsSubject(order.Verb) && order.SubjectKind == -1) {
			throw new InvalidOperationException("That order needs a subject.");
		}

		short added = -1;
		var change = document.Change(() => {
			document.Append(OrderRow, order, out added);
			document.SetField(MsnField.GroupOrder(slot), group, added);
		});
		guid = added;
		return change;
	}

	/// <summary>Why the order in <paramref name="group"/>'s slot <paramref name="slot"/> cannot be removed, or null when it can.</summary>
	public static string? WhyNoRemove(MissionDocument document, short group, int slot) {
		var orders = Orders(document, group);
		if (slot < 0 || slot >= Slots || orders[slot] == -1) {
			return $"There is no order in slot {slot}.";
		}

		if (Filled(orders) > 1) {
			return slot == 0 ? WhyNoFirstRoute(document, group, RouteOf(document, orders[1])) : null;
		}

		var record = document.Script.Groups[document.ExportedIndex(GroupRow, group)!.Value];
		bool members = record.MemberRefs.Any(member => member >= 0);
		bool squad = group == document.GuidOf(GroupBlock, 0);
		return record.HeadingRef < 0 ? "With no heading of its own, the group needs a first order: DBSIM faults loading it."
			: record.MemberKind == MechRoster && members && !squad ? "A herc group needs a first order: its hercs' AI faults with none."
			: record.MemberKind == FlyerRoster && members ? "A flyer group keeps its first order: what its AI does with none is not known."
			: null;
	}

	/// <summary>
	/// Removes the order in <paramref name="group"/>'s slot <paramref name="slot"/>, moving each later order up a slot, and
	/// the order's records when no slot names it any more, as one <see cref="MissionDocument.Change"/>. Throws
	/// <see cref="InvalidOperationException"/> when <see cref="WhyNoRemove"/> refuses it.
	/// </summary>
	public static MsnEdit Remove(MissionDocument document, short group, int slot) {
		if (WhyNoRemove(document, group, slot) is { } reason) {
			throw new InvalidOperationException(reason);
		}

		var orders = Orders(document, group);
		int last = Filled(orders) - 1;
		short removed = orders[slot];
		return document.Change(() => {
			for (int at = slot; at < last; at++) {
				document.SetField(MsnField.GroupOrder(at), group, orders[at + 1]);
			}

			document.SetField(MsnField.GroupOrder(last), group, -1);
			if (SlotsNaming(document, removed) == 0) {
				document.Remove(OrderRow, removed);
			}
		});
	}

	/// <summary>
	/// Why the order in <paramref name="group"/>'s slot <paramref name="slot"/> cannot trade places with the one in slot
	/// <paramref name="to"/>, the slot before or after it, or null when it can.
	/// </summary>
	public static string? WhyNoMove(MissionDocument document, short group, int slot, int to) {
		var orders = Orders(document, group);
		if (Math.Abs(to - slot) != 1 || slot < 0 || to < 0 || slot >= Slots || to >= Slots || orders[slot] == -1 || orders[to] == -1) {
			return "There is no order there to trade places with.";
		}

		return slot == 0 || to == 0 ? WhyNoFirstRoute(document, group, RouteOf(document, orders[slot == 0 ? to : slot])) : null;
	}

	/// <summary>
	/// Why the order in <paramref name="group"/>'s slot <paramref name="slot"/> cannot take route <paramref name="route"/>,
	/// a row-8 GUID or <c>-1</c>, or null when it can (<see cref="WhyNoFirstRoute"/>).
	/// </summary>
	public static string? WhyNoRoute(MissionDocument document, short group, int slot, short route) =>
		slot == 0 ? WhyNoFirstRoute(document, group, route) : null;

	/// <summary>
	/// Swaps the orders in <paramref name="group"/>'s slots <paramref name="slot"/> and <paramref name="to"/>, as one
	/// <see cref="MissionDocument.Change"/>. Throws <see cref="InvalidOperationException"/> when <see cref="WhyNoMove"/>
	/// refuses it.
	/// </summary>
	public static MsnEdit Move(MissionDocument document, short group, int slot, int to) {
		if (WhyNoMove(document, group, slot, to) is { } reason) {
			throw new InvalidOperationException(reason);
		}

		var orders = Orders(document, group);
		return document.Change(() => {
			document.SetField(MsnField.GroupOrder(slot), group, orders[to]);
			document.SetField(MsnField.GroupOrder(to), group, orders[slot]);
		});
	}

	/// <summary>
	/// Sets the verb, 0 to <see cref="LastVerb"/>, of the order in <paramref name="group"/>'s slot <paramref name="slot"/>. A
	/// verb that <see cref="NeedsSubject"/> on an order naming none takes the player's squad, group 0, as its subject in the
	/// same change, as a new herc group's first order has it (<see cref="HercGroups"/>).
	/// </summary>
	public static MsnEdit SetVerb(MissionDocument document, short group, int slot, short verb) {
		if (verb is < 0 or > LastVerb) {
			throw new ArgumentOutOfRangeException(nameof(verb), verb, $"A verb is 0 to {LastVerb}.");
		}

		bool noSubject = slot is >= 0 and < Slots && Exported(document, Orders(document, group)[slot]) is { SubjectKind: -1 };
		short squad = document.GuidOf(GroupBlock, 0);
		if (NeedsSubject(verb) && noSubject && squad == -1) {
			throw new InvalidOperationException("That order needs a subject, and the mission has no group 0 to give it.");
		}

		return document.Change(() => {
			short order = Own(document, group, slot);
			document.SetField(MsnField.OrderVerb, order, verb);
			if (NeedsSubject(verb) && noSubject) {
				document.SetField(MsnField.OrderSubjectKind, order, HercGroups.GroupSubject);
				document.SetField(MsnField.OrderSubject, order, squad);
			}
		});
	}

	/// <summary>
	/// Sets the route, a row-8 GUID or <c>-1</c> for none, of the order in <paramref name="group"/>'s slot
	/// <paramref name="slot"/>. Throws <see cref="InvalidOperationException"/> when <see cref="WhyNoRoute"/> refuses it.
	/// </summary>
	public static MsnEdit SetRoute(MissionDocument document, short group, int slot, short route) {
		if (WhyNoRoute(document, group, slot, route) is { } reason) {
			throw new InvalidOperationException(reason);
		}

		return document.Change(() => document.SetField(MsnField.OrderRoute, Own(document, group, slot), route));
	}

	/// <summary>
	/// Sets the subject of the order in <paramref name="group"/>'s slot <paramref name="slot"/>: <paramref name="kind"/>
	/// <c>-1</c> for none, 0 for row-16 group <paramref name="subject"/>, 1 for row-12 herc or 3 for row-14 structure. Kind 2,
	/// a flyer, is refused: no retail order names one
	/// (docs/retail/formats/msn-mission-file.md#row-15-field-decode--the-order-record-dat_00470658-22-bytesrecord). None is
	/// refused for a verb that <see cref="NeedsSubject"/>.
	/// </summary>
	public static MsnEdit SetSubject(MissionDocument document, short group, int slot, short kind, short subject) {
		if (kind is not (-1 or 0 or 1 or 3)) {
			throw new ArgumentOutOfRangeException(nameof(kind), kind, "A subject is none (-1), a group (0), a herc (1) or a structure (3).");
		}

		if (kind == -1 && slot is >= 0 and < Slots && Exported(document, Orders(document, group)[slot]) is { } current
				&& NeedsSubject(current.Verb)) {
			throw new InvalidOperationException("That order needs a subject.");
		}

		return document.Change(() => {
			short order = Own(document, group, slot);
			document.SetField(MsnField.OrderSubjectKind, order, kind);
			document.SetField(MsnField.OrderSubject, order, kind == -1 ? (short)-1 : subject);
		});
	}

	// Row-15 GUID <order> as generated, or null when it is not exported.
	private static Script.ScriptOrder? Exported(MissionDocument document, short order) =>
		order != -1 && document.ExportedIndex(OrderRow, order) is { } index ? document.Script.Orders[index] : null;

	// The row-8 GUID of the route row-15 GUID <order> runs as generated, or -1.
	private static short RouteOf(MissionDocument document, short order) =>
		Exported(document, order) is { RouteRef: >= 0 and var route } ? document.GuidOf(RouteBlock, route) : (short)-1;

	// The order in <group>'s slot <slot> for an edit to write: the slot's own, or, when another slot names it too, a copy
	// named in the slot instead. Made inside the caller's change.
	private static short Own(MissionDocument document, short group, int slot) {
		short order = slot is >= 0 and < Slots ? Orders(document, group)[slot] : (short)-1;
		if (order == -1) {
			throw new InvalidOperationException($"There is no order in slot {slot}.");
		}

		if (!Shared(document, order)) {
			return order;
		}

		document.AppendCopy(OrderRow, order, out short copy);
		document.SetField(MsnField.GroupOrder(slot), group, copy);
		return copy;
	}
}
