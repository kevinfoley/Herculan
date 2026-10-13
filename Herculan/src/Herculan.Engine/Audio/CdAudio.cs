namespace Herculan.Engine.Audio;

/// <summary>
/// Which <see cref="ICdAudio"/> the machine this is running on gets. <b>The one place the engine
/// decides that</b>, per docs/herculan/planning.md's "Target platform", which asks for OS-specific
/// paths to sit behind a seam from the start even while Windows is the only tested target.
///
/// <para>The first of these that works, in order:</para>
/// <list type="number">
/// <item>A directory of <c>TrackNN.wav</c> files the caller named — <see cref="WaveFileMusicSource"/>.</item>
/// <item>The audio tracks of the disc image the install names, when its disc is one —
/// <see cref="ImageMusicSource"/>.</item>
/// <item>The disc, read digitally — <see cref="CdRipMusicSource"/>, playing the tracks it has cached from
/// the cache and caching the rest of the disc in the background.</item>
/// <item>The disc, played by the drive through MCI — <see cref="MciCdAudio"/>, retail's own
/// transport, for a drive that refuses raw reads or a machine with no digital output device.</item>
/// <item>The rip cache of the one disc this machine has ripped before, with the disc absent.</item>
/// </list>
/// <para>and <see cref="NullCdAudio"/> otherwise, which runs the whole music layer above it — the
/// track choice, the enable flag, the saved position — silently. See docs/retail/simulation/audio.md's
/// "CD audio" for why the digital path is preferred to retail's.</para>
/// </summary>
public static class CdAudio {
	/// <summary>
	/// Opens the machine's music, or answers a <see cref="NullCdAudio"/> carrying the reasons there
	/// is none. Never throws.
	/// </summary>
	/// <param name="backend">
	/// The device the digital paths play through. With none — <see cref="NullAudioBackend"/> — only
	/// MCI can sound.
	/// </param>
	/// <param name="drive">
	/// Which drive to read, as a letter. Null takes the first CD drive holding audio tracks. Naming a
	/// drive is this engine's own; retail opens MCI's default device and nothing else.
	/// </param>
	/// <param name="musicDirectory">A directory of <c>TrackNN.wav</c> files to prefer to the disc.</param>
	/// <param name="cacheRoot">
	/// Where rips are cached; null takes <see cref="CdRipMusicSource.DefaultCacheRoot"/>.
	/// </param>
	/// <param name="discImage">The install's disc, when it is an image; its owner keeps it open while the music plays.</param>
	public static ICdAudio Open(IAudioBackend backend, string? drive = null,
			string? musicDirectory = null, string? cacheRoot = null, HercWorks.Disc.DiscImage? discImage = null) {
		cacheRoot ??= CdRipMusicSource.DefaultCacheRoot;
		var reasons = new List<string>();

		if (musicDirectory != null) {
			var files = new WaveFileMusicSource(musicDirectory);
			if (!files.IsAvailable) {
				reasons.Add(files.Status);
			} else if (Stream(backend, files, reasons) is { } fromFiles) {
				return fromFiles;
			}
		}

		if (discImage != null) {
			if (ImageMusicSource.TryCreate(discImage, out string imageFailure) is not { } fromImage) {
				reasons.Add(imageFailure);
			} else if (Stream(backend, fromImage, reasons) is { } streamed) {
				return streamed;
			}
		}

		if (CdRipMusicSource.TryCreate(drive, cacheRoot, out string ripFailure) is { } rip) {
			if (Stream(backend, rip, reasons) is { } ripped) {
				rip.StartCaching();
				return ripped;
			}
		} else {
			reasons.Add(ripFailure);
		}

		if (OperatingSystem.IsWindows()) {
			var mci = MciCdAudio.TryCreate(drive);
			if (mci.IsAvailable) {
				return mci;
			}

			reasons.Add(mci.Status);
		}

		if (CdRipMusicSource.SoleCachedDisc(cacheRoot) is { } cached
			&& Stream(backend, new WaveFileMusicSource(cached), reasons) is { } fromCache) {
			return fromCache;
		}

		return new NullCdAudio(string.Join("; ", reasons));
	}

	private static StreamedCdAudio? Stream(IAudioBackend backend, IMusicSource source,
			List<string> reasons) {
		if (backend.OpenStream(MusicTrack.SampleRate, MusicTrack.Channels) is { } stream) {
			return new StreamedCdAudio(source, stream);
		}

		reasons.Add($"{source.Status}, but no output device to play it on");
		source.Dispose();
		return null;
	}
}
