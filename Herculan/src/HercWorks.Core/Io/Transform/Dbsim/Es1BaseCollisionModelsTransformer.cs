using HercWorks.Core.Data.File.Dat.Sim;
using HercWorks.Core.Data.File.Dbsim;

namespace HercWorks.Core.Io.Transform.Dbsim;

/// <summary>
/// Reads and writes ES1's <c>dat\BASECOL.DAT</c> (<see cref="Es1BaseCollisionModels"/>;
/// docs/es1/formats/bases-dat.md#the-collision-models): <see cref="HercColliderTransformer"/>'s walk, once a model, to the
/// end of the file. Writing a parsed file gives back its bytes.
///
/// <para>The file has no count: <c>Base_LoadResources</c> (<c>000046ac</c>) reads one model for each <c>BASES.DAT</c>
/// type. This reader reads models until the bytes run out, and throws <see cref="InvalidDataException"/> when the last
/// one runs past the end.</para>
/// </summary>
public class Es1BaseCollisionModelsTransformer : ByteTransformer<Es1BaseCollisionModels> {
	/// <exception cref="InvalidDataException">A model runs past the end of the file.</exception>
	public override Es1BaseCollisionModels? Parse(byte[]? inputArray) {
		if (inputArray == null || inputArray.Length <= 0) {
			return null;
		}

		var walk = new HercColliderTransformer();
		var models = new List<HercCollider>();
		int offset = 0;
		while (offset < inputArray.Length) {
			try {
				models.Add(new HercCollider { Nodes = walk.ReadNodes(inputArray, ref offset) });
			} catch (Exception e) when (e is ArgumentException or IndexOutOfRangeException) {
				throw new InvalidDataException($"Model {models.Count}, from {offset}, runs past the end.", e);
			}
		}

		return new Es1BaseCollisionModels { Models = [.. models] };
	}

	public override byte[]? Write(Es1BaseCollisionModels source) {
		var walk = new HercColliderTransformer();
		using var o = new MemoryStream();
		foreach (var model in source.Models) {
			o.Write(walk.Write(model)!);
		}

		return o.ToArray();
	}
}
