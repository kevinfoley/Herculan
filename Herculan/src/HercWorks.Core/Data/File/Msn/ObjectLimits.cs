using HercWorks.Core.Data.File.Msn.Script;

namespace HercWorks.Core.Data.File.Msn;

/// <summary>
/// How many objects a mission's layout puts in DBSIM's simulation, in every version of the mission at once, against the objects
/// its per-object tables hold (<see cref="ScriptDatLint.ObjectCap"/>): what the editor's readout shows and what the mission check
/// reports (<see cref="MissionCheckKind.ObjectsFull"/>). <see cref="ScriptDatLint.Objects"/> counts the same objects in one
/// generated <c>script.dat</c>. Every herc, flyer and structure a group outside the squad places counts, whatever its condition,
/// as <see cref="RosterLimits"/> counts a roster, and the squad as its positions (<see cref="SquadRoster.Positions"/>).
/// </summary>
public static class ObjectLimits {
	private const int MechRow = 12;
	private const int BaseRow = 14;

	/// <summary>The objects <paramref name="layout"/> puts in the simulation: the squad's machines and every object of a group.</summary>
	public static int Count(MissionLayout layout) {
		short squad = SquadRoster.Squad(layout);
		int objects = Enumerable.Range(MechRow, BaseRow - MechRow + 1)
			.Sum(row => layout.Entries(row).Count(entry => entry.Presence.Group != -1 && entry.Presence.Group != squad));
		return objects + SquadRoster.Positions(layout);
	}

	/// <summary>The objects DBSIM's per-object tables hold.</summary>
	public static int Cap => ScriptDatLint.ObjectCap;
}
