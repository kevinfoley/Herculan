using Herculan.Engine.Numerics;

namespace Herculan.Engine.Sim;

/// <summary>
/// Per-tick control input for <see cref="FlyCameraObject"/>. Each axis is -1, 0 or +1: the host
/// translates keys or a stick into these and the simulation decides what they mean, so no input
/// device's units leak into the sim.
/// </summary>
public struct CameraInput {
	/// <summary>Forward (+1) / backward (-1) along the camera's facing.</summary>
	public int Forward;

	/// <summary>Right (+1) / left (-1), perpendicular to facing, on the ground plane.</summary>
	public int Strafe;

	/// <summary>Up (+1) / down (-1) in world Z, independent of pitch.</summary>
	public int Vertical;

	/// <summary>Turn right (+1) / left (-1).</summary>
	public int Yaw;

	/// <summary>Look up (+1) / down (-1).</summary>
	public int Pitch;

	/// <summary>Multiplies the cruise speed, for crossing a ~10 km zone in reasonable time.</summary>
	public bool Boost;
}

/// <summary>
/// A free-flying observer camera, implemented as a <see cref="SimObject"/> so it moves through the
/// ported fixed-point math rather than around it — the explicit goal of the first milestone (see
/// docs/herculan/planning.md, "First milestone": <i>get a camera moving through it using the actual
/// ported physics/math</i>).
///
/// <para>Concretely, every part of its motion goes through the toolkit: speeds ramp toward their
/// target with <see cref="SimMath.RateLimitedMoveToward"/> — the same slew limiter the rocket's
/// turn-rate cap and the shield recharge use — a tick's displacement comes from
/// <see cref="SimMath.IntegrateRateOverTick"/>, and heading is applied with Q14 fixed-point trig
/// (<see cref="SimMath.Q14Multiply"/>) against a binary-angle facing. The one honest caveat is the
/// trig table itself, which is generated rather than recovered; see <see cref="BinaryAngle"/>.</para>
///
/// <para>This is an observer, not a flight model. It is not pretending to be the flyer physics from
/// <c>flyersys.cpp</c> — that is a real system with its own terrain-avoidance autopilot and belongs
/// to a later milestone.</para>
/// </summary>
public sealed class FlyCameraObject : SimObject {
	/// <summary>
	/// Cruise speed, as a rate in the Q8 timestep scale — about 36 m/s at
	/// <see cref="SimWorld.TicksPerSecond"/> and <see cref="Render.WorldScale.WorldUnitsPerMeter"/>.
	///
	/// <para>These five constants were all multiplied by 3.79 when the simulation timestep was
	/// corrected to the original's recovered 25 Hz / 81 (see <see cref="SimWorld.TickDelta"/>);
	/// the observer's real-world speeds are unchanged.</para>
	/// </summary>
	public short CruiseSpeed { get; set; } = 759;

	/// <summary>
	/// Cruise speed while boosting (~360 m/s), which crosses a retail zone's 12.6 km in a little
	/// over half a minute.
	/// </summary>
	public short BoostSpeed { get; set; } = 7586;

	/// <summary>How much a speed can change in one tick, i.e. the acceleration limit.</summary>
	public short SpeedStep { get; set; } = 228;

	/// <summary>Turn rate at full input, in binary angle units per tick.</summary>
	public short TurnRate { get; set; } = 2655;

	/// <summary>How much the turn rate can change in one tick.</summary>
	public short TurnStep { get; set; } = 531;

	/// <summary>
	/// Pitch limit, just short of straight up or down. Stopping short of a quarter turn keeps the
	/// view basis from degenerating when forward becomes parallel to the up axis.
	/// </summary>
	public int PitchLimit { get; set; } = BinaryAngle.QuarterTurn - 0x0200;

	/// <summary>Minimum clearance kept above the terrain, in world units (~5 m).</summary>
	public int GroundClearance { get; set; } = 1000;

	/// <summary>The control input to apply on the next tick; the host sets this each frame.</summary>
	public CameraInput Input { get; set; }

	private short _forwardSpeed;
	private short _strafeSpeed;
	private short _verticalSpeed;
	private short _yawRate;
	private short _pitchRate;

	/// <summary>An observer has no physical presence, so nothing should ever collide with it.</summary>
	public override int HitRadius => 0;

	public override void Tick(SimWorld world) {
		var input = Input;
		short cruise = input.Boost ? BoostSpeed : CruiseSpeed;

		SimMath.RateLimitedMoveToward(ref _forwardSpeed, (short)(Clamp(input.Forward) * cruise), SpeedStep);
		SimMath.RateLimitedMoveToward(ref _strafeSpeed, (short)(Clamp(input.Strafe) * cruise), SpeedStep);
		SimMath.RateLimitedMoveToward(ref _verticalSpeed, (short)(Clamp(input.Vertical) * cruise), SpeedStep);
		SimMath.RateLimitedMoveToward(ref _yawRate, (short)(Clamp(input.Yaw) * TurnRate), TurnStep);
		SimMath.RateLimitedMoveToward(ref _pitchRate, (short)(Clamp(input.Pitch) * TurnRate), TurnStep);

		Heading = (Heading + SimMath.IntegrateRateOverTick(_yawRate)) & 0xffff;

		int pitch = Pitch + SimMath.IntegrateRateOverTick(_pitchRate);
		Pitch = (short)(pitch > PitchLimit ? PitchLimit : pitch < -PitchLimit ? -PitchLimit : pitch);

		int forwardStep = SimMath.IntegrateRateOverTick(_forwardSpeed);
		int strafeStep = SimMath.IntegrateRateOverTick(_strafeSpeed);
		int verticalStep = SimMath.IntegrateRateOverTick(_verticalSpeed);

		short sinYaw = BinaryAngle.Sin(Heading);
		short cosYaw = BinaryAngle.Cos(Heading);
		short sinPitch = BinaryAngle.Sin(Pitch);
		short cosPitch = BinaryAngle.Cos(Pitch);

		// Heading 0 faces world +Y and increases toward +X, matching Camera.Forward.
		int horizontal = SimMath.Q14Multiply(forwardStep, cosPitch);
		int deltaX = SimMath.Q14Multiply(horizontal, sinYaw) + SimMath.Q14Multiply(strafeStep, cosYaw);
		int deltaY = SimMath.Q14Multiply(horizontal, cosYaw) - SimMath.Q14Multiply(strafeStep, sinYaw);
		int deltaZ = SimMath.Q14Multiply(forwardStep, sinPitch) + verticalStep;

		var moved = new Vec3i(Position.X + deltaX, Position.Y + deltaY, Position.Z + deltaZ);

		// Ride above the terrain rather than through it. This is the ported height query doing real
		// work every tick, which is also the cheapest continuous check that it behaves sanely across
		// the whole grid.
		int ground = world.GroundHeightAt(moved);
		if (moved.Z < ground + GroundClearance) {
			moved = new Vec3i(moved.X, moved.Y, ground + GroundClearance);
			if (_verticalSpeed < 0) {
				_verticalSpeed = 0;
			}
		}

		Position = moved;
	}

	private static int Clamp(int axis) => axis < 0 ? -1 : axis > 0 ? 1 : 0;
}
