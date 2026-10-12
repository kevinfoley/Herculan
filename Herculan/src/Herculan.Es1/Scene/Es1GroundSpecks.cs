using HercWorks.Core.Data.File.Dbsim;
using Herculan.Engine.Numerics;

namespace Herculan.Es1.Scene;

/// <summary>
/// The world file's scattered points, the specks <c>PointScatter_Update</c> (<c>ES1DBSIM 00026e8c</c>) and
/// <c>PointScatter_Draw</c> (<c>00026fe0</c>) dot the ground with round the viewer while the ground is banded
/// (docs/es1/rendering/ground-and-sky.md#the-scattered-points). This holds the points and moves them; the backdrop
/// plots them.
/// </summary>
public sealed class Es1GroundSpecks {
	/// <summary><c>000956be</c>: the preferences value at and above which no speck is drawn.</summary>
	public const int DetailLimit = 240;

	/// <summary>How far from the viewer a point is carried over to the far side, by <c>Math_FastMagnitude2D</c>.</summary>
	public const int CarryDistance = 70000;

	/// <summary>Nearer than this a speck is two pixels wide.</summary>
	public const int PairDistance = 30000;

	/// <summary>Nearer than this a speck is a five-pixel cluster.</summary>
	public const int ClusterDistance = 20000;

	private readonly Vec3i[] _points;
	private readonly Speck[] _drawn;
	private Vec3i _centre;
	private bool _scattered;

	private Es1GroundSpecks(int count, int colour) {
		_points = new Vec3i[count];
		_drawn = new Speck[count];
		Colour = colour;
	}

	/// <summary>The specks' palette index, the world file's header short 8; the cluster adds this less 2 and less 4.</summary>
	public int Colour { get; }

	/// <summary>The world file's points, or null when its count is 0 and the load builds none.</summary>
	public static Es1GroundSpecks? From(Es1WorldData world) =>
		world.PointCount > 0 ? new Es1GroundSpecks(world.PointCount, world.PointColor) : null;

	/// <summary>
	/// <c>PointScatter_Update</c> for a frame: scatters the points round <paramref name="camera"/> the first time, and
	/// while <paramref name="detail"/> is below <see cref="DetailLimit"/> carries the first count × detail / 240 of them
	/// over as <c>PointScatter_Draw</c> does and returns them; otherwise returns none and leaves them where they are.
	/// </summary>
	public ReadOnlySpan<Speck> Update(Vec3i camera, int detail, Random random) {
		if (!_scattered) {
			Scatter(camera, random);
			_scattered = true;
		}

		if (detail >= DetailLimit) {
			return [];
		}

		int drawn = _points.Length * detail / DetailLimit;
		bool moved = camera != _centre;
		int midX = (_centre.X + camera.X) >> 1;
		int midY = (_centre.Y + camera.Y) >> 1;
		for (int i = 0; i < drawn; i++) {
			var point = _points[i];
			int distance = SimMath.FastMagnitude2D(point.X - camera.X, point.Y - camera.Y);
			if (moved && distance > CarryDistance) {
				point = _points[i] = new Vec3i(2 * midX - point.X, 2 * midY - point.Y, point.Z);
			}

			// The size is chosen by the distance measured before the carry.
			_drawn[i] = new Speck(point, distance);
		}

		_centre = camera;
		return _drawn.AsSpan(0, drawn);
	}

	/// <summary>
	/// <c>PointScatter_Scatter</c> (<c>00026ee0</c>): each point at a random bearing and a signed random distance 2r
	/// from the centre, on the ground.
	///
	/// <para>The viewer runs no simulation, so its draws are not <c>Math_RandomNext</c>'s stream at that moment of a
	/// mission: each is a uniform 16-bit value as that generator's is.</para>
	/// </summary>
	private void Scatter(Vec3i centre, Random random) {
		_centre = centre;
		for (int i = 0; i < _points.Length; i++) {
			short angle = unchecked((short)random.Next(0x10000));
			int twiceRadius = unchecked((short)random.Next(0x10000)) * 2;
			_points[i] = new Vec3i(
				centre.X + Es1Math.Q14Multiply(twiceRadius, SimTrig.Cos(angle)),
				centre.Y + Es1Math.Q14Multiply(twiceRadius, SimTrig.Cos(unchecked((short)(angle - BinaryAngle.QuarterTurn)))),
				0);
		}
	}
}

/// <summary>A point to plot this frame and its distance from the viewer, which sets its size.</summary>
public readonly record struct Speck(Vec3i Point, int Distance);
