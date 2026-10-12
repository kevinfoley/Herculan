using HercWorks.Core.Data.File.Dbsim;
using HercWorks.Core.Data.File.Dyn;
using Herculan.Engine.Content;
using Herculan.Engine.Render;

namespace Herculan.Es1.Scene;

/// <summary>
/// How ES1's simulator colours a zone shape's faces (docs/es1/rendering/polygon-shading.md#the-zone-shapes): a solid face
/// draws its palette index unchanged, and a shaded face takes entry (light · length) &gt;&gt; 8 of its ramp, which
/// <c>WorldShades_ApplyForObject</c> (<c>ES1DBSIM 00025d44</c>) re-points before each shape by its distance band. Each
/// band is one block of <see cref="Table"/>, and a drawn shape reads its band's (<see cref="SceneItem.RampBlock"/>). The
/// light is <see cref="Es1Sun"/>'s.
/// </summary>
public sealed class Es1Shading {
	/// <summary>
	/// Rows of the identity ramp the mesh builder resolves solid faces through: as many as a retail ES2 ramp has, so
	/// ES2's row arithmetic is unchanged. Every row passes a palette index through as it is, as ES1's
	/// <c>TSSolidPoly_Render</c> (<c>0005403c</c>) draws a flag-0 value.
	/// </summary>
	private const int IdentityShadeLevels = 32;

	/// <summary>The type tag every zone shape object carries (docs/es1/formats/zone-terrain.md#the-zone-shape-object).</summary>
	public const byte ZoneShapeTag = 0;

	private readonly int[] _thresholds;

	private Es1Shading(SurfaceShading surfaces, SurfaceRampTable? table, int[] thresholds) {
		Surfaces = surfaces;
		Table = table;
		_thresholds = thresholds;
	}

	/// <summary>
	/// The palette with an identity ramp, which <c>DtsMeshBuilder</c> builds the shapes against: a solid face's
	/// palette index and a shaded face's ramp number reach the renderer unchanged.
	/// </summary>
	public SurfaceShading Surfaces { get; }

	/// <summary>
	/// The shaded faces' colours, one block per distance band, or null without a palette, a world file or
	/// <c>dat\shades</c>; the shaded faces then draw through the palette's own ramps, unbanded.
	/// </summary>
	public SurfaceRampTable? Table { get; }

	/// <summary>Builds the shading from the theater's palette, world file and <c>dat\shades</c>.</summary>
	public static Es1Shading For(DynamixPalette? palette, Es1WorldData? world, WorldShades? shades) {
		var rows = new byte[IdentityShadeLevels * ShadeRamp.RowLength];
		for (int i = 0; i < rows.Length; i++) {
			rows[i] = (byte)(i % ShadeRamp.RowLength);
		}

		var surfaces = new SurfaceShading(ShadeRamp.FromRows(IdentityShadeLevels, 1, rows), palette);
		int[] thresholds = world?.DistanceBandsB ?? [];
		if (palette is not { ShadeRamps.Count: > 0 } || world == null || shades == null || world.RampRows < 1) {
			return new Es1Shading(surfaces, null, thresholds);
		}

		var ranges = LevelRanges(world);
		var pairs = shades.Lists.FirstOrDefault(list => list.Tag == ZoneShapeTag)?.Pairs ?? [];
		int bands = world.RampRows;

		// Block b is the raster's ramp table once WorldShades_ApplyForObject has re-pointed the tag's ramps at band b
		// (Palette_SetRampsToRange: a range's consecutive indices).
		var blocks = new IReadOnlyList<short[]>[bands];
		for (int band = 0; band < bands; band++) {
			var ramps = palette.ShadeRamps.ToArray();
			foreach (var (ramp, column) in pairs) {
				if (ramp < ramps.Length && column * bands + band < ranges.Length) {
					var (start, length) = ranges[column * bands + band];
					ramps[ramp] = Enumerable.Range(start, Math.Max((int)length, 0)).Select(i => (short)i).ToArray();
				}
			}

			blocks[band] = ramps;
		}

		byte? Entry(int ramp, int shade, int band) =>
			ramp < blocks[band].Count && blocks[band][ramp] is { Length: > 0 } entries
				? (byte)entries[shade * entries.Length >> 8]
				: null;

		// No theater shape has a TSGouraudPoly (docs/es1/rendering/polygon-shading.md#the-theater-shapes-faces), so its
		// single block is the ramps as the palette loads them.
		var table = SurfaceRampTable.FromIndices(bands, Entry,
			(ramp, shade) => ramp < palette.ShadeRamps.Count && palette.ShadeRamps[ramp] is { Length: > 0 } entries
				? (byte)entries[shade * entries.Length >> 8]
				: null,
			palette);
		return new Es1Shading(surfaces, table, thresholds);
	}

	/// <summary>
	/// <c>WorldShades_DistanceBand</c> (<c>00025cb4</c>) for a zone shape: how many of the tag-0 thresholds the distance
	/// has reached, unsigned, at most one fewer than there are. A zone shape adds no distance offset.
	/// </summary>
	public int Band(int distance) {
		int band = 0;
		while (band < _thresholds.Length - 1 && (uint)distance >= (uint)_thresholds[band]) {
			band++;
		}

		return band;
	}

	/// <summary>
	/// <c>WorldShades_IsBeyondLastBand</c> (<c>00025f38</c>) for a zone shape: whether the distance exceeds the tag-0
	/// table's last threshold, unsigned, which leaves the shape undrawn.
	/// </summary>
	public bool IsBeyondLastBand(int distance) =>
		_thresholds.Length > 0 && (uint)_thresholds[^1] < (uint)distance;

	/// <summary>
	/// <c>WorldShades_LevelRanges</c> (<c>000afdbc</c>) as <c>Palette_InterpolateIndexRanges</c> (<c>000289e0</c>) expands
	/// the world file's two range tables: for each column, <see cref="Es1WorldData.RampRows"/> ranges from the first
	/// table's range to the second's in 8.8 fixed point, the last exactly the second's
	/// (docs/es1/formats/wld-world.md#the-distance-colour-ranges). Entry <c>column × rows + band</c>.
	/// </summary>
	public static (short Start, short Length)[] LevelRanges(Es1WorldData world) {
		int levels = world.RampRows;
		int columns = Math.Min(world.RampTableA.Length, world.RampTableB.Length);
		var ranges = new (short, short)[columns * Math.Max(levels, 0)];
		int at = 0;
		for (int column = 0; column < columns; column++) {
			short fromStart = (short)world.RampTableA[column];
			short fromEnd = (short)(fromStart + (short)(world.RampTableA[column] >> 16) - 1);
			short toStart = (short)world.RampTableB[column];
			short toEnd = (short)(toStart + (short)(world.RampTableB[column] >> 16) - 1);
			int steps = levels - 1;
			int startAccumulator = 0;
			int endAccumulator = 0;
			for (int band = 0; band < steps; band++) {
				short start = (short)((short)((uint)startAccumulator >> 8) + fromStart);
				short end = (short)((short)((uint)endAccumulator >> 8) + fromEnd);
				ranges[at++] = (start, (short)(end - start + 1));
				startAccumulator += (short)((toStart - fromStart + 1) * 0x100 / steps);
				endAccumulator += (short)((toEnd - fromEnd + 1) * 0x100 / steps);
			}

			if (levels > 0) {
				ranges[at++] = (toStart, (short)(toEnd - toStart + 1));
			}
		}

		return ranges;
	}
}
