using HercWorks.Core.Data.File.Dgs;
using Herculan.Engine.Numerics;

namespace Herculan.Es1.Sim;

/// <summary>
/// A zone or structure shape's collision volume, the <c>GridShape</c> (<c>ES1DBSIM</c> vtable <c>000923da</c>) height field
/// the theater libraries load as ES2's (docs/es1/simulation/world.md#the-collision-volume--gridshape), the point queries the
/// walk test asks of it, and the march a ray takes through it.
/// </summary>
public sealed class Es1GridShape(GridShapeCollision grid) {
	/// <summary>
	/// <c>GridShape_HeightAt</c> (<c>0001fc5c</c>), ES2's: 0 for an empty grid, otherwise the point shifted by the grid's
	/// origin (<c>origin &lt;&lt; shift</c>, 16-bit) and <see cref="HeightAround"/>.
	/// </summary>
	public int HeightAt(int x, int y, int radius) =>
		grid.IsSolid ? HeightAround(x + OriginOffsetX, y + OriginOffsetY, radius) : 0;

	/// <summary>
	/// <c>GridShape_Raycast</c> (<c>0001fccc</c>)(shape, <paramref name="clearance"/>, the segment, out): ES2's march
	/// (docs/retail/simulation/hit-detection.md#the-collision-volume--the-dgs-records-height-field) but for the three things
	/// docs/es1/simulation/world.md#the-collision-volume--gridshape names. A grid with no columns, a segment whose ends are
	/// both at or above the ceiling, and one whose ends both have a negative Z miss. Both ends are shifted by the origin; the
	/// direction is rescaled to a step of <c>(1 &lt;&lt; shift) + clearance</c> (<see cref="RescaleToLength"/>); and for
	/// <c>Math_FastMagnitude3D</c>(start − end) / step steps from the start, the first point under which
	/// <see cref="HeightAround"/> with the clearance is greater than its Z is the hit, given less the origin's shift in
	/// <paramref name="hit"/>, out's <c>+4</c>.
	/// </summary>
	/// <remarks>Retail divides by a step of 0 and faults; here a step of 0 misses, an engine guard.</remarks>
	public bool Raycast(short clearance, Vec3i start, Vec3i end, out Vec3i hit) {
		hit = default;
		int ceiling = grid.MaxHeight;
		if (grid.Columns == 0 || (start.Z >= ceiling && end.Z >= ceiling) || (start.Z < 0 && end.Z < 0)) {
			return false;
		}

		int offsetX = OriginOffsetX, offsetY = OriginOffsetY;
		int x = start.X + offsetX, y = start.Y + offsetY, z = start.Z;
		int endX = end.X + offsetX, endY = end.Y + offsetY;
		short step = (short)((1 << grid.CellShift) + clearance);
		if (step == 0) {
			return false;
		}

		var (dx, dy, dz) = RescaleToLength(step, endX - x, endY - y, end.Z - z);
		short steps = (short)(SimMath.FastMagnitude3D(x - endX, y - endY, z - end.Z) / step);
		for (short i = 0; i < steps; i++) {
			if (z < HeightAround(x, y, clearance)) {
				hit = new Vec3i(x - offsetX, y - offsetY, z);
				return true;
			}

			z += dz;
			x += dx;
			y += dy;
		}

		return false;
	}

	/// <summary>
	/// <c>FUN_00063b5c</c>(<paramref name="length"/>, vector), read from the decompile: a vector of
	/// <c>Math_FastMagnitude3D</c> 0 becomes 0; otherwise the scale <c>(length &lt;&lt; 16) / magnitude</c>, truncated by
	/// <c>IDIV</c> and cut to 16 bits, takes each component through the rounding <c>Math_Q16Multiply2</c> (<c>0007358c</c>),
	/// and each product is cut to 16 bits and sign-extended.
	/// </summary>
	private static (int X, int Y, int Z) RescaleToLength(short length, int x, int y, int z) {
		int magnitude = SimMath.FastMagnitude3D(x, y, z);
		if (magnitude == 0) {
			return (0, 0, 0);
		}

		short scale = (short)((length << 16) / magnitude);
		return ((short)SimMath.Q16MultiplyRounded(scale, x), (short)SimMath.Q16MultiplyRounded(scale, y),
			(short)SimMath.Q16MultiplyRounded(scale, z));
	}

	/// <summary>The grid's origin in world units, <c>origin &lt;&lt; shift</c> cut to 16 bits, which both queries add.</summary>
	private int OriginOffsetX => (short)(grid.OriginColumn << grid.CellShift);

	/// <inheritdoc cref="OriginOffsetX"/>
	private int OriginOffsetY => (short)(grid.OriginRow << grid.CellShift);

	/// <summary>
	/// <c>GridShape_HeightAround</c> (<c>0001fa9c</c>), ES2's: the tallest cell within <paramref name="radius"/> of a point
	/// already shifted by the origin when half a cell is less than the radius, otherwise the one cell under it; 0 off the
	/// grid (docs/retail/simulation/hit-detection.md#the-collision-volume--the-dgs-records-height-field).
	/// </summary>
	private int HeightAround(int x, int y, int radius) {
		int shift = grid.CellShift;
		if (shift < 1 || (1 << (shift - 1)) >= radius) {
			return Cell((short)(x >> shift), (short)(y >> shift));
		}

		int best = 0;
		for (short row = (short)((y - radius) >> shift); row <= (short)((y + radius) >> shift); row++) {
			for (short column = (short)((x - radius) >> shift); column <= (short)((x + radius) >> shift); column++) {
				best = Math.Max(best, Cell(column, row));
			}
		}

		return best;
	}

	/// <summary>One cell's height, or 0 for a cell off the grid.</summary>
	private int Cell(short column, short row) =>
		column >= 0 && column < grid.Columns && row >= 0 && row < grid.Rows ? grid.Heights[grid.Cells[row][column]] : 0;
}
