using HercWorks.Core.Data.File.Dyn;

namespace HercWorks.Core.Io.Transform.Common;

/// <summary>
/// Reads and writes a <c>.DCI</c> cursor (<see cref="DynamixCursor"/>) as the cursor class's read slot reads one
/// (<c>GLCursor_ReadFromStream</c>: VSHELL <c>00454e80</c>, <c>ES1GO 00066434</c>): the envelope, the two hotspot dwords,
/// then one class item, the bitmap record, which <see cref="DynamixBitmapTransformer"/> reads and writes with its pad byte
/// (docs/retail/formats/dfn-hfn-dci.md#dci--cursor-image). Bytes past the envelope are kept as
/// <see cref="DynamixCursor.Trailing"/>. Writing a parsed file gives back its bytes.
/// </summary>
public class DynamixCursorTransformer : ByteTransformer<DynamixCursor> {
	private const int EnvelopeLength = 8;
	private const int HotspotLength = 8;

	/// <exception cref="InvalidDataException">The tag is not the cursor's, or the envelope's size runs past the file.</exception>
	public override DynamixCursor? Parse(byte[]? inputArray) {
		if (inputArray == null || inputArray.Length <= 0) {
			return null;
		}

		if (inputArray.Length < EnvelopeLength + HotspotLength || !inputArray.AsSpan(0, 4).SequenceEqual(DynamixCursor.HeaderMagic)) {
			throw new InvalidDataException("The file does not open with the cursor's tag.");
		}

		SetBytes(inputArray);
		Skip(4);
		int size = IndexIntLE();
		int end = EnvelopeLength + size;
		if (size < HotspotLength || end > inputArray.Length) {
			throw new InvalidDataException($"The envelope's size {size} runs past the file's {inputArray.Length} bytes.");
		}

		var cursor = new DynamixCursor { HotspotX = IndexIntLE(), HotspotY = IndexIntLE() };
		cursor.Bitmap = new DynamixBitmapTransformer().Parse(inputArray[Index..end])
			?? throw new InvalidDataException("The envelope holds no bitmap record.");
		cursor.Trailing = inputArray[end..];
		return cursor;
	}

	public override byte[]? Write(DynamixCursor source) {
		byte[] bitmap = new DynamixBitmapTransformer().Write(source.Bitmap)!;
		using var output = new MemoryStream();
		void Emit(byte[] bytes) => output.Write(bytes, 0, bytes.Length);

		Emit(DynamixCursor.HeaderMagic);
		Emit(WriteIntLE(HotspotLength + bitmap.Length));
		Emit(WriteIntLE(source.HotspotX));
		Emit(WriteIntLE(source.HotspotY));
		Emit(bitmap);
		Emit(source.Trailing);
		return output.ToArray();
	}
}
