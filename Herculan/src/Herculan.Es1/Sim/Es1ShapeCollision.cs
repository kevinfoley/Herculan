using Herculan.Engine.Numerics;

namespace Herculan.Es1.Sim;

/// <summary>
/// An object the walk test reads through its shape instance (<c>+0x2e</c>)'s shape: its frame (the rotation
/// <c>Context_GetTransform</c> of <c>+6</c> builds, at its position <c>+0x20</c>), its shape's radius (<c>+8</c>) and its
/// shape's collision volume (docs/es1/simulation/terrain.md#the-tests-against-the-shapes).
/// </summary>
/// <param name="Volume">The shape's collision volume, or null for one whose grid is empty, which answers 0.</param>
public readonly record struct Es1ShapeObject(Transform3 Frame, short Radius, Es1GridShape? Volume);

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
}

/// <summary>
/// A structure of <c>g_BasePool</c>, as far as the walk test reads it: whether its type (<c>+0x188</c>) has a collision
/// model (the record's <c>+0x3a</c> not 0, <c>BASES.DAT</c> types 5, 6 and 8) and its shape
/// (docs/es1/simulation/terrain.md#the-structures-tests). PLACEHOLDER: none is built until the plan's flyers-and-bases
/// step ports the base constructor (<c>00003cbc</c>), which replaces this with the structure object.
/// </summary>
public sealed class Es1Structure(bool hasCollisionModel, Es1ShapeObject shape) {
	public bool HasCollisionModel { get; } = hasCollisionModel;

	public Es1ShapeObject Shape { get; } = shape;
}

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
	/// <c>Structure_GatherWalkCandidates</c> (<c>0000375c</c>): the structures whose type has no collision model, from the
	/// pool's last back, and <see cref="WalkCollisionTest"/>'s answer over them; 0 with none
	/// (docs/es1/simulation/terrain.md#the-structures-tests). Retail gathers into a 40-entry stack array it does not bound;
	/// here the gather has no limit.
	/// </summary>
	public static int GatherWalkCandidates(IReadOnlyList<Es1Structure> structures, Vec3i point) {
		var candidates = new List<Es1ShapeObject>();
		for (int i = structures.Count - 1; i >= 0; i--) {
			if (!structures[i].HasCollisionModel) {
				candidates.Add(structures[i].Shape);
			}
		}

		return WalkCollisionTest(candidates, point);
	}
}
