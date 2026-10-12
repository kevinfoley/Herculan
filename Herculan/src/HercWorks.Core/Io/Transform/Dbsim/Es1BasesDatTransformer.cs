using HercWorks.Core.Data.File.Dat.Sim;

namespace HercWorks.Core.Io.Transform.Dbsim;

/// <summary>
/// Reads and writes ES1's <c>dat\BASES.DAT</c> (<see cref="Es1BasesDat"/>; docs/es1/formats/bases-dat.md). Writing a parsed
/// file gives back its bytes.
///
/// <para>A file whose length is not its count's 2 + 54 × <i>n</i> throws <see cref="InvalidDataException"/>, so a file
/// this model would not give back whole is rejected.</para>
/// </summary>
public class Es1BasesDatTransformer : ByteTransformer<Es1BasesDat> {
	/// <exception cref="InvalidDataException">The file is not 2 + 54 × its count bytes.</exception>
	public override Es1BasesDat? Parse(byte[]? inputArray) {
		if (inputArray == null || inputArray.Length <= 0) {
			return null;
		}

		SetBytes(inputArray);
		short count = inputArray.Length >= 2 ? IndexShortLE() : (short)-1;
		if (count < 0 || inputArray.Length != 2 + count * Es1BaseTypeRecord.Length) {
			throw new InvalidDataException(
				$"The file is {inputArray.Length} bytes, not 2 + {Es1BaseTypeRecord.Length} × its count {count}.");
		}

		var types = new Es1BaseTypeRecord[count];
		for (int i = 0; i < count; i++) {
			types[i] = new Es1BaseTypeRecord {
				TypeIndex = IndexShortLE(),
				ShapeIndex = IndexShortLE(),
				HulkTypeIndex = IndexShortLE(),
				ShapeSet = IndexShortLE(),
				Fields08 = IndexShortLEArray(23),
			};
		}

		return new Es1BasesDat { Types = types };
	}

	public override byte[]? Write(Es1BasesDat source) {
		using var o = new MemoryStream();
		var w = new BinaryWriter(o);
		w.Write((short)source.Types.Length);
		foreach (var t in source.Types) {
			w.Write(t.TypeIndex);
			w.Write(t.ShapeIndex);
			w.Write(t.HulkTypeIndex);
			w.Write(t.ShapeSet);
			foreach (short value in t.Fields08) {
				w.Write(value);
			}
		}

		w.Flush();
		return o.ToArray();
	}
}
