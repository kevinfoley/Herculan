namespace HercWorks.Core.Data.File.Msn;

/// <summary>
/// The record edits <see cref="HercGroups"/>, <see cref="FlyerGroups"/> and <see cref="VehicleGroups"/> share: a new group
/// of one member standing on a route of one waypoint and given one order on it, and a member joining a group. Each new
/// record is unconditioned, draws no variant and takes a new GUID; the group's fields take the value most retail groups of
/// each kind carry, counted with <c>HercWorks.Query</c> over the raw <c>.MSN</c> files with no condition applied: the 433
/// row-16 records of herc groups outside the squad naming a member, the 76 of flyer groups naming a member
/// (<c>values --row 16 --kind flyer</c>), and the 12 of structure groups whose every member is a ground vehicle
/// (<c>records</c>, crossed with <c>structures --type all</c>). Each count is beside its constant.
/// </summary>
internal static class NewGroups {
	private const int OrderRow = 15;
	private const int GroupRow = 16;
	private const int GroupBlock = 11;

	/// <summary>
	/// Formation 0: 283 of the 433 herc group records (3 in 146), 61 of the 76 flyer group records (3 in 13) and 12 of the
	/// 12 vehicle group records; 421 of the herc groups' 453 orders, 59 of the flyer groups' 81 and 11 of the vehicle
	/// groups' 12 slot-0 orders.
	/// </summary>
	internal const short Formation = 0;

	/// <summary>
	/// The paints-ground flag set, for a herc or flyer group: 416 of the 433 herc group records and 74 of the 76 flyer group
	/// records. DBSIM reads it of a structure group alone, which <see cref="VehicleGroups"/> leaves clear.
	/// </summary>
	internal const short PaintsGround = 1;

	/// <summary>Map shown unset: 432 of the 433 herc group records, all 76 flyer group records and all 12 vehicle group records.</summary>
	private const short MapShown = -1;

	/// <summary>
	/// Adds <paramref name="member"/> to roster row <paramref name="row"/> (12 or 13) in a new group of
	/// <paramref name="memberKind"/> (0 hercs, 1 flyers) on <paramref name="side"/>, standing on a route of one waypoint at
	/// (<paramref name="x"/>, <paramref name="y"/>, Z 0) and given a search/destroy order on it whose subject is group 0, the
	/// player's squad, as one <see cref="MissionDocument.Change"/>; answers the member's and the group's GUIDs. Throws
	/// <see cref="InvalidOperationException"/> when the mission has no group 0.
	/// </summary>
	internal static MsnEdit Create(MissionDocument document, int row, MapObject member, short memberKind, short side, int x,
			int y, out short memberGuid, out short groupGuid) {
		short squad = document.GuidOf(GroupBlock, 0);
		if (squad == -1) {
			throw new InvalidOperationException("The mission has no group 0, the player's squad, for the new group to hunt.");
		}

		return Create(document, row, member, memberKind, side, x, y, HercGroups.SearchDestroy, HercGroups.GroupSubject, squad,
			PaintsGround, out memberGuid, out groupGuid);
	}

	/// <summary>
	/// Adds <paramref name="member"/> to roster row <paramref name="row"/> (12 to 14) in a new group of
	/// <paramref name="memberKind"/> on <paramref name="side"/>, its paints-ground flag <paramref name="paintsGround"/>,
	/// standing on a route of one waypoint at (<paramref name="x"/>, <paramref name="y"/>, Z 0) and given an order of
	/// <paramref name="verb"/> on it whose subject is <paramref name="subject"/> of <paramref name="subjectKind"/>
	/// (<see cref="HercGroups.Order"/>; kind <c>-1</c> for none), as one <see cref="MissionDocument.Change"/>; answers the
	/// member's and the group's GUIDs.
	/// </summary>
	internal static MsnEdit Create(MissionDocument document, int row, MapObject member, short memberKind, short side, int x,
			int y, short verb, short subjectKind, short subject, short paintsGround, out short memberGuid, out short groupGuid) {
		short added = -1, made = -1;
		var change = document.Change(() => {
			short route = RouteEditing.Append(document, [(x, y)], closed: false);
			document.Append(OrderRow, HercGroups.Order(verb, route, subjectKind, subject), out short order);
			document.Append(row, member, out added);
			document.Append(GroupRow, Group(memberKind, added, route, order, side, paintsGround), out made);
		});
		memberGuid = added;
		groupGuid = made;
		return change;
	}

	/// <summary>
	/// Why row-16 group <paramref name="group"/> cannot take another member of <paramref name="memberKind"/>, or null when it
	/// can: it is <paramref name="notKind"/> unless it is a group of that kind shown in the editor, and
	/// <paramref name="full"/> when no member slot up to <paramref name="lastSlot"/> is free in every record of it
	/// (<see cref="MissionDocument.FreeMemberSlot"/>).
	/// </summary>
	internal static string? WhyNoJoin(MissionDocument document, short group, short memberKind, int lastSlot, string notKind,
			string full) {
		if (group == -1 || document.ExportedIndex(GroupRow, group) is not { } index
				|| document.Script.Groups[index].MemberKind != memberKind) {
			return notKind;
		}

		int slot = document.FreeMemberSlot(group);
		return slot < 0 || slot > lastSlot ? full : null;
	}

	/// <summary>
	/// Appends <paramref name="member"/> to roster row <paramref name="row"/> and names it in <paramref name="group"/>'s
	/// first free member slot, in the record supplying that slot, as one <see cref="MissionDocument.Change"/>; answers the
	/// member's GUID. The caller has checked the join.
	/// </summary>
	internal static MsnEdit Join(MissionDocument document, int row, short group, MapObject member, out short memberGuid) {
		int slot = document.FreeMemberSlot(group);
		short added = -1;
		var change = document.Change(() => {
			document.Append(row, member, out added);
			document.SetField(MsnField.GroupMember(slot), group, added);
		});
		memberGuid = added;
		return change;
	}

	/// <summary><paramref name="count"/> unset words, <c>-1</c> each.</summary>
	internal static short[] Unset(int count) => Enumerable.Repeat((short)-1, count).ToArray();

	// The new group: the member in slot 0 and the order in slot 0, on <side>. It names no point and no heading (402 and 405
	// of the 433 herc group records name none, 73 and 75 of the 76 flyer group records, 12 and 12 of the 12 vehicle group
	// records) and walks its order's route; its own route field names that route too, as 377 of the 433, 69 of the 76 and
	// 12 of the 12 do (the flyer and vehicle counts against slot 0's first record). No deployment action, so it is in the
	// mission from the start (41 of the 76 flyer group records, 8 of the 12 vehicle group records), and no out-of-action
	// counter writes (427 of 433, 76 of 76, 11 of 12). The near constant 0 and the dead zone's 18 zeros are what all 12
	// vehicle group records carry.
	private static MissionGroup164 Group(short memberKind, short member, short route, short order, short side, short paintsGround) {
		var members = Unset(20);
		members[0] = member;
		var orders = Unset(10);
		orders[0] = order;
		return new MissionGroup164 {
			ConditionRef = -1, CompoundConditionPartner = -1, PaintsGround = paintsGround, NearConstant = 0,
			DeadZone = new short[18], MemberKind = memberKind, FormationId = Formation, PositionRef = -1, HeadingRef = -1,
			RouteRef = route, MemberRefs = members, OrderRefs = orders, Side = side, DeploymentActionRef = -1, PairCount = 0,
			OutOfActionReport = Unset(20), MapShown = MapShown,
		};
	}
}
