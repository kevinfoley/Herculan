using HercWorks.Core.Data.File.Dat.Sim;

namespace HercWorks.Core.Io.Transform.Dbsim;

/// <summary>
/// Reads and writes ES1's theater shape tables, <c>dat\DESERT.DAT</c> and its three siblings
/// (<see cref="Es1TheaterShapeLines"/>; docs/es1/formats/zone-terrain.md#dattheater-the-shapes-dividing-lines). Writing a
/// parsed file gives back its bytes.
///
/// <para>The file has no count: <c>Terrain_LoadZoneShapes</c> (<c>00020238</c>) reads as many records as the theater's
/// library has root shapes. A file whose length is not a multiple of 24 throws <see cref="InvalidDataException"/>.</para>
/// </summary>
public class Es1TheaterShapeLinesTransformer : ByteTransformer<Es1TheaterShapeLines> {
	/// <exception cref="InvalidDataException">The file's length is not a multiple of 24.</exception>
	public override Es1TheaterShapeLines? Parse(byte[]? inputArray) {
		if (inputArray == null || inputArray.Length <= 0) {
			return null;
		}

		if (inputArray.Length % Es1ShapeLine.Length != 0) {
			throw new InvalidDataException($"The file is {inputArray.Length} bytes, not a multiple of {Es1ShapeLine.Length}.");
		}

		SetBytes(inputArray);
		var lines = new Es1ShapeLine[inputArray.Length / Es1ShapeLine.Length];
		for (int i = 0; i < lines.Length; i++) {
			lines[i] = new Es1ShapeLine {
				StartX = IndexIntLE(),
				StartY = IndexIntLE(),
				StartZ = IndexIntLE(),
				EndX = IndexIntLE(),
				EndY = IndexIntLE(),
				EndZ = IndexIntLE(),
			};
		}

		return new Es1TheaterShapeLines { Lines = lines };
	}

	public override byte[]? Write(Es1TheaterShapeLines source) {
		using var o = new MemoryStream();
		var w = new BinaryWriter(o);
		foreach (var line in source.Lines) {
			w.Write(line.StartX);
			w.Write(line.StartY);
			w.Write(line.StartZ);
			w.Write(line.EndX);
			w.Write(line.EndY);
			w.Write(line.EndZ);
		}

		w.Flush();
		return o.ToArray();
	}
}
