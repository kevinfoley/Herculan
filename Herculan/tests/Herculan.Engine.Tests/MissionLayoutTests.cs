using HercWorks.Core.Data.File.Msn;
using HercWorks.Core.Io.Transform.Common;
using Xunit;
using static Herculan.Engine.Tests.SyntheticMsn;

namespace Herculan.Engine.Tests;

/// <summary><see cref="MissionLayout"/> over small synthetic missions.</summary>
public class MissionLayoutTests {
	private const short FlagTest = 0;
	private const short RangeType = 2;
	private const short TableType = 3;
	private const short OpGreater = 0x11d;

	/// <summary>
	/// Catches a layout that merges every record of a GUID whatever its condition (the variation's type reaching the base),
	/// and one whose base stops at the first record (the later unconditioned record's weapon not reaching it).
	/// </summary>
	[Fact]
	public void VariationStaysOutOfTheBase() {
		var msn = new SyntheticMsn();
		msn.Add(1, Condition(guid: 30, condition: -1, type: FlagTest, lower: 8, upper: OpGreater, operand: 0));
		msn.Add(12, Mech(1, type: 5));
		msn.Add(12, Mech(1, type: 9, condition: 30));
		msn.Add(12, Mech(1, type: -1, weapons: [4]));
		var layout = Layout(msn);

		var herc = layout.Entry<MechRosterEntry144>(1)!;
		Assert.Equal(5, herc.Fields.TypeIndex);
		Assert.Equal(4, herc.Fields.WeaponRefs[0]);
		Assert.Equal(new MsnRecordId(12, 2), herc.Supplier(MechRosterEntry144.WeaponRefsWord).Record);
		var variation = Assert.Single(herc.Variations);
		Assert.Equal((new MsnRecordId(12, 1), (short)30), (variation.Record, variation.Condition));
		Assert.Equal(9, ((MechRosterEntry144)variation.Fields).TypeIndex);
	}

	/// <summary>
	/// Catches a keyed herc left at its own point instead of copying its shown option's, and an option chosen by file order
	/// rather than its range's row-1 order.
	/// </summary>
	[Fact]
	public void KeyedHercStandsAtItsFirstOption() {
		var msn = new SyntheticMsn();
		msn.Add(1, Condition(guid: 10, condition: -1, type: TableType, lower: 7, upper: 0, operand: 6));
		msn.Add(1, Condition(guid: 11, condition: 10, type: RangeType, lower: 0, upper: 2, operand: 0));
		msn.Add(1, Condition(guid: 12, condition: 10, type: RangeType, lower: 3, upper: 5, operand: 0));
		msn.Add(12, Positioned(Mech(-1, type: 2, condition: 12), 60));
		msn.Add(12, Positioned(Mech(-1, type: 3, condition: 11), 50));
		msn.Add(12, Positioned(Mech(1, type: 4, tableKey: 7), 70));
		var layout = Layout(msn);

		var herc = layout.Entry<MechRosterEntry144>(1)!;
		Assert.Equal(50, herc.Fields.PositionRef);
		Assert.Equal(new WordSource(new MsnRecordId(12, 2), new MsnRecordId(12, 1)), herc.Supplier(MechRosterEntry144.PositionRefWord));
		var pick = Assert.Single(herc.Presence.Picks);
		Assert.Equal((short)10, pick.Table);
		Assert.Equal(new LayoutOption(11, new MsnRecordId(12, 1)), pick.Shown);
	}

	private static short[] Positioned(short[] mech, short point) {
		mech[MechRosterEntry144.PositionRefWord] = point;
		return mech;
	}

	private static MissionLayout Layout(SyntheticMsn msn) => MissionLayout.Build(new MissionFileTransformer().Parse(msn.Bytes())!);
}
