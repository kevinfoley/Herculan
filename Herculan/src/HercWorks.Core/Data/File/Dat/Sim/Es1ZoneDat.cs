using HercWorks.Vol;

namespace HercWorks.Core.Data.File.Dat.Sim;

/// <summary>
/// FILE - ES1's dat\ZONE*.DAT, in SIMVOL0.VOL and ZONES.VOL — the shapes standing on a Metaltech: Earthsiege
/// zone: two <c>int16</c> and a 20-byte record per shape (docs/es1/formats/zone-terrain.md#the-layout). ES2's
/// file of the same name is <see cref="ZoneDat"/>, a different format. Read by
/// <see cref="Io.Transform.Common.Es1ZoneDatTransformer"/>.
/// </summary>
public class Es1ZoneDat : DataFile {
	/// <summary>0x00 — docs/es1/formats/zone-terrain.md#open.</summary>
	public short Word00 { get; set; }

	/// <summary>The records from 0x04; the file stores their number as the <c>int16</c> at 0x02.</summary>
	public Es1ZoneShape[] Shapes { get; set; } = [];
}

/// <summary>One 20-byte record of <see cref="Es1ZoneDat"/>: a shape placed on the zone (docs/es1/formats/zone-terrain.md#the-layout).</summary>
public class Es1ZoneShape {
	/// <summary>0x00 — index into the theater's shape library, <c>dgs\&lt;theater&gt;</c>.</summary>
	public short ShapeIndex { get; set; }

	/// <summary>0x02 — X.</summary>
	public int X { get; set; }

	/// <summary>0x06 — Y.</summary>
	public int Y { get; set; }

	/// <summary>0x0A — Z.</summary>
	public int Z { get; set; }

	/// <summary>0x0E — the three Euler angles, the third the heading.</summary>
	public short[] Angles { get; set; } = new short[3];
}
