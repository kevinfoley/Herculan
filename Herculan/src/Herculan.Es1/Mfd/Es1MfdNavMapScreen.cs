using Herculan.Engine.Numerics;
using Herculan.Es1.Panels;
using Herculan.Es1.View;

namespace Herculan.Es1.Mfd;

/// <summary>
/// The MFD's NAV MAP, mode 2 (docs/es1/simulation/mfd-nav-map.md): the zone's shapes seen from a camera straight above
/// the active view object at a fixed height, on a flat fill, with a five-pixel cross at the center. Its slots are
/// <c>MFDNavMapVtable</c> (<c>000939f2</c>)'s: an empty paint and the map drawn by the update, which the display runs
/// every half second.
/// </summary>
public sealed class Es1MfdNavMapScreen : Es1MfdScreen {
	/// <summary>The palette index the rect is filled in, and the cross's pen.</summary>
	public const byte Fill = 0x17, CrossColor = 0x3c;

	/// <summary>The camera's height over the ground.</summary>
	public const int Height = 200000;

	/// <summary>The camera's near plane and perspective shift, <c>TSCamera_Ctor</c>'s: a focal length of 2⁷ = 128 pixels.</summary>
	public const int NearPlane = 0x80, FocalShift = 7;

	/// <summary>
	/// The first <c>int16</c> of <c>MfdMapScreen_ViewAngles</c> (<c>000b13e0</c>), which <c>MfdMapScreen_StaticInit</c>
	/// (<c>00036e9c</c>) sets: the camera's pitch, straight down. The paint reads no other.
	/// </summary>
	public const short Pitch = unchecked((short)0xc000);

	/// <summary>The cross, the pixels the paint plots about the viewport's origin in this order.</summary>
	private static readonly (int X, int Y)[] Cross = [(0, -1), (0, 0), (0, 1), (-1, 0), (1, 0)];

	/// <summary>
	/// <c>MfdMapScreen_Ctor</c> (<c>ES1DBSIM 00036b18</c>)(0, <paramref name="rect"/>)
	/// (docs/es1/simulation/mfd-nav-map.md#the-screen-object): the base; the viewport, clip mode 1, its origin
	/// −((<i>x1</i> − <i>x0</i>) &gt;&gt; 1), −((<i>y1</i> − <i>y0</i>) &gt;&gt; 1), so its (0, 0) is the rect's
	/// center; the rect's copy <c>+0x14</c>, the same as <see cref="Es1MfdScreen.Rect"/>, which is not kept apart; and
	/// the camera at (0, 0, 0) with <see cref="Pitch"/> and no roll or heading.
	/// </summary>
	internal Es1MfdNavMapScreen(Es1Cockpit cockpit, Es1Rect rect, Es1MfdSharedBlock shared)
		: base(cockpit, rect, shared) {
		ViewportOrigin = (-((rect.X1 - rect.X0) >> 1), -((rect.Y1 - rect.Y0) >> 1));
	}

	/// <summary>The viewport's origin <c>+0x220</c>/<c>+0x224</c>, which the paint installs in the render context.</summary>
	public (int X, int Y) ViewportOrigin { get; }

	/// <summary>The camera's position, <c>+0</c> of <c>+0x10</c>, as the last paint placed it.</summary>
	public Vec3i CameraPosition { get; private set; }

	/// <summary>The camera's angles <c>+0xc</c>: pitch, roll and heading, as the last paint placed them.</summary>
	public (short Pitch, short Roll, short Heading) CameraAngles { get; private set; } = (Pitch, 0, 0);

	/// <summary>Slot <c>+4</c>, <c>MfdMapScreen_PaintNoOp</c> (<c>00036c28</c>): nothing, so the display's repaint leaves the map to the next refresh.</summary>
	public override void Paint() {
	}

	/// <summary>
	/// Slot <c>+8</c>, <c>MfdMapScreen_Paint</c> (<c>00036c2c</c>) (docs/es1/simulation/mfd-nav-map.md#the-paint): the
	/// rect filled in <see cref="Fill"/> in the canvas context; then a context whose rect is the rect less the canvas
	/// context's origin and whose origin is <see cref="ViewportOrigin"/>, its clip mode left at 1; the camera put at the
	/// active view object's <i>x</i> and <i>y</i> at <see cref="Height"/>, turned with that object's roll and heading;
	/// the cross plotted in <see cref="CrossColor"/> about the new origin, the rect's center; and the rect copied from
	/// the page drawn to the page on the screen (<see cref="Es1Cockpit.DrawToCockpit"/> with 1). With no view object
	/// the camera stays where it was, an engine guard.
	/// </summary>
	public override void Update() {
		var canvas = Cockpit.PushCanvasContext();
		canvas.FillRect(Rect.X0, Rect.Y0, Rect.X1, Rect.Y1, Fill);
		var origin = canvas.Origin;
		var map = new Es1PanelCanvas(canvas.Display,
			new Es1Rect(Rect.X0 - origin.X, Rect.Y0 - origin.Y, Rect.X1 - origin.X, Rect.Y1 - origin.Y), ViewportOrigin);
		if (Cockpit.ActiveViewObject is { } view) {
			CameraPosition = new Vec3i(view.Position.X, view.Position.Y, Height);
			CameraAngles = (Pitch, view.View.Y, view.View.Z);
		}

		// PLACEHOLDER: Raster_InstallViewProjection of the camera, a null model transform, WorldShades_ResetRamps' flat
		// ramps and each of Zone_ShapeObjects drawn through TSObj_RenderCopy (docs/es1/simulation/mfd-nav-map.md#the-paint,
		// step 4). The port draws the world only in the 3D pass of the view window (Es1VideoMemory.DrawWorld); a world
		// pass into a cockpit rect, which the missile camera (plan step 6c5 part C) needs too, is not built, so no
		// shape is drawn.
		foreach (var (x, y) in Cross) {
			map.Plot(x, y, CrossColor);
		}

		Cockpit.DrawToCockpit(Rect);
	}
}
