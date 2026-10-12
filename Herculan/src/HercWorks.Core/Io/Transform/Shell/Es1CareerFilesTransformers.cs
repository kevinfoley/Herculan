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

	private Es1HercRecord ReadHerc() {
		var herc = new Es1HercRecord { Chassis = ReadInt16() };
		short mounts = ReadInt16();
		if (mounts < 0) {
			throw new InvalidDataException($"The mount count at {Index - 2} is {mounts}.");
		}

		herc.Weapons = ReadInt16s(mounts);
		herc.MountWords = ReadInt16s(mounts);
		herc.Word = ReadInt16();
		herc.Status = ReadInt16s(Es1HercRecord.StatusLength);
		return herc;
	}

	private static void WriteHerc(BinaryWriter w, Es1HercRecord herc) {
		if (herc.MountWords.Length != herc.Weapons.Length || herc.Status.Length != Es1HercRecord.StatusLength) {
			throw new InvalidDataException("A HERC needs one mount word per weapon and a 33-word status block.");
		}

		w.Write(herc.Chassis);
		w.Write(checked((short)herc.Weapons.Length));
		WriteInt16s(w, herc.Weapons);
		WriteInt16s(w, herc.MountWords);
		w.Write(herc.Word);
		WriteInt16s(w, herc.Status);
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
