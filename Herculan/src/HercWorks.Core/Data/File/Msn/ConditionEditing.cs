using HercWorks.Core.Io.Transform.Common;

namespace HercWorks.Core.Data.File.Msn;

/// <summary>
/// The records whose condition field references a row-1 GUID, whatever their own condition: <see cref="Records"/> by row and
/// index, row 1's own <c>0x02</c> (a requirement, or a range's draw) included, row 5's by its 64-byte record's index; and
/// <see cref="Text"/>, the <c>.ENG</c> records by their index in the file.
/// </summary>
public sealed record ConditionNamers(IReadOnlyList<MsnRecordId> Records, IReadOnlyList<int> Text) {
	/// <summary>Whether any record references it.</summary>
	public bool Any => Records.Count > 0 || Text.Count > 0;
}

/// <summary>
/// Creates, edits and deletes a <see cref="MissionDocument"/>'s conditions, row 1, as record edits: flag tests (type 0),
/// draws (type 1) and ranges over a draw (type 2). How the load reads row 1 is
/// docs/retail/formats/msn-mission-file.md#the-conditions--row-1, and what changing a draw does to every later one
/// docs/herculan/handoff-editor-document.md#conditions-and-variants.
///
/// <list type="bullet">
/// <item>Row 1 is read in order, each record tested against the survivors before it, so a condition can require, and a
/// range be over, only a record before it: a requirement naming a later record never passes. Every choice is refused
/// unless it names an earlier record (<see cref="WhyNoRequirement"/>), and a new record is appended last, after every
/// record it can require.</item>
/// <item>A row-1 record merges with nothing: a GUID carried twice is two records, and a ref to it passes on either
/// survivor. No retail mission carries a row-1 GUID twice, and every one numbers its records by index
/// (<c>HercWorks.Query records --row 1</c> over the 62 missions), so the editor addresses a condition by its GUID and
/// edits none whose GUID is carried twice (<see cref="WhyFixed"/>). A new record takes the row's largest GUID plus one
/// (<see cref="MissionDocument.Append"/>).</item>
/// <item>A field is written in the record itself (<see cref="MissionDocument.Rewrite{T}"/>).</item>
/// <item>A variant table (type 3) and a range over one, which the other rows' variant keys draw by key rather than
/// reference by GUID, are edited through <see cref="VariantEditing"/>, and not here (<see cref="WhyFixed"/>).</item>
/// <item>A condition is deleted only while nothing references it (<see cref="Namers"/>): a record whose condition names a
/// GUID with no surviving record is dropped in every state. Deleting a draw, or one a draw requires, changes the draws
/// every later draw and variant pick gets.</item>
/// </list>
/// </summary>
public static class ConditionEditing {
	private const int Row = 1;

	/// <summary>Row 5's records, kept raw (<see cref="MissionFile.DebriefBytes"/>): 64 bytes, the condition word first.</summary>
	private const int DebriefRecordLength = 64;

	/// <summary>A row-1 record's type.</summary>
	public const short FlagTest = 0;
	public const short Draw = 1;
	public const short Range = 2;
	public const short VariantTable = 3;

	/// <summary>The type-0 operators, <c>0x119</c>-<c>0x11e</c>, in code order.</summary>
	public static IReadOnlyList<short> Operators { get; } = [
		MissionGenerator.OpEqual, MissionGenerator.OpNotEqual, MissionGenerator.OpLess, MissionGenerator.OpLessOrEqual,
		MissionGenerator.OpGreater, MissionGenerator.OpGreaterOrEqual,
	];

	/// <summary>How many campaign flags a test can read, 0 to 999 (<see cref="MissionGenerator.CampaignFlagCount"/>).</summary>
	public const int Flags = MissionGenerator.CampaignFlagCount;

	/// <summary>
	/// A new flag test's flag, operator and value: flag 8, the first of flags 8, 9 and 10, which tie at 96 of the 1016 retail
	/// flag tests (the editor's choice of the three); <c>&gt;</c>, 707 of them; and 0, 654 of them
	/// (<c>HercWorks.Query values --row 1 --field FlagIndexOrRangeLower --where Type=0</c>, <c>--field
	/// OperatorOrRangeUpperOrResult</c>, <c>--field ComparisonOperand</c>). Unconditioned, as 966 of them are.
	/// </summary>
	public const short NewFlag = 8;
	public const short NewOperator = MissionGenerator.OpGreater;
	public const short NewValue = 0;

	/// <summary>
	/// A new draw's bound: 11, what all 32 retail draws carry (<c>--field FlagIndexOrRangeLower --where Type=1</c>). Each of the
	/// 32 requires a flag test of flag 10, one particular to its mission, so a new draw requires nothing: the editor's choice.
	/// </summary>
	public const short NewBound = 11;

	/// <summary>
	/// A new range's ends: 0 to 9, 32 of the 64 retail ranges over a draw, which tie with 10 to 10 (<c>HercWorks.Query
	/// conditions</c>); the editor takes the first.
	/// </summary>
	public const short NewLow = 0;
	public const short NewHigh = 9;

	/// <summary>The largest bound, and end of a range, the editor writes: a range compares unsigned, and a bound past it reads as negative.</summary>
	public const short MaxBound = short.MaxValue;

	// ---- Reading -----------------------------------------------------------------------------------

	/// <summary>Row 1's records, empty for a file with none.</summary>
	public static MissionCondition14[] Records(MissionDocument document) => document.Mission.Conditions ?? [];

	/// <summary>The index in row 1 of GUID <paramref name="guid"/>'s first record, or null when no record carries it.</summary>
	public static int? IndexOf(MissionDocument document, short guid) =>
		guid != -1 && Array.FindIndex(Records(document), record => record.GUID == guid) is >= 0 and var index ? index : null;

	/// <summary>GUID <paramref name="guid"/>'s first row-1 record, throwing for one no record carries.</summary>
	public static MissionCondition14 Record(MissionDocument document, short guid) =>
		IndexOf(document, guid) is { } index
			? Records(document)[index]
			: throw new InvalidOperationException($"Condition {guid} is not in the file.");

	/// <summary>
	/// Row 1 as the editor's state loaded it: record <paramref name="guid"/>'s outcome, whether it holds and the draw it made
	/// (<see cref="MissionGenerator.Conditions"/>).
	/// </summary>
	public static ConditionOutcome Outcome(MissionDocument document, short guid) =>
		IndexOf(document, guid) is { } index && index < document.Generator.Conditions.Count
			? document.Generator.Conditions[index]
			: new ConditionOutcome(false, null);

	/// <summary>
	/// Whether what <paramref name="guid"/> requires holds in the editor's state: it requires nothing, or a record before it
	/// carrying the GUID it requires survived. For a range, whether its draw was made.
	/// </summary>
	public static bool RequirementHolds(MissionDocument document, short guid) {
		var record = Record(document, guid);
		if (record.ConditionRef == -1) {
			return true;
		}

		int index = IndexOf(document, guid)!.Value;
		var records = Records(document);
		var outcomes = document.Generator.Conditions;
		for (int i = 0; i < index && i < outcomes.Count; i++) {
			if (records[i].GUID == record.ConditionRef && outcomes[i].Survived) {
				return true;
			}
		}

		return false;
	}

	/// <summary>
	/// The record a range is over: the first record before it carrying its <c>0x02</c> GUID, whose draw the load's range test
	/// reads when it is a draw (<see cref="MissionGenerator"/>'s <c>ParentValue</c>); null for none.
	/// </summary>
	public static MissionCondition14? ParentOf(MissionDocument document, short guid) {
		var record = Record(document, guid);
		int index = IndexOf(document, guid)!.Value;
		var records = Records(document);
		for (int i = 0; i < index; i++) {
			if (records[i].GUID == record.ConditionRef) {
				return records[i];
			}
		}

		return null;
	}

	/// <summary>The GUIDs of the records before <paramref name="guid"/>'s, in file order: what it can require.</summary>
	public static IReadOnlyList<short> Earlier(MissionDocument document, short guid) =>
		Records(document).Take(IndexOf(document, guid) ?? 0).Select(record => record.GUID).Where(other => other != -1)
			.Distinct().ToList();

	/// <summary>Every draw (type 1) of the mission, by GUID in file order: what a new range can be over.</summary>
	public static IReadOnlyList<short> Draws(MissionDocument document) =>
		Records(document).Where(record => record.Type == Draw && record.GUID != -1).Select(record => record.GUID).Distinct().ToList();

	// ---- Describing --------------------------------------------------------------------------------

	/// <summary>
	/// The operator <paramref name="op"/> as the test reads, flag on the left: <c>&gt;</c> holds when the flag is greater than
	/// the value, as the load's switch on the code compares (its <c>&gt;</c> and <c>&gt;=</c> take the operands the other way round,
	/// value on the left, docs/retail/formats/msn-mission-file.md#the-conditions--row-1). Null for a code that
	/// always fails.
	/// </summary>
	public static string? OperatorText(short op) => op switch {
		MissionGenerator.OpEqual => "=",
		MissionGenerator.OpNotEqual => "!=",
		MissionGenerator.OpLess => "<",
		MissionGenerator.OpLessOrEqual => "<=",
		MissionGenerator.OpGreater => ">",
		MissionGenerator.OpGreaterOrEqual => ">=",
		_ => null,
	};

	/// <summary>
	/// What row-1 GUID <paramref name="guid"/> tests, in the editor's words: <c>flag 100 &gt; 0</c>, <c>draw below 11</c>,
	/// <c>draw 0 to 9 of condition 7</c>, <c>variant table 3 below 4</c>, with <c>if condition 5</c> after one that requires
	/// another; null for a GUID no record carries.
	/// </summary>
	public static string? Describe(MissionFile mission, short guid) =>
		(mission.Conditions ?? []).FirstOrDefault(record => record.GUID == guid) is { } record ? Describe(mission, record) : null;

	/// <inheritdoc cref="Describe(MissionFile, short)"/>
	public static string Describe(MissionFile mission, MissionCondition14 record) {
		string own = record.Type switch {
			FlagTest => OperatorText(record.OperatorOrRangeUpperOrResult) is { } op
				? $"flag {record.FlagIndexOrRangeLower} {op} {record.ComparisonOperand}"
				: $"flag {record.FlagIndexOrRangeLower}, operator {record.OperatorOrRangeUpperOrResult} (never holds)",
			Draw => $"draw below {record.FlagIndexOrRangeLower}",
			Range => DescribeRange(mission, record),
			VariantTable => $"variant table {record.FlagIndexOrRangeLower} below {record.ComparisonOperand}",
			_ => $"type {record.Type} (never holds)",
		};

		return record.Type == Range || record.ConditionRef == -1 ? own : $"{own} if condition {record.ConditionRef}";
	}

	// A range is over the first earlier record of its 0x02 GUID, when that is a draw or a variant table; it never holds otherwise.
	private static string DescribeRange(MissionFile mission, MissionCondition14 record) {
		var records = mission.Conditions ?? [];
		int index = Array.IndexOf(records, record);
		var parent = records.Take(index < 0 ? records.Length : index).FirstOrDefault(other => other.GUID == record.ConditionRef);
		string ends = $"{(ushort)record.FlagIndexOrRangeLower} to {(ushort)record.OperatorOrRangeUpperOrResult}";
		return parent?.Type switch {
			Draw => $"draw {ends} of condition {record.ConditionRef}",
			VariantTable => $"variant {ends} of condition {record.ConditionRef}",
			_ => $"{ends} of condition {record.ConditionRef} (never holds)",
		};
	}

	// ---- Why not -----------------------------------------------------------------------------------

	/// <summary>Why condition <paramref name="guid"/> cannot be edited or deleted, or null when it can.</summary>
	public static string? WhyFixed(MissionDocument document, short guid) {
		if (IndexOf(document, guid) == null) {
			return $"Condition {guid} is not in the file.";
		}

		var record = Record(document, guid);
		return Records(document).Count(other => other.GUID == guid) > 1 ? $"More than one record carries condition {guid}."
			: InVariantTable(document, guid) ? "A variant table and its ranges are edited in the table's view."
			: record.Type is < FlagTest or > VariantTable ? $"Its type, {record.Type}, is none the load reads."
			: null;
	}

	/// <summary>Whether <paramref name="guid"/> is a variant table (type 3) or a range over one, which <see cref="VariantEditing"/> edits.</summary>
	public static bool InVariantTable(MissionDocument document, short guid) =>
		IndexOf(document, guid) != null && Record(document, guid) is var record
			&& (record.Type == VariantTable || (record.Type == Range && ParentOf(document, guid)?.Type == VariantTable));

	/// <summary>
	/// Why <paramref name="guid"/> cannot require <paramref name="required"/>, a row-1 GUID or <c>-1</c> for nothing, or null
	/// when it can. For a range, <paramref name="required"/> is the draw it is over, which it must have.
	/// </summary>
	public static string? WhyNoRequirement(MissionDocument document, short guid, short required) {
		if (WhyFixed(document, guid) is { } fixedReason) {
			return fixedReason;
		}

		bool range = Record(document, guid).Type == Range;
		if (required == -1) {
			return range ? "A range is over a draw." : null;
		}

		if (required == guid) {
			return "A condition cannot require itself.";
		}

		if (IndexOf(document, required) is not { } at) {
			return $"Condition {required} is not in the file.";
		}

		if (at > IndexOf(document, guid)!.Value) {
			return $"Condition {required} comes after it: the conditions are read in order, so one can require only a condition before it.";
		}

		return range && Records(document)[at].Type != Draw ? $"Condition {required} is not a draw." : null;
	}

	/// <summary>Why condition <paramref name="guid"/> cannot be deleted, or null when it can.</summary>
	public static string? WhyNoDelete(MissionDocument document, short guid) =>
		WhyFixed(document, guid) ?? (Namers(document, guid).Any ? "Records reference it." : null);

	// ---- Creating ----------------------------------------------------------------------------------

	/// <summary>Appends a new flag test, <see cref="NewFlag"/> <see cref="NewOperator"/> <see cref="NewValue"/>, requiring nothing, answering its GUID.</summary>
	public static MsnEdit CreateFlagTest(MissionDocument document, out short guid) => Create(document, AppendFlagTest, out guid);

	/// <summary>
	/// Appends a new draw below <see cref="NewBound"/>, requiring nothing, answering its GUID; its <c>0x08</c> and <c>0x0A</c> 0,
	/// as all 32 retail draws.
	/// </summary>
	public static MsnEdit CreateDraw(MissionDocument document, out short guid) => Create(document, AppendDraw, out guid);

	/// <summary>
	/// Appends a new range, <see cref="NewLow"/> to <see cref="NewHigh"/>, over draw <paramref name="draw"/>, answering its GUID;
	/// its <c>0x0A</c> 0, as all 847 retail ranges (<c>--field ComparisonOperand --where Type=2</c>).
	/// </summary>
	public static MsnEdit CreateRange(MissionDocument document, short draw, out short guid) {
		if (IndexOf(document, draw) is not { } at || Records(document)[at].Type != Draw) {
			throw new InvalidOperationException($"Condition {draw} is not a draw.");
		}

		return Create(document, (MissionDocument inside, out short created) => AppendRange(inside, draw, out created), out guid);
	}

	/// <summary>The record edit of <see cref="CreateFlagTest"/>, made inside the caller's <see cref="MissionDocument.Change"/>.</summary>
	private static void AppendFlagTest(MissionDocument document, out short guid) =>
		Append(document, new MissionCondition14 {
			ConditionRef = -1, Type = FlagTest, FlagIndexOrRangeLower = NewFlag, OperatorOrRangeUpperOrResult = NewOperator,
			ComparisonOperand = NewValue,
		}, out guid);

	/// <summary>The record edit of <see cref="CreateDraw"/>, made inside the caller's <see cref="MissionDocument.Change"/>.</summary>
	private static void AppendDraw(MissionDocument document, out short guid) =>
		Append(document, new MissionCondition14 { ConditionRef = -1, Type = Draw, FlagIndexOrRangeLower = NewBound }, out guid);

	/// <summary>The record edit of <see cref="CreateRange"/>, made inside the caller's <see cref="MissionDocument.Change"/>.</summary>
	private static void AppendRange(MissionDocument document, short draw, out short guid) =>
		Append(document, new MissionCondition14 {
			ConditionRef = draw, Type = Range, FlagIndexOrRangeLower = NewLow, OperatorOrRangeUpperOrResult = NewHigh,
		}, out guid);

	internal delegate void Appending(MissionDocument document, out short guid);

	private static MsnEdit Create(MissionDocument document, Appending append, out short guid) {
		short created = -1;
		var change = document.Change(() => append(document, out created));
		guid = created;
		return change;
	}

	// Appends <record> last in row 1, with a new GUID and 0x0C 0, as every retail record has it.
	private static void Append(MissionDocument document, MissionCondition14 record, out short guid) {
		record.LatestDraw = 0;
		document.Append(Row, record, out guid);
	}

	// ---- Editing -----------------------------------------------------------------------------------

	/// <summary>Sets flag test <paramref name="guid"/>'s flag, 0 to <see cref="Flags"/> - 1.</summary>
	public static MsnEdit SetFlag(MissionDocument document, short guid, short flag) {
		if (flag is < 0 or >= Flags) {
			throw new ArgumentOutOfRangeException(nameof(flag), flag, $"A flag is 0 to {Flags - 1}.");
		}

		return Write(document, guid, FlagTest, record => record.FlagIndexOrRangeLower = flag);
	}

	/// <summary>Sets flag test <paramref name="guid"/>'s operator, one of <see cref="Operators"/>.</summary>
	public static MsnEdit SetOperator(MissionDocument document, short guid, short op) {
		if (!Operators.Contains(op)) {
			throw new ArgumentOutOfRangeException(nameof(op), op, "An operator is 0x119 to 0x11e.");
		}

		return Write(document, guid, FlagTest, record => record.OperatorOrRangeUpperOrResult = op);
	}

	/// <summary>Sets the value flag test <paramref name="guid"/> compares its flag with.</summary>
	public static MsnEdit SetValue(MissionDocument document, short guid, short value) =>
		Write(document, guid, FlagTest, record => record.ComparisonOperand = value);

	/// <summary>Sets draw <paramref name="guid"/>'s bound, 1 to <see cref="MaxBound"/>: it draws 0 to one less.</summary>
	public static MsnEdit SetBound(MissionDocument document, short guid, short bound) {
		if (bound is < 1 or > MaxBound) {
			throw new ArgumentOutOfRangeException(nameof(bound), bound, $"A bound is 1 to {MaxBound}.");
		}

		return Write(document, guid, Draw, record => record.FlagIndexOrRangeLower = bound);
	}

	/// <summary>Sets range <paramref name="guid"/>'s low and high ends, each 0 to <see cref="MaxBound"/>.</summary>
	public static MsnEdit SetEnds(MissionDocument document, short guid, short low, short high) {
		if (low is < 0 or > MaxBound || high is < 0 or > MaxBound) {
			throw new ArgumentOutOfRangeException(nameof(low), $"A range's ends are 0 to {MaxBound}.");
		}

		return Write(document, guid, Range, record => {
			record.FlagIndexOrRangeLower = low;
			record.OperatorOrRangeUpperOrResult = high;
		});
	}

	/// <summary>
	/// Sets what <paramref name="guid"/> requires, a row-1 GUID before it or <c>-1</c> for nothing; for a range, the draw before
	/// it that it is over (<see cref="WhyNoRequirement"/>).
	/// </summary>
	public static MsnEdit SetRequirement(MissionDocument document, short guid, short required) {
		if (WhyNoRequirement(document, guid, required) is { } reason) {
			throw new InvalidOperationException(reason);
		}

		return Write(document, guid, null, record => record.ConditionRef = required);
	}

	/// <summary>Deletes condition <paramref name="guid"/>'s record; refused while <see cref="Namers"/> finds anything.</summary>
	public static MsnEdit Delete(MissionDocument document, short guid) {
		if (WhyNoDelete(document, guid) is { } reason) {
			throw new InvalidOperationException(reason);
		}

		return document.Change(() => document.Remove(Row, guid));
	}

	// Rewrites <guid>'s record through <write>, refusing one fixed or of another type than <type>.
	private static MsnEdit Write(MissionDocument document, short guid, short? type, Action<MissionCondition14> write) {
		if (WhyFixed(document, guid) is { } reason) {
			throw new InvalidOperationException(reason);
		}

		if (type is { } expected && Record(document, guid).Type != expected) {
			throw new InvalidOperationException($"Condition {guid} is not of type {expected}.");
		}

		int index = IndexOf(document, guid)!.Value;
		return document.Change(() => document.Rewrite(Row, index, write));
	}

	// ---- What references a condition -----------------------------------------------------------------

	/// <summary>
	/// Every record whose condition field references row-1 GUID <paramref name="guid"/>, whatever its own condition: row 1's
	/// <c>0x02</c> (what a condition requires, and a range's draw), row 2's and row 4's <c>0x00</c>, row 5's 64-byte records'
	/// word 0, as <see cref="MissionGenerator.LoadDebriefText"/> reads them, the <c>0x02</c> of rows 3 and 6 to 16, row 17's
	/// <c>0x00</c>, and each <c>.ENG</c> record's condition. These are every condition the load gates a record on
	/// (<see cref="MissionGenerator"/>'s <c>Gate</c> calls).
	/// </summary>
	public static ConditionNamers Namers(MissionDocument document, short guid) {
		var mission = document.Mission;
		var records = new List<MsnRecordId>();
		if (guid == -1) {
			return new ConditionNamers(records, []);
		}

		void Add<T>(int row, T[]? rowRecords, Func<T, short> condition) {
			for (int i = 0; i < (rowRecords ?? []).Length; i++) {
				if (rowRecords![i] is { } record && condition(record) == guid) {
					records.Add(new MsnRecordId(row, i));
				}
			}
		}

		Add(1, mission.Conditions, record => record.ConditionRef);
		Add(2, mission.SettingsPatches, record => record.Data[0]);
		Add(3, mission.Variants, record => record.ConditionRef);
		Add(4, mission.Texts, record => record.ConditionRef);
		if (mission.DebriefBytes is { } debrief) {
			for (int i = 0; (i + 1) * DebriefRecordLength <= debrief.Length; i++) {
				if (BitConverter.ToInt16(debrief, i * DebriefRecordLength) == guid) {
					records.Add(new MsnRecordId(5, i));
				}
			}
		}

		Add(6, mission.Points, record => record.ConditionRef);
		Add(7, mission.Headings, record => record.ConditionRef);
		Add(8, mission.WaypointGroups, record => record.ConditionRef);
		Add(9, mission.TriggerAreas, record => record.ConditionRef);
		Add(10, mission.Actions, record => record.ConditionRef);
		Add(11, mission.ActionTimers, record => record.ConditionRef);
		Add(12, mission.Mechs, record => record.ConditionRef);
		Add(13, mission.Flyers, record => record.ConditionRef);
		Add(14, mission.Bases, record => record.ConditionRef);
		Add(15, mission.Orders, record => record.ConditionRef);
		Add(16, mission.Groups, record => record.ConditionRef);
		Add(17, mission.Objectives, record => record!.ConditionRef);

		var text = new List<int>();
		var strings = document.Text?.Strings ?? [];
		for (int i = 0; i < strings.Length; i++) {
			if (strings[i].ConditionRef == guid) {
				text.Add(i);
			}
		}

		return new ConditionNamers(records, text);
	}
}
