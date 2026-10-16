using HercWorks.Core.Data.Struct;

namespace HercWorks.UI;

/// <summary>
/// One entry of the weapon dropdown used wherever a mission file stores a weapon fit as raw ids
/// (script.dat's Herc roster). Names come from <see cref="WeaponLUT"/>, whose ids are the same 0-32
/// catalog ids the loadout arrays carry.
///
/// <para><b>The two files spell an unused slot differently.</b> script.dat's fixed ten-slot fit uses
/// <c>-1</c> for the hardpoints a Herc does not have, so its dropdown gets an "(empty)" entry of its
/// own; player.mec's list is only as long as the machine's real slot count and uses <c>NONE</c>
/// (id 0) instead, so <paramref name="includeEmptySlot"/> keeps <c>-1</c> off a list where it would
/// only invite writing a value retail never writes.</para>
///
/// <para>Ids a file carries that <see cref="WeaponLUT"/> has no name for are kept as their own
/// entries, so hand-edited data round-trips instead of being rejected by the combo column.</para>
/// </summary>
internal sealed class WeaponFitOption {
	/// <summary>What an unused script.dat hardpoint slot carries.</summary>
	public const short EmptySlot = -1;

	public required short Id { get; init; }
	public required string Label { get; init; }

	public override string ToString() => Label;

	/// <summary>
	/// The named weapons plus an entry for every id in <paramref name="idsInUse"/> that has no name,
	/// in id order, with the empty-slot entry first where the format uses one.
	/// </summary>
	public static List<WeaponFitOption> Build(IEnumerable<short> idsInUse, bool includeEmptySlot) {
		var options = WeaponLUT.Values()
			.Select(weapon => new WeaponFitOption { Id = (short)weapon.Id, Label = weapon.Name })
			.ToList();

		if (includeEmptySlot) {
			options.Add(new WeaponFitOption { Id = EmptySlot, Label = "(empty)" });
		}

		var known = options.Select(o => o.Id).ToHashSet();
		foreach (short id in idsInUse.Distinct().Where(id => !known.Contains(id))) {
			options.Add(new WeaponFitOption { Id = id, Label = $"Unknown id {id}" });
		}

		return options.OrderBy(o => o.Id).ToList();
	}

	/// <summary>
	/// A fit as words, for a roster grid: the fitted slots named in order, launchers carrying the
	/// ammunition they are loaded with. Slots holding no weapon are left out — both spellings of
	/// that, since the two loadout formats disagree (see the class remarks).
	/// </summary>
	public static string Summarize(IReadOnlyList<short> weapons, IReadOnlyList<short> ammo) {
		var fitted = weapons
			.Select((weapon, slot) => (weapon, ammo: slot < ammo.Count ? ammo[slot] : AmmoTypeOption.Filler))
			.Where(slot => slot.weapon != EmptySlot && slot.weapon != WeaponLUT.None.Id)
			.Select(slot => Describe(slot.weapon, slot.ammo))
			.ToList();

		return fitted.Count == 0 ? "(no weapons)" : string.Join(", ", fitted);
	}

	private static string Describe(short weapon, short ammo) {
		string name = WeaponLUT.GetById(weapon)?.Name ?? $"?{weapon}";
		return IsLauncher(weapon)
			? $"{name} [{MissileType.GetById(ammo)?.Abbrev ?? ammo.ToString()}]"
			: name;
	}

	/// <summary>
	/// Whether a weapon id is a missile launcher — the only kind of mount that reads the loadout's
	/// second array at all. These four are exactly the ids whose <c>WEAPONS.DAT</c> mount template
	/// carries the "resolve by (Rocket, key) search" sentinel instead of a direct PROJ.DAT index,
	/// so every other weapon fires the same projectile whatever the ammunition slot says. Hardcoded
	/// rather than read from the template table, which lives in a VOL this form has not loaded.
	/// </summary>
	public static bool IsLauncher(short weaponId) =>
		weaponId == WeaponLUT.Msl6.Id || weaponId == WeaponLUT.Msl8.Id
		|| weaponId == WeaponLUT.Msl10.Id || weaponId == WeaponLUT.Flymsl.Id;
}

/// <summary>
/// One entry of the ammunition dropdown — the loadout's parallel second array, which is the
/// guidance type a launcher's rounds are loaded with. Values are <see cref="MissileType"/> ids,
/// matching the PROJ.DAT Rocket subtype a launcher's mount resolves through.
///
/// <para><see cref="MissileType.None"/> (5) is retail's filler in every non-launcher slot. On a
/// launcher it is not rejected: DBSIM's mount factory rewrites a 5 to 0, so such a launcher fires
/// SARH.</para>
/// </summary>
internal sealed class AmmoTypeOption {
	/// <summary>The filler value retail writes in every slot that is not a launcher.</summary>
	public const short Filler = 5;

	/// <summary>
	/// The list's label for <see cref="Filler"/>: what it means on a launcher, which fires it as SARH. The
	/// dropdown only ever opens on a launcher, since the forms cancel an ammunition edit on any other mount.
	/// </summary>
	public const string FillerLabel = "Default (SARH)";

	/// <summary>
	/// Shows <c>(none)</c> in the ammunition cell of a mount that is not a launcher, where the filler
	/// means nothing rather than <see cref="FillerLabel"/>'s SARH.
	/// </summary>
	public static void FormatNonLauncherCell(DataGridViewCellFormattingEventArgs e, short ammoType) {
		if (ammoType == Filler) {
			e.Value = "(none)";
			e.FormattingApplied = true;
		}
	}

	public required short Id { get; init; }
	public required string Label { get; init; }

	public override string ToString() => Label;

	public static List<AmmoTypeOption> Build(IEnumerable<short> idsInUse) {
		var options = MissileType.Values()
			.Select(type => new AmmoTypeOption {
				Id = (short)type.Id,
				Label = type.Id == Filler ? FillerLabel : $"{type.Abbrev} — {type.Name}"
			})
			.ToList();

		var known = options.Select(o => o.Id).ToHashSet();
		foreach (short id in idsInUse.Distinct().Where(id => !known.Contains(id))) {
			options.Add(new AmmoTypeOption { Id = id, Label = $"Unknown type {id}" });
		}

		return options.OrderBy(o => o.Id).ToList();
	}
}
