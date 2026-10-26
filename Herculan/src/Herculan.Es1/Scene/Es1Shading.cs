using HercWorks.Core.Data.File.Dbsim;
using HercWorks.Core.Data.File.Dyn;
using Herculan.Engine.Content;
using Herculan.Engine.Render;

namespace Herculan.Es1.Scene;

/// <summary>
/// How ES1's simulator colours a shape's faces (docs/es1/rendering/polygon-shading.md#the-distance-bands): a solid face
/// draws its palette index unchanged, and a shaded face takes entry (light · length) &gt;&gt; 8 of its ramp, which
/// <c>WorldShades_ApplyForObject</c> (<c>ES1DBSIM 00025d44</c>) re-points before each object by its type tag's
/// <c>SHADES.DAT</c> list and its distance band. Each (tag, band) is one block of <see cref="Table"/>, and a drawn object
/// reads its own (<see cref="SceneItem.RampBlock"/>, <see cref="Block"/>). The light is <see cref="Es1Sun"/>'s.
///
/// <para>A ramp the tag's list does not name keeps whatever the last object drawn left it at, and a block holds the
/// palette's own range for it. That is the original's state for every face this step draws: a zone shape's shaded faces
/// are all on ramp 14, which only tag 0's list names (docs/es1/rendering/polygon-shading.md#the-theater-shapes-faces),
/// and a HERC's root 0 shades only on ramps 0, 2, 4, 6, 8 and 12, which tag 7's list names, and ramp 200, which no list
/// names. The flyers' and bases' tags, whose lists also name ramps 0 to 12, have no object in this step.</para>
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

	/// <summary>The tags whose bands the table holds, in block order: the zone shapes', the HERCs' and the projectiles'.</summary>
	public static IReadOnlyList<byte> Tags { get; } = [ZoneShapeTag, 7, Projectiles.Es1Projectile.TypeTag];

	/// <summary>
	/// The ramps <c>WorldShades_ResetRamps</c> (<c>00025e88</c>) points at <see cref="FlatRange"/>: 0, 2, 4, 6, 8, 10,
	/// 12 and 14 (docs/es1/rendering/polygon-shading.md#the-distance-bands).
	/// </summary>
	private static readonly int[] FlatRamps = [0, 2, 4, 6, 8, 10, 12, 14];

	/// <summary><c>WorldShades_ResetRamps</c>' range {16, 16}: palette indices 16 to 31.</summary>
	private static readonly (short Start, short Length) FlatRange = (16, 16);

	private readonly int[] _tag0Thresholds;
	private readonly int[] _taggedThresholds;
	private readonly int _bands;

	private Es1Shading(SurfaceShading surfaces, SurfaceRampTable? table, int[] tag0Thresholds, int[] taggedThresholds,
			int bands) {
		Surfaces = surfaces;
		Table = table;
		_tag0Thresholds = tag0Thresholds;
		_taggedThresholds = taggedThresholds;
		_bands = bands;
	}

	/// <summary>
	/// The palette with an identity ramp, which <c>DtsMeshBuilder</c> builds the shapes against: a solid face's
	/// palette index and a shaded face's ramp number reach the renderer unchanged.
	/// </summary>
	public SurfaceShading Surfaces { get; }

	/// <summary>
	/// The shaded faces' colours, one block per (tag, band) in <see cref="Tags"/> order, or null without a palette, a
	/// world file or <c>dat\shades</c>; the shaded faces then draw through the palette's own ramps, unbanded.
	/// </summary>
	public SurfaceRampTable? Table { get; }

	/// <summary>
	/// Builds the shading from the live palette, world file and <c>dat\shades</c>: the indices from the palette's ramps,
	/// which the cockpit's scheme does not change, and their colours from the palette's entries.
	/// </summary>
	public static Es1Shading For(DynamixPalette? palette, Es1WorldData? world, WorldShades? shades) {
		var rows = new byte[IdentityShadeLevels * ShadeRamp.RowLength];
		for (int i = 0; i < rows.Length; i++) {
			rows[i] = (byte)(i % ShadeRamp.RowLength);
		}

		var surfaces = new SurfaceShading(ShadeRamp.FromRows(IdentityShadeLevels, 1, rows), palette);
		int[] tag0 = world?.DistanceBandsB ?? [];
		int[] tagged = world?.DistanceBandsA ?? [];
		if (palette is not { ShadeRamps.Count: > 0 } || world == null || shades == null || world.RampRows < 1) {
			return new Es1Shading(surfaces, null, tag0, tagged, 0);
		}

		var ranges = LevelRanges(world);
		int bands = world.RampRows;

		// Block (t, b) is the raster's ramp table once WorldShades_ApplyForObject has re-pointed tag t's ramps at band b
		// (Palette_SetRampsToRange: a range's consecutive indices); the last, FlatBlock, once WorldShades_ResetRamps has.
		var blocks = new IReadOnlyList<short[]>[Tags.Count * bands + 1];
		for (int t = 0; t < Tags.Count; t++) {
			var pairs = shades.Lists.FirstOrDefault(list => list.Tag == Tags[t])?.Pairs ?? [];
			for (int band = 0; band < bands; band++) {
				var ramps = palette.ShadeRamps.ToArray();
				foreach (var (ramp, column) in pairs) {
					if (ramp < ramps.Length && column * bands + band < ranges.Length) {
						var (start, length) = ranges[column * bands + band];
						ramps[ramp] = Enumerable.Range(start, Math.Max((int)length, 0)).Select(i => (short)i).ToArray();
					}
				}

				blocks[t * bands + band] = ramps;
			}
		}

		var flat = palette.ShadeRamps.ToArray();
		foreach (int ramp in FlatRamps.Where(ramp => ramp < flat.Length)) {
			flat[ramp] = Enumerable.Range(FlatRange.Start, FlatRange.Length).Select(i => (short)i).ToArray();
		}

		blocks[^1] = flat;

		byte? Entry(int ramp, int shade, int block) =>
			ramp < blocks[block].Count && blocks[block][ramp] is { Length: > 0 } entries
				? (byte)entries[shade * entries.Length >> 8]
				: null;

		var table = SurfaceRampTable.FromIndices(blocks.Length, Entry,
			(ramp, shade) => ramp < palette.ShadeRamps.Count && palette.ShadeRamps[ramp] is { Length: > 0 } entries
				? (byte)entries[shade * entries.Length >> 8]
				: null,
			palette);
		return new Es1Shading(surfaces, table, tag0, tagged, bands);
	}

	/// <summary>
	/// The block of the ramps as <c>WorldShades_ResetRamps</c> leaves them, <see cref="FlatRamps"/> on palette indices 16
	/// to 31 and every other ramp the palette's own, which a draw outside the object list reads, by no band; null
	/// without a table.
	/// </summary>
	public int? FlatBlock => Table != null ? Tags.Count * _bands : null;

	/// <summary>The block an object of <paramref name="tag"/> at <paramref name="distance"/> reads, or null without a table.</summary>
	public int? Block(byte tag, int distance) {
		int t = Tags.ToList().IndexOf(tag);
		return Table != null && t >= 0 ? t * _bands + Math.Min(Band(tag, distance), _bands - 1) : null;
	}

	/// <summary>
	/// <c>WorldShades_DistanceBand</c> (<c>00025cb4</c>): how many of the tag's thresholds the distance has reached,
	/// unsigned, at most one fewer than there are; tag 0 reads <c>WorldShades_BandsTag0</c> and any other tag
	/// <c>WorldShades_BandsTagged</c>. Neither tag here adds a distance offset.
	/// </summary>
	public int Band(byte tag, int distance) {
		var thresholds = tag == ZoneShapeTag ? _tag0Thresholds : _taggedThresholds;
		int band = 0;
		while (band < thresholds.Length - 1 && (uint)distance >= (uint)thresholds[band]) {
			band++;
		}

		return band;
	}

	/// <summary>
	/// <c>WorldShades_IsBeyondLastBand</c> (<c>00025f38</c>): whether the distance exceeds the tag's table's last threshold,
	/// unsigned, which leaves the object undrawn.
	/// </summary>
	public bool IsBeyondLastBand(byte tag, int distance) {
		var thresholds = tag == ZoneShapeTag ? _tag0Thresholds : _taggedThresholds;
		return thresholds.Length > 0 && (uint)thresholds[^1] < (uint)distance;
	}

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
