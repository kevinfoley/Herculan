using HercWorks.Video.Avi;
using HercWorks.Video.Codecs;
using Xunit;

namespace HercWorks.Video.Tests;

/// <summary>
/// Frame buffers, the MS-RLE decoder, audio decoding and playback timing.
/// </summary>
public class VideoDecodeTests {
	/// <summary>
	/// A new frame is fully opaque, so a codec that only ever writes colour cannot leave a
	/// transparent pixel behind. Catches alpha being left at zero.
	/// </summary>
	[Fact]
	public void NewFramesAreOpaque() {
		var frame = new VideoFrame(4, 4);

		for (int i = 3; i < frame.Rgba.Length; i += 4) {
			Assert.Equal(0xFF, frame.Rgba[i]);
		}
	}

	/// <summary>
	/// Writes outside the frame are dropped rather than wrapping to another row or throwing. This is
	/// the single clip that every codec's run handling relies on, so it is checked on its own.
	/// </summary>
	[Fact]
	public void PixelWritesOutsideTheFrameAreDropped() {
		var frame = new VideoFrame(4, 4);

		frame.SetPixel(-1, 0, 1, 2, 3);
		frame.SetPixel(4, 0, 1, 2, 3);
		frame.SetPixel(0, -1, 1, 2, 3);
		frame.SetPixel(0, 4, 1, 2, 3);
		frame.SetPixel(int.MaxValue, int.MaxValue, 1, 2, 3);
		frame.SetPixel(int.MinValue, 0, 1, 2, 3);

		for (int i = 0; i < frame.Rgba.Length; i += 4) {
			Assert.Equal(0, frame.Rgba[i]);
			Assert.Equal(0, frame.Rgba[i + 1]);
			Assert.Equal(0, frame.Rgba[i + 2]);
		}
	}

	/// <summary>
	/// An encoded run fills the pixels it says it does, on the bottom row, because MS-RLE rows run
	/// bottom-up. Catches the row order being flipped, which renders a recognisable but inverted
	/// picture.
	/// </summary>
	[Fact]
	public void RleRunFillsTheBottomRowFirst() {
		VideoFrame frame = DecodeRle(16, 8, [4, 200, 0, 1]);

		// Stream row 0 is the bottom line of the picture.
		for (int x = 0; x < 4; x++) {
			Assert.Equal(200, Red(frame, x, 7));
		}

		Assert.Equal(0, Red(frame, 4, 7));
		Assert.Equal(0, Red(frame, 0, 6));
	}

	/// <summary>
	/// The end-of-row escape moves up a line and back to column zero. Catches rows being drawn on
	/// top of each other.
	/// </summary>
	[Fact]
	public void RleEndOfRowStartsTheNextLine() {
		VideoFrame frame = DecodeRle(16, 8, [2, 100, 0, 0, 3, 150, 0, 1]);

		Assert.Equal(100, Red(frame, 0, 7));
		Assert.Equal(100, Red(frame, 1, 7));
		Assert.Equal(150, Red(frame, 0, 6));
		Assert.Equal(150, Red(frame, 2, 6));
		Assert.Equal(0, Red(frame, 3, 6));
	}

	/// <summary>
	/// An absolute run copies literal indices and is padded to an even length. Catches the pad byte
	/// being consumed as data, which desynchronises everything after it.
	/// </summary>
	[Fact]
	public void RleAbsoluteRunCopiesLiteralsAndSkipsThePadByte() {
		// Three literals (odd, so one pad byte), then a run that must land on the next column.
		VideoFrame frame = DecodeRle(16, 8, [0, 3, 10, 20, 30, 0, 2, 40, 0, 1]);

		Assert.Equal(10, Red(frame, 0, 7));
		Assert.Equal(20, Red(frame, 1, 7));
		Assert.Equal(30, Red(frame, 2, 7));
		Assert.Equal(40, Red(frame, 3, 7));
		Assert.Equal(40, Red(frame, 4, 7));
	}

	/// <summary>
	/// The delta escape skips pixels without writing them, leaving whatever the previous frame put
	/// there. This is what makes the codec interframe, so it is checked across two packets.
	/// </summary>
	[Fact]
	public void RleDeltaLeavesSkippedPixelsAlone() {
		AviFile avi = OpenRle(16, 8);
		var codec = CodecRegistry.Create(avi.VideoFormat!, avi.Limits)!;
		var frame = new VideoFrame(16, 8);

		// First frame: fill the bottom row.
		Assert.True(codec.DecodeFrame(new byte[] { 8, 99, 0, 1 }, frame));
		Assert.Equal(99, Red(frame, 0, 7));

		// Second frame: skip two columns, then write. The skipped pixels keep their old value.
		Assert.True(codec.DecodeFrame(new byte[] { 0, 2, 2, 0, 2, 55, 0, 1 }, frame));
		Assert.Equal(99, Red(frame, 0, 7));
		Assert.Equal(99, Red(frame, 1, 7));
		Assert.Equal(55, Red(frame, 2, 7));
	}

	/// <summary>
	/// An empty packet means "nothing changed" and must not be treated as a damaged frame. Several
	/// retail thumbnails end with a run of these.
	/// </summary>
	[Fact]
	public void RleAcceptsAnEmptyPacket() {
		AviFile avi = OpenRle(16, 8);
		var codec = CodecRegistry.Create(avi.VideoFormat!, avi.Limits)!;

		Assert.True(codec.DecodeFrame([], new VideoFrame(16, 8)));
	}

	/// <summary>
	/// A truncated opcode stream stops rather than reading past the packet. Every prefix of a valid
	/// packet is decoded; none may throw.
	/// </summary>
	[Fact]
	public void RleSurvivesTruncatedOpcodes() {
		AviFile avi = OpenRle(16, 8);
		var codec = CodecRegistry.Create(avi.VideoFormat!, avi.Limits)!;
		byte[] full = [0, 3, 10, 20, 30, 0, 4, 40, 0, 2, 3, 1, 0, 1];

		for (int length = 0; length <= full.Length; length++) {
			var frame = new VideoFrame(16, 8);
			Exception? thrown = Record.Exception(() => codec.DecodeFrame(full[..length], frame));
			Assert.Null(thrown);
		}
	}

	/// <summary>
	/// A run far longer than the row is clipped, not written past the buffer. Catches a count byte
	/// being trusted as a bound.
	/// </summary>
	[Fact]
	public void RleClipsAnOverlongRun() {
		VideoFrame frame = DecodeRle(4, 4, [255, 77, 0, 1]);

		Assert.Equal(77, Red(frame, 0, 3));
		Assert.Equal(77, Red(frame, 3, 3));
		// Nothing wrapped onto the row above.
		Assert.Equal(0, Red(frame, 0, 2));
	}

	/// <summary>
	/// 8-bit audio is unsigned around 0x80, so silence decodes to zero rather than to a large
	/// offset. Catches the midpoint not being subtracted, which makes every track clip.
	/// </summary>
	[Fact]
	public void DecodesUnsigned8BitAudioAboutItsMidpoint() {
		byte[] bytes = new SyntheticAvi { WithAudio = true, AudioBits = 8, AudioChannels = 1 }
			.AddVideo(1)
			.AddAudio(0x80, 0x80, 0xFF, 0x00)
			.Build();

		AviAudioTrack? track = AviAudioTrack.Decode(AviFile.Open(bytes)!);

		Assert.NotNull(track);
		Assert.Equal(4, track.Samples.Length);
		Assert.Equal(0, track.Samples[0]);
		Assert.Equal(0, track.Samples[1]);
		Assert.Equal(0x7F00, track.Samples[2]);
		Assert.Equal(-0x8000, track.Samples[3]);
	}

	/// <summary>
	/// A stereo track folds to mono by averaging, because the engine's audio backend takes mono
	/// samples. Catches one channel being dropped instead of mixed.
	/// </summary>
	[Fact]
	public void FoldsStereoToMonoByAveraging() {
		byte[] bytes = new SyntheticAvi { WithAudio = true, AudioBits = 8, AudioChannels = 2 }
			.AddVideo(1)
			.AddAudio(0xFF, 0x80, 0x00, 0x80)
			.Build();

		AviAudioTrack track = AviAudioTrack.Decode(AviFile.Open(bytes)!)!;
		short[] mono = track.ToMono();

		Assert.Equal(2, track.Channels);
		Assert.Equal(2, mono.Length);
		Assert.Equal((0x7F00 + 0) / 2, mono[0]);
		Assert.Equal((-0x8000 + 0) / 2, mono[1]);
	}

	/// <summary>
	/// A stream that is not uncompressed PCM is refused rather than decoded as if it were.
	/// </summary>
	[Fact]
	public void RejectsNonPcmAudio() {
		byte[] bytes = new SyntheticAvi { WithAudio = true, AudioBits = 4 }
			.AddVideo(1)
			.AddAudio(1, 2, 3, 4)
			.Build();

		Assert.Null(AviAudioTrack.Decode(AviFile.Open(bytes)!));
	}

	/// <summary>
	/// Playback decodes every frame in order as the clock crosses each interval, and never skips
	/// one — these codecs are interframe, so a skipped frame corrupts all that follow.
	/// </summary>
	[Fact]
	public void PlaybackDecodesEveryFrameInOrder() {
		byte[] bytes = new SyntheticAvi { Width = 16, Height = 8 }
			.WithGreyPalette()
			.AddVideo(4, 10, 0, 1)
			.AddVideo(4, 20, 0, 1)
			.AddVideo(4, 30, 0, 1)
			.Build();

		MoviePlayback? playback = MoviePlayback.Open(bytes);
		Assert.NotNull(playback);
		Assert.Equal(3, playback.FrameCount);
		Assert.Equal(-1, playback.CurrentFrameIndex);

		// One interval is 100 ms at the synthetic file's 10 fps.
		Assert.True(playback.Advance(TimeSpan.FromMilliseconds(100)));
		Assert.Equal(1, playback.CurrentFrameIndex);

		// A tick inside the same interval changes nothing, so the texture need not be re-uploaded.
		Assert.False(playback.Advance(TimeSpan.FromMilliseconds(10)));

		// A long stall still walks through every intervening frame rather than jumping.
		Assert.True(playback.Advance(TimeSpan.FromMilliseconds(500)));
		Assert.Equal(2, playback.CurrentFrameIndex);
		Assert.Equal(30, Red(playback.Frame, 0, 7));
		Assert.True(playback.IsFinished);
	}

	/// <summary>
	/// Rewinding puts playback back before the first frame so a cutscene can be replayed.
	/// </summary>
	[Fact]
	public void RewindReturnsToTheStart() {
		byte[] bytes = new SyntheticAvi { Width = 16, Height = 8 }
			.WithGreyPalette()
			.AddVideo(4, 10, 0, 1)
			.Build();

		MoviePlayback playback = MoviePlayback.Open(bytes)!;
		playback.Advance(TimeSpan.FromSeconds(1));
		Assert.Equal(0, playback.CurrentFrameIndex);

		playback.Rewind();

		Assert.Equal(-1, playback.CurrentFrameIndex);
		Assert.Equal(TimeSpan.Zero, playback.Elapsed);
	}

	/// <summary>
	/// A stream whose compression is not implemented yields null from <see cref="MoviePlayback.Open"/>
	/// rather than a player that shows guessed pixels.
	/// </summary>
	[Fact]
	public void RefusesToOpenAnUnsupportedCodec() {
		// IV50: Indeo Video 5, which no retail file uses.
		byte[] bytes = new SyntheticAvi { Compression = 0x30355649, BitCount = 24 }
			.AddVideo(1, 2, 3, 4)
			.Build();

		Assert.Null(MoviePlayback.Open(bytes));
	}

	private static AviFile OpenRle(int width, int height) {
		byte[] bytes = new SyntheticAvi { Width = width, Height = height }
			.WithGreyPalette()
			.AddVideo(0, 1)
			.Build();

		return AviFile.Open(bytes)!;
	}

	private static VideoFrame DecodeRle(int width, int height, byte[] packet) {
		AviFile avi = OpenRle(width, height);
		var codec = CodecRegistry.Create(avi.VideoFormat!, avi.Limits)!;
		var frame = new VideoFrame(width, height);
		codec.DecodeFrame(packet, frame);
		return frame;
	}

	private static int Red(VideoFrame frame, int x, int y) => frame.Rgba[((y * frame.Width) + x) * 4];
}
