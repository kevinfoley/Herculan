namespace Herculan.Engine.Audio.Opl;

/// <summary>
/// An <see cref="IOplChip"/>'s output played through a stream of the host's mixer (<see cref="IAudioBackend.OpenStream"/>),
/// opened stereo at the chip's own rate, which the mixer resamples. This class is the engine's own and rests on no RE
/// (docs/herculan/plan-es1-sound.md, module D part 6).
///
/// <para>The caller sets the pace: each <see cref="Render"/> advances the chip by the frames asked for, so a driver that
/// makes a timer tick's register writes and then renders that tick's share of frames keeps its writes at the tick's time
/// in the output. The frames are handed to the stream a block at a time; a block the stream has no room for waits, and
/// once <see cref="MaxWaitingBlocks"/> wait, the oldest is dropped, an engine guard that bounds the delay when the device
/// stops taking blocks.</para>
///
/// <para>With no stream, under the host's <c>--no-sound</c> or with no audio device, the writes still reach the chip and
/// <see cref="Render"/> generates nothing.</para>
/// </summary>
public sealed class OplStream : IDisposable {
	/// <summary>The blocks that wait for room in the stream at most.</summary>
	public const int MaxWaitingBlocks = 8;

	private readonly IAudioStream? _stream;
	private readonly int _blockFrames;
	private readonly Queue<(short[] Pcm, long StartFrame)> _waiting = new();
	private readonly Stack<short[]> _spare = new();
	private short[] _block;
	private int _filled;
	private long _frame;

	/// <param name="backend">The host's mixer.</param>
	/// <param name="chip">The chip whose output plays.</param>
	/// <param name="blockFrames">The frames of one block handed to the stream.</param>
	public OplStream(IAudioBackend backend, IOplChip chip, int blockFrames) {
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(blockFrames);
		Chip = chip;
		_blockFrames = blockFrames;
		_block = new short[blockFrames * 2];
		_stream = backend.IsAvailable ? backend.OpenStream(chip.SampleRate, 2) : null;
		_stream?.SetGain(1f);
	}

	/// <summary>The chip, for the driver's register writes.</summary>
	public IOplChip Chip { get; }

	/// <summary>Whether the output reaches a stream; false when <see cref="Render"/> generates nothing.</summary>
	public bool IsAudible => _stream != null;

	/// <summary>
	/// Advances the chip by <paramref name="frames"/> frames and hands each block completed to the stream, or queues it
	/// to wait for room.
	/// </summary>
	public void Render(int frames) {
		if (_stream == null) {
			return;
		}

		while (frames > 0) {
			int count = Math.Min(frames, _blockFrames - _filled);
			Chip.Generate(_block.AsSpan(_filled * 2, count * 2));
			_filled += count;
			frames -= count;
			if (_filled == _blockFrames) {
				if (_waiting.Count == MaxWaitingBlocks) {
					_spare.Push(_waiting.Dequeue().Pcm);
				}

				_waiting.Enqueue((_block, _frame));
				_frame += _blockFrames;
				_block = _spare.Count > 0 ? _spare.Pop() : new short[_blockFrames * 2];
				_filled = 0;
			}
		}

		Feed();
	}

	/// <summary>Hands the waiting blocks to the stream while it has room.</summary>
	private void Feed() {
		while (_waiting.Count > 0 && _stream!.FreeBlocks > 0) {
			var (pcm, startFrame) = _waiting.Dequeue();
			_stream.Queue(pcm, startFrame);
			_spare.Push(pcm);
		}
	}

	/// <summary>Stops the stream and discards what it holds.</summary>
	public void Dispose() {
		_stream?.Dispose();
	}
}
