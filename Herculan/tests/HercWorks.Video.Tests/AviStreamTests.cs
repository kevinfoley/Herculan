using HercWorks.Video.Avi;
using Xunit;

namespace HercWorks.Video.Tests;

/// <summary>
/// Movies read as they play: the stream reader against the in-memory one, and playback over a stream that
/// hands its bytes out slowly, as a CD drive does.
/// </summary>
public class AviStreamTests {
	private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(5);

	/// <summary>
	/// The stream reader finds the same packets as the in-memory reader, in the same order, through
	/// <c>rec </c> lists, loose packets and odd lengths, whether or not the stream can seek. Catches the two
	/// walks drifting apart.
	/// </summary>
	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	public void FindsTheSamePacketsAsTheInMemoryReader(bool seekable) {
		byte[] bytes = new SyntheticAvi { WithAudio = true }
			.AddAudio(1, 2, 3)
			.AddRecord([4, 5], [6])
			.AddVideo()
			.AddRecord([7, 8, 9], [10, 11])
			.Build();

		AssertSamePackets(bytes, seekable);
	}

	/// <summary>
	/// Damage is skipped as the in-memory reader skips it: a chunk claiming more than its list holds ends
	/// that list and the walk carries on after it, and a list nested past the depth limit is passed over
	/// whole. Catches the stream reader stopping dead, or following a lying length.
	/// </summary>
	[Fact]
	public void SkipsWhatTheInMemoryReaderSkips() {
		byte[] bytes = new SyntheticAvi { WithAudio = true }
			.AddRecord([1, 2], [3])
			.AddRecord([4, 5], [6])
			.AddVideo(7)
			.Build();

		// The first record's video chunk now claims far more than its list holds, which loses that
		// record's two packets and nothing after it.
		int at = IndexOf(bytes, "00dc"u8.ToArray());
		BitConverter.GetBytes(0x7FFFFFFF).CopyTo(bytes, at + 4);
		var (video, audio) = AssertSamePackets(bytes, seekable: true);
		Assert.Equal(new byte[][] { [4, 5], [7] }, video);
		Assert.Equal(new byte[][] { [6] }, audio);

		// A one-level limit spends its level on the movie list, so the records inside it go unread.
		var shallow = new VideoLimits { MaxRiffDepth = 1 };
		byte[] nested = new SyntheticAvi().AddRecord([1], [2]).AddVideo(3).Build();
		(video, _) = AssertSamePackets(nested, seekable: true, shallow);
		Assert.Equal(new byte[][] { [3] }, video);
	}

	/// <summary>
	/// Every prefix of a valid file either opens or is refused, and an open one reads to its cut without
	/// throwing. Catches an unguarded read past the end of a truncated stream.
	/// </summary>
	[Fact]
	public void SurvivesTruncationAtEveryLength() {
		byte[] full = new SyntheticAvi { WithAudio = true }
			.AddRecord([1, 2, 3, 4], [5, 6, 7, 8])
			.AddVideo(9)
			.Build();

		for (int length = 0; length < full.Length; length++) {
			Exception? thrown = Record.Exception(() => {
				using var reader = AviStreamReader.Open(new MemoryStream(full[..length]));
				while (reader != null && reader.TryReadNext(out _)) {
				}
			});
			Assert.Null(thrown);
		}
	}

	/// <summary>
	/// A streamed movie plays every frame in order, and knows its length once it has been read to the end.
	/// Catches the read-ahead losing or reordering a packet between threads.
	/// </summary>
	[Fact]
	public void PlaysAStreamedMovieInOrder() {
		using var playback = MoviePlayback.Open(new MemoryStream(ThreeFrames()))!;
		playback.WaitUntilReady();

		var seen = new List<int>();
		int step = 0;
		Until(() => {
			if (playback.AdvanceTo(TimeSpan.FromMilliseconds(100 * step++))) {
				seen.Add(Red(playback.Frame));
			}

			return playback.IsFinished;
		});

		Assert.Equal(new[] { 10, 20, 30 }, seen);
		Assert.Equal(3, playback.FrameCount);
		Assert.False(playback.HasFailed);
	}

	/// <summary>
	/// While the drive has not delivered the frame the clock wants, playback holds the last frame and says
	/// it is starved, then catches up once the bytes come. Catches a slow read being taken for the end of
	/// the movie.
	/// </summary>
	[Fact]
	public void HoldsItsFrameWhileTheReadIsBehind() {
		byte[] bytes = ThreeFrames();
		var stream = new GatedStream(bytes);
		stream.Allow(IndexOf(bytes, "00dc"u8.ToArray()) + 12);
		using var playback = MoviePlayback.Open(stream)!;

		Until(() => {
			playback.Advance(TimeSpan.FromSeconds(1));
			return playback.CurrentFrameIndex == 0;
		});
		Assert.True(playback.Starved);
		Assert.False(playback.IsFinished);

		stream.AllowAll();
		Until(() => {
			playback.Advance(TimeSpan.Zero);
			return playback.CurrentFrameIndex == 2 && !playback.Starved;
		});
		Assert.Equal(30, Red(playback.Frame));
	}

	/// <summary>
	/// A stereo frame split across two packets waits for its second channel when only the first packet has
	/// been read. Catches a starved read handing out half a frame.
	/// </summary>
	[Fact]
	public void KeepsAStereoFrameTogetherWhileTheReadIsBehind() {
		byte[] bytes = new SyntheticAvi { WithAudio = true, AudioBits = 8, AudioChannels = 2 }
			.AddAudio(0x90, 0xA0, 0xB0)
			.AddVideo(1)
			.AddAudio(0xC0)
			.Build();
		var stream = new GatedStream(bytes);
		stream.Allow(IndexOf(bytes, "00dc"u8.ToArray()));
		using var playback = MoviePlayback.Open(stream)!;
		var samples = new short[4];

		int read = 0;
		Until(() => (read = playback.ReadAudio(samples)) > 0);
		Assert.Equal(2, read);
		Assert.Equal(new short[] { 0x1000, 0x2000 }, samples[..2]);

		stream.AllowAll();
		Until(() => (read = playback.ReadAudio(samples)) > 0);
		Assert.Equal(2, read);
		Assert.Equal(new short[] { 0x3000, 0x4000 }, samples[..2]);
	}

	/// <summary>
	/// A read that fails part way ends playback as failed after the last frame it delivered, and says why.
	/// Catches a dead drive passing for a movie's natural end.
	/// </summary>
	[Fact]
	public void EndsAsFailedWhereTheReadFails() {
		byte[] bytes = ThreeFrames();
		var stream = new GatedStream(bytes) { Unlimited = true, FailAt = IndexOf(bytes, "00dc"u8.ToArray()) + 12 };
		using var playback = MoviePlayback.Open(stream)!;

		Until(() => {
			playback.Advance(TimeSpan.FromSeconds(1));
			return playback.HasFailed;
		});
		Assert.Equal(0, playback.CurrentFrameIndex);
		Assert.IsType<IOException>(playback.ReadException);
	}

	/// <summary>
	/// Disposing a streamed movie stops its read and closes its stream once the read under way returns.
	/// Catches the read-ahead thread outliving the movie and holding the file open.
	/// </summary>
	[Fact]
	public void DisposingStopsTheRead() {
		byte[] bytes = ThreeFrames();
		var stream = new GatedStream(bytes);
		stream.Allow(IndexOf(bytes, "00dc"u8.ToArray()));
		var playback = MoviePlayback.Open(stream)!;

		playback.Dispose();
		stream.AllowAll();

		Until(() => stream.IsDisposed);
	}

	private static byte[] ThreeFrames() => new SyntheticAvi { Width = 16, Height = 8 }
		.WithGreyPalette()
		.AddVideo(4, 10, 0, 1)
		.AddVideo(4, 20, 0, 1)
		.AddVideo(4, 30, 0, 1)
		.Build();

	/// <summary>Reads the file both ways, asserts they agree, and returns the stream reader's packets.</summary>
	private static (List<byte[]> Video, List<byte[]> Audio) AssertSamePackets(
		byte[] bytes, bool seekable, VideoLimits? limits = null) {
		AviFile file = AviFile.Open(bytes, limits)!;
		using var reader = AviStreamReader.Open(new GatedStream(bytes, seekable) { Unlimited = true }, limits)!;
		var video = new List<byte[]>();
		var audio = new List<byte[]>();
		while (reader.TryReadNext(out AviStreamPacket packet)) {
			(packet.IsVideo ? video : audio).Add(packet.Data);
		}

		Assert.Equal(file.VideoPackets.Select(p => file.PacketData(p).ToArray()), video);
		Assert.Equal(file.AudioPackets.Select(p => file.PacketData(p).ToArray()), audio);
		return (video, audio);
	}

	private static void Until(Func<bool> condition) {
		var started = DateTime.UtcNow;
		while (!condition()) {
			Assert.True(DateTime.UtcNow - started < Deadline, "The condition was not met in time.");
			Thread.Sleep(5);
		}
	}

	private static int Red(VideoFrame frame) => frame.Rgba[(7 * frame.Width) * 4];

	private static int IndexOf(byte[] haystack, byte[] needle) {
		for (int i = 0; i + needle.Length <= haystack.Length; i++) {
			if (haystack.AsSpan(i, needle.Length).SequenceEqual(needle)) {
				return i;
			}
		}

		return -1;
	}

	/// <summary>
	/// A stream that hands out its bytes only up to a limit the test moves, blocking a read past it, and
	/// can fail at a set offset — a CD drive that is slow, or that gives up.
	/// </summary>
	private sealed class GatedStream(byte[] data, bool seekable = true) : Stream {
		private readonly object _gate = new();
		private long _allowed;
		private long _position;

		public bool Unlimited { get; init; }

		public long FailAt { get; init; } = -1;

		public bool IsDisposed { get; private set; }

		public void Allow(long upTo) {
			lock (_gate) {
				_allowed = upTo;
				Monitor.PulseAll(_gate);
			}
		}

		public void AllowAll() => Allow(data.Length);

		public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

		public override int Read(Span<byte> buffer) {
			lock (_gate) {
				// A test that never opens the gate fails at the deadline rather than hanging the run.
				while (!Unlimited && _position >= _allowed && _position < data.Length && !IsDisposed) {
					if (!Monitor.Wait(_gate, Deadline)) {
						throw new IOException("The gate was never opened this far.");
					}
				}

				ObjectDisposedException.ThrowIf(IsDisposed, this);
				if (FailAt >= 0 && _position >= FailAt) {
					throw new IOException("The drive gave up.");
				}

				long end = Unlimited ? data.Length : Math.Min(_allowed, data.Length);
				if (FailAt >= 0) {
					end = Math.Min(end, FailAt);
				}

				int count = (int)Math.Max(0, Math.Min(buffer.Length, end - _position));
				data.AsSpan((int)_position, count).CopyTo(buffer);
				_position += count;
				return count;
			}
		}

		public override bool CanRead => true;

		public override bool CanSeek => seekable;

		public override bool CanWrite => false;

		public override long Length => seekable ? data.Length : throw new NotSupportedException();

		public override long Position {
			get => _position;
			set => Seek(value, SeekOrigin.Begin);
		}

		public override long Seek(long offset, SeekOrigin origin) {
			if (!seekable) {
				throw new NotSupportedException();
			}

			lock (_gate) {
				_position = origin switch {
					SeekOrigin.Begin => offset,
					SeekOrigin.Current => _position + offset,
					_ => data.Length + offset,
				};
				return _position;
			}
		}

		public override void Flush() {
		}

		public override void SetLength(long value) => throw new NotSupportedException();

		public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

		protected override void Dispose(bool disposing) {
			lock (_gate) {
				IsDisposed = true;
				Monitor.PulseAll(_gate);
			}

			base.Dispose(disposing);
		}
	}
}
