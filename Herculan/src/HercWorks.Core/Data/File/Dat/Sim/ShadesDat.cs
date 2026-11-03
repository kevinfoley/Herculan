namespace HercWorks.Core.Data.File.Dat.Sim;

/// <summary>
/// <c>dat\SHADES.DAT</c>: which shade ramps the distance colour bands re-point before an object of each type tag is
/// drawn, and which column of the theater's level ranges each one takes. Read by <c>World_LoadShades</c>
/// (<c>0042e760</c>) as a record count byte, a list count byte, then each list as its tag byte, a pair count byte and that
/// many <see cref="ShadeRepoint"/> byte pairs. What the bands do is
/// docs/retail/rendering/distance-fog-and-sky.md#distance-colour-bands--worldshades_applyforobject-0042e8e8.
/// </summary>
public sealed class ShadesDat {
	/// <summary>How many type-tag records the loader allocates, the file's first byte. 11 in retail data.</summary>
	public byte TagCount { get; set; }

	/// <summary>The lists, in file order.</summary>
	public List<ShadeList> Lists { get; set; } = new();

	/// <summary>
	/// What an object of type tag <paramref name="tag"/> re-points: the last list naming it, since each list is stored over
	/// the record's earlier one, or none.
	/// </summary>
	public IReadOnlyList<ShadeRepoint> For(int tag) {
		for (int i = Lists.Count - 1; i >= 0; i--) {
			if (Lists[i].Tag == tag) {
				return Lists[i].Repoints;
			}
		}

		return Array.Empty<ShadeRepoint>();
	}
}

/// <summary>One type tag's list: the shade ramps it re-points.</summary>
public sealed record ShadeList(byte Tag, ShadeRepoint[] Repoints);

/// <summary>A shade ramp, and the column of the theater's level ranges it is pointed at.</summary>
public readonly record struct ShadeRepoint(byte Ramp, byte Column);
