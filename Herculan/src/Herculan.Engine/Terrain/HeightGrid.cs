namespace Herculan.Engine.Terrain;

/// <summary>
/// Port of DBSIM's <c>HeightGrid</c> — the loaded terrain of one zone. The original is a 0x129-byte
/// struct built by <c>HeightGrid_Constructor</c> (<c>0046bdf8</c>) and installed as the global
/// <c>ActiveHeightGrid</c> by <c>Terrain_LoadZone</c> (<c>0042789c</c>); see
/// docs/retail/simulation/terrain-heightmap.md, "The HeightGrid struct", for the full field map.
///
/// <para>Storage differs from the original in one deliberate way: DBSIM allocates a single array of
/// 16-byte cells, where this holds parallel arrays. Of the 16 bytes, the loaders write <c>+0x0</c>
/// (raw height) and the material index in <c>+0xf</c>; <c>Terrain_BuildCellSurface</c> then fills in
/// the diagonal selector (also <c>+0xf</c>), the two face normals (<c>+0x1..+0xc</c>) and the two
/// baked shade bytes (<c>+0xd</c>/<c>+0xe</c>). The parallel arrays here hold the same values:
/// <c>_rawHeights</c>, <c>_cellFlags</c>, <c>_diagonals</c> and <c>_normals</c>, with the shade bytes
/// recomputed at mesh-build time instead (<see cref="Render.TerrainMeshBuilder"/>). The grid's
/// second, separate array — the <c>+0xf0</c> per-cell scratch byte — is <c>_cellScratch</c>, and
/// what writes and reads it is in <c>HeightGrid.Footprints.cs</c>. The addressing
/// (<c>index = x + (y &lt;&lt; WidthShift)</c>, row-major) and every value are unchanged, so the
/// height query below is still a literal translation.</para>
/// </summary>
public sealed partial class HeightGrid {
	/// <summary>Length every surface normal is scaled to — <c>Terrain_BuildCellSurfaceAndShade</c> (<c>0046c2ec</c>)'s own constant.</summary>
	public const int NormalOne = 0x800;

	private readonly byte[] _rawHeights;
	private readonly byte[] _cellFlags;

	// Per-cell diagonal selector and the two face normals it splits the cell into, built once at
	// construction exactly as Terrain_BuildSurface (0046c1dc) builds them at zone load. Six shorts per cell: the
	// north-west triangle's normal, then the south-east one's.
	private readonly byte[] _diagonals;
	private readonly short[] _normals;

	internal HeightGrid(int widthShift, int heightShift, int cellShift, int heightScale, int detailLod,
			byte[] rawHeights, byte[] cellFlags) {
		WidthShift = widthShift;
		HeightShift = heightShift;
		CellShift = cellShift;
		HeightScale = heightScale;
		DetailLod = detailLod;
		_rawHeights = rawHeights;
		_cellFlags = cellFlags;

		_diagonals = new byte[rawHeights.Length];
		_normals = new short[rawHeights.Length * 6];
		_cellScratch = new byte[rawHeights.Length];
		BuildSurface();
	}

	/// <summary>log2 of the grid width in cells (<c>+0x100</c>). Re-derived from the heightmap image.</summary>
	public int WidthShift { get; }

	/// <summary>log2 of the grid height in cells (<c>+0x104</c>). Re-derived from the heightmap image.</summary>
	public int HeightShift { get; }

	/// <summary>
	/// log2 of world units per cell (<c>+0x108</c>), and the shift that converts a world (x,y) to a
	/// cell (x,y). Comes from the per-zone <c>.DAT</c> header; 14 (16384 units per cell) in every
	/// retail zone checked.
	/// </summary>
	public int CellShift { get; }

	/// <summary>
	/// Additive height offset (<c>+0x110</c>). Always 0 for the real binary zone format — the
	/// loader sets it to 0xff as a working value and zeroes it on completion. Nonzero only in the
	/// ASCII debug format, which no retail data uses.
	/// </summary>
	public int HeightBase => 0;

	/// <summary>Multiplicative height scale applied to each cell's raw byte (<c>+0x118</c>), from the zone header.</summary>
	public int HeightScale { get; }

	/// <summary>
	/// The value at <c>+0x10c</c>, set once at load from the terrain-detail setting — see
	/// <see cref="TerrainDetail"/>, which owns the table and the derivation.
	/// <c>Terrain_HeightQuery</c> never reads it — it is the <b>view radius in cells</b>, and the
	/// consumer this engine has ported is <c>Terrain_DrawCellQuad</c>, which per cell does
	/// <c>Raster_SetVisibilityRange(grid[0x10c] &lt;&lt; grid[0x108])</c> (<c>00467fdc</c>) to install the visibility range the
	/// distance fog is measured against (see <see cref="VisibilityRange"/>). The field's writer and
	/// its other readers — draw-region and view-distance setup — are in
	/// docs/retail/rendering/terrain-texturing.md's "<c>grid+0x10c</c> — the LOD / draw-radius field".
	/// </summary>
	public int DetailLod { get; }

	/// <summary>
	/// How far the original draws and fogs, in world units: <see cref="DetailLod"/> cells converted
	/// to world units by <see cref="CellShift"/>, exactly as <c>Terrain_DrawCellQuad</c> computes it.
	/// Distance fog begins at half this and is total at it — see
	/// <see cref="Content.ShadeRamp.DepthSliceFor"/>.
	///
	/// <para>Retail zones are shift 12 to 15, so at the highest detail setting this runs from 57344
	/// world units (344 m at <see cref="World.WorldScale.WorldUnitsPerMeter"/>, the one shift-12
	/// zone) to 229376 (1376 m, the 26 zones at shift 14). The lowest setting is 6/14ths of that.
	/// Nothing in the format normalises it: a zone with small cells is simply seen less far
	/// across.</para>
	/// </summary>
	public long VisibilityRange => (long)DetailLod << CellShift;

	/// <summary>Grid width in cells.</summary>
	public int Width => 1 << WidthShift;

	/// <summary>Grid height in cells.</summary>
	public int Height => 1 << HeightShift;

	/// <summary>World units spanned by one cell.</summary>
	public int CellSize => 1 << CellShift;

	/// <summary>World units spanned by the whole zone along X.</summary>
	public long WorldWidth => (long)Width << CellShift;

	/// <summary>World units spanned by the whole zone along Y.</summary>
	public long WorldHeight => (long)Height << CellShift;

	/// <summary>
	/// Highest world-space height anywhere in the grid — the original's <c>+0x114</c>, which the
	/// loader tracks as a running max of raw bytes and multiplies by <see cref="HeightScale"/> on
	/// completion.
	/// </summary>
	public int MaxWorldHeight { get; internal set; }

	/// <summary>Flat cell index for a cell coordinate, matching the original's row-major addressing.</summary>
	public int CellIndex(int cellX, int cellY) => cellX + (cellY << WidthShift);

	/// <summary>Raw 0–255 heightmap byte for a cell.</summary>
	public byte RawHeightAt(int cellX, int cellY) => _rawHeights[CellIndex(cellX, cellY)];

	/// <summary>World-space height of a cell corner: <c>raw * HeightScale + HeightBase</c>.</summary>
	public int WorldHeightAt(int cellX, int cellY) => _rawHeights[CellIndex(cellX, cellY)] * HeightScale + HeightBase;

	/// <summary>
	/// The cell's diagonal-split selector — bits [0:1] of the original's <c>+0xf</c> byte, which
	/// <see cref="HeightAtWorld"/> uses to decide which way the quad's diagonal runs.
	///
	/// <para>Neither loader path writes it. The writer is <c>Terrain_BuildCellSurface</c> (<c>0046bed8</c>), the per-cell
	/// <b>normal builder</b>: computing a cell's two face normals requires choosing its diagonal, so
	/// it derives the selector from the four corner heights and stores it alongside.
	/// <c>Terrain_BuildSurface</c> (<c>0046c1dc</c>) runs that over the whole grid at zone load, and again after footprint
	/// flattening moves the corners it reads, so every interior cell's selector is decided by its
	/// own corners and only the last row and column keep the loader's zero. See
	/// <see cref="BuildSurface"/> and <see cref="FlattenStructureFootprints"/>.</para>
	/// </summary>
	public int DiagonalSelectorAt(int cellX, int cellY) => _diagonals[CellIndex(cellX, cellY)];

	/// <summary>
	/// The surface normal of the triangle under a world position, scaled to length
	/// <see cref="NormalOne"/> — <c>Terrain_FaceNormalAt</c> (<c>0046e394</c>), which picks between the cell's two face
	/// normals using the same diagonal split the height query uses. Null outside the grid, where the
	/// original returns a null pointer and its callers skip their slope terms entirely.
	/// </summary>
	public (short X, short Y, short Z)? SurfaceNormalAt(int worldX, int worldY) {
		int cellX = worldX >> CellShift;
		int cellY = worldY >> CellShift;

		if (cellX < 0 || cellX >= 1 << WidthShift || cellY < 0 || cellY >= 1 << HeightShift) {
			return null;
		}

		int fracX = worldX - (cellX << CellShift);
		int fracY = worldY - (cellY << CellShift);
		int cell = cellX + (cellY << WidthShift);

		// The far triangle is chosen by the same comparison the height query makes, which differs
		// between the two diagonal directions.
		bool farTriangle = _diagonals[cell] == 2
			? fracY < fracX
			: (1 << CellShift) - fracX < fracY;

		int at = cell * 6 + (farTriangle ? 3 : 0);
		return (_normals[at], _normals[at + 1], _normals[at + 2]);
	}

	/// <summary>
	/// <c>Terrain_BuildCellSurface</c> (<c>0046bed8</c>) + <c>Math_NormalizeVec3ShortToLength</c> (<c>0046c138</c>), run over the grid as <c>Terrain_BuildSurface</c> (<c>0046c1dc</c>) does at
	/// zone load: for every cell, choose the diagonal its four corner heights imply and build the
	/// two face normals, each scaled to length <see cref="NormalOne"/>.
	///
	/// <para>The normals are built in <i>raw height units</i>, not world units — the horizontal
	/// components are plain corner differences and the vertical one is
	/// <c>cellSize / HeightScale</c> — which is the same vector as a true cross product divided
	/// through by <c>HeightScale</c>. Both are then doubled before normalising, which changes
	/// nothing but is reproduced anyway.</para>
	///
	/// <para>The last row and column are skipped, as in the original: they have no east/north
	/// neighbour to difference against, so they keep a flat <c>(0, 0, NormalOne)</c> normal and a
	/// zero selector.</para>
	/// </summary>
	private void BuildSurface() {
		int width = 1 << WidthShift;
		int height = 1 << HeightShift;
		short flatZ = (short)NormalOne;

		for (int cellY = 0; cellY < height; cellY++) {
			for (int cellX = 0; cellX < width; cellX++) {
				int cell = cellX + (cellY << WidthShift);
				int at = cell * 6;

				if (cellX >= width - 1 || cellY >= height - 1) {
					_normals[at + 2] = flatZ;
					_normals[at + 5] = flatZ;
					continue;
				}

				int corner00 = _rawHeights[cell];
				int corner01 = _rawHeights[cellX + ((cellY + 1) << WidthShift)];
				int corner11 = _rawHeights[cellX + 1 + ((cellY + 1) << WidthShift)];
				int corner10 = _rawHeights[cellX + 1 + (cellY << WidthShift)];

				short nearX, nearY, farX, farY;
				if (corner00 + corner11 == corner01 + corner10) {
					// A planar quad: both triangles share one normal, and the selector says so.
					_diagonals[cell] = 1;
					nearX = farX = (short)(corner00 - corner10);
					nearY = farY = (short)(corner00 - corner01);
				} else if (corner00 + corner11 - (corner01 + corner10) < 1) {
					_diagonals[cell] = 2;
					nearX = (short)(corner01 - corner11);
					nearY = (short)(corner00 - corner01);
					farX = (short)(corner00 - corner10);
					farY = (short)(corner10 - corner11);
				} else {
					_diagonals[cell] = 0;
					nearX = (short)(corner00 - corner10);
					nearY = (short)(corner00 - corner01);
					farX = (short)(corner01 - corner11);
					farY = (short)(corner10 - corner11);
				}

				short verticalRun = (short)((1 << CellShift) / HeightScale);
				var near = Normalize((short)(nearX << 1), (short)(nearY << 1), (short)(verticalRun << 1));
				var far = Normalize((short)(farX << 1), (short)(farY << 1), (short)(verticalRun << 1));

				_normals[at] = near.X;
				_normals[at + 1] = near.Y;
				_normals[at + 2] = near.Z;
				_normals[at + 3] = far.X;
				_normals[at + 4] = far.Y;
				_normals[at + 5] = far.Z;
			}
		}
	}

	/// <summary>
	/// <c>Math_NormalizeVec3ShortToLength</c> (<c>0046c138</c>) — rescales a vector to length <see cref="NormalOne"/>, measured with the
	/// sim's sqrt-free magnitude approximation, halving it first if it is close enough to overflowing
	/// a signed short that the Q16 scale would.
	/// </summary>
	private static (short X, short Y, short Z) Normalize(short x, short y, short z) {
		while ((short)Numerics.SimMath.FastMagnitude3D(x, y, z) >= 0x7d01) {
			x >>= 1;
			y >>= 1;
			z >>= 1;
		}

		int scale = (NormalOne << 16) / (short)Numerics.SimMath.FastMagnitude3D(x, y, z);
		return (
			(short)Numerics.SimMath.Q16Multiply(x, scale),
			(short)Numerics.SimMath.Q16Multiply(y, scale),
			(short)Numerics.SimMath.Q16Multiply(z, scale));
	}

	/// <summary>
	/// The cell's material/detail-type index — bits [2:7] of the original's <c>+0xf</c> byte, an
	/// index into the shared <c>dat\mat0</c> table. Purely a detail-texture selector; nothing in
	/// the height query or collision reads it.
	/// </summary>
	public int MaterialIndexAt(int cellX, int cellY) => _cellFlags[CellIndex(cellX, cellY)] >> 2;

	/// <summary>
	/// Literal port of <c>Terrain_HeightQuery</c> (<c>0046e07c</c>): converts a world (x, y) to a
	/// grid cell, takes that cell's four corner heights, and interpolates barycentrically across
	/// whichever of the cell's two triangles the point falls in — with the split direction chosen
	/// per cell by <see cref="DiagonalSelectorAt"/>, not fixed grid-wide. Returns 0 for points
	/// outside the grid, same as the original.
	///
	/// <para>Two faithfulness notes worth keeping visible. First, all the interpolation arithmetic
	/// stays in integers with <c>>> CellShift</c> divides, so the result quantizes exactly the way
	/// the original's does — this feeds ballistic ground-impact and the flyer terrain-avoidance
	/// autopilot, where a float rewrite would silently shift impact points. Second, the original
	/// bounds-checks only the *flat* cell index of each of the four corners, not the cell x+1
	/// separately, so a query in the last column reads the next row's first cell as its east
	/// neighbour. That wrap is reproduced rather than fixed: it is exactly the kind of original-game
	/// quirk docs/herculan/planning.md's "vanilla by default" principle says to keep by default.</para>
	/// </summary>
	public int HeightAtWorld(int worldX, int worldY) {
		int cellX = worldX >> CellShift;
		int cellY = worldY >> CellShift;

		if (cellX < 0 || cellX >= (1 << WidthShift) || cellY < 0 || cellY >= (1 << HeightShift)) {
			return 0;
		}

		int cellSize = 1 << CellShift;
		int fracX = worldX - (cellX << CellShift);
		int fracY = worldY - (cellY << CellShift);

		int i00 = Corner(cellX + (cellY << WidthShift));
		int i01 = Corner(cellX + ((cellY + 1) << WidthShift));
		int i11 = Corner(cellX + ((cellY + 1) << WidthShift) + 1);
		int i10 = Corner(cellX + (cellY << WidthShift) + 1);

		if (i00 < 0 || i01 < 0 || i11 < 0 || i10 < 0) {
			return 0;
		}

		int h00 = _rawHeights[i00] * HeightScale + HeightBase;
		int h01 = _rawHeights[i01] * HeightScale + HeightBase;
		int h11 = _rawHeights[i11] * HeightScale + HeightBase;
		int h10 = _rawHeights[i10] * HeightScale + HeightBase;

		int diagonal = _diagonals[i00];

		if (diagonal == 2) {
			// Split along the (0,0)-(1,1) diagonal.
			if (fracY < fracX) {
				return ((h00 - h10) * (cellSize - fracX) >> CellShift) + h10
					 + ((h11 - h10) * fracY >> CellShift);
			}
			return ((h00 - h01) * (cellSize - fracY) >> CellShift) + h01
				 + ((h11 - h01) * fracX >> CellShift);
		}

		if (diagonal == 0) {
			// Split along the (0,1)-(1,0) anti-diagonal.
			if (cellSize - fracX < fracY) {
				return ((h01 - h11) * (cellSize - fracX) >> CellShift) + h11
					 + ((h10 - h11) * (cellSize - fracY) >> CellShift);
			}
			return ((h10 - h00) * fracX >> CellShift) + h00
				 + ((h01 - h00) * fracY >> CellShift);
		}

		// Selectors 1 and 3 fall through to the plane through the (0,0)/(1,0)/(0,1) corners with no
		// triangle test at all — which is exact for selector 1, the coplanar quad BuildSurface writes.
		// Selector 3 has no observed producer; the original handles it, so this branch does too.
		return ((h10 - h00) * fracX >> CellShift) + h00
			 + ((h01 - h00) * fracY >> CellShift);
	}

	/// <summary>
	/// The original's per-corner bounds test: a flat cell index is valid only within
	/// <c>1 &lt;&lt; (WidthShift + HeightShift)</c>. Returns -1 for "out of range", where the
	/// original produces a null pointer and then bails out of the whole query.
	/// </summary>
	private int Corner(int index) =>
		index < 0 || index >= 1 << (WidthShift + HeightShift) ? -1 : index;
}
