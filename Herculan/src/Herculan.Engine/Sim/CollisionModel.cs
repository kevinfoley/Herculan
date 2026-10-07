using HercWorks.Core.Data.File.Dbsim;
using Herculan.Engine.Numerics;
using Herculan.Engine.World;

namespace Herculan.Engine.Sim;

/// <summary>
/// The ray-versus-sphere-model hit test — <c>Mech_SelectStruckComponent</c> (<c>0040c9d4</c>) and
/// the three functions under it. Given an object's collision model and a shot, it answers <b>which
/// component was struck and how far along the ray</b>, which is what turns a hit into damage to a
/// specific part of a specific building.
///
/// <para>Despite the name it is not mech-specific: structures reach it through
/// <c>Base_DirectFireHitTest</c> (<c>00405038</c>) and flyers through
/// <c>Flyer_DirectFireHitTest</c> (<c>00421c8c</c>), all three passing a model read by
/// <see cref="CollisionModelReader"/> — a structure's out of <see cref="BaseCollisionTable"/>, a
/// mech's or a flyer's out of its own <c>col\&lt;NAME&gt;.COL</c>.</para>
///
/// <para><b>The ray shortens as the test runs.</b> Every sphere that is struck clips the working
/// distance to its own entry point, and later spheres are tested against the clipped ray, so the
/// result is the nearest cluster rather than the first. The original does this through three
/// globals (<c>004a9894</c>, <c>004a9898</c>, <c>004a989c</c>/<c>004a98a0</c>) because only one hit
/// test runs at a time; here it is a local, which is the same thing without the aliasing.</para>
/// </summary>
public static class CollisionModel {
	/// <summary>What a hit test found: how far along the ray, and which component owns the geometry.</summary>
	/// <param name="Distance">
	/// Distance along the ray to the entry point, <b>plus one</b> — the original's own
	/// <c>Collision_ClusterSphereTest</c> (<c>0040c524</c>) returns <c>distance + 1</c> so that a hit at zero range is still
	/// distinguishable from a miss, and the caller adds another one on top.
	/// </param>
	/// <param name="ComponentIndex">Which of the type's components was struck.</param>
	public readonly record struct Hit(int Distance, short ComponentIndex);

	/// <summary>
	/// Runs the model against a shot.
	/// </summary>
	/// <param name="model">The object's collision model, one entry per shape node.</param>
	/// <param name="toMuzzleSpace">
	/// The object's own frame expressed in the shot's — model space in, muzzle space out, where the
	/// ray is the Y axis.
	/// </param>
	/// <param name="distance">The ray's remaining length.</param>
	/// <param name="clearance">The shot's clearance — see <see cref="WeaponShot.Clearance"/>.</param>
	/// <param name="componentAlive">
	/// Whether one of the type's components is still standing. A destroyed component's spheres are
	/// skipped outright, so an object that has lost a section stops stopping shots through it.
	/// </param>
	/// <param name="nodeFrame">
	/// Where one of the shape's nodes stands right now, relative to the object's own frame, or null
	/// for "this shape has no such node" — which the original answers with an identity transform
	/// (<c>DAT_006c572c</c>) rather than by skipping the cluster. Only consulted for a cluster whose
	/// <see cref="ColliderNode.NodeIndex"/> is non-negative; passing no resolver at all leaves those
	/// clusters in the object frame, which is the same answer for an object with no animated shape.
	/// <see cref="SimObject.NodeFrame"/> is the resolver.
	///
	/// <para><b>This is the whole of a HERC's hit geometry.</b> A mech <c>.COL</c> places every one
	/// of its clusters on a node — SPIDER has thirteen, PITBULL ten — with spheres of radius 40 to
	/// 600 world units sitting in node-local space, so the hit volume walks with the legs and swings
	/// with the torso. A structure's model is the opposite: its body is in the object frame, and only
	/// six animated types put a cluster on a node.</para>
	/// </param>
	/// <returns>The nearest component struck, or null for a miss.</returns>
	public static Hit? Test(ColliderNode[] model, in Transform3 toMuzzleSpace, int distance,
			int clearance, Func<int, bool> componentAlive, Func<short, Transform3?>? nodeFrame = null) {
		Hit? best = null;

		foreach (var node in model) {
			var frame = toMuzzleSpace;

			if (node.NodeIndex != CollisionModelReader.ObjectFrameNode) {
				// The original composes the node's posed transform with the object-to-muzzle one, so
				// the spheres are read in the node's frame and land wherever the animation has put it.
				// A part the shape does not have reads as identity, which is the object frame.
				if (nodeFrame?.Invoke(node.NodeIndex) is { } posed) {
					frame = Transform3.Concat(posed, toMuzzleSpace);
				}
			}

			foreach (var cluster in node.Clusters) {
				if (!componentAlive(cluster.ComponentIndex)) {
					continue;
				}

				// Collision_ClusterTest (0040c8c8): the cluster's own bound first, and the spheres only if it passes.
				if (!BoundStruck(cluster.Bound, frame, distance, clearance)) {
					continue;
				}

				bool struck = false;
				foreach (var sphere in cluster.Spheres) {
					var center = frame.TransformPoint(sphere.X, sphere.Y, sphere.Z);
					if (SphereStruck(center, sphere.Radius, clearance, ref distance)) {
						struck = true;
					}
				}

				if (struck) {
					best = new Hit(distance + 1, cluster.ComponentIndex);
				}
			}
		}

		return best;
	}

	/// <summary>
	/// <c>Collision_ReplaceClusters</c> (<c>0040cec0</c>) with the count of one
	/// <c>Mech_ConfigureLoadout</c> passes it: the first cluster, node by node, whose component is
	/// <paramref name="componentIndex"/> takes copies of <paramref name="spheres"/>, each centre moved by
	/// <paramref name="offset"/> in 16 bits and its radius kept (<c>Collision_CopyClusterSpheres</c>,
	/// <c>0040c834</c>), and its bound is derived again. A model with no such cluster comes back as it
	/// went in. See docs/retail/formats/collision-spheres.md#a-fitted-weapon-brings-its-own-spheres.
	///
	/// <para>The model passed in is the type's, shared by every object of it, so the node and cluster
	/// arrays the replacement touches are copied rather than written.</para>
	/// </summary>
	public static ColliderNode[] ReplaceCluster(ColliderNode[] model, short componentIndex,
			ColliderSphere[] spheres, Vec3i offset) {
		for (int n = 0; n < model.Length; n++) {
			var clusters = model[n].Clusters;
			for (int c = 0; c < clusters.Length; c++) {
				if (clusters[c].ComponentIndex != componentIndex) {
					continue;
				}

				var moved = spheres
					.Select(s => new ColliderSphere(
						(short)(s.X + offset.X), (short)(s.Y + offset.Y), (short)(s.Z + offset.Z), s.Radius))
					.ToArray();

				var replaced = (ColliderCluster[])clusters.Clone();
				replaced[c] = new ColliderCluster(componentIndex, moved, CollisionModelReader.BoundOf(moved));

				var copy = (ColliderNode[])model.Clone();
				copy[n] = model[n] with { Clusters = replaced };
				return copy;
			}
		}

		return model;
	}

	/// <summary>
	/// <c>Collision_ClusterBoundTest</c> (<c>0040c4c4</c>) — the cluster bound's coarse test, which is the same
	/// ray-versus-vertical-cylinder shape every hit test in the simulation uses: the centre has to
	/// be in front of the muzzle and inside the ray's length, and its distance off the ray axis
	/// under the bound's radius. The length comparison is unsigned, which is what rejects anything
	/// behind the muzzle without a second test.
	/// </summary>
	private static bool BoundStruck(ColliderSphere bound, in Transform3 toMuzzleSpace,
			int distance, int clearance) {
		var center = toMuzzleSpace.TransformPoint(bound.X, bound.Y, bound.Z);
		return (uint)center.Y < (uint)(bound.Radius + distance)
			&& SimMath.FastMagnitude2D(center.X, center.Z) < bound.Radius + clearance;
	}

	/// <summary>
	/// <c>Collision_RaySphereTest</c> (<c>0040c428</c>) — one sphere against the ray, clipping <paramref name="distance"/> to the
	/// entry point on a hit.
	///
	/// <para>The entry point is the original's linearisation, <c>alongAxis - (radius - offAxis)</c>,
	/// floored at zero — the same construction the mech's shield test uses, and just as approximate:
	/// it treats the sphere as a cylinder whose front face is a cone. The 16-bit comparison it is
	/// written as is kept because the doubled radius can overflow a signed short, and the original
	/// relies on the wrap.</para>
	/// </summary>
	private static bool SphereStruck(Vec3i center, short radius, int clearance, ref int distance) {
		if ((uint)center.Y >= (uint)distance) {
			return false;
		}

		int offAxis = SimMath.FastMagnitude2D(center.X, center.Z);
		short reach = (short)(clearance + radius);
		if (offAxis >= 0x7fff || (ushort)(unchecked((short)offAxis) + reach) >= (ushort)(reach * 2)) {
			return false;
		}

		int entry = reach - offAxis;
		distance = entry < center.Y ? center.Y - entry : 0;
		return true;
	}
}
