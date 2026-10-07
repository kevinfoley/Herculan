using System.Text;
using HercWorks.Core.Data.File;

namespace HercWorks.Core.Io.Transform.Common;

/// <summary>Reads a <c>.NAM</c> unit-type name list (<see cref="NameList"/>).</summary>
public class NameListTransformer : ByteTransformer<NameList> {
	/// <summary>
	/// Splits on NUL. The retail files end with a stray newline after the last terminator, which
	/// leaves a whitespace-only fragment; fragments that trim to nothing are dropped.
	/// </summary>
	public override NameList? Parse(byte[]? inputArray) {
		if (inputArray == null) {
			return null;
		}

		var names = new List<string>();
		int start = 0;
		for (int i = 0; i < inputArray.Length; i++) {
			if (inputArray[i] != 0) {
				continue;
			}

			string name = Encoding.ASCII.GetString(inputArray, start, i - start).Trim();
			if (name.Length > 0) {
				names.Add(name.ToUpperInvariant());
			}
			start = i + 1;
		}

		return new NameList { Names = names.ToArray() };
	}

	public override byte[]? Write(NameList source) =>
		throw new NotSupportedException("NameListTransformer is read-only: the parse drops the files' trailing newline and their case.");
}
