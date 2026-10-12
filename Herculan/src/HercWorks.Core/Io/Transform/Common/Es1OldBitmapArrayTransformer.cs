using HercWorks.Core.Data.File.Dyn;

namespace HercWorks.Core.Io.Transform.Common;

/// <summary>
/// Reads and writes an <see cref="Es1OldBitmapArray"/> (docs/es1/formats/dfn-hfn-dci.md#the-old-bitmap-record). Items
/// follow each other with no padding, each its declared size plus 8 long; the last may be cut short by the file's end,
/// and its <see cref="Es1OldBitmap.Tail"/> then holds what there is. Writing a parsed file gives back its bytes.
/// </summary>
public class Es1OldBitmapArrayTransformer : ByteTransformer<Es1OldBitmapArray> {
	/// <summary>The bitmap array's envelope tag, as stored.</summary>
	public static readonly byte[] ArrayTag = [0x01, 0x00, 0x28, 0x00];

	/// <summary>The old bitmap's envelope tag, as stored.</summary>
	public static readonly byte[] OldBitmapTag = [0x00, 0x00, 0x28, 0x00];

	private const int ItemHeaderLength = 20;

	/// <summary>Whether <paramref name="bytes"/> open a bitmap array whose first item is an old-format bitmap.</summary>
	public static bool IsOldBitmapArray(byte[]? bytes) =>
		bytes is { Length: >= 16 } && bytes.AsSpan(0, 4).SequenceEqual(ArrayTag) && bytes.AsSpan(12, 4).SequenceEqual(OldBitmapTag);

	/// <exception cref="InvalidDataException">The bytes are not an array of old-format bitmaps, or an item's header or pixels run past the end.</exception>
	public override Es1OldBitmapArray? Parse(byte[]? inputArray) {
		if (inputArray == null || inputArray.Length <= 0) {
			return null;
		}

		if (!IsOldBitmapArray(inputArray)) {
			throw new InvalidDataException("Not a bitmap array of old-format bitmaps.");
		}

		SetBytes(inputArray);
		Skip(4);
		var array = new Es1OldBitmapArray { DeclaredSize = IndexIntLE() };
		var items = new Es1OldBitmap[IndexIntLE()];
		for (int i = 0; i < items.Length; i++) {
			int start = Index;
			if (start + ItemHeaderLength > inputArray.Length || !inputArray.AsSpan(start, 4).SequenceEqual(OldBitmapTag)) {
				throw new InvalidDataException($"Item {i} at 0x{start:x} is not an old-format bitmap.");
			}

			Skip(4);
			var item = new Es1OldBitmap {
				DeclaredSize = IndexIntLE(),
				Rows = IndexShortLE(),
				Cols = IndexShortLE(),
				Field0C = IndexByte(),
				Field0D = IndexSegment(3),
			};
			int count = IndexIntLE();
			if (count < 0 || Index + count > inputArray.Length) {
				throw new InvalidDataException($"Item {i}'s {count} pixels run past the end.");
			}

			item.Pixels = IndexSegment(count);
			int end = Math.Min(start + 8 + item.DeclaredSize, inputArray.Length);
			item.Tail = end > Index ? IndexSegment(end - Index) : [];
			items[i] = item;
		}

		if (Index != inputArray.Length) {
			throw new InvalidDataException($"{inputArray.Length - Index} bytes follow the last item.");
		}

		array.Items = items;
		return array;
	}

	public override byte[]? Write(Es1OldBitmapArray source) {
		using var output = new MemoryStream();
		void Emit(byte[] bytes) => output.Write(bytes, 0, bytes.Length);

		Emit(ArrayTag);
		Emit(WriteIntLE(source.DeclaredSize));
		Emit(WriteIntLE(source.Items.Length));
		foreach (var item in source.Items) {
			Emit(OldBitmapTag);
			Emit(WriteIntLE(item.DeclaredSize));
			Emit(WriteShortLE(item.Rows));
			Emit(WriteShortLE(item.Cols));
			output.WriteByte(item.Field0C);
			Emit(item.Field0D);
			Emit(WriteIntLE(item.Pixels.Length));
			Emit(item.Pixels);
			Emit(item.Tail);
		}

		return output.ToArray();
	}
}
