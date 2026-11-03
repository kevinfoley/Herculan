using HercWorks.Help.Html;
using Herculan.Engine.Editor;
using Xunit;

namespace Herculan.Engine.Tests;

/// <summary>
/// <see cref="TerrainImages.Read"/>: an image is taken as it is or refused with a reason, never converted or resized, so a
/// gray value is always the height byte the designer drew (docs/herculan/plan-zone-editing.md#decisions).
/// </summary>
public sealed class TerrainImagesTests : IDisposable {
	private readonly string _folder = Directory.CreateTempSubdirectory("herculan-terrain-").FullName;

	public void Dispose() => Directory.Delete(_folder, recursive: true);

	/// <summary>
	/// A gray image of a zone's size reads as its own bytes, and each kind of image that is not one is refused with a reason.
	/// Catches a change that converts a colored image to gray, resizes one of another size, or pads or cuts a raw file of
	/// the wrong length, rather than refusing it.
	/// </summary>
	[Fact]
	public void OnlyAGrayImageOfAZoneSizeIsRead() {
		var gray = new byte[128 * 128];
		for (int i = 0; i < gray.Length; i++) {
			gray[i] = (byte)(i % 251);
		}

		Assert.Equal(gray, TerrainImages.Read(Write("gray.png", PngEncoder.EncodeGray(128, 128, gray)), out string? grayFailure));
		Assert.Null(grayFailure);
		Assert.Equal(gray, TerrainImages.Read(Write("gray.raw", gray), out string? rawFailure));
		Assert.Null(rawFailure);

		var rgb = new byte[128 * 128 * 3];
		for (int i = 0; i < gray.Length; i++) {
			rgb[i * 3] = rgb[i * 3 + 1] = rgb[i * 3 + 2] = gray[i];
		}

		rgb[(64 * 128 + 64) * 3 + 2] ^= 1;
		Refused(Write("color.png", PngEncoder.Encode(128, 128, rgb)));
		Refused(Write("large.png", PngEncoder.EncodeGray(512, 512, new byte[512 * 512])));
		Refused(Write("oblong.png", PngEncoder.EncodeGray(256, 128, new byte[256 * 128])));
		Refused(Write("short.raw", gray[..^1]));
	}

	private static void Refused(string path) {
		Assert.Null(TerrainImages.Read(path, out string? failure));
		Assert.NotNull(failure);
	}

	private string Write(string name, byte[] bytes) {
		string path = Path.Combine(_folder, name);
		File.WriteAllBytes(path, bytes);
		return path;
	}
}
