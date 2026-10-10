using HercWorks.Core.Data.File.Msn;

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

	/// <summary>A condition chain deeper than this is cut short; retail's are at most a few links.</summary>
	private const int MaxDepth = 8;

	/// <summary>The operator a type-0 code stands for, or null for a code the load fails.</summary>
	public static string? Operator(short code) =>
		code >= FirstOperator && code < FirstOperator + Operators.Length ? Operators[code - FirstOperator] : null;

	/// <summary>
	/// What condition ref <paramref name="guid"/> asks, or null for <c>-1</c>. Several row-1 records may
	/// share a GUID; the gate passes on any survivor that has it, so they are joined with <c>or</c>.
	/// </summary>
	public static string? Describe(MissionFile file, short guid) => guid == -1 ? null : Describe(file, guid, 0);

	private static string Describe(MissionFile file, short guid, int depth) {
		var records = (file.Conditions ?? []).Where(c => c.GUID == guid).ToList();
		if (records.Count == 0) {
			return $"cond {guid} (no row-1 record)";
		}

		if (depth >= MaxDepth) {
			return $"cond {guid}";
		}

		var parts = records.Select(r => DescribeOne(file, r, depth)).ToList();
		return parts.Count == 1 ? parts[0] : "(" + string.Join(") or (", parts) + ")";
	}

	/// <summary>What row-1 record <paramref name="c"/> itself asks, with the chain above it.</summary>
	public static string DescribeRecord(MissionFile file, MissionCondition14 c) => DescribeOne(file, c, 0);

	private static string DescribeOne(MissionFile file, MissionCondition14 c, int depth) {
		string own = c.Type switch {
			0 => Operator(c.OperatorOrRangeUpperOrResult) is { } op
				? $"flag {c.FlagIndexOrRangeLower} {op} {c.ComparisonOperand}"
				: $"flag {c.FlagIndexOrRangeLower} op 0x{(ushort)c.OperatorOrRangeUpperOrResult:x} (fails)",
			1 => $"draw {c.GUID} below {c.FlagIndexOrRangeLower}",
			2 => DescribeRange(file, c),
			3 => $"variant key {c.FlagIndexOrRangeLower}, drawn below {c.ComparisonOperand}",
			_ => $"type {c.Type} (never survives)",
		};

		// Type 2's condition field is its parent, which DescribeRange has already named.
		if (c.Type == 2 || c.ConditionRef == -1) {
			return own;
		}

		return $"{Describe(file, c.ConditionRef, depth + 1)} and {own}";
	}

	private static string DescribeRange(MissionFile file, MissionCondition14 c) {
		string range = $"{(ushort)c.FlagIndexOrRangeLower}-{(ushort)c.OperatorOrRangeUpperOrResult}";
		var parent = (file.Conditions ?? []).FirstOrDefault(p => p.GUID == c.ConditionRef && p.Type is 1 or 3);
		return parent?.Type switch {
			1 => $"draw {parent.GUID} in {range}",
			3 => $"variant {range} of key {parent.FlagIndexOrRangeLower}",
			_ => $"draw of cond {c.ConditionRef} in {range}",
		};
	}

	/// <summary>
	/// The variant key a record conditioned on <paramref name="guid"/> answers to: the key of the type-3
	/// parent of the type-2 record <paramref name="guid"/> names, or null when it is not such a record
	/// (docs/retail/formats/msn-mission-file.md#variants).
	/// </summary>
	public static short? VariantKeyOf(MissionFile file, short guid) {
		var child = (file.Conditions ?? []).FirstOrDefault(c => c.GUID == guid && c.Type == 2);
		var parent = child == null ? null : (file.Conditions ?? []).FirstOrDefault(p => p.GUID == child.ConditionRef && p.Type == 3);
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
