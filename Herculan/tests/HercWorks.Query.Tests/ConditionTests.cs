using HercWorks.Core.Data.File.Msn;
using Xunit;

namespace HercWorks.Query.Tests;

/// <summary>The condition and counter-operation readings, on hand-built rows.</summary>
public class ConditionTests {
	private static MissionFile WithConditions(params MissionCondition14[] conditions) => new() { Conditions = conditions };

	[Theory]
	[InlineData(0x119, "==")]
	[InlineData(0x11a, "!=")]
	[InlineData(0x11b, "<")]
	[InlineData(0x11c, "<=")]
	[InlineData(0x11d, ">")]
	[InlineData(0x11e, ">=")]
	public void FlagComparison(short code, string op) {
		var file = WithConditions(new MissionCondition14(23, -1, 0, 625, code, 0, 0));
		Assert.Equal($"flag 625 {op} 0", Conditions.Describe(file, 23));
	}

	[Fact]
	public void UnknownOperatorFailsAndNoConditionIsNull() {
		var file = WithConditions(new MissionCondition14(5, -1, 0, 8, 0x200, 1, 0));
		Assert.Equal("flag 8 op 0x200 (fails)", Conditions.Describe(file, 5));
		Assert.Null(Conditions.Describe(file, -1));
		Assert.Equal("cond 9 (no row-1 record)", Conditions.Describe(file, 9));
	}

	[Fact]
	public void VariantRangeNamesItsKeyAndChainsAreJoined() {
		var file = WithConditions(
			new MissionCondition14(30, -1, 3, 194, -1, 100, 0),
			new MissionCondition14(31, 30, 2, 0, 49, 0, 0),
			new MissionCondition14(40, -1, 0, 8, 0x119, 1, 0),
			new MissionCondition14(41, 40, 1, 11, -1, 0, 0));

		Assert.Equal("variant 0-49 of key 194", Conditions.Describe(file, 31));
		Assert.Equal((short)194, Conditions.VariantKeyOf(file, 31));
		Assert.Null(Conditions.VariantKeyOf(file, 40));
		Assert.Equal("flag 8 == 1 and draw 41 below 11", Conditions.Describe(file, 41));
	}

	[Fact]
	public void SharedGuidIsEitherRecord() {
		var file = WithConditions(
			new MissionCondition14(7, -1, 0, 8, 0x119, 1, 0),
			new MissionCondition14(7, -1, 0, 9, 0x11d, 2, 0));
		Assert.Equal("(flag 8 == 1) or (flag 9 > 2)", Conditions.Describe(file, 7));
	}

	[Theory]
	[InlineData((int)CounterLayer.OutOfAction, 2, "add 1")]
	[InlineData((int)CounterLayer.OutOfAction, 0x0d, "store 1")]
	[InlineData((int)CounterLayer.OutOfAction, 6, "op 6: nothing")]
	[InlineData((int)CounterLayer.Action, 6, "add 1")]
	[InlineData((int)CounterLayer.Objective, 7, "subtract 1")]
	public void CounterOperationsDependOnTheLayer(int layer, short op, string effect) =>
		Assert.Equal(effect, CounterOps.Describe((CounterLayer)layer, op));
}
