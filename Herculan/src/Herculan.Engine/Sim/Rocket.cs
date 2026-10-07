using HercWorks.Core.Data.File.Dat.Sim;
using HercWorks.Core.Data.Struct;
using HercWorks.Core.Data.Struct.Dbsim;
using Herculan.Engine.Numerics;

namespace Herculan.Engine.Sim;

/// <summary>
/// A launcher's round — DBSIM's <c>ROCKET</c> class, built by <c>Rocket_Construct</c>
/// (<c>0040a948</c>, vtable <c>PTR_Bullet_Draw_00498448</c>) and advanced by
/// <c>Rocket_TickUpdate</c> (<c>0040a538</c>, vtable <c>+0x14</c>). It is the third and last fire
/// branch, and the one weapon class that fired nothing at all until now.
///
/// <para>Mechanically it is a <see cref="Projectile"/> with a different engine in it. Both are
/// allocated out of the same effect pool (<c>g_ProjectilePool</c> (<c>004a9746</c>)) that <c>Sim_MainTick</c> walks ahead
/// of the machine list, both carry a euler triple plus a transform whose translation is the
/// position, and both sweep the segment they are about to cross rather than testing a point. What is
/// different is everything about how they move:</para>
///
/// <list type="bullet">
/// <item><b>It accelerates.</b> A gun round leaves the barrel at its final speed; a rocket leaves at
/// a flat <see cref="LaunchSpeed"/> over the launching machine's own travel speed and climbs from
/// there toward the <c>PROJ.DAT</c> record's <c>Speed</c>. That ceiling is 6000 on every retail
/// launcher and the round never reaches it: the burn adds about 39 per tick and the round has 80
/// ticks to live, so it burns out at roughly 3600 — a rocket is slow off the rail and still
/// accelerating when it arrives.</item>
/// <item><b>Its lifetime is in ticks.</b> A bullet ages by <c>0x200</c> per tick against
/// <c>Lifetime * 0x200</c>; a rocket's counter is a plain <c>+1</c> against <c>Lifetime</c>
/// directly.</item>
/// <item><b>It has no firing scatter</b> and no power scaling — a launcher is neither a magazine
/// weapon that disperses nor a capacitor weapon whose shot is worth what it was charged to. The
/// aim angles go in exactly as the mount handed them over and the record's damage applies at face
/// value.</item>
/// <item><b>It is the class guidance was written for.</b> The plasma round borrows a cut-down
/// version; this one steers at a component of its target, has a per-subtype gate on whether it may
/// lock at all, and a player-flown branch. See <see cref="HomingTick"/>.</item>
/// </list>
///
/// <para>Only <c>PROJ.DAT</c> <see cref="ProjectileType.Rocket"/> records reach here.
/// <see cref="ProjectileType.Grenade"/> (type 3) records exist in retail data and no weapon template
/// names them: <c>Grenade_Construct</c> (<c>0040ac3c</c>) builds their class and no reference to it is
/// found (docs/retail/formats/proj-dat.md#open), and its
/// vtable's per-tick slot is <c>Grenade_TickNoOp</c> (<c>0040acb4</c>), a bare <c>return 0</c>, so an instance would never
/// move and never die. The ammunition dispatch tests for type 0 and nothing else. What settles the
/// class name and the unreachability is in docs/retail/simulation/weapon-damage-types.md, "Type — a
/// firing-mechanism selector".</para>
/// </summary>
public sealed class Rocket {
	private RocketType _record;
	private short _subtype;
	private Transform3 _frame;
	private short _eulerX;
	private short _eulerY;
	private short _eulerZ;
	private bool _frameStale;
	private short _age;
	private short _animationTimer;
	private short _speed;

	/// <summary>
	/// <c>Rocket_Fire</c> (<c>0040a9c4</c>) — the spawn, which is the whole of what happens between
	/// the allocation and the round's first tick.
	/// </summary>
	/// <param name="projectile">The firing <c>PROJ.DAT</c> record.</param>
	/// <param name="record">Its <c>ROCKETS.DAT</c> record, looked up by the same subtype id.</param>
	/// <param name="muzzle">Where the round starts — the fire prologue's world muzzle point.</param>
	/// <param name="aim">
	/// Which way it points, as the euler triple the prologue extracts from the shot transform. Unlike
	/// <see cref="Projectile"/>'s, it is used verbatim: no <c>ROCKETS.DAT</c> field is a scatter and
	/// the spawn draws no random numbers at all.
	/// </param>
	/// <param name="ownerSpeed">The launching machine's own travel speed — see <see cref="Speed"/>.</param>
	/// <param name="owner">The machine that fired, which the sweep skips.</param>
	internal Rocket(ProjectileData.Projectile projectile, RocketType record,
			Vec3i muzzle, (short X, short Y, short Z) aim, short ownerSpeed, SimObject? owner) {
		Data = projectile;
		_record = record;
		_subtype = projectile.SubtypeId;
		Owner = owner;

		_eulerX = aim.X;
		_eulerY = aim.Y;
		_eulerZ = aim.Z;
		_frameStale = true;

		_frame.X = muzzle.X;
		_frame.Y = muzzle.Y;
		_frame.Z = muzzle.Z;

		// The launch speed is a literal in the spawn, not a record field: every rocket leaves the rail
		// at the same rate regardless of what it is, and the launching machine's own travel speed is
		// added on top exactly as it is for a gun round.
		_speed = (short)(ownerSpeed + LaunchSpeed);
		_animationTimer = record.FrameInterval;
	}

	/// <summary>
	/// <c>Rocket_Fire</c>'s literal <c>0x1f4</c>: the speed a round leaves the launcher at, before
	/// the machine's own travel speed is added and before <see cref="Tick"/> starts accelerating it.
	/// </summary>
	public const short LaunchSpeed = 500;

	/// <summary>The <c>PROJ.DAT</c> record this round came from — its damage, its splash and its subtype id.</summary>
	public ProjectileData.Projectile Data { get; }

	/// <summary>
	/// <c>+0x41</c>, the round's own subtype id, set at launch from the firing record's. It picks the
	/// <see cref="RocketCatalog"/> record the round reads and which guidance branch it takes;
	/// <see cref="GuidanceTick"/> rewrites it to <see cref="ReleasedSubtype"/> when the player lets a
	/// round go.
	/// </summary>
	public short SubtypeId => _subtype;

	/// <summary>
	/// The subtype the round's shape was built from at launch. A later rewrite of
	/// <see cref="SubtypeId"/> does not change what is drawn.
	/// </summary>
	public short ShapeSubtypeId => Data.SubtypeId;

	/// <summary>
	/// Subtype 2 — <c>ARM</c>, the anti-radiation missile. <c>Rocket_HomingSteer</c> singles it out by
	/// literal value: alone among the five it steers only while its target is <i>emitting</i>
	/// (<c>target+0x96</c>, the scanner the pilot toggles, or <c>target+0xa1</c>, its jammer), and it
	/// is exempt from the spoofing wobble every other subtype can suffer.
	/// </summary>
	public const short AntiRadiationSubtype = 2;

	/// <summary>
	/// Subtype 3 — <c>EO</c>, the electro-optical missile, the one the player flies.
	/// <c>Rocket_TickUpdate</c> sends it to <c>Rocket_PlayerSteer</c> (<c>0040a488</c>) instead of
	/// the seeker when its owner is locally piloted. See <see cref="GuidanceTick"/>.
	/// </summary>
	public const short PlayerFlownSubtype = 3;

	/// <summary>
	/// What <c>Rocket_PlayerSteer</c> rewrites a <see cref="PlayerFlownSubtype"/> round's subtype to
	/// once the trigger is released.
	/// </summary>
	public const short ReleasedSubtype = 0;

	/// <summary>The machine that fired. The sweep skips it, so nothing shoots itself.</summary>
	public SimObject? Owner { get; }

	/// <summary>
	/// <c>+0x52</c>. How far the round travels per 125 ms, as
	/// <see cref="SimMath.IntegrateRateOverTick"/> reads a rate. Unlike a gun round's this is not
	/// fixed at launch: it climbs each tick toward the <c>PROJ.DAT</c> record's own <c>Speed</c>.
	/// </summary>
	public short Speed => _speed;

	/// <summary>
	/// The round's frame: where it is, and which way it is going. The translation is the position —
	/// the original keeps no separate one.
	///
	/// <para>Reading it settles the rotation if the angles have moved since the last tick, for the
	/// same reason <see cref="Projectile.Frame"/> does: without it a round would be drawn unrotated
	/// for the frames between the tick that spawned it and the tick that first moves it.</para>
	/// </summary>
	public Transform3 Frame {
		get {
			RebuildFrame();
			return _frame;
		}
	}

	/// <summary>Where the round is, in world units.</summary>
	public Vec3i Position => new(_frame.X, _frame.Y, _frame.Z);

	/// <summary>
	/// <c>+0x54</c>, and it is a plain tick count — <c>Rocket_TickUpdate</c>'s <c>+ 1</c> against the
	/// record's <see cref="RocketType.Lifetime"/> with no scaling in between. Retail's 80
	/// is 3.2 s at the simulation's rate.
	///
	/// <para>That makes a rocket the one shot in the simulation whose <i>range</i> is frame-rate
	/// dependent in the original: it lives a fixed number of frames while each frame's step scales
	/// with the timestep, so a slower machine threw its rockets further. The engine's fixed timestep
	/// pins it to what the original produces at its own 40 ms cap.</para>
	/// </summary>
	public short Age => _age;

	/// <summary>
	/// What the round struck, if it struck anything — set on the tick that ends its life. Not part of
	/// the original, which simply frees the object.
	/// </summary>
	public SimObject? HitObject { get; private set; }

	/// <summary>Whether the round ended by burning out rather than by hitting something.</summary>
	public bool Expired { get; private set; }

	/// <summary>
	/// The shape's cell-animation frame counter, which the renderer wraps against the drawn shape's
	/// own frame count — the same arrangement <see cref="Projectile.AnimationFrame"/> has, and for the
	/// same reason: the simulation stays clear of needing to know what the shape looks like.
	/// </summary>
	public int AnimationFrame { get; private set; }

	/// <summary>
	/// <c>Rocket_TickUpdate</c> (<c>0040a538</c>), vtable <c>+0x14</c> — one step of flight and the
	/// hit test that goes with it.
	///
	/// <list type="number">
	/// <item><b>Animation</b>, when the record asks for it.</item>
	/// <item><b>Age.</b> One per tick, against the record's lifetime. A round that outlives it is
	/// dropped where it is, with no impact of any kind — a rocket burns out, it does not detonate on
	/// a timer.</item>
	/// <item><b>Acceleration</b>, see below.</item>
	/// <item><b>Guidance</b> — the seeker, or the player's own stick for
	/// <see cref="PlayerFlownSubtype"/> fired by the locally-simulated machine.</item>
	/// <item><b>The step</b>, <c>IntegrateRateOverTick(speed)</c>, taken along the frame's Y axis.</item>
	/// <item><b>The hit test is a raycast over that step alone</b>, exactly as a gun round's is, with
	/// the record's own slack in place of the beam's literal 200.</item>
	/// <item><b>The proximity warning</b>, last, on every tick including the one that ends the round
	/// — see <see cref="InboundWarningTick"/>.</item>
	/// </list>
	///
	/// <para><b>The acceleration is damped, not linear.</b> The original adds the record's rate to
	/// the speed and then averages the result with the speed it started the tick at —
	/// <c>speed = (speed + rate·dt + speed) / 2</c> — so the round takes half the step it would
	/// otherwise, and the whole climb is capped at the <c>PROJ.DAT</c> record's <c>Speed</c>. Kept
	/// literally: it is what the burn curve is.</para>
	///
	/// <para><b>When the round ends</b>, by burning out or by striking something, a round the player
	/// is still flying releases the trigger and latches the first button row
	/// (<see cref="SimWorld.EndFlownRound"/>), and <see cref="SimWorld.PlayerMissile"/> ending short of
	/// its lifetime raises <see cref="SimWorld.PlayerMissileStruck"/> for the missile camera.</para>
	/// </summary>
	/// <returns>Whether the round is finished and should be freed.</returns>
	internal bool Tick(SimWorld world) {
		bool finished = FlightTick(world);

		if (finished) {
			if (SubtypeId == PlayerFlownSubtype && Owner is { LocallyPiloted: true }) {
				world.EndFlownRound();
			}

			if (ReferenceEquals(this, world.PlayerMissile) && _age < _record.Lifetime) {
				world.PlayerMissileStruck = true;
			}
		}

		InboundWarningTick(world);
		return finished;
	}

	/// <summary><see cref="Tick"/> up to and including the hit test.</summary>
	private bool FlightTick(SimWorld world) {
		AnimationTick();

		_age = (short)(_age + 1);
		if (_record.Lifetime < _age) {
			Expired = true;
			return true;
		}

		AccelerationTick();
		GuidanceTick(world);

		short step = (short)SimMath.IntegrateRateOverTick(_speed);
		RebuildFrame();
		var advanced = _frame.TransformPoint(0, step, 0);

		// Power is zero: a launcher spends a round out of a rack, never a capacitor charge, so the
		// record's two damage figures apply at face value. The clearance is the ROCKETS.DAT record's
		// own — 200 for the four ordinary rockets, 300 for the BMSL round, which is what makes BMSL the
		// more forgiving hit.
		var shot = new WeaponShot(_frame, step, Data, 0, Owner, _record.ClipRadius) {
			WeaponClass = Data.SubtypeId
		};
		if (world.Raycast(shot) != 0) {
			HitObject = shot.HitObject;
			world.RecordProjectileHit(shot);
			return true;
		}

		_frame.X = advanced.X;
		_frame.Y = advanced.Y;
		_frame.Z = advanced.Z;
		return false;
	}

	/// <summary>
	/// How near the camera a round has to come before it warns the player — <c>0x9c40</c>, 40,000
	/// world units, about 240 m.
	/// </summary>
	public const int InboundWarningRange = 0x9c40;

	/// <summary><c>round+0x06</c> — the latch that holds the warning to once per approach.</summary>
	private bool _warned;

	/// <summary>
	/// <c>Rocket_TickUpdate</c>'s missile-inbound warning. A round whose owner is not locally piloted
	/// plays <see cref="SoundId.MissileInbound"/> when it is inside <see cref="InboundWarningRange"/>
	/// of the camera with the latch clear, and sets the latch; outside that range the latch clears, so
	/// a round that leaves and comes back warns again. The player's own rounds never warn. The
	/// warning is a cockpit tone, played through <c>Sound_Play</c>, not a positional sound. See
	/// docs/retail/simulation/rockets.md ("Flight").
	/// </summary>
	private void InboundWarningTick(SimWorld world) {
		if (Owner is { LocallyPiloted: true }) {
			return;
		}

		if (Position.ApproxDistanceTo(world.ListenerPosition) >= InboundWarningRange) {
			_warned = false;
			return;
		}

		if (_warned) {
			return;
		}

		world.Sounds?.Play(SoundId.MissileInbound);
		_warned = true;
	}

	/// <summary>
	/// The burn: <c>speed += IntegrateRateOverTick(record.Acceleration)</c>, averaged with the speed
	/// the tick opened at, then capped at the <c>PROJ.DAT</c> record's <c>Speed</c>.
	///
	/// <para>Every retail record carries the same rate, 250, which the timestep turns into 79 and the
	/// damping halves to 39 per tick. Against a ceiling of 6000 and a life of 80 ticks the cap is a
	/// ceiling the round never touches — it is still burning when it burns out, and what the record's
	/// <c>Speed</c> really sets on retail data is nothing at all.</para>
	/// </summary>
	private void AccelerationTick() {
		short opening = _speed;
		_speed = (short)(_speed + SimMath.IntegrateRateOverTick(_record.Acceleration));
		_speed = (short)((_speed + opening) >> 1);

		if (Data.Speed < _speed) {
			_speed = Data.Speed;
		}
	}

	/// <summary>
	/// <c>Rocket_TickUpdate</c>'s guidance branch: <c>Rocket_PlayerSteer</c> (<c>0040a488</c>) for a
	/// <see cref="PlayerFlownSubtype"/> round whose owner is locally piloted, <see cref="HomingTick"/>
	/// for everything else.
	///
	/// <para>The player flies the round only while the fire trigger is held, turning it by
	/// <see cref="PlayerSteerRate"/> times each axis of <see cref="SimWorld.MissileSteer"/> per
	/// 125 ms, with no rate limit and no deadband: the steering axis turns the heading (right is a
	/// falling heading) and the throttle axis the pitch. Once the trigger is released the round drops
	/// its target and becomes <see cref="ReleasedSubtype"/>, reading that subtype's
	/// <c>ROCKETS.DAT</c> record from then on and seeking with nothing to seek, so it flies straight
	/// on. Either way it raises <see cref="SimWorld.MissileFlown"/>. See docs/retail/simulation/rockets.md
	/// ("<c>Rocket_PlayerSteer</c>").</para>
	/// </summary>
	private void GuidanceTick(SimWorld world) {
		if (SubtypeId == PlayerFlownSubtype && Owner is { LocallyPiloted: true }) {
			PlayerSteerTick(world);
			world.MissileFlown = true;
			return;
		}

		HomingTick();
	}

	/// <summary>
	/// <c>Rocket_PlayerSteer</c>'s per-tick turn for full deflection of an axis, before
	/// <see cref="SimMath.Q8Multiply"/> scales it by the axis: <c>0x500</c> per 125 ms, the seeker's
	/// <see cref="HomingTurnRate"/> again.
	/// </summary>
	public const short PlayerSteerRate = 0x500;

	/// <summary><c>Rocket_PlayerSteer</c> (<c>0040a488</c>).</summary>
	private void PlayerSteerTick(SimWorld world) {
		var input = world.MissileSteer;

		if (!input.Trigger) {
			Target = null;
			_subtype = ReleasedSubtype;
			_record = world.Rockets?.Record(ReleasedSubtype) ?? _record;
			return;
		}

		int yaw = SimMath.IntegrateRateOverTick((short)SimMath.Q8Multiply(PlayerSteerRate, input.Steer));
		int pitch = SimMath.IntegrateRateOverTick((short)SimMath.Q8Multiply(PlayerSteerRate, input.Pitch));
		_eulerX = (short)(_eulerX + pitch);
		_eulerZ = (short)(_eulerZ - yaw);
		_frameStale = true;

		// It clears the trigger byte and both axes it read. The axes are rebuilt before anything else
		// would act on them; the trigger is not, so the machine's own fire path sees it released.
		world.PlayerTriggerCleared = true;
	}

	/// <summary>
	/// The round's euler triple, <c>+0x0c</c>/<c>+0x0e</c>/<c>+0x10</c>: pitch, roll and heading.
	/// The missile camera takes all three.
	/// </summary>
	public (short X, short Y, short Z) Euler => (_eulerX, _eulerY, _eulerZ);

	/// <summary>
	/// <c>Rocket_HomingSteer</c> (<c>0040a254</c>) — the seeker, which is a steer of the round's euler
	/// angles rather than of a velocity, exactly as the plasma round's is, but at a component of the
	/// target and behind gates the plasma round has none of. See docs/retail/simulation/rockets.md,
	/// "Guidance".
	///
	/// <para>A round steers only when <c>Rocket_Fire</c> attached a target, which it does when this
	/// class of launcher had lock — see <see cref="SimWorld.FireRocket"/>. Fired without lock it
	/// flies where it was pointed, exactly as the original does.</para>
	/// </summary>
	private void HomingTick() {
		if (Target == null) {
			return;
		}

		// The selection gate. A round the player launched steers only while its target is still the
		// player's selected target; an AI's rounds are not asked.
		if (Owner is { LocallyPiloted: true } && !ReferenceEquals(Owner.Target, Target)) {
			return;
		}

		// The emission gate. An anti-radiation round steers only while its target is emitting — its
		// scanner or its jammer — and coasts on its current heading the moment both go quiet.
		if (SubtypeId == AntiRadiationSubtype
				&& !Target.ScannerActive && !Target.JammerActive) {
			return;
		}

		var lead = LockComponent >= 0 ? Target.ComponentWorldPosition(LockComponent) : Target.AimPoint;
		var (bearingX, _, bearingZ) = SimTrig.EulerToward(lead, Position);
		short pitchError = (short)(bearingX - _eulerX);
		short yawError = (short)(bearingZ - _eulerZ);

		if (SubtypeId == PlayerFlownSubtype) {
			// An electro-optical round in flight suppresses its launcher's next AI weapon selection,
			// once per tick it steers — the machine's equivalent of a pilot flying it, and the only
			// writer of mech+0xb5. See docs/retail/simulation/ai-weapons.md.
			if (Owner is MechObject launcher) {
				launcher.WeaponSelectionSuppressed = true;
			}
		} else if (SubtypeId != AntiRadiationSubtype && Owner is MechObject { EcmSpoofed: true }) {
			// The spoofing wobble, on the launcher's own ECM spoof flag (mech+0x9c). The only writer
			// of that flag found is Mech_PerTickSystemsUpdate (0041aa5c), a HERC's, so a flyer's or a
			// structure's rounds never wobble here.
			yawError = SpoofedAimError(yawError);
			pitchError = SpoofedAimError(pitchError);
		}

		short pitchRate = 0;
		short yawRate = 0;
		SimMath.RateLimitedMoveToward(ref pitchRate, pitchError, HomingTurnRate);
		SimMath.RateLimitedMoveToward(ref yawRate, yawError, HomingTurnRate);

		_eulerX = (short)(_eulerX + SimMath.IntegrateRateOverTick(pitchRate));
		_eulerZ = (short)(_eulerZ + SimMath.IntegrateRateOverTick(yawRate));
		_frameStale = true;
	}

	/// <summary>
	/// The cap <c>Rocket_HomingSteer</c> puts on how fast a seeking round may turn — <c>0x500</c> per
	/// 125 ms, twice what the plasma round is allowed.
	/// </summary>
	public const short HomingTurnRate = 0x500;

	/// <summary>
	/// How far either side of dead ahead the spoofing wobble reaches, and how far it pushes —
	/// <c>Rocket_HomingSteer</c>'s <c>0xc00</c>, about 17°.
	/// </summary>
	public const short SpoofWobble = 0xc00;

	/// <summary>
	/// The spoofing wobble on one aim error: an error in <c>[-0xc00, 0xbff]</c> is moved
	/// <see cref="SpoofWobble"/> toward the opposite sign, so a round nearly on its target steers away
	/// from it. The range test is the original's unsigned <c>(ushort)(error + 0xc00) &lt; 0x1800</c>.
	/// </summary>
	private static short SpoofedAimError(short error) {
		if ((ushort)(error + SpoofWobble) >= 2 * SpoofWobble) {
			return error;
		}

		return (short)(error >= 0 ? error - SpoofWobble : error + SpoofWobble);
	}

	/// <summary>
	/// <c>+0x56</c>, what a seeking round is chasing. <c>Rocket_Fire</c> fills it from the launching
	/// machine's selected target, but only when the machine's vtable <c>+0x6c</c> says this subtype
	/// has lock — that reads the per-subtype lock flags at <c>manager+0x0a</c>, written each tick by
	/// <see cref="MechObject.MissileLockTick"/>. See <see cref="SimWorld.FireRocket"/>.
	/// </summary>
	public SimObject? Target { get; private set; }

	/// <summary>
	/// <c>+0x5a</c>, the component of <see cref="Target"/> the round steers at, or −1 to steer at
	/// the target's <see cref="SimObject.AimPoint"/>. Chosen once, at launch, by the target's
	/// <see cref="SimObject.ComponentNearestAim"/>, and never re-chosen: a round whose component is
	/// shot off goes on steering at where that component stands.
	/// </summary>
	public short LockComponent { get; private set; } = -1;

	/// <summary>
	/// <c>Rocket_Fire</c>'s lock: attaches <paramref name="target"/> and, when there is one, asks it
	/// (vtable <c>+0x54</c>) for the component nearest the round's launch attitude from the muzzle.
	/// </summary>
	internal void Lock(SimObject? target) {
		Target = target;
		if (target != null) {
			LockComponent = target.ComponentNearestAim(Position, Euler);
		}
	}

	/// <summary>
	/// <c>Rocket_TickUpdate</c>'s opening step, and the same countdown-and-reload
	/// <see cref="Projectile"/> runs — with one difference: a rocket's record names <i>which</i> of
	/// the shape's sequences the interval steps (<c>ROCKETS.DAT +0x0a</c>), where a bullet always
	/// steps the first. Retail names sequence zero on all five records, and every
	/// <c>TSCellAnimPart</c> in both <c>ROCKETS.DTS</c> roots carries <c>AnimSequence == 0</c>, so the
	/// record really does drive them.
	///
	/// <para><b>What animates is the exhaust flame.</b> Unlike the EMP round's, a rocket's flipbook is
	/// geometry rather than billboards: two alternate cones of flat polys at the tail, in the
	/// palette's red and yellow-white range, beside a static grey body. See
	/// <see cref="Scene.SceneModelLibrary.Rocket"/>.</para>
	///
	/// <para>Zero interval means a static shape, which is the BMSL round alone — its shape has the
	/// two cells but nothing ever steps them, so its flame is frozen. The four ordinary rounds run at
	/// 256, the same figure the EMP rounds use, which works out to a cell every four ticks.</para>
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
	/// The dirty-flag rebuild at <c>+0x32</c>, which the tick performs twice and the draw a third
	/// time. Identical to <see cref="Projectile"/>'s: the two classes share the base object's frame.
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
}

/// <summary>
/// What <c>Rocket_PlayerSteer</c> reads out of the player input block (<c>0x4d234a</c>): the two values
/// its camera-axis pointers at <c>+0x22</c> and <c>+0x26</c> address, and the trigger byte at
/// <c>+0x0d</c>. While the round has the controls the pointers address the steering and throttle
/// axes; otherwise they follow the JOYSTICK row — see docs/retail/formats/joystick-input.md.
/// </summary>
/// <param name="Steer">The <c>+0x22</c> axis, which turns the heading.</param>
/// <param name="Pitch">The <c>+0x26</c> axis, which pitches the nose.</param>
/// <param name="Trigger">The fire trigger, held.</param>
public readonly record struct MissileSteerInput(short Steer, short Pitch, bool Trigger);
