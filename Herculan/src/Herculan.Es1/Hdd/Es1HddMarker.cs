using HercWorks.Core.Data.File.Dyn;
using Herculan.Engine.Numerics;
using Herculan.Es1.Sim;
using Herculan.Es1.View;

namespace Herculan.Es1.Hdd;

/// <summary>
/// One of the command screen's map markers (docs/es1/simulation/hdd-command-screen.md#the-markers): a frame of
/// <c>DBA\ICONS.DBA</c> over an object or a route point, placed by the map's paint. Its slots are
/// <c>HddMarkerVtable</c> (<c>0009495e</c>)'s: <see cref="Paint"/>, <see cref="Invalidate"/>, <see cref="OnClick"/>
/// and <see cref="Tick"/>, the flush, with the file's copies of the button base's value, set-value and drag.
/// </summary>
public sealed class Es1HddMarker : Es1ButtonWidget {
	/// <summary><c>+0x57</c>'s value from the marker builder: the reference a ranged marker's apparent size is scaled from.</summary>
	public const int ApparentReferenceSize = 25000;

	/// <summary>
	/// <c>HddMarker_OctantNudge</c> (<c>00094874</c>), eight signed bytes: what a HERC's or a flyer's icon is moved by
	/// on both axes, by its heading's octant.
	/// </summary>
	private static readonly sbyte[] OctantNudge = [0, 0, 0, 0, 0, -2, -4, -2];

	/// <summary>
	/// <c>HddMarker_Ctor</c> (<c>ES1DBSIM 00041c50</c>)(0, <paramref name="screen"/>, the rect (0, 0, 0, 0)): the
	/// gadget base on the rect with the screen <c>+0x20</c>, and <see cref="Toggled"/> clear. Retail loads
	/// <c>DBA\ICONS.DBA</c> into <c>HddMarker_Icons</c> (<c>00094870</c>) the first time; this engine loads it with the
	/// screen (<see cref="Es1HddCommandScreen.Icons"/>), of which the simulator builds one.
	/// </summary>
	internal Es1HddMarker(Es1HddCommandScreen screen) {
		Screen = screen;
		Rect = new Es1Rect(0, 0, 0, 0);
	}

	/// <summary><c>+0x1b</c>, the paint counter <see cref="Invalidate"/> sets to 2.</summary>
	public byte PaintCounter { get; set; }

	/// <summary><c>+0x20</c>, the command screen.</summary>
	public Es1HddCommandScreen Screen { get; }

	/// <summary><c>+0x24</c>, the frame of <c>DBA\ICONS.DBA</c>, the first of an object's octant frames.</summary>
	public short Frame { get; set; }

	/// <summary><c>+0x26</c>/<c>+0x2a</c>, the half-size, <see cref="SetSize"/>'s size &gt;&gt; 1.</summary>
	public (int X, int Y) HalfSize { get; private set; }

	/// <summary><c>+0x2e</c>/<c>+0x32</c>, the rect's top-left before the map's paint last placed it.</summary>
	public (int X, int Y) LastTopLeft { get; set; }

	/// <summary><c>+0x36</c>, which <see cref="Es1HddCommandScreen.TogglePilotButton"/> flips; <see cref="Paint"/> does not read it.</summary>
	public bool Toggled { get; set; }

	/// <summary><c>+0x37</c>, set while <see cref="Tick"/> paints; <see cref="PaintUnlessPainting"/> skips the paint while it is.</summary>
	public bool Painting { get; private set; }

	/// <summary><c>+0x38</c>, the object, or none for a route marker.</summary>
	public IEs1SimObject? Object { get; set; }

	/// <summary><c>+0x3c</c>–<c>+0x44</c>, a route marker's point.</summary>
	public Vec3i RoutePoint { get; set; }

	/// <summary><c>+0x48</c>, the object's class, as the builder read it.</summary>
	public short Class { get; set; }

	/// <summary><c>+0x4a</c>, the half-size the last paint drew at, which the chosen unit's frame is grown by.</summary>
	public short DrawnHalfSize { get; private set; }

	/// <summary><c>+0x4c</c>, a structure's marker, whose icon shrinks with its distance.</summary>
	public bool Ranged { get; set; }

	/// <summary><c>+0x4d</c>, the builder's size of a ranged marker, which <see cref="Paint"/> does not read (docs/es1/simulation/hdd-command-screen.md#open).</summary>
	public short RangedSize { get; set; }

	/// <summary><c>+0x4f</c>, the apparent size below which a ranged marker is a pixel; the builder's 0.</summary>
	public short MinimumApparentSize { get; set; }

	/// <summary><c>+0x51</c>, the pixel's color: 0x32 on side 0, 0x35 on another.</summary>
	public short PixelColor { get; set; }

	/// <summary><c>+0x53</c>, the map's paint's <c>Math_FastMagnitude3D</c> of the marker's point from the camera.</summary>
	public int Distance { get; set; }

	/// <summary><c>+0x57</c>, <see cref="ApparentReferenceSize"/> for a ranged marker.</summary>
	public int ApparentReference { get; set; }

	/// <summary>
	/// <c>HddMarker_SetSize</c> (<c>00041fac</c>)(marker, <paramref name="size"/>): the rect's <i>x1</i> <c>+8</c> and
	/// <i>y1</i> <c>+0xc</c> = the size, and <see cref="HalfSize"/> = size &gt;&gt; 1 on both axes.
	/// </summary>
	public void SetSize(int size) {
		Rect = Rect with { X1 = size, Y1 = size };
		HalfSize = (size >> 1, size >> 1);
	}

	/// <summary>
	/// <c>Widget_MoveTo</c> (<c>000438a4</c>)(the rect, <paramref name="point"/>) (docs/es1/simulation/cockpit-mouse.md#hit-testing):
	/// the rect moved so its top-left is the point, its width and height kept.
	/// </summary>
	public void MoveTo((int X, int Y) point) =>
		Rect = new Es1Rect(point.X, point.Y, point.X + (Rect.X1 - Rect.X0), point.Y + (Rect.Y1 - Rect.Y0));

	/// <summary>
	/// Slot <c>+4</c>, <c>HddMarker_Paint</c> (<c>00041d04</c>) (docs/es1/simulation/hdd-command-screen.md#the-markers):
	/// inside the canvas push, the screen's map context (<see cref="Es1HddCommandScreen.InstallMapContext"/>) and the
	/// rect's top-left back through <see cref="Es1HddCommandScreen.FromViewport"/> as the point. The frame: a route
	/// marker's <see cref="Frame"/>; for a HERC or a flyer the point moved on both axes by <see cref="OctantNudge"/> of
	/// its heading's octant (<see cref="Octant"/>) and <see cref="Frame"/> + octant + 1, or <see cref="Frame"/> while it
	/// is destroyed; for a structure <see cref="Frame"/>; for any other class nothing is drawn. Not ranged: the frame
	/// blitted at the point and <see cref="DrawnHalfSize"/> the half-size's <i>y</i>. Ranged: the apparent size <i>a</i>
	/// = (<see cref="ApparentReference"/> &lt;&lt; 7) / <see cref="Distance"/>; below <see cref="MinimumApparentSize"/>
	/// the point plotted in <see cref="PixelColor"/> and 0 drawn; below the frame's height, its <c>int16</c> <c>+4</c>,
	/// the frame stretched over <i>a</i> × <i>a</i> from the point plus the half-size less <i>a</i> &gt;&gt; 1 and
	/// <i>a</i> &gt;&gt; 1 drawn; otherwise blitted whole as a marker that is not ranged. A distance of 0, where
	/// retail divides by it, and a frame the file lacks, which retail reads past the array, draw nothing, engine guards.
	/// </summary>
	public override void Paint() {
		var map = Screen.InstallMapContext(Screen.Display.Cockpit.PushCanvasContext());
		var (x, y) = Screen.FromViewport((Rect.X0, Rect.Y0));
		DynamixBitmap? frame;
		if (Object is not { } subject) {
			frame = Screen.Icon(Frame);
		} else if (subject.TargetClass is Mfd.Es1MfdStatusScreen.HercClass or Mfd.Es1MfdStatusScreen.FlyerClass) {
			int octant = Octant(unchecked((ushort)subject.Heading));
			x += OctantNudge[octant];
			y += OctantNudge[octant];
			frame = Screen.Icon(subject.Destroyed ? Frame : Frame + octant + 1);
		} else if (subject.TargetClass == Mfd.Es1MfdStatusScreen.StructureClass) {
			frame = Screen.Icon(Frame);
		} else {
			return;
		}

		if (!Ranged) {
			map.Blit(frame, x, y);
			DrawnHalfSize = (short)HalfSize.Y;
			return;
		}

		if (Distance == 0) {
			return;
		}

		int apparent = unchecked(ApparentReference << 7) / Distance;
		if (apparent < MinimumApparentSize) {
			map.Plot(x, y, (byte)PixelColor);
			DrawnHalfSize = 0;
		} else if (frame != null && apparent < frame.Rows) {
			int half = apparent >> 1;
			map.BlitScaled(frame, apparent, apparent, 0, x + HalfSize.X - half, y + HalfSize.Y - half);
			DrawnHalfSize = (short)half;
		} else {
			map.Blit(frame, x, y);
			DrawnHalfSize = (short)HalfSize.Y;
		}
	}

	/// <summary>
	/// Slot <c>+8</c>, <c>HddMarker_Invalidate</c> (<c>00041f10</c>): <see cref="PaintCounter"/> 2, and the display's page
	/// 0 update flag and paint stage 2.
	/// </summary>
	public override void Invalidate() {
		PaintCounter = 2;
		Screen.Display.ScheduleUpdate(Es1HddDisplay.CommandPage);
	}

	/// <summary>Slot <c>+0xc</c>, <c>HddMarker_OnClick</c> (<c>00041f78</c>)(marker, <paramref name="value"/>): with bit 0 set, <see cref="Painting"/> cleared.</summary>
	public override void OnClick(short value) {
		if ((value & 1) != 0) {
			Painting = false;
		}
	}

	/// <summary>
	/// Slot <c>+0x10</c>, <c>HddMarker_FlushDeferredPaint</c> (<c>00041f38</c>): while <c>CockpitView_RectCornerVisible</c>
	/// passes the rect and <see cref="PaintCounter"/> is 2, <see cref="Paint"/> with <see cref="Painting"/> set across it,
	/// the rect copied from the page drawn to the page on the screen (<see cref="Es1Cockpit.DrawToCockpit"/> with 1), and
	/// the counter cleared. The markers are on no widget list, and no caller of the slot is ported.
	/// </summary>
	public override void Tick() {
		var cockpit = Screen.Display.Cockpit;
		if (!cockpit.RectCornerVisible(Rect) || PaintCounter != 2) {
			return;
		}

		Painting = true;
		Paint();
		Painting = false;
		cockpit.DrawToCockpit(Rect);
		PaintCounter = 0;
	}

	/// <summary><c>Widget_PaintUnlessByte37</c> (<c>00041f90</c>)(marker): <see cref="Paint"/> unless <see cref="Painting"/> is set.</summary>
	public void PaintUnlessPainting() {
		if (!Painting) {
			Paint();
		}
	}

	/// <summary>
	/// The heading's octant <see cref="Paint"/> reads, as an unsigned word: 0 at or below <c>0x1000</c> and at or above
	/// <c>0xf000</c>; otherwise 7 through <c>0x3000</c>, one less for each <c>0x2000</c> past it.
	/// </summary>
	public static int Octant(ushort heading) {
		if (heading >= 0xf000 || heading <= 0x1000) {
			return 0;
		}

		int octant = 7;
		for (int bound = 0x3000; bound < heading; bound += 0x2000) {
			octant--;
		}

		return octant;
	}
}
