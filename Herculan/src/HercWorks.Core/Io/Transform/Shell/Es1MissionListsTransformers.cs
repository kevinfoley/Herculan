using HercWorks.Core.Data.File.Dat.Shell;

namespace HercWorks.Core.Io.Transform.Shell;

/// <summary>
/// The name list reading ES1's battle sets, mission lists, single missions and the shell's file lists share
/// (<see cref="Es1NameList"/>; docs/es1/formats/bst-battle-sets.md#name-lists), as <c>StrList_Read</c> (<c>ES1GO 000201b7</c>)
/// reads it. A name with no NUL in its 13 bytes is rejected (<see cref="Es1WalkedFileTransformer{T}.ReadCString"/>).
/// </summary>
public abstract class Es1NameListFileTransformer<T> : Es1WalkedFileTransformer<T> where T : class {
	/// <summary>A count byte at the cursor.</summary>
	protected byte ReadCount() => ReadByte();

	/// <summary>A name list at the cursor.</summary>
	protected Es1NameList ReadNameList() {
		int count = ReadCount();
		if (count == 0) {
			return new Es1NameList { SkippedByte = Index < Bytes!.Length ? IndexByte() : null };
		}

		var names = new string[count];
		for (int i = 0; i < count; i++) {
			names[i] = ReadCString(Es1NameList.NameBufferLength);
		}

		return new Es1NameList { Names = names };
	}

	/// <summary>Writes <paramref name="list"/> as <see cref="ReadNameList"/> reads it.</summary>
	protected static void WriteNameList(BinaryWriter w, Es1NameList list) {
		w.Write(checked((byte)list.Names.Length));
		if (list.Names.Length == 0) {
			if (list.SkippedByte is { } skipped) {
				w.Write(skipped);
			}

			return;
		}

		foreach (string name in list.Names) {
			WriteCString(w, name);
		}
	}

	/// <summary>A count byte and that many name lists at the cursor.</summary>
	protected Es1NameList[] ReadNameLists() {
		var lists = new Es1NameList[ReadCount()];
		for (int i = 0; i < lists.Length; i++) {
			lists[i] = ReadNameList();
		}

		return lists;
	}

	/// <summary>Writes <paramref name="lists"/> as <see cref="ReadNameLists"/> reads them.</summary>
	protected static void WriteNameLists(BinaryWriter w, IReadOnlyList<Es1NameList> lists) {
		w.Write(checked((byte)lists.Count));
		foreach (var list in lists) {
			WriteNameList(w, list);
		}
	}
}

/// <summary>Reads and writes one of ES1's <c>bst\ANY_*.DAT</c>, a single name list (<see cref="Es1NameList"/>).</summary>
public class Es1NameListTransformer : Es1NameListFileTransformer<Es1NameList> {
	protected override Es1NameList ReadFile() => ReadNameList();

	protected override void WriteFile(BinaryWriter w, Es1NameList source) => WriteNameList(w, source);
}

/// <summary>
/// Reads and writes ES1's battle sets (<see cref="Es1BattleSet"/>), as <c>BattleSet_ReadFile</c> (<c>ES1GO 00013fad</c>) and
/// <c>Campaign_Read</c> (<c>0001422c</c>) read them.
/// </summary>
public class Es1BattleSetTransformer : Es1NameListFileTransformer<Es1BattleSet> {
	protected override Es1BattleSet ReadFile() {
		var campaigns = new Es1Campaign[ReadCount()];
		for (int c = 0; c < campaigns.Length; c++) {
			var positions = new Es1NameList[ReadCount()];
			for (int p = 0; p < positions.Length; p++) {
				positions[p] = ReadNameList();
			}

			campaigns[c] = new Es1Campaign { Positions = positions };
		}

		return new Es1BattleSet { Campaigns = campaigns };
	}

	protected override void WriteFile(BinaryWriter w, Es1BattleSet source) {
		w.Write(checked((byte)source.Campaigns.Length));
		foreach (var campaign in source.Campaigns) {
			w.Write(checked((byte)campaign.Positions.Length));
			foreach (var position in campaign.Positions) {
				WriteNameList(w, position);
			}
		}
	}
}

/// <summary>Reads and writes ES1's <c>msn\SING_MSN.DAT</c> (<see cref="Es1SingleMissions"/>), as <c>SingleMissions_Load</c> (<c>ES1GO 000361ac</c>) reads it.</summary>
public class Es1SingleMissionsTransformer : Es1NameListFileTransformer<Es1SingleMissions> {
	protected override Es1SingleMissions ReadFile() => new() { Lists = ReadNameLists() };

	protected override void WriteFile(BinaryWriter w, Es1SingleMissions source) => WriteNameLists(w, source.Lists);
}
