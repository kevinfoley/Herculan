using HercWorks.Core.Data.File.Dat.Shell;
using HercWorks.Core.Data.File.Msn.Es1;
using HercWorks.Core.Io.Transform.Common;
using HercWorks.Core.Io.Transform.Shell;
using Herculan.Engine.Numerics;
using Herculan.Es1.Sim;
using Xunit;

namespace Herculan.Engine.Tests;

/// <summary>
/// The simulator's read of the three <c>data\</c> files (docs/es1/formats/script-dat.md#reading) and the handoff files'
/// readers, over constructed files.
/// </summary>
public class Es1MissionScriptTests {
	/// <summary>
	/// Catches the first group's members marked live, the squad's slots numbered before block 6's, a base member's
	/// group byte not kept, or a squad slot given its entry's pilot word for a type.
	/// </summary>
	[Fact]
	public void SlotToPoolMapsNumberTheLiveSlotsThenTheSquad() {
		var script = Es1MissionScript.Read(Files());

		Assert.Equal([false, true, true, true, true], script.Mechs.Live[..5]);
		Assert.Equal([0, 0, 1, 2, 3], script.Mechs.Map[..5]);
		Assert.Equal([false, false, false, true, true], script.MechSquadSlots[..5]);
		Assert.Equal([5, 3], script.Mechs.Types[3..5]);
		Assert.True(script.Bases.Live[0]);
		Assert.Equal(1, script.BaseGroupBytes[0]);
		Assert.Equal(0, script.Flags[Es1MissionScript.ZeroedFlag]);
	}

	/// <summary>Catches a stored heading read as degrees, as ES2's ×182 is, rather than as quarter turns of its two low bits.</summary>
	[Theory]
	[InlineData(0, 0)]
	[InlineData(180, 0)]
	[InlineData(45, 0x4000)]
	[InlineData(270, -0x8000)]
	[InlineData(315, -0x4000)]
	public void AStoredHeadingTurnsByQuarterTurns(short stored, short angle) =>
		Assert.Equal(angle, Es1MissionScript.DecodeHeading(stored));

	/// <summary>Catches a field read or written out of the block layout's order, in either file.</summary>
	[Fact]
	public void TheHandoffFilesReadBackAsWritten() {
		var files = Files();
		byte[] script = new Es1ScriptDatTransformer().Write(files.Script)!;
		Assert.Equal(script, new Es1ScriptDatTransformer().Write(new Es1ScriptDatTransformer().Parse(script)!));

		byte[] player = new Es1PlayerMecTransformer().Write(files.Player)!;
		Assert.Equal(player, new Es1PlayerMecTransformer().Write(new Es1PlayerMecTransformer().Parse(player)!));
	}

	/// <summary>
	/// Catches <c>Math_Rotate2DPoint</c> taking the matrix's entries in the wrong places: a formation offset turned by a
	/// leader's frame lands where that frame puts the point.
	/// </summary>
	[Theory]
	[InlineData(0x4000)]
	[InlineData(-0x2000)]
	[InlineData(12345)]
	public void AFormationOffsetTurnsWithItsLeader(short heading) {
		var frame = Transform3.FromEuler(0, 0, heading);
		var (x, y) = Es1Formation.Rotate2D(frame, 22500, -15000);
		var placed = frame.TransformPoint(22500, -15000, 0);
		Assert.Equal((placed.X, placed.Y), (x, y));
	}

	private static Es1MissionFiles Files() {
		var script = new Es1ScriptDat {
			Header = [0, 1004, 0, 4, 0, 1, 1, 2, 0, 0],
			Points = [new() { X = 1, Y = 2, Z = 3 }],
			Headings = [45],
			WaypointGroups = [[0, 0]],
			TriggerAreas = [new() { Shape = 1, PointRef = 0, Word0A = 50 }],
			Actions = [new() { Type = 1, Verb = 2, AreaRefs = [0, -1, -1, -1, -1, -1, -1, -1], Word1A = 37, Word1C = 6 }],
			Mechs = [Mech(type: 1), Mech(type: 2), Mech(type: 9)],
			Bases = [new() { Type = 4, PointRef = 0, HeadingRef = -1, Word0E = -1, Word10 = -1, ActionRef12 = -1, ActionRef14 = -1, Word16 = 100 }],
			Groups = [Group(0, [0]), Group(0, [2, -1, 1]), Group(2, [0])],
			Tail = [1, 2, 3],
		};

		var player = new Es1PlayerMec {
			PlayerEntry = 0,
			Entries = [Entry(chassis: 5, word1: 7), Entry(chassis: 3, word1: 4)],
			WeaponsAvailable = Enumerable.Repeat((byte)1, Es1PlayerMec.WeaponCount).ToArray(),
		};

		return new Es1MissionFiles(Enumerable.Range(0, 100).Select(i => (short)i).ToArray(), player, script);
	}

	private static Es1ScriptMech Mech(short type) => new() {
		Type = type, Words32 = Enumerable.Repeat((short)-1, 10).ToArray(), PointRef = -1, HeadingRef = -1, Word4A = -1,
		Word4C = -1, Words4E = Enumerable.Repeat((short)5, 10).ToArray(), Word62 = 2, ActionRef64 = -1, ActionRef66 = -1,
		Word68 = 100,
	};

	private static Es1ScriptGroup Group(short kind, short[] members) {
		var refs = Enumerable.Repeat((short)-1, 8).ToArray();
		members.CopyTo(refs, 0);
		return new Es1ScriptGroup {
			MemberKind = kind, Word30 = 0, Word32 = 1, PointRef = 0, HeadingRef = -1, WaypointGroupRef = -1, MemberRefs = refs,
			Word4A = 1, Ref4ERow = -1, Ref4E = -1, ActionRef = -1, Word52 = -1, Word54 = -1, Word56 = -1,
		};
	}

	private static Es1PlayerMecEntry Entry(short chassis, short word1) => new() {
		Word1 = word1,
		Word2 = 0,
		Herc = new Es1HercRecord {
			Chassis = chassis,
			Weapons = [0, 6, 17],
			MountWords = [5, 5, 5],
			Word = 2,
			Status = Enumerable.Repeat((short)100, Es1HercRecord.StatusLength).ToArray(),
		},
	};
}
