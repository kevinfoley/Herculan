using Herculan.Engine.Numerics;

namespace Herculan.Engine.Terrain;

public sealed partial class HeightGrid {
	/// <summary>
	/// <c>HeightGrid_PickDrawCell</c> (<c>0046e528</c>) — the terrain cell an object is filed under
	/// for drawing, which decides where the terrain walk paints it: its own cell, or the next one
	/// toward the viewer on either axis when <paramref name="radius"/> reaches over the edge the two
	/// share. The move is undone on both axes when the terrain face under
	/// <paramref name="position"/> turns away from <paramref name="viewer"/>, and on each axis whose
	/// new cell lies one past <paramref name="region"/>'s bounding box. Null off the grid, which is
	/// the original's no-cell bucket. See docs/retail/rendering/terrain-drawing.md,
	/// "<c>HeightGrid_PickDrawCell</c>".
	/// </summary>
	public (int X, int Y)? PickDrawCell(Vec3i position, int radius, Vec3i viewer,
			TerrainVisibleRegion region) {
		int cellX = position.X >> CellShift;
		int cellY = position.Y >> CellShift;
		int viewerX = viewer.X >> CellShift;
		int viewerY = viewer.Y >> CellShift;

		// The original sorts the object's cell into one of four quadrants about the viewer's and
		// writes out both moves per quadrant, but each axis's move depends only on that axis's side,
		// which is how it is written here.
		bool east = viewerX < cellX;
		bool north = viewerY < cellY;

		int x = cellX;
		int y = cellY;

		if (east) {
			if (position.X < (cellX << CellShift) + radius) {
				x = cellX - 1;
			}
		} else if (((cellX + 1) << CellShift) - radius < position.X && cellX < viewerX) {
			x = cellX + 1;
		}

		if (north) {
			if (position.Y < (cellY << CellShift) + radius) {
				y = cellY - 1;
			}
		} else if (((cellY + 1) << CellShift) - radius < position.Y && cellY < viewerY) {
			y = cellY + 1;
		}

		if (x != cellX || y != cellY) {
			if (SurfaceNormalAt(position.X, position.Y) is { } normal
					&& !FaceTurnsToViewer(normal, position, viewer)) {
				x = cellX;
				y = cellY;
			}

			if (x == unchecked(region.MinCellX - 1) || x == unchecked(region.MaxCellX + 1)) {
				x = cellX;
			}

			if (y == unchecked(region.MinCellY - 1) || y == unchecked(region.MaxCellY + 1)) {
				y = cellY;
			}
		}

		if (x < 0 || x >= Width || y < 0 || y >= Height) {
			return null;
		}

		return (x, y);
	}

	/// <summary>
	/// <c>Terrain_FaceVisibilityTest</c> (<c>0046bd98</c>) — whether a terrain face is turned toward
	/// the viewer: <c>normal · (viewer − point) &gt; 0</c>, the dot product in 32 bits.
	/// </summary>
	private static bool FaceTurnsToViewer((short X, short Y, short Z) normal, Vec3i point, Vec3i viewer) =>
		unchecked(normal.X * (viewer.X - point.X) + normal.Y * (viewer.Y - point.Y)
			+ normal.Z * (viewer.Z - point.Z)) > 0;
}
