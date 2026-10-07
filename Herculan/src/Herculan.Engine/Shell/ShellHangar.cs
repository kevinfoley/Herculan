
namespace Herculan.Engine.Shell;

/// <summary>
/// The eight hangar bays and the salvage pool — what every tab from WEAPONS to CREW works over, and
/// what the repair screen in particular reads a machine out of.
///
/// <para>VSHELL keeps the bays as eight pointers at <c>Hangar_BayRecords</c> (<c>00482ac3</c>), null for an empty bay, with the
/// selected slot in <c>SelectedBaySlot</c> (<c>00482ae5</c>) (<c>-1</c> for none). Sparse is normal: a save really can have
/// a machine in bay 3 and nothing in bay 2, so the bays are addressed by index rather than packed.</para>
/// </summary>
public sealed partial class ShellHangar {
	/// <summary>How many bays there are. Every loop in the shell that walks them bounds at eight.</summary>
	public const int BayCount = 8;

	private readonly ShellBayMachine?[] _bays = new ShellBayMachine?[BayCount];
	private readonly HashSet<int> _availableChassis = new();

	private ShellHangar() { }

	/// <summary>
	/// The salvage pool in kilograms, <c>CareerSalvage</c> (<c>00482af4</c>): the save's, then moved by
	/// BUILD, SCRAP and the repair screen, whose CANCEL writes it back outright.
	/// </summary>
	public int SalvageKilograms { get; internal set; }

	/// <summary>The overall condition below which the debrief scraps a machine.</summary>
	public const int SettleScrapCondition = 30;

	/// <summary>
	/// <c>Herc_SettleAfterMission</c> (<c>00410c7c</c>) — the machine in <paramref name="bay"/> below
	/// <see cref="SettleScrapCondition"/> overall (<see cref="ShellBayMachine.OverallCondition"/>) is valued,
	/// its mounts stripped into stock and the bay emptied, and the value returned with <c>Scrapped</c> set; at
	/// or above, only its overall slot goes back to 100. Unlike <see cref="Scrap"/> the pilot keeps the bay.
	/// An empty bay settles nothing: the original writes the overall slot through the empty bay's null record.
	/// </summary>
	public (int Salvage, bool Scrapped) SettleAfterMission(int bay, ShellRepairCosts? costs) {
		if (Bay(bay) is not { } machine) {
			return (0, false);
		}

		if (machine.OverallCondition < SettleScrapCondition) {
			int value = costs?.ScrapValue(machine) ?? 0;
			StripMounts(machine);
			_bays[bay] = null;
			return (value, true);
		}

		machine.SetOverallSlot(100);
		return (0, false);
	}

	/// <summary>
	/// <c>Repair_Auto</c> (<c>00411328</c>) — the repair level, from 0 down, that <paramref name="budget"/>
	/// covers: while the cost of repairing to the level's target is above the budget, step down a level,
	/// unless the machine's own overall condition is already at or above that level. A cost below the
	/// budget is applied (<see cref="ShellBayMachine.ApplyRepair"/>) and returned; otherwise nothing is
	/// repaired and 0 returned. Both comparisons are unsigned, as the original's are.
	/// </summary>
	public static uint AutoRepair(ShellBayMachine machine, uint budget, ShellRepairCosts costs) {
		int level = 0;
		uint cost = (uint)costs.HercCost(machine, ShellRepairCosts.RepairTarget[level]);
		while (budget < cost) {
			if (ShellRepairCosts.LevelForCondition(machine.OverallCondition) <= level) {
				break;
			}

			level++;
			cost = (uint)costs.HercCost(machine, ShellRepairCosts.RepairTarget[level]);
		}

		if (cost < budget) {
			machine.ApplyRepair(ShellRepairCosts.RepairTarget[level]);
			return cost;
		}

		return 0;
	}

	/// <summary>
	/// <c>HercList_RemoveFirstOfType</c> (<c>00410bbe</c>) — the first bay holding a <paramref name="chassisType"/> machine has its mounts
	/// stripped into stock and is emptied, for no salvage, and its index is returned. With none, it returns
	/// what its last probe read, bay 7's chassis type, or <c>-1</c> for an empty bay 7 — which its caller,
	/// <c>Hangar_WithdrawChassis</c> (<c>0040e7cd</c>), then uses as a bay.
	/// </summary>
	public int RemoveFirstOfType(int chassisType) {
		int probe = -1;
		for (int bay = 0; bay < BayCount; bay++) {
			probe = _bays[bay]?.ChassisType ?? -1;
			if (probe == chassisType) {
				StripMounts(_bays[bay]!);
				_bays[bay] = null;
				return bay;
			}
		}

		return probe;
	}

	/// <summary>
	/// The condition at which a mount survives its machine being scrapped: <c>Herc_StripMounts</c>
	/// (<c>00411795</c>) returns one at or above it to stock, and <c>Herc_ScrapValue</c> (<c>00413b50</c>)
	/// pays salvage for one below it.
	/// </summary>
	public const int ReturnToStockCondition = 80;

	/// <summary>
	/// BUILD's order, <c>Hangar_BuySelected</c> (<c>0040e91c</c>): <c>HercList_OrderIntoSelected</c> (<c>00410982</c>) puts a new record in <paramref name="bay"/> — the
	/// selected one, whatever it holds — and <see cref="ShellBayMachine.Ordered"/> fills it with
	/// <paramref name="buildMissions"/> to go, and the price, <paramref name="priceTons"/> times 1000, comes
	/// off the pool. Returns the price.
	/// </summary>
	public int Order(int bay, int chassisType, int priceTons, int buildMissions) {
		if (bay < 0 || bay >= BayCount) {
			return 0;
		}

		_bays[bay] = ShellBayMachine.Ordered(chassisType, buildMissions);
		int price = priceTons * ShellRepairCosts.KilogramsPerTon;
		SalvageKilograms -= price;
		return price;
	}

	/// <summary>
	/// The scrap dialog's ACCEPT, <c>Hangar_ScrapSelected</c> (<c>0040e757</c>, docs/retail/shell/armory.md#scrapping): the machine in
	/// <paramref name="bay"/> is valued, its mounts stripped into stock and the bay emptied
	/// (<c>HercList_ScrapSelected</c>, <c>00410922</c>), the value goes into the pool, and the pilot the bay had loses it — and whoever
	/// holds that pilot's squad position is taken off strength. Returns the salvage credited.
	/// </summary>
	public int Scrap(int bay, ShellRepairCosts? costs) {
		if (bay < 0 || bay >= BayCount) {
			return 0;
		}

		int value = 0;
		if (_bays[bay] is { } machine) {
			value = costs?.ScrapValue(machine) ?? 0;
			StripMounts(machine);
			_bays[bay] = null;
		}

		SalvageKilograms += value;
		if (PilotFor(bay) is { } pilot) {
			pilot.Bay = -1;
			int member = SquadMemberIndexAt(pilot.SquadPosition);
			if (member != -1) {
				SetOnStrength(member, false);
			}
		}

		return value;
	}

	/// <summary>
	/// <c>Herc_StripMounts</c> (<c>00411795</c>) — each fitted mount below the capacity at
	/// <see cref="ReturnToStockCondition"/> or better goes back into stock through <c>Armory_AddUnit</c>
	/// (<c>00411efd</c>); anything worse is destroyed.
	/// </summary>
	private void StripMounts(ShellBayMachine machine) {
		for (int slot = 0; slot < machine.MountCapacity; slot++) {
			if (machine.Mount(slot) is { } unit
				&& machine.Condition(ShellRepairCategory.Hardpoint, slot) >= ReturnToStockCondition) {
				AddUnit(unit);
			}
		}
	}

	/// <summary>The machine in one bay, or null when the bay is empty or the index is out of range.</summary>
	public ShellBayMachine? Bay(int slot) => slot >= 0 && slot < BayCount ? _bays[slot] : null;

	/// <summary>
	/// A chassis type's availability flag — save block 7, <c>herc_inf.dat</c> record <c>+0x0e</c> at
	/// <c>(&amp;DAT_00483b62)[type * 8]</c>, the flag <c>Herc_GrantUnlocks</c> (<c>004118c5</c>) sets.
	/// </summary>
	public bool IsChassisAvailable(int chassisType) => _availableChassis.Contains(chassisType);

	/// <summary>
	/// <c>Herc_FirstBuiltBay</c> (<c>00410c2a</c>) — the first bay holding a fully built machine, or <c>-1</c> when there is
	/// none. It is what the repair screen falls back to when the selected bay holds nothing it can
	/// work on.
	/// </summary>
	public int FirstBuiltBay() {
		for (int slot = 0; slot < BayCount; slot++) {
			if (_bays[slot] is { IsBuilt: true }) {
				return slot;
			}
		}

		return -1;
	}

	/// <summary>
	/// <c>Herc_HasSingleDeployable</c> (<c>00410add</c>) — whether exactly one bay holds a machine that is built and deployable.
	/// The repair screen's SCRAP button is dead while this holds, which is what stops a player
	/// scrapping the last thing they can fly.
	/// </summary>
	public bool HasSingleDeployable() {
		int count = 0;
		for (int slot = 0; slot < BayCount; slot++) {
			if (_bays[slot] is { IsBuilt: true, IsFlightworthy: true }) {
				count++;
			}
		}

		return count == 1;
	}

	/// <summary>
	/// <c>HercList_Deliver</c> (<c>00410b68</c>) — a machine into an empty bay. The original asserts the bay is empty; this
	/// replaces whatever is there.
	/// </summary>
	public void Deliver(int bay, ShellBayMachine machine) {
		if (bay >= 0 && bay < BayCount) {
			_bays[bay] = machine;
		}
	}
}
