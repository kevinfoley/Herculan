using Herculan.Engine.Numerics;

namespace Herculan.Engine.Sim;

/// <summary>
/// What <see cref="FlightPhysics.Step"/> needs of the airframe it is flying — its attitude, which the
/// model both reads and writes, the height of its origin, and which of its four flight-relevant
/// components are still there. Two classes fly: a HERC whose type record sets the flyer flag (the
/// RAZOR, in <see cref="MechObject"/>) and the <c>Flyer</c> class proper
/// (<see cref="FlyerObject"/>), and DBSIM reaches both through the one
/// <c>FlightModel_Step</c> (<c>00466a54</c>).
/// </summary>
internal interface IFlightBody {
	/// <summary>Body pitch, as a binary angle.</summary>
	short Pitch { get; set; }

	/// <summary>Body roll — the axis an aircraft actually turns with.</summary>
	short Roll { get; set; }

	/// <summary>Heading, as a binary angle in <c>[0, 0x10000)</c>.</summary>
	int Heading { get; set; }

	/// <summary>The origin's world height, which the ceiling test measures against the ground.</summary>
	int PositionZ { get; }

	/// <summary>
	/// The airframe's shape-to-world transform, rebuilt from the euler triple whenever
	/// <see cref="FlightAttitudeChanged"/> has invalidated it. This is the object's own
	/// <c>obj+0x12</c> matrix with <c>obj+0x26</c> in the translation.
	/// </summary>
	Transform3 FlightFrame { get; }

	/// <summary>Called after the model writes a new attitude, to drop the cached matrix.</summary>
	void FlightAttitudeChanged();

	/// <summary>
	/// Whether one of the components named in <see cref="FlightPhysics"/>'s component constants is
	/// still standing. A <see cref="FlyerObject"/> has only component 0, so the four the model asks
	/// about are all intact for as long as it is flying at all.
	/// </summary>
	bool AirframeIntact(int component);
}

/// <summary>
/// The flight state block every flyer carries — <c>mech+0x2b9</c> on a RAZOR and <c>flyer+0x243</c>
/// on a SKIMMER, which are the same 0x4e bytes laid out identically; both constructors zero the
/// block, seed the airspeed and copy the throttle in. Held as a struct so the owning object keeps it
/// inline, as the original does.
/// </summary>
public struct FlightBlock {
	/// <summary>
	/// Velocity in the airframe's own frame, world units per unit time — block <c>+0x00</c>. X is
	/// sideslip, <b>Y is airspeed</b>, Z is the vertical component.
	/// </summary>
	public Vec3i BodyVelocity;

	/// <summary>The same velocity in world space — block <c>+0x0c</c>, and what the move integrates.</summary>
	public Vec3i WorldVelocity;

	/// <summary>Pitch rate, block <c>+0x18</c>, in binary angle per unit time.</summary>
	public short PitchRate;

	/// <summary>Roll rate, block <c>+0x1a</c>.</summary>
	public short RollRate;

	/// <summary>Yaw rate, block <c>+0x1c</c> — what the rudder builds.</summary>
	public short YawRate;

	/// <summary>
	/// Block <c>+0x1e</c> — the flyer's own throttle setting, ±0x400. Distinct from a walker's
	/// throttle field; the flight model owns this one.
	/// </summary>
	public short Throttle;

	/// <summary>
	/// Block <c>+0x44</c> — the heading rate the current bank angle is producing. <b>This is the
	/// aircraft's turning.</b> The rudder yaws the airframe about its own axis; what swings the nose
	/// round the sky is the bank.
	/// </summary>
	public int BankTurnRate;

	// Block +0x24 and +0x38 (mech+0x2dd and mech+0x2f1) — last tick's transform, inverted. The model needs it twice: to bring the
	// world-space drag back into the body frame, and to re-express world velocity in the new body
	// frame after the airframe has rotated.
	internal Transform3 PreviousInverse;
	internal bool PreviousInverseValid;
}

/// <summary>
/// <c>FlightModel_Step</c> (<c>00466a54</c>) — the simulation's one flight model, shared by the
/// player's RAZOR and by every Cybrid flyer. It settles, in this order: the throttle setting and the
/// airspeed it asks for, the sideslip drag, the three angular rates, the new attitude, and finally
/// the velocity vector in that new attitude. See docs/retail/simulation/razor-flight.md.
///
/// <para><b>Nothing here moves the aircraft.</b> What it produces is
/// <see cref="FlightBlock.WorldVelocity"/>, which each class' own move tick integrates.</para>
/// </summary>
internal static class FlightPhysics {
	/// <summary>Full throttle either way — the same ±0x400 range the walker's throttle spans.</summary>
	public const short ThrottleFull = 0x400;

	/// <summary>Q8 gain from the throttle axis to throttle movement per unit time.</summary>
	private const int ThrottleRate = 100;

	/// <summary>
	/// Q10 gain from pitch attitude to demanded airspeed, <b>nose down</b>. Four times the nose-up
	/// figure, so a dive builds speed far faster than a climb sheds it. This is the whole of the
	/// aircraft's gravity, and note where it lands: on the speed the throttle asks for, not on
	/// velocity. Level out and the speed returns to whatever the throttle wants — there is no
	/// momentum to trade.
	/// </summary>
	private const int DiveSpeedGain = 250;

	/// <inheritdoc cref="DiveSpeedGain"/>
	private const int ClimbSpeedGain = 62;

	/// <summary>Q10 gain from how far over the ceiling the aircraft is to the nose-down push.</summary>
	private const int CeilingPushGain = 10;

	/// <summary>
	/// The fraction of any forward speed lost to a turn that is handed straight back, Q10. Velocity
	/// is re-expressed in the airframe's new body frame every tick, which costs forward speed
	/// whenever it rotates; 900/1024 of that is returned, so a hard turn scrubs about 12% and no
	/// more.
	/// </summary>
	private const int TurnSpeedRecovery = 900;

	/// <summary>The bank angle a lost wing drifts the aircraft toward — 0x1000 is 22.5°.</summary>
	private const short LostWingBank = 0x1000;

	/// <summary>The Q14 gain toward <see cref="LostWingBank"/>. Small: it is a lean, not a spin.</summary>
	private const int LostWingGain = 0x14;

	/// <summary>
	/// The airframe components the flight path knows by name, in the game's own terms: the
	/// <c>STRINGS0</c> group 14 damage-readout list a flyer subject takes in place of the walker's
	/// group 13 reads 0 COCKPIT ARMOR, 4/5 L/R NACELLE ARMOR, 6 FUSELAGE ARMOR and 7/8 L/R WING
	/// ARMOR. The wings and nacelles are the four the flight model answers to; the cockpit and
	/// fuselage are the two whose loss ends the flight.
	///
	/// <para>Component 4 being the <i>left</i> nacelle is also what settles the frame's handedness:
	/// its probe point sits at negative X, so -X is port and +X starboard.</para>
	/// </summary>
	public const int ComponentCockpit = 0;

	/// <inheritdoc cref="ComponentCockpit"/>
	public const int ComponentLeftNacelle = 4;

	/// <inheritdoc cref="ComponentCockpit"/>
	public const int ComponentRightNacelle = 5;

	/// <inheritdoc cref="ComponentCockpit"/>
	public const int ComponentFuselage = 6;

	/// <inheritdoc cref="ComponentCockpit"/>
	public const int ComponentLeftWing = 7;

	/// <inheritdoc cref="ComponentCockpit"/>
	public const int ComponentRightWing = 8;

	/// <summary>
	/// One step of the model. <paramref name="analogueThrottle"/> is the original's
	/// input-preferences test, <see cref="Input.JoystickBindings.FlightThrottleIsLever"/>: when it holds
	/// the axis is read as a position rather than a rate. It is a global in DBSIM, so it applies to <i>every</i> airframe in the mission
	/// rather than only to the one the player flies — and an AI flyer passes a zero throttle axis, so
	/// with such a device configured every Cybrid flyer's throttle is held at the middle of its range;
	/// see <see cref="FlyerObject"/>.
	/// </summary>
	public static void Step(IFlightBody body, ref FlightBlock state, FlightModelRecord flight,
			short aileron, short elevator, short rudder, short throttleAxis, int groundHeight,
			bool analogueThrottle) {
		var fm = flight.Data;

		if (!state.PreviousInverseValid) {
			// The original builds this in the constructor. Here it is seeded on the first tick
			// instead, because a mech is positioned and headed after it is constructed and the
			// constructor's copy would be of an attitude the machine never actually had.
			state.PreviousInverse = body.FlightFrame.Inverted();
			state.PreviousInverseValid = true;
		}

		// --- Throttle ---------------------------------------------------------------------------
		if (analogueThrottle) {
			// An analogue throttle is a position, not a rate. Half the axis' travel covers the whole
			// range, and unlike the walker's lever there is no inverted sense and no clamp to one
			// side of zero — a flyer's throttle spans the same signed range either way.
			state.Throttle = ClampThrottle(throttleAxis << 3);
		} else {
			short rate = (short)SimMath.Q8Multiply(ThrottleRate, throttleAxis);
			if (rate != 0) {
				state.Throttle =
					ClampThrottle(state.Throttle + SimMath.IntegrateRateOverTick(rate));
			}
		}

		// --- Airspeed ---------------------------------------------------------------------------
		int speedRange = fm.AirSpeedMax - fm.AirSpeedMin;
		int demand = SimMath.Q10Multiply(speedRange, (state.Throttle + ThrottleFull) >> 1)
			+ fm.AirSpeedMin;
		demand -= SimMath.Q10Multiply(body.Pitch < 0 ? DiveSpeedGain : ClimbSpeedGain, body.Pitch);

		int airSpeed = state.BodyVelocity.Y;
		MoveToward(ref airSpeed, demand, SimMath.IntegrateRateOverTick(fm.ThrustResponse));

		// --- Sideslip drag ----------------------------------------------------------------------
		// The sideways and vertical components of body velocity are taken into world space, scaled
		// down, brought back through *last* tick's frame and subtracted. Forward is excluded, which
		// is what makes this drag rather than braking, and it is why a RAZOR flies where it is
		// pointing instead of sliding round its own turns.
		var frame = body.FlightFrame;
		int lateral = state.BodyVelocity.X;
		int vertical = state.BodyVelocity.Z;

		var slip = frame.RotateVector(lateral, 0, vertical);

		// Only the two ground-plane components are scaled by the coefficient; the world-vertical one
		// is subtracted whole, at an effective coefficient of 1. The asymmetry is the original's and
		// is spelled out in its own instructions (00466c26-00466c67 scales two of the three), and it
		// is load-bearing: it is why a RAZOR sheds vertical speed far harder than sideslip, and so
		// why it settles onto its flight path rather than floating.
		var drag = state.PreviousInverse.RotateVector(
			SimMath.Q10Multiply(fm.LateralDrag, slip.X),
			SimMath.Q10Multiply(fm.LateralDrag, slip.Y),
			slip.Z);

		lateral -= SimMath.IntegrateRateOverTick((short)drag.X);
		airSpeed -= SimMath.IntegrateRateOverTick((short)drag.Y);
		vertical -= SimMath.IntegrateRateOverTick((short)drag.Z);

		state.BodyVelocity = new Vec3i(lateral, airSpeed, vertical);
		state.WorldVelocity = frame.RotateVector(lateral, airSpeed, vertical);

		// --- Angular commands -------------------------------------------------------------------
		short previousPitchRate = state.PitchRate;
		short previousRollRate = state.RollRate;
		short previousYawRate = state.YawRate;

		bool leftWingGone = !body.AirframeIntact(ComponentLeftWing);
		bool rightWingGone = !body.AirframeIntact(ComponentRightWing);
		bool leftNacelleGone = !body.AirframeIntact(ComponentLeftNacelle);
		bool rightNacelleGone = !body.AirframeIntact(ComponentRightNacelle);

		short roll = body.Roll;
		int ceiling = flight.Ceiling(airSpeed);

		if (rightNacelleGone || leftNacelleGone) {
			// A nacelle gone takes the elevator with it and jams it nose-down, resolved through
			// the bank so that "down" stays down however the aircraft is lying.
			elevator = (short)(-(int)SimTrig.Cos(roll) >> 6);
		}

		int pitchCommand;
		int pitchDamping = 0;
		if (elevator == 0) {
			// Pitch self-levelling, which on retail data is switched off: both flight models state a
			// shift of 16, and a 16-bit angle shifted 16 is nothing. An aircraft holds the attitude
			// it was trimmed to and bleeds its pitch rate off through the damping term instead.
			pitchCommand = -(int)body.Pitch >> (fm.PitchLevelShift & 0x1f);
			pitchDamping = -SimMath.Q10Multiply(fm.AngularDamping, state.PitchRate);
		} else {
			pitchCommand = SimMath.Q8Multiply(fm.MaxPitchRate, elevator);
		}

		int yawCommand = SimMath.Q8Multiply(fm.MaxYawRate, -rudder);

		int altitudeAboveGround = body.PositionZ - groundHeight;
		if (ceiling < altitudeAboveGround) {
			// Over the ceiling. The push is a vector in the aircraft's own frame pointing at the
			// ground — cosine of the bank onto pitch, its quarter-turn shift onto yaw — so a RAZOR
			// held over the ceiling inverted is pushed the way that actually takes it down. Note it
			// only ever *lowers* the pitch command: it can refuse a climb but never force one.
			short push =
				(short)-SimMath.Q10Multiply(CeilingPushGain, altitudeAboveGround - ceiling);

			yawCommand = (short)SimMath.Q14Multiply(
				SimTrig.Cos((short)(roll - BinaryAngle.QuarterTurn)), push);

			int pitchPush = (short)SimMath.Q14Multiply(SimTrig.Cos(roll), push);
			if (pitchPush < pitchCommand) {
				pitchCommand = pitchPush;
			}
		} else if (elevator == 0) {
			// The original recomputes here the damping it already has; transcribed rather than
			// folded away so the two branches stay comparable with the disassembly.
			pitchDamping = -SimMath.Q10Multiply(fm.AngularDamping, state.PitchRate);
		}

		// --- Aileron, and what a lost wing does to it ---------------------------------------------
		int aileronCommand = aileron;
		if (rightNacelleGone) {
			aileronCommand = MechControls.AxisFull;
		} else if (leftNacelleGone) {
			aileronCommand = -MechControls.AxisFull;
		} else if (rightWingGone) {
			// A lost wing is survivable where a lost nacelle is not: rather than pinning the
			// stick it adds a small bias that settles the aircraft at a permanent 22.5° lean, which
			// the pilot can hold off but has to keep holding off.
			if (roll < LostWingBank) {
				aileronCommand += (short)SimMath.Q14Multiply(
					LostWingGain, (short)(LostWingBank - roll));
			}
		} else if (leftWingGone) {
			if (roll > -LostWingBank) {
				aileronCommand -= (short)SimMath.Q14Multiply(
					LostWingGain, (short)(roll + LostWingBank));
			}
		}

		int bankMagnitude = roll == short.MinValue ? short.MaxValue : System.Math.Abs((int)roll);

		// Past a quarter turn of bank the sense inverts, measured from the half turn instead — which
		// is what lets an inverted RAZOR turn the way its wings say rather than backwards.
		state.BankTurnRate = bankMagnitude < BinaryAngle.QuarterTurn
			? -(int)roll >> (fm.BankTurnShift & 0x1f)
			: (short)(roll - short.MinValue) >> (fm.BankTurnShift & 0x1f);

		int rollCommand;
		int rollDamping = 0;
		if (aileronCommand == 0) {
			rollCommand = -(int)roll >> (fm.RollLevelShift & 0x1f);
			rollDamping = -SimMath.Q10Multiply(fm.AngularDamping, state.RollRate);

			short settled = (short)(state.RollRate + rollDamping);
			int settledMagnitude =
				settled == short.MinValue ? short.MaxValue : System.Math.Abs((int)settled);

			if (bankMagnitude < settledMagnitude) {
				// The wings would cross level this tick. Stop them exactly there rather than let the
				// self-levelling term carry them past and set up a wallow.
				rollCommand = 0;
				rollDamping = -bankMagnitude - state.RollRate;
			}
		} else {
			rollCommand = SimMath.Q8Multiply(fm.MaxRollRate, aileronCommand);

			// Damping only when the stick is fighting the roll already under way, so reversing a
			// roll is crisp while holding one costs nothing.
			if ((aileronCommand > 0 && state.RollRate < 0) || (aileronCommand < 0 && state.RollRate > 0)) {
				rollDamping = -SimMath.Q10Multiply(fm.AngularDamping, state.RollRate);
			}
		}

		// --- Rates --------------------------------------------------------------------------------
		// Each axis' command is clamped to a maximum acceleration, the damping goes on outside that
		// clamp, and the resulting rate is clamped to a maximum rate. Yaw takes the roll axis' own
		// acceleration limit; the flight model has only the two.
		short pitchAccel =
			(short)(pitchDamping + ClampSymmetric((short)pitchCommand, fm.MaxPitchAccel));
		short rollAccel = (short)(rollDamping + ClampSymmetric((short)rollCommand, fm.MaxRollAccel));
		short yawAccel = (short)(ClampSymmetric((short)yawCommand, fm.MaxRollAccel)
			- SimMath.Q10Multiply(fm.AngularDamping, state.YawRate));

		state.PitchRate = ClampSymmetric(
			(short)(state.PitchRate + SimMath.IntegrateRateOverTick(pitchAccel)), fm.MaxPitchRate);
		state.RollRate = ClampSymmetric(
			(short)(state.RollRate + SimMath.IntegrateRateOverTick(rollAccel)), fm.MaxRollRate);
		state.YawRate = ClampSymmetric(
			(short)(state.YawRate + SimMath.IntegrateRateOverTick(yawAccel)), fm.MaxYawRate);

		// --- Attitude -----------------------------------------------------------------------------
		// The rotation is integrated as a *matrix*, from the mean of this tick's rates and last
		// tick's, and the euler triple is read back out of the result. That is what keeps a RAZOR
		// flyable through a vertical climb, where integrating the three angles directly would
		// gimbal — and it is the one place in the simulation that composes a rotation this way.
		var step = Transform3.FromEuler(
			(short)SimMath.IntegrateRateOverTick(Mean(previousPitchRate, state.PitchRate)),
			(short)SimMath.IntegrateRateOverTick(Mean(previousRollRate, state.RollRate)),
			(short)SimMath.IntegrateRateOverTick(Mean(previousYawRate, state.YawRate)));

		var (pitch, rolled, heading) = Transform3.Concat(step, frame).ToEuler();
		body.Pitch = pitch;
		body.Roll = rolled;

		// The bank-driven turn goes on top of the integrated attitude rather than through it, which
		// is why a banked RAZOR turns about the world's vertical axis and not about its own.
		body.Heading = (heading + SimMath.IntegrateRateOverTick((short)state.BankTurnRate)) & 0xffff;
		body.FlightAttitudeChanged();

		var settledFrame = body.FlightFrame;
		state.PreviousInverse = settledFrame.Inverted();

		// --- Velocity in the new frame -------------------------------------------------------------
		short before = (short)state.BodyVelocity.Y;
		var bodyVelocity = state.PreviousInverse.RotateVector(
			state.WorldVelocity.X, state.WorldVelocity.Y, state.WorldVelocity.Z);
		state.BodyVelocity = bodyVelocity;

		if (bodyVelocity.Y < before) {
			// Rotating velocity into the new attitude costs forward speed. Most of it is handed
			// straight back, so a hard turn scrubs a little energy rather than stalling the aircraft.
			state.BodyVelocity = new Vec3i(bodyVelocity.X,
				bodyVelocity.Y + SimMath.Q10Multiply(TurnSpeedRecovery, before - bodyVelocity.Y), bodyVelocity.Z);
			state.WorldVelocity = settledFrame.RotateVector(
				state.BodyVelocity.X, state.BodyVelocity.Y, state.BodyVelocity.Z);
		}
	}

	/// <summary>The throttle's ±0x400 clamp, applied wherever the setting is written.</summary>
	public static short ClampThrottle(int value) =>
		value >= ThrottleFull ? ThrottleFull
		: value <= -ThrottleFull ? (short)-ThrottleFull
		: (short)value;

	/// <summary>Clamps to ±<paramref name="limit"/>, the shape every rate and acceleration cap takes.</summary>
	private static short ClampSymmetric(short value, short limit) {
		short low = (short)-limit;
		return value >= limit ? limit : value <= low ? low : value;
	}

	/// <summary>The mean of this tick's rate and last tick's, which is what the attitude integrates.</summary>
	private static short Mean(short a, short b) => (short)((a + b) >> 1);

	/// <summary>
	/// <c>Math_RateLimitedMoveTowardInt</c> (<c>00467a24</c>) —
	/// <see cref="SimMath.RateLimitedMoveToward"/> on a 32-bit value. Airspeed is an int where every
	/// rate the walker slews is a short, so the original has both.
	/// </summary>
	private static void MoveToward(ref int current, int target, int step) {
		if (target < current) {
			current -= step;
			if (current < target) {
				current = target;
			}
		} else if (current < target) {
			current += step;
			if (target < current) {
				current = target;
			}
		}
	}
}
