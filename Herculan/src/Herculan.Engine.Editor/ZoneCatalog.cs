using HercWorks.Core.Data.File.Dat.Sim;
using HercWorks.Core.Data.File.Msn;
using Herculan.Engine.Content;

namespace Herculan.Engine.Editor;

/// <summary>
/// The install's terrain zones (<see cref="MissionZones"/>), each with the retail missions played in it, for File &gt; New
/// mission's zone choice and the Mission Settings window: built once per editor
/// run, the first time either asks, from the missions the archives hold (<see cref="ArchivedMissions"/>). A mission's zone is
/// header word 1 (<c>script.dat</c> offset 2, docs/retail/formats/script-dat.md#header-format) as its row-2 base patches it
/// (<see cref="MissionLayout.HeaderWords"/>); no launch writes over it.
/// </summary>
internal sealed class ZoneCatalog {
	/// <summary>The header word that holds the zone, <c>script.dat</c> offset 2.</summary>
	public const int ZoneWord = 2 / 2;

	private readonly GameContent _simulator;
	private readonly ArchivedMissions _missions;
	private IReadOnlyList<ZoneChoice>? _zones;

	/// <param name="simulator">DBSIM's mount of the install, which holds the zones.</param>
	/// <param name="missions">The missions the install's archives hold.</param>
	public ZoneCatalog(GameContent simulator, ArchivedMissions missions) {
		_simulator = simulator;
		_missions = missions;
	}

	/// <summary>Every zone the install holds, in number order, each with the retail missions played in it.</summary>
	public IReadOnlyList<ZoneChoice> Zones => _zones ??= Build();

	/// <summary>Zone <paramref name="zone"/> as its <see cref="ZoneChoice.Label"/>, or by its number alone when the install has no such zone.</summary>
	public string Describe(short zone) => Zones.FirstOrDefault(choice => choice.Zone == zone)?.Label ?? $"Zone {zone}";

	/// <summary>A mission's own terrain <paramref name="own"/>, played as zone <paramref name="zone"/>, and its size.</summary>
	public static string DescribeOwn(short zone, ZoneFiles own) {
		string side = ZoneChoice.Kilometres(own.Side << own.CellSizeShift);
		return $"Zone {zone}: the mission's own terrain ({side} by {side} km)";
	}

	/// <summary>
	/// The zone number a mission's own terrain is played as: the highest up to <see cref="MaxOwnZone"/> that no zone of
	/// the install has (docs/herculan/plan-zone-editing.md#decisions).
	/// </summary>
	public short FreeZone() {
		var taken = Zones.Select(choice => choice.Zone).ToHashSet();
		for (short zone = MaxOwnZone; zone >= 0; zone--) {
			if (!taken.Contains(zone)) {
				return zone;
			}
		}

		throw new InvalidOperationException($"The install has a zone for every number up to {MaxOwnZone}.");
	}

	/// <summary>The highest number a mission's own terrain is played as: zone file names up to it keep to 8.3.</summary>
	public const short MaxOwnZone = 9999;

	private List<ZoneChoice> Build() {
		var usedBy = new Dictionary<short, List<string>>();
		foreach (var mission in _missions.All) {
			var words = MissionLayout.Build(mission.Mission).HeaderWords;
			if (words[ZoneWord] == MissionSettingsPatch.Unset(ZoneWord)) {
				continue;
			}

			if (!usedBy.TryGetValue(words[ZoneWord], out var names)) {
				usedBy[words[ZoneWord]] = names = new List<string>();
			}

			names.Add(mission.Name);
		}

		return MissionZones.List(_simulator)
			.Select(zone => zone with { UsedBy = usedBy.GetValueOrDefault(zone.Zone)?.Order(StringComparer.Ordinal).ToList() ?? [] })
			.ToList();
	}
}
