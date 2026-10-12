using HercWorks.Core.Data.File.Dat.Sim;

namespace HercWorks.Core.Io.Transform.Dbsim;

/// <summary>
/// Reads and writes ES1's <c>dat\BULLETS.DAT</c> (<see cref="Es1BulletData"/>;
/// docs/es1/formats/bullets-skimmer-dat.md#datbulletsdat). Writing a parsed file gives back its bytes.
///
/// <para>A file whose length is not its count's 2 + 10 × <i>n</i> throws <see cref="InvalidDataException"/>, so a file
/// this model would not give back whole is rejected.</para>
/// </summary>
public class Es1BulletDataTransformer : ByteTransformer<Es1BulletData> {
	private const int RecordLength = 10;

	/// <exception cref="InvalidDataException">The file is not 2 + 10 × its count bytes.</exception>
	public override Es1BulletData? Parse(byte[]? inputArray) {
		if (inputArray == null || inputArray.Length <= 0) {
			return null;
		}

		SetBytes(inputArray);
		short count = inputArray.Length >= 2 ? IndexShortLE() : (short)-1;
		if (count < 0 || inputArray.Length != 2 + count * RecordLength) {
			throw new InvalidDataException($"The file is {inputArray.Length} bytes, not 2 + {RecordLength} × its count {count}.");
		}

		var entries = new Es1BulletType[count];
		for (int i = 0; i < count; i++) {
			entries[i] = new Es1BulletType {
				ModelId = IndexShortLE(),
				Lifetime = IndexShortLE(),
				ClipRadius = IndexShortLE(),
				FrameInterval = IndexShortLE(),
				FireSoundId = IndexShortLE(),
			};
		}

		return new Es1BulletData { Entries = entries };
	}

	public override byte[]? Write(Es1BulletData source) {
		using var o = new MemoryStream();
		var w = new BinaryWriter(o);
		w.Write((short)source.Entries.Length);
		foreach (var e in source.Entries) {
			w.Write(e.ModelId);
			w.Write(e.Lifetime);
			w.Write(e.ClipRadius);
			w.Write(e.FrameInterval);
			w.Write(e.FireSoundId);
		}

		w.Flush();
		return o.ToArray();
	}
}
