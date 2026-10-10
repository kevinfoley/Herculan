using Herculan.Engine.Numerics;
using Herculan.Engine.Settings;

namespace Herculan.Engine.Sim;

// A Cybrid flyer's flight half: the attitude it carries, the flight-model command chain its AI
// drives, and the per-tick move. Ported from Flyer_MovementTick (004218c4, the flyer's descriptor
// +0x24 slot), Flyer_ApplyFlightCommand / Flyer_CommandRoll / Flyer_SteerAndFly (the control law)
// and Flyer_PitchCommand / Flyer_PitchToAltitude (the pitch channel). See
// docs/retail/simulation/ai-flyers.md; the flight model itself is shared with the player's RAZOR, in
// FlightPhysics — docs/retail/simulation/razor-flight.md.
public sealed partial class FlyerObject : IFlightBody {
	/// <summary>
	/// The airspeed a flyer powers up at — <c>Flyer_Constructor</c>'s literal into
	/// <c>flyer+0x247</c>, the same one <c>Mech_Constructor</c> writes for a RAZOR.
	/// </summary>
	private const int InitialAirSpeed = 1000;

	/// <summary>
	/// <c>flyer+0x261</c>'s literal, on the model's ±0x400 throttle scale. <b>Nothing a Cybrid flyer
	/// decides moves its throttle.</b> The control law leaves the command array's throttle element zero
	/// on every call, and <c>Flyer_FormationThrottle</c> (<c>00422260</c>)'s accumulator at
	/// <c>flyer+0x21c</c> is never handed to the model. Only the player's own controls can overwrite
	/// it — see <see cref="ApplyFlightCommand"/>.
	/// </summary>
	private const short InitialThrottle = 0x200;

	/// <summary>
	/// How far above the terrain the move refuses to let an aircraft sink, in world units. The
	/// original's <c>terrainHeight + 500</c> — three metres, so a flyer driven into a hillside stops
	/// on it rather than through it.
	/// </summary>
	private const int GroundClearance = 500;

	/// <summary>
	/// Range at which the flyby loop starts, and beyond which the latch is released so the next pass
	/// plays it again.
	/// </summary>
	private const int FlybySoundRange = 30000;

	/// <summary>Q10 gain from how deep a wingtip is in the ground to the roll kick that lifts it.</summary>
	private const int WingRollGain = 4000;

	/// <summary>The nose probe's Q10 gain — half the wings', and it pitches up rather than rolling.</summary>
	private const int NosePitchGain = 2000;

	/// <summary>
	/// The look-ahead probe's Q10 gain. A hundredth of the nose probe's, because it fires long before
	/// anything is actually touched: this is the terrain-following autopilot, not a contact.
	/// </summary>
	private const int LookAheadPitchGain = 0x14;

	/// <summary>
	/// Q16 gain from pitch attitude error to the elevator command, in <c>Flyer_PitchCommand</c> (<c>00422098</c>).
	/// </summary>
	private const int PitchGain = 1500;

	/// <summary>Q16 gain from pitch rate to the elevator's damping term.</summary>
	private const int PitchRateDamping = 8000;

	/// <summary>
	/// The horizontal run <c>Flyer_PitchToAltitude</c> (<c>00422108</c>) measures a height error against when it turns "get to
	/// this altitude" into "hold this pitch". It is a constant, not the real distance to anything, so
	/// the demanded climb angle depends on the height error alone.
	/// </summary>
	private const int PitchAltitudeRun = 10000;

	/// <summary>Q16 gain from bank error to the aileron command, in <c>Flyer_CommandRoll</c> (<c>0042215c</c>).</summary>
	private const int RollGain = 2500;

	/// <summary>Q16 gain from roll rate to the aileron's damping term. Negative: it opposes.</summary>
	private const int RollRateDamping = -5000;

	/// <summary>Q16 gain from the steering command to the bank the aircraft answers it with.</summary>
	private const int TurnToBankGain = 2500;

	/// <summary>
	/// Q16 gain from the heading rate the current bank is already producing back into the bank
	/// command — the term that stops a turn once it is actually coming round.
	/// </summary>
	private const int BankRateGain = 32000;

	/// <summary>
	/// How far inside the chassis' <c>MaxBankAngle</c> the controller starts refusing to roll
	/// further. The band is what keeps it from chattering on the limit.
	/// </summary>
	private const short BankHysteresis = 1500;

	/// <summary>
	/// Steering command below which the aircraft uses <b>rudder</b> and holds its wings level instead
	/// of banking. A small heading correction is a skid, not a turn.
	/// </summary>
	private const short RudderTurnLimit = 2000;

	/// <summary>
	/// Bank angle from which even a small correction gets no rudder — the wings come level first. The
	/// test is signed, so a bank to the negative side never blocks the rudder.
	/// </summary>
	private const short RudderRollLimit = 800;

	/// <summary>Q16 gain from the steering command to that rudder input.</summary>
	private const int RudderGain = 4000;

	/// <summary>
	/// This chassis' <c>fm\&lt;NAME&gt;.FM</c>, or null when the install ships none. Only
	/// <c>SKIMMER</c> does, so <c>HOVTANK</c> and <c>DROPSHIP</c> cannot fly at all — which is the
	/// original's outcome too, since its flyer type loader reads the file unconditionally and would
	/// be working from whatever the failed read left behind.
	/// </summary>
	public FlightModelRecord? Flight { get; }

	/// <summary>
	/// <c>flyer+0x243</c> — the flight state block, laid out exactly as a RAZOR's at
	/// <c>mech+0x2b9</c>. <see cref="FlightPhysics.Step"/> works on it.
	/// </summary>
	private FlightBlock _flight;

	/// <summary>Airspeed — <c>flyer+0x247</c>, the Y of the body-frame velocity.</summary>
	public int AirSpeed => _flight.BodyVelocity.Y;

	/// <summary>
	/// The world velocity the move adds to the position — <c>flyer+0x24f</c>. A <b>per-tick step</b>,
	/// not a rate; see <see cref="MovementTick"/>.
	/// </summary>
	public Vec3i FlightWorldVelocity => _flight.WorldVelocity;

	/// <inheritdoc />
	int IFlightBody.PositionZ => Position.Z;

	/// <inheritdoc />
	Transform3 IFlightBody.FlightFrame => Rotation();

	/// <inheritdoc />
	void IFlightBody.FlightAttitudeChanged() => _rotationValid = false;

	/// <inheritdoc />
	/// <remarks>
	/// Always true. The four components the flight model asks about are a RAZOR's wings and
	/// nacelles, which a <c>Flyer</c> does not have — its health record is one component and one
	/// dependent. The original says so literally: <c>Flyer_ApplyFlightCommand</c> (<c>004221a8</c>) hands the model a
	/// stack-allocated four-element array it has just zeroed.
	/// </remarks>
	bool IFlightBody.AirframeIntact(int component) => true;

	/// <inheritdoc />
	/// <remarks>
	/// A flyer's frame is its full attitude: unlike a structure it banks and pitches, and the
	/// contact probes are placed through it.
	/// </remarks>
	public override Transform3 WorldFrame => Rotation();

	/// <summary>
	/// The object's world transform, rebuilt from the euler triple when the attitude has moved —
	/// the matrix at <c>flyer+0x12</c> with <c>flyer+0x26</c> in the translation, guarded by the
	/// stale flag at <c>flyer+0x32</c>.
	/// </summary>
	private Transform3 Rotation() {
		if (!_rotationValid) {
			_rotation = Transform3.FromEuler(Pitch, Roll, (short)Heading);
			_rotationValid = true;
		}

		var position = Position;
		_rotation.X = position.X;
		_rotation.Y = position.Y;
		_rotation.Z = position.Z;
		return _rotation;
	}

	private Transform3 _rotation;
	private bool _rotationValid;

	/// <summary>
	/// <c>Flyer_MovementTick</c> (<c>004218c4</c>) — the flyer's per-tick move, the function its behaviour descriptors carry
	/// in the <c>+0x24</c> slot where a walking machine carries <c>Mech_MovementTick</c>. It
	/// adds the velocity the control law settled on, runs four terrain probes over the
	/// airframe, refuses to let the aircraft sink into the ground, and pitches the flyby loop.
	///
	/// <para><b>The probes are lighter than a RAZOR's.</b> There are four rather than seven, none of
	/// them sweeps forward against objects, and <b>none of them does any damage</b>: a Cybrid flyer
	/// that scrapes a hillside is rolled or pitched off it and flies on. Nor is there any gate on
	/// being wrecked: none is needed, because the <c>dead</c> state carries no move slot at all and
	/// <c>Flyer_ComponentDamageWrite</c> has already dropped the wreck's Z to -100000.</para>
	///
	/// <para>The frame the probes are placed through is captured <i>after</i> the position moves and
	/// is <b>not</b> rebuilt as they go, even though each contact marks the attitude stale. That is
	/// the original's own behaviour — it holds the matrix pointer across all four — so the whole
	/// group of probes reads this tick's position against last tick's attitude.</para>
	/// </summary>
	internal void MovementTick(SimWorld world) {
		// The velocity goes on RAW, not through Math_IntegrateRateOverTick — three plain ADDs at
		// 0042190a. That is what makes a flyer fast: its world velocity is a per-tick step where
		// Razor_MovementTick's is a rate. Integrating it here scales it down by the tick fraction and leaves a SKIMMER
		// crawling.
		var velocity = _flight.WorldVelocity;
		Position = new Vec3i(
			Position.X + velocity.X, Position.Y + velocity.Y, Position.Z + velocity.Z);

		var frame = Rotation();

		// The wing pair. A wingtip in the ground rolls the airframe away from it, which lifts the tip
		// out rather than dragging it along.
		WingProbe(world, frame, ProbeRightWing, rollAway: -1);
		WingProbe(world, frame, ProbeLeftWing, rollAway: 1);

		// The nose, and then the look-ahead point far in front and well below it. Both pitch up, and
		// both first clear any downward pitch rate, so a climb ordered by the terrain is not fighting
		// a dive the control law asked for.
		PitchProbe(world, frame, ProbeNose, NosePitchGain);
		PitchProbe(world, frame, ProbeLookAhead, LookAheadPitchGain);

		int ground = world.GroundHeightAt(Position);
		if (Position.Z < ground + GroundClearance) {
			Position = new Vec3i(Position.X, Position.Y, ground + GroundClearance);
		}

		UpdateFlybySound(world);
	}

	// The four probe points, in the airframe's own frame and in world units. Model forward is +Y and
	// +Z is up, so the wingtips sit six metres out either side, slightly aft and slightly low; the
	// nose point is ahead; and the look-ahead point is ninety metres ahead and nine below.
	private static readonly Vec3i ProbeRightWing = new(1000, -420, -300);
	private static readonly Vec3i ProbeLeftWing = new(-1000, -420, -300);
	private static readonly Vec3i ProbeNose = new(0, 1000, 0);
	private static readonly Vec3i ProbeLookAhead = new(0, 15000, -1500);

	/// <summary>
	/// One wingtip against the ground beneath it. The roll rate is kicked away from the contact and
	/// applied to the attitude in the same tick, leaving the rate standing for the flight model to
	/// damp out afterwards.
	/// </summary>
	private void WingProbe(SimWorld world, in Transform3 frame, Vec3i offset, int rollAway) {
		var point = frame.TransformPoint(offset.X, offset.Y, offset.Z);
		int ground = world.GroundHeightAt(point);

		if (point.Z >= ground) {
			return;
		}

		_flight.RollRate = (short)(_flight.RollRate
			+ rollAway * SimMath.Q10Multiply(WingRollGain, ground - point.Z));
		Roll = (short)(Roll + _flight.RollRate);
		_rotationValid = false;
	}

	/// <summary>
	/// The nose and look-ahead probes, which differ only in where they sit and how hard they pull.
	/// Both clear a negative pitch rate before they add to it, so the nose comes up from wherever it
	/// was rather than having to overcome a dive first.
	/// </summary>
	private void PitchProbe(SimWorld world, in Transform3 frame, Vec3i offset, int gain) {
		var point = frame.TransformPoint(offset.X, offset.Y, offset.Z);
		int ground = world.GroundHeightAt(point);

		if (point.Z >= ground) {
			return;
		}

		if (_flight.PitchRate < 0) {
			_flight.PitchRate = 0;
		}

		_flight.PitchRate = (short)(_flight.PitchRate + SimMath.Q10Multiply(gain, ground - point.Z));
		Pitch = (short)(Pitch + _flight.PitchRate);
		_rotationValid = false;
	}

	/// <summary>
	/// The flyby loop — catalog id <c>0x31</c>, <c>flyby1.wav</c>. Started once when an aircraft with
	/// any airspeed comes within <see cref="FlybySoundRange"/> of the listener and released again
	/// when it leaves, through a latch on the object itself (<c>flyer+0x06</c>), so a pass sounds
	/// once per pass. <c>Sound_Play</c> is not positional: every flyer in range shares the one row.
	/// </summary>
	private void UpdateFlybySound(SimWorld world) {
		if (AirSpeed <= 0) {
			return;
		}

		var listener = world.ListenerPosition;
		int range = SimMath.FastMagnitude3D(
			Position.X - listener.X, Position.Y - listener.Y, Position.Z - listener.Z);

		if (range >= FlybySoundRange) {
			_flybySounding = false;
			return;
		}

		if (!_flybySounding) {
			world.Sounds?.Play(SoundId.FlyerLoop);
			_flybySounding = true;
		}
	}

	private bool _flybySounding;

	/// <summary>
	/// Silences the loop when the aircraft goes down — the original's <c>Sound_Stop(0x31)</c>,
	/// gated on the same latch so a flyer that was never in earshot does not stop somebody else's.
	/// </summary>
	private void StopFlybySound(SimWorld? world) {
		if (!_flybySounding) {
			return;
		}

		world?.Sounds?.Stop(SoundId.FlyerLoop);
		_flybySounding = false;
	}

	/// <summary>
	/// <c>Flyer_PitchCommand</c> (<c>00422098</c>) — the elevator command that holds a pitch attitude. The error is scaled,
	/// <b>resolved through the current bank</b> so a rolled aircraft asks its elevator for less, and
	/// damped by the pitch rate already under way.
	/// </summary>
	private int PitchCommand(short desiredPitch) {
		int error = SimMath.Q16Multiply(desiredPitch - Pitch, PitchGain);
		short banked = (short)SimMath.Q14Multiply(SimTrig.Cos(Roll), (short)error);
		return banked - SimMath.Q16Multiply(_flight.PitchRate, PitchRateDamping);
	}

	/// <summary>
	/// <c>Flyer_PitchToAltitude</c> (<c>00422108</c>) — the elevator command that flies the aircraft to a world altitude. The
	/// height error is turned into a pitch demand against a fixed <see cref="PitchAltitudeRun"/>
	/// horizontal run, so the demanded angle is a function of the error alone.
	///
	/// <para>Not reproduced: the original opens with a branch on the base object's <c>+0xae</c> that
	/// asks for the current pitch plus a figure from <c>flyer+0x23c</c> instead. The flyer pool is
	/// zero-filled and no flyer path that writes <c>+0xae</c> has been found, so the branch never runs
	/// (docs/retail/simulation/ai-flyers.md, "The control law" and Open).</para>
	/// </summary>
	private int PitchToAltitude(int altitude) =>
		PitchCommand((short)SimTrig.Atan2Guarded(altitude - Position.Z, PitchAltitudeRun));

	/// <summary>
	/// The five-element command array the flyer's control law fills and hands to the flight model —
	/// the original's own <c>int[5]</c> on <c>Flyer_SteerAndFly</c> (<c>004222fc</c>)'s stack. The throttle element is never
	/// written; see <see cref="InitialThrottle"/>.
	/// </summary>
	private struct FlightCommand {
		public int Aileron;
		public int Elevator;
		public int Rudder;
		public int GroundHeight;

		/// <summary>Whether the model reads the throttle element as a lever; see <see cref="ApplyFlightCommand"/>.</summary>
		public bool AnalogueThrottle;
	}

	/// <summary>
	/// <c>Flyer_CommandRoll</c> (<c>0042215c</c>) — roll toward a bank angle and fly. The aileron is the bank error scaled
	/// and damped by the roll rate, and this is the arm the controller takes whenever it is holding
	/// or limiting a bank rather than commanding one outright.
	/// </summary>
	private void CommandRoll(short desiredRoll, ref FlightCommand command) {
		command.Aileron = SimMath.Q16Multiply(desiredRoll - Roll, RollGain)
			+ SimMath.Q16Multiply(_flight.RollRate, RollRateDamping);
		ApplyFlightCommand(ref command);
	}

	/// <summary>
	/// <c>Flyer_ApplyFlightCommand</c> (<c>004221a8</c>) — the hand-off into the flight model. The two stick axes are
	/// <b>squared</b>, sign preserved, before they are clamped to the ±0x100 the model reads: small
	/// commands are softened quadratically and only a large one reaches full deflection, which is
	/// what keeps an AI aircraft from sawing its controls back and forth.
	///
	/// <para>The throttle axis is passed as zero. The model's lever test is the player's
	/// (<see cref="SimWorld.FlightThrottleIsLever"/>): when it holds, the zero axis is read as a lever at
	/// its centre and the throttle set to 0, so a SKIMMER cruises at 750 rather than the 875 that
	/// <see cref="InitialThrottle"/> asks for. <see cref="TweakSettingDefinitions.FlyerIgnoresPlayerThrottle"/>
	/// refuses the lever branch, leaving 875 whatever the player's controls
	/// (docs/retail/simulation/ai-flyers.md, "A flyer cannot change speed").</para>
	/// </summary>
	private void ApplyFlightCommand(ref FlightCommand command) {
		if (Flight is not { } flight) {
			return;
		}

		FlightPhysics.Step(this, ref _flight, flight,
			aileron: (short)SquareCommand(command.Aileron),
			elevator: (short)SquareCommand(command.Elevator),
			rudder: (short)command.Rudder,
			throttleAxis: 0,
			groundHeight: command.GroundHeight,
			analogueThrottle: command.AnalogueThrottle);
	}

	/// <summary>Squares a command with its sign kept, then clamps it to the model's ±0x100 axis range.</summary>
	private static int SquareCommand(int value) {
		int squared = (value * value) >> 8;
		if (value < 0) {
			squared = -squared;
		}

		return squared >= 0x100 ? 0x100 : squared < -0xff ? -0x100 : squared;
	}

	/// <summary>
	/// <c>Flyer_SteerAndFly</c> (<c>004222fc</c>) — the flyer's whole steering channel, and the one function every
	/// movement step ends in. It turns a heading error into a bank, decides whether the aircraft may hold that
	/// bank, and runs the flight model.
	///
	/// <list type="number">
	/// <item>The <b>bank command</b> is the steering error, the heading rate the current bank is
	/// already producing, and the roll rate's damping, summed as a 16-bit accumulator.</item>
	/// <item>Past the chassis' <see cref="HercWorks.Core.Data.File.Dat.Sim.FlyerSimData.MaxBankAngle"/>
	/// (less a hysteresis band) the aircraft is <b>held at the limit</b> instead, and only in the
	/// direction that would take it further over.</item>
	/// <item>A <b>small</b> steering error is answered with rudder and level wings rather than a bank
	/// at all — and the rudder is refused while the roll is <see cref="RudderRollLimit"/> or more
	/// (a signed test), so the wings come level first.</item>
	/// <item>Anything larger is flown as a bank.</item>
	/// </list>
	///
	/// <para>The original also has a leader term in the bank command, scaled by the leader's
	/// <c>+0x1f8</c>. The flyer pool is zero-filled and no writer of that field is known
	/// (docs/retail/simulation/ai-flyers.md, Open), so the term contributes nothing and the branch is not
	/// reproduced.</para>
	/// </summary>
	private void SteerAndFly(SimWorld world, short turn, int elevator) {
		short maxBank = MaxBankAngle;
		short roll = Roll;

		short bank = (short)SimMath.Q16Multiply(-turn, TurnToBankGain);
		bank += (short)SimMath.Q16Multiply(_flight.BankTurnRate, BankRateGain);
		bank += (short)SimMath.Q16Multiply(_flight.RollRate, RollRateDamping);

		var command = new FlightCommand {
			Elevator = elevator,
			GroundHeight = world.GroundHeightAt(Position),
			AnalogueThrottle = world.FlightThrottleIsLever
				&& !world.Tweaks.GetSettingValue(TweakSettingDefinitions.FlyerIgnoresPlayerThrottle)
		};

		if (roll >= maxBank - BankHysteresis && bank > 0) {
			CommandRoll(maxBank, ref command);
			return;
		}

		if (roll <= -(maxBank - BankHysteresis) && bank < 0) {
			CommandRoll((short)-maxBank, ref command);
			return;
		}

		int magnitude = turn == short.MinValue ? short.MaxValue : System.Math.Abs((int)turn);

		if (magnitude < RudderTurnLimit) {
			if (Roll < RudderRollLimit) {
				command.Rudder = SimMath.Q16Multiply(-turn, RudderGain);
			}

			CommandRoll(0, ref command);
			return;
		}

		command.Aileron = bank;
		command.Rudder = 0;
		ApplyFlightCommand(ref command);
	}

	/// <summary>
	/// The chassis' bank limit out of its <c>dat\&lt;NAME&gt;.DAT</c> (type record <c>+0x0e</c>).
	/// Zero for a type the install ships no record for, which leaves the controller holding the wings
	/// level — the safe reading, since a zero limit means every bank command is refused.
	/// </summary>
	private short MaxBankAngle => SimData?.MaxBankAngle ?? 0;
}
