using Herculan.Engine.Numerics;
using Herculan.Engine.Terrain;

namespace Herculan.Engine.Render;

/// <summary>
/// Whether a sphere reaches into a view: in front of the eye's plane and inside the four edge planes
/// <see cref="Camera.EdgeSlopes"/> gives, the same frustum the projection draws. This engine's own test, not the
/// original's: <see cref="ObjectDrawTable"/> uses it under the widescreen tweak to draw a structure whose cell the terrain
/// walk does not reach while part of it is on screen. Conservative near the frustum's corners, where a sphere outside it
/// can still pass.
/// </summary>
public readonly struct ViewSphereTest {
	private readonly Vec3i _eye;
	private readonly (double X, double Y, double Z) _right;
	private readonly (double X, double Y, double Z) _forward;
	private readonly (double X, double Y, double Z) _up;
	private readonly ViewEdgeSlopes _edges;

	/// <param name="eye">The view's position, world units.</param>
	/// <param name="rotation">The view's rotation, rows right, forward and up — <see cref="Camera.ViewRotation"/>.</param>
	/// <param name="edges">The view's edge slopes for the viewport it is drawn into.</param>
	public ViewSphereTest(Vec3i eye, in Transform3 rotation, ViewEdgeSlopes edges) {
		_eye = eye;
		_right = Axis(rotation.RotateVector(Q14One, 0, 0));
		_forward = Axis(rotation.RotateVector(0, Q14One, 0));
		_up = Axis(rotation.RotateVector(0, 0, Q14One));
		_edges = edges;
	}

	private const int Q14One = 1 << 14;

	/// <summary>Whether the sphere of <paramref name="radius"/> about <paramref name="centre"/>, world units, reaches into the view.</summary>
	public bool Overlaps(Vec3i centre, int radius) {
		double dx = (double)centre.X - _eye.X;
		double dy = (double)centre.Y - _eye.Y;
		double dz = (double)centre.Z - _eye.Z;
		double across = dx * _right.X + dy * _right.Y + dz * _right.Z;
		double depth = dx * _forward.X + dy * _forward.Y + dz * _forward.Z;
		double up = dx * _up.X + dy * _up.Y + dz * _up.Z;

		// Each edge plane passes through the eye; a point is inside it when its distance from the plane, signed toward
		// the view's axis, is positive.
		return depth >= -radius
			&& Reaches(across + _edges.Left * depth, _edges.Left, radius)
			&& Reaches(_edges.Right * depth - across, _edges.Right, radius)
			&& Reaches(_edges.Top * depth - up, _edges.Top, radius)
			&& Reaches(up + _edges.Bottom * depth, _edges.Bottom, radius);
	}

	private static bool Reaches(double offset, double slope, int radius) =>
		offset >= -radius * System.Math.Sqrt(1.0 + slope * slope);

	private static (double X, double Y, double Z) Axis(Vec3i axis) =>
		((double)axis.X / Q14One, (double)axis.Y / Q14One, (double)axis.Z / Q14One);
}
