using HercWorks.Core.Data.File.Dat.Shell;

namespace HercWorks.Core.Io.Transform.Shell;

/// <summary>
/// Reads and writes ES1's <c>car\CAR_INIT.DAT</c> and <c>car\SM_INIT.DAT</c> (<see cref="Es1HercList"/>), as <c>Hangar_Read</c>
/// (<c>ES1GO 00028c50</c>) and <c>Herc_ReadFromStream</c> (<c>0002352f</c>) read them.
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
