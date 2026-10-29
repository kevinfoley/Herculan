using HercWorks.Vol;

namespace HercWorks.Core.Data.File.Dat.Sim;

/// <summary>
/// FILE - /ZONE/DAT/ZONEXXXX.DAT — the 16-byte per-zone header <c>Terrain_LoadZone</c> reads beside
/// the zone's heightmap, four little-endian <c>INT32</c>s, read by
/// <see cref="Io.Transform.Common.ZoneDatTransformer"/>. See docs/retail/formats/zone-terrain.md.
/// </summary>
public class ZoneDat : DataFile {
	/// <summary>
	/// Width and height of the cell grid as shifts — 7 or 8. The original reads them and then
	/// re-derives both from the heightmap image's own dimensions.
	/// </summary>
	public int WidthShift { get; set; }

	/// <inheritdoc cref="WidthShift"/>
	public int HeightShift { get; set; }

	/// <summary>World units per cell as a shift: 12 to 15 in the retail zones.</summary>
	public int CellShift { get; set; }

	/// <summary>
	/// Multiplicative height scale applied to each cell's raw byte: 90 to 256 in the retail zones. What a value outside
	/// that does: docs/retail/simulation/terrain-heightmap.md#the-height-scales-range.
	/// </summary>
	public int HeightScale { get; set; }
}
