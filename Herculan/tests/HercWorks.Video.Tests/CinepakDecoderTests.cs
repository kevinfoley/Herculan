using HercWorks.Video.Avi;
using HercWorks.Video.Codecs;
using Xunit;

namespace HercWorks.Video.Tests;

/// <summary>
/// The Cinepak decoder: one hand-built frame through the strip, codebook and vector chunks, and
/// robustness against truncated and damaged packets.
/// </summary>
public sealed class CinepakDecoderTests {
	private const int Width = 8;
	private const int Height = 4;

	/// <summary>
	/// An 8x4 key frame: one strip, a full V1 codebook load of two entries, then a V1-only vector
	/// chunk naming entry 0 for the left block and entry 1 for the right. Chroma is zero, so each
	/// entry's four luma values come out as grey.
	/// </summary>
	private static byte[] BuildFrame() {
		byte[] codebook = [
			0x22, 0x00, 0x00, 16,
			10, 20, 30, 40, 0, 0,
			50, 60, 70, 80, 0, 0,
		];
		byte[] vectors = [0x32, 0x00, 0x00, 6, 0, 1];
		int stripLength = 12 + codebook.Length + vectors.Length;

		var packet = new List<byte> {
			// Frame header: flags, 24-bit length, width, height, strip count, all big-endian.
			0x01, 0x00, 0x00, (byte)(10 + stripLength),
			0x00, Width, 0x00, Height,
			0x00, 0x01,

			// Strip header: id, 24-bit length, top, left, height, width.
			0x10, 0x00, 0x00, (byte)stripLength,
			0x00, 0x00, 0x00, 0x00,
			0x00, Height, 0x00, Width,
		};
		packet.AddRange(codebook);
		packet.AddRange(vectors);
		return [.. packet];
	}

	/// <summary>
	/// A V1 block is its entry scaled up: each of the entry's four pixels covers a 2x2 quarter, in
	/// the order top-left, top-right, bottom-left, bottom-right.
	/// </summary>
	[Fact]
	public void PaintsV1BlocksFromTheirCodebookEntries() {
		var frame = new VideoFrame(Width, Height);
		Assert.True(CreateCodec().DecodeFrame(BuildFrame(), frame));

		Assert.Equal(10, Red(frame, 0, 0));
		Assert.Equal(10, Red(frame, 1, 1));
		Assert.Equal(20, Red(frame, 3, 0));
		Assert.Equal(30, Red(frame, 0, 3));
		Assert.Equal(40, Red(frame, 3, 3));
		Assert.Equal(50, Red(frame, 4, 0));
		Assert.Equal(80, Red(frame, 7, 3));
	}

	/// <summary>Every prefix of a valid frame decodes or is rejected; none throws.</summary>
	[Fact]
	public void SurvivesEveryTruncation() {
		byte[] full = BuildFrame();
		IVideoCodec codec = CreateCodec();

		for (int length = 0; length <= full.Length; length++) {
			var frame = new VideoFrame(Width, Height);
			Assert.Null(Record.Exception(() => codec.DecodeFrame(full.AsSpan(0, length), frame)));
		}
	}

	/// <summary>
	/// Random damage anywhere in the packet — strip count, strip and chunk lengths, chunk ids,
	/// codebook entries, vector indices and flags — never throws. One codec runs every trial, so a
	/// damaged codebook load is carried into the next trial's decode, as it would be into the next
	/// frame's.
	/// </summary>
	[Fact]
	public void SurvivesCorruptedPackets() {
		byte[] original = BuildFrame();
		IVideoCodec codec = CreateCodec();
		var random = new Random(1996);

		for (int trial = 0; trial < 5000; trial++) {
			byte[] packet = (byte[])original.Clone();
			int hits = 1 + random.Next(4);
			for (int i = 0; i < hits; i++) {
				packet[random.Next(packet.Length)] = (byte)random.Next(256);
			}

			var frame = new VideoFrame(Width, Height);
			Assert.Null(Record.Exception(() => codec.DecodeFrame(packet, frame)));
		}
	}

	/// <summary>
	/// A strip that claims a height far past the picture is clipped, not written past the frame: the
	/// decoder walks the claimed rows, and the frame drops every write below its last row.
	/// </summary>
	[Fact]
	public void ClipsAStripTallerThanThePicture() {
		byte[] packet = BuildFrame();
		packet[10 + 8] = 0xFF;
		packet[10 + 9] = 0xFF;

		var frame = new VideoFrame(Width, Height);
		Assert.Null(Record.Exception(() => CreateCodec().DecodeFrame(packet, frame)));
	}

	/// <summary>Only the 24-bit form is decoded; the 8-bit palettised one is refused at creation.</summary>
	[Fact]
	public void RefusesThePalettisedForm() {
		Assert.Null(CodecRegistry.Create(new AviVideoFormat(Width, Height, false, 8, CodecRegistry.Cinepak, []), VideoLimits.Default));
	}

	private static IVideoCodec CreateCodec() =>
		CodecRegistry.Create(new AviVideoFormat(Width, Height, false, 24, CodecRegistry.Cinepak, []), VideoLimits.Default)!;

	private static int Red(VideoFrame frame, int x, int y) => frame.Rgba[((y * frame.Width) + x) * 4];
}
