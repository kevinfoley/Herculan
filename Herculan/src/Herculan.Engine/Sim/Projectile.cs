using HercWorks.Core.Data.File.Dat.Sim;
using HercWorks.Core.Data.Struct;
using HercWorks.Core.Data.Struct.Dbsim;
using Herculan.Engine.Numerics;

namespace Herculan.Engine.Sim;

/// <summary>
/// A travelling shot — DBSIM's <c>Bullet</c> class, built by <c>Bullet_Construct</c>
/// (<c>0040af6c</c>, vtable <c>PTR_FUN_00498628</c>) and advanced by <c>Bullet_TickUpdate</c>
/// (<c>0040b124</c>, vtable <c>+0x14</c>).
///
/// <para>This is the other half of the fire dispatch. A <c>PROJ.DAT</c> record of type
/// <see cref="ProjectileType.Beam"/> carries <c>Speed == 0</c> and resolves its whole life inside
/// the call that fired it (see <see cref="BeamTracer"/>); a record of type
/// <see cref="ProjectileType.Bullet"/> carries a real speed and becomes one of these, which crosses
/// the ground between the muzzle and whatever it hits over several ticks. Every autocannon, every
/// EMP cannon and the plasma cannon fire one.</para>
///
/// <para>Like a tracer it is <b>not</b> a <see cref="SimObject"/> in the original either: it is
/// allocated from the same effect pool (<c>g_ProjectilePool</c> (<c>004a9746</c>)) that tracers come from, which
/// <c>Sim_MainTick</c> walks ahead of the machine list, and it is not in the list the shared raycast
/// sweeps. So a bullet cannot be shot down and cannot be shot at, only shot with.</para>
///
/// <para><b>A shot's whole geometry is one transform.</b> The object carries a euler triple
/// (<c>+0x0c</c>) and a transform (<c>+0x12</c>) whose translation <i>is</i> the shot's position
/// (<c>+0x26</c>); the transform is rebuilt from the triple whenever a dirty flag at <c>+0x32</c>
/// says the angles moved, which for everything but the homing branch means once, on the first tick.
/// Flight is then <c>position = transform * (0, step, 0)</c> — the same "forward is model Y"
/// convention the muzzle frame and the beam ray both use.</para>
/// </summary>
public sealed class Projectile {
	private readonly BulletType _record;
	private Transform3 _frame;
	private short _eulerX;
	private short _eulerY;
	private short _eulerZ;
	private bool _frameStale;
	private short _age;
	private short _animationTimer;

	/// <summary>
	/// <c>Bullet_Fire</c> (<c>0040b43c</c>) — the shared spawn both dispatches reach, and everything that happens
	/// between the allocation and the object's first tick.
	/// </summary>
	/// <param name="projectile">The firing <c>PROJ.DAT</c> record.</param>
	/// <param name="record">Its <c>BULLETS.DAT</c> record, looked up by the same subtype id.</param>
	/// <param name="muzzle">Where the shot starts — the fire prologue's world muzzle point.</param>
	/// <param name="aim">
	/// Which way it points, as the euler triple the prologue extracts from the shot transform
	/// (<c>Transform_RotationToEuler</c>, <c>0047f894</c>). The original passes angles rather than the matrix because this is where
	/// the scatter is applied, and a scattered angle is cheaper than a scattered matrix.
	/// </param>
	/// <param name="ownerSpeed">The firing machine's own travel speed — see <see cref="Speed"/>.</param>
	/// <param name="power">The charge the shot was fired at, or zero — see <see cref="Power"/>.</param>
	/// <param name="owner">The machine that fired, which the sweep skips.</param>
	/// <param name="random">The simulation's generator, for the scatter.</param>
	internal Projectile(ProjectileData.Projectile projectile, BulletType record,
			Vec3i muzzle, (short X, short Y, short Z) aim, short ownerSpeed, short power,
			SimObject? owner, SimRandom random) {
		Data = projectile;
		_record = record;
		Owner = owner;
		Power = power;

		// The scatter, and the only use BULLETS.DAT's field at +0x0a has: two of the three angles are
		// displaced by a uniform draw over [-spread, +spread]. The mask really is `spread * 2` and not
		// a power-of-two-minus-one, so with the retail 63 the draw lands on odd values only — kept
		// literally because it is what the weapon's dispersion actually is. The middle angle is roll
		// about the shot's own axis, which nothing can see, and the original leaves it alone.
		short spread = record.Scatter;
		_eulerX = (short)(aim.X + Scatter(random, spread));
		_eulerY = aim.Y;
		_eulerZ = (short)(aim.Z + Scatter(random, spread));
		_frameStale = true;

		_frame.X = muzzle.X;
		_frame.Y = muzzle.Y;
		_frame.Z = muzzle.Z;

		// A shot inherits the speed of whatever fired it, on top of the record's own — which is what
		// keeps a machine running forward from outrunning its own autocannon rounds.
		Speed = (short)(ownerSpeed + projectile.Speed);
		_animationTimer = record.FrameInterval;
	}

	/// <summary>The <c>PROJ.DAT</c> record this shot came from — its damage, its splash and its subtype id.</summary>
	public ProjectileData.Projectile Data { get; }

	/// <summary>
	/// The record's subtype id, <c>+0x41</c>. It is what indexes <see cref="BulletCatalog"/>, what
	/// picks the shape drawn, and — at <see cref="PlasmaSubtype"/> — what selects the homing and
	/// blast branch.
	/// </summary>
	public short SubtypeId => Data.SubtypeId;

	/// <summary>
	/// The subtype the tick singles out by literal value: 9, the plasma cannon's record. Two weapon
	/// ids reach it — 25 (<c>PLAS</c>) and 28, which the simulator's own name table calls
	/// <c>MAGN</c> and the shell catalog calls <c>MFAC</c>; both templates carry
	/// <c>ProjDatIndex</c> 22. It is the one <c>Bullet</c> record with a nonzero
	/// <c>SplashFactor</c>, and the only one that homes.
	/// </summary>
	public const short PlasmaSubtype = 9;

	/// <summary>The machine that fired. The sweep skips it, so nothing shoots itself.</summary>
	public SimObject? Owner { get; }

	/// <summary>
	/// <c>+0x56</c>. The capacitor charge the shot was fired at, or <b>zero</b> for a shot that was
	/// not fired from a capacitor at all — every autocannon round, which comes out of a magazine. Zero
	/// means the record's damage applies at face value; anything else scales both damage figures
	/// Q10, exactly as a beam's power does.
	/// </summary>
	public short Power { get; }

	/// <summary>
	/// <c>+0x52</c>. How far the shot travels per 125 ms, as
	/// <see cref="SimMath.IntegrateRateOverTick"/> reads a rate: the record's own <c>Speed</c> plus
	/// the firing machine's travel speed at the instant it left the barrel.
	/// </summary>
	public short Speed { get; }

	/// <summary>
	/// The shot's frame: where it is, and which way it is going. The translation is the position —
	/// the original keeps no separate one.
	///
	/// <para>Reading it settles the rotation if the angles have moved since the last tick, which is
	/// what the original's draw does too (<c>SimObject_InstallModelTransform</c> (<c>00401fe4</c>) runs the same dirty-flag rebuild before
	/// it installs the model transform). Without it a shot would be drawn unrotated for the frames
	/// between the tick that spawned it and the tick that first moves it.</para>
	/// </summary>
	public Transform3 Frame {
		get {
			RebuildFrame();
			return _frame;
		}
	}

	/// <summary>Where the shot is, in world units.</summary>
	public Vec3i Position => new(_frame.X, _frame.Y, _frame.Z);

	/// <summary>How far along its life the shot is, against <see cref="BulletType.Lifetime"/> scaled by <see cref="BulletCatalog.AgeRate"/>.</summary>
	public short Age => _age;

	/// <summary>
	/// What the shot struck, if it struck anything — set on the tick that ends its life. Not part of
	/// the original, which simply frees the object; it is here for the same reason
	/// <see cref="WeaponShot.HitObject"/> is.
	/// </summary>
	public SimObject? HitObject { get; private set; }

	/// <summary>Whether the shot ended by running out of life rather than by hitting something.</summary>
	public bool Expired { get; private set; }

	/// <summary>
	/// <c>Bullet_TickUpdate</c> (<c>0040b124</c>), vtable <c>+0x14</c> — one step of flight and the
	/// hit test that goes with it.
	///
	/// <list type="number">
	/// <item><b>Age.</b> A flat <c>0x200</c> per 125 ms, against the record's lifetime scaled by the
	/// same figure. A shot that outlives it is dropped where it is, with no impact of any kind.</item>
	/// <item><b>Homing</b>, for the plasma subtype alone and only when it was given a target.</item>
	/// <item><b>The step</b>, <c>IntegrateRateOverTick(speed)</c>, taken along the frame's Y axis.</item>
	/// <item><b>The hit test is a raycast over that step alone</b> — the shot record is built with the
	/// current frame and the step as the ray's length, so a bullet sweeps the segment it is about to
	/// cross rather than testing a point. That is what stops a fast round from tunnelling through a
	/// machine between ticks, and it is why the same <see cref="WeaponShot"/> a beam uses serves
	/// here.</item>
	/// <item>Struck anything and the shot ends; struck nothing and it moves.</item>
	/// </list>
	///
	/// <para>The record's <c>ClipRadius</c> takes the place of the beam's literal 200 as the shot
	/// record's slack, which is what makes the big EMP round (200) forgiving where an autocannon
	/// round (100) is not.</para>
	///
	/// <para><b>The plasma round ends differently.</b> <see cref="PlasmaSubtype"/> detonates rather
	/// than striking: <see cref="SimWorld.ExplosiveBlastSweep"/> at
	/// <see cref="PlasmaBlastRadius"/>, either where it touched something or, through its proximity
	/// fuze, the moment it has passed its target — see <see cref="PlasmaProximityFuze"/>.</para>
	///
	/// <para><b>The blast is the whole of the weapon.</b> The round empties its own shot record
	/// before it raycasts (<see cref="WeaponShot.StashDamage"/>), so the raycast reports contact and
	/// nothing more, and everything the round does it does through the sweep — which is why the
	/// struck object is <i>not</i> excluded from it. The one thing the stash is read back for is a
	/// structure struck on its collision-volume path; see <see cref="BaseObject.DirectFireHitTest"/>.</para>
	/// </summary>
	/// <returns>Whether the shot is finished and should be freed.</returns>
	internal bool Tick(SimWorld world) {
		AnimationTick();

		_age = (short)(_age + SimMath.IntegrateRateOverTick(BulletCatalog.AgeRate));
		if (_record.Lifetime * BulletCatalog.AgeRate < _age) {
			Expired = true;
			return true;
		}

		HomingTick();

		short step = (short)SimMath.IntegrateRateOverTick(Speed);
		RebuildFrame();
		var advanced = _frame.TransformPoint(0, step, 0);

		// The proximity fuze, which is ahead of the sweep in the original: a plasma round that has
		// gone past its target — the bearing error is over a quarter turn, so the target is behind it
		// — goes off where it is rather than flying on to nothing.
		if (SubtypeId == PlasmaSubtype && Target != null
				&& Position.ApproxDistanceTo(Target.AimPoint) < PlasmaProximityFuze) {
			var (_, _, bearing) = SimTrig.EulerToward(Target.AimPoint, Position);
			if ((ushort)(bearing - _eulerZ + BinaryAngle.QuarterTurn) >= BinaryAngle.HalfTurn) {
				Detonate(world, Position);
				return true;
			}
		}

		var shot = new WeaponShot(_frame, step, Data, Power, Owner, _record.ClipRadius);

		// The plasma round's own preparation, in the original's order: stash and empty before the
		// raycast, so that whatever it touches is only touched, not damaged.
		if (SubtypeId == PlasmaSubtype) {
			shot.StashDamage();
		}

		if (world.Raycast(shot) != 0) {
			HitObject = shot.HitObject;
			world.RecordProjectileHit(shot);
			Detonate(world, shot.Muzzle.TransformPoint(0, shot.Distance, 0));
			return true;
		}

		_frame.X = advanced.X;
		_frame.Y = advanced.Y;
		_frame.Z = advanced.Z;
		return false;
	}

	/// <summary>
	/// The plasma round's own ending, and nothing else's: a <see cref="PlasmaBlastRadius"/> sweep of
	/// the object list from wherever the round stopped. Every other subtype simply ceases to exist,
	/// its damage already applied inside the raycast.
	///
	/// <para>The sweep excludes nothing, so the object the round touched stands in the blast like
	/// everything else — which for a machine is the only damage it takes at all.</para>
	///
	/// <para>The blast's figure is the record's <b>armour</b> damage, power-scaled, and not its
	/// shield damage. On the one record this reaches the two are equal.</para>
	///
	/// <para>It is then scaled by the mission difficulty, which every other shot gets inside the
	/// raycast instead — this round cannot, having emptied its record before firing the ray. What
	/// <see cref="WeaponShot.StashDamage"/> put aside stays unscaled, as it does in the original:
	/// <c>Bullet_StashDirectFireDamage</c> runs before the scale.</para>
	/// </summary>
	private void Detonate(SimWorld world, Vec3i at) {
		if (SubtypeId != PlasmaSubtype) {
			return;
		}

		short damage = Power == 0
			? Data.DamageArmor
			: (short)SimMath.Q10Multiply(Power, Data.DamageArmor);

		if (Owner is { } attacker) {
			damage = (short)SimMath.Q10Multiply(world.DamageScaleFor(attacker.Side), damage);
		}

		world.ExplosiveBlastSweep(at, PlasmaBlastRadius, damage, Owner, null);
	}

	/// <summary>How far the plasma round's blast reaches — <c>Bullet_TickUpdate</c>'s own 4000.</summary>
	public const int PlasmaBlastRadius = 4000;

	/// <summary>
	/// How near its target a plasma round has to be for the fuze to arm — 2000 units. Past that
	/// range the round is only dangerous by contact, and inside it a target that has been overshot
	/// still takes the blast.
	/// </summary>
	public const int PlasmaProximityFuze = 2000;

	/// <summary>
	/// <c>Bullet_TickUpdate</c>'s opening step: the record's <c>+0x06</c> as a countdown, reloaded
	/// each time it expires, stepping the drawn shape's cell-animation frame on. Unlike an
	/// <see cref="ImpactEffect"/>, a round's flipbook simply loops — the wrap is not an ending.
	///
	/// <para>A zero interval means a static shape and the counter never moves, which is every
	/// autocannon round and the plasma round; the three EMP records are the only ones that set it,
	/// and their shapes are the flipbooks of billboards that need it (see
	/// <see cref="Render.DtsSpriteBuilder"/>).</para>
	///
	/// <para>The original also takes the step modulo the shape's own frame count for that sequence.
	/// Here the counter simply climbs and the renderer takes the modulo, which
	/// <c>TSCellAnimPart_Render</c> does anyway — same frame drawn, and the simulation stays clear of
	/// needing to know what the shape looks like.</para>
	/// </summary>
	private void AnimationTick() {
		if (_record.FrameInterval == 0) {
			return;
		}

		if (SimMath.CountdownTimerTick(ref _animationTimer) == 0) {
			_animationTimer = _record.FrameInterval;
			AnimationFrame++;
		}
	}

	/// <summary>
	/// The shape's cell-animation frame counter, which the renderer wraps against the drawn shape's
	/// own frame count. Zero and unmoving for every round whose shape is static.
	/// </summary>
	public int AnimationFrame { get; private set; }

	/// <summary>
	/// <c>Bullet_HomingSteer</c> (<c>0040aff0</c>) — the plasma cannon's guidance, which is a steer of the shot's own euler
	/// angles rather than of a velocity: the bearing to the target is taken in the shot's frame and
	/// the two aiming angles are moved toward it by at most <see cref="HomingTurnRate"/> per 125 ms,
	/// then the frame is marked stale so the next step flies the new way.
	///
	/// <para>The guidance reads the firing machine's <i>selected target</i> (<c>mech+0x1a4</c>),
	/// which <see cref="TargetSelection"/> fills in — see <see cref="SimWorld.FireBullet"/>. Fired
	/// with nothing selected a plasma shot flies straight, which is what the original does too.</para>
	/// </summary>
	private void HomingTick() {
		if (Target == null) {
			return;
		}

		var (bearingX, _, bearingZ) = SimTrig.EulerToward(Target.AimPoint, Position);

		short pitchRate = 0;
		short yawRate = 0;
		SimMath.RateLimitedMoveToward(ref pitchRate, (short)(bearingX - _eulerX), HomingTurnRate);
		SimMath.RateLimitedMoveToward(ref yawRate, (short)(bearingZ - _eulerZ), HomingTurnRate);

		_eulerX = (short)(_eulerX + SimMath.IntegrateRateOverTick(pitchRate));
		_eulerZ = (short)(_eulerZ + SimMath.IntegrateRateOverTick(yawRate));
		_frameStale = true;
	}

	/// <summary>The cap <c>Bullet_HomingSteer</c> (<c>0040aff0</c>) puts on how fast a homing shot may turn — <c>0x280</c> per 125 ms.</summary>
	public const short HomingTurnRate = 0x280;

	/// <summary>
	/// <c>+0x5b</c>, what a homing shot is chasing. Only <c>Bullet_FirePowered</c> (<c>0040b5a0</c>) ever sets it, and only
	/// for <see cref="PlasmaSubtype"/>, from the firing machine's own selected target.
	/// </summary>
	public SimObject? Target { get; internal set; }

	/// <summary>
	/// The dirty-flag rebuild at <c>+0x32</c>, which the tick performs twice and the draw a third
	/// time. The translation is left alone: <c>BuildEulerRotationMatrixQ14</c> writes the rotation
	/// and the kind tag and nothing else, and the shot's position lives past them in the same struct.
	/// </summary>
	private void RebuildFrame() {
		if (!_frameStale) {
			return;
		}

		var rotation = Transform3.FromEuler(_eulerX, _eulerY, _eulerZ);
		rotation.X = _frame.X;
		rotation.Y = _frame.Y;
		rotation.Z = _frame.Z;
		_frame = rotation;
		_frameStale = false;
	}

	/// <summary>One draw from the scatter distribution — <c>(spread * 2 &amp; random) - spread</c>.</summary>
	private static short Scatter(SimRandom random, short spread) =>
		spread == 0 ? (short)0 : (short)(random.NextMasked(spread * 2) - spread);
}
