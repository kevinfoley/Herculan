using HercWorks.Core.Data.File.Msn;
using HercWorks.Core.Io.Transform.Common;
using Herculan.Engine.Editor;
using Herculan.Engine.Numerics;
using Herculan.Engine.World;
using Xunit;
using static Herculan.Engine.Tests.SyntheticMsn;

namespace Herculan.Engine.Tests;

/// <summary><see cref="LayoutPlacement"/> over small synthetic missions and constructed formation tables.</summary>
public class LayoutPlacementTests {
	private static readonly PlacementTables Tables = new(MechFormationTable.Parse(MechFormations()),
		FlyerFormationTable.Parse(new byte[2]), BaseFormationTable.Parse(new byte[4]));

	/// <summary>
	/// Catches a group placed on its own route field's route rather than its slot-0 order's, and a member spread by its
	/// count among the group's members rather than its raw slot.
	/// </summary>
	[Fact]
	public void AHercWithNoPointSpreadsFromItsSlot0RouteStartBySlot() {
		var plan = LayoutPlacement.Place(Layout(PatrolMission()), Tables);

		Vec3i[] route = [new(100000, 200000, 0), new(150000, 260000, 0)];
		int heading = MissionPlacementRules.RouteBearing(route);
		var offset = Tables.Mechs.OffsetFor(0, 2)!.Value;
		Assert.Equal(route[0], plan.Object(new ObjectKey(12, 30))!.Place.Position);
		Assert.Equal(MissionPlacementRules.FormationSpread(route[0], heading, offset.X, offset.Y),
			plan.Object(new ObjectKey(12, 31))!.Place.Position);
		Assert.Equal(heading, plan.Object(new ObjectKey(12, 31))!.Place.Heading);
	}

	/// <summary>
	/// Catches a drag's preview that leaves a group on its route start's stored point when that point is overridden, and an
	/// object standing alone that does not take its override or drags its groupmates with it.
	/// </summary>
	[Fact]
	public void ADragsOverridesPlaceWhatStandsOnThem() {
		var layout = Layout(PatrolMission());
		var dragged = new Vec3i(120000, 210000, 0);
		var plan = LayoutPlacement.Place(layout, Tables, new PlacementOverrides { Points = new Dictionary<short, Vec3i> { [1] = dragged } });
		Assert.Equal(dragged, plan.Group(40)!.Setup.Point);
		Assert.Equal(dragged, plan.Object(new ObjectKey(12, 30))!.Place.Position);

		var alone = new Vec3i(400000, 400000, 0);
		var moved = LayoutPlacement.Place(layout, Tables, new PlacementOverrides { ObjectPoint = (new ObjectKey(12, 31), alone) });
		Assert.Equal(alone, moved.Object(new ObjectKey(12, 31))!.Place.Position);
		Assert.Equal(LayoutPlacement.Place(layout, Tables).Object(new ObjectKey(12, 30))!.Place,
			moved.Object(new ObjectKey(12, 30))!.Place);
	}

	/// <summary>
	/// Catches a herc that group 0 and a later group both reference placed by the later group's claim, or twice, instead of
	/// once as the squad member of its group-0 slot.
	/// </summary>
	[Fact]
	public void AGroup0MemberIsPlacedAsTheSquadsNotClaimed() {
		var msn = new SyntheticMsn();
		msn.Add(6, Point(1, 50000, 60000));
		msn.Add(6, Point(2, 700000, 700000));
		msn.Add(12, Mech(30, type: 1));
		msn.Add(12, Mech(31, type: 1));
		msn.Add(16, Group(0, 0, position: 1, condition: -1, 30, 31));
		msn.Add(16, Group(1, 0, position: 2, condition: -1, 31));
		var plan = LayoutPlacement.Place(Layout(msn), Tables);

		var squadmate = Assert.Single(plan.Objects, placed => placed.Key == new ObjectKey(12, 31));
		Assert.True(squadmate.Squad);
		Assert.Equal((short)0, squadmate.Group);
		Assert.Equal(MissionPlacementRules.PlaceSquadMember(plan.Group(0)!.Setup, Tables.Mechs, 1), squadmate.Place);
		Assert.Equal([new ObjectKey(12, 30), new ObjectKey(12, 31)], plan.Group(0)!.Members);
		Assert.Empty(plan.Group(1)!.Members);
	}

	/// <summary>Catches a herc placed by the last record of its GUID, a variation's, rather than by its base.</summary>
	[Fact]
	public void AHercWithAVariationStandsWhereItsBasePutsIt() {
		var msn = new SyntheticMsn();
		msn.Add(1, Condition(guid: 30, condition: -1, type: 0, lower: 8, upper: 0x11d, operand: 0));
		msn.Add(6, Point(1, 50000, 60000));
		msn.Add(6, Point(2, 700000, 700000));
		msn.Add(12, Positioned(Mech(30, type: 1), 1));
		msn.Add(12, Positioned(Mech(30, type: 1, condition: 30), 2));
		msn.Add(16, Group(0, 0));
		msn.Add(16, Group(40, 0, position: -1, condition: -1, 30));
		var plan = LayoutPlacement.Place(Layout(msn), Tables);

		Assert.Equal(new Vec3i(50000, 60000, 0), plan.Object(new ObjectKey(12, 30))!.Place.Position);
	}

	// Group 40 has no point of its own: its slot-0 order runs route 10 (points 1 and 2), while its own route field names
	// route 11 elsewhere; herc 30 is in member slot 0 and herc 31 in slot 2, past an empty slot.
	private static SyntheticMsn PatrolMission() {
		var msn = new SyntheticMsn();
		msn.Add(6, Point(1, 100000, 200000));
		msn.Add(6, Point(2, 150000, 260000));
		msn.Add(6, Point(3, 900000, 900000));
		msn.Add(6, Point(4, 950000, 900000));
		msn.Add(8, Route(10, 1, 2));
		msn.Add(8, Route(11, 3, 4));
		var order = GroupOrder(20, 0);
		order[MissionOrder22.RouteRefWord] = 10;
		msn.Add(15, order);
		msn.Add(12, Mech(30, type: 1));
		msn.Add(12, Mech(31, type: 1));
		msn.Add(16, Group(0, 0));
		var group = Group(40, 0, position: -1, condition: -1, 30, -1, 31);
		group[MissionGroup164.OrderRefsWord] = 20;
		group[MissionGroup164.RouteRefWord] = 11;
		msn.Add(16, group);
		return msn;
	}

	private static short[] Positioned(short[] mech, short point) {
		mech[MechRosterEntry144.PositionRefWord] = point;
		return mech;
	}

	// One formation whose seven follower slots each spread by a different offset.
	private static byte[] MechFormations() {
		using var stream = new MemoryStream();
		using var writer = new BinaryWriter(stream);
		writer.Write((short)1);
		for (short slot = 1; slot <= 7; slot++) {
			writer.Write((short)(slot * 3000));
			writer.Write((short)(-slot * 1000));
		}

		writer.Flush();
		return stream.ToArray();
	}

	private static MissionLayout Layout(SyntheticMsn msn) => MissionLayout.Build(new MissionFileTransformer().Parse(msn.Bytes())!);
}
