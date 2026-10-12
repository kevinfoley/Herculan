using System.Text;
using HercWorks.Core.Data.File.Dat.Shell;

namespace HercWorks.Core.Io.Transform.Shell;

/// <summary>
/// The name list reading ES1's battle sets, mission lists and single missions share (<see cref="Es1NameList"/>;
/// docs/es1/formats/bst-battle-sets.md#name-lists), as <c>StrList_Read</c> (<c>ES1GO 000201b7</c>) reads it. Writing a parsed
/// file gives back its bytes.
///
/// <para>A name with no NUL in its 13 bytes, a file that ends inside a list, and bytes left after the last list throw
/// <see cref="InvalidDataException"/>, so a file the model would not give back whole is rejected. The retail reader takes 13
/// bytes as a name without its NUL, and ignores what follows.</para>
/// </summary>
public abstract class Es1NameListFileTransformer<T> : ByteTransformer<T> where T : class {
	/// <summary>Reads the file's content from the cursor; the base checks that it ends where the file does.</summary>
	protected abstract T ReadFile();

	/// <summary>Writes the file's content.</summary>
	protected abstract void WriteFile(BinaryWriter w, T source);

	/// <exception cref="InvalidDataException">The bytes do not walk to the file's end as name lists.</exception>
	public sealed override T? Parse(byte[]? inputArray) {
		if (inputArray == null || inputArray.Length <= 0) {
			return null;
		}

		SetBytes(inputArray);
		T file = ReadFile();
		if (Index != inputArray.Length) {
			throw new InvalidDataException($"{inputArray.Length - Index} bytes follow the last list, at {Index}.");
		}

		return file;
	}

	public sealed override byte[]? Write(T source) {
		using var o = new MemoryStream();
		var w = new BinaryWriter(o);
		WriteFile(w, source);
		w.Flush();
		return o.ToArray();
	}

	/// <summary>A count byte at the cursor.</summary>
	protected byte ReadCount() {
		RequireBytes(1);
		return IndexByte();
	}

	/// <summary>A name list at the cursor.</summary>
	protected Es1NameList ReadNameList() {
		int count = ReadCount();
		if (count == 0) {
			return new Es1NameList { SkippedByte = Index < Bytes!.Length ? IndexByte() : null };
		}

		var names = new string[count];
		for (int i = 0; i < count; i++) {
			int end = Array.IndexOf(Bytes!, (byte)0, Index, Math.Min(Es1NameList.NameBufferLength, Bytes!.Length - Index));
			if (end < 0) {
				throw new InvalidDataException($"Name {i} at {Index} has no NUL in its {Es1NameList.NameBufferLength} bytes.");
			}

			names[i] = Encoding.Latin1.GetString(Bytes!, Index, end - Index);
			Index = end + 1;
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
			w.Write(Encoding.Latin1.GetBytes(name));
			w.Write((byte)0);
		}
	}

	private void RequireBytes(int count) {
		if (Index + count > Bytes!.Length) {
			throw new InvalidDataException($"The file ends at {Bytes.Length}, inside a list read at {Index}.");
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
	protected override Es1SingleMissions ReadFile() {
		var lists = new Es1NameList[ReadCount()];
		for (int i = 0; i < lists.Length; i++) {
			lists[i] = ReadNameList();
		}

		return new Es1SingleMissions { Lists = lists };
	}

	protected override void WriteFile(BinaryWriter w, Es1SingleMissions source) {
		w.Write(checked((byte)source.Lists.Length));
		foreach (var list in source.Lists) {
			WriteNameList(w, list);
		}
	}
}
