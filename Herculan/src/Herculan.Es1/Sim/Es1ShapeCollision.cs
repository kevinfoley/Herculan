using Herculan.Engine.Numerics;

namespace Herculan.Es1.Sim;

/// <summary>
/// An object the walk test reads through its shape instance (<c>+0x2e</c>)'s shape: its frame (the rotation
/// <c>Context_GetTransform</c> of <c>+6</c> builds, at its position <c>+0x20</c>), its shape's radius (<c>+8</c>) and its
/// shape's collision volume (docs/es1/simulation/terrain.md#the-tests-against-the-shapes).
/// </summary>
/// <param name="Volume">The shape's collision volume, or null for one whose grid is empty, which answers 0.</param>
public readonly record struct Es1ShapeObject(Transform3 Frame, short Radius, Es1GridShape? Volume) : IEs1Placed {
	/// <summary><c>+0x20</c>, the position, the frame's translation.</summary>
	public Vec3i Position => new(Frame.X, Frame.Y, Frame.Z);
}

/// <summary>
/// <c>Zone_ShapeCount</c> and <c>Zone_ShapeObjects</c>: the zone's placed shapes, which <c>Terrain_LoadZoneShapes</c>
/// builds from the zone file's records on the theater's shape library.
/// </summary>
public sealed class Es1ZoneShapes(IReadOnlyList<Es1ShapeObject> objects) {
	/// <summary>No zone shapes, as a negative zone loads.</summary>
	public static Es1ZoneShapes Empty { get; } = new([]);

	public IReadOnlyList<Es1ShapeObject> Objects { get; } = objects;

	/// <summary>
	/// <c>Zone_WalkCollisionTest</c> (<c>00020860</c>): <see cref="Es1ShapeTests.WalkCollisionTest"/> over the zone's shapes
	/// (docs/es1/simulation/terrain.md#a-point--zone_walkcollisiontest-00020860). Retail passes the radius on unread.
	/// </summary>
	public int WalkCollisionTest(Vec3i point) => Es1ShapeTests.WalkCollisionTest(Objects, point);

	/// <summary>
	/// <c>Zone_RaycastShapeObjects</c> (<c>00020b50</c>): <see cref="Es1ShapeTests.RaycastShapeVolume"/> over the zone's
	/// shapes (docs/es1/simulation/terrain.md#a-ray--zone_raycastshapeobjects-00020b50).
	/// </summary>
	public bool RaycastShapeObjects(Es1Ray ray, out Es1RayHit hit) => Es1ShapeTests.RaycastShapeVolume(Objects, ray, out hit);

	/// <summary>
	/// <c>Sim_RaycastShapes</c> (<c>00020b70</c>)(<paramref name="clearance"/>, <paramref name="to"/>,
	/// <paramref name="from"/>, result): the ray from <paramref name="from"/> to <paramref name="to"/>
	/// (<see cref="Es1Ray.Between"/>) against every zone shape, and whether it struck one
	/// (docs/es1/simulation/terrain.md#a-line-between-two-points--sim_raycastshapes-00020b70).
	/// </summary>
	public bool RaycastShapes(short clearance, Vec3i to, Vec3i from, out Es1RayHit hit) =>
		RaycastShapeObjects(Es1Ray.Between(clearance, to, from), out hit);
}

/// <summary>
/// ES2's ray record, which <c>Sim_RaycastShapes</c> builds on its stack (docs/es1/simulation/terrain.md#a-ray--zone_raycastshapeobjects-00020b50):
/// the frame at <c>+0</c>, the length at <c>+4</c>, which each hit shortens, the clearance at <c>+8</c> and the
/// world-to-ray transform at <c>+0xa</c>, the frame's rotation transposed with the negated translation rotated by it.
/// </summary>
public sealed class Es1Ray(Transform3 frame, int length, short clearance) {
	/// <summary>The frame, its translation the ray's start, its Y axis along the ray.</summary>
	public Transform3 Frame { get; } = frame;

	/// <summary><c>+4</c>, the length.</summary>
	public int Length { get; set; } = length;

	/// <summary><c>+8</c>, the clearance, which widens the shapes' reach and the march's step and samples.</summary>
	public short Clearance { get; } = clearance;

	/// <summary><c>+0xa</c>, the world-to-ray transform.</summary>
	public Transform3 WorldToRay { get; } = frame.Inverted();

	/// <summary>The start, the frame's translation (<c>+0x14</c> of the frame).</summary>
	public Vec3i Start => new(Frame.X, Frame.Y, Frame.Z);

	/// <summary>
	/// <c>Sim_RaycastShapes</c>' record: the rotation <c>BuildEulerRotationMatrixQ14</c> builds from
	/// <c>Math_EulerToward</c> (<c>00063d2c</c>) of <paramref name="to"/> less <paramref name="from"/>, at
	/// <paramref name="from"/>, the length <c>Math_DistanceBetweenPoints</c>(<paramref name="from"/>, <paramref name="to"/>).
	/// </summary>
	public static Es1Ray Between(short clearance, Vec3i to, Vec3i from) {
		var (x, y, z) = SimTrig.EulerToward(to, from);
		var frame = Transform3.FromEuler(x, y, z);
		frame.X = from.X;
		frame.Y = from.Y;
		frame.Z = from.Z;
		return new Es1Ray(frame, from.ApproxDistanceTo(to), clearance);
	}
}

/// <summary>A ray's result: <c>+0</c>, the distance to the hit, and <c>+4</c>, the shape struck.</summary>
public readonly record struct Es1RayHit(int Distance, Es1ShapeObject Shape);

/// <summary>The point tests against shapes' collision volumes (docs/es1/simulation/terrain.md#the-tests-against-the-shapes).</summary>
public static class Es1ShapeTests {
	/// <summary>
	/// <c>Structure_WalkCollisionTest</c> (<c>0002075c</c>): for each object whose <c>Math_DistanceBetweenPoints2D</c>
	/// from its position to <paramref name="point"/> is less than its shape's radius, the point less the position rotated
	/// by the transposed XY block of its rotation, and the first non-zero <c>GridShape_HeightAt</c>(shape, 0, that point)
	/// answered; 0 when none answers (docs/es1/simulation/terrain.md#a-point--zone_walkcollisiontest-00020860). The point's
	/// Z is not read.
	/// </summary>
	public static int WalkCollisionTest(IReadOnlyList<Es1ShapeObject> objects, Vec3i point) {
		foreach (var shape in objects) {
			var frame = shape.Frame;
			int dx = point.X - frame.X, dy = point.Y - frame.Y;
			if (SimMath.FastMagnitude2D(dx, dy) >= shape.Radius) {
				continue;
			}

			// Math_TransposeRotation2D swaps the block's two off-diagonal entries; Math_Rotate2DPoint then rotates by it.
			(frame.M[1], frame.M[2]) = (frame.M[2], frame.M[1]);
			var (x, y) = Es1Formation.Rotate2D(frame, dx, dy);
			int height = shape.Volume?.HeightAt(x, y, 0) ?? 0;
			if (height != 0) {
				return height;
			}
		}

		return 0;
	}

	/// <summary>
	/// <c>Structure_GatherWalkCandidates</c> (<c>0000375c</c>): the structures whose type has no collision model, oldest
	/// first as <c>Pool_Prev</c> walks the pool, destroyed or not, each with the shape its instance holds, and
	/// <see cref="WalkCollisionTest"/>'s answer over them; 0 with none
	/// (docs/es1/simulation/terrain.md#the-structures-tests). Retail gathers into a 40-entry stack array it does not bound;
	/// here the gather has no limit.
	/// </summary>
	public static int GatherWalkCandidates(IReadOnlyList<Es1Base> structures, Vec3i point) {
		var candidates = new List<Es1ShapeObject>();
		for (int i = 0; i < structures.Count; i++) {
			if (!structures[i].Type.HasCollisionModel) {
				candidates.Add(structures[i].ShapeObject);
			}
		}

		return WalkCollisionTest(candidates, point);
	}

	/// <summary>
	/// <c>Sim_RaycastShapeList</c> (<c>000037e0</c>)(<paramref name="side"/>, <paramref name="ray"/>, result): the
	/// structures, oldest first, that are a wreck (<see cref="Es1Base.IsWreck"/>) or whose type has no collision model,
	/// with a side of 0 or more only those whose group's side byte, sign-extended, equals it; and
	/// <see cref="RaycastShapeVolume"/>'s answer over them (docs/es1/simulation/terrain.md#the-structures-tests). Retail
	/// reads the group unchecked; a structure with none matches no side here. The 40-entry array is not bounded, as in
	/// <see cref="GatherWalkCandidates"/>.
	/// </summary>
	public static bool RaycastShapeList(IReadOnlyList<Es1Base> structures, short side, Es1Ray ray, out Es1RayHit hit) {
		var candidates = new List<Es1ShapeObject>();
		foreach (var structure in structures) {
			if ((structure.IsWreck || !structure.Type.HasCollisionModel)
					&& (side < 0 || structure.Group is { } group && (sbyte)group.Side == side)) {
				candidates.Add(structure.ShapeObject);
			}
		}

		return RaycastShapeVolume(candidates, ray, out hit);
	}

	/// <summary>A hit nearer the ray's start than this ends <see cref="RaycastShapeVolume"/>'s walk.</summary>
	public const int NearHit = 500;

	/// <summary>
	/// <c>Sim_RaycastShapeVolume</c> (<c>00020894</c>), ES2's march over a list of objects
	/// (docs/es1/simulation/terrain.md#a-ray--zone_raycastshapeobjects-00020b50). For each, with <i>r</i> its shape's
	/// radius plus the clearance: when its distance from the ray's start is no more than <i>r</i> plus the length and its
	/// position through the world-to-ray transform has |<i>x</i>| &lt; <i>r</i> and −<i>r</i> &lt; <i>y</i> &lt;
	/// <i>r</i> + the length, the ray's start and end are taken into the shape's frame and marched
	/// (<see cref="Es1GridShape.Raycast"/>). A hit shortens the ray to the distance from the start to the hit point, which
	/// <paramref name="hit"/> takes with the shape, and one nearer than <see cref="NearHit"/> ends the walk. Answers whether
	/// anything was struck. An object with no volume, whose grid is empty, is not marched, as the march answers 0 for it.
	/// </summary>
	/// <remarks>
	/// <c>RaycastShapeMinDistance</c> (<c>000afa40</c>), which the walk lowers to each distance and zeroes at each object
	/// within reach, is not kept: its one reader, the shot sweep, stores it where nothing is known to read it
	/// (<see cref="Es1ShotSweep.Raycast"/>).
	/// </remarks>
	public static bool RaycastShapeVolume(IReadOnlyList<Es1ShapeObject> objects, Es1Ray ray, out Es1RayHit hit) {
		hit = default;
		bool struck = false;
		var start = ray.Start;
		foreach (var shape in objects) {
			var frame = shape.Frame;
			int reach = shape.Radius + ray.Clearance;
			int length = ray.Length;
			if (SimMath.FastMagnitude3D(start.X - frame.X, start.Y - frame.Y, start.Z - frame.Z) > reach + length) {
				continue;
			}

			var along = ray.WorldToRay.TransformPoint(frame.X, frame.Y, frame.Z);
			if (along.X >= reach || along.X <= -reach || along.Y <= -reach || along.Y >= reach + length) {
				continue;
			}

			var toShape = frame.Inverted();
			var from = toShape.TransformPoint(start.X, start.Y, start.Z);
			var endInWorld = ray.Frame.TransformPoint(0, length, 0);
			var to = toShape.TransformPoint(endInWorld.X, endInWorld.Y, endInWorld.Z);
			if (shape.Volume is { } volume && volume.Raycast(ray.Clearance, from, to, out var at)) {
				ray.Length = at.ApproxDistanceTo(from);
				hit = new Es1RayHit(ray.Length, shape);
				struck = true;
				if (ray.Length < NearHit) {
					break;
				}
			}
		}

		return struck;
	}
}
