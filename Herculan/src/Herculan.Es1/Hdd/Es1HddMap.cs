using HercWorks.Core.Data.File.Dyn;
using HercWorks.Core.Io.Transform.Common;
using Herculan.Engine.Numerics;
using Herculan.Es1.Mfd;
using Herculan.Es1.Panels;
using Herculan.Es1.Sim;
using Herculan.Es1.View;

namespace Herculan.Es1.Hdd;

/// <summary>
/// The command screen's map (docs/es1/simulation/hdd-command-screen.md#the-map): the camera looking straight down and
/// its projection, the view's center, scale, grid and pans, the 75 markers, and the paint of the grid, the border, the
/// ground and the markers. Retail keeps all of it in the command screen's own object, whose offsets the members cite;
/// this engine holds it apart from <see cref="Es1HddCommandScreen"/>'s order column, message row and picks, which it
/// reads through <see cref="Screen"/>.
/// </summary>
public sealed class Es1HddMap {
	/// <summary>The markers the constructor builds, 75.</summary>
	public const int MarkerCount = 75;

	/// <summary>The magnifiers' step and the scale's two limits.</summary>
	public const int ZoomStep = 50000, MaxScale = 1000000, MinScale = 50000;

	/// <summary>How far the map's center may be panned from the origin on each axis.</summary>
	public const int PanLimit = 5000000;

	/// <summary>What the constructor grows the mission box by on every edge.</summary>
	public const int BoxMargin = 60000;

	/// <summary>The world span whose screen span the grid's lines are a sixteenth of: squares of 50000 units.</summary>
	public const int GridWorldSpan = 800000;

	/// <summary><c>TSCamera_Ctor</c>'s near plane and perspective shift for the map's camera: a focal length of 2⁷ = 128 pixels.</summary>
	public const int NearPlane = 0x80, FocalShift = 7;

	/// <summary>
	/// The first <c>int16</c> of <c>HddMapView_Angles</c> (<c>000b1604</c>), which <c>HddCommandScreen_StaticInit</c>
	/// (<c>000420b0</c>) sets with roll and heading 0: the camera's pitch, straight down.
	/// </summary>
	public const short Pitch = unchecked((short)0xc000);

	/// <summary>The palette indices the map draws in.</summary>
	public const byte MapFill = 0x36, GridColor = 0x37, BorderColor = 0x35, ChosenFrameColor = 0x3c;

	/// <summary>The one palette entry the map's ground takes, <c>Palette_SetRampsToRange</c>({<c>0x38</c>, 1}, 1, 14).</summary>
	public const byte GroundEntry = 0x38;

	private readonly Es1HddMarker[] _markers = new Es1HddMarker[MarkerCount];
	private readonly IReadOnlyList<DynamixBitmap>? _icons;
	private Transform3 _cameraFrame, _worldToView;

	/// <summary>
	/// The map's part of <c>HddCommandScreen_Ctor</c> (<c>ES1DBSIM 0003ee58</c>)
	/// (docs/es1/simulation/hdd-command-screen.md#building-it--hddcommandscreen_ctor-0003ee58): the map viewport
	/// <paramref name="viewport"/>; its render context's origin minus half the viewport, so (0, 0) is its center; the
	/// center (0, 0); the grown mission box and the full zoom-out scale 2 × max((((<i>x1</i> − <i>x0</i>) &gt;&gt; 1)
	/// &lt;&lt; 7) / <i>w</i>, the same on <i>y</i>) on it, <i>w</i> and <i>h</i> the origin's halves doubled;
	/// <see cref="RecomputePanStep"/>; and, which retail does after the screen's click regions and order rows, the 75
	/// markers, nothing built and the grid dirty. A viewport under two pixels wide or tall, where retail divides by zero,
	/// takes a width of 1, an engine guard.
	/// </summary>
	internal Es1HddMap(Es1HddCommandScreen screen, Es1Rect viewport) {
		Screen = screen;
		var display = screen.Display;
		MapViewport = viewport;
		MapOrigin = (-((MapViewport.X1 - MapViewport.X0) >> 1), -((MapViewport.Y1 - MapViewport.Y0) >> 1));
		var box = display.Cockpit.Globals.MissionBox;
		GrownBox = new Es1MissionBox(box.MinX - BoxMargin, box.MinY - BoxMargin, box.MaxX + BoxMargin, box.MaxY + BoxMargin);
		int byWidth = (((GrownBox.MaxX - GrownBox.MinX) >> 1) << FocalShift) / Math.Max(MapOrigin.X * -2, 1);
		int byHeight = (((GrownBox.MaxY - GrownBox.MinY) >> 1) << FocalShift) / Math.Max(MapOrigin.Y * -2, 1);
		Scale = Math.Max(byWidth, byHeight) << 1;
		RecomputePanStep();

		_icons = display.Resources?.Invoke("dba", "ICONS.DBA") is { } iconBytes
			? new DynamixBitmapArrayTransformer().Parse(iconBytes)?.Images
			: null;
		for (int i = 0; i < MarkerCount; i++) {
			_markers[i] = new Es1HddMarker(screen);
		}

		GridDirty = true;
	}

	/// <summary>The command screen whose map this is, for the chosen unit, the selected squadmate, its pick and the blink phase.</summary>
	public Es1HddCommandScreen Screen { get; }

	/// <summary><c>+0x18</c>, <c>+0x1c</c>, the map's center, the camera's <i>x</i> and <i>y</i>.</summary>
	public int CenterX { get; private set; }

	/// <inheritdoc cref="CenterX"/>
	public int CenterY { get; private set; }

	/// <summary><c>+0x20</c>, the scale, the camera's height: a scale <i>s</i> draws <i>s</i> / 128 world units to a pixel.</summary>
	public int Scale { get; private set; }

	/// <summary><c>+0x24</c>/<c>+0x28</c>, the world origin's point in the map's context, and <c>+0x2c</c>, the screen span of <see cref="GridWorldSpan"/>.</summary>
	public (int X, int Y) GridOrigin { get; private set; }

	/// <inheritdoc cref="GridOrigin"/>
	public int GridSpan { get; private set; }

	/// <summary><c>+0x30</c>, the grid wants recomputing.</summary>
	public bool GridDirty { get; private set; }

	/// <summary><c>+0x31</c>, the 75 markers; <see cref="MarkerTotal"/> of them built.</summary>
	public IReadOnlyList<Es1HddMarker> Markers => _markers;

	/// <summary><c>+0x71</c>, the map viewport, in the canvas's coordinates.</summary>
	public Es1Rect MapViewport { get; }

	/// <summary>The map's render context's origin <c>+0x220</c>/<c>+0x224</c>, minus half the viewport.</summary>
	public (int X, int Y) MapOrigin { get; }

	/// <summary><c>+0x109</c>, the markers are built.</summary>
	public bool MarkersBuilt { get; private set; }

	/// <summary><c>+0x10a</c>, the markers built, an <c>int16</c>.</summary>
	public short MarkerTotal { get; private set; }

	/// <summary><c>+0x114</c>, <c>+0x118</c>, the pan offsets added to the cockpit's machine's position for the map's center.</summary>
	public int PanX { get; internal set; }

	/// <inheritdoc cref="PanX"/>
	public int PanY { get; internal set; }

	/// <summary><c>+0x125</c>–<c>+0x12d</c>, the center and the scale the map's last paint used.</summary>
	public (int X, int Y, int Scale) LastView { get; private set; }

	/// <summary><c>+0x145</c>, the pan step, <see cref="RecomputePanStep"/>'s.</summary>
	public int PanStep { get; private set; }

	/// <summary><c>+0x160</c>, the mission box grown by <see cref="BoxMargin"/> on every edge.</summary>
	public Es1MissionBox GrownBox { get; }

	/// <summary><c>+0x170</c>, the map cursor: the last map click as a world point.</summary>
	public Vec3i MapCursor { get; private set; }

	/// <summary><c>HddMarker_Icons</c> (<c>00094870</c>), a global: the frames of <c>DBA\ICONS.DBA</c>; none when the file is missing.</summary>
	public IReadOnlyList<DynamixBitmap>? Icons => _icons;

	/// <summary>Frame <paramref name="index"/> of <see cref="Icons"/>, or none.</summary>
	internal DynamixBitmap? Icon(int index) => _icons != null && index >= 0 && index < _icons.Count ? _icons[index] : null;

	private Es1HddDisplay Display => Screen.Display;

	private Es1Cockpit Cockpit => Display.Cockpit;

	// ---- The view.

	/// <summary>
	/// <c>HddMap_ZoomOut</c> (<c>0003fc70</c>) (docs/es1/simulation/hdd-command-screen.md#the-map): while the scale plus
	/// <see cref="ZoomStep"/> is at most <see cref="MaxScale"/>, the step added, <see cref="RecomputePanStep"/>, the grid
	/// dirty and the map dirty.
	/// </summary>
	public void ZoomOut() {
		if (Scale + ZoomStep <= MaxScale) {
			Zoom(ZoomStep);
		}
	}

	/// <summary><c>HddMap_ZoomIn</c> (<c>0003fca4</c>): while the scale less <see cref="ZoomStep"/> is at least <see cref="MinScale"/>, the step taken off, as <see cref="ZoomOut"/>.</summary>
	public void ZoomIn() {
		if (Scale - ZoomStep >= MinScale) {
			Zoom(-ZoomStep);
		}
	}

	/// <summary>
	/// <c>HddMap_PanLeft</c> (<c>0003fcd4</c>): while the center's <i>x</i> less the pan step stays at or above
	/// −<see cref="PanLimit"/>, the step taken off the <i>x</i> pan offset and the map dirty; otherwise nothing.
	/// </summary>
	public void PanLeft() {
		if (CenterX - PanStep >= -PanLimit) {
			PanX -= PanStep;
			Screen.MapDirty = true;
		}
	}

	/// <summary><c>HddMap_PanRight</c> (<c>0003fd00</c>): while the center's <i>x</i> plus the step stays at or below <see cref="PanLimit"/>, the step added to the <i>x</i> offset.</summary>
	public void PanRight() {
		if (CenterX + PanStep <= PanLimit) {
			PanX += PanStep;
			Screen.MapDirty = true;
		}
	}

	/// <summary><c>HddMap_PanUp</c> (<c>0003fd2c</c>): while the center's <i>y</i> plus the step stays at or below <see cref="PanLimit"/>, the step added to the <i>y</i> offset.</summary>
	public void PanUp() {
		if (CenterY + PanStep <= PanLimit) {
			PanY += PanStep;
			Screen.MapDirty = true;
		}
	}

	/// <summary><c>HddMap_PanDown</c> (<c>0003fd58</c>): while the center's <i>y</i> less the step stays at or above −<see cref="PanLimit"/>, the step taken off the <i>y</i> offset.</summary>
	public void PanDown() {
		if (CenterY - PanStep >= -PanLimit) {
			PanY -= PanStep;
			Screen.MapDirty = true;
		}
	}

	/// <summary>
	/// <c>HddMap_RecomputePanStep</c> (<c>00041994</c>): the pan step (((scale − <see cref="MinScale"/>) &gt;&gt; 3) ×
	/// 11875 / 118750) × 8 + 5000, the product a 32-bit one that wraps as retail's does.
	/// </summary>
	public void RecomputePanStep() => PanStep = unchecked(((Scale - MinScale) >> 3) * 11875) / 118750 * 8 + 5000;

	/// <summary><c>HddMap_ToViewport</c> (<c>0003fd84</c>)(screen, out, <paramref name="point"/>): the point plus the viewport's corner and its half-extent.</summary>
	public (int X, int Y) ToViewport((int X, int Y) point) =>
		(point.X + MapViewport.X0 + ((MapViewport.X1 - MapViewport.X0) >> 1),
			point.Y + MapViewport.Y0 + ((MapViewport.Y1 - MapViewport.Y0) >> 1));

	/// <summary><c>HddMap_FromViewport</c> (<c>0003fdc4</c>)(screen, out, <paramref name="point"/>): the point less the viewport's corner and its half-extent.</summary>
	public (int X, int Y) FromViewport((int X, int Y) point) =>
		(point.X - MapViewport.X0 - ((MapViewport.X1 - MapViewport.X0) >> 1),
			point.Y - MapViewport.Y0 - ((MapViewport.Y1 - MapViewport.Y0) >> 1));

	/// <summary>
	/// <c>HddCommandScreen_InstallMapContext</c> (<c>0003fe04</c>)(screen): the map's render context, its rect the map
	/// viewport less <paramref name="installed"/>'s origin, its origin <see cref="MapOrigin"/>, clip mode 1 and the
	/// translation set, so (0, 0) draws at the viewport's center. The context is handed to what draws rather than
	/// installed (<see cref="Es1PanelCanvas"/>).
	/// </summary>
	public Es1PanelCanvas InstallMapContext(Es1PanelCanvas installed) {
		var origin = installed.Origin;
		return new Es1PanelCanvas(installed.Display, new Es1Rect(MapViewport.X0 - origin.X, MapViewport.Y0 - origin.Y,
			MapViewport.X1 - origin.X, MapViewport.Y1 - origin.Y), MapOrigin);
	}

	/// <summary>
	/// <c>HddCommandScreen_HitTestMarker</c> (<c>00040410</c>)(screen, <paramref name="point"/>)
	/// (docs/es1/simulation/hdd-command-screen.md#the-map): the camera installed (<see cref="InstallCamera"/>); the point
	/// back through <see cref="FromViewport"/> with <i>y</i> negated, (<i>x</i> × scale &gt;&gt; 7, scale, <i>y</i> ×
	/// scale &gt;&gt; 7) put through the camera's frame into <see cref="MapCursor"/>; the same for (<i>x</i> − 5,
	/// <i>y</i> − 5), and <i>h</i> the cursor's <i>x</i> less that point's. Answers the first object of the live object
	/// list whose <i>x</i> and <i>y</i> both lie strictly within <i>h</i> of the cursor's, or none.
	/// </summary>
	public IEs1SimObject? HitTestMarker((int X, int Y) point) {
		InstallCamera();
		var (x, y) = FromViewport(point);
		y = -y;
		MapCursor = ViewToWorld(x, y);
		var corner = ViewToWorld(x - 5, y - 5);
		int reach = MapCursor.X - corner.X;
		foreach (var simObject in Cockpit.Globals.LiveObjects) {
			var p = simObject.Position;
			if (p.X - reach < MapCursor.X && p.Y - reach < MapCursor.Y && MapCursor.X < p.X + reach && MapCursor.Y < p.Y + reach) {
				return simObject;
			}
		}

		return null;
	}

	/// <summary>
	/// <c>HddCommandScreen_IsOnMap</c> (<c>00041bb8</c>)(screen, <paramref name="point"/>)
	/// (docs/es1/simulation/hdd-command-screen.md#tabs-step): the camera installed, and whether the point, projected and
	/// through <see cref="ToViewport"/>, lies in the map viewport, edges included.
	/// </summary>
	public bool IsOnMap(Vec3i point) {
		InstallCamera();
		var (x, y) = ToViewport(Project(point));
		return MapViewport.Contains(x, y);
	}

	// ---- Drawing.

	/// <summary>
	/// <c>HddCommandScreen_BuildMapMarkers</c> (<c>00040a40</c>)(screen) (docs/es1/simulation/hdd-command-screen.md#building-the-markers),
	/// from <see cref="MarkerTotal"/> on: for the cockpit's machine's group's route, its points 1 to its count − 1, at
	/// most 9, marker <i>i</i> taking frame <i>i</i>, the point and the size 9; then each object of the live object list
	/// by its class, its group's side and, for a structure, its type's index: a HERC the player's own <c>0x12</c>, on
	/// side 0 in squad slot <i>n</i> <c>0x1b</c> + 9<i>n</i>, on side 0 otherwise <c>0x36</c>, on another side 9, each
	/// size 7; a flyer <c>0x3f</c>, size 5; a structure of type 5, 6 or 8 <c>0x4b</c> on side 0 and <c>0x49</c> on
	/// another, size 7, and of any other type <c>0x4a</c> or <c>0x48</c>, size 13, each ranged with that size, the pixel
	/// color <c>0x32</c> on side 0 and <c>0x35</c> on another and <see cref="Es1HddMarker.ApparentReferenceSize"/>; an
	/// object of another class none. Then the markers built. Retail counts past the 75 markers and writes through the
	/// pointer after the array (docs/es1/simulation/hdd-command-screen.md#open); this engine stops at 75, and skips an
	/// object with no group or a class-1 object that is not a structure, through which retail reads, engine guards. The
	/// markers past the count, which retail frees and leaves in the array, stay here unbuilt.
	/// </summary>
	public void BuildMapMarkers() {
		var machine = Cockpit.Machine;
		if (machine?.Group?.Route is { } route) {
			int count = Math.Min(route.Length - 1, 9);
			for (int i = 0; i < count && MarkerTotal < MarkerCount; i++) {
				var marker = _markers[MarkerTotal++];
				marker.Frame = (short)i;
				marker.RoutePoint = route[i + 1];
				marker.SetSize(9);
			}
		}

		foreach (var simObject in Cockpit.Globals.LiveObjects) {
			if (MarkerTotal >= MarkerCount) {
				break;
			}

			if (MarkerFor(simObject, machine) is not { } kind) {
				continue;
			}

			var marker = _markers[MarkerTotal++];
			marker.Frame = kind.Frame;
			marker.Object = simObject;
			marker.Class = simObject.TargetClass;
			marker.SetSize(kind.Size);
			marker.Ranged = kind.Ranged;
			if (kind.Ranged) {
				marker.RangedSize = (short)kind.Size;
				marker.MinimumApparentSize = 0;
				marker.PixelColor = kind.PixelColor;
				marker.ApparentReference = Es1HddMarker.ApparentReferenceSize;
			}
		}

		MarkersBuilt = true;
	}

	/// <summary>
	/// <c>HddCommandScreen_DrawMap</c> (<c>00040d4c</c>)(screen) (docs/es1/simulation/hdd-command-screen.md#drawing-the-map),
	/// in the map's context, in this order: the whole viewport filled <see cref="MapFill"/>, and nothing more while the
	/// display is dark. The center, the cockpit's machine's <i>x</i> and <i>y</i> plus the pan offsets, unclamped; the
	/// grid dirty and <see cref="LastView"/> taken when the center or the scale moved; the camera installed. The grid in
	/// <see cref="GridColor"/>: with it dirty, the world origin projected into <see cref="GridOrigin"/> and the screen
	/// <i>x</i> span to (<see cref="GridWorldSpan"/>, <see cref="GridWorldSpan"/>, 0) into <see cref="GridSpan"/>; the
	/// vertical lines at the origin's <i>x</i> + (<i>i</i> × span &gt;&gt; 4), <i>i</i> = 0, 1, … while left of the right
	/// edge and from − span &gt;&gt; 4 down while right of the left edge, each from the top edge to the bottom; the
	/// horizontal lines the same from the origin's <i>y</i>. The border: the grown box's two corners projected at height 0
	/// and framed (brush mode 4) in <see cref="BorderColor"/>. The ground: the zone's shapes, seen from the camera with
	/// every lit face in <see cref="GroundEntry"/>, drawn as a world pass into the map's rect
	/// (<see cref="Es1VideoMemory.DrawWorldView"/>, <see cref="Es1RectScene.HddMapZoneShapes"/>), where retail
	/// rasterizes them into the rect's palette indices (docs/herculan/plan-es1-first-mission.md#decisions, decision 19,
	/// this engine's departure); everything after is drawn over it. The markers, in build order (<see cref="PlaceMarker"/>).
	/// The chosen unit's frame: with its marker placed, the marker's rect back through <see cref="FromViewport"/>, grown
	/// by its <see cref="Es1HddMarker.DrawnHalfSize"/> on every side less a pixel at the right and the bottom, framed in
	/// <see cref="ChosenFrameColor"/>. With no machine, where retail reads through a null pointer, the center stays where
	/// it was, and a grid span of 0 or less, whose loops would never end in retail, draws no grid, engine guards.
	/// </summary>
	public void DrawMap() {
		var canvas = Cockpit.PushCanvasContext();
		var map = InstallMapContext(canvas);
		map.FillRect(MapOrigin.X, MapOrigin.Y, -MapOrigin.X, -MapOrigin.Y, MapFill);
		if (Display.Dropout.Dark) {
			return;
		}

		if (Cockpit.Machine is { } machine) {
			CenterX = machine.Position.X + PanX;
			CenterY = machine.Position.Y + PanY;
		}

		if (CenterX != LastView.X || CenterY != LastView.Y || Scale != LastView.Scale) {
			GridDirty = true;
			LastView = (CenterX, CenterY, Scale);
		}

		InstallCamera();
		if (GridDirty) {
			GridOrigin = Project(new Vec3i(0, 0, 0));
			GridSpan = Project(new Vec3i(GridWorldSpan, GridWorldSpan, 0)).X - GridOrigin.X;
			GridDirty = false;
		}

		DrawGrid(map);
		var low = Project(new Vec3i(GrownBox.MinX, GrownBox.MinY, 0));
		var high = Project(new Vec3i(GrownBox.MaxX, GrownBox.MaxY, 0));
		map.OutlineRect(low.X, low.Y, high.X, high.Y, BorderColor);
		canvas.Display.DrawWorldView(map.Rect, new Es1RectView(new Vec3i(CenterX, CenterY, Scale), (Pitch, 0, 0), NearPlane,
			FocalShift, (-MapOrigin.X, -MapOrigin.Y), map.Rect.X0, Es1RectScene.HddMapZoneShapes));

		Es1HddMarker? chosen = null;
		for (int i = 0; i < MarkerTotal; i++) {
			if (PlaceMarker(map, _markers[i])) {
				chosen = _markers[i];
			}
		}

		if (chosen != null) {
			var (x0, y0) = FromViewport((chosen.Rect.X0, chosen.Rect.Y0));
			var (x1, y1) = FromViewport((chosen.Rect.X1, chosen.Rect.Y1));
			int grow = chosen.DrawnHalfSize;
			map.OutlineRect(x0 - grow, y0 - grow, x1 + grow - 1, y1 + grow - 1, ChosenFrameColor);
		}
	}

	// ---- Internals.

	/// <summary>The marker at <paramref name="index"/>, unless retail has freed it: past the count once the markers are built.</summary>
	internal Es1HddMarker? LiveMarker(int index) =>
		index >= 0 && index < MarkerCount && (!MarkersBuilt || index < MarkerTotal) ? _markers[index] : null;

	/// <summary>
	/// The camera's position (its <c>+0</c>–<c>+8</c>) the center and the scale, <c>Raster_InstallViewProjection</c> of it
	/// and <c>Raster_SetModelTransform</c>(0): the camera's frame from <see cref="Pitch"/> at that position, and its
	/// inverse, the world-to-view transform.
	/// </summary>
	internal void InstallCamera() {
		_cameraFrame = Transform3.FromEuler(Pitch, 0, 0);
		_cameraFrame.X = CenterX;
		_cameraFrame.Y = CenterY;
		_cameraFrame.Z = Scale;
		_worldToView = _cameraFrame.Inverted();
	}

	/// <summary>
	/// <c>Raster_ModelToView</c> and <c>Raster_PerspectiveDivide</c> under the camera's <see cref="FocalShift"/> with
	/// <i>y</i> negated: the point in the map's context, world +<i>y</i> up. A point at depth 0, where retail divides by
	/// it, is taken at depth 1, an engine guard.
	/// </summary>
	internal (int X, int Y) Project(Vec3i point) {
		var view = _worldToView.TransformPoint(point.X, point.Y, point.Z);
		int depth = view.Y == 0 ? 1 : view.Y;
		return (unchecked(view.X << FocalShift) / depth, -(unchecked(view.Z << FocalShift) / depth));
	}

	/// <summary>A magnifier's step: the scale moved, <see cref="RecomputePanStep"/>, the grid dirty and the map dirty.</summary>
	private void Zoom(int step) {
		Scale += step;
		RecomputePanStep();
		GridDirty = true;
		Screen.MapDirty = true;
	}

	/// <summary>
	/// The grid's lines (<see cref="DrawMap"/>): the vertical lines from <see cref="GridOrigin"/>'s <i>x</i>, first
	/// rightward from <i>i</i> = 0 while left of the right edge, then leftward from the origin's <i>x</i> less span
	/// &gt;&gt; 4, the next at (<i>i</i> × span &gt;&gt; 4) for <i>i</i> = −2, −3, …, while right of the left edge; then
	/// the horizontal lines the same way from its <i>y</i> with the same span.
	/// </summary>
	private void DrawGrid(Es1PanelCanvas map) {
		if (GridSpan <= 0) {
			return;
		}

		int left = MapOrigin.X, right = -MapOrigin.X, top = MapOrigin.Y, bottom = -MapOrigin.Y;
		Lines(GridOrigin.X, left, right, x => map.DrawLine(x, top, x, bottom, GridColor));
		Lines(GridOrigin.Y, top, bottom, y => map.DrawLine(left, y, right, y, GridColor));

		void Lines(int origin, int low, int high, Action<int> draw) {
			int at = origin;
			for (int i = 1; at < high; i++) {
				draw(at);
				at = unchecked(i * GridSpan >> 4) + origin;
			}

			at = origin - (GridSpan >> 4);
			for (int i = -2; low < at; i--) {
				draw(at);
				at = unchecked(i * GridSpan >> 4) + origin;
			}
		}
	}

	/// <summary>
	/// Places and paints <paramref name="marker"/>, one step of <see cref="DrawMap"/>'s walk
	/// (docs/es1/simulation/hdd-command-screen.md#drawing-the-map). Returns whether the marker's object is the chosen unit.
	/// <para>Where the marker goes:</para>
	/// <list type="bullet">
	/// <item>A route marker, which has no object, goes at its route point.</item>
	/// <item>An object on a side other than 0 is skipped once it is immobilized or destroyed.</item>
	/// <item>Any other object goes at its position while it is radar-visible, or, on side 0, while its group has no
	/// action. Otherwise it is skipped. Retail has one more case here: a blinking marker at the object's last-known
	/// point, drawn only while the object's byte <c>+0xa1</c> is 0. Every constructor stores 1 in that byte and no
	/// store of 0 has been found, so that case does not run and this port leaves it out
	/// (docs/es1/simulation/mfd-scanner.md#the-contacts).</item>
	/// </list>
	/// <para>The point is projected, offset by the marker's half-size and taken through <see cref="ToViewport"/> to give
	/// the rect's new top-left (<see cref="Es1HddMarker.MoveTo"/>); the old top-left is kept in
	/// <see cref="Es1HddMarker.LastTopLeft"/>.</para>
	/// <para>The selected squadmate's machine gets its link line (<see cref="DrawLinkLine"/>). While a squadmate is
	/// selected and <see cref="Es1HddCommandScreen.BlinkPhase"/> is set, that marker is not painted, so it blinks.</para>
	/// <para>Before painting, a ranged marker's <see cref="Es1HddMarker.Distance"/> is set to the
	/// <c>Math_FastMagnitude3D</c> of its point from the camera.</para>
	/// <para>Engine guard: an object with no group is skipped, where retail reads through a null pointer.</para>
	/// </summary>
	private bool PlaceMarker(Es1PanelCanvas map, Es1HddMarker marker) {
		Vec3i point;
		bool chosen = false;
		var simObject = marker.Object;
		if (simObject == null) {
			point = marker.RoutePoint;
		} else {
			if (simObject.Group is not { } group) {
				return false;
			}

			bool sideZero = group.Side == 0;
			if ((simObject.Immobilized || simObject.Destroyed) && !sideZero) {
				return false;
			}

			if (!simObject.RadarVisible && !(sideZero && group.Action == null)) {
				return false;
			}

			point = simObject.Position;
			chosen = ReferenceEquals(simObject, Screen.ChosenUnit);
		}

		var (x, y) = Project(point);
		var at = ToViewport((x - marker.HalfSize.X, y - marker.HalfSize.Y));
		marker.LastTopLeft = (marker.Rect.X0, marker.Rect.Y0);
		marker.MoveTo(at);
		if (simObject != null && Screen.SelectedSlot != Es1HddCommandScreen.None
				&& ReferenceEquals(simObject, Cockpit.SquadmateMachines[Screen.SelectedSlot])) {
			DrawLinkLine(map, marker);
			if (Screen.PilotSelected && Screen.BlinkPhase) {
				return chosen;
			}
		}

		if (marker.Ranged) {
			marker.Distance = SimMath.FastMagnitude3D(point.X - CenterX, point.Y - CenterY, point.Z - Scale);
		}

		marker.Paint();
		return chosen;
	}

	/// <summary>
	/// Draws the line from <paramref name="marker"/>'s center to the selected squadmate's pick, for
	/// <see cref="PlaceMarker"/>. The pick is the current one while it is held, in <c>HudColorTable_Get</c>(slot); else
	/// the saved one while it is held, in that color less 1; else nothing is drawn. The line ends at the pick's point, or
	/// at its unit's position, projected at its own height. A color of 0 draws nothing.
	/// <para>Engine guard: a pick whose kind is neither a point nor a unit, or whose unit is missing, draws nothing, where
	/// retail draws to a stale point or reads through a null pointer.</para>
	/// </summary>
	private void DrawLinkLine(Es1PanelCanvas map, Es1HddMarker marker) {
		int slot = Screen.SelectedSlot;
		var pick = Screen.Picks[slot];
		int color;
		Vec3i? end;
		if (pick.CurrentHeld) {
			color = Es1Cockpit.SquadSlotColor(slot);
			end = pick.CurrentKind == Es1HddPickRecord.PointKind ? pick.CurrentPoint
				: pick.CurrentKind == Es1HddPickRecord.UnitKind ? pick.CurrentUnit?.Position : null;
		} else if (pick.SavedHeld) {
			color = Es1Cockpit.SquadSlotColor(slot) - 1;
			end = pick.SavedKind == Es1HddPickRecord.PointKind ? pick.SavedPoint
				: pick.SavedKind == Es1HddPickRecord.UnitKind ? pick.SavedUnit?.Position : null;
		} else {
			return;
		}

		if (color == 0 || end is not { } target) {
			return;
		}

		var (x, y) = FromViewport((marker.Rect.X0, marker.Rect.Y0));
		var (ex, ey) = Project(target);
		map.DrawLine(x + marker.HalfSize.X, y + marker.HalfSize.Y, ex, ey, (byte)color);
	}

	/// <summary>
	/// What <see cref="BuildMapMarkers"/> gives <paramref name="simObject"/>: its frame, size, whether it is ranged and the
	/// ranged pixel's color, or none.
	/// </summary>
	private (short Frame, int Size, bool Ranged, short PixelColor)? MarkerFor(IEs1SimObject simObject, Es1Mech? machine) {
		switch (simObject.TargetClass) {
			case Es1MfdStatusScreen.HercClass:
				if (ReferenceEquals(simObject, machine)) {
					return (0x12, 7, false, 0);
				}

				if (simObject.Group is not { } group) {
					return null;
				}

				if (group.Side != 0) {
					return (9, 7, false, 0);
				}

				int slot = Cockpit.SquadIndexOf(simObject);
				return ((short)(slot == Es1HddCommandScreen.None ? 0x36 : 0x1b + 9 * slot), 7, false, 0);
			case Es1MfdStatusScreen.FlyerClass:
				return (0x3f, 5, false, 0);
			case Es1MfdStatusScreen.StructureClass:
				if (simObject is not Es1Base { Group: { } baseGroup } structure) {
					return null;
				}

				bool friendly = baseGroup.Side == 0;
				short color = friendly ? (short)0x32 : (short)0x35;
				return structure.Type.Index is Es1Base.FirstRadarType or Es1Base.LastRadarType or Es1Base.ArmedType
					? (friendly ? (short)0x4b : (short)0x49, 7, true, color)
					: (friendly ? (short)0x4a : (short)0x48, 13, true, color);
			default:
				return null;
		}
	}

	/// <summary>
	/// (<paramref name="x"/> × scale &gt;&gt; 7, scale, <paramref name="y"/> × scale &gt;&gt; 7) through
	/// <c>Transform_ApplyToPoint</c> with <c>g_ViewToModel</c>, the camera's frame: a point of the map's context on the
	/// ground.
	/// </summary>
	private Vec3i ViewToWorld(int x, int y) =>
		_cameraFrame.TransformPoint(unchecked(x * Scale) >> FocalShift, Scale, unchecked(y * Scale) >> FocalShift);
}
