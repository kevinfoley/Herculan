using HercWorks.Core.Data.File.Dbsim;
using HercWorks.Vol;

namespace HercWorks.Core.Io.Transform.Dbsim;

/// <summary>
/// Transforms byte[] data to and from .COL hit-sphere models — see <see cref="HercCollider"/> for
/// the format and the RE behind it.
///
/// <para>Both directions are byte-exact on all 22 retail files: the file has no header, and every
/// short of it is accounted for.</para>
///
/// <para>This is the only parser for the format. <see cref="ReadNodes"/> exposes the walk with a
/// caller-held offset, which is what lets the engine read one whole <c>.COL</c> and lets
/// <c>dat\BASECOL.DAT</c>'s 65 back-to-back structure records come out of one stream.</para>
/// </summary>
public class HercColliderTransformer : ByteTransformer<HercCollider> {
	public override HercCollider? Parse(byte[]? inputArray) {
		if (inputArray == null || inputArray.Length < 2) {
			return null;
		}

		int offset = 0;
		return new HercCollider {
			Nodes = ReadNodes(inputArray, ref offset),
		};
	}

	/// <summary>
	/// One model's worth of nodes — <c>Collision_LoadRecordArray</c> (<c>0040ccf8</c>) itself.
	/// Advances <paramref name="offset"/> past everything it read.
	/// </summary>
	public ColliderNode[] ReadNodes(byte[] bytes, ref int offset) {
		// Index directly rather than JumpTo, which silently no-ops an offset at end-of-buffer.
		SetBytes(bytes);
		Index = offset;

		var nodes = new ColliderNode[NonNegative(IndexShortLE())];
		for (int n = 0; n < nodes.Length; n++) {
			short nodeIndex = IndexShortLE();

			var clusters = new ColliderCluster[NonNegative(IndexShortLE())];
			for (int c = 0; c < clusters.Length; c++) {
				clusters[c] = ParseCluster();
			}

			nodes[n] = new ColliderNode(nodeIndex, clusters);
		}

		offset = Index;
		return nodes;
	}

	/// <summary>
	/// One cluster — <c>Collision_ReadCluster</c> (<c>0040cc14</c>) itself, which the sim
	/// <c>WEAPONS.DAT</c> reader runs too (see <see cref="WeaponsSimTransformer"/>). Advances
	/// <paramref name="offset"/> past everything it read.
	/// </summary>
	public ColliderCluster ReadCluster(byte[] bytes, ref int offset) {
		SetBytes(bytes);
		Index = offset;
		var cluster = ParseCluster();
		offset = Index;
		return cluster;
	}

	/// <summary>The write side of <see cref="ReadCluster"/>.</summary>
	public void WriteCluster(Stream outStream, ColliderCluster cluster) {
		var spheres = cluster.Spheres ?? Array.Empty<ColliderSphere>();

		Emit(outStream, WriteShortLE(cluster.ComponentIndex));
		Emit(outStream, WriteShortLE((short)spheres.Length));

		foreach (var sphere in spheres) {
			Emit(outStream, WriteShortLE(sphere.X));
			Emit(outStream, WriteShortLE(sphere.Y));
			Emit(outStream, WriteShortLE(sphere.Z));
			Emit(outStream, WriteShortLE(sphere.Radius));
		}
	}

	private ColliderCluster ParseCluster() {
		short componentIndex = IndexShortLE();

		// The original tests the count as `value & 0x1fff` but allocates and reads it
		// unmasked, so the mask is only a zero-test. Reproduced as written.
		short sphereCount = IndexShortLE();
		var spheres = new ColliderSphere[
			(sphereCount & SphereCountMask) != 0 ? NonNegative(sphereCount) : 0];

		for (int s = 0; s < spheres.Length; s++) {
			spheres[s] = new ColliderSphere(
				IndexShortLE(), IndexShortLE(), IndexShortLE(), IndexShortLE());
		}

		return new ColliderCluster(componentIndex, spheres);
	}

	public override byte[]? Write(HercCollider col) {
		using var outStream = new MemoryStream();
		var nodes = col.Nodes ?? Array.Empty<ColliderNode>();

		Emit(outStream, WriteShortLE((short)nodes.Length));
		foreach (var node in nodes) {
			var clusters = node.Clusters ?? Array.Empty<ColliderCluster>();

			Emit(outStream, WriteShortLE(node.NodeIndex));
			Emit(outStream, WriteShortLE((short)clusters.Length));

			foreach (var cluster in clusters) {
				WriteCluster(outStream, cluster);
			}
		}

		return outStream.ToArray();
	}

	/// <summary>See the read path — the mask is a zero-test, not a width.</summary>
	private const int SphereCountMask = 0x1fff;

	/// <summary>
	/// A negative count means a corrupt or hand-edited file. The original would allocate a negative
	/// size and fail; here it reads as empty so the rest of the walk still reports something.
	/// </summary>
	private static int NonNegative(short count) => count < 0 ? 0 : count;

	private static void Emit(Stream outArr, byte[] data) => outArr.Write(data, 0, data.Length);
}
