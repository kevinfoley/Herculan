using HercWorks.Core.Data.Struct.Dbsim;

namespace HercWorks.Core.Data.File.Dat.Sim;

/// <summary>
/// FILE - ES1's dat\BULLETS.DAT, in SIMVOL0.VOL: an <c>int16</c> count, then that many 10-byte <see cref="Es1BulletType"/>
/// records, ES2's first five fields (<see cref="BulletType"/>) without its scatter and lifetime-rate flag. Layout:
/// docs/es1/formats/bullets-skimmer-dat.md#datbulletsdat. Read by <see cref="Io.Transform.Dbsim.Es1BulletDataTransformer"/>.
/// </summary>
public class Es1BulletData {
	public Es1BulletType[] Entries { get; set; } = [];
}

/// <summary>
/// One 10-byte record of ES1's <c>BULLETS.DAT</c>, indexed by the firing projectile record's subtype id. Each member takes
/// the name and description of ES2's field at its offset; which ES1 reads as ES2 does is an Open item of the doc, and the
/// members ES1's constructor reads say so.
/// </summary>
public class Es1BulletType {
	/// <summary><c>+0x00</c> — the root of <c>dts\BULLETS</c> the round is drawn as; ES1's constructor reads it so.</summary>
	public short ModelId { get; set; }

	/// <inheritdoc cref="BulletType.Lifetime"/>
	public short Lifetime { get; set; }

	/// <inheritdoc cref="BulletType.ClipRadius"/>
	public short ClipRadius { get; set; }

	/// <summary><c>+0x06</c> — the frame interval, which ES1's constructor loads into the round's frame countdown as ES2's does.</summary>
	public short FrameInterval { get; set; }

	/// <inheritdoc cref="BulletType.FireSoundId"/>
	public short FireSoundId { get; set; }
}
