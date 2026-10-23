namespace HercWorks.Core.Data.File.Msn;

/// <summary>
/// What a set of missions fits on each mount of each herc type: every row-12 record whose own type is set, a variant option
/// (GUID <c>-1</c>) and a record of any condition included, read as stored, no condition tested and no variant drawn; a
/// record of type <c>-1</c>, which draws its fit or takes its base's, is passed over (why that counts each retail fit once:
/// docs/herculan/handoff-editor-roster.md#a-cybrids-weapons). A mount is a fit slot, the index into the
/// record's weapon and ammunition-type arrays (docs/retail/simulation/weapon-mounts.md#the-join--mechloadout_constructweaponmounts-0040fff8):
/// a slot holding a weapon id, <c>0</c> (no weapon) included, is fitted; <c>-1</c> is not.
///
/// <para>The mission editor offers a Cybrid herc's mounts the weapons the install's retail missions fit there, by the user's
/// choice (docs/herculan/handoff-editor-roster.md#a-cybrids-weapons).</para>
/// </summary>
public sealed class RetailMountFits {
	private readonly Dictionary<(int Type, int Slot), Dictionary<short, Dictionary<short, int>>> _fits = new();
	private readonly Dictionary<int, int> _records = new();

	private RetailMountFits(int missions) => Missions = missions;

	/// <summary>How many missions were read.</summary>
	public int Missions { get; }

	/// <summary>The fits of every row-12 record of <paramref name="missions"/>, by the rules in the class summary.</summary>
	public static RetailMountFits Scan(IReadOnlyCollection<MissionFile> missions) {
		var fits = new RetailMountFits(missions.Count);
		foreach (var herc in missions.SelectMany(mission => mission.Mechs ?? [])) {
			if (herc.TypeIndex < 0) {
				continue;
			}

			fits._records[herc.TypeIndex] = fits._records.GetValueOrDefault(herc.TypeIndex) + 1;
			for (int slot = 0; slot < herc.WeaponRefs.Length; slot++) {
				short weapon = herc.WeaponRefs[slot];
				if (weapon < 0) {
					continue;
				}

				short ammo = slot < herc.WeaponSecondary.Length ? herc.WeaponSecondary[slot] : RosterFit.Filler;
				if (!fits._fits.TryGetValue((herc.TypeIndex, slot), out var weapons)) {
					fits._fits[(herc.TypeIndex, slot)] = weapons = new();
				}

				if (!weapons.TryGetValue(weapon, out var ammoCounts)) {
					weapons[weapon] = ammoCounts = new();
				}

				ammoCounts[ammo] = ammoCounts.GetValueOrDefault(ammo) + 1;
			}
		}

		return fits;
	}

	/// <summary>How many records of type <paramref name="type"/> were read.</summary>
	public int Records(int type) => _records.GetValueOrDefault(type);

	/// <summary>The fit slots of type <paramref name="type"/> some record fits, ascending.</summary>
	public IReadOnlyList<int> Slots(int type) =>
		_fits.Keys.Where(key => key.Type == type).Select(key => key.Slot).Order().ToList();

	/// <summary>
	/// The weapon ids the records of type <paramref name="type"/> fit in slot <paramref name="slot"/>, <c>0</c> where one
	/// leaves it empty, ascending; empty where none fits it.
	/// </summary>
	public IReadOnlyList<short> Weapons(int type, int slot) =>
		_fits.TryGetValue((type, slot), out var weapons) ? weapons.Keys.Order().ToList() : [];

	/// <summary>How many records of type <paramref name="type"/> fit <paramref name="weapon"/> in slot <paramref name="slot"/>.</summary>
	public int Count(int type, int slot, short weapon) =>
		_fits.TryGetValue((type, slot), out var weapons) && weapons.TryGetValue(weapon, out var ammo) ? ammo.Values.Sum() : 0;

	/// <summary>
	/// The ammunition type the records of type <paramref name="type"/> carry most often with <paramref name="weapon"/> in slot
	/// <paramref name="slot"/>, the lowest on a tie; null where none fits it there.
	/// </summary>
	public short? Ammo(int type, int slot, short weapon) =>
		_fits.TryGetValue((type, slot), out var weapons) && weapons.TryGetValue(weapon, out var ammo)
			? ammo.OrderByDescending(pair => pair.Value).ThenBy(pair => pair.Key).First().Key
			: null;

	/// <summary>Every ammunition type the records of type <paramref name="type"/> carry with <paramref name="weapon"/> in slot <paramref name="slot"/>, with its count.</summary>
	public IReadOnlyDictionary<short, int> AmmoCounts(int type, int slot, short weapon) =>
		_fits.TryGetValue((type, slot), out var weapons) && weapons.TryGetValue(weapon, out var ammo) ? ammo : new Dictionary<short, int>();
}
