using HercWorks.Vol;

namespace HercWorks.Core.Data.File.Dat.Sim;

/// <summary>
/// FILE - /ZONE/DAT/ZONEXXXX.DAT — the 16-byte per-zone header <c>Terrain_LoadZone</c> reads beside
/// the zone's heightmap, four little-endian <c>INT32</c>s, read by
/// <see cref="Io.Transform.Common.ZoneDatTransformer"/>. See docs/retail/formats/terrain-heightmap.md.
/// </summary>
public class ZoneDat : DataFile {
	/// <summary>
	/// Width and height of the cell grid as shifts — 7 or 8. The original reads them and then
	/// re-derives both from the heightmap image's own dimensions.
	/// </summary>
	public int WidthShift { get; set; }

	/// <inheritdoc cref="WidthShift"/>
	public int HeightShift { get; set; }

	/// <summary>World units per cell as a shift: 13, 14 or 15.</summary>
	public int CellShift { get; set; }

	/// <summary>Multiplicative height scale applied to each cell's raw byte. Nonzero.</summary>
	public int HeightScale { get; set; }
}
