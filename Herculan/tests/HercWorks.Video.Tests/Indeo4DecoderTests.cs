using HercWorks.Video.Avi;
using HercWorks.Video.Codecs;
using HercWorks.Video.Codecs.Indeo4;
using Xunit;

namespace HercWorks.Video.Tests;

/// <summary>
/// The Indeo 4 frame decoder's robustness against damaged streams. The bitstream is bit-packed and
/// tile-structured, so a hand-built frame would exercise little past the picture header; these run
/// over <c>ES2DROP3.AVI</c>, the corpus's one IV41 file, and pass vacuously without it.
/// </summary>
public sealed class Indeo4DecoderTests {
	private static readonly string MoviePath = Path.Combine("AVI", "ES2DROP3.AVI");

	/// <summary>The whole movie decodes without a malformed frame, and without a decoder fault.</summary>
	[Fact]
	public void DecodesTheRetailDropship() {
		string? path = RetailFiles.Find(MoviePath);
		if (path is null) {
			return;
		}

		MoviePlayback playback = MoviePlayback.Open(File.ReadAllBytes(path))!;
		Assert.Equal(391, playback.FrameCount);

		playback.Advance(playback.Duration);
		Assert.Null(playback.DecodeException);
		Assert.False(playback.HasFailed);
		Assert.Equal(390, playback.CurrentFrameIndex);
	}

	/// <summary>
	/// Prefixes of the first intra frame, and of the inter frame after it, decode or are rejected;
	/// none throws. The inter frame is decoded on top of a codec that has seen the intra one, so the
	/// truncation reaches motion compensation and the stores rather than stopping at "no reference".
	/// </summary>
	[Fact]
	public void SurvivesTruncatedRetailFrames() {
		if (Open() is not { } avi) {
			return;
		}

		byte[][] packets = CodedPackets(avi);
		byte[] intra = packets[0];
		byte[] inter = packets[1];

		foreach (int length in Prefixes(intra.Length)) {
			IVideoCodec codec = CreateCodec(avi);
			var frame = NewFrame(avi);
			Assert.Null(Record.Exception(() => codec.DecodeFrame(intra.AsSpan(0, length), frame)));
		}

		IVideoCodec primed = CreateCodec(avi);
		var primedFrame = NewFrame(avi);
		Assert.True(primed.DecodeFrame(intra, primedFrame));

		foreach (int length in Prefixes(inter.Length)) {
			Assert.Null(Record.Exception(() => primed.DecodeFrame(inter.AsSpan(0, length), primedFrame)));
		}
	}

	/// <summary>
	/// Random damage past the sync code — picture and band headers, codebook descriptors, tile
	/// sizes, macroblock codes, motion vectors, coefficients — never throws. Each trial decodes a run
	/// of frames on one codec, so damage to one frame's buffer is carried into the next one's
	/// prediction.
	/// </summary>
	[Fact]
	public void SurvivesCorruptedRetailFrames() {
		if (Open() is not { } avi) {
			return;
		}

		// The 18-bit sync code spans the first three bytes; damage there is refused at the door.
		const int FirstDamagedByte = 3;
		var random = new Random(1996);
		byte[][] packets = CodedPackets(avi);

		for (int trial = 0; trial < 50; trial++) {
			IVideoCodec codec = CreateCodec(avi);
			var frame = NewFrame(avi);

			for (int index = 0; index < 12; index++) {
				byte[] packet = (byte[])packets[index].Clone();
				int hits = 1 + random.Next(8);
				for (int i = 0; i < hits; i++) {
					packet[FirstDamagedByte + random.Next(packet.Length - FirstDamagedByte)] = (byte)random.Next(256);
				}

				Assert.Null(Record.Exception(() => codec.DecodeFrame(packet, frame)));
			}
		}
	}

	/// <summary>
	/// Single bit flips, which a bit-packed stream is more sensitive to than whole-byte damage: one
	/// flipped bit in a variable-length code shifts every field after it.
	/// </summary>
	[Fact]
	public void SurvivesBitFlipsInRetailFrames() {
		if (Open() is not { } avi) {
			return;
		}

		var random = new Random(41);
		byte[][] packets = CodedPackets(avi);
		byte[] intra = packets[0];
		byte[] inter = packets[1];

		for (int trial = 0; trial < 200; trial++) {
			IVideoCodec codec = CreateCodec(avi);
			var frame = NewFrame(avi);

			byte[] first = (byte[])intra.Clone();
			byte[] second = (byte[])inter.Clone();
			first[random.Next(first.Length)] ^= (byte)(1 << random.Next(8));
			second[random.Next(second.Length)] ^= (byte)(1 << random.Next(8));

			Assert.Null(Record.Exception(() => codec.DecodeFrame(first, frame)));
			Assert.Null(Record.Exception(() => codec.DecodeFrame(second, frame)));
		}
	}

	/// <summary>
	/// An inter frame with no intra frame before it has nothing to predict from, and is refused
	/// rather than decoded against an unallocated buffer.
	/// </summary>
	[Fact]
	public void RefusesAnInterFrameBeforeAnyIntraFrame() {
		if (Open() is not { } avi) {
			return;
		}

		IVideoCodec codec = CreateCodec(avi);
		Assert.False(codec.DecodeFrame(CodedPackets(avi)[1], NewFrame(avi)));
	}

	/// <summary>Dimensions the 4:1:0 chroma planes cannot tile are refused at creation.</summary>
	[Fact]
	public void RefusesDimensionsThatAreNotWholeChromaSamples() {
		Assert.Null(Indeo4Decoder.Create(Format(18, 16), VideoLimits.Default));
		Assert.Null(Indeo4Decoder.Create(Format(16, 14), VideoLimits.Default));
		Assert.Null(Indeo4Decoder.Create(Format(8, 8), VideoLimits.Default));
		Assert.NotNull(Indeo4Decoder.Create(Format(16, 16), VideoLimits.Default));
	}

	private static AviFile? Open() {
		string? path = RetailFiles.Find(MoviePath);
		return path is null ? null : AviFile.Open(File.ReadAllBytes(path));
	}

	/// <summary>
	/// The movie's non-empty packets, in order. The first is the intra frame; the five after it in
	/// the file are empty (dropped frames), so the second here is the first inter frame.
	/// </summary>
	private static byte[][] CodedPackets(AviFile avi) =>
		[.. avi.VideoPackets.Where(p => p.Length > 0).Select(p => avi.PacketData(p).ToArray())];

	private static IVideoCodec CreateCodec(AviFile avi) => CodecRegistry.Create(avi.VideoFormat!, avi.Limits)!;

	private static VideoFrame NewFrame(AviFile avi) => new(avi.VideoFormat!.Width, avi.VideoFormat.Height);

	private static AviVideoFormat Format(int width, int height) =>
		new(width, height, false, 24, CodecRegistry.Indeo4, []);

	/// <summary>
	/// Every length up to 64, where the picture header sits, then about 400 more spread over the
	/// rest of the packet, and the full length less one.
	/// </summary>
	private static IEnumerable<int> Prefixes(int length) {
		int dense = Math.Min(64, length);
		for (int i = 0; i < dense; i++) {
			yield return i;
		}

		int step = Math.Max(1, (length - dense) / 400);
		for (int i = dense; i < length; i += step) {
			yield return i;
		}

		if (length > 0) {
			yield return length - 1;
		}
	}
}
