using HercWorks.Core.Data.File.Dbsim;

namespace HercWorks.Core.Io.Transform.Dbsim;

/// <summary>
/// What both games' <c>.TAP</c> transformers share (<see cref="InputTapeTransformer"/>, <see cref="Es1InputTapeTransformer"/>):
/// the seven-file bundle at the head of the tape, the same in both (<c>Tape_PackFile</c> and <c>Tape_UnpackFile</c>, DBSIM
/// <c>0045cc88</c> and <c>0045cd40</c>, ES1DBSIM <c>000574a0</c> and <c>0005756c</c>), and the 14-byte mouse event
/// both frame records carry (<see cref="TapeMouseEvent"/>). See docs/retail/formats/tap-input-tape.md#the-bundle and
/// docs/es1/formats/tap-input-tape.md#the-bundle.
/// </summary>
public abstract class InputTapeTransformerBase<T> : ByteTransformer<T> where T : class {
	/// <summary>One cockpit mouse record.</summary>
	public const int MouseEventSize = 14;

	/// <summary>
	/// Reads the bundle from the start of <see cref="ThreeSpaceByteTransformer.Bytes"/>, leaving the index after it. Null
	/// when a size or a file runs past the end: a check of this reader's own, as ES1's <c>Tape_UnpackFile</c> does not
	/// test its reads.
	/// </summary>
	protected byte[][]? IndexBundle() {
		int length = Bytes!.Length;
		var bundle = new byte[InputTape.BundleFileCount][];
		for (int i = 0; i < InputTape.BundleFileCount; i++) {
			if (Index + 4 > length) {
				return null;
			}

			int size = IndexIntLE();
			if (size < 0 || Index + size > length) {
				return null;
			}

			bundle[i] = IndexSegment(size);
		}

		return bundle;
	}

	/// <summary>
	/// The bundle alone — seven size-prefixed files, a null entry written as size 0 the way <c>Tape_PackFile</c> writes a
	/// file it could not open. A recorder writes this first, then the rest of the tape.
	/// </summary>
	public byte[] WriteBundle(byte[]?[] bundle) {
		using var outStream = new MemoryStream();
		for (int i = 0; i < InputTape.BundleFileCount; i++) {
			byte[] file = (i < bundle.Length ? bundle[i] : null) ?? Array.Empty<byte>();
			Emit(outStream, WriteIntLE(file.Length));
			Emit(outStream, file);
		}

		return outStream.ToArray();
	}

	/// <summary>Reads one mouse event at the index; the caller has checked that its 14 bytes are there.</summary>
	protected TapeMouseEvent IndexMouseEvent() => new() {
		X = IndexIntLE(),
		Y = IndexIntLE(),
		Buttons = (ushort)IndexShortLE(),
		Time = IndexIntLE(),
	};

	/// <summary>Writes one mouse event.</summary>
	protected void EmitMouseEvent(Stream stream, TapeMouseEvent mouse) {
		Emit(stream, WriteIntLE(mouse.X));
		Emit(stream, WriteIntLE(mouse.Y));
		Emit(stream, WriteShortLE((short)mouse.Buttons));
		Emit(stream, WriteIntLE(mouse.Time));
	}

	protected static void Emit(Stream stream, byte[] bytes) => stream.Write(bytes, 0, bytes.Length);
}
