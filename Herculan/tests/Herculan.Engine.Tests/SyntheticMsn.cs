using HercWorks.Core.Data.File.Msn;
using HercWorks.Core.Io.Transform.Common;

namespace Herculan.Engine.Tests;

/// <summary>
/// A revision-5 <c>.MSN</c> built row by row from records given as their <c>int16</c> words, and builders for the
/// records the tests use. A row given nothing is written with a count of 0.
/// </summary>
internal sealed class SyntheticMsn {
	private readonly Dictionary<int, List<short[]>> _rows = new();

	public SyntheticMsn Add(int row, short[] words) {
		if (!_rows.TryGetValue(row, out var records)) {
			_rows[row] = records = new();
		}

		records.Add(words);
		return this;
	}

	public byte[] Bytes() {
		using var stream = new MemoryStream();
		var writer = new BinaryWriter(stream);
		writer.Write(MissionGenerator.Revision);
		for (int row = 1; row <= 17; row++) {
			var records = _rows.GetValueOrDefault(row) ?? [];
			writer.Write((short)records.Count);
			foreach (short word in records.SelectMany(record => record)) {
				writer.Write(word);
			}
		}

		writer.Flush();
		return stream.ToArray();
	}

	/// <summary>
	/// The <c>script.dat</c> index that row <paramref name="row"/>'s GUID <paramref name="guid"/> reached in
	/// <paramref name="document"/>'s last generation, read from its provenance, or null when the generation dropped it: what a
	/// test of an edit finds the generated record by.
	/// </summary>
	public static int? Exported(MissionDocument document, int row, short guid) =>
		document.Provenance.Slots(row - 5).FirstOrDefault(slot => document.RecordAt(slot.Created)?.GUID == guid)?.Index;

	/// <summary>A row-1 condition.</summary>
	public static short[] Condition(short guid, short condition, short type, short lower, short upper, short operand) {
		var words = new short[7];
		words[MapObject.GUIDWord] = guid;
		words[MissionCondition14.ConditionRefWord] = condition;
		words[MissionCondition14.TypeWord] = type;
		words[MissionCondition14.FlagIndexOrRangeLowerWord] = lower;
		words[MissionCondition14.OperatorOrRangeUpperOrResultWord] = upper;
		words[MissionCondition14.ComparisonOperandWord] = operand;
		return words;
	}

	/// <summary>A row-4 text package with every line unset.</summary>
	public static short[] Text() => Enumerable.Repeat((short)-1, 72).ToArray();

	/// <summary>A row-6 point at (<paramref name="x"/>, <paramref name="y"/>, 0).</summary>
	public static short[] Point(short guid, int x, int y = 0, short condition = -1, short variant = -1) {
		var words = new short[11];
		words[MapObject.GUIDWord] = guid;
		words[MapPoint22.ConditionRefWord] = condition;
		words[MapPoint22.VariantKeyWord] = variant;
		words[MapPoint22.XWord] = (short)x;
		words[MapPoint22.XWord + 1] = (short)(x >> 16);
		words[MapPoint22.YWord] = (short)y;
		words[MapPoint22.YWord + 1] = (short)(y >> 16);
		return words;
	}

	/// <summary>A row-7 heading.</summary>
	public static short[] Heading(short guid, short degrees) {
		var words = new short[5];
		words[MapObject.GUIDWord] = guid;
		words[Heading10.ConditionRefWord] = -1;
		words[Heading10.VariantKeyWord] = -1;
		words[Heading10.DegreesWord] = degrees;
		return words;
	}

	/// <summary>A row-8 route through the row-6 GUIDs <paramref name="points"/>.</summary>
	public static short[] Route(short guid, params short[] points) =>
		[guid, -1, -1, -1, (short)points.Length, .. points];

	/// <summary>A row-9 circle trigger area about row-6 GUID <paramref name="point"/>.</summary>
	public static short[] Circle(short guid, short point) => [guid, -1, -1, 1, point, 100];

	/// <summary>A row-10 action of trigger type <paramref name="type"/> on target <paramref name="target"/>, testing no area and writing no counter.</summary>
	public static short[] Action(short guid, short type, short target = -1) {
		var words = Enumerable.Repeat((short)-1, 41).ToArray();
		words[MapObject.GUIDWord] = guid;
		words[MissionAction82.TypeWord] = type;
		words[MissionAction82.TypeWord + 1] = 0; // the verb
		words[MissionAction82.CounterPairsWord - 1] = 0; // the pair count
		words[MissionAction82.MessageIdWord] = 0;
		words[MissionAction82.TargetRefWord] = target;
		return words;
	}

	/// <summary>
	/// A row-12 herc of <paramref name="type"/>, its fit <paramref name="weapons"/> and <paramref name="ammo"/> from the first
	/// slot and the rest <c>-1</c> and the filler 5, every action and counter pair unset, pristine.
	/// </summary>
	public static short[] Mech(short guid, short type, short condition = -1, short variant = -1, short[]? weapons = null,
			short[]? ammo = null) {
		var words = Enumerable.Repeat((short)-1, 72).ToArray();
		words[MapObject.GUIDWord] = guid;
		words[MechRosterEntry144.ConditionRefWord] = condition;
		words[MechRosterEntry144.VariantKeyWord] = variant;
		words[MechRosterEntry144.AiRadarActiveWord] = 0;
		words[MechRosterEntry144.AiRadarActiveWord + 1] = 0;
		for (int word = MechRosterEntry144.AiRadarActiveWord + 2; word < MechRosterEntry144.TypeIndexWord; word++) {
			words[word] = 0;
		}

		words[MechRosterEntry144.TypeIndexWord] = type;
		words[MechRosterEntry144.OutOfActionReportWord - 1] = 0; // the pair count
		for (int slot = 0; slot < 10; slot++) {
			words[MechRosterEntry144.WeaponSecondaryWord + slot] = ammo != null && slot < ammo.Length ? ammo[slot] : (short)5;
			if (weapons != null && slot < weapons.Length) {
				words[MechRosterEntry144.WeaponRefsWord + slot] = weapons[slot];
			}
		}

		words[MechRosterEntry144.Constant2Word] = 2;
		words[MechRosterEntry144.StartingConditionWord] = 100;
		return words;
	}

	/// <summary>A row-13 flyer of type 0, its flag span clear and every ref and counter pair unset.</summary>
	public static short[] Flyer(short guid) {
		var words = Enumerable.Repeat((short)-1, 51).ToArray();
		words[MapObject.GUIDWord] = guid;
		Array.Fill(words, (short)0, FlyerRosterEntry102.FlagSpanWord, 20);
		words[0x34 / 2] = 0; // the type
		words[0x36 / 2] = 0; // the pair count
		words[FlyerRosterEntry102.UnkVal_100Word] = 100;
		return words;
	}

	/// <summary>A row-14 structure of type 0, every action and counter pair unset, pristine.</summary>
	public static short[] Base(short guid, short position = -1, short condition = -1, short variant = -1) {
		var words = Enumerable.Repeat((short)-1, 31).ToArray();
		words[MapObject.GUIDWord] = guid;
		words[BaseRosterEntry62.ConditionRefWord] = condition;
		words[BaseRosterEntry62.VariantKeyWord] = variant;
		words[BaseRosterEntry62.TypeIndexWord] = 0;
		words[BaseRosterEntry62.PositionRefWord] = position;
		words[0x0e / 2] = 0; // the pair count
		words[BaseRosterEntry62.StartingConditionWord] = 100;
		return words;
	}

	/// <summary>
	/// A row-16 group of roster <paramref name="kind"/> (2 structures) on human side, with the roster GUIDs
	/// <paramref name="members"/> in its first slots and everything else unset.
	/// </summary>
	public static short[] Group(short guid, short kind, short position = -1, short condition = -1, params short[] members) {
		var words = Enumerable.Repeat((short)-1, 82).ToArray();
		words[MapObject.GUIDWord] = guid;
		words[MissionGroup164.ConditionRefWord] = condition;
		for (int word = MissionGroup164.PaintsGroundWord; word < MissionGroup164.MemberKindWord; word++) {
			words[word] = 0;
		}

		words[MissionGroup164.MemberKindWord] = kind;
		words[MissionGroup164.MemberKindWord + 1] = 0;
		words[MissionGroup164.PositionRefWord] = position;
		members.CopyTo(words, MissionGroup164.MemberRefsWord);
		words[MissionGroup164.SideWord] = 0;
		words[0x78 / 2] = 0; // the pair count
		return words;
	}

	/// <summary>A row-15 order whose subject is the row-16 GUID <paramref name="group"/>.</summary>
	public static short[] GroupOrder(short guid, short group) {
		var words = Enumerable.Repeat((short)-1, 11).ToArray();
		words[MapObject.GUIDWord] = guid;
		words[MissionOrder22.VerbWord] = 0;
		words[MissionOrder22.SubjectKindWord] = 0;
		words[MissionOrder22.SubjectRefWord] = group;
		return words;
	}

	/// <summary>A row-17 objective on subject <paramref name="subject"/> of kind <paramref name="kind"/> (0 a group, 1 a mech).</summary>
	public static short[] Objective(short condition, short kind = 1, short subject = -1) {
		var words = Enumerable.Repeat((short)-1, 29).ToArray();
		words[MissionObjective58.ConditionRefWord] = condition;
		words[MissionObjective58.SubjectKindWord] = kind;
		words[MissionObjective58.SubjectRefWord] = subject;
		return words;
	}
}
