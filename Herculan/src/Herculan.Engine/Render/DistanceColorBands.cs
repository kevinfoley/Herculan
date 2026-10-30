using HercWorks.Core.Data.File.Dat.Sim;
using HercWorks.Core.Data.File.Dbsim;
using HercWorks.Core.Io.Transform.Dbsim;
using Herculan.Engine.Content;

namespace Herculan.Engine.Render;

/// <summary>
/// The distance colour bands: before each object's draw the original re-points some of the raster's shade ramps at a
/// run of palette indices that its type tag and its distance from the view pick (<c>WorldShades_ApplyForObject</c>,
/// <c>0042e8e8</c>). A <c>TSGouraudPoly</c>'s fill reads those ramps and a <c>TSShadedPoly</c>'s does not, so this is what
/// fades a Gouraud face with distance. See
/// docs/retail/rendering/distance-fog-and-sky.md#distance-colour-bands--worldshades_applyforobject-0042e8e8.
///
/// <para>Two branches are not here: the blend form <c>ObjList_DrawEntryRender</c> asks for with the byte at
/// <c>004cfa08</c> set, and <c>WorldShades_ResetRamps</c>, which runs while <c>0049aef2</c> (0 in the image) is set. A tag
/// with no list leaves the ramps as the last object drawn left them in the original; here it leaves them as the palette
/// has them, since nothing here carries ramp state from one object's draw to the next. Of retail shapes, only the ground
/// vehicles' (tag 10) have Gouraud faces under such a tag; KNOWN_ISSUES.md lists it.</para>
/// </summary>
public sealed class DistanceColorBands {
	/// <summary>The most ramps one tag re-points that the scene program takes; <c>SHADES.DAT</c> lists six at most.</summary>
	public const int MaxRepoints = 8;

	/// <summary>The shape radius from which a structure's distance takes the second offset.</summary>
	private const int LargeShapeRadius = 5000;

	private readonly int[] _bandsTagged;
	private readonly int[] _bandsTag0;
	private readonly int _offsetSmall;
	private readonly int _offsetLarge;
	private readonly int _levels;
	private readonly Dictionary<(int Tag, int Band), BandRamp[]> _ramps = new();

	private DistanceColorBands(WorldData world, ShadesDat shades) {
		_bandsTagged = world.DistanceBandsA;
		_bandsTag0 = world.DistanceBandsB;
		_offsetSmall = world.Trailer2;
		_offsetLarge = world.Trailer3;
		_levels = world.RampRows;
		int columns = world.RampColumns;
		var levelRanges = InterpolateIndexRanges(world.RampTableA, world.RampTableB, columns, _levels);

		foreach (var list in shades.Lists) {
			var repoints = shades.For(list.Tag);
			for (int band = 0; band < _levels; band++) {
				var ramps = new List<BandRamp>();
				foreach (var repoint in repoints) {
					if (repoint.Column < columns) {
						var (start, count) = levelRanges[repoint.Column * _levels + band];
						ramps.Add(new BandRamp(repoint.Ramp, start, count));
					}
				}

				_ramps[(list.Tag, band)] = ramps.Take(MaxRepoints).ToArray();
			}
		}
	}

	/// <summary>The folder <c>World_LoadTheater</c> loads <see cref="ResourceName"/> from.</summary>
	public const string ResourceFolder = "dat";

	/// <summary>The type-tag table, <c>dat\shades</c>.</summary>
	public const string ResourceName = "SHADES.DAT";

	/// <summary>Reads <see cref="ResourceName"/> and builds the theater's bands, or null as <see cref="From"/> answers.</summary>
	public static DistanceColorBands? Load(GameContent content, WorldData world) =>
		From(world, content.Read(ResourceFolder, ResourceName) is { } bytes ? new ShadesDatTransformer().Parse(bytes) : null);

	/// <summary>The bands of a theater, or null when it has no level ranges or <c>SHADES.DAT</c> did not load.</summary>
	public static DistanceColorBands? From(WorldData world, ShadesDat? shades) =>
		shades != null && world.RampRows > 0 && world.RampColumns > 0
			&& world.RampTableA.Length >= world.RampColumns && world.RampTableB.Length >= world.RampColumns
			? new DistanceColorBands(world, shades)
			: null;

	/// <summary>
	/// <c>WorldShades_DistanceBand</c> (<c>0042e878</c>) on the distance <c>WorldShades_ApplyForObject</c> floors at 0: how
	/// many thresholds the distance has reached, at most one fewer than there are. Tag 0 counts the <c>.WLD</c>'s second
	/// threshold set and every other tag its first; a structure's distance first takes one of the two offsets, the second
	/// for a shape radius of <see cref="LargeShapeRadius"/> or more, and is floored at 0 again.
	/// </summary>
	public int Band(ObjectTypeTag tag, int distance, int shapeRadius) {
		distance = Math.Max(distance, 0);
		if (tag == ObjectTypeTag.Structure) {
			distance = Math.Max(distance + (shapeRadius >= LargeShapeRadius ? _offsetLarge : _offsetSmall), 0);
		}

		var thresholds = tag == ObjectTypeTag.DropPod ? _bandsTag0 : _bandsTagged;
		int band = 0;
		while (band < thresholds.Length - 1 && (uint)thresholds[band] <= (uint)distance) {
			band++;
		}

		return Math.Min(band, _levels - 1);
	}

	/// <summary>The ramps an object of <paramref name="tag"/> is drawn with re-pointed in <paramref name="band"/>, in list order.</summary>
	public IReadOnlyList<BandRamp> Ramps(ObjectTypeTag tag, int band) =>
		_ramps.TryGetValue(((int)tag, band), out var ramps) ? ramps : Array.Empty<BandRamp>();

	/// <summary>
	/// <c>Palette_InterpolateIndexRanges</c> (<c>00430d08</c>): for each of <paramref name="columns"/> columns,
	/// <paramref name="levels"/> palette index ranges stepping from <paramref name="from"/>'s range to
	/// <paramref name="to"/>'s in 8.8 fixed point, the last exactly <paramref name="to"/>'s. Each entry of the two tables holds
	/// a range's first index in its low half and its count in its high half. The step is a whole index more than the span
	/// over the level count less one, truncated, as the original computes it.
	/// </summary>
	internal static (short Start, short Count)[] InterpolateIndexRanges(int[] from, int[] to, int columns, int levels) {
		var ranges = new (short Start, short Count)[columns * levels];
		int at = 0;
		for (int column = 0; column < columns; column++) {
			short firstStart = (short)from[column];
			short firstEnd = (short)(firstStart + (short)(from[column] >> 16) - 1);
			short lastStart = (short)to[column];
			short lastEnd = (short)(lastStart + (short)(to[column] >> 16) - 1);

			if (levels > 1) {
				int startStep = (short)((lastStart - firstStart + 1) * 0x100 / (levels - 1));
				int endStep = (short)((lastEnd - firstEnd + 1) * 0x100 / (levels - 1));
				int startAt = 0;
				int endAt = 0;
				for (int level = 0; level < levels - 1; level++) {
					short start = (short)((startAt >> 8) + firstStart);
					ranges[at++] = (start, (short)((endAt >> 8) + firstEnd - start + 1));
					startAt += startStep;
					endAt += endStep;
				}
			}

			if (levels > 0) {
				ranges[at++] = (lastStart, (short)(lastEnd - lastStart + 1));
			}
		}

		return ranges;
	}
}

/// <summary>
/// One re-pointed shade ramp: <paramref name="Count"/> palette indices from <paramref name="Start"/>, which a Gouraud corner
/// at shade <c>s</c> takes entry <c>(s * Count) &gt;&gt; 8</c> of (<c>Raster_FillContourPolygon</c>, <c>004879c0</c>).
/// </summary>
public readonly record struct BandRamp(int Ramp, int Start, int Count);
