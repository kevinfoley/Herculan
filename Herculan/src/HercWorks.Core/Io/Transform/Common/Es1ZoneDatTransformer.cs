using HercWorks.Core.Data.File.Dat.Sim;

namespace HercWorks.Core.Io.Transform.Common;

/// <summary>
/// Reads and writes ES1's <c>dat\zoneNNNN</c> (<see cref="Es1ZoneDat"/>): the word at 0x00, the record count at
/// 0x02 and the 20-byte records (docs/es1/formats/zone-terrain.md#the-layout). Writing a parsed file gives back its
/// bytes.
///
/// <para>A file whose length is not 4 + 20 × its count throws <see cref="InvalidDataException"/>, so a file this
/// model would not give back whole is rejected. The retail loaders test none of this.</para>
/// </summary>
public class Es1ZoneDatTransformer : ByteTransformer<Es1ZoneDat> {
	private const int HeaderSize = 4;
	private const int RecordSize = 20;

	/// <exception cref="InvalidDataException">The file is not 4 + 20 × its count bytes.</exception>
	public override Es1ZoneDat? Parse(byte[]? inputArray) {
		if (inputArray == null || inputArray.Length <= 0) {
			return null;
		}

		if (inputArray.Length < HeaderSize) {
			throw new InvalidDataException($"The file ends at 0x{inputArray.Length:x}, inside the header.");
		}

		SetBytes(inputArray);
		var data = new Es1ZoneDat { Word00 = IndexShortLE() };
		int count = IndexShortLE();
		if (count < 0 || HeaderSize + (long)count * RecordSize != inputArray.Length) {
			throw new InvalidDataException(
				$"The count at 0x2, {count}, of {RecordSize}-byte records does not make the file's {inputArray.Length} bytes.");
		}

		data.Shapes = new Es1ZoneShape[count];
		for (int i = 0; i < count; i++) {
			data.Shapes[i] = new Es1ZoneShape {
				ShapeIndex = IndexShortLE(),
				X = IndexIntLE(),
				Y = IndexIntLE(),
				Z = IndexIntLE(),
				Angles = IndexShortLEArray(3),
			};
		}

		return data;
	}

	public override byte[]? Write(Es1ZoneDat source) {
		using var o = new MemoryStream();
		var w = new BinaryWriter(o);

		w.Write(source.Word00);
		w.Write((short)source.Shapes.Length);
		foreach (var shape in source.Shapes) {
			w.Write(shape.ShapeIndex);
			w.Write(shape.X);
			w.Write(shape.Y);
			w.Write(shape.Z);
			foreach (short angle in shape.Angles) {
				w.Write(angle);
			}
		}

		w.Flush();
		return o.ToArray();
	}
}
