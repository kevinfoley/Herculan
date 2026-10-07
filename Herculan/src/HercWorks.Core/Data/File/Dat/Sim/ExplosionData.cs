namespace HercWorks.Core.Data.File.Dat.Sim;

/// <summary>
/// FILE — /SIMVOL0/DAT/EXPLOS.DAT (and its low-memory twin EXPLOS2.DAT), the impact/explosion
/// effect tables DBSIM's <c>EXPLO.CPP</c> subsystem loads at startup (<c>Explosion_LoadResources</c>, <c>00407b54</c>).
///
/// <para>Two length-prefixed tables back to back: one <see cref="ExplosionShapeEntry"/> per root of
/// <c>dts\EXPLOS.DTS</c>, then the <see cref="ExplosionTypeEntry"/> effect table proper, indexed by
/// the effect type ids that <see cref="ProjectileData.Projectile"/>'s three <c>ImpactFX</c> arrays hold. The byte
/// layout and how the loader binds the banks are in docs/retail/formats/explos-dat.md.</para>
/// </summary>
public class ExplosionData {
	public ExplosionShapeEntry[]? Shapes { get; set; }

	public ExplosionTypeEntry[]? Types { get; set; }
}

/// <summary>One row of <see cref="ExplosionData.Shapes"/>, four bytes, one per <c>EXPLOS.DTS</c> root.</summary>
public class ExplosionShapeEntry {
	/// <summary>
	/// Which cell-animation sequence of the shape the effect drives. The frame counter the effect
	/// steps is <c>instance.cellFrames[AnimSequence]</c>, which is what
	/// <c>TSCellAnimPart_Render</c> (<c>004767e4</c>) indexes its child list by. Zero on every
	/// retail row, matching every <c>TSCellAnimPart</c> in <c>EXPLOS.DTS</c> carrying sequence 0.
	///
	/// <para>A negative value means the shape has no flipbook, and the constructor skips both the
	/// frame reset and the per-tick step — an effect built on one plays no animation and is dropped
	/// on its first timer expiry.</para>
	/// </summary>
	public short AnimSequence { get; set; }

	/// <summary>Index of the <c>dba\EXPLO&lt;n&gt;.DBA</c> bank the shape's sprites are drawn from.</summary>
	public short TextureBankIndex { get; set; }
}

/// <summary>
/// One 40-byte row of <see cref="ExplosionData.Types"/> — everything an impact effect is.
///
/// <para>Field roles are from <c>Explosion_Construct</c> (<c>00407f1c</c>, the constructor) and <c>Explosion_TickUpdate</c> (<c>0040813c</c>, the
/// per-tick update), both of which reach the row through <c>Explosion_GetTypeRecord</c> (<c>00407b20</c>),
/// <c>table + typeId * 0x28</c>.</para>
/// </summary>
public class ExplosionTypeEntry {
	/// <summary><c>+0x00</c> — which <see cref="ExplosionData.Shapes"/> row, i.e. which <c>EXPLOS.DTS</c> root, the effect draws.</summary>
	public short ShapeIndex { get; set; }

	/// <summary>
	/// <c>+0x02</c> — how many ticks each flipbook frame is held for, reloaded into the effect's
	/// countdown every time a frame is stepped. One on every retail row, so retail effects advance a
	/// frame per tick.
	/// </summary>
	public short FrameInterval { get; set; }

	/// <summary>
	/// <c>+0x04</c> — nonzero lays a ground shape under the effect: root 1 of the theater's flat set,
	/// whose cell steps with the effect's frame and which goes when the effect does. Zero on every
	/// row of both <c>EXPLOS.DAT</c> and <c>EXPLOS2.DAT</c>, so nothing in retail data reaches it.
	/// See docs/retail/simulation/ground-shapes.md.
	/// </summary>
	public short GroundShape { get; set; }

	/// <summary>
	/// <c>+0x06</c> — nonzero attaches a light source, driven per frame from
	/// <see cref="FrameIntensity"/>. Values 0, 1 and 2 all occur in retail data.
	/// </summary>
	public short LightMode { get; set; }

	/// <summary>
	/// <c>+0x08</c>..<c>+0x1f</c> — the light's intensity for each flipbook frame in turn; the tick
	/// passes <c>FrameIntensity[frame]</c>'s low byte to the light object as the frame is stepped.
	/// Retail rows run ramps like 50, 100, 200, 255, 200, 255, 150, 255, 200, 150.
	/// </summary>
	public short[] FrameIntensity { get; set; } = new short[FrameIntensityCount];

	/// <summary>How many frames of light intensity the row has room for.</summary>
	public const int FrameIntensityCount = 12;

	/// <summary>
	/// <c>+0x20</c>, an int32 — the radius the effect's proximity test (<c>Explosion_ProximityTest</c>,
	/// <c>00408100</c>) reports a hit inside. Either 0 or 20000 in retail data. That test has no
	/// reference in the image, so the field has no effect in a mission; see
	/// docs/retail/formats/explos-dat.md#type-row-0x28-bytes.
	/// </summary>
	public int ProximityRadius { get; set; }

	/// <summary>
	/// <c>+0x24</c> — sound id, played as <c>id + 10</c> at the effect's position when the
	/// constructor is asked for one. Negative means silent.
	/// </summary>
	public short SoundId { get; set; }

	/// <summary>
	/// <c>+0x26</c> — selects which object-class tag the effect registers under, 2 when zero and 8
	/// otherwise. It is what separates the small surface hits from the ones that read as explosions
	/// to the rest of the simulation.
	/// </summary>
	public short ObjectClass { get; set; }
}
