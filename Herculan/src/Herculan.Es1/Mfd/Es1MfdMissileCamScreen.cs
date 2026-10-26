using HercWorks.Core.Data.File;
using HercWorks.Core.Data.File.Dyn;
using Herculan.Engine.Content;
using Herculan.Engine.Numerics;
using Herculan.Es1.Panels;
using Herculan.Es1.Projectiles;
using Herculan.Es1.Scene;
using Herculan.Es1.Sim;
using Herculan.Es1.View;

namespace Herculan.Es1.Mfd;

/// <summary>
/// The MFD's MISSILE CAM, mode 5 (docs/es1/simulation/mfd-missile-camera.md): while one of the player's missiles is in
/// flight the world seen from beside it, a flash while an explosion near the camera lasts, and otherwise <c>READY TO</c>
/// <c>LAUNCH</c> or <c>NONE</c> over a <c>LOCK</c> legend that blinks while the gunsight has lock. Its slots are
/// <c>MFDMissileViewVtable</c> (<c>0009391a</c>)'s.
/// </summary>
public sealed class Es1MfdMissileCamScreen : Es1MfdScreen {
	/// <summary>The labels, <c>+0x2f</c>–<c>+0x3b</c>.</summary>
	public const int LabelCount = 4;

	/// <summary><c>STRINGS0.STR</c>'s group of the words (docs/es1/formats/str-strings.md#the-groups), and their entries.</summary>
	public const int WordGroup = 31, ReadyToEntry = 1, LaunchEntry = 2, NoneEntry = 3, LockEntry = 4;

	/// <summary>The rect's fill under the labels, and the strike flash's second color.</summary>
	public const byte Fill = 0x3f;

	/// <summary>The strike flash's first color, which it starts on.</summary>
	public const byte FlashColor = 0x3c;

	/// <summary>Label 2's fill, which the constructor sets; nothing writes the label.</summary>
	public const byte Label2Fill = 0x31;

	/// <summary><c>LOCK</c>'s fill without lock, in <c>cpblue</c>, and with it while the clock's <see cref="BlinkBit"/> is set, in <c>cpylw</c> (clear: <see cref="Fill"/>).</summary>
	public const byte LockOffFill = 0x30, LockBlinkFill = 0x35;

	/// <summary>The bit of the clock <c>000981c4</c> the <c>LOCK</c> legend blinks with, 32 counts on and 32 off.</summary>
	public const uint BlinkBit = 0x20;

	/// <summary>The reach from the camera within which an explosion starts the strike flash, <c>Point3I_DistanceBetween</c>'s.</summary>
	public const int FlashReach = 10000;

	/// <summary>How far the camera sits beside the missile, along the missile's own <i>x</i> axis.</summary>
	public const int CameraOffset = 500;

	/// <summary>The camera's near plane and perspective shift, <c>TSCamera_Ctor</c>'s: a focal length of 2⁷ = 128 pixels.</summary>
	public const int NearPlane = 0x80, FocalShift = 7;

	/// <summary>
	/// The horizon's band height <c>+0x54</c> and count <c>+0x58</c>, and its ground's <c>+0x5c</c> and <c>+0x60</c>,
	/// which the constructor writes over <c>Hzline_Ctor</c>'s.
	/// </summary>
	public const int SkyBandHeight = 1, SkyBandCount = 3, GroundBandHeight = 1, GroundBandCount = 3;

	/// <summary><c>Hzline_SetColors</c>(horizon, <see cref="LineOffset"/>, <see cref="GroundColor"/>, <see cref="ZenithColor"/>).</summary>
	public const int LineOffset = 0, GroundColor = 0x16, ZenithColor = 0x10;

	/// <summary>The horizon's skyline bank, <c>dba\smlwld2</c>.</summary>
	public const string SkylineBank = "smlwld2";

	/// <summary>The labels' <i>x</i>, <i>y</i>, width and height from the rect's top-left, the <c>int16</c> tables <c>000938f4</c>, <c>000938fc</c>, <c>00093904</c> and <c>0009390c</c>.</summary>
	private static readonly int[] LabelX = [0, 0, 0x1b, 0x16], LabelY = [1, 8, 0x14, 0x20],
		LabelWidth = [0x44, 0x44, 0xe, 0x18], LabelHeight = [0xa, 0xa, 0xa, 0xa];

	private readonly Es1Label[] _labels = new Es1Label[LabelCount];
	private readonly IReadOnlyList<HudFont?> _fonts;
	private readonly StringFile? _strings;

	/// <summary>
	/// <c>MfdMissileViewScreen_Ctor</c> (<c>ES1DBSIM 000360c0</c>)(0, <paramref name="rect"/>)
	/// (docs/es1/simulation/mfd-missile-camera.md#the-screen-object): the base; the viewport over the rect, clip mode 2,
	/// its origin −((<i>x1</i> − <i>x0</i>) &gt;&gt; 1), −((<i>y1</i> − <i>y0</i>) &gt;&gt; 1), so its (0, 0) is the
	/// rect's center, and its rect the same as <see cref="Es1MfdScreen.Rect"/>, which is not kept apart; the camera at
	/// (0, 0, 0) with no angles; and the four labels in <c>cpred</c>, each centered with no margin over the rect's
	/// top-left plus its <i>x</i> and <i>y</i> to that point plus its width and height, label 2 then in <c>cpylw</c> on
	/// <see cref="Label2Fill"/>. The other fields are the heap's zeros. The horizon is the view's (<see cref="Horizon"/>,
	/// <see cref="SkylineBank"/>).
	/// </summary>
	internal Es1MfdMissileCamScreen(Es1Cockpit cockpit, Es1Rect rect, Es1MfdSharedBlock shared, IReadOnlyList<HudFont?> fonts,
			StringFile? strings)
		: base(cockpit, rect, shared) {
		_fonts = fonts;
		_strings = strings;
		ViewportOrigin = (-((rect.X1 - rect.X0) >> 1), -((rect.Y1 - rect.Y0) >> 1));
		for (int i = 0; i < LabelCount; i++) {
			_labels[i] = new Es1Label(Font(Es1CockpitViews.CpRed));
			int x0 = rect.X0 + LabelX[i], y0 = rect.Y0 + LabelY[i];
			_labels[i].SetRect(Es1Label.Centered, new Es1Rect(x0, y0, x0 + LabelWidth[i], y0 + LabelHeight[i]));
		}

		_labels[2].Font = Font(Es1CockpitViews.CpYellow);
		_labels[2].FillColor = Label2Fill;
	}

	/// <summary>The missile the view rides, <see cref="Es1ProjectilePool.TrackedMissile"/>, which the paint clears once it is gone.</summary>
	public Es1Projectile? TrackedMissile {
		get => Cockpit.Globals.Projectiles.TrackedMissile;
		private set => Cockpit.Globals.Projectiles.TrackedMissile = value;
	}

	/// <summary><c>+0x0c</c>'s origin <c>+0x220</c>/<c>+0x224</c>, the viewport the view draws into.</summary>
	public (int X, int Y) ViewportOrigin { get; }

	/// <summary><c>+0x10</c>'s position <c>+0</c>, the camera's, as the last view placed it.</summary>
	public Vec3i CameraPosition { get; private set; }

	/// <summary><c>+0x10</c>'s angles <c>+0xc</c>: pitch, roll and heading, as the last view placed them.</summary>
	public (short Pitch, short Roll, short Heading) CameraAngles { get; private set; }

	/// <summary><c>+0x18</c>, the explosion of <c>g_ExplosionPool</c> the strike flash is showing, or null.</summary>
	public IEs1PoolTicked? FlashExplosion { get; private set; }

	/// <summary><c>+0x1c</c>, the flash's color toggle: set for <see cref="FlashColor"/>, clear for <see cref="Fill"/>.</summary>
	public bool FlashToggle { get; private set; }

	/// <summary><c>+0x1d</c>, the camera's position the last paint took, which a missile gone since leaves it at.</summary>
	public Vec3i SavedPosition { get; private set; }

	/// <summary><c>+0x29</c>, the camera's pitch, roll and heading the last paint took.</summary>
	public (short Pitch, short Roll, short Heading) SavedAngles { get; private set; }

	/// <summary><c>+0x2f</c>–<c>+0x3b</c>: <c>READY TO</c>, <c>LAUNCH</c> or <c>NONE</c>, one nothing writes, and <c>LOCK</c>.</summary>
	public IReadOnlyList<Es1Label> Labels => _labels;

	/// <summary><c>+0x3f</c>, the repaint counter <see cref="Update"/> raises to 2 and steps down.</summary>
	public byte RepaintCounter { get; private set; }

	/// <summary><c>+0x40</c>, <see cref="BlinkLockLabel"/>'s latch of the clock's <see cref="BlinkBit"/>.</summary>
	public bool BlinkLatch { get; private set; }

	/// <summary>
	/// Slot <c>+4</c>, <c>MfdMissileViewScreen_Paint</c> (<c>000364f8</c>) (docs/es1/simulation/mfd-missile-camera.md#painting),
	/// the first case that applies:
	/// <list type="number">
	/// <item>with <see cref="FlashExplosion"/> set and still in the pool, <see cref="FlashToggle"/> flipped and the rect
	/// filled in <see cref="FlashColor"/> when it is then set, else <see cref="Fill"/>; an explosion gone clears it and
	/// goes on;</item>
	/// <item>with <see cref="TrackedMissile"/> set, the pose: the missile's position and angles with
	/// <see cref="CameraOffset"/> pushed along its <i>x</i> axis (<see cref="PoseBeside"/>), saved in
	/// <see cref="SavedPosition"/> and <see cref="SavedAngles"/>, or, the missile gone from the pool, the saved pose and
	/// the missile cleared; then the first explosion within <see cref="FlashReach"/> of the pose starts the flash, kept
	/// in <see cref="FlashExplosion"/> with <see cref="FlashToggle"/> set and the rect filled in <see cref="FlashColor"/>;
	/// with none and the missile still tracked, the view from the pose (<see cref="DrawView"/>);</item>
	/// <item>with no missile tracked, after either, the labels: the rect filled in <see cref="Fill"/>; <c>NONE</c> on
	/// label 1 when <see cref="SumLauncherCounts"/> is 0, else <c>READY TO</c> on label 0 and <c>LAUNCH</c> on label 1;
	/// and <c>LOCK</c> on label 3, without the gunsight's lock (its state block's <see cref="Herculan.Es1.Gunsight.Es1GunsightState.LockLamp"/>)
	/// in <c>cpblue</c> on <see cref="LockOffFill"/>, with it in <c>cpylw</c> on <see cref="LockBlinkFill"/> while the
	/// clock's <see cref="BlinkBit"/> is set and on <see cref="Fill"/> while it is clear.</item>
	/// </list>
	/// </summary>
	public override void Paint() {
		var canvas = Cockpit.PushCanvasContext();
		if (FlashExplosion != null) {
			if (InExplosionPool(FlashExplosion)) {
				FlashToggle = !FlashToggle;
				canvas.FillRect(Rect.X0, Rect.Y0, Rect.X1, Rect.Y1, FlashToggle ? FlashColor : Fill);
				return;
			}

			FlashExplosion = null;
		}

		if (TrackedMissile != null) {
			Vec3i position;
			(short Pitch, short Roll, short Heading) angles;
			if (MissilePose(TrackedMissile) is { } missile) {
				(position, angles) = PoseBeside(missile.Position, missile.Angles);
				SavedPosition = position;
				SavedAngles = angles;
			} else {
				TrackedMissile = null;
				position = SavedPosition;
				angles = SavedAngles;
			}

			if (ExplosionNear(position) is { } explosion) {
				FlashExplosion = explosion;
				FlashToggle = true;
				canvas.FillRect(Rect.X0, Rect.Y0, Rect.X1, Rect.Y1, FlashColor);
				return;
			}

			if (TrackedMissile != null) {
				DrawView(canvas.Display, position, angles);
			}
		}

		if (TrackedMissile == null) {
			PaintLabels(canvas);
		}
	}

	/// <summary>
	/// Slot <c>+8</c>, <c>MfdMissileViewScreen_Update</c> (<c>000368cc</c>), which the display runs every frame
	/// (docs/es1/simulation/mfd-missile-camera.md#painting): <see cref="RepaintCounter"/> raised to 2 while a missile is
	/// tracked, a flash is up or the gunsight has lock; at 2 the paint, at 1 the rect copied between the pages
	/// (<see cref="Es1Cockpit.DrawToCockpit"/> with 0), each lowering it by 1.
	/// </summary>
	public override void Update() {
		if (TrackedMissile != null || FlashExplosion != null || Cockpit.Gunsight.State.LockLamp) {
			RepaintCounter = 2;
		}

		if (RepaintCounter == 2) {
			Paint();
			RepaintCounter--;
		} else if (RepaintCounter == 1) {
			Cockpit.DrawToCockpit(Rect, 0);
			RepaintCounter--;
		}
	}

	/// <summary>
	/// <c>MfdMissileViewScreen_BlinkLockLabel</c> (<c>000369a8</c>)(screen): when the clock's <see cref="BlinkBit"/>
	/// differs from <see cref="BlinkLatch"/>, it latched and label 3 put in <c>cpylw</c> on <see cref="LockBlinkFill"/>
	/// for the bit set or <see cref="Fill"/> for it clear, answering true; otherwise false. No caller is known
	/// (docs/es1/simulation/mfd-missile-camera.md#open).
	/// </summary>
	public bool BlinkLockLabel() {
		bool bit = (Cockpit.Globals.Clock.Clock & BlinkBit) != 0;
		if (bit == BlinkLatch) {
			return false;
		}

		BlinkLatch = bit;
		_labels[3].Font = Font(Es1CockpitViews.CpYellow);
		_labels[3].FillColor = bit ? LockBlinkFill : Fill;
		return true;
	}

	/// <summary>
	/// <c>CockpitView_SumLauncherCounts</c> (<c>00036930</c>)(screen), which does not read the screen: over the first
	/// <see cref="Es1Cockpit.WeaponRowCount"/> of the cockpit's weapon-row slots, each set row's mount's rounds by its
	/// ammunition subtype slot (<see cref="Es1WeaponMount.AmmunitionSubtype"/>), added when the slot answers other than
	/// <see cref="Es1WeaponMount.NotALauncher"/>: the missiles left across the launchers.
	/// </summary>
	public int SumLauncherCounts() {
		int sum = 0;
		var rows = Cockpit.WeaponRows;
		for (int i = 0; i < Cockpit.WeaponRowCount && i < rows.Count; i++) {
			if (rows[i] is { } row && row.Mount.AmmunitionSubtype(out short rounds) != Es1WeaponMount.NotALauncher) {
				sum += rounds;
			}
		}

		return sum;
	}

	/// <summary>
	/// The pose the view takes beside a missile at <paramref name="position"/> turned by <paramref name="angles"/>: its
	/// angles, and its position with <c>Math_Q14Multiply</c>(<see cref="CameraOffset"/>, <c>Math_CosBam</c>(<i>h</i>))
	/// added to <i>x</i> and <c>Math_Q14Multiply</c>(<see cref="CameraOffset"/>, <c>Math_CosBam</c>(<i>h</i> −
	/// <c>0x4000</c>)) to <i>y</i>, <i>h</i> the heading.
	/// </summary>
	public static (Vec3i Position, (short Pitch, short Roll, short Heading) Angles) PoseBeside(Vec3i position,
			(short Pitch, short Roll, short Heading) angles) {
		short heading = angles.Heading;
		int x = position.X + SimMath.Q14MultiplyRounded(CameraOffset, SimTrig.Cos(heading));
		int y = position.Y + SimMath.Q14MultiplyRounded(CameraOffset, SimTrig.Cos(unchecked((short)(heading - 0x4000))));
		return (new Vec3i(x, y, position.Z), angles);
	}

	/// <summary>
	/// The horizon <c>Hzline_Ctor</c>(0) builds and the constructor sets up, its colours from
	/// <paramref name="palette"/>: <see cref="SkyBandHeight"/>, <see cref="SkyBandCount"/>,
	/// <see cref="GroundBandHeight"/> and <see cref="GroundBandCount"/>, <see cref="LineOffset"/>,
	/// <see cref="GroundColor"/> and <see cref="ZenithColor"/>, and <c>Hzline_Ctor</c>'s <c>+0x6c</c>; its skyline is
	/// <see cref="SkylineBank"/>, and with <c>+0x70</c> cleared the ground is banded. Null as
	/// <see cref="Es1Horizon.From(int, int, int, int, int, int, int, int, DynamixPalette?)"/> answers.
	/// </summary>
	public static Es1Horizon? Horizon(DynamixPalette? palette) => Es1Horizon.From(ZenithColor, SkyBandHeight, SkyBandCount,
		GroundColor, GroundBandHeight, GroundBandCount, Es1Horizon.CtorGapField, LineOffset, palette);

	/// <summary>
	/// The view case of <see cref="Paint"/>: the camera given the pose, and its view drawn as a world pass
	/// (<see cref="Es1VideoMemory.DrawWorldView"/>, <see cref="Es1RectScene.MissileCamera"/>) into the viewport: the
	/// rect itself, which the canvas context's origin is not taken from as the NAV MAP's is, less the shake offset
	/// (<see cref="Es1Cockpit.ShakeOffset"/>), the projection centre at <see cref="ViewportOrigin"/> negated from its
	/// top-left. The viewport's rect is the shake's only change, so nothing is put back.
	/// </summary>
	private void DrawView(Es1VideoMemory display, Vec3i position, (short Pitch, short Roll, short Heading) angles) {
		CameraPosition = position;
		CameraAngles = angles;
		var (shakeX, shakeY) = Cockpit.ShakeOffset;
		var viewport = new Es1Rect(Rect.X0 - shakeX, Rect.Y0 - shakeY, Rect.X1 - shakeX, Rect.Y1 - shakeY);
		display.DrawWorldView(viewport, new Es1RectView(CameraPosition, CameraAngles, NearPlane, FocalShift,
			(-ViewportOrigin.X, -ViewportOrigin.Y), viewport.X0, Es1RectScene.MissileCamera));
	}

	/// <summary>The labels case of <see cref="Paint"/>.</summary>
	private void PaintLabels(Es1PanelCanvas canvas) {
		canvas.FillRect(Rect.X0, Rect.Y0, Rect.X1, Rect.Y1, Fill);
		if (SumLauncherCounts() == 0) {
			_labels[1].SetText(canvas, Text(NoneEntry));
		} else {
			_labels[0].SetText(canvas, Text(ReadyToEntry));
			_labels[1].SetText(canvas, Text(LaunchEntry));
		}

		var lockLabel = _labels[3];
		if (!Cockpit.Gunsight.State.LockLamp) {
			lockLabel.Font = Font(Es1CockpitViews.CpBlue);
			lockLabel.FillColor = LockOffFill;
		} else {
			lockLabel.Font = Font(Es1CockpitViews.CpYellow);
			lockLabel.FillColor = (Cockpit.Globals.Clock.Clock & BlinkBit) != 0 ? LockBlinkFill : Fill;
		}

		lockLabel.SetText(canvas, Text(LockEntry));
	}

	/// <summary>
	/// <c>Pool_Prev</c>'s walk of <c>g_ExplosionPool</c> for <paramref name="explosion"/>. PLACEHOLDER: answers false:
	/// explosions are plan step 7, and the screen does not yet reach the simulation's pool; with
	/// <see cref="ExplosionNear"/> finding none, nothing sets <see cref="FlashExplosion"/> to ask about.
	/// </summary>
	private static bool InExplosionPool(IEs1PoolTicked explosion) {
		_ = explosion;
		return false;
	}

	/// <summary>
	/// <c>Pool_Prev</c>'s walk of the projectile pool <c>000af0b4</c> for <paramref name="missile"/>, and, found, its
	/// position <c>+0x20</c>–<c>+0x28</c> and its pitch, roll and heading <c>+6</c>–<c>+0xa</c>.
	/// </summary>
	private (Vec3i Position, (short Pitch, short Roll, short Heading) Angles)? MissilePose(Es1Projectile missile) =>
		Cockpit.Globals.Projectiles.Contains(missile) ? (missile.Position, missile.Angles) : null;

	/// <summary>
	/// <c>Pool_Prev</c>'s walk of <c>g_ExplosionPool</c> for the first explosion whose position <c>+0x20</c> lies within
	/// <see cref="FlashReach"/> of <paramref name="position"/> (<c>Point3I_DistanceBetween</c>). PLACEHOLDER: answers
	/// none: explosions are plan step 7.
	/// </summary>
	private static IEs1PoolTicked? ExplosionNear(Vec3i position) {
		_ = position;
		return null;
	}

	private string? Text(int entry) => _strings?.Text(WordGroup, entry);

	private HudFont? Font(int index) => index < _fonts.Count ? _fonts[index] : null;
}
