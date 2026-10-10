namespace Herculan.Engine.Audio;

/// <summary>
/// The <c>Music_*</c> layer played through this engine's own mixer: an <see cref="IMusicSource"/>
/// supplies the track's PCM and an <see cref="IAudioStream"/> plays it. <b>The transport is this
/// engine's</b>; retail asks the drive to play through MCI, which on current Windows never reports
/// the end of a play and so never loops — see docs/retail/simulation/audio.md's "CD audio". Everything the
/// original decides — which track, when, and where a suspend resumes — is
/// <see cref="SoundDirector"/>'s and is unchanged.
///
/// <para><b>The loop is gapless.</b> Reading wraps from the track's last frame to its first as it
/// fills the stream's queue, so the seam is two adjacent blocks with nothing between them.</para>
///
/// <para><b>Positions are TMSF words</b>, as MCI's are — track in the low byte, then minutes,
/// seconds and frames of the track — because <see cref="ICdAudio.GetPosition"/>'s value is saved by
/// the director and handed back to <see cref="ResumeAt"/>, and a word of the same shape keeps both
/// transports interchangeable under it. Resolution is one CD frame, 1/75 s.</para>
/// </summary>
public sealed class StreamedCdAudio : ICdAudio {
	/// <summary>
	/// Frames per queued block: about 0.37 s, so the stream's ring holds about 1.5 s — enough to ride
	/// out a slow frame, and small enough that the first block is ready almost as soon as the first
	/// read of a rip lands.
	/// </summary>
	public const int BlockFrames = 16384;

	private readonly IMusicSource _source;
	private readonly IAudioStream _stream;
	private readonly short[] _block = new short[BlockFrames * MusicTrack.Channels];
	private MusicTrack? _track;
	private long _readFrame;
	private long _lastHeard;
	private bool _disposed;

	/// <summary>Plays <paramref name="source"/>'s tracks on <paramref name="stream"/>. Owns both.</summary>
	public StreamedCdAudio(IMusicSource source, IAudioStream stream) {
		_source = source;
		_stream = stream;
	}

	/// <inheritdoc />
	public bool IsAvailable => _source.IsAvailable;

	/// <inheritdoc />
	public bool IsIdle => _track == null;

	/// <inheritdoc />
	public string Status => $"{_source.Status}, played through OpenAL";

	/// <summary>The track being played, or null when idle.</summary>
	public MusicTrack? Track => _track;

	/// <inheritdoc />
	public bool PlayTrack(int track) => Begin(track, 0);

	/// <inheritdoc />
	public bool ResumeAt(int packedTmsf) {
		if (packedTmsf == 0) {
			return false;
		}

		int track = packedTmsf & 0xff;
		long sector = ((packedTmsf >> 8) & 0xff) * 60L * FramesPerSecond
			+ ((packedTmsf >> 16) & 0xff) * (long)FramesPerSecond
			+ ((packedTmsf >> 24) & 0xff);
		return Begin(track, sector * MusicTrack.FramesPerSector);
	}

	/// <inheritdoc />
	public int GetPosition() {
		if (_track is not { } track) {
			return 0;
		}

		long heard = _stream.Position;
		if (heard >= 0) {
			_lastHeard = heard % track.FrameCount;
		}

		long sector = _lastHeard / MusicTrack.FramesPerSector;
		return MakeTmsf(track.Number, (int)(sector / FramesPerSecond / 60),
			(int)(sector / FramesPerSecond % 60), (int)(sector % FramesPerSecond));
	}

	/// <inheritdoc />
	public void Stop() {
		_stream.Stop();
		_track = null;
	}

	/// <summary>
	/// Tops the stream's queue up from the track, wrapping to its start at its end. A block is queued
	/// only once it is whole, unless it is the track's last; a rip that has not reached it yet is
	/// simply waited for.
	/// </summary>
	public void Update() {
		if (_track is not { } track || _disposed) {
			return;
		}

		while (_stream.FreeBlocks > 0) {
			long wanted = Math.Min(BlockFrames, track.FrameCount - _readFrame);
			if (track.FramesReady - _readFrame < wanted) {
				return;
			}

			int frames = track.Read(_readFrame, _block.AsSpan(0, (int)wanted * MusicTrack.Channels));
			_stream.Queue(_block.AsSpan(0, frames * MusicTrack.Channels), _readFrame);

			_readFrame += frames;
			if (_readFrame >= track.FrameCount) {
				_readFrame = 0;
			}
		}
	}

	private bool Begin(int track, long frame) {
		if (track <= 0 || _disposed) {
			return false;
		}

		_stream.Stop();
		_track = _source.Open(track);
		if (_track == null) {
			return false;
		}

		_readFrame = frame < _track.FrameCount ? Math.Max(frame, 0) : 0;
		_lastHeard = _readFrame;
		Update();
		return true;
	}

	/// <summary>Red Book frames in a second, which is what the F of a TMSF word counts.</summary>
	public const int FramesPerSecond = 75;

	private static int MakeTmsf(int track, int minute, int second, int frame) =>
		(track & 0xff) | ((minute & 0xff) << 8) | ((second & 0xff) << 16) | ((frame & 0xff) << 24);

	/// <inheritdoc />
	public void Dispose() {
		if (_disposed) {
			return;
		}

		_disposed = true;
		_stream.Dispose();
		_source.Dispose();
	}
}
