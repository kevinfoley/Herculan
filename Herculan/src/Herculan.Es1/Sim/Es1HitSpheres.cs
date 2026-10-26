using HercWorks.Core.Data.File.Dbsim;
using HercWorks.Core.Io.Transform.Dbsim;
using Herculan.Engine.Numerics;

namespace Herculan.Es1.Sim;

/// <summary>
/// The sphere tests of a machine's <c>.COL</c> model (docs/es1/simulation/component-damage.md#the-struck-component--mech_selectstruckcomponent-00009064):
/// which component a ray strikes, and where a component's cluster is. A test's state, retail's globals
/// <c>Collision_RayRemaining</c> (<c>000bc2a6</c>), <c>Collision_RayClearance</c> (<c>000bc2aa</c>),
/// <c>Collision_NodeToMuzzleTransform</c> (<c>000bc2a2</c>) and the hit, is a local of each pick here.
/// </summary>
public static class Es1HitSpheres {
	/// <summary>The off-axis magnitude a sphere test passes only below.</summary>
	public const int MagnitudeLimit = 0x7fff;

	/// <summary>
	/// <c>Mech_SelectStruckComponent</c> (<c>00009064</c>)(<paramref name="model"/>, <paramref name="occupied"/>,
	/// <paramref name="toMuzzle"/>, <paramref name="ray"/>, <paramref name="machine"/>): the ray's remaining length and
	/// clearance taken from <paramref name="ray"/>, each node's clusters whose component is occupied tested in turn
	/// (<c>Mech_ComponentGeometryTest_Candidate</c>, <c>00008f84</c>), each hit kept, so the last, which the cuts make the
	/// nearest, stands. Answers its distance, the remaining length + 1, and its component; null for a miss.
	/// </summary>
	public static (int Distance, short Component)? SelectStruckComponent(ColliderNode[] model, bool[] occupied,
			in Transform3 toMuzzle, Es1Ray ray, Es1Mech machine) {
		var test = new RayTest((uint)ray.Length, ray.Clearance);
		int distance = 0;
		short component = -1;
		foreach (var node in model) {
			// Mech_ComponentGeometryTest_Candidate: the node's frame, the object-to-muzzle frame itself for a negative part
			// id, otherwise the part's node composed with it. Retail passes a null node for a part the shape lacks, which
			// Transform_Concat reads from address 0; the frame alone here, an engine guard.
			var frame = node.NodeIndex < 0
				? toMuzzle
				: Transform3.Concat(machine.PartNodeTransform(node.NodeIndex) ?? Transform3.Identity, toMuzzle);
			foreach (var cluster in node.Clusters ?? []) {
				// Retail reads a component past the flags array past it; such a cluster is skipped here.
				short index = cluster.ComponentIndex;
				if (index < 0 || index >= occupied.Length || !occupied[index]) {
					continue;
				}

				int hit = test.ClusterTest(cluster, frame);
				if (hit != 0) {
					distance = hit;
					component = index;
				}
			}
		}

		return distance == 0 ? null : (distance, component);
	}

	/// <summary>
	/// <c>Collision_FindClusterForComponent</c> (<c>0000916c</c>)(<paramref name="model"/>, out part,
	/// <paramref name="component"/>): the first cluster, node by node, whose component is <paramref name="component"/>
	/// (<c>Collision_FindClusterInNode</c>, <c>000090fc</c>) and its node's part id; null and 0 when none is.
	/// </summary>
	public static (ColliderCluster? Cluster, short Part) FindClusterForComponent(ColliderNode[] model, short component) {
		foreach (var node in model) {
			foreach (var cluster in node.Clusters ?? []) {
				if (cluster.ComponentIndex == component) {
					return (cluster, node.NodeIndex);
				}
			}
		}

		return (null, 0);
	}

	/// <summary>The box <see cref="BoundOf"/> starts from: its least corner at this, its greatest at the negation.</summary>
	public const short BoundSeed = 10000;

	/// <summary>
	/// A <c>.COL</c> file's model, read by <c>HercWorks.Core</c> (<c>Collision_LoadRecordArray</c>, <c>000094f0</c>), with
	/// each cluster's bound (<c>Collision_ComputeAllBoundingSpheres</c>, <c>000094a0</c>, <see cref="BoundOf"/>); empty
	/// for no bytes.
	/// </summary>
	public static ColliderNode[] ReadModel(byte[]? bytes) {
		if (bytes == null || bytes.Length < 2) {
			return [];
		}

		int offset = 0;
		var nodes = new HercColliderTransformer().ReadNodes(bytes, ref offset);
		for (int n = 0; n < nodes.Length; n++) {
			nodes[n] = WithBounds(nodes[n]);
		}

		return nodes;
	}

	/// <summary><c>Collision_ComputeAllBoundingSpheres</c>(<paramref name="node"/>): each cluster's bound, <see cref="BoundOf"/>.</summary>
	private static ColliderNode WithBounds(ColliderNode node) =>
		node with { Clusters = (node.Clusters ?? []).Select(c => c with { Bound = BoundOf(c.Spheres ?? []) }).ToArray() };

	/// <summary>
	/// <c>Collision_ComputeBoundingSphere</c> (<c>00008b8c</c>, docs/es1/simulation/component-damage.md#the-bounding-spheres):
	/// a box from (<see cref="BoundSeed"/>, …) to (−<see cref="BoundSeed"/>, …) widened by each sphere's center ± its radius,
	/// kept as <c>int16</c>; its middle, each sum an <c>int16</c> halved by an arithmetic shift, as the center, and
	/// <c>Math_FastMagnitude3D</c> of the greatest corner less the center as the radius. An empty cluster's box stays
	/// inverted: center 0, radius that of (−10000, −10000, −10000).
	/// </summary>
	public static ColliderSphere BoundOf(ColliderSphere[] spheres) {
		short minX = BoundSeed, minY = BoundSeed, minZ = BoundSeed;
		short maxX = -BoundSeed, maxY = -BoundSeed, maxZ = -BoundSeed;
		foreach (var s in spheres) {
			maxX = s.X + s.Radius >= maxX ? unchecked((short)(s.X + s.Radius)) : maxX;
			maxY = s.Y + s.Radius >= maxY ? unchecked((short)(s.Y + s.Radius)) : maxY;
			maxZ = s.Z + s.Radius >= maxZ ? unchecked((short)(s.Z + s.Radius)) : maxZ;
			minX = s.X - s.Radius <= minX ? unchecked((short)(s.X - s.Radius)) : minX;
			minY = s.Y - s.Radius <= minY ? unchecked((short)(s.Y - s.Radius)) : minY;
			minZ = s.Z - s.Radius <= minZ ? unchecked((short)(s.Z - s.Radius)) : minZ;
		}

		short x = (short)(unchecked((short)(minX + maxX)) >> 1);
		short y = (short)(unchecked((short)(minY + maxY)) >> 1);
		short z = (short)(unchecked((short)(minZ + maxZ)) >> 1);
		int radius = SimMath.FastMagnitude3D(unchecked((short)(maxX - x)), unchecked((short)(maxY - y)), unchecked((short)(maxZ - z)));
		return new ColliderSphere(x, y, z, unchecked((short)radius));
	}

	/// <summary>
	/// <c>Collision_ReplaceClusters</c> (<c>000096e8</c>) with one template: in the first node holding a cluster of
	/// <paramref name="component"/>, its first such cluster takes <paramref name="spheres"/> with <paramref name="offset"/>
	/// added to each center as <c>int16</c> (<c>Collision_CopyClusterSpheres</c>, <c>00008eb0</c>), and the node's bounds
	/// are worked again (docs/es1/formats/dmg-damage-file.md#the-loadouts-join). The model is copied where it changes, so
	/// a type's shared model is left as it was; one with no such cluster comes back unchanged.
	/// </summary>
	public static ColliderNode[] ReplaceCluster(ColliderNode[] model, short component, ColliderSphere[] spheres, Vec3i offset) {
		for (int n = 0; n < model.Length; n++) {
			var clusters = model[n].Clusters ?? [];
			int c = Array.FindIndex(clusters, cluster => cluster.ComponentIndex == component);
			if (c < 0) {
				continue;
			}

			var replaced = (ColliderCluster[])clusters.Clone();
			replaced[c] = replaced[c] with {
				Spheres = spheres.Select(s => new ColliderSphere(unchecked((short)(s.X + offset.X)), unchecked((short)(s.Y + offset.Y)),
					unchecked((short)(s.Z + offset.Z)), s.Radius)).ToArray(),
			};
			var copy = (ColliderNode[])model.Clone();
			copy[n] = WithBounds(model[n] with { Clusters = replaced });
			return copy;
		}

		return model;
	}

	/// <summary>One pick's remaining length, which a struck sphere cuts, and the shot's clearance.</summary>
	private sealed class RayTest(uint remaining, short clearance) {
		private uint _remaining = remaining;

		/// <summary>
		/// <c>Collision_ClusterTest</c> (<c>00008f54</c>): the bound (<c>Collision_ClusterBoundTest</c>, <c>00008a88</c>,
		/// <see cref="RaySphereTest"/> of its center without the cut) and, when it passes, each sphere through
		/// <paramref name="frame"/> with the cut (<c>Collision_ClusterSphereTest</c>, <c>00008ab8</c>); the remaining length
		/// + 1 when a sphere passed, otherwise 0.
		/// </summary>
		public int ClusterTest(in ColliderCluster cluster, in Transform3 frame) {
			var bound = cluster.Bound;
			if (!RaySphereTest(false, bound.Radius, frame.TransformPoint(bound.X, bound.Y, bound.Z))) {
				return 0;
			}

			bool struck = false;
			foreach (var sphere in cluster.Spheres ?? []) {
				if (RaySphereTest(true, sphere.Radius, frame.TransformPoint(sphere.X, sphere.Y, sphere.Z))) {
					struck = true;
				}
			}

			return struck ? (int)(_remaining + 1) : 0;
		}

		/// <summary>
		/// <c>Collision_RaySphereTest</c> (<c>000089d4</c>)(<paramref name="cut"/>, <paramref name="radius"/>,
		/// <paramref name="point"/>), the point in muzzle space with the ray along +Y: with <i>r</i> the clearance plus the
		/// radius as an <c>int16</c> and <i>m</i> <c>Math_FastMagnitude2D</c>(X, Z), a pass needs Y below the remaining
		/// length, unsigned, <i>m</i> below <see cref="MagnitudeLimit"/>, and the low 16 bits of <i>m</i> + <i>r</i> below
		/// those of 2<i>r</i>, unsigned. With <paramref name="cut"/> a pass cuts the remaining length to Y − (<i>r</i> −
		/// <i>m</i>), or 0 when that is not positive.
		/// </summary>
		private bool RaySphereTest(bool cut, short radius, Vec3i point) {
			if ((uint)point.Y >= _remaining) {
				return false;
			}

			int m = SimMath.FastMagnitude2D(point.X, point.Z);
			short reach = unchecked((short)(clearance + radius));
			if (m >= MagnitudeLimit || unchecked((ushort)((short)m + reach)) >= unchecked((ushort)(reach * 2))) {
				return false;
			}

			if (cut) {
				_remaining = reach - m < point.Y ? (uint)(point.Y - (reach - m)) : 0;
			}

			return true;
		}
	}
}
