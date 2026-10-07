using HercWorks.Core.Data.File.Dgs;
using Herculan.Engine.Numerics;

namespace Herculan.Engine.Sim;

/// <summary>
/// A structure shape's collision volume, and the two queries the simulation runs against it: the
/// height under a point (<see cref="HeightAt"/>) and the ray march that walks the grid looking
/// for one (<see cref="Raycast"/>). The data itself is <see cref="GridShapeCollision"/>, read out of the
/// shape's <c>.DGS</c> record.
///
/// <para><b>A building is a height field, not a mesh.</b> Nothing in the original tests a shot
/// against a structure's polygons; it steps along the ray in shape space and asks the grid how tall
/// the column under each step is, stopping the first time the ray is below it. That is why a shot
/// can pass under an overhang and why the volume is cheap enough to run against every structure in
/// a mission — and it is also the whole reason a hit needs no geometry loaded.</para>
///
/// <para>The grid's own units are shape space, i.e. world units around the model's origin. The
/// origin cell is not cell zero: a point is shifted by the grid's origin <i>in world units</i>
/// before it is divided down, so the footprint straddles the model. Both queries apply it, so a
/// walker and a shot meet the same footprint.</para>
/// </summary>
public sealed class ShapeVolume {
	private readonly GridShapeCollision _grid;

	public ShapeVolume(GridShapeCollision grid) {
		_grid = grid;
	}

	/// <summary>Whether this shape blocks anything at all — a grid with no cells is not solid.</summary>
	public bool IsSolid => _grid.IsSolid;

	/// <summary>The grid's tallest column, in world units.</summary>
	public int MaxHeight => _grid.MaxHeight;

	/// <summary>
	/// <c>GridShape_Raycast</c> (<c>004273c8</c>) — walks the ray from <paramref name="start"/> to <paramref name="end"/>,
	/// both in this shape's own space, and reports the first step that lands inside the volume.
	///
	/// <para>The step length is one cell plus the shot's clearance, so a wider round takes longer
	/// strides and samples a wider column at each of them — the two effects cancel, which is why a
	/// clearance of 200 does not make a shot tunnel through walls.</para>
	///
	/// <para>The march is fixed-point throughout: the direction is rescaled to the step length by a
	/// Q16 divide whose result is <b>truncated to 16 bits</b> before it multiplies, exactly as the
	/// original does, so a very short ray steps slightly imprecisely. That is reproduced rather than
	/// corrected.</para>
	/// </summary>
	/// <param name="start">Ray origin, in shape space.</param>
	/// <param name="end">Ray end, in shape space.</param>
	/// <param name="clearance">The shot's clearance — see <see cref="WeaponShot.Clearance"/>.</param>
	/// <param name="hit">Where the ray entered the volume, in shape space.</param>
	/// <returns>Whether the ray met the volume.</returns>
	public bool Raycast(Vec3i start, Vec3i end, int clearance, out Vec3i hit) {
		hit = default;

		// The original's own two rejects: an empty grid, and a ray running entirely above the
		// tallest column the height table can name. Note it is an "or", not an "and" — a ray with
		// either end below the ceiling is marched.
		if (!_grid.IsSolid || (start.Z >= _grid.MaxHeight && end.Z >= _grid.MaxHeight)) {
			return false;
		}

		int offsetX = OriginOffsetX;
		int offsetY = OriginOffsetY;

		int x = start.X + offsetX;
		int y = start.Y + offsetY;
		int z = start.Z;
		int endX = end.X + offsetX;
		int endY = end.Y + offsetY;

		short step = (short)((1 << _grid.CellShift) + clearance);
		if (step <= 0) {
			return false;
		}

		int deltaX = endX - x;
		int deltaY = endY - y;
		int deltaZ = end.Z - z;
		SimMath.ScaleToLength(ref deltaX, ref deltaY, ref deltaZ, step);

		int length = SimMath.FastMagnitude3D(x - endX, y - endY, z - end.Z);
		short steps = (short)((short)(length / step) + 1);

		for (short i = 0; i < steps; i++) {
			int height = HeightAround(x, y, clearance);
			if (height != 0 && z < height) {
				hit = new Vec3i(x - offsetX, y - offsetY, z);
				return true;
			}

			x += deltaX;
			y += deltaY;
			z += deltaZ;
		}

		return false;
	}

	/// <summary>
	/// <c>GridShape_HeightAt</c> (<c>00427360</c>) — the height under a point in shape space: the
	/// point shifted by the grid origin, then <see cref="HeightAround"/>. Zero for a volume with no
	/// cells. The walk test (<see cref="BaseObject.BlocksWalker"/>) asks this at radius 0.
	/// </summary>
	/// <param name="x">Point X, in shape space.</param>
	/// <param name="y">Point Y, in shape space.</param>
	/// <param name="radius">How wide a footprint to sample.</param>
	public int HeightAt(int x, int y, int radius) =>
		_grid.IsSolid ? HeightAround(x + OriginOffsetX, y + OriginOffsetY, radius) : 0;

	// The grid origin in world units, which both queries add before dividing a point down to a cell.
	// Kept to the original's 16-bit shift.
	private int OriginOffsetX => (short)(_grid.OriginColumn << _grid.CellShift);
	private int OriginOffsetY => (short)(_grid.OriginRow << _grid.CellShift);

	/// <summary>
	/// <c>GridShape_HeightAround</c> (<c>00427238</c>) — the tallest column within <paramref name="radius"/> of a point, or the column the point is in when the radius is
	/// smaller than half a cell. Both forms return zero for a point off the grid, which is what makes
	/// the volume end at its own edges. The point is already shifted by the grid origin;
	/// <see cref="HeightAt"/> and <see cref="Raycast"/> do that.
	/// </summary>
	/// <param name="x">Point X, in the grid's own indexing frame.</param>
	/// <param name="y">Point Y, in the grid's own indexing frame.</param>
	/// <param name="radius">How wide a footprint to sample — the shot's clearance.</param>
	private int HeightAround(int x, int y, int radius) {
		int shift = _grid.CellShift;

		// The narrow case is not an optimisation the engine adds: the original tests the radius
		// against half a cell and takes a single sample below it, which is a different answer from
		// the loop (that would sample the one cell too, but only after the same bounds test).
		if (shift < 1 || (1 << (shift - 1)) >= radius) {
			return HeightAt((short)(x >> shift), (short)(y >> shift));
		}

		int best = 0;
		for (short row = (short)((y - radius) >> shift); row <= (short)((y + radius) >> shift); row++) {
			for (short column = (short)((x - radius) >> shift); column <= (short)((x + radius) >> shift); column++) {
				int height = HeightAt(column, row);
				if (height > best) {
					best = height;
				}
			}
		}

		return best;
	}

	/// <summary>One cell's column height, or zero for a cell outside the grid.</summary>
	private int HeightAt(short column, short row) =>
		column >= 0 && column < _grid.Columns && row >= 0 && row < _grid.Rows
			? _grid.Heights[_grid.Cells[row][column]]
			: 0;
}
