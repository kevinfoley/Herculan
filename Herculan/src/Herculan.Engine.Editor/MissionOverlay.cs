using System.Numerics;
using HercWorks.Core.Data.File.Msn;
using Herculan.Engine.Cockpit;
using Herculan.Engine.Numerics;
using Herculan.Engine.Platform;
using Herculan.Engine.Render;
using Herculan.Engine.Sim;
using Herculan.Engine.World;
using ImGuiNET;

namespace Herculan.Engine.Editor;

/// <summary>
/// The mission's non-object features drawn over the scene: trigger areas, routes, base pads and the
/// mission box with its two margins, as lines laid on the terrain, plus their ImGui labels, the waypoint pick and the
/// tint over a selected area's ground. Every feature is the layout's (<see cref="MissionIndex"/>), by GUID, a conditional
/// one included, so a route or area the mission as opened leaves out is drawn and picked like any other.
///
/// <para>Every line is drawn twice: dim with depth testing off, so a feature behind a hill still
/// shows where it is, then at full colour with depth testing on, so the part in view reads as
/// solid.</para>
/// </summary>
internal sealed class MissionOverlay {
	private static readonly Vector3 PlayerTriggerColor = new(1f, 0.62f, 0.1f);
	private static readonly Vector3 GroupTriggerColor = new(0.95f, 0.3f, 0.3f);
	private static readonly Vector3 TargetTriggerColor = new(0.85f, 0.45f, 1f);
	private static readonly Vector3 UntestedTriggerColor = new(0.5f, 0.55f, 0.62f);
	private static readonly Vector3 RouteColor = new(0.3f, 0.85f, 1f);
	private static readonly Vector3 PlayerRouteColor = new(0.4f, 1f, 0.45f);
	private static readonly Vector3 UnwalkedRouteColor = new(0.5f, 0.55f, 0.62f);
	private static readonly Vector3 BoxColor = new(0.92f, 0.92f, 0.95f);
	private static readonly Vector3 MapExtentColor = new(0.55f, 0.6f, 0.75f);
	private static readonly Vector3 AbortColor = new(0.9f, 0.25f, 0.25f);
	private static readonly Vector3 PadColor = new(0.85f, 0.8f, 0.55f);

	/// <summary>The selection colour, matching the box drawn around a selected object.</summary>
	public static readonly Vector3 SelectedColor = new(1f, 0.85f, 0.1f);

	/// <summary>How much of its colour a line keeps where it is behind the terrain or an object.</summary>
	private const float HiddenBrightness = 0.35f;

	/// <summary>How tall the posts marking a selected area's corners are, in world units (about 60 m).</summary>
	public const int PostHeight = 10000;

	/// <summary>How close, in pixels on a 100% display, a click must land to a waypoint to pick it.</summary>
	private const float WaypointPickRadius = 8f;

	private readonly MissionIndex _index;
	private readonly DrapedLines _drape;
	private List<(PlannedArea Area, List<Vector3> Lines, Vector3 Color)> _areas = new();
	private List<(PlannedRoute Route, List<Vector3> Lines, Vector3 Color)> _routes = new();
	private readonly List<(List<Vector3> Lines, Vector3 Color)> _box = new();
	private readonly List<(MissionBasePad Pad, List<Vector3> Lines)> _pads = new();
	private MissionBox _boxShape;

	public MissionOverlay(MissionIndex index, DrapedLines drape) {
		_index = index;
		_drape = drape;
		Rebuild();
	}

	/// <summary>Lays every line again from the index, for an index built again (<see cref="SceneSync"/>).</summary>
	public void Rebuild() {
		Lay(keep: false);
		SetBox(_index.Box);
	}

	/// <summary>
	/// Lays again the route, area and pad lines the index now places elsewhere, as a drag's preview moves them; a route or
	/// area that stands where it stood keeps its lines.
	/// </summary>
	public void Replan() => Lay(keep: true);

	// Lays the route, area and pad lines from the index, keeping a feature's old lines when <keep> and it has not moved.
	private void Lay(bool keep) {
		var oldAreas = keep ? _areas.ToDictionary(entry => entry.Area.Guid) : null;
		_areas = new List<(PlannedArea, List<Vector3>, Vector3)>(_index.Areas.Count);
		foreach (var area in _index.Areas) {
			var lines = oldAreas?.GetValueOrDefault(area.Guid) is { Area.Area: var was } old && was == area.Area
				? old.Lines
				: AreaLines(area.Area);
			_areas.Add((area, lines, AreaColor(area)));
		}

		var oldRoutes = keep ? _routes.ToDictionary(entry => entry.Route.Guid) : null;
		_routes = new List<(PlannedRoute, List<Vector3>, Vector3)>(_index.Routes.Count);
		var playerRoute = _index.PlayerRoute;
		foreach (var route in _index.Routes) {
			var lines = oldRoutes?.GetValueOrDefault(route.Guid) is { Route.Points: var was } old && was.SequenceEqual(route.Points)
				? old.Lines
				: RouteLines(route);
			var color = !route.Walked ? UnwalkedRouteColor : route.Guid == playerRoute?.Guid ? PlayerRouteColor : RouteColor;
			_routes.Add((route, lines, color));
		}

		_pads.Clear();
		foreach (var pad in _index.BasePads) {
			_pads.Add((pad, PadLines(pad)));
		}
	}

	// The mission box drawn, with its two margins: block 1's extent, which an edit to a point can grow or shrink.
	private void SetBox(MissionBox box) {
		_boxShape = box;
		_box.Clear();
		if (!box.IsEmpty) {
			AddBox(box, BoxColor);
			AddBox(box.Grown(HddMap.Margin), MapExtentColor);
			AddBox(box.Grown(MissionObjectives.RulesOfEngagementMargin), AbortColor);
		}
	}

	/// <summary>Lays every line on the ground again, for terrain whose heights have changed under them.</summary>
	public void Redrape() {
		Lay(keep: false);
		SetBox(_boxShape);
	}

	private List<Vector3> AreaLines(MissionTriggerArea area) {
		var lines = new List<Vector3>();
		if (area.Shape == MissionTriggerShape.Circle) {
			_drape.AddCircle(lines, area.A, area.Radius);
		} else {
			_drape.AddRectangle(lines, area.A, area.B);
		}

		return lines;
	}

	private List<Vector3> RouteLines(PlannedRoute route) {
		var lines = new List<Vector3>();
		_drape.AddPolyline(lines, route.Points);
		return lines;
	}

	private List<Vector3> PadLines(MissionBasePad pad) {
		var (x, y) = pad.TileOrigin;
		int size = pad.Layout.TileSize;
		var lines = new List<Vector3>();
		_drape.AddRectangle(lines, new Vec3i(x, y, 0), new Vec3i(x + size, y + size, 0));
		return lines;
	}

	/// <summary>
	/// Tints the ground a selected trigger area catches, through <paramref name="highlight"/>, and clears it for any
	/// other selection.
	/// </summary>
	public void Highlight(GroundHighlight highlight, SelectionState selection) {
		if (selection.Current is not AreaSelection { Guid: var guid } || _index.Area(guid)?.Area is not { } area) {
			highlight.Clear();
			return;
		}

		var a = WorldScale.ToRender(area.A);
		if (area.Shape == MissionTriggerShape.Circle) {
			highlight.SetRange(new Vector2(a.X, a.Z), WorldScale.DistanceToRender(area.Radius));
		} else {
			var b = WorldScale.ToRender(area.B);
			highlight.SetBox(new Vector2(a.X, a.Z), new Vector2(b.X, b.Z));
		}
	}

	/// <summary>
	/// What colour an area draws in: by who trips it, read off the first action that tests it — the
	/// player's side of the fight, everyone else's groups, or one named target — or grey when no action tests it.
	/// </summary>
	private Vector3 AreaColor(PlannedArea area) {
		foreach (short guid in area.TestedBy) {
			return _index.Action(guid)?.Type switch {
				MissionAction.SubjectPlayer or MissionAction.SubjectPlayerGroup => PlayerTriggerColor,
				>= MissionAction.SubjectTargetMech => TargetTriggerColor,
				_ => GroupTriggerColor
			};
		}

		return UntestedTriggerColor;
	}

	private void AddBox(MissionBox box, Vector3 color) {
		var lines = new List<Vector3>();
		_drape.AddRectangle(lines, new Vec3i(box.MinX, box.MinY, 0), new Vec3i(box.MaxX, box.MaxY, 0));
		_box.Add((lines, color));
	}

	/// <summary>Draws the overlays the settings turn on, the selected feature last and highlighted.</summary>
	public void Draw(WireframeRenderer wireframe, Camera camera, float aspect, EditorSettings settings,
			SelectionState selection) {
		if (settings.ShowMissionBox) {
			foreach (var (lines, color) in _box) {
				DrawLines(wireframe, camera, aspect, lines, color);
			}
		}

		var highlighted = new List<Vector3>();

		if (settings.ShowBasePads) {
			var selectedGroup = selection.Current is GroupSelection group ? _index.Group(group.Guid) : null;
			foreach (var (pad, lines) in _pads) {
				if (selectedGroup != null && pad.GroupIndex == selectedGroup.Position) {
					highlighted.AddRange(lines);
					AddSnap(highlighted, selectedGroup.Setup);
				} else {
					DrawLines(wireframe, camera, aspect, lines, PadColor);
				}
			}
		}

		if (settings.ShowRoutes) {
			var selectedRoutes = SelectedRoutes(selection);
			foreach (var (route, lines, color) in _routes) {
				if (selectedRoutes.Contains(route.Guid)) {
					highlighted.AddRange(lines);
				} else {
					DrawLines(wireframe, camera, aspect, lines, color);
				}
			}
		}

		if (settings.ShowTriggerAreas) {
			var selectedAreas = SelectedAreas(selection);
			foreach (var (area, lines, color) in _areas) {
				if (selectedAreas.Contains(area.Guid)) {
					highlighted.AddRange(lines);
					AddPosts(highlighted, area.Area);
				} else {
					DrawLines(wireframe, camera, aspect, lines, color);
				}
			}
		}

		DrawLines(wireframe, camera, aspect, highlighted, SelectedColor);
	}

	/// <summary>
	/// The areas the selection lights up, by GUID: the selected area, or every area the selected action
	/// tests.
	/// </summary>
	private HashSet<short> SelectedAreas(SelectionState selection) => selection.Current switch {
		AreaSelection area => [area.Guid],
		ActionSelection selected when _index.Action(selected.Guid) is { } action => action.Areas.ToHashSet(),
		_ => []
	};

	/// <summary>
	/// The routes the selection lights up, by GUID: the selected route, the route a selected group walks, or
	/// the route an order-complete objective is about.
	/// </summary>
	private HashSet<short> SelectedRoutes(SelectionState selection) => selection.Current switch {
		RouteSelection route => [route.Guid],
		GroupSelection selected =>
			_index.Routes.Where(route => route.WalkedBy.Contains(selected.Guid)).Select(route => route.Guid).ToHashSet(),
		ObjectiveSelection selected when _index.Layout.Objectives.FirstOrDefault(objective => objective.Index == selected.Record)
				is { Fields: { ConditionCode: MissionObjective.ConditionOrderComplete, RouteRef: not -1 } record } =>
			[record.RouteRef],
		_ => []
	};

	/// <summary>
	/// The paints-ground snap of one group: a post at the point the mission authored and a line from
	/// it to where the formation's fixed spot in the tile puts the group.
	/// </summary>
	private void AddSnap(List<Vector3> into, MissionGroupSetup setup) {
		if (setup.AuthoredPoint == setup.Point) {
			return;
		}

		_drape.AddPost(into, setup.AuthoredPoint.X, setup.AuthoredPoint.Y, PostHeight / 2);
		_drape.AddEdge(into, setup.AuthoredPoint, setup.Point);
	}

	private void AddPosts(List<Vector3> into, MissionTriggerArea area) {
		if (area.Shape == MissionTriggerShape.Circle) {
			_drape.AddPost(into, area.A.X, area.A.Y, PostHeight);
			return;
		}

		_drape.AddPost(into, area.A.X, area.A.Y, PostHeight);
		_drape.AddPost(into, area.A.X, area.B.Y, PostHeight);
		_drape.AddPost(into, area.B.X, area.B.Y, PostHeight);
		_drape.AddPost(into, area.B.X, area.A.Y, PostHeight);
	}

	public static void DrawLines(WireframeRenderer wireframe, Camera camera, float aspect, List<Vector3> lines,
			Vector3 color) {
		var span = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(lines);
		wireframe.DrawLines(camera, span, color * HiddenBrightness, aspect, throughGeometry: true);
		wireframe.DrawLines(camera, span, color, aspect, throughGeometry: false);
	}

	/// <summary>
	/// The labels: each area's number at its centre, each walked route's number at its first
	/// waypoint, and the selected route's waypoints, "Start" and then their numbers (<see cref="MissionText.WaypointMark"/>). An
	/// area and a route are numbered as the editor numbers them everywhere (<see cref="MissionText.Number"/>). Drawn into ImGui's
	/// background list, so they sit under every panel.
	/// </summary>
	public void DrawLabels(Camera camera, EditorSettings settings, SelectionState selection) {
		var drawList = ImGui.GetBackgroundDrawList();
		var project = new ScreenProjection(camera);

		if (settings.ShowTriggerAreas) {
			foreach (var (area, _, color) in _areas) {
				var shape = area.Area;
				var centre = shape.Shape == MissionTriggerShape.Circle
					? shape.A
					: new Vec3i((shape.A.X + shape.B.X) / 2, (shape.A.Y + shape.B.Y) / 2, 0);
				if (project.ToScreen(_drape.OnGround(centre.X, centre.Y)) is { } at) {
					Label(drawList, at, $"Area {area.Number}", color);
				}
			}
		}

		if (!settings.ShowRoutes) {
			return;
		}

		foreach (var (route, _, color) in _routes) {
			bool selected = selection.Is(new RouteSelection(route.Guid));
			for (int i = 0; i < route.Points.Count; i++) {
				// A closed route's last waypoint is its first, and is labelled once.
				if (route.Closed && i == route.Points.Count - 1) {
					break;
				}

				if (project.ToScreen(_drape.OnGround(route.Points[i].X, route.Points[i].Y)) is not { } at) {
					continue;
				}

				bool waypointSelected = Equals(selection.Current, new RouteSelection(route.Guid, i));
				var dotColor = waypointSelected ? SelectedColor : selected ? SelectedColor * 0.8f : color;
				drawList.AddCircleFilled(at, ScaledImGui.Scaled(waypointSelected ? 5f : 3f), Pack(dotColor));

				if (selected) {
					Label(drawList, at + new Vector2(ScaledImGui.Scaled(6f), 0f), MissionText.WaypointMark(i), SelectedColor);
				} else if (i == 0 && route.Walked) {
					Label(drawList, at + new Vector2(ScaledImGui.Scaled(6f), 0f), $"Route {route.Number}", color);
				}
			}
		}
	}

	/// <summary>
	/// The waypoint nearest a click, within <see cref="WaypointPickRadius"/>, or null. Waypoints are
	/// small screen targets with no volume to cast a ray at, so they are picked in screen space.
	/// </summary>
	public RouteSelection? PickWaypoint(Camera camera, Vector2 screen, EditorSettings settings) {
		if (!settings.ShowRoutes) {
			return null;
		}

		var project = new ScreenProjection(camera);
		float best = ScaledImGui.Scaled(WaypointPickRadius);
		RouteSelection? picked = null;

		foreach (var (route, _, _) in _routes) {
			for (int i = 0; i < route.Points.Count; i++) {
				if (project.ToScreen(_drape.OnGround(route.Points[i].X, route.Points[i].Y)) is not { } at) {
					continue;
				}

				float distance = Vector2.Distance(at, screen);
				if (distance < best) {
					best = distance;
					picked = new RouteSelection(route.Guid, route.Closed && i == route.Points.Count - 1 ? 0 : i);
				}
			}
		}

		return picked;
	}

	/// <summary>
	/// The smallest trigger area containing <paramref name="ground"/>, or null — smallest, because
	/// areas nest (a small trigger inside a wide boundary strip) and the inner one is the one a click
	/// there means.
	/// </summary>
	public AreaSelection? PickArea(Vec3i ground, EditorSettings settings) {
		if (!settings.ShowTriggerAreas) {
			return null;
		}

		PlannedArea? best = null;
		double bestSize = double.MaxValue;
		foreach (var (area, _, _) in _areas) {
			var shape = area.Area;
			if (!shape.Contains(ground)) {
				continue;
			}

			double size = shape.Shape == MissionTriggerShape.Circle
				? Math.PI * shape.Radius * (double)shape.Radius
				: Math.Abs((double)(shape.B.X - shape.A.X) * (shape.B.Y - shape.A.Y));
			if (size < bestSize) {
				bestSize = size;
				best = area;
			}
		}

		return best is { } hit ? new AreaSelection(hit.Guid) : null;
	}

	public static void Label(ImDrawListPtr drawList, Vector2 at, string text, Vector3 color) {
		var size = ImGui.CalcTextSize(text);
		var topLeft = at - new Vector2(0f, size.Y * 0.5f);
		drawList.AddRectFilled(topLeft - new Vector2(2f, 1f), topLeft + size + new Vector2(2f, 1f), 0x99000000);
		drawList.AddText(topLeft, Pack(color), text);
	}

	/// <summary>Packs a colour the way ImGui stores one (little-endian ABGR), opaque.</summary>
	public static uint Pack(Vector3 color) =>
		0xff000000u
		| ((uint)(Math.Clamp(color.Z, 0f, 1f) * 255f) << 16)
		| ((uint)(Math.Clamp(color.Y, 0f, 1f) * 255f) << 8)
		| (uint)(Math.Clamp(color.X, 0f, 1f) * 255f);
}

/// <summary>
/// Render space to ImGui's screen space through one camera, for one frame: the same view and
/// projection the scene was drawn with, over ImGui's display size.
/// </summary>
internal readonly struct ScreenProjection {
	private readonly Matrix4x4 _viewProjection;
	private readonly Vector2 _display;

	public ScreenProjection(Camera camera) {
		_display = ImGui.GetIO().DisplaySize;
		float aspect = _display.X / MathF.Max(_display.Y, 1f);
		_viewProjection = camera.ViewMatrix * camera.ProjectionMatrix(aspect);
	}

	/// <summary>Where a render-space point lands on screen, or null when it is behind the camera or past the far plane.</summary>
	public Vector2? ToScreen(Vector3 render) {
		var clip = Vector4.Transform(new Vector4(render, 1f), _viewProjection);
		if (clip.W <= 0f || clip.Z > clip.W) {
			return null;
		}

		return new Vector2(
			(clip.X / clip.W * 0.5f + 0.5f) * _display.X,
			(0.5f - clip.Y / clip.W * 0.5f) * _display.Y);
	}
}
