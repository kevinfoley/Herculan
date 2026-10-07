using HercWorks.Core.Data.File.Dat.Sim;
using HercWorks.Core.Io.Transform.Dbsim;
using Herculan.Engine.Content;
using Herculan.Engine.Install;
using Herculan.Engine.Numerics;
using Herculan.Engine.Sim;
using Herculan.Engine.Sim.Anim;
using Herculan.Engine.Terrain;
using Xunit;

namespace Herculan.Engine.Tests;

/// <summary>
/// Checks on the debug skeleton view — that what it draws is the same pose the simulation is acting
/// on, so a defect it makes visible is a real one and not an artefact of how it samples.
///
/// <para>Skips silently with no install present, as the rest of the suite does.</para>
/// </summary>
[Collection(SimTimestepCollection.Name)]
public class SkeletonPoseTests {
	/// <summary>
	/// The camera joint and <see cref="MechObject.CameraNodeTransform"/> are the same point, exactly.
	/// This is the load-bearing one: it is what makes the drawn skeleton evidence about the cockpit
	/// view rather than a second, independently-wrong sampling of the animation. The eye itself sits
	/// off that joint by the type's own offset — see <see cref="MechObject.EyeTransform"/> — so it is
	/// the joint the two samplings have to agree on, not the eye.
	/// </summary>
	[Theory]
	[MemberData(nameof(MechLocomotionTests.Hercs), MemberType = typeof(MechLocomotionTests))]
	public void TheCameraJointIsExactlyTheCameraNode(string herc) {
		if (Content() is not { } content || Spawn(content, herc) is not { } mech) {
			return;
		}

		var world = FlatWorld(mech);

		// Mid-stride, not at rest: at rest every sampling agrees trivially.
		for (int i = 0; i < 120; i++) {
			mech.Controls = new MechControls(0, -MechControls.AxisFull);
			world.Tick();
		}

		int cameraNode = SkeletonPose.CameraTransformId(mech);
		if (cameraNode < 0) {
			return;
		}

		var joints = SkeletonPose.Build(mech);
		var node = mech.CameraNodeTransform;
		Assert.Equal(new Vec3i(node.X, node.Y, node.Z), joints[cameraNode].World);
	}

	/// <summary>
	/// One joint per transform the shape declares, each naming a parent that is either the root or a
	/// real transform — a skeleton that draws bones to nowhere would look like a broken pose.
	/// </summary>
	[Theory]
	[MemberData(nameof(MechLocomotionTests.Hercs), MemberType = typeof(MechLocomotionTests))]
	public void EveryJointNamesARealParent(string herc) {
		if (Content() is not { } content || Spawn(content, herc) is not { } mech) {
			return;
		}

		FlatWorld(mech);
		var joints = SkeletonPose.Build(mech);
		if (mech.Animation is not { } animation) {
			return;
		}

		Assert.Equal(animation.ParentTransform.Length, joints.Length);
		for (int i = 0; i < joints.Length; i++) {
			Assert.Equal(i, joints[i].TransformId);
			Assert.InRange(joints[i].ParentId, -1, joints.Length - 1);
			Assert.NotEqual(i, joints[i].ParentId);
		}
	}

	/// <summary>
	/// Sampling the pose changes nothing about it: the machine walks the same distance whether or not
	/// the debug view is drawing. <see cref="SkeletonPose"/> reads the thread and the original's own
	/// per-frame update reads the same state, so a sampler that advanced anything would corrupt the
	/// very thing it exists to measure.
	/// </summary>
	[Fact]
	public void SamplingThePoseDoesNotDisturbTheWalk() {
		if (Content() is not { } content
			|| Spawn(content, "OUTLAW") is not { } plain
			|| Spawn(content, "OUTLAW") is not { } sampled) {
			return;
		}

		var plainWorld = FlatWorld(plain);
		var sampledWorld = FlatWorld(sampled);

		for (int i = 0; i < 300; i++) {
			plain.Controls = new MechControls(0, -MechControls.AxisFull);
			plainWorld.Tick();

			sampled.Controls = new MechControls(0, -MechControls.AxisFull);
			sampledWorld.Tick();
			SkeletonPose.Build(sampled);
		}

		Assert.Equal(plain.Position, sampled.Position);
		Assert.Equal(plain.Heading, sampled.Heading);
		Assert.Equal(plain.Speed, sampled.Speed);
	}

	private static SimWorld FlatWorld(MechObject mech) {
		const int widthShift = 8;
		const int cellShift = 12;
		const int cellCount = 1 << (widthShift * 2);

		var heights = new byte[cellCount];
		System.Array.Fill(heights, (byte)40);

		var terrain = new HeightGrid(widthShift, widthShift, cellShift, 16, 10, heights, new byte[cellCount]);
		var world = new SimWorld(terrain);

		int middle = (1 << (widthShift + cellShift)) / 2;
		mech.Position = new Vec3i(middle, middle, terrain.HeightAtWorld(middle, middle));
		world.Add(mech);
		return world;
	}

	private static MechObject? Spawn(GameContent content, string herc) {
		if (content.Read("dat", herc + ".DAT") is not { } datBytes
			|| new HercSimDataTransformer().Parse(datBytes) is not HercSimDat data) {
			return null;
		}

		var animation = content.Read("dts", herc + ".DTS") is { } dtsBytes
			? ShapeAnimation.FromModel(
				new DTSModelTransformer().Parse(dtsBytes) as HercWorks.Core.Data.File.Dyn.DynamixThreeSpaceModel)
			: null;

		return new MechObject(herc, data, 0, MechLoadout.None, animation);
	}

	private static GameContent? Content() {
		string? root = GameInstall.Locate(null);
		return root != null ? GameContent.MountSimulator(root) : null;
	}
}
