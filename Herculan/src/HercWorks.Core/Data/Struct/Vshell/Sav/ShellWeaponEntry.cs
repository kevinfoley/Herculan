namespace HercWorks.Core.Data.Struct.Vshell.Sav;

/// <summary>
/// The save form of a weapon unit, all five shorts of the ten-byte in-memory record, in on-disk
/// order. <see cref="Hercs.UiWeaponEntry"/> is the six-byte <c>gam\*.dat</c> form of the same
/// record. See <c>docs/retail/formats/herc-catalogs.md#the-weapon-unit-record</c>.
/// </summary>
public class ShellWeaponEntry {
	/// <summary><c>+0x00</c>, the weapon catalog id.</summary>
	public WeaponLUT? Id { get; set; }

	/// <summary>
	/// <c>+0x02</c>, the armory class index, derived from the id by <c>Weapon_ClassIndexForId</c>
	/// (<c>004119b4</c>) whenever a unit is constructed; <c>-1</c> for the three Bull weapons.
	/// </summary>
	public short ClassIndex { get; set; }

	/// <summary><c>+0x04</c>, the hardpoint condition <c>Herc_FitMount</c> (<c>004114ec</c>) writes into the status block when the unit is fitted.</summary>
	public short FitCondition { get; set; }

	/// <summary><c>+0x06</c>, the unit's condition.</summary>
	public short Condition { get; set; }

	/// <summary><c>+0x08</c>, the ammo type — the guidance kind, or 5 for none.</summary>
	public MissileType? Guidance { get; set; }
}
