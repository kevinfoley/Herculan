using HercWorks.Core.Data.File.Sav;
using HercWorks.Core.Data.Struct;
using HercWorks.Core.Data.Struct.Herc;
using HercWorks.Core.Data.Struct.Vshell.Sav;

namespace Herculan.Engine.Shell;

/// <summary>
/// The hangar read out of a save game and written back into it for <c>Game_SaveSlot</c>
/// (<c>0040e37b</c>). See docs/retail/formats/save-games.md.
/// </summary>
public sealed partial class ShellHangar {
	/// <summary>
	/// The bays out of an already-parsed save. Returns an empty hangar for a null save, which is what
	/// the screen draws its furniture against when there is nothing to load.
	/// </summary>
	public static ShellHangar From(PlayerSave? save) {
		var hangar = new ShellHangar();
		if (save == null) {
			return hangar;
		}

		hangar.SalvageKilograms = save.SalvageTotal;
		foreach (var item in save.Inventory?.Items ?? Array.Empty<Inventory.InventoryItem>()) {
			if (item?.Id is { } id) {
				// The save's stock reader, Armory_ReadWeaponStock (00411dbb), pushes each unit onto the head as it reads it, so the
				// file's last unit is the head.
				var stock = hangar.Stock(id.Id);
				stock.UnlockFlag = (byte)item.UnlockFlag;
				foreach (var entry in item.Units ?? Array.Empty<ShellWeaponEntry>()) {
					if (entry != null) {
						stock.Units.Add(ShellWeaponUnit.From(entry));
					}
				}
			}
		}

		hangar.QueueFreeSlots = save.BuildQueueFreeSlots;
		for (int slot = 0; slot < QueueSlots && slot < save.BuildQueue.Length; slot++) {
			hangar._queue[slot] = save.BuildQueue[slot]?.Id ?? 0;
		}
		foreach (var (slot, entry) in save.HercBay) {
			if (slot >= 0 && slot < BayCount && entry != null) {
				hangar._bays[slot] = ShellBayMachine.From(entry);
			}
		}

		foreach (var (herc, flag) in save.ChassisAvailability) {
			if (flag != 0) {
				hangar._availableChassis.Add(herc.Id);
			}
		}

		// Player_Read (004101b8) reads the player block's two leading shorts into 00482a78 and 00482a7a, then
		// points the player structure's three squad pointers at record DAT_00483b48[k] of squad k. The
		// save model keeps all five among the eight shorts between the squad block and the player's
		// record: DAT_00483b48's three first, the player block's two last.
		hangar.SquadPositions = save.SquadPositionsInPlay;
		hangar.MachinesOnStrength = save.MachinesOnStrength;
		hangar.Player = Pilot(save.PlayerPilot);
		for (int squad = 0; squad < SquadCount; squad++) {
			int member = save.SquadTailAndPlayerHead[squad];
			int record = squad * PilotsPerSquad + member;
			if (member >= 0 && member < PilotsPerSquad
				&& Pilot(save.Squadmates?.ElementAtOrDefault(record)) is { } pilot) {
				hangar._squad.Add(pilot);
				hangar._squadRecords.Add(record);
			}
		}

		return hangar;
	}

	/// <summary>
	/// Writes what the screens change back into <paramref name="save"/>, the game it was read from, so
	/// that <c>Game_SaveSlot</c> (<c>0040e37b</c>) can write the whole of it: the armory stock and build
	/// queue, the bays, the salvage pool, the chassis flags <see cref="GrantChassis"/> sets, the player's
	/// bay, the three squad members' bay, position and on-strength byte, and the player block's two
	/// counts. Everything else the save carries — the career block, the other 33 squad records and the
	/// flag array — nothing here changes, and is left as it was read.
	///
	/// <para>A weapon's stock is written from the head of its list, as <c>Armory_Write</c>
	/// (<c>004121cf</c>) walks it, and read back by pushing each unit onto the head, so each save and load
	/// reverses the order (docs/retail/formats/save-games.md#armory-stock-record).</para>
	/// </summary>
	public void Store(PlayerSave save) {
		var items = new Inventory.InventoryItem[ShellMissionLaunch.WeaponCatalogCount];
		for (int id = 0; id < items.Length; id++) {
			var units = _stock.TryGetValue(id, out var stock) ? stock.Units : new List<ShellWeaponUnit>();
			items[id] = new Inventory.InventoryItem {
				Id = WeaponLUT.GetById(id),
				UnlockFlag = stock?.UnlockFlag ?? (short)0,
				Quantity = (short)units.Count,
				Units = Enumerable.Reverse(units).Select(unit => unit.ToEntry()).ToArray(),
			};
		}

		save.Inventory = new Inventory { Items = items };
		save.BuildQueueFreeSlots = (short)QueueFreeSlots;
		for (int slot = 0; slot < QueueSlots && slot < save.BuildQueue.Length; slot++) {
			save.BuildQueue[slot] = WeaponLUT.GetById(_queue[slot]) ?? WeaponLUT.None;
		}

		save.HercBay = new Dictionary<short, HercBayEntry>();
		for (int bay = 0; bay < BayCount; bay++) {
			if (_bays[bay] is { } machine) {
				save.HercBay[(short)bay] = machine.ToEntry();
			}
		}

		save.SalvageTotal = SalvageKilograms;
		for (int type = 0; type < ChassisTypeCount; type++) {
			if (HercLUT.GetById((short)type) is { } herc) {
				short flag = save.ChassisAvailability.TryGetValue(herc, out var read) ? read : (short)0;
				save.ChassisAvailability[herc] = !IsChassisAvailable(type) ? (short)0 : flag != 0 ? flag : (short)1;
			}
		}

		save.SquadPositionsInPlay = (short)SquadPositions;
		save.MachinesOnStrength = (short)MachinesOnStrength;
		if (Player != null && save.PlayerPilot != null) {
			save.PlayerPilot.Bay = (short)Player.Bay;
		}

		for (int member = 0; member < _squadRecords.Count; member++) {
			if (save.Squadmates?.ElementAtOrDefault(_squadRecords[member]) is { } record) {
				record.Bay = (short)_squad[member].Bay;
				record.SquadPosition = (short)_squad[member].SquadPosition;
				record.OnStrength = (byte)(_squad[member].OnStrength ? 1 : 0);
			}
		}
	}

	private static ShellBayPilot? Pilot(PilotEntry? pilot) =>
		pilot == null ? null
			: new ShellBayPilot((pilot.Name ?? string.Empty).TrimEnd('\0'), pilot.RosterId, pilot.Bay,
				pilot.Skill?.Id ?? 0, pilot.SquadPosition, pilot.OnStrength != 0, pilot.NameIndex);
}
