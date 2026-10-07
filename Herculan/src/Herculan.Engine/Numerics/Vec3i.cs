namespace Herculan.Engine.Numerics;

/// <summary>
/// A position or offset in DBSIM world units — plain 32-bit integers, matching how the original
/// stores object positions and how <c>Terrain_HeightQuery</c> takes its argument (an <c>int[2]</c>
/// ground-plane coordinate, with height returned separately).
///
/// Axis convention follows the original: X/Y are the ground plane and Z is up. That is *not* the
/// convention the renderer uses (OpenGL here is Y-up); conversion happens once, at the render
/// boundary, in <c>Render/WorldScale</c> — deliberately not here, so simulation code never has to
/// think about a rendering concern.
///
/// Kept as a readonly struct with integer arithmetic rather than wrapping System.Numerics.Vector3:
/// per docs/herculan/planning.md's "Math" decision the simulation stays in the original's fixed-point
/// domain, and a float vector type would quietly reintroduce the drift that decision exists to
/// avoid.
/// </summary>
public readonly struct Vec3i : IEquatable<Vec3i> {
	public int X { get; }
	public int Y { get; }
	public int Z { get; }

	public Vec3i(int x, int y, int z) {
		X = x;
		Y = y;
		Z = z;
	}

	public static Vec3i Zero => new(0, 0, 0);

	public static Vec3i operator +(Vec3i a, Vec3i b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
	public static Vec3i operator -(Vec3i a, Vec3i b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
	public static Vec3i operator -(Vec3i v) => new(-v.X, -v.Y, -v.Z);
	public static bool operator ==(Vec3i a, Vec3i b) => a.Equals(b);
	public static bool operator !=(Vec3i a, Vec3i b) => !a.Equals(b);

	/// <summary>
	/// Distance to <paramref name="other"/> using the original's sqrt-free approximation — see
	/// <see cref="SimMath.FastMagnitude3D"/>. This is the distance function DBSIM actually uses for
	/// collision radii and proximity checks, so it is the one simulation code should reach for; it
	/// runs from about 8% low to about 9% high against true Euclidean distance depending on
	/// direction, and that bias is part of the behavior being reproduced, not an error to correct.
	/// </summary>
	public int ApproxDistanceTo(Vec3i other) =>
		SimMath.FastMagnitude3D(X - other.X, Y - other.Y, Z - other.Z);

	public bool Equals(Vec3i other) => X == other.X && Y == other.Y && Z == other.Z;

	public override bool Equals(object? obj) => obj is Vec3i other && Equals(other);

	public override int GetHashCode() => HashCode.Combine(X, Y, Z);

	public override string ToString() => $"({X}, {Y}, {Z})";
}
