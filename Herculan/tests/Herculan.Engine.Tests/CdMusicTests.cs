using Herculan.Engine.Audio;
using Xunit;

namespace Herculan.Engine.Tests;

/// <summary>
/// The digital CD music transport: the table-of-contents parse, the WAV round trip the rip cache
/// relies on, and <see cref="StreamedCdAudio"/>'s queueing, looping and TMSF positions — all
/// without a drive or an audio device.
/// </summary>
public class CdMusicTests {
	/// <summary>
	/// The retail disc's own table of contents, as <c>IOCTL_CDROM_READ_TOC</c> returned it: a data
	/// track, six audio tracks and the lead-out, addresses in MSF.
	/// </summary>
	private static byte[] RetailToc() {
		(int Number, bool Data, long Lba)[] entries = {
			(1, true, 0), (2, false, 172283), (3, false, 183148), (4, false, 195543),
			(5, false, 207936), (6, false, 221001), (7, false, 234006), (0xaa, false, 245025),
		};

		var toc = new byte[804];
		toc[2] = 1;
		toc[3] = 7;
		for (int i = 0; i < entries.Length; i++) {
			int at = 4 + i * 8;
			long address = entries[i].Lba + 150;
			toc[at + 1] = (byte)(0x10 | (entries[i].Data ? 0x4 : 0));
			toc[at + 2] = (byte)entries[i].Number;
			toc[at + 5] = (byte)(address / 75 / 60);
			toc[at + 6] = (byte)(address / 75 % 60);
			toc[at + 7] = (byte)(address % 75);
		}

		return toc;
	}

	/// <summary>
	/// Six audio tracks, 2 to 7, each running to the next one's start; the data track is left out.
	/// Track 7 is the one <c>% 5 + 2</c> never reaches.
	/// </summary>
	[Fact]
	public void TheRetailTocHasSixAudioTracks() {
		var tracks = CdRipMusicSource.ParseToc(RetailToc(), out string discId);

		Assert.Equal(new[] { 2, 3, 4, 5, 6, 7 }, tracks.Keys.Order());
		Assert.Equal((172283L, 10865L), tracks[2]);
		Assert.Equal((234006L, 11019L), tracks[7]);
		Assert.Equal(16, discId.Length);
	}

	/// <summary>A track written by the cache reads back identical through the file source.</summary>
	[Fact]
	public void ACachedTrackReadsBackAsWritten() {
		string directory = Directory.CreateTempSubdirectory("herculan-music").FullName;
		try {
			var written = Filled(3, 100_000);
			WaveFileMusicSource.Write(WaveFileMusicSource.PathFor(directory, 3), written);

			using var source = new WaveFileMusicSource(directory);
			Assert.True(source.IsAvailable);

			var read = source.Open(3);
			Assert.NotNull(read);
			Assert.True(source.WaitForCurrent(TimeSpan.FromSeconds(10)));
			Assert.True(read!.IsComplete);
			Assert.Same(read, source.Open(3));

			var expected = new short[written.FrameCount * 2];
			var actual = new short[read.FrameCount * 2];
			written.Read(0, expected);
			read.Read(0, actual);
			Assert.Equal(expected, actual);

			Assert.Null(source.Open(4));
		} finally {
			Directory.Delete(directory, recursive: true);
		}
	}

	/// <summary>
	/// A track counts as cached only when its file holds the whole track: one cut short, or only an
	/// interrupted write's <c>.partial</c>, is ripped again.
	/// </summary>
	[Fact]
	public void OnlyAWholeTrackCountsAsCached() {
		string directory = Directory.CreateTempSubdirectory("herculan-music").FullName;
		try {
			var tracks = new Dictionary<int, (long, long)> { [2] = (0, 10), [3] = (10, 10), [4] = (20, 10) };
			WaveFileMusicSource.Write(WaveFileMusicSource.PathFor(directory, 2), Filled(2, 10 * MusicTrack.FramesPerSector));
			WaveFileMusicSource.Write(WaveFileMusicSource.PathFor(directory, 3), Filled(3, 9 * MusicTrack.FramesPerSector));
			File.WriteAllBytes(WaveFileMusicSource.PathFor(directory, 4) + ".partial", new byte[100]);

			Assert.Equal(new[] { 3, 4 }, CdRipMusicSource.UncachedTracks(tracks, directory));
			Assert.Equal(new[] { 2, 3, 4 }, CdRipMusicSource.UncachedTracks(tracks, Path.Combine(directory, "other-disc")));
		} finally {
			Directory.Delete(directory, recursive: true);
		}
	}

	/// <summary>
	/// The queue fills in whole blocks and wraps from the last frame to the first with nothing
	/// between, which is the loop.
	/// </summary>
	[Fact]
	public void TheStreamWrapsToTheTrackStart() {
		long frames = StreamedCdAudio.BlockFrames * 2 + 1000;
		var stream = new FakeStream(capacity: 4);
		using var cd = new StreamedCdAudio(new FakeSource(Filled(2, frames)), stream);

		Assert.True(cd.PlayTrack(2));

		Assert.Equal(new long[] { 0, StreamedCdAudio.BlockFrames, StreamedCdAudio.BlockFrames * 2, 0 },
			stream.Tags);
		Assert.Equal(1000, stream.Frames[2]);
	}

	/// <summary>A rip still behind the read head queues nothing until a whole block has arrived.</summary>
	[Fact]
	public void AStarvedStreamWaitsForAWholeBlock() {
		var track = new MusicTrack(2, StreamedCdAudio.BlockFrames * 4L);
		var stream = new FakeStream(capacity: 4);
		using var cd = new StreamedCdAudio(new FakeSource(track), stream);

		track.Append(new byte[(StreamedCdAudio.BlockFrames - 1) * 4]);
		cd.PlayTrack(2);
		Assert.Empty(stream.Tags);

		track.Append(new byte[4]);
		cd.Update();
		Assert.Single(stream.Tags);
	}

	/// <summary>
	/// The saved position is a TMSF word of the frame being heard, and resuming from it restarts the
	/// queue at that CD frame.
	/// </summary>
	[Fact]
	public void APositionResumesWhereItWasHeard() {
		var stream = new FakeStream(capacity: 4);
		using var cd = new StreamedCdAudio(new FakeSource(Filled(5, 44100L * 150)), stream);
		cd.PlayTrack(5);

		// 1:02 and 10 CD frames in.
		long sector = (62 * 75) + 10;
		stream.Heard = sector * MusicTrack.FramesPerSector + 17;
		int tmsf = cd.GetPosition();
		Assert.Equal(5 | (1 << 8) | (2 << 16) | (10 << 24), tmsf);

		cd.Stop();
		Assert.True(cd.IsIdle);
		Assert.True(cd.ResumeAt(tmsf));
		Assert.Equal(sector * MusicTrack.FramesPerSector, stream.Tags[0]);
	}

	private static MusicTrack Filled(int number, long frames) {
		var track = new MusicTrack(number, frames);
		var bytes = new byte[frames * 4];
		new Random(number).NextBytes(bytes);
		track.Append(bytes);
		return track;
	}

	private sealed class FakeSource : IMusicSource {
		private readonly MusicTrack _track;

		public FakeSource(MusicTrack track) => _track = track;

		public bool IsAvailable => true;

		public string Status => "fake";

		public MusicTrack? Open(int track) => track == _track.Number ? _track : null;

		public void Dispose() { }
	}

	private sealed class FakeStream : IAudioStream {
		private readonly int _capacity;

		public FakeStream(int capacity) => _capacity = capacity;

		public List<long> Tags { get; } = new();

		public List<int> Frames { get; } = new();

		public long Heard { get; set; } = -1;

		public int FreeBlocks => _capacity - Tags.Count;

		public long Position => Heard;

		public void Queue(ReadOnlySpan<short> pcm, long startFrame) {
			Tags.Add(startFrame);
			Frames.Add(pcm.Length / 2);
		}

		public void Stop() {
			Tags.Clear();
			Frames.Clear();
		}

		public void SetGain(float gain) { }

		public void Dispose() { }
	}
}
