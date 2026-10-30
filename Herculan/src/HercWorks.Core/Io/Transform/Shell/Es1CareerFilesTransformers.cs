using HercWorks.Core.Data.File.Dat.Shell;

namespace HercWorks.Core.Io.Transform.Shell;

/// <summary>
/// The parts ES1's career files share, each read and written as the shell's functions read and write it: a HERC list
/// (<c>Hangar_Read</c>, <c>Hangar_Write</c>), a career record (written from memory whole) and a weapon stock
/// (<c>Inventory_Read</c>, <c>Inventory_Write</c>).
/// </summary>
public abstract class Es1CareerDataTransformer<T> : Es1WalkedFileTransformer<T> where T : class {
	/// <summary>
	/// A HERC list at the cursor, as <c>Hangar_Read</c> (<c>ES1GO 00028c50</c>) and <c>Herc_ReadFromStream</c>
	/// (<c>0002352f</c>) read it (docs/es1/formats/car-hercs-and-stock.md#the-herc-lists).
	/// </summary>
	protected Es1HercList ReadHercList() {
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

	/// <summary>Writes <paramref name="source"/> in <see cref="ReadHercList"/>'s layout, its count the HERCs it holds.</summary>
	protected static void WriteHercList(BinaryWriter w, Es1HercList source) {
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

	/// <summary>A <c>0x1f7</c>-byte career record at the cursor (docs/es1/formats/bst-battle-sets.md#the-career-record).</summary>
	protected Es1CareerRecord ReadCareerRecord() {
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

	/// <summary>Writes <paramref name="source"/> in <see cref="ReadCareerRecord"/>'s layout.</summary>
	protected static void WriteCareerRecord(BinaryWriter w, Es1CareerRecord source) {
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

	/// <summary>
	/// A weapon stock at the cursor, as <c>Inventory_Read</c> (<c>ES1GO 00027e3c</c>) reads it
	/// (docs/es1/formats/car-hercs-and-stock.md#the-weapon-stock).
	/// </summary>
	protected Es1Inventory ReadInventory() {
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

	/// <summary>Writes <paramref name="source"/> in <see cref="ReadInventory"/>'s layout.</summary>
	protected static void WriteInventory(BinaryWriter w, Es1Inventory source) {
		w.Write(checked((short)source.Records.Length));
		foreach (var record in source.Records) {
			w.Write(record.Slot);
			w.Write(checked((short)record.Values.Length));
			WriteInt16s(w, record.Values);
		}
	}

	/// <summary>The bytes from the cursor to the file's end.</summary>
	protected byte[] ReadRest() => ReadBytes(Bytes!.Length - Index);
}

/// <summary>
/// Reads and writes ES1's <c>car\CAR_INIT.DAT</c> and <c>car\SM_INIT.DAT</c> (<see cref="Es1HercList"/>), as <c>Hangar_Read</c>
/// (<c>ES1GO 00028c50</c>) and <c>Herc_ReadFromStream</c> (<c>0002352f</c>) read them, and <c>car\sm_play.mec</c>, which
/// <c>Hangar_Write</c> (<c>00029107</c>) writes in the same layout from the list <see cref="Es1Hangar.SingleMissionSave"/>
/// makes.
/// </summary>
public class Es1HercListTransformer : Es1CareerDataTransformer<Es1HercList> {
	protected override Es1HercList ReadFile() => ReadHercList();

	protected override void WriteFile(BinaryWriter w, Es1HercList source) => WriteHercList(w, source);
}

/// <summary>
/// Reads and writes an ES1 career record (<see cref="Es1CareerRecord"/>), the <c>0x1f7</c> bytes
/// <c>SingleMission_SaveRecord</c> (<c>ES1GO 00014f54</c>) writes to <c>car\sm_play.gam</c> from memory whole and
/// <c>SingleMission_RestoreRecord</c> (<c>00014fd6</c>) reads back (docs/es1/formats/bst-battle-sets.md#the-career-record).
/// </summary>
public class Es1CareerRecordTransformer : Es1CareerDataTransformer<Es1CareerRecord> {
	protected override Es1CareerRecord ReadFile() => ReadCareerRecord();

	protected override void WriteFile(BinaryWriter w, Es1CareerRecord source) => WriteCareerRecord(w, source);
}

/// <summary>
/// Reads and writes ES1's <c>car\C_ARMORY.DAT</c> and <c>car\S_ARMORY.DAT</c> (<see cref="Es1Inventory"/>), as
/// <c>Inventory_Read</c> (<c>ES1GO 00027e3c</c>) reads them.
/// </summary>
public class Es1InventoryTransformer : Es1CareerDataTransformer<Es1Inventory> {
	protected override Es1Inventory ReadFile() => ReadInventory();

	protected override void WriteFile(BinaryWriter w, Es1Inventory source) => WriteInventory(w, source);
}

/// <summary>
/// Reads and writes ES1's career file <c>car\CAR_</c><i>n</i><c>.CAR</c> (<see cref="Es1CareerFile"/>), as
/// <c>Career_LoadCurrent</c> (<c>ES1GO 00016c1e</c>) reads it and <c>Career_New</c> (<c>000163ac</c>) and <c>Career_Save</c>
/// (<c>000168b0</c>) write it (docs/es1/shell/career.md#the-files). The bytes after the 41 records are kept as
/// <see cref="Es1CareerFile.Tail"/>.
/// </summary>
public class Es1CareerFileTransformer : Es1CareerDataTransformer<Es1CareerFile> {
	protected override Es1CareerFile ReadFile() {
		var file = new Es1CareerFile { Byte0 = ReadByte(), BattleSet = ReadInt16(), NameGroups = ReadInt16s(Es1CareerFile.NameGroupCount) };
		file.Records = new Es1CareerFileRecord[Es1CareerFile.RecordCount];
		for (int i = 0; i < file.Records.Length; i++) {
			file.Records[i] = new Es1CareerFileRecord { Record = ReadCareerRecord(), Stock = ReadInventory() };
		}

		file.Tail = ReadRest();
		return file;
	}

	protected override void WriteFile(BinaryWriter w, Es1CareerFile source) {
		if (source.NameGroups.Length != Es1CareerFile.NameGroupCount || source.Records.Length != Es1CareerFile.RecordCount) {
			throw new InvalidDataException("A career file needs 12 name groups and 41 records.");
		}

		w.Write(source.Byte0);
		w.Write(source.BattleSet);
		WriteInt16s(w, source.NameGroups);
		foreach (var record in source.Records) {
			WriteCareerRecord(w, record.Record);
			WriteInventory(w, record.Stock);
		}

		w.Write(source.Tail);
	}
}

/// <summary>
/// Reads and writes ES1's career hangar file <c>car\CAR_</c><i>n</i><c>.MEC</c> (<see cref="Es1CareerHangarFile"/>): 41 HERC
/// lists, as <c>Hangar_ReadFromSave</c> (<c>ES1GO 00028bd6</c>) passes over and reads them and <c>Career_New</c>
/// (<c>000163ac</c>) and <c>Career_Save</c> (<c>000168b0</c>) write them (docs/es1/shell/career.md#the-files). The bytes
/// after the 41 lists are kept as <see cref="Es1CareerHangarFile.Tail"/>.
/// </summary>
public class Es1CareerHangarFileTransformer : Es1CareerDataTransformer<Es1CareerHangarFile> {
	protected override Es1CareerHangarFile ReadFile() {
		var lists = new Es1HercList[Es1CareerFile.RecordCount];
		for (int i = 0; i < lists.Length; i++) {
			lists[i] = ReadHercList();
		}

		return new Es1CareerHangarFile { Lists = lists, Tail = ReadRest() };
	}

	protected override void WriteFile(BinaryWriter w, Es1CareerHangarFile source) {
		if (source.Lists.Length != Es1CareerFile.RecordCount) {
			throw new InvalidDataException("A career hangar file needs 41 lists.");
		}

		foreach (var list in source.Lists) {
			WriteHercList(w, list);
		}

		w.Write(source.Tail);
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
