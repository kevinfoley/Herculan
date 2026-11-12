using HercWorks.Core.Data.File.Dbsim;

namespace HercWorks.Core.Io.Transform.Dbsim;

/// <summary>
/// Transforms byte[] data to and from ES1's .TAP input tapes (see <see cref="Es1InputTape"/> for the model and
/// docs/es1/formats/tap-input-tape.md#the-frames for the format). New: no Java equivalent — read from ES1DBSIM's own
/// writer and reader, <c>Input_ReadPlayerInput</c> (<c>0004cb14</c>).
///
/// <para>The frame stream has no count and no terminator: playback reads a frame's 14 bytes and its mouse-event count
/// and stops when either read comes up short. A trailing record whose bytes run short is dropped here, as
/// <see cref="InputTapeTransformer"/> drops one, and the reader also stops at a negative mouse-event count. Both are this
/// reader's own rules, so that what it reads writes back byte for byte: retail plays a record whose 18 fixed bytes are
/// whole however its events end, and treats a count below 1 as no events.</para>
/// </summary>
public class Es1InputTapeTransformer : InputTapeTransformerBase<Es1InputTape> {
	/// <summary>The fixed part of a frame record: the 14 bytes read as one, then the <c>int32</c> mouse-event count.</summary>
	public const int FrameFixedSize = 18;

	public override Es1InputTape? Parse(byte[]? inputArray) {
		if (inputArray == null) {
			return null;
		}

		SetBytes(inputArray);
		if (IndexBundle() is not { } bundle) {
			return null;
		}

		var tape = new Es1InputTape { Bundle = bundle };
		while (Index + FrameFixedSize <= inputArray.Length) {
			var frame = new Es1InputTape.Frame {
				CommandWord = IndexShortLE(),
				Axes = IndexShortLEArray(4),
				TickDelta = IndexShortLE(),
				Buttons = (Es1InputTape.Button)(ushort)IndexShortLE(),
			};

			int mouseCount = IndexIntLE();
			if (mouseCount < 0 || Index + (long)mouseCount * MouseEventSize > inputArray.Length) {
				break;
			}

			for (int m = 0; m < mouseCount; m++) {
				frame.MouseEvents.Add(IndexMouseEvent());
			}

			tape.Frames.Add(frame);
		}

		return tape;
	}

	public override byte[]? Write(Es1InputTape tape) {
		if (tape == null) {
			return null;
		}

		using var outStream = new MemoryStream();
		Emit(outStream, WriteBundle(tape.Bundle));
		foreach (var frame in tape.Frames) {
			Emit(outStream, WriteFrame(frame));
		}

		return outStream.ToArray();
	}

	/// <summary>
	/// One frame record: the command word, the four axes, the tick delta, the button word, the mouse-event count and the
	/// events. A recorder writes <see cref="InputTapeTransformerBase{T}.WriteBundle"/>, then this once per frame.
	/// </summary>
	public byte[] WriteFrame(Es1InputTape.Frame frame) {
		using var outStream = new MemoryStream();
		Emit(outStream, WriteShortLE(frame.CommandWord));
		Emit(outStream, WriteShortLESegment(frame.Axes));
		Emit(outStream, WriteShortLE(frame.TickDelta));
		Emit(outStream, WriteShortLE((short)frame.Buttons));
		Emit(outStream, WriteIntLE(frame.MouseEvents.Count));
		foreach (var mouse in frame.MouseEvents) {
			EmitMouseEvent(outStream, mouse);
		}

		return outStream.ToArray();
	}
}
