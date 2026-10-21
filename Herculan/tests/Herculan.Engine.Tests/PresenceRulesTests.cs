using HercWorks.Core.Data.File.Msn;
using HercWorks.Core.Io.Transform.Common;
using Xunit;
using static Herculan.Engine.Tests.SyntheticMsn;

namespace Herculan.Engine.Tests;

/// <summary>
/// <see cref="PresenceRules"/> over small synthetic missions: a Present when stated as clauses builds its row-1 chain, reusing
/// an identical flag test and never another record's draw, keeps a chance this record alone uses in place, cleans up what
/// nothing references any more, reads back as the clauses written, and is one step; and <see cref="CampaignVariableUses"/>
/// and <see cref="RecordAspects"/>, which the Present when view names variables and variations by.
/// </summary>
public class PresenceRulesTests {
	private const short Structures = 2;
	private const short Squad = 9;
	private const short Greater = MissionGenerator.OpGreater;

	private static readonly PresenceTarget First = PresenceTarget.Of(14, 1);
	private static readonly PresenceTarget Second = PresenceTarget.Of(14, 2);
	private static readonly PresenceTarget Third = PresenceTarget.Of(14, 3);

	/// <summary>Catches a variable clause always appending a flag test: one testing the same variable, operator and value, requiring nothing, is reused.</summary>
	[Fact]
	public void AnIdenticalFlagTestIsReused() {
		var document = Open(Structures3()
			.Add(1, Condition(guid: 1, condition: -1, type: 0, lower: 30, upper: Greater, operand: 0)), firstCondition: 1);
		int conditions = ConditionEditing.Records(document).Length;

		PresenceRules.SetRule(document, Second, Rule(null, (30, Greater, 0)), out _);

		Assert.Equal(conditions, ConditionEditing.Records(document).Length);
		Assert.Equal(1, BaseCondition(document, 2));
	}

	/// <summary>
	/// Catches the cleanup leaving an orphaned flag test or draw behind, or removing one another record still references:
	/// back to Always removes the third structure's own test, draw and range, and keeps the test the first two share.
	/// </summary>
	[Fact]
	public void AlwaysRemovesWhatNothingReferencesAndKeepsWhatIsShared() {
		var document = Open(Structures3()
			.Add(1, Condition(guid: 1, condition: -1, type: 0, lower: 30, upper: Greater, operand: 0)), firstCondition: 1);
		RecordConditions.SetPresence(document, Second, 1, out _);
		int conditions = ConditionEditing.Records(document).Length;
		PresenceRules.SetRule(document, Third, Rule(ChanceClause.OfPercent(50), (31, Greater, 0)), out _);
		Assert.Equal(conditions + 3, ConditionEditing.Records(document).Length);

		PresenceRules.SetRule(document, Third, PresenceRule.Always, out _);
		PresenceRules.SetRule(document, First, PresenceRule.Always, out _);

		Assert.Equal(conditions, ConditionEditing.Records(document).Length);
		Assert.NotNull(ConditionEditing.IndexOf(document, 1));
		Assert.Equal(1, BaseCondition(document, 2));
	}

	/// <summary>
	/// Catches a new chance reusing another record's draw, which would make the two records' rolls one, so they appear
	/// together: the second structure gets a draw of its own, and the first's draw and range are left as they were.
	/// </summary>
	[Fact]
	public void ANewChanceHasADrawOfItsOwn() {
		var document = Open(Structures3()
			.Add(1, Condition(guid: 1, condition: -1, type: 1, lower: 11, upper: 0, operand: 0))
			.Add(1, Condition(guid: 2, condition: 1, type: 2, lower: 0, upper: 9, operand: 0)), firstCondition: 2);

		PresenceRules.SetRule(document, Second, Rule(ChanceClause.OfPercent(50)), out _);

		short range = BaseCondition(document, 2);
		Assert.NotEqual(1, ConditionEditing.ParentOf(document, range)!.GUID);
		Assert.Equal(11, ConditionEditing.Record(document, 1).FlagIndexOrRangeLower);
		Assert.Equal(9, ConditionEditing.Record(document, 2).OperatorOrRangeUpperOrResult);
	}

	/// <summary>
	/// Catches the chain built or read in the wrong order, and a draw that does not require the variables (a roll made even
	/// when they fail): a rule reads back as written, its draw requiring the last variable test, which requires the first.
	/// </summary>
	[Fact]
	public void ARuleReadsBackAndItsDrawRequiresTheVariables() {
		var document = Open(Structures3(), firstCondition: -1);
		var rule = Rule(ChanceClause.OfPercent(25), (30, Greater, 0), (31, MissionGenerator.OpLessOrEqual, 5));

		PresenceRules.SetRule(document, First, rule, out _);

		Assert.Equal(rule, PresenceRules.Read(document, First).Rule);
		var draw = ConditionEditing.ParentOf(document, BaseCondition(document, 1))!;
		var last = ConditionEditing.Record(document, draw.ConditionRef);
		Assert.Equal(31, last.FlagIndexOrRangeLower);
		Assert.Equal(30, ConditionEditing.Record(document, last.ConditionRef).FlagIndexOrRangeLower);
	}

	/// <summary>
	/// Catches a change to a chance only this record uses making a new draw, which moves every later roll: a new percent
	/// rewrites the range in place, and a variable added after it goes in before the draw, which then requires it.
	/// </summary>
	[Fact]
	public void AnOwnChanceIsRewrittenInPlace() {
		var document = Open(Structures3(), firstCondition: -1);
		PresenceRules.SetRule(document, First, Rule(ChanceClause.OfPercent(50)), out _);
		short range = BaseCondition(document, 1);
		short draw = ConditionEditing.ParentOf(document, range)!.GUID;
		int conditions = ConditionEditing.Records(document).Length;

		PresenceRules.SetRule(document, First, Rule(ChanceClause.OfPercent(20)), out _);
		Assert.Equal(conditions, ConditionEditing.Records(document).Length);
		Assert.Equal(range, BaseCondition(document, 1));
		Assert.Equal(19, ConditionEditing.Record(document, range).OperatorOrRangeUpperOrResult);

		PresenceRules.SetRule(document, First, Rule(ChanceClause.OfPercent(20), (30, Greater, 0)), out _);
		Assert.Equal(range, BaseCondition(document, 1));
		short test = ConditionEditing.Record(document, draw).ConditionRef;
		Assert.True(ConditionEditing.IndexOf(document, test) < ConditionEditing.IndexOf(document, draw));
	}

	/// <summary>Catches a roll two records share being rewritten from one of them, which changes the other too: it reads, and is refused.</summary>
	[Fact]
	public void ASharedRollIsReadButNotSet() {
		var document = Open(Structures3()
			.Add(1, Condition(guid: 1, condition: -1, type: 1, lower: 11, upper: 0, operand: 0))
			.Add(1, Condition(guid: 2, condition: 1, type: 2, lower: 0, upper: 9, operand: 0)), firstCondition: 2);
		RecordConditions.SetPresence(document, Second, 2, out _);

		var reading = PresenceRules.Read(document, First);

		Assert.Equal(91, reading.Rule!.Chance!.Value.Percent);
		Assert.NotNull(reading.WhyFixed);
		Assert.Throws<InvalidOperationException>(() => PresenceRules.SetRule(document, First, Rule(ChanceClause.OfPercent(50)), out _));
	}

	/// <summary>Catches the new flag test made as a step of its own beside the set: one undo takes back both, so the structure is back.</summary>
	[Fact]
	public void ARuleIsOneStep() {
		var document = Open(Structures3(), firstCondition: -1);
		int conditions = ConditionEditing.Records(document).Length;

		var change = PresenceRules.SetRule(document, First, Rule(null, (30, Greater, 0)), out _);

		Assert.Null(document.ExportedIndex(14, 1));
		document.Undo(change);
		Assert.Equal(conditions, ConditionEditing.Records(document).Length);
		Assert.Equal(-1, BaseCondition(document, 1));
		Assert.NotNull(document.ExportedIndex(14, 1));
	}

	/// <summary>
	/// Catches a source of campaign variable uses left out or an operation read as another: a flag test, an action's add and
	/// clear, an objective's set and a structure's out-of-action report are each counted under their variable and way.
	/// </summary>
	[Fact]
	public void EveryWayAMissionTouchesAVariableIsCounted() {
		var action = Action(1, type: 1);
		new short[] { 30, ActionEditing.CounterIncrement, 31, ActionEditing.CounterClear }.CopyTo(action, MissionAction82.CounterPairsWord);
		var objective = Objective(condition: -1);
		new short[] { 30, ObjectiveEditing.CounterSet }.CopyTo(objective, MissionObjective58.PairsWord);
		var structure = Base(1);
		new short[] { 32, 2 }.CopyTo(structure, BaseRosterEntry62.OutOfActionReportWord);
		var mission = new MissionFileTransformer().Parse(new SyntheticMsn()
			.Add(1, Condition(guid: 0, condition: -1, type: 0, lower: 30, upper: Greater, operand: 0))
			.Add(10, action).Add(14, structure).Add(17, objective).Bytes())!;

		var uses = CampaignVariableUses.Of(mission).ToDictionary(use => use.Variable, use => use.Counts);

		Assert.Equal(new short[] { 30, 31, 32 }, uses.Keys.ToArray());
		Assert.Equal(new Dictionary<VariableUse, int> {
			[VariableUse.Tested] = 1, [VariableUse.TriggerAdds] = 1, [VariableUse.ObjectiveSets] = 1,
		}, uses[30]);
		Assert.Equal(new Dictionary<VariableUse, int> { [VariableUse.TriggerClears] = 1 }, uses[31]);
		Assert.Equal(new Dictionary<VariableUse, int> { [VariableUse.OutOfFight] = 1 }, uses[32]);
	}

	/// <summary>Catches the word map naming a herc's fields wrongly: a variation that sets only the chassis and a weapon is its chassis and weapons.</summary>
	[Fact]
	public void AVariationIsNamedByTheFieldsItSets() {
		var document = Open(new SyntheticMsn()
			.Add(1, Condition(guid: 0, condition: -1, type: 0, lower: 30, upper: Greater, operand: 0))
			.Add(7, Heading(0, 0))
			.Add(12, Mech(0, type: 0))
			.Add(12, Mech(5, type: 3))
			.Add(12, Mech(5, type: 4, condition: 0, weapons: [7]))
			.Add(16, SquadGroup()), firstCondition: -1);

		var variation = Assert.Single(document.Layout.Entry(12, 5)!.Variations);

		Assert.Equal(new[] { RecordAspect.Type, RecordAspect.Weapons }, RecordAspects.Of(12, variation.Words).ToArray());
	}

	// The squad, its herc facing a heading of its own; Open adds three structures in one group.
	private static SyntheticMsn Structures3() => new SyntheticMsn()
		.Add(7, Heading(0, 0))
		.Add(12, Mech(0, type: 0))
		.Add(16, SquadGroup());

	private static short[] SquadGroup() {
		var squad = Group(Squad, 0, position: -1, condition: -1, 0);
		squad[MissionGroup164.HeadingRefWord] = 0;
		return squad;
	}

	// <msn> with three structures in one group, the first under <firstCondition>, opened with every flag 0.
	private static MissionDocument Open(SyntheticMsn msn, short firstCondition) {
		msn.Add(14, Base(1, condition: firstCondition)).Add(14, Base(2)).Add(14, Base(3))
			.Add(16, Group(10, Structures, position: -1, condition: -1, 1, 2, 3));
		return MissionDocument.Read(msn.Bytes(), null, new MissionGeneration(new short[MissionGenerator.CampaignFlagCount],
			new short?[MissionGeneration.HeaderWords], () => _ => 0));
	}

	private static PresenceRule Rule(ChanceClause? chance, params (short Variable, short Op, short Value)[] clauses) =>
		new(clauses.Select(clause => new VariableClause(clause.Variable, clause.Op, clause.Value)).ToList(), chance);

	private static short BaseCondition(MissionDocument document, short guid) =>
		Assert.Single(document.Mission.Bases!, record => record.GUID == guid).ConditionRef;
}
