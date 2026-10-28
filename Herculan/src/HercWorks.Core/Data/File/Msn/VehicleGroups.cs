using HercWorks.Core.Data.File.Msn.Script;

namespace HercWorks.Core.Data.File.Msn;

/// <summary>
/// Adds ground vehicles to a <see cref="MissionDocument"/>, as record edits: a vehicle in a convoy of its own, or a vehicle
/// joining a convoy. A ground vehicle is a structure, a row-14 record in a row-16 group of structures; a structure group
/// whose first member is a ground vehicle is a convoy, whose leader drives the group's route
/// (docs/retail/simulation/ground-vehicles.md#the-ground-vehicle-tick--0046a5d0). Which <c>BASES.DAT</c> types are ground
/// vehicles is the caller's to answer (<c>isGroundVehicle</c>).
///
/// <para>A new convoy takes the shape of the 12 retail structure groups whose every member is a ground vehicle (64 groups
/// hold one; the other 52 are base groups with no orders), counted with <c>HercWorks.Query records</c> over the raw
/// <c>.MSN</c> files with no condition applied, crossed with <c>structures --type all</c>. It is built as
/// <see cref="HercGroups"/> builds a herc's (<see cref="NewGroups"/>): a route of one waypoint at the drop point, a travel
/// order on it naming no subject (verb 5 with no subject and no point in 11 of the 12 slot-0 orders; the other follows), the
/// vehicle's row-14 record, and a row-16 group of structures with the vehicle in member slot 0 and the order in order slot
/// 0, naming no point and no heading, so it stands on its route's only waypoint and faces heading 0
/// (docs/retail/formats/script-dat.md#placement--the-actual-rule, rules 3 and 4), its own route field naming the route.
/// With one waypoint the leader has no waypoint after its cursor, so the convoy stands until the route is extended
/// (docs/retail/simulation/ground-vehicles.md, Leader). The group does not paint its ground (<see cref="PaintsGround"/>).
/// The vehicle record's fields take the value most of the 108 retail row-14 records of a vehicle type carry; each count is
/// beside its constant.</para>
///
/// <para>A vehicle joining a convoy takes the group's first member slot no record of the group's GUID fills in any state
/// (<see cref="MissionDocument.FreeMemberSlot"/>), up to <see cref="LastJoinSlot"/>, and its side; its record names no point,
/// so it takes its formation post, as 29 of the 30 retail convoy members past slot 0 do.</para>
///
/// <para>Block 9 holds DBSIM's <see cref="ScriptDatLint.BaseSlotCap"/> structure slots, which ground vehicles share
/// (docs/retail/formats/script-dat.md#the-two-pass-read--and-what-it-means-for-dbsim-keeps): an add that would pass it in
/// any version of the mission is refused, counted as <see cref="RosterLimits"/> counts it, as a structure's add is.</para>
/// </summary>
public static class VehicleGroups {
	/// <summary>
	/// The last member slot a vehicle joins a convoy in: the leader pick, <c>Group_FirstAbleMember</c> (<c>0046a4b8</c>),
	/// reaches only the first four group members (docs/retail/simulation/ground-vehicles.md), and no retail convoy has more
	/// than four. This editor's limit, not the file's: a vehicle past them follows, but never leads.
	/// </summary>
	public const int LastJoinSlot = 3;

	/// <summary>Travelling, the verb of 11 of the 12 retail convoys' slot-0 orders (docs/retail/simulation/ai-goals.md).</summary>
	public const short Travel = 5;

	/// <summary>
	/// The paints-ground flag clear: 11 of the 12 convoy records. A structure group with it set snaps its point into its
	/// terrain tile and paints that tile (docs/retail/formats/script-dat.md#base-formation-terrain).
	/// </summary>
	private const short PaintsGround = 0;

	private const int BaseRow = 14;
	private const short BaseRoster = 2;

	/// <summary>Pristine: 108 of the 108 vehicle records.</summary>
	private const short StartingCondition = 100;

	/// <summary>
	/// A ground vehicle of <c>BASES.DAT</c> type <paramref name="type"/>, as a GUID-only record
	/// (<see cref="MissionDocument.Append"/> gives it one), unconditioned and picking from no random table (108 of 108): no point or
	/// heading of its own (105 of 108 name neither), firing no action (no engaged or defeated action in 108) and writing no
	/// counter (53 of 108; each of the other 55 writes a counter of its own mission's), its word at <c>0x06</c> <c>-1</c> (108).
	/// </summary>
	public static BaseRosterEntry62 Machine(short type) => new() {
		GUID = -1, ConditionRef = -1, RandomTableKey = -1, Unk06 = -1, TypeIndex = type, PositionRef = -1, HeadingRef = -1,
		PairCount = 0, OutOfActionReport = NewGroups.Unset(20), EngagementActionRef = -1, DefeatActionRef = -1,
		StartingCondition = StartingCondition,
	};

	/// <summary>Why no ground vehicle can be added to <paramref name="document"/>, or null when one can.</summary>
	public static string? WhyNoAdd(MissionDocument document) => RosterLimits.WhyNoRoom(document.Layout, BaseRow, 1);

	/// <summary>
	/// Adds <paramref name="vehicle"/> in a new convoy on <paramref name="side"/> (0 human, 1 Cybrid), standing on a route of
	/// one waypoint at (<paramref name="x"/>, <paramref name="y"/>) and travelling it, as one
	/// <see cref="MissionDocument.Change"/>; answers the vehicle's row-14 GUID and the group's row-16 GUID. Throws
	/// <see cref="InvalidOperationException"/> when <see cref="WhyNoAdd"/> refuses it.
	/// </summary>
	public static MsnEdit Create(MissionDocument document, BaseRosterEntry62 vehicle, short side, int x, int y,
			out short guid, out short group) {
		if (WhyNoAdd(document) is { } reason) {
			throw new InvalidOperationException(reason);
		}

		return NewGroups.Create(document, BaseRow, vehicle, BaseRoster, side, x, y, Travel, -1, -1, PaintsGround, out guid,
			out group);
	}

	/// <summary>
	/// Whether row-16 group <paramref name="group"/> is a convoy: a structure group whose base's first member a record carries
	/// is, in its own base, of a type <paramref name="isGroundVehicle"/> answers true for.
	/// </summary>
	public static bool IsConvoy(MissionDocument document, short group, Func<int, bool> isGroundVehicle) {
		if (document.Layout.Entry<MissionGroup164>(group)?.Fields is not { MemberKind: BaseRoster } record) {
			return false;
		}

		var first = record.MemberRefs.Select(member => document.Layout.Entry<BaseRosterEntry62>(member)).FirstOrDefault(entry => entry != null);
		return first != null && isGroundVehicle(first.Fields.TypeIndex);
	}

	/// <summary>
	/// Why a ground vehicle cannot join row-16 group <paramref name="group"/> of <paramref name="document"/> as a convoy
	/// member, or null when it can: a convoy (<see cref="IsConvoy"/>) with a member slot up to <see cref="LastJoinSlot"/>
	/// that no record of it fills.
	/// </summary>
	public static string? WhyNoJoin(MissionDocument document, short group, Func<int, bool> isGroundVehicle) =>
		!IsConvoy(document, group, isGroundVehicle) ? "It is not a convoy."
			: NewGroups.WhyNoJoin(document, group, BaseRoster, LastJoinSlot, "It is not a convoy.",
				$"The convoy has places for {LastJoinSlot + 1} ground vehicles, and each is taken.");

	/// <summary>
	/// Adds <paramref name="vehicle"/> to convoy <paramref name="group"/> in its first free member slot, as one
	/// <see cref="MissionDocument.Change"/>; answers the vehicle's row-14 GUID. Throws <see cref="InvalidOperationException"/>
	/// when <see cref="WhyNoAdd"/> or <see cref="WhyNoJoin"/> refuses it.
	/// </summary>
	public static MsnEdit Join(MissionDocument document, short group, BaseRosterEntry62 vehicle,
			Func<int, bool> isGroundVehicle, out short guid) {
		if ((WhyNoAdd(document) ?? WhyNoJoin(document, group, isGroundVehicle)) is { } reason) {
			throw new InvalidOperationException(reason);
		}

		return NewGroups.Join(document, BaseRow, group, vehicle, out guid);
	}
}
