using Herculan.Engine.Content;
using Herculan.Engine.Numerics;
using Herculan.Engine.Sim;
using HercWorks.Core.Data.File.Gau;

namespace Herculan.Engine.Cockpit;

/// <summary>
/// What one of the front-window HUD's two waypoint indicators is pointing at this frame: the bearing
/// error that places it, and — for the route indicator — the number and range its caption prints.
/// </summary>
/// <param name="BearingError">
/// <c>heading - bearing</c> as a binary angle, the quantity the whole gadget is a picture of. The
/// simulation's headings run counter-clockwise (<see cref="Detection.HeadingToward"/> is an
/// <c>atan2</c> less a quarter turn), so a <b>positive</b> error is a subject off to the player's
/// right.
/// </param>
/// <param name="Metres">
/// Ground range to the subject through <c>Hud_WorldUnitsToMetres</c> — quantised to multiples of
/// six, and carrying <see cref="SimMath.FastMagnitude2D"/>'s own error, both of which the original's
/// readout has too.
/// </param>
/// <param name="Number">
/// Which waypoint this is, one-based, or <see cref="NoCaption"/> for an indicator that prints none.
/// </param>
/// <param name="ColorId">The <c>COLORS.DAT</c> id the marks are filled with.</param>
public readonly record struct WaypointMark(short BearingError, int Metres, int Number, int ColorId) {
	/// <summary>
	/// <see cref="Number"/> for the nav marker, whose child is handed no caption to compose — only
	/// the route indicator builds one.
	/// </summary>
	public const int NoCaption = -1;

	/// <summary>
	/// The route indicator's subject, or null when the player's group has no route or has walked off
	/// the end of it — <c>Hud_UpdateWaypointIndicator</c>'s own two null tests.
	///
	/// <para>The subject is the waypoint <b>after</b> the group's route cursor, the same one
	/// <c>Ai_FollowRoute</c> drives an AI leader at, and the number printed is the cursor plus one.
	/// So a fresh group is walking at <c>WAYPOINT 1</c> and the route's own slot 0 — the start point
	/// — is never somewhere the player is sent.</para>
	/// </summary>
	public static WaypointMark? ForRoute(MechObject pilot) {
		ArgumentNullException.ThrowIfNull(pilot);
		if (pilot.Group is not { } group
			|| group.WaypointAt(group.RouteCursor + 1) is not { } waypoint) {
			return null;
		}

		return Toward(pilot, waypoint, group.RouteCursor + 1, RouteColorId);
	}

	/// <summary>
	/// The nav marker's, or null while none is down. It prints no caption and wears a colour of its
	/// own; everything else about it is the route indicator's arithmetic.
	/// </summary>
	public static WaypointMark? ForNavMarker(MechObject pilot, NavMarker marker) {
		ArgumentNullException.ThrowIfNull(pilot);
		ArgumentNullException.ThrowIfNull(marker);
		return marker.IsSet ? Toward(pilot, marker.Position, NoCaption, NavMarkerColorId) : null;
	}

	private static WaypointMark Toward(MechObject pilot, Vec3i subject, int number, int colorId) {
		int range = SimMath.FastMagnitude2D(
			subject.X - pilot.Position.X, subject.Y - pilot.Position.Y);
		short bearing = Detection.HeadingToward(subject, pilot.Position);

		return new WaypointMark((short)((short)pilot.Heading - bearing),
			WorldUnitsToMetres(range), number, colorId);
	}

	/// <summary>
	/// <c>Hud_WorldUnitsToMetres</c> (<c>00434228</c>) — the game stating its own scale,
	/// <c>(units / 1000) * 6</c>. The integer divide comes first, which is why every range the HUD
	/// prints is a multiple of six.
	/// </summary>
	public static int WorldUnitsToMetres(int worldUnits) => worldUnits / 1000 * 6;

	/// <summary><c>COLORS.DAT</c> id 0 — palette 14, the green the manual names.</summary>
	public const int RouteColorId = 0;

	/// <summary><c>DAT_004d3c1e</c>, table entry 15 — palette 13, yellow.</summary>
	public const int NavMarkerColorId = 15;

	/// <summary>The caption, composed the way the original concatenates it.</summary>
	public string Caption(string prefix) => prefix + Number + ": " + Metres + " M.";
}

/// <summary>
/// Where the front-window HUD's waypoint indicators draw — children 7 and 8 of the roving-gunsight
/// complex, both built by <c>HudWaypointIndicator_Ctor</c> (<c>0043c268</c>) and both painted by
/// <c>Hud_UpdateWaypointIndicator</c> (<c>0043c3e4</c>).
///
/// <para><b>Where it comes from.</b> Neither child has a <c>.GAU</c> rect of its own: the complex
/// hands both of them the <b>heading tape's</b> rect (offset 1104, <see cref="GAUFile.HeadingTape"/>)
/// and the same <c>±0xe38</c> limits the tape carries, so the mark rides the same span of bearing the
/// compass under it does and reaches the tape's own ends exactly at the limit. Everything else is
/// literals in the constructor, shifted by the video mode the way the rest of the block is.</para>
///
/// <para><b>The two shapes.</b> Inside the limits the subject is on the tape and gets the manual's
/// diamond, placed along it proportionally. Outside them it gets an arrow parked past whichever end
/// it lies beyond, pointing away — the manual's "turn in the direction of the green arrow until the
/// diamond appears". Both are filled polygons rather than sprites.</para>
///
/// <para><b>Units.</b> Device pixels, like <see cref="RotationIndicator"/>, except
/// <see cref="LabelRect"/>, which is in <c>.GAU</c> units because the caption is placed by the same
/// centred-in-a-rect path every other cockpit label uses.</para>
/// </summary>
public readonly struct WaypointIndicator {
	/// <summary>
	/// The bearing either side of dead ahead the tape spans, <c>±0xe38</c> — about 20°. The pair the
	/// gunsight hands both children's <c>SetLimits</c> slot, and the same one the heading tape gets.
	/// </summary>
	public const short Limit = 0x0e38;

	/// <summary>
	/// <c>widget+0x2c</c> — <c>max - min</c>, the divisor the paint maps the error through. Stated
	/// rather than derived because that field is what the original reads.
	/// </summary>
	public const int Range = Limit * 2;

	private WaypointIndicator(int left, int right, int top,
			(int X0, int Y0, int X1, int Y1) labelRect) {
		Left = left;
		Right = right;
		Top = top;
		LabelRect = labelRect;
	}

	/// <summary>Left edge of the heading tape's rect, device pixels.</summary>
	public int Left { get; }

	/// <summary>Its right edge.</summary>
	public int Right { get; }

	/// <summary>Its top edge — the row both shapes hang off, before their own lift.</summary>
	public int Top { get; }

	/// <summary>
	/// Where the <c>WAYPOINT n: d M.</c> caption goes, in <c>.GAU</c> units: the tape's own rect
	/// dropped by <see cref="LabelDrop"/>, with the text centred in it. That puts the line clear below
	/// the compass while the marks sit above it.
	/// </summary>
	public (int X0, int Y0, int X1, int Y1) LabelRect { get; }

	/// <summary>
	/// This herc's indicators, or null when its <c>.GAU</c> has no heading tape rect — without one
	/// there is nothing to hang either shape off.
	/// </summary>
	public static WaypointIndicator? From(CockpitArt art) {
		ArgumentNullException.ThrowIfNull(art);
		if (art.Gau.HeadingTape is not { } tape) {
			return null;
		}

		int x0 = tape.Origin.X;
		int y0 = tape.Origin.Y;
		int x1 = x0 + tape.Size.Width;
		int y1 = y0 + tape.Size.Height;

		return new WaypointIndicator(x0 * Scale, x1 * Scale, y0 * Scale,
			(x0, y0 + LabelDrop, x1, y1 + LabelDrop));
	}

	/// <summary>
	/// Whether the subject is inside the tape's span, and so wears the diamond rather than an arrow.
	/// The test is on the <i>unsigned</i> error, which is how the original spells "within
	/// <see cref="Limit"/> either way round".
	/// </summary>
	public static bool OnTape(short bearingError) {
		ushort error = (ushort)bearingError;
		return error <= Limit || error >= unchecked((ushort)-Limit);
	}

	/// <summary>
	/// Which end an off-tape subject's arrow parks at. A positive error is a subject to the player's
	/// right — see <see cref="WaypointMark.BearingError"/>.
	/// </summary>
	public static bool PointsRight(short bearingError) => bearingError > 0;

	/// <summary>
	/// The diamond's four corners — bottom, left, top, right — in device pixels. Placed at the tape's
	/// centre plus the error mapped across the tape's width, and lifted clear of the tick line by
	/// <see cref="MarkLift"/>.
	/// </summary>
	public ((int X, int Y) Bottom, (int X, int Y) Left, (int X, int Y) Top, (int X, int Y) Right)
			Diamond(short bearingError) {
		int centre = Left + ((Right - Left) >> 1);
		int x = centre + bearingError * (Right - Left) / Range + DiamondNudgeX;
		int y = Top + MarkLift;

		return ((x, y + DiamondHalfHeight), (x - DiamondHalfWidth, y),
			(x, y - DiamondHalfHeight), (x + DiamondHalfWidth, y));
	}

	/// <summary>
	/// The arrow's three corners — tip first, then the two base ends — in device pixels. Its base sits
	/// <see cref="ArrowGap"/> past the tape's end and its tip <see cref="ArrowLength"/> beyond that. The paint
	/// swaps the base ends between the two arrows — the lower one second on the right-hand arrow, the upper one on
	/// the left-hand — so both outlines wind clockwise, which the polygon fill needs.
	/// </summary>
	public ((int X, int Y) Tip, (int X, int Y) BaseA, (int X, int Y) BaseB) Arrow(bool pointsRight) {
		int y = Top + MarkLift;
		int baseX = pointsRight ? Right + ArrowGap : Left - ArrowGap;
		int tipX = pointsRight ? baseX + ArrowLength : baseX - ArrowLength;
		int half = pointsRight ? ArrowHalfHeight : -ArrowHalfHeight;

		return ((tipX, y), (baseX, y + half), (baseX, y - half));
	}

	/// <summary>
	/// The caption's font: <c>DAT_0049b0f0</c>, <c>ColorSchemePanels</c> entry 17 — the same face the
	/// speed and time <i>values</i> use, and the reason the line is cyan rather than the captions'
	/// yellow-green.
	/// </summary>
	public const string LabelFont = "HUD3";

	/// <summary>
	/// <c>STRINGS0.STR</c> group 37 — the two-entry group whose second string is <c>"WAYPOINT "</c>,
	/// trailing space included. Its first is the <c>ATT</c> legend's; see docs/retail/formats/str-strings.md.
	/// </summary>
	public const int CaptionGroup = 37;

	/// <inheritdoc cref="CaptionGroup"/>
	public const int CaptionIndex = 1;

	// The constructor's own literals, shifted the way it shifts them. Each is written as the raw
	// value times the coordinate scale so it reads against the disassembly, and the half-extents keep
	// the paint's own >> 1 rather than being pre-halved.
	private const int Scale = (int)CockpitArt.GauToPixelScale;

	/// <summary>Half the diamond's width — the paint's <c>(5 &lt;&lt; XCoordShift) &gt;&gt; 1</c>.</summary>
	private const int DiamondHalfWidth = 5 * Scale >> 1;

	/// <summary>And half its height.</summary>
	private const int DiamondHalfHeight = 5 * Scale >> 1;

	/// <summary>The diamond's x nudge, zero in the constructor and kept because the paint applies it.</summary>
	private const int DiamondNudgeX = 0 * Scale;

	/// <summary>
	/// How far above the tape's top edge both shapes sit. The constructor keeps a separate <c>-4</c>
	/// for the diamond and the arrow and gives both the same value.
	/// </summary>
	private const int MarkLift = -4 * Scale;

	/// <summary>The gap between the tape's end and the arrow's base.</summary>
	private const int ArrowGap = 2 * Scale;

	/// <summary>The arrow's length from base to tip.</summary>
	private const int ArrowLength = 4 * Scale;

	/// <summary>Half the arrow's base.</summary>
	private const int ArrowHalfHeight = 5 * Scale >> 1;

	/// <summary>How far below the tape's rect the caption's rect is dropped, in <c>.GAU</c> units.</summary>
	private const int LabelDrop = 3;
}
