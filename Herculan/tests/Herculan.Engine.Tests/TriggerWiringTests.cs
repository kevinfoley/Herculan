using HercWorks.Core.Data.File.Msn;
using HercWorks.Core.Io.Transform.Common;
using Herculan.Engine.Editor;
using Herculan.Engine.Scene;
using Xunit;
using static Herculan.Engine.Tests.SyntheticMsn;

namespace Herculan.Engine.Tests;

/// <summary>
/// The trigger view's wiring over small synthetic missions: what <see cref="MissionIndex"/> lists as firing a trigger and
/// following it (<see cref="TriggerWiring"/>), and the record each Then entry writes (<see cref="TriggerFollowers"/>).
/// </summary>
public class TriggerWiringTests {
	private const short Hercs = 0;
	private const short Structures = 2;
	private const short Squad = 50;
	private const short Trigger = 3;
	private const short Other = 4;

	/// <summary>
	/// Catches the index reading only a countdown's starting trigger and not its fired slots (or the two swapped), an object's
	/// engaged and defeated triggers swapped, a squad HERC listed as firing one (its record fires nothing in play), and a
	/// waiting group, an order's ending trigger or a started countdown missed from what follows.
	/// </summary>
	[Fact]
	public void TheIndexListsWhatFiresATriggerAndWhatFollowsIt() {
		var squadHerc = Mech(0, type: 0);
		squadHerc[MechRosterEntry144.EngagementActionRefWord] = Trigger;
		var engaged = Mech(5, type: 0);
		engaged[MechRosterEntry144.EngagementActionRefWord] = Trigger;
		var defeated = Base(6);
		defeated[BaseRosterEntry62.DefeatActionRefWord] = Trigger;
		var msn = Wired()
			.Add(11, Countdown(31, start: -1, Trigger))
			.Add(12, squadHerc).Add(12, engaged)
			.Add(14, defeated)
			.Add(16, Orders(Group(11, Hercs, position: -1, condition: -1, 5), 21))
			.Add(16, Group(12, Structures, position: -1, condition: -1, 6));

		var links = Index(msn).Links(Trigger);

		Assert.Equal([(short)31], links.FiringCountdowns);
		Assert.Equal([new ObjectKey(12, 5)], links.Engaged);
		Assert.Equal([new ObjectKey(14, 6)], links.Defeated);
		Assert.Equal([new BringsInGroup(13), new EndsOrder(11, 0), new StartsCountdown(30)], links.Follows);
		Assert.Equal([(short)30], Index(msn).Links(Other).FiringCountdowns);
	}

	/// <summary>
	/// Catches a Then entry written in the wrong record (the trigger's own, or another entry's), a remove that leaves the link
	/// or points it at another trigger rather than none, and a new countdown made to run from mission start rather than from
	/// the trigger.
	/// </summary>
	[Fact]
	public void EachThenEntryWritesAndClearsTheRecordHoldingTheLink() {
		var document = Open(Mission()
			.Add(10, Action(Trigger, type: 0))
			.Add(11, Countdown(30, start: -1))
			.Add(12, Mech(5, type: 0))
			.Add(15, GroupOrder(21, Squad))
			.Add(16, Orders(Group(11, Hercs, position: -1, condition: -1, 5), 21))
			.Add(16, Group(13, Hercs, position: -1, condition: -1)));
		var trigger = Record(document);
		TriggerFollower[] follows = [new BringsInGroup(13), new EndsOrder(11, 0), new StartsCountdown(30)];

		foreach (var follower in follows) {
			TriggerFollowers.Add(document, Trigger, follower);
		}

		Assert.Equal(Trigger, GroupRecord(document, 13).DeploymentActionRef);
		Assert.Equal(Trigger, Array.Find(document.Mission.Orders!, order => order.GUID == 21)!.ActionRef);
		Assert.Equal(Trigger, Array.Find(document.Mission.ActionTimers!, countdown => countdown.GUID == 30)!.PrimaryActionRef);
		Assert.Equal(follows, Index(document).Links(Trigger).Follows);
		Assert.Equal(trigger, Record(document));

		foreach (var follower in follows) {
			TriggerFollowers.Remove(document, follower);
		}

		Assert.Equal(-1, GroupRecord(document, 13).DeploymentActionRef);
		Assert.Equal(-1, Array.Find(document.Mission.Orders!, order => order.GUID == 21)!.ActionRef);
		Assert.Equal(-1, Array.Find(document.Mission.ActionTimers!, countdown => countdown.GUID == 30)!.PrimaryActionRef);
		Assert.Empty(Index(document).Links(Trigger).Follows);
		Assert.Equal(trigger, Record(document));

		TimerEditing.Create(document, out short created, Trigger);
		Assert.Equal([new StartsCountdown(created)], Index(document).Links(Trigger).Follows);
	}

	// Group 0, the squad, with herc 0; trigger 3, which group 13 waits on, order 21 (group 11's first) ends and countdown 30
	// starts; trigger 4, which countdown 30 fires.
	private static SyntheticMsn Wired() {
		var waiting = Group(13, Hercs, position: -1, condition: -1);
		waiting[MissionGroup164.DeploymentActionRefWord] = Trigger;
		var order = GroupOrder(21, Squad);
		order[MissionOrder22.ActionRefWord] = Trigger;
		return Mission()
			.Add(10, Action(Trigger, type: 0))
			.Add(10, Action(Other, type: 0))
			.Add(11, Countdown(30, start: Trigger, Other))
			.Add(15, order)
			.Add(16, waiting);
	}

	// Group 0, the squad, of herc 0, facing a heading of its own.
	private static SyntheticMsn Mission() {
		var squad = Group(Squad, Hercs, position: -1, condition: -1, 0);
		squad[MissionGroup164.HeadingRefWord] = 0;
		return new SyntheticMsn().Add(7, Heading(0, 0)).Add(16, squad);
	}

	// A row-11 countdown started by row-10 GUID <start> (or from mission start), of 10 seconds, firing <fired> from slot 0.
	private static short[] Countdown(short guid, short start, params short[] fired) {
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

	private static MissionIndex Index(SyntheticMsn msn) =>
		Index(MissionLayout.Build(new MissionFileTransformer().Parse(msn.Bytes())!));

	private static MissionIndex Index(MissionDocument document) => Index(document.Layout);

	private static MissionIndex Index(MissionLayout layout) =>
		new(LayoutPlacement.Place(layout, LayoutFeaturesTests.Tables), new Dictionary<ObjectKey, SceneObject>());

	private static MissionGroup164 GroupRecord(MissionDocument document, short guid) =>
		Array.Find(document.Mission.Groups!, group => group.GUID == guid)!;

	// The trigger's only record, every field.
	private static short[] Record(MissionDocument document) {
		var action = Assert.Single(document.Mission.Actions!);
		return [action.GUID, action.ConditionRef, action.Unk04, action.Type, action.Verb, .. action.AreaRefs, action.Unk1A,
			.. action.CounterPairs, .. action.TextRefs, action.MessageId, action.TargetRef];
	}

	private static MissionDocument Open(SyntheticMsn msn) =>
		MissionDocument.Read(msn.Bytes(), null, new MissionGeneration(new short[MissionGenerator.CampaignFlagCount],
			new short?[MissionGeneration.HeaderWords], () => _ => 0));
}
