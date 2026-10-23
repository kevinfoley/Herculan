using HercWorks.Core.Data.File.Msn;
using HercWorks.Core.Io.Transform.Common;
using Herculan.Engine.Editor;
using Herculan.Engine.Numerics;
using Herculan.Engine.Scene;
using Xunit;
using static Herculan.Engine.Tests.SyntheticMsn;

namespace Herculan.Engine.Tests;

/// <summary>
/// The editor's names for groups and objects (<see cref="MissionNames"/>), and which later order slots an objective reads
/// (<see cref="MissionIndex.ObjectivesReadingOrder"/>), which decides whether the order view offers a later order's route,
/// over small synthetic missions.
/// </summary>
public class MissionNamesTests {
	private const short Hercs = 0;
	private const short Squad = 50;
	private const short Wing = 11;
	private const short Other = 12;
	private const short First = 41;
	private const short Second = 42;

	/// <summary>
	/// Catches a group named from another group's members (or from every roster record), a member type counted once per
	/// group rather than per member, an object numbered "2 of 3" among every herc of the mission rather than among those
	/// of its own type in its own group, and a group numbered from 0 again: the squad is first in row 16, so the first group
	/// after it is group 2.
	/// </summary>
	[Fact]
	public void AGroupIsNamedByItsOwnMembersAndAnObjectByItsPlaceAmongThem() {
		var msn = Mission()
			.Add(12, Mech(5, type: 19)).Add(12, Mech(6, type: 17)).Add(12, Mech(7, type: 19)).Add(12, Mech(8, type: 19))
			.Add(16, Group(Wing, Hercs, position: -1, condition: -1, 5, 6, 7))
			.Add(16, Group(Other, Hercs, position: -1, condition: -1, 8));
		var names = Names(msn);

		Assert.Equal(new RecordName("Human hercs: Herc type 19 x2, Herc type 17", "(group 2)"), names.Group(Wing));
		Assert.Equal(new RecordName("Human hercs: Herc type 19", "(group 3)"), names.Group(Other));
		Assert.Equal(new RecordName("Herc type 19 2 of 2 in Human hercs", "(group 2)"), names.Object(new ObjectKey(12, 7)));
		Assert.Equal(new RecordName("Herc type 19 in Human hercs", "(group 3)"), names.Object(new ObjectKey(12, 8)));
	}

	/// <summary>
	/// Catches a waypoint row measuring its leg to the waypoint after it, or from the start, rather than from the one before
	/// it, and a waypoint numbered other than the cockpit numbers it: the start, then waypoint 1
	/// (docs/retail/simulation/cockpit-gunsight-hud.md#waypoint-indicators). 50,000 world units are 300 m.
	/// </summary>
	[Fact]
	public void AWaypointIsNumberedAsTheCockpitNumbersItAndMeasuredFromTheOneBefore() {
		var points = new[] { new Vec3i(0, 0, 0), new Vec3i(50000, 0, 0), new Vec3i(50000, 100000, 0) };

		Assert.Equal(["Start", "Waypoint 1", "Waypoint 2"], Enumerable.Range(0, 3).Select(MissionText.Waypoint));
		Assert.Null(MissionText.LegFromPrevious(points, 0));
		Assert.Equal("300 m from the start", MissionText.LegFromPrevious(points, 1));
		Assert.Equal("600 m from waypoint 1", MissionText.LegFromPrevious(points, 2));
	}

	/// <summary>
	/// Catches a later order's route offered as read when no objective reads it: an objective counted for an order whose
	/// route an earlier slot of the group already names (the one the objective reads), for another group's order on the
	/// same route, or for a condition other than a finished order; and a herc subject not resolved to the group that places it.
	/// </summary>
	[Fact]
	public void OnlyTheFirstOrderOnAnObjectivesRouteIsRead() {
		var msn = Mission()
			.Add(6, Point(1, 0)).Add(6, Point(2, 1000))
			.Add(8, Route(First, 1, 2)).Add(8, Route(Second, 2, 1))
			.Add(12, Mech(5, type: 19)).Add(12, Mech(8, type: 19))
			.Add(15, OnRoute(21, First)).Add(15, OnRoute(22, Second)).Add(15, OnRoute(23, Second)).Add(15, OnRoute(24, Second))
			.Add(16, Orders(Group(Wing, Hercs, position: -1, condition: -1, 5), 21, 22, 23))
			.Add(16, Orders(Group(Other, Hercs, position: -1, condition: -1, 8), 21, 24))
			.Add(17, FinishedOrder(kind: 0, Wing, Second))
			.Add(17, FinishedOrder(kind: 1, 8, Second))
			.Add(17, Lost(kind: 0, Wing, Second));
		var index = Index(msn);

		Assert.Equal([0], index.ObjectivesReadingOrder(Wing, 1).Select(objective => objective.Index));
		Assert.Empty(index.ObjectivesReadingOrder(Wing, 2));
		Assert.Equal([1], index.ObjectivesReadingOrder(Other, 1).Select(objective => objective.Index));
	}

	// Group 0, the squad, with herc 0, facing a heading of its own.
	private static SyntheticMsn Mission() {
		var squad = Group(Squad, Hercs, position: -1, condition: -1, 0);
		squad[MissionGroup164.HeadingRefWord] = 0;
		return new SyntheticMsn().Add(7, Heading(0, 0)).Add(12, Mech(0, type: 0)).Add(16, squad);
	}

	// A row-15 patrol on row-8 GUID <route>, naming no subject.
	private static short[] OnRoute(short guid, short route) {
		var words = Enumerable.Repeat((short)-1, 11).ToArray();
		words[MapObject.GUIDWord] = guid;
		words[MissionOrder22.VerbWord] = 3;
		words[MissionOrder22.RouteRefWord] = route;
		return words;
	}

	// <group> with the row-15 GUIDs <orders> in its first order slots.
	private static short[] Orders(short[] group, params short[] orders) {
		orders.CopyTo(group, MissionGroup164.OrderRefsWord);
		return group;
	}

	// A mandatory row-17 objective asking whether subject <subject> of <kind> has finished its order on row-8 GUID <route>.
	private static short[] FinishedOrder(short kind, short subject, short route) => Asking(0, kind, subject, route);

	// The same, asking whether the subject is lost (condition 1), with a route set all the same.
	private static short[] Lost(short kind, short subject, short route) => Asking(1, kind, subject, route);

	private static short[] Asking(short condition, short kind, short subject, short route) {
		var words = Objective(-1, kind, subject);
		words[MissionObjective58.RequiredWord] = 1;
		words[MissionObjective58.RequiredWord + 1] = condition; // the condition code, at 0x04
		words[MissionObjective58.RouteRefWord] = route;
		return words;
	}

	private static MissionIndex Index(SyntheticMsn msn) =>
		new(LayoutPlacement.Place(MissionLayout.Build(new MissionFileTransformer().Parse(msn.Bytes())!), LayoutFeaturesTests.Tables),
			new Dictionary<ObjectKey, SceneObject>());

	private static MissionNames Names(SyntheticMsn msn) => new(Index(msn), RosterTypeNames.None);
}
