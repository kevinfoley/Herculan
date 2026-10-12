namespace HercWorks.Core.Data.File;

/// <summary>
/// FILE - ES1's /SHELL0.VOL/ENG/*_TEXT.BIN — the lines of a talking-head message set: an <c>int16</c> version word, 2 in every
/// retail file, ahead of a <c>.BIN</c> string table (<see cref="StringBinaryFile"/>). The shell shows one line per load, with the
/// lip-sync record of the same number (docs/es1/formats/bin-string-tables.md#the-dialog-text-files).
/// </summary>
public class Es1DialogText {
	/// <summary>The version word: 2 in every retail file, the shell's digital dialog box; 0 makes its default box, which reads no text.</summary>
	public short Version { get; set; } = 2;

	/// <summary>The lines, in index order.</summary>
	public string[] Lines { get; set; } = [];
}
