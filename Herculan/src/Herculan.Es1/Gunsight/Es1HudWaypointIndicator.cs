using Herculan.Engine.Content;
using Herculan.Engine.Numerics;
using Herculan.Es1.Panels;
using Herculan.Es1.Sim;
using Herculan.Es1.View;

namespace Herculan.Es1.Gunsight;

/// <summary>
/// The waypoint indicator, <c>HudWaypointIndicator_Ctor</c> (<c>ES1DBSIM 0003475c</c>): a <c>0x4f</c>-byte rotation
/// indicator on <c>HNavGaugeVtable</c> (<c>00093816</c>) over the heading tape's rect, whose paint marks the next
/// waypoint of the machine's route on the tape or at its end and captions it
/// (docs/es1/simulation/cockpit-gunsight-hud.md#the-waypoint-indicator).
/// </summary>
public sealed class Es1HudWaypointIndicator : Es1HudRotationIndicator {
	/// <summary>The errors that put the marker off the tape: <see cref="OffRight"/> from the first to <c>0x7fff</c>, <see cref="OffLeft"/> from <c>0x8000</c> to the second.</summary>
	public const int RightFrom = 0xe39, LeftTo = 0xf1c7;

	/// <summary>The marker's brush colour.</summary>
	public const byte MarkerColor = 1;

	/// <summary>
	/// The constructor: the rotation indicator, the bytes <c>+0x49</c> and <c>+0x4a</c> cleared, and the caption
	/// <c>+0x4b</c>, <c>Label_Ctor</c> with <paramref name="font"/>, <c>ColorSchemePanels</c>' <c>cpgreen</c>
	/// (<c>00092d44</c>), and <c>Label_SetRect</c>(label, 2, the rect, {0, 6}).
	/// </summary>
	public Es1HudWaypointIndicator(Es1Cockpit cockpit, Es1GunsightBanks banks, Es1Rect rect, HudFont? font)
			: base(cockpit, banks, rect) {
		Caption = new Es1Label(font);
		Caption.SetRect(Es1Label.Centered, rect, 0, 6);
	}

	/// <summary><c>+0x3c</c>, the waypoint's point, its <i>x</i> and <i>y</i>.</summary>
	public (int X, int Y) Point { get; private set; }

	/// <summary><c>+0x45</c>, the waypoint's distance from the machine, in world units and then, once the caption is set, in metres.</summary>
	public int Distance { get; private set; }

	/// <summary><c>+0x49</c>, the waypoint off to the left.</summary>
	public bool OffLeft { get; private set; }

	/// <summary><c>+0x4a</c>, the waypoint off to the right.</summary>
	public bool OffRight { get; private set; }

	/// <summary><c>+0x4b</c>, the caption.</summary>
	public Es1Label Caption { get; }

	/// <summary>The caption's text as the last paint set it; null before one has.</summary>
	public string? CaptionText { get; private set; }

	/// <summary><c>HudWaypointIndicator_ShiftLimits</c> (<c>000347e8</c>), the add-delta slot: the delta added to both limits.</summary>
	public override void AddDelta(short delta) => ShiftLimits(delta);

	/// <summary>
	/// The marker's three points for the bearing in <see cref="Es1HudSlideBar.Value"/>: on the tape at <i>x</i> =
	/// <i>x1</i> − <see cref="Es1HudSlideBar.Map"/>(bearing), (<i>x</i>, <i>y0</i> + 8), (<i>x</i> − 2, <i>y0</i> + 5),
	/// (<i>x</i> + 2, <i>y0</i> + 5); off to the left (<i>x0</i>, <i>y0</i> + 6), (<i>x0</i>, <i>y0</i> + 10), (<i>x0</i>
	/// − 2, <i>y0</i> + 8); off to the right (<i>x1</i>, <i>y0</i> + 6), (<i>x1</i> + 2, <i>y0</i> + 8), (<i>x1</i>,
	/// <i>y0</i> + 10).
	/// </summary>
	public (int X, int Y)[] Marker() {
		if (OffLeft) {
			return [(Rect.X0, Rect.Y0 + 6), (Rect.X0, Rect.Y0 + 10), (Rect.X0 - 2, Rect.Y0 + 8)];
		}

		if (OffRight) {
			return [(Rect.X1, Rect.Y0 + 6), (Rect.X1 + 2, Rect.Y0 + 8), (Rect.X1, Rect.Y0 + 10)];
		}

		int x = Rect.X1 - Map(Value);
		return [(x, Rect.Y0 + 8), (x - 2, Rect.Y0 + 5), (x + 2, Rect.Y0 + 5)];
	}

	/// <summary>
	/// <c>Hud_UpdateWaypointIndicator</c> (<c>00034800</c>), the paint: nothing unless the cockpit's machine has a group
	/// whose route has a waypoint after the cursor, <i>n</i> being the cursor + 1. Then the waypoint's point, its
	/// distance (<c>Vec2_DistanceBetween</c>) and the bearing to it (<c>Math_Atan2Guarded</c> − <c>0x4000</c>) into the
	/// value; the error heading − bearing, unsigned 16-bit, sets <see cref="OffRight"/> from <see cref="RightFrom"/> to
	/// <c>0x7fff</c> and <see cref="OffLeft"/> from <c>0x8000</c> to <see cref="LeftTo"/>; in the canvas push the
	/// <see cref="Marker"/> filled with the brush {0, <see cref="MarkerColor"/>}; and the distance in metres
	/// (<c>Hud_WorldUnitsToMetres</c>, (<i>u</i> / 1000) × 6) and the caption <c>WP </c><i>n</i><c>: </c><i>m</i><c> M.</c>
	/// set with <c>Label_SetText</c>.
	/// </summary>
	public override void Paint() {
		if (Cockpit.Machine is not { Group: { Route: { } route } group } machine) {
			return;
		}

		// Route_WaypointAt (0001cfb8)(the cursor, n) answers null past the route's end; the cursor below the route's count
		// follows. A negative n, which no cursor reaches, draws nothing: an engine guard.
		int n = group.RouteCursor + 1;
		if (n < 0 || n >= route.Length) {
			return;
		}

		var waypoint = route[n];
		Point = (waypoint.X, waypoint.Y);
		Distance = SimMath.FastMagnitude2D(Point.X - machine.Position.X, Point.Y - machine.Position.Y);
		Value = Es1MissionGroup.HeadingToward(machine.Position, new Vec3i(Point.X, Point.Y, 0));
		int error = (ushort)(machine.Heading - Value);
		OffRight = error >= RightFrom && error <= 0x7fff;
		OffLeft = error >= 0x8000 && error <= LeftTo;

		var canvas = Cockpit.PushCanvasContext();
		canvas.FillPolygon(Marker(), MarkerColor);
		Distance = Distance / 1000 * 6;
		CaptionText = "WP " + n + ": " + Distance + " M.";
		Caption.SetText(canvas, CaptionText);
	}
}
