namespace HercWorks.Vol.Util;

/// <summary>
/// Small byte-array helpers used in place of the at.favre.lib.bytes 'Bytes' library
/// that the original Java project depended on. All VOL numeric fields on disk are
/// little-endian, so these helpers are explicit about that rather than relying on
/// host endianness (unlike System.BitConverter).
/// </summary>
public static class ByteOps {
	/// <summary>Copies a run of bytes out of an array.</summary>
	/// <param name="data">The array to copy from.</param>
	/// <param name="offset">The index of the first byte to copy.</param>
	/// <param name="length">The number of bytes to copy.</param>
	public static byte[] Slice(byte[] data, int offset, int length) {
		var result = new byte[length];
		Array.Copy(data, offset, result, 0, length);
		return result;
	}

	public static int ReadUInt16LE(byte[] data, int offset) {
		return data[offset] | (data[offset + 1] << 8);
	}

	public static int ReadInt32LE(byte[] data, int offset) {
		return data[offset]
			 | (data[offset + 1] << 8)
			 | (data[offset + 2] << 16)
			 | (data[offset + 3] << 24);
	}

	public static byte[] GetUInt16LEBytes(int value) {
		return new byte[] { (byte)(value & 0xFF), (byte)((value >> 8) & 0xFF) };
	}

	public static byte[] GetInt32LEBytes(int value) {
		return new byte[]
		{
			(byte)(value & 0xFF),
			(byte)((value >> 8) & 0xFF),
			(byte)((value >> 16) & 0xFF),
			(byte)((value >> 24) & 0xFF)
		};
	}

	public static string ToHex(byte[]? data) {
		return data == null ? string.Empty : Convert.ToHexString(data);
	}
}
