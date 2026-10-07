using HercWorks.Core.Data.File.Msn.Script;
using Xunit;

namespace Herculan.Engine.Tests;

/// <summary>
/// The named views over script.dat roster records' raw spans are settable, so an editor can change
/// them without giving up the byte-exact round trip. These pin where each one writes, against the
/// exported offsets in <c>docs/retail/formats/script-dat.md</c>: a mech's head starts at exported 0x00 and
/// its tail at 0x42; a flyer's and a base's actions sit at the same place in their tails.
/// </summary>
public class ScriptRecordFieldTests {
	[Fact]
	public void MechFieldsWriteToTheirExportedOffsets() {
		var mech = new ScriptMechRecord();

		mech.AiRadarActive = 1;
		mech.AiCruiseSpeed = 0x1234;
		mech.EngagementActionRef = 3;
		mech.DefeatActionRef = 4;
		mech.StartingCondition = 50;

		Assert.Equal(1, BitConverter.ToInt16(mech.HeadBytes, 0x00));
		Assert.Equal(0x1234, BitConverter.ToInt16(mech.HeadBytes, 0x02));
		Assert.Equal(3, BitConverter.ToInt16(mech.TailBytes, 0x80 - 0x42));
		Assert.Equal(4, BitConverter.ToInt16(mech.TailBytes, 0x82 - 0x42));
		Assert.Equal(50, BitConverter.ToInt16(mech.TailBytes, 0x84 - 0x42));
		Assert.Equal((1, 0x1234, 3, 4, 50),
			(mech.AiRadarActive, mech.AiCruiseSpeed, mech.EngagementActionRef, mech.DefeatActionRef, mech.StartingCondition));
	}

	[Fact]
	public void FlyerAndBaseActionsShareTheirTailOffsets() {
		var flyer = new ScriptFlyerRecord { EngagementActionRef = 7, DefeatActionRef = 8 };
		var structure = new ScriptBaseRecord { EngagementActionRef = 9, DefeatActionRef = 10 };

		Assert.Equal(7, BitConverter.ToInt16(flyer.TailBytes, 40));
		Assert.Equal(8, BitConverter.ToInt16(flyer.TailBytes, 42));
		Assert.Equal(9, BitConverter.ToInt16(structure.TailBytes, 40));
		Assert.Equal(10, BitConverter.ToInt16(structure.TailBytes, 42));
	}

	[Fact]
	public void WritingPastAShortSpanThrowsRatherThanGrowingIt() {
		var mech = new ScriptMechRecord { TailBytes = new byte[10] };

		Assert.Throws<InvalidOperationException>(() => mech.StartingCondition = 100);
		Assert.Equal(10, mech.TailBytes.Length);
	}
}
