using Herculan.Engine.Numerics;
using Herculan.Engine.Settings;

namespace Herculan.Engine.Sim;

/// <summary>
/// A structure's turret — the same mechanism a HERC's torso uses, and not a rotation: each axis'
/// angle is a <i>position</i> within a one-sweep animation sequence, seeked rather than played (see
/// <see cref="MechObject.TorsoTwistTick"/> and docs/retail/simulation/torso-aim.md). The two sequences are
/// the two <c>BaseType.AnimThreadCount</c> asks for, and the two threads the constructor builds for
/// them are what the seek drives.
/// </summary>
public sealed partial class BaseObject {
	/// <summary>
	/// Which shape part the turret is — the part id <c>Base_AimTurret</c> (<c>00403eec</c>) asks its
	/// shape for. On all four armed roots of <c>BASES_AN.DTS</c> it resolves to transform 2, the
	/// barrel node hanging off the traversing base at transform 1.
	/// </summary>
	public const int TurretPartId = 2;

	/// <summary>
	/// <c>base+0x209</c> — each axis' angular rate, what the demand is rate-limited into. Axis 0 is
	/// the elevation, axis 1 the traverse.
	/// </summary>
	private readonly short[] _turretRate = new short[TurretAxes];

	/// <summary><c>base+0x20d</c> — each axis' accumulated angle, the position seeked in its sequence.</summary>
	private readonly short[] _turretAngle = new short[TurretAxes];

	/// <summary>Elevation and traverse, in the index order the original's loop uses.</summary>
	private const int TurretAxes = 2;

	/// <summary>The turret's current angle on one axis, for anything that wants to draw or report it.</summary>
	public short TurretAngle(int axis) =>
		axis >= 0 && axis < TurretAxes ? _turretAngle[axis] : (short)0;

	/// <summary>
	/// <c>Base_AimTurret</c> (<c>00403eec</c>) — point the turret at a world point and hand back what
	/// is left of the error.
	///
	/// <para>The point is brought into the turret's own frame by inverting the structure's world
	/// transform and then the turret node's, which is the original's own two-step: the node transform
	/// is in shape space, so the object's has to come off first. <see cref="SimTrig.EulerToward"/> on
	/// the result is the aim error, and <see cref="SeekTurret"/> is what acts on it.</para>
	/// </summary>
	/// <returns>Elevation and traverse error, in binary angle — the original's static pair.</returns>
	public (short Elevation, short Traverse) AimTurret(SimWorld world, Vec3i point) {
		int node = Animation?.TransformIdOfPart(TurretPartId) ?? -1;
		var turret = node >= 0 ? NodeTransform(node) : Transform3.Identity;

		var inWorld = WorldFrame.Inverted();
		var inTurret = turret.Inverted();
		var onObject = inWorld.TransformPoint(point.X, point.Y, point.Z);
		var local = inTurret.TransformPoint(onObject.X, onObject.Y, onObject.Z);
		var (elevation, _, traverse) = SimTrig.EulerToward(local, default);

		SeekTurret(world, elevation, traverse);
		return (elevation, traverse);
	}

	/// <summary>
	/// <c>00403d5c</c> — one tick of both turret axes toward an error, then one step of the shape's
	/// animation.
	///
	/// <para>Per axis: the error over eight, clamped to one stick's travel; squared through
	/// <c>Q8(|in|, in)</c> so the turret runs hard while it is far off and eases as it arrives, and
	/// scaled by that axis' gain; rate-limited into the axis' rate; and the rate accumulated into the
	/// angle, clamped. The angle then <b>seeks</b> its thread — <c>(unsigned)angle >> 2</c>, the same
	/// Q14 sequence position the HERC turret uses, so a full turn spans the sequence exactly
	/// once.</para>
	///
	/// <para>The traverse axis' clamps are the full <c>short</c> range, which is no limit at all in
	/// binary angle: a base turret traverses freely and only the elevation is stopped, at
	/// <see cref="TurretClampUpper"/>'s ±4000 — a little over 20°.</para>
	/// </summary>
	private void SeekTurret(SimWorld world, short elevation, short traverse) {
		// The traverse is the negated one, not the elevation: axis 0 takes EulerToward's pitch
		// straight and axis 1 takes its yaw negated.
		var demand = new[] { ClampAxis(elevation >> 3), (short)-ClampAxis(traverse >> 3) };

		for (int axis = 0; axis < TurretAxes; axis++) {
			short input = demand[axis];
			int step = SimMath.Q8Multiply(
				SimMath.Q8Multiply(SaturatingAbs(input), input), TurretGain[axis]);

			short rate = _turretRate[axis];
			SimMath.RateLimitedMoveToward(ref rate, (short)step, TurretRateLimit[axis]);
			_turretRate[axis] = rate;

			int moved = (short)(_turretAngle[axis] + rate);
			_turretAngle[axis] = moved >= TurretClampUpper[axis] ? TurretClampUpper[axis]
				: moved <= TurretClampLower[axis] ? TurretClampLower[axis]
				: (short)moved;

			_threads[axis]?.SeekToPosition(axis, (short)((ushort)_turretAngle[axis] >> 2),
				world.Tweaks.GetSettingValue(TweakSettingDefinitions.SmootherTurretMovement));
		}
	}

	/// <summary>Q8 gain each axis' squared demand is scaled by — the pair at <c>004973e4</c>.</summary>
	private static readonly short[] TurretGain = { 2000, 2500 };

	/// <summary>How far each axis' rate may move in one tick — the pair at <c>004973e8</c>.</summary>
	private static readonly short[] TurretRateLimit = { 200, 800 };

	/// <summary>Each axis' lower stop — the pair at <c>004973ec</c>.</summary>
	private static readonly short[] TurretClampLower = { -4000, short.MinValue };

	/// <summary>Each axis' upper stop — the pair at <c>004973f0</c>.</summary>
	private static readonly short[] TurretClampUpper = { 4000, short.MaxValue };

	/// <summary>The original's own <c>±0x100</c> clamp on the demand, one stick's travel.</summary>
	private static short ClampAxis(int value) =>
		value >= MechControls.AxisFull ? MechControls.AxisFull
		: value < -MechControls.AxisFull + 1 ? (short)-MechControls.AxisFull
		: (short)value;

	/// <summary>
	/// <c>|x|</c> as the original computes it, saturating rather than wrapping — negating
	/// <see cref="short.MinValue"/> cannot be represented and it yields <see cref="short.MaxValue"/>.
	/// </summary>
	private static short SaturatingAbs(short value) =>
		value == short.MinValue ? short.MaxValue : value < 0 ? (short)-value : value;
}
