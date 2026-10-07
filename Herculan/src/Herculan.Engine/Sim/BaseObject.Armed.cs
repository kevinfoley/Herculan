using HercWorks.Core.Data.File.Dat.Sim;
using Herculan.Engine.Numerics;
using Herculan.Engine.Sim.Ai;
using Herculan.Engine.World;

namespace Herculan.Engine.Sim;

/// <summary>
/// The armed structure's tick — <c>StructureArmedVtable</c>'s <c>+0x18</c> slot (<c>00404100</c>),
/// which the gun and missile towers install and which the mobile ground vehicle borrows to fight with.
/// It is the whole of how a defended base defends itself: acquire, lead, aim, fire.
/// </summary>
public sealed partial class BaseObject {
	/// <summary>The <c>PROJ.DAT</c> record a gun tower fires — see <see cref="GunProjectileIndex"/>.</summary>
	public ProjectileData.Projectile? GunProjectile { get; init; }

	/// <summary>The <c>PROJ.DAT</c> record a missile tower fires — see <see cref="MissileSubtype"/>.</summary>
	public ProjectileData.Projectile? MissileProjectile { get; init; }

	/// <summary>
	/// <c>PROJ.DAT</c> row 2 — the flat index <c>Bullet_Fire</c> is handed, and the same record the
	/// lead calculation takes its projectile speed from. The flyer's guns are the same row.
	/// </summary>
	public const int GunProjectileIndex = 2;

	/// <summary>The launcher subtype a missile tower fires — <c>Rocket_Fire</c>'s literal 0.</summary>
	public const short MissileSubtype = 0;

	/// <summary>
	/// <c>00404100</c> — one tick of an armed structure.
	///
	/// <para>Once the structure has fallen the whole thing gives way to <see cref="ThinkTick"/>, so a
	/// wrecked tower is an ordinary building again. While it stands:</para>
	///
	/// <list type="bullet">
	/// <item><b>Acquire</b> on a <see cref="RetargetInterval"/> countdown, through the shared
	/// <see cref="AiTargeting.SelectTarget"/> with <see cref="TargetFilter.RejectOwnClass"/> and
	/// <see cref="TargetFilter.IgnoreBearing"/> — a turret does not care which way a candidate lies,
	/// only how far. A target that walks past <see cref="DropRange"/> is dropped between
	/// acquisitions.</item>
	/// <item><b>Step the muzzle flash</b> while its countdown is running. This is the same
	/// per-sequence cell array the plain tick free-runs, driven as a one-shot instead: firing kicks
	/// the timer, and the timer stops reloading itself the moment the cell wraps back to zero.</item>
	/// <item><b>Lead</b> — a gun tower offsets its aim point along the target's own heading by
	/// <c>range * targetSpeed / projectileSpeed</c>. A missile tower does not: its rounds track.</item>
	/// <item><b>Aim</b> through <see cref="AimTurret"/>, which is also what drives the turret's two
	/// animation threads.</item>
	/// <item><b>Fire</b>, gated on the refire countdown, on the aim error being inside
	/// <see cref="FiringError"/> on both axes, on the target being inside <see cref="LeadRange"/> —
	/// and on the firing window being open. That last is a plain five-seconds-on, five-seconds-off
	/// duty cycle, so a tower spends half its life not shooting at all.</item>
	/// </list>
	/// </summary>
	private void ArmedThinkTick(SimWorld world) {
		if (Destroyed) {
			ThinkTick(world);
			return;
		}

		DeathSequenceTick(world);

		if (SimMath.CountdownTimerTick(ref _retargetTimer) == 0) {
			Target = AiTargeting.SelectTarget(world, this,
				TargetFilter.RejectOwnClass | TargetFilter.IgnoreBearing);
			_retargetTimer = RetargetInterval;
		}

		int range = Target is { } held ? Position.ApproxDistanceTo(held.Position) : 0;
		if (range > DropRange) {
			Target = null;
		}

		StepMuzzleFlash();

		if (Target is not { } target) {
			return;
		}

		bool gun = Type.Armament != BaseArmament.Launcher;
		var aim = target.AimPoint;

		if (range < LeadRange) {
			int projectileSpeed = gun ? GunProjectile?.Speed ?? 0 : 0;
			int targetSpeed = target.TravelSpeed;

			if (projectileSpeed > 0 && targetSpeed != 0) {
				// The original's own arithmetic, the range narrowed to a short before the multiply
				// and the eighth put back afterwards. Unlike the flyer's lead there is no range cap
				// on it, because the range is already gated to LeadRange above.
				aim = SimTrig.OffsetPointByBearing(aim, (short)target.Heading,
					(short)((short)(range >> 3) * targetSpeed * 8 / projectileSpeed));
			}
		}

		var (elevationError, traverseError) = AimTurret(world, aim);

		if (SimMath.CountdownTimerTick(ref _fireWindowTimer) == 0) {
			_fireWindowOpen = !_fireWindowOpen;
			_fireWindowTimer = FireWindowInterval;
		}

		if (SimMath.CountdownTimerTick(ref _refireTimer) != 0 || !_fireWindowOpen
				|| (ushort)(elevationError + FiringError) >= FiringError * 2
				|| (ushort)(traverseError + FiringError) >= FiringError * 2
				|| range >= LeadRange) {
			return;
		}

		Shoot(world, gun);
	}

	/// <summary>
	/// The trigger pull. A gun tower fires <b>both</b> barrels every time; a missile tower rolls
	/// <see cref="LaunchOdds"/> for the first and, only if that failed, again for the second — so it
	/// launches at most one round per opportunity and usually none, which is what makes a missile
	/// tower a rare threat and a gun tower a constant one.
	/// </summary>
	private void Shoot(SimWorld world, bool gun) {
		bool left = true;
		bool right = true;

		if (!gun) {
			left = world.Random.NextMasked(LaunchOdds) == 0;
			right = !left && world.Random.NextMasked(LaunchOdds) == 0;
		}

		if (!left && !right) {
			return;
		}

		int node = Animation?.TransformIdOfPart(TurretPartId) ?? -1;
		var muzzleFrame = Transform3.Concat(
			node >= 0 ? NodeTransform(node) : Transform3.Identity, WorldFrame);
		var direction = muzzleFrame.ToEuler();

		if (left) {
			FireOneBarrel(world, gun, muzzleFrame, Muzzle.X, direction);
		}

		if (right) {
			FireOneBarrel(world, gun, muzzleFrame, -Muzzle.X, direction);
		}

		_refireTimer = RefireDelay;

		// Kick the muzzle flash. Any non-zero value opens the gate; the original writes 1, which
		// expires on the next tick and so steps the cell immediately.
		_animCellTimer = 1;
	}

	private void FireOneBarrel(SimWorld world, bool gun, in Transform3 muzzleFrame, int x,
			(short X, short Y, short Z) direction) {
		var point = muzzleFrame.TransformPoint(x, Muzzle.Y, Muzzle.Z);

		if (gun) {
			if (GunProjectile is { } round) {
				world.FireBullet(round, point, direction, TravelSpeed, 0, this);
			}
		} else if (MissileProjectile is { } missile) {
			world.FireRocket(missile, point, direction, 0, this);
		}
	}

	/// <summary>
	/// The muzzle flash's one-shot step: while <see cref="_animCellTimer"/> is still running, advance
	/// the type's cell sequence on its own countdown, and reload that countdown <b>only</b> while the
	/// cell has not wrapped back to zero. So the flash plays the sequence through once and stops.
	/// </summary>
	private void StepMuzzleFlash() {
		if (_animCellTimer == 0 || Type.AnimCellSequence < 0
				|| SimMath.CountdownTimerTick(ref _animCellTimer) != 0) {
			return;
		}

		short cell = (short)((CellFrames[Type.AnimCellSequence] + 1) % _animCellCount);
		CellFrames[Type.AnimCellSequence] = cell;

		if (cell != 0) {
			_animCellTimer = Type.AnimCellInterval;
		}
	}

	/// <summary>One barrel's offset in the turret node's frame; the other is its mirror in X.</summary>
	private static readonly Vec3i Muzzle = new(300, 400, 0);

	/// <summary>
	/// <c>base+0x21d</c>'s reload — how often the turret looks for a target. Timer units, not
	/// milliseconds (<see cref="SimMath.CountdownTimerTick"/>): about 4.9 seconds.
	/// </summary>
	private const short RetargetInterval = 10000;

	/// <summary>Range past which a held target is let go. Well beyond anything the turret would hit.</summary>
	private const int DropRange = 60000;

	/// <summary>Range inside which the turret leads its target and will fire at all.</summary>
	private const int LeadRange = 40000;

	/// <summary>
	/// <c>base+0x211</c>'s reload — the refire delay. Timer units: about 0.73 seconds, so a gun tower
	/// puts a pair of rounds up a little under once a second while its window is open.
	/// </summary>
	private const short RefireDelay = 0x5dc;

	/// <summary>
	/// <c>base+0x219</c>'s reload, the pair at <c>004973e0</c> — both entries are 10000, so the
	/// firing window is open and shut for equal spells. In timer units that is about 4.9 seconds
	/// each, <b>not</b> ten: see <see cref="SimMath.CountdownTimerTick"/>.
	/// </summary>
	private const short FireWindowInterval = 10000;

	/// <summary>Aim error inside which the guns will fire, on either axis.</summary>
	private const short FiringError = 1000;

	/// <summary>The mask a launcher rolls against per barrel — one chance in 32.</summary>
	private const int LaunchOdds = 0x1f;

	private short _retargetTimer;
	private short _refireTimer;
	private short _fireWindowTimer;

	/// <summary><c>base+0x21b</c> — whether the firing window is currently open.</summary>
	private bool _fireWindowOpen;
}
