using HercWorks.Core.Data.File.Dts;
using HercWorks.Core.Data.File.Dts.Anim;
using HercWorks.Core.Data.File.Dts.Part;
using HercWorks.Core.Data.File.Dts.Poly;
using HercWorks.Core.Data.Struct;
using Herculan.Es1.Render;
using Herculan.Es1.Sim;
using Xunit;

namespace Herculan.Engine.Tests;

/// <summary>
/// Catches an edit to ES1's LOD root walk (<see cref="Es1ShapeDetail.SelectRoot"/>), the detail setting's key walk
/// (<see cref="Es1DetailSetting.FromOption"/>) and which points the move to a mount point touches
/// (<see cref="Es1WeaponMount.TranslatePointLists"/>), docs/es1/simulation/object-classes.md#drawing and
/// docs/es1/simulation/weapon-mount-classes.md.
/// </summary>
public class Es1ShapeDetailTests {
	private static readonly int[] Thresholds = [75, 60, 45, 25, 18, 12, 6];

	[Fact]
	public void TheRootStartsAtTheBiasAndTheThresholdCursorAtZero() {
		var setting = new Es1DetailSetting(1024, 1, 0);

		// Radius 70 at 326: size (70 << 8) / 256 = 70, under 75 and not under 60, so one step from the bias of 1.
		Assert.Equal(2, Es1ShapeDetail.SelectRoot(7, 70, 326, setting, Thresholds));

		// Close, no step; the bias is clamped to the last root, and the walk stops there however small the shape.
		Assert.Equal(1, Es1ShapeDetail.SelectRoot(7, 70, 70, setting, Thresholds));
		Assert.Equal(2, Es1ShapeDetail.SelectRoot(3, 70, 70, setting with { RootBias = 4 }, Thresholds));
		Assert.Equal(2, Es1ShapeDetail.SelectRoot(3, 70, 1_000_000, setting with { RootBias = 0 }, Thresholds));
	}

	[Fact]
	public void TheOptionPicksTheFirstKeyItIsAtMost() {
		Assert.Equal(new Es1DetailSetting(2000, 4, 2), Es1DetailSetting.FromOption(50));
		Assert.Equal(new Es1DetailSetting(2000, 3, 2), Es1DetailSetting.FromOption(51));
		Assert.Equal(new Es1DetailSetting(2000, 0, 0), Es1DetailSetting.FromOption(255));
	}

	[Fact]
	public void TheMoveTouchesOnlyThePointsAPolyUsesAndSkipsAnAnShape() {
		var group = Group();
		var skipped = Group();
		var model = new TSPartList { Parts = [new TSDetailPart { Parts = [group] }, new ANShape { Parts = [skipped] }] };

		Es1WeaponMount.TranslatePointLists(model, 10, 20, 30);

		// Points 0 and 1 are corners, 2 the centre; 3 is only the normal.
		Assert.Equal((11, 21, 31), At(group, 0));
		Assert.Equal((12, 22, 32), At(group, 1));
		Assert.Equal((13, 23, 33), At(group, 2));
		Assert.Equal((4, 4, 4), At(group, 3));
		Assert.Equal((1, 1, 1), At(skipped, 0));
	}

	private static TSGroup Group() => new() {
		Points = [new Vec3Short(1, 1, 1), new Vec3Short(2, 2, 2), new Vec3Short(3, 3, 3), new Vec3Short(4, 4, 4)],
		Indexes = [1, 0],
		Polys = [new TSSolidPoly { Normal = 3, Center = 2, VertexCount = 2, VertexList = 0 }],
	};

	private static (int, int, int) At(TSGroup group, int i) => (group.Points![i].X, group.Points[i].Y, group.Points[i].Z);
}
