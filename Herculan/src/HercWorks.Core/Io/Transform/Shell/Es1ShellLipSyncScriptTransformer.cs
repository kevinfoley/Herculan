using System.Buffers.Binary;
using HercWorks.Core.Data.File;

namespace HercWorks.Core.Io.Transform.Shell;

/// <summary>
/// Reads and writes ES1's shell lip-sync scripts (<see cref="Es1ShellLipSyncScript"/>;
/// docs/es1/formats/snc-lip-sync.md#the-shells-scripts), the bytes <c>ShellSnc_Load</c> (<c>ES1GO 0004589c</c>) reads. Writing a
/// parsed script gives back its bytes.
///
/// <para>A file shorter than the length's 2 bytes, a negative length, or one that runs past the end throws
/// <see cref="InvalidDataException"/>.</para>
/// </summary>
public class Es1ShellLipSyncScriptTransformer : ByteTransformer<Es1ShellLipSyncScript> {
	/// <summary>
	/// Whether <paramref name="bytes"/> are in the shell's layout: an <c>int16</c> length that is the size less 2. A simulator
	/// script's <c>int32</c> length is its size less 4, so its low word never is.
	/// </summary>
	public static bool IsShellLayout(byte[]? bytes) =>
		bytes is { Length: >= 2 } && BinaryPrimitives.ReadInt16LittleEndian(bytes) + 2 == bytes.Length;

	/// <exception cref="InvalidDataException">The length is missing, negative or runs past the end.</exception>
	public override Es1ShellLipSyncScript? Parse(byte[]? inputArray) {
		if (inputArray == null || inputArray.Length <= 0) {
			return null;
		}

		if (inputArray.Length < 2) {
			throw new InvalidDataException($"{inputArray.Length} byte is shorter than the script's length.");
		}

		SetBytes(inputArray);
		short length = IndexShortLE();
		if (length < 0 || 2 + length > inputArray.Length) {
			throw new InvalidDataException($"The length, {length}, runs past the end of {inputArray.Length} bytes.");
		}

		var records = new Es1ShellLipSyncRecord[length / 4];
		for (int i = 0; i < records.Length; i++) {
			records[i] = new Es1ShellLipSyncRecord { Frame = IndexShortLE(), Delta = IndexShortLE() };
		}

		return new Es1ShellLipSyncScript { Length = length, Records = records, Unread = IndexSegment(inputArray.Length - Index) };
	}

	public override byte[]? Write(Es1ShellLipSyncScript source) {
		using var o = new MemoryStream();
		var w = new BinaryWriter(o);
		w.Write(source.Length);
		foreach (var record in source.Records) {
			w.Write(record.Frame);
			w.Write(record.Delta);
		}

		w.Write(source.Unread);
		w.Flush();
		return o.ToArray();
	}
}
