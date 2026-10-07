using System.Buffers.Binary;
using System.IO.Compression;
using Herculan.Engine.Platform;
using Silk.NET.Core;

namespace Herculan.Engine.Host;

/// <summary>
/// The HERCULAN icon for the title bar and taskbar button where the executable's own icon is not used — any platform
/// but Windows (<see cref="EngineWindow.Icons"/>) — read from the <c>HE Icon *.png</c> files the project links from
/// <c>Branding/</c> into <c>Assets/Icon</c> beside the executable. Without these, GLFW gives its windows the system's
/// default icon.
///
/// <para>Loose files rather than an embedded resource so the artwork, which is not under the MIT license, stays a
/// visible file covered by <c>Branding/README.md</c>. A missing or unreadable file is skipped, and with none the window
/// keeps the default icon.</para>
/// </summary>
internal static class WindowIcon {
	/// <summary>Every size that loaded, for the system to pick from.</summary>
	public static RawImage[] Load() {
		string folder = Path.Combine(AppContext.BaseDirectory, "Assets", "Icon");
		if (!Directory.Exists(folder)) {
			return [];
		}

		var images = new List<RawImage>();
		foreach (string path in Directory.EnumerateFiles(folder, "HE Icon *.png")) {
			try {
				if (Decode(File.ReadAllBytes(path)) is { } image) {
					images.Add(image);
				}
			} catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException) {
				Console.Error.WriteLine($"Skipping window icon {path}: {e.Message}");
			}
		}

		return [.. images];
	}

	private static readonly byte[] Signature = [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A];

	/// <summary>
	/// An 8-bit RGBA or RGB, non-interlaced PNG as RGBA rows top-down; null for any other kind. That covers what an
	/// image editor exports for an icon, and is all this needs, so no image library is taken on for it.
	/// </summary>
	private static RawImage? Decode(byte[] png) {
		if (png.Length < Signature.Length || !png.AsSpan(0, Signature.Length).SequenceEqual(Signature)) {
			return null;
		}

		int width = 0, height = 0, channels = 0;
		using var compressed = new MemoryStream();
		int at = Signature.Length;
		while (at + 8 <= png.Length) {
			int length = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(at));
			string type = System.Text.Encoding.ASCII.GetString(png, at + 4, 4);
			int data = at + 8;
			if (length < 0 || data + length > png.Length) {
				return null;
			}

			if (type == "IHDR") {
				if (length < 13) {
					return null;
				}
				width = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(data));
				height = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(data + 4));
				byte depth = png[data + 8], colour = png[data + 9], interlace = png[data + 12];
				channels = colour switch { 6 => 4, 2 => 3, _ => 0 };
				if (depth != 8 || channels == 0 || interlace != 0 || width is <= 0 or > 1024 || height is <= 0 or > 1024) {
					return null;
				}
			} else if (type == "IDAT") {
				compressed.Write(png, data, length);
			} else if (type == "IEND") {
				break;
			}

			at = data + length + 4; // past the CRC
		}

		if (channels == 0) {
			return null;
		}

		int stride = width * channels;
		var raw = new byte[height * (stride + 1)];
		compressed.Position = 0;
		using (var zlib = new ZLibStream(compressed, CompressionMode.Decompress)) {
			zlib.ReadExactly(raw);
		}

		var rgba = new byte[width * height * 4];
		var previous = new byte[stride];
		var current = new byte[stride];
		for (int y = 0; y < height; y++) {
			int row = y * (stride + 1);
			Unfilter(raw[row], raw.AsSpan(row + 1, stride), previous, current, channels);
			for (int x = 0; x < width; x++) {
				int to = (y * width + x) * 4;
				rgba[to] = current[x * channels];
				rgba[to + 1] = current[x * channels + 1];
				rgba[to + 2] = current[x * channels + 2];
				rgba[to + 3] = channels == 4 ? current[x * channels + 3] : (byte)255;
			}

			(previous, current) = (current, previous);
		}

		return new RawImage(width, height, rgba);
	}

	/// <summary>Undoes one row's filter (PNG specification, section 9) into <paramref name="current"/>.</summary>
	private static void Unfilter(byte filter, ReadOnlySpan<byte> row, byte[] previous, byte[] current, int bpp) {
		for (int i = 0; i < row.Length; i++) {
			int left = i >= bpp ? current[i - bpp] : 0;
			int up = previous[i];
			int upLeft = i >= bpp ? previous[i - bpp] : 0;
			int predictor = filter switch {
				1 => left,
				2 => up,
				3 => (left + up) / 2,
				4 => Paeth(left, up, upLeft),
				0 => 0,
				_ => throw new InvalidDataException($"PNG row filter {filter} is not one of the five the format defines."),
			};
			current[i] = (byte)(row[i] + predictor);
		}
	}

	private static int Paeth(int a, int b, int c) {
		int p = a + b - c;
		int pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
		return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
	}
}
