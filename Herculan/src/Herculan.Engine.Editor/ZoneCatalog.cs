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
