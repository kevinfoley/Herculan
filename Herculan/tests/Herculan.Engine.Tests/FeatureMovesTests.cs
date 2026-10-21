using HercWorks.Core.Data.File.Msn;
using HercWorks.Core.Io.Transform.Common;
using Herculan.Engine.Editor;
using Herculan.Engine.Numerics;
using Herculan.Engine.Scene;
using Xunit;
using static Herculan.Engine.Tests.SyntheticMsn;

namespace Herculan.Engine.Tests;

/// <summary>
/// <see cref="FeatureMoves"/>, the editor's waypoint and trigger-area moves, over small synthetic missions: a preview places
/// what it moves through overrides and writes nothing, and a commit writes the <c>.MSN</c> record its GUID names.
/// </summary>
public class FeatureMovesTests {
	/// <summary>
	/// Catches a waypoint drag's preview that writes the <c>.MSN</c> (or leaves the route and the area sharing its point where
	/// they were), and a commit that does not move the shared point.
	/// </summary>
	[Fact]
	public void AWaypointPreviewWritesNothingAndItsCommitMovesTheSharedPoint() {
		var document = Open(LayoutFeaturesTests.SharedPointMission());
		var moves = new FeatureMoves(document, Index(document));
		var target = moves.TargetOf(new RouteSelection(10, 0))!;
		byte[] opened = Write(document);
		var at = new Vec3i(3000, 4000, 0);

		var preview = LayoutPlacement.Place(document.Layout, LayoutFeaturesTests.Tables, moves.Overrides(target, at));

		Assert.Equal(opened, Write(document));
		Assert.Equal(at, preview.Route(10)!.Points[0]);
		Assert.Equal(at, preview.Area(4)!.Area.A);
		Assert.Equal(1, moves.SharersOf(target));

		document.Change(() => moves.Write(target, at));
		var point = document.Layout.Entry<MapPoint22>(1)!.Fields;
		Assert.Equal((3000, 4000), (point.X, point.Y));
	}

	/// <summary>
	/// Catches a detached waypoint whose commit moves the point it shares with the area rather than giving its list entry a
	/// point of its own, or writes another entry of the list.
	/// </summary>
	[Fact]
	public void ADetachedWaypointGetsAPointOfItsOwn() {
		var document = Open(LayoutFeaturesTests.SharedPointMission());
		var moves = new FeatureMoves(document, Index(document));
		var target = moves.TargetOf(new RouteSelection(10, 0))! with { Detach = true };

		document.Change(() => moves.Write(target, new Vec3i(3000, 4000, 0)));

		short[] list = document.Waypoints(10);
		Assert.NotEqual(1, list[0]);
		Assert.Equal(2, list[1]);
		var own = document.Layout.Entry<MapPoint22>(list[0])!.Fields;
		Assert.Equal((3000, 4000), (own.X, own.Y));
		Assert.Equal(1000, document.Layout.Entry<MapPoint22>(1)!.Fields.X);
	}

	/// <summary>
	/// Catches an area target kept by its generated block-4 index, which a delete of an earlier area renumbers, so the move
	/// lands on another record's point or on none.
	/// </summary>
	[Fact]
	public void AnAreaTargetKeepsItsRecordAcrossADeleteBeforeIt() {
		var document = Open(new SyntheticMsn()
			.Add(6, Point(1, 1000)).Add(6, Point(2, 9000))
			.Add(9, Circle(4, 1)).Add(9, Circle(5, 2)));
		var index = Index(document);
		var moves = new FeatureMoves(document, index);
		var target = Assert.IsType<AreaCornerTarget>(moves.TargetsOf(5)[1]);

		TriggerAreaEditing.Delete(document, 4);
		index.Reload(LayoutPlacement.Place(document.Layout, LayoutFeaturesTests.Tables), new Dictionary<ObjectKey, SceneObject>());
		document.Change(() => moves.Write(target, new Vec3i(6000, 7000, 0)));

		var area = document.Layout.Entry<TriggerArea12>(5)!.Fields;
		var centre = document.Layout.Entry<MapPoint22>(area.PointRef)!.Fields;
		Assert.Equal((6000, 7000), (centre.X, centre.Y));
		Assert.Null(document.Layout.Entry<TriggerArea12>(4));
	}

	private static MissionIndex Index(MissionDocument document) =>
		new(LayoutPlacement.Place(document.Layout, LayoutFeaturesTests.Tables), new Dictionary<ObjectKey, SceneObject>());

	private static MissionDocument Open(SyntheticMsn msn) =>
		MissionDocument.Read(msn.Bytes(), null, new MissionGeneration(new short[MissionGenerator.CampaignFlagCount],
			new short?[MissionGeneration.HeaderWords], () => _ => 0));

	private static byte[] Write(MissionDocument document) => new MissionFileTransformer().Write(document.Mission)!;
}
