using System.Globalization;
using HercWorks.Core.Data.File;
using HercWorks.Core.Data.File.Dyn;
using Herculan.Engine.Content;
using Herculan.Engine.Numerics;
using Herculan.Es1.Panels;
using Herculan.Es1.Sim;
using Herculan.Es1.View;

namespace Herculan.Es1.Mfd;

/// <summary>
/// The MFD's SCANNER, mode 3 (docs/es1/simulation/mfd-scanner.md): frame 0 of <c>dba\radar</c> behind a plot of
/// one-pixel contacts turned with the view's heading, a square around the selected target, the range and the target's
/// distance in the bottom corners, and the <c>ACT</c> or <c>PASS</c> legend at the top left. Its slots are
/// <c>MFDRadarVtable</c> (<c>00093862</c>)'s.
/// </summary>
public sealed class Es1MfdScannerScreen : Es1MfdScreen {
	/// <summary>The plot's center from the rect's top-left, <c>+0x18</c>, and its radius <c>+0x20</c>, the pixels a contact at the range lands from it.</summary>
	public const int CenterX = 0x22, CenterY = 0x16, Radius = 21;

	/// <summary>
	/// <c>STRINGS0.STR</c>'s groups the labels read (docs/es1/formats/str-strings.md#the-groups): <c>ACT</c> (entry 0) and
	/// <c>PASS</c> (entry 1), <c>RNG:</c> and <c>DST:</c>.
	/// </summary>
	public const int LegendGroup = 25, RangeGroup = 26, DistanceGroup = 27;

	/// <summary>The pen of a friend's contact, side 0, and of a foe's by its class 0, 1, 2 or any other.</summary>
	public const byte FriendColor = 0x32, HercColor = 0x35, StructureColor = 0x37, FlyerColor = 0x3b, OtherColor = 0x3e;

	/// <summary>The outline of the square around the selection, brush mode 4.</summary>
	public const byte SelectionColor = 0x3b;

	/// <summary>The square's reach from the selection's pixel each way.</summary>
	public const int SelectionReach = 2;

	/// <summary>The readouts' width, each number zero-padded to it.</summary>
	public const int FieldWidth = 4;

	/// <summary><c>Label_SetRect</c>'s flags 1, which start the text at the anchor.</summary>
	private const int LeftAligned = 1;

	private readonly IReadOnlyList<HudFont?> _fonts;
	private readonly StringFile? _strings;
	private readonly DynamixBitmap? _picture;
	private readonly Es1Label _legend, _distanceCaption, _distance, _rangeCaption, _range;

	/// <summary>
	/// <c>MfdRadarScreen_Ctor</c> (<c>ES1DBSIM 00035298</c>)(0, <paramref name="rect"/>)
	/// (docs/es1/simulation/mfd-scanner.md#the-screen-object): the base; frame 0 of <c>dba\radar</c>; the center
	/// (<see cref="CenterX"/>, <see cref="CenterY"/>) from the rect's top-left; the scale 1, the range it was computed for
	/// 0 and the legend passive; and the five labels in <c>cpblue</c> with no plate, each placed from the top-left:
	/// <c>RNG:</c> (3, <c>0x21</c>)–(<c>0xf</c>, <c>0x26</c>) and the range (3, <c>0x26</c>)–(<c>0x13</c>, <c>0x2b</c>)
	/// from the left, <c>DST:</c> (<c>0x36</c>, <c>0x21</c>)–(<c>0x42</c>, <c>0x26</c>) and the distance (<c>0x32</c>,
	/// <c>0x26</c>)–(<c>0x42</c>, <c>0x2b</c>) to the right, and the legend (1, 2)–(<c>0xf</c>, 7) centered.
	/// </summary>
	/// <param name="radar"><c>MfdRadar_RadarBank</c> (<c>0009385c</c>), the frames of <c>dba\radar</c>, which the constructor loads once; no picture when null or empty.</param>
	internal Es1MfdScannerScreen(Es1Cockpit cockpit, Es1Rect rect, Es1MfdSharedBlock shared, IReadOnlyList<HudFont?> fonts,
			StringFile? strings, IReadOnlyList<DynamixBitmap>? radar)
		: base(cockpit, rect, shared) {
		_fonts = fonts;
		_strings = strings;
		_picture = radar is { Count: > 0 } frames ? frames[0] : null;
		Center = (rect.X0 + CenterX, rect.Y0 + CenterY);
		_rangeCaption = Label(LeftAligned, 3, 0x21, 0xf, 0x26);
		_range = Label(LeftAligned, 3, 0x26, 0x13, 0x2b);
		_distanceCaption = Label(Es1Label.RightAligned, 0x36, 0x21, 0x42, 0x26);
		_distance = Label(Es1Label.RightAligned, 0x32, 0x26, 0x42, 0x2b);
		_legend = Label(Es1Label.Centered, 1, 2, 0xf, 7);

		Es1Label Label(int flags, int x0, int y0, int x1, int y1) {
			var label = new Es1Label(Font(Es1CockpitViews.CpBlue));
			label.SetRect(flags, new Es1Rect(rect.X0 + x0, rect.Y0 + y0, rect.X0 + x1, rect.Y0 + y1));
			return label;
		}
	}

	/// <summary><c>+0x18</c>, the plot's center on the canvas.</summary>
	public (int X, int Y) Center { get; }

	/// <summary><c>+0x24</c>, world units per pixel: the range / <see cref="Radius"/>, truncated, 1 until the first paint.</summary>
	public int Scale { get; private set; } = 1;

	/// <summary><c>+0x28</c>, the range <see cref="Scale"/> was computed for, 0 at first.</summary>
	public int ScaledRange { get; private set; }

	/// <summary><c>+0x2c</c>, the legend: set for passive, clear for active; set at first.</summary>
	public bool Passive { get; private set; } = true;

	/// <summary>
	/// Slot <c>+4</c>, <c>MfdRadarScreen_Paint</c> (<c>000355a4</c>) (docs/es1/simulation/mfd-scanner.md#the-paint): a
	/// new range rescales the plot; frame 0 of <c>dba\radar</c> blitted at the rect's top-left; the legend passive while
	/// the cockpit's machine's radar <c>+0x90</c> is off, <c>PASS</c> in <c>cpred</c>, else <c>ACT</c> in
	/// <c>cpgreen</c>; each object of the live object list plotted (<see cref="Plot"/>); the selection's square outlined
	/// when it was plotted; and <c>DST:</c>, the selection's distance in meters (0 when it was not plotted), <c>RNG:</c>
	/// and the range in meters (<c>Hud_WorldUnitsToMetres</c>, (<i>u</i> / 1000) × 6), each number in
	/// <see cref="FieldWidth"/> zero-padded characters. With no machine, through which retail reads, it stops after the
	/// picture, an engine guard.
	/// </summary>
	public override void Paint() {
		short heading = Cockpit.ActiveViewObject?.View.Z ?? 0;
		int range = Shared.ScannerRange;
		if (range != ScaledRange) {
			ScaledRange = range;
			Scale = range / Radius;
		}

		var canvas = Cockpit.PushCanvasContext();
		canvas.Blit(_picture, Rect.X0, Rect.Y0);
		if (Cockpit.Machine is not { } machine) {
			return;
		}

		Passive = !machine.RadarOn;
		_legend.Font = Font(Passive ? Es1CockpitViews.CpRed : Es1CockpitViews.CpGreen);
		_legend.SetText(canvas, Text(LegendGroup, Passive ? 1 : 0));

		var selection = Cockpit.Selection;
		(int X, int Y)? selected = null;
		int selectedMeters = 0;
		foreach (var other in Cockpit.Globals.LiveObjects) {
			if (ReferenceEquals(other, machine) || Plot(canvas, machine, other, heading) is not { } contact) {
				continue;
			}

			if (ReferenceEquals(other, selection)) {
				selected = (contact.X, contact.Y);
				selectedMeters = Math.Abs(contact.Distance) / 1000 * 6;
			}
		}

		if (selected is { } at) {
			canvas.OutlineRect(at.X - SelectionReach, at.Y - SelectionReach, at.X + SelectionReach, at.Y + SelectionReach,
				SelectionColor);
		}

		_distanceCaption.SetText(canvas, Text(DistanceGroup, 0));
		_distance.SetText(canvas, Field(selectedMeters));
		_rangeCaption.SetText(canvas, Text(RangeGroup, 0));
		_range.SetText(canvas, Field(range / 1000 * 6));
	}

	/// <summary>Slot <c>+8</c>, <c>MfdRadarScreen_Update</c> (<c>0003558c</c>): the paint.</summary>
	public override void Update() => Paint();

	/// <summary>
	/// One object of the paint's walk (docs/es1/simulation/mfd-scanner.md#the-contacts): none while it is immobilized
	/// (<c>+0x9e</c>) or destroyed (<c>+0x93</c>) or its group holds an action (<c>+0x16</c>). Its position is its own
	/// when it is radar-visible (<c>+0x8f</c>) or its side is not 1, else <see cref="LastKnownPosition"/>. With
	/// <i>d</i> its <c>Math_FastMagnitude2D</c> distance from the machine, 1 for 0, it is plotted only within
	/// <see cref="ScaledRange"/>: <i>d</i> negated when the offset's <i>y</i> is positive, <i>a</i> = <c>0x4000</c> −
	/// <c>Math_AsinBam</c>((<i>dx</i> &lt;&lt; 14) / <i>d</i>) + <paramref name="heading"/>, and the pixel at the
	/// center plus <c>Math_Q14Multiply</c>(<i>d</i>, <c>Math_CosBam</c>(<i>a</i>)) and (<i>d</i>,
	/// <c>Math_CosBam</c>(<i>a</i> − <c>0x4000</c>)), each over <see cref="Scale"/>, truncated; its pen
	/// <see cref="FriendColor"/> for side 0, else by its class. Answers the pixel and the signed <i>d</i>, or null. An
	/// object with no group, through which retail reads, is not plotted, an engine guard.
	/// </summary>
	private (int X, int Y, int Distance)? Plot(Es1PanelCanvas canvas, Es1Mech machine, IEs1SimObject other, short heading) {
		if (other.Group is not { } group || other.Immobilized || other.Destroyed || group.Action != null) {
			return null;
		}

		sbyte side = (sbyte)group.Side;
		if ((other.RadarVisible || side != 1 ? other.Position : LastKnownPosition(other)) is not { } position) {
			return null;
		}

		int dx = position.X - machine.Position.X, dy = position.Y - machine.Position.Y;
		int d = SimMath.FastMagnitude2D(dx, dy);
		if (d == 0) {
			d = 1;
		}

		if (d >= ScaledRange) {
			return null;
		}

		if (dy > 0) {
			d = -d;
		}

		short a = unchecked((short)(0x4000 - SimTrig.Asin((short)((dx << 14) / d)) + heading));
		int x = SimMath.Q14MultiplyRounded(d, SimTrig.Cos(a)) / Scale + Center.X;
		int y = SimMath.Q14MultiplyRounded(d, SimTrig.Cos(unchecked((short)(a - 0x4000)))) / Scale + Center.Y;
		byte color = side == 0 ? FriendColor : other.TargetClass switch {
			Es1MfdStatusScreen.HercClass => HercColor,
			Es1MfdStatusScreen.StructureClass => StructureColor,
			Es1MfdStatusScreen.FlyerClass => FlyerColor,
			_ => OtherColor,
		};
		canvas.Plot(x, y, color);
		return (x, y, d);
	}

	/// <summary>
	/// The blinking last-known position of a side-1 object the radar has not shown up: none while the clock
	/// (<c>000981c4</c>) has bit <c>0x20</c> set; otherwise the point <c>+0x164</c> when the byte <c>+0xa1</c> is clear,
	/// and none when it is set. PLACEHOLDER: the port holds neither field, and answers none.
	/// <c>Mech_Constructor</c> sets <c>+0xa1</c> (docs/es1/simulation/mech.md#construction) and no ported code clears
	/// it, so a HERC, the only object the port builds, takes none either way; what clears it and what <c>+0x164</c>
	/// holds is docs/es1/simulation/mfd-scanner.md#open.
	/// </summary>
	private static Vec3i? LastKnownPosition(IEs1SimObject other) {
		_ = other;
		return null;
	}

	/// <summary><c>ltoa</c> in base 10, right-aligned and zero-padded in <see cref="FieldWidth"/> characters.</summary>
	private static string Field(int value) => value.ToString(CultureInfo.InvariantCulture).PadLeft(FieldWidth, '0');

	private string? Text(int group, int entry) => _strings?.Text(group, entry);

	private HudFont? Font(int index) => index < _fonts.Count ? _fonts[index] : null;
}
