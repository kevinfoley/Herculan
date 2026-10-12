namespace HercWorks.Core.Data.File.Dat.Shell;

/// <summary>
/// FILE - ES1's /SHELL0.VOL/CFG/DAMAGE.DAT — 72 bytes, ES2's value table (<see cref="DamageRepairCost"/>) with ten internal
/// percentages and a fixed run of 18 weapon values in place of the counted column
/// (docs/es1/formats/herc-catalogs.md#cfgdamagedat).
/// </summary>
public class Es1DamageRepairCost {
	/// <summary>How many internal percentages the file holds: ES2's nine and a tenth, 0 in retail.</summary>
	public const int InternalCount = 10;

	/// <summary>How many weapon values the file holds, with no count of its own.</summary>
	public const int WeaponCount = 18;

	/// <inheritdoc cref="DamageRepairCost.ChassisScale"/>
	public short ChassisScale { get; set; }

	/// <inheritdoc cref="DamageRepairCost.ExternalGroupPercent"/>
	public short[] ExternalGroupPercent { get; set; } = new short[DamageRepairCost.ExternalGroupCount];

	/// <summary>Per-component share of a chassis price, Q10: ES2's nine, then 0. Retail: 100 100 50 50 25 100 25 25 25 0.</summary>
	public short[] InternalPercent { get; set; } = new short[InternalCount];

	/// <inheritdoc cref="DamageRepairCost.WeaponScale"/>
	public short WeaponScale { get; set; }

	/// <summary>One raw value per weapon, 18, scaled by <see cref="WeaponScale"/> at load; which weapon each prices is an Open item of the doc.</summary>
	public short[] WeaponValue { get; set; } = new short[WeaponCount];
}
