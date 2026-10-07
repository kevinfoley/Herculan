using HercWorks.Vol;

namespace HercWorks.UI;

/// <summary>
/// A game data file the editors have opened, from wherever it was found: a loose file on disk or an
/// entry still packed inside a .VOL. Carries the content with the 9-byte VOL entry prefix already
/// stripped, plus the prefix fields themselves so a save can rebuild the archive-entry shape on
/// request — see VolEntryPrefixCodec. The game reads a loose file content-only, from offset 0
/// (docs/retail/formats/vol-archive.md#loose-files-on-disk-carry-no-prefix), so the prefix only belongs on
/// a file headed back into an unpacked archive tree.
/// </summary>
public sealed class GameFile {
	/// <summary>Bare file name, e.g. <c>HERC_INF.DAT</c>.</summary>
	public required string FileName { get; init; }

	/// <summary>Where it came from, for the status line — a full path, or <c>SHELL0.VOL\GAM</c>.</summary>
	public required string Location { get; init; }

	/// <summary>Full path on disk, or null when this came out of a packed VOL and has none.</summary>
	public string? LoosePath { get; init; }

	public required byte[] Content { get; init; }

	public byte? CompressionType { get; init; }
	public byte[]? MagicPrefix { get; init; }
	public bool HadTrailingByte { get; init; }

	public static GameFile FromLooseFile(string path) {
		var prefix = VolEntryPrefixCodec.StripIfPresent(File.ReadAllBytes(path));

		return new GameFile {
			FileName = Path.GetFileName(path),
			Location = path,
			LoosePath = path,
			Content = prefix.Content,
			CompressionType = prefix.HadPrefix ? prefix.CompressionType : null,
			MagicPrefix = prefix.MagicPrefix,
			HadTrailingByte = prefix.HadTrailingByte
		};
	}

	/// <summary>
	/// A VOL entry arrives already split by VolFileReader — RawBytes is content only, with the
	/// prefix fields parsed out alongside it — so nothing needs stripping here.
	/// </summary>
	public static GameFile FromVolEntry(VolEntry entry, string volFileName, string dirLabel) {
		return new GameFile {
			FileName = entry.FileName ?? string.Empty,
			Location = Path.Combine(volFileName, dirLabel),
			LoosePath = null,
			Content = entry.RawBytes ?? Array.Empty<byte>(),
			CompressionType = entry.FileCompressionType,
			MagicPrefix = entry.MagicPrefix,
			HadTrailingByte = entry.Trailer != null
		};
	}
}
