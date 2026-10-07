using Herculan.Engine.Numerics;

namespace Herculan.Engine.Terrain;

/// <summary>
/// How far a view reaches either side of its axis at unit depth: the slope, across over depth, of
/// its left and right edges and, up over depth, of its top and bottom. For DBSIM's view these are
/// the projection centre's distance from each edge of the view rect over the focal length — the
/// <c>cx</c>, <c>w − cx</c>, <c>cy</c> and <c>h − cy</c> over <c>2^s</c> that
/// <c>ViewFrustum_Build</c> multiplies by the far distance (docs/retail/formats/terrain-drawing.md, "The
/// planes"). See <see cref="Render.Camera.EdgeSlopes"/>.
/// </summary>
public readonly record struct ViewEdgeSlopes(float Left, float Right, float Top, float Bottom);

/// <summary>
/// The terrain cells a view can see this frame — the polygon <c>Terrain_SetupVisibleRegion</c>
/// (<c>0046ca98</c>) builds at <c>grid+0x28</c> (count <c>grid+0xc8</c>, flag <c>grid+0x11c</c>):
/// <c>Terrain_BuildDrawRegionQuad</c>'s square round the viewer, cut by the view's frustum loosened
/// a cell and a half on every side and widened by the zone's height range, in cell coordinates. See
/// docs/retail/formats/terrain-drawing.md, "The visible region".
///
/// <para>What reads it here is <see cref="HeightGrid.PickDrawCell"/>'s bounds test, through
/// <see cref="MinCellX"/> and its siblings. One instance belongs to one grid, as the fields do in
/// the original: a view whose frustum leaves nothing of the square keeps the last polygon, and
/// until the first one is built the bounds are the empty loop's sentinels, which no cell can sit one
/// past.</para>
///
/// <para>The view's rect and focal length come from the engine's <see cref="Render.Camera"/>
/// (<see cref="ViewEdgeSlopes"/>) rather than from a DBSIM view struct, and the square sits at
/// height 0, the engine grid having no origin fields; otherwise this is the original's integer
/// arithmetic.</para>
/// </summary>
public sealed class TerrainVisibleRegion {
	private readonly List<(int X, int Y)> _cells = new();

	/// <summary>The polygon's corners in cell coordinates — <c>grid+0x28</c>.</summary>
	public IReadOnlyList<(int X, int Y)> Cells => _cells;

	/// <summary>
	/// Whether the last build left anything — <c>grid+0x11c</c>. When it did not, <see cref="Cells"/>
	/// is still the build before.
	/// </summary>
	public bool Visible { get; private set; }

	/// <summary>The smallest cell X among <see cref="Cells"/>, <see cref="int.MaxValue"/> while there are none.</summary>
	public int MinCellX { get; private set; } = int.MaxValue;

	/// <summary>The largest cell X among <see cref="Cells"/>, <see cref="int.MinValue"/> while there are none.</summary>
	public int MaxCellX { get; private set; } = int.MinValue;

	/// <summary>The smallest cell Y among <see cref="Cells"/>, <see cref="int.MaxValue"/> while there are none.</summary>
	public int MinCellY { get; private set; } = int.MaxValue;

	/// <summary>The largest cell Y among <see cref="Cells"/>, <see cref="int.MinValue"/> while there are none.</summary>
	public int MaxCellY { get; private set; } = int.MinValue;

	/// <summary>
	/// Rebuilds the region for a view at <paramref name="eye"/> turned by <paramref name="rotation"/>
	/// (the view's euler matrix, rows right, forward, up) — the region half of
	/// <c>Terrain_SetupVisibleRegion</c>.
	/// </summary>
	public void Update(HeightGrid grid, Vec3i eye, in Transform3 rotation, ViewEdgeSlopes edges) {
		int far = (int)grid.VisibilityRange;
		if (far <= 0) {
			return;
		}

		int cellShift = grid.CellShift;
		int margin = (1 << cellShift) + (1 << (cellShift - 1));

		var square = DrawRegionSquare(grid, eye, far);
		var planes = BuildFrustum(eye, rotation, edges, far, margin);
		var region = ClipGroundPolygon(planes, eye, square, grid.HeightBase, grid.MaxWorldHeight);

		Visible = region.Count > 0;
		if (!Visible) {
			return;
		}

		_cells.Clear();
		MinCellX = MinCellY = int.MaxValue;
		MaxCellX = MaxCellY = int.MinValue;
		foreach (var point in region) {
			int cellX = point.X >> cellShift;
			int cellY = point.Y >> cellShift;
			_cells.Add((cellX, cellY));
			MinCellX = System.Math.Min(MinCellX, cellX);
			MaxCellX = System.Math.Max(MaxCellX, cellX);
			MinCellY = System.Math.Min(MinCellY, cellY);
			MaxCellY = System.Math.Max(MaxCellY, cellY);
		}
	}

	/// <summary>
	/// <c>Terrain_BuildDrawRegionQuad</c> (<c>0046d220</c>): the square of half-width
	/// <paramref name="far"/> round the viewer, clamped to the grid's first cell and to two cells
	/// short of its far edges, as four corners in the order the original writes them. Empty when
	/// the clamp leaves nothing.
	/// </summary>
	private static List<Vec3i> DrawRegionSquare(HeightGrid grid, Vec3i eye, int far) {
		int x0 = eye.X - far <= 0 ? 0 : eye.X - far;
		int y0 = eye.Y - far <= 0 ? 0 : eye.Y - far;
		int x1 = System.Math.Min(eye.X + far, (grid.Width - 2) << grid.CellShift);
		int y1 = System.Math.Min(eye.Y + far, (grid.Height - 2) << grid.CellShift);

		if (x1 < x0 || y1 < y0) {
			return new List<Vec3i>();
		}

		int z = grid.HeightBase;
		return new List<Vec3i> { new(x0, y0, z), new(x0, y1, z), new(x1, y1, z), new(x1, y0, z) };
	}

	/// <summary>A plane as the original stores it: a normal of length <c>0x800</c> and <c>d</c>, the plane being <c>n · p + d = 0</c>.</summary>
	private readonly record struct Plane(int NormalX, int NormalY, int NormalZ, int D);

	/// <summary>
	/// <c>ViewFrustum_Build</c> (<c>004948d8</c>), its six planes indexed as it stores them: near,
	/// far, top, bottom, left, right. Each is built in view axes (across, depth, up), rotated into the
	/// world, and passes <paramref name="margin"/> outside the eye rather than through it; the near
	/// plane sits that far behind it. A negated point or normal is negated after the rotation, as the
	/// original does it, which rounds differently from rotating the negated vector.
	/// </summary>
	private static Plane[] BuildFrustum(Vec3i eye, in Transform3 rotation, ViewEdgeSlopes edges,
			int far, int margin) {
		// cx·F >> s and its siblings: an edge's slope scaled out to the far distance.
		int left = (int)System.Math.Floor((double)edges.Left * far);
		int right = (int)System.Math.Floor((double)edges.Right * far);
		int top = (int)System.Math.Floor((double)edges.Top * far);
		int bottom = (int)System.Math.Floor((double)edges.Bottom * far);

		var ahead = rotation.RotateVector(0, far, 0);
		var above = rotation.RotateVector(0, 0, margin);
		var beside = rotation.RotateVector(margin, 0, 0);

		return new[] {
			FromPointNormal(rotation.RotateVector(0, -margin, 0), ahead),
			FromPointNormal(ahead, Negate(ahead)),
			FromPointNormal(above, rotation.RotateVector(0, top, -far)),
			FromPointNormal(Negate(above), rotation.RotateVector(0, bottom, far)),
			FromPointNormal(Negate(beside), rotation.RotateVector(far, left, 0)),
			FromPointNormal(beside, rotation.RotateVector(-far, right, 0)),
		};
	}

	private static Vec3i Negate(Vec3i v) => new(-v.X, -v.Y, -v.Z);

	/// <summary>
	/// <c>Plane_FromPointNormal</c> (<c>0047e344</c>): the normal scaled to length <c>0x800</c> by its
	/// magnitude — exact (<c>Point3I_ExactMagnitude</c>) when every component is strictly inside
	/// ±15001, <see cref="SimMath.FastMagnitude3D"/> otherwise — and <c>d = −(point · normal)</c>.
	/// </summary>
	private static Plane FromPointNormal(Vec3i point, Vec3i normal) {
		const int ExactLimit = 0x3a99;
		bool small = normal.X < ExactLimit && normal.X > -ExactLimit
			&& normal.Y < ExactLimit && normal.Y > -ExactLimit
			&& normal.Z < ExactLimit && normal.Z > -ExactLimit;

		int magnitude = small
			? SimMath.ISqrt(unchecked((uint)(normal.X * normal.X + normal.Y * normal.Y + normal.Z * normal.Z)))
			: SimMath.FastMagnitude3D(normal.X, normal.Y, normal.Z);

		int x = (int)((long)normal.X * 0x800 / magnitude);
		int y = (int)((long)normal.Y * 0x800 / magnitude);
		int z = (int)((long)normal.Z * 0x800 / magnitude);
		return new Plane(x, y, z, unchecked(-(point.X * x + point.Y * y + point.Z * z)));
	}

	/// <summary>
	/// <c>ViewFrustum_ClipGroundPolygon</c> (<c>00494d4c</c>). Every plane with a Z component, or
	/// with no normal at all, is shifted along Z first (<c>Plane_ShiftAlongZ</c>): by
	/// <paramref name="lowZ"/> when that component is 0 or less and by −<paramref name="highZ"/>
	/// when it is positive, so it is met at whichever height passes it more easily. The polygon is
	/// cut relative to the eye, against left, right, top, bottom, far and near in that order.
	/// </summary>
	private static List<Vec3i> ClipGroundPolygon(Plane[] planes, Vec3i eye, List<Vec3i> polygon,
			int lowZ, int highZ) {
		var points = new List<Vec3i>(polygon.Count);
		foreach (var point in polygon) {
			points.Add(new Vec3i(point.X - eye.X, point.Y - eye.Y, point.Z - eye.Z));
		}

		foreach (int index in ClipOrder) {
			var plane = planes[index];
			bool vertical = plane.NormalZ == 0 && (plane.NormalX != 0 || plane.NormalY != 0);
			if (!vertical) {
				int shift = plane.NormalZ < 1 ? lowZ : -highZ;
				plane = plane with { D = unchecked(plane.D - shift * plane.NormalZ) };
			}

			points = KeepFront(plane, points);
		}

		for (int i = 0; i < points.Count; i++) {
			points[i] = new Vec3i(points[i].X + eye.X, points[i].Y + eye.Y, points[i].Z + eye.Z);
		}

		return points;
	}

	/// <summary>The plane order the clip runs: its flag bits <c>0x20</c>, <c>0x40</c>, <c>8</c>, <c>0x10</c>, <c>4</c>, <c>2</c>.</summary>
	private static readonly int[] ClipOrder = { 4, 5, 2, 3, 1, 0 };

	/// <summary>
	/// <c>Poly_SplitByPlane</c> (<c>0047e630</c>), keeping the front as both of the region clip's
	/// callers do. From the last vertex round: a vertex in front goes to the front, one on the plane
	/// to both sides, and an edge crossing the plane puts its <c>Math_PlaneSegmentIntersect</c> point
	/// on both. A side left with fewer than three points is emptied, and when neither side received
	/// a vertex strictly on its own side the front is the whole input.
	/// </summary>
	private static List<Vec3i> KeepFront(Plane plane, List<Vec3i> polygon) {
		var front = new List<Vec3i>();
		if (polygon.Count < 3) {
			return front;
		}

		int back = 0;
		bool frontOnPlaneOnly = true;
		bool backOnPlaneOnly = true;

		var previous = polygon[^1];
		int previousSide = Classify(plane, previous);

		foreach (var current in polygon) {
			int side = Classify(plane, current);
			switch (previousSide * 3 + side) {
				case 1:
				case 4:
					front.Add(current);
					frontOnPlaneOnly = false;
					break;

				case 2:
					if (Crossing(plane, previous, current, out var leaving)) {
						front.Add(leaving);
						back += 2;
						backOnPlaneOnly = false;
					}
					break;

				case -4:
				case -1:
					back++;
					backOnPlaneOnly = false;
					break;

				case -3:
				case 0:
				case 3:
					back++;
					front.Add(current);
					break;

				case -2:
					if (Crossing(plane, previous, current, out var entering)) {
						back++;
						front.Add(entering);
						front.Add(current);
						frontOnPlaneOnly = false;
					}
					break;
			}

			previous = current;
			previousSide = side;
		}

		if (front.Count < 3) {
			frontOnPlaneOnly = true;
			front.Clear();
		}

		if (back < 3) {
			backOnPlaneOnly = true;
		}

		return frontOnPlaneOnly && backOnPlaneOnly ? new List<Vec3i>(polygon) : front;
	}

	/// <summary><c>Plane_ClassifyPoint</c> (<c>0047e9ec</c>): 1 in front, 0 on the plane, −1 behind, the dot product in 32 bits.</summary>
	private static int Classify(Plane plane, Vec3i point) {
		int dot = unchecked(plane.NormalX * point.X + plane.NormalY * point.Y + plane.NormalZ * point.Z);
		int onPlane = unchecked(-plane.D);
		return dot > onPlane ? 1 : dot == onPlane ? 0 : -1;
	}

	private static bool Crossing(Plane plane, Vec3i from, Vec3i to, out Vec3i hit) {
		bool crosses = HeightGrid.PlanePoint(plane.NormalX, plane.NormalY, plane.NormalZ, plane.D,
			from.X, from.Y, from.Z, to.X, to.Y, to.Z, out int x, out int y, out int z);
		hit = new Vec3i(x, y, z);
		return crosses;
	}
}
