using System.Buffers.Binary;
using System.IO.Compression;

namespace HercWorks.Help.Html;

/// <summary>
/// Writes a 24-bit RGB PNG — the one format the browser needs for the manual's pictures, and small
/// enough to write here rather than take a dependency for. Public for the engine host's PrtScn files, so there is
/// one encoder rather than two.
/// </summary>
public static class PngEncoder {
	private static readonly byte[] Signature = [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A];
	private static readonly uint[] CrcTable = BuildCrcTable();

	/// <summary>The PNG of <paramref name="rgb"/>: rows top-down, three bytes a pixel in R, G, B order, no padding.</summary>
	public static byte[] Encode(int width, int height, byte[] rgb) {
		using var output = new MemoryStream();
		output.Write(Signature);

		var header = new byte[13];
		BinaryPrimitives.WriteInt32BigEndian(header, width);
		BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), height);
		header[8] = 8; // bits per channel
		header[9] = 2; // truecolour
		WriteChunk(output, "IHDR", header);

		using (var compressed = new MemoryStream()) {
			using (var zlib = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true)) {
				int row = width * 3;
				for (int y = 0; y < height; y++) {
					zlib.WriteByte(0); // filter: none
					zlib.Write(rgb, y * row, row);
				}
			}

			WriteChunk(output, "IDAT", compressed.ToArray());
		}

		WriteChunk(output, "IEND", []);
		return output.ToArray();
	}

	private static void WriteChunk(Stream output, string type, byte[] data) {
		Span<byte> word = stackalloc byte[4];
		BinaryPrimitives.WriteInt32BigEndian(word, data.Length);
		output.Write(word);

		byte[] typeBytes = [(byte)type[0], (byte)type[1], (byte)type[2], (byte)type[3]];
		output.Write(typeBytes);
		output.Write(data);

		uint crc = Crc(Crc(0xFFFFFFFF, typeBytes), data) ^ 0xFFFFFFFF;
		BinaryPrimitives.WriteUInt32BigEndian(word, crc);
		output.Write(word);
	}

	private static uint Crc(uint crc, byte[] data) {
		foreach (byte b in data) {
			crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
		}

		return crc;
	}

	private static uint[] BuildCrcTable() {
		var table = new uint[256];
		for (uint n = 0; n < 256; n++) {
			uint c = n;
			for (int k = 0; k < 8; k++) {
				c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
			}

			table[n] = c;
		}

		return table;
	}
}
