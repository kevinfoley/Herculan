using HercWorks.Core.Data.File.Dat.Shell;

namespace HercWorks.Core.Io.Transform.Shell;

/// <summary>Reads and writes ES1's <c>cfg\TM_FILES.DAT</c> (<see cref="Es1TtmFileLists"/>), as <c>TtmLists_Load</c> (<c>ES1GO 00011dfc</c>) reads it.</summary>
public class Es1TtmFileListsTransformer : Es1NameListFileTransformer<Es1TtmFileLists> {
	protected override Es1TtmFileLists ReadFile() => new() { Lists = ReadNameLists() };

	protected override void WriteFile(BinaryWriter w, Es1TtmFileLists source) => WriteNameLists(w, source.Lists);
}

/// <summary>
/// Reads and writes one of ES1's <c>snc\&lt;k&gt;_FILES.DAT</c> (<see cref="Es1LipSyncList"/>), as <c>maybe_SncList_Load</c>
/// (<c>ES1GO 000455dc</c>) reads it.
/// </summary>
public class Es1LipSyncListTransformer : Es1NameListFileTransformer<Es1LipSyncList> {
	protected override Es1LipSyncList ReadFile() {
		var lines = new Es1NameList[ReadCount()];
		if (lines.Length == 0) {
			return new Es1LipSyncList();
		}

		string script = ReadCString(Es1LipSyncList.ScriptBufferLength);
		for (int i = 0; i < lines.Length; i++) {
			lines[i] = ReadNameList();
		}

		return new Es1LipSyncList { Script = script, Lines = lines };
	}

	protected override void WriteFile(BinaryWriter w, Es1LipSyncList source) {
		w.Write(checked((byte)source.Lines.Length));
		if (source.Lines.Length == 0) {
			return;
		}

		WriteCString(w, source.Script ?? throw new InvalidDataException("A list with lines names its script."));
		foreach (var line in source.Lines) {
			WriteNameList(w, line);
		}
	}
}
