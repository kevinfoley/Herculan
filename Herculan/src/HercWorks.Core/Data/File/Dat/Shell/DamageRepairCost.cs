namespace HercWorks.Core.Data.File.Dat.Shell;

/// <summary>
/// FILE - /SHELL/GAM/DAMAGE.DAT — 102 bytes, the unit values the repair bay and the scrap screen
/// price against. Read whole at load by <c>hercdisp.cpp</c> and immediately expanded against
/// <c>herc_inf.dat</c>'s chassis prices into the three in-memory tables the cost model actually uses;
/// see <c>docs/retail/formats/herc-catalogs.md</c>.
///
/// <para><b>The percentages are Q10 fractions, not percentages.</b> Both scale factors go through a
/// fixed-point multiply that shifts right by 10, so <see cref="ChassisScale"/> 800 is 800/1024 and
/// not 800%. A figure that looks 2% out is usually this.</para>
///
/// <para>The two count-prefixed halves are asymmetric: the six external and nine internal
/// percentages are fixed-length runs with no count of their own, and only the weapon column carries
/// one. That is the file, not an omission — the group and internal counts are properties of the
/// status block.</para>
/// </summary>
public class DamageRepairCost {
	/// <summary>How many external component groups every chassis has — the status block's own six.</summary>
	public const int ExternalGroupCount = 6;

	/// <summary>How many internal components every chassis has. The status block's tenth slot is the
	/// machine's overall condition and is not a component, so it is not priced.</summary>
	public const int InternalCount = 9;

	/// <summary>Q10 scale applied to every chassis-derived unit value — 800 in retail, so 800/1024.</summary>
	public short ChassisScale { get; set; }

	/// <summary>Per-group share of the chassis price, Q10. Retail: 100 50 50 50 100 100.</summary>
	public short[] ExternalGroupPercent { get; set; } = new short[ExternalGroupCount];

	/// <summary>Per-component share of the chassis price, Q10. Retail: 100 100 50 50 25 100 25 25 25.</summary>
	public short[] InternalPercent { get; set; } = new short[InternalCount];

	/// <summary>Q10 scale applied to every weapon value — 1000 in retail.</summary>
	public short WeaponScale { get; set; }

	/// <summary>
	/// One raw value per weapon id, 33 in retail — the weapon's <c>weapons.dat</c> price times 100 for
	/// every id but the three Bull weapons, which are deliberately zero where the catalog prices them.
	/// </summary>
	public short[] WeaponValue { get; set; } = System.Array.Empty<short>();

	/// <summary>A chassis price is stored in tons and charged in kilograms.</summary>
	public const int KilogramsPerTon = 1000;

	/// <summary>
	/// External group <paramref name="group"/>'s unit value, in kilograms, for a chassis priced at
	/// <paramref name="priceTons"/> — <c>LoadDamageDat_Shell</c> (<c>00413628</c>)'s expansion, the
	/// share and then <see cref="ChassisScale"/> each applied as a Q10 fraction of the price. See
	/// docs/retail/formats/herc-catalogs.md#gamdamagedat--the-component-and-weapon-value-table.
	/// </summary>
	public int ExternalGroupValue(int priceTons, int group) =>
		Q10(ChassisScale, Q10(ExternalGroupPercent[group], priceTons * KilogramsPerTon));

	/// <summary>Internal <paramref name="component"/>'s unit value, as <see cref="ExternalGroupValue"/>.</summary>
	public int InternalValue(int priceTons, int component) =>
		Q10(ChassisScale, Q10(InternalPercent[component], priceTons * KilogramsPerTon));

	/// <summary>Weapon <paramref name="weaponId"/>'s unit value: <see cref="WeaponValue"/> scaled by <see cref="WeaponScale"/>.</summary>
	public int WeaponUnitValue(int weaponId) => Q10(WeaponScale, WeaponValue[weaponId]);

	/// <summary><c>FixedMulQ10</c> (<c>00450a84</c>), <c>(a * b) &gt;&gt; 10</c>.</summary>
	private static int Q10(int a, int b) => a * b >> 10;
}
