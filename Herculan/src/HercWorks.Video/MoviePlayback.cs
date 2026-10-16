using HercWorks.Video.Avi;
using HercWorks.Video.Codecs;

namespace HercWorks.Video;

/// <summary>
/// Drives one movie forward in time: decides which frame should be showing, decodes up to it, and hands
/// out the soundtrack as 16-bit samples.
///
/// <para>This owns no GL and no audio device. It exists so the awkward part of playback — that these
/// codecs are interframe, so you cannot jump to frame *n* without decoding everything before it —
/// is solved once, in a class that can be tested without a window.</para>
///
/// <para>Timing is driven by a clock the caller advances, not by a wall clock read here, so a test
/// can step a whole movie in a loop and a host can drive it from the same delta it uses for
/// everything else, or from where its soundtrack has reached.</para>
///
/// <para>A movie is either held whole (<see cref="Open(byte[], VideoLimits?)"/>) or read as it plays
/// (<see cref="Open(Stream, VideoLimits?)"/>). A streamed movie's frames can arrive later than the clock
/// wants them; playback then holds the last frame and says so (<see cref="Starved"/>).</para>
/// </summary>
public sealed class MoviePlayback : IDisposable {
	/// <summary>How much of a streamed movie <see cref="WaitUntilReady"/> waits to have read before it starts.</summary>
	public static readonly TimeSpan Preroll = TimeSpan.FromSeconds(0.5);

	private MoviePlayback(IMoviePacketSource source, IVideoCodec codec, AviFile header) {
		_source = source;
		_codec = codec;
		Frame = new VideoFrame(header.VideoFormat!.Width, header.VideoFormat.Height);
		AudioFormat = AviAudio.IsPlayable(header.AudioFormat) ? header.AudioFormat : null;
		FrameInterval = TimeSpan.FromSeconds(1.0 / Math.Max(1.0, header.FramesPerSecond));
		if (AudioFormat != null) {
			_carry = new short[AudioFormat.Channels];
		}
	}

	private readonly IMoviePacketSource _source;
	private readonly IVideoCodec _codec;
	private TimeSpan _elapsed;
	private int _decodedThrough = -1;

	// The packet being read out as samples, and how far; then the samples of a frame a read stopped inside.
	private short[]? _pending;
	private int _pendingAt;
	private readonly short[] _carry = [];
	private int _carried;

	/// <summary>The frame buffer, updated in place as playback advances.</summary>
	public VideoFrame Frame { get; }

	/// <summary>The soundtrack's format, or null when the movie has none this assembly can play.</summary>
	public AviAudioFormat? AudioFormat { get; }

	/// <summary>How many video frames the movie holds; null while a streamed movie has not been read to its end.</summary>
	public int? FrameCount => _source.VideoPacketCount;

	/// <summary>How long each frame is shown.</summary>
	public TimeSpan FrameInterval { get; }

	/// <summary>How far into the movie playback has reached.</summary>
	public TimeSpan Elapsed => _elapsed;

	/// <summary>How long the movie runs, by frame count and rate; null while <see cref="FrameCount"/> is.</summary>
	public TimeSpan? Duration => FrameCount is { } frames ? FrameInterval * frames : null;

	/// <summary>The index of the frame currently decoded into <see cref="Frame"/>, or -1 before the first.</summary>
	public int CurrentFrameIndex => _decodedThrough;

	/// <summary>Whether playback has run past the last frame.</summary>
	public bool IsFinished => _source.VideoEnded && _elapsed >= FrameInterval * (_decodedThrough + 1);

	/// <summary>
	/// Whether the last advance stopped at a frame a streamed movie has not read yet. The clock a caller
	/// drives should wait while it does, or the picture falls behind it.
	/// </summary>
	public bool Starved { get; private set; }

	/// <summary>
	/// Set when a packet failed to decode, or a streamed movie's read failed. Playback stops advancing, and
	/// the last good frame stays on screen rather than being replaced by noise.
	/// </summary>
	public bool HasFailed { get; private set; }

	/// <summary>
	/// The exception a decoder threw, when that is why <see cref="HasFailed"/> is set; null when the
	/// decoder reported the packet malformed itself, or nothing has failed.
	/// </summary>
	public Exception? DecodeException { get; private set; }

	/// <summary>Why a streamed movie's read stopped before its end, or null.</summary>
	public Exception? ReadException => _source.Failure;

	/// <summary>Whether <see cref="Rewind"/> works: a movie held whole can, a streamed one cannot.</summary>
	public bool CanRewind => _source.CanRewind;

	/// <summary>
	/// Opens a movie held in memory, or returns null when the file will not parse or its codec is not
	/// one this assembly implements.
	/// </summary>
	public static MoviePlayback? Open(byte[] bytes, VideoLimits? limits = null) {
		AviFile? file = AviFile.Open(bytes, limits);
		if (file?.VideoFormat is null || CodecRegistry.Create(file.VideoFormat, file.Limits) is not { } codec) {
			return null;
		}

		return new MoviePlayback(new FilePacketSource(file), codec, file);
	}

	/// <summary>
	/// Opens a movie to be read as it plays, taking <paramref name="stream"/> over, or returns null, with the
	/// stream disposed, when the file will not parse or its codec is not one this assembly implements. Blocks
	/// while the header is read; the packets are read on a thread of the movie's own from then on.
	/// </summary>
	/// <exception cref="IOException">The stream failed while the header was read; it has been disposed.</exception>
	public static MoviePlayback? Open(Stream stream, VideoLimits? limits = null) {
		if (AviStreamReader.Open(stream, limits) is not { } reader) {
			return null;
		}

		if (CodecRegistry.Create(reader.Header.VideoFormat!, reader.Header.Limits) is not { } codec) {
			reader.Dispose();
			return null;
		}

		return new MoviePlayback(new StreamedPacketSource(reader), codec, reader.Header);
	}

	/// <summary>
	/// Blocks until <see cref="Preroll"/>'s worth of frames has been read, the read-ahead is full, or the read
	/// has ended, so a streamed movie does not starve on its first frames. Returns at once for one held whole.
	/// </summary>
	public void WaitUntilReady() =>
		_source.WaitFor((int)Math.Ceiling(Preroll.Ticks / (double)FrameInterval.Ticks));

	/// <summary>Advances the clock by <paramref name="delta"/>; see <see cref="AdvanceTo"/>.</summary>
	public bool Advance(TimeSpan delta) => AdvanceTo(delta > TimeSpan.Zero ? _elapsed + delta : _elapsed);

	/// <summary>
	/// Moves the clock on to <paramref name="elapsed"/>, never back, and decodes however many frames that
	/// crossed.
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
	public bool AdvanceTo(TimeSpan elapsed) {
		if (HasFailed) {
			return false;
		}

		if (elapsed > _elapsed) {
			_elapsed = elapsed;
		}

		long due = _elapsed.Ticks / FrameInterval.Ticks;
		bool changed = false;
		Starved = false;
		while (_decodedThrough < due) {
			if (!_source.TryTakeVideo(out ReadOnlyMemory<byte> packet)) {
				if (!_source.VideoEnded) {
					Starved = true;
				} else if (_source.Failure != null) {
					HasFailed = true;
				}

				break;
			}

			_decodedThrough++;
			changed = true;
			if (!TryDecode(packet.Span)) {
				HasFailed = true;
				break;
			}
		}

		if (changed) {
			Frame.Touch();
		}

		return changed;
	}

	/// <summary>
	/// Reads the soundtrack's next samples into <paramref name="into"/>, channels interleaved, and answers how
	/// many: whole frames only, as many as are waiting, up to as many as fit. Zero when the movie has no
	/// soundtrack to play, nothing more has been read yet, or the soundtrack has ended
	/// (<see cref="AudioEnded"/>).
	/// </summary>
	public int ReadAudio(Span<short> into) {
		if (AudioFormat is not { } format) {
			return 0;
		}

		int channels = format.Channels;
		int wanted = into.Length - into.Length % channels;
		int written = Math.Min(_carried, wanted);
		_carry.AsSpan(0, written).CopyTo(into);
		_carried = 0;

		while (written < wanted) {
			if (_pending == null || _pendingAt >= _pending.Length) {
				if (!_source.TryTakeAudio(out ReadOnlyMemory<byte> packet)) {
					break;
				}

				_pending = AviAudio.Widen(packet.Span, format);
				_pendingAt = 0;
				continue;
			}

			int count = Math.Min(wanted - written, _pending.Length - _pendingAt);
			_pending.AsSpan(_pendingAt, count).CopyTo(into[written..]);
			_pendingAt += count;
			written += count;
		}

		// A frame the read stopped inside waits for the rest of its channels, so they never swap.
		_carried = written % channels;
		written -= _carried;
		into.Slice(written, _carried).CopyTo(_carry);
		return written;
	}

	/// <summary>Whether every sample of the soundtrack has been read out, or there is none.</summary>
	public bool AudioEnded =>
		AudioFormat == null || (_source.AudioEnded && (_pending == null || _pendingAt >= _pending.Length));

	/// <summary>Reads past up to <paramref name="frames"/> frames of the soundtrack unheard, and answers how many.</summary>
	public long SkipAudio(long frames) {
		if (AudioFormat is not { } format) {
			return 0;
		}

		Span<short> scratch = stackalloc short[2048];
		scratch = scratch[..(scratch.Length - scratch.Length % format.Channels)];
		long skipped = 0;
		while (skipped < frames) {
			int want = (int)Math.Min(scratch.Length / format.Channels, frames - skipped) * format.Channels;
			int read = ReadAudio(scratch[..want]);
			if (read == 0) {
				break;
			}

			skipped += read / format.Channels;
		}

		return skipped;
	}

	/// <summary>Throws away every audio packet read so far, for a host with nothing to play them on.</summary>
	public void DropAudio() {
		while (_source.TryTakeAudio(out _)) {
		}

		_pending = null;
		_carried = 0;
	}

	// The exceptions a missed bounds or arithmetic check raises in managed code. Anything else —
	// out of memory, a bug in the caller's own code — is not the packet's fault and propagates.
	private bool TryDecode(ReadOnlySpan<byte> packet) {
		try {
			return _codec.DecodeFrame(packet, Frame);
		} catch (Exception ex) when (ex is IndexOutOfRangeException or ArgumentException
			or ArithmeticException or InvalidOperationException or InvalidDataException) {
			DecodeException = ex;
			return false;
		}
	}

	/// <summary>
	/// Rewinds to before the first frame and the first sample. The frame buffer keeps its pixels until the
	/// next advance.
	/// </summary>
	/// <exception cref="InvalidOperationException">The movie is streamed (<see cref="CanRewind"/>).</exception>
	public void Rewind() {
		_source.Rewind();
		_elapsed = TimeSpan.Zero;
		_decodedThrough = -1;
		Starved = false;
		HasFailed = false;
		DecodeException = null;
		_pending = null;
		_carried = 0;
	}

	/// <summary>Stops a streamed movie's read; see <see cref="Open(Stream, VideoLimits?)"/>.</summary>
	public void Dispose() => _source.Dispose();
}
