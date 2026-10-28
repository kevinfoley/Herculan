using HercWorks.Core.Data.File.Msn.Script;

namespace HercWorks.Core.Data.File.Msn;

/// <summary>
/// How many records of rows 6, 7 and 9 a mission's layout takes in every version of the mission at once, against what DBSIM's
/// spawn pass reads through its skip array (<see cref="ScriptDatLint.SkipArrayBytes"/>): what the editor's readout shows and
/// what the mission check reports (<see cref="MissionCheckKind.SkipArrayFull"/>). Every GUID of the row counts, whatever its
/// condition, as <see cref="RosterLimits"/> counts a roster. Row 8's limit is on one route's waypoint list, not on the routes,
/// so it is <see cref="Cap"/>'s alone.
/// </summary>
public static class SkipArrayLimits {
	private const int PointRow = 6;
	private const int RouteRow = 8;
	private const int AreaRow = 9;

	/// <summary>The records row <paramref name="row"/> (6 positions, 7 headings, 9 trigger areas) takes.</summary>
	public static int Count(MissionLayout layout, int row) => row is >= PointRow and <= AreaRow and not RouteRow
		? layout.Entries(row).Count
		: throw new ArgumentOutOfRangeException(nameof(row), row, "Rows 6, 7 and 9 are counted whole.");

	/// <summary>The most records of row <paramref name="row"/> the skip array holds; for row 8, the waypoints of one route.</summary>
	public static int Cap(int row) => row is >= PointRow and <= AreaRow
		? ScriptDatLint.SkipArrayCap((ScriptBlock)(row - 5))!.Value
		: throw new ArgumentOutOfRangeException(nameof(row), row, "Rows 6 to 9 are the skipped blocks.");
}
