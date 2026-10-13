using HercWorks.Core.Data.File.Dbsim;

namespace HercWorks.Core.Io.Transform.Dbsim;

/// <summary>
/// Reads and writes ES1's clip regions (<see cref="Es1ClipRegions"/>; docs/es1/formats/hd-ed-clip-regions.md), the walk of
/// <c>CockpitClipRegions_Load</c> (<c>ES1DBSIM 00024d44</c>). Writing a parsed file gives back its bytes.
///
/// <para>A walk that runs past the end, or a negative count, throws <see cref="InvalidDataException"/>. Bytes after the last
/// block, which the loader leaves unread, are kept in <see cref="Es1ClipRegions.Unread"/>.</para>
/// </summary>
public class Es1ClipRegionsTransformer : ByteTransformer<Es1ClipRegions> {
	/// <exception cref="InvalidDataException">The walk runs past the end, or a count is negative.</exception>
	public override Es1ClipRegions? Parse(byte[]? inputArray) {
		if (inputArray == null || inputArray.Length <= 0) {
			return null;
		}

		SetBytes(inputArray);
		var rects = new Es1ClipRect[Count(inputArray.Length)];
		for (int r = 0; r < rects.Length; r++) {
			Need(4, inputArray.Length);
			rects[r] = new Es1ClipRect { Top = IndexShortLE(), Bottom = IndexShortLE() };
		}

		var blocks = new Es1ClipSpanBlock[Count(inputArray.Length)];
		for (int b = 0; b < blocks.Length; b++) {
			Need(2, inputArray.Length);
			short firstRow = IndexShortLE();
			var rows = new Es1ClipSpan[Count(inputArray.Length)];
			for (int i = 0; i < rows.Length; i++) {
				Need(4, inputArray.Length);
				rows[i] = new Es1ClipSpan { Start = IndexShortLE(), End = IndexShortLE() };
			}

			blocks[b] = new Es1ClipSpanBlock { FirstRow = firstRow, Rows = rows };
		}

		return new Es1ClipRegions { Rects = rects, Blocks = blocks, Unread = inputArray[Index..] };
	}

	public override byte[]? Write(Es1ClipRegions source) {
		using var o = new MemoryStream();
		var w = new BinaryWriter(o);
		w.Write((short)source.Rects.Length);
		foreach (var rect in source.Rects) {
			w.Write(rect.Top);
			w.Write(rect.Bottom);
		}

		w.Write((short)source.Blocks.Length);
		foreach (var block in source.Blocks) {
			w.Write(block.FirstRow);
			w.Write((short)block.Rows.Length);
			foreach (var span in block.Rows) {
				w.Write(span.Start);
				w.Write(span.End);
			}
		}

		w.Write(source.Unread);
		w.Flush();
		return o.ToArray();
	}

	private int Count(int length) {
		Need(2, length);
		short count = IndexShortLE();
		return count >= 0 ? count : throw new InvalidDataException($"A negative count, {count}, at {Index - 2}.");
	}

	private void Need(int bytes, int length) {
		if (Index + bytes > length) {
			throw new InvalidDataException($"The walk runs past the end at {Index}.");
		}
	}
}
