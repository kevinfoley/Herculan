using System.Numerics;
using HercWorks.Core.Data.File.Gau;
using HercWorks.Core.Data.Struct;
using Herculan.Engine.Numerics;
using Herculan.Engine.Render;
using Herculan.Engine.Sim;

namespace Herculan.Engine.Cockpit;

/// <summary>What the MISSILE CAM screen has up.</summary>
public enum MfdMissileCamPicture {
	/// <summary>Nothing painted yet.</summary>
	None,

	/// <summary>The whole screen in one colour, the flash that follows a hit — <see cref="MfdMissileCamState.FlashColorId"/>.</summary>
	Flash,

	/// <summary>The world from the round, under a cross and a ring — <see cref="MfdMissileCamState.View"/>.</summary>
	View,

	/// <summary>No round: the flooded screen and its labels.</summary>
	Labels,
}

/// <summary>
/// Where the missile camera stands and which way it looks: the round's position pushed sideways by
/// <see cref="MfdMissileCam.CameraOffset"/>, and the round's own euler triple.
/// </summary>
/// <param name="Eye">The camera's position, world units.</param>
/// <param name="Pitch">The round's euler X.</param>
/// <param name="Roll">The round's euler Y.</param>
/// <param name="Heading">The round's euler Z.</param>
public readonly record struct MissileCamView(Vec3i Eye, short Pitch, short Roll, short Heading);

/// <summary>One paint of the MISSILE CAM screen — see <see cref="MfdMissileCamScreen"/>.</summary>
/// <param name="Picture">Which of the paint's three arms produced it.</param>
/// <param name="FlashColorId">The flash's <c>COLORS.DAT</c> id this paint.</param>
/// <param name="View">The camera, for <see cref="MfdMissileCamPicture.View"/>.</param>
/// <param name="ReadyToLaunch">
/// Whether the labels read <c>READY TO</c> / <c>LAUNCH</c> — some launcher has rounds left — rather
/// than a lone <c>NONE</c>.
/// </param>
/// <param name="Locked">Whether the <c>LOCK</c> label shows lock: <c>mech+0x9b</c>, <see cref="MechObject.LockAcquired"/>.</param>
/// <param name="LockBlinkLit">With lock, whether the label's plate is on its lit half of the blink.</param>
public readonly record struct MfdMissileCamState(MfdMissileCamPicture Picture, int FlashColorId,
	MissileCamView View, bool ReadyToLaunch, bool Locked, bool LockBlinkLit);

/// <summary>
/// The MFD's MISSILE CAM screen, mode 5 — <c>MfdMissileViewScreen_Ctor</c> (<c>0043facc</c>), its
/// update slot <c>MfdMissileViewScreen_Update</c> (<c>004402ec</c>) and its paint
/// <c>MfdMissileViewScreen_Paint</c> (<c>0043fe1c</c>). See docs/retail/simulation/mfd.md,
/// "<c>MFDMissileView</c> — mode 5".
/// </summary>
public static class MfdMissileCam {
	/// <summary>
	/// How far the camera stands from the round, world units, along the round's own X axis — its right,
	/// level, whatever its pitch: the paint adds <c>Q14(500, cos h)</c> to x and
	/// <c>Q14(500, cos(h - 0x4000))</c> to y.
	/// </summary>
	public const int CameraOffset = 500;

	/// <summary>
	/// The view's focal length in device pixels: <c>View_Ctor</c>'s perspective shift of 7, so
	/// <c>2^7</c>. The cockpit's own is <c>2^9</c>.
	/// </summary>
	public const float FocalLengthPixels = 1 << 7;

	/// <summary>The view's near plane, world units — <c>View_Ctor</c>'s <c>0x80</c>.</summary>
	public const int NearPlaneUnits = 0x80;

	/// <summary>
	/// <c>COLORS.DAT</c> id 16 (<c>DAT_004d3c20</c>): what the view is flooded with before the world
	/// goes down — there is no sky — and the flash's lit half.
	/// </summary>
	public const int BackgroundColorId = 16;

	/// <summary>
	/// <c>COLORS.DAT</c> id 19 (<c>DAT_004d3c26</c>): the cross and the ring over the view, the flash's
	/// other half, and the <c>LOCK</c> plate's dark half.
	/// </summary>
	public const int MarkColorId = 19;

	/// <summary>
	/// How long the flash runs, coarse ticks — the paint's <c>0x1e</c>. It alternates its two colours
	/// on every paint for that long.
	/// </summary>
	public const int FlashTicks = 0x1e;

	/// <summary><c>STRINGS0.STR</c> group 35, <c>DAT_004d16e8</c>: <c>MISS</c>, <c>READY TO</c>, <c>LAUNCH</c>, <c>NONE</c>, <c>LOCK</c>.</summary>
	public const int StringGroup = 35;

	/// <summary>Entries of <see cref="StringGroup"/> the labels print.</summary>
	public const int ReadyToEntry = 1, LaunchEntry = 2, NoneEntry = 3, LockEntry = 4;

	/// <summary>
	/// The four labels' rects, GAU from the inset origin, from the constructor's four tables at
	/// <c>0049c374</c> (x), <c>0049c37c</c> (y), <c>0049c384</c> (width) and <c>0049c38c</c> (height),
	/// as <c>(x, y)</c>-<c>(x + width, y + height)</c>. All four are centred.
	/// </summary>
	public static readonly (int X0, int Y0, int X1, int Y1)[] LabelRects = {
		(15, 21, 83, 31),
		(15, 28, 83, 38),
		(42, 30, 56, 40),
		(37, 42, 61, 52),
	};

	/// <summary>Which of <see cref="LabelRects"/> each line takes. Label 2 is built and never written.</summary>
	public const int ReadyToLabel = 0, LaunchLabel = 1, LockLabel = 3;

	/// <summary>The font every label is built with — <c>ColorSchemePanels[2]</c>.</summary>
	public const string LabelFont = "CPRED";

	/// <summary>
	/// The <c>LOCK</c> label without lock: <c>ColorSchemePanels[0]</c> on a plate of <c>COLORS.DAT</c>
	/// id 4.
	/// </summary>
	public const string LockIdleFont = "CPBLUE";

	/// <inheritdoc cref="LockIdleFont"/>
	public const int LockIdlePlateColorId = 4;

	/// <summary>
	/// The <c>LOCK</c> label with lock: <c>ColorSchemePanels[3]</c> on a plate that blinks with bit
	/// <see cref="LockBlinkBit"/> of the coarse clock — <see cref="MarkColorId"/> while it is clear,
	/// id 9 while it is set.
	/// </summary>
	public const string LockFont = "CPYLW";

	/// <inheritdoc cref="LockFont"/>
	public const int LockLitPlateColorId = 9;

	/// <inheritdoc cref="LockFont"/>
	public const long LockBlinkBit = 0x20;

	/// <summary>
	/// The view's projection centre, device pixels from the inset origin — the same origin the
	/// screen's own context is built around as the nav map's is, <see cref="MfdLayout.ScreenCentre"/>.
	/// The cross runs through it the full width and height, and the ring is centred on it with a
	/// radius of half the centre's own y.
	/// </summary>
	public static int RingRadius(GAUFile gau) =>
		MfdLayout.ScreenCentre(gau) is { } centre ? centre.Y >> 1 : 0;

	/// <summary>
	/// The camera <c>MfdMissileViewScreen_Paint</c> places for <paramref name="round"/>.
	/// </summary>
	public static MissileCamView ViewFrom(Rocket round) {
		var (pitch, roll, heading) = round.Euler;
		var position = round.Position;
		return new MissileCamView(
			new Vec3i(
				position.X + SimMath.Q14Multiply(CameraOffset, SimTrig.Cos(heading)),
				position.Y + SimMath.Q14Multiply(CameraOffset, SimTrig.Cos(unchecked((short)(heading - 0x4000)))),
				position.Z),
			pitch, roll, heading);
	}

	/// <summary>
	/// <paramref name="view"/> as a <see cref="Camera"/> over a screen <paramref name="width"/> by
	/// <paramref name="height"/> device pixels projecting about <paramref name="centre"/>: the
	/// field of view is the angle <see cref="FocalLengthPixels"/> subtends over that height, so the
	/// world is drawn at the original's size whatever the window's.
	/// </summary>
	public static Camera CameraFor(MissileCamView view, int width, int height, (int X, int Y) centre) => new() {
		Position = view.Eye,
		Yaw = -view.Heading & 0xffff,
		Pitch = view.Pitch,
		Roll = view.Roll,
		FieldOfView = 2f * MathF.Atan(height / 2f / FocalLengthPixels),
		PrincipalPoint = new Vector2(centre.X / (float)Math.Max(width, 1), centre.Y / (float)Math.Max(height, 1)),
		NearPlane = WorldScale.DistanceToRender(NearPlaneUnits),
	};

	/// <summary>
	/// <c>CockpitView_SumLauncherCounts</c> (<c>00440348</c>): the rounds left across every weapon row
	/// whose mount is a launcher.
	/// </summary>
	/// <param name="rowCount">How many weapon rows the cockpit has — the herc's <c>.GAU</c> row count.</param>
	public static int LauncherRounds(WeaponMounts weapons, int rowCount) {
		int rounds = 0;
		foreach (var mount in weapons.Mounts) {
			if (mount.GaugeSlot >= 0 && mount.GaugeSlot < rowCount && mount.AmmoType != WeaponMount.NotAMissile) {
				rounds += mount.AmmoRounds;
			}
		}

		return rounds;
	}
}

/// <summary>
/// <c>MfdDisplay_SyncMissileCamMode</c> (<c>00447164</c>) and the half of <c>MfdDisplay_SetMode</c>
/// (<c>00446e38</c>) that answers it: the display switching itself to the missile camera while an
/// electro-optical launcher is armed, and back afterwards. See docs/retail/simulation/mfd.md, "Modes".
///
/// <para>Every mode change has to come through <see cref="SetMode"/>, which is what lets a screen
/// chosen by hand during the switch stand until the switch ends.</para>
/// </summary>
public sealed class MfdMissileCamSwitch {
	/// <summary><c>g_MfdMissileCamMode</c> (<c>0049cbc6</c>): 5 while the switch holds, -1 otherwise. -1 in the image.</summary>
	private int _mode = -1;

	/// <summary><c>g_MfdMissileCamArmed</c> (<c>0049cbc8</c>): whether the switch changes the screen when it starts. Set in the image.</summary>
	private bool _armed = true;

	/// <summary><c>g_MfdMissileCamSavedMode</c> (<c>0049cbc4</c>): the screen to go back to. 3 in the image.</summary>
	private MfdMode _saved = MfdMode.Scanner;

	/// <summary>
	/// Whether the switch holds. While it does, a squadmate's transmission does not take the screen
	/// over — <c>MfdDisplay_Update</c> tests this before it draws one.
	/// </summary>
	public bool Holding => _mode != -1;

	/// <summary>
	/// The switch's condition: the armed mount is on a weapon row and its <c>PROJ.DAT</c> record
	/// (<c>mount+0x20</c>) is a <see cref="ProjectileType.Rocket"/> of subtype
	/// <see cref="Rocket.PlayerFlownSubtype"/>. <c>CockpitView_FindArmedMountGauge</c> (<c>00434310</c>) finds the
	/// armed mount's row, and <c>WeaponGauge_MountProjectileType</c> (<c>00440a14</c>) and
	/// <c>WeaponGauge_MountProjectileSubtype</c> (<c>00440a3c</c>) read the record's type and subtype.
	/// </summary>
	/// <param name="rowCount">How many weapon rows the cockpit has.</param>
	public static bool Condition(WeaponMounts weapons, int rowCount) =>
		weapons.Slots.ElementAtOrDefault(weapons.Selected) is { } armed
		&& armed.GaugeSlot >= 0 && armed.GaugeSlot < rowCount
		&& armed.Projectile is { } record
		&& record.Type == ProjectileType.Rocket
		&& record.SubtypeId == Rocket.PlayerFlownSubtype;

	/// <summary>
	/// <c>MfdDisplay_SetMode</c>: the mode the display shows after <paramref name="requested"/> is
	/// asked for. A change while the switch holds releases it, so the switch neither moves the screen
	/// again nor brings the saved one back until it next starts.
	/// </summary>
	public MfdMode SetMode(MfdMode current, MfdMode requested) {
		if (requested == current) {
			return current;
		}

		if (_mode != -1) {
			_armed = false;
			_mode = -1;
		}

		return requested;
	}

	/// <summary>
	/// <c>MfdDisplay_SyncMissileCamMode</c>, once per display update: the mode the display shows after
	/// it, given whether <see cref="Condition"/> holds.
	/// </summary>
	public MfdMode Sync(MfdMode current, bool condition) {
		if (_mode == -1 && condition) {
			if (!_armed) {
				_mode = (int)MfdMode.MissileCam;
				_armed = true;
				return current;
			}

			_saved = current;
			current = SetMode(current, MfdMode.MissileCam);
			_mode = (int)MfdMode.MissileCam;
			return current;
		}

		if (_mode != -1 && !condition) {
			current = SetMode(current, _saved);
			_armed = true;
		}

		return current;
	}
}

/// <summary>
/// The MISSILE CAM screen object: <c>MfdMissileViewScreen_Update</c>'s choice of whether to paint,
/// and <c>MfdMissileViewScreen_Paint</c>. What it painted last stays on the screen until it paints
/// again, as the original's does.
/// </summary>
public sealed class MfdMissileCamScreen {
	/// <summary><c>screen+0x45</c>: the copy of <see cref="PlayerMissileState.Struck"/> that starts a flash once.</summary>
	private bool _flashStarted;

	/// <summary><c>screen+0x46</c>: the coarse tick the flash ends at.</summary>
	private long _flashDeadline;

	/// <summary>The tick the flash started on, which the per-paint alternation is counted from.</summary>
	private long _flashStartTick;

	private MfdMissileCamState _shown;

	/// <summary>
	/// <c>MfdMissileViewScreen_Update</c>: paints when there is a round to ride, when the machine has
	/// lock, or when the display asks for a full repaint, and otherwise leaves the last paint up.
	/// </summary>
	/// <param name="world">The world whose <see cref="PlayerMissileState.Round"/> the camera rides.</param>
	/// <param name="locked"><see cref="MechObject.LockAcquired"/>.</param>
	/// <param name="launcherRounds"><see cref="MfdMissileCam.LauncherRounds"/>.</param>
	/// <param name="coarseTicks">The coarse clock.</param>
	/// <param name="repaint">Whether the display repaints the whole screen this frame.</param>
	public MfdMissileCamState Update(SimWorld world, bool locked, int launcherRounds, long coarseTicks,
			bool repaint) {
		if (world.PlayerMissile.Round != null || locked || repaint) {
			_shown = Paint(world, locked, launcherRounds, coarseTicks);
		}

		return _shown;
	}

	/// <summary>
	/// <c>MfdMissileViewScreen_Paint</c> (<c>0043fe1c</c>), in its own order: a pending flash takes
	/// the whole paint; otherwise the round is looked for among those in flight and, when it is still
	/// there, the camera rides it; when it is not, the round is forgotten and the labels go up.
	///
	/// <para>The flash alternates its colours once a paint, and the original paints once per
	/// simulation tick, so the alternation is counted in ticks here: the host draws many frames to a
	/// tick.</para>
	/// </summary>
	private MfdMissileCamState Paint(SimWorld world, bool locked, int launcherRounds, long coarseTicks) {
		if (world.PlayerMissile.Struck) {
			if (!_flashStarted) {
				_flashDeadline = coarseTicks + MfdMissileCam.FlashTicks;
				_flashStartTick = world.TickCount;
				_flashStarted = true;
			}

			if (coarseTicks < _flashDeadline) {
				bool lit = ((world.TickCount - _flashStartTick) & 1) != 0;
				return new MfdMissileCamState(MfdMissileCamPicture.Flash,
					lit ? MfdMissileCam.BackgroundColorId : MfdMissileCam.MarkColorId, default, false, false, false);
			}

			_flashStarted = false;
			world.PlayerMissile.Struck = false;
		}

		if (world.PlayerMissile.Round is { } round) {
			if (world.RocketsInFlight.Contains(round)) {
				return new MfdMissileCamState(MfdMissileCamPicture.View, 0, MfdMissileCam.ViewFrom(round),
					false, false, false);
			}

			world.PlayerMissile.Round = null;
		}

		return new MfdMissileCamState(MfdMissileCamPicture.Labels, 0, default,
			ReadyToLaunch: launcherRounds != 0,
			Locked: locked,
			LockBlinkLit: (coarseTicks & MfdMissileCam.LockBlinkBit) != 0);
	}
}
