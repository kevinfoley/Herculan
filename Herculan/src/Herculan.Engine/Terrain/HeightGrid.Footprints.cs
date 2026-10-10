namespace Herculan.Engine.Terrain;

/// <summary>
/// The structure-footprint flattening pass — why a turret stands on a flat-topped mound rather than
/// beside the point of a pyramid. A zone's heightmap marks each ground vehicle with one raised sample,
/// which as a corner sample is an apex at the cell's corner rather than a mound at its centre;
/// DBSIM levels the marked cells to their own average at spawn time instead. The derivation, the
/// data evidence and the call chain are in docs/retail/simulation/terrain-heightmap.md, "Structure footprints
/// — the flattening pass"; this file is the port of <c>Terrain_MarkStructureFootprint</c> (<c>00470dc8</c>) and <c>Terrain_FlattenStructureFootprints</c> (<c>00471190</c>) and
/// the two recursions they drive.
/// </summary>
public sealed partial class HeightGrid {
	private const byte FootprintMarked = 1;
	private const byte FootprintCounted = 2;
	private const byte FootprintWritten = 4;

	/// <summary>
	/// The original's <c>+0xf0</c> array: one byte per cell, parallel to the cell records.
	///
	/// <para>Here it belongs to this pass alone and is never cleared, which is safe only because the
	/// engine has no per-cell object dispatch. The original reuses the same bytes every frame as a
	/// pending-object count and gets away with it by <c>memset</c>ting the array at the top of each
	/// frame — anything added here that counts into these bytes needs that reset first, or it will
	/// read a flattened base's footprint marks as counts. See docs/retail/simulation/terrain-heightmap.md,
	/// "Structure footprints — the flattening pass".</para>
	/// </summary>
	private readonly byte[] _cellScratch;

	private int _footprintSum;
	private int _footprintCount;

	/// <summary>
	/// <c>Terrain_MarkStructureFootprint</c> (<c>00470dc8</c>) — registers one structure's
	/// footprint. <paramref name="radius"/> is <see cref="Sim.SimObject.ShapeRadius"/>.
	///
	/// <para>Clamped to the grid, where the original bounds-checks nothing beyond refusing to step to
	/// a negative index: a structure near a zone edge would write past the array there, and an
	/// out-of-bounds write is a crash rather than behaviour worth reproducing.</para>
	/// </summary>
	public void MarkStructureFootprint(int worldX, int worldY, int radius) {
		int cellX = worldX >> CellShift;
		int cellY = worldY >> CellShift;
		SetScratch(cellX, cellY, FootprintMarked);

		int minX = (worldX - radius) >> CellShift;
		int maxX = (worldX + radius) >> CellShift;
		int minY = (worldY - radius) >> CellShift;
		int maxY = (worldY + radius) >> CellShift;

		for (int y = minY; y <= maxY; y++) {
			for (int x = minX; x <= maxX; x++) {
				int dx = (x << CellShift) - worldX;
				int dy = (y << CellShift) - worldY;
				if (Numerics.SimMath.FastMagnitude3D(dx, dy, 0) > radius) {
					continue;
				}

				SetScratch(x, y, FootprintMarked);
				if (x > 0) {
					SetScratch(x - 1, y, FootprintMarked);
					if (y > 0) {
						SetScratch(x - 1, y - 1, FootprintMarked);
					}
				}

				if (y > 0) {
					SetScratch(x, y - 1, FootprintMarked);
				}
			}
		}
	}

	/// <summary>
	/// <c>Terrain_FlattenStructureFootprints</c> (<c>00471190</c>) — flattens every marked
	/// footprint and rebuilds the surface. Run once, after the whole roster is placed; the structures
	/// standing on it must then be re-settled, which is what <c>DBSim_SpawnMissionObjects</c> does
	/// with its own base list the moment this returns.
	///
	/// <para>Walks to the grid's own height where the original walks both axes to
	/// <c>1 &lt;&lt; WidthShift</c> — a latent bug that cannot fire, every retail zone being
	/// square.</para>
	/// </summary>
	public void FlattenStructureFootprints() {
		int width = 1 << WidthShift;
		int height = 1 << HeightShift;

		for (int y = 0; y < height; y++) {
			for (int x = 0; x < width; x++) {
				byte flags = _cellScratch[CellIndex(x, y)];
				if ((flags & FootprintMarked) == 0 || (flags & FootprintCounted) != 0) {
					continue;
				}

				_footprintSum = 0;
				_footprintCount = 0;
				AccumulateFootprint(x, y);
				WriteFootprintHeight(x, y, (byte)(_footprintSum / _footprintCount), force: false);
			}
		}

		BuildSurface();
	}

	/// <summary>
	/// <c>Terrain_AccumulateFootprint</c> (<c>00470edc</c>) — the eight-way flood fill that
	/// measures one footprint: sums the raw heights of every connected marked cell and counts them.
	/// </summary>
	private void AccumulateFootprint(int cellX, int cellY) {
		if (cellX < 0 || cellX >= 1 << WidthShift || cellY < 0 || cellY >= 1 << HeightShift) {
			return;
		}

		int cell = CellIndex(cellX, cellY);
		byte flags = _cellScratch[cell];
		if ((flags & FootprintMarked) == 0 || (flags & FootprintCounted) != 0) {
			return;
		}

		_cellScratch[cell] = (byte)(flags | FootprintCounted);
		_footprintCount++;
		_footprintSum += _rawHeights[cell];

		AccumulateFootprint(cellX - 1, cellY + 1);
		AccumulateFootprint(cellX, cellY + 1);
		AccumulateFootprint(cellX + 1, cellY + 1);
		AccumulateFootprint(cellX - 1, cellY);
		AccumulateFootprint(cellX + 1, cellY);
		AccumulateFootprint(cellX - 1, cellY - 1);
		AccumulateFootprint(cellX, cellY - 1);
		AccumulateFootprint(cellX + 1, cellY - 1);
	}

	/// <summary>
	/// <c>Terrain_WriteFootprintHeight</c> (<c>0047101c</c>) — writes the footprint's average
	/// height back over the same region. <paramref name="force"/> is what gains the region one sample
	/// to the east and north, and so turns it into a flat <i>quad</i> rather than a flat set of corner
	/// samples.
	/// </summary>
	private void WriteFootprintHeight(int cellX, int cellY, byte height, bool force) {
		if (cellX < 0 || cellX >= 1 << WidthShift || cellY < 0 || cellY >= 1 << HeightShift) {
			return;
		}

		int cell = CellIndex(cellX, cellY);
		byte flags = _cellScratch[cell];

		if (force) {
			_rawHeights[cell] = height;
		}

		if ((flags & FootprintMarked) == 0 || (flags & FootprintWritten) != 0) {
			return;
		}

		_cellScratch[cell] = (byte)(flags | FootprintWritten);
		_rawHeights[cell] = height;

		WriteFootprintHeight(cellX - 1, cellY + 1, height, force: false);
		WriteFootprintHeight(cellX, cellY + 1, height, force: true);
		WriteFootprintHeight(cellX + 1, cellY + 1, height, force: true);
		WriteFootprintHeight(cellX - 1, cellY, height, force: false);
		WriteFootprintHeight(cellX + 1, cellY, height, force: true);
		WriteFootprintHeight(cellX - 1, cellY - 1, height, force: false);
		WriteFootprintHeight(cellX, cellY - 1, height, force: false);
		WriteFootprintHeight(cellX + 1, cellY - 1, height, force: false);
	}

	/// <summary>
	/// <c>Terrain_SetCellScratch</c> (<c>00470cd0</c>), clamped to the grid — see
	/// <see cref="MarkStructureFootprint"/>.
	/// </summary>
	private void SetScratch(int cellX, int cellY, byte value) {
		if (cellX < 0 || cellX >= 1 << WidthShift || cellY < 0 || cellY >= 1 << HeightShift) {
			return;
		}

		_cellScratch[CellIndex(cellX, cellY)] = value;
	}
}
