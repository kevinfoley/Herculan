using HercWorks.Core.Data.File.Dat.Shell;
using HercWorks.Core.Data.File.Msn.Es1;
using HercWorks.Core.Io.Transform.Common;
using HercWorks.Core.Io.Transform.Shell;
using Xunit;

namespace Herculan.Engine.Tests;

/// <summary>
/// <see cref="Es1MissionLoad"/>, the shell's mode-0 load (docs/es1/formats/msn-mission-file.md#loading-a-mission), over a
/// constructed mission small enough to work out by hand: a draw, a random table, a repeated point after a point with no
/// GUID, a repeated mech, a squad of the player and one squadmate.
/// </summary>
public class Es1MissionLoadTests {
	private static readonly Es1MissionParams Params = new(Ammo: 1, Damage: 1, Difficulty: 2, Visibility: 1, TimeOfDay: 2);

	/// <summary>The draws the scripted roll answers, in the doc's order: the three flags, the condition's draw, the
	/// random table's roll, the theater, the squadmate's name group, name, rank 2, its three rank draws, and the player's word.</summary>
	private static readonly int[] Answers = [1, 2, 3, 4, 60, 2, 1, 2, 2, 3, 7, 11, 7];

	/// <summary>Catches a draw moved out of the doc's order, or a bound taken from the wrong field.</summary>
	[Fact]
	public void DrawsInTheDocsOrder() {
		var (_, bounds) = Load();

		// 12 × 3 the flags, 10 the type-1 condition, 100 the random table, 4 the theater, then the squadmate:
		// 4 and 3 its name, 6 its rank, 5 + 1 for rank 2's threshold, 5 × 2 and 10 × 2, and 11 the player's word.
		Assert.Equal([12, 12, 12, 10, 100, 4, 4, 3, 6, 6, 10, 20, 11], bounds);
	}

	/// <summary>
	/// Catches refs resolved counting the records with no GUID, a repeated point finding its slot by array index rather
	/// than by the GUID count, a repeated mech overlaying by that count, a template copied from the wrong option, and
	/// the header words the mode overrides.
	/// </summary>
	[Fact]
	public void ScriptDatHoldsTheResolvedSurvivors() {
		var (result, _) = Load();
		var script = result.Script;

		Assert.Equal([2, 1004, 0, 0, 0, 1, 1, 2, 1, 2], script.Header);

		// The repeat of GUID 5 replaces slot 0, the GUID count of GUID 5, which holds the point with no GUID.
		Assert.Equal([30, 10, 20], script.Points.Select(p => p.X));

		// The mechs with no GUID are not written; the repeat overlays the first mech's own record.
		Assert.Equal(2, script.Mechs.Length);
		Assert.Equal(9, script.Mechs[0].Words32[0]);
		Assert.Equal(2, script.Mechs[0].PointRef);
		Assert.Equal(0, script.Mechs[0].HeadingRef);

		// The random table's roll of 60 picks the 50–99 option, whose template is the second source.
		Assert.Equal(2, script.Mechs[1].Type);
		Assert.Equal([6, 9, 17], script.Mechs[1].Words32[..3]);

		Assert.Equal([0, 1, -1], script.Groups[0].MemberRefs[..3]);
		Assert.Equal(0, script.Groups[1].Ref4E);
		Assert.Equal([0, 99, 0, 2, 1, 2, 3], result.Flags[..7]);
	}

	/// <summary>
	/// Catches the player file's fields wrong against docs/es1/formats/career-log-and-player-mec.md#a-single-missions-file:
	/// the player's HERC from the hangar slot its type names, the squadmate's from its mech record and placed in the first
	/// empty slot, the mount words of a missile rack written 0, the status words, the name index and the available bytes.
	/// </summary>
	[Fact]
	public void PlayerMecHoldsTheSquad() {
		var (result, _) = Load();
		var player = result.Player;

		Assert.Equal(0, player.PlayerEntry);
		Assert.Equal(2, player.Entries.Length);

		var own = player.Entries[0];
		Assert.Equal((7, 0), ((int)own.Word1, (int)own.Word2));
		Assert.Equal(6, own.Herc!.Chassis);
		Assert.Equal([5, 5, 5, 0, 5, 5, 5, 5], own.Herc.MountWords);

		var mate = player.Entries[1];
		Assert.Equal((3 * (4 * 0 + 1) + 2, 1), ((int)mate.Word1, (int)mate.Word2));
		Assert.Equal(2, mate.Herc!.Chassis);
		Assert.Equal([6, 9, 17, -1], mate.Herc.Weapons);
		Assert.Equal([5, 0, 5, 5], mate.Herc.MountWords);
		Assert.All(player.Entries, entry => Assert.All(entry.Herc!.Status, word => Assert.Equal(100, word)));

		Assert.Equal(Enumerable.Range(0, Es1PlayerMec.WeaponCount).Select(w => w == 5 ? (byte)0 : (byte)1), player.WeaponsAvailable);

		// And the file reads back as written.
		var bytes = new Es1PlayerMecTransformer().Write(player)!;
		Assert.Equal(bytes, new Es1PlayerMecTransformer().Write(new Es1PlayerMecTransformer().Parse(bytes)!));
	}

	private static (Es1MissionLoadResult Result, List<int> Bounds) Load() {
		var bounds = new List<int>();
		var answers = new Queue<int>(Answers);

		// Math_RandomBelow: no draw for a bound of 0.
		int Roll(short bound) {
			if (bound == 0) {
				return 0;
			}

			bounds.Add(bound);
			return answers.Dequeue();
		}

		return (Es1MissionLoad.Load(Mission(), Params, Hangar(), Tech(), Roll), bounds);
	}

	private static Es1MissionFile Mission() => new() {
		Revision = 5,
		Conditions = [
			new() { GUID = 1, ConditionRef = -1, Type = 1, FlagIndexOrRangeLower = 10 },
			new() { GUID = 2, ConditionRef = -1, Type = 3, FlagIndexOrRangeLower = 7, ComparisonOperand = 100 },
			new() { GUID = 3, ConditionRef = 2, Type = 2, FlagIndexOrRangeLower = 0, OperatorOrRangeUpperOrResult = 49 },
			new() { GUID = 4, ConditionRef = 2, Type = 2, FlagIndexOrRangeLower = 50, OperatorOrRangeUpperOrResult = 99 },
		],
		HeaderPatches = [new() { ConditionRef = -1, HeaderWords = [1, 1004, 0, 0, 0, 0, 0, 0, 0, 0] }],
		Points = [Point(-1, 1), Point(5, 10), Point(6, 20), Point(5, 30)],
		Headings = [new() { GUID = 8, ConditionRef = -1, RandomTableKey = -1, Value = 90 }],
		Mechs = [
			Mech(10, type: 6, key: -1, weapons: [2, 2, 8], point: 6, heading: 8),
			Mech(-1, type: 1, key: -1, weapons: [0, 0, 17], condition: 3),
			Mech(-1, type: 2, key: -1, weapons: [6, 9, 17], condition: 4),
			Mech(11, type: -1, key: 7, weapons: []),
			Mech(10, type: -1, key: -1, weapons: [9]),
		],
		Groups = [
			Group(20, members: [10, 11]),
			Group(21, members: [11], targetGroup: 20),
		],
	};

	private static Es1Point Point(short guid, int x) => new() { GUID = guid, ConditionRef = -1, RandomTableKey = -1, X = x };

	/// <summary>A mech whose unset fields hold their unset values, so a repeat overlays only what it sets.</summary>
	private static Es1Mech Mech(short guid, short type, short key, short[] weapons, short point = -1, short heading = -1,
			short condition = -1) {
		var words32 = Enumerable.Repeat((short)-1, 10).ToArray();
		weapons.CopyTo(words32, 0);
		return new Es1Mech {
			GUID = guid, ConditionRef = condition, RandomTableKey = key, Type = type, Words32 = words32, PointRef = point,
			HeadingRef = heading, Word4A = -1, Word4C = -1, Words4E = Enumerable.Repeat((short)5, 10).ToArray(), Word62 = 2,
			ActionRef64 = -1, ActionRef66 = -1, Word68 = 100,
		};
	}

	private static Es1Group Group(short guid, short[] members, short targetGroup = -1) {
		var refs = Enumerable.Repeat((short)-1, 8).ToArray();
		members.CopyTo(refs, 0);
		return new Es1Group {
			GUID = guid, ConditionRef = -1, MemberKind = 0, Word30 = 0, Word32 = -1, PointRef = -1, HeadingRef = -1,
			WaypointGroupRef = -1, MemberRefs = refs, Word4A = 0, Ref4ERow = (short)(targetGroup >= 0 ? 0 : -1), Ref4E = targetGroup,
			ActionRef = -1, Word52 = -1, Word54 = -1, Word56 = -1,
		};
	}

	/// <summary>An eleven-slot hangar holding one HERC of chassis 6 in slot 6, its fourth mount a missile rack.</summary>
	private static Es1HercList Hangar() => new() {
		SlotCount = 11,
		Hercs = [new Es1HercListEntry {
			Slot = 6,
			Herc = new Es1HercRecord {
				Chassis = 6,
				Weapons = [6, 6, 6, 10, 6, 0, 0, 0],
				MountWords = [5, 5, 5, 5, 5, 5, 5, 5],
				Word = 2,
				Status = Enumerable.Repeat((short)100, Es1HercRecord.StatusLength).ToArray(),
			},
		}],
	};

	/// <summary>Weapon 5 never available; every other entry left at the static record's 0, 0.</summary>
	private static Es1TechFile Tech() => new() {
		Records = [new Es1TechRecord { Kind = 1, Index = 5, GoodGradeMissions = -1, AnyGradeMissions = -1 }],
	};
}
