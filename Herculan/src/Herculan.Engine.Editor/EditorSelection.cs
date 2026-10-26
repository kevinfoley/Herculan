using HercWorks.Core.Data.File.Msn;

namespace Herculan.Engine.Editor;

/// <summary>
/// What the editor has selected. Most of a mission is not an object in the world — a trigger area,
/// an action, a route — so the selection is one of several kinds, each addressed the way the
/// mission file addresses it: a GUID of its row, or an objective by its index in row 17; an object by the roster record it
/// stands for. A selection so names a <c>.MSN</c> record, and survives every edit that keeps that record
/// (<see cref="InFile"/>); every view reads that record from the layout.
/// </summary>
internal abstract record EditorSelection {
	/// <summary>
	/// Whether <paramref name="mission"/> still has the record <paramref name="selection"/> names: a group, trigger area,
	/// action, timer, route or condition by a record of its row (16, 9, 10, 11, 8 or 1) carrying its GUID, since GUIDs are
	/// per row, and an object by a record of its roster row (12, 13 or 14); an objective by a row-17 record at its index.
	/// Every other kind answers false.
	/// </summary>
	public static bool InFile(MissionFile mission, EditorSelection selection) => selection switch {
		GroupSelection { Guid: var guid } => Carries(mission.Groups, guid),
		AreaSelection { Guid: var guid } => Carries(mission.TriggerAreas, guid),
		ActionSelection { Guid: var guid } => Carries(mission.Actions, guid),
		TimerSelection { Guid: var guid } => Carries(mission.ActionTimers, guid),
		RouteSelection { Guid: var guid } => Carries(mission.WaypointGroups, guid),
		ConditionSelection { Guid: var guid } => Carries(mission.Conditions, guid),
		ObjectSelection { Row: 12, Guid: var guid } => Carries(mission.Mechs, guid),
		ObjectSelection { Row: 13, Guid: var guid } => Carries(mission.Flyers, guid),
		ObjectSelection { Row: 14, Guid: var guid } => Carries(mission.Bases, guid),
		ObjectiveSelection { Record: var record } => record >= 0 && record < (mission.Objectives?.Length ?? 0),
		_ => false
	};

	// GUID -1 is a variant alternative's, which no selection names.
	private static bool Carries(IEnumerable<MapObject>? row, short guid) =>
		guid != -1 && row?.Any(record => record.GUID == guid) == true;
}

/// <summary>
/// One placed object, by the roster record it stands for: its row (12 a herc, the squad's included, 13 a flyer, 14 a
/// structure or ground vehicle) and GUID (<see cref="MissionIndex.ObjectOf(ObjectSelection)"/>).
/// </summary>
internal sealed record ObjectSelection(int Row, short Guid) : EditorSelection;

/// <summary>One mission group, by its row-16 GUID.</summary>
internal sealed record GroupSelection(short Guid) : EditorSelection;

/// <summary>One trigger area, by its row-9 GUID.</summary>
internal sealed record AreaSelection(short Guid) : EditorSelection;

/// <summary>One mission action, by its row-10 GUID.</summary>
internal sealed record ActionSelection(short Guid) : EditorSelection;

/// <summary>
/// One route, by its row-8 GUID, and optionally one of its waypoints, by its index in the route's
/// <see cref="PlannedRoute.Points"/> (-1 for the route as a whole).
/// </summary>
internal sealed record RouteSelection(short Guid, int Waypoint = -1) : EditorSelection;

/// <summary>One mission objective, by its index in row 17, which has no GUID.</summary>
internal sealed record ObjectiveSelection(int Record) : EditorSelection;

/// <summary>One action timer, by its row-11 GUID.</summary>
internal sealed record TimerSelection(short Guid) : EditorSelection;

/// <summary>One condition, by its row-1 GUID.</summary>
internal sealed record ConditionSelection(short Guid) : EditorSelection;

/// <summary>
/// The editor's one current selection, shared by everything that can change it: a click in the
/// viewport, the outliner and the links in the Properties panel.
/// </summary>
internal sealed class SelectionState {
	public EditorSelection? Current { get; set; }

	/// <summary>Raised by <see cref="Focus"/>, for the camera to frame the selection.</summary>
	public event Action<EditorSelection>? FocusRequested;

	/// <summary>Selects <paramref name="selection"/> and asks for the view to frame it, as a double-click does.</summary>
	public void Focus(EditorSelection selection) {
		Current = selection;
		FocusRequested?.Invoke(selection);
	}

	/// <summary>
	/// Whether <paramref name="selection"/> is the current selection. A route counts as selected when
	/// any of its waypoints is.
	/// </summary>
	public bool Is(EditorSelection selection) => selection switch {
		RouteSelection { Waypoint: < 0 } route => Current is RouteSelection current && current.Guid == route.Guid,
		_ => Equals(Current, selection)
	};
}
