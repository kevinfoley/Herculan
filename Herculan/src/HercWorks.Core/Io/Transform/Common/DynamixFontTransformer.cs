using HercWorks.Core.Data.File.Dyn;

namespace HercWorks.Core.Io.Transform.Common;

/// <summary>
/// Reads and writes a <see cref="DynamixFont"/> in the order <c>GLFont_ReadFromStream</c> (<c>00482908</c>) reads one
/// (docs/retail/formats/dfn-hfn-dci.md#layout): the header, the <c>arrayCount</c> x 16-byte block, the pool, the offsets, then
/// the widths and the map under their conditions. Writing a parsed font gives back its bytes.
///
/// <para>The envelope's tag is read past, not checked, and written back as <see cref="FontTag"/>: the registry binds this
/// reader by extension, and the engine's font loader takes whatever bytes it is handed. A walk that runs past the end, or a negative count, throws
/// <see cref="InvalidDataException"/>.</para>
/// </summary>
public class DynamixFontTransformer : ByteTransformer<DynamixFont> {
	/// <summary>The font's envelope tag, as stored.</summary>
	public static readonly byte[] FontTag = [0x05, 0x00, 0x28, 0x00];

	private const int HeaderLength = 0x22;

	/// <exception cref="InvalidDataException">The walk runs past the end, or a count is negative.</exception>
	public override DynamixFont? Parse(byte[]? inputArray) {
		if (inputArray == null || inputArray.Length <= 0) {
			return null;
		}

		if (inputArray.Length < HeaderLength) {
			throw new InvalidDataException($"{inputArray.Length} bytes is shorter than the font header.");
		}

		SetBytes(inputArray);
		Skip(4);
		var font = new DynamixFont { DeclaredSize = IndexIntLE() };
		int glyphCount = Count(IndexShortLE(), "glyph");
		font.MapCount = IndexShortLE();
		font.FirstCharCode = IndexShortLE();
		font.CellHeight = IndexShortLE();
		font.WidthsFlag = IndexShortLE();
		font.CellHeightRepeat = IndexShortLE();
		font.Baseline = IndexShortLE();
		font.BitsPerPixel = IndexShortLE();
		font.Field18 = IndexShortLE();
		font.InkHeight = IndexShortLE();
		int arrayCount = Count(IndexShortLE(), "array");
		int poolLength = Count(IndexIntLE(), "pool byte");

		font.ArrayBlock = Take(arrayCount * 16L, "array block");
		font.Pool = Take(poolLength, "glyph pool");
		Need(glyphCount * 4L, "offsets");
		font.Offsets = IndexIntLEArray(glyphCount);
		if (font.WidthsFlag == -1) {
			font.Widths = Take(glyphCount, "widths");
		}

		if (font.FirstCharCode == -1) {
			int mapCount = Count(font.MapCount, "map entry");
			Need(mapCount * 2L, "map");
			font.Map = IndexShortLEArray(mapCount);
		}

		font.Tail = IndexSegment(inputArray.Length - Index);
		return font;
	}

	public override byte[]? Write(DynamixFont source) {
		using var o = new MemoryStream();
		var w = new BinaryWriter(o);
		w.Write(FontTag);
		w.Write(source.DeclaredSize);
		w.Write((short)source.Offsets.Length);
		w.Write(source.MapCount);
		w.Write(source.FirstCharCode);
		w.Write(source.CellHeight);
		w.Write(source.WidthsFlag);
		w.Write(source.CellHeightRepeat);
		w.Write(source.Baseline);
		w.Write(source.BitsPerPixel);
		w.Write(source.Field18);
		w.Write(source.InkHeight);
		w.Write((short)(source.ArrayBlock.Length / 16));
		w.Write(source.Pool.Length);
		w.Write(source.ArrayBlock);
		w.Write(source.Pool);
		foreach (int offset in source.Offsets) {
			w.Write(offset);
		}

		if (source.Widths != null) {
			w.Write(source.Widths);
		}

		foreach (short entry in source.Map ?? []) {
			w.Write(entry);
		}

		w.Write(source.Tail);
		w.Flush();
		return o.ToArray();
	}

	private int Count(int value, string what) =>
		value >= 0 ? value : throw new InvalidDataException($"A negative {what} count, {value}.");

	private void Need(long bytes, string what) {
		if (Index + bytes > Bytes!.Length) {
			throw new InvalidDataException($"The {what} at {Index} run past the end.");
		}
	}

	private byte[] Take(long bytes, string what) {
		Need(bytes, what);
		return IndexSegment((int)bytes);
	}
}
