using HercWorks.Core.Data.File.Msn;
using HercWorks.Core.Data.File.Msn.Script;
using HercWorks.Core.Io.Transform.Common;
using Xunit;
using static Herculan.Engine.Tests.SyntheticMsn;

namespace Herculan.Engine.Tests;

/// <summary>
/// <see cref="ActionEditing"/>, <see cref="TimerEditing"/>, <see cref="DelayedActions"/>, <see cref="ActionWiring"/> and
/// <see cref="GroupOrders.SetAction"/> over small synthetic missions: an action's areas and counter writes stay filled from the
/// first slot with the pair count in step, its message is stored plus one, a delete is refused while any record references it,
/// a wiring edit writes the record that supplies the field, and a timer made or removed with its own action is one edit.
/// </summary>
public class ActionEditingTests {
	private const short Hercs = 0;
	private const short Squad = 50;

	/// <summary>
	/// Catches a delete refusal that reads only a timer's starting action and not its fired slots, or only the records that
	/// survive in the editor's state, so a conditioned timer's later slot is left resolving to nothing.
	/// </summary>
	[Fact]
	public void DeleteIsRefusedWhileOnlyAConditionedTimerFiresIt() {
		var timer = Timer(30, start: -1, 4);
		timer[ActionTimer30.SequenceRefsWord + 3] = 3;
		timer[ActionTimer30.SequenceRefsWord] = -1;
		timer[ActionTimer30.ConditionRefWord] = 20;
		var document = Open(Mission()
			.Add(1, Condition(guid: 20, condition: -1, type: 0, lower: 5, upper: 0x119, operand: 1))
			.Add(10, Action(3, type: 0))
			.Add(11, timer));
		byte[] opened = Write(document);

		Assert.Equal(new MsnRecordId(11, 0), Assert.Single(ActionEditing.Namers(document, 3).Timers));
		Assert.Throws<InvalidOperationException>(() => ActionEditing.Delete(document, 3));
		Assert.Equal(opened, Write(document));
	}

	/// <summary>
	/// Catches a missed action-ref field: a group's deployment action, a template's defeated action (a GUID
	/// <c>-1</c> record), a flyer's and a structure's engaged action, and an order's ending action each refuse the delete.
	/// </summary>
	[Fact]
	public void NamersFindEveryFieldThatReferencesAnAction() {
		var waiting = Group(11, Hercs, position: -1, condition: -1);
		waiting[MissionGroup164.DeploymentActionRefWord] = 3;
		var template = Mech(-1, type: 0, condition: 41);
		template[MechRosterEntry144.DefeatActionRefWord] = 3;
		var flyer = Flyer(5);
		flyer[FlyerRosterEntry102.EngagementActionRefWord] = 3;
		var structure = Base(6);
		structure[BaseRosterEntry62.EngagementActionRefWord] = 3;
		var order = GroupOrder(7, 11);
		order[MissionOrder22.ActionRefWord] = 3;
		var document = Open(Mission()
			.Add(10, Action(3, type: 0))
			.Add(12, template)
			.Add(13, flyer)
			.Add(14, structure)
			.Add(15, order)
			.Add(16, waiting));

		var namers = ActionEditing.Namers(document, 3);

		Assert.Equal(new MsnRecordId(16, 1), Assert.Single(namers.Groups));
		Assert.Equal([new MsnRecordId(12, 1), new MsnRecordId(13, 0), new MsnRecordId(14, 0)], namers.Objects);
		Assert.Equal(new MsnRecordId(15, 0), Assert.Single(namers.Orders));
	}

	/// <summary>
	/// Catches a delete that takes only the record supplying the action here, leaving a conditioned record of the GUID to bring
	/// it back in another state, and one made as separate edits that an undo cannot put back whole.
	/// </summary>
	[Fact]
	public void DeleteTakesEveryRecordOfTheAction() {
		var conditioned = Action(3, type: 0);
		conditioned[MissionAction82.ConditionRefWord] = 20;
		var document = Open(Mission()
			.Add(1, Condition(guid: 20, condition: -1, type: 0, lower: 5, upper: 0x119, operand: 1))
			.Add(10, Action(3, type: 1))
			.Add(10, conditioned));
		byte[] opened = Write(document);

		var change = ActionEditing.Delete(document, 3);

		Assert.Empty(document.Mission.Actions!);
		document.Undo(change);
		Assert.Equal(opened, Write(document));
	}

	/// <summary>
	/// Catches a remove that unsets the slot rather than moving the later areas up, which leaves a gap DBSIM stops at, so
	/// every area after it is never tested.
	/// </summary>
	[Fact]
	public void RemovingAnAreaMovesTheLaterOnesUp() {
		var action = Action(3, type: 0);
		action[MissionAction82.AreaRefsWord] = 4;
		action[MissionAction82.AreaRefsWord + 1] = 5;
		action[MissionAction82.AreaRefsWord + 2] = 6;
		var document = Open(Mission()
			.Add(6, Point(1, 100))
			.Add(9, Circle(4, 1))
			.Add(9, Circle(5, 1))
			.Add(9, Circle(6, 1))
			.Add(10, action));

		ActionEditing.RemoveArea(document, 3, 0);

		Assert.Equal(new short[] { 5, 6, -1, -1 }, ActionEditing.Areas(document, 3));
		Assert.Equal(new short[] { 5, 6, -1, -1, -1, -1, -1, -1 }, document.Mission.Actions![0].AreaRefs);
	}

	/// <summary>
	/// Catches the pair count <c>0x1A</c> left stale after an add or a remove, and a remove that unsets the pair rather than
	/// moving the later pairs up.
	/// </summary>
	[Fact]
	public void PairCountFollowsTheFilledPairs() {
		var document = Open(Mission().Add(10, Action(3, type: 0)));

		ActionEditing.AddPair(document, 3);
		ActionEditing.AddPair(document, 3);
		ActionEditing.SetPair(document, 3, 1, 42, ActionEditing.CounterClear);
		Assert.Equal(2, document.Mission.Actions![0].Unk1A);

		ActionEditing.RemovePair(document, 3, 0);

		var record = document.Mission.Actions![0];
		Assert.Equal(1, record.Unk1A);
		Assert.Equal(((short)42, ActionEditing.CounterClear), ActionEditing.CounterPairs(document, 3)[0]);
		Assert.Equal(((short)-1, (short)-1), ActionEditing.CounterPairs(document, 3)[1]);
	}

	/// <summary>
	/// Catches the message written as the id itself, which DBSIM's load reads as the next line, and none written as <c>-1</c>
	/// rather than 0, which DBSIM reads as line <c>-2</c>.
	/// </summary>
	[Fact]
	public void MessageIsStoredPlusOne() {
		var document = Open(Mission().Add(10, Action(3, type: 0)));

		ActionEditing.SetMessage(document, 3, 1);
		Assert.Equal(2, document.Mission.Actions![0].MessageId);
		Assert.Equal(1, ActionEditing.Message(document, 3));

		ActionEditing.SetMessage(document, 3, -1);
		Assert.Equal(0, document.Mission.Actions![0].MessageId);
	}

	/// <summary>
	/// Catches a type change to 7 to 10 that keeps whatever the target held, which the load then reads as a herc of that GUID,
	/// and one back to 0 to 6 that leaves the target set.
	/// </summary>
	[Fact]
	public void ATypeChangeGivesATargetAndTakesItAway() {
		var document = Open(Mission().Add(10, Action(3, type: 0, target: 77)));

		ActionEditing.SetType(document, 3, 10);
		Assert.Equal(Squad, ActionEditing.Target(document, 3));

		ActionEditing.SetType(document, 3, 1);
		Assert.Equal(-1, document.Mission.Actions![0].TargetRef);
	}

	/// <summary>
	/// Catches a wiring unset written only into the record supplying the field, which leaves another record of the GUID to
	/// overlay the action back, a conditioned one in another state.
	/// </summary>
	[Fact]
	public void ADeploymentUnsetIsWrittenToEveryRecordOfTheGroup() {
		var waiting = Group(11, Hercs, position: -1, condition: -1);
		waiting[MissionGroup164.DeploymentActionRefWord] = 3;
		var conditioned = Group(11, Hercs, position: -1, condition: 20);
		conditioned[MissionGroup164.DeploymentActionRefWord] = 3;
		var document = Open(Mission()
			.Add(1, Condition(guid: 20, condition: -1, type: 0, lower: 5, upper: 0x119, operand: 1))
			.Add(10, Action(3, type: 0))
			.Add(16, waiting)
			.Add(16, conditioned));

		ActionWiring.SetDeployment(document, 11, -1);

		Assert.All(document.Mission.Groups!.Where(group => group.GUID == 11), group => Assert.Equal(-1, group.DeploymentActionRef));
		Assert.NotNull(ActionWiring.WhyNoDeployment(document, Squad));
	}

	/// <summary>
	/// Catches a defeated action written into the template a herc picks, which every herc picking it shares, rather
	/// than into an overlay record of the herc's own GUID; and None accepted for a field the template supplies, which an overlay
	/// cannot unset.
	/// </summary>
	[Fact]
	public void AKeyedHercsActionGoesIntoAnOverlayRecord() {
		var template = Mech(-1, type: 0, condition: 41);
		template[MechRosterEntry144.DefeatActionRefWord] = 3;
		var document = Open(Mission()
			.Add(10, Action(3, type: 0))
			.Add(10, Action(4, type: 0))
			.Add(12, template)
			.Add(12, Mech(2, type: 0, tableKey: 7))
			.Add(16, Orders(Group(11, Hercs, position: -1, condition: -1, 2), 21))
			.Add(15, GroupOrder(21, Squad)));
		var field = MsnField.RosterAction(12, defeated: true);
		Assert.NotNull(ActionWiring.WhyNoNone(document, field, 2));

		ActionWiring.SetObjectAction(document, 12, 2, defeated: true, 4);

		Assert.Equal(3, document.Mission.Mechs![1].DefeatActionRef);
		Assert.Equal(4, document.Mission.Mechs!.Last().DefeatActionRef);
		Assert.Equal(2, document.Mission.Mechs!.Last().GUID);
		var herc = document.Script.Mechs[Exported(document, 12, 2)!.Value];
		Assert.Equal(Exported(document, 10, 4), herc.DefeatActionRef);
	}

	/// <summary>
	/// Catches an order's ending action written into an order another group's slot runs too, which ends that group's order as
	/// well, rather than into a copy for the edited slot.
	/// </summary>
	[Fact]
	public void AnEndingActionOnASharedOrderCopiesItFirst() {
		var document = Open(Mission()
			.Add(10, Action(3, type: 0))
			.Add(15, GroupOrder(21, Squad))
			.Add(16, Orders(Group(11, Hercs, position: -1, condition: -1), 21))
			.Add(16, Orders(Group(12, Hercs, position: -1, condition: -1), 21)));

		GroupOrders.SetAction(document, 11, 0, 3);

		Assert.Equal(-1, Array.Find(document.Mission.Orders!, order => order.GUID == 21)!.ActionRef);
		short copy = GroupOrders.Orders(document, 11)[0];
		Assert.NotEqual(21, copy);
		Assert.Equal(3, Array.Find(document.Mission.Orders!, order => order.GUID == copy)!.ActionRef);
	}

	/// <summary>
	/// Catches a delay past a short accepted, which DBSIM's sign-extending load reads as negative and fires at once, and the
	/// fired slots left with a gap after a remove.
	/// </summary>
	[Fact]
	public void TimerDelayIsKeptToAPositiveShortAndFiredSlotsStayFilled() {
		var document = Open(Mission()
			.Add(10, Action(3, type: 0))
			.Add(10, Action(4, type: 0))
			.Add(11, Timer(30, start: -1, 3, 4)));

		Assert.Throws<ArgumentOutOfRangeException>(() => TimerEditing.SetDelay(document, 30, TimerEditing.MaxDelay + 1));
		TimerEditing.SetDelay(document, 30, TimerEditing.MaxDelay);
		TimerEditing.RemoveFired(document, 30, 0);

		Assert.Equal(TimerEditing.MaxDelay, document.Mission.ActionTimers![0].Delay);
		Assert.Equal(new short[] { 4, -1 }, TimerEditing.Fired(document, 30)[..2]);
	}

	/// <summary>
	/// Catches a new timer appended before its new action has a GUID, or without its fired slot 0, which leaves it firing
	/// nothing, and the pair made as separate edits that an undo cannot put back whole.
	/// </summary>
	[Fact]
	public void ANewTimerFiresItsOwnNewAction() {
		var document = Open(Mission().Add(10, Action(3, type: 0)));
		byte[] opened = Write(document);

		var change = DelayedActions.Create(document, 3, out short timer, out short action);

		Assert.Equal(3, TimerEditing.Start(document, timer));
		Assert.Equal(action, TimerEditing.Fired(document, timer)[0]);
		Assert.Equal(action, DelayedActions.Of(document, timer));
		document.Undo(change);
		Assert.Equal(opened, Write(document));
	}

	/// <summary>
	/// Catches a removal that stops at the first timer, so deleting its own action is refused by the timer that action starts,
	/// and one that deletes the action before unsetting the group waiting on it.
	/// </summary>
	[Fact]
	public void RemovingATimerTakesTheTimersItsOwnActionStarts() {
		var waiting = Group(11, Hercs, position: -1, condition: -1);
		waiting[MissionGroup164.DeploymentActionRefWord] = 4;
		var document = Open(Mission()
			.Add(10, Action(3, type: 0))
			.Add(10, Action(4, type: 0))
			.Add(10, Action(5, type: 0))
			.Add(11, Timer(30, start: 3, 4))
			.Add(11, Timer(31, start: 4, 5))
			.Add(16, waiting));

		DelayedActions.Remove(document, 30);

		Assert.Empty(document.Mission.ActionTimers ?? []);
		Assert.Equal([(short)3], document.Mission.Actions!.Select(record => record.GUID));
		Assert.Equal(-1, Array.Find(document.Mission.Groups!, group => group.GUID == 11)!.DeploymentActionRef);
	}

	/// <summary>
	/// Catches the two action checks this editor's edits can trip dropped from the mission check: a type 7 to 10 action with no
	/// target, and a message on an action that writes no counter, which DBSIM never posts; and either filed under the other's
	/// kind, which the Problems list phrases each finding by.
	/// </summary>
	[Fact]
	public void MissionCheckFindsATargetlessTypeAndAnUnpostedMessage() {
		var silent = Action(4, type: 0);
		silent[MissionAction82.MessageIdWord] = 1;
		var document = Open(Mission().Add(10, Action(3, type: 7)).Add(10, silent));

		var findings = ScriptDatLint.Check(document.Script).Where(finding => finding.Block == ScriptBlock.Actions).ToList();

		Assert.Equal([(0, ScriptLintKind.NoTarget), (1, ScriptLintKind.MessageNeverPosted)],
			findings.Select(finding => (finding.Index, finding.Kind)));
	}

	// Group 0, the squad: the player's machine, facing a heading of its own; and the two row-1 conditions a type-7 random table key
	// picks its template by, the child holding a roll of 0.
	private static SyntheticMsn Mission() {
		var squad = Group(Squad, Hercs, position: -1, condition: -1, 0);
		squad[MissionGroup164.HeadingRefWord] = 0;
		return new SyntheticMsn()
			.Add(1, Condition(guid: 40, condition: -1, type: 3, lower: 7, upper: 0, operand: 1))
			.Add(1, Condition(guid: 41, condition: 40, type: 2, lower: 0, upper: 5, operand: 0))
			.Add(7, Heading(0, 0))
			.Add(12, Mech(0, type: 0))
			.Add(16, squad);
	}

	// A row-11 timer started by row-10 GUID <start> (or from mission start), of 10 seconds, firing <fired> from slot 0.
	private static short[] Timer(short guid, short start, params short[] fired) {
		var words = Enumerable.Repeat((short)-1, 15).ToArray();
		words[MapObject.GUIDWord] = guid;
		words[ActionTimer30.PrimaryActionRefWord] = start;
		words[ActionTimer30.DelayWord] = 10;
		fired.CopyTo(words, ActionTimer30.SequenceRefsWord);
		return words;
	}

	// <group> with the row-15 GUIDs <orders> in its first order slots.
	private static short[] Orders(short[] group, params short[] orders) {
		orders.CopyTo(group, MissionGroup164.OrderRefsWord);
		return group;
	}

	private static MissionDocument Open(SyntheticMsn msn) =>
		MissionDocument.Read(msn.Bytes(), null, new MissionGeneration(new short[MissionGenerator.CampaignFlagCount],
			new short?[MissionGeneration.HeaderWords], () => _ => 0));

	private static byte[] Write(MissionDocument document) => new MissionFileTransformer().Write(document.Mission)!;
}
