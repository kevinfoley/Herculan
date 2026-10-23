using HercWorks.Core.Data.File.Msn.Script;

namespace HercWorks.Core.Data.File.Msn;

/// <summary>
/// Adds hercs outside the player's squad to a <see cref="MissionDocument"/>, as record edits: a herc in a new group of its
/// own, or a herc joining an existing herc group.
///
/// <para>A new group takes the shape retail gives a herc group most often. Of the 433 records of retail herc groups outside
/// the squad that name a member, 288 have a slot-0 order that is search/destroy on a group with a route, 182 of them on the
/// player's squad, and 402 name no point of their own, so the group stands on its route's first waypoint
/// (docs/retail/formats/script-dat.md#placement--the-actual-rule, rule 3). So the add appends, each unconditioned, drawing no variant and under a new GUID: a route of one waypoint at the
/// drop point (<see cref="RouteEditing"/>), a search/destroy order on it whose subject is group 0, the player's squad
/// (docs/retail/simulation/ai-goals.md), the herc's row-12 record, and a row-16 group with the herc in member slot 0 and
/// the order in order slot 0, naming no point and no heading. With one waypoint the route has no first leg, so the group
/// faces heading 0 (rule 4). The other fields take the value most retail records of their kind carry, counted with
/// <c>HercWorks.Query records</c> over the raw <c>.MSN</c> files with no condition applied: the non-squad herc records (662
/// row-12 records drawing no variant, not group 0's, not a variant alternative group 0 draws, of a type), the records of
/// non-squad herc groups naming a member (433 row-16 records), and those groups' orders (453 row-15 records). Each count is
/// beside its constant; the group and the order are built as <see cref="FlyerGroups"/> builds a flyer's
/// (<see cref="NewGroups"/>).</para>
///
/// <para>A herc joining a group takes the group's first member slot no record of the group's GUID fills in any state
/// (<see cref="MissionDocument.FreeMemberSlot"/>), and its side; its record names no point, so it stands in its formation
/// place. The player's squad is refused: <see cref="SquadRoster"/> adds squadmates.</para>
///
/// <para>Block 7 holds DBSIM's <see cref="ScriptDatLint.MechSlotCap"/> mech slots, shared with the squad's
/// <c>player.mec</c> machines (docs/retail/formats/script-dat.md#the-two-pass-read--and-what-it-means-for-dbsim-keeps):
/// an add that would pass it in any version of the mission is refused, counted as <see cref="RosterLimits"/> counts it.</para>
/// </summary>
public static class HercGroups {
	/// <summary>
	/// The first Cybrid type of <c>nam\MECHS.NAM</c>. Types 0 to 8 are the nine chassis the shell fits, each with a stock
	/// fit in <c>gam\ini_*.dat</c> (docs/retail/formats/herc-catalogs.md#the-chassis-type-space); 9 to 20 are the twelve
	/// the model loader paints in the enemy palette (docs/retail/rendering/dts-texture-binding.md), which no shell file
	/// fits.
	/// </summary>
	public const int FirstCybridType = 9;

	/// <summary>
	/// The last member slot a herc joins in: <c>dat\MFORMS.DAT</c>'s formations spread seven followers beside slot 0
	/// (docs/retail/formats/script-dat.md#placement--the-actual-rule, rule 7), and no retail herc group fills a slot past 3.
	/// What DBSIM's <c>Formation_GetSlotOffset</c> (<c>004205cc</c>) reads for a slot past the table is not established;
	/// this editor's limit, not the file's.
	/// </summary>
	public const int LastJoinSlot = 7;

	private const int MechRow = 12;
	private const short MechRoster = 0;

	/// <summary>
	/// Search/destroy, the verb of 290 of the 433 herc groups' slot-0 orders and of 67 of the flyer groups' 81 orders
	/// (docs/retail/simulation/ai-goals.md).
	/// </summary>
	public const short SearchDestroy = 0;

	/// <summary>An order's subject kind naming a row-16 group.</summary>
	public const short GroupSubject = 0;

	/// <summary>Radar active: 662 of the 662 herc records.</summary>
	private const short AiRadarActive = 1;

	/// <summary>Cruise speed 0, the AI's own default: 516 of the 662 (220 in 146).</summary>
	private const short AiCruiseSpeed = 0;

	/// <summary>Pristine: 646 of the 662 (50 in 16).</summary>
	private const short StartingCondition = 100;

	/// <summary>The constant at <c>0x88</c>: 2 in all 662.</summary>
	private const short Constant2 = 2;

	/// <summary>
	/// A Cybrid type's weapon fit and ammunition types: the fit the most retail records of that type carry, among the 662
	/// herc records. A Cybrid is fitted by its own record in every mission, often by a variant drawn at
	/// random, and no file holds a stock fit for one. Where two fits are carried equally often, the first in mission
	/// and record order is taken: this editor's choice.
	/// </summary>
	private static readonly Dictionary<int, (short[] Weapons, short[] Ammo)> CybridFits = new() {
		// MONGOOSE: 15 of 86 records (C1_01's record 9).
		[9] = (Fit(6, 0, 6), Filler()),
		// STINGRAY: 12 of 61, tied with three more of C1_05's alternatives; its record 17.
		[10] = (Fit(6, 3, 6), Filler()),
		// MIRIMAC: 8 of 32, tied with C4_08's record 15; its record 14.
		[11] = (Fit(3, 3, 10, 10, 0), Filler()),
		// RAMSES: 30 of 72 (C1_05's record 13).
		[12] = (Fit(8, 1, 1, 8), Filler()),
		// ACHILLES: 57 of 57.
		[13] = (Fit(10, 0, 0, 3, 3, 15, 15), Ammo(5, 5, 5, 5, 5, 1, 1)),
		// HYPERION: 48 of 48.
		[14] = (Fit(10, 0, 0, 3, 3, 15, 15, 3), Ammo(5, 5, 5, 5, 5, 1, 1, 5)),
		// PITBULL: 22 of 24.
		[15] = (Fit(19), Filler()),
		// SPIDER: 8 of 8, no weapon.
		[16] = (Fit(), Filler()),
		// DIABLO: 93 of 165.
		[17] = (Fit(0, 0, 0, 3, 3, 15, 15, 3), Filler()),
		// HEADHUNT: 19 of 44, tied with C4_01's record 9; its record 8.
		[18] = (Fit(3, 3, 0, 0, 0, 3, 23, 3), Filler()),
		// SCARAB: 8 of 21, tied with C4_09's record 15; its record 14.
		[19] = (Fit(5, 5, 5, 0, 0), Filler()),
		// CERBERUS: 8 of 18, tied with C4_09's record 17; its record 16.
		[20] = (Fit(5, 5, 5, 10, 10, 0, 0, 0), Filler()),
	};

	/// <summary>Whether <c>MECHS.NAM</c> type <paramref name="type"/> is a Cybrid's.</summary>
	public static bool IsCybrid(int type) => type >= FirstCybridType;

	/// <summary>
	/// The weapon fit and ammunition types of Cybrid type <paramref name="type"/> (<see cref="CybridFits"/>), as row 12
	/// carries them; null for any other type, which the caller fits from its stock fit.
	/// </summary>
	public static (short[] Weapons, short[] Ammo)? CybridFit(int type) =>
		CybridFits.TryGetValue(type, out var fit) ? ((short[])fit.Weapons.Clone(), (short[])fit.Ammo.Clone()) : null;

	/// <summary>
	/// A herc of <paramref name="type"/> with <paramref name="weapons"/> and <paramref name="ammo"/> in its ten slots, as
	/// a GUID-only record (<see cref="MissionDocument.Append"/> gives it one), unconditioned and drawing no variant: no point
	/// or heading of its own, firing no action and writing no counter (no action in any of the 662 records, no counter
	/// write in 425), and the AI settings, the starting condition and the constant <see cref="HercGroups"/> counts.
	/// </summary>
	public static MechRosterEntry144 Machine(short type, short[] weapons, short[] ammo) => new() {
		GUID = -1, ConditionRef = -1, VariantKey = -1, CompoundConditionPartner = -1, AiRadarActive = AiRadarActive,
		AiCruiseSpeed = AiCruiseSpeed, DeadZone = new short[18], TypeIndex = type, WeaponRefs = weapons, PositionRef = -1,
		HeadingRef = -1, PairCount = 0, OutOfActionReport = NewGroups.Unset(20), WeaponSecondary = ammo, Constant2 = Constant2,
		EngagementActionRef = -1, DefeatActionRef = -1, StartingCondition = StartingCondition,
	};

	/// <summary>Why no herc can be added to <paramref name="document"/>, or null when one can (<see cref="RosterLimits.WhyNoRoom"/>).</summary>
	public static string? WhyNoAdd(MissionDocument document) => RosterLimits.WhyNoRoom(document.Layout, MechRow, 1);

	/// <summary>
	/// Adds <paramref name="herc"/> in a new group on <paramref name="side"/> (0 human, 1 Cybrid), standing on a route of
	/// one waypoint at (<paramref name="x"/>, <paramref name="y"/>) and hunting the player's squad, as one
	/// <see cref="MissionDocument.Change"/>; answers the herc's row-12 GUID and the group's row-16 GUID. Throws
	/// <see cref="InvalidOperationException"/> when <see cref="WhyNoAdd"/> refuses it, or the mission has no group 0.
	/// </summary>
	public static MsnEdit Create(MissionDocument document, MechRosterEntry144 herc, short side, int x, int y,
			out short mech, out short group) {
		if (WhyNoAdd(document) is { } reason) {
			throw new InvalidOperationException(reason);
		}

		return NewGroups.Create(document, MechRow, herc, MechRoster, side, x, y, out mech, out group);
	}

	/// <summary>
	/// Why a herc cannot join row-16 group <paramref name="group"/> of <paramref name="document"/>, or null when it can: a
	/// herc group shown in the editor, not the player's squad, with a member slot up to
	/// <see cref="LastJoinSlot"/> that no record of it fills.
	/// </summary>
	public static string? WhyNoJoin(MissionDocument document, short group) {
		if (group != -1 && group == SquadRoster.Squad(document)) {
			return "Squadmates are added with Add a squadmate in the Player's squad view: click Player's squad under Groups in the Mission panel.";
		}

		return NewGroups.WhyNoJoin(document, group, MechRoster, LastJoinSlot, "It is not a herc group.",
			$"Its formation has places for {LastJoinSlot + 1} hercs, and each is taken.");
	}

	/// <summary>
	/// Adds <paramref name="herc"/> to herc group <paramref name="group"/> in its first free member slot, as one
	/// <see cref="MissionDocument.Change"/>; answers the herc's row-12 GUID. Throws <see cref="InvalidOperationException"/>
	/// when <see cref="WhyNoAdd"/> or <see cref="WhyNoJoin"/> refuses it.
	/// </summary>
	public static MsnEdit Join(MissionDocument document, short group, MechRosterEntry144 herc, out short mech) {
		if ((WhyNoAdd(document) ?? WhyNoJoin(document, group)) is { } reason) {
			throw new InvalidOperationException(reason);
		}

		return NewGroups.Join(document, MechRow, group, herc, out mech);
	}

	/// <summary>
	/// An order, unconditioned and drawing no variant: <paramref name="verb"/> on <paramref name="route"/>, with subject
	/// <paramref name="subject"/> of <paramref name="subjectKind"/>, in formation 0, naming no point (422 of the herc groups'
	/// 453 orders, 80 of the flyer groups' 81) and no action that ends it (445 of 453, 80 of 81).
	/// </summary>
	public static MissionOrder22 Order(short verb, short route, short subjectKind, short subject) => new() {
		GUID = -1, ConditionRef = -1, VariantKey = -1, CompoundConditionPartner = -1, Verb = verb, FormationId = NewGroups.Formation,
		PointRef = -1, RouteRef = route, SubjectKind = subjectKind, SubjectRef = subjectKind == -1 ? (short)-1 : subject,
		ActionRef = -1,
	};

	// A fit of the weapons given from slot 0, every later slot -1.
	private static short[] Fit(params short[] weapons) {
		var fit = NewGroups.Unset(10);
		weapons.CopyTo(fit, 0);
		return fit;
	}

	// Ammunition types given from slot 0, every later slot the filler 5.
	private static short[] Ammo(params short[] types) {
		var ammo = Filler();
		types.CopyTo(ammo, 0);
		return ammo;
	}

	private static short[] Filler() => Enumerable.Repeat((short)5, 10).ToArray();
}
