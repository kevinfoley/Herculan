using HercWorks.Core.Data.File.Dat.Shell;

namespace Herculan.Es1.Shell;

/// <summary>
/// What the shell does to a career with a mission's results, past reading them (docs/es1/shell/between-missions.md#after-a-mission):
/// the stock's additions and resupply, the pilots' shares of the salvage, the lost squadmates' replacements and their
/// HERCs scrapped, the marked HERC moved on, and the auto repair. Each method is a retail function on the roster
/// (<see cref="Es1ShellMission.Roster"/>), its weapon stock 0, the one the dword <c>000aad62</c> points to, the hangar
/// (<see cref="Es1ShellMission.Hangar"/>) and the value tables (<see cref="Es1ShellData.RepairCosts"/>), citing its symbol.
/// </summary>
public sealed class Es1CareerAfterMission {
	/// <summary>The first of the mission flags <see cref="StockItemsFromFlags"/> reads, flag 8.</summary>
	private const int FirstItemFlag = 8;

	/// <summary>The words at <c>00081834</c>: the weapon each of the flags 8 to 14 counts.</summary>
	private static readonly short[] FlagItems = [13, 2, 8, 11, 12, 4, 3];

	/// <summary><c>Inventory_ResupplyList</c> (<c>00080874</c>), the weapons <see cref="ResupplyWeapons"/> tries in order; −1 ends it in the image.</summary>
	private static readonly short[] ResupplyList = [2, 8, 4, 12, 3, 1, 7, 11, 10, 13, 9, 6, 0];

	/// <summary>The pushes <see cref="ResupplyWeapons"/> makes at most, and the count below which a slot takes one.</summary>
	private const int ResupplyPushes = 5, ResupplyBelow = 2;

	/// <summary>The word each stocked or resupplied weapon pushes, and the condition <see cref="Es1HercRecord.SetAllStatus"/> gives a finished HERC.</summary>
	private const short Full = 100;

	/// <summary>The mount condition at or above which a scrapped HERC's weapon goes back to the stock, <c>0x50</c>.</summary>
	private const short ReturnToStockCondition = 80;

	/// <summary>The words at <c>000808d8</c>: by chassis, the career missions a marked HERC takes to finish.</summary>
	internal static readonly short[] MarkedHercMissions = [1, 1, 2, 2, 2, 3, 3, 3];

	/// <summary>
	/// The first pilot entry <see cref="AutoRepairSquad"/> repairs from, by the record's word <c>+0x1f5</c>: 0 entry 4, so
	/// none; 1 entry 1, the squadmates; 2 entry 0, all four.
	/// </summary>
	private static readonly short[] AutoRepairFirst = [4, 1, 0];

	private readonly Es1ShellMission _mission;
	private readonly Es1ShellData _data;

	/// <param name="mission">The mission globals: the roster, the hangar, the flags and the results' lost pilots' count.</param>
	/// <param name="data">The shared data: the generator and the value tables.</param>
	public Es1CareerAfterMission(Es1ShellMission mission, Es1ShellData data) {
		_mission = mission;
		_data = data;
	}

	private Es1CareerRoster Roster => _mission.Roster;

	/// <summary>The stock the dword <c>000aad62</c> points to, the roster's stock 0.</summary>
	private Es1WeaponStock Stock => Roster.Stocks[0];

	private Es1RepairCosts Costs => _data.RepairCosts;

	/// <summary>
	/// <c>Shell_StockItemsFromFlags</c> (<c>00038e48</c>): for each of the flags 8 to 14, a 100 pushed onto the stock's
	/// slot of the weapon <see cref="FlagItems"/> names for it, once per count the flag holds (none for a flag of 0 or
	/// below), and the flag set to 0.
	/// </summary>
	public void StockItemsFromFlags() {
		short[] flags = _mission.Flags;
		for (int k = 0; k < FlagItems.Length; k++) {
			for (short n = 0; n < flags[FirstItemFlag + k]; n++) {
				Stock.Add(FlagItems[k], Full);
			}

			flags[FirstItemFlag + k] = 0;
		}
	}

	/// <summary>
	/// <c>Inventory_ResupplyWeapons</c> (<c>00028375</c>)(<paramref name="stock"/>) (docs/es1/shell/between-missions.md#resupply):
	/// the weapons of <see cref="ResupplyList"/> in order, until five pushes are made: a 100 pushed onto the slot of each the
	/// current career record makes available (<see cref="Es1EquipmentState.IsAvailable"/>(weapon, 1)) and whose slot holds
	/// fewer than 2 (<see cref="Es1WeaponStock.CountIn"/>).
	/// </summary>
	public void ResupplyWeapons(Es1WeaponStock stock) {
		int pushes = 0;
		for (int i = 0; i < ResupplyList.Length && pushes < ResupplyPushes; i++) {
			short weapon = ResupplyList[i];
			if (_mission.CurrentRecord is { } record && record.Equipment.IsAvailable(weapon, 1) != 0 && stock.CountIn(weapon) < ResupplyBelow) {
				stock.Add(weapon, Full);
				pushes++;
			}
		}
	}

	/// <summary>
	/// <c>CareerRecord_FinishMission</c> (<c>000160cb</c>)(<paramref name="record"/>) (docs/es1/shell/between-missions.md#lost-squadmates):
	/// <i>n</i> the entries on strength with a slot (<see cref="Es1CareerRecord.CountPilots"/>) and the share
	/// <c>Career_Salvage</c> / <i>n</i>, divided unsigned; <i>n</i> times the current pilot stepped
	/// (<see cref="Es1CareerRecord.NextPilot"/>, which moves the hangar's selected slot) and the share stored as its
	/// <see cref="Es1PilotEntry.PayShare"/>; then <see cref="ReplaceLostPilots"/> and the missions played up 1. Retail
	/// divides by 0 with no entry counted; here no share is stored then, an engine guard.
	/// </summary>
	public void FinishMission(Es1CareerRecord record, Es1Hangar hangar) {
		short count = record.CountPilots();
		if (count > 0) {
			int share = (int)((uint)Roster.Salvage / (uint)count);
			for (int i = 0; i < count; i++) {
				record.NextPilot(hangar);
				if (record.CurrentPilot is >= 0 and < Es1CareerRecord.PilotCount) {
					record.Pilots[record.CurrentPilot].PayShare = share;
				}
			}
		}

		ReplaceLostPilots(record, hangar);
		record.MissionsPlayed++;
	}

	/// <summary>
	/// <c>CareerRecord_ReplaceLostPilots</c> (<c>00015e0b</c>)(<paramref name="record"/>)
	/// (docs/es1/shell/between-missions.md#lost-squadmates): for each entry <i>i</i>, 0 to 3, on strength with a slot and a
	/// condition of 0, the word <c>0007ebca</c> (<see cref="Es1ShellResults.LostPilots"/>) up 1; and when <i>i</i> is not the
	/// player's entry, with <i>k</i> its word <c>+4</c> and <i>v</i> its word <c>+6</c>: the HERC in its slot scrapped
	/// (<see cref="ScrapSlot"/>), the entry set up again (<see cref="Es1PilotEntry.InitRandom"/> on <c>Shell_RandomState</c>,
	/// which leaves it no slot), its name group the roster's word <c>+0xb</c> + 8(<i>i</i> − 1) + 2<i>k</i>, its name and
	/// <c>+6</c> <i>v</i>, <c>+4</c> (<i>k</i> + 1) mod 3, and when that is 0, <c>+6</c> (<i>v</i> + 1) mod 3. Retail reads
	/// before the roster's name groups for an <i>i</i> of 0 that is not the player's; the name group is left as the set-up
	/// leaves it here then, and a draw with no generator is 0, engine guards.
	/// </summary>
	public void ReplaceLostPilots(Es1CareerRecord record, Es1Hangar hangar) {
		for (int i = 0; i < Es1CareerRecord.PilotCount; i++) {
			var entry = record.Pilots[i];
			if (!record.OnStrengthWithSlot(i) || entry.Condition != 0) {
				continue;
			}

			_mission.Results.LostPilots++;
			if (i == record.PlayerEntry) {
				continue;
			}

			short k = entry.Word4, v = entry.Word6;
			ScrapSlot(hangar, entry.Slot);
			entry.InitRandom(bound => _data.Random?.Below(bound) ?? 0);
			int group = 4 * (i - 1) + k;
			if (group >= 0 && group < Roster.NameGroups.Length) {
				entry.NameGroup = Roster.NameGroups[group];
			}

			entry.Name = v;
			entry.Word6 = v;
			entry.Word4 = (short)((k + 1) % 3);
			if (entry.Word4 == 0) {
				entry.Word6 = (short)((entry.Word6 + 1) % 3);
			}
		}
	}

	/// <summary>
	/// <c>Hangar_ScrapSlot</c> (<c>0002a20d</c>)(<paramref name="slot"/>) (docs/es1/shell/between-missions.md#lost-squadmates):
	/// for a slot that is not −1 and holds a HERC, the HERC scrapped (<see cref="ScrapToSalvage"/>(HERC, 0)), destroyed
	/// (<c>Herc_Dtor</c> (<c>000232a4</c>)(HERC, 3), which frees its diagram, <see cref="Hangar.Es1HercDiagrams.Destroy"/>),
	/// the slot emptied and the HERC count down 1.
	/// </summary>
	public void ScrapSlot(Es1Hangar hangar, short slot) {
		if (hangar.HercIn(slot) is not { } herc) {
			return;
		}

		ScrapToSalvage(herc, 0);
		_data.HercDiagrams.Destroy(herc);
		hangar.Slots[slot] = null;
		hangar.HercCount--;
	}

	/// <summary>
	/// <c>Herc_ScrapToSalvage</c> (<c>00024cec</c>)(<paramref name="herc"/>, <paramref name="value"/>)
	/// (docs/es1/shell/between-missions.md#lost-squadmates): for each mount below the mount count whose weapon is not
	/// empty, with its per-mount word below 80 the weapon's value × the word / 100 added to <c>Career_Salvage</c>, otherwise
	/// the word pushed onto the stock's slot of the weapon (<see cref="Es1WeaponStock.Add"/>); then <paramref name="value"/>
	/// added to <c>Career_Salvage</c>, or, when it is 0, the HERC's <see cref="Es1RepairCosts.ScrapValue"/>.
	/// </summary>
	public void ScrapToSalvage(Es1HercRecord herc, int value) {
		for (int mount = 0; mount < herc.Weapons.Length; mount++) {
			short weapon = herc.Weapons[mount];
			if (weapon == Es1HercRecord.EmptyMount) {
				continue;
			}

			short condition = herc.MountStatus(mount);
			if (condition < ReturnToStockCondition) {
				Roster.Salvage += Costs.WeaponValue(weapon) * condition / 100;
			} else {
				Stock.Add(weapon, condition);
			}
		}

		Roster.Salvage += value == 0 ? Costs.ScrapValue(herc) : value;
	}

	/// <summary>
	/// <c>Hangar_AdvanceMarkedHerc</c> (<c>00029e01</c>) (docs/es1/shell/between-missions.md#the-marked-herc): nothing
	/// unless <see cref="Es1Hangar.HasMarkedHerc"/>. While <see cref="Es1Hangar.MarkedSlotWord"/> is above 0, it down 1,
	/// the HERC's diagram built again, and <see cref="Es1Hangar.Dword"/> set to (<i>v</i> × 80 × <i>p</i> / 10000 + (100 −
	/// <i>p</i>) × <i>v</i>) / 100, <i>v</i> the chassis' price (<see cref="Es1RepairCosts.ChassisPrices"/>) and <i>p</i>
	/// <see cref="MarkedHercProgress"/>. When the word is then below 1, every status word of the HERC 100
	/// (<see cref="Es1HercRecord.SetAllStatus"/>), its diagram built again, the marked slot −1 and the dword 0. Each build,
	/// <c>Herc_BuildDiagram</c> (<c>00023883</c>)(HERC, the lowered word) (<see cref="Hangar.Es1HercDiagrams.Build"/>), runs
	/// unless <c>Shell_LowMemory</c> (<c>0007fd09</c>) is set, which this engine does not set.
	/// </summary>
	public static void AdvanceMarkedHerc(Es1Hangar hangar, Hangar.Es1HercDiagrams diagrams) {
		if (!hangar.HasMarkedHerc() || hangar.HercIn(hangar.MarkedSlot) is not { } herc) {
			return;
		}

		if (hangar.MarkedSlotWord > 0) {
			hangar.MarkedSlotWord--;
			diagrams.Build(herc, hangar.MarkedSlotWord);
			int price = Price(herc.Chassis);
			int progress = MarkedHercProgress(hangar);
			hangar.Dword = (price * 80 * progress / 10000 + (100 - progress) * price) / 100;
		}

		if (hangar.MarkedSlotWord < 1) {
			herc.SetAllStatus(Full);
			diagrams.Build(herc, hangar.MarkedSlotWord);
			hangar.MarkedSlot = -1;
			hangar.Dword = 0;
		}
	}

	/// <summary>
	/// <c>Hangar_MarkedHercProgress</c> (<c>00029f93</c>): (<i>c</i> − <see cref="Es1Hangar.MarkedSlotWord"/>) × 100 /
	/// <i>c</i>, <i>c</i> the marked HERC's chassis' word of <see cref="MarkedHercMissions"/>. Retail reads the word before
	/// the table for a marked slot that holds no HERC, and past it for a chassis past 7; either gives 0 here, an engine guard.
	/// </summary>
	public static short MarkedHercProgress(Es1Hangar hangar) {
		short chassis = hangar.HercIn(hangar.MarkedSlot)?.Chassis ?? -1;
		if (chassis < 0 || chassis >= MarkedHercMissions.Length) {
			return 0;
		}

		int missions = MarkedHercMissions[chassis];
		return (short)((missions - hangar.MarkedSlotWord) * 100 / missions);
	}

	/// <summary>The chassis' word of <c>000808b8</c>; retail reads past the eight for a chassis past them, 0 here, an engine guard.</summary>
	private static short Price(short chassis) =>
		chassis >= 0 && chassis < Es1RepairCosts.ChassisPrices.Length ? Es1RepairCosts.ChassisPrices[chassis] : (short)0;

	/// <summary>
	/// <c>CareerRecord_AutoRepairSquad</c> (<c>0001601b</c>)(<paramref name="record"/>) (docs/es1/shell/between-missions.md#repairs):
	/// <see cref="AutoRepairFrom"/> from the entry <see cref="AutoRepairFirst"/> gives for the record's word <c>+0x1f5</c>.
	/// Retail passes on the register <c>EDI</c> as its caller left it for any other word; such a word repairs none here, an
	/// engine guard.
	/// </summary>
	public void AutoRepairSquad(Es1CareerRecord record, Es1Hangar hangar) {
		short mode = record.Word1F5;
		short first = mode >= 0 && mode < AutoRepairFirst.Length ? AutoRepairFirst[mode] : (short)Es1CareerRecord.PilotCount;
		AutoRepairFrom(record, first, hangar);
	}

	/// <summary>
	/// <c>CareerRecord_AutoRepairFrom</c> (<c>0001605c</c>)(<paramref name="record"/>, <paramref name="first"/>): for each
	/// entry from <paramref name="first"/> to 3 on strength with a slot, <see cref="RepairAuto"/>(its pay share, its slot).
	/// </summary>
	public void AutoRepairFrom(Es1CareerRecord record, short first, Es1Hangar hangar) {
		for (int i = first; i < Es1CareerRecord.PilotCount; i++) {
			if (record.OnStrengthWithSlot(i)) {
				RepairAuto((uint)record.Pilots[i].PayShare, record.Pilots[i].Slot, hangar);
			}
		}
	}

	/// <summary>
	/// <c>Repair_Auto</c> (<c>00029bf3</c>)(<paramref name="budget"/>, <paramref name="slot"/>)
	/// (docs/es1/shell/between-missions.md#repairs): nothing for −1 or a slot holding no HERC. Otherwise ES2's ladder walk
	/// with ES1's ladder: from level 0, one level down while the cost, <see cref="Es1RepairCosts.HercCost"/> of the level's
	/// target (<see cref="Es1RepairCosts.RepairTargets"/>), is above the budget, compared unsigned, and the level is below
	/// the HERC's own, <see cref="Es1RepairCosts.LevelForCondition"/> of <see cref="Es1HercRecord.OverallCondition"/>. The
	/// walk is ported here and in ES2's shell each, as docs/herculan/planning.md#how-the-two-games-share-code has the
	/// shared-skeleton modules ported. When the cost is below
	/// the budget, compared unsigned, every status word set to the level's target (<see cref="Es1HercRecord.SetAllStatus"/>),
	/// the cost taken from <c>Career_Salvage</c>, and the budget less the cost stored as the pay share of the entry
	/// <see cref="Es1CareerRecord.FindPilotBySlot"/> finds for the slot in <c>CareerRecord_Current</c>'s record. Retail
	/// stores through entry −1 when none is found, over the record's <c>+0x10</c>; nothing is stored here then, an engine
	/// guard.
	/// </summary>
	public void RepairAuto(uint budget, short slot, Es1Hangar hangar) {
		if (hangar.HercIn(slot) is not { } herc) {
			return;
		}

		var record = _mission.CurrentRecord;
		short entry = record?.FindPilotBySlot(slot) ?? -1;
		short level = 0;
		uint cost = (uint)Costs.HercCost(Es1RepairCosts.RepairTargets[level], herc);
		while (budget < cost) {
			if (Es1RepairCosts.LevelForCondition(herc.OverallCondition()) <= level) {
				break;
			}

			level++;
			cost = (uint)Costs.HercCost(Es1RepairCosts.RepairTargets[level], herc);
		}

		if (cost < budget) {
			herc.SetAllStatus(Es1RepairCosts.RepairTargets[level]);
			Roster.Salvage -= (int)cost;
			if (record != null && entry is >= 0 and < Es1CareerRecord.PilotCount) {
				record.Pilots[entry].PayShare = (int)(budget - cost);
			}
		}
	}
}
