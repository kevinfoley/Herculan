using Herculan.Engine.Anim;
using Herculan.Engine.Numerics;
using Herculan.Engine.Settings;
using Herculan.Es1.Sound;

namespace Herculan.Es1.Sim;

/// <summary>
/// A HERC's turret (docs/es1/simulation/mech.md#the-turret): <c>Mech_TorsoTwistTick</c> (<c>ES1DBSIM 000149ac</c>),
/// <c>Mech_TorsoPitchTick</c> (<c>00014ba0</c>) with its gun convergence, and <c>Mech_CenterTorsoTick</c>
/// (<c>000186f8</c>), on the fields of its <see cref="Es1Mech"/>. They are ES2's ticks
/// (docs/retail/simulation/torso-aim.md#the-tick) without the snap-to-target, each axis' angle selecting a position in its
/// thread's sequence (docs/retail/simulation/torso-aim.md#angle-to-pose). The type record's fields are the chassis file's.
///
/// <para>The multiplies are ES1's rounding ones, the rate-over-tick helpers <see cref="Es1SimClock"/>'s, and the seek
/// <see cref="Rounding"/>'s (docs/herculan/plan-es1-first-mission.md#decisions, decisions 1 and 31).</para>
/// </summary>
public sealed class Es1Turret {
	/// <summary>The Q10 gain <c>Mech_CenterTorsoTick</c> takes of each angle.</summary>
	public const short CentringGain = 0xfa;

	/// <summary>Full deflection of a turret axis, either way.</summary>
	public const short AxisFull = 0x100;

	private readonly Es1Mech _mech;

	public Es1Turret(Es1Mech mech) {
		_mech = mech;
	}

	/// <summary>
	/// <c>Mech_TorsoTwistTick</c> (<c>000149ac</c>), (machine, axis): the twist rate <c>+0x22a</c> and twist <c>+0x22e</c>
	/// moved by <paramref name="axis"/> (<see cref="AxisTick"/>) on the record's <c>+0x1e</c> and <c>+0x20</c>, the twist
	/// held to ± the record's <c>+0x22</c>; the thread <c>+0x1c4</c> seeked in sequence <c>+0x1c</c>; then, for the
	/// player's machine, <c>Mech_TorsoTwistServoSound</c> (<c>00014afc</c>).
	/// </summary>
	public void TwistTick(Es1SimClock clock, Es1TurretServos servos, short axis) {
		var mech = _mech;
		var chassis = mech.Type.Chassis;
		(mech.TwistRate, mech.Twist) = AxisTick(clock, mech.TwistRate, mech.Twist, axis, chassis.TorsoTwistMaxRate,
			chassis.TorsoTwistAccel, chassis.TorsoTwistLimit, -chassis.TorsoTwistLimit);
		Seek(mech.TwistThread, chassis.AnimId_TorsoTwist, mech.Twist);
		if (mech.IsPlayer) {
			servos.Twist.Update(axis, mech.Twist);
		}
	}

	/// <summary>
	/// <c>Mech_TorsoPitchTick</c> (<c>00014ba0</c>), (machine, range, axis): <see cref="TwistTick"/>'s steps on the pitch
	/// rate <c>+0x22c</c> and pitch <c>+0x230</c> with the record's <c>+0x26</c> and <c>+0x28</c>, the pitch held to the
	/// record's <c>+0x2c</c> below and <c>+0x2a</c> above, the thread <c>+0x1c8</c> seeked in sequence <c>+0x24</c>; then
	/// <see cref="ConvergeGuns"/> on <paramref name="range"/>; then, for the player's machine,
	/// <c>Mech_TorsoPitchServoSound</c> (<c>00014dc4</c>).
	/// </summary>
	/// <param name="range">The guns' convergence range: the distance to the aim point, or 0 to square them up.</param>
	public void PitchTick(Es1SimClock clock, Es1TurretServos servos, int range, short axis) {
		var mech = _mech;
		var chassis = mech.Type.Chassis;
		(mech.PitchRate, mech.Pitch) = AxisTick(clock, mech.PitchRate, mech.Pitch, axis, chassis.TorsoPitchMaxRate,
			chassis.TorsoPitchAccel, chassis.TorsoPitchMax, chassis.TorsoPitchMin);
		Seek(mech.PitchThread, chassis.AnimId_TorsoPitch, mech.Pitch);

		// Retail's machine always holds a manager; this one has none until Mech_ConfigureLoadout has run.
		if (mech.Mounts is { } mounts) {
			ConvergeGuns(mounts.Mounts, range, chassis.EyeOffsetZ);
		}

		if (mech.IsPlayer) {
			servos.Pitch.Update(axis, mech.Pitch);
		}
	}

	/// <summary>
	/// <c>Mech_CenterTorsoTick</c> (<c>000186f8</c>), (machine, range): ES2's centring
	/// (docs/retail/simulation/torso-aim.md#centring--backspace) without the snap, each axis driven by
	/// −clamp(Q10(<see cref="CentringGain"/>, angle), ±<see cref="AxisFull"/>), both read before either tick runs, the
	/// twist's tick first, <paramref name="range"/> passed to the pitch's (docs/es1/simulation/mech-ai.md#weapons).
	/// </summary>
	public void CenterTorsoTick(Es1SimClock clock, Es1TurretServos servos, int range) {
		var mech = _mech;
		short twistAxis = (short)-CentringDemand(mech.Twist);
		short pitchAxis = (short)-CentringDemand(mech.Pitch);
		TwistTick(clock, servos, twistAxis);
		PitchTick(clock, servos, range, pitchAxis);
	}

	/// <summary>
	/// <c>Cockpit_TargetAnglesFromCameraBone</c> (<c>00018b78</c>), (machine, <paramref name="point"/>)
	/// (docs/es1/simulation/mech-ai.md#weapons): the point taken into the machine's frame and then into the frame of the
	/// eye node the record's <c>+0xc</c> names, each inverted inline, its Z less the record's <c>+0x66</c>;
	/// <c>Math_EulerToward</c> from the node's origin to it, the residual; the twist axis Q8(<i>a</i>, |<i>a</i>|) with
	/// <i>a</i> = −clamp(Q10(<see cref="CentringGain"/>, yaw), ±<see cref="AxisFull"/>), the pitch axis likewise from the
	/// pitch without the negation; then the twist tick, and the pitch tick at the range
	/// <c>Math_FastMagnitude3D</c> of the point in the node's frame. Returns the residual, <c>TargetAngles_Residual</c>
	/// (<c>000af714</c>). The once flag <c>TargetAngles_CalledOnce</c> (<c>000af710</c>) it sets is not kept: no reader of it
	/// is known.
	/// </summary>
	public (short Pitch, short Roll, short Yaw) TrackPoint(Es1SimClock clock, Es1TurretServos servos, Vec3i point) {
		var mech = _mech;
		var chassis = mech.Type.Chassis;
		var inMachine = mech.Frame.Inverted().TransformPoint(point.X, point.Y, point.Z);
		var local = mech.PartTransform(chassis.CameraPartId).Inverted().TransformPoint(inMachine.X, inMachine.Y, inMachine.Z);
		local = new Vec3i(local.X, local.Y, local.Z - chassis.EyeOffsetZ);
		var residual = SimTrig.EulerToward(local, Vec3i.Zero);

		short twistAxis = Squared((short)-CentringDemand(residual.Z));
		short pitchAxis = Squared((short)CentringDemand(residual.X));
		TwistTick(clock, servos, twistAxis);
		PitchTick(clock, servos, SimMath.FastMagnitude3D(local.X, local.Y, local.Z), pitchAxis);
		return residual;

		// Q8(axis, |axis|), the magnitude as retail takes it.
		static short Squared(short axis) => (short)SimMath.Q8MultiplyRounded(axis, (short)Magnitude(axis));
	}

	/// <summary>
	/// The gun convergence of <c>Mech_TorsoPitchTick</c> (docs/es1/simulation/mech.md#the-turret), ES2's
	/// <c>Mech_ConvergeGunsOnRange</c> (docs/retail/simulation/weapon-firing.md#gun-convergence--mech_convergegunsonrange-0041a74c)
	/// with no gates: for each mount, <paramref name="range"/> 0 clears <see cref="Es1WeaponMount.Convergence"/>;
	/// otherwise <c>Math_EulerToward</c> (<c>00063d2c</c>) from the mount's <c>WeaponMount_MuzzleOffset</c> (<c>0000b918</c>)
	/// less <paramref name="eyeLift"/>, the record's <c>+0x66</c>, in Z to (0, range, 0) fills it.
	///
	/// <para><c>00063d2c</c> is ES2's <c>Math_EulerToward</c> (<c>00492884</c>), <see cref="SimTrig.EulerToward"/>, with its
	/// two points in the reverse order (to, from) on the stack: the same <c>Math_Atan2Guarded</c> and
	/// <c>Math_FastMagnitude2D</c> over the same tables (docs/es1/simulation/fixed-point-math.md#the-assembly-library).</para>
	/// </summary>
	public static void ConvergeGuns(IReadOnlyList<Es1WeaponMount?> mounts, int range, short eyeLift) {
		foreach (var mount in mounts) {
			if (mount == null) {
				continue;
			}

			if (range == 0) {
				mount.Convergence = (0, 0, 0);
				continue;
			}

			// WeaponMount_MuzzleOffset returns its sum as int16s, which the tick widens.
			var muzzle = mount.MountPointOffset;
			var from = new Vec3i((short)muzzle.X, (short)muzzle.Y, (short)muzzle.Z - eyeLift);
			mount.Convergence = SimTrig.EulerToward(new Vec3i(0, range, 0), from);
		}
	}

	/// <summary>
	/// One axis' tick (docs/retail/simulation/torso-aim.md#the-tick): the target rate Q8(<paramref name="maxRate"/>,
	/// <paramref name="axis"/>); while |rate| is under |target| the rate moves toward it by <c>Math_RateLimitedMoveToward</c>
	/// at the rate over the tick of <paramref name="accel"/>, otherwise it takes it; the angle adds the rate over the tick
	/// of the rate before and after, summed and shifted right by 1, and is held to <paramref name="lower"/>–<paramref name="upper"/>,
	/// the upper bound tested first.
	/// </summary>
	private static (short Rate, short Angle) AxisTick(Es1SimClock clock, short rate, short angle, short axis, short maxRate,
			short accel, int upper, int lower) {
		short target = (short)SimMath.Q8MultiplyRounded(maxRate, axis);
		short before;
		if (Magnitude(rate) < Magnitude(target)) {
			before = rate;
			SimMath.RateLimitedMoveToward(ref rate, target, clock.IntegrateRateOverTick(accel));
		} else {
			rate = target;
			before = target;
		}

		short next = (short)(angle + clock.IntegrateRateOverTick((short)((before + rate) >> 1)));
		angle = next >= upper ? (short)upper : next <= lower ? (short)lower : next;
		return (rate, angle);
	}

	/// <summary>
	/// <c>AnimThread_SeekToPosition</c> (<c>000620c4</c>) of the thread, when there is one, to the unsigned angle shifted
	/// right by 2, with <see cref="Rounding"/>.
	/// </summary>
	internal static void Seek(AnimationThread? thread, short sequence, short angle) {
		thread?.SeekToPosition(sequence, (short)((ushort)angle >> 2), Rounding);
	}

	/// <summary>
	/// The arithmetic every ES1 seek scales its position with: retail's <see cref="SeekRounding.RoundToNearest"/>,
	/// or with <see cref="TweakSettingDefinitions.SmootherTurretMovement"/> on,
	/// <see cref="SeekRounding.KeepRemainder"/>. Kept, the remainder places the pose at the exact position, so
	/// the tweak is Earthsiege 2's whichever way retail rounds, and the drawn pose is within half an animation tick of
	/// retail's.
	/// </summary>
	internal static SeekRounding Rounding =>
		TweakSettings.Current.GetSettingValue(TweakSettingDefinitions.SmootherTurretMovement)
			? SeekRounding.KeepRemainder : SeekRounding.RoundToNearest;

	/// <summary>Q10(<see cref="CentringGain"/>, <paramref name="angle"/>) held to ±<see cref="AxisFull"/>.</summary>
	private static int CentringDemand(short angle) {
		int demand = SimMath.Q10MultiplyRounded(CentringGain, angle);
		return demand >= AxisFull ? AxisFull : demand <= -AxisFull ? -AxisFull : demand;
	}

	/// <summary>The magnitude of a 16-bit value as retail takes it, −32768 giving <c>0x7fff</c>.</summary>
	private static int Magnitude(short value) => value == short.MinValue ? short.MaxValue : Math.Abs((int)value);
}

/// <summary>
/// The latches and last angles of the two servo-sound helpers, <c>TwistServo_Latch</c> (<c>00091c54</c>) and
/// <c>TwistServo_LastAngle</c> (<c>00091c56</c>), <c>PitchServo_Latch</c> (<c>00091c58</c>) and <c>PitchServo_LastAngle</c>
/// (<c>00091c5a</c>) (docs/es1/simulation/mech.md#globals). Retail's are statics of the image, all four 0 in its file;
/// the simulation owns one of these.
/// </summary>
public sealed class Es1TurretServos {
	/// <param name="sound">The simulator's sound file, read at each call (<see cref="Es1TurretServo(Func{Es1Sound}?)"/>).</param>
	public Es1TurretServos(Func<Es1Sound>? sound = null) {
		Twist = new Es1TurretServo(sound);
		Pitch = new Es1TurretServo(sound);
	}

	/// <summary>The twist's, which <c>Mech_TorsoTwistServoSound</c> (<c>00014afc</c>) runs.</summary>
	public Es1TurretServo Twist { get; }

	/// <summary>The pitch's, which <c>Mech_TorsoPitchServoSound</c> (<c>00014dc4</c>) runs.</summary>
	public Es1TurretServo Pitch { get; }
}

/// <summary>
/// One axis' servo-sound helper, <c>Mech_TorsoTwistServoSound</c> (<c>00014afc</c>) or <c>Mech_TorsoPitchServoSound</c>
/// (<c>00014dc4</c>), (machine, axis): ES2's tests (docs/retail/simulation/torso-aim.md#the-servo-sound-helpers), acted on
/// (docs/es1/simulation/mech.md#the-turret).
/// </summary>
public sealed class Es1TurretServo {
	/// <summary>The sound the helpers play and stop.</summary>
	public const short Sound = 0x21;

	private readonly Func<Es1Sound>? _sound;

	/// <param name="sound">
	/// The simulator's sound file, read at each call; with none, the helper acts as on a sound file whose
	/// <c>Sound_IsPlaying</c> answers 0 and plays nothing.
	/// </param>
	public Es1TurretServo(Func<Es1Sound>? sound = null) => _sound = sound;

	/// <summary>The axis magnitude the helper must exceed to start the sound.</summary>
	public const short Deflection = 0xc0;

	/// <summary>The latch: the sound has been started and not yet stopped.</summary>
	public bool Latched { get; private set; }

	/// <summary>The angle at the last call.</summary>
	public short LastAngle { get; private set; }

	/// <summary>
	/// With the latch clear, |<paramref name="axis"/>| over <see cref="Deflection"/> and <paramref name="angle"/> not the
	/// last angle: when <c>Sound_IsPlaying</c> (<c>0004e05c</c>)(<see cref="Sound"/>) answers 0, <c>Sound_Play</c>
	/// (<c>0004de84</c>) of it and the latch set. Otherwise, with the latch set and the axis 0 or the angle the last angle:
	/// <c>Sound_Stop</c> (<c>0004df70</c>) of it and the latch cleared. The angle is kept as the last either way. The
	/// magnitude is a 16-bit negation, so an axis of −32768 stays negative.
	/// </summary>
	public void Update(short axis, short angle) {
		short magnitude = axis < 0 ? (short)-axis : axis;
		var sound = _sound?.Invoke();
		if (!Latched && magnitude > Deflection && LastAngle != angle) {
			if (sound?.IsPlaying(Sound) != true) {
				sound?.Play(Sound);
				Latched = true;
			}
		} else if (Latched && (magnitude == 0 || LastAngle == angle)) {
			sound?.Stop(Sound);
			Latched = false;
		}

		LastAngle = angle;
	}
}
