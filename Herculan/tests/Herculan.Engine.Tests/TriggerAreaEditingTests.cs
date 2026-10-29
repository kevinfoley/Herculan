using HercWorks.Core.Data.File.Msn;
using HercWorks.Core.Io.Transform.Common;
using Xunit;
using static Herculan.Engine.Tests.SyntheticMsn;

namespace Herculan.Engine.Tests;

/// <summary>
/// <see cref="TriggerAreaEditing"/> and <see cref="MissionDocument.SetArea"/> over small synthetic missions: a new area is
/// one undoable change reaching block 4 with nothing testing it, a circle's radius is stored in tens of world units within a
/// positive short, a shape change leaves no point behind that nothing references, and a delete is refused while any action
/// references the area.
/// </summary>
public class TriggerAreaEditingTests {
	/// <summary>
	/// Catches a creation made as separate edits, whose undo then leaves the points behind; a new area that the generation
	/// does not export while no action references it; and the corners written into the wrong fields (the opposite corner's
	/// point GUID in the first corner's field, or a radius in a box's second field).
	/// </summary>
	[Fact]
	public void CreateBoxIsOneUndoableChangeExportedWithNoAction() {
		var document = Open(new SyntheticMsn().Add(6, Point(1, 50)).Add(9, Circle(4, 1)));
		byte[] opened = Write(document);

		var change = TriggerAreaEditing.CreateBox(document, (100, 200), (300, 400), out short area);

		Assert.Equal(5, area);
		var record = Array.Find(document.Mission.TriggerAreas!, candidate => candidate.GUID == area)!;
		Assert.Equal((-1, -1, TriggerAreaEditing.Box), (record.ConditionRef, record.Unk04, record.Shape));
		var script = document.Script;
		var exported = script.TriggerAreas[Exported(document, 9, area)!.Value];
		Assert.Equal((100, 200, 0), (script.Coordinates[exported.PointRef].X, script.Coordinates[exported.PointRef].Y, script.Coordinates[exported.PointRef].Z));
		Assert.Equal((300, 400), (script.Coordinates[exported.SecondPointOrRadius].X, script.Coordinates[exported.SecondPointOrRadius].Y));
		document.Undo(change);
		Assert.Equal(opened, Write(document));
	}

	/// <summary>
	/// Catches the radius stored as typed rather than divided by 10, truncated rather than rounded, stored as 0 for a small
	/// radius (which catches nothing), and wrapped negative past a short (which DBSIM's sign-extending load reads as a
	/// radius that catches nothing).
	/// </summary>
	[Fact]
	public void RadiusIsStoredInTensRoundedWithinAPositiveShort() {
		Assert.Equal(123, TriggerAreaEditing.StoredRadius(1234));
		Assert.Equal(124, TriggerAreaEditing.StoredRadius(1235));
		Assert.Equal(1, TriggerAreaEditing.StoredRadius(3));
		Assert.Equal(short.MaxValue, TriggerAreaEditing.StoredRadius(400_000));

		var document = Open(new SyntheticMsn());
		TriggerAreaEditing.CreateCircle(document, (0, 0), 5000, out short area);
		Assert.Equal(500, document.Script.TriggerAreas[Exported(document, 9, area)!.Value].SecondPointOrRadius);
	}

	/// <summary>Catches a box of no width or no depth being added, which DBSIM's strict box test can never catch anything in.</summary>
	[Fact]
	public void FlatBoxIsRefused() {
		var document = Open(new SyntheticMsn());
		byte[] opened = Write(document);

		Assert.Throws<InvalidOperationException>(() => TriggerAreaEditing.CreateBox(document, (100, 200), (100, 400), out _));
		Assert.Throws<InvalidOperationException>(() => TriggerAreaEditing.CreateCircle(document, (100, 200), 0, out _));
		Assert.Equal(opened, Write(document));
	}

	/// <summary>
	/// Catches a box-to-circle change leaving the box's second corner behind with nothing referencing it, which keeps
	/// framing the mission box; one removing a corner a waypoint still references; and a circle not about the box's middle
	/// or not of half its longer side.
	/// </summary>
	[Fact]
	public void BoxToCircleRemovesOnlyTheCornerNothingElseReferences() {
		var document = Open(new SyntheticMsn()
			.Add(6, Point(1, 0))
			.Add(6, Point(2, 1000, 400))
			.Add(6, Point(3, 2000, 2000))
			.Add(8, Route(7, 3))
			.Add(9, Box(4, 1, 2))
			.Add(9, Box(5, 3, 2)));

		TriggerAreaEditing.SetShape(document, 4, TriggerAreaEditing.Circle);

		var record = document.AreaRecord(4);
		Assert.Equal(TriggerAreaEditing.Circle, record.Shape);
		Assert.Equal(50, record.SecondPointOrRadius);
		var centre = document.Script.Coordinates[Exported(document, 6, record.PointRef)!.Value];
		Assert.Equal((500, 200), (centre.X, centre.Y));
		Assert.Contains(document.Mission.Points!, point => point.GUID == 2);

		TriggerAreaEditing.SetShape(document, 5, TriggerAreaEditing.Circle);
		Assert.DoesNotContain(document.Mission.Points!, point => point.GUID == 2);
		Assert.Contains(document.Mission.Points!, point => point.GUID == 3);
	}

	/// <summary>
	/// Catches a circle-to-box change that appends both corners and leaves the centre behind with nothing referencing it, and
	/// a square not centred on the circle or not reaching its radius.
	/// </summary>
	[Fact]
	public void CircleToBoxIsTheSquareAboutItsCentreAndLeavesNoPoint() {
		var document = Open(new SyntheticMsn().Add(6, Point(1, 1000, 1000)).Add(9, Circle(4, 1)));

		TriggerAreaEditing.SetShape(document, 4, TriggerAreaEditing.Box);

		var record = document.AreaRecord(4);
		Assert.Equal(TriggerAreaEditing.Box, record.Shape);
		var points = document.Mission.Points!;
		Assert.Equal(2, points.Length);
		var first = Array.Find(points, point => point.GUID == record.PointRef)!;
		var second = Array.Find(points, point => point.GUID == record.SecondPointOrRadius)!;
		Assert.Equal((0, 0, 2000, 2000), (first.X, first.Y, second.X, second.Y));
	}

	/// <summary>
	/// Catches a refusal that reads only an action's first area slot, or only the actions surviving in the editor's state,
	/// so the delete leaves a conditioned action's later slot resolving to nothing.
	/// </summary>
	[Fact]
	public void DeleteIsRefusedWhileAnyActionReferencesTheArea() {
		var action = Action(guid: 9, type: 0);
		action[MissionAction82.AreaRefsWord] = 5;
		action[MissionAction82.AreaRefsWord + 3] = 4;
		action[MissionAction82.ConditionRefWord] = 20;
		var document = Open(new SyntheticMsn()
			.Add(1, Condition(guid: 20, condition: -1, type: 0, lower: 5, upper: 0x119, operand: 1))
			.Add(6, Point(1, 100))
			.Add(9, Circle(4, 1))
			.Add(9, Circle(5, 1))
			.Add(10, action));
		byte[] opened = Write(document);

		Assert.Equal(new MsnRecordId(10, 0), Assert.Single(TriggerAreaEditing.Namers(document, 4)));
		Assert.Throws<InvalidOperationException>(() => TriggerAreaEditing.Delete(document, 4));
		Assert.Equal(opened, Write(document));
	}

	/// <summary>
	/// Catches a delete that takes only the record supplying the area here, leaving a conditioned record of the GUID to bring
	/// it back in another state, and one that takes a point a route still references.
	/// </summary>
	[Fact]
	public void DeleteTakesEveryRecordOfTheAreaAndOnlyItsOwnPoints() {
		var conditioned = Box(4, 1, 3);
		conditioned[TriggerArea12.ConditionRefWord] = 20;
		var document = Open(new SyntheticMsn()
			.Add(1, Condition(guid: 20, condition: -1, type: 0, lower: 5, upper: 0x119, operand: 1))
			.Add(6, Point(1, 100))
			.Add(6, Point(2, 200, 200))
			.Add(6, Point(3, 300, 300))
			.Add(8, Route(7, 2))
			.Add(9, Box(4, 1, 2))
			.Add(9, conditioned));
		byte[] opened = Write(document);

		var change = TriggerAreaEditing.Delete(document, 4);

		Assert.Empty(document.Mission.TriggerAreas!);
		Assert.Equal(new short[] { 2 }, document.Mission.Points!.Select(point => point.GUID));
		document.Undo(change);
		Assert.Equal(opened, Write(document));
	}

	/// <summary>
	/// Catches an area edit written into the first record of a repeated GUID, which the generation's replace throws away for
	/// the last base record, so the edit changes nothing generated.
	/// </summary>
	[Fact]
	public void SetAreaWritesTheLastBaseRecordOfTheGuid() {
		var document = Open(new SyntheticMsn()
			.Add(6, Point(1, 100))
			.Add(9, Circle(4, 1))
			.Add(9, Circle(4, 1)));

		TriggerAreaEditing.SetRadius(document, 4, 7000);

		Assert.Equal(new short[] { 100, 700 }, document.Mission.TriggerAreas!.Select(record => record.SecondPointOrRadius));
		Assert.Equal(700, document.Script.TriggerAreas[Exported(document, 9, 4)!.Value].SecondPointOrRadius);
	}

	// A row-9 box trigger area between row-6 GUIDs <first> and <second>.
	private static short[] Box(short guid, short first, short second) => [guid, -1, -1, 0, first, second];

	private static MissionDocument Open(SyntheticMsn msn) =>
		MissionDocument.Read(msn.Bytes(), null, new MissionGeneration(new short[MissionGenerator.CampaignFlagCount],
			new short?[MissionGeneration.HeaderWords], () => _ => 0));

	private static byte[] Write(MissionDocument document) => new MissionFileTransformer().Write(document.Mission)!;
}
