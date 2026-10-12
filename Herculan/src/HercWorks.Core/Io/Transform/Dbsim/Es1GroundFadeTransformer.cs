using HercWorks.Core.Data.File.Dbsim;

namespace HercWorks.Core.Io.Transform.Dbsim;

/// <summary>
/// Reads and writes ES1's ground fade distances (<see cref="Es1GroundFade"/>) as <c>GndTex_Load</c>
/// (<c>ES1DBSIM 00028de8</c>) reads them (docs/es1/rendering/ground-and-sky.md#loading-the-ground-texture). Writing a
/// parsed file gives back its bytes.
///
/// <para>A negative count, or a file that is not exactly the count's length, throws <see cref="InvalidDataException"/>.
/// The retail loader tests neither.</para>
/// </summary>
public class Es1GroundFadeTransformer : ByteTransformer<Es1GroundFade> {
	/// <exception cref="InvalidDataException">The count does not match the file's length.</exception>
	public override Es1GroundFade? Parse(byte[]? inputArray) {
		if (inputArray == null || inputArray.Length <= 0) {
			return null;
		}

		if (inputArray.Length < 4) {
			throw new InvalidDataException($"The file is {inputArray.Length} bytes, too short for its count.");
		}

		SetBytes(inputArray);
		int count = IndexIntLE();
		if (count < 0 || inputArray.Length != 4 + count * 4L) {
			throw new InvalidDataException($"A count of {count} does not fit a file of {inputArray.Length} bytes.");
		}

		return new Es1GroundFade { Distances = IndexIntLEArray(count) };
	}

	public override byte[]? Write(Es1GroundFade source) {
		using var o = new MemoryStream();
		var w = new BinaryWriter(o);
		w.Write(source.Distances.Length);
		foreach (int distance in source.Distances) {
			w.Write(distance);
		}

		w.Flush();
		return o.ToArray();
	}
}
