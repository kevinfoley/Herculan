namespace Herculan.Engine.Audio.Opl;

/// <summary>
/// An <see cref="IOplChip"/>'s output played through a stream of the host's mixer (<see cref="IAudioBackend.OpenStream(int, int, int)"/>),
/// opened stereo at the chip's own rate, which the mixer resamples. This class is the engine's own and rests on no RE
/// (docs/herculan/plan-es1-sound.md, module D parts 6 and 9).
///
/// <para>The caller sets the pace: each <see cref="Render"/> advances the chip by the frames asked for, so a driver that
/// makes a timer tick's register writes and then renders that tick's share of frames keeps its writes at the tick's time
/// in the output. The frames go to the stream a block at a time, in a ring of <see cref="Buffers"/> blocks.</para>
///
/// <para>The caller renders a host frame's ticks at once, and the mixer takes the blocks at its own pace, so the stream
/// keeps a lag, the frames queued ahead of the one heard, to cover the gap between host frames. The source starts once
/// the lag's target is queued, and after it runs dry it waits for the target again before it plays on. The target starts
/// at <see cref="MinimumLag"/>, grows by <see cref="LagStep"/> each time the source runs dry, up to
/// <see cref="MaximumLag"/>, and eases back by <see cref="LagEaseStep"/> after each <see cref="LagEaseAfter"/> with no dry
/// spell. While it plays, the source's pitch, within <see cref="MaximumPitchShift"/> of 1, holds the lag at its target
/// (<see cref="Steer"/>), where a block dropped or a gap would be heard.</para>
///
/// <para>A block the ring has no room for is dropped. With the ring past <see cref="MaximumLag"/>, that happens only when
/// the device stops taking blocks or a host frame lasts longer than the ring.</para>
///
/// <para>With no stream, under the host's <c>--no-sound</c> or with no audio device, the writes still reach the chip and
/// <see cref="Render"/> generates nothing.</para>
/// </summary>
public sealed class OplStream : IDisposable {
	/// <summary>The blocks of the stream's ring.</summary>
	public const int Buffers = 24;

	/// <summary>The lag's target at the start and its floor.</summary>
	public static readonly TimeSpan MinimumLag = TimeSpan.FromMilliseconds(40);

	/// <summary>What the target grows by each time the source runs dry.</summary>
	public static readonly TimeSpan LagStep = TimeSpan.FromMilliseconds(25);

	/// <summary>The target's ceiling.</summary>
	public static readonly TimeSpan MaximumLag = TimeSpan.FromMilliseconds(150);

	/// <summary>The stretch of playback with no dry spell after which the target eases back.</summary>
	public static readonly TimeSpan LagEaseAfter = TimeSpan.FromSeconds(10);

	/// <summary>What the target eases back by.</summary>
	public static readonly TimeSpan LagEaseStep = TimeSpan.FromMilliseconds(5);

	/// <summary>The most the pitch moves from 1 to steer the lag.</summary>
	public const float MaximumPitchShift = 0.005f;

	/// <summary>The lag's error, past its target, at which the pitch moves by all of <see cref="MaximumPitchShift"/>.</summary>
	private static readonly TimeSpan FullShiftError = TimeSpan.FromMilliseconds(20);

	/// <summary>The share of each block's measured lag taken into the smoothed lag.</summary>
	private const double LagSmoothing = 0.02;

	private readonly IAudioStream? _stream;
	private readonly int _blockFrames;
	private readonly Queue<(short[] Pcm, long StartFrame)> _pending = new();
	private readonly Stack<short[]> _spare = new();
	private short[] _block;
	private int _filled;
	private long _frame;
	private bool _playing;
	private long _targetFrames;
	private long _framesSinceDry;
	private double _smoothedLag;
	private float _pitch = 1f;
	private bool _disposed;

	/// <param name="backend">The host's mixer.</param>
	/// <param name="chip">The chip whose output plays.</param>
	/// <param name="blockFrames">The frames of one block handed to the stream.</param>
	public OplStream(IAudioBackend backend, IOplChip chip, int blockFrames) {
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(blockFrames);
		Chip = chip;
		_blockFrames = blockFrames;
		_block = new short[blockFrames * 2];
		_targetFrames = Frames(MinimumLag);
		_stream = backend.IsAvailable ? backend.OpenStream(chip.SampleRate, 2, Buffers) : null;
		_stream?.SetGain(1f);
	}

	/// <summary>The chip, for the driver's register writes.</summary>
	public IOplChip Chip { get; }

	/// <summary>Whether the output reaches a stream; false when <see cref="Render"/> generates nothing.</summary>
	public bool IsAudible => _stream != null && !_disposed;

	/// <summary>The frame the next frame <see cref="Render"/> generates is, counted from the stream's first.</summary>
	public long NextFrame => _frame + _filled;

	/// <summary>
	/// The frame being heard, in <see cref="NextFrame"/>'s numbering; −1 while the source waits to start or has run dry, and
	/// <see cref="long.MaxValue"/> once the output is not audible, so that whatever waits for a frame to be heard goes on.
	/// </summary>
	public long HeardFrame => IsAudible ? _stream!.Position : long.MaxValue;

	/// <summary>The lag's current target.</summary>
	public TimeSpan TargetLag => TimeSpan.FromSeconds((double)_targetFrames / Chip.SampleRate);

	/// <summary>
	/// Advances the chip by <paramref name="frames"/> frames and hands each block completed to the stream, or keeps it
	/// until the source starts.
	/// </summary>
	public void Render(int frames) {
		if (!IsAudible) {
			return;
		}

		while (frames > 0) {
			int count = Math.Min(frames, _blockFrames - _filled);
			Chip.Generate(_block.AsSpan(_filled * 2, count * 2));
			_filled += count;
			frames -= count;
			if (_filled == _blockFrames) {
				Complete(_block);
				_block = _spare.Count > 0 ? _spare.Pop() : new short[_blockFrames * 2];
				_filled = 0;
			}
		}
	}

	/// <summary>A completed block: queued while the source plays, otherwise kept until the target is reached.</summary>
	private void Complete(short[] block) {
		var stream = _stream!;
		long start = _frame;
		_frame += _blockFrames;
		if (_playing && stream.Position < 0) {
			_playing = false;
			_targetFrames = Math.Min(_targetFrames + Frames(LagStep), Frames(MaximumLag));
			_framesSinceDry = 0;
		}

		if (_playing) {
			Queue(block, start);
			Steer();
			return;
		}

		_pending.Enqueue((block, start));
		if (_pending.Count > Buffers) {
			_spare.Push(_pending.Dequeue().Pcm);
		}

		if ((long)_pending.Count * _blockFrames < _targetFrames) {
			return;
		}

		while (_pending.Count > 0) {
			var (pcm, startFrame) = _pending.Dequeue();
			Queue(pcm, startFrame);
		}

		_playing = true;
		_smoothedLag = _targetFrames;
		SetPitch(1f);
	}

	/// <summary>The block handed to the stream, or dropped when the ring has no room for it.</summary>
	private void Queue(short[] pcm, long startFrame) {
		if (_stream!.FreeBlocks > 0) {
			_stream.Queue(pcm, startFrame);
		}

		_spare.Push(pcm);
	}

	/// <summary>
	/// The lag taken into the smoothed lag, and the pitch set from its error against the target: 1 plus
	/// <see cref="MaximumPitchShift"/> times the error over <see cref="FullShiftError"/>, held to ±<see cref="MaximumPitchShift"/>,
	/// faster while the lag is long. A stretch with no dry spell eases the target back.
	/// </summary>
	private void Steer() {
		long heard = _stream!.Position;
		if (heard < 0) {
			return;
		}

		_smoothedLag += (_frame - heard - _smoothedLag) * LagSmoothing;
		double error = (_smoothedLag - _targetFrames) / Frames(FullShiftError);
		SetPitch(1f + (float)Math.Clamp(error * MaximumPitchShift, -MaximumPitchShift, MaximumPitchShift));		_framesSinceDry += _blockFrames;
		if (_framesSinceDry >= Frames(LagEaseAfter)) {
			_framesSinceDry = 0;
			_targetFrames = Math.Max(_targetFrames - Frames(LagEaseStep), Frames(MinimumLag));
		}
	}

	private void SetPitch(float pitch) {
		if (Math.Abs(pitch - _pitch) >= 0.0001f || pitch == 1f && _pitch != 1f) {
			_pitch = pitch;
			_stream!.SetPitch(pitch);
		}
	}

	private long Frames(TimeSpan time) => (long)(time.TotalSeconds * Chip.SampleRate);

	/// <summary>Stops the stream and discards what it holds.</summary>
	public void Dispose() {
		_disposed = true;
		_stream?.Dispose();
	}
}
