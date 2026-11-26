using System.Runtime.InteropServices;

namespace Herculan.Engine.Audio;

/// <summary>
/// One Red Book track as PCM — 44.1 kHz, 16-bit signed, stereo interleaved — filled progressively by
/// whichever <see cref="IMusicSource"/> is producing it, and read by <see cref="StreamedCdAudio"/>
/// while it fills.
///
/// <para><b>One writer, one reader, no lock.</b> The length is known before the first sample
/// arrives (from the disc's table of contents, or a file's header), so the whole buffer is allocated
/// up front and never moves. The producer writes past <see cref="FramesReady"/> and then publishes
/// the new count; the reader only ever touches frames below the count it read. That is what lets
/// music start as soon as the first read lands rather than after the whole track is in.</para>
/// </summary>
public sealed class MusicTrack {
	/// <summary>Red Book's sampling rate.</summary>
	public const int SampleRate = 44100;

	/// <summary>Red Book is always stereo.</summary>
	public const int Channels = 2;

	/// <summary>Audio frames — one stereo sample pair — per 2352-byte CD sector.</summary>
	public const int FramesPerSector = 588;

	private readonly short[] _pcm;
	private long _framesReady;
	private int _damagedSectors;
	private volatile string? _failure;

	/// <summary>Creates an empty track of known length, for a producer to fill.</summary>
	public MusicTrack(int number, long frameCount) {
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(frameCount);
		Number = number;
		FrameCount = frameCount;
		_pcm = new short[checked(frameCount * Channels)];
	}

	/// <summary>The track number on the disc.</summary>
	public int Number { get; }

	/// <summary>How long the track is, in stereo frames.</summary>
	public long FrameCount { get; }

	/// <summary>How long the track is.</summary>
	public TimeSpan Duration => TimeSpan.FromSeconds((double)FrameCount / SampleRate);

	/// <summary>How many frames from the start have arrived. Only ever grows.</summary>
	public long FramesReady => Volatile.Read(ref _framesReady);

	/// <summary>Whether the whole track has arrived.</summary>
	public bool IsComplete => FramesReady >= FrameCount;

	/// <summary>
	/// Sectors the drive would not give up after retries, which went in as silence. Non-zero keeps
	/// the track out of the rip cache, so a scratch heard once is not heard forever.
	/// </summary>
	public int DamagedSectors => Volatile.Read(ref _damagedSectors);

	/// <summary>
	/// Why the producer gave up before the end, or null. A failed track stays short of complete for
	/// good; <see cref="StreamedCdAudio"/> plays what arrived and then falls silent.
	/// </summary>
	public string? Failure => _failure;

	/// <summary>
	/// Copies frames from a position on, as many as
	/// have arrived and fit.
	/// </summary>
	/// <param name="frame">The first frame to copy.</param>
	/// <param name="destination">Receives the frames.</param>
	/// <returns>How many frames were copied — 0 when the producer has not yet reached
	/// that frame.</returns>
	public int Read(long frame, Span<short> destination) {
		long ready = FramesReady;
		if (frame < 0 || frame >= ready) {
			return 0;
		}

		int frames = (int)Math.Min(destination.Length / Channels, ready - frame);
		_pcm.AsSpan((int)(frame * Channels), frames * Channels).CopyTo(destination);
		return frames;
	}

	/// <summary>The PCM as raw little-endian bytes, for writing a completed track to disk.</summary>
	internal ReadOnlySpan<byte> CompletedBytes =>
		IsComplete ? MemoryMarshal.AsBytes(_pcm.AsSpan()) : throw new InvalidOperationException(
			$"track {Number} is not complete");

	/// <summary>
	/// Producer side: appends little-endian 16-bit stereo PCM at <see cref="FramesReady"/> and
	/// publishes it. Anything past the track's length is dropped.
	/// </summary>
	internal void Append(ReadOnlySpan<byte> pcm) {
		long ready = FramesReady;
		int frames = (int)Math.Min(pcm.Length / (Channels * sizeof(short)), FrameCount - ready);
		if (frames <= 0) {
			return;
		}

		MemoryMarshal.Cast<byte, short>(pcm[..(frames * Channels * sizeof(short))])
			.CopyTo(_pcm.AsSpan((int)(ready * Channels)));
		Volatile.Write(ref _framesReady, ready + frames);
	}

	/// <summary>Producer side: appends sectors of silence.</summary>
	/// <param name="sectors">The number of sectors.</param>
	internal void AppendDamaged(int sectors) {
		long ready = FramesReady;
		long frames = Math.Min((long)sectors * FramesPerSector, FrameCount - ready);
		if (frames <= 0) {
			return;
		}

		_pcm.AsSpan((int)(ready * Channels), (int)(frames * Channels)).Clear();
		Interlocked.Add(ref _damagedSectors, sectors);
		Volatile.Write(ref _framesReady, ready + frames);
	}

	/// <summary>Producer side: gives up, saying why.</summary>
	internal void Fail(string reason) => _failure = reason;
}
