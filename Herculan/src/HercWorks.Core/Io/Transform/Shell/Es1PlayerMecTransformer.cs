using HercWorks.Core.Data.File.Dat.Shell;

namespace HercWorks.Core.Io.Transform.Shell;

/// <summary>
/// Reads and writes ES1's <c>data\player.mec</c> (<see cref="Es1PlayerMec"/>). The read is the simulator's
/// (<c>DBSim_LoadScriptDat</c>, <c>ES1DBSIM 0001d850</c>, docs/es1/formats/career-log-and-player-mec.md#the-simulators-read):
/// a HERC record after every entry's two words. The write is <c>PlayerMec_WriteEntries</c>' (<c>ES1GO 00022a05</c>), which
/// writes nothing for an entry with no HERC.
/// </summary>
public class Es1PlayerMecTransformer : Es1WalkedFileTransformer<Es1PlayerMec> {
	protected override Es1PlayerMec ReadFile() {
		var file = new Es1PlayerMec { PlayerEntry = ReadInt16() };
		short count = ReadInt16();
		if (count < 0) {
			throw new InvalidDataException($"The entry count is {count}.");
		}

		file.Entries = new Es1PlayerMecEntry[count];
		for (int i = 0; i < count; i++) {
			file.Entries[i] = new Es1PlayerMecEntry { Word1 = ReadInt16(), Word2 = ReadInt16(), Herc = ReadHerc() };
		}

		file.WeaponsAvailable = ReadBytes(Es1PlayerMec.WeaponCount);
		return file;
	}

	protected override void WriteFile(BinaryWriter w, Es1PlayerMec source) {
		if (source.WeaponsAvailable.Length != Es1PlayerMec.WeaponCount) {
			throw new InvalidDataException($"The file ends in {Es1PlayerMec.WeaponCount} available bytes.");
		}

		w.Write(source.PlayerEntry);
		w.Write(checked((short)source.Entries.Length));
		foreach (var entry in source.Entries) {
			w.Write(entry.Word1);
			w.Write(entry.Word2);
			if (entry.Herc != null) {
				WriteHerc(w, entry.Herc);
			}
		}

		w.Write(source.WeaponsAvailable);
	}
}
