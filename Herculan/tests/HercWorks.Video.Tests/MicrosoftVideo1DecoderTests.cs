using HercWorks.Video.Avi;
using HercWorks.Video.Codecs;
using Xunit;

namespace HercWorks.Video.Tests;

/// <summary>
/// The Microsoft Video 1 decoder, over hand-built packets and the six retail files. Hand-built
/// packets are for a 4x4 or 8x8 frame, one or four blocks, bottom-up.
/// </summary>
public sealed class MicrosoftVideo1DecoderTests {
	private const ushort Red = 0x7C00;
	private const ushort Green = 0x03E0;
	private const ushort White = 0x7FFF;

	/// <summary>
	/// A word with bit 15 set is a one-colour block, and the first block is the bottom-left one.
	/// Catches bit 15 being read as a flag on a colour, and the block order being flipped.
	/// </summary>
	[Fact]
	public void OneColourBlockFillsTheBottomLeftBlock() {
		VideoFrame frame = Decode(8, 8, Words(0x8000 | White, 0x8403, 0));

		Assert.Equal((255, 255, 255), Pixel(frame, 0, 7));
		Assert.Equal((255, 255, 255), Pixel(frame, 3, 4));
		Assert.Equal((0, 0, 0), Pixel(frame, 4, 7));
		Assert.Equal((0, 0, 0), Pixel(frame, 0, 3));
	}

	/// <summary>
	/// A two-colour block's flag bit n is pixel n, four to a row from the bottom, and a set bit picks
	/// the first colour. Catches the flags being read top row first, or the colour choice inverted.
	/// </summary>
	[Fact]
	public void TwoColourFlagsPickPerPixelFromTheBottomRow() {
		VideoFrame frame = Decode(4, 4, Words(0x0011, Red, Green, 0));

		Assert.Equal((255, 0, 0), Pixel(frame, 0, 3));
		Assert.Equal((255, 0, 0), Pixel(frame, 0, 2));
		Assert.Equal((0, 255, 0), Pixel(frame, 1, 3));
		Assert.Equal((0, 255, 0), Pixel(frame, 0, 0));
	}

	/// <summary>
	/// Bit 15 on the first colour makes an eight-colour block, one pair per 2x2 quadrant. Catches
	/// the quadrants being assigned in the wrong order, or the six extra colours not being consumed.
	/// The flag word is below <c>0x8000</c> like any other, so the last pixel always takes the
	/// second colour of its pair.
	/// </summary>
	[Fact]
	public void EightColourBlockGivesEachQuadrantItsOwnPair() {
		ushort[] colours = [0x8000 | (1 << 10), 0, 2 << 10, 0, 3 << 10, 0, 4 << 10, 5 << 10];
		VideoFrame frame = Decode(8, 4, Words([0x7FFF, .. colours, 0x8000 | White]));

		Assert.Equal(Expand(1), Pixel(frame, 0, 3).R);
		Assert.Equal(Expand(2), Pixel(frame, 3, 3).R);
		Assert.Equal(Expand(3), Pixel(frame, 0, 0).R);
		Assert.Equal(Expand(4), Pixel(frame, 2, 0).R);
		Assert.Equal(Expand(5), Pixel(frame, 3, 0).R);

		// The block after it starts on the next word, not inside the colour list.
		Assert.Equal((255, 255, 255), Pixel(frame, 4, 3));
	}

	/// <summary>
	/// A skip run leaves its blocks as the previous frame left them. Checked across two packets,
	/// because that is what makes the codec interframe.
	/// </summary>
	[Fact]
	public void SkipLeavesThePreviousFrame() {
		IVideoCodec codec = CreateCodec(8, 4);
		var frame = new VideoFrame(8, 4);

		Assert.True(codec.DecodeFrame(Words(0x8000 | Red, 0x8000 | Red, 0), frame));
		Assert.True(codec.DecodeFrame(Words(0x8401, 0x8000 | Green, 0), frame));

		Assert.Equal((255, 0, 0), Pixel(frame, 0, 0));
		Assert.Equal((0, 255, 0), Pixel(frame, 4, 0));
	}

	/// <summary>
	/// Every prefix of a valid packet decodes without throwing, and one that ends before the last
	/// block is reported as malformed.
	/// </summary>
	[Fact]
	public void SurvivesTruncatedPackets() {
		byte[] full = Words(0x7FFF, 0x8000, 0, 0, 0, 0, 0, 0, 0, 0x0F0F, Red, Green, 0x8401, 0x8000 | White, 0);

		for (int length = 1; length < full.Length - 2; length++) {
			var frame = new VideoFrame(8, 8);
			IVideoCodec codec = CreateCodec(8, 8);
			bool ok = true;
			Assert.Null(Record.Exception(() => ok = codec.DecodeFrame(full[..length], frame)));
			Assert.False(ok);
		}
	}

	/// <summary>
	/// Only the 16-bit form is decoded, over whole blocks. Catches a palettised or ragged stream
	/// being decoded as if it were the form the corpus uses.
	/// </summary>
	[Fact]
	public void RefusesFormatsTheCorpusDoesNotUse() {
		Assert.Null(CodecRegistry.Create(Format(16, 16, 8), VideoLimits.Default));
		Assert.Null(CodecRegistry.Create(Format(18, 16, 16), VideoLimits.Default));
		Assert.Null(CodecRegistry.Create(Format(16, 18, 16), VideoLimits.Default));
		Assert.NotNull(CodecRegistry.Create(Format(16, 16, 16), VideoLimits.Default));
	}

	/// <summary>
	/// <c>ALPHA.AVI</c> decodes whole, and its last frame matches a digest of this decoder's output.
	/// The file carries a second, 8-bit video stream whose packets must not reach this decoder, so the
	/// frame count is the first stream's. The digest pins the output against regression; what it was
	/// checked against is a look at the frame itself. Passes vacuously without the retail file.
	/// </summary>
	[Fact]
	public void DecodesTheRetailAlphaMap() {
		string? path = RetailFiles.Find(Path.Combine("AVI", "ALPHA.AVI"));
		if (path is null) {
			return;
		}

		MoviePlayback playback = MoviePlayback.Open(File.ReadAllBytes(path))!;
		Assert.Equal(19, playback.FrameCount);

		playback.Advance(playback.Duration);
		Assert.False(playback.HasFailed);
		Assert.Equal(18, playback.CurrentFrameIndex);
		Assert.Equal(0xF2BA65FB8191B0D2UL, Digest(playback.Frame.Rgba));
	}

	/// <summary>
	/// The 640x480 <c>ESTAB2.AVI</c> decodes whole and its last frame matches a digest. Passes
	/// vacuously without the retail file.
	/// </summary>
	[Fact]
	public void DecodesTheRetailSectorTitle() {
		string? path = RetailFiles.Find(Path.Combine("AVI", "ESTAB2.AVI"));
		if (path is null) {
			return;
		}

		MoviePlayback playback = MoviePlayback.Open(File.ReadAllBytes(path))!;
		Assert.Equal(3, playback.FrameCount);

		playback.Advance(playback.Duration);
		Assert.False(playback.HasFailed);
		Assert.Equal(2, playback.CurrentFrameIndex);
		Assert.Equal(0x3A934BE715916AE4UL, Digest(playback.Frame.Rgba));
	}

	private static AviVideoFormat Format(int width, int height, ushort bitCount) =>
		new(width, height, false, bitCount, CodecRegistry.MicrosoftVideo1Cram, []);

	private static IVideoCodec CreateCodec(int width, int height) =>
		CodecRegistry.Create(Format(width, height, 16), VideoLimits.Default)!;

	private static VideoFrame Decode(int width, int height, byte[] packet) {
		var frame = new VideoFrame(width, height);
		Assert.True(CreateCodec(width, height).DecodeFrame(packet, frame));
		return frame;
	}

	private static byte[] Words(params ushort[] words) {
		var bytes = new byte[words.Length * 2];
		for (int i = 0; i < words.Length; i++) {
			bytes[i * 2] = (byte)words[i];
			bytes[(i * 2) + 1] = (byte)(words[i] >> 8);
		}

		return bytes;
	}

	private static byte Expand(int channel) => (byte)((channel << 3) | (channel >> 2));

	private static (int R, int G, int B) Pixel(VideoFrame frame, int x, int y) {
		int at = ((y * frame.Width) + x) * 4;
		return (frame.Rgba[at], frame.Rgba[at + 1], frame.Rgba[at + 2]);
	}

	private static ulong Digest(byte[] bytes) {
		ulong hash = 0xcbf2_9ce4_8422_2325;
		foreach (byte b in bytes) {
			hash = (hash ^ b) * 0x0000_0100_0000_01b3;
		}

		return hash;
	}
}
