using HercWorks.Core.Data.File.Dgs;

namespace Herculan.Es1.Sim;

/// <summary>
/// A zone or structure shape's collision volume, the <c>GridShape</c> (<c>ES1DBSIM</c> vtable <c>000923da</c>) height field
/// the theater libraries load as ES2's (docs/es1/simulation/world.md#the-collision-volume--gridshape), and the point
/// queries the walk test asks of it. Its march, which differs from ES2's, is not ported: no step tests a shot yet.
/// </summary>
public sealed class Es1GridShape(GridShapeCollision grid) {
	/// <summary>
	/// <c>GridShape_HeightAt</c> (<c>0001fc5c</c>), ES2's: 0 for an empty grid, otherwise the point shifted by the grid's
	/// origin (<c>origin &lt;&lt; shift</c>, 16-bit) and <see cref="HeightAround"/>.
	/// </summary>
	public int HeightAt(int x, int y, int radius) =>
		grid.IsSolid
			? HeightAround(x + (short)(grid.OriginColumn << grid.CellShift), y + (short)(grid.OriginRow << grid.CellShift), radius)
			: 0;

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
