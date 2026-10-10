using Herculan.Engine.Numerics;
using Herculan.Engine.Sim;
using Herculan.Engine.World;

namespace Herculan.Engine.Cockpit;

/// <summary>
/// One icon on the map, as <c>HddCommandScreen_AddObjectMarker</c> (<c>0044e080</c>) leaves it in a marker gadget for
/// <c>HddMarker_Paint</c> (<c>0044f194</c>) to paint.
/// </summary>
/// <param name="WorldX">Where it sits, world units.</param>
/// <param name="WorldY">Same.</param>
/// <param name="Frame">
/// Its frame in the <c>icons</c> bank, with the heading rotation already applied — see
/// <see cref="HddMap.RotatedFrame"/>. The bank is 90 frames of nine-frame groups.
/// </param>
/// <param name="NudgeX">
/// Device-pixel nudge the rotation carries with it, <c>DAT_004d1d54</c> shifted by
/// <c>VideoMode_XCoordShift</c>. Zero for anything that does not rotate.
/// </param>
/// <param name="NudgeY">The same on y, <c>DAT_004d1d5c</c>.</param>
/// <param name="Size">
/// The marker's own square size in device pixels — <c>HddMarker_SetSize</c> (<c>0044f634</c>)'s argument, which becomes
/// both the gadget's extent and (halved) the offset its icon is drawn back by, so the icon lands
/// centred on the object rather than hanging off it.
/// </param>
/// <param name="Ranged">
/// Whether this marker shrinks with distance from the map centre. Set for the two structure classes
/// only: their paint divides <see cref="HddMap.MarkerSizeReference"/> by that distance and, when the
/// result is smaller than the icon, draws a box of that size instead of the icon.
/// </param>
/// <param name="ColorId">
/// <c>COLORS.DAT</c> id the box is drawn in when <paramref name="Ranged"/> shrinks it past the icon:
/// id 5 (blue) for a friendly structure, id 9 (red) for a hostile one.
/// </param>
/// <param name="PilotSlot">
/// Which squad comm box this marker belongs to, or -1. Clicking a squadmate's marker selects that
/// pilot, which is <c>HddCommandScreen_PickPilot</c> (<c>0044d804</c>)'s whole body.
/// </param>
public readonly record struct HddMapMarker(int WorldX, int WorldY, int Frame,
	int NudgeX, int NudgeY, int Size, bool Ranged, int ColorId, int PilotSlot = -1);

/// <summary>
/// The command display's map camera: where it is centred, how far it is zoomed, and the clamps that
/// keep it over the mission. This is <c>HddCommandScreen</c>'s own <c>+0x18</c>/<c>+0x1c</c> centre,
/// <c>+0x20</c> scale and <c>+0x114</c>/<c>+0x118</c> pan offset, kept as one mutable object because
/// that is what it is: state the buttons and keys move and the paint reads.
/// </summary>
/// <remarks>
/// <para>The <see cref="MissionBox"/> is the map's whole frame of reference: the command screen copies
/// it into its own <c>+0x160</c> rect and draws it as the manual's red mission border, its zoom fit
/// is the box's half-extent over the viewport's half-extent, and its pan clamp is the box grown by
/// <see cref="HddMap.Margin"/> on every side.</para>
///
/// <para><b>The projection is a divide, not a matrix.</b> The screen installs a view projection
/// carrying only the centre and the scale (<c>HddMap_ClampAndInstallView</c> writes exactly those three fields)
/// and every point goes through <c>Raster_PerspectiveDivide</c> with a focal length of
/// <c>1 &lt;&lt; DAT_0049d6bc</c> = 256. So a world point lands at
/// <c>(world - centre) * 256 / scale</c> device pixels from the viewport's own centre, with y
/// negated — <see cref="ToScreenX"/> and <see cref="ToScreenY"/>. <see cref="Scale"/> is therefore
/// world units per pixel in 8.8 fixed point, and everything below is arithmetic on that one
/// number.</para>
/// </remarks>
public sealed class HddMapView {
	/// <summary>Fractional bits in <see cref="Scale"/> — <c>DAT_0049d6bc</c>, 8.</summary>
	public const int ScaleShift = 8;

	/// <summary>
	/// How far in the map will go: <c>HddMap_ZoomIn</c> (<c>0044cf9c</c>) takes a step only while the result is still
	/// at least this, so the closest view is 60000 &gt;&gt; 8 = 234 world units per pixel.
	/// </summary>
	public const int MinScale = 60000;

	/// <summary>
	/// Zoom steps between fully out and fully in — the constructor's <c>(fullScale - 60000) / 25</c>.
	/// </summary>
	public const int ZoomSteps = 25;

	/// <summary>Floor on that step, so a small mission still zooms in a sensible number of presses.</summary>
	public const int MinZoomStep = 5000;

	/// <summary>
	/// Pan step at full zoom-in — the constant term of <c>HddMap_RecomputePanStep</c> (<c>0044eea0</c>), which scales the step
	/// from this at the closest view up to this plus <see cref="PanStepSpan"/> at the widest.
	/// </summary>
	public const int PanStepBase = 5000;

	/// <summary>How much the pan step grows across the zoom range — <c>HddMap_RecomputePanStep</c> (<c>0044eea0</c>)'s 45000.</summary>
	public const int PanStepSpan = 45000;

	private readonly int _halfWidth;
	private readonly int _halfHeight;
	private readonly int _width;
	private readonly int _height;

	/// <param name="bounds">The mission box.</param>
	/// <param name="viewportWidth">The map viewport's width in device pixels.</param>
	/// <param name="viewportHeight">Its height.</param>
	public HddMapView(MissionBox bounds, int viewportWidth, int viewportHeight) {
		Bounds = bounds;

		// The render target is centred at -(width >> 1), -(height >> 1), and every clamp below reads
		// those two back as the viewport's half-extent — the constructor's own arithmetic.
		_halfWidth = Math.Max(viewportWidth >> 1, 1);
		_halfHeight = Math.Max(viewportHeight >> 1, 1);
		_width = viewportWidth;
		_height = viewportHeight;

		// Fit the whole box: the smaller of the two axes' units-per-pixel, so neither overflows.
		int byWidth = (bounds.Width >> 1) / _halfWidth << ScaleShift;
		int byHeight = (bounds.Height >> 1) / _halfHeight << ScaleShift;
		// The original's own min, not a max: it fits the tighter axis and lets the other crop. Only
		// the divide-by-zero guard is added, for a mission whose coordinates are all one point.
		FullScale = Math.Max(Math.Min(byWidth, byHeight), 1);
		ZoomStep = Math.Max((FullScale - MinScale) / ZoomSteps, MinZoomStep);
		Scale = FullScale;
	}

	/// <summary>The mission box this view is clamped to.</summary>
	public MissionBox Bounds { get; }

	/// <summary>World units per pixel at full zoom-out, 8.8 fixed — the whole box in the viewport.</summary>
	public int FullScale { get; }

	/// <summary>One press of a magnifier, in the same units.</summary>
	public int ZoomStep { get; }

	/// <summary>Current zoom, between <see cref="MinScale"/> and <see cref="FullScale"/>.</summary>
	public int Scale { get; private set; }

	/// <summary>How far the player has scrolled the map off their own machine, world units.</summary>
	public int PanX { get; private set; }

	/// <summary>The same on y.</summary>
	public int PanY { get; private set; }

	/// <summary>Where the view is centred this frame — the player's position plus the pan, clamped.</summary>
	public int CentreX { get; private set; }

	/// <summary>The same on y.</summary>
	public int CentreY { get; private set; }

	/// <summary>Half the world width the viewport covers at the current zoom.</summary>
	public int HalfWorldWidth => _halfWidth * Scale >> ScaleShift;

	/// <summary>Half the world height it covers.</summary>
	public int HalfWorldHeight => _halfHeight * Scale >> ScaleShift;

	/// <summary>
	/// World units one arrow press scrolls by — <c>HddMap_RecomputePanStep</c> (<c>0044eea0</c>), recomputed on every zoom. Its
	/// own integer arithmetic, shifts included, because it is that arithmetic that makes the step a
	/// round number of pixels rather than a round number of world units.
	/// </summary>
	public int PanStep {
		get {
			int range = FullScale - MinScale;
			return range <= 0
				? PanStepBase
				: (((Scale - MinScale >> ScaleShift) * PanStepSpan) / range << ScaleShift) + PanStepBase;
		}
	}

	/// <summary>
	/// Re-centres on <paramref name="subject"/> and applies the clamp — the first thing
	/// <c>HddCommandScreen_DrawMap</c> does each repaint, followed by <c>HddMap_ClampAndInstallView</c>. The map follows the
	/// player's machine and the pan rides on top of it, which is why a stationary pan still drifts
	/// as the machine walks.
	/// </summary>
	public void Follow(Vec3i subject) {
		CentreX = subject.X + PanX;
		CentreY = subject.Y + PanY;

		var grown = Bounds.Grown(HddMap.Margin);
		CentreX = Math.Clamp(CentreX, grown.MinX + HalfWorldWidth, grown.MaxX - HalfWorldWidth);
		CentreY = Math.Clamp(CentreY, grown.MinY + HalfWorldHeight, grown.MaxY - HalfWorldHeight);
	}

	/// <summary>Widens the view one step, up to the whole mission box — <c>HddMap_ZoomOut</c> (<c>0044cf68</c>).</summary>
	public void ZoomOut() {
		if (Scale + ZoomStep <= FullScale) {
			Scale += ZoomStep;
		}
	}

	/// <summary>Closes in one step, down to <see cref="MinScale"/> — <c>HddMap_ZoomIn</c> (<c>0044cf9c</c>).</summary>
	public void ZoomIn() {
		if (Scale - ZoomStep >= MinScale) {
			Scale -= ZoomStep;
		}
	}

	/// <summary>
	/// Scrolls by one <see cref="PanStep"/>, or by whatever room is left before the view's own edge
	/// reaches the grown box — the four functions <c>HddMap_PanLeft</c> (<c>0044cfd0</c>), <c>HddMap_PanRight</c> (<c>0044d034</c>),
	/// <c>HddMap_PanUp</c> (<c>0044d098</c>) and <c>HddMap_PanDown</c> (<c>0044d0fc</c>), which differ only in sign and axis.
	/// </summary>
	/// <param name="dx">-1 for left, +1 for right, 0 for neither.</param>
	/// <param name="dy">+1 for up (world +y), -1 for down.</param>
	public void Pan(int dx, int dy) {
		var grown = Bounds.Grown(HddMap.Margin);
		if (dx != 0) {
			int room = dx > 0
				? grown.MaxX - CentreX - HalfWorldWidth
				: CentreX - grown.MinX - HalfWorldWidth;
			PanX += dx * Math.Min(Math.Max(room, 0), PanStep);
		}

		if (dy != 0) {
			int room = dy > 0
				? grown.MaxY - CentreY - HalfWorldHeight
				: CentreY - grown.MinY - HalfWorldHeight;
			PanY += dy * Math.Min(Math.Max(room, 0), PanStep);
		}
	}

	/// <summary>
	/// Drops the pan so the map snaps back onto the player's machine — the keypad-5 case of the
	/// screen's key dispatch, which zeroes both offsets and nothing else.
	/// </summary>
	public void Recentre() {
		PanX = 0;
		PanY = 0;
	}

	/// <summary>Device-pixel x of a world x, measured from the map viewport's own left edge.</summary>
	public float ToScreenX(int worldX) =>
		_halfWidth + (worldX - (long)CentreX) * (1 << ScaleShift) / (float)Scale;

	/// <summary>Device-pixel y of a world y. World +y is up the screen, hence the negation.</summary>
	public float ToScreenY(int worldY) =>
		_halfHeight - (worldY - (long)CentreY) * (1 << ScaleShift) / (float)Scale;

	/// <summary>
	/// Whether a world point lands inside the viewport, edges included — <c>HddCommandScreen_IsOnMap</c>
	/// (<c>0044f09c</c>), which projects it the way a marker is placed and tests it against the viewport's rect.
	/// </summary>
	public bool OnViewport(Vec3i point) {
		float x = ToScreenX(point.X);
		float y = ToScreenY(point.Y);
		return x >= 0 && x <= _width - 1 && y >= 0 && y <= _height - 1;
	}

	/// <summary>World x of a device-pixel offset from the viewport's left edge — the inverse, which
	/// is what turns a click into a gridpoint (<c>HddCommandScreen_HitTestMarker</c> (<c>0044d860</c>)'s first three lines).</summary>
	public int ToWorldX(float screenX) =>
		CentreX + (int)((screenX - _halfWidth) * Scale) / (1 << ScaleShift);

	/// <summary>World y of a device-pixel offset from the viewport's top edge.</summary>
	public int ToWorldY(float screenY) =>
		CentreY - (int)((screenY - _halfHeight) * Scale) / (1 << ScaleShift);
}

/// <summary>
/// The command display's map: which icon each object gets, how a heading picks a frame, and the
/// grid the whole thing is drawn over. See docs/retail/simulation/heads-down-display.md.
/// </summary>
public static class HddMap {
	/// <summary>
	/// World units the pan clamp and the raster are allowed past the <see cref="MissionBox"/> — the
	/// literal 60000 the command screen adds to every edge (<c>HddMap_ClampAndInstallView</c> and the
	/// raster builder both).
	/// </summary>
	public const int Margin = 60000;

	/// <summary>Sprite bank the markers come from — <c>HddMarker_Ctor</c>'s lazily loaded <c>icons</c>.</summary>
	public const string IconBank = "ICONS";

	/// <summary>
	/// Frames per rotating icon group. A group is eight headings plus one spare the display never
	/// asks for; the six groups from frame 24 up are laid out back to back at this pitch.
	/// </summary>
	public const int IconGroupStride = 9;

	/// <summary>Hostile HERC.</summary>
	public const int EnemyMechIcon = 0x18;

	/// <summary>Friendly HERC that is not in the player's squad.</summary>
	public const int FriendlyMechIcon = 0x21;

	/// <summary>The player's own machine.</summary>
	public const int PlayerMechIcon = 0x2a;

	/// <summary>Squad slot 0; slots 1 and 2 follow at <see cref="IconGroupStride"/>.</summary>
	public const int SquadMechIcon = 0x33;

	/// <summary>Friendly flyer or ground vehicle.</summary>
	public const int FriendlyFlyerIcon = 6;

	/// <summary>Hostile flyer or ground vehicle.</summary>
	public const int EnemyFlyerIcon = 0xf;

	/// <summary>Friendly structure whose type is not in <see cref="SmallStructureSilhouettes"/>.</summary>
	public const int FriendlyStructureIcon = 2;

	/// <summary>The same structure hostile.</summary>
	public const int EnemyStructureIcon = 4;

	/// <summary>Friendly structure whose silhouette is one of the listed ones.</summary>
	public const int FriendlySmallStructureIcon = 3;

	/// <summary>The same hostile.</summary>
	public const int EnemySmallStructureIcon = 5;

	/// <summary>Friendly vehicle-textured structure — the 8x5 tick.</summary>
	public const int FriendlyVehicleIcon = 0x58;

	/// <summary>The same hostile.</summary>
	public const int EnemyVehicleIcon = 0x59;

	/// <summary>Route waypoint 1; the display draws up to nine, one frame apart.</summary>
	public const int FirstWaypointIcon = 0x4e;

	/// <summary>Waypoints the map will draw — <c>HddCommandScreen_BuildMapMarkers</c>'s own cap.</summary>
	public const int MaxWaypoints = 9;

	/// <summary>Marker gadgets the screen allocates, and so the most icons a frame can carry.</summary>
	public const int MaxMarkers = 140;

	/// <summary>Marker square size for a HERC or a flyer — <c>5 &lt;&lt; XCoordShift</c>.</summary>
	public const int UnitMarkerSize = 10;

	/// <summary>For a structure of an unlisted type — <c>9 &lt;&lt; XCoordShift</c>.</summary>
	public const int StructureMarkerSize = 18;

	/// <summary>For a listed one — <c>7 &lt;&lt; XCoordShift</c>.</summary>
	public const int SmallStructureMarkerSize = 14;

	/// <summary>For a vehicle-textured one — <c>3 &lt;&lt; XCoordShift</c>.</summary>
	public const int VehicleMarkerSize = 6;

	/// <summary>For a route waypoint — <c>9 &lt;&lt; XCoordShift</c>.</summary>
	public const int WaypointMarkerSize = 18;

	/// <summary>
	/// The numerator of a ranged marker's apparent size: its paint draws a box of
	/// <c>MarkerSizeReference &lt;&lt; 7 / distance</c> device pixels when that is smaller than the
	/// icon it would otherwise blit, so a structure a long way from the map centre reads as a dot
	/// and one near it as its own silhouette.
	/// </summary>
	public const int MarkerSizeReference = 25000;

	/// <summary>Fractional bits in that divide — the paint's <c>&lt;&lt; 7</c>.</summary>
	public const int MarkerSizeShift = 7;

	/// <summary>
	/// World units between grid lines: the screen measures 3,200,000 units across and divides the
	/// resulting pixel span by 16, so a grid square is 1200 metres at
	/// <see cref="Render.WorldScale.WorldUnitsPerMeter"/>.
	/// </summary>
	public const int GridSpan = 3_200_000;

	/// <summary>The divisor — <c>HddCommandScreen_DrawMap</c> (<c>0044e30c</c>)'s <c>&gt;&gt; 4</c>.</summary>
	public const int GridDivisions = 16;

	/// <summary>Grid pitch in world units.</summary>
	public const int GridPitch = GridSpan / GridDivisions;

	/// <summary><c>COLORS.DAT</c> id the grid is drawn in — palette 15.</summary>
	public const int GridColorId = 11;

	/// <summary>Id the mission border rect is outlined in — palette 10, the manual's red border.</summary>
	public const int BorderColorId = 9;

	/// <summary>Id a friendly ranged marker's box takes — palette 97.</summary>
	public const int FriendlyMarkerColorId = 5;

	/// <summary>Id a hostile one takes — palette 10.</summary>
	public const int HostileMarkerColorId = 9;

	/// <summary>Id the box around the order's chosen unit is outlined in — palette 31.</summary>
	public const int ChosenUnitColorId = 16;

	/// <summary>
	/// Palette index the raster's lowest ground takes; a cell's colour is this plus its raw height
	/// over <see cref="RasterHeightDivisor"/>, so the whole map lives in the theater-owned 16-31
	/// ramp and re-colours with the theater exactly as the terrain does.
	/// </summary>
	public const int RasterBasePalette = 16;

	/// <summary>Raw height per palette step — the raster builder's <c>height / 8</c>.</summary>
	public const int RasterHeightDivisor = 8;

	/// <summary>Raw height the builder clamps to first, which is what keeps the ramp inside 16 entries.</summary>
	public const int RasterHeightClamp = 0x7f;

	/// <summary>
	/// <c>BASES.DAT</c> silhouettes (<c>+0x28</c>) that take the small structure icon rather than the
	/// large one — the case list of <c>HddCommandScreen_AddObjectMarker</c> (<c>0044e080</c>)'s switch, stated as data because that is what
	/// it is: a set of type ids with nothing in common the code names.
	/// </summary>
	public static readonly int[] SmallStructureSilhouettes =
		{ 1, 2, 6, 7, 10, 11, 15, 19, 20, 21, 22, 23, 24, 26, 28 };

	/// <summary>
	/// Which frame of a nine-frame group each heading octant takes — <c>DAT_0049d67c</c>. North and
	/// south get the two tall frames, east and west the two wide ones, and the diagonals the four
	/// square ones, which is what makes the marker read as an arrow.
	/// </summary>
	private static readonly int[] OctantFrame = { 4, 0, 5, 1, 6, 2, 7, 3 };

	/// <summary>Device-pixel x nudge per octant — <c>DAT_004d1d54</c>, already shifted for 640-wide.</summary>
	private static readonly int[] OctantNudgeX = { 0, 0, 0, 0, 0, -4, -8, -4 };

	/// <summary>The same on y — <c>DAT_004d1d5c</c>.</summary>
	private static readonly int[] OctantNudgeY = { -8, -4, 0, 0, 0, 0, 0, -4 };

	/// <summary>
	/// Which of the eight octants <paramref name="heading"/> falls in, by the paint's own walk: a
	/// heading within half an octant of zero is octant 0, and every other heading counts down from 7
	/// in 0x2000 (45 degree) steps.
	/// </summary>
	public static int Octant(int heading) {
		int angle = heading & 0xffff;
		if (angle is <= 0x1000 or >= 0xf000) {
			return 0;
		}

		int octant = 7;
		for (int edge = 0x3000; edge < angle; edge += 0x2000) {
			octant--;
		}

		return octant;
	}

	/// <summary>
	/// The frame a rotating icon group shows at <paramref name="heading"/>, and the nudge that goes
	/// with it. A destroyed object takes the group's base frame but keeps its octant's nudge: the
	/// paint adds the nudge before its own <c>+0x99</c> branch.
	/// </summary>
	public static (int Frame, int NudgeX, int NudgeY) RotatedFrame(int baseFrame, int heading, bool destroyed) {
		int octant = Octant(heading);
		return (destroyed ? baseFrame : baseFrame + OctantFrame[octant],
			OctantNudgeX[octant], OctantNudgeY[octant]);
	}

	/// <summary>
	/// One object's marker, or null when it has none — <c>HddCommandScreen_AddObjectMarker</c> (<c>0044e080</c>). The three global object
	/// lists are walked in turn and every live object gets one, so the only things without a marker
	/// are classes the switch does not recognise.
	/// </summary>
	/// <param name="subject">The object.</param>
	/// <param name="player">The machine the player is flying, which takes an icon of its own.</param>
	/// <param name="squadSlot">Its squad comm-box slot, or -1 — <c>Squad_IndexOf</c>.</param>
	public static HddMapMarker? MarkerFor(SimObject subject, SimObject? player, int squadSlot) {
		ArgumentNullException.ThrowIfNull(subject);
		bool hostile = subject.Side == MissionSide.Cybrid;
		bool destroyed = subject.Neutralised;

		switch (subject.TargetClass) {
			case TargetClass.Herc: {
				int icon = ReferenceEquals(subject, player) ? PlayerMechIcon
					: hostile ? EnemyMechIcon
					: squadSlot >= 0 ? SquadMechIcon + squadSlot * IconGroupStride
					: FriendlyMechIcon;
				return Rotating(icon, UnitMarkerSize, squadSlot);
			}

			case TargetClass.Flyer:
				return Rotating(hostile ? EnemyFlyerIcon : FriendlyFlyerIcon, UnitMarkerSize, -1);

			case TargetClass.Structure:
			case TargetClass.GroundVehicle: {
				var type = (subject as BaseObject)?.Type;
				int color = hostile ? HostileMarkerColorId : FriendlyMarkerColorId;

				if (type is { IsVehicle: true }) {
					return new HddMapMarker(subject.Position.X, subject.Position.Y,
						hostile ? EnemyVehicleIcon : FriendlyVehicleIcon,
						0, 0, VehicleMarkerSize, Ranged: true, color);
				}

				bool small = type is { } record
					&& Array.IndexOf(SmallStructureSilhouettes, (int)record.SilhouetteIndex) >= 0;
				return new HddMapMarker(subject.Position.X, subject.Position.Y,
					small ? (hostile ? EnemySmallStructureIcon : FriendlySmallStructureIcon)
						: (hostile ? EnemyStructureIcon : FriendlyStructureIcon),
					0, 0, small ? SmallStructureMarkerSize : StructureMarkerSize,
					Ranged: true, color);
			}

			default:
				return null;
		}

		HddMapMarker Rotating(int icon, int size, int slot) {
			var (frame, nudgeX, nudgeY) = RotatedFrame(icon, subject.Heading, destroyed);
			return new HddMapMarker(subject.Position.X, subject.Position.Y, frame,
				nudgeX, nudgeY, size, Ranged: false, ColorId: -1, PilotSlot: slot);
		}
	}

	/// <summary>
	/// The whole marker list for one frame, in the original's own build order: the player's route
	/// waypoints first, then every live object, capped at <see cref="MaxMarkers"/>.
	/// </summary>
	public static IReadOnlyList<HddMapMarker> Markers(IEnumerable<SimObject> objects, SimObject? player,
			IReadOnlyList<Vec3i>? route, IReadOnlyList<SimObject>? squad) {
		ArgumentNullException.ThrowIfNull(objects);
		var markers = new List<HddMapMarker>();

		// Up to nine, and the first waypoint is skipped: the screen walks the route from index 1 and
		// stops one short of the count, so the leg the squad has already reached carries no icon.
		if (route is { Count: > 1 }) {
			int count = Math.Min(route.Count - 1, MaxWaypoints);
			for (int i = 0; i < count; i++) {
				markers.Add(new HddMapMarker(route[i + 1].X, route[i + 1].Y, FirstWaypointIcon + i,
					0, 0, WaypointMarkerSize, Ranged: false, ColorId: -1));
			}
		}

		foreach (var subject in objects) {
			if (markers.Count >= MaxMarkers) {
				break;
			}

			if (subject.Removed || subject.AwaitingDeployment) {
				continue;
			}

			int slot = SquadSlotOf(squad, subject);
			if (MarkerFor(subject, player, slot) is { } marker) {
				markers.Add(marker);
			}
		}

		return markers;
	}

	/// <summary>
	/// Which comm-box slot <paramref name="subject"/> occupies, or -1 — <c>Squad_IndexOf</c> over the
	/// three-entry squad array.
	/// </summary>
	public static int SquadSlotOf(IReadOnlyList<SimObject>? squad, SimObject subject) {
		for (int i = 0; squad != null && i < squad.Count; i++) {
			if (ReferenceEquals(squad[i], subject)) {
				return i;
			}
		}

		return -1;
	}

	/// <summary>
	/// The palette index one raster texel takes — the raster builder's own clamp-and-divide, which
	/// is the whole of how the map is coloured.
	/// </summary>
	public static int RasterPalette(byte rawHeight) =>
		Math.Min((int)rawHeight, RasterHeightClamp) / RasterHeightDivisor + RasterBasePalette;
}
