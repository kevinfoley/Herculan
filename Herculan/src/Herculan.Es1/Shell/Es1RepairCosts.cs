using HercWorks.Core.Data.File.Dat.Shell;
using HercWorks.Core.Io.Transform.Shell;
using Herculan.Engine.Content;
using Herculan.Engine.Numerics;

namespace Herculan.Es1.Shell;

/// <summary>
/// The shell's value tables, which <c>LoadDamageDat_Shell</c> (<c>000387a8</c>) expands from <c>cfg\damage.dat</c> into the
/// rows at <c>000a0580</c> and the weapon values at <c>000ab332</c> (docs/es1/formats/herc-catalogs.md#the-load), and the
/// functions that price a HERC from them: the after-mission repair's (docs/es1/shell/between-missions.md#repairs) and the
/// scrap value. The class is this engine's bundle of those globals; every member is retail's.
/// </summary>
public sealed class Es1RepairCosts {
	/// <summary>The chassis rows the load fills, 0 to 7.</summary>
	public const int ChassisCount = 8;

	/// <summary>The words of a row, <c>0x42</c> bytes, a status block's: six external values from word 0, internal values from word 13 (<c>+0x1a</c>).</summary>
	private const int RowWords = 33;

	/// <summary>The external areas and the internal words the costs read.</summary>
	private const int ExternalAreas = 6;

	/// <summary>The weapon an empty mount carries, <c>0x11</c>.</summary>
	private const short EmptyMount = Es1HercRecord.EmptyMount;

	/// <summary>The chassis prices in tons, the eight words at <c>000808b8</c> in the image.</summary>
	public static readonly short[] ChassisPrices = [50, 60, 80, 100, 120, 140, 170, 200];

	/// <summary><c>RepairTargetTable</c> (<c>00081790</c>), six words: the condition each repair level brings a HERC to.</summary>
	public static readonly short[] RepairTargets = [100, 90, 74, 51, 0, 0];

	/// <summary><c>RepairLevelTable</c> (<c>0008179c</c>), five words: each repair level's lowest condition.</summary>
	public static readonly short[] RepairLevels = [91, 75, 52, 1, 0];

	/// <summary>
	/// The word after <see cref="RepairLevels"/>, at <c>000817a6</c>: the bytes "cf" that open the string
	/// <c>cfg\damage.dat</c>, which <see cref="LevelStepCost"/> and <see cref="FloorForLevel"/> read as level 5's floor.
	/// </summary>
	private const short WordPastLevels = 0x6663;

	/// <summary>The kinds <see cref="UnitValueCost"/> prices: internal parts, external areas, mounts.</summary>
	private const short InternalKind = 0x784, ExternalKind = 0x785, MountKind = 0x786;

	/// <summary>
	/// <c>Repair_StepCost</c> (<c>000ab212</c>), an <c>int16</c>: the cost of one level up of SYSTEM REPAIR's selected
	/// item, which <c>SystemRepairMenu_Ctor</c> and <c>SystemRepairMenu_Refresh</c> set, the + and − cases also set, and
	/// <c>Menu_Open</c> reads (docs/es1/shell/repair-and-build.md#system-repair--screen-1920).
	/// </summary>
	public short StepCost { get; set; }

	/// <summary>The rows at <c>000a0580</c>, by chassis; zero until the load.</summary>
	private readonly short[,] _rows = new short[ChassisCount, RowWords];

	/// <summary>The 18 weapon values at <c>000ab332</c>; zero until the load.</summary>
	private readonly short[] _weapons = new short[Es1DamageRepairCost.WeaponCount];

	/// <summary>
	/// <c>LoadDamageDat_Shell</c> (<c>000387a8</c>), from <c>Shell_LoadData</c>: <c>cfg\damage.dat</c> from
	/// <paramref name="content"/>, and for each chassis <i>s</i>, its price <see cref="ChassisPrices"/>[<i>s</i>] × 1000,
	/// the six external words and the ten internal words from word 13 of row <i>s</i> each
	/// <c>Math_Q10Multiply</c>(<c>Math_Q10Multiply</c>(price, percentage), chassis scale), and each weapon value
	/// <c>Math_Q10Multiply</c>(value, weapon scale). Retail reads what a missing file's stream gives; here a file that is
	/// missing or does not read is reported and leaves the tables zero, an engine guard.
	/// </summary>
	public static Es1RepairCosts Load(GameContent content) {
		var costs = new Es1RepairCosts();
		Es1DamageRepairCost? file = null;
		try {
			file = content.Read("cfg", "DAMAGE.DAT") is { } bytes ? new Es1DamageRepairCostTransformer().Parse(bytes) : null;
		} catch (InvalidDataException ex) {
			Console.Error.WriteLine($"cfg\\damage.dat does not read ({ex.Message}).");
		}

		if (file == null) {
			Console.Error.WriteLine("cfg\\damage.dat is missing or unreadable: the shell's repair and scrap values are 0.");
			return costs;
		}

		for (int s = 0; s < ChassisCount; s++) {
			int price = ChassisPrices[s] * 1000;
			for (int g = 0; g < file.ExternalGroupPercent.Length; g++) {
				costs._rows[s, g] = Expand(price, file.ExternalGroupPercent[g], file.ChassisScale);
			}

			for (int i = 0; i < file.InternalPercent.Length; i++) {
				costs._rows[s, Es1HercRecord.FirstInternalWord + i] = Expand(price, file.InternalPercent[i], file.ChassisScale);
			}
		}

		for (int w = 0; w < costs._weapons.Length; w++) {
			costs._weapons[w] = (short)SimMath.Q10MultiplyRounded(file.WeaponValue[w], file.WeaponScale);
		}

		return costs;

		static short Expand(int price, short percent, short scale) =>
			(short)SimMath.Q10MultiplyRounded(SimMath.Q10MultiplyRounded(price, percent), scale);
	}

	/// <summary>
	/// Word <paramref name="word"/> of <paramref name="chassis"/>' row. Retail indexes with no bounds test, reading rows 8 to
	/// 15 of the 16 <c>00038e2f</c> builds and past them, and past a row's words; a chassis outside 0 to 7 or a word
	/// outside the row reads 0 here, an engine guard.
	/// </summary>
	private short Row(short chassis, int word) =>
		chassis is >= 0 and < ChassisCount && word is >= 0 and < RowWords ? _rows[chassis, word] : (short)0;

	/// <summary>
	/// The word of <c>000ab332</c> for <paramref name="weapon"/>. Retail reads past the 18 for a weapon past them; such a
	/// weapon is worth 0 here, an engine guard.
	/// </summary>
	public short WeaponValue(short weapon) => weapon >= 0 && weapon < _weapons.Length ? _weapons[weapon] : (short)0;

	/// <summary>
	/// <c>Repair_LevelForCondition</c> (<c>000389fb</c>)(<paramref name="condition"/>): the first of the five
	/// <see cref="RepairLevels"/> at or below the condition, or 0 when none is.
	/// </summary>
	public static short LevelForCondition(short condition) {
		for (short level = 0; level < RepairLevels.Length; level++) {
			if (RepairLevels[level] <= condition) {
				return level;
			}
		}

		return 0;
	}

	/// <summary>
	/// <c>Repair_TargetForLevel</c> (<c>00038a30</c>)(<paramref name="level"/>)
	/// (docs/es1/shell/repair-and-build.md#system-repair--screen-1920): 100, <see cref="RepairTargets"/>[0], for level 0,
	/// and <see cref="RepairTargets"/>[level] + 1, the floor of the level above, as a 16-bit add for any other. Retail
	/// indexes with no bounds test; a level outside the six words gives 0 here, an engine guard.
	/// </summary>
	public static short TargetForLevel(short level) {
		if (level is < 0 or >= 6) {
			return 0;
		}

		return level == 0 ? RepairTargets[0] : (short)(RepairTargets[level] + 1);
	}

	/// <summary>
	/// <c>Repair_FloorForLevel</c> (<c>00038a5c</c>)(<paramref name="level"/>): <see cref="RepairLevels"/>[level], the
	/// level's own floor, level 5 reading <see cref="WordPastLevels"/>. Retail indexes with no bounds test; any other level
	/// outside the table gives 0 here, an engine guard.
	/// </summary>
	public static short FloorForLevel(int level) => level switch {
		>= 0 and < 5 => RepairLevels[level],
		5 => WordPastLevels,
		_ => 0,
	};

	/// <summary>
	/// <c>Repair_LevelStepCost</c> (<c>00038a72</c>)(<paramref name="up"/>, <paramref name="condition"/>,
	/// <paramref name="value"/>) (docs/es1/shell/repair-and-build.md#system-repair--screen-1920), with <i>L</i>
	/// <see cref="LevelForCondition"/>(condition): 0 for <i>L</i> below 0 or above 4; up, (100 − condition) × value / 100 at
	/// level 0 and (<see cref="FloorForLevel"/>(<i>L</i> − 1) − condition) × value / 100 at any other; down, (100 −
	/// <see cref="FloorForLevel"/>(<i>L</i>)) × value / 100 for a condition of 100 and (condition −
	/// <see cref="FloorForLevel"/>(<i>L</i> + 1)) × value / 100 for any other. Each product is 32-bit and each division
	/// truncates toward zero. ES1's port of the function ES2's shell ports in its own repair costs, as
	/// docs/herculan/planning.md#how-the-two-games-share-code has each game's shell ported apart.
	/// </summary>
	public static int LevelStepCost(bool up, short condition, short value) {
		short level = LevelForCondition(condition);
		if (level is < 0 or > 4) {
			return 0;
		}

		if (!up) {
			return condition == 100
				? (100 - FloorForLevel(level)) * value / 100
				: (condition - FloorForLevel(level + 1)) * value / 100;
		}

		return level == 0 ? (RepairTargets[0] - condition) * value / 100 : (FloorForLevel(level - 1) - condition) * value / 100;
	}

	/// <summary>
	/// <c>Repair_UnitValueCost</c> (<c>00038bb9</c>)(<paramref name="up"/>, <paramref name="condition"/>,
	/// <paramref name="index"/>, <paramref name="kind"/>, <paramref name="chassis"/>)
	/// (docs/es1/shell/repair-and-build.md#system-repair--screen-1920): <see cref="LevelStepCost"/> priced with the
	/// chassis row's internal word <paramref name="index"/> for <c>0x784</c>, its word <paramref name="index"/> for
	/// <c>0x785</c>, and <see cref="WeaponValue"/>(<paramref name="index"/>) for <c>0x786</c>, so a weapon is priced by its
	/// mount's number, not by the weapon it carries; any other kind answers the kind.
	/// </summary>
	public int UnitValueCost(bool up, short condition, short index, short kind, short chassis) => kind switch {
		InternalKind => LevelStepCost(up, condition, Row(chassis, Es1HercRecord.FirstInternalWord + index)),
		ExternalKind => LevelStepCost(up, condition, Row(chassis, index)),
		MountKind => LevelStepCost(up, condition, WeaponValue(index)),
		_ => kind,
	};

	/// <summary>
	/// <c>Repair_ItemCost</c> (<c>00038b64</c>)(<paramref name="target"/>, <paramref name="condition"/>, <paramref name="value"/>):
	/// 0 when the condition is at or above the target, otherwise (<see cref="RepairTargets"/>[<see cref="LevelForCondition"/>(target)]
	/// − the condition) × the value / 100, truncated. Its callers pass a level's target, so the first factor is the target.
	/// </summary>
	public static int ItemCost(short target, short condition, short value) =>
		target <= condition ? 0 : (RepairTargets[LevelForCondition(target)] - condition) * value / 100;

	/// <summary>
	/// <c>Repair_HercCost</c> (<c>00038c50</c>)(<paramref name="target"/>, <paramref name="herc"/>): the sum of
	/// <see cref="ItemCost"/> over the six external areas' averages (<see cref="Es1HercRecord.ExternalAverage"/>) with the
	/// row's first six words, internal words 0 to 8 with the row's words from 13, and each mount below the mount count whose
	/// weapon is not <see cref="EmptyMount"/> and whose per-mount word is not 0 with the weapon's value.
	/// </summary>
	public int HercCost(short target, Es1HercRecord herc) {
		int sum = 0;
		for (int area = 0; area < ExternalAreas; area++) {
			sum += ItemCost(target, herc.ExternalAverage(area), Row(herc.Chassis, area));
		}

		for (int i = 0; i < Es1HercRecord.InternalCount; i++) {
			int word = Es1HercRecord.FirstInternalWord + i;
			sum += ItemCost(target, herc.Status[word], Row(herc.Chassis, word));
		}

		for (int mount = 0; mount < herc.Weapons.Length; mount++) {
			short weapon = herc.Weapons[mount];
			if (weapon != EmptyMount && herc.MountStatus(mount) != 0) {
				sum += ItemCost(target, herc.MountStatus(mount), WeaponValue(weapon));
			}
		}

		return sum;
	}

	/// <summary>
	/// <c>00038d6c</c>(<paramref name="herc"/>), what the HERC's frame is worth: over the six external areas, the row's word ×
	/// the area's average (<see cref="Es1HercRecord.ExternalAverage"/>) / 100, and over internal words 0 to 8, the row's word
	/// from 13 × the internal word / 100, each term truncated; the mounts are not counted. <c>Herc_ScrapToSalvage</c> adds it
	/// to <c>Career_Salvage</c> when it is given no value, and <c>Hangar_FormatScrapValue</c> (<see cref="Menus.Es1ScrapAlert.FormatValue"/>) prints it in tons.
	/// </summary>
	public int ScrapValue(Es1HercRecord herc) {
		int sum = 0;
		for (int area = 0; area < ExternalAreas; area++) {
			sum += Row(herc.Chassis, area) * herc.ExternalAverage(area) / 100;
		}

		for (int i = 0; i < Es1HercRecord.InternalCount; i++) {
			int word = Es1HercRecord.FirstInternalWord + i;
			sum += Row(herc.Chassis, word) * herc.Status[word] / 100;
		}

		return sum;
	}
}
