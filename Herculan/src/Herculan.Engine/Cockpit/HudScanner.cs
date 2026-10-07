using HercWorks.Core.Data.File.Gau;
using Herculan.Engine.Content;

namespace Herculan.Engine.Cockpit;

/// <summary>
/// The front window's floating scanner repeater — <c>HudScanner_Paint</c> (<c>0043f2b0</c>), reached from
/// <c>Gunsight_Paint</c> (<c>0043d5c8</c>) and <c>Gunsight_UpdateAndPaint</c> (<c>0043d6dc</c>)
/// through the one-line <c>Gunsight_PaintHudScanner</c> (<c>0043e0ec</c>). It is the last thing the gunsight complex draws.
///
/// <para>It plots the <b>same contact list</b> as the MFD's F4 screen — it calls that screen's own
/// update slot to rebuild it, reaching the screen object through
/// <c>CockpitView+0x1ed</c>'s <c>+0xd9</c> — and it draws only when that screen is <b>not</b> the
/// display's current one. So the two are never on screen together, and between them the contact list
/// is rebuilt every frame.</para>
///
/// <para>What it draws is not a small copy of the screen: no dish art, no wedge sprite, no
/// background, no reference line, no range ring and no readouts. It is a bare red circle with a
/// V of two lines for the turret arc, the same player-marker and target-bracket sprites, and blips
/// drawn as outlined dots.</para>
/// </summary>
public static class HudScanner {
	/// <summary>
	/// The repeater's square extent, GAU units on both axes — the paint's own <c>0x2e</c>, applied
	/// with the x shift for width and the y shift for height. Nothing in the <c>.GAU</c> states it;
	/// the file supplies the top-left only.
	/// </summary>
	public const int SizeGau = 0x2e;

	/// <summary>
	/// Half the extent in device pixels, which is both the plot radius and what the contact scale is
	/// built from: <c>scale = range / halfSize</c>. Note the divisor is the <b>half-size</b>, not the
	/// MFD screen's separate <see cref="MfdScanner.PlotRadiusGau"/> — a contact at the display range
	/// lands on this circle's rim, where on the MFD screen it stops short of the dish's.
	/// </summary>
	public static int HalfSizeDevice => SizeGau * (int)CockpitArt.GauToPixelScale / 2;

	/// <summary>
	/// Half-angle of the turret arc, drawn as two lines from the centre at
	/// <c>twist -/+ 0x2000</c> — 45 degrees each side, the same 90-degree arc the MFD screen's wedge
	/// sprite covers.
	/// </summary>
	public const short ArcHalfAngle = 0x2000;

	/// <summary>
	/// The circle and the two arc lines: <c>COLORS.DAT</c> id 9, palette 10, red. The circle is drawn
	/// with the brush in outline mode, the lines with the pen.
	/// </summary>
	public const int OutlineColorId = 9;

	/// <summary>
	/// A blip is two filled dots, an outline and a core: radius 2 in this colour (id 19, palette 16,
	/// black) with radius 1 in the contact's own colour inside it. Both radii are literal device
	/// pixels, unshifted, so a blip is the same size in every video mode.
	/// </summary>
	public const int BlipOutlineColorId = 19;

	/// <inheritdoc cref="BlipOutlineColorId"/>
	public const int BlipOutlineRadius = 2;

	/// <inheritdoc cref="BlipOutlineColorId"/>
	public const int BlipCoreRadius = 1;

	/// <summary>
	/// GAU units the player marker is blitted left of the centre, as on the MFD screen. Same sprite,
	/// same <c>3 &lt;&lt; XCoordShift</c>, same apex-on-the-centre placement.
	/// </summary>
	public const int PlayerMarkerOffsetX = MfdScanner.PlayerMarkerOffsetX;

	/// <summary>And the same target bracket, offset the same way.</summary>
	public const int TargetBracketOffset = MfdScanner.TargetBracketOffset;

	/// <summary>
	/// The repeater's top-left in GAU units, or null when the herc's <c>.GAU</c> has no point at
	/// offset 1196. Position is per herc: APOCA <c>40,27</c>, SAMSON <c>51,5</c>, OGRE <c>67,80</c>,
	/// RAZOR <c>15,20</c>.
	/// </summary>
	public static (int X, int Y)? Origin(GAUFile gau) =>
		gau.HudScanner is { } point ? (point.Origin.X, point.Origin.Y) : null;

	/// <summary>World units one device pixel of the plot covers — the paint's <c>range / halfSize</c>.</summary>
	public static int WorldUnitsPerPixel(int range) => Math.Max(range / HalfSizeDevice, 1);
}
