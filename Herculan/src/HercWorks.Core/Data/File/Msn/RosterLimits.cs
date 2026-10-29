using HercWorks.Core.Data.File.Msn.Script;

namespace HercWorks.Core.Data.File.Msn;

/// <summary>
/// How many of DBSIM's per-roster slots a mission's layout takes in every version of the mission at once, and the refusal an
/// add past them gets: what the adds refuse by (<see cref="HercGroups.WhyNoAdd"/>, <see cref="FlyerGroups.WhyNoAdd"/>,
/// <see cref="VehicleGroups.WhyNoAdd"/> and the editor's structure and squadmate adds) and what the mission check reports
/// (<see cref="MissionCheckKind.RosterFull"/>). Every GUID of the roster's row counts, whatever its condition; block 7 also
/// holds one machine per squad position group 0 gives (<see cref="SquadRoster.Positions"/>), which <c>player.mec</c> brings
/// (docs/retail/formats/script-dat.md#the-two-pass-read--and-what-it-means-for-dbsim-keeps). The caps are
/// <see cref="ScriptDatLint.MechSlotCap"/> and its siblings.
/// </summary>
public static class RosterLimits {
	private const int MechRow = 12;
	private const int FlyerRow = 13;
	private const int BaseRow = 14;

	/// <summary>The slots roster row <paramref name="row"/> (12 hercs, 13 flyers, 14 structures and ground vehicles) takes.</summary>
	public static int Slots(MissionLayout layout, int row) => row switch {
		MechRow => layout.Entries(MechRow).Count + SquadRoster.Positions(layout),
		FlyerRow or BaseRow => layout.Entries(row).Count,
		_ => throw new ArgumentOutOfRangeException(nameof(row), row, "Rows 12 to 14 are the rosters."),
	};

	/// <summary>The slots DBSIM holds for roster row <paramref name="row"/>.</summary>
	public static int Cap(int row) => row switch {
		MechRow => ScriptDatLint.MechSlotCap,
		FlyerRow => ScriptDatLint.FlyerSlotCap,
		BaseRow => ScriptDatLint.BaseSlotCap,
		_ => throw new ArgumentOutOfRangeException(nameof(row), row, "Rows 12 to 14 are the rosters."),
	};

	/// <summary>
	/// Why <paramref name="taking"/> more of roster row <paramref name="row"/>'s slots cannot be taken in
	/// <paramref name="layout"/>, or null when they can: always null for a mission meant for HERCULAN alone
	/// (<see cref="MissionLayout.HerculanOnly"/>), whose simulation has no slot arrays.
	/// </summary>
	public static string? WhyNoRoom(MissionLayout layout, int row, int taking) =>
		!layout.HerculanOnly && Slots(layout, row) + taking > Cap(row) ? $"At maximum {Noun(row)} limit ({Cap(row)})." : null;

	/// <summary>What a slot of roster row <paramref name="row"/> holds, as the refusals name it: HERC, flyer or structure.</summary>
	public static string Noun(int row) => row switch {
		MechRow => "HERC",
		FlyerRow => "flyer",
		_ => "structure",
	};
}
