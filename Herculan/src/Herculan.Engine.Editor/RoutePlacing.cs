using System.Numerics;
using HercWorks.Core.Data.File.Msn;
using Herculan.Engine.Numerics;
using Herculan.Engine.Platform;
using Herculan.Engine.Render;
using ImGuiNET;

namespace Herculan.Engine.Editor;

/// <summary>
/// The editor's route-placing mode: the Toolbox's or the Routes list's New route, or a route view's Add waypoints, starts
/// it, and each left click on the ground in the view then places the next waypoint, a new route's or after an existing
/// route's last. The waypoints so far, their legs and a leg from the last to the ground under the pointer are drawn over
/// the scene. A click on the route's first waypoint closes the loop and finishes, when the loop would have
/// <see cref="RouteEditing.MinLoopWaypoints"/> or more, and is ignored otherwise; Enter or the Toolbox's Finish finishes
/// (<see cref="RequestFinish"/>), and Esc or its Cancel throws the session's waypoints away (<see cref="Cancel"/>).
/// Finishing makes the whole session one edit, one step of the undo history, through <see cref="RouteEditor.Create"/> or
/// <see cref="RouteEditor.Extend"/>, once the frame's ImGui is rendered (<see cref="RunRequest"/>); a new route with no
/// waypoint placed adds nothing.
///
/// <para>While it is on, the host routes the view's left clicks here instead of to the picker and the move handles, and
/// Esc here instead of to the unsaved-changes prompt; the camera flies as ever (<see cref="EditorHost"/>).</para>
/// </summary>
internal sealed class RoutePlacing {
	/// <summary>What the mode asks of the user, in the Toolbox, the route view and over the view.</summary>
	public const string Hint = "Click the ground to place each waypoint, or the route's start to close the loop. Enter finishes; Esc cancels.";

	// How close, in pixels on a 100% display, a click must land to the first waypoint to close the loop on it.
	private const float CloseRadius = 8f;

	private static readonly Vector3 BandColor = new(1f, 1f, 0.6f);

	private readonly RouteEditor _editor;
	private readonly MissionIndex _index;
	private readonly DrapedLines _drape;
	private readonly List<Vec3i> _placed = new();

	// The row-8 GUID of the route the waypoints are added to, or -1 for a new route.
	private short _route = -1;
	private bool _finishing;
	private bool _close;

	public RoutePlacing(RouteEditor editor, MissionIndex index, DrapedLines drape) {
		_editor = editor;
		_index = index;
		_drape = drape;
	}

	/// <summary>Whether waypoints are being placed.</summary>
	public bool Active { get; private set; }

	/// <summary>Why the last finish could not be made, or null.</summary>
	public string? Failure { get; private set; }

	/// <summary>Whether another placing mode is on, which this one does not start during; set once the mission's tools are built.</summary>
	public Func<bool>? Blocked { get; set; }

	/// <summary>Why placing cannot start, or null when it can.</summary>
	public string? WhyNoStart => Blocked?.Invoke() == true ? "A trigger area is being placed." : null;

	/// <summary>Whether the waypoints go to an existing route rather than a new one.</summary>
	public bool Appending => _route != -1;

	/// <summary>How many waypoints have been placed so far.</summary>
	public int Placed => _placed.Count;

	/// <summary>Whether waypoints are being added to row-8 GUID <paramref name="route"/>.</summary>
	public bool IsAppendingTo(short route) => Active && _route != -1 && _route == route;

	/// <summary>Starts placing a new route's waypoints.</summary>
	public void BeginNew() => Begin(-1);

	/// <summary>Starts placing waypoints after the last of row-8 GUID <paramref name="route"/>.</summary>
	public void BeginAppend(short route) {
		if (_editor.WhyFixed(route) == null) {
			Begin(route);
		}
	}

	/// <summary>
	/// A left click at window pixel <paramref name="screen"/> over the view, whose ray meets the ground at
	/// <paramref name="ground"/>, or none: closes the loop on the first waypoint when it lands there, else places a waypoint.
	/// </summary>
	public void Click(Camera camera, Vector2 screen, Vec3i? ground) {
		if (!Active || _finishing) {
			return;
		}

		var existing = Existing;
		if (First(existing) is { } first && OnScreen(camera, first) is { } at
				&& Vector2.Distance(at, screen) <= ScaledImGui.Scaled(CloseRadius)) {
			// A closed route stays one as waypoints are added; an open one closes only into a loop long enough.
			int waypoints = (existing == null ? 0 : Count(existing)) + _placed.Count;
			if (existing is { Closed: true } || waypoints >= RouteEditing.MinLoopWaypoints) {
				RequestFinish(close: true);
			}

			return;
		}

		if (ground is { } point) {
			_placed.Add(new Vec3i(point.X, point.Y, 0));
		}
	}

	/// <summary>Ends placing once the frame's ImGui is rendered, closing the loop when <paramref name="close"/>.</summary>
	public void RequestFinish(bool close = false) {
		if (Active) {
			_finishing = true;
			_close = close;
		}
	}

	/// <summary>Ends placing and throws away the waypoints placed, adding nothing.</summary>
	public void Cancel() {
		if (Active) {
			Reset();
			Failure = null;
		}
	}

	/// <summary>Makes the edit a finish asked for, if any; <see cref="Failure"/> says why it could not be made.</summary>
	public void RunRequest() {
		if (!_finishing) {
			return;
		}

		var placed = _placed.ToList();
		short route = _route;
		bool close = _close;
		Reset();
		Failure = route == -1 ? _editor.Create(placed, close) : _editor.Extend(route, placed, close);
	}

	/// <summary>The waypoints placed, their legs, and the leg from the last to <paramref name="pointer"/>, the ground under the pointer.</summary>
	public void Draw(WireframeRenderer wireframe, Camera camera, float aspect, Vec3i? pointer) {
		if (!Active) {
			return;
		}

		var existing = Existing;
		var path = new List<Vec3i>();
		if (existing != null) {
			path.Add(existing.Points[Count(existing) - 1]);
		}

		path.AddRange(_placed);
		var lines = new List<Vector3>();
		_drape.AddPolyline(lines, path);
		MissionOverlay.DrawLines(wireframe, camera, aspect, lines, MissionOverlay.SelectedColor);

		if (pointer is not { } at || path.Count == 0) {
			return;
		}

		var band = new List<Vector3>();
		_drape.AddEdge(band, path[^1], at);
		if (existing is { Closed: true }) {
			_drape.AddEdge(band, at, existing.Points[0]);
		}

		MissionOverlay.DrawLines(wireframe, camera, aspect, band, BandColor);
	}

	/// <summary>The placed waypoints' marks and numbers, and <see cref="Hint"/> centered at the top of the view, below <paramref name="top"/>.</summary>
	public void DrawLabels(Camera camera, Vector2 display, float top) {
		if (!Active) {
			return;
		}

		var drawList = ImGui.GetBackgroundDrawList();
		var existing = Existing;
		int from = existing == null ? 0 : Count(existing);
		for (int i = 0; i < _placed.Count; i++) {
			if (OnScreen(camera, _placed[i]) is { } at) {
				drawList.AddCircleFilled(at, ScaledImGui.Scaled(4f), MissionOverlay.Pack(MissionOverlay.SelectedColor));
				MissionOverlay.Label(drawList, at + new Vector2(ScaledImGui.Scaled(6f), 0f), MissionText.WaypointMark(from + i),
					MissionOverlay.SelectedColor);
			}
		}

		if (First(existing) is { } first && OnScreen(camera, first) is { } start) {
			drawList.AddCircle(start, ScaledImGui.Scaled(CloseRadius), MissionOverlay.Pack(BandColor));
		}

		var size = ImGui.CalcTextSize(Hint);
		MissionOverlay.Label(drawList, new Vector2((display.X - size.X) / 2f, top + ScaledImGui.Scaled(16f)), Hint, BandColor);
	}

	/// <summary>The Toolbox's route entry: New route, or while placing what has been placed and Finish.</summary>
	public void DrawToolboxEntry() {
		if (Active) {
			ImGui.Text(Appending ? $"Adding waypoints: {_placed.Count} placed" : $"New route: {_placed.Count} waypoint(s) placed");
			if (ImGui.Button("Finish")) {
				RequestFinish();
			}

			ImGui.SameLine();
			if (ImGui.Button("Cancel")) {
				Cancel();
			}

			EditorText.Note(Hint);
		} else {
			ImGui.BeginDisabled(WhyNoStart != null);
			if (ImGui.Button("New route")) {
				BeginNew();
			}

			ImGui.EndDisabled();
		}

		EditorText.Reason(Failure);
	}

	private void Begin(short route) {
		if (Active || WhyNoStart != null) {
			return;
		}

		Reset();
		_route = route;
		Failure = null;
		Active = true;
	}

	private void Reset() {
		_placed.Clear();
		_route = -1;
		_finishing = false;
		_close = false;
		Active = false;
	}

	// The route the waypoints are added to, as the layout places it now, or null for a new route.
	private PlannedRoute? Existing => _route != -1 ? _index.Route(_route) : null;

	// The waypoint a click closes the loop on: the route's first, or the first placed.
	private Vec3i? First(PlannedRoute? existing) =>
		existing != null ? existing.Points[0] : _placed.Count > 0 ? _placed[0] : null;

	// How many waypoints a route has: a closed route's last is its first again.
	private static int Count(PlannedRoute route) => route.Closed ? route.Points.Count - 1 : route.Points.Count;

	private Vector2? OnScreen(Camera camera, Vec3i point) => new ScreenProjection(camera).ToScreen(_drape.OnGround(point.X, point.Y));
}
