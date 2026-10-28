using Herculan.Engine.Numerics;
using Herculan.Es1.Sim;

namespace Herculan.Es1.Ai;

/// <summary>
/// <c>Mech_AiObstacleAvoidance</c> (<c>ES1DBSIM 00011c80</c>), which <c>Mech_LocomotionTick</c> runs for a machine other
/// than the player's to bend the steer it was handed away from what lies ahead
/// (docs/es1/simulation/mech-locomotion.md#obstacle-avoidance--mech_aiobstacleavoidance-00011c80, which links ES2's
/// derivation).
/// </summary>
public static class Es1ObstacleAvoidance {
	/// <summary>Each side's figure for nothing there, and the range from which the steer grows.</summary>
	public const int ClearRange = 22000;

	/// <summary>The steer's gain at contact.</summary>
	public const int Gain = 2000;

	/// <summary>The Q10 scale of another object's distance, near enough twice it.</summary>
	public const short ObjectRangeScale = 2000;

	/// <summary>The half-arc, either side of the heading, inside which another object counts.</summary>
	public const int ObjectArc = 0x2000;

	/// <summary>The bit of the flags argument that skips the probes and the objects.</summary>
	public const short SkipBit = 1;

	/// <summary>The two probes' ends in the machine's frame, the left's X negated: (∓<see cref="ProbeStartX"/>, 0, 0) to (∓<see cref="ProbeEndX"/>, <see cref="ProbeLength"/>, 0).</summary>
	public const int ProbeStartX = 1500, ProbeEndX = 10000, ProbeLength = 20000;

	/// <summary>
	/// The avoidance on <paramref name="desired"/> and <paramref name="steer"/>. It runs when the machine moves or is asked
	/// to, and asked backwards only while the unstick countdown runs. With <paramref name="flags"/>' <see cref="SkipBit"/>
	/// clear: each probe's nearest hit (<see cref="Probe"/>) is its side's figure, then each live object but this one whose
	/// group holds no action and whose body radius (slot <c>+0x60</c>) is not 0, at Q10(<c>Math_DistanceBetweenPoints</c>,
	/// <see cref="ObjectRangeScale"/>) and inside <see cref="ObjectArc"/> of the heading, lowers the right figure for a
	/// negative bearing and the left otherwise, each only to a smaller value. With either figure under
	/// <see cref="ClearRange"/>: the unstick side <c>+0x1e8</c> added to the left when <c>+0xa8</c> is set and both are under
	/// it; the steer gains (<see cref="ClearRange"/> − nearer) × <see cref="Gain"/> / <see cref="ClearRange"/>, negated when the
	/// left is nearer or equal; and the desired speed becomes 0 when the nearer is negative.
	/// </summary>
	public static void Avoid(Es1Mech mech, short flags, ref short desired, ref short steer, Es1SimGlobals globals) {
		if ((mech.Speed == 0 && desired == 0) || (desired < 0 && mech.UnstickCountdown == 0)) {
			return;
		}

		int left = ClearRange, right = ClearRange;
		if ((flags & SkipBit) == 0) {
			left = Probe(mech, -ProbeStartX, -ProbeEndX, left, globals);
			right = Probe(mech, ProbeStartX, ProbeEndX, right, globals);
		}

		if ((flags & SkipBit) == 0) {
			var position = mech.Position;
			foreach (var other in globals.LiveObjects) {
				// Retail reads the group's action through +0x3f unchecked; an object with no group is taken as holding none.
				if (ReferenceEquals(other, mech) || other.Group is { Action: not null } || other.BodyRadius == 0) {
					continue;
				}

				var at = other.Position;
				int range = SimMath.Q10MultiplyRounded(
					SimMath.FastMagnitude3D(at.X - position.X, at.Y - position.Y, at.Z - position.Z), ObjectRangeScale);
				if (range >= left && range >= right) {
					continue;
				}

				short bearing = (short)(Es1MissionGroup.HeadingToward(position, at) - mech.Heading);
				int magnitude = bearing == short.MinValue ? short.MaxValue : Math.Abs((int)bearing);
				if (magnitude >= ObjectArc) {
					continue;
				}

				if (bearing < 0) {
					right = Math.Min(right, range);
				} else {
					left = Math.Min(left, range);
				}
			}
		}

		if (left >= ClearRange && right >= ClearRange) {
			return;
		}

		if (mech.UnstickReverse && left < ClearRange && right < ClearRange) {
			left += mech.UnstickSide;
		}

		int nearer = left > right ? right : left;
		short turn = (short)((ClearRange - nearer) * Gain / ClearRange);
		if (left <= right) {
			turn = (short)-turn;
		}

		steer = (short)(steer + turn);
		if (nearer < 0) {
			desired = 0;
		}
	}

	/// <summary>
	/// One probe, from (<paramref name="startX"/>, 0, 0) to (<paramref name="endX"/>, <see cref="ProbeLength"/>, 0) taken
	/// through the machine's frame: <c>Sim_RaycastShapes</c> (<c>00020b70</c>)(0, the end, the start, result) against the
	/// zone's shapes, its distance replacing <paramref name="figure"/> on a hit, then <see cref="StructureRaycastShapes"/>,
	/// its distance kept when nearer.
	/// </summary>
	private static int Probe(Es1Mech mech, int startX, int endX, int figure, Es1SimGlobals globals) {
		var frame = mech.Frame;
		var from = frame.TransformPoint(startX, 0, 0);
		var to = frame.TransformPoint(endX, ProbeLength, 0);
		if (globals.ZoneShapes.RaycastShapes(0, to, from, out var hit)) {
			figure = hit.Distance;
		}

		if (StructureRaycastShapes(-1, to, from, globals, out int distance) && distance < figure) {
			figure = distance;
		}

		return figure;
	}

	/// <summary>
	/// <c>Structure_RaycastShapes</c> (<c>000038b8</c>)(<paramref name="side"/>, 0, <paramref name="to"/>,
	/// <paramref name="from"/>, result): the ray from <paramref name="from"/> to <paramref name="to"/>
	/// (<see cref="Es1Ray.Between"/>) against the structures of that side, or every side's for −1
	/// (<see cref="Es1ShapeTests.RaycastShapeList"/>, docs/es1/simulation/terrain.md#the-structures-tests), and whether it
	/// struck one, with the result's distance.
	/// </summary>
	internal static bool StructureRaycastShapes(short side, Vec3i to, Vec3i from, Es1SimGlobals globals, out int distance) {
		bool struck = Es1ShapeTests.RaycastShapeList(globals.Structures.Live, side, Es1Ray.Between(0, to, from), out var hit);
		distance = hit.Distance;
		return struck;
	}
}
