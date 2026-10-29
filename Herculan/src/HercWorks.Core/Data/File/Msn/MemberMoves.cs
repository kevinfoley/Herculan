namespace HercWorks.Core.Data.File.Msn;

/// <summary>
/// Moves a herc, flyer, structure or ground vehicle outside the player's squad from its group into another, as record edits:
/// its GUID is cleared from every member slot of its group naming it, in every record of the group's GUID
/// (<see cref="MissionDocument.ClearMember"/>), and written into the other group's first member slot no record of it fills
/// in any state (<see cref="MissionDocument.FreeMemberSlot"/>), as a join fills one. It then fights on the other group's
/// side, works through its orders and is present when it is, as every member does.
///
/// <list type="bullet">
/// <item>The group it may move into is one an add of its kind may join: <see cref="HercGroups.WhyNoJoin"/>,
/// <see cref="FlyerGroups.WhyNoJoin"/>, <see cref="VehicleGroups.WhyNoJoin"/> for a ground vehicle moving into a convoy, and
/// otherwise any structure group but the squad with a free member slot.</item>
/// <item>A herc, a flyer, and a ground vehicle moving into a convoy, naming no point of its own, take their formation place
/// in the new group, as one joining it does. A structure naming none, or a ground vehicle moving into a group that is not a
/// convoy, is given a point where it stands, as a structure added to a group is, since the group's formation would move it
/// (docs/retail/formats/script-dat.md#placement--the-actual-rule, rules 2 and 7).</item>
/// <item>A group the move leaves with no member in any record goes with it, as a delete removes it
/// (<see cref="RosterDeletion"/>), and the move is refused while anything references that group.</item>
/// <item>The player's squad is refused, and so is an object more than one group places.</item>
/// </list>
/// </summary>
public static class MemberMoves {
	private const int PointRow = 6;
	private const int MechRow = 12;
	private const int FlyerRow = 13;
	private const int BaseRow = 14;
	private const short BaseRoster = 2;

	/// <summary>
	/// Why row <paramref name="row"/>'s GUID <paramref name="member"/> (12 a herc, 13 a flyer, 14 a structure or ground
	/// vehicle) cannot move into row-16 group <paramref name="to"/>, or null when it can. <paramref name="isGroundVehicle"/>
	/// answers which <c>BASES.DAT</c> types are ground vehicles.
	/// </summary>
	public static string? WhyNoMove(MissionDocument document, int row, short member, short to, Func<int, bool> isGroundVehicle) {
		if (row is < MechRow or > BaseRow || document.Layout.Entry(row, member) == null) {
			return "Not found.";
		}

		var check = RosterDeletion.Check(document, row, member);
		if (check.InSquad) {
			return "It is in the player's squad, whose squadmates are added and removed in the Player's squad view.";
		}

		if (check.MemberOf.Count != 1) {
			return check.MemberOf.Count == 0 ? "No group places it." : "More than one group places it.";
		}

		short from = check.MemberOf[0];
		if (to == from) {
			return "It is in that group already.";
		}

		if (document.Layout.Entry<MissionGroup164>(to) == null) {
			return "Not found.";
		}

		string? refused = row switch {
			MechRow => HercGroups.WhyNoJoin(document, to),
			FlyerRow => FlyerGroups.WhyNoJoin(document, to),
			_ when IntoConvoy(document, member, to, isGroundVehicle) => VehicleGroups.WhyNoJoin(document, to, isGroundVehicle),
			_ => WhyNoStructureJoin(document, to),
		};
		if (refused != null) {
			return refused;
		}

		return check.EmptiedGroups.Any(emptied => emptied.Group == from && emptied.Namers.Any)
			? "It is the last member of its group, which would go with it, and the mission references that group."
			: null;
	}

	/// <summary>
	/// Moves row <paramref name="row"/>'s GUID <paramref name="member"/> into row-16 group <paramref name="to"/>, as one
	/// <see cref="MissionDocument.Change"/>: out of its group, which goes with it when it is the last member, into the other's
	/// first free member slot, and, for a structure that needs one, onto a new point at <paramref name="standAt"/>, where it
	/// stands. Throws <see cref="InvalidOperationException"/> when <see cref="WhyNoMove"/> refuses it.
	/// </summary>
	public static MsnEdit Move(MissionDocument document, int row, short member, short to, Func<int, bool> isGroundVehicle,
			(int X, int Y, int Z) standAt) {
		if (WhyNoMove(document, row, member, to, isGroundVehicle) is { } reason) {
			throw new InvalidOperationException(reason);
		}

		var check = RosterDeletion.Check(document, row, member);
		short from = check.MemberOf[0];
		bool emptied = check.EmptiedGroups.Any(group => group.Group == from);
		int slot = document.FreeMemberSlot(to);
		bool needsPoint = row == BaseRow && !IntoConvoy(document, member, to, isGroundVehicle)
			&& document.Layout.Entry(PointRow, document.Layout.Entry<BaseRosterEntry62>(member)!.Fields.PositionRef) == null;
		return document.Change(() => {
			if (emptied) {
				RosterDeletion.RemoveGroup(document, from);
			} else {
				document.ClearMember(from, member);
			}

			document.SetField(MsnField.GroupMember(slot), to, member);
			if (needsPoint) {
				document.Append(PointRow, new MapPoint22 {
					ConditionRef = -1, RandomTableKey = -1, Unk06 = -1, X = standAt.X, Y = standAt.Y, Z = standAt.Z,
				}, out short point);
				document.SetField(MsnField.BasePosition, member, point);
			}
		});
	}

	// Whether row-14 GUID <member> is a ground vehicle moving into a convoy.
	private static bool IntoConvoy(MissionDocument document, short member, short to, Func<int, bool> isGroundVehicle) =>
		document.Layout.Entry<BaseRosterEntry62>(member)?.Fields is { } record && isGroundVehicle(record.TypeIndex)
			&& VehicleGroups.IsConvoy(document, to, isGroundVehicle);

	// Why a structure cannot join row-16 group <to>: it must be a structure group, not the squad, with a member slot no record
	// of it fills.
	private static string? WhyNoStructureJoin(MissionDocument document, short to) =>
		to == SquadRoster.Squad(document) || document.Layout.Entry<MissionGroup164>(to)?.Fields.MemberKind != BaseRoster
			? "It is not a group of structures."
			: document.FreeMemberSlot(to) < 0 ? "It already has 20 members, as many as a group holds."
			: null;
}
