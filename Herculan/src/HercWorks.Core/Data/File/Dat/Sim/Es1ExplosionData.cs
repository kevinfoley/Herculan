namespace HercWorks.Core.Data.File.Dat.Sim;

/// <summary>
/// FILE - ES1's dat\EXPLOS.DAT, in SIMVOL0.VOL: an <c>int16</c> count, then that many 44-byte <see cref="Es1ExplosionTypeEntry"/>
/// rows. ES2's file (<see cref="ExplosionData"/>) has a separate shape table before its 40-byte rows; ES1's carries each
/// shape row's two fields inside the type row. Layout: docs/es1/formats/explos-dat.md. Read by
/// <see cref="Io.Transform.Dbsim.Es1ExplosionDataTransformer"/>.
/// </summary>
public class Es1ExplosionData {
	public Es1ExplosionTypeEntry[] Types { get; set; } = [];
}

/// <summary>
/// One 44-byte row, reached as <c>table + typeId * 0x2c</c>: ES2's 40-byte <see cref="ExplosionTypeEntry"/> with the
/// shape row's <see cref="ExplosionShapeEntry.AnimSequence"/> and <see cref="ExplosionShapeEntry.TextureBankIndex"/> inserted
/// after the shape index (docs/es1/formats/explos-dat.md#the-row). Each member takes the description of ES2's field;
/// which of them ES1's tick and light read as ES2's do is an Open item of the doc.
/// </summary>
public class Es1ExplosionTypeEntry {
	/// <inheritdoc cref="ExplosionTypeEntry.ShapeIndex"/>
	public short ShapeIndex { get; set; }

	/// <inheritdoc cref="ExplosionShapeEntry.AnimSequence"/>
	public short AnimSequence { get; set; }

	/// <summary>
	/// <c>+0x04</c> — the <c>dba\explo&lt;n&gt;</c> bank, 0 to 8, the loader binds to the root at <see cref="ShapeIndex"/>.
	/// </summary>
	public short TextureBankIndex { get; set; }

	/// <inheritdoc cref="ExplosionTypeEntry.FrameInterval"/>
	public short FrameInterval { get; set; }

	/// <inheritdoc cref="ExplosionTypeEntry.GroundShape"/>
	public short GroundShape { get; set; }

	/// <inheritdoc cref="ExplosionTypeEntry.LightMode"/>
	public short LightMode { get; set; }

	/// <inheritdoc cref="ExplosionTypeEntry.FrameIntensity"/>
	public short[] FrameIntensity { get; set; } = new short[ExplosionTypeEntry.FrameIntensityCount];

	/// <summary><c>+0x24</c>, an int32 — at the place of ES2's <see cref="ExplosionTypeEntry.ProximityRadius"/>, 0 or 20000.</summary>
	public int ProximityRadius { get; set; }

	/// <summary><c>+0x28</c> — the sound id, played as <c>id + 10</c> at the effect's position on every construction.</summary>
	public short SoundId { get; set; }

	/// <inheritdoc cref="ExplosionTypeEntry.ObjectClass"/>
	public short ObjectClass { get; set; }
}
