using HercWorks.Core.Data.File.Msn.Script;
using HercWorks.Core.Io.Transform.Common;

namespace HercWorks.Core.Data.File.Msn;

/// <summary>
/// Adds and removes the player's squadmates in a <see cref="MissionDocument"/>, as record edits. The squad is group 0, the
/// row-16 GUID exported as block 11's record 0: its first member is the player's machine, and the members set in an
/// unbroken run from its second slot are the squadmates, one squad position each, in slot order, which is how the shell's
/// squad build reads them (<c>Squad_SetPositionsInPlay</c> (<c>004102ff</c>),
/// docs/retail/shell/main-menu.md#starting-a-practice-mission). So an add names the new record in the slot just past the
/// run, and a remove moves every later squadmate up a slot, keeping the run unbroken.
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

	private const int MechBlock = 7;
	private const int MechRow = 12;
	private const int SquadBlock = 11;
	private const short MechRoster = 0;

	/// <summary>
	/// The squad positions <paramref name="script"/>'s group 0 gives, the player's among them:
	/// <see cref="MissionGenerator.SquadPositions"/> read back from the generated group's 20 member slots. Squadmate
	/// <c>k</c>, from 1, is member slot <c>k</c>.
	/// </summary>
	public static int Positions(ScriptDat script) {
		if (script.Groups.Length == 0) {
			return 1;
		}

		var refs = script.Groups[0].MemberRefs;
		int positions = 1;
		while (positions < refs.Length && refs[positions] != -1) {
			positions++;
		}

		return positions;
	}

	/// <summary>Why a squadmate cannot be added to <paramref name="document"/>, or null when one can.</summary>
	public static string? WhyNoAdd(MissionDocument document) {
		var script = document.Script;
		if (script.Groups.Length == 0 || document.GuidOf(SquadBlock, 0) == -1) {
			return "The mission has no group 0, the player's squad.";
		}

		if (script.Groups[0].MemberKind != MechRoster) {
			return "Group 0 is not a group of Hercs.";
		}

		int positions = Positions(script);
		if (positions - 1 >= MaxSquadmates) {
			return $"The squad already has {MaxSquadmates} squadmates, one in each squad position beside the player's.";
		}

		// A member set past the slot the add fills joins the run with it.
		int after = positions + 1;
		while (after < script.Groups[0].MemberRefs.Length && script.Groups[0].MemberRefs[after] != -1) {
			after++;
		}

		return after - 1 > MaxSquadmates
			? $"Group 0 references a Herc in member slot {positions + 1}, which a squadmate in slot {positions} would join to the squad, past its {MaxSquadmates} squadmates."
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

		short squad = document.GuidOf(SquadBlock, 0);
		int slot = Positions(document.Script);
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
	/// player's machine or another squadmate's, or one whose delete would leave group 0 with no member.
	/// </summary>
	public static (short Guid, RosterDeleteCheck Check)? Check(MissionDocument document, int member) {
		var script = document.Script;
		int positions = Positions(script);
		if (member < 1 || member >= positions) {
			return null;
		}

		var refs = script.Groups[0].MemberRefs;
		short guid = document.GuidOf(MechBlock, refs[member]);
		bool shared = Enumerable.Range(0, positions)
			.Any(other => other != member && document.GuidOf(MechBlock, Math.Max((int)refs[other], 0)) == guid);
		if (guid == -1 || shared) {
			return null;
		}

		var check = RosterDeletion.Check(document, MechRow, guid);
		short squad = document.GuidOf(SquadBlock, 0);
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
			throw new InvalidOperationException($"Group 0's member slot {member} is not a squadmate of its own.");
		}

		if (check.Needed) {
			throw new InvalidOperationException($"Squadmate {member} is referenced or needed by the mission.");
		}

		var script = document.Script;
		short squad = document.GuidOf(SquadBlock, 0);
		int positions = Positions(script);
		var later = Enumerable.Range(member + 1, positions - member - 1)
			.Select(slot => document.GuidOf(MechBlock, script.Groups[0].MemberRefs[slot]))
			.ToList();
		return document.Change(() => {
			for (int slot = member; slot < positions - 1; slot++) {
				document.SetField(MsnField.GroupMember(slot), squad, later[slot - member]);
			}

			document.SetField(MsnField.GroupMember(positions - 1), squad, -1);
			RosterDeletion.Take(document, MechRow, guid, check);
		});
	}

	/// <summary>
	/// A squad Herc of chassis <paramref name="chassis"/> with the weapons <paramref name="weapons"/> and ammunition
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
}
