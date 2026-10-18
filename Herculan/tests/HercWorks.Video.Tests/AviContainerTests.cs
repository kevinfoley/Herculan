using HercWorks.Video.Avi;
using HercWorks.Video.Riff;
using Xunit;

namespace HercWorks.Video.Tests;

/// <summary>
/// The RIFF and AVI parsing layer, including the cases a hostile or damaged file can present.
/// </summary>
public class AviContainerTests {
	/// <summary>
	/// The baseline: a well-formed file yields its declared geometry, frame rate and packets.
	/// Catches a regression that breaks parsing outright.
	/// </summary>
	[Fact]
	public void ParsesAWellFormedFile() {
		byte[] bytes = new SyntheticAvi { Width = 32, Height = 16 }
			.AddVideo(1, 2, 3, 4)
			.AddVideo(5, 6)
			.Build();

		AviFile? avi = AviFile.Open(bytes);

		Assert.NotNull(avi);
		Assert.Equal(32, avi.VideoFormat!.Width);
		Assert.Equal(16, avi.VideoFormat.Height);
		Assert.Equal(10.0, avi.FramesPerSecond, 3);
		Assert.Equal(2, avi.VideoPackets.Count);
		Assert.Equal(4, avi.VideoPackets[0].Length);
		Assert.Equal(new byte[] { 1, 2, 3, 4 }, avi.PacketData(avi.VideoPackets[0]).ToArray());
	}

	/// <summary>
	/// Video and audio packets are told apart by both the chunk suffix and the stream header, so a
	/// file with both streams does not mix them. Catches an <c>01wb</c> payload being handed to a
	/// video decoder.
	/// </summary>
	[Fact]
	public void SeparatesVideoAndAudioPackets() {
		byte[] bytes = new SyntheticAvi { WithAudio = true }
			.AddVideo(1, 2)
			.AddAudio(3, 4, 5, 6)
			.AddVideo(7, 8)
			.Build();

		AviFile? avi = AviFile.Open(bytes);

		Assert.NotNull(avi);
		Assert.Equal(2, avi.VideoPackets.Count);
		Assert.Single(avi.AudioPackets);
		Assert.Equal(4, avi.AudioPackets[0].Length);
	}

	/// <summary>
	/// A negative height means top-down rows and is not itself an error. Catches the sign being
	/// taken as a corrupt value, which would reject a legitimate file.
	/// </summary>
	[Fact]
	public void ReadsTopDownHeightAsPositiveWithAFlag() {
		byte[] bytes = new SyntheticAvi { Height = -16 }.AddVideo(1).Build();

		AviFile? avi = AviFile.Open(bytes);

		Assert.NotNull(avi);
		Assert.Equal(16, avi.VideoFormat!.Height);
		Assert.True(avi.VideoFormat.TopDown);
	}

	/// <summary>
	/// <c>int.MinValue</c> is the one height whose negation overflows, so it is rejected rather than
	/// negated. Catches a crash from <c>Math.Abs(int.MinValue)</c> on a four-byte field that can
	/// hold exactly that.
	/// </summary>
	[Fact]
	public void RejectsTheHeightThatCannotBeNegated() {
		byte[] bytes = new SyntheticAvi { Height = int.MinValue }.AddVideo(1).Build();

		Assert.Null(AviFile.Open(bytes));
	}

	/// <summary>
	/// A frame size past the limits is refused before anything is allocated for it. Catches a
	/// four-byte dimension field being turned into a multi-gigabyte buffer.
	/// </summary>
	[Theory]
	[InlineData(100000, 16)]
	[InlineData(16, 100000)]
	[InlineData(-4, 16)]
	[InlineData(0, 16)]
	public void RejectsFrameSizesOutsideTheLimits(int width, int height) {
		byte[] bytes = new SyntheticAvi { Width = width, Height = height }.AddVideo(1).Build();

		Assert.Null(AviFile.Open(bytes));
	}

	/// <summary>
	/// Two dimensions that each pass the per-axis cap can still multiply past the pixel cap, and the
	/// product is formed as a long so it cannot wrap. Catches an overflow that would present a huge
	/// frame as a small one.
	/// </summary>
	[Fact]
	public void RejectsAPixelCountThatPassesBothAxisLimits() {
		var limits = new VideoLimits { MaxDimension = 4096, MaxPixelsPerFrame = 1024 };
		byte[] bytes = new SyntheticAvi { Width = 4096, Height = 4096 }.AddVideo(1).Build();

		Assert.Null(AviFile.Open(bytes, limits));
	}

	/// <summary>
	/// A file that is not RIFF, or not AVI, is refused rather than parsed speculatively.
	/// </summary>
	[Fact]
	public void RejectsNonAviInput() {
		Assert.Null(AviFile.Open([]));
		Assert.Null(AviFile.Open("not a riff file at all"u8.ToArray()));

		byte[] wave = new SyntheticAvi().AddVideo(1).Build();
		wave[8] = (byte)'W';
		wave[9] = (byte)'A';
		wave[10] = (byte)'V';
		wave[11] = (byte)'E';
		Assert.Null(AviFile.Open(wave));
	}

	/// <summary>
	/// Truncation anywhere in the file must not throw. Every prefix of a valid file is fed through
	/// the parser; each one either parses or returns null. Catches an unguarded read past the end.
	/// </summary>
	[Fact]
	public void SurvivesTruncationAtEveryLength() {
		byte[] full = new SyntheticAvi { WithAudio = true }
			.AddVideo(1, 2, 3, 4)
			.AddAudio(5, 6, 7, 8)
			.Build();

		for (int length = 0; length < full.Length; length++) {
			byte[] cut = full[..length];
			Exception? thrown = Record.Exception(() => AviFile.Open(cut));
			Assert.Null(thrown);
		}
	}

	/// <summary>
	/// A chunk whose declared length runs past the file ends the walk instead of being followed.
	/// Catches a lying length being used as a read bound.
	/// </summary>
	[Fact]
	public void StopsAtAChunkThatClaimsMoreThanItHas() {
		byte[] bytes = new SyntheticAvi().AddVideo(1, 2, 3, 4).Build();

		// Find the video chunk and inflate its declared length far past the end of the file.
		int at = IndexOf(bytes, "00dc"u8.ToArray());
		Assert.True(at > 0);
		BitConverter.GetBytes(0x7FFFFFFF).CopyTo(bytes, at + 4);

		AviFile? avi = AviFile.Open(bytes);

		// The header still parsed; the bad packet was simply not collected.
		Assert.NotNull(avi);
		Assert.Empty(avi.VideoPackets);
	}

	/// <summary>
	/// A packet buried in lists nested past the depth limit is skipped rather than descended to,
	/// while one a couple of lists down is read as usual, and the loose packet beside either is kept.
	/// Catches the absence of a depth cap on the leaf walk.
	/// </summary>
	[Theory]
	[InlineData(2, 2)]
	[InlineData(200, 1)]
	public void StopsDescendingAtTheNestingLimit(int depth, int expectedPackets) {
		byte[] bytes = new SyntheticAvi()
			.AddVideo(1)
			.AddNestedVideo(depth, 9, 9)
			.Build();

		AviFile? avi = AviFile.Open(bytes);

		Assert.NotNull(avi);
		Assert.Equal(expectedPackets, avi.VideoPackets.Count);
	}

	/// <summary>
	/// A zero-length chunk cannot leave the cursor where it started, which would loop forever.
	/// Catches the missing forward-progress check in the chunk walk.
	/// </summary>
	[Fact]
	public void MakesProgressOverZeroLengthChunks() {
		byte[] bytes = new SyntheticAvi()
			.AddVideo()
			.AddVideo()
			.AddVideo(7)
			.Build();

		AviFile? avi = AviFile.Open(bytes);

		Assert.NotNull(avi);
		Assert.Equal(3, avi.VideoPackets.Count);
		Assert.Equal(0, avi.VideoPackets[0].Length);
		Assert.Equal(1, avi.VideoPackets[2].Length);
	}

	/// <summary>
	/// Four-character codes round-trip through the packing helper, and unprintable bytes render as
	/// a placeholder rather than as control characters in a diagnostic.
	/// </summary>
	[Fact]
	public void PacksAndRendersFourCharacterCodes() {
		uint id = RiffReader.FourCc('I', 'V', '3', '2');

		Assert.Equal(0x32335649u, id);
		Assert.Equal("IV32", RiffReader.FourCcText(id));
		Assert.Equal("????", RiffReader.FourCcText(1));
	}

	private static int IndexOf(byte[] haystack, byte[] needle) {
		for (int i = 0; i + needle.Length <= haystack.Length; i++) {
			bool match = true;
			for (int j = 0; j < needle.Length; j++) {
				if (haystack[i + j] != needle[j]) {
					match = false;
					break;
				}
			}

			if (match) {
				return i;
			}
		}

		return -1;
	}
}
