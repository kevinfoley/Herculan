using System.Numerics;
using Herculan.Engine.Anim;
using Herculan.Engine.Numerics;

namespace Herculan.Engine.Render;

/// <summary>
/// Turns a sampled <see cref="SkeletonPose"/> into the line list <see cref="WireframeRenderer"/>
/// draws — bones between a node and its parent, plus a small axis cross at each node so leaves and
/// unparented roots are visible at all.
///
/// <para>Debug geometry, not a port. See <see cref="SkeletonPose"/> for why it exists.</para>
/// </summary>
public static class SkeletonWireframe {
	/// <summary>Half-length of each joint's axis cross, in metres.</summary>
	public const float JointCrossMeters = 0.25f;

	/// <summary>Half-length of the marker drawn on the camera node, in metres.</summary>
	public const float CameraCrossMeters = 0.6f;

	/// <summary>
	/// Bones and joint crosses for <paramref name="joints"/>, in render space. A joint whose parent
	/// id is out of range contributes its cross only.
	/// </summary>
	public static Vector3[] Build(IReadOnlyList<SkeletonJoint> joints) {
		var segments = new List<Vector3>(joints.Count * 8);

		foreach (var joint in joints) {
			var here = WorldScale.ToRender(joint.World);
			AppendCross(segments, here, JointCrossMeters);

			if (joint.ParentId >= 0 && joint.ParentId < joints.Count) {
				segments.Add(WorldScale.ToRender(joints[joint.ParentId].World));
				segments.Add(here);
			}
		}

		return segments.ToArray();
	}

	/// <summary>A single axis cross at one world point, in render space — used to flag one node.</summary>
	public static Vector3[] Marker(Vec3i world, float halfSizeMeters) {
		var segments = new List<Vector3>(6);
		AppendCross(segments, WorldScale.ToRender(world), halfSizeMeters);
		return segments.ToArray();
	}

	private static void AppendCross(List<Vector3> segments, Vector3 center, float halfSize) {
		segments.Add(center - Vector3.UnitX * halfSize);
		segments.Add(center + Vector3.UnitX * halfSize);
		segments.Add(center - Vector3.UnitY * halfSize);
		segments.Add(center + Vector3.UnitY * halfSize);
		segments.Add(center - Vector3.UnitZ * halfSize);
		segments.Add(center + Vector3.UnitZ * halfSize);
	}
}
