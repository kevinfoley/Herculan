namespace HercWorks.Core.Data.File.Dat.Sim;

/// <summary>
/// FILE - ES1's dat\BASES.DAT, in SIMVOL0.VOL: an <c>int16</c> count, then that many fixed 54-byte
/// <see cref="Es1BaseTypeRecord"/>s, read into 62-byte runtime records. ES2's (<see cref="BasesDat"/>) are variable-length,
/// with their components inline. Layout: docs/es1/formats/bases-dat.md. Read by
/// <see cref="Io.Transform.Dbsim.Es1BasesDatTransformer"/>.
/// </summary>
public class Es1BasesDat {
	public Es1BaseTypeRecord[] Types { get; set; } = [];
}

/// <summary>One structure type, 54 bytes, at the runtime record's offsets (docs/es1/formats/bases-dat.md#the-type-record).</summary>
public class Es1BaseTypeRecord {
	/// <summary>The record's length in the file.</summary>
	public const int Length = 0x36;

	/// <summary><c>+0x00</c> — the record's own index in every retail record.</summary>
	public short TypeIndex { get; set; }

	/// <summary><c>+0x02</c> — the shape's index in the shape set <see cref="ShapeSet"/> picks.</summary>
	public short ShapeIndex { get; set; }

	/// <summary><c>+0x04</c> — the wreck type in <c>dgs\BHULKS</c>.</summary>
	public short HulkTypeIndex { get; set; }

	/// <summary>
	/// <c>+0x06</c> — zero puts the type in the first shape set; non-zero puts it in the second and gives it its
	/// <c>BASECOL.DAT</c> model. 1 or 2 on types 5, 6 and 8, where ES2's field at this offset is the animation thread count.
	/// </summary>
	public short ShapeSet { get; set; }

	/// <summary><c>+0x08</c>-<c>+0x35</c>, 23 <c>int16</c> the loader does not read (docs/es1/formats/bases-dat.md#open).</summary>
	public short[] Fields08 { get; set; } = new short[23];
}
