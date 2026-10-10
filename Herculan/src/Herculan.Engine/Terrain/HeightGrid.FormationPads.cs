namespace Herculan.Engine.Terrain;

/// <summary>
/// The base-formation terrain pass — why a retail base stands on a concrete pad with painted
/// markings rather than on open ground. A base group whose <c>script.dat</c> block-11 record sets
/// its <c>PaintsGround</c> stamps its formation's own material over a tile of terrain cells, which
/// draws that material's <c>dat\mat0</c> frame — one of the eleven pad layouts frames 2-12 of a
/// theater bank carry — in place of the rolled ground texture.
///
/// <para>Port of <c>Terrain_PaintFormationPad</c> (<c>00471260</c>), reached from
/// <c>Base_ApplyFormationTerrain</c> (<c>00405db0</c>), which
/// <c>DBSim_SpawnMissionObjects</c> (<c>004253d8</c>) calls for each base group immediately after
/// building its group record. It is also the <i>second</i> input to the flattening pass in
/// <c>HeightGrid.Footprints.cs</c>: alongside recolouring, it marks the cells the formation's
/// layout map calls occupied. <see cref="MarkStructureFootprint"/> supplies the other input, per
/// object and by shape radius.</para>
///
/// <para>The tile geometry, why the layout map is a levelling mask rather than the pad's shape,
/// what the two inputs add up to on the ground, and the evidence for all of it are in
/// docs/retail/rendering/terrain-texturing.md, "Base formation pads".</para>
/// </summary>
public sealed partial class HeightGrid {
	/// <summary>
	/// Paints one base formation's material over the terrain tile <paramref name="worldX"/>,
	/// <paramref name="worldY"/> falls in.
	///
	/// <para>The tile is the extent one <c>mat0</c> frame covers at
	/// <paramref name="blockShift"/> — <c>1 &lt;&lt; (0x15 - blockShift)</c> world units square,
	/// independent of <see cref="CellShift"/> — and <paramref name="dimension"/> is the side of the
	/// formation's layout map, which spans exactly that tile. So a map entry is
	/// <c>1 &lt;&lt; (CellShift - 13)</c> per cell along each axis: one map entry per cell at cell
	/// shift 13, a 2x2 block of entries per cell at 14.</para>
	///
	/// <para><b>Rows run north to south.</b> The original indexes row 0 at the tile's <i>high</i>-y
	/// edge and counts down.</para>
	///
	/// <para>Cells outside the grid are skipped, where the original clamps an out-of-range index to
	/// zero and paints cell 0 instead — the same deviation, and for the same reason, as
	/// <see cref="MarkStructureFootprint"/>.</para>
	/// </summary>
	/// <param name="materialIndex">
	/// The formation's <c>dat\mat0</c> index. Its <c>BlockShift</c> is
	/// <paramref name="blockShift"/> and its <c>Index</c> is the bank frame that gets drawn.
	/// </param>
	/// <param name="dimension">The layout map's side, 8 or 16 in retail data.</param>
	/// <param name="map">
	/// <paramref name="dimension"/> rows of <paramref name="dimension"/> 0/1 bytes, row 0 first.
	/// Every cell of the tile is painted regardless; a nonzero entry additionally marks its cell for
	/// <see cref="FlattenStructureFootprints"/>. Several map entries can share one cell, in which
	/// case any one of them marking it is enough.
	/// </param>
	public void PaintFormationPad(int worldX, int worldY, int materialIndex, int blockShift,
			int dimension, ReadOnlySpan<byte> map) {
		// Map entries per cell along one axis, as a shift: the map is expressed at the material's
		// own block size, the grid at its cell size, and only their ratio matters here.
		int entryShift = CellShift - 13;
		if (entryShift < 0 || dimension <= 0) {
			return;
		}

		// World -> tile, then tile -> the cell index its low corner sits at.
		int tileShift = 0x15 - blockShift;
		int cellsShift = 8 - blockShift - entryShift;
		if (tileShift < 0 || cellsShift < 0) {
			return;
		}

		int width = 1 << WidthShift;
		int height = 1 << HeightShift;
		int originX = (worldX >> tileShift) << cellsShift;
		int topY = (((worldY >> tileShift) + 1) << cellsShift) - 1;

		for (int row = 0; row < dimension; row++) {
			int cellY = topY - (row >> entryShift);
			if (cellY < 0 || cellY >= height) {
				continue;
			}

			for (int col = 0; col < dimension; col++) {
				int cellX = originX + (col >> entryShift);
				if (cellX < 0 || cellX >= width) {
					continue;
				}

				SetMaterialIndex(cellX, cellY, materialIndex);

				if (map[row * dimension + col] != 0) {
					SetScratch(cellX, cellY, FootprintMarked);
				}
			}
		}
	}

	/// <summary>
	/// Writes a cell's material index — bits [2:7] of the original's <c>+0xf</c> byte, the field
	/// <see cref="MaterialIndexAt"/> reads back. The diagonal selector the original keeps in the
	/// same byte lives in its own array here, so nothing else in the byte needs preserving.
	/// </summary>
	private void SetMaterialIndex(int cellX, int cellY, int materialIndex) =>
		_cellFlags[CellIndex(cellX, cellY)] = (byte)(materialIndex << 2);
}
