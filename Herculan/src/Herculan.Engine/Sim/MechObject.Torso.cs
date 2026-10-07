using Herculan.Engine.Numerics;
using Herculan.Engine.Settings;
using Herculan.Engine.Sim.Anim;

namespace Herculan.Engine.Sim;

/// <summary>
/// The torso — the manual's "turret": the part of a HERC that carries the pilot and the weapons and
/// aims independently of the legs.
///
/// <para>It has no rotation of its own. The type record names a sequence for each axis, each one a
/// single full sweep of one node, and the angle is used as a <i>position</i> within that sequence
/// (<see cref="AnimationThread.SeekToPosition"/>) rather than as an angle anything rotates by. Twist
/// and pitch are therefore the same kind of thing as the walk cycle, and reach the screen the same
/// way — see docs/retail/formats/dts-node-posing.md.</para>
/// </summary>
public sealed partial class MechObject {
	/// <summary>
	/// Twist rate, <c>mech+0x294</c>, in binary angle per second. Built up toward what the stick asks
	/// for, then integrated into <see cref="TorsoTwistAngle"/>.
	/// </summary>
	public short TorsoTwistRate { get; private set; }

	/// <summary>
	/// Twist angle, <c>mech+0x298</c>, a binary angle relative to the machine's own heading. Positive
	/// is the direction <see cref="MechControls.TorsoTwist"/> positive drives it.
	/// </summary>
	public short TorsoTwistAngle { get; private set; }

	/// <summary>Pitch rate, <c>mech+0x296</c>.</summary>
	public short TorsoPitchRate { get; private set; }

	/// <summary>Pitch angle, <c>mech+0x29a</c>. Positive looks up.</summary>
	public short TorsoPitchAngle { get; private set; }

	/// <summary>
	/// <c>Mech_TorsoTwistTick</c> (<c>0041a550</c>) — one tick of the twist axis.
	///
	/// <para>The rate ramps toward <c>Q8(axis, TorsoTwistMaxRate)</c> at the type's own acceleration,
	/// but <b>only while it is growing</b>: the moment the stick asks for less than the torso is
	/// already doing, the rate snaps to it. Releasing the stick therefore stops the torso dead, and
	/// so does reversing it. The angle then integrates the mean of the rate before and after, which
	/// is a trapezoid rule while ramping and a plain step while not, and clamps to ±the type's
	/// limit.</para>
	///
	/// <para><paramref name="snapTarget"/> is only consulted when <paramref name="snapEnable"/> is
	/// set, which normal piloting does not: the input path passes it disabled. It stops the torso
	/// dead on the tick it crosses the target angle, and is how
	/// <see cref="CenterTorsoTick"/> lands exactly on centre instead of oscillating about it.</para>
	/// </summary>
	public void TorsoTwistTick(SimWorld world, short axis, short snapTarget = -1, bool snapEnable = false) {
		short previousAngle = TorsoTwistAngle;
		short rate = TorsoTwistRate;
		short angle = TorsoTwistAngle;

		StepTorsoAxis(ref rate, ref angle, axis, Type.TorsoTwistMaxRate, Type.TorsoTwistAccel,
			(short)-Type.TorsoTwistLimit, Type.TorsoTwistLimit);

		TorsoTwistRate = rate;
		TorsoTwistAngle = angle;

		if (snapEnable && CrossedTarget(previousAngle, TorsoTwistAngle, snapTarget)) {
			TorsoTwistAngle = snapTarget;
			TorsoTwistRate = 0;
		}

		TorsoTwistThread?.SeekToPosition(Type.TorsoTwistSequence, SequencePosition(TorsoTwistAngle),
			world.Tweaks.GetSettingValue(TweakSettingDefinitions.SmootherTurretMovement));
	}

	/// <summary>
	/// <c>Mech_TorsoPitchTick</c> (<c>0041a808</c>) — the same, on the pitch axis. The only
	/// differences are the fields it works on and that its limits are asymmetric: a HERC looks
	/// further up than down.
	///
	/// <para>Its third argument is the range the guns are to converge on — the distance to whatever
	/// the machine is aiming at, and zero for nothing. That is the original's own placement: the
	/// convergence pass is this tick's tail. See
	/// <see cref="WeaponMounts.ConvergeOnRange"/>.</para>
	/// </summary>
	public void TorsoPitchTick(SimWorld world, short axis, int convergeRange = 0, short snapTarget = -1,
			bool snapEnable = false) {
		short previousAngle = TorsoPitchAngle;
		short rate = TorsoPitchRate;
		short angle = TorsoPitchAngle;

		StepTorsoAxis(ref rate, ref angle, axis, Type.TorsoPitchMaxRate, Type.TorsoPitchAccel,
			Type.TorsoPitchMin, Type.TorsoPitchMax);

		TorsoPitchRate = rate;
		TorsoPitchAngle = angle;

		if (snapEnable && CrossedTarget(previousAngle, TorsoPitchAngle, snapTarget)) {
			TorsoPitchAngle = snapTarget;
			TorsoPitchRate = 0;
		}

		TorsoPitchThread?.SeekToPosition(Type.TorsoPitchSequence, SequencePosition(TorsoPitchAngle),
			world.Tweaks.GetSettingValue(TweakSettingDefinitions.SmootherTurretMovement));

		Weapons.ConvergeOnRange(this, convergeRange);
	}

	/// <summary>
	/// <c>Mech_CenterTorsoTick</c> (<c>0041e8d4</c>) — the [Backspace] "Center Turret" command, run every tick until the pilot
	/// takes the torso back. It drives both axes from the angles themselves, so the torso runs home
	/// fast and eases off as it arrives, and enables the snap so it stops exactly on centre.
	///
	/// <para><paramref name="convergeRange"/> is passed straight through to the pitch tick's
	/// convergence pass, so the guns keep toeing in on the selected target while the turret comes
	/// home. The player's input path is the only caller that has a range to give; every AI caller
	/// passes zero.</para>
	/// </summary>
	public void CenterTorsoTick(SimWorld world, int convergeRange = 0) {
		TorsoTwistTick(world, (short)-ClampAxis(SimMath.Q10Multiply(CenterGain, TorsoTwistAngle)),
			snapTarget: 0, snapEnable: true);
		TorsoPitchTick(world, (short)-ClampAxis(SimMath.Q10Multiply(CenterGain, TorsoPitchAngle)),
			convergeRange: convergeRange, snapTarget: 0, snapEnable: true);
	}

	/// <summary>
	/// <c>Cockpit_TargetAnglesFromCameraBone</c> (<c>0041ef14</c>) — bring <paramref name="point"/>
	/// into the pilot's own frame, drive both turret axes at it, and hand back what is left of the
	/// error. It is the whole of "point the turret at that", and the AI's fire path reaches it exactly
	/// as the player's automatic tracking does. The derivation is in docs/retail/simulation/torso-aim.md,
		/// "Aiming at a point".
	///
	/// <list type="bullet">
	/// <item>Each axis' demand is the residual angle scaled by <see cref="CenterGain"/>, clamped to
	/// the stick's own <c>±0x100</c> and then <b>squared</b> (<c>Q8(|v|, v)</c>), so the turret runs
	/// hard while it is far off and eases as it arrives.</item>
	/// <item>Past <see cref="TrackFarRange"/> a small residual is damped and a very small one is
	/// discarded outright — a dead band that stops the turret hunting on a distant target.</item>
	/// <item>The snap target is the angle the turret <i>would</i> have if it were already on the
	/// point, so the axis stops exactly there rather than swinging through it.</item>
	/// </list>
	/// </summary>
	/// <returns>The residual aim error: yaw and pitch, in binary angle.</returns>
	public (short Yaw, short Pitch) TrackWorldPoint(SimWorld world, Vec3i point) {
		var local = CameraNodeTransform.Inverted().TransformPoint(point.X, point.Y, point.Z);
		local = new Vec3i(local.X, local.Y, local.Z - Type.EyeOffsetZ);

		var (pitchError, _, yawError) = SimTrig.EulerToward(local, default);

		int yawDemand = SimMath.Q10Multiply(CenterGain, yawError);
		int pitchDemand = SimMath.Q10Multiply(CenterGain, pitchError);

		if (local.Y > TrackFarRange) {
			yawDemand = DampedFarDemand(yawDemand, SaturatingAbs(yawError), TrackYawDeadband);
			pitchDemand = DampedFarDemand(pitchDemand, SaturatingAbs(pitchError), TrackPitchDeadband);
		}

		short yawAxis = (short)-ClampAxis(yawDemand);
		short pitchAxis = (short)ClampAxis(pitchDemand);

		TorsoTwistTick(world, (short)SimMath.Q8Multiply(SaturatingAbs(yawAxis), yawAxis),
			snapTarget: (short)(TorsoTwistAngle - yawError), snapEnable: true);
		TorsoPitchTick(world, (short)SimMath.Q8Multiply(SaturatingAbs(pitchAxis), pitchAxis),
			convergeRange: SimMath.FastMagnitude3D(local.X, local.Y, local.Z),
			snapTarget: (short)(TorsoPitchAngle + pitchError), snapEnable: true);

		return (yawError, pitchError);
	}

	/// <summary>
	/// The far-range dead band: below <paramref name="deadband"/> of error the axis is stilled
	/// outright, and otherwise its demand is cut to <see cref="TrackFarGain"/>.
	/// </summary>
	private static int DampedFarDemand(int demand, short error, short deadband) {
		if (error >= TrackTrackingBand) {
			return demand;
		}

		return error < deadband ? 0 : SimMath.Q10Multiply(TrackFarGain, demand);
	}

	/// <summary>Beyond this range in the pilot's frame the dead band applies — the original's 50000.</summary>
	public const int TrackFarRange = 50000;

	/// <summary>The error the far-range damping applies below, in binary angle.</summary>
	private const short TrackTrackingBand = 1000;

	/// <summary>Q10 gain the far-range damping cuts the demand to.</summary>
	private const int TrackFarGain = 700;

	/// <summary>Yaw error the far-range band stills the axis under.</summary>
	private const short TrackYawDeadband = 0x32;

	/// <summary>Pitch error the far-range band stills the axis under — larger than the yaw's.</summary>
	private const short TrackPitchDeadband = 0x55;

	/// <summary>How hard the centring command pulls, Q10 — the original's own <c>0xfa</c>.</summary>
	private const int CenterGain = 0xfa;

	/// <summary>
	/// The shared body of the two ticks: they are the same code in the original, differing only in
	/// which pair of fields and which pair of limits they use.
	/// </summary>
	private static void StepTorsoAxis(ref short rate, ref short angle, short axis, short maxRate,
			short accel, short limitMin, short limitMax) {
		short target = (short)SimMath.Q8Multiply(axis, maxRate);
		short meanFrom = rate;

		if (SaturatingAbs(rate) < SaturatingAbs(target)) {
			short step = (short)SimMath.IntegrateRateOverTick(accel);
			SimMath.RateLimitedMoveToward(ref rate, target, step);
		} else {
			// Slowing down is not rate-limited, and neither is turning round: both land on the
			// target immediately, and the angle integrates the new rate over the whole tick.
			rate = target;
			meanFrom = target;
		}

		short moved = (short)(angle + SimMath.IntegrateRateOverTick((short)((meanFrom + rate) >> 1)));
		angle = moved >= limitMax ? limitMax : moved <= limitMin ? limitMin : moved;
	}

	/// <summary>
	/// Whether the angle moved onto or across <paramref name="target"/> this tick — the original's
	/// own sign test on the before and after differences, which counts landing exactly on it.
	/// </summary>
	private static bool CrossedTarget(short before, short after, short target) {
		int now = (short)(after - target);
		int then = (short)(before - target);
		return (now >= 0 && then < 0) || (now <= 0 && then > 0);
	}

	/// <summary>
	/// An angle as a Q14 position in its sequence: the <b>unsigned</b> angle shifted down two bits,
	/// so a whole turn spans the sequence exactly once and a negative angle lands in its far end
	/// rather than off the front.
	/// </summary>
	private static short SequencePosition(short angle) => (short)((ushort)angle >> 2);

	/// <summary>
	/// <c>|x|</c> as the original computes it, saturating rather than wrapping: negating
	/// <see cref="short.MinValue"/> cannot be represented, and it yields <see cref="short.MaxValue"/>.
	/// </summary>
	private static short SaturatingAbs(short value) =>
		value == short.MinValue ? short.MaxValue : value < 0 ? (short)-value : value;

	/// <summary>Clamps to one stick's travel, as the centring command does before handing it on.</summary>
	private static short ClampAxis(int value) =>
		value >= MechControls.AxisFull ? MechControls.AxisFull
		: value < -MechControls.AxisFull + 1 ? (short)-MechControls.AxisFull
		: (short)value;

	private AnimationThread? AddTorsoThread(ShapeAnimation animation, short sequence) =>
		sequence >= 0 && animation.HasSequence(sequence) ? Shape!.AddThread(sequence) : null;

	/// <summary>
	/// The thread the torso's twist angle is seeked on (<c>mech+0x230</c>), or null when the type
	/// names no twist sequence.
	/// </summary>
	public AnimationThread? TorsoTwistThread { get; }

	/// <summary>The pitch counterpart (<c>mech+0x234</c>).</summary>
	public AnimationThread? TorsoPitchThread { get; }

	/// <summary>
	/// The <c>[\]</c> "Center Body" command's own latch, from <c>Sim_DispatchCommand</c>'s scancode
	/// <c>0x2b</c> case (<c>0045fdac</c>) and the identical one in <c>Sim_PollPlayerInput</c>: it
	/// takes the world direction the turret is pointing in, <c>heading - twist</c>, and everything
	/// the mode does afterwards is measured against that one number.
	///
	/// <para>The two centring commands are exclusive. Each one's dispatch clears the other's global,
	/// so pressing [Backspace] mid-manoeuvre abandons this and brings the turret home instead. Both
	/// also clear ATT (<c>manager+0x14</c>): a pilot who has asked for the turret back does not get
	/// it taken again by the tracker.</para>
	/// </summary>
	private void LatchCenterBody() {
		var controls = Controls;

		if (controls.CenterBody && !_centerBodyHeld) {
			_centeringBody = true;
			_centeringTorso = false;
			Weapons.AutoTrack = false;
			_centerBodyReference = (short)((short)Heading - TorsoTwistAngle);
		}

		_centerBodyHeld = controls.CenterBody;

		if (_centeringBody && controls.CenterTorso) {
			_centeringBody = false;
			_centeringTorso = true;
		}
	}

	/// <summary>
	/// <c>Sim_PollPlayerInput</c>'s Center Body branch (<c>00460764</c>) — the machine walks its legs
	/// round until they point where the turret was when the command was given, unwinding the turret
	/// by exactly as much as the body gains so the pilot keeps looking at the same place throughout.
	///
	/// <para>Two errors drive it, both measured against the captured direction: how far the
	/// <i>heading</i> still is from it, which steers, and how far the <i>turret</i> has drifted off
	/// it, which twists. Both go to zero together, and only then, since heading meeting the reference
	/// forces the twist to be zero.</para>
	///
	/// <para>Each error is gained, then <b>squared</b> and rescaled — the original's own
	/// <c>e² >> 8</c> with the sign put back afterwards. That makes it soft near the target and hard
	/// away from it, which is what stops the legs hunting about the reference. The mode ends when
	/// both squared terms fall under their own thresholds, on the same tick it issues its last
	/// commands.</para>
	///
	/// <para>The pilot keeps the throttle and the pitch axis; only steering and twist are taken.</para>
	/// </summary>
	private void CenterBodyTick(SimWorld world) {
		short heading = (short)Heading;
		short bodyError = (short)(heading - _centerBodyReference);
		short turretError = (short)((short)(heading - TorsoTwistAngle) - _centerBodyReference);

		short steerGain = (short)SimMath.Q10Multiply(CenterBodySteerGain, bodyError);
		short twistGain = (short)SimMath.Q10Multiply(CenterBodyTwistGain, turretError);

		int steer = steerGain * steerGain >> 8;
		int twist = twistGain * twistGain >> 8;

		if (steer < CenterBodySteerDeadband && twist < CenterBodyTwistDeadband) {
			_centeringBody = false;
		}

		if (steerGain < 0) {
			steer = -steer;
		}

		if (twistGain < 0) {
			twist = -twist;
		}

		// Steering inverts when the machine is travelling backwards, read off the object's own speed
		// accessor (mech vtable +0x38, 00415498) rather than off the throttle — the control law does
		// its own inversion from the stick, and this one is on top of it.
		if (TravelSpeed < 0) {
			steer = -steer;
		}

		ApplyThrottleInput(world, (short)steer);
		TorsoTwistTick(world, (short)twist);
		TorsoPitchTick(world, Controls.TorsoPitch, GunConvergenceRange);
	}

	/// <summary>Q10 gain on the heading error before it is squared into a steering command.</summary>
	private const int CenterBodySteerGain = 100;

	/// <summary>Q10 gain on the turret error, lower than the steering one so the turret trails.</summary>
	private const int CenterBodyTwistGain = 0x46;

	/// <summary>Squared-steering term the mode disengages under, with the turret one below.</summary>
	private const int CenterBodySteerDeadband = 0x1e;

	private const int CenterBodyTwistDeadband = 10;

	/// <summary>
	/// <c>Sim_PollPlayerInput</c>'s turret block (<c>00460764</c>), which runs between the throttle
	/// and the move. Three cases, in the original's own order of tests: the pilot is holding the
	/// turret axes, Automatic Turret Tracking is flying the turret for him, or the centring command
	/// is latched and drives them instead.
	///
	/// <para><b>The axes come first</b>, because touching either one drops both of the other two:
	/// tracking is skipped for the tick and the centring latch is cleared outright. That is the
	/// manual's "take manual control of the turret" — the pilot always wins the axis he is
	/// holding.</para>
	///
	/// <para><b>Automatic Turret Tracking (ATT, [T])</b> is the middle case. It needs the TRACK latch
	/// (<see cref="WeaponMounts.AutoTrack"/>), a selected target, and that target not destroyed
	/// (<c>+0x99</c> alone — a crippled target is still tracked, unlike everywhere the AI tests
	/// liveness). It aims at the target's <see cref="SimObject.AimPoint"/>, which for a HERC is its
	/// cockpit node and is what the manual means by "ATT aims at the target's center", and it clears
	/// the centring latch on the way past. <see cref="TrackWorldPoint"/> runs both axis ticks itself,
	/// convergence included, so the manual pair below is skipped for the tick.</para>
	/// </summary>
	private void TorsoTick(SimWorld world) {
		var controls = Controls;
		bool tracked = false;

		if (controls.CenterTorso) {
			LatchCenterTorso();
		}

		if (controls.TorsoTwist != 0 || controls.TorsoPitch != 0) {
			_centeringTorso = false;
		} else if (Weapons.AutoTrack) {
			if (Target is { Destroyed: false } target) {
				TrackWorldPoint(world, target.AimPoint);
				_centeringTorso = false;
				tracked = true;
			} else if (Target == null && SimMath.CountdownTimerTick(ref _autoTrackIdle) == 0) {
				// ATT left holding nothing brings the turret home once its timer runs out, and does
				// not clear the latch: selecting again puts the turret straight back on a target.
				_centeringTorso = true;
			}
		}

		if (_centeringTorso) {
			CenterTorsoTick(world, GunConvergenceRange);
			return;
		}

		if (tracked) {
			return;
		}

		TorsoTwistTick(world, controls.TorsoTwist);
		TorsoPitchTick(world, controls.TorsoPitch, GunConvergenceRange);
	}

	/// <summary>
	/// What <c>Sim_PollPlayerInput</c> hands the pitch tick to converge the guns on: the 3D distance to
	/// the selected target, or zero with nothing selected. So the player's guns toe in on whatever the
	/// targeting system is holding, and square up when it is let go. <c>Razor_MovementTick</c> computes
	/// the same figure for a flyer (<c>Math_DistanceBetweenPoints</c>, <c>00492780</c>) — see
	/// <see cref="FlyerMovementTick"/>.
	/// </summary>
	private int GunConvergenceRange =>
		Target is { } target ? Position.ApproxDistanceTo(target.Position) : 0;

	/// <summary>
	/// <c>ConsoleButtons_ToggleAutoTrack</c> (<c>00441f7c</c>) — flips ATT and announces the new
	/// state, which is the whole of what the console's TRACK button does. Both messages are withdrawn
	/// before the new one is posted, so flipping twice quickly says where it ended up rather than
	/// reading out the sequence; the radar toggle is written the same way.
	///
	/// <para>The [T] command is this plus a tail: turning ATT <i>off</i> that way also centres the
	/// turret. See <see cref="LatchCenterTorso"/>.</para>
	/// </summary>
	/// <returns>Whether ATT is now on.</returns>
	public bool ToggleAutoTrack(SimWorld? world = null) {
		Weapons.AutoTrack = !Weapons.AutoTrack;

		if (world?.Sounds is { } sounds) {
			sounds.Unsay(Content.SystemMessages.AutoTrackingEngaged);
			sounds.Unsay(Content.SystemMessages.AutoTrackingDisabled);
			sounds.Say(Weapons.AutoTrack
				? Content.SystemMessages.AutoTrackingEngaged
				: Content.SystemMessages.AutoTrackingDisabled);
		}

		return Weapons.AutoTrack;
	}

	/// <summary>
	/// The three writes <c>Sim_DispatchCommand</c> makes wherever the "Center Turret" command is
	/// issued — its scancode <c>0x0e</c> case ([Backspace]) and the tail of its <c>0x14</c> case
	/// ([T], when the toggle it just ran turned ATT <i>off</i>): the centring mode goes on, Center
	/// Body goes off, and ATT's own latch (<c>manager+0x14</c>) is cleared.
	///
	/// <para>ATT is cleared here rather than left alone because a pilot who has asked for the turret
	/// back would otherwise have it taken again by the tracker on the very next tick. It is also what
	/// the manual says the command does.</para>
	/// </summary>
	public void LatchCenterTorso() {
		_centeringTorso = true;
		_centeringBody = false;
		Weapons.AutoTrack = false;
	}

	/// <summary>Whether [Backspace] centring is latched, for the debug readout.</summary>
	public bool CenteringTorso => _centeringTorso;

	/// <summary>
	/// Whether [\] Center Body is latched, and the turret world direction it is steering the legs
	/// onto — both for the debug readout.
	/// </summary>
	public bool CenteringBody => _centeringBody;

	/// <inheritdoc cref="CenteringBody"/>
	public short CenterBodyReference => _centerBodyReference;

	// g_CenterTurretMode (004d2588) — the latched centring mode. A global in the original, since only the player has
	// one; per-object here for the same reason SimWorld has no globals.
	private bool _centeringTorso;

	// g_CenterBodyMode (004d2af4) and g_CenterBodyTargetHeading (004d2af8) — the Center Body mode and the turret world direction it was
	// latched on, globals in the original for the same reason. _centerBodyHeld is the edge detector
	// the original gets for free from being dispatched on a keystroke rather than on a held key.
	private bool _centeringBody;
	private bool _centerBodyHeld;
	private short _centerBodyReference;
}
