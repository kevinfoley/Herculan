using HercWorks.Video.Avi;
using HercWorks.Video.Codecs;

namespace HercWorks.Video;

/// <summary>
/// Drives one movie forward in time: decides which frame should be showing, and decodes up to it.
///
/// <para>This owns no GL and no audio. It exists so the awkward part of playback — that these
/// codecs are interframe, so you cannot jump to frame *n* without decoding everything before it —
/// is solved once, in a class that can be tested without a window.</para>
///
/// <para>Timing is driven by a clock the caller advances, not by a wall clock read here, so a test
/// can step a whole movie in a loop and a host can drive it from the same delta it uses for
/// everything else.</para>
/// </summary>
public sealed class MoviePlayback {
	private MoviePlayback(AviFile file, IVideoCodec codec, VideoFrame frame, AviAudioTrack? audio) {
		_file = file;
		_codec = codec;
		Frame = frame;
		Audio = audio;
		FrameCount = file.VideoPackets.Count;
		FrameInterval = TimeSpan.FromSeconds(1.0 / Math.Max(1.0, file.FramesPerSecond));
	}

	private readonly AviFile _file;
	private readonly IVideoCodec _codec;
	private TimeSpan _elapsed;
	private int _decodedThrough = -1;

	/// <summary>The frame buffer, updated in place as playback advances.</summary>
	public VideoFrame Frame { get; }

	/// <summary>The whole audio track, or null when the movie has none this reader understood.</summary>
	public AviAudioTrack? Audio { get; }

	/// <summary>How many video frames the movie holds.</summary>
	public int FrameCount { get; }

	/// <summary>How long each frame is shown.</summary>
	public TimeSpan FrameInterval { get; }

	/// <summary>How far into the movie playback has reached.</summary>
	public TimeSpan Elapsed => _elapsed;

	/// <summary>How long the movie runs, by frame count and rate.</summary>
	public TimeSpan Duration => FrameInterval * FrameCount;

	/// <summary>The index of the frame currently decoded into <see cref="Frame"/>, or -1 before the first.</summary>
	public int CurrentFrameIndex => _decodedThrough;

	/// <summary>Whether playback has run past the last frame.</summary>
	public bool IsFinished => _decodedThrough >= FrameCount - 1 && _elapsed >= Duration;

	/// <summary>
	/// Set when a packet failed to decode. Playback stops advancing, and the last good frame stays
	/// on screen rather than being replaced by noise.
	/// </summary>
	public bool HasFailed { get; private set; }

	/// <summary>
	/// The exception a decoder threw, when that is why <see cref="HasFailed"/> is set; null when the
	/// decoder reported the packet malformed itself, or nothing has failed.
	/// </summary>
	public Exception? DecodeException { get; private set; }

	/// <summary>
	/// Opens a movie for playback, or returns null when the file will not parse or its codec is not
	/// one this assembly implements.
	/// </summary>
	public static MoviePlayback? Open(byte[] bytes, VideoLimits? limits = null) {
		AviFile? file = AviFile.Open(bytes, limits);
		if (file?.VideoFormat is null) {
			return null;
		}

		IVideoCodec? codec = CodecRegistry.Create(file.VideoFormat, file.Limits);
		if (codec is null) {
			return null;
		}

		var frame = new VideoFrame(file.VideoFormat.Width, file.VideoFormat.Height);
		return new MoviePlayback(file, codec, frame, AviAudioTrack.Decode(file));
	}

	/// <summary>
	/// Advances the clock and decodes however many frames that crossed.
	///
	/// <para>Returns true when <see cref="Frame"/> changed, so a caller can skip re-uploading a
	/// texture on the many ticks that fall inside one frame's interval.</para>
	///
	/// <para>Frames are decoded in sequence and never skipped, even when the caller is late enough
	/// that several are due at once: each one is the previous one plus a delta, so skipping would
	/// corrupt every frame after it. A host that falls far behind drops presentation, not decoding.</para>
	///
	/// <para>A decoder that throws is treated as one that returned false. Every decoder here is meant
	/// to reject bad input rather than throw, so an exception is a bounds bug in this assembly; the
	/// CLR has already stopped it doing harm, and catching it here keeps one damaged movie from taking
	/// the host down with it. The fuzz tests call the decoders directly, so they still see it.</para>
	/// </summary>
	public bool Advance(TimeSpan delta) {
		if (HasFailed || FrameCount == 0) {
			return false;
		}

		if (delta > TimeSpan.Zero) {
			_elapsed += delta;
		}

		int due = (int)(_elapsed.Ticks / FrameInterval.Ticks);
		if (due >= FrameCount) {
			due = FrameCount - 1;
		}

		if (due <= _decodedThrough) {
			return false;
		}

		for (int index = _decodedThrough + 1; index <= due; index++) {
			if (!TryDecode(index)) {
				HasFailed = true;
				_decodedThrough = index;
				Frame.Touch();
				return true;
			}
		}

		_decodedThrough = due;
		Frame.Touch();
		return true;
	}

	// The exceptions a missed bounds or arithmetic check raises in managed code. Anything else —
	// out of memory, a bug in the caller's own code — is not the packet's fault and propagates.
	private bool TryDecode(int index) {
		try {
			return _codec.DecodeFrame(_file.PacketData(_file.VideoPackets[index]), Frame);
		} catch (Exception ex) when (ex is IndexOutOfRangeException or ArgumentException
			or ArithmeticException or InvalidOperationException or InvalidDataException) {
			DecodeException = ex;
			return false;
		}
	}

	/// <summary>Rewinds to before the first frame. The frame buffer keeps its pixels until the next advance.</summary>
	public void Rewind() {
		_elapsed = TimeSpan.Zero;
		_decodedThrough = -1;
		HasFailed = false;
		DecodeException = null;
	}
}
