using System.Reflection;

namespace HercWorks.Core.Data.File.Msn;

/// <summary>
/// One edit of a <see cref="MissionDocument"/>'s <see cref="MissionFile"/>: each row it changed, as the row stood before
/// and after, every record copied. <see cref="MissionDocument.Undo"/> and <see cref="MissionDocument.Redo"/> put a row
/// back as one side left it. A history of these is undone last first, so the rows an undo restores are the ones its
/// edit left.
/// </summary>
public sealed class MsnEdit {
	private readonly SortedDictionary<int, (Array Before, Array After)> _rows;

	internal MsnEdit(SortedDictionary<int, (Array Before, Array After)> rows) => _rows = rows;

	/// <summary>The rows the edit changed.</summary>
	public IEnumerable<int> Rows => _rows.Keys;

	/// <summary>
	/// The edits <paramref name="edits"/> made one after another, as one: each row as the first edit to change it found
	/// it, and as the last left it.
	/// </summary>
	public static MsnEdit Combine(IEnumerable<MsnEdit> edits) {
		var rows = new SortedDictionary<int, (Array Before, Array After)>();
		foreach (var edit in edits) {
			foreach (var (row, sides) in edit._rows) {
				rows[row] = rows.TryGetValue(row, out var first) ? (first.Before, sides.After) : sides;
			}
		}

		return new MsnEdit(rows);
	}

	/// <summary>Writes each changed row of <paramref name="file"/> as the edit found it, or as it left it.</summary>
	internal void WriteTo(MissionFile file, bool after) {
		foreach (var (row, sides) in _rows) {
			MsnRows.Set(file, row, MsnRows.Copy(after ? sides.After : sides.Before));
		}
	}
}

/// <summary>
/// A field of a GUID-keyed <c>.MSN</c> record that an edit writes through <see cref="MissionDocument.SetField"/>: its
/// row, its word (the model classes' <c>…Word</c> constants), and its unset value, the one the generator's overlay of a
/// repeated GUID skips (docs/retail/formats/msn-mission-file.md#repeated-guids).
/// </summary>
public sealed class MsnField {
	private readonly Func<MapObject, short> _get;
	private readonly Action<MapObject, short> _set;

	private MsnField(int row, int word, short unset, Func<MapObject, short> get, Action<MapObject, short> set) {
		Row = row;
		Word = word;
		Unset = unset;
		_get = get;
		_set = set;
	}

	public int Row { get; }

	public int Word { get; }

	public short Unset { get; }

	/// <summary>A mech's own point, row 12's <see cref="MechRosterEntry144.PositionRef"/>.</summary>
	public static MsnField MechPosition { get; } = new(12, MechRosterEntry144.PositionRefWord, -1,
		record => ((MechRosterEntry144)record).PositionRef, (record, value) => ((MechRosterEntry144)record).PositionRef = value);

	/// <summary>A flyer's own point, row 13's <see cref="FlyerRosterEntry102.PositionRef"/>.</summary>
	public static MsnField FlyerPosition { get; } = new(13, FlyerRosterEntry102.PositionRefWord, -1,
		record => ((FlyerRosterEntry102)record).PositionRef, (record, value) => ((FlyerRosterEntry102)record).PositionRef = value);

	/// <summary>A structure's own point, row 14's <see cref="BaseRosterEntry62.PositionRef"/>.</summary>
	public static MsnField BasePosition { get; } = new(14, BaseRosterEntry62.PositionRefWord, -1,
		record => ((BaseRosterEntry62)record).PositionRef, (record, value) => ((BaseRosterEntry62)record).PositionRef = value);

	/// <summary>A mech's chassis, row 12's <see cref="MechRosterEntry144.TypeIndex"/>.</summary>
	public static MsnField MechType { get; } = new(12, MechRosterEntry144.TypeIndexWord, -1,
		record => ((MechRosterEntry144)record).TypeIndex, (record, value) => ((MechRosterEntry144)record).TypeIndex = value);

	/// <summary>Weapon slot <paramref name="slot"/> of a mech's fit, row 12's <see cref="MechRosterEntry144.WeaponRefs"/>.</summary>
	public static MsnField MechWeapon(int slot) => new(12, MechRosterEntry144.WeaponRefsWord + slot, -1,
		record => ((MechRosterEntry144)record).WeaponRefs[slot], (record, value) => ((MechRosterEntry144)record).WeaponRefs[slot] = value);

	/// <summary>
	/// The ammunition type of weapon slot <paramref name="slot"/>, row 12's <see cref="MechRosterEntry144.WeaponSecondary"/>,
	/// whose unset value is the filler 5.
	/// </summary>
	public static MsnField MechAmmo(int slot) => new(12, MechRosterEntry144.WeaponSecondaryWord + slot, 5,
		record => ((MechRosterEntry144)record).WeaponSecondary[slot],
		(record, value) => ((MechRosterEntry144)record).WeaponSecondary[slot] = value);

	/// <summary>A group's point, row 16's <see cref="MissionGroup164.PositionRef"/>.</summary>
	public static MsnField GroupPosition { get; } = new(16, MissionGroup164.PositionRefWord, -1,
		record => ((MissionGroup164)record).PositionRef, (record, value) => ((MissionGroup164)record).PositionRef = value);

	/// <summary>A group's side, row 16's <see cref="MissionGroup164.Side"/>.</summary>
	public static MsnField GroupSide { get; } = new(16, MissionGroup164.SideWord, -1,
		record => ((MissionGroup164)record).Side, (record, value) => ((MissionGroup164)record).Side = value);

	/// <summary>Member slot <paramref name="slot"/> of a group, row 16's <see cref="MissionGroup164.MemberRefs"/>.</summary>
	public static MsnField GroupMember(int slot) => new(16, MissionGroup164.MemberRefsWord + slot, -1,
		record => ((MissionGroup164)record).MemberRefs[slot], (record, value) => ((MissionGroup164)record).MemberRefs[slot] = value);

	/// <summary>The roster row's own-point field: <see cref="MechPosition"/>, <see cref="FlyerPosition"/> or <see cref="BasePosition"/>.</summary>
	public static MsnField RosterPosition(int row) => row switch {
		12 => MechPosition,
		13 => FlyerPosition,
		14 => BasePosition,
		_ => throw new ArgumentOutOfRangeException(nameof(row), row, "Rows 12 to 14 are the rosters."),
	};

	public short Get(MapObject record) => _get(record);

	public void Set(MapObject record, short value) => _set(record, value);
}

/// <summary>A <see cref="MissionFile"/>'s rows by number, and deep copies of them.</summary>
internal static class MsnRows {
	private static readonly MethodInfo ShallowCopy =
		typeof(object).GetMethod("MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic)!;

	/// <summary>Row <paramref name="row"/>'s records. Row 5 is kept raw (<see cref="MissionFile.DebriefBytes"/>) and is not one.</summary>
	public static Array Get(MissionFile file, int row) {
		Array? records = row switch {
			1 => file.Conditions,
			2 => file.SettingsPatches,
			3 => file.Variants,
			4 => file.Texts,
			6 => file.Points,
			7 => file.Headings,
			8 => file.WaypointGroups,
			9 => file.TriggerAreas,
			10 => file.Actions,
			11 => file.ActionTimers,
			12 => file.Mechs,
			13 => file.Flyers,
			14 => file.Bases,
			15 => file.Orders,
			16 => file.Groups,
			17 => file.Objectives,
			_ => throw new ArgumentOutOfRangeException(nameof(row), row, "Rows 1 to 4 and 6 to 17 hold records."),
		};
		return records ?? throw new InvalidOperationException($"The mission file has no row {row}.");
	}

	public static void Set(MissionFile file, int row, Array records) {
		switch (row) {
			case 1: file.Conditions = (MissionCondition14[])records; break;
			case 2: file.SettingsPatches = (MissionSettingsPatch[])records; break;
			case 3: file.Variants = (VariantValue8[])records; break;
			case 4: file.Texts = (MissionText144[])records; break;
			case 6: file.Points = (MapPoint22[])records; break;
			case 7: file.Headings = (Heading10[])records; break;
			case 8: file.WaypointGroups = (WaypointGroup[])records; break;
			case 9: file.TriggerAreas = (TriggerArea12[])records; break;
			case 10: file.Actions = (MissionAction82[])records; break;
			case 11: file.ActionTimers = (ActionTimer30[])records; break;
			case 12: file.Mechs = (MechRosterEntry144[])records; break;
			case 13: file.Flyers = (FlyerRosterEntry102[])records; break;
			case 14: file.Bases = (BaseRosterEntry62[])records; break;
			case 15: file.Orders = (MissionOrder22[])records; break;
			case 16: file.Groups = (MissionGroup164[])records; break;
			case 17: file.Objectives = (MissionObjective58?[])records; break;
			default: throw new ArgumentOutOfRangeException(nameof(row), row, "Rows 1 to 4 and 6 to 17 hold records.");
		}
	}

	/// <summary>A copy of <paramref name="records"/> sharing nothing with it: each record and each array it holds copied.</summary>
	public static Array Copy(Array records) {
		var copy = Array.CreateInstance(records.GetType().GetElementType()!, records.Length);
		for (int i = 0; i < records.Length; i++) {
			copy.SetValue(records.GetValue(i) is { } record ? CopyRecord(record) : null, i);
		}

		return copy;
	}

	/// <summary>
	/// A record's copy: its fields copied, then each <see cref="short"/> array and row 17's
	/// <see cref="CounterPair"/> array replaced by a copy, the only reference-typed values the record classes hold.
	/// </summary>
	public static object CopyRecord(object record) {
		object copy = ShallowCopy.Invoke(record, null)!;
		foreach (var property in record.GetType().GetProperties()) {
			if (!property.CanWrite) {
				continue;
			}

			if (property.PropertyType == typeof(short[]) && property.GetValue(record) is short[] words) {
				property.SetValue(copy, words.Clone());
			} else if (property.PropertyType == typeof(CounterPair[]) && property.GetValue(record) is CounterPair[] pairs) {
				property.SetValue(copy, Array.ConvertAll(pairs, pair => new CounterPair { CounterRef = pair.CounterRef, Op = pair.Op }));
			}
		}

		return copy;
	}
}
