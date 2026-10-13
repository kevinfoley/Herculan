namespace HercWorks.Core.Data.File;

/// <summary>
/// FILE - one of ES1's shell lip-sync scripts, <c>SHELL0.VOL</c>'s <c>snc\*.SNC</c> (docs/es1/formats/snc-lip-sync.md#the-shells-scripts).
/// The records are stored as the file holds them, each delta its own; the running totals the shell turns them into as it
/// loads them are not in the model. Read by <see cref="Io.Transform.Shell.Es1ShellLipSyncScriptTransformer"/>.
/// </summary>
public class Es1ShellLipSyncScript {
	/// <summary>The <c>int16</c> length as read: four times the records' count in every retail file.</summary>
	public short Length { get; set; }

	/// <summary>The records in order, <see cref="Length"/> / 4 of them.</summary>
	public Es1ShellLipSyncRecord[] Records { get; set; } = [];

	/// <summary>
	/// Bytes after the records: what a length that is not a multiple of 4 leaves, and anything past the length. Empty in every
	/// retail file; kept so a written script gives back its bytes.
	/// </summary>
	public byte[] Unread { get; set; } = [];
}

/// <summary>One record of an <see cref="Es1ShellLipSyncScript"/>.</summary>
public class Es1ShellLipSyncRecord {
	/// <summary>What the line's <c>.TTM</c> script is handed.</summary>
	public short Frame { get; set; }

	/// <summary>Timer counts until the next record.</summary>
	public short Delta { get; set; }
}
