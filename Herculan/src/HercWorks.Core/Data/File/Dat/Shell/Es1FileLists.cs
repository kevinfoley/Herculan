namespace HercWorks.Core.Data.File.Dat.Shell;

/// <summary>
/// FILE - ES1's <c>cfg\TM_FILES.DAT</c> in <c>SHELL0.VOL</c>: a count byte and that many name lists of <c>.TTM</c> scripts
/// (docs/es1/formats/shell-file-lists.md#the-animation-lists). Read by <see cref="Io.Transform.Shell.Es1TtmFileListsTransformer"/>.
/// </summary>
public class Es1TtmFileLists {
	public Es1NameList[] Lists { get; set; } = [];
}

/// <summary>
/// FILE - one of ES1's <c>snc\&lt;k&gt;_FILES.DAT</c> in <c>SHELL0.VOL</c> (<c>C0</c>–<c>C3</c>, <c>D0</c>–<c>D3</c>, <c>G0</c>,
/// <c>T0</c>, <c>T1</c>): a count byte, then, when it is not 0, a <c>.TTM</c> script's name and that many name lists, one per
/// line of the talking head's text (docs/es1/formats/shell-file-lists.md#the-lip-sync-lists). Read by
/// <see cref="Io.Transform.Shell.Es1LipSyncListTransformer"/>.
/// </summary>
public class Es1LipSyncList {
	/// <summary>The longest script name, its NUL included: <c>maybe_SncList_Load</c> (<c>ES1GO 000455dc</c>) reads it into 32 bytes.</summary>
	public const int ScriptBufferLength = 32;

	/// <summary>The script's name; null when the count is 0, which ends the file.</summary>
	public string? Script { get; set; }

	/// <summary>Per line, its lip-sync and sound file names.</summary>
	public Es1NameList[] Lines { get; set; } = [];
}
