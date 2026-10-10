using Herculan.Engine.Scene;

namespace Herculan.Engine.Host.Editor;

/// <summary>
/// What the editor has selected. Most of a mission is not an object in the world — a trigger area,
/// an action, a route — so the selection is one of several kinds, each addressed the way the
/// mission file addresses it.
/// </summary>
internal abstract record EditorSelection;

/// <summary>One placed object.</summary>
internal sealed record ObjectSelection(SceneObject Object) : EditorSelection;

/// <summary>One mission group, by <c>script.dat</c> block-11 record index.</summary>
internal sealed record GroupSelection(int Group) : EditorSelection;

/// <summary>One trigger area, by block-4 record index.</summary>
internal sealed record AreaSelection(int Area) : EditorSelection;

/// <summary>One mission action, by block-5 record index.</summary>
internal sealed record ActionSelection(int Action) : EditorSelection;

/// <summary>
/// One route, by block-3 record index, and optionally one of its waypoints (-1 for the route as a
/// whole).
/// </summary>
internal sealed record RouteSelection(int Route, int Waypoint = -1) : EditorSelection;

/// <summary>One mission objective, by block-12 record index.</summary>
internal sealed record ObjectiveSelection(int Objective) : EditorSelection;

/// <summary>One action timer, by block-6 record index.</summary>
internal sealed record TimerSelection(int Timer) : EditorSelection;

/// <summary>
/// The editor's one current selection, shared by everything that can change it: a click in the
/// viewport, the outliner and the links in the Properties panel.
/// </summary>
internal sealed class SelectionState {
	public EditorSelection? Current { get; set; }

	/// <summary>
	/// Whether <paramref name="selection"/> is the current selection. A route counts as selected when
	/// any of its waypoints is.
	/// </summary>
	public bool Is(EditorSelection selection) => selection switch {
		RouteSelection { Waypoint: < 0 } route => Current is RouteSelection current && current.Route == route.Route,
		_ => Equals(Current, selection)
	};
}
