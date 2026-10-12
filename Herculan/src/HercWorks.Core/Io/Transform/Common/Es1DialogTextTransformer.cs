using HercWorks.Core.Data.File;

namespace HercWorks.Core.Io.Transform.Common;

/// <summary>
/// Reads and writes ES1's talking-head text files (<see cref="Es1DialogText"/>;
/// docs/es1/formats/bin-string-tables.md#the-dialog-text-files): the version word, then the table
/// <see cref="BinStringFileTransformer"/> reads. Writing a parsed file gives back its bytes.
///
/// <para>A file whose table does not end at the file's last byte (8 + 2 × its count + its pool size after the version) throws
/// <see cref="InvalidDataException"/>, so a file this model would not give back whole is rejected.</para>
/// </summary>
public class Es1DialogTextTransformer : ByteTransformer<Es1DialogText> {
	/// <exception cref="InvalidDataException">The table does not end at the file's last byte.</exception>
	public override Es1DialogText? Parse(byte[]? inputArray) {
		if (inputArray == null || inputArray.Length <= 0) {
			return null;
		}

		if (inputArray.Length < 10) {
			throw new InvalidDataException($"The file is {inputArray.Length} bytes, shorter than its version word and table counts.");
		}

		short version = BitConverter.ToInt16(inputArray, 0);
		int count = BitConverter.ToInt32(inputArray, 2);
		int pool = BitConverter.ToInt32(inputArray, 6);
		if (count < 0 || pool < 0 || 10L + 2L * count + pool != inputArray.Length) {
			throw new InvalidDataException($"The file is {inputArray.Length} bytes, not 10 + 2 × its count {count} + its pool {pool}.");
		}

		var table = new BinStringFileTransformer().Parse(inputArray[2..])!;
		return new Es1DialogText { Version = version, Lines = table.Values! };
	}

	public override byte[]? Write(Es1DialogText source) {
		byte[] table = new BinStringFileTransformer().Write(new StringBinaryFile { Values = source.Lines })!;
		return [.. BitConverter.GetBytes(source.Version), .. table];
	}
}
