using HercWorks.Core.Data.File.Dat.Sim;
using Herculan.Engine.Numerics;

namespace Herculan.Engine.Sim;

// The RAZOR's flight path: everything a chassis whose type record sets MechTypeRecord.IsFlyer does
// instead of walking. Ported from Razor_ApplyFlightInput (0041bb9c, the input hand-off),
// FlightModel_Step (00466a54, the flight model proper) and Razor_MovementTick (004198f4, the
// per-tick move). See docs/retail/simulation/razor-flight.md.
public sealed partial class MechObject : IFlightBody {
	/// <summary>
	/// The airspeed a flyer powers up at — <c>Mech_Constructor</c>'s literal into <c>mech+0x2bd</c>.
	/// An aircraft cannot be handed to the pilot at rest, and this is well above the RAZOR's own
	/// idle airspeed of 250.
	/// </summary>
	private const int InitialAirSpeed = 1000;

	/// <summary>
	/// This chassis' flight parameters, or null when it does not fly. Non-null is what puts the
	/// machine on the flight path in <see cref="Tick"/>. A RAZOR built without its <c>.FM</c> is left
	/// on the walker paths, which is wrong but is at least not a crash.
	/// </summary>
	public FlightModelRecord? Flight { get; }

	/// <summary>
	/// Velocity in the airframe's own frame, world units per unit time — <c>mech+0x2b9</c>. X is
	/// sideslip, <b>Y is airspeed</b>, Z is the vertical component. The flight model works entirely
	/// in this frame and derives <see cref="FlightWorldVelocity"/> from it; only the move itself uses
	/// the world one.
	///
	/// <para>A flyer having a velocity vector at all is what most sets it apart from a HERC, which
	/// has only a speed scalar the walk animation consumes — see the type summary.</para>
	/// </summary>
	public Vec3i FlightVelocity {
		get => _flight.BodyVelocity;
		private set => _flight.BodyVelocity = value;
	}

	/// <summary>The same velocity in world space — <c>mech+0x2c5</c>, and what the move integrates.</summary>
	public Vec3i FlightWorldVelocity {
		get => _flight.WorldVelocity;
		private set => _flight.WorldVelocity = value;
	}

	/// <summary>
	/// Airspeed — <c>mech+0x2bd</c>, and what <c>Mech_GetSpeed</c> (<c>00415498</c>) returns for a
	/// flyer where it returns a scaled <see cref="Speed"/> for a walker. <see cref="Speed"/> itself
	/// is never written on this path.
	/// </summary>
	public int AirSpeed => FlightVelocity.Y;

	/// <summary>Pitch rate, <c>mech+0x2d1</c>, in binary angle per unit time.</summary>
	public short PitchRate { get => _flight.PitchRate; private set => _flight.PitchRate = value; }

	/// <summary>Roll rate, <c>mech+0x2d3</c>.</summary>
	public short RollRate { get => _flight.RollRate; private set => _flight.RollRate = value; }

	/// <summary>
	/// Yaw rate, <c>mech+0x2d5</c> — what the rudder builds. Distinct from
	/// <see cref="BankTurnRate"/>, which is not a rate the airframe carries.
	/// </summary>
	public short YawRate { get => _flight.YawRate; private set => _flight.YawRate = value; }

	/// <summary>
	/// <c>mech+0x2fd</c> — the heading rate the current bank angle is producing. <b>This is the
	/// aircraft's turning.</b> The rudder yaws the airframe about its own axis, but what swings the
	/// nose round the sky is the bank: roll the wings and the heading follows, at a rate the flight
	/// model reads straight off the bank angle. A RAZOR pilot turns by rolling, not by steering.
	/// </summary>
	public int BankTurnRate => _flight.BankTurnRate;

	/// <summary>
	/// <c>mech+0x2d7</c> — the flyer's own throttle setting, a second copy of <see cref="Throttle"/>
	/// rather than the same field. The flight model owns this one, and the control tick pushes it
	/// onto <see cref="Throttle"/> whenever the pilot moved the axis, so the cockpit gauge follows
	/// the flight model rather than driving it.
	/// </summary>
	public short FlightThrottle {
		get => _flight.Throttle;
		private set => _flight.Throttle = value;
	}

	/// <summary>
	/// <c>mech+0x2b9</c> — the whole 0x4e-byte flight block, and the state
	/// <see cref="FlightPhysics.Step"/> works on. The properties above are views onto it.
	/// </summary>
	private FlightBlock _flight;

	/// <inheritdoc />
	int IFlightBody.PositionZ => Position.Z;

	/// <inheritdoc />
	Transform3 IFlightBody.FlightFrame => Rotation();

	/// <inheritdoc />
	void IFlightBody.FlightAttitudeChanged() => _rotationValid = false;

	/// <inheritdoc />
	bool IFlightBody.AirframeIntact(int component) => AirframeIntact(component);

	/// <summary>
	/// One tick of the flight path, replacing the walker's throttle law, turret tick and move
	/// together. <c>Sim_PollPlayerInput</c> (<c>00460764</c>) branches on the type record's flyer
	/// flag and calls <c>Razor_ApplyFlightInput</c> (<c>0041bb9c</c>) in place of
	/// <c>Mech_ApplyThrottleInput</c> and the two turret ticks, while the object list dispatches
	/// <c>Razor_MovementTick</c> (<c>004198f4</c>) in place of
	/// <c>Mech_MovementTick</c> — a whole distinct behaviour class, picked in
	/// <c>Mech_Constructor</c> (<c>00415bb0</c>) by (is this the player, is this a flyer).
	///
	/// <para><b>The turret does not move.</b> Neither turret tick is on this path at all, so a
	/// RAZOR's guns point where its nose points and aiming means flying.</para>
	///
	/// <para><b>Nor is there any of the walker's collision handling.</b> No swept body test, no
	/// terrain clamp, no back-off — see <see cref="FlyerMovementTick"/> for what stands in.</para>
	/// </summary>
	private void FlightTick(SimWorld world, FlightModelRecord flight) {
		FlightControlTick(world, flight);
		FlyerMovementTick(world, flight);
	}

	/// <summary>
	/// <c>Razor_ApplyFlightInput</c> (<c>0041bb9c</c>) — the flyer's input hand-off. It gathers this
	/// tick's four stick axes, the
	/// ground height under the aircraft and the four wing-damage flags, runs the flight model, and
	/// hands the throttle it settled on back to the cockpit gauge.
	///
	/// <para><b>The axes are remapped.</b> The device layer hands the same four axes to both paths,
	/// but a flyer reads them as an aircraft's controls rather than a walker's:</para>
	/// <list type="bullet">
	/// <item><see cref="MechControls.Turn"/> — stick X — is the <b>aileron</b>.</item>
	/// <item><see cref="MechControls.Throttle"/> — stick Y — is the <b>elevator</b>. On a walker this
	/// axis is the throttle; on an aircraft the primary stick axes have to be pitch and roll, so the
	/// throttle moves elsewhere.</item>
	/// <item><see cref="MechControls.TorsoTwist"/> is the <b>rudder</b>.</item>
	/// <item><see cref="MechControls.TorsoPitch"/> is the <b>throttle</b> — the axis a walker pitches
	/// its turret with, which a flyer has no use for.</item>
	/// </list>
	/// </summary>
	private void FlightControlTick(SimWorld world, FlightModelRecord flight) {
		var controls = Controls;

		FlightPhysics.Step(this, ref _flight, flight,
			aileron: controls.Turn,
			elevator: controls.Throttle,
			rudder: controls.TorsoTwist,
			throttleAxis: controls.TorsoPitch,
			groundHeight: world.GroundHeightAt(Position),
			analogueThrottle: controls.FlightThrottleLever);

		// Only a tick the pilot actually moved the throttle axis on pushes the setting onto the
		// machine's throttle field, so a gauge being dragged is not immediately overwritten. It is
		// the same exchange the walker's gauge makes, the other way round — see
		// ExchangeCockpitThrottle.
		if (controls.TorsoPitch != 0) {
			Throttle = FlightThrottle;
			ThrottleDirty = true;
		}
	}


	/// <summary>
	/// <c>Razor_MovementTick</c> (<c>004198f4</c>) — the flyer's move, in place of
	/// <c>Mech_MovementTick</c>. It integrates
	/// the velocity the flight model settled on, then runs six contact probes over the airframe.
	///
	/// <para><b>The probes are the collision model.</b> There is no swept body test and no terrain
	/// clamp on the airframe as a whole. Six points — the two wings, the two nacelles, the cockpit
	/// and the fuselage — are each checked against the ground beneath them and, bar the fuselage,
	/// swept forward as a ray one tick's travel long. A contact damages that component, kicks the
	/// airframe away from whatever it touched, and scales the damage by how fast it was going. So a
	/// RAZOR that clips a ridge with a wingtip is rolled off it and loses the wing rather than
	/// stopping dead.</para>
	///
	/// <para><b>Two of them end the flight.</b> A cockpit or fuselage contact that destroys its component
	/// latches <see cref="Immobilised"/>, and with that set the aircraft stops integrating position
	/// altogether — it is down, wherever it fell.</para>
	///
	/// <para><b>The two that end it also shed wreckage</b> — <see cref="CrashDebrisGroup"/> at the
	/// contact point, and only on the contact that destroys the component. The four probes that
	/// cannot end the flight throw nothing however hard they hit.</para>
	///
	/// <para><b>The probes share one stale transform, as in the original.</b> One copy of the
	/// transform is taken before the move and never refreshed after a contact kick; the five probe
	/// points are computed from it up front, and each ray probe writes its point into the copy's
	/// translation and leaves it there. So every probe but the fuselage tests the pre-move position,
	/// and the left wing, cockpit and look-ahead ground tests sample displaced points, which a contact
	/// then reports. A retail bug, reproduced — see docs/retail/simulation/razor-flight.md ("The probes share
	/// one transform") and KNOWN_ISSUES.md.</para>
	///
	/// <para><b>It closes with the gun convergence</b>, every tick and whatever state the airframe is
	/// in: the guns toe in on the range to the selected target (<see cref="GunConvergenceRange"/>) or
	/// square up with none. A walker reaches the convergence from its turret pitch tick, which a flyer
	/// does not run. See docs/retail/simulation/razor-flight.md ("Gun convergence").</para>
	/// </summary>
	private void FlyerMovementTick(SimWorld world, FlightModelRecord flight) {
		// The original's local copy of the airframe transform (00419a03-00419a97), taken before the
		// move. Every probe works through it, and the five probe points are computed from it here,
		// before anything has moved or been kicked.
		var shared = Rotation();
		var rightWing = shared.TransformPoint(ProbeRightWing.X, ProbeRightWing.Y, ProbeRightWing.Z);
		var leftWing = shared.TransformPoint(ProbeLeftWing.X, ProbeLeftWing.Y, ProbeLeftWing.Z);
		var rightNacelle = shared.TransformPoint(ProbeRightNacelle.X, ProbeRightNacelle.Y, ProbeRightNacelle.Z);
		var leftNacelle = shared.TransformPoint(ProbeLeftNacelle.X, ProbeLeftNacelle.Y, ProbeLeftNacelle.Z);
		var cockpit = shared.TransformPoint(ProbeCockpit.X, ProbeCockpit.Y, ProbeCockpit.Z);

		if (!Immobilised) {
			var velocity = FlightWorldVelocity;
			Position = new Vec3i(
				Position.X + SimMath.IntegrateRateOverTick((short)velocity.X),
				Position.Y + SimMath.IntegrateRateOverTick((short)velocity.Y),
				Position.Z + SimMath.IntegrateRateOverTick((short)velocity.Z));
		}

		int airSpeed = FlightVelocity.Y;

		// The wing pair. Contact rolls the airframe away from what it touched, hard enough that a
		// wing dragged along a hillside flips the aircraft off it.
		WingProbe(world, ProbeRightWing, rightWing, FlightPhysics.ComponentRightWing, ref shared,
			airSpeed, rollAway: -1);
		WingProbe(world, ProbeLeftWing, leftWing, FlightPhysics.ComponentLeftWing, ref shared,
			airSpeed, rollAway: 1);

		// The nacelles, which have no terrain check at all — only the object ray, at half the wings'
		// clearance. They sit inboard and low, where the ground is already the wings' and the
		// fuselage's business.
		NacelleProbe(world, leftNacelle, FlightPhysics.ComponentLeftNacelle, ref shared, airSpeed,
			rollAway: 1, reported: leftNacelle);
		NacelleProbe(world, rightNacelle, FlightPhysics.ComponentRightNacelle, ref shared, airSpeed,
			rollAway: -1, reported: leftNacelle);

		CockpitProbe(world, cockpit, ref shared, airSpeed);
		GroundAvoidance(world, shared);
		FuselageContact(world, airSpeed);

		UpdateEngineNote(world);

		Weapons.ConvergeOnRange(this, GunConvergenceRange);
	}

	// The six probe points, in the airframe's own frame and in world units. Model forward is +Y and
	// +Z is up, so the wingtips sit six metres out either side, slightly aft and slightly low; the
	// cockpit point is ahead; and the look-ahead point is far ahead and well below.
	private static readonly Vec3i ProbeRightWing = new(1000, -700, -100);
	private static readonly Vec3i ProbeLeftWing = new(-1000, -700, -100);
	private static readonly Vec3i ProbeRightNacelle = new(450, -500, 0);
	private static readonly Vec3i ProbeLeftNacelle = new(-450, -500, 0);
	private static readonly Vec3i ProbeCockpit = new(0, 1000, 0);
	private static readonly Vec3i ProbeLookAhead = new(0, 15000, -1500);

	/// <summary>How much slack a probe ray allows past its own length, per probe point.</summary>
	private const int WingClearance = 300;

	/// <inheritdoc cref="WingClearance"/>
	private const int NacelleClearance = 150;

	/// <inheritdoc cref="WingClearance"/>
	private const int CockpitClearance = 200;

	/// <summary>The shield figure every airframe contact carries. It never varies with speed.</summary>
	private const short ContactDamageShield = 8000;

	/// <summary>What striking an <i>object</i> costs a wing, where a ground contact is speed-scaled.</summary>
	private const short WingObjectDamage = 1000;

	/// <summary>The same for the nacelles and the cockpit, which take a solid hit rather than a scrape.</summary>
	private const short HeavyObjectDamage = 5000;

	/// <summary>Q10 gain from airspeed to the damage a ground contact does, per probe point.</summary>
	private const int WingGroundDamageGain = 500;

	/// <inheritdoc cref="WingGroundDamageGain"/>
	private const int CockpitGroundDamageGain = 1000;

	/// <inheritdoc cref="WingGroundDamageGain"/>
	private const int FuselageGroundDamageGain = 5000;

	/// <summary>Q10 gain from how deep a wing is in the ground to the roll rate it is kicked with.</summary>
	private const int WingGroundRollGain = 4000;

	/// <summary>The flat roll kick an object contact gives, a ground depth not being available.</summary>
	private const short WingObjectRollKick = 4000;

	/// <inheritdoc cref="WingObjectRollKick"/>
	private const short NacelleRollKick = 8000;

	/// <summary>Q10 gain from cockpit-probe depth to the pitch rate it is kicked with.</summary>
	private const int CockpitGroundPitchGain = 2000;

	/// <inheritdoc cref="CockpitGroundPitchGain"/>
	private const short CockpitObjectPitchKick = 2000;

	/// <summary>
	/// Q10 gain for the look-ahead pull-up — a hundredth of the cockpit probe's, because it is a
	/// warning rather than a collision.
	/// </summary>
	private const int LookAheadPitchGain = 0x14;

	/// <summary>
	/// One wing. A point is tested against the ground under it <i>and</i> the wing's own point is
	/// swept forward as a ray, so a wing catches a building as readily as a hillside.
	///
	/// <para>The ground point is <paramref name="offset"/> through <paramref name="shared"/> as it
	/// stands, whose translation an earlier ray probe may have left behind, and it is where a contact
	/// of either kind is reported. The wing's own <paramref name="rayOrigin"/> is then written into
	/// that translation for the ray, and left there.</para>
	/// </summary>
	/// <param name="rollAway">Which way a contact rolls the airframe: -1 for a surface out to starboard.</param>
	private void WingProbe(SimWorld world, Vec3i offset, Vec3i rayOrigin, int component,
			ref Transform3 shared, int airSpeed, int rollAway) {
		if (!AirframeIntact(component)) {
			return;
		}

		var point = shared.TransformPoint(offset.X, offset.Y, offset.Z);
		int ground = world.Terrain.HeightAtWorld(point.X, point.Y);
		bool inGround = point.Z < ground;

		AimProbeRay(ref shared, rayOrigin);
		if (!inGround && !ProbeStruckObject(world, shared, airSpeed, WingClearance)) {
			return;
		}

		short damage;
		if (inGround) {
			RollRate = (short)(RollRate
				+ rollAway * SimMath.Q10Multiply(WingGroundRollGain, ground - point.Z));
			damage = (short)SimMath.Q10Multiply(airSpeed, WingGroundDamageGain);
		} else {
			RollRate = (short)(RollRate + rollAway * WingObjectRollKick);
			damage = WingObjectDamage;
		}

		// The kick lands on the attitude this tick, not through the flight model's integrator — the
		// airframe is pushed, and the rate it was pushed with is left standing for the flight model
		// to damp out over the ticks that follow.
		Roll = (short)(Roll + RollRate);
		_rotationValid = false;

		ApplyContactDamage(world, (short)component, damage,
			inGround ? new Vec3i(point.X, point.Y, ground) : point);
	}

	/// <summary>
	/// One nacelle. No ground test — only the object ray, from <paramref name="rayOrigin"/> written
	/// into <paramref name="shared"/>'s translation and left there — and a flat roll kick twice the
	/// wings'.
	/// </summary>
	/// <param name="reported">
	/// Where a contact is reported: the left nacelle's point for both, as in the original, whose
	/// right-hand branch passes the left point's address (KNOWN_ISSUES.md).
	/// </param>
	private void NacelleProbe(SimWorld world, Vec3i rayOrigin, int component, ref Transform3 shared,
			int airSpeed, int rollAway, Vec3i reported) {
		if (!AirframeIntact(component)) {
			return;
		}

		AimProbeRay(ref shared, rayOrigin);
		if (!ProbeStruckObject(world, shared, airSpeed, NacelleClearance)) {
			return;
		}

		RollRate = (short)(RollRate + rollAway * NacelleRollKick);
		Roll = (short)(Roll + RollRate);
		_rotationValid = false;

		ApplyContactDamage(world, (short)component, HeavyObjectDamage, reported);
	}

	/// <summary>
	/// The cockpit. It pitches the aircraft <i>up</i> out of whatever it hit, and losing the cockpit
	/// section outright ends the flight. Its ground point and its ray work as a wing's do
	/// (<see cref="WingProbe"/>), so the ground point carries the last ray probe's translation.
	/// </summary>
	private void CockpitProbe(SimWorld world, Vec3i rayOrigin, ref Transform3 shared, int airSpeed) {
		if (!AirframeIntact(FlightPhysics.ComponentCockpit)) {
			return;
		}

		var point = shared.TransformPoint(ProbeCockpit.X, ProbeCockpit.Y, ProbeCockpit.Z);
		int ground = world.Terrain.HeightAtWorld(point.X, point.Y);
		bool inGround = point.Z < ground;

		AimProbeRay(ref shared, rayOrigin);
		if (!inGround && !ProbeStruckObject(world, shared, airSpeed, CockpitClearance)) {
			return;
		}

		// A nose already dropping is zeroed first, so the kick is a pull-up from rest rather
		// than a correction applied to a dive steep enough to swallow it.
		if (PitchRate < 0) {
			PitchRate = 0;
		}

		short damage;
		if (inGround) {
			PitchRate =
				(short)(PitchRate + SimMath.Q10Multiply(CockpitGroundPitchGain, ground - point.Z));
			damage = (short)SimMath.Q10Multiply(airSpeed, CockpitGroundDamageGain);
		} else {
			PitchRate = (short)(PitchRate + CockpitObjectPitchKick);
			damage = HeavyObjectDamage;
		}

		Pitch = (short)(Pitch + PitchRate);
		_rotationValid = false;

		var contact = inGround ? new Vec3i(point.X, point.Y, ground) : point;
		ApplyContactDamage(world, FlightPhysics.ComponentCockpit, damage, contact);

		if (!AirframeIntact(FlightPhysics.ComponentCockpit)) {
			world.Effects.SpawnDebris(CrashDebrisGroup, contact, DebrisTable(world));
			Immobilised = true;
		}
	}

	/// <summary>
	/// The terrain look-ahead — a single point 15000 units ahead and 1500 below, through the shared
	/// probe transform, whose translation the cockpit's ray always leaves at the cockpit's point
	/// (<see cref="FlyerMovementTick"/>). It pulls the nose up when the ground rises into it. It is
	/// the closest thing the RAZOR has to a stall recovery, and it is why the aircraft skims a
	/// hillside rather than burying itself in it.
	///
	/// <para><b>It only runs on an intact airframe</b>: both nacelles and the cockpit have to be
	/// alive. A RAZOR that has lost any of the three flies straight into the hill.</para>
	/// </summary>
	private void GroundAvoidance(SimWorld world, in Transform3 frame) {
		if (!AirframeIntact(FlightPhysics.ComponentLeftNacelle) || !AirframeIntact(FlightPhysics.ComponentRightNacelle)
				|| !AirframeIntact(FlightPhysics.ComponentCockpit)) {
			return;
		}

		var point = frame.TransformPoint(ProbeLookAhead.X, ProbeLookAhead.Y, ProbeLookAhead.Z);
		int ground = world.Terrain.HeightAtWorld(point.X, point.Y);
		if (ground <= point.Z) {
			return;
		}

		if (PitchRate < 0) {
			PitchRate = 0;
		}

		PitchRate = (short)(PitchRate + SimMath.Q10Multiply(LookAheadPitchGain, ground - point.Z));
		Pitch = (short)(Pitch + PitchRate);
		_rotationValid = false;
	}

	/// <summary>
	/// The fuselage, and the only probe that moves the aircraft: below the ground, it is put back on
	/// it.
	/// The damage scales with airspeed, so setting a RAZOR down slowly is survivable and arriving at
	/// speed is not.
	/// </summary>
	private void FuselageContact(SimWorld world, int airSpeed) {
		int ground = world.Terrain.HeightAtWorld(Position.X, Position.Y);
		if (Position.Z >= ground) {
			return;
		}

		Position = new Vec3i(Position.X, Position.Y, ground);
		_rotationValid = false;

		ApplyContactDamage(world, FlightPhysics.ComponentFuselage,
			(short)SimMath.Q10Multiply(airSpeed, FuselageGroundDamageGain), Position);

		if (!AirframeIntact(FlightPhysics.ComponentFuselage)) {
			world.Effects.SpawnDebris(CrashDebrisGroup, Position, DebrisTable(world));
			Immobilised = true;
		}
	}

	/// <summary>
	/// Writes a probe's ray origin into the shared probe transform's translation, where the original
	/// leaves it for every probe after (see <see cref="FlyerMovementTick"/>).
	/// </summary>
	private static void AimProbeRay(ref Transform3 shared, Vec3i origin) {
		shared.X = origin.X;
		shared.Y = origin.Y;
		shared.Z = origin.Z;
	}

	/// <summary>
	/// Whether the ray <paramref name="ray"/> describes struck anything: one tick's travel long, from
	/// its translation along its attitude — the shared probe transform as the last
	/// <see cref="AimProbeRay"/> left it.
	/// </summary>
	private bool ProbeStruckObject(SimWorld world, in Transform3 ray, int airSpeed, int clearance) {
		var probe = new WeaponShot(ray, airSpeed, ContactDamageShield, ContactDamageShield,
			AirframeContact, owner: null, excluded: this, clearance: clearance);

		return HitTests.Raycast(world, probe) != 0;
	}

	/// <summary>
	/// Puts one contact through the ordinary damage path. The armour figure varies per contact; the
	/// shield figure never does, and <b>the shot has no attacker</b> — the original leaves the
	/// record's attacker field null, so flying into a hillside is nobody's kill.
	/// </summary>
	private void ApplyContactDamage(SimWorld world, short component, short damage, Vec3i point) {
		var contact = new WeaponShot(Rotation(), 0, damage, ContactDamageShield, AirframeContact,
			owner: null, excluded: this);

		ApplyDirectFireDamage(world, component, contact, point);
	}

	/// <summary>
	/// What a fatal airframe contact throws — the original's literal 3, out of <c>DEF_DEB</c>.
	/// </summary>
	private const short CrashDebrisGroup = 3;

	/// <summary>
	/// The impact-effect table airframe contacts draw from — a hand-built <c>PROJ.DAT</c>-shaped
	/// record in the executable's statics at <c>0049a158</c> rather than a real projectile's, so a
	/// wing clipping a ridge throws up its own effects rather than some weapon's.
	/// </summary>
	private static readonly ProjectileData.Projectile AirframeContact = new() {
		DamageArmor = ContactDamageShield,
		DamageShield = ContactDamageShield,
		SplashFactor = 0,
		ImpactFXShield = new short[] { 11, 11, 11, 11 },
		ImpactFXGround = new short[] { 0, 1, 4, 5 },
		ImpactFXArmor = new short[] { 0, 1, 4, 5 },
	};

	/// <summary>
	/// The engine note — <c>Sound_SetPitch</c> on the looping hum, at a rate that climbs with the
	/// magnitude of the whole velocity vector rather than with airspeed alone, so a RAZOR falling out
	/// of the sky screams as loudly as one running flat out. Only the player's machine has a hum
	/// (<see cref="Audio.GameAudio.PowerUp"/>), and a destroyed one is silenced.
	/// </summary>
	private void UpdateEngineNote(SimWorld world) {
		if (!IsPlayer || world.Sounds is not { } sounds) {
			return;
		}

		if (Destroyed) {
			sounds.Stop(SoundId.EngineLoop);
			return;
		}

		var v = FlightVelocity;
		int rate = SimMath.FastMagnitude3D(v.X, v.Y, v.Z) * EngineNoteSpeedGain
			+ EngineNoteIdlePitch;
		rate = rate > 0xfffe ? 0xffff : rate < 1 ? 0 : rate;

		sounds.SetPitch(SoundId.EngineLoop, rate);
		sounds.MoveTo(SoundId.EngineLoop, Position);
	}

	/// <summary>
	/// The playback rate the hum sits at with the aircraft stationary, 16.16 — a shade under
	/// <see cref="SoundId.EngineLoopPitch"/>, the rate the cockpit power-up drops it to.
	/// </summary>
	private const int EngineNoteIdlePitch = 28000;

	/// <summary>How much each unit of speed raises that rate.</summary>
	private const int EngineNoteSpeedGain = 16;

	/// <summary>
	/// Whether one airframe component is still there. A machine with no <c>.DMG</c> loaded counts as
	/// whole rather than as wrecked, which matters here where <see cref="ComponentAlive"/>'s "no,
	/// because there is no damage model" would jam the controls hard over.
	/// </summary>
	private bool AirframeIntact(int component) => _damage == null || _damage.IsActive(component);

}
