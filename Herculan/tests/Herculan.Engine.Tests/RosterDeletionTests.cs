using HercWorks.Core.Data.File.Msn;
using HercWorks.Core.Io.Transform.Common;
using Xunit;
using static Herculan.Engine.Tests.SyntheticMsn;

namespace Herculan.Engine.Tests;

/// <summary>
/// <see cref="RosterDeletion"/> over small synthetic missions: a delete removes the object's records and its GUID from
/// every group naming it, takes a group it leaves empty with it, and is refused while the mission names what it would
/// remove.
/// </summary>
public class RosterDeletionTests {
	private const short Structures = 2;
	private const short StructureSubject = 3;
	private const short GroupSubject = 0;
	private const short TargetsGroup = 10;
	private const short FlagFiveIsSet = 20;

	/// <summary>
	/// Catches a member slot cleared in the record that supplies it alone, which leaves a record that fails its condition
	/// here naming a GUID that resolves to nothing in the states it survives in.
	/// </summary>
	[Fact]
	public void DeleteClearsTheObjectFromEveryRecordOfItsGroup() {
		var document = Open(new SyntheticMsn()
			.Add(1, Condition(guid: FlagFiveIsSet, condition: -1, type: 0, lower: 5, upper: 0x119, operand: 1))
			.Add(14, Base(7))
			.Add(14, Base(8))
			.Add(16, Group(1, Structures, position: -1, condition: -1, 7, 8))
			.Add(16, Group(1, Structures, position: -1, condition: FlagFiveIsSet, -1, -1, 7)));

		RosterDeletion.Delete(document, 14, 7);

		Assert.DoesNotContain(document.Mission.Bases!, record => record.GUID == 7);
		Assert.All(document.Mission.Groups!, group => Assert.DoesNotContain((short)7, group.MemberRefs));
		Assert.Equal(2, document.Mission.Groups!.Length);
		Assert.Equal<int?>(Exported(document, 14, 8), document.Script.Groups[0].MemberRefs[1]);
	}

	/// <summary>
	/// Catches a group left behind with no member, which DBSIM does not run safely, or a later group's refs renumbered by
	/// hand rather than resolved again by the generation.
	/// </summary>
	[Fact]
	public void LastMemberTakesItsGroupAndLaterRefsStillResolve() {
		var document = Open(new SyntheticMsn()
			.Add(14, Base(7))
			.Add(14, Base(8))
			.Add(15, GroupOrder(1, group: 2))
			.Add(16, Group(1, Structures, position: -1, condition: -1, 7))
			.Add(16, Group(2, Structures, position: -1, condition: -1, 8))
			.Add(17, Objective(condition: -1, kind: GroupSubject, subject: 2)));

		Assert.Contains(RosterDeletion.Check(document, 14, 7).EmptiedGroups, emptied => emptied.Group == 1);
		RosterDeletion.Delete(document, 14, 7);

		var script = document.Script;
		Assert.Single(script.Groups);
		Assert.DoesNotContain(document.Mission.Groups!, group => group.GUID == 1);
		Assert.Equal<int?>(Exported(document, 16, 2), script.Objectives[0].SubjectRef);
		Assert.Equal<int?>(Exported(document, 16, 2), script.Orders[0].SubjectRef);
		Assert.Equal<int?>(Exported(document, 14, 8), script.Groups[0].MemberRefs[0]);
	}

	/// <summary>Catches a check that misses the objectives naming the object, or a delete made without the check.</summary>
	[Fact]
	public void ObjectAnObjectiveNamesIsRefused() {
		var document = Open(new SyntheticMsn()
			.Add(14, Base(7))
			.Add(14, Base(8))
			.Add(16, Group(1, Structures, position: -1, condition: -1, 7, 8))
			.Add(17, Objective(condition: -1, kind: StructureSubject, subject: 7)));
		byte[] opened = Write(document);

		var check = RosterDeletion.Check(document, 14, 7);

		Assert.False(check.Allowed);
		Assert.Equal(new MsnRecordId(17, 0), Assert.Single(check.Namers.Objectives));
		Assert.Throws<InvalidOperationException>(() => RosterDeletion.Delete(document, 14, 7));
		Assert.Equal(opened, Write(document));
	}

	/// <summary>
	/// Catches the namers of a group checked whether or not the delete removes it: refusing a member that leaves others
	/// behind, or letting the last member go while an action targets the group.
	/// </summary>
	[Fact]
	public void GroupNamersRefuseOnlyTheDeleteThatRemovesTheGroup() {
		var document = Open(new SyntheticMsn()
			.Add(10, Action(3, TargetsGroup, target: 1))
			.Add(14, Base(7))
			.Add(14, Base(8))
			.Add(14, Base(9))
			.Add(16, Group(1, Structures, position: -1, condition: -1, 7))
			.Add(16, Group(2, Structures, position: -1, condition: -1, 8, 9))
			.Add(17, Objective(condition: -1, kind: GroupSubject, subject: 2)));

		var lastMember = RosterDeletion.Check(document, 14, 7);
		var oneOfTwo = RosterDeletion.Check(document, 14, 8);

		Assert.False(lastMember.Allowed);
		Assert.Equal(new MsnRecordId(10, 0), Assert.Single(Assert.Single(lastMember.EmptiedGroups).Namers.Actions));
		Assert.True(oneOfTwo.Allowed);
		Assert.Empty(oneOfTwo.EmptiedGroups);
	}

	/// <summary>
	/// Catches the own-action check reading the object's records alone, where a keyed record fires what the
	/// template it picks names.
	/// </summary>
	[Fact]
	public void ActionThePickedTemplateFiresRefuses() {
		var template = Base(-1, condition: 11);
		template[BaseRosterEntry62.DefeatActionRefWord] = 3;
		var document = Open(new SyntheticMsn()
			.Add(1, Condition(guid: 10, condition: -1, type: 3, lower: 7, upper: 0, operand: 1))
			.Add(1, Condition(guid: 11, condition: 10, type: 2, lower: 0, upper: 5, operand: 0))
			.Add(10, Action(3, type: 0))
			.Add(14, template)
			.Add(14, Base(2, tableKey: 7))
			.Add(16, Group(1, Structures, position: -1, condition: -1, 2)));

		var check = RosterDeletion.Check(document, 14, 2);

		Assert.False(check.Allowed);
		Assert.Equal(new MsnRecordId(10, 0), Assert.Single(check.FiredActions));
	}

	/// <summary>
	/// Catches a delete made as separate edits, whose undo then puts back only the last of them, and a document whose
	/// record lookups go stale across the renumbering, so a later edit by the regenerated index writes another record.
	/// </summary>
	[Fact]
	public void UndoRestoresTheRecordsAndIndicesFollowTheRenumbering() {
		var document = Open(new SyntheticMsn()
			.Add(6, Point(1, 100))
			.Add(14, Base(7))
			.Add(14, Base(8, position: 1))
			.Add(16, Group(1, Structures, position: -1, condition: -1, 7))
			.Add(16, Group(2, Structures, position: -1, condition: -1, 8)));
		byte[] opened = Write(document);
		byte[] generated = document.ScriptBytes;

		var delete = RosterDeletion.Delete(document, 14, 7);
		short first = GuidAt(document, 9, 0);
		document.Undo(delete);
		byte[] undone = Write(document);
		byte[] regenerated = document.ScriptBytes;
		document.SetPoint(GuidAt(document, 1, document.Script.Bases[1].PositionRef), 900, 0, 0);
		int moved = Exported(document, 14, 8)!.Value;
		var script = document.Script;

		Assert.Equal((short)8, first);
		Assert.Equal(opened, undone);
		Assert.Equal(generated, regenerated);
		Assert.Equal(900, script.Coordinates[script.Bases[moved].PositionRef].X);
	}

	/// <summary>
	/// Catches the delete taking the template the record picked, which every other record picking the same
	/// template shares.
	/// </summary>
	[Fact]
	public void DeletingAKeyedRecordLeavesTheSharedTemplate() {
		var document = Open(new SyntheticMsn()
			.Add(1, Condition(guid: 10, condition: -1, type: 3, lower: 7, upper: 0, operand: 1))
			.Add(1, Condition(guid: 11, condition: 10, type: 2, lower: 0, upper: 5, operand: 0))
			.Add(6, Point(1, 100))
			.Add(14, Base(-1, position: 1, condition: 11))
			.Add(14, Base(2, tableKey: 7))
			.Add(14, Base(3, tableKey: 7))
			.Add(16, Group(1, Structures, position: -1, condition: -1, 2, 3)));

		RosterDeletion.Delete(document, 14, 2);

		var script = document.Script;
		Assert.Contains(document.Mission.Bases!, record => record.GUID == -1);
		Assert.Equal(100, script.Coordinates[script.Bases[Exported(document, 14, 3)!.Value].PositionRef].X);
	}

	/// <summary>
	/// Catches a group removed with its last member but its orders left behind, which no group runs and whose route then
	/// cannot be deleted, and an order removed that another group still names.
	/// </summary>
	[Fact]
	public void LastMemberTakesTheOrdersOnlyItsGroupNamed() {
		var own = GroupOrder(20, group: 2);
		own[MissionOrder22.RouteRefWord] = 5;
		var emptied = Group(1, Structures, position: -1, condition: -1, 7);
		emptied[MissionGroup164.OrderRefsWord] = 20;
		emptied[MissionGroup164.OrderRefsWord + 1] = 21;
		var kept = Group(2, Structures, position: -1, condition: -1, 8);
		kept[MissionGroup164.OrderRefsWord] = 21;
		var document = Open(new SyntheticMsn()
			.Add(6, Point(1, 100))
			.Add(8, Route(5, 1))
			.Add(14, Base(7))
			.Add(14, Base(8))
			.Add(15, own)
			.Add(15, GroupOrder(21, group: 2))
			.Add(16, emptied)
			.Add(16, kept));

		RosterDeletion.Delete(document, 14, 7);

		Assert.DoesNotContain(document.Mission.Orders!, record => record.GUID == 20);
		Assert.Contains(document.Mission.Orders!, record => record.GUID == 21);
		Assert.False(RouteEditing.Namers(document, 5).Any);
	}

	private static MissionDocument Open(SyntheticMsn msn) =>
		MissionDocument.Read(msn.Bytes(), null, new MissionGeneration(new short[MissionGenerator.CampaignFlagCount],
			new short?[MissionGeneration.HeaderWords], () => _ => 0));

	private static byte[] Write(MissionDocument document) => new MissionFileTransformer().Write(document.Mission)!;
}
