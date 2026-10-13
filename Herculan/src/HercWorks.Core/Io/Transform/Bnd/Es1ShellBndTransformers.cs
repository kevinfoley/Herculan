using HercWorks.Core.Data.File.Bnd;

namespace HercWorks.Core.Io.Transform.Bnd;

/// <summary>
/// Reads and writes ES1's shell <c>BND\MAIN.BND</c> (<see cref="Es1ShellMainBnd"/>), the 20 bytes <c>Main_LoadBnd</c>
/// (<c>ES1GO 00012704</c>) reads (docs/es1/formats/bnd-notes.md#bndmainbnd). Writing a parsed file gives back its bytes. A
/// file of any other length throws <see cref="InvalidDataException"/>.
/// </summary>
public class Es1ShellMainBndTransformer : ByteTransformer<Es1ShellMainBnd> {
	private const int FileLength = 20;

	/// <exception cref="InvalidDataException">The file is not 20 bytes.</exception>
	public override Es1ShellMainBnd? Parse(byte[]? inputArray) {
		if (inputArray == null || inputArray.Length <= 0) {
			return null;
		}

		if (inputArray.Length != FileLength) {
			throw new InvalidDataException($"The file is {inputArray.Length} bytes, not {FileLength}.");
		}

		SetBytes(inputArray);
		return new Es1ShellMainBnd {
			Stamp = (uint)IndexIntLE(),
			Unread04 = IndexIntLE(),
			LowHeapFlagLimit = IndexIntLE(),
			MinimumHeap = IndexIntLE(),
			Unread10 = IndexIntLE(),
		};
	}

	public override byte[]? Write(Es1ShellMainBnd source) {
		using var output = new MemoryStream();
		foreach (int value in new[] { (int)source.Stamp, source.Unread04, source.LowHeapFlagLimit, source.MinimumHeap, source.Unread10 }) {
			byte[] bytes = WriteIntLE(value);
			output.Write(bytes, 0, bytes.Length);
		}

		return output.ToArray();
	}
}

/// <summary>
/// Reads and writes one of ES1's other seven shell tuning files (<see cref="Es1ShellTuningFile"/>), each of which its
/// <c>*_LoadBnd</c> reads whole with <c>Ovl_ReadFile</c> (<c>ES1GO 00051918</c>; docs/es1/formats/bnd-notes.md#the-shells-files).
/// Writing a parsed file gives back its bytes.
/// </summary>
public class Es1ShellTuningFileTransformer : ByteTransformer<Es1ShellTuningFile> {
	public override Es1ShellTuningFile? Parse(byte[]? inputArray) =>
		inputArray == null || inputArray.Length <= 0 ? null : new Es1ShellTuningFile { Bytes = (byte[])inputArray.Clone() };

	public override byte[]? Write(Es1ShellTuningFile source) => (byte[])source.Bytes.Clone();
}
