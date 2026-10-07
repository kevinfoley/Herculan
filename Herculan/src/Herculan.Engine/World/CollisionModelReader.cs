using HercWorks.Core.Data.File.Dbsim;
using HercWorks.Core.Io.Transform.Dbsim;
using Herculan.Engine.Content;
using Herculan.Engine.Numerics;

namespace Herculan.Engine.World;

/// <summary>
/// The engine's entry point to every hit-sphere model in the simulation: it fetches the bytes, hands
/// them to the format's one parser (<see cref="HercColliderTransformer.ReadNodes"/> in
/// HercWorks.Core), and fills in the per-cluster bound the original derives at load.
///
/// <para><b>One format, two sources.</b> Structures read 65 records back to back out of
/// <c>dat\BASECOL.DAT</c> (see <see cref="BaseCollisionTable"/>); mechs and flyers each read one
/// whole file, <c>col\&lt;NAME&gt;.COL</c>, through <c>Collision_RegisterObject</c>
/// (<c>0040cd88</c>) — the mech from its constructor (<c>00415bb0</c>, into <c>mech+0x1f6</c>) and
/// the flyer from its type loader (<c>00422ed0</c>, into <c>flyerTypeRec+0x32</c>). Retail ships 22
/// <c>.COL</c> files, one per HERC plus <c>SKIMMER</c>.</para>
///
/// <para>The format itself is documented on <see cref="HercCollider"/>, which owns the only walk of
/// it. This class calls into Core rather than reading the bytes a second time.</para>
/// </summary>
public static class CollisionModelReader {
	/// <summary>VOL folder the per-type <c>.COL</c> files live in.</summary>
	public const string ResourceFolder = "col";

	/// <summary>The node index meaning "these spheres are in the object's own frame".</summary>
	public const short ObjectFrameNode = -1;

	/// <summary>
	/// One model out of <paramref name="bytes"/>, bounds included. Advances
	/// <paramref name="offset"/> past everything it read, which is what lets
	/// <see cref="BaseCollisionTable"/> walk 65 of them out of one stream.
	/// </summary>
	public static ColliderNode[] Read(byte[] bytes, ref int offset) =>
		WithBounds(new HercColliderTransformer().ReadNodes(bytes, ref offset));

	/// <summary>
	/// One type's <c>col\&lt;NAME&gt;.COL</c>, or an empty model when the install has no such file.
	/// Two of the three flyer names legitimately have none — retail ships a <c>.COL</c> for
	/// <c>SKIMMER</c> only — and an object with no model is simply not shootable, which is what the
	/// original ends up with too.
	/// </summary>
	public static ColliderNode[] Load(GameContent content, string typeName) {
		byte[]? bytes = content.Read(ResourceFolder, typeName + ".COL");
		if (bytes == null || bytes.Length < 2) {
			return Array.Empty<ColliderNode>();
		}

		int offset = 0;
		return Read(bytes, ref offset);
	}

	/// <summary>
	/// <c>Collision_ComputeBoundingSphere</c> (<c>0040c5d0</c>), over a freshly parsed model — the
	/// load-time step that gives every cluster the sphere the hit test tries before its children.
	/// See <see cref="ColliderCluster.Bound"/> for why it is filled here rather than in the parser.
	/// </summary>
	private static ColliderNode[] WithBounds(ColliderNode[] nodes) {
		for (int n = 0; n < nodes.Length; n++) {
			var clusters = nodes[n].Clusters ?? Array.Empty<ColliderCluster>();

			for (int c = 0; c < clusters.Length; c++) {
				clusters[c] = clusters[c] with { Bound = BoundOf(clusters[c].Spheres) };
			}

			nodes[n] = nodes[n] with { Clusters = clusters };
		}

		return nodes;
	}

	/// <summary>
	/// The bound itself: the AABB of the children each inflated by its own radius, its midpoint as
	/// the centre, and the fast magnitude of the half-extents as the radius. That approximation is
	/// part of the behaviour, not an implementation detail — it runs from about 8% under to about 9%
	/// over a true Euclidean radius depending on the box's proportions, so the bound is not exactly
	/// circumscribing.
	///
	/// <para>The original seeds the box at ±10000 and does not special-case an empty cluster, which
	/// leaves it inverted. Every mech <c>.COL</c> has empty clusters, its weapon mounts', and an
	/// empty hardpoint keeps one. Whatever the original's inverted bound does at the coarse test, the
	/// empty sphere list under it strikes nothing, so an empty bound here is a zero sphere that misses
	/// at the first test instead, with the same result.</para>
	/// </summary>
	internal static ColliderSphere BoundOf(ColliderSphere[]? spheres) {
		if (spheres == null || spheres.Length == 0) {
			return default;
		}

		short minX = short.MaxValue, minY = short.MaxValue, minZ = short.MaxValue;
		short maxX = short.MinValue, maxY = short.MinValue, maxZ = short.MinValue;

		foreach (var s in spheres) {
			minX = Math.Min(minX, (short)(s.X - s.Radius));
			minY = Math.Min(minY, (short)(s.Y - s.Radius));
			minZ = Math.Min(minZ, (short)(s.Z - s.Radius));
			maxX = Math.Max(maxX, (short)(s.X + s.Radius));
			maxY = Math.Max(maxY, (short)(s.Y + s.Radius));
			maxZ = Math.Max(maxZ, (short)(s.Z + s.Radius));
		}

		short centerX = (short)((maxX + minX) >> 1);
		short centerY = (short)((maxY + minY) >> 1);
		short centerZ = (short)((maxZ + minZ) >> 1);

		int radius = SimMath.FastMagnitude3D(
			(short)(maxX - centerX), (short)(maxY - centerY), (short)(maxZ - centerZ));

		return new ColliderSphere(centerX, centerY, centerZ, (short)radius);
	}
}
