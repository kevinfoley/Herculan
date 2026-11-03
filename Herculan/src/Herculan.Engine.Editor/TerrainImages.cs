using HercWorks.Core.Data.File.Dat.Sim;
using HercWorks.Help.Html;
using Herculan.Engine.Platform;

namespace Herculan.Engine.Editor;

/// <summary>
/// The images a mission's own terrain (<see cref="ZoneFiles"/>) is made from and exported as. A gray value is a cell's
/// height as it is, black the lowest: a PNG read through <see cref="PngDecoder"/>, every pixel of which is gray (red,
/// green and blue the same), a 16-bit one by its high byte; or a raw file of one byte a cell, its length the square of one of
/// <see cref="ZoneFiles.Sides"/>. Either way the image is square, one of <see cref="ZoneFiles.Sides"/> a side, its top row
/// the zone's north edge, as <see cref="ZoneFiles.Heights"/> orders them; anything else is refused with the reason, never
/// resized. Exported, a heightmap is an 8-bit grayscale PNG, which reads back as the same heights.
/// </summary>
internal static class TerrainImages {
	/// <summary>The files the picker offers.</summary>
	public static readonly NativePathPicker.FileFilter Filter = new("Terrain images (PNG, raw 8-bit)", ["png", "raw", "r8"]);

	/// <summary>The extension of an exported heightmap.</summary>
	public const string PngExtension = ".png";

	// A file past this is refused unread: a 256x256 PNG holding even incompressible noise is well under it.
	private const long MaxFileBytes = 4 << 20;

	private static readonly string SidesText = string.Join(" or ", ZoneFiles.Sides.Select(side => $"{side}x{side}"));

	/// <summary>
	/// The heights the image at <paramref name="path"/> holds, as <see cref="ZoneFiles.Heights"/> orders them, or null with
	/// the reason it holds none: it cannot be read, is not a PNG or raw file this reads, is not one of
	/// <see cref="ZoneFiles.Sides"/> square, or has a pixel that is not gray.
	/// </summary>
	public static byte[]? Read(string path, out string? failure) {
		byte[] bytes;
		try {
			if (new FileInfo(path).Length > MaxFileBytes) {
				failure = $"The file is over {MaxFileBytes >> 20} MB; a terrain image is {SidesText} pixels.";
				return null;
			}

			bytes = File.ReadAllBytes(path);
		} catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) {
			failure = ex.Message;
			return null;
		}

		return Path.GetExtension(path).ToLowerInvariant() is ".raw" or ".r8" ? Raw(bytes, out failure) : Png(bytes, out failure);
	}

	/// <summary>
	/// <paramref name="heights"/>, <paramref name="height"/> rows of <paramref name="width"/> in image order, as an 8-bit
	/// grayscale PNG.
	/// </summary>
	public static byte[] WritePng(int width, int height, ReadOnlySpan<byte> heights) =>
		PngEncoder.EncodeGray(width, height, heights[..(width * height)].ToArray());

	private static byte[]? Raw(byte[] bytes, out string? failure) {
		if (!ZoneFiles.Sides.Any(side => side * side == bytes.Length)) {
			failure = $"The raw file is {bytes.Length} bytes; a raw terrain image is one byte a cell, "
				+ string.Join(" or ", ZoneFiles.Sides.Select(side => $"{side * side} bytes for {side}x{side}")) + ".";
			return null;
		}

		failure = null;
		return bytes;
	}

	private static byte[]? Png(byte[] bytes, out string? failure) {
		if (PngDecoder.Decode(bytes, ZoneFiles.Sides.Max(), out failure) is not { } image) {
			return null;
		}

		if (image.Width != image.Height || !ZoneFiles.Sides.Contains(image.Width)) {
			failure = $"The image is {image.Width}x{image.Height} pixels; a terrain image is {SidesText}.";
			return null;
		}

		var heights = new byte[image.Width * image.Height];
		var rgba = image.Rgba;
		for (int i = 0; i < heights.Length; i++) {
			byte red = rgba[i * 4];
			if (rgba[i * 4 + 1] != red || rgba[i * 4 + 2] != red) {
				failure = $"The image has color: pixel ({i % image.Width}, {i / image.Width}) is not gray. A terrain image is "
					+ "grayscale, each gray a height.";
				return null;
			}

			heights[i] = red;
		}

		failure = null;
		return heights;
	}
}
