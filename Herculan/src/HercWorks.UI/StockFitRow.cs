namespace HercWorks.UI;

/// <summary>
/// One occupied hardpoint of an <c>INI_*.DAT</c> stock fit in StockFitForm: the hardpoint index the
/// file writes before each weapon unit, and the unit's three catalog-form fields. The file lists
/// occupied hardpoints only, so an empty hardpoint is a missing row, not a row holding <c>NONE</c>.
/// <see cref="AmmoType"/> only means anything on a launcher (see <see cref="WeaponFitOption.IsLauncher"/>).
/// </summary>
internal sealed class StockFitRow {
	public short Hardpoint { get; set; }
	public short WeaponId { get; set; }
	public short Condition { get; set; }
	public short AmmoType { get; set; }

	public bool IsLauncher => WeaponFitOption.IsLauncher(WeaponId);
}
