namespace HercWorks.Core.Data.File.Msn;

/// <summary>
/// The player's squad in a <see cref="MissionDocument"/>'s layout, and its squadmates added and removed as record edits. The
/// squad is group 0, the first row-16 entry of the layout (<see cref="Squad(MissionLayout)"/>): its first member is the
/// player's HERC, and the members set in an unbroken run from its second slot are the squadmates, one squad position each,
/// in slot order, which is how the shell's squad build reads them (<c>Squad_SetPositionsInPlay</c> (<c>004102ff</c>),
/// docs/retail/shell/main-menu.md#starting-a-practice-mission). So an add names the new record in the slot just past the
/// run, and a remove moves every later squadmate up a slot, keeping the run unbroken. A member slot naming a GUID no row-12
/// record carries counts as unset, as the load resolves it to none.
///
/// <para>The squad holds at most <see cref="MaxSquadmates"/>: the player structure points at three squad members, and the
/// squad build gives a machine to squad member <c>i</c> for slot <c>i + 1</c> only while there is such a member
/// (docs/retail/shell/squad-and-crew.md#the-rows: the player's row and three squad positions).</para>
///
/// <para>A remove is refused as <see cref="RosterDeletion.Check"/> refuses a roster delete: while an objective, an action
/// or an order names the squadmate, or it fires an action of its own; its records then go as a roster delete's do
/// (<see cref="RosterDeletion.Take"/>).</para>
/// </summary>
public static class SquadRoster {
	/// <summary>Squadmates beside the player: the three squad members the shell's squad build fits machines for.</summary>
	public const int MaxSquadmates = 3;

	private const int MechRow = 12;
	private const int GroupRow = 16;
	private const short MechRoster = 0;

	/// <summary>The row-16 GUID of group 0, the player's squad: the layout's first group; <c>-1</c> when it has none.</summary>
	public static short Squad(MissionLayout layout) => layout.Entries(GroupRow) is { Count: > 0 } groups ? groups[0].Guid : (short)-1;

	/// <inheritdoc cref="Squad(MissionLayout)"/>
	public static short Squad(MissionDocument document) => Squad(document.Layout);

	/// <summary>
	/// The row-12 GUID group 0's member slot <paramref name="slot"/> names, as its base gives it, or <c>-1</c> when it names
	/// none, a GUID no row-12 record carries, or the layout has no group 0 of hercs.
	/// </summary>
	public static short Member(MissionLayout layout, int slot) {
		if (Group(layout) is not { } group || slot < 0 || slot >= group.MemberRefs.Length) {
			return -1;
		}

		short guid = group.MemberRefs[slot];
		return guid != -1 && layout.Entry(MechRow, guid) != null ? guid : (short)-1;
	}

	/// <summary>
	/// The squad positions group 0 gives, the player's among them: 1, plus each member set in an unbroken run from its second
	/// slot, as <see cref="Io.Transform.Common.MissionGenerator.SquadPositions"/> counts them. Squadmate <c>k</c>, from 1, is
	/// member slot <c>k</c>.
	/// </summary>
	public static int Positions(MissionLayout layout) {
		int slots = Group(layout)?.MemberRefs.Length ?? 0;
		int positions = 1;
		while (positions < slots && Member(layout, positions) != -1) {
			positions++;
		}

		return positions;
	}

	/// <summary>Why a squadmate cannot be added to <paramref name="document"/>, or null when one can.</summary>
	public static string? WhyNoAdd(MissionDocument document) {
		var layout = document.Layout;
		if (Squad(layout) == -1) {
			return "The mission has no player's squad.";
		}

		if (Group(layout) is not { } group) {
			return "The player's squad is not a group of HERCs.";
		}

		int positions = Positions(layout);
		if (positions - 1 >= MaxSquadmates) {
			return $"The squad already has {MaxSquadmates} squadmates, one in each squad position beside the player's.";
		}

		// A member set past the slot the add fills joins the run with it.
		int after = positions + 1;
		while (after < group.MemberRefs.Length && Member(layout, after) != -1) {
			after++;
		}

		return after - 1 > MaxSquadmates
			? $"A HERC after a gap in the squad's list would join the squad with a new squadmate, past its {MaxSquadmates} squadmates."
			: null;
	}

	/// <summary>
	/// Appends <paramref name="record"/> to row 12 under a new GUID, <paramref name="guid"/>, and names it in group 0's
	/// member slot just past the squadmates, as one <see cref="MissionDocument.Change"/>. Throws
	/// <see cref="InvalidOperationException"/> when <see cref="WhyNoAdd"/> refuses it.
	/// </summary>
	public static MsnEdit Add(MissionDocument document, MechRosterEntry144 record, out short guid) {
		if (WhyNoAdd(document) is { } reason) {
			throw new InvalidOperationException(reason);
		}

		short squad = Squad(document);
		int slot = Positions(document.Layout);
		short added = -1;
		var change = document.Change(() => {
			document.Append(MechRow, record, out added);
			document.SetField(MsnField.GroupMember(slot), squad, added);
		});
		guid = added;
		return change;
	}

	/// <summary>
	/// The row-12 GUID of the squadmate in group 0's member slot <paramref name="member"/>, from 1, and what deleting it
	/// would take and what refuses it; null for a slot that is not a squadmate's, a squadmate whose record is also the
	/// player's HERC or another squadmate's, or one whose delete would leave group 0 with no member.
	/// </summary>
	public static (short Guid, RosterDeleteCheck Check)? Check(MissionDocument document, int member) {
		var layout = document.Layout;
		int positions = Positions(layout);
		if (member < 1 || member >= positions) {
			return null;
		}

		short guid = Member(layout, member);
		bool shared = Enumerable.Range(0, positions).Any(other => other != member && Member(layout, other) == guid);
		if (guid == -1 || shared) {
			return null;
		}

		var check = RosterDeletion.Check(document, MechRow, guid);
		short squad = Squad(layout);
		return check.EmptiedGroups.Any(emptied => emptied.Group == squad) ? null : (guid, check);
	}

	/// <summary>
	/// Removes the squadmate in group 0's member slot <paramref name="member"/> as one <see cref="MissionDocument.Change"/>:
	/// each later squadmate moves up a slot in the record supplying it, the last slot of the run is unset in every record
	/// of the group, and the squadmate's records go as a roster delete's do. Throws <see cref="InvalidOperationException"/>
	/// when <see cref="Check(MissionDocument, int)"/> finds no squadmate there or the mission names or needs it.
	/// </summary>
	public static MsnEdit Remove(MissionDocument document, int member) {
		if (Check(document, member) is not var (guid, check)) {
			throw new InvalidOperationException($"Squadmate {member} is not one of its own.");
		}

		if (check.Needed) {
			throw new InvalidOperationException($"Squadmate {member} is referenced or needed by the mission.");
		}

		var layout = document.Layout;
		short squad = Squad(layout);
		int positions = Positions(layout);
		var later = Enumerable.Range(member + 1, positions - member - 1).Select(slot => Member(layout, slot)).ToList();
		return document.Change(() => {
			for (int slot = member; slot < positions - 1; slot++) {
				document.SetField(MsnField.GroupMember(slot), squad, later[slot - member]);
			}

			document.SetField(MsnField.GroupMember(positions - 1), squad, -1);
			RosterDeletion.Take(document, MechRow, guid, check);
		});
	}

	/// <summary>
	/// A squad HERC of chassis <paramref name="chassis"/> with the weapons <paramref name="weapons"/> and ammunition
	/// types <paramref name="guidance"/> of its ten slots, as a GUID-only record (<see cref="MissionDocument.Append"/> gives
	/// it one), unconditioned and drawing no variant. Every other field is <c>TRAIN1.MSN</c>'s player record's, mech 46
	/// with the alternative it draws resolved: radar active, cruise speed 0, no point or heading of its own, no
	/// out-of-action writes, firing no action, pristine.
	/// </summary>
	public static MechRosterEntry144 Machine(short chassis, short[] weapons, short[] guidance) => new() {
		GUID = -1, ConditionRef = -1, VariantKey = -1, CompoundConditionPartner = -1, AiRadarActive = 1, AiCruiseSpeed = 0,
		DeadZone = new short[18], TypeIndex = chassis, WeaponRefs = weapons, PositionRef = -1, HeadingRef = -1, PairCount = 0,
		OutOfActionReport = Enumerable.Repeat((short)-1, 20).ToArray(), WeaponSecondary = guidance, Constant2 = 2,
		EngagementActionRef = -1, DefeatActionRef = -1, StartingCondition = 100,
	};

	// Group 0's base fields, when it is a group of hercs.
	private static MissionGroup164? Group(MissionLayout layout) =>
		layout.Entries(GroupRow) is { Count: > 0 } groups && groups[0].Fields is MissionGroup164 { MemberKind: MechRoster } group
			? group
			: null;
}
