using HercWorks.Core.Data.File.Dat.Shell;

namespace HercWorks.Core.Io.Transform.Shell;

/// <summary>
/// Reads and writes ES1's <c>car\CAR_INIT.DAT</c> and <c>car\SM_INIT.DAT</c> (<see cref="Es1HercList"/>), as <c>Hangar_Read</c>
/// (<c>ES1GO 00028c50</c>) and <c>Herc_ReadFromStream</c> (<c>0002352f</c>) read them, and <c>car\sm_play.mec</c>, which
/// <c>Hangar_Write</c> (<c>00029107</c>) writes in the same layout from the list <see cref="Es1Hangar.SingleMissionSave"/>
/// makes.
/// </summary>
public class Es1HercListTransformer : Es1WalkedFileTransformer<Es1HercList> {
	protected override Es1HercList ReadFile() {
		var list = new Es1HercList {
			SlotCount = ReadInt16(),
			Word1 = ReadInt16(),
			MarkedSlot = ReadInt16(),
			MarkedSlotWord = ReadInt16(),
			Dword = ReadInt32(),
		};

		short count = ReadInt16();
		if (count < 0) {
			throw new InvalidDataException($"The HERC count is {count}.");
		}

		list.Hercs = new Es1HercListEntry[count];
		for (int i = 0; i < count; i++) {
			list.Hercs[i] = new Es1HercListEntry { Slot = ReadInt16(), Herc = ReadHerc() };
		}

		return list;
	}

	protected override void WriteFile(BinaryWriter w, Es1HercList source) {
		w.Write(source.SlotCount);
		w.Write(source.Word1);
		w.Write(source.MarkedSlot);
		w.Write(source.MarkedSlotWord);
		w.Write(source.Dword);
		w.Write(checked((short)source.Hercs.Length));
		foreach (var entry in source.Hercs) {
			w.Write(entry.Slot);
			WriteHerc(w, entry.Herc);
		}
	}

}

/// <summary>
/// Reads and writes an ES1 career record (<see cref="Es1CareerRecord"/>), the <c>0x1f7</c> bytes
/// <c>SingleMission_SaveRecord</c> (<c>ES1GO 00014f54</c>) writes to <c>car\sm_play.gam</c> from memory whole and
/// <c>SingleMission_RestoreRecord</c> (<c>00014fd6</c>) reads back (docs/es1/formats/bst-battle-sets.md#the-career-record).
/// </summary>
public class Es1CareerRecordTransformer : Es1WalkedFileTransformer<Es1CareerRecord> {
	protected override Es1CareerRecord ReadFile() {
		var record = new Es1CareerRecord {
			InUse = ReadByte(),
			Byte1 = ReadByte(),
			Word2 = ReadInt16(),
			Word4 = ReadInt16(),
			Campaign = ReadInt16(),
			Position = ReadInt16(),
			MissionsPlayed = ReadInt16(),
			LastMissionKind = ReadInt16(),
			PlayerEntry = ReadInt16(),
			PilotCountWord = ReadInt16(),
			CurrentPilot = ReadInt16(),
		};

		for (int i = 0; i < Es1CareerRecord.PilotCount; i++) {
			record.Pilots[i] = new Es1PilotEntry {
				NameGroup = ReadInt16(),
				Name = ReadInt16(),
				Word4 = ReadInt16(),
				Word6 = ReadInt16(),
				Slot = ReadInt16(),
				OnStrength = ReadByte(),
				WordB = ReadInt16(),
				Rank = ReadInt16(),
				Condition = ReadInt16(),
				Gains = ReadInt16s(Es1PilotEntry.GainCount),
				GainSums = ReadInt16s(Es1PilotEntry.GainCount),
				Word1D = ReadInt16(),
				Word1F = ReadInt16(),
				PayShare = ReadInt32(),
			};
		}

		record.Equipment.LastKind = ReadInt16();
		record.Equipment.LastIndex = ReadInt16();
		foreach (var entry in record.Equipment.Chassis.Concat(record.Equipment.Weapons)) {
			entry.GoodGradeMissions = ReadInt16();
			entry.AnyGradeMissions = ReadInt16();
			entry.Available = ReadByte();
		}

		record.Dword129 = ReadInt32();
		record.Flags = ReadInt16s(Es1CareerRecord.FlagCount);
		record.Word1F5 = ReadInt16();
		return record;
	}

	protected override void WriteFile(BinaryWriter w, Es1CareerRecord source) {
		if (source.Pilots.Length != Es1CareerRecord.PilotCount || source.Flags.Length != Es1CareerRecord.FlagCount
				|| source.Equipment.Chassis.Length != Es1EquipmentState.ChassisCount
				|| source.Equipment.Weapons.Length != Es1EquipmentState.WeaponCount
				|| source.Pilots.Any(p => p.Gains.Length != Es1PilotEntry.GainCount || p.GainSums.Length != Es1PilotEntry.GainCount)) {
			throw new InvalidDataException("A career record needs four pilot entries of three gains, 8 chassis and 17 weapon entries and 100 flags.");
		}

		w.Write(source.InUse);
		w.Write(source.Byte1);
		w.Write(source.Word2);
		w.Write(source.Word4);
		w.Write(source.Campaign);
		w.Write(source.Position);
		w.Write(source.MissionsPlayed);
		w.Write(source.LastMissionKind);
		w.Write(source.PlayerEntry);
		w.Write(source.PilotCountWord);
		w.Write(source.CurrentPilot);
		foreach (var pilot in source.Pilots) {
			w.Write(pilot.NameGroup);
			w.Write(pilot.Name);
			w.Write(pilot.Word4);
			w.Write(pilot.Word6);
			w.Write(pilot.Slot);
			w.Write(pilot.OnStrength);
			w.Write(pilot.WordB);
			w.Write(pilot.Rank);
			w.Write(pilot.Condition);
			WriteInt16s(w, pilot.Gains);
			WriteInt16s(w, pilot.GainSums);
			w.Write(pilot.Word1D);
			w.Write(pilot.Word1F);
			w.Write(pilot.PayShare);
		}

		w.Write(source.Equipment.LastKind);
		w.Write(source.Equipment.LastIndex);
		foreach (var entry in source.Equipment.Chassis.Concat(source.Equipment.Weapons)) {
			w.Write(entry.GoodGradeMissions);
			w.Write(entry.AnyGradeMissions);
			w.Write(entry.Available);
		}

		w.Write(source.Dword129);
		WriteInt16s(w, source.Flags);
		w.Write(source.Word1F5);
	}
}

/// <summary>
/// Reads and writes ES1's <c>car\C_ARMORY.DAT</c> and <c>car\S_ARMORY.DAT</c> (<see cref="Es1Inventory"/>), as
/// <c>Inventory_Read</c> (<c>ES1GO 00027e3c</c>) reads them.
/// </summary>
public class Es1InventoryTransformer : Es1WalkedFileTransformer<Es1Inventory> {
	protected override Es1Inventory ReadFile() {
		short count = ReadInt16();
		if (count < 0) {
			throw new InvalidDataException($"The record count is {count}.");
		}

		var records = new Es1InventoryRecord[count];
		for (int i = 0; i < count; i++) {
			short slot = ReadInt16();
			short values = ReadInt16();
			if (values < 0) {
				throw new InvalidDataException($"The value count at {Index - 2} is {values}.");
			}

			records[i] = new Es1InventoryRecord { Slot = slot, Values = ReadInt16s(values) };
		}

		return new Es1Inventory { Records = records };
	}

	protected override void WriteFile(BinaryWriter w, Es1Inventory source) {
		w.Write(checked((short)source.Records.Length));
		foreach (var record in source.Records) {
			w.Write(record.Slot);
			w.Write(checked((short)record.Values.Length));
			WriteInt16s(w, record.Values);
		}
	}
}

/// <summary>
/// Reads and writes ES1's <c>car\NAMES.LOG</c> (<see cref="Es1CareerLog"/>), as <c>CareerLog_Load</c> (<c>ES1GO 00017e7f</c>)
/// reads it and <c>CareerLog_Write</c> (<c>00017f5f</c>) writes it: a count of 0 is followed by nothing.
/// </summary>
public class Es1CareerLogTransformer : Es1WalkedFileTransformer<Es1CareerLog> {
	protected override Es1CareerLog ReadFile() {
		short count = ReadInt16();
		if (count < 0) {
			throw new InvalidDataException($"The entry count is {count}.");
		}

		var log = new Es1CareerLog();
		if (count == 0) {
			return log;
		}

		log.Current = ReadInt16();
		log.Entries = new Es1CareerLogEntry[count];
		for (int i = 0; i < count; i++) {
			log.Entries[i] = new Es1CareerLogEntry {
				Name = ReadBytes(Es1CareerLogEntry.NameLength),
				Word = ReadInt16(),
				CareerFile = ReadBytes(Es1CareerLogEntry.PathLength),
				HangarFile = ReadBytes(Es1CareerLogEntry.PathLength),
			};
		}

		return log;
	}

	protected override void WriteFile(BinaryWriter w, Es1CareerLog source) {
		w.Write(checked((short)source.Entries.Length));
		if (source.Entries.Length == 0) {
			return;
		}

		w.Write(source.Current);
		foreach (var entry in source.Entries) {
			if (entry.Name.Length != Es1CareerLogEntry.NameLength || entry.CareerFile.Length != Es1CareerLogEntry.PathLength
				|| entry.HangarFile.Length != Es1CareerLogEntry.PathLength) {
				throw new InvalidDataException("An entry needs a 64-byte name and two 17-byte paths.");
			}

			w.Write(entry.Name);
			w.Write(entry.Word);
			w.Write(entry.CareerFile);
			w.Write(entry.HangarFile);
		}
	}
}

/// <summary>
/// Reads and writes ES1's <c>car\NAMES.DAT</c> (<see cref="Es1PilotNames"/>), as <c>PilotNames_Load</c> (<c>ES1GO 00015070</c>)
/// reads it: unlike a name list's, a group's count of 0 is followed by nothing.
/// </summary>
public class Es1PilotNamesTransformer : Es1WalkedFileTransformer<Es1PilotNames> {
	protected override Es1PilotNames ReadFile() {
		var groups = new string[ReadByte()][];
		for (int g = 0; g < groups.Length; g++) {
			groups[g] = new string[ReadByte()];
			for (int n = 0; n < groups[g].Length; n++) {
				groups[g][n] = ReadCString(Es1PilotNames.NameBufferLength);
			}
		}

		return new Es1PilotNames { Groups = groups };
	}

	protected override void WriteFile(BinaryWriter w, Es1PilotNames source) {
		w.Write(checked((byte)source.Groups.Length));
		foreach (string[] group in source.Groups) {
			w.Write(checked((byte)group.Length));
			foreach (string name in group) {
				WriteCString(w, name);
			}
		}
	}
}
