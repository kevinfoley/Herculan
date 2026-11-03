using System.Buffers.Binary;
using System.IO.Compression;

namespace HercWorks.Help.Html;

/// <summary>A decoded picture: <see cref="Width"/> by <see cref="Height"/> pixels, rows top-down, four bytes a pixel in R, G, B, A order.</summary>
public sealed record PngImage(int Width, int Height, byte[] Rgba);

/// <summary>
/// Reads a PNG (PNG specification, third edition) into RGBA, the counterpart of <see cref="PngEncoder"/>: every color type, at
/// every bit depth the specification allows, non-interlaced. A 16-bit sample keeps its high byte; a sample under 8 bits is
/// scaled up to fill one. A palette's transparency (<c>tRNS</c>) is not read, so palette pixels are opaque. The engine host's
/// window icon and the mission editor's terrain images both read through it, so there is one decoder rather than two.
/// </summary>
public static class PngDecoder {
	private static readonly byte[] Signature = [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A];

	private const int Gray = 0, Rgb = 2, Palette = 3, GrayAlpha = 4, Rgba = 6;

	/// <summary>
	/// The picture in <paramref name="png"/>, or null with the reason it is not one this reads: not a PNG, cut short, wider
	/// or taller than <paramref name="maxSide"/> pixels, interlaced, or a color type and bit depth the specification does not
	/// pair. Never throws on bad bytes.
	/// </summary>
	public static PngImage? Decode(byte[] png, int maxSide, out string? failure) {
		if (png.Length < Signature.Length || !png.AsSpan(0, Signature.Length).SequenceEqual(Signature)) {
			failure = "The file is not a PNG.";
			return null;
		}

		int width = 0, height = 0, depth = 0, color = -1;
		byte[]? palette = null;
		using var compressed = new MemoryStream();
		int at = Signature.Length;
		while (at + 8 <= png.Length) {
			int length = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(at));
			string type = System.Text.Encoding.ASCII.GetString(png, at + 4, 4);
			int data = at + 8;
			if (length < 0 || data + length > png.Length) {
				failure = "The PNG is cut short.";
				return null;
			}

			if (type == "IHDR") {
				if (length < 13) {
					failure = "The PNG's header is cut short.";
					return null;
				}

				width = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(data));
				height = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(data + 4));
				depth = png[data + 8];
				color = png[data + 9];
				if (width <= 0 || height <= 0 || width > maxSide || height > maxSide) {
					failure = $"The image is {width}x{height} pixels; at most {maxSide}x{maxSide} is read.";
					return null;
				}

				if (!Allowed(color, depth)) {
					failure = $"The PNG's color type {color} at {depth} bits is not one the PNG specification defines.";
					return null;
				}

				if (png[data + 12] != 0) {
					failure = "The PNG is interlaced; save it without interlacing.";
					return null;
				}
			} else if (type == "PLTE") {
				palette = png.AsSpan(data, length).ToArray();
			} else if (type == "IDAT") {
				compressed.Write(png, data, length);
			} else if (type == "IEND") {
				break;
			}

			at = data + length + 4; // past the CRC
		}

		if (color < 0) {
			failure = "The PNG has no header.";
			return null;
		}

		if (color == Palette && palette == null) {
			failure = "The PNG has a palette color type but no palette.";
			return null;
		}

		int channels = color switch { Gray => 1, Rgb => 3, Palette => 1, GrayAlpha => 2, _ => 4 };
		int bitsPerPixel = channels * depth;
		int stride = (width * bitsPerPixel + 7) / 8;
		int filterStep = Math.Max(1, bitsPerPixel / 8);
		var raw = new byte[height * (stride + 1)];
		try {
			compressed.Position = 0;
			using var zlib = new ZLibStream(compressed, CompressionMode.Decompress);
			zlib.ReadExactly(raw);
		} catch (Exception ex) when (ex is InvalidDataException or EndOfStreamException) {
			failure = "The PNG's image data is cut short or damaged.";
			return null;
		}

		var rgba = new byte[width * height * 4];
		var previous = new byte[stride];
		var current = new byte[stride];
		for (int y = 0; y < height; y++) {
			int row = y * (stride + 1);
			if (!Unfilter(raw[row], raw.AsSpan(row + 1, stride), previous, current, filterStep)) {
				failure = $"The PNG's row {y} names filter {raw[row]}; the format has five, 0 to 4.";
				return null;
			}

			for (int x = 0; x < width; x++) {
				int to = (y * width + x) * 4;
				switch (color) {
					case Gray:
						rgba[to] = rgba[to + 1] = rgba[to + 2] = Sample(current, x, depth);
						rgba[to + 3] = 255;
						break;
					case GrayAlpha:
						rgba[to] = rgba[to + 1] = rgba[to + 2] = Sample(current, x * 2, depth);
						rgba[to + 3] = Sample(current, x * 2 + 1, depth);
						break;
					case Palette:
						int entry = Index(current, x, depth) * 3;
						if (entry + 2 >= palette!.Length) {
							failure = $"The PNG's pixel ({x}, {y}) names palette entry {entry / 3}, past its {palette.Length / 3}.";
							return null;
						}

						rgba[to] = palette[entry];
						rgba[to + 1] = palette[entry + 1];
						rgba[to + 2] = palette[entry + 2];
						rgba[to + 3] = 255;
						break;
					default:
						for (int c = 0; c < channels; c++) {
							rgba[to + c] = Sample(current, x * channels + c, depth);
						}

						if (channels == 3) {
							rgba[to + 3] = 255;
						}

						break;
				}
			}

			(previous, current) = (current, previous);
		}

		failure = null;
		return new PngImage(width, height, rgba);
	}

	// The color type and bit depth pairs the specification's table 11.1 allows.
	private static bool Allowed(int color, int depth) => color switch {
		Gray => depth is 1 or 2 or 4 or 8 or 16,
		Palette => depth is 1 or 2 or 4 or 8,
		Rgb or GrayAlpha or Rgba => depth is 8 or 16,
		_ => false
	};

	// Sample <index> of a row as one byte: a 16-bit one's high byte, one under 8 bits scaled to fill the byte.
	private static byte Sample(byte[] row, int index, int depth) => depth switch {
		16 => row[index * 2],
		8 => row[index],
		_ => (byte)(Index(row, index, depth) * 255 / ((1 << depth) - 1))
	};

	// Sample <index> of a row of <depth>-bit samples (8 or fewer) as it is stored, packed from the high bit down.
	private static int Index(byte[] row, int index, int depth) {
		if (depth == 8) {
			return row[index];
		}

		int bit = index * depth;
		return (row[bit / 8] >> (8 - depth - bit % 8)) & ((1 << depth) - 1);
	}

	/// <summary>
	/// Undoes one row's filter (PNG specification, section 9) into <paramref name="current"/>; false for a filter type the
	/// format does not define.
	/// </summary>
	private static bool Unfilter(byte filter, ReadOnlySpan<byte> row, byte[] previous, byte[] current, int step) {
		if (filter > 4) {
			return false;
		}

		for (int i = 0; i < row.Length; i++) {
			int left = i >= step ? current[i - step] : 0;
			int up = previous[i];
			int upLeft = i >= step ? previous[i - step] : 0;
			int predictor = filter switch {
				1 => left,
				2 => up,
				3 => (left + up) / 2,
				4 => Paeth(left, up, upLeft),
				_ => 0,
			};
			current[i] = (byte)(row[i] + predictor);
		}

		return true;
	}

	private static int Paeth(int a, int b, int c) {
		int p = a + b - c;
		int pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
		return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
	}
}
