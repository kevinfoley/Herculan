using HercWorks.Core.Data.Struct;
using HercWorks.Core.Data.Struct.Vshell.Sav;

namespace Herculan.Engine.Shell;

/// <summary>
/// One weapon unit — the ten-byte record a mount holds and the armory's stock lists are made of
/// (docs/retail/formats/herc-catalogs.md#the-weapon-unit-record). Fitting and stripping move the unit itself
/// between a mount and the stock, so what it carries goes with it.
/// </summary>
public sealed class ShellWeaponUnit {
	public ShellWeaponUnit(int weaponId, int fitCondition = 100, int condition = 100, int guidance = NoGuidance,
			int? classIndex = null) {
		WeaponId = weaponId;
		ClassIndex = classIndex ?? ClassIndexForId(weaponId);
		FitCondition = fitCondition;
		Condition = condition;
		Guidance = guidance;
	}

	/// <summary>The kind an unguided unit carries, <c>WeaponUnit_Ctor</c>'s 5, and what the arming screen reads for an empty mount.</summary>
	public const int NoGuidance = 5;

	/// <summary><c>+0x00</c>, the weapon catalog id.</summary>
	public int WeaponId { get; }

	/// <summary>
	/// <c>+0x02</c>, the armory class index: derived from the id whenever a unit is made, and read back
	/// from the save as it was written.
	/// </summary>
	public int ClassIndex { get; }

	/// <summary>
	/// <c>Weapon_ClassIndexForId</c> (<c>004119b4</c>) — the id's position in the thirty-entry table at
	/// <c>0046f868</c>, which holds ids 0-18 and 22-32, so the three Bull weapons are <c>-1</c>.
	/// </summary>
	private static int ClassIndexForId(int weaponId) => weaponId switch {
		>= 0 and <= 18 => weaponId,
		>= 22 and <= 32 => weaponId - 3,
		_ => -1,
	};

	/// <summary><c>+0x04</c> — what <c>Herc_FitMount</c> (<c>004114ec</c>) writes into the hardpoint's status entry when the unit is fitted.</summary>
	public int FitCondition { get; }

	/// <summary><c>+0x06</c>.</summary>
	public int Condition { get; }

	/// <summary><c>+0x08</c>, the guidance kind: SARH 0, ARH 1, ARM 2, EO 3, or <see cref="NoGuidance"/>.</summary>
	public int Guidance { get; internal set; }

	internal static ShellWeaponUnit From(ShellWeaponEntry entry) =>
		new(entry.Id?.Id ?? 0, entry.FitCondition, entry.Condition, entry.Guidance?.Id ?? NoGuidance, entry.ClassIndex);

	/// <summary><c>WeaponUnit_WriteSaveForm</c> (<c>00411aff</c>)'s five shorts, as the save model holds them.</summary>
	internal ShellWeaponEntry ToEntry() => new() {
		Id = WeaponLUT.GetById(WeaponId),
		ClassIndex = (short)ClassIndex,
		FitCondition = (short)FitCondition,
		Condition = (short)Condition,
		Guidance = MissileType.GetById(Guidance),
	};
}
