using HercWorks.Vol.Util;

namespace HercWorks.Vol;

/// <summary>
/// Result of <see cref="VolEntryPrefixCodec.StripIfPresent"/>: the file's content bytes plus
/// whatever per-entry prefix fields were captured (if any) so they can be preserved on export.
/// </summary>
public readonly record struct VolEntryPrefixResult(
    byte[] Content, bool HadPrefix, byte CompressionType, byte[]? MagicPrefix, bool HadTrailingByte);

/// <summary>
/// Handles the same 9-byte per-entry prefix (1-byte compression type + 4-byte little-endian content
/// size + 4-byte MS-DOS date/time) that <see cref="VolEntry"/> carries when parsed out of a packed
/// .VOL, plus the single trailing byte that follows an entry's content in the archive — see
/// docs/retail/formats/vol-archive.md.
///
/// <para>A loose file on disk may be either shape: the retail game's own overrides are
/// content-only, while anything unpacked by a tool that copies archive bytes wholesale (such as
/// <c>ES2/VOL/extractVol.py</c>) keeps the prefix. Editors open paths the user picked, so
/// detection lives here rather than being re-derived per editor.</para>
/// </summary>
public static class VolEntryPrefixCodec {
	private const int PrefixLength = 9;

	/// <summary>
	/// Detects the prefix via the entry's own self-declared content-size field (reliable — it's
	/// the same signal <see cref="Io.VolFileReader"/> relies on) and strips it off, returning
	/// clean content either way. If the file doesn't match that shape, it's assumed to already be
	/// content-only and is returned unchanged.
	/// </summary>
	public static VolEntryPrefixResult StripIfPresent(byte[] bytes) {
		if (bytes.Length > PrefixLength) {
			int declaredContentSize = ByteOps.ReadInt32LE(bytes, 1);
			long expectedTotal = PrefixLength + declaredContentSize;

			// Allow for the single trailing "unknown EOF byte" VolFileReader also observed.
			if (expectedTotal == bytes.Length || expectedTotal + 1 == bytes.Length) {
				bool hasTrailingByte = expectedTotal + 1 == bytes.Length;
				byte compressionType = bytes[0];
				byte[] magicPrefix = ByteOps.Slice(bytes, 5, 4);
				byte[] content = ByteOps.Slice(bytes, PrefixLength, declaredContentSize);

				return new VolEntryPrefixResult(content, true, compressionType, magicPrefix, hasTrailingByte);
			}
		}

		return new VolEntryPrefixResult(bytes, false, 0, null, false);
	}

	/// <summary>
	/// Rebuilds the archive byte layout around edited content: original compression type and date/time
	/// preserved byte-for-byte, size field recalculated for the new content length, and the
	/// trailing byte reproduced the way the retail packer writes it — as a repeat of the last
	/// content byte.
	/// </summary>
	public static byte[] Wrap(byte[] content, byte compressionType, byte[] magicPrefix, bool includeTrailingByte) {
		using var outStream = new MemoryStream();
		outStream.WriteByte(compressionType);

		var sizeBytes = ByteOps.GetInt32LEBytes(content.Length);
		outStream.Write(sizeBytes, 0, sizeBytes.Length);
		outStream.Write(magicPrefix, 0, magicPrefix.Length);
		outStream.Write(content, 0, content.Length);

		if (includeTrailingByte && content.Length > 0) {
			outStream.WriteByte(content[^1]);
		}

		return outStream.ToArray();
	}
}
