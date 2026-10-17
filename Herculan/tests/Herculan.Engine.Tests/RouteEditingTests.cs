using HercWorks.Core.Data.File.Msn;
using HercWorks.Core.Io.Transform.Common;
using Xunit;
using static Herculan.Engine.Tests.SyntheticMsn;

namespace Herculan.Engine.Tests;

/// <summary>
/// <see cref="RouteEditing"/> over small synthetic missions: a route's creation is one undoable change, a list edit
/// writes the record whose list the route holds, a removed waypoint or a deleted route takes the points nothing else names,
/// and a delete is refused while anything names the route.
/// </summary>
public class RouteEditingTests {
	private const short Structures = 2;

	/// <summary>
	/// Catches a creation made as separate edits, whose undo then leaves the points behind, and a closed route closed on a
	/// new point at the first one's place rather than on the first point itself, which the route cursor would not wrap on.
	/// </summary>
	[Fact]
	public void CreateIsOneUndoableChangeClosedOnItsFirstPoint() {
		var document = Open(new SyntheticMsn().Add(6, Point(1, 50)));
		byte[] opened = Write(document);

		var change = RouteEditing.Create(document, [(100, 10), (200, 20), (300, 30)], closed: true, out short route);

		var script = document.Script;
		var refs = script.WaypointGroups[document.ExportedIndex(8, route)!.Value].Waypoints;
		Assert.Equal(4, refs.Length);
		Assert.Equal(refs[0], refs[^1]);
		Assert.Equal(new[] { 100, 200, 300 }, refs.Take(3).Select(reference => script.Coordinates[reference].X));
		Assert.All(refs, reference => Assert.Equal(0, script.Coordinates[reference].Z));
		document.Undo(change);
		Assert.Equal(opened, Write(document));
	}

	/// <summary>
	/// Catches waypoints added after a closed route's closing entry, which leaves the first waypoint in the middle of the
	/// list and the route no longer a loop.
	/// </summary>
	[Fact]
	public void AddingToAClosedRouteKeepsItClosed() {
		var document = Open(new SyntheticMsn()
			.Add(6, Point(1, 100))
			.Add(6, Point(2, 200))
			.Add(8, Route(5, 1, 2, 1)));

		RouteEditing.Extend(document, 5, [(300, 0)], close: false);

		short[] list = document.Waypoints(5);
		Assert.Equal(4, list.Length);
		Assert.True(RouteEditing.IsClosed(list));
		Assert.Equal(3, RouteEditing.WaypointCount(list));
		Assert.Equal(300, document.Script.Coordinates[document.ExportedIndex(6, list[2])!.Value].X);
	}

	/// <summary>
	/// Catches a list edit written into the route's own record, which a variant-keyed record replaces with the alternative's
	/// list as it is read, so the edit changes nothing generated.
	/// </summary>
	[Fact]
	public void EditOfADrawnListWritesTheSharedAlternative() {
		var document = Open(new SyntheticMsn()
			.Add(1, Condition(guid: 10, condition: -1, type: 3, lower: 7, upper: 0, operand: 1))
			.Add(1, Condition(guid: 11, condition: 10, type: 2, lower: 0, upper: 5, operand: 0))
			.Add(6, Point(1, 100))
			.Add(6, Point(2, 300))
			.Add(8, [-1, 11, -1, -1, 2, 1, 2])
			.Add(8, [5, -1, 7, -1, 0])
			.Add(8, [6, -1, 7, -1, 0]));

		Assert.True(document.WaypointsShared(5));
		RouteEditing.InsertAfter(document, 5, 0);

		var script = document.Script;
		var other = script.WaypointGroups[document.ExportedIndex(8, 6)!.Value].Waypoints;
		Assert.Equal(3, other.Length);
		Assert.Equal(200, script.Coordinates[other[1]].X);
		Assert.Empty(Array.Find(document.Mission.WaypointGroups!, record => record.GUID == 5)!.Waypoints);
	}

	/// <summary>
	/// Catches the cleanup removing a waypoint's point whatever else names it, which leaves the trigger area on it resolving
	/// to nothing, or not removing a point at all once nothing names it, which keeps it framing the mission box.
	/// </summary>
	[Fact]
	public void RemovedWaypointTakesItsPointOnlyWhenNothingElseNamesIt() {
		var document = Open(new SyntheticMsn()
			.Add(6, Point(1, 100))
			.Add(6, Point(2, 200))
			.Add(6, Point(3, 300))
			.Add(8, Route(5, 1, 2, 3))
			.Add(9, Circle(4, 2)));

		RouteEditing.RemoveAt(document, 5, 1);
		Assert.Contains(document.Mission.Points!, point => point.GUID == 2);

		RouteEditing.RemoveAt(document, 5, 1);
		Assert.DoesNotContain(document.Mission.Points!, point => point.GUID == 3);
		Assert.Equal(new short[] { 1 }, document.Waypoints(5));
		Assert.NotNull(RouteEditing.WhyNoRemove(document, 5, 0));
	}

	/// <summary>
	/// Catches a closed route's first waypoint removed from the front of the list alone, which leaves the route open and
	/// ending on the removed point.
	/// </summary>
	[Fact]
	public void RemovingAClosedRoutesFirstWaypointClosesItOnTheNext() {
		var document = Open(new SyntheticMsn()
			.Add(6, Point(1, 100))
			.Add(6, Point(2, 200))
			.Add(6, Point(3, 300))
			.Add(8, Route(5, 1, 2, 3, 1)));

		RouteEditing.RemoveAt(document, 5, 0);

		Assert.Equal(new short[] { 2, 3, 2 }, document.Waypoints(5));
		Assert.DoesNotContain(document.Mission.Points!, point => point.GUID == 1);
	}

	/// <summary>
	/// The editor makes no loop of one waypoint, but leaves a retail one as it is. The first assertion catches a removal
	/// that closes the loop again on the one waypoint left (<c>[1, 1]</c>); the second a close allowed on one waypoint; the
	/// third the same minimum applied to opening, which would leave a retail loop of one stuck closed.
	/// </summary>
	[Fact]
	public void NoLoopIsMadeOfOneWaypoint() {
		var document = Open(new SyntheticMsn()
			.Add(6, Point(1, 100))
			.Add(6, Point(2, 200))
			.Add(8, Route(5, 1, 2, 1))
			.Add(8, Route(6, 1, 1)));

		RouteEditing.RemoveAt(document, 5, 1);

		Assert.Equal(new short[] { 1 }, document.Waypoints(5));
		Assert.NotNull(RouteEditing.WhyNoClose(document, 5, closed: true));
		Assert.Null(RouteEditing.WhyNoClose(document, 6, closed: false));
	}

	/// <summary>
	/// Catches a namer check that reads the orders alone, letting a route an objective or a group's own route field names be
	/// deleted, so the ref resolves to nothing.
	/// </summary>
	[Fact]
	public void DeleteIsRefusedWhileAnObjectiveOrAGroupNamesTheRoute() {
		var objective = Objective(condition: -1, kind: 0, subject: 1);
		objective[MissionObjective58.RouteRefWord] = 5;
		var group = Group(1, Structures, position: -1, condition: -1, 7);
		group[MissionGroup164.RouteRefWord] = 5;
		var document = Open(new SyntheticMsn()
			.Add(6, Point(1, 100))
			.Add(8, Route(5, 1))
			.Add(14, Base(7))
			.Add(16, group)
			.Add(17, objective));
		byte[] opened = Write(document);

		var namers = RouteEditing.Namers(document, 5);

		Assert.Equal(new MsnRecordId(17, 0), Assert.Single(namers.Objectives));
		Assert.Equal(new MsnRecordId(16, 0), Assert.Single(namers.Groups));
		Assert.Throws<InvalidOperationException>(() => RouteEditing.Delete(document, 5));
		Assert.Equal(opened, Write(document));
	}

	/// <summary>
	/// Catches a delete that takes only the record supplying the route here, leaving a conditioned record of the GUID to
	/// bring it back in another state, and one that takes a point another route still names.
	/// </summary>
	[Fact]
	public void DeleteTakesEveryRecordOfTheRouteAndOnlyItsOwnPoints() {
		var conditioned = Route(5, 1, 3);
		conditioned[WaypointGroup.ConditionRefWord] = 20;
		var document = Open(new SyntheticMsn()
			.Add(1, Condition(guid: 20, condition: -1, type: 0, lower: 5, upper: 0x119, operand: 1))
			.Add(6, Point(1, 100))
			.Add(6, Point(2, 200))
			.Add(6, Point(3, 300))
			.Add(8, Route(5, 1, 2))
			.Add(8, conditioned)
			.Add(8, Route(6, 2)));
		byte[] opened = Write(document);

		var change = RouteEditing.Delete(document, 5);

		Assert.DoesNotContain(document.Mission.WaypointGroups!, record => record.GUID == 5);
		Assert.Equal(new short[] { 2 }, document.Mission.Points!.Select(point => point.GUID));
		document.Undo(change);
		Assert.Equal(opened, Write(document));
	}

	private static MissionDocument Open(SyntheticMsn msn) =>
		MissionDocument.Read(msn.Bytes(), null, new MissionGeneration(new short[MissionGenerator.CampaignFlagCount],
			new short?[MissionGeneration.HeaderWords], () => _ => 0));

	private static byte[] Write(MissionDocument document) => new MissionFileTransformer().Write(document.Mission)!;
}
