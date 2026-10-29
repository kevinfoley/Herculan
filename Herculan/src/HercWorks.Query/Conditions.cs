using HercWorks.Core.Data.File.Msn;
using HercWorks.Core.Data.File.Msn.Es1;

namespace HercWorks.Query;

/// <summary>
/// Reads a mission's row-1 conditions as text — <c>flag 625 &gt; 0</c> — following the survival rules
/// of docs/retail/formats/msn-mission-file.md#the-conditions--row-1. This is a reading of the file, not the
/// load: nothing is evaluated against a flag array or drawn.
/// </summary>
internal static class Conditions {
	/// <summary>The type-0 comparison codes, <c>0x119</c>-<c>0x11e</c>, in order.</summary>
	private static readonly string[] Operators = ["==", "!=", "<", "<=", ">", ">="];
	private const short FirstOperator = 0x119;

	/// <summary>ES1's four type-0 comparison codes, <c>0x114</c>-<c>0x117</c>, in order (docs/es1/formats/msn-mission-file.md#conditions).</summary>
	private static readonly string[] Es1Operators = ["==", "!=", "<", ">"];
	private const short Es1FirstOperator = 0x114;

	/// <summary>A condition chain deeper than this is cut short; retail's are at most a few links.</summary>
	private const int MaxDepth = 8;

	/// <summary>One row-1 record's words, from either game's model, whose records are field for field the same.</summary>
	private readonly record struct Row1(short Guid, short ConditionRef, short Type, short FlagIndexOrRangeLower,
		short OperatorOrRangeUpperOrResult, short ComparisonOperand);

	/// <summary>A mission's row 1 and the operator codes its game's type-0 records use.</summary>
	private sealed record Table(IReadOnlyList<Row1> Records, Func<short, string?> Operator);

	private static Table Of(MissionFile file) => new((file.Conditions ?? [])
		.Select(c => new Row1(c.GUID, c.ConditionRef, c.Type, c.FlagIndexOrRangeLower, c.OperatorOrRangeUpperOrResult, c.ComparisonOperand))
		.ToList(), Operator);

	private static Table Of(Es1MissionFile file) => new(file.Conditions
		.Select(Row)
		.ToList(), Es1Operator);

	private static Row1 Row(Es1Condition c) =>
		new(c.GUID, c.ConditionRef, c.Type, c.FlagIndexOrRangeLower, c.OperatorOrRangeUpperOrResult, c.ComparisonOperand);

	/// <summary>The operator a type-0 code stands for, or null for a code the load fails.</summary>
	public static string? Operator(short code) =>
		code >= FirstOperator && code < FirstOperator + Operators.Length ? Operators[code - FirstOperator] : null;

	/// <summary>The operator an ES1 type-0 code stands for, or null for a code the load fails.</summary>
	public static string? Es1Operator(short code) =>
		code >= Es1FirstOperator && code < Es1FirstOperator + Es1Operators.Length ? Es1Operators[code - Es1FirstOperator] : null;

	/// <summary>
	/// What condition ref <paramref name="guid"/> asks, or null for <c>-1</c>. Several row-1 records may
	/// share a GUID; the gate passes on any survivor that has it, so they are joined with <c>or</c>.
	/// </summary>
	public static string? Describe(MissionFile file, short guid) => guid == -1 ? null : Describe(Of(file), guid, 0);

	/// <inheritdoc cref="Describe(MissionFile, short)"/>
	public static string? Describe(Es1MissionFile file, short guid) => guid == -1 ? null : Describe(Of(file), guid, 0);

	private static string Describe(Table table, short guid, int depth) {
		var records = table.Records.Where(c => c.Guid == guid).ToList();
		if (records.Count == 0) {
			return $"cond {guid} (no row-1 record)";
		}

		if (depth >= MaxDepth) {
			return $"cond {guid}";
		}

		var parts = records.Select(r => DescribeOne(table, r, depth)).ToList();
		return parts.Count == 1 ? parts[0] : "(" + string.Join(") or (", parts) + ")";
	}

	/// <summary>What row-1 record <paramref name="c"/> itself asks, with the chain above it.</summary>
	public static string DescribeRecord(MissionFile file, MissionCondition14 c) =>
		DescribeOne(Of(file), new Row1(c.GUID, c.ConditionRef, c.Type, c.FlagIndexOrRangeLower, c.OperatorOrRangeUpperOrResult, c.ComparisonOperand), 0);

	/// <inheritdoc cref="DescribeRecord(MissionFile, MissionCondition14)"/>
	public static string DescribeRecord(Es1MissionFile file, Es1Condition c) => DescribeOne(Of(file), Row(c), 0);

	private static string DescribeOne(Table table, Row1 c, int depth) {
		string own = c.Type switch {
			0 => table.Operator(c.OperatorOrRangeUpperOrResult) is { } op
				? $"flag {c.FlagIndexOrRangeLower} {op} {c.ComparisonOperand}"
				: $"flag {c.FlagIndexOrRangeLower} op 0x{(ushort)c.OperatorOrRangeUpperOrResult:x} (fails)",
			1 => $"draw {c.Guid} below {c.FlagIndexOrRangeLower}",
			2 => DescribeRange(table, c),
			3 => $"random table key {c.FlagIndexOrRangeLower}, rolled below {c.ComparisonOperand}",
			_ => $"type {c.Type} (never survives)",
		};

		// Type 2's condition field is its parent, which DescribeRange has already named.
		if (c.Type == 2 || c.ConditionRef == -1) {
			return own;
		}

		return $"{Describe(table, c.ConditionRef, depth + 1)} and {own}";
	}

	private static string DescribeRange(Table table, Row1 c) {
		string range = $"{(ushort)c.FlagIndexOrRangeLower}-{(ushort)c.OperatorOrRangeUpperOrResult}";
		var parent = table.Records.Where(p => p.Guid == c.ConditionRef && p.Type is 1 or 3).Cast<Row1?>().FirstOrDefault();
		return parent?.Type switch {
			1 => $"draw {parent.Value.Guid} in {range}",
			3 => $"option {range} of key {parent.Value.FlagIndexOrRangeLower}",
			_ => $"draw of cond {c.ConditionRef} in {range}",
		};
	}

	/// <summary>
	/// The random table key a record conditioned on <paramref name="guid"/> answers to: the key of the type-3
	/// parent of the type-2 record <paramref name="guid"/> names, or null when it is not such a record
	/// (docs/retail/formats/msn-mission-file.md#random-tables).
	/// </summary>
	public static short? RandomTableKeyOf(MissionFile file, short guid) => RandomTableKeyOf(Of(file), guid);

	/// <inheritdoc cref="RandomTableKeyOf(MissionFile, short)"/>
	public static short? RandomTableKeyOf(Es1MissionFile file, short guid) => RandomTableKeyOf(Of(file), guid);

	private static short? RandomTableKeyOf(Table table, short guid) {
		var child = table.Records.Where(c => c.Guid == guid && c.Type == 2).Cast<Row1?>().FirstOrDefault();
		var parent = child == null ? null : table.Records.Where(p => p.Guid == child.Value.ConditionRef && p.Type == 3).Cast<Row1?>().FirstOrDefault();
		return parent?.FlagIndexOrRangeLower;
	}
}

/// <summary>Who writes a mission counter, which decides what an operation code means.</summary>
internal enum CounterLayer {
	/// <summary>A machine, flyer, structure or group going out of the fight (docs/retail/simulation/mission-deployment.md#the-out-of-action-report).</summary>
	OutOfAction,

	/// <summary><c>Action_Activate</c> (docs/retail/simulation/mission-deployment.md#the-four-ways-an-action-activates).</summary>
	Action,

	/// <summary>An objective (docs/retail/simulation/mission-objectives.md#the-record).</summary>
	Objective,

	/// <summary>Row #2's clear list, zeroed as the mission loads (docs/retail/formats/msn-mission-file.md#the-header-patch--row-2).</summary>
	LoadClear,
}

internal static class CounterOps {
	/// <summary>What operation <paramref name="op"/> does to a counter in <paramref name="layer"/>.</summary>
	public static string Describe(CounterLayer layer, short op) => layer switch {
		CounterLayer.OutOfAction => op switch {
			1 => "zero",
			2 => "add 1",
			>= 0x0d and <= 0x10 => $"store {op - 0x0c}",
			_ => $"op {op}: nothing",
		},
		CounterLayer.Action => op switch {
			5 => "zero",
			6 => "add 1",
			_ => $"op {op}: nothing",
		},
		CounterLayer.Objective => op switch {
			4 => "set",
			5 => "zero",
			6 => "add 1",
			7 => "subtract 1",
			_ => $"op {op}: nothing",
		},
		_ => "zero at load",
	};
}
