using HercWorks.Core.Data.File.Dbsim;
using HercWorks.Core.Data.File.Dyn;
using Herculan.Engine.Content;
using Herculan.Engine.Render;

namespace Herculan.Es1.Scene;

/// <summary>
/// How ES1's simulator colours a shape's faces (docs/es1/rendering/polygon-shading.md#the-distance-bands): a solid face
/// draws its palette index unchanged, and a shaded face takes entry (light · length) &gt;&gt; 8 of its ramp, which
/// <c>WorldShades_ApplyForObject</c> (<c>ES1DBSIM 00025d44</c>) re-points before each object by its type tag's
/// <c>SHADES.DAT</c> list and its distance band. <see cref="Table"/> holds the palette's own ramps, and a drawn object
/// carries the few its tag and band re-point (<see cref="SceneItem.RepointedRamps"/>, <see cref="RampsFor"/>), one list
/// instance per (tag, band). The light is <see cref="Es1Sun"/>'s and the effect lights' within reach
/// (<see cref="Effects.Es1EffectLights"/>).
///
/// <para>The table cannot hold runs alone: a palette's ramps are not all runs
/// (docs/es1/rendering/palette.md#the-palettes-shade-ramps).</para>
///
/// <para>A ramp the tag's list does not name keeps whatever the last object drawn left it at, and an object here reads
/// the palette's own range for it. That is the original's state for a zone shape's shaded faces, all on ramp 14, which
/// only tag 0's list names (docs/es1/rendering/polygon-shading.md#the-theater-shapes-faces), for a HERC's root 0, which
/// shades only on ramps 0, 2, 4, 6, 8 and 12, which tag 7's list names, and ramp 200, which no list names, for a flyer's
/// shape (tag 6), which shades on the ramps its tag's list names, and for the impact effects' retail shapes, which hold
/// bitmap parts and solid faces alone, which no ramp colours.</para>
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

	/// <summary>The tag whose cull <c>WorldShades_IsBeyondLastBand</c> moves by the world file's first distance offset.</summary>
	private const byte OffsetCullTag = Effects.Es1Explosion.ClassTypeTag;

	/// <summary>
	/// The ramps <c>WorldShades_ResetRamps</c> (<c>00025e88</c>) points at <see cref="ResetRange"/>: 0, 2, 4, 6, 8, 10,
	/// 12 and 14 (docs/es1/rendering/polygon-shading.md#the-distance-bands).
	/// </summary>
	private static readonly int[] ResetRampNumbers = [0, 2, 4, 6, 8, 10, 12, 14];

	/// <summary><c>WorldShades_ResetRamps</c>' range {16, 16}: palette indices 16 to 31.</summary>
	private static readonly (short Start, short Length) ResetRange = (16, 16);

	private static readonly IReadOnlyList<BandRamp> None = Array.Empty<BandRamp>();

	private readonly int[] _tag0Thresholds;
	private readonly int[] _taggedThresholds;
	private readonly int _cullOffset;
	private readonly int _largeOffset;

	/// <summary>Each listed tag's re-pointed ramps, one list per band, the same instance for every object that reads it.</summary>
	private readonly IReadOnlyDictionary<byte, IReadOnlyList<BandRamp>[]> _bandRamps;

	private Es1Shading(SurfaceShading surfaces, SurfaceRampTable? table, int[] tag0Thresholds, int[] taggedThresholds,
			int cullOffset, int largeOffset, IReadOnlyDictionary<byte, IReadOnlyList<BandRamp>[]> bandRamps,
			IReadOnlyList<BandRamp> flatRamps, IReadOnlyList<BandRamp> hddMapRamps) {
		Surfaces = surfaces;
		Table = table;
		_tag0Thresholds = tag0Thresholds;
		_taggedThresholds = taggedThresholds;
		_cullOffset = cullOffset;
		_largeOffset = largeOffset;
		_bandRamps = bandRamps;
		FlatRamps = flatRamps;
		HddMapRamps = hddMapRamps;
	}

	/// <summary>
	/// The palette with an identity ramp, which <c>DtsMeshBuilder</c> builds the shapes against: a solid face's
	/// palette index and a shaded face's ramp number reach the renderer unchanged.
	/// </summary>
	public SurfaceShading Surfaces { get; }

	/// <summary>
	/// The palette's own ramps, which a shaded face reads on any ramp its object does not re-point, shown through the
	/// renderer's live palette (<see cref="SurfaceRampTable.NamesIndices"/>), or null without a palette, a world file or
	/// <c>dat\shades</c>; the shaded faces then draw through the palette's own ramps, unbanded.
	/// </summary>
	public SurfaceRampTable? Table { get; }

	/// <summary>
	/// Builds the shading from the live palette, world file and <c>dat\shades</c>: the indices from the palette's ramps,
	/// which the cockpit's scheme does not change.
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
			return new Es1Shading(surfaces, null, tag0, tagged, world?.DistanceOffsetSmall ?? 0, world?.DistanceOffsetLarge ?? 0,
				new Dictionary<byte, IReadOnlyList<BandRamp>[]>(), None, None);
		}

		var ranges = LevelRanges(world);
		int bands = world.RampRows;
		int rampCount = palette.ShadeRamps.Count;

		// Tag t's list at band b is the ramps WorldShades_ApplyForObject re-points (Palette_SetRampsToRange: a range's
		// consecutive indices), a later pair on a ramp replacing an earlier one's; each list goes to its tag's record
		// (docs/es1/rendering/polygon-shading.md#the-shades-file). A run whose length comes out at 0 or below has no
		// entry, which draws mid grey.
		var bandRamps = new Dictionary<byte, IReadOnlyList<BandRamp>[]>();
		foreach (var list in shades.Lists) {
			var perBand = new IReadOnlyList<BandRamp>[bands];
			for (int band = 0; band < bands; band++) {
				var ramps = new List<BandRamp>();
				foreach (var (ramp, column) in list.Pairs) {
					if (ramp < rampCount && column * bands + band < ranges.Length) {
						var (start, length) = ranges[column * bands + band];
						Repoint(ramps, new BandRamp(ramp, start, Math.Max((int)length, 0)));
					}
				}

				perBand[band] = Capped(ramps);
			}

			bandRamps[list.Tag] = perBand;
		}

		var flat = new List<BandRamp>();
		foreach (int ramp in ResetRampNumbers.Where(ramp => ramp < rampCount)) {
			Repoint(flat, new BandRamp(ramp, ResetRange.Start, ResetRange.Length));
		}

		var hddMap = new List<BandRamp>();
		if (HddMapRamp < rampCount) {
			hddMap.Add(new BandRamp(HddMapRamp, HddMapEntry, 1));
		}

		var table = SurfaceRampTable.FromIndices((ramp, shade) =>
			ramp < rampCount && palette.ShadeRamps[ramp] is { Length: > 0 } entries
				? (byte)entries[shade * entries.Length >> 8]
				: null);
		return new Es1Shading(surfaces, table, tag0, tagged, world.DistanceOffsetSmall, world.DistanceOffsetLarge, bandRamps,
			Capped(flat), Capped(hddMap));
	}

	/// <summary>Puts <paramref name="run"/> in <paramref name="ramps"/>, in place of an earlier run on its ramp.</summary>
	private static void Repoint(List<BandRamp> ramps, BandRamp run) {
		int at = ramps.FindIndex(r => r.Ramp == run.Ramp);
		if (at >= 0) {
			ramps[at] = run;
		} else {
			ramps.Add(run);
		}
	}

	/// <summary>
	/// <paramref name="ramps"/> as an item carries them: the first <see cref="BandRamp.MaxPerItem"/>, which every retail
	/// list is within (at most six, and <see cref="ResetRampNumbers"/>' eight); a ramp past them keeps the palette's own.
	/// </summary>
	private static IReadOnlyList<BandRamp> Capped(List<BandRamp> ramps) =>
		ramps.Count == 0 ? None : ramps.Take(BandRamp.MaxPerItem).ToArray();

	/// <summary>
	/// The ramps as <c>WorldShades_ResetRamps</c> leaves them, <see cref="ResetRampNumbers"/> on palette indices 16 to 31,
	/// which a draw outside the object list reads, by no band; empty without a table.
	/// </summary>
	public IReadOnlyList<BandRamp> FlatRamps { get; }

	/// <summary>
	/// The ramps as <c>HddCommandScreen_DrawMap</c> (<c>00040d4c</c>) leaves them with
	/// <c>Palette_SetRampsToRange</c>({<see cref="HddMapEntry"/>, 1}, 1, <see cref="HddMapRamp"/>) before it draws the
	/// zone's shapes (docs/es1/simulation/hdd-command-screen.md#drawing-the-map): ramp 14 the one entry; empty without a
	/// table.
	/// </summary>
	public IReadOnlyList<BandRamp> HddMapRamps { get; }

	/// <summary>The ramp the command screen's map points, and the one palette entry it points it at.</summary>
	private const int HddMapRamp = 14;

	/// <inheritdoc cref="HddMapRamp"/>
	private const short HddMapEntry = 0x38;

	/// <summary>
	/// The ramps an object of <paramref name="tag"/> at <paramref name="distance"/> draws with re-pointed, a tag-5 object's
	/// by its slot <c>+0x14</c> <paramref name="radius"/> as well: its tag's list at its band, or none for a tag with no
	/// list (docs/es1/rendering/polygon-shading.md#the-shades-file) and without a table.
	/// </summary>
	public IReadOnlyList<BandRamp> RampsFor(byte tag, int distance, int radius = 0) =>
		_bandRamps.TryGetValue(tag, out var perBand)
			? perBand[Math.Min(Band(tag, distance, radius), perBand.Length - 1)]
			: None;

	/// <summary>
	/// The distance a tag-5 object, a structure, is banded and culled at: <paramref name="distance"/> plus
	/// <c>WorldShades_DistanceOffsets</c> (<c>000bc5c6</c>) entry 0, or entry 1 when its slot <c>+0x14</c> radius
	/// <paramref name="radius"/> is 5000 or more, clamped at 0; any other tag's distance unchanged.
	/// </summary>
	private int StructureDistance(byte tag, int distance, int radius) =>
		tag == StructureTag ? Math.Max(distance + (radius >= LargeStructureRadius ? _largeOffset : _cullOffset), 0) : distance;

	/// <summary>The type tag whose band and cull take <see cref="StructureDistance"/>.</summary>
	private const byte StructureTag = (byte)Sim.Es1Base.TypeTag;

	/// <summary>The slot <c>+0x14</c> radius from which a tag-5 object takes the second distance offset.</summary>
	private const int LargeStructureRadius = 5000;

	/// <summary>
	/// <c>WorldShades_DistanceBand</c> (<c>00025cb4</c>): how many of the tag's thresholds the distance has reached,
	/// unsigned, at most one fewer than there are; tag 0 reads <c>WorldShades_BandsTag0</c> and any other tag
	/// <c>WorldShades_BandsTagged</c>, a tag-5 object at <see cref="StructureDistance"/> with its <paramref name="radius"/>.
	/// </summary>
	public int Band(byte tag, int distance, int radius = 0) {
		distance = StructureDistance(tag, distance, radius);
		var thresholds = tag == ZoneShapeTag ? _tag0Thresholds : _taggedThresholds;
		int band = 0;
		while (band < thresholds.Length - 1 && (uint)distance >= (uint)thresholds[band]) {
			band++;
		}

		return band;
	}

	/// <summary>
	/// <c>WorldShades_IsBeyondLastBand</c> (<c>00025f38</c>): whether the distance exceeds the tag's table's last threshold,
	/// unsigned, which leaves the object undrawn; for tag 8, an impact effect's, the distance first takes the world file's
	/// first distance offset (<c>WorldShades_DistanceOffsets</c> entry 0), clamped at 0, and a tag-5 object's is
	/// <see cref="StructureDistance"/> with its <paramref name="radius"/>.
	/// </summary>
	public bool IsBeyondLastBand(byte tag, int distance, int radius = 0) {
		if (tag == OffsetCullTag) {
			distance = Math.Max(distance + _cullOffset, 0);
		}

		distance = StructureDistance(tag, distance, radius);

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
