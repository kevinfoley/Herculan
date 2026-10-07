using HercWorks.Core.Data.File.Sav;

namespace Herculan.Engine.Shell;

/// <summary>
/// The player and the three squad members the player structure points at: their bays, their
/// squad positions and who is on strength (the <c>Squad_*</c> functions), and the debrief's
/// replacement of a lost squad member.
/// </summary>
public sealed partial class ShellHangar {
	/// <summary>The squad block's shape: three squads of twelve pilot records, one squad member drawn from each.</summary>
	private const int SquadCount = 3;
	private const int PilotsPerSquad = 12;
	private readonly List<ShellBayPilot> _squad = new();

	/// <summary>
	/// <c>Squad_TakeMember</c> (<c>0040fb4f</c>) as the debrief uses it to replace squad member
	/// <paramref name="member"/>: the squad's next record by its cursor becomes the member, the cursor steps
	/// modulo 12, and the record is reset by <c>Pilot_SetDefaults</c> (<c>0040fd17</c>) — no bay, off strength,
	/// no position, condition 100 and every counter 0. The debrief then takes one off
	/// <see cref="MachinesOnStrength"/>. The record written is <paramref name="save"/>'s, the game this hangar
	/// was read from; the pilot it replaces keeps its own record as it was.
	/// </summary>
	internal void ReplaceSquadMember(int member, PlayerSave save) {
		if (member < 0 || member >= _squad.Count || save.Squadmates == null) {
			return;
		}

		int squad = _squadRecords[member] / PilotsPerSquad;
		short next = save.SquadTailAndPlayerHead[SquadCount + squad];
		save.SquadTailAndPlayerHead[squad] = next;
		save.SquadTailAndPlayerHead[SquadCount + squad] = (short)((next + 1) % PilotsPerSquad);
		int record = squad * PilotsPerSquad + next;
		if (save.Squadmates.ElementAtOrDefault(record) is not { } pilot) {
			return;
		}

		pilot.Bay = -1;
		pilot.OnStrength = 0;
		pilot.SquadPosition = -1;
		pilot.Condition = 100;
		pilot.HercKills = pilot.FlyerKills = pilot.BaseKills = 0;
		pilot.TotalHercKills = pilot.TotalFlyerKills = pilot.TotalBaseKills = 0;
		pilot.MissionsFlown = 0;
		_squad[member] = Pilot(pilot)!;
		_squadRecords[member] = record;
		MachinesOnStrength--;
	}

	/// <summary>Which of the save's 36 squad records squad member <paramref name="member"/> is.</summary>
	internal int SquadRecordIndex(int member) => _squadRecords[member];

	/// <summary>The player's own pilot record, embedded in the player structure at <c>+0x04</c>.</summary>
	public ShellBayPilot? Player { get; private set; }

	/// <summary>
	/// The three squad members the player structure points at from <c>+0x3f</c>, in pointer order —
	/// the pilots the crew screen offers as <c>Available Pilots</c>. Fewer than three only when the
	/// save's squad block is short.
	/// </summary>
	public IReadOnlyList<ShellBayPilot> SquadMembers => _squad;

	/// <summary>
	/// The player structure's leading <c>int16</c>, <c>SquadPositionsInPlay</c> (<c>00482a78</c>): how many squad positions,
	/// counting the player's as position 0, are in play. The auto-repair pass and the
	/// <c>player.mec</c> export both bound their per-position loops by it, a squad member counts as on
	/// strength only in a position below it (<c>Squad_UpdateOnStrength</c>, <c>00410366</c>), and the crew screen draws the rows
	/// below it lit.
	/// </summary>
	public int SquadPositions { get; private set; }

	/// <summary>
	/// <c>00482a7a</c>, the player structure's second short: how many machines are on strength, the
	/// player's included. <see cref="SetOnStrength"/> moves it with the squad members' <c>+0x24</c> bytes.
	/// </summary>
	public int MachinesOnStrength { get; private set; }

	/// <summary>
	/// <c>Squad_MemberAtPosition</c> (<c>004102d6</c>) — the index among <see cref="SquadMembers"/> of
	/// the member whose <c>+0x27</c> is <paramref name="position"/>, or <c>-1</c>. It tests the three in
	/// pointer order and takes the first.
	/// </summary>
	public int SquadMemberIndexAt(int position) {
		for (int member = 0; member < _squad.Count; member++) {
			if (_squad[member].SquadPosition == position) {
				return member;
			}
		}

		return -1;
	}

	/// <summary>The squad member at <paramref name="position"/>, as <see cref="SquadMemberIndexAt"/> finds them, or null.</summary>
	public ShellBayPilot? SquadMemberAt(int position) =>
		SquadMemberIndexAt(position) is >= 0 and var member ? _squad[member] : null;

	/// <summary>
	/// Writes a pilot's bay, <c>+0x22</c> — for the player <c>Player_SetBay</c> (<c>0040e6c8</c>), which writes
	/// <c>00482a9e</c>; for a squad member the plain store the crew screen makes before
	/// <see cref="UpdateOnStrength"/>.
	/// </summary>
	public static void SetBay(ShellBayPilot pilot, int bay) => pilot.Bay = bay;

	/// <summary><c>Squad_SetMemberPosition</c> (<c>004102be</c>) — writes squad member <paramref name="member"/>'s position, <c>+0x27</c>.</summary>
	public void SetSquadPosition(int member, int position) {
		if (member >= 0 && member < _squad.Count) {
			_squad[member].SquadPosition = position;
		}
	}

	/// <summary>
	/// <c>Squad_SetMemberBay(member, -1)</c> (<c>0040e6d7</c>) — takes squad member <paramref name="member"/> out of any bay and
	/// off strength. That is the only way the crew screen calls it.
	/// </summary>
	public void UnassignSquadMemberBay(int member) {
		if (member >= 0 && member < _squad.Count) {
			_squad[member].Bay = -1;
			SetOnStrength(member, false);
		}
	}

	/// <summary>
	/// <c>Squad_UpdateOnStrength</c> (<c>00410366</c>) — the member at <paramref name="position"/> is on
	/// strength when the position is in play and they have a bay, and off it otherwise.
	/// </summary>
	public void UpdateOnStrength(int position) {
		int member = SquadMemberIndexAt(position);
		if (member != -1) {
			SetOnStrength(member, position < SquadPositions && _squad[member].Bay != -1);
		}
	}

	/// <summary>
	/// <c>Squad_SetOnStrength</c> (<c>00410327</c>) — writes a member's on-strength byte and moves <see cref="MachinesOnStrength"/>
	/// by one when the byte actually changes.
	/// </summary>
	private void SetOnStrength(int member, bool onStrength) {
		var pilot = _squad[member];
		if (onStrength && !pilot.OnStrength) {
			MachinesOnStrength++;
		} else if (!onStrength && pilot.OnStrength) {
			MachinesOnStrength--;
		}

		pilot.OnStrength = onStrength;
	}

	/// <summary>
	/// <c>Squad_PilotForBay(00482a78, slot)</c> (<c>00410220</c>) — the pilot assigned to a bay, or null. It searches exactly
	/// four records: the player's own, then the three squad members the player structure points at
	/// (<c>+0x3f</c>). Each of those pointers is set on load to record <c>DAT_00483b48[k]</c> of squad
	/// <c>k</c>, so a pilot in the squad block who is not one of the three is never found here.
	/// </summary>
	public ShellBayPilot? PilotFor(int slot) {
		if (slot < 0) {
			return null;
		}

		if (Player?.Bay == slot) {
			return Player;
		}

		foreach (var pilot in _squad) {
			if (pilot.Bay == slot) {
				return pilot;
			}
		}

		return null;
	}

	/// <summary>
	/// <c>Squad_SetPositionsInPlay</c> (<c>004102ff</c>) — how many squad positions are in play, then <see cref="UpdateOnStrength"/> for
	/// positions 1 to 3.
	/// </summary>
	public void SetPositionsInPlay(int positions) {
		SquadPositions = positions;
		for (int position = 1; position < 4; position++) {
			UpdateOnStrength(position);
		}
	}

	/// <summary>Which of the save's 36 squad records each of <see cref="SquadMembers"/> was read from.</summary>
	private readonly List<int> _squadRecords = new();
}
