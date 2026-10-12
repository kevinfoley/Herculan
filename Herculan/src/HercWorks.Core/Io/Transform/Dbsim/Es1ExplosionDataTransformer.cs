using HercWorks.Core.Data.File.Dat.Sim;

namespace HercWorks.Core.Io.Transform.Dbsim;

/// <summary>
/// Reads and writes ES1's <c>dat\EXPLOS.DAT</c> (<see cref="Es1ExplosionData"/>; docs/es1/formats/explos-dat.md). Writing a
/// parsed file gives back its bytes.
///
/// <para>A file whose length is not its count's 2 + 44 × <i>n</i> throws <see cref="InvalidDataException"/>, so a file
/// this model would not give back whole is rejected.</para>
/// </summary>
public class Es1ExplosionDataTransformer : ByteTransformer<Es1ExplosionData> {
	private const int RowLength = 0x2c;

	/// <exception cref="InvalidDataException">The file is not 2 + 44 × its count bytes.</exception>
	public override Es1ExplosionData? Parse(byte[]? inputArray) {
		if (inputArray == null || inputArray.Length <= 0) {
			return null;
		}

		SetBytes(inputArray);
		short count = inputArray.Length >= 2 ? IndexShortLE() : (short)-1;
		if (count < 0 || inputArray.Length != 2 + count * RowLength) {
			throw new InvalidDataException($"The file is {inputArray.Length} bytes, not 2 + {RowLength} × its count {count}.");
		}

		var types = new Es1ExplosionTypeEntry[count];
		for (int i = 0; i < count; i++) {
			types[i] = new Es1ExplosionTypeEntry {
				ShapeIndex = IndexShortLE(),
				AnimSequence = IndexShortLE(),
				TextureBankIndex = IndexShortLE(),
				FrameInterval = IndexShortLE(),
				GroundShape = IndexShortLE(),
				LightMode = IndexShortLE(),
				FrameIntensity = IndexShortLEArray(ExplosionTypeEntry.FrameIntensityCount),
				ProximityRadius = IndexIntLE(),
				SoundId = IndexShortLE(),
				ObjectClass = IndexShortLE(),
			};
		}

		return new Es1ExplosionData { Types = types };
	}

	public override byte[]? Write(Es1ExplosionData source) {
		using var o = new MemoryStream();
		var w = new BinaryWriter(o);
		w.Write((short)source.Types.Length);
		foreach (var t in source.Types) {
			foreach (short value in new[] { t.ShapeIndex, t.AnimSequence, t.TextureBankIndex, t.FrameInterval, t.GroundShape, t.LightMode }) {
				w.Write(value);
			}

			foreach (short value in t.FrameIntensity) {
				w.Write(value);
			}

			w.Write(t.ProximityRadius);
			w.Write(t.SoundId);
			w.Write(t.ObjectClass);
		}

		w.Flush();
		return o.ToArray();
	}
}
