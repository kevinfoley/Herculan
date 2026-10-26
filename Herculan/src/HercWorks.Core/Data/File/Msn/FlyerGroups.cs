using HercWorks.Core.Data.File.Msn.Script;

namespace HercWorks.Core.Data.File.Msn;

/// <summary>
/// Adds flyers to a <see cref="MissionDocument"/>, as record edits: a flyer in a new group of its own, or a flyer joining
/// an existing flyer group.
///
/// <para>A new group is built as <see cref="HercGroups"/> builds a herc's (<see cref="NewGroups"/>): a route of one
/// waypoint at the drop point, an order on it (<see cref="NewGroups.FirstOrder"/>: for a Cybrid group search/destroy on group
/// 0, the player's squad, for a Human one patrol), the flyer's row-13 record, and a row-16 group of flyers with the flyer in member slot 0 and the order in order slot 0, naming no point
/// and no heading, so the group stands on its route's only waypoint (docs/retail/formats/script-dat.md#placement--the-actual-rule,
/// rule 3). That waypoint's point has Z 0, so DBSIM starts the flyer at 5,000 units (rule 5), as every retail flyer group
/// starts. Of the 76 records of retail flyer groups naming a member, 73 name no point and 75 no heading, and the slot-0
/// orders of 52 have the player's squad as their subject; 67 of those groups' 81 orders are search/destroy
/// (<c>HercWorks.Query values</c> and <c>orders</c>, raw records with no condition applied). The flyer record's fields take the value most of the 84 retail row-13 records of a type carry
/// (<c>values --row 13 --where TypeIndex!=-1</c>); each count is beside its constant.</para>
///
/// <para>A flyer joining a group takes the group's first member slot no record of the group's GUID fills in any state
/// (<see cref="MissionDocument.FreeMemberSlot"/>), up to <see cref="LastJoinSlot"/>, and its side; its record names no
/// point, so it flies in its formation place.</para>
///
/// <para>Block 8 holds DBSIM's <see cref="ScriptDatLint.FlyerSlotCap"/> flyer slots
/// (docs/retail/formats/script-dat.md#the-two-pass-read--and-what-it-means-for-dbsim-keeps): an add that would pass it in
/// any version of the mission is refused, counted as <see cref="RosterLimits"/> counts it.</para>
/// </summary>
public static class FlyerGroups {
	/// <summary>
	/// The last member slot a flyer joins in: <c>dat\FFORMS.DAT</c>'s formations place three followers beside slot 0
	/// (docs/retail/formats/script-dat.md#the-flyer-formation-table), and no retail flyer group has more than two members.
	/// This editor's limit, as <see cref="HercGroups.LastJoinSlot"/> is, not the file's.
	/// </summary>
	public const int LastJoinSlot = 3;

	private const int FlyerRow = 13;
	private const short FlyerRoster = 1;

	/// <summary>The span at <c>0x08</c>: 1 in its first word and 0 in the other 19, in all 84 flyer records.</summary>
	private const short FlagSpanFirst = 1;

	/// <summary>The word at <c>0x64</c>: 100 in all 84.</summary>
	private const short Constant100 = 100;

	/// <summary>
	/// A flyer of <c>nam\FLYERS.NAM</c> type <paramref name="type"/>, as a GUID-only record (<see cref="MissionDocument.Append"/>
	/// gives it one), unconditioned and drawing no variant: no point or heading of its own (84 of 84 name none), firing no
	/// action (no engaged action in 84, no defeated action in 60) and writing no counter (84 of 84).
	/// </summary>
	public static FlyerRosterEntry102 Machine(short type) {
		var flags = new short[20];
		flags[0] = FlagSpanFirst;
		return new FlyerRosterEntry102 {
			GUID = -1, ConditionRef = -1, VariantKey = -1, Unk06 = -1, FlagSpan = flags, PositionRef = -1, HeadingRef = -1,
			TypeIndex = type, PairCount = 0, OutOfActionReport = NewGroups.Unset(20), EngagementActionRef = -1,
			DefeatActionRef = -1, UnkVal_100 = Constant100,
		};
	}

	/// <summary>Why no flyer can be added to <paramref name="document"/>, or null when one can.</summary>
	public static string? WhyNoAdd(MissionDocument document) => RosterLimits.WhyNoRoom(document.Layout, FlyerRow, 1);

	/// <summary>
	/// Adds <paramref name="flyer"/> in a new group on <paramref name="side"/> (0 human, 1 Cybrid), standing on a route of
	/// one waypoint at (<paramref name="x"/>, <paramref name="y"/>) and given its first order there
	/// (<see cref="NewGroups.FirstOrder"/>), as one <see cref="MissionDocument.Change"/>; answers the flyer's row-13 GUID and the
	/// group's row-16 GUID. Throws <see cref="InvalidOperationException"/> when <see cref="WhyNoAdd"/> refuses it, or a Cybrid
	/// group's mission has no group 0.
	/// </summary>
	public static MsnEdit Create(MissionDocument document, FlyerRosterEntry102 flyer, short side, int x, int y,
			out short guid, out short group) {
		if (WhyNoAdd(document) is { } reason) {
			throw new InvalidOperationException(reason);
		}

		return NewGroups.Create(document, FlyerRow, flyer, FlyerRoster, side, x, y, out guid, out group);
	}

	/// <summary>
	/// Why a flyer cannot join row-16 group <paramref name="group"/> of <paramref name="document"/>, or null when it can: a
	/// flyer group shown in the editor with a member slot up to <see cref="LastJoinSlot"/> that no record of it
	/// fills.
	/// </summary>
	public static string? WhyNoJoin(MissionDocument document, short group) =>
		NewGroups.WhyNoJoin(document, group, FlyerRoster, LastJoinSlot, "It is not a flyer group.",
			$"Its formation has places for {LastJoinSlot + 1} flyers, and each is taken.");

	/// <summary>
	/// Adds <paramref name="flyer"/> to flyer group <paramref name="group"/> in its first free member slot, as one
	/// <see cref="MissionDocument.Change"/>; answers the flyer's row-13 GUID. Throws <see cref="InvalidOperationException"/>
	/// when <see cref="WhyNoAdd"/> or <see cref="WhyNoJoin"/> refuses it.
	/// </summary>
	public static MsnEdit Join(MissionDocument document, short group, FlyerRosterEntry102 flyer, out short guid) {
		if ((WhyNoAdd(document) ?? WhyNoJoin(document, group)) is { } reason) {
			throw new InvalidOperationException(reason);
		}

		return NewGroups.Join(document, FlyerRow, group, flyer, out guid);
	}
}
