
namespace Herculan.Engine.Shell;

/// <summary>
/// The armory: each weapon's unlock flag and stock of units (<c>Armory_AddUnit</c>, <c>00411efd</c>;
/// <c>Armory_PopUnit</c>, <c>00411ec7</c>), fitting a unit to a hardpoint, and the five-slot build queue
/// (docs/retail/shell/armory.md).
/// </summary>
public sealed partial class ShellHangar {
	/// <summary>
	/// The armory's stock of one weapon: its unlock flag, and the units it holds — the list at
	/// <c>weapons.dat</c> record <c>+0x19</c>, kept here with its head last. <c>Armory_AddUnit</c>
	/// (<c>00411efd</c>) pushes onto the head and <c>Armory_PopUnit</c> (<c>00411ec7</c>) takes it off, so the
	/// unit fitted next is the one that went in last.
	/// </summary>
	private sealed class WeaponStock {
		/// <summary>The unlock byte as the save holds it; any value but 0 is unlocked.</summary>
		public byte UnlockFlag;
		public readonly List<ShellWeaponUnit> Units = new();

		public bool Unlocked => UnlockFlag != 0;
	}

	private readonly Dictionary<int, WeaponStock> _stock = new();

	private WeaponStock Stock(int weaponId) {
		if (!_stock.TryGetValue(weaponId, out var stock)) {
			stock = new WeaponStock();
			_stock[weaponId] = stock;
		}

		return stock;
	}

	/// <summary>
	/// A weapon's unlock flag, <c>weapons.dat</c> record <c>+0x16</c> at <c>(&amp;DAT_00483bfa)[id * 0x1d]</c> — save
	/// block 1's leading byte for that id (docs/retail/formats/weapons-dat.md).
	/// </summary>
	public bool IsWeaponUnlocked(int weaponId) => _stock.TryGetValue(weaponId, out var entry) && entry.Unlocked;

	/// <summary>How many units of a weapon the armory holds, record <c>+0x17</c> at <c>DAT_00483bfb</c>.</summary>
	public int WeaponsOwned(int weaponId) => _stock.TryGetValue(weaponId, out var entry) ? entry.Units.Count : 0;

	/// <summary><c>Armory_AddUnit</c> (<c>00411efd</c>) — the unit onto the head of its weapon's list.</summary>
	private void AddUnit(ShellWeaponUnit unit) => Stock(unit.WeaponId).Units.Add(unit);

	/// <summary><c>Armory_PopUnit</c> (<c>00411ec7</c>) — the head of a weapon's list, or null when it is empty.</summary>
	private ShellWeaponUnit? PopUnit(int weaponId) {
		var units = Stock(weaponId).Units;
		if (units.Count == 0) {
			return null;
		}

		var unit = units[^1];
		units.RemoveAt(units.Count - 1);
		return unit;
	}

	/// <summary>
	/// <c>Herc_FitMount</c> (<c>004114ec</c>), which the weapons screen's row click runs through
	/// <c>Arming_FitSelected</c>: the unit in <paramref name="slot"/> goes back onto its weapon's stock
	/// list, and then <c>None</c> (0) leaves the slot empty at condition 100, and any other weapon takes
	/// the head of that weapon's list — its <see cref="ShellWeaponUnit.FitCondition"/> becomes the
	/// hardpoint's condition and its guidance is reset, to ARH for the three missile racks and to none for
	/// everything else, the Razor's launcher included. With that list empty the slot is left empty and
	/// its condition untouched. See docs/retail/shell/screen-layout.md#fitting-a-weapon.
	/// </summary>
	public void FitMount(ShellBayMachine machine, int slot, int weaponId) {
		if (machine.Mount(slot) is { } old) {
			AddUnit(old);
		}

		if (weaponId == 0) {
			machine.SetMount(slot, null);
			machine.SetCondition(ShellRepairCategory.Hardpoint, slot, 100);
			return;
		}

		var unit = PopUnit(weaponId);
		machine.SetMount(slot, unit);
		if (unit != null) {
			machine.SetCondition(ShellRepairCategory.Hardpoint, slot, unit.FitCondition);
			unit.Guidance = weaponId is >= FirstGuidedRack and <= LastGuidedRack ? ArhGuidance : ShellWeaponUnit.NoGuidance;
		}
	}

	/// <summary>The weapons <c>Herc_FitMount</c> fits with a guidance kind — the three missile racks, <c>0xd</c> to <c>0xf</c>.</summary>
	private const int FirstGuidedRack = 0xd;
	private const int LastGuidedRack = 0xf;
	private const int ArhGuidance = 1;

	/// <summary>The armory build queue's five slots, <c>0046f8d6</c>: one weapon id each, 0 for an empty slot.</summary>
	private readonly int[] _queue = new int[QueueSlots];

	/// <summary>How many slots the armory build queue has (docs/retail/shell/armory.md).</summary>
	public const int QueueSlots = 5;

	/// <summary>The queue's free-slot count, <c>0046f8d4</c> — the armory's <c>Workspace Available:</c>.</summary>
	public int QueueFreeSlots { get; private set; } = QueueSlots;

	/// <summary>The weapon id in each queue slot, 0 for an empty one.</summary>
	public IReadOnlyList<int> QueuedWeapons => _queue;

	/// <summary><c>Armory_QueuedCount</c> (<c>00412642</c>) — how many of the five queue slots hold <paramref name="weaponId"/>.</summary>
	public int QueuedCount(int weaponId) => _queue.Count(id => id == weaponId);

	/// <summary>
	/// <c>Armory_Enqueue</c> (<c>004125e7</c>) — the weapon into the first empty slot, one free slot fewer.
	/// Nothing while <see cref="QueueFreeSlots"/> is 0, which <c>Armory_FirstFreeSlot</c> (<c>004125bb</c>)
	/// tests before it looks at the slots.
	/// </summary>
	public void Enqueue(int weaponId) {
		if (QueueFreeSlots == 0) {
			return;
		}

		int slot = Array.IndexOf(_queue, 0);
		if (slot != -1) {
			_queue[slot] = weaponId;
			QueueFreeSlots--;
		}
	}

	/// <summary><c>Armory_Dequeue</c> (<c>0041260d</c>) — every slot holding the weapon emptied, one free slot more for each.</summary>
	public void Dequeue(int weaponId) {
		for (int slot = 0; slot < QueueSlots; slot++) {
			if (_queue[slot] == weaponId) {
				_queue[slot] = 0;
				QueueFreeSlots++;
			}
		}
	}

	/// <summary><c>Armory_ResetQueue</c> (<c>0041213d</c>) — five free slots, all empty.</summary>
	public void ResetQueue() {
		Array.Clear(_queue);
		QueueFreeSlots = QueueSlots;
	}

	/// <summary>
	/// <c>Armory_TrimQueueToBudget</c> (<c>004123ba</c>) — while what the queue has committed is more than
	/// the pool, compared unsigned, slots are emptied from the first, one free slot more for each.
	/// </summary>
	public void TrimQueueToBudget(Func<int, int> priceKilograms) {
		uint total = (uint)_queue.Where(id => id != 0).Sum(priceKilograms);
		for (int slot = 0; slot < QueueSlots && (uint)SalvageKilograms < total; slot++) {
			if (_queue[slot] != 0) {
				total -= (uint)priceKilograms(_queue[slot]);
				_queue[slot] = 0;
				QueueFreeSlots++;
			}
		}
	}

	/// <summary>
	/// The weapon scrap dialog's ACCEPT, <c>Armory_ScrapWeapons</c> (<c>0040e7b2</c>) through
	/// <c>Armory_ScrapStock</c> (<c>00412555</c>): every unit of the weapon the armory holds is freed and
	/// <paramref name="valueTons"/> times 1000 goes into the pool.
	/// </summary>
	public void ScrapStock(int weaponId, int valueTons) {
		Stock(weaponId).Units.Clear();
		SalvageKilograms += valueTons * ShellRepairCosts.KilogramsPerTon;
	}

	/// <summary>
	/// A new unit into stock — <c>WeaponUnit_Init(unit, id, fitCondition, 100, guidance)</c> then
	/// <c>Armory_AddUnit</c>. <c>Armory_AddNewUnit</c> (<c>0041229d</c>) makes the pair with guidance 5 for a
	/// <c>results.dat</c> salvage pair or a campaign grant, and <c>Armory_DeliverQueue</c> makes it inline.
	/// </summary>
	internal void AddNewUnit(int weaponId, int fitCondition, int guidance) =>
		AddUnit(new ShellWeaponUnit(weaponId, fitCondition: fitCondition, condition: 100, guidance: guidance));

	/// <summary>The missile racks <c>Armory_DeliverQueue</c> delivers with guidance 1: ids <c>0xd</c> to <c>0x10</c>, Razor's included.</summary>
	private const int FirstDeliveredRack = 0xd;
	private const int LastDeliveredRack = 0x10;

	/// <summary>
	/// <c>Armory_DeliverQueue</c> (<c>00412428</c>) — one new unit at condition 100 per occupied queue slot,
	/// guidance 1 for the four missile racks and none for anything else, and the queue's total price,
	/// which the caller takes off the pool. The queue itself is left as it was, so a hand-built one
	/// delivers again at the next debrief (docs/retail/shell/campaign-loop.md#the-debrief--game_processmissionresults-0040eae7).
	/// </summary>
	public int DeliverQueue(Func<int, int> priceKilograms) {
		int total = 0;
		foreach (int weaponId in _queue) {
			if (weaponId != 0) {
				total += priceKilograms(weaponId);
				AddNewUnit(weaponId, 100, weaponId is >= FirstDeliveredRack and <= LastDeliveredRack ? ArhGuidance : ShellWeaponUnit.NoGuidance);
			}
		}

		return total;
	}
}
