using HercWorks.Core.Data.File.Bnd;

namespace HercWorks.Core.Io.Transform.Bnd;

/// <summary>
/// Reads and writes ES1's <c>bnd\CAM.BND</c> (<see cref="Es1Cam"/>; docs/es1/formats/bnd-notes.md#bndcambnd). Writing a
/// parsed file gives back its bytes. A file of any length but 20 bytes throws <see cref="InvalidDataException"/>.
/// </summary>
public class Es1CamTransformer : ByteTransformer<Es1Cam> {
	private const int FileLength = 20;

	/// <exception cref="InvalidDataException">The file is not 20 bytes.</exception>
	public override Es1Cam? Parse(byte[]? inputArray) {
		if (inputArray == null || inputArray.Length <= 0) {
			return null;
		}

		if (inputArray.Length != FileLength) {
			throw new InvalidDataException($"The file is {inputArray.Length} bytes, not {FileLength}.");
		}

		SetBytes(inputArray);
		return new Es1Cam {
			Stamp = (uint)IndexIntLE(),
			MinOrbitDistance = IndexShortLE(),
			MaxOrbitDistance = IndexShortLE(),
			AngleRateScale = IndexShortLE(),
			AngleRateStep = IndexShortLE(),
			ZoomRateScale = IndexShortLE(),
			ZoomRateStep = IndexShortLE(),
			PitchLimit = IndexShortLE(),
			MinEyeHeight = IndexShortLE(),
		};
	}

	public override byte[]? Write(Es1Cam source) {
		using var output = new MemoryStream();
		void Emit(byte[] bytes) => output.Write(bytes, 0, bytes.Length);

		Emit(WriteIntLE((int)source.Stamp));
		foreach (short value in new[] {
			source.MinOrbitDistance, source.MaxOrbitDistance, source.AngleRateScale, source.AngleRateStep,
			source.ZoomRateScale, source.ZoomRateStep, source.PitchLimit, source.MinEyeHeight,
		}) {
			Emit(WriteShortLE(value));
		}

		return output.ToArray();
	}
}
