using HercWorks.Core.Data.File.Dat.Shell;
using HercWorks.Core.Io.Transform.Shell;
using Herculan.Engine.Content;

namespace Herculan.Engine.Shell;

/// <summary>
/// Which of a machine's three condition arrays a repair-bay selection addresses. These are
/// <c>HercStatus_Get</c>'s own three modes (<c>00411d06</c>), and the repair screen's
/// <c>(column, row)</c> pair resolves into one of them through <c>Repair_HotspotCategory</c> (<c>00433410</c>) — so the same
/// number is the accessor mode, the cost table to price against, and the list the row belongs to.
/// </summary>
public enum ShellRepairCategory {
	/// <summary>The six external component groups, each the mean of two or three facets.</summary>
	ExternalGroup = 0,

	/// <summary>The nine internal components.</summary>
	Internal = 1,

	/// <summary>The per-hardpoint conditions, one per mount slot.</summary>
	Hardpoint = 2,
}

/// <summary>
/// The repair bay's price list: <c>gam\damage.dat</c> expanded against <c>gam\herc_inf.dat</c>'s
/// chassis prices, plus the three cost functions that read it.
///
/// <para><b>The expansion is the loader's, not the file's.</b> <c>damage.dat</c> carries fifteen
/// shares and two scale factors and nothing chassis-specific; <c>hercdisp.cpp</c> turns them into a
/// 9 x 6 external table, a 9 x 9 internal table and a 33-entry weapon table by multiplying each share
/// by the chassis price through <see cref="Q10"/>. Every later cost is a plain percentage of one of
/// those unit values, which is why the whole model can live behind three small functions. See
/// docs/retail/formats/herc-catalogs.md and docs/retail/shell/armory.md.</para>
///
/// <para><b>Two different targets, two different functions.</b> <see cref="HercCost"/> is
/// <c>Repair_HercCost</c> and prices a rebuild to a target the caller names — 100 everywhere the
/// screen uses it. <see cref="SelectedItemCost"/> is <c>Repair_LevelStepCost</c> (<c>00413871</c>), which the detail panel
/// quotes for the selected component, and it lifts that component to the floor of the <i>next band
/// up</i> rather than to 100. They are not two spellings of one figure and the screen shows both at
/// once.</para>
/// </summary>
public sealed class ShellRepairCosts {
	/// <summary>The catalog file the component and weapon shares come from.</summary>
	public const string ValuesResourceName = "DAMAGE.DAT";

	/// <summary>The catalog file the chassis prices come from.</summary>
	public const string ChassisResourceName = "HERC_INF.DAT";

	/// <summary>The archive folder both catalogs live in.</summary>
	public const string CatalogFolder = "gam";

	/// <summary>
	/// What one repair level lifts a component to, by level — <c>RepairTargetTable</c> at
	/// <c>0046fd78</c>.
	/// </summary>
	public static readonly short[] RepairTarget = { 100, 89, 79, 59, 29, 0 };

	/// <summary>
	/// The floor of each level's condition band — <c>RepairLevelTable</c> at <c>0046fd84</c>.
	/// </summary>
	public static readonly short[] RepairLevelFloor = { 90, 80, 60, 30, 1, 0 };

	/// <summary>How many repair levels there are. Level 5 is a destroyed component.</summary>
	public const int LevelCount = 6;

	/// <summary>The salvage pool is kept in kilograms and a chassis price in tons.</summary>
	public const int KilogramsPerTon = 1000;

	private readonly short[,] _externalGroupValue;
	private readonly short[,] _internalValue;
	private readonly short[] _weaponValue;
	private readonly short[] _priceTons;

	private ShellRepairCosts(short[,] externalGroupValue, short[,] internalValue, short[] weaponValue,
			short[] priceTons) {
		_externalGroupValue = externalGroupValue;
		_internalValue = internalValue;
		_weaponValue = weaponValue;
		_priceTons = priceTons;
	}

	/// <summary>How many chassis types the tables cover — nine in retail.</summary>
	public int ChassisCount => _externalGroupValue.GetLength(0);

	/// <summary>
	/// Reads both catalogs out of <c>SHELL0.VOL</c> and expands them. Returns null when either is
	/// missing or unreadable, in which case the repair screen draws its labels and no figures rather
	/// than quoting invented ones.
	/// </summary>
	public static ShellRepairCosts? Load(GameContent content) {
		if (content.Read(CatalogFolder, ValuesResourceName) is not { } valueBytes
			|| new DamageRepairCostTransformer().Parse(valueBytes) is not { } values
			|| content.Read(CatalogFolder, ChassisResourceName) is not { } chassisBytes
			|| new HercInfoTransformer().Parse(chassisBytes) is not { Data.Length: > 0 } chassis) {
			return null;
		}

		return Expand(values, Array.ConvertAll(chassis.Data, entry => entry.SalvageReq));
	}

	/// <summary>
	/// The loader's own expansion, given the shares and one price in tons per chassis:
	/// each unit value is the chassis price in kilograms scaled by the component's share and then by
	/// the file's chassis scale, both as Q10 fractions.
	/// </summary>
	public static ShellRepairCosts Expand(DamageRepairCost values, IReadOnlyList<short> priceInTons) {
		int chassisCount = priceInTons.Count;
		var external = new short[chassisCount, DamageRepairCost.ExternalGroupCount];
		var internals = new short[chassisCount, DamageRepairCost.InternalCount];

		for (int type = 0; type < chassisCount; type++) {
			int price = priceInTons[type] * KilogramsPerTon;

			for (int group = 0; group < DamageRepairCost.ExternalGroupCount; group++) {
				external[type, group] =
					(short)Q10(values.ChassisScale, Q10(values.ExternalGroupPercent[group], price));
			}

			for (int component = 0; component < DamageRepairCost.InternalCount; component++) {
				internals[type, component] =
					(short)Q10(values.ChassisScale, Q10(values.InternalPercent[component], price));
			}
		}

		var weapons = new short[values.WeaponValue.Length];
		for (int id = 0; id < weapons.Length; id++) {
			weapons[id] = (short)Q10(values.WeaponScale, values.WeaponValue[id]);
		}

		return new ShellRepairCosts(external, internals, weapons, priceInTons.ToArray());
	}

	/// <summary>
	/// <c>FixedMulQ10</c> (<c>00450a84</c>) — the Q10 fixed-point multiply the whole cost model is built on. Both of
	/// <c>damage.dat</c>'s scale factors are fractions of 1024, not percentages, and a figure that
	/// looks 2% out is usually this.
	/// </summary>
	public static int Q10(int a, int b) => a * b >> 10;

	/// <summary>
	/// <c>Repair_LevelForCondition</c> (<c>0041381c</c>) — the first level whose band floor is at or
	/// below <paramref name="condition"/>, so 90 and up is level 0 and 0 is level 5.
	/// </summary>
	public static int LevelForCondition(int condition) {
		for (int level = 0; level < LevelCount; level++) {
			if (RepairLevelFloor[level] <= condition) {
				return level;
			}
		}

		return LevelCount - 1;
	}

	/// <summary>The unit value one component is priced against, or 0 when the tables do not cover it.</summary>
	public int UnitValue(int chassisType, ShellRepairCategory category, int index) {
		if (chassisType < 0 || index < 0) {
			return 0;
		}

		switch (category) {
			case ShellRepairCategory.ExternalGroup:
				return chassisType < _externalGroupValue.GetLength(0)
					&& index < _externalGroupValue.GetLength(1)
						? _externalGroupValue[chassisType, index] : 0;
			case ShellRepairCategory.Internal:
				return chassisType < _internalValue.GetLength(0) && index < _internalValue.GetLength(1)
					? _internalValue[chassisType, index] : 0;
			default:
				// The hardpoint table is indexed by weapon id and not by mount slot, so it is the same
				// column the scrap screen values a mount out of.
				return index < _weaponValue.Length ? _weaponValue[index] : 0;
		}
	}

	/// <summary>
	/// <c>Repair_ItemCost</c> (<c>0041392a</c>) — what it costs to lift one component from
	/// <paramref name="condition"/> to <paramref name="target"/>. Nothing, if it is already there.
	/// </summary>
	public static int ItemCost(int unitValue, int condition, int target) =>
		target <= condition ? 0 : (RepairTarget[LevelForCondition(target)] - condition) * unitValue / 100;

	/// <summary>
	/// <c>Repair_LevelStepCost</c> (<c>00413871</c>) in its <c>param_3 != 0</c> form — the per-item figure the repair screen's
	/// detail panel quotes for whatever is selected.
	///
	/// <para><b>It repairs one band, not to full.</b> The target is the floor of the level above the
	/// one the component is in: a component at 70 is level 2 and is priced up to 80, not to 100. Only
	/// a component already at level 0 is priced all the way to 100. That is the same ladder
	/// <c>Repair_Auto</c> steps down, which is why the two agree on what a level costs.</para>
	/// </summary>
	public static int SelectedItemCost(int unitValue, int condition) {
		int level = LevelForCondition(condition);
		if (level < 0 || level >= LevelCount) {
			return 0;
		}

		return (TargetForLevel(level) - condition) * unitValue / 100;
	}

	/// <summary>
	/// <c>Repair_TargetForLevel</c> (<c>00413838</c>) — what one REPAIR lifts a component in
	/// <paramref name="level"/> to: 100 for level 0, otherwise one more than the level's
	/// <see cref="RepairTarget"/>, which is the floor of the band above.
	/// </summary>
	public static int TargetForLevel(int level) =>
		level == 0 ? RepairTarget[0] : RepairTarget[level] + 1;

	/// <summary>
	/// <c>Repair_HercCost</c> (<c>004139f7</c>) — the whole machine to <paramref name="target"/>:
	/// every external group, every internal, and every occupied mount whose condition is not already
	/// zero. A mount at 0 is destroyed rather than damaged and is not billed for.
	/// </summary>
	public int HercCost(ShellBayMachine? machine, int target = 100) {
		if (machine == null) {
			return 0;
		}

		int total = 0;
		for (int group = 0; group < DamageRepairCost.ExternalGroupCount; group++) {
			total += ItemCost(UnitValue(machine.ChassisType, ShellRepairCategory.ExternalGroup, group),
				machine.Condition(ShellRepairCategory.ExternalGroup, group), target);
		}

		for (int component = 0; component < DamageRepairCost.InternalCount; component++) {
			total += ItemCost(UnitValue(machine.ChassisType, ShellRepairCategory.Internal, component),
				machine.Condition(ShellRepairCategory.Internal, component), target);
		}

		for (int mount = 0; mount < machine.MountCapacity; mount++) {
			int weaponId = machine.WeaponAt(mount);
			int condition = machine.Condition(ShellRepairCategory.Hardpoint, mount);
			if (weaponId == 0 || condition == 0) {
				continue;
			}

			total += ItemCost(UnitValue(machine.ChassisType, ShellRepairCategory.Hardpoint, weaponId),
				condition, target);
		}

		return total;
	}

	/// <summary>
	/// <c>Herc_ScrapValue</c> (<c>00413b50</c>) — what scrapping a machine yields, in kilograms
	/// (docs/retail/shell/armory.md#scrapping): each external group and internal at its condition's share of
	/// its unit value, and each mount below <see cref="ShellHangar.ReturnToStockCondition"/> likewise
	/// against its weapon's value. Every term truncates on its own. A machine not yet built is worth
	/// <c>(100 - built) / 100</c> of its price, divided first as the original does, so nothing between 1%
	/// and 99%.
	/// </summary>
	public int ScrapValue(ShellBayMachine? machine) {
		if (machine == null) {
			return 0;
		}

		int type = machine.ChassisType;
		if (!machine.IsBuilt) {
			int price = type >= 0 && type < _priceTons.Length ? _priceTons[type] : 0;
			return (100 - machine.BuildPercent) / 100 * price * KilogramsPerTon;
		}

		int total = 0;
		for (int group = 0; group < DamageRepairCost.ExternalGroupCount; group++) {
			total += machine.Condition(ShellRepairCategory.ExternalGroup, group)
				* UnitValue(type, ShellRepairCategory.ExternalGroup, group) / 100;
		}

		for (int component = 0; component < DamageRepairCost.InternalCount; component++) {
			total += machine.Condition(ShellRepairCategory.Internal, component)
				* UnitValue(type, ShellRepairCategory.Internal, component) / 100;
		}

		// No test that the mount is fitted, as in the original; an empty slot is valued against weapon 0.
		for (int slot = 0; slot < machine.MountCapacity; slot++) {
			int condition = machine.Condition(ShellRepairCategory.Hardpoint, slot);
			if (condition < ShellHangar.ReturnToStockCondition) {
				total += condition * UnitValue(type, ShellRepairCategory.Hardpoint, machine.WeaponAt(slot)) / 100;
			}
		}

		return total;
	}

	/// <summary>
	/// The detail panel's own cost — <c>Repair_SelectionCost</c> (<c>00411454</c>), which picks the unit value for the selection
	/// and then takes <see cref="SelectedItemCost"/> of it. A hardpoint prices against the fitted
	/// weapon's id rather than against the slot number.
	/// </summary>
	public int SelectionCost(ShellBayMachine? machine, ShellRepairCategory category, int index) {
		if (machine == null) {
			return 0;
		}

		int valueIndex = category == ShellRepairCategory.Hardpoint ? machine.WeaponAt(index) : index;
		return SelectedItemCost(UnitValue(machine.ChassisType, category, valueIndex),
			machine.Condition(category, index));
	}
}
