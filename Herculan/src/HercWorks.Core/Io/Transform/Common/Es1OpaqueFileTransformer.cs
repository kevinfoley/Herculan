using HercWorks.Core.Data.File;

namespace HercWorks.Core.Io.Transform.Common;

/// <summary>
/// Reads and writes an <see cref="Es1OpaqueFile"/>: the bytes, uninterpreted. A round-trip through it says nothing about
/// the file's layout.
/// </summary>
public class Es1OpaqueFileTransformer : ByteTransformer<Es1OpaqueFile> {
	public override Es1OpaqueFile? Parse(byte[]? inputArray) =>
		inputArray == null || inputArray.Length <= 0 ? null : new Es1OpaqueFile { Bytes = (byte[])inputArray.Clone() };

	public override byte[]? Write(Es1OpaqueFile source) => (byte[])source.Bytes.Clone();
}
