using HercWorks.Core.Data.File.Dyn;
using HercWorks.Vol;

namespace HercWorks.Core.Io.Transform.Common;

/// <summary>
/// Transforms byte[] data to and from DynamixBitmap game files.
/// Ported from org.hercworks.core.io.transform.common.DynamixBitmapTransformer.
/// </summary>
public class DynamixBitmapTransformer : ByteTransformer<DynamixBitmap> {
	public override DynamixBitmap? Parse(byte[]? inputArray) {
		if (inputArray == null || inputArray.Length <= 0) {
			// TODO (carried over from Java): log null
			return null;
		}
		SetBytes(inputArray);

		Skip(4); // magic header — the write path emits DynamixBitmap.HeaderMagic, so it isn't retained.
		Skip(4); // on-disk size — the write path recomputes it from ImageData, so it isn't retained.

		var dbm = new DynamixBitmap {
			Rows = IndexShortLE(),
			Cols = IndexShortLE(),
			BitsPerPixel = IndexByte(),
			Flags = IndexByte(),
			Packing = (BitmapPacking)IndexByte(),
			ImageDataLen = IndexIntLE()
		};
		short extraCount = IndexShortLE();
		dbm.ImageData = IndexSegment(dbm.ImageDataLen);
		if (extraCount > 0) {
			dbm.ExtraDwords = new uint[extraCount];
			for (int i = 0; i < extraCount; i++) {
				dbm.ExtraDwords[i] = (uint)IndexIntLE();
			}
		}

		return dbm;
	}

	public override byte[]? Write(DynamixBitmap dbm) {
		using var objectBytes = new MemoryStream();

		objectBytes.Write(DynamixBitmap.HeaderMagic, 0, DynamixBitmap.HeaderMagic.Length);

		int size = 13 + dbm.ImageData!.Length + dbm.ExtraDwords.Length * 4;

		var sizeBytes = WriteIntLE(size);
		objectBytes.Write(sizeBytes, 0, sizeBytes.Length);

		var rowsBytes = WriteShortLE(dbm.Rows);
		objectBytes.Write(rowsBytes, 0, rowsBytes.Length);

		var colsBytes = WriteShortLE(dbm.Cols);
		objectBytes.Write(colsBytes, 0, colsBytes.Length);

		objectBytes.WriteByte(dbm.BitsPerPixel);
		objectBytes.WriteByte(dbm.Flags);
		objectBytes.WriteByte((byte)dbm.Packing);

		var imgLenBytes = WriteIntLE(dbm.ImageDataLen);
		objectBytes.Write(imgLenBytes, 0, imgLenBytes.Length);

		var extraCountBytes = WriteShortLE((short)dbm.ExtraDwords.Length);
		objectBytes.Write(extraCountBytes, 0, extraCountBytes.Length);

		objectBytes.Write(dbm.ImageData, 0, dbm.ImageData.Length);

		foreach (uint extra in dbm.ExtraDwords) {
			var extraBytes = WriteIntLE((int)extra);
			objectBytes.Write(extraBytes, 0, extraBytes.Length);
		}

		// The record pads to an even length with a zero byte, standalone and inside a .DBA alike.
		if (objectBytes.Length % 2 != 0) {
			objectBytes.WriteByte(0x00);
		}

		return objectBytes.ToArray();
	}
}
