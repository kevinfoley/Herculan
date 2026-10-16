using Herculan.Engine.Audio;
using Herculan.Engine.Gl;
using HercWorks.Video;
using Silk.NET.OpenGL;

namespace Herculan.Engine.Video;

/// <summary>
/// Plays one <c>.AVI</c> cutscene: decodes its frames on the CPU, keeps a GL texture in step with
/// them, and plays its soundtrack through a stream of the engine's audio backend.
///
/// <para>Decoding lives in <c>HercWorks.Video</c>, which has no GL and no audio. This class is only
/// the seam between that and the engine — it holds the texture, the sound stream and the clock.</para>
///
/// <para><b>The soundtrack is the clock.</b> While it is being heard the picture shows the frame for the
/// sample being heard, so the two cannot drift apart. Without one — no soundtrack, no device, or the
/// soundtrack over before the picture — the clock runs on the update's delta, and it holds while a
/// streamed movie's frames have not been read yet. When the sound runs dry and the clock moves on
/// without it, the samples it passed are skipped, so the sound comes back in step. Retail plays its
/// movies through MCI and leaves keeping the two together to it; the soundtrack-led clock is this
/// engine's.</para>
/// </summary>
public sealed class MoviePlayer : IDisposable {
	/// <summary>
	/// The length of one block handed to the sound stream. The stream holds
	/// four, so a second of sound is queued ahead of what is heard.
	/// </summary>
	public static readonly TimeSpan AudioBlock = TimeSpan.FromSeconds(0.25);

	private MoviePlayer(MoviePlayback playback) {
		_playback = playback;
	}

	private readonly MoviePlayback _playback;
	private GpuTexture? _texture;
	private int _uploadedRevision = -1;
	private IAudioStream? _audio;
	private short[] _block = [];
	private long _queuedFrames;
	private bool _disposed;

	/// <summary>Frame width in pixels.</summary>
	public int Width => _playback.Frame.Width;

	/// <summary>Frame height in pixels.</summary>
	public int Height => _playback.Frame.Height;

	/// <inheritdoc cref="MoviePlayback.Duration"/>
	public TimeSpan? Duration => _playback.Duration;

	/// <summary>How far playback has reached.</summary>
	public TimeSpan Elapsed => _playback.Elapsed;

	/// <summary>Whether playback has run past the last frame.</summary>
	public bool IsFinished => _playback.IsFinished;

	/// <summary>
	/// Whether a packet failed to decode, or a streamed movie's read failed. The last good frame stays on
	/// screen; a caller that wants to bail out of the cutscene rather than hold on a still can watch this.
	/// </summary>
	public bool HasFailed => _playback.HasFailed;

	/// <inheritdoc cref="MoviePlayback.DecodeException"/>
	public Exception? DecodeException => _playback.DecodeException;

	/// <inheritdoc cref="MoviePlayback.ReadException"/>
	public Exception? ReadException => _playback.ReadException;

	/// <summary>
	/// Opens a movie held in memory, or returns null when it will not parse or uses a codec the decoder does
	/// not implement.
	/// </summary>
	public static MoviePlayer? Open(byte[] bytes) =>
		MoviePlayback.Open(bytes) is { } playback ? new MoviePlayer(playback) : null;

	/// <summary>
	/// Opens a movie to be read from <paramref name="stream"/> as it plays; see
	/// <see cref="MoviePlayback.Open(Stream, HercWorks.Video.VideoLimits?)"/>, which blocks while the header is read.
	/// Call <see cref="WaitUntilReady"/> off the main thread before playing it.
	/// </summary>
	/// <exception cref="IOException">The stream failed while the header was read.</exception>
	public static MoviePlayer? Open(Stream stream) =>
		MoviePlayback.Open(stream) is { } playback ? new MoviePlayer(playback) : null;

	/// <inheritdoc cref="MoviePlayback.WaitUntilReady"/>
	public void WaitUntilReady() => _playback.WaitUntilReady();

	/// <summary>
	/// Starts the soundtrack, if the movie has one and the backend can stream it, and queues its first
	/// second.
	///
	/// <para>Call this as playback begins, just before the first <see cref="Update"/>. A movie with no
	/// audio, or a backend that is the null one, simply plays silent — neither is treated as a failure,
	/// because several of the shipped cutscenes genuinely have no audio stream.</para>
	/// </summary>
	public void StartAudio(IAudioBackend backend, float gain = 1.0f) {
		ArgumentNullException.ThrowIfNull(backend);

		if (_audio != null || _playback.AudioFormat is not { } format || !backend.IsAvailable
			|| backend.OpenStream(format.SampleRate, format.Channels) is not { } stream) {
			return;
		}

		_audio = stream;
		_audio.SetGain(gain);
		int blockFrames = Math.Max(1, (int)(format.SampleRate * AudioBlock.TotalSeconds));
		_block = new short[blockFrames * format.Channels];
		FeedAudio();
	}

	/// <summary>
	/// Moves playback on — to the sample being heard, or by <paramref name="delta"/> without one — keeps
	/// the soundtrack fed, and re-uploads the texture if the frame changed.
	///
	/// <para><paramref name="gl"/> is passed per call rather than held, matching how the rest of the
	/// engine's renderers take it, and the texture is created on the first update so that opening a
	/// movie does not require a GL context.</para>
	/// </summary>
	public void Update(GL gl, TimeSpan delta) {
		ArgumentNullException.ThrowIfNull(gl);
		ObjectDisposedException.ThrowIf(_disposed, this);

		FeedAudio();
		long heard = _audio?.Position ?? -1;
		if (heard >= 0) {
			_playback.AdvanceTo(TimeSpan.FromSeconds(heard / (double)_playback.AudioFormat!.SampleRate));
		} else if (!_playback.Starved) {
			_playback.Advance(delta);
		} else {
			_playback.AdvanceTo(_playback.Elapsed);
		}

		VideoFrame frame = _playback.Frame;
		if (_texture is null) {
			_texture = new GpuTexture(gl, frame.Rgba, frame.Width, frame.Height);
			_uploadedRevision = frame.Revision;
			return;
		}

		if (frame.Revision != _uploadedRevision) {
			_texture.Update(frame.Rgba, frame.Width, frame.Height);
			_uploadedRevision = frame.Revision;
		}
	}

	/// <summary>
	/// The texture holding the current frame, or null before the first <see cref="Update"/>.
	///
	/// <para>Sampling is nearest-neighbour, which <see cref="GpuTexture"/> chooses by default and is
	/// the right call here for the same reason it is everywhere else: these are 240x180 and 288x180
	/// pictures that the original scaled up with a point-sampling blitter.</para>
	/// </summary>
	public GpuTexture? Texture => _texture;

	/// <summary>
	/// Rewinds a movie held in memory to before its first frame, and its soundtrack with it.
	/// </summary>
	/// <exception cref="InvalidOperationException">The movie is streamed, which cannot rewind.</exception>
	public void Restart() {
		_playback.Rewind();
		_audio?.Stop();
		_queuedFrames = 0;
	}

	/// <summary>Stops the soundtrack.</summary>
	public void Stop() {
		_audio?.Dispose();
		_audio = null;
	}

	/// <summary>Stops the soundtrack, the read of a streamed movie, and releases the texture.</summary>
	public void Dispose() {
		if (_disposed) {
			return;
		}

		_disposed = true;
		Stop();
		_playback.Dispose();
		_texture?.Dispose();
		_texture = null;
	}

	// Whole blocks while the stream has room, and whatever has been read when it has run dry. With no
	// stream the packets are thrown away, so they never fill the read-ahead the picture needs.
	private void FeedAudio() {
		if (_audio == null) {
			_playback.DropAudio();
			return;
		}

		var format = _playback.AudioFormat!;
		if (_audio.Position < 0) {
			long due = (long)(_playback.Elapsed.TotalSeconds * format.SampleRate);
			if (due > _queuedFrames) {
				_queuedFrames += _playback.SkipAudio(due - _queuedFrames);
			}
		}

		while (_audio.FreeBlocks > 0) {
			int samples = _playback.ReadAudio(_block);
			if (samples == 0) {
				return;
			}

			_audio.Queue(_block.AsSpan(0, samples), _queuedFrames);
			_queuedFrames += samples / format.Channels;
		}
	}
}
