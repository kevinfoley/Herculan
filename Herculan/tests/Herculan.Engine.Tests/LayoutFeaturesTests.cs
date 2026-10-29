using HercWorks.Core.Data.File.Msn;
using HercWorks.Core.Io.Transform.Common;
using Herculan.Engine.Editor;
using Herculan.Engine.Numerics;
using Herculan.Engine.Scene;
using Herculan.Engine.World;
using Xunit;
using static Herculan.Engine.Tests.SyntheticMsn;

namespace Herculan.Engine.Tests;

/// <summary><see cref="LayoutFeatures"/>' routes, trigger areas and actions, through <see cref="LayoutPlacement"/>, over small synthetic missions.</summary>
public class LayoutFeaturesTests {
	internal static readonly PlacementTables Tables = new(MechFormationTable.Parse(new byte[2]),
		FlyerFormationTable.Parse(new byte[2]), BaseFormationTable.Parse(new byte[4]));

	/// <summary>
	/// Catches a route or an area left out because its record carries a condition (one built from the generation, or one that
	/// keeps only records present in every campaign state), and an action that does not list such an area as one it tests.
	/// </summary>
	[Fact]
	public void ARouteAndAnAreaUnderAConditionArePlaced() {
		var route = Route(12, 3, 4);
		route[WaypointGroup.ConditionRefWord] = 30;
		var area = Circle(6, 3);
		area[TriggerArea12.ConditionRefWord] = 30;
		var action = Action(50, type: 0);
		action[MissionAction82.AreaRefsWord] = 6;
		var msn = new SyntheticMsn()
			.Add(1, Condition(guid: 30, condition: -1, type: 0, lower: 8, upper: 0x11d, operand: 0))
			.Add(6, Point(3, 1000, 2000)).Add(6, Point(4, 3000, 2000))
			.Add(8, route).Add(9, area).Add(10, action);

		var plan = LayoutPlacement.Place(Layout(msn), Tables);

		Assert.Equal([new Vec3i(1000, 2000, 0), new Vec3i(3000, 2000, 0)], plan.Route(12)!.Points);
		Assert.Equal(new Vec3i(1000, 2000, 0), plan.Area(6)!.Area.A);
		Assert.Equal([(short)50], plan.Area(6)!.TestedBy);
	}

	/// <summary>
	/// Catches a drag's preview that leaves the route and area lines standing on a moved point where they were, and a waypoint
	/// dragged alone that moves the point it names (the area sharing it, or the group standing on its route's start) rather
	/// than its own line alone.
	/// </summary>
	[Fact]
	public void ADragsOverridesMoveTheRoutesAndAreasOnIt() {
		var layout = Layout(SharedPointMission());
		var dragged = new Vec3i(7000, 8000, 0);

		var shared = LayoutPlacement.Place(layout, Tables, new PlacementOverrides { Points = new Dictionary<short, Vec3i> { [1] = dragged } });
		Assert.Equal(dragged, shared.Route(10)!.Points[0]);
		Assert.Equal(dragged, shared.Area(4)!.Area.A);

		var alone = LayoutPlacement.Place(layout, Tables, new PlacementOverrides { DetachedWaypoint = (10, [0], dragged) });
		var unmoved = LayoutPlacement.Place(layout, Tables);
		Assert.Equal(dragged, alone.Route(10)!.Points[0]);
		Assert.Equal(unmoved.Route(10)!.Points[1], alone.Route(10)!.Points[1]);
		Assert.Equal(unmoved.Area(4)!.Area, alone.Area(4)!.Area);
		Assert.Equal(unmoved.Group(40)!.Setup.Point, alone.Group(40)!.Setup.Point);
	}

	/// <summary>
	/// Catches an action read as testing every one of its eight area slots, past the first unset one or one naming an area
	/// the mission does not have, where the generation and DBSIM stop.
	/// </summary>
	[Fact]
	public void AnActionTestsItsAreasUpToTheFirstUnsetOrMissingOne() {
		var stopsAtUnset = Action(50, type: 0);
		stopsAtUnset[MissionAction82.AreaRefsWord] = 4;
		stopsAtUnset[MissionAction82.AreaRefsWord + 2] = 5;
		var stopsAtMissing = Action(51, type: 0);
		stopsAtMissing[MissionAction82.AreaRefsWord] = 99;
		stopsAtMissing[MissionAction82.AreaRefsWord + 1] = 5;
		var msn = new SyntheticMsn()
			.Add(6, Point(1, 1000)).Add(6, Point(2, 9000))
			.Add(9, Circle(4, 1)).Add(9, Circle(5, 2))
			.Add(10, stopsAtUnset).Add(10, stopsAtMissing);

		var plan = LayoutPlacement.Place(Layout(msn), Tables);

		Assert.Equal([(short)4], plan.Action(50)!.Areas);
		Assert.Empty(plan.Action(51)!.Areas);
		Assert.Empty(plan.Area(5)!.TestedBy);
	}

	/// <summary>
	/// Catches a route counted as walked by a group that names it only in a later order slot or in its own route field, which
	/// no group walks, and the player's route taken from another group than group 0.
	/// </summary>
	[Fact]
	public void ARouteIsWalkedOnlyByTheGroupsWhoseFirstOrderNamesIt() {
		var plan = LayoutPlacement.Place(Layout(SharedPointMission()), Tables);
		var index = new MissionIndex(plan, new Dictionary<ObjectKey, SceneObject>());

		Assert.Equal([(short)40], plan.Route(10)!.WalkedBy);
		Assert.Equal([(short)41], plan.Route(10)!.NamedBy);
		Assert.False(plan.Route(11)!.Walked);
		Assert.Null(index.PlayerRoute);
	}

	/// <summary>Route 10 runs through points 1 and 2 and area 4 is a circle about point 1. Group 40's slot-0 order runs route 10;
	/// group 41 runs it only in slot 1, and names route 11 in its own route field; group 0 has no orders.</summary>
	internal static SyntheticMsn SharedPointMission() {
		var msn = new SyntheticMsn();
		msn.Add(6, Point(1, 1000, 2000));
		msn.Add(6, Point(2, 5000, 2000));
		msn.Add(6, Point(3, 9000, 9000));
		msn.Add(8, Route(10, 1, 2));
		msn.Add(8, Route(11, 3, 2));
		msn.Add(9, Circle(4, 1));
		var order = GroupOrder(20, 0);
		order[MissionOrder22.RouteRefWord] = 10;
		msn.Add(15, order);
		msn.Add(16, Group(0, 0));
		var walker = Group(40, 0);
		walker[MissionGroup164.OrderRefsWord] = 20;
		msn.Add(16, walker);
		var namer = Group(41, 0);
		namer[MissionGroup164.OrderRefsWord + 1] = 20;
		namer[MissionGroup164.RouteRefWord] = 11;
		msn.Add(16, namer);
		return msn;
	}

	private static MissionLayout Layout(SyntheticMsn msn) => MissionLayout.Build(new MissionFileTransformer().Parse(msn.Bytes())!);
}
