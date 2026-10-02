using Herculan.Engine.Audio;
using Herculan.Engine.Gl;
using HercWorks.Video;
using Silk.NET.OpenGL;

namespace Herculan.Engine.Video;

/// <summary>
/// Plays one <c>.AVI</c> cutscene: decodes its frames on the CPU, keeps a GL texture in step with
/// them, and starts its soundtrack through the engine's existing audio backend.
///
/// <para>Decoding lives in <c>HercWorks.Video</c>, which has no GL and no audio. This class is only
/// the seam between that and the engine — it holds the texture, the sound handle and the clock.</para>
///
/// <para>Sound is handed to <see cref="IAudioBackend"/> as one sample rather than streamed. The
/// backend's sample model is one buffer per sound, and the longest track in the retail corpus is
/// under a megabyte once widened to 16-bit, so streaming would be machinery for nothing. It does
/// mean a stereo track is folded to mono on the way in, because <see cref="WaveSample"/> is mono:
/// the backend pans at play time and has nowhere to put a second channel.</para>
///
/// <para>Video is the clock and audio is free-running. Once the sample is started nothing re-syncs
/// it, which is right for a cutscene of a few minutes on a machine that can decode faster than
/// real time, and is what the original did — it also had no mechanism to resynchronise a movie
/// mid-playback.</para>
/// </summary>
public sealed class MoviePlayer : IDisposable {
	private MoviePlayer(MoviePlayback playback) {
		_playback = playback;
	}

	private readonly MoviePlayback _playback;
	private GpuTexture? _texture;
	private int _uploadedRevision = -1;
	private int _soundPlay = -1;
	private bool _disposed;

	/// <summary>Frame width in pixels.</summary>
	public int Width => _playback.Frame.Width;

	/// <summary>Frame height in pixels.</summary>
	public int Height => _playback.Frame.Height;

	/// <summary>How long the movie runs.</summary>
	public TimeSpan Duration => _playback.Duration;

	/// <summary>How far playback has reached.</summary>
	public TimeSpan Elapsed => _playback.Elapsed;

	/// <summary>Whether playback has run past the last frame.</summary>
	public bool IsFinished => _playback.IsFinished;

	/// <summary>
	/// Whether a packet failed to decode. The last good frame stays on screen; a caller that wants
	/// to bail out of the cutscene rather than hold on a still can watch this.
	/// </summary>
	public bool HasFailed => _playback.HasFailed;

	/// <inheritdoc cref="MoviePlayback.DecodeException"/>
	public Exception? DecodeException => _playback.DecodeException;

	/// <summary>
	/// Opens a movie file, or returns null when it will not parse or uses a codec the decoder does
	/// not implement.
	///
	/// <para>Reading the file is the caller's business, so this takes bytes: cutscenes sit loose in
	/// <c>ES2/AVI/</c> today but nothing here should care whether they came from a directory or an
	/// archive.</para>
	/// </summary>
	public static MoviePlayer? Open(byte[] bytes) {
		MoviePlayback? playback = MoviePlayback.Open(bytes);
		return playback is null ? null : new MoviePlayer(playback);
	}

	/// <summary>
	/// Starts the soundtrack, if the movie has one and the backend is available.
	///
	/// <para>Call this as playback begins. A movie with no audio, or a backend that is the null one,
	/// simply plays silent — neither is treated as a failure, because several of the shipped
	/// cutscenes genuinely have no audio stream.</para>
	/// </summary>
	public void StartAudio(IAudioBackend backend, float gain = 1.0f) {
		ArgumentNullException.ThrowIfNull(backend);

		if (_soundPlay >= 0 || _playback.Audio is null || !backend.IsAvailable) {
			return;
		}

		short[] mono = _playback.Audio.ToMono();
		if (mono.Length == 0) {
			return;
		}

		int sample = backend.CreateSample(WaveSample.FromSamples(mono, _playback.Audio.SampleRate));
		_soundPlay = backend.Start(sample, gain, 0.0f, 1.0f, looping: false);
	}

	/// <summary>
	/// Advances playback by <paramref name="delta"/> and re-uploads the texture if the frame changed.
	///
	/// <para><paramref name="gl"/> is passed per call rather than held, matching how the rest of the
	/// engine's renderers take it, and the texture is created on the first update so that opening a
	/// movie does not require a GL context.</para>
	/// </summary>
	public void Update(GL gl, TimeSpan delta) {
		ArgumentNullException.ThrowIfNull(gl);
		ObjectDisposedException.ThrowIf(_disposed, this);

		_playback.Advance(delta);

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
	/// Rewinds to before the first frame.
	///
	/// <para>The soundtrack is not restarted with it — it is one buffer already handed to the
	/// backend, so a caller that wants audio on a second pass has to stop and start it.</para>
	/// </summary>
	public void Restart() => _playback.Rewind();

	/// <summary>Stops the soundtrack and releases the texture.</summary>
	public void Stop(IAudioBackend? backend = null) {
		if (_soundPlay >= 0 && backend is not null) {
			backend.Stop(_soundPlay);
		}

		_soundPlay = -1;
	}

	public void Dispose() {
		if (_disposed) {
			return;
		}

		_disposed = true;
		_texture?.Dispose();
		_texture = null;
	}
}
