using HercWorks.Core.Data.File.Dbsim;

namespace HercWorks.Core.Io.Transform.Dbsim;

/// <summary>
/// Transforms byte[] data to and from clip-region files (<c>.EDG</c>, <c>.HD0</c>-<c>.HD3</c>,
/// <c>.ED0</c>-<c>.ED3</c>; see <see cref="ClipRegionFile"/>). Reads in the order
/// <c>CockpitClipRegions_Load</c> (<c>0042dcf0</c>) does, and keeps what follows the last block so a
/// file writes back byte for byte.
/// </summary>
public class ClipRegionFileTransformer : ByteTransformer<ClipRegionFile> {
	public override ClipRegionFile? Parse(byte[]? inputArray) {
		if (inputArray == null) {
			return null;
		}

		SetBytes(inputArray);

		var rects = new ClipRegionFile.Rect[Math.Max(0, (int)IndexShortLE())];
		for (int i = 0; i < rects.Length; i++) {
			rects[i] = new ClipRegionFile.Rect {
				Y0 = IndexShortLE(),
				Y1 = IndexShortLE(),
				X0 = IndexShortLE(),
				X1 = IndexShortLE(),
			};
		}

		var blocks = new ClipRegionFile.SpanBlock[Math.Max(0, (int)IndexShortLE())];
		for (int b = 0; b < blocks.Length; b++) {
			short firstRow = IndexShortLE();
			var rows = new ClipRegionFile.Span[Math.Max(0, (int)IndexShortLE())];
			for (int i = 0; i < rows.Length; i++) {
				rows[i] = new ClipRegionFile.Span {
					XStart = IndexShortLE(),
					XEnd = IndexShortLE(),
				};
			}

			blocks[b] = new ClipRegionFile.SpanBlock { FirstRow = firstRow, Rows = rows };
		}

		return new ClipRegionFile {
			Rects = rects,
			Blocks = blocks,
			TrailingBytes = IndexSegment(inputArray.Length - Index),
		};
	}

	public override byte[]? Write(ClipRegionFile file) {
		using var outStream = new MemoryStream();

		void Emit(byte[] bytes) => outStream.Write(bytes, 0, bytes.Length);

		Emit(WriteShortLE((short)file.Rects.Length));
		foreach (var rect in file.Rects) {
			Emit(WriteShortLE(rect.Y0));
			Emit(WriteShortLE(rect.Y1));
			Emit(WriteShortLE(rect.X0));
			Emit(WriteShortLE(rect.X1));
		}

		Emit(WriteShortLE((short)file.Blocks.Length));
		foreach (var block in file.Blocks) {
			Emit(WriteShortLE(block.FirstRow));
			Emit(WriteShortLE((short)block.Rows.Length));
			foreach (var span in block.Rows) {
				Emit(WriteShortLE(span.XStart));
				Emit(WriteShortLE(span.XEnd));
			}
		}

		Emit(file.TrailingBytes);

		return outStream.ToArray();
	}
}
