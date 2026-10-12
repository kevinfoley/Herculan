using HercWorks.Core.Data.File.Dbsim;

namespace HercWorks.Core.Io.Transform.Dbsim;

/// <summary>
/// Reads and writes ES1's ground texture (<see cref="Es1GroundTexture"/>; docs/es1/rendering/ground-and-sky.md#loading-the-ground-texture).
/// Writing a parsed file gives back its bytes.
///
/// <para>A file of any length but 65,536 bytes throws <see cref="InvalidDataException"/>: retail reads the first 65,536
/// bytes of whatever it opens, and both retail files are exactly that long.</para>
/// </summary>
public class Es1GroundTextureTransformer : ByteTransformer<Es1GroundTexture> {
	private const int FileLength = Es1GroundTexture.Size * Es1GroundTexture.Size;

	/// <exception cref="InvalidDataException">The file is not 65,536 bytes.</exception>
	public override Es1GroundTexture? Parse(byte[]? inputArray) {
		if (inputArray == null || inputArray.Length <= 0) {
			return null;
		}

		if (inputArray.Length != FileLength) {
			throw new InvalidDataException($"The file is {inputArray.Length} bytes, not {FileLength}.");
		}

		return new Es1GroundTexture { Texels = (byte[])inputArray.Clone() };
	}

	public override byte[]? Write(Es1GroundTexture source) => (byte[])source.Texels.Clone();
}
