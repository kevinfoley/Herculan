using HercWorks.Core.Data.File.Msn.Script;

namespace HercWorks.Core.Data.File.Msn;

/// <summary>
/// How many markers a mission's layout puts on the heads-down map, in every version of the mission at once, against the
/// markers DBSIM builds (<see cref="ScriptDatLint.MapMarkerCap"/>): what the editor's readout shows and what the mission check
/// reports (<see cref="MissionCheckKind.MapMarkersFull"/>). <see cref="ScriptDatLint.MapMarkers"/> counts the same markers in
/// one generated <c>script.dat</c>. Every herc, flyer and structure a group outside the squad places counts, whatever its
/// condition, as <see cref="RosterLimits"/> counts a roster, and the squad as its positions (<see cref="SquadRoster.Positions"/>).
/// </summary>
public static class MapMarkerLimits {
	private const int MechRow = 12;
	private const int BaseRow = 14;

	/// <summary>The markers <paramref name="layout"/> puts on the map: the squad's route markers, one per object, and the nav marker's.</summary>
	public static int Count(MissionLayout layout) {
		short squad = SquadRoster.Squad(layout);
		int objects = Enumerable.Range(MechRow, BaseRow - MechRow + 1)
			.Sum(row => layout.Entries(row).Count(entry => entry.Presence.Group != -1 && entry.Presence.Group != squad));
		return RouteMarkers(layout, squad) + objects + SquadRoster.Positions(layout) + 1;
	}

	/// <summary>The markers DBSIM builds.</summary>
	public static int Cap => ScriptDatLint.MapMarkerCap;

	// One per waypoint after the first of the route the squad's first order runs, at most ScriptDatLint.RouteMarkerCap.
	private static int RouteMarkers(MissionLayout layout, short squad) =>
		layout.Entry<MissionGroup164>(squad)?.Fields is { } group && group.OrderRefs.Length > 0
			&& layout.Entry<MissionOrder22>(group.OrderRefs[0])?.Fields is { } order
			&& layout.Entry<WaypointGroup>(order.RouteRef)?.Fields is { } route
			? Math.Clamp(route.Waypoints.Length - 1, 0, ScriptDatLint.RouteMarkerCap)
			: 0;
}
