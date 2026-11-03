using HercWorks.Help.Html;
using Xunit;

namespace HercWorks.Help.Tests;

/// <summary><see cref="PngDecoder"/> reads what <see cref="PngEncoder"/> writes.</summary>
public class PngTests {
	/// <summary>
	/// A grayscale PNG reads back as the gray values written, opaque. Catches a change to either side that breaks the mission
	/// editor's heightmap export reading back in as the same heights: a wrong color type or bit depth written, a row filter
	/// undone wrongly, or a gray sample not copied to all three channels.
	/// </summary>
	[Fact]
	public void AGrayscalePngReadsBackAsWritten() {
		const int width = 256, height = 128;
		var gray = new byte[width * height];
		for (int i = 0; i < gray.Length; i++) {
			gray[i] = (byte)(i * 7 + i / width);
		}

		var image = PngDecoder.Decode(PngEncoder.EncodeGray(width, height, gray), width, out string? failure);

		Assert.Null(failure);
		Assert.NotNull(image);
		Assert.Equal((width, height), (image.Width, image.Height));
		for (int i = 0; i < gray.Length; i++) {
			Assert.Equal([gray[i], gray[i], gray[i], 255], image.Rgba.AsSpan(i * 4, 4).ToArray());
		}
	}
}
