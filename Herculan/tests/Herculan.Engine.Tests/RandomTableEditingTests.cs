using HercWorks.Core.Data.File.Msn;
using HercWorks.Core.Io.Transform.Common;
using Xunit;
using static Herculan.Engine.Tests.SyntheticMsn;

namespace Herculan.Engine.Tests;

/// <summary>
/// <see cref="RandomTableEditing"/> over small synthetic missions: a new table keeps the record as generated, chances become
/// contiguous ranges, a table is deleted only once nothing picks from it, and a template is read before the records
/// picking it.
/// </summary>
public class RandomTableEditingTests {
	private const short Hercs = 0;
	private const short Flyers = 1;
	private const short Structures = 2;
	private const short Squad = 9;

	/// <summary>
	/// Catches a new table whose one template copies the wrong span, or what the record carries itself where it picks a
	/// template: the generated <c>script.dat</c> would change. A herc with a point, a heading, an action and a fit, a flyer,
	/// a structure, and a herc already picking from a table, each made a table of its own.
	/// </summary>
	[Fact]
	public void ANewTableKeepsTheRecordAsGenerated() {
		var herc = Mech(5, type: 7, weapons: [3, 4], ammo: [0]);
		herc[MechRosterEntry144.PositionRefWord] = 0;
		herc[MechRosterEntry144.HeadingRefWord] = 0;
		herc[MechRosterEntry144.EngagementActionRefWord] = 0;
		var flyer = Flyer(6);
		flyer[FlyerRosterEntry102.PositionRefWord] = 0;
		var document = Open(Mission()
			.Add(1, Table(0, key: 1, bound: 4)).Add(1, Range(1, 0, 0, 3))
			.Add(6, Point(0, 1000, 2000))
			.Add(10, Action(0, type: 1))
			.Add(12, Mech(-1, type: 9, condition: 1, weapons: [8]))
			.Add(12, herc).Add(12, Mech(8, type: 2, tableKey: 1))
			.Add(13, flyer)
			.Add(14, Base(7, position: 0))
			.Add(16, Group(20, Hercs, position: -1, condition: -1, 5, 8))
			.Add(16, Group(21, Flyers, position: -1, condition: -1, 6))
			.Add(16, Group(22, Structures, position: -1, condition: -1, 7)), Sequence(0));
		byte[] generated = document.ScriptBytes;

		foreach (var (row, guid) in new (int, short)[] { (12, 5), (12, 8), (13, 6), (14, 7) }) {
			RandomTableEditing.CreateTable(document, row, guid, out short table);
			Assert.Equal(table, RandomTableEditing.TableOf(document, row, guid));
			Assert.Equal(generated, document.ScriptBytes);
		}
	}

	/// <summary>
	/// Catches the chances written as off-by-one or overlapping ranges, or over the old bound: each option is picked at
	/// both ends of its run, a 0% one never, and a draw past the runs takes none, leaving the herc's own type.
	/// </summary>
	[Fact]
	public void ChancesBecomeContiguousRangesFromZero() {
		var document = Open(Mission()
			.Add(1, Table(0, key: 1, bound: 200)).Add(1, Range(1, 0, 0, 99)).Add(1, Range(2, 0, 100, 149)).Add(1, Range(3, 0, 150, 199))
			.Add(12, Mech(-1, type: 1, condition: 1)).Add(12, Mech(-1, type: 2, condition: 2)).Add(12, Mech(-1, type: 3, condition: 3))
			.Add(12, Mech(10, type: 15, tableKey: 1))
			.Add(16, Group(20, Hercs, position: -1, condition: -1, 10)), Sequence(0));
		Assert.Equal([50, 25, 25], RandomTableEditing.Percents(RandomTableEditing.Table(document, 0)!));

		RandomTableEditing.SetChances(document, 0, [70, 20, 0]);

		var table = RandomTableEditing.Table(document, 0)!;
		Assert.Equal(RandomTableEditing.PercentBound, table.Bound);
		Assert.Equal([70, 20, 0], RandomTableEditing.Percents(table));
		Assert.Equal(10, table.Leftover);
		foreach (var (draw, type) in new (short, short)[] { (0, 1), (69, 1), (70, 2), (89, 2), (90, 15), (99, 15) }) {
			var drawn = new MissionDocument(document.Mission, document.Text,
				new MissionGeneration(document.Generation.Flags, document.Generation.Header, Sequence(draw)));
			Assert.Equal(type, TypeOf(drawn, 10));
		}
	}

	/// <summary>
	/// Catches Delete allowed while a herc picks from the table, and taking the table away leaving the herc with its own
	/// stale fields rather than what it picked; then the delete takes the ranges and the templates with the table.
	/// </summary>
	[Fact]
	public void ATableIsDeletedOnlyOnceNothingPicksFromIt() {
		var document = Open(Mission()
			.Add(1, Table(0, key: 1, bound: 100)).Add(1, Range(1, 0, 0, 99))
			.Add(12, Mech(-1, type: 3, condition: 1, weapons: [6]))
			.Add(12, Mech(10, type: 15, tableKey: 1))
			.Add(16, Group(20, Hercs, position: -1, condition: -1, 10)), Sequence(0));
		byte[] generated = document.ScriptBytes;
		int conditions = ConditionEditing.Records(document).Length;
		Assert.NotNull(RandomTableEditing.WhyNoDelete(document, 0));
		Assert.Throws<InvalidOperationException>(() => RandomTableEditing.DeleteTable(document, 0));

		RandomTableEditing.SetTable(document, 12, 10, -1);
		Assert.Equal(generated, document.ScriptBytes);
		RandomTableEditing.DeleteTable(document, 0);

		Assert.Equal(conditions - 2, ConditionEditing.Records(document).Length);
		Assert.DoesNotContain(document.Mission.Mechs!, record => record.GUID == -1);
		Assert.Equal(generated, document.ScriptBytes);
	}

	/// <summary>
	/// Catches an added template, or a table's templates for a herc given that table, left after the herc in its row,
	/// where its pick finds no record to copy: the herc that picks the new option takes its type, and a herc read before
	/// the table's templates picks once given the table.
	/// </summary>
	[Fact]
	public void TemplatesAreReadBeforeTheRecordsPickingThem() {
		var document = Open(Mission()
			.Add(1, Table(0, key: 1, bound: 100)).Add(1, Range(1, 0, 0, 99))
			.Add(12, Mech(11, type: 15))
			.Add(12, Mech(-1, type: 3, condition: 1))
			.Add(12, Mech(10, type: 15, tableKey: 1))
			.Add(16, Group(20, Hercs, position: -1, condition: -1, 10, 11)), Sequence(60));

		RandomTableEditing.AddOption(document, 0, 1, out short added);
		var table = RandomTableEditing.Table(document, 0)!;
		Assert.Equal([50, 50], RandomTableEditing.Percents(table));
		Assert.Single(table.Options, other => other.Range == added);
		Assert.Equal(3, TypeOf(document, 10));

		RandomTableEditing.SetTable(document, 12, 11, 0);

		Assert.Equal(3, TypeOf(document, 11));
	}

	// Condition 0 of a table is GUID 0 here; group 0, the squad, is the player's machine.
	private static SyntheticMsn Mission() {
		var squad = Group(Squad, Hercs, position: -1, condition: -1, 0);
		squad[MissionGroup164.HeadingRefWord] = 0;
		return new SyntheticMsn()
			.Add(7, Heading(0, 0))
			.Add(12, Mech(0, type: 0))
			.Add(16, squad);
	}

	private static short[] Table(short guid, short key, short bound) =>
		Condition(guid, condition: -1, type: ConditionEditing.RandomTable, lower: key, upper: 0, operand: bound);

	private static short[] Range(short guid, short table, short low, short high) =>
		Condition(guid, condition: table, type: ConditionEditing.Range, lower: low, upper: high, operand: 0);

	// A roll answering <draws> in turn, from the first again for each run.
	private static Func<Func<short, int>> Sequence(params short[] draws) => () => {
		int next = 0;
		return _ => draws[next++ % draws.Length];
	};

	private static short TypeOf(MissionDocument document, short guid) =>
		document.Script.Mechs[Exported(document, 12, guid)!.Value].TypeIndex;

	private static MissionDocument Open(SyntheticMsn msn, Func<Func<short, int>> roll) =>
		MissionDocument.Read(msn.Bytes(), null, new MissionGeneration(new short[MissionGenerator.CampaignFlagCount],
			new short?[MissionGeneration.HeaderWords], roll));
}
