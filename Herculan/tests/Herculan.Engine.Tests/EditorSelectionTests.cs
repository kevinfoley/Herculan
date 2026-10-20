using HercWorks.Core.Data.File.Msn;
using HercWorks.Core.Io.Transform.Common;
using Herculan.Engine.Editor;
using Xunit;
using static Herculan.Engine.Tests.SyntheticMsn;

namespace Herculan.Engine.Tests;

/// <summary>
/// The editor's selections over a small synthetic mission: a selection names a <c>.MSN</c> record and is kept while the file
/// has it (<see cref="EditorSelection.InFile"/>), and <see cref="GeneratedRecords"/> finds the generated record it stands for,
/// or none for a record the generation drops.
/// </summary>
public class EditorSelectionTests {
	private const short Structures = 2;
	private const short Squad = 9;

	// Condition 0 tests flag 5 = 1, which fails with every flag 0.
	private const short Fails = 0;

	/// <summary>
	/// Catches a selection checked against another row than its own (GUIDs are per row, so an action selection must not be
	/// kept by a group or an area of the same GUID), kept after its own record is deleted, or cleared by an unrelated add.
	/// </summary>
	[Fact]
	public void ASelectionIsKeptWhileItsOwnRowCarriesItsGuid() {
		var document = Open(Mission()
			.Add(6, Point(5, 1000))
			.Add(9, Circle(10, 5))
			.Add(14, Base(1))
			.Add(16, Group(10, Structures, position: -1, condition: -1, 1))
			.Add(17, Objective(-1, kind: 0, subject: 10)));
		var group = new GroupSelection(10);
		Assert.True(EditorSelection.InFile(document.Mission, group));
		Assert.True(EditorSelection.InFile(document.Mission, new AreaSelection(10)));
		Assert.False(EditorSelection.InFile(document.Mission, new ActionSelection(10)));
		Assert.True(EditorSelection.InFile(document.Mission, new ObjectiveSelection(0)));
		Assert.False(EditorSelection.InFile(document.Mission, new ObjectiveSelection(1)));

		TriggerAreaEditing.CreateCircle(document, (5000, 5000), 1000, out _);
		Assert.True(EditorSelection.InFile(document.Mission, group));

		document.Remove(16, 10);
		Assert.False(EditorSelection.InFile(document.Mission, group));
		Assert.True(EditorSelection.InFile(document.Mission, new AreaSelection(10)));
	}

	/// <summary>
	/// Catches a view given a generated index for a record the generation dropped, and a link selecting a GUID read from the
	/// wrong block: a group whose condition fails has no generated index though the file keeps it, and a surviving group's
	/// generated index selects its own GUID again.
	/// </summary>
	[Fact]
	public void ARecordTheGenerationDropsHasNoGeneratedIndex() {
		var document = Open(Mission()
			.Add(14, Base(1))
			.Add(14, Base(2))
			.Add(16, Group(10, Structures, position: -1, condition: Fails, 1))
			.Add(16, Group(11, Structures, position: -1, condition: -1, 2)));
		var records = new GeneratedRecords(document);

		Assert.Null(records.Group(10));
		Assert.True(EditorSelection.InFile(document.Mission, new GroupSelection(10)));
		Assert.Equal(new GroupSelection(11), records.GroupAt(Assert.NotNull(records.Group(11))));
	}

	// Condition 0, which fails, and group 0, the squad: the player's machine, facing a heading of its own.
	private static SyntheticMsn Mission() {
		var squad = Group(Squad, 0, position: -1, condition: -1, 0);
		squad[MissionGroup164.HeadingRefWord] = 0;
		return new SyntheticMsn()
			.Add(1, Condition(guid: Fails, condition: -1, type: 0, lower: 5, upper: MissionGenerator.OpEqual, operand: 1))
			.Add(7, Heading(0, 0))
			.Add(12, Mech(0, type: 0))
			.Add(16, squad);
	}

	private static MissionDocument Open(SyntheticMsn msn) =>
		MissionDocument.Read(msn.Bytes(), null, new MissionGeneration(new short[MissionGenerator.CampaignFlagCount],
			new short?[MissionGeneration.HeaderWords], () => _ => 0));
}
