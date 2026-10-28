namespace HercWorks.Core.Data.File.Msn;

/// <summary>
/// The row-6 points of a mission that nothing uses, and their removal: points no field names
/// (<see cref="MissionDocument.CountPointRefs"/>, every record whatever its condition), which an edit that removed whatever
/// stood on one can leave behind. Removing them brings the count DBSIM's spawn pass is limited to down
/// (<see cref="SkipArrayLimits"/>). Three kinds of unnamed point stay, since removing them would change the mission:
/// <list type="bullet">
/// <item>one a record of its GUID picks from a random table for, since removing it would move every later draw
/// (docs/herculan/handoff-editor-document.md#conditions-and-random-tables);</item>
/// <item>one a sum-flag point names by the low words of its X and Y, as <see cref="RouteEditing.RemoveLeftBehind"/> keeps it;</item>
/// <item>one on the play area's edge, the box of block 1's points as the layout has them, since the box frames the
/// heads-down map and sets the mission's boundary (docs/retail/simulation/mission-objectives.md#the-status--mission_status-004135e8).</item>
/// </list>
/// </summary>
public static class UnusedPoints {
	private const int PointRow = 6;

	/// <summary>The GUIDs of <paramref name="document"/>'s row-6 points nothing uses, in layout order.</summary>
	public static IReadOnlyList<short> Find(MissionDocument document) {
		var entries = document.Layout.Entries<MapPoint22>();
		if (entries.Count == 0) {
			return [];
		}

		int minX = entries.Min(entry => entry.Fields.X);
		int maxX = entries.Max(entry => entry.Fields.X);
		int minY = entries.Min(entry => entry.Fields.Y);
		int maxY = entries.Max(entry => entry.Fields.Y);
		var records = document.Mission.Points ?? [];
		return entries
			.Where(entry => entry.Fields.X != minX && entry.Fields.X != maxX && entry.Fields.Y != minY && entry.Fields.Y != maxY)
			.Select(entry => entry.Guid)
			.Where(point => document.CountPointRefs(point) == 0
				&& records.Where(record => record.GUID == point).All(record => record.RandomTableKey == -1)
				&& !records.Any(record => record.SumFlag != 0 && ((short)record.X == point || (short)record.Y == point)))
			.ToList();
	}

	/// <summary>
	/// Removes every record of each point <see cref="Find"/> answers, as one <see cref="MissionDocument.Change"/>. Throws
	/// <see cref="InvalidOperationException"/> when it answers none.
	/// </summary>
	public static MsnEdit Remove(MissionDocument document) {
		var unused = Find(document);
		if (unused.Count == 0) {
			throw new InvalidOperationException("No position is unused.");
		}

		return document.Change(() => {
			foreach (short point in unused) {
				document.Remove(PointRow, point);
			}
		});
	}
}
