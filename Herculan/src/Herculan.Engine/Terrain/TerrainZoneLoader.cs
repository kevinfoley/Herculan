using HercWorks.Core.Data.File.Dyn;
using HercWorks.Core.Io.Transform.Common;
using Herculan.Engine.Content;
using Herculan.Engine.Numerics;

namespace Herculan.Engine.Terrain;

/// <summary>
/// Port of DBSIM's zone-loading pipeline — <c>Terrain_LoadZone</c> (<c>0042789c</c>),
/// <c>TerrainZone_LoadHeightmap</c> (<c>0046c650</c>) and <c>TerrainZone_PopulateFromBitmap</c>
/// (<c>0046c3c0</c>) — producing a ready <see cref="HeightGrid"/>. See
/// docs/retail/formats/zone-terrain.md for the byte-level verification of each step against the
/// real files in <c>ES2/VOL/ZONES.VOL</c>, and docs/retail/simulation/terrain-heightmap.md for the
/// grid it builds.
///
/// <para>Two files per zone, both keyed off the same <c>zoneNNNN</c> base name the original builds
/// with <c>_itoa</c>:</para>
/// <list type="number">
/// <item><c>dat\zoneNNNN.dat</c> — the 16-byte <see cref="HercWorks.Core.Data.File.Dat.Sim.ZoneDat"/>
/// header, of which the original keeps the cell shift and height scale.</item>
/// <item><c>dba\zoneNNNN.dba</c> — an ordinary <see cref="DynamixBitmapArray"/> holding a single
/// 8-bit image, the same container format as any other texture in the game. Its pixels
/// <i>are</i> the heightmap.</item>
/// </list>
///
/// <para>The ASCII <c>fopen</c>/<c>fscanf</c> fallback path in <c>TerrainZone_LoadHeightmap</c> (for
/// any extension other than <c>.dba</c>) is not ported: no loose files in that format exist in
/// retail data, and it is almost certainly a level-design/debug-only path.</para>
/// </summary>
public static class TerrainZoneLoader {
	/// <summary>Resource folder holding the per-zone 16-byte header.</summary>
	public const string HeaderFolder = "dat";

	/// <summary>Resource folder holding the per-zone heightmap image.</summary>
	public const string HeightmapFolder = "dba";

	/// <summary>
	/// The pixel bias subtracted from every heightmap byte. <c>Terrain_LoadZone</c> passes a
	/// literal <c>'\0'</c> for it on the retail path, so it is 0 for every real zone; it stays a
	/// parameter here only because the original threads it through as one.
	/// </summary>
	public const byte DefaultHeightBias = 0;

	/// <summary>
	/// Loads zone <paramref name="zoneIndex"/> (e.g. 504 for <c>ZONE504</c>) from mounted content.
	/// <paramref name="materials"/> supplies the shared <c>dat\mat0</c> table, whose first record
	/// sizes the material-roll block (see <see cref="TerrainMaterial.BlockShift"/>);
	/// <paramref name="random"/> drives the roll itself.
	/// </summary>
	/// <param name="detailLevel">
	/// The terrain-detail setting the zone's draw radius comes from — see <see cref="TerrainDetail"/>.
	/// </param>
	public static HeightGrid Load(GameContent content, int zoneIndex, TerrainMaterialTable materials,
			SimRandom random, byte heightBias = DefaultHeightBias,
			int detailLevel = TerrainDetail.DefaultLevel) {
		string baseName = $"zone{zoneIndex}";

		byte[] bytes = content.ReadRequired(HeaderFolder, baseName + ".dat");
		var header = new ZoneDatTransformer().Parse(bytes)
			?? throw new InvalidDataException(
				$"{HeaderFolder}\\{baseName}.dat is {bytes.Length} bytes; the zone header is {ZoneDatTransformer.Size}.");

		// The width/height shifts are not used. The original reads them into locals and then
		// discards them — TerrainZone_PopulateFromBitmap re-derives both from the heightmap image's
		// own dimensions rather than trusting these copies.
		int cellShift = header.CellShift;
		int heightScale = header.HeightScale;

		var bitmap = ReadHeightmapImage(content, baseName);

		return PopulateFromBitmap(bitmap, cellShift, heightScale, materials, random, heightBias,
			detailLevel);
	}

	/// <summary>
	/// Opens <c>dba\zoneNNNN.dba</c> and returns its single image. In the original this goes
	/// through the generic <c>ClassItem_LoadResource</c> registry dispatch — the same polymorphic
	/// loader used for <c>.DFN</c>/<c>.HFN</c>/<c>.DCI</c> — which resolves to the ordinary
	/// DynamixBitmapArray reader; here that dispatch collapses to calling the already-ported
	/// transformer directly.
	/// </summary>
	private static DynamixBitmap ReadHeightmapImage(GameContent content, string baseName) {
		byte[] bytes = content.ReadRequired(HeightmapFolder, baseName + ".dba");

		var array = new DynamixBitmapArrayTransformer().Parse(bytes) as DynamixBitmapArray
			?? throw new InvalidDataException($"{HeightmapFolder}\\{baseName}.dba is not a DynamixBitmapArray.");

		if (array.Images is not { Length: > 0 } images || images[0] is not { } image) {
			throw new InvalidDataException($"{HeightmapFolder}\\{baseName}.dba contains no image.");
		}

		if (image.ImageData == null || image.Cols <= 0 || image.Rows <= 0) {
			throw new InvalidDataException($"{HeightmapFolder}\\{baseName}.dba's image has no pixel data.");
		}

		return image;
	}

	/// <summary>
	/// Port of <c>TerrainZone_PopulateFromBitmap</c>. Walks the heightmap image row by row, writing
	/// each pixel (minus the bias) into a cell's raw height, and assigns each cell's material index.
	///
	/// <para>Two details that only show up in the disassembly and matter here. The grid is stored
	/// <b>vertically flipped</b> relative to the image: image row 0 becomes the grid's highest cell
	/// row, which is what makes a heightmap authored as a top-down picture line up with a
	/// world whose +Y runs north. And the material roll is <b>sparse</b> — only cells on a block
	/// boundary roll, and every other cell in that block copies the roll's result, so terrain detail
	/// comes in patches rather than per-cell noise.</para>
	///
	/// <para>The roll itself is <c>~30%</c> (<c>rand() &amp; 0xfff &lt; 0x4ce</c>) for material 1,
	/// otherwise material 0 — note the bitmap path hardcodes a ceiling of two materials, unlike the
	/// ASCII fallback which loops over the whole <c>mat0</c> table. The diagonal-selector bits
	/// (<see cref="HeightGrid.DiagonalSelectorAt"/>) are deliberately <i>not</i> written here: every
	/// assignment in this function masks the flag byte with <c>&amp; 2</c>, preserving bit 1 and
	/// clearing bit 0, and the cells arrive freshly zeroed. The writer is the per-cell normal builder
	/// that runs after loading (<c>Terrain_BuildCellSurface</c> (<c>0046bed8</c>), driven over the whole grid by
	/// <c>Terrain_BuildSurface</c> (<c>0046c1dc</c>)), which derives each cell's diagonal from its own corner heights —
	/// see <see cref="HeightGrid"/>, which does the same in its constructor.</para>
	/// </summary>
	private static HeightGrid PopulateFromBitmap(DynamixBitmap image, int cellShift, int heightScale,
			TerrainMaterialTable materials, SimRandom random, byte heightBias, int detailLevel) {
		// Cols drives the width shift and Rows the height shift, matching the original's reads at
		// bitmap offsets +6 and +4 respectively. Every retail zone is square (128x128 or 256x256),
		// so no real data distinguishes the two — worth knowing if a non-square zone ever turns up.
		int widthShift = CeilLog2(image.Cols);
		int heightShift = CeilLog2(image.Rows);

		int width = 1 << widthShift;
		int height = 1 << heightShift;
		int cellCount = width * height;

		byte[] pixels = image.ImageData!;
		if (pixels.Length < cellCount) {
			throw new InvalidDataException(
				$"Zone heightmap is {image.Cols}x{image.Rows} with {pixels.Length} pixels, but the " +
				$"{width}x{height} cell grid derived from it needs {cellCount}.");
		}

		var rawHeights = new byte[cellCount];
		var cellFlags = new byte[cellCount];

		// LOD (HeightGrid+0x10c): the terrain draw radius in cells, off the detail setting's table
		// with the shift-15 correction — see TerrainDetail, which owns the derivation.
		//
		// The one difference from the original is when: Terrain_SetupVisibleRegion re-reads the
		// setting every frame, where this reads it once at load. Equivalent while nothing changes the
		// setting mid-mission, which nothing in this engine does.
		int detailLod = TerrainDetail.RadiusFor(detailLevel, cellShift);

		int blockMask = (1 << (0x15 - materials[0].BlockShift - cellShift)) - 1;
		if (blockMask < 0) {
			blockMask = 0;
		}

		int maxRaw = 0;

		for (int row = 0; row < height; row++) {
			int destRowBase = (height - row - 1) << widthShift;
			int sourceRowBase = row << widthShift;

			for (int col = 0; col < width; col++) {
				int destIndex = destRowBase + col;

				byte raw = (byte)(pixels[sourceRowBase + col] - heightBias);
				if (raw > maxRaw) {
					maxRaw = raw;
				}
				rawHeights[destIndex] = raw;

				if ((row & blockMask) == 0 && (col & blockMask) == 0) {
					cellFlags[destIndex] &= 2;
					if (random.NextMasked(0xfff) < 0x4ce) {
						cellFlags[destIndex] = (byte)((cellFlags[destIndex] & 2) | (1 << 2));
					}
				} else {
					int blockRowBase = (height - (row & ~blockMask) - 1) << widthShift;
					int sourceIndex = blockRowBase + (col & ~blockMask);
					cellFlags[destIndex] = (byte)((cellFlags[destIndex] & 2) | ((cellFlags[sourceIndex] >> 2) << 2));
				}
			}
		}

		return new HeightGrid(widthShift, heightShift, cellShift, heightScale, detailLod, rawHeights, cellFlags) {
			MaxWorldHeight = heightScale * maxRaw
		};
	}

	/// <summary>
	/// The original's inline <c>for (i = 1; i &lt; value; i *= 2) shift++;</c> — ceil(log2), and 0
	/// for any value of 1 or less.
	/// </summary>
	private static int CeilLog2(int value) {
		int shift = 0;
		for (int i = 1; i < value; i *= 2) {
			shift++;
		}
		return shift;
	}
}
