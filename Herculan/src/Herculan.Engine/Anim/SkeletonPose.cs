using Herculan.Engine.Numerics;

namespace Herculan.Engine.Anim;

/// <summary>
/// One node of a machine's skeleton on the frame it was sampled: which transform it is, which
/// transform it hangs from, and where that puts it in the world.
/// </summary>
/// <param name="TransformId">Index into the shape's transform table — the id
/// <see cref="ShapeInstance.NodeTransform"/> takes.</param>
/// <param name="ParentId">The transform this one hangs from, or -1 at the root.</param>
/// <param name="World">The node's origin in world units.</param>
public readonly record struct SkeletonJoint(int TransformId, int ParentId, Vec3i World);

/// <summary>
/// Samples every node of an animating machine into world space — the debug counterpart to ES2's
/// <c>MechObject.EyePosition</c>, which asks the same question about one node only.
///
/// <para>It shows the whole skeleton, including the nodes no geometry hangs from, which the drawn
/// model cannot. Overlaid on a posed machine (the external view, <c>[V]</c>) it is also the check
/// that the two agree: the joints land on the model's own knees and feet, so a defect in the thread,
/// the sequence data or the root motion shows up as the pair disagreeing rather than having to be
/// inferred from how the camera feels.</para>
///
/// <para>Nothing here is a port; the original has no such view. It reads the same
/// <see cref="AnimationThread"/> state the simulation reads and adds no state of its own, so it
/// cannot change what it is measuring.</para>
/// </summary>
public static class SkeletonPose {
	/// <summary>
	/// Every transform in <paramref name="shape"/>, in world units, on the frame its threads are
	/// currently showing, placed by the machine's <paramref name="world"/> transform. Empty when the
	/// machine has no animated shape.
	///
	/// <para>Nodes no thread places come back at the machine's own origin:
	/// <see cref="ShapeInstance.NodeTransform"/> accumulates nothing for them and returns identity,
	/// which is indistinguishable from a node genuinely posed at the origin. A cluster of joints
	/// sitting exactly on the origin is that case, not a collapsed skeleton.</para>
	/// </summary>
	public static SkeletonJoint[] Build(ShapeInstance? shape, in Transform3 world) {
		if (shape == null) {
			return Array.Empty<SkeletonJoint>();
		}

		var parents = shape.Animation.ParentTransform;

		var joints = new SkeletonJoint[parents.Length];
		for (int id = 0; id < parents.Length; id++) {
			var local = shape.NodeTransform(id);
			joints[id] = new SkeletonJoint(id, parents[id],
				world.TransformPoint(local.X, local.Y, local.Z));
		}

		return joints;
	}

	/// <summary>
	/// The transform the machine's camera node sits on — the one ES2's <c>MechObject.EyePosition</c>
	/// rides — or -1 when the shape does not carry it.
	/// </summary>
	/// <param name="animation">The machine's animation data, or null when it has none.</param>
	/// <param name="cameraPartId">The shape part id the machine's type names as its camera node.</param>
	public static int CameraTransformId(ShapeAnimation? animation, int cameraPartId) =>
		animation?.TransformIdOfPart(cameraPartId) ?? -1;
}
