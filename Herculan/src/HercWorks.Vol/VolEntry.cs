using HercWorks.Vol.Util;

namespace HercWorks.Vol;

/// <summary>
/// Earthsiege 2 VOL entries follow this schema:
///   18 bytes total for the listing entry.
///     13 bytes for file name (plus odd trailing bytes observed on some entries)
///     UINT8  directory index number - mapped to the directory listing
///     UINT32 file's offset in the VOL (little-endian)
///
///   File prefix - 9 bytes, found at the offset above:
///     UINT8  compression type: 2 = stored, the only value in the retail archives
///            (7 RLE and 9 LZH are decoded by the game but never used)
///     UINT32 content size in bytes (little-endian) - the content alone, no prefix, no trailer
///     UINT16 MS-DOS packed date, UINT16 MS-DOS packed time - the source file's timestamp
///
///   Then <c>size</c> content bytes, then one trailing byte that repeats the last content byte.
///
/// The prefix and the trailing byte belong to the archive, not to the file: what the game reads
/// is the content alone. See docs/retail/formats/vol-archive.md for the evidence.
///
/// Ported from org.hercworks.voln.VolEntry.
/// </summary>
public class VolEntry : DataFile {
	/// <summary>Raw little-endian offset bytes as read from the VOL file (round-tripped as-is on strict writes).</summary>
	public byte[]? VolOffset { get; set; }

	/// <summary>Raw 13-byte filename field, including any odd trailing bytes observed on some entries.</summary>
	public byte[]? VolListBytes { get; set; }

	public byte DirIdx { get; set; }

	/// <summary>
	/// The single byte the archive stores after this entry's content, before the next entry's
	/// prefix. It repeats the content's last byte and is outside the declared size; VSHELL's
	/// stream read copies it on a read that runs past the content. Round-tripped rather than
	/// reconstructed, as are the writers' (<see cref="VolEntryPrefixCodec"/>,
	/// <c>VolFileWriter</c>, <c>VolFileCompiler</c>).
	/// </summary>
	public byte[]? Trailer { get; set; }

	/// <summary>
	/// The prefix's 4-byte field at +5: the source file's MS-DOS packed date (UINT16) and time
	/// (UINT16). Kept raw — TransformerRegistry matches some file types on its exact value.
	/// </summary>
	public byte[]? MagicPrefix { get; set; }

	public byte FileCompressionType { get; set; }

	/// <summary>Numeric interpretation of <see cref="VolOffset"/> (little-endian).</summary>
	public int VolOffsetValue => VolOffset == null ? 0 : ByteOps.ReadInt32LE(VolOffset, 0);

	public override string ToString() {
		return "VolEntry [fileName=" + FileName
			 + ", volOffset=" + VolOffsetValue
			 + ", byteSize=" + (RawBytes?.Length ?? 0)
			 + ", magicPrefix=" + PrintMagicPrefix()
			 + ", dirIdx=" + DirIdx + "]";
	}

	/// <summary>
	/// Sometimes in a VOL, the file-list entry has trailing bytes after the filename string.
	/// This strips the trailing bytes to produce a clean file name.
	/// </summary>
	public static string NameFromListBytes(byte[] listBytes) {
		var chars = new System.Text.StringBuilder();
		foreach (byte b in listBytes) {
			if (b == 0x00) break;
			chars.Append((char)b);
		}
		return chars.ToString();
	}

	public string PrintMagicPrefix() {
		return $"{FileName}|\t({FileCompressionType})\t[{ByteOps.ToHex(MagicPrefix)}]\t";
	}
}
