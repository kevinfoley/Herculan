using HercWorks.Core.Data.File;
using HercWorks.Vol;
using System.Text;

namespace HercWorks.Core.Io.Transform.Common;

/// <summary>Transforms byte[] data to and from a <c>.BIN</c> string table (see <see cref="StringBinaryFile"/>).</summary>
public class BinStringFileTransformer : ByteTransformer<StringBinaryFile> {
	public override StringBinaryFile? Parse(byte[]? inputArray) {
		if (inputArray == null) {
			return null;
		}
		Index = 0;
		SetBytes(inputArray);

		var binFile = new StringBinaryFile();

		// note - the reading here ditches the structure of the file; writing back to the format
		// will do the metadata generation.
		int totalStrings = IndexIntLE();

		Skip(4); // skips total strings size, not needed here.

		int indexStart = Index;
		int stringStart = Index + totalStrings * 2;

		// Each string runs from its offset to its NUL, read as Latin-1 so every byte survives a write.
		var values = new string[totalStrings];
		for (int i = 0; i < totalStrings; i++) {
			Index = indexStart + i * 2;
			int start = stringStart + IndexShortLE();
			int end = Array.IndexOf(inputArray, (byte)0, start);
			if (end < 0) {
				end = inputArray.Length;
			}

			values[i] = Encoding.Latin1.GetString(inputArray, start, end - start);
			Index = Math.Min(end + 1, inputArray.Length);
		}

		binFile.Values = values;

		return binFile;
	}

	/// <summary>Writes the strings back in order, each with its NUL, and regenerates the offsets.</summary>
	public override byte[]? Write(StringBinaryFile? sbf) {
		if (sbf == null) {
			return null;
		}

		using var pool = new MemoryStream();
		var index = new short[sbf.Values!.Length];
		for (int s = 0; s < sbf.Values.Length; s++) {
			index[s] = (short)pool.Length;
			var strBytes = Encoding.Latin1.GetBytes(sbf.Values[s]);
			pool.Write(strBytes, 0, strBytes.Length);
			pool.WriteByte(0x00);
		}

		using var outStream = new MemoryStream();

		var totalBytes = WriteIntLE(sbf.Values.Length);
		outStream.Write(totalBytes, 0, totalBytes.Length);

		var sizeBytes = WriteIntLE((int)pool.Length);
		outStream.Write(sizeBytes, 0, sizeBytes.Length);

		for (int i = 0; i < sbf.Values.Length; i++) {
			var idxBytes = WriteShortLE(index[i]);
			outStream.Write(idxBytes, 0, idxBytes.Length);
		}

		pool.WriteTo(outStream);

		return outStream.ToArray();
	}
}
