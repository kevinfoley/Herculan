namespace HercWorks.Core.Data.File.Dbsim;

/// <summary>
/// FILE - <c>dat\shades.dat</c>, in SIMVOL0.VOL in both games: per object type tag, the ramps its distance band re-points
/// and the column of the world file's range tables each takes. <c>World_LoadShades</c> reads it, ES2's at
/// <c>0042e760</c> and ES1's at <c>ES1DBSIM 00025bc8</c> (docs/es1/rendering/polygon-shading.md#the-shades-file,
/// docs/retail/rendering/distance-fog-and-sky.md#distance-colour-bands--worldshades_applyforobject-0042e8e8).
/// Read by <see cref="Io.Transform.Dbsim.WorldShadesTransformer"/>.
/// </summary>
public class WorldShades {
	/// <summary><i>N</i>, the first byte: how many type tags the load's table has room for.</summary>
	public byte TagCapacity { get; set; }

	/// <summary>The lists in file order; the byte before them counts them.</summary>
	public List<WorldShadesList> Lists { get; set; } = [];
}

/// <summary>One type tag's list: the tag, then its <c>{ramp, column}</c> pairs.</summary>
/// <param name="Tag">The type tag the list is for.</param>
/// <param name="Pairs">Each ramp the band re-points and the range column it takes.</param>
public sealed record WorldShadesList(byte Tag, IReadOnlyList<(byte Ramp, byte Column)> Pairs);
