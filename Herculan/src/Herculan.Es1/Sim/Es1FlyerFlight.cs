using Herculan.Engine.Numerics;
using Herculan.Engine.Settings;
using Herculan.Es1.Ai;
using Herculan.Es1.Settings;

namespace Herculan.Es1.Sim;

/// <summary>
/// How a flyer flies (docs/es1/simulation/flyers.md#flying): no flight model, a step along the nose at the type's speed
/// each tick, three turn rates added to the attitude, and the steps below that set the rates and the pitch. The type
/// record's fields are <see cref="Es1FlyerType.Data"/>, whose names are ES2's; each use says what ES1's code reads the
/// field as (docs/es1/formats/bullets-skimmer-dat.md#datskimmerdat). Every step is per tick, not scaled by the tick
/// length, as retail's are, but for the step along the nose under <see cref="Es2Speed"/>.
/// </summary>
public static class Es1FlyerFlight {
	/// <summary><c>Flyer_PitchToAltitude</c>'s run against which the height error makes the pitch.</summary>
	public const int AltitudeRun = 6000;

	/// <summary>The look-ahead's length along the nose, and the height a ground warning's figure is a fraction of.</summary>
	public const int LookAhead = 15000;

	/// <summary>The height the look-ahead treats as the ground: both of its ends are lowered by it.</summary>
	public const int GroundClearance = 500;

	/// <summary>The ground warning's countdown reload, about 0.6 seconds.</summary>
	public const short GroundWarningHold = 1500;

	/// <summary>
	/// Whether a flyer's per-tick steps count 40 ms of the tick rather than a tick, from
	/// <see cref="Es1TweakSettingDefinitions.Es2FlyerSpeed"/>: each is × <c>SimTickDelta</c> / 102.4, the tick delta of
	/// 40 ms, so a flyer flies and turns at any frame rate as it would at Earthsiege 2's 25 Hz tick, whose flyer steps are
	/// also per tick (docs/retail/simulation/ai-flyers.md#the-move--flyer_movementtick-004218c4). The steps are the move
	/// along the nose, the three rates added to the attitude, <see cref="PitchCommand"/>'s slew limit and
	/// <see cref="ApplyFlightCommand"/>'s roll-rate step. A <c>SKIMMER</c>'s 1000 then makes 25,000 units (150 m) a second,
	/// against Earthsiege 2's <c>SKIMMER</c> cruising at 875 a tick, 21,875 units (131 m); retail's steps 1000 each tick,
	/// 40,000 units a second at the 25 ms tick of <see cref="Es1SimClock"/> and 60,000 at retail's 60 frames a second. The
	/// stored values keep retail's: the forward speed <c>+0x1c9</c>, read for the rocket's launch speed and the formation
	/// throttle, and the rates, read by <see cref="SteerAndFly"/>. Each scaled step is rounded, losing up to half a unit a
	/// tick. This engine's invention; off, retail's steps.
	/// </summary>
	public static bool Es2Speed => TweakSettings.Current.GetSettingValue(Es1TweakSettingDefinitions.Es2FlyerSpeed);

	/// <summary><c>SimTickDelta</c> for 40 ms, Earthsiege 2's tick at its 25 Hz cap: 0.4 of the Q8 100 ms unit, ×10 to keep it whole.</summary>
	private const int StepTickDelta10 = 1024;

	/// <summary>
	/// A per-tick step this tick: <paramref name="step"/>, or with <see cref="Es2Speed"/> the step × <c>SimTickDelta</c>
	/// × 10 / <see cref="StepTickDelta10"/>, rounded away from 0, so a step that is not 0 stays so.
	/// </summary>
	private static short PerTick(short step, Es1SimClock clock) =>
		Es2Speed ? (short)Math.Round(step * clock.TickDelta * 10.0 / StepTickDelta10, MidpointRounding.AwayFromZero) : step;

	/// <summary><c>Flyer_ApplyFlightCommand</c>'s roll command limit, either side.</summary>
	public const short FullRollCommand = 0x100;

	/// <summary>The band inside the bank limit from which <see cref="SteerAndFly"/> holds the roll at the limit.</summary>
	public const int BankBand = 500;

	/// <summary><see cref="SteerAndFly"/>'s Q10 gains on the heading rate and on the leader's heading rate.</summary>
	public const short HeadingRateGain = 1100, LeaderRateGain = 200;

	/// <summary>
	/// <c>Flyer_PitchCommand</c> (<c>ES1DBSIM 0001b6c0</c>)(<paramref name="flyer"/>, <paramref name="pitch"/>):
	/// <c>Math_RateLimitedMoveToward</c> moves the pitch <c>+6</c> toward <paramref name="pitch"/> by at most the record's
	/// <c>+0</c> (<see cref="HercWorks.Core.Data.File.Dat.Sim.Es1FlyerSimData.SpeedTurn"/>), and <c>+0x2c</c> is cleared.
	/// </summary>
	public static void PitchCommand(Es1Flyer flyer, short pitch, Es1SimClock clock) {
		var rotation = flyer.Rotation;
		short current = rotation.X;
		SimMath.RateLimitedMoveToward(ref current, pitch, PerTick(flyer.Type.Data.SpeedTurn, clock));
		flyer.Rotation = (current, rotation.Y, rotation.Z);
		flyer.RotationWord = 0;
	}

	/// <summary>
	/// <c>Flyer_PitchToAltitude</c> (<c>0001b708</c>)(<paramref name="flyer"/>, <paramref name="altitude"/>): with the
	/// ground warning set the altitude is <c>Math_Q8Multiply</c>(<see cref="LookAhead"/>, the warning's figure); then
	/// <see cref="PitchCommand"/> with <c>Math_Atan2Guarded</c> of the height error over <see cref="AltitudeRun"/>.
	/// </summary>
	public static void PitchToAltitude(Es1Flyer flyer, int altitude, Es1SimClock clock) {
		if (flyer.GroundWarning) {
			altitude = SimMath.Q8MultiplyRounded(LookAhead, flyer.GroundWarningFigure);
		}

		PitchCommand(flyer, (short)SimTrig.Atan2Guarded(altitude - flyer.Position.Z, AltitudeRun), clock);
	}

	/// <summary>
	/// <c>Flyer_ApplyFlightCommand</c> (<c>0001b794</c>)(<paramref name="flyer"/>, <paramref name="pitchCommand"/>,
	/// <paramref name="rollCommand"/>): the roll command held to ±<see cref="FullRollCommand"/>; the roll rate
	/// <c>+0x18c</c> moved toward <c>Math_Q8Multiply</c>(the command, the record's <c>+2</c>,
	/// <see cref="HercWorks.Core.Data.File.Dat.Sim.Es1FlyerSimData.SpeedReverse"/>) by at most the record's <c>+8</c>
	/// (<see cref="HercWorks.Core.Data.File.Dat.Sim.Es1FlyerSimData.TurnAccelDecel"/>); the heading rate <c>+0x18e</c> the
	/// negated roll shifted right by the record's <c>+0xa</c>
	/// (<see cref="HercWorks.Core.Data.File.Dat.Sim.Es1FlyerSimData.CameraPartId"/>), taken modulo 32 as <c>SAR</c> takes
	/// it; then <see cref="PitchCommand"/> with the pitch plus 16 × the pitch command, as an <c>int16</c>.
	/// </summary>
	public static void ApplyFlightCommand(Es1Flyer flyer, short pitchCommand, short rollCommand, Es1SimClock clock) {
		var data = flyer.Type.Data;
		short roll = Math.Clamp(rollCommand, (short)-FullRollCommand, FullRollCommand);
		var rates = flyer.Rates;
		short rollRate = rates.Roll;
		SimMath.RateLimitedMoveToward(ref rollRate, (short)SimMath.Q8MultiplyRounded(roll, data.SpeedReverse), PerTick(data.TurnAccelDecel, clock));
		short headingRate = (short)(-flyer.Rotation.Y >> (data.CameraPartId & 0x1f));
		flyer.Rates = (rates.Pitch, rollRate, headingRate);
		PitchCommand(flyer, (short)((pitchCommand << 4) + flyer.Rotation.X), clock);
	}

	/// <summary>
	/// <c>Flyer_CommandRoll</c> (<c>0001b76c</c>)(<paramref name="flyer"/>, <paramref name="roll"/>):
	/// <see cref="ApplyFlightCommand"/>(flyer, 0, (roll − the roll <c>+8</c>) &gt;&gt; 4).
	/// </summary>
	public static void CommandRoll(Es1Flyer flyer, short roll, Es1SimClock clock) =>
		ApplyFlightCommand(flyer, 0, (short)((roll - flyer.Rotation.Y) >> 4), clock);

	/// <summary>
	/// <c>Flyer_SteerAndFly</c> (<c>0001b8c4</c>)(<paramref name="flyer"/>, <paramref name="leader"/>,
	/// <paramref name="steering"/>): the command −steering &gt;&gt; 4 plus Q10(<see cref="HeadingRateGain"/>, the heading
	/// rate), the rate first less, with a leader, Q10(<see cref="LeaderRateGain"/>, Q14(the leader's heading rate,
	/// cos(heading − the leader's heading))), each sum an <c>int16</c>. With the roll at least the bank limit (the record's
	/// <c>+0xe</c>) less <see cref="BankBand"/> and the command positive, <see cref="CommandRoll"/> to the limit; with the
	/// roll at most its negation and the command negative, to the negated limit; otherwise
	/// <see cref="ApplyFlightCommand"/>(flyer, 0, command). The multiplies are ES1's rounding ones.
	/// </summary>
	public static void SteerAndFly(Es1Flyer flyer, Es1Flyer? leader, short steering, Es1SimClock clock) {
		short limit = flyer.Type.Data.MaxBankAngle;
		short roll = flyer.Rotation.Y;
		short rate = flyer.Rates.Heading;
		short command = (short)(-steering >> 4);
		if (leader != null) {
			short cosine = SimTrig.Cos((short)(flyer.Heading - leader.Heading));
			short leaderTerm = (short)SimMath.Q14MultiplyRounded(leader.Rates.Heading, cosine);
			rate = (short)(rate - SimMath.Q10MultiplyRounded(LeaderRateGain, leaderTerm));
		}

		command = (short)(command + SimMath.Q10MultiplyRounded(HeadingRateGain, rate));
		if (limit - BankBand <= roll && command > 0) {
			CommandRoll(flyer, limit, clock);
		} else if (roll <= -(limit - BankBand) && command < 0) {
			CommandRoll(flyer, (short)-limit, clock);
		} else {
			ApplyFlightCommand(flyer, 0, command, clock);
		}
	}

	/// <summary>
	/// <c>Flyer_MovementTick</c> (<c>0001b084</c>), the move of the states that have one
	/// (docs/es1/simulation/flyers.md#the-move--flyer_movementtick-0001b084):
	/// <list type="number">
	/// <item>the forward speed <c>+0x1c9</c> reloaded from the record's <c>+4</c>, and the position the flyer's frame
	/// applied to the velocity (<c>Transform_ApplyToShortPoint</c>), a step along the nose;</item>
	/// <item>the three rates added to the pitch, roll and heading, and <c>+0x2c</c> cleared;</item>
	/// <item>the look-ahead: the ray from the position to the point <see cref="LookAhead"/> along the nose of the rebuilt
	/// frame, both ends lowered by <see cref="GroundClearance"/>, its distance <i>d</i> 0 to start. With the end below 0,
	/// <i>d</i> is 1 when the start is too, otherwise (the start's Z &lt;&lt; 14) / <c>Math_CosBam</c>(−<i>e</i> −
	/// <c>0x4000</c>) for <i>e</i> the elevation from start to end, when −<i>e</i> is not 0. A hit by
	/// <c>Sim_RaycastShapes</c>(0, end, start) or by <see cref="Es1ObstacleAvoidance.StructureRaycastShapes"/>(−1, end,
	/// start) replaces <i>d</i> when nearer than it;</item>
	/// <item>the warning: <c>Math_CountdownTimerTick</c> of its countdown; with it at 0 and <i>d</i> 0 the warning cleared;
	/// otherwise set, and with <i>d</i> not 0 the figure (<see cref="LookAhead"/> − <i>d</i>) × 256 /
	/// <see cref="LookAhead"/> and the countdown <see cref="GroundWarningHold"/>.</item>
	/// </list>
	/// Answers 1. Both ends of the ray the shapes are tested on lie <see cref="GroundClearance"/> below the flyer's path,
	/// as retail lowers the points in place before it passes them.
	/// </summary>
	public static short MovementTick(Es1Flyer flyer, Es1SimGlobals globals) {
		var velocity = flyer.Velocity;
		velocity.Y = flyer.Type.Data.SpeedForward;
		flyer.Velocity = velocity;
		flyer.Position = flyer.Frame.TransformPoint(velocity.X, PerTick(flyer.Type.Data.SpeedForward, globals.Clock), velocity.Z);

		var (pitch, roll, heading) = flyer.Rotation;
		var rates = flyer.Rates;
		var clock = globals.Clock;
		flyer.Rotation = ((short)(pitch + PerTick(rates.Pitch, clock)), (short)(roll + PerTick(rates.Roll, clock)), (short)(heading + PerTick(rates.Heading, clock)));
		flyer.RotationWord = 0;

		var start = flyer.Position;
		var end = flyer.Frame.TransformPoint(0, LookAhead, 0);
		start = new Vec3i(start.X, start.Y, start.Z - GroundClearance);
		end = new Vec3i(end.X, end.Y, end.Z - GroundClearance);
		int distance = 0;
		if (end.Z < 0) {
			if (start.Z < 0) {
				distance = 1;
			} else {
				short dip = (short)-Es1ShotSweep.ElevationAngle(start, end);
				if (dip != 0) {
					// Math_CosBam answers 0 for a dip of 1 to 7, which retail divides by; here the distance stays 0.
					short cosine = SimTrig.Cos(unchecked((short)(dip - 0x4000)));
					if (cosine != 0) {
						distance = unchecked(start.Z << 14) / cosine;
					}
				}
			}
		}

		if (globals.ZoneShapes.RaycastShapes(0, end, start, out var hit) && hit.Distance < distance) {
			distance = hit.Distance;
		}

		if (Es1ObstacleAvoidance.StructureRaycastShapes(-1, end, start, globals, out int structureDistance) && structureDistance < distance) {
			distance = structureDistance;
		}

		short countdown = flyer.GroundWarningCountdown;
		globals.Clock.CountdownTimerTick(ref countdown);
		flyer.GroundWarningCountdown = countdown;
		if (countdown == 0 && distance == 0) {
			flyer.GroundWarning = false;
		} else {
			flyer.GroundWarning = true;
			if (distance != 0) {
				flyer.GroundWarningFigure = (short)((LookAhead - distance) * 0x100 / LookAhead);
				flyer.GroundWarningCountdown = GroundWarningHold;
			}
		}

		return 1;
	}
}
