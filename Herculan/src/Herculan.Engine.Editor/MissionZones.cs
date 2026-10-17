using HercWorks.Core.Io.Transform.Common;
using Herculan.Engine.Content;
using Herculan.Engine.Shell;
using Herculan.Engine.Terrain;
using Herculan.Engine.World;

namespace Herculan.Engine.Editor;

/// <summary>One theater a retail mission is flown on in a zone, and the missions that fly it there.</summary>
internal sealed record ZoneTheater(short Theater, string? Bank, IReadOnlyList<string> Missions);

/// <summary>
/// One terrain zone the install has, which File &gt; New mission can put a mission in: its number, as the
/// <c>script.dat</c> header names it, its size in world units, and the theaters the retail missions in it are flown on.
/// </summary>
internal sealed record ZoneChoice(short Zone, int Width, int Height, IReadOnlyList<ZoneTheater> Theaters) {
	/// <summary>
	/// The theater a new mission's header patch is given for this zone: the first campaign theater a retail mission flies
	/// it on, else the first theater any does, else the practice theater, which a mission no archive holds is flown on
	/// whatever its patch says (<see cref="ShellTrainingLaunch.PracticeTheater"/>).
	/// </summary>
	public short Theater =>
		Theaters.FirstOrDefault(theater => theater.Theater != ShellTrainingLaunch.PracticeTheater)?.Theater
		?? Theaters.FirstOrDefault()?.Theater
		?? ShellTrainingLaunch.PracticeTheater;

	/// <summary>The list's line: the zone, then each theater it is flown on with the missions flying it.</summary>
	public string Label => $"ZONE{Zone}" + (Theaters.Count == 0
		? " - no retail mission"
		: " - " + string.Join("; ", Theaters.Select(theater =>
			$"theater {theater.Theater}{(theater.Bank is { } bank ? $" ({bank})" : "")}: {string.Join(", ", theater.Missions)}")));
}

/// <summary>
/// The zones File &gt; New mission offers (<see cref="List"/>). A zone is the pair of <c>dat\zoneNNNN</c> and
/// <c>dba\zoneNNNN.dba</c> resources <c>Terrain_LoadZone</c> (<c>0042789c</c>) reads (docs/retail/formats/zone-terrain.md);
/// nothing ties a zone to a theater, which the header names separately (docs/retail/formats/script-dat.md#header-format),
/// so the theater shown for a zone is what the retail missions in it are flown on: each mission's zone as its load writes
/// the header, and the theater its launch writes over it (<see cref="MissionFileLaunch.LaunchTheater"/>).
/// </summary>
internal static class MissionZones {
	private const string ZonePrefix = "ZONE";
	private const string HeaderExtension = ".DAT";
	private const string HeightmapExtension = ".DBA";

	/// <summary>
	/// Every zone <paramref name="simulator"/>, DBSIM's mount, holds both resources of, in number order, each with the
	/// theaters the missions in <paramref name="shell"/>, VSHELL's mount, are flown on in it.
	/// </summary>
	public static IReadOnlyList<ZoneChoice> List(GameContent simulator, GameContent shell) {
		var flown = Flown(shell);
		var banks = new Dictionary<short, string?>();
		var zones = new List<ZoneChoice>();
		foreach (string name in simulator.ListFolder(TerrainZoneLoader.HeaderFolder)) {
			if (!name.StartsWith(ZonePrefix, StringComparison.OrdinalIgnoreCase)
					|| !name.EndsWith(HeaderExtension, StringComparison.OrdinalIgnoreCase)
					|| !short.TryParse(name[ZonePrefix.Length..^HeaderExtension.Length], out short zone)
					|| !simulator.Contains(TerrainZoneLoader.HeightmapFolder, $"{ZonePrefix}{zone}{HeightmapExtension}")
					|| new ZoneDatTransformer().Parse(simulator.Read(TerrainZoneLoader.HeaderFolder, name)) is not { } header) {
				continue;
			}

			var theaters = flown.Where(mission => mission.Zone == zone)
				.GroupBy(mission => mission.Theater)
				.Select(group => new ZoneTheater(group.Key, Bank(simulator, group.Key, banks), group.Select(mission => mission.Name).ToList()))
				.ToList();
			zones.Add(new ZoneChoice(zone, 1 << (header.WidthShift + header.CellShift), 1 << (header.HeightShift + header.CellShift), theaters));
		}

		return zones.OrderBy(zone => zone.Zone).ToList();
	}

	// Each archive mission's zone, as its load writes the header with every campaign flag zero and every roll 0, as
	// HercWorks.Query lint generates it, and the theater its launch writes; in name order.
	private static List<(string Name, short Zone, short Theater)> Flown(GameContent shell) {
		var missions = new List<(string, short, short)>();
		foreach (string file in MissionFileLaunch.List(shell)) {
			if (shell.Read(MissionFileLaunch.MissionFolder, file) is not { } msn) {
				continue;
			}

			short zone;
			try {
				zone = MissionGenerator.Load(msn, null, new short[MissionGenerator.CampaignFlagCount],
					new short[MissionGenerator.ClearListLength], _ => 0).Header[1];
			} catch (Exception ex) when (ex is InvalidDataException or IndexOutOfRangeException or ArgumentException) {
				continue;
			}

			missions.Add((Path.GetFileNameWithoutExtension(file).ToUpperInvariant(), zone, MissionFileLaunch.LaunchTheater(shell, file)));
		}

		return missions;
	}

	// The theater's terrain bank, as its day descriptor names it, or null when the install has none.
	private static string? Bank(GameContent simulator, short theater, Dictionary<short, string?> banks) {
		if (!banks.TryGetValue(theater, out string? bank)) {
			try {
				bank = TheaterDescriptor.Load(simulator, theater).TerrainBankName;
			} catch (Exception ex) when (ex is FileNotFoundException or ArgumentOutOfRangeException or InvalidDataException) {
				bank = null;
			}

			banks[theater] = bank;
		}

		return bank;
	}
}
