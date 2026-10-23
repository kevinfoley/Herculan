using HercWorks.Core.Io.Transform.Common;
using Herculan.Engine.Content;
using Herculan.Engine.Terrain;

namespace Herculan.Engine.Editor;

/// <summary>
/// One terrain zone the install has, which File &gt; New mission can put a mission in: its number, as the
/// <c>script.dat</c> header names it, and its size in world units.
/// </summary>
internal sealed record ZoneChoice(short Zone, int Width, int Height) {
	/// <summary>The retail missions played in it, by name (<see cref="ZoneCatalog"/>); empty for none.</summary>
	public IReadOnlyList<string> UsedBy { get; init; } = [];

	/// <summary>The zone, its size and the retail missions played in it: "Zone 502 (12.6 by 12.6 km): used by C5_02, C5_04".</summary>
	public string Label =>
		$"Zone {Zone} ({Kilometres(Width)} by {Kilometres(Height)} km): "
		+ (UsedBy.Count > 0 ? $"used by {string.Join(", ", UsedBy)}" : "used by no retail mission");

	private static string Kilometres(int worldUnits) =>
		(worldUnits / Render.WorldScale.WorldUnitsPerMeter / 1000f).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
}

/// <summary>
/// The zones File &gt; New mission offers (<see cref="List"/>). A zone is the pair of <c>dat\zoneNNNN</c> and
/// <c>dba\zoneNNNN.dba</c> resources <c>Terrain_LoadZone</c> (<c>0042789c</c>) reads (docs/retail/formats/zone-terrain.md);
/// nothing ties a zone to a theater, which the header names separately (docs/retail/formats/script-dat.md#header-format).
/// </summary>
internal static class MissionZones {
	private const string ZonePrefix = "ZONE";
	private const string HeaderExtension = ".DAT";
	private const string HeightmapExtension = ".DBA";

	/// <summary>Every zone <paramref name="simulator"/>, DBSIM's mount, holds both resources of, in number order.</summary>
	public static IReadOnlyList<ZoneChoice> List(GameContent simulator) {
		var zones = new List<ZoneChoice>();
		foreach (string name in simulator.ListFolder(TerrainZoneLoader.HeaderFolder)) {
			if (!name.StartsWith(ZonePrefix, StringComparison.OrdinalIgnoreCase)
					|| !name.EndsWith(HeaderExtension, StringComparison.OrdinalIgnoreCase)
					|| !short.TryParse(name[ZonePrefix.Length..^HeaderExtension.Length], out short zone)
					|| !simulator.Contains(TerrainZoneLoader.HeightmapFolder, $"{ZonePrefix}{zone}{HeightmapExtension}")
					|| new ZoneDatTransformer().Parse(simulator.Read(TerrainZoneLoader.HeaderFolder, name)) is not { } header) {
				continue;
			}

			zones.Add(new ZoneChoice(zone, 1 << (header.WidthShift + header.CellShift), 1 << (header.HeightShift + header.CellShift)));
		}

		return zones.OrderBy(zone => zone.Zone).ToList();
	}
}
