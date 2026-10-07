using Herculan.Engine.Numerics;
using Herculan.Engine.Terrain;

namespace Herculan.Engine.Render;

/// <summary>
/// The order the original paints terrain cells in — <c>Terrain_DrawVisibleCells</c>
/// (<c>0046d0a4</c>)'s walk about the viewer's cell, by row or by column as the view's heading
/// decides, far to near (docs/retail/formats/terrain-drawing.md, "The cell walk"). Each cell's objects are
/// painted straight after its ground and under every later cell, so this is also the order that
/// decides what paints over a ground shape.
///
/// <para><see cref="Rank"/> turns the walk into a number per cell that grows with paint order, so
/// "painted later" becomes a comparison. It ranks every cell of the grid, not only those inside
/// the visible region the original walks: a cell outside it is not drawn there, and ranking it
/// changes nothing a comparison between drawn cells can see.</para>
/// </summary>
public readonly struct TerrainPaintOrder : IEquatable<TerrainPaintOrder> {
	/// <summary>The rank of a pixel no terrain was drawn at — sky — below every cell's.</summary>
	public const uint NoTerrain = 0;

	/// <summary>
	/// The rank of the original's no-cell bucket, which <c>ObjList_DrawAfterTerrain</c>
	/// (<c>0042883c</c>) draws once the walk is done: after every cell.
	/// </summary>
	public const uint AfterTerrain = uint.MaxValue;

	/// <summary>The range of cell offsets one rank field holds: a run of up to this many cells either side.</summary>
	private const int Half = 1 << 14;

	private TerrainPaintOrder(int centreX, int centreY, bool byColumn) {
		CentreX = centreX;
		CentreY = centreY;
		ByColumn = byColumn;
	}

	/// <summary>The viewer's cell X — <c>g_TerrainViewerCellX</c> (<c>006b4fd4</c>), the walk's centre, painted last.</summary>
	public int CentreX { get; }

	/// <summary>The viewer's cell Y — <c>g_TerrainViewerCellY</c> (<c>006b4fd8</c>).</summary>
	public int CentreY { get; }

	/// <summary>
	/// Whether the walk goes by column (<c>CellWalk_PolygonByColumn</c>, <c>004723f8</c>) rather than
	/// by row (<c>CellWalk_Polygon</c>, <c>00471e38</c>): when the view looks more along X than Y.
	/// </summary>
	public bool ByColumn { get; }

	/// <summary>
	/// The walk for a view at <paramref name="viewer"/> with the simulation heading
	/// <paramref name="heading"/> (the view's euler Z, <c>view+0x14</c>): by column when its
	/// magnitude, <c>−0x8000</c> read as <c>0x7fff</c>, lies in <c>0x2001</c>..<c>0x5fff</c>.
	/// </summary>
	public static TerrainPaintOrder For(HeightGrid grid, Vec3i viewer, short heading) {
		int magnitude = heading == short.MinValue ? short.MaxValue : System.Math.Abs((int)heading);
		return new TerrainPaintOrder(viewer.X >> grid.CellShift, viewer.Y >> grid.CellShift,
			magnitude >= 0x2001 && magnitude <= 0x5fff);
	}

	/// <summary>
	/// Where cell (<paramref name="cellX"/>, <paramref name="cellY"/>) falls in the walk: a larger
	/// rank is painted later. Always above <see cref="NoTerrain"/> and below
	/// <see cref="AfterTerrain"/>.
	/// </summary>
	public uint Rank(int cellX, int cellY) {
		int dx = cellX - CentreX;
		int dy = cellY - CentreY;

		// CellWalk_PolygonByColumn turns the polygon a quarter about the centre
		// (Point_RotateQuarterAbout: x' = cx + (y - cy), y' = cy - (x - cx)) and walks rows.
		return ByColumn ? RowWalkRank(dy, -dx) : RowWalkRank(dx, dy);
	}

	/// <summary>
	/// <c>CellWalk_Polygon</c>'s far-to-near branch as a rank. First the rows below the centre in
	/// its own coordinates (<c>dy ≥ 1</c>), the farthest first, each running the cells right of the
	/// centre's column from the far end in and then the rest from the far end in; then the rows
	/// <c>dy ≤ 0</c>, the farthest first, each running the cells left of the column from the far end
	/// in and then the rest from the far end in. The fields, most significant first, are the half,
	/// the row and the cell's place along it.
	/// </summary>
	private static uint RowWalkRank(int dx, int dy) {
		dx = System.Math.Clamp(dx, 1 - Half, Half - 1);
		dy = System.Math.Clamp(dy, 1 - Half, Half - 1);

		uint half, row, along;
		if (dy >= 1) {
			half = 0;
			row = (uint)(Half - dy);
			along = dx >= 1 ? (uint)(Half - dx) : (uint)(2 * Half + Half + dx);
		} else {
			half = 1;
			row = (uint)(Half + dy);
			along = dx <= -1 ? (uint)(Half + dx) : (uint)(2 * Half + Half - dx);
		}

		// row < 2 * Half and along < 4 * Half, so each field fits the width the next leaves it.
		return ((half * 2 * Half + row) * 4 * Half + along) + 1;
	}

	public bool Equals(TerrainPaintOrder other) =>
		CentreX == other.CentreX && CentreY == other.CentreY && ByColumn == other.ByColumn;

	public override bool Equals(object? obj) => obj is TerrainPaintOrder other && Equals(other);

	public override int GetHashCode() => HashCode.Combine(CentreX, CentreY, ByColumn);
}
