using HercWorks.Core.Data.File.Dbsim;

namespace HercWorks.Core.Io.Transform.Dbsim;

/// <summary>
/// Transforms byte[] data to and from .TAP input tapes (see <see cref="InputTape"/> for the model and
/// docs/retail/formats/tap-input-tape.md for the format). New: no Java equivalent, not a ported format — read
/// from DBSIM's own writer.
///
/// <para>The frame stream has no count and no terminator: DBSIM reads until a header read comes up
/// short, so a trailing partial header is dropped here the same way.</para>
/// </summary>
public class InputTapeTransformer : InputTapeTransformerBase<InputTape> {
	/// <summary>The fixed part of a frame record.</summary>
	public const int FrameHeaderSize = 0x18;

	public override InputTape? Parse(byte[]? inputArray) {
		if (inputArray == null) {
			return null;
		}

		SetBytes(inputArray);
		if (IndexBundle() is not { } bundle) {
			return null;
		}

		var tape = new InputTape { Bundle = bundle };

		if (Index + InputTape.CapabilityBlockSize > inputArray.Length) {
			return tape;
		}

		tape.Capabilities = IndexSegment(InputTape.CapabilityBlockSize);

		while (Index + FrameHeaderSize <= inputArray.Length) {
			var frame = new InputTape.Frame {
				CommandWord = IndexShortLE(),
				Axes = IndexShortLEArray(4),
				TickDelta = IndexShortLE(),
				ButtonBank = IndexByte(),
				RawButtonBank = IndexByte(),
				TriggerBank = IndexByte(),
				Spare = IndexByte(),
			};

			int mouseCount = IndexIntLE();
			int commandCount = IndexIntLE();
			if (mouseCount < 0 || commandCount < 0
				|| Index + (long)mouseCount * MouseEventSize + (long)commandCount * 2 > inputArray.Length) {
				break;
			}

			for (int m = 0; m < mouseCount; m++) {
				frame.MouseEvents.Add(IndexMouseEvent());
			}

			frame.Commands = IndexShortLEArray(commandCount);
			tape.Frames.Add(frame);
		}

		return tape;
	}

	public override byte[]? Write(InputTape tape) {
		if (tape == null) {
			return null;
		}

		using var outStream = new MemoryStream();
		Emit(outStream, WriteBundle(tape.Bundle));
		Emit(outStream, tape.Capabilities);

		foreach (var frame in tape.Frames) {
			Emit(outStream, WriteFrame(frame));
		}

		return outStream.ToArray();
	}

	/// <summary>
	/// One frame record: the 24-byte header, its mouse events and its command codes. A recorder writes
	/// <see cref="InputTapeTransformerBase{T}.WriteBundle"/>, then the capability block, then this once per frame.
	/// </summary>
	public byte[] WriteFrame(InputTape.Frame frame) {
		using var outStream = new MemoryStream();
		Emit(outStream, WriteShortLE(frame.CommandWord));
		Emit(outStream, WriteShortLESegment(frame.Axes));
		Emit(outStream, WriteShortLE(frame.TickDelta));
		outStream.WriteByte(frame.ButtonBank);
		outStream.WriteByte(frame.RawButtonBank);
		outStream.WriteByte(frame.TriggerBank);
		outStream.WriteByte(frame.Spare);
		Emit(outStream, WriteIntLE(frame.MouseEvents.Count));
		Emit(outStream, WriteIntLE(frame.Commands.Length));

		foreach (var mouse in frame.MouseEvents) {
			EmitMouseEvent(outStream, mouse);
		}

		Emit(outStream, WriteShortLESegment(frame.Commands));
		return outStream.ToArray();
	}
}
