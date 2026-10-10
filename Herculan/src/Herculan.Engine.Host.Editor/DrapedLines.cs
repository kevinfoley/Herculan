using System.Numerics;
using Herculan.Engine.Numerics;
using Herculan.Engine.Render;
using Herculan.Engine.Terrain;

namespace Herculan.Engine.Host.Editor;

/// <summary>
/// Builds render-space line lists that lie on the terrain: each edge is cut into pieces no longer
/// than half a terrain cell and every vertex is set on the ground under it, lifted by
/// <see cref="LiftWorld"/> so the line does not sink into the faces it crosses. The output feeds
/// <see cref="WireframeRenderer.DrawLines"/>, two vertices per segment.
///
/// <para>Off the heightmap the ground query answers 0, so a line that leaves the grid drops to height
/// 0 there, which is where the simulation itself puts anything off the grid.</para>
/// </summary>
internal sealed class DrapedLines {
	/// <summary>How far above the ground a draped line runs, in world units (about two metres).</summary>
	public const int LiftWorld = 300;

	private const int CircleSegments = 96;

	private readonly HeightGrid _terrain;
	private readonly int _maxPiece;

	public DrapedLines(HeightGrid terrain) {
		_terrain = terrain;
		_maxPiece = Math.Max(terrain.CellSize / 2, 1);
	}

	/// <summary>Appends one edge from <paramref name="from"/> to <paramref name="to"/>, by their X and Y.</summary>
	public void AddEdge(List<Vector3> into, Vec3i from, Vec3i to) {
		long dx = to.X - from.X;
		long dy = to.Y - from.Y;
		double length = Math.Sqrt(dx * dx + dy * dy);
		int pieces = Math.Max(1, (int)Math.Ceiling(length / _maxPiece));

		var previous = OnGround(from.X, from.Y);
		for (int i = 1; i <= pieces; i++) {
			var next = OnGround((int)(from.X + dx * i / pieces), (int)(from.Y + dy * i / pieces));
			into.Add(previous);
			into.Add(next);
			previous = next;
		}
	}

	/// <summary>Appends a polyline through <paramref name="points"/>, closing it if asked.</summary>
	public void AddPolyline(List<Vector3> into, IReadOnlyList<Vec3i> points, bool closed = false) {
		for (int i = 1; i < points.Count; i++) {
			AddEdge(into, points[i - 1], points[i]);
		}

		if (closed && points.Count > 2) {
			AddEdge(into, points[^1], points[0]);
		}
	}

	/// <summary>Appends the axis-aligned rectangle with corners <paramref name="a"/> and <paramref name="b"/>.</summary>
	public void AddRectangle(List<Vector3> into, Vec3i a, Vec3i b) {
		var c = new Vec3i(a.X, b.Y, 0);
		var d = new Vec3i(b.X, a.Y, 0);
		AddPolyline(into, new[] { a, c, b, d }, closed: true);
	}

	/// <summary>Appends a circle of <paramref name="radius"/> world units about <paramref name="centre"/>.</summary>
	public void AddCircle(List<Vector3> into, Vec3i centre, int radius) {
		var ring = new Vec3i[CircleSegments];
		for (int i = 0; i < ring.Length; i++) {
			double angle = i * Math.Tau / ring.Length;
			ring[i] = new Vec3i(centre.X + (int)(Math.Cos(angle) * radius),
				centre.Y + (int)(Math.Sin(angle) * radius), 0);
		}

		AddPolyline(into, ring, closed: true);
	}

	/// <summary>A vertical post from the ground at (x, y) up <paramref name="heightWorld"/> units.</summary>
	public void AddPost(List<Vector3> into, int x, int y, int heightWorld) {
		int ground = _terrain.HeightAtWorld(x, y);
		into.Add(WorldScale.ToRender(new Vec3i(x, y, ground)));
		into.Add(WorldScale.ToRender(new Vec3i(x, y, ground + heightWorld)));
	}

	/// <summary>The render-space point on the ground under (x, y), lifted.</summary>
	public Vector3 OnGround(int x, int y) =>
		WorldScale.ToRender(new Vec3i(x, y, _terrain.HeightAtWorld(x, y) + LiftWorld));
}
